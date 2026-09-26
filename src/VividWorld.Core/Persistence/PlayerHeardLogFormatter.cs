#nullable enable
using System.Globalization;

namespace VividWorld.Core.Persistence
{
    /// <summary>
    /// 玩家聽過的消息日誌版型（卡 MF3a §1.5）。
    /// </summary>
    public static class PlayerHeardLogFormatter
    {
        public static string FormatLoad(int n, string how, PlayerHeardBackfillResult backfill)
        {
            int added = backfill?.Added ?? 0;
            int considered = backfill?.Considered ?? 0;
            int secret = backfill?.SkippedSecret ?? 0;
            int noEntry = backfill?.NoPlayerEntry ?? 0;
            int notLoadable = backfill?.NotLoadable ?? 0;

            return FormatLoad(n, how, added, considered, secret, noEntry, notLoadable);
        }

        public static string FormatLoad(int n, string how, int added, int considered, int secret, int noEntry, int notLoadable)
        {
            return string.Format(CultureInfo.InvariantCulture,
                "Player heard-log: {0} on file ({1}); backfilled {2} of {3} player-known event(s) from the store (skipped {4} unleaked secret, {5} without a player entry, {6} not loadable)",
                n, how, added, considered, secret, noEntry, notLoadable);
        }

        public static string FormatTold(bool isUpdate, string eventId, int hop, int factCount, string source)
        {
            return FormatTold(isUpdate ? "updated" : "added", eventId, hop, factCount, source);
        }

        public static string FormatTold(string action, string eventId, int hop, int factCount, string source)
        {
            return string.Format(CultureInfo.InvariantCulture,
                "Player heard-log: {0} {1} (hop {2}, {3} fact(s), from {4})",
                action, eventId, hop, factCount, source);
        }

        public static string FormatWriteFailed()
        {
            return "Player heard-log: write failed, will retry on the next flush";
        }
    }
}
