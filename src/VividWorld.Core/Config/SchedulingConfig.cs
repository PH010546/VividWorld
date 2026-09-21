using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace VividWorld.Core.Config
{
    public sealed class SchedulingConfig
    {
        public int EventsPerHourlyTick { get; set; } = 4;
        public int TellersPerHourlyTick { get; set; } = 8;

        // 敘事型時長一律以原生曆法（一年 84 天）為基準撰寫，載入時依實際曆法縮放（規格 §11.1）。
        // 調整前先照 §11.2 換算成遊戲年——天數不可判讀，遊戲年才可判讀。
        public double RumorLifetimeDays { get; set; } = 120.0;    // 1.43 遊戲年
        public double StaleDays { get; set; } = 30.0;             // 0.36 遊戲年

        /// <summary>未洩漏的秘密留在洩漏清單的上限（規格 §7.3）。
        /// 252 = 整整 3 個遊戲年（84×3），4.2 個半衰期，剩餘洩漏期望值約 5.4%。</summary>
        public double SecretWatchDays { get; set; } = 252.0;      // 3.00 遊戲年

        /// <summary>依 CampaignTime.DaysInYear / 84 縮放敘事型時長（規格 §11.1）。
        /// 曆法長度於執行期計算，改曆法的 mod 會讓寫死的天數靜默壓縮整套時間感。
        /// 關掉時所有值照字面使用。</summary>
        public bool ScaleDurationsToGameCalendar { get; set; } = true;

        public int FlushIntervalHours { get; set; } = 6;           // 不縮放：磁碟記帳節奏

        [JsonExtensionData]
        public IDictionary<string, JToken> Extra { get; set; } = new Dictionary<string, JToken>();
    }
}
