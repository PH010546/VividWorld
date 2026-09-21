using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace VividWorld.Core.Situations
{
    public static class SituationLogFormatter
    {
        public static List<string> FormatExecution(
            string templateId,
            long instanceId,
            IEnumerable<KeyValuePair<string, string?>> roles,
            string settlementId,
            IEnumerable<ConditionEvaluationResult> conditionResults,
            SituationDecision decision)
        {
            var lines = new List<string>();
            var culture = CultureInfo.InvariantCulture;

            // 1. Header line
            string rolesStr = string.Join(", ", (roles ?? Enumerable.Empty<KeyValuePair<string, string?>>())
                .Select(kv => $"{kv.Key}={kv.Value ?? "none"}"));
            lines.Add(string.Format(culture, "Situation {0}: instance {1} roles {2} at {3}",
                templateId, instanceId, rolesStr, settlementId));

            // 2. Conditions line
            var condDetails = (conditionResults ?? Enumerable.Empty<ConditionEvaluationResult>())
                .Select(r => r.Detail);
            lines.Add(string.Format(culture, "  conditions: {0}", string.Join("; ", condDetails)));

            // 3. Branches
            if (decision?.Branches != null)
            {
                foreach (var b in decision.Branches)
                {
                    if (b.IsExcluded)
                    {
                        lines.Add(string.Format(culture, "  branch {0}: excluded - {1}", b.BranchId, b.ExcludedReason));
                    }
                    else
                    {
                        var termsStr = string.Empty;
                        if (b.TraitTerms != null && b.TraitTerms.Count > 0)
                        {
                            var parts = b.TraitTerms.Select(t =>
                                string.Format(culture, " + {0} {1} x {2:0.00}", t.TraitName, t.TraitValue, t.Coefficient));
                            termsStr = string.Concat(parts);
                        }

                        if (b.IsClamped)
                        {
                            lines.Add(string.Format(culture, "  branch {0}: base {1:0.00}{2} = {3:0.00} -> floor {4:0.00}",
                                b.BranchId, b.Base, termsStr, b.Raw, b.Weight));
                        }
                        else
                        {
                            lines.Add(string.Format(culture, "  branch {0}: base {1:0.00}{2} = {3:0.00}",
                                b.BranchId, b.Base, termsStr, b.Raw));
                        }
                    }
                }
            }

            // 4. Pick line
            var available = decision?.Branches?.Where(b => !b.IsExcluded).ToList() ?? new List<SituationBranchDetail>();
            if (available.Count == 0)
            {
                lines.Add("  pick: no branch available");
            }
            else
            {
                var pickParts = available.Select(b =>
                    string.Format(culture, "{0} {1:0.00} ({2:0.0}%)", b.BranchId, b.Weight, b.Probability));
                lines.Add(string.Format(culture, "  pick: {0}; roll {1:0.0000} -> {2}",
                    string.Join(", ", pickParts), decision!.Roll, decision.SelectedBranchId));
            }

            return lines;
        }

        public static string FormatCandidateRejected(
            string templateId,
            string role,
            string heroId,
            IEnumerable<ConditionEvaluationResult> failedConditions)
        {
            var culture = CultureInfo.InvariantCulture;
            var details = (failedConditions ?? Enumerable.Empty<ConditionEvaluationResult>())
                .Select(c => c.Detail);
            return string.Format(culture, "Situation {0}: candidate {1}={2} rejected - {3}",
                templateId, role, heroId, string.Join("; ", details));
        }
    }
}
