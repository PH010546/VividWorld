using VividWorld.Core.Presentation;

namespace VividWorld.Core.Dialogue
{
    public sealed class RumorOffer
    {
        public string EventId = string.Empty;
        public int TellerHop;
        /// <summary>挑碎片用的詳細程度（落點手數）：完整版＝TellerHop + 1，只講大概時再往後推。
        /// 玩家那筆紀錄的手數不是這個，是 <see cref="PlayerHop"/>。</summary>
        public int ResultingPlayerHop;

        /// <summary>這次講述真正轉了幾手：講者手數 + 1。玩家那筆紀錄與紀事裡的每一份來源都記這個。</summary>
        public int PlayerHop => TellerHop + 1;

        /// <summary>這次講述實際講出來的碎片代號；手工建的提案沒填時為 null（記錄時會重算同一批）。</summary>
        public System.Collections.Generic.IReadOnlyList<string>? ToldFactIds;
        public ComposedRumor Composed = new();      // 已套用保留策略與排序的「結構」，尚未渲染成字串（§9.3）
        public bool IsRetell;                       // 玩家已知、這次是更詳細的重述
        public bool IsCorrection;                   // 是否為更正
        public string? SourceHeroId;                // 講述者從誰那裡聽來的
        public RumorPrefix? Prefix;                 // 挑選的開頭語
        public double Score;
        public string? SpeakerRole;                 // 講述者角色（當事人判定，例 rival、claimant）
        public bool IsGist;                         // 交情不夠、只講大概
        public bool HeldBack;                       // 只講大概而且確實少講了他知道的事（會接收尾）
    }
}
