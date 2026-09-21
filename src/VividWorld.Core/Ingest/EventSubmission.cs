using System.Collections.Generic;
using VividWorld.Core.Events;

namespace VividWorld.Core.Ingest
{
    public sealed class EventSubmission
    {
        public string Type = string.Empty;
        public double Day;

        /// <summary>規格 §10.1（v3.2 修正）：<b>必須可空。</b>
        /// 非可空列舉沒有「未指定」這個可表示狀態，驗證器無從判斷提交者是否真的填了，
        /// 而 Secret 是 0 值、缺漏會靜默變成秘密。null 一律拒收。</summary>
        public EventOrigin? Origin;

        public string? LinkedEventId;
        public string? SituationId;
        public int? DramaWeight;                                 // null → 匯入時由設定依 Type 解析
        public Dictionary<string, string> Participants = new();  // 不得為空

        /// <summary>參與者中「確實知道這件事」的角色名。空集合代表全部參與者都知道。
        /// 設計文件 §6 的範例：participants 有 mastermind/target/agent 三人，
        /// 但 knownBy 只有 mastermind 與 agent——受害者 Boris 不知道自己被下手。</summary>
        public HashSet<string> KnowingRoles = new();

        public List<Fact> Facts = new();                         // 不得為空
        public List<string> InitialKnowerHeroIds = new();
        public bool AutoResolveWitnesses = true;                 // 僅 Public 有效
    }

    public enum IngestResult
    {
        Accepted,
        RejectedInvalid,
        RejectedDuplicate,
        Deferred
    }

    public interface IWorldEventSink
    {
        IngestResult Submit(EventSubmission submission, out string? eventId, out string? rejectionReason);
    }
}
