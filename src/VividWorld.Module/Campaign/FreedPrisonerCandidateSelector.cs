using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Settlements;
using VividWorld.Core.Rumors;
using VividWorld.Core.Situations;

namespace VividWorld.Campaign
{
    internal sealed class FreedPrisonerCandidateReport
    {
        public int TotalPrisonersCount { get; set; }
        public int DungeonPrisonersCount { get; set; }
        public int FreerFoundCount { get; set; }
        public List<FreedPrisonerItem> Items { get; set; } = new();
    }

    internal sealed class FreedPrisonerItem
    {
        public Hero Prisoner { get; set; } = null!;
        public Settlement Settlement { get; set; } = null!;
        public AbsentRoleSelectionResult SelectionResult { get; set; } = null!;
    }

    internal static class FreedPrisonerCandidateSelector
    {
        public static FreedPrisonerCandidateReport EvaluateAll(HeroLookup? heroLookup, IHeroTraitLookup? traitLookup)
        {
            var report = new FreedPrisonerCandidateReport();
            var allAlive = heroLookup?.AllAlive ?? (IEnumerable<Hero>?)Hero.AllAliveHeroes ?? Array.Empty<Hero>();
            var prisoners = allAlive.Where(h =>
                h != null &&
                h.IsAlive &&
                h.IsPrisoner &&
                h.IsLord &&
                !h.IsHumanPlayerCharacter &&
                h != Hero.MainHero).ToList();

            report.TotalPrisonersCount = prisoners.Count;

            var dungeonPrisoners = new List<(Hero Prisoner, Settlement Settlement)>();
            foreach (var p in prisoners)
            {
                var party = p.PartyBelongedToAsPrisoner;
                if (party != null && party.IsSettlement && party.Settlement != null && (party.Settlement.IsTown || party.Settlement.IsCastle))
                {
                    dungeonPrisoners.Add((p, party.Settlement));
                }
            }
            report.DungeonPrisonersCount = dungeonPrisoners.Count;

            // Gather candidate pool of lords present in settlements
            var lordsPool = allAlive
                .Where(h => h != null && h.IsAlive && !h.IsPrisoner && h.IsLord && h != Hero.MainHero && !h.IsHumanPlayerCharacter && h.CurrentSettlement != null)
                .ToList();

            var candidateFacts = lordsPool.Select(h => SituationFactsReader.Read(h)).ToList();

            var context = new SituationWorldContext
            {
                Traits = traitLookup,
                PlayerHeroId = Hero.MainHero?.StringId ?? "player"
            };
            SituationRunner.AttachHeroRelationReaders(context, heroLookup);

            foreach (var (prisoner, settlement) in dungeonPrisoners)
            {
                var prisonerFacts = SituationFactsReader.Read(prisoner);
                context.CurrentSettlementFactionId = settlement.MapFaction?.StringId;

                var selResult = AbsentRoleSelector.Select(
                    derivedRoleName: "freer",
                    derivedStrategy: "freerOf:prisoner",
                    targetRoleFacts: prisonerFacts,
                    currentSettlementId: settlement.StringId,
                    candidatePool: candidateFacts,
                    alreadyBoundHeroes: new Dictionary<string, string?> { ["prisoner"] = prisoner.StringId },
                    context: context);

                if (selResult.IsBound && selResult.PickedHeroId != null)
                {
                    report.FreerFoundCount++;
                }

                report.Items.Add(new FreedPrisonerItem
                {
                    Prisoner = prisoner,
                    Settlement = settlement,
                    SelectionResult = selResult
                });
            }

            return report;
        }
    }
}
