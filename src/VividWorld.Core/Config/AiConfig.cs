using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace VividWorld.Core.Config
{
    public sealed class AiConfig
    {
        public bool Enabled { get; set; } = true;
        public string PushLanguage { get; set; } = "english";
        public int PersistentMaxNewPerChat { get; set; } = 2;
        public int ChatOnlyMaxPerChat { get; set; } = 8;

        [JsonExtensionData]
        public IDictionary<string, JToken> Extra { get; set; } = new Dictionary<string, JToken>();
    }
}
