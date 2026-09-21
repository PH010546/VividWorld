#nullable enable
using System;
using System.Collections.Generic;
using VividWorld.Core.Config;

namespace VividWorld.Core.Presentation
{
    /// <summary>
    /// 傳聞文字組裝核心（規格 §9.3、§9.3.1，M6c）。
    /// 負責將 ComposedRumor 依 PresentationConfig、在地化模板與變數解析器，
    /// 同時產出「顯示字串」與「純文字診斷字串」兩個版本（非正規表示式剝除標記）。
    /// </summary>
    public static class RumorTextAssembler
    {
        public static RumorRenderResult Assemble(
            ComposedRumor? r,
            PresentationConfig? cfg,
            Func<string, bool, string> resolveVar,
            Func<string?, string?, string>? getTemplate = null,
            Func<string?, string, string>? getLocalized = null,
            Action<string>? onWarning = null)
        {
            if (r == null || r.Parts == null || r.Parts.Count == 0)
            {
                return new RumorRenderResult(string.Empty, string.Empty);
            }

            if (resolveVar == null) throw new ArgumentNullException(nameof(resolveVar));

            bool enableLinks = cfg?.EncyclopediaLinksEnabled ?? true;

            string separator = !string.IsNullOrEmpty(cfg?.FactSeparator)
                ? cfg!.FactSeparator
                : (getLocalized != null ? getLocalized("VividWorld_FactSeparator", ", ") : ", ");

            string sentenceEnd = !string.IsNullOrEmpty(cfg?.SentenceEnd)
                ? cfg!.SentenceEnd
                : (getLocalized != null ? getLocalized("VividWorld_SentenceEnd", ".") : ".");

            // 兩趟各渲染一次（顯示用帶連結、診斷用純文字），**但診斷只在純文字那一趟發**——
            // 否則同一個代換失敗會在 log 裡印兩行（帳本 L-23 那條 WARN 是實機第 11 項的判準，
            // 不能因為改成雙重渲染就消失、也不能變成兩份）。
            string displayBody = RenderBody(r, separator, sentenceEnd, enableLinks, resolveVar, getTemplate, getLocalized, null);
            string plainBody = RenderBody(r, separator, sentenceEnd, false, resolveVar, getTemplate, getLocalized, onWarning);

            string prefix = string.Empty;
            if (!string.IsNullOrEmpty(r.PrefixTextId) || !string.IsNullOrEmpty(r.PrefixFallback))
            {
                prefix = getLocalized != null
                    ? getLocalized(r.PrefixTextId, r.PrefixFallback ?? string.Empty)
                    : (r.PrefixFallback ?? string.Empty);
            }

            string displayFull = ApplyPrefix(prefix, displayBody);
            string plainFull = ApplyPrefix(prefix, plainBody);

            return new RumorRenderResult(displayFull, plainFull);
        }

        private static string RenderBody(
            ComposedRumor r,
            string separator,
            string sentenceEnd,
            bool useLinks,
            Func<string, bool, string> resolveVar,
            Func<string?, string?, string>? getTemplate,
            Func<string?, string, string>? getLocalized,
            Action<string>? onWarning)
        {
            var parts = new List<string>();
            foreach (var part in r.Parts)
            {
                if (part == null) continue;

                string template = getTemplate != null
                    ? getTemplate(part.TextId, part.Fallback)
                    : (part.Fallback ?? string.Empty);

                string rendered = FactTextSubstitution.Apply(
                    template,
                    part.Vars,
                    val => resolveVar(val, useLinks));

                var unresolved = FactTextSubstitution.UnresolvedPlaceholders(rendered);
                if (unresolved.Count > 0)
                {
                    string unknown = getLocalized != null
                        ? getLocalized("VividWorld_UnknownSubject", "someone")
                        : "someone";

                    onWarning?.Invoke(FormatUnresolvedWarning(part, unresolved, unknown));

                    foreach (string name in unresolved)
                    {
                        rendered = rendered.Replace("{" + name + "}", unknown);
                    }
                }

                if (!string.IsNullOrWhiteSpace(rendered))
                {
                    parts.Add(rendered.Trim());
                }
            }

            if (parts.Count == 0) return string.Empty;

            string body = string.Join(separator, parts);
            if (!string.IsNullOrEmpty(sentenceEnd) && !body.EndsWith(sentenceEnd, StringComparison.Ordinal))
            {
                body += sentenceEnd;
            }
            return body;
        }

        /// <summary>
        /// 代換不掉的佔位符要留下**看得出為什麼**的一行：哪些變數沒解析出來、
        /// 該碎片實際帶了哪些 Vars 鍵、原始樣板是什麼（帳本 L-23）。
        /// 訊息在 Core 組裝，測試才釘得住；Module 只負責送進 ModLog。
        /// </summary>
        internal static string FormatUnresolvedWarning(
            ComposedFactPart part, IReadOnlyList<string> unresolved, string renderedAs)
        {
            string varsPresent = (part.Vars == null || part.Vars.Count == 0)
                ? "(none)"
                : string.Join(", ", part.Vars.Keys);

            return $"Rumor text: placeholder(s) {{{string.Join(", ", unresolved)}}} in fact part " +
                   $"'{part.TextId}' had no value in Vars (vars present: {varsPresent}) " +
                   $"- rendered as \"{renderedAs}\". Raw text: \"{part.Fallback}\"";
        }

        /// <summary>
        /// 前綴語（「事實上，當時我也在場——」）與本文的接縫。
        ///
        /// **那個空白是給拉丁文字斷詞用的，中日韓不需要**：繁中會變成
        /// 「當時我也在場—— 索埃拉斯與曼格斯…」（實機 `log-20260910-ms1.txt`）。
        /// 規則是**接縫兩邊都不是中日韓字元時才加空白**，任一側是中日韓就直接接上。
        /// 前綴語自己已經以空白結尾時一律照用（英文後備 "I was there, in fact— " 就是這種）。
        ///
        /// 注意繁中前綴語結尾的破折號是 U+2014，**不在**中日韓區段：所以「中文前綴＋英文本文」
        /// （只翻一半的語言）仍然會留空白，那正是拉丁字需要的。決定權實際上落在本文的第一個字。
        /// </summary>
        private static string ApplyPrefix(string prefix, string body)
        {
            if (string.IsNullOrEmpty(body)) return string.Empty;
            if (string.IsNullOrEmpty(prefix)) return body;
            if (prefix.EndsWith(" ", StringComparison.Ordinal)) return prefix + body;

            bool needsSpace = !IsCjk(prefix[prefix.Length - 1]) && !IsCjk(body[0]);
            return needsSpace ? prefix + " " + body : prefix + body;
        }

        /// <summary>
        /// 中日韓字元與全形標點：中日韓符號與標點（2E80–303F，含全形破折號與句號）、
        /// 假名（3040–30FF）、統一表意文字含擴充 A（3400–4DBF、4E00–9FFF）、
        /// 諺文（AC00–D7AF）、全形與半形變體（FF00–FFEF）。
        /// U+20000 以上的罕用字以代理對表示，高位代理落在 D800–DBFF，不在上面任何一段裡
        /// ⇒ 會被判成「不是中日韓」而多一個空白。那是一個字一個空格的等級，人名不會用到那個區段。
        /// </summary>
        private static bool IsCjk(char c)
        {
            return (c >= '⺀' && c <= '〿')
                || (c >= '぀' && c <= 'ヿ')
                || (c >= '㐀' && c <= '䶿')
                || (c >= '一' && c <= '鿿')
                || (c >= '가' && c <= '힯')
                || (c >= '＀' && c <= '￯');
        }
    }
}
