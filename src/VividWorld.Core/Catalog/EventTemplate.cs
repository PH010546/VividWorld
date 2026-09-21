using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using VividWorld.Core.Events;

namespace VividWorld.Core.Catalog
{
    public sealed class EventTemplate
    {
        public string Type = string.Empty;
        public EventOrigin? Origin;                              // null 一律拒收（§10.1：Secret 是 0 值）
        public int? DramaWeight;                                 // null → 交給 §10.1 步驟 3 依設定解析
        public string? LinkedTemplateType;                       // 引用「同一份目錄裡的另一個型別」，不是 eventId
        public Dictionary<string, string> Roles = new();         // 角色名 → 佔位符（例：challenger → "{MASTERMIND}"）
        public HashSet<string> KnowingRoles = new();
        public List<TemplateFact> Facts = new();

        [JsonProperty("headline")]
        public string? Headline;

        [JsonProperty("opinion")]
        public List<OpinionDef>? Opinions;

        [JsonExtensionData]
        public IDictionary<string, JToken> Extra { get; set; } = new Dictionary<string, JToken>();
    }

    public sealed class OpinionDef
    {
        public string About = string.Empty;   // 這個模板自己的角色名，例 "favored"
        public double Amount;                 // 正負皆可。這是「第一手聽到、而且是當事人」的滿額

        [JsonExtensionData]
        public IDictionary<string, JToken> Extra { get; set; } = new Dictionary<string, JToken>();
    }
}
