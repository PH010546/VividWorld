using VividWorld.Core.Presentation;

namespace VividWorld.Core.Dialogue
{
    public sealed class RumorOffer
    {
        public string EventId = string.Empty;
        public int TellerHop;
        public int ResultingPlayerHop;              // TellerHop + 1
        public ComposedRumor Composed = new();      // 已套用保留策略與排序的「結構」，尚未渲染成字串（§9.3）
        public bool IsRetell;                       // 玩家已知、這次是更詳細的重述
        public bool IsCorrection;                   // 是否為更正
        public string? SourceHeroId;                // 講述者從誰那裡聽來的
        public RumorPrefix? Prefix;                 // 挑選的開頭語
        public double Score;
        public string? SpeakerRole;                 // 講述者角色（當事人判定，例 rival、claimant）
    }
}
