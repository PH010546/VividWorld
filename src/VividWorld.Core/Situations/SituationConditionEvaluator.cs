using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using VividWorld.Core.Config;
using VividWorld.Core.Rumors;
using VividWorld.Core.Secrets;
using VividWorld.Core.Util;

namespace VividWorld.Core.Situations
{
    public sealed class ConditionEvaluationResult
    {
        public bool Ok { get; set; }
        public string Type { get; set; } = string.Empty;

        /// <summary>聚合統計用的鍵。同一個情境可以宣告同一種條件好幾次（座次之爭就有
        /// <c>isClanLeader(slighted)</c> 與 <c>isClanLeader(favored)</c> 兩條），只用 <see cref="Type"/>
        /// 當鍵會把兩條併成一個數字，而且同時失敗的一組會被記兩次 ⇒ 排除理由那行看不出是哪一條在擋。</summary>
        public string Label { get; set; } = string.Empty;

        public string Detail { get; set; } = string.Empty;

        public override string ToString() => Detail;
    }

    public static class SituationConditionEvaluator
    {
        public static ConditionEvaluationResult Evaluate(
            SituationConditionDef condition,
            IReadOnlyDictionary<string, SituationRoleFacts> factsByRole,
            IReadOnlyDictionary<string, string?>? boundHeroes = null,
            IReadOnlyDictionary<string, string>? unboundReasons = null,
            string? situationId = null,
            double day = 0.0,
            ISituationHistory? history = null,
            IReadOnlyCollection<string>? nonDerivedHeroIds = null,
            SituationWorldContext? context = null,
            bool forced = false)
        {
            if (condition == null) throw new ArgumentNullException(nameof(condition));
            factsByRole ??= new Dictionary<string, SituationRoleFacts>();

            string type = condition.Type?.Trim() ?? string.Empty;
            ConditionEvaluationResult result;

            switch (type.ToLowerInvariant())
            {
                case "samesettlement":
                    result = EvaluateSameSettlement(condition, factsByRole);
                    break;

                case "differentclan":
                    result = EvaluateDifferentClan(condition, factsByRole);
                    break;

                case "samekingdom":
                    result = EvaluateSameKingdom(condition, factsByRole);
                    break;

                case "isclanleader":
                    result = EvaluateIsClanLeader(condition, factsByRole);
                    break;

                case "islord":
                    result = EvaluateIsLord(condition, factsByRole);
                    break;

                case "clantiercompare":
                    result = EvaluateClanTierCompare(condition, factsByRole);
                    break;

                case "rolebound":
                    result = EvaluateRoleBound(condition, boundHeroes, unboundReasons);
                    break;

                case "cooldown":
                    result = EvaluateCooldown(condition, day, situationId, history, nonDerivedHeroIds);
                    break;

                case "traitatleast":
                    result = EvaluateTraitCompare(condition, factsByRole, context, isAtLeast: true);
                    break;

                case "traitatmost":
                    result = EvaluateTraitCompare(condition, factsByRole, context, isAtLeast: false);
                    break;

                case "hasgrudge":
                    result = EvaluateHasGrudge(condition, factsByRole, context);
                    break;

                case "chance":
                    result = EvaluateChance(condition, factsByRole, context, situationId, day);
                    break;

                case "knowseventabout":
                    result = EvaluateKnowsEventAbout(condition, factsByRole, context);
                    break;

                case "partyroleholder":
                    result = EvaluatePartyRoleHolder(condition, factsByRole, context);
                    break;

                case "teachableskill":
                    result = EvaluateTeachableSkill(condition, factsByRole, context);
                    break;

                case "skillbelow":
                    result = EvaluateSkillBelow(condition, factsByRole, context);
                    break;

                case "foughtinbattle":
                    result = EvaluateFoughtInBattle(condition, factsByRole, context);
                    break;

                case "woundedinbattle":
                    result = EvaluateWoundedInBattle(condition, factsByRole, context);
                    break;

                case "relationabove":
                    result = EvaluateRelationAbove(condition, factsByRole, boundHeroes, context);
                    break;

                case "relationbelow":
                    result = EvaluateRelationBelow(condition, factsByRole, boundHeroes, context);
                    break;

                case "iskin":
                    result = EvaluateIsKin(condition, factsByRole, boundHeroes, context);
                    break;

                case "isliegeof":
                    result = EvaluateIsLiegeOf(condition, factsByRole, boundHeroes, context);
                    break;

                case "battlewon":
                    result = EvaluateBattleWon(condition, context);
                    break;

                case "hasstakeablesecret":
                    result = EvaluateHasStakeableSecret(condition, factsByRole, boundHeroes, context);
                    break;

                default:
                    result = new ConditionEvaluationResult
                    {
                        Ok = false,
                        Detail = $"unknown condition type '{type}'"
                    };
                    break;
            }

            if (forced)
            {
                string lower = type.ToLowerInvariant();
                if (lower == "chance" || lower == "traitatleast" || lower == "traitatmost" || lower == "cooldown")
                {
                    result.Ok = true;
                    if (!result.Detail.Contains("(bypassed: forced)"))
                    {
                        result.Detail = string.IsNullOrEmpty(result.Detail) ? "(bypassed: forced)" : $"{result.Detail} (bypassed: forced)";
                    }
                }
            }

            result.Type = condition.Type ?? string.Empty;
            result.Label = BuildLabel(result.Type, type.ToLowerInvariant(), condition);
            return result;
        }

        /// <summary>聚合鍵：會在同一個情境裡出現不只一次的條件要帶上角色，其餘就用型別本身。</summary>
        private static string BuildLabel(string type, string lowerType, SituationConditionDef condition)
        {
            switch (lowerType)
            {
                case "isclanleader":
                case "islord":
                    return $"{type}({condition.Role ?? "role"})";

                case "traitatleast":
                case "traitatmost":
                    return $"{type}({condition.Role ?? "role"}.{condition.Trait ?? "trait"})";

                case "hasgrudge":
                    return $"{type}({condition.A ?? "a"}->{condition.B ?? "b"})";

                case "chance":
                    string seedRolesStr = condition.SeedRoles != null ? string.Join(",", condition.SeedRoles) : string.Empty;
                    return $"{type}({seedRolesStr})";

                case "knowseventabout":
                    return $"{type}({condition.Role ?? "role"}->{condition.About ?? "about"})";

                case "partyroleholder":
                    return $"{type}({condition.Role ?? "role"})";

                case "teachableskill":
                    return $"{type}({condition.Teacher ?? "teacher"}->{condition.Learner ?? "learner"})";

                case "skillbelow":
                    return $"{type}({condition.A ?? "a"}<{condition.B ?? "b"}.{condition.Skill ?? "skill"})";

                case "foughtinbattle":
                    string foughtRoles = condition.Roles != null ? string.Join(",", condition.Roles) : string.Empty;
                    return $"{type}({foughtRoles})";

                case "woundedinbattle":
                    return $"{type}({condition.Role ?? "role"})";

                case "relationabove":
                case "relationbelow":
                    return $"{type}({condition.A ?? "a"}->{condition.B ?? "b"})";

                case "iskin":
                    return $"{type}({condition.A ?? "a"}&{condition.B ?? "b"})";

                case "isliegeof":
                    return $"{type}({condition.A ?? "a"}->{condition.B ?? "b"})";

                case "battlewon":
                    return type;

                case "hasstakeablesecret":
                    return $"{type}({condition.Bettor ?? "bettor"}->{condition.Opponent ?? "opponent"})";

                default:
                    return type;
            }
        }

        public static IReadOnlyList<ConditionEvaluationResult> EvaluateAll(
            IEnumerable<SituationConditionDef> conditions,
            IReadOnlyDictionary<string, SituationRoleFacts> factsByRole,
            IReadOnlyDictionary<string, string?>? boundHeroes = null,
            IReadOnlyDictionary<string, string>? unboundReasons = null,
            string? situationId = null,
            double day = 0.0,
            ISituationHistory? history = null,
            IReadOnlyCollection<string>? nonDerivedHeroIds = null,
            SituationWorldContext? context = null,
            bool forced = false)
        {
            var results = new List<ConditionEvaluationResult>();
            if (conditions != null)
            {
                foreach (var cond in conditions)
                {
                    results.Add(Evaluate(cond, factsByRole, boundHeroes, unboundReasons, situationId, day, history, nonDerivedHeroIds, context, forced));
                }
            }
            return results;
        }

        private static ConditionEvaluationResult EvaluateCooldown(
            SituationConditionDef cond,
            double day,
            string? situationId,
            ISituationHistory? history,
            IReadOnlyCollection<string>? nonDerivedHeroIds)
        {
            double condDays = cond.Days ?? 0.0;
            string daysStr = $"{condDays.ToString("0.##", CultureInfo.InvariantCulture)}d";

            if (history == null)
            {
                return new ConditionEvaluationResult
                {
                    Ok = true,
                    Type = cond.Type,
                    Detail = $"cooldown {daysStr} ok (no history available)"
                };
            }

            // 冷卻的對象**只有非 derived 角色**（規格 §10.4.3）。
            // 退回去用 factsByRole／boundHeroes 會把 derived 角色（座次之爭的 host）一起算進來，
            // 那會讓「涵蓋」變嚴、冷卻靜默失效，所以這裡不給退路：沒有角色就直說。
            if (nonDerivedHeroIds == null || nonDerivedHeroIds.Count == 0)
            {
                return new ConditionEvaluationResult
                {
                    Ok = true,
                    Type = cond.Type,
                    Detail = $"cooldown {daysStr} ok (no non-derived roles bound)"
                };
            }

            IReadOnlyCollection<string> heroIds = nonDerivedHeroIds;

            var occ = history.LastOccurrence(situationId ?? string.Empty, heroIds);
            if (occ == null)
            {
                return new ConditionEvaluationResult
                {
                    Ok = true,
                    Type = cond.Type,
                    Detail = $"cooldown {daysStr} ok (never)"
                };
            }

            double elapsed = day - occ.Day;
            if (elapsed >= condDays)
            {
                return new ConditionEvaluationResult
                {
                    Ok = true,
                    Type = cond.Type,
                    Detail = string.Format(CultureInfo.InvariantCulture,
                        "cooldown {0} ok (last was {1} on day {2:F1}, {3:F1}d ago)",
                        daysStr, occ.EventId, occ.Day, elapsed)
                };
            }
            else
            {
                return new ConditionEvaluationResult
                {
                    Ok = false,
                    Type = cond.Type,
                    Detail = string.Format(CultureInfo.InvariantCulture,
                        "cooldown {0} failed (last was {1} on day {2:F1}, {3:F1}d ago)",
                        daysStr, occ.EventId, occ.Day, elapsed)
                };
            }
        }

        private static ConditionEvaluationResult EvaluateSameSettlement(
            SituationConditionDef cond,
            IReadOnlyDictionary<string, SituationRoleFacts> factsByRole)
        {
            var roles = cond.Roles ?? new List<string>();
            var kinds = cond.Kinds ?? new List<string>();

            var settlementIds = new List<string?>();
            var settlementKinds = new List<string?>();
            var roleDetails = new List<string>();

            foreach (var r in roles)
            {
                factsByRole.TryGetValue(r, out var f);
                string? sId = f?.SettlementId;
                settlementIds.Add(sId);
                settlementKinds.Add(f?.SettlementKind);
                roleDetails.Add($"{r}={sId ?? "null"}");
            }

            bool anyNull = settlementIds.Any(string.IsNullOrEmpty);
            bool allSame = !anyNull && settlementIds.Distinct().Count() == 1;

            if (anyNull || !allSame)
            {
                return new ConditionEvaluationResult
                {
                    Ok = false,
                    Detail = $"sameSettlement failed ({string.Join(", ", roleDetails)})"
                };
            }

            string targetSettlementId = settlementIds[0]!;
            string? targetKind = settlementKinds[0];

            bool kindOk = targetKind != null && kinds.Any(k => string.Equals(k, targetKind, StringComparison.OrdinalIgnoreCase));
            if (!kindOk)
            {
                string allowedKinds = string.Join("/", kinds);
                return new ConditionEvaluationResult
                {
                    Ok = false,
                    Detail = $"sameSettlement failed ({targetSettlementId} is {targetKind ?? "null"}, need {allowedKinds})"
                };
            }

            return new ConditionEvaluationResult
            {
                Ok = true,
                Detail = $"sameSettlement ok ({targetSettlementId}, {targetKind})"
            };
        }

        private static ConditionEvaluationResult EvaluateDifferentClan(
            SituationConditionDef cond,
            IReadOnlyDictionary<string, SituationRoleFacts> factsByRole)
        {
            string a = cond.A ?? "a";
            string b = cond.B ?? "b";

            factsByRole.TryGetValue(a, out var fA);
            factsByRole.TryGetValue(b, out var fB);

            if (string.IsNullOrEmpty(fA?.ClanId))
            {
                return new ConditionEvaluationResult
                {
                    Ok = false,
                    Detail = $"differentClan failed ({a} has no clan)"
                };
            }

            if (string.IsNullOrEmpty(fB?.ClanId))
            {
                return new ConditionEvaluationResult
                {
                    Ok = false,
                    Detail = $"differentClan failed ({b} has no clan)"
                };
            }

            if (string.Equals(fA!.ClanId, fB!.ClanId, StringComparison.Ordinal))
            {
                return new ConditionEvaluationResult
                {
                    Ok = false,
                    Detail = $"differentClan failed (both {fA.ClanId})"
                };
            }

            return new ConditionEvaluationResult
            {
                Ok = true,
                Detail = $"differentClan ok ({fA.ClanId} vs {fB.ClanId})"
            };
        }

        private static ConditionEvaluationResult EvaluateSameKingdom(
            SituationConditionDef cond,
            IReadOnlyDictionary<string, SituationRoleFacts> factsByRole)
        {
            string a = cond.A ?? "a";
            string b = cond.B ?? "b";

            factsByRole.TryGetValue(a, out var fA);
            factsByRole.TryGetValue(b, out var fB);

            if (string.IsNullOrEmpty(fA?.KingdomId))
            {
                return new ConditionEvaluationResult
                {
                    Ok = false,
                    Detail = $"sameKingdom failed ({a} has no kingdom)"
                };
            }

            if (string.IsNullOrEmpty(fB?.KingdomId))
            {
                return new ConditionEvaluationResult
                {
                    Ok = false,
                    Detail = $"sameKingdom failed ({b} has no kingdom)"
                };
            }

            if (!string.Equals(fA!.KingdomId, fB!.KingdomId, StringComparison.Ordinal))
            {
                return new ConditionEvaluationResult
                {
                    Ok = false,
                    Detail = $"sameKingdom failed ({fA.KingdomId} vs {fB.KingdomId})"
                };
            }

            return new ConditionEvaluationResult
            {
                Ok = true,
                Detail = $"sameKingdom ok ({fA.KingdomId})"
            };
        }

        private static ConditionEvaluationResult EvaluateIsClanLeader(
            SituationConditionDef cond,
            IReadOnlyDictionary<string, SituationRoleFacts> factsByRole)
        {
            string role = cond.Role ?? "role";
            bool expected = cond.Value ?? true;

            factsByRole.TryGetValue(role, out var f);
            bool actual = f?.IsClanLeader ?? false;

            if (actual == expected)
            {
                return new ConditionEvaluationResult
                {
                    Ok = true,
                    Detail = $"isClanLeader({role}) ok ({expected.ToString().ToLowerInvariant()})"
                };
            }

            return new ConditionEvaluationResult
            {
                Ok = false,
                Detail = $"isClanLeader({role}) failed (is {actual.ToString().ToLowerInvariant()}, need {expected.ToString().ToLowerInvariant()})"
            };
        }

        private static ConditionEvaluationResult EvaluateIsLord(
            SituationConditionDef cond,
            IReadOnlyDictionary<string, SituationRoleFacts> factsByRole)
        {
            string role = cond.Role ?? "role";
            bool expected = cond.Value ?? true;

            factsByRole.TryGetValue(role, out var f);
            bool actual = f?.IsLord ?? false;

            if (actual == expected)
            {
                return new ConditionEvaluationResult
                {
                    Ok = true,
                    Detail = $"isLord({role}) ok ({expected.ToString().ToLowerInvariant()})"
                };
            }

            return new ConditionEvaluationResult
            {
                Ok = false,
                Detail = $"isLord({role}) failed (not a lord, is {actual.ToString().ToLowerInvariant()}, need {expected.ToString().ToLowerInvariant()})"
            };
        }

        private static ConditionEvaluationResult EvaluateClanTierCompare(
            SituationConditionDef cond,
            IReadOnlyDictionary<string, SituationRoleFacts> factsByRole)
        {
            string a = cond.A ?? "a";
            string b = cond.B ?? "b";
            string op = cond.Op ?? "==";

            factsByRole.TryGetValue(a, out var fA);
            factsByRole.TryGetValue(b, out var fB);

            if (fA?.ClanTier == null)
            {
                return new ConditionEvaluationResult
                {
                    Ok = false,
                    Detail = $"clanTierCompare failed ({a} has no clan)"
                };
            }

            if (fB?.ClanTier == null)
            {
                return new ConditionEvaluationResult
                {
                    Ok = false,
                    Detail = $"clanTierCompare failed ({b} has no clan)"
                };
            }

            int tA = fA.ClanTier.Value;
            int tB = fB.ClanTier.Value;

            bool compResult = op switch
            {
                ">" => tA > tB,
                ">=" => tA >= tB,
                "<" => tA < tB,
                "<=" => tA <= tB,
                "==" => tA == tB,
                "!=" => tA != tB,
                _ => false
            };

            if (compResult)
            {
                return new ConditionEvaluationResult
                {
                    Ok = true,
                    Detail = $"clanTierCompare ok ({a} {tA} {op} {b} {tB})"
                };
            }

            return new ConditionEvaluationResult
            {
                Ok = false,
                Detail = $"clanTierCompare failed ({a} {tA} {op} {b} {tB})"
            };
        }

        private static ConditionEvaluationResult EvaluateRoleBound(
            SituationConditionDef cond,
            IReadOnlyDictionary<string, string?>? boundHeroes,
            IReadOnlyDictionary<string, string>? unboundReasons)
        {
            string role = cond.Role ?? "role";
            string? heroId = null;
            bool isBound = boundHeroes != null && boundHeroes.TryGetValue(role, out heroId) && !string.IsNullOrEmpty(heroId);

            if (isBound)
            {
                return new ConditionEvaluationResult
                {
                    Ok = true,
                    Detail = $"roleBound({role}) ok ({heroId})"
                };
            }

            string reason = "role not bound";
            if (unboundReasons != null && unboundReasons.TryGetValue(role, out var r) && !string.IsNullOrEmpty(r))
            {
                reason = r;
            }

            return new ConditionEvaluationResult
            {
                Ok = false,
                Detail = $"roleBound({role}) failed ({reason})"
            };
        }

        private static ConditionEvaluationResult EvaluateTraitCompare(
            SituationConditionDef cond,
            IReadOnlyDictionary<string, SituationRoleFacts> factsByRole,
            SituationWorldContext? context,
            bool isAtLeast)
        {
            string role = cond.Role ?? "role";
            string trait = cond.Trait ?? "trait";
            int target = (int)(cond.Number ?? 0);

            if (context?.Traits == null)
            {
                return new ConditionEvaluationResult
                {
                    Ok = false,
                    Detail = $"missing context: {role} traits unavailable"
                };
            }

            if (!factsByRole.TryGetValue(role, out var facts) || string.IsNullOrEmpty(facts.HeroId))
            {
                return new ConditionEvaluationResult
                {
                    Ok = false,
                    Detail = $"role '{role}' unbound"
                };
            }

            var profile = context.Traits.Of(facts.HeroId);
            if (profile == null)
            {
                return new ConditionEvaluationResult
                {
                    Ok = false,
                    Detail = $"traits for '{facts.HeroId}' unavailable"
                };
            }

            int actual = GetTraitValue(profile, trait);
            bool ok = isAtLeast ? actual >= target : actual <= target;
            string cmp = isAtLeast ? (ok ? ">=" : "<") : (ok ? "<=" : ">");

            return new ConditionEvaluationResult
            {
                Ok = ok,
                Detail = $"{role} {trait} {actual} {cmp} {target}"
            };
        }

        private static ConditionEvaluationResult EvaluateHasGrudge(
            SituationConditionDef cond,
            IReadOnlyDictionary<string, SituationRoleFacts> factsByRole,
            SituationWorldContext? context)
        {
            string a = cond.A ?? "a";
            string b = cond.B ?? "b";

            if (!factsByRole.TryGetValue(a, out var factsA) || string.IsNullOrEmpty(factsA.HeroId))
            {
                return new ConditionEvaluationResult
                {
                    Ok = false,
                    Detail = $"role '{a}' unbound"
                };
            }
            if (!factsByRole.TryGetValue(b, out var factsB) || string.IsNullOrEmpty(factsB.HeroId))
            {
                return new ConditionEvaluationResult
                {
                    Ok = false,
                    Detail = $"role '{b}' unbound"
                };
            }

            if (context == null || (context.GrudgeSum == null && context.Affection == null))
            {
                return new ConditionEvaluationResult
                {
                    Ok = false,
                    Detail = "missing context: grudge and affection functions unavailable"
                };
            }

            int line = cond.Line ?? context.GrudgeLine;
            int nativeLine = context.NativeGrudgeLine;

            int grudge = context.GrudgeSum != null ? context.GrudgeSum(factsA.HeroId, factsB.HeroId) : 0;
            int? affection = context.Affection != null ? context.Affection(factsA.HeroId, factsB.HeroId) : null;

            bool grudgeMet = context.GrudgeSum != null && grudge <= line;
            bool affectionMet = affection.HasValue && affection.Value <= nativeLine;
            bool ok = grudgeMet || affectionMet;

            string detail = ok
                ? $"{a}->{b} grudge {grudge} <= {line} (or affection {affection?.ToString() ?? "null"} <= {nativeLine})"
                : $"{a}->{b} grudge {grudge} > {line} and affection {affection?.ToString() ?? "null"} > {nativeLine}";

            return new ConditionEvaluationResult
            {
                Ok = ok,
                Detail = detail
            };
        }

        private static ConditionEvaluationResult EvaluateChance(
            SituationConditionDef cond,
            IReadOnlyDictionary<string, SituationRoleFacts> factsByRole,
            SituationWorldContext? context,
            string? situationId,
            double day)
        {
            if (context == null)
            {
                return new ConditionEvaluationResult
                {
                    Ok = false,
                    Detail = "missing context: rng unavailable"
                };
            }

            double threshold;
            if (!string.IsNullOrEmpty(cond.ValueRef))
            {
                threshold = SituationConfigResolver.ResolveDouble(cond.ValueRef!, context.Config);
            }
            else
            {
                threshold = cond.Number ?? 0.0;
            }
            var parts = new List<object>
            {
                "chance",
                situationId ?? string.Empty,
                RumorSeed.DayBucket(day)
            };

            if (cond.SeedRoles != null)
            {
                foreach (var r in cond.SeedRoles)
                {
                    string? hId = factsByRole.TryGetValue(r, out var f) ? f.HeroId : null;
                    parts.Add($"{r}={hId ?? string.Empty}");
                }
            }

            long seed = RumorSeed.Of(context.Seed, parts.ToArray());
            var rng = context.Rng ?? new SplitMix64Rng();
            double roll = rng.NextDouble(seed);
            bool ok = roll < threshold;

            return new ConditionEvaluationResult
            {
                Ok = ok,
                Detail = $"chance roll {roll.ToString("0.###", CultureInfo.InvariantCulture)} < {threshold.ToString("0.###", CultureInfo.InvariantCulture)}: {(ok ? "passed" : "failed")}"
            };
        }

        private static ConditionEvaluationResult EvaluateKnowsEventAbout(
            SituationConditionDef cond,
            IReadOnlyDictionary<string, SituationRoleFacts> factsByRole,
            SituationWorldContext? context)
        {
            string role = cond.Role ?? "role";
            string about = cond.About ?? "about";

            if (!factsByRole.TryGetValue(role, out var factsRole) || string.IsNullOrEmpty(factsRole.HeroId))
            {
                return new ConditionEvaluationResult
                {
                    Ok = false,
                    Detail = $"role '{role}' unbound"
                };
            }
            if (!factsByRole.TryGetValue(about, out var factsAbout) || string.IsNullOrEmpty(factsAbout.HeroId))
            {
                return new ConditionEvaluationResult
                {
                    Ok = false,
                    Detail = $"role '{about}' unbound"
                };
            }

            if (cond.EventTypes == null || cond.EventTypes.Count == 0)
            {
                return new ConditionEvaluationResult
                {
                    Ok = false,
                    Detail = "knowsEventAbout missing eventTypes"
                };
            }

            if (context == null || (context.FindRememberedEventAbout == null && context.RemembersEventAbout == null))
            {
                return new ConditionEvaluationResult
                {
                    Ok = false,
                    Detail = "missing context: memory lookup unavailable"
                };
            }

            string? foundEventId = null;
            bool remembers = false;
            if (context.FindRememberedEventAbout != null)
            {
                foundEventId = context.FindRememberedEventAbout(factsRole.HeroId, cond.EventTypes, factsAbout.HeroId);
                remembers = !string.IsNullOrEmpty(foundEventId);
            }
            else if (context.RemembersEventAbout != null)
            {
                remembers = context.RemembersEventAbout(factsRole.HeroId, cond.EventTypes, factsAbout.HeroId);
            }

            string detail = remembers
                ? (!string.IsNullOrEmpty(foundEventId) ? $"{role} remembers {foundEventId} about {about}" : $"{role} remembers event about {about}")
                : $"{role} does not remember any matching event about {about}";

            return new ConditionEvaluationResult
            {
                Ok = remembers,
                Detail = detail
            };
        }

        private static ConditionEvaluationResult EvaluatePartyRoleHolder(
            SituationConditionDef cond,
            IReadOnlyDictionary<string, SituationRoleFacts> factsByRole,
            SituationWorldContext? context)
        {
            string role = cond.Role ?? "role";
            if (!factsByRole.TryGetValue(role, out var facts) || string.IsNullOrEmpty(facts.HeroId))
            {
                return new ConditionEvaluationResult { Ok = false, Detail = $"role '{role}' unbound" };
            }

            if (context?.IsPartyRoleHolder == null)
            {
                return new ConditionEvaluationResult { Ok = false, Detail = "missing context: party role lookup unavailable" };
            }

            bool ok = context.IsPartyRoleHolder(facts.HeroId);
            return new ConditionEvaluationResult
            {
                Ok = ok,
                Detail = ok ? $"partyRoleHolder({role}) ok" : $"partyRoleHolder({role}) failed (hero {facts.HeroId} does not hold a party role)"
            };
        }

        private static ConditionEvaluationResult EvaluateTeachableSkill(
            SituationConditionDef cond,
            IReadOnlyDictionary<string, SituationRoleFacts> factsByRole,
            SituationWorldContext? context)
        {
            string teacherRole = cond.Teacher ?? "teacher";
            string learnerRole = cond.Learner ?? "learner";

            if (!factsByRole.TryGetValue(teacherRole, out var fTeacher) || string.IsNullOrEmpty(fTeacher.HeroId))
            {
                return new ConditionEvaluationResult { Ok = false, Detail = $"role '{teacherRole}' unbound" };
            }
            if (!factsByRole.TryGetValue(learnerRole, out var fLearner) || string.IsNullOrEmpty(fLearner.HeroId))
            {
                return new ConditionEvaluationResult { Ok = false, Detail = $"role '{learnerRole}' unbound" };
            }

            if (context?.HasTeachableSkill == null)
            {
                return new ConditionEvaluationResult { Ok = false, Detail = "missing context: teachable skill lookup unavailable" };
            }

            bool ok = context.HasTeachableSkill(fTeacher.HeroId, fLearner.HeroId);
            return new ConditionEvaluationResult
            {
                Ok = ok,
                Detail = ok ? $"teachableSkill({teacherRole}->{learnerRole}) ok" : $"teachableSkill({teacherRole}->{learnerRole}) failed"
            };
        }

        private static ConditionEvaluationResult EvaluateSkillBelow(
            SituationConditionDef cond,
            IReadOnlyDictionary<string, SituationRoleFacts> factsByRole,
            SituationWorldContext? context)
        {
            string aRole = cond.A ?? "a";
            string bRole = cond.B ?? "b";
            string skill = cond.Skill ?? string.Empty;

            if (!factsByRole.TryGetValue(aRole, out var fA) || string.IsNullOrEmpty(fA.HeroId))
            {
                return new ConditionEvaluationResult { Ok = false, Detail = $"role '{aRole}' unbound" };
            }
            if (!factsByRole.TryGetValue(bRole, out var fB) || string.IsNullOrEmpty(fB.HeroId))
            {
                return new ConditionEvaluationResult { Ok = false, Detail = $"role '{bRole}' unbound" };
            }

            if (context?.IsSkillBelow == null)
            {
                return new ConditionEvaluationResult { Ok = false, Detail = "missing context: skill comparison unavailable" };
            }

            bool ok = context.IsSkillBelow(fA.HeroId, fB.HeroId, skill);
            return new ConditionEvaluationResult
            {
                Ok = ok,
                Detail = ok ? $"skillBelow({aRole}<{bRole}.{skill}) ok" : $"skillBelow({aRole}<{bRole}.{skill}) failed"
            };
        }

        private static ConditionEvaluationResult EvaluateFoughtInBattle(
            SituationConditionDef cond,
            IReadOnlyDictionary<string, SituationRoleFacts> factsByRole,
            SituationWorldContext? context)
        {
            if (cond.Roles == null || cond.Roles.Count == 0)
            {
                return new ConditionEvaluationResult { Ok = false, Detail = "foughtInBattle requires roles" };
            }

            if (context?.FoughtInBattle == null)
            {
                return new ConditionEvaluationResult { Ok = false, Detail = "missing context: battle participants lookup unavailable" };
            }

            foreach (var r in cond.Roles)
            {
                if (!factsByRole.TryGetValue(r, out var f) || string.IsNullOrEmpty(f.HeroId))
                {
                    return new ConditionEvaluationResult { Ok = false, Detail = $"role '{r}' unbound" };
                }
                if (!context.FoughtInBattle(f.HeroId))
                {
                    return new ConditionEvaluationResult { Ok = false, Detail = $"foughtInBattle failed: {r} ({f.HeroId}) did not fight in battle" };
                }
            }

            return new ConditionEvaluationResult { Ok = true, Detail = "foughtInBattle ok" };
        }

        private static ConditionEvaluationResult EvaluateWoundedInBattle(
            SituationConditionDef cond,
            IReadOnlyDictionary<string, SituationRoleFacts> factsByRole,
            SituationWorldContext? context)
        {
            string role = cond.Role ?? "role";
            if (!factsByRole.TryGetValue(role, out var f) || string.IsNullOrEmpty(f.HeroId))
            {
                return new ConditionEvaluationResult { Ok = false, Detail = $"role '{role}' unbound" };
            }

            if (context?.WoundedInBattle == null)
            {
                return new ConditionEvaluationResult { Ok = false, Detail = "missing context: battle wounded lookup unavailable" };
            }

            bool ok = context.WoundedInBattle(f.HeroId);
            return new ConditionEvaluationResult
            {
                Ok = ok,
                Detail = ok ? $"woundedInBattle({role}) ok" : $"woundedInBattle({role}) failed: {role} ({f.HeroId}) was not wounded in battle"
            };
        }

        private static ConditionEvaluationResult EvaluateRelationAbove(
            SituationConditionDef cond,
            IReadOnlyDictionary<string, SituationRoleFacts> factsByRole,
            IReadOnlyDictionary<string, string?>? boundHeroes,
            SituationWorldContext? context)
        {
            string roleA = cond.A ?? "a";
            string roleB = cond.B ?? "b";
            string label = BuildLabel(cond.Type ?? "relationAbove", "relationabove", cond);

            string? heroA = TryGetHeroId(roleA, factsByRole, boundHeroes);
            if (string.IsNullOrEmpty(heroA))
            {
                return new ConditionEvaluationResult { Ok = false, Detail = $"{label} failed: role '{roleA}' unbound" };
            }
            string? heroB = TryGetHeroId(roleB, factsByRole, boundHeroes);
            if (string.IsNullOrEmpty(heroB))
            {
                return new ConditionEvaluationResult { Ok = false, Detail = $"{label} failed: role '{roleB}' unbound" };
            }

            if (context?.RelationBetween == null)
            {
                return new ConditionEvaluationResult { Ok = false, Detail = $"{label} failed: context.RelationBetween unavailable" };
            }

            bool isKin = context.IsKin != null && context.IsKin(heroA!, heroB!);
            double? threshold = null;
            if (isKin && (cond.KinNumber.HasValue || !string.IsNullOrEmpty(cond.KinValueRef)))
            {
                threshold = ResolveThreshold(cond.KinNumber, cond.KinValueRef, context.Config);
            }
            else
            {
                threshold = ResolveThreshold(cond.Number, cond.ValueRef, context.Config);
            }

            if (!threshold.HasValue)
            {
                return new ConditionEvaluationResult { Ok = false, Detail = $"{label} failed: threshold could not be resolved" };
            }

            int relation = context.RelationBetween(heroA!, heroB!);
            bool ok = relation > threshold.Value;

            bool usedKinThreshold = isKin && (cond.KinNumber.HasValue || !string.IsNullOrEmpty(cond.KinValueRef));
            string kinTag = usedKinThreshold ? " (kin threshold)" : "";
            return new ConditionEvaluationResult
            {
                Ok = ok,
                Detail = ok
                    ? $"{label} ok: relation is {relation} > {threshold.Value.ToString("0.##", CultureInfo.InvariantCulture)}{kinTag}"
                    : $"{label} failed: relation {relation} not > {threshold.Value.ToString("0.##", CultureInfo.InvariantCulture)}{kinTag}"
            };
        }

        private static ConditionEvaluationResult EvaluateRelationBelow(
            SituationConditionDef cond,
            IReadOnlyDictionary<string, SituationRoleFacts> factsByRole,
            IReadOnlyDictionary<string, string?>? boundHeroes,
            SituationWorldContext? context)
        {
            string roleA = cond.A ?? "a";
            string roleB = cond.B ?? "b";
            string label = BuildLabel(cond.Type ?? "relationBelow", "relationbelow", cond);

            string? heroA = TryGetHeroId(roleA, factsByRole, boundHeroes);
            if (string.IsNullOrEmpty(heroA))
            {
                return new ConditionEvaluationResult { Ok = false, Detail = $"{label} failed: role '{roleA}' unbound" };
            }
            string? heroB = TryGetHeroId(roleB, factsByRole, boundHeroes);
            if (string.IsNullOrEmpty(heroB))
            {
                return new ConditionEvaluationResult { Ok = false, Detail = $"{label} failed: role '{roleB}' unbound" };
            }

            if (context?.RelationBetween == null)
            {
                return new ConditionEvaluationResult { Ok = false, Detail = $"{label} failed: context.RelationBetween unavailable" };
            }

            bool isKin = context.IsKin != null && context.IsKin(heroA!, heroB!);
            double? threshold = null;
            if (isKin && (cond.KinNumber.HasValue || !string.IsNullOrEmpty(cond.KinValueRef)))
            {
                threshold = ResolveThreshold(cond.KinNumber, cond.KinValueRef, context.Config);
            }
            else
            {
                threshold = ResolveThreshold(cond.Number, cond.ValueRef, context.Config);
            }

            if (!threshold.HasValue)
            {
                return new ConditionEvaluationResult { Ok = false, Detail = $"{label} failed: threshold could not be resolved" };
            }

            int relation = context.RelationBetween(heroA!, heroB!);
            bool ok = relation < threshold.Value;

            bool usedKinThreshold = isKin && (cond.KinNumber.HasValue || !string.IsNullOrEmpty(cond.KinValueRef));
            string kinTag = usedKinThreshold ? " (kin threshold)" : "";
            return new ConditionEvaluationResult
            {
                Ok = ok,
                Detail = ok
                    ? $"{label} ok: relation is {relation} < {threshold.Value.ToString("0.##", CultureInfo.InvariantCulture)}{kinTag}"
                    : $"{label} failed: relation {relation} not < {threshold.Value.ToString("0.##", CultureInfo.InvariantCulture)}{kinTag}"
            };
        }

        private static ConditionEvaluationResult EvaluateIsKin(
            SituationConditionDef cond,
            IReadOnlyDictionary<string, SituationRoleFacts> factsByRole,
            IReadOnlyDictionary<string, string?>? boundHeroes,
            SituationWorldContext? context)
        {
            string roleA = cond.A ?? "a";
            string roleB = cond.B ?? "b";
            string label = BuildLabel(cond.Type ?? "isKin", "iskin", cond);

            string? heroA = TryGetHeroId(roleA, factsByRole, boundHeroes);
            if (string.IsNullOrEmpty(heroA))
            {
                return new ConditionEvaluationResult { Ok = false, Detail = $"{label} failed: role '{roleA}' unbound" };
            }
            string? heroB = TryGetHeroId(roleB, factsByRole, boundHeroes);
            if (string.IsNullOrEmpty(heroB))
            {
                return new ConditionEvaluationResult { Ok = false, Detail = $"{label} failed: role '{roleB}' unbound" };
            }

            if (context?.IsKin == null)
            {
                return new ConditionEvaluationResult { Ok = false, Detail = $"{label} failed: context.IsKin unavailable" };
            }

            bool expected = cond.Value ?? true;
            bool actual = context.IsKin(heroA!, heroB!);
            bool ok = (actual == expected);

            return new ConditionEvaluationResult
            {
                Ok = ok,
                Detail = ok
                    ? $"{label} ok"
                    : (expected ? $"{label} failed: {roleA} and {roleB} are not kin" : $"{label} failed: {roleA} and {roleB} are kin")
            };
        }

        private static ConditionEvaluationResult EvaluateIsLiegeOf(
            SituationConditionDef cond,
            IReadOnlyDictionary<string, SituationRoleFacts> factsByRole,
            IReadOnlyDictionary<string, string?>? boundHeroes,
            SituationWorldContext? context)
        {
            string roleA = cond.A ?? "a";
            string roleB = cond.B ?? "b";
            string label = BuildLabel(cond.Type ?? "isLiegeOf", "isliegeof", cond);

            string? heroA = TryGetHeroId(roleA, factsByRole, boundHeroes);
            if (string.IsNullOrEmpty(heroA))
            {
                return new ConditionEvaluationResult { Ok = false, Detail = $"{label} failed: role '{roleA}' unbound" };
            }
            string? heroB = TryGetHeroId(roleB, factsByRole, boundHeroes);
            if (string.IsNullOrEmpty(heroB))
            {
                return new ConditionEvaluationResult { Ok = false, Detail = $"{label} failed: role '{roleB}' unbound" };
            }

            if (context?.IsLiegeOf == null)
            {
                return new ConditionEvaluationResult { Ok = false, Detail = $"{label} failed: context.IsLiegeOf unavailable" };
            }

            bool expected = cond.Value ?? true;
            bool actual = context.IsLiegeOf(heroA!, heroB!);
            bool ok = (actual == expected);

            return new ConditionEvaluationResult
            {
                Ok = ok,
                Detail = ok
                    ? $"{label}: isLiegeOf ok"
                    : (expected ? $"{label}: isLiegeOf failed ({roleA} is not liege of {roleB})" : $"{label}: isLiegeOf failed ({roleA} is liege of {roleB})")
            };
        }

        private static ConditionEvaluationResult EvaluateBattleWon(
            SituationConditionDef cond,
            SituationWorldContext? context)
        {
            if (context?.BattleWon == null)
            {
                return new ConditionEvaluationResult { Ok = false, Detail = "missing context: battle won lookup unavailable" };
            }

            bool ok = context.BattleWon();
            return new ConditionEvaluationResult
            {
                Ok = ok,
                Detail = ok ? "battleWon ok" : "battleWon failed (battle was not won by player side)"
            };
        }

        private static string? TryGetHeroId(string? role, IReadOnlyDictionary<string, SituationRoleFacts> factsByRole, IReadOnlyDictionary<string, string?>? boundHeroes)
        {
            if (string.IsNullOrEmpty(role)) return null;
            if (factsByRole != null && factsByRole.TryGetValue(role!, out var f) && !string.IsNullOrEmpty(f.HeroId)) return f.HeroId;
            if (boundHeroes != null && boundHeroes.TryGetValue(role!, out var h) && !string.IsNullOrEmpty(h)) return h;
            return null;
        }

        private static double? ResolveThreshold(double? number, string? valueRef, VividWorldConfig? config)
        {
            if (number.HasValue) return number.Value;
            if (!string.IsNullOrEmpty(valueRef))
            {
                var secretVal = SecretConfigResolver.ResolveDouble(valueRef, config);
                if (secretVal.HasValue) return secretVal.Value;

                if (SituationConfigResolver.IsValidChanceRef(valueRef))
                {
                    return SituationConfigResolver.ResolveDouble(valueRef!, config);
                }
            }
            return null;
        }

        public static int GetTraitValue(TraitProfile profile, string traitName)
        {
            if (profile == null || string.IsNullOrEmpty(traitName)) return 0;
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

        private static ConditionEvaluationResult EvaluateHasStakeableSecret(
            SituationConditionDef cond,
            IReadOnlyDictionary<string, SituationRoleFacts> factsByRole,
            IReadOnlyDictionary<string, string?>? boundHeroes,
            SituationWorldContext? context)
        {
            string bettorRole = cond.Bettor ?? "challenged";
            string opponentRole = cond.Opponent ?? "challenger";

            string? bettorId = null;
            if (boundHeroes != null && boundHeroes.TryGetValue(bettorRole, out var bId) && !string.IsNullOrEmpty(bId))
            {
                bettorId = bId;
            }
            else if (factsByRole.TryGetValue(bettorRole, out var bFacts) && !string.IsNullOrEmpty(bFacts.HeroId))
            {
                bettorId = bFacts.HeroId;
            }

            string? opponentId = null;
            if (boundHeroes != null && boundHeroes.TryGetValue(opponentRole, out var oId) && !string.IsNullOrEmpty(oId))
            {
                opponentId = oId;
            }
            else if (factsByRole.TryGetValue(opponentRole, out var oFacts) && !string.IsNullOrEmpty(oFacts.HeroId))
            {
                opponentId = oFacts.HeroId;
            }

            if (string.IsNullOrEmpty(bettorId) || string.IsNullOrEmpty(opponentId))
            {
                return new ConditionEvaluationResult
                {
                    Ok = false,
                    Detail = $"hasStakeableSecret failed: bettor ('{bettorRole}') or opponent ('{opponentRole}') unbound"
                };
            }

            if (context == null)
            {
                return new ConditionEvaluationResult
                {
                    Ok = false,
                    Detail = "hasStakeableSecret failed: missing context"
                };
            }

            // 玩家是做決定的那位時這個分支不成立（玩家不押秘密）
            if (!string.IsNullOrEmpty(context.PlayerHeroId) && string.Equals(bettorId, context.PlayerHeroId, StringComparison.Ordinal))
            {
                return new ConditionEvaluationResult
                {
                    Ok = false,
                    Detail = $"hasStakeableSecret failed: bettor '{bettorId}' is player (player does not stake secrets)"
                };
            }

            if (context.GetKnownSecrets == null)
            {
                return new ConditionEvaluationResult
                {
                    Ok = false,
                    Detail = "hasStakeableSecret failed: GetKnownSecrets unavailable"
                };
            }

            var knownSecrets = context.GetKnownSecrets(bettorId!);
            var eval = StakeableSecretEvaluation.Evaluate(bettorId!, opponentId!, context.PlayerHeroId, knownSecrets);

            return new ConditionEvaluationResult
            {
                Ok = eval.IsEligible,
                Detail = eval.IsEligible
                    ? $"hasStakeableSecret ok: {bettorId} has qualified secret {eval.ChosenSecret?.EventId}"
                    : $"hasStakeableSecret failed: {eval.Reason}"
            };
        }
    }
}
