using System;
using System.Collections.Generic;
using TaleWorlds.CampaignSystem;
using VividWorld.Core.Secrets;

namespace VividWorld.Campaign
{
    internal sealed class GameSecretRelationshipLookup : ISecretRelationshipLookup
    {
        private readonly HeroLookup? _heroLookup;

        internal GameSecretRelationshipLookup(HeroLookup? heroLookup = null)
        {
            _heroLookup = heroLookup;
        }

        private Hero? FindHero(string heroId)
        {
            if (string.IsNullOrEmpty(heroId)) return null;
            return _heroLookup?.Get(heroId) ?? Hero.Find(heroId);
        }

        public IReadOnlyCollection<string> GetKin(string heroId)
        {
            var hero = FindHero(heroId);
            if (hero == null) return Array.Empty<string>();

            var set = new HashSet<string>(StringComparer.Ordinal);

            void TryAdd(Hero? candidate)
            {
                if (candidate == null || candidate == hero) return;
                if (!string.IsNullOrEmpty(candidate.StringId) && GameWorldChannel.IsFamilyOf(hero, candidate))
                {
                    set.Add(candidate.StringId);
                }
            }

            if (hero.Clan?.Heroes != null)
            {
                foreach (var clanHero in hero.Clan.Heroes)
                {
                    TryAdd(clanHero);
                }
            }

            TryAdd(hero.Spouse);
            TryAdd(hero.Father);
            TryAdd(hero.Mother);

            if (hero.Children != null)
            {
                foreach (var child in hero.Children)
                {
                    TryAdd(child);
                }
            }

            if (hero.Siblings != null)
            {
                foreach (var sibling in hero.Siblings)
                {
                    TryAdd(sibling);
                }
            }

            return set;
        }

        public IReadOnlyCollection<string> GetFriends(string heroId)
        {
            var hero = FindHero(heroId);
            if (hero == null) return Array.Empty<string>();

            var friends = new List<string>();
            foreach (var other in Hero.AllAliveHeroes)
            {
                if (other == null || other == hero || !other.IsAlive) continue;
                if (hero.GetBaseHeroRelation(other) > 50)
                {
                    if (!string.IsNullOrEmpty(other.StringId))
                    {
                        friends.Add(other.StringId);
                    }
                }
            }

            return friends;
        }

        public int? GetAffection(string heroA, string heroB)
        {
            var hA = FindHero(heroA);
            var hB = FindHero(heroB);
            if (hA == null || hB == null) return null;
            return (int)hA.GetBaseHeroRelation(hB);
        }

        public bool IsKin(string heroA, string heroB)
        {
            var hA = FindHero(heroA);
            var hB = FindHero(heroB);
            if (hA == null || hB == null) return false;
            return GameWorldChannel.IsFamilyOf(hA, hB);
        }
    }
}
