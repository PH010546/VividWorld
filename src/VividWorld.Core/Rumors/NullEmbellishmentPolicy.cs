using System.Collections.Generic;
using VividWorld.Core.Events;

namespace VividWorld.Core.Rumors
{
    public sealed class NullEmbellishmentPolicy : IFactEmbellishmentPolicy
    {
        public static readonly NullEmbellishmentPolicy Instance = new NullEmbellishmentPolicy();

        public IReadOnlyList<Fact> Embellish(WorldEvent evt, IReadOnlyList<Fact> retained, int hop, TraitProfile teller)
        {
            return retained;
        }
    }
}
