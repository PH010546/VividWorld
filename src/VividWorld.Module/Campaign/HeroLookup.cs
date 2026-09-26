using System;
using System.Collections.Generic;
using TaleWorlds.CampaignSystem;

namespace VividWorld.Campaign
{
    internal sealed class HeroLookup
    {
        private readonly Dictionary<string, Hero> _byId = new(StringComparer.Ordinal);
        private readonly Dictionary<Kingdom, List<Hero>> _byKingdom = new();
        private readonly Dictionary<string, List<Hero>> _kingdomCorrespondentCache = new(StringComparer.Ordinal);

        internal int KingdomScanCountToday { get; private set; }

        internal IEnumerable<Hero> AllAlive => (IEnumerable<Hero>?)Hero.AllAliveHeroes ?? _byId.Values;

        internal Hero? Get(string stringId)
        {
            if (string.IsNullOrEmpty(stringId)) return null;
            if (_byId.TryGetValue(stringId, out var hero)) return hero;

            // D-22: 後備查詢
            var found = Hero.Find(stringId);
            if (found != null && found.IsAlive)
            {
                _byId[stringId] = found;
                return found;
            }
            return null;
        }

        internal IReadOnlyList<Hero> GetByKingdom(Kingdom kingdom)
        {
            if (kingdom != null && _byKingdom.TryGetValue(kingdom, out var list))
            {
                return list;
            }
            return Array.Empty<Hero>();
        }

        internal IReadOnlyList<Hero> GetKingdomCorrespondents(Hero teller, int count)
        {
            if (teller?.Clan?.Kingdom == null || count <= 0)
            {
                return Array.Empty<Hero>();
            }

            if (_kingdomCorrespondentCache.TryGetValue(teller.StringId, out var cached))
            {
                return cached;
            }

            KingdomScanCountToday++;

            var kingdom = teller.Clan.Kingdom;
            var pool = GetByKingdom(kingdom);
            var eligible = new List<Hero>();

            foreach (var h in pool)
            {
                if (h == null || h.StringId == null || h == teller) continue;
                if (!h.IsAlive || h.IsPrisoner) continue;
                if (!h.IsLord && !h.IsWanderer) continue;

                eligible.Add(h);
            }

            eligible.Sort((a, b) =>
            {
                int relA = teller.GetRelation(a);
                int relB = teller.GetRelation(b);
                int cmp = relB.CompareTo(relA); // 降冪：好感度高的排前面
                if (cmp != 0) return cmp;
                return string.CompareOrdinal(a.StringId, b.StringId);
            });

            var result = eligible.Count > count ? eligible.GetRange(0, count) : eligible;
            _kingdomCorrespondentCache[teller.StringId] = result;
            return result;
        }

        internal void ResetDailyKingdomScan()
        {
            _kingdomCorrespondentCache.Clear();
            KingdomScanCountToday = 0;
        }

        internal void RebuildAll()
        {
            _byId.Clear();
            ResetDailyKingdomScan();

            var alive = Hero.AllAliveHeroes;
            if (alive != null)
            {
                foreach (var hero in alive)
                {
                    if (hero?.StringId != null)
                    {
                        _byId[hero.StringId] = hero;
                    }
                }
            }
            RebuildKingdomIndex();
        }

        internal void RebuildKingdomIndex()
        {
            _byKingdom.Clear();
            var alive = Hero.AllAliveHeroes;
            if (alive != null)
            {
                foreach (var hero in alive)
                {
                    var kingdom = hero?.Clan?.Kingdom;
                    if (kingdom != null && hero?.StringId != null)
                    {
                        if (!_byKingdom.TryGetValue(kingdom, out var list))
                        {
                            list = new List<Hero>();
                            _byKingdom[kingdom] = list;
                        }
                        list.Add(hero);
                    }
                }
                foreach (var list in _byKingdom.Values)
                {
                    list.Sort((a, b) => string.CompareOrdinal(a.StringId, b.StringId));
                }
            }
        }
    }
}
