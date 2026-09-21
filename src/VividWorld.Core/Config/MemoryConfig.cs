using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace VividWorld.Core.Config
{
    public sealed class MemoryConfig
    {
        public bool Enabled { get; set; } = true;
        public double BaseDays { get; set; } = 60.0;
        public int DramaReference { get; set; } = 4;
        public double MinDays { get; set; } = 1.0;
        public int RelationFullAt { get; set; } = 100;
        public InterestFloorsConfig InterestFloors { get; set; } = new();
        public double TellFactorMin { get; set; } = 0.3;
        public double UpgradeMinInterest { get; set; } = 0.6;
        public double ReinforceGrowth { get; set; } = 1.5;
        public double ReinforceMaxMultiplier { get; set; } = 3.0;

        [JsonExtensionData]
        public IDictionary<string, JToken> Extra { get; set; } = new Dictionary<string, JToken>();
    }

    public sealed class InterestFloorsConfig
    {
        public double Participant { get; set; } = 1.0;
        public double Kin { get; set; } = 0.6;
        public double SameClan { get; set; } = 0.4;
        public double Other { get; set; } = 0.05;
        public double[] OtherByDrama { get; set; } = { 0.05, 0.05, 0.1, 0.2, 0.4 };

        [JsonExtensionData]
        public IDictionary<string, JToken> Extra { get; set; } = new Dictionary<string, JToken>();
    }
}
