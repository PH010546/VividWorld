#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace VividWorld.Core.Dialogue
{
    public static class ListenTallyLogFormatter
    {
        private static readonly string[] OutcomeKeys = new[]
        {
            ListenTallyKeys.NotInNetwork,
            ListenTallyKeys.VolunteerNotEvaluatedTokenLost,
            ListenTallyKeys.VolunteerNotEvaluatedStartNotReached,
            ListenTallyKeys.VolunteerBlockedCommonerTier,
            ListenTallyKeys.VolunteerBlockedLordAttack,
            ListenTallyKeys.VolunteerBlockedRelationGate,
            ListenTallyKeys.VolunteerBlockedCooldown,
            ListenTallyKeys.VolunteerBlockedDailyCap,
            ListenTallyKeys.VolunteerNoTopicNothingOnFile,
            ListenTallyKeys.VolunteerNoTopicAllForgotten,
            ListenTallyKeys.VolunteerNoTopicAllOutdated,
            ListenTallyKeys.VolunteerNoTopicForgottenOrOutdated,
            ListenTallyKeys.VolunteerFiltered,
            ListenTallyKeys.VolunteerTold,
            ListenTallyKeys.VolunteerChosenNotDelivered
        };

        private static readonly (string Key, string ShortName)[] BlockerKeyMap = new[]
        {
            (ListenTallyKeys.VolunteerBlockedRelationGate, "relationGate"),
            (ListenTallyKeys.VolunteerNoTopicNothingOnFile, "noTopic.nothingOnFile"),
            (ListenTallyKeys.VolunteerNoTopicAllForgotten, "noTopic.allForgotten"),
            (ListenTallyKeys.VolunteerNoTopicAllOutdated, "noTopic.allOutdated"),
            (ListenTallyKeys.VolunteerNoTopicForgottenOrOutdated, "noTopic.forgottenOrOutdated"),
            (ListenTallyKeys.VolunteerBlockedCooldown, "cooldown"),
            (ListenTallyKeys.VolunteerBlockedDailyCap, "dailyCap"),
            (ListenTallyKeys.VolunteerBlockedCommonerTier, "commonerTier"),
            (ListenTallyKeys.VolunteerBlockedLordAttack, "lordAttack"),
            (ListenTallyKeys.VolunteerNotEvaluatedStartNotReached, "startNotReached"),
            (ListenTallyKeys.VolunteerNotEvaluatedTokenLost, "tokenLost"),
            (ListenTallyKeys.VolunteerFiltered, "filtered"),
            (ListenTallyKeys.VolunteerChosenNotDelivered, "chosenNotDelivered"),
            (ListenTallyKeys.NotInNetwork, "notInNetwork"),
            (ListenTallyKeys.AskRefusedRelationGate, "ask.relationGate"),
            (ListenTallyKeys.AskRefusedWillingnessGate, "ask.willingnessGate"),
            (ListenTallyKeys.AskNoTopicNothingOnFile, "ask.noTopic.nothingOnFile"),
            (ListenTallyKeys.AskNoTopicAllForgotten, "ask.noTopic.allForgotten"),
            (ListenTallyKeys.AskNoTopicAllOutdated, "ask.noTopic.allOutdated"),
            (ListenTallyKeys.AskNoTopicForgottenOrOutdated, "ask.noTopic.forgottenOrOutdated"),
            (ListenTallyKeys.AskFiltered, "ask.filtered")
        };

        public static string FormatDailyLine(int day, ListenDayTally? tally)
        {
            if (tally == null)
            {
                return $"Listen tally day {day}: talked 0 (network 0, distinct 0) | told 0 volunteered + 0 asked | no conversations | ask shown 0, ask blocked by clan tier 0";
            }

            int talked = 0;
            foreach (var key in OutcomeKeys)
            {
                talked += tally.GetCount(key);
            }

            int notInNetwork = tally.GetCount(ListenTallyKeys.NotInNetwork);
            int network = Math.Max(0, talked - notInNetwork);
            int distinct = tally.DistinctPartners;

            int volTold = tally.GetCount(ListenTallyKeys.VolunteerTold);
            int askTold = tally.GetCount(ListenTallyKeys.AskTold);
            int askShown = tally.GetCount(ListenTallyKeys.AskShown);
            int askBlockedClanTier = tally.GetCount(ListenTallyKeys.AskBlockedCommonerTier);

            var blockers = new List<(string Name, int Count)>();
            foreach (var (k, name) in BlockerKeyMap)
            {
                int c = tally.GetCount(k);
                if (c > 0)
                {
                    blockers.Add((name, c));
                }
            }
            blockers.Sort((a, b) => b.Count.CompareTo(a.Count));

            string blockersStr = blockers.Count > 0
                ? string.Join(", ", blockers.Select(b => $"{b.Name} {b.Count}"))
                : "none";

            return $"Listen tally day {day}: talked {talked} (network {network}, distinct {distinct}) | told {volTold} volunteered + {askTold} asked | top blockers: {blockersStr} | ask shown {askShown}, ask blocked by clan tier {askBlockedClanTier}";
        }

        public static string FormatDevReport(ListenTally tally, int currentDay, string filePath)
        {
            var sb = new StringBuilder();
            sb.AppendLine("=== Listen Tally (Why do I hear so little?) ===");
            sb.AppendLine($"File: {filePath}");
            sb.AppendLine();

            if (tally == null || tally.Days.Count == 0)
            {
                sb.AppendLine("No tally data recorded yet.");
                return sb.ToString().TrimEnd();
            }

            // All-time aggregation
            int allTalked = 0;
            int allNotInNetwork = 0;
            int allVolTold = 0;
            int allAskTold = 0;
            var allPartners = new HashSet<string>(StringComparer.Ordinal);
            var allCounts = new Dictionary<string, int>(StringComparer.Ordinal);
            var allRelationHist = new Dictionary<string, int>(StringComparer.Ordinal);

            foreach (var dayKvp in tally.Days)
            {
                var dt = dayKvp.Value;
                foreach (var k in OutcomeKeys)
                {
                    allTalked += dt.GetCount(k);
                }
                allNotInNetwork += dt.GetCount(ListenTallyKeys.NotInNetwork);
                allVolTold += dt.GetCount(ListenTallyKeys.VolunteerTold);
                allAskTold += dt.GetCount(ListenTallyKeys.AskTold);

                if (dt.Partners != null)
                {
                    foreach (var p in dt.Partners) allPartners.Add(p);
                }

                foreach (var c in dt.Counts)
                {
                    allCounts[c.Key] = (allCounts.TryGetValue(c.Key, out int v) ? v : 0) + c.Value;
                }

                foreach (var rh in dt.RelationHist)
                {
                    allRelationHist[rh.Key] = (allRelationHist.TryGetValue(rh.Key, out int v) ? v : 0) + rh.Value;
                }
            }

            int allNetwork = Math.Max(0, allTalked - allNotInNetwork);
            int allDistinct = allPartners.Count;

            sb.AppendLine($"[Campaign Summary (All Time: {tally.Days.Count} day(s))]");
            sb.AppendLine($"Total talks: {allTalked} (in network: {allNetwork}, distinct partners: {allDistinct})");
            sb.AppendLine($"Total rumors delivered: {allVolTold + allAskTold} ({allVolTold} volunteered, {allAskTold} asked)");
            sb.AppendLine("Breakdown:");
            AppendBreakdown(sb, allCounts);
            sb.AppendLine();

            sb.AppendLine("Relation Histogram (All Time):");
            AppendHistogram(sb, allRelationHist);
            sb.AppendLine();

            // Recent 7 days breakdown
            int startDay = Math.Max(0, currentDay - 6);
            sb.AppendLine($"[Recent 7 Days (Days {startDay} to {currentDay})]");

            int recentTalked = 0;
            int recentNotInNetwork = 0;
            int recentVolTold = 0;
            int recentAskTold = 0;
            var recentPartners = new HashSet<string>(StringComparer.Ordinal);
            var recentCounts = new Dictionary<string, int>(StringComparer.Ordinal);
            var recentRelationHist = new Dictionary<string, int>(StringComparer.Ordinal);

            for (int d = startDay; d <= currentDay; d++)
            {
                var dt = tally.GetDay(d);
                if (dt != null)
                {
                    foreach (var k in OutcomeKeys) recentTalked += dt.GetCount(k);
                    recentNotInNetwork += dt.GetCount(ListenTallyKeys.NotInNetwork);
                    recentVolTold += dt.GetCount(ListenTallyKeys.VolunteerTold);
                    recentAskTold += dt.GetCount(ListenTallyKeys.AskTold);

                    if (dt.Partners != null)
                    {
                        foreach (var p in dt.Partners) recentPartners.Add(p);
                    }

                    foreach (var c in dt.Counts)
                    {
                        recentCounts[c.Key] = (recentCounts.TryGetValue(c.Key, out int v) ? v : 0) + c.Value;
                    }

                    foreach (var rh in dt.RelationHist)
                    {
                        recentRelationHist[rh.Key] = (recentRelationHist.TryGetValue(rh.Key, out int v) ? v : 0) + rh.Value;
                    }

                    sb.AppendLine($"  Day {d}: {FormatDailyLine(d, dt)}");
                }
                else
                {
                    sb.AppendLine($"  Day {d}: (no talks)");
                }
            }

            int recentNetwork = Math.Max(0, recentTalked - recentNotInNetwork);
            sb.AppendLine($"Recent 7 days total talks: {recentTalked} (in network: {recentNetwork}, distinct partners: {recentPartners.Count})");
            sb.AppendLine($"Recent 7 days delivered: {recentVolTold + recentAskTold} ({recentVolTold} volunteered, {recentAskTold} asked)");
            sb.AppendLine("Recent 7 days relation histogram:");
            AppendHistogram(sb, recentRelationHist);

            return sb.ToString().TrimEnd();
        }

        private static void AppendBreakdown(StringBuilder sb, Dictionary<string, int> counts)
        {
            var orderedKeys = new[]
            {
                ListenTallyKeys.NotInNetwork,
                ListenTallyKeys.NotInNetworkNoHero,
                ListenTallyKeys.VolunteerNotEvaluatedTokenLost,
                ListenTallyKeys.VolunteerNotEvaluatedStartNotReached,
                ListenTallyKeys.VolunteerBlockedCommonerTier,
                ListenTallyKeys.VolunteerBlockedLordAttack,
                ListenTallyKeys.VolunteerBlockedRelationGate,
                ListenTallyKeys.VolunteerBlockedCooldown,
                ListenTallyKeys.VolunteerBlockedDailyCap,
                ListenTallyKeys.VolunteerNoTopicNothingOnFile,
                ListenTallyKeys.VolunteerNoTopicAllForgotten,
                ListenTallyKeys.VolunteerNoTopicAllOutdated,
                ListenTallyKeys.VolunteerNoTopicForgottenOrOutdated,
                ListenTallyKeys.VolunteerFiltered,
                ListenTallyKeys.VolunteerFilteredSecret,
                ListenTallyKeys.VolunteerFilteredFuture,
                ListenTallyKeys.VolunteerFilteredPlayerKnows,
                ListenTallyKeys.VolunteerFilteredOther,
                ListenTallyKeys.VolunteerTold,
                ListenTallyKeys.VolunteerChosenNotDelivered,
                ListenTallyKeys.AskShown,
                ListenTallyKeys.AskBlockedCommonerTier,
                ListenTallyKeys.AskAsked,
                ListenTallyKeys.AskTold,
                ListenTallyKeys.AskRefusedRelationGate,
                ListenTallyKeys.AskRefusedWillingnessGate,
                ListenTallyKeys.AskNoTopicNothingOnFile,
                ListenTallyKeys.AskNoTopicAllForgotten,
                ListenTallyKeys.AskNoTopicAllOutdated,
                ListenTallyKeys.AskNoTopicForgottenOrOutdated,
                ListenTallyKeys.AskFiltered,
                ListenTallyKeys.RecoveryShown,
                ListenTallyKeys.RecoveryUsed
            };

            foreach (var key in orderedKeys)
            {
                int c = counts.TryGetValue(key, out int val) ? val : 0;
                if (c > 0)
                {
                    sb.AppendLine($"  - {key}: {c}");
                }
            }
        }

        private static void AppendHistogram(StringBuilder sb, Dictionary<string, int> hist)
        {
            if (hist == null || hist.Count == 0)
            {
                sb.AppendLine("  (no relation data)");
                return;
            }

            var binOrder = new[]
            {
                "-100..-91", "-90..-81", "-80..-71", "-70..-61", "-60..-51",
                "-50..-41", "-40..-31", "-30..-21", "-20..-11", "-10..-1",
                "0..9", "10..19", "20..29", "30..39", "40..49",
                "50..59", "60..69", "70..79", "80..89", "90..100"
            };

            bool any = false;
            foreach (var bin in binOrder)
            {
                if (hist.TryGetValue(bin, out int count) && count > 0)
                {
                    sb.AppendLine($"  {bin,8}: {count}");
                    any = true;
                }
            }

            if (!any)
            {
                foreach (var kv in hist.OrderBy(x => x.Key, StringComparer.Ordinal))
                {
                    sb.AppendLine($"  {kv.Key,8}: {kv.Value}");
                }
            }
        }
    }
}
