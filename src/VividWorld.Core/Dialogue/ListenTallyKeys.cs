#nullable enable

namespace VividWorld.Core.Dialogue
{
    public static class ListenTallyKeys
    {
        // Eligibility
        public const string NotInNetwork = "notInNetwork";
        public const string NotInNetworkNoHero = "notInNetwork.noHero";

        // Volunteer not evaluated
        public const string VolunteerNotEvaluatedTokenLost = "volunteer.notEvaluated.tokenLost";
        public const string VolunteerNotEvaluatedStartNotReached = "volunteer.notEvaluated.startNotReached";

        // Volunteer blocked
        public const string VolunteerBlockedCommonerTier = "volunteer.blocked.commonerTier";
        public const string VolunteerBlockedLordAttack = "volunteer.blocked.lordAttack";
        public const string VolunteerBlockedRelationGate = "volunteer.blocked.relationGate";
        public const string VolunteerBlockedCooldown = "volunteer.blocked.cooldown";
        public const string VolunteerBlockedDailyCap = "volunteer.blocked.dailyCap";

        // Volunteer no topic
        public const string VolunteerNoTopicNothingOnFile = "volunteer.noTopic.nothingOnFile";
        public const string VolunteerNoTopicAllForgotten = "volunteer.noTopic.allForgotten";
        public const string VolunteerNoTopicAllOutdated = "volunteer.noTopic.allOutdated";
        public const string VolunteerNoTopicForgottenOrOutdated = "volunteer.noTopic.forgottenOrOutdated";

        // Volunteer filtered
        public const string VolunteerFiltered = "volunteer.filtered";
        public const string VolunteerFilteredSecret = "volunteer.filtered.secret";
        public const string VolunteerFilteredFuture = "volunteer.filtered.future";
        public const string VolunteerFilteredPlayerKnows = "volunteer.filtered.playerKnows";
        public const string VolunteerFilteredOther = "volunteer.filtered.other";

        // Volunteer delivered / chosen
        public const string VolunteerTold = "volunteer.told";
        public const string VolunteerChosenNotDelivered = "volunteer.chosenNotDelivered";

        // Ask
        public const string AskShown = "ask.shown";
        public const string AskBlockedCommonerTier = "ask.blocked.commonerTier";
        public const string AskAsked = "ask.asked";
        public const string AskTold = "ask.told";
        public const string AskRefusedRelationGate = "ask.refused.relationGate";
        public const string AskRefusedWillingnessGate = "ask.refused.willingnessGate";
        public const string AskNoTopicNothingOnFile = "ask.noTopic.nothingOnFile";
        public const string AskNoTopicAllForgotten = "ask.noTopic.allForgotten";
        public const string AskNoTopicAllOutdated = "ask.noTopic.allOutdated";
        public const string AskNoTopicForgottenOrOutdated = "ask.noTopic.forgottenOrOutdated";
        public const string AskFiltered = "ask.filtered";

        // Recovery
        public const string RecoveryShown = "recovery.shown";
        public const string RecoveryUsed = "recovery.used";
    }
}
