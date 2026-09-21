#nullable enable
using System;
using System.Collections.Generic;

namespace VividWorld.Core.Dialogue
{
    public enum AskRefusal
    {
        None,                  // 有提案
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
        RetellNotCloser,     // TellerHop + 1 >= PlayerExistingHop
        RetellNoNewFacts     // 更近，但該 hop 的碎片集合玩家已經全有
    }

    public sealed class AskDecision
    {
        public AskRefusal Refusal { get; set; }
        public RumorOffer? Offer { get; set; }
        public int Relation { get; set; }
        public double Willingness { get; set; }
        public double Threshold { get; set; }
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

            switch (decision.Refusal)
            {
                case AskRefusal.None when decision.Offer != null:
                {
                    var offer = decision.Offer;
                    int filtered = decision.FilteredNotVisible + decision.FilteredFutureTimeline + decision.FilteredPlayerKnows + decision.FilteredOther;
                    string candidateUnit = decision.CandidateCount == 1 ? "candidate" : "candidates";
                    return $"{prefix} offer {offer.EventId} hop {offer.TellerHop}->{offer.ResultingPlayerHop} score {offer.Score:F2} | rel {decision.Relation}, willingness {decision.Willingness:F1} >= {decision.Threshold:F1} | {knownCount} known, {decision.CandidateCount} {candidateUnit}, {filtered} filtered";
                }

                case AskRefusal.RelationGate:
                    return $"{prefix} no offer - relation {decision.Relation} < gate {relationGate} | {knownStr}";

                case AskRefusal.WillingnessGate:
                    return $"{prefix} no offer - willingness {decision.Willingness:F1} < {decision.Threshold:F1} | rel {decision.Relation} (gen {gen}, hon {hon}, calc {calc}) | {knownStr}";

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
                    return $"{prefix} no offer - all {decision.CandidateCount} {candidateUnit} filtered{detail} | rel {decision.Relation}, willingness {decision.Willingness:F1} >= {decision.Threshold:F1}{notes}";
                }

                default:
                    return $"{prefix} no offer | rel {decision.Relation}";
            }
        }
    }
}
