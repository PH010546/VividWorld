#nullable enable

namespace VividWorld.Core.Dialogue
{
    public enum NoTopicReason
    {
        NothingOnFile,         // 名下 0 則
        AllForgotten,          // 有登記、全忘了
        AllOutdated,           // 有登記、全過時
        ForgottenOrOutdated    // 忘了與過時混在一起（或候選載不到），沒剩
    }

    public static class ListenTallyClassifier
    {
        /// <summary>「候選清單是空的」細分成哪一種的**唯一**判定處：主動講、玩家問、預演的「只看話題」都只准呼叫它。</summary>
        public static NoTopicReason ClassifyNoTopic(int knownCount, int forgottenCount, int outdatedCount)
        {
            if (knownCount == 0) return NoTopicReason.NothingOnFile;
            if (forgottenCount == knownCount) return NoTopicReason.AllForgotten;
            if (outdatedCount == knownCount) return NoTopicReason.AllOutdated;
            return NoTopicReason.ForgottenOrOutdated;
        }

        public static string GetRelationBin(int relation)
        {
            if (relation < -100) relation = -100;
            if (relation > 100) relation = 100;

            if (relation >= 90) return "90..100";
            if (relation >= 0)
            {
                int floor = (relation / 10) * 10;
                return $"{floor}..{floor + 9}";
            }
            else
            {
                // Negative: -1 to -10 -> -10..-1; -11 to -20 -> -20..-11, etc.
                int floor = ((relation - 9) / 10) * 10;
                return $"{floor}..{floor + 9}";
            }
        }

        public static string ClassifyVolunteer(
            bool isEligible,
            bool volunteerConditionRan,
            int rivalCount,
            bool commonerTierBlocked,
            bool willLordAttack,
            VolunteerDecision? decision,
            int knownCount,
            int forgottenCount,
            int outdatedCount,
            bool delivered)
        {
            if (!isEligible)
            {
                return ListenTallyKeys.NotInNetwork;
            }

            if (!volunteerConditionRan)
            {
                if (rivalCount > 0)
                {
                    return ListenTallyKeys.VolunteerNotEvaluatedTokenLost;
                }
                return ListenTallyKeys.VolunteerNotEvaluatedStartNotReached;
            }

            if (commonerTierBlocked)
            {
                return ListenTallyKeys.VolunteerBlockedCommonerTier;
            }

            if (willLordAttack)
            {
                return ListenTallyKeys.VolunteerBlockedLordAttack;
            }

            if (decision != null)
            {
                switch (decision.Refusal)
                {
                    case VolunteerRefusal.RelationGate:
                        return ListenTallyKeys.VolunteerBlockedRelationGate;

                    case VolunteerRefusal.Cooldown:
                        return ListenTallyKeys.VolunteerBlockedCooldown;

                    case VolunteerRefusal.DailyCap:
                        return ListenTallyKeys.VolunteerBlockedDailyCap;

                    case VolunteerRefusal.NoKnownEvents:
                        switch (ClassifyNoTopic(knownCount, forgottenCount, outdatedCount))
                        {
                            case NoTopicReason.NothingOnFile: return ListenTallyKeys.VolunteerNoTopicNothingOnFile;
                            case NoTopicReason.AllForgotten: return ListenTallyKeys.VolunteerNoTopicAllForgotten;
                            case NoTopicReason.AllOutdated: return ListenTallyKeys.VolunteerNoTopicAllOutdated;
                            default: return ListenTallyKeys.VolunteerNoTopicForgottenOrOutdated;
                        }

                    case VolunteerRefusal.AllCandidatesFiltered:
                        return ListenTallyKeys.VolunteerFiltered;

                    case VolunteerRefusal.None:
                        if (decision.Offer != null)
                        {
                            return delivered ? ListenTallyKeys.VolunteerTold : ListenTallyKeys.VolunteerChosenNotDelivered;
                        }
                        break;
                }
            }

            return ListenTallyKeys.VolunteerNotEvaluatedStartNotReached;
        }

        public static string ClassifyAsk(
            bool asked,
            bool told,
            AskDecision? decision,
            int knownCount,
            int forgottenCount,
            int outdatedCount)
        {
            if (!asked) return string.Empty;
            if (told) return ListenTallyKeys.AskTold;

            if (decision != null)
            {
                switch (decision.Refusal)
                {
                    case AskRefusal.RelationGate:
                        return ListenTallyKeys.AskRefusedRelationGate;

                    case AskRefusal.WillingnessGate:
                        return ListenTallyKeys.AskRefusedWillingnessGate;

                    case AskRefusal.NoKnownEvents:
                        switch (ClassifyNoTopic(knownCount, forgottenCount, outdatedCount))
                        {
                            case NoTopicReason.NothingOnFile: return ListenTallyKeys.AskNoTopicNothingOnFile;
                            case NoTopicReason.AllForgotten: return ListenTallyKeys.AskNoTopicAllForgotten;
                            case NoTopicReason.AllOutdated: return ListenTallyKeys.AskNoTopicAllOutdated;
                            default: return ListenTallyKeys.AskNoTopicForgottenOrOutdated;
                        }

                    case AskRefusal.AllCandidatesFiltered:
                        return ListenTallyKeys.AskFiltered;
                }
            }

            return ListenTallyKeys.AskFiltered;
        }
    }
}
