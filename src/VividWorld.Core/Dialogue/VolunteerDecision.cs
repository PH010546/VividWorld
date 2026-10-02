#nullable enable
using System;
using System.Collections.Generic;

namespace VividWorld.Core.Dialogue
{
    public enum VolunteerTier
    {
        None = 0,
        Full,
        Gist
    }

    public enum VolunteerRefusal
    {
        None = 0,              // 有提案
        RelationGate = 1,      // RelationWithPlayer < gate (且非近親)
        SharedToday = 2,       // SharedToday >= SharesPerHeroPerDay (cap > 0)
        NoKnownEvents = 3,     // 候選清單是空的
        AllCandidatesFiltered = 4, // 有候選，但一則都不合格
        NoCloselyRelatedEvent = 5, // 沒有切身的事
        WillingnessGate = RelationGate
    }

    public enum VolunteerReasonCategory
    {
        None,
        TellerSelf,
        TellerKin,
        TellerClan,
        TellerRelation,
        TellerGrudge,
        PlayerRelated,
        Sequel
    }

    public sealed class VolunteerDecision
    {
        public VolunteerRefusal Refusal { get; set; }
        public RumorOffer? Offer { get; set; }

        public VolunteerTier Tier { get; set; } = VolunteerTier.None;
        public int Relation { get; set; }
        public int RelationGate { get; set; }
        public int FullRelationGate
        {
            get => RelationGate;
            set => RelationGate = value;
        }
        public int ChatRelationGate { get; set; }
        public bool IsCloseKin { get; set; }

        public double Willingness { get; set; }
        public double WillingnessLine { get; set; }
        public bool WillingnessPassed { get; set; }
        public double GenerosityTerm { get; set; }
        public double HonorTerm { get; set; }
        public double CalculatingTerm { get; set; }
        public string ReasonCategory { get; set; } = string.Empty;
        public int ChosenTopicWeight { get; set; }
        public int ChosenTopicScale { get; set; }
        public string ChosenTopicWhy { get; set; } = string.Empty;

        public double Day { get; set; }
        public int SharedToday { get; set; }
        public int SharesPerHeroPerDay { get; set; }

        public int CandidateCount { get; set; }
        public int FilteredNotVisible { get; set; }   // 秘密未洩漏
        public int FilteredFutureTimeline { get; set; } // 日期比今天晚（規格 §2.2.1）
        public int FilteredPlayerKnows { get; set; }  // 玩家已知且重述不夠豐富
        public int FilteredOther { get; set; }        // 其餘（候選壞掉、講述者其實不知情）

        /// <summary>被重述閘擋掉的候選，每則一句話。</summary>
        public List<string> FilterNotes { get; set; } = new List<string>();

        public static string FormatLordAboutToAttack(string heroName, string heroId)
        {
            return $"Volunteer {heroName} ({heroId}): silent - lord is about to attack (WillLordAttack)";
        }

        /// <summary>
        /// 格式化主動開口傳聞診斷日誌（英文、不在地化，符合規格 §9.5.6 與 M6b §4.1）。
        /// </summary>
        public static string FormatLog(
            string heroName,
            string heroId,
            VolunteerDecision decision,
            int knownCount = 0,
            int forgottenCount = 0)
        {
            if (decision == null) return string.Empty;

            string prefix = $"Volunteer {heroName} ({heroId}):";

            // `knownCount` 數的是「名字登記在這則事件裡」，遺忘不在它的判斷裡
            // （`KnownByIndex.EventsKnownBy` 只濾掉日期比今天晚的）。印成「N known」
            // 會跟底下逐則的 (forgotten) 直接打架——表頭說知道 6 件，下一行說忘了 5 件。
            // 所以這裡一律把「還記得幾件／登記幾件」兩個數字都講出來。
            string knownStr;
            if (knownCount == 0)
            {
                knownStr = "nothing on file";
            }
            else if (forgottenCount > 0)
            {
                knownStr = $"{knownCount - forgottenCount} remembered of {knownCount} on file";
            }
            else
            {
                knownStr = $"{knownCount} on file";
            }

            switch (decision.Refusal)
            {
                case VolunteerRefusal.None when decision.Offer != null:
                {
                    var offer = decision.Offer;
                    string tierStr = decision.Tier == VolunteerTier.Gist ? "gist" : "full";
                    string willDetail = $"willingness {decision.Willingness:F1} >= line {decision.WillingnessLine:F1} (rel {decision.Relation}, gen {decision.GenerosityTerm:+0.0;-0.0;0.0}, hon {decision.HonorTerm:+0.0;-0.0;0.0}, calc {decision.CalculatingTerm:+0.0;-0.0;0.0})";
                    string chosenStr = !string.IsNullOrEmpty(decision.ReasonCategory)
                        ? $" | reason {decision.ReasonCategory}, weight {decision.ChosenTopicWeight}, scale {decision.ChosenTopicScale} ({decision.ChosenTopicWhy})"
                        : "";
                    string notes = decision.FilterNotes != null && decision.FilterNotes.Count > 0
                        ? Environment.NewLine + "    " + string.Join(Environment.NewLine + "    ", decision.FilterNotes)
                        : "";
                    return $"{prefix} told {offer.EventId} hop {offer.TellerHop}->{offer.ResultingPlayerHop} score {offer.Score:F2}{chosenStr} | tier {tierStr} (chat gate {decision.ChatRelationGate}, full gate {decision.RelationGate}), {(decision.IsCloseKin && decision.Tier != VolunteerTier.Gist ? $"rel {decision.Relation} (close kin)" : $"rel {decision.Relation} >= {(decision.Tier == VolunteerTier.Gist ? decision.ChatRelationGate : decision.RelationGate)}")}, {willDetail}, shared today {decision.SharedToday}/{decision.SharesPerHeroPerDay}{notes}";
                }

                case VolunteerRefusal.RelationGate:
                {
                    string kinStr = decision.IsCloseKin ? "(close kin)" : "(not close kin)";
                    string willDetail = $"willingness {decision.Willingness:F1} < line {decision.WillingnessLine:F1} (rel {decision.Relation}, gen {decision.GenerosityTerm:+0.0;-0.0;0.0}, hon {decision.HonorTerm:+0.0;-0.0;0.0}, calc {decision.CalculatingTerm:+0.0;-0.0;0.0})";
                    return $"{prefix} silent - not familiar enough to bring things up: {willDetail} {kinStr} | {knownStr}";
                }

                case VolunteerRefusal.SharedToday:
                {
                    int today = DailyCounter.BucketOf(decision.Day);
                    string tierStr = decision.Tier == VolunteerTier.Gist ? "gist" : (decision.Tier == VolunteerTier.Full ? "full" : "none");
                    return $"{prefix} silent - already shared today {decision.SharedToday}/{decision.SharesPerHeroPerDay} (day {today}) | rel {decision.Relation} (tier {tierStr}, chat gate {decision.ChatRelationGate}, full gate {decision.RelationGate})";
                }

                case VolunteerRefusal.NoKnownEvents:
                    // 走到這裡是「候選一個都沒有」，而候選已經把遺忘與過時濾掉了
                    // （`RumorDialogBehavior.BuildCandidates`）。印「knows nothing」會把
                    // 「全部忘光了」講成「從來沒聽過」，所以這裡也要帶上 knownStr。
                    string noTopicTierStr = decision.Tier == VolunteerTier.Gist ? "gist" : (decision.Tier == VolunteerTier.Full ? "full" : "none");
                    return $"{prefix} silent - no topic left | {knownStr} | rel {decision.Relation} (tier {noTopicTierStr}, chat gate {decision.ChatRelationGate}, full gate {decision.RelationGate})";

                case VolunteerRefusal.AllCandidatesFiltered:
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
                    string filtTierStr = decision.Tier == VolunteerTier.Gist ? "gist" : (decision.Tier == VolunteerTier.Full ? "full" : "none");
                    string willDetail = $"willingness {decision.Willingness:F1} >= line {decision.WillingnessLine:F1} (rel {decision.Relation}, gen {decision.GenerosityTerm:+0.0;-0.0;0.0}, hon {decision.HonorTerm:+0.0;-0.0;0.0}, calc {decision.CalculatingTerm:+0.0;-0.0;0.0})";
                    return $"{prefix} silent - all {decision.CandidateCount} {candidateUnit} filtered{detail} | rel {decision.Relation} ({willDetail}) (tier {filtTierStr}, chat gate {decision.ChatRelationGate}, full gate {decision.RelationGate}){notes}";
                }

                case VolunteerRefusal.NoCloselyRelatedEvent:
                {
                    string notes = decision.FilterNotes != null && decision.FilterNotes.Count > 0
                        ? Environment.NewLine + "    " + string.Join(Environment.NewLine + "    ", decision.FilterNotes)
                        : "";
                    string willDetail = $"willingness {decision.Willingness:F1} >= line {decision.WillingnessLine:F1} (rel {decision.Relation}, gen {decision.GenerosityTerm:+0.0;-0.0;0.0}, hon {decision.HonorTerm:+0.0;-0.0;0.0}, calc {decision.CalculatingTerm:+0.0;-0.0;0.0})";
                    return $"{prefix} silent - no closely related topic | {knownStr} | rel {decision.Relation} ({willDetail}){notes}";
                }

                default:
                    return $"{prefix} silent | rel {decision.Relation}";
            }
        }
    }
}
