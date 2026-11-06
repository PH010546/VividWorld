using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;

namespace VividWorld.Core.Situations
{
    public static partial class SituationCatalogLoader
    {
        /// <summary>
        /// 讀角色的 <c>pickTraits</c>（挑人時看的個性係數）。個性名不認得、係數不是有限數的那一項忽略並報警告；
        /// 寫在推導角色或只綁玩家的角色上整個忽略並報警告。警告不擋載入：資料寫壞一項，情境照樣能用，只是那一項不看個性。
        /// 呼叫前要先把 <see cref="SituationRoleDef.Derived"/> 與 <see cref="SituationRoleDef.PlayerOnly"/> 設好。
        /// </summary>
        private static void ParsePickTraits(
            JObject roleObj,
            string roleName,
            SituationRoleDef roleDef,
            int situationIndex,
            string situationId,
            List<SituationIssue> issues)
        {
            var prop = roleObj.Property("pickTraits", StringComparison.OrdinalIgnoreCase);
            if (prop == null || prop.Value.Type == JTokenType.Null) return;

            string field = $"roles.{roleName}.pickTraits";

            if (roleDef.IsDerived || roleDef.PlayerOnly)
            {
                issues.Add(new SituationIssue
                {
                    SituationIndex = situationIndex,
                    SituationId = situationId,
                    Field = field,
                    Code = SituationIssueCode.PickTraitsOnWrongRole,
                    IsError = false,
                    Detail = roleDef.IsDerived
                        ? $"Role '{roleName}' is derived; pickTraits is ignored (derived roles are not picked by personality)."
                        : $"Role '{roleName}' is player-only; pickTraits is ignored."
                });
                return;
            }

            if (prop.Value is not JObject traitsObj)
            {
                issues.Add(new SituationIssue
                {
                    SituationIndex = situationIndex,
                    SituationId = situationId,
                    Field = field,
                    Code = SituationIssueCode.InvalidPickTraitCoefficient,
                    IsError = false,
                    Detail = "pickTraits must be an object mapping trait names to numbers; ignored."
                });
                return;
            }

            var result = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
            foreach (var tProp in traitsObj.Properties())
            {
                string tName = tProp.Name.Trim().ToLowerInvariant();
                if (!ValidTraits.Contains(tName))
                {
                    issues.Add(new SituationIssue
                    {
                        SituationIndex = situationIndex,
                        SituationId = situationId,
                        Field = $"{field}.{tProp.Name}",
                        Code = SituationIssueCode.UnknownPickTrait,
                        IsError = false,
                        Detail = $"Unknown trait '{tProp.Name}' in pickTraits, only honor/mercy/valor/calculating/generosity allowed; ignored."
                    });
                    continue;
                }

                bool numeric = tProp.Value.Type == JTokenType.Integer || tProp.Value.Type == JTokenType.Float;
                if (!numeric)
                {
                    issues.Add(new SituationIssue
                    {
                        SituationIndex = situationIndex,
                        SituationId = situationId,
                        Field = $"{field}.{tProp.Name}",
                        Code = SituationIssueCode.InvalidPickTraitCoefficient,
                        IsError = false,
                        Detail = "pickTraits coefficient must be a number; ignored."
                    });
                    continue;
                }

                double coef = tProp.Value.Value<double>();
                if (double.IsNaN(coef) || double.IsInfinity(coef))
                {
                    issues.Add(new SituationIssue
                    {
                        SituationIndex = situationIndex,
                        SituationId = situationId,
                        Field = $"{field}.{tProp.Name}",
                        Code = SituationIssueCode.InvalidPickTraitCoefficient,
                        IsError = false,
                        Detail = "pickTraits coefficient must be finite; ignored."
                    });
                    continue;
                }

                result[tName] = coef;
            }

            if (result.Count > 0) roleDef.PickTraits = result;
        }
    }
}
