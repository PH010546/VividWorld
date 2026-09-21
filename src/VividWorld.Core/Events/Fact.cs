using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace VividWorld.Core.Events
{
    public sealed class Fact
    {
        public string Id { get; set; } = string.Empty;      // 事件內唯一
        public FactCategory Category { get; set; }

        /// <summary>在地化鍵，例 "VividWorld_Fact_CovertSabotage_Who"。空字串時退回 Text 字面。規格 §9.5。</summary>
        public string TextId { get; set; } = string.Empty;

        /// <summary>英文後備文字，含 {VAR} 佔位符。字串表缺鍵時玩家看到的就是這一句。</summary>
        public string Text { get; set; } = string.Empty;

        /// <summary>佔位符 → 引用（hero:／settlement:／faction:／key:／num:／text:）。規格 §9.5.2。</summary>
        public Dictionary<string, string>? Vars { get; set; }

        public int Fragility { get; set; } = 3;             // 1..5，1 最黏
        public string? RefersTo { get; set; }
        public string? Role { get; set; }                   // WHO 專用：mastermind/target/agent
        public bool IsFabricated { get; set; }              // 保留給加油添醋；第一階段恆 false

        [JsonExtensionData]
        public IDictionary<string, JToken> Extra { get; set; } = new Dictionary<string, JToken>();

        /// <summary>深拷貝。Vars 與 Extra 都必須是新的字典，不得共用參考。</summary>
        public Fact Clone()
        {
            var clone = new Fact
            {
                Id = Id,
                Category = Category,
                TextId = TextId,
                Text = Text,
                Fragility = Fragility,
                RefersTo = RefersTo,
                Role = Role,
                IsFabricated = IsFabricated,
            };

            if (Vars != null)
            {
                clone.Vars = new Dictionary<string, string>(Vars);
            }

            if (Extra != null)
            {
                clone.Extra = Extra.ToDictionary(kvp => kvp.Key, kvp => kvp.Value.DeepClone());
            }

            return clone;
        }
    }
}
