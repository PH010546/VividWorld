#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using VividWorld.Core.Ingest;

namespace VividWorld.Core.Events
{
    public static class MadeUpTalk
    {
        public static bool IsHearsayOnly(WorldEvent? evt)
        {
            if (evt == null || !evt.Fabricated || string.IsNullOrEmpty(evt.OriginatorHeroId))
            {
                return false;
            }

            if (evt.Participants != null && evt.Participants.Values.Contains(evt.OriginatorHeroId))
            {
                return false;
            }

            return true;
        }

        public static bool IsHearsayOnly(EventSubmission? s)
        {
            if (s == null || !s.Fabricated || string.IsNullOrEmpty(s.OriginatorHeroId))
            {
                return false;
            }

            if (s.Participants != null && s.Participants.Values.Contains(s.OriginatorHeroId))
            {
                return false;
            }

            return true;
        }

        /// <summary>不信、又知道是誰起頭的人，對起頭的人記的恩怨：那句話分數絕對值的一半（好話也是扣）。
        /// 不乘手數、旁人、兩個倍數。</summary>
        public static double OriginatorGrudge(double opinionScore) => -System.Math.Abs(opinionScore) / 2.0;

        /// <summary>
        /// 計算知道實情的人（或被說的人）對起頭者的恩怨扣分與是否受每日上限限制。
        /// 壞話被說的人 1.5 倍且不受上限；好話被說的人僅榮譽 >= +1 記恨且受上限；其餘知情人折半且受上限。
        /// </summary>
        public static (double requested, bool isCapped) CalculateKnowerOriginatorGrudge(
            bool isAccused,
            bool isGoodTalk,
            int honor,
            double opinionScore,
            double accusedMultiplier = 1.5)
        {
            if (isAccused)
            {
                if (isGoodTalk)
                {
                    if (honor >= 1)
                    {
                        return (OriginatorGrudge(opinionScore), true);
                    }
                    return (0.0, true);
                }
                else
                {
                    return (accusedMultiplier * opinionScore, false);
                }
            }
            else
            {
                return (OriginatorGrudge(opinionScore), true);
            }
        }

        public static bool KnowsOriginator(WorldEvent? evt, KnownByEntry? knower)
        {
            if (!IsHearsayOnly(evt) || knower == null || evt == null)
            {
                return false;
            }

            if (string.IsNullOrEmpty(knower.HeroId) || knower.HeroId == evt.OriginatorHeroId)
            {
                return false;
            }

            if (knower.OriginatorKnownOverride.HasValue)
            {
                return knower.OriginatorKnownOverride.Value;
            }

            return knower.Hop == 2 || knower.Hop == 3;
        }

        public static bool KnowsOriginator(WorldEvent? evt, string heroId, int hop)
        {
            if (!IsHearsayOnly(evt) || evt == null)
            {
                return false;
            }

            if (string.IsNullOrEmpty(heroId) || heroId == evt.OriginatorHeroId)
            {
                return false;
            }

            return hop == 2 || hop == 3;
        }

        public static string? GetAccusedHeroId(WorldEvent? evt)
        {
            if (evt?.Participants == null) return null;
            return evt.Type switch
            {
                "conduct_spoke_against_ruler" => evt.Participants.TryGetValue("speaker", out var h) ? h : null,
                "conduct_refused_aid" => evt.Participants.TryGetValue("refuser", out var h) ? h : null,
                "conduct_rash_capture" => evt.Participants.TryGetValue("prisoner", out var h) ? h : null,
                "conduct_mistreated_prisoner" => evt.Participants.TryGetValue("captor", out var h) ? h : null,
                "conduct_poisoned" => evt.Participants.TryGetValue("poisoner", out var h) ? h : null,
                "victory_credit_deferred" => evt.Participants.TryGetValue("claimant", out var h) ? h : null,
                "advice_given_freely" => evt.Participants.TryGetValue("veteran", out var h) ? h : null,
                "brawl_man_handed_over" => evt.Participants.TryGetValue("patron", out var h) ? h : null,
                "seat_dispute_yielded" => evt.Participants.TryGetValue("slighted", out var h) ? h : null,
                "tavern_good_word" => evt.Participants.TryGetValue("speaker", out var h) ? h : null,
                _ => null
            };
        }

        public static IReadOnlyList<TruthKnower> GetTruthKnowers(WorldEvent x, WorldEvent? l, string? playerHeroId = null)
        {
            if (!IsHearsayOnly(x) || x.Participants == null)
            {
                return System.Array.Empty<TruthKnower>();
            }

            string? originatorId = x.OriginatorHeroId;
            bool IsEligibleKnower(string? h) =>
                !string.IsNullOrEmpty(h) &&
                h != originatorId;

            var candidates = new System.Collections.Generic.List<TruthKnower>();

            // 1. Accused (被說的人)
            string? accusedId = GetAccusedHeroId(x);
            if (!string.IsNullOrEmpty(accusedId))
            {
                string? denialResponse = x.Type switch
                {
                    "conduct_spoke_against_ruler" => "talk_denied_spoke_against_ruler",
                    "conduct_refused_aid" => "talk_denied_refused_aid",
                    "conduct_rash_capture" => "talk_denied_rash_capture",
                    "conduct_mistreated_prisoner" => "talk_denied_mistreated_prisoner",
                    "conduct_poisoned" => "talk_denied_poisoned",
                    _ => null // Praises do not step forward
                };
                candidates.Add(new TruthKnower
                {
                    HeroId = accusedId!,
                    ResponseType = denialResponse,
                    Reason = "accused",
                    IsAccused = true
                });
            }

            // 2. Specific steppers-forward
            switch (x.Type)
            {
                case "conduct_rash_capture":
                    if (x.Participants.TryGetValue("captor", out var captorId))
                    {
                        candidates.Add(new TruthKnower
                        {
                            HeroId = captorId,
                            ResponseType = "talk_corrected_rash_capture_by_captor",
                            Reason = "captor"
                        });
                    }
                    if (l?.KnownBy != null)
                    {
                        var lPartSet = new System.Collections.Generic.HashSet<string>(
                            l.Participants?.Values ?? System.Linq.Enumerable.Empty<string>(),
                            System.StringComparer.Ordinal);

                        foreach (var kn in l.KnownBy)
                        {
                            if (kn.Hop == 0 && !string.IsNullOrEmpty(kn.HeroId) && !lPartSet.Contains(kn.HeroId))
                            {
                                candidates.Add(new TruthKnower
                                {
                                    HeroId = kn.HeroId,
                                    ResponseType = "talk_corrected_rash_capture_by_bystander",
                                    Reason = "bystander"
                                });
                            }
                        }
                    }
                    break;

                case "conduct_mistreated_prisoner":
                    if (x.Participants.TryGetValue("prisoner", out var prisonerId))
                    {
                        candidates.Add(new TruthKnower
                        {
                            HeroId = prisonerId,
                            ResponseType = "talk_corrected_mistreated_by_prisoner",
                            Reason = "prisoner"
                        });
                    }
                    if (l?.CaptorArmyLeaderHeroIds != null)
                    {
                        x.Participants.TryGetValue("captor", out var xCaptorId);
                        foreach (var comradeId in l.CaptorArmyLeaderHeroIds)
                        {
                            if (!string.IsNullOrEmpty(comradeId) && comradeId != xCaptorId)
                            {
                                candidates.Add(new TruthKnower
                                {
                                    HeroId = comradeId,
                                    ResponseType = "talk_corrected_mistreated_by_comrade",
                                    Reason = "comrade"
                                });
                            }
                        }
                    }
                    break;

                case "conduct_refused_aid":
                    if (x.Participants.TryGetValue("asker", out var askerId))
                    {
                        candidates.Add(new TruthKnower
                        {
                            HeroId = askerId,
                            ResponseType = "talk_corrected_refused_aid_by_asker",
                            Reason = "asker"
                        });
                    }
                    break;

                case "victory_credit_deferred":
                    if (x.Participants.TryGetValue("rival", out var rivalId))
                    {
                        candidates.Add(new TruthKnower
                        {
                            HeroId = rivalId,
                            ResponseType = "talk_not_so_victory_credit_deferred",
                            Reason = "beneficiary"
                        });
                    }
                    break;

                case "advice_given_freely":
                    if (x.Participants.TryGetValue("student", out var studentId))
                    {
                        candidates.Add(new TruthKnower
                        {
                            HeroId = studentId,
                            ResponseType = "talk_not_so_advice_given_freely",
                            Reason = "beneficiary"
                        });
                    }
                    break;

                case "brawl_man_handed_over":
                    if (x.Participants.TryGetValue("aggrieved", out var aggrievedId))
                    {
                        candidates.Add(new TruthKnower
                        {
                            HeroId = aggrievedId,
                            ResponseType = "talk_not_so_brawl_man_handed_over",
                            Reason = "beneficiary"
                        });
                    }
                    break;

                case "seat_dispute_yielded":
                    if (x.Participants.TryGetValue("favored", out var favoredId))
                    {
                        candidates.Add(new TruthKnower
                        {
                            HeroId = favoredId,
                            ResponseType = "talk_not_so_seat_dispute_yielded",
                            Reason = "beneficiary"
                        });
                    }
                    break;

                case "tavern_good_word":
                    if (x.Participants.TryGetValue("listener", out var listenerId))
                    {
                        candidates.Add(new TruthKnower
                        {
                            HeroId = listenerId,
                            ResponseType = "talk_not_so_tavern_good_word",
                            Reason = "beneficiary"
                        });
                    }
                    break;
            }

            // 3. Passive knowers (no response)
            if (x.Type == "conduct_poisoned")
            {
                if (x.Participants.TryGetValue("victim", out var victimId))
                {
                    candidates.Add(new TruthKnower
                    {
                        HeroId = victimId,
                        ResponseType = null,
                        Reason = "victim"
                    });
                }
            }

            if (l != null && (x.Type == "conduct_rash_capture" || x.Type == "conduct_mistreated_prisoner" || x.Type == "conduct_poisoned"))
            {
                if (l.Participants != null)
                {
                    foreach (var h in l.Participants.Values)
                    {
                        if (!string.IsNullOrEmpty(h))
                        {
                            candidates.Add(new TruthKnower
                            {
                                HeroId = h,
                                ResponseType = null,
                                Reason = "hung_participant"
                            });
                        }
                    }
                }
                if (l.KnownBy != null)
                {
                    foreach (var kn in l.KnownBy)
                    {
                        if (kn.Hop == 0 && !string.IsNullOrEmpty(kn.HeroId))
                        {
                            candidates.Add(new TruthKnower
                            {
                                HeroId = kn.HeroId,
                                ResponseType = null,
                                Reason = "hung_hop0"
                            });
                        }
                    }
                }
            }

            var map = new System.Collections.Generic.Dictionary<string, TruthKnower>(System.StringComparer.Ordinal);
            foreach (var cand in candidates)
            {
                if (!IsEligibleKnower(cand.HeroId)) continue;

                bool isPlayer = !string.IsNullOrEmpty(playerHeroId) && cand.HeroId == playerHeroId;
                cand.IsPlayer = isPlayer;
                if (isPlayer)
                {
                    cand.ResponseType = null;
                }

                if (!map.TryGetValue(cand.HeroId, out var existing))
                {
                    map[cand.HeroId] = cand;
                }
                else
                {
                    if (existing.ResponseType == null && cand.ResponseType != null)
                    {
                        existing.ResponseType = cand.ResponseType;
                        existing.Reason = cand.Reason;
                    }
                    if (cand.IsAccused)
                    {
                        existing.IsAccused = true;
                    }
                    if (cand.IsPlayer)
                    {
                        existing.IsPlayer = true;
                        existing.ResponseType = null;
                    }
                }
            }

            return map.Values.ToList();
        }

        public sealed class TruthKnower
        {
            public string HeroId { get; set; } = string.Empty;
            public string? ResponseType { get; set; }
            public string Reason { get; set; } = string.Empty;
            public bool IsAccused { get; set; }
            public bool IsPlayer { get; set; }
        }
    }
}
