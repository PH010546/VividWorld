#nullable enable
using System;
using System.Collections.Generic;
using VividWorld.Core.Events;
using VividWorld.Core.Persistence;

namespace VividWorld.Core.Presentation
{
    /// <summary>一筆紀事的標題最後用了哪一句，以及為什麼（日誌用）。</summary>
    public sealed class ChronicleHeadlineResult
    {
        public string Text { get; }
        /// <summary>true＝帶名字的標題；false＝退回不帶名字的標題。</summary>
        public bool UsedNamed { get; }
        /// <summary>一小段英文說明：用了帶名字的版本，或為什麼退回。</summary>
        public string Note { get; }

        public ChronicleHeadlineResult(string text, bool usedNamed, string note)
        {
            Text = text;
            UsedNamed = usedNamed;
            Note = note;
        }
    }

    /// <summary>
    /// 紀事每一筆與每個區塊的標題。
    /// 帶人名的標題將事件角色名稱換成當事人的名字；名字換不出來或該語言缺少字串時退回原本不帶名字的標題。
    /// 回應類（talk_*）不帶人名標題。
    /// </summary>
    public static class ChronicleHeadline
    {
        public const string NamedKeyPrefix = "VividWorld_EventTypeNamed_";
        public const string NameToken = "{PRISONER}";

        private const string PrisonerRole = "prisoner";
        private const string PrisonerVar = "PRISONER";
        private const string HeroVarPrefix = "hero:";

        private static readonly HashSet<string> CaptivityNamedTypes = new(StringComparer.Ordinal)
        {
            "hero_taken_prisoner",
            "hero_released",
            "hero_escaped_captivity",
            "hero_captured_by_bandits",
            "hero_rescued_from_bandits",
            "hero_escaped_bandits"
        };

        private static readonly HashSet<string> AllNamedTypes = new(StringComparer.Ordinal)
        {
            // 俘虜類 6 種
            "hero_taken_prisoner",
            "hero_released",
            "hero_escaped_captivity",
            "hero_captured_by_bandits",
            "hero_rescued_from_bandits",
            "hero_escaped_bandits",

            // 第十一批 30 種
            "hero_murdered",
            "hero_executed",
            "hero_died_in_battle",
            "hero_died_naturally",
            "hero_died_of_old_age",
            "hero_died_in_labor",
            "heroes_married",
            "child_born",
            "seat_dispute_demanded",
            "seat_dispute_endured",
            "seat_dispute_yielded",
            "seat_dispute_walked_out",
            "advice_given_freely",
            "advice_brushed_off",
            "advice_mocked",
            "tavern_boast_told",
            "tavern_confidence",
            "tavern_sour_words",
            "tavern_good_word",
            "victory_credit_claimed",
            "victory_credit_deferred",
            "victory_credit_belittled",
            "victory_credit_judged",
            "brawl_man_handed_over",
            "brawl_shielded_own",
            "brawl_counter_accused",
            "brawl_hushed_up",
            "wager_struck",
            "wager_refused",
            "wager_secret_stake",

            // 5 種 conduct
            "conduct_spoke_against_ruler",
            "conduct_mistreated_prisoner",
            "conduct_refused_aid",
            "conduct_rash_capture",
            "conduct_poisoned"
        };

        /// <summary>保持與舊測試相容的俘虜類六種清單。</summary>
        public static IReadOnlyCollection<string> TypesWithNamedHeadline => CaptivityNamedTypes;

        /// <summary>所有具備帶人名標題的消息種類。</summary>
        public static IReadOnlyCollection<string> AllTypesWithNamedHeadline => AllNamedTypes;

        /// <summary>這種消息的帶名字標題的鍵；不帶名字的種類（如 talk_* 或未收錄種類）回傳 null。</summary>
        public static string? NamedTextIdFor(string? eventType)
        {
            if (string.IsNullOrEmpty(eventType) || eventType!.StartsWith("talk_", StringComparison.Ordinal))
            {
                return null;
            }
            return AllNamedTypes.Contains(eventType)
                ? NamedKeyPrefix + eventType
                : null;
        }

        /// <summary>
        /// 從玩家的紀錄找出被抓的人：先看紀錄上的角色，沒有再看碎片的代換值。
        /// 回傳 <c>hero:代號</c>；紀錄裡找不到就回傳 null。
        /// </summary>
        public static string? PrisonerVarOf(PlayerHeardEntry? entry)
        {
            if (entry == null) return null;

            if (entry.Participants != null)
            {
                foreach (var kvp in entry.Participants)
                {
                    if (string.Equals(kvp.Key, PrisonerRole, StringComparison.OrdinalIgnoreCase) && !string.IsNullOrEmpty(kvp.Value))
                    {
                        return HeroVarPrefix + kvp.Value;
                    }
                }
            }

            if (entry.Facts != null)
            {
                foreach (var fact in entry.Facts)
                {
                    if (fact?.Vars == null) continue;
                    foreach (var kvp in fact.Vars)
                    {
                        if (string.Equals(kvp.Key, PrisonerVar, StringComparison.OrdinalIgnoreCase)
                            && !string.IsNullOrEmpty(kvp.Value)
                            && kvp.Value.StartsWith(HeroVarPrefix, StringComparison.OrdinalIgnoreCase)
                            && kvp.Value.Length > HeroVarPrefix.Length)
                        {
                            return kvp.Value;
                        }
                    }
                }
            }

            return null;
        }

        /// <summary>
        /// 決定一筆紀事或區塊的標題。
        /// </summary>
        public static ChronicleHeadlineResult Resolve(
            ChronicleEntry entry,
            Func<string?, string?, string> getTemplate,
            Func<string, string?> resolveName)
        {
            if (entry == null) throw new ArgumentNullException(nameof(entry));
            if (getTemplate == null) throw new ArgumentNullException(nameof(getTemplate));

            var participants = entry.RootParticipants != null && entry.RootParticipants.Count > 0
                ? entry.RootParticipants
                : entry.Participants;

            return Resolve(
                entry.EventType,
                entry.HeadlineTextId,
                entry.NamedHeadlineTextId,
                entry.HeadlineFallback,
                participants,
                entry.HeadlineNameVar,
                null,
                getTemplate,
                resolveName);
        }

        /// <summary>
        /// 決定一件事的小標題。
        /// </summary>
        public static ChronicleHeadlineResult Resolve(
            ChronicleMatter matter,
            Func<string?, string?, string> getTemplate,
            Func<string, string?> resolveName)
        {
            if (matter == null) throw new ArgumentNullException(nameof(matter));
            if (getTemplate == null) throw new ArgumentNullException(nameof(getTemplate));

            return Resolve(
                matter.EventType,
                matter.HeadlineTextId,
                matter.NamedHeadlineTextId,
                matter.HeadlineFallback,
                matter.Participants,
                null,
                null,
                getTemplate,
                resolveName);
        }

        /// <summary>
        /// 泛用的標題判定方法（支援任意角色佔位符替換與退回機制）。
        /// </summary>
        public static ChronicleHeadlineResult Resolve(
            string? eventType,
            string? headlineTextId,
            string? namedHeadlineTextId,
            string? headlineFallback,
            IReadOnlyDictionary<string, string>? participants,
            string? explicitNameVar,
            IReadOnlyList<Fact>? facts,
            Func<string?, string?, string> getTemplate,
            Func<string, string?> resolveName)
        {
            string plainKey = !string.IsNullOrEmpty(headlineTextId) ? headlineTextId! : ("VividWorld_EventType_" + (eventType ?? string.Empty));
            string plainFallback = !string.IsNullOrEmpty(headlineFallback) ? headlineFallback! : (eventType ?? string.Empty);
            string plain = getTemplate(plainKey, plainFallback) ?? plainFallback;

            if (string.IsNullOrEmpty(namedHeadlineTextId))
            {
                return new ChronicleHeadlineResult(plain, false, "plain (this kind of news has no named title)");
            }

            string named = getTemplate(namedHeadlineTextId, null) ?? string.Empty;
            if (string.IsNullOrEmpty(named))
            {
                return new ChronicleHeadlineResult(plain, false,
                    $"plain (fell back: no string '{namedHeadlineTextId}' in the current language)");
            }

            // 解析模板中的所有佔位符 {ROLE}
            var tokens = new List<string>();
            int idx = 0;
            while ((idx = named.IndexOf('{', idx)) >= 0)
            {
                int end = named.IndexOf('}', idx);
                if (end < 0) break;
                string token = named.Substring(idx + 1, end - idx - 1);
                if (!tokens.Contains(token)) tokens.Add(token);
                idx = end + 1;
            }

            if (tokens.Count == 0)
            {
                return new ChronicleHeadlineResult(named, true, $"named ({namedHeadlineTextId})");
            }

            string resultText = named;
            var replacedVars = new List<string>();
            bool isCaptivity = CaptivityNamedTypes.Contains(eventType ?? string.Empty);

            foreach (var token in tokens)
            {
                if (isCaptivity && !string.Equals(token, PrisonerVar, StringComparison.OrdinalIgnoreCase))
                {
                    continue; // 俘虜類標題只認 PRISONER；出現其他未支援佔位符（如 CAPTOR）留給後續檢查判定退回
                }
                string? heroId = null;

                if (participants != null)
                {
                    foreach (var kvp in participants)
                    {
                        if (string.Equals(kvp.Key, token, StringComparison.OrdinalIgnoreCase) && !string.IsNullOrEmpty(kvp.Value))
                        {
                            heroId = kvp.Value;
                            break;
                        }
                    }
                }

                if (string.IsNullOrEmpty(heroId) && facts != null)
                {
                    foreach (var fact in facts)
                    {
                        if (fact?.Vars == null) continue;
                        foreach (var kvp in fact.Vars)
                        {
                            if (string.Equals(kvp.Key, token, StringComparison.OrdinalIgnoreCase) && !string.IsNullOrEmpty(kvp.Value))
                            {
                                heroId = kvp.Value;
                                break;
                            }
                        }
                        if (!string.IsNullOrEmpty(heroId)) break;
                    }
                }

                if (string.IsNullOrEmpty(heroId) && string.Equals(token, PrisonerVar, StringComparison.OrdinalIgnoreCase) && !string.IsNullOrEmpty(explicitNameVar))
                {
                    heroId = explicitNameVar;
                }

                if (string.IsNullOrEmpty(heroId))
                {
                    return new ChronicleHeadlineResult(plain, false,
                        $"plain (fell back: the record does not say who the {token.ToLowerInvariant()} is)");
                }

                string varRef = heroId!.StartsWith(HeroVarPrefix, StringComparison.OrdinalIgnoreCase)
                    ? heroId!
                    : (HeroVarPrefix + heroId!);

                string? name = resolveName?.Invoke(varRef);
                if (string.IsNullOrWhiteSpace(name))
                {
                    return new ChronicleHeadlineResult(plain, false,
                        $"plain (fell back: no name could be found for {varRef})");
                }

                resultText = resultText.Replace("{" + token + "}", name);
                replacedVars.Add(varRef);
            }

            if (resultText.IndexOf('{') >= 0 || resultText.IndexOf('}') >= 0)
            {
                return new ChronicleHeadlineResult(plain, false,
                    $"plain (fell back: '{namedHeadlineTextId}' still had a placeholder left after putting in the name)");
            }

            return new ChronicleHeadlineResult(resultText, true, $"named ({string.Join(", ", replacedVars)})");
        }
    }
}
