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
    }

    public sealed class ProminenceResult
    {
        public string HeroId { get; }
        public ProminenceTier Tier { get; }
        public string Reason { get; }            // 見 §3.3，逐字
        public int Drama { get; }                // 已套用設定值

        public ProminenceResult(string heroId, ProminenceTier tier, string reason, int drama)
        {
            HeroId = heroId ?? string.Empty;
            Tier = tier;
            Reason = reason ?? string.Empty;
            Drama = drama;
        }

        public string TierName => Tier switch
        {
            ProminenceTier.Ruler => "ruler",
            ProminenceTier.ClanLeader => "clanLeader",
            ProminenceTier.NobleMember => "nobleMember",
            ProminenceTier.Minor => "minor",
            _ => Tier.ToString()
        };

        /// <param name="templateDrama">模板的 dramaWeight；模板沒寫時為 null，印 <c>unset</c>。</param>
        public string Describe(int? templateDrama)
        {
            return string.Format(
                CultureInfo.InvariantCulture,
                "prisoner={0} -> {1} ({2}) => drama {3} (template {4})",
                HeroId,
                TierName,
                Reason,
                Drama,
                templateDrama.HasValue ? templateDrama.Value.ToString(CultureInfo.InvariantCulture) : "unset");
        }
    }

    public static class PrisonerProminence
    {
        public static ProminenceResult Classify(ProminenceFacts facts, ProminenceDramaConfig? cfg)
        {
            if (facts == null) throw new ArgumentNullException(nameof(facts));
            cfg ??= new ProminenceDramaConfig();

            string heroId = facts.HeroId ?? string.Empty;

            // 1. ruler: prisoner.IsKingdomLeader
            if (facts.IsKingdomLeader)
            {
                string kId = !string.IsNullOrEmpty(facts.KingdomId) ? facts.KingdomId! : "?";
                return new ProminenceResult(
                    heroId,
                    ProminenceTier.Ruler,
                    $"leader of kingdom {kId}",
                    cfg.Ruler);
            }

            // 2. clanLeader: prisoner.IsClanLeader
            if (facts.IsClanLeader)
            {
                string cId = !string.IsNullOrEmpty(facts.ClanId) ? facts.ClanId! : "?";
                return new ProminenceResult(
                    heroId,
                    ProminenceTier.ClanLeader,
                    $"leader of clan {cId}",
                    cfg.ClanLeader);
            }

            // 3. minor (no clan): prisoner.Clan == null
            if (string.IsNullOrEmpty(facts.ClanId))
            {
                return new ProminenceResult(
                    heroId,
                    ProminenceTier.Minor,
                    "no clan",
                    cfg.Minor);
            }

            // 4. minor (clan is minor faction): prisoner.Clan.IsMinorFaction
            if (facts.ClanIsMinorFaction)
            {
                string cId = !string.IsNullOrEmpty(facts.ClanId) ? facts.ClanId! : "?";
                return new ProminenceResult(
                    heroId,
                    ProminenceTier.Minor,
                    $"clan {cId} is a minor faction",
                    cfg.Minor);
            }

            // 5. minor (not a lord): !prisoner.IsLord
            if (!facts.IsLord)
            {
                string cId = !string.IsNullOrEmpty(facts.ClanId) ? facts.ClanId! : "?";
                return new ProminenceResult(
                    heroId,
                    ProminenceTier.Minor,
                    $"not a lord, clan {cId}",
                    cfg.Minor);
            }

            // 6. nobleMember: 其餘
            string clanId = !string.IsNullOrEmpty(facts.ClanId) ? facts.ClanId! : "?";
            return new ProminenceResult(
                heroId,
                ProminenceTier.NobleMember,
                $"lord of clan {clanId}",
                cfg.NobleMember);
        }
    }
}
