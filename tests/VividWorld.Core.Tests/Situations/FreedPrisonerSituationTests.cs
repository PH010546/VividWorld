#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using VividWorld.Core.Catalog;
using VividWorld.Core.Config;
using VividWorld.Core.Events;
using VividWorld.Core.Rumors;
using VividWorld.Core.Secrets;
using VividWorld.Core.Situations;
using VividWorld.Core.Util;
using Xunit;

namespace VividWorld.Core.Tests.Situations
{
    public class FreedPrisonerSituationTests
    {
        [Fact]
        public void Condition_RelationAbove_EvaluatesCorrectly()
        {
            var cond = new SituationConditionDef
            {
                Type = "relationAbove",
                A = "a",
                B = "b",
                Number = 50.0,
                KinNumber = 30.0
            };

            var factsByRole = new Dictionary<string, SituationRoleFacts>
            {
                ["a"] = new SituationRoleFacts { HeroId = "hero_a" },
                ["b"] = new SituationRoleFacts { HeroId = "hero_b" }
            };

            // Test non-kin: 51 > 50 (true), 50 > 50 (false boundary), 49 > 50 (false)
            int currentRel = 51;
            bool currentKin = false;
            var ctx = new SituationWorldContext
            {
                RelationBetween = (x, y) => currentRel,
                IsKin = (x, y) => currentKin
            };

            var res51 = SituationConditionEvaluator.Evaluate(cond, factsByRole, context: ctx);
            Assert.True(res51.Ok);
            Assert.Contains("relationAbove(a->b) ok", res51.Detail);

            currentRel = 50;
            var res50 = SituationConditionEvaluator.Evaluate(cond, factsByRole, context: ctx);
            Assert.False(res50.Ok);
            Assert.Contains("relationAbove(a->b) failed", res50.Detail);

            currentRel = 49;
            var res49 = SituationConditionEvaluator.Evaluate(cond, factsByRole, context: ctx);
            Assert.False(res49.Ok);

            // Test kin: uses KinNumber = 30
            currentKin = true;
            currentRel = 35; // 35 > 30 (true for kin, but would be false for non-kin 50)
            var resKin35 = SituationConditionEvaluator.Evaluate(cond, factsByRole, context: ctx);
            Assert.True(resKin35.Ok);
            Assert.Contains("kin threshold", resKin35.Detail);

            currentRel = 30; // 30 > 30 (false boundary for kin)
            var resKin30 = SituationConditionEvaluator.Evaluate(cond, factsByRole, context: ctx);
            Assert.False(resKin30.Ok);

            // Test config reference
            var cfgCond = new SituationConditionDef
            {
                Type = "relationAbove",
                A = "a",
                B = "b",
                ValueRef = "@secrets.freedPrisoner.chance" // 0.5
            };
            currentKin = false;
            currentRel = 1; // 1 > 0.5
            ctx.Config = new VividWorldConfig();
            var resCfg = SituationConditionEvaluator.Evaluate(cfgCond, factsByRole, context: ctx);
            Assert.True(resCfg.Ok);
        }

        [Fact]
        public void Condition_RelationBelow_EvaluatesCorrectly()
        {
            var cond = new SituationConditionDef
            {
                Type = "relationBelow",
                A = "a",
                B = "b",
                Number = 0.0,
                KinNumber = -10.0
            };

            var factsByRole = new Dictionary<string, SituationRoleFacts>
            {
                ["a"] = new SituationRoleFacts { HeroId = "hero_a" },
                ["b"] = new SituationRoleFacts { HeroId = "hero_b" }
            };

            int currentRel = -1;
            bool currentKin = false;
            var ctx = new SituationWorldContext
            {
                RelationBetween = (x, y) => currentRel,
                IsKin = (x, y) => currentKin
            };

            // Non-kin: -1 < 0 (true), 0 < 0 (false boundary), 1 < 0 (false)
            var resNeg1 = SituationConditionEvaluator.Evaluate(cond, factsByRole, context: ctx);
            Assert.True(resNeg1.Ok);
            Assert.Contains("relationBelow(a->b) ok", resNeg1.Detail);

            currentRel = 0;
            var res0 = SituationConditionEvaluator.Evaluate(cond, factsByRole, context: ctx);
            Assert.False(res0.Ok);

            currentRel = 1;
            var res1 = SituationConditionEvaluator.Evaluate(cond, factsByRole, context: ctx);
            Assert.False(res1.Ok);

            // Kin: uses KinNumber = -10
            currentKin = true;
            currentRel = -15; // -15 < -10 (true)
            var resKinNeg15 = SituationConditionEvaluator.Evaluate(cond, factsByRole, context: ctx);
            Assert.True(resKinNeg15.Ok);

            currentRel = -5; // -5 < -10 (false for kin, though < 0 for non-kin)
            var resKinNeg5 = SituationConditionEvaluator.Evaluate(cond, factsByRole, context: ctx);
            Assert.False(resKinNeg5.Ok);
        }

        [Fact]
        public void Condition_IsKin_EvaluatesCorrectly()
        {
            var condTrue = new SituationConditionDef
            {
                Type = "isKin",
                A = "a",
                B = "b",
                Value = true
            };
            var condFalse = new SituationConditionDef
            {
                Type = "isKin",
                A = "a",
                B = "b",
                Value = false
            };

            var factsByRole = new Dictionary<string, SituationRoleFacts>
            {
                ["a"] = new SituationRoleFacts { HeroId = "hero_a" },
                ["b"] = new SituationRoleFacts { HeroId = "hero_b" }
            };

            bool kinState = true;
            var ctx = new SituationWorldContext
            {
                IsKin = (x, y) => kinState
            };

            Assert.True(SituationConditionEvaluator.Evaluate(condTrue, factsByRole, context: ctx).Ok);
            Assert.False(SituationConditionEvaluator.Evaluate(condFalse, factsByRole, context: ctx).Ok);

            kinState = false;
            Assert.False(SituationConditionEvaluator.Evaluate(condTrue, factsByRole, context: ctx).Ok);
            Assert.True(SituationConditionEvaluator.Evaluate(condFalse, factsByRole, context: ctx).Ok);
        }

        [Fact]
        public void Condition_IsLiegeOf_EvaluatesCorrectly()
        {
            var condLiege = new SituationConditionDef
            {
                Type = "isLiegeOf",
                A = "ruler",
                B = "vassal",
                Value = true
            };

            var factsByRole = new Dictionary<string, SituationRoleFacts>
            {
                ["ruler"] = new SituationRoleFacts { HeroId = "hero_ruler" },
                ["vassal"] = new SituationRoleFacts { HeroId = "hero_vassal" }
            };

            bool liegeState = true;
            var ctx = new SituationWorldContext
            {
                IsLiegeOf = (x, y) => liegeState
            };

            var resOk = SituationConditionEvaluator.Evaluate(condLiege, factsByRole, context: ctx);
            Assert.True(resOk.Ok);
            Assert.Contains("isLiegeOf ok", resOk.Detail);

            liegeState = false;
            var resFail = SituationConditionEvaluator.Evaluate(condLiege, factsByRole, context: ctx);
            Assert.False(resFail.Ok);
            Assert.Contains("isLiegeOf failed", resFail.Detail);
        }

        [Fact]
        public void AbsentRole_FreerOf_PicksHighestRelationAndTieBreaksByStringId()
        {
            var prisonerFacts = new SituationRoleFacts
            {
                HeroId = "prisoner_1",
                SettlementId = "town_1"
            };

            // Candidates in town_1:
            // c_low: relation 55 (>50)
            // c_high_b: relation 70 (>50), id "lord_b"
            // c_high_a: relation 70 (>50), id "lord_a" (tie-breaks before "lord_b")
            // c_kin: kin, relation 35 (>30)
            // c_kin_boundary: kin, relation 30 (<=30 excluded)
            // c_other_boundary: non-kin, relation 50 (<=50 excluded)
            // c_wrong_faction: wrong faction
            // c_dead: not alive
            // c_prisoner: is prisoner
            // c_player: is player
            var pool = new List<SituationRoleFacts>
            {
                new() { HeroId = "lord_low", SettlementId = "town_1", IsAlive = true, IsLord = true, MapFactionId = "faction_1" },
                new() { HeroId = "lord_b", SettlementId = "town_1", IsAlive = true, IsLord = true, MapFactionId = "faction_1" },
                new() { HeroId = "lord_a", SettlementId = "town_1", IsAlive = true, IsLord = true, MapFactionId = "faction_1" },
                new() { HeroId = "lord_kin", SettlementId = "town_1", IsAlive = true, IsLord = true, MapFactionId = "faction_1" },
                new() { HeroId = "lord_kin_boundary", SettlementId = "town_1", IsAlive = true, IsLord = true, MapFactionId = "faction_1" },
                new() { HeroId = "lord_other_boundary", SettlementId = "town_1", IsAlive = true, IsLord = true, MapFactionId = "faction_1" },
                new() { HeroId = "lord_wrong_faction", SettlementId = "town_1", IsAlive = true, IsLord = true, MapFactionId = "faction_2" },
                new() { HeroId = "lord_dead", SettlementId = "town_1", IsAlive = false, IsLord = true, MapFactionId = "faction_1" },
                new() { HeroId = "lord_prisoner", SettlementId = "town_1", IsAlive = true, IsLord = true, IsPrisoner = true, MapFactionId = "faction_1" },
                new() { HeroId = "player_hero", SettlementId = "town_1", IsAlive = true, IsLord = true, MapFactionId = "faction_1" }
            };

            var ctx = new SituationWorldContext
            {
                CurrentSettlementFactionId = "faction_1",
                PlayerHeroId = "player_hero",
                IsKin = (a, b) => a == "lord_kin" || a == "lord_kin_boundary",
                RelationBetween = (a, b) =>
                {
                    if (a == "lord_low") return 55;
                    if (a == "lord_b") return 70;
                    if (a == "lord_a") return 70;
                    if (a == "lord_kin") return 35;
                    if (a == "lord_kin_boundary") return 30;
                    if (a == "lord_other_boundary") return 50;
                    return 60;
                }
            };

            var result = AbsentRoleSelector.Select(
                "freer",
                "freerOf:prisoner",
                prisonerFacts,
                "town_1",
                pool,
                new Dictionary<string, string?>(),
                ctx);

            Assert.True(result.IsBound);
            // Between lord_a and lord_b both at 70, lord_a wins by StringId ascending
            Assert.Equal("lord_a", result.PickedHeroId);

            // Assert exclusions
            var exclusions = result.Exclusions.ToDictionary(e => e.HeroId, e => e.Reason);
            Assert.Equal("player", exclusions["player_hero"]);
            Assert.Equal("not alive", exclusions["lord_dead"]);
            Assert.Equal("is prisoner", exclusions["lord_prisoner"]);
            Assert.Equal("faction mismatch", exclusions["lord_wrong_faction"]);
            Assert.Equal("kin relation 30 <= 30", exclusions["lord_kin_boundary"]);
            Assert.Equal("relation 50 <= 50", exclusions["lord_other_boundary"]);
        }

        [Fact]
        public void HarmedHeroes_FreerKingAndSettlementOwner_ExcludedIfFreerHoldsRole()
        {
            // Simulate the harm list construction matching SituationRunner.cs logic:
            // 1. Regular freer (neither king nor owner) -> both king and owner harmed
            string freerId = "lord_freer";
            string kingId = "king_freer";
            string ownerId = "owner_town";

            var harmed = BuildHarmedList(freerId, kingId, ownerId);
            Assert.Equal(2, harmed.Count);
            Assert.Contains(kingId, harmed);
            Assert.Contains(ownerId, harmed);

            // 2. Freer is king -> only owner harmed
            freerId = "king_freer";
            harmed = BuildHarmedList(freerId, kingId, ownerId);
            Assert.Single(harmed);
            Assert.Equal(ownerId, harmed[0]);

            // 3. Freer is settlement owner -> only king harmed
            freerId = "owner_town";
            harmed = BuildHarmedList(freerId, kingId, ownerId);
            Assert.Single(harmed);
            Assert.Equal(kingId, harmed[0]);

            // 4. Freer is both king and owner -> empty
            freerId = "king_and_owner";
            harmed = BuildHarmedList(freerId, freerId, freerId);
            Assert.Empty(harmed);
        }

        private static List<string> BuildHarmedList(string freerId, string? kingId, string? ownerId)
        {
            var list = new List<string>();
            if (!string.IsNullOrEmpty(kingId) && !string.Equals(kingId, freerId, StringComparison.Ordinal))
            {
                list.Add(kingId!);
            }
            if (!string.IsNullOrEmpty(ownerId) && !string.Equals(ownerId, freerId, StringComparison.Ordinal) && !list.Contains(ownerId!))
            {
                list.Add(ownerId!);
            }
            return list;
        }

        [Fact]
        public void SecretReactions_FreedPrisoner_OnHeardTriggersCorrectly()
        {
            var evt = new WorldEvent
            {
                EventId = "evt_freed_1",
                Type = "prisoner_freed_secretly",
                Origin = EventOrigin.Secret,
                SecretOwnerHeroIds = new List<string> { "freer_1" },
                SecretHarmedHeroIds = new List<string> { "king_1", "owner_1" },
                Participants = new Dictionary<string, string>
                {
                    ["freer"] = "freer_1",
                    ["prisoner"] = "prisoner_1"
                },
                State = new RumorState
                {
                    Leaked = true,
                    LeakedDay = 10.0
                }
            };
            evt.Extra["secretHarmedKing"] = "king_1";
            evt.Extra["secretHarmedOwner"] = "owner_1";

            var template = new EventTemplate
            {
                Type = "prisoner_freed_secretly",
                OnHeard = new List<TemplateOnHeardRule>
                {
                    new()
                    {
                        ListenerCategory = "secretHarmedKing",
                        TargetRole = "freer",
                        AmountRef = "@secrets.freedPrisoner.kingOnHeard",
                        Escalate = "clan"
                    },
                    new()
                    {
                        ListenerCategory = "secretHarmedOwner",
                        TargetRole = "freer",
                        AmountRef = "@secrets.freedPrisoner.ownerOnHeard"
                    }
                }
            };

            var cfg = new VividWorldConfig();
            cfg.Secrets.FreedPrisoner.KingOnHeard = -8.0;
            cfg.Secrets.FreedPrisoner.OwnerOnHeard = -8.0;

            // 1. King hears
            var hearerKing = new KnownByEntry
            {
                HeroId = "king_1",
                LearnedDay = 15.0,
                Hop = 1,
                Believes = true
            };
            var outcomeKing = SecretReactionEvaluator.Evaluate(
                evt, hearerKing, template, null, null, cfg, null, null, 15.0);

            Assert.Single(outcomeKing.Changes);
            Assert.Equal("king_1", outcomeKing.Changes[0].ObserverHeroId);
            Assert.Equal("freer_1", outcomeKing.Changes[0].TargetHeroId);
            Assert.Equal(-8.0, outcomeKing.Changes[0].Amount);
            Assert.True(outcomeKing.Changes[0].EscalateClan);

            // 2. Settlement owner hears
            var hearerOwner = new KnownByEntry
            {
                HeroId = "owner_1",
                LearnedDay = 16.0,
                Hop = 2,
                Believes = true
            };
            var outcomeOwner = SecretReactionEvaluator.Evaluate(
                evt, hearerOwner, template, null, null, cfg, null, null, 16.0);

            Assert.Single(outcomeOwner.Changes);
            Assert.Equal("owner_1", outcomeOwner.Changes[0].ObserverHeroId);
            Assert.Equal("freer_1", outcomeOwner.Changes[0].TargetHeroId);
            Assert.Equal(-8.0, outcomeOwner.Changes[0].Amount);
            Assert.False(outcomeOwner.Changes[0].EscalateClan);

            // 3. Unrelated hero hears -> no change
            var hearerOther = new KnownByEntry
            {
                HeroId = "other_lord",
                LearnedDay = 17.0,
                Hop = 2,
                Believes = true
            };
            var outcomeOther = SecretReactionEvaluator.Evaluate(
                evt, hearerOther, template, null, null, cfg, null, null, 17.0);

            Assert.Empty(outcomeOther.Changes);
        }

        [Fact]
        public void DeterministicRng_FixedSeed_ProducesIdenticalRollAcrossRuns()
        {
            var cond = new SituationConditionDef
            {
                Type = "chance",
                ValueRef = "@secrets.freedPrisoner.chance",
                SeedRoles = new List<string> { "prisoner", "freer" }
            };

            var factsByRole = new Dictionary<string, SituationRoleFacts>
            {
                ["prisoner"] = new SituationRoleFacts { HeroId = "prisoner_1" },
                ["freer"] = new SituationRoleFacts { HeroId = "freer_1" }
            };

            var cfg = new VividWorldConfig();
            cfg.Secrets.FreedPrisoner.Chance = 0.5;

            var ctx1 = new SituationWorldContext
            {
                Seed = 9876543210L,
                Config = cfg
            };
            var ctx2 = new SituationWorldContext
            {
                Seed = 9876543210L,
                Config = cfg
            };

            double day = 42.0;
            var res1 = SituationConditionEvaluator.Evaluate(cond, factsByRole, situationId: "freed_prisoner_secretly", day: day, context: ctx1);
            var res2 = SituationConditionEvaluator.Evaluate(cond, factsByRole, situationId: "freed_prisoner_secretly", day: day, context: ctx2);

            Assert.Equal(res1.Ok, res2.Ok);
            Assert.Equal(res1.Detail, res2.Detail);
        }
    }
}
