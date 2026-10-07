using System;
using System.Collections.Generic;

namespace VividWorld.Core.Presentation
{
    public sealed class ChronicleEntry
    {
        public string EventId = string.Empty;
        public string EventType = string.Empty;
        public double Day;
        public double LearnedDay;
        public int PlayerHop;
        public string? SourceHeroId;
        public string? LinkedEventId;

        public string HeadlineTextId = string.Empty;   // 慣例：VividWorld_EventType_<type>
        public string HeadlineFallback = string.Empty; // 英文，來自模板的 headline 欄位

        /// <summary>帶名字的標題的鍵（俘虜類的消息才有）；其餘種類為 null。字串裡的名字佔位符見 <see cref="ChronicleHeadline.NameToken"/>。</summary>
        public string? NamedHeadlineTextId;

        /// <summary>標題要放的那個人（<c>hero:代號</c>）；紀錄裡找不到就是 null，標題退回不帶名字的版本。</summary>
        public string? HeadlineNameVar;

        public ComposedRumor Body = new();

        /// <summary>區塊內是否存在相互矛盾的說法。</summary>
        public bool HasConflict;

        /// <summary>矛盾的成因或對立事件代號（供除錯與開發工具顯示）。</summary>
        public string? ConflictReason;

        public Dictionary<string, string> RootParticipants = new(StringComparer.Ordinal);
        public Dictionary<string, string> Participants = new(StringComparer.Ordinal);

        /// <summary>區塊內的事（源頭算第一件事，後續脫逃／獲釋／獲救各算一件事）。</summary>
        public IReadOnlyList<ChronicleMatter> Matters = Array.Empty<ChronicleMatter>();

        /// <summary>每個告訴過玩家這件事的人一份，依第一次講的先後排列。</summary>
        public IReadOnlyList<ChronicleSource> Sources = Array.Empty<ChronicleSource>();

        // 以下由 Module 建 VM 時填，Core 一律留空字串／null
        public string DayLabel = string.Empty;
        public string? SourceHeroName;

        /// <summary>最後顯示的標題（帶名字，或退回不帶名字的）；Module 開紀事時算好填進來，沒填時為 null。</summary>
        public string? HeadlineText;
    }

    /// <summary>紀事裡的一份來源：誰告訴玩家、第幾手、他講的那些碎片組成的事實，以及他當時講的原句或附的感想。</summary>
    public sealed class ChronicleSource
    {
        public string? HeroId;
        public double Day;
        public int Hop;

        /// <summary>是否存了對話當下的原句。</summary>
        public bool HasSpokenLine;

        /// <summary>是否為打探的回答。</summary>
        public bool HasProbeAnswer;
        public string? ProbeAnswerKey;
        public IReadOnlyDictionary<string, string>? ProbeAnswerVars;
        public string? ProbeAddressKey;
        public string? ProbeAddressHeroId;

        /// <summary>有存原句時為重建的整句（含開頭語、當事人句尾／感想／收尾）；沒存時為照舊組的第三人稱事實。</summary>
        public ComposedRumor Body = new();

        /// <summary>
        /// 他當時附的感想，照存下來的句子鍵與代換值重建，不重新判定；這一份沒有感想時為 null。
        /// （有存原句時，感想已包含在 Body.Feeling 裡）
        /// </summary>
        public VividWorld.Core.Feelings.FeelingDecision? Feeling;

        public int FactCount;
    }
}
