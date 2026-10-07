using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using VividWorld.Core.Catalog;
using VividWorld.Core.Config;
using VividWorld.Core.Diagnostics;
using VividWorld.Core.Events;
using VividWorld.Core.Persistence;
using VividWorld.Core.Presentation;
using VividWorld.Core.Rumors;
using VividWorld.Core.Situations;
using VividWorld.Core.Util;
using Xunit;

namespace VividWorld.Core.Tests
{
    public class AbsentRolesAndTriggerTests
    {
        private readonly PersistenceConfig _defaultPersistence = new() { MaxFactsPerEvent = 24 };

        private static string FindRepoFile(string relativePath)
        {
            string? current = AppContext.BaseDirectory;
            while (!string.IsNullOrEmpty(current))
            {
                if (File.Exists(Path.Combine(current, "VividWorld.sln")))
                {
                    string path = Path.Combine(current, relativePath);
                    if (File.Exists(path)) return path;
                }
                var parent = Directory.GetParent(current);
                if (parent == null) break;
                current = parent.FullName;
            }
            throw new FileNotFoundException($"Could not find '{relativePath}' relative to {AppContext.BaseDirectory}");
        }

        private sealed class TestTraitLookup : IHeroTraitLookup
        {
            private readonly Dictionary<string, TraitProfile> _profiles = new();

            public void Set(string heroId, TraitProfile profile) => _profiles[heroId] = profile;

            public TraitProfile? Of(string heroId)
            {
                return _profiles.TryGetValue(heroId, out var p) ? p : null;
            }
        }

        #region Absent Derived Roles

        [Fact]
        public void AbsentRoleSelector_GrudgeTarget_FindsEligibleHeroByGrudgeOrAffection()
        {
            var targetHero = new SituationRoleFacts
            {
                HeroId = "hero_a",
                ClanId = "clan_1",
                KingdomId = "kingdom_1",
                SettlementId = "settlement_town",
                IsAlive = true,
                IsLord = true
            };

            var enemyGrudge = new SituationRoleFacts
            {
                HeroId = "hero_enemy_grudge",
                ClanId = "clan_2",
                KingdomId = "kingdom_1",
                SettlementId = "settlement_castle", // absent from town
                IsAlive = true,
                IsLord = true
            };

            var enemyAffection = new SituationRoleFacts
            {
                HeroId = "hero_enemy_affection",
                ClanId = "clan_3",
                KingdomId = "kingdom_2",
                SettlementId = "settlement_village", // absent
                IsAlive = true,
                IsLord = true
            };

            var context = new SituationWorldContext
            {
                GrudgeLine = -5,
                NativeGrudgeLine = -20,
                GrudgeSum = (a, b) => (a == "hero_a" && b == "hero_enemy_grudge") ? -10 : 0,
                Affection = (a, b) => (a == "hero_a" && b == "hero_enemy_affection") ? -25 : 0
            };

            // When evaluating for enemyGrudge alone
            var boundHeroes = new Dictionary<string, string?> { ["hero_a"] = "hero_a" };
            var resultGrudge = AbsentRoleSelector.Select(
                "grudge_role",
                "grudgeTargetOf:hero_a",
                targetHero,
                currentSettlementId: "settlement_town",
                candidatePool: new[] { enemyGrudge },
                alreadyBoundHeroes: boundHeroes,
                context: context,
                situationId: "test_sit",
                day: 1.0);

            Assert.Equal("hero_enemy_grudge", resultGrudge.PickedHeroId);
            Assert.Null(resultGrudge.UnboundReason);

            // When evaluating for enemyAffection alone
            var resultAffection = AbsentRoleSelector.Select(
                "grudge_role",
                "grudgeTargetOf:hero_a",
                targetHero,
                currentSettlementId: "settlement_town",
                candidatePool: new[] { enemyAffection },
                alreadyBoundHeroes: boundHeroes,
                context: context,
                situationId: "test_sit",
                day: 1.0);

            Assert.Equal("hero_enemy_affection", resultAffection.PickedHeroId);
            Assert.Null(resultAffection.UnboundReason);
        }

        [Fact]
        public void AbsentRoleSelector_RivalClanLeader_FindsEligibleCandidate()
        {
            var targetHero = new SituationRoleFacts
            {
                HeroId = "hero_leader_a",
                ClanId = "clan_a",
                KingdomId = "kingdom_1",
                ClanTier = 4,
                IsClanLeader = true,
                SettlementId = "town_1",
                IsAlive = true,
                IsLord = true
            };

            var rivalHero = new SituationRoleFacts
            {
                HeroId = "hero_rival_b",
                ClanId = "clan_b",
                KingdomId = "kingdom_1", // same kingdom
                ClanTier = 5, // tier diff <= 1
                IsClanLeader = true,
                SettlementId = "town_2", // absent from town_1
                IsAlive = true,
                IsLord = true
            };

            var farRivalHero = new SituationRoleFacts
            {
                HeroId = "hero_far_c",
                ClanId = "clan_c",
                KingdomId = "kingdom_1",
                ClanTier = 2, // tier diff = 2 (> 1) -> excluded
                IsClanLeader = true,
                SettlementId = "town_2",
                IsAlive = true,
                IsLord = true
            };

            var boundHeroes = new Dictionary<string, string?> { ["leader_a"] = "hero_leader_a" };
            var result = AbsentRoleSelector.Select(
                "rival",
                "rivalClanLeaderOf:leader_a",
                targetHero,
                currentSettlementId: "town_1",
                candidatePool: new[] { rivalHero, farRivalHero },
                alreadyBoundHeroes: boundHeroes,
                context: null,
                situationId: "test_sit",
                day: 1.0);

            Assert.Equal("hero_rival_b", result.PickedHeroId);
            Assert.Equal(1, result.EligibleCandidateCount);
            Assert.Single(result.Exclusions);
            Assert.Equal("clan tier difference too large", result.Exclusions[0].Reason);
        }

        [Fact]
        public void AbsentRoleSelector_SelfOrKin_FindsSelfOrClanMember()
        {
            var targetHero = new SituationRoleFacts
            {
                HeroId = "hero_a",
                ClanId = "clan_1",
                KingdomId = "kingdom_1",
                SettlementId = "town_1",
                IsAlive = true,
                IsLord = true
            };

            var kinHero = new SituationRoleFacts
            {
                HeroId = "hero_kin",
                ClanId = "clan_1", // same clan
                KingdomId = "kingdom_1",
                SettlementId = "town_1", // can be present in same settlement
                IsAlive = true,
                IsLord = true
            };

            var otherHero = new SituationRoleFacts
            {
                HeroId = "hero_other",
                ClanId = "clan_2",
                KingdomId = "kingdom_1",
                SettlementId = "town_1",
                IsAlive = true,
                IsLord = true
            };

            var boundHeroes = new Dictionary<string, string?>();
            var result = AbsentRoleSelector.Select(
                "kin",
                "selfOrKinOf:actor",
                targetHero,
                currentSettlementId: "town_1",
                candidatePool: new[] { targetHero, kinHero, otherHero },
                alreadyBoundHeroes: boundHeroes,
                context: null,
                situationId: "test_sit",
                day: 1.0);

            Assert.True(result.PickedHeroId == "hero_a" || result.PickedHeroId == "hero_kin");
            Assert.Equal(2, result.EligibleCandidateCount);
        }

        [Fact]
        public void AbsentRoleSelector_RulerOf_FindsKingdomRuler()
        {
            var targetHero = new SituationRoleFacts
            {
                HeroId = "vassal_1",
                ClanId = "clan_vassal",
                KingdomId = "kingdom_vlandia",
                SettlementId = "town_1",
                IsAlive = true,
                IsLord = true
            };

            var rulerHero = new SituationRoleFacts
            {
                HeroId = "king_derthert",
                ClanId = "clan_royal",
                KingdomId = "kingdom_vlandia",
                IsKingdomLeader = true,
                SettlementId = "town_capital",
                IsAlive = true,
                IsLord = true
            };

            var boundHeroes = new Dictionary<string, string?> { ["vassal"] = "vassal_1" };
            var result = AbsentRoleSelector.Select(
                "ruler",
                "rulerOf:vassal",
                targetHero,
                currentSettlementId: "town_1",
                candidatePool: new[] { rulerHero },
                alreadyBoundHeroes: boundHeroes,
                context: null,
                situationId: "test_sit",
                day: 1.0);

            Assert.Equal("king_derthert", result.PickedHeroId);
            Assert.Equal(1, result.EligibleCandidateCount);
        }

        [Fact]
        public void AbsentRoleSelector_ExcludesPlayerDeadInSettlementAndBound()
        {
            var targetHero = new SituationRoleFacts
            {
                HeroId = "hero_a",
                ClanId = "clan_1",
                KingdomId = "kingdom_1",
                SettlementId = "town_1",
                IsAlive = true,
                IsLord = true
            };

            var playerCandidate = new SituationRoleFacts
            {
                HeroId = "player_hero",
                ClanId = "clan_2",
                IsPlayer = true,
                IsAlive = true,
                IsLord = true
            };

            var deadCandidate = new SituationRoleFacts
            {
                HeroId = "dead_hero",
                ClanId = "clan_2",
                IsAlive = false,
                IsLord = true
            };

            var inSettlementCandidate = new SituationRoleFacts
            {
                HeroId = "present_hero",
                ClanId = "clan_2",
                SettlementId = "town_1", // in settlement
                IsAlive = true,
                IsLord = true
            };

            var alreadyBoundCandidate = new SituationRoleFacts
            {
                HeroId = "bound_hero",
                ClanId = "clan_2",
                SettlementId = "town_2",
                IsAlive = true,
                IsLord = true
            };

            var context = new SituationWorldContext
            {
                GrudgeLine = -5,
                NativeGrudgeLine = -20,
                GrudgeSum = (a, b) => -10
            };

            var boundHeroes = new Dictionary<string, string?> { ["bound_role"] = "bound_hero" };
            var result = AbsentRoleSelector.Select(
                "enemy",
                "grudgeTargetOf:hero_a",
                targetHero,
                currentSettlementId: "town_1",
                candidatePool: new[] { playerCandidate, deadCandidate, inSettlementCandidate, alreadyBoundCandidate },
                alreadyBoundHeroes: boundHeroes,
                context: context,
                situationId: "test_sit",
                day: 1.0);

            Assert.Null(result.PickedHeroId);
            Assert.Equal(0, result.EligibleCandidateCount);
            Assert.Contains(result.Exclusions, e => e.HeroId == "player_hero" && e.Reason == "player");
            Assert.Contains(result.Exclusions, e => e.HeroId == "dead_hero" && e.Reason == "dead");
            Assert.Contains(result.Exclusions, e => e.HeroId == "present_hero" && e.Reason == "present in settlement");
            Assert.Contains(result.Exclusions, e => e.HeroId == "bound_hero" && e.Reason == "already bound");
        }

        [Fact]
        public void AbsentRoleSelector_DeterministicSelection_WhenMultipleCandidates()
        {
            var targetHero = new SituationRoleFacts
            {
                HeroId = "hero_a",
                ClanId = "clan_1",
                KingdomId = "kingdom_1",
                SettlementId = "town_1",
                IsAlive = true,
                IsLord = true
            };

            var candidates = Enumerable.Range(1, 10).Select(i => new SituationRoleFacts
            {
                HeroId = $"enemy_{i:D2}",
                ClanId = $"clan_{i}",
                SettlementId = "town_other",
                IsAlive = true,
                IsLord = true
            }).ToList();

            var context = new SituationWorldContext
            {
                Seed = 1001,
                GrudgeLine = -5,
                NativeGrudgeLine = -20,
                GrudgeSum = (a, b) => -10
            };

            var boundHeroes = new Dictionary<string, string?>();

            var result1 = AbsentRoleSelector.Select(
                "enemy",
                "grudgeTargetOf:hero_a",
                targetHero,
                currentSettlementId: "town_1",
                candidatePool: candidates,
                alreadyBoundHeroes: boundHeroes,
                context: context,
                situationId: "sit_grudge",
                day: 42.0);

            var result2 = AbsentRoleSelector.Select(
                "enemy",
                "grudgeTargetOf:hero_a",
                targetHero,
                currentSettlementId: "town_1",
                candidatePool: candidates,
                alreadyBoundHeroes: boundHeroes,
                context: context,
                situationId: "sit_grudge",
                day: 42.0);

            Assert.NotNull(result1.PickedHeroId);
            Assert.Equal(result1.PickedHeroId, result2.PickedHeroId);
        }

        #endregion

        #region New Conditions

        [Fact]
        public void Condition_TraitAtLeastAndAtMost_EvaluatesCorrectly()
        {
            var traits = new TestTraitLookup();
            traits.Set("hero_honorable", new TraitProfile
            {
                HeroId = "hero_honorable",
                Honor = 1,
                Mercy = -1,
                Valor = 2,
                Calculating = 0,
                Generosity = -2
            });

            var context = new SituationWorldContext { Traits = traits };
            var factsByRole = new Dictionary<string, SituationRoleFacts>
            {
                ["hero"] = new() { HeroId = "hero_honorable" }
            };

            // traitAtLeast Honor >= 1 -> Pass
            var condHonorPass = new SituationConditionDef { Type = "traitAtLeast", Role = "hero", Trait = "honor", Number = 1 };
            var evalHonorPass = SituationConditionEvaluator.Evaluate(condHonorPass, factsByRole, context: context);
            Assert.True(evalHonorPass.Ok);

            // traitAtLeast Honor >= 2 -> Fail
            var condHonorFail = new SituationConditionDef { Type = "traitAtLeast", Role = "hero", Trait = "honor", Number = 2 };
            var evalHonorFail = SituationConditionEvaluator.Evaluate(condHonorFail, factsByRole, context: context);
            Assert.False(evalHonorFail.Ok);

            // traitAtMost Mercy <= -1 -> Pass
            var condMercyPass = new SituationConditionDef { Type = "traitAtMost", Role = "hero", Trait = "mercy", Number = -1 };
            var evalMercyPass = SituationConditionEvaluator.Evaluate(condMercyPass, factsByRole, context: context);
            Assert.True(evalMercyPass.Ok);

            // traitAtMost Mercy <= -2 -> Fail
            var condMercyFail = new SituationConditionDef { Type = "traitAtMost", Role = "hero", Trait = "mercy", Number = -2 };
            var evalMercyFail = SituationConditionEvaluator.Evaluate(condMercyFail, factsByRole, context: context);
            Assert.False(evalMercyFail.Ok);
        }

        [Fact]
        public void Condition_Trait_FailsWhenProfileOrContextMissing()
        {
            var factsByRole = new Dictionary<string, SituationRoleFacts>
            {
                ["hero"] = new() { HeroId = "hero_unknown" }
            };

            var cond = new SituationConditionDef { Type = "traitAtLeast", Role = "hero", Trait = "honor", Number = 0 };

            // 1. Missing context
            var evalNoContext = SituationConditionEvaluator.Evaluate(cond, factsByRole, context: null);
            Assert.False(evalNoContext.Ok);
            Assert.Contains("missing context: hero traits unavailable", evalNoContext.Detail);

            // 2. Missing profile
            var context = new SituationWorldContext { Traits = new TestTraitLookup() };
            var evalNoProfile = SituationConditionEvaluator.Evaluate(cond, factsByRole, context: context);
            Assert.False(evalNoProfile.Ok);
            Assert.Contains("traits for 'hero_unknown' unavailable", evalNoProfile.Detail);
        }

        [Fact]
        public void Condition_HasGrudge_EvaluatesPersonalAndNativeGrudge()
        {
            var factsByRole = new Dictionary<string, SituationRoleFacts>
            {
                ["a"] = new() { HeroId = "hero_a" },
                ["b"] = new() { HeroId = "hero_b" },
                ["c"] = new() { HeroId = "hero_c" }
            };

            var context = new SituationWorldContext
            {
                GrudgeLine = -5,
                NativeGrudgeLine = -20,
                GrudgeSum = (from, to) => (from == "hero_a" && to == "hero_b") ? -8 : 0,
                Affection = (from, to) => (from == "hero_a" && to == "hero_c") ? -30 : 10
            };

            var condAb = new SituationConditionDef { Type = "hasGrudge", A = "a", B = "b" };
            var evalAb = SituationConditionEvaluator.Evaluate(condAb, factsByRole, context: context);
            Assert.True(evalAb.Ok);

            var condAc = new SituationConditionDef { Type = "hasGrudge", A = "a", B = "c" };
            var evalAc = SituationConditionEvaluator.Evaluate(condAc, factsByRole, context: context);
            Assert.True(evalAc.Ok);

            var condBa = new SituationConditionDef { Type = "hasGrudge", A = "b", B = "a" };
            var evalBa = SituationConditionEvaluator.Evaluate(condBa, factsByRole, context: context);
            Assert.False(evalBa.Ok);

            // Missing context
            var evalNoContext = SituationConditionEvaluator.Evaluate(condAb, factsByRole, context: null);
            Assert.False(evalNoContext.Ok);
            Assert.Contains("missing context", evalNoContext.Detail);
        }

        [Fact]
        public void Condition_Chance_DeterministicAndThresholdComparison()
        {
            var factsByRole = new Dictionary<string, SituationRoleFacts>
            {
                ["instigator"] = new() { HeroId = "lord_1" }
            };

            var context = new SituationWorldContext
            {
                Rng = new SplitMix64Rng(),
                Seed = 12345
            };

            var condChance = new SituationConditionDef
            {
                Type = "chance",
                Number = 0.5,
                SeedRoles = new List<string> { "instigator" }
            };

            // Evaluate twice with same inputs -> identical result
            var eval1 = SituationConditionEvaluator.Evaluate(condChance, factsByRole, context: context, situationId: "test_sit", day: 10);
            var eval2 = SituationConditionEvaluator.Evaluate(condChance, factsByRole, context: context, situationId: "test_sit", day: 10);

            Assert.Equal(eval1.Ok, eval2.Ok);
            Assert.Equal(eval1.Detail, eval2.Detail);
            Assert.Contains("chance roll ", eval1.Detail);

            // Missing context
            var evalNoContext = SituationConditionEvaluator.Evaluate(condChance, factsByRole, context: null);
            Assert.False(evalNoContext.Ok);
            Assert.Contains("missing context: rng unavailable", evalNoContext.Detail);
        }

        [Fact]
        public void Condition_KnowsEventAbout_EvaluatesMemoryCorrectly()
        {
            var factsByRole = new Dictionary<string, SituationRoleFacts>
            {
                ["knower"] = new() { HeroId = "hero_knower" },
                ["target"] = new() { HeroId = "hero_target" }
            };

            var context = new SituationWorldContext
            {
                RemembersEventAbout = (knowerId, types, aboutHeroId) =>
                    knowerId == "hero_knower" && aboutHeroId == "hero_target" && types.Contains("betrayal")
            };

            var condPass = new SituationConditionDef
            {
                Type = "knowsEventAbout",
                Role = "knower",
                About = "target",
                EventTypes = new List<string> { "betrayal", "murder" }
            };

            var evalPass = SituationConditionEvaluator.Evaluate(condPass, factsByRole, context: context);
            Assert.True(evalPass.Ok);

            var condFail = new SituationConditionDef
            {
                Type = "knowsEventAbout",
                Role = "knower",
                About = "target",
                EventTypes = new List<string> { "feast" }
            };

            var evalFail = SituationConditionEvaluator.Evaluate(condFail, factsByRole, context: context);
            Assert.False(evalFail.Ok);

            // Missing context
            var evalNoContext = SituationConditionEvaluator.Evaluate(condPass, factsByRole, context: null);
            Assert.False(evalNoContext.Ok);
            Assert.Contains("missing context: memory lookup unavailable", evalNoContext.Detail);
        }

        #endregion

        #region Catalog Loader & Cross Check

        [Fact]
        public void CatalogLoader_AfterEvent_ValidatesEventTypesAndBindFromEvent()
        {
            // Missing eventTypes
            string jsonMissingEventTypes = @"{
  ""situations"": [
    {
      ""id"": ""sit_after_test"",
      ""trigger"": ""afterEvent"",
      ""decider"": ""actor"",
      ""roles"": { ""actor"": { } },
      ""bindFromEvent"": { ""actor"": ""ACTOR"" }
    }
  ]
}";
            var cat1 = SituationCatalogLoader.Load(jsonMissingEventTypes);
            Assert.Contains(cat1.Issues, i => i.Code == SituationIssueCode.MissingEventTypes);

            // Missing bindFromEvent
            string jsonMissingBindFromEvent = @"{
  ""situations"": [
    {
      ""id"": ""sit_after_test"",
      ""trigger"": ""afterEvent"",
      ""decider"": ""actor"",
      ""eventTypes"": [""duel""],
      ""roles"": { ""actor"": { } }
    }
  ]
}";
            var cat2 = SituationCatalogLoader.Load(jsonMissingBindFromEvent);
            Assert.Contains(cat2.Issues, i => i.Code == SituationIssueCode.MissingBindFromEvent);

            // Non-derived role missing from bindFromEvent
            string jsonUnboundRole = @"{
  ""situations"": [
    {
      ""id"": ""sit_after_test"",
      ""trigger"": ""afterEvent"",
      ""decider"": ""actor"",
      ""eventTypes"": [""duel""],
      ""roles"": { ""actor"": { }, ""witness"": { } },
      ""bindFromEvent"": { ""actor"": ""ACTOR"" }
    }
  ]
}";
            var cat3 = SituationCatalogLoader.Load(jsonUnboundRole);
            Assert.Contains(cat3.Issues, i => i.Code == SituationIssueCode.MissingRoleInBindFromEvent && i.Detail.Contains("witness"));
        }

        [Fact]
        public void CatalogLoader_BackwardCompatibility_ValueTrueLoadsCorrectly()
        {
            string json = @"{
  ""situations"": [
    {
      ""id"": ""sit_compat"",
      ""trigger"": ""direct"",
      ""decider"": ""lord"",
      ""minBranchWeight"": 0.1,
      ""roles"": {
        ""lord"": { },
        ""other"": { }
      },
      ""conditions"": [
        { ""type"": ""isClanLeader"", ""role"": ""lord"", ""value"": true }
      ],
      ""branches"": [
        {
          ""id"": ""b1"",
          ""base"": 1.0,
          ""events"": [
            { ""type"": ""ev_test"", ""bind"": { ""ROLE_A"": ""lord"", ""ROLE_B"": ""other"" } }
          ]
        }
      ]
    }
  ]
}";
            var catalog = SituationCatalogLoader.Load(json);
            Assert.Empty(catalog.Issues.Where(i => i.IsError));
            Assert.NotEmpty(catalog.Situations);
            var cond = catalog.Situations[0].Conditions[0];
            Assert.True(cond.Value == true);
        }

        [Fact]
        public void CatalogCrossCheck_LinkTo_RequiresLinkedTemplateType()
        {
            string sitEventsPath = FindRepoFile(Path.Combine("module", "ModuleData", "vividworld_situation_events.json"));
            string sitEventsJson = File.ReadAllText(sitEventsPath);
            var sitEventCatalog = EventCatalogLoader.Load(sitEventsJson, _defaultPersistence);
            var mainCatalog = EventCatalogLoader.Load("[]", _defaultPersistence);

            var situationJson = @"{
  ""situations"": [
    {
      ""id"": ""sit_link_test"",
      ""trigger"": ""afterEvent"",
      ""decider"": ""slighted"",
      ""minBranchWeight"": 0.1,
      ""eventTypes"": [""duel""],
      ""bindFromEvent"": { ""slighted"": ""SLIGHTED"", ""favored"": ""FAVORED"" },
      ""roles"": { ""slighted"": { }, ""favored"": { } },
      ""branches"": [
        {
          ""id"": ""b1"",
          ""base"": 1.0,
          ""events"": [
            {
              ""type"": ""seat_dispute_demanded"",
              ""linkTo"": ""@trigger"",
              ""bind"": { ""SLIGHTED"": ""slighted"", ""FAVORED"": ""favored"", ""SETTLEMENT"": ""@settlement"" }
            }
          ]
        }
      ]
    }
  ]
}";
            var sitCatalog = SituationCatalogLoader.Load(situationJson);
            Assert.Empty(sitCatalog.Issues.Where(i => i.IsError));

            var issues = SituationCatalogCrossCheck.Check(sitCatalog, sitEventCatalog, mainCatalog);
            Assert.Contains(issues, i => i.Code == SituationIssueCode.LinkToRequiresLinkedTemplateType);
        }

        #endregion

        #region Per-Situation MaxPerDay

        [Fact]
        public void SituationQuota_PerSituation_RollsSeparateQuota()
        {
            long campaignSeed = 987654321;
            int dayBucket = 12;

            long sitSeedA = RumorSeed.Of(campaignSeed, "situation.scan", dayBucket, "quota", "situation_special_a");
            long sitSeedB = RumorSeed.Of(campaignSeed, "situation.scan", dayBucket, "quota", "situation_special_b");

            var rng = new SplitMix64Rng();

            var rollA = SituationQuota.Roll(1.5, rng, sitSeedA);
            var rollB = SituationQuota.Roll(1.5, rng, sitSeedB);

            // Quotas are deterministic for each situation ID
            Assert.Equal(rollA.Quota, SituationQuota.Roll(1.5, rng, sitSeedA).Quota);
            Assert.Equal(rollB.Quota, SituationQuota.Roll(1.5, rng, sitSeedB).Quota);
            Assert.True(rollA.Quota >= 1 && rollA.Quota <= 2);
        }

        #endregion

        #region Happened situations (misconduct) and engine extensions

        [Fact]
        public void ConfigReferences_ValidAndInvalidReferences_ParsedOrReported()
        {
            string validJson = @"{
  ""situations"": [
    {
      ""id"": ""sit_cfg_ref"",
      ""enabledBy"": ""@falseRumors.enabled"",
      ""trigger"": ""direct"",
      ""maxPerDay"": ""@falseRumors.misconductPerDay"",
      ""decider"": ""speaker"",
      ""minBranchWeight"": 0.1,
      ""roles"": {
        ""speaker"": { },
        ""listener"": { },
        ""enemy"": {
          ""derived"": ""grudgeHolderAgainst:speaker"",
          ""grudgeLine"": ""@falseRumors.poisonGrudgeLine"",
          ""nativeGrudgeLine"": ""@falseRumors.poisonNativeGrudgeLine""
        }
      },
      ""conditions"": [
        {
          ""type"": ""chance"",
          ""value"": ""@falseRumors.captureMisconductChance"",
          ""seedRoles"": [""speaker""]
        }
      ],
      ""branches"": [
        {
          ""id"": ""b1"",
          ""base"": 1.0,
          ""events"": []
        }
      ]
    }
  ]
}";
            var validCat = SituationCatalogLoader.Load(validJson);
            Assert.Empty(validCat.Issues.Where(i => i.IsError));
            var sit = validCat.Situations[0];
            Assert.Equal("@falseRumors.enabled", sit.EnabledBy);
            Assert.Equal("@falseRumors.misconductPerDay", sit.MaxPerDayRef);
            var roleDef = sit.Roles["enemy"];
            Assert.Equal("@falseRumors.poisonGrudgeLine", roleDef.GrudgeLineRef);
            Assert.Equal("@falseRumors.poisonNativeGrudgeLine", roleDef.NativeGrudgeLineRef);
            var cond = sit.Conditions[0];
            Assert.Equal("@falseRumors.captureMisconductChance", cond.ValueRef);

            // Dynamic resolution check
            var cfg = new VividWorldConfig();
            cfg.FalseRumors.Enabled = true;
            cfg.FalseRumors.MisconductPerDay = 0.25;
            cfg.FalseRumors.CaptureMisconductChance = 0.45;
            cfg.FalseRumors.PoisonGrudgeLine = -15;
            cfg.FalseRumors.PoisonNativeGrudgeLine = -35;

            Assert.True(sit.IsEnabled(cfg));
            Assert.Equal(0.25, sit.GetMaxPerDay(cfg));

            // Changing config changes value without re-parsing
            cfg.FalseRumors.Enabled = false;
            cfg.FalseRumors.MisconductPerDay = 0.50;
            Assert.False(sit.IsEnabled(cfg));
            Assert.Equal(0.50, sit.GetMaxPerDay(cfg));

            // Invalid reference
            string invalidJson = @"{
  ""situations"": [
    {
      ""id"": ""sit_bad_ref"",
      ""enabledBy"": ""@falseRumors.nonExistentKey"",
      ""trigger"": ""direct"",
      ""decider"": ""speaker"",
      ""minBranchWeight"": 0.1,
      ""roles"": { ""speaker"": { } },
      ""branches"": [ { ""id"": ""b1"", ""base"": 1.0, ""events"": [] } ]
    }
  ]
}";
            var invalidCat = SituationCatalogLoader.Load(invalidJson);
            Assert.Contains(invalidCat.Issues, i => i.Code == SituationIssueCode.InvalidConfigReference);
        }

        [Fact]
        public void IsLordCondition_EvaluatesCorrectly()
        {
            var cond = new SituationConditionDef
            {
                Type = "isLord",
                Role = "actor"
            };

            var lordFacts = new SituationRoleFacts { HeroId = "hero_lord", IsLord = true };
            var nonLordFacts = new SituationRoleFacts { HeroId = "hero_commoner", IsLord = false };

            var evalLord = SituationConditionEvaluator.Evaluate(cond, new Dictionary<string, SituationRoleFacts> { ["actor"] = lordFacts });
            Assert.True(evalLord.Ok);
            Assert.Equal("isLord(actor)", evalLord.Label);

            var evalNonLord = SituationConditionEvaluator.Evaluate(cond, new Dictionary<string, SituationRoleFacts> { ["actor"] = nonLordFacts });
            Assert.False(evalNonLord.Ok);
            Assert.Equal("isLord(actor)", evalNonLord.Label);
            Assert.Contains("not a lord", evalNonLord.Detail);
        }

        [Fact]
        public void AbsentRoleSelector_GrudgeHolderAgainst_SelectsEligibleCandidatesAndExcludesUnqualified()
        {
            var targetHero = new SituationRoleFacts
            {
                HeroId = "hero_target",
                ClanId = "clan_1",
                KingdomId = "kingdom_1",
                SettlementId = "settlement_town",
                IsLord = true,
                IsAlive = true
            };

            var traitLookup = new TestTraitLookup();
            // Candidate 1: deep grudge, dishonorable
            traitLookup.Set("hero_grudger", new TraitProfile { Honor = -1, Mercy = -1 });
            // Candidate 2: native hostility, but honorable
            traitLookup.Set("hero_noble_enemy", new TraitProfile { Honor = 1, Mercy = 1 });
            // Candidate 3: neutral
            traitLookup.Set("hero_neutral", new TraitProfile { Honor = -1, Mercy = -1 });
            // Candidate 4: same settlement candidate (allowed for grudgeHolderAgainst!)
            traitLookup.Set("hero_colocated", new TraitProfile { Honor = -1, Mercy = 0 });

            var pool = new List<SituationRoleFacts>
            {
                new() { HeroId = "hero_grudger", ClanId = "clan_2", KingdomId = "kingdom_1", SettlementId = "settlement_castle", IsLord = true, IsAlive = true },
                new() { HeroId = "hero_noble_enemy", ClanId = "clan_3", KingdomId = "kingdom_1", SettlementId = "settlement_town", IsLord = true, IsAlive = true },
                new() { HeroId = "hero_neutral", ClanId = "clan_4", KingdomId = "kingdom_1", SettlementId = "settlement_castle", IsLord = true, IsAlive = true },
                new() { HeroId = "hero_colocated", ClanId = "clan_5", KingdomId = "kingdom_1", SettlementId = "settlement_town", IsLord = true, IsAlive = true },
                new() { HeroId = "hero_dead", ClanId = "clan_6", KingdomId = "kingdom_1", SettlementId = "settlement_castle", IsLord = true, IsAlive = false },
                new() { HeroId = "hero_player", ClanId = "clan_player", KingdomId = "kingdom_1", SettlementId = "settlement_castle", IsLord = true, IsAlive = true },
                new() { HeroId = "hero_commoner", ClanId = "clan_7", KingdomId = "kingdom_1", SettlementId = "settlement_castle", IsLord = false, IsAlive = true }
            };

            var context = new SituationWorldContext
            {
                GrudgeSum = (a, b) =>
                {
                    // Candidate to target
                    if (a == "hero_grudger" && b == "hero_target") return -12;
                    if (a == "hero_colocated" && b == "hero_target") return -15;
                    return 0;
                },
                Affection = (a, b) =>
                {
                    if (a == "hero_noble_enemy" && b == "hero_target") return -35;
                    return 0;
                },
                Traits = traitLookup,
                PlayerHeroId = "hero_player",
                Rng = new SplitMix64Rng(),
                Seed = 12345
            };

            var roleDef = new SituationRoleDef
            {
                Derived = "grudgeHolderAgainst:target",
                GrudgeLine = -10,
                NativeGrudgeLine = -30,
                AnyTraitAtMost = new Dictionary<string, int>
                {
                    ["honor"] = -1,
                    ["mercy"] = -1
                }
            };

            var result = AbsentRoleSelector.Select(
                derivedRoleName: "poisoner",
                derivedStrategy: "grudgeHolderAgainst:target",
                targetRoleFacts: targetHero,
                currentSettlementId: "settlement_town",
                candidatePool: pool,
                alreadyBoundHeroes: new Dictionary<string, string?> { ["target"] = "hero_target" },
                context: context,
                situationId: "poisoned_secretly",
                day: 10.0,
                roleDef: roleDef);

            // hero_grudger (grudge <= -10, honor <= -1) -> eligible
            // hero_colocated (grudge <= -10, in same settlement, honor <= -1) -> eligible!
            // hero_noble_enemy (affection <= -30, but honor=1, mercy=1, fails anyTraitAtMost) -> excluded (personality)
            // hero_neutral (grudge=0, affection=0) -> excluded (no grudge)
            // hero_dead -> excluded (dead)
            // hero_player -> excluded (player)
            // hero_commoner -> excluded (not lord)
            Assert.Equal(2, result.EligibleCandidateCount);
            Assert.Contains(result.EligibleCandidates, c => c.HeroId == "hero_grudger");
            Assert.Contains(result.EligibleCandidates, c => c.HeroId == "hero_colocated");
            Assert.NotNull(result.PickedHeroId);
            Assert.True(result.PickedHeroId == "hero_grudger" || result.PickedHeroId == "hero_colocated");
        }

        [Fact]
        public void ForcedMode_BypassesConditionsAndPrefersBranchesWithEvents()
        {
            var condChance = new SituationConditionDef
            {
                Type = "chance",
                Number = 0.0, // 0% chance normally fails
                SeedRoles = new List<string> { "a" }
            };
            var condTrait = new SituationConditionDef
            {
                Type = "traitAtMost",
                Role = "a",
                Trait = "honor",
                Number = -1
            };
            var condCooldown = new SituationConditionDef
            {
                Type = "cooldown",
                Days = 30
            };

            var facts = new Dictionary<string, SituationRoleFacts>
            {
                ["a"] = new SituationRoleFacts { HeroId = "hero_a", IsLord = true }
            };
            var traitLookup = new TestTraitLookup();
            traitLookup.Set("hero_a", new TraitProfile { Honor = 1 }); // fails honor <= -1

            var context = new SituationWorldContext { Traits = traitLookup };

            // In normal mode:
            var normalChance = SituationConditionEvaluator.Evaluate(condChance, facts, context: context, forced: false);
            Assert.False(normalChance.Ok);
            var normalTrait = SituationConditionEvaluator.Evaluate(condTrait, facts, context: context, forced: false);
            Assert.False(normalTrait.Ok);

            // In forced mode:
            var forcedChance = SituationConditionEvaluator.Evaluate(condChance, facts, context: context, forced: true);
            Assert.True(forcedChance.Ok);
            Assert.Contains("(bypassed: forced)", forcedChance.Detail);

            var forcedTrait = SituationConditionEvaluator.Evaluate(condTrait, facts, context: context, forced: true);
            Assert.True(forcedTrait.Ok);
            Assert.Contains("(bypassed: forced)", forcedTrait.Detail);

            var forcedCooldown = SituationConditionEvaluator.Evaluate(condCooldown, facts, context: context, forced: true);
            Assert.True(forcedCooldown.Ok);
            Assert.Contains("(bypassed: forced)", forcedCooldown.Detail);

            // Branch selection in forced mode prefers branches with events
            var branches = new List<SituationBranchDef>
            {
                new()
                {
                    Id = "empty_branch",
                    Base = 100.0,
                    Events = new List<SituationBranchEventDef>()
                },
                new()
                {
                    Id = "event_branch",
                    Base = 1.0,
                    Events = new List<SituationBranchEventDef> { new() { Type = "some_event" } }
                }
            };
            var testTemplate = new SituationTemplate
            {
                Id = "test_sit",
                Branches = branches
            };
            var rng = new SplitMix64Rng();
            var pickNormal = SituationBranchSelector.Select(
                testTemplate,
                facts,
                boundHeroes: null,
                unboundReasons: null,
                deciderTraits: traitLookup.Of("hero_a")!,
                defaultMinBranchWeight: 0.1,
                rng: rng,
                seed: 100L,
                forced: false);
            Assert.Equal("empty_branch", pickNormal.SelectedBranchId);

            var pickForced = SituationBranchSelector.Select(
                testTemplate,
                facts,
                boundHeroes: null,
                unboundReasons: null,
                deciderTraits: traitLookup.Of("hero_a")!,
                defaultMinBranchWeight: 0.1,
                rng: rng,
                seed: 100L,
                forced: true);
            Assert.Equal("event_branch", pickForced.SelectedBranchId);
            var forcedOut = pickForced.Branches.Single(b => b.BranchId == "empty_branch");
            Assert.True(forcedOut.IsExcluded);
            Assert.Contains("forced", forcedOut.ExcludedReason);
            Assert.False(pickNormal.Branches.Single(b => b.BranchId == "empty_branch").IsExcluded);
        }

        [Fact]
        public void WitnessSource_TemplateBinderAndEnumerator_BehavesPerSpec()
        {
            var colocatedPublic = new EventTemplate
            {
                Type = "colocated_evt",
                Origin = EventOrigin.Public,
                WitnessSource = "colocated",
                Roles = new Dictionary<string, string> { ["actor"] = "{ACTOR}" }
            };
            var nonePublic = new EventTemplate
            {
                Type = "none_evt",
                Origin = EventOrigin.Public,
                WitnessSource = "none",
                Roles = new Dictionary<string, string> { ["actor"] = "{ACTOR}" }
            };
            var captorArmyPublic = new EventTemplate
            {
                Type = "army_evt",
                Origin = EventOrigin.Public,
                WitnessSource = "triggerCaptorArmy",
                Roles = new Dictionary<string, string> { ["actor"] = "{ACTOR}" }
            };
            var secretEvt = new EventTemplate
            {
                Type = "secret_evt",
                Origin = EventOrigin.Secret,
                WitnessSource = "none",
                Roles = new Dictionary<string, string> { ["actor"] = "{ACTOR}" }
            };

            // TemplateBinder AutoResolveWitnesses
            Assert.True(TemplateBinder.AutoResolveWitnesses(colocatedPublic));
            Assert.False(TemplateBinder.AutoResolveWitnesses(nonePublic));
            Assert.False(TemplateBinder.AutoResolveWitnesses(captorArmyPublic));
            Assert.False(TemplateBinder.AutoResolveWitnesses(secretEvt));

            // SentenceCombinationEnumerator DeadRoles check
            Assert.True(SentenceCombinationEnumerator.IsDeadRole("conduct_poisoned", "victim"));
        }

        [Fact]
        public void WorldEvent_CaptorArmyLeaderHeroIds_SerializationOmitsWhenEmpty()
        {
            var evtWithArmy = new WorldEvent
            {
                EventId = "evt_army_1",
                Type = "hero_taken_prisoner",
                Day = 1.0,
                Origin = EventOrigin.Public,
                CaptorArmyLeaderHeroIds = new List<string> { "lord_b", "lord_c" }
            };

            string jsonWithArmy = VividJson.Write(evtWithArmy);
            Assert.Contains("captorArmyLeaderHeroIds", jsonWithArmy);

            var readBackWithArmy = VividJson.Read<WorldEvent>(jsonWithArmy);
            Assert.NotNull(readBackWithArmy?.CaptorArmyLeaderHeroIds);
            Assert.Equal(2, readBackWithArmy!.CaptorArmyLeaderHeroIds!.Count);
            Assert.Equal("lord_b", readBackWithArmy.CaptorArmyLeaderHeroIds[0]);

            var evtWithoutArmy = new WorldEvent
            {
                EventId = "evt_army_2",
                Type = "hero_taken_prisoner",
                Day = 1.0,
                Origin = EventOrigin.Public,
                CaptorArmyLeaderHeroIds = null
            };

            string jsonWithout = VividJson.Write(evtWithoutArmy);
            Assert.DoesNotContain("captorArmyLeaderHeroIds", jsonWithout);

            // Backward compatibility: old JSON without the field
            string oldJson = @"{ ""eventId"": ""evt_old"", ""type"": ""hero_taken_prisoner"", ""day"": 1.0, ""origin"": 1 }";
            var readBackOld = VividJson.Read<WorldEvent>(oldJson);
            Assert.NotNull(readBackOld);
            Assert.Null(readBackOld!.CaptorArmyLeaderHeroIds);
        }

        [Fact]
        public void ShippedCatalogs_AllFiveFalse1dSituationsAndEvents_LoadWithoutErrors()
        {
            string sitEventsPath = FindRepoFile(Path.Combine("module", "ModuleData", "vividworld_situation_events.json"));
            string sitPath = FindRepoFile(Path.Combine("module", "ModuleData", "vividworld_situations.json"));
            string eventsPath = FindRepoFile(Path.Combine("module", "ModuleData", "vividworld_events.json"));

            var sitEventCatalog = EventCatalogLoader.Load(File.ReadAllText(sitEventsPath), _defaultPersistence);
            var sitCatalog = SituationCatalogLoader.Load(File.ReadAllText(sitPath));
            var mainCatalog = EventCatalogLoader.Load(File.ReadAllText(eventsPath), _defaultPersistence);

            Assert.Empty(sitEventCatalog.Issues.Where(i => i.IsError));
            Assert.Empty(sitCatalog.Issues.Where(i => i.IsError));
            Assert.Empty(mainCatalog.Issues.Where(i => i.IsError));

            var crossIssues = SituationCatalogCrossCheck.Check(sitCatalog, sitEventCatalog, mainCatalog);
            Assert.Empty(crossIssues.Where(i => i.IsError));

            // Verify the 5 situations exist
            var situationIds = new[] { "spoke_against_ruler", "refused_aid", "rash_capture", "mistreated_prisoner", "poisoned_secretly" };
            foreach (var id in situationIds)
            {
                var s = sitCatalog.Situations.FirstOrDefault(sit => sit.Id == id);
                Assert.NotNull(s);
                Assert.Equal("@falseRumors.enabled", s!.EnabledBy);
            }

            // Verify the 5 events exist
            var eventTypes = new[] { "conduct_spoke_against_ruler", "conduct_mistreated_prisoner", "conduct_refused_aid", "conduct_rash_capture", "conduct_poisoned" };
            foreach (var t in eventTypes)
            {
                var evtT = sitEventCatalog.Templates.FirstOrDefault(templ => templ.Type == t);
                Assert.NotNull(evtT);
            }
        }

        [Fact]
        public void SampleFormatter_OutputsFiveTypesWithoutConcat()
        {
            string root = FindRepoFile("VividWorld.sln");
            root = Path.GetDirectoryName(root)!;
            var pCfg = new PersistenceConfig();
            var all = new List<EventTemplate>();
            foreach (var f in new[] { "vividworld_events.json", "vividworld_situation_events.json" })
            {
                all.AddRange(EventCatalogLoader.Load(File.ReadAllText(Path.Combine(root, "module", "ModuleData", f)), pCfg).Templates);
            }
            var en = EnglishStringTable.LoadFromFile(Path.Combine(root, "module", "ModuleData", "Languages", "std_module_strings_xml.xml"));
            var cnt = EnglishStringTable.LoadFromFile(Path.Combine(root, "module", "ModuleData", "Languages", "CNt", "std_module_strings_xml.xml"));
            string output = TemplateRenderSampleFormatter.FormatAll(all, en, cnt);

            Assert.DoesNotContain("[Concat]", output);
        }

        [Fact]
        public void RoleDef_AllowDead_ParsedCorrectly()
        {
            string json = @"{
  ""situations"": [
    {
      ""id"": ""test_allow_dead"",
      ""trigger"": ""afterEvent"",
      ""decider"": ""killer"",
      ""minBranchWeight"": 0.1,
      ""eventTypes"": [""hero_died_of_old_age""],
      ""bindFromEvent"": { ""victim"": ""VICTIM"", ""killer"": ""KILLER"" },
      ""roles"": {
        ""victim"": { ""allowDead"": true },
        ""killer"": { ""allowDead"": false }
      },
      ""branches"": [
        { ""id"": ""b1"", ""base"": 1.0, ""events"": [] }
      ]
    }
  ]
}";
            var cat = SituationCatalogLoader.Load(json);
            Assert.Empty(cat.Issues.Where(i => i.IsError));
            Assert.True(cat.Situations[0].Roles["victim"].AllowDead);
            Assert.False(cat.Situations[0].Roles["killer"].AllowDead);
        }

        [Fact]
        public void ShippedSituations_FiveSituations_TriggerConditionsAndExclusions()
        {
            string sitPath = FindRepoFile(Path.Combine("module", "ModuleData", "vividworld_situations.json"));
            var sitCatalog = SituationCatalogLoader.Load(File.ReadAllText(sitPath));

            var sitSpoke = sitCatalog.Situations.First(s => s.Id == "spoke_against_ruler");
            var sitRefused = sitCatalog.Situations.First(s => s.Id == "refused_aid");
            var sitRash = sitCatalog.Situations.First(s => s.Id == "rash_capture");
            var sitMistreated = sitCatalog.Situations.First(s => s.Id == "mistreated_prisoner");
            var sitPoison = sitCatalog.Situations.First(s => s.Id == "poisoned_secretly");

            var traits = new TestTraitLookup();
            traits.Set("speaker_dishonorable", new TraitProfile { Honor = -1 });
            traits.Set("speaker_honorable", new TraitProfile { Honor = 1 });
            traits.Set("refuser_stingy", new TraitProfile { Generosity = -1 });
            traits.Set("refuser_generous", new TraitProfile { Generosity = 1 });
            traits.Set("prisoner_rash", new TraitProfile { Calculating = -1 });
            traits.Set("prisoner_calm", new TraitProfile { Calculating = 1 });
            traits.Set("captor_cruel", new TraitProfile { Mercy = -1 });
            traits.Set("captor_merciful", new TraitProfile { Mercy = 1 });

            var ctx = new SituationWorldContext { Traits = traits };

            // 1. spoke_against_ruler: speaker honor <= -1
            var factsSpokeOk = new Dictionary<string, SituationRoleFacts>
            {
                ["speaker"] = new() { HeroId = "speaker_dishonorable", IsLord = true, SettlementId = "town_1", SettlementKind = "town", KingdomId = "k1", ClanId = "c1" },
                ["listener"] = new() { HeroId = "listener_1", IsLord = true, SettlementId = "town_1", SettlementKind = "town", KingdomId = "k1", ClanId = "c2" },
                ["ruler"] = new() { HeroId = "ruler_1", IsLord = true, ClanId = "c_ruler" }
            };
            var factsSpokeExcluded = new Dictionary<string, SituationRoleFacts>
            {
                ["speaker"] = new() { HeroId = "speaker_honorable", IsLord = true, SettlementId = "town_1", SettlementKind = "town", KingdomId = "k1", ClanId = "c1" },
                ["listener"] = new() { HeroId = "listener_1", IsLord = true, SettlementId = "town_1", SettlementKind = "town", KingdomId = "k1", ClanId = "c2" },
                ["ruler"] = new() { HeroId = "ruler_1", IsLord = true, ClanId = "c_ruler" }
            };
            var boundSpoke = new Dictionary<string, string?>
            {
                ["speaker"] = "speaker_dishonorable",
                ["listener"] = "listener_1",
                ["ruler"] = "ruler_1"
            };

            Assert.All(sitSpoke.Conditions, c => Assert.True(SituationConditionEvaluator.Evaluate(c, factsSpokeOk, boundHeroes: boundSpoke, context: ctx).Ok));
            Assert.Contains(sitSpoke.Conditions, c => !SituationConditionEvaluator.Evaluate(c, factsSpokeExcluded, boundHeroes: boundSpoke, context: ctx).Ok);

            // 2. refused_aid: refuser generosity <= -1
            var factsRefusedOk = new Dictionary<string, SituationRoleFacts>
            {
                ["asker"] = new() { HeroId = "asker_1", IsLord = true, SettlementId = "town_1", SettlementKind = "town", KingdomId = "k1" },
                ["refuser"] = new() { HeroId = "refuser_stingy", IsLord = true, SettlementId = "town_1", SettlementKind = "town", KingdomId = "k1" }
            };
            var factsRefusedExcluded = new Dictionary<string, SituationRoleFacts>
            {
                ["asker"] = new() { HeroId = "asker_1", IsLord = true, SettlementId = "town_1", SettlementKind = "town", KingdomId = "k1" },
                ["refuser"] = new() { HeroId = "refuser_generous", IsLord = true, SettlementId = "town_1", SettlementKind = "town", KingdomId = "k1" }
            };

            Assert.All(sitRefused.Conditions, c => Assert.True(SituationConditionEvaluator.Evaluate(c, factsRefusedOk, context: ctx).Ok));
            Assert.Contains(sitRefused.Conditions, c => !SituationConditionEvaluator.Evaluate(c, factsRefusedExcluded, context: ctx).Ok);

            // 3. rash_capture: prisoner calculating <= -1
            var factsRashOk = new Dictionary<string, SituationRoleFacts>
            {
                ["captor"] = new() { HeroId = "captor_1", IsLord = true },
                ["prisoner"] = new() { HeroId = "prisoner_rash", IsLord = true }
            };
            var factsRashExcluded = new Dictionary<string, SituationRoleFacts>
            {
                ["captor"] = new() { HeroId = "captor_1", IsLord = true },
                ["prisoner"] = new() { HeroId = "prisoner_calm", IsLord = true }
            };

            var condTraitRash = sitRash.Conditions.First(c => c.Type == "traitAtMost");
            Assert.True(SituationConditionEvaluator.Evaluate(condTraitRash, factsRashOk, context: ctx).Ok);
            Assert.False(SituationConditionEvaluator.Evaluate(condTraitRash, factsRashExcluded, context: ctx).Ok);

            // 4. mistreated_prisoner: captor mercy <= -1
            var factsMistreatOk = new Dictionary<string, SituationRoleFacts>
            {
                ["captor"] = new() { HeroId = "captor_cruel", IsLord = true },
                ["prisoner"] = new() { HeroId = "prisoner_1", IsLord = true }
            };
            var factsMistreatExcluded = new Dictionary<string, SituationRoleFacts>
            {
                ["captor"] = new() { HeroId = "captor_merciful", IsLord = true },
                ["prisoner"] = new() { HeroId = "prisoner_1", IsLord = true }
            };

            var condTraitMistreat = sitMistreated.Conditions.First(c => c.Type == "traitAtMost");
            Assert.True(SituationConditionEvaluator.Evaluate(condTraitMistreat, factsMistreatOk, context: ctx).Ok);
            Assert.False(SituationConditionEvaluator.Evaluate(condTraitMistreat, factsMistreatExcluded, context: ctx).Ok);

            // 5. poisoned_secretly: victim is lord
            var factsPoisonOk = new Dictionary<string, SituationRoleFacts>
            {
                ["victim"] = new() { HeroId = "victim_1", IsLord = true, IsAlive = false },
                ["poisoner"] = new() { HeroId = "poisoner_1", IsLord = true }
            };
            var factsPoisonExcluded = new Dictionary<string, SituationRoleFacts>
            {
                ["victim"] = new() { HeroId = "victim_1", IsLord = false, IsAlive = false },
                ["poisoner"] = new() { HeroId = "poisoner_1", IsLord = true }
            };

            var condLordPoison = sitPoison.Conditions.First(c => c.Type == "isLord");
            Assert.True(SituationConditionEvaluator.Evaluate(condLordPoison, factsPoisonOk, context: ctx).Ok);
            Assert.False(SituationConditionEvaluator.Evaluate(condLordPoison, factsPoisonExcluded, context: ctx).Ok);
        }

        [Fact]
        public void PoisonedEvent_IsSecret_Hop0HasOnlyPoisoner()
        {
            string sitEventsPath = FindRepoFile(Path.Combine("module", "ModuleData", "vividworld_situation_events.json"));
            var sitEventCatalog = EventCatalogLoader.Load(File.ReadAllText(sitEventsPath), _defaultPersistence);

            var template = sitEventCatalog.ByType("conduct_poisoned");
            Assert.NotNull(template);
            Assert.Equal(EventOrigin.Secret, template!.Origin);
            Assert.Equal(new[] { "poisoner" }, template.KnowingRoles);

            var bindings = new Dictionary<string, string>
            {
                ["POISONER"] = "hero_poisoner",
                ["VICTIM"] = "hero_victim"
            };

            var submission = TemplateBinder.Bind(template, bindings, 10.0, null, out var issues);
            Assert.NotNull(submission);
            Assert.Empty(issues.Where(i => i.IsError));
            Assert.Contains("poisoner", submission.KnowingRoles);
            Assert.DoesNotContain("victim", submission.KnowingRoles);
            Assert.Single(submission.KnowingRoles);

            var evt = new WorldEvent
            {
                EventId = "evt_poison_1",
                Type = submission.Type,
                Origin = submission.Origin!.Value,
                Participants = submission.Participants
            };
            VividWorld.Core.Ingest.Hop0Seeding.Seed(evt, submission, channel: null!, traits: null!, cfg: new VividWorld.Core.Config.PropagationConfig(), playerHeroId: "hero_player", day: 10.0, log: null);
            Assert.Single(evt.KnownBy);
            Assert.Equal("hero_poisoner", evt.KnownBy[0].HeroId);
            Assert.DoesNotContain(evt.KnownBy, k => k.HeroId == "hero_victim");
        }

        #endregion
    }
}
