using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using VividWorld.Core.Config;

namespace VividWorld.Core.Situations
{
    /// <summary>
    /// 流浪者事件的日誌格式化工具。純字串，由 Module 印出。
    /// 包含擲骰結果、候選排除與條件、做法決策、職位檢查與 dev 狀態字串。
    /// </summary>
    public static class WandererEventLogFormatter
    {
        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        public static string DecisionKindToken(WandererDecisionKind kind)
        {
            switch (kind)
            {
                case WandererDecisionKind.Decide: return "decide";
                case WandererDecisionKind.Watch: return "watch";
                case WandererDecisionKind.Unseen: return "unseen";
                default: return kind.ToString().ToLowerInvariant();
            }
        }

        /// <summary>擲骰一行：觸發、可用流浪者幾位、冷卻剩幾天、間隔剩幾天、機率與擲出的數、私下數、結果。</summary>
        public static string FormatRoll(WandererRollResult r)
        {
            if (r == null) throw new ArgumentNullException(nameof(r));

            string trigger = r.Trigger == WandererTrigger.Battle ? "battle" : "daily";

            string cooldown;
            bool hasCooldownRecord = r.Trigger == WandererTrigger.Battle ? r.LastBattleDay.HasValue : r.LastDailyDay.HasValue;
            if (!hasCooldownRecord)
            {
                cooldown = "cooldown none";
            }
            else if (r.CooldownRemainingDays.HasValue && r.CooldownRemainingDays.Value > 0.0)
            {
                double lastDay = (r.Trigger == WandererTrigger.Battle ? r.LastBattleDay : r.LastDailyDay) ?? 0.0;
                cooldown = string.Format(Inv, "cooldown {0:0.0}d left (last on day {1:0.0}, {2}d)",
                    r.CooldownRemainingDays.Value, lastDay, r.CooldownDays.ToString("0.##", Inv));
            }
            else
            {
                double lastDay = (r.Trigger == WandererTrigger.Battle ? r.LastBattleDay : r.LastDailyDay) ?? 0.0;
                cooldown = string.Format(Inv, "cooldown over (last on day {0:0.0}, {1}d)",
                    lastDay, r.CooldownDays.ToString("0.##", Inv));
            }

            string gap;
            if (!r.LastAnyDay.HasValue)
            {
                gap = "gap none";
            }
            else if (r.GapRemainingDays.HasValue && r.GapRemainingDays.Value > 0.0)
            {
                gap = string.Format(Inv, "gap {0:0.0}d left (last on day {1:0.0}, min {2}d)",
                    r.GapRemainingDays.Value, r.LastAnyDay.Value, r.MinGapDays.ToString("0.##", Inv));
            }
            else
            {
                gap = string.Format(Inv, "gap over (last on day {0:0.0}, min {1}d)",
                    r.LastAnyDay.Value, r.MinGapDays.ToString("0.##", Inv));
            }

            string chance = r.Forced
                ? "forced (cooldown, gap and chance ignored)"
                : r.Roll.HasValue
                    ? string.Format(Inv, "chance {0:0.###}, roll {1:0.###}", r.Chance, r.Roll.Value)
                    : string.Format(Inv, "chance {0:0.###}, not rolled", r.Chance);

            string unseenPart = string.Empty;
            if (r.Trigger == WandererTrigger.Daily && r.Outcome == WandererRollOutcome.Happened)
            {
                unseenPart = r.UnseenRoll.HasValue
                    ? string.Format(Inv, ", unseen roll {0:0.###} (share {1:0.###}) -> {2}", r.UnseenRoll.Value, r.UnseenShare, r.IsUnseen ? "unseen" : "seen")
                    : (r.IsUnseen ? ", forced unseen" : ", forced seen");
            }

            string outcome;
            switch (r.Outcome)
            {
                case WandererRollOutcome.Happened:
                    outcome = r.Pick != null
                        ? $"happened: {r.Pick.Template.Id} as {DecisionKindToken(r.Pick.DecisionKind)}"
                        : "happened";
                    break;
                case WandererRollOutcome.ChanceMissed:
                    outcome = "missed";
                    break;
                default:
                    outcome = $"not rolled: {r.Reason ?? r.Outcome.ToString()}";
                    if (r.Outcome == WandererRollOutcome.NoCandidate) outcome = $"no situation: {r.Reason}";
                    break;
            }

            return string.Format(Inv, "WandererEvent[{0}] {1} wanderer(s), {2}, {3}, {4}{5} -> {6}",
                trigger, r.AvailableWandererCount, cooldown, gap, chance, unseenPart, outcome);
        }

        /// <summary>候選情境評估日誌：每個情境一行，沒過的寫出條件標籤、次數與第一次失敗說明。</summary>
        public static List<string> FormatCandidates(WandererRollResult r)
        {
            var lines = new List<string>();
            if (r?.Candidates == null) return lines;

            foreach (var c in r.Candidates)
            {
                string head = c.SituationId;

                if (c.StructuralReason != null)
                {
                    lines.Add($"  excluded {head}: {c.StructuralReason}");
                    continue;
                }

                if (c.Passed)
                {
                    string weightNote = c.Weight > 0.0 ? string.Empty : " (weight 0, never picked)";
                    lines.Add(string.Format(Inv, "  candidate {0}: {1} of {2} assignment(s) passed, weight {3}{4}",
                        head, c.AssignmentsPassed, c.AssignmentsTried, c.Weight.ToString("0.##", Inv), weightNote));
                    continue;
                }

                var reasons = c.FailedByLabel
                    .OrderByDescending(kv => kv.Value)
                    .ThenBy(kv => kv.Key, StringComparer.Ordinal)
                    .Select(kv =>
                    {
                        c.FirstFailureDetail.TryGetValue(kv.Key, out var detail);
                        return string.IsNullOrEmpty(detail) ? $"{kv.Key} x{kv.Value}" : $"{kv.Key} x{kv.Value} ({detail})";
                    });
                lines.Add(string.Format(Inv, "  excluded {0}: 0 of {1} assignment(s) passed; {2}",
                    head, c.AssignmentsTried, string.Join("; ", reasons)));
            }

            if (r.Pick != null)
            {
                string roles = string.Join(", ", r.Pick.Assignment.NonDerivedRoleHeroIds
                    .OrderBy(kv => kv.Key, StringComparer.Ordinal)
                    .Select(kv => $"{kv.Key}={kv.Value}"));
                lines.Add(string.Format(Inv, "  picked {0} as {1} (p={2:0.00}, 1 of {3} assignment(s)): {4}",
                    r.Pick.Template.Id, DecisionKindToken(r.Pick.DecisionKind), r.Pick.Probability, r.Pick.AssignmentChoices, roles));
            }

            return lines;
        }

        /// <summary>不插手或私下時分支擲骰結果日誌：列出每個做法的權重、機率與挑中哪一個。</summary>
        public static string FormatDecision(string situationId, SituationDecision d)
        {
            if (d == null) return $"  decision for {situationId}: none";
            var parts = new List<string>();
            foreach (var b in d.Branches)
            {
                if (b.IsExcluded)
                {
                    parts.Add($"{b.BranchId} (excluded: {b.ExcludedReason})");
                }
                else
                {
                    parts.Add(string.Format(Inv, "{0} (w={1:0.##}, p={2:0.00})", b.BranchId, b.Weight, b.Probability));
                }
            }
            return string.Format(Inv, "  decision for {0}: {1}; selected {2}",
                situationId, string.Join(", ", parts), d.SelectedBranchId ?? "none");
        }

        /// <summary>被排除的流浪者與原因。</summary>
        public static string FormatExcludedWanderers(IReadOnlyList<(string HeroId, string Reason)>? excluded)
        {
            if (excluded == null || excluded.Count == 0) return "  excluded wanderers: none";
            var items = excluded.Select(e => $"{e.HeroId} ({e.Reason})");
            return $"  excluded {excluded.Count} wanderer(s): {string.Join(", ", items)}";
        }

        /// <summary>換回職位每日檢查日誌：今天與昨天四個職位、每筆紀錄狀態與結清動作。</summary>
        public static List<string> FormatRoleCheck(
            WandererRoleCheckDayResult r,
            IReadOnlyDictionary<string, string?>? todayRoles,
            IReadOnlyDictionary<string, string?>? yesterdayRoles)
        {
            var lines = new List<string>();

            string FormatRoles(IReadOnlyDictionary<string, string?>? dict)
            {
                if (dict == null || dict.Count == 0) return "empty";
                return string.Join(", ", dict
                    .OrderBy(kv => kv.Key, StringComparer.Ordinal)
                    .Select(kv => $"{kv.Key}={(string.IsNullOrEmpty(kv.Value) ? "none" : kv.Value)}"));
            }

            lines.Add($"  roles today: {FormatRoles(todayRoles)}");
            lines.Add($"  roles yesterday: {FormatRoles(yesterdayRoles)}");

            if (r?.Evaluations != null)
            {
                foreach (var ev in r.Evaluations)
                {
                    string statusStr;
                    switch (ev.Status)
                    {
                        case WandererRoleSwapStatus.Active: statusStr = "active"; break;
                        case WandererRoleSwapStatus.Reverted: statusStr = "reverted"; break;
                        case WandererRoleSwapStatus.Expired: statusStr = "expired"; break;
                        case WandererRoleSwapStatus.PartyLeft: statusStr = "party_left"; break;
                        case WandererRoleSwapStatus.ReplacedByThirdParty: statusStr = "replaced_by_third_party"; break;
                        default: statusStr = ev.Status.ToString().ToLowerInvariant(); break;
                    }
                    lines.Add(string.Format(Inv, "  swap record {0} ({1}->{2}, day {3:0.0}, {4}): {5} ({6})",
                        ev.Swap.Role, ev.Swap.From, ev.Swap.To, ev.Swap.Day, ev.Swap.Source, statusStr, ev.Reason));
                }
            }

            if (r?.Reverts != null)
            {
                foreach (var rev in r.Reverts)
                {
                    lines.Add(string.Format(Inv, "  revert action: {0} lost {1} back to {2} (day {3:0.0}->{4:0.0}, relation {5}, morale {6:0.0}, complaint queued)",
                        rev.ReplacedHeroId, rev.Role, rev.OriginalHeroId, rev.SwapDay, rev.RevertDay, rev.RelationChange, rev.MoraleChange));
                }
            }

            return lines;
        }

        /// <summary>
        /// 開發者工具狀態字串：開關、三個日子與冷卻剩幾天、換人紀錄、四個職位、待開的抱怨。
        /// </summary>
        public static string FormatStatus(
            bool enabled,
            double today,
            double? lastBattleDay,
            double? lastDailyDay,
            double? lastAnyDay,
            WandererEventsConfig config,
            WandererRoleData roleData)
        {
            config ??= new WandererEventsConfig();
            var lines = new List<string>();
            lines.Add($"Wanderer Events Status: enabled={enabled}");

            double? battleCooldownLeft = lastBattleDay.HasValue ? Math.Max(0.0, config.BattleCooldownDays - (today - lastBattleDay.Value)) : null;
            double? dailyCooldownLeft = lastDailyDay.HasValue ? Math.Max(0.0, config.DailyCooldownDays - (today - lastDailyDay.Value)) : null;
            double? gapLeft = lastAnyDay.HasValue ? Math.Max(0.0, config.MinGapDays - (today - lastAnyDay.Value)) : null;

            lines.Add(string.Format(Inv, "  Battle: lastDay={0}, cooldownLeft={1}d (total {2}d, chance {3})",
                lastBattleDay.HasValue ? lastBattleDay.Value.ToString("0.0", Inv) : "none",
                battleCooldownLeft.HasValue ? battleCooldownLeft.Value.ToString("0.0", Inv) : "none",
                config.BattleCooldownDays, config.BattleChance));

            lines.Add(string.Format(Inv, "  Daily: lastDay={0}, cooldownLeft={1}d (total {2}d, chance {3}, unseenShare {4})",
                lastDailyDay.HasValue ? lastDailyDay.Value.ToString("0.0", Inv) : "none",
                dailyCooldownLeft.HasValue ? dailyCooldownLeft.Value.ToString("0.0", Inv) : "none",
                config.DailyCooldownDays, config.DailyChance, config.UnseenShare));

            lines.Add(string.Format(Inv, "  MinGap: lastAnyDay={0}, gapLeft={1}d (min {2}d)",
                lastAnyDay.HasValue ? lastAnyDay.Value.ToString("0.0", Inv) : "none",
                gapLeft.HasValue ? gapLeft.Value.ToString("0.0", Inv) : "none",
                config.MinGapDays));

            if (roleData != null)
            {
                string yesterdayRoles = roleData.Yesterday != null && roleData.Yesterday.Count > 0
                    ? string.Join(", ", roleData.Yesterday.OrderBy(kv => kv.Key).Select(kv => $"{kv.Key}={kv.Value}"))
                    : "empty";
                lines.Add($"  Roles yesterday: {yesterdayRoles}");

                lines.Add($"  Active swap records: {(roleData.Swaps?.Count ?? 0)}");
                if (roleData.Swaps != null)
                {
                    foreach (var s in roleData.Swaps)
                    {
                        lines.Add(string.Format(Inv, "    {0}: {1} -> {2} on day {3:0.0} ({4})", s.Role, s.From, s.To, s.Day, s.Source));
                    }
                }

                if (roleData.PendingComplaint != null)
                {
                    var p = roleData.PendingComplaint;
                    lines.Add(string.Format(Inv, "  Pending complaint: hero={0}, role={1}, day={2:0.0}, relation={3}->{4}",
                        p.HeroId, p.Role, p.Day, p.RelationBefore, p.RelationAfter));
                }
                else
                {
                    lines.Add("  Pending complaint: none");
                }
            }

            return string.Join("\n", lines);
        }
    }
}
