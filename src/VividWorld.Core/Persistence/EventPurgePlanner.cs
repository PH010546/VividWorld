#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using VividWorld.Core.Config;
using VividWorld.Core.Memory;

namespace VividWorld.Core.Persistence
{
    public enum EventPurgeVerdict
    {
        KeepFuture,
        KeepActive,
        KeepGrudges,
        KeepTooRecent,
        KeepSituationCooldown,
        KeepUnleakedSecret,
        KeepPlayerNotLogged,
        KeepRemembered,
        DeleteForgotten
    }

    public sealed class PurgedEventInfo
    {
        public string EventId { get; }
        public string Type { get; }
        public double Day { get; }
        public int NpcKnowerCount { get; }

        public string Why => NpcKnowerCount > 0
            ? string.Format(CultureInfo.InvariantCulture, "all {0} NPC knower(s) forgot it", NpcKnowerCount)
            : "no NPC knower";

        public PurgedEventInfo(string eventId, string type, double day, int npcKnowerCount)
        {
            EventId = eventId ?? string.Empty;
            Type = type ?? string.Empty;
            Day = day;
            NpcKnowerCount = npcKnowerCount;
        }
    }

    public sealed class EventPurgePlan
    {
        public List<string> PurgeEventIds { get; } = new();
        public List<PurgedEventInfo> PurgedEvents { get; } = new();
        public List<string> PlayerNotLoggedEventIds { get; } = new();

        public int KeepFutureCount { get; set; }
        public int KeepActiveCount { get; set; }
        public int KeepGrudgesCount { get; set; }
        public int KeepTooRecentCount { get; set; }
        public int KeepSituationCooldownCount { get; set; }
        public int KeepUnleakedSecretCount { get; set; }
        public int KeepPlayerNotLoggedCount { get; set; }
        public int KeepRememberedCount { get; set; }
        public int DeleteForgottenCount { get; set; }

        public int DeletedCount => PurgeEventIds.Count;

        public int KeptCount =>
            KeepActiveCount +
            KeepRememberedCount +
            KeepUnleakedSecretCount +
            KeepGrudgesCount +
            KeepSituationCooldownCount +
            KeepTooRecentCount +
            KeepFutureCount +
            KeepPlayerNotLoggedCount;
    }

    /// <summary>
    /// 事件刪除規劃器（純函式，卡 MF3b §1.4）。
    /// 只看索引判斷每一則事件刪除或保留。
    /// </summary>
    public static class EventPurgePlanner
    {
        public const double MinAgeDays = 1.0;

        public static EventPurgePlan Plan(
            RumorIndex? index,
            double today,
            string playerHeroId,
            MemoryConfig? memory,
            double? situationMinAgeDays,
            Func<string, bool>? playerLogHas)
        {
            var plan = new EventPurgePlan();
            if (index?.Entries == null) return plan;

            foreach (var entry in index.Entries)
            {
                if (entry == null || string.IsNullOrEmpty(entry.EventId)) continue;

                var verdict = Evaluate(entry, today, playerHeroId, memory, situationMinAgeDays, playerLogHas);
                switch (verdict)
                {
                    case EventPurgeVerdict.KeepFuture:
                        plan.KeepFutureCount++;
                        break;
                    case EventPurgeVerdict.KeepActive:
                        plan.KeepActiveCount++;
                        break;
                    case EventPurgeVerdict.KeepGrudges:
                        plan.KeepGrudgesCount++;
                        break;
                    case EventPurgeVerdict.KeepTooRecent:
                        plan.KeepTooRecentCount++;
                        break;
                    case EventPurgeVerdict.KeepSituationCooldown:
                        plan.KeepSituationCooldownCount++;
                        break;
                    case EventPurgeVerdict.KeepUnleakedSecret:
                        plan.KeepUnleakedSecretCount++;
                        break;
                    case EventPurgeVerdict.KeepPlayerNotLogged:
                        plan.KeepPlayerNotLoggedCount++;
                        plan.PlayerNotLoggedEventIds.Add(entry.EventId);
                        break;
                    case EventPurgeVerdict.KeepRemembered:
                        plan.KeepRememberedCount++;
                        break;
                    case EventPurgeVerdict.DeleteForgotten:
                        plan.DeleteForgottenCount++;
                        plan.PurgeEventIds.Add(entry.EventId);
                        int npcCount = 0;
                        if (entry.KnownByHeroIds != null)
                        {
                            foreach (var h in entry.KnownByHeroIds)
                            {
                                if (!string.IsNullOrEmpty(h) && !string.Equals(h, playerHeroId, StringComparison.Ordinal))
                                {
                                    npcCount++;
                                }
                            }
                        }
                        plan.PurgedEvents.Add(new PurgedEventInfo(entry.EventId, entry.Type, entry.Day, npcCount));
                        break;
                }
            }

            return plan;
        }

        public static EventPurgeVerdict Evaluate(
            RumorIndexEntry entry,
            double today,
            string playerHeroId,
            MemoryConfig? memory,
            double? situationMinAgeDays,
            Func<string, bool>? playerLogHas)
        {
            if (entry == null) return EventPurgeVerdict.DeleteForgotten;

            // 1. !EventVisibility.IsVisibleOn(entry, today) => KeepFuture
            if (!EventVisibility.IsVisibleOn(entry, today))
            {
                return EventPurgeVerdict.KeepFuture;
            }

            // 2. !entry.Dormant => KeepActive
            if (!entry.Dormant)
            {
                return EventPurgeVerdict.KeepActive;
            }

            // 3. entry.HasGrudges => KeepGrudges
            if (entry.HasGrudges)
            {
                return EventPurgeVerdict.KeepGrudges;
            }

            // 4. today - entry.Day < MinAgeDays => KeepTooRecent
            if (today - entry.Day < MinAgeDays)
            {
                return EventPurgeVerdict.KeepTooRecent;
            }

            // 5. SituationId 不是空的，且（situationMinAgeDays == null 或 today - entry.Day < situationMinAgeDays） => KeepSituationCooldown
            if (!string.IsNullOrEmpty(entry.SituationId))
            {
                if (!situationMinAgeDays.HasValue || (today - entry.Day < situationMinAgeDays.Value))
                {
                    return EventPurgeVerdict.KeepSituationCooldown;
                }
            }

            // 6. entry.Secret && !entry.Leaked => KeepUnleakedSecret（決策 0038）
            if (entry.Secret && !entry.Leaked)
            {
                return EventPurgeVerdict.KeepUnleakedSecret;
            }

            // 7. KnownByHeroIds 含玩家，且 !playerLogHas(entry.EventId) => KeepPlayerNotLogged
            bool playerKnows = entry.KnownByHeroIds != null &&
                               entry.KnownByHeroIds.Contains(playerHeroId, StringComparer.Ordinal);
            if (playerKnows)
            {
                bool logged = playerLogHas != null && playerLogHas(entry.EventId);
                if (!logged)
                {
                    return EventPurgeVerdict.KeepPlayerNotLogged;
                }
            }

            // 8. 有任何一位非玩家知情者 IndexMemory.Remembers(...) == true => KeepRemembered
            if (entry.KnownByHeroIds != null)
            {
                foreach (var heroId in entry.KnownByHeroIds)
                {
                    if (!string.IsNullOrEmpty(heroId) && !string.Equals(heroId, playerHeroId, StringComparison.Ordinal))
                    {
                        if (IndexMemory.Remembers(entry, heroId, today, memory))
                        {
                            return EventPurgeVerdict.KeepRemembered;
                        }
                    }
                }
            }

            // 9. 其餘 => DeleteForgotten
            return EventPurgeVerdict.DeleteForgotten;
        }
    }
}
