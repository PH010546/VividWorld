#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using VividWorld.Core.Catalog;
using VividWorld.Core.Config;
using VividWorld.Core.Events;
using VividWorld.Core.Rumors;
using VividWorld.Core.Secrets;
using VividWorld.Core.Tests.Fakes;
using VividWorld.Core.Util;
using Xunit;

namespace VividWorld.Core.Tests.Secrets
{
    public class TournamentThrowTests
    {
        private static string ReadShipped(string fileName)
        {
            string? current = AppContext.BaseDirectory;
            while (!string.IsNullOrEmpty(current))
            {
                if (File.Exists(Path.Combine(current, "VividWorld.sln")))
                {
                    return File.ReadAllText(Path.Combine(current, "module", "ModuleData", fileName));
                }
                current = Directory.GetParent(current)?.FullName;
            }
            throw new FileNotFoundException(fileName);
        }

        private static EventTemplate LoadShippingTemplate()
        {
            var catalog = EventCatalogLoader.Load(ReadShipped("vividworld_situation_events.json"), new PersistenceConfig());
            var tmpl = catalog.ByType("tournament_final_thrown");
            Assert.NotNull(tmpl);
            return tmpl!;
        }

        [Fact]
        public void Step1_ChampionHonorPositive_StopsAndWillNotAsk()
        {
            var champion = new TournamentWinnerFact(
                "champ_1", "Champion", true, false, score: 100f, honor: 1, calculating: 1, townId: "town_1", townName: "Town");

            var part = new TournamentParticipantFact(
                "p_1", "P1", score: 90f, honor: 0, calculating: 1, relationWithWinner: 40, isKinWithWinner: true, isClanLeaderWinner: false, isKingdomLeaderWinner: false);

            var result = TournamentThrowEvaluation.Evaluate(
                champion,
                new[] { part },
                new VividWorldConfig(),
                campaignSeed: 12345,
                townId: "town_1",
                day: 10.0);

            Assert.False(result.IsThrown);
            Assert.Contains("winner honor positive", result.StopReason);
            Assert.Null(result.ChosenCandidate);
        }

        [Fact]
        public void Step2_NoCandidateScoreAboveThreshold_Stops()
        {
            var champion = new TournamentWinnerFact(
                "champ_1", "Champion", true, false, score: 100f, honor: 0, calculating: 1, townId: "town_1", townName: "Town");

            // Score 70 is <= 100 * 0.75 = 75
            var part = new TournamentParticipantFact(
                "p_1", "P1", score: 70f, honor: 0, calculating: 1, relationWithWinner: 40, isKinWithWinner: true, isClanLeaderWinner: false, isKingdomLeaderWinner: false);

            var result = TournamentThrowEvaluation.Evaluate(
                champion,
                new[] { part },
                new VividWorldConfig(),
                campaignSeed: 12345,
                townId: "town_1",
                day: 10.0);

            Assert.False(result.IsThrown);
            Assert.Contains("no qualified candidate", result.StopReason);
            Assert.Single(result.Evaluations);
            Assert.False(result.Evaluations[0].HasChanceToWin);
        }

        [Fact]
        public void Step2_CandidateScoreExactThreshold_DoesNotQualify_Boundary()
        {
            var champion = new TournamentWinnerFact(
                "champ_1", "Champion", true, false, score: 100f, honor: 0, calculating: 1, townId: "town_1", townName: "Town");

            // Exactly 75.0f == 100f * 0.75f => does NOT count
            var partExact = new TournamentParticipantFact(
                "p_exact", "PExact", score: 75.0f, honor: 0, calculating: 1, relationWithWinner: 40, isKinWithWinner: true, isClanLeaderWinner: true, isKingdomLeaderWinner: false);

            // 75.1f > 75.0f => counts
            var partAbove = new TournamentParticipantFact(
                "p_above", "PAbove", score: 75.1f, honor: 0, calculating: 1, relationWithWinner: 40, isKinWithWinner: true, isClanLeaderWinner: true, isKingdomLeaderWinner: false);

            var resultExact = TournamentThrowEvaluation.Evaluate(
                champion,
                new[] { partExact },
                new VividWorldConfig(),
                campaignSeed: 12345,
                townId: "town_1",
                day: 10.0);

            Assert.False(resultExact.IsThrown);
            Assert.False(resultExact.Evaluations[0].HasChanceToWin);
            Assert.Contains("score 75.0 <= threshold 75.0", resultExact.Evaluations[0].DisqualifiedReason);

            var resultAbove = TournamentThrowEvaluation.Evaluate(
                champion,
                new[] { partAbove },
                new VividWorldConfig(),
                campaignSeed: 12345,
                townId: "town_1",
                day: 10.0);

            Assert.True(resultAbove.IsThrown);
            Assert.True(resultAbove.Evaluations[0].HasChanceToWin);
            Assert.Equal("p_above", resultAbove.ThrowerHeroId);
        }

        [Fact]
        public void Step2_CandidateScoreAboveThreshold_FailsMasterServantAndFriendship_Stops()
        {
            var champion = new TournamentWinnerFact(
                "champ_1", "Champion", true, false, score: 100f, honor: 0, calculating: 0, townId: "town_1", townName: "Town");

            // Not kin, relation 20 <= 50, not clan/kingdom leader
            var part = new TournamentParticipantFact(
                "p_1", "P1", score: 80f, honor: 0, calculating: 1, relationWithWinner: 20, isKinWithWinner: false, isClanLeaderWinner: false, isKingdomLeaderWinner: false);

            var result = TournamentThrowEvaluation.Evaluate(
                champion,
                new[] { part },
                new VividWorldConfig(),
                campaignSeed: 12345,
                townId: "town_1",
                day: 10.0);

            Assert.False(result.IsThrown);
            Assert.True(result.Evaluations[0].HasChanceToWin);
            Assert.False(result.Evaluations[0].IsQualified);
            Assert.Contains("not master-servant", result.Evaluations[0].DisqualifiedReason);
        }

        [Fact]
        public void Step2_MasterServant_ClanLeaderOrKingdomLeader_Qualifies()
        {
            var champion = new TournamentWinnerFact(
                "champ_1", "Champion", true, false, score: 100f, honor: 0, calculating: 0, townId: "town_1", townName: "Town");

            var partClan = new TournamentParticipantFact(
                "p_clan", "PClan", score: 80f, honor: 0, calculating: 0, relationWithWinner: 0, isKinWithWinner: false, isClanLeaderWinner: true, isKingdomLeaderWinner: false);

            var partKingdom = new TournamentParticipantFact(
                "p_king", "PKing", score: 85f, honor: 0, calculating: 0, relationWithWinner: 0, isKinWithWinner: false, isClanLeaderWinner: false, isKingdomLeaderWinner: true);

            var resultClan = TournamentThrowEvaluation.Evaluate(
                champion, new[] { partClan }, new VividWorldConfig(), 12345, "town_1", 10.0);
            Assert.True(resultClan.IsThrown);
            Assert.Equal("liege", resultClan.SecretPath);

            var resultKingdom = TournamentThrowEvaluation.Evaluate(
                champion, new[] { partKingdom }, new VividWorldConfig(), 12345, "town_1", 10.0);
            Assert.True(resultKingdom.IsThrown);
            Assert.Equal("liege", resultKingdom.SecretPath);
        }

        [Fact]
        public void Step2_KinFriendship_RelationOver30_HonorNonPositive_Qualifies()
        {
            var champion = new TournamentWinnerFact(
                "champ_1", "Champion", true, false, score: 100f, honor: 0, calculating: 0, townId: "town_1", townName: "Town");

            // Kin, relation 31 > 30, honor 0 <= 0
            var partKin = new TournamentParticipantFact(
                "p_kin", "PKin", score: 80f, honor: 0, calculating: 0, relationWithWinner: 31, isKinWithWinner: true, isClanLeaderWinner: false, isKingdomLeaderWinner: false);

            var result = TournamentThrowEvaluation.Evaluate(
                champion, new[] { partKin }, new VividWorldConfig(), 12345, "town_1", 10.0);

            Assert.True(result.IsThrown);
            Assert.Equal("friendship", result.SecretPath);
        }

        [Fact]
        public void Step2_OtherFriendship_RelationOver50_BothCalculatingPositive_Qualifies()
        {
            var champion = new TournamentWinnerFact(
                "champ_1", "Champion", true, false, score: 100f, honor: 0, calculating: 1, townId: "town_1", townName: "Town");

            // Non-kin, relation 51 > 50, both calculating > 0
            var partOther = new TournamentParticipantFact(
                "p_other", "POther", score: 80f, honor: 0, calculating: 1, relationWithWinner: 51, isKinWithWinner: false, isClanLeaderWinner: false, isKingdomLeaderWinner: false);

            var result = TournamentThrowEvaluation.Evaluate(
                champion, new[] { partOther }, new VividWorldConfig(), 12345, "town_1", 10.0);

            Assert.True(result.IsThrown);
            Assert.Equal("friendship", result.SecretPath);
        }

        [Fact]
        public void Step2_BothMasterServantAndFriendship_CountsAsMasterServant_PathIsLiege()
        {
            var champion = new TournamentWinnerFact(
                "champ_1", "Champion", true, false, score: 100f, honor: 0, calculating: 1, townId: "town_1", townName: "Town");

            // Meets both: clan leader (master-servant) AND kin with relation 40 & honor 0 (friendship)
            var partBoth = new TournamentParticipantFact(
                "p_both", "PBoth", score: 90f, honor: 0, calculating: 1, relationWithWinner: 40, isKinWithWinner: true, isClanLeaderWinner: true, isKingdomLeaderWinner: false);

            var result = TournamentThrowEvaluation.Evaluate(
                champion, new[] { partBoth }, new VividWorldConfig(), 12345, "town_1", 10.0);

            Assert.True(result.IsThrown);
            Assert.Equal("liege", result.SecretPath);
            Assert.True(result.ChosenCandidate!.QualifiesMasterServant);
            Assert.True(result.ChosenCandidate.QualifiesKinFriendship);
        }

        [Fact]
        public void Step3_MultipleCandidates_DeterministicPickWithSeed()
        {
            var champion = new TournamentWinnerFact(
                "champ_1", "Champion", true, false, score: 100f, honor: 0, calculating: 1, townId: "town_1", townName: "Town");

            var partA = new TournamentParticipantFact("p_a", "PA", 85f, 0, 1, 60, false, false, false);
            var partB = new TournamentParticipantFact("p_b", "PB", 85f, 0, 1, 60, false, false, false);
            var partC = new TournamentParticipantFact("p_c", "PC", 85f, 0, 1, 60, false, false, false);

            var result1 = TournamentThrowEvaluation.Evaluate(
                champion, new[] { partA, partB, partC }, new VividWorldConfig(), 9999, "town_1", 10.0);
            var result2 = TournamentThrowEvaluation.Evaluate(
                champion, new[] { partA, partB, partC }, new VividWorldConfig(), 9999, "town_1", 10.0);

            Assert.True(result1.IsThrown);
            Assert.True(result2.IsThrown);
            Assert.NotNull(result1.ThrowerHeroId);
            Assert.Equal(result1.ThrowerHeroId, result2.ThrowerHeroId);
            // Harmed hero ids contain other 2 participants
            Assert.Equal(2, result1.HarmedHeroIds.Count);
            Assert.DoesNotContain(result1.ThrowerHeroId, result1.HarmedHeroIds);
            Assert.DoesNotContain("champ_1", result1.HarmedHeroIds);
        }

        [Fact]
        public void Step4_ChanceRoll_MissStops_HitSucceeds()
        {
            var champion = new TournamentWinnerFact(
                "champ_1", "Champion", true, false, score: 100f, honor: 0, calculating: 1, townId: "town_1", townName: "Town");

            var part = new TournamentParticipantFact("p_1", "P1", 85f, 0, 1, 60, false, false, false);

            var cfgZeroChance = new VividWorldConfig();
            cfgZeroChance.Secrets.TournamentThrow.Chance = 0.0;

            var resultMiss = TournamentThrowEvaluation.Evaluate(
                champion, new[] { part }, cfgZeroChance, 12345, "town_1", 10.0);

            Assert.False(resultMiss.IsThrown);
            Assert.Contains("chance missed", resultMiss.StopReason);

            var cfgFullChance = new VividWorldConfig();
            cfgFullChance.Secrets.TournamentThrow.Chance = 1.0;

            var resultHit = TournamentThrowEvaluation.Evaluate(
                champion, new[] { part }, cfgFullChance, 12345, "town_1", 10.0);

            Assert.True(resultHit.IsThrown);
            Assert.Empty(resultHit.StopReason);
        }

        [Fact]
        public void WillingAndReluctant_FourCombinations_ExpectedGrudge()
        {
            var champion = new TournamentWinnerFact(
                "champ_1", "Champion", true, false, score: 100f, honor: 0, calculating: 1, townId: "town_1", townName: "Town");

            // (1) honor < 0, relation > line (kin relation 40 > 30) => willing (no grudge)
            var p1 = new TournamentParticipantFact("p1", "P1", 80f, honor: -1, calculating: 1, relationWithWinner: 40, isKinWithWinner: true, isClanLeaderWinner: true, isKingdomLeaderWinner: false);
            var r1 = TournamentThrowEvaluation.Evaluate(champion, new[] { p1 }, new VividWorldConfig(), 12345, "town_1", 10.0);
            Assert.True(r1.IsWilling);

            // (2) honor < 0, relation <= line (kin relation 10 <= 30, qualifies via clan leader) => willing (no grudge)
            var p2 = new TournamentParticipantFact("p2", "P2", 80f, honor: -1, calculating: 1, relationWithWinner: 10, isKinWithWinner: true, isClanLeaderWinner: true, isKingdomLeaderWinner: false);
            var r2 = TournamentThrowEvaluation.Evaluate(champion, new[] { p2 }, new VividWorldConfig(), 12345, "town_1", 10.0);
            Assert.True(r2.IsWilling);

            // (3) honor >= 0, relation > line (kin relation 40 > 30) => willing (no grudge)
            var p3 = new TournamentParticipantFact("p3", "P3", 80f, honor: 0, calculating: 1, relationWithWinner: 40, isKinWithWinner: true, isClanLeaderWinner: true, isKingdomLeaderWinner: false);
            var r3 = TournamentThrowEvaluation.Evaluate(champion, new[] { p3 }, new VividWorldConfig(), 12345, "town_1", 10.0);
            Assert.True(r3.IsWilling);

            // (4) honor >= 0, relation <= line (kin relation 10 <= 30, qualifies via clan leader) => reluctant (grudge applied)
            var p4 = new TournamentParticipantFact("p4", "P4", 80f, honor: 0, calculating: 1, relationWithWinner: 10, isKinWithWinner: true, isClanLeaderWinner: true, isKingdomLeaderWinner: false);
            var r4 = TournamentThrowEvaluation.Evaluate(champion, new[] { p4 }, new VividWorldConfig(), 12345, "town_1", 10.0);
            Assert.False(r4.IsWilling);
            Assert.Equal(-6.0, r4.ReluctantGrudge);
        }

        [Fact]
        public void OnHeard_NineGrid_DeductionsAndLiegeExemption_NoClanEscalation()
        {
            var tmpl = LoadShippingTemplate();

            var cfg = new VividWorldConfig();
            var rel = new FakeSecretRelationshipLookup();
            var traits = new FakeHeroTraitLookup();

            int[] honorLevels = { 1, 0, -1 };
            double[] expectedChampionDeductions = { -5.0, -3.0, -1.0 };
            double[] expectedFriendThrowerDeductions = { -3.0, -2.0, 0.0 };

            for (int i = 0; i < honorLevels.Length; i++)
            {
                int honor = honorLevels[i];
                traits.Set(new TraitProfile { HeroId = "harmed_hero", Honor = honor });

                // 1. Friendship path: harmed hears, champion penalized [-5, -3, -1], thrower penalized [-3, -2, 0]
                var evtFriend = new WorldEvent
                {
                    EventId = $"evt_friend_{i}",
                    Type = "tournament_final_thrown",
                    Origin = EventOrigin.Secret,
                    SecretPath = "friendship",
                    SecretOwnerHeroIds = new List<string> { "lord_champ", "lord_thrower" },
                    SecretHarmedHeroIds = new List<string> { "harmed_hero" },
                    Participants = new Dictionary<string, string>
                    {
                        ["champion"] = "lord_champ",
                        ["thrower"] = "lord_thrower"
                    },
                    State = new RumorState { Leaked = true, LeakedDay = 5.0, LeakerHeroId = "lord_champ" }
                };

                var hearerFriend = new KnownByEntry
                {
                    HeroId = "harmed_hero",
                    Hop = 1,
                    LearnedDay = 6.0,
                    Believes = true
                };
                evtFriend.KnownBy.Add(hearerFriend);

                var outcomeFriend = SecretReactionEvaluator.Evaluate(
                    evtFriend, hearerFriend, tmpl, rel, traits, cfg, null, "player_hero", 6.0);

                Assert.True(outcomeFriend.Handled);
                Assert.Equal(2, outcomeFriend.Changes.Count);

                var champChange = outcomeFriend.Changes.FirstOrDefault(c => c.TargetHeroId == "lord_champ");
                Assert.NotNull(champChange);
                Assert.Equal(expectedChampionDeductions[i], champChange!.Amount);
                Assert.False(champChange.EscalateClan);

                var throwerChange = outcomeFriend.Changes.FirstOrDefault(c => c.TargetHeroId == "lord_thrower");
                Assert.NotNull(throwerChange);
                Assert.Equal(expectedFriendThrowerDeductions[i], throwerChange!.Amount);
                Assert.False(throwerChange.EscalateClan);

                // 2. Liege path: champion penalized [-5, -3, -1], thrower exempted (0 deduction, omitted from changes)
                var evtLiege = new WorldEvent
                {
                    EventId = $"evt_liege_{i}",
                    Type = "tournament_final_thrown",
                    Origin = EventOrigin.Secret,
                    SecretPath = "liege",
                    SecretOwnerHeroIds = new List<string> { "lord_champ", "lord_thrower" },
                    SecretHarmedHeroIds = new List<string> { "harmed_hero" },
                    Participants = new Dictionary<string, string>
                    {
                        ["champion"] = "lord_champ",
                        ["thrower"] = "lord_thrower"
                    },
                    State = new RumorState { Leaked = true, LeakedDay = 5.0, LeakerHeroId = "lord_champ" }
                };

                var hearerLiege = new KnownByEntry
                {
                    HeroId = "harmed_hero",
                    Hop = 1,
                    LearnedDay = 6.0,
                    Believes = true
                };
                evtLiege.KnownBy.Add(hearerLiege);

                var outcomeLiege = SecretReactionEvaluator.Evaluate(
                    evtLiege, hearerLiege, tmpl, rel, traits, cfg, null, "player_hero", 6.0);

                Assert.True(outcomeLiege.Handled);
                // In liege path, thrower deduction is skipped entirely
                Assert.Single(outcomeLiege.Changes);

                var liegeChampChange = outcomeLiege.Changes[0];
                Assert.Equal("lord_champ", liegeChampChange.TargetHeroId);
                Assert.Equal(expectedChampionDeductions[i], liegeChampChange.Amount);
                Assert.False(liegeChampChange.EscalateClan);
            }
        }
    }
}
