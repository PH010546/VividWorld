using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace VividWorld.Core.Situations
{
    /// <summary>流浪者事件的情境欄位：觸發 wandererBattle／wandererDaily、做法的裁決與不插手、
    /// 新效果的資料形狀（技能經驗、職位交接、較量）、新條件種類，以及整個模板的一致性檢查。
    /// 這裡只讀進來與檢查，求值與執行在後續的實作。</summary>
    public static partial class SituationCatalogLoader
    {
        private const string WandererBattleTrigger = "wandererBattle";
        private const string WandererDailyTrigger = "wandererDaily";

        /// <summary>技能欄位允許的綁定名。@disputedRoleSkill 是正在爭的職位的技能，@taught 是這次被教的技能。</summary>
        private static readonly HashSet<string> WandererSkillBindings = new(StringComparer.Ordinal)
        {
            "@disputedRoleSkill", "@taught"
        };

        /// <summary>較量的勝負綁定名，只准用在有 contest 的做法的恩怨 from／to。</summary>
        private static readonly HashSet<string> ContestOutcomeBindings = new(StringComparer.Ordinal)
        {
            "@loser", "@winner"
        };

        /// <summary>只准出現在戰後情境（wandererBattle）的條件種類。</summary>
        private static readonly HashSet<string> BattleOnlyConditionTypes = new(StringComparer.OrdinalIgnoreCase)
        {
            "foughtInBattle", "woundedInBattle", "battleWon"
        };

        /// <summary>流浪者模板的非裁決做法，playerEffects 裡只准寫 morale；寫了這些就報錯。</summary>
        private static readonly string[] WandererForbiddenPlayerEffects = { "renown", "influence", "traits" };

        /// <summary>不插手的做法自己不能寫的欄位：它只准寫恩怨。</summary>
        private static readonly string[] StandAsideForbiddenKeys =
        {
            "events", "playerEffects", "madeUpTalk", "grudgeWeight", "skillXp", "partyRoleChange", "contest"
        };

        public static bool IsWandererTrigger(string? trigger) =>
            string.Equals(trigger, WandererBattleTrigger, StringComparison.Ordinal) ||
            string.Equals(trigger, WandererDailyTrigger, StringComparison.Ordinal);

        private static SituationIssue WandererIssue(int index, string situationId, string field, SituationIssueCode code, string detail)
        {
            return new SituationIssue
            {
                SituationIndex = index,
                SituationId = situationId,
                Field = field,
                Code = code,
                IsError = true,
                Detail = detail
            };
        }

        private static bool IsValidSkillRef(string skill) =>
            !string.IsNullOrEmpty(skill) && (!skill.StartsWith("@", StringComparison.Ordinal) || WandererSkillBindings.Contains(skill));

        /// <summary>grudge 的 from／to 能寫 @loser／@winner 的條件：做法有 contest。</summary>
        private static bool AllowsContestOutcomeRef(string value, JObject bObj) =>
            ContestOutcomeBindings.Contains(value) && bObj["contest"] is JObject;

        private static bool ReadBranchFlag(JObject obj, string name, int index, string situationId, string field, List<SituationIssue> issues)
        {
            var prop = obj.Property(name, StringComparison.OrdinalIgnoreCase);
            if (prop == null || prop.Value.Type == JTokenType.Null) return false;
            if (prop.Value.Type == JTokenType.Boolean) return prop.Value.Value<bool>();
            issues.Add(WandererIssue(index, situationId, $"{field}.{name}", SituationIssueCode.InvalidSituationField,
                $"{field}.{name} must be true or false."));
            return false;
        }

        /// <summary>讀做法的 judge、standAside 與三種新效果，並檢查它們的形狀。
        /// 流浪者模板的非裁決做法在 playerEffects 只准寫 morale（寫了別的就報錯）；不插手的做法只准寫恩怨。</summary>
        private static void ParseBranchWandererFields(
            JObject bObj,
            int index,
            string situationId,
            int bIdx,
            string trigger,
            IReadOnlyDictionary<string, SituationRoleDef> rolesDict,
            List<SituationIssue> issues,
            out bool judge,
            out bool standAside,
            out List<SituationSkillXpDef>? skillXp,
            out SituationPartyRoleChangeDef? partyRoleChange,
            out SituationContestDef? contest)
        {
            string field = $"branches[{bIdx}]";
            judge = ReadBranchFlag(bObj, "judge", index, situationId, field, issues);
            standAside = ReadBranchFlag(bObj, "standAside", index, situationId, field, issues);
            skillXp = null;
            partyRoleChange = null;
            contest = null;

            if (standAside && !judge)
            {
                issues.Add(WandererIssue(index, situationId, $"{field}.standAside", SituationIssueCode.InvalidWandererTemplate,
                    "standAside requires judge: true; the branch that does not interfere is one of the judged branches."));
            }

            if (standAside)
            {
                foreach (var key in StandAsideForbiddenKeys)
                {
                    var fp = bObj.Property(key, StringComparison.OrdinalIgnoreCase);
                    if (fp == null || fp.Value.Type == JTokenType.Null) continue;
                    if (fp.Value is JArray emptyArr && emptyArr.Count == 0) continue;
                    issues.Add(WandererIssue(index, situationId, $"{field}.{key}", SituationIssueCode.InvalidWandererTemplate,
                        $"A standAside branch may only carry grudges; '{key}' is not allowed on it."));
                }
            }

            if (IsWandererTrigger(trigger) && !judge && bObj["playerEffects"] is JObject peObj)
            {
                foreach (var forbidden in WandererForbiddenPlayerEffects)
                {
                    if (peObj.Property(forbidden, StringComparison.OrdinalIgnoreCase) == null) continue;
                    issues.Add(WandererIssue(index, situationId, $"{field}.playerEffects.{forbidden}", SituationIssueCode.InvalidPlayerEffects,
                        $"A wanderer situation's branch without judge may only set morale in playerEffects; '{forbidden}' is not allowed."));
                }
            }

            var skillXpProp = bObj.Property("skillXp", StringComparison.OrdinalIgnoreCase);
            if (skillXpProp != null && skillXpProp.Value.Type != JTokenType.Null)
            {
                if (skillXpProp.Value is not JArray skillArr)
                {
                    issues.Add(WandererIssue(index, situationId, $"{field}.skillXp", SituationIssueCode.InvalidNewEffect,
                        "skillXp must be an array of { role, skill, amount }."));
                }
                else
                {
                    var list = new List<SituationSkillXpDef>();
                    for (int k = 0; k < skillArr.Count; k++)
                    {
                        string entryField = $"{field}.skillXp[{k}]";
                        if (skillArr[k] is not JObject entry)
                        {
                            issues.Add(WandererIssue(index, situationId, entryField, SituationIssueCode.InvalidNewEffect,
                                "skillXp entry must be an object."));
                            continue;
                        }

                        string role = entry["role"]?.Value<string>()?.Trim() ?? string.Empty;
                        string skill = entry["skill"]?.Value<string>()?.Trim() ?? string.Empty;
                        var amountTok = entry["amount"];

                        if (!rolesDict.ContainsKey(role))
                        {
                            issues.Add(WandererIssue(index, situationId, $"{entryField}.role", SituationIssueCode.InvalidNewEffect,
                                $"skillXp.role '{role}' is not defined in roles."));
                        }
                        if (!IsValidSkillRef(skill))
                        {
                            issues.Add(WandererIssue(index, situationId, $"{entryField}.skill", SituationIssueCode.InvalidNewEffect,
                                "skillXp.skill must be a skill name, or @disputedRoleSkill / @taught."));
                        }
                        if (amountTok == null || amountTok.Type != JTokenType.Integer)
                        {
                            issues.Add(WandererIssue(index, situationId, $"{entryField}.amount", SituationIssueCode.InvalidNewEffect,
                                "skillXp.amount must be a whole number."));
                        }
                        else
                        {
                            list.Add(new SituationSkillXpDef { Role = role, Skill = skill, Amount = amountTok.Value<int>() });
                        }
                    }
                    skillXp = list;
                }
            }

            var prcProp = bObj.Property("partyRoleChange", StringComparison.OrdinalIgnoreCase);
            if (prcProp != null && prcProp.Value.Type != JTokenType.Null)
            {
                if (prcProp.Value is not JObject prcObj)
                {
                    issues.Add(WandererIssue(index, situationId, $"{field}.partyRoleChange", SituationIssueCode.InvalidNewEffect,
                        "partyRoleChange must be an object { to }."));
                }
                else
                {
                    string to = prcObj["to"]?.Value<string>()?.Trim() ?? string.Empty;
                    if (!rolesDict.ContainsKey(to))
                    {
                        issues.Add(WandererIssue(index, situationId, $"{field}.partyRoleChange.to", SituationIssueCode.InvalidNewEffect,
                            $"partyRoleChange.to '{to}' is not defined in roles."));
                    }
                    else
                    {
                        partyRoleChange = new SituationPartyRoleChangeDef { To = to };
                    }
                }
            }

            var contestProp = bObj.Property("contest", StringComparison.OrdinalIgnoreCase);
            if (contestProp != null && contestProp.Value.Type != JTokenType.Null)
            {
                if (contestProp.Value is not JObject cstObj)
                {
                    issues.Add(WandererIssue(index, situationId, $"{field}.contest", SituationIssueCode.InvalidNewEffect,
                        "contest must be an object { a, b, skill, randomMax, winnerTakesRole }."));
                }
                else
                {
                    string a = cstObj["a"]?.Value<string>()?.Trim() ?? string.Empty;
                    string b = cstObj["b"]?.Value<string>()?.Trim() ?? string.Empty;
                    string skillRef = cstObj["skill"]?.Value<string>()?.Trim() ?? string.Empty;
                    var randTok = cstObj["randomMax"];
                    bool rolesOk = rolesDict.ContainsKey(a) && rolesDict.ContainsKey(b) && !string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
                    bool skillOk = IsValidSkillRef(skillRef);
                    bool randOk = randTok != null && randTok.Type == JTokenType.Integer && randTok.Value<int>() >= 1;
                    bool winnerTakesRole = ReadBranchFlag(cstObj, "winnerTakesRole", index, situationId, $"{field}.contest", issues);

                    if (!rolesOk)
                    {
                        issues.Add(WandererIssue(index, situationId, $"{field}.contest", SituationIssueCode.InvalidNewEffect,
                            "contest.a and contest.b must be two different roles defined in roles."));
                    }
                    if (!skillOk)
                    {
                        issues.Add(WandererIssue(index, situationId, $"{field}.contest.skill", SituationIssueCode.InvalidNewEffect,
                            "contest.skill must be a skill name, or @disputedRoleSkill / @taught."));
                    }
                    if (!randOk)
                    {
                        issues.Add(WandererIssue(index, situationId, $"{field}.contest.randomMax", SituationIssueCode.InvalidNewEffect,
                            "contest.randomMax must be a whole number of at least 1."));
                    }

                    if (rolesOk && skillOk && randOk)
                    {
                        contest = new SituationContestDef
                        {
                            A = a,
                            B = b,
                            Skill = skillRef,
                            RandomMax = randTok!.Value<int>(),
                            WinnerTakesRole = winnerTakesRole
                        };
                    }
                }
            }
        }

        /// <summary>讀 <c>player</c> 裡的 judge 與 judgeRole 的檢查。judgeRole 必須是標了 player: true 的那個角色。</summary>
        private static void CheckPlayerJudge(
            JObject obj,
            int index,
            string situationId,
            IReadOnlyDictionary<string, SituationRoleDef> rolesDict,
            SituationPlayerDef def,
            List<SituationIssue> issues,
            ref bool bad)
        {
            var judgeRoleProp = obj.Property("judgeRole", StringComparison.OrdinalIgnoreCase);
            if (judgeRoleProp != null && judgeRoleProp.Value.Type != JTokenType.Null)
            {
                string? judgeRole = judgeRoleProp.Value.Type == JTokenType.String ? judgeRoleProp.Value.Value<string>()?.Trim() : null;
                if (string.IsNullOrEmpty(judgeRole) || !rolesDict.TryGetValue(judgeRole!, out var jr) || !jr.PlayerOnly)
                {
                    issues.Add(WandererIssue(index, situationId, "player.judgeRole", SituationIssueCode.InvalidPlayerBlock,
                        "player.judgeRole must be the role marked player: true."));
                    bad = true;
                    return;
                }
                def.JudgeRole = judgeRole;
            }

            if (def.Judge && def.JudgeRole == null)
            {
                issues.Add(WandererIssue(index, situationId, "player.judgeRole", SituationIssueCode.InvalidPlayerBlock,
                    "player.judge requires judgeRole: the role marked player: true."));
                bad = true;
            }
        }

        /// <summary>整個模板的流浪者規則：沒標的做法、standAside 的唯一性與順序、judge 與 player.judge 互相對應、
        /// 戰後條件只准用在 wandererBattle。</summary>
        private static void ValidateWandererTemplate(
            int index,
            string situationId,
            string trigger,
            IReadOnlyList<SituationBranchDef> branches,
            SituationPlayerDef? player,
            IReadOnlyList<SituationConditionDef> conditions,
            List<SituationIssue> issues)
        {
            bool wanderer = IsWandererTrigger(trigger);
            bool anyNonJudge = branches.Any(b => !b.Judge);

            if (wanderer && !anyNonJudge)
            {
                issues.Add(WandererIssue(index, situationId, "branches", SituationIssueCode.InvalidWandererTemplate,
                    "A wanderer situation needs at least one branch without judge; that branch plays out when the player does not decide."));
            }

            int standAsideCount = branches.Count(b => b.StandAside);
            if (standAsideCount > 1)
            {
                issues.Add(WandererIssue(index, situationId, "branches", SituationIssueCode.InvalidWandererTemplate,
                    $"At most one branch may be standAside: true; found {standAsideCount}."));
            }
            if (standAsideCount > 0 && !anyNonJudge)
            {
                issues.Add(WandererIssue(index, situationId, "branches", SituationIssueCode.InvalidWandererTemplate,
                    "A standAside branch needs at least one branch without judge to be an alternative to."));
            }

            bool anyJudge = branches.Any(b => b.Judge);
            bool playerJudge = player?.Judge == true;
            if (anyJudge && !playerJudge)
            {
                issues.Add(WandererIssue(index, situationId, "player.judge", SituationIssueCode.InvalidWandererTemplate,
                    "A branch with judge: true needs player.judge: true in the same situation."));
            }
            if (playerJudge && !anyJudge)
            {
                issues.Add(WandererIssue(index, situationId, "player.judge", SituationIssueCode.InvalidWandererTemplate,
                    "player.judge: true needs at least one branch with judge: true."));
            }

            if (!string.Equals(trigger, WandererBattleTrigger, StringComparison.Ordinal))
            {
                bool usesBattle = conditions.Any(c => BattleOnlyConditionTypes.Contains(c.Type))
                    || branches.Any(b => b.Preconditions.Any(c => BattleOnlyConditionTypes.Contains(c.Type)));
                if (usesBattle)
                {
                    issues.Add(WandererIssue(index, situationId, "conditions", SituationIssueCode.InvalidWandererTemplate,
                        "foughtInBattle, woundedInBattle and battleWon are only allowed in wandererBattle situations."));
                }
            }
        }

        /// <summary>讀新條件種類的欄位（partyRoleHolder、teachableSkill、skillBelow、foughtInBattle、woundedInBattle、battleWon）。
        /// 只讀進來與檢查，求值在後續的實作。</summary>
        private static void ParseWandererCondition(
            string lowerType,
            JObject cObj,
            int sitIndex,
            string sitId,
            string fieldPrefix,
            IReadOnlyDictionary<string, SituationRoleDef> rolesDict,
            SituationIssueCode roleIssueCode,
            SituationConditionDef condDef,
            List<SituationIssue> situationIssues)
        {
            switch (lowerType)
            {
                case "partyroleholder":
                    condDef.Role = ReadConditionRole(cObj, "role", lowerType, sitIndex, sitId, fieldPrefix, rolesDict, roleIssueCode, situationIssues);
                    break;
                case "teachableskill":
                    condDef.Teacher = ReadConditionRole(cObj, "teacher", lowerType, sitIndex, sitId, fieldPrefix, rolesDict, roleIssueCode, situationIssues);
                    condDef.Learner = ReadConditionRole(cObj, "learner", lowerType, sitIndex, sitId, fieldPrefix, rolesDict, roleIssueCode, situationIssues);
                    break;
                case "skillbelow":
                    condDef.A = ReadConditionRole(cObj, "a", lowerType, sitIndex, sitId, fieldPrefix, rolesDict, roleIssueCode, situationIssues);
                    condDef.B = ReadConditionRole(cObj, "b", lowerType, sitIndex, sitId, fieldPrefix, rolesDict, roleIssueCode, situationIssues);
                    string skill = cObj["skill"]?.Value<string>()?.Trim() ?? string.Empty;
                    if (!IsValidSkillRef(skill))
                    {
                        situationIssues.Add(WandererIssue(sitIndex, sitId, $"{fieldPrefix}.skill", SituationIssueCode.MissingConditionProperty,
                            "skillBelow condition requires skill: a skill name, or @disputedRoleSkill / @taught."));
                    }
                    else
                    {
                        condDef.Skill = skill;
                    }
                    break;
                case "foughtinbattle":
                {
                    if (cObj["roles"] is not JArray rolesArr || rolesArr.Count == 0)
                    {
                        situationIssues.Add(WandererIssue(sitIndex, sitId, $"{fieldPrefix}.roles", SituationIssueCode.MissingConditionProperty,
                            "foughtInBattle condition requires a non-empty 'roles' array."));
                        break;
                    }
                    var list = new List<string>();
                    foreach (var tok in rolesArr)
                    {
                        string name = tok.Value<string>()?.Trim() ?? string.Empty;
                        if (!rolesDict.ContainsKey(name))
                        {
                            situationIssues.Add(WandererIssue(sitIndex, sitId, $"{fieldPrefix}.roles", roleIssueCode,
                                $"Unknown role '{name}' in foughtInBattle condition."));
                            continue;
                        }
                        list.Add(name);
                    }
                    condDef.Roles = list;
                    break;
                }
                case "woundedinbattle":
                    condDef.Role = ReadConditionRole(cObj, "role", lowerType, sitIndex, sitId, fieldPrefix, rolesDict, roleIssueCode, situationIssues);
                    break;
                case "battlewon":
                    break;
            }
        }

        private static string? ReadConditionRole(
            JObject cObj,
            string key,
            string typeName,
            int sitIndex,
            string sitId,
            string fieldPrefix,
            IReadOnlyDictionary<string, SituationRoleDef> rolesDict,
            SituationIssueCode roleIssueCode,
            List<SituationIssue> situationIssues)
        {
            string r = cObj[key]?.Value<string>()?.Trim() ?? string.Empty;
            if (string.IsNullOrEmpty(r))
            {
                situationIssues.Add(WandererIssue(sitIndex, sitId, $"{fieldPrefix}.{key}", SituationIssueCode.MissingConditionProperty,
                    $"{typeName} condition requires '{key}'."));
                return null;
            }
            if (!rolesDict.ContainsKey(r))
            {
                situationIssues.Add(WandererIssue(sitIndex, sitId, $"{fieldPrefix}.{key}", roleIssueCode,
                    $"Unknown role '{r}' in {typeName} condition."));
                return null;
            }
            return r;
        }
    }
}
