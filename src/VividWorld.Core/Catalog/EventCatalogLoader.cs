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

                // 3. DramaScale、DramaWeight
                // 沒標明尺度的模板是舊寫法（dramaWeight 是 1..5 的段）；dramaScale 寫 10 才是 1..10 的份量。
                int dramaScale = DramaScales.Legacy;
                bool dramaScaleValid = true;
                var scaleProp = templateObj.Property("dramaScale", StringComparison.OrdinalIgnoreCase);
                if (scaleProp != null && scaleProp.Value.Type != JTokenType.Null)
                {
                    int parsedScale = 0;
                    bool parsed = false;
                    try
                    {
                        parsedScale = scaleProp.Value.Value<int>();
                        parsed = true;
                    }
                    catch
                    {
                        parsed = false;
                    }
                    if (parsed && DramaScales.IsKnownScale(parsedScale))
                    {
                        dramaScale = parsedScale;
                    }
                    else
                    {
                        dramaScaleValid = false;
                        templateIssues.Add(new CatalogIssue
                        {
                            TemplateIndex = i,
                            TemplateType = templateType,
                            Field = "dramaScale",
                            Code = CatalogIssueCode.DramaOutOfRange,
                            IsError = true,
                            Detail = "dramaScale must be 10 (dramaWeight is 1..10) or omitted (dramaWeight is the old 1..5)."
                        });
                    }
                }

                int? dramaWeight = null;
                var dramaProp = templateObj.Property("dramaWeight", StringComparison.OrdinalIgnoreCase);
                if (dramaScaleValid && dramaProp != null && dramaProp.Value.Type != JTokenType.Null)
                {
                    int maxAllowed = dramaScale == DramaScales.Ten ? DramaScales.MaxWeight : DramaScales.MaxBand;
                    try
                    {
                        int dw = dramaProp.Value.Value<int>();
                        if (!FactValidationRules.IsValidDramaWeight(dw, dramaScale))
                        {
                            templateIssues.Add(new CatalogIssue
                            {
                                TemplateIndex = i,
                                TemplateType = templateType,
                                Field = "dramaWeight",
                                Code = CatalogIssueCode.DramaOutOfRange,
                                IsError = true,
                                Detail = $"DramaWeight {dw} is out of range. Must be between 1 and {maxAllowed}."
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
                            Detail = $"DramaWeight must be an integer between 1 and {maxAllowed}."
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
                                bool isDynamicPlaceholder = varVal.Length > 2 &&
                                                            varVal.StartsWith("{", StringComparison.Ordinal) &&
                                                            varVal.EndsWith("}", StringComparison.Ordinal);
                                if (!isDynamicPlaceholder && !FactValidationRules.HasValidVarPrefix(varVal))
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

                // 8. SelfTell
                Dictionary<string, SelfTellRule>? selfTell = null;
                var selfTellProp = templateObj.Property("selfTell", StringComparison.OrdinalIgnoreCase);
                if (selfTellProp != null && selfTellProp.Value.Type != JTokenType.Null)
                {
                    if (origin == EventOrigin.Secret)
                    {
                        allIssues.Add(new CatalogIssue
                        {
                            TemplateIndex = i,
                            TemplateType = templateType,
                            Field = "selfTell",
                            Code = CatalogIssueCode.SelfTellInvalid,
                            IsError = true,
                            Detail = "Secret event template cannot declare selfTell. Field will be ignored."
                        });
                        // 秘密類模板不可宣告 selfTell，報錯並忽略該欄位
                    }
                    else if (selfTellProp.Value is not JObject selfTellObj)
                    {
                        templateIssues.Add(new CatalogIssue
                        {
                            TemplateIndex = i,
                            TemplateType = templateType,
                            Field = "selfTell",
                            Code = CatalogIssueCode.SelfTellInvalid,
                            IsError = true,
                            Detail = "Template selfTell must be a JSON object."
                        });
                    }
                    else
                    {
                        selfTell = new Dictionary<string, SelfTellRule>(StringComparer.Ordinal);
                        foreach (var prop in selfTellObj.Properties())
                        {
                            string roleKey = prop.Name;
                            if (!roles.ContainsKey(roleKey))
                            {
                                templateIssues.Add(new CatalogIssue
                                {
                                    TemplateIndex = i,
                                    TemplateType = templateType,
                                    Field = $"selfTell.{roleKey}",
                                    Code = CatalogIssueCode.SelfTellInvalid,
                                    IsError = true,
                                    Detail = $"Role '{roleKey}' in selfTell is not a declared role."
                                });
                                continue;
                            }

                            var val = prop.Value;
                            if (val.Type == JTokenType.String)
                            {
                                string strVal = val.Value<string>() ?? "";
                                if (string.Equals(strVal, "never", StringComparison.OrdinalIgnoreCase))
                                {
                                    selfTell[roleKey] = SelfTellRule.Never;
                                }
                                else
                                {
                                    templateIssues.Add(new CatalogIssue
                                    {
                                        TemplateIndex = i,
                                        TemplateType = templateType,
                                        Field = $"selfTell.{roleKey}",
                                        Code = CatalogIssueCode.SelfTellInvalid,
                                        IsError = true,
                                        Detail = $"Invalid string value '{strVal}' for selfTell. Expected 'never'."
                                    });
                                }
                            }
                            else if (val is JObject ruleObj)
                            {
                                var traitProp = ruleObj.Property("trait", StringComparison.OrdinalIgnoreCase);
                                string? trait = traitProp?.Value?.Value<string>();
                                var minProp = ruleObj.Property("min", StringComparison.OrdinalIgnoreCase);

                                bool traitValid = trait != null && (
                                    string.Equals(trait, "honor", StringComparison.OrdinalIgnoreCase) ||
                                    string.Equals(trait, "mercy", StringComparison.OrdinalIgnoreCase) ||
                                    string.Equals(trait, "valor", StringComparison.OrdinalIgnoreCase) ||
                                    string.Equals(trait, "calculating", StringComparison.OrdinalIgnoreCase) ||
                                    string.Equals(trait, "generosity", StringComparison.OrdinalIgnoreCase));

                                bool minValid = false;
                                int minVal = 0;
                                if (minProp != null && minProp.Value.Type == JTokenType.Integer)
                                {
                                    minVal = minProp.Value.Value<int>();
                                    minValid = true;
                                }

                                if (!traitValid)
                                {
                                    templateIssues.Add(new CatalogIssue
                                    {
                                        TemplateIndex = i,
                                        TemplateType = templateType,
                                        Field = $"selfTell.{roleKey}.trait",
                                        Code = CatalogIssueCode.SelfTellInvalid,
                                        IsError = true,
                                        Detail = $"Trait '{trait}' is invalid. Expected honor, mercy, valor, calculating, or generosity."
                                    });
                                }

                                if (!minValid)
                                {
                                    templateIssues.Add(new CatalogIssue
                                    {
                                        TemplateIndex = i,
                                        TemplateType = templateType,
                                        Field = $"selfTell.{roleKey}.min",
                                        Code = CatalogIssueCode.SelfTellInvalid,
                                        IsError = true,
                                        Detail = "Min trait threshold must be an integer."
                                    });
                                }

                                if (traitValid && minValid)
                                {
                                    var rule = new SelfTellRule(trait!.ToLowerInvariant(), minVal);
                                    foreach (var p in ruleObj.Properties())
                                    {
                                        if (!string.Equals(p.Name, "trait", StringComparison.OrdinalIgnoreCase) &&
                                            !string.Equals(p.Name, "min", StringComparison.OrdinalIgnoreCase))
                                        {
                                            rule.Extra[p.Name] = p.Value;
                                        }
                                    }
                                    selfTell[roleKey] = rule;
                                }
                            }
                            else
                            {
                                templateIssues.Add(new CatalogIssue
                                {
                                    TemplateIndex = i,
                                    TemplateType = templateType,
                                    Field = $"selfTell.{roleKey}",
                                    Code = CatalogIssueCode.SelfTellInvalid,
                                    IsError = true,
                                    Detail = "SelfTell rule must be 'never' or an object with 'trait' and 'min'."
                                });
                            }
                        }
                    }
                }

                // 9. retired：停用的型別不再產生、已存下來的也不再傳，事件資料保留
                bool retired = templateObj.Property("retired", StringComparison.OrdinalIgnoreCase)?.Value?.Value<bool>() ?? false;

                // 10. colocatedWitnessAsHearsay：同在一地的人在當地聽到消息（第 1 手、沒有指名來源），不算親眼看到
                bool colocatedWitnessAsHearsay = templateObj.Property("colocatedWitnessAsHearsay", StringComparison.OrdinalIgnoreCase)?.Value?.Value<bool>() ?? false;

                // 11. feelings／feelingOverrides：角色 → 感想類別。沒有這個欄位的模板講給玩家聽時不附感想
                Dictionary<string, string>? feelings = null;
                List<FeelingOverride>? feelingOverrides = null;
                var feelingsProp = templateObj.Property("feelings", StringComparison.OrdinalIgnoreCase);
                if (feelingsProp != null && feelingsProp.Value.Type != JTokenType.Null)
                {
                    if (feelingsProp.Value is not JObject feelingsObj)
                    {
                        templateIssues.Add(new CatalogIssue
                        {
                            TemplateIndex = i,
                            TemplateType = templateType,
                            Field = "feelings",
                            Code = CatalogIssueCode.FeelingInvalid,
                            IsError = true,
                            Detail = "Template feelings must be a JSON object of role -> category."
                        });
                    }
                    else
                    {
                        feelings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                        foreach (var prop in feelingsObj.Properties())
                        {
                            string? cat = prop.Value.Type == JTokenType.String ? prop.Value.Value<string>() : null;
                            if (!roles.ContainsKey(prop.Name))
                            {
                                templateIssues.Add(new CatalogIssue
                                {
                                    TemplateIndex = i,
                                    TemplateType = templateType,
                                    Field = $"feelings.{prop.Name}",
                                    Code = CatalogIssueCode.FeelingInvalid,
                                    IsError = true,
                                    Detail = $"Role '{prop.Name}' in feelings is not a declared role."
                                });
                            }
                            else if (cat == null || !VividWorld.Core.Feelings.FeelingCategories.IsKnown(cat))
                            {
                                templateIssues.Add(new CatalogIssue
                                {
                                    TemplateIndex = i,
                                    TemplateType = templateType,
                                    Field = $"feelings.{prop.Name}",
                                    Code = CatalogIssueCode.FeelingInvalid,
                                    IsError = true,
                                    Detail = $"Feeling category '{cat}' is not one of: {string.Join(", ", VividWorld.Core.Feelings.FeelingCategories.Ids)}."
                                });
                            }
                            else
                            {
                                feelings[prop.Name] = cat;
                            }
                        }
                    }
                }

                var feelingOverridesProp = templateObj.Property("feelingOverrides", StringComparison.OrdinalIgnoreCase);
                if (feelingOverridesProp != null && feelingOverridesProp.Value.Type != JTokenType.Null)
                {
                    if (feelingOverridesProp.Value is not JArray overridesArr)
                    {
                        templateIssues.Add(new CatalogIssue
                        {
                            TemplateIndex = i,
                            TemplateType = templateType,
                            Field = "feelingOverrides",
                            Code = CatalogIssueCode.FeelingInvalid,
                            IsError = true,
                            Detail = "Template feelingOverrides must be a JSON array."
                        });
                    }
                    else
                    {
                        feelingOverrides = new List<FeelingOverride>();
                        for (int oi = 0; oi < overridesArr.Count; oi++)
                        {
                            var ov = overridesArr[oi] as JObject;
                            string? ovRole = ov?["role"]?.Value<string>();
                            string? ovFact = ov?["whenFact"]?.Value<string>();
                            string? ovCat = ov?["category"]?.Value<string>();
                            if (string.IsNullOrEmpty(ovRole) || !roles.ContainsKey(ovRole!)
                                || string.IsNullOrEmpty(ovFact)
                                || ovCat == null || !VividWorld.Core.Feelings.FeelingCategories.IsKnown(ovCat))
                            {
                                templateIssues.Add(new CatalogIssue
                                {
                                    TemplateIndex = i,
                                    TemplateType = templateType,
                                    Field = $"feelingOverrides[{oi}]",
                                    Code = CatalogIssueCode.FeelingInvalid,
                                    IsError = true,
                                    Detail = "Each override needs a declared 'role', a 'whenFact' text id and a known 'category'."
                                });
                                continue;
                            }
                            feelingOverrides.Add(new FeelingOverride { Role = ovRole!, WhenFact = ovFact!, Category = ovCat });
                        }
                    }
                }

                // 12. selfFeelingVariants：角色 → 個性版本清單（順序就是優先順序）
                Dictionary<string, List<SelfFeelingVariantRule>>? selfFeelingVariants = null;
                var sfvProp = templateObj.Property("selfFeelingVariants", StringComparison.OrdinalIgnoreCase);
                if (sfvProp != null && sfvProp.Value.Type != JTokenType.Null)
                {
                    void SfvIssue(string field, string detail) => templateIssues.Add(new CatalogIssue
                    {
                        TemplateIndex = i,
                        TemplateType = templateType,
                        Field = field,
                        Code = CatalogIssueCode.SelfFeelingVariantInvalid,
                        IsError = true,
                        Detail = detail
                    });

                    if (sfvProp.Value is not JObject sfvObj)
                    {
                        SfvIssue("selfFeelingVariants", "Template selfFeelingVariants must be a JSON object of role -> array of variants.");
                    }
                    else
                    {
                        selfFeelingVariants = new Dictionary<string, List<SelfFeelingVariantRule>>(StringComparer.OrdinalIgnoreCase);
                        foreach (var prop in sfvObj.Properties())
                        {
                            string field = $"selfFeelingVariants.{prop.Name}";
                            if (!roles.ContainsKey(prop.Name))
                            {
                                SfvIssue(field, $"Role '{prop.Name}' in selfFeelingVariants is not a declared role.");
                                continue;
                            }
                            if (prop.Value is not JArray arr)
                            {
                                SfvIssue(field, "Each role needs a JSON array of variants.");
                                continue;
                            }
                            var list = new List<SelfFeelingVariantRule>();
                            for (int vi = 0; vi < arr.Count; vi++)
                            {
                                var vo = arr[vi] as JObject;
                                string? tendency = vo?["tendency"]?.Type == JTokenType.String ? vo["tendency"]!.Value<string>() : null;
                                string? trait = vo?["trait"]?.Type == JTokenType.String ? vo["trait"]!.Value<string>() : null;
                                var minTok = vo?["min"];
                                var maxTok = vo?["max"];
                                bool hasMin = minTok != null && minTok.Type != JTokenType.Null;
                                bool hasMax = maxTok != null && maxTok.Type != JTokenType.Null;
                                bool minOk = !hasMin || minTok!.Type == JTokenType.Integer;
                                bool maxOk = !hasMax || maxTok!.Type == JTokenType.Integer;
                                string vfield = $"{field}[{vi}]";
                                if (string.IsNullOrWhiteSpace(tendency) || !tendency!.All(char.IsLetterOrDigit))
                                {
                                    SfvIssue(vfield, "Each variant needs a 'tendency' name made of letters and digits.");
                                }
                                else if (list.Any(x => string.Equals(x.Tendency, tendency, StringComparison.Ordinal)))
                                {
                                    SfvIssue(vfield, $"Tendency '{tendency}' is declared twice for this role.");
                                }
                                else if (!SelfFeelingVariantSelector.IsKnownTrait(trait))
                                {
                                    SfvIssue(vfield, $"Trait '{trait}' is invalid. Expected honor, mercy, valor, calculating, or generosity.");
                                }
                                else if (hasMin == hasMax || !minOk || !maxOk)
                                {
                                    SfvIssue(vfield, "A variant needs exactly one integer threshold: 'min' or 'max'.");
                                }
                                else
                                {
                                    list.Add(new SelfFeelingVariantRule
                                    {
                                        Tendency = tendency!,
                                        Trait = trait!.ToLowerInvariant(),
                                        Min = hasMin ? minTok!.Value<int>() : (int?)null,
                                        Max = hasMax ? maxTok!.Value<int>() : (int?)null
                                    });
                                }
                            }
                            selfFeelingVariants[prop.Name] = list;
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
                        DramaScale = dramaScale,
                        LinkedTemplateType = linkedTemplateType,
                        Roles = roles,
                        KnowingRoles = knowingRoles,
                        Facts = facts,
                        Opinions = opinions,
                        SelfTell = selfTell,
                        Retired = retired,
                        ColocatedWitnessAsHearsay = colocatedWitnessAsHearsay,
                        Feelings = feelings,
                        FeelingOverrides = feelingOverrides,
                        SelfFeelingVariants = selfFeelingVariants
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
