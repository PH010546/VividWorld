#nullable enable
using System;
using System.Collections.Generic;

namespace VividWorld.Core.Rumors
{
    public sealed class BanditCaptureFamilyExclusion
    {
        public string HeroId { get; }
        public string Reason { get; }

        public BanditCaptureFamilyExclusion(string heroId, string reason)
        {
            HeroId = heroId ?? string.Empty;
            Reason = reason ?? string.Empty;
        }
    }

    public sealed class BanditCaptureFamilyResult
    {
        public IReadOnlyList<string> SelectedHeroIds { get; }
        public IReadOnlyList<BanditCaptureFamilyExclusion> Exclusions { get; }

        public BanditCaptureFamilyResult(IReadOnlyList<string> selectedHeroIds, IReadOnlyList<BanditCaptureFamilyExclusion> exclusions)
        {
            SelectedHeroIds = selectedHeroIds ?? Array.Empty<string>();
            Exclusions = exclusions ?? Array.Empty<BanditCaptureFamilyExclusion>();
        }
    }

    public static class BanditCaptureFamilySelector
    {
        public static BanditCaptureFamilyResult Select(
            string prisonerHeroId,
            IEnumerable<KeyValuePair<string, TraitProfile?>>? candidates,
            string? playerHeroId,
            int maxCount)
        {
            if (candidates == null)
            {
                return new BanditCaptureFamilyResult(Array.Empty<string>(), Array.Empty<BanditCaptureFamilyExclusion>());
            }

            var seen = new HashSet<string>(StringComparer.Ordinal);
            var eligibleHeroIds = new List<string>();
            var exclusions = new List<BanditCaptureFamilyExclusion>();

            foreach (var kvp in candidates)
            {
                string heroId = kvp.Key;
                if (string.IsNullOrEmpty(heroId)) continue;
                if (!seen.Add(heroId)) continue;

                var profile = kvp.Value;

                if (string.Equals(heroId, prisonerHeroId, StringComparison.Ordinal))
                {
                    exclusions.Add(new BanditCaptureFamilyExclusion(heroId, "self"));
                }
                else if (!string.IsNullOrEmpty(playerHeroId) && string.Equals(heroId, playerHeroId, StringComparison.Ordinal))
                {
                    exclusions.Add(new BanditCaptureFamilyExclusion(heroId, "player"));
                }
                else if (Eligibility.IsEligible(profile))
                {
                    eligibleHeroIds.Add(heroId);
                }
                else
                {
                    // 要不要收只看 Eligibility（傳播網資格的唯一定義）；這裡只是替日誌說出是哪一項不合格。
                    string reason = profile == null ? "not in network"
                        : !profile.IsAlive ? "dead"
                        : profile.IsPrisoner ? "prisoner"
                        : "not in network";
                    exclusions.Add(new BanditCaptureFamilyExclusion(heroId, reason));
                }
            }

            eligibleHeroIds.Sort(StringComparer.Ordinal);

            var selected = new List<string>();
            int limit = Math.Max(0, maxCount);

            for (int i = 0; i < eligibleHeroIds.Count; i++)
            {
                string id = eligibleHeroIds[i];
                if (i < limit)
                {
                    selected.Add(id);
                }
                else
                {
                    exclusions.Add(new BanditCaptureFamilyExclusion(id, "over cap"));
                }
            }

            return new BanditCaptureFamilyResult(selected, exclusions);
        }
    }
}
