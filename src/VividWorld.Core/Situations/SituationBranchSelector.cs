using System;
using System.Collections.Generic;
using System.Linq;
using VividWorld.Core.Rumors;
using VividWorld.Core.Util;

namespace VividWorld.Core.Situations
{
    public sealed class SituationTraitTerm
    {
        public string TraitName { get; set; } = string.Empty;
        public int TraitValue { get; set; }
        public double Coefficient { get; set; }
        public double Product => TraitValue * Coefficient;
    }

    public sealed class SituationBranchDetail
    {
        public string BranchId { get; set; } = string.Empty;
        public double Base { get; set; }
        public List<SituationTraitTerm> TraitTerms { get; set; } = new();
        public double Raw { get; set; }
        public bool IsClamped { get; set; }
        public double Weight { get; set; }
        public double Probability { get; set; }
        public double? GrudgeDepth { get; set; }
        public double? GrudgeBonus { get; set; }
        public bool IsExcluded { get; set; }
        public string? ExcludedReason { get; set; }
    }

    public sealed class SituationDecision
    {
        public IReadOnlyList<SituationBranchDetail> Branches { get; set; } = Array.Empty<SituationBranchDetail>();
        public double Roll { get; set; }
        public string? SelectedBranchId { get; set; }
        public SituationBranchDef? SelectedBranch { get; set; }
    }

    public static class SituationBranchSelector
    {
        public static SituationDecision Select(
            SituationTemplate template,
            IReadOnlyDictionary<string, SituationRoleFacts>? factsByRole,
            IReadOnlyDictionary<string, string?>? boundHeroes,
            IReadOnlyDictionary<string, string>? unboundReasons,
            TraitProfile deciderTraits,
            double defaultMinBranchWeight,
            IDeterministicRng rng,
            long seed,
            SituationWorldContext? context = null,
            string? situationId = null,
            double day = 0.0,
            ISituationHistory? history = null,
            IReadOnlyCollection<string>? nonDerivedHeroIds = null,
            bool forced = false)
        {
            if (template == null) throw new ArgumentNullException(nameof(template));
            if (deciderTraits == null) throw new ArgumentNullException(nameof(deciderTraits));
            if (rng == null) throw new ArgumentNullException(nameof(rng));

            factsByRole ??= new Dictionary<string, SituationRoleFacts>();
            double minWeight = template.MinBranchWeight ?? defaultMinBranchWeight;
            situationId ??= template.Id;

            var branchDetails = new List<SituationBranchDetail>();

            foreach (var b in template.Branches)
            {
                var detail = new SituationBranchDetail
                {
                    BranchId = b.Id,
                    Base = b.Base
                };

                // 1. Evaluate preconditions
                var precondResults = SituationConditionEvaluator.EvaluateAll(
                    b.Preconditions,
                    factsByRole,
                    boundHeroes,
                    unboundReasons,
                    situationId,
                    day,
                    history,
                    nonDerivedHeroIds,
                    context,
                    forced);

                var failedPreconds = precondResults.Where(r => !r.Ok).ToList();
                if (failedPreconds.Count > 0)
                {
                    detail.IsExcluded = true;
                    detail.ExcludedReason = string.Join("; ", failedPreconds.Select(p => p.Detail));
                    branchDetails.Add(detail);
                    continue;
                }

                // 2. Compute raw weight
                double sumTraits = 0.0;
                if (b.Traits != null)
                {
                    foreach (var kvp in b.Traits)
                    {
                        int tVal = GetTraitValue(deciderTraits, kvp.Key);
                        double prod = tVal * kvp.Value;
                        sumTraits += prod;
                        detail.TraitTerms.Add(new SituationTraitTerm
                        {
                            TraitName = kvp.Key,
                            TraitValue = tVal,
                            Coefficient = kvp.Value
                        });
                    }
                }

                double grudgeBonus = 0.0;
                if (b.GrudgeWeight != null)
                {
                    string fromRole = b.GrudgeWeight.From;
                    string towardRole = b.GrudgeWeight.Toward;
                    string? fromHeroId = boundHeroes != null && boundHeroes.TryGetValue(fromRole, out var fh) ? fh : null;
                    string? towardHeroId = boundHeroes != null && boundHeroes.TryGetValue(towardRole, out var th) ? th : null;

                    if (!string.IsNullOrEmpty(fromHeroId) && !string.IsNullOrEmpty(towardHeroId))
                    {
                        double personalGrudge = context?.GrudgeSum != null ? -context.GrudgeSum(fromHeroId!, towardHeroId!) : 0.0;
                        double gameRelation = context?.Affection != null ? (-(context.Affection(fromHeroId!, towardHeroId!) ?? 0) / 4.0) : 0.0;
                        double depth = Math.Max(0.0, Math.Max(personalGrudge, gameRelation));
                        grudgeBonus = Math.Min(b.GrudgeWeight.Max, b.GrudgeWeight.PerPoint * depth);
                        detail.GrudgeDepth = depth;
                        detail.GrudgeBonus = grudgeBonus;
                    }
                }

                double raw = b.Base + sumTraits + grudgeBonus;
                detail.Raw = raw;

                double weight = Math.Max(raw, minWeight);
                detail.Weight = weight;
                detail.IsClamped = raw < minWeight;

                branchDetails.Add(detail);
            }

            var available = branchDetails.Where(b => !b.IsExcluded).ToList();
            if (forced)
            {
                var branchDefsById = template.Branches.ToDictionary(b => b.Id, b => b);
                bool hasActionBranch = available.Any(a => branchDefsById.TryGetValue(a.BranchId, out var def) && (def.Events.Count > 0 || def.MadeUpTalk != null));
                if (hasActionBranch)
                {
                    foreach (var a in available)
                    {
                        if (branchDefsById.TryGetValue(a.BranchId, out var def) && (def.Events.Count > 0 || def.MadeUpTalk != null))
                            continue;
                        // 標成排除，日誌的 pick 行才不會把它列成 0% 的候選
                        a.IsExcluded = true;
                        a.ExcludedReason = "forced: only branches that produce something";
                    }
                    available = available.Where(a => !a.IsExcluded).ToList();
                }
            }
            if (available.Count == 0)
            {
                return new SituationDecision
                {
                    Branches = branchDetails,
                    Roll = 0.0,
                    SelectedBranchId = null,
                    SelectedBranch = null
                };
            }

            double totalWeight = available.Sum(b => b.Weight);
            foreach (var b in available)
            {
                b.Probability = totalWeight > 0.0 ? (b.Weight / totalWeight) * 100.0 : 0.0;
            }

            double roll = rng.NextDouble(seed);
            var weightsList = available.Select(b => b.Weight).ToList();
            int pickedIndex = rng.PickWeighted(weightsList, seed);
            var pickedBranchDetail = available[pickedIndex];
            var pickedBranchDef = template.Branches.FirstOrDefault(b => b.Id == pickedBranchDetail.BranchId);

            return new SituationDecision
            {
                Branches = branchDetails,
                Roll = roll,
                SelectedBranchId = pickedBranchDetail.BranchId,
                SelectedBranch = pickedBranchDef
            };
        }

        private static int GetTraitValue(TraitProfile profile, string traitName)
        {
            return traitName.ToLowerInvariant() switch
            {
                "honor" => profile.Honor,
                "mercy" => profile.Mercy,
                "valor" => profile.Valor,
                "calculating" => profile.Calculating,
                "generosity" => profile.Generosity,
                _ => 0
            };
        }
    }
}
