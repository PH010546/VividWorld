#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
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
            Action<string>? onWarning = null,
            Func<string, bool?>? isFemale = null)
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
            string displayBody = RenderBody(r, separator, sentenceEnd, enableLinks, resolveVar, getTemplate, getLocalized, null, isFemale);
            string plainBody = RenderBody(r, separator, sentenceEnd, false, resolveVar, getTemplate, getLocalized, onWarning, isFemale);

            string displayPrefix = RenderPrefix(r, enableLinks, resolveVar, getTemplate, getLocalized, null);
            string plainPrefix = RenderPrefix(r, false, resolveVar, getTemplate, getLocalized, onWarning);

            string displayFull = ApplyPrefix(displayPrefix, displayBody);
            string plainFull = ApplyPrefix(plainPrefix, plainBody);

            return new RumorRenderResult(displayFull, plainFull);
        }

        private static string RenderPrefix(
            ComposedRumor r,
            bool useLinks,
            Func<string, bool, string> resolveVar,
            Func<string?, string?, string>? getTemplate,
            Func<string?, string, string>? getLocalized,
            Action<string>? onWarning)
        {
            if (string.IsNullOrEmpty(r.PrefixTextId) && string.IsNullOrEmpty(r.PrefixFallback))
            {
                return string.Empty;
            }

            string template = getTemplate != null
                ? getTemplate(r.PrefixTextId, r.PrefixFallback)
                : (getLocalized != null
                    ? getLocalized(r.PrefixTextId, r.PrefixFallback ?? string.Empty)
                    : (r.PrefixFallback ?? string.Empty));

            if (string.IsNullOrEmpty(template)) return string.Empty;

            var vars = r.PrefixVars ?? new Dictionary<string, string>();
            string rendered = FactTextSubstitution.Apply(
                template,
                vars,
                val => resolveVar(val, useLinks));

            var unresolved = FactTextSubstitution.UnresolvedPlaceholders(rendered);
            if (unresolved.Count > 0)
            {
                string unknown = getLocalized != null
                    ? getLocalized("VividWorld_UnknownSubject", "someone")
                    : "someone";

                onWarning?.Invoke(FormatUnresolvedPrefixWarning(r.PrefixTextId, unresolved, unknown, r.PrefixFallback, vars));

                foreach (string name in unresolved)
                {
                    rendered = rendered.Replace("{" + name + "}", unknown);
                }
            }

            return rendered;
        }

        internal static string FormatUnresolvedPrefixWarning(
            string? prefixTextId, IReadOnlyList<string> unresolved, string renderedAs, string? fallback, IReadOnlyDictionary<string, string> vars)
        {
            string varsPresent = (vars == null || vars.Count == 0)
                ? "(none)"
                : string.Join(", ", vars.Keys);

            return $"Rumor text: placeholder(s) {{{string.Join(", ", unresolved)}}} in prefix " +
                   $"'{prefixTextId}' had no value in Vars (vars present: {varsPresent}) " +
                   $"- rendered as \"{renderedAs}\". Raw text: \"{fallback}\"";
        }

        private static string RenderBody(
            ComposedRumor r,
            string separator,
            string sentenceEnd,
            bool useLinks,
            Func<string, bool, string> resolveVar,
            Func<string?, string?, string>? getTemplate,
            Func<string?, string, string>? getLocalized,
            Action<string>? onWarning,
            Func<string, bool?>? isFemale)
        {
            string selfSubj = getLocalized != null ? getLocalized("VividWorld_Self_Subject", "I") : "I";
            string selfObj = getLocalized != null ? getLocalized("VividWorld_Self_Object", "me") : "me";
            string selfPoss = getLocalized != null ? getLocalized("VividWorld_Self_Possessive", "my") : "my";

            var parts = new List<string>();
            foreach (var part in r.Parts)
            {
                if (part == null) continue;

                string template;
                bool isSpeakerParticipant = !string.IsNullOrEmpty(r.SpeakerRole) ||
                    (!string.IsNullOrEmpty(r.SpeakerHeroId) && part.Vars != null && part.Vars.Values.Any(v => v == "hero:" + r.SpeakerHeroId));

                if (isSpeakerParticipant)
                {
                    string roleUpper = !string.IsNullOrEmpty(r.SpeakerRole)
                        ? r.SpeakerRole!.ToUpperInvariant()
                        : (part.Vars?.FirstOrDefault(kv => kv.Value == "hero:" + r.SpeakerHeroId).Key?.ToUpperInvariant() ?? string.Empty);

                    // Step 1: 每一塊碎片先找 <textId>_Self_<角色大寫>
                    string? selfTemplate = null;
                    if (!string.IsNullOrEmpty(part.TextId) && !string.IsNullOrEmpty(roleUpper))
                    {
                        if (getTemplate != null)
                        {
                            selfTemplate = getTemplate(part.TextId + "_Self_" + roleUpper, null);
                        }
                        else if (getLocalized != null)
                        {
                            string loc = getLocalized(part.TextId + "_Self_" + roleUpper, string.Empty);
                            if (!string.IsNullOrEmpty(loc)) selfTemplate = loc;
                        }
                    }

                    if (!string.IsNullOrEmpty(selfTemplate))
                    {
                        template = selfTemplate!;
                    }
                    else
                    {
                        // Step 2: 否則在取到的樣板上替換說話者佔位符 {P}：
                        // {P}'s → 所有格（英 my）；樣板開頭的 {P} → 主格（英 I）；其餘 → 受格（英 me）
                        string baseTemplate = getTemplate != null
                            ? getTemplate(part.TextId, part.Fallback)
                            : (part.Fallback ?? string.Empty);

                        template = ApplyFirstPersonPronouns(baseTemplate, roleUpper, selfSubj, selfObj, selfPoss);
                    }
                }
                else
                {
                    template = getTemplate != null
                        ? getTemplate(part.TextId, part.Fallback)
                        : (part.Fallback ?? string.Empty);
                }

                // 英文代名詞記號（{ROLE.he}／{ROLE.him}／{ROLE.his}）在一般變數代換前替換
                template = ApplyPronounTokens(template, r, isFemale, selfSubj, selfObj, selfPoss, part, onWarning);

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

            var sb = new System.Text.StringBuilder();
            sb.Append(parts[0]);
            for (int i = 1; i < parts.Count; i++)
            {
                if (!EndsWithEllipsisOrDash(parts[i - 1]))
                {
                    sb.Append(separator);
                }
                sb.Append(parts[i]);
            }

            string body = sb.ToString();
            if (!string.IsNullOrEmpty(sentenceEnd) && !body.EndsWith(sentenceEnd, StringComparison.Ordinal) && !EndsWithEllipsisOrDash(body))
            {
                body += sentenceEnd;
            }
            return body;
        }

        /// <summary>
        /// 碎片接縫：前一塊碎片結尾是刪節號或破折號（……、…、——、...）時不插分隔符，用來寫說溜嘴、欲言又止。
        /// </summary>
        public static bool EndsWithEllipsisOrDash(string s)
        {
            if (string.IsNullOrEmpty(s)) return false;
            return s.EndsWith("……", StringComparison.Ordinal)
                || s.EndsWith("…", StringComparison.Ordinal)
                || s.EndsWith("——", StringComparison.Ordinal)
                || s.EndsWith("...", StringComparison.Ordinal);
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

        private static readonly Regex PronounTokenRegex = new Regex(
            @"\{([A-Za-z0-9_]+)\.(he|him|his)\}",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        internal static string ApplyPronounTokens(
            string template,
            ComposedRumor r,
            Func<string, bool?>? isFemale,
            string selfSubj,
            string selfObj,
            string selfPoss,
            ComposedFactPart part,
            Action<string>? onWarning)
        {
            if (string.IsNullOrEmpty(template) || !template.Contains(".")) return template;

            var missingRoles = new List<string>();

            string result = PronounTokenRegex.Replace(template, match =>
            {
                string roleName = match.Groups[1].Value;
                string pronoun = match.Groups[2].Value.ToLowerInvariant();

                string? roleHeroId = null;
                bool hasRole = false;
                if (r.Roles != null)
                {
                    if (r.Roles.TryGetValue(roleName, out var hid))
                    {
                        roleHeroId = hid;
                        hasRole = true;
                    }
                    else
                    {
                        foreach (var kvp in r.Roles)
                        {
                            if (string.Equals(kvp.Key, roleName, StringComparison.OrdinalIgnoreCase))
                            {
                                roleHeroId = kvp.Value;
                                hasRole = true;
                                break;
                            }
                        }
                    }
                }

                bool isSpeaker = (!string.IsNullOrEmpty(r.SpeakerRole) && string.Equals(r.SpeakerRole, roleName, StringComparison.OrdinalIgnoreCase))
                    || (!string.IsNullOrEmpty(r.SpeakerHeroId) && hasRole && !string.IsNullOrEmpty(roleHeroId) && string.Equals(r.SpeakerHeroId, roleHeroId, StringComparison.OrdinalIgnoreCase));

                if (!isSpeaker && !string.IsNullOrEmpty(r.SpeakerHeroId) && part.Vars != null)
                {
                    if (part.Vars.TryGetValue(roleName, out var varVal) && varVal == "hero:" + r.SpeakerHeroId)
                    {
                        isSpeaker = true;
                    }
                }

                if (isSpeaker)
                {
                    return pronoun switch
                    {
                        "he" => selfSubj,
                        "him" => selfObj,
                        "his" => selfPoss,
                        _ => match.Value
                    };
                }

                if (!hasRole || string.IsNullOrEmpty(roleHeroId))
                {
                    missingRoles.Add(roleName + "." + pronoun);
                    return pronoun switch
                    {
                        "he" => "they",
                        "him" => "them",
                        "his" => "their",
                        _ => match.Value
                    };
                }

                bool? female = isFemale != null ? isFemale(roleHeroId!) : null;
                if (female == true)
                {
                    return pronoun switch
                    {
                        "he" => "she",
                        "him" => "her",
                        "his" => "her",
                        _ => match.Value
                    };
                }
                else if (female == false)
                {
                    return pronoun switch
                    {
                        "he" => "he",
                        "him" => "him",
                        "his" => "his",
                        _ => match.Value
                    };
                }
                else
                {
                    return pronoun switch
                    {
                        "he" => "they",
                        "him" => "them",
                        "his" => "their",
                        _ => match.Value
                    };
                }
            });

            if (missingRoles.Count > 0 && onWarning != null)
            {
                onWarning(FormatUnresolvedWarning(part, missingRoles, "they"));
            }

            return result;
        }

        /// <summary>
        /// 說話者就是事件當事人時，把樣板裡他的佔位符換成第一人稱：
        /// 後面接 's 的換成所有格、樣板開頭的換成主格、其餘換成受格。
        /// 繁中三種都是「我」（繁中樣板沒有 's，「{P}的」自然變成「我的」）。
        /// 通用規則講不通的句子由字串表另立當事人版本，不走這裡。
        /// </summary>
        internal static string ApplyFirstPersonPronouns(string template, string placeholderName, string subj, string obj, string poss)
        {
            if (string.IsNullOrEmpty(template) || string.IsNullOrEmpty(placeholderName)) return template;

            string p = "{" + placeholderName + "}";

            // 1. {P}'s / {P}’s -> poss
            template = ReplaceIgnoringCase(template, p + "’s", poss);
            template = ReplaceIgnoringCase(template, p + "'s", poss);

            // 2. 樣板開頭的 {P} -> subj
            int idx = template.IndexOf(p, StringComparison.OrdinalIgnoreCase);
            if (idx >= 0)
            {
                bool isAtStart = string.IsNullOrWhiteSpace(template.Substring(0, idx));
                if (isAtStart)
                {
                    template = template.Substring(0, idx) + subj + template.Substring(idx + p.Length);
                }
            }

            // 3. 其餘 {P} -> obj
            template = ReplaceIgnoringCase(template, p, obj);

            return template;
        }

        private static string ReplaceIgnoringCase(string source, string target, string replacement)
        {
            if (string.IsNullOrEmpty(source) || string.IsNullOrEmpty(target)) return source;
            var sb = new System.Text.StringBuilder();
            int i = 0;
            while (i < source.Length)
            {
                int idx = source.IndexOf(target, i, StringComparison.OrdinalIgnoreCase);
                if (idx < 0)
                {
                    sb.Append(source, i, source.Length - i);
                    break;
                }
                sb.Append(source, i, idx - i);
                sb.Append(replacement);
                i = idx + target.Length;
            }
            return sb.ToString();
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
        /// （只翻一半的語言）仍然會留空白，那正是拉丁字需要的。
        ///
        /// 例外：前綴語以半形字元結尾（英文的 "that"、"myself:"）時，後面接中文人名也要留空白。
        /// 中文介面下推給 AI 的英文句子，人名是遊戲介面上的中文，不留會變成 "I heard it said that科爾"。
        /// </summary>
        private static string ApplyPrefix(string prefix, string body)
        {
            if (string.IsNullOrEmpty(body)) return string.Empty;
            if (string.IsNullOrEmpty(prefix)) return body;
            if (prefix.EndsWith(" ", StringComparison.Ordinal)) return prefix + body;

            char last = prefix[prefix.Length - 1];
            bool needsSpace = !IsCjk(last) && (!IsCjk(body[0]) || last < 0x80);
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
