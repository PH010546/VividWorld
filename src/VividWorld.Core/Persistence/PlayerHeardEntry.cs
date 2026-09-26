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
        public int PlayerHop { get; set; }
        public string? SourceHeroId { get; set; }
        public double LearnedDay { get; set; }                  // 第一次聽到
        public double UpdatedDay { get; set; }                  // 最近一次寫這一筆
        public List<Fact> Facts { get; set; } = new();          // 玩家知道的碎片，Fact.Clone() 的複本

        [JsonExtensionData]
        public IDictionary<string, JToken> Extra { get; set; } = new Dictionary<string, JToken>();
    }
}
