#nullable enable
using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using VividWorld.Core.Events;

namespace VividWorld.Core.Secrets
{
    public static class SecretVisibilityPolicy
    {
        /// <summary>
        /// 判斷藏著的那一塊是否在玩家看得到的收口之內：
        /// 若事件並非秘密（公開事件），退回一般可見性；
        /// 若為秘密，必須 IsVisibleToRumorSystem 為真（已公開或走漏），
        /// 或（玩家在 KnownBy 內且 SecretRetired 為假）。
        /// 僅限用於玩家見聞與講給玩家聽的句子，傳播、AI 交接、紀事以外的地方一律不准用它。
        /// </summary>
        public static bool IsHiddenPartVisibleToPlayer(WorldEvent? evt, string? playerHeroId)
        {
            if (evt == null) return false;
            if (evt.Origin != EventOrigin.Secret)
            {
                return evt.IsVisibleToRumorSystem;
            }

            if (evt.IsVisibleToRumorSystem) return true;

            if (string.IsNullOrEmpty(playerHeroId)) return false;
            if (evt.SecretRetired) return false;

            return evt.IsKnownBy(playerHeroId!);
        }
    }

    public sealed class PlayerConfideQueueItem
    {
        [JsonProperty("eventId")]
        public string EventId { get; set; } = string.Empty;

        [JsonProperty("tellerHeroId")]
        public string TellerHeroId { get; set; } = string.Empty;

        [JsonProperty("queuedDay")]
        public double QueuedDay { get; set; }

        [JsonIgnore]
        public double Day
        {
            get => QueuedDay;
            set => QueuedDay = value;
        }

        [JsonExtensionData]
        public IDictionary<string, JToken> Extra { get; set; } = new Dictionary<string, JToken>();
    }

    public sealed class PlayerConfideQueueData
    {
        [JsonProperty("version")]
        public int Version { get; set; } = 1;

        [JsonProperty("items")]
        public List<PlayerConfideQueueItem> Items { get; set; } = new();

        [JsonExtensionData]
        public IDictionary<string, JToken> Extra { get; set; } = new Dictionary<string, JToken>();
    }
}
