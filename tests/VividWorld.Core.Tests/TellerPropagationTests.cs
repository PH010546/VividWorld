using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using VividWorld.Core.Channels;
using VividWorld.Core.Config;
using VividWorld.Core.Events;
using VividWorld.Core.Memory;
using VividWorld.Core.Persistence;
using VividWorld.Core.Rumors;
using VividWorld.Core.Tests.Fakes;
using VividWorld.Core.Util;
using Xunit;

namespace VividWorld.Core.Tests
{
    public class TellerPropagationTests
    {
        private (RumorEngine engine, FakePropagationChannel channel, FakeHeroTraitLookup traits, VividWorldConfig cfg)
            CreateEngine(string playerHeroId = "player", long seed = 42L, VividWorldConfig? customConfig = null, IDeterministicRng? customRng = null)
        {
            var cfg = customConfig ?? new VividWorldConfig();
            cfg.Propagation.BaseTellChancePerContact = 1.0;
            var rng = customRng ?? new SplitMix64Rng();
            var channel = new FakePropagationChannel();
            var traits = new FakeHeroTraitLookup();
            var retention = FactRetentionPolicies.Create(cfg, rng, seed);
            var embellishment = NullEmbellishmentPolicy.Instance;
            var engine = new RumorEngine(cfg, retention, embellishment, channel, traits, rng, seed, playerHeroId);
            return (engine, channel, traits, cfg);
        }

        private WorldEvent CreateTestEvent(string id = "evt_1", int drama = 3, double day = 10.0, EventOrigin origin = EventOrigin.Public)
        {
            var evt = new WorldEvent
            {
                EventId = id,
                Type = "test_event",
                DramaWeight = drama,
                Day = day,
                Origin = origin,
                Facts = new List<Fact>
                {
                    new Fact { Id = "f1", Text = "Something happened", TextId = "VividWorld_Fact_f1" }
                },
                KnownBy = new List<KnownByEntry>()
            };
            if (origin == EventOrigin.Secret)
            {
                evt.State = new RumorState { Leaked = false };
            }
            return evt;
        }

        #region 1. Config Tests

        [Fact]
        public void Config_Defaults_MatchDocumentedValues()
        {
            var cfg = new VividWorldConfig();
            Assert.Equal(8, cfg.Scheduling.TellersPerHourlyTick);
            Assert.Equal(new[] { 0.35, 0.6, 1.0, 1.5, 2.2 }, cfg.Propagation.TopicDramaWeight);
            Assert.Equal(0.1, cfg.Propagation.TopicFreshnessFloor);
            Assert.Equal(new[] { 0.05, 0.05, 0.1, 0.2, 0.4 }, cfg.Memory.InterestFloors.OtherByDrama);
            Assert.False(cfg.Debug.LogTellerTurns);   // 發行前改成 false
        }

        [Fact]
        public void Normalize_ClampsTellersPerHourlyTick_AndGeneratesClampNotice()
        {
            var cfg = new VividWorldConfig();
            cfg.Scheduling.TellersPerHourlyTick = 0;
            var notices = new List<ClampNotice>();
            cfg.Normalize(notices);

            Assert.Equal(1, cfg.Scheduling.TellersPerHourlyTick);
            Assert.Contains(notices, n => n.Key == "scheduling.tellersPerHourlyTick" && n.Applied == 1.0);
        }

        [Fact]
        public void Normalize_ClampsTopicFreshnessFloor()
        {
            var cfg1 = new VividWorldConfig();
            cfg1.Propagation.TopicFreshnessFloor = -0.5;
            var notices = new List<ClampNotice>();
            cfg1.Normalize(notices);
            Assert.Equal(0.0, cfg1.Propagation.TopicFreshnessFloor);
            Assert.Contains(notices, n => n.Key == "propagation.topicFreshnessFloor");

            var cfg2 = new VividWorldConfig();
            cfg2.Propagation.TopicFreshnessFloor = 1.5;
            cfg2.Normalize();
            Assert.Equal(1.0, cfg2.Propagation.TopicFreshnessFloor);
        }

        [Fact]
        public void Normalize_PadsShortArrays()
        {
            var cfg = new VividWorldConfig();
            cfg.Propagation.TopicDramaWeight = new[] { 0.5, 1.2 };
            cfg.Memory.InterestFloors.OtherByDrama = new[] { 0.1, 0.2 };
            cfg.Normalize();

            Assert.Equal(5, cfg.Propagation.TopicDramaWeight.Length);
            Assert.Equal(0.5, cfg.Propagation.TopicDramaWeight[0]);
            Assert.Equal(1.2, cfg.Propagation.TopicDramaWeight[1]);
            Assert.Equal(1.2, cfg.Propagation.TopicDramaWeight[2]);
            Assert.Equal(1.2, cfg.Propagation.TopicDramaWeight[3]);
            Assert.Equal(1.2, cfg.Propagation.TopicDramaWeight[4]);

            Assert.Equal(5, cfg.Memory.InterestFloors.OtherByDrama.Length);
            Assert.Equal(0.1, cfg.Memory.InterestFloors.OtherByDrama[0]);
            Assert.Equal(0.2, cfg.Memory.InterestFloors.OtherByDrama[1]);
            Assert.Equal(0.2, cfg.Memory.InterestFloors.OtherByDrama[2]);
            Assert.Equal(0.2, cfg.Memory.InterestFloors.OtherByDrama[3]);
            Assert.Equal(0.2, cfg.Memory.InterestFloors.OtherByDrama[4]);
        }

        [Fact]
        public void ConfigMerge_AddsOtherByDramaToFourKeyInterestFloors_WithoutAlteringExisting()
        {
            string oldJson = @"{
  ""memory"": {
    ""interestFloors"": {
      ""participant"": 0.85,
      ""kin"": 0.65,
      ""sameClan"": 0.45,
      ""other"": 0.15
    }
  }
}";
            var existing = JObject.Parse(oldJson);
            var canonical = JObject.Parse(VividJson.Write(new VividWorldConfig()));
            var result = ConfigMerge.AddMissingKeys(existing, canonical);
            var obj = result.Merged;

            var floors = obj["memory"]?["interestFloors"];
            Assert.NotNull(floors);
            Assert.Equal(0.85, (double)floors!["participant"]!);
            Assert.Equal(0.65, (double)floors["kin"]!);
            Assert.Equal(0.45, (double)floors["sameClan"]!);
            Assert.Equal(0.15, (double)floors["other"]!);

            var otherByDrama = floors["otherByDrama"];
            Assert.NotNull(otherByDrama);
            var arr = otherByDrama!.ToObject<double[]>();
            Assert.NotNull(arr);
            Assert.Equal(new[] { 0.05, 0.05, 0.1, 0.2, 0.4 }, arr);
        }

        #endregion

        #region 2. Serialization Tests

        [Fact]
        public void KnownByEntry_Deserialization_NullHeardFromIds_WhenMissing()
        {
            string json = @"{
  ""HeroId"": ""hero_1"",
  ""Hop"": 1,
  ""SourceHeroId"": ""hero_0"",
  ""LearnedDay"": 5.0,
  ""LastHeardDay"": 5.0,
  ""HeardCount"": 1
}";
            var entry = JsonConvert.DeserializeObject<KnownByEntry>(json);
            Assert.NotNull(entry);
            Assert.Null(entry!.HeardFromIds);
        }

        [Fact]
        public void KnownByEntry_Serialization_RoundTrip_PreservesHeardFromIds()
        {
            var original = new KnownByEntry
            {
                HeroId = "hero_1",
                Hop = 1,
                SourceHeroId = "hero_0",
                LearnedDay = 5.0,
                LastHeardDay = 6.0,
                HeardCount = 2,
                HeardFromIds = new List<string> { "hero_0", "hero_2" }
            };

            string json = JsonConvert.SerializeObject(original);
            var deserialized = JsonConvert.DeserializeObject<KnownByEntry>(json);

            Assert.NotNull(deserialized);
            Assert.NotNull(deserialized!.HeardFromIds);
            Assert.Equal(2, deserialized.HeardFromIds!.Count);
            Assert.Equal("hero_0", deserialized.HeardFromIds[0]);
            Assert.Equal("hero_2", deserialized.HeardFromIds[1]);
        }

        #endregion

        #region 3. TellerEligibility Tests

        [Fact]
        public void TellerEligibility_ReturnsOk_WhenEligible()
        {
            var evt = CreateTestEvent();
            evt.KnownBy.Add(new KnownByEntry { HeroId = "lord_1", Hop = 1 });
            var traits = new FakeHeroTraitLookup();
            traits.Set(new TraitProfile { HeroId = "lord_1", IsAlive = true, IsLord = true });
            var cfg = new VividWorldConfig();

            var reason = TellerEligibility.Check(evt, "lord_1", 10.0, 3, "player", cfg, traits);
            Assert.Equal(TellReason.Ok, reason);
        }

        [Fact]
        public void TellerEligibility_ReturnsNotVisible_WhenEventInvisible()
        {
            var evt = CreateTestEvent(origin: EventOrigin.Secret);
            evt.KnownBy.Add(new KnownByEntry { HeroId = "lord_1", Hop = 0 });
            var traits = new FakeHeroTraitLookup();
            traits.Set(new TraitProfile { HeroId = "lord_1", IsAlive = true, IsLord = true });
            var cfg = new VividWorldConfig();

            var reason = TellerEligibility.Check(evt, "lord_1", 10.0, 3, "player", cfg, traits);
            Assert.Equal(TellReason.NotVisible, reason);
        }

        [Fact]
        public void TellerEligibility_ReturnsPlayer_WhenHeroIsPlayer_AndPlayerCanTellFalse()
        {
            var evt = CreateTestEvent();
            evt.KnownBy.Add(new KnownByEntry { HeroId = "player", Hop = 0 });
            var traits = new FakeHeroTraitLookup();
            var cfg = new VividWorldConfig();
            cfg.Propagation.PlayerCanTell = false;

            var reason = TellerEligibility.Check(evt, "player", 10.0, 3, "player", cfg, traits);
            Assert.Equal(TellReason.Player, reason);
        }

        [Fact]
        public void TellerEligibility_ReturnsNoEntry_WhenNoKnownByEntry()
        {
            var evt = CreateTestEvent();
            var traits = new FakeHeroTraitLookup();
            traits.Set(new TraitProfile { HeroId = "lord_1", IsAlive = true, IsLord = true });
            var cfg = new VividWorldConfig();

            var reason = TellerEligibility.Check(evt, "lord_1", 10.0, 3, "player", cfg, traits);
            Assert.Equal(TellReason.NoEntry, reason);
        }

        [Fact]
        public void TellerEligibility_ReturnsNotEligible_WhenHeroDeadOrNull()
        {
            var evt = CreateTestEvent();
            evt.KnownBy.Add(new KnownByEntry { HeroId = "dead_lord", Hop = 0 });
            var traits = new FakeHeroTraitLookup();
            traits.Set(new TraitProfile { HeroId = "dead_lord", IsAlive = false, IsLord = true });
            var cfg = new VividWorldConfig();

            var reason = TellerEligibility.Check(evt, "dead_lord", 10.0, 3, "player", cfg, traits);
            Assert.Equal(TellReason.NotEligible, reason);
        }

        [Fact]
        public void TellerEligibility_ReturnsForgotten_WhenHeroHasForgotten()
        {
            var evt = CreateTestEvent();
            evt.KnownBy.Add(new KnownByEntry
            {
                HeroId = "lord_1",
                Hop = 0,
                ForgetDay = 5.0,
                Interest = 0.5
            });
            var traits = new FakeHeroTraitLookup();
            traits.Set(new TraitProfile { HeroId = "lord_1", IsAlive = true, IsLord = true });
            var cfg = new VividWorldConfig();

            var reason = TellerEligibility.Check(evt, "lord_1", 10.0, 3, "player", cfg, traits);
            Assert.Equal(TellReason.Forgotten, reason);
        }

        [Fact]
        public void TellerEligibility_ReturnsAtMaxHop_WhenHopAtOrAboveMax()
        {
            var evt = CreateTestEvent();
            evt.KnownBy.Add(new KnownByEntry { HeroId = "lord_1", Hop = 3 });
            var traits = new FakeHeroTraitLookup();
            traits.Set(new TraitProfile { HeroId = "lord_1", IsAlive = true, IsLord = true });
            var cfg = new VividWorldConfig();

            var reason = TellerEligibility.Check(evt, "lord_1", 10.0, 3, "player", cfg, traits);
            Assert.Equal(TellReason.AtMaxHop, reason);
        }

        [Fact]
        public void TellerEligibility_ReturnsLeakNotSpread_WhenJustLeakedAndNotLeaker()
        {
            var evt = CreateTestEvent(origin: EventOrigin.Secret);
            evt.State = new RumorState { Leaked = true, LeakerHeroId = "leaker_1" };
            evt.KnownBy.Add(new KnownByEntry { HeroId = "leaker_1", Hop = 0 });
            evt.KnownBy.Add(new KnownByEntry { HeroId = "insider_2", Hop = 0 });

            var traits = new FakeHeroTraitLookup();
            traits.Set(new TraitProfile { HeroId = "leaker_1", IsAlive = true, IsLord = true });
            traits.Set(new TraitProfile { HeroId = "insider_2", IsAlive = true, IsLord = true });
            var cfg = new VividWorldConfig();

            // Leaker is Ok
            var reasonLeaker = TellerEligibility.Check(evt, "leaker_1", 10.0, 3, "player", cfg, traits);
            Assert.Equal(TellReason.Ok, reasonLeaker);

            // Insider 2 is LeakNotSpread
            var reasonInsider = TellerEligibility.Check(evt, "insider_2", 10.0, 3, "player", cfg, traits);
            Assert.Equal(TellReason.LeakNotSpread, reasonInsider);
        }

        [Fact]
        public void TellerEligibility_Priority_ForgottenOverAtMaxHop()
        {
            var evt = CreateTestEvent();
            evt.KnownBy.Add(new KnownByEntry
            {
                HeroId = "lord_1",
                Hop = 3, // At max hop
                ForgetDay = 5.0, // Also forgotten at day 10.0
                Interest = 0.5
            });
            var traits = new FakeHeroTraitLookup();
            traits.Set(new TraitProfile { HeroId = "lord_1", IsAlive = true, IsLord = true });
            var cfg = new VividWorldConfig();

            // Rule 5 (Forgotten) should trigger before Rule 6 (AtMaxHop)
            var reason = TellerEligibility.Check(evt, "lord_1", 10.0, 3, "player", cfg, traits);
            Assert.Equal(TellReason.Forgotten, reason);
        }

        #endregion

        #region 4. PropagateOnce Equivalence Tests

        [Fact]
        public void PropagateOnce_GeneralBranch_CandidatesAndOutcomeEquivalent()
        {
            var (engine, channel, traits, cfg) = CreateEngine(seed: 12345L);
            traits.Set(new TraitProfile { HeroId = "h0", IsAlive = true, IsLord = true });
            traits.Set(new TraitProfile { HeroId = "h1", IsAlive = true, IsLord = true });
            traits.Set(new TraitProfile { HeroId = "c1", IsAlive = true, IsLord = true });
            channel.AddLink("h0", "c1", ChannelKind.SameParty);
            channel.AddLink("h1", "c1", ChannelKind.SameParty);

            var evt = CreateTestEvent("evt_eq1", drama: 3, day: 10.0);
            evt.KnownBy.Add(new KnownByEntry { HeroId = "h0", Hop = 0 });
            evt.KnownBy.Add(new KnownByEntry { HeroId = "h1", Hop = 1 });

            var outcome = engine.PropagateOnce(evt, 10.0, 0);

            Assert.NotEmpty(outcome.NewKnowers);
            Assert.Equal("c1", outcome.NewKnowers[0].HeroId);
            Assert.Equal("h1", outcome.TellerHeroId);
            Assert.Equal(2, outcome.NewKnowers[0].Hop);
        }

        [Fact]
        public void PropagateOnce_SecretJustLeakedBranch_OnlyLeakerEligible()
        {
            var (engine, channel, traits, cfg) = CreateEngine(seed: 9999L);
            traits.Set(new TraitProfile { HeroId = "leaker", IsAlive = true, IsLord = true });
            traits.Set(new TraitProfile { HeroId = "insider", IsAlive = true, IsLord = true });
            traits.Set(new TraitProfile { HeroId = "contact", IsAlive = true, IsLord = true });
            channel.AddLink("leaker", "contact", ChannelKind.SameParty);
            channel.AddLink("insider", "contact", ChannelKind.SameParty);

            var evt = CreateTestEvent("evt_leak_eq", drama: 4, day: 10.0, origin: EventOrigin.Secret);
            evt.State = new RumorState { Leaked = true, LeakerHeroId = "leaker" };
            evt.KnownBy.Add(new KnownByEntry { HeroId = "insider", Hop = 0 });
            evt.KnownBy.Add(new KnownByEntry { HeroId = "leaker", Hop = 0 });

            var outcome = engine.PropagateOnce(evt, 10.0, 0);

            Assert.Single(outcome.NewKnowers);
            Assert.Equal("contact", outcome.NewKnowers[0].HeroId);
            Assert.Equal("leaker", outcome.NewKnowers[0].SourceHeroId);
        }

        #endregion

        #region 5. Topic Choice Tests

        [Fact]
        public void ChooseTopic_FormulaTerms_MatchesDramaWeightTellFreshness()
        {
            var (engine, channel, traits, cfg) = CreateEngine();
            traits.Set(new TraitProfile { HeroId = "teller", IsAlive = true, IsLord = true });

            var evt = CreateTestEvent("e1", drama: 3, day: 10.0);
            evt.KnownBy.Add(new KnownByEntry { HeroId = "teller", Hop = 0 });

            // Drama 3 weight = 1.0 (TopicDramaWeight[2])
            // Tell factor for unstamped entry = 1.0
            // Freshness: day=10, evt.Day=10 -> 1 - 0/120 = 1.0
            // Weight = 1.0 * 1.0 * 1.0 = 1.0
            var choice = engine.ChooseTopic("teller", new[] { evt }, 10.0, 12);

            Assert.Single(choice.Candidates);
            Assert.Equal(0, choice.PickedIndex);
            Assert.Equal("e1", choice.PickedEventId);
            var cand = choice.Candidates[0];
            Assert.Equal(3, cand.Drama);
            Assert.Equal(1.0, cand.Tell, 3);
            Assert.Equal(1.0, cand.Freshness, 3);
            Assert.Equal(1.0, cand.Weight, 3);
            Assert.Equal(1.0, choice.Chance, 3);
        }

        [Fact]
        public void ChooseTopic_FreshnessFloor_AppliedWhenOlderThanLifetime()
        {
            var (engine, channel, traits, cfg) = CreateEngine();
            traits.Set(new TraitProfile { HeroId = "teller", IsAlive = true, IsLord = true });

            // day = 200, evt.Day = 10, difference = 190 > 120 (RumorLifetimeDays)
            var evt = CreateTestEvent("e_old", drama: 3, day: 10.0);
            evt.KnownBy.Add(new KnownByEntry { HeroId = "teller", Hop = 0 });

            var choice = engine.ChooseTopic("teller", new[] { evt }, 200.0, 12);

            Assert.Single(choice.Candidates);
            var cand = choice.Candidates[0];
            Assert.Equal(cfg.Propagation.TopicFreshnessFloor, cand.Freshness, 3);
            Assert.Equal(0.1, cand.Freshness, 3);
        }

        [Fact]
        public void ChooseTopic_FreshnessMax_ClampedToOneWithinTolerance()
        {
            var (engine, channel, traits, cfg) = CreateEngine();
            traits.Set(new TraitProfile { HeroId = "teller", IsAlive = true, IsLord = true });

            // 事件日期只比今天晚一點點（存檔時間與事件日期本來就有落差），
            // 還在 EventVisibility.ToleranceDays = 0.5 之內 ⇒ 算數，新鮮度夾成 1.0。
            var evt = CreateTestEvent("e_just_ahead", drama: 3, day: 10.3);
            evt.KnownBy.Add(new KnownByEntry { HeroId = "teller", Hop = 0 });

            var choice = engine.ChooseTopic("teller", new[] { evt }, 10.0, 12);

            Assert.Single(choice.Candidates);
            Assert.Equal(1.0, choice.Candidates[0].Freshness, 3);
        }

        [Fact]
        public void ChooseTopic_EventWellAfterToday_IsNotACandidateAtAll()
        {
            // 規格 §2.2.1：超過容差就是一條被抹掉的時間線。
            // 舊行為是新鮮度被夾成 1.0＝滿分新鮮，讓它變成 NPC 最想講的話題；
            // 現在它連候選都進不去，而且排除理由講得出來。
            var (engine, channel, traits, cfg) = CreateEngine();
            traits.Set(new TraitProfile { HeroId = "teller", IsAlive = true, IsLord = true });

            var evt = CreateTestEvent("e_future", drama: 3, day: 15.0);
            evt.KnownBy.Add(new KnownByEntry { HeroId = "teller", Hop = 0 });

            var choice = engine.ChooseTopic("teller", new[] { evt }, 10.0, 12);

            Assert.Empty(choice.Candidates);
            Assert.Contains(choice.Exclusions, e => e.EventId == "e_future" && e.Reason == TellReason.FutureEvent);
        }

        [Fact]
        public void ChooseTopic_Exclusions_PopulatedWithSpecificReasons()
        {
            var (engine, channel, traits, cfg) = CreateEngine();
            traits.Set(new TraitProfile { HeroId = "teller", IsAlive = true, IsLord = true });

            var evtForgotten = CreateTestEvent("e_forgotten", drama: 2, day: 1.0);
            evtForgotten.KnownBy.Add(new KnownByEntry
            {
                HeroId = "teller",
                Hop = 0,
                ForgetDay = 5.0,
                Interest = 0.5
            });

            var evtMaxHop = CreateTestEvent("e_maxhop", drama: 1, day: 10.0);
            evtMaxHop.KnownBy.Add(new KnownByEntry { HeroId = "teller", Hop = 2 }); // Drama 1 MaxHop = 2

            var evtOk = CreateTestEvent("e_ok", drama: 4, day: 10.0);
            evtOk.KnownBy.Add(new KnownByEntry { HeroId = "teller", Hop = 0 });

            var choice = engine.ChooseTopic("teller", new[] { evtForgotten, evtMaxHop, evtOk }, 10.0, 12);

            Assert.Single(choice.Candidates);
            Assert.Equal("e_ok", choice.Candidates[0].EventId);
            Assert.Equal(2, choice.Exclusions.Count);
            Assert.Contains(choice.Exclusions, e => e.EventId == "e_forgotten" && e.Reason == TellReason.Forgotten);
            Assert.Contains(choice.Exclusions, e => e.EventId == "e_maxhop" && e.Reason == TellReason.AtMaxHop);
        }

        [Fact]
        public void ChooseTopic_AllExcluded_ReturnsPickedIndexMinusOne()
        {
            var (engine, channel, traits, cfg) = CreateEngine();
            traits.Set(new TraitProfile { HeroId = "teller", IsAlive = true, IsLord = true });

            var evt = CreateTestEvent("e1", drama: 1, day: 10.0);
            evt.KnownBy.Add(new KnownByEntry { HeroId = "teller", Hop = 2 }); // AtMaxHop

            var choice = engine.ChooseTopic("teller", new[] { evt }, 10.0, 12);

            Assert.Empty(choice.Candidates);
            Assert.Equal(-1, choice.PickedIndex);
            Assert.Null(choice.PickedEventId);
            Assert.Equal(0.0, choice.Chance);
            Assert.Single(choice.Exclusions);
        }

        [Fact]
        public void ChooseTopic_DeterministicSeed_PicksSameEvent()
        {
            var (engine, channel, traits, cfg) = CreateEngine(seed: 42L);
            traits.Set(new TraitProfile { HeroId = "teller", IsAlive = true, IsLord = true });

            var evt1 = CreateTestEvent("e1", drama: 3, day: 10.0);
            evt1.KnownBy.Add(new KnownByEntry { HeroId = "teller", Hop = 0 });
            var evt2 = CreateTestEvent("e2", drama: 4, day: 10.0);
            evt2.KnownBy.Add(new KnownByEntry { HeroId = "teller", Hop = 0 });

            var choice1 = engine.ChooseTopic("teller", new[] { evt1, evt2 }, 10.0, 8);
            var choice2 = engine.ChooseTopic("teller", new[] { evt1, evt2 }, 10.0, 8);

            Assert.Equal(choice1.PickedIndex, choice2.PickedIndex);
            Assert.Equal(choice1.PickedEventId, choice2.PickedEventId);
        }

        [Fact]
        public void ChooseTopic_Chance_CalculatedProportionalToWeight_AndFallbackUniform()
        {
            var (engine, channel, traits, cfg) = CreateEngine();
            traits.Set(new TraitProfile { HeroId = "teller", IsAlive = true, IsLord = true });

            var evt1 = CreateTestEvent("e1", drama: 1, day: 10.0); // drama 1 weight = 0.35
            evt1.KnownBy.Add(new KnownByEntry { HeroId = "teller", Hop = 0 });
            var evt2 = CreateTestEvent("e2", drama: 2, day: 10.0); // drama 2 weight = 0.60
            evt2.KnownBy.Add(new KnownByEntry { HeroId = "teller", Hop = 0 });

            var choice = engine.ChooseTopic("teller", new[] { evt1, evt2 }, 10.0, 8);

            double expectedSum = 0.35 + 0.60;
            double expectedChance = (choice.PickedIndex == 0 ? 0.35 : 0.60) / expectedSum;
            Assert.Equal(expectedChance, choice.Chance, 3);
        }

        [Fact]
        public void ChooseTopic_MutationSafety_DoesNotModifyEvents()
        {
            var (engine, channel, traits, cfg) = CreateEngine();
            traits.Set(new TraitProfile { HeroId = "teller", IsAlive = true, IsLord = true });

            var evt = CreateTestEvent("e1", drama: 3, day: 10.0);
            evt.State.LastPropagatedDay = 8.0;
            evt.KnownBy.Add(new KnownByEntry { HeroId = "teller", Hop = 0, HeardCount = 1 });

            engine.ChooseTopic("teller", new[] { evt }, 10.0, 8);

            Assert.Equal(8.0, evt.State.LastPropagatedDay);
            Assert.Single(evt.KnownBy);
            Assert.Equal(1, evt.KnownBy[0].HeardCount);
        }

        [Fact]
        public void ChooseTopic_PickedEventId_MatchesCandidatesAtPickedIndex()
        {
            var (engine, channel, traits, cfg) = CreateEngine(seed: 12345L);
            traits.Set(new TraitProfile { HeroId = "teller", IsAlive = true, IsLord = true });

            var e1 = CreateTestEvent("e1", drama: 3, day: 10.0);
            e1.KnownBy.Add(new KnownByEntry { HeroId = "teller", Hop = 0 });

            var e2 = CreateTestEvent("e2", drama: 1, day: 10.0);
            e2.KnownBy.Add(new KnownByEntry { HeroId = "teller", Hop = 2 });

            var e3 = CreateTestEvent("e3", drama: 4, day: 10.0);
            e3.KnownBy.Add(new KnownByEntry { HeroId = "teller", Hop = 0 });

            var e4 = CreateTestEvent("e4", drama: 2, day: 10.0);
            e4.KnownBy.Add(new KnownByEntry { HeroId = "teller", Hop = 0, ForgetDay = 5.0, Interest = 0.5 });

            var e5 = CreateTestEvent("e5", drama: 5, day: 10.0);
            e5.KnownBy.Add(new KnownByEntry { HeroId = "teller", Hop = 0 });

            var choice = engine.ChooseTopic("teller", new[] { e1, e2, e3, e4, e5 }, 10.0, 12);

            Assert.Equal(3, choice.Candidates.Count);
            Assert.Equal(2, choice.Exclusions.Count);
            Assert.True(choice.PickedIndex >= 0 && choice.PickedIndex < choice.Candidates.Count);
            Assert.Equal(choice.Candidates[choice.PickedIndex].EventId, choice.PickedEventId);
        }

        [Fact]
        public void ChooseTopic_PickedEventId_IsNeverInExclusions()
        {
            var (engine, channel, traits, cfg) = CreateEngine(seed: 12345L);
            traits.Set(new TraitProfile { HeroId = "teller", IsAlive = true, IsLord = true });

            var e1 = CreateTestEvent("e1", drama: 3, day: 10.0);
            e1.KnownBy.Add(new KnownByEntry { HeroId = "teller", Hop = 0 });

            var e2 = CreateTestEvent("e2", drama: 1, day: 10.0);
            e2.KnownBy.Add(new KnownByEntry { HeroId = "teller", Hop = 2 });

            var e3 = CreateTestEvent("e3", drama: 4, day: 10.0);
            e3.KnownBy.Add(new KnownByEntry { HeroId = "teller", Hop = 0 });

            var e4 = CreateTestEvent("e4", drama: 2, day: 10.0);
            e4.KnownBy.Add(new KnownByEntry { HeroId = "teller", Hop = 0, ForgetDay = 5.0, Interest = 0.5 });

            var e5 = CreateTestEvent("e5", drama: 5, day: 10.0);
            e5.KnownBy.Add(new KnownByEntry { HeroId = "teller", Hop = 0 });

            var choice = engine.ChooseTopic("teller", new[] { e1, e2, e3, e4, e5 }, 10.0, 12);

            Assert.NotNull(choice.PickedEventId);
            Assert.DoesNotContain(choice.PickedEventId, choice.Exclusions.Select(e => e.EventId));
        }

        [Fact]
        public void ChooseTopic_WhenExclusionsComeFirst_PickedIndexDoesNotAddressTheInputList()
        {
            // 描述帳本 L-38 的缺陷條件：
            // 當被排除的事件排在輸入清單的前面（例如 [已過時, 已遺忘, 可講A, 可講B]），
            // Candidates 的索引空間（0..1）與 inputList 的索引空間（0..3）發生位移。
            // 若呼叫端拿 choice.PickedIndex 直接索引 inputList，必然拿到被排除在前面的錯事件。
            var (engine, channel, traits, cfg) = CreateEngine(seed: 9999L);
            traits.Set(new TraitProfile { HeroId = "teller", IsAlive = true, IsLord = true });

            var eOutdated = CreateTestEvent("evt_outdated", drama: 3, day: 10.0);
            eOutdated.KnownBy.Add(new KnownByEntry { HeroId = "teller", Hop = 0, OutdatedDay = 8.0 });

            var eForgotten = CreateTestEvent("evt_forgotten", drama: 3, day: 10.0);
            eForgotten.KnownBy.Add(new KnownByEntry { HeroId = "teller", Hop = 0, ForgetDay = 5.0, Interest = 0.5 });

            var eTellableA = CreateTestEvent("evt_tellable_a", drama: 3, day: 10.0);
            eTellableA.KnownBy.Add(new KnownByEntry { HeroId = "teller", Hop = 0 });

            var eTellableB = CreateTestEvent("evt_tellable_b", drama: 4, day: 10.0);
            eTellableB.KnownBy.Add(new KnownByEntry { HeroId = "teller", Hop = 0 });

            var inputList = new[] { eOutdated, eForgotten, eTellableA, eTellableB };
            var choice = engine.ChooseTopic("teller", inputList, 10.0, 12);

            Assert.Equal(2, choice.Candidates.Count);
            Assert.Equal(2, choice.Exclusions.Count);
            Assert.True(choice.PickedIndex >= 0 && choice.PickedIndex < choice.Candidates.Count);

            // 回歸斷言：PickedIndex 是 Candidates 的索引（0 或 1），
            // 拿它索引 inputList 得到的是 inputList[0] (evt_outdated) 或 inputList[1] (evt_forgotten)，
            // 永遠不等於抽中的 PickedEventId (evt_tellable_a 或 evt_tellable_b)！
            Assert.NotEqual(choice.PickedEventId, inputList[choice.PickedIndex].EventId);
            Assert.Equal(choice.Candidates[choice.PickedIndex].EventId, choice.PickedEventId);
        }

        [Fact]
        public void ChooseTopic_NoExclusions_PickedIndexAddressesInputList()
        {
            var (engine, channel, traits, cfg) = CreateEngine(seed: 42L);
            traits.Set(new TraitProfile { HeroId = "teller", IsAlive = true, IsLord = true });

            var e1 = CreateTestEvent("evt_1", drama: 3, day: 10.0);
            e1.KnownBy.Add(new KnownByEntry { HeroId = "teller", Hop = 0 });

            var e2 = CreateTestEvent("evt_2", drama: 4, day: 10.0);
            e2.KnownBy.Add(new KnownByEntry { HeroId = "teller", Hop = 0 });

            var e3 = CreateTestEvent("evt_3", drama: 5, day: 10.0);
            e3.KnownBy.Add(new KnownByEntry { HeroId = "teller", Hop = 0 });

            var inputList = new[] { e1, e2, e3 };
            var choice = engine.ChooseTopic("teller", inputList, 10.0, 12);

            Assert.Equal(3, choice.Candidates.Count);
            Assert.Empty(choice.Exclusions);
            Assert.True(choice.PickedIndex >= 0);
            Assert.Equal(inputList[choice.PickedIndex].EventId, choice.PickedEventId);
        }

        #endregion

        #region 6. PropagateFromTeller Tests

        [Fact]
        public void PropagateFromTeller_IneligibleTeller_ReturnsNothingAndPreservesLastPropagatedDay()
        {
            var (engine, channel, traits, cfg) = CreateEngine();
            traits.Set(new TraitProfile { HeroId = "dead_hero", IsAlive = false, IsLord = true });

            var evt = CreateTestEvent("e1", drama: 3, day: 10.0);
            evt.State.LastPropagatedDay = 5.0;
            evt.KnownBy.Add(new KnownByEntry { HeroId = "dead_hero", Hop = 0 });

            var outcome = engine.PropagateFromTeller(evt, "dead_hero", 10.0, 8);

            Assert.Same(PropagationOutcome.Nothing, outcome);
            Assert.Equal(5.0, evt.State.LastPropagatedDay);
        }

        [Fact]
        public void PropagateFromTeller_EligibleTeller_ProducesIdenticalOutcomeToPropagateOnce()
        {
            var (engine1, channel1, traits1, cfg1) = CreateEngine(seed: 5555L);
            traits1.Set(new TraitProfile { HeroId = "teller", IsAlive = true, IsLord = true });
            traits1.Set(new TraitProfile { HeroId = "contact", IsAlive = true, IsLord = true });
            channel1.AddLink("teller", "contact", ChannelKind.SameParty);

            var evt1 = CreateTestEvent("e1", drama: 3, day: 10.0);
            evt1.KnownBy.Add(new KnownByEntry { HeroId = "teller", Hop = 0 });

            var (engine2, channel2, traits2, cfg2) = CreateEngine(seed: 5555L);
            traits2.Set(new TraitProfile { HeroId = "teller", IsAlive = true, IsLord = true });
            traits2.Set(new TraitProfile { HeroId = "contact", IsAlive = true, IsLord = true });
            channel2.AddLink("teller", "contact", ChannelKind.SameParty);

            var evt2 = CreateTestEvent("e1", drama: 3, day: 10.0);
            evt2.KnownBy.Add(new KnownByEntry { HeroId = "teller", Hop = 0 });

            var outcome1 = engine1.PropagateOnce(evt1, 10.0, 8);
            var outcome2 = engine2.PropagateFromTeller(evt2, "teller", 10.0, 8);

            Assert.Equal(outcome1.NewKnowers.Count, outcome2.NewKnowers.Count);
            Assert.Equal(outcome1.NewKnowers[0].HeroId, outcome2.NewKnowers[0].HeroId);
            Assert.Equal(outcome1.NewKnowers[0].Hop, outcome2.NewKnowers[0].Hop);
        }

        [Fact]
        public void PropagateFromTeller_PlayerAsTeller_Rejected()
        {
            var (engine, channel, traits, cfg) = CreateEngine(playerHeroId: "player");
            var evt = CreateTestEvent("e1", drama: 3, day: 10.0);
            evt.KnownBy.Add(new KnownByEntry { HeroId = "player", Hop = 0 });

            var outcome = engine.PropagateFromTeller(evt, "player", 10.0, 8);
            Assert.Same(PropagationOutcome.Nothing, outcome);
        }

        [Fact]
        public void PropagateFromTeller_ForgottenTeller_Rejected()
        {
            var (engine, channel, traits, cfg) = CreateEngine();
            traits.Set(new TraitProfile { HeroId = "teller", IsAlive = true, IsLord = true });

            var evt = CreateTestEvent("e1", drama: 3, day: 10.0);
            evt.KnownBy.Add(new KnownByEntry
            {
                HeroId = "teller",
                Hop = 0,
                ForgetDay = 5.0,
                Interest = 0.5
            });

            var outcome = engine.PropagateFromTeller(evt, "teller", 10.0, 8);
            Assert.Same(PropagationOutcome.Nothing, outcome);
        }

        #endregion

        #region 7. 甲1 Rehear Policy Tests

        [Fact]
        public void 甲1_Cell1_SameTeller_Remembered_SameTellerIgnored()
        {
            var (engine, channel, traits, cfg) = CreateEngine();
            traits.Set(new TraitProfile { HeroId = "teller", IsAlive = true, IsLord = true });
            traits.Set(new TraitProfile { HeroId = "knower", IsAlive = true, IsLord = true });
            channel.AddLink("teller", "knower", ChannelKind.SameParty);

            var evt = CreateTestEvent("e1", drama: 3, day: 10.0);
            evt.KnownBy.Add(new KnownByEntry { HeroId = "teller", Hop = 0 });
            var knowerEntry = new KnownByEntry
            {
                HeroId = "knower",
                Hop = 1,
                SourceHeroId = "teller",
                HeardFromIds = new List<string> { "teller" },
                HeardCount = 1,
                LearnedDay = 5.0,
                LastHeardDay = 5.0,
                ForgetDay = 25.0,
                Interest = 0.5
            };
            evt.KnownBy.Add(knowerEntry);

            var outcome = engine.PropagateFromTeller(evt, "teller", 10.0, 8);

            Assert.Single(outcome.Reheard);
            var reheard = outcome.Reheard[0];
            Assert.Equal(RehearKind.SameTellerIgnored, reheard.Kind);
            // Nothing changed on existing entry
            Assert.Equal(1, knowerEntry.HeardCount);
            Assert.Equal(5.0, knowerEntry.LastHeardDay);
            Assert.Equal(25.0, knowerEntry.ForgetDay);
        }

        [Fact]
        public void 甲1_Cell2_DifferentTeller_Remembered_Counted()
        {
            var (engine, channel, traits, cfg) = CreateEngine();
            traits.Set(new TraitProfile { HeroId = "teller2", IsAlive = true, IsLord = true });
            traits.Set(new TraitProfile { HeroId = "knower", IsAlive = true, IsLord = true });
            channel.AddLink("teller2", "knower", ChannelKind.SameParty);

            var evt = CreateTestEvent("e1", drama: 3, day: 10.0);
            evt.KnownBy.Add(new KnownByEntry { HeroId = "teller2", Hop = 0 });
            var knowerEntry = new KnownByEntry
            {
                HeroId = "knower",
                Hop = 2,
                SourceHeroId = "teller1",
                HeardFromIds = new List<string> { "teller1" },
                HeardCount = 1,
                LearnedDay = 5.0,
                LastHeardDay = 5.0,
                ForgetDay = 25.0,
                Interest = 0.8
            };
            evt.KnownBy.Add(knowerEntry);

            var outcome = engine.PropagateFromTeller(evt, "teller2", 10.0, 8);

            Assert.Single(outcome.Reheard);
            var reheard = outcome.Reheard[0];
            Assert.Equal(RehearKind.Counted, reheard.Kind);
            Assert.Equal(2, knowerEntry.HeardCount);
            Assert.Equal(10.0, knowerEntry.LastHeardDay);
            Assert.Contains("teller2", knowerEntry.HeardFromIds!);
            // Adopted version because teller2 has hop 0 -> hop becomes 1
            Assert.Equal(1, knowerEntry.Hop);
            Assert.Equal("teller2", knowerEntry.SourceHeroId);
        }

        [Fact]
        public void 甲1_Cell3_SameTeller_Forgotten_SameTellerRelearned()
        {
            var (engine, channel, traits, cfg) = CreateEngine();
            traits.Set(new TraitProfile { HeroId = "teller", IsAlive = true, IsLord = true });
            traits.Set(new TraitProfile { HeroId = "knower", IsAlive = true, IsLord = true });
            channel.AddLink("teller", "knower", ChannelKind.SameParty);

            var evt = CreateTestEvent("e1", drama: 3, day: 10.0);
            evt.KnownBy.Add(new KnownByEntry { HeroId = "teller", Hop = 0 });
            var knowerEntry = new KnownByEntry
            {
                HeroId = "knower",
                Hop = 1,
                SourceHeroId = "teller",
                HeardFromIds = new List<string> { "teller" },
                HeardCount = 1,
                LearnedDay = 2.0,
                LastHeardDay = 2.0,
                ForgetDay = 6.0, // Forgotten at day 10.0
                Interest = 0.5
            };
            evt.KnownBy.Add(knowerEntry);

            var outcome = engine.PropagateFromTeller(evt, "teller", 10.0, 8);

            Assert.Single(outcome.Reheard);
            var reheard = outcome.Reheard[0];
            Assert.Equal(RehearKind.SameTellerRelearned, reheard.Kind);
            // HeardCount not incremented
            Assert.Equal(1, knowerEntry.HeardCount);
            // LastHeardDay updated to 10.0
            Assert.Equal(10.0, knowerEntry.LastHeardDay);
            // Version adopted
            Assert.True(reheard.Rule.AdoptVersion);
        }

        [Fact]
        public void 甲1_Cell4_DifferentTeller_Forgotten_Counted()
        {
            var (engine, channel, traits, cfg) = CreateEngine();
            traits.Set(new TraitProfile { HeroId = "teller2", IsAlive = true, IsLord = true });
            traits.Set(new TraitProfile { HeroId = "knower", IsAlive = true, IsLord = true });
            channel.AddLink("teller2", "knower", ChannelKind.SameParty);

            var evt = CreateTestEvent("e1", drama: 3, day: 10.0);
            evt.KnownBy.Add(new KnownByEntry { HeroId = "teller2", Hop = 0 });
            var knowerEntry = new KnownByEntry
            {
                HeroId = "knower",
                Hop = 1,
                SourceHeroId = "teller1",
                HeardFromIds = new List<string> { "teller1" },
                HeardCount = 1,
                LearnedDay = 2.0,
                LastHeardDay = 2.0,
                ForgetDay = 6.0, // Forgotten at day 10.0
                Interest = 0.5
            };
            evt.KnownBy.Add(knowerEntry);

            var outcome = engine.PropagateFromTeller(evt, "teller2", 10.0, 8);

            Assert.Single(outcome.Reheard);
            var reheard = outcome.Reheard[0];
            Assert.Equal(RehearKind.Counted, reheard.Kind);
            // HeardCount incremented
            Assert.Equal(2, knowerEntry.HeardCount);
            Assert.Equal(10.0, knowerEntry.LastHeardDay);
            Assert.Contains("teller2", knowerEntry.HeardFromIds!);
        }

        [Fact]
        public void 甲1_HeardFromIds_Null_FallsBackToSourceHeroId()
        {
            var entry = new KnownByEntry
            {
                HeroId = "knower",
                SourceHeroId = "original_teller",
                HeardFromIds = null
            };

            Assert.True(HeardFrom.ToldBefore(entry, "original_teller"));
            Assert.False(HeardFrom.ToldBefore(entry, "new_teller"));
        }

        [Fact]
        public void 甲1_Hop0Entry_FirstTellerIsCounted()
        {
            var (engine, channel, traits, cfg) = CreateEngine();
            traits.Set(new TraitProfile { HeroId = "teller", IsAlive = true, IsLord = true });
            traits.Set(new TraitProfile { HeroId = "participant", IsAlive = true, IsLord = true });
            channel.AddLink("teller", "participant", ChannelKind.SameParty);

            var evt = CreateTestEvent("e1", drama: 3, day: 10.0);
            evt.KnownBy.Add(new KnownByEntry { HeroId = "teller", Hop = 0 });
            // Hop 0 participant entry has no SourceHeroId
            var partEntry = new KnownByEntry
            {
                HeroId = "participant",
                Hop = 0,
                SourceHeroId = null,
                HeardFromIds = null,
                HeardCount = 1,
                Interest = 0.8
            };
            evt.KnownBy.Add(partEntry);

            var outcome = engine.PropagateFromTeller(evt, "teller", 10.0, 8);

            Assert.Single(outcome.Reheard);
            var reheard = outcome.Reheard[0];
            Assert.Equal(RehearKind.Counted, reheard.Kind);
            Assert.Equal(2, partEntry.HeardCount);
            Assert.NotNull(partEntry.HeardFromIds);
            Assert.Contains("teller", partEntry.HeardFromIds!);
        }

        [Fact]
        public void 甲1_MaterializeBeforeVersionAdoption_PreservesOriginalSource()
        {
            var entry = new KnownByEntry
            {
                HeroId = "knower",
                Hop = 2,
                SourceHeroId = "teller1",
                HeardFromIds = null
            };

            // Before Materialize, HeardFromIds is null
            Assert.Null(entry.HeardFromIds);

            HeardFrom.Materialize(entry);

            // Materialize creates HeardFromIds containing teller1
            Assert.NotNull(entry.HeardFromIds);
            Assert.Single(entry.HeardFromIds!);
            Assert.Equal("teller1", entry.HeardFromIds![0]);

            // If version adoption overwrites SourceHeroId to teller2, teller1 is still in HeardFromIds
            entry.SourceHeroId = "teller2";
            Assert.Contains("teller1", entry.HeardFromIds);
        }

        [Fact]
        public void 甲1_NewKnower_InitializesHeardFromIdsWithTeller()
        {
            var (engine, channel, traits, cfg) = CreateEngine();
            traits.Set(new TraitProfile { HeroId = "teller", IsAlive = true, IsLord = true });
            traits.Set(new TraitProfile { HeroId = "new_hero", IsAlive = true, IsLord = true });
            channel.AddLink("teller", "new_hero", ChannelKind.SameParty);

            var evt = CreateTestEvent("e1", drama: 3, day: 10.0);
            evt.KnownBy.Add(new KnownByEntry { HeroId = "teller", Hop = 0 });

            var outcome = engine.PropagateFromTeller(evt, "teller", 10.0, 8);

            Assert.Single(outcome.NewKnowers);
            var newKnower = outcome.NewKnowers[0];
            Assert.NotNull(newKnower.HeardFromIds);
            Assert.Single(newKnower.HeardFromIds!);
            Assert.Equal("teller", newKnower.HeardFromIds![0]);
        }

        #endregion

        #region 8. TellerRing Tests

        [Fact]
        public void TellerRing_Add_IgnoresDuplicatesAndNull()
        {
            var ring = new TellerRing();
            ring.Add("h1");
            ring.Add("h1");
            ring.Add("");
            ring.Add(null!);
            Assert.Equal(1, ring.Count);
        }

        [Fact]
        public void TellerRing_Remove_CursorAdjustment()
        {
            var ring = new TellerRing();
            ring.Add("h1");
            ring.Add("h2");
            ring.Add("h3");
            ring.Add("h4");
            ring.Cursor = 2; // Pointing to h3 (index 2)

            // Removing h1 (index 0, before cursor) should decrement cursor to 1
            Assert.True(ring.Remove("h1"));
            Assert.Equal(1, ring.Cursor);
            Assert.Equal(3, ring.Count);

            // Removing h4 (index 2, after cursor index 1) should keep cursor at 1
            Assert.True(ring.Remove("h4"));
            Assert.Equal(1, ring.Cursor);
            Assert.Equal(2, ring.Count);
        }

        [Fact]
        public void TellerRing_TakeBatch_WrapsAroundAndAdvancesCursor()
        {
            var ring = new TellerRing();
            ring.Add("h1");
            ring.Add("h2");
            ring.Add("h3");

            // Take 2 from cursor 0 -> [h1, h2], cursor becomes 2
            var batch1 = ring.TakeBatch(2);
            Assert.Equal(new[] { "h1", "h2" }, batch1);
            Assert.Equal(2, ring.Cursor);

            // Take 2 from cursor 2 -> wraps around: [h3, h1], cursor becomes (2+2)%3 = 1
            var batch2 = ring.TakeBatch(2);
            Assert.Equal(new[] { "h3", "h1" }, batch2);
            Assert.Equal(1, ring.Cursor);
        }

        [Fact]
        public void TellerRing_Cursor_ModuloAndNegativeClamping()
        {
            var ring = new TellerRing();
            ring.Add("h1");
            ring.Add("h2");
            ring.Add("h3");

            ring.Cursor = 5;
            Assert.Equal(2, ring.Cursor); // 5 % 3 = 2

            ring.Cursor = -1;
            Assert.Equal(0, ring.Cursor); // Negative clamped to 0
        }

        [Fact]
        public void TellerRing_Rebuild_PreservesOrderAndDeduplicates()
        {
            var ring = new TellerRing();
            ring.Add("old1");
            ring.Add("old2");
            ring.Cursor = 1;

            ring.Rebuild(new[] { "h1", "h2", "h1", "h3" });
            Assert.Equal(3, ring.Count);
            Assert.Equal(new[] { "h1", "h2", "h3" }, ring.Ids);
            Assert.Equal(1, ring.Cursor); // 1 % 3 = 1 preserved
        }

        [Fact]
        public void TellerRing_CurrentId_IsTheHeroTheCursorPointsAt()
        {
            var ring = new TellerRing();
            Assert.Null(ring.CurrentId); // 空的輪沒有「下一個」

            ring.Add("h1");
            ring.Add("h2");
            ring.Add("h3");
            Assert.Equal("h1", ring.CurrentId);

            ring.TakeBatch(2); // 游標走到 2
            Assert.Equal("h3", ring.CurrentId);
        }

        [Fact]
        public void TellerRing_SetCursorTo_FindsTheHeroAfterRebuild()
        {
            var ring = new TellerRing();
            ring.Rebuild(new[] { "h1", "h2", "h3" });
            ring.TakeBatch(2);
            Assert.Equal("h3", ring.CurrentId);

            // 讀檔：成員與順序都變了，同一個位置 2 會指到別人（h1），要靠人名對回來
            ring.Rebuild(new[] { "h9", "h3", "h1", "h8", "h2" });
            Assert.Equal("h1", ring.Ids[2]);

            Assert.True(ring.SetCursorTo("h3"));
            Assert.Equal(1, ring.Cursor);
            Assert.Equal("h3", ring.CurrentId);
        }

        [Fact]
        public void TellerRing_SetCursorTo_LeavesCursorAloneWhenTheHeroIsGone()
        {
            var ring = new TellerRing();
            ring.Rebuild(new[] { "h1", "h2", "h3" });
            ring.Cursor = 2;

            Assert.False(ring.SetCursorTo("someone-who-left"));
            Assert.False(ring.SetCursorTo(""));
            Assert.Equal(2, ring.Cursor); // 沒動，呼叫端才能退回位置值

            var empty = new TellerRing();
            Assert.False(empty.SetCursorTo("h1"));
        }

        #endregion

        #region 9. Stranger Floor Tests

        [Fact]
        public void Interest_Drama5_Stranger_UsesDramaFloor()
        {
            var cfg = new VividWorldConfig();
            var knower = new InterestHeroFacts("k", "clan_k");
            var part = new InterestParticipant("role1", "p", new InterestHeroFacts("p", "clan_p"), null);
            var res = InterestCalculator.Compute(knower, new[] { part }, 5, cfg.Memory);

            Assert.Equal(0.40, res.Interest, 2);
            Assert.Equal("drama floor", res.BestSource);
        }

        [Fact]
        public void Interest_Drama1_Stranger_UsesOtherFloor()
        {
            var cfg = new VividWorldConfig();
            var knower = new InterestHeroFacts("k", "clan_k");
            var part = new InterestParticipant("role1", "p", new InterestHeroFacts("p", "clan_p"), null);
            var res = InterestCalculator.Compute(knower, new[] { part }, 1, cfg.Memory);

            Assert.Equal(0.05, res.Interest, 2);
            Assert.Equal("other floor", res.BestSource);
        }

        [Fact]
        public void Interest_KinFloor_HigherThanDrama5_StaysKinFloor()
        {
            var cfg = new VividWorldConfig();
            var knower = new InterestHeroFacts("k", "clan_k");
            // Kin relationship gives kin floor (0.60), higher than drama 5 floor (0.40)
            var kinPart = new InterestParticipant("role1", "p", new InterestHeroFacts("p", "clan_p", spouseId: "k"), null);
            var res = InterestCalculator.Compute(knower, new[] { kinPart }, 5, cfg.Memory);

            Assert.Equal(0.60, res.Interest, 2);
            Assert.Equal("kin floor", res.BestSource);
        }

        [Fact]
        public void Interest_ExistingOverloads_Unchanged()
        {
            var cfg = new VividWorldConfig();
            var knower = new InterestHeroFacts("k", "clan_k");
            var part = new InterestParticipant("role1", "p", new InterestHeroFacts("p", "clan_p"), null);
            // Existing overload without drama uses Other (0.05)
            var res = InterestCalculator.Compute(knower, new[] { part }, cfg.Memory);

            Assert.Equal(0.05, res.Interest, 2);
            Assert.Equal("other floor", res.BestSource);
        }

        #endregion

        #region 10. TellerLogFormatter Tests

        [Fact]
        public void TellerLogFormatter_FormatTellerTurn_TopicsAndExclusionsWithMore()
        {
            var candidates = new List<TopicCandidate>();
            for (int i = 1; i <= 8; i++)
            {
                candidates.Add(new TopicCandidate($"evt_{i}", 3, 1.0, 0.9, 0.9));
            }
            var exclusions = new List<TopicExclusion>();
            for (int i = 1; i <= 7; i++)
            {
                exclusions.Add(new TopicExclusion($"ex_{i}", TellReason.Forgotten));
            }

            var choice = new TopicChoice("teller_1", candidates, exclusions, 0, "evt_1", 0.125);
            var outcome = new PropagationOutcome(false, null, Array.Empty<KnownByEntry>());

            string line = TellerLogFormatter.FormatTellerTurn("teller_1", "evt_1", choice, outcome, inactiveCount: 2);

            Assert.Contains("(+2 more)", line); // 8 - 6 = 2
            Assert.Contains("(+2 more)", line); // 7 - 5 = 2
            Assert.Contains("inactive 2", line);
            Assert.Contains("Teller teller_1 told evt_1", line);
        }

        [Fact]
        public void TellerLogFormatter_FormatTellerTurn_ZeroExclusions()
        {
            var candidates = new List<TopicCandidate> { new TopicCandidate("evt_1", 3, 1.0, 1.0, 1.0) };
            var choice = new TopicChoice("teller_1", candidates, new List<TopicExclusion>(), 0, "evt_1", 1.0);
            var outcome = new PropagationOutcome(false, null, Array.Empty<KnownByEntry>());

            string line = TellerLogFormatter.FormatTellerTurn("teller_1", "evt_1", choice, outcome, inactiveCount: 0);
            Assert.Contains("; excluded 0;", line);
        }

        [Fact]
        public void TellerLogFormatter_SkippedAndLeftRing_FormatsExpected()
        {
            string skipped = TellerLogFormatter.FormatSkippedNotEligible("h1");
            Assert.Equal("Teller h1 skipped: not eligible now", skipped);

            string dead = TellerLogFormatter.FormatLeftRingHeroNotFoundOrDead("h2");
            Assert.Equal("Teller h2 left the ring: hero not found or dead", dead);

            var exclusions = new List<TopicExclusion>
            {
                new TopicExclusion("e1", TellReason.Forgotten),
                new TopicExclusion("e2", TellReason.AtMaxHop)
            };
            string noTopic = TellerLogFormatter.FormatLeftRingNoTellableTopic("h3", 2, exclusions, 1);
            Assert.Contains("Teller h3 left the ring: no tellable topic (2 known - excluded 2:", noTopic);
            Assert.Contains("forgotten 1", noTopic);
            Assert.Contains("at max hop 1", noTopic);
            Assert.Contains("inactive 1)", noTopic);
        }

        [Fact]
        public void TellerLogFormatter_SameTeller_IgnoredAndRelearned()
        {
            string ignored = TellerLogFormatter.FormatSameTellerIgnored("knower", "teller", "evt_1");
            Assert.Equal("Memory: knower re-heard from teller again (same teller, not counted) evt_1", ignored);

            string relearnedHow = TellerLogFormatter.FormatSameTellerRelearnedHow("teller", 2, 1, "better hop");
            Assert.Equal("re-heard from teller again, adopted version hop 2->1 (better hop; same teller, not counted)", relearnedHow);
        }

        [Fact]
        public void TellerLogFormatter_WorldStatusLines_FormatsExpected()
        {
            string ringLine = TellerLogFormatter.FormatWorldStatusTellerRing(
                120, 8, 24, new[] { 1, 2, 3, 4, 5 }, 2, 1);
            Assert.Equal("- Teller ring: 120 tellers, 8 per hour; this session turns 24, told by drama 1:1 2:2 3:3 4:4 5:5, skipped not eligible 2, left ring 1", ringLine);

            string reheardLine = TellerLogFormatter.FormatWorldStatusReheard(10, 5, 2);
            Assert.Equal("- Re-heard this session: counted 10, same teller ignored 5, same teller re-learned 2", reheardLine);

            var stats = new (int drama, int eventCount, int hopGte1Total, int hopGte1Remembered)[]
            {
                (1, 2, 4, 2),
                (2, 0, 0, 0),
                (3, 1, 3, 3),
                (4, 0, 0, 0),
                (5, 5, 25, 20)
            };
            string spreadLine = TellerLogFormatter.FormatWorldStatusSpreadByDrama(stats);
            Assert.Contains("d1 2.0 / 1.0 (2 events)", spreadLine);
            Assert.Contains("d2 - (0 events)", spreadLine);
            Assert.Contains("d3 3.0 / 3.0 (1 events)", spreadLine);
            Assert.Contains("d4 - (0 events)", spreadLine);
            Assert.Contains("d5 5.0 / 4.0 (5 events)", spreadLine);
        }

        [Fact]
        public void TellerLogFormatter_FormatTopicsIfSpokeNow_FormatsExpected()
        {
            // Empty candidates
            var emptyChoice = new TopicChoice("h1", new List<TopicCandidate>(), new List<TopicExclusion>(), -1, null, 0.0);
            string noneLine = TellerLogFormatter.FormatTopicsIfSpokeNow(emptyChoice, true, 2);
            Assert.Contains("topics if he spoke now (in teller ring: yes): none; excluded 0; inactive 2", noneLine);

            // With candidates
            var candidates = new List<TopicCandidate>
            {
                new TopicCandidate("e1", 3, 1.0, 0.9, 0.9)
            };
            var choice = new TopicChoice("h1", candidates, new List<TopicExclusion>(), 0, "e1", 1.0);
            string withCand = TellerLogFormatter.FormatTopicsIfSpokeNow(choice, false, 0);
            Assert.Contains("topics if he spoke now (in teller ring: no): e1 100.0% (drama 3, tell 1.00, fresh 0.90); excluded 0; inactive 0", withCand);
        }

        #endregion
    }
}
