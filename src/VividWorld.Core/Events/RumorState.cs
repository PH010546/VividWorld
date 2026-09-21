using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace VividWorld.Core.Events
{
    public sealed class RumorState
    {
        public bool Leaked { get; set; }
        public double LeakedDay { get; set; } = -1;
        public string? LeakerHeroId { get; set; }
        public double LastPropagatedDay { get; set; } = -1;
        public double LastNewKnowerDay { get; set; } = -1;
        public bool Dormant { get; set; }

        /// <summary>規格 §5.1：任何會被寫進分片的型別都必須帶未知欄位保存。</summary>
        [JsonExtensionData]
        public IDictionary<string, JToken> Extra { get; set; } = new Dictionary<string, JToken>();
    }
}
