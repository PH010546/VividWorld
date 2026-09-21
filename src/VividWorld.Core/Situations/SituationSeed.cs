using System;
using System.Collections.Generic;
using System.Linq;
using VividWorld.Core.Util;

namespace VividWorld.Core.Situations
{
    public static class SituationSeed
    {
        public static long ComputeInstanceId(
            long campaignSeed,
            string templateId,
            double day,
            IReadOnlyDictionary<string, string?> roles)
        {
            int dayBucket = RumorSeed.DayBucket(day);

            var sortedSegments = (roles ?? new Dictionary<string, string?>())
                .OrderBy(kv => kv.Key, StringComparer.Ordinal)
                .Select(kv => $"{kv.Key}={kv.Value ?? string.Empty}")
                .ToArray();

            var parts = new object[2 + sortedSegments.Length];
            parts[0] = templateId;
            parts[1] = dayBucket;
            for (int i = 0; i < sortedSegments.Length; i++)
            {
                parts[2 + i] = sortedSegments[i];
            }

            return RumorSeed.Of(campaignSeed, parts);
        }

        public static long ComputeBranchSeed(
            long campaignSeed,
            long situationInstanceId,
            string deciderHeroId)
        {
            return RumorSeed.Of(campaignSeed, situationInstanceId, "branch", deciderHeroId);
        }

        public static long InstanceId(
            long campaignSeed,
            string templateId,
            double day,
            IReadOnlyDictionary<string, string?> roles)
            => ComputeInstanceId(campaignSeed, templateId, day, roles);

        public static long BranchSeed(
            long campaignSeed,
            long situationInstanceId,
            string deciderHeroId)
            => ComputeBranchSeed(campaignSeed, situationInstanceId, deciderHeroId);
    }
}
