using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace VividWorld.Core.Situations
{
    public sealed class SituationPickLogOptions
    {
        /// <summary>true＝挑人那段把全部候選逐行印出（debug.logSituationCandidates、乾跑）；false＝只印前幾組與挑中的那組。</summary>
        public bool AllCandidates;

        /// <summary>英雄 id → 名字，日誌用；null 或查不到就只印 id。</summary>
        public Func<string, string?>? NameOf;
    }

    /// <summary>
    /// 把挑事件的紀錄印成日誌段落：挑種類、挑人、名單。城裡每天的掃描、玩家在場、流浪者事件三處共用。
    /// </summary>
    public static class SituationPickLogFormatter
    {
        /// <summary>挑人那段平常印幾組（比重最高的前幾組）。</summary>
        public const int TopCandidateCount = 5;

        private static string F2(double v) => v.ToString("0.00", CultureInfo.InvariantCulture);
        private static string F4(double v) => v.ToString("0.0000", CultureInfo.InvariantCulture);

        private static string Hero(string? id, SituationPickLogOptions? options)
        {
            if (string.IsNullOrEmpty(id)) return "(none)";
            string? name = options?.NameOf?.Invoke(id!);
            return string.IsNullOrEmpty(name) ? id! : $"{id} ({name})";
        }

        /// <summary>一件的完整段落（開頭、挑種類、挑人、名單）。</summary>
        public static IReadOnlyList<string> Format(SituationPickRecord record, SituationPickLogOptions? options = null)
        {
            var lines = new List<string>();
            if (record == null) return lines;

            lines.Add($"Pick [{record.Site}] day {SituationScanLogFormatter.FormatDay(record.Day)} item {record.Slot + 1}/{record.SlotCount}");

            for (int ri = 0; ri < record.Rounds.Count; ri++)
            {
                var round = record.Rounds[ri];
                string roundLabel = record.Rounds.Count > 1 ? $" (round {ri + 1})" : string.Empty;
                bool anyParticipant = round.Entries.Any(e => e.Participates);
                if (anyParticipant)
                {
                    lines.Add($"  kinds{roundLabel}: total weight {F2(round.TotalWeight)}, r {F4(round.R)}, r*total {F4(round.RTimesTotal)}");
                }
                else
                {
                    lines.Add($"  kinds{roundLabel}: nothing can be picked");
                }

                foreach (var e in round.Entries)
                {
                    lines.Add("    " + FormatKindEntry(e, e.Id == round.PickedKindId && anyParticipant));
                }

                if (round.EnumeratedPickedKind)
                {
                    lines.Add(round.EnumeratedRemaining > 0
                        ? $"    enumerated {round.PickedKindId}: {round.EnumeratedQualified} qualified, {round.EnumeratedQualified - round.EnumeratedRemaining} excluded by roster, {round.EnumeratedRemaining} left"
                        : $"    enumerated {round.PickedKindId}: {round.EnumeratedQualified} qualified, {round.EnumeratedQualified - round.EnumeratedRemaining} excluded by roster, 0 left -> marked as having no candidate, picking again");
                }
            }

            if (record.PickedKindId == null)
            {
                lines.Add($"  stopped: {record.StopReason ?? "nothing to pick"}");
                AppendRoster(lines, record, options);
                return lines;
            }

            lines.Add($"  picked kind: {record.PickedKindId}");
            AppendPerson(lines, record, options);
            AppendRoster(lines, record, options);
            return lines;
        }

        private static string FormatKindEntry(PickKindEntry e, bool picked)
        {
            string head = $"{e.Id} weight {F2(e.Weight)} ({e.WeightSource})";
            switch (e.Status)
            {
                case PickKindStatus.WeightZero:
                    return $"{head}: skipped, weight 0" + CountTail(e);
                case PickKindStatus.NoCandidates:
                    return $"{head}: skipped, 0 qualified ({e.Reason})";
                case PickKindStatus.AllExcludedByRoster:
                    return $"{head}: skipped, {e.Qualified} qualified, {e.ExcludedByRoster} excluded by roster, 0 left ({e.Reason})";
                case PickKindStatus.NotEnumerated:
                    return $"{head}: not enumerated yet (counted as having candidates), p {F4(e.Probability)} [{F2(e.IntervalStart)}, {F2(e.IntervalEnd)})" + (picked ? " <- picked" : string.Empty);
                default:
                    return $"{head}: {e.Qualified} qualified, {e.ExcludedByRoster} excluded by roster, {e.Remaining} left, p {F4(e.Probability)} [{F2(e.IntervalStart)}, {F2(e.IntervalEnd)})" + (picked ? " <- picked" : string.Empty);
            }
        }

        private static string CountTail(PickKindEntry e)
        {
            return e.Qualified < 0 ? string.Empty : $" ({e.Qualified} qualified, {e.ExcludedByRoster} excluded by roster)";
        }

        private static void AppendPerson(List<string> lines, SituationPickRecord record, SituationPickLogOptions? options)
        {
            var stage = record.Person;
            if (stage == null) return;

            string dist = string.Join(", ", stage.Distribution.Select(d => $"{F2(d.Weight)}x{d.Count}"));
            lines.Add($"  people: {stage.Remaining} group(s) left, total weight {F2(stage.TotalWeight)}, weights {dist}");
            if (!stage.UsesTraits)
            {
                lines.Add("    this kind ignores personality (no pickTraits), every group has the same weight");
            }

            bool all = options?.AllCandidates == true;
            IEnumerable<PickPersonEntry> shown;
            if (all)
            {
                shown = stage.Entries.OrderByDescending(e => e.Weight).ThenBy(e => e.Index);
            }
            else
            {
                var top = stage.Entries.OrderByDescending(e => e.Weight).ThenBy(e => e.Index).Take(TopCandidateCount).ToList();
                if (stage.PickedIndex >= 0 && top.All(e => e.Index != stage.PickedIndex))
                {
                    top.Add(stage.Entries[stage.PickedIndex]);
                }
                shown = top;
            }

            foreach (var e in shown)
            {
                lines.Add("    " + FormatPersonEntry(e, e.Index == stage.PickedIndex, options));
            }

            if (!all && stage.Entries.Count > TopCandidateCount)
            {
                lines.Add($"    ({stage.Entries.Count} group(s) in total, set debug.logSituationCandidates to list them all)");
            }

            if (stage.PickedIndex >= 0)
            {
                lines.Add($"    r {F4(stage.R)}, r*total {F4(stage.RTimesTotal)} -> group #{stage.PickedIndex + 1}");
            }
        }

        private static string FormatPersonEntry(PickPersonEntry e, bool picked, SituationPickLogOptions? options)
        {
            var c = e.Candidate;
            var roles = string.Join(", ", c.RoleHeroIds
                .OrderBy(kv => kv.Key, StringComparer.Ordinal)
                .Select(kv => $"{kv.Key}={Hero(kv.Value, options)}"));
            string settlement = string.IsNullOrEmpty(c.SettlementId) ? string.Empty : $"{c.SettlementId}: ";
            var sb = new System.Text.StringBuilder();
            sb.Append('#').Append(e.Index + 1).Append(' ').Append(settlement).Append(roles);

            foreach (var t in e.Terms)
            {
                if (!t.Applies)
                {
                    sb.Append(" | ").Append(t.Role).Append(" x1 (").Append(t.SkipReason).Append(')');
                    continue;
                }
                var parts = string.Join(" ", t.Parts.Select(p => $"{p.Trait} {p.Level}*{F2(p.Coefficient)}"));
                sb.Append(" | ").Append(t.Role).Append(": ").Append(parts).Append(" -> 1+sum=").Append(F2(t.Raw));
                if (t.Clamped) sb.Append(", clamped to ").Append(F2(t.Factor));
            }

            sb.Append(" | weight ").Append(F2(e.Weight)).Append(" [").Append(F2(e.IntervalStart)).Append(", ").Append(F2(e.IntervalEnd)).Append(')');
            if (picked) sb.Append(" <- picked");
            return sb.ToString();
        }

        private static void AppendRoster(List<string> lines, SituationPickRecord record, SituationPickLogOptions? options)
        {
            if (!record.UsesRoster)
            {
                lines.Add("  roster: not used here (people already involved today are not excluded)");
                return;
            }

            if (record.RosterBefore.Count == 0)
            {
                lines.Add("  roster before: empty");
            }
            else
            {
                lines.Add("  roster before: " + string.Join("; ", record.RosterBefore.Select(r => $"{Hero(r.HeroId, options)} [{r.Source}]")));
            }

            if (record.PickedKindId != null)
            {
                lines.Add($"  excluded by roster: {record.ExcludedByRoster} group(s) of {record.PickedKindId}");
                lines.Add("  to be added if it goes ahead: " + (record.HeroesOfPicked.Count == 0 ? "(nobody)" : string.Join(", ", record.HeroesOfPicked.Select(h => Hero(h, options)))));
            }
        }

        /// <summary>每天掃描總結行要的：每一種事各有幾組合格。沒列舉的種類寫 not enumerated。</summary>
        public static string FormatQualifiedCounts(IReadOnlyList<SituationPickKindSummary> summaries, int rosterCount)
        {
            string kinds = summaries == null || summaries.Count == 0
                ? "none"
                : string.Join(", ", summaries.Select(s => s.Qualified < 0 ? $"{s.Id} not enumerated" : $"{s.Id} {s.Qualified}"));
            return $"qualified per kind: {kinds}; roster {rosterCount} hero(es)";
        }
    }
}
