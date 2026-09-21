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
        public double Score;
    }
}
