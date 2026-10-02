#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using VividWorld.Core.Rumors;

namespace VividWorld.Core.Catalog
{
    /// <summary>一次句尾個性版本的判定：挑中的傾向與理由（日誌用）。</summary>
    public sealed class SelfFeelingVariantChoice
    {
        /// <summary>挑中的傾向；null 表示用不帶傾向的句尾。</summary>
        public string? Tendency;

        /// <summary>一行日誌：當事人相關的特質值，與挑中哪個傾向、或為什麼用預設句尾。</summary>
        public string Log = string.Empty;
    }

    /// <summary>依當事人的特質，從模板宣告的個性版本裡挑一個；宣告順序就是優先順序，第一個符合的勝出。</summary>
    public static class SelfFeelingVariantSelector
    {
        public static SelfFeelingVariantChoice Choose(EventTemplate? template, string? role, TraitProfile? traits)
        {
            var rules = RulesFor(template, role);
            if (rules == null || rules.Count == 0)
            {
                return new SelfFeelingVariantChoice { Log = "no personality variants declared for this role, default line" };
            }

            if (traits == null)
            {
                return new SelfFeelingVariantChoice { Log = "speaker traits unknown, default line" };
            }

            var shown = string.Join(", ", rules.Select(r => r.Trait.ToLowerInvariant()).Distinct()
                .Select(t => $"{t}={TraitValue(traits, t)}"));

            foreach (var rule in rules)
            {
                if (Matches(rule, traits))
                {
                    return new SelfFeelingVariantChoice
                    {
                        Tendency = rule.Tendency,
                        Log = $"traits {shown}; chose '{rule.Tendency}' ({rule.Describe()}, first match in declared order)"
                    };
                }
            }

            var tried = string.Join("; ", rules.Select(r => $"{r.Tendency}: {r.Describe()}"));
            return new SelfFeelingVariantChoice { Log = $"traits {shown}; no variant matched ({tried}), default line" };
        }

        public static IReadOnlyList<SelfFeelingVariantRule>? RulesFor(EventTemplate? template, string? role)
        {
            if (template?.SelfFeelingVariants == null || string.IsNullOrEmpty(role)) return null;
            foreach (var kvp in template.SelfFeelingVariants)
            {
                if (string.Equals(kvp.Key, role, StringComparison.OrdinalIgnoreCase)) return kvp.Value;
            }
            return null;
        }

        public static bool Matches(SelfFeelingVariantRule rule, TraitProfile traits)
        {
            int v = TraitValue(traits, rule.Trait);
            if (rule.Min.HasValue) return v >= rule.Min.Value;
            if (rule.Max.HasValue) return v <= rule.Max.Value;
            return false;
        }

        public static bool IsKnownTrait(string? trait)
        {
            switch (trait?.ToLowerInvariant())
            {
                case "honor": case "mercy": case "valor": case "calculating": case "generosity": return true;
                default: return false;
            }
        }

        public static int TraitValue(TraitProfile p, string trait)
        {
            switch (trait.ToLowerInvariant())
            {
                case "honor": return p.Honor;
                case "mercy": return p.Mercy;
                case "valor": return p.Valor;
                case "calculating": return p.Calculating;
                case "generosity": return p.Generosity;
                default: return 0;
            }
        }

        /// <summary>把傾向版本的鍵插在候選鍵清單裡、不帶傾向的一般鍵之前
        /// （碎片專屬的句尾鍵仍然優先；版本的字串在當前語言缺了，照順序退回一般鍵）。</summary>
        public static IReadOnlyList<string> InsertVariantKey(IReadOnlyList<string> candidates, string tendency)
        {
            if (candidates.Count == 0) return candidates;
            var list = new List<string>(candidates);
            string general = list[list.Count - 1];
            list.Insert(list.Count - 1, general + "_" + tendency);
            return list;
        }
    }
}
