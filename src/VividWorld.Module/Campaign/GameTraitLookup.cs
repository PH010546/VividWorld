using System;
using System.Collections.Generic;
using TaleWorlds.CampaignSystem.CharacterDevelopment;
using VividWorld.Core.Rumors;

namespace VividWorld.Campaign
{
    internal sealed class GameTraitLookup : IHeroTraitLookup
    {
        private readonly HeroLookup _heroLookup;
        private readonly Dictionary<string, TraitProfile?> _cache = new(StringComparer.Ordinal);

        internal GameTraitLookup(HeroLookup heroLookup)
        {
            _heroLookup = heroLookup ?? throw new ArgumentNullException(nameof(heroLookup));
        }

        public TraitProfile? Of(string heroId)
        {
            if (string.IsNullOrEmpty(heroId)) return null;
            if (_cache.TryGetValue(heroId, out var cached))
            {
                return cached;
            }

            var hero = _heroLookup.Get(heroId);
            if (hero == null)
            {
                _cache[heroId] = null;
                return null;
            }

            var profile = new TraitProfile
            {
                HeroId = heroId,
                Honor = hero.GetTraitLevel(DefaultTraits.Honor),
                Mercy = hero.GetTraitLevel(DefaultTraits.Mercy),
                Valor = hero.GetTraitLevel(DefaultTraits.Valor),
                Calculating = hero.GetTraitLevel(DefaultTraits.Calculating),
                Generosity = hero.GetTraitLevel(DefaultTraits.Generosity),
                IsAlive = hero.IsAlive,
                IsPrisoner = hero.IsPrisoner,
                IsLord = hero.IsLord,
                IsWanderer = hero.IsWanderer
            };

            _cache[heroId] = profile;
            return profile;
        }

        internal void ClearCache()
        {
            _cache.Clear();
        }
    }
}
