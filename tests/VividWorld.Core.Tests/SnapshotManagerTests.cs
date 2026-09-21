#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using VividWorld.Core.Diagnostics;
using VividWorld.Core.Persistence;
using Xunit;

namespace VividWorld.Core.Tests
{
    /// <summary>
    /// SNAP1：份數不再由上限決定（`maxSnapshots = 0`），改由玩家在遊戲裡自己刪。
    /// 這一份測的是 Core 那一半：不限份數的剪除行為、刪除 API、清單文字。
    /// </summary>
    public class SnapshotManagerTests
    {
        private static string CreateTempDir()
        {
            string dir = Path.Combine(Path.GetTempPath(), "VividWorld_SnapMgr_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            return dir;
        }

        private static void SafeDelete(string dir)
        {
            try
            {
                if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
            }
            catch
            {
                // best-effort
            }
        }

        /// <summary>現場資料夾裡放一個檔案，讓每次 Take 都真的有東西可以複製。</summary>
        private static void SeedLiveData(string campaignRoot, string content)
        {
            string eventsDir = Path.Combine(campaignRoot, "events");
            Directory.CreateDirectory(eventsDir);
            File.WriteAllText(Path.Combine(eventsDir, "d100-199.json"), content);
        }

        private static string TokenOf(int i) => i.ToString("D2") + new string('a', 30);

        // ====================================================================
        // 1. 份數上限
        // ====================================================================

        [Fact]
        public void Take_WhenMaxSnapshotsIsZero_KeepsEverySnapshot()
        {
            string dir = CreateTempDir();
            try
            {
                SeedLiveData(dir, "one");

                for (int i = 0; i < 10; i++)
                {
                    var result = RumorSnapshotStore.Take(dir, TokenOf(i), "save_" + i, maxSnapshots: 0);
                    Assert.Equal(SnapshotOutcome.Taken, result.Outcome);
                    Assert.Empty(result.Pruned.Where(p => p.Reason == PruneReason.OverCap));
                }

                var inv = RumorSnapshotStore.Inventory(dir);
                Assert.Equal(10, inv.Slots.Count);
                Assert.All(inv.Slots, s => Assert.True(s.ExistsOnDisk));
            }
            finally
            {
                SafeDelete(dir);
            }
        }

        [Fact]
        public void Take_WhenMaxSnapshotsIsThree_StillPrunesToThree()
        {
            string dir = CreateTempDir();
            try
            {
                SeedLiveData(dir, "one");

                for (int i = 0; i < 5; i++)
                {
                    RumorSnapshotStore.Take(dir, TokenOf(i), "save_" + i, maxSnapshots: 3);
                }

                var inv = RumorSnapshotStore.Inventory(dir);
                Assert.Equal(3, inv.Slots.Count);
            }
            finally
            {
                SafeDelete(dir);
            }
        }

        [Fact]
        public void Take_WhenUnlimited_SameSlotIsStillOverwritten()
        {
            string dir = CreateTempDir();
            try
            {
                SeedLiveData(dir, "one");

                RumorSnapshotStore.Take(dir, TokenOf(1), "same name", maxSnapshots: 0);
                var second = RumorSnapshotStore.Take(dir, TokenOf(2), "same name", maxSnapshots: 0);

                Assert.Contains(second.Pruned, p => p.Reason == PruneReason.SlotOverwritten);

                var inv = RumorSnapshotStore.Inventory(dir);
                Assert.Single(inv.Slots);
                Assert.Equal(TokenOf(2), inv.Slots[0].Token);
                Assert.False(Directory.Exists(Path.Combine(dir, RumorSnapshotStore.SnapshotsFolderName, TokenOf(1))));
            }
            finally
            {
                SafeDelete(dir);
            }
        }

        // ====================================================================
        // 2. 刪除
        // ====================================================================

        [Fact]
        public void Delete_RemovesOnlyTheSelectedSnapshots()
        {
            string dir = CreateTempDir();
            try
            {
                SeedLiveData(dir, "one");
                for (int i = 0; i < 3; i++)
                {
                    RumorSnapshotStore.Take(dir, TokenOf(i), "save_" + i, maxSnapshots: 0);
                }

                var result = RumorSnapshotStore.Delete(dir, new[] { TokenOf(0), TokenOf(2) });

                Assert.Equal(2, result.Deleted.Count);
                Assert.Empty(result.Failed);
                Assert.True(result.BytesFreed > 0);
                Assert.Equal(1, result.RemainingSlots);

                string root = Path.Combine(dir, RumorSnapshotStore.SnapshotsFolderName);
                Assert.False(Directory.Exists(Path.Combine(root, TokenOf(0))));
                Assert.True(Directory.Exists(Path.Combine(root, TokenOf(1))));
                Assert.False(Directory.Exists(Path.Combine(root, TokenOf(2))));

                var manifest = JObject.Parse(File.ReadAllText(Path.Combine(root, RumorSnapshotStore.ManifestFileName)));
                var slots = (JObject)manifest["slots"]!;
                Assert.Single(slots.Properties());
                Assert.NotNull(slots["save_1"]);
            }
            finally
            {
                SafeDelete(dir);
            }
        }

        [Fact]
        public void Delete_LeavesLiveDataUntouched()
        {
            string dir = CreateTempDir();
            try
            {
                SeedLiveData(dir, "live bytes");
                RumorSnapshotStore.Take(dir, TokenOf(1), "save_1", maxSnapshots: 0);

                RumorSnapshotStore.Delete(dir, new[] { TokenOf(1) });

                Assert.Equal("live bytes", File.ReadAllText(Path.Combine(dir, "events", "d100-199.json")));
            }
            finally
            {
                SafeDelete(dir);
            }
        }

        [Fact]
        public void Delete_UnknownToken_ReportsFailureInsteadOfThrowing()
        {
            string dir = CreateTempDir();
            try
            {
                SeedLiveData(dir, "one");
                RumorSnapshotStore.Take(dir, TokenOf(1), "save_1", maxSnapshots: 0);

                var result = RumorSnapshotStore.Delete(dir, new[] { "deadbeef" + new string('f', 24) });

                Assert.Empty(result.Deleted);
                Assert.Single(result.Failed);
                Assert.Equal("no folder and no manifest entry for this token", result.Failed[0].Reason);
                Assert.Equal(1, result.RemainingSlots);
            }
            finally
            {
                SafeDelete(dir);
            }
        }

        [Fact]
        public void Delete_WhenThereIsNoSnapshotFolder_ReportsFailureInsteadOfThrowing()
        {
            string dir = CreateTempDir();
            try
            {
                var result = RumorSnapshotStore.Delete(dir, new[] { TokenOf(1) });

                Assert.Empty(result.Deleted);
                Assert.Single(result.Failed);
                Assert.Contains("no snapshot folder", result.Failed[0].Reason);
            }
            finally
            {
                SafeDelete(dir);
            }
        }

        [Fact]
        public void Delete_WithNoTokens_DoesNothing()
        {
            string dir = CreateTempDir();
            try
            {
                SeedLiveData(dir, "one");
                RumorSnapshotStore.Take(dir, TokenOf(1), "save_1", maxSnapshots: 0);

                var result = RumorSnapshotStore.Delete(dir, Array.Empty<string>());

                Assert.Empty(result.Deleted);
                Assert.Empty(result.Failed);
                Assert.Single(RumorSnapshotStore.Inventory(dir).Slots);
            }
            finally
            {
                SafeDelete(dir);
            }
        }

        // ====================================================================
        // 3. 清單文字（玩家會看到的那幾行，逐字）
        // ====================================================================

        [Fact]
        public void FormatSize_PicksAReadableUnit()
        {
            Assert.Equal("512 B", SnapshotManagerFormatter.FormatSize(512));
            Assert.Equal("2 KB", SnapshotManagerFormatter.FormatSize(2048));
            Assert.Equal("1.9 MB", SnapshotManagerFormatter.FormatSize(1_963_407));
        }

        [Fact]
        public void FormatRow_ShowsSaveNameTimeSizeAndWhetherTheSaveIsStillThere()
        {
            var slot = new SnapshotSlot
            {
                SaveName = "1091秋",
                Token = "db218c8bd6794aeb807994a8ae8ac030",
                Utc = "2026-09-17T09:57:29.0201279Z",
                Files = 4,
                Bytes = 1_963_407,
                ExistsOnDisk = true
            };

            string kept = SnapshotManagerFormatter.FormatRow(slot, true, "存檔還在", "存檔已不在");
            string gone = SnapshotManagerFormatter.FormatRow(slot, false, "存檔還在", "存檔已不在");

            string when = SnapshotManagerFormatter.FormatUtc(slot.Utc);   // 本機時區，不寫死
            Assert.Equal($"1091秋  ·  {when}  ·  1.9 MB  ·  存檔還在", kept);
            Assert.EndsWith("存檔已不在", gone);
        }

        [Fact]
        public void FormatHint_ShowsTokenAndFileCount_OrSaysTheFolderIsGone()
        {
            var ok = new SnapshotSlot { Token = "db218c8bd6794aeb807994a8ae8ac030", Files = 4, ExistsOnDisk = true };
            var missing = new SnapshotSlot { Token = "0c6b0b06a9ed43d3ad403c09bbdb00d7", Files = 4, ExistsOnDisk = false };

            // 兩個標籤都是 Module 從字串表取好傳進來的，Core 不寫死任何給玩家看的字
            Assert.Equal("db218c8b - 4 個檔案", SnapshotManagerFormatter.FormatHint(ok, "資料夾不見了", "4 個檔案"));
            Assert.Equal("0c6b0b06 - 資料夾不見了", SnapshotManagerFormatter.FormatHint(missing, "資料夾不見了", "4 個檔案"));
        }

        [Fact]
        public void FormatUtc_FallsBackToTheRawValueWhenItCannotBeParsed()
        {
            Assert.Equal("not a date", SnapshotManagerFormatter.FormatUtc("not a date"));
            Assert.Equal(string.Empty, SnapshotManagerFormatter.FormatUtc(null));
        }

        // ====================================================================
        // 4. 日誌（可觀測性）
        // ====================================================================

        [Fact]
        public void FormatOpened_SaysHowManyAndHowBig()
        {
            var inv = new SnapshotInventory
            {
                FolderExists = true,
                Slots = new List<SnapshotSlot> { new SnapshotSlot(), new SnapshotSlot() },
                OrphanFolders = new List<string> { "stray" },
                TotalBytes = 4_096_000
            };

            Assert.Equal("Snapshot manager: listed 2 snapshot(s), 1 orphan folder(s), total 4000 KB.",
                SnapshotManagerFormatter.FormatOpened(inv));
        }

        [Fact]
        public void FormatDeleted_ListsEveryDeletionAndEveryFailure()
        {
            var result = new SnapshotDeleteResult
            {
                Deleted = new List<DeletedSnapshot>
                {
                    new DeletedSnapshot { Token = "db218c8bd6794aeb807994a8ae8ac030", SaveName = "1091秋", Bytes = 2_048_000 }
                },
                Failed = new List<FailedDelete>
                {
                    new FailedDelete { Token = "0c6b0b06a9ed43d3ad403c09bbdb00d7", SaveName = "1091冬", Reason = "access denied" }
                },
                BytesFreed = 2_048_000,
                RemainingSlots = 3
            };

            var lines = SnapshotManagerFormatter.FormatDeleted(result, selectedCount: 2);

            Assert.Equal(3, lines.Count);
            Assert.Equal("Snapshot manager: deleted 1 of 2 selected - freed 2000 KB, 3 snapshot(s) left.", lines[0]);
            Assert.Equal("  deleted db218c8b (slot \"1091秋\", 2000 KB)", lines[1]);
            Assert.Equal("  could not delete 0c6b0b06 (slot \"1091冬\"): access denied", lines[2]);
        }

        /// <summary>
        /// SNAP3：存檔還在的快照被勾到時，刪之前會先問一句——三條路各印一行，
        /// 事後從 log 就分得出「問了沒有、他按了什麼」。
        /// </summary>
        [Fact]
        public void FormatGuard_PrintsWhyItAsked_AndWhichButtonWasPressed()
        {
            Assert.Equal("Snapshot manager: 2 of 5 selected still have their save - asking for confirmation.",
                SnapshotManagerFormatter.FormatGuardPrompt(2, 5));
            Assert.Equal("Snapshot manager: confirmed - deleting 2 snapshot(s) whose save is still there.",
                SnapshotManagerFormatter.FormatGuardConfirmed(2));
            Assert.Equal("Snapshot manager: confirmation declined, nothing deleted.",
                SnapshotManagerFormatter.FormatGuardDeclined());
        }

        /// <summary>
        /// SNAP4：熱鍵按下去先問「要不要把存檔已經刪掉的那幾份清掉」——
        /// 四條路（問了、清了、改看清單、沒得清）各印一行。
        /// </summary>
        [Fact]
        public void FormatSweep_PrintsTheOffer_AndWhichWayItWent()
        {
            Assert.Equal("Snapshot manager: 3 snapshot(s) have no save left (7812 KB), 2 still have theirs - offering to sweep.",
                SnapshotManagerFormatter.FormatSweepOffer(3, 8_000_000, 2));
            Assert.Equal("Snapshot manager: sweeping 3 snapshot(s) whose save is gone.",
                SnapshotManagerFormatter.FormatSweepAccepted(3));
            Assert.Equal("Snapshot manager: sweep declined, opening the full list.",
                SnapshotManagerFormatter.FormatSweepDeclined());
            Assert.Equal("Snapshot manager: all 5 snapshot(s) still have their save - nothing to sweep, opening the full list.",
                SnapshotManagerFormatter.FormatNothingToSweep(5));
        }

        [Fact]
        public void FormatUnknownHotkey_NamesTheKeyTheBadValueAndTheFallback()
        {
            Assert.Equal("Hotkey: unknown value \"Banana\" for snapshot manager, falling back to F9.",
                HotkeyLogFormatter.FormatUnknown("snapshot manager", "Banana", "F9"));
            Assert.Equal("Hotkey: unknown value \"Ctrl+Banana\" for chronicle, falling back to Ctrl+L.",
                HotkeyLogFormatter.FormatUnknown("chronicle", "Ctrl+Banana", "Ctrl+L"));
        }

        [Fact]
        public void FormatBindings_PrintsBothKeys()
        {
            Assert.Equal("Hotkeys: chronicle=Ctrl+L, snapshot manager=F9.",
                HotkeyLogFormatter.FormatBindings("Ctrl+L", "F9"));
        }

        [Fact]
        public void FormatTake_WhenUnlimited_DoesNotPrintOfZero()
        {
            var result = new SnapshotResult
            {
                Token = "db218c8bd6794aeb807994a8ae8ac030",
                Files = 4,
                Bytes = 1_963_407,
                ElapsedMs = 7.3,
                Kept = 12
            };

            Assert.Contains("12 kept (no limit)", SnapshotLogFormatter.FormatTake(result, "1091秋", 0));
            Assert.Contains("12 of 8 kept", SnapshotLogFormatter.FormatTake(result, "1091秋", 8));
        }
    }
}
