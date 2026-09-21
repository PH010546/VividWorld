using System;
using System.Collections.Generic;
using System.Linq;
using VividWorld.Core.Catalog;
using VividWorld.Core.Config;
using VividWorld.Core.Events;
using VividWorld.Core.Persistence;
using VividWorld.Core.Rumors;

namespace VividWorld.Core.Presentation
{
    public sealed class ChronicleProvider
    {
        private readonly RumorEngine _engine;
        private readonly KnownByIndex _index;
        private readonly Func<string, WorldEvent?> _load;
        private readonly Func<string, EventTemplate?> _templateByType;
        private readonly string _playerHeroId;
        private readonly PresentationConfig _cfg;

        public ChronicleProvider(
            RumorEngine engine,
            KnownByIndex index,
            Func<string, WorldEvent?> load,
            string playerHeroId,
            PresentationConfig cfg)
            : this(engine, index, load, _ => null, playerHeroId, cfg)
        {
        }

        public ChronicleProvider(
            RumorEngine engine,
            KnownByIndex index,
            Func<string, WorldEvent?> load,
            Func<string, EventTemplate?> templateByType,
            string playerHeroId,
            PresentationConfig cfg)
        {
            _engine = engine;
            _index = index;
            _load = load;
            _templateByType = templateByType;
            _playerHeroId = playerHeroId ?? string.Empty;
            _cfg = cfg;
        }

        // 刻意沒有 ForPlayer(maxEntries) 這個多載：它只能用 double.MaxValue 當「今天」，
        // 而那會讓日期比今天晚的事件全部看得見——正是規格 §2.2.1 要擋掉的那件事。
        // 呼叫端一律得把「今天是哪一天」講出來。
        public IReadOnlyList<ChronicleEntry> ForPlayer(int maxEntries, double currentDay, out ChronicleStats stats)
        {
            stats = new ChronicleStats();
            if (_index == null) return Array.Empty<ChronicleEntry>();

            var ids = _index.EventsKnownBy(_playerHeroId, currentDay);
            stats.TotalKnown = ids.Count;
            stats.HiddenFuture = _index.HiddenFutureCountKnownBy(_playerHeroId, currentDay);

            var collected = new List<ChronicleEntry>();

            foreach (var id in ids)
            {
                if (string.IsNullOrEmpty(id)) continue;

                var evt = _load?.Invoke(id);
                if (evt == null)
                {
                    stats.SkippedNoShard++;
                    continue;
                }

                // 未洩漏的秘密在整個傳聞系統中不可見。**要算進統計**：不算的話
                // log 那一行的「顯示幾則 ＋ 跳過幾則」加起來對不上總數，看的人只會以為資料掉了。
                if (!evt.IsVisibleToRumorSystem)
                {
                    stats.SkippedSecret++;
                    continue;
                }

                var entry = evt.EntryFor(_playerHeroId);
                if (entry == null)
                {
                    stats.SkippedNoEntry++;
                    continue;
                }

                IReadOnlyList<Fact> retained;
                if (entry.KnownFactIds != null)
                {
                    var set = new HashSet<string>(entry.KnownFactIds, StringComparer.Ordinal);
                    retained = evt.Facts.Where(f => set.Contains(f.Id)).ToList();
                }
                else
                {
                    // 引擎不在就**給空的**，絕不退回 evt.Facts——那是 hop 0 的完整集合，
                    // 等於偷偷把全知版本交給玩家（卡片禁令第 2 條）。寧可只剩標題。
                    retained = _engine != null
                        ? _engine.FactsAtHop(evt, entry.Hop, entry.SourceHeroId ?? string.Empty)
                        : Array.Empty<Fact>();
                }

                var body = RumorTextComposer.Compose(evt, retained, _cfg, isRetell: false);

                string headlineFallback = _templateByType?.Invoke(evt.Type)?.Headline ?? string.Empty;
                if (string.IsNullOrEmpty(headlineFallback))
                {
                    headlineFallback = evt.Type ?? string.Empty;
                }

                var item = new ChronicleEntry
                {
                    EventId = evt.EventId ?? string.Empty,
                    EventType = evt.Type ?? string.Empty,
                    Day = evt.Day,
                    PlayerHop = entry.Hop,
                    SourceHeroId = entry.SourceHeroId,
                    LinkedEventId = evt.LinkedEventId,
                    HeadlineTextId = "VividWorld_EventType_" + (evt.Type ?? string.Empty),
                    HeadlineFallback = headlineFallback,
                    Body = body,
                    DayLabel = string.Empty,
                    SourceHeroName = null
                };

                collected.Add(item);
            }

            // 排序：Day 由大到小；同一天再用 EventId 的 StringComparer.Ordinal 當第二鍵（穩定排序）
            var sorted = collected
                .OrderByDescending(e => e.Day)
                .ThenBy(e => e.EventId, StringComparer.Ordinal)
                .ToList();

            int cap = Math.Max(1, maxEntries);
            var result = sorted.Take(cap).ToList();
            stats.Returned = result.Count;

            return result;
        }
    }

    public sealed class ChronicleStats
    {
        public int TotalKnown;       // index 回報的總數
        public int HiddenFuture;     // 日期比今天晚而被藏起來的
        public int SkippedNoShard;   // load 回 null
        public int SkippedNoEntry;   // 分片裡找不到玩家那筆 KnownByEntry
        public int SkippedSecret;    // 還沒走漏的秘密（§9.4 契約 3）
        public int Returned;         // 實際回傳幾則（＝夾到上限之後）
    }
}
