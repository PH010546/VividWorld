using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace VividWorld.Core.Config
{
    public sealed class AskTraitWeights
    {
        public double Generosity  { get; set; } =  6.0;
        public double Honor       { get; set; } =  4.0;
        public double Calculating { get; set; } = -8.0;

        [JsonExtensionData]
        public IDictionary<string, JToken> Extra { get; set; } = new Dictionary<string, JToken>();
    }

    public sealed class DialogueConfig
    {
        public int NpcVolunteerRelationGate { get; set; } = 30;
        public bool NpcVolunteerAlwaysForCloseKin { get; set; } = true;
        public int MaxVolunteersPerDay { get; set; } = 1;
        public double VolunteerCooldownDays { get; set; } = 3.0;
        public int AskRelationGate { get; set; } = 0;
        public double AskWillingnessThreshold { get; set; } = 5.0;
        public AskTraitWeights AskTraitWeights { get; set; } = new();
        public bool AllowRicherRetell { get; set; } = true;
        public double ScoreRetellMultiplier { get; set; } = 0.5;          // §9.1.1 (4)
        public double ScoreDrama { get; set; } = 2.0;
        public double ScoreFreshness { get; set; } = 3.0;
        public double ScoreDetail { get; set; } = 1.0;
        public double ScoreRelevance { get; set; } = 4.0;
        public int ScoreRelevanceRelationGate { get; set; } = 20;
        public string NpcLineInputToken { get; set; } = "lord_start";          // §9.2【v3.14，帳本 D-46】
        public int NpcLinePriority { get; set; } = 105;                        // §9.2【v3.14，帳本 D-46】原版用這一個
        public string PlayerLineInputToken { get; set; } = "hero_main_options";
        public int PlayerLinePriority { get; set; } = 105;


        // ── 「平民不該跟貴族攀談」相容模式（§9.2.1【v3.15】，帳本 D-43／X-17／L-22）──
        // NaN（Not-a-Noble）在 lord_start 壓了一條 150，條件在玩家氏族 Tier ≤ 1 時恆為真（X-17），
        // 而引擎挑到第一條通過的就 return（D-43）⇒ 我們那條 105 永遠不會被評估（L-22）。
        // 兩邊都要講得通：相容模式開起時提高優先權（才講得出口），同時加一道氏族 Tier 閘
        // （NaN 自己封鎖外交與任務的那一層是 Tier == 0，我們跟它同層）。原版沒裝 NaN 就一切照舊。
        /// <summary>"auto"（偵測到 CommonerCompatModules 列的任一組件就開）、"on"、"off"。</summary>
        public string CommonerCompatMode { get; set; } = "auto";

        /// <summary>會觸發相容模式的組件名（不含 .dll）。</summary>
        public List<string> CommonerCompatModules { get; set; } = new List<string> { "NaN", "Lowborn" };

        /// <summary>相容模式下改用的 NPC 主動行優先權（必須高於 NaN 的 150）。</summary>
        public int CommonerCompatLinePriority { get; set; } = 155;

        /// <summary>相容模式下，玩家氏族 Tier 低於此值時，「詢問傳聞」的選項不出現。
        /// 預設 1＝Tier 0 問不到，與 NaN 拒絕談外交／發任務同一層。</summary>
        public int CommonerCompatAskMinClanTier { get; set; } = 1;

        /// <summary>相容模式下，玩家氏族 Tier 低於此值時，領主不主動講。
        /// 預設 0＝不擋：領主開口不是玩家攀談，而且那條路徑本來就要求好感 ≥ 30。</summary>
        public int CommonerCompatVolunteerMinClanTier { get; set; } = 0;

        [JsonExtensionData]
        public IDictionary<string, JToken> Extra { get; set; } = new Dictionary<string, JToken>();
    }
}
