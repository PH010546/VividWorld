using System;
using System.Collections.Generic;
using System.Linq;
using VividWorld.Core.Memory;
using VividWorld.Core.Persistence;

namespace VividWorld.Core.Ai
{
    public enum AiPushExclusionReason
    {
        AlreadyPushedSameVersion,
        ExceedsLimit
    }

    public sealed class AiPushExclusion
    {
        public string EventId { get; }
        public AiPushExclusionReason Reason { get; }
        public string Detail { get; }

        public AiPushExclusion(string eventId, AiPushExclusionReason reason, string detail)
        {
            EventId = eventId;
            Reason = reason;
            Detail = detail;
        }
    }

    public sealed class AiPushCandidate
    {
        public NpcRecalledMemory Memory { get; }
        public string Version { get; }

        public AiPushCandidate(NpcRecalledMemory memory, string version)
        {
            Memory = memory;
            Version = version;
        }
    }

    public sealed class AiPushPlan
    {
        public IReadOnlyList<AiPushCandidate> ToPush { get; }
        public IReadOnlyList<AiPushExclusion> Exclusions { get; }

        public AiPushPlan(IReadOnlyList<AiPushCandidate> toPush, IReadOnlyList<AiPushExclusion> exclusions)
        {
            ToPush = toPush;
            Exclusions = exclusions;
        }
    }

    public static class AiPushPlanner
    {
        public static AiPushPlan Plan(
            string targetId,
            string heroId,
            IReadOnlyList<NpcRecalledMemory> recalledMemories,
            AiPushedStore? pushStore,
            bool isPersistent,
            int maxPerChat)
        {
            var toPush = new List<AiPushCandidate>();
            var exclusions = new List<AiPushExclusion>();

            if (recalledMemories == null || recalledMemories.Count == 0)
            {
                return new AiPushPlan(toPush, exclusions);
            }

            foreach (var memory in recalledMemories)
            {
                string version = AiPushVersion.Compute(memory.Facts.Select(f => f.Id), memory.IsCorrection);

                if (isPersistent && pushStore != null && pushStore.HasPushedVersion(targetId, heroId, memory.EventId, version))
                {
                    exclusions.Add(new AiPushExclusion(memory.EventId, AiPushExclusionReason.AlreadyPushedSameVersion, version));
                    continue;
                }

                if (toPush.Count >= maxPerChat)
                {
                    exclusions.Add(new AiPushExclusion(memory.EventId, AiPushExclusionReason.ExceedsLimit, $"limit is {maxPerChat}"));
                    continue;
                }

                toPush.Add(new AiPushCandidate(memory, version));
            }

            return new AiPushPlan(toPush, exclusions);
        }
    }
}
