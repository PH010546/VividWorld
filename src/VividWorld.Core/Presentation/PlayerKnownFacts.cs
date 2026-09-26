#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using VividWorld.Core.Events;
using VividWorld.Core.Rumors;

namespace VividWorld.Core.Presentation
{
    /// <summary>
    /// 玩家知道哪幾條碎片——全專案唯一算法（卡 MF3a §1.1）。
    /// </summary>
    public static class PlayerKnownFacts
    {
        public static IReadOnlyList<Fact> Of(WorldEvent evt, KnownByEntry playerEntry, RumorEngine? engine)
        {
            if (evt == null || playerEntry == null) return Array.Empty<Fact>();

            if (playerEntry.KnownFactIds != null)
            {
                var set = new HashSet<string>(playerEntry.KnownFactIds, StringComparer.Ordinal);
                return evt.Facts.Where(f => set.Contains(f.Id)).ToList();
            }

            if (engine != null)
            {
                return engine.FactsAtHop(evt, playerEntry.Hop, playerEntry.SourceHeroId ?? string.Empty);
            }

            // 引擎不在就給空的，絕不退回 evt.Facts（那是 hop 0 的全知版本）
            return Array.Empty<Fact>();
        }
    }
}
