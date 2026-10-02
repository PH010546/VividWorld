#nullable enable
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using VividWorld.Core.Events;

namespace VividWorld.Core.Persistence
{
    /// <summary>
    /// 玩家聽過的消息項目（卡 MF3a §1.2）。
    /// </summary>
    public sealed class PlayerHeardEntry
    {
        public string EventId { get; set; } = string.Empty;
        public string Type { get; set; } = string.Empty;
        public double Day { get; set; }                         // 事件日期
        public string? LinkedEventId { get; set; }
        public Dictionary<string, string> Participants { get; set; } = new();   // role -> heroId，M10 要用
        public int DramaWeight { get; set; } = 3;

        /// <summary><see cref="DramaWeight"/> 的尺度（5＝舊的 1..5、10＝1..10 的份量），跟事件上的欄位同一個意思；缺欄位的舊資料是 5。</summary>
        public int DramaScale { get; set; } = VividWorld.Core.Events.DramaScales.Legacy;
        public int PlayerHop { get; set; }
        public string? SourceHeroId { get; set; }
        public double LearnedDay { get; set; }                  // 第一次聽到
        public double UpdatedDay { get; set; }                  // 最近一次寫這一筆
        public List<Fact> Facts { get; set; } = new();          // 玩家知道的碎片，Fact.Clone() 的複本

        /// <summary>每個告訴過玩家這件事的人一份，依第一次講的先後排列。
        /// 舊檔沒有這個欄位：讀進來時用既有欄位合成一份（見 <see cref="PlayerHeardSource.FromLegacy"/>）。</summary>
        public List<PlayerHeardSource> Sources { get; set; } = new();

        /// <summary>還沒合成來源清單的舊紀錄回傳合成的一份（不改紀錄本身）；已有清單就回傳清單。</summary>
        public IReadOnlyList<PlayerHeardSource> EffectiveSources()
        {
            if (Sources != null && Sources.Count > 0) return Sources;
            return new[] { PlayerHeardSource.FromLegacy(this) };
        }

        [JsonExtensionData]
        public IDictionary<string, JToken> Extra { get; set; } = new Dictionary<string, JToken>();
    }
}
