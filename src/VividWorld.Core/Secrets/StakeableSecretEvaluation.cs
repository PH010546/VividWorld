#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using VividWorld.Core.Events;

namespace VividWorld.Core.Secrets
{
    public sealed class StakeableSecretCandidate
    {
        public WorldEvent Event { get; set; } = null!;
        public bool IsSecret { get; set; }
        public bool HasNewShapeOwners { get; set; }
        public bool NotOwnSecret { get; set; }
        public bool NotLeaked { get; set; }
        public bool NotRetired { get; set; }
        public bool OpponentNotOwner { get; set; }
        public bool OpponentDoesNotKnow { get; set; }
        public bool KnowerKnows { get; set; }
        public bool IsEligible => IsSecret && HasNewShapeOwners && NotOwnSecret && NotLeaked && NotRetired && OpponentNotOwner && OpponentDoesNotKnow && KnowerKnows;
        public string DisqualifiedReason { get; set; } = string.Empty;
    }

    public sealed class StakeableSecretResult
    {
        public bool IsEligible { get; set; }
        public string Reason { get; set; } = string.Empty;
        public WorldEvent? ChosenSecret { get; set; }
        public List<StakeableSecretCandidate> Candidates { get; set; } = new();
    }

    public sealed class HuntWagerContestResult
    {
        public string BettorHeroId { get; set; } = string.Empty;
        public string OpponentHeroId { get; set; } = string.Empty;
        public int BettorBow { get; set; }
        public int BettorRiding { get; set; }
        public double BettorRoll { get; set; }
        public double BettorScore { get; set; }
        public int OpponentBow { get; set; }
        public int OpponentRiding { get; set; }
        public double OpponentRoll { get; set; }
        public double OpponentScore { get; set; }
        public bool BettorWon { get; set; }
        public string WinnerHeroId => BettorWon ? BettorHeroId : OpponentHeroId;
        public string LoserHeroId => BettorWon ? OpponentHeroId : BettorHeroId;
    }

    public static class StakeableSecretEvaluation
    {
        public static StakeableSecretResult Evaluate(
            string bettorHeroId,
            string opponentHeroId,
            string? playerHeroId,
            IEnumerable<WorldEvent>? secrets)
        {
            var result = new StakeableSecretResult();

            if (string.IsNullOrEmpty(bettorHeroId) || string.IsNullOrEmpty(opponentHeroId))
            {
                result.Reason = "bettor or opponent is empty";
                return result;
            }

            if (!string.IsNullOrEmpty(playerHeroId) && string.Equals(bettorHeroId, playerHeroId, StringComparison.Ordinal))
            {
                result.Reason = "bettor is player (player does not stake secrets)";
                return result;
            }

            if (secrets != null)
            {
                foreach (var evt in secrets)
                {
                    if (evt == null) continue;
                    var cand = EvaluateCandidate(evt, bettorHeroId, opponentHeroId);
                    result.Candidates.Add(cand);
                }
            }

            var qualified = result.Candidates.Where(c => c.IsEligible).ToList();
            if (qualified.Count == 0)
            {
                result.Reason = result.Candidates.Count == 0
                    ? "no known secrets"
                    : $"none of {result.Candidates.Count} secret(s) qualified";
                return result;
            }

            // Pick highest weight (DramaWeightTen), tie-break earliest Day, then EventId
            var sorted = qualified
                .OrderByDescending(c => c.Event.DramaWeightTen)
                .ThenBy(c => c.Event.Day)
                .ThenBy(c => c.Event.EventId, StringComparer.Ordinal)
                .ToList();

            result.ChosenSecret = sorted[0].Event;
            result.IsEligible = true;
            result.Reason = "eligible";
            return result;
        }

        public static StakeableSecretCandidate EvaluateCandidate(
            WorldEvent evt,
            string bettorHeroId,
            string opponentHeroId)
        {
            var cand = new StakeableSecretCandidate
            {
                Event = evt,
                IsSecret = evt.Origin == EventOrigin.Secret,
                HasNewShapeOwners = evt.SecretOwnerHeroIds != null && evt.SecretOwnerHeroIds.Count > 0,
                NotOwnSecret = evt.SecretOwnerHeroIds == null || !evt.SecretOwnerHeroIds.Contains(bettorHeroId, StringComparer.Ordinal),
                NotLeaked = evt.State == null || !evt.State.Leaked,
                NotRetired = !evt.SecretRetired,
                OpponentNotOwner = evt.SecretOwnerHeroIds == null || !evt.SecretOwnerHeroIds.Contains(opponentHeroId, StringComparer.Ordinal),
                OpponentDoesNotKnow = !evt.IsKnownBy(opponentHeroId),
                KnowerKnows = evt.IsKnownBy(bettorHeroId)
            };

            var reasons = new List<string>();
            if (!cand.IsSecret) reasons.Add("origin is not secret");
            if (!cand.HasNewShapeOwners) reasons.Add("missing secret owners (old shape or null)");
            if (!cand.NotOwnSecret) reasons.Add("bettor is owner");
            if (!cand.NotLeaked) reasons.Add("already leaked");
            if (!cand.NotRetired) reasons.Add("secret retired");
            if (!cand.OpponentNotOwner) reasons.Add("opponent is owner");
            if (!cand.OpponentDoesNotKnow) reasons.Add("opponent already knows");
            if (!cand.KnowerKnows) reasons.Add("bettor does not know");

            cand.DisqualifiedReason = reasons.Count > 0 ? string.Join(", ", reasons) : string.Empty;
            return cand;
        }

        public static HuntWagerContestResult CalculateContest(
            string bettorHeroId,
            int bettorBow,
            int bettorRiding,
            double bettorRoll,
            string opponentHeroId,
            int opponentBow,
            int opponentRiding,
            double opponentRoll)
        {
            double bettorScore = (bettorBow + bettorRiding) * (0.75 + 0.25 * bettorRoll);
            double opponentScore = (opponentBow + opponentRiding) * (0.75 + 0.25 * opponentRoll);
            bool bettorWon = bettorScore > opponentScore;

            return new HuntWagerContestResult
            {
                BettorHeroId = bettorHeroId,
                BettorBow = bettorBow,
                BettorRiding = bettorRiding,
                BettorRoll = bettorRoll,
                BettorScore = bettorScore,
                OpponentHeroId = opponentHeroId,
                OpponentBow = opponentBow,
                OpponentRiding = opponentRiding,
                OpponentRoll = opponentRoll,
                OpponentScore = opponentScore,
                BettorWon = bettorWon
            };
        }

        public static bool ApplyContestConsequence(
            WorldEvent stakedSecret,
            string bettorHeroId,
            string opponentHeroId,
            bool bettorWon,
            double day,
            out KnownByEntry? addedEntry)
        {
            addedEntry = null;
            if (stakedSecret == null || string.IsNullOrEmpty(opponentHeroId)) return false;

            if (bettorWon)
            {
                return false;
            }

            if (stakedSecret.IsKnownBy(opponentHeroId))
            {
                return false;
            }

            var bettorEntry = stakedSecret.EntryFor(bettorHeroId);
            int bettorHop = bettorEntry?.Hop ?? 0;
            int opponentHop = bettorHop + 1;

            addedEntry = new KnownByEntry
            {
                HeroId = opponentHeroId,
                Hop = opponentHop,
                SourceHeroId = bettorHeroId,
                LearnedDay = day
            };
            stakedSecret.KnownBy.Add(addedEntry);
            return true;
        }
    }
}
