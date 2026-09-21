using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Settlements;
using VividWorld.Core.Catalog;
using VividWorld.Core.Config;
using VividWorld.Core.Events;
using VividWorld.Core.Grudges;
using VividWorld.Core.Ingest;
using VividWorld.Core.Rumors;
using VividWorld.Core.Situations;
using VividWorld.Core.Util;

namespace VividWorld.Campaign
{
    internal sealed class SituationRunResult
    {
        public bool Started { get; set; }
        public bool ConditionsPassed { get; set; }
        public List<ConditionEvaluationResult> ConditionResults { get; set; } = new();
        public string? SelectedBranchId { get; set; }
        public List<string> EventIds { get; set; } = new();
        public List<IngestResult> IngestResults { get; set; } = new();
        public string? AbortReason { get; set; }
    }

    internal sealed class SituationEvaluation
    {
        public bool ConditionsPassed;
        public List<ConditionEvaluationResult> ConditionResults = new();
        public Settlement? Settlement;
        public string SettlementId = "none";
        public Dictionary<string, Hero?> AllHeroes = new();
        public Dictionary<string, string?> BoundHeroIds = new();
        public Dictionary<string, string> UnboundReasons = new();
        public Dictionary<string, SituationRoleFacts> FactsByRole = new();
    }

    internal static class SituationRunner
    {
        public static int ForcedThisSession { get; internal set; }
        public static int ScanTriggeredThisSession { get; internal set; }
        public static int EventsSubmittedThisSession { get; internal set; }
        public static int NoBranchThisSession { get; internal set; }

        /// <summary>靜態計數器跨讀檔存活；每次 OnGameStart 歸零，「this session」才名副其實。</summary>
        public static void ResetSessionCounters()
        {
            ForcedThisSession = 0;
            ScanTriggeredThisSession = 0;
            EventsSubmittedThisSession = 0;
            NoBranchThisSession = 0;
        }

        internal static SituationEvaluation Evaluate(
            SituationTemplate template,
            IReadOnlyDictionary<string, Hero> nonDerivedHeroes,
            double day,
            IHeroTraitLookup? traitLookup,
            ISituationHistory? history,
            IDictionary<string, SituationRoleFacts>? heroFactsCache = null)
        {
            if (template == null) throw new ArgumentNullException(nameof(template));
            if (nonDerivedHeroes == null) throw new ArgumentNullException(nameof(nonDerivedHeroes));

            var evaluation = new SituationEvaluation();

            // 1. Resolve settlement from non-derived heroes
            Settlement? settlement = null;
            foreach (var h in nonDerivedHeroes.Values)
            {
                if (h.CurrentSettlement != null)
                {
                    settlement = h.CurrentSettlement;
                    break;
                }
            }
            evaluation.Settlement = settlement;
            evaluation.SettlementId = settlement?.StringId ?? "none";

            // 2. Resolve derived roles
            var allHeroes = new Dictionary<string, Hero?>(StringComparer.OrdinalIgnoreCase);
            var unboundReasons = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var boundHeroIds = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);

            foreach (var kvp in nonDerivedHeroes)
            {
                allHeroes[kvp.Key] = kvp.Value;
                boundHeroIds[kvp.Key] = kvp.Value.StringId;
            }

            foreach (var roleKvp in template.Roles)
            {
                string roleName = roleKvp.Key;
                var roleDef = roleKvp.Value;
                if (!roleDef.IsDerived) continue;

                var (derivedHero, failureReason) = ResolveDerivedRole(
                    roleDef.Derived,
                    settlement,
                    nonDerivedHeroes,
                    traitLookup);

                if (derivedHero != null)
                {
                    allHeroes[roleName] = derivedHero;
                    boundHeroIds[roleName] = derivedHero.StringId;
                }
                else
                {
                    allHeroes[roleName] = null;
                    boundHeroIds[roleName] = null;
                    unboundReasons[roleName] = failureReason ?? "unbound derived role";
                }
            }

            evaluation.AllHeroes = allHeroes;
            evaluation.BoundHeroIds = boundHeroIds;
            evaluation.UnboundReasons = unboundReasons;

            // 3. Read facts for all defined roles
            var factsByRole = new Dictionary<string, SituationRoleFacts>(StringComparer.OrdinalIgnoreCase);
            foreach (var roleName in template.Roles.Keys)
            {
                allHeroes.TryGetValue(roleName, out var hero);
                SituationRoleFacts facts;
                if (hero != null && heroFactsCache != null && heroFactsCache.TryGetValue(hero.StringId, out var cached))
                {
                    facts = cached;
                }
                else
                {
                    facts = SituationFactsReader.Read(hero);
                    if (hero != null && heroFactsCache != null)
                    {
                        heroFactsCache[hero.StringId] = facts;
                    }
                }
                factsByRole[roleName] = facts;
            }
            evaluation.FactsByRole = factsByRole;

            // 4. Evaluate all conditions
            var nonDerivedHeroIds = nonDerivedHeroes.Values
                .Where(h => h != null && !string.IsNullOrEmpty(h.StringId))
                .Select(h => h.StringId)
                .ToList();

            var conditionResults = SituationConditionEvaluator.EvaluateAll(
                template.Conditions,
                factsByRole,
                boundHeroIds,
                unboundReasons,
                template.Id,
                day,
                history,
                nonDerivedHeroIds).ToList();

            evaluation.ConditionResults = conditionResults;
            evaluation.ConditionsPassed = conditionResults.All(c => c.Ok);

            return evaluation;
        }

        internal static SituationRunResult Execute(
            SituationTemplate template,
            SituationEvaluation evaluation,
            double day,
            string triggerSource,
            WorldEventStore? eventStore,
            IHeroTraitLookup? traitLookup,
            VividWorldConfig config,
            HeroLookup? heroLookup = null)
        {
            if (template == null) throw new ArgumentNullException(nameof(template));
            if (evaluation == null) throw new ArgumentNullException(nameof(evaluation));
            config ??= new VividWorldConfig();

            var result = new SituationRunResult
            {
                Started = true,
                ConditionsPassed = evaluation.ConditionsPassed,
                ConditionResults = evaluation.ConditionResults
            };

            if (string.Equals(triggerSource, "daily scan", StringComparison.OrdinalIgnoreCase))
            {
                ScanTriggeredThisSession++;
            }
            else
            {
                ForcedThisSession++;
            }

            string settlementId = evaluation.SettlementId;
            var allHeroes = evaluation.AllHeroes;
            var boundHeroIds = evaluation.BoundHeroIds;
            var unboundReasons = evaluation.UnboundReasons;
            var factsByRole = evaluation.FactsByRole;
            var conditionResults = evaluation.ConditionResults;

            // Compute situationInstanceId & branch seed
            long campaignSeed = eventStore?.CampaignSeed ?? 0;
            long instanceId = SituationSeed.ComputeInstanceId(campaignSeed, template.Id, day, boundHeroIds);

            // 5. Decider traits
            string deciderRole = template.Decider;
            allHeroes.TryGetValue(deciderRole, out var deciderHero);
            string deciderHeroId = deciderHero?.StringId ?? string.Empty;
            var deciderProfile = !string.IsNullOrEmpty(deciderHeroId) && traitLookup != null
                ? traitLookup.Of(deciderHeroId)
                : null;

            if (deciderProfile == null)
            {
                // Print header and conditions first
                var emptyDecision = new SituationDecision();
                var headerLines = SituationLogFormatter.FormatExecution(
                    template.Id,
                    instanceId,
                    boundHeroIds,
                    settlementId,
                    conditionResults,
                    emptyDecision);

                // 空決策的最後一行是 "pick: no branch available"，那不是這裡的原因——只印標頭與條件兩行
                foreach (var line in headerLines.Take(2))
                {
                    ModLog.Info(line);
                }
                ModLog.Info("  pick: decider traits unavailable");

                result.AbortReason = "decider traits unavailable";
                return result;
            }

            // 6. Select branch
            long branchSeed = SituationSeed.ComputeBranchSeed(campaignSeed, instanceId, deciderHeroId);
            var rng = new SplitMix64Rng();
            var decision = SituationBranchSelector.Select(
                template,
                factsByRole,
                boundHeroIds,
                unboundReasons,
                deciderProfile,
                config.Situations.MinBranchWeight,
                rng,
                branchSeed);

            result.SelectedBranchId = decision.SelectedBranchId;

            // 7. Log branch calculation
            var logLines = SituationLogFormatter.FormatExecution(
                template.Id,
                instanceId,
                boundHeroIds,
                settlementId,
                conditionResults,
                decision);

            foreach (var line in logLines)
            {
                ModLog.Info(line);
            }

            if (decision.SelectedBranch == null)
            {
                NoBranchThisSession++;
                result.AbortReason = "no branch available";
                return result;
            }

            // 8. Bind and submit events for selected branch
            var selectedBranch = decision.SelectedBranch;
            var situationEventCatalog = SituationCatalogStore.EventCatalog;

            foreach (var bEvent in selectedBranch.Events)
            {
                var eventTemplate = situationEventCatalog.ByType(bEvent.Type);
                if (eventTemplate == null)
                {
                    ModLog.Warn($"  SituationRunner: event template '{bEvent.Type}' not found in situation event catalog.");
                    continue;
                }

                // Build bindings
                var bindings = new Dictionary<string, string>(StringComparer.Ordinal);
                foreach (var kvp in bEvent.Bind)
                {
                    string placeholder = kvp.Key;
                    string target = kvp.Value;
                    if (string.Equals(target, "@settlement", StringComparison.Ordinal))
                    {
                        bindings[placeholder] = settlementId;
                    }
                    else if (boundHeroIds.TryGetValue(target, out var hId) && !string.IsNullOrEmpty(hId))
                    {
                        bindings[placeholder] = hId!;
                    }
                    else
                    {
                        bindings[placeholder] = string.Empty;
                    }
                }

                var submission = TemplateBinder.Bind(eventTemplate, bindings, day, null, out var bindIssues);
                if (submission == null)
                {
                    string issueDetails = string.Join("; ", bindIssues.Select(iss => $"{iss.Field}: {iss.Detail}"));
                    ModLog.Warn($"  SituationRunner: Failed to bind template '{bEvent.Type}': {issueDetails}");
                    continue;
                }

                // Set SituationId on the submission (spec §3.1)
                submission.SituationId = template.Id;

                string varsStr = string.Join(", ", bEvent.Bind.Select(b =>
                {
                    bindings.TryGetValue(b.Key, out var val);
                    return $"{b.Key}={val ?? ""}";
                }));

                if (eventStore == null)
                {
                    ModLog.Warn($"  SituationRunner: event store is null, cannot submit '{bEvent.Type}'.");
                    continue;
                }

                // 同一天、同一類事件、同一組參與者已經有一則 ⇒ 不再產生。
                // EventId.Mint 撞號時會加鹽另鑄新 id，不會回 RejectedDuplicate，所以這一關要自己擋（SE1 自驗時發現）。
                int todayBucket = RumorSeed.DayBucket(day);
                var participantSet = submission.Participants.Values
                    .Where(v => !string.IsNullOrEmpty(v))
                    .OrderBy(v => v, StringComparer.Ordinal)
                    .ToList();
                var sameToday = eventStore.Index?.Entries?.FirstOrDefault(e =>
                    e != null
                    && string.Equals(e.Type, submission.Type, StringComparison.Ordinal)
                    && RumorSeed.DayBucket(e.Day) == todayBucket
                    && (e.ParticipantHeroIds ?? new List<string>())
                        .OrderBy(v => v, StringComparer.Ordinal)
                        .SequenceEqual(participantSet, StringComparer.Ordinal));
                if (sameToday != null)
                {
                    result.IngestResults.Add(IngestResult.RejectedDuplicate);
                    ModLog.Info($"  submit {bEvent.Type} skipped: same event already exists today as {sameToday.EventId} (instance {instanceId.ToString(CultureInfo.InvariantCulture)})");
                    continue;
                }

                var ingestResult = eventStore.Submit(submission, out string? eventId, out string? rejectionReason);
                result.IngestResults.Add(ingestResult);

                if (ingestResult != IngestResult.Accepted || string.IsNullOrEmpty(eventId))
                {
                    ModLog.Info($"  submit {bEvent.Type} rejected: {ingestResult} - {rejectionReason}");
                }
                else
                {
                    EventsSubmittedThisSession++;
                    result.EventIds.Add(eventId!);

                    ModLog.Info($"  bound {bEvent.Type} as {eventId} ({varsStr})");

                    var submittedFactIds = new HashSet<string>(submission.Facts.Select(f => f.Id), StringComparer.Ordinal);
                    foreach (var tf in eventTemplate.Facts)
                    {
                        if (submittedFactIds.Contains(tf.Id))
                        {
                            if (tf.Optional)
                            {
                                ModLog.Info($"  fact '{tf.Id}' kept");
                            }
                        }
                        else
                        {
                            var why = bindIssues.FirstOrDefault(iss =>
                                !string.IsNullOrEmpty(iss.Detail) && iss.Detail.Contains($"'{tf.Id}'"));
                            string reason = why != null ? $"{why.Field}: {why.Detail}" : "reason not reported by the binder";
                            ModLog.Info($"  fact '{tf.Id}' dropped - {reason}");
                        }
                    }

                    try
                    {
                        Hop0SummaryLogger.LogHop0Summary(eventTemplate, submission, eventId!, eventStore, traitLookup);
                    }
                    catch (Exception ex)
                    {
                        ModLog.Error($"SituationRunner: Failed to format hop0 summary for {eventId}", ex);
                    }
                }
            }

            // 9. Grudge ledger & relation application (SE2)
            if (selectedBranch.Grudges != null && selectedBranch.Grudges.Count > 0 && eventStore != null)
            {
                // 關掉時完全惰性：不載入事件、不記帳、不跳過計數，只留一行說明（卡片 §0 完成的定義 7）
                if (config.Situations?.GrudgesEnabled == false)
                {
                    ModLog.Info(GrudgeLogFormatter.FormatDisabled(selectedBranch.Grudges.Count));
                    return result;
                }

                heroLookup ??= new HeroLookup();
                var groupedByEvt = new Dictionary<string, (WorldEvent Evt, List<GrudgeRequest> Reqs)>(StringComparer.Ordinal);

                string? lastDuplicateEventId = null;
                var lastDuplicate = result.IngestResults.Count > 0 && result.IngestResults.Contains(IngestResult.RejectedDuplicate)
                    ? eventStore.Index?.Entries?.FirstOrDefault(e =>
                        e != null
                        && RumorSeed.DayBucket(e.Day) == RumorSeed.DayBucket(day)
                        && selectedBranch.Events.Any(be => string.Equals(be.Type, e.Type, StringComparison.Ordinal)))
                    : null;
                if (lastDuplicate != null)
                {
                    lastDuplicateEventId = lastDuplicate.EventId;
                }

                foreach (var gDef in selectedBranch.Grudges)
                {
                    allHeroes.TryGetValue(gDef.From, out var heroFrom);
                    allHeroes.TryGetValue(gDef.To, out var heroTo);

                    var req = new GrudgeRequest
                    {
                        FromRole = gDef.From,
                        ToRole = gDef.To,
                        FromHeroId = heroFrom?.StringId ?? string.Empty,
                        AboutHeroId = heroTo?.StringId ?? string.Empty,
                        Requested = gDef.Amount,
                        LedgerOnly = gDef.LedgerOnly,
                        ForceClanEscalation = string.Equals(gDef.Escalate, "clan", StringComparison.OrdinalIgnoreCase)
                    };

                    WorldEvent? targetEvt = null;
                    if (!string.IsNullOrEmpty(req.FromHeroId))
                    {
                        foreach (var eId in result.EventIds)
                        {
                            var evt = eventStore.Load(eId);
                            if (evt?.KnownBy != null && evt.KnownBy.Any(k => string.Equals(k.HeroId, req.FromHeroId, StringComparison.Ordinal) && k.Hop == 0))
                            {
                                targetEvt = evt;
                                break;
                            }
                        }
                    }

                    if (targetEvt == null)
                    {
                        GrudgeApplier.SkippedThisSession++;
                        if (result.EventIds.Count == 0 && !string.IsNullOrEmpty(lastDuplicateEventId))
                        {
                            // 沒有任何事件被收錄，是因為同一天同一組人已經有一則了 ⇒ 理由不是「不是知情者」
                            ModLog.Info(GrudgeLogFormatter.FormatSkippedDuplicateEvent(
                                req.FromRole, req.FromHeroId, req.ToRole, req.AboutHeroId, lastDuplicateEventId!));
                        }
                        else
                        {
                            string eventIdsStr = result.EventIds.Count > 0
                                ? string.Join(", ", result.EventIds)
                                : "(none)";
                            ModLog.Info(GrudgeLogFormatter.FormatSkippedNotHop0(
                                req.FromRole, req.FromHeroId, req.ToRole, req.AboutHeroId, eventIdsStr));
                        }
                    }
                    else
                    {
                        if (!groupedByEvt.TryGetValue(targetEvt.EventId, out var tuple))
                        {
                            tuple = (targetEvt, new List<GrudgeRequest>());
                            groupedByEvt[targetEvt.EventId] = tuple;
                        }
                        tuple.Reqs.Add(req);
                    }
                }

                foreach (var tuple in groupedByEvt.Values)
                {
                    GrudgeApplier.Apply(tuple.Evt, tuple.Reqs, day, eventStore, heroLookup, traitLookup ?? eventStore.Traits, config);
                }
            }

            return result;
        }

        public static SituationRunResult Run(
            SituationTemplate template,
            IReadOnlyDictionary<string, Hero> nonDerivedHeroes,
            double day,
            string triggerSource,
            WorldEventStore? eventStore,
            IHeroTraitLookup? traitLookup,
            VividWorldConfig config,
            HeroLookup? heroLookup = null)
        {
            if (template == null) throw new ArgumentNullException(nameof(template));
            if (nonDerivedHeroes == null) throw new ArgumentNullException(nameof(nonDerivedHeroes));
            config ??= new VividWorldConfig();

            ISituationHistory? history = eventStore?.Index?.Entries != null
                ? new SituationHistoryIndex(eventStore.Index.Entries, day)
                : null;

            var evaluation = Evaluate(template, nonDerivedHeroes, day, traitLookup, history);
            if (!evaluation.ConditionsPassed)
            {
                return new SituationRunResult
                {
                    Started = false,
                    ConditionsPassed = false,
                    ConditionResults = evaluation.ConditionResults,
                    AbortReason = "conditions failed"
                };
            }

            return Execute(template, evaluation, day, triggerSource, eventStore, traitLookup, config, heroLookup);
        }

        private static (Hero? Hero, string? FailureReason) ResolveDerivedRole(
            string? derivedStrategy,
            Settlement? settlement,
            IReadOnlyDictionary<string, Hero> boundHeroes,
            IHeroTraitLookup? traitLookup)
        {
            if (string.Equals(derivedStrategy, "settlementOwnerClanLeader", StringComparison.Ordinal))
            {
                if (settlement?.OwnerClan == null)
                {
                    return (null, "settlement has no owner clan");
                }

                var leader = settlement.OwnerClan.Leader;
                if (leader == null)
                {
                    return (null, "owner clan has no leader");
                }

                string? playerHeroId = Hero.MainHero?.StringId;
                if (leader == Hero.MainHero || (!string.IsNullOrEmpty(playerHeroId) && leader.StringId == playerHeroId))
                {
                    return (null, $"owner clan leader {leader.StringId} is the player");
                }

                foreach (var kvp in boundHeroes)
                {
                    if (kvp.Value?.StringId == leader.StringId)
                    {
                        return (null, $"owner clan leader {leader.StringId} is already {kvp.Key}");
                    }
                }

                if (EligibilityLabel.GetRejectionReason(leader, traitLookup) != null)
                {
                    return (null, $"owner clan leader {leader.StringId} is not eligible");
                }

                return (leader, null);
            }

            return (null, $"unknown derived strategy '{derivedStrategy}'");
        }
    }
}
