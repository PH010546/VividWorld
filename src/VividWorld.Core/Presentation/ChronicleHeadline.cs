#nullable enable
using System;
using System.Collections.Generic;
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
    /// 紀事每一筆的標題。俘虜類的消息把被抓的人的名字放進標題（「〈名字〉被俘」「〈名字〉脫逃」），
    /// 同一個人的被俘與結局在清單裡一眼對得起來；名字換不出來就退回原本不帶名字的標題，
    /// 絕不讓沒換掉的佔位符上螢幕。其餘種類的標題不帶名字。
    /// </summary>
    public static class ChronicleHeadline
    {
        public const string NamedKeyPrefix = "VividWorld_EventTypeNamed_";
        public const string NameToken = "{PRISONER}";

        private const string PrisonerRole = "prisoner";
        private const string PrisonerVar = "PRISONER";
        private const string HeroVarPrefix = "hero:";

        private static readonly HashSet<string> NamedTypes = new(StringComparer.Ordinal)
        {
            "hero_taken_prisoner",
            "hero_released",
            "hero_escaped_captivity",
            "hero_captured_by_bandits",
            "hero_rescued_from_bandits",
            "hero_escaped_bandits"
        };

        public static IReadOnlyCollection<string> TypesWithNamedHeadline => NamedTypes;

        /// <summary>這種消息的帶名字標題的鍵；不帶名字的種類回傳 null。</summary>
        public static string? NamedTextIdFor(string? eventType)
        {
            return !string.IsNullOrEmpty(eventType) && NamedTypes.Contains(eventType!)
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
        /// 決定一筆紀事的標題。
        /// </summary>
        /// <param name="getTemplate">查字串：第二個參數給 null 時查不到要回傳空字串（用來探測帶名字的標題在目前語言有沒有）。</param>
        /// <param name="resolveName">把 <c>hero:代號</c> 換成名字；這個人查不到時回傳 null 或空字串。</param>
        public static ChronicleHeadlineResult Resolve(
            ChronicleEntry entry,
            Func<string?, string?, string> getTemplate,
            Func<string, string?> resolveName)
        {
            if (entry == null) throw new ArgumentNullException(nameof(entry));
            if (getTemplate == null) throw new ArgumentNullException(nameof(getTemplate));

            string plain = getTemplate(entry.HeadlineTextId, entry.HeadlineFallback) ?? string.Empty;

            if (string.IsNullOrEmpty(entry.NamedHeadlineTextId))
            {
                return new ChronicleHeadlineResult(plain, false, "plain (this kind of news has no named title)");
            }

            if (string.IsNullOrEmpty(entry.HeadlineNameVar))
            {
                return new ChronicleHeadlineResult(plain, false, "plain (fell back: the record does not say who the prisoner is)");
            }

            string? name = resolveName?.Invoke(entry.HeadlineNameVar!);
            if (string.IsNullOrWhiteSpace(name))
            {
                return new ChronicleHeadlineResult(plain, false,
                    $"plain (fell back: no name could be found for {entry.HeadlineNameVar})");
            }

            string named = getTemplate(entry.NamedHeadlineTextId, null) ?? string.Empty;
            if (string.IsNullOrEmpty(named))
            {
                return new ChronicleHeadlineResult(plain, false,
                    $"plain (fell back: no string '{entry.NamedHeadlineTextId}' in the current language)");
            }

            string text = named.Replace(NameToken, name);
            if (text.IndexOf('{') >= 0 || text.IndexOf('}') >= 0)
            {
                return new ChronicleHeadlineResult(plain, false,
                    $"plain (fell back: '{entry.NamedHeadlineTextId}' still had a placeholder left after putting in the name)");
            }

            return new ChronicleHeadlineResult(text, true, $"named ({entry.HeadlineNameVar})");
        }
    }
}
