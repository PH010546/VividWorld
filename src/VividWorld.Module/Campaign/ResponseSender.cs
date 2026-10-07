#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;
using VividWorld.Core.Catalog;
using VividWorld.Core.Events;
using VividWorld.Core.Ingest;
using VividWorld.Core.Rumors;
using VividWorld.Core.Util;

namespace VividWorld.Campaign
{
    internal static class ResponseSender
    {
        public static string? SendResponse(
            WorldEvent xEvent,
            KnownByEntry stepperEntry,
            string responseType,
            double day,
            WorldEventStore store,
            HeroLookup heroLookup,
            IHeroTraitLookup? traitLookup = null,
            string? forcedSettlementId = null)
        {
            if (xEvent == null || stepperEntry == null || string.IsNullOrEmpty(responseType) || store == null)
            {
                return null;
            }

            var stepperHero = heroLookup?.Get(stepperEntry.HeroId) ?? Hero.Find(stepperEntry.HeroId);
            string? settlementId = forcedSettlementId;

            if (string.IsNullOrEmpty(settlementId))
            {
                if (stepperHero?.CurrentSettlement != null && (stepperHero.CurrentSettlement.IsTown || stepperHero.CurrentSettlement.IsCastle))
                {
                    settlementId = stepperHero.CurrentSettlement.StringId;
                }
                else
                {
                    // Not in a town or castle
                    return null;
                }
            }

            var baseTemplate = EventCatalogStore.TemplateByType(responseType);
            if (baseTemplate == null)
            {
                ModLog.Warn($"ResponseSender: response template '{responseType}' not found in catalog.");
                return null;
            }

            EventTemplate templateToUse = baseTemplate;
            bool isNamed = false;

            if (string.Equals(baseTemplate.Response, "denial", StringComparison.OrdinalIgnoreCase))
            {
                if (MadeUpTalk.KnowsOriginator(xEvent, stepperEntry) && !string.IsNullOrEmpty(xEvent.OriginatorHeroId))
                {
                    string stem = GetStemForDenial(baseTemplate.Type);
                    if (!string.IsNullOrEmpty(stem))
                    {
                        templateToUse = TemplateVariants.DeniedNamed(baseTemplate, stem);
                        isNamed = true;
                    }
                }
            }

            var bindings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            // Stepper role is the one role in KnowingRoles
            string stepperRole = templateToUse.KnowingRoles?.FirstOrDefault() ?? "denier";
            bindings[stepperRole] = stepperEntry.HeroId;

            string? originatorId = xEvent.OriginatorHeroId;
            if (isNamed && originatorId != null && originatorId.Length > 0)
            {
                bindings["originator"] = originatorId;
            }

            // Fill all other roles from xEvent.Participants
            foreach (var role in templateToUse.Roles.Keys)
            {
                if (string.Equals(role, stepperRole, StringComparison.OrdinalIgnoreCase)) continue;
                if (string.Equals(role, "originator", StringComparison.OrdinalIgnoreCase)) continue;

                if (xEvent.Participants != null && xEvent.Participants.TryGetValue(role, out var boundHeroId))
                {
                    bindings[role] = boundHeroId;
                }
                else
                {
                    // Special mappings if role names slightly differ
                    if (string.Equals(baseTemplate.Type, "talk_corrected_refused_aid_by_asker", StringComparison.OrdinalIgnoreCase))
                    {
                        if (string.Equals(role, "refuser", StringComparison.OrdinalIgnoreCase) && xEvent.Participants != null && xEvent.Participants.TryGetValue("refuser", out var rId))
                        {
                            bindings["refuser"] = rId;
                        }
                    }
                }
            }

            bindings["SETTLEMENT"] = settlementId!;
            bindings["settlement"] = settlementId!;

            var submission = TemplateBinder.Bind(templateToUse, bindings, day, xEvent.EventId, out var bindIssues);
            if (submission == null)
            {
                string issueDetails = string.Join("; ", bindIssues.Select(iss => $"{iss.Field}: {iss.Detail}"));
                ModLog.Warn($"ResponseSender: Failed to bind response template '{templateToUse.Type}': {issueDetails}");
                return null;
            }

            submission.AutoResolveWitnesses = true;
            submission.Fabricated = false;

            var ingestResult = store.Submit(submission, out string? eventId, out string? rejectionReason);
            if (ingestResult != IngestResult.Accepted || string.IsNullOrEmpty(eventId))
            {
                ModLog.Warn($"ResponseSender: Submit response {templateToUse.Type} rejected: {ingestResult} - {rejectionReason}");
                return null;
            }

            stepperEntry.StepForward = "done";
            stepperEntry.StepForwardEventId = eventId;

            ModLog.Info($"Response submitted: {templateToUse.Type} as {eventId} by {stepperEntry.HeroId} answering {xEvent.EventId} at {settlementId} (named: {isNamed})");

            try
            {
                Hop0SummaryLogger.LogHop0Summary(templateToUse, submission, eventId!, store, traitLookup ?? store.Traits);
            }
            catch (Exception ex)
            {
                ModLog.Error($"ResponseSender: Failed to format hop0 summary for {eventId}", ex);
            }

            return eventId;
        }

        private static string GetStemForDenial(string templateType)
        {
            switch (templateType)
            {
                case "talk_denied_spoke_against_ruler": return "TalkDeniedSpokeAgainstRuler";
                case "talk_denied_mistreated_prisoner": return "TalkDeniedMistreatedPrisoner";
                case "talk_denied_refused_aid": return "TalkDeniedRefusedAid";
                case "talk_denied_rash_capture": return "TalkDeniedRashCapture";
                case "talk_denied_poisoned": return "TalkDeniedPoisoned";
                default: return string.Empty;
            }
        }
    }
}
