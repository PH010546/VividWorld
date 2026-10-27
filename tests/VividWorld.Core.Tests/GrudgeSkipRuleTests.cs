using VividWorld.Core.Events;
using VividWorld.Core.Grudges;
using Xunit;

namespace VividWorld.Core.Tests
{
    public class GrudgeSkipRuleTests
    {
        [Fact]
        public void Situation_AboutHeroIsPlayer_DoesNotSkip()
        {
            const string playerHeroId = "hero_player";
            var decision = GrudgeSkipRule.Evaluate(
                GrudgeSource.Situation,
                fromHeroId: "lord_aggrieved",
                aboutHeroId: playerHeroId,
                fromRole: "aggrieved",
                toRole: "target",
                playerHeroId: playerHeroId,
                eventId: "evt_sit_1");

            Assert.False(decision.ShouldSkip);
            Assert.Equal(GrudgeSkipReason.None, decision.ReasonCode);
            Assert.Null(decision.Reason);
            Assert.Null(decision.LogMessage);
        }

        [Fact]
        public void Situation_FromHeroIsPlayer_Skips_WithOriginalLogReason()
        {
            const string playerHeroId = "hero_player";
            var decision = GrudgeSkipRule.Evaluate(
                GrudgeSource.Situation,
                fromHeroId: playerHeroId,
                aboutHeroId: "lord_patron",
                fromRole: "patron",
                toRole: "aggrieved",
                playerHeroId: playerHeroId,
                eventId: "evt_sit_1");

            Assert.True(decision.ShouldSkip);
            Assert.Equal(GrudgeSkipReason.FromIsPlayer, decision.ReasonCode);
            Assert.Equal("hero_player is the player", decision.Reason);
            Assert.Equal("grudge skipped patron hero_player -> aggrieved lord_patron: hero_player is the player", decision.LogMessage);
        }

        [Fact]
        public void Rumor_AboutHeroIsPlayer_DoesNotSkip()
        {
            const string playerHeroId = "hero_player";
            var decision = GrudgeSkipRule.Evaluate(
                GrudgeSource.Rumor,
                fromHeroId: "lord_listener",
                aboutHeroId: playerHeroId,
                fromRole: "listener",
                toRole: "subject",
                playerHeroId: playerHeroId,
                eventId: "evt_rumor_1");

            Assert.False(decision.ShouldSkip);
            Assert.Equal(GrudgeSkipReason.None, decision.ReasonCode);
            Assert.Null(decision.Reason);
            Assert.Null(decision.LogMessage);
        }

        [Fact]
        public void Rumor_FromHeroIsPlayer_Skips_WithObserverIsThePlayer()
        {
            const string playerHeroId = "hero_player";
            var decision = GrudgeSkipRule.Evaluate(
                GrudgeSource.Rumor,
                fromHeroId: playerHeroId,
                aboutHeroId: "lord_subject",
                fromRole: "listener",
                toRole: "subject",
                playerHeroId: playerHeroId,
                eventId: "evt_rumor_1");

            Assert.True(decision.ShouldSkip);
            Assert.Equal(GrudgeSkipReason.FromIsPlayer, decision.ReasonCode);
            Assert.Equal("observer is the player", decision.Reason);
            Assert.Equal("opinion skipped hero_player -> subject on evt_rumor_1: observer is the player", decision.LogMessage);
        }

        [Fact]
        public void UnboundRole_OrSameHero_Skips()
        {
            // 沒綁到
            var unbound = GrudgeSkipRule.Evaluate(GrudgeSource.Situation, "", "lord_target", "r1", "r2", "player", "e1");
            Assert.True(unbound.ShouldSkip);

            // 同一人
            var same = GrudgeSkipRule.Evaluate(GrudgeSource.Situation, "lord_x", "lord_x", "r1", "r2", "player", "e1");
            Assert.True(same.ShouldSkip);
        }

        [Theory]
        [InlineData(GrudgeSource.Situation)]
        [InlineData(GrudgeSource.Rumor)]
        public void UnboundRole_Skips(GrudgeSource source)
        {
            var dFromUnbound = GrudgeSkipRule.Evaluate(
                source,
                fromHeroId: "",
                aboutHeroId: "lord_target",
                fromRole: "source_role",
                toRole: "target_role",
                playerHeroId: "hero_player",
                eventId: "evt_1");

            Assert.True(dFromUnbound.ShouldSkip);
            Assert.Equal(GrudgeSkipReason.UnboundRole, dFromUnbound.ReasonCode);
            Assert.Equal("role 'source_role' is unbound", dFromUnbound.Reason);
            if (source == GrudgeSource.Situation)
            {
                Assert.Equal("grudge skipped source_role  -> target_role lord_target: role 'source_role' is unbound", dFromUnbound.LogMessage);
            }
            else
            {
                Assert.Equal("opinion skipped  -> target_role on evt_1: role 'source_role' is unbound", dFromUnbound.LogMessage);
            }

            var dAboutUnbound = GrudgeSkipRule.Evaluate(
                source,
                fromHeroId: "lord_source",
                aboutHeroId: null,
                fromRole: "source_role",
                toRole: "target_role",
                playerHeroId: "hero_player",
                eventId: "evt_1");

            Assert.True(dAboutUnbound.ShouldSkip);
            Assert.Equal(GrudgeSkipReason.UnboundRole, dAboutUnbound.ReasonCode);
            Assert.Equal("role 'target_role' is unbound", dAboutUnbound.Reason);
            if (source == GrudgeSource.Situation)
            {
                Assert.Equal("grudge skipped source_role lord_source -> target_role : role 'target_role' is unbound", dAboutUnbound.LogMessage);
            }
            else
            {
                Assert.Equal("opinion skipped lord_source -> target_role on evt_1: role 'target_role' is unbound", dAboutUnbound.LogMessage);
            }
        }

        [Theory]
        [InlineData(GrudgeSource.Situation)]
        [InlineData(GrudgeSource.Rumor)]
        public void SameHero_Skips(GrudgeSource source)
        {
            var decision = GrudgeSkipRule.Evaluate(
                source,
                fromHeroId: "lord_same",
                aboutHeroId: "lord_same",
                fromRole: "role_a",
                toRole: "role_b",
                playerHeroId: "hero_player",
                eventId: "evt_1");

            Assert.True(decision.ShouldSkip);
            Assert.Equal(GrudgeSkipReason.SameHero, decision.ReasonCode);
            if (source == GrudgeSource.Situation)
            {
                Assert.Equal("from and about are the same hero", decision.Reason);
                Assert.Equal("grudge skipped role_a lord_same -> role_b lord_same: from and about are the same hero", decision.LogMessage);
            }
            else
            {
                Assert.Equal("target is the observer self", decision.Reason);
                Assert.Equal("opinion skipped lord_same -> role_b on evt_1: target is the observer self", decision.LogMessage);
            }
        }
    }
}
