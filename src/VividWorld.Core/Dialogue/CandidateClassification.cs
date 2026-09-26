#nullable enable
using System.Collections.Generic;

namespace VividWorld.Core.Dialogue
{
    public sealed class CandidateClassification
    {
        public List<RumorCandidate> Eligible { get; } = new List<RumorCandidate>();
        public int FilteredNotVisible { get; set; }
        public int FilteredFutureTimeline { get; set; }
        public int FilteredPlayerKnows { get; set; }
        public int FilteredOther { get; set; }
        public List<string> FilterNotes { get; } = new List<string>();

        public int EligibleCount => Eligible.Count;
        public int FilteredTotal => FilteredNotVisible + FilteredFutureTimeline + FilteredPlayerKnows + FilteredOther;
    }
}
