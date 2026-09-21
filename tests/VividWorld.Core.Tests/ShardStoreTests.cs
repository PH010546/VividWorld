#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using VividWorld.Core.Events;
using VividWorld.Core.Persistence;
using VividWorld.Core.Tests.Fakes;
using Xunit;

namespace VividWorld.Core.Tests
{
    public class ShardStoreTests
    {
        private static WorldEvent CreateSampleEvent(string id, double day, string type = "duel", EventOrigin origin = EventOrigin.Public)
        {
            return new WorldEvent
            {
                EventId = id,
                Type = type,
                Day = day,
                Origin = origin,
                Participants = new Dictionary<string, string> { { "hero", "hero_1" } },
                Facts = new List<Fact> { new Fact { Id = "f1", Category = FactCategory.What, TextId = "t1" } },
                KnownBy = new List<KnownByEntry> { new KnownByEntry { HeroId = "hero_1", Hop = 0 } }
            };
        }

        [Fact]
        public void Sharding_BucketsEventsByDayRange()
        {
            Assert.Equal("d0000-0099", ShardKey.For(0, 100));
            Assert.Equal("d0000-0099", ShardKey.For(50, 100));
            Assert.Equal("d0000-0099", ShardKey.For(99.9, 100));
            Assert.Equal("d0100-0199", ShardKey.For(100, 100));
            Assert.Equal("d0100-0199", ShardKey.For(199.9, 100));
            Assert.Equal("d0200-0299", ShardKey.For(200, 100));
            Assert.Equal("d0000-0099", ShardKey.For(-5, 100));

            var writer = new FailingFileWriter();
            var store = new EventShardStore("events", 100, writer);

            store.Upsert(CreateSampleEvent("e_day50", 50));
            store.Upsert(CreateSampleEvent("e_day150", 150));
            store.Upsert(CreateSampleEvent("e_day250", 250));
            store.Flush();

            Assert.True(writer.Files.ContainsKey("events/d0000-0099.json"));
            Assert.True(writer.Files.ContainsKey("events/d0100-0199.json"));
            Assert.True(writer.Files.ContainsKey("events/d0200-0299.json"));

            var loaded1 = store.Load("e_day50", null);
            var loaded2 = store.Load("e_day150", null);
            var loaded3 = store.Load("e_day250", null);

            Assert.NotNull(loaded1);
            Assert.NotNull(loaded2);
            Assert.NotNull(loaded3);
            Assert.Equal(50, loaded1!.Day);
            Assert.Equal(150, loaded2!.Day);
            Assert.Equal(250, loaded3!.Day);
        }

        [Fact]
        public void Save_IsAtomic_NoTempFileRemains()
        {
            var writer = new FailingFileWriter();
            var store = new EventShardStore("events", 100, writer);

            store.Upsert(CreateSampleEvent("evt_atomic", 120));
            store.Flush();

            Assert.True(writer.Files.ContainsKey("events/d0100-0199.json"));
            Assert.True(writer.Files.ContainsKey("events/_index.json"));

            Assert.DoesNotContain(writer.Files.Keys, k => k.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase));
        }

        [Fact]
        public void Save_FailureMidWrite_LeavesThePreviousShardIntact()
        {
            var writer = new FailingFileWriter();
            var store = new EventShardStore("events", 100, writer);

            // 1. Initial write
            var evtInitial = CreateSampleEvent("evt_initial", 120);
            store.Upsert(evtInitial);
            store.Flush();

            Assert.True(writer.Files.TryGetValue("events/d0100-0199.json", out var originalContent));

            // 2. Upsert second event into the same shard
            var evtSecond = CreateSampleEvent("evt_second", 130);
            store.Upsert(evtSecond);

            // 3. Inject failure on Replace operation
            writer.FailOn("d0100-0199", FileOp.Replace, 1);

            // 4. Flush fails on shard replace
            store.Flush();

            // 5. Old content MUST be verbatim intact
            Assert.Equal(originalContent, writer.Files["events/d0100-0199.json"]);

            // 6. Clear failures and retry Flush
            writer.ClearFailures();
            store.Flush();

            // 7. Shard now updated with both events
            var updatedContent = writer.Files["events/d0100-0199.json"];
            Assert.NotEqual(originalContent, updatedContent);
            var events = VividJson.Read<List<WorldEvent>>(updatedContent);
            Assert.NotNull(events);
            Assert.Equal(2, events!.Count);
        }

        [Fact]
        public void Load_CorruptShard_IsSkippedAndTheRestSurvive()
        {
            var writer = new FailingFileWriter();

            var evt1 = CreateSampleEvent("evt_valid_1", 50);
            var evt3 = CreateSampleEvent("evt_valid_3", 250);

            // Write shard 1 (valid)
            writer.Files["events/d0000-0099.json"] = VividJson.Write(new List<WorldEvent> { evt1 });
            // Write shard 2 (corrupted JSON)
            writer.Files["events/d0100-0199.json"] = "MALFORMED_CORRUPTED_JSON {{{ [invalid";
            // Write shard 3 (valid)
            writer.Files["events/d0200-0299.json"] = VividJson.Write(new List<WorldEvent> { evt3 });

            // No _index.json on disk: LoadIndex must rebuild from shards
            var store = new EventShardStore("events", 100, writer);
            var index = store.LoadIndex();

            Assert.NotNull(index);
            Assert.Equal(2, index.Entries.Count);
            Assert.NotNull(index.Find("evt_valid_1"));
            Assert.NotNull(index.Find("evt_valid_3"));
            Assert.Null(index.Find("non_existent"));
        }

        [Fact]
        public void Index_RebuildsFromShards_WhenIndexFileIsMissingOrCorrupt()
        {
            var writer = new FailingFileWriter();
            var store = new EventShardStore("events", 100, writer);

            store.Upsert(CreateSampleEvent("evt_1", 20));
            store.Upsert(CreateSampleEvent("evt_2", 140));
            store.Flush();

            Assert.True(writer.Files.ContainsKey("events/_index.json"));

            // Scenario A: Missing _index.json
            writer.Files.Remove("events/_index.json");

            var storeRebuild1 = new EventShardStore("events", 100, writer);
            var rebuiltIndex1 = storeRebuild1.LoadIndex();

            Assert.Equal(2, rebuiltIndex1.Entries.Count);
            Assert.NotNull(rebuiltIndex1.Find("evt_1"));
            Assert.NotNull(rebuiltIndex1.Find("evt_2"));
            Assert.True(writer.Files.ContainsKey("events/_index.json")); // Written back

            // Scenario B: Corrupted _index.json
            writer.Files["events/_index.json"] = "NOT_VALID_JSON{{{{";

            var storeRebuild2 = new EventShardStore("events", 100, writer);
            var rebuiltIndex2 = storeRebuild2.LoadIndex();

            Assert.Equal(2, rebuiltIndex2.Entries.Count);
            Assert.NotNull(rebuiltIndex2.Find("evt_1"));
            Assert.NotNull(rebuiltIndex2.Find("evt_2"));
        }

                /// <summary>
        /// 部分失敗時，索引絕不能跑到分片前面。
        ///
        /// 索引超前 = 懸空指標：它宣稱事件在分片 B，但 B 的磁碟內容是舊的、沒有它。
        /// Load 回傳 null，而 KnownByIndex 仍會把它列給某個英雄——對話層會提供一則
        /// 載不出來的傳聞，靜默失敗。索引落後只是「暫時查不到」，下次沖寫就補上。
        /// </summary>
        [Fact]
        public void Flush_PartialFailure_DoesNotWriteIndexAheadOfShards()
        {
            var writer = new FailingFileWriter();
            var store = new EventShardStore("events", 100, writer);

            store.Upsert(CreateSampleEvent("e_ok", 50));
            store.Upsert(CreateSampleEvent("e_lost", 150));

            writer.FailOn("d0100-0199", FileOp.WriteAllText, 1);
            store.Flush();

            Assert.True(writer.Files.ContainsKey("events/d0000-0099.json"));
            Assert.False(writer.Files.ContainsKey("events/d0100-0199.json"));

            // 一片成功、一片失敗 ⇒ 磁碟上的索引不得含有寫失敗那片裡的事件。
            //（_index.json 檔案本身可能已存在——第一次 Upsert 會觸發索引重建並沖寫一個空索引。
            //  要驗的是「內容有沒有超前」，不是「檔案存不存在」。）
            var indexOnDisk = writer.Files.TryGetValue("events/_index.json", out var ij) ? ij : string.Empty;
            Assert.DoesNotContain("e_lost", indexOnDisk);

            var midway = new EventShardStore("events", 100, writer);
            Assert.Null(midway.LoadIndex().Find("e_lost"));   // 索引落後：查不到，但沒有懸空

            // 全部成功之後索引才落地，且此時它與分片一致
            writer.ClearFailures();
            store.Flush();

            Assert.True(writer.Files.ContainsKey("events/d0100-0199.json"));
            Assert.True(writer.Files.ContainsKey("events/_index.json"));

            var reloaded = new EventShardStore("events", 100, writer);
            var index = reloaded.LoadIndex();
            Assert.NotNull(index.Find("e_ok"));
            Assert.NotNull(index.Find("e_lost"));
            Assert.NotNull(reloaded.Load("e_lost", index));   // 索引指到的事件必須真的載得出來
        }

[Fact]
        public void Flush_PartialFailure_KeepsDirtyFlagAndRetriesNextTime()
        {
            var writer = new FailingFileWriter();
            var store = new EventShardStore("events", 100, writer);

            store.Upsert(CreateSampleEvent("e_shard1", 50));
            store.Upsert(CreateSampleEvent("e_shard2", 150));
            store.Upsert(CreateSampleEvent("e_shard3", 250));

            // Inject failure on writing d0100-0199
            writer.FailOn("d0100-0199", FileOp.WriteAllText, 1);

            store.Flush();

            Assert.True(writer.Files.ContainsKey("events/d0000-0099.json"));
            Assert.True(writer.Files.ContainsKey("events/d0200-0299.json"));
            Assert.False(writer.Files.ContainsKey("events/d0100-0199.json")); // Failed

            // Next flush without failure should retry and succeed
            writer.ClearFailures();
            store.Flush();

            Assert.True(writer.Files.ContainsKey("events/d0100-0199.json")); // Retried and succeeded
        }

        [Fact]
        public void Store_RoundTripsThroughRealFileSystem()
        {
            var tempDir = Path.Combine(Path.GetTempPath(), "VividWorld_Test_" + Guid.NewGuid().ToString("N"), "events");
            try
            {
                Directory.CreateDirectory(tempDir);
                var writer = new SystemFileWriter();
                var store = new EventShardStore(tempDir, 100, writer);

                var evt1 = CreateSampleEvent("real_evt_1", 45);
                var evt2 = CreateSampleEvent("real_evt_2", 145);

                store.Upsert(evt1);
                store.Upsert(evt2);
                store.Flush();

                // Open with fresh store instance from real file system
                var store2 = new EventShardStore(tempDir, 100, writer);
                var index = store2.LoadIndex();

                Assert.Equal(2, index.Entries.Count);

                var loaded1 = store2.Load("real_evt_1", index);
                Assert.NotNull(loaded1);
                Assert.Equal("real_evt_1", loaded1!.EventId);
                Assert.Equal(45, loaded1.Day);

                var loaded2 = store2.Load("real_evt_2", index);
                Assert.NotNull(loaded2);
                Assert.Equal("real_evt_2", loaded2!.EventId);
                Assert.Equal(145, loaded2.Day);
            }
            finally
            {
                try
                {
                    var parentDir = Path.GetDirectoryName(tempDir);
                    if (!string.IsNullOrEmpty(parentDir) && Directory.Exists(parentDir))
                    {
                        Directory.Delete(parentDir, true);
                    }
                }
                catch
                {
                    // Ignore cleanup failure in tests
                }
            }
        }
    }
}
