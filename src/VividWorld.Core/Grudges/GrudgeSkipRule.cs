using System;
using VividWorld.Core.Events;

namespace VividWorld.Core.Grudges
{
    public enum GrudgeSkipReason
    {
        None,
        UnboundRole,
        SameHero,
        FromIsPlayer
    }

    public sealed class GrudgeSkipDecision
    {
        public bool ShouldSkip { get; }
        public GrudgeSkipReason ReasonCode { get; }
        public string? Reason { get; }
        public string? LogMessage { get; }

        public GrudgeSkipDecision(bool shouldSkip, GrudgeSkipReason reasonCode, string? reason, string? logMessage)
        {
            ShouldSkip = shouldSkip;
            ReasonCode = reasonCode;
            Reason = reason;
            LogMessage = logMessage;
        }

        public static GrudgeSkipDecision DoNotSkip() =>
            new GrudgeSkipDecision(false, GrudgeSkipReason.None, null, null);

        public static GrudgeSkipDecision Skip(GrudgeSkipReason reasonCode, string reason, string logMessage) =>
            new GrudgeSkipDecision(true, reasonCode, reason, logMessage);
    }

    public static class GrudgeSkipRule
    {
        public static GrudgeSkipDecision Evaluate(
            GrudgeSource source,
            string? fromHeroId,
            string? aboutHeroId,
            string? fromRole,
            string? toRole,
            string? playerHeroId,
            string? eventId = null)
        {
            // 1. FromHeroId / AboutHeroId 任一為空 => 跳過
            if (string.IsNullOrEmpty(fromHeroId) || string.IsNullOrEmpty(aboutHeroId))
            {
                string unboundRole = string.IsNullOrEmpty(fromHeroId)
                    ? (fromRole ?? string.Empty)
                    : (toRole ?? string.Empty);
                string reason = $"role '{unboundRole}' is unbound";
                string logMessage = source == GrudgeSource.Rumor
                    ? GrudgeLogFormatter.FormatOpinionSkipped(fromHeroId ?? string.Empty, toRole ?? string.Empty, eventId ?? string.Empty, reason)
                    : GrudgeLogFormatter.FormatSkippedUnboundRole(fromRole ?? string.Empty, fromHeroId ?? string.Empty, toRole ?? string.Empty, aboutHeroId ?? string.Empty, unboundRole);

                return GrudgeSkipDecision.Skip(GrudgeSkipReason.UnboundRole, reason, logMessage);
            }

            // 2. 兩者相同 => 跳過
            if (string.Equals(fromHeroId, aboutHeroId, StringComparison.Ordinal))
            {
                if (source == GrudgeSource.Rumor)
                {
                    string reason = "target is the observer self";
                    string logMessage = GrudgeLogFormatter.FormatOpinionSkipped(fromHeroId ?? string.Empty, toRole ?? string.Empty, eventId ?? string.Empty, reason);
                    return GrudgeSkipDecision.Skip(GrudgeSkipReason.SameHero, reason, logMessage);
                }
                else
                {
                    string reason = "from and about are the same hero";
                    string logMessage = $"grudge skipped {fromRole} {fromHeroId} -> {toRole} {aboutHeroId}: from and about are the same hero";
                    return GrudgeSkipDecision.Skip(GrudgeSkipReason.SameHero, reason, logMessage);
                }
            }

            // 3. 出發是玩家 => 跳過
            if (!string.IsNullOrEmpty(playerHeroId) && string.Equals(fromHeroId, playerHeroId, StringComparison.Ordinal))
            {
                if (source == GrudgeSource.Rumor)
                {
                    string reason = "observer is the player";
                    string logMessage = GrudgeLogFormatter.FormatOpinionSkipped(fromHeroId ?? string.Empty, toRole ?? string.Empty, eventId ?? string.Empty, reason);
                    return GrudgeSkipDecision.Skip(GrudgeSkipReason.FromIsPlayer, reason, logMessage);
                }
                else
                {
                    string reason = $"{fromHeroId} is the player";
                    string logMessage = GrudgeLogFormatter.FormatSkippedPlayer(fromRole ?? string.Empty, fromHeroId!, toRole ?? string.Empty, aboutHeroId ?? string.Empty, playerHeroId!);
                    return GrudgeSkipDecision.Skip(GrudgeSkipReason.FromIsPlayer, reason, logMessage);
                }
            }

            // 4. 對象是玩家 => 不跳過（情境與消息皆允許）
            return GrudgeSkipDecision.DoNotSkip();
        }
    }
}
