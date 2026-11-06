using System;
using System.Collections.Generic;
using System.Linq;
using VividWorld.Core.Config;
using VividWorld.Core.Events;
using VividWorld.Core.Util;

namespace VividWorld.Core.Rumors
{
    public enum SecretSpreadAction
    {
        NotRolled,
        Miss,
        Confided,
        Leaked
    }

    public enum SecretCandidateExcludeReason
    {
        AlreadyKnows,
        Harmed,
        BelowTrustLine
    }

    public sealed class SecretCandidateExclusion
    {
        public string HeroId { get; set; } = string.Empty;
        public SecretCandidateExcludeReason Reason { get; set; }
        public int Relation { get; set; }
        public bool IsKin { get; set; }

        public SecretCandidateExclusion() { }

        public SecretCandidateExclusion(string heroId, SecretCandidateExcludeReason reason, int relation, bool isKin)
        {
            HeroId = heroId;
            Reason = reason;
            Relation = relation;
            IsKin = isKin;
        }
    }

    public sealed class SecretRollBreakdown
    {
        public double Probability { get; set; }
        public double BaseChance { get; set; }
        public double Decay { get; set; }
        public double WeightDiscount { get; set; }
        public double RelationMultiplier { get; set; }
        public double TraitMultiplier { get; set; }
        public bool Hit { get; set; }
    }

    public sealed class SecretKnowerResult
    {
        public string KnowerHeroId { get; set; } = string.Empty;
        public SecretSpreadAction Action { get; set; }
        public string? TargetHeroId { get; set; }
        public string? UnrolledReason { get; set; }
        public string? ConfideUnrolledReason { get; set; }
        public string? LeakUnrolledReason { get; set; }
        public SecretRollBreakdown? ConfideRoll { get; set; }
        public SecretRollBreakdown? LeakRoll { get; set; }
        public List<SecretCandidateExclusion> ExcludedCandidates { get; set; } = new();
        public string? ChosenTargetId { get; set; }
    }

    public sealed class SecretCoLocatedContact
    {
        public string HeroId { get; set; } = string.Empty;
        public bool IsPlayer { get; set; }
        public bool IsLord { get; set; }
        public int Relation { get; set; }
        public bool IsKin { get; set; }

        public SecretCoLocatedContact() { }

        public SecretCoLocatedContact(string heroId, bool isPlayer, bool isLord, int relation, bool isKin)
        {
            HeroId = heroId;
            IsPlayer = isPlayer;
            IsLord = isLord;
            Relation = relation;
            IsKin = isKin;
        }
    }

    public sealed class SecretKnowerContext
    {
        public string HeroId { get; set; } = string.Empty;
        public TraitProfile? Traits { get; set; }
        public IReadOnlyList<SecretCoLocatedContact> CoLocatedContacts { get; set; } = Array.Empty<SecretCoLocatedContact>();
        public IReadOnlyDictionary<string, int>? RelationToOwners { get; set; }
        public IReadOnlyDictionary<string, bool>? IsKinOfOwners { get; set; }
    }

    public sealed class SecretSpreadEvaluation
    {
        public WorldEvent Event { get; set; }
        public double Day { get; set; }
        public IReadOnlyList<SecretKnowerResult> Results { get; set; }

        public SecretSpreadEvaluation(WorldEvent evt, double day, IReadOnlyList<SecretKnowerResult> results)
        {
            Event = evt;
            Day = day;
            Results = results;
        }
    }

    public static class SecretSpread
    {
        public static double ComputeWeightDiscount(int dramaWeightTen)
        {
            if (dramaWeightTen <= 3) return 1.0;
            if (dramaWeightTen <= 5) return 0.5;
            if (dramaWeightTen <= 7) return 0.2;
            return 0.05;
        }

        public static double ComputeRelationMultiplier(int relation, bool isKin)
        {
            if (relation > 50 || (isKin && relation > 30))
            {
                return 0.1;
            }
            if (relation < -50)
            {
                return 3.0;
            }
            return 1.0;
        }

        public static double ComputeIntermediaryRelationMultiplier(
            string knowerHeroId,
            IReadOnlyList<string>? secretOwnerHeroIds,
            IReadOnlyDictionary<string, int>? relationToOwners,
            IReadOnlyDictionary<string, bool>? isKinOfOwners)
        {
            if (secretOwnerHeroIds == null || secretOwnerHeroIds.Count == 0)
            {
                return 1.0;
            }

            double minMultiplier = double.MaxValue;
            for (int i = 0; i < secretOwnerHeroIds.Count; i++)
            {
                string ownerId = secretOwnerHeroIds[i];
                int rel = 0;
                if (relationToOwners != null && relationToOwners.TryGetValue(ownerId, out int r))
                {
                    rel = r;
                }

                bool isKin = false;
                if (isKinOfOwners != null && isKinOfOwners.TryGetValue(ownerId, out bool k))
                {
                    isKin = k;
                }

                double m = ComputeRelationMultiplier(rel, isKin);
                if (m < minMultiplier)
                {
                    minMultiplier = m;
                }
            }

            return minMultiplier == double.MaxValue ? 1.0 : minMultiplier;
        }

        public static double ComputeOwnerTraitMultiplier(TraitProfile? traits, LeakConfig leakConfig)
        {
            if (traits == null || leakConfig == null) return 1.0;
            var w = leakConfig.TraitWeights;
            double raw = 1.0
                + (w.Calculating * traits.Calculating)
                + (w.Generosity * traits.Generosity);
            return Math.Max(leakConfig.MultiplierMin, Math.Min(leakConfig.MultiplierMax, raw));
        }

        public static double ComputeIntermediaryTraitMultiplier(TraitProfile? traits, LeakConfig leakConfig)
        {
            if (traits == null || leakConfig == null) return 1.0;
            var w = leakConfig.TraitWeights;
            double raw = 1.0
                + (w.Honor * traits.Honor)
                + (w.Calculating * traits.Calculating)
                + (w.Generosity * traits.Generosity);
            return Math.Max(leakConfig.MultiplierMin, Math.Min(leakConfig.MultiplierMax, raw));
        }

        public static SecretSpreadEvaluation Evaluate(
            WorldEvent evt,
            double day,
            IReadOnlyList<SecretKnowerContext> knowers,
            IReadOnlyCollection<string>? harmedHeroIds,
            VividWorldConfig config,
            IDeterministicRng rng,
            long campaignSeed,
            string? playerHeroId = null)
        {
            var results = new List<SecretKnowerResult>();
            if (evt == null || knowers == null || knowers.Count == 0)
            {
                return new SecretSpreadEvaluation(evt!, day, results);
            }

            var mergedHarmed = new HashSet<string>(StringComparer.Ordinal);
            if (evt.SecretHarmedHeroIds != null)
            {
                foreach (var id in evt.SecretHarmedHeroIds)
                {
                    if (!string.IsNullOrEmpty(id)) mergedHarmed.Add(id);
                }
            }
            if (harmedHeroIds != null)
            {
                foreach (var id in harmedHeroIds)
                {
                    if (!string.IsNullOrEmpty(id)) mergedHarmed.Add(id);
                }
            }

            bool eventAlreadyLeaked = evt.State != null && evt.State.Leaked;

            // 追蹤當天已被吐露並加入的新知情者（同日避免被重複吐露）
            var newKnowersToday = new HashSet<string>(StringComparer.Ordinal);

            for (int i = 0; i < knowers.Count; i++)
            {
                var knower = knowers[i];
                var result = EvaluateKnower(
                    evt, day, knower, mergedHarmed, config, rng, campaignSeed,
                    playerHeroId, eventAlreadyLeaked, newKnowersToday);

                results.Add(result);

                if (result.Action == SecretSpreadAction.Leaked)
                {
                    eventAlreadyLeaked = true;
                }
                else if (result.Action == SecretSpreadAction.Confided && !string.IsNullOrEmpty(result.TargetHeroId))
                {
                    newKnowersToday.Add(result.TargetHeroId!);
                }
            }

            return new SecretSpreadEvaluation(evt, day, results);
        }

        public static SecretKnowerResult EvaluateKnower(
            WorldEvent evt,
            double day,
            SecretKnowerContext knower,
            IReadOnlyCollection<string>? harmedHeroIds,
            VividWorldConfig config,
            IDeterministicRng rng,
            long campaignSeed,
            string? playerHeroId = null,
            bool eventAlreadyLeaked = false,
            IReadOnlyCollection<string>? newKnowersToday = null)
        {
            var result = new SecretKnowerResult
            {
                KnowerHeroId = knower?.HeroId ?? string.Empty
            };

            if (evt == null || knower == null)
            {
                result.Action = SecretSpreadAction.NotRolled;
                result.UnrolledReason = "invalid input";
                return result;
            }

            if (!string.IsNullOrEmpty(playerHeroId) && string.Equals(knower.HeroId, playerHeroId, StringComparison.Ordinal))
            {
                result.Action = SecretSpreadAction.NotRolled;
                result.UnrolledReason = "player";
                return result;
            }

            if (eventAlreadyLeaked || (evt.State != null && evt.State.Leaked))
            {
                result.Action = SecretSpreadAction.NotRolled;
                result.UnrolledReason = "already leaked";
                result.ConfideUnrolledReason = "already leaked";
                result.LeakUnrolledReason = "already leaked";
                return result;
            }

            if (evt.SecretRetired)
            {
                result.Action = SecretSpreadAction.NotRolled;
                result.UnrolledReason = "retired";
                result.ConfideUnrolledReason = "retired";
                result.LeakUnrolledReason = "retired";
                return result;
            }

            if (day - evt.Day < config.Leak.GraceDays)
            {
                result.Action = SecretSpreadAction.NotRolled;
                result.UnrolledReason = "within grace period";
                result.ConfideUnrolledReason = "within grace period";
                result.LeakUnrolledReason = "within grace period";
                return result;
            }

            bool isOwner = evt.SecretOwnerHeroIds != null && evt.SecretOwnerHeroIds.Contains(knower.HeroId);
            double decay = Math.Pow(2.0, -(day - evt.Day) / config.Leak.ChanceDecayHalfLifeDays);

            // 1. 吐露候選人篩選
            var validCandidates = new List<SecretCoLocatedContact>();
            if (knower.CoLocatedContacts != null)
            {
                for (int ci = 0; ci < knower.CoLocatedContacts.Count; ci++)
                {
                    var contact = knower.CoLocatedContacts[ci];
                    if (contact == null || string.IsNullOrEmpty(contact.HeroId)) continue;
                    if (string.Equals(contact.HeroId, knower.HeroId, StringComparison.Ordinal)) continue;

                    // 排除已經知道的人（包含今日剛被吐露者）
                    if (evt.IsKnownBy(contact.HeroId) || (newKnowersToday != null && newKnowersToday.Contains(contact.HeroId)))
                    {
                        result.ExcludedCandidates.Add(new SecretCandidateExclusion(
                            contact.HeroId, SecretCandidateExcludeReason.AlreadyKnows, contact.Relation, contact.IsKin));
                        continue;
                    }

                    // 排除會受害的人
                    if (harmedHeroIds != null && harmedHeroIds.Contains(contact.HeroId))
                    {
                        result.ExcludedCandidates.Add(new SecretCandidateExclusion(
                            contact.HeroId, SecretCandidateExcludeReason.Harmed, contact.Relation, contact.IsKin));
                        continue;
                    }

                    // 信任門檻（嚴格大於，剛好等於線不算）
                    int trustLine = contact.IsKin ? config.Secrets.ConfideTrustKin : config.Secrets.ConfideTrustOther;
                    if (contact.Relation <= trustLine)
                    {
                        result.ExcludedCandidates.Add(new SecretCandidateExclusion(
                            contact.HeroId, SecretCandidateExcludeReason.BelowTrustLine, contact.Relation, contact.IsKin));
                        continue;
                    }

                    validCandidates.Add(contact);
                }
            }

            // 2. 吐露判定
            bool confideHit = false;
            if (validCandidates.Count == 0)
            {
                result.ConfideUnrolledReason = "no trusted person present";
            }
            else
            {
                // 身邊有多位信得過的人時，挑好感最高的那位（玩家一起比）
                var chosen = validCandidates.OrderByDescending(c => c.Relation).First();
                result.ChosenTargetId = chosen.HeroId;

                double weightDiscount;
                double relationMultiplier;
                double traitMultiplier;

                if (isOwner)
                {
                    weightDiscount = ComputeWeightDiscount(evt.DramaWeightTen);
                    relationMultiplier = 1.0;
                    traitMultiplier = ComputeOwnerTraitMultiplier(knower.Traits, config.Leak);
                }
                else
                {
                    weightDiscount = 1.0;
                    relationMultiplier = ComputeIntermediaryRelationMultiplier(
                        knower.HeroId, evt.SecretOwnerHeroIds, knower.RelationToOwners, knower.IsKinOfOwners);
                    traitMultiplier = ComputeIntermediaryTraitMultiplier(knower.Traits, config.Leak);
                }

                double pConfide = config.Secrets.ConfideChancePerDay * decay * weightDiscount * relationMultiplier * traitMultiplier;
                long confideSeed = RumorSeed.Of(campaignSeed, evt.EventId, knower.HeroId, "confide", RumorSeed.DayBucket(day));
                confideHit = rng.Chance(pConfide, confideSeed);

                result.ConfideRoll = new SecretRollBreakdown
                {
                    Probability = pConfide,
                    BaseChance = config.Secrets.ConfideChancePerDay,
                    Decay = decay,
                    WeightDiscount = weightDiscount,
                    RelationMultiplier = relationMultiplier,
                    TraitMultiplier = traitMultiplier,
                    Hit = confideHit
                };

                if (confideHit)
                {
                    result.Action = SecretSpreadAction.Confided;
                    result.TargetHeroId = chosen.HeroId;
                    return result; // 同一天吐露與走漏都中只算吐露
                }
            }

            // 3. 走漏判定（只有非當事人）
            if (isOwner)
            {
                result.LeakUnrolledReason = "owner won't leak";
                if (result.ConfideRoll != null)
                {
                    result.Action = SecretSpreadAction.Miss;
                }
                else
                {
                    result.Action = SecretSpreadAction.NotRolled;
                    result.UnrolledReason = result.ConfideUnrolledReason ?? "no trusted person present";
                }
                return result;
            }

            // 誠實的人（榮譽 ≥ +1）不走漏
            if (knower.Traits != null && knower.Traits.Honor >= 1)
            {
                result.LeakUnrolledReason = "honor ≥ 1";
                if (result.ConfideRoll != null)
                {
                    result.Action = SecretSpreadAction.Miss;
                }
                else
                {
                    result.Action = SecretSpreadAction.NotRolled;
                    result.UnrolledReason = result.ConfideUnrolledReason ?? "no trusted person present";
                }
                return result;
            }

            // 當天跟任何一位領主同在一處（自己除外）
            bool hasCoLocatedLord = knower.CoLocatedContacts != null && knower.CoLocatedContacts.Any(c => c.IsLord && !string.Equals(c.HeroId, knower.HeroId, StringComparison.Ordinal));
            if (!hasCoLocatedLord)
            {
                result.LeakUnrolledReason = "no lord co-located";
                if (result.ConfideRoll != null)
                {
                    result.Action = SecretSpreadAction.Miss;
                }
                else
                {
                    result.Action = SecretSpreadAction.NotRolled;
                    result.UnrolledReason = result.ConfideUnrolledReason ?? "no trusted person present";
                }
                return result;
            }

            double leakRelationMultiplier = ComputeIntermediaryRelationMultiplier(
                knower.HeroId, evt.SecretOwnerHeroIds, knower.RelationToOwners, knower.IsKinOfOwners);
            double leakTraitMultiplier = ComputeIntermediaryTraitMultiplier(knower.Traits, config.Leak);
            double pLeak = config.Leak.BaseChancePerInsiderPerDay * decay * leakRelationMultiplier * leakTraitMultiplier;

            long leakSeed = RumorSeed.Of(campaignSeed, evt.EventId, knower.HeroId, "leak", RumorSeed.DayBucket(day));
            bool leakHit = rng.Chance(pLeak, leakSeed);

            result.LeakRoll = new SecretRollBreakdown
            {
                Probability = pLeak,
                BaseChance = config.Leak.BaseChancePerInsiderPerDay,
                Decay = decay,
                WeightDiscount = 1.0,
                RelationMultiplier = leakRelationMultiplier,
                TraitMultiplier = leakTraitMultiplier,
                Hit = leakHit
            };

            if (leakHit)
            {
                result.Action = SecretSpreadAction.Leaked;
                return result;
            }

            result.Action = SecretSpreadAction.Miss;
            return result;
        }
    }
}
