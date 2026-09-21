#nullable enable
using System;

namespace VividWorld.Core.Persistence
{
    public static class StoreTimeline
    {
        /// <summary>沿用 <see cref="EventVisibility.ToleranceDays"/>，不另外定一個值。</summary>
        public const double ToleranceDays = EventVisibility.ToleranceDays;

        /// <summary>
        /// 索引裡日期晚於 <paramref name="currentDay"/> 的事件數與最大日期。
        /// 大於 0 表示存檔被回捲過，事件庫比遊戲時間新。
        /// </summary>
        public static (int Count, double MaxDay) EventsAfter(RumorIndex? index, double currentDay)
        {
            if (index?.Entries == null || index.Entries.Count == 0)
            {
                return (0, 0.0);
            }

            int count = 0;
            double maxDay = 0.0;

            foreach (var entry in index.Entries)
            {
                if (!EventVisibility.IsVisibleOn(entry, currentDay))
                {
                    count++;
                    if (entry.Day > maxDay)
                    {
                        maxDay = entry.Day;
                    }
                }
            }

            return (count, count > 0 ? maxDay : 0.0);
        }
    }
}
