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

            int skipped = stats.SkippedNoShard + stats.SkippedNoEntry + stats.SkippedSecret;
            return string.Format(CultureInfo.InvariantCulture,
                "Chronicle opened for {0} on day {1:0.0}: {2} shown of {3} known (max {4}), hidden future {5}, skipped {6} (no shard {7}, no player entry {8}, unleaked secret {9})",
                heroId, day, stats.Returned, stats.TotalKnown, maxEntries, stats.HiddenFuture, skipped, stats.SkippedNoShard, stats.SkippedNoEntry, stats.SkippedSecret);
        }
    }
}
