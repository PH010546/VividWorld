#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using VividWorld.Core.Catalog;
using VividWorld.Core.Config;
using VividWorld.Core.Events;
using VividWorld.Core.Presentation;

namespace VividWorld.Core.Diagnostics
{
    /// <summary>
    /// 對目錄裡每種消息（含依情況換碎片的變體），列出所有實際會出現的碎片組合 × 講的人 × 中英兩種語言的渲染結果，
    /// 標明用了整句或退回拼接，最後列出缺少的鍵、有 _From_ 句的組合數與沒被任何組合用到的鍵。
    /// 開發者對話行「template renders」呼叫它。
    /// </summary>
    public static class TemplateRenderSampleFormatter
    {
        private sealed class SampleHero
        {
            public string En { get; }
            public string Cnt { get; }
            public bool Female { get; }

            public SampleHero(string en, string cnt, bool female = false)
            {
                En = en;
                Cnt = cnt;
                Female = female;
            }
        }

        // 名字與定稿句子一致，方便逐句對照。
        private static readonly Dictionary<string, SampleHero> RoleHeroNames =
            new(StringComparer.OrdinalIgnoreCase)
            {
                ["victim"] = new("Derthert", "德瑟特"),
                ["killer"] = new("Caladog", "卡拉多格"),
                ["prisoner"] = new("Derthert", "德瑟特"),
                ["captor"] = new("Caladog", "卡拉多格"),
                ["rescuer"] = new("Pol", "波爾"),
                ["slighted"] = new("Derthert", "德瑟特"),
                ["favored"] = new("Ergeon", "埃爾貢"),
                ["host"] = new("Ergeon", "埃爾貢"),
                ["student"] = new("Pol", "波爾"),
                ["veteran"] = new("Caladog", "卡拉多格"),
                ["speaker"] = new("Caladog", "卡拉多格"),
                ["listener"] = new("Pol", "波爾"),
                ["claimant"] = new("Caladog", "卡拉多格"),
                ["rival"] = new("Pol", "波爾"),
                ["aggrieved"] = new("Derthert", "德瑟特"),
                ["patron"] = new("Caladog", "卡拉多格"),
                ["challenger"] = new("Caladog", "卡拉多格"),
                ["challenged"] = new("Pol", "波爾"),
                ["spouse_a"] = new("Pol", "波爾"),
                ["spouse_b"] = new("Ira", "伊拉", female: true),
                ["mother"] = new("Ira", "伊拉", female: true),
                ["child"] = new("Rolan", "羅蘭"),
                ["onlooker"] = new("Garios", "加里奧斯")
            };

        // 個別消息裡同一個角色名指的人不同（例：宴會擺在誰家、難產過世的是誰）
        private static readonly Dictionary<(string Type, string Role), SampleHero> TypeSpecificNames =
            new()
            {
                [("seat_dispute_walked_out", "host")] = new("Caladog", "卡拉多格"),
                [("hero_died_in_labor", "victim")] = new("Ira", "伊拉", female: true)
            };

        private static SampleHero HeroFor(string type, string role)
        {
            if (TypeSpecificNames.TryGetValue((type, role.ToLowerInvariant()), out var specific)) return specific;
            return RoleHeroNames.TryGetValue(role, out var hero) ? hero : new SampleHero(role, role);
        }

        public static string FormatAll(
            IReadOnlyList<EventTemplate> templates,
            EnglishStringTable enTable,
            EnglishStringTable cntTable,
            PresentationConfig? cfg = null,
            RetentionConfig? retention = null)
        {
            if (templates == null || templates.Count == 0)
            {
                return "(no templates to render)";
            }

            retention ??= new RetentionConfig();

            bool placeFirst = cfg?.PlaceFirst ?? true;
            bool wholeSentences = cfg?.WholeSentences ?? true;
            var cfgEn = new PresentationConfig
            {
                PlaceFirst = placeFirst,
                WholeSentences = wholeSentences,
                EncyclopediaLinksEnabled = false,
                FactSeparator = ", ",
                SentenceEnd = "."
            };
            var cfgCnt = new PresentationConfig
            {
                PlaceFirst = placeFirst,
                WholeSentences = wholeSentences,
                EncyclopediaLinksEnabled = false,
                FactSeparator = "，",
                SentenceEnd = "。"
            };

            var sb = new StringBuilder();
            sb.AppendLine("=== Sample Rendering of All Templates (wholeSentences=" + (wholeSentences ? "true" : "false") + ", placeFirst=" + (placeFirst ? "true" : "false") + ") ===");
            sb.AppendLine("(no opening phrase is added here; each line is the sentence the speaker would say after it)");

            var en = new LanguageReport(isEnglish: true, enTable, cntTable, cfgEn);
            var cnt = new LanguageReport(isEnglish: false, enTable, cntTable, cfgCnt);

            foreach (var report in new[] { en, cnt })
            {
                sb.AppendLine();
                sb.AppendLine(report.IsEnglish ? "--- English (EN) ---" : "--- Traditional Chinese (CNt) ---");
                sb.AppendLine();

                foreach (var tmpl in templates)
                {
                    if (tmpl.Retired) continue;
                    foreach (var (label, shape) in TemplateVariants.AllShapes(tmpl))
                    {
                        FormatShape(sb, label, shape, retention, report);
                    }
                }
            }

            sb.AppendLine();
            AppendSummary(sb, en, "English");
            AppendSummary(sb, cnt, "Traditional Chinese");

            return sb.ToString().TrimEnd();
        }

        private static void AppendSummary(StringBuilder sb, LanguageReport r, string name)
        {
            // 收尾句不屬於任何一種消息：只講大概又少講時一律可能用到，全部算被引用、缺了就列出來
            foreach (var key in GistClosing.AllKeys())
            {
                r.Referenced.Add(key);
                if (r.ActiveTable.Get(key) == null) r.Missing.Add(key);
            }

            sb.AppendLine($"=== Sentence Combinations with _From_ Key in {name} (Count: {r.CombinationsWithFrom.Count}) ===");
            sb.AppendLine();
            sb.AppendLine($"=== Missing Sentence Keys in {name} (Count: {r.Missing.Count}) ===");
            foreach (var key in r.Missing.OrderBy(k => k, StringComparer.Ordinal))
            {
                sb.AppendLine($"  {key}");
            }
            sb.AppendLine();

            var table = r.ActiveTable;
            var unreferenced = table.Keys
                .Where(k => (k.StartsWith("VividWorld_Sentence_", StringComparison.Ordinal) || k.StartsWith("VividWorld_SelfFeeling_", StringComparison.Ordinal)
                             || k.StartsWith("VividWorld_Closing_", StringComparison.Ordinal))
                            && !r.Referenced.Contains(k))
                .OrderBy(k => k, StringComparer.Ordinal)
                .ToList();
            sb.AppendLine($"=== Unreferenced Sentence Keys in {name} (Count: {unreferenced.Count}) ===");
            foreach (var key in unreferenced)
            {
                sb.AppendLine($"  {key}");
            }
            sb.AppendLine();

            int from = 0, self = 0, onlooker = 0, feeling = 0, witness = 0, closing = 0;
            foreach (var key in table.Keys)
            {
                if (key.StartsWith("VividWorld_SelfFeeling_", StringComparison.Ordinal)) feeling++;
                else if (key.StartsWith("VividWorld_Closing_", StringComparison.Ordinal)) closing++;
                else if (key.StartsWith("VividWorld_Sentence_", StringComparison.Ordinal))
                {
                    if (key.EndsWith("_Witness", StringComparison.Ordinal)) witness++;
                    else if (key.Contains("_From_")) from++;
                    else if (key.Contains("_Self_")) self++;
                    else onlooker++;
                }
            }
            sb.AppendLine($"=== Sentence Counts in {name}: onlooker {onlooker}, eyewitness {witness}, from {from}, self {self}, self feeling {feeling}, gist closing {closing} ===");
            sb.AppendLine();
        }

        private static void FormatShape(
            StringBuilder sb,
            string label,
            EventTemplate tmpl,
            RetentionConfig retention,
            LanguageReport report)
        {
            var roles = tmpl.Roles ?? new Dictionary<string, string>();
            var participants = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var role in roles.Keys)
            {
                participants[role] = "hero_" + role.ToLowerInvariant();
            }

            var evt = new WorldEvent
            {
                EventId = "evt_sample_" + tmpl.Type,
                Type = tmpl.Type,
                Origin = tmpl.Origin ?? EventOrigin.Public,
                Participants = participants
            };

            sb.AppendLine($"[{label}]");

            var requirements = SentenceCombinationEnumerator.EnumerateRequirements(tmpl, retention);
            var fromCombinations = new HashSet<string>(StringComparer.Ordinal);

            foreach (var req in requirements)
            {
                var comb = req.Combination;
                var facts = BuildFacts(comb.Facts);

                string? speakerHeroId;
                string? sourceHeroId = null;
                string angleLabel;
                switch (req.Kind)
                {
                    case SentenceAngleKind.Self:
                        speakerHeroId = "hero_" + req.Role!.ToLowerInvariant();
                        angleLabel = report.IsEnglish ? $"Role {req.Role}" : $"當事人 {req.Role}";
                        break;
                    case SentenceAngleKind.Witness:
                        speakerHeroId = "hero_onlooker";
                        angleLabel = report.IsEnglish ? "Eyewitness" : "親眼看到";
                        break;
                    case SentenceAngleKind.From:
                        speakerHeroId = "hero_onlooker";
                        sourceHeroId = "hero_" + req.Role!.ToLowerInvariant();
                        angleLabel = report.IsEnglish ? $"Heard from {req.Role}" : $"聽{req.Role}說";
                        break;
                    default:
                        speakerHeroId = "hero_onlooker";
                        angleLabel = report.IsEnglish ? "Onlooker" : "旁人";
                        break;
                }

                var prefix = req.Kind == SentenceAngleKind.Witness
                    ? new RumorPrefix(RumorPrefixKind.Eyewitness, RumorPrefixSelector.EyewitnessTextId, RumorPrefixSelector.EyewitnessFallback)
                    : null;
                var composed = RumorTextComposer.Compose(evt, facts, report.Config, prefix: prefix, speakerHeroId: speakerHeroId, sourceHeroId: sourceHeroId);
                var result = RenderSingle(evt, tmpl.Type ?? string.Empty, composed, report);

                var table = report.ActiveTable;
                foreach (var key in composed.SentenceKeyCandidates) report.Referenced.Add(key);
                foreach (var key in composed.SelfFeelingKeyCandidates) report.Referenced.Add(key);

                bool sentenceExists = composed.SentenceKeyCandidates.Any(k => table.Get(k) != null);
                if (!sentenceExists && !req.IsOptionalKey && composed.SentenceKeyCandidates.Count > 0)
                {
                    // 來源是當事人的第 1 手：不帶尾巴的句子也可以頂（_From_ 是選用的）；
                    // 只有 _From_ 一種講法可能出現時，缺的就是 _From_ 那一句
                    report.Missing.Add(composed.SentenceKeyCandidates[0]);
                }

                if (req.Kind == SentenceAngleKind.From && composed.SentenceKeyCandidates.Count > 1
                    && table.Get(composed.SentenceKeyCandidates[0]) != null)
                {
                    fromCombinations.Add(comb.CombinationKey);
                }

                bool feelingMissing = false;
                if (req.Kind == SentenceAngleKind.Self && composed.SelfFeelingKeyCandidates.Count > 0
                    && !composed.SelfFeelingKeyCandidates.Any(k => table.Get(k) != null))
                {
                    report.Missing.Add(composed.SelfFeelingKeyCandidates[composed.SelfFeelingKeyCandidates.Count - 1]);
                    feelingMissing = true;
                }

                string tag = report.IsEnglish
                    ? (result.UsedWholeSentence ? "[Whole]" : "[Concat]")
                    : (result.UsedWholeSentence ? "[整句]" : "[拼接]");
                string note = feelingMissing ? (report.IsEnglish ? " (no self feeling)" : "（沒有句尾）") : string.Empty;

                if (report.IsEnglish)
                {
                    sb.AppendLine($"  {tag} {angleLabel} ({comb.CombinationKey}): {result.PlainText}{note}");
                }
                else
                {
                    sb.AppendLine($"  {tag} {angleLabel}（{comb.CombinationKey}）：{result.PlainText}{note}");
                }

                // 當事人句尾的個性版本：每個宣告的傾向各列一行（只在用整句、且有句尾時才有意義）
                if (req.Kind == SentenceAngleKind.Self && composed.SelfFeelingKeyCandidates.Count > 0 && result.UsedWholeSentence)
                {
                    var variantRules = SelfFeelingVariantSelector.RulesFor(tmpl, req.Role);
                    if (variantRules != null)
                    {
                        foreach (var rule in variantRules)
                        {
                            var variantComposed = RumorTextComposer.Compose(evt, facts, report.Config, prefix: prefix, speakerHeroId: speakerHeroId, sourceHeroId: sourceHeroId);
                            var keys = SelfFeelingVariantSelector.InsertVariantKey(variantComposed.SelfFeelingKeyCandidates, rule.Tendency);
                            string variantKey = keys[keys.Count - 2];
                            variantComposed.SelfFeelingKeyCandidates = keys;
                            report.Referenced.Add(variantKey);

                            bool variantExists = table.Get(variantKey) != null;
                            if (!variantExists) report.Missing.Add(variantKey);
                            var variantResult = RenderSingle(evt, tmpl.Type ?? string.Empty, variantComposed, report);
                            string variantNote = variantExists ? string.Empty : (report.IsEnglish ? " (no string, falls back to the default line)" : "（沒有字串，退回預設句尾）");
                            if (report.IsEnglish)
                            {
                                sb.AppendLine($"    <variant {rule.Tendency}: {rule.Describe()}> {variantResult.PlainText}{variantNote}");
                            }
                            else
                            {
                                sb.AppendLine($"    【句尾版本 {rule.Tendency}：{rule.Describe()}】{variantResult.PlainText}{variantNote}");
                            }
                        }
                    }
                }
            }

            foreach (var key in fromCombinations)
            {
                report.CombinationsWithFrom.Add($"{label}|{key}");
            }

            sb.AppendLine();
        }

        private static List<Fact> BuildFacts(IReadOnlyList<TemplateFact> templateFacts)
        {
            var facts = new List<Fact>();
            foreach (var tf in templateFacts)
            {
                var vars = new Dictionary<string, string>(StringComparer.Ordinal);
                if (tf.Vars != null)
                {
                    foreach (var kvp in tf.Vars)
                    {
                        string val = kvp.Value;
                        if (val.StartsWith("hero:{", StringComparison.Ordinal) && val.EndsWith("}", StringComparison.Ordinal))
                        {
                            string rolePlaceholder = val.Substring(6, val.Length - 7);
                            val = "hero:hero_" + rolePlaceholder.ToLowerInvariant();
                        }
                        else if (val.StartsWith("settlement:{", StringComparison.Ordinal) && val.EndsWith("}", StringComparison.Ordinal))
                        {
                            val = "settlement:settlement_praven";
                        }
                        else if (string.Equals(val, "{BANDITS}", StringComparison.OrdinalIgnoreCase))
                        {
                            val = "sample:bandits";
                        }
                        vars[kvp.Key] = val;
                    }
                }

                facts.Add(new Fact
                {
                    Id = tf.Id,
                    Category = tf.Category,
                    TextId = tf.TextId ?? string.Empty,
                    Text = tf.Text,
                    Vars = vars,
                    Fragility = tf.Fragility
                });
            }
            return facts;
        }

        private static RumorRenderResult RenderSingle(
            WorldEvent evt,
            string type,
            ComposedRumor composed,
            LanguageReport report)
        {
            bool isEnglish = report.IsEnglish;
            var enTable = report.EnTable;
            var cntTable = report.CntTable;

            string ResolveVar(string val, bool useLinks)
            {
                if (string.IsNullOrEmpty(val)) return string.Empty;
                int colon = val.IndexOf(':');
                if (colon >= 0)
                {
                    string prefix = val.Substring(0, colon);
                    string payload = val.Substring(colon + 1);

                    if (string.Equals(prefix, "settlement", StringComparison.OrdinalIgnoreCase))
                    {
                        return isEnglish ? "Pravend" : "帕拉汶德";
                    }
                    if (string.Equals(prefix, "sample", StringComparison.OrdinalIgnoreCase))
                    {
                        return isEnglish ? "bandits" : "山賊";
                    }
                    if (string.Equals(prefix, "key", StringComparison.OrdinalIgnoreCase))
                    {
                        return isEnglish
                            ? enTable.GetWithFallback(payload, payload)
                            : cntTable.GetWithFallback(payload, payload);
                    }
                    if (string.Equals(prefix, "hero", StringComparison.OrdinalIgnoreCase))
                    {
                        string id = payload;
                        if (id.StartsWith("hero_", StringComparison.OrdinalIgnoreCase))
                        {
                            var hero = HeroFor(type, id.Substring(5));
                            return isEnglish ? hero.En : hero.Cnt;
                        }
                        return id;
                    }
                    return payload.Trim('{', '}');
                }
                return val.Trim('{', '}');
            }

            string GetTemplate(string? textId, string? fallback)
            {
                if (isEnglish)
                {
                    return fallback == null ? (enTable.Get(textId) ?? string.Empty) : enTable.Lookup(textId, fallback);
                }
                return fallback == null ? (cntTable.Get(textId) ?? string.Empty) : cntTable.Lookup(textId, fallback);
            }

            string GetLocalized(string? key, string fallback)
            {
                if (isEnglish)
                {
                    return enTable.GetWithFallback(key, fallback);
                }
                return cntTable.GetWithFallback(key, fallback);
            }

            bool? IsFemale(string heroId)
            {
                if (heroId.StartsWith("hero_", StringComparison.OrdinalIgnoreCase))
                {
                    return HeroFor(type, heroId.Substring(5)).Female;
                }
                return null;
            }

            return RumorTextAssembler.Assemble(
                composed,
                report.Config,
                ResolveVar,
                GetTemplate,
                GetLocalized,
                onWarning: null,
                isFemale: IsFemale);
        }

        private sealed class LanguageReport
        {
            public bool IsEnglish { get; }
            public EnglishStringTable EnTable { get; }
            public EnglishStringTable CntTable { get; }
            public PresentationConfig Config { get; }
            public EnglishStringTable ActiveTable => IsEnglish ? EnTable : CntTable;
            public HashSet<string> Missing { get; } = new(StringComparer.Ordinal);
            public HashSet<string> Referenced { get; } = new(StringComparer.Ordinal);
            public HashSet<string> CombinationsWithFrom { get; } = new(StringComparer.Ordinal);

            public LanguageReport(bool isEnglish, EnglishStringTable en, EnglishStringTable cnt, PresentationConfig config)
            {
                IsEnglish = isEnglish;
                EnTable = en;
                CntTable = cnt;
                Config = config;
            }
        }
    }
}
