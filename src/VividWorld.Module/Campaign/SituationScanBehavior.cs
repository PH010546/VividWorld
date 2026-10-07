using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Settlements;
using VividWorld.Core.Catalog;
using VividWorld.Core.Config;
using VividWorld.Core.Diagnostics;
using VividWorld.Core.Persistence;
using VividWorld.Core.Rumors;
using VividWorld.Core.Situations;
using VividWorld.Core.Util;
using VividWorld.Debug;

namespace VividWorld.Campaign
{
    internal sealed class SituationScanResult
    {
        public bool Disabled { get; set; }
        public int SettlementsWithPairs { get; set; }
        public int PairsEvaluated { get; set; }
        public int CandidatesPassed { get; set; }
        public int Quota { get; set; }
        public string? WouldTriggerSummary { get; set; }
        public string? TopBlockersSummary { get; set; }
        /// <summary>編的話的三個起因各有幾位符合條件的候選（試算與正式掃描都填；沒列舉的不在表裡）。</summary>
        public Dictionary<string, int> MadeUpTalkCandidateCounts { get; } = new Dictionary<string, int>(StringComparer.Ordinal);
    }

    internal sealed class SituationScanBehavior : CampaignBehaviorBase
    {
        private readonly VividWorldConfig _config;
        private WorldEventStore? _eventStore;
        private HeroLookup? _heroLookup;
        private IHeroTraitLookup? _traits;
        private bool _ready;

        public static double? LastScanDay { get; private set; }
        public static int LastScanPairsEvaluated { get; private set; }
        public static int LastScanCandidatesPassed { get; private set; }
        public static int LastScanTriggered { get; private set; }
        public static bool HasScanned => LastScanDay.HasValue;

        /// <summary>靜態的「上一次掃描」跨讀檔存活；每次 OnGameStart 歸零，換一局才不會報上一局的數字。
        /// （SE1 的 SituationRunner 出過同一類問題，見卡片自驗紀錄。）</summary>
        public static void ResetSessionState()
        {
            LastScanDay = null;
            LastScanPairsEvaluated = 0;
            LastScanCandidatesPassed = 0;
            LastScanTriggered = 0;
        }

        public SituationScanBehavior(VividWorldConfig config)
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));
        }

        public void Initialize(WorldEventStore eventStore, HeroLookup heroLookup, IHeroTraitLookup traits)
        {
            _eventStore = eventStore ?? throw new ArgumentNullException(nameof(eventStore));
            _heroLookup = heroLookup ?? throw new ArgumentNullException(nameof(heroLookup));
            _traits = traits ?? throw new ArgumentNullException(nameof(traits));
            _ready = true;
        }

        public override void RegisterEvents()
        {
            CampaignEvents.DailyTickEvent.AddNonSerializedListener(this, OnDailyTick);
            ModLog.Info("SituationScanBehavior.RegisterEvents: subscribed to DailyTickEvent.");
        }

        public override void SyncData(IDataStore dataStore)
        {
            // 空的。不得新增鍵
        }

        private void OnDailyTick()
        {
            using (DevMetrics.Measure("situationScan"))
            {
                try
                {
                    ExecuteScan(isDryRun: false);
                }
                catch (Exception ex)
                {
                    ModLog.Error("Error in SituationScanBehavior.OnDailyTick", ex);
                }
            }
        }

        public SituationScanResult ExecuteScan(bool isDryRun = false)
        {
            var result = new SituationScanResult();

            // 1. 沒 Initialize 過 ⇒ ModLog.Warn 一行就回
            if (!_ready || _eventStore == null || _heroLookup == null || _traits == null)
            {
                ModLog.Warn("SituationScan: daily tick before Initialize - skipped.");
                return result;
            }

            // 2. config.Situations.DailyScanEnabled == false ⇒ 印 disabled 行，回傳
            if (_config.Situations == null || !_config.Situations.DailyScanEnabled)
            {
                ModLog.Info(SituationScanLogFormatter.FormatDisabled());
                result.Disabled = true;
                return result;
            }

            double day = TaleWorlds.CampaignSystem.Campaign.Current != null
                ? CampaignTime.Now.ToDays
                : 0.0;
            long dayBucket = RumorSeed.DayBucket(day);
            long campaignSeed = _eventStore.CampaignSeed;
            var rng = new SplitMix64Rng();

            // 3. SituationQuota.Roll(...)
            long quotaSeed = RumorSeed.Of(campaignSeed, "situation.scan", dayBucket, "quota");
            var scanConfig = _config.Situations?.Scan ?? new SituationScanConfig();
            double maxPerDay = _config.Situations?.MaxPerDay ?? 1.5;
            var quotaRoll = SituationQuota.Roll(maxPerDay, rng, quotaSeed);
            result.Quota = quotaRoll.Quota;

            // 有自己每日上限的情境不佔這份額度：這份擲到 0 時，只有在沒有那種情境的情況下才整個收工
            bool hasOwnQuotaSituations = (SituationCatalogStore.Catalog?.Situations ?? new List<SituationTemplate>())
                .Any(t => t != null && !t.DevOnly && t.IsEnabled(_config) && (t.GetMaxPerDay(_config).HasValue || t.QuotaGroup != null)
                          && !string.Equals(t.Trigger, "afterEvent", StringComparison.OrdinalIgnoreCase));
            if (quotaRoll.Quota <= 0 && hasOwnQuotaSituations)
            {
                ModLog.Info(SituationScanLogFormatter.FormatQuotaZero(day, quotaRoll));
            }
            else if (quotaRoll.Quota <= 0)
            {
                ModLog.Info(SituationScanLogFormatter.FormatQuotaZero(day, quotaRoll));
                if (isDryRun)
                {
                    ModLog.Info(SituationScanLogFormatter.FormatDryRunEnd());
                }
                return result;
            }

            var sw = Stopwatch.StartNew();

            // 4. 建 SituationHistoryIndex 一次
            var history = new SituationHistoryIndex(_eventStore.Index?.Entries ?? new List<RumorIndexEntry>(), day);

            // 5. 列舉
            var situationCatalog = SituationCatalogStore.Catalog;
            var situationTemplates = situationCatalog?.Situations ?? new List<SituationTemplate>();
            var skippedDevOnlyIds = new List<string>();
            var skippedDisabledIds = new List<string>();
            var skippedAfterEventIds = new List<string>();
            var perSituationCandidates = new Dictionary<string, List<SituationCandidate>>(StringComparer.Ordinal);
            var settlementPairs = new List<(Settlement Settlement, List<(Hero A, Hero B)> Pairs)>();

            var allSettlements = Settlement.All;
            var eligibleSettlements = allSettlements != null
                ? allSettlements.Where(s => s != null && (s.IsTown || s.IsCastle || s.IsVillage)).ToList()
                : new List<Settlement>();

            int totalSettlements = eligibleSettlements.Count;
            int settlementsWithPairs = 0;
            int pairsEvaluated = 0;
            int maxPairsPerSettlement = scanConfig.MaxPairsPerSettlement;
            int maxCandidates = scanConfig.MaxCandidates;

            var candidates = new List<SituationCandidate>();
            var heroFactsCache = new Dictionary<string, SituationRoleFacts>(StringComparer.Ordinal);
            var rejections = new Dictionary<string, int>(StringComparer.Ordinal);
            var nearMisses = new List<(string SitId, string SetId, Dictionary<string, string> Roles, string Detail)>();
            int maxNearMisses = _config.Debug != null ? _config.Debug.DevReportMaxCrowdedOut : 5;
            bool truncated = false;
            string? mainHeroId = Hero.MainHero?.StringId;

            foreach (var settlement in eligibleSettlements)
            {
                var present = EligibilityLabel.GetPresentHeroesAtSettlement(settlement);
                var eligibleHeroes = present
                    .Where(h => h != null
                                && !string.IsNullOrEmpty(h.StringId)
                                && h != Hero.MainHero
                                && (string.IsNullOrEmpty(mainHeroId) || !string.Equals(h.StringId, mainHeroId, StringComparison.Ordinal))
                                && EligibilityLabel.GetRejectionReason(h, _traits) == null)
                    .OrderBy(h => h.StringId, StringComparer.Ordinal)
                    .ToList();

                if (eligibleHeroes.Count < 2)
                {
                    continue;
                }

                settlementsWithPairs++;

                var pairs = new List<(Hero A, Hero B)>();
                for (int i = 0; i < eligibleHeroes.Count; i++)
                {
                    for (int j = 0; j < eligibleHeroes.Count; j++)
                    {
                        if (i != j)
                        {
                            pairs.Add((eligibleHeroes[i], eligibleHeroes[j]));
                        }
                    }
                }

                if (pairs.Count > maxPairsPerSettlement)
                {
                    long shuffleSeed = RumorSeed.Of(campaignSeed, "situation.scan", dayBucket, settlement.StringId);
                    for (int i = pairs.Count - 1; i > 0; i--)
                    {
                        long stepSeed = RumorSeed.Of(shuffleSeed, i);
                        int pickIdx = rng.Pick(i + 1, stepSeed);
                        var tmp = pairs[i];
                        pairs[i] = pairs[pickIdx];
                        pairs[pickIdx] = tmp;
                    }
                    pairs = pairs.Take(maxPairsPerSettlement).ToList();
                }

                settlementPairs.Add((settlement, pairs));

                foreach (var template in situationTemplates)
                {
                    if (!template.IsEnabled(_config))
                    {
                        if (!skippedDisabledIds.Contains(template.Id))
                        {
                            skippedDisabledIds.Add(template.Id);
                            ModLog.Info($"SituationScan: skipped situation '{template.Id}' (disabled by falseRumors.enabled)");
                        }
                        continue;
                    }

                    if (template.DevOnly)
                    {
                        if (!skippedDevOnlyIds.Contains(template.Id))
                        {
                            skippedDevOnlyIds.Add(template.Id);
                        }
                        continue;
                    }

                    if (string.Equals(template.Trigger, "afterEvent", StringComparison.OrdinalIgnoreCase))
                    {
                        if (!skippedAfterEventIds.Contains(template.Id))
                        {
                            skippedAfterEventIds.Add(template.Id);
                        }
                        continue;
                    }

                    // 有合計上限群組的情境不在這裡列舉：等群組挑中它的時候才列舉（見下面 7c）
                    if (template.QuotaGroup != null)
                    {
                        continue;
                    }

                    var nonDerivedRoles = template.Roles
                        .Where(r => !r.Value.IsDerived)
                        .Select(r => r.Key)
                        .ToList();

                    if (nonDerivedRoles.Count != 2)
                    {
                        continue;
                    }

                    string role0 = nonDerivedRoles[0];
                    string role1 = nonDerivedRoles[1];

                    foreach (var pair in pairs)
                    {
                        pairsEvaluated++;

                        var roleAssignments = new Dictionary<string, Hero>(StringComparer.OrdinalIgnoreCase)
                        {
                            [role0] = pair.A,
                            [role1] = pair.B
                        };

                        var eval = SituationRunner.Evaluate(
                            template,
                            roleAssignments,
                            day,
                            _traits,
                            history,
                            heroFactsCache,
                            _heroLookup,
                            _eventStore,
                            _config);

                        if (eval.ConditionsPassed)
                        {
                            var candidate = new SituationCandidate
                            {
                                SituationId = template.Id,
                                SettlementId = settlement.StringId,
                                Weight = template.Weight,
                                RoleHeroIds = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                                {
                                    [role0] = pair.A.StringId,
                                    [role1] = pair.B.StringId
                                }
                            };

                            if (template.GetMaxPerDay(_config).HasValue)
                            {
                                if (!perSituationCandidates.TryGetValue(template.Id, out var sitList))
                                {
                                    sitList = new List<SituationCandidate>();
                                    perSituationCandidates[template.Id] = sitList;
                                }
                                sitList.Add(candidate);
                            }
                            else
                            {
                                candidates.Add(candidate);

                                if (candidates.Count >= maxCandidates)
                                {
                                    truncated = true;
                                    break;
                                }
                            }
                        }
                        else
                        {
                            var failedConditions = eval.ConditionResults.Where(c => !c.Ok).ToList();
                            foreach (var fc in failedConditions)
                            {
                                // Label 才分得出同一種條件的不同角色（isClanLeader(slighted) vs (favored)）；
                                // 用 Type 當鍵會併成一桶，同時失敗的一組也會被記兩次。
                                string condKey = !string.IsNullOrEmpty(fc.Label)
                                    ? fc.Label
                                    : (string.IsNullOrEmpty(fc.Type) ? "unknown" : fc.Type);
                                rejections[condKey] = rejections.TryGetValue(condKey, out int cnt) ? cnt + 1 : 1;
                            }

                            if (failedConditions.Count == 1 && nearMisses.Count < maxNearMisses)
                            {
                                nearMisses.Add((
                                    template.Id,
                                    settlement.StringId,
                                    new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                                    {
                                        [role0] = pair.A.StringId,
                                        [role1] = pair.B.StringId
                                    },
                                    failedConditions[0].Detail
                                ));
                            }
                        }
                    }

                    if (truncated) break;
                }

                if (truncated) break;
            }

            result.SettlementsWithPairs = settlementsWithPairs;
            result.PairsEvaluated = pairsEvaluated;
            result.CandidatesPassed = candidates.Count;

            // 6. SituationScanPlanner.Plan(...)
            var plan = SituationScanPlanner.Plan(
                candidates,
                quotaRoll.Quota,
                rng,
                slot => RumorSeed.Of(campaignSeed, "situation.scan", dayBucket, "pick", slot));

            // 7. 對每一個 Picked：重新 Evaluate → 成立才 Execute
            int triggered = 0;
            if (!isDryRun)
            {
                foreach (var pick in plan.Picked)
                {
                    var template = situationTemplates.FirstOrDefault(t => string.Equals(t.Id, pick.Candidate.SituationId, StringComparison.Ordinal));
                    if (template == null) continue;

                    var heroesForRoles = new Dictionary<string, Hero>(StringComparer.OrdinalIgnoreCase);
                    foreach (var kvp in pick.Candidate.RoleHeroIds)
                    {
                        var hero = _heroLookup.Get(kvp.Value);
                        if (hero != null)
                        {
                            heroesForRoles[kvp.Key] = hero;
                        }
                    }

                    if (heroesForRoles.Count != pick.Candidate.RoleHeroIds.Count)
                    {
                        ModLog.Info($"  scan pick #{pick.Slot + 1} skipped: one or more heroes no longer found.");
                        continue;
                    }

                    var reEval = SituationRunner.Evaluate(
                        template,
                        heroesForRoles,
                        day,
                        _traits,
                        history,
                        heroFactsCache,
                        _heroLookup,
                        _eventStore,
                        _config);

                    if (!reEval.ConditionsPassed)
                    {
                        var failedCond = reEval.ConditionResults.FirstOrDefault(c => !c.Ok);
                        ModLog.Info($"  scan pick #{pick.Slot + 1} skipped: condition '{failedCond?.Type}' no longer passed.");
                        continue;
                    }

                    var runResult = SituationRunner.Execute(
                        template,
                        reEval,
                        day,
                        "daily scan",
                        _eventStore,
                        _traits,
                        _config,
                        _heroLookup);

                    if (runResult.Started)
                    {
                        triggered++;
                    }
                }

                LastScanDay = day;
                LastScanPairsEvaluated = pairsEvaluated;
                LastScanCandidatesPassed = candidates.Count;
                LastScanTriggered = triggered;
            }

            // 7b. Per-situation quota planning & execution
            var perSituationStats = new List<(string SituationId, QuotaRoll Roll, int Candidates, int Triggered)>();
            var perSituationTemplates = situationTemplates
                .Where(t => !t.DevOnly && !string.Equals(t.Trigger, "afterEvent", StringComparison.OrdinalIgnoreCase) && t.IsEnabled(_config) && t.GetMaxPerDay(_config).HasValue)
                .OrderBy(t => t.Id, StringComparer.Ordinal)
                .ToList();

            foreach (var template in perSituationTemplates)
            {
                double perMaxPerDay = template.GetMaxPerDay(_config)!.Value;
                long perQuotaSeed = RumorSeed.Of(campaignSeed, "situation.scan", dayBucket, "quota", template.Id);
                var perQuotaRoll = SituationQuota.Roll(perMaxPerDay, rng, perQuotaSeed);

                perSituationCandidates.TryGetValue(template.Id, out var sitCandidates);
                sitCandidates ??= new List<SituationCandidate>();

                int perTriggered = 0;
                if (perQuotaRoll.Quota > 0 && sitCandidates.Count > 0)
                {
                    var perPlan = SituationScanPlanner.Plan(
                        sitCandidates,
                        perQuotaRoll.Quota,
                        rng,
                        slot => RumorSeed.Of(campaignSeed, "situation.scan", dayBucket, "pick", template.Id, slot));

                    if (!isDryRun)
                    {
                        foreach (var pick in perPlan.Picked)
                        {
                            var heroesForRoles = new Dictionary<string, Hero>(StringComparer.OrdinalIgnoreCase);
                            foreach (var kvp in pick.Candidate.RoleHeroIds)
                            {
                                var hero = _heroLookup.Get(kvp.Value);
                                if (hero != null)
                                {
                                    heroesForRoles[kvp.Key] = hero;
                                }
                            }

                            if (heroesForRoles.Count != pick.Candidate.RoleHeroIds.Count)
                            {
                                continue;
                            }

                            var reEval = SituationRunner.Evaluate(
                                template,
                                heroesForRoles,
                                day,
                                _traits,
                                history,
                                heroFactsCache,
                                _heroLookup,
                                _eventStore,
                                _config);

                            if (!reEval.ConditionsPassed)
                            {
                                continue;
                            }

                            var runResult = SituationRunner.Execute(
                                template,
                                reEval,
                                day,
                                "daily scan",
                                _eventStore,
                                _traits,
                                _config,
                                _heroLookup);

                            if (runResult.Started)
                            {
                                perTriggered++;
                            }
                        }
                    }
                    else
                    {
                        perTriggered = perPlan.Picked.Count;
                    }
                }

                perSituationStats.Add((template.Id, perQuotaRoll, sitCandidates.Count, perTriggered));
            }

            // 7c. 合計上限群組（編的話的三個起因）
            double groupElapsedMs = 0.0;
            var groupTemplates = situationTemplates
                .Where(t => t != null && t.QuotaGroup != null && !t.DevOnly && t.IsEnabled(_config)
                            && !string.Equals(t.Trigger, "afterEvent", StringComparison.OrdinalIgnoreCase))
                .OrderBy(t => t.Id, StringComparer.Ordinal)
                .ToList();
            if (groupTemplates.Count > 0)
            {
                var groupSw = Stopwatch.StartNew();
                SituationRunner.BeginScanAbsentCache(groupTemplates.Select(t => t.Id));
                try
                {
                    var enumerated = new Dictionary<string, List<SituationCandidate>>(StringComparer.Ordinal);
                    List<SituationCandidate> EnumerateGroupTemplate(SituationTemplate gt)
                    {
                        if (enumerated.TryGetValue(gt.Id, out var cached)) return cached;
                        var list = new List<SituationCandidate>();
                        enumerated[gt.Id] = list;

                        var gtRoles = gt.Roles.Where(r => !r.Value.IsDerived).Select(r => r.Key).ToList();
                        if (gtRoles.Count != 2) return list;

                        foreach (var sp in settlementPairs)
                        {
                            foreach (var pair in sp.Pairs)
                            {
                                pairsEvaluated++;
                                var assignments = new Dictionary<string, Hero>(StringComparer.OrdinalIgnoreCase)
                                {
                                    [gtRoles[0]] = pair.A,
                                    [gtRoles[1]] = pair.B
                                };
                                var gEval = SituationRunner.Evaluate(gt, assignments, day, _traits, history, heroFactsCache, _heroLookup, _eventStore, _config);
                                if (gEval.ConditionsPassed)
                                {
                                    list.Add(new SituationCandidate
                                    {
                                        SituationId = gt.Id,
                                        SettlementId = sp.Settlement.StringId,
                                        Weight = gt.Weight,
                                        RoleHeroIds = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                                        {
                                            [gtRoles[0]] = pair.A.StringId,
                                            [gtRoles[1]] = pair.B.StringId
                                        }
                                    });
                                }
                                else
                                {
                                    foreach (var fc in gEval.ConditionResults.Where(c => !c.Ok))
                                    {
                                        string condKey = !string.IsNullOrEmpty(fc.Label) ? fc.Label : (string.IsNullOrEmpty(fc.Type) ? "unknown" : fc.Type);
                                        rejections[condKey] = rejections.TryGetValue(condKey, out int cnt) ? cnt + 1 : 1;
                                    }
                                }
                            }
                        }
                        return list;
                    }

                    foreach (var grp in groupTemplates.GroupBy(t => t.QuotaGroup!.Id, StringComparer.Ordinal).OrderBy(g => g.Key, StringComparer.Ordinal))
                    {
                        var members = grp.ToList();
                        double groupMax = members[0].QuotaGroup!.GetMaxPerDay(_config) ?? 0.0;
                        long groupQuotaSeed = RumorSeed.Of(campaignSeed, "situation.scan", dayBucket, "quota", grp.Key);
                        var groupRoll = SituationQuota.Roll(groupMax, rng, groupQuotaSeed);
                        var shareList = members.Select(t => (t.Id, t.QuotaGroup!.GetShare(_config))).ToList();
                        string sharesText = string.Join(", ", shareList.Select(x => x.Id + "=" + x.Item2.ToString("0.##", CultureInfo.InvariantCulture)));
                        string rollText = SituationScanLogFormatter.FormatQuotaDetail(groupRoll);

                        if (isDryRun)
                        {
                            var counts = new List<string>();
                            foreach (var m in members)
                            {
                                int n = EnumerateGroupTemplate(m).Count;
                                result.MadeUpTalkCandidateCounts[m.Id] = n;
                                counts.Add(m.Id + "=" + n);
                            }
                            ModLog.Info($"SituationScan: quota group '{grp.Key}' (dry run): {rollText}; shares [{sharesText}]; candidates [{string.Join(", ", counts)}]");
                            continue;
                        }

                        var slotNotes = new List<string>();
                        int groupTriggered = 0;
                        for (int slot = 0; slot < groupRoll.Quota; slot++)
                        {
                            var failed = new List<string>();
                            int attempt = 0;
                            while (true)
                            {
                                long groupPickSeed = RumorSeed.Of(campaignSeed, "situation.scan", dayBucket, "group", grp.Key, slot, attempt);
                                var groupPick = SituationGroupPicker.Pick(shareList, failed, rng, groupPickSeed);
                                if (groupPick.SelectedSituationId == null)
                                {
                                    slotNotes.Add($"slot {slot + 1}: nothing to pick ({groupPick.Log})");
                                    break;
                                }
                                attempt++;
                                int curAttempt = attempt;

                                var chosenTemplate = members.First(t => string.Equals(t.Id, groupPick.SelectedSituationId, StringComparison.Ordinal));
                                var chosenCandidates = EnumerateGroupTemplate(chosenTemplate);
                                result.MadeUpTalkCandidateCounts[chosenTemplate.Id] = chosenCandidates.Count;
                                string repickNote = curAttempt > 1 ? " (repick)" : "";
                                if (chosenCandidates.Count == 0)
                                {
                                    failed.Add(chosenTemplate.Id);
                                    slotNotes.Add($"slot {slot + 1}: {groupPick.Log}{repickNote}, 0 candidates");
                                    continue;
                                }

                                int curSlot = slot;
                                var groupPlan = SituationScanPlanner.Plan(
                                    chosenCandidates,
                                    1,
                                    rng,
                                    sl => RumorSeed.Of(campaignSeed, "situation.scan", dayBucket, "group.pick", grp.Key, curSlot, curAttempt, sl));
                                if (groupPlan.Picked.Count == 0)
                                {
                                    failed.Add(chosenTemplate.Id);
                                    slotNotes.Add($"slot {slot + 1}: {groupPick.Log}{repickNote}, {chosenCandidates.Count} candidates but none picked");
                                    continue;
                                }

                                var pickedCandidate = groupPlan.Picked[0].Candidate;
                                var heroesForRoles = new Dictionary<string, Hero>(StringComparer.OrdinalIgnoreCase);
                                foreach (var kvp in pickedCandidate.RoleHeroIds)
                                {
                                    var hero = _heroLookup.Get(kvp.Value);
                                    if (hero != null) heroesForRoles[kvp.Key] = hero;
                                }
                                if (heroesForRoles.Count != pickedCandidate.RoleHeroIds.Count)
                                {
                                    failed.Add(chosenTemplate.Id);
                                    slotNotes.Add($"slot {slot + 1}: {groupPick.Log}{repickNote}, heroes no longer found");
                                    continue;
                                }

                                var gReEval = SituationRunner.Evaluate(chosenTemplate, heroesForRoles, day, _traits, history, heroFactsCache, _heroLookup, _eventStore, _config);
                                if (!gReEval.ConditionsPassed)
                                {
                                    failed.Add(chosenTemplate.Id);
                                    slotNotes.Add($"slot {slot + 1}: {groupPick.Log}{repickNote}, conditions no longer passed");
                                    continue;
                                }

                                var gRun = SituationRunner.Execute(chosenTemplate, gReEval, day, "daily scan", _eventStore, _traits, _config, _heroLookup);
                                if (gRun.Started) groupTriggered++;
                                string outcome = gRun.EventIds.Count > 0
                                    ? "event " + string.Join("/", gRun.EventIds)
                                    : (string.IsNullOrEmpty(gRun.SelectedBranchId)
                                        ? "no branch"
                                        : "branch " + gRun.SelectedBranchId + (string.IsNullOrEmpty(gRun.AbortReason) ? "" : ", " + gRun.AbortReason));
                                slotNotes.Add($"slot {slot + 1}: {groupPick.Log}{repickNote}, {chosenCandidates.Count} candidates, result {outcome}");
                                break;
                            }
                        }

                        ModLog.Info($"SituationScan: quota group '{grp.Key}': {rollText}; shares [{sharesText}]; started {groupTriggered}{(slotNotes.Count > 0 ? "; " + string.Join("; ", slotNotes) : "")}");
                    }
                }
                finally
                {
                    groupSw.Stop();
                    groupElapsedMs = groupSw.Elapsed.TotalMilliseconds;
                    ModLog.Info($"SituationScan: made-up talk stage took {groupElapsedMs:0.0} ms (absent-role cache: {SituationRunner.ScanAbsentCacheHits} hit(s), {SituationRunner.ScanAbsentCacheMisses} miss(es))");
                    SituationRunner.EndScanAbsentCache();
                }
            }

            sw.Stop();
            double elapsedMs = sw.Elapsed.TotalMilliseconds;

            int leftOver = candidates.Count - (isDryRun ? plan.Picked.Count : triggered);

            // 8. 印 §3.7 的總結行、聚合行、near-miss 行、pick 行
            // 順序固定：pick 行 → 總結行 → 聚合行 → near-miss 行
            foreach (var pick in plan.Picked)
            {
                ModLog.Info(SituationScanLogFormatter.FormatPick(pick));
            }

            ModLog.Info(SituationScanLogFormatter.FormatSummary(
                day,
                totalSettlements,
                settlementsWithPairs,
                pairsEvaluated,
                maxPairsPerSettlement,
                candidates.Count,
                truncated,
                maxCandidates,
                quotaRoll,
                isDryRun ? plan.Picked.Count : triggered,
                leftOver,
                elapsedMs,
                isDryRun));

            var perSituationQuotasLine = SituationScanLogFormatter.FormatPerSituationQuotas(perSituationStats);
            if (!string.IsNullOrEmpty(perSituationQuotasLine))
            {
                ModLog.Info(perSituationQuotasLine!);
            }

            var rejectionsLine = SituationScanLogFormatter.FormatRejections(rejections);
            if (!string.IsNullOrEmpty(rejectionsLine))
            {
                ModLog.Info(rejectionsLine!);
            }

            var skippedDevOnlyLine = SituationScanLogFormatter.FormatSkippedDevOnly(skippedDevOnlyIds);
            if (!string.IsNullOrEmpty(skippedDevOnlyLine))
            {
                ModLog.Info(skippedDevOnlyLine!);
            }

            var skippedAfterEventLine = SituationScanLogFormatter.FormatSkippedAfterEvent(skippedAfterEventIds);
            if (!string.IsNullOrEmpty(skippedAfterEventLine))
            {
                ModLog.Info(skippedAfterEventLine!);
            }

            foreach (var nm in nearMisses)
            {
                ModLog.Info(SituationScanLogFormatter.FormatNearMiss(nm.SitId, nm.SetId, nm.Roles, nm.Detail));
            }

            if (isDryRun)
            {
                ModLog.Info(SituationScanLogFormatter.FormatDryRunEnd());
            }

            // Build summaries for dev dialogue
            if (plan.Picked.Count > 0)
            {
                var firstPick = plan.Picked[0];
                // Settlement.Find 沒有帳本列（卡片 §1 的原生成員白名單）⇒ 用已經允許的 Settlement.All 反查
                var s = Settlement.All?.FirstOrDefault(x => x != null
                    && string.Equals(x.StringId, firstPick.Candidate.SettlementId, StringComparison.Ordinal));
                string sName = s?.Name?.ToString() ?? firstPick.Candidate.SettlementId;
                result.WouldTriggerSummary = $"{firstPick.Candidate.SituationId} at {sName}";
            }

            if (rejections.Count > 0)
            {
                var top = rejections
                    .OrderByDescending(kv => kv.Value)
                    .ThenBy(kv => kv.Key, StringComparer.Ordinal)
                    .Take(2)
                    .Select(kv => $"{kv.Key} {kv.Value}");
                result.TopBlockersSummary = string.Join(", ", top);
            }

            return result;
        }
    }
}
