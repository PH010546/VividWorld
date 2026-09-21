using System;
using System.Globalization;

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
    }
}
