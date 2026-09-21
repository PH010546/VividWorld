using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using VividWorld.Core.Config;

namespace VividWorld.Core.Memory
{
    public static class MemoryLogFormatter
    {
        public static string FormatCalculation(
            string knowerId,
            string how,
            string eventId,
            InterestResult interest,
            int drama,
            int dramaReference,
            double baseDays,
            MemorySpanResult span)
        {
            return FormatCalculation(knowerId, how, eventId, interest, drama, dramaReference, baseDays, span, null!);
        }

        public static string FormatCalculation(
            string knowerId,
            string how,
            string eventId,
            InterestResult interest,
            int drama,
            int dramaReference,
            double baseDays,
            MemorySpanResult span,
            MemoryConfig cfg)
        {
            string viaClause;
            if (!interest.KnowerFound)
            {
                viaClause = "via (knower not found)";
            }
            else if (interest.ParticipantDetails == null || interest.ParticipantDetails.Count == 0)
            {
                viaClause = "via other floor (no participants)";
            }
            else
            {
                string bestPart = $"{interest.BestRole}={interest.BestHeroId} {interest.BestSource}";
                var others = interest.ParticipantDetails
                    .Where(d => !(d.Role == interest.BestRole && d.HeroId == interest.BestHeroId))
                    .ToList();

                if (others.Count > 0)
                {
                    string othersList = string.Join(", ", others.Select(o =>
                        string.Format(CultureInfo.InvariantCulture, "{0}={1} {2:0.00} {3}", o.Role, o.HeroId, o.Value, o.Source)));
                    viaClause = $"via {bestPart} (others: {othersList})";
                }
                else
                {
                    viaClause = $"via {bestPart}";
                }
            }

            string daysPart = span.RaisedToMin
                ? string.Format(CultureInfo.InvariantCulture, "= {0:0.0}d (raised from {1:0.00}d to min)", span.Days, span.Raw)
                : string.Format(CultureInfo.InvariantCulture, "= {0:0.0}d", span.Days);

            string forgetsPart = span.KeptOld
                ? string.Format(CultureInfo.InvariantCulture, "-> forgets on day {0:0.0} (kept; new {1:0.0})", span.ForgetDay, span.Candidate)
                : string.Format(CultureInfo.InvariantCulture, "-> forgets on day {0:0.0}", span.ForgetDay);

            return string.Format(
                CultureInfo.InvariantCulture,
                "Memory: {0} {1} {2} - interest {3:0.00} {4} x drama {5}/{6} x base {7:0.0}d x reinforce {8:0.00} {9} {10}",
                knowerId,
                how,
                eventId,
                interest.Interest,
                viaClause,
                drama,
                dramaReference,
                baseDays,
                span.Reinforce,
                daysPart,
                forgetsPart);
        }

        public static string FormatDormancy(string eventId, DormancyReason reason)
        {
            switch (reason.Kind)
            {
                case DormancyKind.AllAtMaxHop:
                    return string.Format(CultureInfo.InvariantCulture,
                        "Rumor {0} went dormant: all {1} knower(s) at max hop {2}",
                        eventId, reason.Count, reason.MaxHop);
                case DormancyKind.LifetimeExpired:
                    return string.Format(CultureInfo.InvariantCulture,
                        "Rumor {0} went dormant: age {1:0.0}d > lifetime {2:0.0}d",
                        eventId, reason.Age, reason.Lifetime);
                case DormancyKind.Stale:
                    return string.Format(CultureInfo.InvariantCulture,
                        "Rumor {0} went dormant: no new knower for {1:0.0}d > stale {2:0.0}d",
                        eventId, reason.Idle, reason.StaleThreshold);
                case DormancyKind.AllForgotten:
                    return string.Format(CultureInfo.InvariantCulture,
                        "Rumor {0} went dormant: all {1} NPC knower(s) have forgotten",
                        eventId, reason.Count);
                case DormancyKind.AllOutdated:
                    return string.Format(CultureInfo.InvariantCulture,
                        "Rumor {0} went dormant: all {1} NPC knower(s) are outdated or have forgotten (outdated {2}, forgotten {3})",
                        eventId, reason.Count, reason.OutdatedCount, reason.ForgottenCount);
                default:
                    return string.Format(CultureInfo.InvariantCulture,
                        "Rumor {0} went dormant", eventId);
            }
        }

        public static string FormatForgottenSummary(
            string name,
            string heroId,
            IReadOnlyList<(string EventId, double ForgetDay)> forgotten,
            int totalKnown)
        {
            if (forgotten == null || forgotten.Count == 0) return string.Empty;

            int f = forgotten.Count;
            var sorted = forgotten
                .OrderByDescending(x => x.ForgetDay)
                .ThenBy(x => x.EventId, StringComparer.Ordinal)
                .ToList();

            var top5 = sorted.Take(5).Select(x =>
                string.Format(CultureInfo.InvariantCulture, "{0} (forgot day {1:0.0})", x.EventId, x.ForgetDay));
            string itemsStr = string.Join(", ", top5);

            int k = f - 5;
            string moreStr = k > 0 ? string.Format(CultureInfo.InvariantCulture, " (+{0} more)", k) : "";

            return string.Format(CultureInfo.InvariantCulture,
                "  memory: {0} ({1}) has forgotten {2} of {3} event(s) on file: {4}{5}",
                name, heroId, f, totalKnown, itemsStr, moreStr);
        }

        public static string FormatOutdatedSummary(
            string name,
            string heroId,
            IReadOnlyList<(string EventId, double OutdatedDay)> outdated)
        {
            if (outdated == null || outdated.Count == 0) return string.Empty;

            int count = outdated.Count;
            var sorted = outdated
                .OrderByDescending(x => x.OutdatedDay)
                .ThenBy(x => x.EventId, StringComparer.Ordinal)
                .ToList();

            var top5 = sorted.Take(5).Select(x =>
                string.Format(CultureInfo.InvariantCulture, "{0} (outdated day {1:0.0})", x.EventId, x.OutdatedDay));
            string itemsStr = string.Join(", ", top5);

            int k = count - 5;
            string moreStr = k > 0 ? string.Format(CultureInfo.InvariantCulture, " (+{0} more)", k) : "";

            return string.Format(CultureInfo.InvariantCulture,
                "  memory: {0} ({1}) has {2} outdated event(s): {3}{4}",
                name, heroId, count, itemsStr, moreStr);
        }

        public static string FormatWorldStatus(
            MemoryConfig cfg,
            int remembered,
            int forgotten,
            int unstamped,
            int eventsAllForgotten)
        {
            if (cfg == null || !cfg.Enabled)
            {
                return "- Memory: off";
            }

            return string.Format(CultureInfo.InvariantCulture,
                "- Memory: on, base {0:0.0}d, drama ref {1}, min {2:0.0}d, tell min {3:0.00}, upgrade at {4:0.00}; NPC knower entries remembered {5} / forgotten {6} / unstamped {7}; events with every NPC knower forgotten {8}",
                cfg.BaseDays,
                cfg.DramaReference,
                cfg.MinDays,
                cfg.TellFactorMin,
                cfg.UpgradeMinInterest,
                remembered,
                forgotten,
                unstamped,
                eventsAllForgotten);
        }

        // 規格 §6.9.3：過時的人還留在名單上（那一筆裡有 RelationImpacts），但已經不傳這件事了。
        // 沒過時的那幾列逐字不變——期望值就是實機 log 的真實輸出，見 FormatRosterLine_NotOutdated_MatchesLegacyLayout。
        public static string FormatRosterLine(
            int hop,
            string heroName,
            string? heroId,
            string location,
            string source,
            double? outdatedDay)
        {
            string outdated = outdatedDay != null
                ? string.Format(CultureInfo.InvariantCulture, " [outdated day {0:0.0}]", outdatedDay.Value)
                : string.Empty;

            return string.Format(CultureInfo.InvariantCulture,
                "  hop {0,-2} {1,-14} ({2,-22}) at {3,-12} source: {4}{5}",
                hop, heroName, heroId, location, source, outdated);
        }
    }
}
