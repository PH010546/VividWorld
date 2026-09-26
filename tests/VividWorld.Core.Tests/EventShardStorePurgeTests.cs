using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using VividWorld.Core.Config;
using VividWorld.Core.Events;
using VividWorld.Core.Memory;
using VividWorld.Core.Persistence;
using VividWorld.Core.Tests.Fakes;
using Xunit;

namespace VividWorld.Core.Tests
{
    public class EventShardStorePurgeTests
    {
        private static WorldEvent CreateSampleEvent(string id, double day, string knower = "hero_1", double? forgetDay = null, string participant = "hero_p")
        {
            return new WorldEvent
            {
                EventId = id,
                Type = "test_event",
                Day = day,
                Origin = EventOrigin.Public,
                Participants = new Dictionary<string, string> { { "role", participant } },
                Facts = new List<Fact> { new Fact { Id = "f1", Category = FactCategory.What, TextId = "t1" } },
                KnownBy = new List<KnownByEntry>
                {
                    new KnownByEntry { HeroId = knower, Hop = 0, ForgetDay = forgetDay }
                },
                State = new RumorState { Dormant = true }
            };
        }

        [Fact]
        public void Remove_DeletesSpecifiedEvents_PreservesOtherEvents_AndWritesEmptyListForEmptyShard()
        {
            var writer = new FailingFileWriter();
            var store = new EventShardStore("events", 100, writer);

            var e1 = CreateSampleEvent("e1", 10.0);
            var e2 = CreateSampleEvent("e2", 20.0);
            var e3 = CreateSampleEvent("e3", 150.0);

            store.Upsert(e1);
            store.Upsert(e2);
            store.Upsert(e3);
            store.Flush();

            var index = store.LoadIndex();
            Assert.NotNull(index.Find("e1"));
            Assert.NotNull(index.Find("e2"));
            Assert.NotNull(index.Find("e3"));

            var (removedCount, touchedShards) = store.Remove(new[] { "e1", "e3" });

            Assert.Equal(2, removedCount);
            Assert.Equal(2, touchedShards);

            // 記憶體查不到被刪的
            Assert.Null(store.Load("e1", null));
            Assert.Null(store.Load("e3", null));
            Assert.NotNull(store.Load("e2", null));

            // 記憶體的 index 移除了被刪的
            Assert.Null(index.Find("e1"));
            Assert.Null(index.Find("e3"));
            Assert.NotNull(index.Find("e2"));

            store.Flush();

            // Flush 後從磁碟讀出的 index 也沒有被刪的
            var flushedIndex = store.LoadIndex();
            Assert.Null(flushedIndex.Find("e1"));
            Assert.Null(flushedIndex.Find("e3"));
            Assert.NotNull(flushedIndex.Find("e2"));

            // e3 所屬的分片 (d0100-0199) 被清空，寫成 "[]"，不刪檔
            Assert.True(writer.Files.ContainsKey("events/d0100-0199.json"));
            string emptyShardJson = writer.Files["events/d0100-0199.json"].Trim();
            Assert.Equal("[]", emptyShardJson);

            // e2 依然在 d0000-0099.json
            string shard0Json = writer.Files["events/d0000-0099.json"];
            Assert.Contains("e2", shard0Json);
            Assert.DoesNotContain("e1", shard0Json);

            // 從分片重建出來的索引與記憶體裡的索引 id 集合完全相同
            writer.Files.Remove("events/_index.json");
            var freshStore = new EventShardStore("events", 100, writer);
            var rebuiltIndex = freshStore.LoadIndex();
            Assert.Equal(new[] { "e2" }, rebuiltIndex.Entries.Select(e => e.EventId).ToArray());
        }

        [Fact]
        public void KnownByIndex_Remove_RemovesFromBothKnownByAndAbout_PreservingOtherEvents()
        {
            var index = new RumorIndex();
            var e1 = new RumorIndexEntry
            {
                EventId = "e1",
                Day = 10.0,
                KnownByHeroIds = new List<string> { "hero_A", "hero_B" },
                ParticipantHeroIds = new List<string> { "hero_X" }
            };
            var e2 = new RumorIndexEntry
            {
                EventId = "e2",
                Day = 20.0,
                KnownByHeroIds = new List<string> { "hero_A" },
                ParticipantHeroIds = new List<string> { "hero_Y" }
            };
            index.Upsert(e1);
            index.Upsert(e2);

            var knownBy = new KnownByIndex();
            knownBy.Rebuild(index);

            Assert.Contains("e1", knownBy.EventsKnownBy("hero_A", 100.0));
            Assert.Contains("e2", knownBy.EventsKnownBy("hero_A", 100.0));
            Assert.Contains("e1", knownBy.EventsKnownBy("hero_B", 100.0));
            Assert.Contains("e1", knownBy.EventsAbout("hero_X", 100.0));
            Assert.Contains("e2", knownBy.EventsAbout("hero_Y", 100.0));

            knownBy.Remove("e1");

            Assert.DoesNotContain("e1", knownBy.EventsKnownBy("hero_A", 100.0));
            Assert.Contains("e2", knownBy.EventsKnownBy("hero_A", 100.0));
            Assert.Empty(knownBy.EventsKnownBy("hero_B", 100.0));
            Assert.Empty(knownBy.EventsAbout("hero_X", 100.0));
            Assert.Contains("e2", knownBy.EventsAbout("hero_Y", 100.0));
        }

        [Fact]
        public void Compatibility_OldIndexWithoutFormatVersionOrForgetDays_RebuildsFromShardsWithFormat1AndForgetDays()
        {
            var writer = new FailingFileWriter();

            // 準備分片檔案
            var evt = CreateSampleEvent("e_old", 15.0, knower: "lord_knower", forgetDay: 30.0);
            string shardJson = VividJson.Write(new List<WorldEvent> { evt });
            writer.WriteAllText("events/d0000-0099.json", shardJson);

            // 舊版 _index.json: 沒有 formatVersion, 沒有 forgetDays
            string oldIndexJson = @"{
                ""entries"": [
                    {
                        ""eventId"": ""e_old"",
                        ""type"": ""test_event"",
                        ""day"": 15.0,
                        ""shardKey"": ""d0000-0099"",
                        ""secret"": false,
                        ""leaked"": false,
                        ""dormant"": true,
                        ""minHop"": 0,
                        ""maxHop"": 0,
                        ""knownByHeroIds"": [""lord_knower""],
                        ""participantHeroIds"": [""hero_p""],
                        ""hasGrudges"": false
                    }
                ]
            }";
            writer.WriteAllText("events/_index.json", oldIndexJson);

            var store = new EventShardStore("events", 100, writer);
            var index = store.LoadIndex();

            Assert.Equal(1, index.FormatVersion);
            Assert.Equal("rebuilt from shards: index format 0 < 1", store.LastLoadNote);

            var entry = index.Find("e_old");
            Assert.NotNull(entry);
            Assert.NotNull(entry!.ForgetDays);
            Assert.True(entry.ForgetDays!.ContainsKey("lord_knower"));
            Assert.Equal(30.0, entry.ForgetDays["lord_knower"]);
        }

        [Fact]
        public void Compatibility_NewIndexRoundTrip_PreservesFormatVersionForgetDaysAndExtraFields()
        {
            var writer = new FailingFileWriter();
            var index = new RumorIndex
            {
                FormatVersion = 1,
                Entries = new List<RumorIndexEntry>
                {
                    new RumorIndexEntry
                    {
                        EventId = "e_roundtrip",
                        Day = 25.0,
                        ForgetDays = new Dictionary<string, double> { ["lord_1"] = 40.0 },
                        Extra = new Dictionary<string, JToken> { ["unknownFieldFromFuture"] = "future_val" }
                    }
                },
                Extra = new Dictionary<string, JToken> { ["indexFutureField"] = 123 }
            };

            string serialized = VividJson.Write(index);
            writer.WriteAllText("events/_index.json", serialized);

            var store = new EventShardStore("events", 100, writer);
            var loaded = store.LoadIndex();

            Assert.Equal("read _index.json (format 1)", store.LastLoadNote);
            Assert.Equal(1, loaded.FormatVersion);
            Assert.Equal(123, (int)loaded.Extra["indexFutureField"]);

            var loadedEntry = loaded.Find("e_roundtrip");
            Assert.NotNull(loadedEntry);
            Assert.NotNull(loadedEntry!.ForgetDays);
            Assert.Equal(40.0, loadedEntry.ForgetDays!["lord_1"]);
            Assert.Equal("future_val", (string?)loadedEntry.Extra["unknownFieldFromFuture"]);
        }

        [Fact]
        public void Compatibility_EntryWithoutForgetDays_TreatedAsStillRemembered_NotPurged()
        {
            var memory = new MemoryConfig { Enabled = true };
            double today = 100.0;

            // 某一筆沒有 ForgetDays（模擬舊版改寫後遺失該鍵）
            var entry = new RumorIndexEntry
            {
                EventId = "e_missing_forget_days",
                Day = 10.0,
                Dormant = true,
                KnownByHeroIds = new List<string> { "lord_npc" },
                ForgetDays = null
            };

            // IndexMemory.Remembers 回傳 true
            Assert.True(IndexMemory.Remembers(entry, "lord_npc", today, memory));

            // PurgePlanner 判定為 KeepRemembered，不會被刪除
            var verdict = EventPurgePlanner.Evaluate(entry, today, "player", memory, 45.0, _ => true);
            Assert.Equal(EventPurgeVerdict.KeepRemembered, verdict);
        }
    }
}
