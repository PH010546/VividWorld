using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using VividWorld.Core.Catalog;
using VividWorld.Core.Config;
using VividWorld.Core.Events;
using VividWorld.Core.Persistence;
using VividWorld.Core.Rumors;
using VividWorld.Core.Secrets;
using VividWorld.Core.Tests.Fakes;
using VividWorld.Core.Util;
using Xunit;

namespace VividWorld.Core.Tests.Secrets
{
    public class OldSecretConversionTests
    {
        private static string FindRepoRoot()
        {
            string? current = AppContext.BaseDirectory;
            while (!string.IsNullOrEmpty(current))
            {
                if (File.Exists(Path.Combine(current, "VividWorld.sln")))
                {
                    return current;
                }
                var parent = Directory.GetParent(current);
                if (parent == null) break;
                current = parent.FullName;
            }
            throw new DirectoryNotFoundException("Could not find VividWorld.sln directory");
        }

        private static string GetOldCampaignFixtureDir()
        {
            string repoRoot = FindRepoRoot();
            return Path.Combine(repoRoot, "tests", "VividWorld.Core.Tests", "Fixtures", "OldCampaign");
        }

        [Fact]
        public void Convert_RealOldCampaignData_RoundTripsAndMatchesRulesExactly()
        {
            string fixtureDir = GetOldCampaignFixtureDir();
            Assert.True(Directory.Exists(fixtureDir), $"Fixture directory must exist at {fixtureDir}");

            // Create an isolated sandbox directory for reading, converting, and writing
            string sandboxDir = Path.Combine(Path.GetTempPath(), "vw_test_old_campaign_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(sandboxDir);

            try
            {
                // Copy fixture files into sandbox
                foreach (var file in Directory.GetFiles(fixtureDir, "*.json"))
                {
                    File.Copy(file, Path.Combine(sandboxDir, Path.GetFileName(file)), overwrite: true);
                }

                var writer = new SystemFileWriter();
                var store = new EventShardStore(sandboxDir, 100, writer);
                var index = store.LoadIndex();
                Assert.NotNull(index);
                Assert.Equal(19, index.Entries.Count);

                // Load all 19 real secret events
                var originalEvents = new List<WorldEvent>();
                foreach (var entry in index.Entries)
                {
                    var evt = store.Load(entry.EventId, index);
                    Assert.NotNull(evt);
                    originalEvents.Add(evt!);
                }
                Assert.Equal(19, originalEvents.Count);

                // Mock settlement clan leader lookup
                var settlementOwners = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["town_S1"] = "lord_S_clan_leader",
                    ["town_B1"] = "lord_B_clan_leader",
                    ["town_K1"] = "lord_K_clan_leader"
                };

                // Run conversion with applyInPlace: true
                var plan = OldSecretConverter.Plan(
                    originalEvents,
                    playerHeroId: "main_hero_id",
                    settlementOwnerClanLeaderLookup: sId => settlementOwners.TryGetValue(sId, out var oId) ? oId : null,
                    getTemplate: null,
                    isEventIdTaken: id => index.Find(id) != null,
                    applyInPlace: true);

                // Assert plan results
                Assert.Equal(19, plan.TotalProcessed);
                Assert.Equal(5, plan.ConvertedCount); // 5 seat_dispute_endured converted
                Assert.Equal(10, plan.RetiredCount);   // 9 wager_secret_stake + 1 hero_murdered retired
                Assert.Equal(4, plan.UnchangedCount); // 3 leaked seat_dispute + 1 leaked wager unchanged

                // Apply changes to store
                foreach (var item in plan.Items)
                {
                    if (item.ConvertedEvent != null &&
                        item.Action != SecretConversionAction.UnchangedAlreadyLeaked &&
                        item.Action != SecretConversionAction.UnchangedUnrecognized)
                    {
                        store.Upsert(item.ConvertedEvent);
                    }
                    if (item.GeneratedGrudgeEvent != null)
                    {
                        store.Upsert(item.GeneratedGrudgeEvent);
                    }
                }

                // Flush changes to disk
                store.Flush();

                // ── Verify disk round-trip ──
                // Re-instantiate a fresh EventShardStore pointing to the sandbox and reload index from disk
                var freshStore = new EventShardStore(sandboxDir, 100, writer);
                var reloadedIndex = freshStore.LoadIndex();
                Assert.NotNull(reloadedIndex);

                // 19 original events + 5 new seat_dispute_grudge_held events = 24 entries
                Assert.Equal(24, reloadedIndex.Entries.Count);

                // Verify the 4 leaked events are still secret and untouched
                var leakedIds = new[] { "evt_91089_ff54", "evt_91142_8c0e", "evt_91173_15a8", "evt_91255_fa32" };
                foreach (var lid in leakedIds)
                {
                    var entry = reloadedIndex.Find(lid);
                    Assert.NotNull(entry);
                    Assert.True(entry!.Secret);
                    Assert.True(entry.Leaked);

                    var evt = freshStore.Load(lid, reloadedIndex);
                    Assert.NotNull(evt);
                    Assert.Equal(EventOrigin.Secret, evt!.Origin);
                    Assert.False(evt.SecretRetired);
                }

                // Verify the 5 unleaked seat_dispute_endured are now Public, have witnesses, and have a linked grudge held event
                var enduredIds = new[] { "evt_91188_c657", "evt_91215_3d80", "evt_91219_3db8", "evt_91247_0c02", "evt_91258_56d0" };
                foreach (var eid in enduredIds)
                {
                    var entry = reloadedIndex.Find(eid);
                    Assert.NotNull(entry);
                    Assert.False(entry!.Secret, $"Event {eid} should be Public in index");

                    var evt = freshStore.Load(eid, reloadedIndex);
                    Assert.NotNull(evt);
                    Assert.Equal(EventOrigin.Public, evt!.Origin);
                    Assert.False(evt.SecretRetired);

                    // Witnesses in KnownBy
                    string slighted = evt.Participants["slighted"];
                    string favored = evt.Participants["favored"];
                    Assert.True(evt.IsKnownBy(slighted));
                    Assert.True(evt.IsKnownBy(favored));

                    var slightedKnower = evt.EntryFor(slighted);
                    Assert.Equal(0, slightedKnower!.Hop);

                    var favoredKnower = evt.EntryFor(favored);
                    Assert.Equal(0, favoredKnower!.Hop);

                    // Find corresponding grudge held event in reloadedIndex
                    var grudgeEntry = reloadedIndex.Entries.FirstOrDefault(e => e.LinkedEventId == eid);
                    Assert.NotNull(grudgeEntry);
                    Assert.Equal("seat_dispute_grudge_held", grudgeEntry!.Type);
                    Assert.True(grudgeEntry.Secret);
                    Assert.False(grudgeEntry.Leaked);

                    var grudgeEvt = freshStore.Load(grudgeEntry.EventId, reloadedIndex);
                    Assert.NotNull(grudgeEvt);
                    Assert.Equal(EventOrigin.Secret, grudgeEvt!.Origin);
                    Assert.Equal(eid, grudgeEvt.LinkedEventId);
                    Assert.Equal(evt.Day, grudgeEvt.Day);
                    Assert.NotNull(grudgeEvt.SecretOwnerHeroIds);
                    Assert.Contains(slighted, grudgeEvt.SecretOwnerHeroIds!);
                    Assert.Single(grudgeEvt.KnownBy);
                    Assert.Equal(slighted, grudgeEvt.KnownBy[0].HeroId);
                    Assert.Equal(0, grudgeEvt.KnownBy[0].Hop);
                }

                // Verify the 10 retired events (9 wager + 1 murder)
                var retiredIds = new[]
                {
                    "evt_91095_9fd0", "evt_91141_e1ef", "evt_91195_5879", "evt_91200_f823",
                    "evt_91240_fcf5", "evt_91253_861c", "evt_91257_e49b", "evt_91265_3c72", "evt_91266_de77",
                    "evt_91249_4d04"
                };
                foreach (var rid in retiredIds)
                {
                    var entry = reloadedIndex.Find(rid);
                    Assert.NotNull(entry);
                    Assert.True(entry!.Secret);
                    Assert.False(entry.Leaked);

                    var evt = freshStore.Load(rid, reloadedIndex);
                    Assert.NotNull(evt);
                    Assert.Equal(EventOrigin.Secret, evt!.Origin);
                    Assert.True(evt.SecretRetired, $"Event {rid} should be retired");
                }
            }
            finally
            {
                if (Directory.Exists(sandboxDir))
                {
                    try { Directory.Delete(sandboxDir, recursive: true); } catch { }
                }
            }
        }

        [Fact]
        public void PlayerEndured_ConvertsToPublic_WithoutGeneratingHiddenPiece()
        {
            var evt = new WorldEvent
            {
                EventId = "evt_player_endured_001",
                Type = "seat_dispute_endured",
                Origin = EventOrigin.Secret,
                Day = 100.0,
                Participants = new Dictionary<string, string>
                {
                    ["slighted"] = "player_hero",
                    ["favored"] = "lord_rival"
                },
                Facts = new List<Fact>
                {
                    new Fact
                    {
                        Id = "where",
                        Category = FactCategory.Where,
                        Text = "in hall",
                        Vars = new Dictionary<string, string> { ["SETTLEMENT"] = "settlement:town_A1" }
                    }
                },
                KnownBy = new List<KnownByEntry>
                {
                    new KnownByEntry { HeroId = "player_hero", Hop = 0, LearnedDay = 100.0 }
                }
            };

            var plan = OldSecretConverter.Plan(
                new[] { evt },
                playerHeroId: "player_hero",
                settlementOwnerClanLeaderLookup: _ => "lord_host",
                applyInPlace: true);

            Assert.Single(plan.Items);
            var item = plan.Items[0];
            Assert.Equal(SecretConversionAction.EnduredPlayerToPublic, item.Action);
            Assert.Null(item.GeneratedGrudgeEvent);
            Assert.Equal(EventOrigin.Public, evt.Origin);
            Assert.False(evt.SecretRetired);

            // Witnesses: player, rival, host
            Assert.Equal(3, evt.KnownBy.Count);
            Assert.True(evt.IsKnownBy("player_hero"));
            Assert.True(evt.IsKnownBy("lord_rival"));
            Assert.True(evt.IsKnownBy("lord_host"));
        }

        [Fact]
        public void Endured_MadePublic_CountsAsFreshFromConversionDay()
        {
            var evt = new WorldEvent
            {
                EventId = "evt_old",
                Type = "seat_dispute_endured",
                Day = 100.0,
                Origin = EventOrigin.Secret,
                Participants = new Dictionary<string, string> { ["slighted"] = "hero_a", ["favored"] = "hero_b" }
            };

            OldSecretConverter.Plan(new[] { evt }, "main_hero", applyInPlace: true, conversionDay: 250.0);

            Assert.Equal(EventOrigin.Public, evt.Origin);
            Assert.Equal(250.0, evt.State.LastNewKnowerDay);
        }

        [Fact]
        public void ConductPoisoned_NoPoisonerRecorded_LeftUnchanged()
        {
            var evt = new WorldEvent
            {
                EventId = "evt_old",
                Type = "conduct_poisoned",
                Day = 100.0,
                Origin = EventOrigin.Secret
            };

            var plan = OldSecretConverter.Plan(new[] { evt }, "main_hero", applyInPlace: true);

            Assert.Equal(SecretConversionAction.UnchangedUnrecognized, plan.Items[0].Action);
            Assert.Null(evt.SecretOwnerHeroIds);
        }

        [Fact]
        public void ConductPoisoned_AddsSecretOwnerHeroId()
        {
            var evt = new WorldEvent
            {
                EventId = "evt_poison_001",
                Type = "conduct_poisoned",
                Origin = EventOrigin.Secret,
                Day = 50.0,
                Participants = new Dictionary<string, string>
                {
                    ["poisoner"] = "lord_assassin",
                    ["victim"] = "lord_victim"
                },
                KnownBy = new List<KnownByEntry>
                {
                    new KnownByEntry { HeroId = "lord_assassin", Hop = 0, LearnedDay = 50.0 }
                }
            };

            var plan = OldSecretConverter.Plan(new[] { evt }, playerHeroId: "player", applyInPlace: true);

            Assert.Single(plan.Items);
            var item = plan.Items[0];
            Assert.Equal(SecretConversionAction.PoisonedAddedOwner, item.Action);
            Assert.NotNull(evt.SecretOwnerHeroIds);
            Assert.Contains("lord_assassin", evt.SecretOwnerHeroIds!);
            Assert.Equal(EventOrigin.Secret, evt.Origin);
            Assert.False(evt.SecretRetired);
        }

        [Fact]
        public void RetiredSecrets_DoNotLeak_InRumorEngine_AndSecretSpread()
        {
            var retiredEvt = new WorldEvent
            {
                EventId = "evt_retired_leak_test",
                Type = "wager_secret_stake",
                Origin = EventOrigin.Secret,
                SecretRetired = true,
                Day = 10.0,
                KnownBy = new List<KnownByEntry>
                {
                    new KnownByEntry { HeroId = "hero_insider", Hop = 0, LearnedDay = 10.0 }
                }
            };

            // 1. RumorEngine.TryLeak must return LeakOutcome.None
            var cfg = new VividWorldConfig();
            cfg.Leak.BaseChancePerInsiderPerDay = 1.0; // 100% leak chance if rolled
            cfg.Leak.GraceDays = 0.0;
            var rng = new SplitMix64Rng();
            var retention = FactRetentionPolicies.Create(cfg, rng, 12345);
            var engine = new RumorEngine(cfg, retention, NullEmbellishmentPolicy.Instance, new FakePropagationChannel(), new FakeHeroTraitLookup(), rng, 12345, "player");

            var leakOutcome = engine.TryLeak(retiredEvt, 15.0);
            Assert.False(leakOutcome.Leaked);

            // 2. SecretSpread.Evaluate must return unrolled reason "retired"
            var knowers = new List<SecretKnowerContext>
            {
                new SecretKnowerContext
                {
                    HeroId = "hero_insider",
                    Traits = new TraitProfile { IsAlive = true, Honor = -1 }
                }
            };
            var spreadEval = SecretSpread.Evaluate(retiredEvt, 15.0, knowers, Array.Empty<string>(), cfg, rng, 12345, "player");
            Assert.Single(spreadEval.Results);
            Assert.Equal(SecretSpreadAction.NotRolled, spreadEval.Results[0].Action);
            Assert.Equal("retired", spreadEval.Results[0].UnrolledReason);

            // 3. SecretVisibilityPolicy must hide it from player
            retiredEvt.KnownBy.Add(new KnownByEntry { HeroId = "player", Hop = 1, LearnedDay = 12.0 });
            bool playerSees = SecretVisibilityPolicy.IsHiddenPartVisibleToPlayer(retiredEvt, "player");
            Assert.False(playerSees);
        }

        [Fact]
        public void ForwardCompatibility_OlderVwReadsConvertedEvents_PreservingExtraFields()
        {
            // Create a converted event JSON containing SecretOwnerHeroIds and SecretRetired
            var convertedEvt = new WorldEvent
            {
                EventId = "evt_converted_001",
                Type = "conduct_poisoned",
                Origin = EventOrigin.Secret,
                Day = 75.0,
                SecretOwnerHeroIds = new List<string> { "lord_culprit" },
                SecretRetired = true
            };

            string json = JsonConvert.SerializeObject(convertedEvt, Formatting.Indented);
            Assert.Contains("\"SecretOwnerHeroIds\":", json);
            Assert.Contains("\"SecretRetired\": true", json);

            // Older version of WorldEvent mock that lacks SecretOwnerHeroIds and SecretRetired properties
            var olderEvt = JsonConvert.DeserializeObject<OldVwEventMock>(json);
            Assert.NotNull(olderEvt);
            Assert.Equal("evt_converted_001", olderEvt!.EventId);
            Assert.True(olderEvt.Extra.ContainsKey("SecretOwnerHeroIds"));
            Assert.True(olderEvt.Extra.ContainsKey("SecretRetired"));

            // Older VW modifies some field and serializes back
            olderEvt.DramaWeight = 7;
            string writtenBackJson = JsonConvert.SerializeObject(olderEvt, Formatting.Indented);

            // Newer VW reads back
            var reloadedEvt = JsonConvert.DeserializeObject<WorldEvent>(writtenBackJson);
            Assert.NotNull(reloadedEvt);
            Assert.Equal("evt_converted_001", reloadedEvt!.EventId);
            Assert.Equal(7, reloadedEvt.DramaWeight);
            Assert.NotNull(reloadedEvt.SecretOwnerHeroIds);
            Assert.Contains("lord_culprit", reloadedEvt.SecretOwnerHeroIds!);
            Assert.True(reloadedEvt.SecretRetired);
        }

        [Fact]
        public void ConversionPreview_DoesNotMutateEvents()
        {
            var evt = new WorldEvent
            {
                EventId = "evt_preview_test",
                Type = "wager_secret_stake",
                Origin = EventOrigin.Secret,
                Day = 20.0,
                SecretRetired = false
            };

            var plan = OldSecretConverter.Plan(new[] { evt }, playerHeroId: "player", applyInPlace: false);

            Assert.Single(plan.Items);
            Assert.Equal(SecretConversionAction.Retired, plan.Items[0].Action);
            Assert.Equal("secret conversion: evt_preview_test wager_secret_stake -> retired", plan.Items[0].FormatLogLine());

            // Original event must be completely unchanged
            Assert.False(evt.SecretRetired);
            Assert.Equal(EventOrigin.Secret, evt.Origin);
        }

        [Fact]
        public void SecretConversionRecord_SerializesAndDeserializes_WithForwardCompatibility()
        {
            var record = new SecretConversionRecord
            {
                Day = 91200.5,
                BackupPath = @"C:\Configs\VividWorld\campaigns\campaign_7ef8a0b0-before-secret-conversion-20261031_150000",
                TotalConverted = 15,
                TotalProcessed = 19,
                CountsByType = new Dictionary<string, int>
                {
                    ["seat_dispute_endured"] = 5,
                    ["wager_secret_stake"] = 9,
                    ["hero_murdered"] = 1
                }
            };
            record.Extra["customFutureField"] = JToken.FromObject("someValue");

            string json = VividJson.Write(record);
            var deserialized = VividJson.Read<SecretConversionRecord>(json);

            Assert.NotNull(deserialized);
            Assert.Equal(91200.5, deserialized!.Day);
            Assert.Equal(15, deserialized.TotalConverted);
            Assert.Equal(19, deserialized.TotalProcessed);
            Assert.Equal(3, deserialized.CountsByType.Count);
            Assert.Equal(5, deserialized.CountsByType["seat_dispute_endured"]);
            Assert.True(deserialized.Extra.ContainsKey("customFutureField"));
        }

        private sealed class OldVwEventMock
        {
            public string EventId { get; set; } = string.Empty;
            public string Type { get; set; } = string.Empty;
            public double Day { get; set; }
            public EventOrigin Origin { get; set; }
            public int DramaWeight { get; set; }

            [JsonExtensionData]
            public IDictionary<string, JToken> Extra { get; set; } = new Dictionary<string, JToken>();
        }
    }
}
