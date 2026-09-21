using System;
using System.Globalization;

namespace VividWorld.Core.Events
{
    public static class OutdatingLogFormatter
    {
        public static string FormatReleaseHeader(string detail, string templateType, string prisonerId, string? captorId)
        {
            var captorStr = string.IsNullOrEmpty(captorId) ? "none" : captorId;
            return string.Format(CultureInfo.InvariantCulture,
                "RealEventSource: HeroPrisonerReleased detail={0} -> template '{1}' (prisoner={2}, captor={3})",
                detail, templateType, prisonerId, captorStr);
        }

        public static string FormatLinkedCapture(string captureEventId, string captureType, double captureDay, double currentDay)
        {
            double diff = Math.Max(0.0, currentDay - captureDay);
            return string.Format(CultureInfo.InvariantCulture,
                "  linked to {0} ({1}, day {2:0.0}, {3:0.0} days ago)",
                captureEventId, captureType, captureDay, diff);
        }

        public static string FormatLinkedNone(string prisonerId, int checkedCount)
        {
            return string.Format(CultureInfo.InvariantCulture,
                "  linked: none - no capture event for {0} in the index (checked {1} entries)",
                prisonerId, checkedCount);
        }

        public static string FormatOutdatedSummary(int nowKnowingCount, int totalNpcKnowers, string captureEventId, string releaseEventId)
        {
            return string.Format(CultureInfo.InvariantCulture,
                "  outdated: {0} of {1} NPC knower(s) of {2} now know he is out (by {3})",
                nowKnowingCount, totalNpcKnowers, captureEventId, releaseEventId);
        }

        public static string FormatHeroOutdated(string heroId, string captureEventId, string releaseEventId, double day)
        {
            return string.Format(CultureInfo.InvariantCulture,
                "Outdated: {0} no longer spreads {1} (heard {2} on day {3:0.0})",
                heroId, captureEventId, releaseEventId, day);
        }
    }
}
