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

        /// <summary>
        /// 一組過了條件的候選：角色綁定含推導角色（求值結果），有 pickTraits 的角色順便查好個性。
        /// Payload 留非推導角色的綁定，挑中之後重新求值用。
        /// </summary>
        private SituationPickCandidate BuildPickCandidate(
            SituationPickKind kind,
            string settlementId,
            Dictionary<string, string> nonDerivedRoleIds,
            IReadOnlyDictionary<string, string?> boundHeroIds,
            string? playerHeroId)
        {
            var roles = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
            foreach (var kv in boundHeroIds) roles[kv.Key] = kv.Value;
            foreach (var kv in nonDerivedRoleIds) roles[kv.Key] = kv.Value;

            return new SituationPickCandidate
            {
                SettlementId = settlementId,
                RoleHeroIds = roles,
                RoleTraits = SituationPicker.LookUpRoleTraits(kind, roles, _traits, playerHeroId),
                Payload = new SituationCandidate
                {
                    SituationId = kind.Id,
                    SettlementId = settlementId,
                    Weight = kind.Weight,
                    RoleHeroIds = nonDerivedRoleIds
                }
            };
        }

        /// <summary>候選的穩定順序：聚落，再來是非推導角色的綁定（角色鍵排序後串起來），全部用 Ordinal。</summary>
        private static List<SituationPickCandidate> StableOrder(IEnumerable<SituationPickCandidate> candidates)
        {
            return candidates
                .OrderBy(c => c.SettlementId ?? string.Empty, StringComparer.Ordinal)
                .ThenBy(c => RolesKey(((SituationCandidate)c.Payload!).RoleHeroIds), StringComparer.Ordinal)
                .ToList();
        }

        private static string RolesKey(IReadOnlyDictionary<string, string> roleHeroes)
        {
            if (roleHeroes == null || roleHeroes.Count == 0) return string.Empty;
            return string.Join(";", roleHeroes.OrderBy(kvp => kvp.Key, StringComparer.Ordinal).Select(kvp => $"{kvp.Key}={kvp.Value}"));
        }

        /// <summary>同一種事換一份候選（列舉好的結果，或重新求值失敗後的空串與原因）。</summary>
        private static SituationPickKind CloneKind(SituationPickKind source, IReadOnlyList<SituationPickCandidate> candidates, string? noCandidateReason)
        {
            return new SituationPickKind
            {
                Id = source.Id,
                Weight = source.Weight,
                WeightSource = source.WeightSource,
                PickTraitRoles = source.PickTraitRoles,
                Candidates = candidates,
                NoCandidateReason = noCandidateReason,
                Payload = source.Payload
            };
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
                          && CityScanTriggerFilter.IsCityScanTrigger(t.Trigger));
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

            var besiegedSettlementIds = new List<string>();
            var eligibleSettlements = new List<Settlement>();
            if (allSettlements != null)
            {
                foreach (var s in allSettlements)
                {
                    if (s == null || (!s.IsTown && !s.IsCastle && !s.IsVillage)) continue;
                    if (s.IsUnderSiege)
                    {
                        if (!string.IsNullOrEmpty(s.StringId))
                        {
                            besiegedSettlementIds.Add(s.StringId);
                        }
                    }
                    else
                    {
                        eligibleSettlements.Add(s);
                    }
                }
            }

            if (besiegedSettlementIds.Count > 0)
            {
                besiegedSettlementIds.Sort(StringComparer.Ordinal);
                ModLog.Info($"SituationScan: skipped {besiegedSettlementIds.Count} settlement(s) under siege [{string.Join(", ", besiegedSettlementIds)}]");
            }


            int totalSettlements = eligibleSettlements.Count;
            int settlementsWithPairs = 0;
            int pairsEvaluated = 0;
            int maxPairsPerSettlement = scanConfig.MaxPairsPerSettlement;
            int maxCandidates = scanConfig.MaxCandidates;

            // 每個模板一種「事」：候選列舉完再交給共用挑法。派生角色的綁定也跟著候選走（挑人排名單要看）
            var kindById = new Dictionary<string, SituationPickKind>(StringComparer.Ordinal);
            var pickCandidatesById = new Dictionary<string, List<SituationPickCandidate>>(StringComparer.Ordinal);
            int mainCandidateCount = 0;
            var heroFactsCache = new Dictionary<string, SituationRoleFacts>(StringComparer.Ordinal);
            var rejections = new Dictionary<string, int>(StringComparer.Ordinal);
            var nearMisses = new List<(string SitId, string SetId, Dictionary<string, string> Roles, string Detail)>();
            int maxNearMisses = _config.Debug != null ? _config.Debug.DevReportMaxCrowdedOut : 5;
            bool truncated = false;
            string? mainHeroId = Hero.MainHero?.StringId;

            foreach (var t in situationTemplates)
            {
                if (t == null || !t.IsEnabled(_config) || t.DevOnly || !CityScanTriggerFilter.IsCityScanTrigger(t.Trigger) || t.QuotaGroup != null) continue;
                if (t.Roles.Count(r => !r.Value.IsDerived) != 2) continue;
                kindById[t.Id] = new SituationPickKind
                {
                    Id = t.Id,
                    Weight = t.Weight,
                    WeightSource = "template weight",
                    PickTraitRoles = SituationPickKind.TraitRolesOf(t),
                    Payload = t
                };
                pickCandidatesById[t.Id] = new List<SituationPickCandidate>();
            }

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

                    // 只收 direct：afterEvent 要等一件事件才發生，流浪者的觸發有自己的擲骰
                    if (!CityScanTriggerFilter.IsCityScanTrigger(template.Trigger))
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
                            var roleIds = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                            {
                                [role0] = pair.A.StringId,
                                [role1] = pair.B.StringId
                            };
                            pickCandidatesById[template.Id].Add(BuildPickCandidate(kindById[template.Id], settlement.StringId, roleIds, eval.BoundHeroIds, mainHeroId));

                            if (!template.GetMaxPerDay(_config).HasValue)
                            {
                                mainCandidateCount++;

                                if (mainCandidateCount >= maxCandidates)
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
            result.CandidatesPassed = mainCandidateCount;

            // 候選照列舉的順序排穩定（聚落、再來是角色綁定），共用挑法照這個順序挑
            foreach (var kv in kindById)
            {
                kv.Value.Candidates = StableOrder(pickCandidatesById[kv.Key]);
            }

            var mainKinds = kindById.Values
                .Where(k => !((SituationTemplate)k.Payload!).GetMaxPerDay(_config).HasValue)
                .OrderBy(k => k.Id, StringComparer.Ordinal)
                .ToList();

            // 每種事各有幾組合格：每天的總結行用
            var qualifiedSummaries = new List<SituationPickKindSummary>();
            foreach (var k in mainKinds)
            {
                qualifiedSummaries.Add(new SituationPickKindSummary { Id = k.Id, Weight = k.Weight, Qualified = k.Candidates?.Count ?? 0 });
            }

            // 今天已經捲進事情的人：主要六種、背地說君主壞話與拒絕援手、編的話依序共用這一份，每天重算、不存
            var roster = new SituationRoster();
            var logOptions = SituationPickLogWriter.Options(_heroLookup, isDryRun || SituationPickLogWriter.LogAllCandidates(_config));

            // 重新求值，成立才執行；真的執行了，才把實際綁到的所有人放進名單
            SituationRunResult? ReevaluateAndRun(SituationTemplate template, SituationCandidate cand, out string skipKind, out string? failedType)
            {
                skipKind = string.Empty;
                failedType = null;

                var heroesForRoles = new Dictionary<string, Hero>(StringComparer.OrdinalIgnoreCase);
                foreach (var kvp in cand.RoleHeroIds)
                {
                    var hero = _heroLookup.Get(kvp.Value);
                    if (hero != null)
                    {
                        heroesForRoles[kvp.Key] = hero;
                    }
                }

                if (heroesForRoles.Count != cand.RoleHeroIds.Count)
                {
                    skipKind = "heroes";
                    return null;
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
                    skipKind = "conditions";
                    failedType = reEval.ConditionResults.FirstOrDefault(c => !c.Ok)?.Type;
                    return null;
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
                    var involved = new List<string?>(reEval.BoundHeroIds.Values);
                    involved.AddRange(cand.RoleHeroIds.Values);
                    string eventText = runResult.EventIds.Count > 0 ? string.Join("/", runResult.EventIds) : "(no event)";
                    roster.AddExecuted(involved, template.Id + " " + eventText);
                }

                return runResult;
            }

            // 一個名額叫一次共用挑法（重新求值不成立的那一件，它的人不會被排掉）。
            // 回傳真的執行的件數；乾跑回傳挑中的件數（把挑中的當作都成立，名單照同一套規則長）。
            int RunSlots(
                string site,
                IReadOnlyList<SituationPickKind> kinds,
                int quota,
                Func<int, long> kindSeed,
                Func<int, SituationPickKind, long> personSeed,
                bool announceSkips,
                List<SituationCandidate>? pickedOut)
            {
                int count = 0;
                for (int slot = 0; slot < quota; slot++)
                {
                    var pickResult = SituationPicker.Pick(new SituationPickRequest
                    {
                        Kinds = kinds,
                        Count = 1,
                        FirstSlot = slot,
                        TotalSlots = quota,
                        Roster = roster,
                        Rng = rng,
                        PlayerHeroId = mainHeroId,
                        KindSeed = kindSeed,
                        PersonSeed = personSeed,
                        Site = site,
                        Day = day
                    });
                    SituationPickLogWriter.Write(pickResult.Records.Count > 0 ? pickResult.Records[0] : null, logOptions);

                    if (pickResult.Picks.Count == 0) break;

                    var pickedKind = pickResult.Picks[0].Kind;
                    var pickedCandidate = pickResult.Picks[0].Candidate;
                    var scanCandidate = (SituationCandidate)pickedCandidate.Payload!;
                    pickedOut?.Add(scanCandidate);

                    if (isDryRun)
                    {
                        roster.AddExecuted(pickedCandidate.HeroIds(), pickedKind.Id + " (dry run)");
                        count++;
                        continue;
                    }

                    var template = (SituationTemplate)pickedKind.Payload!;
                    var runResult = ReevaluateAndRun(template, scanCandidate, out string skipKind, out string? failedType);
                    if (runResult == null)
                    {
                        if (announceSkips)
                        {
                            ModLog.Info(skipKind == "heroes"
                                ? $"  scan pick #{slot + 1} skipped: one or more heroes no longer found."
                                : $"  scan pick #{slot + 1} skipped: condition '{failedType}' no longer passed.");
                        }
                        continue;
                    }

                    if (runResult.Started)
                    {
                        count++;
                    }
                }
                return count;
            }

            // 6. 7. 主要六種：共用挑法，一個名額挑一件，重新求值成立才執行
            var pickedMain = new List<SituationCandidate>();
            int triggered = RunSlots(
                "city scan, main",
                mainKinds,
                quotaRoll.Quota,
                slot => RumorSeed.Of(campaignSeed, "situation.scan", dayBucket, "pick", slot),
                (slot, kind) => RumorSeed.Of(campaignSeed, "situation.scan", dayBucket, "pick", slot, "person"),
                announceSkips: true,
                pickedOut: pickedMain);

            if (!isDryRun)
            {
                LastScanDay = day;
                LastScanPairsEvaluated = pairsEvaluated;
                LastScanCandidatesPassed = mainCandidateCount;
                LastScanTriggered = triggered;
            }

            // 7b. Per-situation quota planning & execution（接著用主要六種留下的名單）
            var perSituationStats = new List<(string SituationId, QuotaRoll Roll, int Candidates, int Triggered)>();
            var perSituationTemplates = situationTemplates
                .Where(t => !t.DevOnly && CityScanTriggerFilter.IsCityScanTrigger(t.Trigger) && t.IsEnabled(_config) && t.GetMaxPerDay(_config).HasValue)
                .OrderBy(t => t.Id, StringComparer.Ordinal)
                .ToList();

            foreach (var template in perSituationTemplates)
            {
                double perMaxPerDay = template.GetMaxPerDay(_config)!.Value;
                long perQuotaSeed = RumorSeed.Of(campaignSeed, "situation.scan", dayBucket, "quota", template.Id);
                var perQuotaRoll = SituationQuota.Roll(perMaxPerDay, rng, perQuotaSeed);

                kindById.TryGetValue(template.Id, out var perKind);
                int perCandidateCount = perKind?.Candidates?.Count ?? 0;
                if (perKind != null)
                {
                    qualifiedSummaries.Add(new SituationPickKindSummary { Id = perKind.Id, Weight = perKind.Weight, Qualified = perCandidateCount });
                }

                int perTriggered = 0;
                if (perQuotaRoll.Quota > 0 && perKind != null)
                {
                    string perId = template.Id;
                    perTriggered = RunSlots(
                        "city scan, " + perId,
                        new[] { perKind },
                        perQuotaRoll.Quota,
                        slot => RumorSeed.Of(campaignSeed, "situation.scan", dayBucket, "pick", perId, slot),
                        (slot, kind) => RumorSeed.Of(campaignSeed, "situation.scan", dayBucket, "pick", perId, slot, "person"),
                        announceSkips: false,
                        pickedOut: null);
                }

                perSituationStats.Add((template.Id, perQuotaRoll, perCandidateCount, perTriggered));
            }

            // 7c. 合計上限群組（編的話的三個起因）：三種的候選等挑中那一種才列舉，接著用名單
            double groupElapsedMs = 0.0;
            var groupTemplates = situationTemplates
                .Where(t => t != null && t.QuotaGroup != null && !t.DevOnly && t.IsEnabled(_config)
                            && CityScanTriggerFilter.IsCityScanTrigger(t.Trigger))
                .OrderBy(t => t.Id, StringComparer.Ordinal)
                .ToList();
            if (groupTemplates.Count > 0)
            {
                var groupSw = Stopwatch.StartNew();
                SituationRunner.BeginScanAbsentCache(groupTemplates.Select(t => t.Id));
                var groupSummaryKinds = new List<SituationPickKind>();
                var enumerated = new Dictionary<string, List<SituationPickCandidate>>(StringComparer.Ordinal);
                try
                {
                    List<SituationPickCandidate> EnumerateGroupTemplate(SituationTemplate gt, SituationPickKind gtKind)
                    {
                        if (enumerated.TryGetValue(gt.Id, out var cached)) return cached;
                        var list = new List<SituationPickCandidate>();
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
                                    var gRoleIds = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                                    {
                                        [gtRoles[0]] = pair.A.StringId,
                                        [gtRoles[1]] = pair.B.StringId
                                    };
                                    list.Add(BuildPickCandidate(gtKind, sp.Settlement.StringId, gRoleIds, gEval.BoundHeroIds, mainHeroId));
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

                        var ordered = StableOrder(list);
                        list.Clear();
                        list.AddRange(ordered);
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

                        // 三種都先當作有候選參加，挑中才列舉
                        var groupKinds = new List<SituationPickKind>();
                        foreach (var m in members)
                        {
                            var memberTemplate = m;
                            var memberKind = new SituationPickKind
                            {
                                Id = m.Id,
                                Weight = m.QuotaGroup!.GetShare(_config),
                                WeightSource = "share",
                                PickTraitRoles = SituationPickKind.TraitRolesOf(m),
                                Payload = m
                            };
                            memberKind.CandidateProvider = () => EnumerateGroupTemplate(memberTemplate, memberKind);
                            groupKinds.Add(memberKind);
                            groupSummaryKinds.Add(memberKind);
                        }

                        string dryRunCounts = string.Empty;
                        if (isDryRun)
                        {
                            var counts = new List<string>();
                            for (int mi = 0; mi < members.Count; mi++)
                            {
                                int n = EnumerateGroupTemplate(members[mi], groupKinds[mi]).Count;
                                result.MadeUpTalkCandidateCounts[members[mi].Id] = n;
                                counts.Add(members[mi].Id + "=" + n);
                            }
                            dryRunCounts = $"; candidates [{string.Join(", ", counts)}]";
                        }

                        var slotNotes = new List<string>();
                        int groupTriggered = 0;
                        for (int slot = 0; slot < groupRoll.Quota; slot++)
                        {
                            // 重新求值不成立的那一種，同一個名額再叫一次時當作沒有候選
                            var failed = new Dictionary<string, string>(StringComparer.Ordinal);
                            int attempt = 0;
                            while (true)
                            {
                                // 已經列舉過的種類直接給列舉結果（日誌才印得出合格數），沒列舉過的仍然等挑中才列舉
                                var requestKinds = new List<SituationPickKind>();
                                foreach (var k in groupKinds)
                                {
                                    if (failed.TryGetValue(k.Id, out var failReason))
                                    {
                                        requestKinds.Add(CloneKind(k, new List<SituationPickCandidate>(), failReason));
                                    }
                                    else if (enumerated.TryGetValue(k.Id, out var alreadyListed))
                                    {
                                        requestKinds.Add(CloneKind(k, alreadyListed, null));
                                    }
                                    else
                                    {
                                        requestKinds.Add(k);
                                    }
                                }

                                int kindAttempt = attempt;
                                var groupPickResult = SituationPicker.Pick(new SituationPickRequest
                                {
                                    Kinds = requestKinds,
                                    Count = 1,
                                    FirstSlot = slot,
                                    TotalSlots = groupRoll.Quota,
                                    Roster = roster,
                                    Rng = rng,
                                    PlayerHeroId = mainHeroId,
                                    KindSeed = s => RumorSeed.Of(campaignSeed, "situation.scan", dayBucket, "group", grp.Key, s, kindAttempt),
                                    PersonSeed = (s, k) => RumorSeed.Of(campaignSeed, "situation.scan", dayBucket, "group.pick", grp.Key, s, kindAttempt + 1, 0),
                                    Site = "city scan, made-up talk (" + grp.Key + ")",
                                    Day = day
                                });
                                var groupRecord = groupPickResult.Records.Count > 0 ? groupPickResult.Records[0] : null;
                                SituationPickLogWriter.Write(groupRecord, logOptions);

                                foreach (var m in members)
                                {
                                    if (enumerated.TryGetValue(m.Id, out var enumeratedList))
                                    {
                                        result.MadeUpTalkCandidateCounts[m.Id] = enumeratedList.Count;
                                    }
                                }

                                if (groupPickResult.Picks.Count == 0)
                                {
                                    slotNotes.Add($"slot {slot + 1}: nothing to pick ({groupRecord?.StopReason ?? "nothing to pick"})");
                                    break;
                                }
                                attempt++;
                                string repickNote = attempt > 1 ? " (repick)" : "";

                                var chosenKind = groupPickResult.Picks[0].Kind;
                                var chosenCandidate = groupPickResult.Picks[0].Candidate;
                                var chosenTemplate = (SituationTemplate)chosenKind.Payload!;
                                string pickNote = $"picked {chosenKind.Id} (share {chosenKind.Weight:0.00}){repickNote}";
                                int chosenCount = enumerated.TryGetValue(chosenTemplate.Id, out var chosenList) ? chosenList.Count : 0;

                                if (isDryRun)
                                {
                                    roster.AddExecuted(chosenCandidate.HeroIds(), chosenKind.Id + " (dry run)");
                                    groupTriggered++;
                                    slotNotes.Add($"slot {slot + 1}: {pickNote}, {chosenCount} candidates");
                                    break;
                                }

                                var pickedScanCandidate = (SituationCandidate)chosenCandidate.Payload!;
                                var gRun = ReevaluateAndRun(chosenTemplate, pickedScanCandidate, out string gSkipKind, out _);
                                if (gRun == null)
                                {
                                    string reason = gSkipKind == "heroes" ? "heroes no longer found" : "conditions no longer passed";
                                    failed[chosenTemplate.Id] = reason;
                                    slotNotes.Add($"slot {slot + 1}: {pickNote}, {reason}");
                                    continue;
                                }

                                if (gRun.Started) groupTriggered++;
                                string outcome = gRun.EventIds.Count > 0
                                    ? "event " + string.Join("/", gRun.EventIds)
                                    : (string.IsNullOrEmpty(gRun.SelectedBranchId)
                                        ? "no branch"
                                        : "branch " + gRun.SelectedBranchId + (string.IsNullOrEmpty(gRun.AbortReason) ? "" : ", " + gRun.AbortReason));
                                slotNotes.Add($"slot {slot + 1}: {pickNote}, {chosenCount} candidates, result {outcome}");
                                break;
                            }
                        }

                        string dryTag = isDryRun ? " (dry run)" : string.Empty;
                        string startedWord = isDryRun ? "would start" : "started";
                        ModLog.Info($"SituationScan: quota group '{grp.Key}'{dryTag}: {rollText}; shares [{sharesText}]{dryRunCounts}; {startedWord} {groupTriggered}{(slotNotes.Count > 0 ? "; " + string.Join("; ", slotNotes) : "")}");
                    }
                }
                finally
                {
                    foreach (var gk in groupSummaryKinds)
                    {
                        qualifiedSummaries.Add(new SituationPickKindSummary
                        {
                            Id = gk.Id,
                            Weight = gk.Weight,
                            Qualified = enumerated.TryGetValue(gk.Id, out var gl) ? gl.Count : -1
                        });
                    }

                    groupSw.Stop();
                    groupElapsedMs = groupSw.Elapsed.TotalMilliseconds;
                    ModLog.Info($"SituationScan: made-up talk stage took {groupElapsedMs:0.0} ms (absent-role cache: {SituationRunner.ScanAbsentCacheHits} hit(s), {SituationRunner.ScanAbsentCacheMisses} miss(es))");
                    SituationRunner.EndScanAbsentCache();
                }
            }

            sw.Stop();
            double elapsedMs = sw.Elapsed.TotalMilliseconds;

            int leftOver = mainCandidateCount - triggered;

            // 8. 印 §3.7 的總結行、聚合行、near-miss 行、pick 行
            // 順序固定：總結行 → 聚合行 → near-miss 行
            ModLog.Info(SituationScanLogFormatter.FormatSummary(
                day,
                totalSettlements,
                settlementsWithPairs,
                pairsEvaluated,
                maxPairsPerSettlement,
                mainCandidateCount,
                truncated,
                maxCandidates,
                quotaRoll,
                triggered,
                leftOver,
                elapsedMs,
                isDryRun));

            ModLog.Info("SituationScan: " + SituationPickLogFormatter.FormatQualifiedCounts(qualifiedSummaries, roster.Count));

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
            if (pickedMain.Count > 0)
            {
                var firstPick = pickedMain[0];
                // Settlement.Find 沒有帳本列（卡片 §1 的原生成員白名單）⇒ 用已經允許的 Settlement.All 反查
                var s = Settlement.All?.FirstOrDefault(x => x != null
                    && string.Equals(x.StringId, firstPick.SettlementId, StringComparison.Ordinal));
                string sName = s?.Name?.ToString() ?? firstPick.SettlementId;
                result.WouldTriggerSummary = $"{firstPick.SituationId} at {sName}";
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
