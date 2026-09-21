using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using VividWorld.Core.Catalog;
using VividWorld.Core.Config;
using VividWorld.Core.Events;
using VividWorld.Core.Persistence;
using VividWorld.Core.Situations;
using VividWorld.Core.Util;
using Xunit;

namespace VividWorld.Core.Tests
{
    public class SituationScanTests
    {
        /// <summary>情境歷史索引的「今天」。這些測試驗的不是可見性，
        /// 所以一律給一個遠在所有測試事件之後的日子 ⇒ 每一則都看得見。</summary>
        private const double TodayFarAhead = 1_000_000.0;

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

        // ==========================================
        // 1. Quota (至少 6 條)
        // ==========================================

        [Fact]
        public void Quota_IntegerMaxPerDay_GuaranteedCountAndNoFractionalRoll()
        {
            var rng = new SplitMix64Rng();
            var roll = SituationQuota.Roll(2.0, rng, 12345L);

            Assert.Equal(2, roll.Quota);
            Assert.Equal(2, roll.Guaranteed);
            Assert.Equal(2.0, roll.MaxPerDay);
            Assert.Equal(0.0, roll.Fraction);
            Assert.Equal(0.0, roll.RollValue);
            Assert.False(roll.FractionWon);
            Assert.False(roll.Clamped);
        }

        [Fact]
        public void Quota_FractionalRoll_Wins()
        {
            var rng = new SplitMix64Rng();
            // Find a seed where rng.NextDouble < 0.5
            long seed = 0;
            while (rng.NextDouble(seed) >= 0.5) seed++;

            double rollVal = rng.NextDouble(seed);
            Assert.True(rollVal < 0.5);

            var roll = SituationQuota.Roll(1.5, rng, seed);
            Assert.Equal(2, roll.Quota);
            Assert.Equal(1, roll.Guaranteed);
            Assert.Equal(0.5, roll.Fraction);
            Assert.Equal(rollVal, roll.RollValue);
            Assert.True(roll.FractionWon);
            Assert.False(roll.Clamped);
        }

        [Fact]
        public void Quota_FractionalRoll_Loses()
        {
            var rng = new SplitMix64Rng();
            // Find a seed where rng.NextDouble >= 0.5
            long seed = 0;
            while (rng.NextDouble(seed) < 0.5) seed++;

            double rollVal = rng.NextDouble(seed);
            Assert.True(rollVal >= 0.5);

            var roll = SituationQuota.Roll(1.5, rng, seed);
            Assert.Equal(1, roll.Quota);
            Assert.Equal(1, roll.Guaranteed);
            Assert.Equal(0.5, roll.Fraction);
            Assert.Equal(rollVal, roll.RollValue);
            Assert.False(roll.FractionWon);
            Assert.False(roll.Clamped);
        }

        [Fact]
        public void Quota_ZeroMaxPerDay_ReturnsZero()
        {
            var rng = new SplitMix64Rng();
            var roll = SituationQuota.Roll(0.0, rng, 9999L);

            Assert.Equal(0, roll.Quota);
            Assert.Equal(0, roll.Guaranteed);
            Assert.Equal(0.0, roll.MaxPerDay);
            Assert.Equal(0.0, roll.Fraction);
            Assert.Equal(0.0, roll.RollValue);
            Assert.False(roll.FractionWon);
            Assert.False(roll.Clamped);
        }

        [Fact]
        public void Quota_NegativeMaxPerDay_ReturnsZero()
        {
            var rng = new SplitMix64Rng();
            var roll = SituationQuota.Roll(-1.5, rng, 9999L);

            Assert.Equal(0, roll.Quota);
            Assert.Equal(0, roll.Guaranteed);
            Assert.Equal(0.0, roll.MaxPerDay);
            Assert.False(roll.FractionWon);
            Assert.False(roll.Clamped);
        }

        [Fact]
        public void Quota_ExceedingHardCap_ClampsToHardCap()
        {
            var rng = new SplitMix64Rng();
            var roll = SituationQuota.Roll(25.0, rng, 12345L);

            Assert.Equal(SituationQuota.HardCap, roll.Quota);
            Assert.Equal(20, roll.Quota);
            Assert.True(roll.Clamped);
        }

        // ==========================================
        // 2. Planner (至少 8 條)
        // ==========================================

        [Fact]
        public void Planner_EmptyCandidates_ReturnsEmptyPlan()
        {
            var rng = new SplitMix64Rng();
            var plan = SituationScanPlanner.Plan(new List<SituationCandidate>(), 5, rng, s => s);

            Assert.Empty(plan.Picked);
            Assert.Empty(plan.NotPicked);
        }

        [Fact]
        public void Planner_ZeroQuota_ReturnsEmptyPlan()
        {
            var rng = new SplitMix64Rng();
            var candidates = new List<SituationCandidate>
            {
                new() { SituationId = "s1", SettlementId = "t1", RoleHeroIds = new() { ["a"] = "h1", ["b"] = "h2" } }
            };

            var plan = SituationScanPlanner.Plan(candidates, 0, rng, s => s);

            Assert.Empty(plan.Picked);
            Assert.Single(plan.NotPicked);
        }

        [Fact]
        public void Planner_QuotaExceedsCandidateCount_PicksAllAvailable()
        {
            var rng = new SplitMix64Rng();
            var candidates = new List<SituationCandidate>
            {
                new() { SituationId = "s1", SettlementId = "t1", RoleHeroIds = new() { ["a"] = "h1", ["b"] = "h2" }, Weight = 1.0 },
                new() { SituationId = "s2", SettlementId = "t2", RoleHeroIds = new() { ["a"] = "h3", ["b"] = "h4" }, Weight = 1.0 },
                new() { SituationId = "s3", SettlementId = "t3", RoleHeroIds = new() { ["a"] = "h5", ["b"] = "h6" }, Weight = 1.0 }
            };

            var plan = SituationScanPlanner.Plan(candidates, 10, rng, s => 42 + s);

            Assert.Equal(3, plan.Picked.Count);
            Assert.Empty(plan.NotPicked);
        }

        [Fact]
        public void Planner_DroppingCandidatesSharingHero_ExcludesThemFromSubsequentPicks()
        {
            var rng = new SplitMix64Rng();
            // Candidate 1: (A, B)
            // Candidate 2: (A, C) -> shares A
            // Candidate 3: (D, A) -> shares A
            // Candidate 4: (E, F) -> independent
            var c1 = new SituationCandidate { SituationId = "s1", SettlementId = "t1", RoleHeroIds = new() { ["a"] = "lord_A", ["b"] = "lord_B" }, Weight = 100.0 };
            var c2 = new SituationCandidate { SituationId = "s2", SettlementId = "t2", RoleHeroIds = new() { ["a"] = "lord_A", ["b"] = "lord_C" }, Weight = 1.0 };
            var c3 = new SituationCandidate { SituationId = "s3", SettlementId = "t3", RoleHeroIds = new() { ["a"] = "lord_D", ["b"] = "lord_A" }, Weight = 1.0 };
            var c4 = new SituationCandidate { SituationId = "s4", SettlementId = "t4", RoleHeroIds = new() { ["a"] = "lord_E", ["b"] = "lord_F" }, Weight = 1.0 };

            var candidates = new List<SituationCandidate> { c1, c2, c3, c4 };
            var plan = SituationScanPlanner.Plan(candidates, 2, rng, s => 12345L + s);

            Assert.Equal(2, plan.Picked.Count);
            Assert.Equal("s1", plan.Picked[0].Candidate.SituationId);
            Assert.Equal(2, plan.Picked[0].DroppedCount); // c2 and c3 dropped
            Assert.Equal("s4", plan.Picked[1].Candidate.SituationId);

            Assert.Contains(c2, plan.NotPicked);
            Assert.Contains(c3, plan.NotPicked);
        }

        [Fact]
        public void Planner_ZeroWeightCandidate_NeverPicked()
        {
            var rng = new SplitMix64Rng();
            var c1 = new SituationCandidate { SituationId = "s1", SettlementId = "t1", RoleHeroIds = new() { ["a"] = "h1", ["b"] = "h2" }, Weight = 1.0 };
            var c2 = new SituationCandidate { SituationId = "s2", SettlementId = "t2", RoleHeroIds = new() { ["a"] = "h3", ["b"] = "h4" }, Weight = 0.0 };

            var plan = SituationScanPlanner.Plan(new List<SituationCandidate> { c1, c2 }, 2, rng, s => s);

            Assert.Single(plan.Picked);
            Assert.Equal("s1", plan.Picked[0].Candidate.SituationId);
            Assert.Contains(c2, plan.NotPicked);
        }

        [Fact]
        public void Planner_InputOrderIndependent_ProducesIdenticalPlan()
        {
            var rng = new SplitMix64Rng();
            var c1 = new SituationCandidate { SituationId = "s1", SettlementId = "t1", RoleHeroIds = new() { ["a"] = "h1", ["b"] = "h2" }, Weight = 1.0 };
            var c2 = new SituationCandidate { SituationId = "s2", SettlementId = "t2", RoleHeroIds = new() { ["a"] = "h3", ["b"] = "h4" }, Weight = 2.0 };
            var c3 = new SituationCandidate { SituationId = "s3", SettlementId = "t3", RoleHeroIds = new() { ["a"] = "h5", ["b"] = "h6" }, Weight = 3.0 };

            var listA = new List<SituationCandidate> { c1, c2, c3 };
            var listB = new List<SituationCandidate> { c3, c1, c2 };

            var planA = SituationScanPlanner.Plan(listA, 2, rng, s => 999L + s);
            var planB = SituationScanPlanner.Plan(listB, 2, rng, s => 999L + s);

            Assert.Equal(planA.Picked.Count, planB.Picked.Count);
            for (int i = 0; i < planA.Picked.Count; i++)
            {
                Assert.Equal(planA.Picked[i].Candidate.SituationId, planB.Picked[i].Candidate.SituationId);
                Assert.Equal(planA.Picked[i].Candidate.SettlementId, planB.Picked[i].Candidate.SettlementId);
                Assert.Equal(planA.Picked[i].Roll, planB.Picked[i].Roll);
                Assert.Equal(planA.Picked[i].Probability, planB.Picked[i].Probability);
            }
        }

        [Fact]
        public void Planner_SameSeed_ProducesDeterministicResult()
        {
            var rng = new SplitMix64Rng();
            var candidates = new List<SituationCandidate>
            {
                new() { SituationId = "s1", SettlementId = "t1", RoleHeroIds = new() { ["a"] = "h1", ["b"] = "h2" }, Weight = 1.0 },
                new() { SituationId = "s2", SettlementId = "t2", RoleHeroIds = new() { ["a"] = "h3", ["b"] = "h4" }, Weight = 2.0 }
            };

            var plan1 = SituationScanPlanner.Plan(candidates, 2, rng, s => 5555L + s);
            var plan2 = SituationScanPlanner.Plan(candidates, 2, rng, s => 5555L + s);

            Assert.Equal(plan1.Picked[0].Candidate.SituationId, plan2.Picked[0].Candidate.SituationId);
            Assert.Equal(plan1.Picked[0].Roll, plan2.Picked[0].Roll);
        }

        [Fact]
        public void Planner_Probability_MatchesWeightProportionInRound()
        {
            var rng = new SplitMix64Rng();
            var c1 = new SituationCandidate { SituationId = "s1", SettlementId = "t1", RoleHeroIds = new() { ["a"] = "h1", ["b"] = "h2" }, Weight = 1.0 };
            var c2 = new SituationCandidate { SituationId = "s2", SettlementId = "t2", RoleHeroIds = new() { ["a"] = "h3", ["b"] = "h4" }, Weight = 2.0 };

            var plan = SituationScanPlanner.Plan(new List<SituationCandidate> { c1, c2 }, 1, rng, s => 12345L);
            Assert.Single(plan.Picked);

            var pick = plan.Picked[0];
            double expectedProb = pick.Candidate.Weight / (1.0 + 2.0);
            Assert.Equal(expectedProb, pick.Probability, 5);
        }

        // ==========================================
        // 3. Cooldown Condition (至少 8 條)
        // ==========================================

        [Fact]
        public void Cooldown_NeverOccurred_PassesWithNeverDetail()
        {
            var cond = new SituationConditionDef { Type = "cooldown", Days = 30.0 };
            var history = new SituationHistoryIndex(new List<RumorIndexEntry>(), TodayFarAhead);
            var nonDerived = new List<string> { "h1", "h2" };

            var result = SituationConditionEvaluator.Evaluate(
                cond,
                new Dictionary<string, SituationRoleFacts>(),
                null,
                null,
                situationId: "seat_dispute",
                day: 100.0,
                history: history,
                nonDerivedHeroIds: nonDerived);

            Assert.True(result.Ok);
            Assert.Equal("cooldown 30d ok (never)", result.Detail);
        }

        [Fact]
        public void Cooldown_WithinCooldownPeriod_Fails()
        {
            var cond = new SituationConditionDef { Type = "cooldown", Days = 30.0 };
            var entry = new RumorIndexEntry
            {
                EventId = "evt_test",
                SituationId = "seat_dispute",
                Day = 90.0,
                ParticipantHeroIds = new List<string> { "h1", "h2" }
            };
            var history = new SituationHistoryIndex(new List<RumorIndexEntry> { entry }, TodayFarAhead);
            var nonDerived = new List<string> { "h1", "h2" };

            var result = SituationConditionEvaluator.Evaluate(
                cond,
                new Dictionary<string, SituationRoleFacts>(),
                null,
                null,
                situationId: "seat_dispute",
                day: 100.0,
                history: history,
                nonDerivedHeroIds: nonDerived);

            Assert.False(result.Ok);
            Assert.Equal("cooldown 30d failed (last was evt_test on day 90.0, 10.0d ago)", result.Detail);
        }

        [Fact]
        public void Cooldown_ExactlyAtCooldownBoundary_Passes()
        {
            var cond = new SituationConditionDef { Type = "cooldown", Days = 30.0 };
            var entry = new RumorIndexEntry
            {
                EventId = "evt_test",
                SituationId = "seat_dispute",
                Day = 70.0,
                ParticipantHeroIds = new List<string> { "h1", "h2" }
            };
            var history = new SituationHistoryIndex(new List<RumorIndexEntry> { entry }, TodayFarAhead);
            var nonDerived = new List<string> { "h1", "h2" };

            var result = SituationConditionEvaluator.Evaluate(
                cond,
                new Dictionary<string, SituationRoleFacts>(),
                null,
                null,
                situationId: "seat_dispute",
                day: 100.0,
                history: history,
                nonDerivedHeroIds: nonDerived);

            Assert.True(result.Ok);
            Assert.Equal("cooldown 30d ok (last was evt_test on day 70.0, 30.0d ago)", result.Detail);
        }

        [Fact]
        public void Cooldown_NullHistory_PassesWithNoHistoryDetail()
        {
            var cond = new SituationConditionDef { Type = "cooldown", Days = 30.0 };

            var result = SituationConditionEvaluator.Evaluate(
                cond,
                new Dictionary<string, SituationRoleFacts>(),
                null,
                null,
                situationId: "seat_dispute",
                day: 100.0,
                history: null);

            Assert.True(result.Ok);
            Assert.Equal("cooldown 30d ok (no history available)", result.Detail);
        }

        [Fact]
        public void Cooldown_DifferentSituationId_NotCountedInCooldown()
        {
            var cond = new SituationConditionDef { Type = "cooldown", Days = 30.0 };
            var entry = new RumorIndexEntry
            {
                SituationId = "other_situation",
                Day = 95.0,
                ParticipantHeroIds = new List<string> { "h1", "h2" }
            };
            var history = new SituationHistoryIndex(new List<RumorIndexEntry> { entry }, TodayFarAhead);

            var result = SituationConditionEvaluator.Evaluate(
                cond,
                new Dictionary<string, SituationRoleFacts>(),
                null,
                null,
                situationId: "seat_dispute",
                day: 100.0,
                history: history,
                nonDerivedHeroIds: new List<string> { "h1", "h2" });

            Assert.True(result.Ok);
            Assert.Equal("cooldown 30d ok (never)", result.Detail);
        }

        [Fact]
        public void Cooldown_PartialHeroMatch_NotCounted()
        {
            var cond = new SituationConditionDef { Type = "cooldown", Days = 30.0 };
            var entry = new RumorIndexEntry
            {
                SituationId = "seat_dispute",
                Day = 95.0,
                ParticipantHeroIds = new List<string> { "h1" } // Missing h2
            };
            var history = new SituationHistoryIndex(new List<RumorIndexEntry> { entry }, TodayFarAhead);

            var result = SituationConditionEvaluator.Evaluate(
                cond,
                new Dictionary<string, SituationRoleFacts>(),
                null,
                null,
                situationId: "seat_dispute",
                day: 100.0,
                history: history,
                nonDerivedHeroIds: new List<string> { "h1", "h2" });

            Assert.True(result.Ok);
            Assert.Equal("cooldown 30d ok (never)", result.Detail);
        }

        [Fact]
        public void Cooldown_SuperSetHeroMatch_Counted()
        {
            var cond = new SituationConditionDef { Type = "cooldown", Days = 30.0 };
            var entry = new RumorIndexEntry
            {
                EventId = "evt_test",
                SituationId = "seat_dispute",
                Day = 95.0,
                ParticipantHeroIds = new List<string> { "h1", "h2", "host_leader" } // Super set including derived host
            };
            var history = new SituationHistoryIndex(new List<RumorIndexEntry> { entry }, TodayFarAhead);

            var result = SituationConditionEvaluator.Evaluate(
                cond,
                new Dictionary<string, SituationRoleFacts>(),
                null,
                null,
                situationId: "seat_dispute",
                day: 100.0,
                history: history,
                nonDerivedHeroIds: new List<string> { "h1", "h2" });

            Assert.False(result.Ok);
            Assert.Equal("cooldown 30d failed (last was evt_test on day 95.0, 5.0d ago)", result.Detail);
        }

        [Fact]
        public void Cooldown_MultipleOccurrences_UsesMostRecentDay()
        {
            var cond = new SituationConditionDef { Type = "cooldown", Days = 30.0 };
            var entry1 = new RumorIndexEntry
            {
                EventId = "evt_old",
                SituationId = "seat_dispute",
                Day = 10.0,
                ParticipantHeroIds = new List<string> { "h1", "h2" }
            };
            var entry2 = new RumorIndexEntry
            {
                EventId = "evt_recent",
                SituationId = "seat_dispute",
                Day = 85.0,
                ParticipantHeroIds = new List<string> { "h1", "h2" }
            };
            var history = new SituationHistoryIndex(new List<RumorIndexEntry> { entry1, entry2 }, TodayFarAhead);

            var result = SituationConditionEvaluator.Evaluate(
                cond,
                new Dictionary<string, SituationRoleFacts>(),
                null,
                null,
                situationId: "seat_dispute",
                day: 100.0,
                history: history,
                nonDerivedHeroIds: new List<string> { "h1", "h2" });

            Assert.False(result.Ok);
            Assert.Equal("cooldown 30d failed (last was evt_recent on day 85.0, 15.0d ago)", result.Detail);
        }

        // ==========================================
        // 4. SituationHistoryIndex (至少 3 條)
        // ==========================================

        [Fact]
        public void HistoryIndex_NullSituationId_ExcludedFromIndex()
        {
            var entry = new RumorIndexEntry
            {
                SituationId = null,
                Day = 50.0,
                ParticipantHeroIds = new List<string> { "h1", "h2" }
            };
            var index = new SituationHistoryIndex(new List<RumorIndexEntry> { entry }, TodayFarAhead);

            var occ = index.LastOccurrence("seat_dispute", new List<string> { "h1", "h2" });
            Assert.Null(occ);
        }

        [Fact]
        public void HistoryIndex_MultipleEventsSameHeroes_ReturnsMaxDay()
        {
            var entries = new List<RumorIndexEntry>
            {
                new() { SituationId = "sit_A", Day = 10.0, ParticipantHeroIds = new() { "h1", "h2" } },
                new() { SituationId = "sit_A", Day = 95.0, ParticipantHeroIds = new() { "h1", "h2" } },
                new() { SituationId = "sit_A", Day = 40.0, ParticipantHeroIds = new() { "h1", "h2" } }
            };
            var index = new SituationHistoryIndex(entries, TodayFarAhead);

            var occ = index.LastOccurrence("sit_A", new List<string> { "h1", "h2" });
            Assert.NotNull(occ);
            Assert.Equal(95.0, occ!.Day);
        }

        [Fact]
        public void HistoryIndex_HeroMatching_IsOrdinalCaseSensitive()
        {
            var entries = new List<RumorIndexEntry>
            {
                new() { SituationId = "sit_A", Day = 50.0, ParticipantHeroIds = new() { "lord_1", "lord_2" } }
            };
            var index = new SituationHistoryIndex(entries, TodayFarAhead);

            var occLower = index.LastOccurrence("sit_A", new List<string> { "lord_1", "lord_2" });
            var occUpper = index.LastOccurrence("sit_A", new List<string> { "LORD_1", "LORD_2" });

            Assert.NotNull(occLower);
            Assert.Null(occUpper);
        }

        // ==========================================
        // 5. Template New Keys (至少 4 條)
        // ==========================================

        [Fact]
        public void Catalog_MissingWeight_DefaultsToOne()
        {
            string json = @"{
  ""situations"": [
    {
      ""id"": ""s_no_weight"",
      ""trigger"": ""direct"",
      ""decider"": ""slighted"",
      ""roles"": { ""slighted"": {}, ""favored"": {} },
      ""conditions"": [
        { ""type"": ""differentClan"", ""a"": ""slighted"", ""b"": ""favored"" }
      ],
      ""branches"": [
        {
          ""id"": ""b1"",
          ""base"": 1.0,
          ""events"": [
            { ""type"": ""seat_dispute_demanded"", ""bind"": { ""SLIGHTED"": ""slighted"", ""FAVORED"": ""favored"" } }
          ]
        }
      ]
    }
  ]
}";
            var catalog = SituationCatalogLoader.Load(json);
            var sit = catalog.ById("s_no_weight");

            Assert.NotNull(sit);
            Assert.Equal(1.0, sit!.Weight);
        }

        [Fact]
        public void Catalog_InvalidWeight_EmitsInvalidWeightIssue()
        {
            string json = @"{
  ""situations"": [
    {
      ""id"": ""s_neg_weight"",
      ""trigger"": ""direct"",
      ""weight"": -5.0,
      ""decider"": ""slighted"",
      ""roles"": { ""slighted"": {}, ""favored"": {} },
      ""conditions"": [
        { ""type"": ""differentClan"", ""a"": ""slighted"", ""b"": ""favored"" }
      ],
      ""branches"": [
        {
          ""id"": ""b1"",
          ""base"": 1.0,
          ""events"": [
            { ""type"": ""seat_dispute_demanded"", ""bind"": { ""SLIGHTED"": ""slighted"", ""FAVORED"": ""favored"" } }
          ]
        }
      ]
    }
  ]
}";
            var catalog = SituationCatalogLoader.Load(json);
            var issue = catalog.Issues.FirstOrDefault(i => i.Code == SituationIssueCode.InvalidWeight);

            Assert.NotNull(issue);
            Assert.True(issue!.IsError);
        }

        [Fact]
        public void Catalog_MissingDevOnly_DefaultsToFalse()
        {
            string json = @"{
  ""situations"": [
    {
      ""id"": ""s_no_devonly"",
      ""trigger"": ""direct"",
      ""decider"": ""slighted"",
      ""roles"": { ""slighted"": {}, ""favored"": {} },
      ""conditions"": [
        { ""type"": ""differentClan"", ""a"": ""slighted"", ""b"": ""favored"" }
      ],
      ""branches"": [
        {
          ""id"": ""b1"",
          ""base"": 1.0,
          ""events"": [
            { ""type"": ""seat_dispute_demanded"", ""bind"": { ""SLIGHTED"": ""slighted"", ""FAVORED"": ""favored"" } }
          ]
        }
      ]
    }
  ]
}";
            var catalog = SituationCatalogLoader.Load(json);
            var sit = catalog.ById("s_no_devonly");

            Assert.NotNull(sit);
            Assert.False(sit!.DevOnly);
        }

        [Fact]
        public void Catalog_UnknownField_EmitsWarningNotError()
        {
            string json = @"{
  ""situations"": [
    {
      ""id"": ""s_extra"",
      ""trigger"": ""direct"",
      ""customKey"": ""futureValue"",
      ""decider"": ""slighted"",
      ""roles"": { ""slighted"": {}, ""favored"": {} },
      ""conditions"": [
        { ""type"": ""differentClan"", ""a"": ""slighted"", ""b"": ""favored"" }
      ],
      ""branches"": [
        {
          ""id"": ""b1"",
          ""base"": 1.0,
          ""events"": [
            { ""type"": ""seat_dispute_demanded"", ""bind"": { ""SLIGHTED"": ""slighted"", ""FAVORED"": ""favored"" } }
          ]
        }
      ]
    }
  ]
}";
            var catalog = SituationCatalogLoader.Load(json);
            var sit = catalog.ById("s_extra");

            Assert.NotNull(sit);
            var issue = catalog.Issues.FirstOrDefault(i => i.Code == SituationIssueCode.UnknownProperty);
            Assert.NotNull(issue);
            Assert.False(issue!.IsError); // Warning only, not blocking
        }

        // ==========================================
        // 6. Log Format (至少 5 條)
        // ==========================================

        [Fact]
        public void LogFormat_Summary_MatchesVerbatim()
        {
            // Won fractional roll
            var wonRoll = new QuotaRoll
            {
                Quota = 2,
                Guaranteed = 1,
                MaxPerDay = 1.5,
                Fraction = 0.5,
                RollValue = 0.4213,
                FractionWon = true
            };

            string line1 = SituationScanLogFormatter.FormatSummary(
                91320.0, 124, 38, 612, 24, 3, false, 200, wonRoll, 2, 1, 12.4, isDryRun: false);

            Assert.Equal("Situation scan day 91320: 124 settlement(s), 38 with >=2 eligible, 612 pair(s) evaluated (cap 24/settlement), 3 candidate(s) passed, quota 2 (maxPerDay 1.50, roll 0.4213 < 0.50 -> +1), triggered 2, left over 1. 12.4 ms", line1);

            // Lost fractional roll
            var lostRoll = new QuotaRoll
            {
                Quota = 1,
                Guaranteed = 1,
                MaxPerDay = 1.5,
                Fraction = 0.5,
                RollValue = 0.6104,
                FractionWon = false
            };

            string line2 = SituationScanLogFormatter.FormatSummary(
                91320.0, 124, 38, 612, 24, 3, false, 200, lostRoll, 1, 2, 12.4, isDryRun: false);

            Assert.Equal("Situation scan day 91320: 124 settlement(s), 38 with >=2 eligible, 612 pair(s) evaluated (cap 24/settlement), 3 candidate(s) passed, quota 1 (maxPerDay 1.50, roll 0.6104 >= 0.50 -> +0), triggered 1, left over 2. 12.4 ms", line2);

            // Integer maxPerDay
            var intRoll = new QuotaRoll
            {
                Quota = 1,
                Guaranteed = 1,
                MaxPerDay = 1.0,
                Fraction = 0.0,
                RollValue = 0.0,
                FractionWon = false
            };

            string line3 = SituationScanLogFormatter.FormatSummary(
                91320.0, 124, 38, 612, 24, 3, false, 200, intRoll, 1, 2, 12.4, isDryRun: false);

            Assert.Contains("quota 1 (maxPerDay 1.00, no fractional roll)", line3);

            // Truncated at 200
            string line4 = SituationScanLogFormatter.FormatSummary(
                91320.0, 124, 38, 612, 24, 200, true, 200, wonRoll, 2, 198, 12.4, isDryRun: false);

            Assert.Contains("200 candidate(s) passed (truncated at 200)", line4);
        }

        [Fact]
        public void LogFormat_Disabled_MatchesVerbatim()
        {
            string line = SituationScanLogFormatter.FormatDisabled();
            Assert.Equal("Situation scan: disabled (situations.dailyScanEnabled = false)", line);
        }

        [Fact]
        public void LogFormat_QuotaZero_MatchesVerbatim()
        {
            var roll = new QuotaRoll
            {
                Quota = 0,
                Guaranteed = 0,
                MaxPerDay = 0.5,
                Fraction = 0.5,
                RollValue = 0.8812,
                FractionWon = false
            };

            string line = SituationScanLogFormatter.FormatQuotaZero(91320.0, roll);
            Assert.Equal("Situation scan day 91320: quota 0 (maxPerDay 0.50, roll 0.8812 >= 0.50 -> +0), nothing scanned.", line);
        }

        [Fact]
        public void LogFormat_Rejections_MatchesVerbatim()
        {
            var rejections = new Dictionary<string, int>
            {
                ["cooldown"] = 4,
                ["sameKingdom"] = 96,
                ["clanTierCompare"] = 402,
                ["isClanLeader"] = 380,
                ["differentClan"] = 210
            };

            string? line = SituationScanLogFormatter.FormatRejections(rejections);
            Assert.Equal("  scan rejections: clanTierCompare 402, isClanLeader 380, differentClan 210, sameKingdom 96, cooldown 4", line);
        }

        [Fact]
        public void LogFormat_SkippedDevOnly_MatchesVerbatim()
        {
            var list = new List<string> { "seat_dispute_dev" };
            string? line = SituationScanLogFormatter.FormatSkippedDevOnly(list);
            Assert.Equal("  scan skipped 1 devOnly situation(s): seat_dispute_dev", line);
        }

        [Fact]
        public void LogFormat_NearMiss_MatchesVerbatim()
        {
            var roles = new Dictionary<string, string>
            {
                ["slighted"] = "lord_5_131",
                ["favored"] = "lord_1_22"
            };
            string line = SituationScanLogFormatter.FormatNearMiss(
                "seat_dispute", "town_ES3", roles, "isClanLeader(slighted) failed: lord_5_131 is not the leader of clan_battania_1");

            Assert.Equal("  near miss: seat_dispute at town_ES3 slighted=lord_5_131 favored=lord_1_22 - isClanLeader(slighted) failed: lord_5_131 is not the leader of clan_battania_1", line);
        }

        [Fact]
        public void LogFormat_Pick_MatchesVerbatim()
        {
            var roles = new Dictionary<string, string>
            {
                ["slighted"] = "lord_4_15",
                ["favored"] = "lord_1_22"
            };
            string line = SituationScanLogFormatter.FormatPick(
                0, "seat_dispute", "town_ES3", roles, 1.0, 3.0, 1.0 / 3.0, 0.7104, 2);

            Assert.Equal("  scan pick #1: seat_dispute at town_ES3 slighted=lord_4_15 favored=lord_1_22, weight 1.00 of 3.00 (33.3%), roll 0.7104 -> picked; 2 candidate(s) dropped (shares a hero)", line);
        }

        [Fact]
        public void LogFormat_DryRun_MatchesVerbatim()
        {
            string line = SituationScanLogFormatter.FormatDryRunEnd();
            Assert.Equal("  scan (dry run): nothing was triggered, no events were submitted.", line);
        }

        // ==========================================
        // 7. Config (至少 2 條)
        // ==========================================

        [Fact]
        public void Config_BackwardCompatibility_GrowsScanSubtree()
        {
            string oldJson = @"{
  ""situations"": {
    ""dailyScanEnabled"": true,
    ""maxPerDay"": 2.5
  }
}";
            var config = JsonConvert.DeserializeObject<VividWorldConfig>(oldJson);
            Assert.NotNull(config);
            Assert.NotNull(config!.Situations);
            Assert.Equal(2.5, config.Situations.MaxPerDay);
            Assert.NotNull(config.Situations.Scan);
            Assert.Equal(24, config.Situations.Scan.MaxPairsPerSettlement);
            Assert.Equal(200, config.Situations.Scan.MaxCandidates);
        }

        [Fact]
        public void Config_Normalize_ClampsScanPropertiesWithNotices()
        {
            var config = new VividWorldConfig
            {
                Situations = new SituationsConfig
                {
                    Scan = new SituationScanConfig
                    {
                        MaxPairsPerSettlement = 1,     // min 2
                        MaxCandidates = 10000          // max 5000
                    }
                }
            };

            var notices = new List<ClampNotice>();
            config.Normalize(notices);

            Assert.Equal(2, config.Situations.Scan.MaxPairsPerSettlement);
            Assert.Equal(5000, config.Situations.Scan.MaxCandidates);

            Assert.Contains(notices, n => n.Key.Contains("situations.scan.maxPairsPerSettlement"));
            Assert.Contains(notices, n => n.Key.Contains("situations.scan.maxCandidates"));
        }

        // ==========================================
        // 8. Serialization (至少 2 條)
        // ==========================================

        [Fact]
        public void Serialization_WorldEvent_SituationId_RoundTripsAndOmittedWhenNull()
        {
            var evtWithSit = new WorldEvent
            {
                EventId = "evt_sit_1",
                Type = "seat_dispute_demanded",
                SituationId = "seat_dispute"
            };

            string jsonWith = VividJson.Write(evtWithSit);
            Assert.Contains("\"situationId\":", jsonWith);

            var deserializedWith = VividJson.Read<WorldEvent>(jsonWith);
            Assert.NotNull(deserializedWith);
            Assert.Equal("seat_dispute", deserializedWith!.SituationId);

            var evtWithoutSit = new WorldEvent
            {
                EventId = "evt_sit_2",
                Type = "some_event",
                SituationId = null
            };

            string jsonWithout = VividJson.Write(evtWithoutSit);
            Assert.DoesNotContain("\"situationId\":", jsonWithout);

            var deserializedWithout = VividJson.Read<WorldEvent>(jsonWithout);
            Assert.NotNull(deserializedWithout);
            Assert.Null(deserializedWithout!.SituationId);
        }

        [Fact]
        public void Serialization_RumorIndexEntry_SituationId_RoundTripsAndHandlesLegacyJson()
        {
            string legacyJson = @"{
  ""eventId"": ""evt_legacy"",
  ""type"": ""some_event"",
  ""day"": 10.0,
  ""shardFile"": ""events_0000.json""
}";
            var legacyEntry = VividJson.Read<RumorIndexEntry>(legacyJson);
            Assert.NotNull(legacyEntry);
            Assert.Null(legacyEntry!.SituationId);

            var entryWithSit = new RumorIndexEntry
            {
                EventId = "evt_sit",
                Type = "seat_dispute_demanded",
                SituationId = "seat_dispute",
                Day = 20.0
            };

            string json = VividJson.Write(entryWithSit);
            Assert.Contains("\"situationId\":", json);

            var roundTripped = VividJson.Read<RumorIndexEntry>(json);
            Assert.NotNull(roundTripped);
            Assert.Equal("seat_dispute", roundTripped!.SituationId);
        }

        // ==========================================
        // 9. Real Files (至少 2 條)
        // ==========================================

        [Fact]
        public void RealCatalog_VividWorldSituations_LoadsWithZeroErrors()
        {
            string path = FindRepoFile("module/ModuleData/vividworld_situations.json");
            string json = File.ReadAllText(path);

            var catalog = SituationCatalogLoader.Load(json);
            bool hasErrors = catalog.Issues.Any(i => i.IsError);
            Assert.False(hasErrors, $"Catalog has errors: {string.Join(", ", catalog.Issues.Where(i => i.IsError).Select(i => $"{i.Code}: {i.Detail}"))}");
            Assert.NotEmpty(catalog.Situations);
        }

        [Fact]
        public void RealCatalog_SeatDispute_HasWeight1AndCooldownAndDevOnlyOnDev()
        {
            string path = FindRepoFile("module/ModuleData/vividworld_situations.json");
            string json = File.ReadAllText(path);

            var catalog = SituationCatalogLoader.Load(json);

            var seatDispute = catalog.ById("seat_dispute");
            Assert.NotNull(seatDispute);
            Assert.Equal(1.0, seatDispute!.Weight);
            Assert.False(seatDispute.DevOnly);

            var cdCond = seatDispute.Conditions.FirstOrDefault(c => string.Equals(c.Type, "cooldown", StringComparison.OrdinalIgnoreCase));
            Assert.NotNull(cdCond);
            Assert.Equal(30.0, cdCond!.Days);

            var seatDisputeDev = catalog.ById("seat_dispute_dev");
            Assert.NotNull(seatDisputeDev);
            Assert.True(seatDisputeDev!.DevOnly);
        }
    }
}
