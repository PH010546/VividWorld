#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using VividWorld.Core.Rumors;
using VividWorld.Core.Util;

namespace VividWorld.Core.Situations
{
    public sealed class MadeUpTalkCandidateRealEvent
    {
        public string EventId { get; set; } = string.Empty;
        public string Type { get; set; } = string.Empty;
        public double Day { get; set; }
        public string? SettlementId { get; set; }
        public Dictionary<string, string> Participants { get; set; } = new(StringComparer.OrdinalIgnoreCase);
        public HashSet<string> AlreadyLinkedTypes { get; set; } = new(StringComparer.OrdinalIgnoreCase);
        /// <summary>死者所屬王國（死者已不在活著的名單裡，由呼叫端查好帶進來）。</summary>
        public string? VictimKingdomId { get; set; }
        public string? CaptorHeroId => Participants.TryGetValue("captor", out var h) ? h : null;
        public string? PrisonerHeroId => Participants.TryGetValue("prisoner", out var h) ? h : null;
        public string? VictimHeroId => Participants.TryGetValue("victim", out var h) ? h : (Participants.TryGetValue("dead", out var d) ? d : null);
    }

    public sealed class MadeUpTalkPlanResult
    {
        public bool Success { get; set; }
        public string? ContentType { get; set; }
        public string? TargetRole { get; set; }
        public string? TargetHeroId { get; set; }
        public string? CounterpartRole { get; set; }
        public string? CounterpartHeroId { get; set; }
        public string? SettlementId { get; set; }
        public string? LinkedEventId { get; set; }
        public string? LinkedTemplateType { get; set; }
        public List<string> CandidateExplanations { get; set; } = new();
        public string Log { get; set; } = string.Empty;
    }

    public static class MadeUpTalkPlanner
    {
        public const string SpokeAgainstRuler = "conduct_spoke_against_ruler";
        public const string RefusedAid = "conduct_refused_aid";
        public const string RashCapture = "conduct_rash_capture";
        public const string MistreatedPrisoner = "conduct_mistreated_prisoner";
        public const string Poisoned = "conduct_poisoned";

        public const string VictoryCreditDeferred = "victory_credit_deferred";
        public const string AdviceGivenFreely = "advice_given_freely";
        public const string BrawlManHandedOver = "brawl_man_handed_over";
        public const string SeatDisputeYielded = "seat_dispute_yielded";
        public const string TavernGoodWord = "tavern_good_word";

        public static readonly string[] SlanderContents =
        {
            SpokeAgainstRuler,
            RefusedAid,
            RashCapture,
            MistreatedPrisoner,
            Poisoned
        };

        public static readonly string[] PraiseContents =
        {
            VictoryCreditDeferred,
            AdviceGivenFreely,
            BrawlManHandedOver,
            SeatDisputeYielded,
            TavernGoodWord
        };

        private sealed class EvaluatedContent
        {
            public string Type { get; set; } = string.Empty;
            public bool IsHangable { get; set; }
            public string Trait { get; set; } = string.Empty;
            public bool IsPositive { get; set; }
            public string TargetRole { get; set; } = string.Empty;
            public string CounterpartRole { get; set; } = string.Empty;
            public string CounterpartHeroId { get; set; } = string.Empty;
            public string? LinkedEventId { get; set; }
            public string? LinkedTemplateType { get; set; }
            public string? SettlementId { get; set; }
            public int TraitMatchLevel { get; set; }
            public bool FitsPersonality { get; set; }
            public string Note { get; set; } = string.Empty;
        }

        public static MadeUpTalkPlanResult Plan(
            string kind, // "slander" or "praise"
            SituationRoleFacts teller,
            SituationRoleFacts listener,
            SituationRoleFacts target,
            TraitProfile? tellerTraits,
            TraitProfile? targetTraits,
            string currentSettlementId,
            IReadOnlyCollection<SituationRoleFacts> candidatePool,
            IReadOnlyList<MadeUpTalkCandidateRealEvent>? realEvents,
            Func<string, string, bool>? hasGrudgeRecord,
            long campaignSeed,
            long instanceId,
            IDeterministicRng rng)
        {
            var result = new MadeUpTalkPlanResult
            {
                TargetHeroId = target.HeroId
            };

            bool isSlander = string.Equals(kind, "slander", StringComparison.OrdinalIgnoreCase);
            var contentList = isSlander ? SlanderContents : PraiseContents;

            var available = new List<EvaluatedContent>();
            var explanations = new List<string>();

            foreach (var type in contentList)
            {
                var eval = EvaluateContent(
                    type,
                    isSlander,
                    teller,
                    listener,
                    target,
                    targetTraits,
                    currentSettlementId,
                    candidatePool,
                    realEvents,
                    hasGrudgeRecord,
                    campaignSeed,
                    instanceId,
                    rng,
                    out string reason);

                if (eval != null)
                {
                    available.Add(eval);
                    explanations.Add($"{type}: available, weight 1{(eval.IsHangable ? $", hangs on {eval.LinkedEventId}" : ", no real event to hang on")}{(string.IsNullOrEmpty(eval.Note) ? "" : $", counterpart {eval.CounterpartHeroId} from {eval.Note}")}");
                }
                else
                {
                    explanations.Add($"{type}: unavailable ({reason})");
                }
            }

            result.CandidateExplanations = explanations;

            if (available.Count == 0)
            {
                result.Success = false;
                result.Log = $"made-up talk {kind} failed: no contents available. [{string.Join("; ", explanations)}]";
                return result;
            }

            // 有可掛的真事就只在掛得上的那幾種裡挑；沒有才憑空說
            var hangableAvailable = available.Where(a => a.IsHangable).ToList();
            if (hangableAvailable.Count > 0 && hangableAvailable.Count < available.Count)
            {
                explanations.Add($"only contents that hang on a real event are considered ({hangableAvailable.Count} of {available.Count})");
            }
            var poolToChooseFrom = hangableAvailable.Count > 0 ? hangableAvailable : available;

            // 說的人理性 >= 1：只挑對得上被說的人個性的那幾種
            int tellerCalc = tellerTraits?.Calculating ?? 0;
            EvaluatedContent? chosen = null;
            string selectionReason;

            if (tellerCalc >= 1)
            {
                var fitting = poolToChooseFrom.Where(c => c.FitsPersonality).ToList();
                if (fitting.Count > 0)
                {
                    int maxAbsLevel = fitting.Max(c => Math.Abs(c.TraitMatchLevel));
                    var bestFitting = fitting.Where(c => Math.Abs(c.TraitMatchLevel) == maxAbsLevel).ToList();

                    if (bestFitting.Count == 1)
                    {
                        chosen = bestFitting[0];
                        selectionReason = $"calculating teller picked trait-fit {chosen.Type} (level {chosen.TraitMatchLevel})";
                    }
                    else
                    {
                        long contentSeed = SituationSeed.Compute(instanceId, "madeUpTalk", "content");
                        int pickIdx = rng.Pick(bestFitting.Count, contentSeed);
                        chosen = bestFitting[pickIdx];
                        selectionReason = $"calculating teller tied trait-fit ({bestFitting.Count} candidates at level {maxAbsLevel}), picked {chosen.Type}";
                    }
                }
                else
                {
                    // Fallback to random pick from pool
                    long contentSeed = SituationSeed.Compute(instanceId, "madeUpTalk", "content");
                    int pickIdx = rng.Pick(poolToChooseFrom.Count, contentSeed);
                    chosen = poolToChooseFrom[pickIdx];
                    selectionReason = $"calculating teller found no trait-fit, fell back to random pick {chosen.Type}";
                }
            }
            else
            {
                long contentSeed = SituationSeed.Compute(instanceId, "madeUpTalk", "content");
                int pickIdx = rng.Pick(poolToChooseFrom.Count, contentSeed);
                chosen = poolToChooseFrom[pickIdx];
                selectionReason = $"uniform random picked {chosen.Type}";
            }

            result.Success = true;
            result.ContentType = chosen.Type;
            result.TargetRole = chosen.TargetRole;
            result.CounterpartRole = chosen.CounterpartRole;
            result.CounterpartHeroId = chosen.CounterpartHeroId;
            result.LinkedEventId = chosen.LinkedEventId;
            result.LinkedTemplateType = chosen.LinkedTemplateType;
            result.SettlementId = chosen.SettlementId;
            result.Log = $"{selectionReason}. Target={target.HeroId}, Counterpart={chosen.CounterpartHeroId}. [{string.Join("; ", explanations)}]";

            return result;
        }

        private static EvaluatedContent? EvaluateContent(
            string type,
            bool isSlander,
            SituationRoleFacts teller,
            SituationRoleFacts listener,
            SituationRoleFacts target,
            TraitProfile? targetTraits,
            string currentSettlementId,
            IReadOnlyCollection<SituationRoleFacts> candidatePool,
            IReadOnlyList<MadeUpTalkCandidateRealEvent>? realEvents,
            Func<string, string, bool>? hasGrudgeRecord,
            long campaignSeed,
            long instanceId,
            IDeterministicRng rng,
            out string failureReason)
        {
            failureReason = string.Empty;

            switch (type)
            {
                case SpokeAgainstRuler:
                {
                    if (string.IsNullOrEmpty(target.KingdomId))
                    {
                        failureReason = "target has no kingdom";
                        return null;
                    }
                    if (target.IsKingdomLeader)
                    {
                        failureReason = "target is ruler";
                        return null;
                    }

                    // Find ruler in candidatePool
                    var ruler = candidatePool.FirstOrDefault(c => c.IsAlive && c.IsLord && !c.IsPlayer &&
                                                                 c.HeroId != teller.HeroId && c.HeroId != listener.HeroId &&
                                                                 string.Equals(c.KingdomId, target.KingdomId, StringComparison.OrdinalIgnoreCase) &&
                                                                 c.IsKingdomLeader);
                    if (ruler == null || string.IsNullOrEmpty(ruler.HeroId))
                    {
                        failureReason = "kingdom ruler not found, is the player, or is the teller or listener";
                        return null;
                    }

                    int tVal = targetTraits?.Honor ?? 0;
                    return new EvaluatedContent
                    {
                        Type = type,
                        IsHangable = false,
                        Trait = "honor",
                        IsPositive = false,
                        TargetRole = "speaker",
                        CounterpartRole = "ruler",
                        CounterpartHeroId = ruler.HeroId,
                        SettlementId = currentSettlementId,
                        TraitMatchLevel = tVal,
                        FitsPersonality = tVal < 0
                    };
                }

                case RefusedAid:
                {
                    var counterpart = PickCounterpart(
                        target,
                        teller,
                        listener,
                        currentSettlementId,
                        candidatePool,
                        excludeSameClan: false,
                        campaignSeed,
                        instanceId,
                        type,
                        rng,
                        out string cpReason);

                    if (counterpart == null)
                    {
                        failureReason = $"no counterpart asker ({cpReason})";
                        return null;
                    }

                    int tVal = targetTraits?.Generosity ?? 0;
                    return new EvaluatedContent
                    {
                        Type = type,
                        IsHangable = false,
                        Trait = "generosity",
                        IsPositive = false,
                        TargetRole = "refuser",
                        CounterpartRole = "asker",
                        CounterpartHeroId = counterpart.HeroId,
                        Note = cpReason,
                        SettlementId = currentSettlementId,
                        TraitMatchLevel = tVal,
                        FitsPersonality = tVal < 0
                    };
                }

                case RashCapture:
                {
                    // Needs real hero_taken_prisoner event where target is PRISONER
                    var evt = FindBestRealEvent(
                        realEvents,
                        "hero_taken_prisoner",
                        type,
                        target.HeroId,
                        isTargetCaptor: false,
                        teller.HeroId,
                        listener.HeroId);

                    if (evt == null)
                    {
                        failureReason = "no hangable hero_taken_prisoner where target was prisoner";
                        return null;
                    }

                    string captorId = evt.CaptorHeroId ?? "";
                    int tVal = targetTraits?.Calculating ?? 0;
                    return new EvaluatedContent
                    {
                        Type = type,
                        IsHangable = true,
                        Trait = "calculating",
                        IsPositive = false,
                        TargetRole = "prisoner",
                        CounterpartRole = "captor",
                        CounterpartHeroId = captorId,
                        LinkedEventId = evt.EventId,
                        LinkedTemplateType = evt.Type,
                        SettlementId = evt.SettlementId, // 掛的那一則沒有地點就不給，碎片照既有規則掉
                        TraitMatchLevel = tVal,
                        FitsPersonality = tVal < 0
                    };
                }

                case MistreatedPrisoner:
                {
                    // Needs real hero_taken_prisoner event where target is CAPTOR
                    var evt = FindBestRealEvent(
                        realEvents,
                        "hero_taken_prisoner",
                        type,
                        target.HeroId,
                        isTargetCaptor: true,
                        teller.HeroId,
                        listener.HeroId);

                    if (evt == null)
                    {
                        failureReason = "no hangable hero_taken_prisoner where target was captor";
                        return null;
                    }

                    string prisonerId = evt.PrisonerHeroId ?? "";
                    int tVal = targetTraits?.Mercy ?? 0;
                    return new EvaluatedContent
                    {
                        Type = type,
                        IsHangable = true,
                        Trait = "mercy",
                        IsPositive = false,
                        TargetRole = "captor",
                        CounterpartRole = "prisoner",
                        CounterpartHeroId = prisonerId,
                        LinkedEventId = evt.EventId,
                        LinkedTemplateType = evt.Type,
                        SettlementId = evt.SettlementId, // 掛的那一則沒有地點就不給，碎片照既有規則掉
                        TraitMatchLevel = tVal,
                        FitsPersonality = tVal < 0
                    };
                }

                case Poisoned:
                {
                    // Needs real hero_died_of_old_age or hero_died_naturally event
                    // Target and victim must be same kingdom OR have grudge record
                    var evt = FindBestPoisonEvent(
                        realEvents,
                        type,
                        target,
                        candidatePool,
                        hasGrudgeRecord);

                    if (evt == null)
                    {
                        failureReason = "no hangable death event with valid poison target relation";
                        return null;
                    }

                    string victimId = evt.VictimHeroId ?? "";
                    int tVal = targetTraits?.Honor ?? 0;
                    return new EvaluatedContent
                    {
                        Type = type,
                        IsHangable = true,
                        Trait = "honor",
                        IsPositive = false,
                        TargetRole = "poisoner",
                        CounterpartRole = "victim",
                        CounterpartHeroId = victimId,
                        LinkedEventId = evt.EventId,
                        LinkedTemplateType = evt.Type,
                        SettlementId = evt.SettlementId, // 掛的那一則沒有地點就不給，碎片照既有規則掉
                        TraitMatchLevel = tVal,
                        FitsPersonality = tVal < 0
                    };
                }

                case VictoryCreditDeferred:
                case AdviceGivenFreely:
                case BrawlManHandedOver:
                case SeatDisputeYielded:
                case TavernGoodWord:
                {
                    var counterpart = PickCounterpart(
                        target,
                        teller,
                        listener,
                        currentSettlementId,
                        candidatePool,
                        excludeSameClan: true, // Praise excludes same clan
                        campaignSeed,
                        instanceId,
                        type,
                        rng,
                        out string cpReason);

                    if (counterpart == null)
                    {
                        failureReason = $"no counterpart ({cpReason})";
                        return null;
                    }

                    string trait = type == BrawlManHandedOver ? "honor" : "generosity";
                    int tVal = trait == "honor" ? (targetTraits?.Honor ?? 0) : (targetTraits?.Generosity ?? 0);

                    string tRole = type switch
                    {
                        VictoryCreditDeferred => "claimant",
                        AdviceGivenFreely => "veteran",
                        BrawlManHandedOver => "patron",
                        SeatDisputeYielded => "slighted",
                        _ => "speaker"
                    };

                    string cpRole = type switch
                    {
                        VictoryCreditDeferred => "rival",
                        AdviceGivenFreely => "student",
                        BrawlManHandedOver => "aggrieved",
                        SeatDisputeYielded => "favored",
                        _ => "listener"
                    };

                    return new EvaluatedContent
                    {
                        Type = type,
                        IsHangable = false,
                        Trait = trait,
                        IsPositive = true,
                        TargetRole = tRole,
                        CounterpartRole = cpRole,
                        CounterpartHeroId = counterpart.HeroId,
                        Note = cpReason,
                        SettlementId = currentSettlementId,
                        TraitMatchLevel = tVal,
                        FitsPersonality = tVal > 0
                    };
                }

                default:
                    failureReason = $"unknown content type '{type}'";
                    return null;
            }
        }

        private static SituationRoleFacts? PickCounterpart(
            SituationRoleFacts target,
            SituationRoleFacts teller,
            SituationRoleFacts listener,
            string currentSettlementId,
            IReadOnlyCollection<SituationRoleFacts> pool,
            bool excludeSameClan,
            long campaignSeed,
            long instanceId,
            string contentType,
            IDeterministicRng rng,
            out string reason)
        {
            if (string.IsNullOrEmpty(target.KingdomId))
            {
                reason = "target has no kingdom";
                return null;
            }

            var candidates = new List<SituationRoleFacts>();
            foreach (var c in pool)
            {
                if (c == null || string.IsNullOrEmpty(c.HeroId)) continue;
                if (!c.IsAlive || !c.IsLord || c.IsPlayer) continue;
                if (c.HeroId == teller.HeroId || c.HeroId == listener.HeroId || c.HeroId == target.HeroId) continue;
                if (!string.IsNullOrEmpty(currentSettlementId) && string.Equals(c.SettlementId, currentSettlementId, StringComparison.OrdinalIgnoreCase)) continue;
                if (!string.Equals(c.KingdomId, target.KingdomId, StringComparison.OrdinalIgnoreCase)) continue;
                if (excludeSameClan && !string.IsNullOrEmpty(target.ClanId) && string.Equals(c.ClanId, target.ClanId, StringComparison.OrdinalIgnoreCase)) continue;

                candidates.Add(c);
            }

            if (candidates.Count == 0)
            {
                reason = "no eligible counterparts";
                return null;
            }

            reason = $"{candidates.Count} candidate(s)";
            if (candidates.Count == 1) return candidates[0];

            var sorted = candidates.OrderBy(c => c.HeroId, StringComparer.Ordinal).ToList();
            long cpSeed = SituationSeed.Compute(instanceId, "madeUpTalk", "counterpart", contentType);
            int idx = rng.Pick(sorted.Count, cpSeed);
            return sorted[idx];
        }

        private static MadeUpTalkCandidateRealEvent? FindBestRealEvent(
            IReadOnlyList<MadeUpTalkCandidateRealEvent>? realEvents,
            string requiredEventType,
            string madeUpContentType,
            string targetHeroId,
            bool isTargetCaptor,
            string tellerHeroId,
            string listenerHeroId)
        {
            if (realEvents == null || realEvents.Count == 0) return null;

            return realEvents
                .Where(e => string.Equals(e.Type, requiredEventType, StringComparison.OrdinalIgnoreCase))
                .Where(e => !e.AlreadyLinkedTypes.Contains(madeUpContentType))
                .Where(e =>
                {
                    string? captor = e.CaptorHeroId;
                    string? prisoner = e.PrisonerHeroId;
                    if (string.IsNullOrEmpty(captor) || string.IsNullOrEmpty(prisoner)) return false;

                    // 另一位當事人是說的人或聽的人就不掛：說的人會變成編的話的參與者，聽的人等於當面拿他自己的事編給他聽
                    string? counterpart = isTargetCaptor ? prisoner : captor;
                    if (counterpart == tellerHeroId || counterpart == listenerHeroId) return false;

                    if (isTargetCaptor)
                    {
                        return captor == targetHeroId;
                    }
                    else
                    {
                        return prisoner == targetHeroId;
                    }
                })
                .OrderByDescending(e => e.Day)
                .FirstOrDefault();
        }

        private static MadeUpTalkCandidateRealEvent? FindBestPoisonEvent(
            IReadOnlyList<MadeUpTalkCandidateRealEvent>? realEvents,
            string madeUpContentType,
            SituationRoleFacts target,
            IReadOnlyCollection<SituationRoleFacts> pool,
            Func<string, string, bool>? hasGrudgeRecord)
        {
            if (realEvents == null || realEvents.Count == 0) return null;

            var poolByHeroId = new Dictionary<string, SituationRoleFacts>(StringComparer.Ordinal);
            foreach (var p in pool)
            {
                if (p != null && !string.IsNullOrEmpty(p.HeroId)) poolByHeroId[p.HeroId] = p;
            }

            return realEvents
                .Where(e => string.Equals(e.Type, "hero_died_of_old_age", StringComparison.OrdinalIgnoreCase) ||
                            string.Equals(e.Type, "hero_died_naturally", StringComparison.OrdinalIgnoreCase))
                .Where(e => !e.AlreadyLinkedTypes.Contains(madeUpContentType))
                .Where(e =>
                {
                    string? victimId = e.VictimHeroId;
                    if (string.IsNullOrEmpty(victimId) || victimId == target.HeroId) return false;

                    // 謀害可以指控誰：被說的人跟死者同王國，或兩人之間在恩怨帳上有任一筆紀錄
                    bool sameKingdom = false;
                    string? victimKingdom = e.VictimKingdomId;
                    if (string.IsNullOrEmpty(victimKingdom) && poolByHeroId.TryGetValue(victimId!, out var victimFacts))
                    {
                        victimKingdom = victimFacts.KingdomId;
                    }
                    if (!string.IsNullOrEmpty(target.KingdomId) && !string.IsNullOrEmpty(victimKingdom) &&
                        string.Equals(target.KingdomId, victimKingdom, StringComparison.OrdinalIgnoreCase))
                    {
                        sameKingdom = true;
                    }

                    bool grudge = hasGrudgeRecord != null && hasGrudgeRecord(target.HeroId, victimId!);

                    return sameKingdom || grudge;
                })
                .OrderByDescending(e => e.Day)
                .FirstOrDefault();
        }
    }
}
