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
using VividWorld.Core.Memory;
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
        public SituationWorldContext? Context;
        public List<string> NonDerivedHeroIds = new();
        public ISituationHistory? History;
    }

    internal static class SituationRunner
    {
        public static int ForcedThisSession { get; internal set; }
        public static int ScanTriggeredThisSession { get; internal set; }
        public static int AfterEventTriggeredThisSession { get; internal set; }
        public static int EventsSubmittedThisSession { get; internal set; }
        public static int NoBranchThisSession { get; internal set; }

        /// <summary>靜態計數器跨讀檔存活；每次 OnGameStart 歸零，「this session」才名副其實。</summary>
        public static void ResetSessionCounters()
        {
            ForcedThisSession = 0;
            ScanTriggeredThisSession = 0;
            AfterEventTriggeredThisSession = 0;
            EventsSubmittedThisSession = 0;
            NoBranchThisSession = 0;
        }

        private static Dictionary<string, (Hero? Hero, string? FailureReason)>? _scanAbsentCache;
        private static HashSet<string>? _scanAbsentCacheSituations;
        public static int ScanAbsentCacheHits { get; private set; }
        public static int ScanAbsentCacheMisses { get; private set; }

        /// <summary>一次掃描期間，指定的情境把「不在場角色」的挑人結果依（情境、角色、綁到的那位英雄）快取，
        /// 同一位說的人配不同的聽的人時不重算。掃描結束一定要呼叫 <see cref="EndScanAbsentCache"/>。</summary>
        internal static void BeginScanAbsentCache(IEnumerable<string> situationIds)
        {
            _scanAbsentCache = new Dictionary<string, (Hero?, string?)>(StringComparer.Ordinal);
            _scanAbsentCacheSituations = new HashSet<string>(situationIds, StringComparer.Ordinal);
            ScanAbsentCacheHits = 0;
            ScanAbsentCacheMisses = 0;
        }

        internal static void EndScanAbsentCache()
        {
            _scanAbsentCache = null;
            _scanAbsentCacheSituations = null;
        }

        internal static SituationEvaluation Evaluate(
            SituationTemplate template,
            IReadOnlyDictionary<string, Hero> nonDerivedHeroes,
            double day,
            IHeroTraitLookup? traitLookup,
            ISituationHistory? history,
            IDictionary<string, SituationRoleFacts>? heroFactsCache = null,
            HeroLookup? heroLookup = null,
            WorldEventStore? eventStore = null,
            VividWorldConfig? config = null,
            SituationWorldContext? context = null,
            bool forced = false,
            IReadOnlyDictionary<string, Hero>? preBoundDerivedHeroes = null)
        {
            if (template == null) throw new ArgumentNullException(nameof(template));
            if (nonDerivedHeroes == null) throw new ArgumentNullException(nameof(nonDerivedHeroes));

            var evaluation = new SituationEvaluation();
            evaluation.History = history;

            long campaignSeed = eventStore?.CampaignSeed ?? 0;
            string? playerHeroId = Hero.MainHero?.StringId;
            int grudgeLine = config?.FalseRumors?.GrudgeLine ?? -5;
            int nativeGrudgeLine = config?.FalseRumors?.NativeGrudgeLine ?? -20;

            context ??= new SituationWorldContext
            {
                Traits = traitLookup ?? eventStore?.Traits,
                GrudgeSum = (a, b) => eventStore?.Grudges?.Between(a, b, GrudgeScope.Personal)?.Sum(e => e.Delta) ?? 0,
                Affection = (a, b) =>
                {
                    var ha = heroLookup?.Get(a) ?? Hero.Find(a);
                    var hb = heroLookup?.Get(b) ?? Hero.Find(b);
                    if (ha == null || hb == null || ha == hb) return null;
                    var player = Hero.MainHero;
                    if (player != null && (ha == player || hb == player) && !SubModule.PersonalWithPlayerEnabled)
                    {
                        return ha.GetRelation(hb);
                    }
                    return ha.GetBaseHeroRelation(hb);
                },
                FindRememberedEventAbout = (roleHeroId, eventTypes, aboutHeroId) =>
                {
                    if (eventStore?.Index?.Entries == null) return null;
                    var memConfig = config?.Memory;
                    foreach (var entry in eventStore.Index.Entries)
                    {
                        if (entry == null || entry.Dormant) continue;
                        if (eventTypes != null && eventTypes.Count > 0 && !eventTypes.Any(t => string.Equals(t, entry.Type, StringComparison.OrdinalIgnoreCase)))
                        {
                            continue;
                        }
                        if (entry.ParticipantHeroIds == null || !entry.ParticipantHeroIds.Contains(aboutHeroId))
                        {
                            continue;
                        }
                        if (entry.KnownByHeroIds == null || !entry.KnownByHeroIds.Contains(roleHeroId))
                        {
                            continue;
                        }
                        if (!IndexMemory.Remembers(entry, roleHeroId, day, memConfig))
                        {
                            continue;
                        }
                        return entry.EventId;
                    }
                    return null;
                },
                // 只用上面那個查法：它同時給得出是哪一則，日誌才印得出事件 id
                RemembersEventAbout = null,
                Rng = new SplitMix64Rng(),
                Seed = campaignSeed,
                GrudgeLine = grudgeLine,
                NativeGrudgeLine = nativeGrudgeLine,
                PlayerHeroId = playerHeroId,
                Config = config
            };
            if (context.Config == null) context.Config = config;
            evaluation.Context = context;

            // 1. Resolve settlement from non-derived heroes
            Settlement? settlement = null;
            foreach (var h in nonDerivedHeroes.Values)
            {
                if (h != null && h.CurrentSettlement != null)
                {
                    settlement = h.CurrentSettlement;
                    break;
                }
            }
            evaluation.Settlement = settlement;
            evaluation.SettlementId = settlement?.StringId ?? "none";

            // 2. Read facts for non-derived roles first
            var allHeroes = new Dictionary<string, Hero?>(StringComparer.OrdinalIgnoreCase);
            var unboundReasons = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var boundHeroIds = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
            var factsByRole = new Dictionary<string, SituationRoleFacts>(StringComparer.OrdinalIgnoreCase);

            foreach (var kvp in nonDerivedHeroes)
            {
                allHeroes[kvp.Key] = kvp.Value;
                boundHeroIds[kvp.Key] = kvp.Value?.StringId;
                if (kvp.Value != null)
                {
                    if (heroFactsCache != null && heroFactsCache.TryGetValue(kvp.Value.StringId, out var cached))
                    {
                        factsByRole[kvp.Key] = cached;
                    }
                    else
                    {
                        var facts = SituationFactsReader.Read(kvp.Value);
                        if (heroFactsCache != null) heroFactsCache[kvp.Value.StringId] = facts;
                        factsByRole[kvp.Key] = facts;
                    }
                }
                else
                {
                    factsByRole[kvp.Key] = SituationFactsReader.Read(null);
                }
            }

            // 3. Resolve derived roles
            List<SituationRoleFacts>? absentCandidateFacts = null;
            Dictionary<string, Hero>? absentCandidateHeroes = null;

            foreach (var roleKvp in template.Roles)
            {
                string roleName = roleKvp.Key;
                var roleDef = roleKvp.Value;
                if (!roleDef.IsDerived) continue;

                Hero? derivedHero = null;
                string? failureReason = null;

                if (preBoundDerivedHeroes != null && preBoundDerivedHeroes.TryGetValue(roleName, out var preHero) && preHero != null)
                {
                    derivedHero = preHero;
                }
                else
                {
                    (derivedHero, failureReason) = ResolveDerivedRole(
                        roleName,
                        roleDef.Derived,
                        settlement,
                        allHeroes,
                        boundHeroIds,
                        factsByRole,
                        traitLookup,
                        heroLookup,
                        heroFactsCache,
                        context,
                        template.Id,
                        day,
                        ref absentCandidateFacts,
                        ref absentCandidateHeroes,
                        roleDef);
                }

                if (derivedHero != null)
                {
                    allHeroes[roleName] = derivedHero;
                    boundHeroIds[roleName] = derivedHero.StringId;
                    if (heroFactsCache != null && heroFactsCache.TryGetValue(derivedHero.StringId, out var cachedD))
                    {
                        factsByRole[roleName] = cachedD;
                    }
                    else
                    {
                        var facts = SituationFactsReader.Read(derivedHero);
                        if (heroFactsCache != null) heroFactsCache[derivedHero.StringId] = facts;
                        factsByRole[roleName] = facts;
                    }
                }
                else
                {
                    allHeroes[roleName] = null;
                    boundHeroIds[roleName] = null;
                    factsByRole[roleName] = SituationFactsReader.Read(null);
                    unboundReasons[roleName] = failureReason ?? "unbound derived role";
                }
            }

            evaluation.AllHeroes = allHeroes;
            evaluation.BoundHeroIds = boundHeroIds;
            evaluation.UnboundReasons = unboundReasons;
            evaluation.FactsByRole = factsByRole;

            // 4. Evaluate all conditions
            var nonDerivedHeroIds = nonDerivedHeroes.Values
                .Where(h => h != null && !string.IsNullOrEmpty(h.StringId))
                .Select(h => h.StringId)
                .ToList();
            evaluation.NonDerivedHeroIds = nonDerivedHeroIds;

            var conditionResults = SituationConditionEvaluator.EvaluateAll(
                template.Conditions,
                factsByRole,
                boundHeroIds,
                unboundReasons,
                template.Id,
                day,
                history,
                nonDerivedHeroIds,
                context,
                forced).ToList();

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
            HeroLookup? heroLookup = null,
            string? triggerEventId = null,
            bool forced = false)
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

            if (!template.IsEnabled(config))
            {
                result.Started = false;
                result.AbortReason = "disabled by falseRumors.enabled";
                return result;
            }

            if (string.Equals(triggerSource, "daily scan", StringComparison.OrdinalIgnoreCase))
            {
                ScanTriggeredThisSession++;
            }
            else if (triggerSource.StartsWith("after ", StringComparison.OrdinalIgnoreCase))
            {
                AfterEventTriggeredThisSession++;
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
                branchSeed,
                context: evaluation.Context,
                situationId: template.Id,
                day: day,
                history: evaluation.History,
                nonDerivedHeroIds: evaluation.NonDerivedHeroIds,
                forced: forced);

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
            if (selectedBranch.MadeUpTalk != null)
            {
                ExecuteMadeUpTalk(
                    template,
                    selectedBranch,
                    evaluation,
                    day,
                    instanceId,
                    eventStore,
                    config,
                    heroLookup,
                    result);
                return result;
            }

            if (selectedBranch.Events.Count == 0)
            {
                ModLog.Info($"  branch {selectedBranch.Id}: no event (by design)");
                result.Started = true;
                result.SelectedBranchId = selectedBranch.Id;
                return result;
            }

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

                string? linkedEventId = null;
                if (string.Equals(bEvent.LinkTo, "@trigger", StringComparison.OrdinalIgnoreCase))
                {
                    linkedEventId = triggerEventId;
                }

                var submission = TemplateBinder.Bind(eventTemplate, bindings, day, linkedEventId, out var bindIssues);
                if (submission == null)
                {
                    string issueDetails = string.Join("; ", bindIssues.Select(iss => $"{iss.Field}: {iss.Detail}"));
                    ModLog.Warn($"  SituationRunner: Failed to bind template '{bEvent.Type}': {issueDetails}");
                    continue;
                }

                if (eventTemplate.MadeUpBy != null && config.FalseRumors.Enabled)
                {
                    string boundHeroId = string.Empty;
                    if (boundHeroIds.TryGetValue(eventTemplate.MadeUpBy, out var mhId) && !string.IsNullOrEmpty(mhId))
                    {
                        boundHeroId = mhId!;
                    }
                    else if (submission.Participants.TryGetValue(eventTemplate.MadeUpBy, out var phId) && !string.IsNullOrEmpty(phId))
                    {
                        boundHeroId = phId!;
                    }

                    submission.Fabricated = true;
                    submission.OriginatorHeroId = boundHeroId;
                    submission.MadeUpBy = eventTemplate.MadeUpBy;
                    foreach (var f in submission.Facts)
                    {
                        f.IsFabricated = true;
                    }
                }

                // triggerCaptorArmy witness injection
                if (string.Equals(eventTemplate.WitnessSource, "triggerCaptorArmy", StringComparison.OrdinalIgnoreCase))
                {
                    var triggerEvent = !string.IsNullOrEmpty(triggerEventId) && eventStore != null ? eventStore.Load(triggerEventId!) : null;
                    var armyLeaders = triggerEvent?.CaptorArmyLeaderHeroIds;
                    if (armyLeaders != null && armyLeaders.Count > 0)
                    {
                        string playerHeroId = Hero.MainHero?.StringId ?? "player";
                        var participantHeroIds = new HashSet<string>(submission.Participants.Values.Where(v => !string.IsNullOrEmpty(v)), StringComparer.Ordinal);
                        var addedWitnesses = new List<string>();
                        foreach (var leaderId in armyLeaders)
                        {
                            if (string.IsNullOrEmpty(leaderId)) continue;
                            if (participantHeroIds.Contains(leaderId)) continue;
                            if (string.Equals(leaderId, playerHeroId, StringComparison.Ordinal)) continue;
                            var h = heroLookup?.Get(leaderId) ?? Hero.Find(leaderId);
                            if (h == null || !h.IsAlive) continue;

                            if (!submission.InitialKnowerHeroIds.Contains(leaderId))
                            {
                                submission.InitialKnowerHeroIds.Add(leaderId);
                                addedWitnesses.Add(leaderId);
                            }
                        }
                        if (addedWitnesses.Count > 0)
                        {
                            ModLog.Info($"  army witnesses: {addedWitnesses.Count} added [{string.Join(", ", addedWitnesses)}]");
                        }
                        else
                        {
                            ModLog.Info($"  army witnesses: 0 added (all {armyLeaders.Count} leader(s) were participants, the player, or dead)");
                        }
                    }
                    else
                    {
                        ModLog.Info("  army witnesses: 0 (captor was not in an army)");
                    }
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
            HeroLookup? heroLookup = null,
            bool forced = false,
            IReadOnlyDictionary<string, Hero>? preBoundDerivedHeroes = null)
        {
            if (template == null) throw new ArgumentNullException(nameof(template));
            if (nonDerivedHeroes == null) throw new ArgumentNullException(nameof(nonDerivedHeroes));
            config ??= new VividWorldConfig();

            ISituationHistory? history = eventStore?.Index?.Entries != null
                ? new SituationHistoryIndex(eventStore.Index.Entries, day)
                : null;

            var evaluation = Evaluate(
                template,
                nonDerivedHeroes,
                day,
                traitLookup,
                history,
                heroFactsCache: null,
                heroLookup: heroLookup,
                eventStore: eventStore,
                config: config,
                context: null,
                forced: forced,
                preBoundDerivedHeroes: preBoundDerivedHeroes);

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

            return Execute(template, evaluation, day, triggerSource, eventStore, traitLookup, config, heroLookup, forced: forced);
        }

        private static (Hero? Hero, string? FailureReason) ResolveDerivedRole(
            string roleName,
            string? derivedStrategy,
            Settlement? settlement,
            Dictionary<string, Hero?> allHeroes,
            Dictionary<string, string?> boundHeroIds,
            IReadOnlyDictionary<string, SituationRoleFacts> factsByRole,
            IHeroTraitLookup? traitLookup,
            HeroLookup? heroLookup,
            IDictionary<string, SituationRoleFacts>? heroFactsCache,
            SituationWorldContext? context,
            string situationId,
            double day,
            ref List<SituationRoleFacts>? absentCandidateFacts,
            ref Dictionary<string, Hero>? absentCandidateHeroes,
            SituationRoleDef? roleDef = null)
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

                foreach (var kvp in allHeroes)
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

            var parsed = AbsentRoleSelector.ParseStrategy(derivedStrategy);
            if (parsed != null)
            {
                string targetRole = parsed.Value.TargetRole;
                if (!allHeroes.TryGetValue(targetRole, out var targetHero) || targetHero == null)
                {
                    return (null, $"target role '{targetRole}' is unbound");
                }

                if (!factsByRole.TryGetValue(targetRole, out var targetFacts) || targetFacts == null)
                {
                    targetFacts = SituationFactsReader.Read(targetHero);
                }

                string? absentCacheKey = null;
                if (_scanAbsentCache != null && _scanAbsentCacheSituations != null && _scanAbsentCacheSituations.Contains(situationId))
                {
                    absentCacheKey = situationId + "|" + roleName + "|" + targetHero.StringId;
                    if (_scanAbsentCache.TryGetValue(absentCacheKey, out var cachedAbsent))
                    {
                        ScanAbsentCacheHits++;
                        return cachedAbsent;
                    }
                    ScanAbsentCacheMisses++;
                }

                if (absentCandidateFacts == null)
                {
                    absentCandidateFacts = new List<SituationRoleFacts>();
                    absentCandidateHeroes = new Dictionary<string, Hero>(StringComparer.Ordinal);
                    var pool = heroLookup?.AllAlive ?? (IEnumerable<Hero>?)Hero.AllAliveHeroes ?? Array.Empty<Hero>();
                    string? pId = Hero.MainHero?.StringId;
                    foreach (var h in pool)
                    {
                        if (h == null || string.IsNullOrEmpty(h.StringId)) continue;
                        if (h == Hero.MainHero || (!string.IsNullOrEmpty(pId) && h.StringId == pId)) continue;
                        if (!h.IsAlive || !h.IsLord) continue;

                        absentCandidateHeroes[h.StringId] = h;
                        if (heroFactsCache != null && heroFactsCache.TryGetValue(h.StringId, out var cachedF))
                        {
                            absentCandidateFacts.Add(cachedF);
                        }
                        else
                        {
                            var f = SituationFactsReader.Read(h);
                            if (heroFactsCache != null) heroFactsCache[h.StringId] = f;
                            absentCandidateFacts.Add(f);
                        }
                    }
                }

                var selResult = AbsentRoleSelector.Select(
                    derivedRoleName: roleName,
                    derivedStrategy: derivedStrategy ?? string.Empty,
                    targetRoleFacts: targetFacts,
                    currentSettlementId: settlement?.StringId,
                    candidatePool: absentCandidateFacts,
                    alreadyBoundHeroes: boundHeroIds,
                    context: context,
                    situationId: situationId,
                    day: day,
                    roleDef: roleDef);

                if (selResult.IsBound && selResult.PickedHeroId != null)
                {
                    Hero? pickedHero = null;
                    absentCandidateHeroes?.TryGetValue(selResult.PickedHeroId, out pickedHero);
                    pickedHero ??= heroLookup?.Get(selResult.PickedHeroId) ?? Hero.Find(selResult.PickedHeroId);
                    if (absentCacheKey != null) _scanAbsentCache![absentCacheKey] = (pickedHero, null);
                    return (pickedHero, null);
                }

                var unbound = (Hero?)null;
                string unboundWhy = selResult.UnboundReason ?? "no candidates";
                if (absentCacheKey != null) _scanAbsentCache![absentCacheKey] = (unbound, unboundWhy);
                return (unbound, unboundWhy);
            }

            return (null, $"unknown derived strategy '{derivedStrategy}'");
        }

        internal static void HandleAfterEvent(
            string eventType,
            IReadOnlyDictionary<string, string> eventBindings,
            string eventId,
            double day,
            WorldEventStore? eventStore,
            IHeroTraitLookup? traitLookup,
            VividWorldConfig config,
            HeroLookup? heroLookup)
        {
            if (string.IsNullOrEmpty(eventType) || eventBindings == null || string.IsNullOrEmpty(eventId)) return;
            var catalog = SituationCatalogStore.Catalog;
            if (catalog?.Situations == null || catalog.Situations.Count == 0) return;

            var matchingSituations = catalog.Situations
                .Where(t => t != null
                            && string.Equals(t.Trigger, "afterEvent", StringComparison.OrdinalIgnoreCase)
                            && t.EventTypes != null
                            && t.EventTypes.Any(et => string.Equals(et, eventType, StringComparison.OrdinalIgnoreCase)))
                .ToList();

            if (matchingSituations.Count == 0) return;

            ISituationHistory? history = eventStore?.Index?.Entries != null
                ? new SituationHistoryIndex(eventStore.Index.Entries, day)
                : null;

            var results = new List<string>();

            foreach (var template in matchingSituations)
            {
                if (!template.IsEnabled(config))
                {
                    results.Add($"{template.Id}: skipped (disabled by falseRumors.enabled)");
                    continue;
                }

                if (template.BindFromEvent == null || template.BindFromEvent.Count == 0)
                {
                    results.Add($"{template.Id}: skipped (no bindFromEvent defined)");
                    continue;
                }

                var nonDerivedRoles = template.Roles
                    .Where(r => !r.Value.IsDerived)
                    .Select(r => r.Key)
                    .ToList();

                var roleHeroes = new Dictionary<string, Hero>(StringComparer.OrdinalIgnoreCase);
                string? bindFailure = null;

                foreach (var roleName in nonDerivedRoles)
                {
                    template.Roles.TryGetValue(roleName, out var roleDef);
                    if (!template.BindFromEvent.TryGetValue(roleName, out var placeholder) || string.IsNullOrEmpty(placeholder))
                    {
                        bindFailure = $"role '{roleName}' not in bindFromEvent";
                        break;
                    }

                    string? heroId = null;
                    foreach (var kvp in eventBindings)
                    {
                        if (string.Equals(kvp.Key, placeholder, StringComparison.OrdinalIgnoreCase))
                        {
                            heroId = kvp.Value;
                            break;
                        }
                    }

                    if (string.IsNullOrEmpty(heroId))
                    {
                        bindFailure = $"role '{roleName}' ({placeholder}) unbound: not found in event bindings";
                        break;
                    }

                    string playerHeroId = Hero.MainHero?.StringId ?? "player";
                    if (string.Equals(heroId, playerHeroId, StringComparison.Ordinal))
                    {
                        ModLog.Info($"SituationRunner: role '{roleName}' is the player");
                        bindFailure = $"role '{roleName}' is the player";
                        break;
                    }

                    var hero = heroLookup?.Get(heroId!) ?? Hero.Find(heroId!);
                    bool allowDead = roleDef?.AllowDead == true;
                    if (hero == null || (!allowDead && !hero.IsAlive))
                    {
                        bindFailure = $"role '{roleName}' ({placeholder}) unbound: hero '{heroId}' null or dead";
                        break;
                    }

                    roleHeroes[roleName] = hero;
                }

                if (bindFailure != null)
                {
                    results.Add($"{template.Id}: skipped ({bindFailure})");
                    continue;
                }

                var eval = Evaluate(
                    template,
                    roleHeroes,
                    day,
                    traitLookup,
                    history,
                    heroFactsCache: null,
                    heroLookup: heroLookup,
                    eventStore: eventStore,
                    config: config);

                string? sId = null;
                foreach (var kvp in eventBindings)
                {
                    if (string.Equals(kvp.Key, "SETTLEMENT", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrEmpty(kvp.Value))
                    {
                        sId = kvp.Value;
                        break;
                    }
                }
                if (!string.IsNullOrEmpty(sId))
                {
                    var sObj = Settlement.Find(sId);
                    if (sObj != null)
                    {
                        eval.Settlement = sObj;
                        eval.SettlementId = sId!;
                        ModLog.Info($"SituationRunner: @settlement '{sId}' resolved from trigger event");
                    }
                }

                if (!eval.ConditionsPassed)
                {
                    var firstFailed = eval.ConditionResults.FirstOrDefault(c => !c.Ok);
                    string failDetail = firstFailed != null ? $"{(!string.IsNullOrEmpty(firstFailed.Label) ? firstFailed.Label : firstFailed.Type)}: {firstFailed.Detail}" : "conditions failed";
                    results.Add($"{template.Id}: conditions failed ({failDetail})");
                    continue;
                }

                var runResult = Execute(
                    template,
                    eval,
                    day,
                    $"after {eventType}",
                    eventStore,
                    traitLookup,
                    config,
                    heroLookup,
                    triggerEventId: eventId);

                if (runResult.Started)
                {
                    string branch = runResult.SelectedBranchId ?? "none";
                    string submitted = runResult.EventIds.Count > 0 ? string.Join(",", runResult.EventIds) : "none";
                    results.Add($"{template.Id}: executed (branch {branch}, submitted {submitted})");
                }
                else
                {
                    results.Add($"{template.Id}: aborted ({runResult.AbortReason ?? "unknown"})");
                }
            }

            ModLog.Info($"afterEvent for {eventId} ({eventType}): evaluated {matchingSituations.Count} situation(s) - {string.Join("; ", results)}");
        }

        public static SituationRunResult RunAfterEventForced(
            string situationId,
            WorldEvent triggerEvent,
            double day,
            WorldEventStore? eventStore,
            IHeroTraitLookup? traitLookup,
            VividWorldConfig config,
            HeroLookup? heroLookup = null,
            IReadOnlyDictionary<string, string?>? preBoundDerivedHeroes = null)
        {
            if (string.IsNullOrEmpty(situationId)) throw new ArgumentNullException(nameof(situationId));
            if (triggerEvent == null) throw new ArgumentNullException(nameof(triggerEvent));
            config ??= new VividWorldConfig();
            heroLookup ??= new HeroLookup();

            var template = SituationCatalogStore.Catalog?.Situations.FirstOrDefault(s => string.Equals(s.Id, situationId, StringComparison.OrdinalIgnoreCase));
            if (template == null)
            {
                return new SituationRunResult { Started = false, AbortReason = $"situation '{situationId}' not found in catalog" };
            }

            if (!template.IsEnabled(config))
            {
                ModLog.Info($"SituationRunner: {template.Id} forced run skipped (disabled by falseRumors.enabled)");
                return new SituationRunResult { Started = false, AbortReason = "disabled by falseRumors.enabled" };
            }

            var eventBindings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (triggerEvent.Participants != null)
            {
                foreach (var kvp in triggerEvent.Participants)
                {
                    eventBindings[kvp.Key] = kvp.Value;
                }
            }
            if (triggerEvent.Facts != null)
            {
                foreach (var fact in triggerEvent.Facts)
                {
                    if (fact.Vars != null)
                    {
                        foreach (var kvp in fact.Vars)
                        {
                            string val = kvp.Value;
                            int colonIdx = val.IndexOf(':');
                            if (colonIdx >= 0)
                            {
                                val = val.Substring(colonIdx + 1);
                            }
                            if (!eventBindings.ContainsKey(kvp.Key))
                            {
                                eventBindings[kvp.Key] = val;
                            }
                        }
                    }
                }
            }

            var nonDerivedRoles = template.Roles
                .Where(r => !r.Value.IsDerived)
                .Select(r => r.Key)
                .ToList();

            var roleHeroes = new Dictionary<string, Hero>(StringComparer.OrdinalIgnoreCase);
            foreach (var roleName in nonDerivedRoles)
            {
                template.Roles.TryGetValue(roleName, out var roleDef);
                if (template.BindFromEvent == null || !template.BindFromEvent.TryGetValue(roleName, out var placeholder) || string.IsNullOrEmpty(placeholder))
                {
                    return new SituationRunResult { Started = false, AbortReason = $"role '{roleName}' not in bindFromEvent" };
                }

                string? heroId = null;
                foreach (var kvp in eventBindings)
                {
                    if (string.Equals(kvp.Key, placeholder, StringComparison.OrdinalIgnoreCase))
                    {
                        heroId = kvp.Value;
                        break;
                    }
                }

                if (string.IsNullOrEmpty(heroId))
                {
                    return new SituationRunResult { Started = false, AbortReason = $"role '{roleName}' ({placeholder}) unbound: not found in event bindings" };
                }

                string playerHeroId = Hero.MainHero?.StringId ?? "player";
                if (string.Equals(heroId, playerHeroId, StringComparison.Ordinal))
                {
                    ModLog.Info($"SituationRunner: role '{roleName}' is the player");
                    return new SituationRunResult { Started = false, AbortReason = $"role '{roleName}' is the player" };
                }

                var hero = heroLookup.Get(heroId!) ?? Hero.Find(heroId!);
                bool allowDead = roleDef?.AllowDead == true;
                if (hero == null || (!allowDead && !hero.IsAlive))
                {
                    return new SituationRunResult { Started = false, AbortReason = $"role '{roleName}' ({placeholder}) unbound: hero '{heroId}' null or dead" };
                }

                roleHeroes[roleName] = hero;
            }

            ISituationHistory? history = eventStore?.Index?.Entries != null
                ? new SituationHistoryIndex(eventStore.Index.Entries, day)
                : null;

            var preBoundHeroMap = new Dictionary<string, Hero>(StringComparer.OrdinalIgnoreCase);
            if (preBoundDerivedHeroes != null)
            {
                foreach (var kvp in preBoundDerivedHeroes)
                {
                    string? val = kvp.Value;
                    if (!string.IsNullOrEmpty(val))
                    {
                        var h = heroLookup.Get(val!) ?? Hero.Find(val!);
                        if (h != null)
                        {
                            preBoundHeroMap[kvp.Key] = h;
                        }
                    }
                }
            }

            var eval = Evaluate(
                template,
                roleHeroes,
                day,
                traitLookup,
                history,
                heroFactsCache: null,
                heroLookup: heroLookup,
                eventStore: eventStore,
                config: config,
                context: null,
                forced: true,
                preBoundDerivedHeroes: preBoundHeroMap.Count > 0 ? preBoundHeroMap : null);

            string? sId = null;
            foreach (var kvp in eventBindings)
            {
                if (string.Equals(kvp.Key, "SETTLEMENT", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrEmpty(kvp.Value))
                {
                    sId = kvp.Value;
                    break;
                }
            }
            if (!string.IsNullOrEmpty(sId))
            {
                var sObj = Settlement.Find(sId);
                if (sObj != null)
                {
                    eval.Settlement = sObj;
                    eval.SettlementId = sId!;
                    ModLog.Info($"SituationRunner: @settlement '{sId}' resolved from trigger event");
                }
            }

            if (!eval.ConditionsPassed)
            {
                var firstFailed = eval.ConditionResults.FirstOrDefault(c => !c.Ok);
                string failDetail = firstFailed != null ? $"{(!string.IsNullOrEmpty(firstFailed.Label) ? firstFailed.Label : firstFailed.Type)}: {firstFailed.Detail}" : "conditions failed";
                return new SituationRunResult
                {
                    Started = false,
                    ConditionsPassed = false,
                    ConditionResults = eval.ConditionResults,
                    AbortReason = failDetail
                };
            }

            return Execute(
                template,
                eval,
                day,
                "forced by dev",
                eventStore,
                traitLookup,
                config,
                heroLookup,
                triggerEventId: triggerEvent.EventId,
                forced: true);
        }

        private static void ExecuteMadeUpTalk(
            SituationTemplate template,
            SituationBranchDef selectedBranch,
            SituationEvaluation evaluation,
            double day,
            long instanceId,
            WorldEventStore? eventStore,
            VividWorldConfig config,
            HeroLookup? heroLookup,
            SituationRunResult result)
        {
            var mut = selectedBranch.MadeUpTalk;
            if (mut == null) return;

            string tellerRole = !string.IsNullOrEmpty(mut.Teller) ? mut.Teller : "teller";
            string listenerRole = !string.IsNullOrEmpty(mut.Listener) ? mut.Listener : "listener";
            string targetRole = !string.IsNullOrEmpty(mut.Target) ? mut.Target : "target";

            evaluation.BoundHeroIds.TryGetValue(tellerRole, out var tellerId);
            evaluation.BoundHeroIds.TryGetValue(listenerRole, out var listenerId);
            evaluation.BoundHeroIds.TryGetValue(targetRole, out var targetId);

            if (string.IsNullOrEmpty(tellerId) || string.IsNullOrEmpty(listenerId) || string.IsNullOrEmpty(targetId))
            {
                ModLog.Info($"  made-up talk: missing roles for {mut.Kind} (teller={tellerId}, listener={listenerId}, target={targetId})");
                result.AbortReason = "missing roles";
                return;
            }

            if (!evaluation.FactsByRole.TryGetValue(tellerRole, out var tellerFacts) || tellerFacts == null ||
                !evaluation.FactsByRole.TryGetValue(listenerRole, out var listenerFacts) || listenerFacts == null ||
                !evaluation.FactsByRole.TryGetValue(targetRole, out var targetFacts) || targetFacts == null)
            {
                ModLog.Info($"  made-up talk: missing facts for {mut.Kind}");
                result.AbortReason = "missing facts";
                return;
            }

            var traitLookup = evaluation.Context?.Traits ?? eventStore?.Traits;
            var tellerTraits = !string.IsNullOrEmpty(tellerId) ? traitLookup?.Of(tellerId!) : null;
            var targetTraits = !string.IsNullOrEmpty(targetId) ? traitLookup?.Of(targetId!) : null;

            var allAlive = heroLookup?.AllAlive ?? (IEnumerable<Hero>?)Hero.AllAliveHeroes ?? Array.Empty<Hero>();
            var candidateFacts = new List<SituationRoleFacts>();
            foreach (var h in allAlive)
            {
                if (h == null || !h.IsAlive || !h.IsLord || h.IsHumanPlayerCharacter) continue;
                candidateFacts.Add(SituationFactsReader.Read(h));
            }

            // 說的人還記得的真事：型別對、（被抓的兩種）參與者含被說的人，日子新到舊最多看 20 則
            var rememberedList = new List<MadeUpTalkCandidateRealEvent>();
            string playerHeroId = Hero.MainHero?.StringId ?? "player";
            if (eventStore?.Index?.Entries != null)
            {
                var validEntries = eventStore.Index.Entries
                    .Where(e => e != null && !e.Dormant
                        && e.KnownByHeroIds != null && e.KnownByHeroIds.Contains(tellerId!)
                        && (string.Equals(e.Type, "hero_taken_prisoner", StringComparison.Ordinal)
                            || string.Equals(e.Type, "hero_died_of_old_age", StringComparison.Ordinal)
                            || string.Equals(e.Type, "hero_died_naturally", StringComparison.Ordinal))
                        && (!string.Equals(e.Type, "hero_taken_prisoner", StringComparison.Ordinal)
                            || (e.ParticipantHeroIds != null && e.ParticipantHeroIds.Contains(targetId!)))
                        && (e.ParticipantHeroIds == null || !e.ParticipantHeroIds.Contains(playerHeroId))
                        && IndexMemory.Remembers(e, tellerId!, day, config?.Memory))
                    .OrderByDescending(e => e.Day)
                    .Take(20)
                    .ToList();

                foreach (var entry in validEntries)
                {
                    var evt = eventStore.Load(entry.EventId);
                    if (evt == null) continue;
                    var participantsDict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    if (evt.Participants != null)
                    {
                        foreach (var kvp in evt.Participants) participantsDict[kvp.Key] = kvp.Value;
                    }

                    var alreadyLinked = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    foreach (var other in eventStore.Index.Entries)
                    {
                        if (other != null && string.Equals(other.LinkedEventId, entry.EventId, StringComparison.OrdinalIgnoreCase))
                        {
                            alreadyLinked.Add(other.Type);
                        }
                    }

                    string? eventSettlementId = null;
                    if (evt.Facts != null)
                    {
                        foreach (var f in evt.Facts)
                        {
                            if (f.Vars != null && f.Vars.TryGetValue("SETTLEMENT", out var sRef) && !string.IsNullOrEmpty(sRef))
                            {
                                eventSettlementId = sRef.StartsWith("settlement:", StringComparison.OrdinalIgnoreCase)
                                    ? sRef.Substring("settlement:".Length)
                                    : sRef;
                                break;
                            }
                        }
                    }

                    string? victimKingdomId = null;
                    if (participantsDict.TryGetValue("victim", out var victimHeroId) || participantsDict.TryGetValue("dead", out victimHeroId))
                    {
                        var victimHero = heroLookup?.Get(victimHeroId) ?? Hero.Find(victimHeroId);
                        victimKingdomId = victimHero?.Clan?.Kingdom?.StringId;
                    }

                    rememberedList.Add(new MadeUpTalkCandidateRealEvent
                    {
                        EventId = entry.EventId,
                        Type = entry.Type,
                        Day = entry.Day,
                        SettlementId = eventSettlementId,
                        Participants = participantsDict,
                        AlreadyLinkedTypes = alreadyLinked,
                        VictimKingdomId = victimKingdomId
                    });
                }
            }

            // 怨的來源（只有惡意中傷與爭權中傷用得上；好話沒有）
            if (string.Equals(mut.Kind, "slander", StringComparison.OrdinalIgnoreCase))
            {
                double personalGrudge = evaluation.Context?.GrudgeSum != null ? evaluation.Context.GrudgeSum(tellerId!, targetId!) : 0;
                int? gameRelation = evaluation.Context?.Affection != null ? evaluation.Context.Affection(tellerId!, targetId!) : null;
                ModLog.Info($"  made-up talk: cause={template.Id}, teller={tellerId}, listener={listenerId}, target={targetId}; grudge of teller against target: our personal grudge sum {personalGrudge:0.##}, game relation {(gameRelation.HasValue ? gameRelation.Value.ToString() : "unknown")}");
            }
            else
            {
                ModLog.Info($"  made-up talk: cause={template.Id}, teller={tellerId}, listener={listenerId}, target={targetId}; praise (no grudge involved)");
            }

            Func<string, string, bool> hasGrudgeRecord = (a, b) =>
                eventStore?.Grudges != null &&
                (eventStore.Grudges.Between(a, b, GrudgeScope.Personal).Count > 0
                 || eventStore.Grudges.Between(b, a, GrudgeScope.Personal).Count > 0
                 || eventStore.Grudges.Between(a, b, GrudgeScope.Clan).Count > 0
                 || eventStore.Grudges.Between(b, a, GrudgeScope.Clan).Count > 0);
            long campaignSeed = eventStore?.CampaignSeed ?? 0;
            var rng = new SplitMix64Rng();

            var plan = MadeUpTalkPlanner.Plan(
                mut.Kind,
                tellerFacts,
                listenerFacts,
                targetFacts,
                tellerTraits,
                targetTraits,
                string.Equals(evaluation.SettlementId, "none", StringComparison.Ordinal) ? string.Empty : evaluation.SettlementId,
                candidateFacts,
                rememberedList,
                hasGrudgeRecord,
                campaignSeed,
                instanceId,
                rng);

            if (!plan.Success || string.IsNullOrEmpty(plan.ContentType))
            {
                ModLog.Info($"  made-up talk: no valid target for {mut.Kind}, skipping");
                if (!string.IsNullOrEmpty(plan.Log))
                {
                    ModLog.Info(plan.Log);
                }
                result.AbortReason = "no valid content planned";
                return;
            }

            if (!string.IsNullOrEmpty(plan.Log))
            {
                ModLog.Info(plan.Log);
            }

            var situationEventCatalog = SituationCatalogStore.EventCatalog;
            var baseTemplate = situationEventCatalog?.ByType(plan.ContentType!);
            if (baseTemplate == null)
            {
                ModLog.Warn($"  SituationRunner: event template '{plan.ContentType}' not found in situation event catalog.");
                result.AbortReason = $"event template '{plan.ContentType}' not found";
                return;
            }

            var eventTemplate = TemplateVariants.GetMadeUpVariant(baseTemplate, plan.LinkedTemplateType);
            if (eventTemplate == null)
            {
                ModLog.Warn($"  SituationRunner: made-up variant for '{plan.ContentType}' is null");
                result.AbortReason = "variant is null";
                return;
            }

            var bindings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (!string.IsNullOrEmpty(plan.TargetRole) && !string.IsNullOrEmpty(plan.TargetHeroId))
            {
                bindings[plan.TargetRole!] = plan.TargetHeroId!;
            }
            if (!string.IsNullOrEmpty(plan.CounterpartRole) && !string.IsNullOrEmpty(plan.CounterpartHeroId))
            {
                bindings[plan.CounterpartRole!] = plan.CounterpartHeroId!;
            }
            string settlementVal = !string.IsNullOrEmpty(plan.SettlementId) ? plan.SettlementId! : string.Empty;
            if (!string.IsNullOrEmpty(settlementVal))
            {
                bindings["SETTLEMENT"] = settlementVal;
                bindings["settlement"] = settlementVal;
            }

            var submission = TemplateBinder.Bind(eventTemplate, bindings, day, plan.LinkedEventId, out var bindIssues);
            if (submission == null)
            {
                string issueDetails = string.Join("; ", bindIssues.Select(iss => $"{iss.Field}: {iss.Detail}"));
                ModLog.Warn($"  SituationRunner: Failed to bind made-up template '{eventTemplate.Type}': {issueDetails}");
                result.AbortReason = $"binding failed: {issueDetails}";
                return;
            }

            submission.SituationId = template.Id;
            submission.AutoResolveWitnesses = false;
            submission.Fabricated = true;
            submission.OriginatorHeroId = tellerId;
            submission.HearsayKnowerHeroIds = new List<string> { tellerId! };
            submission.RelayKnowers = new List<RelayKnower>
            {
                new RelayKnower(listenerId!, 2, tellerId)
            };
            foreach (var f in submission.Facts)
            {
                f.IsFabricated = true;
            }

            if (eventStore == null)
            {
                ModLog.Warn($"  SituationRunner: event store is null, cannot submit made-up '{eventTemplate.Type}'.");
                result.AbortReason = "event store is null";
                return;
            }

            // 同一天、同一類事件、同一組參與者已經有一則 ⇒ 不再產生
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
                result.AbortReason = "same event already exists today";
                ModLog.Info($"  made-up talk not produced: same event already exists today as {sameToday.EventId} (instance {instanceId.ToString(CultureInfo.InvariantCulture)})");
                return;
            }

            var ingestResult = eventStore.Submit(submission, out string? eventId, out string? rejectionReason);
            result.IngestResults.Add(ingestResult);

            if (ingestResult != IngestResult.Accepted || string.IsNullOrEmpty(eventId))
            {
                result.AbortReason = $"submit rejected: {ingestResult}";
                ModLog.Info($"  submit made-up {eventTemplate.Type} rejected: {ingestResult} - {rejectionReason}");
            }
            else
            {
                EventsSubmittedThisSession++;
                result.EventIds.Add(eventId!);
                string hangNote = string.IsNullOrEmpty(plan.LinkedEventId) ? "hangs on nothing" : $"hangs on {plan.LinkedEventId}";
                ModLog.Info($"  bound made-up {eventTemplate.Type} as {eventId} (cause={template.Id}, teller={tellerId}, listener={listenerId}, target={plan.TargetHeroId}, counterpart={plan.CounterpartHeroId}, {hangNote})");
                try
                {
                    Hop0SummaryLogger.LogHop0Summary(eventTemplate, submission, eventId!, eventStore, evaluation.Context?.Traits ?? eventStore.Traits);
                }
                catch (Exception ex)
                {
                    ModLog.Error($"SituationRunner: Failed to format hop0 summary for {eventId}", ex);
                }
            }
        }
    }
}
