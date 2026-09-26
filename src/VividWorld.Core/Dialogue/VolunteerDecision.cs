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
        None,                  // 有提案
        RelationGate,          // RelationWithPlayer < gate (且非近親)
        Cooldown,              // day - LastVolunteeredDay < VolunteerCooldownDays
        DailyCap,              // volunteersAlreadyToday >= MaxVolunteersPerDay
        NoKnownEvents,         // 候選清單是空的
        AllCandidatesFiltered  // 有候選，但一則都不合格
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

        public double Day { get; set; }
        public double LastVolunteeredDay { get; set; }
        public double CooldownDays { get; set; }

        public int VolunteersToday { get; set; }
        public int MaxVolunteersPerDay { get; set; }

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
            string? lastVolunteerDesc = null,
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
                    string lastStr;
                    if (decision.LastVolunteeredDay >= 0)
                    {
                        double diff = decision.Day - decision.LastVolunteeredDay;
                        lastStr = $"last {decision.LastVolunteeredDay:F1} (+{diff:F1}d >= {decision.CooldownDays:F1})";
                    }
                    else
                    {
                        lastStr = "last never";
                    }
                    string tierStr = decision.Tier == VolunteerTier.Gist ? "gist" : "full";
                    return $"{prefix} told {offer.EventId} hop {offer.TellerHop}->{offer.ResultingPlayerHop} score {offer.Score:F2} | tier {tierStr} (chat gate {decision.ChatRelationGate}, full gate {decision.RelationGate}), {(decision.IsCloseKin && decision.Tier != VolunteerTier.Gist ? $"rel {decision.Relation} (close kin)" : $"rel {decision.Relation} >= {(decision.Tier == VolunteerTier.Gist ? decision.ChatRelationGate : decision.RelationGate)}")}, {lastStr}, {decision.VolunteersToday}/{decision.MaxVolunteersPerDay} today";
                }

                case VolunteerRefusal.RelationGate:
                {
                    string kinStr = decision.IsCloseKin ? "(close kin)" : "(not close kin)";
                    return $"{prefix} silent - relation {decision.Relation} < gate {decision.RelationGate} {kinStr} (chat gate {decision.ChatRelationGate}, full gate {decision.RelationGate}) | {knownStr}";
                }

                case VolunteerRefusal.Cooldown:
                {
                    double diff = decision.Day - decision.LastVolunteeredDay;
                    string tierStr = decision.Tier == VolunteerTier.Gist ? "gist" : (decision.Tier == VolunteerTier.Full ? "full" : "none");
                    return $"{prefix} silent - cooldown {diff:F1}d < {decision.CooldownDays:F1} (last {decision.LastVolunteeredDay:F1}) | rel {decision.Relation} (tier {tierStr}, chat gate {decision.ChatRelationGate}, full gate {decision.RelationGate})";
                }

                case VolunteerRefusal.DailyCap:
                {
                    string lastPart = !string.IsNullOrEmpty(lastVolunteerDesc) ? $" (last: {lastVolunteerDesc})" : "";
                    string tierStr = decision.Tier == VolunteerTier.Gist ? "gist" : (decision.Tier == VolunteerTier.Full ? "full" : "none");
                    return $"{prefix} silent - daily cap {decision.VolunteersToday}/{decision.MaxVolunteersPerDay} already used today{lastPart} | tier {tierStr} (chat gate {decision.ChatRelationGate}, full gate {decision.RelationGate})";
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
                    return $"{prefix} silent - all {decision.CandidateCount} {candidateUnit} filtered{detail} | rel {decision.Relation} (tier {filtTierStr}, chat gate {decision.ChatRelationGate}, full gate {decision.RelationGate}){notes}";
                }

                default:
                    return $"{prefix} silent | rel {decision.Relation}";
            }
        }
    }
}
