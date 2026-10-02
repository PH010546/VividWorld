using System;
using System.Collections.Generic;

namespace VividWorld.Core.Presentation
{
    public sealed class ComposedRumor
    {
        public IReadOnlyList<ComposedFactPart> Parts = Array.Empty<ComposedFactPart>();
        public string? PrefixTextId;                // 重述前綴等，可為 null
        public string? PrefixFallback;
        public IReadOnlyDictionary<string, string> PrefixVars = new Dictionary<string, string>();
        public string? SpeakerHeroId;               // 講述者英雄 id（第一人稱當事人判定）
        public string? SpeakerRole;                 // 講述者在事件中的角色名（小寫，例 claimant、student）
        public string? SourceHeroId;                // 來源知情者英雄 id
        public string? SourceRole;                  // 來源知情者角色名（小寫）
        public IReadOnlyDictionary<string, string> Roles = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase); // 角色小寫 -> 英雄 id
        public string? SentenceKeyCandidate;        // 整句句型候選鍵（第一優先）
        public IReadOnlyList<string> SentenceKeyCandidates = Array.Empty<string>(); // 候選鍵清單（依查找優先順序）
        public IReadOnlyList<string> SelfFeelingKeyCandidates = Array.Empty<string>(); // 當事人句尾候選鍵（由具體到一般）
        public string? SelfFeelingVariantKey;       // 依個性挑中的句尾版本鍵；null＝用不帶傾向的句尾
        public string? SelfFeelingVariantLog;       // 個性版本判定的理由（特質值、挑中的傾向或為什麼用預設）；null＝沒判定
        public IReadOnlyDictionary<string, string> SentenceVars = new Dictionary<string, string>(); // 剩下碎片的變數聯集
        public bool IsGist;                         // 交情不夠、只講大概：不接當事人句尾
        public bool HeldBack;                       // 只講大概而且確實少講了他知道的事
        public string? ClosingKey;                  // 只講大概又少講時接的收尾句鍵；null＝不接
        public VividWorld.Core.Feelings.FeelingDecision? Feeling; // 說話的人講給玩家聽的感想判定（含沒有感想的原因）；null＝沒判定過（紀事、推給 AI）
    }
}
