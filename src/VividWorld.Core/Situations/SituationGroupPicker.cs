#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using VividWorld.Core.Util;

namespace VividWorld.Core.Situations
{
    public sealed class SituationGroupPickResult
    {
        public string? SelectedSituationId { get; set; }
        public bool Repicked { get; set; }
        public string Log { get; set; } = string.Empty;
    }

    public static class SituationGroupPicker
    {
        public static SituationGroupPickResult Pick(
            IReadOnlyList<(string SituationId, double Share)> candidateSituations,
            IReadOnlyCollection<string>? failedSituationIds,
            IDeterministicRng rng,
            long seed)
        {
            if (candidateSituations == null || candidateSituations.Count == 0)
            {
                return new SituationGroupPickResult
                {
                    SelectedSituationId = null,
                    Repicked = false,
                    Log = "empty candidates list"
                };
            }

            HashSet<string>? failedSet = failedSituationIds != null ? new HashSet<string>(failedSituationIds, StringComparer.OrdinalIgnoreCase) : null;

            var valid = candidateSituations
                .Where(s => s.Share > 0.0 && (failedSet == null || !failedSet.Contains(s.SituationId)))
                .ToList();

            if (valid.Count == 0)
            {
                return new SituationGroupPickResult
                {
                    SelectedSituationId = null,
                    Repicked = failedSet != null && failedSet.Count > 0,
                    Log = "no eligible situations remaining in quota group"
                };
            }

            var weights = valid.Select(s => s.Share).ToList();
            int pickedIdx = rng.PickWeighted(weights, seed);
            var chosen = valid[pickedIdx];

            bool repicked = failedSet != null && failedSet.Count > 0;
            string log = repicked
                ? $"repicked {chosen.SituationId} (share {chosen.Share:0.00}) after previous failed"
                : $"picked {chosen.SituationId} (share {chosen.Share:0.00})";

            return new SituationGroupPickResult
            {
                SelectedSituationId = chosen.SituationId,
                Repicked = repicked,
                Log = log
            };
        }
    }
}
