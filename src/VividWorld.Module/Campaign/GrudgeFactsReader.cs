using System;
using System.Linq;
using TaleWorlds.CampaignSystem;
using VividWorld.Core.Grudges;

namespace VividWorld.Campaign
{
    internal static class GrudgeFactsReader
    {
        public static EscalationFacts Read(Hero? heroA, Hero? heroB)
        {
            var facts = new EscalationFacts
            {
                AHeroId = heroA?.StringId ?? string.Empty,
                BHeroId = heroB?.StringId ?? string.Empty,
                AClanId = heroA?.Clan?.StringId,
                BClanId = heroB?.Clan?.StringId,
                ALeaderId = heroA?.Clan?.Leader?.StringId,
                BLeaderId = heroB?.Clan?.Leader?.StringId,
                AIsClanLeader = heroA != null && heroA.IsClanLeader
            };

            if (heroA != null && heroA.Clan?.Leader != null)
            {
                var leader = heroA.Clan.Leader;
                string leaderId = leader.StringId;

                if (heroA.Spouse != null && string.Equals(heroA.Spouse.StringId, leaderId, StringComparison.Ordinal))
                {
                    facts.AIsKinOfOwnLeader = true;
                    facts.AKinRelation = "spouse";
                }
                else if ((leader.Father != null && string.Equals(leader.Father.StringId, heroA.StringId, StringComparison.Ordinal)) ||
                         (leader.Mother != null && string.Equals(leader.Mother.StringId, heroA.StringId, StringComparison.Ordinal)))
                {
                    facts.AIsKinOfOwnLeader = true;
                    facts.AKinRelation = "parent";
                }
                else if ((heroA.Father != null && string.Equals(heroA.Father.StringId, leaderId, StringComparison.Ordinal)) ||
                         (heroA.Mother != null && string.Equals(heroA.Mother.StringId, leaderId, StringComparison.Ordinal)))
                {
                    facts.AIsKinOfOwnLeader = true;
                    facts.AKinRelation = "child";
                }
                else if (heroA.Siblings != null && heroA.Siblings.Any(s => s != null && string.Equals(s.StringId, leaderId, StringComparison.Ordinal)))
                {
                    facts.AIsKinOfOwnLeader = true;
                    facts.AKinRelation = "sibling";
                }
            }

            return facts;
        }
    }
}
