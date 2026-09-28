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
    /// Formats sample renderings for all catalog templates across hop levels (0, 1, 2)
    /// and participant roles in both English and Traditional Chinese.
    /// Used for dev diagnostic verification of sentence flow, capitalization, and place-first order.
    /// </summary>
    public static class TemplateRenderSampleFormatter
    {
        private static readonly Dictionary<string, (string En, string Cnt)> RoleHeroNames =
            new(StringComparer.OrdinalIgnoreCase)
            {
                ["victim"] = ("Derthert", "德瑟特"),
                ["killer"] = ("Caladog", "卡拉多格"),
                ["executor"] = ("Caladog", "卡拉多格"),
                ["betrayer"] = ("Caladog", "卡拉多格"),
                ["betrayed"] = ("Derthert", "德瑟特"),
                ["deceased"] = ("Derthert", "德瑟特"),
                ["defeated"] = ("Derthert", "德瑟特"),
                ["victor"] = ("Caladog", "卡拉多格"),
                ["prisoner"] = ("Derthert", "德瑟特"),
                ["captor"] = ("Caladog", "卡拉多格"),
                ["rescuer"] = ("Caladog", "卡拉多格"),
                ["ransomer"] = ("Caladog", "卡拉多格"),
                ["ransomed"] = ("Derthert", "德瑟特"),
                ["slighted"] = ("Derthert", "德瑟特"),
                ["favored"] = ("Caladog", "卡拉多格"),
                ["aggrieved"] = ("Derthert", "德瑟特"),
                ["patron"] = ("Caladog", "卡拉多格"),
                ["accused"] = ("Caladog", "卡拉多格"),
                ["offender"] = ("Caladog", "卡拉多格"),
                ["defender"] = ("Derthert", "德瑟特"),
                ["claimant"] = ("Derthert", "德瑟特"),
                ["challenger"] = ("Derthert", "德瑟特"),
                ["challenged"] = ("Caladog", "卡拉多格"),
                ["target"] = ("Caladog", "卡拉多格"),
                ["host"] = ("Unqid", "烏克齊德"),
                ["caller"] = ("Derthert", "德瑟特"),
                ["student"] = ("Derthert", "德瑟特"),
                ["master"] = ("Caladog", "卡拉多格"),
                ["supporter"] = ("Unqid", "烏克齊德"),
                ["giver"] = ("Derthert", "德瑟特"),
                ["recip"] = ("Caladog", "卡拉多格"),
                ["producer"] = ("Derthert", "德瑟特"),
                ["merchant"] = ("Caladog", "卡拉多格"),
                ["onlooker"] = ("Garios", "加里奧斯")
            };

        public static string FormatAll(
            IReadOnlyList<EventTemplate> templates,
            EnglishStringTable enTable,
            EnglishStringTable cntTable,
            PresentationConfig? cfg = null)
        {
            if (templates == null || templates.Count == 0)
            {
                return "(no templates to render)";
            }

            bool placeFirst = cfg?.PlaceFirst ?? true;
            var cfgEn = new PresentationConfig
            {
                PlaceFirst = placeFirst,
                EncyclopediaLinksEnabled = false,
                FactSeparator = ", ",
                SentenceEnd = "."
            };
            var cfgCnt = new PresentationConfig
            {
                PlaceFirst = placeFirst,
                EncyclopediaLinksEnabled = false,
                FactSeparator = "，",
                SentenceEnd = "。"
            };

            var sb = new StringBuilder();
            sb.AppendLine("=== Sample Rendering of All Templates (placeFirst=" + (placeFirst ? "true" : "false") + ") ===");
            sb.AppendLine();
            sb.AppendLine("--- English (EN) ---");
            sb.AppendLine();

            foreach (var tmpl in templates)
            {
                if (tmpl.Retired) continue;
                FormatTemplate(sb, tmpl, enTable, cntTable, cfgEn, isEnglish: true);
            }

            sb.AppendLine();
            sb.AppendLine("--- Traditional Chinese (CNt) ---");
            sb.AppendLine();

            foreach (var tmpl in templates)
            {
                if (tmpl.Retired) continue;
                FormatTemplate(sb, tmpl, enTable, cntTable, cfgCnt, isEnglish: false);
            }

            return sb.ToString().TrimEnd();
        }

        private static void FormatTemplate(
            StringBuilder sb,
            EventTemplate tmpl,
            EnglishStringTable enTable,
            EnglishStringTable cntTable,
            PresentationConfig cfg,
            bool isEnglish)
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

            var allFacts = new List<Fact>();
            foreach (var tf in tmpl.Facts)
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
                            val = "key:VividWorld_UnknownBandits";
                        }
                        vars[kvp.Key] = val;
                    }
                }

                allFacts.Add(new Fact
                {
                    Id = tf.Id,
                    Category = tf.Category,
                    TextId = tf.TextId ?? string.Empty,
                    Text = tf.Text,
                    Vars = vars,
                    Fragility = tf.Fragility
                });
            }

            // 按 fragility 排序以取得 hop 0/1/2 碎片子集
            var sortedByFragility = allFacts.OrderBy(f => f.Fragility).ToList();
            var hop0Facts = allFacts; // 4 段（全部）
            var hop1Facts = sortedByFragility.Take(Math.Min(3, sortedByFragility.Count)).ToList(); // 3 段
            var hop2Facts = sortedByFragility.Take(Math.Min(2, sortedByFragility.Count)).ToList(); // 2 段

            sb.AppendLine($"[{tmpl.Type}]");

            // 旁人 (Onlooker)
            string hop0Text = RenderSingle(evt, hop0Facts, cfg, enTable, cntTable, isEnglish, speakerHeroId: "hero_onlooker");
            string hop1Text = RenderSingle(evt, hop1Facts, cfg, enTable, cntTable, isEnglish, speakerHeroId: "hero_onlooker");
            string hop2Text = RenderSingle(evt, hop2Facts, cfg, enTable, cntTable, isEnglish, speakerHeroId: "hero_onlooker");

            if (isEnglish)
            {
                sb.AppendLine($"  Onlooker Hop 0 (4 facts): {hop0Text}");
                sb.AppendLine($"  Onlooker Hop 1 (3 facts): {hop1Text}");
                sb.AppendLine($"  Onlooker Hop 2 (2 facts): {hop2Text}");
            }
            else
            {
                sb.AppendLine($"  旁人第 0 手（四段）：{hop0Text}");
                sb.AppendLine($"  旁人第 1 手（三段）：{hop1Text}");
                sb.AppendLine($"  旁人第 2 手（兩段）：{hop2Text}");
            }

            // 有當事人句的角色再以該角色第 0 手渲染一次
            foreach (var role in roles.Keys)
            {
                string roleUpper = role.ToUpperInvariant();
                bool hasSelfLine = tmpl.Facts.Any(f =>
                    !string.IsNullOrEmpty(f.TextId) &&
                    (enTable.Get(f.TextId + "_Self_" + roleUpper) != null ||
                     cntTable.Get(f.TextId + "_Self_" + roleUpper) != null));

                if (hasSelfLine)
                {
                    string speakerHeroId = "hero_" + role.ToLowerInvariant();
                    string selfText = RenderSingle(evt, hop0Facts, cfg, enTable, cntTable, isEnglish, speakerHeroId: speakerHeroId);
                    if (isEnglish)
                    {
                        sb.AppendLine($"  Role {role} Hop 0 (4 facts): {selfText}");
                    }
                    else
                    {
                        sb.AppendLine($"  當事人 {role} 第 0 手（四段）：{selfText}");
                    }
                }
            }

            sb.AppendLine();
        }

        private static string RenderSingle(
            WorldEvent evt,
            IReadOnlyList<Fact> facts,
            PresentationConfig cfg,
            EnglishStringTable enTable,
            EnglishStringTable cntTable,
            bool isEnglish,
            string speakerHeroId)
        {
            var composed = RumorTextComposer.Compose(
                evt,
                facts,
                cfg,
                prefix: null,
                speakerHeroId: speakerHeroId);

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
                        return isEnglish ? "Pravend" : "帕拉文德";
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
                            string roleKey = id.Substring(5);
                            if (RoleHeroNames.TryGetValue(roleKey, out var names))
                            {
                                return isEnglish ? names.En : names.Cnt;
                            }
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

            var result = RumorTextAssembler.Assemble(
                composed,
                cfg,
                ResolveVar,
                GetTemplate,
                GetLocalized,
                onWarning: null);

            return result.PlainText;
        }
    }
}
