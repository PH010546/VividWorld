using System;
using System.Globalization;
using VividWorld.Core.Config;

namespace VividWorld.Core.Catalog
{
    public enum ProminenceTier
    {
        Ruler,
        ClanLeader,
        NobleMember,
        Minor
    }

    public sealed class ProminenceFacts
    {
        public string HeroId { get; set; } = string.Empty;
        public bool IsKingdomLeader { get; set; }
        public string? KingdomId { get; set; }   // 只供 log；IsKingdomLeader 為 true 時 Module 填 MapFaction.StringId
        public bool IsClanLeader { get; set; }
        public string? ClanId { get; set; }      // Clan 為 null 時為 null
        public bool ClanIsMinorFaction { get; set; }
        public bool IsLord { get; set; }

        /// <summary>只供日誌：家族等級（<c>Clan.Tier</c>）；沒有家族時為 null。</summary>
        public int? ClanTier { get; set; }

        /// <summary>只供日誌：這個人的家族是不是所屬王國的王族（<c>Kingdom.RulingClan</c>）。</summary>
        public bool ClanIsRuling { get; set; }
    }

    public sealed class ProminenceResult
    {
        public string HeroId { get; }
        public ProminenceTier Tier { get; }
        public string Reason { get; }            // 判成這一級的理由，逐字
        public int Bonus { get; }                // 這一級的加成（設定值），加在模板的基礎分上
        public string Facts { get; }             // 判定用到的值，供日誌

        public ProminenceResult(string heroId, ProminenceTier tier, string reason, int bonus, string facts = "")
        {
            HeroId = heroId ?? string.Empty;
            Tier = tier;
            Reason = reason ?? string.Empty;
            Bonus = bonus;
            Facts = facts ?? string.Empty;
        }

        public string TierName => Tier switch
        {
            ProminenceTier.Ruler => "ruler",
            ProminenceTier.ClanLeader => "clanLeader",
            ProminenceTier.NobleMember => "nobleMember",
            ProminenceTier.Minor => "minor",
            _ => Tier.ToString()
        };

        /// <summary>日誌用：誰、判成哪一級、為什麼、判定用到的值、加成。</summary>
        public string Describe()
        {
            return string.Format(
                CultureInfo.InvariantCulture,
                "hero={0} -> {1} ({2}) [{3}] bonus {4:+0;-0;+0}",
                HeroId,
                TierName,
                Reason,
                Facts,
                Bonus);
        }
    }

    public static class PrisonerProminence
    {
        public static ProminenceResult Classify(ProminenceFacts facts, ProminenceWeightBonusConfig? cfg)
        {
            if (facts == null) throw new ArgumentNullException(nameof(facts));
            cfg ??= new ProminenceWeightBonusConfig();

            string heroId = facts.HeroId ?? string.Empty;
            string seen = string.Format(
                CultureInfo.InvariantCulture,
                "isKingdomLeader={0}, isClanLeader={1}, clan={2}, clanTier={3}, clanIsMinorFaction={4}, clanIsRuling={5}, isLord={6}",
                facts.IsKingdomLeader ? "yes" : "no",
                facts.IsClanLeader ? "yes" : "no",
                string.IsNullOrEmpty(facts.ClanId) ? "none" : facts.ClanId,
                facts.ClanTier.HasValue ? facts.ClanTier.Value.ToString(CultureInfo.InvariantCulture) : "?",
                facts.ClanIsMinorFaction ? "yes" : "no",
                facts.ClanIsRuling ? "yes" : "no",
                facts.IsLord ? "yes" : "no");

            // 1. ruler: prisoner.IsKingdomLeader
            if (facts.IsKingdomLeader)
            {
                string kId = !string.IsNullOrEmpty(facts.KingdomId) ? facts.KingdomId! : "?";
                return new ProminenceResult(heroId, ProminenceTier.Ruler, $"leader of kingdom {kId}", cfg.Ruler, seen);
            }

            // 2. clanLeader: prisoner.IsClanLeader
            if (facts.IsClanLeader)
            {
                string cId = !string.IsNullOrEmpty(facts.ClanId) ? facts.ClanId! : "?";
                return new ProminenceResult(heroId, ProminenceTier.ClanLeader, $"leader of clan {cId}", cfg.ClanLeader, seen);
            }

            // 3. minor (no clan): prisoner.Clan == null
            if (string.IsNullOrEmpty(facts.ClanId))
            {
                return new ProminenceResult(heroId, ProminenceTier.Minor, "no clan", cfg.Minor, seen);
            }

            // 4. minor (clan is minor faction): prisoner.Clan.IsMinorFaction
            if (facts.ClanIsMinorFaction)
            {
                return new ProminenceResult(heroId, ProminenceTier.Minor, $"clan {facts.ClanId} is a minor faction", cfg.Minor, seen);
            }

            // 5. minor (not a lord): !prisoner.IsLord
            if (!facts.IsLord)
            {
                return new ProminenceResult(heroId, ProminenceTier.Minor, $"not a lord, clan {facts.ClanId}", cfg.Minor, seen);
            }

            // 6. nobleMember: 其餘
            return new ProminenceResult(heroId, ProminenceTier.NobleMember, $"lord of clan {facts.ClanId}", cfg.NobleMember, seen);
        }
    }
}
