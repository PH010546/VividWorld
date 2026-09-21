using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Settlements;
using VividWorld.Core.Catalog;
using VividWorld.Core.Events;
using VividWorld.Core.Ingest;
using VividWorld.Core.Rumors;

namespace VividWorld.Campaign
{
    internal static class Hop0SummaryLogger
    {
        public static void LogHop0Summary(
            EventTemplate template,
            EventSubmission submission,
            string eventId,
            WorldEventStore? eventStore,
            IHeroTraitLookup? traitLookup)
        {
            if (template == null || submission == null || string.IsNullOrEmpty(eventId)) return;

            var evt = eventStore?.Load(eventId);
            int totalKnowers = evt?.KnownBy?.Count(k => k.Hop == 0) ?? 0;

            Hop0WitnessInfo witnessInfo;
            if (template.Origin == EventOrigin.Secret)
            {
                witnessInfo = Hop0WitnessInfo.Secret();
            }
            else
            {
                string? anchorHeroId = Hop0Seeding.AnchorOf(submission);
                string? anchorRole = null;
                if (!string.IsNullOrEmpty(anchorHeroId) && submission.Participants != null)
                {
                    foreach (var kvp in submission.Participants)
                    {
                        if (kvp.Value == anchorHeroId)
                        {
                            anchorRole = kvp.Key;
                            break;
                        }
                    }
                }

                Hero? anchorHero = !string.IsNullOrEmpty(anchorHeroId) ? Hero.Find(anchorHeroId) : null;
                Settlement? anchorSettlement = anchorHero?.CurrentSettlement;

                if (anchorSettlement == null)
                {
                    witnessInfo = Hop0WitnessInfo.AnchorNotInSettlement(anchorRole ?? "anchor", anchorHeroId ?? "none");
                }
                else
                {
                    var participantIds = new HashSet<string>(StringComparer.Ordinal);
                    if (submission.Participants != null)
                    {
                        foreach (var v in submission.Participants.Values)
                        {
                            if (!string.IsNullOrEmpty(v))
                            {
                                participantIds.Add(v);
                            }
                        }
                    }

                    var hop0Entries = evt?.KnownBy?.Where(k => k.Hop == 0).ToList() ?? new List<KnownByEntry>();
                    var hop0HeroIds = new HashSet<string>(hop0Entries.Select(k => k.HeroId).Where(id => !string.IsNullOrEmpty(id)), StringComparer.Ordinal);

                    int witnessCount = hop0Entries.Count(k => !participantIds.Contains(k.HeroId));

                    var presentHeroes = EligibilityLabel.GetPresentHeroesAtSettlement(anchorSettlement);
                    string? playerHeroId = Hero.MainHero?.StringId;
                    var nonParticipantPresent = presentHeroes
                        .Where(h => h != null && !string.IsNullOrEmpty(h.StringId) && !participantIds.Contains(h.StringId) && h.StringId != playerHeroId)
                        .ToList();

                    int presentCount = nonParticipantPresent.Count;

                    var witnessRejections = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                    foreach (var h in nonParticipantPresent)
                    {
                        if (!hop0HeroIds.Contains(h.StringId))
                        {
                            string reason = EligibilityLabel.GetRejectionReason(h, traitLookup) ?? "max witnesses cap";
                            witnessRejections.TryGetValue(reason, out int rc);
                            witnessRejections[reason] = rc + 1;
                        }
                    }

                    witnessInfo = Hop0WitnessInfo.AtSettlement(witnessCount, presentCount, anchorSettlement.StringId, witnessRejections);
                }
            }

            var participantsOrdered = new List<KeyValuePair<string, string>>();
            if (template.Roles != null && submission.Participants != null)
            {
                foreach (var role in template.Roles.Keys)
                {
                    if (submission.Participants.TryGetValue(role, out string hId) && !string.IsNullOrEmpty(hId))
                    {
                        participantsOrdered.Add(new KeyValuePair<string, string>(role, hId));
                    }
                }
            }
            if (participantsOrdered.Count == 0 && submission.Participants != null)
            {
                foreach (var kvp in submission.Participants)
                {
                    if (!string.IsNullOrEmpty(kvp.Value))
                    {
                        participantsOrdered.Add(kvp);
                    }
                }
            }

            string hop0Summary = Hop0Summary.Format(
                totalKnowers,
                participantsOrdered,
                submission.KnowingRoles,
                hId =>
                {
                    var h = Hero.Find(hId);
                    return EligibilityLabel.GetRejectionReason(h, traitLookup);
                },
                witnessInfo);

            ModLog.Info($"  {hop0Summary}");
        }
    }
}
