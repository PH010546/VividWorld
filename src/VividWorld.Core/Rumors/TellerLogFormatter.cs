using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using VividWorld.Core.Memory;

namespace VividWorld.Core.Rumors
{
    public static class TellerLogFormatter
    {
        public static string FormatTellerTurn(
            string tellerId,
            string eventId,
            TopicChoice choice,
            PropagationOutcome outcome,
            int inactiveCount)
        {
            var picked = (choice.PickedIndex >= 0 && choice.PickedIndex < choice.Candidates.Count)
                ? choice.Candidates[choice.PickedIndex]
                : null;

            int drama = picked?.Drama ?? 0;
            double tell = picked?.Tell ?? 0.0;
            double fresh = picked?.Freshness ?? 0.0;
            double weight = picked?.Weight ?? 0.0;
            double chancePct = choice.Chance * 100.0;

            int n = choice.Candidates.Count;
            var topicItems = choice.Candidates.Take(6)
                .Select(c => string.Format(CultureInfo.InvariantCulture, "{0} {1:0.000}", c.EventId, c.Weight))
                .ToList();
            string topicsStr = string.Join(", ", topicItems);
            if (n > 6)
            {
                topicsStr += string.Format(CultureInfo.InvariantCulture, " (+{0} more)", n - 6);
            }

            int x = choice.Exclusions.Count;
            string exclStr;
            if (x == 0)
            {
                exclStr = "; excluded 0";
            }
            else
            {
                var exclItems = choice.Exclusions.Take(5)
                    .Select(e => string.Format(CultureInfo.InvariantCulture, "{0} {1}", e.EventId, TellerEligibility.TellReasonText(e.Reason)))
                    .ToList();
                string joined = string.Join(", ", exclItems);
                if (x > 5)
                {
                    joined += string.Format(CultureInfo.InvariantCulture, " (+{0} more)", x - 5);
                }
                exclStr = string.Format(CultureInfo.InvariantCulture, "; excluded {0}: {1}", x, joined);
            }

            int a = outcome.NewKnowers?.Count ?? 0;
            int b = outcome.Reheard?.Count ?? 0;
            int s = outcome.Reheard?.Count(r => r.Kind == RehearKind.SameTellerIgnored || r.Kind == RehearKind.SameTellerRelearned) ?? 0;

            return string.Format(
                CultureInfo.InvariantCulture,
                "Teller {0} told {1} (drama {2}, tell {3:0.00}, fresh {4:0.00}, weight {5:0.000}, chance {6:0.0}%) - {7} topic(s): {8}{9}; inactive {10} -> new {11}, re-heard {12} (same teller {13})",
                tellerId,
                eventId,
                drama,
                tell,
                fresh,
                weight,
                chancePct,
                n,
                topicsStr,
                exclStr,
                inactiveCount,
                a,
                b,
                s);
        }

        public static string FormatSkippedNotEligible(string tellerId)
        {
            return string.Format(CultureInfo.InvariantCulture, "Teller {0} skipped: not eligible now", tellerId);
        }

        public static string FormatLeftRingHeroNotFoundOrDead(string tellerId)
        {
            return string.Format(CultureInfo.InvariantCulture, "Teller {0} left the ring: hero not found or dead", tellerId);
        }

        /// <param name="hiddenFuture">今天被藏起來的事件數（日期比今天晚，規格 §2.2.1）。
        /// 沒有這個數字，只知道未來事件的人會被印成「0 known」——看起來像他什麼都不知道，
        /// 而實際上他知道的每一則都來自一條被抹掉的時間線。</param>
        public static string FormatLeftRingNoTellableTopic(
            string tellerId,
            int totalKnown,
            IReadOnlyList<TopicExclusion> exclusions,
            int inactiveCount,
            int hiddenFuture = 0)
        {
            string hiddenPart = hiddenFuture > 0
                ? string.Format(CultureInfo.InvariantCulture, "; {0} hidden from a future timeline", hiddenFuture)
                : string.Empty;

            int x = exclusions?.Count ?? 0;
            if (x == 0)
            {
                return string.Format(
                    CultureInfo.InvariantCulture,
                    "Teller {0} left the ring: no tellable topic ({1} known - excluded 0; inactive {2}{3})",
                    tellerId,
                    totalKnown,
                    inactiveCount,
                    hiddenPart);
            }

            var groupStrings = new List<string>();
            foreach (TellReason reason in Enum.GetValues(typeof(TellReason)))
            {
                if (reason == TellReason.Ok) continue;
                int count = exclusions!.Count(e => e.Reason == reason);
                if (count > 0)
                {
                    groupStrings.Add(string.Format(CultureInfo.InvariantCulture, "{0} {1}", TellerEligibility.TellReasonText(reason), count));
                }
            }

            string groupsStr = string.Join(", ", groupStrings);
            return string.Format(
                CultureInfo.InvariantCulture,
                "Teller {0} left the ring: no tellable topic ({1} known - excluded {2}: {3}; inactive {4}{5})",
                tellerId,
                totalKnown,
                x,
                groupsStr,
                inactiveCount,
                hiddenPart);
        }

        public static string FormatSameTellerIgnored(string knowerId, string tellerId, string eventId)
        {
            return string.Format(
                CultureInfo.InvariantCulture,
                "Memory: {0} re-heard from {1} again (same teller, not counted) {2}",
                knowerId,
                tellerId,
                eventId);
        }

        public static string FormatSameTellerRelearnedHow(string tellerId, int oldHop, int newHop, string reason)
        {
            return string.Format(
                CultureInfo.InvariantCulture,
                "re-heard from {0} again, adopted version hop {1}->{2} ({3}; same teller, not counted)",
                tellerId,
                oldHop,
                newHop,
                reason);
        }

        public static string FormatWorldStatusTellerRing(
            int size,
            int tellersPerHour,
            int turns,
            IReadOnlyList<int> toldByDrama,
            int skipped,
            int leftRing)
        {
            int d1 = toldByDrama != null && toldByDrama.Count > 0 ? toldByDrama[0] : 0;
            int d2 = toldByDrama != null && toldByDrama.Count > 1 ? toldByDrama[1] : 0;
            int d3 = toldByDrama != null && toldByDrama.Count > 2 ? toldByDrama[2] : 0;
            int d4 = toldByDrama != null && toldByDrama.Count > 3 ? toldByDrama[3] : 0;
            int d5 = toldByDrama != null && toldByDrama.Count > 4 ? toldByDrama[4] : 0;

            return string.Format(
                CultureInfo.InvariantCulture,
                "- Teller ring: {0} tellers, {1} per hour; this session turns {2}, told by drama 1:{3} 2:{4} 3:{5} 4:{6} 5:{7}, skipped not eligible {8}, left ring {9}",
                size,
                tellersPerHour,
                turns,
                d1, d2, d3, d4, d5,
                skipped,
                leftRing);
        }

        public static string FormatWorldStatusReheard(int counted, int sameTellerIgnored, int sameTellerRelearned)
        {
            return string.Format(
                CultureInfo.InvariantCulture,
                "- Re-heard this session: counted {0}, same teller ignored {1}, same teller re-learned {2}",
                counted,
                sameTellerIgnored,
                sameTellerRelearned);
        }

        public static string FormatWorldStatusSpreadByDrama(
            IReadOnlyList<(int drama, int eventCount, int hopGte1Total, int hopGte1Remembered)> dramaStats)
        {
            var parts = new List<string>(5);
            for (int d = 1; d <= 5; d++)
            {
                var stat = dramaStats != null ? dramaStats.FirstOrDefault(s => s.drama == d) : default;
                if (stat.eventCount > 0)
                {
                    double x = (double)stat.hopGte1Total / stat.eventCount;
                    double y = (double)stat.hopGte1Remembered / stat.eventCount;
                    parts.Add(string.Format(CultureInfo.InvariantCulture, "d{0} {1:0.0} / {2:0.0} ({3} events)", d, x, y, stat.eventCount));
                }
                else
                {
                    parts.Add(string.Format(CultureInfo.InvariantCulture, "d{0} - (0 events)", d));
                }
            }

            return "- Spread by drama (NPC entries at hop >= 1 per event, all / remembered): " + string.Join(", ", parts);
        }

        public static string FormatTopicsIfSpokeNow(TopicChoice choice, bool inRing, int inactiveCount)
        {
            string ringStr = inRing ? "yes" : "no";
            string candidatesStr;

            if (choice.Candidates.Count == 0)
            {
                candidatesStr = "none";
            }
            else
            {
                double sumWeight = choice.Candidates.Sum(c => c.Weight);
                var candList = choice.Candidates.Select(c =>
                {
                    double cPct = (sumWeight <= 0.0 ? (1.0 / choice.Candidates.Count) : (c.Weight / sumWeight)) * 100.0;
                    return string.Format(
                        CultureInfo.InvariantCulture,
                        "{0} {1:0.0}% (drama {2}, tell {3:0.00}, fresh {4:0.00})",
                        c.EventId,
                        cPct,
                        c.Drama,
                        c.Tell,
                        c.Freshness);
                });
                candidatesStr = string.Join(", ", candList);
            }

            string excludedStr;
            if (choice.Exclusions.Count == 0)
            {
                excludedStr = "excluded 0";
            }
            else
            {
                var exclList = choice.Exclusions.Select(e =>
                    string.Format(CultureInfo.InvariantCulture, "{0} {1}", e.EventId, TellerEligibility.TellReasonText(e.Reason)));
                excludedStr = string.Format(CultureInfo.InvariantCulture, "excluded {0}: {1}", choice.Exclusions.Count, string.Join(", ", exclList));
            }

            return string.Format(
                CultureInfo.InvariantCulture,
                "  topics if he spoke now (in teller ring: {0}): {1}; {2}; inactive {3}",
                ringStr,
                candidatesStr,
                excludedStr,
                inactiveCount);
        }

        public static string FormatRebuilt(int n, int k)
        {
            return string.Format(CultureInfo.InvariantCulture,
                "Teller ring: rebuilt with {0} teller(s) from the index, {1} knower(s) left out because they forgot everything they knew",
                n, k);
        }
    }
}

