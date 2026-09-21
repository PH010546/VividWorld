using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using VividWorld.Core.Events;

namespace VividWorld.Core.Catalog
{
    public sealed class TemplateFact
    {
        public string Id = string.Empty;
        public FactCategory Category;
        public string? TextId;                                   // 缺時記 Warning 並以 Text 降級（§9.5.6）
        public string Text = string.Empty;                       // 英文後備，不得為空
        public Dictionary<string, string> Vars = new();          // 值必須帶合法前綴
        public int Fragility = 3;
        public string? RefersToTemplateType;
        public string? Role;
        public bool Optional = false;

        [JsonExtensionData]
        public IDictionary<string, JToken> Extra { get; set; } = new Dictionary<string, JToken>();
    }
}
