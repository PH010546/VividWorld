using VividWorld.Core.Channels;
using VividWorld.Core.Config;
using VividWorld.Core.Rumors;
using Xunit;

namespace VividWorld.Core.Tests
{
    public class RelationFactorTests
    {
        [Fact]
        public void RelationFactor_InPerson_NeutralRelation_IsExactlyOne()
        {
            var cfg = new RelationConfig();

            double fSettlement = RelationFactor.For(ChannelKind.SameSettlement, 0, cfg);
            double fParty = RelationFactor.For(ChannelKind.SameParty, 0, cfg);
            double fArmy = RelationFactor.For(ChannelKind.SameArmy, 0, cfg);

            Assert.Equal(1.0, fSettlement);
            Assert.Equal(1.0, fParty);
            Assert.Equal(1.0, fArmy);
        }

        [Fact]
        public void RelationFactor_InPerson_IsAsymmetric_SmallOnPositiveLargeOnNegative()
        {
            var cfg = new RelationConfig();

            double fPositive = RelationFactor.For(ChannelKind.SameSettlement, 60, cfg);
            double increase = fPositive - 1.0;

            double fNegative = RelationFactor.For(ChannelKind.SameSettlement, -60, cfg);
            double decrease = 1.0 - fNegative;

            Assert.Equal(1.21, fPositive, precision: 4);
            Assert.Equal(0.22, fNegative, precision: 4);
            Assert.True(increase < decrease);
            Assert.True(decrease > increase * 3.0);
        }

        [Fact]
        public void RelationFactor_Remote_NeutralRelation_IsNearZero()
        {
            var cfg = new RelationConfig();

            double fKingdom = RelationFactor.For(ChannelKind.Kingdom, 0, cfg);
            double fClan = RelationFactor.For(ChannelKind.SameClan, 0, cfg);
            double fKinAbroad = RelationFactor.For(ChannelKind.KinAbroad, 0, cfg);

            Assert.Equal(0.10, fKingdom);
            Assert.Equal(0.10, fClan);
            Assert.Equal(0.10, fKinAbroad);
        }

        [Fact]
        public void RelationFactor_Remote_NegativeRelation_IsZero()
        {
            var cfg = new RelationConfig();

            double fKingdom = RelationFactor.For(ChannelKind.Kingdom, -20, cfg);
            double fClan = RelationFactor.For(ChannelKind.SameClan, -60, cfg);
            double fKinAbroad = RelationFactor.For(ChannelKind.KinAbroad, -5, cfg);

            Assert.Equal(0.0, fKingdom);
            Assert.Equal(0.0, fClan);
            Assert.Equal(0.0, fKinAbroad);
        }

        [Fact]
        public void RelationFactor_HostileSameParty_ScoresBelowFriendlyKinAbroad()
        {
            var propCfg = new PropagationConfig();
            var relCfg = new RelationConfig();

            double partyWeight = propCfg.ChannelWeights.SameParty;
            double partyFactor = RelationFactor.For(ChannelKind.SameParty, -60, relCfg);
            double partyScore = partyWeight * partyFactor;

            double kinAbroadWeight = propCfg.ChannelWeights.KinAbroad;
            double kinAbroadFactor = RelationFactor.For(ChannelKind.KinAbroad, 60, relCfg);
            double kinAbroadScore = kinAbroadWeight * kinAbroadFactor;

            Assert.Equal(0.275, partyScore, precision: 4);
            Assert.Equal(1.04, kinAbroadScore, precision: 4);
            Assert.True(partyScore < kinAbroadScore);
        }

        [Fact]
        public void RelationFactor_ClampsAtConfiguredBounds()
        {
            var cfg = new RelationConfig();
            cfg.MeetFactorMax = 1.20;
            cfg.RemoteFactorMax = 1.50;

            double inPersonExtremeNegative = RelationFactor.For(ChannelKind.SameParty, -500, cfg);
            double inPersonExtremePositive = RelationFactor.For(ChannelKind.SameParty, 500, cfg);

            Assert.Equal(cfg.MeetFactorMin, inPersonExtremeNegative);
            Assert.Equal(cfg.MeetFactorMax, inPersonExtremePositive);

            double remoteExtremeNegative = RelationFactor.For(ChannelKind.Kingdom, -500, cfg);
            double remoteExtremePositive = RelationFactor.For(ChannelKind.Kingdom, 500, cfg);

            Assert.Equal(0.0, remoteExtremeNegative);
            Assert.Equal(cfg.RemoteFactorMax, remoteExtremePositive);
        }
    }
}
