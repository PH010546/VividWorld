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
                new ChannelLink("h1", ChannelKind.SameSettlement, 1.0, relation: 40, ownRelation: 40),
                new ChannelLink("h2", ChannelKind.SameSettlement, 1.0, relation: 30, ownRelation: 30),
                new ChannelLink("h3", ChannelKind.SameSettlement, 1.0, relation: 20, ownRelation: 20),
                new ChannelLink("h4", ChannelKind.SameSettlement, 1.0, relation: 10, ownRelation: 10),
                new ChannelLink("h5", ChannelKind.SameSettlement, 1.0, relation: 0, ownRelation: 0),
                new ChannelLink("r1", ChannelKind.SameClan, 1.0, relation: 50, ownRelation: 50),
                new ChannelLink("r2", ChannelKind.SameClan, 1.0, relation: 40, ownRelation: 40),
                new ChannelLink("r3", ChannelKind.SameClan, 1.0, relation: 30, ownRelation: 30),
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
                new ChannelLink("h1", ChannelKind.SameSettlement, 1.0, relation: 10, ownRelation: 10),
                new ChannelLink("r1", ChannelKind.SameClan, 1.0, relation: 50, ownRelation: 50),
                new ChannelLink("r2", ChannelKind.SameClan, 1.0, relation: 40, ownRelation: 40),
                new ChannelLink("r3", ChannelKind.SameClan, 1.0, relation: 30, ownRelation: 30),
                new ChannelLink("r4", ChannelKind.SameClan, 1.0, relation: 20, ownRelation: 20),
                new ChannelLink("r5", ChannelKind.SameClan, 1.0, relation: 10, ownRelation: 10),
            };

            var resultA = ChannelQuota.Allocate(candidatesA, propCfg, relCfg);
            Assert.Equal(6, resultA.Selected.Count);
            Assert.Single(resultA.Selected.Where(c => ChannelClass.IsInPerson(c.Kind)));
            Assert.Equal(5, resultA.Selected.Count(c => !ChannelClass.IsInPerson(c.Kind)));

            var candidatesB = new List<ChannelLink>
            {
                new ChannelLink("h1", ChannelKind.SameSettlement, 1.0, relation: 50, ownRelation: 50),
                new ChannelLink("h2", ChannelKind.SameSettlement, 1.0, relation: 40, ownRelation: 40),
                new ChannelLink("h3", ChannelKind.SameSettlement, 1.0, relation: 30, ownRelation: 30),
                new ChannelLink("h4", ChannelKind.SameSettlement, 1.0, relation: 20, ownRelation: 20),
                new ChannelLink("h5", ChannelKind.SameSettlement, 1.0, relation: 10, ownRelation: 10),
                new ChannelLink("h6", ChannelKind.SameSettlement, 1.0, relation: 0, ownRelation: 0),
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
                new ChannelLink("h1", ChannelKind.SameSettlement, 1.0, relation: 0, ownRelation: 0),
                new ChannelLink("h1", ChannelKind.SameClan, 1.0, relation: 0, ownRelation: 0),
                new ChannelLink("h2", ChannelKind.SameSettlement, 1.0, relation: 10, ownRelation: 10),
                new ChannelLink("r1", ChannelKind.SameClan, 1.0, relation: 20, ownRelation: 20),
            };

            var result = ChannelQuota.Allocate(candidates, propCfg, relCfg);

            Assert.Single(result.Selected.Where(c => c.HeroId == "h1"));
            var h1Selected = result.Selected.First(c => c.HeroId == "h1");
            Assert.Equal(ChannelKind.SameSettlement, h1Selected.Kind);
            Assert.DoesNotContain(result.SqueezedOut, c => c.HeroId == "h1");
        }

        [Fact]
        public void Quota_AllocatesAndDeduplicatesByOwnRelation_NotRelation()
        {
            var propCfg = new PropagationConfig();
            var relCfg = new RelationConfig();

            // 1. 兩位聽的人 Relation 排序與 OwnRelation 排序相反時，名額照 OwnRelation 給
            var candidates = new List<ChannelLink>
            {
                new ChannelLink("h_high_rel", ChannelKind.SameSettlement, 1.0, relation: 80, ownRelation: -20),
                new ChannelLink("h_high_own", ChannelKind.SameSettlement, 1.0, relation: -20, ownRelation: 80),
            };

            var result = ChannelQuota.Allocate(candidates, propCfg.ChannelWeights, relCfg, maxTotal: 1, maxInPerson: 1, maxRemote: 0);

            Assert.Single(result.Selected);
            Assert.Equal("h_high_own", result.Selected[0].HeroId);
            Assert.Single(result.SqueezedOut);
            Assert.Equal("h_high_rel", result.SqueezedOut[0].HeroId);

            // 2. 同一人從兩個通道來時，照 OwnRelation 算出的分數留下較高的那一條
            // SameParty (cw=1.25): Relation=+50, OwnRelation=-50 => rf = 0.35, score = 1.25 * 0.35 = 0.4375
            // SameSettlement (cw=0.80): Relation=-50, OwnRelation=+50 => rf = 1.175, score = 0.80 * 1.175 = 0.94
            // 若照 Relation 算分則 SameParty 勝出；改照 OwnRelation 算分則 SameSettlement 勝出
            var dedupeCandidates = new List<ChannelLink>
            {
                new ChannelLink("h_multi", ChannelKind.SameParty, 1.0, relation: 50, ownRelation: -50),
                new ChannelLink("h_multi", ChannelKind.SameSettlement, 1.0, relation: -50, ownRelation: 50),
            };

            var dedupeResult = ChannelQuota.Allocate(dedupeCandidates, propCfg, relCfg);

            Assert.Single(dedupeResult.Selected);
            var selectedMulti = dedupeResult.Selected[0];
            Assert.Equal("h_multi", selectedMulti.HeroId);
            Assert.Equal(ChannelKind.SameSettlement, selectedMulti.Kind);
            Assert.Empty(dedupeResult.SqueezedOut);
        }
    }
}
