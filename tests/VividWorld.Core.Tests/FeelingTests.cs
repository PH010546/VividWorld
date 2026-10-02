using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Newtonsoft.Json.Linq;
using VividWorld.Core.Catalog;
using VividWorld.Core.Config;
using VividWorld.Core.Dialogue;
using VividWorld.Core.Events;
using VividWorld.Core.Feelings;
using VividWorld.Core.Grudges;
using VividWorld.Core.Memory;
using VividWorld.Core.Persistence;
using VividWorld.Core.Presentation;
using VividWorld.Core.Rumors;
using VividWorld.Core.Tests.Fakes;
using VividWorld.Core.Util;
using Xunit;

namespace VividWorld.Core.Tests
{
    /// <summary>說話的人的感想：焦點人物、九宮格、稱呼、挑句、不產生的情況、字串表與模板資料。</summary>
    public class FeelingTests
    {
        // ───────────── 假世界與組裝 ─────────────

        private sealed class FakeWorld : IDialogueWorld
        {
            public double Today { get; set; } = 100.0;
            public Dictionary<string, int> Aff = new(StringComparer.Ordinal);
            public Dictionary<string, int> Rank = new(StringComparer.Ordinal);
            public Dictionary<string, string> Clan = new(StringComparer.Ordinal);
            public Dictionary<string, bool> Female = new(StringComparer.Ordinal);
            public List<GrudgeEntry> Grudges = new();

            public int? Affection(string speakerId, string heroId)
                => Aff.TryGetValue(speakerId + ">" + heroId, out var v) ? v : 0;

            public int? StandingRank(string heroId) => Rank.TryGetValue(heroId, out var r) ? r : 0;

            public bool? IsFemale(string heroId) => Female.TryGetValue(heroId, out var f) ? f : (bool?)null;

            public InterestHeroFacts? InterestFacts(string heroId)
                => new InterestHeroFacts(heroId, clanId: Clan.TryGetValue(heroId, out var c) ? c : null);

            public IReadOnlyList<GrudgeEntry> PersonalGrudges(string speakerId, string heroId)
                => Grudges.Where(g => g.FromHeroId == speakerId && g.AboutHeroId == heroId && g.Scope == GrudgeScope.Personal).ToList();

            public string NameOf(string heroId) => heroId;

            public string? PlayerKingdomLeaderId => null;
            public bool IsPlayerCompanion(string heroId) => false;
            public bool IsPlayerClanMember(string heroId) => false;
            public bool IsPlayerSpouse(string heroId) => false;
        }

        private const string Spk = "spk";
        private const string Victim = "victim";
        private const string Killer = "killer";

        private static string RepoFile(params string[] parts)
        {
            string? current = AppContext.BaseDirectory;
            while (!string.IsNullOrEmpty(current))
            {
                if (File.Exists(Path.Combine(current, "VividWorld.sln")))
                {
                    return Path.Combine(new[] { current }.Concat(parts).ToArray());
                }
                current = Directory.GetParent(current)?.FullName;
            }
            throw new FileNotFoundException("repo root not found");
        }

        private static readonly Lazy<FeelingCatalog> RealFeelings = new(() =>
            FeelingCatalog.Parse(File.ReadAllText(RepoFile("module", "ModuleData", "vividworld_feelings.json"))));

        private static readonly Lazy<List<EventCatalog>> RealCatalogs = new(() =>
            new[] { "vividworld_events.json", "vividworld_situation_events.json" }
                .Select(f => EventCatalogLoader.Load(File.ReadAllText(RepoFile("module", "ModuleData", f)), new PersistenceConfig { MaxFactsPerEvent = 24 }))
                .ToList());

        private static EventTemplate? RealTemplate(string type)
            => RealCatalogs.Value.Select(c => c.ByType(type)).FirstOrDefault(t => t != null);

        private static WorldEvent DiedInBattle(string eventId = "evt_1")
        {
            return new WorldEvent
            {
                EventId = eventId,
                Type = "hero_died_in_battle",
                Origin = EventOrigin.Public,
                Day = 90,
                DramaWeight = 3,
                Participants = new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["victim"] = Victim,
                    ["killer"] = Killer
                }
            };
        }

        private sealed class Rig
        {
            public readonly FakeWorld World = new();
            public readonly VividWorldConfig Cfg = new();
            public readonly FakeHeroTraitLookup Traits = new();
            public Func<string, EventTemplate?> Templates = RealTemplate;
            public long Seed = 7;

            public Rig()
            {
                // 說話的人與死者同一氏族，焦點人物就會是死者（氏族保底 0.4 高過陌生人保底）
                World.Clan[Spk] = "c1";
                World.Clan[Victim] = "c1";
                World.Clan[Killer] = "c2";
                Traits.Set(new TraitProfile { HeroId = Spk });
            }

            public FeelingResolver Resolver() => new FeelingResolver(Cfg, RealFeelings.Value, World, Traits, Templates, Seed);

            public FeelingDecision Resolve(WorldEvent? evt = null, bool gist = false, string speaker = Spk)
                => Resolver().Resolve(evt ?? DiedInBattle(), speaker, gist);

            public void SetAffection(int value) => World.Aff[Spk + ">" + Victim] = value;

            public void AddGrudge(string eventId, double requested, string about = Victim)
                => World.Grudges.Add(new GrudgeEntry
                {
                    EventId = eventId, FromHeroId = Spk, AboutHeroId = about, Day = 100.0,
                    Requested = requested, Delta = 0, Scope = GrudgeScope.Personal
                });

            public void SetTrait(Action<TraitProfile> set)
            {
                var p = new TraitProfile { HeroId = Spk };
                set(p);
                Traits.Set(p);
            }
        }

        // ───────────── 九宮格 ─────────────

        [Theory]
        [InlineData(AffectionLevel.High, GrudgeLevel.None, FeelingMood.Close)]
        [InlineData(AffectionLevel.High, GrudgeLevel.Favor, FeelingMood.Close)]
        [InlineData(AffectionLevel.High, GrudgeLevel.Grudge, FeelingMood.Sore)]
        [InlineData(AffectionLevel.Neutral, GrudgeLevel.None, FeelingMood.Neutral)]
        [InlineData(AffectionLevel.Neutral, GrudgeLevel.Favor, FeelingMood.Close)]
        [InlineData(AffectionLevel.Neutral, GrudgeLevel.Grudge, FeelingMood.Hostile)]
        [InlineData(AffectionLevel.Low, GrudgeLevel.None, FeelingMood.Hostile)]
        [InlineData(AffectionLevel.Low, GrudgeLevel.Favor, FeelingMood.Owe)]
        [InlineData(AffectionLevel.Low, GrudgeLevel.Grudge, FeelingMood.Hostile)]
        public void Grid_EachOfTheNineCells_MapsToItsMood(AffectionLevel a, GrudgeLevel g, FeelingMood expected)
        {
            Assert.Equal(expected, FeelingGrid.MoodOf(a, g));
        }

        [Theory]
        [InlineData(30, AffectionLevel.High)]
        [InlineData(29, AffectionLevel.Neutral)]
        [InlineData(-29, AffectionLevel.Neutral)]
        [InlineData(-30, AffectionLevel.Low)]
        [InlineData(100, AffectionLevel.High)]
        [InlineData(-100, AffectionLevel.Low)]
        public void Grid_AffectionBoundaries_AreInclusive(int affection, AffectionLevel expected)
        {
            Assert.Equal(expected, FeelingGrid.Classify(affection, new FeelingsConfig()));
        }

        [Theory]
        [InlineData(4.0, GrudgeLevel.Favor)]
        [InlineData(3.99, GrudgeLevel.None)]
        [InlineData(-3.99, GrudgeLevel.None)]
        [InlineData(-4.0, GrudgeLevel.Grudge)]
        [InlineData(0.0, GrudgeLevel.None)]
        public void Grid_GrudgeBoundaries_AreInclusive(double net, GrudgeLevel expected)
        {
            Assert.Equal(expected, FeelingGrid.Classify(net, new FeelingsConfig()));
        }

        [Fact]
        public void Grid_ThresholdsComeFromConfig()
        {
            var cfg = new FeelingsConfig { AffectionHigh = 10, AffectionLow = -10, GrudgeThreshold = 1.5 };
            Assert.Equal(AffectionLevel.High, FeelingGrid.Classify(10, cfg));
            Assert.Equal(AffectionLevel.Low, FeelingGrid.Classify(-10, cfg));
            Assert.Equal(GrudgeLevel.Favor, FeelingGrid.Classify(1.5, cfg));
        }

        [Theory]
        [InlineData(30, 0, FeelingMood.Close)]      // 好感高、無恩怨
        [InlineData(30, -6, FeelingMood.Sore)]      // 好感高、有怨
        [InlineData(0, 0, FeelingMood.Neutral)]     // 好感普通、無恩怨
        [InlineData(0, 6, FeelingMood.Close)]       // 好感普通、有恩
        [InlineData(0, -6, FeelingMood.Hostile)]    // 好感普通、有怨
        [InlineData(-30, 0, FeelingMood.Hostile)]   // 好感低、無恩
        [InlineData(-30, 6, FeelingMood.Owe)]       // 好感低、有恩
        [InlineData(-30, -6, FeelingMood.Hostile)]  // 好感低、有怨
        [InlineData(30, 6, FeelingMood.Close)]      // 好感高、有恩
        public void Resolve_GridCells_EndToEnd(int affection, double requested, FeelingMood expected)
        {
            var rig = new Rig();
            rig.SetAffection(affection);
            if (requested != 0) rig.AddGrudge("evt_earlier", requested);

            var d = rig.Resolve();

            Assert.True(d.Applied, d.LogLine);
            Assert.Equal(Victim, d.FocusHeroId);
            Assert.Equal("killed", d.Category);
            Assert.Equal(expected, d.Mood);
            Assert.StartsWith($"VividWorld_Feeling_Killed_{expected}_", d.LineKey);
        }

        // ───────────── 稱呼 ─────────────

        [Theory]
        [InlineData(3, 2, 30, "VividWorld_Address_Higher_High")]
        [InlineData(3, 2, 0, "VividWorld_Address_Higher_Neutral")]
        [InlineData(3, 2, -30, "VividWorld_Address_Higher_Low")]
        [InlineData(2, 2, 30, "VividWorld_Address_Equal_High")]
        [InlineData(2, 2, 0, "VividWorld_Address_Equal_Neutral")]
        [InlineData(2, 2, -30, "VividWorld_Address_Equal_Low")]
        [InlineData(1, 2, 30, "VividWorld_Address_Lower_High")]
        [InlineData(1, 2, 0, "VividWorld_Address_Lower_Neutral")]
        [InlineData(1, 2, -30, "VividWorld_Address_Lower_Low")]
        public void Address_AllNineCells(int focusRank, int speakerRank, int affection, string expectedKey)
        {
            var rig = new Rig();
            rig.World.Rank[Victim] = focusRank;
            rig.World.Rank[Spk] = speakerRank;
            rig.SetAffection(affection);

            var d = rig.Resolve();

            Assert.True(d.Applied, d.LogLine);
            Assert.Equal(expectedKey, d.AddressKey);
            Assert.Contains("address " + expectedKey, d.LogLine);
        }

        [Fact]
        public void Address_StandingOrder_KingAboveClanLeaderAboveLordAboveWandererAboveOthers()
        {
            // 地位序由遊戲那側算成 4／3／2／1／0，Core 只比大小
            Assert.Equal(StandingComparison.Higher, FeelingGrid.Compare(4, 3));
            Assert.Equal(StandingComparison.Higher, FeelingGrid.Compare(2, 1));
            Assert.Equal(StandingComparison.Higher, FeelingGrid.Compare(1, 0));
            Assert.Equal(StandingComparison.Equal, FeelingGrid.Compare(2, 2));
            Assert.Equal(StandingComparison.Lower, FeelingGrid.Compare(0, 1));
            Assert.Equal(9, FeelingGrid.AllAddressKeys().Distinct().Count());
        }

        // ───────────── 恩怨：排除這一則自己造成的 ─────────────

        [Fact]
        public void Grudge_EntriesFromThisVeryEvent_AreExcluded()
        {
            var rig = new Rig();
            rig.AddGrudge("evt_1", -6);        // 這一則造成的：不算
            rig.AddGrudge("evt_other", -2);    // 別件事：算，但不到門檻

            var d = rig.Resolve(DiedInBattle("evt_1"));

            Assert.Equal(FeelingMood.Neutral, d.Mood);
            Assert.Contains("excluded 1 from this event", d.LogLine);
            Assert.Contains("-6 points", d.LogLine);
            Assert.Contains("grudge net -2", d.LogLine);
        }

        [Fact]
        public void Grudge_SameEntriesCountWhenTheRumorIsAnotherEvent()
        {
            var rig = new Rig();
            rig.AddGrudge("evt_1", -6);

            var d = rig.Resolve(DiedInBattle("evt_2"));

            Assert.Equal(FeelingMood.Hostile, d.Mood);
            Assert.Contains("excluded 0 from this event", d.LogLine);
        }

        [Fact]
        public void Grudge_OnlyPersonalScopeAndOnlyTowardTheFocusHeroCount()
        {
            var rig = new Rig();
            rig.World.Grudges.Add(new GrudgeEntry { EventId = "e", FromHeroId = Spk, AboutHeroId = Victim, Day = 100, Requested = -9, Scope = GrudgeScope.Clan });
            rig.AddGrudge("e2", -9, about: Killer);
            rig.World.Grudges.Add(new GrudgeEntry { EventId = "e3", FromHeroId = "someone_else", AboutHeroId = Victim, Day = 100, Requested = -9, Scope = GrudgeScope.Personal });

            var d = rig.Resolve();

            Assert.Equal(FeelingMood.Neutral, d.Mood);
        }

        [Theory]
        [InlineData(4.0, FeelingMood.Close)]
        [InlineData(3.99, FeelingMood.Neutral)]
        [InlineData(-3.99, FeelingMood.Neutral)]
        [InlineData(-4.0, FeelingMood.Hostile)]
        public void Grudge_ThresholdBoundaryThroughTheResolver(double requested, FeelingMood expected)
        {
            var rig = new Rig();
            rig.AddGrudge("evt_earlier", requested);
            Assert.Equal(expected, rig.Resolve().Mood);
        }

        [Theory]
        [InlineData(30, FeelingMood.Close)]
        [InlineData(29, FeelingMood.Neutral)]
        [InlineData(-29, FeelingMood.Neutral)]
        [InlineData(-30, FeelingMood.Hostile)]
        public void Affection_ThresholdBoundaryThroughTheResolver(int affection, FeelingMood expected)
        {
            var rig = new Rig();
            rig.SetAffection(affection);
            Assert.Equal(expected, rig.Resolve().Mood);
        }

        [Fact]
        public void Config_ChangedThresholdsMoveTheBoundaries()
        {
            var rig = new Rig();
            rig.Cfg.Presentation.Feelings.AffectionHigh = 50;
            rig.SetAffection(40);
            Assert.Equal(FeelingMood.Neutral, rig.Resolve().Mood);

            rig.Cfg.Presentation.Feelings.GrudgeThreshold = 1.0;
            rig.AddGrudge("evt_earlier", -1.0);
            Assert.Equal(FeelingMood.Hostile, rig.Resolve().Mood);
        }

        // ───────────── 焦點人物 ─────────────

        [Fact]
        public void Focus_IsTheParticipantTheSpeakerCaresAboutMost_AndTheLogSaysWhy()
        {
            var rig = new Rig();
            // 兇手的好感度高到蓋過同氏族的保底，焦點人物就是兇手，類別跟著角色走
            rig.World.Aff[Spk + ">" + Killer] = 80;

            var d = rig.Resolve();

            Assert.Equal(Killer, d.FocusHeroId);
            Assert.Equal("killer", d.FocusRole);
            Assert.Equal("victor", d.Category);
            Assert.Contains("focus hero killer as killer (relation +80 with killer)", d.LogLine);
        }

        // ───────────── 不產生 ─────────────

        [Fact]
        public void None_WhenFeaturesDisabled()
        {
            var rig = new Rig();
            rig.Cfg.Presentation.Feelings.Enabled = false;
            var d = rig.Resolve();
            Assert.False(d.Applied);
            Assert.Contains("switched off", d.Reason);
            Assert.StartsWith("Feeling: none for spk on evt_1", d.LogLine);
        }

        [Fact]
        public void None_WhenOnlyTheGistIsTold()
        {
            var rig = new Rig();
            var d = rig.Resolve(gist: true);
            Assert.False(d.Applied);
            Assert.Contains("gist", d.Reason);
        }

        [Fact]
        public void None_WhenTheSpeakerIsOneOfThePeopleItHappenedTo()
        {
            var rig = new Rig();
            foreach (var who in new[] { Victim, Killer })
            {
                var d = rig.Resolve(speaker: who);
                Assert.False(d.Applied);
                Assert.Contains("one of the people this happened to", d.Reason);
            }
        }

        [Fact]
        public void None_WhenNoFocusHeroCanBeFound()
        {
            var rig = new Rig();
            var evt = DiedInBattle();
            evt.Participants.Clear();
            var d = rig.Resolve(evt);
            Assert.False(d.Applied);
            Assert.Contains("no focus hero", d.Reason);
        }

        [Fact]
        public void None_WhenTheFocusRoleHasNoCategory()
        {
            var rig = new Rig();
            rig.Templates = _ => new EventTemplate { Type = "hero_died_in_battle", Feelings = new Dictionary<string, string> { ["killer"] = "victor" } };
            var d = rig.Resolve();   // 焦點人物是死者，模板沒替 victim 標類別
            Assert.False(d.Applied);
            Assert.Equal(Victim, d.FocusHeroId);
            Assert.Contains("no feeling category", d.Reason);
        }

        [Fact]
        public void None_WhenThereIsNoTemplate()
        {
            var rig = new Rig();
            rig.Templates = _ => null;
            var d = rig.Resolve();
            Assert.False(d.Applied);
            Assert.Contains("no template", d.Reason);
        }

        [Fact]
        public void None_WhenTheCatalogHasNoLinesForTheCell()
        {
            var rig = new Rig();
            var resolver = new FeelingResolver(rig.Cfg, FeelingCatalog.Empty, rig.World, rig.Traits, RealTemplate, 7);
            var d = resolver.Resolve(DiedInBattle(), Spk, false);
            Assert.False(d.Applied);
            Assert.Contains("no lines in the feeling catalog", d.Reason);
        }

        [Fact]
        public void Composer_NeverAttachesAFeelingDecision_SoChronicleAndAiPushStayFactsOnly()
        {
            var evt = DiedInBattle();
            var facts = new List<Fact> { new Fact { Id = "who", Category = FactCategory.Who, TextId = "VividWorld_Fact_X_Who", Text = "x", Fragility = 1 } };
            var composed = RumorTextComposer.Compose(evt, facts, new PresentationConfig(), prefix: null, speakerHeroId: Spk);
            Assert.Null(composed.Feeling);
            Assert.Null(RumorTextComposer.Compose(facts, new PresentationConfig()).Feeling);
        }

        // ───────────── 同一人同一件事挑同一句、個性優先 ─────────────

        [Fact]
        public void Pick_SameSpeakerAndEvent_AlwaysTheSameLine_EvenAcrossResolvers()
        {
            var first = new Rig().Resolve(DiedInBattle("evt_a")).LineKey;
            for (int i = 0; i < 5; i++)
            {
                Assert.Equal(first, new Rig().Resolve(DiedInBattle("evt_a")).LineKey);
            }
        }

        [Fact]
        public void Pick_WithNoTraitPreference_DifferentEventsUseBothLines()
        {
            var rig = new Rig();
            rig.SetAffection(-50);   // 敵視：兩句，一句標心腸硬
            var keys = new HashSet<string>();
            for (int i = 0; i < 60; i++)
            {
                keys.Add(rig.Resolve(DiedInBattle("evt_" + i)).LineKey!);
            }
            Assert.Equal(2, keys.Count);
        }

        [Fact]
        public void Pick_LineWhoseTraitTagMatchesTheSpeaker_IsAlwaysPreferred()
        {
            var rig = new Rig();
            rig.SetAffection(-50);
            rig.SetTrait(p => p.Mercy = -1);   // 標「心腸硬」的那一句是 _2

            for (int i = 0; i < 30; i++)
            {
                var d = rig.Resolve(DiedInBattle("evt_" + i));
                Assert.Equal("VividWorld_Feeling_Killed_Hostile_2", d.LineKey);
                Assert.Contains("trait match Mercy-", d.LogLine);
            }
        }

        [Fact]
        public void Pick_LineWhoseTraitTagPointsTheOtherWay_IsAvoided()
        {
            var rig = new Rig();
            rig.SetAffection(-50);
            rig.SetTrait(p => p.Mercy = 2);    // 心腸軟的人不講「心腸硬」那一句

            for (int i = 0; i < 30; i++)
            {
                Assert.Equal("VividWorld_Feeling_Killed_Hostile_1", rig.Resolve(DiedInBattle("evt_" + i)).LineKey);
            }
        }

        [Fact]
        public void Pick_PositiveTagNeedsATraitAboveZero_ZeroIsNotAMatch()
        {
            var rig = new Rig();
            rig.SetAffection(60);              // 親近：_2 標心腸軟
            rig.SetTrait(p => p.Mercy = 1);
            for (int i = 0; i < 20; i++)
            {
                Assert.Equal("VividWorld_Feeling_Killed_Close_2", rig.Resolve(DiedInBattle("evt_" + i)).LineKey);
            }

            rig.SetTrait(p => p.Mercy = 0);
            var keys = Enumerable.Range(0, 60).Select(i => rig.Resolve(DiedInBattle("evt_" + i)).LineKey!).Distinct().Count();
            Assert.Equal(2, keys);
        }

        // ───────────── 記進日誌的內容 ─────────────

        [Fact]
        public void LogLine_ShowsEveryInputAndTheChosenLine()
        {
            var rig = new Rig();
            rig.SetAffection(45);
            rig.World.Rank[Victim] = 3;
            rig.World.Rank[Spk] = 2;
            rig.AddGrudge("evt_1", -6);
            rig.AddGrudge("evt_x", -1);

            var d = rig.Resolve();

            Assert.Contains("focus hero victim as victim", d.LogLine);
            Assert.Contains("affection +45 -> High", d.LogLine);
            Assert.Contains("grudge net -1 over 1 entry (excluded 1 from this event, -6 points) -> None", d.LogLine);
            Assert.Contains("focus rank 3 vs speaker rank 2 -> Higher, address VividWorld_Address_Higher_High", d.LogLine);
            Assert.Contains("category killed, mood close", d.LogLine);
            Assert.Contains("candidates VividWorld_Feeling_Killed_Close_1, VividWorld_Feeling_Killed_Close_2 [Mercy+]", d.LogLine);
            Assert.Contains("chose " + d.LineKey, d.LogLine);
        }

        // ───────────── 事實句後另起一句、稱呼代換、英文大寫 ─────────────

        private static readonly Dictionary<string, string> Table = new()
        {
            ["VividWorld_Feeling_Test_1"] = "{ADDRESS} did well.",
            ["VividWorld_Feeling_Test_2"] = "I think {ADDRESS} did well.",
            ["VividWorld_Feeling_Test_3"] = "{ADDRESS}就這麼走了。",
            ["VividWorld_Feeling_Test_4"] = "This needs no address.",
            ["VividWorld_Feeling_Pronoun_EN"] = "I know {he} was there. {He} saw {himself}, and {his} friend helped {him}.",
            ["VividWorld_Feeling_Pronoun_ZH"] = "我知道{he}在那裡。{he}看見{himself}，{he}的朋友幫了{he}。",
            ["VividWorld_Address_Equal_High"] = "my old friend",
            ["VividWorld_Address_Equal_Neutral"] = "{NAME}",
            ["VividWorld_Address_Higher_Low"] = "那位大人物",
        };

        private static FeelingDecision Decision(string line, string address = "VividWorld_Address_Equal_High", string focus = "hero_focus")
            => new FeelingDecision { LineKey = line, AddressKey = address, FocusHeroId = focus, SpeakerId = Spk, EventId = "evt" };

        private static string? RenderLine(FeelingDecision d, out string? missing, bool links = false, string sentenceEnd = ".")
        {
            return RumorTextAssembler.RenderFeeling(
                d, links,
                (val, l) => val.StartsWith("hero:") ? (l ? "<a>Alice</a>" : "Alice") : val,
                (id, fb) => Table.TryGetValue(id ?? "", out var v) ? v : (fb ?? string.Empty),
                (id, fb) => Table.TryGetValue(id ?? "", out var v) ? v : fb,
                sentenceEnd, out missing);
        }

        [Fact]
        public void Render_SentenceStartingWithAddress_CapitalisesAfterSubstitution()
        {
            Assert.Equal("My old friend did well.", RenderLine(Decision("VividWorld_Feeling_Test_1"), out _));
        }

        [Fact]
        public void Render_SentenceNotStartingWithAddress_KeepsCase()
        {
            Assert.Equal("I think my old friend did well.", RenderLine(Decision("VividWorld_Feeling_Test_2"), out _));
        }

        [Fact]
        public void Render_NameAddress_UsesTheFocusHeroName_AndLinksWhenAsked()
        {
            var d = Decision("VividWorld_Feeling_Test_1", "VividWorld_Address_Equal_Neutral");
            Assert.Equal("Alice did well.", RenderLine(d, out _));
            Assert.Equal("<a>Alice</a> did well.", RenderLine(d, out _, links: true));
        }

        [Fact]
        public void Render_Chinese_HasNoCapitalisationAndNoStrayPlaceholder()
        {
            // 繁中的句尾是「。」，句子自己已經以它收尾就不再補
            var text = RenderLine(Decision("VividWorld_Feeling_Test_3", "VividWorld_Address_Higher_Low"), out _, sentenceEnd: "。");
            Assert.Equal("那位大人物就這麼走了。", text);
        }

        [Fact]
        public void Render_SentenceWithoutAddress_NeedsNoAddressKey()
        {
            Assert.Equal("This needs no address.", RenderLine(Decision("VividWorld_Feeling_Test_4", address: ""), out var missing));
            Assert.Null(missing);
        }

        [Fact]
        public void Render_MissingKeys_ReturnNullAndNameTheMissingKey()
        {
            Assert.Null(RenderLine(Decision("VividWorld_Feeling_Nope"), out var m1));
            Assert.Equal("VividWorld_Feeling_Nope", m1);

            Assert.Null(RenderLine(Decision("VividWorld_Feeling_Test_1", "VividWorld_Address_Lower_Low"), out var m2));
            Assert.Equal("VividWorld_Address_Lower_Low", m2);
        }

        [Theory]
        [InlineData("female", "I know she was there. She saw herself, and her friend helped her.", "我知道她在那裡。她看見她自己，她的朋友幫了她。")]
        [InlineData("male", "I know he was there. He saw himself, and his friend helped him.", "我知道他在那裡。他看見他自己，他的朋友幫了他。")]
        [InlineData("unknown", "I know he was there. He saw himself, and his friend helped him.", "我知道他在那裡。他看見他自己，他的朋友幫了他。")]
        public void RenderFeeling_GenderPronouns_SwapsHeShe_ForMaleFemaleUnknown_InChineseAndEnglish(
            string gender, string expectedEn, string expectedZh)
        {
            bool? isFemale = gender == "female" ? true : (gender == "male" ? false : (bool?)null);
            var dEn = new FeelingDecision
            {
                LineKey = "VividWorld_Feeling_Pronoun_EN",
                AddressKey = "",
                FocusHeroId = "hero_focus",
                FocusIsFemale = isFemale,
                SpeakerId = Spk,
                EventId = "evt"
            };
            var dZh = new FeelingDecision
            {
                LineKey = "VividWorld_Feeling_Pronoun_ZH",
                AddressKey = "",
                FocusHeroId = "hero_focus",
                FocusIsFemale = isFemale,
                SpeakerId = Spk,
                EventId = "evt"
            };

            string? actualEn = RumorTextAssembler.RenderFeeling(
                dEn, false,
                (val, l) => val,
                (id, fb) => Table.TryGetValue(id ?? "", out var v) ? v : fb ?? "",
                (id, fb) => Table.TryGetValue(id ?? "", out var v) ? v : fb,
                ".", out _);

            var zhTable = new Dictionary<string, string>(Table)
            {
                ["VividWorld_Pronoun_He_M"] = "他",
                ["VividWorld_Pronoun_He_F"] = "她",
                ["VividWorld_Pronoun_He_N"] = "他",
                ["VividWorld_Pronoun_Him_M"] = "他",
                ["VividWorld_Pronoun_Him_F"] = "她",
                ["VividWorld_Pronoun_Him_N"] = "他",
                ["VividWorld_Pronoun_His_M"] = "他的",
                ["VividWorld_Pronoun_His_F"] = "她的",
                ["VividWorld_Pronoun_His_N"] = "他的",
                ["VividWorld_Pronoun_Himself_M"] = "他自己",
                ["VividWorld_Pronoun_Himself_F"] = "她自己",
                ["VividWorld_Pronoun_Himself_N"] = "他自己",
                ["VividWorld_Self_Reflexive"] = "我自己"
            };

            string? actualZh = RumorTextAssembler.RenderFeeling(
                dZh, false,
                (val, l) => val,
                (id, fb) => zhTable.TryGetValue(id ?? "", out var v) ? v : fb ?? "",
                (id, fb) => zhTable.TryGetValue(id ?? "", out var v) ? v : fb,
                "。", out _);

            Assert.Equal(expectedEn, actualEn);
            Assert.Equal(expectedZh, actualZh);
        }

        [Fact]
        public void SingleLineCell_AlwaysPicksTheOnlyLine_RegardlessOfTraitsOrSeed()
        {
            var rig = new Rig();
            rig.SetAffection(40);
            rig.AddGrudge("other_evt", -10);
            var evt = new WorldEvent
            {
                EventId = "evt_released",
                Type = "hero_released",
                Origin = EventOrigin.Public,
                Day = 90,
                DramaWeight = 2,
                Participants = new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["captor"] = Victim,
                    ["prisoner"] = Killer
                }
            };
            rig.World.Rank[Victim] = 2;
            rig.World.Rank[Spk] = 2;

            for (int seed = 1; seed <= 5; seed++)
            {
                rig.Seed = seed;
                rig.SetTrait(p => { p.Mercy = 2; p.Honor = -2; p.Valor = 2; p.Calculating = -2; p.Generosity = 2; });
                var d1 = rig.Resolve(evt, gist: false, speaker: "spk");
                Assert.Equal(FeelingMood.Sore, d1.Mood);
                Assert.Equal("released", d1.Category);
                Assert.Equal("VividWorld_Feeling_Released_Sore_1", d1.LineKey);

                rig.SetTrait(p => { p.Mercy = -2; p.Honor = 2; p.Valor = -2; p.Calculating = 2; p.Generosity = -2; });
                var d2 = rig.Resolve(evt, gist: false, speaker: "spk");
                Assert.Equal(FeelingMood.Sore, d2.Mood);
                Assert.Equal("released", d2.Category);
                Assert.Equal("VividWorld_Feeling_Released_Sore_1", d2.LineKey);
            }
        }

        [Theory]
        [InlineData("female", true)]
        [InlineData("male", false)]
        [InlineData("unknown", null)]
        public void LogLine_ShowsFocusHeroGender(string expectedGenderStr, bool? isFemale)
        {
            var rig = new Rig();
            rig.SetAffection(45);
            if (isFemale.HasValue)
            {
                rig.World.Female[Victim] = isFemale.Value;
            }

            var d = rig.Resolve();

            Assert.Contains($"gender {expectedGenderStr}", d.LogLine);
            Assert.Equal(isFemale, d.FocusIsFemale);
        }

        private static ComposedRumor FactOnly(FeelingDecision? feeling)
        {
            return new ComposedRumor
            {
                Parts = new[] { new ComposedFactPart { TextId = string.Empty, Fallback = "Alice was captured near Pravend", Vars = new Dictionary<string, string>() } },
                Feeling = feeling
            };
        }

        private static RumorRenderResult Assemble(ComposedRumor r, List<string>? info = null)
        {
            return RumorTextAssembler.Assemble(
                r,
                new PresentationConfig { EncyclopediaLinksEnabled = false, SentenceEnd = ".", FactSeparator = ", " },
                (val, l) => val.Contains(':') ? "Alice" : val,
                (id, fb) => Table.TryGetValue(id ?? "", out var v) ? v : (fb ?? string.Empty),
                (id, fb) => Table.TryGetValue(id ?? "", out var v) ? v : fb,
                onWarning: null,
                isFemale: null,
                onInfo: info == null ? null : info.Add);
        }

        [Fact]
        public void Assemble_FeelingIsItsOwnSentenceAfterTheFactSentence()
        {
            var result = Assemble(FactOnly(Decision("VividWorld_Feeling_Test_1")));
            Assert.Equal("Alice was captured near Pravend. My old friend did well.", result.PlainText);
            Assert.Equal("VividWorld_Feeling_Test_1", result.FeelingKeyUsed);
        }

        [Fact]
        public void Assemble_WithoutADecision_OrWithANoFeelingDecision_AddsNothing()
        {
            Assert.Equal("Alice was captured near Pravend.", Assemble(FactOnly(null)).PlainText);

            var none = new FeelingDecision { Reason = "only told the gist" };
            var result = Assemble(FactOnly(none));
            Assert.Equal("Alice was captured near Pravend.", result.PlainText);
            Assert.Null(result.FeelingKeyUsed);
        }

        [Fact]
        public void Assemble_MissingFeelingText_LeavesTheFactSentenceAloneAndSaysSo()
        {
            var info = new List<string>();
            var result = Assemble(FactOnly(Decision("VividWorld_Feeling_Nope")), info);
            Assert.Equal("Alice was captured near Pravend.", result.PlainText);
            Assert.Contains(info, l => l.Contains("Rumor text feeling: omitted (missing key: VividWorld_Feeling_Nope)"));
        }

        // ───────────── 挑選器：只有完整分享才判定 ─────────────

        private static (RumorOfferSelector selector, HeroSocialProfile teller, List<RumorCandidate> candidates) OfferRig(int relation)
        {
            var cfg = new VividWorldConfig();
            var rig = new Rig();
            var rng = new SplitMix64Rng();
            var engine = new RumorEngine(cfg, FactRetentionPolicies.Create(cfg, rng, 7), NullEmbellishmentPolicy.Instance,
                new FakePropagationChannel(), rig.Traits, rng, 7, "player");
            var resolver = new FeelingResolver(cfg, RealFeelings.Value, rig.World, rig.Traits, RealTemplate, 7);
            var selector = new RumorOfferSelector(cfg, engine, "player", mode: RumorMode.Casual, feelings: resolver, dialogueWorld: rig.World);

            var evt = DiedInBattle("evt_offer");
            evt.Facts = new List<Fact> { new Fact { Id = "f_who", Category = FactCategory.Who, Text = "x", Fragility = 1 } };
            evt.KnownBy.Add(new KnownByEntry { HeroId = Spk, Hop = 1 });
            var teller = new HeroSocialProfile { HeroId = Spk, RelationWithPlayer = relation };
            var candidates = new List<RumorCandidate> { new RumorCandidate { Event = evt, TellerHop = 1 } };
            return (selector, teller, candidates);
        }

        [Fact]
        public void Selector_FullShareToThePlayer_CarriesAFeelingDecision()
        {
            var (selector, teller, candidates) = OfferRig(relation: 40);
            var offer = selector.SelectVolunteered(teller, candidates, day: 100);
            Assert.NotNull(offer);
            Assert.False(offer!.IsGist);
            Assert.NotNull(offer.Composed.Feeling);
            Assert.True(offer.Composed.Feeling!.Applied, offer.Composed.Feeling.LogLine);
        }

        [Fact]
        public void Selector_AskedShare_AlsoCarriesAFeelingDecision()
        {
            var (selector, teller, candidates) = OfferRig(relation: 40);
            var offer = selector.SelectOnAsk(teller, candidates, day: 100);
            Assert.NotNull(offer);
            Assert.True(offer!.Composed.Feeling!.Applied);
        }

        [Fact]
        public void Selector_UnfamiliarStranger_AskedBigNews_HasNoFeeling()
        {
            var (selector, teller, candidates) = OfferRig(relation: 6);
            selector.Mode = RumorMode.Realistic; // 寫實模式下門檻 10，意願 6 為不熟但肯答
            candidates[0].Event.DramaWeight = 6; // 大事 (weight >= 5)
            var offer = selector.SelectOnAsk(teller, candidates, day: 100);
            Assert.NotNull(offer);
            Assert.False(offer!.IsGist);
            Assert.Null(offer.Composed.Feeling);
        }

        // ───────────── 模板資料 ─────────────

        private static readonly string[] Unclassified = { "tavern_confidence", "victory_credit_belittled" };

        [Fact]
        public void Templates_ShippedCatalogsLoadWithoutFeelingIssues()
        {
            foreach (var cat in RealCatalogs.Value)
            {
                Assert.DoesNotContain(cat.Issues, i => i.Code == CatalogIssueCode.FeelingInvalid);
                Assert.Equal(0, cat.SkippedCount);
            }
        }

        [Fact]
        public void Templates_EveryRoleHasACategory_ExceptTheTwoUnclassifiedTypes()
        {
            var seen = 0;
            foreach (var template in RealCatalogs.Value.SelectMany(c => c.Templates))
            {
                seen++;
                if (Unclassified.Contains(template.Type))
                {
                    Assert.True(template.Feelings == null || template.Feelings.Count == 0, template.Type + " must have no feeling category");
                    continue;
                }

                Assert.NotNull(template.Feelings);
                foreach (var role in template.Roles.Keys)
                {
                    Assert.True(template.Feelings!.TryGetValue(role, out var cat), $"{template.Type}.{role} has no feeling category");
                    Assert.True(FeelingCategories.IsKnown(cat), $"{template.Type}.{role}: unknown category '{cat}'");
                }
                Assert.All(template.Feelings!.Keys, k => Assert.True(template.Roles.ContainsKey(k), $"{template.Type}: feelings names undeclared role '{k}'"));
            }
            Assert.Equal(36, seen);
        }

        [Theory]
        [InlineData("hero_murdered", "victim", "killed")]
        [InlineData("hero_executed", "victim", "killed")]
        [InlineData("hero_died_in_battle", "victim", "killed")]
        [InlineData("hero_died_of_old_age", "victim", "died")]
        [InlineData("hero_died_in_labor", "victim", "died")]
        [InlineData("hero_died_naturally", "victim", "died")]
        [InlineData("hero_murdered", "killer", "executor")]
        [InlineData("hero_executed", "killer", "executor")]
        [InlineData("hero_died_in_battle", "killer", "victor")]
        [InlineData("hero_taken_prisoner", "captor", "victor")]
        [InlineData("hero_taken_prisoner", "prisoner", "captured")]
        [InlineData("hero_captured_by_bandits", "prisoner", "captured")]
        [InlineData("hero_released", "prisoner", "freed")]
        [InlineData("hero_escaped_captivity", "prisoner", "freed")]
        [InlineData("hero_escaped_bandits", "prisoner", "freed")]
        [InlineData("hero_rescued_from_bandits", "prisoner", "freed")]
        [InlineData("hero_escaped_captivity", "captor", "lost_prisoner")]
        [InlineData("seat_dispute_demanded", "favored", "lost_face")]
        [InlineData("seat_dispute_demanded", "slighted", "brash")]
        [InlineData("seat_dispute_endured", "slighted", "lost_face")]
        [InlineData("seat_dispute_endured", "favored", "pending")]
        [InlineData("seat_dispute_walked_out", "host", "lost_face")]
        [InlineData("seat_dispute_walked_out", "favored", "lost_face")]
        [InlineData("seat_dispute_walked_out", "slighted", "brash")]
        [InlineData("seat_dispute_yielded", "slighted", "decent")]
        [InlineData("seat_dispute_yielded", "favored", "cared")]
        [InlineData("advice_brushed_off", "student", "lost_face")]
        [InlineData("advice_mocked", "veteran", "brash")]
        [InlineData("advice_given_freely", "veteran", "decent")]
        [InlineData("advice_given_freely", "student", "cared")]
        [InlineData("tavern_sour_words", "listener", "lost_face")]
        [InlineData("tavern_sour_words", "speaker", "brash")]
        [InlineData("tavern_boast_told", "speaker", "brash")]
        [InlineData("tavern_boast_told", "listener", "pending")]
        [InlineData("tavern_good_word", "speaker", "decent")]
        [InlineData("tavern_good_word", "listener", "cared")]
        [InlineData("victory_credit_claimed", "rival", "lost_face")]
        [InlineData("victory_credit_claimed", "claimant", "brash")]
        [InlineData("victory_credit_judged", "claimant", "lost_face")]
        [InlineData("victory_credit_judged", "host", "decent")]
        [InlineData("victory_credit_judged", "rival", "cared")]
        [InlineData("victory_credit_deferred", "claimant", "decent")]
        [InlineData("victory_credit_deferred", "rival", "cared")]
        [InlineData("brawl_shielded_own", "aggrieved", "lost_face")]
        [InlineData("brawl_counter_accused", "patron", "brash")]
        [InlineData("brawl_man_handed_over", "patron", "decent")]
        [InlineData("brawl_man_handed_over", "aggrieved", "cared")]
        [InlineData("brawl_hushed_up", "aggrieved", "pending")]
        [InlineData("brawl_hushed_up", "patron", "pending")]
        [InlineData("wager_refused", "challenger", "lost_face")]
        [InlineData("wager_refused", "challenged", "brash")]
        [InlineData("wager_struck", "challenger", "pending")]
        [InlineData("wager_secret_stake", "challenged", "pending")]
        [InlineData("heroes_married", "spouse_a", "joy")]
        [InlineData("heroes_married", "spouse_b", "joy")]
        [InlineData("child_born", "mother", "joy")]
        [InlineData("child_born", "child", "joy")]
        public void Templates_RoleToCategory_MatchesTheAgreedTable(string type, string role, string expected)
        {
            var template = RealTemplate(type);
            Assert.NotNull(template);
            var evt = new WorldEvent { EventId = "e", Type = type };
            Assert.Equal(expected, FeelingResolver.CategoryFor(template, evt, role, out _));
        }

        private static WorldEvent ReleasedWith(string reasonTextId)
        {
            return new WorldEvent
            {
                EventId = "e", Type = "hero_released",
                Facts = new List<Fact>
                {
                    new Fact { Id = "who", Category = FactCategory.Who, TextId = "VividWorld_Fact_HeroReleased_Who", Fragility = 1 },
                    new Fact { Id = "reason", Category = FactCategory.Why, TextId = reasonTextId, Fragility = 2 }
                }
            };
        }

        [Fact]
        public void Templates_ReleasedCaptor_DefeatedIsLostPrisoner_OtherReasonsAreReleased()
        {
            var template = RealTemplate("hero_released");
            Assert.Equal("lost_prisoner", FeelingResolver.CategoryFor(template, ReleasedWith("VividWorld_Fact_HeroReleased_Defeated"), "captor", out _));
            foreach (var seg in new[] { "Ransom", "Peace", "Disbanded", "SettlementOwnerChanged" })
            {
                Assert.Equal("released", FeelingResolver.CategoryFor(template, ReleasedWith("VividWorld_Fact_HeroReleased_" + seg), "captor", out _));
            }
            // 沒記原因的舊事件
            var legacy = new WorldEvent { EventId = "e", Type = "hero_released", Facts = new List<Fact> { new Fact { Id = "who", TextId = "VividWorld_Fact_HeroReleased_Who" } } };
            Assert.Equal("released", FeelingResolver.CategoryFor(template, legacy, "captor", out _));
            // 被放的人不受原因影響
            Assert.Equal("freed", FeelingResolver.CategoryFor(template, ReleasedWith("VividWorld_Fact_HeroReleased_Defeated"), "prisoner", out _));
        }

        [Fact]
        public void Templates_DerivedVariants_KeepTheCategories()
        {
            var released = TemplateVariants.Released(RealTemplate("hero_released")!, ReleaseReasons.Defeated, captorKnown: true);
            Assert.Equal("lost_prisoner", released.FeelingOverrides!.Single(o => o.WhenFact.EndsWith("_Defeated")).Category);
            Assert.Equal("freed", released.Feelings!["prisoner"]);
        }

        [Fact]
        public void Loader_RejectsUnknownCategoryAndUndeclaredRole()
        {
            const string json = @"[
              { ""type"": ""a"", ""origin"": ""public"", ""dramaWeight"": 2, ""roles"": { ""x"": ""{X}"" }, ""feelings"": { ""x"": ""no_such_category"" },
                ""facts"": [ { ""id"": ""who"", ""category"": ""WHO"", ""textId"": ""T_A_Who"", ""text"": ""{X}"", ""vars"": { ""X"": ""hero:{X}"" }, ""fragility"": 1 } ] },
              { ""type"": ""b"", ""origin"": ""public"", ""dramaWeight"": 2, ""roles"": { ""x"": ""{X}"" }, ""feelings"": { ""ghost"": ""joy"" },
                ""facts"": [ { ""id"": ""who"", ""category"": ""WHO"", ""textId"": ""T_B_Who"", ""text"": ""{X}"", ""vars"": { ""X"": ""hero:{X}"" }, ""fragility"": 1 } ] }
            ]";
            var cat = EventCatalogLoader.Load(json, new PersistenceConfig());
            Assert.Equal(2, cat.Issues.Count(i => i.Code == CatalogIssueCode.FeelingInvalid));
            Assert.Empty(cat.Templates);
        }

        // ───────────── 字串表：句子與稱呼 ─────────────

        private static Dictionary<string, string> LoadTable(string path)
        {
            var doc = XDocument.Load(path);
            return doc.Root!.Element("strings")!.Elements("string")
                .ToDictionary(e => e.Attribute("id")!.Value, e => e.Attribute("text")!.Value, StringComparer.Ordinal);
        }

        private static readonly Lazy<(Dictionary<string, string> en, Dictionary<string, string> zh)> Tables = new(() =>
            (LoadTable(RepoFile("module", "ModuleData", "Languages", "std_module_strings_xml.xml")),
             LoadTable(RepoFile("module", "ModuleData", "Languages", "CNt", "std_module_strings_xml.xml"))));

        [Fact]
        public void Catalog_EveryCategoryAndMood_HasExpectedLineCount()
        {
            Assert.Empty(RealFeelings.Value.Issues);
            var singleLineCells = new HashSet<(string category, FeelingMood mood)>
            {
                ("released", FeelingMood.Sore),
                ("cared", FeelingMood.Sore),
                ("pending", FeelingMood.Sore),
                ("victor", FeelingMood.Owe),
                ("freed", FeelingMood.Owe),
                ("lost_prisoner", FeelingMood.Owe),
                ("released", FeelingMood.Owe),
                ("lost_face", FeelingMood.Owe),
                ("cared", FeelingMood.Owe),
                ("joy", FeelingMood.Owe),
                ("pending", FeelingMood.Owe)
            };

            foreach (var category in FeelingCategories.Ids)
            {
                foreach (FeelingMood mood in Enum.GetValues(typeof(FeelingMood)))
                {
                    int expected = singleLineCells.Contains((category, mood)) ? 1 : 2;
                    Assert.True(RealFeelings.Value.Lines(category, mood).Count == expected,
                        $"{category}/{mood} must have {expected} line(s)");
                }
            }
            Assert.Equal(129, RealFeelings.Value.AllLines.Count());
            Assert.Equal(129, RealFeelings.Value.AllLines.Select(l => l.Key).Distinct().Count());
        }

        [Fact]
        public void Strings_CatalogKeysAndAddressKeys_ExistInBothLanguages_WithIdenticalFeelingKeySets()
        {
            var (en, zh) = Tables.Value;
            var catalogKeys = RealFeelings.Value.AllLines.Select(l => l.Key).ToList();

            foreach (var key in catalogKeys.Concat(FeelingGrid.AllAddressKeys()))
            {
                Assert.True(en.TryGetValue(key, out var e) && !string.IsNullOrWhiteSpace(e), "English is missing " + key);
                Assert.True(zh.TryGetValue(key, out var z) && !string.IsNullOrWhiteSpace(z), "Chinese is missing " + key);
            }

            bool IsFeelingKey(string k) => k.StartsWith("VividWorld_Feeling_", StringComparison.Ordinal) || k.StartsWith("VividWorld_Address_", StringComparison.Ordinal);
            var enKeys = en.Keys.Where(IsFeelingKey).OrderBy(k => k, StringComparer.Ordinal).ToList();
            var zhKeys = zh.Keys.Where(IsFeelingKey).OrderBy(k => k, StringComparer.Ordinal).ToList();
            Assert.Equal(enKeys, zhKeys);
            Assert.Equal(catalogKeys.Concat(FeelingGrid.AllAddressKeys()).OrderBy(k => k, StringComparer.Ordinal).ToList(), enKeys);
        }

        [Fact]
        public void Strings_OnlyTheAddressAndNamePlaceholdersAppear()
        {
            var (en, zh) = Tables.Value;
            foreach (var table in new[] { en, zh })
            {
                foreach (var kvp in table.Where(k => k.Key.StartsWith("VividWorld_Feeling_", StringComparison.Ordinal)))
                {
                    Assert.DoesNotMatch(new Regex(@"\{(?!ADDRESS|he|him|his|He|His|himself|PRONOUN\})"), kvp.Value);
                    Assert.DoesNotContain("〔", kvp.Value);   // 審閱備註不是句子
                }
                foreach (var kvp in table.Where(k => k.Key.StartsWith("VividWorld_Address_", StringComparison.Ordinal)))
                {
                    Assert.DoesNotMatch(new Regex(@"\{(?!NAME\})"), kvp.Value);
                }
            }
        }

        [Fact]
        public void Strings_TraitTagsCoverTheSixAgreedPersonalities()
        {
            var tags = RealFeelings.Value.AllLines.Where(l => l.Trait != null).Select(l => l.TagText).Distinct().OrderBy(t => t, StringComparer.Ordinal).ToList();
            Assert.Equal(new[] { "Calculating+", "Generosity+", "Honor+", "Mercy+", "Mercy-", "Valor+" }, tags);
        }

        // ───────────── 設定鍵 ─────────────

        [Fact]
        public void Config_Defaults()
        {
            var f = new VividWorldConfig().Presentation.Feelings;
            Assert.True(f.Enabled);
            Assert.Equal(30, f.AffectionHigh);
            Assert.Equal(-30, f.AffectionLow);
            Assert.Equal(4.0, f.GrudgeThreshold);
        }

        [Fact]
        public void Config_MergeAddsOnlyTheMissingFeelingKeys_KeepingWhatThePlayerSet()
        {
            const string old = @"{ ""presentation"": { ""wholeSentences"": true, ""feelings"": { ""affectionHigh"": 45 } } }";
            var canonical = JObject.Parse(VividJson.Write(new VividWorldConfig()));

            var result = ConfigMerge.AddMissingKeys(JObject.Parse(old), canonical);

            Assert.Contains("presentation.feelings.enabled", result.AddedPaths);
            Assert.Contains("presentation.feelings.affectionLow", result.AddedPaths);
            Assert.Contains("presentation.feelings.grudgeThreshold", result.AddedPaths);
            Assert.DoesNotContain("presentation.feelings.affectionHigh", result.AddedPaths);
            Assert.Equal(45, (int)result.Merged["presentation"]!["feelings"]!["affectionHigh"]!);
            Assert.True((bool)result.Merged["presentation"]!["feelings"]!["enabled"]!);
        }

        [Fact]
        public void Config_NormalizeKeepsTheBoundariesSane()
        {
            var cfg = new VividWorldConfig();
            cfg.Presentation.Feelings.AffectionHigh = 500;
            cfg.Presentation.Feelings.AffectionLow = 20;
            cfg.Presentation.Feelings.GrudgeThreshold = -3;
            cfg.Normalize();
            Assert.Equal(100, cfg.Presentation.Feelings.AffectionHigh);
            Assert.Equal(0, cfg.Presentation.Feelings.AffectionLow);
            Assert.Equal(0.0, cfg.Presentation.Feelings.GrudgeThreshold);
        }
    }
}
