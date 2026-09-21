using System.Collections.Generic;
using System.Linq;
using VividWorld.Core.Channels;
using VividWorld.Core.Config;
using Xunit;

namespace VividWorld.Core.Tests
{
    public class ChannelQuotaTests
    {
        [Fact]
        public void Quota_SplitsBetweenInPersonAndRemote()
        {
            var propCfg = new PropagationConfig();
            var relCfg = new RelationConfig();

            var candidates = new List<ChannelLink>
            {
                new ChannelLink("h1", ChannelKind.SameSettlement, 1.0, relation: 40),
                new ChannelLink("h2", ChannelKind.SameSettlement, 1.0, relation: 30),
                new ChannelLink("h3", ChannelKind.SameSettlement, 1.0, relation: 20),
                new ChannelLink("h4", ChannelKind.SameSettlement, 1.0, relation: 10),
                new ChannelLink("h5", ChannelKind.SameSettlement, 1.0, relation: 0),
                new ChannelLink("r1", ChannelKind.SameClan, 1.0, relation: 50),
                new ChannelLink("r2", ChannelKind.SameClan, 1.0, relation: 40),
                new ChannelLink("r3", ChannelKind.SameClan, 1.0, relation: 30),
            };

            var result = ChannelQuota.Allocate(candidates, propCfg, relCfg);

            Assert.Equal(6, result.Selected.Count);
            int inPersonCount = result.Selected.Count(c => ChannelClass.IsInPerson(c.Kind));
            int remoteCount = result.Selected.Count(c => !ChannelClass.IsInPerson(c.Kind));

            Assert.Equal(4, inPersonCount);
            Assert.Equal(2, remoteCount);
            Assert.Equal(2, result.SqueezedOut.Count);
        }

        [Fact]
        public void Quota_UnderfilledSideYieldsItsSlots()
        {
            var propCfg = new PropagationConfig();
            var relCfg = new RelationConfig();

            var candidatesA = new List<ChannelLink>
            {
                new ChannelLink("h1", ChannelKind.SameSettlement, 1.0, relation: 10),
                new ChannelLink("r1", ChannelKind.SameClan, 1.0, relation: 50),
                new ChannelLink("r2", ChannelKind.SameClan, 1.0, relation: 40),
                new ChannelLink("r3", ChannelKind.SameClan, 1.0, relation: 30),
                new ChannelLink("r4", ChannelKind.SameClan, 1.0, relation: 20),
                new ChannelLink("r5", ChannelKind.SameClan, 1.0, relation: 10),
            };

            var resultA = ChannelQuota.Allocate(candidatesA, propCfg, relCfg);
            Assert.Equal(6, resultA.Selected.Count);
            Assert.Single(resultA.Selected.Where(c => ChannelClass.IsInPerson(c.Kind)));
            Assert.Equal(5, resultA.Selected.Count(c => !ChannelClass.IsInPerson(c.Kind)));

            var candidatesB = new List<ChannelLink>
            {
                new ChannelLink("h1", ChannelKind.SameSettlement, 1.0, relation: 50),
                new ChannelLink("h2", ChannelKind.SameSettlement, 1.0, relation: 40),
                new ChannelLink("h3", ChannelKind.SameSettlement, 1.0, relation: 30),
                new ChannelLink("h4", ChannelKind.SameSettlement, 1.0, relation: 20),
                new ChannelLink("h5", ChannelKind.SameSettlement, 1.0, relation: 10),
                new ChannelLink("h6", ChannelKind.SameSettlement, 1.0, relation: 0),
            };

            var resultB = ChannelQuota.Allocate(candidatesB, propCfg, relCfg);
            Assert.Equal(6, resultB.Selected.Count);
            Assert.Equal(6, resultB.Selected.Count(c => ChannelClass.IsInPerson(c.Kind)));
            Assert.Equal(0, resultB.Selected.Count(c => !ChannelClass.IsInPerson(c.Kind)));
        }

        [Fact]
        public void Quota_DeduplicatesBeforeSplitting()
        {
            var propCfg = new PropagationConfig();
            var relCfg = new RelationConfig();

            var candidates = new List<ChannelLink>
            {
                new ChannelLink("h1", ChannelKind.SameSettlement, 1.0, relation: 0),
                new ChannelLink("h1", ChannelKind.SameClan, 1.0, relation: 0),
                new ChannelLink("h2", ChannelKind.SameSettlement, 1.0, relation: 10),
                new ChannelLink("r1", ChannelKind.SameClan, 1.0, relation: 20),
            };

            var result = ChannelQuota.Allocate(candidates, propCfg, relCfg);

            Assert.Single(result.Selected.Where(c => c.HeroId == "h1"));
            var h1Selected = result.Selected.First(c => c.HeroId == "h1");
            Assert.Equal(ChannelKind.SameSettlement, h1Selected.Kind);
            Assert.DoesNotContain(result.SqueezedOut, c => c.HeroId == "h1");
        }
    }
}
