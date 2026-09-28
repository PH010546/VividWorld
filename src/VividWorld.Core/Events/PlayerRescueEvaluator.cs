using System;
using System.Globalization;
using System.Linq;

namespace VividWorld.Core.Events
{
    /// <summary>
    /// 玩家從盜匪手中救出俘虜的判定結果。
    /// </summary>
    public sealed class PlayerRescueResult
    {
        public bool IsPlayerRescue { get; }
        public string? Via { get; }
        public string? Reason { get; }

        public PlayerRescueResult(bool isPlayerRescue, string? via, string? reason)
        {
            IsPlayerRescue = isPlayerRescue;
            Via = via;
            Reason = reason;
        }
    }

    /// <summary>
    /// 判定俘虜獲釋是否屬於玩家擊散盜匪救出英雄的純函式評估器。
    /// </summary>
    public static class PlayerRescueEvaluator
    {
        /// <summary>
        /// 評估俘虜獲釋是否由玩家救出。
        /// 輸入：最近一次被俘的事件類型、獲釋時隊伍是否為玩家隊伍、隊伍是否有領隊或擁有者、獲釋原因細節。
        /// 輸出：判定結果（是否為玩家救出、若是則走哪一途徑、若否則為不符合的原因）。
        /// </summary>
        public static PlayerRescueResult Evaluate(
            string? latestCaptureType,
            bool isMainParty,
            bool hasLeaderOrOwner,
            EndCaptivityDetail detail)
        {
            if (!string.Equals(latestCaptureType, "hero_captured_by_bandits", StringComparison.Ordinal))
            {
                return new PlayerRescueResult(false, null, "latest capture is not by bandits");
            }

            if (isMainParty)
            {
                return new PlayerRescueResult(true, "main party holds prisoner", null);
            }

            if (hasLeaderOrOwner)
            {
                return new PlayerRescueResult(false, null, "party is another hero's");
            }

            if (detail == EndCaptivityDetail.ReleasedByChoice)
            {
                return new PlayerRescueResult(true, "released by choice from leaderless party", null);
            }

            return new PlayerRescueResult(false, null, string.Format(CultureInfo.InvariantCulture, "detail {0} from leaderless party", detail));
        }

        /// <summary>
        /// 從連回的被俘事件中讀取當時綁定的 BANDITS 變數（who 碎片的 Vars["BANDITS"]），讀不到則退回未知盜匪代碼。
        /// </summary>
        public static string ResolveBanditsVar(WorldEvent? captureEvent)
        {
            if (captureEvent?.Facts != null)
            {
                var whoFact = captureEvent.Facts.FirstOrDefault(f => string.Equals(f.Id, "who", StringComparison.OrdinalIgnoreCase));
                if (whoFact?.Vars != null && whoFact.Vars.TryGetValue("BANDITS", out var bandits) && !string.IsNullOrEmpty(bandits))
                {
                    return bandits;
                }
            }
            return "key:VividWorld_UnknownBandits";
        }

        /// <summary>
        /// 格式化玩家救援判定日誌字串。
        /// </summary>
        public static string FormatLog(PlayerRescueResult result, string? partyId, string detail)
        {
            string pid = string.IsNullOrEmpty(partyId) ? "none" : partyId!;
            if (result.IsPlayerRescue)
            {
                return string.Format(CultureInfo.InvariantCulture,
                    "player rescue: yes via {0} (party={1}, detail={2})",
                    result.Via, pid, detail);
            }
            return string.Format(CultureInfo.InvariantCulture,
                "player rescue: no ({0}) (party={1}, detail={2})",
                result.Reason, pid, detail);
        }
    }
}
