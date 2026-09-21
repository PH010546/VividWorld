#nullable enable
using System;
using System.Collections.Generic;

namespace VividWorld.Core.Dialogue
{
    /// <summary>
    /// 補救選項的判定閘與結算（規格 §9.2.3，M6c）。
    /// 當 NPC 在 start 上的招呼狀態（lord_start）被第三方模組繞開時，
    /// 若該 NPC 本來有一則傳聞要講，在主選單提供一條玩家選項把話接回來。
    /// </summary>
    public static class VolunteerRecoveryGate
    {
        /// <summary>
        /// 判定被第三方繞開時的補救選項是否可用（規格 §9.2.3，M6c）。
        /// 四道閘全部成立才顯示：
        /// 1. 有提案（hasOffer）
        /// 2. 這場對話沒有走到 lord_start（!reachedGreetingToken）
        /// 3. 確實被繞開（wasBypassed）
        /// 4. 這場對話還沒送出過（!alreadyDeliveredThisConversation）
        /// </summary>
        public static bool IsAvailable(
            bool hasOffer,
            bool reachedGreetingToken,
            bool wasBypassed,
            bool alreadyDeliveredThisConversation)
        {
            if (!hasOffer) return false;
            if (reachedGreetingToken) return false;
            if (!wasBypassed) return false;
            if (alreadyDeliveredThisConversation) return false;
            return true;
        }

        /// <summary>
        /// 執行補救送出後的配額與冷卻消耗結算（規格 §9.2.3，與主動講完全相同）。
        /// 消耗每日計數器，並更新講述者最後講述天數。
        /// </summary>
        public static void ConsumeQuotaAndCooldown(
            DailyCounter? dailyCounter,
            IDictionary<string, double>? lastVolunteeredDays,
            string tellerHeroId,
            double day)
        {
            if (dailyCounter != null)
            {
                dailyCounter.Advance(day);
                dailyCounter.Increment();
            }

            if (lastVolunteeredDays != null && !string.IsNullOrEmpty(tellerHeroId))
            {
                lastVolunteeredDays[tellerHeroId] = day;
            }
        }
    }
}
