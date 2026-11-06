using System.Globalization;

namespace VividWorld.Core.Rumors
{
    public static class SecretSpreadLogFormatter
    {
        public static string FormatCandidateExcluded(string name, string id, SecretCandidateExcludeReason reason)
        {
            string reasonStr = reason switch
            {
                SecretCandidateExcludeReason.AlreadyKnows => "already knows",
                SecretCandidateExcludeReason.Harmed => "harmed",
                SecretCandidateExcludeReason.BelowTrustLine => "below trust line",
                _ => reason.ToString()
            };

            return string.Format(CultureInfo.InvariantCulture,
                "secret confide candidate excluded: {0} ({1}) -> {2}",
                name, id, reasonStr);
        }

        public static string FormatRoll(string action, string eventId, string eventType, string name, string id, SecretRollBreakdown roll)
        {
            if (roll == null) return string.Empty;

            return string.Format(CultureInfo.InvariantCulture,
                "secret {0} roll: {1} {2} by {3} ({4}) p={5:0.0000} (base={6:0.000}, decay={7:0.000}, weight={8:0.00}, relation={9:0.00}, traits={10:0.00}) -> {11}",
                action,
                eventId,
                eventType,
                name,
                id,
                roll.Probability,
                roll.BaseChance,
                roll.Decay,
                roll.WeightDiscount,
                roll.RelationMultiplier,
                roll.TraitMultiplier,
                roll.Hit ? "hit" : "miss");
        }

        public static string FormatConfideHit(string eventId, string eventType, string tellerName, string tellerId, string targetName, string targetId)
        {
            return string.Format(CultureInfo.InvariantCulture,
                "secret confide hit: {0} {1} by {2} ({3}) -> {4} ({5})",
                eventId, eventType, tellerName, tellerId, targetName, targetId);
        }

        public static string FormatLeakHit(string eventId, string eventType, string leakerName, string leakerId)
        {
            return string.Format(CultureInfo.InvariantCulture,
                "secret leak hit: {0} {1} by {2} ({3})",
                eventId, eventType, leakerName, leakerId);
        }

        public static string FormatUnrolled(string action, string eventId, string eventType, string name, string id, string reason)
        {
            return string.Format(CultureInfo.InvariantCulture,
                "secret {0} skip: {1} {2} by {3} ({4}): {5}",
                action, eventId, eventType, name, id, reason);
        }

        public static string FormatDailyEvaluation(SecretSpreadEvaluation eval)
        {
            if (eval?.Event == null) return string.Empty;
            var sb = new System.Text.StringBuilder();
            sb.Append(string.Format(CultureInfo.InvariantCulture,
                "[SecretSpread] Day {0:0.0} Event {1} ({2}): {3} knower(s) evaluated",
                eval.Day, eval.Event.EventId, eval.Event.Type ?? string.Empty, eval.Results?.Count ?? 0));

            if (eval.Results != null)
            {
                foreach (var r in eval.Results)
                {
                    sb.AppendLine();
                    sb.Append(string.Format(CultureInfo.InvariantCulture,
                        "  knower {0}: {1}", r.KnowerHeroId, r.Action));
                    if (!string.IsNullOrEmpty(r.TargetHeroId))
                    {
                        sb.Append(string.Format(CultureInfo.InvariantCulture, " -> target {0}", r.TargetHeroId));
                    }
                    if (r.ConfideRoll != null)
                    {
                        sb.Append(string.Format(CultureInfo.InvariantCulture, " [confide p={0:0.0000} hit={1}]", r.ConfideRoll.Probability, r.ConfideRoll.Hit));
                    }
                    if (r.LeakRoll != null)
                    {
                        sb.Append(string.Format(CultureInfo.InvariantCulture, " [leak p={0:0.0000} hit={1}]", r.LeakRoll.Probability, r.LeakRoll.Hit));
                    }
                }
            }
            return sb.ToString();
        }
    }
}
