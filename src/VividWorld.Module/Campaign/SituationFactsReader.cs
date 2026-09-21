using System;
using TaleWorlds.CampaignSystem;
using VividWorld.Core.Situations;

namespace VividWorld.Campaign
{
    internal static class SituationFactsReader
    {
        public static SituationRoleFacts Read(Hero? hero)
        {
            if (hero == null)
            {
                return new SituationRoleFacts();
            }

            var settlement = hero.CurrentSettlement;
            string? settlementKind = null;
            if (settlement != null)
            {
                if (settlement.IsTown)
                {
                    settlementKind = "town";
                }
                else if (settlement.IsCastle)
                {
                    settlementKind = "castle";
                }
                else
                {
                    settlementKind = "other";
                }
            }

            return new SituationRoleFacts
            {
                HeroId = hero.StringId ?? string.Empty,
                ClanId = hero.Clan?.StringId,
                KingdomId = hero.Clan?.Kingdom?.StringId,
                IsClanLeader = hero.IsClanLeader,
                ClanTier = hero.Clan != null ? (int?)hero.Clan.Tier : null,
                SettlementId = settlement?.StringId,
                SettlementKind = settlementKind
            };
        }
    }
}
