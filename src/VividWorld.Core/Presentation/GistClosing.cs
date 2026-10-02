using System;
using System.Collections.Generic;
using VividWorld.Core.Util;

namespace VividWorld.Core.Presentation
{
    /// <summary>
    /// 只講大概、而且確實少講了他知道的事時，事實句後面接的一句「收尾」：
    /// 表示話沒說完，讓玩家聽得出交情好了會聽到更多。它不評論這件事或裡面的人，所以不算感想。
    /// 依說話的人的位置分三組（當事人、親眼看到、聽來的），每組幾句；同一個人對同一件事每次挑同一句。
    /// </summary>
    public static class GistClosing
    {
        public const string Self = "Self";
        public const string Witness = "Witness";
        public const string Heard = "Heard";

        public const int VariantsPerGroup = 3;

        public static readonly IReadOnlyList<string> Groups = new[] { Self, Witness, Heard };

        public static string KeyFor(string group, int variant) => $"VividWorld_Closing_{group}_{variant}";

        public static IEnumerable<string> AllKeys()
        {
            foreach (var g in Groups)
            {
                for (int i = 1; i <= VariantsPerGroup; i++) yield return KeyFor(g, i);
            }
        }

        /// <summary>說話的人是當事人 ⇒ 當事人組；開頭語是親眼看到（含「我當時就在場」的重述）⇒ 親眼看到組；其餘都是聽來的。</summary>
        public static string GroupFor(bool speakerIsParticipant, RumorPrefix? prefix)
        {
            if (speakerIsParticipant) return Self;
            if (prefix != null && (prefix.Kind == RumorPrefixKind.Eyewitness || prefix.Kind == RumorPrefixKind.Retell)) return Witness;
            return Heard;
        }

        /// <summary>同一個人、同一件事挑同一句：用說話的人與事件 id 算穩定雜湊，不用執行期會變的 GetHashCode。</summary>
        public static string Select(string group, string? speakerHeroId, string? eventId)
        {
            long h = RumorSeed.Of(0, "closing", speakerHeroId ?? string.Empty, eventId ?? string.Empty);
            int variant = (int)((ulong)h % VariantsPerGroup) + 1;
            return KeyFor(group, variant);
        }
    }
}
