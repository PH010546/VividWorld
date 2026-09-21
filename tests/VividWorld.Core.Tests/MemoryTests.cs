using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using VividWorld.Core.Channels;
using VividWorld.Core.Config;
using VividWorld.Core.Diagnostics;
using VividWorld.Core.Events;
using VividWorld.Core.Memory;
using VividWorld.Core.Persistence;
using VividWorld.Core.Rumors;
using VividWorld.Core.Tests.Fakes;
using VividWorld.Core.Util;
using Xunit;

namespace VividWorld.Core.Tests
{
    public class MemoryTests
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

        #region 1. Config Tests

        [Fact]
        public void MemoryConfig_Defaults_MatchDocumentedValues()
        {
            var config = new VividWorldConfig();
            var m = config.Memory;

            Assert.True(m.Enabled);
            Assert.Equal(60.0, m.BaseDays);
            Assert.Equal(4, m.DramaReference);
            Assert.Equal(1.0, m.MinDays);
            Assert.Equal(100, m.RelationFullAt);
            Assert.Equal(1.5, m.ReinforceGrowth);
            Assert.Equal(3.0, m.ReinforceMaxMultiplier);
            Assert.Equal(0.3, m.TellFactorMin);
            Assert.Equal(0.6, m.UpgradeMinInterest);

            Assert.NotNull(m.InterestFloors);
            Assert.Equal(1.0, m.InterestFloors.Participant);
            Assert.Equal(0.6, m.InterestFloors.Kin);
            Assert.Equal(0.4, m.InterestFloors.SameClan);
            Assert.Equal(0.05, m.InterestFloors.Other);
        }

        [Fact]
        public void MemoryConfig_Normalize_ClampsBoundsAndAddsClampNotices()
        {
            var config = new VividWorldConfig();
            var m = config.Memory;

            // Set extreme values
            m.BaseDays = -10.0;
            m.DramaReference = 0;
            m.MinDays = -5.0;
            m.ReinforceGrowth = 0.5;
            m.ReinforceMaxMultiplier = 0.5;
            m.TellFactorMin = -0.5;
            m.RelationFullAt = 0;
            m.UpgradeMinInterest = 1.5;

            m.InterestFloors.Participant = 2.0;
            m.InterestFloors.Kin = -0.5;
            m.InterestFloors.SameClan = 1.2;
            m.InterestFloors.Other = -0.1;

            var notices = new List<ClampNotice>();
            config.Normalize(notices);

            Assert.Equal(0.0, m.BaseDays);
            Assert.Equal(1, m.DramaReference);
            Assert.Equal(0.0, m.MinDays);
            Assert.Equal(1.0, m.ReinforceGrowth);
            Assert.Equal(1.0, m.ReinforceMaxMultiplier);
            Assert.Equal(0.0, m.TellFactorMin);
            Assert.Equal(1, m.RelationFullAt);
            Assert.Equal(1.0, m.UpgradeMinInterest);

            Assert.Equal(1.0, m.InterestFloors.Participant);
            Assert.Equal(0.0, m.InterestFloors.Kin);
            Assert.Equal(1.0, m.InterestFloors.SameClan);
            Assert.Equal(0.0, m.InterestFloors.Other);

            Assert.NotEmpty(notices);
            Assert.Contains(notices, n => n.Key == "memory.baseDays");
            Assert.Contains(notices, n => n.Key == "memory.dramaReference");
            Assert.Contains(notices, n => n.Key == "memory.minDays");
            Assert.Contains(notices, n => n.Key == "memory.reinforceGrowth");
            Assert.Contains(notices, n => n.Key == "memory.reinforceMaxMultiplier");
            Assert.Contains(notices, n => n.Key == "memory.tellFactorMin");
            Assert.Contains(notices, n => n.Key == "memory.relationFullAt");
            Assert.Contains(notices, n => n.Key == "memory.upgradeMinInterest");
            Assert.Contains(notices, n => n.Key == "memory.interestFloors.participant");
            Assert.Contains(notices, n => n.Key == "memory.interestFloors.kin");
            Assert.Contains(notices, n => n.Key == "memory.interestFloors.sameClan");
            Assert.Contains(notices, n => n.Key == "memory.interestFloors.other");
        }

        [Fact]
        public void MemoryConfig_Merge_AddsMissingKeysWhileKeepingExisting()
        {
            string oldJson = "{\n  \"configVersion\": 1,\n  \"enabled\": true,\n  \"logLevel\": \"Debug\"\n}";
            var oldObj = JObject.Parse(oldJson);
            var canonObj = JObject.Parse(VividJson.Write(new VividWorldConfig()));

            var result = ConfigMerge.AddMissingKeys(oldObj, canonObj);

            Assert.Contains("memory.enabled", result.AddedPaths);
            Assert.Contains("memory.baseDays", result.AddedPaths);
            var memoryToken = result.Merged["memory"];
            Assert.NotNull(memoryToken);
            Assert.Equal(true, memoryToken!["enabled"]?.Value<bool>());
            Assert.Equal(60.0, memoryToken!["baseDays"]?.Value<double>());
            Assert.Equal("Debug", result.Merged["logLevel"]?.Value<string>());
        }

        [Fact]
        public void MemoryConfig_CalendarScaling_ScalesBaseDaysOnly()
        {
            var config = new VividWorldConfig();
            config.Normalize();

            CalendarScaling.Apply(config, 28.0); // 1/3 scale

            Assert.Equal(20.0, config.Memory.BaseDays, 3);
            Assert.Equal(1.0, config.Memory.MinDays, 3); // unscaled
        }

        #endregion

        #region 2. Serialization Tests

        [Fact]
        public void Serialization_LegacyJsonWithoutMemoryFields_DeserializesToNull()
        {
            string legacyJson = "{\"heroId\":\"lord_1\",\"hop\":1,\"learnedDay\":12.0,\"sourceHeroId\":\"witness\"}";
            var entry = JsonConvert.DeserializeObject<KnownByEntry>(legacyJson);

            Assert.NotNull(entry);
            Assert.Equal("lord_1", entry!.HeroId);
            Assert.Equal(1, entry.Hop);
            Assert.Equal(12.0, entry.LearnedDay);
            Assert.Null(entry.Interest);
            Assert.Null(entry.InterestSource);
            Assert.Null(entry.ForgetDay);
            Assert.Null(entry.LastHeardDay);
            Assert.Null(entry.HeardCount);
        }

        [Fact]
        public void Serialization_RoundtripWithMemoryValues_AndPreservesExtra()
        {
            var entry = new KnownByEntry
            {
                HeroId = "lord_test",
                Hop = 2,
                LearnedDay = 15.5,
                Interest = 0.75,
                InterestSource = "relation +60",
                ForgetDay = 45.5,
                LastHeardDay = 20.0,
                HeardCount = 2
            };
            entry.Extra["customFutureField"] = "preservedValue";

            string json = JsonConvert.SerializeObject(entry);
            var deserialized = JsonConvert.DeserializeObject<KnownByEntry>(json);

            Assert.NotNull(deserialized);
            Assert.Equal("lord_test", deserialized!.HeroId);
            Assert.Equal(0.75, deserialized.Interest);
            Assert.Equal("relation +60", deserialized.InterestSource);
            Assert.Equal(45.5, deserialized.ForgetDay);
            Assert.Equal(20.0, deserialized.LastHeardDay);
            Assert.Equal(2, deserialized.HeardCount);

            Assert.True(deserialized.Extra.ContainsKey("customFutureField"));
            Assert.Equal("preservedValue", deserialized.Extra["customFutureField"]?.ToString());
        }

        #endregion

        #region 3. Interest Tests

        [Fact]
        public void Interest_SymmetricPersonalRelation_ProducesIdenticalInterest()
        {
            var cfg = new MemoryConfig { RelationFullAt = 80 };
            var knower = new InterestHeroFacts("knower", "clan_k");

            var partPos = new InterestParticipant("victim", "hero_pos", new InterestHeroFacts("hero_pos", "clan_p"), 80);
            var partNeg = new InterestParticipant("victim", "hero_neg", new InterestHeroFacts("hero_neg", "clan_n"), -80);

            var resPos = InterestCalculator.Compute(knower, new[] { partPos }, cfg);
            var resNeg = InterestCalculator.Compute(knower, new[] { partNeg }, cfg);

            Assert.Equal(1.0, resPos.Interest);
            Assert.Equal("relation +80", resPos.BestSource);

            Assert.Equal(1.0, resNeg.Interest);
            Assert.Equal("relation -80", resNeg.BestSource);
        }

        [Fact]
        public void Interest_RelationFullAtClampedToOne()
        {
            var cfg = new MemoryConfig { RelationFullAt = 80 };
            var knower = new InterestHeroFacts("knower", "clan_k");
            var part = new InterestParticipant("victim", "hero_100", new InterestHeroFacts("hero_100", "clan_p"), 100);

            var res = InterestCalculator.Compute(knower, new[] { part }, cfg);

            Assert.Equal(1.0, res.Interest);
            Assert.Equal("relation +100", res.BestSource);
        }

        [Fact]
        public void Interest_ListenerIsParticipant_YieldsOnePointZero()
        {
            var cfg = new MemoryConfig();
            var knower = new InterestHeroFacts("lord_a", "clan_a");
            var part = new InterestParticipant("aggressor", "lord_a", knower, null);

            var res = InterestCalculator.Compute(knower, new[] { part }, cfg);

            Assert.Equal(1.0, res.Interest);
            Assert.Equal("participant floor", res.BestSource);
        }

        [Fact]
        public void Interest_KinDirections_ParentChildSibling()
        {
            var cfg = new MemoryConfig();

            // 1. Participant is parent of Knower
            var knowerChild = new InterestHeroFacts("child", "clan_k", fatherId: "parent_p");
            var partParent = new InterestParticipant("victim", "parent_p", new InterestHeroFacts("parent_p", "clan_p"), null);
            var res1 = InterestCalculator.Compute(knowerChild, new[] { partParent }, cfg);
            Assert.Equal(0.6, res1.Interest);
            Assert.Equal("kin floor", res1.BestSource);

            // 2. Knower is parent of Participant
            var knowerParent = new InterestHeroFacts("parent_k", "clan_k");
            var partChild = new InterestParticipant("victim", "child_p", new InterestHeroFacts("child_p", "clan_p", motherId: "parent_k"), null);
            var res2 = InterestCalculator.Compute(knowerParent, new[] { partChild }, cfg);
            Assert.Equal(0.6, res2.Interest);
            Assert.Equal("kin floor", res2.BestSource);

            // 3. Knower and Participant are siblings
            var knowerSib = new InterestHeroFacts("sib_1", "clan_k", siblingIds: new[] { "sib_2" });
            var partSib = new InterestParticipant("victim", "sib_2", new InterestHeroFacts("sib_2", "clan_p"), null);
            var res3 = InterestCalculator.Compute(knowerSib, new[] { partSib }, cfg);
            Assert.Equal(0.6, res3.Interest);
            Assert.Equal("kin floor", res3.BestSource);
        }

        [Fact]
        public void Interest_Spouse_YieldsKinFloor()
        {
            var cfg = new MemoryConfig();
            var knower = new InterestHeroFacts("husband", "clan_h", spouseId: "wife");
            var part = new InterestParticipant("victim", "wife", new InterestHeroFacts("wife", "clan_w"), null);

            var res = InterestCalculator.Compute(knower, new[] { part }, cfg);

            Assert.Equal(0.6, res.Interest);
            Assert.Equal("kin floor", res.BestSource);
        }

        [Fact]
        public void Interest_SameClan_YieldsSameClanFloor()
        {
            var cfg = new MemoryConfig();
            var knower = new InterestHeroFacts("noble_1", "clan_fen_derngil");
            var part = new InterestParticipant("victim", "noble_2", new InterestHeroFacts("noble_2", "clan_fen_derngil"), 0);

            var res = InterestCalculator.Compute(knower, new[] { part }, cfg);

            Assert.Equal(0.4, res.Interest);
            Assert.Equal("sameClan floor", res.BestSource);
        }

        [Fact]
        public void Interest_RelationHigherThanFloor_UsesRelationSource()
        {
            var cfg = new MemoryConfig { RelationFullAt = 100 };
            var knower = new InterestHeroFacts("noble_1", "clan_fen_derngil");
            // Clan floor is 0.4. Relation +60 gives 0.6 > 0.4.
            var part = new InterestParticipant("victim", "noble_2", new InterestHeroFacts("noble_2", "clan_fen_derngil"), 60);

            var res = InterestCalculator.Compute(knower, new[] { part }, cfg);

            Assert.Equal(0.6, res.Interest, 3);
            Assert.Equal("relation +60", res.BestSource);
        }

        [Fact]
        public void Interest_MultipleParticipants_PicksHighest()
        {
            var cfg = new MemoryConfig();
            var knower = new InterestHeroFacts("noble_1", "clan_k");

            var part1 = new InterestParticipant("killer", "noble_other", new InterestHeroFacts("noble_other", "clan_o"), 0); // other floor 0.05
            var part2 = new InterestParticipant("victim", "noble_kin", new InterestHeroFacts("noble_kin", "clan_o", fatherId: "noble_1"), null); // kin floor 0.6

            var res = InterestCalculator.Compute(knower, new[] { part1, part2 }, cfg);

            Assert.Equal(0.6, res.Interest);
            Assert.Equal("victim", res.BestRole);
            Assert.Equal("noble_kin", res.BestHeroId);
            Assert.Equal("kin floor", res.BestSource);
            Assert.Equal(2, res.ParticipantDetails.Count);
        }

        [Fact]
        public void Interest_RoleOrdinalTieBreaker()
        {
            var cfg = new MemoryConfig();
            var knower = new InterestHeroFacts("knower", "clan_k");

            // Both give same clan floor 0.4
            var part1 = new InterestParticipant("firstRole", "hero_1", new InterestHeroFacts("hero_1", "clan_k"), null);
            var part2 = new InterestParticipant("secondRole", "hero_2", new InterestHeroFacts("hero_2", "clan_k"), null);

            var res = InterestCalculator.Compute(knower, new[] { part1, part2 }, cfg);

            Assert.Equal(0.4, res.Interest);
            Assert.Equal("firstRole", res.BestRole);
            Assert.Equal("hero_1", res.BestHeroId);
        }

        [Fact]
        public void Interest_NullRelation_FallsBackToFloors()
        {
            var cfg = new MemoryConfig();
            var knower = new InterestHeroFacts("knower", "clan_k");
            var part = new InterestParticipant("victim", "hero_o", new InterestHeroFacts("hero_o", "clan_o"), null);

            var res = InterestCalculator.Compute(knower, new[] { part }, cfg);

            Assert.Equal(0.05, res.Interest);
            Assert.Equal("other floor", res.BestSource);
        }

        [Fact]
        public void Interest_KnowerNotFound_YieldsOtherFloor()
        {
            var cfg = new MemoryConfig();
            var part = new InterestParticipant("victim", "hero_o", new InterestHeroFacts("hero_o", "clan_o"), 50);

            var res = InterestCalculator.Compute(null, new[] { part }, cfg);

            Assert.False(res.KnowerFound);
            Assert.Equal(0.05, res.Interest);
            Assert.Equal("other floor", res.BestSource);
        }

        [Fact]
        public void Interest_InterestSourceStrings_MatchExactFormat()
        {
            var cfg = new MemoryConfig { RelationFullAt = 100 };
            var knower = new InterestHeroFacts("k", "clan_k");

            // 1. Participant floor
            var r1 = InterestCalculator.Compute(knower, new[] { new InterestParticipant("r", "k", knower, null) }, cfg);
            Assert.Equal("participant floor", r1.BestSource);

            // 2. Kin floor
            var r2 = InterestCalculator.Compute(knower, new[] { new InterestParticipant("r", "p", new InterestHeroFacts("p", "clan_p", spouseId: "k"), null) }, cfg);
            Assert.Equal("kin floor", r2.BestSource);

            // 3. SameClan floor
            var r3 = InterestCalculator.Compute(knower, new[] { new InterestParticipant("r", "p", new InterestHeroFacts("p", "clan_k"), null) }, cfg);
            Assert.Equal("sameClan floor", r3.BestSource);

            // 4. Other floor
            var r4 = InterestCalculator.Compute(knower, new[] { new InterestParticipant("r", "p", new InterestHeroFacts("p", "clan_p"), null) }, cfg);
            Assert.Equal("other floor", r4.BestSource);

            // 5. Relation positive
            var r5 = InterestCalculator.Compute(knower, new[] { new InterestParticipant("r", "p", new InterestHeroFacts("p", "clan_p"), 50) }, cfg);
            Assert.Equal("relation +50", r5.BestSource);

            // 6. Relation negative
            var r6 = InterestCalculator.Compute(knower, new[] { new InterestParticipant("r", "p", new InterestHeroFacts("p", "clan_p"), -30) }, cfg);
            Assert.Equal("relation -30", r6.BestSource);
        }

        #endregion

        #region 4. Memory Span Tests

        [Fact]
        public void MemorySpan_Formula_CalculatesExpectedDays()
        {
            var cfg = new MemoryConfig
            {
                BaseDays = 30.0,
                DramaReference = 3,
                MinDays = 3.0
            };

            // interest 0.5, drama 3, dramaRef 3, baseDays 30, heardCount 0 -> reinforce 1.0
            // days = 0.5 * (3/3) * 30.0 * 1.0 = 15.0
            var res = MemorySpan.Compute(0.5, 3, 10.0, 0, cfg);

            Assert.Equal(15.0, res.Days);
            Assert.Equal(25.0, res.ForgetDay);
            Assert.False(res.RaisedToMin);
            Assert.False(res.KeptOld);
            Assert.Equal(1.0, res.Reinforce);
        }

        [Fact]
        public void MemorySpan_DramaWeight_ScalesSpan()
        {
            var cfg = new MemoryConfig
            {
                BaseDays = 30.0,
                DramaReference = 3,
                MinDays = 1.0
            };

            // drama 1: 0.6 * (1/3) * 30 = 6.0
            var res1 = MemorySpan.Compute(0.6, 1, 0.0, 0, cfg);
            Assert.Equal(6.0, res1.Days, 3);

            // drama 5: 0.6 * (5/3) * 30 = 30.0
            var res5 = MemorySpan.Compute(0.6, 5, 0.0, 0, cfg);
            Assert.Equal(30.0, res5.Days, 3);
        }

        [Fact]
        public void MemorySpan_RaisedToMin_WhenBelowMinDays()
        {
            var cfg = new MemoryConfig
            {
                BaseDays = 30.0,
                DramaReference = 3,
                MinDays = 5.0
            };

            // interest 0.1, drama 1, dramaRef 3 -> raw = 0.1 * (1/3) * 30 * 1.0 = 1.0
            var res = MemorySpan.Compute(0.1, 1, 10.0, 0, cfg);

            Assert.Equal(5.0, res.Days);
            Assert.Equal(15.0, res.ForgetDay);
            Assert.True(res.RaisedToMin);
            Assert.Equal(1.0, res.Raw, 3);
        }

        [Fact]
        public void MemorySpan_ReinforceGrowth_AndMaxCap()
        {
            var cfg = new MemoryConfig
            {
                BaseDays = 10.0,
                DramaReference = 1,
                MinDays = 1.0,
                ReinforceGrowth = 1.5,
                ReinforceMaxMultiplier = 3.0
            };

            // heardCount 0 -> reinforce = 1.0
            var res0 = MemorySpan.Compute(1.0, 1, 0.0, 0, cfg);
            Assert.Equal(1.0, res0.Reinforce);
            Assert.Equal(10.0, res0.Days);

            // heardCount 1 -> reinforce = 1.5
            var res1 = MemorySpan.Compute(1.0, 1, 0.0, 1, cfg);
            Assert.Equal(1.5, res1.Reinforce);
            Assert.Equal(15.0, res1.Days);

            // heardCount 5 -> 1.5^5 = 7.59 -> clamped to 3.0
            var res5 = MemorySpan.Compute(1.0, 1, 0.0, 5, cfg);
            Assert.Equal(3.0, res5.Reinforce);
            Assert.Equal(30.0, res5.Days);
        }

        [Fact]
        public void MemorySpan_KeptOld_WhenCandidateBeforeOldForgetDay()
        {
            var cfg = new MemoryConfig
            {
                BaseDays = 20.0,
                DramaReference = 1,
                MinDays = 1.0
            };

            // oldForgetDay = 50.0. New computation at day 10.0 with interest 0.5 gives 10 days -> candidate = 20.0
            var res = MemorySpan.Compute(0.5, 1, 10.0, 0, cfg, oldForgetDay: 50.0);

            Assert.True(res.KeptOld);
            Assert.Equal(50.0, res.ForgetDay);
            Assert.Equal(20.0, res.Candidate);
        }

        #endregion

        #region 5. TellFactor & IsForgotten Tests

        [Fact]
        public void TellFactor_NullInterest_OrDisabled_ReturnsOnePointZero()
        {
            var cfg = new MemoryConfig { Enabled = true, TellFactorMin = 0.2 };
            Assert.Equal(1.0, Forgetting.TellFactor((double?)null, cfg));

            var entryNull = new KnownByEntry { Interest = null };
            Assert.Equal(1.0, Forgetting.TellFactor(entryNull, cfg));

            cfg.Enabled = false;
            Assert.Equal(1.0, Forgetting.TellFactor(0.5, cfg));
        }

        [Fact]
        public void TellFactor_MinAndMaxInterest_ReturnsExpected()
        {
            var cfg = new MemoryConfig { Enabled = true, TellFactorMin = 0.2 };

            Assert.Equal(0.2, Forgetting.TellFactor(0.0, cfg), 3);
            Assert.Equal(1.0, Forgetting.TellFactor(1.0, cfg), 3);
            Assert.Equal(0.6, Forgetting.TellFactor(0.5, cfg), 3);
        }

        [Fact]
        public void IsForgotten_MemoryDisabled_ReturnsFalse()
        {
            var cfg = new MemoryConfig { Enabled = false };
            var entry = new KnownByEntry { HeroId = "npc_1", ForgetDay = 10.0 };
            var evt = new WorldEvent { Origin = EventOrigin.Public };

            Assert.False(Forgetting.IsForgotten(evt, entry, 50.0, "player", cfg));
        }

        [Fact]
        public void IsForgotten_PlayerKnower_ReturnsFalse()
        {
            var cfg = new MemoryConfig { Enabled = true };
            var entry = new KnownByEntry { HeroId = "player", ForgetDay = 10.0 };
            var evt = new WorldEvent { Origin = EventOrigin.Public };

            Assert.False(Forgetting.IsForgotten(evt, entry, 50.0, "player", cfg));
        }

        [Fact]
        public void IsForgotten_UnleakedSecret_ReturnsFalse()
        {
            var cfg = new MemoryConfig { Enabled = true };
            var entry = new KnownByEntry { HeroId = "npc_1", ForgetDay = 10.0 };
            var evt = new WorldEvent { Origin = EventOrigin.Secret, State = new RumorState { Leaked = false } };

            Assert.False(Forgetting.IsForgotten(evt, entry, 50.0, "player", cfg));
        }

        [Fact]
        public void IsForgotten_LeakedSecret_CanBeForgotten()
        {
            var cfg = new MemoryConfig { Enabled = true };
            var entry = new KnownByEntry { HeroId = "npc_1", ForgetDay = 10.0 };
            var evt = new WorldEvent { Origin = EventOrigin.Secret, State = new RumorState { Leaked = true } };

            Assert.True(Forgetting.IsForgotten(evt, entry, 15.0, "player", cfg));
        }

        [Fact]
        public void IsForgotten_NullForgetDay_ReturnsFalse()
        {
            var cfg = new MemoryConfig { Enabled = true };
            var entry = new KnownByEntry { HeroId = "npc_1", ForgetDay = null };
            var evt = new WorldEvent { Origin = EventOrigin.Public };

            Assert.False(Forgetting.IsForgotten(evt, entry, 50.0, "player", cfg));
        }

        [Fact]
        public void IsForgotten_DayEqualsForgetDay_ReturnsTrue()
        {
            var cfg = new MemoryConfig { Enabled = true };
            var entry = new KnownByEntry { HeroId = "npc_1", ForgetDay = 25.0 };
            var evt = new WorldEvent { Origin = EventOrigin.Public };

            Assert.True(Forgetting.IsForgotten(evt, entry, 25.0, "player", cfg));
            Assert.False(Forgetting.IsForgotten(evt, entry, 24.9, "player", cfg));
        }

        #endregion

        #region 6. Rehear Rule Tests

        [Fact]
        public void RehearRule_Forgotten_CloserTeller_AdoptsVersion()
        {
            var cfg = new MemoryConfig();
            var entry = new KnownByEntry { HeroId = "npc_1", Hop = 3, Interest = 0.8 };

            var decision = RehearRule.Decide(entry, tellerHop: 1, tellerId: "teller_1", isForgotten: true, cfg);

            Assert.True(decision.AdoptVersion);
            Assert.Equal("forgotten", decision.Reason);
        }

        [Fact]
        public void RehearRule_Forgotten_NotCloserTeller_AdoptsVersion()
        {
            var cfg = new MemoryConfig();
            var entry = new KnownByEntry { HeroId = "npc_1", Hop = 2, Interest = 0.2 };

            var decision = RehearRule.Decide(entry, tellerHop: 2, tellerId: "teller_1", isForgotten: true, cfg);

            Assert.True(decision.AdoptVersion);
            Assert.Equal("forgotten", decision.Reason);
        }

        [Fact]
        public void RehearRule_Remembered_HighInterest_CloserTeller_AdoptsVersion()
        {
            var cfg = new MemoryConfig { UpgradeMinInterest = 0.5 };
            var entry = new KnownByEntry { HeroId = "npc_1", Hop = 3, Interest = 0.65 };

            var decision = RehearRule.Decide(entry, tellerHop: 1, tellerId: "teller_1", isForgotten: false, cfg);

            Assert.True(decision.AdoptVersion);
            Assert.Equal("closer source and interest 0.65 >= 0.50", decision.Reason);
        }

        [Fact]
        public void RehearRule_Remembered_HighInterest_NotCloserTeller_KeepsVersion()
        {
            var cfg = new MemoryConfig { UpgradeMinInterest = 0.5 };
            var entry = new KnownByEntry { HeroId = "npc_1", Hop = 2, Interest = 0.70 };

            var decision = RehearRule.Decide(entry, tellerHop: 2, tellerId: "teller_1", isForgotten: false, cfg);

            Assert.False(decision.AdoptVersion);
            Assert.Equal("kept: source not closer", decision.Reason);
        }

        [Fact]
        public void RehearRule_Remembered_LowInterest_CloserTeller_KeepsVersion()
        {
            var cfg = new MemoryConfig { UpgradeMinInterest = 0.5 };
            var entry = new KnownByEntry { HeroId = "npc_1", Hop = 3, Interest = 0.35 };

            var decision = RehearRule.Decide(entry, tellerHop: 1, tellerId: "teller_1", isForgotten: false, cfg);

            Assert.False(decision.AdoptVersion);
            Assert.Equal("kept: interest 0.35 < 0.50", decision.Reason);
        }

        [Fact]
        public void RehearRule_Remembered_LowInterest_NotCloserTeller_KeepsVersion()
        {
            var cfg = new MemoryConfig { UpgradeMinInterest = 0.5 };
            var entry = new KnownByEntry { HeroId = "npc_1", Hop = 2, Interest = 0.30 };

            var decision = RehearRule.Decide(entry, tellerHop: 2, tellerId: "teller_1", isForgotten: false, cfg);

            Assert.False(decision.AdoptVersion);
            Assert.Equal("kept: source not closer", decision.Reason);
        }

        #endregion

        #region 7. RumorEngine Tests

        [Fact]
        public void RumorEngine_ForgottenTeller_IsExcludedFromCandidates()
        {
            var (engine, channel, traits, cfg) = CreateEngine(customRng: new AlwaysTellRng());
            traits.Set(new TraitProfile { HeroId = "teller_forgotten", IsAlive = true, IsLord = true });
            traits.Set(new TraitProfile { HeroId = "teller_remembered", IsAlive = true, IsLord = true });
            traits.Set(new TraitProfile { HeroId = "target_npc", IsAlive = true, IsLord = true });

            channel.AddLink("teller_remembered", "target_npc", ChannelKind.Kingdom);

            var evt = new WorldEvent
            {
                EventId = "evt_teller_test",
                Origin = EventOrigin.Public,
                Day = 1.0,
                KnownBy =
                {
                    // Forgotten teller at day 50.0
                    new KnownByEntry { HeroId = "teller_forgotten", Hop = 0, ForgetDay = 10.0, Interest = 0.2 },
                    // Remembered teller
                    new KnownByEntry { HeroId = "teller_remembered", Hop = 0, ForgetDay = 100.0, Interest = 0.8 }
                }
            };

            var outcome = engine.PropagateOnce(evt, 50.0, 12);

            Assert.Single(outcome.NewKnowers);
            Assert.Equal("target_npc", outcome.NewKnowers[0].HeroId);
            var targetEntry = evt.EntryFor("target_npc");
            Assert.NotNull(targetEntry);
            Assert.Equal("teller_remembered", targetEntry!.SourceHeroId);
        }

        [Fact]
        public void RumorEngine_TellFactor_AltersTellerWeights()
        {
            var cfg = new VividWorldConfig();
            cfg.Memory.TellFactorMin = 0.1;

            var (engine, channel, traits, _) = CreateEngine(customConfig: cfg);
            traits.Set(new TraitProfile { HeroId = "teller_low", IsAlive = true, IsLord = true });
            traits.Set(new TraitProfile { HeroId = "teller_high", IsAlive = true, IsLord = true });
            traits.Set(new TraitProfile { HeroId = "target_npc", IsAlive = true, IsLord = true });

            channel.AddLink("teller_low", "target_npc", ChannelKind.Kingdom);
            channel.AddLink("teller_high", "target_npc", ChannelKind.Kingdom);

            int lowCount = 0;
            int highCount = 0;

            for (int i = 0; i < 50; i++)
            {
                var evt = new WorldEvent
                {
                    EventId = $"evt_weight_{i}",
                    Origin = EventOrigin.Public,
                    Day = 1.0,
                    KnownBy =
                    {
                        new KnownByEntry { HeroId = "teller_low", Hop = 0, ForgetDay = 100.0, Interest = 0.0 }, // TellFactor = 0.1
                        new KnownByEntry { HeroId = "teller_high", Hop = 0, ForgetDay = 100.0, Interest = 1.0 }  // TellFactor = 1.0
                    }
                };

                var outcome = engine.PropagateOnce(evt, 10.0 + i * 0.1, 12);
                var entry = evt.EntryFor("target_npc");
                if (entry?.SourceHeroId == "teller_low") lowCount++;
                if (entry?.SourceHeroId == "teller_high") highCount++;
            }

            Assert.True(highCount > lowCount * 3, $"High count ({highCount}) should significantly exceed low count ({lowCount})");
        }

        [Fact]
        public void RumorEngine_TellFactor_AltersContactTellChance()
        {
            // 同一位講述者、同一位接觸者，只差講述者的興趣：記錄引擎實際擲的 p，比值必須等於講述意願的比值
            double RecordP(double? interest)
            {
                var cfg = new VividWorldConfig();
                cfg.Memory.TellFactorMin = 0.3;
                var rng = new CapturingRng();
                var (engine, channel, traits, _) = CreateEngine(customConfig: cfg, customRng: rng);
                traits.Set(new TraitProfile { HeroId = "teller", IsAlive = true, IsLord = true });
                traits.Set(new TraitProfile { HeroId = "contact", IsAlive = true, IsLord = true });
                channel.AddLink("teller", "contact", ChannelKind.Kingdom);

                var evt = new WorldEvent
                {
                    EventId = "evt_tell_chance",
                    Origin = EventOrigin.Public,
                    Day = 1.0,
                    KnownBy = { new KnownByEntry { HeroId = "teller", Hop = 0, ForgetDay = 100.0, Interest = interest } }
                };

                engine.PropagateOnce(evt, 5.0, 12);
                Assert.Single(rng.RecordedProbabilities);
                return rng.RecordedProbabilities[0];
            }

            double pLegacy = RecordP(null);   // 未計算 ⇒ 講述意願 1.0
            double pZero = RecordP(0.0);      // 0.3
            double pHalf = RecordP(0.5);      // 0.3 + 0.7 × 0.5 = 0.65
            double pFull = RecordP(1.0);      // 1.0

            Assert.True(pLegacy > 0);
            Assert.Equal(pLegacy, pFull, 12);
            Assert.Equal(0.3, pZero / pLegacy, 12);
            Assert.Equal(0.65, pHalf / pLegacy, 12);
        }

        /// <summary>擲骰一律成功；挑講述者取權重最大者（同分取前者）。用來讓「傳播成功之後」的斷言不受真亂數影響。</summary>
        private sealed class AlwaysTellRng : IDeterministicRng
        {
            public double NextDouble(long seed) => 0.0;
            public bool Chance(double p, long seed) => p > 0;
            public int Pick(int count, long seed) => 0;
            public int PickWeighted(IReadOnlyList<double> weights, long seed)
            {
                int best = 0;
                for (int i = 1; i < weights.Count; i++)
                {
                    if (weights[i] > weights[best]) best = i;
                }
                return best;
            }
        }

        /// <summary>記錄每一次擲骰的 p，一律不成功。</summary>
        private sealed class CapturingRng : IDeterministicRng
        {
            public List<double> RecordedProbabilities { get; } = new();
            public double NextDouble(long seed) => 0.5;
            public bool Chance(double p, long seed)
            {
                RecordedProbabilities.Add(p);
                return false;
            }
            public int Pick(int count, long seed) => 0;
            public int PickWeighted(IReadOnlyList<double> weights, long seed) => 0;
        }

        [Fact]
        public void RumorEngine_UnstampedKnownContact_IsSkipped()
        {
            var (engine, channel, traits, cfg) = CreateEngine();
            traits.Set(new TraitProfile { HeroId = "teller", IsAlive = true, IsLord = true });
            traits.Set(new TraitProfile { HeroId = "contact_unstamped", IsAlive = true, IsLord = true });

            channel.AddLink("teller", "contact_unstamped", ChannelKind.Kingdom);

            var evt = new WorldEvent
            {
                EventId = "evt_unstamped",
                Origin = EventOrigin.Public,
                Day = 1.0,
                KnownBy =
                {
                    new KnownByEntry { HeroId = "teller", Hop = 0, ForgetDay = 100.0, Interest = 0.9 },
                    new KnownByEntry { HeroId = "contact_unstamped", Hop = 1, Interest = null, ForgetDay = null }
                }
            };

            var outcome = engine.PropagateOnce(evt, 5.0, 12);
            Assert.Empty(outcome.NewKnowers);
            Assert.Empty(outcome.Reheard);
        }

        [Fact]
        public void RumorEngine_StampedKnownContact_ReheardsSuccessfully()
        {
            var (engine, channel, traits, cfg) = CreateEngine(customRng: new AlwaysTellRng());
            traits.Set(new TraitProfile { HeroId = "teller", IsAlive = true, IsLord = true });
            traits.Set(new TraitProfile { HeroId = "contact_known", IsAlive = true, IsLord = true });

            channel.AddLink("teller", "contact_known", ChannelKind.Kingdom);

            var contactEntry = new KnownByEntry
            {
                HeroId = "contact_known",
                Hop = 2,
                LearnedDay = 1.0,
                Interest = 0.8,
                ForgetDay = 100.0,
                HeardCount = 0
            };

            var evt = new WorldEvent
            {
                EventId = "evt_rehear",
                Origin = EventOrigin.Public,
                Day = 1.0,
                KnownBy =
                {
                    new KnownByEntry { HeroId = "teller", Hop = 0, ForgetDay = 100.0, Interest = 0.9 },
                    contactEntry
                }
            };

            var outcome = engine.PropagateOnce(evt, 15.0, 12);

            Assert.Empty(outcome.NewKnowers);
            Assert.Single(outcome.Reheard);
            Assert.Equal(1, contactEntry.HeardCount);
            Assert.Equal(15.0, contactEntry.LastHeardDay);
            Assert.Equal("contact_known", outcome.Reheard[0].Entry.HeroId);
        }

        [Fact]
        public void RumorEngine_ForgottenKnower_AdoptsVersionOnRehear()
        {
            var (engine, channel, traits, cfg) = CreateEngine(customRng: new AlwaysTellRng());
            traits.Set(new TraitProfile { HeroId = "teller", IsAlive = true, IsLord = true });
            traits.Set(new TraitProfile { HeroId = "contact_forgotten", IsAlive = true, IsLord = true });

            channel.AddLink("teller", "contact_forgotten", ChannelKind.Kingdom);

            var contactEntry = new KnownByEntry
            {
                HeroId = "contact_forgotten",
                Hop = 3,
                LearnedDay = 1.0,
                Interest = 0.2,
                ForgetDay = 10.0, // forgotten at day 20.0
                HeardCount = 0
            };

            var evt = new WorldEvent
            {
                EventId = "evt_rehear_forgotten",
                Origin = EventOrigin.Public,
                Day = 1.0,
                KnownBy =
                {
                    new KnownByEntry { HeroId = "teller", Hop = 1, ForgetDay = 100.0, Interest = 0.9 },
                    contactEntry
                }
            };

            var outcome = engine.PropagateOnce(evt, 20.0, 12);

            Assert.Single(outcome.Reheard);
            Assert.True(outcome.Reheard[0].Rule.AdoptVersion);
            Assert.True(outcome.Reheard[0].WasForgotten);
            Assert.Equal(2, contactEntry.Hop); // adopted teller.Hop + 1 = 1 + 1 = 2
            Assert.Equal("teller", contactEntry.SourceHeroId);
        }

        [Fact]
        public void RumorEngine_CloserTeller_UpgradesHop_FartherTeller_KeepsHop()
        {
            var (engine, channel, traits, cfg) = CreateEngine(customRng: new AlwaysTellRng());
            traits.Set(new TraitProfile { HeroId = "teller_closer", IsAlive = true, IsLord = true });
            traits.Set(new TraitProfile { HeroId = "teller_farther", IsAlive = true, IsLord = true });
            traits.Set(new TraitProfile { HeroId = "listener", IsAlive = true, IsLord = true });

            // 1. Closer teller (Hop 0 -> listener Hop 2 upgrades to Hop 1)
            channel.AddLink("teller_closer", "listener", ChannelKind.Kingdom);

            var listenerEntry = new KnownByEntry
            {
                HeroId = "listener",
                Hop = 2,
                Interest = 0.8,
                ForgetDay = 100.0
            };

            var evt1 = new WorldEvent
            {
                EventId = "evt_closer",
                Origin = EventOrigin.Public,
                Day = 1.0,
                KnownBy =
                {
                    new KnownByEntry { HeroId = "teller_closer", Hop = 0, Interest = 0.9, ForgetDay = 100.0 },
                    listenerEntry
                }
            };

            var outcome1 = engine.PropagateOnce(evt1, 10.0, 12);
            Assert.Single(outcome1.Reheard);
            Assert.True(outcome1.Reheard[0].Rule.AdoptVersion);
            Assert.Equal(1, listenerEntry.Hop);

            // 2. Farther teller (teller Hop 1 -> tellerHop + 1 = 2 not < listener Hop 1 -> kept)
            var channel2 = new FakePropagationChannel();
            channel2.AddLink("teller_farther", "listener", ChannelKind.Kingdom);
            var engine2 = new RumorEngine(cfg, FactRetentionPolicies.Create(cfg, new AlwaysTellRng(), 42L), NullEmbellishmentPolicy.Instance, channel2, traits, new AlwaysTellRng(), 42L, "player");

            var listenerEntry2 = new KnownByEntry
            {
                HeroId = "listener",
                Hop = 1,
                Interest = 0.8,
                ForgetDay = 100.0
            };

            var evt2 = new WorldEvent
            {
                EventId = "evt_farther",
                Origin = EventOrigin.Public,
                Day = 1.0,
                KnownBy =
                {
                    new KnownByEntry { HeroId = "teller_farther", Hop = 1, Interest = 0.9, ForgetDay = 100.0 },
                    listenerEntry2
                }
            };

            var outcome2 = engine2.PropagateOnce(evt2, 10.0, 12);
            Assert.Single(outcome2.Reheard);
            Assert.False(outcome2.Reheard[0].Rule.AdoptVersion);
            Assert.Equal(1, listenerEntry2.Hop); // unchanged
        }

        [Fact]
        public void RumorEngine_Reheard_DoesNotIncrementNewKnowers_OrSetLastNewKnowerDay()
        {
            var (engine, channel, traits, cfg) = CreateEngine(customRng: new AlwaysTellRng());
            traits.Set(new TraitProfile { HeroId = "teller", IsAlive = true, IsLord = true });
            traits.Set(new TraitProfile { HeroId = "listener", IsAlive = true, IsLord = true });

            channel.AddLink("teller", "listener", ChannelKind.Kingdom);

            var evt = new WorldEvent
            {
                EventId = "evt_no_new_knowers",
                Origin = EventOrigin.Public,
                Day = 1.0,
                State = new RumorState { LastNewKnowerDay = 5.0 },
                KnownBy =
                {
                    new KnownByEntry { HeroId = "teller", Hop = 0, Interest = 0.9, ForgetDay = 100.0 },
                    new KnownByEntry { HeroId = "listener", Hop = 1, Interest = 0.8, ForgetDay = 100.0 }
                }
            };

            var outcome = engine.PropagateOnce(evt, 12.0, 12);

            Assert.Empty(outcome.NewKnowers);
            Assert.Single(outcome.Reheard);
            Assert.Equal(5.0, evt.State.LastNewKnowerDay); // unchanged, not bumped to 12.0
        }

        [Fact]
        public void RumorEngine_AllNpcKnowersForgotten_TriggersAllForgottenDormancy()
        {
            var (engine, _, traits, cfg) = CreateEngine();

            var evt = new WorldEvent
            {
                EventId = "evt_all_forgotten",
                Origin = EventOrigin.Public,
                Day = 1.0,
                DramaWeight = 3,
                State = new RumorState { LastNewKnowerDay = 1.0 },
                KnownBy =
                {
                    new KnownByEntry { HeroId = "player", Hop = 0, LearnedDay = 1.0 },
                    new KnownByEntry { HeroId = "npc_1", Hop = 0, LearnedDay = 1.0, ForgetDay = 15.0 },
                    new KnownByEntry { HeroId = "npc_2", Hop = 1, LearnedDay = 2.0, ForgetDay = 20.0 }
                }
            };

            // At day 10: neither has forgotten
            var reason10 = engine.DormancyReasonFor(evt, 10.0);
            Assert.Null(reason10);
            Assert.False(engine.ShouldGoDormant(evt, 10.0));

            // At day 25: both NPCs have forgotten (player never forgets)
            var reason25 = engine.DormancyReasonFor(evt, 25.0);
            Assert.NotNull(reason25);
            Assert.Equal(DormancyKind.AllForgotten, reason25!.Kind);
            Assert.Equal(2, reason25.Count);
            Assert.True(engine.ShouldGoDormant(evt, 25.0));
        }

        [Fact]
        public void RumorEngine_MemoryDisabled_MatchesLegacyBehavior()
        {
            var cfg = new VividWorldConfig();
            cfg.Memory.Enabled = false;

            var (engine, channel, traits, _) = CreateEngine(customConfig: cfg);
            traits.Set(new TraitProfile { HeroId = "teller", IsAlive = true, IsLord = true });
            traits.Set(new TraitProfile { HeroId = "listener", IsAlive = true, IsLord = true });

            channel.AddLink("teller", "listener", ChannelKind.Kingdom);

            var evt = new WorldEvent
            {
                EventId = "evt_legacy",
                Origin = EventOrigin.Public,
                Day = 1.0,
                KnownBy =
                {
                    // Past forget day, but memory is disabled
                    new KnownByEntry { HeroId = "teller", Hop = 0, ForgetDay = 10.0, Interest = 0.5 },
                    new KnownByEntry { HeroId = "listener", Hop = 1, ForgetDay = 10.0, Interest = 0.5 }
                }
            };

            var outcome = engine.PropagateOnce(evt, 20.0, 12);

            // Teller was not excluded (memory disabled), listener already knew -> skipped (no rehear)
            Assert.Empty(outcome.NewKnowers);
            Assert.Empty(outcome.Reheard);
        }

        #endregion

        #region 8. Formatter Tests

        [Fact]
        public void MemoryLogFormatter_FormatCalculation_FormatsAllVariants()
        {
            var cfg = new MemoryConfig { BaseDays = 30.0, DramaReference = 3, MinDays = 3.0 };
            var knower = new InterestHeroFacts("lord_1", "clan_k");
            var part1 = new InterestParticipant("victim", "lord_2", new InterestHeroFacts("lord_2", "clan_o"), 60);
            var part2 = new InterestParticipant("witness", "lord_3", new InterestHeroFacts("lord_3", "clan_k"), null);

            var intRes = InterestCalculator.Compute(knower, new[] { part1, part2 }, cfg);
            var spanRes = MemorySpan.Compute(intRes.Interest, 3, 10.0, 0, cfg);

            // 1. Learned with others
            string log1 = MemoryLogFormatter.FormatCalculation("lord_1", "learned", "evt_1", intRes, 3, 3, 30.0, spanRes);
            Assert.Contains("Memory: lord_1 learned evt_1 - interest 0.60 via victim=lord_2 relation +60 (others: witness=lord_3 0.40 sameClan floor) x drama 3/3 x base 30.0d x reinforce 1.00 = 18.0d -> forgets on day 28.0", log1);

            // 2. Re-heard adopted version
            var rehearRule = new RehearDecision(true, "closer source and interest 0.60 >= 0.50");
            string howAdopt = string.Format(CultureInfo.InvariantCulture,
                "re-heard from {0}, adopted version hop {1}->{2} ({3})", "teller_1", 2, 1, rehearRule.Reason);
            string log2 = MemoryLogFormatter.FormatCalculation("lord_1", howAdopt, "evt_1", intRes, 3, 3, 30.0, spanRes);
            Assert.Contains("re-heard from teller_1, adopted version hop 2->1 (closer source and interest 0.60 >= 0.50)", log2);

            // 3. Raised to min
            var spanRaised = MemorySpan.Compute(0.05, 1, 10.0, 0, cfg);
            string logRaised = MemoryLogFormatter.FormatCalculation("lord_1", "learned", "evt_1", intRes, 1, 3, 30.0, spanRaised);
            Assert.Contains("(raised from 0.50d to min)", logRaised);

            // 4. Kept old
            var spanKept = MemorySpan.Compute(0.5, 3, 10.0, 0, cfg, oldForgetDay: 50.0);
            string logKept = MemoryLogFormatter.FormatCalculation("lord_1", "learned", "evt_1", intRes, 3, 3, 30.0, spanKept);
            Assert.Contains("-> forgets on day 50.0 (kept; new 25.0)", logKept);

            // 5. Knower not found
            var intResNotFound = InterestCalculator.Compute(null, new[] { part1 }, cfg);
            string logNotFound = MemoryLogFormatter.FormatCalculation("lord_unknown", "learned", "evt_1", intResNotFound, 3, 3, 30.0, spanRes);
            Assert.Contains("via (knower not found)", logNotFound);
        }

        [Fact]
        public void MemoryLogFormatter_FormatDormancy_FormatsAllFourReasons()
        {
            var r1 = DormancyReason.ForMaxHop(5, 3);
            Assert.Equal("Rumor evt_1 went dormant: all 5 knower(s) at max hop 3", MemoryLogFormatter.FormatDormancy("evt_1", r1));

            var r2 = DormancyReason.ForLifetime(125.4, 120.0);
            Assert.Equal("Rumor evt_2 went dormant: age 125.4d > lifetime 120.0d", MemoryLogFormatter.FormatDormancy("evt_2", r2));

            var r3 = DormancyReason.ForStale(32.1, 30.0);
            Assert.Equal("Rumor evt_3 went dormant: no new knower for 32.1d > stale 30.0d", MemoryLogFormatter.FormatDormancy("evt_3", r3));

            var r4 = DormancyReason.ForAllForgotten(4);
            Assert.Equal("Rumor evt_4 went dormant: all 4 NPC knower(s) have forgotten", MemoryLogFormatter.FormatDormancy("evt_4", r4));
        }

        [Fact]
        public void MemoryLogFormatter_FormatForgottenSummary_WithAndWithoutMore()
        {
            // 3 items (<= 5)
            var list3 = new List<(string EventId, double ForgetDay)>
            {
                ("evt_c", 20.0),
                ("evt_a", 50.0),
                ("evt_b", 30.0)
            };
            string s3 = MemoryLogFormatter.FormatForgottenSummary("Lord Val", "lord_val", list3, 10);
            Assert.Equal("  memory: Lord Val (lord_val) has forgotten 3 of 10 event(s) on file: evt_a (forgot day 50.0), evt_b (forgot day 30.0), evt_c (forgot day 20.0)", s3);

            // 7 items (> 5 -> +2 more)
            var list7 = new List<(string EventId, double ForgetDay)>
            {
                ("e1", 70.0), ("e2", 60.0), ("e3", 50.0), ("e4", 40.0), ("e5", 30.0), ("e6", 20.0), ("e7", 10.0)
            };
            string s7 = MemoryLogFormatter.FormatForgottenSummary("Lord Val", "lord_val", list7, 12);
            Assert.Equal("  memory: Lord Val (lord_val) has forgotten 7 of 12 event(s) on file: e1 (forgot day 70.0), e2 (forgot day 60.0), e3 (forgot day 50.0), e4 (forgot day 40.0), e5 (forgot day 30.0) (+2 more)", s7);
        }

        [Fact]
        public void MemoryLogFormatter_FormatWorldStatus_EnabledAndDisabled()
        {
            var cfg = new MemoryConfig
            {
                Enabled = true,
                BaseDays = 30.0,
                DramaReference = 3,
                MinDays = 3.0,
                TellFactorMin = 0.2,
                UpgradeMinInterest = 0.5
            };

            string statusOn = MemoryLogFormatter.FormatWorldStatus(cfg, remembered: 15, forgotten: 4, unstamped: 2, eventsAllForgotten: 1);
            Assert.Equal("- Memory: on, base 30.0d, drama ref 3, min 3.0d, tell min 0.20, upgrade at 0.50; NPC knower entries remembered 15 / forgotten 4 / unstamped 2; events with every NPC knower forgotten 1", statusOn);

            cfg.Enabled = false;
            string statusOff = MemoryLogFormatter.FormatWorldStatus(cfg, 15, 4, 2, 1);
            Assert.Equal("- Memory: off", statusOff);
        }

        [Fact]
        public void KnowledgeShareFormatter_FormatRemembered_HasPrefixAndLeavesFormatUnchanged()
        {
            var data = new Dictionary<string, int>
            {
                ["battle"] = 8,
                ["duel"] = 2
            };

            string format = KnowledgeShareFormatter.Format(data);
            Assert.StartsWith("- Knowledge share by event type (hop entries): ", format);
            Assert.Contains("battle 8 (80.0%)", format);
            Assert.Contains("duel 2 (20.0%)", format);

            string formatRem = KnowledgeShareFormatter.FormatRemembered(data);
            Assert.StartsWith("- Knowledge share by event type (remembered NPC entries): ", formatRem);
            Assert.Contains("battle 8 (80.0%)", formatRem);
            Assert.Contains("duel 2 (20.0%)", formatRem);
        }

        #endregion
    }
}
