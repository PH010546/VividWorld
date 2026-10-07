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

        private static readonly HashSet<string> LaterEventTypes = new(StringComparer.Ordinal)
        {
            "hero_released",
            "hero_escaped_captivity",
            "hero_rescued_from_bandits",
            "hero_escaped_bandits"
        };

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

            // 打開紀事時補一次舊紀錄源頭
            _log.EnsureRoots();

            stats.TotalKnown = _log.Count;
            stats.HiddenFuture = _log.HiddenFutureCount(currentDay);

            var visible = _log.VisibleEntries(currentDay)
                .Where(entry => entry != null && !string.IsNullOrEmpty(entry.EventId))
                .ToList();

            if (visible.Count == 0)
            {
                stats.Returned = 0;
                stats.TotalBlocks = 0;
                stats.CappedOutBlocks = 0;
                stats.ConflictBlocks = 0;
                stats.FallbackTitleBlocks = 0;
                return Array.Empty<ChronicleEntry>();
            }

            // 1. 依 RootEventId 分組，一組一個區塊
            var groups = new Dictionary<string, List<PlayerHeardEntry>>(StringComparer.Ordinal);
            foreach (var entry in visible)
            {
                string rootId = !string.IsNullOrEmpty(entry.RootEventId) ? entry.RootEventId! : entry.EventId;
                if (!groups.TryGetValue(rootId, out var list))
                {
                    list = new List<PlayerHeardEntry>();
                    groups[rootId] = list;
                }
                list.Add(entry);
            }

            var blocks = new List<ChronicleEntry>(groups.Count);

            foreach (var kvp in groups)
            {
                string rootId = kvp.Key;
                var blockEntries = kvp.Value;

                // 源頭型別、日子、角色
                var rootEntry = blockEntries.FirstOrDefault(e => string.Equals(e.EventId, rootId, StringComparison.Ordinal));
                string rootType = blockEntries.FirstOrDefault(e => !string.IsNullOrEmpty(e.RootType))?.RootType
                    ?? (rootEntry?.Type ?? blockEntries[0].Type ?? string.Empty);

                double rootDay = blockEntries.FirstOrDefault(e => e.RootDay > 0)?.RootDay
                    ?? (rootEntry?.Day ?? blockEntries[0].Day);

                var rootParts = blockEntries.FirstOrDefault(e => e.RootParticipants != null && e.RootParticipants.Count > 0)?.RootParticipants
                    ?? (rootEntry?.Participants ?? blockEntries[0].Participants ?? new Dictionary<string, string>());

                // 區塊在清單裡的位置：
                // 區塊裡所有來源的 Day（那個人第一次講給玩家聽的日子）的最大值，新到舊；
                // 來源沒有日子的舊紀錄用 LearnedDay。
                double maxSourceDay = double.MinValue;
                foreach (var e in blockEntries)
                {
                    foreach (var s in e.EffectiveSources())
                    {
                        // 打探的回答不是聽到新消息：不把區塊往清單前面推
                        if (s.HasProbeAnswer) continue;
                        double d = s.Day > 0 ? s.Day : e.LearnedDay;
                        if (d > maxSourceDay) maxSourceDay = d;
                    }
                }
                if (maxSourceDay == double.MinValue) maxSourceDay = 0.0;

                // 檢查「有矛盾」
                bool hasConflict = false;
                string? conflictReason = null;

                var entriesById = blockEntries.ToDictionary(e => e.EventId, StringComparer.Ordinal);
                foreach (var y in blockEntries)
                {
                    if (!string.IsNullOrEmpty(y.LinkedEventId)
                        && y.Type != null
                        && y.Type.StartsWith("talk_", StringComparison.Ordinal)
                        && entriesById.TryGetValue(y.LinkedEventId!, out var x))
                    {
                        hasConflict = true;
                        conflictReason = $"{x.EventId} & {y.EventId}";
                        break;
                    }
                }

                if (!hasConflict)
                {
                    var naturalDeath = blockEntries.FirstOrDefault(e => e.Type == "hero_died_of_old_age" || e.Type == "hero_died_naturally");
                    var poison = blockEntries.FirstOrDefault(e => e.Type == "conduct_poisoned");
                    if (naturalDeath != null && poison != null)
                    {
                        hasConflict = true;
                        conflictReason = $"{naturalDeath.EventId} & {poison.EventId}";
                    }
                }

                // 區塊裡的事：
                // 源頭那件事在最前面；其餘的事依該事件的 Day 由舊到新
                var rootMatterEntries = blockEntries.Where(e => !LaterEventTypes.Contains(e.Type)).ToList();
                var laterMatters = blockEntries
                    .Where(e => LaterEventTypes.Contains(e.Type))
                    .OrderBy(e => e.Day)
                    .ThenBy(e => e.EventId, StringComparer.Ordinal)
                    .ToList();

                var matters = new List<ChronicleMatter>();

                if (rootMatterEntries.Count > 0)
                {
                    var rootSources = FlattenSources(rootMatterEntries);
                    string mType = rootType;
                    string mFallback = _templateByType(mType)?.Headline ?? mType;
                    matters.Add(new ChronicleMatter
                    {
                        EventId = rootId,
                        EventType = mType,
                        Day = rootDay,
                        HasHeading = false, // 第一件事不加小標
                        HeadlineTextId = "VividWorld_EventType_" + mType,
                        HeadlineFallback = mFallback,
                        NamedHeadlineTextId = ChronicleHeadline.NamedTextIdFor(mType),
                        Participants = new Dictionary<string, string>(rootParts, StringComparer.Ordinal),
                        Sources = rootSources
                    });
                }

                for (int i = 0; i < laterMatters.Count; i++)
                {
                    var lm = laterMatters[i];
                    var lmSources = FlattenSources(new[] { lm });
                    string mType = lm.Type ?? string.Empty;
                    string mFallback = _templateByType(mType)?.Headline ?? mType;
                    // 源頭那件事玩家沒聽過時，第一段就是後來的事：仍要小標，否則會被當成源頭那件事的說法
                    bool hasHeading = true;
                    matters.Add(new ChronicleMatter
                    {
                        EventId = lm.EventId,
                        EventType = mType,
                        Day = lm.Day,
                        HasHeading = hasHeading,
                        HeadlineTextId = "VividWorld_EventType_" + mType,
                        HeadlineFallback = mFallback,
                        NamedHeadlineTextId = ChronicleHeadline.NamedTextIdFor(mType),
                        Participants = lm.Participants != null ? new Dictionary<string, string>(lm.Participants, StringComparer.Ordinal) : new Dictionary<string, string>(),
                        Sources = lmSources
                    });
                }

                var allSources = matters.SelectMany(m => m.Sources).ToList();
                string headlineFallback = _templateByType(rootType)?.Headline ?? rootType;

                var block = new ChronicleEntry
                {
                    EventId = rootId,
                    EventType = rootType,
                    Day = rootDay,
                    LearnedDay = maxSourceDay,
                    PlayerHop = blockEntries.Min(e => e.PlayerHop),
                    SourceHeroId = blockEntries[0].SourceHeroId,
                    LinkedEventId = blockEntries[0].LinkedEventId,
                    HeadlineTextId = "VividWorld_EventType_" + rootType,
                    HeadlineFallback = headlineFallback,
                    NamedHeadlineTextId = ChronicleHeadline.NamedTextIdFor(rootType),
                    HeadlineNameVar = ChronicleHeadline.PrisonerVarOf(rootEntry ?? blockEntries[0]),
                    RootParticipants = new Dictionary<string, string>(rootParts, StringComparer.Ordinal),
                    Participants = new Dictionary<string, string>(rootParts, StringComparer.Ordinal),
                    HasConflict = hasConflict,
                    ConflictReason = conflictReason,
                    Matters = matters,
                    Sources = allSources,
                    Body = allSources.Count > 0 ? allSources[0].Body : new ComposedRumor(),
                    DayLabel = string.Empty,
                    SourceHeroName = null
                };

                blocks.Add(block);
            }

            // 排序：MaxSourceDay 新到舊 → 同值依源頭日子 Day 新到舊 → 再依 RootEventId (Ordinal)
            var sortedBlocks = blocks
                .OrderByDescending(b => b.LearnedDay)
                .ThenByDescending(b => b.Day)
                .ThenBy(b => b.EventId, StringComparer.Ordinal)
                .ToList();

            int cap = Math.Max(1, maxEntries);
            var cappedBlocks = sortedBlocks.Take(cap).ToList();

            stats.TotalBlocks = sortedBlocks.Count;
            stats.Returned = cappedBlocks.Count;
            stats.CappedOutBlocks = Math.Max(0, sortedBlocks.Count - cappedBlocks.Count);
            stats.ConflictBlocks = cappedBlocks.Count(b => b.HasConflict);
            stats.FallbackTitleBlocks = 0;

            return cappedBlocks;
        }

        /// <summary>
        /// 把一件事底下所有紀錄的每一份來源攤平：
        /// 依那份來源的 Day（那個人第一次講給玩家聽的日子）由舊到新；
        /// 同值依紀錄的 LearnedDay、EventId（Ordinal）、來源在清單裡的先後。
        /// </summary>
        private IReadOnlyList<ChronicleSource> FlattenSources(IEnumerable<PlayerHeardEntry> entries)
        {
            var list = new List<(PlayerHeardEntry entry, PlayerHeardSource source, ChronicleSource built, int index)>();
            foreach (var entry in entries)
            {
                var builtSources = BuildSources(entry);
                var effSources = entry.EffectiveSources();
                for (int i = 0; i < builtSources.Count; i++)
                {
                    var eff = i < effSources.Count ? effSources[i] : effSources[0];
                    list.Add((entry, eff, builtSources[i], i));
                }
            }

            return list
                .OrderBy(t => t.source.Day > 0 ? t.source.Day : t.entry.LearnedDay)
                .ThenBy(t => t.entry.LearnedDay)
                .ThenBy(t => t.entry.EventId, StringComparer.Ordinal)
                .ThenBy(t => t.index)
                .Select(t => t.built)
                .ToList();
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
                if (source.HasProbeAnswer)
                {
                    body = new ComposedRumor
                    {
                        SentenceKeyCandidate = source.ProbeAnswerKey,
                        SentenceVars = source.ProbeAnswerVars ?? new Dictionary<string, string>(),
                        SpeakerHeroId = source.HeroId
                    };
                }
                else if (source.HasSpokenLine)
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
                    HasProbeAnswer = source.HasProbeAnswer,
                    ProbeAnswerKey = source.ProbeAnswerKey,
                    ProbeAnswerVars = source.ProbeAnswerVars,
                    ProbeAddressKey = source.ProbeAddressKey,
                    ProbeAddressHeroId = source.ProbeAddressHeroId,
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
        public int TotalKnown;           // 紀錄的總筆數
        public int HiddenFuture;         // 日期比今天晚而被藏起來的
        public int Returned;             // 實際回傳幾則區塊（＝夾到上限之後）
        public int TotalBlocks;          // 總區塊數（未夾上限前）
        public int CappedOutBlocks;      // 被上限擋掉的區塊數
        public int ConflictBlocks;       // 標了有矛盾的區塊數
        public int FallbackTitleBlocks;  // 標題退回不帶名字的區塊數
    }
}
