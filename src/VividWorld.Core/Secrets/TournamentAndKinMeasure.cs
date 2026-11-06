using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace VividWorld.Core.Secrets
{
    public sealed class TournamentWinnerFact
    {
        public string HeroId { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public bool IsLord { get; set; }
        public bool IsWanderer { get; set; }
        public float Score { get; set; }
        public int Honor { get; set; }
        public int Calculating { get; set; }
        public string TownId { get; set; } = string.Empty;
        public string TownName { get; set; } = string.Empty;

        public bool IsHonorPositive => Honor > 0;

        public TournamentWinnerFact() { }

        public TournamentWinnerFact(
            string heroId,
            string name,
            bool isLord,
            bool isWanderer,
            float score,
            int honor,
            int calculating,
            string townId,
            string townName)
        {
            HeroId = heroId ?? string.Empty;
            Name = name ?? string.Empty;
            IsLord = isLord;
            IsWanderer = isWanderer;
            Score = score;
            Honor = honor;
            Calculating = calculating;
            TownId = townId ?? string.Empty;
            TownName = townName ?? string.Empty;
        }

        public string RoleDescription => IsLord ? "lord" : (IsWanderer ? "wanderer" : "hero");
    }

    public sealed class TournamentParticipantFact
    {
        public string HeroId { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public float Score { get; set; }
        public int Honor { get; set; }
        public int Calculating { get; set; }
        public int RelationWithWinner { get; set; }
        public bool IsKinWithWinner { get; set; }
        public bool IsClanLeaderWinner { get; set; }
        public bool IsKingdomLeaderWinner { get; set; }

        public TournamentParticipantFact() { }

        public TournamentParticipantFact(
            string heroId,
            string name,
            float score,
            int honor,
            int calculating,
            int relationWithWinner,
            bool isKinWithWinner,
            bool isClanLeaderWinner,
            bool isKingdomLeaderWinner)
        {
            HeroId = heroId ?? string.Empty;
            Name = name ?? string.Empty;
            Score = score;
            Honor = honor;
            Calculating = calculating;
            RelationWithWinner = relationWithWinner;
            IsKinWithWinner = isKinWithWinner;
            IsClanLeaderWinner = isClanLeaderWinner;
            IsKingdomLeaderWinner = isKingdomLeaderWinner;
        }
    }

    public sealed class TournamentParticipantEvaluation
    {
        public TournamentParticipantFact Participant { get; set; } = new TournamentParticipantFact();
        public float ScoreRatio { get; set; }
        public bool HasChanceToWin { get; set; }
        public bool QualifiesMasterServant { get; set; }
        public bool QualifiesKinFriendship { get; set; }
        public bool QualifiesOtherFriendship { get; set; }
        public bool QualifiesFriendship => QualifiesKinFriendship || QualifiesOtherFriendship;
        public bool IsWilling { get; set; }
        public bool PassedRelationLine { get; set; }
        public string FailureReason { get; set; } = string.Empty;

        public bool IsQualified => QualifiesMasterServant || QualifiesKinFriendship || QualifiesOtherFriendship;

        public string FormatVerdict()
        {
            if (!HasChanceToWin)
            {
                return "分數不到冠軍的 0.75 倍 (score <= winner * 0.75)";
            }

            if (IsQualified)
            {
                var tags = new List<string>();
                if (QualifiesMasterServant) tags.Add("主從");
                if (QualifiesKinFriendship) tags.Add("自家人交情");
                if (QualifiesOtherFriendship) tags.Add("其他人交情");

                string willingText = IsWilling ? "甘願" : "不情願";
                return string.Format(
                    CultureInfo.InvariantCulture,
                    "夠格 [{0}] - {1}",
                    string.Join(", ", tags),
                    willingText);
            }

            return string.Format(
                CultureInfo.InvariantCulture,
                "未過 ({0})",
                FailureReason);
        }
    }

    public sealed class TournamentMeasureTally
    {
        public int TotalTournaments { get; set; }
        public int PlayerParticipatedTournaments { get; set; }
        public int WinnerNotHeroTournaments { get; set; }
        public int WinnerIsHeroTournaments { get; set; }
        public int WinnerHonorPositiveTournaments { get; set; }
        public int HasQualifiedAndWinnerWillAskTournaments { get; set; }
        public double FirstTournamentDay { get; set; } = -1;
        public double LastTournamentDay { get; set; } = -1;

        public int TotalHeroParticipants { get; set; }
        public int PassedScoreParticipants { get; set; }
        public int QualifiedParticipants { get; set; }
        public int QualifiedMasterServantParticipants { get; set; }
        public int QualifiedKinFriendshipParticipants { get; set; }
        public int QualifiedOtherFriendshipParticipants { get; set; }

        public int WillAskHeroParticipants { get; set; }
        public int WillAskPassedScoreParticipants { get; set; }
        public int WillAskQualifiedParticipants { get; set; }
        public int WillAskQualifiedMasterServantParticipants { get; set; }
        public int WillAskQualifiedKinFriendshipParticipants { get; set; }
        public int WillAskQualifiedOtherFriendshipParticipants { get; set; }

        public int ThrownTournaments { get; set; }
        public int ThrownLiegeTournaments { get; set; }
        public int ThrownFriendshipTournaments { get; set; }
        public int ThrownReluctantTournaments { get; set; }

        public double DaysElapsed(double currentDay)
        {
            if (FirstTournamentDay < 0) return 0;
            return Math.Max(0, currentDay - FirstTournamentDay);
        }

        private void TouchDay(double day)
        {
            if (FirstTournamentDay < 0)
            {
                FirstTournamentDay = day;
            }
            LastTournamentDay = day;
        }

        public void RecordPlayerParticipated(double day)
        {
            TotalTournaments++;
            PlayerParticipatedTournaments++;
            TouchDay(day);
        }

        public void RecordThrown(bool isLiege, bool isReluctant)
        {
            ThrownTournaments++;
            if (isLiege) ThrownLiegeTournaments++;
            else ThrownFriendshipTournaments++;
            if (isReluctant) ThrownReluctantTournaments++;
        }

        public void RecordWinnerNotHero(double day)
        {
            TotalTournaments++;
            WinnerNotHeroTournaments++;
            TouchDay(day);
        }

        public void RecordWinnerHeroMatch(
            double day,
            TournamentWinnerFact winner,
            IReadOnlyList<TournamentParticipantEvaluation> evaluations)
        {
            TotalTournaments++;
            WinnerIsHeroTournaments++;
            TouchDay(day);

            if (winner != null && winner.Honor > 0)
            {
                WinnerHonorPositiveTournaments++;
            }

            bool winnerWillAsk = winner != null && winner.Honor <= 0;
            bool hasQualified = evaluations != null && evaluations.Any(e => e.IsQualified);

            if (winnerWillAsk && hasQualified)
            {
                HasQualifiedAndWinnerWillAskTournaments++;
            }

            if (evaluations != null)
            {
                int pCount = evaluations.Count;
                int passScore = evaluations.Count(e => e.HasChanceToWin);
                int qual = evaluations.Count(e => e.IsQualified);
                int master = evaluations.Count(e => e.QualifiesMasterServant);
                int kinFriend = evaluations.Count(e => e.QualifiesKinFriendship);
                int otherFriend = evaluations.Count(e => e.QualifiesOtherFriendship);

                TotalHeroParticipants += pCount;
                PassedScoreParticipants += passScore;
                QualifiedParticipants += qual;
                QualifiedMasterServantParticipants += master;
                QualifiedKinFriendshipParticipants += kinFriend;
                QualifiedOtherFriendshipParticipants += otherFriend;

                if (winnerWillAsk)
                {
                    WillAskHeroParticipants += pCount;
                    WillAskPassedScoreParticipants += passScore;
                    WillAskQualifiedParticipants += qual;
                    WillAskQualifiedMasterServantParticipants += master;
                    WillAskQualifiedKinFriendshipParticipants += kinFriend;
                    WillAskQualifiedOtherFriendshipParticipants += otherFriend;
                }
            }
        }

        public List<string> FormatReportLines(double currentDay)
        {
            double elapsed = DaysElapsed(currentDay);
            var lines = new List<string>
            {
                string.Format(
                    CultureInfo.InvariantCulture,
                    "tournament measure: total {0} tournament(s): player participated {1}, winner not hero {2}, winner is hero {3} (honor > 0: {4}, qualified and winner will ask: {5}) | first day {6:F1}, last day {7:F1}, elapsed {8:F1} day(s)",
                    TotalTournaments,
                    PlayerParticipatedTournaments,
                    WinnerNotHeroTournaments,
                    WinnerIsHeroTournaments,
                    WinnerHonorPositiveTournaments,
                    HasQualifiedAndWinnerWillAskTournaments,
                    FirstTournamentDay,
                    LastTournamentDay,
                    elapsed),
                string.Format(
                    CultureInfo.InvariantCulture,
                    "tournament measure: hero participants (excl winner): {0}, passed score (>0.75x): {1}, qualified: {2} (master-servant: {3}, kin-friendship: {4}, other-friendship: {5})",
                    TotalHeroParticipants,
                    PassedScoreParticipants,
                    QualifiedParticipants,
                    QualifiedMasterServantParticipants,
                    QualifiedKinFriendshipParticipants,
                    QualifiedOtherFriendshipParticipants),
                string.Format(
                    CultureInfo.InvariantCulture,
                    "tournament measure: winner will ask (honor <= 0): hero participants: {0}, passed score (>0.75x): {1}, qualified: {2} (master-servant: {3}, kin-friendship: {4}, other-friendship: {5})",
                    WillAskHeroParticipants,
                    WillAskPassedScoreParticipants,
                    WillAskQualifiedParticipants,
                    WillAskQualifiedMasterServantParticipants,
                    WillAskQualifiedKinFriendshipParticipants,
                    WillAskQualifiedOtherFriendshipParticipants)
            };

            if (ThrownTournaments > 0)
            {
                lines.Add(string.Format(
                    CultureInfo.InvariantCulture,
                    "tournament measure: thrown tournaments: {0} (master-servant: {1}, friendship: {2}, reluctant: {3})",
                    ThrownTournaments,
                    ThrownLiegeTournaments,
                    ThrownFriendshipTournaments,
                    ThrownReluctantTournaments));
            }

            return lines;
        }

        public string FormatSummary(double currentDay)
        {
            return string.Join(Environment.NewLine, FormatReportLines(currentDay));
        }

        public string FormatDialogSummary(double currentDay)
        {
            double elapsed = DaysElapsed(currentDay);
            return string.Format(
                CultureInfo.InvariantCulture,
                "(dev) tournaments: total {0}, player {1}, not hero {2}, hero {3} (honor > 0: {4}, qualified & will ask: {5}), thrown {6} (master: {7}, friend: {8}, reluctant: {9}), elapsed {10:F1} days",
                TotalTournaments,
                PlayerParticipatedTournaments,
                WinnerNotHeroTournaments,
                WinnerIsHeroTournaments,
                WinnerHonorPositiveTournaments,
                HasQualifiedAndWinnerWillAskTournaments,
                ThrownTournaments,
                ThrownLiegeTournaments,
                ThrownFriendshipTournaments,
                ThrownReluctantTournaments,
                elapsed);
        }
    }

    public sealed class KinPairFact
    {
        public string Hero1Id { get; set; } = string.Empty;
        public string Hero1Name { get; set; } = string.Empty;
        public int Hero1Calculating { get; set; }
        public int Hero1Honor { get; set; }

        public string Hero2Id { get; set; } = string.Empty;
        public string Hero2Name { get; set; } = string.Empty;
        public int Hero2Calculating { get; set; }
        public int Hero2Honor { get; set; }

        public int Relation { get; set; }

        public bool BothCalculatingPositive => Hero1Calculating > 0 && Hero2Calculating > 0;
        public bool BothHonorNonPositive => Hero1Honor <= 0 && Hero2Honor <= 0;

        public KinPairFact() { }

        public KinPairFact(
            string hero1Id,
            string hero1Name,
            int hero1Calculating,
            string hero2Id,
            string hero2Name,
            int hero2Calculating,
            int relation,
            int hero1Honor = 0,
            int hero2Honor = 0)
        {
            Hero1Id = hero1Id ?? string.Empty;
            Hero1Name = hero1Name ?? string.Empty;
            Hero1Calculating = hero1Calculating;
            Hero1Honor = hero1Honor;
            Hero2Id = hero2Id ?? string.Empty;
            Hero2Name = hero2Name ?? string.Empty;
            Hero2Calculating = hero2Calculating;
            Hero2Honor = hero2Honor;
            Relation = relation;
        }
    }

    public sealed class KinRelationMeasureTally
    {
        public int TotalPairs { get; set; }
        public int CountNegative { get; set; }
        public int CountZero { get; set; }
        public int Count1To10 { get; set; }
        public int Count11To20 { get; set; }
        public int Count21To30 { get; set; }
        public int Count31To50 { get; set; }
        public int CountOver50 { get; set; }
        public int CountOver30BothCalculatingPositive { get; set; }
        public int CountOver30BothHonorNonPositive { get; set; }

        public int CountOver30 => Count31To50 + CountOver50;

        public List<KinPairFact> Top10HighestPairs { get; set; } = new List<KinPairFact>();

        public List<string> FormatReportLines()
        {
            var lines = new List<string>
            {
                string.Format(CultureInfo.InvariantCulture, "kin relation measure: total kin pairs: {0}", TotalPairs),
                string.Format(
                    CultureInfo.InvariantCulture,
                    "kin relation measure: distribution: < 0: {0}, 0: {1}, 1-10: {2}, 11-20: {3}, 21-30: {4}, 31-50: {5}, > 50: {6}",
                    CountNegative,
                    CountZero,
                    Count1To10,
                    Count11To20,
                    Count21To30,
                    Count31To50,
                    CountOver50),
                string.Format(
                    CultureInfo.InvariantCulture,
                    "kin relation measure: relation > 30: {0} pair(s), both calculating > 0: {1} pair(s)",
                    CountOver30,
                    CountOver30BothCalculatingPositive),
                string.Format(
                    CultureInfo.InvariantCulture,
                    "kin relation measure: relation > 30 and both honor <= 0: {0} pair(s)",
                    CountOver30BothHonorNonPositive),
                string.Format(CultureInfo.InvariantCulture, "kin relation measure: top {0} highest relation pair(s):", Top10HighestPairs.Count)
            };

            for (int i = 0; i < Top10HighestPairs.Count; i++)
            {
                var p = Top10HighestPairs[i];
                lines.Add(string.Format(
                    CultureInfo.InvariantCulture,
                    "kin relation measure:   #{0:D2}: {1} ({2}) & {3} ({4}) - relation {5}, calculating: ({6}, {7})",
                    i + 1,
                    p.Hero1Name,
                    p.Hero1Id,
                    p.Hero2Name,
                    p.Hero2Id,
                    p.Relation,
                    p.Hero1Calculating,
                    p.Hero2Calculating));
            }

            return lines;
        }

        public string FormatDialogSummary()
        {
            return string.Format(
                CultureInfo.InvariantCulture,
                "(dev) kin pairs: total {0}, <0: {1}, 0: {2}, 1-10: {3}, 11-20: {4}, 21-30: {5}, 31-50: {6}, >50: {7} | >30: {8} (both calc > 0: {9}) (see log.txt)",
                TotalPairs,
                CountNegative,
                CountZero,
                Count1To10,
                Count11To20,
                Count21To30,
                Count31To50,
                CountOver50,
                CountOver30,
                CountOver30BothCalculatingPositive);
        }
    }

    public static class TournamentAndKinMeasure
    {
        public static TournamentParticipantEvaluation EvaluateParticipant(
            TournamentWinnerFact winner,
            TournamentParticipantFact participant)
        {
            if (winner == null) throw new ArgumentNullException(nameof(winner));
            if (participant == null) throw new ArgumentNullException(nameof(participant));

            var result = new TournamentParticipantEvaluation
            {
                Participant = participant,
                ScoreRatio = winner.Score > 0f ? participant.Score / winner.Score : 0f
            };

            // 1. 分數判定：大於冠軍 × 0.75 才往下（嚴格大於），否則「分數不到冠軍的 0.75 倍」
            if (participant.Score <= winner.Score * 0.75f)
            {
                result.HasChanceToWin = false;
                result.FailureReason = "分數不到冠軍的 0.75 倍 (score <= winner * 0.75)";
                return result;
            }

            result.HasChanceToWin = true;

            // 2. 主從判定：冠軍是他的族長或國王 => 夠格，標「主從」（不看好感、不看個性）
            bool isMaster = participant.IsClanLeaderWinner || participant.IsKingdomLeaderWinner;
            result.QualifiesMasterServant = isMaster;

            // 3. 交情分兩條：
            //    - 自家人：好感 > 30，且參賽者榮譽 <= 0；不看謀略
            //    - 其他人：好感 > 50，且兩人謀略都 > 0
            bool passedRelation = participant.IsKinWithWinner
                ? participant.RelationWithWinner > 30
                : participant.RelationWithWinner > 50;

            result.PassedRelationLine = passedRelation;

            bool bothCalculatingPositive = winner.Calculating > 0 && participant.Calculating > 0;
            bool kinFriendship = participant.IsKinWithWinner && participant.RelationWithWinner > 30 && participant.Honor <= 0;
            bool otherFriendship = !participant.IsKinWithWinner && participant.RelationWithWinner > 50 && bothCalculatingPositive;

            result.QualifiesKinFriendship = kinFriendship;
            result.QualifiesOtherFriendship = otherFriendship;

            // 4. 甘願或不情願判定：榮譽為負，或好感過了上面的線 => 甘願；其餘不情願
            if (result.IsQualified)
            {
                result.IsWilling = participant.Honor < 0 || passedRelation;
            }
            else
            {
                // 都不是 => 寫出沒過哪一條
                var fails = new List<string>();
                if (!isMaster)
                {
                    fails.Add("not clan/kingdom leader");
                }

                if (participant.IsKinWithWinner)
                {
                    if (participant.RelationWithWinner <= 30)
                    {
                        fails.Add(string.Format(CultureInfo.InvariantCulture, "kin relation {0} <= 30", participant.RelationWithWinner));
                    }
                    if (participant.Honor > 0)
                    {
                        fails.Add(string.Format(CultureInfo.InvariantCulture, "kin participant honor {0} > 0", participant.Honor));
                    }
                }
                else
                {
                    if (participant.RelationWithWinner <= 50)
                    {
                        fails.Add(string.Format(CultureInfo.InvariantCulture, "non-kin relation {0} <= 50", participant.RelationWithWinner));
                    }
                    if (!bothCalculatingPositive)
                    {
                        fails.Add(string.Format(
                            CultureInfo.InvariantCulture,
                            "calculating not both positive (winner={0}, participant={1})",
                            winner.Calculating,
                            participant.Calculating));
                    }
                }

                result.FailureReason = string.Join("; ", fails);
            }

            return result;
        }

        public static KinRelationMeasureTally ComputeKinTally(IReadOnlyList<KinPairFact>? pairs)
        {
            var list = pairs ?? Array.Empty<KinPairFact>();
            var tally = new KinRelationMeasureTally
            {
                TotalPairs = list.Count
            };

            int countNeg = 0;
            int countZero = 0;
            int count1To10 = 0;
            int count11To20 = 0;
            int count21To30 = 0;
            int count31To50 = 0;
            int countOver50 = 0;
            int countOver30BothCalc = 0;
            int countOver30BothHonorNonPos = 0;

            foreach (var p in list)
            {
                int r = p.Relation;
                if (r < 0)
                {
                    countNeg++;
                }
                else if (r == 0)
                {
                    countZero++;
                }
                else if (r <= 10)
                {
                    count1To10++;
                }
                else if (r <= 20)
                {
                    count11To20++;
                }
                else if (r <= 30)
                {
                    count21To30++;
                }
                else if (r <= 50)
                {
                    count31To50++;
                }
                else
                {
                    countOver50++;
                }

                if (r > 30)
                {
                    if (p.BothCalculatingPositive)
                    {
                        countOver30BothCalc++;
                    }
                    if (p.BothHonorNonPositive)
                    {
                        countOver30BothHonorNonPos++;
                    }
                }
            }

            tally.CountNegative = countNeg;
            tally.CountZero = countZero;
            tally.Count1To10 = count1To10;
            tally.Count11To20 = count11To20;
            tally.Count21To30 = count21To30;
            tally.Count31To50 = count31To50;
            tally.CountOver50 = countOver50;
            tally.CountOver30BothCalculatingPositive = countOver30BothCalc;
            tally.CountOver30BothHonorNonPositive = countOver30BothHonorNonPos;

            // 好感最高的 10 對（名字、id、好感、兩人的謀略），先比好感降序，再比 id 保證穩定排序
            tally.Top10HighestPairs = list
                .OrderByDescending(p => p.Relation)
                .ThenBy(p => p.Hero1Id, StringComparer.Ordinal)
                .ThenBy(p => p.Hero2Id, StringComparer.Ordinal)
                .Take(10)
                .ToList();

            return tally;
        }
    }
}
