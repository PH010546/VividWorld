using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace VividWorld.Core.Situations
{
    public static class SituationScanLogFormatter
    {
        public static string FormatDay(double day)
        {
            return (day % 1 == 0)
                ? day.ToString("0", CultureInfo.InvariantCulture)
                : day.ToString("0.0", CultureInfo.InvariantCulture);
        }

        public static string FormatQuotaDetail(QuotaRoll roll)
        {
            if (roll == null) return "no roll";
            if (roll.Fraction <= 1e-9)
            {
                return string.Format(CultureInfo.InvariantCulture, "maxPerDay {0:0.00}, no fractional roll", roll.MaxPerDay);
            }
            if (roll.FractionWon)
            {
                return string.Format(CultureInfo.InvariantCulture, "maxPerDay {0:0.00}, roll {1:0.0000} < {2:0.00} -> +1", roll.MaxPerDay, roll.RollValue, roll.Fraction);
            }
            return string.Format(CultureInfo.InvariantCulture, "maxPerDay {0:0.00}, roll {1:0.0000} >= {2:0.00} -> +0", roll.MaxPerDay, roll.RollValue, roll.Fraction);
        }

        public static string FormatRoleBindings(IReadOnlyDictionary<string, string>? roleHeroIds)
        {
            if (roleHeroIds == null || roleHeroIds.Count == 0) return string.Empty;
            return string.Join(" ", roleHeroIds.Select(kv => $"{kv.Key}={kv.Value}"));
        }

        public static string FormatSummary(
            double day,
            int totalSettlements,
            int settlementsWithPairs,
            int pairsEvaluated,
            int capPerSettlement,
            int candidatesPassed,
            bool truncated,
            int maxCandidates,
            QuotaRoll quotaRoll,
            int triggered,
            int leftOver,
            double elapsedMs,
            bool isDryRun = false)
        {
            string scanText = isDryRun ? "scan (dry run)" : "scan";
            string dayText = FormatDay(day);
            string truncText = truncated ? string.Format(CultureInfo.InvariantCulture, " (truncated at {0})", maxCandidates) : string.Empty;
            string quotaDetail = FormatQuotaDetail(quotaRoll);

            return string.Format(
                CultureInfo.InvariantCulture,
                "Situation {0} day {1}: {2} settlement(s), {3} with >=2 eligible, {4} pair(s) evaluated (cap {5}/settlement), {6} candidate(s) passed{7}, quota {8} ({9}), triggered {10}, left over {11}. {12:0.0} ms",
                scanText,
                dayText,
                totalSettlements,
                settlementsWithPairs,
                pairsEvaluated,
                capPerSettlement,
                candidatesPassed,
                truncText,
                quotaRoll.Quota,
                quotaDetail,
                triggered,
                leftOver,
                elapsedMs);
        }

        public static string FormatDisabled()
        {
            return "Situation scan: disabled (situations.dailyScanEnabled = false)";
        }

        public static string FormatQuotaZero(double day, QuotaRoll quotaRoll)
        {
            return string.Format(
                CultureInfo.InvariantCulture,
                "Situation scan day {0}: quota 0 ({1}), nothing scanned.",
                FormatDay(day),
                FormatQuotaDetail(quotaRoll));
        }

        public static string? FormatRejections(IReadOnlyDictionary<string, int> rejections)
        {
            if (rejections == null || rejections.Count == 0) return null;
            var nonZero = rejections.Where(kv => kv.Value > 0).ToList();
            if (nonZero.Count == 0) return null;

            var sorted = nonZero
                .OrderByDescending(kv => kv.Value)
                .ThenBy(kv => kv.Key, StringComparer.Ordinal)
                .Select(kv => $"{kv.Key} {kv.Value}");

            return "  scan rejections: " + string.Join(", ", sorted);
        }

        public static string? FormatSkippedDevOnly(IReadOnlyList<string> devOnlySituationIds)
        {
            if (devOnlySituationIds == null || devOnlySituationIds.Count == 0) return null;
            return string.Format(
                CultureInfo.InvariantCulture,
                "  scan skipped {0} devOnly situation(s): {1}",
                devOnlySituationIds.Count,
                string.Join(", ", devOnlySituationIds));
        }

        public static string FormatNearMiss(
            string situationId,
            string settlementId,
            IReadOnlyDictionary<string, string> roleHeroIds,
            string failureDetail)
        {
            string bindings = FormatRoleBindings(roleHeroIds);
            string spacer = string.IsNullOrEmpty(bindings) ? string.Empty : " " + bindings;
            return string.Format(
                CultureInfo.InvariantCulture,
                "  near miss: {0} at {1}{2} - {3}",
                situationId,
                settlementId,
                spacer,
                failureDetail);
        }

        public static string FormatPick(
            int slot,
            string situationId,
            string settlementId,
            IReadOnlyDictionary<string, string> roleHeroIds,
            double weight,
            double totalWeight,
            double probability,
            double roll,
            int droppedCount)
        {
            string bindings = FormatRoleBindings(roleHeroIds);
            string spacer = string.IsNullOrEmpty(bindings) ? string.Empty : " " + bindings;
            return string.Format(
                CultureInfo.InvariantCulture,
                "  scan pick #{0}: {1} at {2}{3}, weight {4:0.00} of {5:0.00} ({6:0.0}%), roll {7:0.0000} -> picked; {8} candidate(s) dropped (shares a hero)",
                slot + 1,
                situationId,
                settlementId,
                spacer,
                weight,
                totalWeight,
                probability * 100.0,
                roll,
                droppedCount);
        }

        public static string FormatPick(SituationPick pick)
        {
            if (pick == null) throw new ArgumentNullException(nameof(pick));
            return FormatPick(
                pick.Slot,
                pick.Candidate.SituationId,
                pick.Candidate.SettlementId,
                pick.Candidate.RoleHeroIds,
                pick.Candidate.Weight,
                pick.TotalWeight,
                pick.Probability,
                pick.Roll,
                pick.DroppedCount);
        }

        public static string FormatDryRunEnd()
        {
            return "  scan (dry run): nothing was triggered, no events were submitted.";
        }
    }
}
