#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using VividWorld.Core.Rumors;
using VividWorld.Core.Util;

namespace VividWorld.Core.Secrets
{
    public sealed class TournamentCandidateInput
    {
        public string HeroId { get; }
        public string Name { get; }
        public float BaseScore { get; }

        public TournamentCandidateInput(string heroId, string name, float baseScore)
        {
            HeroId = heroId ?? string.Empty;
            Name = name ?? string.Empty;
            BaseScore = baseScore;
        }
    }

    public sealed class TournamentRematchCandidate
    {
        public string HeroId { get; }
        public string Name { get; }
        public float BaseScore { get; }
        public double RandomRoll { get; }
        public float FinalScore { get; }

        public TournamentRematchCandidate(string heroId, string name, float baseScore, double randomRoll, float finalScore)
        {
            HeroId = heroId ?? string.Empty;
            Name = name ?? string.Empty;
            BaseScore = baseScore;
            RandomRoll = randomRoll;
            FinalScore = finalScore;
        }
    }

    public sealed class TournamentFinalRematchResult
    {
        public TournamentRematchCandidate? SelectedOpponent { get; }
        public IReadOnlyList<TournamentRematchCandidate> Candidates { get; }
        public bool WasDesignated { get; }

        public TournamentFinalRematchResult(
            TournamentRematchCandidate? selectedOpponent,
            IReadOnlyList<TournamentRematchCandidate> candidates,
            bool wasDesignated)
        {
            SelectedOpponent = selectedOpponent;
            Candidates = candidates ?? Array.Empty<TournamentRematchCandidate>();
            WasDesignated = wasDesignated;
        }
    }

    /// <summary>
    /// 比武大會決賽對手模擬再比機制。
    /// 冠軍以外的英雄參賽者照原版模擬比法（GetTournamentSimulationScore * (0.75 + 0.25 * roll)）再比一次，
    /// 最高分者成為決賽對手。若無其他英雄參賽者則無決賽對手。
    /// 亦支援外部指定對手介面。
    /// </summary>
    public static class TournamentFinalRematch
    {
        public static TournamentFinalRematchResult DetermineFinalOpponent(
            IEnumerable<TournamentCandidateInput>? candidateInputs,
            string championHeroId,
            long campaignSeed,
            string townId,
            double day,
            string? designatedOpponentHeroId = null,
            IDeterministicRng? rng = null)
        {
            rng ??= new SplitMix64Rng();

            var candidates = new List<TournamentRematchCandidate>();
            if (candidateInputs != null)
            {
                foreach (var input in candidateInputs)
                {
                    if (input == null) continue;
                    if (string.IsNullOrEmpty(input.HeroId)) continue;
                    if (string.Equals(input.HeroId, championHeroId, StringComparison.Ordinal)) continue;

                    long seed = RumorSeed.Of(campaignSeed, "tournament.final", townId ?? string.Empty, RumorSeed.DayBucket(day), input.HeroId);
                    double roll = rng.NextDouble(seed);
                    float finalScore = input.BaseScore * (0.75f + 0.25f * (float)roll);

                    candidates.Add(new TournamentRematchCandidate(input.HeroId, input.Name, input.BaseScore, roll, finalScore));
                }
            }

            if (candidates.Count == 0)
            {
                return new TournamentFinalRematchResult(null, candidates, false);
            }

            if (!string.IsNullOrEmpty(designatedOpponentHeroId))
            {
                var designated = candidates.FirstOrDefault(c => string.Equals(c.HeroId, designatedOpponentHeroId, StringComparison.Ordinal));
                if (designated != null)
                {
                    return new TournamentFinalRematchResult(designated, candidates, true);
                }
            }

            TournamentRematchCandidate selected = candidates[0];
            for (int i = 1; i < candidates.Count; i++)
            {
                var current = candidates[i];
                if (current.FinalScore > selected.FinalScore)
                {
                    selected = current;
                }
                else if (Math.Abs(current.FinalScore - selected.FinalScore) < 0.0001f)
                {
                    if (string.Compare(current.HeroId, selected.HeroId, StringComparison.Ordinal) < 0)
                    {
                        selected = current;
                    }
                }
            }

            return new TournamentFinalRematchResult(selected, candidates, false);
        }
    }
}
