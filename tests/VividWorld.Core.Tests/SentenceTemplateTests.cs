using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using VividWorld.Core.Catalog;
using VividWorld.Core.Config;
using VividWorld.Core.Events;
using VividWorld.Core.Presentation;
using Xunit;

namespace VividWorld.Core.Tests
{
    public class SentenceTemplateTests
    {
        private static string ResolveVarPassthrough(string val, bool useLinks) => val;

        private static WorldEvent CreateSampleEvent(string eventType, Dictionary<string, string> participants, List<Fact> facts)
        {
            return new WorldEvent
            {
                EventId = "evt_test_1",
                Type = eventType,
                Day = 10,
                Participants = participants,
                Facts = facts
            };
        }

        [Fact]
        public void Assemble_WhenWholeSentenceKeyExists_UsesWholeSentence()
        {
            var cfg = new PresentationConfig { WholeSentences = true };
            var facts = new List<Fact>
            {
                new Fact
                {
                    TextId = "VividWorld_Fact_HeroEscaped_Who",
                    Vars = new Dictionary<string, string> { ["PRISONER"] = "Corein", ["CAPTOR"] = "Caladog" }
                },
                new Fact
                {
                    TextId = "VividWorld_Fact_HeroEscaped_Where",
                    Vars = new Dictionary<string, string> { ["SETTLEMENT"] = "Marunath" }
                }
            };
            var evt = CreateSampleEvent(
                "hero_escaped_captivity",
                new Dictionary<string, string> { ["PRISONER"] = "hero_1", ["CAPTOR"] = "hero_2" },
                facts);

            var composed = RumorTextComposer.Compose(evt, facts, cfg, null);

            var templates = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["VividWorld_Sentence_HeroEscaped_Who_Where"] = "Near {SETTLEMENT}, {PRISONER} slipped out of {CAPTOR}'s custody"
            };

            var result = RumorTextAssembler.Assemble(
                composed,
                cfg,
                ResolveVarPassthrough,
                getTemplate: (key, fallback) => key != null && templates.TryGetValue(key, out var t) ? t : fallback ?? string.Empty);

            Assert.True(result.UsedWholeSentence);
            Assert.Equal("VividWorld_Sentence_HeroEscaped_Who_Where", result.SentenceKeyUsed);
            Assert.Equal("Near Marunath, Corein slipped out of Caladog's custody.", result.DisplayText);
        }

        [Fact]
        public void Assemble_WhenWholeSentenceKeyMissing_FallsBackToConcatenation()
        {
            var cfg = new PresentationConfig { WholeSentences = true };
            var facts = new List<Fact>
            {
                new Fact
                {
                    TextId = "VividWorld_Fact_HeroEscaped_Who",
                    Text = "{PRISONER} slipped out of {CAPTOR}'s custody",
                    Vars = new Dictionary<string, string> { ["PRISONER"] = "Corein", ["CAPTOR"] = "Caladog" }
                },
                new Fact
                {
                    TextId = "VividWorld_Fact_HeroEscaped_Where",
                    Text = "near {SETTLEMENT}",
                    Vars = new Dictionary<string, string> { ["SETTLEMENT"] = "Marunath" }
                }
            };
            var evt = CreateSampleEvent(
                "hero_escaped_captivity",
                new Dictionary<string, string> { ["PRISONER"] = "hero_1", ["CAPTOR"] = "hero_2" },
                facts);

            var composed = RumorTextComposer.Compose(evt, facts, cfg, null);

            // getTemplate 傳回 null，代表字典中缺少該候選整句鍵
            var result = RumorTextAssembler.Assemble(
                composed,
                cfg,
                ResolveVarPassthrough,
                getTemplate: (key, fallback) => fallback ?? string.Empty);

            Assert.False(result.UsedWholeSentence);
            Assert.Null(result.SentenceKeyUsed);
            Assert.Equal("VividWorld_Sentence_HeroEscaped_Who_Where", result.SentenceKeyCandidate);
            // 應退回為碎片拼接
            Assert.Contains("Corein", result.DisplayText);
            Assert.Contains("Marunath", result.DisplayText);
        }

        [Fact]
        public void Assemble_ParticipantSpeaking_UsesSelfSentenceTemplate()
        {
            var cfg = new PresentationConfig { WholeSentences = true };
            var facts = new List<Fact>
            {
                new Fact
                {
                    TextId = "VividWorld_Fact_HeroEscaped_Who",
                    Vars = new Dictionary<string, string> { ["PRISONER"] = "Corein", ["CAPTOR"] = "Caladog" }
                },
                new Fact
                {
                    TextId = "VividWorld_Fact_HeroEscaped_Where",
                    Vars = new Dictionary<string, string> { ["SETTLEMENT"] = "Marunath" }
                }
            };
            var evt = CreateSampleEvent(
                "hero_escaped_captivity",
                new Dictionary<string, string> { ["PRISONER"] = "hero_1", ["CAPTOR"] = "hero_2" },
                facts);

            // 講述者為當事人 PRISONER
            var composed = RumorTextComposer.Compose(evt, facts, cfg, null, speakerHeroId: "hero_1");

            Assert.Equal("VividWorld_Sentence_HeroEscaped_Who_Where_Self_PRISONER", composed.SentenceKeyCandidate);

            var templates = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["VividWorld_Sentence_HeroEscaped_Who_Where_Self_PRISONER"] = "Near {SETTLEMENT}, I got away from {CAPTOR}"
            };

            var result = RumorTextAssembler.Assemble(
                composed,
                cfg,
                ResolveVarPassthrough,
                getTemplate: (key, fallback) => key != null && templates.TryGetValue(key, out var t) ? t : fallback ?? string.Empty);

            Assert.True(result.UsedWholeSentence);
            Assert.Equal("VividWorld_Sentence_HeroEscaped_Who_Where_Self_PRISONER", result.SentenceKeyUsed);
            Assert.Equal("Near Marunath, I got away from Caladog.", result.DisplayText);
        }

        [Fact]
        public void Compose_WhoNoCaptorVariant_ProducesCorrectSentenceKey()
        {
            var cfg = new PresentationConfig { WholeSentences = true };
            var facts = new List<Fact>
            {
                new Fact
                {
                    TextId = "VividWorld_Fact_HeroEscaped_WhoNoCaptor",
                    Vars = new Dictionary<string, string> { ["PRISONER"] = "Corein" }
                },
                new Fact
                {
                    TextId = "VividWorld_Fact_HeroEscaped_Where",
                    Vars = new Dictionary<string, string> { ["SETTLEMENT"] = "Marunath" }
                }
            };
            var evt = CreateSampleEvent(
                "hero_escaped_captivity",
                new Dictionary<string, string> { ["PRISONER"] = "hero_1" },
                facts);

            var composed = RumorTextComposer.Compose(evt, facts, cfg, null);

            Assert.Equal("VividWorld_Sentence_HeroEscaped_WhoNoCaptor_Where", composed.SentenceKeyCandidate);
        }

        [Fact]
        public void Assemble_WithPrefix_AttachesPrefixBeforeWholeSentence()
        {
            var cfg = new PresentationConfig { WholeSentences = true };
            var facts = new List<Fact>
            {
                new Fact
                {
                    TextId = "VividWorld_Fact_HeroEscaped_Who",
                    Vars = new Dictionary<string, string> { ["PRISONER"] = "Corein", ["CAPTOR"] = "Caladog" }
                }
            };
            var evt = CreateSampleEvent(
                "hero_escaped_captivity",
                new Dictionary<string, string> { ["PRISONER"] = "hero_1", ["CAPTOR"] = "hero_2" },
                facts);

            var prefix = new RumorPrefix(RumorPrefixKind.Retell, "VividWorld_RetellPrefix", "I was there, in fact—", null);
            var composed = RumorTextComposer.Compose(evt, facts, cfg, prefix);

            var templates = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["VividWorld_Sentence_HeroEscaped_Who"] = "{PRISONER} slipped out of {CAPTOR}'s custody"
            };

            var result = RumorTextAssembler.Assemble(
                composed,
                cfg,
                ResolveVarPassthrough,
                getTemplate: (key, fallback) => key != null && templates.TryGetValue(key, out var t) ? t : fallback ?? string.Empty);

            Assert.True(result.UsedWholeSentence);
            Assert.StartsWith("I was there, in fact—", result.DisplayText);
            Assert.Contains("Corein slipped out of Caladog's custody.", result.DisplayText);
        }

        [Fact]
        public void Assemble_WhenWholeSentencesDisabled_FallsBackToConcatenation()
        {
            var cfg = new PresentationConfig { WholeSentences = false };
            var facts = new List<Fact>
            {
                new Fact
                {
                    TextId = "VividWorld_Fact_HeroEscaped_Who",
                    Vars = new Dictionary<string, string> { ["PRISONER"] = "Corein", ["CAPTOR"] = "Caladog" }
                },
                new Fact
                {
                    TextId = "VividWorld_Fact_HeroEscaped_Where",
                    Vars = new Dictionary<string, string> { ["SETTLEMENT"] = "Marunath" }
                }
            };
            var evt = CreateSampleEvent(
                "hero_escaped_captivity",
                new Dictionary<string, string> { ["PRISONER"] = "hero_1", ["CAPTOR"] = "hero_2" },
                facts);

            var composed = RumorTextComposer.Compose(evt, facts, cfg, null);

            var templates = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["VividWorld_Sentence_HeroEscaped_Who_Where"] = "Near {SETTLEMENT}, {PRISONER} slipped out of {CAPTOR}'s custody"
            };

            var result = RumorTextAssembler.Assemble(
                composed,
                cfg,
                ResolveVarPassthrough,
                getTemplate: (key, fallback) => key != null && templates.TryGetValue(key, out var t) ? t : fallback ?? string.Empty);

            Assert.False(result.UsedWholeSentence);
            Assert.Null(result.SentenceKeyUsed);
        }

        [Fact]
        public void EnumerateCombinations_IncludesOptionalFactsPresentAndAbsent()
        {
            var template = new EventTemplate
            {
                Type = "test_template",
                DramaWeight = 3,
                Facts = new List<TemplateFact>
                {
                    new TemplateFact { Category = FactCategory.Who, TextId = "VividWorld_Fact_HeroTakenPrisoner_Who", Fragility = 1, Optional = false },
                    new TemplateFact { Category = FactCategory.Where, TextId = "VividWorld_Fact_HeroTakenPrisoner_Where", Fragility = 4, Optional = true },
                    new TemplateFact { Category = FactCategory.What, TextId = "VividWorld_Fact_HeroTakenPrisoner_What", Fragility = 2, Optional = false },
                    new TemplateFact { Category = FactCategory.Outcome, TextId = "VividWorld_Fact_HeroTakenPrisoner_Outcome", Fragility = 5, Optional = false }
                }
            };

            var retention = new RetentionConfig();
            var combinations = SentenceCombinationEnumerator.EnumerateCombinations(template, retention);

            Assert.NotEmpty(combinations);
            Assert.Contains(combinations, c => !c.IsMissingOptional);
            Assert.Contains(combinations, c => c.IsMissingOptional);

            // 包含選填碎片（Where）的組合與排除選填碎片的組合皆存在
            Assert.Contains(combinations, c => c.CombinationKey.Contains("Where"));
            Assert.Contains(combinations, c => !c.CombinationKey.Contains("Where"));
        }

        [Fact]
        public void StringTables_AllSentenceKeys_MatchExactlyBetweenEnAndCnt()
        {
            string repoRoot = FindRepoRoot();
            string enPath = Path.Combine(repoRoot, "module", "ModuleData", "Languages", "std_module_strings_xml.xml");
            string cntPath = Path.Combine(repoRoot, "module", "ModuleData", "Languages", "CNt", "std_module_strings_xml.xml");

            Assert.True(File.Exists(enPath), $"EN table not found: {enPath}");
            Assert.True(File.Exists(cntPath), $"CNt table not found: {cntPath}");

            var enKeys = LoadSentenceKeys(enPath);
            var cntKeys = LoadSentenceKeys(cntPath);

            Assert.NotEmpty(enKeys);

            var missingInCnt = enKeys.Except(cntKeys).OrderBy(k => k, StringComparer.Ordinal).ToList();
            var missingInEn = cntKeys.Except(enKeys).OrderBy(k => k, StringComparer.Ordinal).ToList();

            Assert.Empty(missingInCnt);
            Assert.Empty(missingInEn);
        }

        [Fact]
        public void TemplateRenderSampleFormatter_ReportMissingSentenceKeysCount()
        {
            string repoRoot = FindRepoRoot();
            var pCfg = new PersistenceConfig();
            string eventsPath = Path.Combine(repoRoot, "module", "ModuleData", "vividworld_events.json");
            string sitEventsPath = Path.Combine(repoRoot, "module", "ModuleData", "vividworld_situation_events.json");
            string enPath = Path.Combine(repoRoot, "module", "ModuleData", "Languages", "std_module_strings_xml.xml");
            string cntPath = Path.Combine(repoRoot, "module", "ModuleData", "Languages", "CNt", "std_module_strings_xml.xml");

            var catEvents = EventCatalogLoader.Load(File.ReadAllText(eventsPath), pCfg);
            var catSit = EventCatalogLoader.Load(File.ReadAllText(sitEventsPath), pCfg);
            var allTemplates = new List<EventTemplate>();
            allTemplates.AddRange(catEvents.Templates);
            allTemplates.AddRange(catSit.Templates);

            var enTable = EnglishStringTable.LoadFromFile(enPath);
            var cntTable = EnglishStringTable.LoadFromFile(cntPath);

            string output = VividWorld.Core.Diagnostics.TemplateRenderSampleFormatter.FormatAll(allTemplates, enTable, cntTable);
            Assert.Contains("Missing Sentence Keys in English (Count:", output);
            Assert.Contains("Missing Sentence Keys in Traditional Chinese (Count:", output);

            var m = System.Text.RegularExpressions.Regex.Match(output, @"Missing Sentence Keys in English \(Count: (\d+)\)");
            Assert.Equal("0", m.Groups[1].Value);
            var mCnt = System.Text.RegularExpressions.Regex.Match(output, @"Missing Sentence Keys in Traditional Chinese \(Count: (\d+)\)");
            Assert.Equal("0", mCnt.Groups[1].Value);

            // 逃脫與被俘的所有句型皆被成功解析使用，而不是退回拼接
            Assert.Contains("[Whole] Onlooker (Who_Where_What_Outcome): On a night when the guards were few, Derthert escaped from Caladog in Pravend, and no one saw which way he went.", output);
            Assert.Contains("[整句] 旁人（Who_Where_What_Outcome）：德瑟特在帕拉汶德趁夜裡看守的人少，從卡拉多格手中逃了出來，誰也沒看清他往哪跑了。", output);
        }

        private static HashSet<string> LoadSentenceKeys(string filePath)
        {
            var doc = XDocument.Load(filePath);
            var set = new HashSet<string>(StringComparer.Ordinal);

            foreach (var elem in doc.Root?.Element("strings")?.Elements("string") ?? Enumerable.Empty<XElement>())
            {
                string id = elem.Attribute("id")?.Value ?? string.Empty;
                if (id.StartsWith("VividWorld_Sentence_", StringComparison.Ordinal) ||
                    id.StartsWith("VividWorld_SelfFeeling_", StringComparison.Ordinal))
                {
                    set.Add(id);
                }
            }

            return set;
        }

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

            Assert.Fail($"Could not locate repo root starting from {AppContext.BaseDirectory}");
            return string.Empty;
        }
    }
}
