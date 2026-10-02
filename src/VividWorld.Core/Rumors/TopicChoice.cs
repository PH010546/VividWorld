using System;
using System.Collections.Generic;

namespace VividWorld.Core.Rumors
{
    public sealed class TopicCandidate
    {
        public string EventId { get; }
        /// <summary>段（1..5）：選題的權重查表用的就是它。</summary>
        public int Drama { get; }

        /// <summary>份量（1..10），只供日誌；沒給時當成段 × 2。</summary>
        public int DramaWeight { get; }
        public double Tell { get; }
        public double Freshness { get; }
        public double Weight { get; }

        public TopicCandidate(string eventId, int drama, double tell, double freshness, double weight, int dramaWeight = 0)
        {
            EventId = eventId ?? string.Empty;
            Drama = drama;
            DramaWeight = dramaWeight > 0 ? dramaWeight : drama * 2;
            Tell = tell;
            Freshness = freshness;
            Weight = weight;
        }
    }

    public sealed class TopicExclusion
    {
        public string EventId { get; }
        public TellReason Reason { get; }
        public string? Detail { get; }

        public TopicExclusion(string eventId, TellReason reason, string? detail = null)
        {
            EventId = eventId ?? string.Empty;
            Reason = reason;
            Detail = detail;
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
