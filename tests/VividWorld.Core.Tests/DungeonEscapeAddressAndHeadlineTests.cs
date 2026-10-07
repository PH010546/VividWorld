#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using VividWorld.Core.Catalog;
using VividWorld.Core.Config;
using VividWorld.Core.Diagnostics;
using VividWorld.Core.Events;
using VividWorld.Core.Feelings;
using VividWorld.Core.Ingest;
using VividWorld.Core.Memory;
using VividWorld.Core.Persistence;
using VividWorld.Core.Presentation;
using VividWorld.Core.Rumors;
using VividWorld.Core.Tests.Fakes;
using Xunit;

namespace VividWorld.Core.Tests
{
    /// <summary>
    /// 關在城鎮或城堡的牢裡而逃脫（不記看守的人、另一套句子）、「趁夜裡看守的人少」的換字、
    /// 逃掉的人大膽版的句尾、感想句的稱呼九格、紀事標題帶被抓的人的名字。
    /// </summary>
    public class DungeonEscapeAddressAndHeadlineTests
    {
        // ────────────── 共用 ──────────────

        private static string FindRepoRoot()
        {
            string? current = AppContext.BaseDirectory;
            while (!string.IsNullOrEmpty(current))
            {
                if (File.Exists(Path.Combine(current, "VividWorld.sln"))) return current;
                current = Directory.GetParent(current)?.FullName;
            }
            throw new DirectoryNotFoundException("repo root not found");
        }

        private static readonly Lazy<EnglishStringTable> En = new(() => EnglishStringTable.LoadFromFile(
            Path.Combine(FindRepoRoot(), "module", "ModuleData", "Languages", "std_module_strings_xml.xml")));

        private static readonly Lazy<EnglishStringTable> Zh = new(() => EnglishStringTable.LoadFromFile(
            Path.Combine(FindRepoRoot(), "module", "ModuleData", "Languages", "CNt", "std_module_strings_xml.xml")));

        private static EventTemplate EscapeTemplate()
        {
            string path = Path.Combine(FindRepoRoot(), "module", "ModuleData", "vividworld_events.json");
            return EventCatalogLoader.Load(File.ReadAllText(path), new PersistenceConfig()).ByType("hero_escaped_captivity")!;
        }

        private static string Segments(EventTemplate t)
            => string.Join("_", t.Facts.Select(f => SentenceCombinationEnumerator.ExtractSegment(f.TextId ?? string.Empty)));

        private const string Prisoner = "hero_prisoner";
        private const string CityOwner = "hero_city_owner";
        private const string Town = "town_x";

        private static EventSubmission BindDungeonEscape(string? linkedEventId = "evt_capture")
        {
            var template = TemplateVariants.EscapeFromDungeon(EscapeTemplate());
            var bindings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["PRISONER"] = Prisoner,
                ["SETTLEMENT"] = Town
            };
            var submission = TemplateBinder.Bind(template, bindings, 20.0, linkedEventId, out var issues);
            Assert.Empty(issues.Where(i => i.IsError));
            Assert.NotNull(submission);
            return submission!;
        }

        private static WorldEvent EventFrom(EventSubmission s, string eventId = "evt_escape")
        {
            return new WorldEvent
            {
                EventId = eventId,
                Type = s.Type,
                Day = s.Day,
                Origin = EventOrigin.Public,
                DramaWeight = s.DramaWeight ?? 4,
                LinkedEventId = s.LinkedEventId,
                Participants = new Dictionary<string, string>(s.Participants),
                Facts = s.Facts.Select(f => f.Clone()).ToList()
            };
        }

        private static string Render(
            WorldEvent evt, int hop, bool chinese, string speaker, string? source = null,
            EventTemplate? template = null, TraitProfile? traits = null, List<string>? info = null)
        {
            var retained = new ThresholdRetentionPolicy(new RetentionConfig()).Retain(evt, hop, string.Empty);
            var table = chinese ? Zh.Value : En.Value;
            var cfg = new PresentationConfig
            {
                EncyclopediaLinksEnabled = false,
                FactSeparator = chinese ? "，" : ", ",
                SentenceEnd = chinese ? "。" : "."
            };
            var composed = RumorTextComposer.Compose(evt, retained, cfg, prefix: null, speakerHeroId: speaker, sourceHeroId: source,
                template: template, speakerTraits: traits);
            var result = RumorTextAssembler.Assemble(
                composed, cfg,
                (val, links) =>
                {
                    if (val == "hero:" + Prisoner) return chinese ? "德瑟特" : "Derthert";
                    if (val == "hero:hero_captor") return chinese ? "卡拉多格" : "Caladog";
                    if (val.StartsWith("settlement:", StringComparison.Ordinal)) return chinese ? "帕拉汶德" : "Pravend";
                    return val;
                },
                (id, fb) => fb == null ? (table.Get(id) ?? string.Empty) : table.Lookup(id, fb),
                (key, fb) => table.GetWithFallback(key, fb),
                onWarning: null, isFemale: id => false, onInfo: info != null ? info.Add : null);
            Assert.True(result.UsedWholeSentence, "fell back to joining fragments: " + result.PlainText);
            return result.PlainText;
        }

        // ────────────── 押著他的是哪一種 ⇒ 用哪一套碎片 ──────────────

        [Fact]
        public void ChooseEscapeShape_HeldBySettlement_IsDungeon_AndTheLogNamesTheSettlementAndWhy()
        {
            var choice = TemplateVariants.ChooseEscapeShape(heldBySettlement: true, heldSettlementId: Town, captorKnown: true);

            Assert.Equal(EscapeShape.FromDungeon, choice.Shape);
            Assert.Contains("escape template: dungeon", choice.Log);
            Assert.Contains("held by settlement " + Town, choice.Log);
            Assert.Contains("no captor role", choice.Log);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("  ")]
        public void ChooseEscapeShape_HeldBySettlementWithoutAnId_StillHasNoCaptor_AndSaysWhy(string? settlementId)
        {
            var choice = TemplateVariants.ChooseEscapeShape(heldBySettlement: true, heldSettlementId: settlementId, captorKnown: true);

            Assert.Equal(EscapeShape.WithoutCaptor, choice.Shape);
            Assert.Contains("no captor role", choice.Log);
            Assert.Contains("id could not be read", choice.Log);
        }

        [Fact]
        public void ChooseEscapeShape_HeldByAParty_BehavesAsBefore()
        {
            var withCaptor = TemplateVariants.ChooseEscapeShape(heldBySettlement: false, heldSettlementId: null, captorKnown: true);
            Assert.Equal(EscapeShape.WithCaptor, withCaptor.Shape);
            Assert.Contains("captor", withCaptor.Log);
            Assert.Null(TemplateVariants.EscapeFor(EscapeTemplate(), withCaptor.Shape));

            var noCaptor = TemplateVariants.ChooseEscapeShape(heldBySettlement: false, heldSettlementId: null, captorKnown: false);
            Assert.Equal(EscapeShape.WithoutCaptor, noCaptor.Shape);
            Assert.Equal("WhoNoCaptor_Where_What_Outcome", Segments(TemplateVariants.EscapeFor(EscapeTemplate(), noCaptor.Shape)!));
        }

        [Fact]
        public void EscapeFromDungeon_HasNoCaptorRole_PlaceIsRequired_AndKeepsTheRestOfTheTemplate()
        {
            var baseTemplate = EscapeTemplate();
            var shape = TemplateVariants.EscapeFor(baseTemplate, EscapeShape.FromDungeon)!;

            Assert.Equal("WhoNoCaptor_WhereDungeon_What_Outcome", Segments(shape));
            Assert.Equal(new[] { "prisoner" }, shape.Roles.Keys.ToArray());
            Assert.False(shape.Facts.Single(f => f.Id == "where").Optional);
            Assert.Equal(baseTemplate.Facts.Single(f => f.Id == "where").Fragility, shape.Facts.Single(f => f.Id == "where").Fragility);
            Assert.Equal("hero_taken_prisoner", shape.LinkedTemplateType);
            Assert.True(shape.ColocatedWitnessAsHearsay);
            Assert.Equal(baseTemplate.Type, shape.Type);
            Assert.Same(baseTemplate.SelfFeelingVariants, shape.SelfFeelingVariants);
        }

        [Fact]
        public void EscapeFromDungeon_WithoutAPlace_DoesNotBind()
        {
            // 這一套一定有城名；模組端讀不到聚落代號時改用「不知道誰關著他」那一套，不會走到這裡
            var template = TemplateVariants.EscapeFromDungeon(EscapeTemplate());
            var bindings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["PRISONER"] = Prisoner };

            var submission = TemplateBinder.Bind(template, bindings, 20.0, "evt_capture", out _);

            Assert.True(submission == null || submission.Facts.All(f => f.Id != "where"));
            if (submission != null)
            {
                // 萬一綁得出來，也只能是沒有地點的那一組，而那一組已經有句子
                Assert.Equal("VividWorld_Sentence_HeroEscaped_WhoNoCaptor_What_Outcome_Self_PRISONER",
                    RumorTextComposer.Compose(EventFrom(submission), submission.Facts, new PresentationConfig(), prefix: null, speakerHeroId: Prisoner).SentenceKeyCandidate);
            }
        }

        // ────────────── 城主不是當事人；連結與過時照舊 ──────────────

        private sealed class CapturingSink : ILogSink
        {
            public List<string> Lines { get; } = new();
            public void Info(string message) => Lines.Add(message);
            public void Warn(string message) => Lines.Add(message);
            public void Error(string message, Exception? ex = null) => Lines.Add(message);
        }

        [Fact]
        public void DungeonEscape_TheCityOwnerIsNotAParticipant_AndPeopleInTownHearItSecondHand()
        {
            var submission = BindDungeonEscape();
            Assert.Equal(new[] { "prisoner" }, submission.Participants.Keys.ToArray());
            Assert.DoesNotContain(CityOwner, submission.Participants.Values);

            var channel = new FakePropagationChannel();
            var traits = new FakeHeroTraitLookup();
            traits.Set(new TraitProfile { HeroId = "lord_in_town", IsAlive = true, IsPrisoner = false, IsLord = true });
            channel.AddWitness(Prisoner, "lord_in_town");

            var evt = EventFrom(submission);
            Hop0Seeding.Seed(evt, submission, channel, traits, new PropagationConfig(), "player", 20.0, new CapturingSink());

            Assert.Null(evt.EntryFor(CityOwner));
            Assert.Null(evt.RoleOf(CityOwner));
            Assert.Equal(0, evt.EntryFor(Prisoner)!.Hop);
            var inTown = evt.EntryFor("lord_in_town")!;
            Assert.Equal(1, inTown.Hop);
            Assert.Null(inTown.SourceHeroId);
        }

        [Fact]
        public void DungeonEscape_WithNoCaptorRole_StillLinksToTheCapture_AndMakesTheCaptureOutdatedForThoseWhoHear()
        {
            var submission = BindDungeonEscape(linkedEventId: "evt_capture");
            Assert.Equal("evt_capture", submission.LinkedEventId);
            Assert.False(submission.Participants.ContainsKey("captor"));

            var capture = new WorldEvent
            {
                EventId = "evt_capture",
                Type = "hero_taken_prisoner",
                Day = 10.0,
                Origin = EventOrigin.Public,
                Participants = new Dictionary<string, string> { ["prisoner"] = Prisoner, ["captor"] = "hero_captor" },
                KnownBy = new List<KnownByEntry>
                {
                    new KnownByEntry { HeroId = "lord_in_town", Hop = 2, LearnedDay = 11.0 },
                    new KnownByEntry { HeroId = "lord_far_away", Hop = 3, LearnedDay = 12.0 }
                }
            };

            var release = EventFrom(submission);
            var channel = new FakePropagationChannel();
            var traits = new FakeHeroTraitLookup();
            traits.Set(new TraitProfile { HeroId = "lord_in_town", IsAlive = true, IsPrisoner = false, IsLord = true });
            channel.AddWitness(Prisoner, "lord_in_town");
            Hop0Seeding.Seed(release, submission, channel, traits, new PropagationConfig(), "player", 20.0, new CapturingSink());

            // 聽到逃脫的人，手上那則被俘就過時：只看逃脫連到哪一則被俘，跟逃脫有沒有「看守的人」無關
            Assert.Equal(capture.EventId, release.LinkedEventId);
            var marked = new List<string>();
            int count = Outdating.MarkOutdated(capture, release.KnownBy.Select(k => k.HeroId), 20.0, marked);

            Assert.Equal(1, count);
            Assert.Equal(new[] { "lord_in_town" }, marked);
            Assert.True(Outdating.IsOutdated(capture.EntryFor("lord_in_town")));
            Assert.False(Outdating.IsOutdated(capture.EntryFor("lord_far_away")));
        }

        // ────────────── 句子：中文逐字、英文對照 ──────────────

        [Fact]
        public void DungeonEscape_FirstHand_SomeoneWhoWasInThatTown()
        {
            var evt = EventFrom(BindDungeonEscape());

            Assert.Equal("德瑟特趁夜裡看守的人少，從帕拉汶德的牢裡逃了出來，誰也沒看清他往哪跑了。",
                Render(evt, hop: 1, chinese: true, speaker: "hero_onlooker"));
            Assert.Equal("On a night when the guards were few, Derthert escaped from the dungeon at Pravend, and no one saw which way he went.",
                Render(evt, hop: 1, chinese: false, speaker: "hero_onlooker"));
        }

        [Fact]
        public void DungeonEscape_FirstHand_HeardFromTheEscapedPrisoner()
        {
            var evt = EventFrom(BindDungeonEscape());

            Assert.Equal("他趁夜裡看守的人少，從帕拉汶德的牢裡逃了出來。",
                Render(evt, hop: 1, chinese: true, speaker: "hero_onlooker", source: Prisoner));
            Assert.Equal("He escaped from the dungeon at Pravend on a night when the guards were few.",
                Render(evt, hop: 1, chinese: false, speaker: "hero_onlooker", source: Prisoner));
        }

        [Theory]
        [InlineData(2)]
        [InlineData(3)]
        public void DungeonEscape_SecondAndThirdHand_KeepTheTownButNotTheDetails(int hop)
        {
            var evt = EventFrom(BindDungeonEscape());

            Assert.Equal("德瑟特從帕拉汶德的牢裡逃了出來。", Render(evt, hop, chinese: true, speaker: "hero_onlooker"));
            Assert.Equal("Derthert escaped from the dungeon at Pravend.", Render(evt, hop, chinese: false, speaker: "hero_onlooker"));
        }

        [Theory]
        [InlineData(4)]
        [InlineData(5)]
        public void DungeonEscape_FourthHandOnward_HasForgottenTheTown_AndUsesTheExistingSentence(int hop)
        {
            var evt = EventFrom(BindDungeonEscape());

            Assert.Equal("德瑟特從牢裡逃了出來。", Render(evt, hop, chinese: true, speaker: "hero_onlooker"));
            Assert.Equal("Derthert escaped from prison.", Render(evt, hop, chinese: false, speaker: "hero_onlooker"));
        }

        [Fact]
        public void DungeonEscape_ThePrisonerTellsIt_WithTheUsualClosingLine()
        {
            var template = TemplateVariants.EscapeFromDungeon(EscapeTemplate());
            var evt = EventFrom(BindDungeonEscape());
            var cautious = new TraitProfile { HeroId = Prisoner, Valor = 0 };

            Assert.Equal("我趁夜裡看守的人少，從帕拉汶德的牢裡逃出來了。我一路都沒敢回頭。",
                Render(evt, hop: 0, chinese: true, speaker: Prisoner, template: template, traits: cautious));
            Assert.Equal("On a night when the guards were few, I escaped from the dungeon at Pravend. I didn't dare look back the whole way.",
                Render(evt, hop: 0, chinese: false, speaker: Prisoner, template: template, traits: cautious));
        }

        [Fact]
        public void DungeonEscape_NobodyCanSayFromMyHands()
        {
            // 城主不在角色裡：就算他是說話的人，也只會用旁人的句子
            var evt = EventFrom(BindDungeonEscape());

            string line = Render(evt, hop: 1, chinese: true, speaker: CityOwner);

            Assert.DoesNotContain("我", line);
            Assert.Equal("德瑟特趁夜裡看守的人少，從帕拉汶德的牢裡逃了出來，誰也沒看清他往哪跑了。", line);
        }

        [Fact]
        public void EscapeFromALordsParty_StillSaysFromHisHands_WithTheNewWording()
        {
            var bindings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["PRISONER"] = Prisoner, ["CAPTOR"] = "hero_captor", ["SETTLEMENT"] = Town
            };
            var submission = TemplateBinder.Bind(EscapeTemplate(), bindings, 20.0, "evt_capture", out _)!;
            var evt = EventFrom(submission);

            Assert.Equal("德瑟特在帕拉汶德趁夜裡看守的人少，從卡拉多格手中逃了出來，誰也沒看清他往哪跑了。",
                Render(evt, hop: 1, chinese: true, speaker: "hero_onlooker"));
            Assert.Equal("在帕拉汶德附近，德瑟特趁夜裡看守的人少，從我手中逃掉了，等發現的時候早就不見人影了。說來丟臉。",
                Render(evt, hop: 0, chinese: true, speaker: "hero_captor"));
            Assert.Equal("Near Pravend, on a night when the guards were few, Derthert got away from me. By the time anyone noticed, he was long gone. It shames me to say it.",
                Render(evt, hop: 0, chinese: false, speaker: "hero_captor"));
        }

        [Fact]
        public void OldEscapeEvent_RecordedWithTheCityOwnerAsCaptor_IsStillToldTheOldWay()
        {
            // 更新前存下來的逃脫：城主記在看守的人那個角色上、碎片是「從某某手中」那一套，照原樣講
            var evt = new WorldEvent
            {
                EventId = "evt_old", Type = "hero_escaped_captivity", Origin = EventOrigin.Public, DramaWeight = 4,
                Participants = new Dictionary<string, string> { ["captor"] = "hero_captor", ["prisoner"] = Prisoner },
                Facts = new List<Fact>
                {
                    new Fact { Id = "who", Category = FactCategory.Who, TextId = "VividWorld_Fact_HeroEscaped_Who", Text = "x", Fragility = 1,
                        Vars = new Dictionary<string, string> { ["CAPTOR"] = "hero:hero_captor", ["PRISONER"] = "hero:" + Prisoner } },
                    new Fact { Id = "where", Category = FactCategory.Where, TextId = "VividWorld_Fact_HeroEscaped_Where", Text = "x", Fragility = 2,
                        Vars = new Dictionary<string, string> { ["SETTLEMENT"] = "settlement:" + Town } }
                }
            };

            Assert.Equal("德瑟特在帕拉汶德附近從卡拉多格手中逃了出來。", Render(evt, hop: 2, chinese: true, speaker: "hero_onlooker"));
            Assert.Equal("德瑟特在帕拉汶德附近從我手中逃掉了。說來丟臉。", Render(evt, hop: 2, chinese: true, speaker: "hero_captor"));
        }

        [Fact]
        public void DevToolRenderOfEveryTemplate_ListsTheDungeonShape_WithWholeSentencesInBothLanguages()
        {
            string root = FindRepoRoot();
            var templates = new List<EventTemplate>();
            foreach (var file in new[] { "vividworld_events.json", "vividworld_situation_events.json" })
            {
                templates.AddRange(EventCatalogLoader.Load(
                    File.ReadAllText(Path.Combine(root, "module", "ModuleData", file)), new PersistenceConfig()).Templates);
            }

            string output = TemplateRenderSampleFormatter.FormatAll(templates, En.Value, Zh.Value);

            Assert.Contains("[hero_escaped_captivity (WhereDungeon)]", output);
            Assert.Contains("[整句] 旁人（WhoNoCaptor_WhereDungeon_What_Outcome）：德瑟特趁夜裡看守的人少，從帕拉汶德的牢裡逃了出來，誰也沒看清他往哪跑了。", output);
            Assert.Contains("[整句] 聽PRISONER說（WhoNoCaptor_WhereDungeon_What_Outcome）：他趁夜裡看守的人少，從帕拉汶德的牢裡逃了出來。", output);
            Assert.Contains("[整句] 旁人（WhoNoCaptor_WhereDungeon）：德瑟特從帕拉汶德的牢裡逃了出來。", output);
            Assert.Contains("[整句] 當事人 PRISONER（WhoNoCaptor_WhereDungeon_What_Outcome）：我趁夜裡看守的人少，從帕拉汶德的牢裡逃出來了。我一路都沒敢回頭。", output);
            Assert.Contains("【句尾版本 Valor：valor >= 1】我趁夜裡看守的人少，從帕拉汶德的牢裡逃出來了。想關住我，沒那麼容易。", output);
            Assert.Contains("[Whole] Onlooker (WhoNoCaptor_WhereDungeon): Derthert escaped from the dungeon at Pravend.", output);
            Assert.DoesNotContain("[Concat]", output);
            Assert.DoesNotContain("[拼接]", output);
            Assert.Contains("Missing Sentence Keys in English (Count: 0)", output);
            Assert.Contains("Missing Sentence Keys in Traditional Chinese (Count: 0)", output);
            Assert.Contains("Unreferenced Sentence Keys in English (Count: 0)", output);
            Assert.Contains("Unreferenced Sentence Keys in Traditional Chinese (Count: 0)", output);
        }

        // ────────────── 換字：趁夜裡看守的人少 ──────────────

        [Fact]
        public void OldWording_IsGoneFromBothTables_AndTheNewWordingIsInEverySentenceThatHadIt()
        {
            var zhValues = Zh.Value.Keys.ToDictionary(k => k, k => Zh.Value.Get(k)!);
            var enValues = En.Value.Keys.ToDictionary(k => k, k => En.Value.Get(k)!);

            Assert.DoesNotContain(zhValues, kv => kv.Value.Contains("趁著看守稀疏的長夜"));
            Assert.DoesNotContain(zhValues, kv => kv.Value.Contains("看守稀疏，而夜很長"));
            Assert.DoesNotContain(enValues, kv => kv.Value.IndexOf("long night when the guard was thin", StringComparison.OrdinalIgnoreCase) >= 0);

            Assert.Equal("夜裡看守的人少", Zh.Value.Get("VividWorld_Fact_HeroEscaped_What"));
            Assert.Equal("the guards were few that night", En.Value.Get("VividWorld_Fact_HeroEscaped_What"));

            // 兩個語言裡帶這個說法的句子是同一批鍵
            var zhKeys = zhValues.Where(kv => kv.Key.StartsWith("VividWorld_Sentence_HeroEscaped_", StringComparison.Ordinal) && kv.Value.Contains("趁夜裡看守的人少"))
                .Select(kv => kv.Key).OrderBy(k => k, StringComparer.Ordinal).ToList();
            var enKeys = enValues.Where(kv => kv.Key.StartsWith("VividWorld_Sentence_HeroEscaped_", StringComparison.Ordinal)
                    && kv.Value.IndexOf("on a night when the guards were few", StringComparison.OrdinalIgnoreCase) >= 0)
                .Select(kv => kv.Key).OrderBy(k => k, StringComparer.Ordinal).ToList();
            Assert.Equal(zhKeys, enKeys);
            Assert.Equal(17, zhKeys.Count);   // 原有的 14 句，加上城裡的牢那一套裡帶這個說法的 3 句
        }

        // ────────────── 逃掉的人，大膽的句尾 ──────────────

        [Theory]
        [InlineData(1, "想關住我，沒那麼容易", "It takes more than that to hold me")]
        [InlineData(2, "想關住我，沒那麼容易", "It takes more than that to hold me")]
        [InlineData(0, "我一路都沒敢回頭", "I didn't dare look back the whole way")]
        [InlineData(-1, "我一路都沒敢回頭", "I didn't dare look back the whole way")]
        public void EscapedPrisoner_ClosingLine_FollowsValor_InTheDungeonAndInTheField(int valor, string zhTail, string enTail)
        {
            var traits = new TraitProfile { HeroId = Prisoner, Valor = valor };
            var baseTemplate = EscapeTemplate();

            // 關在城裡的牢
            var dungeon = EventFrom(BindDungeonEscape());
            var dungeonTemplate = TemplateVariants.EscapeFromDungeon(baseTemplate);
            var info = new List<string>();
            Assert.Equal("我趁夜裡看守的人少，從帕拉汶德的牢裡逃出來了。" + zhTail + "。",
                Render(dungeon, 0, chinese: true, speaker: Prisoner, template: dungeonTemplate, traits: traits, info: info));
            Assert.EndsWith(enTail + ".", Render(dungeon, 0, chinese: false, speaker: Prisoner, template: dungeonTemplate, traits: traits));
            Assert.Contains(info, l => l.Contains("self feeling variant") && l.Contains("valor=" + valor));

            // 押在野外的隊伍裡
            var bindings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["PRISONER"] = Prisoner, ["CAPTOR"] = "hero_captor", ["SETTLEMENT"] = Town
            };
            var field = EventFrom(TemplateBinder.Bind(baseTemplate, bindings, 20.0, null, out _)!);
            Assert.Equal("在帕拉汶德附近，我趁夜裡看守的人少，從卡拉多格手中逃出來了。" + zhTail + "。",
                Render(field, 0, chinese: true, speaker: Prisoner, template: baseTemplate, traits: traits));

            // 不知道是誰關著的
            var noCaptorTemplate = TemplateVariants.EscapeWithoutCaptor(baseTemplate);
            var noCaptorBindings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["PRISONER"] = Prisoner, ["SETTLEMENT"] = Town };
            var noCaptor = EventFrom(TemplateBinder.Bind(noCaptorTemplate, noCaptorBindings, 20.0, null, out _)!);
            Assert.Equal("在帕拉汶德附近，我趁夜裡看守的人少，從牢裡逃出來了。" + zhTail + "。",
                Render(noCaptor, 0, chinese: true, speaker: Prisoner, template: noCaptorTemplate, traits: traits));
        }

        [Fact]
        public void EscapedPrisoner_ValorVariant_IsDeclaredAtPlusOne_AndTheCaptorsVariantsAreUntouched()
        {
            var template = EscapeTemplate();

            var prisonerRules = SelfFeelingVariantSelector.RulesFor(template, "prisoner")!;
            var rule = Assert.Single(prisonerRules);
            Assert.Equal("Valor", rule.Tendency);
            Assert.Equal("valor", rule.Trait.ToLowerInvariant());
            Assert.Equal(1, rule.Min);
            Assert.Null(rule.Max);

            Assert.Equal(new[] { "Cruel", "Calculating" }, SelfFeelingVariantSelector.RulesFor(template, "captor")!.Select(r => r.Tendency));
        }

        // ────────────── 感想句的稱呼九格 ──────────────

        private static string RenderAddress(EnglishStringTable table, string addressKey, string focusName, string sentenceEnd)
        {
            var decision = new FeelingDecision
            {
                SpeakerId = "hero_speaker",
                EventId = "evt",
                LineKey = "VividWorld_Feeling_Pending_Hostile_1",
                AddressKey = addressKey,
                FocusHeroId = "hero_focus"
            };
            string? line = RumorTextAssembler.RenderFeeling(
                decision, false,
                (val, links) => val == "hero:hero_focus" ? focusName : val,
                (id, fb) => fb == null ? (table.Get(id) ?? string.Empty) : table.Lookup(id, fb),
                (key, fb) => table.GetWithFallback(key, fb),
                sentenceEnd, out string? missing, id => false);
            Assert.Null(missing);
            Assert.NotNull(line);
            return line!;
        }

        [Theory]
        [InlineData("Higher", "High", "卡拉多格大人", true)]
        [InlineData("Higher", "Neutral", "卡拉多格", true)]
        [InlineData("Higher", "Low", "那位大人物", false)]
        [InlineData("Equal", "High", "老朋友", false)]
        [InlineData("Equal", "Neutral", "卡拉多格", true)]
        [InlineData("Equal", "Low", "那傢伙", false)]
        [InlineData("Lower", "High", "老朋友", false)]
        [InlineData("Lower", "Neutral", "卡拉多格", true)]
        [InlineData("Lower", "Low", "那個人", false)]
        public void Address_Chinese_NineCells_RenderAsDecided_AndNameCellsShowTheFocusHerosName(
            string standing, string affection, string expectedAddress, bool carriesName)
        {
            string key = FeelingGrid.AddressKey(
                (StandingComparison)Enum.Parse(typeof(StandingComparison), standing),
                (AffectionLevel)Enum.Parse(typeof(AffectionLevel), affection));
            Assert.Equal("VividWorld_Address_" + standing + "_" + affection, key);

            string line = RenderAddress(Zh.Value, key, "卡拉多格", "。");

            Assert.Equal(expectedAddress + "最好別又鬧出什麼事來。", line);
            Assert.Equal(carriesName, line.Contains("卡拉多格"));
            Assert.DoesNotContain("{", line);
            Assert.DoesNotContain("}", line);

            // 名字那幾格換一個人就跟著換，不是寫死的字
            string other = RenderAddress(Zh.Value, key, "伊拉", "。");
            Assert.Equal(carriesName, other.Contains("伊拉"));
        }

        [Theory]
        [InlineData("Higher", "High", "Caladog")]
        [InlineData("Higher", "Neutral", "Caladog")]
        [InlineData("Higher", "Low", "That grandee")]
        [InlineData("Equal", "High", "My old friend")]
        [InlineData("Equal", "Neutral", "Caladog")]
        [InlineData("Equal", "Low", "That one")]
        [InlineData("Lower", "High", "My old friend")]
        [InlineData("Lower", "Neutral", "Caladog")]
        [InlineData("Lower", "Low", "That person")]
        public void Address_English_NineCells(string standing, string affection, string expectedStart)
        {
            string line = RenderAddress(En.Value, "VividWorld_Address_" + standing + "_" + affection, "Caladog", ".");

            Assert.StartsWith(expectedStart + " ", line);
            Assert.DoesNotContain("{", line);
        }

        // ────────────── 紀事的標題帶被抓的人的名字 ──────────────

        private static readonly (string Type, string Zh, string En, string PlainZh)[] NamedTitles =
        {
            ("hero_taken_prisoner", "科爾被俘", "Corr was taken prisoner", "被俘"),
            ("hero_released", "科爾獲釋", "Corr was released", "俘虜獲釋"),
            ("hero_escaped_captivity", "科爾脫逃", "Corr escaped", "俘虜脫逃"),
            ("hero_captured_by_bandits", "科爾被盜匪俘虜", "Corr was captured by bandits", "被盜匪俘虜"),
            ("hero_rescued_from_bandits", "科爾從盜匪手裡獲救", "Corr was rescued from bandits", "從盜匪手裡獲救"),
            ("hero_escaped_bandits", "科爾從盜匪手裡逃脫", "Corr escaped from bandits", "從盜匪手裡逃脫")
        };

        private static Func<string?, string?, string> Lookup(EnglishStringTable table)
            => (id, fb) => fb == null ? (table.Get(id) ?? string.Empty) : table.Lookup(id, fb);

        private static ChronicleEntry EntryFor(string type, string? prisonerId = "lord_corr")
        {
            var heard = new PlayerHeardEntry
            {
                EventId = "evt_" + type,
                Type = type,
                Day = 10.0,
                LearnedDay = 11.0,
                PlayerHop = 1,
                SourceHeroId = "hero_source",
                Participants = prisonerId != null
                    ? new Dictionary<string, string> { ["prisoner"] = prisonerId, ["captor"] = "lord_other" }
                    : new Dictionary<string, string>(),
                Facts = new List<Fact>()
            };
            var writer = new FailingFileWriter();
            writer.WriteAllText("player_heard.json", VividJson.Write(new PlayerHeardLog { Entries = new List<PlayerHeardEntry> { heard } }));
            var store = new PlayerHeardLogStore("player_heard.json", writer);
            store.Load();
            var provider = new ChronicleProvider(store, _ => new EventTemplate { Type = type, Headline = "plain english headline" }, new PresentationConfig());
            return Assert.Single(provider.ForPlayer(50, 100.0, out _));
        }

        private static string? CorrName(string nameVar, bool chinese)
            => nameVar == "hero:lord_corr" ? (chinese ? "科爾" : "Corr") : null;

        [Fact]
        public void Headline_SixCaptivityKinds_CarryThePrisonersName_InBothLanguages()
        {
            Assert.Equal(NamedTitles.Select(t => t.Type).OrderBy(t => t, StringComparer.Ordinal),
                ChronicleHeadline.TypesWithNamedHeadline.OrderBy(t => t, StringComparer.Ordinal));

            foreach (var row in NamedTitles)
            {
                var entry = EntryFor(row.Type);
                Assert.Equal("VividWorld_EventTypeNamed_" + row.Type, entry.NamedHeadlineTextId);
                Assert.Equal("hero:lord_corr", entry.HeadlineNameVar);
                Assert.Equal("VividWorld_EventType_" + row.Type, entry.HeadlineTextId);

                var zh = ChronicleHeadline.Resolve(entry, Lookup(Zh.Value), v => CorrName(v, chinese: true));
                Assert.True(zh.UsedNamed);
                Assert.Equal(row.Zh, zh.Text);
                Assert.Equal("named (hero:lord_corr)", zh.Note);

                var en = ChronicleHeadline.Resolve(entry, Lookup(En.Value), v => CorrName(v, chinese: false));
                Assert.True(en.UsedNamed);
                Assert.Equal(row.En, en.Text);
            }
        }

        [Fact]
        public void Headline_NameCannotBeFound_FallsBackToThePlainTitle_WithoutAnyBraces()
        {
            foreach (var row in NamedTitles)
            {
                var entry = EntryFor(row.Type);

                // 這個人查不到（例如已經不在遊戲裡）
                var gone = ChronicleHeadline.Resolve(entry, Lookup(Zh.Value), _ => null);
                Assert.False(gone.UsedNamed);
                Assert.Equal(row.PlainZh, gone.Text);
                Assert.Contains("fell back", gone.Note);
                Assert.Contains("hero:lord_corr", gone.Note);
                Assert.DoesNotContain("{", gone.Text);

                var blank = ChronicleHeadline.Resolve(entry, Lookup(Zh.Value), _ => "  ");
                Assert.Equal(row.PlainZh, blank.Text);

                // 紀錄裡沒記被抓的是誰（舊紀錄）
                var noRole = EntryFor(row.Type, prisonerId: null);
                Assert.Null(noRole.HeadlineNameVar);
                var old = ChronicleHeadline.Resolve(noRole, Lookup(Zh.Value), v => CorrName(v, chinese: true));
                Assert.False(old.UsedNamed);
                Assert.Equal(row.PlainZh, old.Text);
                Assert.Contains("does not say who the prisoner is", old.Note);
                Assert.DoesNotContain("{", old.Text);
            }
        }

        [Fact]
        public void Headline_NamedStringMissingOrBroken_FallsBackToThePlainTitle()
        {
            var entry = EntryFor("hero_taken_prisoner");

            // 目前語言沒有帶名字的那一句
            var plainOnly = new EnglishStringTable(new Dictionary<string, string> { ["VividWorld_EventType_hero_taken_prisoner"] = "被俘" });
            var missing = ChronicleHeadline.Resolve(entry, Lookup(plainOnly), v => "科爾");
            Assert.False(missing.UsedNamed);
            Assert.Equal("被俘", missing.Text);
            Assert.Contains("no string 'VividWorld_EventTypeNamed_hero_taken_prisoner'", missing.Note);

            // 帶名字的那一句裡有換不掉的佔位符
            var broken = new EnglishStringTable(new Dictionary<string, string>
            {
                ["VividWorld_EventType_hero_taken_prisoner"] = "被俘",
                ["VividWorld_EventTypeNamed_hero_taken_prisoner"] = "{PRISONER}被{CAPTOR}俘虜"
            });
            var leftover = ChronicleHeadline.Resolve(entry, Lookup(broken), v => "科爾");
            Assert.False(leftover.UsedNamed);
            Assert.Equal("被俘", leftover.Text);
            Assert.Contains("placeholder", leftover.Note);

            // 連不帶名字的字串都沒有時用模板的英文標題（原本的行為）
            var empty = new EnglishStringTable(new Dictionary<string, string>());
            Assert.Equal("plain english headline", ChronicleHeadline.Resolve(entry, Lookup(empty), _ => null).Text);
        }

        [Fact]
        public void Headline_OtherKindsOfNews_KeepTheirPlainTitle()
        {
            foreach (var type in new[] { "talk_denied_spoke_against_ruler", "talk_denied_mistreated_prisoner", "talk_not_so_seat_dispute_yielded", "talk_not_so_tavern_good_word" })
            {
                Assert.Null(ChronicleHeadline.NamedTextIdFor(type));
                Assert.Null(Zh.Value.Get("VividWorld_EventTypeNamed_" + type));
                Assert.Null(En.Value.Get("VividWorld_EventTypeNamed_" + type));

                var entry = EntryFor(type);
                Assert.Null(entry.NamedHeadlineTextId);
                var result = ChronicleHeadline.Resolve(entry, Lookup(Zh.Value), v => "科爾");
                Assert.False(result.UsedNamed);
                Assert.Equal(Zh.Value.Get("VividWorld_EventType_" + type), result.Text);
                Assert.DoesNotContain("科爾", result.Text);
                Assert.Contains("no named title", result.Note);
            }
            Assert.Null(ChronicleHeadline.NamedTextIdFor(null));
        }

        [Fact]
        public void Headline_PrisonerIsFoundFromTheFactsWhenTheRecordHasNoRoles()
        {
            var heard = new PlayerHeardEntry
            {
                EventId = "evt_x", Type = "hero_taken_prisoner",
                Facts = new List<Fact>
                {
                    new Fact { Id = "who", Category = FactCategory.Who, TextId = "VividWorld_Fact_HeroTakenPrisoner_Who", Text = "x",
                        Vars = new Dictionary<string, string> { ["CAPTOR"] = "hero:lord_other", ["PRISONER"] = "hero:lord_corr" } }
                }
            };
            Assert.Equal("hero:lord_corr", ChronicleHeadline.PrisonerVarOf(heard));

            heard.Facts[0].Vars!["PRISONER"] = "hero:";
            Assert.Null(ChronicleHeadline.PrisonerVarOf(heard));
            Assert.Null(ChronicleHeadline.PrisonerVarOf(null));
        }

        [Fact]
        public void Headline_NamedStrings_ExistInBothTables_EachWithExactlyTheNamePlaceholder_AndPlainTitlesAreUnchanged()
        {
            foreach (var row in NamedTitles)
            {
                foreach (var table in new[] { En.Value, Zh.Value })
                {
                    string? named = table.Get("VividWorld_EventTypeNamed_" + row.Type);
                    Assert.False(string.IsNullOrEmpty(named));
                    Assert.Contains(ChronicleHeadline.NameToken, named);
                    Assert.DoesNotContain("{", named!.Replace(ChronicleHeadline.NameToken, string.Empty));
                }
                Assert.Equal(row.PlainZh, Zh.Value.Get("VividWorld_EventType_" + row.Type));
            }
        }

        [Fact]
        public void ChronicleLog_SaysWhichTitleWasUsed()
        {
            var sources = new List<ChronicleSource> { new() { HeroId = "hero_a", Hop = 1, FactCount = 2, HasSpokenLine = true } };
            string plainLine = ChronicleLogFormatter.FormatEntrySources("evt_1", sources);

            Assert.Equal(plainLine + "; title: named (hero:lord_corr)",
                ChronicleLogFormatter.FormatEntrySources("evt_1", sources, "named (hero:lord_corr)"));
            Assert.Equal(plainLine, ChronicleLogFormatter.FormatEntrySources("evt_1", sources, null));
        }
    }
}
