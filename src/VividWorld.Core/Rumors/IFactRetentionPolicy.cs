using System.Collections.Generic;
using VividWorld.Core.Events;

namespace VividWorld.Core.Rumors
{
    public interface IFactRetentionPolicy
    {
        /// <summary>這位講述者在第 hop 手轉述時，還拿得出哪些碎片。
        /// 契約：純查詢。不得修改 evt、不得修改任何 Fact、不得有可觀察的副作用。
        /// 回傳的是 evt.Facts 裡的原始實例（不複製），呼叫端不得修改它們。</summary>
        IReadOnlyList<Fact> Retain(WorldEvent evt, int hop, string tellerHeroId);
    }
}
