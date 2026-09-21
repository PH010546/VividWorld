using VividWorld.Core.Events;

namespace VividWorld.Core.Dialogue
{
    /// <summary>一則「這個 NPC 可以講給玩家聽」的候選。由 Module 從 KnownByIndex 組出來。</summary>
    public sealed class RumorCandidate
    {
        public WorldEvent Event = null!;
        public int TellerHop;                       // 講述者的手數
        public int? PlayerExistingHop;              // 玩家已知則為其手數，未知為 null
        public bool InvolvesHeroPlayerCaresAbout;   // 事件參與者中有玩家好感度 >= scoreRelevanceRelationGate 者
    }
}
