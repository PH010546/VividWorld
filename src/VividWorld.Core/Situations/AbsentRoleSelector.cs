using System;
using System.Collections.Generic;
using System.Linq;
using VividWorld.Core.Util;

namespace VividWorld.Core.Situations
{
    public sealed class AbsentRoleCandidateExclusion
    {
        public string HeroId { get; set; } = string.Empty;
        public string Reason { get; set; } = string.Empty;
    }

    public sealed class AbsentRoleSelectionResult
    {
        public string DerivedRoleName { get; set; } = string.Empty;
        public string Strategy { get; set; } = string.Empty;
        public string? StrategyPrefix { get; set; }
        public string? TargetRoleName { get; set; }
        public string? TargetHeroId { get; set; }

        public SituationRoleFacts? PickedHeroFacts { get; set; }
        public string? PickedHeroId => PickedHeroFacts?.HeroId;

        public int EligibleCandidateCount { get; set; }
        public List<SituationRoleFacts> EligibleCandidates { get; set; } = new();
        public List<AbsentRoleCandidateExclusion> Exclusions { get; set; } = new();
        public string? UnboundReason { get; set; }

        public bool IsBound => PickedHeroFacts != null;
    }

    public static class AbsentRoleSelector
    {
        public static readonly HashSet<string> SupportedPrefixes = new(StringComparer.OrdinalIgnoreCase)
        {
            "grudgeTargetOf",
            "rivalClanLeaderOf",
            "selfOrKinOf",
            "rulerOf",
            "grudgeHolderAgainst",
            "kinOf"
        };

        public static (string StrategyPrefix, string TargetRole)? ParseStrategy(string? derived)
        {
            if (string.IsNullOrWhiteSpace(derived)) return null;
            int colonIdx = derived!.IndexOf(':');
            if (colonIdx <= 0 || colonIdx >= derived.Length - 1) return null;
            string prefix = derived.Substring(0, colonIdx).Trim();
            string targetRole = derived.Substring(colonIdx + 1).Trim();
            if (!SupportedPrefixes.Contains(prefix) || string.IsNullOrEmpty(targetRole))
            {
                return null;
            }
            return (prefix, targetRole);
        }

        public static AbsentRoleSelectionResult Select(
            string derivedRoleName,
            string derivedStrategy,
            SituationRoleFacts? targetRoleFacts,
            string? currentSettlementId,
            IReadOnlyCollection<SituationRoleFacts> candidatePool,
            IReadOnlyDictionary<string, string?>? alreadyBoundHeroes,
            SituationWorldContext? context,
            string? situationId = null,
            double day = 0.0,
            SituationRoleDef? roleDef = null)
        {
            var parsed = ParseStrategy(derivedStrategy);
            var result = new AbsentRoleSelectionResult
            {
                DerivedRoleName = derivedRoleName,
                Strategy = derivedStrategy ?? string.Empty,
                StrategyPrefix = parsed?.StrategyPrefix,
                TargetRoleName = parsed?.TargetRole,
                TargetHeroId = targetRoleFacts?.HeroId
            };

            if (parsed == null)
            {
                result.UnboundReason = $"unknown or invalid derived strategy '{derivedStrategy}'";
                return result;
            }

            string prefix = parsed.Value.StrategyPrefix.ToLowerInvariant();
            string targetRoleName = parsed.Value.TargetRole;

            if (targetRoleFacts == null || string.IsNullOrEmpty(targetRoleFacts.HeroId))
            {
                result.UnboundReason = $"target role '{targetRoleName}' unbound";
                return result;
            }

            string targetHeroId = targetRoleFacts.HeroId;
            var boundHeroIdSet = new HashSet<string>(
                (alreadyBoundHeroes?.Values ?? Enumerable.Empty<string?>())
                .Where(h => !string.IsNullOrEmpty(h))!,
                StringComparer.Ordinal);

            var eligible = new List<SituationRoleFacts>();

            switch (prefix)
            {
                case "grudgetargetof":
                    SelectGrudgeTarget(
                        targetRoleFacts,
                        currentSettlementId,
                        candidatePool,
                        boundHeroIdSet,
                        context,
                        eligible,
                        result.Exclusions);
                    break;

                case "grudgeholderagainst":
                    SelectGrudgeHolder(
                        targetRoleFacts,
                        candidatePool,
                        boundHeroIdSet,
                        context,
                        roleDef,
                        eligible,
                        result.Exclusions);
                    break;

                case "rivalclanleaderof":
                    if (!targetRoleFacts.IsClanLeader)
                    {
                        result.UnboundReason = $"target role '{targetRoleName}' is not a clan leader";
                        return result;
                    }
                    if (string.IsNullOrEmpty(targetRoleFacts.KingdomId))
                    {
                        result.UnboundReason = $"target role '{targetRoleName}' has no kingdom";
                        return result;
                    }
                    if (!targetRoleFacts.ClanTier.HasValue)
                    {
                        result.UnboundReason = $"target role '{targetRoleName}' has no clan tier";
                        return result;
                    }

                    SelectRivalClanLeader(
                        targetRoleFacts,
                        currentSettlementId,
                        candidatePool,
                        boundHeroIdSet,
                        context,
                        eligible,
                        result.Exclusions);
                    break;

                case "selforkinof":
                    if (string.IsNullOrEmpty(targetRoleFacts.ClanId))
                    {
                        result.UnboundReason = $"target role '{targetRoleName}' has no clan";
                        return result;
                    }

                    SelectSelfOrKin(
                        targetRoleFacts,
                        candidatePool,
                        boundHeroIdSet,
                        context,
                        eligible,
                        result.Exclusions);
                    break;

                case "rulerof":
                    if (string.IsNullOrEmpty(targetRoleFacts.KingdomId))
                    {
                        result.UnboundReason = $"target role '{targetRoleName}' has no kingdom";
                        return result;
                    }
                    if (targetRoleFacts.IsKingdomLeader)
                    {
                        result.UnboundReason = $"target role '{targetRoleName}' is already kingdom leader";
                        return result;
                    }

                    SelectRuler(
                        targetRoleFacts,
                        candidatePool,
                        boundHeroIdSet,
                        context,
                        eligible,
                        result.Exclusions);
                    break;

                case "kinof":
                    if (string.IsNullOrEmpty(targetRoleFacts.ClanId))
                    {
                        result.UnboundReason = $"target role '{targetRoleName}' has no clan";
                        return result;
                    }

                    SelectKin(
                        targetRoleFacts,
                        currentSettlementId,
                        candidatePool,
                        boundHeroIdSet,
                        context,
                        eligible,
                        result.Exclusions);
                    break;

                default:
                    result.UnboundReason = $"unsupported derived strategy prefix '{prefix}'";
                    return result;
            }

            result.EligibleCandidateCount = eligible.Count;
            result.EligibleCandidates = eligible;
            if (eligible.Count == 0)
            {
                result.UnboundReason = "no eligible candidates";
                return result;
            }

            if (eligible.Count == 1)
            {
                result.PickedHeroFacts = eligible[0];
                return result;
            }

            // Deterministic pick when multiple candidates
            var sorted = eligible.OrderBy(c => c.HeroId, StringComparer.Ordinal).ToList();
            long campaignSeed = context?.Seed ?? 0;
            long seed = SituationSeed.ComputeDerivedRoleSeed(campaignSeed, situationId ?? string.Empty, day, derivedRoleName, targetHeroId);
            var rng = context?.Rng ?? new SplitMix64Rng();
            int pickedIdx = rng.Pick(sorted.Count, seed);
            result.PickedHeroFacts = sorted[pickedIdx];
            return result;
        }

        private static void SelectGrudgeTarget(
            SituationRoleFacts target,
            string? settlementId,
            IReadOnlyCollection<SituationRoleFacts> pool,
            HashSet<string> boundHeroIds,
            SituationWorldContext? context,
            List<SituationRoleFacts> eligible,
            List<AbsentRoleCandidateExclusion> exclusions)
        {
            int grudgeLine = context?.GrudgeLine ?? -5;
            int nativeGrudgeLine = context?.NativeGrudgeLine ?? -20;
            string? playerId = context?.PlayerHeroId;

            foreach (var c in pool)
            {
                if (c == null || string.IsNullOrEmpty(c.HeroId)) continue;

                if (c.HeroId == target.HeroId)
                {
                    exclusions.Add(new AbsentRoleCandidateExclusion { HeroId = c.HeroId, Reason = "self" });
                    continue;
                }
                if (c.IsPlayer || (!string.IsNullOrEmpty(playerId) && c.HeroId == playerId))
                {
                    exclusions.Add(new AbsentRoleCandidateExclusion { HeroId = c.HeroId, Reason = "player" });
                    continue;
                }
                if (!c.IsAlive)
                {
                    exclusions.Add(new AbsentRoleCandidateExclusion { HeroId = c.HeroId, Reason = "dead" });
                    continue;
                }
                if (!c.IsLord)
                {
                    exclusions.Add(new AbsentRoleCandidateExclusion { HeroId = c.HeroId, Reason = "not a lord" });
                    continue;
                }
                if (!string.IsNullOrEmpty(settlementId) && !string.IsNullOrEmpty(c.SettlementId) &&
                    string.Equals(c.SettlementId, settlementId, StringComparison.OrdinalIgnoreCase))
                {
                    exclusions.Add(new AbsentRoleCandidateExclusion { HeroId = c.HeroId, Reason = "present in settlement" });
                    continue;
                }
                if (boundHeroIds.Contains(c.HeroId))
                {
                    exclusions.Add(new AbsentRoleCandidateExclusion { HeroId = c.HeroId, Reason = "already bound" });
                    continue;
                }

                int grudgeSum = context?.GrudgeSum != null ? context.GrudgeSum(target.HeroId, c.HeroId) : 0;
                int? affection = context?.Affection != null ? context.Affection(target.HeroId, c.HeroId) : null;

                bool meetsGrudge = (context?.GrudgeSum != null && grudgeSum <= grudgeLine) ||
                                   (affection.HasValue && affection.Value <= nativeGrudgeLine);

                if (!meetsGrudge)
                {
                    exclusions.Add(new AbsentRoleCandidateExclusion { HeroId = c.HeroId, Reason = "grudge not deep enough" });
                    continue;
                }

                eligible.Add(c);
            }
        }

        private static void SelectGrudgeHolder(
            SituationRoleFacts target,
            IReadOnlyCollection<SituationRoleFacts> pool,
            HashSet<string> boundHeroIds,
            SituationWorldContext? context,
            SituationRoleDef? roleDef,
            List<SituationRoleFacts> eligible,
            List<AbsentRoleCandidateExclusion> exclusions)
        {
            int grudgeLine = roleDef?.GetGrudgeLine(context?.Config, context) ?? context?.GrudgeLine ?? -5;
            int nativeGrudgeLine = roleDef?.GetNativeGrudgeLine(context?.Config, context) ?? context?.NativeGrudgeLine ?? -20;
            string? playerId = context?.PlayerHeroId;

            foreach (var c in pool)
            {
                if (c == null || string.IsNullOrEmpty(c.HeroId)) continue;

                if (c.HeroId == target.HeroId)
                {
                    exclusions.Add(new AbsentRoleCandidateExclusion { HeroId = c.HeroId, Reason = "self" });
                    continue;
                }
                if (c.IsPlayer || (!string.IsNullOrEmpty(playerId) && c.HeroId == playerId))
                {
                    exclusions.Add(new AbsentRoleCandidateExclusion { HeroId = c.HeroId, Reason = "player" });
                    continue;
                }
                if (!c.IsAlive)
                {
                    exclusions.Add(new AbsentRoleCandidateExclusion { HeroId = c.HeroId, Reason = "dead" });
                    continue;
                }
                if (!c.IsLord)
                {
                    exclusions.Add(new AbsentRoleCandidateExclusion { HeroId = c.HeroId, Reason = "not a lord" });
                    continue;
                }
                if (boundHeroIds.Contains(c.HeroId))
                {
                    exclusions.Add(new AbsentRoleCandidateExclusion { HeroId = c.HeroId, Reason = "already bound" });
                    continue;
                }

                int grudgeSum = context?.GrudgeSum != null ? context.GrudgeSum(c.HeroId, target.HeroId) : 0;
                int? affection = context?.Affection != null ? context.Affection(c.HeroId, target.HeroId) : null;

                bool meetsGrudge = (context?.GrudgeSum != null && grudgeSum <= grudgeLine) ||
                                   (affection.HasValue && affection.Value <= nativeGrudgeLine);

                if (!meetsGrudge)
                {
                    exclusions.Add(new AbsentRoleCandidateExclusion { HeroId = c.HeroId, Reason = "grudge not deep enough" });
                    continue;
                }

                if (roleDef?.AnyTraitAtMost != null && roleDef.AnyTraitAtMost.Count > 0)
                {
                    var profile = context?.Traits?.Of(c.HeroId);
                    bool traitMatch = false;
                    foreach (var kvp in roleDef.AnyTraitAtMost)
                    {
                        int actual = profile != null ? SituationConditionEvaluator.GetTraitValue(profile, kvp.Key) : 0;
                        if (actual <= kvp.Value)
                        {
                            traitMatch = true;
                            break;
                        }
                    }

                    if (!traitMatch)
                    {
                        exclusions.Add(new AbsentRoleCandidateExclusion { HeroId = c.HeroId, Reason = "personality mismatch" });
                        continue;
                    }
                }

                eligible.Add(c);
            }
        }

        private static void SelectRivalClanLeader(
            SituationRoleFacts target,
            string? settlementId,
            IReadOnlyCollection<SituationRoleFacts> pool,
            HashSet<string> boundHeroIds,
            SituationWorldContext? context,
            List<SituationRoleFacts> eligible,
            List<AbsentRoleCandidateExclusion> exclusions)
        {
            string? playerId = context?.PlayerHeroId;

            foreach (var c in pool)
            {
                if (c == null || string.IsNullOrEmpty(c.HeroId)) continue;

                if (c.HeroId == target.HeroId)
                {
                    exclusions.Add(new AbsentRoleCandidateExclusion { HeroId = c.HeroId, Reason = "self" });
                    continue;
                }
                if (c.IsPlayer || (!string.IsNullOrEmpty(playerId) && c.HeroId == playerId))
                {
                    exclusions.Add(new AbsentRoleCandidateExclusion { HeroId = c.HeroId, Reason = "player" });
                    continue;
                }
                if (!c.IsAlive)
                {
                    exclusions.Add(new AbsentRoleCandidateExclusion { HeroId = c.HeroId, Reason = "dead" });
                    continue;
                }
                if (!c.IsLord)
                {
                    exclusions.Add(new AbsentRoleCandidateExclusion { HeroId = c.HeroId, Reason = "not a lord" });
                    continue;
                }
                if (!string.IsNullOrEmpty(settlementId) && !string.IsNullOrEmpty(c.SettlementId) &&
                    string.Equals(c.SettlementId, settlementId, StringComparison.OrdinalIgnoreCase))
                {
                    exclusions.Add(new AbsentRoleCandidateExclusion { HeroId = c.HeroId, Reason = "present in settlement" });
                    continue;
                }
                if (boundHeroIds.Contains(c.HeroId))
                {
                    exclusions.Add(new AbsentRoleCandidateExclusion { HeroId = c.HeroId, Reason = "already bound" });
                    continue;
                }
                if (!c.IsClanLeader)
                {
                    exclusions.Add(new AbsentRoleCandidateExclusion { HeroId = c.HeroId, Reason = "not a clan leader" });
                    continue;
                }
                if (string.IsNullOrEmpty(c.KingdomId) || !string.Equals(c.KingdomId, target.KingdomId, StringComparison.OrdinalIgnoreCase))
                {
                    exclusions.Add(new AbsentRoleCandidateExclusion { HeroId = c.HeroId, Reason = "different kingdom" });
                    continue;
                }
                if (string.IsNullOrEmpty(c.ClanId) || string.Equals(c.ClanId, target.ClanId, StringComparison.OrdinalIgnoreCase))
                {
                    exclusions.Add(new AbsentRoleCandidateExclusion { HeroId = c.HeroId, Reason = "same clan" });
                    continue;
                }
                if (!c.ClanTier.HasValue || Math.Abs(c.ClanTier.Value - target.ClanTier!.Value) > 1)
                {
                    exclusions.Add(new AbsentRoleCandidateExclusion { HeroId = c.HeroId, Reason = "clan tier difference too large" });
                    continue;
                }

                eligible.Add(c);
            }
        }

        private static void SelectSelfOrKin(
            SituationRoleFacts target,
            IReadOnlyCollection<SituationRoleFacts> pool,
            HashSet<string> boundHeroIds,
            SituationWorldContext? context,
            List<SituationRoleFacts> eligible,
            List<AbsentRoleCandidateExclusion> exclusions)
        {
            string? playerId = context?.PlayerHeroId;

            foreach (var c in pool)
            {
                if (c == null || string.IsNullOrEmpty(c.HeroId)) continue;

                if (c.IsPlayer || (!string.IsNullOrEmpty(playerId) && c.HeroId == playerId))
                {
                    exclusions.Add(new AbsentRoleCandidateExclusion { HeroId = c.HeroId, Reason = "player" });
                    continue;
                }
                if (!c.IsAlive)
                {
                    exclusions.Add(new AbsentRoleCandidateExclusion { HeroId = c.HeroId, Reason = "dead" });
                    continue;
                }
                if (!c.IsLord)
                {
                    exclusions.Add(new AbsentRoleCandidateExclusion { HeroId = c.HeroId, Reason = "not a lord" });
                    continue;
                }

                // If candidate is someone else, cannot be already bound to another role
                if (c.HeroId != target.HeroId && boundHeroIds.Contains(c.HeroId))
                {
                    exclusions.Add(new AbsentRoleCandidateExclusion { HeroId = c.HeroId, Reason = "already bound" });
                    continue;
                }

                if (string.IsNullOrEmpty(c.ClanId) || !string.Equals(c.ClanId, target.ClanId, StringComparison.OrdinalIgnoreCase))
                {
                    exclusions.Add(new AbsentRoleCandidateExclusion { HeroId = c.HeroId, Reason = "different clan" });
                    continue;
                }

                eligible.Add(c);
            }
        }

        private static void SelectRuler(
            SituationRoleFacts target,
            IReadOnlyCollection<SituationRoleFacts> pool,
            HashSet<string> boundHeroIds,
            SituationWorldContext? context,
            List<SituationRoleFacts> eligible,
            List<AbsentRoleCandidateExclusion> exclusions)
        {
            string? playerId = context?.PlayerHeroId;

            foreach (var c in pool)
            {
                if (c == null || string.IsNullOrEmpty(c.HeroId)) continue;

                if (string.IsNullOrEmpty(c.KingdomId) || !string.Equals(c.KingdomId, target.KingdomId, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (!c.IsKingdomLeader)
                {
                    continue;
                }

                // Found the ruler of the kingdom
                if (c.IsPlayer || (!string.IsNullOrEmpty(playerId) && c.HeroId == playerId))
                {
                    exclusions.Add(new AbsentRoleCandidateExclusion { HeroId = c.HeroId, Reason = "ruler is the player" });
                    continue;
                }
                if (!c.IsAlive)
                {
                    exclusions.Add(new AbsentRoleCandidateExclusion { HeroId = c.HeroId, Reason = "ruler is dead" });
                    continue;
                }
                if (!c.IsLord)
                {
                    exclusions.Add(new AbsentRoleCandidateExclusion { HeroId = c.HeroId, Reason = "ruler is not a lord" });
                    continue;
                }
                if (boundHeroIds.Contains(c.HeroId))
                {
                    exclusions.Add(new AbsentRoleCandidateExclusion { HeroId = c.HeroId, Reason = "already bound" });
                    continue;
                }

                eligible.Add(c);
            }
        }

        private static void SelectKin(
            SituationRoleFacts target,
            string? currentSettlementId,
            IReadOnlyCollection<SituationRoleFacts> pool,
            HashSet<string> boundHeroIds,
            SituationWorldContext? context,
            List<SituationRoleFacts> eligible,
            List<AbsentRoleCandidateExclusion> exclusions)
        {
            string? playerId = context?.PlayerHeroId;

            foreach (var c in pool)
            {
                if (c == null || string.IsNullOrEmpty(c.HeroId)) continue;

                if (c.IsPlayer || (!string.IsNullOrEmpty(playerId) && c.HeroId == playerId))
                {
                    exclusions.Add(new AbsentRoleCandidateExclusion { HeroId = c.HeroId, Reason = "player" });
                    continue;
                }
                if (c.HeroId == target.HeroId)
                {
                    exclusions.Add(new AbsentRoleCandidateExclusion { HeroId = c.HeroId, Reason = "self" });
                    continue;
                }
                if (!c.IsAlive)
                {
                    exclusions.Add(new AbsentRoleCandidateExclusion { HeroId = c.HeroId, Reason = "dead" });
                    continue;
                }
                if (!c.IsLord)
                {
                    exclusions.Add(new AbsentRoleCandidateExclusion { HeroId = c.HeroId, Reason = "not a lord" });
                    continue;
                }
                if (boundHeroIds.Contains(c.HeroId))
                {
                    exclusions.Add(new AbsentRoleCandidateExclusion { HeroId = c.HeroId, Reason = "already bound" });
                    continue;
                }
                if (string.IsNullOrEmpty(c.ClanId) || !string.Equals(c.ClanId, target.ClanId, StringComparison.OrdinalIgnoreCase))
                {
                    exclusions.Add(new AbsentRoleCandidateExclusion { HeroId = c.HeroId, Reason = "different clan" });
                    continue;
                }
                if (!string.IsNullOrEmpty(currentSettlementId) && !string.IsNullOrEmpty(c.SettlementId) &&
                    string.Equals(c.SettlementId, currentSettlementId, StringComparison.OrdinalIgnoreCase))
                {
                    exclusions.Add(new AbsentRoleCandidateExclusion { HeroId = c.HeroId, Reason = "present in settlement" });
                    continue;
                }

                eligible.Add(c);
            }
        }

        public static string FormatLog(AbsentRoleSelectionResult result, int maxExclusions = 5)
        {
            if (result == null) return string.Empty;

            string exclStr = string.Empty;
            if (result.Exclusions != null && result.Exclusions.Count > 0)
            {
                var sample = result.Exclusions.Take(maxExclusions)
                    .Select(e => $"{e.HeroId}: {e.Reason}");
                exclStr = $" (excluded: {string.Join(", ", sample)})";
            }

            if (result.IsBound)
            {
                return $"derived {result.DerivedRoleName} = {result.Strategy}: {result.EligibleCandidateCount} candidate(s), picked {result.PickedHeroId}{exclStr}";
            }

            return $"derived {result.DerivedRoleName} = {result.Strategy}: unbound ({result.UnboundReason ?? "no candidates"}){exclStr}";
        }
    }
}
