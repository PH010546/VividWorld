using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using VividWorld.Core.Dialogue;
using VividWorld.Core.Events;
using VividWorld.Core.Rumors;

namespace VividWorld.Core.Grudges
{
    public static class GrudgeLogFormatter
    {
        // 1. Application lines
        public static string FormatAppliedNative(
            string fromRole, string fromHeroId,
            string toRole, string toHeroId,
            double requested, int nativeDelta,
            int before, int after)
        {
            return string.Format(CultureInfo.InvariantCulture,
                "grudge {0} {1} -> {2} {3}: requested {4:0.0} personal, native {5} (base {6} -> {7})",
                fromRole, fromHeroId, toRole, toHeroId, requested, nativeDelta, before, after);
        }

        public static string FormatAppliedLedgerOnly(
            string fromRole, string fromHeroId,
            string toRole, string toHeroId,
            double requested)
        {
            return string.Format(CultureInfo.InvariantCulture,
                "grudge {0} {1} -> {2} {3}: requested {4:0.0} personal, ledger-only (native untouched)",
                fromRole, fromHeroId, toRole, toHeroId, requested);
        }

        public static string FormatSkippedNotHop0(
            string fromRole, string fromHeroId,
            string toRole, string toHeroId,
            string eventId)
        {
            return string.Format(CultureInfo.InvariantCulture,
                "grudge skipped {0} {1} -> {2} {3}: not a hop 0 knower of {4}",
                fromRole, fromHeroId, toRole, toHeroId, eventId);
        }

        /// <summary>同一天同一組人已經有一則了（SE1 §3.5 的設計），恩怨不重記。
        /// 這跟「不是 hop 0 知情者」是兩回事，理由要分得出來。</summary>
        public static string FormatSkippedDuplicateEvent(
            string fromRole, string fromHeroId,
            string toRole, string toHeroId,
            string existingEventId)
        {
            return string.Format(CultureInfo.InvariantCulture,
                "grudge skipped {0} {1} -> {2} {3}: the branch's event already exists today as {4}",
                fromRole, fromHeroId, toRole, toHeroId, existingEventId);
        }

        public static string FormatSkippedUnboundRole(
            string fromRole, string fromHeroId,
            string toRole, string toHeroId,
            string unboundRole)
        {
            return string.Format(CultureInfo.InvariantCulture,
                "grudge skipped {0} {1} -> {2} {3}: role '{4}' is unbound",
                fromRole, fromHeroId, toRole, toHeroId, unboundRole);
        }

        public static string FormatSkippedPlayer(
            string fromRole, string fromHeroId,
            string toRole, string toHeroId,
            string playerHeroId)
        {
            return string.Format(CultureInfo.InvariantCulture,
                "grudge skipped {0} {1} -> {2} {3}: {4} is the player",
                fromRole, fromHeroId, toRole, toHeroId, playerHeroId);
        }

        public static string FormatDisabled(int count)
        {
            return string.Format(CultureInfo.InvariantCulture,
                "grudge disabled: situations.grudgesEnabled is false, {0} grudge(s) not applied",
                count);
        }

        // 2. Escalation lines
        public static string FormatEscalationEscalatedNative(
            string fromHeroId, string toHeroId,
            string reason,
            string leaderA, string leaderB,
            double requested, int nativeDelta,
            int before, int after)
        {
            return string.Format(CultureInfo.InvariantCulture,
                "escalation {0} -> {1}: escalated - {2} -> clan {3} <-> {4}, requested {5:0.0}, native {6} (base {7} -> {8})",
                fromHeroId, toHeroId, reason, leaderA, leaderB, requested, nativeDelta, before, after);
        }

        public static string FormatEscalationEscalatedLedgerOnly(
            string fromHeroId, string toHeroId,
            string reason,
            string leaderA, string leaderB,
            double requested)
        {
            return string.Format(CultureInfo.InvariantCulture,
                "escalation {0} -> {1}: escalated - {2} -> clan {3} <-> {4}, requested {5:0.0}, ledger-only (native untouched)",
                fromHeroId, toHeroId, reason, leaderA, leaderB, requested);
        }

        public static string FormatEscalationNotEscalated(
            string fromHeroId, string toHeroId,
            string reason)
        {
            return string.Format(CultureInfo.InvariantCulture,
                "escalation {0} -> {1}: not escalated - {2}",
                fromHeroId, toHeroId, reason);
        }

        // 3. Replay lines
        public static string FormatReplayHeader(string heroId, double day, int pairCount)
        {
            return string.Format(CultureInfo.InvariantCulture,
                "Grudge ledger for {0} (day {1:0.0}): {2} pair(s)",
                heroId, day, pairCount);
        }

        public static string FormatReplayHeaderNoGrudges(string heroId, double day)
        {
            return string.Format(CultureInfo.InvariantCulture,
                "Grudge ledger for {0} (day {1:0.0}): no grudges recorded",
                heroId, day);
        }

        public static string FormatPairHeader(
            string aboutHeroId,
            GrudgeScope scope,
            double value,
            double band,
            int entryCount)
        {
            string scopeStr = scope == GrudgeScope.Personal ? "personal" : "clan";
            return string.Format(CultureInfo.InvariantCulture,
                "  vs {0} [{1}] value {2:0.0} (band +-{3:0.0}, {4} entry(ies))",
                aboutHeroId, scopeStr, value, band, entryCount);
        }

        public static string FormatRequestedStep(double day, string? eventId, double requested, double valueAfter)
        {
            return string.Format(CultureInfo.InvariantCulture,
                "    day {0:0.0} {1} requested {2:0.0} -> {3:0.0}",
                day, eventId ?? "(none)", requested, valueAfter);
        }

        public static string FormatDecayInsideBand(
            double startDay, double endDay,
            double daysElapsed, double daysPerPoint,
            double multiplier, double potentialPoints,
            double band, double valueAfterDecay)
        {
            return string.Format(CultureInfo.InvariantCulture,
                "    decay {0:0.0} -> {1:0.0}: {2:0.0}d / {3:0.00}d per point x {4:0.00} = {5:0.00} points, inside band +-{6:0.0}, no change -> {7:0.0}",
                startDay, endDay, daysElapsed, daysPerPoint, multiplier, potentialPoints, band, valueAfterDecay);
        }

        public static string FormatDecayNormal(
            double startDay, double endDay,
            double daysElapsed, double daysPerPoint,
            double multiplier, double pointsDecayed,
            double valueAfterDecay)
        {
            return string.Format(CultureInfo.InvariantCulture,
                "    decay {0:0.0} -> {1:0.0}: {2:0.0}d / {3:0.00}d per point x {4:0.00} = {5:0.00} points -> {6:0.0}",
                startDay, endDay, daysElapsed, daysPerPoint, multiplier, pointsDecayed, valueAfterDecay);
        }

        public static string FormatDecayHitBand(
            double startDay, double endDay,
            double daysElapsed, double daysPerPoint,
            double multiplier, double potentialPoints,
            double bandReached, double valueAfterDecay)
        {
            return string.Format(CultureInfo.InvariantCulture,
                "    decay {0:0.0} -> {1:0.0}: {2:0.0}d / {3:0.00}d per point x {4:0.00} = {5:0.00} points, hit band {6:0.0} -> {7:0.0}",
                startDay, endDay, daysElapsed, daysPerPoint, multiplier, potentialPoints, bandReached, valueAfterDecay);
        }

        public static string FormatMultiplierLine(double multiplier, string detail)
        {
            return string.Format(CultureInfo.InvariantCulture,
                "    multiplier x{0:0.00} = {1}",
                multiplier, detail);
        }

        public static string FormatMultiplierPerSegmentLine(double multiplier, string detail)
        {
            return string.Format(CultureInfo.InvariantCulture,
                "      multiplier x{0:0.00} = {1}",
                multiplier, detail);
        }

        public static IReadOnlyList<string> FormatPairReplay(
            string aboutHeroId,
            GrudgeScope scope,
            GrudgeReplayResult result)
        {
            var lines = new List<string>();
            lines.Add(FormatPairHeader(aboutHeroId, scope, result.Value, result.Band, result.EntryCount));

            if (result.HiddenFutureCount > 0)
            {
                lines.Add(string.Format(CultureInfo.InvariantCulture,
                    "      {0} entry(ies) from a future timeline not replayed (event day is later than today)",
                    result.HiddenFutureCount));
            }

            if (result.Steps == null || result.Steps.Count == 0)
            {
                return lines;
            }

            // Check if multiplier varies across decay segments with DaysElapsed > 0
            var decaySteps = result.Steps.Where(s => s.DaysElapsed > 0).ToList();
            bool variesBySign = decaySteps.Select(s => s.MultiplierDetail).Distinct().Count() > 1;

            for (int i = 0; i < result.Steps.Count; i++)
            {
                var step = result.Steps[i];

                // 每一段的順序就是實際發生的順序：先記下那一筆，再從那一天淡化到下一件事／今天。
                // 兩行都用 step.EntryDay 當起點——用「下一段的日期」會讓帳本上的日期整個位移一筆。
                if (step.Requested.HasValue)
                {
                    lines.Add(FormatRequestedStep(step.EntryDay, step.EventId, step.Requested.Value, step.ValueAfterRequested));
                }

                if (step.DaysElapsed > 0)
                {
                    double potentialPoints = step.DaysElapsed / (result.DaysPerPoint > 0 ? result.DaysPerPoint : 1.0) * step.Multiplier;
                    if (step.InsideBand)
                    {
                        lines.Add(FormatDecayInsideBand(
                            step.EntryDay, step.Day,
                            step.DaysElapsed, result.DaysPerPoint,
                            step.Multiplier, potentialPoints,
                            result.Band, step.ValueAfterDecay));
                    }
                    else if (step.HitBand)
                    {
                        double signedBand = Math.Sign(step.ValueBefore) * result.Band;
                        lines.Add(FormatDecayHitBand(
                            step.EntryDay, step.Day,
                            step.DaysElapsed, result.DaysPerPoint,
                            step.Multiplier, potentialPoints,
                            signedBand, step.ValueAfterDecay));
                    }
                    else
                    {
                        lines.Add(FormatDecayNormal(
                            step.EntryDay, step.Day,
                            step.DaysElapsed, result.DaysPerPoint,
                            step.Multiplier, step.PointsDecayed,
                            step.ValueAfterDecay));
                    }

                    if (variesBySign)
                    {
                        lines.Add(FormatMultiplierPerSegmentLine(step.Multiplier, step.MultiplierDetail));
                    }
                }
            }

            if (variesBySign)
            {
                lines.Add("    multiplier varies by sign, printed per segment");
            }
            else
            {
                // Multiplier is the same for the whole pair: print at the end
                var sampleStep = decaySteps.FirstOrDefault() ?? result.Steps.LastOrDefault();
                if (sampleStep != null)
                {
                    lines.Add(FormatMultiplierLine(sampleStep.Multiplier, sampleStep.MultiplierDetail));
                }
            }

            return lines;
        }

        // 4. Opinion log formatters (M6.5 §4.5)
        public static string FormatOpinionAppliedNative(
            string observer, string aboutHeroId, string aboutRole, string eventId,
            double amount, int hop, double hopConfidence, bool isWitness, double multiplier,
            double fullAmount, double alreadyApplied, double requested,
            int nativeBefore, int nativeAfter, int nativeDelta)
        {
            string roleStr = isWitness ? "participant" : "onlooker";
            return string.Format(CultureInfo.InvariantCulture,
                "opinion applied {0} -> {1} ({2}) on {3}: template {4:0.00}, hop {5} x{6:0.00}, {7} x{8:0.00} => full {9:0.00}, already {10:0.00}, requested {11:0.00}, native {12} -> {13} ({14})",
                observer, aboutHeroId, aboutRole, eventId,
                amount, hop, hopConfidence, roleStr, multiplier,
                fullAmount, alreadyApplied, requested,
                nativeBefore, nativeAfter, nativeDelta);
        }

        public static string FormatOpinionAppliedLedgerOnly(
            string observer, string aboutHeroId, string aboutRole, string eventId,
            double amount, int hop, double hopConfidence, bool isWitness, double multiplier,
            double fullAmount, double alreadyApplied, double requested)
        {
            string roleStr = isWitness ? "participant" : "onlooker";
            return string.Format(CultureInfo.InvariantCulture,
                "opinion ledger-only {0} -> {1} ({2}) on {3}: template {4:0.00}, hop {5} x{6:0.00}, {7} x{8:0.00} => full {9:0.00}, already {10:0.00}, requested {11:0.00}",
                observer, aboutHeroId, aboutRole, eventId,
                amount, hop, hopConfidence, roleStr, multiplier,
                fullAmount, alreadyApplied, requested);
        }

        public static string FormatOpinionSkipped(string observer, string aboutRole, string eventId, string reason)
        {
            return string.Format(CultureInfo.InvariantCulture,
                "opinion skipped {0} -> {1} on {2}: {3}",
                observer, aboutRole, eventId, reason);
        }

        public static string FormatOpinionSkipReason(OpinionSkipReason reason, string? aboutHeroId = null, string? aboutRole = null, double calculatedAmount = 0.0)
        {
            return reason switch
            {
                OpinionSkipReason.NotNamedByAnyFact => string.Format(CultureInfo.InvariantCulture, "no retained fragment names {0}", aboutHeroId ?? "hero"),
                OpinionSkipReason.AboutIsObserver => "target is the observer self",
                OpinionSkipReason.AboutNotBound => string.Format(CultureInfo.InvariantCulture, "target role {0} is not bound in event", aboutRole ?? "about"),
                OpinionSkipReason.BelowMinimum => string.Format(CultureInfo.InvariantCulture, "calculated amount {0:0.00} is below minimum threshold", calculatedAmount),
                OpinionSkipReason.DailyBudgetExhausted => "daily relation shift budget exhausted",
                OpinionSkipReason.AlreadyFullyApplied => "shift has already been fully applied",
                _ => reason.ToString()
            };
        }

        public static string FormatOpinionBudget(string observer, double day, double used, double cap, int clampedCount)
        {
            int dayBucket = DailyCounter.BucketOf(day);
            return string.Format(CultureInfo.InvariantCulture,
                "opinion budget {0} on day {1}: {2:0.00} of {3:0.00} used, {4} change(s) clamped",
                observer, dayBucket, used, cap, clampedCount);
        }

        public static string FormatOpinionsDisabled(int hearerCount)
        {
            return string.Format(CultureInfo.InvariantCulture,
                "opinions disabled: {0} hearer(s) skipped this tick",
                hearerCount);
        }
    }
}
