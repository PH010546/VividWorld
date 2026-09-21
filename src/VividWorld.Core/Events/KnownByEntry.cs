using System.Collections.Generic;
using System.Runtime.Serialization;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Linq;

namespace VividWorld.Core.Events
{
    public sealed class KnownByEntry
    {
        public string HeroId { get; set; } = string.Empty;
        public int Hop { get; set; }                        // 0 = 當事人／現場目擊者
        public double LearnedDay { get; set; }
        public string? SourceHeroId { get; set; }           // hop 0 為 null
        public string? AddedDetail { get; set; }            // 保留給加油添醋

        public double? Interest { get; set; }        // 0..1；null = 尚未計算
        public string? InterestSource { get; set; }  // 例 "relation -64 with lord_5_16" / "kin floor via lord_5_16"（dev 顯示用）
        public double? ForgetDay { get; set; }       // null = 尚未計算 ⇒ 視為記得

        /// <summary>規格 §6.9.3：這個人已經知道後續了（例如聽說那位俘虜被放了），手上這一則不再新鮮。
        /// null = 沒過時。只標記、不刪除——這一筆裡還有 RelationImpacts。</summary>
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public double? OutdatedDay { get; set; }

        public double? LastHeardDay { get; set; }    // 最近一次「再次聽到」的日子；從未發生為 null
        public int? HeardCount { get; set; }         // 初次得知之後又被提起幾次；null 視為 0
        public List<string>? HeardFromIds { get; set; }

        /// <summary>此人實際持有的碎片 id。null = 依 Hop 與 SourceHeroId 計算（正常情況，
        /// NPC 永遠是 null，磁碟大小不變）。非 null = 這個集合就是權威。規格 §9.1.1。</summary>
        public List<string>? KnownFactIds { get; set; }

        /// <summary>此人因這則傳聞而對他人施加的好感度變化。null = 尚未結算。規格 §6.7、§10.4.6。</summary>
        public List<RelationImpact>? RelationImpacts { get; set; }

        /// <summary>規格 §5.1：<b>任何會被寫進分片的型別都必須帶未知欄位保存。</b>
        /// EventShardStore.Flush() 是整片重寫，一次 read-modify-write 就會讓舊版建置永久刪掉
        /// 它不認識的欄位。這個類別是系統裡變動最頻繁的持久化型別（v3 加了 KnownFactIds、
        /// v3.1 加了 RelationImpacts），而 dev 版與 release 版共用同一個 Configs\VividWorld
        /// 資料夾，版本回退是受支援的日常流程。</summary>
        [JsonExtensionData]
        public IDictionary<string, JToken> Extra { get; set; } = new Dictionary<string, JToken>();
    }

    [JsonConverter(typeof(StringEnumConverter))]
    public enum GrudgeScope
    {
        [EnumMember(Value = "personal")] Personal = 0,
        [EnumMember(Value = "clan")]     Clan = 1
    }

    [JsonConverter(typeof(StringEnumConverter))]
    public enum GrudgeSource
    {
        [EnumMember(Value = "situation")] Situation = 0,   // 預設 0：磁碟上的舊資料全部是情境結下的
        [EnumMember(Value = "rumor")]     Rumor = 1
    }

    /// <summary>一筆已施加的好感度變化。這是「有方向的怨恨」的唯一儲存處——原生可寫入的
    /// base relation 是對稱的（規格 §15），方向性只能存在我們自己的資料裡。</summary>
    public sealed class RelationImpact
    {
        public string AboutHeroId { get; set; } = string.Empty;  // Personal = 對方本人；Clan = 對方的族長
        public double Requested { get; set; }                    // 模板要求的量（帳本上的恩怨值）
        public int Delta { get; set; }                           // 既有：實際寫進原生的量，量出來的；LedgerOnly 時 0
        public GrudgeScope Scope { get; set; } = GrudgeScope.Personal;
        public GrudgeSource Source { get; set; } = GrudgeSource.Situation;

        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public List<string>? NativePair { get; set; }            // 實際被寫入的兩位英雄 id；LedgerOnly 時 null

        public bool LedgerOnly { get; set; }

        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public string? EscalatedFrom { get; set; }               // 只有 Clan 有，"<A>|<B>"

        public string? SourceFactId { get; set; }
        public double AppliedDay { get; set; }
        public bool Contradicted { get; set; }
        public double ContradictedDay { get; set; } = -1;
        public bool Resolved { get; set; }
        public double ResolvedDay { get; set; } = -1;

        /// <summary>規格 §5.1：任何會被寫進分片的型別都必須帶未知欄位保存。</summary>
        [JsonExtensionData]
        public IDictionary<string, JToken> Extra { get; set; } = new Dictionary<string, JToken>();
    }
}
