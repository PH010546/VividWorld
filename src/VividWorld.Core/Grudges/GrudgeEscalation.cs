using System;
using System.Globalization;
using VividWorld.Core.Config;

namespace VividWorld.Core.Grudges
{
    public static class GrudgeEscalation
    {
        public static EscalationDecision Decide(
            EscalationFacts facts,
            double triggeringRequested,
            double personalValueAfterDecay,
            bool branchDeclaresClan,
            bool alreadyEscalated,
            string? escalatedEventId,
            double escalatedDay,
            SituationsConfig cfg)
        {
            if (facts == null)
            {
                return new EscalationDecision
                {
                    Escalate = false,
                    Reason = "facts are null"
                };
            }

            // 1. alreadyEscalated
            if (alreadyEscalated)
            {
                return new EscalationDecision
                {
                    Escalate = false,
                    Reason = string.Format(CultureInfo.InvariantCulture,
                        "this pair already escalated on day {0:0.0} ({1})",
                        escalatedDay, escalatedEventId ?? "unknown")
                };
            }

            // 2. AClanId or BClanId is null
            if (string.IsNullOrEmpty(facts.AClanId))
            {
                return new EscalationDecision
                {
                    Escalate = false,
                    Reason = $"{facts.AHeroId} has no clan"
                };
            }
            if (string.IsNullOrEmpty(facts.BClanId))
            {
                return new EscalationDecision
                {
                    Escalate = false,
                    Reason = $"{facts.BHeroId} has no clan"
                };
            }

            // 3. AClanId == BClanId
            if (string.Equals(facts.AClanId, facts.BClanId, StringComparison.Ordinal))
            {
                return new EscalationDecision
                {
                    Escalate = false,
                    Reason = $"both in {facts.AClanId}"
                };
            }

            // 4. ALeaderId or BLeaderId is null
            if (string.IsNullOrEmpty(facts.ALeaderId))
            {
                return new EscalationDecision
                {
                    Escalate = false,
                    Reason = $"clan {facts.AClanId} has no leader"
                };
            }
            if (string.IsNullOrEmpty(facts.BLeaderId))
            {
                return new EscalationDecision
                {
                    Escalate = false,
                    Reason = $"clan {facts.BClanId} has no leader"
                };
            }

            // 5. ALeaderId == BLeaderId
            if (string.Equals(facts.ALeaderId, facts.BLeaderId, StringComparison.Ordinal))
            {
                return new EscalationDecision
                {
                    Escalate = false,
                    Reason = $"{facts.AClanId} and {facts.BClanId} share the same leader {facts.ALeaderId}"
                };
            }

            // 6. If branch does not declare clan: must be clan leader or kin of clan leader
            if (!branchDeclaresClan && !facts.AIsClanLeader && !facts.AIsKinOfOwnLeader)
            {
                return new EscalationDecision
                {
                    Escalate = false,
                    Reason = $"{facts.AHeroId} is neither clan leader nor kin of clan leader {facts.ALeaderId}"
                };
            }

            // 7. If branch does not declare clan: |personalValueAfterDecay| >= threshold
            int threshold = cfg?.ClanEscalationThreshold ?? 30;
            if (!branchDeclaresClan && Math.Abs(personalValueAfterDecay) < threshold)
            {
                return new EscalationDecision
                {
                    Escalate = false,
                    Reason = string.Format(CultureInfo.InvariantCulture,
                        "personal {0:0.0} < threshold {1}",
                        personalValueAfterDecay, threshold)
                };
            }

            // All checks passed -> Escalate!
            double factor = cfg?.ClanEscalationFactor ?? 0.5;
            double amount = triggeringRequested * factor;
            string reason;

            if (branchDeclaresClan)
            {
                reason = "branch declares escalate=clan";
            }
            else if (facts.AIsClanLeader)
            {
                reason = string.Format(CultureInfo.InvariantCulture,
                    "{0} is clan leader, personal {1:0.0} >= threshold {2}",
                    facts.AHeroId, personalValueAfterDecay, threshold);
            }
            else
            {
                reason = string.Format(CultureInfo.InvariantCulture,
                    "{0} is {1} of clan leader {2}, personal {3:0.0} >= threshold {4}",
                    facts.AHeroId, facts.AKinRelation ?? "kin", facts.ALeaderId, personalValueAfterDecay, threshold);
            }

            return new EscalationDecision
            {
                Escalate = true,
                Reason = reason,
                Amount = amount,
                LeaderA = facts.ALeaderId,
                LeaderB = facts.BLeaderId
            };
        }
    }
}
