using System.Collections.Generic;

namespace VividWorld.Core.Channels
{
    public interface IPropagationChannel
    {
        /// <summary>此人當下可能交談的對象，至多 maxResults 個。
        /// 必須是局部查詢（proposal §5）：不掃全世界、不做圖搜尋。</summary>
        IReadOnlyList<ChannelLink> ContactsOf(string heroId, int maxResults);

        /// <summary>與此人同地點者——僅用於匯入時種下公開事件的 hop 0 目擊者。</summary>
        IReadOnlyList<string> WitnessesAt(string heroId, int maxResults);
    }
}
