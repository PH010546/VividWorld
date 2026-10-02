#nullable enable
using System.Collections.Generic;
using System.Linq;
using VividWorld.Core.Catalog;

namespace VividWorld.Core.Presentation
{
    public sealed class SentenceCombination
    {
        public string CombinationKey { get; }
        public IReadOnlyList<TemplateFact> Facts { get; }
        public bool IsMissingOptional { get; }
        /// <summary>第一次出現這組碎片的手數。</summary>
        public int Hop { get; }
        /// <summary>所有會剩下這組碎片的手數（例：第 2、3 手同一組）。</summary>
        public IReadOnlyList<int> Hops { get; }

        public SentenceCombination(string combinationKey, IReadOnlyList<TemplateFact> facts, bool isMissingOptional, int hop, IReadOnlyList<int>? hops = null)
        {
            CombinationKey = combinationKey;
            Facts = facts;
            IsMissingOptional = isMissingOptional;
            Hop = hop;
            Hops = hops != null && hops.Count > 0 ? hops : new[] { hop };
        }

        public bool OccursAtHop0 => Hops.Contains(0);
        public bool OccursAtHop1 => Hops.Contains(1);
        public bool OccursAtHop2OrLater => Hops.Any(h => h >= 2);
        public bool OccursAfterHop0 => Hops.Any(h => h >= 1);
    }
}
