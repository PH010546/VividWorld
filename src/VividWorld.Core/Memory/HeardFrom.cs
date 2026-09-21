using System;
using System.Collections.Generic;
using VividWorld.Core.Events;

namespace VividWorld.Core.Memory
{
    public static class HeardFrom
    {
        public static bool ToldBefore(KnownByEntry e, string tellerId)
        {
            if (e == null || string.IsNullOrEmpty(tellerId)) return false;
            if (e.HeardFromIds != null)
            {
                return e.HeardFromIds.Contains(tellerId);
            }
            return e.SourceHeroId != null && string.Equals(e.SourceHeroId, tellerId, StringComparison.Ordinal);
        }

        public static void Materialize(KnownByEntry e)
        {
            if (e == null) return;
            if (e.HeardFromIds == null)
            {
                e.HeardFromIds = e.SourceHeroId != null
                    ? new List<string> { e.SourceHeroId }
                    : new List<string>();
            }
        }
    }
}
