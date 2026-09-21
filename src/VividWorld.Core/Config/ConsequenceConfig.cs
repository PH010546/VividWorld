using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace VividWorld.Core.Config
{
    public sealed class MisconceptionTraitWeights
    {
        public double HonorMisled       { get; set; } =  0.15;
        public double HonorBlamed       { get; set; } =  0.15;
        public double GenerosityBlamed  { get; set; } =  0.10;
        public double CalculatingMisled { get; set; } = -0.20;

        [JsonExtensionData]
        public IDictionary<string, JToken> Extra { get; set; } = new Dictionary<string, JToken>();
    }

    public sealed class MisconceptionConfig
    {
        public double BaseClearUpChancePerMeeting { get; set; } = 0.15;
        public MisconceptionTraitWeights TraitWeights { get; set; } = new();
        public double MultiplierMin { get; set; } = 0.05;
        public double MultiplierMax { get; set; } = 3.0;

        [JsonExtensionData]
        public IDictionary<string, JToken> Extra { get; set; } = new Dictionary<string, JToken>();
    }

    public sealed class ConsequenceConfig
    {
        public bool Enabled { get; set; } = true;
        public double[] HopConfidence { get; set; } = { 1.0, 1.0, 0.75, 0.5, 0.3, 0.2 };
        public double BystanderMultiplier { get; set; } = 0.35;
        public int MinAbsoluteDelta { get; set; } = 1;
        public double MaxAbsoluteDeltaPerHeroPerDay { get; set; } = 6.0;
        public bool LedgerOnly { get; set; } = false;
        public MisconceptionConfig Misconception { get; set; } = new();

        [JsonExtensionData]
        public IDictionary<string, JToken> Extra { get; set; } = new Dictionary<string, JToken>();
    }
}
