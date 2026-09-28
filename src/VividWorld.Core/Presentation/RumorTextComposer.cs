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
        public const string RetellSelfPrefixTextId = RumorPrefixSelector.RetellSelfTextId;
        public const string RetellSelfPrefixFallback = RumorPrefixSelector.RetellSelfFallback;

        public static ComposedRumor Compose(IReadOnlyList<Fact> retained,
                                            PresentationConfig cfg, RumorPrefix? prefix)
        {
            return Compose(null, retained, cfg, prefix, null);
        }

        public static ComposedRumor Compose(WorldEvent? evt, IReadOnlyList<Fact> retained,
                                            PresentationConfig cfg, RumorPrefix? prefix, string? speakerHeroId = null)
        {
            var prefixTextId = prefix?.TextId;
            var prefixFallback = prefix?.Fallback;
            var prefixVars = new Dictionary<string, string>();
            if (prefix?.Vars != null)
            {
                foreach (var kvp in prefix.Vars)
                {
                    prefixVars[kvp.Key] = kvp.Value;
                }
            }

            string? speakerRole = !string.IsNullOrEmpty(speakerHeroId) ? evt?.RoleOf(speakerHeroId!) : null;

            var roles = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (evt?.Participants != null)
            {
                foreach (var kvp in evt.Participants)
                {
                    if (!string.IsNullOrEmpty(kvp.Key) && !string.IsNullOrEmpty(kvp.Value))
                    {
                        roles[kvp.Key.ToLowerInvariant()] = kvp.Value;
                    }
                }
            }

            if (retained == null || retained.Count == 0)
            {
                return new ComposedRumor
                {
                    Parts = Array.Empty<ComposedFactPart>(),
                    PrefixTextId = prefixTextId,
                    PrefixFallback = prefixFallback,
                    PrefixVars = prefixVars,
                    SpeakerHeroId = speakerHeroId,
                    SpeakerRole = speakerRole,
                    Roles = roles
                };
            }

            var factOrder = cfg?.FactOrder;
            bool placeFirst = cfg?.PlaceFirst ?? true;

            int GetCategoryRank(FactCategory cat)
            {
                if (placeFirst && cat == FactCategory.Where)
                {
                    return -1;
                }
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
                PrefixTextId = prefixTextId,
                PrefixFallback = prefixFallback,
                PrefixVars = prefixVars,
                SpeakerHeroId = speakerHeroId,
                SpeakerRole = speakerRole,
                Roles = roles
            };
        }

        public static ComposedRumor Compose(IReadOnlyList<Fact> retained,
                                            PresentationConfig cfg, bool isRetell = false)
        {
            var prefix = isRetell
                ? new RumorPrefix(RumorPrefixKind.Retell, RetellPrefixTextId, RetellPrefixFallback)
                : null;
            return Compose(null, retained, cfg, prefix, null);
        }

        public static ComposedRumor Compose(WorldEvent? evt, IReadOnlyList<Fact> retained,
                                            PresentationConfig cfg, bool isRetell = false, string? speakerHeroId = null)
        {
            RumorPrefix? prefix = null;
            if (isRetell)
            {
                bool isParticipant = !string.IsNullOrEmpty(speakerHeroId) && evt?.RoleOf(speakerHeroId!) != null;
                prefix = isParticipant
                    ? new RumorPrefix(RumorPrefixKind.RetellSelf, RetellSelfPrefixTextId, RetellSelfPrefixFallback)
                    : new RumorPrefix(RumorPrefixKind.Retell, RetellPrefixTextId, RetellPrefixFallback);
            }
            return Compose(evt, retained, cfg, prefix, speakerHeroId);
        }
    }
}
