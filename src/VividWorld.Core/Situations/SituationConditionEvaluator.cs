using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

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
            IReadOnlyCollection<string>? nonDerivedHeroIds = null)
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

                case "clantiercompare":
                    result = EvaluateClanTierCompare(condition, factsByRole);
                    break;

                case "rolebound":
                    result = EvaluateRoleBound(condition, boundHeroes, unboundReasons);
                    break;

                case "cooldown":
                    result = EvaluateCooldown(condition, day, situationId, history, nonDerivedHeroIds);
                    break;

                default:
                    result = new ConditionEvaluationResult
                    {
                        Ok = false,
                        Detail = $"unknown condition type '{type}'"
                    };
                    break;
            }

            result.Type = condition.Type ?? string.Empty;
            result.Label = BuildLabel(result.Type, type.ToLowerInvariant(), condition);
            return result;
        }

        /// <summary>聚合鍵：會在同一個情境裡出現不只一次的條件要帶上角色，其餘就用型別本身。</summary>
        private static string BuildLabel(string type, string lowerType, SituationConditionDef condition)
        {
            if (string.Equals(lowerType, "isclanleader", StringComparison.Ordinal))
            {
                return $"{type}({condition.Role ?? "role"})";
            }

            return type;
        }

        public static IReadOnlyList<ConditionEvaluationResult> EvaluateAll(
            IEnumerable<SituationConditionDef> conditions,
            IReadOnlyDictionary<string, SituationRoleFacts> factsByRole,
            IReadOnlyDictionary<string, string?>? boundHeroes = null,
            IReadOnlyDictionary<string, string>? unboundReasons = null,
            string? situationId = null,
            double day = 0.0,
            ISituationHistory? history = null,
            IReadOnlyCollection<string>? nonDerivedHeroIds = null)
        {
            var results = new List<ConditionEvaluationResult>();
            if (conditions != null)
            {
                foreach (var cond in conditions)
                {
                    results.Add(Evaluate(cond, factsByRole, boundHeroes, unboundReasons, situationId, day, history, nonDerivedHeroIds));
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
    }
}
