using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace VividWorld.Core.Secrets
{
    public sealed class PrisonVisitLordFact
    {
        public string HeroId { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public bool IsCaptorSide { get; set; }
        public bool IsKin { get; set; }
        public int Relation { get; set; }

        public PrisonVisitLordFact() { }

        public PrisonVisitLordFact(string heroId, string name, bool isCaptorSide, bool isKin, int relation)
        {
            HeroId = heroId ?? string.Empty;
            Name = name ?? string.Empty;
            IsCaptorSide = isCaptorSide;
            IsKin = isKin;
            Relation = relation;
        }

        public bool CountsA => IsCaptorSide && IsKin;
        public bool CountsB => IsCaptorSide && IsKin && Relation > 30;
        public bool CountsC => IsCaptorSide && !IsKin && Relation > 50;
        public bool CountsAnyKinOrFriend => IsKin || Relation > 50;
    }

    public sealed class TownPrisonerFact
    {
        public string PrisonerHeroId { get; set; } = string.Empty;
        public string PrisonerId => PrisonerHeroId;
        public string PrisonerName { get; set; } = string.Empty;
        public string SettlementId { get; set; } = string.Empty;
        public string SettlementName { get; set; } = string.Empty;
        public string OwnerClanId { get; set; } = string.Empty;
        public string OwnerClanName { get; set; } = string.Empty;
        public string OwnerFactionName { get; set; } = string.Empty;
        public string PrisonerFactionName { get; set; } = string.Empty;
        public List<PrisonVisitLordFact> LordsInTown { get; set; } = new List<PrisonVisitLordFact>();

        public TownPrisonerFact() { }

        public TownPrisonerFact(
            string prisonerHeroId,
            string prisonerName,
            string settlementId,
            string settlementName,
            string ownerClanName,
            string ownerFactionName,
            string prisonerFactionName,
            List<PrisonVisitLordFact>? lordsInTown = null)
        {
            PrisonerHeroId = prisonerHeroId ?? string.Empty;
            PrisonerName = prisonerName ?? string.Empty;
            SettlementId = settlementId ?? string.Empty;
            SettlementName = settlementName ?? string.Empty;
            OwnerClanName = ownerClanName ?? string.Empty;
            OwnerFactionName = ownerFactionName ?? string.Empty;
            PrisonerFactionName = prisonerFactionName ?? string.Empty;
            LordsInTown = lordsInTown ?? new List<PrisonVisitLordFact>();
        }

        public bool HasA => LordsInTown != null && LordsInTown.Any(l => l.CountsA);
        public bool HasB => LordsInTown != null && LordsInTown.Any(l => l.CountsB);
        public bool HasC => LordsInTown != null && LordsInTown.Any(l => l.CountsC);
        public bool HasBOrC => HasB || HasC;
        public bool HasAnyFactionKinOrFriend => LordsInTown != null && LordsInTown.Any(l => l.CountsAnyKinOrFriend);
    }

    public sealed class PrisonVisitMeasureTally
    {
        public int TotalLordPrisoners { get; set; }
        public int HeldInSettlement { get; set; }
        public int HeldInParty { get; set; }
        public int CountA { get; set; }
        public int CountB { get; set; }
        public int CountC { get; set; }
        public int CountBOrC { get; set; }
        public int CountAnyFactionKinOrFriend { get; set; }

        public string FormatSummaryLine() => PrisonVisitMeasure.FormatSummaryLine(this);
    }

    public static class PrisonVisitMeasure
    {
        public static PrisonVisitMeasureTally ComputeTally(
            int totalLordPrisoners,
            int heldInParty,
            IReadOnlyList<TownPrisonerFact>? townPrisoners)
        {
            var list = townPrisoners ?? Array.Empty<TownPrisonerFact>();
            int countA = 0;
            int countB = 0;
            int countC = 0;
            int countBOrC = 0;
            int countAny = 0;

            foreach (var p in list)
            {
                if (p.HasA) countA++;
                if (p.HasB) countB++;
                if (p.HasC) countC++;
                if (p.HasBOrC) countBOrC++;
                if (p.HasAnyFactionKinOrFriend) countAny++;
            }

            return new PrisonVisitMeasureTally
            {
                TotalLordPrisoners = totalLordPrisoners,
                HeldInSettlement = list.Count,
                HeldInParty = heldInParty,
                CountA = countA,
                CountB = countB,
                CountC = countC,
                CountBOrC = countBOrC,
                CountAnyFactionKinOrFriend = countAny
            };
        }

        public static PrisonVisitMeasureTally ComputeTally(
            int totalLordPrisoners,
            int heldInTownCount,
            int heldInPartyCount,
            IReadOnlyList<TownPrisonerFact>? townFacts)
        {
            var tally = ComputeTally(totalLordPrisoners, heldInPartyCount, townFacts);
            if (townFacts == null && heldInTownCount > 0)
            {
                tally.HeldInSettlement = heldInTownCount;
            }
            return tally;
        }

        public static string DescribeLord(PrisonVisitLordFact lord)
        {
            if (lord == null) return "none (null)";

            var matches = new List<string>();
            if (lord.CountsA) matches.Add("A");
            if (lord.CountsB) matches.Add("B");
            if (lord.CountsC) matches.Add("C");

            if (matches.Count > 0)
            {
                return string.Join(", ", matches);
            }

            if (!lord.IsCaptorSide)
            {
                if (lord.IsKin)
                {
                    return string.Format(CultureInfo.InvariantCulture, "none (not captor side: is kin, relation {0})", lord.Relation);
                }
                if (lord.Relation > 50)
                {
                    return string.Format(CultureInfo.InvariantCulture, "none (not captor side: friend, relation {0} > 50)", lord.Relation);
                }
                return string.Format(CultureInfo.InvariantCulture, "none (not captor side, not kin, relation {0} <= 50)", lord.Relation);
            }

            if (lord.IsKin)
            {
                return "A";
            }

            return string.Format(CultureInfo.InvariantCulture, "none (not kin, relation {0} <= 50)", lord.Relation);
        }

        public static string FormatSummaryLine(PrisonVisitMeasureTally tally)
        {
            if (tally == null) return "prison visit measure: no tally";
            return string.Format(
                CultureInfo.InvariantCulture,
                "prison visit measure: total {0} lord prisoner(s), {1} in town, {2} in party | in town: A (kin)={3}, B (kin > 30)={4}, C (friend > 50)={5}, B or C={6} | control (any faction kin or friend)={7}",
                tally.TotalLordPrisoners,
                tally.HeldInSettlement,
                tally.HeldInParty,
                tally.CountA,
                tally.CountB,
                tally.CountC,
                tally.CountBOrC,
                tally.CountAnyFactionKinOrFriend);
        }

        public static string FormatDialogResult(PrisonVisitMeasureTally tally)
        {
            if (tally == null) return "(dev) no tally";
            return string.Format(
                CultureInfo.InvariantCulture,
                "(dev) total {0} lord prisoner(s): {1} in town, {2} in party | in town: A={3}, B={4}, C={5}, B or C={6} | any faction kin/friend={7} (see log.txt)",
                tally.TotalLordPrisoners,
                tally.HeldInSettlement,
                tally.HeldInParty,
                tally.CountA,
                tally.CountB,
                tally.CountC,
                tally.CountBOrC,
                tally.CountAnyFactionKinOrFriend);
        }
    }
}
