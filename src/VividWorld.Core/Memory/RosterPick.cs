using System;
using System.Collections.Generic;
using VividWorld.Core.Events;

namespace VividWorld.Core.Memory
{
    public static class RosterPick
    {
        /// <summary>
        /// 從 candidates 裡挑一則最值得看名單的事件，回傳 EventId；candidates 空的回 null。
        /// </summary>
        public static string? Choose(IReadOnlyList<WorldEvent>? candidates)
        {
            if (candidates == null || candidates.Count == 0)
            {
                return null;
            }

            WorldEvent? bestLayer1 = null;
            WorldEvent? bestLayer2 = null;
            WorldEvent? bestLayer3 = null;

            for (int i = 0; i < candidates.Count; i++)
            {
                var evt = candidates[i];
                if (evt == null) continue;

                int layer = GetLayer(evt);
                switch (layer)
                {
                    case 1:
                        if (bestLayer1 == null || IsBetter(evt, bestLayer1))
                        {
                            bestLayer1 = evt;
                        }
                        break;
                    case 2:
                        if (bestLayer2 == null || IsBetter(evt, bestLayer2))
                        {
                            bestLayer2 = evt;
                        }
                        break;
                    case 3:
                        if (bestLayer3 == null || IsBetter(evt, bestLayer3))
                        {
                            bestLayer3 = evt;
                        }
                        break;
                }
            }

            var chosen = bestLayer1 ?? bestLayer2 ?? bestLayer3;
            return chosen?.EventId;
        }

        private static int GetLayer(WorldEvent evt)
        {
            if (evt.KnownBy == null || evt.KnownBy.Count == 0)
            {
                return 3;
            }

            bool hasOutdated = false;
            bool hasFresh = false;

            for (int i = 0; i < evt.KnownBy.Count; i++)
            {
                var entry = evt.KnownBy[i];
                if (entry == null) continue;

                if (Outdating.IsOutdated(entry))
                {
                    hasOutdated = true;
                }
                else
                {
                    hasFresh = true;
                }

                if (hasOutdated && hasFresh)
                {
                    break;
                }
            }

            if (hasOutdated && hasFresh)
            {
                return 1;
            }
            if (hasOutdated)
            {
                return 2;
            }
            return 3;
        }

        private static bool IsBetter(WorldEvent candidate, WorldEvent currentBest)
        {
            int candidateCount = candidate.KnownBy?.Count ?? 0;
            int bestCount = currentBest.KnownBy?.Count ?? 0;

            if (candidateCount != bestCount)
            {
                return candidateCount > bestCount;
            }

            double dayDiff = candidate.Day - currentBest.Day;
            if (Math.Abs(dayDiff) >= 0.0001)
            {
                return dayDiff > 0;
            }

            return string.Compare(candidate.EventId ?? string.Empty, currentBest.EventId ?? string.Empty, StringComparison.Ordinal) < 0;
        }
    }
}
