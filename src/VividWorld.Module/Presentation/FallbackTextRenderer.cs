using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Localization;
using VividWorld.Core.Config;
using VividWorld.Core.Presentation;

namespace VividWorld.Presentation
{
    internal static class FallbackTextRenderer
    {
        internal static RumorRenderResult RenderBoth(ComposedRumor r, PresentationConfig? cfg)
        {
            return RumorTextAssembler.Assemble(
                r,
                cfg,
                ResolveVar,
                LocalizedTemplate,
                Localized,
                // 代換不掉的佔位符照舊留一行 WARN（帳本 L-23）。訊息在 Core 組裝、只在純文字
                // 那一趟發出，所以雙重渲染不會印成兩行。`NoteMissingKey` 不必從這裡傳——
                // 缺鍵的告警本來就在 LocalizedTemplate 裡自己發。
                ModLog.Warn);
        }

        internal static string Render(ComposedRumor r, PresentationConfig? cfg)
        {
            return RenderBoth(r, cfg).DisplayText;
        }

        /// <summary>
        /// 把 <c>TextId</c> 查成**還帶著 <c>{VAR}</c> 佔位符**的樣板文字（帳本 D-49、L-26）。
        ///
        /// 不能用 <c>new TextObject("{=id}fallback").ToString()</c>：那會讓遊戲的文字引擎
        /// 先解一次，沒定義的 <c>{SETTLEMENT}</c> 會**當場被吃成空字串**（就是 L-23 那條路）。
        /// <c>MBTextManager.GetLocalizedText</c> 做的正是「拆 <c>{=id}</c> ＋ 查表、不代換」，
        /// 但它是 internal；<c>LocalizedTextManager.GetTranslatedText</c> 是 public，
        /// 而且實作就是 <c>_gameTextDictionary.TryGetValue(id)</c>
        /// （<c>RawIl.exe</c> 讀過，19 bytes，languageId 根本沒用到）。
        /// 所以照引擎自己的規則走：英文用後備，其他語言查表、查不到再退回後備。
        /// </summary>
        internal static string LocalizedTemplate(string? textId, string? fallback)
        {
            string english = fallback ?? string.Empty;
            if (string.IsNullOrEmpty(textId)) return english;

            try
            {
                string lang = MBTextManager.ActiveTextLanguage ?? string.Empty;
                string englishId = LocalizedTextManager.DefaultEnglishLanguageId ?? "English";
                if (string.IsNullOrEmpty(lang) || string.Equals(lang, englishId, StringComparison.OrdinalIgnoreCase))
                {
                    // 英文環境下引擎根本不查表（帳本 D-07／規格 §9.5.3），後備就是英文版本本身。
                    return english;
                }

                string translated = LocalizedTextManager.GetTranslatedText(lang, textId!);
                if (string.IsNullOrEmpty(translated))
                {
                    NoteMissingKey(textId!, lang);
                    return english;
                }
                return translated;
            }
            catch (Exception ex)
            {
                ModLog.Error($"Error localizing fact text id '{textId}'", ex);
                return english;
            }
        }

        /// <summary>同一個鍵只吵一次：缺鍵是內容問題，不是每則傳聞都要重講一遍的事。</summary>
        private static readonly HashSet<string> MissingKeysReported = new HashSet<string>(StringComparer.Ordinal);

        private static void NoteMissingKey(string textId, string language)
        {
            if (!MissingKeysReported.Add(textId)) return;
            ModLog.Info(
                $"Rumor text: string table for '{language}' has no key '{textId}' - falling back to the English literal " +
                "stored on the fact. Add the key to module/ModuleData/Languages/<lang>/std_module_strings_xml.xml.");
        }

        private static string ResolveVar(string val, bool useLinks)
        {
            if (string.IsNullOrEmpty(val)) return string.Empty;
            int colon = val.IndexOf(':');
            if (colon < 0) return val;

            string prefix = val.Substring(0, colon);
            string payload = val.Substring(colon + 1);

            // 規格 §9.5.2：英雄已死、聚落不存在、id 打錯 ⇒ 渲染成 someone／ somewhere。
            // 回 payload 等於把 `lord_5_11`、`town_B2` 這種內部 id 直接給玩家看。
            // 規格 §9.3.1（M6c）：開啟超連結且查得到物件時使用 EncyclopediaLinkWithName；
            // 關閉超連結或純文字診斷輸出時使用 .Name；查不到物件時退回純名字／someone／somewhere，絕不產生半截錨點。
            switch (prefix.ToLowerInvariant())
            {
                case "hero":
                {
                    var hero = Hero.Find(payload);
                    if (hero == null) return UnknownSubject();
                    return useLinks
                        ? hero.EncyclopediaLinkWithName.ToString()
                        : (hero.Name?.ToString() ?? UnknownSubject());
                }
                case "settlement":
                {
                    var settlement = Settlement.Find(payload);
                    if (settlement == null) return UnknownPlace();
                    return useLinks
                        ? settlement.EncyclopediaLinkWithName.ToString()
                        : (settlement.Name?.ToString() ?? UnknownPlace());
                }
                case "faction":
                    var clan = Clan.FindFirst(c => c.StringId == payload);
                    if (clan != null) return clan.Name?.ToString() ?? UnknownSubject();
                    var kingdom = Kingdom.All?.FirstOrDefault(k => k.StringId == payload);
                    if (kingdom != null) return kingdom.Name?.ToString() ?? UnknownSubject();
                    return UnknownSubject();
                case "num":
                case "text":
                    return payload;
                case "key":
                    // M6b：字串表的鍵。查得到就用表裡的，查不到原樣退回。
                    return Localized(payload, payload);
                default:
                    return payload;
            }
        }

        private static string UnknownSubject()
        {
            return Localized("VividWorld_UnknownSubject", "someone");
        }

        private static string UnknownPlace()
        {
            return Localized("VividWorld_UnknownPlace", "somewhere");
        }

        /// <summary>
        /// 字串表查得到就用表裡的，查不到就用英文後備。
        /// 英文環境下引擎根本不查表（帳本：規格 §9.5.3 的 GetLocalizedText 方法體），
        /// 所以後備就是英文版本本身。
        /// **只用在不含佔位符的字串**（分隔符、句尾、someone／somewhere、重述前綴）：
        /// 帶佔位符的樣板文字要走 <see cref="LocalizedTemplate"/>，不能讓引擎先解一次。
        /// </summary>
        private static string Localized(string? key, string english)
        {
            if (string.IsNullOrEmpty(key)) return english;
            try
            {
                return new TextObject("{=" + key + "}" + english).ToString();
            }
            catch
            {
                return english;
            }
        }
    }
}
