#nullable enable
using VividWorld.Core.Feelings;

namespace VividWorld.Core.Dialogue
{
    /// <summary>
    /// 對話層向遊戲或測試環境查詢親屬、地位與關係的介面。
    /// 承繼 IFeelingWorld，Core 不碰遊戲組件。
    /// </summary>
    public interface IDialogueWorld : IFeelingWorld
    {
        /// <summary>玩家效忠王國的統治者英雄 id；查不到為 null。</summary>
        string? PlayerKingdomLeaderId { get; }

        /// <summary>該英雄是否為玩家夥伴。</summary>
        bool IsPlayerCompanion(string heroId);

        /// <summary>該英雄是否為玩家家族成員。</summary>
        bool IsPlayerClanMember(string heroId);

        /// <summary>該英雄是否為玩家配偶。</summary>
        bool IsPlayerSpouse(string heroId);
    }
}
