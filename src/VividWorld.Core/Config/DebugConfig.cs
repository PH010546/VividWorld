using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace VividWorld.Core.Config
{
    public sealed class DebugConfig
    {
        public bool FakeProducerEnabled { get; set; } = false;
        public double FakeProducerEventsPerDay { get; set; } = 1.0;
        public bool DebugDialogueEnabled { get; set; } = false;
        public bool AllowTestEventInjection { get; set; } = false;
        public bool AnnounceRumorEvents { get; set; } = false;
        public bool VerboseTickLog { get; set; } = false;
        public bool LogTellerTurns { get; set; } = false;   // 發行前改 false（每遊戲日約 60 KB log）
        public bool AllowFalseAccusationInjection { get; set; } = false;   // §6.7.3
        public int DevReportMaxCrowdedOut { get; set; } = 8;
        public int LogFlushEveryLines { get; set; } = 64;
        public double LogFlushEverySeconds { get; set; } = 5.0;
        public bool MetricsEnabled { get; set; } = false;
        public int DevRelationBoost { get; set; } = 20;

        [JsonExtensionData]
        public IDictionary<string, JToken> Extra { get; set; } = new Dictionary<string, JToken>();
    }
}
