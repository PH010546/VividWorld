using System;
using System.Collections.Generic;

namespace VividWorld.Core.Rumors
{
    public sealed class TopicCandidate
    {
        public string EventId { get; }
        public int Drama { get; }
        public double Tell { get; }
        public double Freshness { get; }
        public double Weight { get; }

        public TopicCandidate(string eventId, int drama, double tell, double freshness, double weight)
        {
            EventId = eventId ?? string.Empty;
            Drama = drama;
            Tell = tell;
            Freshness = freshness;
            Weight = weight;
        }
    }

    public sealed class TopicExclusion
    {
        public string EventId { get; }
        public TellReason Reason { get; }

        public TopicExclusion(string eventId, TellReason reason)
        {
            EventId = eventId ?? string.Empty;
            Reason = reason;
        }
    }

    public sealed class TopicChoice
    {
        public string TellerHeroId { get; }
        public IReadOnlyList<TopicCandidate> Candidates { get; }
        public IReadOnlyList<TopicExclusion> Exclusions { get; }
        public int PickedIndex { get; }
        public string? PickedEventId { get; }
        public double Chance { get; }

        public TopicChoice(
            string tellerHeroId,
            IReadOnlyList<TopicCandidate> candidates,
            IReadOnlyList<TopicExclusion> exclusions,
            int pickedIndex,
            string? pickedEventId,
            double chance)
        {
            TellerHeroId = tellerHeroId ?? string.Empty;
            Candidates = candidates ?? Array.Empty<TopicCandidate>();
            Exclusions = exclusions ?? Array.Empty<TopicExclusion>();
            PickedIndex = pickedIndex;
            PickedEventId = pickedEventId;
            Chance = chance;
        }
    }
}
