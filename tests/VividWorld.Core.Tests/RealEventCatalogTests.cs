using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using VividWorld.Core.Catalog;
using VividWorld.Core.Config;
using VividWorld.Core.Events;
using VividWorld.Core.Ingest;
using VividWorld.Core.Persistence;
using Xunit;

namespace VividWorld.Core.Tests
{
    public class RealEventCatalogTests
    {
        private readonly PersistenceConfig _defaultPersistence = new() { MaxFactsPerEvent = 24 };

        private static string FindRealEventsJson()
        {
            string? current = AppContext.BaseDirectory;
            while (!string.IsNullOrEmpty(current))
            {
                if (File.Exists(Path.Combine(current, "VividWorld.sln")))
                {
                    string path = Path.Combine(current, "module", "ModuleData", "vividworld_events.json");
                    if (File.Exists(path)) return File.ReadAllText(path);
                }

                var parent = Directory.GetParent(current);
                if (parent == null) break;
                current = parent.FullName;
            }
            throw new FileNotFoundException("vividworld_events.json could not be found relative to " + AppContext.BaseDirectory);
        }

        private EventCatalog LoadRealEventsCatalog()
        {
            string json = FindRealEventsJson();
            return EventCatalogLoader.Load(json, _defaultPersistence);
        }

        // ==========================================
        // 7.3 RealEventCatalogTests
        // ==========================================

        [Fact]
        public void RealEventsJson_LoadsAll7Templates()
        {
            var catalog = LoadRealEventsCatalog();

            Assert.Equal(9, catalog.Templates.Count);
            Assert.Equal(0, catalog.SkippedCount);
            Assert.Empty(catalog.Issues.Where(i => i.IsError));

            var expectedTypes = new[]
            {
                "hero_murdered",
                "hero_executed",
                "hero_died_in_battle",
                "hero_died_naturally",
                "hero_taken_prisoner",
                "heroes_married",
                "child_born",
                "hero_released",
                "hero_escaped_captivity"
            };

            foreach (var type in expectedTypes)
            {
                var template = catalog.ByType(type);
                Assert.NotNull(template);
            }
        }

        [Fact]
        public void HeroMurdered_IsSecret_KnowingRolesKiller()
        {
            var catalog = LoadRealEventsCatalog();
            var murder = catalog.ByType("hero_murdered");

            Assert.NotNull(murder);
            Assert.Equal(EventOrigin.Secret, murder!.Origin);
            Assert.Equal(5, murder.DramaWeight);
            Assert.Single(murder.KnowingRoles);
            Assert.Contains("killer", murder.KnowingRoles);
        }

        [Fact]
        public void AllFactsInRealEvents_HaveNonEmptyTextId()
        {
            var catalog = LoadRealEventsCatalog();
            int totalFacts = 0;

            foreach (var template in catalog.Templates)
            {
                Assert.Equal(4, template.Facts.Count);
                foreach (var fact in template.Facts)
                {
                    totalFacts++;
                    Assert.False(string.IsNullOrWhiteSpace(fact.TextId), $"Fact {fact.Id} in template {template.Type} has empty TextId");
                    Assert.StartsWith("VividWorld_Fact_", fact.TextId);
                }
            }

            Assert.Equal(36, totalFacts);
        }

        [Fact]
        public void OnlyWhereFacts_AreOptional()
        {
            var catalog = LoadRealEventsCatalog();

            foreach (var template in catalog.Templates)
            {
                foreach (var fact in template.Facts)
                {
                    if (fact.Category == FactCategory.Where)
                    {
                        Assert.True(fact.Optional, $"WHERE fact {fact.Id} in {template.Type} should be optional");
                    }
                    else
                    {
                        Assert.False(fact.Optional, $"Non-WHERE fact {fact.Id} in {template.Type} should NOT be optional");
                    }
                }
            }
        }

        // ==========================================
        // 7.4 7 個模板各自的完整綁定測試
        // ==========================================

        [Fact]
        public void Bind_HeroMurdered_ProducesCorrectSubmission()
        {
            var catalog = LoadRealEventsCatalog();
            var template = catalog.ByType("hero_murdered")!;
            var bindings = new Dictionary<string, string>
            {
                ["VICTIM"] = "hero_victim",
                ["KILLER"] = "hero_killer",
                ["SETTLEMENT"] = "settlement_murder"
            };

            var sub = TemplateBinder.Bind(template, bindings, 100.0, null, out var issues);

            Assert.NotNull(sub);
            Assert.Empty(issues.Where(i => i.IsError));
            Assert.Equal("hero_murdered", sub!.Type);
            Assert.Equal(EventOrigin.Secret, sub.Origin);
            Assert.Equal(5, sub.DramaWeight);
            Assert.Equal(100.0, sub.Day);
            Assert.Equal("hero_victim", sub.Participants["victim"]);
            Assert.Equal("hero_killer", sub.Participants["killer"]);
            Assert.Contains("killer", sub.KnowingRoles);

            Assert.Equal(4, sub.Facts.Count);
            Assert.Equal(FactCategory.Who, sub.Facts[0].Category);
            Assert.Equal(FactCategory.Where, sub.Facts[1].Category);
            Assert.Equal(FactCategory.What, sub.Facts[2].Category);
            Assert.Equal(FactCategory.Outcome, sub.Facts[3].Category);
            Assert.NotNull(sub.Facts[0].Vars);
            Assert.Contains("hero:hero_victim", sub.Facts[0].Vars!.Values);
            Assert.Contains("hero:hero_killer", sub.Facts[0].Vars!.Values);
            Assert.NotNull(sub.Facts[1].Vars);
            Assert.Equal("settlement:settlement_murder", sub.Facts[1].Vars!["SETTLEMENT"]);
        }

        [Fact]
        public void Bind_HeroExecuted_ProducesCorrectSubmission()
        {
            var catalog = LoadRealEventsCatalog();
            var template = catalog.ByType("hero_executed")!;
            var bindings = new Dictionary<string, string>
            {
                ["VICTIM"] = "hero_victim",
                ["KILLER"] = "hero_exec",
                ["SETTLEMENT"] = "settlement_exec"
            };

            var sub = TemplateBinder.Bind(template, bindings, 101.0, null, out var issues);

            Assert.NotNull(sub);
            Assert.Empty(issues.Where(i => i.IsError));
            Assert.Equal("hero_executed", sub!.Type);
            Assert.Equal(EventOrigin.Public, sub.Origin);
            Assert.Equal(5, sub.DramaWeight);
            Assert.Equal(101.0, sub.Day);
            Assert.Equal("hero_victim", sub.Participants["victim"]);
            Assert.Equal("hero_exec", sub.Participants["killer"]);
            Assert.Empty(sub.KnowingRoles);

            Assert.Equal(4, sub.Facts.Count);
            Assert.Equal(FactCategory.Who, sub.Facts[0].Category);
            Assert.Equal(FactCategory.Where, sub.Facts[1].Category);
            Assert.Equal(FactCategory.What, sub.Facts[2].Category);
            Assert.Equal(FactCategory.Outcome, sub.Facts[3].Category);
            Assert.NotNull(sub.Facts[0].Vars);
            Assert.Contains("hero:hero_victim", sub.Facts[0].Vars!.Values);
            Assert.Contains("hero:hero_exec", sub.Facts[0].Vars!.Values);
            Assert.NotNull(sub.Facts[1].Vars);
            Assert.Equal("settlement:settlement_exec", sub.Facts[1].Vars!["SETTLEMENT"]);
        }

        [Fact]
        public void Bind_HeroDiedInBattle_ProducesCorrectSubmission()
        {
            var catalog = LoadRealEventsCatalog();
            var template = catalog.ByType("hero_died_in_battle")!;
            var bindings = new Dictionary<string, string>
            {
                ["VICTIM"] = "hero_victim",
                ["KILLER"] = "hero_killer",
                ["SETTLEMENT"] = "settlement_battle"
            };

            var sub = TemplateBinder.Bind(template, bindings, 102.0, null, out var issues);

            Assert.NotNull(sub);
            Assert.Empty(issues.Where(i => i.IsError));
            Assert.Equal("hero_died_in_battle", sub!.Type);
            Assert.Equal(EventOrigin.Public, sub.Origin);
            Assert.Equal(4, sub.DramaWeight);
            Assert.Equal(102.0, sub.Day);
            Assert.Equal("hero_victim", sub.Participants["victim"]);
            Assert.Equal("hero_killer", sub.Participants["killer"]);
            Assert.Empty(sub.KnowingRoles);

            Assert.Equal(4, sub.Facts.Count);
            Assert.Equal(FactCategory.Who, sub.Facts[0].Category);
            Assert.Equal(FactCategory.Where, sub.Facts[1].Category);
            Assert.Equal(FactCategory.What, sub.Facts[2].Category);
            Assert.Equal(FactCategory.Outcome, sub.Facts[3].Category);
            Assert.NotNull(sub.Facts[1].Vars);
            Assert.Equal("settlement:settlement_battle", sub.Facts[1].Vars!["SETTLEMENT"]);
        }

        [Fact]
        public void Bind_HeroDiedNaturally_ProducesCorrectSubmission()
        {
            var catalog = LoadRealEventsCatalog();
            var template = catalog.ByType("hero_died_naturally")!;
            var bindings = new Dictionary<string, string>
            {
                ["VICTIM"] = "hero_victim",
                ["SETTLEMENT"] = "settlement_town"
            };

            var sub = TemplateBinder.Bind(template, bindings, 103.0, null, out var issues);

            Assert.NotNull(sub);
            Assert.Empty(issues.Where(i => i.IsError));
            Assert.Equal("hero_died_naturally", sub!.Type);
            Assert.Equal(EventOrigin.Public, sub.Origin);
            Assert.Equal(2, sub.DramaWeight);
            Assert.Equal(103.0, sub.Day);
            Assert.Equal("hero_victim", sub.Participants["victim"]);
            Assert.Empty(sub.KnowingRoles);

            Assert.Equal(4, sub.Facts.Count);
            Assert.Equal(FactCategory.Who, sub.Facts[0].Category);
            Assert.Equal(FactCategory.Where, sub.Facts[1].Category);
            Assert.Equal(FactCategory.What, sub.Facts[2].Category);
            Assert.Equal(FactCategory.Outcome, sub.Facts[3].Category);
            Assert.NotNull(sub.Facts[1].Vars);
            Assert.Equal("settlement:settlement_town", sub.Facts[1].Vars!["SETTLEMENT"]);
        }

        [Fact]
        public void Bind_HeroTakenPrisoner_ProducesCorrectSubmission()
        {
            var catalog = LoadRealEventsCatalog();
            var template = catalog.ByType("hero_taken_prisoner")!;
            var bindings = new Dictionary<string, string>
            {
                ["PRISONER"] = "hero_captured",
                ["CAPTOR"] = "hero_captor",
                ["SETTLEMENT"] = "settlement_keep"
            };

            var sub = TemplateBinder.Bind(template, bindings, 104.0, null, out var issues);

            Assert.NotNull(sub);
            Assert.Empty(issues.Where(i => i.IsError));
            Assert.Equal("hero_taken_prisoner", sub!.Type);
            Assert.Equal(EventOrigin.Public, sub.Origin);
            Assert.Equal(4, sub.DramaWeight);
            Assert.Equal(104.0, sub.Day);
            Assert.Equal("hero_captured", sub.Participants["prisoner"]);
            Assert.Equal("hero_captor", sub.Participants["captor"]);
            Assert.Empty(sub.KnowingRoles);

            Assert.Equal(4, sub.Facts.Count);
            Assert.Equal(FactCategory.Who, sub.Facts[0].Category);
            Assert.Equal(FactCategory.Where, sub.Facts[1].Category);
            Assert.Equal(FactCategory.What, sub.Facts[2].Category);
            Assert.Equal(FactCategory.Outcome, sub.Facts[3].Category);
            Assert.NotNull(sub.Facts[1].Vars);
            Assert.Equal("settlement:settlement_keep", sub.Facts[1].Vars!["SETTLEMENT"]);
        }

        [Fact]
        public void Bind_HeroesMarried_ProducesCorrectSubmission()
        {
            var catalog = LoadRealEventsCatalog();
            var template = catalog.ByType("heroes_married")!;
            var bindings = new Dictionary<string, string>
            {
                ["SPOUSE_A"] = "hero_spouse1",
                ["SPOUSE_B"] = "hero_spouse2",
                ["SETTLEMENT"] = "settlement_hall"
            };

            var sub = TemplateBinder.Bind(template, bindings, 105.0, null, out var issues);

            Assert.NotNull(sub);
            Assert.Empty(issues.Where(i => i.IsError));
            Assert.Equal("heroes_married", sub!.Type);
            Assert.Equal(EventOrigin.Public, sub.Origin);
            Assert.Equal(3, sub.DramaWeight);
            Assert.Equal(105.0, sub.Day);
            Assert.Equal("hero_spouse1", sub.Participants["spouse_a"]);
            Assert.Equal("hero_spouse2", sub.Participants["spouse_b"]);
            Assert.Empty(sub.KnowingRoles);

            Assert.Equal(4, sub.Facts.Count);
            Assert.Equal(FactCategory.Who, sub.Facts[0].Category);
            Assert.Equal(FactCategory.Where, sub.Facts[1].Category);
            Assert.Equal(FactCategory.What, sub.Facts[2].Category);
            Assert.Equal(FactCategory.Outcome, sub.Facts[3].Category);
            Assert.NotNull(sub.Facts[1].Vars);
            Assert.Equal("settlement:settlement_hall", sub.Facts[1].Vars!["SETTLEMENT"]);
        }

        [Fact]
        public void Bind_ChildBorn_ProducesCorrectSubmission()
        {
            var catalog = LoadRealEventsCatalog();
            var template = catalog.ByType("child_born")!;
            var bindings = new Dictionary<string, string>
            {
                ["MOTHER"] = "hero_mother",
                ["CHILD"] = "hero_baby",
                ["SETTLEMENT"] = "settlement_crib"
            };

            var sub = TemplateBinder.Bind(template, bindings, 106.0, null, out var issues);

            Assert.NotNull(sub);
            Assert.Empty(issues.Where(i => i.IsError));
            Assert.Equal("child_born", sub!.Type);
            Assert.Equal(EventOrigin.Public, sub.Origin);
            Assert.Equal(2, sub.DramaWeight);
            Assert.Equal(106.0, sub.Day);
            Assert.Equal("hero_mother", sub.Participants["mother"]);
            Assert.Equal("hero_baby", sub.Participants["child"]);
            Assert.Empty(sub.KnowingRoles);

            Assert.Equal(4, sub.Facts.Count);
            Assert.Equal(FactCategory.Who, sub.Facts[0].Category);
            Assert.Equal(FactCategory.Where, sub.Facts[1].Category);
            Assert.Equal(FactCategory.What, sub.Facts[2].Category);
            Assert.Equal(FactCategory.Outcome, sub.Facts[3].Category);
            Assert.NotNull(sub.Facts[1].Vars);
            Assert.Equal("settlement:settlement_crib", sub.Facts[1].Vars!["SETTLEMENT"]);
        }

        // ==========================================
        // 7.5 釋放／逃脫：連結必須活著走完 Bind()
        //
        // L-36：兩個模板原本寫 linkedTemplateType: null，而 TemplateBinder 只在這個欄位
        // 非空時才把 linkedEventId 放進提交 ⇒ 存下來的釋放事件 LinkedEventId 一律 null，
        // MemoryStamper.StampOutdated 在第一道 guard 就回 0，過時整條鏈一次都沒跑。
        // 實機 71 則釋放、連上 70 則被俘、標記 0 筆。以下三條是那個缺陷的護欄。
        // ==========================================

        [Fact]
        public void ReleaseTemplates_DeclareTheCaptureAsTheirLinkedTemplate()
        {
            var catalog = LoadRealEventsCatalog();

            Assert.Equal("hero_taken_prisoner", catalog.ByType("hero_released")!.LinkedTemplateType);
            Assert.Equal("hero_taken_prisoner", catalog.ByType("hero_escaped_captivity")!.LinkedTemplateType);

            // 其餘七個維持沒有連結，而且宣告出去的型別不能是懸空的
            foreach (var template in catalog.Templates)
            {
                if (template.Type == "hero_released" || template.Type == "hero_escaped_captivity") continue;
                Assert.True(string.IsNullOrEmpty(template.LinkedTemplateType), $"{template.Type} should not declare a linked template");
            }

            Assert.Empty(catalog.Issues.Where(i => i.Code == CatalogIssueCode.DanglingTemplateRef));
        }

        [Theory]
        [InlineData("hero_released")]
        [InlineData("hero_escaped_captivity")]
        public void Bind_Release_KeepsTheLinkedCaptureEventId(string type)
        {
            var catalog = LoadRealEventsCatalog();
            var template = catalog.ByType(type)!;
            var bindings = new Dictionary<string, string>
            {
                ["PRISONER"] = "hero_prisoner",
                ["CAPTOR"] = "hero_captor",
                ["SETTLEMENT"] = "settlement_gate"
            };

            var sub = TemplateBinder.Bind(template, bindings, 107.0, "evt_91078_88ff", out var issues);

            Assert.NotNull(sub);
            Assert.Empty(issues.Where(i => i.IsError));
            Assert.Equal(type, sub!.Type);
            Assert.Equal(EventOrigin.Public, sub.Origin);
            Assert.Equal(107.0, sub.Day);
            Assert.Equal("hero_prisoner", sub.Participants["prisoner"]);
            Assert.Equal("hero_captor", sub.Participants["captor"]);

            // 這一行就是 L-36 的缺陷本身
            Assert.Equal("evt_91078_88ff", sub.LinkedEventId);

            Assert.Equal(4, sub.Facts.Count);
            Assert.Equal(FactCategory.Who, sub.Facts[0].Category);
            Assert.Equal(FactCategory.Where, sub.Facts[1].Category);
            Assert.Equal(FactCategory.What, sub.Facts[2].Category);
            Assert.Equal(FactCategory.Outcome, sub.Facts[3].Category);
            Assert.NotNull(sub.Facts[1].Vars);
            Assert.Equal("settlement:settlement_gate", sub.Facts[1].Vars!["SETTLEMENT"]);
        }

        [Theory]
        [InlineData("hero_released")]
        [InlineData("hero_escaped_captivity")]
        public void Bind_Release_WithNoCaptureFound_LeavesLinkedEventIdNull(string type)
        {
            var catalog = LoadRealEventsCatalog();
            var template = catalog.ByType(type)!;
            var bindings = new Dictionary<string, string>
            {
                ["PRISONER"] = "hero_prisoner",
                ["CAPTOR"] = "hero_captor",
                ["SETTLEMENT"] = "settlement_gate"
            };

            // 索引裡找不到那則被俘時，行為傳 null 進來：照樣產生事件，但不得寫假的連結
            var sub = TemplateBinder.Bind(template, bindings, 108.0, null, out var issues);

            Assert.NotNull(sub);
            Assert.Empty(issues.Where(i => i.IsError));
            Assert.Null(sub!.LinkedEventId);
            Assert.Equal(4, sub.Facts.Count);
        }
    }
}
