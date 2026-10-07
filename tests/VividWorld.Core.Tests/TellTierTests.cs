using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using VividWorld.Core.Channels;
using VividWorld.Core.Config;
using VividWorld.Core.Events;
using VividWorld.Core.Memory;
using VividWorld.Core.Persistence;
using VividWorld.Core.Rumors;
using VividWorld.Core.Tests.Fakes;
using VividWorld.Core.Util;
using Xunit;

namespace VividWorld.Core.Tests
{
    public class TellTierTests
    {
        private static ContactObservation Obs(string id, ChannelKind kind, int relation, double chance, ContactStatus status,
            int own = 0, bool family = false, double will = 0.0, TellTier tier = TellTier.Familiar, double? rf = null)
            => new ContactObservation(id, kind, relation, chance, status, own, family, will, tier,
                relationFactor: rf ?? RelationFactor.For(kind, own, new RelationConfig()));

        private static readonly TellTiersConfig DefaultTiers = new TellTiersConfig();

        // ── Classify ──

        [Fact]
        public void Classify_FamilyIgnoresEveryNumber()
        {
            Assert.Equal(TellTier.Familiar, TellTierRule.Classify(true, -80, -99.0, DefaultTiers));
        }

        [Theory]
        [InlineData(-2, 99.0, TellTier.Hostile)]
        [InlineData(-1, 99.0, TellTier.Hostile)]
        [InlineData(0, 9.9, TellTier.Unfamiliar)]
        [InlineData(0, 10.0, TellTier.Familiar)]
        [InlineData(5, -50.0, TellTier.Unfamiliar)]
        [InlineData(5, 10.1, TellTier.Familiar)]
        public void Classify_AtTheBoundaries(int own, double willingness, TellTier expected)
        {
            Assert.Equal(expected, TellTierRule.Classify(false, own, willingness, DefaultTiers));
        }

        [Fact]
        public void Classify_UsesTheConfiguredLines()
        {
            var cfg = new TellTiersConfig { HostileAtOrBelow = -20, FamiliarWillingness = 30.0 };
            Assert.Equal(TellTier.Unfamiliar, TellTierRule.Classify(false, -19, 29.9, cfg));
            Assert.Equal(TellTier.Hostile, TellTierRule.Classify(false, -20, 99.0, cfg));
            Assert.Equal(TellTier.Familiar, TellTierRule.Classify(false, -19, 30.0, cfg));
        }

        [Theory]
        [InlineData(TellTier.Hostile, 4, false)]
        [InlineData(TellTier.Hostile, 5, false)]
        [InlineData(TellTier.Unfamiliar, 4, false)]
        [InlineData(TellTier.Unfamiliar, 5, true)]
        [InlineData(TellTier.Familiar, 4, true)]
        [InlineData(TellTier.Familiar, 5, true)]
        public void WouldStillTell_ThreeTiersAroundTheBigNewsLine(TellTier tier, int weightTen, bool expected)
        {
            Assert.Equal(expected, TellTierRule.WouldStillTell(tier, weightTen, 5));
        }

        // ── Willingness ──

        [Fact]
        public void Willingness_AddsTheThreeTraitTerms()
        {
            var w = new AskTraitWeights();   // 6 / 4 / -8
            var teller = new TraitProfile { Generosity = 2, Honor = 1, Calculating = -1 };
            Assert.Equal(10 + 12.0 + 4.0 + 8.0, TellTierRule.Willingness(10, teller, w));
        }

        [Fact]
        public void Willingness_WithoutTraitProfile_IsTheRelation()
        {
            Assert.Equal(-7.0, TellTierRule.Willingness(-7, null, new AskTraitWeights()));
        }

        // ── 設定 ──

        [Fact]
        public void Config_Defaults()
        {
            var cfg = new VividWorldConfig();
            Assert.True(cfg.Propagation.TellTiers.Enabled);
            Assert.Equal(-1, cfg.Propagation.TellTiers.HostileAtOrBelow);
            Assert.Equal(10.0, cfg.Propagation.TellTiers.FamiliarWillingness);
        }

        [Fact]
        public void Config_OldConfigWithoutTheThreeKeys_GetsThemAdded()
        {
            var existing = JObject.Parse(@"{ ""propagation"": { ""baseTellChancePerContact"": 0.5 } }");
            var canonical = JObject.Parse(VividJson.Write(new VividWorldConfig()));

            var result = ConfigMerge.AddMissingKeys(existing, canonical);

            Assert.Contains("propagation.tellTiers.enabled", result.AddedPaths);
            Assert.Contains("propagation.tellTiers.hostileAtOrBelow", result.AddedPaths);
            Assert.Contains("propagation.tellTiers.familiarWillingness", result.AddedPaths);
            Assert.Equal(0.5, (double)result.Merged["propagation"]!["baseTellChancePerContact"]!);
            Assert.True((bool)result.Merged["propagation"]!["tellTiers"]!["enabled"]!);
        }

        [Fact]
        public void Config_OutOfRangeValuesAreClampedBack()
        {
            var cfg = new VividWorldConfig();
            cfg.Propagation.TellTiers.HostileAtOrBelow = 7;
            cfg.Propagation.TellTiers.FamiliarWillingness = 999.0;
            var notices = new List<ClampNotice>();

            cfg.Normalize(notices);

            Assert.Equal(0, cfg.Propagation.TellTiers.HostileAtOrBelow);
            Assert.Equal(200.0, cfg.Propagation.TellTiers.FamiliarWillingness);
            Assert.Contains(notices, n => n.Key == "propagation.tellTiers.hostileAtOrBelow");
            Assert.Contains(notices, n => n.Key == "propagation.tellTiers.familiarWillingness");

            cfg.Propagation.TellTiers.HostileAtOrBelow = -500;
            cfg.Propagation.TellTiers.FamiliarWillingness = -500.0;
            cfg.Normalize(new List<ClampNotice>());
            Assert.Equal(-100, cfg.Propagation.TellTiers.HostileAtOrBelow);
            Assert.Equal(-200.0, cfg.Propagation.TellTiers.FamiliarWillingness);
        }

        // ── 連結 ──

        [Fact]
        public void ChannelLink_NewValuesDefaultToZeroAndFalse_AndSurviveTheQuota()
        {
            var plain = new ChannelLink("a", ChannelKind.SameParty);
            Assert.Equal(0, plain.OwnRelation);
            Assert.False(plain.IsFamily);

            var link = new ChannelLink("b", ChannelKind.SameParty, 1.0, 5, -3, true);
            var result = ChannelQuota.Allocate(new[] { link }, new PropagationConfig(), new RelationConfig());
            var kept = Assert.Single(result.Selected);
            Assert.Equal(-3, kept.OwnRelation);
            Assert.True(kept.IsFamily);
        }

        // ── 統計 ──

        [Fact]
        public void Tally_CountsEveryStatusAndSplitsInPersonFromRemote()
        {
            var t = new TellTierTally();
            t.Record(Obs("a", ChannelKind.SameParty, 0, 0.5, ContactStatus.Told), 7, 5);
            t.Record(Obs("b", ChannelKind.SameClan, 0, 0.5, ContactStatus.Missed), 7, 5);
            t.Record(Obs("c", ChannelKind.Kingdom, 0, 0.0, ContactStatus.AlreadyKnows), 7, 5);
            t.Record(Obs("d", ChannelKind.SameArmy, 0, 0.0, ContactStatus.NotEligible), 7, 5);
            t.Record(Obs("e", ChannelKind.KinAbroad, 0, 0.0, ContactStatus.NotReached), 7, 5);
            t.Record(Obs("f", ChannelKind.SameSettlement, 0, 0.5, ContactStatus.Reheard), 7, 5);
            t.Record(Obs("g", ChannelKind.SameSettlement, 0, 0.0, ContactStatus.HeldBackHostile), 7, 5);
            t.Record(Obs("h", ChannelKind.Kingdom, 0, 0.0, ContactStatus.HeldBackSmallNews), 7, 5);
            t.Record(Obs("i", ChannelKind.SameParty, 0, 0.0, ContactStatus.HeldBackShameful), 7, 5);

            Assert.Equal(9, t.Total);
            Assert.Equal(5, t.InPerson);
            Assert.Equal(4, t.Remote);
            foreach (var s in Enum.GetValues(typeof(ContactStatus)).Cast<ContactStatus>())
            {
                Assert.Equal(1, t.StatusCount(s));
            }
            Assert.Equal(1, t.ToldTotal);
        }

        [Fact]
        public void Tally_ReheardIsNotCountedAsTold()
        {
            var t = new TellTierTally();
            t.Record(Obs("a", ChannelKind.SameSettlement, 50, 0.5, ContactStatus.Reheard), 9, 5);
            Assert.Equal(1, t.Total);
            Assert.Equal(0, t.ToldTotal);
        }

        [Fact]
        public void Tally_ToldSplitsIntoBigAndSmall()
        {
            var t = new TellTierTally();
            t.Record(Obs("a", ChannelKind.SameSettlement, 12, 0.5, ContactStatus.Told), 5, 5);
            t.Record(Obs("b", ChannelKind.Kingdom, 5, 0.5, ContactStatus.Told), 4, 5);
            t.Record(Obs("c", ChannelKind.Kingdom, 5, 0.5, ContactStatus.Missed), 9, 5);
            Assert.Equal(1, t.ToldBig);
            Assert.Equal(1, t.ToldSmall);
        }

        [Fact]
        public void Tally_RelationBins_AtTheBoundaries_InPersonAndRemoteSeparately()
        {
            var t = new TellTierTally();
            int[] rels = { -21, -20, -19, -1, 0, 1, 9, 10, 29, 30, 31 };
            foreach (int r in rels)
            {
                t.Record(Obs("a", ChannelKind.SameSettlement, r, 0, ContactStatus.Missed), 5, 5);
            }
            t.Record(Obs("b", ChannelKind.Kingdom, 0, 0, ContactStatus.Missed), 5, 5);

            Assert.Equal(new[] { 2, 2, 1, 2, 2, 2 }, t.RelationInPerson.ToArray());
            Assert.Equal(new[] { 0, 0, 1, 0, 0, 0 }, t.RelationRemote.ToArray());
        }

        [Fact]
        public void Tally_OwnRelationBins_FollowTheOwnRelation_NotTheChannelRelation()
        {
            var t = new TellTierTally();
            int[] own = { -21, -20, -19, -1, 0, 1, 9, 10, 29, 30, 31 };
            foreach (int r in own)
            {
                t.Record(Obs("a", ChannelKind.SameSettlement, 0, 0, ContactStatus.Missed, own: r), 5, 5);
            }
            t.Record(Obs("b", ChannelKind.Kingdom, 50, 0, ContactStatus.Missed, own: 0), 5, 5);

            Assert.Equal(new[] { 2, 2, 1, 2, 2, 2 }, t.OwnRelationInPerson.ToArray());
            Assert.Equal(new[] { 0, 0, 1, 0, 0, 0 }, t.OwnRelationRemote.ToArray());
        }

        [Fact]
        public void Tally_WillingnessBins_AtTheBoundaries()
        {
            var t = new TellTierTally();
            double[] values = { -0.1, 0.0, 4.99, 5.0, 9.99, 10.0, 29.99, 30.0 };
            foreach (double v in values)
            {
                t.Record(Obs("a", ChannelKind.Kingdom, 0, 0, ContactStatus.Missed, will: v), 5, 5);
            }
            Assert.Equal(new[] { 1, 2, 2, 2, 1 }, t.WillingnessBins.ToArray());
        }

        [Fact]
        public void Tally_TierClasses_FamilyIsNotCountedAsFamiliar_InPersonAndRemoteSeparately()
        {
            var t = new TellTierTally();
            t.Record(Obs("a", ChannelKind.SameParty, 0, 0, ContactStatus.Missed, family: true, tier: TellTier.Familiar), 5, 5);
            t.Record(Obs("b", ChannelKind.SameParty, 0, 0, ContactStatus.Missed, tier: TellTier.Familiar), 5, 5);
            t.Record(Obs("c", ChannelKind.SameArmy, 0, 0, ContactStatus.Missed, tier: TellTier.Unfamiliar), 5, 5);
            t.Record(Obs("d", ChannelKind.SameArmy, 0, 0, ContactStatus.Missed, tier: TellTier.Unfamiliar), 5, 5);
            t.Record(Obs("e", ChannelKind.Kingdom, 0, 0, ContactStatus.Missed, tier: TellTier.Hostile), 5, 5);
            t.Record(Obs("f", ChannelKind.KinAbroad, 0, 0, ContactStatus.Missed, family: true, tier: TellTier.Familiar), 5, 5);

            // 順序：自家人、熟人、不熟、交惡
            Assert.Equal(new[] { 1, 1, 2, 0 }, t.TierInPerson.ToArray());
            Assert.Equal(new[] { 1, 0, 0, 1 }, t.TierRemote.ToArray());
        }

        [Fact]
        public void Tally_HeldBackByReasonAndChannel()
        {
            var t = new TellTierTally();
            t.Record(Obs("a", ChannelKind.SameSettlement, 0, 0, ContactStatus.HeldBackHostile), 5, 5);
            t.Record(Obs("b", ChannelKind.SameSettlement, 0, 0, ContactStatus.HeldBackHostile), 5, 5);
            t.Record(Obs("c", ChannelKind.Kingdom, 0, 0, ContactStatus.HeldBackHostile), 5, 5);
            t.Record(Obs("d", ChannelKind.SameParty, 0, 0, ContactStatus.HeldBackSmallNews), 5, 5);
            t.Record(Obs("e", ChannelKind.Kingdom, 0, 0, ContactStatus.HeldBackSmallNews), 5, 5);
            t.Record(Obs("f", ChannelKind.KinAbroad, 0, 0, ContactStatus.HeldBackSmallNews), 5, 5);

            Assert.Equal(2, t.HeldBackHostileInPerson);
            Assert.Equal(1, t.HeldBackHostileRemote);
            Assert.Equal(1, t.HeldBackSmallNewsInPerson);
            Assert.Equal(2, t.HeldBackSmallNewsRemote);
        }

        [Fact]
        public void Tally_SignDiffers_CountsOnlyOnePositiveOneNegative()
        {
            var t = new TellTierTally();
            t.Record(Obs("a", ChannelKind.Kingdom, 10, 0, ContactStatus.Missed, own: -5), 5, 5);   // 一正一負
            t.Record(Obs("b", ChannelKind.Kingdom, -10, 0, ContactStatus.Missed, own: 5), 5, 5);   // 一負一正
            t.Record(Obs("c", ChannelKind.Kingdom, 10, 0, ContactStatus.Missed, own: 0), 5, 5);    // 0 不算
            t.Record(Obs("d", ChannelKind.Kingdom, 0, 0, ContactStatus.Missed, own: -5), 5, 5);    // 0 不算
            t.Record(Obs("e", ChannelKind.Kingdom, 10, 0, ContactStatus.Missed, own: 5), 5, 5);    // 同號
            Assert.Equal(2, t.SignDiffers);
        }

        [Fact]
        public void Tally_MergeAddsEverything_AndResetClearsEverything()
        {
            var a = new TellTierTally();
            a.Record(Obs("a", ChannelKind.SameSettlement, 12, 0.5, ContactStatus.Told, own: 3, family: true, will: 18.0), 7, 5);
            var b = new TellTierTally();
            b.Record(Obs("b", ChannelKind.Kingdom, 5, 0.5, ContactStatus.Told, own: -2, will: 5.0, tier: TellTier.Unfamiliar), 3, 5);
            b.Record(Obs("c", ChannelKind.Kingdom, 5, 0.0, ContactStatus.HeldBackHostile, own: -7, tier: TellTier.Hostile), 3, 5);
            b.Record(Obs("d", ChannelKind.SameParty, 5, 0.0, ContactStatus.HeldBackSmallNews, own: 1, tier: TellTier.Unfamiliar), 3, 5);

            a.Merge(b);

            Assert.Equal(4, a.Total);
            Assert.Equal(2, a.InPerson);
            Assert.Equal(2, a.Remote);
            Assert.Equal(2, a.ToldTotal);
            Assert.Equal(1, a.ToldBig);
            Assert.Equal(1, a.ToldSmall);
            Assert.Equal(1, a.StatusCount(ContactStatus.HeldBackHostile));
            Assert.Equal(1, a.StatusCount(ContactStatus.HeldBackSmallNews));
            Assert.Equal(1, a.HeldBackHostileRemote);
            Assert.Equal(1, a.HeldBackSmallNewsInPerson);
            Assert.Equal(2, a.SignDiffers);
            Assert.Equal(new[] { 1, 0, 1, 0 }, a.TierInPerson.ToArray());
            Assert.Equal(new[] { 0, 0, 1, 1 }, a.TierRemote.ToArray());
            Assert.Equal(2, a.OwnRelationRemote[1]);
            Assert.Equal(3, b.Total);   // 被併進去的那份不動

            a.Reset();

            Assert.Equal(0, a.Total);
            Assert.Equal(0, a.InPerson);
            Assert.Equal(0, a.Remote);
            Assert.Equal(0, a.ToldTotal);
            Assert.Equal(0, a.SignDiffers);
            Assert.All(Enum.GetValues(typeof(ContactStatus)).Cast<ContactStatus>(), s => Assert.Equal(0, a.StatusCount(s)));
            Assert.All(a.RelationInPerson, n => Assert.Equal(0, n));
            Assert.All(a.RelationRemote, n => Assert.Equal(0, n));
            Assert.All(a.OwnRelationInPerson, n => Assert.Equal(0, n));
            Assert.All(a.OwnRelationRemote, n => Assert.Equal(0, n));
            Assert.All(a.WillingnessBins, n => Assert.Equal(0, n));
            Assert.All(a.TierInPerson, n => Assert.Equal(0, n));
            Assert.All(a.TierRemote, n => Assert.Equal(0, n));
            Assert.Equal(0, a.HeldBackHostileInPerson + a.HeldBackHostileRemote + a.HeldBackSmallNewsInPerson + a.HeldBackSmallNewsRemote);
        }

        // ── 格式 ──

        private static TellTierTally SampleTally()
        {
            var t = new TellTierTally();
            t.Record(Obs("lord_2_3", ChannelKind.SameSettlement, 12, 0.053, ContactStatus.Told, own: 2, will: 18.0, tier: TellTier.Familiar), 7, 5);
            t.Record(Obs("lord_4_1", ChannelKind.Kingdom, 0, 0.004, ContactStatus.Missed, own: 0, will: -8.0, tier: TellTier.Unfamiliar), 7, 5);
            t.Record(Obs("lord_5_2", ChannelKind.SameArmy, -30, 0.0, ContactStatus.AlreadyKnows, own: -30, will: -30.0, tier: TellTier.Hostile), 7, 5);
            t.Record(Obs("lord_6_1", ChannelKind.Kingdom, 5, 0.0, ContactStatus.HeldBackSmallNews, own: -2, will: 5.0, tier: TellTier.Unfamiliar), 3, 5);
            t.Record(Obs("lord_7_1", ChannelKind.Kingdom, 40, 0.0, ContactStatus.HeldBackHostile, own: -9, will: 40.0, tier: TellTier.Hostile), 7, 5);
            t.Record(Obs("lord_8_1", ChannelKind.SameParty, -4, 0.2, ContactStatus.Told, own: -4, family: true, will: -4.0, tier: TellTier.Familiar), 7, 5);
            return t;
        }

        [Fact]
        public void FormatTurnLine_ListsEveryContactWithTierAndStatus()
        {
            var contacts = new[]
            {
                Obs("lord_2_3", ChannelKind.SameArmy, 15, 0.104, ContactStatus.Missed, own: 2, will: 7.0, tier: TellTier.Unfamiliar),
                Obs("lord_4_1", ChannelKind.Kingdom, 0, 0.004, ContactStatus.Told, own: 0, will: 12.5, tier: TellTier.Familiar),
                Obs("lord_5_2", ChannelKind.SameClan, -30, 0.2, ContactStatus.Told, own: -30, family: true, will: -30.0, tier: TellTier.Familiar),
                Obs("lord_6_1", ChannelKind.SameParty, 8, 0.0, ContactStatus.HeldBackHostile, own: -3, will: 4.0, tier: TellTier.Hostile),
                Obs("lord_7_1", ChannelKind.Kingdom, 1, 0.0, ContactStatus.HeldBackSmallNews, own: 1, will: 1.0, tier: TellTier.Unfamiliar)
            };
            string line = TellTierLogFormatter.FormatTurnLine("lord_1_1", "evt_x", 7, true, contacts);

            Assert.Equal(
                "Tell tiers: lord_1_1 told evt_x (weight 7, big) -> " +
                "lord_2_3 SameArmy rel +15 own +2 will 7.0 rf 1.01 unfamiliar p 10.4% missed; " +
                "lord_4_1 Kingdom rel 0 own 0 will 12.5 rf 0.10 familiar p 0.4% told; " +
                "lord_5_2 SameClan rel -30 own -30 will -30.0 rf 0.00 family p 20.0% told; " +
                "lord_6_1 SameParty rel +8 own -3 will 4.0 rf 0.96 hostile p 0.0% held back (hostile); " +
                "lord_7_1 Kingdom rel +1 own +1 will 1.0 rf 0.12 unfamiliar p 0.0% held back (small news)",
                line);
        }

        [Fact]
        public void FormatTurnLine_NoContacts()
        {
            string line = TellTierLogFormatter.FormatTurnLine("lord_1_1", "evt_x", 3, false, Array.Empty<ContactObservation>());
            Assert.Equal("Tell tiers: lord_1_1 told evt_x (weight 3, small) -> (no contacts)", line);
        }

        [Fact]
        public void FormatTurnLine_NotEligibleAndNotReachedTexts()
        {
            var contacts = new[]
            {
                Obs("a", ChannelKind.SameParty, 3, 0.0, ContactStatus.NotEligible, own: 3, will: 3.0, tier: TellTier.Unfamiliar),
                Obs("b", ChannelKind.KinAbroad, -2, 0.0, ContactStatus.NotReached, own: -2, will: -2.0, tier: TellTier.Hostile)
            };
            string line = TellTierLogFormatter.FormatTurnLine("t", "e", 5, true, contacts);
            Assert.Equal(
                "Tell tiers: t told e (weight 5, big) -> a SameParty rel +3 own +3 will 3.0 rf 1.01 unfamiliar p 0.0% not eligible; b KinAbroad rel -2 own -2 will -2.0 rf 0.06 hostile p 0.0% not reached",
                line);
        }

        [Fact]
        public void FormatDailyLine_Verbatim()
        {
            string line = TellTierLogFormatter.FormatDailyLine(91121, SampleTally());
            Assert.Equal(
                "Tell tiers day 91121: links 6 (in-person 3, remote 3)" +
                " | told 2 (big 2, small 0), re-heard 0, missed 1, already knew 1, not eligible 0, not reached 0" +
                " | relation in-person: <=-20 1, -19..-1 1, 0 0, 1..9 0, 10..29 1, >=30 0" +
                " | relation remote: <=-20 0, -19..-1 0, 0 1, 1..9 1, 10..29 0, >=30 1" +
                " | own relation in-person: <=-20 1, -19..-1 1, 0 0, 1..9 1, 10..29 0, >=30 0" +
                " | own relation remote: <=-20 0, -19..-1 2, 0 1, 1..9 0, 10..29 0, >=30 0" +
                " | willingness: <0 3, 0..5 0, 5..10 1, 10..30 1, >=30 1" +
                " | tiers in-person: family 1, familiar 1, unfamiliar 0, hostile 1" +
                " | tiers remote: family 0, familiar 0, unfamiliar 2, hostile 1" +
                " | sign differs 2" +
                " | held back: hostile 1 (in-person 0, remote 1), small news 1 (in-person 0, remote 1), shameful 0 (in-person 0, remote 0)",
                line);
        }

        [Fact]
        public void FormatDailyLine_NobodySpoke()
        {
            Assert.Equal("Tell tiers day 7: no lord spoke", TellTierLogFormatter.FormatDailyLine(7, new TellTierTally()));
        }

        [Fact]
        public void FormatDevReport_Verbatim()
        {
            string report = TellTierLogFormatter.FormatDevReport(SampleTally(), new TellTierTally(), 91121, DefaultTiers, 5)
                .Replace("\r\n", "\n");

            string expected = string.Join("\n", new[]
            {
                "=== Tell tiers (How do lords pass news to each other?) ===",
                "tiers on, hostile own <= -1, familiar will >= 10, big news weight >= 5",
                "Day 91121",
                "",
                "--- Today so far ---",
                "links 6 (in-person 3, remote 3)",
                "told 2 (big 2, small 0)",
                "re-heard 0",
                "missed 1",
                "already knew 1",
                "not eligible 0",
                "not reached 0",
                "relation in-person: <=-20 1, -19..-1 1, 0 0, 1..9 0, 10..29 1, >=30 0",
                "relation remote: <=-20 0, -19..-1 0, 0 1, 1..9 1, 10..29 0, >=30 1",
                "own relation in-person: <=-20 1, -19..-1 1, 0 0, 1..9 1, 10..29 0, >=30 0",
                "own relation remote: <=-20 0, -19..-1 2, 0 1, 1..9 0, 10..29 0, >=30 0",
                "willingness: <0 3, 0..5 0, 5..10 1, 10..30 1, >=30 1",
                "tiers in-person: family 1, familiar 1, unfamiliar 0, hostile 1",
                "tiers remote: family 0, familiar 0, unfamiliar 2, hostile 1",
                "sign differs 2",
                "held back: hostile 1 (in-person 0, remote 1), small news 1 (in-person 0, remote 1), shameful 0 (in-person 0, remote 0)",
                "",
                "--- Since this save was loaded (includes today) ---",
                "no lord spoke"
            });
            Assert.Equal(expected, report);
        }

        [Fact]
        public void FormatDevReport_ShowsTheCurrentSettings()
        {
            var cfg = new TellTiersConfig { Enabled = false, HostileAtOrBelow = -20, FamiliarWillingness = 12.5 };
            string report = TellTierLogFormatter.FormatDevReport(new TellTierTally(), new TellTierTally(), 3, cfg, 7)
                .Replace("\r\n", "\n");
            Assert.Contains("\ntiers off, hostile own <= -20, familiar will >= 12.5, big news weight >= 7\n", report);
        }

        // ── 引擎 ──

        private static (RumorEngine engine, FakePropagationChannel channel, FakeHeroTraitLookup traits, VividWorldConfig cfg)
            CreateEngine(bool logTurns, long seed = 42L, bool tiers = true)
        {
            var cfg = new VividWorldConfig();
            cfg.Propagation.BaseTellChancePerContact = 1.0;
            cfg.Propagation.MaxContactsPerQuery = 20;
            cfg.Debug.LogTellerTurns = logTurns;
            cfg.Propagation.TellTiers.Enabled = tiers;
            var rng = new SplitMix64Rng();
            var channel = new FakePropagationChannel();
            var traits = new FakeHeroTraitLookup();
            var retention = FactRetentionPolicies.Create(cfg, rng, seed);
            var engine = new RumorEngine(cfg, retention, NullEmbellishmentPolicy.Instance, channel, traits, rng, seed, "player");
            return (engine, channel, traits, cfg);
        }

        private static WorldEvent NewEvent(int weightTen = 6)
        {
            return new WorldEvent
            {
                EventId = "evt_1",
                Type = "test_event",
                DramaWeight = weightTen,
                DramaScale = DramaScales.Ten,
                Day = 10.0,
                Origin = EventOrigin.Public,
                Facts = new List<Fact> { new Fact { Id = "f1", Text = "Something happened", TextId = "VividWorld_Fact_f1" } },
                KnownBy = new List<KnownByEntry>()
            };
        }

        private static void Lord(FakeHeroTraitLookup traits, string id)
            => traits.Set(new TraitProfile { HeroId = id, IsAlive = true, IsLord = true });

        private static PropagationOutcome Tell(RumorEngine engine, WorldEvent evt)
        {
            if (evt.EntryFor("teller") == null) evt.KnownBy.Add(new KnownByEntry { HeroId = "teller", Hop = 0 });
            return engine.PropagateFromTeller(evt, "teller", 10.0, 8);
        }

        [Fact]
        public void Engine_HostileContact_IsHeldBack_NoRollNoChance()
        {
            var (engine, channel, traits, _) = CreateEngine(logTurns: true);
            Lord(traits, "teller"); Lord(traits, "foe"); Lord(traits, "friend");
            channel.AddLink("teller", "foe", ChannelKind.SameSettlement, 1000.0, relation: 50, ownRelation: -1);
            channel.AddLink("teller", "friend", ChannelKind.SameSettlement, 1000.0, relation: 50, ownRelation: 20);

            var outcome = Tell(engine, NewEvent());

            Assert.Equal(new[] { "friend" }, outcome.NewKnowers.Select(k => k.HeroId).ToArray());
            var foe = outcome.Contacts.Single(c => c.HeroId == "foe");
            Assert.Equal(ContactStatus.HeldBackHostile, foe.Status);
            Assert.Equal(0.0, foe.Chance);
            Assert.Equal(TellTier.Hostile, foe.Tier);
            Assert.Equal(-1, foe.OwnRelation);
        }

        [Fact]
        public void Engine_UnfamiliarContact_SmallNewsHeldBack_BigNewsRolls()
        {
            var (engine, channel, traits, _) = CreateEngine(logTurns: true);
            Lord(traits, "teller"); Lord(traits, "stranger");
            channel.AddLink("teller", "stranger", ChannelKind.SameSettlement, 1000.0, relation: 0, ownRelation: 0);

            var small = Tell(engine, NewEvent(weightTen: 4));
            Assert.Empty(small.NewKnowers);
            var held = Assert.Single(small.Contacts);
            Assert.Equal(ContactStatus.HeldBackSmallNews, held.Status);
            Assert.Equal(TellTier.Unfamiliar, held.Tier);
            Assert.Equal(0.0, held.Chance);

            var big = Tell(engine, NewEvent(weightTen: 5));
            Assert.Equal(new[] { "stranger" }, big.NewKnowers.Select(k => k.HeroId).ToArray());
            Assert.Equal(ContactStatus.Told, Assert.Single(big.Contacts).Status);
        }

        [Fact]
        public void Engine_Family_IsToldEvenWhenSmallNews_AndEvenWhenHostileByTheNumbers()
        {
            var (engine, channel, traits, _) = CreateEngine(logTurns: true);
            Lord(traits, "teller"); Lord(traits, "kin");
            channel.AddLink("teller", "kin", ChannelKind.SameParty, 1000.0, relation: 0, ownRelation: -80, isFamily: true);

            var outcome = Tell(engine, NewEvent(weightTen: 1));

            Assert.Equal(new[] { "kin" }, outcome.NewKnowers.Select(k => k.HeroId).ToArray());
            var obs = Assert.Single(outcome.Contacts);
            Assert.True(obs.IsFamily);
            Assert.Equal(TellTier.Familiar, obs.Tier);
            Assert.Equal(ContactStatus.Told, obs.Status);
        }

        [Fact]
        public void Engine_TellersTraitsMoveWillingnessAcrossTheFamiliarLine()
        {
            // 同一個好感 5：仗義的人意願 5 + 6*2 = 17 算熟人，小事照講；
            // 理性的人意願 5 + (-8)*2 = -11 算不熟，小事不講。
            var (engine, channel, traits, _) = CreateEngine(logTurns: true);
            traits.Set(new TraitProfile { HeroId = "teller", IsAlive = true, IsLord = true, Generosity = 2 });
            Lord(traits, "c1");
            channel.AddLink("teller", "c1", ChannelKind.SameSettlement, 1000.0, relation: 5, ownRelation: 5);
            var generous = Tell(engine, NewEvent(weightTen: 2));
            Assert.Equal(new[] { "c1" }, generous.NewKnowers.Select(k => k.HeroId).ToArray());
            Assert.Equal(17.0, Assert.Single(generous.Contacts).Willingness);

            var (engine2, channel2, traits2, _) = CreateEngine(logTurns: true);
            traits2.Set(new TraitProfile { HeroId = "teller", IsAlive = true, IsLord = true, Calculating = 2 });
            Lord(traits2, "c1");
            channel2.AddLink("teller", "c1", ChannelKind.SameSettlement, 1000.0, relation: 5, ownRelation: 5);
            var calculating = Tell(engine2, NewEvent(weightTen: 2));
            Assert.Empty(calculating.NewKnowers);
            var obs = Assert.Single(calculating.Contacts);
            Assert.Equal(-11.0, obs.Willingness);
            Assert.Equal(ContactStatus.HeldBackSmallNews, obs.Status);
        }

        [Fact]
        public void Engine_EveryObservationCarriesTheNewFields_IncludingNotEligibleAndNotReached()
        {
            var (engine, channel, traits, cfg) = CreateEngine(logTurns: true);
            cfg.Propagation.MaxNewKnowersPerEventPerTick = 1;
            Lord(traits, "teller"); Lord(traits, "t1"); Lord(traits, "r1");
            channel.AddLink("teller", "ghost", ChannelKind.SameSettlement, 1.0, relation: 3, ownRelation: -4, isFamily: true);
            channel.AddLink("teller", "t1", ChannelKind.SameParty, 1000.0, relation: 3, ownRelation: 12);
            channel.AddLink("teller", "r1", ChannelKind.Kingdom, 1.0, relation: 3, ownRelation: 2);

            var outcome = Tell(engine, NewEvent());

            var ghost = outcome.Contacts.Single(c => c.HeroId == "ghost");
            Assert.Equal(ContactStatus.NotEligible, ghost.Status);
            Assert.Equal(-4, ghost.OwnRelation);
            Assert.True(ghost.IsFamily);
            Assert.Equal(-4.0, ghost.Willingness);
            var rest = outcome.Contacts.Single(c => c.HeroId == "r1");
            Assert.Equal(ContactStatus.NotReached, rest.Status);
            Assert.Equal(2, rest.OwnRelation);
            Assert.Equal(2.0, rest.Willingness);
            Assert.Equal(TellTier.Unfamiliar, rest.Tier);
        }

        [Fact]
        public void Engine_TiersBlockEvenWhenTurnLoggingIsOff()
        {
            var (engine, channel, traits, _) = CreateEngine(logTurns: false);
            Lord(traits, "teller"); Lord(traits, "foe");
            channel.AddLink("teller", "foe", ChannelKind.SameSettlement, 1000.0, relation: 50, ownRelation: -5);

            var outcome = Tell(engine, NewEvent());

            Assert.Empty(outcome.NewKnowers);
            Assert.Empty(outcome.Contacts);
        }

        [Fact]
        public void Engine_TiersDisabled_BehavesLikeBefore()
        {
            var (engine, channel, traits, _) = CreateEngine(logTurns: true, tiers: false);
            Lord(traits, "teller"); Lord(traits, "foe"); Lord(traits, "stranger");
            channel.AddLink("teller", "foe", ChannelKind.SameSettlement, 1000.0, relation: 50, ownRelation: -50);
            channel.AddLink("teller", "stranger", ChannelKind.SameParty, 1000.0, relation: 0, ownRelation: 0);

            var outcome = Tell(engine, NewEvent(weightTen: 1));

            Assert.Equal(new[] { "foe", "stranger" }, outcome.NewKnowers.Select(k => k.HeroId).ToArray());
            Assert.All(outcome.Contacts, c => Assert.Equal(ContactStatus.Told, c.Status));
            Assert.Equal(TellTier.Hostile, outcome.Contacts.Single(c => c.HeroId == "foe").Tier);   // 診斷照樣算層，只是不擋
        }

        [Fact]
        public void Engine_AHitOnSomeoneWhoAlreadyHeardIt_IsRecordedAsReheard_NotTold()
        {
            var (engine, channel, traits, _) = CreateEngine(logTurns: true);
            Lord(traits, "teller");
            Lord(traits, "old");
            Lord(traits, "fresh");
            channel.AddLink("teller", "old", ChannelKind.SameSettlement, 1000.0, 15);     // 一定擲中
            channel.AddLink("teller", "fresh", ChannelKind.SameSettlement, 1000.0, 15);   // 一定擲中

            var evt = NewEvent();
            evt.KnownBy.Add(new KnownByEntry { HeroId = "teller", Hop = 0 });
            // 已經聽過、帶著興趣的人：擲中時走重聽
            evt.KnownBy.Add(new KnownByEntry { HeroId = "old", Hop = 2, Interest = 0.5 });

            var outcome = engine.PropagateFromTeller(evt, "teller", 10.0, 8);

            Assert.Equal(new[] { "fresh" }, outcome.NewKnowers.Select(k => k.HeroId).ToArray());
            Assert.Single(outcome.Reheard);
            Assert.Equal(ContactStatus.Reheard, outcome.Contacts.Single(c => c.HeroId == "old").Status);
            Assert.Equal(ContactStatus.Told, outcome.Contacts.Single(c => c.HeroId == "fresh").Status);

            string line = TellTierLogFormatter.FormatTurnLine("teller", "evt_1", 3, false,
                outcome.Contacts.Where(c => c.HeroId == "old").ToArray());
            Assert.EndsWith(" re-heard", line);
        }

        [Fact]
        public void Engine_RecordsAllFiveRollStatuses_IncludingNotReachedAfterTheCap()
        {
            var (engine, channel, traits, cfg) = CreateEngine(logTurns: true);
            Assert.Equal(2, cfg.Propagation.MaxNewKnowersPerEventPerTick);
            foreach (var id in new[] { "teller", "known", "miss", "t1", "t2", "r1", "r2" }) Lord(traits, id);
            // "ghost" 沒有個性資料：不在傳話網路裡

            channel.AddLink("teller", "known", ChannelKind.SameSettlement, 1.0, 5);
            channel.AddLink("teller", "ghost", ChannelKind.SameSettlement, 1.0, 6);
            channel.AddLink("teller", "miss", ChannelKind.Kingdom, 0.0, -3);          // 權重 0 => 機率 0 => 一定擲不中
            channel.AddLink("teller", "t1", ChannelKind.SameArmy, 1000.0, 20);        // 機率遠大於 1 => 一定擲中
            channel.AddLink("teller", "player", ChannelKind.SameSettlement, 1000.0, 50);   // 玩家那一筆不記
            channel.AddLink("teller", "t2", ChannelKind.SameParty, 1000.0, 30);       // 擲中，新增人數達上限
            channel.AddLink("teller", "r1", ChannelKind.SameClan, 1.0, 7);            // 沒輪到
            channel.AddLink("teller", "r2", ChannelKind.KinAbroad, 1.0, -9);          // 沒輪到

            var evt = NewEvent();
            evt.KnownBy.Add(new KnownByEntry { HeroId = "teller", Hop = 0 });
            evt.KnownBy.Add(new KnownByEntry { HeroId = "known", Hop = 1 });

            var outcome = engine.PropagateFromTeller(evt, "teller", 10.0, 8);

            Assert.Equal(new[] { "t1", "t2" }, outcome.NewKnowers.Select(k => k.HeroId).ToArray());
            var seen = outcome.Contacts.Select(c => (c.HeroId, c.Status)).ToArray();
            Assert.Equal(new[]
            {
                ("known", ContactStatus.AlreadyKnows),
                ("ghost", ContactStatus.NotEligible),
                ("miss", ContactStatus.Missed),
                ("t1", ContactStatus.Told),
                ("t2", ContactStatus.Told),
                ("r1", ContactStatus.NotReached),
                ("r2", ContactStatus.NotReached)
            }, seen);

            var miss = outcome.Contacts.Single(c => c.HeroId == "miss");
            Assert.Equal(0.0, miss.Chance);
            Assert.Equal(-3, miss.Relation);
            Assert.Equal(ChannelKind.Kingdom, miss.Kind);
            Assert.True(outcome.Contacts.Single(c => c.HeroId == "t1").Chance > 1.0);
            Assert.Equal(0.0, outcome.Contacts.Single(c => c.HeroId == "r1").Chance);
        }

        [Fact]
        public void Engine_WithLoggingOff_ContactsIsEmpty()
        {
            var (engine, channel, traits, _) = CreateEngine(logTurns: false);
            Lord(traits, "teller");
            Lord(traits, "c1");
            channel.AddLink("teller", "c1", ChannelKind.SameParty, 1000.0, 10);

            var evt = NewEvent();
            evt.KnownBy.Add(new KnownByEntry { HeroId = "teller", Hop = 0 });
            var outcome = engine.PropagateFromTeller(evt, "teller", 10.0, 8);

            Assert.Single(outcome.NewKnowers);
            Assert.Empty(outcome.Contacts);
        }

        private static (string[] newKnowers, string[] reheard, string[] knownBy) RunScenario(bool logTurns, bool tiers)
        {
            var (engine, channel, traits, cfg) = CreateEngine(logTurns, seed: 7777L, tiers: tiers);
            cfg.Propagation.MaxNewKnowersPerEventPerTick = 100;   // 上限不介入，被擋的人才不會影響誰先被講到
            var ids = new[] { "teller", "a", "b", "c", "d", "e", "f", "g", "h" };
            foreach (var id in ids) Lord(traits, id);

            // 機率落在 0 與 1 之間，讓擲骰結果真的取決於種子。b、f 自己的好感是負的：分層開啟時被擋。
            channel.AddLink("teller", "a", ChannelKind.SameSettlement, 0.4, 3, ownRelation: 3);
            channel.AddLink("teller", "b", ChannelKind.SameArmy, 0.4, -10, ownRelation: -10);
            channel.AddLink("teller", "c", ChannelKind.SameArmy, 0.4, 25, ownRelation: 25);
            channel.AddLink("teller", "d", ChannelKind.SameClan, 0.4, 0, ownRelation: 0);
            channel.AddLink("teller", "e", ChannelKind.SameParty, 0.4, 40, ownRelation: 40);
            channel.AddLink("teller", "f", ChannelKind.KinAbroad, 0.4, -40, ownRelation: -40);
            channel.AddLink("teller", "g", ChannelKind.SameSettlement, 0.4, 12, ownRelation: 12);
            channel.AddLink("teller", "h", ChannelKind.Kingdom, 0.4, 8, ownRelation: 8);

            var evt = NewEvent();
            evt.KnownBy.Add(new KnownByEntry { HeroId = "teller", Hop = 0 });
            // 已經聽過、帶著興趣的人：擲中時走重聽
            evt.KnownBy.Add(new KnownByEntry { HeroId = "d", Hop = 2, Interest = 0.5 });
            evt.KnownBy.Add(new KnownByEntry { HeroId = "f", Hop = 2, Interest = 0.5 });

            var newKnowers = new List<string>();
            var reheard = new List<string>();
            for (int day = 10; day < 40; day++)
            {
                var o = engine.PropagateFromTeller(evt, "teller", day, 8);
                newKnowers.AddRange(o.NewKnowers.Select(k => day + ":" + k.HeroId));
                reheard.AddRange(o.Reheard.Select(r => day + ":" + r.Entry.HeroId + ":" + r.Kind));
            }
            return (newKnowers.ToArray(), reheard.ToArray(), evt.KnownBy.Select(k => k.HeroId + "/" + k.Hop + "/" + k.HeardCount).ToArray());
        }

        [Fact]
        public void Engine_SameSeedAndData_LoggingOnAndOff_GiveIdenticalResults()
        {
            var on = RunScenario(logTurns: true, tiers: true);
            var off = RunScenario(logTurns: false, tiers: true);

            Assert.NotEmpty(on.newKnowers);
            Assert.Equal(off.newKnowers, on.newKnowers);
            Assert.Equal(off.reheard, on.reheard);
            Assert.Equal(off.knownBy, on.knownBy);
        }

        [Fact]
        public void Engine_BlockedContactsDoNotChangeTheDiceOfTheOthers_TiersOnVersusOff()
        {
            var on = RunScenario(logTurns: false, tiers: true);
            var off = RunScenario(logTurns: false, tiers: false);

            // 關著時 b、f 也會被擲到；開著時完全不出現。其他人的結果必須逐筆相同。
            Assert.NotEmpty(on.newKnowers);
            Assert.Contains(off.newKnowers, e => e.EndsWith(":b"));
            Assert.DoesNotContain(on.newKnowers, e => e.EndsWith(":b"));
            Assert.Equal(off.newKnowers.Where(e => !e.EndsWith(":b")).ToArray(), on.newKnowers);

            Assert.Equal(off.reheard.Where(e => !e.Contains(":f:")).ToArray(), on.reheard);
            Assert.Equal(
                off.knownBy.Where(e => !e.StartsWith("b/") && !e.StartsWith("f/")).ToArray(),
                on.knownBy.Where(e => !e.StartsWith("b/") && !e.StartsWith("f/")).ToArray());
        }

        [Fact]
        public void FormatTurnLine_PrintsRelationFactor_MatchingOwnRelation()
        {
            var (engine, channel, traits, cfg) = CreateEngine(logTurns: true);
            Lord(traits, "teller");
            Lord(traits, "c_inperson");
            Lord(traits, "c_remote");

            // c_inperson: Relation = 50, OwnRelation = -30
            channel.AddLink("teller", "c_inperson", ChannelKind.SameSettlement, weight: 1.0, relation: 50, ownRelation: -30);
            // c_remote: Relation = -50, OwnRelation = 40
            channel.AddLink("teller", "c_remote", ChannelKind.Kingdom, weight: 1.0, relation: -50, ownRelation: 40);

            var evt = NewEvent();
            var outcome = Tell(engine, evt);

            Assert.Equal(2, outcome.Contacts.Count);
            var obsInPerson = outcome.Contacts.Single(c => c.HeroId == "c_inperson");
            var obsRemote = outcome.Contacts.Single(c => c.HeroId == "c_remote");

            double expectedRfInPerson = RelationFactor.For(ChannelKind.SameSettlement, -30, cfg.Relation);
            double expectedRfRemote = RelationFactor.For(ChannelKind.Kingdom, 40, cfg.Relation);

            Assert.Equal(expectedRfInPerson, obsInPerson.RelationFactor, precision: 4);
            Assert.Equal(expectedRfRemote, obsRemote.RelationFactor, precision: 4);

            string line = TellTierLogFormatter.FormatTurnLine("teller", evt.EventId, evt.DramaWeightTen, true, outcome.Contacts);

            string expectedSnippetInPerson = string.Format(System.Globalization.CultureInfo.InvariantCulture,
                "c_inperson SameSettlement rel +50 own -30 will {0:0.0} rf {1:0.00}",
                obsInPerson.Willingness, expectedRfInPerson);
            string expectedSnippetRemote = string.Format(System.Globalization.CultureInfo.InvariantCulture,
                "c_remote Kingdom rel -50 own +40 will {0:0.0} rf {1:0.00}",
                obsRemote.Willingness, expectedRfRemote);

            Assert.Contains(expectedSnippetInPerson, line);
            Assert.Contains(expectedSnippetRemote, line);
        }
    }
}
