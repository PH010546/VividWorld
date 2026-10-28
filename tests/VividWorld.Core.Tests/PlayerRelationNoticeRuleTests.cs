using VividWorld.Core.Events;
using VividWorld.Core.Grudges;
using Xunit;

namespace VividWorld.Core.Tests
{
    public class PlayerRelationNoticeRuleTests
    {
        [Fact]
        public void Situation_WrittenToDirectPerson_NegativeDelta_NotifiesPersonDecreased()
        {
            var notice = PlayerRelationNoticeRule.Evaluate(
                source: GrudgeSource.Situation,
                ledgerOnly: false,
                targetAId: "caladog",
                targetBId: "player",
                fromHeroId: "caladog",
                aboutHeroId: "player",
                playerHeroId: "player",
                delta: -5);

            Assert.True(notice.ShouldNotify);
            Assert.Null(notice.SkipReason);
            Assert.Equal("caladog", notice.OtherHeroId);
            Assert.Equal("caladog", notice.OriginalFromHeroId);
            Assert.Equal(PlayerRelationNoticeKind.Person, notice.Kind);
            Assert.Equal(PlayerRelationDirection.Decreased, notice.Direction);
            Assert.Equal(5, notice.Magnitude);
            Assert.Equal(-5, notice.Delta);
        }

        [Fact]
        public void Situation_WrittenToClanLeader_NegativeDelta_NotifiesClanDecreased()
        {
            var notice = PlayerRelationNoticeRule.Evaluate(
                source: GrudgeSource.Situation,
                ledgerOnly: false,
                targetAId: "derthert",
                targetBId: "player",
                fromHeroId: "caladog",
                aboutHeroId: "player",
                playerHeroId: "player",
                delta: -5);

            Assert.True(notice.ShouldNotify);
            Assert.Null(notice.SkipReason);
            Assert.Equal("derthert", notice.OtherHeroId);
            Assert.Equal("caladog", notice.OriginalFromHeroId);
            Assert.Equal(PlayerRelationNoticeKind.Clan, notice.Kind);
            Assert.Equal(PlayerRelationDirection.Decreased, notice.Direction);
            Assert.Equal(5, notice.Magnitude);
            Assert.Equal(-5, notice.Delta);
        }

        [Fact]
        public void ClanEscalation_OriginalCaladog_WrittenToClanLeader_DeltaNegative4_NotifiesClan()
        {
            var notice = PlayerRelationNoticeRule.Evaluate(
                source: GrudgeSource.Situation,
                ledgerOnly: false,
                targetAId: "derthert",
                targetBId: "player",
                fromHeroId: "caladog",
                aboutHeroId: "player",
                playerHeroId: "player",
                delta: -4);

            Assert.True(notice.ShouldNotify);
            Assert.Null(notice.SkipReason);
            Assert.Equal("derthert", notice.OtherHeroId);
            Assert.Equal("caladog", notice.OriginalFromHeroId);
            Assert.Equal(PlayerRelationNoticeKind.Clan, notice.Kind);
            Assert.Equal(PlayerRelationDirection.Decreased, notice.Direction);
            Assert.Equal(4, notice.Magnitude);
            Assert.Equal(-4, notice.Delta);
        }

        [Fact]
        public void OriginalHeroIsClanLeader_WrittenToSelf_NotifiesPerson()
        {
            var notice = PlayerRelationNoticeRule.Evaluate(
                source: GrudgeSource.Situation,
                ledgerOnly: false,
                targetAId: "derthert",
                targetBId: "player",
                fromHeroId: "derthert",
                aboutHeroId: "player",
                playerHeroId: "player",
                delta: -5);

            Assert.True(notice.ShouldNotify);
            Assert.Equal(PlayerRelationNoticeKind.Person, notice.Kind);
            Assert.Equal("derthert", notice.OtherHeroId);
        }

        [Fact]
        public void DeltaPositive3_NotifiesIncreased()
        {
            var notice = PlayerRelationNoticeRule.Evaluate(
                source: GrudgeSource.Situation,
                ledgerOnly: false,
                targetAId: "caladog",
                targetBId: "player",
                fromHeroId: "caladog",
                aboutHeroId: "player",
                playerHeroId: "player",
                delta: 3);

            Assert.True(notice.ShouldNotify);
            Assert.Equal(PlayerRelationDirection.Increased, notice.Direction);
            Assert.Equal(3, notice.Magnitude);
            Assert.Equal(3, notice.Delta);
        }

        [Fact]
        public void DeltaZero_DoesNotNotify_ReasonNoChange()
        {
            var notice = PlayerRelationNoticeRule.Evaluate(
                source: GrudgeSource.Situation,
                ledgerOnly: false,
                targetAId: "caladog",
                targetBId: "player",
                fromHeroId: "caladog",
                aboutHeroId: "player",
                playerHeroId: "player",
                delta: 0);

            Assert.False(notice.ShouldNotify);
            Assert.Equal(PlayerRelationNoticeRule.ReasonNoChange, notice.SkipReason);
            Assert.Equal("caladog", notice.OtherHeroId);
        }

        [Fact]
        public void LedgerOnly_DoesNotNotify_ReasonLedgerOnly()
        {
            var notice = PlayerRelationNoticeRule.Evaluate(
                source: GrudgeSource.Situation,
                ledgerOnly: true,
                targetAId: "caladog",
                targetBId: "player",
                fromHeroId: "caladog",
                aboutHeroId: "player",
                playerHeroId: "player",
                delta: -5);

            Assert.False(notice.ShouldNotify);
            Assert.Equal(PlayerRelationNoticeRule.ReasonLedgerOnly, notice.SkipReason);
            Assert.Equal("caladog", notice.OtherHeroId);
        }

        [Fact]
        public void SourceIsRumor_DoesNotNotify_ReasonRumor()
        {
            var notice = PlayerRelationNoticeRule.Evaluate(
                source: GrudgeSource.Rumor,
                ledgerOnly: false,
                targetAId: "caladog",
                targetBId: "player",
                fromHeroId: "caladog",
                aboutHeroId: "player",
                playerHeroId: "player",
                delta: -5);

            Assert.False(notice.ShouldNotify);
            Assert.Equal(PlayerRelationNoticeRule.ReasonRumor, notice.SkipReason);
            Assert.Equal("caladog", notice.OtherHeroId);
        }

        [Fact]
        public void NeitherIsPlayer_DoesNotNotify_ReasonNotAboutPlayer()
        {
            var notice = PlayerRelationNoticeRule.Evaluate(
                source: GrudgeSource.Situation,
                ledgerOnly: false,
                targetAId: "caladog",
                targetBId: "derthert",
                fromHeroId: "caladog",
                aboutHeroId: "derthert",
                playerHeroId: "player",
                delta: -5);

            Assert.False(notice.ShouldNotify);
            Assert.Equal(PlayerRelationNoticeRule.ReasonNotAboutPlayer, notice.SkipReason);
        }

        [Fact]
        public void PlayerOnFromOrAboutSide_BothConsideredAboutPlayer()
        {
            // Case A: player is targetB and fromHeroId
            var noticeA = PlayerRelationNoticeRule.Evaluate(
                source: GrudgeSource.Situation,
                ledgerOnly: false,
                targetAId: "caladog",
                targetBId: "player",
                fromHeroId: "player",
                aboutHeroId: "caladog",
                playerHeroId: "player",
                delta: 3);

            Assert.True(noticeA.ShouldNotify);
            Assert.Equal("caladog", noticeA.OtherHeroId);
            Assert.Equal(PlayerRelationNoticeKind.Person, noticeA.Kind);

            // Case B: player is targetA and aboutHeroId
            var noticeB = PlayerRelationNoticeRule.Evaluate(
                source: GrudgeSource.Situation,
                ledgerOnly: false,
                targetAId: "player",
                targetBId: "caladog",
                fromHeroId: "caladog",
                aboutHeroId: "player",
                playerHeroId: "player",
                delta: 3);

            Assert.True(noticeB.ShouldNotify);
            Assert.Equal("caladog", noticeB.OtherHeroId);
            Assert.Equal(PlayerRelationNoticeKind.Person, noticeB.Kind);
        }

        [Fact]
        public void LogFormatter_NoticeSkipped_MatchesExpected()
        {
            string line = GrudgeLogFormatter.FormatPlayerRelationNoticeSkipped("caladog", "no change (at the relation limit)");
            Assert.Equal("relation notice skipped for caladog: no change (at the relation limit)", line);
        }

        [Fact]
        public void LogFormatter_NoticeShown_Person_MatchesExpected()
        {
            string line = GrudgeLogFormatter.FormatPlayerRelationNoticeShown("person", "caladog", "caladog", -5, 10);
            Assert.Equal("relation notice shown (person): other caladog, original from caladog, delta -5, value 10", line);
        }

        [Fact]
        public void LogFormatter_NoticeShown_Clan_MatchesExpected()
        {
            string line = GrudgeLogFormatter.FormatPlayerRelationNoticeShown("clan", "derthert", "caladog", -5, 15);
            Assert.Equal("relation notice shown (clan): other derthert, original from caladog, delta -5, value 15", line);
        }

        [Fact]
        public void LogFormatter_NoticeShown_Increased_MatchesExpected()
        {
            string line = GrudgeLogFormatter.FormatPlayerRelationNoticeShown("person", "caladog", "caladog", 3, 25);
            Assert.Equal("relation notice shown (person): other caladog, original from caladog, delta +3, value 25", line);
        }

        [Fact]
        public void LogFormatter_ClanFallback_MatchesExpected()
        {
            string line = GrudgeLogFormatter.FormatPlayerRelationNoticeClanFallback("derthert", "hero has no clan");
            Assert.Equal("relation notice clan line fallback for derthert: hero has no clan", line);
        }
    }
}
