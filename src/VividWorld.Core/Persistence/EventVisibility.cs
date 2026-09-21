#nullable enable
using VividWorld.Core.Events;

namespace VividWorld.Core.Persistence
{
    /// <summary>「這則事件今天看得見嗎」——全專案**唯一**的判斷處（規格 §2.2.1）。
    ///
    /// 日期比今天晚的事件代表一條已經被抹掉的時間線（讀了比快照更舊的存檔）。
    /// 規則是**看不見、但絕不刪除**：日子走到那一天，同一則事件自己會回來。
    /// 因為只靠日期比較、不存任何旗標，裝上或移除模組都不會留下痕跡。
    ///
    /// 讀事件的地方一律呼叫這裡，不准各寫一份 `if (day > today)`——
    /// 各寫一份正是規格 §2.2.1 清點出「七處裡有五處漏掉」的成因。</summary>
    public static class EventVisibility
    {
        /// <summary>容差：存檔時間與事件日期本來就有不到一天的落差，
        /// 半天以內不算「未來」。`StoreTimeline.ToleranceDays` 沿用同一個值。</summary>
        public const double ToleranceDays = 0.5;

        public static bool IsVisibleOn(double eventDay, double currentDay)
        {
            return eventDay <= currentDay + ToleranceDays;
        }

        public static bool IsVisibleOn(RumorIndexEntry? entry, double currentDay)
        {
            return entry != null && IsVisibleOn(entry.Day, currentDay);
        }

        public static bool IsVisibleOn(WorldEvent? evt, double currentDay)
        {
            return evt != null && IsVisibleOn(evt.Day, currentDay);
        }
    }
}
