using System;
using System.Linq;
using VividWorld.Core.Persistence;

namespace VividWorld.Core.Events
{
    public static class CaptureLookup
    {
        public static RumorIndexEntry? FindLatestCapture(RumorIndex? index, string prisonerHeroId, double day)
        {
            if (index == null || index.Entries == null || string.IsNullOrEmpty(prisonerHeroId))
            {
                return null;
            }

            RumorIndexEntry? best = null;

            // 日期比今天晚的不看（規格 §2.2.1）——判斷在 EventVisibility，這裡不自己寫一份。
            foreach (var entry in index.VisibleEntries(day))
            {
                if (entry == null) continue;
                if (entry.Dormant) continue;
                if (!string.Equals(entry.Type, "hero_taken_prisoner", StringComparison.Ordinal)) continue;
                if (entry.ParticipantHeroIds == null || !entry.ParticipantHeroIds.Contains(prisonerHeroId, StringComparer.Ordinal)) continue;

                if (best == null)
                {
                    best = entry;
                }
                else if (entry.Day > best.Day)
                {
                    best = entry;
                }
                else if (entry.Day == best.Day && string.Compare(entry.EventId, best.EventId, StringComparison.Ordinal) > 0)
                {
                    best = entry;
                }
            }

            return best;
        }
    }
}
