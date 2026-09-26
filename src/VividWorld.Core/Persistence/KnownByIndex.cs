#nullable enable
using System;
using System.Collections.Generic;

namespace VividWorld.Core.Persistence
{
    /// <summary>
    /// 記憶體反向索引（不持久化）。
    /// 提供對話層 O(1) 查詢指定英雄知道哪些事件、以及關於指定英雄的事件。
    ///
    /// **日期比今天晚的事件查不到**（規格 §2.2.1）：那是一條被抹掉的時間線。
    /// 索引裡還留著它，日子走到那一天，同一個查詢自己就會把它交出來——
    /// 不需要重建索引，也沒有任何旗標被寫進磁碟。
    /// </summary>
    public sealed class KnownByIndex
    {
        private readonly Dictionary<string, List<string>> _eventsKnownBy = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, List<string>> _eventsAbout = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>事件 → 日期。只有這一份，兩個方向的清單共用。</summary>
        private readonly Dictionary<string, double> _eventDay = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>索引裡最晚的事件日期。它沒有超過「今天＋容差」時，
        /// 一則都不必濾 ⇒ 直接把原本那份清單交出去，不配置新的 List。
        /// 傳播每小時要跑過整個講述者輪，這條快路是為了不讓可見性判斷變成尖峰成本。</summary>
        private double _maxDay = double.NegativeInfinity;

        public void Rebuild(RumorIndex index)
        {
            _eventsKnownBy.Clear();
            _eventsAbout.Clear();
            _eventDay.Clear();
            _maxDay = double.NegativeInfinity;
            if (index == null) return;

            foreach (var entry in index.Entries)
            {
                if (entry == null || string.IsNullOrEmpty(entry.EventId)) continue;

                NoteDay(entry.EventId, entry.Day);

                if (entry.KnownByHeroIds != null)
                {
                    foreach (var heroId in entry.KnownByHeroIds)
                    {
                        if (string.IsNullOrEmpty(heroId)) continue;
                        Add(_eventsKnownBy, heroId, entry.EventId);
                    }
                }

                if (entry.ParticipantHeroIds != null)
                {
                    foreach (var heroId in entry.ParticipantHeroIds)
                    {
                        if (string.IsNullOrEmpty(heroId)) continue;
                        Add(_eventsAbout, heroId, entry.EventId);
                    }
                }
            }
        }

        public void NoteKnower(string heroId, string eventId, double eventDay)
        {
            if (string.IsNullOrEmpty(heroId) || string.IsNullOrEmpty(eventId)) return;

            NoteDay(eventId, eventDay);
            Add(_eventsKnownBy, heroId, eventId);
        }

        public IReadOnlyList<string> EventsKnownBy(string heroId, double currentDay)
        {
            return Visible(_eventsKnownBy, heroId, currentDay);
        }

        public IReadOnlyList<string> EventsAbout(string heroId, double currentDay)
        {
            return Visible(_eventsAbout, heroId, currentDay);
        }

        /// <summary>今天被擋下來的筆數（診斷用：要講得出「藏了幾則」）。</summary>
        public int HiddenFutureCountKnownBy(string heroId, double currentDay)
        {
            if (string.IsNullOrEmpty(heroId) || !_eventsKnownBy.TryGetValue(heroId, out var list))
            {
                return 0;
            }

            int count = 0;
            foreach (var id in list)
            {
                if (!IsVisible(id, currentDay)) count++;
            }
            return count;
        }

        private IReadOnlyList<string> Visible(Dictionary<string, List<string>> map, string heroId, double currentDay)
        {
            if (string.IsNullOrEmpty(heroId) || !map.TryGetValue(heroId, out var list))
            {
                return Array.Empty<string>();
            }

            if (EventVisibility.IsVisibleOn(_maxDay, currentDay))
            {
                return list;
            }

            var filtered = new List<string>(list.Count);
            foreach (var id in list)
            {
                if (IsVisible(id, currentDay)) filtered.Add(id);
            }
            return filtered;
        }

        private bool IsVisible(string eventId, double currentDay)
        {
            // 日期查不到的事件一律當成看得見——這個索引不是「哪些事件存在」的權威，
            // 查不到只代表沒人登記過日期，不代表它來自未來。
            return !_eventDay.TryGetValue(eventId, out double day)
                   || EventVisibility.IsVisibleOn(day, currentDay);
        }

        private void NoteDay(string eventId, double day)
        {
            _eventDay[eventId] = day;
            if (day > _maxDay) _maxDay = day;
        }

        private static void Add(Dictionary<string, List<string>> map, string heroId, string eventId)
        {
            if (!map.TryGetValue(heroId, out var list))
            {
                list = new List<string>();
                map[heroId] = list;
            }
            if (!list.Contains(eventId))
            {
                list.Add(eventId);
            }
        }

        public void Remove(string eventId)
        {
            if (string.IsNullOrEmpty(eventId)) return;

            _eventDay.Remove(eventId);

            foreach (var list in _eventsKnownBy.Values)
            {
                list.Remove(eventId);
            }

            foreach (var list in _eventsAbout.Values)
            {
                list.Remove(eventId);
            }
        }
    }
}
