using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using VividWorld.Core.Grudges;

namespace VividWorld.Core.Situations
{
    /// <summary>玩家入局的日誌行。純字串，由 Module 印。每個沒擲、沒中、被排除的都寫出原因。</summary>
    public static class PlayerPresenceLogFormatter
    {
        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        public static string RoleToken(PlayerPresenceRole role)
        {
            return role.ToString().ToLowerInvariant();
        }

        /// <summary>擲骰一行：在哪、可用的領主幾位、冷卻剩幾天、機率與擲出的數、結果。</summary>
        public static string FormatRoll(PlayerPresenceRollResult r)
        {
            if (r == null) throw new ArgumentNullException(nameof(r));

            string where = string.IsNullOrEmpty(r.Place?.SettlementId)
                ? "nowhere"
                : $"{r.Place!.SettlementId} ({r.Place.SettlementKind ?? "null"})";

            string cooldown;
            if (!r.LastOccurrenceDay.HasValue)
            {
                cooldown = "cooldown none";
            }
            else if (r.CooldownRemainingDays.HasValue && r.CooldownRemainingDays.Value > 0.0)
            {
                cooldown = string.Format(Inv, "cooldown {0:0.0}d left (last on day {1:0.0}, {2}d)",
                    r.CooldownRemainingDays.Value, r.LastOccurrenceDay.Value, r.CooldownDays.ToString("0.##", Inv));
            }
            else
            {
                cooldown = string.Format(Inv, "cooldown over (last on day {0:0.0}, {1}d)",
                    r.LastOccurrenceDay.Value, r.CooldownDays.ToString("0.##", Inv));
            }

            string chance = r.Forced
                ? "forced (cooldown and chance ignored)"
                : r.Roll.HasValue
                    ? string.Format(Inv, "chance {0:0.###}, roll {1:0.###}", r.Chance, r.Roll.Value)
                    : string.Format(Inv, "chance {0:0.###}, not rolled", r.Chance);

            string outcome;
            switch (r.Outcome)
            {
                case PlayerPresenceOutcome.Happened:
                    outcome = r.Pick != null
                        ? $"happened: {r.Pick.Template.Id} as {RoleToken(r.Pick.Role)}"
                        : "happened";
                    break;
                case PlayerPresenceOutcome.ChanceMissed:
                    outcome = "missed";
                    break;
                default:
                    outcome = $"not rolled: {r.Reason ?? r.Outcome.ToString()}";
                    if (r.Outcome == PlayerPresenceOutcome.NoCandidate) outcome = $"no situation: {r.Reason}";
                    break;
            }

            return string.Format(Inv, "PlayerPresence[{0}] {1} at {2}: {3} lord(s) or wanderer(s), {4}, {5} -> {6}",
                r.TriggerId, r.Moment.ToString().ToLowerInvariant(), where, r.AvailableHeroCount, cooldown, chance, outcome);
        }

        /// <summary>挑候選：每個情境 × 身分一行，被排除的寫哪一條條件卡了幾組、第一次卡住時的說明。</summary>
        public static List<string> FormatCandidates(PlayerPresenceRollResult r)
        {
            var lines = new List<string>();
            if (r?.Candidates == null) return lines;

            foreach (var c in r.Candidates)
            {
                string head = $"{c.SituationId} as {RoleToken(c.Role)}";
                if (c.PlayerRoleName != null) head += $" ({c.PlayerRoleName})";

                string skipped = c.SkippedConditions.Count > 0
                    ? $", not checked with the player: {string.Join(", ", c.SkippedConditions)}"
                    : string.Empty;

                if (c.StructuralReason != null)
                {
                    lines.Add($"  excluded {head}: {c.StructuralReason}");
                    continue;
                }

                if (c.Passed)
                {
                    string weightNote = c.Weight > 0.0 ? string.Empty : " (weight 0, never picked)";
                    lines.Add(string.Format(Inv, "  candidate {0}: {1} of {2} assignment(s) passed, weight {3}{4}{5}",
                        head, c.AssignmentsPassed, c.AssignmentsTried, c.Weight.ToString("0.##", Inv), skipped, weightNote));
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
                lines.Add(string.Format(Inv, "  excluded {0}: 0 of {1} assignment(s) passed{2}; {3}",
                    head, c.AssignmentsTried, skipped, string.Join("; ", reasons)));
            }

            if (r.Pick != null)
            {
                string roles = string.Join(", ", r.Pick.Assignment.NonDerivedRoleHeroIds
                    .OrderBy(kv => kv.Key, StringComparer.Ordinal)
                    .Select(kv => $"{kv.Key}={kv.Value}"));
                lines.Add(string.Format(Inv, "  picked {0} as {1} (p={2:0.00}, 1 of {3} assignment(s)): {4}",
                    r.Pick.Template.Id, RoleToken(r.Pick.Role), r.Pick.Probability, r.Pick.AssignmentChoices, roles));
            }

            return lines;
        }

        /// <summary>恩怨過濾：照記的與因為從玩家出發而不記的。</summary>
        public static string FormatGrudgeFilter(string situationId, string? branchId, PlayerGrudgeFilterResult filter)
        {
            string Describe(IEnumerable<SituationGrudgeDef> list)
            {
                var parts = list.Select(g =>
                {
                    string flags = string.Empty;
                    if (g.LedgerOnly) flags += " ledger-only";
                    if (!string.IsNullOrEmpty(g.Escalate)) flags += $" escalate:{g.Escalate}";
                    return string.Format(Inv, "{0}->{1} {2:+0.##;-0.##;0}{3}", g.From, g.To, g.Amount, flags);
                }).ToList();
                return parts.Count == 0 ? "none" : string.Join(", ", parts);
            }

            filter ??= new PlayerGrudgeFilterResult();
            return string.Format(Inv, "  player grudges for {0}/{1}: kept {2} ({3}); not kept, from the player: {4} ({5})",
                situationId, branchId ?? "none", filter.Kept.Count, Describe(filter.Kept), filter.Dropped.Count, Describe(filter.Dropped));
        }

        /// <summary>一個做法的代價方向。</summary>
        public static string FormatDirections(string branchId, IReadOnlyList<PlayerChoiceDirection> directions)
        {
            string body = directions == null || directions.Count == 0
                ? "none"
                : string.Join(", ", directions.Select(d => d.ToString()));
            return $"  directions for {branchId}: {body}";
        }

        /// <summary>做決定一行：列出的做法、沒列的與原因、玩家選了哪個。</summary>
        public static string FormatChoice(string situationId, IReadOnlyList<PlayerChoiceOption> options, string? chosenBranchId)
        {
            options ??= Array.Empty<PlayerChoiceOption>();
            string offered = string.Join(", ", options.Where(o => o.Offered).Select(o => o.Branch.Id));
            var notOffered = options.Where(o => !o.Offered).Select(o => $"{o.Branch.Id} ({o.ExcludedReason})").ToList();
            string notOfferedStr = notOffered.Count == 0 ? string.Empty : $"; not offered: {string.Join(", ", notOffered)}";
            return $"  player choice for {situationId}: offered {(offered.Length == 0 ? "none" : offered)}{notOfferedStr}; chose {chosenBranchId ?? "none"}";
        }

        /// <summary>一項數值改變前後（聲望、影響力、士氣）。前後值讀不到時傳 null，日誌照實寫沒讀。</summary>
        public static string FormatValueChange(string what, double requested, double? before, double? after)
        {
            string change = before.HasValue && after.HasValue
                ? string.Format(Inv, "{0:0.##} -> {1:0.##}", before.Value, after.Value)
                : "before/after not read";
            return string.Format(Inv, "  player {0} {1:+0.##;-0.##;0}: {2}", what, requested, change);
        }

        /// <summary>個性經驗與等級改變前後。經驗值前後讀不到時傳 null。</summary>
        public static string FormatTraitChange(string trait, int xp, int? xpBefore, int? xpAfter, int levelBefore, int levelAfter)
        {
            string xpPart = xpBefore.HasValue && xpAfter.HasValue
                ? string.Format(Inv, "xp {0} -> {1}", xpBefore.Value, xpAfter.Value)
                : "xp before/after not read";
            return string.Format(Inv, "  player trait {0} {1:+0;-0;0} xp: {2}, level {3} -> {4}",
                trait, xp, xpPart, levelBefore, levelAfter);
        }

        /// <summary>沒動的數值與原因（例：玩家沒有王國，影響力不動）。</summary>
        public static string FormatValueSkipped(string what, string reason)
        {
            return $"  player {what} not changed: {reason}";
        }

        /// <summary>寫進紀事一行。</summary>
        public static string FormatChronicle(string eventId, bool recorded, string? reason = null)
        {
            return recorded
                ? $"  chronicle: recorded {eventId} as seen first-hand"
                : $"  chronicle: {eventId} not recorded ({reason ?? "unknown"})";
        }
    }
}
