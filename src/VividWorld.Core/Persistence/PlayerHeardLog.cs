#nullable enable
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace VividWorld.Core.Persistence
{
    /// <summary>
    /// 玩家聽過的消息總紀錄（卡 MF3a §1.2）。
    /// </summary>
    public sealed class PlayerHeardLog
    {
        public List<PlayerHeardEntry> Entries { get; set; } = new();

        [JsonExtensionData]
        public IDictionary<string, JToken> Extra { get; set; } = new Dictionary<string, JToken>();
    }
}
