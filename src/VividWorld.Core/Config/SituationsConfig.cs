using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace VividWorld.Core.Config
{
    public sealed class SituationContestConfig
    {
        // SE4 使用
        public int MaxWitnessRollers { get; set; } = 3;

        [JsonExtensionData]
        public IDictionary<string, JToken> Extra { get; set; } = new Dictionary<string, JToken>();
    }

    public sealed class SituationScanConfig
    {
        public int MaxPairsPerSettlement { get; set; } = 24;
        public int MaxCandidates { get; set; } = 200;

        [JsonExtensionData]
        public IDictionary<string, JToken> Extra { get; set; } = new Dictionary<string, JToken>();
    }

    public sealed class SituationsConfig
    {
        public string CatalogFile { get; set; } = "vividworld_situations.json";
        public string EventCatalogFile { get; set; } = "vividworld_situation_events.json";

        public bool DailyScanEnabled { get; set; } = true;

        public double MaxPerDay { get; set; } = 1.5;

        public SituationScanConfig Scan { get; set; } = new();

        public double MinBranchWeight { get; set; } = 0.1;

        public bool GrudgesEnabled { get; set; } = true;

        public int ClanEscalationThreshold { get; set; } = 30;

        public double ClanEscalationFactor { get; set; } = 0.5;

        public GrudgeDecayConfig GrudgeDecay { get; set; } = new();

        // SE4 使用
        public int MaxPending { get; set; } = 200;

        // SE4 使用
        public int MaxPendingChars { get; set; } = 20000;

        public SituationContestConfig Contest { get; set; } = new();

        [JsonExtensionData]
        public IDictionary<string, JToken> Extra { get; set; } = new Dictionary<string, JToken>();
    }
}
