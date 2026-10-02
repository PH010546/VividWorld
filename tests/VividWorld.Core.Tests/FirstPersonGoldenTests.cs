#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json.Linq;
using VividWorld.Core.Catalog;
using VividWorld.Core.Config;
using VividWorld.Core.Events;
using VividWorld.Core.Presentation;
using Xunit;

namespace VividWorld.Core.Tests
{
    public class FirstPersonGoldenTests
    {
        private static string FindRepoRoot()
        {
            string? current = AppContext.BaseDirectory;
            while (!string.IsNullOrEmpty(current))
            {
                if (File.Exists(Path.Combine(current, "VividWorld.sln")))
                {
                    return current;
                }
                var parent = Directory.GetParent(current);
                if (parent == null) break;
                current = parent.FullName;
            }
            throw new DirectoryNotFoundException("Could not find repository root containing VividWorld.sln");
        }

        private static List<EventTemplate> LoadShippingTemplates(string repoRoot)
        {
            var result = new List<EventTemplate>();
            var pCfg = new PersistenceConfig();
            string eventsPath = Path.Combine(repoRoot, "module", "ModuleData", "vividworld_events.json");
            string sitEventsPath = Path.Combine(repoRoot, "module", "ModuleData", "vividworld_situation_events.json");

            var catEvents = EventCatalogLoader.Load(File.ReadAllText(eventsPath), pCfg);
            var catSit = EventCatalogLoader.Load(File.ReadAllText(sitEventsPath), pCfg);

            result.AddRange(catEvents.Templates);
            result.AddRange(catSit.Templates);
            return result;
        }

        public static string GenerateGoldenContent(List<string> warnings)
        {
            string repoRoot = FindRepoRoot();
            var templates = LoadShippingTemplates(repoRoot);

            string enPath = Path.Combine(repoRoot, "module", "ModuleData", "Languages", "std_module_strings_xml.xml");
            string cntPath = Path.Combine(repoRoot, "module", "ModuleData", "Languages", "CNt", "std_module_strings_xml.xml");

            var enTable = EnglishStringTable.LoadFromFile(enPath);
            var cntTable = EnglishStringTable.LoadFromFile(cntPath);

            var cfg = new PresentationConfig
            {
                EncyclopediaLinksEnabled = false,
                FactSeparator = ", ",
                SentenceEnd = string.Empty
            };

            string ResolveVarEn(string val, bool useLinks)
            {
                if (string.IsNullOrEmpty(val)) return string.Empty;
                int colon = val.IndexOf(':');
                if (colon >= 0)
                {
                    string prefix = val.Substring(0, colon);
                    string payload = val.Substring(colon + 1);
                    if (string.Equals(prefix, "key", StringComparison.OrdinalIgnoreCase))
                    {
                        return enTable.GetWithFallback(payload, payload);
                    }
                    return payload.Trim('{', '}');
                }
                return val.Trim('{', '}');
            }

            string ResolveVarCnt(string val, bool useLinks)
            {
                if (string.IsNullOrEmpty(val)) return string.Empty;
                int colon = val.IndexOf(':');
                if (colon >= 0)
                {
                    string prefix = val.Substring(0, colon);
                    string payload = val.Substring(colon + 1);
                    if (string.Equals(prefix, "key", StringComparison.OrdinalIgnoreCase))
                    {
                        return cntTable.GetWithFallback(payload, payload);
                    }
                    return payload.Trim('{', '}');
                }
                return val.Trim('{', '}');
            }

            string GetTemplateEn(string? textId, string? fallback) =>
                fallback == null ? (enTable.Get(textId) ?? string.Empty) : enTable.Lookup(textId, fallback);

            string GetTemplateCnt(string? textId, string? fallback) =>
                fallback == null ? (cntTable.Get(textId) ?? string.Empty) : cntTable.Lookup(textId, fallback);

            string GetLocalizedEn(string? key, string fallback) => enTable.GetWithFallback(key, fallback);
            string GetLocalizedCnt(string? key, string fallback) => cntTable.GetWithFallback(key, fallback);

            var sb = new StringBuilder();
            sb.AppendLine("# Vivid World - First-Person Golden File");
            sb.AppendLine("# Generated from shipped templates (vividworld_events.json & vividworld_situation_events.json)");
            sb.AppendLine("# Format: [template_type | role | fact_id]");
            sb.AppendLine("# EN:  <first_person_english>");
            sb.AppendLine("# CNT: <first_person_cnt>");
            sb.AppendLine();

            foreach (var template in templates)
            {
                // 卡片明訂：除只有死者一個角色的 hero_died_naturally 之外全部涵蓋
                if (string.Equals(template.Type, "hero_died_naturally", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(template.Type, "hero_died_of_old_age", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(template.Type, "hero_died_in_labor", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                // 停用的事件型別不再由任何人講述
                if (template.Retired)
                {
                    continue;
                }

                var roles = template.Roles ?? new Dictionary<string, string>();
                foreach (var roleKvp in roles)
                {
                    string roleName = roleKvp.Key;
                    string speakerHeroId = $"hero_{roleName}";

                    var evt = new WorldEvent
                    {
                        EventId = $"evt_golden_{template.Type}",
                        Type = template.Type,
                        Origin = template.Origin ?? EventOrigin.Public,
                        Participants = new Dictionary<string, string>(StringComparer.Ordinal)
                    };

                    foreach (var r in roles.Keys)
                    {
                        evt.Participants[r] = $"hero_{r}";
                    }

                    foreach (var tf in template.Facts)
                    {
                        var fact = new Fact
                        {
                            Id = tf.Id,
                            Category = tf.Category,
                            TextId = tf.TextId ?? string.Empty,
                            Text = tf.Text,
                            Vars = tf.Vars != null ? new Dictionary<string, string>(tf.Vars) : new Dictionary<string, string>(),
                            Fragility = tf.Fragility
                        };

                        var composed = RumorTextComposer.Compose(
                            evt,
                            new[] { fact },
                            cfg,
                            prefix: null,
                            speakerHeroId: speakerHeroId);

                        var resEn = RumorTextAssembler.Assemble(
                            composed,
                            cfg,
                            ResolveVarEn,
                            GetTemplateEn,
                            GetLocalizedEn,
                            warnings.Add);

                        var resCnt = RumorTextAssembler.Assemble(
                            composed,
                            cfg,
                            ResolveVarCnt,
                            GetTemplateCnt,
                            GetLocalizedCnt,
                            warnings.Add);

                        sb.AppendLine($"[{template.Type} | {roleName} | {fact.Id}]");
                        sb.AppendLine($"EN:  {resEn.PlainText}");
                        sb.AppendLine($"CNT: {resCnt.PlainText}");
                        sb.AppendLine();
                    }
                }
            }

            return sb.ToString().Replace("\r\n", "\n");
        }

        [Fact]
        public void FirstPersonGolden_MatchesExpectedFile()
        {
            string repoRoot = FindRepoRoot();
            string goldenPath = Path.Combine(repoRoot, "tests", "VividWorld.Core.Tests", "GoldenFiles", "first_person_golden.txt");

            var warnings = new List<string>();
            string actual = GenerateGoldenContent(warnings);

            // 代換不掉的佔位符會被印成「某個人」而不報錯；黃金檔裡不准有任何一條
            Assert.Empty(warnings);

            if (!File.Exists(goldenPath))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(goldenPath)!);
                File.WriteAllText(goldenPath, actual, Encoding.UTF8);
            }

            string expected = File.ReadAllText(goldenPath, Encoding.UTF8).Replace("\r\n", "\n");
            Assert.Equal(expected, actual);
        }

        [Fact]
        public void FirstPerson_DisplayVersion_DoesNotWrapFirstPersonInLink()
        {
            string repoRoot = FindRepoRoot();
            string eventsPath = Path.Combine(repoRoot, "module", "ModuleData", "vividworld_events.json");
            var catEvents = EventCatalogLoader.Load(File.ReadAllText(eventsPath), new PersistenceConfig());
            var tmpl = catEvents.Templates.First(t => t.Type == "hero_taken_prisoner");
            var tf = tmpl.Facts.First(f => f.Id == "who");
            var fact = new Fact
            {
                Id = tf.Id,
                Category = tf.Category,
                TextId = tf.TextId ?? string.Empty,
                Text = tf.Text,
                Vars = tf.Vars != null ? new Dictionary<string, string>(tf.Vars) : new Dictionary<string, string>(),
                Fragility = tf.Fragility
            };

            var evt = new WorldEvent
            {
                EventId = "evt_prisoner",
                Type = "hero_taken_prisoner",
                Origin = EventOrigin.Public,
                Participants = new Dictionary<string, string>
                {
                    ["captor"] = "hero_captor",
                    ["prisoner"] = "hero_prisoner"
                }
            };

            var cfg = new PresentationConfig
            {
                EncyclopediaLinksEnabled = true,
                FactSeparator = ", ",
                SentenceEnd = ""
            };

            string ResolveVarWithLinks(string val, bool useLinks)
            {
                if (val.IndexOf("captor", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return useLinks ? "<a href=\"event:captor\"><b>Captor</b></a>" : "Captor";
                }
                return val;
            }

            string GetTemplate(string? id, string? fb) => fb ?? string.Empty;
            string GetLocalized(string? k, string fb) => fb;

            // Speaker is prisoner (object pronoun "me")
            var composed = RumorTextComposer.Compose(evt, new[] { fact }, cfg, prefix: null, speakerHeroId: "hero_prisoner");
            var res = RumorTextAssembler.Assemble(composed, cfg, ResolveVarWithLinks, GetTemplate, GetLocalized, null);

            // Display text should have link for captor, but "me" is plain text
            Assert.Contains("<a href=\"event:captor\"><b>Captor</b></a>", res.DisplayText);
            Assert.Contains(" captured me as a prisoner of war", res.DisplayText);
            Assert.DoesNotContain("<a", res.PlainText);
        }
    }
}
