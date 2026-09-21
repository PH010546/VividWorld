#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using VividWorld.Core.Config;
using VividWorld.Core.Diagnostics;
using VividWorld.Core.Events;
using VividWorld.Core.Persistence;
using Xunit;

namespace VividWorld.Core.Tests
{
    public class RumorSnapshotStoreTests
    {
        private static string CreateTempDir()
        {
            string dir = Path.Combine(Path.GetTempPath(), "VividWorld_Test_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            return dir;
        }

        private static void SafeDelete(string dir)
        {
            try
            {
                if (Directory.Exists(dir))
                {
                    Directory.Delete(dir, recursive: true);
                }
            }
            catch
            {
                // best-effort cleanup
            }
        }

        private static WorldEvent CreateSampleEvent(string id, double day, string type = "duel")
        {
            return new WorldEvent
            {
                EventId = id,
                Type = type,
                Day = day,
                Origin = EventOrigin.Public,
                Participants = new Dictionary<string, string> { { "hero", "hero_1" } },
                Facts = new List<Fact> { new Fact { Id = "f1", Category = FactCategory.What, TextId = "t1" } },
                KnownBy = new List<KnownByEntry> { new KnownByEntry { HeroId = "hero_1", Hop = 0 } }
            };
        }

        // ====================================================================
        // Take Tests (1–13)
        // ====================================================================

        [Fact]
        public void Take_WhenTokenIsEmpty_ReturnsNoToken_AndDoesNotCreateSnapshotsFolder()
        {
            string tempDir = CreateTempDir();
            try
            {
                var result = RumorSnapshotStore.Take(tempDir, "", "save_1", 8);
                Assert.Equal(SnapshotOutcome.NoToken, result.Outcome);
                Assert.False(Directory.Exists(Path.Combine(tempDir, RumorSnapshotStore.SnapshotsFolderName)));
            }
            finally
            {
                SafeDelete(tempDir);
            }
        }

        [Fact]
        public void Take_WhenCampaignRootDoesNotExist_ReturnsNoCampaignFolder_AndLeavesDiskUntouched()
        {
            string nonExistent = Path.Combine(Path.GetTempPath(), "VividWorld_NonExistent_" + Guid.NewGuid().ToString("N"));
            try
            {
                var result = RumorSnapshotStore.Take(nonExistent, "token123", "save_1", 8);
                Assert.Equal(SnapshotOutcome.NoCampaignFolder, result.Outcome);
                Assert.False(Directory.Exists(nonExistent));
            }
            finally
            {
                SafeDelete(nonExistent);
            }
        }

        [Fact]
        public void Take_NormalSnapshot_CopiesAllFilesByteForByte_IncludingEventsSubfolderAndShards()
        {
            string tempDir = CreateTempDir();
            try
            {
                string eventsDir = Path.Combine(tempDir, "events");
                Directory.CreateDirectory(eventsDir);
                File.WriteAllText(Path.Combine(tempDir, "index.json"), "{\"entries\":[]}");
                File.WriteAllText(Path.Combine(eventsDir, "d0000-0099.json"), "{\"events\":[1,2,3]}");
                File.WriteAllText(Path.Combine(eventsDir, "d0100-0199.json"), "{\"events\":[4,5,6]}");

                string token = "token_abc_12345678";
                var result = RumorSnapshotStore.Take(tempDir, token, "save_1", 8);

                Assert.Equal(SnapshotOutcome.Taken, result.Outcome);
                string snapshotDir = Path.Combine(tempDir, RumorSnapshotStore.SnapshotsFolderName, token);
                Assert.True(Directory.Exists(snapshotDir));

                // Byte-for-byte comparison
                string[] liveFiles = Directory.GetFiles(tempDir, "*.*", SearchOption.AllDirectories)
                    .Where(f => !f.Contains(RumorSnapshotStore.SnapshotsFolderName))
                    .ToArray();

                foreach (var liveFile in liveFiles)
                {
                    string rel = liveFile.Substring(tempDir.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                    string target = Path.Combine(snapshotDir, rel);
                    Assert.True(File.Exists(target), $"Expected file {rel} in snapshot.");
                    Assert.Equal(File.ReadAllBytes(liveFile), File.ReadAllBytes(target));
                }
            }
            finally
            {
                SafeDelete(tempDir);
            }
        }

        [Fact]
        public void Take_SnapshotsFolderItselfIsNotCopiedIntoSnapshots()
        {
            string tempDir = CreateTempDir();
            try
            {
                File.WriteAllText(Path.Combine(tempDir, "data.txt"), "some data");

                var res1 = RumorSnapshotStore.Take(tempDir, "token_1", "save_1", 8);
                Assert.Equal(SnapshotOutcome.Taken, res1.Outcome);

                var res2 = RumorSnapshotStore.Take(tempDir, "token_2", "save_2", 8);
                Assert.Equal(SnapshotOutcome.Taken, res2.Outcome);

                string snapshot2Dir = Path.Combine(tempDir, RumorSnapshotStore.SnapshotsFolderName, "token_2");
                Assert.False(Directory.Exists(Path.Combine(snapshot2Dir, RumorSnapshotStore.SnapshotsFolderName)));
            }
            finally
            {
                SafeDelete(tempDir);
            }
        }

        [Fact]
        public void Take_SameTokenRetaken_ClearsDestinationFolderBeforeCopying()
        {
            string tempDir = CreateTempDir();
            try
            {
                File.WriteAllText(Path.Combine(tempDir, "data.txt"), "initial");
                string token = "token_same";

                var res1 = RumorSnapshotStore.Take(tempDir, token, "save_1", 8);
                Assert.Equal(SnapshotOutcome.Taken, res1.Outcome);

                string destDir = Path.Combine(tempDir, RumorSnapshotStore.SnapshotsFolderName, token);
                string strayFile = Path.Combine(destDir, "stray_file.txt");
                File.WriteAllText(strayFile, "i should be deleted on retake");
                Assert.True(File.Exists(strayFile));

                var res2 = RumorSnapshotStore.Take(tempDir, token, "save_1", 8);
                Assert.Equal(SnapshotOutcome.Taken, res2.Outcome);
                Assert.False(File.Exists(strayFile));
            }
            finally
            {
                SafeDelete(tempDir);
            }
        }

        [Fact]
        public void Take_FilesAndBytesMatchSourceCountsAndTotalBytes()
        {
            string tempDir = CreateTempDir();
            try
            {
                byte[] bytes1 = new byte[] { 1, 2, 3, 4, 5 };
                byte[] bytes2 = new byte[] { 10, 20, 30 };
                File.WriteAllBytes(Path.Combine(tempDir, "file1.bin"), bytes1);
                string sub = Path.Combine(tempDir, "sub");
                Directory.CreateDirectory(sub);
                File.WriteAllBytes(Path.Combine(sub, "file2.bin"), bytes2);

                var result = RumorSnapshotStore.Take(tempDir, "token_count", "save_1", 8);
                Assert.Equal(SnapshotOutcome.Taken, result.Outcome);
                Assert.Equal(2, result.Files);
                Assert.Equal(bytes1.Length + bytes2.Length, result.Bytes);
            }
            finally
            {
                SafeDelete(tempDir);
            }
        }

        [Fact]
        public void Take_GeneratesManifestWithSaveNameSlotAndFourFields()
        {
            string tempDir = CreateTempDir();
            try
            {
                File.WriteAllText(Path.Combine(tempDir, "data.txt"), "hello");
                string token = "token_manifest_test";

                var result = RumorSnapshotStore.Take(tempDir, token, "slot_alpha", 8);
                Assert.Equal(SnapshotOutcome.Taken, result.Outcome);

                string manifestPath = Path.Combine(tempDir, RumorSnapshotStore.SnapshotsFolderName, RumorSnapshotStore.ManifestFileName);
                Assert.True(File.Exists(manifestPath));

                var manifest = JObject.Parse(File.ReadAllText(manifestPath));
                var slot = manifest["slots"]?["slot_alpha"] as JObject;
                Assert.NotNull(slot);
                Assert.Equal(token, (string?)slot["token"]);
                Assert.False(string.IsNullOrEmpty((string?)slot["utc"]));
                Assert.Equal(1, (int)slot["files"]!);
                Assert.True((long)slot["bytes"]! > 0);
            }
            finally
            {
                SafeDelete(tempDir);
            }
        }

        [Fact]
        public void Take_WhenSaveNameIsWhitespace_FallsBackToTokenAsSlotKey()
        {
            string tempDir = CreateTempDir();
            try
            {
                File.WriteAllText(Path.Combine(tempDir, "data.txt"), "hello");
                string token = "token_fallback_key";

                var result = RumorSnapshotStore.Take(tempDir, token, "   ", 8);
                Assert.Equal(SnapshotOutcome.Taken, result.Outcome);

                string manifestPath = Path.Combine(tempDir, RumorSnapshotStore.SnapshotsFolderName, RumorSnapshotStore.ManifestFileName);
                var manifest = JObject.Parse(File.ReadAllText(manifestPath));
                var slot = manifest["slots"]?[token] as JObject;
                Assert.NotNull(slot);
                Assert.Equal(token, (string?)slot["token"]);
            }
            finally
            {
                SafeDelete(tempDir);
            }
        }

        [Fact]
        public void Take_SameSaveNameOverwritten_DeletesOldTokenFolder_KeepsNew_AndReportsSlotOverwritten()
        {
            string tempDir = CreateTempDir();
            try
            {
                File.WriteAllText(Path.Combine(tempDir, "data.txt"), "hello");
                string token1 = "token_old_save";
                string token2 = "token_new_save";

                var res1 = RumorSnapshotStore.Take(tempDir, token1, "quicksave", 8);
                Assert.Equal(SnapshotOutcome.Taken, res1.Outcome);

                string snapshotsRoot = Path.Combine(tempDir, RumorSnapshotStore.SnapshotsFolderName);
                Assert.True(Directory.Exists(Path.Combine(snapshotsRoot, token1)));

                var res2 = RumorSnapshotStore.Take(tempDir, token2, "quicksave", 8);
                Assert.Equal(SnapshotOutcome.Taken, res2.Outcome);

                Assert.False(Directory.Exists(Path.Combine(snapshotsRoot, token1)));
                Assert.True(Directory.Exists(Path.Combine(snapshotsRoot, token2)));

                var pruned = res2.Pruned.FirstOrDefault(p => p.Reason == PruneReason.SlotOverwritten);
                Assert.NotNull(pruned);
                Assert.Equal(token1, pruned.Token);
                Assert.Equal("quicksave", pruned.SaveName);

                var manifest = JObject.Parse(File.ReadAllText(Path.Combine(snapshotsRoot, RumorSnapshotStore.ManifestFileName)));
                Assert.Single(((JObject)manifest["slots"]!).Properties());
                Assert.Equal(token2, (string?)manifest["slots"]?["quicksave"]?["token"]);
            }
            finally
            {
                SafeDelete(tempDir);
            }
        }

        [Fact]
        public void Take_WhenExceedingCap_DeletesOldestByUtc_KeptEqualsMax_AndReportsOverCap()
        {
            string tempDir = CreateTempDir();
            try
            {
                File.WriteAllText(Path.Combine(tempDir, "data.txt"), "hello");
                string snapshotsRoot = Path.Combine(tempDir, RumorSnapshotStore.SnapshotsFolderName);

                // Slot 1
                RumorSnapshotStore.Take(tempDir, "token_cap_1", "save_1", 2);

                // Ensure token_cap_1 has older UTC in manifest
                string manifestPath = Path.Combine(snapshotsRoot, RumorSnapshotStore.ManifestFileName);
                var manifest = JObject.Parse(File.ReadAllText(manifestPath));
                manifest["slots"]!["save_1"]!["utc"] = "2026-01-01T00:00:00.000Z";
                File.WriteAllText(manifestPath, manifest.ToString(Formatting.Indented));

                // Slot 2
                RumorSnapshotStore.Take(tempDir, "token_cap_2", "save_2", 2);

                // Slot 3 -> exceeds cap of 2
                var res3 = RumorSnapshotStore.Take(tempDir, "token_cap_3", "save_3", 2);

                Assert.Equal(SnapshotOutcome.Taken, res3.Outcome);
                Assert.Equal(2, res3.Kept);

                // token_cap_1 should be pruned (oldest)
                Assert.False(Directory.Exists(Path.Combine(snapshotsRoot, "token_cap_1")));
                Assert.True(Directory.Exists(Path.Combine(snapshotsRoot, "token_cap_2")));
                Assert.True(Directory.Exists(Path.Combine(snapshotsRoot, "token_cap_3")));

                var overCapPruned = res3.Pruned.FirstOrDefault(p => p.Reason == PruneReason.OverCap);
                Assert.NotNull(overCapPruned);
                Assert.Equal("token_cap_1", overCapPruned.Token);
                Assert.Equal("save_1", overCapPruned.SaveName);
            }
            finally
            {
                SafeDelete(tempDir);
            }
        }

        /// <summary>SNAP1 把這條的語意反過來了：0 或負數＝**不限份數**（原本是「當成 1」）。
        /// 份數由玩家在遊戲裡自己刪，不由上限自動剪掉。</summary>
        [Fact]
        public void Take_WhenMaxSnapshotsIsZeroOrNegative_KeepsEverySnapshot()
        {
            string tempDir = CreateTempDir();
            try
            {
                File.WriteAllText(Path.Combine(tempDir, "data.txt"), "hello");
                string snapshotsRoot = Path.Combine(tempDir, RumorSnapshotStore.SnapshotsFolderName);

                RumorSnapshotStore.Take(tempDir, "token_zero_1", "save_1", 0);

                // Manually mark save_1 older so order is clear
                string manifestPath = Path.Combine(snapshotsRoot, RumorSnapshotStore.ManifestFileName);
                var manifest = JObject.Parse(File.ReadAllText(manifestPath));
                manifest["slots"]!["save_1"]!["utc"] = "2026-01-01T00:00:00.000Z";
                File.WriteAllText(manifestPath, manifest.ToString(Formatting.Indented));

                var res2 = RumorSnapshotStore.Take(tempDir, "token_zero_2", "save_2", -5);

                Assert.Equal(SnapshotOutcome.Taken, res2.Outcome);
                Assert.Equal(2, res2.Kept);
                Assert.Empty(res2.Pruned.Where(p => p.Reason == PruneReason.OverCap));
                Assert.True(Directory.Exists(Path.Combine(snapshotsRoot, "token_zero_1")));
                Assert.True(Directory.Exists(Path.Combine(snapshotsRoot, "token_zero_2")));
            }
            finally
            {
                SafeDelete(tempDir);
            }
        }

        [Fact]
        public void Take_WhenManifestIsCorruptedJson_ReconstructsCleanlyAndSucceeds()
        {
            string tempDir = CreateTempDir();
            try
            {
                File.WriteAllText(Path.Combine(tempDir, "data.txt"), "hello");
                string snapshotsRoot = Path.Combine(tempDir, RumorSnapshotStore.SnapshotsFolderName);
                Directory.CreateDirectory(snapshotsRoot);

                string manifestPath = Path.Combine(snapshotsRoot, RumorSnapshotStore.ManifestFileName);
                File.WriteAllText(manifestPath, "{ corrupted json syntax !!! ");

                var result = RumorSnapshotStore.Take(tempDir, "token_recover", "save_1", 8);
                Assert.Equal(SnapshotOutcome.Taken, result.Outcome);

                // Manifest is reconstructed as valid JSON with current slot
                Assert.True(File.Exists(manifestPath));
                var manifest = JObject.Parse(File.ReadAllText(manifestPath));
                Assert.NotNull(manifest["slots"]?["save_1"]);
            }
            finally
            {
                SafeDelete(tempDir);
            }
        }

        [Fact]
        public void Take_OrphanFoldersNotReferencedInManifest_AreDeletedAndReported()
        {
            string tempDir = CreateTempDir();
            try
            {
                File.WriteAllText(Path.Combine(tempDir, "data.txt"), "hello");
                string snapshotsRoot = Path.Combine(tempDir, RumorSnapshotStore.SnapshotsFolderName);
                string orphanDir = Path.Combine(snapshotsRoot, "orphan_stray_token");
                Directory.CreateDirectory(orphanDir);
                File.WriteAllText(Path.Combine(orphanDir, "stray.txt"), "i am stray");

                var result = RumorSnapshotStore.Take(tempDir, "token_active", "save_1", 8);

                Assert.Equal(SnapshotOutcome.Taken, result.Outcome);
                Assert.False(Directory.Exists(orphanDir));

                var orphanPruned = result.Pruned.FirstOrDefault(p => p.Reason == PruneReason.OrphanFolder);
                Assert.NotNull(orphanPruned);
                Assert.Equal("orphan_stray_token", orphanPruned.Token);
            }
            finally
            {
                SafeDelete(tempDir);
            }
        }

        // ====================================================================
        // Restore Tests (14–21)
        // ====================================================================

        [Fact]
        public void Restore_WhenTokenIsEmpty_ReturnsNoToken_AndLeavesLiveFilesUntouched()
        {
            string tempDir = CreateTempDir();
            try
            {
                string liveFile = Path.Combine(tempDir, "live.txt");
                File.WriteAllText(liveFile, "live untouched");

                var result = RumorSnapshotStore.Restore(tempDir, "");
                Assert.Equal(SnapshotOutcome.NoToken, result.Outcome);
                Assert.Equal("live untouched", File.ReadAllText(liveFile));
            }
            finally
            {
                SafeDelete(tempDir);
            }
        }

        [Fact]
        public void Restore_WhenTokenFolderDoesNotExist_ReturnsSnapshotMissing_AndLeavesLiveFilesUntouched()
        {
            string tempDir = CreateTempDir();
            try
            {
                string liveFile = Path.Combine(tempDir, "live.txt");
                File.WriteAllText(liveFile, "live untouched");

                var result = RumorSnapshotStore.Restore(tempDir, "non_existent_token");
                Assert.Equal(SnapshotOutcome.SnapshotMissing, result.Outcome);
                Assert.Equal("live untouched", File.ReadAllText(liveFile));
            }
            finally
            {
                SafeDelete(tempDir);
            }
        }

        [Fact]
        public void Restore_WhenSnapshotFolderExistsButIsEmpty_ReturnsSnapshotEmpty_AndLeavesLiveFilesUntouched()
        {
            string tempDir = CreateTempDir();
            try
            {
                string liveFile = Path.Combine(tempDir, "live.txt");
                File.WriteAllText(liveFile, "live must survive");

                string snapshotsRoot = Path.Combine(tempDir, RumorSnapshotStore.SnapshotsFolderName);
                string emptySnapshot = Path.Combine(snapshotsRoot, "empty_token");
                Directory.CreateDirectory(emptySnapshot);

                var result = RumorSnapshotStore.Restore(tempDir, "empty_token");
                Assert.Equal(SnapshotOutcome.SnapshotEmpty, result.Outcome);
                Assert.Equal("live must survive", File.ReadAllText(liveFile));
            }
            finally
            {
                SafeDelete(tempDir);
            }
        }

        /// <summary>
        /// 一次中途失敗的複製會留下「有東西但不完整」的資料夾，而 manifest 裡不會有那個 token
        /// （manifest 是 Take 的最後一步）。這種照片**絕不能**蓋回現場——那是整個系統唯一一個
        /// 會刪東西的地方，蓋錯就是把完好的戰役資料換成半份。
        /// </summary>
        [Fact]
        public void Restore_SnapshotNotListedInManifest_ReturnsSnapshotIncomplete_AndLeavesLiveFilesUntouched()
        {
            string tempDir = CreateTempDir();
            try
            {
                string liveFile = Path.Combine(tempDir, "live.txt");
                File.WriteAllText(liveFile, "live must survive");

                // 手工造一個「複製到一半就停了」的資料夾：有內容，但 manifest 完全不存在
                string snapshotsRoot = Path.Combine(tempDir, RumorSnapshotStore.SnapshotsFolderName);
                string halfSnapshot = Path.Combine(snapshotsRoot, "half_written_token");
                Directory.CreateDirectory(halfSnapshot);
                File.WriteAllText(Path.Combine(halfSnapshot, "partial.txt"), "only half of the copy landed");

                var result = RumorSnapshotStore.Restore(tempDir, "half_written_token");

                Assert.Equal(SnapshotOutcome.SnapshotIncomplete, result.Outcome);
                Assert.Contains("not listed in the manifest", result.Reason);
                Assert.Equal("live must survive", File.ReadAllText(liveFile));
            }
            finally
            {
                SafeDelete(tempDir);
            }
        }

        /// <summary>
        /// 第二道關：token 在 manifest 裡，但資料夾裡的檔數與 manifest 記的不一樣
        /// （例如複製完之後有檔案被砍掉）⇒ 同樣拒絕還原。
        /// </summary>
        [Fact]
        public void Restore_SnapshotFileCountDoesNotMatchManifest_ReturnsSnapshotIncomplete_AndLeavesLiveFilesUntouched()
        {
            string tempDir = CreateTempDir();
            try
            {
                string fileA = Path.Combine(tempDir, "fileA.txt");
                string fileB = Path.Combine(tempDir, "fileB.txt");
                File.WriteAllText(fileA, "A_original");
                File.WriteAllText(fileB, "B_original");

                string token = "token_count_mismatch";
                var takeRes = RumorSnapshotStore.Take(tempDir, token, "slot1", 8);
                Assert.Equal(SnapshotOutcome.Taken, takeRes.Outcome);
                Assert.Equal(2, takeRes.Files);

                // 照片拍好之後從裡面砍掉一個檔 ⇒ manifest 說 2 個，實際只剩 1 個
                string snapshotsRoot = Path.Combine(tempDir, RumorSnapshotStore.SnapshotsFolderName);
                File.Delete(Path.Combine(snapshotsRoot, token, "fileB.txt"));

                File.WriteAllText(fileA, "A_changed_after_snapshot");

                var result = RumorSnapshotStore.Restore(tempDir, token);

                Assert.Equal(SnapshotOutcome.SnapshotIncomplete, result.Outcome);
                Assert.Contains("holds 1 file(s) but the manifest recorded 2", result.Reason);
                // 現場一個位元組都不准動
                Assert.Equal("A_changed_after_snapshot", File.ReadAllText(fileA));
                Assert.Equal("B_original", File.ReadAllText(fileB));
            }
            finally
            {
                SafeDelete(tempDir);
            }
        }

        [Fact]
        public void Restore_NormalSnapshot_RemovesNewFiles_RestoresModifiedFiles_RestoresDeletedFiles()
        {
            string tempDir = CreateTempDir();
            try
            {
                string fileA = Path.Combine(tempDir, "fileA.txt");
                string fileB = Path.Combine(tempDir, "fileB.txt");
                File.WriteAllText(fileA, "version_A_original");
                File.WriteAllText(fileB, "version_B_original");

                string token = "token_snap_test";
                var takeRes = RumorSnapshotStore.Take(tempDir, token, "slot1", 8);
                Assert.Equal(SnapshotOutcome.Taken, takeRes.Outcome);

                // Modify state: fileA changed, fileB deleted, fileC added
                AtomicFile.Write(new SystemFileWriter(), fileA, "version_A_corrupted");
                File.Delete(fileB);
                string fileC = Path.Combine(tempDir, "fileC_new.txt");
                File.WriteAllText(fileC, "i was created after snapshot");

                var restoreRes = RumorSnapshotStore.Restore(tempDir, token);
                Assert.Equal(SnapshotOutcome.Restored, restoreRes.Outcome);

                Assert.Equal("version_A_original", File.ReadAllText(fileA));
                Assert.True(File.Exists(fileB));
                Assert.Equal("version_B_original", File.ReadAllText(fileB));
                Assert.False(File.Exists(fileC));
            }
            finally
            {
                SafeDelete(tempDir);
            }
        }

        [Fact]
        public void Restore_DoesNotTouchSnapshotsFolderOrOtherTokens()
        {
            string tempDir = CreateTempDir();
            try
            {
                File.WriteAllText(Path.Combine(tempDir, "data.txt"), "data_v1");
                RumorSnapshotStore.Take(tempDir, "token_a", "save_a", 8);

                AtomicFile.Write(new SystemFileWriter(), Path.Combine(tempDir, "data.txt"), "data_v2");
                RumorSnapshotStore.Take(tempDir, "token_b", "save_b", 8);

                string snapshotsRoot = Path.Combine(tempDir, RumorSnapshotStore.SnapshotsFolderName);
                Assert.True(Directory.Exists(Path.Combine(snapshotsRoot, "token_a")));
                Assert.True(Directory.Exists(Path.Combine(snapshotsRoot, "token_b")));

                // Restore token_a
                var restoreRes = RumorSnapshotStore.Restore(tempDir, "token_a");
                Assert.Equal(SnapshotOutcome.Restored, restoreRes.Outcome);

                // token_b in _snapshots must still be intact
                Assert.True(Directory.Exists(Path.Combine(snapshotsRoot, "token_b")));
                Assert.Equal("data_v1", File.ReadAllText(Path.Combine(tempDir, "data.txt")));
            }
            finally
            {
                SafeDelete(tempDir);
            }
        }

        [Fact]
        public void Restore_RoundTrip_TakeA_Modify_TakeB_RestoreA_RestoreB_MaintainsIntactData()
        {
            string tempDir = CreateTempDir();
            try
            {
                string dataFile = Path.Combine(tempDir, "data.txt");
                File.WriteAllText(dataFile, "state_A");
                RumorSnapshotStore.Take(tempDir, "token_A", "save_A", 8);

                AtomicFile.Write(new SystemFileWriter(), dataFile, "state_B");
                RumorSnapshotStore.Take(tempDir, "token_B", "save_B", 8);

                RumorSnapshotStore.Restore(tempDir, "token_A");
                Assert.Equal("state_A", File.ReadAllText(dataFile));

                RumorSnapshotStore.Restore(tempDir, "token_B");
                Assert.Equal("state_B", File.ReadAllText(dataFile));
            }
            finally
            {
                SafeDelete(tempDir);
            }
        }

        [Fact]
        public void Restore_FilesAndBytesMatchSource()
        {
            string tempDir = CreateTempDir();
            try
            {
                byte[] data = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 };
                File.WriteAllBytes(Path.Combine(tempDir, "file.bin"), data);

                var takeRes = RumorSnapshotStore.Take(tempDir, "token_src_match", "save_1", 8);
                Assert.Equal(1, takeRes.Files);
                Assert.Equal(data.Length, takeRes.Bytes);

                var restoreRes = RumorSnapshotStore.Restore(tempDir, "token_src_match");
                Assert.Equal(SnapshotOutcome.Restored, restoreRes.Outcome);
                Assert.Equal(takeRes.Files, restoreRes.Files);
                Assert.Equal(takeRes.Bytes, restoreRes.Bytes);
            }
            finally
            {
                SafeDelete(tempDir);
            }
        }

        [Fact]
        public void Restore_NestedSubdirectoryFilesRestoredProperly()
        {
            string tempDir = CreateTempDir();
            try
            {
                string eventsDir = Path.Combine(tempDir, "events");
                Directory.CreateDirectory(eventsDir);
                string shard = Path.Combine(eventsDir, "d0000-0099.json");
                File.WriteAllText(shard, "{\"events\":[{\"id\":\"evt_orig\"}]}");

                var takeRes = RumorSnapshotStore.Take(tempDir, "token_nested", "save_1", 8);
                Assert.Equal(SnapshotOutcome.Taken, takeRes.Outcome);

                // Overwrite shard and add another nested file
                AtomicFile.Write(new SystemFileWriter(), shard, "{\"events\":[{\"id\":\"evt_overwritten\"}]}");
                string extraNested = Path.Combine(eventsDir, "extra.json");
                File.WriteAllText(extraNested, "extra");

                var restoreRes = RumorSnapshotStore.Restore(tempDir, "token_nested");
                Assert.Equal(SnapshotOutcome.Restored, restoreRes.Outcome);

                Assert.Equal("{\"events\":[{\"id\":\"evt_orig\"}]}", File.ReadAllText(shard));
                Assert.False(File.Exists(extraNested));
            }
            finally
            {
                SafeDelete(tempDir);
            }
        }

        // ====================================================================
        // Config Tests (39–40)
        // ====================================================================

        [Fact]
        public void Normalize_MaxSnapshotsClampedTo64_AndEmitsClampNotice()
        {
            var config = new VividWorldConfig();
            config.Persistence.MaxSnapshots = 1000;
            var notices = new List<ClampNotice>();

            config.Normalize(notices);

            Assert.Equal(64, config.Persistence.MaxSnapshots);
            var notice = notices.FirstOrDefault(n => n.Key == "persistence.maxSnapshots");
            Assert.NotNull(notice);
            Assert.Equal("persistence.maxSnapshots", notice.Key);
            Assert.Equal(1000, Convert.ToInt32(notice.Requested));
            Assert.Equal(64, Convert.ToInt32(notice.Applied));
            Assert.Equal("0..64", notice.AllowedRange);   // SNAP1：下限改成 0（= 不限份數）
        }

        [Fact]
        public void ConfigMerge_ExistingConfigJson_KeepsFourKeys_AndDoesNotDropTheRetiredRevertWithSaves()
        {
            string fixturePath = Path.Combine(AppContext.BaseDirectory, "Fixtures", "config.json");
            Assert.True(File.Exists(fixturePath), $"Fixture file must exist at {fixturePath}");

            string existingJson = File.ReadAllText(fixturePath);
            var existingJObj = JObject.Parse(existingJson);
            var canonical = new VividWorldConfig();
            var canonicalJObj = JObject.Parse(VividJson.Write(canonical));

            var mergeResult = ConfigMerge.AddMissingKeys(existingJObj, canonicalJObj);
            var persistenceObj = mergeResult.Merged["persistence"];
            Assert.NotNull(persistenceObj);

            Assert.Equal(50, (int)persistenceObj["shardDays"]!);
            Assert.Equal(16, (int)persistenceObj["maxSnapshots"]!);
            Assert.Equal(12000, (int)persistenceObj["maxPendingIngestChars"]!);
            Assert.Equal(12, (int)persistenceObj["maxFactsPerEvent"]!);

            // v3.28 之後 `revertWithSaves` 不再是設定鍵。他的 config.json 裡那一行還在，
            // 而合併**不准把它丟掉**——使用者的檔案不該因為我們退休一個鍵而被改寫。
            // 它落進 PersistenceConfig.Extra（JsonExtensionData），讀得進、寫得回、不影響任何行為。
            Assert.NotNull(persistenceObj["revertWithSaves"]);
            Assert.False((bool)persistenceObj["revertWithSaves"]!);
            Assert.Null(typeof(VividWorld.Core.Config.PersistenceConfig).GetProperty("RevertWithSaves"));
        }

        // ====================================================================
        // Scale Tests (41–42)
        // ====================================================================

        [Fact]
        public void Scale_Synthesizes30ShardsAnd3000Events_TakeMatchesCountsAndBytes()
        {
            string tempDir = CreateTempDir();
            try
            {
                string eventsDir = Path.Combine(tempDir, "events");
                Directory.CreateDirectory(eventsDir);
                var writer = new SystemFileWriter();
                var store = new EventShardStore(eventsDir, 100, writer);

                for (int s = 0; s < 30; s++)
                {
                    for (int e = 0; e < 100; e++)
                    {
                        double day = (s * 100) + (e * 0.5);
                        store.Upsert(CreateSampleEvent($"scale_evt_{s}_{e}", day));
                    }
                }
                store.Flush();

                int expectedFiles = Directory.GetFiles(tempDir, "*.*", SearchOption.AllDirectories).Length;
                long expectedBytes = Directory.GetFiles(tempDir, "*.*", SearchOption.AllDirectories)
                    .Sum(f => new FileInfo(f).Length);

                Assert.Equal(31, expectedFiles); // 30 shards + 1 events/_index.json

                var takeRes = RumorSnapshotStore.Take(tempDir, "token_scale_3000", "scale_save", 8);
                Assert.Equal(SnapshotOutcome.Taken, takeRes.Outcome);
                Assert.Equal(expectedFiles, takeRes.Files);
                Assert.Equal(expectedBytes, takeRes.Bytes);
            }
            finally
            {
                SafeDelete(tempDir);
            }
        }

        [Fact]
        public void Scale_Synthesizes30ShardsAnd3000Events_RestoreMatchesByteForByte()
        {
            string tempDir = CreateTempDir();
            try
            {
                string eventsDir = Path.Combine(tempDir, "events");
                Directory.CreateDirectory(eventsDir);
                var writer = new SystemFileWriter();
                var store = new EventShardStore(eventsDir, 100, writer);

                for (int s = 0; s < 30; s++)
                {
                    for (int e = 0; e < 100; e++)
                    {
                        double day = (s * 100) + (e * 0.5);
                        store.Upsert(CreateSampleEvent($"scale_evt_{s}_{e}", day));
                    }
                }
                store.Flush();

                var originalBytes = new Dictionary<string, byte[]>();
                foreach (var file in Directory.GetFiles(tempDir, "*.*", SearchOption.AllDirectories))
                {
                    string rel = file.Substring(tempDir.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                    originalBytes[rel] = File.ReadAllBytes(file);
                }

                string token = "token_scale_restore";
                var takeRes = RumorSnapshotStore.Take(tempDir, token, "scale_save", 8);
                Assert.Equal(SnapshotOutcome.Taken, takeRes.Outcome);

                // Scramble live files: delete 10 shards, modify 5 shards, add extra files
                var files = Directory.GetFiles(eventsDir, "*.json");
                for (int i = 0; i < 10 && i < files.Length; i++)
                {
                    File.Delete(files[i]);
                }
                for (int i = 10; i < 15 && i < files.Length; i++)
                {
                    AtomicFile.Write(new SystemFileWriter(), files[i], "corrupted data");
                }
                File.WriteAllText(Path.Combine(tempDir, "stray_file.txt"), "stray");

                var restoreRes = RumorSnapshotStore.Restore(tempDir, token);
                Assert.Equal(SnapshotOutcome.Restored, restoreRes.Outcome);

                Assert.False(File.Exists(Path.Combine(tempDir, "stray_file.txt")));

                foreach (var kvp in originalBytes)
                {
                    string livePath = Path.Combine(tempDir, kvp.Key);
                    Assert.True(File.Exists(livePath), $"Expected file {kvp.Key} to be restored.");
                    Assert.Equal(kvp.Value, File.ReadAllBytes(livePath));
                }
            }
            finally
            {
                SafeDelete(tempDir);
            }
        }

        // ====================================================================
        // SNAP2 Tests: Hardlink Snapshots (Items 1–7 + ConfigMerge)
        // ====================================================================

        [Fact]
        public void SNAP2_1_HardlinkSnapshotsTrue_WhenLiveFileOverwrittenViaAtomicFile_SnapshotContentRemainsUnchanged()
        {
            string tempDir = CreateTempDir();
            try
            {
                string eventsDir = Path.Combine(tempDir, "events");
                Directory.CreateDirectory(eventsDir);
                string shardPath = Path.Combine(eventsDir, "d0000-0099.json");
                File.WriteAllText(shardPath, "OLD_LIVE_SHARD_CONTENT");

                string token = "snap_hl_true_test";
                var result = RumorSnapshotStore.Take(tempDir, token, "slot1", 8, hardlink: true);
                Assert.Equal(SnapshotOutcome.Taken, result.Outcome);
                Assert.True(result.LinkedFiles > 0, $"Expected at least 1 linked file, got {result.LinkedFiles}");

                // Modify live file via AtomicFile (Write .tmp then File.Replace)
                bool atomicSuccess = AtomicFile.Write(new SystemFileWriter(), shardPath, "NEW_LIVE_SHARD_CONTENT");
                Assert.True(atomicSuccess);

                // Verify live file has new content
                Assert.Equal("NEW_LIVE_SHARD_CONTENT", File.ReadAllText(shardPath));

                // Verify snapshot file still has old content (hardlink broken on Replace)
                string snapFile = Path.Combine(tempDir, RumorSnapshotStore.SnapshotsFolderName, token, "events", "d0000-0099.json");
                Assert.True(File.Exists(snapFile));
                Assert.Equal("OLD_LIVE_SHARD_CONTENT", File.ReadAllText(snapFile));
            }
            finally
            {
                SafeDelete(tempDir);
            }
        }

        [Fact]
        public void SNAP2_2_HardlinkSnapshotsFalse_WhenLiveFileOverwrittenViaAtomicFile_SnapshotContentRemainsUnchanged()
        {
            string tempDir = CreateTempDir();
            try
            {
                string eventsDir = Path.Combine(tempDir, "events");
                Directory.CreateDirectory(eventsDir);
                string shardPath = Path.Combine(eventsDir, "d0000-0099.json");
                File.WriteAllText(shardPath, "OLD_LIVE_SHARD_CONTENT");

                string token = "snap_hl_false_test";
                var result = RumorSnapshotStore.Take(tempDir, token, "slot1", 8, hardlink: false);
                Assert.Equal(SnapshotOutcome.Taken, result.Outcome);
                Assert.Equal(0, result.LinkedFiles);
                Assert.True(result.CopiedFiles > 0);

                // Modify live file via AtomicFile
                bool atomicSuccess = AtomicFile.Write(new SystemFileWriter(), shardPath, "NEW_LIVE_SHARD_CONTENT");
                Assert.True(atomicSuccess);

                // Live has new content
                Assert.Equal("NEW_LIVE_SHARD_CONTENT", File.ReadAllText(shardPath));

                // Snapshot still has old content (physical copy is independent)
                string snapFile = Path.Combine(tempDir, RumorSnapshotStore.SnapshotsFolderName, token, "events", "d0000-0099.json");
                Assert.True(File.Exists(snapFile));
                Assert.Equal("OLD_LIVE_SHARD_CONTENT", File.ReadAllText(snapFile));
            }
            finally
            {
                SafeDelete(tempDir);
            }
        }

        [Fact]
        public void SNAP2_3_HardlinkSnapshot_WhenSnapshotDeleted_LiveDataRemainsIntact()
        {
            string tempDir = CreateTempDir();
            try
            {
                string eventsDir = Path.Combine(tempDir, "events");
                Directory.CreateDirectory(eventsDir);
                string file1 = Path.Combine(tempDir, "index.json");
                string file2 = Path.Combine(eventsDir, "d0000-0099.json");
                File.WriteAllText(file1, "{\"version\":1}");
                File.WriteAllText(file2, "{\"events\":[1,2,3]}");

                byte[] expected1 = File.ReadAllBytes(file1);
                byte[] expected2 = File.ReadAllBytes(file2);

                string token = "snap_to_delete";
                var result = RumorSnapshotStore.Take(tempDir, token, "save_del", 8, hardlink: true);
                Assert.Equal(SnapshotOutcome.Taken, result.Outcome);
                Assert.True(result.LinkedFiles > 0);

                // Delete the snapshot via RumorSnapshotStore.Delete
                var delResult = RumorSnapshotStore.Delete(tempDir, new[] { token });
                Assert.Single(delResult.Deleted);
                Assert.Empty(delResult.Failed);

                // Assert live data remains completely intact and byte-for-byte identical
                Assert.True(File.Exists(file1));
                Assert.True(File.Exists(file2));
                Assert.Equal(expected1, File.ReadAllBytes(file1));
                Assert.Equal(expected2, File.ReadAllBytes(file2));
            }
            finally
            {
                SafeDelete(tempDir);
            }
        }

        [Fact]
        public void SNAP2_4_HardlinkSnapshot_WhenLiveDataDeleted_SnapshotRemainsIntact()
        {
            string tempDir = CreateTempDir();
            try
            {
                string eventsDir = Path.Combine(tempDir, "events");
                Directory.CreateDirectory(eventsDir);
                string file1 = Path.Combine(tempDir, "index.json");
                string file2 = Path.Combine(eventsDir, "d0000-0099.json");
                File.WriteAllText(file1, "{\"version\":1}");
                File.WriteAllText(file2, "{\"events\":[1,2,3]}");

                byte[] expected1 = File.ReadAllBytes(file1);
                byte[] expected2 = File.ReadAllBytes(file2);

                string token = "snap_survives_live_delete";
                var result = RumorSnapshotStore.Take(tempDir, token, "save_live_del", 8, hardlink: true);
                Assert.Equal(SnapshotOutcome.Taken, result.Outcome);

                // Delete live files
                File.Delete(file1);
                File.Delete(file2);
                Directory.Delete(eventsDir, recursive: true);

                Assert.False(File.Exists(file1));
                Assert.False(File.Exists(file2));

                // Assert snapshot files still exist and match original bytes
                string snapFile1 = Path.Combine(tempDir, RumorSnapshotStore.SnapshotsFolderName, token, "index.json");
                string snapFile2 = Path.Combine(tempDir, RumorSnapshotStore.SnapshotsFolderName, token, "events", "d0000-0099.json");
                Assert.True(File.Exists(snapFile1));
                Assert.True(File.Exists(snapFile2));
                Assert.Equal(expected1, File.ReadAllBytes(snapFile1));
                Assert.Equal(expected2, File.ReadAllBytes(snapFile2));
            }
            finally
            {
                SafeDelete(tempDir);
            }
        }

        [Fact]
        public void SNAP2_5_Restore_NeverUsesHardlinks_WhenLiveFileDirectlyModifiedAfterRestore_SnapshotRemainsUnchanged()
        {
            string tempDir = CreateTempDir();
            try
            {
                string file = Path.Combine(tempDir, "data.txt");
                File.WriteAllText(file, "ORIGINAL_SNAPSHOT_CONTENT");

                string token = "snap_restore_redline";
                var takeRes = RumorSnapshotStore.Take(tempDir, token, "save_redline", 8, hardlink: true);
                Assert.Equal(SnapshotOutcome.Taken, takeRes.Outcome);

                // Restore to live folder (Restore MUST physically copy, never hardlink)
                var restoreRes = RumorSnapshotStore.Restore(tempDir, token);
                Assert.Equal(SnapshotOutcome.Restored, restoreRes.Outcome);

                // Directly overwrite the live file in-place without AtomicFile
                File.WriteAllText(file, "DIRECT_INPLACE_OVERWRITE_MUTATION");
                Assert.Equal("DIRECT_INPLACE_OVERWRITE_MUTATION", File.ReadAllText(file));

                // Assert that snapshot file was NOT modified by the in-place overwrite
                string snapFile = Path.Combine(tempDir, RumorSnapshotStore.SnapshotsFolderName, token, "data.txt");
                Assert.True(File.Exists(snapFile));
                Assert.Equal("ORIGINAL_SNAPSHOT_CONTENT", File.ReadAllText(snapFile));
            }
            finally
            {
                SafeDelete(tempDir);
            }
        }

        [Fact]
        public void SNAP2_6_LinkedFilesPlusCopiedFiles_EqualsTotalFiles()
        {
            string tempDir = CreateTempDir();
            try
            {
                string eventsDir = Path.Combine(tempDir, "events");
                Directory.CreateDirectory(eventsDir);
                File.WriteAllText(Path.Combine(tempDir, "a.txt"), "A");
                File.WriteAllText(Path.Combine(tempDir, "b.txt"), "B");
                File.WriteAllText(Path.Combine(eventsDir, "c.txt"), "C");

                var resHardlink = RumorSnapshotStore.Take(tempDir, "token_hl", "slot1", 8, hardlink: true);
                Assert.Equal(SnapshotOutcome.Taken, resHardlink.Outcome);
                Assert.Equal(3, resHardlink.Files);
                Assert.Equal(resHardlink.Files, resHardlink.LinkedFiles + resHardlink.CopiedFiles);

                var resCopy = RumorSnapshotStore.Take(tempDir, "token_cp", "slot2", 8, hardlink: false);
                Assert.Equal(SnapshotOutcome.Taken, resCopy.Outcome);
                Assert.Equal(3, resCopy.Files);
                Assert.Equal(resCopy.Files, resCopy.LinkedFiles + resCopy.CopiedFiles);
            }
            finally
            {
                SafeDelete(tempDir);
            }
        }

        [Fact]
        public void SNAP2_7_HardlinkSnapshotsFalse_LinkedFilesEqualsZero()
        {
            string tempDir = CreateTempDir();
            try
            {
                File.WriteAllText(Path.Combine(tempDir, "file1.txt"), "hello");
                File.WriteAllText(Path.Combine(tempDir, "file2.txt"), "world");

                string token = "token_hl_zero";
                var result = RumorSnapshotStore.Take(tempDir, token, "save_hl_zero", 8, hardlink: false);
                Assert.Equal(SnapshotOutcome.Taken, result.Outcome);
                Assert.Equal(0, result.LinkedFiles);
                Assert.Equal(2, result.CopiedFiles);
                Assert.Equal(2, result.Files);
                Assert.Equal(result.Files, result.CopiedFiles);

                // Note indicates hardlinks disabled by config
                Assert.NotNull(result.HardlinkNote);
                Assert.Equal(SnapshotLogFormatter.FormatHardlinksDisabled(token, 2), result.HardlinkNote);
            }
            finally
            {
                SafeDelete(tempDir);
            }
        }

        [Fact]
        public void ConfigMerge_AddsHardlinkSnapshots_WhenMissing_AndDefaultsToTrue()
        {
            var existingJObj = new JObject
            {
                ["persistence"] = new JObject
                {
                    ["shardDays"] = 100,
                    ["maxSnapshots"] = 0
                }
            };
            var canonical = new VividWorldConfig();
            var canonicalJObj = JObject.Parse(VividJson.Write(canonical));

            var result = ConfigMerge.AddMissingKeys(existingJObj, canonicalJObj);
            var persistence = result.Merged["persistence"];
            Assert.NotNull(persistence);
            Assert.True((bool)persistence!["hardlinkSnapshots"]!);
            Assert.Equal(100, (int)persistence["shardDays"]!);
            Assert.Equal(0, (int)persistence["maxSnapshots"]!);
        }
    }
}
