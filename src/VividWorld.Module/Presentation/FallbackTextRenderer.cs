using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Localization;
using VividWorld.Core.Config;
using VividWorld.Core.Feelings;
using VividWorld.Core.Presentation;
using VividWorld.Core.Rumors;

namespace VividWorld.Presentation
{
    internal static class FallbackTextRenderer
    {
        internal static IHeroTraitLookup? TraitLookup { get; set; }

        internal static RumorRenderResult RenderBoth(ComposedRumor r, PresentationConfig? cfg, Func<string, bool?>? isFemale = null)
        {
            isFemale ??= (id => TraitLookup?.Of(id)?.IsFemale ?? Hero.Find(id)?.IsFemale);

            return RumorTextAssembler.Assemble(
                r,
                cfg,
                ResolveVar,
                LocalizedTemplate,
                Localized,
                // 代換不掉的佔位符照舊留一行 WARN（帳本 L-23）。訊息在 Core 組裝、只在純文字
                // 那一趟發出，所以雙重渲染不會印成兩行。`NoteMissingKey` 不必從這裡傳——
                // 缺鍵的告警本來就在 LocalizedTemplate 裡自己發。
                ModLog.Warn,
                isFemale,
                ModLog.Info,
                Hero.MainHero?.StringId);
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
                    // 英文環境下引擎不查表；但若 fallback 為 null（探測可選鍵），改由英文字串表查詢
                    if (fallback == null)
                    {
                        var enVal = EnglishStringTableStore.Instance.Get(textId!);
                        return enVal ?? string.Empty;
                    }
                    return english;
                }

                string translated = LocalizedTextManager.GetTranslatedText(lang, textId!);
                if (string.IsNullOrEmpty(translated))
                {
                    if (fallback != null)
                    {
                        NoteMissingKey(textId!, lang);
                    }
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

        internal static string ResolveVar(string val, bool useLinks)
            => ResolveVar(val, useLinks, Localized);

        internal static string ResolveVar(string val, bool useLinks, Func<string?, string, string>? localizer)
        {
            localizer ??= Localized;
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
                    if (hero == null) return UnknownSubject(localizer);
                    return useLinks
                        ? hero.EncyclopediaLinkWithName.ToString()
                        : (hero.Name?.ToString() ?? UnknownSubject(localizer));
                }
                case "settlement":
                {
                    var settlement = Settlement.Find(payload);
                    if (settlement == null) return UnknownPlace(localizer);
                    return useLinks
                        ? settlement.EncyclopediaLinkWithName.ToString()
                        : (settlement.Name?.ToString() ?? UnknownPlace(localizer));
                }
                case "faction":
                    var clan = Clan.FindFirst(c => c.StringId == payload);
                    if (clan != null) return clan.Name?.ToString() ?? UnknownSubject(localizer);
                    var kingdom = Kingdom.All?.FirstOrDefault(k => k.StringId == payload);
                    if (kingdom != null) return kingdom.Name?.ToString() ?? UnknownSubject(localizer);
                    return UnknownSubject(localizer);
                case "num":
                case "text":
                    return payload;
                case "key":
                    // M6b：字串表的鍵。查得到就用表裡的，查不到原樣退回。
                    return localizer(payload, payload);
                default:
                    return payload;
            }
        }

        private static string UnknownSubject(Func<string?, string, string>? localizer = null)
        {
            return (localizer ?? Localized)("VividWorld_UnknownSubject", "someone");
        }

        private static string UnknownPlace(Func<string?, string, string>? localizer = null)
        {
            return (localizer ?? Localized)("VividWorld_UnknownPlace", "somewhere");
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

        /// <summary>把感想判定渲染成玩家會聽到的那一句；字串表沒有這一句時回傳 null。
        /// 預設純文字（開發者工具用）；紀事要讓稱呼裡的人名可以點時傳 useLinks。</summary>
        internal static string? RenderFeelingLine(FeelingDecision decision, bool useLinks = false, Func<string, bool?>? isFemale = null, string? speakerHeroId = null)
        {
            isFemale ??= (id => TraitLookup?.Of(id)?.IsFemale ?? Hero.Find(id)?.IsFemale);
            return RumorTextAssembler.RenderFeeling(
                decision,
                useLinks,
                ResolveVar,
                LocalizedTemplate,
                Localized,
                Localized("VividWorld_SentenceEnd", "."),
                out _,
                isFemale,
                speakerHeroId,
                null,
                Hero.MainHero?.StringId);
        }

        /// <summary>固定句子的警告每個字串鍵每個 session 只印一次：這些條件每次重畫對話選單都會跑。</summary>
        private static readonly HashSet<string> FixedLineWarnedKeys = new HashSet<string>(StringComparer.Ordinal);

        private static void WarnFixedLineOnce(string key, string message)
        {
            lock (FixedLineWarnedKeys)
            {
                if (!FixedLineWarnedKeys.Add(key)) return;
            }
            ModLog.Warn(message);
        }

        /// <summary>
        /// 取出對話選單固定句子的字（還沒解析的版本）並套依性別選字的記號。
        /// 取字或套記號丟出例外、或結果是空字串時，回傳英文原句並留一行警告，不會回傳空白。
        /// </summary>
        internal static string RenderFixedLine(string key, string english, string? speakerHeroId, string? listenerHeroId)
        {
            try
            {
                Func<string, bool?> isFemale = id => TraitLookup?.Of(id)?.IsFemale ?? Hero.Find(id)?.IsFemale;
                string raw = LocalizedTemplate(key, english);
                string text = RumorTextAssembler.ApplyFixedLineGenderSelect(
                    raw,
                    key,
                    speakerHeroId,
                    listenerHeroId,
                    isFemale,
                    msg => WarnFixedLineOnce(key, msg));
                if (string.IsNullOrWhiteSpace(text))
                {
                    WarnFixedLineOnce(key, $"Fixed dialogue line '{key}' rendered to an empty string - using the English text instead.");
                    return english;
                }
                return text;
            }
            catch (Exception ex)
            {
                WarnFixedLineOnce(key, $"Fixed dialogue line '{key}' could not be rendered ({ex.GetType().Name}: {ex.Message}) - using the English text instead.");
                return english;
            }
        }

        /// <summary>
        /// 把固定句子渲染好、設成對話行讀的文字變數。整段包在自己的 try／catch 裡，
        /// 失敗只會讓變數退回英文原句，不會影響呼叫它的條件的回傳值。
        /// </summary>
        internal static void SetFixedLineVariable(string variable, string key, string english, string? speakerHeroId, string? listenerHeroId)
        {
            string text = english;
            try
            {
                text = RenderFixedLine(key, english, speakerHeroId, listenerHeroId);
            }
            catch
            {
                text = english;
            }

            try
            {
                MBTextManager.SetTextVariable(variable, text, false);
            }
            catch (Exception ex)
            {
                WarnFixedLineOnce(key, $"Could not set text variable '{variable}' for fixed dialogue line '{key}' ({ex.GetType().Name}: {ex.Message}).");
            }
        }

        internal static RumorRenderResult RenderProbeResponse(
            string templateKey,
            IReadOnlyDictionary<string, string>? vars,
            string? addressKey,
            string? addressHeroId,
            string? speakerHeroId,
            string? listenerHeroId,
            bool enableLinks,
            Func<string, bool?>? isFemale = null,
            string? englishFallback = null)
        {
            isFemale ??= (id => TraitLookup?.Of(id)?.IsFemale ?? Hero.Find(id)?.IsFemale);

            // 英文預設取英文字串表那一句；字串表裡沒有這個鍵時才是空的（日誌會警告）
            englishFallback ??= EnglishStringTableStore.Instance.Get(templateKey) ?? string.Empty;
            // 查字串用的後備也一樣：鍵沒有譯文、也沒有呼叫端給的英文時，改取英文字串表
            string? Localizer(string? id, string fb)
                => LocalizedTemplate(id, string.IsNullOrEmpty(fb) && !string.IsNullOrEmpty(id)
                    ? (EnglishStringTableStore.Instance.Get(id!) ?? string.Empty)
                    : fb);

            return RumorTextAssembler.AssembleProbeResponse(
                templateKey,
                vars,
                addressKey,
                addressHeroId,
                speakerHeroId,
                listenerHeroId,
                enableLinks,
                ResolveVar,
                (id, fb) => Localizer(id, fb) ?? string.Empty,
                isFemale,
                ModLog.Warn,
                englishFallback);
        }

        internal static string RenderRecallMemory(ComposedRumor rumor, string? language, PresentationConfig? cfg, EnglishStringTable? stringTable = null, Func<string, bool?>? isFemale = null)
        {
            if (rumor == null) return string.Empty;

            bool isEnglish = string.IsNullOrEmpty(language) || string.Equals(language, "english", StringComparison.OrdinalIgnoreCase);
            var table = stringTable ?? EnglishStringTableStore.Instance;

            Func<string?, string?, string> getTemplate = isEnglish
                ? (id, fallback) => table.Lookup(id, fallback)
                : LocalizedTemplate;

            Func<string?, string, string> getLocalized = isEnglish
                ? (id, eng) => table.GetWithFallback(id, eng)
                : Localized;

            isFemale ??= (id => TraitLookup?.Of(id)?.IsFemale ?? Hero.Find(id)?.IsFemale);

            var result = RumorTextAssembler.Assemble(
                rumor,
                cfg,
                (val, links) => ResolveVar(val, links, getLocalized),
                getTemplate,
                getLocalized,
                null,
                isFemale,
                ModLog.Info,
                Hero.MainHero?.StringId);

            return result.PlainText;
        }
    }
}
