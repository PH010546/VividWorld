#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using VividWorld.Core.Catalog;
using VividWorld.Core.Config;
using VividWorld.Core.Diagnostics;
using VividWorld.Core.Events;
using VividWorld.Core.Persistence;
using VividWorld.Core.Presentation;
using Xunit;

namespace VividWorld.Core.Tests
{
    public class PlaceFirstTests
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

        [Fact]
        public void PlaceFirst_WhenTrue_WhereCategoryRankedFirst()
        {
            var cfg = new PresentationConfig
            {
                PlaceFirst = true,
                FactOrder = new[] { "WHO", "CONTEXT", "WHEN", "WHERE", "WHAT", "WHY", "OUTCOME" }
            };

            var facts = new List<Fact>
            {
                new() { Id = "who", Category = FactCategory.Who, Text = "who fact" },
                new() { Id = "where", Category = FactCategory.Where, Text = "where fact" },
                new() { Id = "what", Category = FactCategory.What, Text = "what fact" },
                new() { Id = "outcome", Category = FactCategory.Outcome, Text = "outcome fact" }
            };

            var composed = RumorTextComposer.Compose(facts, cfg);

            Assert.Equal(4, composed.Parts.Count);
            Assert.Equal("where fact", composed.Parts[0].Fallback);
            Assert.Equal("who fact", composed.Parts[1].Fallback);
            Assert.Equal("what fact", composed.Parts[2].Fallback);
            Assert.Equal("outcome fact", composed.Parts[3].Fallback);
        }

        [Fact]
        public void PlaceFirst_WhenFalse_OriginalFactOrderPreserved()
        {
            var cfg = new PresentationConfig
            {
                PlaceFirst = false,
                FactOrder = new[] { "WHO", "CONTEXT", "WHEN", "WHERE", "WHAT", "WHY", "OUTCOME" }
            };

            var facts = new List<Fact>
            {
                new() { Id = "who", Category = FactCategory.Who, Text = "who fact" },
                new() { Id = "where", Category = FactCategory.Where, Text = "where fact" },
                new() { Id = "what", Category = FactCategory.What, Text = "what fact" },
                new() { Id = "outcome", Category = FactCategory.Outcome, Text = "outcome fact" }
            };

            var composed = RumorTextComposer.Compose(facts, cfg);

            Assert.Equal(4, composed.Parts.Count);
            Assert.Equal("who fact", composed.Parts[0].Fallback);
            Assert.Equal("where fact", composed.Parts[1].Fallback);
            Assert.Equal("what fact", composed.Parts[2].Fallback);
            Assert.Equal("outcome fact", composed.Parts[3].Fallback);
        }

        [Fact]
        public void PlaceFirst_WhenNoWhereFact_OriginalFactOrderPreserved()
        {
            var cfg = new PresentationConfig
            {
                PlaceFirst = true,
                FactOrder = new[] { "WHO", "CONTEXT", "WHEN", "WHERE", "WHAT", "WHY", "OUTCOME" }
            };

            var facts = new List<Fact>
            {
                new() { Id = "who", Category = FactCategory.Who, Text = "who fact" },
                new() { Id = "what", Category = FactCategory.What, Text = "what fact" },
                new() { Id = "outcome", Category = FactCategory.Outcome, Text = "outcome fact" }
            };

            var composed = RumorTextComposer.Compose(facts, cfg);

            Assert.Equal(3, composed.Parts.Count);
            Assert.Equal("who fact", composed.Parts[0].Fallback);
            Assert.Equal("what fact", composed.Parts[1].Fallback);
            Assert.Equal("outcome fact", composed.Parts[2].Fallback);
        }

        [Fact]
        public void CapitalizeFirstAscii_WhenPrefixEmpty_CapitalizesInitialLetter()
        {
            var cfg = new PresentationConfig
            {
                EncyclopediaLinksEnabled = false,
                FactSeparator = ", ",
                SentenceEnd = "."
            };

            var composed = new ComposedRumor
            {
                Parts = new List<ComposedFactPart>
                {
                    new() { TextId = "f1", Fallback = "near Pravend" },
                    new() { TextId = "f2", Fallback = "Derthert was captured" }
                },
                PrefixTextId = null,
                PrefixFallback = null
            };

            var result = RumorTextAssembler.Assemble(
                composed,
                cfg,
                resolveVar: (val, links) => val,
                getTemplate: (id, fb) => fb ?? string.Empty);

            Assert.StartsWith("Near Pravend, Derthert was captured.", result.PlainText);
            Assert.StartsWith("Near Pravend, Derthert was captured.", result.DisplayText);
        }

        [Fact]
        public void CapitalizeFirstAscii_WhenPrefixPresent_DoesNotModifyBody()
        {
            var cfg = new PresentationConfig
            {
                EncyclopediaLinksEnabled = false,
                FactSeparator = ", ",
                SentenceEnd = "."
            };

            var composed = new ComposedRumor
            {
                Parts = new List<ComposedFactPart>
                {
                    new() { TextId = "f1", Fallback = "near Pravend" },
                    new() { TextId = "f2", Fallback = "Derthert was captured" }
                },
                PrefixTextId = "prefix_heard",
                PrefixFallback = "I heard that "
            };

            var result = RumorTextAssembler.Assemble(
                composed,
                cfg,
                resolveVar: (val, links) => val,
                getTemplate: (id, fb) => fb ?? string.Empty);

            Assert.Equal("I heard that near Pravend, Derthert was captured.", result.PlainText);
        }

        [Fact]
        public void CapitalizeFirstAscii_WhenChinese_DoesNotModify()
        {
            var cfg = new PresentationConfig
            {
                EncyclopediaLinksEnabled = false,
                FactSeparator = "，",
                SentenceEnd = "。"
            };

            var composed = new ComposedRumor
            {
                Parts = new List<ComposedFactPart>
                {
                    new() { TextId = "f1", Fallback = "在帕拉文德附近" },
                    new() { TextId = "f2", Fallback = "德瑟特被俘" }
                },
                PrefixTextId = null,
                PrefixFallback = null
            };

            var result = RumorTextAssembler.Assemble(
                composed,
                cfg,
                resolveVar: (val, links) => val,
                getTemplate: (id, fb) => fb ?? string.Empty);

            Assert.Equal("在帕拉文德附近，德瑟特被俘。", result.PlainText);
        }

        [Fact]
        public void ConfigMerge_AddsPlaceFirst_DefaultingToTrue_WithoutChangingFactOrder()
        {
            string oldJson = @"{
                ""presentation"": {
                    ""factOrder"": [ ""WHO"", ""CONTEXT"", ""WHEN"", ""WHERE"", ""WHAT"", ""WHY"", ""OUTCOME"" ]
                }
            }";

            var liveToken = JObject.Parse(oldJson);
            var defaultConfig = new VividWorldConfig();
            defaultConfig.Normalize();
            var defaultToken = JObject.FromObject(defaultConfig);

            var mergeResult = ConfigMerge.AddMissingKeys(liveToken, defaultToken);

            Assert.NotEmpty(mergeResult.AddedPaths);
            Assert.Contains(mergeResult.AddedPaths, p => p.IndexOf("placeFirst", StringComparison.OrdinalIgnoreCase) >= 0);

            var mergedConfig = mergeResult.Merged.ToObject<VividWorldConfig>();
            Assert.NotNull(mergedConfig);
            Assert.True(mergedConfig!.Presentation.PlaceFirst);
            Assert.Equal(new[] { "WHO", "CONTEXT", "WHEN", "WHERE", "WHAT", "WHY", "OUTCOME" }, mergedConfig.Presentation.FactOrder);

            // 當讀入舊設定合併後組裝，地點仍成功排在第一
            var facts = new List<Fact>
            {
                new() { Id = "who", Category = FactCategory.Who, Text = "who" },
                new() { Id = "where", Category = FactCategory.Where, Text = "where" }
            };
            var composed = RumorTextComposer.Compose(facts, mergedConfig.Presentation);
            Assert.Equal("where", composed.Parts[0].Fallback);
            Assert.Equal("who", composed.Parts[1].Fallback);
        }

        [Fact]
        public void TemplateRenderSampleFormatter_RendersAll34Templates()
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

            Assert.Equal(34, allTemplates.Count);

            var enTable = EnglishStringTable.LoadFromFile(enPath);
            var cntTable = EnglishStringTable.LoadFromFile(cntPath);

            string output = TemplateRenderSampleFormatter.FormatAll(allTemplates, enTable, cntTable);

            Assert.Contains("--- English (EN) ---", output);
            Assert.Contains("--- Traditional Chinese (CNt) ---", output);
            Assert.Contains("[hero_captured_by_bandits]", output);
            Assert.Contains("[hero_rescued_from_bandits]", output);
            Assert.Contains("[hero_escaped_bandits]", output);
            Assert.Contains("Near Pravend", output);
            Assert.Contains("在帕拉文德附近", output);
        }
    }
}
