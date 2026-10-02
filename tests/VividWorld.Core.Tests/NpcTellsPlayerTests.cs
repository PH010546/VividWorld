using System;
using System.Collections.Generic;
using System.Linq;
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
    public class NpcTellsPlayerTests
    {
        private sealed class TestDialogueWorld : IDialogueWorld
        {
            public double Today { get; set; } = 100.0;
            public HashSet<string> Companions = new(StringComparer.Ordinal);
            public HashSet<string> ClanMembers = new(StringComparer.Ordinal);
            public HashSet<string> Spouses = new(StringComparer.Ordinal);
            public string? PlayerKingdomLeaderId { get; set; }
            public bool IsPlayerCompanion(string heroId) => Companions.Contains(heroId);
            public bool IsPlayerClanMember(string heroId) => ClanMembers.Contains(heroId);
            public bool IsPlayerSpouse(string heroId) => Spouses.Contains(heroId);

            public Dictionary<(string, string), int> Affections = new();
            public int? Affection(string speakerId, string heroId) =>
                Affections.TryGetValue((speakerId, heroId), out var val) ? val : 0;

            public int? StandingRank(string heroId) => 0;
            public bool? IsFemale(string heroId) => null;

            public Dictionary<string, InterestHeroFacts> HeroFacts = new(StringComparer.Ordinal);
            public InterestHeroFacts? InterestFacts(string heroId) =>
                HeroFacts.TryGetValue(heroId, out var facts) ? facts : null;

            public Dictionary<(string, string), List<GrudgeEntry>> Grudges = new();
            public IReadOnlyList<GrudgeEntry> PersonalGrudges(string speakerId, string heroId) =>
                Grudges.TryGetValue((speakerId, heroId), out var list) ? list : Array.Empty<GrudgeEntry>();

            public string NameOf(string heroId) => heroId;
        }

        private (RumorOfferSelector selector, RumorEngine engine, VividWorldConfig cfg, TestDialogueWorld world, PlayerHeardLogStore heardLog, Dictionary<string, WorldEvent> eventRepo)
            CreateTestSetup(
                string playerHeroId = "player",
                long seed = 42L,
                RumorMode mode = RumorMode.Casual,
                VividWorldConfig? customConfig = null)
        {
            var cfg = customConfig ?? new VividWorldConfig();
            var rng = new SplitMix64Rng();
            var channel = new FakePropagationChannel();
            var traits = new FakeHeroTraitLookup();
            var retention = FactRetentionPolicies.Create(cfg, rng, seed);
            var embellishment = NullEmbellishmentPolicy.Instance;
            var engine = new RumorEngine(cfg, retention, embellishment, channel, traits, rng, seed, playerHeroId);
            var world = new TestDialogueWorld();
            var fakeFs = new FailingFileWriter();
            var heardLog = new PlayerHeardLogStore("test_heard.json", fakeFs);
            var eventRepo = new Dictionary<string, WorldEvent>(StringComparer.Ordinal);

            var feelings = new FeelingResolver(
                cfg,
                FeelingCatalog.Empty,
                world,
                traits,
                null,
                seed);

            var selector = new RumorOfferSelector(
                cfg,
                engine,
                playerHeroId,
                mode,
                traits: traits,
                feelings: feelings,
                dialogueWorld: world,
                playerHeardLog: heardLog,
                getEvent: id => eventRepo.TryGetValue(id, out var ev) ? ev : null);

            return (selector, engine, cfg, world, heardLog, eventRepo);
        }

        private WorldEvent CreateSampleEvent(
            string eventId,
            double day = 10.0,
            int drama = 3,
            int scale = DramaScales.Ten,
            EventOrigin origin = EventOrigin.Public,
            string? actorId = null)
        {
            var evt = new WorldEvent
            {
                EventId = eventId,
                Origin = origin,
                Day = day,
                DramaWeight = drama,
                DramaScale = scale,
                Facts = new List<Fact>
                {
                    new() { Id = "f_who", Category = FactCategory.Who, Text = "Lord Bob", Fragility = 1 },
                    new() { Id = "f_where", Category = FactCategory.Where, Text = "Pravend", Fragility = 2 },
                    new() { Id = "f_what", Category = FactCategory.What, Text = "fought a duel", Fragility = 3 }
                }
            };
            if (!string.IsNullOrEmpty(actorId))
            {
                evt.Participants["actor"] = actorId;
            }
            return evt;
        }

        // ==========================================
        // 1. 三條線各自邊界
        // ==========================================

        [Fact]
        public void Thresholds_CasualMode_Willingness0To5_AnswersWhenAsked()
        {
            var (selector, _, _, _, _, _) = CreateTestSetup(mode: RumorMode.Casual);

            var teller = new HeroSocialProfile
            {
                HeroId = "teller",
                RelationWithPlayer = 2,
                Traits = new TraitProfile { Generosity = 0, Honor = 0, Calculating = 0 }
            };

            var evt = CreateSampleEvent("evt_big", drama: 6); // weight 6 >= 5 (BigNews)
            evt.KnownBy.Add(new KnownByEntry { HeroId = "teller", Hop = 1 });
            var candidates = new List<RumorCandidate>
            {
                new() { Event = evt, TellerHop = 1, PlayerExistingHop = null }
            };

            var decision = selector.DecideOnAsk(teller, candidates, day: 15.0);
            Assert.Equal(AskRefusal.None, decision.Refusal);
            Assert.NotNull(decision.Offer);
            Assert.True(decision.CanAnswer);
            Assert.Equal("familiar_big_news", decision.AnswerMode);
        }

        [Fact]
        public void Thresholds_RealisticMode_Willingness0To4_Refused()
        {
            var (selector, _, _, _, _, _) = CreateTestSetup(mode: RumorMode.Realistic);

            var teller = new HeroSocialProfile
            {
                HeroId = "teller",
                RelationWithPlayer = 4,
                Traits = new TraitProfile { Generosity = 0, Honor = 0, Calculating = 0 }
            };

            var evt = CreateSampleEvent("evt_big", drama: 6, scale: 1);
            evt.KnownBy.Add(new KnownByEntry { HeroId = "teller", Hop = 1 });
            var candidates = new List<RumorCandidate>
            {
                new() { Event = evt, TellerHop = 1, PlayerExistingHop = null }
            };

            var decision = selector.DecideOnAsk(teller, candidates, day: 15.0);
            Assert.Equal(AskRefusal.WillingnessGate, decision.Refusal);
            Assert.Null(decision.Offer);
            Assert.False(decision.CanAnswer);
        }

        [Fact]
        public void Thresholds_RealisticMode_Willingness5To9_AnswersBigNewsAsStranger()
        {
            var (selector, _, _, _, _, _) = CreateTestSetup(mode: RumorMode.Realistic);

            var teller = new HeroSocialProfile
            {
                HeroId = "teller",
                RelationWithPlayer = 6,
                Traits = new TraitProfile { Generosity = 0, Honor = 0, Calculating = 0 }
            };

            var evt = CreateSampleEvent("evt_big", drama: 6, scale: 1);
            evt.KnownBy.Add(new KnownByEntry { HeroId = "teller", Hop = 1 });
            var candidates = new List<RumorCandidate>
            {
                new() { Event = evt, TellerHop = 1, PlayerExistingHop = null }
            };

            var decision = selector.DecideOnAsk(teller, candidates, day: 15.0);
            Assert.Equal(AskRefusal.None, decision.Refusal);
            Assert.NotNull(decision.Offer);
            Assert.False(decision.IsFamiliar);
            Assert.Equal("unfamiliar_big_news", decision.AnswerMode);
            Assert.Null(decision.Offer.Composed.Feeling);
        }

        [Fact]
        public void Thresholds_RealisticMode_Willingness10Plus_Familiar()
        {
            var (selector, _, _, _, _, _) = CreateTestSetup(mode: RumorMode.Realistic);

            var teller = new HeroSocialProfile
            {
                HeroId = "teller",
                RelationWithPlayer = 12,
                Traits = new TraitProfile { Generosity = 0, Honor = 0, Calculating = 0 }
            };

            var evt = CreateSampleEvent("evt_big", drama: 6, scale: 1);
            evt.KnownBy.Add(new KnownByEntry { HeroId = "teller", Hop = 1 });
            var candidates = new List<RumorCandidate>
            {
                new() { Event = evt, TellerHop = 1, PlayerExistingHop = null }
            };

            var decision = selector.DecideOnAsk(teller, candidates, day: 15.0);
            Assert.Equal(AskRefusal.None, decision.Refusal);
            Assert.NotNull(decision.Offer);
            Assert.True(decision.IsFamiliar);
            Assert.Equal("familiar_big_news", decision.AnswerMode);
        }

        // ==========================================
        // 2. 三類切身理由各自成立與不成立
        // ==========================================

        [Fact]
        public void CloselyRelated_TellerSelf_IsCloselyRelated()
        {
            var (selector, _, _, _, _, _) = CreateTestSetup();
            var teller = new HeroSocialProfile { HeroId = "teller", RelationWithPlayer = 20 };
            var evt = CreateSampleEvent("evt_self", actorId: "teller");

            bool related = selector.IsCloselyRelated(teller, evt, day: 10.0, out var cat, out _);
            Assert.True(related);
            Assert.Equal(VolunteerReasonCategory.TellerSelf, cat);
        }

        [Fact]
        public void CloselyRelated_TellerKin_IsCloselyRelated()
        {
            var (selector, _, _, world, _, _) = CreateTestSetup();
            var teller = new HeroSocialProfile { HeroId = "teller", RelationWithPlayer = 20 };
            world.HeroFacts["teller"] = new InterestHeroFacts { SpouseId = "spouse_mary" };
            world.HeroFacts["spouse_mary"] = new InterestHeroFacts { SpouseId = "teller" };

            var evt = CreateSampleEvent("evt_kin", actorId: "spouse_mary");

            bool related = selector.IsCloselyRelated(teller, evt, day: 10.0, out var cat, out _);
            Assert.True(related);
            Assert.Equal(VolunteerReasonCategory.TellerKin, cat);
        }

        [Fact]
        public void CloselyRelated_TellerClan_IsCloselyRelated()
        {
            var (selector, _, _, world, _, _) = CreateTestSetup();
            var teller = new HeroSocialProfile { HeroId = "teller", RelationWithPlayer = 20 };
            world.HeroFacts["teller"] = new InterestHeroFacts { ClanId = "clan_vlandia_1" };
            world.HeroFacts["clanmate"] = new InterestHeroFacts { ClanId = "clan_vlandia_1" };

            var evt = CreateSampleEvent("evt_clan", actorId: "clanmate");

            bool related = selector.IsCloselyRelated(teller, evt, day: 10.0, out var cat, out _);
            Assert.True(related);
            Assert.Equal(VolunteerReasonCategory.TellerClan, cat);
        }

        [Fact]
        public void CloselyRelated_TellerRelationHighOrLow_IsCloselyRelated()
        {
            var (selector, _, _, world, _, _) = CreateTestSetup();
            var teller = new HeroSocialProfile { HeroId = "teller", RelationWithPlayer = 20 };
            world.Affections[("teller", "best_friend")] = 35;
            world.Affections[("teller", "arch_nemesis")] = -35;

            var evtFriend = CreateSampleEvent("evt_friend", actorId: "best_friend");
            var evtNemesis = CreateSampleEvent("evt_nemesis", actorId: "arch_nemesis");

            Assert.True(selector.IsCloselyRelated(teller, evtFriend, 10.0, out var catF, out _));
            Assert.Equal(VolunteerReasonCategory.TellerRelation, catF);

            Assert.True(selector.IsCloselyRelated(teller, evtNemesis, 10.0, out var catN, out _));
            Assert.Equal(VolunteerReasonCategory.TellerRelation, catN);
        }

        [Fact]
        public void CloselyRelated_TellerGrudge_IsCloselyRelated()
        {
            var (selector, _, _, world, _, _) = CreateTestSetup();
            var teller = new HeroSocialProfile { HeroId = "teller", RelationWithPlayer = 20 };
            world.Grudges[("teller", "villain")] = new List<GrudgeEntry>
            {
                new()
                {
                    FromHeroId = "teller",
                    AboutHeroId = "villain",
                    EventId = "past_evt",
                    Requested = 5.0,
                    Delta = 5,
                    Day = 9.0,
                    Scope = GrudgeScope.Personal
                }
            };

            var evt = CreateSampleEvent("evt_now", actorId: "villain");

            Assert.True(selector.IsCloselyRelated(teller, evt, day: 10.0, out var cat, out _));
            Assert.Equal(VolunteerReasonCategory.TellerGrudge, cat);
        }

        [Fact]
        public void CloselyRelated_PlayerRelated_CompanionClanSpouseKing_IsCloselyRelated()
        {
            var (selector, _, _, world, _, _) = CreateTestSetup();
            var teller = new HeroSocialProfile { HeroId = "teller", RelationWithPlayer = 20 };

            world.Companions.Add("companion_bob");
            world.ClanMembers.Add("clan_alice");
            world.Spouses.Add("spouse_helen");
            world.PlayerKingdomLeaderId = "king_derthert";

            var evtComp = CreateSampleEvent("e1", actorId: "companion_bob");
            var evtClan = CreateSampleEvent("e2", actorId: "clan_alice");
            var evtSpouse = CreateSampleEvent("e3", actorId: "spouse_helen");
            var evtKing = CreateSampleEvent("e4", actorId: "king_derthert");

            Assert.True(selector.IsCloselyRelated(teller, evtComp, 10.0, out var c1, out _));
            Assert.Equal(VolunteerReasonCategory.PlayerRelated, c1);

            Assert.True(selector.IsCloselyRelated(teller, evtClan, 10.0, out var c2, out _));
            Assert.Equal(VolunteerReasonCategory.PlayerRelated, c2);

            Assert.True(selector.IsCloselyRelated(teller, evtSpouse, 10.0, out var c3, out _));
            Assert.Equal(VolunteerReasonCategory.PlayerRelated, c3);

            Assert.True(selector.IsCloselyRelated(teller, evtKing, 10.0, out var c4, out _));
            Assert.Equal(VolunteerReasonCategory.PlayerRelated, c4);
        }

        [Fact]
        public void CloselyRelated_SequelToToldEvent_IsCloselyRelated()
        {
            var (selector, _, _, _, heardLog, _) = CreateTestSetup();
            var teller = new HeroSocialProfile { HeroId = "teller", RelationWithPlayer = 20 };

            var pastEvt = CreateSampleEvent("evt_past", actorId: "stranger");
            pastEvt.KnownBy.Add(new KnownByEntry { HeroId = "player", Hop = 1, SourceHeroId = "teller" });
            heardLog.Record(pastEvt, pastEvt.EntryFor("player")!, pastEvt.Facts, day: 5.0);

            var sequelEvt = CreateSampleEvent("evt_sequel", actorId: "stranger");
            sequelEvt.LinkedEventId = "evt_past";

            Assert.True(selector.IsCloselyRelated(teller, sequelEvt, 10.0, out var cat, out _));
            Assert.Equal(VolunteerReasonCategory.Sequel, cat);
        }

        [Fact]
        public void CloselyRelated_NoneApplicable_ReturnsFalse()
        {
            var (selector, _, _, _, _, _) = CreateTestSetup();
            var teller = new HeroSocialProfile { HeroId = "teller", RelationWithPlayer = 20 };
            var evt = CreateSampleEvent("evt_random", actorId: "random_npc");

            bool related = selector.IsCloselyRelated(teller, evt, 10.0, out var cat, out _);
            Assert.False(related);
            Assert.Equal(VolunteerReasonCategory.None, cat);
        }

        // ==========================================
        // 3. 先後順序 (未聽過 > 份量 > 新鮮 > id)
        // ==========================================

        [Fact]
        public void Ordering_UnheardOverHeard_WeightOverDayOverId()
        {
            var (selector, _, _, _, _, _) = CreateTestSetup();
            var teller = new HeroSocialProfile { HeroId = "teller", RelationWithPlayer = 25 };

            // e_heard: 份量大但玩家已聽過 (retell)
            var eHeard = CreateSampleEvent("e_heard", day: 12.0, drama: 10, actorId: "teller");
            eHeard.KnownBy.Add(new KnownByEntry { HeroId = "teller", Hop = 0 });
            eHeard.KnownBy.Add(new KnownByEntry { HeroId = "player", Hop = 2, KnownFactIds = new List<string> { "f_who" } });

            // e_unheard_small: 未聽過，份量較小 (drama 4)
            var eUnheardSmall = CreateSampleEvent("e_small", day: 10.0, drama: 4, actorId: "teller");
            eUnheardSmall.KnownBy.Add(new KnownByEntry { HeroId = "teller", Hop = 0 });

            // e_unheard_big: 未聽過，份量較大 (drama 8)
            var eUnheardBig = CreateSampleEvent("e_big", day: 10.0, drama: 8, actorId: "teller");
            eUnheardBig.KnownBy.Add(new KnownByEntry { HeroId = "teller", Hop = 0 });

            // e_unheard_big_newer: 未聽過，份量同為 8，但更新 (day 11.0)
            var eUnheardBigNewer = CreateSampleEvent("e_big_newer", day: 11.0, drama: 8, actorId: "teller");
            eUnheardBigNewer.KnownBy.Add(new KnownByEntry { HeroId = "teller", Hop = 0 });

            var candidates = new List<RumorCandidate>
            {
                new() { Event = eHeard, TellerHop = 0, PlayerExistingHop = 2 },
                new() { Event = eUnheardSmall, TellerHop = 0, PlayerExistingHop = null },
                new() { Event = eUnheardBig, TellerHop = 0, PlayerExistingHop = null },
                new() { Event = eUnheardBigNewer, TellerHop = 0, PlayerExistingHop = null }
            };

            var offer = selector.SelectVolunteered(teller, candidates, day: 15.0);
            Assert.NotNull(offer);
            // 優先挑未聽過之中份量最大、最新鮮的 e_big_newer
            Assert.Equal("e_big_newer", offer!.EventId);
        }

        // ==========================================
        // 4. 熟人無切身講大事；不熟只講大事且不帶感想
        // ==========================================

        [Fact]
        public void Ask_FamiliarHeroWithNoCloselyRelated_FallsBackToBigNewsWithFeeling()
        {
            var (selector, _, _, _, _, _) = CreateTestSetup(mode: RumorMode.Realistic);
            var teller = new HeroSocialProfile { HeroId = "teller", RelationWithPlayer = 20 }; // familiar

            var smallEvt = CreateSampleEvent("evt_small", drama: 2, actorId: "random_nobody");
            smallEvt.KnownBy.Add(new KnownByEntry { HeroId = "teller", Hop = 1 });

            var bigEvt = CreateSampleEvent("evt_big", drama: 8, actorId: "random_nobody");
            bigEvt.KnownBy.Add(new KnownByEntry { HeroId = "teller", Hop = 1 });

            var candidates = new List<RumorCandidate>
            {
                new() { Event = smallEvt, TellerHop = 1, PlayerExistingHop = null },
                new() { Event = bigEvt, TellerHop = 1, PlayerExistingHop = null }
            };

            var decision = selector.DecideOnAsk(teller, candidates, day: 15.0);
            Assert.Equal(AskRefusal.None, decision.Refusal);
            Assert.NotNull(decision.Offer);
            Assert.Equal("evt_big", decision.Offer!.EventId);
            Assert.Equal("familiar_big_news", decision.AnswerMode);
        }

        [Fact]
        public void Ask_UnfamiliarStranger_RejectsSmallEvents_AnswersBigNewsWithoutFeeling()
        {
            var (selector, _, _, _, _, _) = CreateTestSetup(mode: RumorMode.Realistic);
            var teller = new HeroSocialProfile { HeroId = "teller", RelationWithPlayer = 5 }; // unfamiliar but willingness >= 5

            var smallEvt = CreateSampleEvent("evt_small", drama: 3, actorId: "random_nobody");
            smallEvt.KnownBy.Add(new KnownByEntry { HeroId = "teller", Hop = 1 });

            // 只有小事 -> 拒絕
            var decSmall = selector.DecideOnAsk(teller, new List<RumorCandidate>
            {
                new() { Event = smallEvt, TellerHop = 1, PlayerExistingHop = null }
            }, day: 15.0);
            Assert.Equal(AskRefusal.AllCandidatesFiltered, decSmall.Refusal);

            // 有大事 -> 講大事，不帶感想
            var bigEvt = CreateSampleEvent("evt_big", drama: 7, actorId: "random_nobody");
            bigEvt.KnownBy.Add(new KnownByEntry { HeroId = "teller", Hop = 1 });

            var decBig = selector.DecideOnAsk(teller, new List<RumorCandidate>
            {
                new() { Event = smallEvt, TellerHop = 1, PlayerExistingHop = null },
                new() { Event = bigEvt, TellerHop = 1, PlayerExistingHop = null }
            }, day: 15.0);
            Assert.Equal(AskRefusal.None, decBig.Refusal);
            Assert.NotNull(decBig.Offer);
            Assert.Equal("evt_big", decBig.Offer!.EventId);
            Assert.Equal("unfamiliar_big_news", decBig.AnswerMode);
            Assert.Null(decBig.Offer.Composed.Feeling);
        }

        // ==========================================
        // 5. 結局跟著開頭算 & 聽過結局不再講開頭
        // ==========================================

        [Fact]
        public void CaptivityEnding_LinkedToBigOpening_InheritsEffectiveWeight()
        {
            var (selector, _, _, _, _, repo) = CreateTestSetup();

            var opening = CreateSampleEvent("evt_captured", drama: 8);
            repo["evt_captured"] = opening;

            var ending = CreateSampleEvent("evt_released", drama: 2);
            ending.Type = "hero_released";
            ending.LinkedEventId = "evt_captured";

            int effective = selector.GetEffectiveWeight(ending);
            Assert.Equal(8, effective);
        }

        [Theory]
        [InlineData("hero_released")]
        [InlineData("hero_escaped_captivity")]
        [InlineData("hero_escaped_bandits")]
        [InlineData("hero_rescued_from_bandits")]
        public void CaptivityEnding_EveryKindOfEnding_InheritsABigOpening(string endingType)
        {
            var (selector, _, _, _, _, repo) = CreateTestSetup();

            var opening = CreateSampleEvent("evt_captured", drama: 7);
            repo["evt_captured"] = opening;

            var ending = CreateSampleEvent("evt_ending", drama: 3);
            ending.Type = endingType;
            ending.LinkedEventId = "evt_captured";

            Assert.Equal(7, selector.GetEffectiveWeight(ending));
        }

        [Fact]
        public void StandingWithPlayer_IsTheOnePlaceThatDecidesFamiliarAndCanAnswer()
        {
            var (casual, _, _, _, _, _) = CreateTestSetup(mode: RumorMode.Casual);
            var (realistic, _, _, _, _, _) = CreateTestSetup(mode: RumorMode.Realistic);
            var teller = new HeroSocialProfile { HeroId = "teller", RelationWithPlayer = 3 };

            var c = casual.StandingWithPlayer(teller);
            Assert.True(c.IsFamiliar);          // 暢玩的線是 0
            Assert.True(c.CanAnswer);           // 熟的人被問一定肯答，即使沒到打聽的門檻 5
            Assert.Equal(casual.WillVolunteer(teller, 1.0), c.IsFamiliar);
            Assert.Equal(casual.WillAnswerAsk(teller), c.CanAnswer);

            var r = realistic.StandingWithPlayer(teller);
            Assert.False(r.IsFamiliar);         // 寫實的線是 10
            Assert.False(r.CanAnswer);          // 也沒到打聽的門檻 5
            Assert.Equal(realistic.WillVolunteer(teller, 1.0), r.IsFamiliar);
            Assert.Equal(realistic.WillAnswerAsk(teller), r.CanAnswer);
        }

        [Fact]
        public void CaptivityEnding_UnlinkedOrMissingOpening_KeepsOwnWeight()
        {
            var (selector, _, _, _, _, _) = CreateTestSetup();

            var ending = CreateSampleEvent("evt_released", drama: 2);
            ending.Type = "hero_released";
            ending.LinkedEventId = "non_existent_opening";

            int effective = selector.GetEffectiveWeight(ending);
            Assert.Equal(2, effective);
        }

        [Fact]
        public void CaptivityOpening_WhenPlayerHeardEnding_IsFiltered()
        {
            var (selector, _, _, _, heardLog, _) = CreateTestSetup();
            var teller = new HeroSocialProfile { HeroId = "teller", RelationWithPlayer = 20 };

            var opening = CreateSampleEvent("evt_captured", drama: 8, actorId: "teller");
            opening.KnownBy.Add(new KnownByEntry { HeroId = "teller", Hop = 0 });

            var ending = CreateSampleEvent("evt_released", drama: 8, actorId: "teller");
            ending.LinkedEventId = "evt_captured";
            ending.KnownBy.Add(new KnownByEntry { HeroId = "player", Hop = 1, SourceHeroId = "teller" });
            heardLog.Record(ending, ending.EntryFor("player")!, ending.Facts, day: 5.0);

            var candidates = new List<RumorCandidate>
            {
                new() { Event = opening, TellerHop = 0, PlayerExistingHop = null }
            };

            var decision = selector.DecideOnAsk(teller, candidates, day: 15.0);
            Assert.Equal(AskRefusal.AllCandidatesFiltered, decision.Refusal);
            Assert.Contains(decision.FilterNotes, n => n.Contains("player already heard ending for this captivity event"));
        }

        // ==========================================
        // 6. 走漏秘密的四象限
        // ==========================================

        [Fact]
        public void LeakedSecret_HonorableHero_NeverTells()
        {
            var (selector, _, _, _, _, _) = CreateTestSetup(mode: RumorMode.Casual);
            var teller = new HeroSocialProfile
            {
                HeroId = "teller_honorable",
                RelationWithPlayer = 20,
                Traits = new TraitProfile { Honor = 1, Valor = 0 }
            };

            var secretEvt = CreateSampleEvent("sec_1", drama: 8, origin: EventOrigin.Secret, actorId: "holder");
            secretEvt.State.Leaked = true;
            secretEvt.KnownBy.Add(new KnownByEntry { HeroId = "teller_honorable", Hop = 1 });

            var candidates = new List<RumorCandidate>
            {
                new() { Event = secretEvt, TellerHop = 1, PlayerExistingHop = null }
            };

            var decision = selector.DecideOnAsk(teller, candidates, day: 15.0);
            Assert.Equal(AskRefusal.AllCandidatesFiltered, decision.Refusal);
            Assert.Contains(decision.FilterNotes, n => n.Contains("honorable people do not spread secrets"));
        }

        [Fact]
        public void LeakedSecret_DishonorableCautious_TellsFamiliar_RejectsStranger()
        {
            var (selector, _, _, _, _, _) = CreateTestSetup(mode: RumorMode.Realistic);

            var secretEvt = CreateSampleEvent("sec_1", drama: 8, origin: EventOrigin.Secret, actorId: "holder");
            secretEvt.State.Leaked = true;
            secretEvt.KnownBy.Add(new KnownByEntry { HeroId = "teller", Hop = 1 });

            var candidate = new RumorCandidate { Event = secretEvt, TellerHop = 1, PlayerExistingHop = null };

            // 1. 不熟 (willingness 10 - 4 = 6.0 >= threshold 5.0 但 < Realistic line 10.0) -> 拒絕
            var tellerStranger = new HeroSocialProfile
            {
                HeroId = "teller",
                RelationWithPlayer = 10,
                Traits = new TraitProfile { Honor = -1, Valor = -1 }
            };
            var decStranger = selector.DecideOnAsk(tellerStranger, new[] { candidate }, day: 15.0);
            Assert.Equal(AskRefusal.AllCandidatesFiltered, decStranger.Refusal);
            Assert.Contains(decStranger.FilterNotes, n => n.Contains("valor -1 <= -1 and not familiar"));

            // 2. 熟人 (willingness 15 >= Realistic line 10) -> 講
            var tellerFamiliar = new HeroSocialProfile
            {
                HeroId = "teller",
                RelationWithPlayer = 20,
                Traits = new TraitProfile { Honor = -1, Valor = -1 }
            };
            var decFamiliar = selector.DecideOnAsk(tellerFamiliar, new[] { candidate }, day: 15.0);
            Assert.Equal(AskRefusal.None, decFamiliar.Refusal);
            Assert.NotNull(decFamiliar.Offer);
            Assert.Equal("sec_1", decFamiliar.Offer!.EventId);
        }

        [Fact]
        public void LeakedSecret_DishonorableBrave_TellsAnyone()
        {
            var (selector, _, _, _, _, _) = CreateTestSetup(mode: RumorMode.Realistic);

            var secretEvt = CreateSampleEvent("sec_1", drama: 8, origin: EventOrigin.Secret, actorId: "holder");
            secretEvt.State.Leaked = true;
            secretEvt.KnownBy.Add(new KnownByEntry { HeroId = "teller", Hop = 1 });

            var candidate = new RumorCandidate { Event = secretEvt, TellerHop = 1, PlayerExistingHop = null };

            var tellerStranger = new HeroSocialProfile
            {
                HeroId = "teller",
                RelationWithPlayer = 5,
                Traits = new TraitProfile { Honor = 0, Valor = 1 }
            };
            var decision = selector.DecideOnAsk(tellerStranger, new[] { candidate }, day: 15.0);
            Assert.Equal(AskRefusal.None, decision.Refusal);
            Assert.NotNull(decision.Offer);
            Assert.Equal("sec_1", decision.Offer!.EventId);
        }

        // ==========================================
        // 7. 秘密當事人線
        // ==========================================

        [Fact]
        public void SecretHolder_WillingnessBelow30_RefusesOwnSecret()
        {
            var (selector, _, _, _, _, _) = CreateTestSetup(mode: RumorMode.Casual);

            var holder = new HeroSocialProfile
            {
                HeroId = "holder",
                RelationWithPlayer = 25, // < SecretLine (30)
                Traits = new TraitProfile { Generosity = 0, Honor = 0, Calculating = 0 }
            };

            var secretEvt = CreateSampleEvent("sec_own", drama: 8, origin: EventOrigin.Secret, actorId: "holder");
            secretEvt.State.Leaked = true;
            secretEvt.KnownBy.Add(new KnownByEntry { HeroId = "holder", Hop = 0 });

            var candidates = new List<RumorCandidate>
            {
                new() { Event = secretEvt, TellerHop = 0, PlayerExistingHop = null }
            };

            var decision = selector.DecideOnAsk(holder, candidates, day: 15.0);
            Assert.Equal(AskRefusal.AllCandidatesFiltered, decision.Refusal);
            Assert.Contains(decision.FilterNotes, n => n.Contains("secret holder willingness 25.0 < secret line 30.0"));
        }

        [Fact]
        public void SecretHolder_Willingness30OrAbove_TellsOwnSecret()
        {
            var (selector, _, _, _, _, _) = CreateTestSetup(mode: RumorMode.Casual);

            var holder = new HeroSocialProfile
            {
                HeroId = "holder",
                RelationWithPlayer = 32, // >= SecretLine (30)
                Traits = new TraitProfile { Generosity = 0, Honor = 0, Calculating = 0 }
            };

            var secretEvt = CreateSampleEvent("sec_own", drama: 8, origin: EventOrigin.Secret, actorId: "holder");
            secretEvt.State.Leaked = true;
            secretEvt.KnownBy.Add(new KnownByEntry { HeroId = "holder", Hop = 0 });

            var candidates = new List<RumorCandidate>
            {
                new() { Event = secretEvt, TellerHop = 0, PlayerExistingHop = null }
            };

            var decision = selector.DecideOnAsk(holder, candidates, day: 15.0);
            Assert.Equal(AskRefusal.None, decision.Refusal);
            Assert.NotNull(decision.Offer);
            Assert.Equal("sec_own", decision.Offer!.EventId);
        }

        // ==========================================
        // 8. 徹底移除 Gist
        // ==========================================

        [Fact]
        public void Offers_NeverProduceGistOrClosing()
        {
            var (selector, _, _, _, _, _) = CreateTestSetup();
            var teller = new HeroSocialProfile { HeroId = "teller", RelationWithPlayer = 30 };

            var evt = CreateSampleEvent("evt_test", drama: 6, actorId: "teller");
            evt.KnownBy.Add(new KnownByEntry { HeroId = "teller", Hop = 0 });

            var candidates = new List<RumorCandidate>
            {
                new() { Event = evt, TellerHop = 0, PlayerExistingHop = null }
            };

            var volunteerOffer = selector.SelectVolunteered(teller, candidates, day: 10.0);
            Assert.NotNull(volunteerOffer);
            Assert.False(volunteerOffer!.IsGist);
            Assert.False(volunteerOffer.HeldBack);
            Assert.False(volunteerOffer.Composed.IsGist);
            Assert.False(volunteerOffer.Composed.HeldBack);
            Assert.Null(volunteerOffer.Composed.ClosingKey);

            var askOffer = selector.SelectOnAsk(teller, candidates, day: 10.0);
            Assert.NotNull(askOffer);
            Assert.False(askOffer!.IsGist);
            Assert.False(askOffer.HeldBack);
            Assert.False(askOffer.Composed.IsGist);
            Assert.False(askOffer.Composed.HeldBack);
            Assert.Null(askOffer.Composed.ClosingKey);
        }
    }
}
