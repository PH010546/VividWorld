#nullable enable
using System;
using System.Collections.Generic;
using System.Text;

namespace VividWorld.Core.Dialogue
{
    public static class ListenPreviewLogFormatter
    {
        private static readonly string[] VolunteerOrder = new[]
        {
            ListenTallyKeys.VolunteerTold,
            ListenTallyKeys.VolunteerBlockedCommonerTier,
            ListenTallyKeys.VolunteerBlockedRelationGate,
            ListenTallyKeys.VolunteerBlockedCooldown,
            ListenTallyKeys.VolunteerBlockedDailyCap,
            ListenTallyKeys.VolunteerNoTopicNothingOnFile,
            ListenTallyKeys.VolunteerNoTopicAllForgotten,
            ListenTallyKeys.VolunteerNoTopicAllOutdated,
            ListenTallyKeys.VolunteerNoTopicForgottenOrOutdated,
            ListenTallyKeys.VolunteerFiltered
        };

        private static readonly string[] TopicOnlyOrder = new[]
        {
            "hasTopic",
            "nothingOnFile",
            "allForgotten",
            "allOutdated",
            "forgottenOrOutdated",
            "filtered"
        };

        private static readonly string[] AskOrder = new[]
        {
            ListenTallyKeys.AskTold,
            ListenTallyKeys.AskRefusedRelationGate,
            ListenTallyKeys.AskRefusedWillingnessGate,
            ListenTallyKeys.AskNoTopicNothingOnFile,
            ListenTallyKeys.AskNoTopicAllForgotten,
            ListenTallyKeys.AskNoTopicAllOutdated,
            ListenTallyKeys.AskNoTopicForgottenOrOutdated,
            ListenTallyKeys.AskFiltered
        };

        private static readonly string[] AskRefusalOrder = new[]
        {
            "told",
            nameof(AskRefusalLineKind.Unwilling),
            nameof(AskRefusalLineKind.NothingHeard),
            nameof(AskRefusalLineKind.Forgotten),
            nameof(AskRefusalLineKind.Outdated),
            nameof(AskRefusalLineKind.PlayerKnowsAll),
            nameof(AskRefusalLineKind.Other)
        };

        private static readonly string[] RelationBins = new[]
        {
            "-100..-91", "-90..-81", "-80..-71", "-70..-61", "-60..-51",
            "-50..-41", "-40..-31", "-30..-21", "-20..-11", "-10..-1",
            "0..9", "10..19", "20..29", "30..39", "40..49",
            "50..59", "60..69", "70..79", "80..89", "90..100"
        };

        public static string FormatSummary(ListenPreviewResult result, bool includeTopNames = false, string trigger = "dev")
        {
            if (result == null) throw new ArgumentNullException(nameof(result));

            var sb = new StringBuilder();
            sb.AppendLine("=== Listen Preview (What if I talk to everyone right now?) ===");
            if (!string.IsNullOrEmpty(result.ModeLine))
            {
                sb.AppendLine(result.ModeLine);
            }
            sb.AppendLine($"Day {result.Day} | trigger: {trigger}");
            sb.AppendLine($"Network: {result.TotalNetworkCount} heroes ({result.LordCount} lords, {result.WandererCount} wanderers) | Elapsed: {result.ElapsedMilliseconds}ms");
            sb.AppendLine($"Volunteers today: {result.VolunteersToday}/{result.MaxVolunteersPerDay} | Relation >= {result.VolunteerRelationGate}: {result.HeroesMeetingVolunteerRelationGate} heroes");
            sb.AppendLine($"Relation >= {result.ChatRelationGate} (chat gate): {result.HeroesMeetingChatRelationGate} heroes");
            sb.AppendLine($"Notice: {result.LordAttackNote} | Unstamped entries: {result.UnstampedEntriesCount}");
            sb.AppendLine();

            sb.AppendLine("[1. Volunteer Path (as-is)]");
            AppendCategoryList(sb, result.VolunteerCounts, result.VolunteerNames, VolunteerOrder, includeTopNames, result);
            sb.AppendLine();

            sb.AppendLine("[2. Volunteer Path (Topic Only - ignoring relation, cooldown, daily cap)]");
            AppendCategoryList(sb, result.TopicOnlyCounts, result.TopicOnlyNames, TopicOnlyOrder, includeTopNames);
            sb.AppendLine();

            string askClanStatus = string.IsNullOrEmpty(result.AskClanTierStatus)
                ? (result.AskBlockedByClanTier ? "BLOCKED" : "allowed")
                : result.AskClanTierStatus;
            sb.AppendLine($"[3. Ask Path (Commoner clan tier: {askClanStatus})]");
            if (result.AskBlockedByClanTier)
            {
                sb.AppendLine("  (the ask line is hidden for everyone right now; counts below are as if the clan-tier gate passed)");
            }
            AppendCategoryList(sb, result.AskCounts, result.AskNames, AskOrder, includeTopNames);
            sb.AppendLine();

            string askRefusalHeader = result.AskBlockedByClanTier
                ? "[3b. Ask refusal lines (as if the clan-tier gate passed)]"
                : "[3b. Ask refusal lines]";
            sb.AppendLine(askRefusalHeader);
            AppendCategoryList(sb, result.AskRefusalCounts, result.AskRefusalNames, AskRefusalOrder, includeTopNames);
            sb.AppendLine();

            sb.AppendLine("[4. Relation Histogram]");
            AppendHistogram(sb, result.RelationHist, result.RelationHistNames, includeTopNames);

            return sb.ToString().TrimEnd();
        }

        public static string FormatHeroTable(ListenPreviewResult result)
        {
            if (result == null) throw new ArgumentNullException(nameof(result));

            var sb = new StringBuilder();
            sb.AppendLine($"=== Listen Preview Person Details ({result.HeroDetails.Count} heroes) ===");
            sb.AppendLine("Hero | Type | Rel | Volunteer | TopicOnly | Ask");
            foreach (var h in result.HeroDetails)
            {
                string type = h.IsLord ? "Lord" : "Wanderer";
                sb.AppendLine($"{h.HeroName} ({h.HeroId}) | {type} | Rel: {h.Relation} | {h.VolunteerOutcome} | {h.TopicOnlyOutcome} | {h.AskOutcome}");
            }
            return sb.ToString().TrimEnd();
        }

        private static void AppendCategoryList(
            StringBuilder sb,
            Dictionary<string, int> counts,
            Dictionary<string, List<string>> names,
            string[] order,
            bool includeTopNames,
            ListenPreviewResult? result = null)
        {
            foreach (var key in order)
            {
                int count = counts.TryGetValue(key, out int c) ? c : 0;
                string line = $"  - {key}: {count}";
                if (key == ListenTallyKeys.VolunteerTold && result != null)
                {
                    line += $" (full {result.VolunteerToldFullCount}, gist {result.VolunteerToldGistCount})";
                }
                if (includeTopNames && names.TryGetValue(key, out var list) && list.Count > 0)
                {
                    line += $" ({string.Join(", ", list)})";
                }
                sb.AppendLine(line);
            }
        }

        private static void AppendHistogram(
            StringBuilder sb,
            Dictionary<string, int> hist,
            Dictionary<string, List<string>> names,
            bool includeTopNames)
        {
            bool any = false;
            foreach (var bin in RelationBins)
            {
                if (hist.TryGetValue(bin, out int count) && count > 0)
                {
                    string line = $"  {bin,8}: {count}";
                    if (includeTopNames && names.TryGetValue(bin, out var list) && list.Count > 0)
                    {
                        line += $" ({string.Join(", ", list)})";
                    }
                    sb.AppendLine(line);
                    any = true;
                }
            }

            if (!any)
            {
                sb.AppendLine("  (no relation data)");
            }
        }
    }
}
