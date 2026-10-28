using VividWorld.Core.Events;
using VividWorld.Core.Grudges;
using Xunit;

namespace VividWorld.Core.Tests
{
    public class EffectiveGrudgePairRuleTests
    {
        [Fact]
        public void SwitchOff_Situation_AboutPlayer_WritesEffectivePair_AndSkipsClanEscalation()
        {
            const string playerHeroId = "hero_player";
            var decision = EffectiveGrudgePairRule.Evaluate(
                personalWithPlayerEnabled: false,
                source: GrudgeSource.Situation,
                aboutHeroId: playerHeroId,
                playerHeroId: playerHeroId);

            Assert.True(decision.UseEffectivePair);
            Assert.False(decision.ShouldEvaluateClanEscalation);
            Assert.Equal("per-person relations with the player are off, the grudge already landed on the pair the game shows", decision.Reason);

            var boolDecision = EffectiveGrudgePairRule.Evaluate(
                personalWithPlayerEnabled: false,
                source: GrudgeSource.Situation,
                isAboutPlayer: true);

            Assert.True(boolDecision.UseEffectivePair);
            Assert.False(boolDecision.ShouldEvaluateClanEscalation);
            Assert.Equal("per-person relations with the player are off, the grudge already landed on the pair the game shows", boolDecision.Reason);
        }

        [Fact]
        public void SwitchOn_Situation_AboutPlayer_WritesDirectPair_AndEvaluatesClanEscalation()
        {
            const string playerHeroId = "hero_player";
            var decision = EffectiveGrudgePairRule.Evaluate(
                personalWithPlayerEnabled: true,
                source: GrudgeSource.Situation,
                aboutHeroId: playerHeroId,
                playerHeroId: playerHeroId);

            Assert.False(decision.UseEffectivePair);
            Assert.True(decision.ShouldEvaluateClanEscalation);
            Assert.Null(decision.Reason);

            var boolDecision = EffectiveGrudgePairRule.Evaluate(
                personalWithPlayerEnabled: true,
                source: GrudgeSource.Situation,
                isAboutPlayer: true);

            Assert.False(boolDecision.UseEffectivePair);
            Assert.True(boolDecision.ShouldEvaluateClanEscalation);
            Assert.Null(boolDecision.Reason);
        }

        [Fact]
        public void SwitchOff_Situation_AboutOtherNpc_WritesDirectPair_AndEvaluatesClanEscalation()
        {
            const string playerHeroId = "hero_player";
            var decision = EffectiveGrudgePairRule.Evaluate(
                personalWithPlayerEnabled: false,
                source: GrudgeSource.Situation,
                aboutHeroId: "lord_other_npc",
                playerHeroId: playerHeroId);

            Assert.False(decision.UseEffectivePair);
            Assert.True(decision.ShouldEvaluateClanEscalation);
            Assert.Null(decision.Reason);

            var boolDecision = EffectiveGrudgePairRule.Evaluate(
                personalWithPlayerEnabled: false,
                source: GrudgeSource.Situation,
                isAboutPlayer: false);

            Assert.False(boolDecision.UseEffectivePair);
            Assert.True(boolDecision.ShouldEvaluateClanEscalation);
            Assert.Null(boolDecision.Reason);
        }

        [Fact]
        public void SwitchOff_Rumor_AboutPlayer_WritesEffectivePair_AndDoesNotEvaluateClanEscalation()
        {
            const string playerHeroId = "hero_player";
            var decision = EffectiveGrudgePairRule.Evaluate(
                personalWithPlayerEnabled: false,
                source: GrudgeSource.Rumor,
                aboutHeroId: playerHeroId,
                playerHeroId: playerHeroId);

            Assert.True(decision.UseEffectivePair);
            Assert.False(decision.ShouldEvaluateClanEscalation);
            Assert.Equal("per-person relations with the player are off, the grudge already landed on the pair the game shows", decision.Reason);

            var boolDecision = EffectiveGrudgePairRule.Evaluate(
                personalWithPlayerEnabled: false,
                source: GrudgeSource.Rumor,
                isAboutPlayer: true);

            Assert.True(boolDecision.UseEffectivePair);
            Assert.False(boolDecision.ShouldEvaluateClanEscalation);
            Assert.Equal("per-person relations with the player are off, the grudge already landed on the pair the game shows", boolDecision.Reason);
        }

        [Fact]
        public void SwitchOn_Rumor_AboutPlayer_WritesDirectPair_AndDoesNotEvaluateClanEscalation()
        {
            const string playerHeroId = "hero_player";
            var decision = EffectiveGrudgePairRule.Evaluate(
                personalWithPlayerEnabled: true,
                source: GrudgeSource.Rumor,
                aboutHeroId: playerHeroId,
                playerHeroId: playerHeroId);

            Assert.False(decision.UseEffectivePair);
            Assert.False(decision.ShouldEvaluateClanEscalation);
            Assert.Null(decision.Reason);

            var boolDecision = EffectiveGrudgePairRule.Evaluate(
                personalWithPlayerEnabled: true,
                source: GrudgeSource.Rumor,
                isAboutPlayer: true);

            Assert.False(boolDecision.UseEffectivePair);
            Assert.False(boolDecision.ShouldEvaluateClanEscalation);
            Assert.Null(boolDecision.Reason);
        }

        [Fact]
        public void SwitchOff_Rumor_AboutOtherNpc_WritesDirectPair_AndDoesNotEvaluateClanEscalation()
        {
            const string playerHeroId = "hero_player";
            var decision = EffectiveGrudgePairRule.Evaluate(
                personalWithPlayerEnabled: false,
                source: GrudgeSource.Rumor,
                aboutHeroId: "lord_other_npc",
                playerHeroId: playerHeroId);

            Assert.False(decision.UseEffectivePair);
            Assert.False(decision.ShouldEvaluateClanEscalation);
            Assert.Null(decision.Reason);

            var boolDecision = EffectiveGrudgePairRule.Evaluate(
                personalWithPlayerEnabled: false,
                source: GrudgeSource.Rumor,
                isAboutPlayer: false);

            Assert.False(boolDecision.UseEffectivePair);
            Assert.False(boolDecision.ShouldEvaluateClanEscalation);
            Assert.Null(boolDecision.Reason);
        }

        [Fact]
        public void RetractPair_WhenRecordedNativePairIsClanLeader_ReturnsRecordedPair()
        {
            const string knowerId = "lord_knower";
            const string aboutId = "hero_player";
            var recordedPair = new[] { "lord_clan_leader", "hero_player" };

            var pair = EffectiveGrudgePairRule.ResolveRetractionPair(knowerId, aboutId, recordedPair);

            Assert.Equal("lord_clan_leader", pair.Hero1);
            Assert.Equal("hero_player", pair.Hero2);
        }

        [Fact]
        public void RetractPair_WhenRecordedNativePairIsDirectHero_ReturnsDirectPair()
        {
            const string knowerId = "lord_knower";
            const string aboutId = "hero_player";
            var recordedPair = new[] { knowerId, aboutId };

            var pair = EffectiveGrudgePairRule.ResolveRetractionPair(knowerId, aboutId, recordedPair);

            Assert.Equal(knowerId, pair.Hero1);
            Assert.Equal(aboutId, pair.Hero2);
        }

        [Fact]
        public void RetractPair_WhenRecordedNativePairIsNullOrFewerThanTwo_FallsBackToKnowerAndAbout()
        {
            const string knowerId = "lord_knower";
            const string aboutId = "hero_player";

            var pairNull = EffectiveGrudgePairRule.ResolveRetractionPair(knowerId, aboutId, null);
            Assert.Equal(knowerId, pairNull.Hero1);
            Assert.Equal(aboutId, pairNull.Hero2);

            var pairEmpty = EffectiveGrudgePairRule.ResolveRetractionPair(knowerId, aboutId, new string[0]);
            Assert.Equal(knowerId, pairEmpty.Hero1);
            Assert.Equal(aboutId, pairEmpty.Hero2);

            var pairSingle = EffectiveGrudgePairRule.ResolveRetractionPair(knowerId, aboutId, new[] { "lord_clan_leader" });
            Assert.Equal(knowerId, pairSingle.Hero1);
            Assert.Equal(aboutId, pairSingle.Hero2);

            var pairEmptyItem = EffectiveGrudgePairRule.ResolveRetractionPair(knowerId, aboutId, new[] { "", "hero_player" });
            Assert.Equal(knowerId, pairEmptyItem.Hero1);
            Assert.Equal(aboutId, pairEmptyItem.Hero2);
        }

        [Fact]
        public void LogFormatter_FormatAppliedNativeOnEffectivePair_MatchesExactString()
        {
            string line = GrudgeLogFormatter.FormatAppliedNativeOnEffectivePair(
                fromRole: "slighted",
                fromHeroId: "lord_4_15",
                toRole: "favored",
                toHeroId: "hero_player",
                requested: -8.0,
                nativeDelta: -8,
                before: 12,
                after: 4,
                effectiveHero1: "lord_4_1",
                effectiveHero2: "hero_player");

            Assert.Equal("grudge slighted lord_4_15 -> favored hero_player: requested -8.0 personal, native -8 (base 12 -> 4) on effective pair lord_4_1 <-> hero_player (per-person relations with the player are off)", line);
        }

        [Fact]
        public void LogFormatter_FormatOpinionAppliedNativeOnEffectivePair_MatchesExactString()
        {
            string line = GrudgeLogFormatter.FormatOpinionAppliedNativeOnEffectivePair(
                observer: "lord_4_15",
                aboutHeroId: "hero_player",
                aboutRole: "accused",
                eventId: "evt_test_1",
                amount: -12.0,
                hop: 1,
                hopConfidence: 1.0,
                isWitness: false,
                multiplier: 1.0,
                fullAmount: -12.0,
                alreadyApplied: 0.0,
                requested: -12.0,
                nativeBefore: 10,
                nativeAfter: -2,
                nativeDelta: -12,
                effectiveHero1: "lord_4_1",
                effectiveHero2: "hero_player",
                reactionSuffix: "trait x1.00 (none), receiver x1.00 (none)");

            Assert.Equal("opinion applied lord_4_15 -> hero_player (accused) on evt_test_1: template -12.00, hop 1 x1.00, onlooker x1.00 => full -12.00, already 0.00, requested -12.00, native 10 -> -2 (-12), trait x1.00 (none), receiver x1.00 (none) on effective pair lord_4_1 <-> hero_player (per-person relations with the player are off)", line);
        }

        [Fact]
        public void LogFormatter_FormatRetractedNative_MatchesExactString()
        {
            string line = GrudgeLogFormatter.FormatRetractedNative(
                knowerHeroId: "lord_4_15",
                aboutHeroId: "hero_player",
                reverseDelta: 12,
                originalDelta: -12.0,
                before: -2,
                after: 10,
                eventId: "evt_test_1");

            Assert.Equal("Retracted native relation: lord_4_15 toward hero_player reversed by 12 (original delta -12): before -2, after 10 on evt_test_1", line);
        }

        [Fact]
        public void LogFormatter_FormatRetractedNativeOnRecordedPair_MatchesExactString()
        {
            string line = GrudgeLogFormatter.FormatRetractedNativeOnRecordedPair(
                knowerHeroId: "lord_4_15",
                aboutHeroId: "hero_player",
                reverseDelta: 12,
                originalDelta: -12.0,
                before: -2,
                after: 10,
                eventId: "evt_test_1",
                recordedHero1: "lord_4_1",
                recordedHero2: "hero_player");

            Assert.Equal("Retracted native relation: lord_4_15 toward hero_player reversed by 12 (original delta -12): before -2, after 10 on evt_test_1 on recorded pair lord_4_1 <-> hero_player", line);
        }

        [Fact]
        public void LogFormatter_FormatEscalationNotEscalatedEffectivePair_MatchesExactString()
        {
            string line = GrudgeLogFormatter.FormatEscalationNotEscalatedEffectivePair(
                fromHeroId: "lord_4_15",
                toHeroId: "hero_player");

            Assert.Equal("escalation lord_4_15 -> hero_player: not escalated - per-person relations with the player are off, the grudge already landed on the pair the game shows", line);
        }
    }
}
