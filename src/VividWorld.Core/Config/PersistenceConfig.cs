using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace VividWorld.Core.Config
{
    public sealed class PersistenceConfig
    {
        public int ShardDays { get; set; } = 100;
        // `revertWithSaves` 在 M1 從 ImmersiveAI 照抄過來，但沒有把理由一起抄過來（v3.28 拿掉）。
        // 那邊存的是 NPC 對「玩家」的記憶，關掉＝讀檔抹不掉你的爛態度，是說得出口的玩法偏好；
        // 這邊存的是 NPC 之間的事件，關掉只會讓世界談論這條時間線上沒發生的事。
        // 舊 config.json 留著那個鍵不會出錯：未知鍵由 Extra 原樣保存。
        /// <summary>保留幾張快照（一個存檔名字一張）。<b>0 = 不限制</b>，這是預設值：
        /// 份數由玩家用遊戲裡的快照管理工具決定，不由上限自動剪掉。
        /// 大於 0 時照舊由舊到新剪到剩這麼多張。</summary>
        public int MaxSnapshots { get; set; } = 0;
        public int MaxPendingIngestChars { get; set; } = 24000;   // 安全低於文件記載的 31,743
        public int MaxFactsPerEvent { get; set; } = 24;

        /// <summary>
        /// SNAP2：快照是否優先使用硬連結（同磁碟區不佔額外空間）。預設 true。
        /// 關閉時整段跳過、一律實體複製。
        /// </summary>
        public bool HardlinkSnapshots { get; set; } = true;

        /// <summary>
        /// MF3b：存檔時刪除沒有 NPC 記得的舊事件。預設 true。
        /// </summary>
        public bool PurgeForgottenEvents { get; set; } = true;

        /// <summary>
        /// MF3c：幾次沖寫沒被碰過的分片就從記憶體放掉。0 ＝ 永不放。預設 8。
        /// </summary>
        public int ShardCacheIdleFlushes { get; set; } = 8;

        [JsonExtensionData]
        public IDictionary<string, JToken> Extra { get; set; } = new Dictionary<string, JToken>();
    }
}
