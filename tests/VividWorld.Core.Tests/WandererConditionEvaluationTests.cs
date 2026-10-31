using System.Collections.Generic;
using VividWorld.Core.Situations;
using Xunit;

namespace VividWorld.Core.Tests
{
    public class WandererConditionEvaluationTests
    {
        [Fact]
        public void Evaluate_PartyRoleHolder_TrueWhenHeroHoldsRole()
        {
            var cond = new SituationConditionDef
            {
                Type = "partyRoleHolder",
                Role = "claimant"
            };

            var factsByRole = new Dictionary<string, SituationRoleFacts>
            {
                ["claimant"] = new SituationRoleFacts { HeroId = "hero_a" }
            };

            var context = new SituationWorldContext
            {
                IsPartyRoleHolder = id => id == "hero_a"
            };

            var res = SituationConditionEvaluator.Evaluate(cond, factsByRole, context: context);
            Assert.True(res.Ok);

            var contextFalse = new SituationWorldContext
            {
                IsPartyRoleHolder = id => id == "hero_b"
            };
            var resFalse = SituationConditionEvaluator.Evaluate(cond, factsByRole, context: contextFalse);
            Assert.False(resFalse.Ok);
        }

        [Fact]
        public void Evaluate_PartyRoleHolder_FailsWhenUnboundOrMissingContext()
        {
            var cond = new SituationConditionDef
            {
                Type = "partyRoleHolder",
                Role = "claimant"
            };

            var factsByRole = new Dictionary<string, SituationRoleFacts>();
            var resUnbound = SituationConditionEvaluator.Evaluate(cond, factsByRole, context: new SituationWorldContext());
            Assert.False(resUnbound.Ok);

            factsByRole["claimant"] = new SituationRoleFacts { HeroId = "hero_a" };
            var resNoContext = SituationConditionEvaluator.Evaluate(cond, factsByRole, context: null);
            Assert.False(resNoContext.Ok);
        }

        [Fact]
        public void Evaluate_TeachableSkill_TrueWhenTeacherCanTeachLearner()
        {
            var cond = new SituationConditionDef
            {
                Type = "teachableSkill",
                Teacher = "mentor",
                Learner = "pupil"
            };

            var factsByRole = new Dictionary<string, SituationRoleFacts>
            {
                ["mentor"] = new SituationRoleFacts { HeroId = "h_mentor" },
                ["pupil"] = new SituationRoleFacts { HeroId = "h_pupil" }
            };

            var context = new SituationWorldContext
            {
                HasTeachableSkill = (t, l) => t == "h_mentor" && l == "h_pupil"
            };

            var res = SituationConditionEvaluator.Evaluate(cond, factsByRole, context: context);
            Assert.True(res.Ok);

            var contextFalse = new SituationWorldContext
            {
                HasTeachableSkill = (t, l) => false
            };
            var resFalse = SituationConditionEvaluator.Evaluate(cond, factsByRole, context: contextFalse);
            Assert.False(resFalse.Ok);
        }

        [Fact]
        public void Evaluate_SkillBelow_ComparesSkillsProperly()
        {
            var cond = new SituationConditionDef
            {
                Type = "skillBelow",
                A = "pupil",
                B = "mentor",
                Skill = "tactics"
            };

            var factsByRole = new Dictionary<string, SituationRoleFacts>
            {
                ["pupil"] = new SituationRoleFacts { HeroId = "h_pupil" },
                ["mentor"] = new SituationRoleFacts { HeroId = "h_mentor" }
            };

            var context = new SituationWorldContext
            {
                IsSkillBelow = (a, b, s) => a == "h_pupil" && b == "h_mentor" && s == "tactics"
            };

            var res = SituationConditionEvaluator.Evaluate(cond, factsByRole, context: context);
            Assert.True(res.Ok);

            var contextFalse = new SituationWorldContext
            {
                IsSkillBelow = (a, b, s) => false
            };
            var resFalse = SituationConditionEvaluator.Evaluate(cond, factsByRole, context: contextFalse);
            Assert.False(resFalse.Ok);
        }

        [Fact]
        public void Evaluate_FoughtInBattle_TrueWhenAllRolesFought()
        {
            var cond = new SituationConditionDef
            {
                Type = "foughtInBattle",
                Roles = new List<string> { "hero1", "hero2" }
            };

            var factsByRole = new Dictionary<string, SituationRoleFacts>
            {
                ["hero1"] = new SituationRoleFacts { HeroId = "h1" },
                ["hero2"] = new SituationRoleFacts { HeroId = "h2" }
            };

            var context = new SituationWorldContext
            {
                FoughtInBattle = id => id == "h1" || id == "h2"
            };

            var res = SituationConditionEvaluator.Evaluate(cond, factsByRole, context: context);
            Assert.True(res.Ok);

            var contextPartial = new SituationWorldContext
            {
                FoughtInBattle = id => id == "h1" // h2 did not fight
            };
            var resPartial = SituationConditionEvaluator.Evaluate(cond, factsByRole, context: contextPartial);
            Assert.False(resPartial.Ok);
        }

        [Fact]
        public void Evaluate_WoundedInBattle_ChecksWoundedStatus()
        {
            var cond = new SituationConditionDef
            {
                Type = "woundedInBattle",
                Role = "victim"
            };

            var factsByRole = new Dictionary<string, SituationRoleFacts>
            {
                ["victim"] = new SituationRoleFacts { HeroId = "h_vic" }
            };

            var context = new SituationWorldContext
            {
                WoundedInBattle = id => id == "h_vic"
            };

            var res = SituationConditionEvaluator.Evaluate(cond, factsByRole, context: context);
            Assert.True(res.Ok);

            var contextFalse = new SituationWorldContext
            {
                WoundedInBattle = id => false
            };
            var resFalse = SituationConditionEvaluator.Evaluate(cond, factsByRole, context: contextFalse);
            Assert.False(resFalse.Ok);
        }

        [Fact]
        public void Evaluate_BattleWon_ChecksWinningOutcome()
        {
            var cond = new SituationConditionDef
            {
                Type = "battleWon"
            };

            var contextWon = new SituationWorldContext
            {
                BattleWon = () => true
            };
            var resWon = SituationConditionEvaluator.Evaluate(cond, new Dictionary<string, SituationRoleFacts>(), context: contextWon);
            Assert.True(resWon.Ok);

            var contextLost = new SituationWorldContext
            {
                BattleWon = () => false
            };
            var resLost = SituationConditionEvaluator.Evaluate(cond, new Dictionary<string, SituationRoleFacts>(), context: contextLost);
            Assert.False(resLost.Ok);
        }
    }
}
