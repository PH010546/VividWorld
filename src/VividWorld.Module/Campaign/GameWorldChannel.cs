using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;
using VividWorld.Core.Channels;
using VividWorld.Core.Config;
using VividWorld.Core.Util;
using VividWorld.Debug;

namespace VividWorld.Campaign
{
    internal sealed class GameWorldChannel : IPropagationChannel
    {
        private readonly VividWorldConfig _config;
        private readonly HeroLookup _heroLookup;
        private readonly string _playerHeroId;

        internal GameWorldChannel(VividWorldConfig config, HeroLookup heroLookup, string playerHeroId)
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));
            _heroLookup = heroLookup ?? throw new ArgumentNullException(nameof(heroLookup));
            _playerHeroId = playerHeroId ?? string.Empty;
        }

        public IReadOnlyList<ChannelLink> ContactsOf(string heroId, int maxResults)
        {
            using (DevMetrics.Measure("contacts"))
            {
                var result = QueryDetailedContacts(heroId, out _, out _, recordRelations: true);
                return result.Selected;
            }
        }

        internal ChannelQuotaResult QueryDetailedContacts(
            string heroId,
            out Hero? hero,
            out Dictionary<ChannelKind, string> emptyReasons,
            bool recordRelations = false)
        {
            emptyReasons = new Dictionary<ChannelKind, string>();
            hero = null;

            if (string.IsNullOrEmpty(heroId))
            {
                return new ChannelQuotaResult(Array.Empty<ChannelLink>(), Array.Empty<ChannelLink>());
            }

            var currentHero = _heroLookup.Get(heroId);
            if (currentHero == null)
            {
                return new ChannelQuotaResult(Array.Empty<ChannelLink>(), Array.Empty<ChannelLink>());
            }

            hero = currentHero;
            var candidates = new List<ChannelLink>();

            void AddLink(Hero? contact, ChannelKind kind)
            {
                if (contact?.StringId == null) return;
                if (contact.StringId == currentHero.StringId) return;
                if (!string.IsNullOrEmpty(_playerHeroId) && contact.StringId == _playerHeroId) return;
                if (recordRelations)
                {
                    DevMetrics.RecordGetRelationCall();
                }
                int relation = currentHero.GetRelation(contact);
                // M5.1 禁令：link.Weight 一律填 1.0，不再平方
                candidates.Add(new ChannelLink(contact.StringId, kind, 1.0, relation));
            }

            // 1. SameParty: currentHero.PartyBelongedTo -> LeaderHero + MemberRoster (D-17, D-13, D-14)
            int partyStartCount = candidates.Count;
            var party = currentHero.PartyBelongedTo;
            if (party != null)
            {
                AddLink(party.LeaderHero, ChannelKind.SameParty);

                var roster = party.MemberRoster?.GetTroopRoster();
                if (roster != null)
                {
                    for (int i = 0; i < roster.Count; i++)
                    {
                        var elem = roster[i];
                        var character = elem.Character;
                        if (character != null && character.IsHero && character.HeroObject != null)
                        {
                            AddLink(character.HeroObject, ChannelKind.SameParty);
                        }
                    }
                }

                if (candidates.Count == partyStartCount)
                {
                    emptyReasons[ChannelKind.SameParty] = "no other heroes in party";
                }
            }
            else
            {
                emptyReasons[ChannelKind.SameParty] = "not in a party";
            }

            // 2. SameArmy: currentHero.PartyBelongedTo?.Army -> Army.Parties -> 各隊 LeaderHero (D-27)
            int armyStartCount = candidates.Count;
            var army = party?.Army;
            if (army != null)
            {
                var armyParties = army.Parties;
                if (armyParties != null)
                {
                    for (int i = 0; i < armyParties.Count; i++)
                    {
                        var p = armyParties[i];
                        if (p != null && p != party)
                        {
                            AddLink(p.LeaderHero, ChannelKind.SameArmy);
                        }
                    }
                }

                if (candidates.Count == armyStartCount)
                {
                    emptyReasons[ChannelKind.SameArmy] = "no other party leaders in army";
                }
            }
            else
            {
                emptyReasons[ChannelKind.SameArmy] = "not in an army";
            }

            // 3. SameSettlement: currentHero.CurrentSettlement -> HeroesWithoutParty (住民) + Settlement.Parties -> LeaderHero (訪客) (D-15, D-25, D-26)
            int settlementStartCount = candidates.Count;
            var settlement = currentHero.CurrentSettlement;
            if (settlement != null)
            {
                var heroesWithoutParty = settlement.HeroesWithoutParty;
                if (heroesWithoutParty != null)
                {
                    for (int i = 0; i < heroesWithoutParty.Count; i++)
                    {
                        AddLink(heroesWithoutParty[i], ChannelKind.SameSettlement);
                    }
                }

                var settlementParties = settlement.Parties;
                if (settlementParties != null)
                {
                    for (int i = 0; i < settlementParties.Count; i++)
                    {
                        var p = settlementParties[i];
                        if (p != null && p != party)
                        {
                            AddLink(p.LeaderHero, ChannelKind.SameSettlement);
                        }
                    }
                }

                if (candidates.Count == settlementStartCount)
                {
                    emptyReasons[ChannelKind.SameSettlement] = "no other heroes in settlement";
                }
            }
            else
            {
                emptyReasons[ChannelKind.SameSettlement] = "not in a settlement";
            }

            // 4. SameClan: currentHero.Clan?.Heroes (D-18)
            int clanStartCount = candidates.Count;
            var clan = currentHero.Clan;
            if (clan != null)
            {
                var clanHeroes = clan.Heroes;
                if (clanHeroes != null)
                {
                    for (int i = 0; i < clanHeroes.Count; i++)
                    {
                        var ch = clanHeroes[i];
                        if (ch != null && ch.Clan == clan)
                        {
                            AddLink(ch, ChannelKind.SameClan);
                        }
                    }
                }

                if (candidates.Count == clanStartCount)
                {
                    emptyReasons[ChannelKind.SameClan] = "no other heroes in clan";
                }
            }
            else
            {
                emptyReasons[ChannelKind.SameClan] = "no clan";
            }

            // 5. KinAbroad: 血親欄位中 Clan != currentHero.Clan 者 (Spouse, ExSpouses, Children, Father, Mother, Siblings) (S-07, §15)
            int kinStartCount = candidates.Count;
            void CheckKinAbroad(Hero? kin)
            {
                if (kin == null || kin.Clan == clan) return;
                AddLink(kin, ChannelKind.KinAbroad);
            }

            CheckKinAbroad(currentHero.Spouse);
            if (currentHero.ExSpouses != null)
            {
                for (int i = 0; i < currentHero.ExSpouses.Count; i++)
                {
                    CheckKinAbroad(currentHero.ExSpouses[i]);
                }
            }
            if (currentHero.Children != null)
            {
                for (int i = 0; i < currentHero.Children.Count; i++)
                {
                    CheckKinAbroad(currentHero.Children[i]);
                }
            }
            CheckKinAbroad(currentHero.Father);
            CheckKinAbroad(currentHero.Mother);
            if (currentHero.Siblings != null)
            {
                foreach (var sib in currentHero.Siblings)
                {
                    CheckKinAbroad(sib);
                }
            }

            if (candidates.Count == kinStartCount)
            {
                emptyReasons[ChannelKind.KinAbroad] = "no kin abroad";
            }

            // 6. Kingdom: currentHero.Clan?.Kingdom -> _heroLookup.GetKingdomCorrespondents (D-18, §4.4)
            int kingdomStartCount = candidates.Count;
            var kingdom = clan?.Kingdom;
            if (kingdom != null)
            {
                var correspondents = _heroLookup.GetKingdomCorrespondents(currentHero, _config.Propagation.KingdomCorrespondentCacheSize);
                for (int i = 0; i < correspondents.Count; i++)
                {
                    AddLink(correspondents[i], ChannelKind.Kingdom);
                }

                if (candidates.Count == kingdomStartCount)
                {
                    emptyReasons[ChannelKind.Kingdom] = "no other eligible lords in kingdom";
                }
            }
            else
            {
                emptyReasons[ChannelKind.Kingdom] = "no kingdom";
            }

            // 分配名額（ChannelQuota 負責去重、名額、自補與排序）
            return ChannelQuota.Allocate(
                candidates,
                _config.Propagation,
                _config.Relation,
                selfHeroId: currentHero.StringId,
                playerHeroId: _playerHeroId);
        }

        public IReadOnlyList<string> WitnessesAt(string heroId, int maxResults)
        {
            if (string.IsNullOrEmpty(heroId) || maxResults <= 0)
            {
                return Array.Empty<string>();
            }

            var hero = _heroLookup.Get(heroId);
            var settlement = hero?.CurrentSettlement;
            if (settlement == null)
            {
                return Array.Empty<string>();
            }

            var result = new List<string>();
            var seen = new HashSet<string>(StringComparer.Ordinal);

            void TryAddHero(Hero? h)
            {
                if (result.Count >= maxResults) return;
                if (h?.StringId == null) return;
                if (h.StringId == heroId) return;
                if (!string.IsNullOrEmpty(_playerHeroId) && h.StringId == _playerHeroId) return;

                if (seen.Add(h.StringId))
                {
                    result.Add(h.StringId);
                }
            }

            var heroesWithoutParty = settlement.HeroesWithoutParty;
            if (heroesWithoutParty != null)
            {
                for (int i = 0; i < heroesWithoutParty.Count; i++)
                {
                    if (result.Count >= maxResults) break;
                    TryAddHero(heroesWithoutParty[i]);
                }
            }

            var settlementParties = settlement.Parties;
            if (settlementParties != null)
            {
                for (int i = 0; i < settlementParties.Count; i++)
                {
                    if (result.Count >= maxResults) break;
                    TryAddHero(settlementParties[i]?.LeaderHero);
                }
            }

            return result;
        }
    }
}
