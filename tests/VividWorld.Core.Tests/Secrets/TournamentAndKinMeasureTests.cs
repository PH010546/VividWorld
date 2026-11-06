using System.Collections.Generic;
using VividWorld.Core.Secrets;
using Xunit;

namespace VividWorld.Core.Tests.Secrets
{
    public class TournamentAndKinMeasureTests
    {
        [Fact]
        public void Score_At75PercentOfWinner_DoesNotCount_AboveCounts_BelowWinnerAbove75PercentCounts()
        {
            var winner = new TournamentWinnerFact(
                "winner_1",
                "Winner",
                isLord: true,
                isWanderer: false,
                score: 100f,
                honor: 0,
                calculating: 1,
                townId: "town_1",
                townName: "Town");

            // Exactly 75% of winner score (100 * 0.75 = 75.0)
            var partExact75 = new TournamentParticipantFact(
                "part_exact75",
                "PartExact75",
                score: 75.0f,
                honor: 0,
                calculating: 1,
                relationWithWinner: 60,
                isKinWithWinner: false,
                isClanLeaderWinner: true,
                isKingdomLeaderWinner: false);

            // Slightly below 75%
            var partBelow75 = new TournamentParticipantFact(
                "part_below75",
                "PartBelow75",
                score: 74.9f,
                honor: 0,
                calculating: 1,
                relationWithWinner: 60,
                isKinWithWinner: false,
                isClanLeaderWinner: true,
                isKingdomLeaderWinner: false);

            // Slightly above 75% (75.1f)
            var partAbove75 = new TournamentParticipantFact(
                "part_above75",
                "PartAbove75",
                score: 75.1f,
                honor: 0,
                calculating: 1,
                relationWithWinner: 60,
                isKinWithWinner: false,
                isClanLeaderWinner: true,
                isKingdomLeaderWinner: false);

            // Below winner (90f) but well above 75%
            var partBelowWinnerAbove75 = new TournamentParticipantFact(
                "part_90",
                "Part90",
                score: 90.0f,
                honor: 0,
                calculating: 1,
                relationWithWinner: 60,
                isKinWithWinner: false,
                isClanLeaderWinner: true,
                isKingdomLeaderWinner: false);

            // Equal to winner (100f)
            var partEqualWinner = new TournamentParticipantFact(
                "part_100",
                "Part100",
                score: 100.0f,
                honor: 0,
                calculating: 1,
                relationWithWinner: 60,
                isKinWithWinner: false,
                isClanLeaderWinner: true,
                isKingdomLeaderWinner: false);

            // Above winner (110f)
            var partAboveWinner = new TournamentParticipantFact(
                "part_110",
                "Part110",
                score: 110.0f,
                honor: 0,
                calculating: 1,
                relationWithWinner: 60,
                isKinWithWinner: false,
                isClanLeaderWinner: true,
                isKingdomLeaderWinner: false);

            var evalExact75 = TournamentAndKinMeasure.EvaluateParticipant(winner, partExact75);
            var evalBelow75 = TournamentAndKinMeasure.EvaluateParticipant(winner, partBelow75);
            var evalAbove75 = TournamentAndKinMeasure.EvaluateParticipant(winner, partAbove75);
            var evalBelowWinner = TournamentAndKinMeasure.EvaluateParticipant(winner, partBelowWinnerAbove75);
            var evalEqualWinner = TournamentAndKinMeasure.EvaluateParticipant(winner, partEqualWinner);
            var evalAboveWinner = TournamentAndKinMeasure.EvaluateParticipant(winner, partAboveWinner);

            Assert.False(evalExact75.HasChanceToWin);
            Assert.False(evalExact75.IsQualified);
            Assert.Contains("分數不到冠軍的 0.75 倍", evalExact75.FormatVerdict());

            Assert.False(evalBelow75.HasChanceToWin);
            Assert.False(evalBelow75.IsQualified);
            Assert.Contains("分數不到冠軍的 0.75 倍", evalBelow75.FormatVerdict());

            Assert.True(evalAbove75.HasChanceToWin);
            Assert.True(evalAbove75.IsQualified);
            Assert.True(evalAbove75.QualifiesMasterServant);
            Assert.Contains("夠格", evalAbove75.FormatVerdict());

            Assert.True(evalBelowWinner.HasChanceToWin);
            Assert.True(evalBelowWinner.IsQualified);

            Assert.True(evalEqualWinner.HasChanceToWin);
            Assert.True(evalEqualWinner.IsQualified);

            Assert.True(evalAboveWinner.HasChanceToWin);
            Assert.True(evalAboveWinner.IsQualified);
        }

        [Fact]
        public void MasterServant_MustPassScoreFirst_ThenIgnoresRelationAndPersonality()
        {
            var winner = new TournamentWinnerFact(
                "winner_1",
                "Winner",
                isLord: true,
                isWanderer: false,
                score: 100f,
                honor: 0,
                calculating: -1,
                townId: "town_1",
                townName: "Town");

            // Clan leader as winner, but score <= 75%
            var partFailScore = new TournamentParticipantFact(
                "part_fail_score",
                "PartFailScore",
                score: 70f,
                honor: 0,
                calculating: 2,
                relationWithWinner: 100,
                isKinWithWinner: false,
                isClanLeaderWinner: true,
                isKingdomLeaderWinner: false);

            // Clan leader as winner, score > 75%, deeply negative relation, calculating negative
            var partClan = new TournamentParticipantFact(
                "part_clan",
                "PartClan",
                score: 80f,
                honor: 0,
                calculating: -2,
                relationWithWinner: -40,
                isKinWithWinner: false,
                isClanLeaderWinner: true,
                isKingdomLeaderWinner: false);

            // Kingdom leader as winner, score > 75%, neutral relation, calculating negative, honor negative
            var partKingdom = new TournamentParticipantFact(
                "part_king",
                "PartKingdom",
                score: 85f,
                honor: -1,
                calculating: -1,
                relationWithWinner: 0,
                isKinWithWinner: false,
                isClanLeaderWinner: false,
                isKingdomLeaderWinner: true);

            var evalFailScore = TournamentAndKinMeasure.EvaluateParticipant(winner, partFailScore);
            var evalClan = TournamentAndKinMeasure.EvaluateParticipant(winner, partClan);
            var evalKingdom = TournamentAndKinMeasure.EvaluateParticipant(winner, partKingdom);

            // Fails at score first
            Assert.False(evalFailScore.HasChanceToWin);
            Assert.False(evalFailScore.IsQualified);
            Assert.Contains("分數不到冠軍的 0.75 倍", evalFailScore.FormatVerdict());

            // Passed score, qualifies master-servant ignoring relation and calculating
            Assert.True(evalClan.HasChanceToWin);
            Assert.True(evalClan.QualifiesMasterServant);
            Assert.False(evalClan.QualifiesFriendship);
            Assert.True(evalClan.IsQualified);
            Assert.False(evalClan.IsWilling); // honor >= 0 and relation <= 50 => reluctant
            Assert.Contains("主從", evalClan.FormatVerdict());
            Assert.Contains("不情願", evalClan.FormatVerdict());

            Assert.True(evalKingdom.HasChanceToWin);
            Assert.True(evalKingdom.QualifiesMasterServant);
            Assert.False(evalKingdom.QualifiesFriendship);
            Assert.True(evalKingdom.IsQualified);
            Assert.True(evalKingdom.IsWilling); // honor < 0 => willing
            Assert.Contains("主從", evalKingdom.FormatVerdict());
            Assert.Contains("甘願", evalKingdom.FormatVerdict());
        }

        [Fact]
        public void Friendship_Kin_BoundariesAndRequirements()
        {
            var winner = new TournamentWinnerFact(
                "winner_1",
                "Winner",
                isLord: true,
                isWanderer: false,
                score: 100f,
                honor: 0,
                calculating: 1,
                townId: "town_1",
                townName: "Town");

            // Kin with relation exactly 30, honor 0
            var kinAt30 = new TournamentParticipantFact(
                "kin_30",
                "Kin30",
                score: 80f,
                honor: 0,
                calculating: 1,
                relationWithWinner: 30,
                isKinWithWinner: true,
                isClanLeaderWinner: false,
                isKingdomLeaderWinner: false);

            // Kin with relation 31, honor 0, calculating negative
            var kinAt31Honor0CalcNeg = new TournamentParticipantFact(
                "kin_31_calc_neg",
                "Kin31CalcNeg",
                score: 80f,
                honor: 0,
                calculating: -2,
                relationWithWinner: 31,
                isKinWithWinner: true,
                isClanLeaderWinner: false,
                isKingdomLeaderWinner: false);

            // Kin with relation 31, honor 1 (positive), calculating positive
            var kinAt31Honor1 = new TournamentParticipantFact(
                "kin_31_honor1",
                "Kin31Honor1",
                score: 80f,
                honor: 1,
                calculating: 2,
                relationWithWinner: 31,
                isKinWithWinner: true,
                isClanLeaderWinner: false,
                isKingdomLeaderWinner: false);

            // Kin with relation 20, honor 1
            var kinRelation20Honor1 = new TournamentParticipantFact(
                "kin_20_honor1",
                "Kin20Honor1",
                score: 80f,
                honor: 1,
                calculating: 2,
                relationWithWinner: 20,
                isKinWithWinner: true,
                isClanLeaderWinner: false,
                isKingdomLeaderWinner: false);

            var evalAt30 = TournamentAndKinMeasure.EvaluateParticipant(winner, kinAt30);
            var evalAt31Honor0 = TournamentAndKinMeasure.EvaluateParticipant(winner, kinAt31Honor0CalcNeg);
            var evalAt31Honor1 = TournamentAndKinMeasure.EvaluateParticipant(winner, kinAt31Honor1);
            var evalKin20Honor1 = TournamentAndKinMeasure.EvaluateParticipant(winner, kinRelation20Honor1);

            // Exactly 30 does not count
            Assert.False(evalAt30.QualifiesKinFriendship);
            Assert.False(evalAt30.IsQualified);
            Assert.Contains("kin relation 30 <= 30", evalAt30.FailureReason);

            // 31 and honor <= 0 counts (calculating ignored for kin)
            Assert.True(evalAt31Honor0.QualifiesKinFriendship);
            Assert.True(evalAt31Honor0.IsQualified);
            Assert.Contains("自家人交情", evalAt31Honor0.FormatVerdict());

            // 31 and honor 1 does not count (honor > 0)
            Assert.False(evalAt31Honor1.QualifiesKinFriendship);
            Assert.False(evalAt31Honor1.IsQualified);
            Assert.Contains("kin participant honor 1 > 0", evalAt31Honor1.FailureReason);

            // Both relation <= 30 and honor > 0 listed in failure, no calculating
            Assert.Contains("kin relation 20 <= 30", evalKin20Honor1.FailureReason);
            Assert.Contains("kin participant honor 1 > 0", evalKin20Honor1.FailureReason);
            Assert.DoesNotContain("calculating", evalKin20Honor1.FailureReason);
        }

        [Fact]
        public void Friendship_Other_BoundariesAndRequirements()
        {
            var winner = new TournamentWinnerFact(
                "winner_1",
                "Winner",
                isLord: true,
                isWanderer: false,
                score: 100f,
                honor: 0,
                calculating: 1,
                townId: "town_1",
                townName: "Town");

            // Other with relation exactly 50, both calculating > 0
            var otherAt50 = new TournamentParticipantFact(
                "other_50",
                "Other50",
                score: 80f,
                honor: 1,
                calculating: 1,
                relationWithWinner: 50,
                isKinWithWinner: false,
                isClanLeaderWinner: false,
                isKingdomLeaderWinner: false);

            // Other with relation 51, both calculating > 0, honor 1
            var otherAt51 = new TournamentParticipantFact(
                "other_51",
                "Other51",
                score: 80f,
                honor: 1,
                calculating: 1,
                relationWithWinner: 51,
                isKinWithWinner: false,
                isClanLeaderWinner: false,
                isKingdomLeaderWinner: false);

            // Other with relation 51, participant calculating is 0
            var otherCalcZero = new TournamentParticipantFact(
                "other_calc0",
                "OtherCalc0",
                score: 80f,
                honor: 1,
                calculating: 0,
                relationWithWinner: 51,
                isKinWithWinner: false,
                isClanLeaderWinner: false,
                isKingdomLeaderWinner: false);

            // Other with relation 51, winner calculating is 0
            var winnerCalcZero = new TournamentWinnerFact(
                "winner_calc0",
                "WinnerCalc0",
                isLord: true,
                isWanderer: false,
                score: 100f,
                honor: 0,
                calculating: 0,
                townId: "town_1",
                townName: "Town");

            // Other with relation 40, participant calculating 0
            var otherRel40Calc0 = new TournamentParticipantFact(
                "other_40_0",
                "Other40_0",
                score: 80f,
                honor: 1,
                calculating: 0,
                relationWithWinner: 40,
                isKinWithWinner: false,
                isClanLeaderWinner: false,
                isKingdomLeaderWinner: false);

            var evalAt50 = TournamentAndKinMeasure.EvaluateParticipant(winner, otherAt50);
            var evalAt51 = TournamentAndKinMeasure.EvaluateParticipant(winner, otherAt51);
            var evalCalcZero = TournamentAndKinMeasure.EvaluateParticipant(winner, otherCalcZero);
            var evalWinnerCalcZero = TournamentAndKinMeasure.EvaluateParticipant(winnerCalcZero, otherAt51);
            var evalRel40Calc0 = TournamentAndKinMeasure.EvaluateParticipant(winner, otherRel40Calc0);

            // Exactly 50 does not count
            Assert.False(evalAt50.QualifiesOtherFriendship);
            Assert.False(evalAt50.IsQualified);
            Assert.Contains("non-kin relation 50 <= 50", evalAt50.FailureReason);

            // 51 and both calculating positive counts even if honor is 1
            Assert.True(evalAt51.QualifiesOtherFriendship);
            Assert.True(evalAt51.IsQualified);
            Assert.Contains("其他人交情", evalAt51.FormatVerdict());

            // Participant calc <= 0 disqualifies
            Assert.False(evalCalcZero.QualifiesOtherFriendship);
            Assert.Contains("calculating not both positive", evalCalcZero.FailureReason);

            // Winner calc <= 0 disqualifies
            Assert.False(evalWinnerCalcZero.QualifiesOtherFriendship);
            Assert.Contains("calculating not both positive", evalWinnerCalcZero.FailureReason);

            // Lists both relation <= 50 and calculating not both positive
            Assert.Contains("non-kin relation 40 <= 50", evalRel40Calc0.FailureReason);
            Assert.Contains("calculating not both positive", evalRel40Calc0.FailureReason);
        }

        [Fact]
        public void Willingness_HonorNegativeOrRelationLineCrossed_DistinguishesWillingAndReluctant()
        {
            var winner = new TournamentWinnerFact(
                "winner_1",
                "Winner",
                isLord: true,
                isWanderer: false,
                score: 100f,
                honor: 0,
                calculating: 0, // friendship won't trigger for non-kin
                townId: "town_1",
                townName: "Town");

            // MasterServant: Honor < 0 => Willing
            var partHonorNeg = new TournamentParticipantFact(
                "p_neg",
                "PNeg",
                score: 80f,
                honor: -1,
                calculating: 0,
                relationWithWinner: 10,
                isKinWithWinner: false,
                isClanLeaderWinner: true,
                isKingdomLeaderWinner: false);

            // MasterServant: Honor >= 0, relation <= 50 (non-kin) => Reluctant
            var partReluctant = new TournamentParticipantFact(
                "p_rel",
                "PRel",
                score: 80f,
                honor: 1,
                calculating: 0,
                relationWithWinner: 10,
                isKinWithWinner: false,
                isClanLeaderWinner: true,
                isKingdomLeaderWinner: false);

            // MasterServant: Honor >= 0, but relation > 50 (non-kin) => Willing
            var partHighRelOther = new TournamentParticipantFact(
                "p_high_other",
                "PHighOther",
                score: 80f,
                honor: 1,
                calculating: 0,
                relationWithWinner: 55,
                isKinWithWinner: false,
                isClanLeaderWinner: true,
                isKingdomLeaderWinner: false);

            // MasterServant: Honor >= 0, but relation > 30 (kin) => Willing
            var partHighRelKin = new TournamentParticipantFact(
                "p_high_kin",
                "PHighKin",
                score: 80f,
                honor: 1,
                calculating: 0,
                relationWithWinner: 35,
                isKinWithWinner: true,
                isClanLeaderWinner: true,
                isKingdomLeaderWinner: false);

            Assert.True(TournamentAndKinMeasure.EvaluateParticipant(winner, partHonorNeg).IsWilling);
            Assert.False(TournamentAndKinMeasure.EvaluateParticipant(winner, partReluctant).IsWilling);
            Assert.True(TournamentAndKinMeasure.EvaluateParticipant(winner, partHighRelOther).IsWilling);
            Assert.True(TournamentAndKinMeasure.EvaluateParticipant(winner, partHighRelKin).IsWilling);
        }

        [Fact]
        public void Tally_NewParticipantCounters_IncludingWinnerWillAskGroup()
        {
            var winnerPos = new TournamentWinnerFact(
                "winner_pos",
                "WinnerPos",
                isLord: true,
                isWanderer: false,
                score: 100f,
                honor: 1, // positive: will not ask
                calculating: 1,
                townId: "town_1",
                townName: "Town");

            var winnerZero = new TournamentWinnerFact(
                "winner_zero",
                "WinnerZero",
                isLord: true,
                isWanderer: false,
                score: 100f,
                honor: 0, // non-positive: will ask
                calculating: 1,
                townId: "town_2",
                townName: "Town2");

            // Match 1 participants (winnerPos):
            // P1: score 70 (score <= 75)
            var p1 = new TournamentParticipantFact("p1", "P1", 70f, 0, 1, 10, false, false, false);
            // P2: score 80 (score > 75, qualifies master servant)
            var p2 = new TournamentParticipantFact("p2", "P2", 80f, 0, 1, 10, false, true, false);
            // P3: score 85 (score > 75, kin relation 35, honor 0 -> qualifies kin friendship)
            var p3 = new TournamentParticipantFact("p3", "P3", 85f, 0, -1, 35, true, false, false);

            var evalP1 = TournamentAndKinMeasure.EvaluateParticipant(winnerPos, p1);
            var evalP2 = TournamentAndKinMeasure.EvaluateParticipant(winnerPos, p2);
            var evalP3 = TournamentAndKinMeasure.EvaluateParticipant(winnerPos, p3);

            // Match 2 participants (winnerZero):
            // P4: score 60 (score <= 75)
            var p4 = new TournamentParticipantFact("p4", "P4", 60f, 0, 1, 10, false, false, false);
            // P5: score 90 (score > 75, other relation 55, calc 1 -> qualifies other friendship)
            var p5 = new TournamentParticipantFact("p5", "P5", 90f, 0, 1, 55, false, false, false);

            var evalP4 = TournamentAndKinMeasure.EvaluateParticipant(winnerZero, p4);
            var evalP5 = TournamentAndKinMeasure.EvaluateParticipant(winnerZero, p5);

            var tally = new TournamentMeasureTally();

            tally.RecordWinnerHeroMatch(10.0, winnerPos, new[] { evalP1, evalP2, evalP3 });

            Assert.Equal(1, tally.TotalTournaments);
            Assert.Equal(1, tally.WinnerIsHeroTournaments);
            Assert.Equal(1, tally.WinnerHonorPositiveTournaments);
            Assert.Equal(0, tally.HasQualifiedAndWinnerWillAskTournaments); // will not ask!

            // Overall match 1 counters
            Assert.Equal(3, tally.TotalHeroParticipants);
            Assert.Equal(2, tally.PassedScoreParticipants); // P2, P3
            Assert.Equal(2, tally.QualifiedParticipants);
            Assert.Equal(1, tally.QualifiedMasterServantParticipants); // P2
            Assert.Equal(1, tally.QualifiedKinFriendshipParticipants); // P3
            Assert.Equal(0, tally.QualifiedOtherFriendshipParticipants);

            // WillAsk counters should remain 0 because winner honor > 0
            Assert.Equal(0, tally.WillAskHeroParticipants);
            Assert.Equal(0, tally.WillAskPassedScoreParticipants);
            Assert.Equal(0, tally.WillAskQualifiedParticipants);
            Assert.Equal(0, tally.WillAskQualifiedMasterServantParticipants);
            Assert.Equal(0, tally.WillAskQualifiedKinFriendshipParticipants);
            Assert.Equal(0, tally.WillAskQualifiedOtherFriendshipParticipants);

            tally.RecordWinnerHeroMatch(12.0, winnerZero, new[] { evalP4, evalP5 });

            Assert.Equal(2, tally.TotalTournaments);
            Assert.Equal(2, tally.WinnerIsHeroTournaments);
            Assert.Equal(1, tally.WinnerHonorPositiveTournaments);
            Assert.Equal(1, tally.HasQualifiedAndWinnerWillAskTournaments); // will ask!

            // Overall totals after match 2
            Assert.Equal(5, tally.TotalHeroParticipants);
            Assert.Equal(3, tally.PassedScoreParticipants); // P2, P3, P5
            Assert.Equal(3, tally.QualifiedParticipants); // P2, P3, P5
            Assert.Equal(1, tally.QualifiedMasterServantParticipants); // P2
            Assert.Equal(1, tally.QualifiedKinFriendshipParticipants); // P3
            Assert.Equal(1, tally.QualifiedOtherFriendshipParticipants); // P5

            // WillAsk totals after match 2 (only match 2 counted)
            Assert.Equal(2, tally.WillAskHeroParticipants);
            Assert.Equal(1, tally.WillAskPassedScoreParticipants); // P5
            Assert.Equal(1, tally.WillAskQualifiedParticipants); // P5
            Assert.Equal(0, tally.WillAskQualifiedMasterServantParticipants);
            Assert.Equal(0, tally.WillAskQualifiedKinFriendshipParticipants);
            Assert.Equal(1, tally.WillAskQualifiedOtherFriendshipParticipants); // P5

            var reportLines = tally.FormatReportLines(12.0);
            Assert.Equal(3, reportLines.Count);
            Assert.Contains("hero participants (excl winner): 5, passed score (>0.75x): 3, qualified: 3 (master-servant: 1, kin-friendship: 1, other-friendship: 1)", reportLines[1]);
            Assert.Contains("winner will ask (honor <= 0): hero participants: 2, passed score (>0.75x): 1, qualified: 1 (master-servant: 0, kin-friendship: 0, other-friendship: 1)", reportLines[2]);
        }

        [Fact]
        public void KinRelation_Over30AndBothHonorNonPositive_Boundaries()
        {
            var pairs = new List<KinPairFact>
            {
                // r = 30, honor (0, 0): not counted in >30
                new KinPairFact("h1", "H1", 1, "h2", "H2", 1, 30, hero1Honor: 0, hero2Honor: 0),
                // r = 31, honor (0, 0): counted in >30 and both honor <= 0
                new KinPairFact("h1", "H1", 1, "h3", "H3", 1, 31, hero1Honor: 0, hero2Honor: 0),
                // r = 31, honor (1, 0): counted in >30, NOT in both honor <= 0
                new KinPairFact("h1", "H1", 1, "h4", "H4", 1, 31, hero1Honor: 1, hero2Honor: 0),
                // r = 31, honor (0, 1): counted in >30, NOT in both honor <= 0
                new KinPairFact("h1", "H1", 1, "h5", "H5", 1, 31, hero1Honor: 0, hero2Honor: 1),
                // r = 31, honor (1, 1): counted in >30, NOT in both honor <= 0
                new KinPairFact("h1", "H1", 1, "h6", "H6", 1, 31, hero1Honor: 1, hero2Honor: 1),
                // r = 55, honor (-1, 0): counted in >30 and both honor <= 0
                new KinPairFact("h1", "H1", 1, "h7", "H7", 1, 55, hero1Honor: -1, hero2Honor: 0),
                // r = 55, honor (-1, -1): counted in >30 and both honor <= 0
                new KinPairFact("h1", "H1", 1, "h8", "H8", 1, 55, hero1Honor: -1, hero2Honor: -1),
                // r = 10, honor (-1, -1): not counted in >30
                new KinPairFact("h1", "H1", 1, "h9", "H9", 1, 10, hero1Honor: -1, hero2Honor: -1)
            };

            var tally = TournamentAndKinMeasure.ComputeKinTally(pairs);

            Assert.Equal(8, tally.TotalPairs);
            Assert.Equal(6, tally.CountOver30); // 31x4, 55x2
            Assert.Equal(3, tally.CountOver30BothHonorNonPositive); // (31, 0,0), (55, -1,0), (55, -1,-1)

            var reportLines = tally.FormatReportLines();
            Assert.Contains(reportLines, line => line == "kin relation measure: relation > 30 and both honor <= 0: 3 pair(s)");
        }

        [Fact]
        public void KinRelation_DistributionBoundaries_CoversAllBins()
        {
            var pairs = new List<KinPairFact>
            {
                new KinPairFact("h1", "H1", 1, "h2", "H2", 1, -5),  // < 0
                new KinPairFact("h1", "H1", 1, "h3", "H3", 1, 0),   // 0
                new KinPairFact("h1", "H1", 1, "h4", "H4", 1, 1),   // 1~10
                new KinPairFact("h1", "H1", 1, "h5", "H5", 1, 10),  // 1~10
                new KinPairFact("h1", "H1", 1, "h6", "H6", 1, 11),  // 11~20
                new KinPairFact("h1", "H1", 1, "h7", "H7", 1, 20),  // 11~20
                new KinPairFact("h1", "H1", 1, "h8", "H8", 1, 21),  // 21~30
                new KinPairFact("h1", "H1", 1, "h9", "H9", 1, 30),  // 21~30
                new KinPairFact("h1", "H1", 1, "h10", "H10", 1, 31), // 31~50, both calc > 0
                new KinPairFact("h1", "H1", 1, "h11", "H11", 0, 50), // 31~50, one calc <= 0
                new KinPairFact("h1", "H1", 2, "h12", "H12", 2, 51)  // > 50, both calc > 0
            };

            var tally = TournamentAndKinMeasure.ComputeKinTally(pairs);

            Assert.Equal(11, tally.TotalPairs);
            Assert.Equal(1, tally.CountNegative);
            Assert.Equal(1, tally.CountZero);
            Assert.Equal(2, tally.Count1To10);
            Assert.Equal(2, tally.Count11To20);
            Assert.Equal(2, tally.Count21To30);
            Assert.Equal(2, tally.Count31To50);
            Assert.Equal(1, tally.CountOver50);

            Assert.Equal(3, tally.CountOver30); // 31, 50, 51
            Assert.Equal(2, tally.CountOver30BothCalculatingPositive); // 31 (calc 1,1) and 51 (calc 2,2)
            Assert.Equal(3, tally.CountOver30BothHonorNonPositive); // All 3 have default honor 0,0

            // Top 10 has 10 elements and highest is relation 51
            Assert.Equal(10, tally.Top10HighestPairs.Count);
            Assert.Equal(51, tally.Top10HighestPairs[0].Relation);
            Assert.Equal(50, tally.Top10HighestPairs[1].Relation);
            Assert.Equal(31, tally.Top10HighestPairs[2].Relation);
        }
    }
}
