#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using VividWorld.Core.Config;
using VividWorld.Core.Rumors;
using VividWorld.Core.Util;

namespace VividWorld.Core.Secrets
{
    public sealed class TournamentThrowCandidateEvaluation
    {
        public TournamentParticipantFact Participant { get; set; } = new();
        public bool HasChanceToWin { get; set; }
        public bool QualifiesMasterServant { get; set; }
        public bool QualifiesKinFriendship { get; set; }
        public bool QualifiesOtherFriendship { get; set; }
        public bool QualifiesFriendship => QualifiesKinFriendship || QualifiesOtherFriendship;
        public bool IsQualified => QualifiesMasterServant || QualifiesKinFriendship || QualifiesOtherFriendship;
        public string DisqualifiedReason { get; set; } = string.Empty;
        public bool IsWilling { get; set; }

        public string SecretPath => QualifiesMasterServant ? "liege" : "friendship";
    }

    public sealed class TournamentThrowResult
    {
        public bool IsThrown { get; set; }
        public string StopReason { get; set; } = string.Empty;
        public TournamentThrowCandidateEvaluation? ChosenCandidate { get; set; }
        public string? ThrowerHeroId => ChosenCandidate?.Participant.HeroId;
        public string? SecretPath => ChosenCandidate?.SecretPath;
        public bool IsWilling => ChosenCandidate?.IsWilling ?? true;
        public double ReluctantGrudge { get; set; }
        public List<string> HarmedHeroIds { get; set; } = new();
        public List<TournamentThrowCandidateEvaluation> Evaluations { get; set; } = new();
        public double ChanceRoll { get; set; }
        public double ChanceThreshold { get; set; }
        public string LogMessage { get; set; } = string.Empty;
    }

    public static class TournamentThrowEvaluation
    {
        public static TournamentThrowResult Evaluate(
            TournamentWinnerFact winner,
            IReadOnlyList<TournamentParticipantFact>? participants,
            VividWorldConfig? config,
            long campaignSeed,
            string townId,
            double day,
            IDeterministicRng? rng = null)
        {
            rng ??= new SplitMix64Rng();
            var result = new TournamentThrowResult();
            var cfgSecrets = config?.Secrets?.TournamentThrow ?? new SecretTournamentThrowConfig();
            result.ReluctantGrudge = cfgSecrets.ReluctantGrudge;
            result.ChanceThreshold = cfgSecrets.Chance;

            if (winner == null)
            {
                result.StopReason = "winner is null";
                result.LogMessage = "tournament throw: stopped at step 1: winner fact is null";
                return result;
            }

            // 1. 冠軍榮譽為正 => 不開口
            if (winner.Honor > 0)
            {
                result.StopReason = "winner honor positive, will not ask (冠軍榮譽為正，不會開口)";
                result.LogMessage = string.Format(
                    CultureInfo.InvariantCulture,
                    "tournament throw: stopped at step 1: winner {0} ({1}) honor {2} > 0, will not ask",
                    winner.Name,
                    winner.HeroId,
                    winner.Honor);
                return result;
            }

            // 2. 候選 = 原版分數大於冠軍原版分數 × 0.75 的英雄參賽者（那天有機會贏；玩家不算），而且符合主從或交情；兩條都符合算主從
            var qualified = new List<TournamentThrowCandidateEvaluation>();
            float scoreThreshold = winner.Score * 0.75f;

            if (participants != null)
            {
                foreach (var p in participants)
                {
                    if (p == null) continue;
                    if (string.Equals(p.HeroId, winner.HeroId, StringComparison.Ordinal)) continue;

                    var eval = new TournamentThrowCandidateEvaluation
                    {
                        Participant = p
                    };

                    // 分數大於冠軍原版分數 × 0.75（嚴格大於；剛好等於冠軍分數 × 0.75 不算）
                    if (p.Score <= scoreThreshold)
                    {
                        eval.HasChanceToWin = false;
                        eval.DisqualifiedReason = string.Format(
                            CultureInfo.InvariantCulture,
                            "score {0:F1} <= threshold {1:F1} (0.75x)",
                            p.Score,
                            scoreThreshold);
                        result.Evaluations.Add(eval);
                        continue;
                    }
                    eval.HasChanceToWin = true;

                    // 主從：冠軍是他的族長或國王
                    bool isMasterServant = p.IsClanLeaderWinner || p.IsKingdomLeaderWinner;
                    eval.QualifiesMasterServant = isMasterServant;

                    // 交情：自家人好感 > 30 且讓的人榮譽不為正；其他人好感 > 50 且兩人謀略都為正
                    bool passedRelationLine = p.IsKinWithWinner
                        ? p.RelationWithWinner > 30
                        : p.RelationWithWinner > 50;

                    bool isKinFriendship = p.IsKinWithWinner && p.RelationWithWinner > 30 && p.Honor <= 0;
                    bool isOtherFriendship = !p.IsKinWithWinner && p.RelationWithWinner > 50 && winner.Calculating > 0 && p.Calculating > 0;

                    eval.QualifiesKinFriendship = isKinFriendship;
                    eval.QualifiesOtherFriendship = isOtherFriendship;

                    if (eval.IsQualified)
                    {
                        // 甘願（讓的人榮譽為負，或好感過了交情那條線）=> 不記恩怨；否則不情願 => 他對冠軍記負向好感與恩怨
                        eval.IsWilling = p.Honor < 0 || passedRelationLine;
                        qualified.Add(eval);
                    }
                    else
                    {
                        var reasons = new List<string>();
                        if (!isMasterServant) reasons.Add("not master-servant");
                        if (p.IsKinWithWinner)
                        {
                            if (p.RelationWithWinner <= 30) reasons.Add(string.Format(CultureInfo.InvariantCulture, "kin relation {0} <= 30", p.RelationWithWinner));
                            if (p.Honor > 0) reasons.Add(string.Format(CultureInfo.InvariantCulture, "kin honor {0} > 0", p.Honor));
                        }
                        else
                        {
                            if (p.RelationWithWinner <= 50) reasons.Add(string.Format(CultureInfo.InvariantCulture, "other relation {0} <= 50", p.RelationWithWinner));
                            if (winner.Calculating <= 0 || p.Calculating <= 0) reasons.Add(string.Format(CultureInfo.InvariantCulture, "calculating not both positive (winner={0}, participant={1})", winner.Calculating, p.Calculating));
                        }
                        eval.DisqualifiedReason = string.Join("; ", reasons);
                    }

                    result.Evaluations.Add(eval);
                }
            }

            if (qualified.Count == 0)
            {
                result.StopReason = "no qualified candidate (無符合條件候選者)";
                result.LogMessage = string.Format(
                    CultureInfo.InvariantCulture,
                    "tournament throw: stopped at step 2: no qualified candidate among {0} participant(s)",
                    result.Evaluations.Count);
                return result;
            }

            // 3. 有好幾位時用固定種子挑一位
            TournamentThrowCandidateEvaluation chosen;
            if (qualified.Count == 1)
            {
                chosen = qualified[0];
            }
            else
            {
                var sorted = qualified.OrderBy(c => c.Participant.HeroId, StringComparer.Ordinal).ToList();
                long pickSeed = RumorSeed.Of(campaignSeed, "tournament.throw.pick", townId ?? string.Empty, RumorSeed.DayBucket(day), winner.HeroId);
                int pickIdx = rng.Pick(sorted.Count, pickSeed);
                chosen = sorted[pickIdx];
            }
            result.ChosenCandidate = chosen;

            // 4. 擲 secrets.tournamentThrow.chance（固定種子）
            long chanceSeed = RumorSeed.Of(campaignSeed, "tournament.throw.chance", townId ?? string.Empty, RumorSeed.DayBucket(day), winner.HeroId);
            double roll = rng.NextDouble(chanceSeed);
            result.ChanceRoll = roll;

            bool hit = result.ChanceThreshold >= 1.0 || roll < result.ChanceThreshold;
            if (!hit)
            {
                result.StopReason = string.Format(
                    CultureInfo.InvariantCulture,
                    "chance missed: roll {0:F3} >= threshold {1:F2} (機率未中)",
                    roll,
                    result.ChanceThreshold);
                result.LogMessage = string.Format(
                    CultureInfo.InvariantCulture,
                    "tournament throw: stopped at step 4: chance missed (candidate={0}, roll={1:F3}, threshold={2:F2})",
                    chosen.Participant.HeroId,
                    roll,
                    result.ChanceThreshold);
                return result;
            }

            result.IsThrown = true;
            result.StopReason = string.Empty;

            // SecretHarmedHeroIds 填同場其他英雄參賽者（冠軍與讓的人以外，玩家不算）
            if (participants != null)
            {
                foreach (var p in participants)
                {
                    if (p == null || string.IsNullOrEmpty(p.HeroId)) continue;
                    if (string.Equals(p.HeroId, winner.HeroId, StringComparison.Ordinal)) continue;
                    if (string.Equals(p.HeroId, chosen.Participant.HeroId, StringComparison.Ordinal)) continue;
                    result.HarmedHeroIds.Add(p.HeroId);
                }
            }

            string willingStr = result.IsWilling
                ? "willing (甘願)"
                : string.Format(CultureInfo.InvariantCulture, "reluctant (不情願, grudge {0:F1})", result.ReluctantGrudge);

            result.LogMessage = string.Format(
                CultureInfo.InvariantCulture,
                "tournament throw: hit! chosen {0} ({1}), path {2}, {3}, roll={4:F3} < threshold={5:F2}",
                chosen.Participant.Name,
                chosen.Participant.HeroId,
                result.SecretPath,
                willingStr,
                roll,
                result.ChanceThreshold);

            return result;
        }
    }
}
