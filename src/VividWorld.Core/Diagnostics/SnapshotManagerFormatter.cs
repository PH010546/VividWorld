#nullable enable

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using VividWorld.Core.Persistence;

namespace VividWorld.Core.Diagnostics
{
    /// <summary>
    /// SNAP1：玩家的快照管理清單要印的文字。**版型放在 Core 是為了逐字測**——
    /// 寫在 Module 的 UI 回呼裡就只有實機看得到，而那正是最貴的驗證方式。
    /// 會被玩家看到的字（「存檔還在」之類）由 Module 從字串表取好傳進來，這裡不寫死任何一句中文或英文。
    /// </summary>
    public static class SnapshotManagerFormatter
    {
        /// <summary>1 KB 以下也要有個數字，不然玩家看到 0 會以為是壞的。</summary>
        public static string FormatSize(long bytes)
        {
            if (bytes >= 1024L * 1024L)
            {
                return (bytes / (1024.0 * 1024.0)).ToString("0.0", CultureInfo.InvariantCulture) + " MB";
            }
            if (bytes >= 1024L)
            {
                return (bytes / 1024L).ToString(CultureInfo.InvariantCulture) + " KB";
            }
            return bytes.ToString(CultureInfo.InvariantCulture) + " B";
        }

        /// <summary>清單上的一列：存檔名字 · 拍照時間 · 大小 · 那個存檔還在不在。</summary>
        public static string FormatRow(SnapshotSlot slot, bool saveFileExists, string saveExistsLabel, string saveGoneLabel)
        {
            if (slot == null) return string.Empty;

            string when = FormatUtc(slot.Utc);
            string size = FormatSize(slot.Bytes);
            string mark = saveFileExists ? saveExistsLabel : saveGoneLabel;

            return string.Join("  ·  ", new[] { slot.SaveName, when, size, mark }
                .Where(p => !string.IsNullOrWhiteSpace(p)));
        }

        /// <summary>
        /// 滑鼠提示：token 前八碼與檔數，讓玩家對得上資料夾名字。
        /// **兩個標籤都由 Module 從字串表取好傳進來**——這裡不寫死任何一句給玩家看的話，
        /// 包括「N 個檔案」那半句（它以前寫死成英文 <c>file(s)</c>，是本檔唯一漏掉在地化的地方）。
        /// </summary>
        public static string FormatHint(SnapshotSlot slot, string missingFolderLabel, string fileCountLabel)
        {
            if (slot == null) return string.Empty;
            string t8 = SnapshotLogFormatter.Token8(slot.Token);
            if (!slot.ExistsOnDisk) return $"{t8} - {missingFolderLabel}";
            return $"{t8} - {fileCountLabel}";
        }

        /// <summary>存檔時間（manifest 記的是 UTC ISO）轉成本機時間；解析不出來就原樣印。</summary>
        public static string FormatUtc(string? utc)
        {
            if (string.IsNullOrWhiteSpace(utc)) return string.Empty;
            if (DateTimeOffset.TryParse(utc, CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var parsed))
            {
                return parsed.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
            }
            return utc!;
        }

        // ---------------------------------------------------------------- log

        public static string FormatOpened(SnapshotInventory inv)
        {
            if (inv == null) return "Snapshot manager: nothing to list.";
            return string.Format(CultureInfo.InvariantCulture,
                "Snapshot manager: listed {0} snapshot(s), {1} orphan folder(s), total {2} KB.",
                inv.Slots.Count, inv.OrphanFolders.Count, inv.TotalBytes / 1024);
        }

        public static string FormatCancelled()
            => "Snapshot manager: cancelled, nothing deleted.";

        public static string FormatNothingSelected()
            => "Snapshot manager: confirmed with nothing selected, nothing deleted.";

        /// <summary>
        /// SNAP4：熱鍵按下去時，有幾份快照的存檔已經被刪掉了 ⇒ 先問要不要一口氣清掉。
        /// 三個數字都印出來，因為玩家看到的那句話就是照這三個數字組的。
        /// </summary>
        public static string FormatSweepOffer(int goneCount, long goneBytes, int keptCount)
            => string.Format(CultureInfo.InvariantCulture,
                "Snapshot manager: {0} snapshot(s) have no save left ({1} KB), {2} still have theirs - offering to sweep.",
                goneCount, goneBytes / 1024, keptCount);

        /// <summary>他按了「清除無用快照」。</summary>
        public static string FormatSweepAccepted(int goneCount)
            => string.Format(CultureInfo.InvariantCulture,
                "Snapshot manager: sweeping {0} snapshot(s) whose save is gone.", goneCount);

        /// <summary>他按了「看完整清單…」⇒ 一份都沒刪，改開多選清單。</summary>
        public static string FormatSweepDeclined()
            => "Snapshot manager: sweep declined, opening the full list.";

        /// <summary>每一份快照的存檔都還在 ⇒ 沒有東西可以清，直接開清單。</summary>
        public static string FormatNothingToSweep(int total)
            => string.Format(CultureInfo.InvariantCulture,
                "Snapshot manager: all {0} snapshot(s) still have their save - nothing to sweep, opening the full list.",
                total);

        /// <summary>
        /// SNAP3：勾到的快照裡有幾份「存檔還在」⇒ 先跳確認視窗。
        /// 刪掉這種快照，那個存檔以後就回捲不了傳聞了（`Restore` 會回 `SnapshotMissing`，現場資料原封不動）。
        /// </summary>
        public static string FormatGuardPrompt(int guardedCount, int selectedCount)
            => string.Format(CultureInfo.InvariantCulture,
                "Snapshot manager: {0} of {1} selected still have their save - asking for confirmation.",
                guardedCount, selectedCount);

        /// <summary>他在確認視窗按了「還是刪掉」。</summary>
        public static string FormatGuardConfirmed(int guardedCount)
            => string.Format(CultureInfo.InvariantCulture,
                "Snapshot manager: confirmed - deleting {0} snapshot(s) whose save is still there.",
                guardedCount);

        /// <summary>他在確認視窗按了「取消」⇒ 一份都不刪（連沒有存檔的那幾份也不刪）。</summary>
        public static string FormatGuardDeclined()
            => "Snapshot manager: confirmation declined, nothing deleted.";

        /// <summary>刪除結果。第一行是總數，之後每一筆刪掉的與失敗的各一行——失敗不能只藏在數字裡。</summary>
        public static IReadOnlyList<string> FormatDeleted(SnapshotDeleteResult result, int selectedCount)
        {
            var lines = new List<string>();
            if (result == null) return lines;

            lines.Add(string.Format(CultureInfo.InvariantCulture,
                "Snapshot manager: deleted {0} of {1} selected - freed {2} KB, {3} snapshot(s) left.",
                result.Deleted.Count, selectedCount, result.BytesFreed / 1024, result.RemainingSlots));

            foreach (var d in result.Deleted)
            {
                lines.Add(string.Format(CultureInfo.InvariantCulture,
                    "  deleted {0} (slot \"{1}\", {2} KB)",
                    SnapshotLogFormatter.Token8(d.Token), d.SaveName, d.Bytes / 1024));
            }

            foreach (var f in result.Failed)
            {
                lines.Add(string.Format(CultureInfo.InvariantCulture,
                    "  could not delete {0} (slot \"{1}\"): {2}",
                    SnapshotLogFormatter.Token8(f.Token), f.SaveName, f.Reason));
            }

            return lines;
        }
    }
}
