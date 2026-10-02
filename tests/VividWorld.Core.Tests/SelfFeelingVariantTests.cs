using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using Newtonsoft.Json.Linq;
using VividWorld.Core.Catalog;
using VividWorld.Core.Config;
using VividWorld.Core.Events;
using VividWorld.Core.Presentation;
using VividWorld.Core.Rumors;
using Xunit;

namespace VividWorld.Core.Tests
{
    /// <summary>當事人句尾依個性挑版本：門檻、優先順序、缺字串退回預設、中英鍵一致、日誌。</summary>
    public class SelfFeelingVariantTests
    {
        private static string FindRepoRoot()
        {
            string? dir = AppContext.BaseDirectory;
            while (!string.IsNullOrEmpty(dir))
            {
                if (Directory.Exists(Path.Combine(dir, "module", "ModuleData"))) return dir;
                dir = Directory.GetParent(dir)?.FullName;
            }
            throw new DirectoryNotFoundException("Could not find repository root containing module/ModuleData");
        }

        private static string DataPath(string file) => Path.Combine(FindRepoRoot(), "module", "ModuleData", file);

        private static EventCatalog LoadCatalog(params string[] files)
        {
            var templates = new List<EventTemplate>();
            var issues = new List<CatalogIssue>();
            foreach (var f in files)
            {
                var c = EventCatalogLoader.Load(File.ReadAllText(DataPath(f)), new PersistenceConfig { MaxFactsPerEvent = 24 });
                templates.AddRange(c.Templates);
                issues.AddRange(c.Issues);
            }
            return new EventCatalog(templates, issues, 0);
        }

        private static EventCatalog RealCatalog() => LoadCatalog("vividworld_events.json", "vividworld_situation_events.json");

        private static Dictionary<string, string> LoadStrings(string relative)
        {
            var doc = XDocument.Load(Path.Combine(FindRepoRoot(), "module", "ModuleData", "Languages", relative));
            return doc.Descendants("string").ToDictionary(x => (string)x.Attribute("id")!, x => (string)x.Attribute("text")!);
        }

        // 五組（消息 × 角色）：(消息型別, 角色, 句尾鍵前綴, 版本們：傾向, 特質, 達標的值, 差一點的值, 是 max 還是 min)
        private static readonly (string Type, string Role, string Key, (string Tendency, string Trait, int AtThreshold, int JustBelow)[] Variants)[] Table =
        {
            ("hero_escaped_captivity", "captor", "VividWorld_SelfFeeling_HeroEscaped_CAPTOR",
                new[] { ("Cruel", "mercy", -1, 0), ("Calculating", "calculating", 1, 0) }),
            ("hero_escaped_captivity", "prisoner", "VividWorld_SelfFeeling_HeroEscaped_PRISONER",
                new[] { ("Valor", "valor", 1, 0) }),
            ("hero_taken_prisoner", "captor", "VividWorld_SelfFeeling_HeroTakenPrisoner_CAPTOR",
                new[] { ("Cruel", "mercy", -1, 0), ("Mercy", "mercy", 1, 0) }),
            ("advice_mocked", "student", "VividWorld_SelfFeeling_AdviceMocked_STUDENT",
                new[] { ("Valor", "valor", 2, 1), ("Calculating", "calculating", 1, 0) }),
            ("wager_refused", "challenger", "VividWorld_SelfFeeling_WagerRefused_CHALLENGER",
                new[] { ("Valor", "valor", 1, 0), ("Mercy", "mercy", 1, 0) }),
        };

        private static TraitProfile Traits(string trait, int value)
        {
            var p = new TraitProfile { HeroId = "hero_x" };
            switch (trait)
            {
                case "mercy": p.Mercy = value; break;
                case "valor": p.Valor = value; break;
                case "calculating": p.Calculating = value; break;
                case "honor": p.Honor = value; break;
                case "generosity": p.Generosity = value; break;
            }
            return p;
        }

        private static EventTemplate Synthetic(params SelfFeelingVariantRule[] rules)
        {
            return new EventTemplate
            {
                Type = "t",
                SelfFeelingVariants = new Dictionary<string, List<SelfFeelingVariantRule>> { ["captor"] = rules.ToList() }
            };
        }

        [Fact]
        public void RealCatalog_LoadsWithoutIssues_AndDeclaresTheFiveVariantSets()
        {
            var cat = RealCatalog();
            Assert.DoesNotContain(cat.Issues, i => i.Code == CatalogIssueCode.SelfFeelingVariantInvalid);
            foreach (var row in Table)
            {
                var rules = SelfFeelingVariantSelector.RulesFor(cat.ByType(row.Type), row.Role);
                Assert.NotNull(rules);
                Assert.Equal(row.Variants.Select(v => v.Tendency), rules!.Select(r => r.Tendency));
            }
        }

        [Fact]
        public void EveryVariant_AtItsThreshold_IsChosen_JustBelow_FallsToDefault()
        {
            var cat = RealCatalog();
            foreach (var row in Table)
            {
                var template = cat.ByType(row.Type);
                foreach (var v in row.Variants)
                {
                    var at = SelfFeelingVariantSelector.Choose(template, row.Role, Traits(v.Trait, v.AtThreshold));
                    Assert.Equal(v.Tendency, at.Tendency);
                    Assert.Contains(v.Tendency, at.Log);
                    Assert.Contains($"{v.Trait}={v.AtThreshold}", at.Log);

                    var below = SelfFeelingVariantSelector.Choose(template, row.Role, Traits(v.Trait, v.JustBelow));
                    Assert.Null(below.Tendency);
                    Assert.Contains("default line", below.Log);
                    Assert.Contains($"{v.Trait}={v.JustBelow}", below.Log);
                }
            }
        }

        [Fact]
        public void Cruel_MeansMercyAtMostMinusOne_NotJustLow()
        {
            var t = RealCatalog().ByType("hero_taken_prisoner");
            Assert.Equal("Cruel", SelfFeelingVariantSelector.Choose(t, "captor", Traits("mercy", -2)).Tendency);
            Assert.Equal("Cruel", SelfFeelingVariantSelector.Choose(t, "captor", Traits("mercy", -1)).Tendency);
            Assert.Null(SelfFeelingVariantSelector.Choose(t, "captor", Traits("mercy", 0)).Tendency);
        }

        [Fact]
        public void TwoMatches_FirstDeclaredWins()
        {
            var t = RealCatalog().ByType("advice_mocked");
            var both = new TraitProfile { Valor = 2, Calculating = 2 };
            Assert.Equal("Valor", SelfFeelingVariantSelector.Choose(t, "student", both).Tendency);

            var t2 = RealCatalog().ByType("wager_refused");
            var both2 = new TraitProfile { Valor = 1, Mercy = 2 };
            Assert.Equal("Valor", SelfFeelingVariantSelector.Choose(t2, "challenger", both2).Tendency);

            // 宣告順序反過來，勝出的跟著變：優先順序完全來自資料
            var reversed = Synthetic(
                new SelfFeelingVariantRule { Tendency = "B", Trait = "calculating", Min = 1 },
                new SelfFeelingVariantRule { Tendency = "A", Trait = "valor", Min = 1 });
            Assert.Equal("B", SelfFeelingVariantSelector.Choose(reversed, "captor", new TraitProfile { Valor = 2, Calculating = 2 }).Tendency);
        }

        [Fact]
        public void NoDeclaration_OrUnknownTraits_OrOtherRole_UseDefault_WithReason()
        {
            var t = RealCatalog().ByType("hero_taken_prisoner");
            Assert.Null(SelfFeelingVariantSelector.Choose(t, "prisoner", Traits("mercy", -2)).Tendency);
            Assert.Contains("no personality variants", SelfFeelingVariantSelector.Choose(t, "prisoner", Traits("mercy", -2)).Log);
            Assert.Contains("traits unknown", SelfFeelingVariantSelector.Choose(t, "captor", null).Log);
            Assert.Null(SelfFeelingVariantSelector.Choose(null, "captor", Traits("mercy", -2)).Tendency);
        }

        private static ComposedRumor ComposeCaptor(EventTemplate? template, TraitProfile? traits)
        {
            var evt = new WorldEvent
            {
                EventId = "evt_t",
                Type = "hero_taken_prisoner",
                Origin = EventOrigin.Public,
                Participants = new Dictionary<string, string>(StringComparer.Ordinal) { ["captor"] = "hero_captor", ["prisoner"] = "hero_prisoner" }
            };
            var facts = new List<Fact>
            {
                new Fact
                {
                    Id = "who", Category = FactCategory.Who, TextId = "VividWorld_Fact_HeroTakenPrisoner_Who",
                    Text = "{CAPTOR} captured {PRISONER}", Fragility = 1,
                    Vars = new Dictionary<string, string> { ["CAPTOR"] = "hero:hero_captor", ["PRISONER"] = "hero:hero_prisoner" }
                }
            };
            return RumorTextComposer.Compose(evt, facts, new PresentationConfig(), prefix: null, speakerHeroId: "hero_captor",
                template: template, speakerTraits: traits);
        }

        private static RumorRenderResult Render(ComposedRumor composed, Dictionary<string, string> table, List<string> info)
        {
            var t = new EnglishStringTable(table);
            var cfg = new PresentationConfig { EncyclopediaLinksEnabled = false, SentenceEnd = ".", FactSeparator = ", " };
            RumorTextAssembler.ResetSessionMissingSentenceKeys();
            return RumorTextAssembler.Assemble(
                composed, cfg,
                (val, links) => val.Contains(':') ? val.Substring(val.IndexOf(':') + 1) : val,
                (id, fb) => fb == null ? (t.Get(id) ?? string.Empty) : t.Lookup(id, fb),
                (key, fb) => t.GetWithFallback(key, fb),
                onWarning: null, isFemale: null, onInfo: info.Add);
        }

        private const string Sentence = "VividWorld_Sentence_HeroTakenPrisoner_Who_Self_CAPTOR";
        private const string Default = "VividWorld_SelfFeeling_HeroTakenPrisoner_CAPTOR";

        [Fact]
        public void Composer_ChosenVariantKey_SitsBeforeTheDefaultKey()
        {
            var t = RealCatalog().ByType("hero_taken_prisoner");
            var composed = ComposeCaptor(t, Traits("mercy", -2));
            Assert.Equal(new[] { Default + "_Cruel", Default }, composed.SelfFeelingKeyCandidates);
            Assert.Equal(Default + "_Cruel", composed.SelfFeelingVariantKey);

            var plain = ComposeCaptor(t, Traits("mercy", 0));
            Assert.Equal(new[] { Default }, plain.SelfFeelingKeyCandidates);
            Assert.Null(plain.SelfFeelingVariantKey);
            Assert.False(string.IsNullOrEmpty(plain.SelfFeelingVariantLog));
        }

        [Fact]
        public void Render_UsesVariantLine_AndLogsTraitsAndChoice()
        {
            var t = RealCatalog().ByType("hero_taken_prisoner");
            var table = new Dictionary<string, string>
            {
                [Sentence] = "I captured {PRISONER}",
                [Default] = "Well fought",
                [Default + "_Cruel"] = "A cell suits them"
            };
            var info = new List<string>();
            var r = Render(ComposeCaptor(t, Traits("mercy", -2)), table, info);
            Assert.Equal("I captured hero_prisoner. A cell suits them.", r.PlainText);
            Assert.Equal(Default + "_Cruel", r.SelfFeelingKeyUsed);
            Assert.Contains(info, l => l.Contains("self feeling variant") && l.Contains("mercy=-2") && l.Contains("chose 'Cruel'"));
        }

        [Fact]
        public void Render_NoTendencyMatches_UsesDefaultLine_AndLogsWhy()
        {
            var t = RealCatalog().ByType("hero_taken_prisoner");
            var table = new Dictionary<string, string>
            {
                [Sentence] = "I captured {PRISONER}",
                [Default] = "Well fought",
                [Default + "_Cruel"] = "A cell suits them"
            };
            var info = new List<string>();
            var r = Render(ComposeCaptor(t, Traits("mercy", 0)), table, info);
            Assert.Equal("I captured hero_prisoner. Well fought.", r.PlainText);
            Assert.Contains(info, l => l.Contains("self feeling variant") && l.Contains("no variant matched") && l.Contains("mercy=0"));
        }

        [Fact]
        public void Render_VariantKeyMissingInLanguage_FallsBackToDefault_AndSaysSo()
        {
            var t = RealCatalog().ByType("hero_taken_prisoner");
            var table = new Dictionary<string, string>
            {
                [Sentence] = "I captured {PRISONER}",
                [Default] = "Well fought"
            };
            var info = new List<string>();
            var r = Render(ComposeCaptor(t, Traits("mercy", -2)), table, info);
            Assert.Equal("I captured hero_prisoner. Well fought.", r.PlainText);
            Assert.Equal(Default, r.SelfFeelingKeyUsed);
            Assert.Contains(info, l => l.Contains("self feeling variant") && l.Contains("has no string in this language"));
        }

        [Fact]
        public void NoTemplate_BehavesExactlyAsBefore()
        {
            var composed = ComposeCaptor(null, Traits("mercy", -2));
            Assert.Equal(new[] { Default }, composed.SelfFeelingKeyCandidates);
            Assert.Null(composed.SelfFeelingVariantKey);
        }

        [Fact]
        public void NineVariantKeys_ExistInBothLanguages_WithNonEmptyTextAndNoTrailingPeriod()
        {
            var en = LoadStrings("std_module_strings_xml.xml");
            var zh = LoadStrings(Path.Combine("CNt", "std_module_strings_xml.xml"));
            int count = 0;
            foreach (var row in Table)
            {
                foreach (var v in row.Variants)
                {
                    string key = row.Key + "_" + v.Tendency;
                    Assert.True(en.ContainsKey(key), "missing English: " + key);
                    Assert.True(zh.ContainsKey(key), "missing Chinese: " + key);
                    Assert.False(string.IsNullOrWhiteSpace(en[key]));
                    Assert.False(string.IsNullOrWhiteSpace(zh[key]));
                    Assert.False(en[key].TrimEnd().EndsWith("."));
                    Assert.False(zh[key].TrimEnd().EndsWith("。"));
                    count++;
                }
            }
            Assert.Equal(9, count);

            // 兩個語言的句尾版本鍵集合完全一致
            var enKeys = en.Keys.Where(k => k.StartsWith("VividWorld_SelfFeeling_", StringComparison.Ordinal)).OrderBy(k => k, StringComparer.Ordinal);
            var zhKeys = zh.Keys.Where(k => k.StartsWith("VividWorld_SelfFeeling_", StringComparison.Ordinal)).OrderBy(k => k, StringComparer.Ordinal);
            Assert.Equal(enKeys, zhKeys);
        }

        [Fact]
        public void EveryDeclaredVariantInTheCatalog_HasAStringInBothLanguages()
        {
            var en = LoadStrings("std_module_strings_xml.xml");
            var zh = LoadStrings(Path.Combine("CNt", "std_module_strings_xml.xml"));
            foreach (var tmpl in RealCatalog().Templates.Where(t => t.SelfFeelingVariants != null))
            {
                foreach (var kvp in tmpl.SelfFeelingVariants!)
                {
                    // 句尾鍵：消息型別的字串前綴 + 角色大寫；用表裡對得上的那一列確認
                    var row = Table.Single(r => r.Type == tmpl.Type && r.Role == kvp.Key);
                    foreach (var rule in kvp.Value)
                    {
                        Assert.True(en.ContainsKey(row.Key + "_" + rule.Tendency));
                        Assert.True(zh.ContainsKey(row.Key + "_" + rule.Tendency));
                    }
                }
            }
        }

        [Fact]
        public void Loader_RejectsBadDeclarations()
        {
            string events = File.ReadAllText(DataPath("vividworld_events.json"));

            EventCatalog Mutated(Action<JObject> edit)
            {
                var arr = JArray.Parse(events);
                var tmpl = (JObject)arr.Single(x => (string?)x["type"] == "hero_taken_prisoner");
                edit(tmpl);
                return EventCatalogLoader.Load(arr.ToString(), new PersistenceConfig { MaxFactsPerEvent = 24 });
            }

            Assert.Contains(Mutated(t => t["selfFeelingVariants"] = JObject.Parse(@"{""nobody"":[{""tendency"":""Cruel"",""trait"":""mercy"",""max"":-1}]}")).Issues,
                i => i.Code == CatalogIssueCode.SelfFeelingVariantInvalid);
            Assert.Contains(Mutated(t => t["selfFeelingVariants"] = JObject.Parse(@"{""captor"":[{""tendency"":""Cruel"",""trait"":""bravery"",""max"":-1}]}")).Issues,
                i => i.Code == CatalogIssueCode.SelfFeelingVariantInvalid);
            Assert.Contains(Mutated(t => t["selfFeelingVariants"] = JObject.Parse(@"{""captor"":[{""tendency"":""Cruel"",""trait"":""mercy"",""min"":1,""max"":-1}]}")).Issues,
                i => i.Code == CatalogIssueCode.SelfFeelingVariantInvalid);
            Assert.Contains(Mutated(t => t["selfFeelingVariants"] = JObject.Parse(@"{""captor"":[{""tendency"":""Cruel"",""trait"":""mercy""}]}")).Issues,
                i => i.Code == CatalogIssueCode.SelfFeelingVariantInvalid);
            Assert.Contains(Mutated(t => t["selfFeelingVariants"] = JObject.Parse(@"{""captor"":[{""tendency"":""Cruel"",""trait"":""mercy"",""max"":-1},{""tendency"":""Cruel"",""trait"":""valor"",""min"":1}]}")).Issues,
                i => i.Code == CatalogIssueCode.SelfFeelingVariantInvalid);
        }

        [Fact]
        public void DevCombinationListing_IncludesTheVariantLines_InBothLanguages()
        {
            var cat = RealCatalog();
            var en = new EnglishStringTable(LoadStrings("std_module_strings_xml.xml"));
            var zh = new EnglishStringTable(LoadStrings(Path.Combine("CNt", "std_module_strings_xml.xml")));
            string text = VividWorld.Core.Diagnostics.TemplateRenderSampleFormatter.FormatAll(cat.Templates, en, zh);
            Assert.Contains("<variant Cruel: mercy <= -1>", text);
            Assert.Contains("<variant Calculating: calculating >= 1>", text);
            Assert.Contains("【句尾版本 Cruel：mercy <= -1】", text);
            Assert.Contains("下回再落到我手裡，可不會這麼客氣了", text);
            Assert.Contains("Let that prisoner fall into my hands again", text);
            Assert.DoesNotContain("falls back to the default line", text);
        }
    }
}
