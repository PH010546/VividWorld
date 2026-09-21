using System;
using System.Collections.Generic;
using System.Linq;
using VividWorld.Core.Config;
using VividWorld.Core.Events;

namespace VividWorld.Core.Presentation
{
    public static class RumorTextComposer
    {
        public const string RetellPrefixTextId = "VividWorld_RetellPrefix";
        public const string RetellPrefixFallback = "I was there, in fact—";

        public static ComposedRumor Compose(WorldEvent evt, IReadOnlyList<Fact> retained,
                                            PresentationConfig cfg, bool isRetell = false)
        {
            if (retained == null || retained.Count == 0)
            {
                return new ComposedRumor
                {
                    Parts = Array.Empty<ComposedFactPart>(),
                    PrefixTextId = isRetell ? RetellPrefixTextId : null,
                    PrefixFallback = isRetell ? RetellPrefixFallback : null
                };
            }

            var factOrder = cfg?.FactOrder;

            int GetCategoryRank(FactCategory cat)
            {
                if (factOrder == null || factOrder.Length == 0) return 0;
                string catName = cat.ToString();
                for (int i = 0; i < factOrder.Length; i++)
                {
                    if (string.Equals(factOrder[i], catName, StringComparison.OrdinalIgnoreCase))
                    {
                        return i;
                    }
                }
                return int.MaxValue;
            }

            // OrderBy 在 LINQ 中是穩定排序 (stable sort)，能保持同類別內作者撰寫順序
            var sorted = retained.OrderBy(f => GetCategoryRank(f.Category)).ToList();

            var parts = sorted.Select(f => new ComposedFactPart
            {
                TextId = f.TextId ?? string.Empty,
                Fallback = f.Text ?? string.Empty,
                Vars = f.Vars != null ? new Dictionary<string, string>(f.Vars) : new Dictionary<string, string>()
            }).ToList();

            return new ComposedRumor
            {
                Parts = parts,
                PrefixTextId = isRetell ? RetellPrefixTextId : null,
                PrefixFallback = isRetell ? RetellPrefixFallback : null
            };
        }
    }
}
