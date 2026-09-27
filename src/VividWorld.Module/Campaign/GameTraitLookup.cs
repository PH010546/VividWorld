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
                // 性格值一天快取一次就夠，但「活著、被俘、領主或流浪者」白天隨時會變：
                // 被俘的人不能在被抓那天剩下的時間裡照樣傳話，剛放出來的人也不該等到隔天才能開口。
                // 這幾個屬性只讀英雄身上的狀態欄位，每次重讀不花什麼（帳本 D-90）。
                if (cached != null)
                {
                    var live = _heroLookup.Get(heroId);
                    if (live != null) RefreshStatus(cached, live);
                }
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
                Generosity = hero.GetTraitLevel(DefaultTraits.Generosity)
            };
            RefreshStatus(profile, hero);

            _cache[heroId] = profile;
            return profile;
        }

        private static void RefreshStatus(TraitProfile profile, TaleWorlds.CampaignSystem.Hero hero)
        {
            profile.IsAlive = hero.IsAlive;
            profile.IsPrisoner = hero.IsPrisoner;
            profile.IsLord = hero.IsLord;
            profile.IsWanderer = hero.IsWanderer;
            profile.IsFemale = hero.IsFemale;
        }

        internal void ClearCache()
        {
            _cache.Clear();
        }
    }
}
