using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;

namespace VividWorld.Core.Situations
{
    public static partial class SituationCatalogLoader
    {
        private static void ParseRelationCondition(
            string lowerType,
            JObject cObj,
            int sitIndex,
            string sitId,
            string fieldPrefix,
            Dictionary<string, SituationRoleDef> rolesDict,
            SituationIssueCode roleIssueCode,
            SituationConditionDef condDef,
            List<SituationIssue> situationIssues)
        {
            switch (lowerType)
            {
                case "relationabove":
                case "relationbelow":
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
                            Detail = $"{condDef.Type} condition requires 'a' and 'b'."
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
                                Detail = $"Unknown role '{a}' in {condDef.Type} condition."
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
                                Detail = $"Unknown role '{b}' in {condDef.Type} condition."
                            });
                        }
                        condDef.A = a;
                        condDef.B = b;
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
                            Detail = $"{condDef.Type} condition requires numeric or reference 'value'."
                        });
                    }
                    else if (vToken.Type == JTokenType.String && vToken.Value<string>()?.Trim().StartsWith("@") == true)
                    {
                        condDef.ValueRef = vToken.Value<string>()!.Trim();
                    }
                    else
                    {
                        try
                        {
                            condDef.Number = vToken.Value<double>();
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
                                Detail = $"{condDef.Type} condition 'value' must be a valid number or reference."
                            });
                        }
                    }

                    var kvToken = cObj["kinValue"];
                    if (kvToken != null && kvToken.Type != JTokenType.Null)
                    {
                        if (kvToken.Type == JTokenType.String && kvToken.Value<string>()?.Trim().StartsWith("@") == true)
                        {
                            condDef.KinValueRef = kvToken.Value<string>()!.Trim();
                        }
                        else
                        {
                            try
                            {
                                condDef.KinNumber = kvToken.Value<double>();
                            }
                            catch
                            {
                                situationIssues.Add(new SituationIssue
                                {
                                    SituationIndex = sitIndex,
                                    SituationId = sitId,
                                    Field = $"{fieldPrefix}.kinValue",
                                    Code = SituationIssueCode.InvalidConditionValue,
                                    IsError = true,
                                    Detail = $"{condDef.Type} condition 'kinValue' must be a valid number or reference."
                                });
                            }
                        }
                    }
                    break;
                }

                case "iskin":
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
                            Detail = "isKin condition requires 'a' and 'b'."
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
                                Detail = $"Unknown role '{a}' in isKin condition."
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
                                Detail = $"Unknown role '{b}' in isKin condition."
                            });
                        }
                        condDef.A = a;
                        condDef.B = b;
                    }

                    var vToken = cObj["value"];
                    if (vToken != null && vToken.Type != JTokenType.Null)
                    {
                        try
                        {
                            condDef.Value = vToken.Value<bool>();
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
                                Detail = "isKin condition 'value' must be a boolean."
                            });
                        }
                    }
                    else
                    {
                        condDef.Value = true;
                    }
                    break;
                }

                case "isliegeof":
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
                            Detail = "isLiegeOf condition requires 'a' and 'b'."
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
                                Detail = $"Unknown role '{a}' in isLiegeOf condition."
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
                                Detail = $"Unknown role '{b}' in isLiegeOf condition."
                            });
                        }
                        condDef.A = a;
                        condDef.B = b;
                    }

                    var vToken = cObj["value"];
                    if (vToken != null && vToken.Type != JTokenType.Null)
                    {
                        try
                        {
                            condDef.Value = vToken.Value<bool>();
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
                                Detail = "isLiegeOf condition 'value' must be a boolean."
                            });
                        }
                    }
                    else
                    {
                        condDef.Value = true;
                    }
                    break;
                }
            }
        }
    }
}
