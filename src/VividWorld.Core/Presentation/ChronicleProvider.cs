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
                    NamedHeadlineTextId = ChronicleHeadline.NamedTextIdFor(entry.Type),
                    HeadlineNameVar = ChronicleHeadline.PrisonerVarOf(entry),
                    Body = body,
                    Sources = BuildSources(entry),
                    DayLabel = string.Empty,
                    SourceHeroName = null
                };

                result.Add(item);
            }

            return result;
        }

        /// <summary>
        /// 每份來源一塊：那一份講的碎片組成事實（順序照整筆紀錄的碎片順序），感想照存下來的鍵重建。
        /// 沒有來源清單的舊紀錄得到合成的一份，內容就是整筆紀錄。
        /// </summary>
        private IReadOnlyList<ChronicleSource> BuildSources(PlayerHeardEntry entry)
        {
            var list = new List<ChronicleSource>();
            foreach (var source in entry.EffectiveSources())
            {
                var told = new HashSet<string>(source.FactIds ?? new List<string>(), StringComparer.Ordinal);
                var facts = entry.Facts.Where(f => told.Contains(f.Id)).ToList();
                if (facts.Count == 0)
                {
                    // 來源清單裡的碎片代號一個也對不上整筆紀錄：寧可顯示整筆的內容，也不要留一塊空白
                    facts = entry.Facts;
                }

                ComposedRumor body;
                if (source.HasSpokenLine)
                {
                    body = RumorTextComposer.Reconstruct(facts, source, _cfg, entry.EventId);
                }
                else
                {
                    body = RumorTextComposer.Compose(facts, _cfg, isRetell: false);
                }

                var item = new ChronicleSource
                {
                    HeroId = source.HeroId,
                    Day = source.Day,
                    Hop = source.Hop,
                    FactCount = facts.Count,
                    HasSpokenLine = source.HasSpokenLine,
                    Body = body
                };

                if (source.HasFeeling)
                {
                    item.Feeling = body.Feeling ?? new VividWorld.Core.Feelings.FeelingDecision
                    {
                        SpeakerId = source.HeroId ?? string.Empty,
                        EventId = entry.EventId,
                        LineKey = source.FeelingLineKey,
                        AddressKey = source.FeelingAddressKey,
                        FocusHeroId = source.FeelingFocusHeroId
                    };
                }

                list.Add(item);
            }
            return list;
        }
    }

    public sealed class ChronicleStats
    {
        public int TotalKnown;       // 紀錄的總筆數
        public int HiddenFuture;     // 日期比今天晚而被藏起來的
        public int Returned;         // 實際回傳幾則（＝夾到上限之後）
    }
}
