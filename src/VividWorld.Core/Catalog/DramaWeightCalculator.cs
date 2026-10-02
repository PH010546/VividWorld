using System;
using System.Collections.Generic;
using System.Globalization;
using VividWorld.Core.Config;
using VividWorld.Core.Events;

namespace VividWorld.Core.Catalog
{
    /// <summary>門第的四級（成親、生子看這個）。</summary>
    public enum ClanStanding
    {
        Royal,
        High,
        Ordinary,
        Minor
    }

    /// <summary>判門第用到的原生值。沒有家族時 <see cref="ClanId"/> 為 null。</summary>
    public sealed class ClanFacts
    {
        public string? ClanId { get; set; }
        public int? Tier { get; set; }
        public bool IsMinorFaction { get; set; }

        /// <summary>這個家族是不是它所屬王國的王族（<c>Kingdom.RulingClan</c>）。</summary>
        public bool IsRuling { get; set; }
        public string? KingdomId { get; set; }
    }

    public sealed class ClanStandingResult
    {
        public ClanStanding Standing { get; }
        public int Bonus { get; }
        public string Reason { get; }
        public string Facts { get; }

        public ClanStandingResult(ClanStanding standing, int bonus, string reason, string facts)
        {
            Standing = standing;
            Bonus = bonus;
            Reason = reason ?? string.Empty;
            Facts = facts ?? string.Empty;
        }

        public string StandingName => Standing switch
        {
            ClanStanding.Royal => "royal",
            ClanStanding.High => "high",
            ClanStanding.Ordinary => "ordinary",
            ClanStanding.Minor => "minor",
            _ => Standing.ToString()
        };
    }

    /// <summary>基礎分加上加成、夾在 1..10 之後的結果。</summary>
    public sealed class WeightComputation
    {
        public int BaseScore { get; }
        public int Bonus { get; }
        public int Weight { get; }
        public int Band => DramaScales.BandOfWeight(Weight);

        public WeightComputation(int baseScore, int bonus, int weight)
        {
            BaseScore = baseScore;
            Bonus = bonus;
            Weight = weight;
        }

        /// <summary>例：<c>weight 6/10 (band 3) = base 8 bonus -2</c>；夾過會多印 clamped from。</summary>
        public string Describe()
        {
            int raw = BaseScore + Bonus;
            string clamp = raw != Weight ? string.Format(CultureInfo.InvariantCulture, " (clamped from {0})", raw) : string.Empty;
            return string.Format(
                CultureInfo.InvariantCulture,
                "weight {0}/10 (band {1}) = base {2} bonus {3:+0;-0;+0}{4}",
                Weight, Band, BaseScore, Bonus, clamp);
        }
    }

    /// <summary>家族的事裡的一方（成親的一個新人、生子的一位家長）。</summary>
    public sealed class FamilyParty
    {
        public string Role { get; }
        public string HeroId { get; }
        public ProminenceResult Person { get; }
        public ClanStandingResult Clan { get; }

        /// <summary>這一方算進去的加成：門第的加成；本人是國王或族長時，跟本人的加成取高的。</summary>
        public int EffectiveBonus { get; }
        public string EffectiveSource { get; }

        public FamilyParty(string role, string heroId, ProminenceResult person, ClanStandingResult clan)
        {
            Role = role ?? string.Empty;
            HeroId = heroId ?? string.Empty;
            Person = person;
            Clan = clan;
            bool personCounts = person.Tier == ProminenceTier.Ruler || person.Tier == ProminenceTier.ClanLeader;
            if (personCounts && person.Bonus > clan.Bonus)
            {
                EffectiveBonus = person.Bonus;
                EffectiveSource = "person " + person.TierName;
            }
            else
            {
                EffectiveBonus = clan.Bonus;
                EffectiveSource = "clan " + clan.StandingName;
            }
        }
    }

    public sealed class FamilyWeightResult
    {
        public IReadOnlyList<FamilyParty> Parties { get; }
        public int ChosenIndex { get; }
        public WeightComputation Computation { get; }

        public FamilyWeightResult(IReadOnlyList<FamilyParty> parties, int chosenIndex, WeightComputation computation)
        {
            Parties = parties;
            ChosenIndex = chosenIndex;
            Computation = computation;
        }

        /// <summary>日誌用，每一方一行：誰、本人判成哪一級、家族判成哪一級（含判定用到的值）、算進去的加成；最後一行是取了哪一方。</summary>
        public IEnumerable<string> DescribeLines()
        {
            for (int i = 0; i < Parties.Count; i++)
            {
                var p = Parties[i];
                yield return string.Format(
                    CultureInfo.InvariantCulture,
                    "{0} {1}: person {2} bonus {3:+0;-0;+0}; clan {4} ({5}) [{6}] bonus {7:+0;-0;+0}; counts {8:+0;-0;+0} via {9}",
                    p.Role, p.HeroId,
                    p.Person.TierName, p.Person.Bonus,
                    p.Clan.StandingName, p.Clan.Reason, p.Clan.Facts, p.Clan.Bonus,
                    p.EffectiveBonus, p.EffectiveSource);
            }
            if (ChosenIndex >= 0 && ChosenIndex < Parties.Count)
            {
                yield return string.Format(
                    CultureInfo.InvariantCulture,
                    "higher side: {0} {1} -> {2}",
                    Parties[ChosenIndex].Role, Parties[ChosenIndex].HeroId, Computation.Describe());
            }
        }
    }

    public static class DramaWeightCalculator
    {
        /// <summary>基礎分加加成，夾在 1..10。</summary>
        public static WeightComputation Compute(int baseScore, int bonus)
        {
            return new WeightComputation(baseScore, bonus, DramaScales.ClampWeight(baseScore + bonus));
        }

        /// <summary>判門第：沒家族 ＞ 王族 ＞ 小勢力（<c>IsMinorFaction</c>）＞ 高等家族（家族等級達門檻）＞ 一般家族。</summary>
        public static ClanStandingResult ClassifyClan(ClanFacts? facts, EventsConfig? cfg)
        {
            cfg ??= new EventsConfig();
            var bonus = cfg.WeightBonusByClanStanding ?? new ClanStandingWeightBonusConfig();
            int highMin = cfg.HighClanMinTier;
            facts ??= new ClanFacts();

            string seen = string.Format(
                CultureInfo.InvariantCulture,
                "clan={0}, tier={1}, isMinorFaction={2}, isRuling={3}, kingdom={4}, highTierFrom={5}",
                string.IsNullOrEmpty(facts.ClanId) ? "none" : facts.ClanId,
                facts.Tier.HasValue ? facts.Tier.Value.ToString(CultureInfo.InvariantCulture) : "?",
                facts.IsMinorFaction ? "yes" : "no",
                facts.IsRuling ? "yes" : "no",
                string.IsNullOrEmpty(facts.KingdomId) ? "none" : facts.KingdomId,
                highMin);

            if (string.IsNullOrEmpty(facts.ClanId))
            {
                return new ClanStandingResult(ClanStanding.Minor, bonus.Minor, "no clan", seen);
            }
            if (facts.IsRuling)
            {
                return new ClanStandingResult(ClanStanding.Royal, bonus.Royal, $"ruling clan of kingdom {facts.KingdomId ?? "?"}", seen);
            }
            if (facts.IsMinorFaction)
            {
                return new ClanStandingResult(ClanStanding.Minor, bonus.Minor, $"clan {facts.ClanId} is a minor faction", seen);
            }
            if (facts.Tier.HasValue && facts.Tier.Value >= highMin)
            {
                return new ClanStandingResult(ClanStanding.High, bonus.High, $"clan {facts.ClanId} tier {facts.Tier.Value} >= {highMin}", seen);
            }
            string tierText = facts.Tier.HasValue ? facts.Tier.Value.ToString(CultureInfo.InvariantCulture) : "?";
            return new ClanStandingResult(ClanStanding.Ordinary, bonus.Ordinary, $"clan {facts.ClanId} tier {tierText}", seen);
        }

        /// <summary>
        /// 家族的事（成親、生子）：每一方各自算加成，取加成最高的一方（同分取靠前的）。
        /// 一方的加成＝門第的加成；本人是國王或族長時，跟本人的加成取高的。
        /// </summary>
        public static FamilyWeightResult ComputeForFamily(int baseScore, IReadOnlyList<FamilyParty> parties)
        {
            if (parties == null || parties.Count == 0)
            {
                return new FamilyWeightResult(Array.Empty<FamilyParty>(), -1, Compute(baseScore, 0));
            }
            int best = 0;
            for (int i = 1; i < parties.Count; i++)
            {
                if (parties[i].EffectiveBonus > parties[best].EffectiveBonus) best = i;
            }
            return new FamilyWeightResult(parties, best, Compute(baseScore, parties[best].EffectiveBonus));
        }
    }
}
