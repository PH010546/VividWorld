#nullable enable
using System;

namespace VividWorld.Core.Dialogue
{
    public enum AskRefusalLineKind
    {
        Unwilling,
        NothingHeard,
        Forgotten,
        Outdated,
        PlayerKnowsAll,
        Other
    }

    public static class AskRefusalLine
    {
        public const string KeyUnwilling = "VividWorld_AskRefuseUnwilling";
        public const string KeyNothingHeard = "VividWorld_AskRefuseNothingHeard";
        public const string KeyForgotten = "VividWorld_AskRefuseForgotten";
        public const string KeyOutdated = "VividWorld_AskRefuseOutdated";
        public const string KeyPlayerKnows = "VividWorld_AskRefusePlayerKnows";

        public const string FallbackUnwilling = "That's not something I'd care to discuss.";
        public const string FallbackNothingHeard = "I haven't heard any news lately.";
        public const string FallbackForgotten = "Someone mentioned something... I can't quite recall what.";
        public const string FallbackOutdated = "Whatever I heard is likely no longer the case.";
        public const string FallbackPlayerKnows = "Whatever I know, you've surely heard already.";

        /// <summary>
        /// 問傳聞被拒時選句的唯一判定處（規格 §11.2、§11.3）。
        /// 條件函式、兜底日誌、預演三處都呼叫它。
        /// </summary>
        public static AskRefusalLineKind Choose(AskDecision? decision, int knownCount, int forgottenCount, int outdatedCount)
        {
            if (decision == null) return AskRefusalLineKind.Other;
            if (decision.Offer != null) return AskRefusalLineKind.Other;

            switch (decision.Refusal)
            {
                case AskRefusal.RelationGate:
                case AskRefusal.WillingnessGate:
                    return AskRefusalLineKind.Unwilling;

                case AskRefusal.NoKnownEvents:
                    var noTopic = ListenTallyClassifier.ClassifyNoTopic(knownCount, forgottenCount, outdatedCount);
                    switch (noTopic)
                    {
                        case NoTopicReason.NothingOnFile:
                            return AskRefusalLineKind.NothingHeard;
                        case NoTopicReason.AllForgotten:
                        case NoTopicReason.ForgottenOrOutdated:
                            return AskRefusalLineKind.Forgotten;
                        case NoTopicReason.AllOutdated:
                            return AskRefusalLineKind.Outdated;
                        default:
                            return AskRefusalLineKind.Other;
                    }

                case AskRefusal.AllCandidatesFiltered:
                    if (decision.FilteredPlayerKnows > 0)
                    {
                        return AskRefusalLineKind.PlayerKnowsAll;
                    }
                    return AskRefusalLineKind.Other;

                case AskRefusal.None:
                default:
                    return AskRefusalLineKind.Other;
            }
        }

        public static string? GetStringKey(AskRefusalLineKind kind)
        {
            switch (kind)
            {
                case AskRefusalLineKind.Unwilling: return KeyUnwilling;
                case AskRefusalLineKind.NothingHeard: return KeyNothingHeard;
                case AskRefusalLineKind.Forgotten: return KeyForgotten;
                case AskRefusalLineKind.Outdated: return KeyOutdated;
                case AskRefusalLineKind.PlayerKnowsAll: return KeyPlayerKnows;
                default: return null;
            }
        }

        public static string? GetEnglishFallback(AskRefusalLineKind kind)
        {
            switch (kind)
            {
                case AskRefusalLineKind.Unwilling: return FallbackUnwilling;
                case AskRefusalLineKind.NothingHeard: return FallbackNothingHeard;
                case AskRefusalLineKind.Forgotten: return FallbackForgotten;
                case AskRefusalLineKind.Outdated: return FallbackOutdated;
                case AskRefusalLineKind.PlayerKnowsAll: return FallbackPlayerKnows;
                default: return null;
            }
        }

        public static string FormatRefusedLog(
            string heroName,
            string heroId,
            string lineKey,
            AskDecision? decision,
            int knownCount,
            int forgottenCount,
            int outdatedCount)
        {
            string refusalReason = decision?.Refusal.ToString() ?? "Unknown";
            int notVis = decision?.FilteredNotVisible ?? 0;
            int future = decision?.FilteredFutureTimeline ?? 0;
            int playerKnows = decision?.FilteredPlayerKnows ?? 0;
            int other = decision?.FilteredOther ?? 0;

            return $"Ask refused line: {heroName} ({heroId}) -> {lineKey} (refusal {refusalReason}, known {knownCount}, forgotten {forgottenCount}, outdated {outdatedCount}, filtered notVisible {notVis} / future {future} / playerKnows {playerKnows} / other {other})";
        }

        public static string FormatFallbackReason(AskDecision? decision, bool offerRenderedEmpty)
        {
            if (offerRenderedEmpty)
            {
                return "offer rendered empty";
            }

            string refusalReason = decision?.Refusal.ToString() ?? "Unknown";
            int notVis = decision?.FilteredNotVisible ?? 0;
            int future = decision?.FilteredFutureTimeline ?? 0;
            int playerKnows = decision?.FilteredPlayerKnows ?? 0;
            int other = decision?.FilteredOther ?? 0;

            return $"no dedicated line (refusal {refusalReason}, filtered notVisible {notVis} / future {future} / playerKnows {playerKnows} / other {other})";
        }

        public static string FormatFallbackLog(string heroName, string heroId, string reason)
        {
            return $"Ask fallback line: {heroName} ({heroId}) - {reason}";
        }
    }
}
