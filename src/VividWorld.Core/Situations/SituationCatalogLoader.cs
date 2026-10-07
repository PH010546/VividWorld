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
            "cooldown",
            "traitAtLeast",
            "traitAtMost",
            "hasGrudge",
            "chance",
            "knowsEventAbout",
            "isLord"
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
            "id", "trigger", "decider", "minBranchWeight", "roles", "conditions", "branches", "weight", "devOnly",
            "maxPerDay", "eventTypes", "bindFromEvent", "enabledBy", "quotaGroup"
        };

        private static readonly HashSet<string> KnownRoleKeys = new(StringComparer.OrdinalIgnoreCase)
        {
            "derived", "optional", "allowDead", "grudgeLine", "nativeGrudgeLine", "anyTraitAtMost"
        };

        private static readonly HashSet<string> KnownConditionKeys = new(StringComparer.OrdinalIgnoreCase)
        {
            "type", "roles", "kinds", "a", "b", "role", "value", "op", "days",
            "trait", "line", "seedRoles", "about", "eventTypes"
        };

        private static readonly HashSet<string> KnownBranchKeys = new(StringComparer.OrdinalIgnoreCase)
        {
            "id", "base", "traits", "preconditions", "events", "grudges", "madeUpTalk", "grudgeWeight"
        };

        private static readonly HashSet<string> KnownQuotaGroupKeys = new(StringComparer.OrdinalIgnoreCase)
        {
            "id", "maxPerDay", "maxPerDayRef", "share"
        };

        private static readonly HashSet<string> KnownMadeUpTalkKeys = new(StringComparer.OrdinalIgnoreCase)
        {
            "kind", "teller", "listener", "about", "target"
        };

        private static readonly HashSet<string> KnownGrudgeWeightKeys = new(StringComparer.OrdinalIgnoreCase)
        {
            "from", "toward", "perPoint", "max"
        };

        private static readonly HashSet<string> KnownGrudgeKeys = new(StringComparer.OrdinalIgnoreCase)
        {
            "from", "to", "amount", "ledgerOnly", "escalate"
        };

        private static readonly HashSet<string> KnownBranchEventKeys = new(StringComparer.OrdinalIgnoreCase)
        {
            "type", "bind", "linkTo"
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
            var quotaGroupDefinitions = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
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
                string trimmedTrigger = rawTrigger?.Trim() ?? string.Empty;
                if (string.Equals(trimmedTrigger, "direct", StringComparison.Ordinal) ||
                    string.Equals(trimmedTrigger, "afterEvent", StringComparison.Ordinal))
                {
                    trigger = trimmedTrigger;
                }
                else
                {
                    situationIssues.Add(new SituationIssue
                    {
                        SituationIndex = i,
                        SituationId = situationId,
                        Field = "trigger",
                        Code = SituationIssueCode.BadTrigger,
                        IsError = true,
                        Detail = $"Situation trigger must be 'direct' or 'afterEvent', got '{rawTrigger}'."
                    });
                }

                // enabledBy (optional)
                string? enabledBy = null;
                var enabledByProp = sitObj.Property("enabledBy", StringComparison.OrdinalIgnoreCase);
                if (enabledByProp != null && enabledByProp.Value.Type != JTokenType.Null)
                {
                    string? rawRef = enabledByProp.Value.Value<string>()?.Trim();
                    if (!SituationConfigResolver.IsValidEnabledByRef(rawRef))
                    {
                        situationIssues.Add(new SituationIssue
                        {
                            SituationIndex = i,
                            SituationId = situationId,
                            Field = "enabledBy",
                            Code = SituationIssueCode.InvalidConfigReference,
                            IsError = true,
                            Detail = $"Invalid enabledBy reference '{rawRef}', only '@falseRumors.enabled' is allowed."
                        });
                    }
                    else
                    {
                        enabledBy = rawRef;
                    }
                }

                // maxPerDay (optional)
                double? maxPerDay = null;
                string? maxPerDayRef = null;
                var maxPerDayProp = sitObj.Property("maxPerDay", StringComparison.OrdinalIgnoreCase);
                if (maxPerDayProp != null && maxPerDayProp.Value.Type != JTokenType.Null)
                {
                    if (maxPerDayProp.Value.Type == JTokenType.String)
                    {
                        string? rawRef = maxPerDayProp.Value.Value<string>()?.Trim();
                        if (rawRef != null && rawRef.StartsWith("@", StringComparison.Ordinal))
                        {
                            if (!SituationConfigResolver.IsValidMaxPerDayRef(rawRef))
                            {
                                situationIssues.Add(new SituationIssue
                                {
                                    SituationIndex = i,
                                    SituationId = situationId,
                                    Field = "maxPerDay",
                                    Code = SituationIssueCode.InvalidConfigReference,
                                    IsError = true,
                                    Detail = $"Invalid maxPerDay reference '{rawRef}', only '@falseRumors.misconductPerDay' is allowed."
                                });
                            }
                            else
                            {
                                maxPerDayRef = rawRef;
                            }
                        }
                        else
                        {
                            situationIssues.Add(new SituationIssue
                            {
                                SituationIndex = i,
                                SituationId = situationId,
                                Field = "maxPerDay",
                                Code = SituationIssueCode.InvalidMaxPerDay,
                                IsError = true,
                                Detail = $"Invalid maxPerDay value '{rawRef}'."
                            });
                        }
                    }
                    else
                    {
                        try
                        {
                            double val = maxPerDayProp.Value.Value<double>();
                            if (double.IsNaN(val) || double.IsInfinity(val) || val <= 0.0)
                            {
                                situationIssues.Add(new SituationIssue
                                {
                                    SituationIndex = i,
                                    SituationId = situationId,
                                    Field = "maxPerDay",
                                    Code = SituationIssueCode.InvalidMaxPerDay,
                                    IsError = true,
                                    Detail = $"maxPerDay must be > 0.0, got {val}."
                                });
                            }
                            else
                            {
                                maxPerDay = val;
                            }
                        }
                        catch
                        {
                            situationIssues.Add(new SituationIssue
                            {
                                SituationIndex = i,
                                SituationId = situationId,
                                Field = "maxPerDay",
                                Code = SituationIssueCode.InvalidMaxPerDay,
                                IsError = true,
                                Detail = $"Invalid maxPerDay value '{maxPerDayProp.Value}'."
                            });
                        }
                    }
                }

                // quotaGroup (optional)
                SituationQuotaGroupDef? quotaGroup = null;
                var qgProp = sitObj.Property("quotaGroup", StringComparison.OrdinalIgnoreCase);
                if (qgProp != null && qgProp.Value.Type != JTokenType.Null)
                {
                    if (qgProp.Value is not JObject qgObj)
                    {
                        situationIssues.Add(new SituationIssue
                        {
                            SituationIndex = i,
                            SituationId = situationId,
                            Field = "quotaGroup",
                            Code = SituationIssueCode.InvalidQuotaGroup,
                            IsError = true,
                            Detail = "quotaGroup must be a JSON object."
                        });
                    }
                    else
                    {
                        foreach (var p in qgObj.Properties())
                        {
                            if (!KnownQuotaGroupKeys.Contains(p.Name))
                            {
                                situationIssues.Add(new SituationIssue
                                {
                                    SituationIndex = i,
                                    SituationId = situationId,
                                    Field = $"quotaGroup.{p.Name}",
                                    Code = SituationIssueCode.UnknownProperty,
                                    IsError = false,
                                    Detail = $"Unknown property '{p.Name}' in quotaGroup."
                                });
                            }
                        }

                        string qgId = qgObj["id"]?.Value<string>()?.Trim() ?? string.Empty;
                        if (string.IsNullOrEmpty(qgId))
                        {
                            situationIssues.Add(new SituationIssue
                            {
                                SituationIndex = i,
                                SituationId = situationId,
                                Field = "quotaGroup.id",
                                Code = SituationIssueCode.InvalidQuotaGroup,
                                IsError = true,
                                Detail = "quotaGroup id cannot be empty."
                            });
                        }

                        double? qgMaxPerDay = null;
                        string? qgMaxPerDayRef = null;
                        var qgMaxProp = qgObj.Property("maxPerDay", StringComparison.OrdinalIgnoreCase)
                                     ?? qgObj.Property("maxPerDayRef", StringComparison.OrdinalIgnoreCase);
                        if (qgMaxProp == null || qgMaxProp.Value.Type == JTokenType.Null)
                        {
                            situationIssues.Add(new SituationIssue
                            {
                                SituationIndex = i,
                                SituationId = situationId,
                                Field = "quotaGroup.maxPerDay",
                                Code = SituationIssueCode.InvalidQuotaGroup,
                                IsError = true,
                                Detail = "quotaGroup must specify maxPerDay or maxPerDayRef."
                            });
                        }
                        else if (qgMaxProp.Value.Type == JTokenType.String)
                        {
                            string? rawRef = qgMaxProp.Value.Value<string>()?.Trim();
                            if (rawRef != null && rawRef.StartsWith("@", StringComparison.Ordinal))
                            {
                                if (!SituationConfigResolver.IsValidMaxPerDayRef(rawRef))
                                {
                                    situationIssues.Add(new SituationIssue
                                    {
                                        SituationIndex = i,
                                        SituationId = situationId,
                                        Field = "quotaGroup.maxPerDay",
                                        Code = SituationIssueCode.InvalidConfigReference,
                                        IsError = true,
                                        Detail = $"Invalid maxPerDay reference '{rawRef}' in quotaGroup."
                                    });
                                }
                                else
                                {
                                    qgMaxPerDayRef = rawRef;
                                }
                            }
                            else
                            {
                                situationIssues.Add(new SituationIssue
                                {
                                    SituationIndex = i,
                                    SituationId = situationId,
                                    Field = "quotaGroup.maxPerDay",
                                    Code = SituationIssueCode.InvalidQuotaGroup,
                                    IsError = true,
                                    Detail = $"Invalid maxPerDay string '{rawRef}' in quotaGroup."
                                });
                            }
                        }
                        else
                        {
                            try
                            {
                                double val = qgMaxProp.Value.Value<double>();
                                if (double.IsNaN(val) || double.IsInfinity(val) || val <= 0.0)
                                {
                                    situationIssues.Add(new SituationIssue
                                    {
                                        SituationIndex = i,
                                        SituationId = situationId,
                                        Field = "quotaGroup.maxPerDay",
                                        Code = SituationIssueCode.InvalidQuotaGroup,
                                        IsError = true,
                                        Detail = $"quotaGroup maxPerDay must be > 0.0, got {val}."
                                    });
                                }
                                else
                                {
                                    qgMaxPerDay = val;
                                }
                            }
                            catch
                            {
                                situationIssues.Add(new SituationIssue
                                {
                                    SituationIndex = i,
                                    SituationId = situationId,
                                    Field = "quotaGroup.maxPerDay",
                                    Code = SituationIssueCode.InvalidQuotaGroup,
                                    IsError = true,
                                    Detail = $"Invalid quotaGroup maxPerDay '{qgMaxProp.Value}'."
                                });
                            }
                        }

                        // Consistency check across same quotaGroup.id
                        string defKey = qgMaxPerDayRef ?? qgMaxPerDay?.ToString(CultureInfo.InvariantCulture) ?? "";
                        if (!string.IsNullOrEmpty(qgId) && !string.IsNullOrEmpty(defKey))
                        {
                            if (quotaGroupDefinitions.TryGetValue(qgId, out var existingDef))
                            {
                                if (!string.Equals(existingDef, defKey, StringComparison.Ordinal))
                                {
                                    situationIssues.Add(new SituationIssue
                                    {
                                        SituationIndex = i,
                                        SituationId = situationId,
                                        Field = "quotaGroup.maxPerDay",
                                        Code = SituationIssueCode.InvalidQuotaGroup,
                                        IsError = true,
                                        Detail = $"Inconsistent maxPerDay for quotaGroup '{qgId}'. Expected '{existingDef}', got '{defKey}'."
                                    });
                                }
                            }
                            else
                            {
                                quotaGroupDefinitions[qgId] = defKey;
                            }
                        }

                        // share
                        double qgShare = 1.0;
                        string? qgShareRef = null;
                        var shareProp = qgObj.Property("share", StringComparison.OrdinalIgnoreCase);
                        if (shareProp != null && shareProp.Value.Type != JTokenType.Null)
                        {
                            if (shareProp.Value.Type == JTokenType.String)
                            {
                                string? rawShareRef = shareProp.Value.Value<string>()?.Trim();
                                if (rawShareRef != null && rawShareRef.StartsWith("@", StringComparison.Ordinal))
                                {
                                    if (!SituationConfigResolver.IsValidShareRef(rawShareRef))
                                    {
                                        situationIssues.Add(new SituationIssue
                                        {
                                            SituationIndex = i,
                                            SituationId = situationId,
                                            Field = "quotaGroup.share",
                                            Code = SituationIssueCode.InvalidConfigReference,
                                            IsError = true,
                                            Detail = $"Invalid share reference '{rawShareRef}' in quotaGroup."
                                        });
                                    }
                                    else
                                    {
                                        qgShareRef = rawShareRef;
                                        qgShare = SituationConfigResolver.ResolveDouble(rawShareRef, null);
                                    }
                                }
                                else
                                {
                                    situationIssues.Add(new SituationIssue
                                    {
                                        SituationIndex = i,
                                        SituationId = situationId,
                                        Field = "quotaGroup.share",
                                        Code = SituationIssueCode.InvalidQuotaGroup,
                                        IsError = true,
                                        Detail = $"Invalid share value '{rawShareRef}' in quotaGroup."
                                    });
                                }
                            }
                            else
                            {
                                try
                                {
                                    double sVal = shareProp.Value.Value<double>();
                                    if (double.IsNaN(sVal) || double.IsInfinity(sVal) || sVal < 0.0)
                                    {
                                        situationIssues.Add(new SituationIssue
                                        {
                                            SituationIndex = i,
                                            SituationId = situationId,
                                            Field = "quotaGroup.share",
                                            Code = SituationIssueCode.InvalidQuotaGroup,
                                            IsError = true,
                                            Detail = $"quotaGroup share must be >= 0.0, got {sVal}."
                                        });
                                    }
                                    else
                                    {
                                        qgShare = sVal;
                                    }
                                }
                                catch
                                {
                                    situationIssues.Add(new SituationIssue
                                    {
                                        SituationIndex = i,
                                        SituationId = situationId,
                                        Field = "quotaGroup.share",
                                        Code = SituationIssueCode.InvalidQuotaGroup,
                                        IsError = true,
                                        Detail = $"Invalid quotaGroup share '{shareProp.Value}'."
                                    });
                                }
                            }
                        }

                        quotaGroup = new SituationQuotaGroupDef
                        {
                            Id = qgId,
                            MaxPerDay = qgMaxPerDay,
                            MaxPerDayRef = qgMaxPerDayRef,
                            ShareRef = qgShareRef,
                            Share = qgShare
                        };
                    }
                }

                // afterEvent properties
                List<string>? eventTypesList = null;
                Dictionary<string, string>? bindFromEventDict = null;

                if (string.Equals(trigger, "afterEvent", StringComparison.Ordinal))
                {
                    var etProp = sitObj.Property("eventTypes", StringComparison.OrdinalIgnoreCase);
                    if (etProp == null || etProp.Value is not JArray etArray || etArray.Count == 0)
                    {
                        situationIssues.Add(new SituationIssue
                        {
                            SituationIndex = i,
                            SituationId = situationId,
                            Field = "eventTypes",
                            Code = SituationIssueCode.MissingEventTypes,
                            IsError = true,
                            Detail = "afterEvent situation must specify 'eventTypes' with at least one event type."
                        });
                    }
                    else
                    {
                        eventTypesList = new List<string>();
                        foreach (var token in etArray)
                        {
                            string et = token.Value<string>()?.Trim() ?? string.Empty;
                            if (!string.IsNullOrEmpty(et))
                            {
                                eventTypesList.Add(et);
                            }
                        }
                        if (eventTypesList.Count == 0)
                        {
                            situationIssues.Add(new SituationIssue
                            {
                                SituationIndex = i,
                                SituationId = situationId,
                                Field = "eventTypes",
                                Code = SituationIssueCode.MissingEventTypes,
                                IsError = true,
                                Detail = "afterEvent situation must specify 'eventTypes' with at least one non-empty event type."
                            });
                        }
                    }

                    var bfeProp = sitObj.Property("bindFromEvent", StringComparison.OrdinalIgnoreCase);
                    if (bfeProp == null || bfeProp.Value is not JObject bfeObj || !bfeObj.Properties().Any())
                    {
                        situationIssues.Add(new SituationIssue
                        {
                            SituationIndex = i,
                            SituationId = situationId,
                            Field = "bindFromEvent",
                            Code = SituationIssueCode.MissingBindFromEvent,
                            IsError = true,
                            Detail = "afterEvent situation must specify 'bindFromEvent' mapping situation roles to event placeholders."
                        });
                    }
                    else
                    {
                        bindFromEventDict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                        foreach (var p in bfeObj.Properties())
                        {
                            bindFromEventDict[p.Name.Trim()] = p.Value.Value<string>()?.Trim() ?? string.Empty;
                        }
                    }
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
                                string trimmedDerived = derived!.Trim();
                                if (string.Equals(trimmedDerived, "settlementOwnerClanLeader", StringComparison.Ordinal))
                                {
                                    rDef.Derived = trimmedDerived;
                                }
                                else
                                {
                                    var parsedStrategy = AbsentRoleSelector.ParseStrategy(trimmedDerived);
                                    if (parsedStrategy == null)
                                    {
                                        situationIssues.Add(new SituationIssue
                                        {
                                            SituationIndex = i,
                                            SituationId = situationId,
                                            Field = $"roles.{roleName}.derived",
                                            Code = SituationIssueCode.UnknownDerivedRole,
                                            IsError = true,
                                            Detail = $"Unknown derived role '{derived}'."
                                        });
                                    }
                                    else
                                    {
                                        rDef.Derived = trimmedDerived;
                                    }
                                }
                            }

                            bool? opt = rObj["optional"]?.Value<bool>();
                            if (opt.HasValue)
                            {
                                rDef.Optional = opt.Value;
                            }

                            bool? allowDead = rObj["allowDead"]?.Value<bool>();
                            if (allowDead.HasValue)
                            {
                                rDef.AllowDead = allowDead.Value;
                            }

                            var grudgeLineTok = rObj["grudgeLine"];
                            if (grudgeLineTok != null && grudgeLineTok.Type != JTokenType.Null)
                            {
                                if (grudgeLineTok.Type == JTokenType.String)
                                {
                                    string? gRef = grudgeLineTok.Value<string>()?.Trim();
                                    if (gRef != null && gRef.StartsWith("@", StringComparison.Ordinal))
                                    {
                                        if (!SituationConfigResolver.IsValidGrudgeLineRef(gRef))
                                        {
                                            situationIssues.Add(new SituationIssue
                                            {
                                                SituationIndex = i,
                                                SituationId = situationId,
                                                Field = $"roles.{roleName}.grudgeLine",
                                                Code = SituationIssueCode.InvalidConfigReference,
                                                IsError = true,
                                                Detail = $"Invalid grudgeLine reference '{gRef}', only '@falseRumors.poisonGrudgeLine' is allowed."
                                            });
                                        }
                                        else
                                        {
                                            rDef.GrudgeLineRef = gRef;
                                        }
                                    }
                                    else
                                    {
                                        situationIssues.Add(new SituationIssue
                                        {
                                            SituationIndex = i,
                                            SituationId = situationId,
                                            Field = $"roles.{roleName}.grudgeLine",
                                            Code = SituationIssueCode.InvalidConditionValue,
                                            IsError = true,
                                            Detail = $"Invalid grudgeLine value '{gRef}'."
                                        });
                                    }
                                }
                                else
                                {
                                    try
                                    {
                                        rDef.GrudgeLine = grudgeLineTok.Value<int>();
                                    }
                                    catch
                                    {
                                        situationIssues.Add(new SituationIssue
                                        {
                                            SituationIndex = i,
                                            SituationId = situationId,
                                            Field = $"roles.{roleName}.grudgeLine",
                                            Code = SituationIssueCode.InvalidConditionValue,
                                            IsError = true,
                                            Detail = "grudgeLine must be an integer or valid @ reference."
                                        });
                                    }
                                }
                            }

                            var nativeGrudgeLineTok = rObj["nativeGrudgeLine"];
                            if (nativeGrudgeLineTok != null && nativeGrudgeLineTok.Type != JTokenType.Null)
                            {
                                if (nativeGrudgeLineTok.Type == JTokenType.String)
                                {
                                    string? gRef = nativeGrudgeLineTok.Value<string>()?.Trim();
                                    if (gRef != null && gRef.StartsWith("@", StringComparison.Ordinal))
                                    {
                                        if (!SituationConfigResolver.IsValidNativeGrudgeLineRef(gRef))
                                        {
                                            situationIssues.Add(new SituationIssue
                                            {
                                                SituationIndex = i,
                                                SituationId = situationId,
                                                Field = $"roles.{roleName}.nativeGrudgeLine",
                                                Code = SituationIssueCode.InvalidConfigReference,
                                                IsError = true,
                                                Detail = $"Invalid nativeGrudgeLine reference '{gRef}', only '@falseRumors.poisonNativeGrudgeLine' is allowed."
                                            });
                                        }
                                        else
                                        {
                                            rDef.NativeGrudgeLineRef = gRef;
                                        }
                                    }
                                    else
                                    {
                                        situationIssues.Add(new SituationIssue
                                        {
                                            SituationIndex = i,
                                            SituationId = situationId,
                                            Field = $"roles.{roleName}.nativeGrudgeLine",
                                            Code = SituationIssueCode.InvalidConditionValue,
                                            IsError = true,
                                            Detail = $"Invalid nativeGrudgeLine value '{gRef}'."
                                        });
                                    }
                                }
                                else
                                {
                                    try
                                    {
                                        rDef.NativeGrudgeLine = nativeGrudgeLineTok.Value<int>();
                                    }
                                    catch
                                    {
                                        situationIssues.Add(new SituationIssue
                                        {
                                            SituationIndex = i,
                                            SituationId = situationId,
                                            Field = $"roles.{roleName}.nativeGrudgeLine",
                                            Code = SituationIssueCode.InvalidConditionValue,
                                            IsError = true,
                                            Detail = "nativeGrudgeLine must be an integer or valid @ reference."
                                        });
                                    }
                                }
                            }

                            var anyTraitTok = rObj["anyTraitAtMost"];
                            if (anyTraitTok is JObject anyTraitObj)
                            {
                                var traitsDict = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                                foreach (var p in anyTraitObj.Properties())
                                {
                                    if (!ValidTraits.Contains(p.Name))
                                    {
                                        situationIssues.Add(new SituationIssue
                                        {
                                            SituationIndex = i,
                                            SituationId = situationId,
                                            Field = $"roles.{roleName}.anyTraitAtMost.{p.Name}",
                                            Code = SituationIssueCode.UnknownTraitName,
                                            IsError = true,
                                            Detail = $"Unknown trait '{p.Name}' in anyTraitAtMost."
                                        });
                                    }
                                    else
                                    {
                                        try
                                        {
                                            traitsDict[p.Name] = p.Value.Value<int>();
                                        }
                                        catch
                                        {
                                            situationIssues.Add(new SituationIssue
                                            {
                                                SituationIndex = i,
                                                SituationId = situationId,
                                                Field = $"roles.{roleName}.anyTraitAtMost.{p.Name}",
                                                Code = SituationIssueCode.InvalidConditionValue,
                                                IsError = true,
                                                Detail = $"Value for trait '{p.Name}' in anyTraitAtMost must be an integer."
                                            });
                                        }
                                    }
                                }
                                rDef.AnyTraitAtMost = traitsDict;
                            }
                        }

                        if (!rDef.IsDerived)
                        {
                            nonDerivedCount++;
                        }

                        rolesDict[roleName] = rDef;
                    }

                    foreach (var rKvp in rolesDict)
                    {
                        if (rKvp.Value.IsDerived && !string.Equals(rKvp.Value.Derived, "settlementOwnerClanLeader", StringComparison.Ordinal))
                        {
                            var parsedStrategy = AbsentRoleSelector.ParseStrategy(rKvp.Value.Derived);
                            if (parsedStrategy != null)
                            {
                                if (!rolesDict.ContainsKey(parsedStrategy.Value.TargetRole))
                                {
                                    situationIssues.Add(new SituationIssue
                                    {
                                        SituationIndex = i,
                                        SituationId = situationId,
                                        Field = $"roles.{rKvp.Key}.derived",
                                        Code = SituationIssueCode.UnknownRoleInDerivedRole,
                                        IsError = true,
                                        Detail = $"Target role '{parsedStrategy.Value.TargetRole}' in derived strategy '{rKvp.Value.Derived}' is not defined in roles."
                                    });
                                }
                            }
                        }
                    }

                    int minNonDerived = string.Equals(trigger, "afterEvent", StringComparison.Ordinal) ? 1 : 2;
                    if (nonDerivedCount < minNonDerived)
                    {
                        situationIssues.Add(new SituationIssue
                        {
                            SituationIndex = i,
                            SituationId = situationId,
                            Field = "roles",
                            Code = SituationIssueCode.InsufficientRoles,
                            IsError = true,
                            Detail = $"Situation must define at least {minNonDerived} non-derived role(s), found {nonDerivedCount}."
                        });
                    }

                    if (string.Equals(trigger, "afterEvent", StringComparison.Ordinal) && bindFromEventDict != null)
                    {
                        foreach (var kvp in bindFromEventDict)
                        {
                            if (!rolesDict.TryGetValue(kvp.Key, out var rDef))
                            {
                                situationIssues.Add(new SituationIssue
                                {
                                    SituationIndex = i,
                                    SituationId = situationId,
                                    Field = $"bindFromEvent.{kvp.Key}",
                                    Code = SituationIssueCode.UnknownRoleInBindFromEvent,
                                    IsError = true,
                                    Detail = $"Role '{kvp.Key}' in bindFromEvent is not defined in roles."
                                });
                            }
                            else if (rDef.IsDerived)
                            {
                                situationIssues.Add(new SituationIssue
                                {
                                    SituationIndex = i,
                                    SituationId = situationId,
                                    Field = $"bindFromEvent.{kvp.Key}",
                                    Code = SituationIssueCode.DerivedRoleInBindFromEvent,
                                    IsError = true,
                                    Detail = $"Role '{kvp.Key}' in bindFromEvent is a derived role; only non-derived roles can be bound from event."
                                });
                            }
                        }

                        foreach (var rKvp in rolesDict)
                        {
                            if (!rKvp.Value.IsDerived && !bindFromEventDict.ContainsKey(rKvp.Key))
                            {
                                situationIssues.Add(new SituationIssue
                                {
                                    SituationIndex = i,
                                    SituationId = situationId,
                                    Field = $"bindFromEvent.{rKvp.Key}",
                                    Code = SituationIssueCode.MissingRoleInBindFromEvent,
                                    IsError = true,
                                    Detail = $"Non-derived role '{rKvp.Key}' must be mapped in bindFromEvent."
                                });
                            }
                        }
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
                    else if (deciderDef.IsDerived && !string.Equals(trigger, "afterEvent", StringComparison.OrdinalIgnoreCase))
                    {
                        situationIssues.Add(new SituationIssue
                        {
                            SituationIndex = i,
                            SituationId = situationId,
                            Field = "decider",
                            Code = SituationIssueCode.DeciderIsDerived,
                            IsError = true,
                            Detail = $"decider '{decider}' cannot be a derived role in direct situations."
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

                        var condDef = ParseCondition(
                            cObj,
                            i,
                            situationId,
                            $"conditions[{cIdx}]",
                            isPrecondition: false,
                            rolesDict,
                            situationIssues);

                        if (condDef != null)
                        {
                            conditionsList.Add(condDef);
                        }
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

                                var pDef = ParseCondition(
                                    pObj,
                                    i,
                                    situationId,
                                    $"branches[{bIdx}].preconditions[{pIdx}]",
                                    isPrecondition: true,
                                    rolesDict,
                                    situationIssues);

                                if (pDef != null)
                                {
                                    precondsList.Add(pDef);
                                }
                            }
                        }

                        var eventsList = new List<SituationBranchEventDef>();
                        if (bObj["events"] is not JArray eventsArray)
                        {
                            situationIssues.Add(new SituationIssue
                            {
                                SituationIndex = i,
                                SituationId = situationId,
                                Field = $"branches[{bIdx}].events",
                                Code = SituationIssueCode.NoBranchEvents,
                                IsError = true,
                                Detail = "Branch must define 'events' array."
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

                                string? linkTo = null;
                                if (eObj.TryGetValue("linkTo", out var linkToToken) && linkToToken.Type != JTokenType.Null)
                                {
                                    linkTo = linkToToken.Value<string>()?.Trim();
                                    if (!string.Equals(linkTo, "@trigger", StringComparison.Ordinal))
                                    {
                                        situationIssues.Add(new SituationIssue
                                        {
                                            SituationIndex = i,
                                            SituationId = situationId,
                                            Field = $"branches[{bIdx}].events[{eIdx}].linkTo",
                                            Code = SituationIssueCode.InvalidLinkTo,
                                            IsError = true,
                                            Detail = $"linkTo must be '@trigger', got '{linkTo}'."
                                        });
                                    }
                                }

                                eventsList.Add(new SituationBranchEventDef
                                {
                                    Type = eType,
                                    LinkTo = linkTo,
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

                        SituationMadeUpTalkDef? madeUpTalk = null;
                        if (bObj.TryGetValue("madeUpTalk", out var mutToken) && mutToken.Type != JTokenType.Null)
                        {
                            if (mutToken is not JObject mutObj)
                            {
                                situationIssues.Add(new SituationIssue
                                {
                                    SituationIndex = i,
                                    SituationId = situationId,
                                    Field = $"branches[{bIdx}].madeUpTalk",
                                    Code = SituationIssueCode.InvalidMadeUpTalk,
                                    IsError = true,
                                    Detail = "madeUpTalk must be a JSON object."
                                });
                            }
                            else
                            {
                                if (eventsList.Count > 0)
                                {
                                    situationIssues.Add(new SituationIssue
                                    {
                                        SituationIndex = i,
                                        SituationId = situationId,
                                        Field = $"branches[{bIdx}].events",
                                        Code = SituationIssueCode.InvalidMadeUpTalk,
                                        IsError = true,
                                        Detail = "Branch with madeUpTalk must have an empty 'events' array ([])."
                                    });
                                }

                                foreach (var p in mutObj.Properties())
                                {
                                    if (!KnownMadeUpTalkKeys.Contains(p.Name))
                                    {
                                        situationIssues.Add(new SituationIssue
                                        {
                                            SituationIndex = i,
                                            SituationId = situationId,
                                            Field = $"branches[{bIdx}].madeUpTalk.{p.Name}",
                                            Code = SituationIssueCode.UnknownProperty,
                                            IsError = false,
                                            Detail = $"Unknown property '{p.Name}' in madeUpTalk."
                                        });
                                    }
                                }

                                string kind = mutObj["kind"]?.Value<string>()?.Trim()?.ToLowerInvariant() ?? string.Empty;
                                if (kind != "slander" && kind != "praise")
                                {
                                    situationIssues.Add(new SituationIssue
                                    {
                                        SituationIndex = i,
                                        SituationId = situationId,
                                        Field = $"branches[{bIdx}].madeUpTalk.kind",
                                        Code = SituationIssueCode.InvalidMadeUpTalk,
                                        IsError = true,
                                        Detail = $"madeUpTalk kind must be 'slander' or 'praise', got '{kind}'."
                                    });
                                }

                                string teller = mutObj["teller"]?.Value<string>()?.Trim() ?? string.Empty;
                                if (string.IsNullOrEmpty(teller) || !rolesDict.ContainsKey(teller))
                                {
                                    situationIssues.Add(new SituationIssue
                                    {
                                        SituationIndex = i,
                                        SituationId = situationId,
                                        Field = $"branches[{bIdx}].madeUpTalk.teller",
                                        Code = SituationIssueCode.InvalidMadeUpTalk,
                                        IsError = true,
                                        Detail = $"madeUpTalk teller role '{teller}' is not defined in situation roles."
                                    });
                                }

                                string listener = mutObj["listener"]?.Value<string>()?.Trim() ?? string.Empty;
                                if (string.IsNullOrEmpty(listener) || !rolesDict.ContainsKey(listener))
                                {
                                    situationIssues.Add(new SituationIssue
                                    {
                                        SituationIndex = i,
                                        SituationId = situationId,
                                        Field = $"branches[{bIdx}].madeUpTalk.listener",
                                        Code = SituationIssueCode.InvalidMadeUpTalk,
                                        IsError = true,
                                        Detail = $"madeUpTalk listener role '{listener}' is not defined in situation roles."
                                    });
                                }

                                string target = mutObj["target"]?.Value<string>()?.Trim()
                                             ?? mutObj["about"]?.Value<string>()?.Trim() ?? string.Empty;
                                if (string.IsNullOrEmpty(target) || !rolesDict.ContainsKey(target))
                                {
                                    situationIssues.Add(new SituationIssue
                                    {
                                        SituationIndex = i,
                                        SituationId = situationId,
                                        Field = $"branches[{bIdx}].madeUpTalk.target",
                                        Code = SituationIssueCode.InvalidMadeUpTalk,
                                        IsError = true,
                                        Detail = $"madeUpTalk target role '{target}' is not defined in situation roles."
                                    });
                                }

                                string? counterpart = mutObj["counterpart"]?.Value<string>()?.Trim();
                                if (!string.IsNullOrEmpty(counterpart) && !rolesDict.ContainsKey(counterpart!))
                                {
                                    situationIssues.Add(new SituationIssue
                                    {
                                        SituationIndex = i,
                                        SituationId = situationId,
                                        Field = $"branches[{bIdx}].madeUpTalk.counterpart",
                                        Code = SituationIssueCode.InvalidMadeUpTalk,
                                        IsError = true,
                                        Detail = $"madeUpTalk counterpart role '{counterpart}' is not defined in situation roles."
                                    });
                                }

                                madeUpTalk = new SituationMadeUpTalkDef
                                {
                                    Kind = kind,
                                    Teller = teller,
                                    Listener = listener,
                                    Target = target,
                                    CounterpartRole = counterpart
                                };
                            }
                        }

                        SituationGrudgeWeightDef? grudgeWeight = null;
                        if (bObj.TryGetValue("grudgeWeight", out var gwToken) && gwToken.Type != JTokenType.Null)
                        {
                            if (gwToken is not JObject gwObj)
                            {
                                situationIssues.Add(new SituationIssue
                                {
                                    SituationIndex = i,
                                    SituationId = situationId,
                                    Field = $"branches[{bIdx}].grudgeWeight",
                                    Code = SituationIssueCode.InvalidGrudgeWeight,
                                    IsError = true,
                                    Detail = "grudgeWeight must be a JSON object."
                                });
                            }
                            else
                            {
                                foreach (var p in gwObj.Properties())
                                {
                                    if (!KnownGrudgeWeightKeys.Contains(p.Name))
                                    {
                                        situationIssues.Add(new SituationIssue
                                        {
                                            SituationIndex = i,
                                            SituationId = situationId,
                                            Field = $"branches[{bIdx}].grudgeWeight.{p.Name}",
                                            Code = SituationIssueCode.UnknownProperty,
                                            IsError = false,
                                            Detail = $"Unknown property '{p.Name}' in grudgeWeight."
                                        });
                                    }
                                }

                                string gwFrom = gwObj["from"]?.Value<string>()?.Trim() ?? string.Empty;
                                if (string.IsNullOrEmpty(gwFrom) || !rolesDict.ContainsKey(gwFrom))
                                {
                                    situationIssues.Add(new SituationIssue
                                    {
                                        SituationIndex = i,
                                        SituationId = situationId,
                                        Field = $"branches[{bIdx}].grudgeWeight.from",
                                        Code = SituationIssueCode.InvalidGrudgeWeight,
                                        IsError = true,
                                        Detail = $"grudgeWeight 'from' role '{gwFrom}' is not defined in situation roles."
                                    });
                                }

                                string gwToward = gwObj["toward"]?.Value<string>()?.Trim() ?? string.Empty;
                                if (string.IsNullOrEmpty(gwToward) || !rolesDict.ContainsKey(gwToward))
                                {
                                    situationIssues.Add(new SituationIssue
                                    {
                                        SituationIndex = i,
                                        SituationId = situationId,
                                        Field = $"branches[{bIdx}].grudgeWeight.toward",
                                        Code = SituationIssueCode.InvalidGrudgeWeight,
                                        IsError = true,
                                        Detail = $"grudgeWeight 'toward' role '{gwToward}' is not defined in situation roles."
                                    });
                                }

                                double perPoint = 0.0;
                                double max = 0.0;
                                try
                                {
                                    perPoint = gwObj["perPoint"]?.Value<double>() ?? 0.0;
                                    max = gwObj["max"]?.Value<double>() ?? 0.0;
                                }
                                catch
                                {
                                    situationIssues.Add(new SituationIssue
                                    {
                                        SituationIndex = i,
                                        SituationId = situationId,
                                        Field = $"branches[{bIdx}].grudgeWeight",
                                        Code = SituationIssueCode.InvalidGrudgeWeight,
                                        IsError = true,
                                        Detail = "perPoint and max in grudgeWeight must be numbers."
                                    });
                                }

                                grudgeWeight = new SituationGrudgeWeightDef
                                {
                                    From = gwFrom,
                                    Toward = gwToward,
                                    PerPoint = perPoint,
                                    Max = max
                                };
                            }
                        }

                        branchesList.Add(new SituationBranchDef
                        {
                            Id = bId,
                            Base = bBase,
                            Traits = traitsDict,
                            Preconditions = precondsList,
                            Events = eventsList,
                            Grudges = grudgesList,
                            MadeUpTalk = madeUpTalk,
                            GrudgeWeight = grudgeWeight
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
                        EnabledBy = enabledBy,
                        MaxPerDayRef = maxPerDayRef,
                        MaxPerDay = maxPerDay,
                        QuotaGroup = quotaGroup,
                        EventTypes = eventTypesList,
                        BindFromEvent = bindFromEventDict,
                        Roles = rolesDict,
                        Conditions = conditionsList,
                        Branches = branchesList
                    });
                }
            }

            return new SituationCatalog(acceptedSituations, allIssues, skippedCount);
        }

        private static SituationConditionDef? ParseCondition(
            JObject cObj,
            int sitIndex,
            string sitId,
            string fieldPrefix,
            bool isPrecondition,
            Dictionary<string, SituationRoleDef> rolesDict,
            List<SituationIssue> situationIssues)
        {
            foreach (var p in cObj.Properties())
            {
                if (!KnownConditionKeys.Contains(p.Name))
                {
                    situationIssues.Add(new SituationIssue
                    {
                        SituationIndex = sitIndex,
                        SituationId = sitId,
                        Field = $"{fieldPrefix}.{p.Name}",
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
                    SituationIndex = sitIndex,
                    SituationId = sitId,
                    Field = $"{fieldPrefix}.type",
                    Code = isPrecondition ? SituationIssueCode.UnknownPreconditionType : SituationIssueCode.UnknownConditionType,
                    IsError = true,
                    Detail = isPrecondition
                        ? $"Unknown precondition type '{cType}'."
                        : $"Unknown condition type '{cType}'."
                });
                return null;
            }

            var roleIssueCode = isPrecondition
                ? SituationIssueCode.UnknownRoleInPrecondition
                : SituationIssueCode.UnknownRoleInCondition;

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
                            SituationIndex = sitIndex,
                            SituationId = sitId,
                            Field = $"{fieldPrefix}.roles",
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
                                    SituationIndex = sitIndex,
                                    SituationId = sitId,
                                    Field = $"{fieldPrefix}.roles",
                                    Code = roleIssueCode,
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
                            SituationIndex = sitIndex,
                            SituationId = sitId,
                            Field = $"{fieldPrefix}.kinds",
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
                                    SituationIndex = sitIndex,
                                    SituationId = sitId,
                                    Field = $"{fieldPrefix}.kinds",
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
                            SituationIndex = sitIndex,
                            SituationId = sitId,
                            Field = $"{fieldPrefix}",
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
                                SituationIndex = sitIndex,
                                SituationId = sitId,
                                Field = $"{fieldPrefix}.a",
                                Code = roleIssueCode,
                                IsError = true,
                                Detail = $"Unknown role '{a}' in {cType} condition."
                            });
                        }
                        if (!rolesDict.ContainsKey(b))
                        {
                            situationIssues.Add(new SituationIssue
                            {
                                SituationIndex = sitIndex,
                                SituationId = sitId,
                                Field = $"{fieldPrefix}.b",
                                Code = roleIssueCode,
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
                    var vToken = cObj["value"];
                    bool? val = (vToken != null && vToken.Type == JTokenType.Boolean) ? vToken.Value<bool>() : null;
                    if (string.IsNullOrEmpty(r) || !val.HasValue)
                    {
                        situationIssues.Add(new SituationIssue
                        {
                            SituationIndex = sitIndex,
                            SituationId = sitId,
                            Field = $"{fieldPrefix}",
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
                                SituationIndex = sitIndex,
                                SituationId = sitId,
                                Field = $"{fieldPrefix}.role",
                                Code = roleIssueCode,
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
                            SituationIndex = sitIndex,
                            SituationId = sitId,
                            Field = $"{fieldPrefix}",
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
                                SituationIndex = sitIndex,
                                SituationId = sitId,
                                Field = $"{fieldPrefix}.a",
                                Code = roleIssueCode,
                                IsError = true,
                                Detail = $"Unknown role '{a}' in clanTierCompare condition."
                            });
                        }
                        if (!rolesDict.ContainsKey(b))
                        {
                            situationIssues.Add(new SituationIssue
                            {
                                SituationIndex = sitIndex,
                                SituationId = sitId,
                                Field = $"{fieldPrefix}.b",
                                Code = roleIssueCode,
                                IsError = true,
                                Detail = $"Unknown role '{b}' in clanTierCompare condition."
                            });
                        }
                        if (!ValidComparisonOps.Contains(op))
                        {
                            situationIssues.Add(new SituationIssue
                            {
                                SituationIndex = sitIndex,
                                SituationId = sitId,
                                Field = $"{fieldPrefix}.op",
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
                            SituationIndex = sitIndex,
                            SituationId = sitId,
                            Field = $"{fieldPrefix}.role",
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
                                SituationIndex = sitIndex,
                                SituationId = sitId,
                                Field = $"{fieldPrefix}.role",
                                Code = roleIssueCode,
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
                            SituationIndex = sitIndex,
                            SituationId = sitId,
                            Field = $"{fieldPrefix}.days",
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
                                    SituationIndex = sitIndex,
                                    SituationId = sitId,
                                    Field = $"{fieldPrefix}.days",
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
                                SituationIndex = sitIndex,
                                SituationId = sitId,
                                Field = $"{fieldPrefix}.days",
                                Code = SituationIssueCode.MissingConditionProperty,
                                IsError = true,
                                Detail = "cooldown condition 'days' must be a numeric value."
                            });
                        }
                    }
                    break;
                }

                case "traitatleast":
                case "traitatmost":
                {
                    string r = cObj["role"]?.Value<string>()?.Trim() ?? string.Empty;
                    if (string.IsNullOrEmpty(r))
                    {
                        situationIssues.Add(new SituationIssue
                        {
                            SituationIndex = sitIndex,
                            SituationId = sitId,
                            Field = $"{fieldPrefix}.role",
                            Code = SituationIssueCode.MissingConditionProperty,
                            IsError = true,
                            Detail = $"{cType} condition requires 'role'."
                        });
                    }
                    else if (!rolesDict.ContainsKey(r))
                    {
                        situationIssues.Add(new SituationIssue
                        {
                            SituationIndex = sitIndex,
                            SituationId = sitId,
                            Field = $"{fieldPrefix}.role",
                            Code = roleIssueCode,
                            IsError = true,
                            Detail = $"Unknown role '{r}' in {cType} condition."
                        });
                    }
                    else
                    {
                        condDef.Role = r;
                    }

                    string trait = cObj["trait"]?.Value<string>()?.Trim() ?? string.Empty;
                    if (string.IsNullOrEmpty(trait))
                    {
                        situationIssues.Add(new SituationIssue
                        {
                            SituationIndex = sitIndex,
                            SituationId = sitId,
                            Field = $"{fieldPrefix}.trait",
                            Code = SituationIssueCode.MissingConditionProperty,
                            IsError = true,
                            Detail = $"{cType} condition requires 'trait'."
                        });
                    }
                    else if (!ValidTraits.Contains(trait))
                    {
                        situationIssues.Add(new SituationIssue
                        {
                            SituationIndex = sitIndex,
                            SituationId = sitId,
                            Field = $"{fieldPrefix}.trait",
                            Code = SituationIssueCode.UnknownTraitName,
                            IsError = true,
                            Detail = $"Unknown trait '{trait}' in {cType} condition, allowed traits: honor, mercy, valor, calculating, generosity."
                        });
                    }
                    else
                    {
                        condDef.Trait = trait;
                    }

                    var vToken = cObj["value"];
                    if (vToken == null || vToken.Type == JTokenType.Null)
                    {
                        situationIssues.Add(new SituationIssue
                        {
                            SituationIndex = sitIndex,
                            SituationId = sitId,
                            Field = $"{fieldPrefix}.value",
                            Code = SituationIssueCode.MissingConditionProperty,
                            IsError = true,
                            Detail = $"{cType} condition requires integer 'value'."
                        });
                    }
                    else
                    {
                        try
                        {
                            double dVal = vToken.Value<double>();
                            int iVal = (int)dVal;
                            if (double.IsNaN(dVal) || double.IsInfinity(dVal) || Math.Abs(dVal - iVal) > 1e-6 || iVal < -2 || iVal > 2)
                            {
                                situationIssues.Add(new SituationIssue
                                {
                                    SituationIndex = sitIndex,
                                    SituationId = sitId,
                                    Field = $"{fieldPrefix}.value",
                                    Code = SituationIssueCode.InvalidConditionValue,
                                    IsError = true,
                                    Detail = $"{cType} condition 'value' must be an integer between -2 and 2, got {vToken}."
                                });
                            }
                            else
                            {
                                condDef.Number = iVal;
                            }
                        }
                        catch
                        {
                            situationIssues.Add(new SituationIssue
                            {
                                SituationIndex = sitIndex,
                                SituationId = sitId,
                                Field = $"{fieldPrefix}.value",
                                Code = SituationIssueCode.InvalidConditionValue,
                                IsError = true,
                                Detail = $"{cType} condition 'value' must be an integer between -2 and 2, got {vToken}."
                            });
                        }
                    }
                    break;
                }

                case "hasgrudge":
                {
                    string a = cObj["a"]?.Value<string>()?.Trim() ?? string.Empty;
                    string b = cObj["b"]?.Value<string>()?.Trim() ?? string.Empty;
                    if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b))
                    {
                        situationIssues.Add(new SituationIssue
                        {
                            SituationIndex = sitIndex,
                            SituationId = sitId,
                            Field = $"{fieldPrefix}",
                            Code = SituationIssueCode.MissingConditionProperty,
                            IsError = true,
                            Detail = "hasGrudge condition requires 'a' and 'b' role names."
                        });
                    }
                    else
                    {
                        if (!rolesDict.ContainsKey(a))
                        {
                            situationIssues.Add(new SituationIssue
                            {
                                SituationIndex = sitIndex,
                                SituationId = sitId,
                                Field = $"{fieldPrefix}.a",
                                Code = roleIssueCode,
                                IsError = true,
                                Detail = $"Unknown role '{a}' in hasGrudge condition."
                            });
                        }
                        if (!rolesDict.ContainsKey(b))
                        {
                            situationIssues.Add(new SituationIssue
                            {
                                SituationIndex = sitIndex,
                                SituationId = sitId,
                                Field = $"{fieldPrefix}.b",
                                Code = roleIssueCode,
                                IsError = true,
                                Detail = $"Unknown role '{b}' in hasGrudge condition."
                            });
                        }
                        condDef.A = a;
                        condDef.B = b;
                    }

                    var lineToken = cObj["line"];
                    if (lineToken != null && lineToken.Type != JTokenType.Null)
                    {
                        try
                        {
                            condDef.Line = lineToken.Value<int>();
                        }
                        catch
                        {
                            situationIssues.Add(new SituationIssue
                            {
                                SituationIndex = sitIndex,
                                SituationId = sitId,
                                Field = $"{fieldPrefix}.line",
                                Code = SituationIssueCode.InvalidConditionValue,
                                IsError = true,
                                Detail = "hasGrudge condition 'line' must be an integer."
                            });
                        }
                    }
                    break;
                }

                case "islord":
                {
                    string r = cObj["role"]?.Value<string>()?.Trim() ?? string.Empty;
                    var vToken = cObj["value"];
                    bool val = (vToken != null && vToken.Type == JTokenType.Boolean) ? vToken.Value<bool>() : true;
                    if (string.IsNullOrEmpty(r))
                    {
                        situationIssues.Add(new SituationIssue
                        {
                            SituationIndex = sitIndex,
                            SituationId = sitId,
                            Field = $"{fieldPrefix}.role",
                            Code = SituationIssueCode.MissingConditionProperty,
                            IsError = true,
                            Detail = "isLord condition requires 'role'."
                        });
                    }
                    else
                    {
                        if (!rolesDict.ContainsKey(r))
                        {
                            situationIssues.Add(new SituationIssue
                            {
                                SituationIndex = sitIndex,
                                SituationId = sitId,
                                Field = $"{fieldPrefix}.role",
                                Code = roleIssueCode,
                                IsError = true,
                                Detail = $"Unknown role '{r}' in isLord condition."
                            });
                        }
                        condDef.Role = r;
                        condDef.Value = val;
                    }
                    break;
                }

                case "chance":
                {
                    var vToken = cObj["value"];
                    if (vToken == null || vToken.Type == JTokenType.Null)
                    {
                        situationIssues.Add(new SituationIssue
                        {
                            SituationIndex = sitIndex,
                            SituationId = sitId,
                            Field = $"{fieldPrefix}.value",
                            Code = SituationIssueCode.MissingConditionProperty,
                            IsError = true,
                            Detail = "chance condition requires numeric or reference 'value'."
                        });
                    }
                    else if (vToken.Type == JTokenType.String && vToken.Value<string>()?.Trim().StartsWith("@") == true)
                    {
                        string refVal = vToken.Value<string>()!.Trim();
                        if (!SituationConfigResolver.IsValidChanceRef(refVal))
                        {
                            situationIssues.Add(new SituationIssue
                            {
                                SituationIndex = sitIndex,
                                SituationId = sitId,
                                Field = $"{fieldPrefix}.value",
                                Code = SituationIssueCode.InvalidConfigReference,
                                IsError = true,
                                Detail = $"Invalid chance reference '{refVal}', allowed: @falseRumors.captureMisconductChance, @falseRumors.poisonChance."
                            });
                        }
                        else
                        {
                            condDef.ValueRef = refVal;
                        }
                    }
                    else
                    {
                        try
                        {
                            double val = vToken.Value<double>();
                            if (double.IsNaN(val) || double.IsInfinity(val) || val < 0.0 || val > 1.0)
                            {
                                situationIssues.Add(new SituationIssue
                                {
                                    SituationIndex = sitIndex,
                                    SituationId = sitId,
                                    Field = $"{fieldPrefix}.value",
                                    Code = SituationIssueCode.InvalidConditionValue,
                                    IsError = true,
                                    Detail = $"chance condition 'value' must be between 0.0 and 1.0, got {val}."
                                });
                            }
                            else
                            {
                                condDef.Number = val;
                            }
                        }
                        catch
                        {
                            situationIssues.Add(new SituationIssue
                            {
                                SituationIndex = sitIndex,
                                SituationId = sitId,
                                Field = $"{fieldPrefix}.value",
                                Code = SituationIssueCode.InvalidConditionValue,
                                IsError = true,
                                Detail = $"Invalid chance condition 'value' '{vToken}'."
                            });
                        }
                    }

                    if (cObj["seedRoles"] is JArray srArray && srArray.Count > 0)
                    {
                        condDef.SeedRoles = new List<string>();
                        foreach (var sTok in srArray)
                        {
                            string rName = sTok.Value<string>()?.Trim() ?? string.Empty;
                            if (!rolesDict.ContainsKey(rName))
                            {
                                situationIssues.Add(new SituationIssue
                                {
                                    SituationIndex = sitIndex,
                                    SituationId = sitId,
                                    Field = $"{fieldPrefix}.seedRoles",
                                    Code = roleIssueCode,
                                    IsError = true,
                                    Detail = $"Unknown role '{rName}' in chance condition seedRoles."
                                });
                            }
                            condDef.SeedRoles.Add(rName);
                        }
                    }
                    else
                    {
                        situationIssues.Add(new SituationIssue
                        {
                            SituationIndex = sitIndex,
                            SituationId = sitId,
                            Field = $"{fieldPrefix}.seedRoles",
                            Code = SituationIssueCode.MissingConditionProperty,
                            IsError = true,
                            Detail = "chance condition requires non-empty 'seedRoles' array."
                        });
                    }
                    break;
                }

                case "knowseventabout":
                {
                    string r = cObj["role"]?.Value<string>()?.Trim() ?? string.Empty;
                    if (string.IsNullOrEmpty(r))
                    {
                        situationIssues.Add(new SituationIssue
                        {
                            SituationIndex = sitIndex,
                            SituationId = sitId,
                            Field = $"{fieldPrefix}.role",
                            Code = SituationIssueCode.MissingConditionProperty,
                            IsError = true,
                            Detail = "knowsEventAbout condition requires 'role'."
                        });
                    }
                    else if (!rolesDict.ContainsKey(r))
                    {
                        situationIssues.Add(new SituationIssue
                        {
                            SituationIndex = sitIndex,
                            SituationId = sitId,
                            Field = $"{fieldPrefix}.role",
                            Code = roleIssueCode,
                            IsError = true,
                            Detail = $"Unknown role '{r}' in knowsEventAbout condition."
                        });
                    }
                    else
                    {
                        condDef.Role = r;
                    }

                    string about = cObj["about"]?.Value<string>()?.Trim() ?? string.Empty;
                    if (string.IsNullOrEmpty(about))
                    {
                        situationIssues.Add(new SituationIssue
                        {
                            SituationIndex = sitIndex,
                            SituationId = sitId,
                            Field = $"{fieldPrefix}.about",
                            Code = SituationIssueCode.MissingConditionProperty,
                            IsError = true,
                            Detail = "knowsEventAbout condition requires 'about'."
                        });
                    }
                    else if (!rolesDict.ContainsKey(about))
                    {
                        situationIssues.Add(new SituationIssue
                        {
                            SituationIndex = sitIndex,
                            SituationId = sitId,
                            Field = $"{fieldPrefix}.about",
                            Code = roleIssueCode,
                            IsError = true,
                            Detail = $"Unknown role '{about}' in knowsEventAbout condition."
                        });
                    }
                    else
                    {
                        condDef.About = about;
                    }

                    if (cObj["eventTypes"] is JArray etArray && etArray.Count > 0)
                    {
                        condDef.EventTypes = new List<string>();
                        foreach (var tok in etArray)
                        {
                            string et = tok.Value<string>()?.Trim() ?? string.Empty;
                            if (!string.IsNullOrEmpty(et))
                            {
                                condDef.EventTypes.Add(et);
                            }
                        }
                        if (condDef.EventTypes.Count == 0)
                        {
                            situationIssues.Add(new SituationIssue
                            {
                                SituationIndex = sitIndex,
                                SituationId = sitId,
                                Field = $"{fieldPrefix}.eventTypes",
                                Code = SituationIssueCode.MissingConditionProperty,
                                IsError = true,
                                Detail = "knowsEventAbout condition requires non-empty 'eventTypes' array."
                            });
                        }
                    }
                    else
                    {
                        situationIssues.Add(new SituationIssue
                        {
                            SituationIndex = sitIndex,
                            SituationId = sitId,
                            Field = $"{fieldPrefix}.eventTypes",
                            Code = SituationIssueCode.MissingConditionProperty,
                            IsError = true,
                            Detail = "knowsEventAbout condition requires non-empty 'eventTypes' array."
                        });
                    }
                    break;
                }
            }

            return condDef;
        }
    }
}
