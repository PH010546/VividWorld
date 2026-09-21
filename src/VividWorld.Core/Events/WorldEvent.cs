using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace VividWorld.Core.Events
{
    public sealed class WorldEvent
    {
        public string EventId { get; set; } = string.Empty;
        public string Type { get; set; } = string.Empty;
        public double Day { get; set; }

        /// <summary>規格 §5.2（v3.2 修正）：<b>絕對不要加初始式。</b>
        /// Newtonsoft 在 JSON 缺欄位時不會覆寫屬性初始式，帶著 = EventOrigin.Public 的舊檔案
        /// 會反序列化成「公開」，保密不變式 fail-open。
        /// 無初始式 ⇒ default ⇒ Secret ⇒ fail-closed。</summary>
        public EventOrigin Origin { get; set; }

        public string? LinkedEventId { get; set; }
        public string? SituationId { get; set; }
        public int DramaWeight { get; set; } = 3;                       // 1..5，匯入時解析定案
        public Dictionary<string, string> Participants { get; set; } = new();   // role -> heroId
        public List<Fact> Facts { get; set; } = new();
        public List<KnownByEntry> KnownBy { get; set; } = new();
        public RumorState State { get; set; } = new();

        [JsonExtensionData]
        public IDictionary<string, JToken> Extra { get; set; } = new Dictionary<string, JToken>();

        public bool IsKnownBy(string heroId)
        {
            if (string.IsNullOrEmpty(heroId)) return false;
            return KnownBy.Any(k => k.HeroId == heroId);
        }

        public KnownByEntry? EntryFor(string heroId)
        {
            if (string.IsNullOrEmpty(heroId)) return null;
            return KnownBy.FirstOrDefault(k => k.HeroId == heroId);
        }

        public int MinHop()
        {
            if (KnownBy.Count == 0) return 0;
            return KnownBy.Min(k => k.Hop);
        }

        public int MaxHop()
        {
            if (KnownBy.Count == 0) return 0;
            return KnownBy.Max(k => k.Hop);
        }

        public string? RoleOf(string heroId)
        {
            if (string.IsNullOrEmpty(heroId)) return null;
            foreach (var kvp in Participants)
            {
                if (kvp.Value == heroId)
                {
                    return kvp.Key;
                }
            }
            return null;
        }

        /// <summary>未洩漏的秘密在整個傳聞系統裡不存在。計算屬性，絕不可寫進磁碟。</summary>
        [JsonIgnore]
        public bool IsVisibleToRumorSystem => Origin == EventOrigin.Public || State.Leaked;
    }
}
