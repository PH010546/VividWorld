using System;
using VividWorld.Core.Events;

namespace VividWorld.Core.Grudges
{
    public enum PlayerRelationNoticeKind
    {
        Person,
        Clan
    }

    public enum PlayerRelationDirection
    {
        Increased,
        Decreased
    }

    public sealed class PlayerRelationNotice
    {
        public bool ShouldNotify { get; set; }
        public string? SkipReason { get; set; }
        public string OtherHeroId { get; set; } = string.Empty;
        public string OriginalFromHeroId { get; set; } = string.Empty;
        public PlayerRelationNoticeKind Kind { get; set; }
        public PlayerRelationDirection Direction { get; set; }
        public int Magnitude { get; set; }
        public int Delta { get; set; }
    }

    public static class PlayerRelationNoticeRule
    {
        public const string ReasonNotAboutPlayer = "not about the player";
        public const string ReasonRumor = "rumor";
        public const string ReasonLedgerOnly = "ledger only";
        public const string ReasonNoChange = "no change (at the relation limit)";

        /// <summary>
        /// 判定好感度變更是否需要向玩家跳出通知。
        /// 只有情境來源、實際寫入且涉及玩家、且變更值非零時才跳通知。
        /// </summary>
        public static PlayerRelationNotice Evaluate(
            GrudgeSource source,
            bool ledgerOnly,
            string? targetAId,
            string? targetBId,
            string? fromHeroId,
            string? aboutHeroId,
            string? playerHeroId,
            int delta)
        {
            if (string.IsNullOrEmpty(playerHeroId) ||
                (!string.Equals(targetAId, playerHeroId, StringComparison.Ordinal) &&
                 !string.Equals(targetBId, playerHeroId, StringComparison.Ordinal)))
            {
                return new PlayerRelationNotice
                {
                    ShouldNotify = false,
                    SkipReason = ReasonNotAboutPlayer,
                    Delta = delta
                };
            }

            string otherHeroId = string.Equals(targetAId, playerHeroId, StringComparison.Ordinal)
                ? (targetBId ?? string.Empty)
                : (targetAId ?? string.Empty);

            string originalOtherHeroId = string.Equals(fromHeroId, playerHeroId, StringComparison.Ordinal)
                ? (aboutHeroId ?? string.Empty)
                : (fromHeroId ?? string.Empty);

            if (source != GrudgeSource.Situation)
            {
                return new PlayerRelationNotice
                {
                    ShouldNotify = false,
                    SkipReason = ReasonRumor,
                    OtherHeroId = otherHeroId,
                    OriginalFromHeroId = fromHeroId ?? string.Empty,
                    Delta = delta
                };
            }

            if (ledgerOnly)
            {
                return new PlayerRelationNotice
                {
                    ShouldNotify = false,
                    SkipReason = ReasonLedgerOnly,
                    OtherHeroId = otherHeroId,
                    OriginalFromHeroId = fromHeroId ?? string.Empty,
                    Delta = delta
                };
            }

            if (delta == 0)
            {
                return new PlayerRelationNotice
                {
                    ShouldNotify = false,
                    SkipReason = ReasonNoChange,
                    OtherHeroId = otherHeroId,
                    OriginalFromHeroId = fromHeroId ?? string.Empty,
                    Delta = 0
                };
            }

            var kind = string.Equals(otherHeroId, originalOtherHeroId, StringComparison.Ordinal)
                ? PlayerRelationNoticeKind.Person
                : PlayerRelationNoticeKind.Clan;

            var direction = delta > 0
                ? PlayerRelationDirection.Increased
                : PlayerRelationDirection.Decreased;

            return new PlayerRelationNotice
            {
                ShouldNotify = true,
                SkipReason = null,
                OtherHeroId = otherHeroId,
                OriginalFromHeroId = fromHeroId ?? string.Empty,
                Kind = kind,
                Direction = direction,
                Magnitude = Math.Abs(delta),
                Delta = delta
            };
        }
    }
}
