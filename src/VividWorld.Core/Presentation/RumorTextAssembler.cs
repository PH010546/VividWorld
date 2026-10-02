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
        private static readonly HashSet<string> ReportedMissingSentenceKeys = new HashSet<string>(StringComparer.Ordinal);

        public static void ResetSessionMissingSentenceKeys()
        {
            lock (ReportedMissingSentenceKeys)
            {
                ReportedMissingSentenceKeys.Clear();
            }
        }

        public static RumorRenderResult Assemble(
            ComposedRumor? r,
            PresentationConfig? cfg,
            Func<string, bool, string> resolveVar,
            Func<string?, string?, string>? getTemplate = null,
            Func<string?, string, string>? getLocalized = null,
            Action<string>? onWarning = null,
            Func<string, bool?>? isFemale = null,
            Action<string>? onInfo = null,
            string? listenerHeroId = null)
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

            // 句型候選鍵與變數取得
            string? candidateKey = r.SentenceKeyCandidate;
            IReadOnlyDictionary<string, string> vars = r.SentenceVars;

            if (string.IsNullOrEmpty(candidateKey) && r.Parts != null && r.Parts.Count > 0)
            {
                var partsWithId = r.Parts.Where(p => !string.IsNullOrEmpty(p.TextId)).ToList();
                if (partsWithId.Count > 0)
                {
                    string stem = SentenceCombinationEnumerator.ExtractStem(partsWithId[0].TextId);
                    var segs = partsWithId.Select(p => SentenceCombinationEnumerator.ExtractSegment(p.TextId));
                    string? roleUpper = !string.IsNullOrEmpty(r.SpeakerRole)
                        ? r.SpeakerRole!.ToUpperInvariant()
                        : null;
                    candidateKey = SentenceCombinationEnumerator.ComputeSentenceKey(stem, segs, roleUpper);
                    if (vars == null || vars.Count == 0)
                    {
                        var combinedVars = new Dictionary<string, string>(StringComparer.Ordinal);
                        foreach (var p in r.Parts)
                        {
                            if (p.Vars != null)
                            {
                                foreach (var kvp in p.Vars) combinedVars[kvp.Key] = kvp.Value;
                            }
                        }
                        vars = combinedVars;
                    }
                }
            }

            // 挑句判定：依候選鍵順序查找，查無句型或設定關閉才退回拼接
            bool enableWholeSentences = cfg?.WholeSentences ?? true;
            string? wholeTemplate = null;
            string? usedSentenceKey = null;
            var checkedKeys = new List<string>();

            var candidatesToCheck = (r.SentenceKeyCandidates != null && r.SentenceKeyCandidates.Count > 0)
                ? r.SentenceKeyCandidates
                : (!string.IsNullOrEmpty(candidateKey) ? new[] { candidateKey! } : Array.Empty<string>());

            if (!enableWholeSentences)
            {
                onInfo?.Invoke("Rumor text: fallback to concatenation (wholeSentences is false)");
            }
            else
            {
                foreach (var k in candidatesToCheck)
                {
                    checkedKeys.Add(k);
                    string? t = null;
                    if (getTemplate != null)
                    {
                        string? got = getTemplate(k, null);
                        if (!string.IsNullOrEmpty(got)) t = got;
                    }
                    else if (getLocalized != null)
                    {
                        string got = getLocalized(k, string.Empty);
                        if (!string.IsNullOrEmpty(got)) t = got;
                    }

                    if (t != null)
                    {
                        wholeTemplate = t;
                        usedSentenceKey = k;
                        break;
                    }
                }

                if (wholeTemplate != null)
                {
                    onInfo?.Invoke($"Rumor text: using sentence template '{usedSentenceKey}' (candidate keys checked: {string.Join(", ", checkedKeys)})");
                }
                else if (checkedKeys.Count > 0)
                {
                    bool shouldReport = false;
                    lock (ReportedMissingSentenceKeys)
                    {
                        shouldReport = ReportedMissingSentenceKeys.Add(checkedKeys[0]);
                    }
                    if (shouldReport)
                    {
                        onInfo?.Invoke($"Rumor text: fallback to concatenation (missing keys: {string.Join(", ", checkedKeys)})");
                    }
                }
            }

            // 當事人句尾判定：說話的人是當事人、且這次用的是整句（_Self_ 句型）時，事實句後面接他的句尾一句。
            // 退回拼接時不接：拼接用的 _Self_ 碎片本身已經帶了他的口氣，再接會講兩遍。
            string? usedSelfFeelingKey = null;
            string? selfFeelingTemplate = null;
            var feelingCandidates = r.SelfFeelingKeyCandidates ?? Array.Empty<string>();

            if (r.IsGist && !string.IsNullOrEmpty(r.SpeakerRole))
            {
                onInfo?.Invoke("Rumor text self feeling: omitted (the speaker is only telling the gist, so the player doesn't hear how they felt)");
            }
            else if (feelingCandidates.Count == 0)
            {
                onInfo?.Invoke("Rumor text self feeling: omitted (speaker is not the person this happened to, or the message has no speaker)");
            }
            else if (wholeTemplate == null)
            {
                onInfo?.Invoke("Rumor text self feeling: omitted (no whole sentence was used, so the concatenated first-person fragments already carry the speaker's own voice)");
            }
            else
            {
                var feelingChecked = new List<string>();
                foreach (var sfKey in feelingCandidates)
                {
                    feelingChecked.Add(sfKey);
                    string? t = null;
                    if (getTemplate != null)
                    {
                        string? got = getTemplate(sfKey, null);
                        if (!string.IsNullOrEmpty(got)) t = got;
                    }
                    else if (getLocalized != null)
                    {
                        string got = getLocalized(sfKey, string.Empty);
                        if (!string.IsNullOrEmpty(got)) t = got;
                    }

                    if (!string.IsNullOrEmpty(t))
                    {
                        selfFeelingTemplate = t;
                        usedSelfFeelingKey = sfKey;
                        break;
                    }
                }

                if (selfFeelingTemplate != null)
                {
                    onInfo?.Invoke($"Rumor text self feeling: using '{usedSelfFeelingKey}' (keys checked: {string.Join(", ", feelingChecked)})");
                    if (r.SelfFeelingVariantLog != null)
                    {
                        string missingNote = r.SelfFeelingVariantKey != null && r.SelfFeelingVariantKey != usedSelfFeelingKey
                            ? $" (variant '{r.SelfFeelingVariantKey}' has no string in this language, so the default line is used)"
                            : string.Empty;
                        onInfo?.Invoke($"Rumor text self feeling variant: {r.SelfFeelingVariantLog}{missingNote}");
                    }
                }
                else
                {
                    onInfo?.Invoke($"Rumor text self feeling: omitted (missing keys: {string.Join(", ", feelingChecked)})");
                }
            }

            // 兩趟各渲染一次（顯示用帶連結、診斷用純文字），**但診斷只在純文字那一趟發**
            string displayBody = wholeTemplate != null
                ? RenderSentenceBody(wholeTemplate, r, vars, sentenceEnd, enableLinks, resolveVar, getLocalized, null, isFemale, usedSentenceKey, listenerHeroId)
                : RenderBody(r, separator, sentenceEnd, enableLinks, resolveVar, getTemplate, getLocalized, null, isFemale, listenerHeroId);

            string plainBody = wholeTemplate != null
                ? RenderSentenceBody(wholeTemplate, r, vars, sentenceEnd, false, resolveVar, getLocalized, onWarning, isFemale, usedSentenceKey, listenerHeroId)
                : RenderBody(r, separator, sentenceEnd, false, resolveVar, getTemplate, getLocalized, onWarning, isFemale, listenerHeroId);

            if (!string.IsNullOrEmpty(selfFeelingTemplate))
            {
                string displaySelfFeeling = RenderSentenceBody(selfFeelingTemplate!, r, vars, sentenceEnd, enableLinks, resolveVar, getLocalized, null, isFemale, usedSelfFeelingKey, listenerHeroId);
                string plainSelfFeeling = RenderSentenceBody(selfFeelingTemplate!, r, vars, sentenceEnd, false, resolveVar, getLocalized, onWarning, isFemale, usedSelfFeelingKey, listenerHeroId);

                displayBody = AttachSelfFeeling(displayBody, displaySelfFeeling, sentenceEnd);
                plainBody = AttachSelfFeeling(plainBody, plainSelfFeeling, sentenceEnd);
            }

            // 說話的人的感想：只在講給玩家聽的完整分享時有判定；事實句後面另起一句
            string? usedFeelingKey = null;
            if (r.Feeling != null && r.Feeling.LineKey != null)
            {
                string? displayFeeling = RenderFeeling(r.Feeling, enableLinks, resolveVar, getTemplate, getLocalized, sentenceEnd, out string? feelingMissing, isFemale, r.SpeakerHeroId, null, listenerHeroId);
                if (displayFeeling != null)
                {
                    string plainFeeling = RenderFeeling(r.Feeling, false, resolveVar, getTemplate, getLocalized, sentenceEnd, out _, isFemale, r.SpeakerHeroId, onWarning, listenerHeroId)!;
                    displayBody = AttachSelfFeeling(displayBody, displayFeeling, sentenceEnd);
                    plainBody = AttachSelfFeeling(plainBody, plainFeeling, sentenceEnd);
                    usedFeelingKey = r.Feeling.LineKey;
                    onInfo?.Invoke($"Rumor text feeling: using '{usedFeelingKey}' (address '{r.Feeling.AddressKey}')");
                }
                else
                {
                    onInfo?.Invoke($"Rumor text feeling: omitted (missing key: {feelingMissing})");
                }
            }

            // 收尾：只講大概而且確實少講時，事實句（與句尾）後面接一句表示話沒說完
            string? usedClosingKey = null;
            if (!string.IsNullOrEmpty(r.ClosingKey))
            {
                string? closing = null;
                if (getTemplate != null)
                {
                    string? got = getTemplate(r.ClosingKey!, null);
                    if (!string.IsNullOrEmpty(got)) closing = got;
                }
                else if (getLocalized != null)
                {
                    string got = getLocalized(r.ClosingKey!, string.Empty);
                    if (!string.IsNullOrEmpty(got)) closing = got;
                }

                if (closing != null)
                {
                    usedClosingKey = r.ClosingKey;
                    string displayClosing = RenderSentenceBody(closing, r, vars, sentenceEnd, enableLinks, resolveVar, getLocalized, null, isFemale, usedClosingKey, listenerHeroId);
                    string plainClosing = RenderSentenceBody(closing, r, vars, sentenceEnd, false, resolveVar, getLocalized, onWarning, isFemale, usedClosingKey, listenerHeroId);
                    displayBody = AttachSelfFeeling(displayBody, displayClosing, sentenceEnd);
                    plainBody = AttachSelfFeeling(plainBody, plainClosing, sentenceEnd);
                    onInfo?.Invoke($"Rumor text closing: using '{usedClosingKey}' (gist, and the speaker held back facts they know)");
                }
                else
                {
                    onInfo?.Invoke($"Rumor text closing: omitted (missing key: {r.ClosingKey})");
                }
            }
            else if (r.IsGist)
            {
                onInfo?.Invoke(r.HeldBack
                    ? "Rumor text closing: omitted (no speaker, so not told in conversation)"
                    : "Rumor text closing: omitted (gist, but the speaker knows nothing more than this)");
            }

            string displayPrefix = RenderPrefix(r, enableLinks, resolveVar, getTemplate, getLocalized, null, isFemale, listenerHeroId);
            string plainPrefix = RenderPrefix(r, false, resolveVar, getTemplate, getLocalized, onWarning, isFemale, listenerHeroId);

            string displayFull = ApplyPrefix(displayPrefix, displayBody);
            string plainFull = ApplyPrefix(plainPrefix, plainBody);

            string candidateReportingKey = candidatesToCheck.Count > 0 ? candidatesToCheck[0] : (candidateKey ?? string.Empty);
            return new RumorRenderResult(displayFull, plainFull, usedWholeSentence: wholeTemplate != null, sentenceKeyUsed: usedSentenceKey, sentenceKeyCandidate: candidateReportingKey, selfFeelingKeyUsed: usedSelfFeelingKey, sentenceKeysChecked: candidatesToCheck.ToList(), closingKeyUsed: usedClosingKey, feelingKeyUsed: usedFeelingKey);
        }

        /// <summary>
        /// 把感想判定渲染成一句：查句子與稱呼的字串，稱呼裡的 <c>{NAME}</c> 換成焦點人物的名字，
        /// 再把句子裡的 <c>{ADDRESS}</c> 換成稱呼；代名詞依焦點人物性別換字（查不到時為他／he）；
        /// 句子以稱呼開頭時，換完之後首字母大寫（英文）。
        /// 查不到句子或稱呼時回傳 null，<paramref name="missingKey"/> 是缺的那一個鍵。
        /// </summary>
        public static string? RenderFeeling(
            VividWorld.Core.Feelings.FeelingDecision? decision,
            bool useLinks,
            Func<string, bool, string> resolveVar,
            Func<string?, string?, string>? getTemplate,
            Func<string?, string, string>? getLocalized,
            string sentenceEnd,
            out string? missingKey,
            Func<string, bool?>? isFemale = null,
            string? speakerHeroId = null,
            Action<string>? onWarning = null,
            string? listenerHeroId = null)
        {
            missingKey = null;
            if (decision == null || string.IsNullOrEmpty(decision.LineKey)) return null;

            string? sentence = LookupString(decision.LineKey!, getTemplate, getLocalized);
            if (string.IsNullOrEmpty(sentence))
            {
                missingKey = decision.LineKey;
                return null;
            }

            // 依性別選字的記號要在稱呼換成人名之前處理：人名（或它的百科連結標記）是外來文字，
            // 不該有機會被當成記號再解一次。
            sentence = ApplyGenderSelectTokens(sentence!, null, decision, speakerHeroId, isFemale, null, onWarning, decision.LineKey, listenerHeroId);

            const string token = "{ADDRESS}";
            if (sentence!.Contains(token))
            {
                string? address = string.IsNullOrEmpty(decision.AddressKey)
                    ? null
                    : LookupString(decision.AddressKey!, getTemplate, getLocalized);
                if (string.IsNullOrEmpty(address))
                {
                    missingKey = decision.AddressKey ?? "(no address key)";
                    return null;
                }

                if (address!.Contains("{NAME}"))
                {
                    string name = string.IsNullOrEmpty(decision.FocusHeroId)
                        ? string.Empty
                        : resolveVar("hero:" + decision.FocusHeroId, useLinks);
                    address = address.Replace("{NAME}", name);
                }

                bool startsWithAddress = sentence.TrimStart().StartsWith(token, StringComparison.Ordinal);
                sentence = sentence.Replace(token, address);
                if (startsWithAddress) sentence = CapitalizeFirstAscii(sentence.TrimStart());
            }

            // 指焦點人物的代名詞依性別換字。講的當下查不到時用判定那時記下的性別，再不行當成男性。
            bool? female = null;
            if (isFemale != null && !string.IsNullOrEmpty(decision.FocusHeroId))
            {
                female = isFemale(decision.FocusHeroId!);
            }
            female ??= decision.FocusIsFemale;
            sentence = ApplyFocusPronouns(sentence, female == true, getLocalized);

            string body = sentence.Trim();
            if (!string.IsNullOrEmpty(sentenceEnd) && !EndsWithSentenceMark(body, sentenceEnd))
            {
                body += sentenceEnd;
            }
            return body;
        }

        /// <summary>
        /// 感想句裡指焦點人物的記號：{he}／{him}／{his}／{himself}（句首用 {He}）。
        /// 查字串表 VividWorld_Pronoun_*；getLocalized 為 null 時使用英文後備。
        /// </summary>
        internal static string ApplyFocusPronouns(string sentence, bool female, Func<string?, string, string>? getLocalized = null)
        {
            if (string.IsNullOrEmpty(sentence) || sentence.IndexOf('{') < 0) return sentence;

            char gender = female ? 'F' : 'M';

            if (sentence.Contains("{himself}"))
            {
                string rep = PronounFor("himself", gender, getLocalized, female ? "herself" : "himself");
                sentence = sentence.Replace("{himself}", rep);
            }
            if (sentence.Contains("{He}"))
            {
                string rep = PronounFor("he", gender, getLocalized, female ? "she" : "he");
                string capitalized = rep.Length > 0 ? char.ToUpperInvariant(rep[0]) + rep.Substring(1) : rep;
                sentence = sentence.Replace("{He}", capitalized);
            }
            if (sentence.Contains("{he}"))
            {
                string rep = PronounFor("he", gender, getLocalized, female ? "she" : "he");
                sentence = sentence.Replace("{he}", rep);
            }
            if (sentence.Contains("{him}"))
            {
                string rep = PronounFor("him", gender, getLocalized, female ? "her" : "him");
                sentence = sentence.Replace("{him}", rep);
            }
            if (sentence.Contains("{his}"))
            {
                string rep = PronounFor("his", gender, getLocalized, female ? "her" : "his");
                sentence = sentence.Replace("{his}", rep);
            }

            return sentence;
        }

        private static string? LookupString(
            string key,
            Func<string?, string?, string>? getTemplate,
            Func<string?, string, string>? getLocalized)
        {
            if (getTemplate != null)
            {
                string? got = getTemplate(key, null);
                if (!string.IsNullOrEmpty(got)) return got;
                return null;
            }
            if (getLocalized != null)
            {
                string got = getLocalized(key, string.Empty);
                if (!string.IsNullOrEmpty(got)) return got;
            }
            return null;
        }

        private static string RenderPrefix(
            ComposedRumor r,
            bool useLinks,
            Func<string, bool, string> resolveVar,
            Func<string?, string?, string>? getTemplate,
            Func<string?, string, string>? getLocalized,
            Action<string>? onWarning,
            Func<string, bool?>? isFemale = null,
            string? listenerHeroId = null)
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
            template = ApplyGenderSelectTokens(template, r, null, r.SpeakerHeroId, isFemale, vars, onWarning, r.PrefixTextId, listenerHeroId);
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

        private static string RenderSentenceBody(
            string template,
            ComposedRumor r,
            IReadOnlyDictionary<string, string> vars,
            string sentenceEnd,
            bool useLinks,
            Func<string, bool, string> resolveVar,
            Func<string?, string, string>? getLocalized,
            Action<string>? onWarning,
            Func<string, bool?>? isFemale,
            string? usedSentenceKey,
            string? listenerHeroId = null)
        {
            string selfSubj = getLocalized != null ? getLocalized("VividWorld_Self_Subject", "I") : "I";
            string selfObj = getLocalized != null ? getLocalized("VividWorld_Self_Object", "me") : "me";
            string selfPoss = getLocalized != null ? getLocalized("VividWorld_Self_Possessive", "my") : "my";

            template = ApplyGenderSelectTokens(template, r, null, r.SpeakerHeroId, isFemale, vars, onWarning, usedSentenceKey, listenerHeroId);
            template = ApplyPronounTokens(template, r, isFemale, selfSubj, selfObj, selfPoss, vars, onWarning, usedSentenceKey, fixNeutralAgreement: true, getLocalized: getLocalized);

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

                onWarning?.Invoke(FormatUnresolvedSentenceWarning(usedSentenceKey, unresolved, unknown, vars));

                foreach (string name in unresolved)
                {
                    rendered = rendered.Replace("{" + name + "}", unknown);
                }
            }

            string body = rendered.Trim();
            if (!string.IsNullOrEmpty(sentenceEnd) && !EndsWithSentenceMark(body, sentenceEnd))
            {
                body += sentenceEnd;
            }
            return body;
        }

        /// <summary>
        /// 句子本身已經用句號、問號、驚嘆號、刪節號或破折號收尾時，不再補句號
        /// （整句句型與句尾都會有反問句，補了會變成「?.」）。
        /// </summary>
        internal static bool EndsWithSentenceMark(string s, string sentenceEnd)
        {
            if (string.IsNullOrEmpty(s)) return false;
            if (!string.IsNullOrEmpty(sentenceEnd) && s.EndsWith(sentenceEnd, StringComparison.Ordinal)) return true;
            if (EndsWithEllipsisOrDash(s)) return true;
            char last = s[s.Length - 1];
            return last == '?' || last == '!' || last == '？' || last == '！';
        }

        internal static string FormatUnresolvedSentenceWarning(
            string? sentenceKey, IReadOnlyList<string> unresolved, string renderedAs, IReadOnlyDictionary<string, string>? vars)
        {
            string varsPresent = (vars == null || vars.Count == 0)
                ? "(none)"
                : string.Join(", ", vars.Keys);

            return $"Rumor text: placeholder(s) {{{string.Join(", ", unresolved)}}} in sentence template " +
                   $"'{sentenceKey}' had no value in Vars (vars present: {varsPresent}) " +
                   $"- rendered as \"{renderedAs}\".";
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
            Func<string, bool?>? isFemale,
            string? listenerHeroId = null)
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

                // 英文代名詞記號（{ROLE.he}／{ROLE.him}／{ROLE.his}）與依性別選字記號在一般變數代換前替換
                template = ApplyGenderSelectTokens(template, r, null, r.SpeakerHeroId, isFemale, part.Vars, onWarning, part.TextId, listenerHeroId);
                template = ApplyPronounTokens(template, r, isFemale, selfSubj, selfObj, selfPoss, part, onWarning, getLocalized);

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

        internal static string FormatUnresolvedWarning(
            string sourceId, IReadOnlyList<string> unresolved, string renderedAs, IReadOnlyDictionary<string, string>? vars)
        {
            string varsPresent = (vars == null || vars.Count == 0)
                ? "(none)"
                : string.Join(", ", vars.Keys);

            return $"Rumor text: placeholder(s) {{{string.Join(", ", unresolved)}}} in fact part " +
                   $"'{sourceId}' had no value in Vars (vars present: {varsPresent}) " +
                   $"- rendered as \"{renderedAs}\".";
        }

        private static readonly Regex PronounTokenRegex = new Regex(
            @"\{([A-Za-z0-9_]+)\.(himself|he|him|his)\}",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        internal static string ApplyPronounTokens(
            string template,
            ComposedRumor r,
            Func<string, bool?>? isFemale,
            string selfSubj,
            string selfObj,
            string selfPoss,
            ComposedFactPart part,
            Action<string>? onWarning,
            Func<string?, string, string>? getLocalized = null)
        {
            return ApplyPronounTokens(template, r, isFemale, selfSubj, selfObj, selfPoss, part?.Vars, onWarning, part?.TextId, false, getLocalized);
        }

        internal static string ApplyPronounTokens(
            string template,
            ComposedRumor r,
            Func<string, bool?>? isFemale,
            string selfSubj,
            string selfObj,
            string selfPoss,
            IReadOnlyDictionary<string, string>? vars,
            Action<string>? onWarning,
            string? sourceId = null,
            bool fixNeutralAgreement = false,
            Func<string?, string, string>? getLocalized = null)
        {
            if (string.IsNullOrEmpty(template) || !template.Contains(".")) return template;

            var missingRoles = new List<string>();
            bool usedNeutral = false;

            string ResolveToken(Match match)
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

                if (!isSpeaker && !string.IsNullOrEmpty(r.SpeakerHeroId) && vars != null)
                {
                    if (vars.TryGetValue(roleName, out var varVal) && varVal == "hero:" + r.SpeakerHeroId)
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
                        "himself" => getLocalized != null ? getLocalized("VividWorld_Self_Reflexive", "myself") : "myself",
                        _ => match.Value
                    };
                }

                if (!hasRole || string.IsNullOrEmpty(roleHeroId))
                {
                    missingRoles.Add(roleName + "." + pronoun);
                    usedNeutral = true;
                    return PronounFor(pronoun, 'N', getLocalized, match.Value);
                }

                bool? female = isFemale != null ? isFemale(roleHeroId!) : null;
                if (female == null) usedNeutral = true;
                return PronounFor(pronoun, female == true ? 'F' : (female == false ? 'M' : 'N'), getLocalized, match.Value);
            }

            // 樣板寫成 {P.He}（首字大寫）時，換出來的代名詞也大寫，給句首用
            string result = PronounTokenRegex.Replace(template, match =>
            {
                string resolved = ResolveToken(match);
                string written = match.Groups[2].Value;
                if (written.Length > 0 && char.IsUpper(written[0]) && resolved.Length > 0)
                {
                    return char.ToUpperInvariant(resolved[0]) + resolved.Substring(1);
                }
                return resolved;
            });

            // 只有整句句型做這件事：拼接的舊字串本來就不管動詞的單複數，維持原樣
            if (usedNeutral && fixNeutralAgreement)
            {
                result = FixNeutralPronounAgreement(result);
            }

            if (missingRoles.Count > 0 && onWarning != null)
            {
                onWarning(FormatUnresolvedWarning(sourceId ?? "(unknown)", missingRoles, "they", vars));
            }

            return result;
        }

        /// <summary>
        /// 代名詞查字串表：性別 M／F／N（查不到性別時用 N 一組）。
        /// getLocalized 為 null 時使用英文後備。
        /// </summary>
        private static string PronounFor(string pronoun, char gender, Func<string?, string, string>? getLocalized, string fallbackWhenUnknown)
        {
            string key;
            string fallback;
            switch (pronoun)
            {
                case "he":
                    key = gender == 'F' ? "VividWorld_Pronoun_He_F" : (gender == 'M' ? "VividWorld_Pronoun_He_M" : "VividWorld_Pronoun_He_N");
                    fallback = gender == 'F' ? "she" : (gender == 'M' ? "he" : "they");
                    break;
                case "him":
                    key = gender == 'F' ? "VividWorld_Pronoun_Him_F" : (gender == 'M' ? "VividWorld_Pronoun_Him_M" : "VividWorld_Pronoun_Him_N");
                    fallback = gender == 'F' ? "her" : (gender == 'M' ? "him" : "them");
                    break;
                case "his":
                    key = gender == 'F' ? "VividWorld_Pronoun_His_F" : (gender == 'M' ? "VividWorld_Pronoun_His_M" : "VividWorld_Pronoun_His_N");
                    fallback = gender == 'F' ? "her" : (gender == 'M' ? "his" : "their");
                    break;
                case "himself":
                    key = gender == 'F' ? "VividWorld_Pronoun_Himself_F" : (gender == 'M' ? "VividWorld_Pronoun_Himself_M" : "VividWorld_Pronoun_Himself_N");
                    fallback = gender == 'F' ? "herself" : (gender == 'M' ? "himself" : "themselves");
                    break;
                default:
                    return fallbackWhenUnknown;
            }

            return getLocalized != null ? getLocalized(key, fallback) : fallback;
        }

        private static readonly Regex GenderSelectTokenRegex = new Regex(
            @"\{([^{}:]*):([^{}]*)\}",
            RegexOptions.Compiled);

        public static string ApplyGenderSelectTokens(
            string template,
            ComposedRumor? r,
            VividWorld.Core.Feelings.FeelingDecision? feelingDecision,
            string? speakerHeroId,
            Func<string, bool?>? isFemale,
            IReadOnlyDictionary<string, string>? vars,
            Action<string>? onWarning,
            string? stringKey,
            string? listenerHeroId = null)
        {
            if (string.IsNullOrEmpty(template) || template.IndexOf(':') < 0 || template.IndexOf('{') < 0)
            {
                return template;
            }

            speakerHeroId ??= r?.SpeakerHeroId;

            return GenderSelectTokenRegex.Replace(template, match =>
            {
                string name = match.Groups[1].Value.Trim();
                string rawSegments = match.Groups[2].Value;
                string[] segments = rawSegments.Split('|');

                // 檢查語法錯誤：只有一段
                if (segments.Length < 2)
                {
                    string singleOutput = segments.Length == 1 ? segments[0] : string.Empty;
                    onWarning?.Invoke(FormatGenderSelectWarning(stringKey, match.Value, "has only one segment (missing '|') - expected at least 2 segments separated by '|'", singleOutput));
                    return singleOutput;
                }

                // 解析名字
                char? gender = null;
                bool nameRecognized = false;

                if (string.Equals(name, "ME", StringComparison.OrdinalIgnoreCase))
                {
                    nameRecognized = true;
                    if (!string.IsNullOrEmpty(speakerHeroId) && isFemale != null)
                    {
                        bool? f = isFemale(speakerHeroId!);
                        if (f.HasValue)
                        {
                            gender = f.Value ? 'F' : 'M';
                        }
                    }
                }
                else if (string.Equals(name, "YOU", StringComparison.OrdinalIgnoreCase))
                {
                    // 聽的人：沒傳或查不到性別時不發警告，照查不到性別處理（跟 ME 沒有說話的人時相同）
                    nameRecognized = true;
                    if (!string.IsNullOrEmpty(listenerHeroId) && isFemale != null)
                    {
                        bool? f = isFemale(listenerHeroId!);
                        if (f.HasValue)
                        {
                            gender = f.Value ? 'F' : 'M';
                        }
                    }
                }
                else if (string.Equals(name, "FOCUS", StringComparison.OrdinalIgnoreCase))
                {
                    if (feelingDecision != null)
                    {
                        nameRecognized = true;
                        bool? f = null;
                        if (isFemale != null && !string.IsNullOrEmpty(feelingDecision.FocusHeroId))
                        {
                            f = isFemale(feelingDecision.FocusHeroId!);
                        }
                        f ??= feelingDecision.FocusIsFemale;
                        if (f.HasValue)
                        {
                            gender = f.Value ? 'F' : 'M';
                        }
                    }
                }
                else
                {
                    // 事件角色名（不分大小寫）
                    string? roleHeroId = null;
                    bool hasRole = false;

                    if (r?.Roles != null)
                    {
                        if (r.Roles.TryGetValue(name, out var hid))
                        {
                            roleHeroId = hid;
                            hasRole = true;
                        }
                        else
                        {
                            foreach (var kvp in r.Roles)
                            {
                                if (string.Equals(kvp.Key, name, StringComparison.OrdinalIgnoreCase))
                                {
                                    roleHeroId = kvp.Value;
                                    hasRole = true;
                                    break;
                                }
                            }
                        }
                    }

                    if (!hasRole && vars != null)
                    {
                        if (vars.TryGetValue(name, out var varVal) && varVal != null && varVal.StartsWith("hero:"))
                        {
                            roleHeroId = varVal.Substring("hero:".Length);
                            hasRole = true;
                        }
                        else
                        {
                            foreach (var kvp in vars)
                            {
                                if (string.Equals(kvp.Key, name, StringComparison.OrdinalIgnoreCase) &&
                                    kvp.Value != null && kvp.Value.StartsWith("hero:"))
                                {
                                    roleHeroId = kvp.Value.Substring("hero:".Length);
                                    hasRole = true;
                                    break;
                                }
                            }
                        }
                    }

                    if (!hasRole && !string.IsNullOrEmpty(r?.SpeakerRole) && string.Equals(r!.SpeakerRole, name, StringComparison.OrdinalIgnoreCase))
                    {
                        roleHeroId = r.SpeakerHeroId;
                        hasRole = true;
                    }

                    if (hasRole)
                    {
                        nameRecognized = true;
                        if (!string.IsNullOrEmpty(roleHeroId) && isFemale != null)
                        {
                            bool? f = isFemale(roleHeroId!);
                            if (f.HasValue)
                            {
                                gender = f.Value ? 'F' : 'M';
                            }
                        }
                    }
                }

                if (!nameRecognized)
                {
                    // 名字認不得就用第一段，並發警告
                    string fallbackOutput = segments[0];
                    onWarning?.Invoke(FormatGenderSelectWarning(stringKey, match.Value, $"name '{name}' is not a recognized role in this event and not ME, YOU or FOCUS", fallbackOutput));
                    return fallbackOutput;
                }

                // 依性別選字：男用第一段、女用第二段、查不到時有第三段用第三段，沒有用第一段
                if (gender == 'M')
                {
                    return segments[0];
                }
                if (gender == 'F')
                {
                    return segments[1];
                }

                // 查不到性別
                return segments.Length >= 3 ? segments[2] : segments[0];
            });
        }

        /// <summary>
        /// 對話選單上的固定句子（沒有事件角色、不經過組句）套依性別選字的記號。
        /// 只認 <c>ME</c>（說話的人）與 <c>YOU</c>（聽的人）；事件角色名與 <c>FOCUS</c> 在這裡沒有對象，
        /// 照記號的既有規則發警告並用第一段。解析本身交給 <see cref="ApplyGenderSelectTokens"/>，不另寫一份。
        /// </summary>
        public static string ApplyFixedLineGenderSelect(
            string template,
            string stringKey,
            string? speakerHeroId,
            string? listenerHeroId,
            Func<string, bool?>? isFemale,
            Action<string>? onWarning)
        {
            return ApplyGenderSelectTokens(template, null, null, speakerHeroId, isFemale, null, onWarning, stringKey, listenerHeroId);
        }

        internal static string FormatGenderSelectWarning(
            string? stringKey, string tokenText, string reason, string renderedAs)
        {
            return $"Rumor text: invalid gender selection token '{tokenText}' in '{stringKey ?? "(unknown)"}': {reason} - rendered as \"{renderedAs}\".";
        }

        private static readonly Regex NeutralVerbRegex = new Regex(
            @"\b(they|They) (was|wasn't|is|isn't|has|hasn't|does|doesn't)\b",
            RegexOptions.Compiled);

        /// <summary>
        /// 性別查不到時代名詞退成 they，緊接著的動詞要跟著換複數形（「they was」→「they were」）。
        /// 只在這次真的換出 they 的句子上做，寫死的 they 不受影響。
        /// </summary>
        internal static string FixNeutralPronounAgreement(string text)
        {
            return NeutralVerbRegex.Replace(text, m =>
            {
                string verb = m.Groups[2].Value switch
                {
                    "was" => "were",
                    "wasn't" => "weren't",
                    "is" => "are",
                    "isn't" => "aren't",
                    "has" => "have",
                    "hasn't" => "haven't",
                    "does" => "do",
                    _ => "don't"
                };
                return m.Groups[1].Value + " " + verb;
            });
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

        private static string AttachSelfFeeling(string body, string selfFeeling, string sentenceEnd)
        {
            if (string.IsNullOrEmpty(selfFeeling)) return body;
            if (string.IsNullOrEmpty(body)) return selfFeeling;

            string b = body.TrimEnd();
            if (!string.IsNullOrEmpty(sentenceEnd) && !EndsWithSentenceMark(b, sentenceEnd))
            {
                b += sentenceEnd;
            }

            char last = b[b.Length - 1];
            bool needsSpace = !IsCjk(last) && (!IsCjk(selfFeeling[0]) || last < 0x80);
            return needsSpace ? b + " " + selfFeeling : b + selfFeeling;
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
            if (string.IsNullOrEmpty(prefix)) return CapitalizeFirstAscii(body);
            if (prefix.EndsWith(" ", StringComparison.Ordinal)) return prefix + body;

            char last = prefix[prefix.Length - 1];
            bool needsSpace = !IsCjk(last) && (!IsCjk(body[0]) || last < 0x80);
            return needsSpace ? prefix + " " + body : prefix + body;
        }

        internal static string CapitalizeFirstAscii(string s)
        {
            if (string.IsNullOrEmpty(s)) return s;
            char c = s[0];
            if (c >= 'a' && c <= 'z')
            {
                return (char)(c - 32) + s.Substring(1);
            }
            return s;
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
