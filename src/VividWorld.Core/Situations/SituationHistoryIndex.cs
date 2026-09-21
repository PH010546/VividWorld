using System;
using System.Collections.Generic;
using VividWorld.Core.Persistence;

namespace VividWorld.Core.Situations
{
    public sealed class SituationHistoryIndex : ISituationHistory
    {
        private readonly Dictionary<string, List<RumorIndexEntry>> _bySituation;

        /// <summary>情境冷卻查的歷史。
        /// **日期比 <paramref name="today"/> 晚的不算數**（規格 §2.2.1）——
        /// 否則一條被抹掉的時間線上發生過的情境，會擋住今天該發生的那一個。</summary>
        public SituationHistoryIndex(IEnumerable<RumorIndexEntry> entries, double today)
        {
            _bySituation = new Dictionary<string, List<RumorIndexEntry>>(StringComparer.Ordinal);
            if (entries == null) return;

            foreach (var entry in entries)
            {
                if (entry != null && !string.IsNullOrEmpty(entry.SituationId)
                    && EventVisibility.IsVisibleOn(entry, today))
                {
                    if (!_bySituation.TryGetValue(entry.SituationId!, out var list))
                    {
                        list = new List<RumorIndexEntry>();
                        _bySituation[entry.SituationId!] = list;
                    }
                    list.Add(entry);
                }
            }
        }

        public SituationOccurrence? LastOccurrence(string situationId, IReadOnlyCollection<string> heroIds)
        {
            if (string.IsNullOrEmpty(situationId) || heroIds == null || heroIds.Count == 0) return null;
            if (!_bySituation.TryGetValue(situationId, out var entries) || entries.Count == 0) return null;

            RumorIndexEntry? latest = null;

            foreach (var entry in entries)
            {
                if (entry.ParticipantHeroIds == null || entry.ParticipantHeroIds.Count == 0) continue;

                bool allCovered = true;
                foreach (var heroId in heroIds)
                {
                    bool found = false;
                    for (int i = 0; i < entry.ParticipantHeroIds.Count; i++)
                    {
                        if (string.Equals(entry.ParticipantHeroIds[i], heroId, StringComparison.Ordinal))
                        {
                            found = true;
                            break;
                        }
                    }

                    if (!found)
                    {
                        allCovered = false;
                        break;
                    }
                }

                if (allCovered)
                {
                    if (latest == null || entry.Day > latest.Day)
                    {
                        latest = entry;
                    }
                }
            }

            if (latest == null) return null;

            return new SituationOccurrence
            {
                Day = latest.Day,
                EventId = latest.EventId
            };
        }
    }
}
