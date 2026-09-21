using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace VividWorld.Core.Config
{
    public sealed class GrudgeBandConfig
    {
        public double Band { get; set; } = 10.0;
        public double DaysPerPoint { get; set; } = 3.0;

        [JsonExtensionData]
        public IDictionary<string, JToken> Extra { get; set; } = new Dictionary<string, JToken>();
    }

    public sealed class GrudgeTraitWeightsConfig
    {
        public double Mercy { get; set; } = 0.25;
        public double Calculating { get; set; } = -0.15;
        public double HonorForGratitude { get; set; } = -0.20;

        [JsonExtensionData]
        public IDictionary<string, JToken> Extra { get; set; } = new Dictionary<string, JToken>();
    }

    public sealed class GrudgeDecayConfig
    {
        public GrudgeBandConfig Personal { get; set; } = new() { Band = 10.0, DaysPerPoint = 3.0 };
        public GrudgeBandConfig Clan { get; set; } = new() { Band = 20.0, DaysPerPoint = 6.0 };
        public GrudgeTraitWeightsConfig TraitWeights { get; set; } = new();
        public double MultiplierMin { get; set; } = 0.5;
        public double MultiplierMax { get; set; } = 2.0;

        [JsonExtensionData]
        public IDictionary<string, JToken> Extra { get; set; } = new Dictionary<string, JToken>();
    }
}
