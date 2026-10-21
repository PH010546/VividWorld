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

    /// <summary>玩家在城鎮或城堡時另外替他擲的「情境在他面前發生」。不佔全世界每天的情境量。</summary>
    public sealed class PlayerPresenceConfig
    {
        /// <summary>整個玩家入局的開關；關掉就不擲、不跳視窗、不動玩家的數值。</summary>
        public bool Enabled { get; set; } = true;

        /// <summary>每次擲（進城當下、待在城裡每天）發生的機率，0～1。</summary>
        public double Chance { get; set; } = 0.25;

        /// <summary>發生一次之後幾天內不再擲。</summary>
        public int CooldownDays { get; set; } = 7;

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

        public PlayerPresenceConfig PlayerPresence { get; set; } = new();

        [JsonExtensionData]
        public IDictionary<string, JToken> Extra { get; set; } = new Dictionary<string, JToken>();
    }
}
