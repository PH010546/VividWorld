using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace VividWorld.Core.Config
{
    /// <summary>說話的人講給玩家聽時，事實句後面另起一句的感想。</summary>
    public sealed class FeelingsConfig
    {
        public bool Enabled { get; set; } = true;

        /// <summary>好感度大於這個值算「高」。</summary>
        public int AffectionHigh { get; set; } = 50;

        /// <summary>好感度小於這個值算「低」。</summary>
        public int AffectionLow { get; set; } = -50;

        /// <summary>個人恩怨淨額的絕對值到這個門檻（含）才算恩或怨。</summary>
        public double GrudgeThreshold { get; set; } = 4.0;

        [JsonExtensionData]
        public IDictionary<string, JToken> Extra { get; set; } = new Dictionary<string, JToken>();
    }
}
