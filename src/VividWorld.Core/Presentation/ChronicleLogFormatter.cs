#nullable enable
using System.Collections.Generic;
using System.Globalization;

namespace VividWorld.Core.Presentation
{
    public static class ChronicleLogFormatter
    {
        public static string FormatOpen(string heroId, double day, ChronicleStats stats, int maxEntries)
        {
            if (stats == null || stats.TotalKnown == 0)
            {
                return string.Format(CultureInfo.InvariantCulture,
                    "Chronicle opened for {0} on day {1:0.0}: nothing known yet (0 known)",
                    heroId, day);
            }

            return string.Format(CultureInfo.InvariantCulture,
                "Chronicle opened for {0} on day {1:0.0}: {2} shown of {3} heard (max {4}), hidden future {5}",
                heroId, day, stats.Returned, stats.TotalKnown, maxEntries, stats.HiddenFuture);
        }

        /// <summary>
        /// 紀事視窗開啟時的一行統計：
        /// 幾筆紀錄、幾個區塊、被上限擋掉幾個、標了有矛盾的幾個、標題退回不帶名字的幾個。
        /// </summary>
        public static string FormatOpenBlocks(string heroId, double day, ChronicleStats stats, int maxEntries)
        {
            if (stats == null || stats.TotalKnown == 0)
            {
                return string.Format(CultureInfo.InvariantCulture,
                    "Chronicle opened for {0} on day {1:0.0}: nothing known yet (0 known)",
                    heroId, day);
            }

            return string.Format(CultureInfo.InvariantCulture,
                "Chronicle opened for {0} on day {1:0.0}: {2} record(s), {3} block(s) shown of {4} (max {5}, capped out {6}), {7} conflict(s), {8} fallback title(s), hidden future {9}",
                heroId, day, stats.TotalKnown, stats.Returned, stats.TotalBlocks, maxEntries, stats.CappedOutBlocks, stats.ConflictBlocks, stats.FallbackTitleBlocks, stats.HiddenFuture);
        }

        /// <summary>
        /// 每個區塊印一行：
        /// 源頭、標題用哪一句（或為什麼退回）、幾件事、各幾則、有矛盾與否與原因（哪兩則）。
        /// </summary>
        public static string FormatBlock(
            string rootEventId,
            string rootType,
            string headlineText,
            string headlineNote,
            int mattersCount,
            string mattersDetail,
            bool hasConflict,
            string? conflictReason)
        {
            string conflictStr = hasConflict ? ("yes (" + (conflictReason ?? "unspecified") + ")") : "no";
            return string.Format(CultureInfo.InvariantCulture,
                "Chronicle block {0} ({1}): title '{2}' ({3}), {4} matter(s) [{5}], conflict: {6}",
                rootEventId, rootType, headlineText, headlineNote, mattersCount, mattersDetail, conflictStr);
        }

        public static string FormatBlock(
            string rootEventId,
            string rootType,
            string headlineText,
            string headlineNote,
            IReadOnlyList<int> matterTellingsCounts,
            bool hasConflict,
            string? conflictReason)
        {
            int mCount = matterTellingsCounts?.Count ?? 0;
            string detail = matterTellingsCounts != null
                ? string.Join(", ", matterTellingsCounts)
                : string.Empty;
            return FormatBlock(rootEventId, rootType, headlineText, headlineNote, mCount, detail, hasConflict, conflictReason);
        }

        /// <summary>舊紀錄補源頭的統計。</summary>
        public static string FormatRootBackfill(int total, int fromStore, int fromHeardLog, int asSelf)
        {
            return string.Format(CultureInfo.InvariantCulture,
                "Chronicle root backfill: {0} entries backfilled ({1} from event store, {2} from heard log, {3} as self)",
                total, fromStore, fromHeardLog, asSelf);
        }

        /// <summary>每一筆顯示出來的紀事印一行：有幾份來源，每份是誰、手數、碎片數、有沒有感想、是照原句重組還是照舊組事實。</summary>
        /// <param name="headlineNote">標題用的是帶名字的版本還是退回的版本（與原因）；有給就接在行尾。</param>
        public static string FormatEntrySources(string eventId, IReadOnlyList<ChronicleSource>? sources, string? headlineNote)
        {
            string line = FormatEntrySources(eventId, sources);
            return string.IsNullOrEmpty(headlineNote) ? line : line + "; title: " + headlineNote;
        }

        public static string FormatEntrySources(string eventId, IReadOnlyList<ChronicleSource>? sources)
        {
            int count = sources?.Count ?? 0;
            var sb = new System.Text.StringBuilder();
            sb.Append(string.Format(CultureInfo.InvariantCulture,
                "Chronicle entry {0}: {1} source(s)", eventId, count));
            if (sources != null)
            {
                for (int i = 0; i < sources.Count; i++)
                {
                    var s = sources[i];
                    sb.Append(i == 0 ? " - " : "; ");
                    string modeStr = s.HasSpokenLine
                        ? "reconstructed quote"
                        : "facts (reason: no spoken line saved)";
                    sb.Append(string.Format(CultureInfo.InvariantCulture,
                        "{0} (hop {1}, {2} fact(s), {3}, {4})",
                        string.IsNullOrEmpty(s.HeroId) ? "unknown" : s.HeroId,
                        s.Hop, s.FactCount, s.Feeling != null ? "with a feeling" : "no feeling",
                        modeStr));
                }
            }
            return sb.ToString();
        }

        public static string ComputeVerdict(
            int entryCount,
            IReadOnlyList<ChronicleRowLayout>? rows,
            bool listWidgetFound = true)
        {
            if (!listWidgetFound || rows == null)
            {
                return "list widget not found";
            }

            if (rows.Count != entryCount)
            {
                return "row count mismatch";
            }

            var zeroHeightIndices = new System.Collections.Generic.List<int>();
            for (int i = 0; i < rows.Count; i++)
            {
                if (rows[i].Height <= 0.5)
                {
                    zeroHeightIndices.Add(i);
                }
            }
            if (zeroHeightIndices.Count > 0)
            {
                return "zero-height row(s): " + string.Join(", ", zeroHeightIndices);
            }

            var overlappingPairs = new System.Collections.Generic.List<string>();
            for (int i = 0; i < rows.Count - 1; i++)
            {
                if (rows[i + 1].Y < rows[i].Y + rows[i].Height - 0.5)
                {
                    overlappingPairs.Add(string.Format(CultureInfo.InvariantCulture, "{0}&{1}", i, i + 1));
                }
            }
            if (overlappingPairs.Count > 0)
            {
                return "overlapping rows: " + string.Join(", ", overlappingPairs);
            }

            return "ok";
        }

        public static string FormatLayout(
            int frame,
            int entryCount,
            double viewportHeight,
            IReadOnlyList<ChronicleRowLayout>? rows,
            bool listWidgetFound,
            out string verdict)
        {
            verdict = ComputeVerdict(entryCount, rows, listWidgetFound);
            int rowCount = (listWidgetFound && rows != null) ? rows.Count : 0;

            var sb = new System.Text.StringBuilder();
            sb.Append(string.Format(CultureInfo.InvariantCulture,
                "Chronicle layout (frame {0}): {1} row widget(s) for {2} entry(ies), viewport height {3:0}; {4}",
                frame, rowCount, entryCount, viewportHeight, verdict));

            if (rowCount > 0 && rows != null)
            {
                int limit = System.Math.Min(rowCount, 10);
                for (int i = 0; i < limit; i++)
                {
                    sb.Append('\n');
                    var row = rows[i];
                    string visStr = row.Visible ? "true" : "false";
                    sb.Append(string.Format(CultureInfo.InvariantCulture,
                        "  row {0}: y {1:0}, height {2:0}, visible {3}",
                        i, row.Y, row.Height, visStr));
                }

                if (rowCount > 10)
                {
                    sb.Append('\n');
                    sb.Append(string.Format(CultureInfo.InvariantCulture,
                        "  ... and {0} more",
                        rowCount - 10));
                }
            }

            return sb.ToString();
        }

        public static string FormatLayout(
            int frame,
            int entryCount,
            double viewportHeight,
            IReadOnlyList<ChronicleRowLayout>? rows,
            bool listWidgetFound = true)
        {
            return FormatLayout(frame, entryCount, viewportHeight, rows, listWidgetFound, out _);
        }

        public static string FormatLinkQueued(string link)
        {
            return string.Format(CultureInfo.InvariantCulture,
                "Chronicle link: {0} clicked - handled on the next application tick (not inside the widget's own update).",
                link);
        }

        public static string FormatOpeningLink(string link)
        {
            return string.Format(CultureInfo.InvariantCulture,
                "Chronicle link: {0} - closing the chronicle and opening the encyclopedia; will reopen when it closes.",
                link);
        }

        public static string FormatEncyclopediaOpened(int frameCount)
        {
            return string.Format(CultureInfo.InvariantCulture,
                "Chronicle link: encyclopedia opened after {0} frame(s).",
                frameCount);
        }

        public static string FormatEncyclopediaClosedReopening()
        {
            return "Chronicle link: encyclopedia closed - reopening the chronicle.";
        }

        public static string FormatNotReopening(string reason)
        {
            return string.Format(CultureInfo.InvariantCulture,
                "Chronicle link: not reopening - {0}.",
                reason);
        }

        public static string FormatIgnoredLink(string link)
        {
            return string.Format(CultureInfo.InvariantCulture,
                "Chronicle link: ignored {0} - encyclopedia links are disabled (presentation.encyclopediaLinksEnabled = false).",
                link);
        }
    }

    public readonly struct ChronicleRowLayout
    {
        public double Y { get; }
        public double Height { get; }
        public bool Visible { get; }

        public ChronicleRowLayout(double y, double height, bool visible)
        {
            Y = y;
            Height = height;
            Visible = visible;
        }
    }
}
