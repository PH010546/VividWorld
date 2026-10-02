using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace VividWorld.Core.Config
{
    /// <summary>說話的人講給玩家聽時，事實句後面另起一句的感想。</summary>
    public sealed class FeelingsConfig
    {
        public bool Enabled { get; set; } = true;

        /// <summary>好感度到這個值（含）以上算「高」。</summary>
        public int AffectionHigh { get; set; } = 30;

        /// <summary>好感度到這個值（含）以下算「低」。</summary>
        public int AffectionLow { get; set; } = -30;

        /// <summary>個人恩怨淨額的絕對值到這個門檻（含）才算恩或怨。</summary>
        public double GrudgeThreshold { get; set; } = 4.0;

        [JsonExtensionData]
        public IDictionary<string, JToken> Extra { get; set; } = new Dictionary<string, JToken>();
    }
}
