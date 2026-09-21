using System.Collections.Generic;
using VividWorld.Core.Events;

namespace VividWorld.Core.Rumors
{
    public interface IFactEmbellishmentPolicy
    {
        /// <summary>第一階段一律回傳 retained 原樣。
        /// 契約：實作若要修改碎片，必須先 Fact.Clone()——retained 裡是事件的原始實例。
        /// 且注入的內容存在聽者的 KnownByEntry.AddedDetail，絕不寫進事件的 Facts
        /// （規格 §6.2：客觀紀錄必須乾淨，這是事件／傳聞分離的全部意義）。</summary>
        IReadOnlyList<Fact> Embellish(WorldEvent evt, IReadOnlyList<Fact> retained, int hop, TraitProfile teller);
    }
}
