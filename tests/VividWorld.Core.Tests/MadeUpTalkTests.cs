using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using VividWorld.Core.Catalog;
using VividWorld.Core.Config;
using VividWorld.Core.Events;
using VividWorld.Core.Diagnostics;
using VividWorld.Core.Feelings;
using VividWorld.Core.Grudges;
using VividWorld.Core.Memory;
using VividWorld.Core.Ingest;
using VividWorld.Core.Persistence;
using VividWorld.Core.Presentation;
using VividWorld.Core.Rumors;
using VividWorld.Core.Situations;
using VividWorld.Core.Tests.Fakes;
using VividWorld.Core.Util;
using Xunit;

namespace VividWorld.Core.Tests
{
    /// <summary>編的話：資料欄位、三個起因的情境、合計上限、挑內容、編的話版本的模板、知情者、信不信、不洩漏。</summary>
    public class MadeUpTalkTests
    {
        // ───────────── 共用 ─────────────

        private static string RepoFile(params string[] parts)
        {
            string? current = AppContext.BaseDirectory;
            while (!string.IsNullOrEmpty(current))
            {
                if (File.Exists(Path.Combine(current, "VividWorld.sln")))
                {
                    return Path.Combine(new[] { current }.Concat(parts).ToArray());
                }
                current = Directory.GetParent(current)?.FullName;
            }
            throw new FileNotFoundException("repo root not found");
        }

        private static readonly Lazy<List<EventCatalog>> RealEventCatalogs = new(() =>
            new[] { "vividworld_events.json", "vividworld_situation_events.json" }
                .Select(f => EventCatalogLoader.Load(File.ReadAllText(RepoFile("module", "ModuleData", f)), new PersistenceConfig { MaxFactsPerEvent = 24 }))
                .ToList());

        private static EventTemplate RealTemplate(string type)
        {
            var t = RealEventCatalogs.Value.Select(c => c.ByType(type)).FirstOrDefault(x => x != null);
            Assert.NotNull(t);
            return t!;
        }

        private static SituationCatalog RealSituations()
            => SituationCatalogLoader.Load(File.ReadAllText(RepoFile("module", "ModuleData", "vividworld_situations.json")));

        private static SituationRoleFacts Facts(string id, string clan, string? kingdom, string? settlement,
            bool leader = false, bool kingdomLeader = false, bool player = false, bool alive = true, bool lord = true)
            => new SituationRoleFacts
            {
                HeroId = id,
                ClanId = clan,
                KingdomId = kingdom,
                SettlementId = settlement,
                IsClanLeader = leader,
                IsKingdomLeader = kingdomLeader,
                IsPlayer = player,
                IsAlive = alive,
                IsLord = lord
            };

        // ───────────── 1. 資料欄位與存檔相容 ─────────────

        [Fact]
        public void WorldEvent_FabricatedFalse_AndNullOriginator_AreNotWrittenToFile()
        {
            var evt = new WorldEvent { EventId = "evt_1", Type = "hero_died_of_old_age" };
            string json = VividJson.Write(evt);
            Assert.DoesNotContain("fabricated", json, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("originatorHeroId", json, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void WorldEvent_FabricatedTrue_RoundTrips()
        {
            var evt = new WorldEvent { EventId = "evt_1", Type = "conduct_refused_aid", Fabricated = true, OriginatorHeroId = "hero_t" };
            string json = VividJson.Write(evt);
            Assert.Contains("\"fabricated\": true", json);
            Assert.Contains("\"originatorHeroId\": \"hero_t\"", json);
            var back = VividJson.Read<WorldEvent>(json)!;
            Assert.True(back.Fabricated);
            Assert.Equal("hero_t", back.OriginatorHeroId);
        }

        [Fact]
        public void WorldEvent_OldFileWithoutTheFields_ReadsAsARealEvent()
        {
            var back = VividJson.Read<WorldEvent>("{\"eventId\":\"evt_old\",\"type\":\"hero_died_of_old_age\",\"day\":3.0}")!;
            Assert.False(back.Fabricated);
            Assert.Null(back.OriginatorHeroId);
            Assert.False(MadeUpTalk.IsHearsayOnly(back));
        }

        [Fact]
        public void IndexEntry_CarriesFabricated_AndOldIndexReadsAsFalse()
        {
            var evt = new WorldEvent
            {
                EventId = "evt_1",
                Type = "conduct_refused_aid",
                Fabricated = true,
                OriginatorHeroId = "hero_t",
                Participants = new Dictionary<string, string> { ["refuser"] = "hero_x" }
            };
            var entry = RumorIndexEntry.From(evt, 10);
            Assert.True(entry.Fabricated);
            Assert.Contains("fabricated", VividJson.Write(entry), StringComparison.OrdinalIgnoreCase);

            var real = RumorIndexEntry.From(new WorldEvent { EventId = "evt_2", Type = "hero_died_of_old_age" }, 10);
            Assert.False(real.Fabricated);
            Assert.DoesNotContain("fabricated", VividJson.Write(real), StringComparison.OrdinalIgnoreCase);

            var old = VividJson.Read<RumorIndexEntry>("{\"eventId\":\"evt_old\",\"type\":\"hero_died_of_old_age\"}")!;
            Assert.False(old.Fabricated);
        }

        [Fact]
        public void IsHearsayOnly_NeedsFabricatedAndAnOriginatorWhoIsNotAParticipant()
        {
            var evt = new WorldEvent
            {
                Fabricated = true,
                OriginatorHeroId = "hero_t",
                Participants = new Dictionary<string, string> { ["refuser"] = "hero_x" }
            };
            Assert.True(MadeUpTalk.IsHearsayOnly(evt));

            evt.Participants["speaker"] = "hero_t"; // 誇戰功：起頭的人自己是參與者
            Assert.False(MadeUpTalk.IsHearsayOnly(evt));

            evt.Participants.Remove("speaker");
            evt.Fabricated = false;
            Assert.False(MadeUpTalk.IsHearsayOnly(evt));
            Assert.False(MadeUpTalk.IsHearsayOnly((WorldEvent?)null));
        }

        // ───────────── 2. 情境載入與驗證 ─────────────

        [Fact]
        public void RealSituations_ThreeMadeUpSituations_LoadWithoutErrors()
        {
            var catalog = RealSituations();
            Assert.DoesNotContain(catalog.Issues, i => i.IsError);

            foreach (var id in new[] { "madeup_slander", "madeup_rivalry", "madeup_praise" })
            {
                var t = catalog.Situations.Single(s => s.Id == id);
                Assert.Equal("@falseRumors.enabled", t.EnabledBy);
                Assert.NotNull(t.QuotaGroup);
                Assert.Equal("madeUpTalk", t.QuotaGroup!.Id);
                Assert.Equal("@falseRumors.maxPerDay", t.QuotaGroup.MaxPerDayRef);
                Assert.False(t.GetMaxPerDay(new VividWorldConfig()).HasValue && t.MaxPerDayRef != null); // 不走自有上限那條路
                var say = t.Branches.Single(b => b.Id == "say");
                Assert.NotNull(say.MadeUpTalk);
                Assert.Empty(say.Events);
                var hold = t.Branches.Single(b => b.Id == "hold");
                Assert.Null(hold.MadeUpTalk);
                Assert.Empty(hold.Events);
                Assert.DoesNotContain(t.Conditions, c => string.Equals(c.Type, "cooldown", StringComparison.OrdinalIgnoreCase));
            }

            var slander = catalog.Situations.Single(s => s.Id == "madeup_slander");
            Assert.Equal("@falseRumors.slanderMaxPerDay", slander.QuotaGroup!.ShareRef);
            Assert.Equal("slander", slander.Branches.Single(b => b.Id == "say").MadeUpTalk!.Kind);
            var gw = slander.Branches.Single(b => b.Id == "say").GrudgeWeight!;
            Assert.Equal("teller", gw.From);
            Assert.Equal("target", gw.Toward);
            Assert.Equal(0.05, gw.PerPoint);
            Assert.Equal(1.0, gw.Max);
            Assert.Equal("grudgeTargetOf:teller", slander.Roles["target"].Derived);

            Assert.Equal("@falseRumors.rivalryPerDay", catalog.Situations.Single(s => s.Id == "madeup_rivalry").QuotaGroup!.ShareRef);
            var praise = catalog.Situations.Single(s => s.Id == "madeup_praise");
            Assert.Equal("@falseRumors.praisePerDay", praise.QuotaGroup!.ShareRef);
            Assert.Equal("praise", praise.Branches.Single(b => b.Id == "say").MadeUpTalk!.Kind);
            Assert.Equal("kinOf:teller", praise.Roles["target"].Derived);
        }

        private static string OneSituation(string quotaGroup = "\"quotaGroup\": { \"id\": \"g\", \"maxPerDay\": \"@falseRumors.maxPerDay\", \"share\": \"@falseRumors.slanderMaxPerDay\" },",
            string sayBranch = "{ \"id\": \"say\", \"base\": 1.0, \"madeUpTalk\": { \"kind\": \"slander\", \"teller\": \"teller\", \"listener\": \"listener\", \"about\": \"target\" }, \"events\": [] }",
            string id = "sit_a")
        {
            return "{ \"situations\": [ { \"id\": \"" + id + "\", \"trigger\": \"direct\", \"decider\": \"teller\", " + quotaGroup +
                   " \"roles\": { \"teller\": {}, \"listener\": {}, \"target\": { \"derived\": \"kinOf:teller\" } }, \"conditions\": [], \"branches\": [ " + sayBranch + " ] } ] }";
        }

        [Fact]
        public void Loader_ValidMadeUpSituation_HasNoErrors()
        {
            var catalog = SituationCatalogLoader.Load(OneSituation());
            Assert.Single(catalog.Situations);
            Assert.DoesNotContain(catalog.Issues, i => i.IsError);
        }

        [Fact]
        public void Loader_MadeUpTalk_WithEvents_IsRejected()
        {
            string json = OneSituation(sayBranch: "{ \"id\": \"say\", \"base\": 1.0, \"madeUpTalk\": { \"kind\": \"slander\", \"teller\": \"teller\", \"listener\": \"listener\", \"about\": \"target\" }, \"events\": [ { \"type\": \"x\", \"bind\": { \"A\": \"teller\" } } ] }");
            var catalog = SituationCatalogLoader.Load(json);
            Assert.Contains(catalog.Issues, i => i.Code == SituationIssueCode.InvalidMadeUpTalk && i.IsError);
        }

        [Fact]
        public void Loader_MadeUpTalk_BadKindOrUnknownRole_IsRejected()
        {
            var badKind = SituationCatalogLoader.Load(OneSituation(sayBranch: "{ \"id\": \"say\", \"base\": 1.0, \"madeUpTalk\": { \"kind\": \"gossip\", \"teller\": \"teller\", \"listener\": \"listener\", \"about\": \"target\" }, \"events\": [] }"));
            Assert.Contains(badKind.Issues, i => i.Code == SituationIssueCode.InvalidMadeUpTalk && i.IsError);

            var badRole = SituationCatalogLoader.Load(OneSituation(sayBranch: "{ \"id\": \"say\", \"base\": 1.0, \"madeUpTalk\": { \"kind\": \"praise\", \"teller\": \"teller\", \"listener\": \"nobody\", \"about\": \"target\" }, \"events\": [] }"));
            Assert.Contains(badRole.Issues, i => i.Code == SituationIssueCode.InvalidMadeUpTalk && i.IsError);
        }

        [Fact]
        public void Loader_GrudgeWeight_UnknownRole_IsRejected()
        {
            string json = OneSituation(sayBranch: "{ \"id\": \"say\", \"base\": 1.0, \"grudgeWeight\": { \"from\": \"teller\", \"toward\": \"ghost\", \"perPoint\": 0.05, \"max\": 1.0 }, \"madeUpTalk\": { \"kind\": \"slander\", \"teller\": \"teller\", \"listener\": \"listener\", \"about\": \"target\" }, \"events\": [] }");
            var catalog = SituationCatalogLoader.Load(json);
            Assert.Contains(catalog.Issues, i => i.Code == SituationIssueCode.InvalidGrudgeWeight && i.IsError);
        }

        [Fact]
        public void Loader_QuotaGroup_BadReferenceOrMissingId_IsRejected()
        {
            var badRef = SituationCatalogLoader.Load(OneSituation(quotaGroup: "\"quotaGroup\": { \"id\": \"g\", \"maxPerDay\": \"@falseRumors.bogus\", \"share\": 1 },"));
            Assert.Contains(badRef.Issues, i => i.IsError && i.Field.StartsWith("quotaGroup", StringComparison.Ordinal));

            var badShare = SituationCatalogLoader.Load(OneSituation(quotaGroup: "\"quotaGroup\": { \"id\": \"g\", \"maxPerDay\": 0.8, \"share\": \"@falseRumors.bogus\" },"));
            Assert.Contains(badShare.Issues, i => i.IsError && i.Field.StartsWith("quotaGroup", StringComparison.Ordinal));

            var noId = SituationCatalogLoader.Load(OneSituation(quotaGroup: "\"quotaGroup\": { \"maxPerDay\": 0.8, \"share\": 1 },"));
            Assert.Contains(noId.Issues, i => i.Code == SituationIssueCode.InvalidQuotaGroup && i.IsError);
        }

        [Fact]
        public void Loader_QuotaGroup_SameIdWithDifferentMaxPerDay_IsRejected()
        {
            string a = OneSituation(id: "sit_a");
            string b = OneSituation(quotaGroup: "\"quotaGroup\": { \"id\": \"g\", \"maxPerDay\": 2.0, \"share\": 1 },", id: "sit_b");
            var joined = "{ \"situations\": [" + a.Substring(a.IndexOf('[') + 1, a.LastIndexOf(']') - a.IndexOf('[') - 1) + "," +
                         b.Substring(b.IndexOf('[') + 1, b.LastIndexOf(']') - b.IndexOf('[') - 1) + "] }";
            var catalog = SituationCatalogLoader.Load(joined);
            Assert.Contains(catalog.Issues, i => i.Code == SituationIssueCode.InvalidQuotaGroup && i.IsError && i.Detail.Contains("Inconsistent"));
        }

        // ───────────── 設定鍵 ─────────────

        [Fact]
        public void Config_NewKeys_HaveDefaults_AndAreClamped()
        {
            var cfg = new VividWorldConfig();
            Assert.Equal(0.8, cfg.FalseRumors.MaxPerDay);
            Assert.Equal(0.5, cfg.FalseRumors.SlanderMaxPerDay);
            Assert.Equal(0.15, cfg.FalseRumors.RivalryPerDay);
            Assert.Equal(0.15, cfg.FalseRumors.PraisePerDay);

            cfg.FalseRumors.MaxPerDay = 99;
            cfg.FalseRumors.SlanderMaxPerDay = -3;
            cfg.Normalize();
            Assert.Equal(10.0, cfg.FalseRumors.MaxPerDay);
            Assert.Equal(0.0, cfg.FalseRumors.SlanderMaxPerDay);
        }

        [Fact]
        public void Config_OldFileWithoutTheNewKeys_GetsDefaults()
        {
            var cfg = VividJson.Read<VividWorldConfig>("{ \"falseRumors\": { \"enabled\": true } }")!;
            cfg.Normalize();
            Assert.Equal(0.8, cfg.FalseRumors.MaxPerDay);
            Assert.Equal(0.5, cfg.FalseRumors.SlanderMaxPerDay);
        }

        [Fact]
        public void ConfigResolver_ResolvesTheFourReferences_AndRejectsOthers()
        {
            var cfg = new VividWorldConfig();
            cfg.FalseRumors.MaxPerDay = 1.25;
            cfg.FalseRumors.SlanderMaxPerDay = 0.4;
            cfg.FalseRumors.RivalryPerDay = 0.2;
            cfg.FalseRumors.PraisePerDay = 0.1;

            Assert.Equal(1.25, SituationConfigResolver.ResolveDouble("@falseRumors.maxPerDay", cfg));
            Assert.Equal(0.4, SituationConfigResolver.ResolveDouble("@falseRumors.slanderMaxPerDay", cfg));
            Assert.Equal(0.2, SituationConfigResolver.ResolveDouble("@falseRumors.rivalryPerDay", cfg));
            Assert.Equal(0.1, SituationConfigResolver.ResolveDouble("@falseRumors.praisePerDay", cfg));

            Assert.True(SituationConfigResolver.IsValidMaxPerDayRef("@falseRumors.maxPerDay"));
            Assert.False(SituationConfigResolver.IsValidMaxPerDayRef("@falseRumors.slanderMaxPerDay"));
            Assert.True(SituationConfigResolver.IsValidShareRef("@falseRumors.slanderMaxPerDay"));
            Assert.False(SituationConfigResolver.IsValidShareRef("@falseRumors.maxPerDay"));
        }

        [Fact]
        public void McmKeys_FalseRumorsEnabledAndMaxPerDay_AreExposed()
        {
            Assert.Contains(McmExposedKeys.All, k => k.Path == "falseRumors.enabled" && k.Kind == McmKeyKind.Bool);
            var max = McmExposedKeys.All.Single(k => k.Path == "falseRumors.maxPerDay");
            Assert.Equal(McmKeyKind.Double, max.Kind);
            Assert.Equal(0.0, max.Min);
            Assert.Equal(5.0, max.Max);
        }

        // ───────────── kinOf ─────────────

        private static AbsentRoleSelectionResult SelectKin(SituationRoleFacts teller, string? settlement, params SituationRoleFacts[] pool)
            => AbsentRoleSelector.Select(
                "target",
                "kinOf:teller",
                teller,
                currentSettlementId: settlement,
                candidatePool: pool,
                alreadyBoundHeroes: new Dictionary<string, string?> { ["teller"] = teller.HeroId },
                context: new SituationWorldContext { Seed = 1, Rng = new SplitMix64Rng() },
                situationId: "madeup_praise",
                day: 10.0);

        [Fact]
        public void KinOf_FindsAClanMateWhoIsNotInThisSettlement()
        {
            var teller = Facts("t", "c1", "k1", "town_1");
            var kin = Facts("kin", "c1", "k1", "castle_9");
            var r = SelectKin(teller, "town_1", kin);
            Assert.Equal("kin", r.PickedHeroId);
            Assert.Null(r.UnboundReason);
        }

        [Fact]
        public void KinOf_ExcludesSelfPlayerPresentDeadNonLordOtherClan_AndSaysWhy()
        {
            var teller = Facts("t", "c1", "k1", "town_1");
            var r = SelectKin(teller, "town_1",
                Facts("t", "c1", "k1", "castle_9"),                       // 本人
                Facts("p", "c1", "k1", "castle_9", player: true),         // 玩家
                Facts("here", "c1", "k1", "town_1"),                      // 在這座聚落
                Facts("dead", "c1", "k1", "castle_9", alive: false),      // 死的
                Facts("child", "c1", "k1", "castle_9", lord: false),      // 不是領主
                Facts("other", "c2", "k1", "castle_9"));                  // 別的家族
            Assert.Null(r.PickedHeroId);
            Assert.NotNull(r.UnboundReason);
            var reasons = r.Exclusions.Select(e => e.Reason).ToList();
            Assert.Contains("self", reasons);
            Assert.Contains("player", reasons);
            Assert.Contains("present in settlement", reasons);
            Assert.Contains("dead", reasons);
            Assert.Contains("not a lord", reasons);
            Assert.Contains("different clan", reasons);
        }

        [Fact]
        public void KinOf_TellerWithoutAClan_IsUnbound()
        {
            var teller = Facts("t", "", "k1", "town_1");
            teller.ClanId = null;
            var r = SelectKin(teller, "town_1", Facts("kin", "c1", "k1", "castle_9"));
            Assert.Null(r.PickedHeroId);
            Assert.Contains("no clan", r.UnboundReason);
        }

        [Fact]
        public void KinOf_IsOneOfTheSupportedStrategies()
        {
            Assert.Equal(("kinOf", "teller"), AbsentRoleSelector.ParseStrategy("kinOf:teller"));
        }

        // ───────────── grudgeWeight ─────────────

        private static SituationDecision DecideSlander(int grudgeSum, int? affection)
        {
            var template = RealSituations().Situations.Single(s => s.Id == "madeup_slander");
            var facts = new Dictionary<string, SituationRoleFacts>
            {
                ["teller"] = Facts("t", "c1", "k1", "town_1"),
                ["listener"] = Facts("l", "c2", "k1", "town_1"),
                ["target"] = Facts("x", "c3", "k1", "castle_9")
            };
            var bound = new Dictionary<string, string?> { ["teller"] = "t", ["listener"] = "l", ["target"] = "x" };
            var context = new SituationWorldContext
            {
                GrudgeSum = (a, b) => a == "t" && b == "x" ? grudgeSum : 0,
                Affection = (a, b) => a == "t" && b == "x" ? affection : null,
                Rng = new SplitMix64Rng(),
                Seed = 1
            };
            return SituationBranchSelector.Select(template, facts, bound, new Dictionary<string, string>(),
                new TraitProfile { HeroId = "t", Honor = 0 }, 0.05, new SplitMix64Rng(), 5, context: context, situationId: template.Id, day: 10);
        }

        [Fact]
        public void GrudgeWeight_AddsPerPointTimesDepth_TakingTheDeeperOfLedgerAndRelation()
        {
            // 帳 -10（深度 10）、好感 -20（深度 5）：取 10，加 0.05 × 10 = 0.5
            var d = DecideSlander(-10, -20);
            var say = d.Branches.Single(b => b.BranchId == "say");
            Assert.Equal(10.0, say.GrudgeDepth!.Value, 6);
            Assert.Equal(0.5, say.GrudgeBonus!.Value, 6);
            Assert.Equal(1.5, say.Raw, 6);

            // 帳 0、好感 -80（深度 20）：0.05 × 20 = 1.0，剛好是上限
            var capped = DecideSlander(0, -80).Branches.Single(b => b.BranchId == "say");
            Assert.Equal(20.0, capped.GrudgeDepth!.Value, 6);
            Assert.Equal(1.0, capped.GrudgeBonus!.Value, 6);

            // 深度 100：只加到上限 1.0
            var over = DecideSlander(-100, null).Branches.Single(b => b.BranchId == "say");
            Assert.Equal(1.0, over.GrudgeBonus!.Value, 6);
        }

        [Fact]
        public void GrudgeWeight_NoGrudgeAndFriendlyRelation_AddsNothing_AndHoldBranchHasNone()
        {
            var d = DecideSlander(5, 40);
            var say = d.Branches.Single(b => b.BranchId == "say");
            Assert.Equal(0.0, say.GrudgeDepth!.Value, 6);
            Assert.Equal(0.0, say.GrudgeBonus!.Value, 6);
            var hold = d.Branches.Single(b => b.BranchId == "hold");
            Assert.Null(hold.GrudgeBonus);
        }

        [Fact]
        public void GrudgeWeight_ShowsUpInTheBranchWeightLogLine()
        {
            var template = RealSituations().Situations.Single(s => s.Id == "madeup_slander");
            var d = DecideSlander(-10, null);
            var lines = SituationLogFormatter.FormatExecution(template.Id, 1, new Dictionary<string, string?> { ["teller"] = "t" }, "town_1",
                new List<ConditionEvaluationResult>(), d);
            Assert.Contains(lines, l => l.Contains("grudge(depth=10.00, add=0.50)"));
        }

        // ───────────── 群組挑起因 ─────────────

        [Fact]
        public void GroupPicker_ZeroShare_IsNeverPicked_AndPicksAreDeterministic()
        {
            var shares = new List<(string, double)> { ("a", 0.5), ("b", 0.0), ("c", 0.15) };
            var rng = new SplitMix64Rng();
            for (long seed = 0; seed < 200; seed++)
            {
                var r = SituationGroupPicker.Pick(shares, null, rng, seed);
                Assert.NotEqual("b", r.SelectedSituationId);
                Assert.Equal(r.SelectedSituationId, SituationGroupPicker.Pick(shares, null, rng, seed).SelectedSituationId);
            }
        }

        [Fact]
        public void GroupPicker_FollowsTheShares()
        {
            var shares = new List<(string, double)> { ("a", 0.5), ("c", 0.5) };
            var rng = new SplitMix64Rng();
            int a = 0;
            for (long seed = 0; seed < 2000; seed++)
            {
                if (SituationGroupPicker.Pick(shares, null, rng, seed).SelectedSituationId == "a") a++;
            }
            Assert.InRange(a, 850, 1150);

            var lopsided = new List<(string, double)> { ("a", 0.9), ("c", 0.1) };
            int a2 = 0;
            for (long seed = 0; seed < 2000; seed++)
            {
                if (SituationGroupPicker.Pick(lopsided, null, rng, seed).SelectedSituationId == "a") a2++;
            }
            Assert.InRange(a2, 1650, 1950);
        }

        [Fact]
        public void GroupPicker_RepicksAmongTheRest_WhenSomeHaveNoCandidates()
        {
            var shares = new List<(string, double)> { ("a", 0.5), ("b", 0.15), ("c", 0.15) };
            var rng = new SplitMix64Rng();
            for (long seed = 0; seed < 100; seed++)
            {
                var r = SituationGroupPicker.Pick(shares, new[] { "a" }, rng, seed);
                Assert.True(r.Repicked);
                Assert.Contains(r.SelectedSituationId, new[] { "b", "c" });
            }

            var none = SituationGroupPicker.Pick(shares, new[] { "a", "b", "c" }, rng, 1);
            Assert.Null(none.SelectedSituationId);

            var onlyZeroLeft = SituationGroupPicker.Pick(new List<(string, double)> { ("a", 0.5), ("b", 0.0) }, new[] { "a" }, rng, 1);
            Assert.Null(onlyZeroLeft.SelectedSituationId);
        }

        // ───────────── 挑內容 ─────────────

        private sealed class PlanRig
        {
            public SituationRoleFacts Teller = Facts("t", "c1", "k1", "town_1");
            public SituationRoleFacts Listener = Facts("l", "c2", "k1", "town_1");
            public SituationRoleFacts Target = Facts("x", "c3", "k1", "castle_9");
            public SituationRoleFacts Ruler = Facts("ruler", "c9", "k1", "city_7", leader: true, kingdomLeader: true);
            public List<SituationRoleFacts> Pool = new();
            public TraitProfile TellerTraits = new TraitProfile { HeroId = "t" };
            public TraitProfile TargetTraits = new TraitProfile { HeroId = "x" };
            public List<MadeUpTalkCandidateRealEvent> Real = new();
            public Func<string, string, bool> Grudge = (a, b) => false;

            public PlanRig()
            {
                Pool.Add(Ruler);
                Pool.Add(Facts("asker", "c4", "k1", "castle_5")); // 求援的人／受惠的人
            }

            public MadeUpTalkPlanResult Plan(string kind, long instance = 11)
                => MadeUpTalkPlanner.Plan(kind, Teller, Listener, Target, TellerTraits, TargetTraits, "town_1",
                    Pool, Real, Grudge, 99, instance, new SplitMix64Rng());
        }

        private static MadeUpTalkCandidateRealEvent PrisonEvent(string id, string captor, string prisoner, double day = 50, string? settlement = "town_2")
            => new MadeUpTalkCandidateRealEvent
            {
                EventId = id,
                Type = "hero_taken_prisoner",
                Day = day,
                SettlementId = settlement,
                Participants = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["captor"] = captor, ["prisoner"] = prisoner }
            };

        private static MadeUpTalkCandidateRealEvent DeathEvent(string id, string type, string victim, double day = 40, string? kingdom = null)
            => new MadeUpTalkCandidateRealEvent
            {
                EventId = id,
                Type = type,
                Day = day,
                SettlementId = "town_3",
                VictimKingdomId = kingdom,
                Participants = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["victim"] = victim }
            };

        [Fact]
        public void Plan_NoRealEventsToHangOn_OnlyTheFloatingContentsAreAvailable_AndTheReasonsAreListed()
        {
            var rig = new PlanRig();
            var r = rig.Plan("slander");
            Assert.True(r.Success);
            Assert.Contains(r.ContentType, new[] { MadeUpTalkPlanner.SpokeAgainstRuler, MadeUpTalkPlanner.RefusedAid });
            Assert.Null(r.LinkedEventId);
            Assert.Contains(r.CandidateExplanations, e => e.StartsWith("conduct_rash_capture: unavailable", StringComparison.Ordinal));
            Assert.Contains(r.CandidateExplanations, e => e.StartsWith("conduct_mistreated_prisoner: unavailable", StringComparison.Ordinal));
            Assert.Contains(r.CandidateExplanations, e => e.StartsWith("conduct_poisoned: unavailable", StringComparison.Ordinal));
        }

        [Fact]
        public void Plan_SpokeAgainstRuler_UnavailableWhenNoKingdomTargetIsTheRulerOrRulerIsThePlayer()
        {
            var noKingdom = new PlanRig();
            noKingdom.Target.KingdomId = null;
            var r1 = noKingdom.Plan("slander");
            Assert.Contains(r1.CandidateExplanations, e => e.StartsWith("conduct_spoke_against_ruler: unavailable (target has no kingdom", StringComparison.Ordinal));

            var isRuler = new PlanRig();
            isRuler.Target.IsKingdomLeader = true;
            Assert.Contains(isRuler.Plan("slander").CandidateExplanations, e => e.StartsWith("conduct_spoke_against_ruler: unavailable (target is ruler", StringComparison.Ordinal));

            var playerRuler = new PlanRig();
            playerRuler.Ruler.IsPlayer = true;
            Assert.Contains(playerRuler.Plan("slander").CandidateExplanations, e => e.StartsWith("conduct_spoke_against_ruler: unavailable", StringComparison.Ordinal));
        }

        [Fact]
        public void Plan_AnotherParty_ExcludesPlayerPresentOtherKingdomTellerListenerTarget()
        {
            var rig = new PlanRig();
            rig.Pool.Clear();
            rig.Pool.Add(Facts("pl", "c5", "k1", "castle_5", player: true));
            rig.Pool.Add(Facts("here", "c5", "k1", "town_1"));
            rig.Pool.Add(Facts("far", "c6", "k2", "castle_6"));
            rig.Pool.Add(Facts("dead", "c7", "k1", "castle_7", alive: false));
            rig.Pool.Add(Facts("t", "c1", "k1", "castle_8"));
            rig.Pool.Add(Facts("l", "c2", "k1", "castle_8"));
            rig.Pool.Add(Facts("x", "c3", "k1", "castle_8"));
            var r = rig.Plan("slander");
            Assert.Contains(r.CandidateExplanations, e => e.StartsWith("conduct_refused_aid: unavailable (no counterpart asker", StringComparison.Ordinal));

            rig.Pool.Add(Facts("ok", "c8", "k1", "castle_9"));
            var ok = rig.Plan("slander");
            Assert.Contains(ok.CandidateExplanations, e => e.StartsWith("conduct_refused_aid: available", StringComparison.Ordinal) && e.Contains("counterpart ok"));
        }

        [Fact]
        public void Plan_Praise_ExcludesTheTargetsOwnClan_ButSlanderDoesNot()
        {
            var rig = new PlanRig();
            rig.Pool.Clear();
            rig.Pool.Add(Facts("sibling", "c3", "k1", "castle_5")); // 跟被說的人同家族
            var praise = rig.Plan("praise");
            Assert.False(praise.Success);

            var slander = rig.Plan("slander");
            Assert.Contains(slander.CandidateExplanations, e => e.StartsWith("conduct_refused_aid: available", StringComparison.Ordinal));
        }

        [Fact]
        public void Plan_WhenARealEventCanBeHungOn_OnlyTheHangingContentsAreChosen()
        {
            var rig = new PlanRig();
            rig.Real.Add(PrisonEvent("evt_p", "captor_1", "x"));
            for (long i = 0; i < 40; i++)
            {
                var r = rig.Plan("slander", i);
                Assert.True(r.Success);
                Assert.Equal(MadeUpTalkPlanner.RashCapture, r.ContentType);
                Assert.Equal("evt_p", r.LinkedEventId);
                Assert.Equal("prisoner", r.TargetRole);
                Assert.Equal("captor", r.CounterpartRole);
                Assert.Equal("captor_1", r.CounterpartHeroId);
                Assert.Equal("town_2", r.SettlementId);
            }
        }

        [Fact]
        public void Plan_TargetWasCaptor_HangsAsMistreatedPrisoner_AndTheEventWithoutAPlaceGivesNoPlace()
        {
            var rig = new PlanRig();
            rig.Real.Add(PrisonEvent("evt_p", "x", "prisoner_1", settlement: null));
            var r = rig.Plan("slander");
            Assert.Equal(MadeUpTalkPlanner.MistreatedPrisoner, r.ContentType);
            Assert.Equal("captor", r.TargetRole);
            Assert.Equal("prisoner", r.CounterpartRole);
            Assert.Null(r.SettlementId);
        }

        [Fact]
        public void Plan_ARealEventWhoseOtherPartyIsTheTellerOrTheListener_IsNotHungOn()
        {
            var byTeller = new PlanRig();
            byTeller.Real.Add(PrisonEvent("evt_t", "t", "x"));
            var r1 = byTeller.Plan("slander");
            Assert.Null(r1.LinkedEventId);
            Assert.Contains(r1.CandidateExplanations, e => e.StartsWith("conduct_rash_capture: unavailable", StringComparison.Ordinal));

            var ofListener = new PlanRig();
            ofListener.Real.Add(PrisonEvent("evt_l", "x", "l"));
            var r2 = ofListener.Plan("slander");
            Assert.Null(r2.LinkedEventId);
            Assert.Contains(r2.CandidateExplanations, e => e.StartsWith("conduct_mistreated_prisoner: unavailable", StringComparison.Ordinal));
        }

        [Fact]
        public void Plan_TheNewestOfSeveralRealEventsIsTheOneHungOn()
        {
            var rig = new PlanRig();
            rig.Real.Add(PrisonEvent("evt_old", "c_old", "x", day: 10));
            rig.Real.Add(PrisonEvent("evt_new", "c_new", "x", day: 80));
            rig.Real.Add(PrisonEvent("evt_mid", "c_mid", "x", day: 40));
            Assert.Equal("evt_new", rig.Plan("slander").LinkedEventId);
        }

        [Fact]
        public void Plan_ARealEventAlreadyLinkedToTheSameContent_IsNotHungOnAgain()
        {
            var rig = new PlanRig();
            var linked = PrisonEvent("evt_p", "captor_1", "x");
            linked.AlreadyLinkedTypes.Add(MadeUpTalkPlanner.RashCapture);
            rig.Real.Add(linked);
            var r = rig.Plan("slander");
            Assert.Null(r.LinkedEventId);
            Assert.Contains(r.CandidateExplanations, e => e.StartsWith("conduct_rash_capture: unavailable", StringComparison.Ordinal));
        }

        [Fact]
        public void Plan_Poisoned_CanAccuseOnlyWhenSameKingdomOrAGrudgeRecordExists()
        {
            // 兩樣都沒有
            var neither = new PlanRig();
            neither.Real.Add(DeathEvent("evt_d", "hero_died_of_old_age", "victim_1", kingdom: "k2"));
            Assert.Contains(neither.Plan("slander").CandidateExplanations, e => e.StartsWith("conduct_poisoned: unavailable", StringComparison.Ordinal));

            // 同王國
            var sameKingdom = new PlanRig();
            sameKingdom.Real.Add(DeathEvent("evt_d", "hero_died_of_old_age", "victim_1", kingdom: "k1"));
            var r1 = sameKingdom.Plan("slander");
            Assert.Equal(MadeUpTalkPlanner.Poisoned, r1.ContentType);
            Assert.Equal("poisoner", r1.TargetRole);
            Assert.Equal("victim", r1.CounterpartRole);
            Assert.Equal("victim_1", r1.CounterpartHeroId);
            Assert.Equal("hero_died_of_old_age", r1.LinkedTemplateType);

            // 有恩怨紀錄（不同王國）
            var grudge = new PlanRig();
            grudge.Grudge = (a, b) => a == "x" && b == "victim_1";
            grudge.Real.Add(DeathEvent("evt_d", "hero_died_naturally", "victim_1", kingdom: "k2"));
            var r2 = grudge.Plan("slander");
            Assert.Equal(MadeUpTalkPlanner.Poisoned, r2.ContentType);
            Assert.Equal("hero_died_naturally", r2.LinkedTemplateType);
        }

        [Fact]
        public void Plan_Poisoned_VictimCannotBeTheTargetHimself()
        {
            var rig = new PlanRig();
            rig.Real.Add(DeathEvent("evt_d", "hero_died_of_old_age", "x", kingdom: "k1"));
            Assert.Contains(rig.Plan("slander").CandidateExplanations, e => e.StartsWith("conduct_poisoned: unavailable", StringComparison.Ordinal));
        }

        [Fact]
        public void Plan_CalculatingTeller_PicksOnlyTheContentThatFitsTheTargetsTraits()
        {
            var rig = new PlanRig();
            rig.TellerTraits.Calculating = 1;
            rig.TargetTraits.Honor = -1;       // 背後說君主對得上（honor < 0）
            rig.TargetTraits.Generosity = 1;   // 同袍求援不肯伸手對不上
            for (long i = 0; i < 40; i++)
            {
                Assert.Equal(MadeUpTalkPlanner.SpokeAgainstRuler, rig.Plan("slander", i).ContentType);
            }

            rig.TargetTraits.Honor = 1;
            rig.TargetTraits.Generosity = -1;
            for (long i = 0; i < 40; i++)
            {
                Assert.Equal(MadeUpTalkPlanner.RefusedAid, rig.Plan("slander", i).ContentType);
            }
        }

        [Fact]
        public void Plan_CalculatingTeller_TakesTheStrongestFit()
        {
            var rig = new PlanRig();
            rig.TellerTraits.Calculating = 2;
            rig.TargetTraits.Honor = -1;
            rig.TargetTraits.Generosity = -2;
            for (long i = 0; i < 40; i++)
            {
                Assert.Equal(MadeUpTalkPlanner.RefusedAid, rig.Plan("slander", i).ContentType);
            }
        }

        [Fact]
        public void Plan_CalculatingTellerWithNoFit_FallsBackToEqualWeightRandom()
        {
            var rig = new PlanRig();
            rig.TellerTraits.Calculating = 1;
            rig.TargetTraits.Honor = 1;
            rig.TargetTraits.Generosity = 1;
            var seen = new HashSet<string?>();
            for (long i = 0; i < 80; i++) seen.Add(rig.Plan("slander", i).ContentType);
            Assert.Contains(MadeUpTalkPlanner.SpokeAgainstRuler, seen);
            Assert.Contains(MadeUpTalkPlanner.RefusedAid, seen);
        }

        [Fact]
        public void Plan_PraiseFit_UsesPositiveTraitsOfTheTarget()
        {
            var rig = new PlanRig();
            rig.TellerTraits.Calculating = 1;
            rig.TargetTraits.Honor = 2;          // 為難的人交出去（honor）對得上，等級 2
            rig.TargetTraits.Generosity = 1;     // 其餘四種 generosity，等級 1
            for (long i = 0; i < 40; i++)
            {
                var r = rig.Plan("praise", i);
                Assert.True(r.Success);
                Assert.Equal(MadeUpTalkPlanner.BrawlManHandedOver, r.ContentType);
                Assert.Equal("patron", r.TargetRole);
                Assert.Equal("aggrieved", r.CounterpartRole);
            }
        }

        [Fact]
        public void Plan_IsDeterministic_ForTheSameInstance()
        {
            var rig = new PlanRig();
            var a = rig.Plan("slander", 5);
            var b = rig.Plan("slander", 5);
            Assert.Equal(a.ContentType, b.ContentType);
            Assert.Equal(a.CounterpartHeroId, b.CounterpartHeroId);
            Assert.Equal(a.Log, b.Log);
        }

        [Fact]
        public void Plan_NothingAvailable_SaysWhyForEveryContent()
        {
            var rig = new PlanRig();
            rig.Pool.Clear();
            var r = rig.Plan("slander");
            Assert.False(r.Success);
            Assert.Equal(5, r.CandidateExplanations.Count);
            Assert.All(r.CandidateExplanations, e => Assert.Contains("unavailable", e));
            Assert.Contains("failed", r.Log);
        }

        [Fact]
        public void Plan_ContentTable_RolesMatchTheRealTemplates()
        {
            var contents = new Dictionary<string, (string Target, string Counterpart)>
            {
                [MadeUpTalkPlanner.SpokeAgainstRuler] = ("speaker", "ruler"),
                [MadeUpTalkPlanner.RefusedAid] = ("refuser", "asker"),
                [MadeUpTalkPlanner.RashCapture] = ("prisoner", "captor"),
                [MadeUpTalkPlanner.MistreatedPrisoner] = ("captor", "prisoner"),
                [MadeUpTalkPlanner.Poisoned] = ("poisoner", "victim"),
                [MadeUpTalkPlanner.VictoryCreditDeferred] = ("claimant", "rival"),
                [MadeUpTalkPlanner.AdviceGivenFreely] = ("veteran", "student"),
                [MadeUpTalkPlanner.BrawlManHandedOver] = ("patron", "aggrieved"),
                [MadeUpTalkPlanner.SeatDisputeYielded] = ("slighted", "favored"),
                [MadeUpTalkPlanner.TavernGoodWord] = ("speaker", "listener")
            };
            foreach (var kv in contents)
            {
                var t = RealTemplate(kv.Key);
                Assert.True(t.Roles.ContainsKey(kv.Value.Target), kv.Key + " has no role " + kv.Value.Target);
                Assert.True(t.Roles.ContainsKey(kv.Value.Counterpart), kv.Key + " has no role " + kv.Value.Counterpart);
                Assert.Equal("{" + kv.Value.Target.ToUpperInvariant() + "}", t.Roles[kv.Value.Target]);
                Assert.Equal("{" + kv.Value.Counterpart.ToUpperInvariant() + "}", t.Roles[kv.Value.Counterpart]);
            }

            // 兩組內容表各五種，各自掛得上的與掛不上的
            Assert.Equal(5, MadeUpTalkPlanner.SlanderContents.Length);
            Assert.Equal(5, MadeUpTalkPlanner.PraiseContents.Length);
        }

        // ───────────── 編的話版本的模板 ─────────────

        [Fact]
        public void MadeUpVariant_SpokeAgainstRuler_HasNoListenerRoleAndNoContextFact()
        {
            var baseT = RealTemplate("conduct_spoke_against_ruler");
            Assert.Contains("listener", baseT.Roles.Keys);
            Assert.Contains(baseT.Facts, f => f.Id == "context");

            var v = TemplateVariants.MadeUpSpokeAgainstRuler(baseT);
            Assert.DoesNotContain("listener", v.Roles.Keys);
            Assert.DoesNotContain(v.Facts, f => f.Id == "context");
            Assert.True(v.MadeUpHearsay);
            Assert.Equal("none", v.WitnessSource);
            Assert.Empty(v.KnowingRoles);
            Assert.Equal(EventOrigin.Public, v.Origin);
            Assert.All(v.SelfTell!.Values, r => Assert.True(r.IsNever));
            Assert.Equal(v.Roles.Keys.OrderBy(k => k), v.SelfTell!.Keys.OrderBy(k => k));
        }

        [Fact]
        public void MadeUpVariant_Poisoned_OnOldAgeKeepsTheFragments_OnAnyDeathSwapsTheWhoFragment()
        {
            var baseT = RealTemplate("conduct_poisoned");
            var oldAge = TemplateVariants.MadeUpPoisonedOldAge(baseT);
            Assert.Equal("hero_died_of_old_age", oldAge.LinkedTemplateType);
            Assert.Equal(baseT.Facts.Select(f => f.TextId), oldAge.Facts.Select(f => f.TextId));
            Assert.Equal(EventOrigin.Public, oldAge.Origin);

            var anyDeath = TemplateVariants.MadeUpPoisonedAnyDeath(baseT);
            Assert.Equal("hero_died_naturally", anyDeath.LinkedTemplateType);
            var who = anyDeath.Facts.Single(f => f.Id == "who");
            Assert.Equal("VividWorld_Fact_ConductPoisoned_WhoAnyDeath", who.TextId);
            Assert.Equal(baseT.Facts.Single(f => f.Id == "who").Vars!.Keys.OrderBy(k => k), who.Vars!.Keys.OrderBy(k => k));
            Assert.Equal(
                baseT.Facts.Where(f => f.Id != "who").Select(f => f.TextId),
                anyDeath.Facts.Where(f => f.Id != "who").Select(f => f.TextId));
        }

        [Fact]
        public void MadeUpVariant_PickerChoosesByLinkedType()
        {
            var baseT = RealTemplate("conduct_poisoned");
            Assert.Equal("hero_died_naturally", TemplateVariants.GetMadeUpVariant(baseT, "hero_died_naturally").LinkedTemplateType);
            Assert.Equal("hero_died_of_old_age", TemplateVariants.GetMadeUpVariant(baseT, "hero_died_of_old_age").LinkedTemplateType);
            Assert.True(TemplateVariants.GetMadeUpVariant(RealTemplate("conduct_refused_aid")).MadeUpHearsay);
        }

        [Fact]
        public void AllShapes_ListsTheElevenMadeUpShapes()
        {
            var labels = new List<string>();
            foreach (var cat in RealEventCatalogs.Value)
            {
                foreach (var t in cat.Templates)
                {
                    labels.AddRange(TemplateVariants.AllShapes(t).Select(s => s.Label).Where(l => l.Contains("(made up", StringComparison.Ordinal)));
                }
            }

            Assert.Equal(11, labels.Count);
            foreach (var expected in new[]
            {
                "conduct_spoke_against_ruler (made up)", "conduct_refused_aid (made up)", "conduct_rash_capture (made up)",
                "conduct_mistreated_prisoner (made up)", "conduct_poisoned (made up)", "conduct_poisoned (made up, any death)",
                "victory_credit_deferred (made up)", "advice_given_freely (made up)", "brawl_man_handed_over (made up)",
                "seat_dispute_yielded (made up)", "tavern_good_word (made up)"
            })
            {
                Assert.Contains(expected, labels);
            }
        }

        [Fact]
        public void MadeUpVariants_LeaveTheOriginalTemplateUntouched()
        {
            var baseT = RealTemplate("conduct_poisoned");
            int roles = baseT.Roles.Count;
            string? firstFact = baseT.Facts.Single(f => f.Id == "who").TextId;
            _ = TemplateVariants.MadeUpPoisonedAnyDeath(baseT);
            _ = TemplateVariants.MadeUpSpokeAgainstRuler(RealTemplate("conduct_spoke_against_ruler"));
            Assert.False(baseT.MadeUpHearsay);
            Assert.Equal(EventOrigin.Secret, baseT.Origin);
            Assert.Equal(roles, baseT.Roles.Count);
            Assert.Equal(firstFact, baseT.Facts.Single(f => f.Id == "who").TextId);
        }

        [Fact]
        public void SampleFormatter_RendersTheElevenMadeUpShapes_InBothLanguages_WithNoLeftovers()
        {
            var all = new List<EventTemplate>();
            foreach (var cat in RealEventCatalogs.Value) all.AddRange(cat.Templates);
            var en = EnglishStringTable.LoadFromFile(RepoFile("module", "ModuleData", "Languages", "std_module_strings_xml.xml"));
            var cnt = EnglishStringTable.LoadFromFile(RepoFile("module", "ModuleData", "Languages", "CNt", "std_module_strings_xml.xml"));
            string output = TemplateRenderSampleFormatter.FormatAll(all, en, cnt);

            Assert.Contains("[conduct_poisoned (made up, any death)]", output);
            Assert.Contains("[tavern_good_word (made up)]", output);
            Assert.DoesNotContain("{", output);
            Assert.Contains("Missing Sentence Keys in English (Count: 0)", output);
            Assert.Contains("Unreferenced Sentence Keys in Traditional Chinese (Count: 0)", output);
        }

        [Fact]
        public void Enumerator_MadeUpShapes_NeedOnlyTheTaillessSentences_IncludingTheFullFirstHandOne()
        {
            var baseT = RealTemplate("conduct_refused_aid");
            var made = TemplateVariants.MadeUpCommon(baseT);
            var reqs = SentenceCombinationEnumerator.EnumerateRequirements(made);

            // 沒有當事人會講、沒有人親眼看到：只剩旁人的句子，沒有「聽〈某人〉說」與當事人自己講的
            Assert.NotEmpty(reqs);
            Assert.All(reqs, r => Assert.Equal(SentenceAngleKind.Onlooker, r.Kind));
            // 第 1 手（起頭的人）講的是帶全部碎片的那一句，不能被「沒有第 0 手」省掉
            Assert.Contains(reqs, r => r.Combination.Facts.Count == made.Facts.Count);

            // 同一個模板的一般版本有當事人句
            var ordinary = SentenceCombinationEnumerator.EnumerateRequirements(baseT);
            Assert.Contains(ordinary, r => r.Kind != SentenceAngleKind.Onlooker);
        }

        // ───────────── 種知情者 ─────────────

        private static EventSubmission MadeUpSubmission()
        {
            return new EventSubmission
            {
                Type = "conduct_refused_aid",
                Day = 100,
                Origin = EventOrigin.Public,
                AutoResolveWitnesses = false,
                Participants = new Dictionary<string, string> { ["refuser"] = "x", ["asker"] = "a" },
                Facts = new List<Fact>
                {
                    new Fact { Id = "who", Category = FactCategory.Who, TextId = "k", Text = "t", Fragility = 2, IsFabricated = true }
                },
                Fabricated = true,
                OriginatorHeroId = "t",
                HearsayKnowerHeroIds = new List<string> { "t" },
                RelayKnowers = new List<RelayKnower> { new RelayKnower("l", 2, "t") }
            };
        }

        [Fact]
        public void Seed_MadeUpTalk_OriginatorHop1NoSource_ListenerHop2FromHim_NoHop0_ParticipantsUnaware()
        {
            var channel = new FakePropagationChannel();
            channel.AddWitness("x", "bystander");
            var traits = new FakeHeroTraitLookup();
            traits.Set(new TraitProfile { HeroId = "bystander", IsAlive = true, IsLord = true });
            var sub = MadeUpSubmission();
            sub.AutoResolveWitnesses = true; // 就算要求，編的話也不自動加目擊者
            var evt = new WorldEvent
            {
                EventId = "evt_m",
                Origin = EventOrigin.Public,
                Participants = new Dictionary<string, string>(sub.Participants),
                Fabricated = true,
                OriginatorHeroId = "t"
            };

            Hop0Seeding.Seed(evt, sub, channel, traits, new PropagationConfig(), "player", 100.0);

            Assert.DoesNotContain(evt.KnownBy, k => k.Hop == 0);
            var origin = evt.KnownBy.Single(k => k.HeroId == "t");
            Assert.Equal(1, origin.Hop);
            Assert.Null(origin.SourceHeroId);
            var listener = evt.KnownBy.Single(k => k.HeroId == "l");
            Assert.Equal(2, listener.Hop);
            Assert.Equal("t", listener.SourceHeroId);
            Assert.Contains("t", listener.HeardFromIds ?? new List<string>());
            Assert.DoesNotContain(evt.KnownBy, k => k.HeroId == "x" || k.HeroId == "a" || k.HeroId == "bystander");
            Assert.Equal(2, evt.KnownBy.Count);
        }

        [Fact]
        public void Seed_MadeUpTalk_KnowingRolesAreIgnored()
        {
            var channel = new FakePropagationChannel();
            var traits = new FakeHeroTraitLookup();
            var sub = MadeUpSubmission();
            sub.KnowingRoles = new HashSet<string> { "refuser" };
            var evt = new WorldEvent { EventId = "evt_m", Origin = EventOrigin.Public, Participants = new Dictionary<string, string>(sub.Participants) };
            Hop0Seeding.Seed(evt, sub, channel, traits, new PropagationConfig(), "player", 100.0);
            Assert.DoesNotContain(evt.KnownBy, k => k.HeroId == "x");
        }

        [Fact]
        public void Seed_BoastWithMadeUpBy_StillSeedsTheSpeakerAtHop0()
        {
            var channel = new FakePropagationChannel();
            var traits = new FakeHeroTraitLookup();
            var sub = new EventSubmission
            {
                Type = "tavern_boast_told",
                Day = 100,
                Origin = EventOrigin.Public,
                AutoResolveWitnesses = false,
                Participants = new Dictionary<string, string> { ["speaker"] = "s", ["listener"] = "l" },
                Fabricated = true,
                OriginatorHeroId = "s",
                MadeUpBy = "speaker"
            };
            var evt = new WorldEvent { EventId = "evt_b", Origin = EventOrigin.Public, Participants = new Dictionary<string, string>(sub.Participants) };
            Hop0Seeding.Seed(evt, sub, channel, traits, new PropagationConfig(), "player", 100.0);
            Assert.Contains(evt.KnownBy, k => k.HeroId == "s" && k.Hop == 0);
        }

        [Fact]
        public void Validator_FabricatedSubmission_RequiresAnOriginatorWhoIsHearsayAndNotAParticipant()
        {
            var cfg = new PersistenceConfig();

            Assert.True(EventSubmissionValidator.Validate(MadeUpSubmission(), cfg, _ => false).IsValid);

            var noOriginator = MadeUpSubmission();
            noOriginator.OriginatorHeroId = null;
            Assert.False(EventSubmissionValidator.Validate(noOriginator, cfg, _ => false).IsValid);

            var notHearsay = MadeUpSubmission();
            notHearsay.HearsayKnowerHeroIds = new List<string> { "someone_else" };
            Assert.False(EventSubmissionValidator.Validate(notHearsay, cfg, _ => false).IsValid);

            var participant = MadeUpSubmission();
            participant.OriginatorHeroId = "x";
            participant.HearsayKnowerHeroIds = new List<string> { "x" };
            Assert.False(EventSubmissionValidator.Validate(participant, cfg, _ => false).IsValid);

            var initial = MadeUpSubmission();
            initial.InitialKnowerHeroIds = new List<string> { "w" };
            Assert.False(EventSubmissionValidator.Validate(initial, cfg, _ => false).IsValid);

            // 誇戰功（madeUpBy）不走這條路
            var boast = MadeUpSubmission();
            boast.MadeUpBy = "speaker";
            boast.OriginatorHeroId = "x";
            boast.HearsayKnowerHeroIds = new List<string>();
            Assert.True(EventSubmissionValidator.Validate(boast, cfg, _ => false).IsValid);
        }

        // ───────────── 誰知道是誰起頭的 ─────────────

        private static WorldEvent HearsayEvent() => new WorldEvent
        {
            EventId = "evt_m",
            Type = "conduct_refused_aid",
            Fabricated = true,
            OriginatorHeroId = "t",
            Participants = new Dictionary<string, string> { ["refuser"] = "x", ["asker"] = "a" }
        };

        [Theory]
        [InlineData(1, "t", false)]  // 起頭的人自己不算
        [InlineData(2, "l", true)]   // 第 2 手：直接從他那裡聽來
        [InlineData(3, "m", true)]   // 第 3 手：聽到的是「聽〈起頭的人〉說」
        [InlineData(4, "n", false)]  // 第 4 手以後只聽到「聽人說」
        [InlineData(5, "o", false)]
        public void KnowsOriginator_ByHop(int hop, string hearer, bool expected)
        {
            var evt = HearsayEvent();
            Assert.Equal(expected, MadeUpTalk.KnowsOriginator(evt, hearer, hop));
            Assert.Equal(expected, MadeUpTalk.KnowsOriginator(evt, new KnownByEntry { HeroId = hearer, Hop = hop }));
        }

        [Fact]
        public void KnowsOriginator_OriginatorAtHop2Or3_IsStillNotCounted_AndOrdinaryEventsHaveNone()
        {
            var evt = HearsayEvent();
            Assert.False(MadeUpTalk.KnowsOriginator(evt, "t", 2));
            Assert.False(MadeUpTalk.KnowsOriginator(new WorldEvent { OriginatorHeroId = null }, "l", 2));
            evt.Fabricated = false;
            Assert.False(MadeUpTalk.KnowsOriginator(evt, "l", 2));
        }

        [Fact]
        public void OriginatorGrudge_IsHalfOfTheScoreAsAbsolute_ForBadAndGoodTalkAlike()
        {
            Assert.Equal(-5.0, MadeUpTalk.OriginatorGrudge(-10));
            Assert.Equal(-3.0, MadeUpTalk.OriginatorGrudge(6));
            Assert.Equal(-1.25, MadeUpTalk.OriginatorGrudge(-2.5));
        }

        [Fact]
        public void SelfTell_ParticipantsOfMadeUpTalk_NeverSpeakOfIt_ButTheOriginatorMay()
        {
            var evt = HearsayEvent();
            Func<string, EventTemplate?> get = type => RealTemplate(type);

            var participant = SelfTellEvaluator.Evaluate(evt, "x", get, new TraitProfile { HeroId = "x" });
            Assert.False(participant.CanTell);
            Assert.True(participant.IsNever);
            Assert.Equal("refuser", participant.Role);

            var asker = SelfTellEvaluator.Evaluate(evt, "a", get, new TraitProfile { HeroId = "a" });
            Assert.False(asker.CanTell);

            Assert.True(SelfTellEvaluator.Evaluate(evt, "t", get, new TraitProfile { HeroId = "t" }).CanTell);
            Assert.True(SelfTellEvaluator.Evaluate(evt, "l", get, new TraitProfile { HeroId = "l" }).CanTell);
        }

        // ───────────── 誇戰功與結算哪幾條 ─────────────

        [Fact]
        public void Boast_RealTemplate_HasMadeUpByAndTwoOpinionsWithWhen()
        {
            var t = RealTemplate("tavern_boast_told");
            Assert.Equal("speaker", t.MadeUpBy);
            Assert.Equal(2, t.Opinions!.Count);
            Assert.Equal(("speaker", 2.0, "valor", "believed"), (t.Opinions[0].About, t.Opinions[0].Amount, t.Opinions[0].Trait, t.Opinions[0].When));
            Assert.Equal(("speaker", -3.0, "honor", "disbelieved"), (t.Opinions[1].About, t.Opinions[1].Amount, t.Opinions[1].Trait, t.Opinions[1].When));
        }

        [Fact]
        public void OpinionSelector_FabricatedBoast_BelievedGivesPlus2_DisbelievedGivesMinus3()
        {
            var ops = RealTemplate("tavern_boast_told").Opinions!;
            var believed = OpinionSelector.ToSettle(ops, fabricated: true, believes: true);
            Assert.Single(believed);
            Assert.Equal(2.0, believed[0].Amount);

            var disbelieved = OpinionSelector.ToSettle(ops, fabricated: true, believes: false);
            Assert.Single(disbelieved);
            Assert.Equal(-3.0, disbelieved[0].Amount);
        }

        [Fact]
        public void OpinionSelector_OldBoastEvent_StaysMinus3_AndDisbeliefSettlesNothing()
        {
            var ops = RealTemplate("tavern_boast_told").Opinions!;
            var believed = OpinionSelector.ToSettle(ops, fabricated: false, believes: true);
            Assert.Single(believed);
            Assert.Equal(-3.0, believed[0].Amount);

            Assert.Empty(OpinionSelector.ToSettle(ops, fabricated: false, believes: false));
        }

        [Fact]
        public void OpinionSelector_PlainOpinions_BehaveAsBefore_AndFabricatedDisbeliefSettlesNothingWithoutAWhen()
        {
            var plain = new List<OpinionDef> { new OpinionDef { About = "refuser", Amount = -4.0, Trait = "generosity" } };
            Assert.Single(OpinionSelector.ToSettle(plain, fabricated: false, believes: true));
            Assert.Empty(OpinionSelector.ToSettle(plain, fabricated: false, believes: false));
            Assert.Single(OpinionSelector.ToSettle(plain, fabricated: true, believes: true));
            Assert.Empty(OpinionSelector.ToSettle(plain, fabricated: true, believes: false));
        }

        [Fact]
        public void OpinionSelector_Subject_IsTheFirstOneThatSettlesWhenBelieved()
        {
            var ops = RealTemplate("tavern_boast_told").Opinions!;
            Assert.Equal(2.0, OpinionSelector.Subject(ops, fabricated: true)!.Amount);
            Assert.Equal(-3.0, OpinionSelector.Subject(ops, fabricated: false)!.Amount);
            Assert.Null(OpinionSelector.Subject(null, true));
        }

        [Fact]
        public void Loader_OpinionWhen_AcceptsOnlyBelievedOrDisbelieved()
        {
            string Json(string when) => "[ { \"type\": \"t1\", \"origin\": \"public\", \"headline\": \"h\", \"roles\": { \"a\": \"{A}\" }, " +
                "\"opinion\": [ { \"about\": \"a\", \"amount\": 1, \"trait\": \"valor\", \"when\": \"" + when + "\" } ], " +
                "\"facts\": [ { \"id\": \"who\", \"category\": \"WHO\", \"textId\": \"k\", \"text\": \"{A}\", \"vars\": { \"A\": \"hero:{A}\" }, \"fragility\": 2 } ] } ]";

            var ok = EventCatalogLoader.Load(Json("believed"), new PersistenceConfig());
            Assert.DoesNotContain(ok.Issues, i => i.IsError);
            Assert.Equal("believed", ok.ByType("t1")!.Opinions![0].When);

            var bad = EventCatalogLoader.Load(Json("maybe"), new PersistenceConfig());
            Assert.Contains(bad.Issues, i => i.IsError && i.Field.EndsWith(".when", StringComparison.Ordinal));
        }

        [Fact]
        public void Loader_MadeUpBy_MustBeADeclaredRole()
        {
            string Json(string role) => "[ { \"type\": \"t1\", \"origin\": \"public\", \"headline\": \"h\", \"roles\": { \"a\": \"{A}\" }, \"madeUpBy\": \"" + role + "\", " +
                "\"facts\": [ { \"id\": \"who\", \"category\": \"WHO\", \"textId\": \"k\", \"text\": \"{A}\", \"vars\": { \"A\": \"hero:{A}\" }, \"fragility\": 2 } ] } ]";

            var ok = EventCatalogLoader.Load(Json("a"), new PersistenceConfig());
            Assert.DoesNotContain(ok.Issues, i => i.IsError);
            Assert.Equal("a", ok.ByType("t1")!.MadeUpBy);

            var bad = EventCatalogLoader.Load(Json("ghost"), new PersistenceConfig());
            Assert.Contains(bad.Issues, i => i.Code == CatalogIssueCode.MadeUpByInvalid && i.IsError);
        }

        // ───────────── 信不信：交情反過來、理由看方向 ─────────────

        private static BeliefInputs Inputs(double amount, int? subjectAffection, int? tellerAffection = null, int hop = 2, int calculating = 0)
            => new BeliefInputs
            {
                CampaignSeed = 1,
                EventId = "evt_1",
                HearerId = "h",
                SubjectId = "s",
                TellerId = "t",
                Hop = hop,
                SubjectAffection = subjectAffection,
                TellerAffection = tellerAffection,
                OpinionAmount = amount,
                ListenerCalculating = calculating
            };

        [Theory]
        [InlineData(40, 1)]    // 交情深 ≥ 30：好事反而更信
        [InlineData(20, 1)]    // 10～29
        [InlineData(-30, -1)]  // ≤ -20：好事更不信
        [InlineData(0, 0)]
        public void SubjectTerm_ForGoodNews_FlipsTheSignOfTheBadNewsValue(int affection, int sign)
        {
            var cfg = new BeliefConfig();
            double bad = BeliefJudge.SubjectTerm(affection, cfg, -5.0);
            double good = BeliefJudge.SubjectTerm(affection, cfg, 5.0);
            Assert.Equal(-bad, good);
            if (sign == 0) Assert.Equal(0.0, good); else Assert.Equal(sign, Math.Sign(good));
        }

        [Fact]
        public void SubjectTerm_ForGoodNews_UsesTheNegatedExistingSettings()
        {
            var cfg = new BeliefConfig();
            Assert.Equal(-cfg.SubjectFriendDelta, BeliefJudge.SubjectTerm(cfg.SubjectRelationFriend, cfg, 3.0));
            Assert.Equal(-cfg.SubjectWarmDelta, BeliefJudge.SubjectTerm(cfg.SubjectRelationWarm, cfg, 3.0));
            Assert.Equal(-cfg.SubjectHostileDelta, BeliefJudge.SubjectTerm(cfg.SubjectRelationHostile, cfg, 3.0));
            Assert.Equal(cfg.SubjectFriendDelta, BeliefJudge.SubjectTerm(cfg.SubjectRelationFriend, cfg, -3.0));
        }

        [Fact]
        public void Judge_GoodNewsAboutAFriend_IsMoreCredibleThanAboutAnEnemy()
        {
            var cfg = new BeliefConfig();
            var rng = new SplitMix64Rng();
            var friend = BeliefJudge.Judge(Inputs(6, 50), cfg, rng);
            var enemy = BeliefJudge.Judge(Inputs(6, -50), cfg, rng);
            Assert.True(friend.RawChance > enemy.RawChance);
            Assert.Equal(-cfg.SubjectFriendDelta, friend.SubjectRelation);
        }

        [Fact]
        public void Heaviest_BelievedPicksTheLargestPositive_DisbelievedTheLargestNegative_NoneWhenNoneAgree()
        {
            var r = new BeliefResult { Believes = true, SubjectRelation = -40, TellerRelation = 15, TraitFit = 20 };
            Assert.Equal(BeliefReason.TraitFit, BeliefJudge.HeaviestOf(r));

            r.Believes = false;
            Assert.Equal(BeliefReason.SubjectRelation, BeliefJudge.HeaviestOf(r));

            var onlyPositive = new BeliefResult { Believes = false, TellerRelation = 15, TraitFit = 20 };
            Assert.Equal(BeliefReason.None, BeliefJudge.HeaviestOf(onlyPositive));

            var onlyNegative = new BeliefResult { Believes = true, SubjectRelation = -40 };
            Assert.Equal(BeliefReason.None, BeliefJudge.HeaviestOf(onlyNegative));

            Assert.Equal(BeliefReason.None, BeliefJudge.HeaviestOf(new BeliefResult { Believes = true }));
        }

        // ───────────── 不信的感想 ─────────────

        private sealed class StubWorld : IFeelingWorld
        {
            public double Today => 100.0;
            public int? Affection(string speakerId, string heroId) => 0;
            public int? StandingRank(string heroId) => 0;
            public InterestHeroFacts? InterestFacts(string heroId) => new InterestHeroFacts(heroId, clanId: null);
            public IReadOnlyList<GrudgeEntry> PersonalGrudges(string speakerId, string heroId) => new List<GrudgeEntry>();
            public string NameOf(string heroId) => heroId;
            public bool? IsFemale(string heroId) => null;
        }

        private static FeelingDecision ResolveDisbelief(string eventId, bool believes, string speaker = "l", bool gist = false)
        {
            var catalog = FeelingCatalog.Parse(File.ReadAllText(RepoFile("module", "ModuleData", "vividworld_feelings.json")));
            var resolver = new FeelingResolver(new VividWorldConfig(), catalog, new StubWorld(), new FakeHeroTraitLookup(), RealTemplate, 7);
            var evt = HearsayEvent();
            evt.EventId = eventId;
            evt.KnownBy = new List<KnownByEntry>
            {
                new KnownByEntry { HeroId = "t", Hop = 1 },
                new KnownByEntry { HeroId = speaker, Hop = 2, SourceHeroId = "t", Believes = believes }
            };
            return resolver.Resolve(evt, speaker, gist);
        }

        [Fact]
        public void Feeling_SpeakerWhoDoesNotBelieve_GetsOneOfTheThreeDisbeliefLines_WithNoAddress()
        {
            var keys = new HashSet<string?>();
            for (int i = 0; i < 60; i++)
            {
                var d = ResolveDisbelief("evt_" + i, believes: false);
                Assert.True(d.Applied);
                Assert.Equal("disbelief", d.Category);
                Assert.Null(d.AddressKey);
                Assert.Contains("disbelief", d.LogLine);
                keys.Add(d.LineKey);
            }

            Assert.Equal(new HashSet<string?>
            {
                "VividWorld_FeelingDisbelief_1", "VividWorld_FeelingDisbelief_2", "VividWorld_FeelingDisbelief_3"
            }, keys);
        }

        [Fact]
        public void Feeling_DisbeliefRotation_IsDeterministicPerEventAndSpeaker()
        {
            Assert.Equal(ResolveDisbelief("evt_9", false).LineKey, ResolveDisbelief("evt_9", false).LineKey);
        }

        [Fact]
        public void Feeling_SpeakerWhoBelieves_DoesNotGetADisbeliefLine()
        {
            var d = ResolveDisbelief("evt_1", believes: true);
            Assert.DoesNotContain("Disbelief", d.LineKey ?? "");
        }

        [Fact]
        public void Feeling_DisbeliefLines_AreInBothStringTables()
        {
            foreach (var path in new[]
            {
                RepoFile("module", "ModuleData", "Languages", "std_module_strings_xml.xml"),
                RepoFile("module", "ModuleData", "Languages", "CNt", "std_module_strings_xml.xml")
            })
            {
                string text = File.ReadAllText(path);
                for (int i = 1; i <= 3; i++) Assert.Contains("id=\"VividWorld_FeelingDisbelief_" + i + "\"", text);
            }
        }

        // ───────────── 不洩漏 ─────────────

        private static WorldEvent LeakyEvent()
        {
            var evt = HearsayEvent();
            evt.SituationId = "madeup_slander";
            evt.Facts = new List<Fact>
            {
                new Fact { Id = "who", Category = FactCategory.Who, TextId = "k", Text = "t", Fragility = 2, IsFabricated = true }
            };
            return evt;
        }

        [Fact]
        public void PublicJson_DropsTheThreeFields_AndTheSituationIdOfHearsayOnlyEvents()
        {
            var evt = LeakyEvent();
            Assert.Contains("fabricated", VividJson.Write(evt), StringComparison.OrdinalIgnoreCase); // 存檔裡有

            string json = PublicEventSanitizer.SanitizeJson(evt);
            Assert.DoesNotContain("fabricated", json, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("originatorHeroId", json, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("madeup_slander", json);
            Assert.DoesNotContain("situationId", json, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("conduct_refused_aid", json);
            Assert.NotNull(JObject.Parse(json)["facts"]);
        }

        [Fact]
        public void PublicJson_OfARealEvent_KeepsItsSituationId_AndMatchesThePlainSerialization()
        {
            var evt = new WorldEvent { EventId = "evt_r", Type = "conduct_refused_aid", SituationId = "refused_aid", Participants = new Dictionary<string, string> { ["refuser"] = "x" } };
            string json = PublicEventSanitizer.SanitizeJson(evt);
            Assert.Contains("refused_aid", json);
            Assert.Equal(VividJson.Write(evt), json);
        }

        [Fact]
        public void PublicJson_ABoastThatIsFabricated_KeepsItsSituationIdButNotTheFlags()
        {
            var evt = new WorldEvent
            {
                EventId = "evt_b",
                Type = "tavern_boast_told",
                SituationId = "tavern_boast",
                Fabricated = true,
                OriginatorHeroId = "s",
                Participants = new Dictionary<string, string> { ["speaker"] = "s" }
            };
            string json = PublicEventSanitizer.SanitizeJson(evt);
            Assert.Contains("tavern_boast", json);
            Assert.DoesNotContain("fabricated", json, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("originatorHeroId", json, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void PlayerFacingCode_NeverReadsTheMadeUpFlags()
        {
            // 紀事、對話句子、AI 推送、玩家看得到的字都不准讀這三個欄位
            string src = RepoFile("src");
            var folders = new[]
            {
                Path.Combine(src, "VividWorld.Core", "Presentation"),
                Path.Combine(src, "VividWorld.Core", "Dialogue"),
                Path.Combine(src, "VividWorld.Core", "Ai"),
                Path.Combine(src, "VividWorld.Module", "Ai"),
                Path.Combine(src, "VividWorld.Module", "AI"),
                Path.Combine(src, "VividWorld.Module", "Ui")
            };

            var offenders = new List<string>();
            foreach (var folder in folders.Where(Directory.Exists))
            {
                foreach (var file in Directory.EnumerateFiles(folder, "*.cs", SearchOption.AllDirectories))
                {
                    string text = File.ReadAllText(file);
                    if (text.Contains("Fabricated") || text.Contains("OriginatorHeroId") || text.Contains("IsHearsayOnly"))
                    {
                        offenders.Add(Path.GetFileName(file));
                    }
                }
            }

            Assert.Empty(offenders);
        }

        [Fact]
        public void HearsayOnlyEvent_DoesNotRaiseEventOccurred_Rule()
        {
            // 這條規則在遊戲組件裡（WorldEventStore.TriggerPublicEventOccurred）呼叫同一個判斷；這裡鎖住判斷本身
            Assert.True(MadeUpTalk.IsHearsayOnly(LeakyEvent()));
            var boast = LeakyEvent();
            boast.Participants["speaker"] = boast.OriginatorHeroId!;
            Assert.False(MadeUpTalk.IsHearsayOnly(boast));
        }
    }
}
