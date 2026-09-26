#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace VividWorld.Core.Persistence
{
    /// <summary>
    /// 事件刪除日誌版型（卡 MF3b §1.6）。
    /// </summary>
    public static class EventPurgeLogFormatter
    {
        public static string FormatSummary(
            string saveName,
            double day,
            EventPurgePlan plan,
            double? situationMinAgeDays,
            int shardsRewritten,
            long elapsedMs)
        {
            if (plan == null) throw new ArgumentNullException(nameof(plan));

            string cd = situationMinAgeDays.HasValue
                ? situationMinAgeDays.Value.ToString("0.0", CultureInfo.InvariantCulture)
                : "catalog not loaded";

            return string.Format(CultureInfo.InvariantCulture,
                "Purge at save '{0}' (day {1:0.0}): deleted {2} event(s) (no NPC remembers); kept {3}: active {4}, remembered {5}, unleaked secret {6}, grudges {7}, situation cooldown {8} (min age {9}), too recent {10}, future {11}, player heard but not logged {12}; {13} shard(s) rewritten in {14:0} ms",
                saveName,
                day,
                plan.DeletedCount,
                plan.KeptCount,
                plan.KeepActiveCount,
                plan.KeepRememberedCount,
                plan.KeepUnleakedSecretCount,
                plan.KeepGrudgesCount,
                plan.KeepSituationCooldownCount,
                cd,
                plan.KeepTooRecentCount,
                plan.KeepFutureCount,
                plan.KeepPlayerNotLoggedCount,
                shardsRewritten,
                elapsedMs);
        }

        public static string FormatDetail(string eventId, string type, double day, string why)
        {
            return string.Format(CultureInfo.InvariantCulture,
                "  purged {0} ({1}, day {2:0.0}): {3}",
                eventId, type, day, why);
        }

        public static string FormatSkippedDisabled(string saveName)
        {
            return string.Format(CultureInfo.InvariantCulture,
                "Purge skipped at save '{0}': persistence.purgeForgottenEvents is false",
                saveName);
        }

        public static string FormatSkippedHeardLogDirty(string saveName)
        {
            return string.Format(CultureInfo.InvariantCulture,
                "Purge skipped at save '{0}': the player heard-log could not be written, so nothing is deleted this time",
                saveName);
        }

        public static string FormatPlayerNotLoggedWarn(int count, IEnumerable<string>? eventIds)
        {
            string idsStr = string.Join(", ", (eventIds ?? Array.Empty<string>()).Take(10));
            return string.Format(CultureInfo.InvariantCulture,
                "Purge kept {0} event(s) the player knows but the heard-log does not have: {1}",
                count, idsStr);
        }

        public static string FormatWorldStatusPreview(EventPurgePlan plan, double? situationMinAgeDays)
        {
            if (plan == null) throw new ArgumentNullException(nameof(plan));

            string cd = situationMinAgeDays.HasValue
                ? situationMinAgeDays.Value.ToString("0.0", CultureInfo.InvariantCulture)
                : "catalog not loaded";

            return string.Format(CultureInfo.InvariantCulture,
                "- Purge preview (if saved now): would delete {0} (no NPC remembers); would keep {1}: active {2}, remembered {3}, unleaked secret {4}, grudges {5}, situation cooldown {6} (min age {7}), too recent {8}, future {9}, player heard but not logged {10}",
                plan.DeletedCount,
                plan.KeptCount,
                plan.KeepActiveCount,
                plan.KeepRememberedCount,
                plan.KeepUnleakedSecretCount,
                plan.KeepGrudgesCount,
                plan.KeepSituationCooldownCount,
                cd,
                plan.KeepTooRecentCount,
                plan.KeepFutureCount,
                plan.KeepPlayerNotLoggedCount);
        }
    }
}
