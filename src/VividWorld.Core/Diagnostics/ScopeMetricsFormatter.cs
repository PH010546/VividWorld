using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace VividWorld.Core.Diagnostics
{
    public static class ScopeMetricsFormatter
    {
        /// <summary>
        /// Detailed line for a single scope: call count, total ms, avg ms, and peak ms.
        /// When callCount is 0, all millisecond metrics are rendered as 0.00ms.
        /// </summary>
        public static string Detail(string scope, long callCount, double totalMs, double peakMs)
        {
            if (callCount > 0)
            {
                double avg = totalMs / callCount;
                return string.Format(CultureInfo.InvariantCulture,
                    "- {0,-9}: {1} calls, total {2:0.00}ms, avg {3:0.00}ms, peak {4:0.00}ms",
                    scope, callCount, totalMs, avg, peakMs);
            }

            return string.Format(CultureInfo.InvariantCulture,
                "- {0,-9}: 0 calls, total 0.00ms, avg 0.00ms, peak 0.00ms",
                scope);
        }

        /// <summary>
        /// Detailed line with peak metadata (call index, time, first after load, sub-breakdown).
        /// </summary>
        public static string Detail(string scope, long callCount, double totalMs, double peakMs, ScopePeakDetail? peakDetail)
        {
            string baseLine = Detail(scope, callCount, totalMs, peakMs);
            if (callCount <= 0 || peakDetail == null)
            {
                return baseLine;
            }

            string detailSuffix = FormatPeakDetailSuffix(peakDetail);
            if (string.IsNullOrEmpty(detailSuffix))
            {
                return baseLine;
            }

            return baseLine + " " + detailSuffix;
        }

        /// <summary>
        /// Compact summary line for dialogue display.
        /// When the difference between peak and average is less than 0.05ms, peak is omitted.
        /// </summary>
        public static string Compact(string scope, long callCount, double totalMs, double peakMs)
        {
            if (callCount == 0)
            {
                return string.Format(CultureInfo.InvariantCulture, "{0} 0ms x 0", scope);
            }

            double avg = totalMs / callCount;
            if (Math.Abs(peakMs - avg) < 0.05)
            {
                return string.Format(CultureInfo.InvariantCulture, "{0} {1:0.#}ms x {2}", scope, avg, callCount);
            }

            return string.Format(CultureInfo.InvariantCulture, "{0} {1:0.#}ms avg / {2:0.#}ms peak x {3}", scope, avg, peakMs, callCount);
        }

        /// <summary>
        /// Format the daily summary line for daily tick.
        /// Example:
        /// Perf day 131: hourly 24x avg 2.10 max 9.80ms (h14, call #3121, tellers 6: lookup 3.20 choose 1.10 propagate 5.00 other 0.50) | daily 1x 4.00ms | contacts 140x avg 0.05 max 0.40ms | situationScan 1x 6.00ms | snapshot 0x | purge 0x | load 0x | world: teller ring 471, active rumors 380, secrets watched 40, events on disk 2210
        /// </summary>
        public static string FormatDailyLine(
            int dayNumber,
            IReadOnlyList<ScopeStatsSnapshot> scopes,
            WorldScaleMetrics worldScale)
        {
            var sb = new StringBuilder();
            sb.Append(string.Format(CultureInfo.InvariantCulture, "Perf day {0}:", dayNumber));

            if (scopes != null)
            {
                for (int i = 0; i < scopes.Count; i++)
                {
                    sb.Append(" ");
                    if (i > 0)
                    {
                        sb.Append("| ");
                    }
                    sb.Append(FormatScopeDailySummary(scopes[i]));
                }
            }

            sb.Append(" | world: ");
            sb.Append(FormatWorldScale(worldScale));

            return sb.ToString();
        }

        /// <summary>
        /// Format the single log line after game loading / rebuild finishes.
        /// Example: Load: 12.50ms (index 380 events, roster 471 tellers)
        /// </summary>
        public static string FormatLoadLine(double elapsedMs, int indexedEventCount, int tellerCount)
        {
            return string.Format(CultureInfo.InvariantCulture,
                "Load: {0:0.00}ms (index {1} events, roster {2} tellers)",
                elapsedMs, indexedEventCount, tellerCount);
        }

        private static string FormatScopeDailySummary(ScopeStatsSnapshot snap)
        {
            if (snap == null) return string.Empty;

            if (snap.CallCount <= 0)
            {
                return string.Format(CultureInfo.InvariantCulture, "{0} 0x", snap.Scope);
            }

            if (snap.CallCount == 1)
            {
                string oneCall = string.Format(CultureInfo.InvariantCulture, "{0} 1x {1:0.00}ms", snap.Scope, snap.TotalMilliseconds);
                if (snap.PeakDetail != null)
                {
                    string suffix = FormatPeakDetailSuffix(snap.PeakDetail);
                    if (!string.IsNullOrEmpty(suffix))
                    {
                        oneCall += " " + suffix;
                    }
                }
                return oneCall;
            }

            double avg = snap.TotalMilliseconds / snap.CallCount;
            string multiCall = string.Format(CultureInfo.InvariantCulture,
                "{0} {1}x avg {2:0.00} max {3:0.00}ms",
                snap.Scope, snap.CallCount, avg, snap.PeakMilliseconds);

            if (snap.PeakDetail != null)
            {
                string suffix = FormatPeakDetailSuffix(snap.PeakDetail);
                if (!string.IsNullOrEmpty(suffix))
                {
                    multiCall += " " + suffix;
                }
            }
            return multiCall;
        }

        public static string FormatPeakDetailSuffix(ScopePeakDetail detail)
        {
            if (detail == null) return string.Empty;

            var parts = new List<string>(4);

            if (detail.Day >= 0)
            {
                parts.Add(string.Format(CultureInfo.InvariantCulture, "day {0}", (int)detail.Day));
            }

            if (detail.HourOfDay >= 0)
            {
                parts.Add(string.Format(CultureInfo.InvariantCulture, "h{0}", detail.HourOfDay));
            }

            if (detail.CallIndex > 0)
            {
                parts.Add(string.Format(CultureInfo.InvariantCulture, "call #{0}", detail.CallIndex));
            }

            if (detail.FirstAfterLoad)
            {
                parts.Add("first after load");
            }

            if (detail.HourlySub.HasValue)
            {
                var sub = detail.HourlySub.Value;
                parts.Add(string.Format(CultureInfo.InvariantCulture,
                    "tellers {0}: lookup {1:0.00} choose {2:0.00} propagate {3:0.00} other {4:0.00}",
                    sub.TellerCount, sub.LookupMs, sub.ChooseMs, sub.PropagateMs, sub.OtherMs));
            }

            if (parts.Count == 0) return string.Empty;

            return "(" + string.Join(", ", parts) + ")";
        }

        private static string FormatWorldScale(WorldScaleMetrics scale)
        {
            string tellerRingStr = scale.TellerRing.HasValue
                ? scale.TellerRing.Value.ToString(CultureInfo.InvariantCulture)
                : "?";

            string activeRumorsStr = scale.ActiveRumors.HasValue
                ? scale.ActiveRumors.Value.ToString(CultureInfo.InvariantCulture)
                : "?";

            string secretsWatchedStr = scale.SecretsWatched.HasValue
                ? scale.SecretsWatched.Value.ToString(CultureInfo.InvariantCulture)
                : "?";

            string eventsOnDiskStr = scale.EventsOnDisk.HasValue
                ? scale.EventsOnDisk.Value.ToString(CultureInfo.InvariantCulture)
                : "?";

            string summary = string.Format(CultureInfo.InvariantCulture,
                "teller ring {0}, active rumors {1}, secrets watched {2}, events on disk {3}",
                tellerRingStr, activeRumorsStr, secretsWatchedStr, eventsOnDiskStr);

            if (!string.IsNullOrEmpty(scale.ReasonMissing))
            {
                summary += " (" + scale.ReasonMissing + ")";
            }

            return summary;
        }
    }
}
