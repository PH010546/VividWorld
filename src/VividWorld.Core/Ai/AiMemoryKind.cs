using System;
using VividWorld.Core.Events;

namespace VividWorld.Core.Ai
{
    /// <summary>
    /// 交給 AI 對話模組的每一筆記憶屬於哪一種，照這位 NPC 跟那則事件的關係決定。
    /// Calradia Remembers 把 <see cref="Other"/> 當成「跟玩家的往事」，傳聞標成它會擠掉 NPC 跟玩家真正的往事；
    /// 它不認得的種類（舊版沒有 Hearsay、OwnLife）一律當 Other，所以裝舊版的玩家行為不變（帳本 X-43、X-44）。
    /// </summary>
    public static class AiMemoryKind
    {
        public const string Hearsay = "Hearsay";
        public const string OwnLife = "OwnLife";
        public const string Other = "Other";

        /// <summary>
        /// 依序判斷：不是當事人且為聽來的（hop > 0）⇒ 聽說的；其餘（當事人，或第 0 手在場看到的）：玩家也是當事人 ⇒ 跟玩家的往事，否則 ⇒ 他自己的事。
        /// 不看事件是不是編出來的：交給 AI 的文字與種類都不能透露真假；而編的話的當事人（沒做過那件事的人）
        /// 本來就不會把它當自己的事交出去，挑記憶時已經排除了。
        /// </summary>
        public static string For(WorldEvent evt, string npcHeroId, string? playerHeroId, int hop)
        {
            if (evt == null) throw new ArgumentNullException(nameof(evt));

            bool npcIsParticipant = !string.IsNullOrEmpty(npcHeroId) && evt.RoleOf(npcHeroId) != null;
            if (!npcIsParticipant && hop > 0)
            {
                return Hearsay;
            }

            bool playerIsParticipant = !string.IsNullOrEmpty(playerHeroId) && evt.RoleOf(playerHeroId!) != null;
            return playerIsParticipant ? Other : OwnLife;
        }
    }
}
