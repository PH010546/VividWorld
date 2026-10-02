#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using VividWorld.Core.Events;

namespace VividWorld.Core.Persistence
{
    /// <summary>
    /// 告訴過玩家某件事的一個人，以及他那一次講了什麼。
    /// 存的是碎片代號與感想句的字串鍵，不存寫好的文字：切換遊戲語言時紀事跟著換，
    /// 而且感想是講的當下挑定的那一句，打開紀事時不重算。
    /// </summary>
    public sealed class PlayerHeardSource
    {
        /// <summary>講的人；不明（舊紀錄沒留來源）時為 null。</summary>
        public string? HeroId { get; set; }

        /// <summary>講給玩家的那一天。</summary>
        public double Day { get; set; }

        /// <summary>這一份真正的手數：講的人的手數 + 1。只記距離，不表示講了多少。</summary>
        public int Hop { get; set; }

        /// <summary>他講了哪幾塊碎片（碎片代號）。</summary>
        public List<string> FactIds { get; set; } = new();

        /// <summary>他附的感想：句子字串鍵；這一份沒有感想時為 null。</summary>
        public string? FeelingLineKey { get; set; }

        /// <summary>感想句裡 <c>{ADDRESS}</c> 的稱呼字串鍵；感想句沒有稱呼時可為 null。</summary>
        public string? FeelingAddressKey { get; set; }

        /// <summary>稱呼裡 <c>{NAME}</c> 要代換成誰的名字（代換值，存英雄代號）。</summary>
        public string? FeelingFocusHeroId { get; set; }

        /// <summary>開頭語字串鍵；可為 null。</summary>
        public string? PrefixTextId { get; set; }

        /// <summary>開頭語代換值（變數鍵 -> 值）。</summary>
        public Dictionary<string, string> PrefixVars { get; set; } = new();

        /// <summary>講述者英雄 id（第一人稱當事人判定）。</summary>
        public string? SpeakerHeroId { get; set; }

        /// <summary>講述者在事件中的角色名（小寫，例 claimant、student）。</summary>
        public string? SpeakerRole { get; set; }

        /// <summary>來源知情者英雄 id。</summary>
        public string? SourceHeroId { get; set; }

        /// <summary>來源知情者角色名（小寫）。</summary>
        public string? SourceRole { get; set; }

        /// <summary>角色小寫 -> 英雄 id。</summary>
        public Dictionary<string, string> Roles { get; set; } = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>整句句型候選鍵清單（依查找優先順序）。</summary>
        public List<string> SentenceKeyCandidates { get; set; } = new();

        /// <summary>當事人句尾候選鍵清單（由具體到一般，已插入個性版本）。</summary>
        public List<string> SelfFeelingKeyCandidates { get; set; } = new();

        /// <summary>交情不夠、只講大概：不接當事人句尾。</summary>
        public bool IsGist { get; set; }

        /// <summary>只講大概而且確實少講了他知道的事。</summary>
        public bool HeldBack { get; set; }

        /// <summary>只講大概又少講時接的收尾句鍵；null＝不接。</summary>
        public string? ClosingKey { get; set; }

        [JsonExtensionData]
        public IDictionary<string, JToken> Extra { get; set; } = new Dictionary<string, JToken>();

        [JsonIgnore]
        public bool HasFeeling => !string.IsNullOrEmpty(FeelingLineKey);

        [JsonIgnore]
        public bool HasSpokenLine => (SentenceKeyCandidates != null && SentenceKeyCandidates.Count > 0)
            || !string.IsNullOrEmpty(SpeakerHeroId)
            || !string.IsNullOrEmpty(PrefixTextId);

        [JsonIgnore]
        public string? FirstSentenceCandidate => SentenceKeyCandidates != null && SentenceKeyCandidates.Count > 0
            ? SentenceKeyCandidates[0]
            : null;

        [JsonIgnore]
        public string TrailingKind
        {
            get
            {
                if (SelfFeelingKeyCandidates != null && SelfFeelingKeyCandidates.Count > 0 && !IsGist && !string.IsNullOrEmpty(SpeakerRole))
                {
                    return "self feeling";
                }
                if (HasFeeling)
                {
                    return "feeling";
                }
                if (!string.IsNullOrEmpty(ClosingKey))
                {
                    return "closing";
                }
                return "none";
            }
        }

        /// <summary>
        /// 把對話當下組好的那一句抄下來：開頭語、說話的人與來源的角色、事實句與當事人句尾的候選鍵、
        /// 是不是只講大概、收尾。紀事之後只靠這些欄位重組同一句，不再回頭判定。
        /// </summary>
        public void CaptureSpokenLine(VividWorld.Core.Presentation.ComposedRumor? composed)
        {
            if (composed == null) return;

            PrefixTextId = composed.PrefixTextId;
            PrefixVars = composed.PrefixVars != null
                ? composed.PrefixVars.ToDictionary(kv => kv.Key, kv => kv.Value)
                : new Dictionary<string, string>();
            SpeakerHeroId = composed.SpeakerHeroId;
            SpeakerRole = composed.SpeakerRole;
            SourceHeroId = composed.SourceHeroId;
            SourceRole = composed.SourceRole;
            Roles = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (composed.Roles != null)
            {
                foreach (var kv in composed.Roles) Roles[kv.Key] = kv.Value;
            }
            SentenceKeyCandidates = composed.SentenceKeyCandidates != null
                ? new List<string>(composed.SentenceKeyCandidates)
                : new List<string>();
            SelfFeelingKeyCandidates = composed.SelfFeelingKeyCandidates != null
                ? new List<string>(composed.SelfFeelingKeyCandidates)
                : new List<string>();
            IsGist = composed.IsGist;
            HeldBack = composed.HeldBack;
            ClosingKey = composed.ClosingKey;
        }

        public PlayerHeardSource Clone()
        {
            return new PlayerHeardSource
            {
                HeroId = HeroId,
                Day = Day,
                Hop = Hop,
                FactIds = new List<string>(FactIds ?? new List<string>()),
                FeelingLineKey = FeelingLineKey,
                FeelingAddressKey = FeelingAddressKey,
                FeelingFocusHeroId = FeelingFocusHeroId,
                PrefixTextId = PrefixTextId,
                PrefixVars = PrefixVars != null ? new Dictionary<string, string>(PrefixVars) : new(),
                SpeakerHeroId = SpeakerHeroId,
                SpeakerRole = SpeakerRole,
                SourceHeroId = SourceHeroId,
                SourceRole = SourceRole,
                Roles = Roles != null ? new Dictionary<string, string>(Roles, StringComparer.OrdinalIgnoreCase) : new(StringComparer.OrdinalIgnoreCase),
                SentenceKeyCandidates = SentenceKeyCandidates != null ? new List<string>(SentenceKeyCandidates) : new(),
                SelfFeelingKeyCandidates = SelfFeelingKeyCandidates != null ? new List<string>(SelfFeelingKeyCandidates) : new(),
                IsGist = IsGist,
                HeldBack = HeldBack,
                ClosingKey = ClosingKey,
                Extra = Extra != null
                    ? new Dictionary<string, JToken>(Extra)
                    : new Dictionary<string, JToken>()
            };
        }

        /// <summary>
        /// 舊紀錄（沒有來源清單）當成只有一份：碎片取整筆紀錄的碎片、來源取最上層的來源、
        /// 手數取最上層的手數、日期取第一次聽到的那天。舊紀錄沒存感想，所以沒有感想。
        /// </summary>
        public static PlayerHeardSource FromLegacy(PlayerHeardEntry entry)
        {
            return new PlayerHeardSource
            {
                HeroId = string.IsNullOrEmpty(entry.SourceHeroId) ? null : entry.SourceHeroId,
                Day = entry.LearnedDay,
                Hop = entry.PlayerHop,
                FactIds = (entry.Facts ?? new List<Fact>()).Select(f => f.Id).ToList()
            };
        }

        public static bool SameTeller(PlayerHeardSource a, PlayerHeardSource b)
        {
            return string.Equals(a.HeroId ?? string.Empty, b.HeroId ?? string.Empty, StringComparison.Ordinal);
        }
    }
}
