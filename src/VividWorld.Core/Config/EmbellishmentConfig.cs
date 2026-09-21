using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace VividWorld.Core.Config
{
    public sealed class EmbellishmentConfig    // 全部保留、第一階段不被讀取（規格 §6.2）
    {
        public bool Enabled { get; set; } = false;
        public int MinHop { get; set; } = 2;
        public double ChancePerNegativeHonor { get; set; } = 0.15;

        [JsonExtensionData]
        public IDictionary<string, JToken> Extra { get; set; } = new Dictionary<string, JToken>();
    }
}
