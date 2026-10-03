using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace VividWorld.Core.Config
{
    public sealed class RelationConfig
    {
        /// <summary>0 = 執行期讀 DiplomacyModel.MaxRelationLimit（D-29）。> 0 時覆寫為指定值。</summary>
        public double ScaleOverride { get; set; } = 0.0;

        /// <summary>執行期生效的關係尺度。執行期由 Module 填入，不序列化回 config.json。</summary>
        [JsonIgnore]
        public double EffectiveScale { get; set; } = 100.0;

        public double MeetPositiveWeight { get; set; } = 0.35;
        public double MeetNegativeWeight { get; set; } = 1.30;
        public double MeetFactorMin { get; set; } = 0.10;
        public double MeetFactorMax { get; set; } = 1.40;

        public double RemoteBaseFactor { get; set; } = 0.10;
        public double RemoteRelationWeight { get; set; } = 2.00;
        public double RemoteFactorMax { get; set; } = 2.00;

        /// <summary>
        /// true = 跟玩家有關的好感各人各算（不換成族長）；false = 回到原版規則。
        /// 補丁每次呼叫都現讀這個值，設定選單切換立即生效。
        /// </summary>
        public bool PersonalWithPlayer { get; set; } = true;

        [JsonExtensionData]
        public IDictionary<string, JToken> Extra { get; set; } = new Dictionary<string, JToken>();
    }
}
