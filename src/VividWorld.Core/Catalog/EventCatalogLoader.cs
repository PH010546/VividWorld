using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;
using VividWorld.Core.Config;
using VividWorld.Core.Events;
using VividWorld.Core.Ingest;

namespace VividWorld.Core.Catalog
{
    public static class EventCatalogLoader
    {
        private static readonly Regex PlaceholderRegex = new(@"\{([A-Za-z0-9_]+)\}", RegexOptions.Compiled);

        public static EventCatalog Load(string json, PersistenceConfig cfg)
        {
            return Load(json, cfg, false);
        }

        public static EventCatalog Load(string json, PersistenceConfig cfg, bool strictCatalog)
        {
            cfg ??= new PersistenceConfig();
            var allIssues = new List<CatalogIssue>();

            if (string.IsNullOrWhiteSpace(json))
            {
                allIssues.Add(new CatalogIssue
                {
                    TemplateIndex = -1,
                    TemplateType = "(unknown)",
                    Field = "json",
                    Code = CatalogIssueCode.BadJson,
                    IsError = true,
                    Detail = "Catalog JSON is empty or whitespace."
                });
                return new EventCatalog(Array.Empty<EventTemplate>(), allIssues, 0);
            }

            JToken root;
            try
            {
                root = JToken.Parse(json);
            }
            catch (Exception ex)
            {
                allIssues.Add(new CatalogIssue
                {
                    TemplateIndex = -1,
                    TemplateType = "(unknown)",
                    Field = "json",
                    Code = CatalogIssueCode.BadJson,
                    IsError = true,
                    Detail = $"Failed to parse catalog JSON: {ex.Message}"
                });
                return new EventCatalog(Array.Empty<EventTemplate>(), allIssues, 0);
            }

            JArray? templatesArray = root as JArray;
            if (templatesArray == null && root is JObject rootObj)
            {
                templatesArray = rootObj["templates"] as JArray;
            }

            if (templatesArray == null)
            {
                allIssues.Add(new CatalogIssue
                {
                    TemplateIndex = -1,
                    TemplateType = "(unknown)",
                    Field = "json",
                    Code = CatalogIssueCode.BadJson,
                    IsError = true,
                    Detail = "Catalog root must be a JSON array of templates."
                });
                return new EventCatalog(Array.Empty<EventTemplate>(), allIssues, 0);
            }

            var acceptedTemplates = new List<EventTemplate>();
            var seenTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            int skippedCount = 0;

            for (int i = 0; i < templatesArray.Count; i++)
            {
                var templateToken = templatesArray[i];
                if (templateToken is not JObject templateObj)
                {
                    allIssues.Add(new CatalogIssue
                    {
                        TemplateIndex = i,
                        TemplateType = "(unknown)",
                        Field = $"[{i}]",
                        Code = CatalogIssueCode.BadJson,
                        IsError = true,
                        Detail = "Template entry must be a JSON object."
                    });
                    skippedCount++;
                    continue;
                }

                var templateIssues = new List<CatalogIssue>();

                // 1. Type
                string templateType = "(unknown)";
                var typeProp = templateObj.Property("type", StringComparison.OrdinalIgnoreCase);
                string? rawType = typeProp?.Value?.Value<string>();

                if (string.IsNullOrWhiteSpace(rawType))
                {
                    templateIssues.Add(new CatalogIssue
                    {
                        TemplateIndex = i,
                        TemplateType = "(unknown)",
                        Field = "type",
                        Code = CatalogIssueCode.MissingType,
                        IsError = true,
                        Detail = "Template type cannot be null or whitespace."
                    });
                }
                else
                {
                    templateType = rawType!.Trim();
                    if (seenTypes.Contains(templateType))
                    {
                        templateIssues.Add(new CatalogIssue
                        {
                            TemplateIndex = i,
                            TemplateType = templateType,
                            Field = "type",
                            Code = CatalogIssueCode.DuplicateType,
                            IsError = true,
                            Detail = $"Duplicate template type '{templateType}'. Earlier definition takes precedence."
                        });
                    }
                    else
                    {
                        seenTypes.Add(templateType);
                    }
                }

                // 2. Origin
                EventOrigin? origin = null;
                var originProp = templateObj.Property("origin", StringComparison.OrdinalIgnoreCase);
                string? rawOrigin = originProp?.Value?.Value<string>();

                if (string.IsNullOrWhiteSpace(rawOrigin))
                {
                    templateIssues.Add(new CatalogIssue
                    {
                        TemplateIndex = i,
                        TemplateType = templateType,
                        Field = "origin",
                        Code = CatalogIssueCode.BadOrigin,
                        IsError = true,
                        Detail = "Event origin is missing or null. Must be 'public' or 'secret'."
                    });
                }
                else if (string.Equals(rawOrigin, "public", StringComparison.OrdinalIgnoreCase))
                {
                    origin = EventOrigin.Public;
                }
                else if (string.Equals(rawOrigin, "secret", StringComparison.OrdinalIgnoreCase))
                {
                    origin = EventOrigin.Secret;
                }
                else
                {
                    templateIssues.Add(new CatalogIssue
                    {
                        TemplateIndex = i,
                        TemplateType = templateType,
                        Field = "origin",
                        Code = CatalogIssueCode.BadOrigin,
                        IsError = true,
                        Detail = $"Invalid origin '{rawOrigin}'. Must be 'public' or 'secret'."
                    });
                }

                // 3. DramaWeight
                int? dramaWeight = null;
                var dramaProp = templateObj.Property("dramaWeight", StringComparison.OrdinalIgnoreCase);
                if (dramaProp != null && dramaProp.Value.Type != JTokenType.Null)
                {
                    try
                    {
                        int dw = dramaProp.Value.Value<int>();
                        if (!FactValidationRules.IsValidDramaWeight(dw))
                        {
                            templateIssues.Add(new CatalogIssue
                            {
                                TemplateIndex = i,
                                TemplateType = templateType,
                                Field = "dramaWeight",
                                Code = CatalogIssueCode.DramaOutOfRange,
                                IsError = true,
                                Detail = $"DramaWeight {dw} is out of range. Must be between 1 and 5."
                            });
                        }
                        else
                        {
                            dramaWeight = dw;
                        }
                    }
                    catch
                    {
                        templateIssues.Add(new CatalogIssue
                        {
                            TemplateIndex = i,
                            TemplateType = templateType,
                            Field = "dramaWeight",
                            Code = CatalogIssueCode.DramaOutOfRange,
                            IsError = true,
                            Detail = "DramaWeight must be an integer between 1 and 5."
                        });
                    }
                }

                // 4. LinkedTemplateType
                string? linkedTemplateType = null;
                var linkedProp = templateObj.Property("linkedTemplateType", StringComparison.OrdinalIgnoreCase)
                                 ?? templateObj.Property("linkedEventId", StringComparison.OrdinalIgnoreCase);
                if (linkedProp != null && linkedProp.Value.Type != JTokenType.Null)
                {
                    string rawLinked = linkedProp.Value.Value<string>() ?? string.Empty;
                    linkedTemplateType = NormalizeTemplateTypeRef(rawLinked);
                }

                // 5. Roles / Participants
                var roles = new Dictionary<string, string>(StringComparer.Ordinal);
                var rolesProp = templateObj.Property("roles", StringComparison.OrdinalIgnoreCase)
                                ?? templateObj.Property("participants", StringComparison.OrdinalIgnoreCase);
                string rolesFieldName = rolesProp?.Name ?? "roles";

                if (rolesProp == null || rolesProp.Value is not JObject rolesObj || !rolesObj.Properties().Any())
                {
                    templateIssues.Add(new CatalogIssue
                    {
                        TemplateIndex = i,
                        TemplateType = templateType,
                        Field = rolesFieldName,
                        Code = CatalogIssueCode.NoRoles,
                        IsError = true,
                        Detail = "Template must define at least one role."
                    });
                }
                else
                {
                    foreach (var prop in rolesObj.Properties())
                    {
                        roles[prop.Name] = prop.Value?.Value<string>() ?? string.Empty;
                    }
                }

                // 6. KnowingRoles
                var knowingRoles = new HashSet<string>(StringComparer.Ordinal);
                var knowingProp = templateObj.Property("knowingRoles", StringComparison.OrdinalIgnoreCase);
                if (knowingProp != null && knowingProp.Value is JArray knowingArr)
                {
                    for (int k = 0; k < knowingArr.Count; k++)
                    {
                        string roleName = knowingArr[k]?.Value<string>() ?? string.Empty;
                        if (!roles.ContainsKey(roleName))
                        {
                            templateIssues.Add(new CatalogIssue
                            {
                                TemplateIndex = i,
                                TemplateType = templateType,
                                Field = $"knowingRoles[{k}]",
                                Code = CatalogIssueCode.KnowingRoleNotARole,
                                IsError = true,
                                Detail = $"KnowingRole '{roleName}' is not defined in template roles."
                            });
                        }
                        else
                        {
                            knowingRoles.Add(roleName);
                        }
                    }
                }

                // 7. Facts
                var facts = new List<TemplateFact>();
                var factsProp = templateObj.Property("facts", StringComparison.OrdinalIgnoreCase);
                var factIds = new HashSet<string>(StringComparer.Ordinal);

                if (factsProp == null || factsProp.Value is not JArray factsArr || factsArr.Count == 0)
                {
                    templateIssues.Add(new CatalogIssue
                    {
                        TemplateIndex = i,
                        TemplateType = templateType,
                        Field = "facts",
                        Code = CatalogIssueCode.NoFacts,
                        IsError = true,
                        Detail = "Template must define at least one fact."
                    });
                }
                else
                {
                    if (factsArr.Count > cfg.MaxFactsPerEvent)
                    {
                        templateIssues.Add(new CatalogIssue
                        {
                            TemplateIndex = i,
                            TemplateType = templateType,
                            Field = "facts",
                            Code = CatalogIssueCode.TooManyFacts,
                            IsError = true,
                            Detail = $"Facts count ({factsArr.Count}) exceeds maximum allowed ({cfg.MaxFactsPerEvent})."
                        });
                    }

                    for (int f = 0; f < factsArr.Count; f++)
                    {
                        var factToken = factsArr[f];
                        if (factToken is not JObject factObj)
                        {
                            templateIssues.Add(new CatalogIssue
                            {
                                TemplateIndex = i,
                                TemplateType = templateType,
                                Field = $"facts[{f}]",
                                Code = CatalogIssueCode.BadJson,
                                IsError = true,
                                Detail = "Fact entry must be a JSON object."
                            });
                            continue;
                        }

                        // Fact ID
                        string factId = factObj.Property("id", StringComparison.OrdinalIgnoreCase)?.Value?.Value<string>() ?? string.Empty;
                        if (string.IsNullOrWhiteSpace(factId))
                        {
                            templateIssues.Add(new CatalogIssue
                            {
                                TemplateIndex = i,
                                TemplateType = templateType,
                                Field = $"facts[{f}].id",
                                Code = CatalogIssueCode.EmptyFactId,
                                IsError = true,
                                Detail = "Fact id cannot be empty."
                            });
                        }
                        else if (!factIds.Add(factId))
                        {
                            templateIssues.Add(new CatalogIssue
                            {
                                TemplateIndex = i,
                                TemplateType = templateType,
                                Field = $"facts[{f}].id",
                                Code = CatalogIssueCode.DuplicateFactId,
                                IsError = true,
                                Detail = $"Duplicate fact id '{factId}' within template."
                            });
                        }

                        // Category
                        string rawCat = factObj.Property("category", StringComparison.OrdinalIgnoreCase)?.Value?.Value<string>() ?? string.Empty;
                        FactCategory category = FactCategory.What;
                        if (!string.IsNullOrEmpty(rawCat) && Enum.TryParse<FactCategory>(rawCat, true, out var parsedCat))
                        {
                            category = parsedCat;
                        }

                        // Text
                        string factText = factObj.Property("text", StringComparison.OrdinalIgnoreCase)?.Value?.Value<string>() ?? string.Empty;
                        if (string.IsNullOrEmpty(factText))
                        {
                            templateIssues.Add(new CatalogIssue
                            {
                                TemplateIndex = i,
                                TemplateType = templateType,
                                Field = $"facts[{f}].text",
                                Code = CatalogIssueCode.EmptyFactText,
                                IsError = true,
                                Detail = "Fact text cannot be empty."
                            });
                        }

                        // TextId
                        string? factTextId = factObj.Property("textId", StringComparison.OrdinalIgnoreCase)?.Value?.Value<string>();
                        if (string.IsNullOrWhiteSpace(factTextId))
                        {
                            templateIssues.Add(new CatalogIssue
                            {
                                TemplateIndex = i,
                                TemplateType = templateType,
                                Field = $"facts[{f}].textId",
                                Code = CatalogIssueCode.MissingTextId,
                                IsError = false,
                                Detail = $"Fact '{factId}' textId is missing; falling back to Text literal."
                            });
                            factTextId = null;
                        }

                        // Fragility
                        int fragility = 3;
                        var fragProp = factObj.Property("fragility", StringComparison.OrdinalIgnoreCase);
                        if (fragProp != null && fragProp.Value.Type != JTokenType.Null)
                        {
                            fragility = fragProp.Value.Value<int>();
                            if (!FactValidationRules.IsValidFragility(fragility))
                            {
                                templateIssues.Add(new CatalogIssue
                                {
                                    TemplateIndex = i,
                                    TemplateType = templateType,
                                    Field = $"facts[{f}].fragility",
                                    Code = CatalogIssueCode.FragilityOutOfRange,
                                    IsError = true,
                                    Detail = $"Fact '{factId}' fragility {fragility} is out of range (1..5)."
                                });
                            }
                        }

                        // Vars
                        var vars = new Dictionary<string, string>(StringComparer.Ordinal);
                        var varsProp = factObj.Property("vars", StringComparison.OrdinalIgnoreCase);
                        if (varsProp != null && varsProp.Value is JObject varsObj)
                        {
                            foreach (var vProp in varsObj.Properties())
                            {
                                string varVal = vProp.Value?.Value<string>() ?? string.Empty;
                                if (!FactValidationRules.HasValidVarPrefix(varVal))
                                {
                                    templateIssues.Add(new CatalogIssue
                                    {
                                        TemplateIndex = i,
                                        TemplateType = templateType,
                                        Field = $"facts[{f}].vars.{vProp.Name}",
                                        Code = CatalogIssueCode.BadVarPrefix,
                                        IsError = true,
                                        Detail = $"Variable '{vProp.Name}' value \"{varVal}\" has no valid scheme prefix (expected hero:/settlement:/faction:/key:/num:/text:)."
                                    });
                                }
                                vars[vProp.Name] = varVal;
                            }
                        }

                        // Unknown placeholders in text
                        if (!string.IsNullOrEmpty(factText))
                        {
                            var matches = PlaceholderRegex.Matches(factText);
                            foreach (Match match in matches)
                            {
                                string placeholder = match.Groups[1].Value;
                                if (!vars.ContainsKey(placeholder))
                                {
                                    templateIssues.Add(new CatalogIssue
                                    {
                                        TemplateIndex = i,
                                        TemplateType = templateType,
                                        Field = $"facts[{f}].text",
                                        Code = CatalogIssueCode.UnknownPlaceholder,
                                        IsError = true,
                                        Detail = $"Placeholder '{{{placeholder}}}' appears in text but is not defined in vars."
                                    });
                                }
                            }
                        }

                        // RefersToTemplateType
                        string? refersToTemplate = null;
                        var refersProp = factObj.Property("refersToTemplateType", StringComparison.OrdinalIgnoreCase)
                                         ?? factObj.Property("refersTo", StringComparison.OrdinalIgnoreCase);
                        if (refersProp != null && refersProp.Value.Type != JTokenType.Null)
                        {
                            string rawRefers = refersProp.Value.Value<string>() ?? string.Empty;
                            refersToTemplate = NormalizeTemplateTypeRef(rawRefers);
                        }

                        string? role = factObj.Property("role", StringComparison.OrdinalIgnoreCase)?.Value?.Value<string>();
                        bool optional = factObj.Property("optional", StringComparison.OrdinalIgnoreCase)?.Value?.Value<bool>() ?? false;

                        facts.Add(new TemplateFact
                        {
                            Id = factId,
                            Category = category,
                            TextId = factTextId,
                            Text = factText,
                            Fragility = fragility,
                            Vars = vars,
                            RefersToTemplateType = refersToTemplate,
                            Role = role,
                            Optional = optional
                        });
                    }
                }

                // 7. Opinion (M6.5)
                List<OpinionDef>? opinions = null;
                var opinionProp = templateObj.Property("opinion", StringComparison.OrdinalIgnoreCase);
                if (opinionProp != null && opinionProp.Value.Type != JTokenType.Null)
                {
                    if (opinionProp.Value is not JArray opinionArray)
                    {
                        templateIssues.Add(new CatalogIssue
                        {
                            TemplateIndex = i,
                            TemplateType = templateType,
                            Field = "opinion",
                            Code = CatalogIssueCode.OpinionInvalid,
                            IsError = true,
                            Detail = "Template opinion must be a JSON array."
                        });
                    }
                    else
                    {
                        opinions = new List<OpinionDef>();
                        var seenAbout = new HashSet<string>(StringComparer.Ordinal);
                        for (int oIdx = 0; oIdx < opinionArray.Count; oIdx++)
                        {
                            var itemToken = opinionArray[oIdx];
                            if (itemToken is not JObject itemObj)
                            {
                                templateIssues.Add(new CatalogIssue
                                {
                                    TemplateIndex = i,
                                    TemplateType = templateType,
                                    Field = $"opinion[{oIdx}]",
                                    Code = CatalogIssueCode.OpinionInvalid,
                                    IsError = true,
                                    Detail = "Opinion entry must be a JSON object."
                                });
                                continue;
                            }

                            // Check unknown fields: only "about" and "amount" are known
                            var extraDict = new Dictionary<string, JToken>();
                            foreach (var prop in itemObj.Properties())
                            {
                                if (!string.Equals(prop.Name, "about", StringComparison.OrdinalIgnoreCase) &&
                                    !string.Equals(prop.Name, "amount", StringComparison.OrdinalIgnoreCase))
                                {
                                    extraDict[prop.Name] = prop.Value;
                                    templateIssues.Add(new CatalogIssue
                                    {
                                        TemplateIndex = i,
                                        TemplateType = templateType,
                                        Field = $"opinion[{oIdx}].{prop.Name}",
                                        Code = CatalogIssueCode.OpinionInvalid,
                                        IsError = false, // 警告，照常載入
                                        Detail = $"Unknown property '{prop.Name}' in opinion entry."
                                    });
                                }
                            }

                            var aboutProp = itemObj.Property("about", StringComparison.OrdinalIgnoreCase);
                            string? about = aboutProp?.Value?.Value<string>();
                            if (string.IsNullOrWhiteSpace(about) || !roles.ContainsKey(about!))
                            {
                                templateIssues.Add(new CatalogIssue
                                {
                                    TemplateIndex = i,
                                    TemplateType = templateType,
                                    Field = $"opinion[{oIdx}].about",
                                    Code = CatalogIssueCode.OpinionInvalid,
                                    IsError = true,
                                    Detail = $"Opinion 'about' value '{about}' is not a declared role."
                                });
                            }
                            else if (!seenAbout.Add(about!))
                            {
                                templateIssues.Add(new CatalogIssue
                                {
                                    TemplateIndex = i,
                                    TemplateType = templateType,
                                    Field = $"opinion[{oIdx}].about",
                                    Code = CatalogIssueCode.OpinionInvalid,
                                    IsError = true,
                                    Detail = $"Duplicate opinion for role '{about}'."
                                });
                            }

                            var amountProp = itemObj.Property("amount", StringComparison.OrdinalIgnoreCase);
                            double amount = 0.0;
                            bool amountValid = false;
                            if (amountProp != null && amountProp.Value.Type != JTokenType.Null)
                            {
                                if (amountProp.Value.Type == JTokenType.Float || amountProp.Value.Type == JTokenType.Integer)
                                {
                                    amount = amountProp.Value.Value<double>();
                                    if (amount != 0.0 && !double.IsNaN(amount) && !double.IsInfinity(amount))
                                    {
                                        amountValid = true;
                                    }
                                }
                                else if (amountProp.Value.Type == JTokenType.String)
                                {
                                    string s = amountProp.Value.Value<string>() ?? "";
                                    if (double.TryParse(s, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double parsed))
                                    {
                                        if (parsed != 0.0 && !double.IsNaN(parsed) && !double.IsInfinity(parsed))
                                        {
                                            amount = parsed;
                                            amountValid = true;
                                        }
                                    }
                                }
                            }

                            if (!amountValid)
                            {
                                templateIssues.Add(new CatalogIssue
                                {
                                    TemplateIndex = i,
                                    TemplateType = templateType,
                                    Field = $"opinion[{oIdx}].amount",
                                    Code = CatalogIssueCode.OpinionInvalid,
                                    IsError = true,
                                    Detail = "Opinion amount must be non-zero, non-NaN, non-Infinity."
                                });
                            }

                            opinions.Add(new OpinionDef
                            {
                                About = about ?? string.Empty,
                                Amount = amount,
                                Extra = extraDict
                            });
                        }
                    }
                }

                allIssues.AddRange(templateIssues);

                if (templateIssues.Any(issue => issue.IsError))
                {
                    skippedCount++;
                }
                else
                {
                    acceptedTemplates.Add(new EventTemplate
                    {
                        Type = templateType,
                        Origin = origin,
                        DramaWeight = dramaWeight,
                        LinkedTemplateType = linkedTemplateType,
                        Roles = roles,
                        KnowingRoles = knowingRoles,
                        Facts = facts,
                        Opinions = opinions
                    });
                }
            }

            // Second pass: Dangling template reference check (Warning only)
            for (int tIdx = 0; tIdx < acceptedTemplates.Count; tIdx++)
            {
                var template = acceptedTemplates[tIdx];

                if (!string.IsNullOrEmpty(template.LinkedTemplateType) && !seenTypes.Contains(template.LinkedTemplateType!))
                {
                    allIssues.Add(new CatalogIssue
                    {
                        TemplateIndex = tIdx,
                        TemplateType = template.Type,
                        Field = "linkedTemplateType",
                        Code = CatalogIssueCode.DanglingTemplateRef,
                        IsError = false,
                        Detail = $"'{template.LinkedTemplateType}' is not in this catalog."
                    });
                }

                for (int fIdx = 0; fIdx < template.Facts.Count; fIdx++)
                {
                    var fact = template.Facts[fIdx];
                    if (!string.IsNullOrEmpty(fact.RefersToTemplateType) && !seenTypes.Contains(fact.RefersToTemplateType!))
                    {
                        allIssues.Add(new CatalogIssue
                        {
                            TemplateIndex = tIdx,
                            TemplateType = template.Type,
                            Field = $"facts[{fIdx}].refersToTemplateType",
                            Code = CatalogIssueCode.DanglingTemplateRef,
                            IsError = false,
                            Detail = $"'{fact.RefersToTemplateType}' is not in this catalog."
                        });
                    }
                }
            }

            if (strictCatalog && allIssues.Any(i => i.IsError))
            {
                return new EventCatalog(Array.Empty<EventTemplate>(), allIssues, templatesArray.Count);
            }

            return new EventCatalog(acceptedTemplates, allIssues, skippedCount);
        }

        private static string? NormalizeTemplateTypeRef(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return null;
            string trimmed = raw.Trim();
            if (trimmed.StartsWith("{LINKED_", StringComparison.OrdinalIgnoreCase) && trimmed.EndsWith("}", StringComparison.Ordinal))
            {
                string inner = trimmed.Substring(8, trimmed.Length - 9);
                return inner.ToLowerInvariant();
            }
            if (trimmed.StartsWith("{", StringComparison.Ordinal) && trimmed.EndsWith("}", StringComparison.Ordinal))
            {
                string inner = trimmed.Substring(1, trimmed.Length - 2);
                return inner.ToLowerInvariant();
            }
            return trimmed;
        }
    }
}
