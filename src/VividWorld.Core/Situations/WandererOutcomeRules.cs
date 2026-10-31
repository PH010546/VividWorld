using System;
using System.Collections.Generic;
using System.Linq;
using VividWorld.Core.Grudges;
using VividWorld.Core.Util;

namespace VividWorld.Core.Situations
{
    /// <summary>
    /// 比一比的結果。持有者是現在擔任那個職位的人（資料裡的 contest.a），挑戰者是 contest.b。
    /// </summary>
    public sealed class WandererContestResult
    {
        public int HolderSkill { get; set; }
        public int ChallengerSkill { get; set; }
        public int RandomMax { get; set; }
        public int HolderRoll { get; set; }
        public int ChallengerRoll { get; set; }
        public int HolderScore => HolderSkill + HolderRoll;
        public int ChallengerScore => ChallengerSkill + ChallengerRoll;

        /// <summary>平手時持有者留著（不是挑戰者贏）。</summary>
        public bool HolderWon { get; set; }
    }

    public static class WandererContestRules
    {
        /// <summary>
        /// 兩人各取技能值，各加 0～randomMax 的亂數（同一個種子各自推一個數，兩邊互不影響），
        /// 總分高的贏；平手持有者留著。亂數用整數、閉區間，randomMax 小於 0 當 0。
        /// </summary>
        public static WandererContestResult Decide(int holderSkill, int challengerSkill, int randomMax, long seed)
        {
            int max = Math.Max(0, randomMax);
            var rng = new SplitMix64Rng();
            var result = new WandererContestResult
            {
                HolderSkill = holderSkill,
                ChallengerSkill = challengerSkill,
                RandomMax = max,
                HolderRoll = rng.Pick(max + 1, RumorSeed.Of(seed, "holder")),
                ChallengerRoll = rng.Pick(max + 1, RumorSeed.Of(seed, "challenger"))
            };
            result.HolderWon = result.HolderScore >= result.ChallengerScore;
            return result;
        }
    }

    public static class WandererOutcomeRules
    {
        /// <summary>grudge 的 from／to 可以寫的比一比角色名：贏的那位與輸的那位。</summary>
        public const string WinnerKey = "@winner";
        public const string LoserKey = "@loser";

        /// <summary>
        /// 把做法的 grudges 裡 @winner／@loser 換成這次比一比真正的角色名（例如 holder、challenger），
        /// 回傳新的做法物件，原來的做法不動。恩怨要在比一比定案之後才套用，所以在執行前換。
        /// </summary>
        public static SituationBranchDef WithContestRoles(SituationBranchDef branch, string winnerRole, string loserRole)
        {
            if (branch == null) throw new ArgumentNullException(nameof(branch));

            string Map(string role)
            {
                if (string.Equals(role, WinnerKey, StringComparison.Ordinal)) return winnerRole;
                if (string.Equals(role, LoserKey, StringComparison.Ordinal)) return loserRole;
                return role;
            }

            return new SituationBranchDef
            {
                Id = branch.Id,
                Base = branch.Base,
                Traits = branch.Traits,
                Preconditions = branch.Preconditions,
                Events = branch.Events,
                Grudges = branch.Grudges?.Select(g => new SituationGrudgeDef
                {
                    From = Map(g.From),
                    To = Map(g.To),
                    Amount = g.Amount,
                    LedgerOnly = g.LedgerOnly,
                    Escalate = g.Escalate
                }).ToList(),
                MadeUpTalk = branch.MadeUpTalk,
                GrudgeWeight = branch.GrudgeWeight,
                Judge = branch.Judge,
                StandAside = branch.StandAside,
                SkillXp = branch.SkillXp,
                PartyRoleChange = branch.PartyRoleChange,
                Contest = branch.Contest,
                PlayerEffects = branch.PlayerEffects,
                Extra = branch.Extra
            };
        }

        /// <summary>
        /// 技能欄位的值：以 @ 開頭的是綁定名，查 bindings 得到技能的 StringId；查不到回傳 null。
        /// 其他的原樣回傳（技能名）。
        /// </summary>
        public static string? ResolveSkillKey(string? skill, IReadOnlyDictionary<string, string>? bindings)
        {
            if (string.IsNullOrEmpty(skill)) return null;
            if (!skill!.StartsWith("@", StringComparison.Ordinal)) return skill;
            if (bindings != null && bindings.TryGetValue(skill, out var value) && !string.IsNullOrEmpty(value))
            {
                return value;
            }
            return null;
        }
    }
}
