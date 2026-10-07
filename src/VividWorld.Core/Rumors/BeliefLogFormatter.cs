using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace VividWorld.Core.Rumors
{
    /// <summary>信不信的日誌行。每一行都要看得出為什麼：輸入、每一項加減（含 0）、合計、擲出的值、結果、最重的一項。</summary>
    public static class BeliefLogFormatter
    {
        private static string Signed(double v) => v.ToString("+0.##;-0.##;+0", CultureInfo.InvariantCulture);

        private static string Num(double v) => v.ToString("0.##", CultureInfo.InvariantCulture);

        private static string Aff(int? a) => a.HasValue ? a.Value.ToString(CultureInfo.InvariantCulture) : "n/a";

        public static string FormatJudged(BeliefInputs input, BeliefResult r)
        {
            if (!r.Rolled)
            {
                if (r.Heaviest == BeliefReason.KnowsTruth || input.KnowsTruth)
                {
                    string extra = string.IsNullOrEmpty(input.KnowsTruthReason) ? "" : $" ({input.KnowsTruthReason})";
                    return string.Format(CultureInfo.InvariantCulture,
                        "belief {0} on {1}: does not believe without a roll (knows the truth{2}) | reason {3}",
                        input.HearerId, input.EventId, extra, r.Heaviest);
                }
                return FormatAlwaysBelieves(input, r);
            }

            var sb = new StringBuilder();
            sb.Append("belief ").Append(input.HearerId).Append(" on ").Append(input.EventId)
              .Append(": subject ").Append(input.SubjectId)
              .Append(", told by ").Append(string.IsNullOrEmpty(input.TellerId) ? "(nobody)" : input.TellerId)
              .Append(", hop ").Append(input.Hop.ToString(CultureInfo.InvariantCulture))
              .Append(" | base ").Append(Num(r.Base))
              .Append(" subjectRelation ").Append(Signed(r.SubjectRelation))
              .Append(" (affection ").Append(Aff(input.SubjectAffection)).Append(')')
              .Append(" tellerRelation ").Append(Signed(r.TellerRelation))
              .Append(" (affection ").Append(Aff(input.TellerAffection)).Append(')')
              .Append(" traitFit ").Append(Signed(r.TraitFit))
              .Append(" (trait ").Append(input.Trait ?? "none")
              .Append(" level ").Append(input.SubjectTraitLevel.HasValue ? input.SubjectTraitLevel.Value.ToString(CultureInfo.InvariantCulture) : "n/a")
              .Append(" amount ").Append(Num(input.OpinionAmount)).Append(')')
              .Append(" listenerNature ").Append(Signed(r.ListenerNature))
              .Append(" (calculating ").Append(input.ListenerCalculating.ToString(CultureInfo.InvariantCulture)).Append(')')
              .Append(" distance ").Append(Signed(r.Distance))
              .Append(" response ").Append(Signed(r.Response))
              .Append(" | chance ").Append(Num(r.Chance)).Append('%');
            if (r.Chance != r.RawChance)
            {
                sb.Append(" (clamped from ").Append(Num(r.RawChance)).Append(')');
            }
            sb.Append(" roll ").Append(Num(r.Roll))
              .Append(" round ").Append(input.Round.ToString(CultureInfo.InvariantCulture))
              .Append(" => ").Append(r.Believes ? "believes" : "does not believe")
              .Append(" | heaviest ").Append(r.Heaviest);
            return sb.ToString();
        }

        public static string FormatAlwaysBelieves(BeliefInputs input, BeliefResult r)
        {
            string why = input.HearerIsParticipant ? "a party to the event" : "hop 0, saw it first-hand";
            return string.Format(CultureInfo.InvariantCulture,
                "belief {0} on {1}: believes without a roll ({2}) | reason {3}",
                input.HearerId, input.EventId, why, r.Heaviest);
        }

        public static string FormatLegacy(string hearerId, string eventId)
        {
            return string.Format(CultureInfo.InvariantCulture,
                "belief {0} on {1}: believes without a roll (opinions on this event were settled before belief was judged) | reason {2}",
                hearerId, eventId, BeliefJudge.LegacyReason);
        }

        /// <summary>不信而跳過結算：寫明跳過了哪幾條 opinion（角色、對誰、原本多少）。</summary>
        public static string FormatSkipped(string hearerId, string eventId,
            IReadOnlyList<string> roles, IReadOnlyList<string> heroIds, IReadOnlyList<double> amounts)
        {
            var sb = new StringBuilder();
            sb.Append("belief ").Append(hearerId).Append(" on ").Append(eventId)
              .Append(": does not believe it, so no opinion was settled this round. Skipped:");
            for (int i = 0; i < roles.Count; i++)
            {
                sb.Append(i == 0 ? " " : ", ")
                  .Append(roles[i]).Append('=').Append(string.IsNullOrEmpty(heroIds[i]) ? "(unbound)" : heroIds[i])
                  .Append(" (template amount ").Append(Num(amounts[i])).Append(')');
            }
            return sb.ToString();
        }

        public static string FormatBeliefDisabled(int hearerCount)
        {
            return string.Format(CultureInfo.InvariantCulture,
                "belief judging disabled: {0} hearer(s) settled as if they all believed this tick",
                hearerCount);
        }

        /// <summary>講述者不信而少傳：併進既有的講述者日誌那一行。</summary>
        public static string FormatTellerMultiplier(double multiplier)
        {
            return string.Format(CultureInfo.InvariantCulture, "disbeliever tell x{0}", Num(multiplier));
        }

        /// <summary>dev 工具一則事件一行；沒判過寫 not judged。</summary>
        public static string FormatDevLine(string eventId, string eventType, string subjectId, bool? believes, double? chance, string? reason, double? day)
        {
            string subject = string.IsNullOrEmpty(subjectId) ? "(unbound)" : subjectId;
            if (believes == null)
            {
                return string.Format(CultureInfo.InvariantCulture,
                    "{0} type {1} about {2}: not judged", eventId, eventType, subject);
            }
            return string.Format(CultureInfo.InvariantCulture,
                "{0} type {1} about {2}: {3}, chance {4}, reason {5}, judged on day {6}",
                eventId, eventType, subject,
                believes.Value ? "believes" : "does not believe",
                chance.HasValue ? Num(chance.Value) + "%" : "n/a (no roll)",
                reason ?? "n/a",
                day.HasValue ? day.Value.ToString("F1", CultureInfo.InvariantCulture) : "n/a");
        }
    }
}
