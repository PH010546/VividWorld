using System;
using System.Collections.Generic;
using VividWorld.Core.Events;

namespace VividWorld.Core.Memory
{
    public static class Outdating
    {
        public static bool IsOutdated(KnownByEntry? entry) => entry?.OutdatedDay != null;

        public static bool ShouldMark(KnownByEntry? entry) => entry != null && entry.OutdatedDay == null;

        public static int MarkOutdated(WorldEvent? capture, IEnumerable<string>? heroIds, double day, IList<string>? markedHeroIds = null)
        {
            if (capture == null || heroIds == null || capture.KnownBy == null)
            {
                return 0;
            }

            int count = 0;
            foreach (var heroId in heroIds)
            {
                if (string.IsNullOrEmpty(heroId)) continue;

                var entry = capture.EntryFor(heroId);
                if (entry != null && ShouldMark(entry))
                {
                    entry.OutdatedDay = day;
                    count++;
                    markedHeroIds?.Add(heroId);
                }
            }

            return count;
        }
    }
}
