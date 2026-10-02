#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using VividWorld.Core.Catalog;
using VividWorld.Core.Config;
using VividWorld.Core.Events;
using VividWorld.Core.Feelings;
using VividWorld.Core.Persistence;
using VividWorld.Core.Presentation;
using VividWorld.Core.Rumors;
using Xunit;

namespace VividWorld.Core.Tests
{
    /// <summary>
    /// 紀事顯示的那一句，要跟對話當下講的那一句逐字相同：用出貨的模板與繁中字串表，
    /// 把對話組好的句子抄進玩家紀錄、存檔讀回、再重組，比對兩邊渲染出來的字。
    /// </summary>
    public class ChronicleSpokenLineTests
    {
        private const string Captor = "hero_captor";
        private const string Prisoner = "hero_prisoner";
        private const string Bystander = "hero_bystander";
        private const string Informant = "hero_informant";

        private static readonly Dictionary<string, string> Names = new()
        {
            ["hero:" + Captor] = "卡拉多格",
            ["hero:" + Prisoner] = "德瑟特",
            ["hero:" + Bystander] = "波爾",
            ["hero:" + Informant] = "埃爾貢",
            ["settlement:town_pravend"] = "帕拉汶德"
        };

        private static string RepoRoot()
        {
            string? current = AppContext.BaseDirectory;
            while (!string.IsNullOrEmpty(current))
            {
                if (File.Exists(Path.Combine(current, "VividWorld.sln"))) return current!;
                current = Directory.GetParent(current!)?.FullName;
            }
            throw new DirectoryNotFoundException("Could not find repository root containing VividWorld.sln");
        }

        private static EventTemplate CaptureTemplate()
        {
            string path = Path.Combine(RepoRoot(), "module", "ModuleData", "vividworld_events.json");
            var catalog = EventCatalogLoader.Load(File.ReadAllText(path), new PersistenceConfig());
            return catalog.Templates.Single(t => t.Type == "hero_taken_prisoner");
        }

        private static EnglishStringTable ChineseTable()
        {
            return EnglishStringTable.LoadFromFile(
                Path.Combine(RepoRoot(), "module", "ModuleData", "Languages", "CNt", "std_module_strings_xml.xml"));
        }

        private static WorldEvent CaptureEvent()
        {
            var heroes = new Dictionary<string, string> { ["CAPTOR"] = "hero:" + Captor, ["PRISONER"] = "hero:" + Prisoner };
            return new WorldEvent
            {
                EventId = "evt_capture",
                Type = "hero_taken_prisoner",
                Day = 10.0,
                Origin = EventOrigin.Public,
                Participants = new Dictionary<string, string> { ["captor"] = Captor, ["prisoner"] = Prisoner },
                Facts = new List<Fact>
                {
                    new() { Id = "who", Category = FactCategory.Who, TextId = "VividWorld_Fact_HeroTakenPrisoner_Who",
                            Text = "{CAPTOR} captured {PRISONER} as a prisoner of war", Vars = new Dictionary<string, string>(heroes) },
                    new() { Id = "where", Category = FactCategory.Where, TextId = "VividWorld_Fact_HeroTakenPrisoner_Where",
                            Text = "near {SETTLEMENT}", Vars = new Dictionary<string, string> { ["SETTLEMENT"] = "settlement:town_pravend" } },
                    new() { Id = "what", Category = FactCategory.What, TextId = "VividWorld_Fact_HeroTakenPrisoner_What",
                            Text = "stripped of weapons and placed under close guard", Vars = new Dictionary<string, string>() },
                    new() { Id = "outcome", Category = FactCategory.Outcome, TextId = "VividWorld_Fact_HeroTakenPrisoner_Outcome",
                            Text = "then led away", Vars = new Dictionary<string, string>() }
                }
            };
        }

        private static string Render(ComposedRumor composed, EnglishStringTable table)
        {
            string ResolveVar(string val, bool useLinks) => Names.TryGetValue(val, out var name) ? name : val;
            string GetTemplate(string? key, string? fallback) => (key != null ? table.Get(key) : null) ?? fallback ?? string.Empty;
            string GetLocalized(string? key, string english) => table.GetWithFallback(key, english);

            return RumorTextAssembler.Assemble(composed, new PresentationConfig(), ResolveVar, GetTemplate, GetLocalized).PlainText;
        }

        /// <summary>對話當下的那一句抄進玩家紀錄、存檔讀回，再照紀事的做法重組。</summary>
        private static ComposedRumor ThroughTheChronicle(ComposedRumor spoken, string teller, IReadOnlyList<Fact> told)
        {
            var telling = new PlayerHeardSource { HeroId = teller, Day = 12.0, Hop = 1, FactIds = told.Select(f => f.Id).ToList() };
            if (spoken.Feeling != null && spoken.Feeling.Applied)
            {
                telling.FeelingLineKey = spoken.Feeling.LineKey;
                telling.FeelingAddressKey = spoken.Feeling.AddressKey;
                telling.FeelingFocusHeroId = spoken.Feeling.FocusHeroId;
            }
            telling.CaptureSpokenLine(spoken);

            var reloaded = JsonConvert.DeserializeObject<PlayerHeardSource>(JsonConvert.SerializeObject(telling))!;
            Assert.True(reloaded.HasSpokenLine);

            // 事件可能早就被刪掉：重組只拿得到玩家紀錄裡的碎片與這一份來源
            return RumorTextComposer.Reconstruct(told, reloaded, new PresentationConfig(), "evt_capture");
        }

        [Fact]
        public void Participant_WithPersonalityTail_ChronicleShowsTheSameLine()
        {
            var table = ChineseTable();
            var evt = CaptureEvent();
            var prefix = RumorPrefixSelector.SelectPrefix(hop: 0, sourceHeroId: null, isRetell: false, isCorrection: false, isParticipant: true);

            var spoken = RumorTextComposer.Compose(evt, evt.Facts, new PresentationConfig(), prefix, Captor, null,
                isGist: false, heldBack: false, template: CaptureTemplate(),
                speakerTraits: new TraitProfile { HeroId = Captor, Mercy = -1 });

            string said = Render(spoken, table);
            Assert.StartsWith("我在帕拉汶德附近抓了德瑟特", said);
            Assert.Contains("人先關著，吃點苦頭也好", said);
            Assert.DoesNotContain("卡拉多格", said);

            Assert.Equal(said, Render(ThroughTheChronicle(spoken, Captor, evt.Facts), table));
        }

        [Fact]
        public void EyewitnessGist_WithClosing_ChronicleShowsTheSameLine()
        {
            var table = ChineseTable();
            var evt = CaptureEvent();
            var told = evt.Facts.Where(f => f.Id == "who" || f.Id == "where").ToList();
            var prefix = RumorPrefixSelector.SelectPrefix(hop: 0, sourceHeroId: null, isRetell: false, isCorrection: false);

            var spoken = RumorTextComposer.Compose(evt, told, new PresentationConfig(), prefix, Bystander, null,
                isGist: true, heldBack: true, template: CaptureTemplate());

            Assert.False(string.IsNullOrEmpty(spoken.ClosingKey));
            string said = Render(spoken, table);
            Assert.StartsWith("我親眼看到的：", said);
            Assert.Contains(table.Get(spoken.ClosingKey!)!, said);

            Assert.Equal(said, Render(ThroughTheChronicle(spoken, Bystander, told), table));
        }

        [Fact]
        public void HeardFromSomeone_WithFeeling_ChronicleShowsTheSameLine()
        {
            var table = ChineseTable();
            var evt = CaptureEvent();
            var prefix = RumorPrefixSelector.SelectPrefix(hop: 1, sourceHeroId: Informant, isRetell: false, isCorrection: false);

            var spoken = RumorTextComposer.Compose(evt, evt.Facts, new PresentationConfig(), prefix, Bystander, Informant,
                isGist: false, heldBack: false, template: CaptureTemplate());
            spoken.Feeling = new FeelingDecision
            {
                SpeakerId = Bystander,
                EventId = evt.EventId,
                LineKey = "VividWorld_Feeling_Captured_Close_1",
                AddressKey = "VividWorld_Address_Equal_High",
                FocusHeroId = Prisoner
            };

            string said = Render(spoken, table);
            Assert.StartsWith("聽埃爾貢說，", said);
            Assert.Contains("希望老朋友在那裡沒受什麼委屈", said);

            Assert.Equal(said, Render(ThroughTheChronicle(spoken, Bystander, evt.Facts), table));
        }

        [Fact]
        public void SourceWithoutSpokenLine_IsNotTreatedAsAQuote()
        {
            var legacy = JsonConvert.DeserializeObject<PlayerHeardSource>(
                "{\"HeroId\":\"hero_bystander\",\"Day\":12.0,\"Hop\":1,\"FactIds\":[\"who\"],\"FeelingLineKey\":null}")!;

            Assert.False(legacy.HasSpokenLine);
        }
    }
}
