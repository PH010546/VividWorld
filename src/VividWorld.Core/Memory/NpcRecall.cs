#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using VividWorld.Core.Catalog;
using VividWorld.Core.Config;
using VividWorld.Core.Events;
using VividWorld.Core.Rumors;

namespace VividWorld.Core.Memory
{
    public enum RecallExclusionReason
    {
        NotVisible,     // 事件對傳聞系統不可見（未走漏的秘密）
        NotKnown,       // 英雄無知情紀錄
        Forgotten,      // 已遺忘（ForgetDay 早於或等於當前日期）
        Outdated,       // 已過時（OutdatedDay 非空）
        NoFacts,        // 手上沒有保留任何碎片
        WontTellOwn,    // 當事人自己不講（selfTell 規則排除）
        RetiredType     // 停用的事件型別：已存下來的也不再傳，事件資料保留
    }

    public sealed class RecallExclusion
    {
        public string EventId { get; }
        public RecallExclusionReason Reason { get; }
        public string? Detail { get; }

        public RecallExclusion(string eventId, RecallExclusionReason reason, string? detail = null)
        {
            EventId = eventId;
            Reason = reason;
            Detail = detail;
        }

        public override string ToString() => string.IsNullOrEmpty(Detail)
            ? $"{EventId}: {Reason}"
            : $"{EventId}: {Reason} ({Detail})";
    }

    public sealed class NpcRecalledMemory
    {
        public string EventId { get; set; } = string.Empty;
        public WorldEvent Event { get; set; } = null!;
        public KnownByEntry Entry { get; set; } = null!;
        public IReadOnlyList<Fact> Facts { get; set; } = Array.Empty<Fact>();
        public int Hop { get; set; }
        public string? SourceHeroId { get; set; }
        public double LearnedDay { get; set; }
        public double? Interest { get; set; }
        public bool IsCorrection { get; set; }
    }

    public sealed class NpcRecallResult
    {
        public string HeroId { get; }
        public double CurrentDay { get; }
        public IReadOnlyList<NpcRecalledMemory> Items { get; }
        public IReadOnlyList<RecallExclusion> Exclusions { get; }
        public int CandidateCount { get; }

        public NpcRecallResult(
            string heroId,
            double currentDay,
            IReadOnlyList<NpcRecalledMemory> items,
            IReadOnlyList<RecallExclusion> exclusions,
            int candidateCount)
        {
            HeroId = heroId ?? string.Empty;
            CurrentDay = currentDay;
            Items = items ?? Array.Empty<NpcRecalledMemory>();
            Exclusions = exclusions ?? Array.Empty<RecallExclusion>();
            CandidateCount = candidateCount;
        }

        public int ExclusionCountByReason(RecallExclusionReason reason)
        {
            int count = 0;
            for (int i = 0; i < Exclusions.Count; i++)
            {
                if (Exclusions[i].Reason == reason) count++;
            }
            return count;
        }

        public string ExclusionSummary()
        {
            var summary = string.Format(CultureInfo.InvariantCulture,
                "forgotten={0}, outdated={1}, notVisible={2}, notKnown={3}, noFacts={4}",
                ExclusionCountByReason(RecallExclusionReason.Forgotten),
                ExclusionCountByReason(RecallExclusionReason.Outdated),
                ExclusionCountByReason(RecallExclusionReason.NotVisible),
                ExclusionCountByReason(RecallExclusionReason.NotKnown),
                ExclusionCountByReason(RecallExclusionReason.NoFacts));

            int wontTell = ExclusionCountByReason(RecallExclusionReason.WontTellOwn);
            if (wontTell > 0)
            {
                summary += string.Format(CultureInfo.InvariantCulture, ", wontTellOwn={0}", wontTell);
            }

            int retired = ExclusionCountByReason(RecallExclusionReason.RetiredType);
            if (retired > 0)
            {
                summary += string.Format(CultureInfo.InvariantCulture, ", retiredType={0}", retired);
            }
            return summary;
        }
    }

    /// <summary>
    /// NPC 當前記憶查詢（純函式）。
    /// 依據知情、遺忘、過時與碎片留存規則篩選事件，並依更正優先、興趣高、學習日期近、事件編號遞增排序。
    /// </summary>
    public static class NpcRecallQuery
    {
        public static NpcRecallResult Query(
            string heroId,
            double currentDay,
            int maxCount,
            IEnumerable<WorldEvent>? candidateEvents,
            IFactRetentionPolicy? retentionPolicy = null,
            Func<string, bool>? isEventKnown = null,
            VividWorldConfig? config = null,
            Func<string, EventTemplate?>? getTemplate = null,
            IHeroTraitLookup? traits = null)
        {
            if (string.IsNullOrEmpty(heroId) || candidateEvents == null)
            {
                return new NpcRecallResult(heroId ?? string.Empty, currentDay, Array.Empty<NpcRecalledMemory>(), Array.Empty<RecallExclusion>(), 0);
            }

            retentionPolicy ??= new ThresholdRetentionPolicy(config?.Retention ?? new RetentionConfig());

            var candidateList = candidateEvents as IReadOnlyList<WorldEvent> ?? candidateEvents.ToList();
            var exclusions = new List<RecallExclusion>();
            var eligible = new List<NpcRecalledMemory>();

            HashSet<string>? knownEventIds = null;
            bool CheckLinkedKnown(string linkedId)
            {
                if (string.IsNullOrEmpty(linkedId)) return false;
                if (isEventKnown != null) return isEventKnown(linkedId);
                if (knownEventIds == null)
                {
                    knownEventIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    for (int i = 0; i < candidateList.Count; i++)
                    {
                        var e = candidateList[i];
                        if (e != null && e.IsKnownBy(heroId))
                        {
                            knownEventIds.Add(e.EventId);
                        }
                    }
                }
                return knownEventIds.Contains(linkedId);
            }

            for (int i = 0; i < candidateList.Count; i++)
            {
                var evt = candidateList[i];
                if (evt == null) continue;

                // 1. 事件對傳聞系統可見（未走漏的秘密不算）
                if (!evt.IsVisibleToRumorSystem)
                {
                    exclusions.Add(new RecallExclusion(evt.EventId, RecallExclusionReason.NotVisible, "Secret not leaked or event not visible"));
                    continue;
                }

                // 2. 這個人有知情紀錄
                var entry = evt.EntryFor(heroId);
                if (entry == null)
                {
                    exclusions.Add(new RecallExclusion(evt.EventId, RecallExclusionReason.NotKnown, $"Hero {heroId} has no known entry"));
                    continue;
                }

                // 3. 沒遺忘（ForgetDay 為 null 或晚於今天）
                if (entry.ForgetDay.HasValue && entry.ForgetDay.Value <= currentDay)
                {
                    exclusions.Add(new RecallExclusion(evt.EventId, RecallExclusionReason.Forgotten, string.Format(CultureInfo.InvariantCulture, "Forgot on day {0:F1}", entry.ForgetDay.Value)));
                    continue;
                }

                // 4. 沒過時（OutdatedDay 為 null）
                if (entry.OutdatedDay.HasValue)
                {
                    exclusions.Add(new RecallExclusion(evt.EventId, RecallExclusionReason.Outdated, string.Format(CultureInfo.InvariantCulture, "Outdated on day {0:F1}", entry.OutdatedDay.Value)));
                    continue;
                }

                // 5. 停用的事件型別：已存下來的也不交給 AI 模組
                if (VividWorld.Core.Catalog.RetiredTypeEvaluator.IsRetired(evt, getTemplate))
                {
                    exclusions.Add(new RecallExclusion(evt.EventId, RecallExclusionReason.RetiredType, "retired type"));
                    continue;
                }

                // 6. 當事人自己不講（selfTell 規則排除）
                var selfTell = VividWorld.Core.Catalog.SelfTellEvaluator.Evaluate(evt, heroId, getTemplate, traits);
                if (!selfTell.CanTell)
                {
                    exclusions.Add(new RecallExclusion(evt.EventId, RecallExclusionReason.WontTellOwn, selfTell.ReasonText));
                    continue;
                }

                // 7. 手上至少一塊碎片（KnownFactIds 非 null 時以它為準，否則照保留規則）
                IReadOnlyList<Fact> facts;
                if (entry.KnownFactIds != null)
                {
                    var factSet = new HashSet<string>(entry.KnownFactIds, StringComparer.OrdinalIgnoreCase);
                    facts = evt.Facts.Where(f => factSet.Contains(f.Id)).ToList();
                }
                else
                {
                    // 第三個參數是「握著這個版本的人」，跟對話那邊傳講述者自己一樣；傳來源會在機率保留下算成別人的版本
                    facts = retentionPolicy.Retain(evt, entry.Hop, heroId);
                }

                if (facts.Count == 0)
                {
                    exclusions.Add(new RecallExclusion(evt.EventId, RecallExclusionReason.NoFacts, "No facts retained"));
                    continue;
                }

                bool isCorrection = !string.IsNullOrEmpty(evt.LinkedEventId) && CheckLinkedKnown(evt.LinkedEventId!);

                eligible.Add(new NpcRecalledMemory
                {
                    EventId = evt.EventId,
                    Event = evt,
                    Entry = entry,
                    Facts = facts,
                    Hop = entry.Hop,
                    SourceHeroId = entry.SourceHeroId,
                    LearnedDay = entry.LearnedDay,
                    Interest = entry.Interest,
                    IsCorrection = isCorrection
                });
            }

            // 排序：更正優先 → Interest 高 → LearnedDay 近 → 事件 id（穩定）。取前 N。
            var sorted = eligible
                .OrderByDescending(x => x.IsCorrection)
                .ThenByDescending(x => x.Interest ?? 0.0)
                .ThenByDescending(x => x.LearnedDay)
                .ThenBy(x => x.EventId, StringComparer.Ordinal);

            var items = (maxCount <= 0 ? Enumerable.Empty<NpcRecalledMemory>() : sorted.Take(maxCount)).ToList();

            return new NpcRecallResult(heroId, currentDay, items, exclusions, candidateList.Count);
        }
    }
}
