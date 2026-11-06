#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using VividWorld.Core.Catalog;
using VividWorld.Core.Config;
using VividWorld.Core.Events;
using VividWorld.Core.Persistence;
using VividWorld.Core.Presentation;
using VividWorld.Core.Secrets;
using VividWorld.Core.Tests.Fakes;
using Xunit;

namespace VividWorld.Core.Tests.Secrets
{
    public class SecretConfideWindowAndChronicleTests
    {
        [Fact]
        public void SecretVisibilityPolicy_IsHiddenPartVisibleToPlayer_FollowsRules()
        {
            const string playerHeroId = "hero_player";

            // Null inputs
            Assert.False(SecretVisibilityPolicy.IsHiddenPartVisibleToPlayer(null, playerHeroId));
            Assert.False(SecretVisibilityPolicy.IsHiddenPartVisibleToPlayer(null, null));

            // Public event
            var publicEvt = new WorldEvent
            {
                EventId = "evt_pub",
                Origin = EventOrigin.Public
            };
            Assert.True(SecretVisibilityPolicy.IsHiddenPartVisibleToPlayer(publicEvt, playerHeroId));

            // Secret event - unleaked, player not knower
            var secretEvt = new WorldEvent
            {
                EventId = "evt_sec",
                Origin = EventOrigin.Secret,
                State = new RumorState { Leaked = false },
                SecretRetired = false,
                KnownBy = new List<KnownByEntry>
                {
                    new KnownByEntry { HeroId = "hero_other", Hop = 0 }
                }
            };
            Assert.False(SecretVisibilityPolicy.IsHiddenPartVisibleToPlayer(secretEvt, playerHeroId));
            Assert.False(SecretVisibilityPolicy.IsHiddenPartVisibleToPlayer(secretEvt, null));

            // Secret event - unleaked, player is knower, not retired
            secretEvt.KnownBy.Add(new KnownByEntry { HeroId = playerHeroId, Hop = 1 });
            Assert.True(SecretVisibilityPolicy.IsHiddenPartVisibleToPlayer(secretEvt, playerHeroId));

            // Secret event - unleaked, player is knower, retired
            secretEvt.SecretRetired = true;
            Assert.False(SecretVisibilityPolicy.IsHiddenPartVisibleToPlayer(secretEvt, playerHeroId));

            // Secret event - leaked
            secretEvt.SecretRetired = false;
            secretEvt.State.Leaked = true;
            Assert.True(SecretVisibilityPolicy.IsHiddenPartVisibleToPlayer(secretEvt, playerHeroId));
            Assert.True(SecretVisibilityPolicy.IsHiddenPartVisibleToPlayer(secretEvt, "anyone_else"));
        }

        [Fact]
        public void PlayerConfideQueueStore_SaveAndLoad_Roundtrips()
        {
            var writer = new FailingFileWriter();
            string path = "secret_confide_queue.json";
            var store = new PlayerConfideQueueStore(path, writer);

            var loadResult = store.Load();
            Assert.Equal(PlayerConfideQueueLoadStatus.FileNotFound, loadResult.Status);
            Assert.Equal(0, store.Count);

            store.Enqueue(new PlayerConfideQueueItem
            {
                EventId = "evt_secret_1",
                TellerHeroId = "lord_teller_1",
                Day = 15.25
            });

            store.Enqueue(new PlayerConfideQueueItem
            {
                EventId = "evt_secret_2",
                TellerHeroId = "lord_teller_2",
                Day = 16.50
            });

            Assert.Equal(2, store.Count);

            // Re-load in a fresh store instance
            var store2 = new PlayerConfideQueueStore(path, writer);
            var loadResult2 = store2.Load();
            Assert.Equal(PlayerConfideQueueLoadStatus.Success, loadResult2.Status);
            Assert.Equal(2, store2.Count);

            var first = store2.Peek();
            Assert.NotNull(first);
            Assert.Equal("evt_secret_1", first!.EventId);
            Assert.Equal("lord_teller_1", first.TellerHeroId);
            Assert.Equal(15.25, first.Day);

            var dequeued = store2.Dequeue();
            Assert.NotNull(dequeued);
            Assert.Equal("evt_secret_1", dequeued!.EventId);
            Assert.Equal(1, store2.Count);

            var second = store2.Peek();
            Assert.NotNull(second);
            Assert.Equal("evt_secret_2", second!.EventId);
        }

        [Fact]
        public void PlayerConfideQueueStore_ForwardCompatibility_PreservesUnknownFields()
        {
            var writer = new FailingFileWriter();
            string path = "secret_confide_queue.json";

            string jsonWithUnknownFields = @"{
  ""version"": 1,
  ""futureModuleConfig"": ""active"",
  ""items"": [
    {
      ""eventId"": ""sec_1"",
      ""tellerHeroId"": ""lord_1"",
      ""queuedDay"": 10.0,
      ""futureItemTag"": 42
    }
  ]
}";
            writer.WriteAllText(path, jsonWithUnknownFields);

            var store = new PlayerConfideQueueStore(path, writer);
            var loadResult = store.Load();
            Assert.Equal(PlayerConfideQueueLoadStatus.Success, loadResult.Status);
            Assert.Equal(1, store.Count);

            var item = store.Peek();
            Assert.NotNull(item);
            Assert.True(item!.Extra.ContainsKey("futureItemTag"));
            Assert.Equal(42, item.Extra["futureItemTag"].Value<int>());
            Assert.True(store.Data.Extra.ContainsKey("futureModuleConfig"));

            // Add an item and save
            store.Enqueue(new PlayerConfideQueueItem
            {
                EventId = "sec_2",
                TellerHeroId = "lord_2",
                Day = 11.0
            });

            string updatedJson = writer.ReadAllText(path)!;
            Assert.Contains("futureModuleConfig", updatedJson);
            Assert.Contains("futureItemTag", updatedJson);
        }

        [Fact]
        public void PlayerConfideQueueStore_CorruptFile_SetsAsideAndStartsEmpty()
        {
            var writer = new FailingFileWriter();
            string path = "secret_confide_queue.json";
            writer.WriteAllText(path, "{ corrupted json without proper closing");

            var store = new PlayerConfideQueueStore(path, writer);
            var loadResult = store.Load();

            Assert.Equal(PlayerConfideQueueLoadStatus.Corrupt, loadResult.Status);
            Assert.Equal(0, store.Count);
            Assert.NotNull(loadResult.SetAsidePath);
            Assert.True(writer.Exists(loadResult.SetAsidePath!));
        }

        [Fact]
        public void PlayerHeardLog_ReceivesConfidedSecret_UnderLinkedPublicBlock()
        {
            var writer = new FailingFileWriter();
            string path = "player_heard.json";
            var heardStore = new PlayerHeardLogStore(path, writer);
            heardStore.Load();

            const string playerHeroId = "hero_player";
            const string tellerHeroId = "hero_teller";

            var publicEvt = new WorldEvent
            {
                EventId = "evt_seat_dispute",
                Type = "seat_dispute",
                Origin = EventOrigin.Public,
                Day = 10.0,
                Participants = new Dictionary<string, string>
                {
                    ["slighted"] = tellerHeroId,
                    ["favored"] = "lord_rival"
                },
                Facts = new List<Fact>
                {
                    new Fact { Id = "f_seat_pub", TextId = "Fact_Seat_Pub", Text = "A seat dispute occurred." }
                },
                KnownBy = new List<KnownByEntry>
                {
                    new KnownByEntry { HeroId = tellerHeroId, Hop = 0, LearnedDay = 10.0 },
                    new KnownByEntry { HeroId = playerHeroId, Hop = 1, SourceHeroId = tellerHeroId, LearnedDay = 10.5 }
                }
            };

            var secretEvt = new WorldEvent
            {
                EventId = "evt_seat_dispute_grudge",
                Type = "seat_dispute_grudge_held",
                Origin = EventOrigin.Secret,
                LinkedEventId = "evt_seat_dispute",
                Day = 10.0,
                State = new RumorState { Leaked = false },
                SecretRetired = false,
                Participants = new Dictionary<string, string>
                {
                    ["slighted"] = tellerHeroId,
                    ["favored"] = "lord_rival"
                },
                Facts = new List<Fact>
                {
                    new Fact { Id = "f_grudge", TextId = "Fact_Grudge", Text = "The slight was swallowed with a grudge." }
                },
                KnownBy = new List<KnownByEntry>
                {
                    new KnownByEntry { HeroId = tellerHeroId, Hop = 0, LearnedDay = 10.0 }
                }
            };

            var eventLookup = new Dictionary<string, WorldEvent>(StringComparer.Ordinal)
            {
                [publicEvt.EventId] = publicEvt,
                [secretEvt.EventId] = secretEvt
            };

            // First, record public event into heard log
            var pubPlayerEntry = publicEvt.EntryFor(playerHeroId)!;
            var pubTelling = new PlayerHeardSource
            {
                HeroId = tellerHeroId,
                Day = 10.5,
                Hop = 1,
                FactIds = new List<string> { "f_seat_pub" }
            };
            heardStore.RecordTelling(publicEvt, pubPlayerEntry, publicEvt.Facts, 10.5, pubTelling, id => eventLookup[id]);
            Assert.Equal(1, heardStore.Count);

            // Teller confides secret to player
            var secPlayerEntry = new KnownByEntry
            {
                HeroId = playerHeroId,
                Hop = 1,
                SourceHeroId = tellerHeroId,
                LearnedDay = 11.0
            };
            secretEvt.KnownBy.Add(secPlayerEntry);

            var secTelling = new PlayerHeardSource
            {
                HeroId = tellerHeroId,
                Day = 11.0,
                Hop = 1,
                FactIds = new List<string> { "f_grudge" }
            };

            // Record secret telling
            var recordResult = heardStore.RecordTelling(secretEvt, secPlayerEntry, secretEvt.Facts, 11.0, secTelling, id => eventLookup[id]);
            Assert.True(recordResult.Changed);
            Assert.True(recordResult.EntryAdded);
            Assert.Equal(2, heardStore.Count);

            var secretEntry = heardStore.Find(secretEvt.EventId);
            Assert.NotNull(secretEntry);
            Assert.Equal(publicEvt.EventId, secretEntry!.RootEventId);
            Assert.Equal(secretEvt.LinkedEventId, publicEvt.EventId);

            // ChronicleProvider grouping test
            var provider = new ChronicleProvider(
                heardStore,
                type => new EventTemplate { Type = type, Headline = type },
                new PresentationConfig());

            var chronicleEntries = provider.ForPlayer(maxEntries: 50, currentDay: 20.0, out var stats);
            // Both entries share the same root "evt_seat_dispute", so there is 1 block!
            Assert.Single(chronicleEntries);
            var block = chronicleEntries[0];
            Assert.Equal("evt_seat_dispute", block.EventId);
            Assert.Equal(2, block.Sources.Count);
            Assert.Single(block.Matters);
            Assert.False(block.Matters[0].HasHeading);
            Assert.Equal(2, block.Matters[0].Sources.Count);

            // Sources are from teller, with proper hops and facts
            Assert.Equal(tellerHeroId, block.Sources[0].HeroId);
            Assert.Equal(1, block.Sources[0].Hop);
            Assert.Equal(tellerHeroId, block.Sources[1].HeroId);
            Assert.Equal(1, block.Sources[1].Hop);
        }

        [Fact]
        public void PlayerHeardLog_OldCampaignWithoutSecret_LoadsCleanly()
        {
            var writer = new FailingFileWriter();
            string path = "player_heard.json";
            string legacyJson = @"{
  ""version"": 1,
  ""entries"": [
    {
      ""eventId"": ""old_evt_1"",
      ""type"": ""tournament_won"",
      ""day"": 5.0,
      ""playerHop"": 1,
      ""sourceHeroId"": ""lord_1"",
      ""learnedDay"": 6.0,
      ""updatedDay"": 6.0,
      ""facts"": [
        { ""id"": ""f1"", ""text"": ""won tournament"" }
      ]
    }
  ]
}";
            writer.WriteAllText(path, legacyJson);

            var heardStore = new PlayerHeardLogStore(path, writer);
            var loadResult = heardStore.Load();

            Assert.Equal(PlayerHeardLoadStatus.ReadFromFile, loadResult.Status);
            Assert.Equal(1, heardStore.Count);
            Assert.True(heardStore.Contains("old_evt_1"));
            var entry = heardStore.Find("old_evt_1");
            Assert.NotNull(entry);
            Assert.Equal("old_evt_1", entry!.EventId);
            Assert.Single(entry.EffectiveSources());

            // ChronicleProvider can cleanly display legacy entry
            var provider = new ChronicleProvider(
                heardStore,
                type => new EventTemplate { Type = type, Headline = type },
                new PresentationConfig());
            var chronicleEntries = provider.ForPlayer(maxEntries: 50, currentDay: 10.0, out var stats);
            Assert.Single(chronicleEntries);
            Assert.Equal("old_evt_1", chronicleEntries[0].EventId);
            Assert.Single(chronicleEntries[0].Sources);
            Assert.Equal(1, chronicleEntries[0].Sources[0].Hop);
            Assert.Equal("lord_1", chronicleEntries[0].Sources[0].HeroId);
        }

        [Fact]
        public void PlayerConfideQueueStore_QueuePersistsAcrossSaveLoadCycle()
        {
            var writer = new FailingFileWriter();
            string path = "secret_confide_queue.json";

            // Session 1: enqueue items
            var session1 = new PlayerConfideQueueStore(path, writer);
            session1.Load();
            session1.Enqueue(new PlayerConfideQueueItem { EventId = "sec_a", TellerHeroId = "hero_a", Day = 1.0 });
            session1.Enqueue(new PlayerConfideQueueItem { EventId = "sec_b", TellerHeroId = "hero_b", Day = 2.0 });
            session1.Enqueue(new PlayerConfideQueueItem { EventId = "sec_c", TellerHeroId = "hero_c", Day = 3.0 });
            Assert.Equal(3, session1.Count);

            // Session 2: reload and dequeue one item
            var session2 = new PlayerConfideQueueStore(path, writer);
            var loadResult2 = session2.Load();
            Assert.Equal(PlayerConfideQueueLoadStatus.Success, loadResult2.Status);
            Assert.Equal(3, session2.Count);

            var popped = session2.Dequeue();
            Assert.NotNull(popped);
            Assert.Equal("sec_a", popped!.EventId);
            Assert.Equal(2, session2.Count);

            // Session 3: reload and verify remaining queue state
            var session3 = new PlayerConfideQueueStore(path, writer);
            var loadResult3 = session3.Load();
            Assert.Equal(PlayerConfideQueueLoadStatus.Success, loadResult3.Status);
            Assert.Equal(2, session3.Count);
            Assert.Equal("sec_b", session3.Peek()!.EventId);
        }
    }
}
