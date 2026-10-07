#nullable enable
using System;
using System.Collections.Generic;

namespace VividWorld.Core.Dialogue
{
    public enum AskRefusal
    {
        None,                  // 有提案
        SharedToday,           // SharedToday >= SharesPerHeroPerDay (cap > 0)
        RelationGate,          // RelationWithPlayer < AskRelationGate
        WillingnessGate,       // willingness < AskWillingnessThreshold
        NoKnownEvents,         // 候選清單是空的
        AllCandidatesFiltered  // 有候選，但一則都不合格
    }

    /// <summary>單一候選被擋掉的確切理由。
    /// 「玩家已知」有三種截然不同的成因，收成同一個桶會讓人分不出
    /// 「重複的舊消息」與「更近的來源但講不出新東西」——後者看起來像缺陷，其實是規格 §7 的重述閘。</summary>
    public enum CandidateRejection
    {
        None,
        EventMissing,
        NotVisible,          // 秘密未洩漏
        FutureTimeline,      // 事件日期比今天晚（規格 §2.2.1）
        TellerDoesNotKnow,
        RetellDisabled,      // AllowRicherRetell = false
        RetellNoNewFacts,    // 這次能講的碎片玩家已經全有
        WontTellOwn,         // 當事人自己不講（selfTell）
        RetiredType,         // 停用的事件型別：已存下來的也不再傳，事件資料保留
        SecretHolderGist,    // 舊版留存
        PlayerHeardEnding,   // 玩家已經聽過某次被俘的結局
        LeakedSecretHonorable, // 走漏的秘密：誠實的人不講
        LeakedSecretCautiousStranger, // 走漏的秘密：謹慎的人只跟熟人講
        SecretHolderNotWilling, // 秘密當事人意願未過秘密線
        NotCloselyRelated,   // 主動講/熟人問：不屬於三類切身相關之一
        NotBigNews,          // 被問：份量未達大事門檻
        HeldBackShameful     // 醜事關係表判定不講給玩家
    }

    public sealed class AskDecision
    {
        public AskRefusal Refusal { get; set; }
        public RumorOffer? Offer { get; set; }
        public int Relation { get; set; }
        public double Willingness { get; set; }
        public double Threshold { get; set; }
        public double ActiveVolunteerLine { get; set; }
        public bool IsFamiliar { get; set; }
        public bool CanAnswer { get; set; }
        public double GenerosityTerm { get; set; }
        public double HonorTerm { get; set; }
        public double CalculatingTerm { get; set; }
        public string AnswerMode { get; set; } = string.Empty;
        public string ReasonCategory { get; set; } = string.Empty;
        public int ChosenTopicWeight { get; set; }
        public int ChosenTopicScale { get; set; }
        public string ChosenTopicWhy { get; set; } = string.Empty;
        public int SharedToday { get; set; }
        public int SharesPerHeroPerDay { get; set; }
        public int CandidateCount { get; set; }
        public int FilteredNotVisible { get; set; }   // 秘密未洩漏
        public int FilteredFutureTimeline { get; set; } // 日期比今天晚（規格 §2.2.1）
        public int FilteredPlayerKnows { get; set; }  // 玩家已知且重述不夠豐富
        public int FilteredOther { get; set; }        // 其餘（候選壞掉、講述者其實不知情）——留著數字才加得起來

        /// <summary>被重述閘擋掉的候選，每則一句話：哪一則事件、雙方各在第幾手、各有幾個碎片。
        /// 數字本身（「1 player already knows」）答不出「他明明是目擊者為什麼不肯講」，這一行答得出來。</summary>
        public List<string> FilterNotes { get; set; } = new List<string>();

        /// <summary>
        /// 格式化詢問傳聞診斷日誌（英文、不在地化，符合規格 §9.5.6 與 M6a-fix3 §3.3）。
        /// </summary>
        public static string FormatLog(
            string heroName,
            string heroId,
            AskDecision decision,
            int knownCount,
            int relationGate,
            int gen = 0,
            int hon = 0,
            int calc = 0)
        {
            if (decision == null) return string.Empty;

            string prefix = $"Ask {heroName} ({heroId}):";
            string knownStr = knownCount == 0 ? "knows nothing" : $"{knownCount} known";

            string willDetail = $"willingness {decision.Willingness:F1} (rel {decision.Relation}, gen {decision.GenerosityTerm:+0.0;-0.0;0.0}, hon {decision.HonorTerm:+0.0;-0.0;0.0}, calc {decision.CalculatingTerm:+0.0;-0.0;0.0})";

            switch (decision.Refusal)
            {
                case AskRefusal.None when decision.Offer != null:
                {
                    var offer = decision.Offer;
                    int filtered = decision.FilteredNotVisible + decision.FilteredFutureTimeline + decision.FilteredPlayerKnows + decision.FilteredOther;
                    string candidateUnit = decision.CandidateCount == 1 ? "candidate" : "candidates";
                    string modeStr = $" | mode {decision.AnswerMode}, reason {decision.ReasonCategory}, weight {decision.ChosenTopicWeight}, band {decision.ChosenTopicScale} ({decision.ChosenTopicWhy})";
                    string lineComparison = decision.IsFamiliar
                        ? $"willingness {decision.Willingness:F1} >= line {decision.ActiveVolunteerLine:F1} (familiar)"
                        : $"willingness {decision.Willingness:F1} >= threshold {decision.Threshold:F1} (stranger)";
                    string notes = decision.FilterNotes != null && decision.FilterNotes.Count > 0
                        ? Environment.NewLine + "    " + string.Join(Environment.NewLine + "    ", decision.FilterNotes)
                        : "";
                    return $"{prefix} offer {offer.EventId} hop {offer.TellerHop}->{offer.ResultingPlayerHop} score {offer.Score:F2}{modeStr} | {willDetail}, {lineComparison} | {knownCount} known, {decision.CandidateCount} {candidateUnit}, {filtered} filtered{notes}";
                }

                case AskRefusal.SharedToday:
                    return $"{prefix} refused - already shared today {decision.SharedToday}/{decision.SharesPerHeroPerDay}";

                case AskRefusal.RelationGate:
                    return $"{prefix} no offer - relation {decision.Relation} < gate {relationGate} | {knownStr}";

                case AskRefusal.WillingnessGate:
                    return $"{prefix} no offer - {willDetail} < threshold {decision.Threshold:F1} and < line {decision.ActiveVolunteerLine:F1} | {knownStr}";

                case AskRefusal.NoKnownEvents:
                    return $"{prefix} no offer - knows nothing | rel {decision.Relation}";

                case AskRefusal.AllCandidatesFiltered:
                {
                    var reasons = new List<string>();
                    if (decision.FilteredPlayerKnows > 0)
                    {
                        reasons.Add($"{decision.FilteredPlayerKnows} player already knows");
                    }
                    if (decision.FilteredNotVisible > 0)
                    {
                        reasons.Add($"{decision.FilteredNotVisible} secret not leaked");
                    }
                    if (decision.FilteredFutureTimeline > 0)
                    {
                        reasons.Add($"{decision.FilteredFutureTimeline} from a future timeline");
                    }
                    if (decision.FilteredOther > 0)
                    {
                        reasons.Add($"{decision.FilteredOther} other");
                    }
                    string detail = reasons.Count > 0 ? $" ({string.Join(", ", reasons)})" : "";
                    string candidateUnit = decision.CandidateCount == 1 ? "candidate" : "candidates";
                    string notes = decision.FilterNotes != null && decision.FilterNotes.Count > 0
                        ? Environment.NewLine + "    " + string.Join(Environment.NewLine + "    ", decision.FilterNotes)
                        : "";
                    return $"{prefix} no offer - all {decision.CandidateCount} {candidateUnit} filtered{detail} | {willDetail}{notes}";
                }

                default:
                    return $"{prefix} no offer | rel {decision.Relation}";
            }
        }
    }
}
