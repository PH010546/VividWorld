using System;
using System.Collections.Generic;
using System.Linq;
using VividWorld.Core.Config;
using VividWorld.Core.Rumors;
using VividWorld.Core.Util;

namespace VividWorld.Core.Situations
{
    public enum WandererTrigger
    {
        Battle,
        Daily
    }

    public enum WandererRollOutcome
    {
        Disabled,
        PlayerCaptured,
        InBattle,
        InSiege,
        TownWindowAlreadyShown,
        TooFewWanderers,
        Cooldown,
        GapCooldown,
        ChanceMissed,
        NoCandidate,
        Happened
    }

    public enum WandererDecisionKind
    {
        Decide,
        Watch,
        Unseen
    }

    /// <summary>開發者工具用：指定情境與是否私下，並無視冷卻、間隔與機率。</summary>
    public sealed class WandererRollForce
    {
        public string? SituationId;
        public bool? Unseen;
    }

    public sealed class WandererRollInput
    {
        public WandererTrigger Trigger;
        public double Today;
        public double? LastBattleDay;
        public double? LastDailyDay;
        public double? LastAnyDay;

        /// <summary>在場、能被挑的流浪者（不含玩家）。由呼叫端篩好。</summary>
        public IReadOnlyList<string> AvailableWandererHeroIds = Array.Empty<string>();

        public string PlayerHeroId = string.Empty;

        /// <summary>同一天城裡視窗是否已跳過。</summary>
        public bool TownWindowShownToday;

        /// <summary>上一次城裡情境視窗發生的日子；若與今天在同一個天數區塊亦視為同一天已跳過。</summary>
        public double? LastTownPresenceDay;

        public bool PlayerCaptured;
        public bool InBattle;
        public bool InSiege;

        public long CampaignSeed;

        /// <summary>戰鬥序號，戰後觸發進種子（同一天兩場仗不會擲出同一個數）。</summary>
        public int BattleSequence;

        /// <summary>非 null＝開發者強制：冷卻、間隔與機率不看；可指定情境或私下。</summary>
        public WandererRollForce? Force;
    }

    public sealed class WandererCandidateReport
    {
        public string SituationId = string.Empty;
        public double Weight;
        public int AssignmentsTried;
        public int AssignmentsPassed;

        /// <summary>這個情境不成立的原因（角色不符等）；null＝有試指派。</summary>
        public string? StructuralReason;

        /// <summary>沒過的條件（Label）→ 幾組指派卡在它。</summary>
        public Dictionary<string, int> FailedByLabel = new(StringComparer.Ordinal);

        /// <summary>每個沒過的條件第一次出現時的完整說明。</summary>
        public Dictionary<string, string> FirstFailureDetail = new(StringComparer.Ordinal);

        public bool Passed => StructuralReason == null && AssignmentsPassed > 0;

        public List<WandererAssignment> PassingAssignments = new();
        internal SituationTemplate? Template;
    }

    public sealed class WandererAssignment
    {
        public Dictionary<string, string> NonDerivedRoleHeroIds = new(StringComparer.OrdinalIgnoreCase);
        public PlayerPresenceEvaluation Evaluation = new();
    }

    public sealed class WandererPick
    {
        public SituationTemplate Template = null!;
        public WandererAssignment Assignment = null!;
        public double Probability;
        public int AssignmentChoices;
        public WandererDecisionKind DecisionKind;
    }

    public sealed class WandererRollResult
    {
        public WandererTrigger Trigger;
        public WandererRollOutcome Outcome;

        /// <summary>沒擲或沒發生的原因；發生了就是 null。</summary>
        public string? Reason;

        public bool Forced;
        public int AvailableWandererCount;
        public double? LastBattleDay;
        public double? LastDailyDay;
        public double? LastAnyDay;
        public double CooldownDays;

        /// <summary>冷卻還剩幾天；不在冷卻中是 0，沒有紀錄是 null。</summary>
        public double? CooldownRemainingDays;

        public double MinGapDays;

        /// <summary>間隔還剩幾天；不在間隔冷卻中是 0，沒有紀錄是 null。</summary>
        public double? GapRemainingDays;

        public double Chance;

        /// <summary>機率擲出的數；沒擲到這一步（或強制）是 null。</summary>
        public double? Roll;

        public double UnseenShare;

        /// <summary>私下擲出的數；沒擲到這一步（非平常類或強制指定）是 null。</summary>
        public double? UnseenRoll;

        public bool IsUnseen;
        public WandererDecisionKind? DecisionKind;

        public List<WandererCandidateReport> Candidates = new();
        public WandererPick? Pick;
    }

    public static class WandererEventRoller
    {
        public static WandererRollResult Roll(
            WandererRollInput input,
            VividWorldConfig? config,
            IEnumerable<SituationTemplate> templates,
            IPlayerPresenceEvaluator evaluator,
            IDeterministicRng rng)
        {
            if (input == null) throw new ArgumentNullException(nameof(input));
            if (evaluator == null) throw new ArgumentNullException(nameof(evaluator));
            if (rng == null) throw new ArgumentNullException(nameof(rng));

            var settings = config?.Situations?.WandererEvents ?? new WandererEventsConfig();
            string playerId = input.PlayerHeroId ?? string.Empty;
            var wanderers = (input.AvailableWandererHeroIds ?? Array.Empty<string>())
                .Where(h => !string.IsNullOrEmpty(h) && !string.Equals(h, playerId, StringComparison.Ordinal))
                .Distinct(StringComparer.Ordinal)
                .OrderBy(h => h, StringComparer.Ordinal)
                .ToList();

            var result = new WandererRollResult
            {
                Trigger = input.Trigger,
                Forced = input.Force != null,
                AvailableWandererCount = wanderers.Count,
                LastBattleDay = input.LastBattleDay,
                LastDailyDay = input.LastDailyDay,
                LastAnyDay = input.LastAnyDay,
                MinGapDays = settings.MinGapDays,
                UnseenShare = settings.UnseenShare
            };

            if (input.Trigger == WandererTrigger.Battle)
            {
                result.CooldownDays = settings.BattleCooldownDays;
                result.Chance = settings.BattleChance;
                if (input.LastBattleDay.HasValue)
                {
                    double elapsed = input.Today - input.LastBattleDay.Value;
                    result.CooldownRemainingDays = Math.Max(0.0, settings.BattleCooldownDays - elapsed);
                }
            }
            else
            {
                result.CooldownDays = settings.DailyCooldownDays;
                result.Chance = settings.DailyChance;
                if (input.LastDailyDay.HasValue)
                {
                    double elapsed = input.Today - input.LastDailyDay.Value;
                    result.CooldownRemainingDays = Math.Max(0.0, settings.DailyCooldownDays - elapsed);
                }
            }

            if (input.LastAnyDay.HasValue)
            {
                double elapsedAny = input.Today - input.LastAnyDay.Value;
                result.GapRemainingDays = Math.Max(0.0, settings.MinGapDays - elapsedAny);
            }

            // 1. 開關關著
            if (!settings.Enabled)
            {
                result.Outcome = WandererRollOutcome.Disabled;
                result.Reason = "disabled in settings";
                return result;
            }

            // 2. 玩家被俘
            if (input.PlayerCaptured)
            {
                result.Outcome = WandererRollOutcome.PlayerCaptured;
                result.Reason = "player is a prisoner";
                return result;
            }

            // 3. 平常那類：玩家隊伍在戰鬥或圍城中
            if (input.Trigger == WandererTrigger.Daily)
            {
                if (input.InBattle)
                {
                    result.Outcome = WandererRollOutcome.InBattle;
                    result.Reason = "player party is in battle";
                    return result;
                }
                if (input.InSiege)
                {
                    result.Outcome = WandererRollOutcome.InSiege;
                    result.Reason = "player party is in a siege";
                    return result;
                }
            }

            // 4. 同一天已經跳過城裡情境的視窗
            bool townWindowShown = input.TownWindowShownToday
                || (input.LastTownPresenceDay.HasValue && RumorSeed.DayBucket(input.LastTownPresenceDay.Value) == RumorSeed.DayBucket(input.Today));
            if (townWindowShown)
            {
                result.Outcome = WandererRollOutcome.TownWindowAlreadyShown;
                result.Reason = "town window already shown today";
                return result;
            }

            // 5. 能被挑的少於兩位
            if (wanderers.Count < 2)
            {
                result.Outcome = WandererRollOutcome.TooFewWanderers;
                result.Reason = $"{wanderers.Count} available wanderer(s), need 2";
                return result;
            }

            // 6. 這一類在冷卻
            if (input.Force == null && result.CooldownRemainingDays.HasValue && result.CooldownRemainingDays.Value > 0.0)
            {
                result.Outcome = WandererRollOutcome.Cooldown;
                result.Reason = "in cooldown";
                return result;
            }

            // 7. 離上一件流浪者事件不到 minGapDays
            if (input.Force == null && result.GapRemainingDays.HasValue && result.GapRemainingDays.Value > 0.0)
            {
                result.Outcome = WandererRollOutcome.GapCooldown;
                result.Reason = "gap cooldown";
                return result;
            }

            int dayBucket = RumorSeed.DayBucket(input.Today);
            string triggerStr = input.Trigger == WandererTrigger.Battle ? "wandererBattle" : "wandererDaily";
            long baseSeed = input.Trigger == WandererTrigger.Battle
                ? RumorSeed.Of(input.CampaignSeed, "wandererEvent", triggerStr, dayBucket, input.BattleSequence)
                : RumorSeed.Of(input.CampaignSeed, "wandererEvent", triggerStr, dayBucket);

            // 8. 機率沒中
            if (input.Force == null)
            {
                double roll = rng.NextDouble(RumorSeed.Of(baseSeed, "chance"));
                result.Roll = roll;
                if (!(roll < result.Chance))
                {
                    result.Outcome = WandererRollOutcome.ChanceMissed;
                    result.Reason = "chance missed";
                    return result;
                }
            }

            // 私下判定（僅限平常類）
            if (input.Trigger == WandererTrigger.Daily)
            {
                if (input.Force?.Unseen.HasValue == true)
                {
                    result.IsUnseen = input.Force.Unseen.Value;
                }
                else
                {
                    double unseenRoll = rng.NextDouble(RumorSeed.Of(baseSeed, "unseen"));
                    result.UnseenRoll = unseenRoll;
                    result.IsUnseen = unseenRoll < settings.UnseenShare;
                }
            }
            else
            {
                result.IsUnseen = false;
            }

            int maxAssignments = Math.Max(2, config?.Situations?.Scan?.MaxPairsPerSettlement ?? 24);
            bool forced = input.Force != null;

            foreach (var template in templates ?? Enumerable.Empty<SituationTemplate>())
            {
                if (template == null) continue;
                if (template.DevOnly) continue;
                if (!template.IsEnabled(config)) continue;
                if (!string.Equals(template.Trigger, triggerStr, StringComparison.OrdinalIgnoreCase)) continue;
                if (input.Force?.SituationId != null && !string.Equals(input.Force.SituationId, template.Id, StringComparison.OrdinalIgnoreCase)) continue;

                var report = EvaluateCandidate(template, playerId, wanderers, evaluator, rng, baseSeed, maxAssignments, forced);
                result.Candidates.Add(report);
            }

            var passing = result.Candidates.Where(c => c.Passed && c.Weight > 0.0).ToList();
            if (passing.Count == 0)
            {
                result.Outcome = WandererRollOutcome.NoCandidate;
                result.Reason = result.Candidates.Count == 0 ? "no wanderer situation available" : "no candidate passed";
                return result;
            }

            var weights = passing.Select(c => c.Weight).ToList();
            double total = weights.Sum();
            int pickIdx = rng.PickWeighted(weights, RumorSeed.Of(baseSeed, "pick"));
            var picked = passing[pickIdx];
            int assignmentIdx = rng.Pick(picked.PassingAssignments.Count, RumorSeed.Of(baseSeed, "assignment", picked.SituationId));

            result.Outcome = WandererRollOutcome.Happened;
            var decisionKind = result.IsUnseen
                ? WandererDecisionKind.Unseen
                : (picked.Template!.Branches.Any(b => b.Judge) ? WandererDecisionKind.Decide : WandererDecisionKind.Watch);

            result.DecisionKind = decisionKind;
            result.Pick = new WandererPick
            {
                Template = picked.Template!,
                Assignment = picked.PassingAssignments[assignmentIdx],
                Probability = total > 0.0 ? picked.Weight / total : 0.0,
                AssignmentChoices = picked.PassingAssignments.Count,
                DecisionKind = decisionKind
            };

            return result;
        }

        /// <summary>
        /// 照 decider 個性擲非裁決做法（私下或玩家選不插手時共用）。
        /// 回傳擲出的做法與所有做法的權重（裁決做法排除）。
        /// </summary>
        public static SituationDecision RollNonJudgeBranch(
            SituationTemplate template,
            IReadOnlyDictionary<string, SituationRoleFacts>? factsByRole,
            IReadOnlyDictionary<string, string?>? boundHeroes,
            IReadOnlyDictionary<string, string>? unboundReasons,
            TraitProfile deciderTraits,
            double defaultMinBranchWeight,
            IDeterministicRng rng,
            long seed,
            SituationWorldContext? context = null,
            double day = 0.0,
            ISituationHistory? history = null,
            IReadOnlyCollection<string>? nonDerivedHeroIds = null,
            bool forced = false)
        {
            if (template == null) throw new ArgumentNullException(nameof(template));
            if (deciderTraits == null) throw new ArgumentNullException(nameof(deciderTraits));
            if (rng == null) throw new ArgumentNullException(nameof(rng));

            return SituationBranchSelector.Select(
                template,
                factsByRole,
                boundHeroes,
                unboundReasons,
                deciderTraits,
                defaultMinBranchWeight,
                rng,
                seed,
                context,
                template.Id,
                day,
                history,
                nonDerivedHeroIds,
                forced,
                excludeBranch: b => b.Judge ? "judge branch" : null);
        }

        private static WandererCandidateReport EvaluateCandidate(
            SituationTemplate template,
            string playerId,
            List<string> wanderers,
            IPlayerPresenceEvaluator evaluator,
            IDeterministicRng rng,
            long baseSeed,
            int maxAssignments,
            bool forced)
        {
            var report = new WandererCandidateReport
            {
                SituationId = template.Id,
                Weight = Math.Max(0.0, template.Weight),
                Template = template
            };

            string? playerRole = template.Roles
                .Where(r => r.Value.PlayerOnly || string.Equals(r.Key, template.Player?.JudgeRole, StringComparison.OrdinalIgnoreCase))
                .Select(r => r.Key)
                .FirstOrDefault();

            var wandererRoles = template.Roles
                .Where(r => !r.Value.IsDerived && (playerRole == null || !string.Equals(r.Key, playerRole, StringComparison.OrdinalIgnoreCase)))
                .Select(r => r.Key)
                .ToList();

            if (wandererRoles.Count != 2)
            {
                report.StructuralReason = $"needs exactly 2 wanderer roles, has {wandererRoles.Count}";
                return report;
            }

            var assignments = new List<Dictionary<string, string>>();
            for (int a = 0; a < wanderers.Count; a++)
            {
                for (int b = 0; b < wanderers.Count; b++)
                {
                    if (a == b) continue;
                    var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        [wandererRoles[0]] = wanderers[a],
                        [wandererRoles[1]] = wanderers[b]
                    };
                    if (!string.IsNullOrEmpty(playerRole) && !string.IsNullOrEmpty(playerId))
                    {
                        dict[playerRole] = playerId;
                    }
                    assignments.Add(dict);
                }
            }

            if (assignments.Count == 0)
            {
                report.StructuralReason = "no wanderer pair available";
                return report;
            }

            if (assignments.Count > maxAssignments)
            {
                long shuffleSeed = RumorSeed.Of(baseSeed, "shuffle", template.Id);
                for (int i = assignments.Count - 1; i > 0; i--)
                {
                    int j = rng.Pick(i + 1, RumorSeed.Of(shuffleSeed, i));
                    var tmp = assignments[i];
                    assignments[i] = assignments[j];
                    assignments[j] = tmp;
                }
                assignments = assignments.Take(maxAssignments).ToList();
            }

            IReadOnlyList<SituationConditionDef> conditions = template.Conditions ?? new List<SituationConditionDef>();

            foreach (var assignment in assignments)
            {
                report.AssignmentsTried++;
                var eval = evaluator.Evaluate(template, conditions, assignment, forced) ?? new PlayerPresenceEvaluation();

                var failures = (eval.ConditionResults ?? Array.Empty<ConditionEvaluationResult>())
                    .Where(r => r != null && !r.Ok)
                    .Select(r => (Label: string.IsNullOrEmpty(r.Label) ? (string.IsNullOrEmpty(r.Type) ? "unknown" : r.Type) : r.Label, r.Detail))
                    .ToList();

                if (failures.Count == 0)
                {
                    report.AssignmentsPassed++;
                    report.PassingAssignments.Add(new WandererAssignment
                    {
                        NonDerivedRoleHeroIds = assignment,
                        Evaluation = eval
                    });
                    continue;
                }

                foreach (var f in failures)
                {
                    report.FailedByLabel[f.Label] = report.FailedByLabel.TryGetValue(f.Label, out int n) ? n + 1 : 1;
                    if (!report.FirstFailureDetail.ContainsKey(f.Label))
                    {
                        report.FirstFailureDetail[f.Label] = f.Detail ?? string.Empty;
                    }
                }
            }

            return report;
        }
    }
}
