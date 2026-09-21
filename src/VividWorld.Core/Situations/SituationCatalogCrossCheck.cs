using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using VividWorld.Core.Catalog;
using VividWorld.Core.Ingest;

namespace VividWorld.Core.Situations
{
    public static class SituationCatalogCrossCheck
    {
        private static readonly Regex PlaceholderRegex = new(@"\{([A-Za-z0-9_]+)\}", RegexOptions.Compiled);

        public static IReadOnlyList<SituationIssue> Check(
            SituationCatalog situationCatalog,
            EventCatalog situationEvents,
            EventCatalog mainEvents)
        {
            var issues = new List<SituationIssue>();

            if (situationCatalog == null) return issues;
            situationEvents ??= new EventCatalog(Array.Empty<EventTemplate>(), Array.Empty<CatalogIssue>(), 0);
            mainEvents ??= new EventCatalog(Array.Empty<EventTemplate>(), Array.Empty<CatalogIssue>(), 0);

            // 1. Check duplicate types between situationEvents and mainEvents
            foreach (var sTemplate in situationEvents.Templates)
            {
                if (mainEvents.ByType(sTemplate.Type) != null)
                {
                    issues.Add(new SituationIssue
                    {
                        SituationIndex = -1,
                        SituationId = "(catalog)",
                        Field = "type",
                        Code = SituationIssueCode.DuplicateWithMainCatalog,
                        IsError = true,
                        Detail = $"Situation event template type '{sTemplate.Type}' duplicates an event type in the main event catalog."
                    });
                }
            }

            // 2. Check situations against situationEvents
            for (int sIdx = 0; sIdx < situationCatalog.Situations.Count; sIdx++)
            {
                var situation = situationCatalog.Situations[sIdx];
                for (int bIdx = 0; bIdx < situation.Branches.Count; bIdx++)
                {
                    var branch = situation.Branches[bIdx];
                    for (int eIdx = 0; eIdx < branch.Events.Count; eIdx++)
                    {
                        var bEvent = branch.Events[eIdx];
                        var eventTemplate = situationEvents.ByType(bEvent.Type);
                        if (eventTemplate == null)
                        {
                            issues.Add(new SituationIssue
                            {
                                SituationIndex = sIdx,
                                SituationId = situation.Id,
                                Field = $"branches[{bIdx}].events[{eIdx}].type",
                                Code = SituationIssueCode.EventTemplateNotFound,
                                IsError = true,
                                Detail = $"Event template '{bEvent.Type}' not found in situation event catalog."
                            });
                            continue;
                        }

                        // Check bind values
                        foreach (var kvp in bEvent.Bind)
                        {
                            string target = kvp.Value?.Trim() ?? string.Empty;
                            if (!string.Equals(target, "@settlement", StringComparison.Ordinal) &&
                                !situation.Roles.ContainsKey(target))
                            {
                                issues.Add(new SituationIssue
                                {
                                    SituationIndex = sIdx,
                                    SituationId = situation.Id,
                                    Field = $"branches[{bIdx}].events[{eIdx}].bind.{kvp.Key}",
                                    Code = SituationIssueCode.InvalidBindTarget,
                                    IsError = true,
                                    Detail = $"Bind target '{target}' is not a defined role in situation '{situation.Id}' and not '@settlement'."
                                });
                            }
                        }

                        // Check non-optional placeholders coverage
                        var nonOptionalPlaceholders = GetNonOptionalPlaceholders(eventTemplate);
                        var normalizedBindKeys = new HashSet<string>(
                            bEvent.Bind.Keys.Select(k => k.Trim().Trim('{', '}')),
                            StringComparer.OrdinalIgnoreCase);

                        foreach (var ph in nonOptionalPlaceholders)
                        {
                            if (!normalizedBindKeys.Contains(ph))
                            {
                                issues.Add(new SituationIssue
                                {
                                    SituationIndex = sIdx,
                                    SituationId = situation.Id,
                                    Field = $"branches[{bIdx}].events[{eIdx}].bind",
                                    Code = SituationIssueCode.UnboundNonOptionalPlaceholder,
                                    IsError = true,
                                    Detail = $"Non-optional placeholder '{{{ph}}}' in template '{eventTemplate.Type}' is not bound in branch '{branch.Id}'."
                                });
                            }
                        }
                    }

                    if (branch.Grudges != null)
                    {
                        for (int gIdx = 0; gIdx < branch.Grudges.Count; gIdx++)
                        {
                            var grudge = branch.Grudges[gIdx];
                            if (string.IsNullOrEmpty(grudge.From)) continue;

                            bool isHop0Knower = false;

                            foreach (var bEvent in branch.Events)
                            {
                                var eventTemplate = situationEvents.ByType(bEvent.Type);
                                if (eventTemplate == null) continue;

                                foreach (var bindKvp in bEvent.Bind)
                                {
                                    if (string.Equals(bindKvp.Value?.Trim(), grudge.From, StringComparison.Ordinal))
                                    {
                                        string ph = bindKvp.Key.Trim().Trim('{', '}');
                                        if (eventTemplate.Roles != null)
                                        {
                                            foreach (var roleKvp in eventTemplate.Roles)
                                            {
                                                string templatePh = roleKvp.Value?.Trim().Trim('{', '}') ?? string.Empty;
                                                if (string.Equals(ph, templatePh, StringComparison.OrdinalIgnoreCase))
                                                {
                                                    string templateRole = roleKvp.Key;
                                                    if (Hop0Seeding.IsKnowingRole(eventTemplate.KnowingRoles, templateRole))
                                                    {
                                                        isHop0Knower = true;
                                                        break;
                                                    }
                                                }
                                            }
                                        }
                                    }
                                    if (isHop0Knower) break;
                                }
                                if (isHop0Knower) break;
                            }

                            if (!isHop0Knower)
                            {
                                issues.Add(new SituationIssue
                                {
                                    SituationIndex = sIdx,
                                    SituationId = situation.Id,
                                    Field = $"branches[{bIdx}].grudges[{gIdx}].from",
                                    Code = SituationIssueCode.GrudgeFromNotHop0Knower,
                                    IsError = true,
                                    Detail = $"Grudge 'from' role '{grudge.From}' is not a hop 0 knower of any event in branch '{branch.Id}'."
                                });
                            }
                        }
                    }
                }
            }

            return issues;
        }

        private static HashSet<string> GetNonOptionalPlaceholders(EventTemplate template)
        {
            var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            if (template.Roles != null)
            {
                foreach (var rVal in template.Roles.Values)
                {
                    if (!string.IsNullOrEmpty(rVal))
                    {
                        var matches = PlaceholderRegex.Matches(rVal);
                        if (matches.Count > 0)
                        {
                            foreach (Match m in matches)
                            {
                                result.Add(m.Groups[1].Value);
                            }
                        }
                        else
                        {
                            result.Add(rVal.Trim('{', '}'));
                        }
                    }
                }
            }

            if (template.Facts != null)
            {
                foreach (var fact in template.Facts)
                {
                    if (!fact.Optional)
                    {
                        if (!string.IsNullOrEmpty(fact.Text))
                        {
                            foreach (Match m in PlaceholderRegex.Matches(fact.Text))
                            {
                                result.Add(m.Groups[1].Value);
                            }
                        }
                        if (fact.Vars != null)
                        {
                            foreach (var vVal in fact.Vars.Values)
                            {
                                if (!string.IsNullOrEmpty(vVal))
                                {
                                    foreach (Match m in PlaceholderRegex.Matches(vVal))
                                    {
                                        result.Add(m.Groups[1].Value);
                                    }
                                }
                            }
                        }
                    }
                }
            }

            return result;
        }
    }
}
