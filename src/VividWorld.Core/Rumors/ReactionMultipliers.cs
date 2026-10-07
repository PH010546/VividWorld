using System;
using VividWorld.Core.Catalog;
using VividWorld.Core.Config;
using VividWorld.Core.Events;

namespace VividWorld.Core.Rumors
{
    /// <summary>
    /// 算「聽的人反應多大」要從遊戲讀的資料。遊戲那一側把查詢包成委派餵進來，Core 不碰遊戲型別。
    /// 欄位沒給（null）就當查不到，對應的倍數是 1。
    /// </summary>
    public sealed class ReactionInputs
    {
        public ReactionConfig Config = new ReactionConfig();

        /// <summary>聽者的個性等級；查不到聽者為 null。</summary>
        public TraitProfile? HearerTraits;

        /// <summary>聽者對這位英雄的好感；任一方查不到為 null。</summary>
        public Func<string, int?>? AffectionToward;

        /// <summary>聽者跟這位英雄是不是同一個家族；任一方查不到就當不同家族。</summary>
        public Func<string, bool>? IsSameClan;
    }

    /// <summary>一條 opinion 對一位聽者算出來的兩個倍數與它們的由來（給日誌用）。</summary>
    public sealed class ReactionMultipliers
    {
        public const string ReasonNone = "none";
        public const string ReasonSelf = "self";
        public const string ReasonSameClan = "same clan";
        public const string ReasonFriend = "friend";
        public const string ReasonHostile = "hostile";

        public double TraitMultiplier = 1.0;
        public string? TraitName;           // opinion 寫的那一項個性（正規化後）；沒寫為 null
        public int? TraitLevel;             // 聽者在那一項的等級；沒寫 trait 或查不到聽者為 null

        public double RelationMultiplier = 1.0;
        public string RelationReason = ReasonNone;
        public string ReceiverHeroId = string.Empty;   // 綁到的承受的人；沒寫 receiver 或綁不到為空
    }

    public static class ReactionCalculator
    {
        public static ReactionMultipliers Compute(OpinionDef def, WorldEvent evt, KnownByEntry observer, ReactionInputs? inputs)
        {
            var result = new ReactionMultipliers();
            if (def == null || inputs == null || inputs.Config == null || !inputs.Config.Enabled)
            {
                return result;
            }

            var cfg = inputs.Config;

            // 個性倍數：等級 −2..+2 查表（索引 = 等級 + 2）
            string? trait = OpinionTraits.Normalize(def.Trait);
            result.TraitName = trait;
            int? level = trait == null ? null : OpinionTraits.LevelOf(trait, inputs.HearerTraits);
            result.TraitLevel = level;
            if (level.HasValue && cfg.TraitMultipliers != null && cfg.TraitMultipliers.Length > 0)
            {
                int index = Math.Max(0, Math.Min(cfg.TraitMultipliers.Length - 1, Math.Max(-2, Math.Min(2, level.Value)) + 2));
                result.TraitMultiplier = cfg.TraitMultipliers[index];
            }

            // 關係倍數：聽者跟承受的人
            if (string.IsNullOrWhiteSpace(def.Receiver) ||
                evt?.Participants == null ||
                !evt.Participants.TryGetValue(def.Receiver!, out var receiverId) ||
                string.IsNullOrEmpty(receiverId))
            {
                return result;
            }

            result.ReceiverHeroId = receiverId;
            if (string.Equals(receiverId, observer?.HeroId, StringComparison.Ordinal))
            {
                result.RelationReason = ReactionMultipliers.ReasonSelf;
                return result;
            }

            if (inputs.IsSameClan != null && inputs.IsSameClan(receiverId))
            {
                result.RelationMultiplier = cfg.ReceiverSameClan;
                result.RelationReason = ReactionMultipliers.ReasonSameClan;
                return result;
            }

            int? affection = inputs.AffectionToward?.Invoke(receiverId);
            if (affection.HasValue && affection.Value >= cfg.ReceiverFriendRelation)
            {
                result.RelationMultiplier = cfg.ReceiverFriend;
                result.RelationReason = ReactionMultipliers.ReasonFriend;
            }
            else if (affection.HasValue && affection.Value <= cfg.ReceiverHostileRelation)
            {
                result.RelationMultiplier = cfg.ReceiverHostile;
                result.RelationReason = ReactionMultipliers.ReasonHostile;
            }

            return result;
        }
    }
}
