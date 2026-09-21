using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Newtonsoft.Json.Linq;
using VividWorld.Core.Grudges;

namespace VividWorld.Core.Situations
{
    public static class SituationCatalogLoader
    {
        private static readonly HashSet<string> ValidConditionTypes = new(StringComparer.OrdinalIgnoreCase)
        {
            "sameSettlement",
            "differentClan",
            "sameKingdom",
            "isClanLeader",
            "clanTierCompare",
            "roleBound",
            "cooldown"
        };

        private static readonly HashSet<string> ValidComparisonOps = new(StringComparer.Ordinal)
        {
            ">", ">=", "<", "<=", "==", "!="
        };

        private static readonly HashSet<string> ValidSettlementKinds = new(StringComparer.OrdinalIgnoreCase)
        {
            "town", "castle"
        };

        private static readonly HashSet<string> ValidTraits = new(StringComparer.OrdinalIgnoreCase)
        {
            "honor", "mercy", "valor", "calculating", "generosity"
        };

        private static readonly HashSet<string> KnownSituationKeys = new(StringComparer.OrdinalIgnoreCase)
        {
            "id", "trigger", "decider", "minBranchWeight", "roles", "conditions", "branches", "weight", "devOnly"
        };

        private static readonly HashSet<string> KnownRoleKeys = new(StringComparer.OrdinalIgnoreCase)
        {
            "derived", "optional"
        };

        private static readonly HashSet<string> KnownConditionKeys = new(StringComparer.OrdinalIgnoreCase)
        {
            "type", "roles", "kinds", "a", "b", "role", "value", "op", "days"
        };

        private static readonly HashSet<string> KnownBranchKeys = new(StringComparer.OrdinalIgnoreCase)
        {
            "id", "base", "traits", "preconditions", "events", "grudges"
        };

        private static readonly HashSet<string> KnownGrudgeKeys = new(StringComparer.OrdinalIgnoreCase)
        {
            "from", "to", "amount", "ledgerOnly", "escalate"
        };

        private static readonly HashSet<string> KnownBranchEventKeys = new(StringComparer.OrdinalIgnoreCase)
        {
            "type", "bind"
        };

        public static SituationCatalog Load(string json)
        {
            var allIssues = new List<SituationIssue>();

            if (string.IsNullOrWhiteSpace(json))
            {
                allIssues.Add(new SituationIssue
                {
                    SituationIndex = -1,
                    SituationId = "(unknown)",
                    Field = "json",
                    Code = SituationIssueCode.BadJson,
                    IsError = true,
                    Detail = "Situation catalog JSON is empty or whitespace."
                });
                return new SituationCatalog(Array.Empty<SituationTemplate>(), allIssues, 0);
            }

            JToken root;
            try
            {
                root = JToken.Parse(json);
            }
            catch (Exception ex)
            {
                allIssues.Add(new SituationIssue
                {
                    SituationIndex = -1,
                    SituationId = "(unknown)",
                    Field = "json",
                    Code = SituationIssueCode.BadJson,
                    IsError = true,
                    Detail = $"Failed to parse situation catalog JSON: {ex.Message}"
                });
                return new SituationCatalog(Array.Empty<SituationTemplate>(), allIssues, 0);
            }

            JArray? situationsArray = root as JArray;
            if (situationsArray == null && root is JObject rootObj)
            {
                situationsArray = rootObj["situations"] as JArray;
            }

            if (situationsArray == null)
            {
                allIssues.Add(new SituationIssue
                {
                    SituationIndex = -1,
                    SituationId = "(unknown)",
                    Field = "json",
                    Code = SituationIssueCode.BadJson,
                    IsError = true,
                    Detail = "Situation catalog root must contain a 'situations' JSON array."
                });
                return new SituationCatalog(Array.Empty<SituationTemplate>(), allIssues, 0);
            }

            var acceptedSituations = new List<SituationTemplate>();
            var seenIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            int skippedCount = 0;

            for (int i = 0; i < situationsArray.Count; i++)
            {
                var sitToken = situationsArray[i];
                if (sitToken is not JObject sitObj)
                {
                    allIssues.Add(new SituationIssue
                    {
                        SituationIndex = i,
                        SituationId = "(unknown)",
                        Field = $"[{i}]",
                        Code = SituationIssueCode.BadJson,
                        IsError = true,
                        Detail = "Situation entry must be a JSON object."
                    });
                    skippedCount++;
                    continue;
                }

                var situationIssues = new List<SituationIssue>();

                // Check unknown properties on situation object
                foreach (var prop in sitObj.Properties())
                {
                    if (!KnownSituationKeys.Contains(prop.Name))
                    {
                        situationIssues.Add(new SituationIssue
                        {
                            SituationIndex = i,
                            SituationId = sitObj["id"]?.Value<string>() ?? "(unknown)",
                            Field = prop.Name,
                            Code = SituationIssueCode.UnknownProperty,
                            IsError = false,
                            Detail = $"Unknown property '{prop.Name}' in situation definition."
                        });
                    }
                }

                // 1. id
                string situationId = "(unknown)";
                var idProp = sitObj.Property("id", StringComparison.OrdinalIgnoreCase);
                string? rawId = idProp?.Value?.Value<string>();

                if (string.IsNullOrWhiteSpace(rawId))
                {
                    situationIssues.Add(new SituationIssue
                    {
                        SituationIndex = i,
                        SituationId = "(unknown)",
                        Field = "id",
                        Code = SituationIssueCode.MissingId,
                        IsError = true,
                        Detail = "Situation id cannot be null or whitespace."
                    });
                }
                else
                {
                    situationId = rawId!.Trim();
                    if (seenIds.Contains(situationId))
                    {
                        situationIssues.Add(new SituationIssue
                        {
                            SituationIndex = i,
                            SituationId = situationId,
                            Field = "id",
                            Code = SituationIssueCode.DuplicateId,
                            IsError = true,
                            Detail = $"Duplicate situation id '{situationId}'. Earlier definition takes precedence."
                        });
                    }
                    else
                    {
                        seenIds.Add(situationId);
                    }
                }

                // 2. trigger
                string trigger = string.Empty;
                var triggerProp = sitObj.Property("trigger", StringComparison.OrdinalIgnoreCase);
                string? rawTrigger = triggerProp?.Value?.Value<string>();
                if (string.IsNullOrWhiteSpace(rawTrigger) || !string.Equals(rawTrigger!.Trim(), "direct", StringComparison.Ordinal))
                {
                    situationIssues.Add(new SituationIssue
                    {
                        SituationIndex = i,
                        SituationId = situationId,
                        Field = "trigger",
                        Code = SituationIssueCode.BadTrigger,
                        IsError = true,
                        Detail = $"Situation trigger must be 'direct', got '{rawTrigger}'."
                    });
                }
                else
                {
                    trigger = rawTrigger!.Trim();
                }

                // 3. minBranchWeight (optional)
                double? minBranchWeight = null;
                var mbwProp = sitObj.Property("minBranchWeight", StringComparison.OrdinalIgnoreCase);
                if (mbwProp != null && mbwProp.Value.Type != JTokenType.Null)
                {
                    try
                    {
                        double val = mbwProp.Value.Value<double>();
                        if (double.IsNaN(val) || double.IsInfinity(val) || val < 0.0)
                        {
                            situationIssues.Add(new SituationIssue
                            {
                                SituationIndex = i,
                                SituationId = situationId,
                                Field = "minBranchWeight",
                                Code = SituationIssueCode.InvalidMinBranchWeight,
                                IsError = true,
                                Detail = $"minBranchWeight must be >= 0.0, got {val}."
                            });
                        }
                        else
                        {
                            minBranchWeight = val;
                        }
                    }
                    catch
                    {
                        situationIssues.Add(new SituationIssue
                        {
                            SituationIndex = i,
                            SituationId = situationId,
                            Field = "minBranchWeight",
                            Code = SituationIssueCode.InvalidMinBranchWeight,
                            IsError = true,
                            Detail = "minBranchWeight must be a numeric value or null."
                        });
                    }
                }

                // 3b. weight (optional, default 1.0)
                double weight = 1.0;
                var weightProp = sitObj.Property("weight", StringComparison.OrdinalIgnoreCase);
                if (weightProp != null && weightProp.Value.Type != JTokenType.Null)
                {
                    try
                    {
                        double val = weightProp.Value.Value<double>();
                        if (double.IsNaN(val) || double.IsInfinity(val) || val < 0.0)
                        {
                            situationIssues.Add(new SituationIssue
                            {
                                SituationIndex = i,
                                SituationId = situationId,
                                Field = "weight",
                                Code = SituationIssueCode.InvalidWeight,
                                IsError = true,
                                Detail = $"weight must be a finite non-negative number, got {val}."
                            });
                            weight = 1.0;
                        }
                        else
                        {
                            weight = val;
                        }
                    }
                    catch
                    {
                        situationIssues.Add(new SituationIssue
                        {
                            SituationIndex = i,
                            SituationId = situationId,
                            Field = "weight",
                            Code = SituationIssueCode.InvalidWeight,
                            IsError = true,
                            Detail = "weight must be a numeric value."
                        });
                        weight = 1.0;
                    }
                }

                // 3c. devOnly (optional, default false)
                bool devOnly = false;
                var devOnlyProp = sitObj.Property("devOnly", StringComparison.OrdinalIgnoreCase);
                if (devOnlyProp != null && devOnlyProp.Value.Type != JTokenType.Null)
                {
                    try
                    {
                        devOnly = devOnlyProp.Value.Value<bool>();
                    }
                    catch
                    {
                        devOnly = false;
                    }
                }

                // 4. roles
                var rolesDict = new Dictionary<string, SituationRoleDef>(StringComparer.OrdinalIgnoreCase);
                var rolesProp = sitObj.Property("roles", StringComparison.OrdinalIgnoreCase);
                if (rolesProp?.Value is not JObject rolesObj)
                {
                    situationIssues.Add(new SituationIssue
                    {
                        SituationIndex = i,
                        SituationId = situationId,
                        Field = "roles",
                        Code = SituationIssueCode.InsufficientRoles,
                        IsError = true,
                        Detail = "roles must be a JSON object mapping role names to definitions."
                    });
                }
                else
                {
                    int nonDerivedCount = 0;
                    foreach (var rProp in rolesObj.Properties())
                    {
                        string roleName = rProp.Name.Trim();
                        var rDef = new SituationRoleDef();
                        if (rProp.Value is JObject rObj)
                        {
                            foreach (var p in rObj.Properties())
                            {
                                if (!KnownRoleKeys.Contains(p.Name))
                                {
                                    situationIssues.Add(new SituationIssue
                                    {
                                        SituationIndex = i,
                                        SituationId = situationId,
                                        Field = $"roles.{roleName}.{p.Name}",
                                        Code = SituationIssueCode.UnknownProperty,
                                        IsError = false,
                                        Detail = $"Unknown property '{p.Name}' in role '{roleName}'."
                                    });
                                }
                            }

                            string? derived = rObj["derived"]?.Value<string>();
                            if (!string.IsNullOrWhiteSpace(derived))
                            {
                                if (!string.Equals(derived!.Trim(), "settlementOwnerClanLeader", StringComparison.Ordinal))
                                {
                                    situationIssues.Add(new SituationIssue
                                    {
                                        SituationIndex = i,
                                        SituationId = situationId,
                                        Field = $"roles.{roleName}.derived",
                                        Code = SituationIssueCode.UnknownDerivedRole,
                                        IsError = true,
                                        Detail = $"Unknown derived role '{derived}', only 'settlementOwnerClanLeader' is supported."
                                    });
                                }
                                rDef.Derived = derived!.Trim();
                            }

                            bool? opt = rObj["optional"]?.Value<bool>();
                            if (opt.HasValue)
                            {
                                rDef.Optional = opt.Value;
                            }
                        }

                        if (!rDef.IsDerived)
                        {
                            nonDerivedCount++;
                        }

                        rolesDict[roleName] = rDef;
                    }

                    if (nonDerivedCount < 2)
                    {
                        situationIssues.Add(new SituationIssue
                        {
                            SituationIndex = i,
                            SituationId = situationId,
                            Field = "roles",
                            Code = SituationIssueCode.InsufficientRoles,
                            IsError = true,
                            Detail = $"Situation must define at least 2 non-derived roles, found {nonDerivedCount}."
                        });
                    }
                }

                // 5. decider
                string decider = string.Empty;
                var deciderProp = sitObj.Property("decider", StringComparison.OrdinalIgnoreCase);
                string? rawDecider = deciderProp?.Value?.Value<string>();
                if (string.IsNullOrWhiteSpace(rawDecider))
                {
                    situationIssues.Add(new SituationIssue
                    {
                        SituationIndex = i,
                        SituationId = situationId,
                        Field = "decider",
                        Code = SituationIssueCode.MissingDecider,
                        IsError = true,
                        Detail = "decider cannot be null or whitespace."
                    });
                }
                else
                {
                    decider = rawDecider!.Trim();
                    if (!rolesDict.TryGetValue(decider, out var deciderDef))
                    {
                        situationIssues.Add(new SituationIssue
                        {
                            SituationIndex = i,
                            SituationId = situationId,
                            Field = "decider",
                            Code = SituationIssueCode.MissingDecider,
                            IsError = true,
                            Detail = $"decider '{decider}' is not defined in roles."
                        });
                    }
                    else if (deciderDef.IsDerived)
                    {
                        situationIssues.Add(new SituationIssue
                        {
                            SituationIndex = i,
                            SituationId = situationId,
                            Field = "decider",
                            Code = SituationIssueCode.DeciderIsDerived,
                            IsError = true,
                            Detail = $"decider '{decider}' cannot be a derived role."
                        });
                    }
                }

                // 6. conditions
                var conditionsList = new List<SituationConditionDef>();
                var condsProp = sitObj.Property("conditions", StringComparison.OrdinalIgnoreCase);
                if (condsProp?.Value is JArray condsArray)
                {
                    for (int cIdx = 0; cIdx < condsArray.Count; cIdx++)
                    {
                        if (condsArray[cIdx] is not JObject cObj)
                        {
                            situationIssues.Add(new SituationIssue
                            {
                                SituationIndex = i,
                                SituationId = situationId,
                                Field = $"conditions[{cIdx}]",
                                Code = SituationIssueCode.BadJson,
                                IsError = true,
                                Detail = "Condition must be a JSON object."
                            });
                            continue;
                        }

                        foreach (var p in cObj.Properties())
                        {
                            if (!KnownConditionKeys.Contains(p.Name))
                            {
                                situationIssues.Add(new SituationIssue
                                {
                                    SituationIndex = i,
                                    SituationId = situationId,
                                    Field = $"conditions[{cIdx}].{p.Name}",
                                    Code = SituationIssueCode.UnknownProperty,
                                    IsError = false,
                                    Detail = $"Unknown property '{p.Name}' in condition."
                                });
                            }
                        }

                        string cType = cObj["type"]?.Value<string>()?.Trim() ?? string.Empty;
                        if (!ValidConditionTypes.Contains(cType))
                        {
                            situationIssues.Add(new SituationIssue
                            {
                                SituationIndex = i,
                                SituationId = situationId,
                                Field = $"conditions[{cIdx}].type",
                                Code = SituationIssueCode.UnknownConditionType,
                                IsError = true,
                                Detail = $"Unknown condition type '{cType}'."
                            });
                            continue;
                        }

                        var condDef = new SituationConditionDef { Type = cType };

                        switch (cType.ToLowerInvariant())
                        {
                            case "samesettlement":
                            {
                                var rArray = cObj["roles"] as JArray;
                                if (rArray == null || rArray.Count == 0)
                                {
                                    situationIssues.Add(new SituationIssue
                                    {
                                        SituationIndex = i,
                                        SituationId = situationId,
                                        Field = $"conditions[{cIdx}].roles",
                                        Code = SituationIssueCode.MissingConditionProperty,
                                        IsError = true,
                                        Detail = "sameSettlement condition requires non-empty 'roles' array."
                                    });
                                }
                                else
                                {
                                    condDef.Roles = new List<string>();
                                    foreach (var rTok in rArray)
                                    {
                                        string rName = rTok.Value<string>()?.Trim() ?? string.Empty;
                                        if (!rolesDict.ContainsKey(rName))
                                        {
                                            situationIssues.Add(new SituationIssue
                                            {
                                                SituationIndex = i,
                                                SituationId = situationId,
                                                Field = $"conditions[{cIdx}].roles",
                                                Code = SituationIssueCode.UnknownRoleInCondition,
                                                IsError = true,
                                                Detail = $"Unknown role '{rName}' in sameSettlement condition."
                                            });
                                        }
                                        condDef.Roles.Add(rName);
                                    }
                                }

                                var kArray = cObj["kinds"] as JArray;
                                if (kArray == null || kArray.Count == 0)
                                {
                                    situationIssues.Add(new SituationIssue
                                    {
                                        SituationIndex = i,
                                        SituationId = situationId,
                                        Field = $"conditions[{cIdx}].kinds",
                                        Code = SituationIssueCode.MissingConditionProperty,
                                        IsError = true,
                                        Detail = "sameSettlement condition requires 'kinds' array (town/castle)."
                                    });
                                }
                                else
                                {
                                    condDef.Kinds = new List<string>();
                                    foreach (var kTok in kArray)
                                    {
                                        string kName = kTok.Value<string>()?.Trim().ToLowerInvariant() ?? string.Empty;
                                        if (!ValidSettlementKinds.Contains(kName))
                                        {
                                            situationIssues.Add(new SituationIssue
                                            {
                                                SituationIndex = i,
                                                SituationId = situationId,
                                                Field = $"conditions[{cIdx}].kinds",
                                                Code = SituationIssueCode.InvalidConditionKind,
                                                IsError = true,
                                                Detail = $"Invalid settlement kind '{kName}', only 'town' and 'castle' allowed."
                                            });
                                        }
                                        condDef.Kinds.Add(kName);
                                    }
                                }
                                break;
                            }

                            case "differentclan":
                            case "samekingdom":
                            {
                                string a = cObj["a"]?.Value<string>()?.Trim() ?? string.Empty;
                                string b = cObj["b"]?.Value<string>()?.Trim() ?? string.Empty;
                                if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b))
                                {
                                    situationIssues.Add(new SituationIssue
                                    {
                                        SituationIndex = i,
                                        SituationId = situationId,
                                        Field = $"conditions[{cIdx}]",
                                        Code = SituationIssueCode.MissingConditionProperty,
                                        IsError = true,
                                        Detail = $"{cType} condition requires 'a' and 'b' role names."
                                    });
                                }
                                else
                                {
                                    if (!rolesDict.ContainsKey(a))
                                    {
                                        situationIssues.Add(new SituationIssue
                                        {
                                            SituationIndex = i,
                                            SituationId = situationId,
                                            Field = $"conditions[{cIdx}].a",
                                            Code = SituationIssueCode.UnknownRoleInCondition,
                                            IsError = true,
                                            Detail = $"Unknown role '{a}' in {cType} condition."
                                        });
                                    }
                                    if (!rolesDict.ContainsKey(b))
                                    {
                                        situationIssues.Add(new SituationIssue
                                        {
                                            SituationIndex = i,
                                            SituationId = situationId,
                                            Field = $"conditions[{cIdx}].b",
                                            Code = SituationIssueCode.UnknownRoleInCondition,
                                            IsError = true,
                                            Detail = $"Unknown role '{b}' in {cType} condition."
                                        });
                                    }
                                    condDef.A = a;
                                    condDef.B = b;
                                }
                                break;
                            }

                            case "isclanleader":
                            {
                                string r = cObj["role"]?.Value<string>()?.Trim() ?? string.Empty;
                                bool? val = cObj["value"]?.Value<bool>();
                                if (string.IsNullOrEmpty(r) || !val.HasValue)
                                {
                                    situationIssues.Add(new SituationIssue
                                    {
                                        SituationIndex = i,
                                        SituationId = situationId,
                                        Field = $"conditions[{cIdx}]",
                                        Code = SituationIssueCode.MissingConditionProperty,
                                        IsError = true,
                                        Detail = "isClanLeader condition requires 'role' and boolean 'value'."
                                    });
                                }
                                else
                                {
                                    if (!rolesDict.ContainsKey(r))
                                    {
                                        situationIssues.Add(new SituationIssue
                                        {
                                            SituationIndex = i,
                                            SituationId = situationId,
                                            Field = $"conditions[{cIdx}].role",
                                            Code = SituationIssueCode.UnknownRoleInCondition,
                                            IsError = true,
                                            Detail = $"Unknown role '{r}' in isClanLeader condition."
                                        });
                                    }
                                    condDef.Role = r;
                                    condDef.Value = val.Value;
                                }
                                break;
                            }

                            case "clantiercompare":
                            {
                                string a = cObj["a"]?.Value<string>()?.Trim() ?? string.Empty;
                                string b = cObj["b"]?.Value<string>()?.Trim() ?? string.Empty;
                                string op = cObj["op"]?.Value<string>()?.Trim() ?? string.Empty;

                                if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b) || string.IsNullOrEmpty(op))
                                {
                                    situationIssues.Add(new SituationIssue
                                    {
                                        SituationIndex = i,
                                        SituationId = situationId,
                                        Field = $"conditions[{cIdx}]",
                                        Code = SituationIssueCode.MissingConditionProperty,
                                        IsError = true,
                                        Detail = "clanTierCompare condition requires 'a', 'op', and 'b'."
                                    });
                                }
                                else
                                {
                                    if (!rolesDict.ContainsKey(a))
                                    {
                                        situationIssues.Add(new SituationIssue
                                        {
                                            SituationIndex = i,
                                            SituationId = situationId,
                                            Field = $"conditions[{cIdx}].a",
                                            Code = SituationIssueCode.UnknownRoleInCondition,
                                            IsError = true,
                                            Detail = $"Unknown role '{a}' in clanTierCompare condition."
                                        });
                                    }
                                    if (!rolesDict.ContainsKey(b))
                                    {
                                        situationIssues.Add(new SituationIssue
                                        {
                                            SituationIndex = i,
                                            SituationId = situationId,
                                            Field = $"conditions[{cIdx}].b",
                                            Code = SituationIssueCode.UnknownRoleInCondition,
                                            IsError = true,
                                            Detail = $"Unknown role '{b}' in clanTierCompare condition."
                                        });
                                    }
                                    if (!ValidComparisonOps.Contains(op))
                                    {
                                        situationIssues.Add(new SituationIssue
                                        {
                                            SituationIndex = i,
                                            SituationId = situationId,
                                            Field = $"conditions[{cIdx}].op",
                                            Code = SituationIssueCode.InvalidConditionOp,
                                            IsError = true,
                                            Detail = $"Invalid operator '{op}' in clanTierCompare condition, only >, >=, <, <=, ==, != allowed."
                                        });
                                    }
                                    condDef.A = a;
                                    condDef.B = b;
                                    condDef.Op = op;
                                }
                                break;
                            }

                            case "rolebound":
                            {
                                string r = cObj["role"]?.Value<string>()?.Trim() ?? string.Empty;
                                if (string.IsNullOrEmpty(r))
                                {
                                    situationIssues.Add(new SituationIssue
                                    {
                                        SituationIndex = i,
                                        SituationId = situationId,
                                        Field = $"conditions[{cIdx}].role",
                                        Code = SituationIssueCode.MissingConditionProperty,
                                        IsError = true,
                                        Detail = "roleBound condition requires 'role'."
                                    });
                                }
                                else
                                {
                                    if (!rolesDict.ContainsKey(r))
                                    {
                                        situationIssues.Add(new SituationIssue
                                        {
                                            SituationIndex = i,
                                            SituationId = situationId,
                                            Field = $"conditions[{cIdx}].role",
                                            Code = SituationIssueCode.UnknownRoleInCondition,
                                            IsError = true,
                                            Detail = $"Unknown role '{r}' in roleBound condition."
                                        });
                                    }
                                    condDef.Role = r;
                                }
                                break;
                            }

                            case "cooldown":
                            {
                                var daysProp = cObj["days"];
                                if (daysProp == null || daysProp.Type == JTokenType.Null)
                                {
                                    situationIssues.Add(new SituationIssue
                                    {
                                        SituationIndex = i,
                                        SituationId = situationId,
                                        Field = $"conditions[{cIdx}].days",
                                        Code = SituationIssueCode.MissingConditionProperty,
                                        IsError = true,
                                        Detail = "cooldown condition requires 'days'."
                                    });
                                }
                                else
                                {
                                    try
                                    {
                                        double dVal = daysProp.Value<double>();
                                        if (double.IsNaN(dVal) || double.IsInfinity(dVal) || dVal <= 0.0)
                                        {
                                            situationIssues.Add(new SituationIssue
                                            {
                                                SituationIndex = i,
                                                SituationId = situationId,
                                                Field = $"conditions[{cIdx}].days",
                                                Code = SituationIssueCode.MissingConditionProperty,
                                                IsError = true,
                                                Detail = $"cooldown condition requires a positive 'days' number, got {dVal}."
                                            });
                                        }
                                        else
                                        {
                                            condDef.Days = dVal;
                                        }
                                    }
                                    catch
                                    {
                                        situationIssues.Add(new SituationIssue
                                        {
                                            SituationIndex = i,
                                            SituationId = situationId,
                                            Field = $"conditions[{cIdx}].days",
                                            Code = SituationIssueCode.MissingConditionProperty,
                                            IsError = true,
                                            Detail = "cooldown condition 'days' must be a numeric value."
                                        });
                                    }
                                }
                                break;
                            }
                        }

                        conditionsList.Add(condDef);
                    }
                }

                // 7. branches
                var branchesList = new List<SituationBranchDef>();
                var seenBranchIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var branchesProp = sitObj.Property("branches", StringComparison.OrdinalIgnoreCase);
                if (branchesProp?.Value is not JArray branchesArray || branchesArray.Count == 0)
                {
                    situationIssues.Add(new SituationIssue
                    {
                        SituationIndex = i,
                        SituationId = situationId,
                        Field = "branches",
                        Code = SituationIssueCode.NoBranches,
                        IsError = true,
                        Detail = "Situation must have at least one branch."
                    });
                }
                else
                {
                    for (int bIdx = 0; bIdx < branchesArray.Count; bIdx++)
                    {
                        if (branchesArray[bIdx] is not JObject bObj)
                        {
                            situationIssues.Add(new SituationIssue
                            {
                                SituationIndex = i,
                                SituationId = situationId,
                                Field = $"branches[{bIdx}]",
                                Code = SituationIssueCode.BadJson,
                                IsError = true,
                                Detail = "Branch must be a JSON object."
                            });
                            continue;
                        }

                        foreach (var p in bObj.Properties())
                        {
                            if (!KnownBranchKeys.Contains(p.Name))
                            {
                                situationIssues.Add(new SituationIssue
                                {
                                    SituationIndex = i,
                                    SituationId = situationId,
                                    Field = $"branches[{bIdx}].{p.Name}",
                                    Code = SituationIssueCode.UnknownProperty,
                                    IsError = false,
                                    Detail = $"Unknown property '{p.Name}' in branch."
                                });
                            }
                        }

                        string bId = bObj["id"]?.Value<string>()?.Trim() ?? string.Empty;
                        if (string.IsNullOrEmpty(bId))
                        {
                            situationIssues.Add(new SituationIssue
                            {
                                SituationIndex = i,
                                SituationId = situationId,
                                Field = $"branches[{bIdx}].id",
                                Code = SituationIssueCode.MissingId,
                                IsError = true,
                                Detail = "Branch id cannot be null or whitespace."
                            });
                        }
                        else if (seenBranchIds.Contains(bId))
                        {
                            situationIssues.Add(new SituationIssue
                            {
                                SituationIndex = i,
                                SituationId = situationId,
                                Field = $"branches[{bIdx}].id",
                                Code = SituationIssueCode.DuplicateBranchId,
                                IsError = true,
                                Detail = $"Duplicate branch id '{bId}' within situation."
                            });
                        }
                        else
                        {
                            seenBranchIds.Add(bId);
                        }

                        double bBase = 0.0;
                        var baseProp = bObj["base"];
                        if (baseProp == null || baseProp.Type == JTokenType.Null)
                        {
                            situationIssues.Add(new SituationIssue
                            {
                                SituationIndex = i,
                                SituationId = situationId,
                                Field = $"branches[{bIdx}].base",
                                Code = SituationIssueCode.InvalidBranchBase,
                                IsError = true,
                                Detail = "Branch base weight is required."
                            });
                        }
                        else
                        {
                            try
                            {
                                bBase = baseProp.Value<double>();
                                if (double.IsNaN(bBase) || double.IsInfinity(bBase))
                                {
                                    situationIssues.Add(new SituationIssue
                                    {
                                        SituationIndex = i,
                                        SituationId = situationId,
                                        Field = $"branches[{bIdx}].base",
                                        Code = SituationIssueCode.InvalidBranchBase,
                                        IsError = true,
                                        Detail = "Branch base weight must be finite."
                                    });
                                }
                            }
                            catch
                            {
                                situationIssues.Add(new SituationIssue
                                {
                                    SituationIndex = i,
                                    SituationId = situationId,
                                    Field = $"branches[{bIdx}].base",
                                    Code = SituationIssueCode.InvalidBranchBase,
                                    IsError = true,
                                    Detail = "Branch base weight must be a number."
                                });
                            }
                        }

                        var traitsDict = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
                        if (bObj["traits"] is JObject traitsObj)
                        {
                            foreach (var tProp in traitsObj.Properties())
                            {
                                string tName = tProp.Name.Trim().ToLowerInvariant();
                                if (!ValidTraits.Contains(tName))
                                {
                                    situationIssues.Add(new SituationIssue
                                    {
                                        SituationIndex = i,
                                        SituationId = situationId,
                                        Field = $"branches[{bIdx}].traits.{tProp.Name}",
                                        Code = SituationIssueCode.UnknownTraitName,
                                        IsError = true,
                                        Detail = $"Unknown trait '{tProp.Name}', only honor/mercy/valor/calculating/generosity allowed."
                                    });
                                }
                                else
                                {
                                    try
                                    {
                                        double coef = tProp.Value.Value<double>();
                                        if (double.IsNaN(coef) || double.IsInfinity(coef))
                                        {
                                            situationIssues.Add(new SituationIssue
                                            {
                                                SituationIndex = i,
                                                SituationId = situationId,
                                                Field = $"branches[{bIdx}].traits.{tProp.Name}",
                                                Code = SituationIssueCode.InvalidTraitCoefficient,
                                                IsError = true,
                                                Detail = "Trait coefficient must be finite."
                                            });
                                        }
                                        else
                                        {
                                            traitsDict[tName] = coef;
                                        }
                                    }
                                    catch
                                    {
                                        situationIssues.Add(new SituationIssue
                                        {
                                            SituationIndex = i,
                                            SituationId = situationId,
                                            Field = $"branches[{bIdx}].traits.{tProp.Name}",
                                            Code = SituationIssueCode.InvalidTraitCoefficient,
                                            IsError = true,
                                            Detail = "Trait coefficient must be a number."
                                        });
                                    }
                                }
                            }
                        }

                        var precondsList = new List<SituationConditionDef>();
                        if (bObj["preconditions"] is JArray precondsArray)
                        {
                            for (int pIdx = 0; pIdx < precondsArray.Count; pIdx++)
                            {
                                if (precondsArray[pIdx] is not JObject pObj)
                                {
                                    situationIssues.Add(new SituationIssue
                                    {
                                        SituationIndex = i,
                                        SituationId = situationId,
                                        Field = $"branches[{bIdx}].preconditions[{pIdx}]",
                                        Code = SituationIssueCode.BadJson,
                                        IsError = true,
                                        Detail = "Precondition must be a JSON object."
                                    });
                                    continue;
                                }

                                string pType = pObj["type"]?.Value<string>()?.Trim() ?? string.Empty;
                                if (!string.Equals(pType, "roleBound", StringComparison.OrdinalIgnoreCase))
                                {
                                    situationIssues.Add(new SituationIssue
                                    {
                                        SituationIndex = i,
                                        SituationId = situationId,
                                        Field = $"branches[{bIdx}].preconditions[{pIdx}].type",
                                        Code = SituationIssueCode.UnknownPreconditionType,
                                        IsError = true,
                                        Detail = $"Precondition type must be 'roleBound', got '{pType}'."
                                    });
                                    continue;
                                }

                                string pRole = pObj["role"]?.Value<string>()?.Trim() ?? string.Empty;
                                if (string.IsNullOrEmpty(pRole) || !rolesDict.ContainsKey(pRole))
                                {
                                    situationIssues.Add(new SituationIssue
                                    {
                                        SituationIndex = i,
                                        SituationId = situationId,
                                        Field = $"branches[{bIdx}].preconditions[{pIdx}].role",
                                        Code = SituationIssueCode.UnknownRoleInPrecondition,
                                        IsError = true,
                                        Detail = $"Unknown role '{pRole}' in roleBound precondition."
                                    });
                                }
                                else
                                {
                                    precondsList.Add(new SituationConditionDef
                                    {
                                        Type = "roleBound",
                                        Role = pRole
                                    });
                                }
                            }
                        }

                        var eventsList = new List<SituationBranchEventDef>();
                        if (bObj["events"] is not JArray eventsArray || eventsArray.Count == 0)
                        {
                            situationIssues.Add(new SituationIssue
                            {
                                SituationIndex = i,
                                SituationId = situationId,
                                Field = $"branches[{bIdx}].events",
                                Code = SituationIssueCode.NoBranchEvents,
                                IsError = true,
                                Detail = "Branch must define at least one event."
                            });
                        }
                        else
                        {
                            for (int eIdx = 0; eIdx < eventsArray.Count; eIdx++)
                            {
                                if (eventsArray[eIdx] is not JObject eObj)
                                {
                                    situationIssues.Add(new SituationIssue
                                    {
                                        SituationIndex = i,
                                        SituationId = situationId,
                                        Field = $"branches[{bIdx}].events[{eIdx}]",
                                        Code = SituationIssueCode.BadJson,
                                        IsError = true,
                                        Detail = "Event must be a JSON object."
                                    });
                                    continue;
                                }

                                foreach (var p in eObj.Properties())
                                {
                                    if (!KnownBranchEventKeys.Contains(p.Name))
                                    {
                                        situationIssues.Add(new SituationIssue
                                        {
                                            SituationIndex = i,
                                            SituationId = situationId,
                                            Field = $"branches[{bIdx}].events[{eIdx}].{p.Name}",
                                            Code = SituationIssueCode.UnknownProperty,
                                            IsError = false,
                                            Detail = $"Unknown property '{p.Name}' in branch event."
                                        });
                                    }
                                }

                                string eType = eObj["type"]?.Value<string>()?.Trim() ?? string.Empty;
                                if (string.IsNullOrEmpty(eType))
                                {
                                    situationIssues.Add(new SituationIssue
                                    {
                                        SituationIndex = i,
                                        SituationId = situationId,
                                        Field = $"branches[{bIdx}].events[{eIdx}].type",
                                        Code = SituationIssueCode.MissingEventType,
                                        IsError = true,
                                        Detail = "Event type cannot be null or whitespace."
                                    });
                                }

                                var bindDict = new Dictionary<string, string>(StringComparer.Ordinal);
                                if (eObj["bind"] is not JObject bindObj)
                                {
                                    situationIssues.Add(new SituationIssue
                                    {
                                        SituationIndex = i,
                                        SituationId = situationId,
                                        Field = $"branches[{bIdx}].events[{eIdx}].bind",
                                        Code = SituationIssueCode.MissingEventBind,
                                        IsError = true,
                                        Detail = "Event bind must be a JSON object."
                                    });
                                }
                                else
                                {
                                    foreach (var bProp in bindObj.Properties())
                                    {
                                        string phName = bProp.Name.Trim();
                                        string val = bProp.Value?.Value<string>()?.Trim() ?? string.Empty;
                                        if (string.IsNullOrEmpty(val))
                                        {
                                            situationIssues.Add(new SituationIssue
                                            {
                                                SituationIndex = i,
                                                SituationId = situationId,
                                                Field = $"branches[{bIdx}].events[{eIdx}].bind.{phName}",
                                                Code = SituationIssueCode.MissingEventBind,
                                                IsError = true,
                                                Detail = $"Binding for placeholder '{phName}' cannot be empty."
                                            });
                                        }
                                        else if (!string.Equals(val, "@settlement", StringComparison.Ordinal) && !rolesDict.ContainsKey(val))
                                        {
                                            situationIssues.Add(new SituationIssue
                                            {
                                                SituationIndex = i,
                                                SituationId = situationId,
                                                Field = $"branches[{bIdx}].events[{eIdx}].bind.{phName}",
                                                Code = SituationIssueCode.UnknownRoleInEventBind,
                                                IsError = true,
                                                Detail = $"Binding target '{val}' is neither '@settlement' nor a defined role."
                                            });
                                        }
                                        else
                                        {
                                            bindDict[phName] = val;
                                        }
                                    }
                                }

                                eventsList.Add(new SituationBranchEventDef
                                {
                                    Type = eType,
                                    Bind = bindDict
                                });
                            }
                        }

                        List<SituationGrudgeDef>? grudgesList = null;
                        if (bObj.TryGetValue("grudges", out var grudgesToken) && grudgesToken.Type != JTokenType.Null)
                        {
                            if (grudgesToken is not JArray grudgesArray)
                            {
                                situationIssues.Add(new SituationIssue
                                {
                                    SituationIndex = i,
                                    SituationId = situationId,
                                    Field = $"branches[{bIdx}].grudges",
                                    Code = SituationIssueCode.BadJson,
                                    IsError = true,
                                    Detail = "Grudges must be a JSON array."
                                });
                            }
                            else
                            {
                                grudgesList = new List<SituationGrudgeDef>();
                                for (int gIdx = 0; gIdx < grudgesArray.Count; gIdx++)
                                {
                                    if (grudgesArray[gIdx] is not JObject gObj)
                                    {
                                        situationIssues.Add(new SituationIssue
                                        {
                                            SituationIndex = i,
                                            SituationId = situationId,
                                            Field = $"branches[{bIdx}].grudges[{gIdx}]",
                                            Code = SituationIssueCode.BadJson,
                                            IsError = true,
                                            Detail = "Grudge must be a JSON object."
                                        });
                                        continue;
                                    }

                                    var extraDict = new Dictionary<string, JToken>();
                                    foreach (var p in gObj.Properties())
                                    {
                                        if (!KnownGrudgeKeys.Contains(p.Name))
                                        {
                                            situationIssues.Add(new SituationIssue
                                            {
                                                SituationIndex = i,
                                                SituationId = situationId,
                                                Field = $"branches[{bIdx}].grudges[{gIdx}].{p.Name}",
                                                Code = SituationIssueCode.UnknownProperty,
                                                IsError = false,
                                                Detail = $"Unknown property '{p.Name}' in grudge."
                                            });
                                            extraDict[p.Name] = p.Value;
                                        }
                                    }

                                    string gFrom = gObj["from"]?.Value<string>()?.Trim() ?? string.Empty;
                                    if (string.IsNullOrEmpty(gFrom))
                                    {
                                        situationIssues.Add(new SituationIssue
                                        {
                                            SituationIndex = i,
                                            SituationId = situationId,
                                            Field = $"branches[{bIdx}].grudges[{gIdx}].from",
                                            Code = SituationIssueCode.MissingGrudgeProperty,
                                            IsError = true,
                                            Detail = "Grudge 'from' is required."
                                        });
                                    }
                                    else if (!rolesDict.ContainsKey(gFrom))
                                    {
                                        situationIssues.Add(new SituationIssue
                                        {
                                            SituationIndex = i,
                                            SituationId = situationId,
                                            Field = $"branches[{bIdx}].grudges[{gIdx}].from",
                                            Code = SituationIssueCode.UnknownRoleInGrudge,
                                            IsError = true,
                                            Detail = $"Grudge 'from' role '{gFrom}' is not defined in situation roles."
                                        });
                                    }

                                    string gTo = gObj["to"]?.Value<string>()?.Trim() ?? string.Empty;
                                    if (string.IsNullOrEmpty(gTo))
                                    {
                                        situationIssues.Add(new SituationIssue
                                        {
                                            SituationIndex = i,
                                            SituationId = situationId,
                                            Field = $"branches[{bIdx}].grudges[{gIdx}].to",
                                            Code = SituationIssueCode.MissingGrudgeProperty,
                                            IsError = true,
                                            Detail = "Grudge 'to' is required."
                                        });
                                    }
                                    else if (!rolesDict.ContainsKey(gTo))
                                    {
                                        situationIssues.Add(new SituationIssue
                                        {
                                            SituationIndex = i,
                                            SituationId = situationId,
                                            Field = $"branches[{bIdx}].grudges[{gIdx}].to",
                                            Code = SituationIssueCode.UnknownRoleInGrudge,
                                            IsError = true,
                                            Detail = $"Grudge 'to' role '{gTo}' is not defined in situation roles."
                                        });
                                    }
                                    else if (!string.IsNullOrEmpty(gFrom) && string.Equals(gFrom, gTo, StringComparison.Ordinal))
                                    {
                                        situationIssues.Add(new SituationIssue
                                        {
                                            SituationIndex = i,
                                            SituationId = situationId,
                                            Field = $"branches[{bIdx}].grudges[{gIdx}].to",
                                            Code = SituationIssueCode.GrudgeFromEqualsTo,
                                            IsError = true,
                                            Detail = $"Grudge 'to' role cannot be equal to 'from' ('{gFrom}')."
                                        });
                                    }

                                    double gAmount = 0.0;
                                    var amountProp = gObj["amount"];
                                    if (amountProp == null || amountProp.Type == JTokenType.Null)
                                    {
                                        situationIssues.Add(new SituationIssue
                                        {
                                            SituationIndex = i,
                                            SituationId = situationId,
                                            Field = $"branches[{bIdx}].grudges[{gIdx}].amount",
                                            Code = SituationIssueCode.MissingGrudgeProperty,
                                            IsError = true,
                                            Detail = "Grudge 'amount' is required."
                                        });
                                    }
                                    else
                                    {
                                        try
                                        {
                                            gAmount = amountProp.Value<double>();
                                            if (double.IsNaN(gAmount) || double.IsInfinity(gAmount) || gAmount == 0.0)
                                            {
                                                situationIssues.Add(new SituationIssue
                                                {
                                                    SituationIndex = i,
                                                    SituationId = situationId,
                                                    Field = $"branches[{bIdx}].grudges[{gIdx}].amount",
                                                    Code = SituationIssueCode.InvalidGrudgeAmount,
                                                    IsError = true,
                                                    Detail = "Grudge 'amount' must be a finite non-zero number."
                                                });
                                            }
                                        }
                                        catch
                                        {
                                            situationIssues.Add(new SituationIssue
                                            {
                                                SituationIndex = i,
                                                SituationId = situationId,
                                                Field = $"branches[{bIdx}].grudges[{gIdx}].amount",
                                                Code = SituationIssueCode.InvalidGrudgeAmount,
                                                IsError = true,
                                                Detail = "Grudge 'amount' must be a number."
                                            });
                                        }
                                    }

                                    bool gLedgerOnly = false;
                                    var ledgerOnlyProp = gObj["ledgerOnly"];
                                    if (ledgerOnlyProp != null && ledgerOnlyProp.Type != JTokenType.Null)
                                    {
                                        try
                                        {
                                            gLedgerOnly = ledgerOnlyProp.Value<bool>();
                                        }
                                        catch
                                        {
                                            gLedgerOnly = false;
                                        }
                                    }

                                    string? gEscalate = null;
                                    var escalateProp = gObj["escalate"];
                                    if (escalateProp != null && escalateProp.Type != JTokenType.Null)
                                    {
                                        string? escVal = escalateProp.Value<string>()?.Trim();
                                        if (escVal != null && !string.Equals(escVal, "clan", StringComparison.Ordinal))
                                        {
                                            situationIssues.Add(new SituationIssue
                                            {
                                                SituationIndex = i,
                                                SituationId = situationId,
                                                Field = $"branches[{bIdx}].grudges[{gIdx}].escalate",
                                                Code = SituationIssueCode.InvalidGrudgeEscalate,
                                                IsError = true,
                                                Detail = $"Invalid escalate value '{escVal}', must be null or 'clan'."
                                            });
                                        }
                                        else
                                        {
                                            gEscalate = escVal;
                                        }
                                    }

                                    grudgesList.Add(new SituationGrudgeDef
                                    {
                                        From = gFrom,
                                        To = gTo,
                                        Amount = gAmount,
                                        LedgerOnly = gLedgerOnly,
                                        Escalate = gEscalate,
                                        Extra = extraDict
                                    });
                                }
                            }
                        }

                        branchesList.Add(new SituationBranchDef
                        {
                            Id = bId,
                            Base = bBase,
                            Traits = traitsDict,
                            Preconditions = precondsList,
                            Events = eventsList,
                            Grudges = grudgesList
                        });
                    }
                }

                allIssues.AddRange(situationIssues);

                bool hasErrors = situationIssues.Any(iss => iss.IsError);
                if (hasErrors)
                {
                    skippedCount++;
                }
                else
                {
                    acceptedSituations.Add(new SituationTemplate
                    {
                        Id = situationId,
                        Trigger = trigger,
                        Decider = decider,
                        MinBranchWeight = minBranchWeight,
                        Weight = weight,
                        DevOnly = devOnly,
                        Roles = rolesDict,
                        Conditions = conditionsList,
                        Branches = branchesList
                    });
                }
            }

            return new SituationCatalog(acceptedSituations, allIssues, skippedCount);
        }
    }
}
