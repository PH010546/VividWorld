#nullable enable
using System;
using System.Collections.Generic;
using VividWorld.Core.Config;
using VividWorld.Core.Memory;
using VividWorld.Core.Persistence;

namespace VividWorld.Core.Rumors
{
    /// <summary>
    /// 講述者名冊收集器（卡 MF3c §2）。
    /// 從索引中收集所有在至少一則未休眠、非秘密事件中仍然記得的知情者。
    /// </summary>
    public static class TellerRoster
    {
        public static (IReadOnlyList<string> TellerIds, int SkippedForgotten) Collect(
            RumorIndex? index,
            double today,
            string playerHeroId,
            MemoryConfig? memoryConfig)
        {
            if (index == null || index.Entries == null)
            {
                return (Array.Empty<string>(), 0);
            }

            var memory = memoryConfig ?? new MemoryConfig();
            var tellerSet = new HashSet<string>(StringComparer.Ordinal);
            var tellerList = new List<string>();
            var potentiallyForgotten = new HashSet<string>(StringComparer.Ordinal);

            foreach (var entry in index.Entries)
            {
                if (entry == null || string.IsNullOrEmpty(entry.EventId) || entry.Dormant)
                    continue;

                // 未洩漏的秘密不進傳播環
                if (entry.Secret && !entry.Leaked)
                    continue;

                if (entry.KnownByHeroIds == null)
                    continue;

                foreach (var heroId in entry.KnownByHeroIds)
                {
                    if (string.IsNullOrEmpty(heroId) || string.Equals(heroId, playerHeroId, StringComparison.Ordinal))
                        continue;

                    if (IndexMemory.Remembers(entry, heroId, today, memory))
                    {
                        if (tellerSet.Add(heroId))
                        {
                            tellerList.Add(heroId);
                        }
                    }
                    else
                    {
                        potentiallyForgotten.Add(heroId);
                    }
                }
            }

            int skippedForgotten = 0;
            foreach (var heroId in potentiallyForgotten)
            {
                if (!tellerSet.Contains(heroId))
                {
                    skippedForgotten++;
                }
            }

            return (tellerList, skippedForgotten);
        }
    }
}
