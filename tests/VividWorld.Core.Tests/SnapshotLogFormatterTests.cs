#nullable enable

using System;
using System.Collections.Generic;
using VividWorld.Core.Diagnostics;
using VividWorld.Core.Persistence;
using Xunit;

namespace VividWorld.Core.Tests
{
    public class SnapshotLogFormatterTests
    {
        [Fact]
        public void FormatTake_MatchesDocumentedFormat()
        {
            var result = new SnapshotResult
            {
                Outcome = SnapshotOutcome.Taken,
                Token = "4f2a1c9e8b7c4d5e",
                Files = 3,
                LinkedFiles = 2,
                CopiedFiles = 1,
                Bytes = 1929 * 1024,
                ElapsedMs = 38.7,
                Kept = 2
            };

            string formatted = SnapshotLogFormatter.FormatTake(result, "ad150736 - day 91118", 8);
            Assert.Equal("Snapshot 4f2a1c9e: 3 file(s) (2 linked, 1 copied) / 1929 KB copied in 38.7 ms - slot \"ad150736 - day 91118\", 2 of 8 kept.", formatted);
        }

        [Fact]
        public void FormatTake_HardlinkSnapshots_MatchesDocumentedSection4Format()
        {
            var result = new SnapshotResult
            {
                Outcome = SnapshotOutcome.Taken,
                Token = "4f2a1c9e8b7c4d5e",
                Files = 4,
                LinkedFiles = 3,
                CopiedFiles = 1,
                Bytes = 1929 * 1024,
                ElapsedMs = 7.3,
                Kept = 3
            };

            string formatted = SnapshotLogFormatter.FormatTake(result, "1091秋", 0);
            Assert.Equal("Snapshot 4f2a1c9e: 4 file(s) (3 linked, 1 copied) / 1929 KB copied in 7.3 ms - slot \"1091秋\", 3 kept (no limit).", formatted);
        }

        [Fact]
        public void FormatHardlinksUnavailable_MatchesDocumentedFormat()
        {
            string formatted = SnapshotLogFormatter.FormatHardlinksUnavailable(
                "4f2a1c9e8b7c4d5e",
                17,
                "the system cannot move the file to a different disk drive",
                4);
            Assert.Equal("Snapshot 4f2a1c9e: hard links unavailable (error 17: the system cannot move the file to a different disk drive) - copied 4 file(s) instead.", formatted);
        }

        [Fact]
        public void FormatHardlinksDisabled_MatchesDocumentedFormat()
        {
            string formatted = SnapshotLogFormatter.FormatHardlinksDisabled(
                "4f2a1c9e8b7c4d5e",
                4);
            Assert.Equal("Snapshot 4f2a1c9e: hard links disabled by config - copied 4 file(s).", formatted);
        }

        [Fact]
        public void FormatSkippedTake_AllThreeReasons_MatchDocumentedText()
        {
            Assert.Equal("Snapshot skipped: the save did not complete successfully.",
                SnapshotLogFormatter.FormatSkippedTake(SnapshotOutcome.SaveFailed));

            Assert.Equal("Snapshot skipped: no token was minted for this save.",
                SnapshotLogFormatter.FormatSkippedTake(SnapshotOutcome.NoToken));

            Assert.Equal("Snapshot skipped: the campaign folder does not exist yet.",
                SnapshotLogFormatter.FormatSkippedTake(SnapshotOutcome.NoCampaignFolder));
        }

        [Fact]
        public void FormatPrune_AllThreeReasons_MatchDocumentedText()
        {
            var p1 = new PrunedSnapshot
            {
                Token = "4f2a1c9e8b7c4d5e",
                SaveName = "auto_save_1",
                Reason = PruneReason.SlotOverwritten
            };
            Assert.Equal("Snapshot prune: dropped 4f2a1c9e (slot \"auto_save_1\") - slot was overwritten by a newer save.",
                SnapshotLogFormatter.FormatPrune(p1, 8));

            var p2 = new PrunedSnapshot
            {
                Token = "9b71d00411223344",
                SaveName = "old_slot",
                Reason = PruneReason.OverCap
            };
            Assert.Equal("Snapshot prune: dropped 9b71d004 (slot \"old_slot\") - over the cap of 8.",
                SnapshotLogFormatter.FormatPrune(p2, 8));

            var p3 = new PrunedSnapshot
            {
                Token = "1122334455667788",
                SaveName = "",
                Reason = PruneReason.OrphanFolder
            };
            Assert.Equal("Snapshot prune: dropped 11223344 (slot \"\") - orphan folder with no manifest entry.",
                SnapshotLogFormatter.FormatPrune(p3, 8));
        }

        [Fact]
        public void FormatRestore_MatchesDocumentedFormat()
        {
            var result = new SnapshotResult
            {
                Outcome = SnapshotOutcome.Restored,
                Token = "4f2a1c9e8b7c4d5e",
                Files = 3,
                Bytes = 1929 * 1024,
                ElapsedMs = 41.2
            };

            string formatted = SnapshotLogFormatter.FormatRestore(result);
            Assert.Equal("Snapshot restore 4f2a1c9e: 3 file(s) / 1929 KB restored in 41.2 ms - the event store is back to the state this save was written in.", formatted);
        }

        [Fact]
        public void FormatSkippedRestore_AllReasons_MatchDocumentedText()
        {
            Assert.Equal("Snapshot restore skipped: this save carries no snapshot token (it was saved before M7).",
                SnapshotLogFormatter.FormatSkippedRestore(SnapshotOutcome.NoToken, ""));

            Assert.Equal("Snapshot restore skipped: no snapshot folder for token 4f2a1c9e - it was pruned, or the folder was deleted by hand.",
                SnapshotLogFormatter.FormatSkippedRestore(SnapshotOutcome.SnapshotMissing, "4f2a1c9e8b7c4d5e"));

            Assert.Equal("Snapshot restore skipped: the snapshot for token 4f2a1c9e is empty - live data left untouched.",
                SnapshotLogFormatter.FormatSkippedRestore(SnapshotOutcome.SnapshotEmpty, "4f2a1c9e8b7c4d5e"));
        }

        [Fact]
        public void FormatFailed_OperationsAndToken_MatchDocumentedFormat()
        {
            string copyFailed = SnapshotLogFormatter.FormatFailed("copy", "4f2a1c9e8b7c4d5e", "Access denied");
            Assert.Equal("Snapshot copy failed for token 4f2a1c9e: Access denied", copyFailed);

            string restoreFailed = SnapshotLogFormatter.FormatFailed("restore", "9b71d00411223344", "Disk full");
            Assert.Equal("Snapshot restore failed for token 9b71d004: Disk full", restoreFailed);
        }

        [Fact]
        public void FormatInventory_TwoSlotsWithAsterisk_MatchesDocumentedLayout()
        {
            var inv = new SnapshotInventory
            {
                FolderExists = true,
                LiveFiles = 3,
                LiveBytes = 1929 * 1024,
                TotalBytes = 3858 * 1024,
                Slots = new List<SnapshotSlot>
                {
                    new SnapshotSlot
                    {
                        Token = "4f2a1c9e8b7c4d5e",
                        SaveName = "ad150736 - day 91118",
                        Utc = "2026-09-17T09:14:02Z",
                        Files = 3,
                        Bytes = 1929 * 1024,
                        ExistsOnDisk = true
                    },
                    new SnapshotSlot
                    {
                        Token = "9b71d00411223344",
                        SaveName = "auto_save_1",
                        Utc = "2026-09-17T09:02:55Z",
                        Files = 3,
                        Bytes = 1929 * 1024,
                        ExistsOnDisk = true
                    }
                }
            };

            var lastRestore = new SnapshotResult
            {
                Outcome = SnapshotOutcome.Restored,
                Token = "4f2a1c9e8b7c4d5e",
                Files = 3,
                Bytes = 1929 * 1024,
                ElapsedMs = 41.2
            };

            var lastTake = new SnapshotResult
            {
                Outcome = SnapshotOutcome.Taken,
                Files = 3,
                ElapsedMs = 38.7
            };

            string formatted = SnapshotLogFormatter.FormatInventory(
                inv,
                "campaign_ad150736",
                "4f2a1c9e8b7c4d5e",
                lastTake,
                lastRestore,
                8);

            string expected =
                "Snapshots:\r\n" +
                "---------------------------------------------\r\n" +
                "  campaign folder: campaign_ad150736  (3 file(s) / 1929 KB live)\r\n" +
                "  this session:    restored from 4f2a1c9e - 3 file(s) / 1929 KB in 41.2 ms\r\n" +
                "  cap:             8 slot(s), 2 in use, 3858 KB on disk\r\n" +
                "  note:            sizes are logical; linked files share their bytes with the live folder\r\n" +
                "  ---\r\n" +
                "* 4f2a1c9e  \"ad150736 - day 91118\"  2026-09-17T09:14:02Z  3 file(s)  1929 KB\r\n" +
                "  9b71d004  \"auto_save_1\"           2026-09-17T09:02:55Z  3 file(s)  1929 KB\r\n" +
                "  ---\r\n" +
                "  last save: 3 file(s) copied in 38.7 ms";

            // Standardize line breaks before comparison
            Assert.Equal(expected.Replace("\r\n", "\n"), formatted.Replace("\r\n", "\n"));
        }

        [Fact]
        public void FormatInventory_IncludesLogicalSizesNote()
        {
            var inv = new SnapshotInventory
            {
                FolderExists = true,
                LiveFiles = 1,
                LiveBytes = 1024,
                TotalBytes = 1024,
                Slots = Array.Empty<SnapshotSlot>()
            };
            string formatted = SnapshotLogFormatter.FormatInventory(
                inv,
                "campaign_test",
                sessionToken: null,
                lastTake: null,
                lastRestore: null,
                maxSnapshots: 0);
            Assert.Contains("  note:            sizes are logical; linked files share their bytes with the live folder", formatted);
        }

        [Fact]
        public void SnapshotOutcome_HasNoDisabledMember_TheSwitchIsGone()
        {
            // v3.28 拿掉 `revertWithSaves`（M1 從 ImmersiveAI 照抄、理由不轉移）。
            // 這條釘住的是「不會有人順手把開關加回來」：一旦 Disabled 復活，
            // 就又有一條路徑會讓事件庫比存檔新，而那正是 M7 要消滅的狀態。
            Assert.DoesNotContain("Disabled", Enum.GetNames(typeof(SnapshotOutcome)));
        }

        [Fact]
        public void FormatInventory_NoSnapshotsYet_MatchesDocumentedLayout()
        {
            var inv = new SnapshotInventory
            {
                FolderExists = true,
                LiveFiles = 2,
                LiveBytes = 200 * 1024,
                TotalBytes = 0,
                Slots = Array.Empty<SnapshotSlot>()
            };

            string formatted = SnapshotLogFormatter.FormatInventory(
                inv,
                "campaign_1",
                sessionToken: null,
                lastTake: null,
                lastRestore: null,
                maxSnapshots: 8);

            Assert.Contains("  this session:    new campaign - nothing to revert", formatted);
            Assert.Contains("  (no snapshots yet - save once)", formatted);
        }

        /// <summary>
        /// 讀一個 M7 之前存的檔（`NoToken`）**不是**新戰役。兩者印同一句話會讓 dev 工具
        /// 對著一個載入的舊存檔謊報「new campaign」，而那正是 M7 上線後最常見的一種情況。
        /// </summary>
        [Fact]
        public void FormatInventory_PreM7SaveWithNoToken_IsNotReportedAsNewCampaign()
        {
            var inv = new SnapshotInventory
            {
                FolderExists = true,
                LiveFiles = 3,
                LiveBytes = 1929 * 1024,
                TotalBytes = 0,
                Slots = Array.Empty<SnapshotSlot>()
            };

            var lastRestore = new SnapshotResult
            {
                Outcome = SnapshotOutcome.NoToken,
                Token = string.Empty,
                Reason = SnapshotLogFormatter.GetRestoreSkippedReason(SnapshotOutcome.NoToken)
            };

            string formatted = SnapshotLogFormatter.FormatInventory(
                inv,
                "campaign_ad150736",
                sessionToken: null,
                lastTake: null,
                lastRestore: lastRestore,
                maxSnapshots: 8);

            Assert.DoesNotContain("new campaign", formatted);
            Assert.Contains(
                "  this session:    no revert (this save carries no snapshot token (it was saved before M7))",
                formatted);
        }

        [Fact]
        public void FormatInventory_MissingOnDiskAndOrphanFolders_MatchesDocumentedLayout()
        {
            var inv = new SnapshotInventory
            {
                FolderExists = true,
                LiveFiles = 2,
                LiveBytes = 200 * 1024,
                TotalBytes = 500 * 1024,
                OrphanFolders = new List<string> { "stray_token_1", "stray_token_2" },
                Slots = new List<SnapshotSlot>
                {
                    new SnapshotSlot
                    {
                        Token = "aaaaaaaa11112222",
                        SaveName = "missing_slot",
                        Utc = "2026-09-17T08:00:00Z",
                        Files = 2,
                        Bytes = 200 * 1024,
                        ExistsOnDisk = false
                    }
                }
            };

            string formatted = SnapshotLogFormatter.FormatInventory(
                inv,
                "campaign_1",
                sessionToken: null,
                lastTake: null,
                lastRestore: null,
                maxSnapshots: 8);

            Assert.Contains("  orphan folder(s) with no manifest entry: 2", formatted);
            Assert.Contains("<missing on disk>", formatted);
        }
    }
}
