using System;
using System.Collections.Generic;
using System.Linq;
using VividWorld.Core.Config;
using VividWorld.Core.Util;

namespace VividWorld.Core.Situations
{
    /// <summary>玩家在情境裡的身分。</summary>
    public enum PlayerPresenceRole
    {
        /// <summary>兩位 NPC 之間發生，玩家看到。</summary>
        Bystander,
        /// <summary>玩家擔任原本照個性擲分支的角色，改由玩家選做法。</summary>
        Decider,
        /// <summary>話是私下講給玩家聽的；分支照講話那一方的個性擲。</summary>
        Listener
    }

    /// <summary>這次擲骰是在什麼時候擲的；進種子，同一天進城與每天的擲骰才不會擲出同一個數。</summary>
    public enum PlayerPresenceMoment
    {
        Entry,
        Daily,
        Dev
    }

    public enum PlayerPresenceOutcome
    {
        Disabled,
        PlaceNotAllowed,
        TooFewHeroes,
        Cooldown,
        ChanceMissed,
        NoCandidate,
        Happened
    }

    /// <summary>玩家所在的地方。</summary>
    public sealed class PlayerPresencePlace
    {
        public string? SettlementId;
        public string? SettlementKind; // "town" / "castle" / "village" / "other" / null
    }

    /// <summary>
    /// 觸發是可替換的一塊：什麼地方能擲、用哪一組設定（機率、冷卻、開關）。
    /// 從哪群人挑角色由呼叫端決定，交進 <see cref="PlayerPresenceRollInput.AvailableHeroIds"/>。
    /// 每種觸發各有自己的設定鍵與冷卻，彼此不共用。
    /// </summary>
    public interface IPlayerPresenceTrigger
    {
        /// <summary>進種子與日誌；不同觸發的擲骰互不相干。</summary>
        string Id { get; }

        PlayerPresenceConfig Settings(VividWorldConfig? config);

        /// <summary>null＝這裡可以擲；否則是不擲的原因。</summary>
        string? PlaceRejection(PlayerPresencePlace place);
    }

    /// <summary>玩家在城鎮或城堡、角色從城裡的領主挑。野外、村莊不算。</summary>
    public sealed class TownPresenceTrigger : IPlayerPresenceTrigger
    {
        public static readonly TownPresenceTrigger Instance = new TownPresenceTrigger();

        public string Id => "town";

        public PlayerPresenceConfig Settings(VividWorldConfig? config)
        {
            return config?.Situations?.PlayerPresence ?? new PlayerPresenceConfig();
        }

        public string? PlaceRejection(PlayerPresencePlace place)
        {
            if (place == null || string.IsNullOrEmpty(place.SettlementId)) return "not in a settlement";
            string kind = place.SettlementKind ?? "null";
            if (string.Equals(kind, "town", StringComparison.OrdinalIgnoreCase)) return null;
            if (string.Equals(kind, "castle", StringComparison.OrdinalIgnoreCase)) return null;
            return $"{place.SettlementId} is {kind}, need town/castle";
        }
    }

    /// <summary>dev 工具用：指定情境與身分，並無視冷卻與機率。</summary>
    public sealed class PlayerPresenceForce
    {
        public string? SituationId;
        public PlayerPresenceRole? Role;
    }

    public sealed class PlayerPresenceRollInput
    {
        public string PlayerHeroId = string.Empty;
        public PlayerPresencePlace Place = new PlayerPresencePlace();

        /// <summary>在場、可以擔任角色的英雄（不含玩家）。由觸發的呼叫端挑好。</summary>
        public IReadOnlyList<string> AvailableHeroIds = Array.Empty<string>();

        public double Today;

        /// <summary>這個觸發上一次真的發生是第幾天；null＝沒有冷卻（舊戰役沒有這筆也是 null）。</summary>
        public double? LastOccurrenceDay;

        public long CampaignSeed;
        public PlayerPresenceMoment Moment;

        /// <summary>非 null＝dev 強制：冷卻與機率不看；指定了情境或身分就只挑那些。</summary>
        public PlayerPresenceForce? Force;
    }

    /// <summary>
    /// 一組角色指派的條件求值結果。求值本身交給呼叫端（Module 走 NPC 情境原本那一套：讀英雄事實、
    /// 推導 host／ruler、評估條件），這裡只決定要評估哪些條件、指派誰當哪個角色、怎麼挑。
    /// </summary>
    public sealed class PlayerPresenceEvaluation
    {
        public IReadOnlyList<ConditionEvaluationResult> ConditionResults = Array.Empty<ConditionEvaluationResult>();

        /// <summary>所有角色（含推導角色）綁到的英雄；沒綁到的值是 null。</summary>
        public IReadOnlyDictionary<string, string?> BoundHeroIds = new Dictionary<string, string?>();

        /// <summary>呼叫端自己的求值結果，挑中之後原樣交回去執行，不必再算一次。</summary>
        public object? State;
    }

    public interface IPlayerPresenceEvaluator
    {
        /// <summary>
        /// 照情境原本的規則評估 <paramref name="conditions"/>（已經扣掉玩家在場時不檢查的那幾條）。
        /// 推導角色照原本的規則推導，而原本的規則永遠不會推導成玩家。
        /// </summary>
        PlayerPresenceEvaluation Evaluate(
            SituationTemplate template,
            IReadOnlyList<SituationConditionDef> conditions,
            IReadOnlyDictionary<string, string> nonDerivedRoleHeroIds,
            bool forced);
    }

    /// <summary>一個候選＝一個情境 × 一種身分；底下試過好幾組角色指派。</summary>
    public sealed class PlayerPresenceCandidateReport
    {
        public string SituationId = string.Empty;
        public PlayerPresenceRole Role;
        public string? PlayerRoleName;
        public double Weight;
        public int AssignmentsTried;
        public int AssignmentsPassed;

        /// <summary>這個身分整個不成立的原因（角色不夠、沒人可以當某個角色）；null＝有試指派。</summary>
        public string? StructuralReason;

        /// <summary>玩家在場時不檢查的條件。</summary>
        public List<string> SkippedConditions = new List<string>();

        /// <summary>沒過的條件（Label）→ 幾組指派卡在它。</summary>
        public Dictionary<string, int> FailedByLabel = new Dictionary<string, int>(StringComparer.Ordinal);

        /// <summary>每個沒過的條件第一次出現時的完整說明。</summary>
        public Dictionary<string, string> FirstFailureDetail = new Dictionary<string, string>(StringComparer.Ordinal);

        public bool Passed => StructuralReason == null && AssignmentsPassed > 0;

        public List<PlayerPresenceAssignment> PassingAssignments = new List<PlayerPresenceAssignment>();
        internal SituationTemplate? Template;
    }

    public sealed class PlayerPresenceAssignment
    {
        public Dictionary<string, string> NonDerivedRoleHeroIds = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        public PlayerPresenceEvaluation Evaluation = new PlayerPresenceEvaluation();
    }

    public sealed class PlayerPresencePick
    {
        public SituationTemplate Template = null!;
        public PlayerPresenceRole Role;

        /// <summary>玩家綁的角色；旁觀時 null。</summary>
        public string? PlayerRoleName;

        public PlayerPresenceAssignment Assignment = null!;
        public double Probability;
        public int AssignmentChoices;
    }

    public sealed class PlayerPresenceRollResult
    {
        public string TriggerId = string.Empty;
        public PlayerPresenceMoment Moment;
        public PlayerPresencePlace Place = new PlayerPresencePlace();
        public PlayerPresenceOutcome Outcome;

        /// <summary>沒擲或沒發生的原因；發生了就是 null。</summary>
        public string? Reason;

        public bool Forced;
        public int AvailableHeroCount;
        public double? LastOccurrenceDay;
        public double CooldownDays;

        /// <summary>冷卻還剩幾天；不在冷卻中是 0，沒有紀錄是 null。</summary>
        public double? CooldownRemainingDays;

        public double Chance;

        /// <summary>機率擲出的數；沒擲到這一步（或強制）是 null。</summary>
        public double? Roll;

        public List<PlayerPresenceCandidateReport> Candidates = new List<PlayerPresenceCandidateReport>();
        public PlayerPresencePick? Pick;
    }

    public static class PlayerPresenceRoller
    {
        public static PlayerPresenceRollResult Roll(
            PlayerPresenceRollInput input,
            IPlayerPresenceTrigger trigger,
            VividWorldConfig? config,
            IEnumerable<SituationTemplate> templates,
            IPlayerPresenceEvaluator evaluator,
            IDeterministicRng rng)
        {
            if (input == null) throw new ArgumentNullException(nameof(input));
            if (trigger == null) throw new ArgumentNullException(nameof(trigger));
            if (evaluator == null) throw new ArgumentNullException(nameof(evaluator));
            if (rng == null) throw new ArgumentNullException(nameof(rng));

            var settings = trigger.Settings(config);
            string playerId = input.PlayerHeroId ?? string.Empty;
            var heroes = (input.AvailableHeroIds ?? Array.Empty<string>())
                .Where(h => !string.IsNullOrEmpty(h) && !string.Equals(h, playerId, StringComparison.Ordinal))
                .Distinct(StringComparer.Ordinal)
                .OrderBy(h => h, StringComparer.Ordinal)
                .ToList();

            var result = new PlayerPresenceRollResult
            {
                TriggerId = trigger.Id,
                Moment = input.Moment,
                Place = input.Place ?? new PlayerPresencePlace(),
                Forced = input.Force != null,
                AvailableHeroCount = heroes.Count,
                LastOccurrenceDay = input.LastOccurrenceDay,
                CooldownDays = settings.CooldownDays,
                Chance = settings.Chance
            };

            if (input.LastOccurrenceDay.HasValue)
            {
                double elapsed = input.Today - input.LastOccurrenceDay.Value;
                result.CooldownRemainingDays = Math.Max(0.0, settings.CooldownDays - elapsed);
            }

            if (!settings.Enabled)
            {
                result.Outcome = PlayerPresenceOutcome.Disabled;
                result.Reason = "disabled in settings";
                return result;
            }

            string? placeRejection = trigger.PlaceRejection(result.Place);
            if (placeRejection != null)
            {
                result.Outcome = PlayerPresenceOutcome.PlaceNotAllowed;
                result.Reason = placeRejection;
                return result;
            }

            if (heroes.Count < 2)
            {
                result.Outcome = PlayerPresenceOutcome.TooFewHeroes;
                result.Reason = $"{heroes.Count} available lord(s) or wanderer(s), need 2";
                return result;
            }

            if (input.Force == null && result.CooldownRemainingDays.HasValue && result.CooldownRemainingDays.Value > 0.0)
            {
                result.Outcome = PlayerPresenceOutcome.Cooldown;
                result.Reason = "in cooldown";
                return result;
            }

            int dayBucket = RumorSeed.DayBucket(input.Today);
            long baseSeed = RumorSeed.Of(input.CampaignSeed, "playerPresence", trigger.Id, input.Moment.ToString(), dayBucket, result.Place.SettlementId ?? string.Empty);

            if (input.Force == null)
            {
                double roll = rng.NextDouble(RumorSeed.Of(baseSeed, "chance"));
                result.Roll = roll;
                if (!(roll < settings.Chance))
                {
                    result.Outcome = PlayerPresenceOutcome.ChanceMissed;
                    result.Reason = "chance missed";
                    return result;
                }
            }

            int maxAssignments = Math.Max(2, config?.Situations?.Scan?.MaxPairsPerSettlement ?? 24);
            bool forced = input.Force != null;

            foreach (var template in templates ?? Enumerable.Empty<SituationTemplate>())
            {
                if (template?.Player == null) continue;
                if (template.DevOnly) continue;
                if (!template.IsEnabled(config)) continue;
                if (!string.Equals(template.Trigger, "direct", StringComparison.OrdinalIgnoreCase)) continue;
                if (input.Force?.SituationId != null && !string.Equals(input.Force.SituationId, template.Id, StringComparison.OrdinalIgnoreCase)) continue;

                foreach (var role in OpenRoles(template.Player))
                {
                    if (input.Force?.Role != null && input.Force.Role.Value != role) continue;

                    var report = EvaluateCandidate(template, role, playerId, heroes, evaluator, rng, baseSeed, maxAssignments, forced);
                    result.Candidates.Add(report);
                }
            }

            var passing = result.Candidates.Where(c => c.Passed && c.Weight > 0.0).ToList();
            if (passing.Count == 0)
            {
                result.Outcome = PlayerPresenceOutcome.NoCandidate;
                result.Reason = result.Candidates.Count == 0 ? "no situation is open to the player" : "no candidate passed";
                return result;
            }

            var weights = passing.Select(c => c.Weight).ToList();
            double total = weights.Sum();
            int pickIdx = rng.PickWeighted(weights, RumorSeed.Of(baseSeed, "pick"));
            var picked = passing[pickIdx];
            int assignmentIdx = rng.Pick(picked.PassingAssignments.Count, RumorSeed.Of(baseSeed, "assignment", picked.SituationId, picked.Role.ToString()));

            result.Outcome = PlayerPresenceOutcome.Happened;
            result.Pick = new PlayerPresencePick
            {
                Template = picked.Template!,
                Role = picked.Role,
                PlayerRoleName = picked.PlayerRoleName,
                Assignment = picked.PassingAssignments[assignmentIdx],
                Probability = total > 0.0 ? picked.Weight / total : 0.0,
                AssignmentChoices = picked.PassingAssignments.Count
            };
            return result;
        }

        /// <summary>照固定順序（旁觀、做決定、聽的人）列出這個情境開放的身分。</summary>
        public static IEnumerable<PlayerPresenceRole> OpenRoles(SituationPlayerDef? player)
        {
            if (player == null) yield break;
            if (player.Bystander) yield return PlayerPresenceRole.Bystander;
            if (!string.IsNullOrEmpty(player.DeciderRole)) yield return PlayerPresenceRole.Decider;
            if (!string.IsNullOrEmpty(player.ListenerRole)) yield return PlayerPresenceRole.Listener;
        }

        /// <summary>玩家擔任的角色之外的那個非推導角色（做決定時就是對方）。找不到回 null。</summary>
        public static string? CounterpartRole(SituationTemplate template, string? playerRoleName)
        {
            if (template == null || string.IsNullOrEmpty(playerRoleName)) return null;
            var others = template.Roles
                .Where(r => !r.Value.IsDerived && !string.Equals(r.Key, playerRoleName, StringComparison.OrdinalIgnoreCase))
                .Select(r => r.Key)
                .ToList();
            return others.Count == 1 ? others[0] : null;
        }

        private static PlayerPresenceCandidateReport EvaluateCandidate(
            SituationTemplate template,
            PlayerPresenceRole role,
            string playerId,
            List<string> heroes,
            IPlayerPresenceEvaluator evaluator,
            IDeterministicRng rng,
            long baseSeed,
            int maxAssignments,
            bool forced)
        {
            var report = new PlayerPresenceCandidateReport
            {
                SituationId = template.Id,
                Role = role,
                Weight = Math.Max(0.0, template.Weight),
                Template = template
            };

            var nonDerived = template.Roles.Where(r => !r.Value.IsDerived).Select(r => r.Key).ToList();
            if (nonDerived.Count != 2)
            {
                report.StructuralReason = $"needs exactly 2 non-derived roles, has {nonDerived.Count}";
                return report;
            }

            var assignments = new List<Dictionary<string, string>>();
            if (role == PlayerPresenceRole.Bystander)
            {
                for (int a = 0; a < heroes.Count; a++)
                {
                    for (int b = 0; b < heroes.Count; b++)
                    {
                        if (a == b) continue;
                        assignments.Add(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                        {
                            [nonDerived[0]] = heroes[a],
                            [nonDerived[1]] = heroes[b]
                        });
                    }
                }
            }
            else
            {
                string? playerRole = role == PlayerPresenceRole.Decider ? template.Player!.DeciderRole : template.Player!.ListenerRole;
                report.PlayerRoleName = playerRole;
                string? other = CounterpartRole(template, playerRole);
                if (playerRole == null || other == null)
                {
                    report.StructuralReason = $"role '{playerRole ?? "null"}' has no counterpart role";
                    return report;
                }
                foreach (var h in heroes)
                {
                    assignments.Add(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        [playerRole] = playerId,
                        [other] = h
                    });
                }
            }

            if (assignments.Count == 0)
            {
                report.StructuralReason = "no lord left for the roles";
                return report;
            }

            // 太多組時穩定洗牌後截斷，跟每日掃描每座聚落的上限同一個數
            if (assignments.Count > maxAssignments)
            {
                long shuffleSeed = RumorSeed.Of(baseSeed, "shuffle", template.Id, role.ToString());
                for (int i = assignments.Count - 1; i > 0; i--)
                {
                    int j = rng.Pick(i + 1, RumorSeed.Of(shuffleSeed, i));
                    var tmp = assignments[i];
                    assignments[i] = assignments[j];
                    assignments[j] = tmp;
                }
                assignments = assignments.Take(maxAssignments).ToList();
            }

            // 玩家擔任角色時，只扣掉資料指定的那幾種條件；旁觀時玩家不是任何角色，條件全部照舊
            IReadOnlyList<SituationConditionDef> conditions = template.Conditions ?? new List<SituationConditionDef>();
            var skip = template.Player?.SkipConditionsWhenPlayer;
            if (role != PlayerPresenceRole.Bystander && skip != null && skip.Count > 0)
            {
                var kept = new List<SituationConditionDef>();
                foreach (var c in conditions)
                {
                    if (c != null && skip.Any(t => string.Equals(t, c.Type, StringComparison.OrdinalIgnoreCase)))
                    {
                        report.SkippedConditions.Add(c.Type);
                        continue;
                    }
                    kept.Add(c!);
                }
                conditions = kept;
            }

            foreach (var assignment in assignments)
            {
                report.AssignmentsTried++;
                var eval = evaluator.Evaluate(template, conditions, assignment, forced) ?? new PlayerPresenceEvaluation();

                var failures = (eval.ConditionResults ?? Array.Empty<ConditionEvaluationResult>())
                    .Where(r => r != null && !r.Ok)
                    .Select(r => (Label: string.IsNullOrEmpty(r.Label) ? (string.IsNullOrEmpty(r.Type) ? "unknown" : r.Type) : r.Label, r.Detail))
                    .ToList();

                // 推導角色永遠不能是玩家；旁觀時玩家不能是任何角色
                foreach (var kv in eval.BoundHeroIds ?? new Dictionary<string, string?>())
                {
                    if (!string.Equals(kv.Value, playerId, StringComparison.Ordinal) || string.IsNullOrEmpty(playerId)) continue;
                    bool derived = template.Roles.TryGetValue(kv.Key, out var def) && def.IsDerived;
                    if (derived || role == PlayerPresenceRole.Bystander)
                    {
                        failures.Add(($"playerBound({kv.Key})", $"role '{kv.Key}' would be the player"));
                    }
                }

                if (failures.Count == 0)
                {
                    report.AssignmentsPassed++;
                    report.PassingAssignments.Add(new PlayerPresenceAssignment
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

    public sealed class PlayerGrudgeFilterResult
    {
        public List<Grudges.SituationGrudgeDef> Kept = new List<Grudges.SituationGrudgeDef>();

        /// <summary>從玩家出發的恩怨：玩家怎麼看別人由玩家自己決定，不替他記。</summary>
        public List<Grudges.SituationGrudgeDef> Dropped = new List<Grudges.SituationGrudgeDef>();
    }

    public static class PlayerGrudgeFilter
    {
        /// <summary>
        /// 玩家擔任做決定或聽的人時：<c>from</c> 是玩家的恩怨不記；<c>to</c> 是玩家的照記
        /// （含只記帳本的與會升級成家族恩怨的）。玩家沒綁任何角色（旁觀）時全部照記。
        /// </summary>
        public static PlayerGrudgeFilterResult Filter(
            IEnumerable<Grudges.SituationGrudgeDef>? grudges,
            IReadOnlyDictionary<string, string?>? boundHeroIds,
            string? playerHeroId)
        {
            var result = new PlayerGrudgeFilterResult();
            if (grudges == null) return result;

            foreach (var g in grudges)
            {
                if (g == null) continue;
                string? fromHero = null;
                bool fromIsPlayer = !string.IsNullOrEmpty(playerHeroId)
                    && boundHeroIds != null
                    && boundHeroIds.TryGetValue(g.From ?? string.Empty, out fromHero)
                    && string.Equals(fromHero, playerHeroId, StringComparison.Ordinal);

                if (fromIsPlayer) result.Dropped.Add(g);
                else result.Kept.Add(g);
            }

            return result;
        }
    }

    public static class PlayerBystanderBranches
    {
        /// <summary>
        /// 玩家旁觀時不擲的做法：有事件、而且每一則都不是公開的。祕密事件不加目擊者，
        /// 玩家站在旁邊就不可能是祕密；祕密系統重做（把公開的行為與藏在裡面的那一塊拆開）之前先這樣擋。
        /// 回傳排除的原因；照常擲時回 null。查不到事件模板的不排除。
        /// </summary>
        public static string? SecretOnlyReason(SituationBranchDef branch, Func<string, Catalog.EventTemplate?> templateByType)
        {
            if (branch?.Events == null || branch.Events.Count == 0 || templateByType == null) return null;

            var secretTypes = new List<string>();
            foreach (var e in branch.Events)
            {
                var template = e == null ? null : templateByType(e.Type);
                if (template == null || template.Origin == Events.EventOrigin.Public) return null;
                secretTypes.Add(e!.Type);
            }

            return $"bystander: only secret events ({string.Join(", ", secretTypes)})";
        }
    }

    public sealed class PlayerChoiceOption
    {
        public SituationBranchDef Branch = null!;
        public bool Offered;

        /// <summary>沒列出來的原因（前提哪一條不成立）。</summary>
        public string? ExcludedReason;
    }

    public static class PlayerChoiceOptions
    {
        /// <summary>
        /// 玩家做決定時列出的做法：情境原本的做法全部列出，不看玩家的個性；
        /// 只有原本就有前提的做法在前提不成立時不列（例：拂袖離席要有城主）。
        /// </summary>
        public static IReadOnlyList<PlayerChoiceOption> List(
            SituationTemplate template,
            IReadOnlyDictionary<string, SituationRoleFacts>? factsByRole,
            IReadOnlyDictionary<string, string?>? boundHeroes,
            IReadOnlyDictionary<string, string>? unboundReasons,
            SituationWorldContext? context = null,
            double day = 0.0,
            ISituationHistory? history = null,
            IReadOnlyCollection<string>? nonDerivedHeroIds = null)
        {
            if (template == null) throw new ArgumentNullException(nameof(template));
            factsByRole ??= new Dictionary<string, SituationRoleFacts>();

            var options = new List<PlayerChoiceOption>();
            foreach (var b in template.Branches ?? new List<SituationBranchDef>())
            {
                if (b == null) continue;
                var results = SituationConditionEvaluator.EvaluateAll(
                    b.Preconditions ?? new List<SituationConditionDef>(),
                    factsByRole,
                    boundHeroes,
                    unboundReasons,
                    template.Id,
                    day,
                    history,
                    nonDerivedHeroIds,
                    context,
                    forced: false);

                var failed = results.Where(r => !r.Ok).ToList();
                options.Add(new PlayerChoiceOption
                {
                    Branch = b,
                    Offered = failed.Count == 0,
                    ExcludedReason = failed.Count == 0 ? null : string.Join("; ", failed.Select(f => f.Detail))
                });
            }

            return options;
        }
    }

    /// <summary>實際要動的玩家數值（資料的數字套上「玩家沒有王國」「對方不同王國」之後）。</summary>
    public sealed class PlayerEffectsPlan
    {
        public double Renown;
        public double Influence;

        /// <summary>影響力被拿掉的原因；資料寫了影響力而且照動時是 null。</summary>
        public string? InfluenceSkippedReason;

        public double Morale;

        /// <summary>個性名 → 經驗值，照固定順序（榮譽、仁慈、勇猛、仗義、理性），不含 0。</summary>
        public List<KeyValuePair<string, int>> Traits = new List<KeyValuePair<string, int>>();

        public bool IsEmpty => Renown == 0.0 && Influence == 0.0 && Morale == 0.0 && Traits.Count == 0;
    }

    public static class PlayerEffectsResolver
    {
        public static readonly IReadOnlyList<string> TraitOrder = new[] { "honor", "mercy", "valor", "generosity", "calculating" };

        public static PlayerEffectsPlan Resolve(
            SituationTemplate template,
            SituationBranchDef branch,
            bool playerHasKingdom,
            bool counterpartSameKingdom)
        {
            var plan = new PlayerEffectsPlan();
            var fx = branch?.PlayerEffects;
            if (fx == null) return plan;

            plan.Renown = fx.Renown;
            plan.Morale = fx.Morale;

            if (fx.Influence != 0.0)
            {
                if (!playerHasKingdom)
                {
                    plan.InfluenceSkippedReason = "player has no kingdom";
                }
                else if (template?.Player != null && template.Player.InfluenceOnlySameKingdom && !counterpartSameKingdom)
                {
                    plan.InfluenceSkippedReason = "other party is not in the player's kingdom";
                }
                else
                {
                    plan.Influence = fx.Influence;
                }
            }

            if (fx.Traits != null)
            {
                foreach (var trait in TraitOrder)
                {
                    if (fx.Traits.TryGetValue(trait, out int xp) && xp != 0)
                    {
                        plan.Traits.Add(new KeyValuePair<string, int>(trait, xp));
                    }
                }
            }

            return plan;
        }
    }

    public enum PlayerChoiceDirectionKind
    {
        /// <summary>某位對玩家的好感（只算會改好感的恩怨，不算只記帳本的）。</summary>
        Affection,
        Renown,
        Influence,
        Morale,
        Trait
    }

    /// <summary>代價方向的一項。只有結構，字串由 Module 用字串表組。</summary>
    public sealed class PlayerChoiceDirection
    {
        public PlayerChoiceDirectionKind Kind;
        public bool Up;

        /// <summary>好感那一項：誰對玩家（角色名與英雄）。</summary>
        public string? Role;
        public string? HeroId;

        /// <summary>個性那一項：個性名。</summary>
        public string? Trait;

        public override string ToString()
        {
            string dir = Up ? "up" : "down";
            switch (Kind)
            {
                case PlayerChoiceDirectionKind.Affection:
                    return $"{Role}({HeroId}) {(Up ? "pleased" : "displeased")}";
                case PlayerChoiceDirectionKind.Trait:
                    return $"{Trait} {dir}";
                default:
                    return $"{Kind.ToString().ToLowerInvariant()} {dir}";
            }
        }
    }

    public static class PlayerChoiceDirections
    {
        /// <summary>
        /// 做法的代價方向：對方好感（只看 <c>to</c> 是玩家、不是只記帳本的恩怨；同一位的幾筆加總）、
        /// 聲望、影響力、士氣、每項個性。影響力與 <see cref="PlayerEffectsResolver"/> 同一套規則，
        /// 顯示的方向與實際會動的一致。
        /// </summary>
        public static IReadOnlyList<PlayerChoiceDirection> Compose(
            SituationTemplate template,
            SituationBranchDef branch,
            IReadOnlyDictionary<string, string?>? boundHeroIds,
            string playerHeroId,
            bool playerHasKingdom,
            bool counterpartSameKingdom)
        {
            var items = new List<PlayerChoiceDirection>();
            if (branch == null) return items;

            var sums = new List<(string Role, string HeroId, double Sum)>();
            foreach (var g in branch.Grudges ?? new List<Grudges.SituationGrudgeDef>())
            {
                if (g == null || g.LedgerOnly) continue;
                string? toHero = null;
                string? fromHero = null;
                if (boundHeroIds == null
                    || !boundHeroIds.TryGetValue(g.To ?? string.Empty, out toHero)
                    || !string.Equals(toHero, playerHeroId, StringComparison.Ordinal))
                {
                    continue;
                }
                if (!boundHeroIds.TryGetValue(g.From ?? string.Empty, out fromHero)
                    || string.IsNullOrEmpty(fromHero)
                    || string.Equals(fromHero, playerHeroId, StringComparison.Ordinal))
                {
                    continue;
                }

                int idx = sums.FindIndex(s => string.Equals(s.Role, g.From, StringComparison.OrdinalIgnoreCase));
                if (idx < 0) sums.Add((g.From!, fromHero!, g.Amount));
                else sums[idx] = (sums[idx].Role, sums[idx].HeroId, sums[idx].Sum + g.Amount);
            }

            foreach (var s in sums)
            {
                if (s.Sum == 0.0) continue;
                items.Add(new PlayerChoiceDirection
                {
                    Kind = PlayerChoiceDirectionKind.Affection,
                    Up = s.Sum > 0.0,
                    Role = s.Role,
                    HeroId = s.HeroId
                });
            }

            var plan = PlayerEffectsResolver.Resolve(template, branch, playerHasKingdom, counterpartSameKingdom);
            if (plan.Renown != 0.0) items.Add(new PlayerChoiceDirection { Kind = PlayerChoiceDirectionKind.Renown, Up = plan.Renown > 0.0 });
            if (plan.Influence != 0.0) items.Add(new PlayerChoiceDirection { Kind = PlayerChoiceDirectionKind.Influence, Up = plan.Influence > 0.0 });
            if (plan.Morale != 0.0) items.Add(new PlayerChoiceDirection { Kind = PlayerChoiceDirectionKind.Morale, Up = plan.Morale > 0.0 });
            foreach (var t in plan.Traits)
            {
                items.Add(new PlayerChoiceDirection { Kind = PlayerChoiceDirectionKind.Trait, Up = t.Value > 0, Trait = t.Key });
            }

            return items;
        }
    }
}
