using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace VividWorld.Core.Grudges
{
    public sealed class SituationGrudgeDef
    {
        public string From { get; set; } = string.Empty;
        public string To { get; set; } = string.Empty;
        public double Amount { get; set; }
        public bool LedgerOnly { get; set; }
        public string? Escalate { get; set; }

        [JsonExtensionData]
        public IDictionary<string, JToken> Extra { get; set; } = new Dictionary<string, JToken>();
    }
}
