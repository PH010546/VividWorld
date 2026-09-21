using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace VividWorld.Core.Config
{
    public sealed class LeakTraitWeights
    {
        public double Honor       { get; set; } = -0.15;
        public double Calculating { get; set; } = -0.20;
        public double Generosity  { get; set; } =  0.15;

        [JsonExtensionData]
        public IDictionary<string, JToken> Extra { get; set; } = new Dictionary<string, JToken>();
    }

    public sealed class LeakConfig
    {
        public double BaseChancePerInsiderPerDay { get; set; } = 0.012;   // 見規格 §6.4 的保密率換算表
        public double GraceDays { get; set; } = 1.0;
        public double ChanceDecayHalfLifeDays { get; set; } = 60.0;
        public LeakTraitWeights TraitWeights { get; set; } = new();
        public double MultiplierMin { get; set; } = 0.05;
        public double MultiplierMax { get; set; } = 5.0;

        [JsonExtensionData]
        public IDictionary<string, JToken> Extra { get; set; } = new Dictionary<string, JToken>();
    }
}
