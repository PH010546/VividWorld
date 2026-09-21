using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using VividWorld.Core.Catalog;
using VividWorld.Core.Config;
using VividWorld.Core.Events;
using VividWorld.Core.Grudges;
using VividWorld.Core.Persistence;
using VividWorld.Core.Rumors;
using VividWorld.Core.Situations;
using VividWorld.Core.Tests.Fakes;
using VividWorld.Core.Util;
using Xunit;

namespace VividWorld.Core.Tests
{
    public class GrudgeTests
    {
        private static string FindRepoFile(string relativePath)
        {
            string? current = AppContext.BaseDirectory;
            while (!string.IsNullOrEmpty(current))
            {
                if (File.Exists(Path.Combine(current, "VividWorld.sln")))
                {
                    string path = Path.Combine(current, relativePath);
                    if (File.Exists(path)) return path;
                }
                var parent = Directory.GetParent(current);
                if (parent == null) break;
                current = parent.FullName;
            }
            throw new FileNotFoundException($"Could not find '{relativePath}' relative to {AppContext.BaseDirectory}");
        }

        #region 1. Serialization (5 tests)

        [Fact]
        public void RelationImpact_Roundtrip_PreservesAllFields()
        {
            var original = new RelationImpact
            {
                Scope = GrudgeScope.Clan,
                AboutHeroId = "lord_b",
                Requested = -8.5,
                Delta = -8,
                NativePair = new List<string> { "lord_a", "lord_b" },
                LedgerOnly = true,
                AppliedDay = 120.5,
                SourceFactId = "fact_1",
                EscalatedFrom = "lord_x|lord_y"
            };
            original.Extra["customKey"] = "customValue";

            string json = VividJson.Write(original);
            var restored = VividJson.Read<RelationImpact>(json);

            Assert.NotNull(restored);
            Assert.Equal(GrudgeScope.Clan, restored!.Scope);
            Assert.Equal("lord_b", restored.AboutHeroId);
            Assert.Equal(-8.5, restored.Requested);
            Assert.Equal(-8, restored.Delta);
            Assert.NotNull(restored.NativePair);
            Assert.Equal(new[] { "lord_a", "lord_b" }, restored.NativePair);
            Assert.True(restored.LedgerOnly);
            Assert.Equal(120.5, restored.AppliedDay);
            Assert.Equal("fact_1", restored.SourceFactId);
            Assert.Equal("lord_x|lord_y", restored.EscalatedFrom);
            Assert.Equal("customValue", restored.Extra["customKey"]?.ToString());
        }

        [Fact]
        public void Scope_SerializesAsString()
        {
            var personal = new RelationImpact { Scope = GrudgeScope.Personal };
            string jsonPersonal = VividJson.Write(personal);
            Assert.Contains("\"scope\": \"personal\"", jsonPersonal);

            var clan = new RelationImpact { Scope = GrudgeScope.Clan };
            string jsonClan = VividJson.Write(clan);
            Assert.Contains("\"scope\": \"clan\"", jsonClan);
        }

        [Fact]
        public void RelationImpact_NativePair_NullValueIgnored()
        {
            var impact = new RelationImpact
            {
                Scope = GrudgeScope.Personal,
                AboutHeroId = "lord_1",
                NativePair = null
            };
            string json = VividJson.Write(impact);
            Assert.DoesNotContain("\"nativePair\"", json, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void Shard_WithCorruptOrOldObjectRelationImpacts_DoesNotDropWholeShard()
        {
            // Shard JSON with 2 events: one has malformed relationImpacts, one is clean
            string shardJson = @"[
              {
                ""eventId"": ""evt_corrupt"",
                ""type"": ""some_event"",
                ""origin"": ""public"",
                ""dramaWeight"": 1,
                ""day"": 10.0,
                ""facts"": [],
                ""state"": { ""dormant"": false },
                ""knownBy"": [
                  {
                    ""heroId"": ""lord_1"",
                    ""hop"": 0,
                    ""relationImpacts"": ""not-an-array-or-object""
                  }
                ]
              },
              {
                ""eventId"": ""evt_valid"",
                ""type"": ""some_event"",
                ""origin"": ""public"",
                ""dramaWeight"": 1,
                ""day"": 10.0,
                ""facts"": [],
                ""state"": { ""dormant"": false },
                ""knownBy"": [
                  {
                    ""heroId"": ""lord_2"",
                    ""hop"": 0
                  }
                ]
              }
            ]";

            var fileWriter = new FailingFileWriter();
            fileWriter.WriteAllText(@"C:\fake\d0000-0049.json", shardJson);
            var store = new EventShardStore(@"C:\fake", 50, fileWriter);

            var loaded = store.Load("evt_valid", null);
            Assert.NotNull(loaded);
            Assert.Equal("evt_valid", loaded!.EventId);
        }

        [Fact]
        public void KnownByEntry_RelationImpacts_ListRoundtrip()
        {
            var entry = new KnownByEntry
            {
                HeroId = "hero_1",
                Hop = 0,
                RelationImpacts = new List<RelationImpact>
                {
                    new() { Scope = GrudgeScope.Personal, AboutHeroId = "hero_2", Requested = -5.0 },
                    new() { Scope = GrudgeScope.Clan, AboutHeroId = "leader_2", Requested = -2.5 }
                }
            };

            string json = VividJson.Write(entry);
            var restored = VividJson.Read<KnownByEntry>(json);

            Assert.NotNull(restored);
            Assert.NotNull(restored!.RelationImpacts);
            Assert.Equal(2, restored.RelationImpacts!.Count);
            Assert.Equal(GrudgeScope.Personal, restored.RelationImpacts[0].Scope);
            Assert.Equal(GrudgeScope.Clan, restored.RelationImpacts[1].Scope);
        }

        #endregion

        #region 2. Declaration Validation (9 tests)

        private const string BaseValidSituationJson = @"{
  ""situations"": [
    {
      ""id"": ""test_sit"",
      ""trigger"": ""direct"",
      ""decider"": ""slighted"",
      ""roles"": {
        ""slighted"": { },
        ""favored"": { }
      },
      ""conditions"": [],
      ""branches"": [
        {
          ""id"": ""b1"",
          ""base"": 1.0,
          ""events"": [
            { ""type"": ""e1"", ""bind"": { ""SLIGHTED"": ""slighted"" } }
          ],
          ""grudges"": [
            { ""from"": ""slighted"", ""to"": ""favored"", ""amount"": -8.0, ""ledgerOnly"": true, ""escalate"": ""clan"" }
          ]
        }
      ]
    }
  ]
}";

        [Fact]
        public void CatalogLoader_ValidGrudges_NoIssues()
        {
            var catalog = SituationCatalogLoader.Load(BaseValidSituationJson);
            Assert.Empty(catalog.Issues);
            Assert.Single(catalog.Situations);
            var branch = catalog.Situations[0].Branches[0];
            Assert.NotNull(branch.Grudges);
            Assert.Single(branch.Grudges!);
            Assert.Equal("slighted", branch.Grudges![0].From);
            Assert.Equal("favored", branch.Grudges[0].To);
            Assert.Equal(-8.0, branch.Grudges[0].Amount);
            Assert.True(branch.Grudges[0].LedgerOnly);
            Assert.Equal("clan", branch.Grudges[0].Escalate);
        }

        [Fact]
        public void CatalogLoader_GrudgeFrom_NotInRoles_ReportsUnknownRoleInGrudge()
        {
            string json = BaseValidSituationJson.Replace("\"from\": \"slighted\"", "\"from\": \"stranger\"");
            var catalog = SituationCatalogLoader.Load(json);
            Assert.Contains(catalog.Issues, i => i.Code == SituationIssueCode.UnknownRoleInGrudge && i.Field.EndsWith(".from"));
        }

        [Fact]
        public void CatalogLoader_GrudgeTo_NotInRoles_ReportsUnknownRoleInGrudge()
        {
            string json = BaseValidSituationJson.Replace("\"to\": \"favored\"", "\"to\": \"stranger\"");
            var catalog = SituationCatalogLoader.Load(json);
            Assert.Contains(catalog.Issues, i => i.Code == SituationIssueCode.UnknownRoleInGrudge && i.Field.EndsWith(".to"));
        }

        [Fact]
        public void CatalogLoader_GrudgeFromEqualsTo_ReportsGrudgeFromEqualsTo()
        {
            string json = BaseValidSituationJson.Replace("\"to\": \"favored\"", "\"to\": \"slighted\"");
            var catalog = SituationCatalogLoader.Load(json);
            Assert.Contains(catalog.Issues, i => i.Code == SituationIssueCode.GrudgeFromEqualsTo);
        }

        [Fact]
        public void CatalogLoader_GrudgeAmountZero_ReportsInvalidGrudgeAmount()
        {
            string json = BaseValidSituationJson.Replace("\"amount\": -8.0", "\"amount\": 0.0");
            var catalog = SituationCatalogLoader.Load(json);
            Assert.Contains(catalog.Issues, i => i.Code == SituationIssueCode.InvalidGrudgeAmount);
        }

        [Fact]
        public void CatalogLoader_GrudgeAmountNonFinite_ReportsInvalidGrudgeAmount()
        {
            string json = BaseValidSituationJson.Replace("\"amount\": -8.0", "\"amount\": \"not-a-number\"");
            var catalog = SituationCatalogLoader.Load(json);
            Assert.Contains(catalog.Issues, i => i.Code == SituationIssueCode.InvalidGrudgeAmount);
        }

        [Fact]
        public void CatalogLoader_GrudgeEscalateInvalid_ReportsInvalidGrudgeEscalate()
        {
            string json = BaseValidSituationJson.Replace("\"escalate\": \"clan\"", "\"escalate\": \"kingdom\"");
            var catalog = SituationCatalogLoader.Load(json);
            Assert.Contains(catalog.Issues, i => i.Code == SituationIssueCode.InvalidGrudgeEscalate);
        }

        [Fact]
        public void CatalogLoader_GrudgeMissingFrom_ReportsMissingGrudgeProperty()
        {
            string json = BaseValidSituationJson.Replace("\"from\": \"slighted\",", "");
            var catalog = SituationCatalogLoader.Load(json);
            Assert.Contains(catalog.Issues, i => i.Code == SituationIssueCode.MissingGrudgeProperty && i.Field.EndsWith(".from"));
        }

        [Fact]
        public void CatalogLoader_GrudgeUnknownProperty_EmitsWarningButLoads()
        {
            string json = BaseValidSituationJson.Replace("\"escalate\": \"clan\"", "\"escalate\": \"clan\", \"futureField\": 123");
            var catalog = SituationCatalogLoader.Load(json);
            Assert.Contains(catalog.Issues, i => i.Code == SituationIssueCode.UnknownProperty && !i.IsError);
            Assert.Single(catalog.Situations);
            var g = catalog.Situations[0].Branches[0].Grudges![0];
            Assert.Equal("123", g.Extra["futureField"]?.ToString());
        }

        #endregion

        #region 3. Cross Check (4 tests)

        private static (SituationCatalog Situations, EventCatalog Events) SetupCrossCheckCatalog(
            EventOrigin origin,
            HashSet<string>? knowingRoles,
            string bindPlaceholder,
            string bindRole)
        {
            var eventTemplate = new EventTemplate
            {
                Type = "test_event",
                Origin = origin,
                DramaWeight = 1,
                Roles = new Dictionary<string, string> { ["template_role"] = "{PH}" },
                KnowingRoles = knowingRoles ?? new HashSet<string>()
            };
            var eventCatalog = new EventCatalog(new[] { eventTemplate }, Array.Empty<CatalogIssue>(), 0);

            var situation = new SituationTemplate
            {
                Id = "sit_1",
                Trigger = "direct",
                Decider = "slighted",
                Roles = new Dictionary<string, SituationRoleDef>
                {
                    ["slighted"] = new(),
                    ["favored"] = new()
                },
                Branches = new List<SituationBranchDef>
                {
                    new()
                    {
                        Id = "b1",
                        Base = 1.0,
                        Events = new List<SituationBranchEventDef>
                        {
                            new()
                            {
                                Type = "test_event",
                                Bind = new Dictionary<string, string> { [bindPlaceholder] = bindRole }
                            }
                        },
                        Grudges = new List<SituationGrudgeDef>
                        {
                            new() { From = "slighted", To = "favored", Amount = -5.0 }
                        }
                    }
                }
            };
            var situationCatalog = new SituationCatalog(new[] { situation }, Array.Empty<SituationIssue>(), 0);

            return (situationCatalog, eventCatalog);
        }

        [Fact]
        public void CrossCheck_GrudgeFromIsHop0InSecretEvent_Passes()
        {
            var (sitCat, evtCat) = SetupCrossCheckCatalog(
                EventOrigin.Secret,
                new HashSet<string> { "template_role" },
                "PH",
                "slighted");

            var issues = SituationCatalogCrossCheck.Check(sitCat, evtCat, null!);
            Assert.DoesNotContain(issues, i => i.Code == SituationIssueCode.GrudgeFromNotHop0Knower);
        }

        [Fact]
        public void CrossCheck_GrudgeFromIsNotHop0InSecretEvent_ReportsGrudgeFromNotHop0Knower()
        {
            var (sitCat, evtCat) = SetupCrossCheckCatalog(
                EventOrigin.Secret,
                new HashSet<string> { "other_role" }, // template_role is NOT knowing
                "PH",
                "slighted");

            var issues = SituationCatalogCrossCheck.Check(sitCat, evtCat, null!);
            Assert.Contains(issues, i => i.Code == SituationIssueCode.GrudgeFromNotHop0Knower);
        }

        [Fact]
        public void CrossCheck_GrudgeFromInPublicEvent_Passes()
        {
            var (sitCat, evtCat) = SetupCrossCheckCatalog(
                EventOrigin.Public,
                new HashSet<string>(), // public => empty knowingRoles => all roles are hop 0
                "PH",
                "slighted");

            var issues = SituationCatalogCrossCheck.Check(sitCat, evtCat, null!);
            Assert.DoesNotContain(issues, i => i.Code == SituationIssueCode.GrudgeFromNotHop0Knower);
        }

        [Fact]
        public void CrossCheck_GrudgeFromNotBoundInAnyEvent_ReportsGrudgeFromNotHop0Knower()
        {
            var (sitCat, evtCat) = SetupCrossCheckCatalog(
                EventOrigin.Public,
                new HashSet<string>(),
                "PH",
                "favored"); // slighted is not bound at all!

            var issues = SituationCatalogCrossCheck.Check(sitCat, evtCat, null!);
            Assert.Contains(issues, i => i.Code == SituationIssueCode.GrudgeFromNotHop0Knower);
        }

        #endregion

        #region 4. Decay Replay (13 tests)

        [Fact]
        public void Replay_SingleEntryInsideBand_NoDecay()
        {
            var cfg = new SituationsConfig();
            cfg.GrudgeDecay.Personal.Band = 10.0;
            cfg.GrudgeDecay.Personal.DaysPerPoint = 3.0;

            var entries = new List<GrudgeEntry>
            {
                new() { Day = 10.0, Requested = -6.0, EventId = "evt_1", Scope = GrudgeScope.Personal }
            };

            var res = GrudgeDecay.Replay(entries, GrudgeScope.Personal, cfg, null, 20.0);
            Assert.Equal(-6.0, res.Value);
            Assert.Single(res.Steps);
            Assert.True(res.Steps[0].InsideBand);
            Assert.Equal(0.0, res.Steps[0].PointsDecayed);
        }

        [Fact]
        public void Replay_SingleEntryOutsideBand_DecaysTowardBand()
        {
            var cfg = new SituationsConfig();
            cfg.GrudgeDecay.Personal.Band = 10.0;
            cfg.GrudgeDecay.Personal.DaysPerPoint = 3.0;

            var entries = new List<GrudgeEntry>
            {
                new() { Day = 10.0, Requested = -16.0, EventId = "evt_1", Scope = GrudgeScope.Personal }
            };

            // 3 days elapsed -> 1.0 point decayed toward band
            var res = GrudgeDecay.Replay(entries, GrudgeScope.Personal, cfg, null, 13.0);
            Assert.Equal(-15.0, res.Value);
            Assert.False(res.Steps[0].InsideBand);
            Assert.False(res.Steps[0].HitBand);
            Assert.Equal(1.0, res.Steps[0].PointsDecayed);
        }

        [Fact]
        public void Replay_DecayStopsAtBand_DoesNotOvershootOrFlipSign()
        {
            var cfg = new SituationsConfig();
            cfg.GrudgeDecay.Personal.Band = 10.0;
            cfg.GrudgeDecay.Personal.DaysPerPoint = 3.0;

            var entries = new List<GrudgeEntry>
            {
                new() { Day = 10.0, Requested = -12.0, EventId = "evt_1", Scope = GrudgeScope.Personal }
            };

            // 90 days elapsed -> 30 potential points, but distance to band is only 2.0
            var res = GrudgeDecay.Replay(entries, GrudgeScope.Personal, cfg, null, 100.0);
            Assert.Equal(-10.0, res.Value);
            Assert.True(res.Steps[0].HitBand);
            Assert.Equal(2.0, res.Steps[0].PointsDecayed);
        }

        [Fact]
        public void Replay_MultiEntry_CalculatedAccurately()
        {
            var cfg = new SituationsConfig();
            cfg.GrudgeDecay.Personal.Band = 10.0;
            cfg.GrudgeDecay.Personal.DaysPerPoint = 3.0;

            var entries = new List<GrudgeEntry>
            {
                new() { Day = 10.0, Requested = -15.0, EventId = "evt_1", Scope = GrudgeScope.Personal },
                new() { Day = 13.0, Requested = -6.0, EventId = "evt_2", Scope = GrudgeScope.Personal }
            };

            // Day 10 to 13: 3 days = 1.0 point decayed -> -14.0. Then + (-6.0) = -20.0.
            // Day 13 to 16: 3 days = 1.0 point decayed -> -19.0.
            var res = GrudgeDecay.Replay(entries, GrudgeScope.Personal, cfg, null, 16.0);
            Assert.Equal(-19.0, res.Value);
            Assert.Equal(2, res.Steps.Count);
            Assert.Equal(-20.0, res.Steps[0].ValueAfter);
            Assert.Equal(-19.0, res.Steps[1].ValueAfter);
        }

        [Fact]
        public void Replay_SameDayEntries_OrderIsStable()
        {
            var cfg = new SituationsConfig();
            var entries = new List<GrudgeEntry>
            {
                new() { Day = 10.0, Requested = -5.0, EventId = "evt_b", Scope = GrudgeScope.Personal },
                new() { Day = 10.0, Requested = -3.0, EventId = "evt_a", Scope = GrudgeScope.Personal }
            };

            var res = GrudgeDecay.Replay(entries, GrudgeScope.Personal, cfg, null, 10.0);
            // evt_a comes first, then evt_b
            Assert.Equal(2, res.Steps.Count);
            Assert.Equal("evt_a", res.Steps[0].EventId);
            Assert.Equal("evt_b", res.Steps[1].EventId);
            Assert.Equal(-8.0, res.Value);
        }

        [Fact]
        public void Replay_TodayEarlierThanLastEntry_NoReverseDecay()
        {
            // 「今天比那一筆早、但還在容差之內」：這一筆算數，只是不准反向淡化。
            // 容差是 0.5 天（EventVisibility.ToleranceDays），所以 9.7 這個今天看得見第 10 天那一筆。
            var cfg = new SituationsConfig();
            var entries = new List<GrudgeEntry>
            {
                new() { Day = 10.0, Requested = -8.0, EventId = "evt_1", Scope = GrudgeScope.Personal }
            };

            var res = GrudgeDecay.Replay(entries, GrudgeScope.Personal, cfg, null, 9.7);
            Assert.Equal(-8.0, res.Value);
            Assert.Equal(0.0, res.Steps[0].DaysElapsed);
            Assert.Equal(0, res.HiddenFutureCount);
        }

        [Fact]
        public void Replay_EntryDatedWellAfterToday_IsNotCountedAtAll()
        {
            // 超過容差 ⇒ 那一筆來自一條被抹掉的時間線，整筆不算（規格 §2.2.1）。
            // 這與上面那條的差別只有「今天是哪一天」：9.7 算，8.0 不算。
            var cfg = new SituationsConfig();
            var entries = new List<GrudgeEntry>
            {
                new() { Day = 10.0, Requested = -8.0, EventId = "evt_1", Scope = GrudgeScope.Personal }
            };

            var res = GrudgeDecay.Replay(entries, GrudgeScope.Personal, cfg, null, 8.0);
            Assert.Equal(0.0, res.Value);
            Assert.Equal(0, res.EntryCount);
            Assert.Equal(1, res.HiddenFutureCount);
            Assert.Empty(res.Steps);
        }

        [Fact]
        public void Replay_PositiveValue_UsesHonorForGratitude()
        {
            var cfg = new SituationsConfig();
            cfg.GrudgeDecay.Personal.Band = 5.0;
            cfg.GrudgeDecay.TraitWeights.HonorForGratitude = 0.25;

            var profile = new TraitProfile { Honor = 2, Mercy = 0, Valor = 0, Calculating = 0, Generosity = 0 };
            var entries = new List<GrudgeEntry>
            {
                new() { Day = 10.0, Requested = 20.0, EventId = "evt_1", Scope = GrudgeScope.Personal }
            };

            var res = GrudgeDecay.Replay(entries, GrudgeScope.Personal, cfg, profile, 13.0);
            // 1 + 2 * 0.25 = 1.50
            Assert.Equal(1.50, res.Steps[0].Multiplier);
            Assert.Contains("honorForGratitude", res.Steps[0].MultiplierDetail);
        }

        [Fact]
        public void Replay_NegativeValue_UsesMercyAndCalculating()
        {
            var cfg = new SituationsConfig();
            cfg.GrudgeDecay.Personal.Band = 5.0;
            cfg.GrudgeDecay.TraitWeights.Mercy = 0.25;
            cfg.GrudgeDecay.TraitWeights.Calculating = -0.15;

            var profile = new TraitProfile { Honor = 2, Mercy = 1, Valor = 0, Calculating = 2, Generosity = 0 };
            var entries = new List<GrudgeEntry>
            {
                new() { Day = 10.0, Requested = -20.0, EventId = "evt_1", Scope = GrudgeScope.Personal }
            };

            var res = GrudgeDecay.Replay(entries, GrudgeScope.Personal, cfg, profile, 13.0);
            // 1 + 1*0.25 + 2*(-0.15) = 0.95
            Assert.Equal(0.95, res.Steps[0].Multiplier);
            Assert.Contains("mercy", res.Steps[0].MultiplierDetail);
            Assert.Contains("calculating", res.Steps[0].MultiplierDetail);
            Assert.DoesNotContain("honor", res.Steps[0].MultiplierDetail);
        }

        [Fact]
        public void Replay_SignFlip_SwitchesTraitWeightsForNextSegment()
        {
            var cfg = new SituationsConfig();
            cfg.GrudgeDecay.Personal.Band = 5.0;
            cfg.GrudgeDecay.Personal.DaysPerPoint = 3.0;
            cfg.GrudgeDecay.TraitWeights.Mercy = 0.20;
            cfg.GrudgeDecay.TraitWeights.HonorForGratitude = 0.30;

            var profile = new TraitProfile { Honor = 1, Mercy = 1, Valor = 0, Calculating = 0, Generosity = 0 };
            var entries = new List<GrudgeEntry>
            {
                new() { Day = 10.0, Requested = -20.0, EventId = "evt_1", Scope = GrudgeScope.Personal },
                new() { Day = 13.0, Requested = 40.0, EventId = "evt_2", Scope = GrudgeScope.Personal }
            };

            // Segment 1 (10->13): negative -> mercy used
            // Segment 2 (13->16): positive -> honorForGratitude used
            var res = GrudgeDecay.Replay(entries, GrudgeScope.Personal, cfg, profile, 16.0);
            Assert.Equal(2, res.Steps.Count);
            Assert.Contains("mercy", res.Steps[0].MultiplierDetail);
            Assert.Contains("honorForGratitude", res.Steps[1].MultiplierDetail);
        }

        [Fact]
        public void Replay_MultiplierClampedToMin()
        {
            var cfg = new SituationsConfig();
            cfg.GrudgeDecay.MultiplierMin = 0.5;
            cfg.GrudgeDecay.TraitWeights.Calculating = -1.0; // severe negative weight

            var profile = new TraitProfile { Honor = 0, Mercy = 0, Valor = 0, Calculating = 2, Generosity = 0 };
            var entries = new List<GrudgeEntry>
            {
                new() { Day = 10.0, Requested = -20.0, EventId = "evt_1", Scope = GrudgeScope.Personal }
            };

            var res = GrudgeDecay.Replay(entries, GrudgeScope.Personal, cfg, profile, 13.0);
            Assert.Equal(0.5, res.Steps[0].Multiplier);
        }

        [Fact]
        public void Replay_MultiplierClampedToMax()
        {
            var cfg = new SituationsConfig();
            cfg.GrudgeDecay.MultiplierMax = 2.0;
            cfg.GrudgeDecay.TraitWeights.Mercy = 1.0;

            var profile = new TraitProfile { Honor = 0, Mercy = 2, Valor = 0, Calculating = 0, Generosity = 0 };
            var entries = new List<GrudgeEntry>
            {
                new() { Day = 10.0, Requested = -20.0, EventId = "evt_1", Scope = GrudgeScope.Personal }
            };

            var res = GrudgeDecay.Replay(entries, GrudgeScope.Personal, cfg, profile, 13.0);
            Assert.Equal(2.0, res.Steps[0].Multiplier);
        }

        [Fact]
        public void Replay_HolderNull_UsesDefaultMultiplier()
        {
            var cfg = new SituationsConfig();
            var entries = new List<GrudgeEntry>
            {
                new() { Day = 10.0, Requested = -20.0, EventId = "evt_1", Scope = GrudgeScope.Personal }
            };

            var res = GrudgeDecay.Replay(entries, GrudgeScope.Personal, cfg, null, 13.0);
            Assert.Equal(1.0, res.Steps[0].Multiplier);
            Assert.Equal("no trait profile", res.Steps[0].MultiplierDetail);
        }

        [Fact]
        public void Replay_EmptyEntries_ReturnsZero()
        {
            var cfg = new SituationsConfig();
            var res = GrudgeDecay.Replay(Array.Empty<GrudgeEntry>(), GrudgeScope.Personal, cfg, null, 13.0);
            Assert.Equal(0.0, res.Value);
            Assert.Empty(res.Steps);
        }

        #endregion

        #region 5. Escalation Decision (10 tests)

        private static EscalationFacts CreateValidEscalationFacts()
        {
            return new EscalationFacts
            {
                AHeroId = "lord_a",
                BHeroId = "lord_b",
                AClanId = "clan_a",
                BClanId = "clan_b",
                ALeaderId = "leader_a",
                BLeaderId = "leader_b",
                AIsClanLeader = true,
                AIsKinOfOwnLeader = false,
                AKinRelation = null
            };
        }

        [Fact]
        public void Escalation_AlreadyEscalated_DoesNotEscalate()
        {
            var facts = CreateValidEscalationFacts();
            var cfg = new SituationsConfig();

            var dec = GrudgeEscalation.Decide(facts, -8.0, -35.0, false, true, "evt_prev", 100.5, cfg);
            Assert.False(dec.Escalate);
            Assert.Contains("this pair already escalated", dec.Reason);
            Assert.Contains("evt_prev", dec.Reason);
            Assert.Contains("100.5", dec.Reason);
        }

        [Fact]
        public void Escalation_MissingAClanId_DoesNotEscalate()
        {
            var facts = CreateValidEscalationFacts();
            facts.AClanId = null;
            var cfg = new SituationsConfig();

            var dec = GrudgeEscalation.Decide(facts, -8.0, -35.0, false, false, null, 0.0, cfg);
            Assert.False(dec.Escalate);
            Assert.Contains("lord_a has no clan", dec.Reason);
        }

        [Fact]
        public void Escalation_SameClan_DoesNotEscalate()
        {
            var facts = CreateValidEscalationFacts();
            facts.BClanId = facts.AClanId;
            var cfg = new SituationsConfig();

            var dec = GrudgeEscalation.Decide(facts, -8.0, -35.0, false, false, null, 0.0, cfg);
            Assert.False(dec.Escalate);
            Assert.Contains("both in clan_a", dec.Reason);
        }

        [Fact]
        public void Escalation_MissingLeaderId_DoesNotEscalate()
        {
            var facts = CreateValidEscalationFacts();
            facts.ALeaderId = null;
            var cfg = new SituationsConfig();

            var dec = GrudgeEscalation.Decide(facts, -8.0, -35.0, false, false, null, 0.0, cfg);
            Assert.False(dec.Escalate);
            Assert.Contains("clan_a has no leader", dec.Reason);
        }

        [Fact]
        public void Escalation_SameLeaderId_DoesNotEscalate()
        {
            var facts = CreateValidEscalationFacts();
            facts.BLeaderId = facts.ALeaderId;
            var cfg = new SituationsConfig();

            var dec = GrudgeEscalation.Decide(facts, -8.0, -35.0, false, false, null, 0.0, cfg);
            Assert.False(dec.Escalate);
            Assert.Contains("share the same leader", dec.Reason);
        }

        [Fact]
        public void Escalation_NotLeaderNorKin_DoesNotEscalate()
        {
            var facts = CreateValidEscalationFacts();
            facts.AIsClanLeader = false;
            facts.AIsKinOfOwnLeader = false;
            var cfg = new SituationsConfig();

            var dec = GrudgeEscalation.Decide(facts, -8.0, -35.0, false, false, null, 0.0, cfg);
            Assert.False(dec.Escalate);
            Assert.Contains("neither clan leader nor kin", dec.Reason);
        }

        [Fact]
        public void Escalation_BelowThreshold_DoesNotEscalate()
        {
            var facts = CreateValidEscalationFacts();
            var cfg = new SituationsConfig(); // default threshold is 30.0

            var dec = GrudgeEscalation.Decide(facts, -8.0, -12.3, false, false, null, 0.0, cfg);
            Assert.False(dec.Escalate);
            Assert.Contains("personal -12.3 < threshold 30", dec.Reason);
        }

        [Fact]
        public void Escalation_BranchDeclaresClan_SkipsLeaderAndThresholdCheck()
        {
            var facts = CreateValidEscalationFacts();
            facts.AIsClanLeader = false;
            facts.AIsKinOfOwnLeader = false;
            var cfg = new SituationsConfig();

            // Below threshold (-12.3) and not leader, but branchDeclaresClan = true
            var dec = GrudgeEscalation.Decide(facts, -8.0, -12.3, true, false, null, 0.0, cfg);
            Assert.True(dec.Escalate);
            Assert.Contains("branch declares escalate=clan", dec.Reason);
        }

        [Fact]
        public void Escalation_BranchDeclaresClan_StillBlockedBySameClan()
        {
            var facts = CreateValidEscalationFacts();
            facts.BClanId = facts.AClanId;
            var cfg = new SituationsConfig();

            var dec = GrudgeEscalation.Decide(facts, -8.0, -35.0, true, false, null, 0.0, cfg);
            Assert.False(dec.Escalate);
            Assert.Contains("both in clan_a", dec.Reason);
        }

        [Fact]
        public void Escalation_Amount_MultipliedByFactor()
        {
            var facts = CreateValidEscalationFacts();
            var cfg = new SituationsConfig();
            cfg.ClanEscalationFactor = 0.5;

            var dec = GrudgeEscalation.Decide(facts, -8.0, -35.0, false, false, null, 0.0, cfg);
            Assert.True(dec.Escalate);
            Assert.Equal(-4.0, dec.Amount);
        }

        #endregion

        #region 6. GrudgeIndex (5 tests)

        [Fact]
        public void GrudgeIndex_Note_BetweenOrdersByDayThenEventId()
        {
            var index = new GrudgeIndex();
            index.Note(new GrudgeEntry { FromHeroId = "a", AboutHeroId = "b", Scope = GrudgeScope.Personal, Day = 20.0, EventId = "evt_2" });
            index.Note(new GrudgeEntry { FromHeroId = "a", AboutHeroId = "b", Scope = GrudgeScope.Personal, Day = 10.0, EventId = "evt_1" });
            index.Note(new GrudgeEntry { FromHeroId = "a", AboutHeroId = "b", Scope = GrudgeScope.Personal, Day = 20.0, EventId = "evt_1" });

            var list = index.Between("a", "b", GrudgeScope.Personal);
            Assert.Equal(3, list.Count);
            Assert.Equal("evt_1", list[0].EventId);
            Assert.Equal(10.0, list[0].Day);
            Assert.Equal("evt_1", list[1].EventId);
            Assert.Equal(20.0, list[1].Day);
            Assert.Equal("evt_2", list[2].EventId);
            Assert.Equal(20.0, list[2].Day);
        }

        [Fact]
        public void GrudgeIndex_ClanScope_FromHeroIdIsLeader()
        {
            var index = new GrudgeIndex();
            index.Note(new GrudgeEntry { FromHeroId = "leader_a", AboutHeroId = "leader_b", Scope = GrudgeScope.Clan, Day = 10.0, EventId = "evt_c" });

            Assert.Equal(1, index.PairCount(GrudgeScope.Clan));
            var pairs = index.Pairs;
            Assert.Contains(pairs, p => p.From == "leader_a" && p.About == "leader_b" && p.Scope == GrudgeScope.Clan);
        }

        [Fact]
        public void GrudgeIndex_TryGetEscalation_DirectionSensitive()
        {
            var index = new GrudgeIndex();
            index.Note(new GrudgeEntry
            {
                FromHeroId = "leader_a",
                AboutHeroId = "leader_b",
                Scope = GrudgeScope.Clan,
                Day = 15.0,
                EventId = "evt_esc",
                EscalatedFrom = "a|b"
            });

            Assert.True(index.TryGetEscalation("a", "b", out string eId, out double day));
            Assert.Equal("evt_esc", eId);
            Assert.Equal(15.0, day);

            Assert.False(index.TryGetEscalation("b", "a", out _, out _));
        }

        [Fact]
        public void GrudgeIndex_RebuildFrom_OnlyReadsEventsWithHasGrudges()
        {
            var rumorIndex = new RumorIndex();
            for (int i = 1; i <= 10; i++)
            {
                rumorIndex.Upsert(new RumorIndexEntry
                {
                    EventId = $"evt_{i}",
                    HasGrudges = (i <= 2) // only first 2 have grudges
                });
            }

            int loadCount = 0;
            var rebuilt = GrudgeIndex.RebuildFrom(rumorIndex, id =>
            {
                loadCount++;
                return new WorldEvent
                {
                    EventId = id,
                    Day = 10.0,
                    KnownBy = new List<KnownByEntry>
                    {
                        new()
                        {
                            HeroId = "h_from",
                            RelationImpacts = new List<RelationImpact>
                            {
                                new() { Scope = GrudgeScope.Personal, AboutHeroId = "h_to", Requested = -5.0 }
                            }
                        }
                    }
                };
            }, 100.0, out int read, out int skipped, out int hiddenFuture);

            Assert.Equal(2, loadCount);
            Assert.Equal(2, read);
            Assert.Equal(0, skipped);
            Assert.Equal(0, hiddenFuture);
            Assert.Single(rebuilt.Pairs);
        }

        [Fact]
        public void GrudgeIndex_RebuildFrom_LoadReturnsNull_IncrementsSkipped()
        {
            var rumorIndex = new RumorIndex();
            rumorIndex.Upsert(new RumorIndexEntry { EventId = "evt_missing", HasGrudges = true });

            var rebuilt = GrudgeIndex.RebuildFrom(rumorIndex, _ => null, 100.0, out int read, out int skipped, out int hiddenFuture);
            Assert.Equal(0, read);
            Assert.Equal(1, skipped);
            Assert.Equal(0, hiddenFuture);
            Assert.Empty(rebuilt.Pairs);
        }

        #endregion

        #region 7. HasGrudges Flag (2 tests)

        [Fact]
        public void HasGrudges_SetCorrectlyInRumorIndexEntryFrom()
        {
            var evtWithGrudges = new WorldEvent
            {
                EventId = "evt_g",
                KnownBy = new List<KnownByEntry>
                {
                    new()
                    {
                        HeroId = "h1",
                        RelationImpacts = new List<RelationImpact> { new() { Requested = -5.0 } }
                    }
                }
            };
            var entryWith = RumorIndexEntry.From(evtWithGrudges, 50);
            Assert.True(entryWith.HasGrudges);

            var evtWithout = new WorldEvent
            {
                EventId = "evt_nog",
                KnownBy = new List<KnownByEntry> { new() { HeroId = "h1" } }
            };
            var entryWithout = RumorIndexEntry.From(evtWithout, 50);
            Assert.False(entryWithout.HasGrudges);
        }

        [Fact]
        public void HasGrudges_PreservedWhenRebuildingIndexFromShards()
        {
            var fileWriter = new FailingFileWriter();
            var store = new EventShardStore(@"C:\test_shards", 50, fileWriter);

            var evt = new WorldEvent
            {
                EventId = "evt_save",
                Day = 10.0,
                Type = "test_type",
                Origin = EventOrigin.Public,
                State = new RumorState(),
                KnownBy = new List<KnownByEntry>
                {
                    new()
                    {
                        HeroId = "h1",
                        RelationImpacts = new List<RelationImpact> { new() { Requested = -4.0 } }
                    }
                }
            };

            store.Upsert(evt);
            store.Flush();

            // 刪除 _index.json，模擬索引遺失由分片重建
            var indexKeys = fileWriter.Files.Keys.Where(k => k.EndsWith("_index.json", StringComparison.OrdinalIgnoreCase)).ToList();
            foreach (var k in indexKeys) fileWriter.Files.Remove(k);

            var newStore = new EventShardStore(@"C:\test_shards", 50, fileWriter);
            var index = newStore.LoadIndex();
            var entry = index.Find("evt_save");
            Assert.NotNull(entry);
            Assert.True(entry!.HasGrudges);
        }

        #endregion

        #region 8. Log Formatting (6 tests)

        [Fact]
        public void LogFormatter_FormatAppliedNative_MatchesExactString()
        {
            string line = GrudgeLogFormatter.FormatAppliedNative(
                "slighted", "lord_4_15",
                "favored", "lord_1_22",
                -8.0, -8, 12, 4);

            Assert.Equal("grudge slighted lord_4_15 -> favored lord_1_22: requested -8.0 personal, native -8 (base 12 -> 4)", line);
        }

        [Fact]
        public void LogFormatter_FormatAppliedLedgerOnly_MatchesExactString()
        {
            string line = GrudgeLogFormatter.FormatAppliedLedgerOnly(
                "slighted", "lord_4_15",
                "favored", "lord_1_22",
                -6.0);

            Assert.Equal("grudge slighted lord_4_15 -> favored lord_1_22: requested -6.0 personal, ledger-only (native untouched)", line);
        }

        [Fact]
        public void LogFormatter_FormatSkippedNotHop0_MatchesExactString()
        {
            string line = GrudgeLogFormatter.FormatSkippedNotHop0(
                "favored", "lord_1_22",
                "slighted", "lord_4_15",
                "evt_91300_ab12");

            Assert.Equal("grudge skipped favored lord_1_22 -> slighted lord_4_15: not a hop 0 knower of evt_91300_ab12", line);
        }

        [Fact]
        public void LogFormatter_FormatEscalationEscalatedNative_MatchesExactString()
        {
            string line = GrudgeLogFormatter.FormatEscalationEscalatedNative(
                "lord_4_15", "lord_1_22",
                "branch declares escalate=clan",
                "lord_4_15", "lord_1_1",
                -4.0, -4, 0, -4);

            Assert.Equal("escalation lord_4_15 -> lord_1_22: escalated - branch declares escalate=clan -> clan lord_4_15 <-> lord_1_1, requested -4.0, native -4 (base 0 -> -4)", line);
        }

        [Fact]
        public void LogFormatter_FormatEscalationNotEscalated_MatchesExactString()
        {
            string line = GrudgeLogFormatter.FormatEscalationNotEscalated(
                "lord_4_15", "lord_1_22",
                "personal -12.3 < threshold 30");

            Assert.Equal("escalation lord_4_15 -> lord_1_22: not escalated - personal -12.3 < threshold 30", line);
        }

        [Fact]
        public void LogFormatter_FormatDecayLines_MatchesHitBandAndInsideBand()
        {
            string inside = GrudgeLogFormatter.FormatDecayInsideBand(
                91300.0, 91308.0, 8.0, 3.0, 1.25, 3.333, 10.0, -8.0);
            Assert.Equal("    decay 91300.0 -> 91308.0: 8.0d / 3.00d per point x 1.25 = 3.33 points, inside band +-10.0, no change -> -8.0", inside);

            string hit = GrudgeLogFormatter.FormatDecayHitBand(
                91308.0, 91400.0, 92.0, 3.0, 1.25, 38.333, -10.0, -10.0);
            Assert.Equal("    decay 91308.0 -> 91400.0: 92.0d / 3.00d per point x 1.25 = 38.33 points, hit band -10.0 -> -10.0", hit);
        }

        /// <summary>整段重播的行序與每一行的日期／值。個別行的格式測試看不出「第一筆被印成下一段的日期」
        /// 這種錯位，必須把整串拉出來逐行比對。</summary>
        [Fact]
        public void LogFormatter_PairReplay_PrintsEachEntryOnItsOwnDay_ThenTheDecayFromThatDay()
        {
            var cfg = new SituationsConfig();
            cfg.GrudgeDecay.Personal.Band = 10.0;
            cfg.GrudgeDecay.Personal.DaysPerPoint = 3.0;

            var entries = new List<GrudgeEntry>
            {
                new() { Day = 91300.0, Requested = -8.0, EventId = "evt_91300_ab12", Scope = GrudgeScope.Personal },
                new() { Day = 91308.0, Requested = -14.0, EventId = "evt_91308_77c1", Scope = GrudgeScope.Personal }
            };

            // 無特質 ⇒ 倍率 1.00。91300 記 -8（在 ±10 內，到 91308 不動）；
            // 91308 記 -14 ⇒ -22；到 91320.0 經過 12 天 = 4.00 點 ⇒ -18.0
            var res = GrudgeDecay.Replay(entries, GrudgeScope.Personal, cfg, null, 91320.0);
            var lines = GrudgeLogFormatter.FormatPairReplay("lord_1_22", GrudgeScope.Personal, res);

            Assert.Equal(new[]
            {
                "  vs lord_1_22 [personal] value -18.0 (band +-10.0, 2 entry(ies))",
                "    day 91300.0 evt_91300_ab12 requested -8.0 -> -8.0",
                "    decay 91300.0 -> 91308.0: 8.0d / 3.00d per point x 1.00 = 2.67 points, inside band +-10.0, no change -> -8.0",
                "    day 91308.0 evt_91308_77c1 requested -14.0 -> -22.0",
                "    decay 91308.0 -> 91320.0: 12.0d / 3.00d per point x 1.00 = 4.00 points -> -18.0",
                "    multiplier x1.00 = no trait profile"
            }, lines);
        }

        #endregion

        #region 9. Config & Scaling (2 tests)

        [Fact]
        public void ConfigMerge_AddsGrudgeDecayAndGrudgesEnabled_KeepsExistingValues()
        {
            string oldJson = @"{ ""enabled"": true, ""situations"": { ""maxPerDay"": 2.5 } }";
            var userObj = JObject.Parse(oldJson);
            var defaultObj = JObject.Parse(VividJson.Write(new VividWorldConfig()));
            var result = ConfigMerge.AddMissingKeys(userObj, defaultObj);
            var merged = VividJson.Read<VividWorldConfig>(result.Merged.ToString());

            Assert.NotNull(merged);

            Assert.True(merged!.Situations.GrudgesEnabled);
            Assert.NotNull(merged.Situations.GrudgeDecay);
            Assert.Equal(10.0, merged.Situations.GrudgeDecay.Personal.Band);
            Assert.Equal(3.0, merged.Situations.GrudgeDecay.Personal.DaysPerPoint);
            Assert.Equal(20.0, merged.Situations.GrudgeDecay.Clan.Band);
            Assert.Equal(6.0, merged.Situations.GrudgeDecay.Clan.DaysPerPoint);
            Assert.Equal(2.5, merged.Situations.MaxPerDay);
        }

        [Fact]
        public void CalendarScaling_ScalesDaysPerPoint_KeepsBandUnscaled()
        {
            var config = new VividWorldConfig();
            config.Situations.GrudgeDecay.Personal.DaysPerPoint = 3.0;
            config.Situations.GrudgeDecay.Personal.Band = 10.0;
            config.Situations.GrudgeDecay.Clan.DaysPerPoint = 6.0;
            config.Situations.GrudgeDecay.Clan.Band = 20.0;

            // calendarFactor = 2.0 (e.g. 168 days per year instead of 84)
            CalendarScaling.Apply(config, 168.0);

            Assert.Equal(6.0, config.Situations.GrudgeDecay.Personal.DaysPerPoint);
            Assert.Equal(10.0, config.Situations.GrudgeDecay.Personal.Band); // unscaled
            Assert.Equal(12.0, config.Situations.GrudgeDecay.Clan.DaysPerPoint);
            Assert.Equal(20.0, config.Situations.GrudgeDecay.Clan.Band); // unscaled
        }

        #endregion

        #region 10. Actual Repository Files (2 tests)

        [Fact]
        public void ActualRepo_SituationsJson_LoadsAndCrossChecksWithZeroErrors()
        {
            string situationsPath = FindRepoFile(@"module\ModuleData\vividworld_situations.json");
            string situationsJson = File.ReadAllText(situationsPath);

            var catalog = SituationCatalogLoader.Load(situationsJson);
            Assert.DoesNotContain(catalog.Issues, i => i.IsError);

            string eventsPath = FindRepoFile(@"module\ModuleData\vividworld_situation_events.json");
            string eventsJson = File.ReadAllText(eventsPath);
            var eventsCatalog = EventCatalogLoader.Load(eventsJson, new PersistenceConfig());

            var crossIssues = SituationCatalogCrossCheck.Check(catalog, eventsCatalog, null!);
            Assert.DoesNotContain(crossIssues, i => i.IsError);
        }

        [Fact]
        public void ActualRepo_SeatDisputeGrudges_MatchCardSpecificationExactly()
        {
            string situationsPath = FindRepoFile(@"module\ModuleData\vividworld_situations.json");
            string situationsJson = File.ReadAllText(situationsPath);
            var catalog = SituationCatalogLoader.Load(situationsJson);

            var seatDispute = catalog.Situations.FirstOrDefault(s => s.Id == "seat_dispute");
            Assert.NotNull(seatDispute);

            // 1. demand_seat
            var demand = seatDispute!.Branches.FirstOrDefault(b => b.Id == "demand_seat");
            Assert.NotNull(demand);
            Assert.NotNull(demand!.Grudges);
            Assert.Equal(2, demand.Grudges!.Count);
            Assert.Equal("favored", demand.Grudges[0].From);
            Assert.Equal("slighted", demand.Grudges[0].To);
            Assert.Equal(-8.0, demand.Grudges[0].Amount);
            Assert.Equal("slighted", demand.Grudges[1].From);
            Assert.Equal("favored", demand.Grudges[1].To);
            Assert.Equal(-5.0, demand.Grudges[1].Amount);

            // 2. endure
            var endure = seatDispute.Branches.FirstOrDefault(b => b.Id == "endure");
            Assert.NotNull(endure);
            Assert.NotNull(endure!.Grudges);
            Assert.Single(endure.Grudges!);
            Assert.Equal("slighted", endure.Grudges[0].From);
            Assert.Equal("favored", endure.Grudges[0].To);
            Assert.Equal(-6.0, endure.Grudges[0].Amount);
            Assert.True(endure.Grudges[0].LedgerOnly);

            // 3. yield
            var yield = seatDispute.Branches.FirstOrDefault(b => b.Id == "yield");
            Assert.NotNull(yield);
            Assert.NotNull(yield!.Grudges);
            Assert.Single(yield.Grudges!);
            Assert.Equal("favored", yield.Grudges[0].From);
            Assert.Equal("slighted", yield.Grudges[0].To);
            Assert.Equal(5.0, yield.Grudges[0].Amount);

            // 4. walk_out
            var walkOut = seatDispute.Branches.FirstOrDefault(b => b.Id == "walk_out");
            Assert.NotNull(walkOut);
            Assert.NotNull(walkOut!.Grudges);
            Assert.Equal(2, walkOut.Grudges!.Count);
            Assert.Equal("slighted", walkOut.Grudges[0].From);
            Assert.Equal("favored", walkOut.Grudges[0].To);
            Assert.Equal(-8.0, walkOut.Grudges[0].Amount);
            Assert.Equal("clan", walkOut.Grudges[0].Escalate);
            Assert.Equal("host", walkOut.Grudges[1].From);
            Assert.Equal("slighted", walkOut.Grudges[1].To);
            Assert.Equal(-6.0, walkOut.Grudges[1].Amount);
        }

        #endregion
    }
}
