using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using VividWorld.Core.Config;
using VividWorld.Core.Events;
using VividWorld.Core.Persistence;
using VividWorld.Core.Rumors;
using VividWorld.Core.Util;
using Xunit;

namespace VividWorld.Core.Tests
{
    public class SecretSpreadTests
    {
        [Fact]
        public void Multipliers_IndividualComputations_MatchSpec()
        {
            var leakCfg = new LeakConfig();

            // Drama discount bands
            Assert.Equal(1.0, SecretSpread.ComputeWeightDiscount(2));
            Assert.Equal(0.5, SecretSpread.ComputeWeightDiscount(4));
            Assert.Equal(0.2, SecretSpread.ComputeWeightDiscount(6));
            Assert.Equal(0.05, SecretSpread.ComputeWeightDiscount(9));

            // Relation multiplier
            Assert.Equal(0.1, SecretSpread.ComputeRelationMultiplier(60, false));
            Assert.Equal(0.1, SecretSpread.ComputeRelationMultiplier(35, true));
            Assert.Equal(3.0, SecretSpread.ComputeRelationMultiplier(-60, false));
            Assert.Equal(1.0, SecretSpread.ComputeRelationMultiplier(0, false));

            // Owner traits (calc, gen)
            var ownerProfile = new TraitProfile { Calculating = 1, Generosity = 1, Honor = 2 };
            double ownerMultiplier = SecretSpread.ComputeOwnerTraitMultiplier(ownerProfile, leakCfg);
            double expectedOwner = 1.0 + (leakCfg.TraitWeights.Calculating * 1) + (leakCfg.TraitWeights.Generosity * 1);
            Assert.Equal(expectedOwner, ownerMultiplier, 4);

            // Intermediary traits (hon, calc, gen)
            var interProfile = new TraitProfile { Honor = -1, Calculating = 1, Generosity = 1 };
            double interMultiplier = SecretSpread.ComputeIntermediaryTraitMultiplier(interProfile, leakCfg);
            double expectedInter = 1.0 + (leakCfg.TraitWeights.Honor * -1) + (leakCfg.TraitWeights.Calculating * 1) + (leakCfg.TraitWeights.Generosity * 1);
            Assert.Equal(expectedInter, interMultiplier, 4);
        }

        [Fact]
        public void OwnerTraitMultiplier_OnlyCountsCalculatingAndGenerosity_IgnoresHonor()
        {
            var leakCfg = new LeakConfig();
            var profile1 = new TraitProfile { Calculating = 1, Generosity = -1, Honor = 2 };
            var profile2 = new TraitProfile { Calculating = 1, Generosity = -1, Honor = -2 };

            double mult1 = SecretSpread.ComputeOwnerTraitMultiplier(profile1, leakCfg);
            double mult2 = SecretSpread.ComputeOwnerTraitMultiplier(profile2, leakCfg);

            Assert.Equal(mult1, mult2);
        }

        [Theory]
        [InlineData(1, 1.0)]
        [InlineData(2, 1.0)]
        [InlineData(3, 1.0)]
        [InlineData(4, 0.5)]
        [InlineData(5, 0.5)]
        [InlineData(6, 0.2)]
        [InlineData(7, 0.2)]
        [InlineData(8, 0.05)]
        [InlineData(9, 0.05)]
        [InlineData(10, 0.05)]
        public void DramaWeightDiscount_FourBands_MatchSpec(int dramaWeightTen, double expectedDiscount)
        {
            Assert.Equal(expectedDiscount, SecretSpread.ComputeWeightDiscount(dramaWeightTen));
        }

        [Fact]
        public void RelationMultiplier_ValuesAndMinimumAcrossMultipleOwners()
        {
            var owners = new List<string> { "owner1", "owner2" };

            // owner1 relation = -60 (3.0), owner2 relation = 60 (0.1) -> min is 0.1
            var relMap = new Dictionary<string, int>
            {
                { "owner1", -60 },
                { "owner2", 60 }
            };
            var kinMap = new Dictionary<string, bool>
            {
                { "owner1", false },
                { "owner2", false }
            };

            double mult = SecretSpread.ComputeIntermediaryRelationMultiplier("knower", owners, relMap, kinMap);
            Assert.Equal(0.1, mult);

            // Kin with relation 35 (0.1) vs neutral other (1.0) -> min is 0.1
            var kinMap2 = new Dictionary<string, bool>
            {
                { "owner1", true },
                { "owner2", false }
            };
            var relMap2 = new Dictionary<string, int>
            {
                { "owner1", 35 },
                { "owner2", 10 }
            };
            double mult2 = SecretSpread.ComputeIntermediaryRelationMultiplier("knower", owners, relMap2, kinMap2);
            Assert.Equal(0.1, mult2);
        }

        [Theory]
        [InlineData(1)]
        [InlineData(2)]
        public void Leak_HonorGreaterThanOrEqualToOne_NeverLeaks(int honor)
        {
            var config = new VividWorldConfig();
            config.Leak.BaseChancePerInsiderPerDay = 1.0;
            config.Secrets.ConfideChancePerDay = 0.0; // disable confide to isolate leak test

            var evt = new WorldEvent
            {
                EventId = "evt_secret_honor",
                Origin = EventOrigin.Secret,
                Day = 1.0,
                SecretOwnerHeroIds = new List<string> { "lord_owner" },
                KnownBy = new List<KnownByEntry> { new KnownByEntry { HeroId = "knower_honorable", Hop = 1, LearnedDay = 1.0 } }
            };

            var knower = new SecretKnowerContext
            {
                HeroId = "knower_honorable",
                Traits = new TraitProfile { Honor = honor },
                CoLocatedContacts = new List<SecretCoLocatedContact>
                {
                    new SecretCoLocatedContact("lord_other", isPlayer: false, isLord: true, relation: 20, isKin: false)
                }
            };

            var rng = new SplitMix64Rng();
            var eval = SecretSpread.Evaluate(evt, 5.0, new[] { knower }, null, config, rng, 12345L);

            Assert.Single(eval.Results);
            Assert.Equal(SecretSpreadAction.NotRolled, eval.Results[0].Action);
            Assert.Equal("honor ≥ 1", eval.Results[0].LeakUnrolledReason);
        }

        [Fact]
        public void ConfideTrustThreshold_StrictlyGreaterThanLineRequired()
        {
            var config = new VividWorldConfig();
            // Default lines: Kin = 30, Other = 50

            var evt = new WorldEvent
            {
                EventId = "evt_trust",
                Origin = EventOrigin.Secret,
                Day = 1.0,
                SecretOwnerHeroIds = new List<string> { "knower_hero" },
                KnownBy = new List<KnownByEntry> { new KnownByEntry { HeroId = "knower_hero", Hop = 0, LearnedDay = 1.0 } }
            };

            var knower = new SecretKnowerContext
            {
                HeroId = "knower_hero",
                CoLocatedContacts = new List<SecretCoLocatedContact>
                {
                    new SecretCoLocatedContact("kin_30", isPlayer: false, isLord: true, relation: 30, isKin: true),
                    new SecretCoLocatedContact("kin_31", isPlayer: false, isLord: true, relation: 31, isKin: true),
                    new SecretCoLocatedContact("other_50", isPlayer: false, isLord: true, relation: 50, isKin: false),
                    new SecretCoLocatedContact("other_51", isPlayer: false, isLord: true, relation: 51, isKin: false)
                }
            };

            var rng = new SplitMix64Rng();
            var result = SecretSpread.EvaluateKnower(evt, 5.0, knower, null, config, rng, 12345L);

            // 30 kin and 50 other should be excluded for BelowTrustLine
            Assert.Contains(result.ExcludedCandidates, e => e.HeroId == "kin_30" && e.Reason == SecretCandidateExcludeReason.BelowTrustLine);
            Assert.Contains(result.ExcludedCandidates, e => e.HeroId == "other_50" && e.Reason == SecretCandidateExcludeReason.BelowTrustLine);

            // Chosen target should be between kin_31 and other_51 (highest relation is other_51)
            Assert.Equal("other_51", result.ChosenTargetId);
        }

        [Fact]
        public void ConfideCandidates_ExcludeAlreadyKnowsAndHarmed()
        {
            var config = new VividWorldConfig();

            var evt = new WorldEvent
            {
                EventId = "evt_exclude",
                Origin = EventOrigin.Secret,
                Day = 1.0,
                SecretOwnerHeroIds = new List<string> { "knower_hero" },
                SecretHarmedHeroIds = new List<string> { "victim_hero" },
                KnownBy = new List<KnownByEntry>
                {
                    new KnownByEntry { HeroId = "knower_hero", Hop = 0, LearnedDay = 1.0 },
                    new KnownByEntry { HeroId = "already_knows_hero", Hop = 1, LearnedDay = 1.0 }
                }
            };

            var knower = new SecretKnowerContext
            {
                HeroId = "knower_hero",
                CoLocatedContacts = new List<SecretCoLocatedContact>
                {
                    new SecretCoLocatedContact("already_knows_hero", isPlayer: false, isLord: true, relation: 90, isKin: false),
                    new SecretCoLocatedContact("victim_hero", isPlayer: false, isLord: true, relation: 95, isKin: false),
                    new SecretCoLocatedContact("eligible_hero", isPlayer: false, isLord: true, relation: 60, isKin: false)
                }
            };

            var rng = new SplitMix64Rng();
            var result = SecretSpread.EvaluateKnower(evt, 5.0, knower, evt.SecretHarmedHeroIds, config, rng, 12345L);

            Assert.Contains(result.ExcludedCandidates, e => e.HeroId == "already_knows_hero" && e.Reason == SecretCandidateExcludeReason.AlreadyKnows);
            Assert.Contains(result.ExcludedCandidates, e => e.HeroId == "victim_hero" && e.Reason == SecretCandidateExcludeReason.Harmed);
            Assert.Equal("eligible_hero", result.ChosenTargetId);
        }

        [Fact]
        public void ConfideCandidates_PicksHighestRelation_PlayerComparedEqually()
        {
            var config = new VividWorldConfig();

            var evt = new WorldEvent
            {
                EventId = "evt_player_comp",
                Origin = EventOrigin.Secret,
                Day = 1.0,
                SecretOwnerHeroIds = new List<string> { "knower_hero" },
                KnownBy = new List<KnownByEntry> { new KnownByEntry { HeroId = "knower_hero", Hop = 0, LearnedDay = 1.0 } }
            };

            // Case A: Player has higher relation (80 > 60)
            var knowerA = new SecretKnowerContext
            {
                HeroId = "knower_hero",
                CoLocatedContacts = new List<SecretCoLocatedContact>
                {
                    new SecretCoLocatedContact("npc_friend", isPlayer: false, isLord: true, relation: 60, isKin: false),
                    new SecretCoLocatedContact("player", isPlayer: true, isLord: false, relation: 80, isKin: false)
                }
            };

            var rng = new SplitMix64Rng();
            var resultA = SecretSpread.EvaluateKnower(evt, 5.0, knowerA, null, config, rng, 12345L, playerHeroId: "player");
            Assert.Equal("player", resultA.ChosenTargetId);

            // Case B: NPC has higher relation (90 > 75)
            var knowerB = new SecretKnowerContext
            {
                HeroId = "knower_hero",
                CoLocatedContacts = new List<SecretCoLocatedContact>
                {
                    new SecretCoLocatedContact("npc_best_friend", isPlayer: false, isLord: true, relation: 90, isKin: false),
                    new SecretCoLocatedContact("player", isPlayer: true, isLord: false, relation: 75, isKin: false)
                }
            };

            var resultB = SecretSpread.EvaluateKnower(evt, 5.0, knowerB, null, config, rng, 12345L, playerHeroId: "player");
            Assert.Equal("npc_best_friend", resultB.ChosenTargetId);
        }

        [Fact]
        public void ConfideAndLeak_BothHitOnSameDay_OnlyConfideCounts()
        {
            var config = new VividWorldConfig();
            config.Secrets.ConfideChancePerDay = 1.0;
            config.Leak.BaseChancePerInsiderPerDay = 1.0;

            var evt = new WorldEvent
            {
                EventId = "evt_both_hit",
                Origin = EventOrigin.Secret,
                Day = 1.0,
                SecretOwnerHeroIds = new List<string> { "owner_lord" },
                KnownBy = new List<KnownByEntry> { new KnownByEntry { HeroId = "intermediary", Hop = 1, LearnedDay = 1.0 } }
            };

            var knower = new SecretKnowerContext
            {
                HeroId = "intermediary",
                Traits = new TraitProfile { Honor = 0, Calculating = 0, Generosity = 0 },
                CoLocatedContacts = new List<SecretCoLocatedContact>
                {
                    new SecretCoLocatedContact("confide_target", isPlayer: false, isLord: true, relation: 80, isKin: false)
                }
            };

            var rng = new SplitMix64Rng();
            var result = SecretSpread.EvaluateKnower(evt, 5.0, knower, null, config, rng, 12345L);

            Assert.Equal(SecretSpreadAction.Confided, result.Action);
            Assert.Equal("confide_target", result.TargetHeroId);
            Assert.Null(result.LeakRoll); // Leak roll suppressed or bypassed on confide hit
        }

        [Fact]
        public void LeakedSecret_SecretOwnerNeverPickedAsTeller()
        {
            var evt = new WorldEvent
            {
                EventId = "evt_leaked_owner",
                Origin = EventOrigin.Secret,
                Day = 1.0,
                SecretOwnerHeroIds = new List<string> { "lord_owner" },
                State = new RumorState { Leaked = true, LeakedDay = 2.0 },
                KnownBy = new List<KnownByEntry>
                {
                    new KnownByEntry { HeroId = "lord_owner", Hop = 0, LearnedDay = 1.0 },
                    new KnownByEntry { HeroId = "lord_intermediary", Hop = 1, LearnedDay = 2.0 }
                }
            };

            var config = new VividWorldConfig();
            var traitLookup = new VividWorld.Core.Tests.Fakes.FakeHeroTraitLookup();
            traitLookup.Set(new TraitProfile { HeroId = "lord_owner", IsAlive = true, IsLord = true });
            traitLookup.Set(new TraitProfile { HeroId = "lord_intermediary", IsAlive = true, IsLord = true });

            var reasonOwner = TellerEligibility.Check(
                evt, "lord_owner", 3.0, 5, "player", config, traitLookup, null, null, out string? detailOwner);

            var reasonIntermediary = TellerEligibility.Check(
                evt, "lord_intermediary", 3.0, 5, "player", config, traitLookup, null, null, out string? detailIntermediary);

            Assert.Equal(TellReason.SecretOwner, reasonOwner);
            Assert.Equal("secret owner", detailOwner);

            Assert.Equal(TellReason.Ok, reasonIntermediary);
        }

        [Fact]
        public void DeterministicRng_SameDayReRoll_ReturnsSameAnswer()
        {
            var config = new VividWorldConfig();
            config.Secrets.ConfideChancePerDay = 0.5;
            config.Leak.BaseChancePerInsiderPerDay = 0.5;

            var evt = new WorldEvent
            {
                EventId = "evt_reroll",
                Origin = EventOrigin.Secret,
                Day = 1.0,
                SecretOwnerHeroIds = new List<string> { "owner" },
                KnownBy = new List<KnownByEntry> { new KnownByEntry { HeroId = "knower", Hop = 1, LearnedDay = 1.0 } }
            };

            var knower = new SecretKnowerContext
            {
                HeroId = "knower",
                Traits = new TraitProfile { Honor = 0 },
                CoLocatedContacts = new List<SecretCoLocatedContact>
                {
                    new SecretCoLocatedContact("friend", isPlayer: false, isLord: true, relation: 60, isKin: false)
                }
            };

            var rng1 = new SplitMix64Rng();
            var eval1 = SecretSpread.Evaluate(evt, 10.0, new[] { knower }, null, config, rng1, 99999L);

            var rng2 = new SplitMix64Rng();
            var eval2 = SecretSpread.Evaluate(evt, 10.0, new[] { knower }, null, config, rng2, 99999L);

            Assert.Equal(eval1.Results[0].Action, eval2.Results[0].Action);
            Assert.Equal(eval1.Results[0].ConfideRoll?.Hit, eval2.Results[0].ConfideRoll?.Hit);
            Assert.Equal(eval1.Results[0].LeakRoll?.Hit, eval2.Results[0].LeakRoll?.Hit);
        }

        [Fact]
        public void ConfigMerge_OldConfigWithoutSecrets_MergesDefaultsAndPreservesExisting()
        {
            var oldJson = @"{
                ""configVersion"": 1,
                ""enabled"": true,
                ""propagation"": {
                    ""baseTellChancePerContact"": 0.18
                }
            }";

            var canonical = new VividWorldConfig();
            var canonicalJson = VividJson.Write(canonical);

            var oldObj = JObject.Parse(oldJson);
            var canonicalObj = JObject.Parse(canonicalJson);

            var result = ConfigMerge.AddMissingKeys(oldObj, canonicalObj);

            Assert.Contains("secrets.confideChancePerDay", result.AddedPaths);
            Assert.Contains("secrets.confideTrustKin", result.AddedPaths);
            Assert.Contains("secrets.confideTrustOther", result.AddedPaths);

            Assert.NotNull(result.Merged["secrets"]);
            Assert.Equal(0.02, (double)result.Merged["secrets"]!["confideChancePerDay"]!);
            Assert.Equal(30, (int)result.Merged["secrets"]!["confideTrustKin"]!);
            Assert.Equal(50, (int)result.Merged["secrets"]!["confideTrustOther"]!);

            // Existing keys preserved
            Assert.Equal(0.18, (double)result.Merged["propagation"]!["baseTellChancePerContact"]!);
        }

        [Fact]
        public void OldShapeSecret_WithoutSecretOwnerHeroIds_LeaksViaTryLeak()
        {
            var config = new VividWorldConfig();
            config.Leak.GraceDays = 0.0;
            config.Leak.BaseChancePerInsiderPerDay = 1.0;

            var traitLookup = new VividWorld.Core.Tests.Fakes.FakeHeroTraitLookup();
            traitLookup.Set(new TraitProfile
            {
                HeroId = "dishonest_hero",
                Honor = -2,
                Calculating = -2,
                Generosity = 2
            });

            var rng = new SplitMix64Rng();
            var channel = new VividWorld.Core.Tests.Fakes.FakePropagationChannel();
            var retention = FactRetentionPolicies.Create(config, rng, 12345L);
            var embellishment = NullEmbellishmentPolicy.Instance;
            var engine = new RumorEngine(config, retention, embellishment, channel, traitLookup, rng, 12345L, "player");

            var evt = new WorldEvent
            {
                EventId = "evt_old_secret",
                Origin = EventOrigin.Secret,
                Day = 1.0,
                SecretOwnerHeroIds = null, // Old shape!
                OriginatorHeroId = "lord_orig",
                KnownBy = new List<KnownByEntry>
                {
                    new KnownByEntry { HeroId = "dishonest_hero", Hop = 1, LearnedDay = 1.0 }
                }
            };

            var outcome = engine.TryLeak(evt, 5.0);
            Assert.True(outcome.Leaked);
            Assert.True(evt.State.Leaked);
            Assert.Equal("dishonest_hero", evt.State.LeakerHeroId);
        }
    }
}
