using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace VividWorld.Core.Config
{
    public sealed class RetentionConfig
    {
        public string Policy { get; set; } = "Threshold";                    // "Threshold" | "Probabilistic"
        // 索引 = hop；超出夾在末值。hop 0（當事人／目擊者）永遠是 5 ＝ 什麼都沒忘。
        // 【2026-09-09 帳本 L-17】舊值 {5,5,4,3,2,1} 讓五個樣板的 hop 0／1／2 碎片集合完全相同
        // ——失真是本模組的賣點，卻在前三手完全沒發生，淺 hop 之間的重述升級也不可能觸發。
        public int[] HopThresholds { get; set; } = { 5, 3, 2, 2, 1, 1 };
        public double Softness { get; set; } = 0.35;                         // 僅機率版；→0 精確重現門檻版
        public int AlwaysKeepAtOrBelowFragility { get; set; } = 1;
        public int MinFactsRetained { get; set; } = 1;
        public int[] DramaThresholdShift { get; set; } = { 0, 0, 0, 0, 0 };

        [JsonExtensionData]
        public IDictionary<string, JToken> Extra { get; set; } = new Dictionary<string, JToken>();
    }
}
