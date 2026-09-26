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
