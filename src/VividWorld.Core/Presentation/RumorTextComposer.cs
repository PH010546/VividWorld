using System;
using System.Collections.Generic;
using System.Linq;
using VividWorld.Core.Config;
using VividWorld.Core.Events;
using VividWorld.Core.Persistence;

namespace VividWorld.Core.Presentation
{
    public static class RumorTextComposer
    {
        public const string RetellPrefixTextId = "VividWorld_RetellPrefix";
        public const string RetellPrefixFallback = "I was there, in fact—";
        public const string RetellSelfPrefixTextId = RumorPrefixSelector.RetellSelfTextId;
        public const string RetellSelfPrefixFallback = RumorPrefixSelector.RetellSelfFallback;

        public static ComposedRumor Compose(IReadOnlyList<Fact> retained,
                                            PresentationConfig cfg, RumorPrefix? prefix)
        {
            return Compose(null, retained, cfg, prefix, null);
        }

        /// <param name="isGist">交情不夠、只講大概：不接當事人句尾（只講大概聽不到他的心情）。</param>
        /// <param name="heldBack">只講大概而且確實少講了他知道的事：事實句後面接一句收尾。他本來就只知道這麼多時不接，免得暗示還有更多。</param>
        public static ComposedRumor Compose(WorldEvent? evt, IReadOnlyList<Fact> retained,
                                            PresentationConfig cfg, RumorPrefix? prefix, string? speakerHeroId = null, string? sourceHeroId = null,
                                            bool isGist = false, bool heldBack = false,
                                            Catalog.EventTemplate? template = null, Rumors.TraitProfile? speakerTraits = null)
        {
            var prefixTextId = prefix?.TextId;
            var prefixFallback = prefix?.Fallback;
            var prefixVars = new Dictionary<string, string>();
            if (prefix?.Vars != null)
            {
                foreach (var kvp in prefix.Vars)
                {
                    prefixVars[kvp.Key] = kvp.Value;
                }
            }

            string? speakerRole = !string.IsNullOrEmpty(speakerHeroId) ? evt?.RoleOf(speakerHeroId!) : null;
            string? sourceRole = !string.IsNullOrEmpty(sourceHeroId) ? evt?.RoleOf(sourceHeroId!) : null;

            var roles = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (evt?.Participants != null)
            {
                foreach (var kvp in evt.Participants)
                {
                    if (!string.IsNullOrEmpty(kvp.Key) && !string.IsNullOrEmpty(kvp.Value))
                    {
                        roles[kvp.Key.ToLowerInvariant()] = kvp.Value;
                    }
                }
            }

            if (retained == null || retained.Count == 0)
            {
                return new ComposedRumor
                {
                    Parts = Array.Empty<ComposedFactPart>(),
                    PrefixTextId = prefixTextId,
                    PrefixFallback = prefixFallback,
                    PrefixVars = prefixVars,
                    SpeakerHeroId = speakerHeroId,
                    SpeakerRole = speakerRole,
                    SourceHeroId = sourceHeroId,
                    SourceRole = sourceRole,
                    Roles = roles,
                    IsGist = isGist
                };
            }

            var factOrder = cfg?.FactOrder;
            bool placeFirst = cfg?.PlaceFirst ?? true;

            int GetCategoryRank(FactCategory cat)
            {
                if (placeFirst && cat == FactCategory.Where)
                {
                    return -1;
                }
                if (factOrder == null || factOrder.Length == 0) return 0;
                string catName = cat.ToString();
                for (int i = 0; i < factOrder.Length; i++)
                {
                    if (string.Equals(factOrder[i], catName, StringComparison.OrdinalIgnoreCase))
                    {
                        return i;
                    }
                }
                return int.MaxValue;
            }

            // OrderBy 在 LINQ 中是穩定排序 (stable sort)，能保持同類別內作者撰寫順序
            var sorted = retained.OrderBy(f => GetCategoryRank(f.Category)).ToList();

            var parts = sorted.Select(f => new ComposedFactPart
            {
                TextId = f.TextId ?? string.Empty,
                Fallback = f.Text ?? string.Empty,
                Vars = f.Vars != null ? new Dictionary<string, string>(f.Vars) : new Dictionary<string, string>()
            }).ToList();

            // 依事件原本 Facts 順序計算整句句型候選鍵
            var orderedFacts = (evt?.Facts != null && evt.Facts.Count > 0)
                ? retained.OrderBy(f =>
                {
                    int idx = evt.Facts.IndexOf(f);
                    return idx >= 0 ? idx : int.MaxValue;
                }).ToList()
                : retained.ToList();

            string stem = string.Empty;
            var segments = new List<string>();
            var sentenceVars = new Dictionary<string, string>(StringComparer.Ordinal);

            foreach (var f in orderedFacts)
            {
                if (!string.IsNullOrEmpty(f.TextId))
                {
                    if (string.IsNullOrEmpty(stem))
                    {
                        stem = SentenceCombinationEnumerator.ExtractStem(f.TextId);
                    }
                    segments.Add(SentenceCombinationEnumerator.ExtractSegment(f.TextId));
                }
                if (f.Vars != null)
                {
                    foreach (var kvp in f.Vars)
                    {
                        sentenceVars[kvp.Key] = kvp.Value;
                    }
                }
            }

            string? roleUpper = null;
            if (!string.IsNullOrEmpty(speakerRole))
            {
                roleUpper = speakerRole!.ToUpperInvariant();
            }
            else if (!string.IsNullOrEmpty(speakerHeroId))
            {
                var matchingKey = orderedFacts
                    .SelectMany(f => f.Vars ?? Enumerable.Empty<KeyValuePair<string, string>>())
                    .FirstOrDefault(kv => kv.Value == "hero:" + speakerHeroId).Key;
                if (!string.IsNullOrEmpty(matchingKey))
                {
                    roleUpper = matchingKey.ToUpperInvariant();
                }
            }

            // 查鍵順序：說話的人是當事人 ⇒ 只查 _Self_角色；
            // 否則親眼看到的旁人 ⇒ 先查 _Witness；來源是當事人 ⇒ 先查 _From_角色；沒有再查不帶尾巴的。
            // 沒有事件（紀事視窗）就沒有說話的人與來源，照舊用不帶尾巴的鍵。
            var candidateKeys = new List<string>();
            IReadOnlyList<string> selfFeelingKeyCandidates = Array.Empty<string>();

            if (!string.IsNullOrEmpty(stem) && segments.Count > 0)
            {
                if (!string.IsNullOrEmpty(roleUpper))
                {
                    candidateKeys.Add(SentenceCombinationEnumerator.ComputeSentenceKey(stem, segments, roleUpper));
                    if (!isGist)
                    {
                        selfFeelingKeyCandidates = SentenceCombinationEnumerator.ComputeSelfFeelingKeys(stem, segments, roleUpper!);
                    }
                }
                else
                {
                    string? sourceRoleUpper = null;
                    if (!string.IsNullOrEmpty(sourceRole))
                    {
                        sourceRoleUpper = sourceRole!.ToUpperInvariant();
                    }
                    else if (!string.IsNullOrEmpty(sourceHeroId))
                    {
                        var matchingKey = orderedFacts
                            .SelectMany(f => f.Vars ?? Enumerable.Empty<KeyValuePair<string, string>>())
                            .FirstOrDefault(kv => kv.Value == "hero:" + sourceHeroId).Key;
                        if (!string.IsNullOrEmpty(matchingKey))
                        {
                            sourceRoleUpper = matchingKey.ToUpperInvariant();
                        }
                    }

                    // 親眼看到的旁人（開頭語是「我親眼看到的」或重述的「我當時就在場」）先查 _Witness
                    bool speakerIsEyewitness = prefix != null
                        && (prefix.Kind == RumorPrefixKind.Eyewitness || prefix.Kind == RumorPrefixKind.Retell);
                    if (speakerIsEyewitness)
                    {
                        candidateKeys.Add(SentenceCombinationEnumerator.ComputeWitnessSentenceKey(stem, segments));
                    }
                    else if (!string.IsNullOrEmpty(sourceRoleUpper))
                    {
                        candidateKeys.Add(SentenceCombinationEnumerator.ComputeFromSentenceKey(stem, segments, sourceRoleUpper!));
                    }
                    candidateKeys.Add(SentenceCombinationEnumerator.ComputeSentenceKey(stem, segments, null));
                }
            }

            // 當事人句尾的個性版本：依說話的當事人的特質，挑模板宣告的第一個符合的傾向
            string? variantKey = null;
            string? variantLog = null;
            if (selfFeelingKeyCandidates.Count > 0 && !string.IsNullOrEmpty(roleUpper))
            {
                var choice = Catalog.SelfFeelingVariantSelector.Choose(template, roleUpper, speakerTraits);
                variantLog = choice.Log;
                if (choice.Tendency != null)
                {
                    selfFeelingKeyCandidates = Catalog.SelfFeelingVariantSelector.InsertVariantKey(selfFeelingKeyCandidates, choice.Tendency);
                    variantKey = selfFeelingKeyCandidates[selfFeelingKeyCandidates.Count - 2];
                }
            }

            string? sentenceKeyCandidate = candidateKeys.Count > 0 ? candidateKeys[0] : null;

            // 收尾只在對話裡講給玩家時才有（有說話的人）；紀事與推給 AI 對話模組都不帶
            string? closingKey = null;
            if (isGist && heldBack && evt != null && !string.IsNullOrEmpty(speakerHeroId))
            {
                string group = GistClosing.GroupFor(!string.IsNullOrEmpty(roleUpper), prefix);
                closingKey = GistClosing.Select(group, speakerHeroId, evt.EventId);
            }

            return new ComposedRumor
            {
                Parts = parts,
                PrefixTextId = prefixTextId,
                PrefixFallback = prefixFallback,
                PrefixVars = prefixVars,
                SpeakerHeroId = speakerHeroId,
                SpeakerRole = speakerRole,
                SourceHeroId = sourceHeroId,
                SourceRole = sourceRole,
                Roles = roles,
                SentenceKeyCandidate = sentenceKeyCandidate,
                SentenceKeyCandidates = candidateKeys,
                SelfFeelingKeyCandidates = selfFeelingKeyCandidates,
                SelfFeelingVariantKey = variantKey,
                SelfFeelingVariantLog = variantLog,
                SentenceVars = sentenceVars,
                IsGist = isGist,
                HeldBack = isGist && heldBack,
                ClosingKey = closingKey
            };
        }

        public static ComposedRumor Compose(IReadOnlyList<Fact> retained,
                                            PresentationConfig cfg, bool isRetell = false)
        {
            var prefix = isRetell
                ? new RumorPrefix(RumorPrefixKind.Retell, RetellPrefixTextId, RetellPrefixFallback)
                : null;
            return Compose(null, retained, cfg, prefix, null, null);
        }

        public static ComposedRumor Compose(WorldEvent? evt, IReadOnlyList<Fact> retained,
                                            PresentationConfig cfg, bool isRetell = false, string? speakerHeroId = null, string? sourceHeroId = null)
        {
            RumorPrefix? prefix = null;
            if (isRetell)
            {
                bool isParticipant = !string.IsNullOrEmpty(speakerHeroId) && evt?.RoleOf(speakerHeroId!) != null;
                prefix = isParticipant
                    ? new RumorPrefix(RumorPrefixKind.RetellSelf, RetellSelfPrefixTextId, RetellSelfPrefixFallback)
                    : new RumorPrefix(RumorPrefixKind.Retell, RetellPrefixTextId, RetellPrefixFallback);
            }
            return Compose(evt, retained, cfg, prefix, speakerHeroId, sourceHeroId);
        }

        /// <summary>
        /// 從來源紀錄中存下的開頭語、角色、候選鍵、感想等資料重建對話當下組好的那一整句。
        /// 不查事件、特質、好感度，純依存下的欄位與碎片組裝。
        /// </summary>
        public static ComposedRumor Reconstruct(
            IReadOnlyList<Fact> facts,
            PlayerHeardSource source,
            PresentationConfig? cfg,
            string? eventId = null)
        {
            if (facts == null) facts = Array.Empty<Fact>();
            if (source == null) throw new ArgumentNullException(nameof(source));

            var factOrder = cfg?.FactOrder;
            bool placeFirst = cfg?.PlaceFirst ?? true;

            int GetCategoryRank(FactCategory cat)
            {
                if (placeFirst && cat == FactCategory.Where) return -1;
                if (factOrder == null || factOrder.Length == 0) return 0;
                string catName = cat.ToString();
                for (int i = 0; i < factOrder.Length; i++)
                {
                    if (string.Equals(factOrder[i], catName, StringComparison.OrdinalIgnoreCase)) return i;
                }
                return int.MaxValue;
            }

            var sorted = facts.OrderBy(f => GetCategoryRank(f.Category)).ToList();
            var parts = sorted.Select(f => new ComposedFactPart
            {
                TextId = f.TextId ?? string.Empty,
                Fallback = f.Text ?? string.Empty,
                Vars = f.Vars != null ? new Dictionary<string, string>(f.Vars) : new Dictionary<string, string>()
            }).ToList();

            var sentenceVars = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var f in facts)
            {
                if (f.Vars != null)
                {
                    foreach (var kvp in f.Vars)
                    {
                        sentenceVars[kvp.Key] = kvp.Value;
                    }
                }
            }

            VividWorld.Core.Feelings.FeelingDecision? feeling = null;
            if (source.HasFeeling)
            {
                feeling = new VividWorld.Core.Feelings.FeelingDecision
                {
                    SpeakerId = source.HeroId ?? string.Empty,
                    EventId = eventId ?? string.Empty,
                    LineKey = source.FeelingLineKey,
                    AddressKey = source.FeelingAddressKey,
                    FocusHeroId = source.FeelingFocusHeroId
                };
            }

            return new ComposedRumor
            {
                Parts = parts,
                PrefixTextId = source.PrefixTextId,
                PrefixFallback = RumorPrefixSelector.FallbackFor(source.PrefixTextId),
                PrefixVars = source.PrefixVars != null ? new Dictionary<string, string>(source.PrefixVars) : new Dictionary<string, string>(),
                SpeakerHeroId = source.SpeakerHeroId,
                SpeakerRole = source.SpeakerRole,
                SourceHeroId = source.SourceHeroId,
                SourceRole = source.SourceRole,
                Roles = source.Roles != null ? new Dictionary<string, string>(source.Roles, StringComparer.OrdinalIgnoreCase) : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
                SentenceKeyCandidate = source.SentenceKeyCandidates != null && source.SentenceKeyCandidates.Count > 0 ? source.SentenceKeyCandidates[0] : null,
                SentenceKeyCandidates = source.SentenceKeyCandidates != null ? new List<string>(source.SentenceKeyCandidates) : new List<string>(),
                SelfFeelingKeyCandidates = source.SelfFeelingKeyCandidates != null ? new List<string>(source.SelfFeelingKeyCandidates) : new List<string>(),
                SentenceVars = sentenceVars,
                IsGist = source.IsGist,
                HeldBack = source.HeldBack,
                ClosingKey = source.ClosingKey,
                Feeling = feeling
            };
        }
    }
}
