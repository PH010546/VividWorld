#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using VividWorld.Core.Catalog;
using VividWorld.Core.Config;
using VividWorld.Core.Persistence;

namespace VividWorld.Core.Presentation
{
    public sealed class ChronicleProvider
    {
        private readonly PlayerHeardLogStore _log;
        private readonly Func<string, EventTemplate?> _templateByType;
        private readonly PresentationConfig _cfg;

        public ChronicleProvider(
            PlayerHeardLogStore log,
            Func<string, EventTemplate?> templateByType,
            PresentationConfig cfg)
        {
            _log = log ?? throw new ArgumentNullException(nameof(log));
            _templateByType = templateByType ?? (_ => null);
            _cfg = cfg ?? new PresentationConfig();
        }

        // 呼叫端一律得把「今天是哪一天」講出來（規格 §2.2.1）。
        public IReadOnlyList<ChronicleEntry> ForPlayer(int maxEntries, double currentDay, out ChronicleStats stats)
        {
            stats = new ChronicleStats();
            if (_log == null) return Array.Empty<ChronicleEntry>();

            stats.TotalKnown = _log.Count;
            stats.HiddenFuture = _log.HiddenFutureCount(currentDay);

            // 排序：依 PlayerHeardEntry.LearnedDay 由大到小 → 同值再依事件 Day 由大到小 → 再依 EventId（StringComparer.Ordinal）
            var sortedEntries = _log.VisibleEntries(currentDay)
                .Where(entry => entry != null && !string.IsNullOrEmpty(entry.EventId))
                .OrderByDescending(entry => entry.LearnedDay)
                .ThenByDescending(entry => entry.Day)
                .ThenBy(entry => entry.EventId, StringComparer.Ordinal)
                .ToList();

            int cap = Math.Max(1, maxEntries);
            var cappedEntries = sortedEntries.Take(cap).ToList();
            stats.Returned = cappedEntries.Count;

            var result = new List<ChronicleEntry>(cappedEntries.Count);
            foreach (var entry in cappedEntries)
            {
                var body = RumorTextComposer.Compose(entry.Facts, _cfg, isRetell: false);

                string headlineFallback = _templateByType(entry.Type ?? string.Empty)?.Headline ?? string.Empty;
                if (string.IsNullOrEmpty(headlineFallback))
                {
                    headlineFallback = entry.Type ?? string.Empty;
                }

                var item = new ChronicleEntry
                {
                    EventId = entry.EventId,
                    EventType = entry.Type ?? string.Empty,
                    Day = entry.Day,
                    LearnedDay = entry.LearnedDay,
                    PlayerHop = entry.PlayerHop,
                    SourceHeroId = entry.SourceHeroId,
                    LinkedEventId = entry.LinkedEventId,
                    HeadlineTextId = "VividWorld_EventType_" + (entry.Type ?? string.Empty),
                    HeadlineFallback = headlineFallback,
                    Body = body,
                    DayLabel = string.Empty,
                    SourceHeroName = null
                };

                result.Add(item);
            }

            return result;
        }
    }

    public sealed class ChronicleStats
    {
        public int TotalKnown;       // 紀錄的總筆數
        public int HiddenFuture;     // 日期比今天晚而被藏起來的
        public int Returned;         // 實際回傳幾則（＝夾到上限之後）
    }
}
