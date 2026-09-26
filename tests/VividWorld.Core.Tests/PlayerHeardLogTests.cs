#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using VividWorld.Core.Channels;
using VividWorld.Core.Config;
using VividWorld.Core.Events;
using VividWorld.Core.Persistence;
using VividWorld.Core.Presentation;
using VividWorld.Core.Rumors;
using VividWorld.Core.Tests.Fakes;
using VividWorld.Core.Util;
using Xunit;

namespace VividWorld.Core.Tests
{
    public class PlayerHeardLogTests
    {
        private const string PlayerHeroId = "hero_player";

        private static (RumorEngine engine, FakePropagationChannel channel, FakeHeroTraitLookup traits, VividWorldConfig cfg)
            CreateEngine(string playerHeroId = PlayerHeroId, long seed = 42L)
        {
            var cfg = new VividWorldConfig();
            var rng = new SplitMix64Rng();
            var channel = new FakePropagationChannel();
            var traits = new FakeHeroTraitLookup();
            var retention = FactRetentionPolicies.Create(cfg, rng, seed);
            var embellishment = NullEmbellishmentPolicy.Instance;
            var engine = new RumorEngine(cfg, retention, embellishment, channel, traits, rng, seed, playerHeroId);
            return (engine, channel, traits, cfg);
        }

        private static WorldEvent CreateEvent(
            string eventId,
            string type = "duel",
            double day = 10.0,
            EventOrigin origin = EventOrigin.Public,
            bool isLeaked = true,
            int factCount = 5)
        {
            var facts = new List<Fact>();
            for (int i = 0; i < factCount; i++)
            {
                facts.Add(new Fact
                {
                    Id = "fact_" + i,
                    Category = FactCategory.What,
                    TextId = "VividWorld_Fact_Test_" + i,
                    Text = "fact text " + i,
                    Fragility = i + 1
                });
            }

            return new WorldEvent
            {
                EventId = eventId,
                Type = type,
                Day = day,
                Origin = origin,
                State = new RumorState { Leaked = isLeaked },
                DramaWeight = 3,
                Facts = facts
            };
        }

        #region 1. PlayerKnownFacts

        [Fact]
        public void PlayerKnownFacts_Of_KnownFactIdsTakesPrecedence()
        {
            var (engine, _, _, _) = CreateEngine();
            var evt = CreateEvent("evt_1", factCount: 5);
            var playerEntry = new KnownByEntry
            {
                HeroId = PlayerHeroId,
                Hop = 2,
                LearnedDay = 10.0,
                KnownFactIds = new List<string> { "fact_3", "fact_1" }
            };

            var facts = PlayerKnownFacts.Of(evt, playerEntry, engine);

            // Must preserve evt.Facts order: fact_1 then fact_3
            Assert.Equal(2, facts.Count);
            Assert.Equal("fact_1", facts[0].Id);
            Assert.Equal("fact_3", facts[1].Id);
        }

        [Fact]
        public void PlayerKnownFacts_Of_NullKnownFactIds_UsesEngine()
        {
            var (engine, _, _, _) = CreateEngine();
            var evt = CreateEvent("evt_2", factCount: 5);
            var playerEntry = new KnownByEntry
            {
                HeroId = PlayerHeroId,
                Hop = 1,
                SourceHeroId = "hero_source",
                LearnedDay = 10.0,
                KnownFactIds = null
            };

            var facts = PlayerKnownFacts.Of(evt, playerEntry, engine);

            var expectedFromEngine = engine.FactsAtHop(evt, playerEntry.Hop, playerEntry.SourceHeroId ?? string.Empty);
            Assert.Equal(expectedFromEngine.Select(f => f.Id), facts.Select(f => f.Id));
            Assert.NotEmpty(facts);
        }

        [Fact]
        public void PlayerKnownFacts_Of_NullEngine_ReturnsEmpty()
        {
            var evt = CreateEvent("evt_3", factCount: 5);
            var playerEntry = new KnownByEntry
            {
                HeroId = PlayerHeroId,
                Hop = 2,
                LearnedDay = 10.0,
                KnownFactIds = null
            };

            var facts = PlayerKnownFacts.Of(evt, playerEntry, null);

            Assert.Empty(facts);
            Assert.NotEmpty(evt.Facts);
        }

        #endregion

        #region 2. Record

        [Fact]
        public void Record_AddNewEntry_SucceedsAndClonesFacts()
        {
            var writer = new FailingFileWriter();
            var store = new PlayerHeardLogStore("player_heard.json", writer);
            store.Load();

            var evt = CreateEvent("evt_add", type: "duel", day: 15.0, factCount: 3);
            evt.Participants["actor"] = "hero_lord";
            var playerEntry = new KnownByEntry
            {
                HeroId = PlayerHeroId,
                Hop = 1,
                SourceHeroId = "hero_teller",
                LearnedDay = 15.5
            };

            var knownFacts = new List<Fact>
            {
                evt.Facts[0],
                evt.Facts[1]
            };

            bool recorded = store.Record(evt, playerEntry, knownFacts, 16.0);

            Assert.True(recorded);
            Assert.True(store.IsDirty);
            Assert.Equal(1, store.Count);

            var entry = store.Find("evt_add");
            Assert.NotNull(entry);
            Assert.Equal("evt_add", entry!.EventId);
            Assert.Equal("duel", entry.Type);
            Assert.Equal(15.0, entry.Day);
            Assert.Equal(1, entry.PlayerHop);
            Assert.Equal("hero_teller", entry.SourceHeroId);
            Assert.Equal(15.5, entry.LearnedDay);
            Assert.Equal(16.0, entry.UpdatedDay);
            Assert.Equal(2, entry.Facts.Count);
            Assert.Equal("hero_lord", entry.Participants["actor"]);

            // Mutation on original fact must not affect stored fact
            knownFacts[0].Text = "mutated text";
            Assert.NotEqual("mutated text", entry.Facts[0].Text);
        }

        [Fact]
        public void Record_RetellUnion_KeepsOldFacts_AppendsNewInEventOrder_MinHop_UpdatesSourceAndDay()
        {
            var writer = new FailingFileWriter();
            var store = new PlayerHeardLogStore("player_heard.json", writer);
            store.Load();

            var evt = CreateEvent("evt_retell", day: 10.0, factCount: 5);
            var entry1 = new KnownByEntry
            {
                HeroId = PlayerHeroId,
                Hop = 2,
                SourceHeroId = "hero_teller_1",
                LearnedDay = 11.0
            };
            store.Record(evt, entry1, new[] { evt.Facts[1], evt.Facts[3] }, 11.0);

            var entry2 = new KnownByEntry
            {
                HeroId = PlayerHeroId,
                Hop = 1,
                SourceHeroId = "hero_teller_2",
                LearnedDay = 12.0
            };
            bool recorded = store.Record(evt, entry2, new[] { evt.Facts[0], evt.Facts[2] }, 12.0);

            Assert.True(recorded);
            var updated = store.Find("evt_retell");
            Assert.NotNull(updated);
            Assert.Equal(new[] { "fact_0", "fact_1", "fact_2", "fact_3" }, updated!.Facts.Select(f => f.Id));
            Assert.Equal(1, updated.PlayerHop);
            Assert.Equal(11.0, updated.LearnedDay);
            Assert.Equal(12.0, updated.UpdatedDay);
            Assert.Equal("hero_teller_2", updated.SourceHeroId);

            // Higher hop retell with another new fact preserves min hop 1
            var entry3 = new KnownByEntry
            {
                HeroId = PlayerHeroId,
                Hop = 3,
                SourceHeroId = "hero_teller_3",
                LearnedDay = 13.0
            };
            store.Record(evt, entry3, new[] { evt.Facts[4] }, 13.0);
            Assert.Equal(1, updated.PlayerHop);
            Assert.Equal("hero_teller_3", updated.SourceHeroId);
            Assert.Equal(13.0, updated.UpdatedDay);
            Assert.Equal(5, updated.Facts.Count);
        }

        [Fact]
        public void Record_UnleakedSecret_ReturnsFalse_DoesNotRecord()
        {
            var writer = new FailingFileWriter();
            var store = new PlayerHeardLogStore("player_heard.json", writer);
            store.Load();

            var evt = CreateEvent("evt_secret", origin: EventOrigin.Secret, isLeaked: false, factCount: 3);
            var playerEntry = new KnownByEntry
            {
                HeroId = PlayerHeroId,
                Hop = 0,
                LearnedDay = 10.0
            };

            bool recorded = store.Record(evt, playerEntry, evt.Facts, 10.0);

            Assert.False(recorded);
            Assert.False(store.IsDirty);
            Assert.Equal(0, store.Count);
            Assert.False(store.Contains("evt_secret"));
        }

        [Fact]
        public void Record_NoNewFacts_ReturnsFalse_DoesNotMarkDirty()
        {
            var writer = new FailingFileWriter();
            var store = new PlayerHeardLogStore("player_heard.json", writer);
            store.Load();

            var evt = CreateEvent("evt_same", day: 10.0, factCount: 3);
            var playerEntry = new KnownByEntry
            {
                HeroId = PlayerHeroId,
                Hop = 1,
                SourceHeroId = "hero_teller_1",
                LearnedDay = 10.0
            };
            store.Record(evt, playerEntry, new[] { evt.Facts[0], evt.Facts[1] }, 10.0);
            store.Flush();

            Assert.False(store.IsDirty);
            var initialUpdatedDay = store.Find("evt_same")!.UpdatedDay;

            var retellEntry = new KnownByEntry
            {
                HeroId = PlayerHeroId,
                Hop = 1,
                SourceHeroId = "hero_teller_2",
                LearnedDay = 12.0
            };
            bool recorded = store.Record(evt, retellEntry, new[] { evt.Facts[0] }, 12.0);

            Assert.False(recorded);
            Assert.False(store.IsDirty);
            Assert.Equal(initialUpdatedDay, store.Find("evt_same")!.UpdatedDay);
        }

        #endregion

        #region 3. Backfill

        [Fact]
        public void Backfill_WithoutFile_PopulatesPlayerKnownEvents_SkipsUnleakedSecrets()
        {
            var writer = new FailingFileWriter();
            var store = new PlayerHeardLogStore("player_heard.json", writer);
            var loadResult = store.Load();
            Assert.Equal(PlayerHeardLoadStatus.FileNotFound, loadResult.Status);

            var evt1 = CreateEvent("evt_1", factCount: 4);
            evt1.KnownBy.Add(new KnownByEntry
            {
                HeroId = PlayerHeroId,
                Hop = 1,
                LearnedDay = 10.0,
                KnownFactIds = new List<string> { "fact_1", "fact_2" }
            });

            var evt2 = CreateEvent("evt_2", factCount: 4);
            evt2.KnownBy.Add(new KnownByEntry
            {
                HeroId = PlayerHeroId,
                Hop = 2,
                LearnedDay = 12.0,
                KnownFactIds = null
            });

            var evtSecret = CreateEvent("evt_secret", origin: EventOrigin.Secret, isLeaked: false, factCount: 3);
            evtSecret.KnownBy.Add(new KnownByEntry
            {
                HeroId = PlayerHeroId,
                Hop = 0,
                LearnedDay = 5.0
            });

            var index = new RumorIndex();
            index.Entries.Add(new RumorIndexEntry { EventId = "evt_1", KnownByHeroIds = new List<string> { PlayerHeroId } });
            index.Entries.Add(new RumorIndexEntry { EventId = "evt_2", KnownByHeroIds = new List<string> { PlayerHeroId } });
            index.Entries.Add(new RumorIndexEntry { EventId = "evt_secret", KnownByHeroIds = new List<string> { PlayerHeroId } });

            var dict = new Dictionary<string, WorldEvent>
            {
                ["evt_1"] = evt1,
                ["evt_2"] = evt2,
                ["evt_secret"] = evtSecret
            };

            var (engine, _, _, _) = CreateEngine();

            var result = store.Backfill(
                index,
                PlayerHeroId,
                id => dict.TryGetValue(id, out var e) ? e : null,
                (e, k) => PlayerKnownFacts.Of(e, k, engine),
                day: 20.0);

            Assert.Equal(3, result.Considered);
            Assert.Equal(2, result.Added);
            Assert.Equal(1, result.SkippedSecret);
            Assert.Equal(0, result.NoPlayerEntry);
            Assert.Equal(0, result.NotLoadable);

            Assert.Equal(2, store.Count);
            Assert.True(store.Contains("evt_1"));
            Assert.True(store.Contains("evt_2"));
            Assert.False(store.Contains("evt_secret"));

            var entry1 = store.Find("evt_1");
            Assert.NotNull(entry1);
            Assert.Equal(new[] { "fact_1", "fact_2" }, entry1!.Facts.Select(f => f.Id));

            var entry2 = store.Find("evt_2");
            Assert.NotNull(entry2);
            Assert.NotEmpty(entry2!.Facts);
        }

        [Fact]
        public void Backfill_ForwardCompatibility_PreservesExtraFields()
        {
            var writer = new FailingFileWriter();
            string initialJson = @"{
  ""futureRootKey"": ""futureRootValue"",
  ""Entries"": [
    {
      ""EventId"": ""evt_existing"",
      ""Type"": ""duel"",
      ""Day"": 10.0,
      ""DramaWeight"": 3,
      ""PlayerHop"": 1,
      ""LearnedDay"": 10.0,
      ""UpdatedDay"": 10.0,
      ""Facts"": [],
      ""futureEntryKey"": 12345
    }
  ]
}";
            writer.WriteAllText("player_heard.json", initialJson);

            var store = new PlayerHeardLogStore("player_heard.json", writer);
            var loadResult = store.Load();
            Assert.Equal(PlayerHeardLoadStatus.ReadFromFile, loadResult.Status);
            Assert.Equal(1, store.Count);

            var evt = CreateEvent("evt_new", factCount: 2);
            evt.KnownBy.Add(new KnownByEntry { HeroId = PlayerHeroId, Hop = 1, LearnedDay = 15.0 });

            var index = new RumorIndex();
            index.Entries.Add(new RumorIndexEntry { EventId = "evt_new", KnownByHeroIds = new List<string> { PlayerHeroId } });

            store.Backfill(
                index,
                PlayerHeroId,
                id => id == "evt_new" ? evt : null,
                (e, k) => e.Facts,
                day: 20.0);

            bool flushed = store.Flush();
            Assert.True(flushed);

            string savedJson = writer.ReadAllText("player_heard.json")!;
            Assert.Contains("futureRootKey", savedJson);
            Assert.Contains("futureRootValue", savedJson);
            Assert.Contains("futureEntryKey", savedJson);
            Assert.Contains("12345", savedJson);
        }

        [Fact]
        public void Load_UnreadableFile_IsMovedAside_SoTheNextFlushDoesNotOverwriteIt()
        {
            var writer = new FailingFileWriter();
            writer.WriteAllText("player_heard.json", "{ this is not json");

            var store = new PlayerHeardLogStore("player_heard.json", writer);
            var result = store.Load();

            Assert.Equal(PlayerHeardLoadStatus.FileUnreadable, result.Status);
            Assert.NotNull(result.SetAsidePath);
            Assert.StartsWith("player_heard.json.unreadable-", result.SetAsidePath);
            Assert.Equal("{ this is not json", writer.ReadAllText(result.SetAsidePath!));
            Assert.False(writer.Exists("player_heard.json"));

            var evt = CreateEvent("evt_new", factCount: 1);
            evt.KnownBy.Add(new KnownByEntry { HeroId = PlayerHeroId, Hop = 1, LearnedDay = 15.0 });
            store.Record(evt, evt.EntryFor(PlayerHeroId)!, evt.Facts, 15.0);
            Assert.True(store.Flush());

            Assert.Equal("{ this is not json", writer.ReadAllText(result.SetAsidePath!));
        }

        [Fact]
        public void Backfill_DoesNotReloadAlreadyRecordedEvents()
        {
            var writer = new FailingFileWriter();
            var store = new PlayerHeardLogStore("player_heard.json", writer);
            store.Load();

            var evt1 = CreateEvent("evt_known", factCount: 2);
            var playerEntry = new KnownByEntry { HeroId = PlayerHeroId, Hop = 1, LearnedDay = 10.0 };
            store.Record(evt1, playerEntry, evt1.Facts, 10.0);

            var index = new RumorIndex();
            index.Entries.Add(new RumorIndexEntry { EventId = "evt_known", KnownByHeroIds = new List<string> { PlayerHeroId } });
            index.Entries.Add(new RumorIndexEntry { EventId = "evt_unrecorded", KnownByHeroIds = new List<string> { PlayerHeroId } });

            int loadCalls = 0;
            var loadedIds = new List<string>();

            var evt2 = CreateEvent("evt_unrecorded", factCount: 2);
            evt2.KnownBy.Add(playerEntry);

            store.Backfill(
                index,
                PlayerHeroId,
                id =>
                {
                    loadCalls++;
                    loadedIds.Add(id);
                    return id == "evt_unrecorded" ? evt2 : null;
                },
                (e, k) => e.Facts,
                day: 15.0);

            Assert.Equal(1, loadCalls);
            Assert.Single(loadedIds);
            Assert.Equal("evt_unrecorded", loadedIds[0]);
            Assert.DoesNotContain("evt_known", loadedIds);
        }

        [Fact]
        public void Backfill_CountsNotLoadableAndNoPlayerEntry()
        {
            var writer = new FailingFileWriter();
            var store = new PlayerHeardLogStore("player_heard.json", writer);
            store.Load();

            var index = new RumorIndex();
            index.Entries.Add(new RumorIndexEntry { EventId = "evt_missing", KnownByHeroIds = new List<string> { PlayerHeroId } });
            index.Entries.Add(new RumorIndexEntry { EventId = "evt_no_player_entry", KnownByHeroIds = new List<string> { PlayerHeroId } });
            index.Entries.Add(new RumorIndexEntry { EventId = "evt_not_known", KnownByHeroIds = new List<string> { "other_hero" } });

            var evtNoPlayer = CreateEvent("evt_no_player_entry");

            var result = store.Backfill(
                index,
                PlayerHeroId,
                id => id == "evt_no_player_entry" ? evtNoPlayer : null,
                (e, k) => e.Facts,
                day: 10.0);

            Assert.Equal(2, result.Considered);
            Assert.Equal(1, result.NotLoadable);
            Assert.Equal(1, result.NoPlayerEntry);
            Assert.Equal(0, result.Added);
        }

        #endregion

        #region 4. Flush

        [Fact]
        public void Flush_FailureKeepsDirty_SuccessClearsDirty()
        {
            var writer = new FailingFileWriter();
            var store = new PlayerHeardLogStore("player_heard.json", writer);
            store.Load();

            var evt = CreateEvent("evt_flush", factCount: 2);
            store.Record(evt, new KnownByEntry { HeroId = PlayerHeroId, Hop = 1, LearnedDay = 10.0 }, evt.Facts, 10.0);
            Assert.True(store.IsDirty);

            writer.FailOn("player_heard", FileOp.WriteAllText);
            bool ok1 = store.Flush();
            Assert.False(ok1);
            Assert.True(store.IsDirty, "Flush failure must keep store dirty");

            writer.ClearFailures();
            bool ok2 = store.Flush();
            Assert.True(ok2);
            Assert.False(store.IsDirty, "Flush success must clear dirty flag");
        }

        #endregion

        #region 5. VisibleEntries & HiddenFuture

        [Fact]
        public void VisibleEntries_FiltersByTolerance_AndCountsHiddenFuture()
        {
            var writer = new FailingFileWriter();
            var store = new PlayerHeardLogStore("player_heard.json", writer);
            store.Load();

            double currentDay = 100.0;
            var past = CreateEvent("evt_past", day: 90.0);
            store.Record(past, new KnownByEntry { HeroId = PlayerHeroId, Hop = 1, LearnedDay = 90.0 }, past.Facts, 90.0);

            var nearFuture = CreateEvent("evt_near", day: 100.02);
            store.Record(nearFuture, new KnownByEntry { HeroId = PlayerHeroId, Hop = 1, LearnedDay = 100.0 }, nearFuture.Facts, 100.0);

            var farFuture = CreateEvent("evt_future", day: 105.0);
            store.Record(farFuture, new KnownByEntry { HeroId = PlayerHeroId, Hop = 1, LearnedDay = 100.0 }, farFuture.Facts, 100.0);

            var visible = store.VisibleEntries(currentDay).Select(e => e.EventId).ToList();
            Assert.Contains("evt_past", visible);
            Assert.Contains("evt_near", visible);
            Assert.DoesNotContain("evt_future", visible);

            Assert.Equal(1, store.HiddenFutureCount(currentDay));
        }

        #endregion

        #region 6. Formatters

        [Fact]
        public void PlayerHeardLogFormatter_VerbatimTemplates_Load_Told_WriteFailed()
        {
            var backfill = new PlayerHeardBackfillResult
            {
                Considered = 10,
                Added = 8,
                SkippedSecret = 1,
                NoPlayerEntry = 1,
                NotLoadable = 0
            };

            string loadRead = PlayerHeardLogFormatter.FormatLoad(8, "read from player_heard.json", backfill);
            Assert.Equal(
                "Player heard-log: 8 on file (read from player_heard.json); backfilled 8 of 10 player-known event(s) from the store (skipped 1 unleaked secret, 1 without a player entry, 0 not loadable)",
                loadRead);

            string loadNoFile = PlayerHeardLogFormatter.FormatLoad(0, "no file yet", backfill);
            Assert.Equal(
                "Player heard-log: 0 on file (no file yet); backfilled 8 of 10 player-known event(s) from the store (skipped 1 unleaked secret, 1 without a player entry, 0 not loadable)",
                loadNoFile);

            string loadCorrupt = PlayerHeardLogFormatter.FormatLoad(0, "file unreadable, started empty", backfill);
            Assert.Equal(
                "Player heard-log: 0 on file (file unreadable, started empty); backfilled 8 of 10 player-known event(s) from the store (skipped 1 unleaked secret, 1 without a player entry, 0 not loadable)",
                loadCorrupt);

            string toldAdded = PlayerHeardLogFormatter.FormatTold(isUpdate: false, "evt_42", hop: 1, factCount: 3, source: "hero_source");
            Assert.Equal("Player heard-log: added evt_42 (hop 1, 3 fact(s), from hero_source)", toldAdded);

            string toldUpdated = PlayerHeardLogFormatter.FormatTold(isUpdate: true, "evt_42", hop: 0, factCount: 4, source: "hero_teller");
            Assert.Equal("Player heard-log: updated evt_42 (hop 0, 4 fact(s), from hero_teller)", toldUpdated);

            string writeFailed = PlayerHeardLogFormatter.FormatWriteFailed();
            Assert.Equal("Player heard-log: write failed, will retry on the next flush", writeFailed);
        }

        [Fact]
        public void ChronicleLogFormatter_VerbatimTemplates()
        {
            string emptyLine = ChronicleLogFormatter.FormatOpen("hero_player", 10.5, new ChronicleStats { TotalKnown = 0, Returned = 0, HiddenFuture = 0 }, 50);
            Assert.Equal("Chronicle opened for hero_player on day 10.5: nothing known yet (0 known)", emptyLine);

            string dataLine = ChronicleLogFormatter.FormatOpen("hero_player", 10.5, new ChronicleStats { TotalKnown = 5, Returned = 2, HiddenFuture = 1 }, 50);
            Assert.Equal("Chronicle opened for hero_player on day 10.5: 2 shown of 5 heard (max 50), hidden future 1", dataLine);
        }

        #endregion
    }
}
