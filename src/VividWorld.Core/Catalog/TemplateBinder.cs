using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using VividWorld.Core.Events;
using VividWorld.Core.Ingest;

namespace VividWorld.Core.Catalog
{
    public static class TemplateBinder
    {
        private static readonly Regex PlaceholderRegex = new(@"\{([A-Za-z0-9_]+)\}", RegexOptions.Compiled);

        public static EventSubmission? Bind(
            EventTemplate template,
            IReadOnlyDictionary<string, string> bindings,
            double day,
            string? linkedEventId,
            out IReadOnlyList<CatalogIssue> issues)
        {
            var issueList = new List<CatalogIssue>();

            if (template == null)
            {
                issueList.Add(new CatalogIssue
                {
                    TemplateIndex = -1,
                    TemplateType = "(unknown)",
                    Field = string.Empty,
                    Code = CatalogIssueCode.BadJson,
                    IsError = true,
                    Detail = "Template cannot be null."
                });
                issues = issueList;
                return null;
            }

            var normalizedBindings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (bindings != null)
            {
                foreach (var kvp in bindings)
                {
                    string key = kvp.Key;
                    if (key.StartsWith("{", StringComparison.Ordinal) && key.EndsWith("}", StringComparison.Ordinal) && key.Length > 2)
                    {
                        key = key.Substring(1, key.Length - 2);
                    }
                    normalizedBindings[key] = kvp.Value;
                }
            }

            // 1. Bind roles to participants
            var participants = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var roleKvp in template.Roles)
            {
                string roleName = roleKvp.Key;
                string placeholder = roleKvp.Value ?? string.Empty;
                string placeholderKey = placeholder;
                if (placeholderKey.StartsWith("{", StringComparison.Ordinal) && placeholderKey.EndsWith("}", StringComparison.Ordinal) && placeholderKey.Length > 2)
                {
                    placeholderKey = placeholderKey.Substring(1, placeholderKey.Length - 2);
                }

                if (!normalizedBindings.TryGetValue(placeholderKey, out var boundVal) || string.IsNullOrEmpty(boundVal))
                {
                    issueList.Add(new CatalogIssue
                    {
                        TemplateIndex = -1,
                        TemplateType = template.Type,
                        Field = $"roles.{roleName}",
                        Code = CatalogIssueCode.UnboundPlaceholder,
                        IsError = true,
                        Detail = $"Role '{roleName}' placeholder '{{{placeholderKey}}}' is unbound."
                    });
                }
                else
                {
                    participants[roleName] = boundVal;
                }
            }

            // 2. Bind facts and vars
            var facts = new List<Fact>();
            for (int fIdx = 0; fIdx < template.Facts.Count; fIdx++)
            {
                var tf = template.Facts[fIdx];
                var resolvedVars = new Dictionary<string, string>(StringComparer.Ordinal);
                bool factDropped = false;

                if (tf.Vars != null)
                {
                    foreach (var varKvp in tf.Vars)
                    {
                        string varVal = varKvp.Value ?? string.Empty;
                        string replaced = varVal;

                        var matches = PlaceholderRegex.Matches(varVal);
                        foreach (Match match in matches)
                        {
                            string phKey = match.Groups[1].Value;
                            if (normalizedBindings.TryGetValue(phKey, out var boundVal))
                            {
                                replaced = replaced.Replace(match.Value, boundVal);
                            }
                            else
                            {
                                factDropped = true;
                                if (tf.Optional)
                                {
                                    issueList.Add(new CatalogIssue
                                    {
                                        TemplateIndex = -1,
                                        TemplateType = template.Type,
                                        Field = $"facts[{fIdx}].vars.{varKvp.Key}",
                                        Code = CatalogIssueCode.UnboundPlaceholder,
                                        IsError = false,
                                        Detail = $"Optional fact '{tf.Id}' dropped: variable '{varKvp.Key}' references unbound placeholder '{{{phKey}}}'."
                                    });
                                }
                                else
                                {
                                    issueList.Add(new CatalogIssue
                                    {
                                        TemplateIndex = -1,
                                        TemplateType = template.Type,
                                        Field = $"facts[{fIdx}].vars.{varKvp.Key}",
                                        Code = CatalogIssueCode.UnboundPlaceholder,
                                        IsError = true,
                                        Detail = $"Variable '{varKvp.Key}' references unbound placeholder '{{{phKey}}}'."
                                    });
                                }
                            }
                        }

                        if (!factDropped)
                        {
                            resolvedVars[varKvp.Key] = replaced;
                        }
                    }
                }

                if (factDropped)
                {
                    continue;
                }

                // Bind() 只拿得到**一個** linkedEventId。碎片指向的模板若不是它，
                // 沿用它就是靜默指錯事件——寧可整則不生，也不要生出一則指錯對象的傳聞。
                string? refersTo = null;
                if (!string.IsNullOrEmpty(tf.RefersToTemplateType))
                {
                    if (string.Equals(tf.RefersToTemplateType, template.LinkedTemplateType, StringComparison.OrdinalIgnoreCase))
                    {
                        refersTo = linkedEventId;
                    }
                    else
                    {
                        issueList.Add(new CatalogIssue
                        {
                            TemplateIndex = -1,
                            TemplateType = template.Type,
                            Field = $"facts[{fIdx}].refersToTemplateType",
                            Code = CatalogIssueCode.RefersToLinkMismatch,
                            IsError = true,
                            Detail = $"Fact refers to template '{tf.RefersToTemplateType}' but this submission's linked template is " +
                                     $"'{template.LinkedTemplateType ?? "(none)"}'. Bind() resolves exactly one linked event id."
                        });
                    }
                }

                var fact = new Fact
                {
                    Id = tf.Id,
                    Category = tf.Category,
                    TextId = tf.TextId ?? string.Empty,
                    Text = tf.Text,
                    Fragility = tf.Fragility,
                    Role = tf.Role,
                    RefersTo = refersTo,
                    Vars = resolvedVars
                };
                facts.Add(fact);
            }

            if (facts.Count == 0)
            {
                issueList.Add(new CatalogIssue
                {
                    TemplateIndex = -1,
                    TemplateType = template.Type,
                    Field = "facts",
                    Code = CatalogIssueCode.NoFacts,
                    IsError = true,
                    Detail = "All facts were dropped; submission must contain at least one fact."
                });
            }

            issues = issueList;

            if (issueList.Any(i => i.IsError))
            {
                return null;
            }

            var submission = new EventSubmission
            {
                Type = template.Type,
                Day = day,
                Origin = template.Origin,
                DramaWeight = template.DramaWeight,
                LinkedEventId = !string.IsNullOrEmpty(template.LinkedTemplateType) ? linkedEventId : null,
                AutoResolveWitnesses = (template.Origin == EventOrigin.Public),
                Participants = participants,
                KnowingRoles = new HashSet<string>(template.KnowingRoles, StringComparer.Ordinal),
                Facts = facts
            };

            return submission;
        }
    }
}
