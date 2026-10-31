using System;
using System.Collections.Generic;
using VividWorld.Core.Grudges;
using VividWorld.Core.Situations;
using VividWorld.Core.Util;
using Xunit;

namespace VividWorld.Core.Tests
{
    public class WandererOutcomeRulesTests
    {
        [Fact]
        public void Contest_HigherSkillWinsWhenNoRandomness()
        {
            var r = WandererContestRules.Decide(50, 10, 0, 1L);
            Assert.True(r.HolderWon);

            var r2 = WandererContestRules.Decide(10, 50, 0, 1L);
            Assert.False(r2.HolderWon);
        }

        [Fact]
        public void Contest_TieKeepsHolder()
        {
            var r = WandererContestRules.Decide(30, 30, 0, 99L);
            Assert.Equal(r.HolderScore, r.ChallengerScore);
            Assert.True(r.HolderWon);
        }

        [Fact]
        public void Contest_RollsStayWithinZeroToRandomMax()
        {
            for (long seed = 0; seed < 300; seed++)
            {
                var r = WandererContestRules.Decide(40, 40, 60, seed);
                Assert.InRange(r.HolderRoll, 0, 60);
                Assert.InRange(r.ChallengerRoll, 0, 60);
            }
        }

        [Fact]
        public void Contest_NegativeRandomMaxIsTreatedAsZero()
        {
            var r = WandererContestRules.Decide(20, 19, -5, 7L);
            Assert.Equal(0, r.RandomMax);
            Assert.Equal(0, r.HolderRoll);
            Assert.Equal(0, r.ChallengerRoll);
            Assert.True(r.HolderWon);
        }

        [Fact]
        public void Contest_SameSeedSameResult()
        {
            var a = WandererContestRules.Decide(45, 47, 60, 12345L);
            var b = WandererContestRules.Decide(45, 47, 60, 12345L);
            Assert.Equal(a.HolderRoll, b.HolderRoll);
            Assert.Equal(a.ChallengerRoll, b.ChallengerRoll);
            Assert.Equal(a.HolderWon, b.HolderWon);
        }

        [Fact]
        public void Contest_RandomnessCanLetTheLowerSkillWin()
        {
            // 持有者技能比較高，亂數範圍夠大時挑戰者仍然會贏幾次，不是只看技能
            bool holderSeen = false;
            bool challengerSeen = false;
            for (long seed = 0; seed < 400 && !(holderSeen && challengerSeen); seed++)
            {
                var r = WandererContestRules.Decide(50, 40, 60, seed);
                if (r.HolderWon) holderSeen = true; else challengerSeen = true;
            }
            Assert.True(holderSeen);
            Assert.True(challengerSeen);
        }

        [Fact]
        public void WithContestRoles_ReplacesWinnerAndLoserInGrudgesOnly()
        {
            var branch = new SituationBranchDef
            {
                Id = "contest",
                Judge = true,
                Grudges = new List<SituationGrudgeDef>
                {
                    new SituationGrudgeDef { From = "@loser", To = "@winner", Amount = -2 },
                    new SituationGrudgeDef { From = "claimant", To = "rival", Amount = 4 }
                }
            };

            var resolved = WandererOutcomeRules.WithContestRoles(branch, "challenger", "incumbent");

            Assert.Equal("incumbent", resolved.Grudges![0].From);
            Assert.Equal("challenger", resolved.Grudges[0].To);
            Assert.Equal(-2, resolved.Grudges[0].Amount);
            Assert.Equal("claimant", resolved.Grudges[1].From);
            Assert.Equal("rival", resolved.Grudges[1].To);

            // 原來的做法不動（同一份資料要能重複拿來比）
            Assert.Equal("@loser", branch.Grudges![0].From);
            Assert.Equal("@winner", branch.Grudges[0].To);
            Assert.True(resolved.Judge);
            Assert.Equal("contest", resolved.Id);
        }

        [Fact]
        public void ResolveSkillKey_ReadsBindingsAndPassesSkillNamesThrough()
        {
            var bindings = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["@disputedRoleSkill"] = "Scouting",
                ["@taught"] = "Medicine"
            };

            Assert.Equal("Scouting", WandererOutcomeRules.ResolveSkillKey("@disputedRoleSkill", bindings));
            Assert.Equal("Medicine", WandererOutcomeRules.ResolveSkillKey("@taught", bindings));
            Assert.Equal("Engineering", WandererOutcomeRules.ResolveSkillKey("Engineering", bindings));
            Assert.Null(WandererOutcomeRules.ResolveSkillKey("@disputedRoleSkill", new Dictionary<string, string>()));
            Assert.Null(WandererOutcomeRules.ResolveSkillKey(null, bindings));
        }
    }
}
