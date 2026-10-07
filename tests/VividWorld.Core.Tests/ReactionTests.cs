using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using VividWorld.Core.Catalog;
using VividWorld.Core.Config;
using VividWorld.Core.Events;
using VividWorld.Core.Grudges;
using VividWorld.Core.Persistence;
using VividWorld.Core.Rumors;
using Xunit;

namespace VividWorld.Core.Tests
{
    /// <summary>聽到消息的人反應多大：個性倍數與「跟承受的人的關係」倍數。</summary>
    public class ReactionTests
    {
        private const string Hearer = "hero_hearer";
        private const string Subject = "hero_subject";
        private const string Receiver = "hero_receiver";

        private static ConsequenceConfig Cfg() => new ConsequenceConfig
        {
            Enabled = true,
            HopConfidence = new[] { 1.0, 1.0, 0.75, 0.5, 0.3, 0.2 },
            BystanderMultiplier = 0.35,
            MaxAbsoluteDeltaPerHeroPerDay = 6.0,
            MinAbsoluteDelta = 1
        };

        /// <summary>聽者是第 0 手的當事人（手數折扣與旁人倍數都是 1），這樣每條測試只看新倍數。</summary>
        private static (WorldEvent evt, KnownByEntry obs, List<Fact> facts) Context(
            bool hearerIsParticipant = true, int hop = 0, bool bindReceiver = true, string receiverId = Receiver)
        {
            var participants = new Dictionary<string, string> { ["subject"] = Subject };
            if (bindReceiver) participants["receiver"] = receiverId;
            if (hearerIsParticipant) participants["witness"] = Hearer;
            var evt = new WorldEvent { EventId = "evt_r", Type = "test_event", Day = 10.0, Participants = participants };
            var obs = new KnownByEntry { HeroId = Hearer, Hop = hop };
            var facts = new List<Fact>
            {
                new Fact { Id = "f1", Vars = new Dictionary<string, string> { ["S"] = "hero:" + Subject } }
            };
            return (evt, obs, facts);
        }

        private static OpinionDef Opinion(double amount, string? trait = null, string? receiver = null) =>
            new OpinionDef { About = "subject", Amount = amount, Trait = trait, Receiver = receiver };

        private static TraitProfile Profile(int honor = 0, int mercy = 0, int valor = 0, int generosity = 0, int calculating = 0) =>
            new TraitProfile { HeroId = Hearer, Honor = honor, Mercy = mercy, Valor = valor, Generosity = generosity, Calculating = calculating };

        private static ReactionInputs Inputs(
            TraitProfile? traits = null, Func<string, int?>? affection = null, Func<string, bool>? sameClan = null,
            bool enabled = true) => new ReactionInputs
            {
                Config = new ReactionConfig { Enabled = enabled },
                HearerTraits = traits,
                AffectionToward = affection,
                IsSameClan = sameClan
            };

        private static ConsequenceResult Run(
            OpinionDef def, ReactionInputs? inputs, double budget = 100.0,
            bool hearerIsParticipant = true, int hop = 0, bool bindReceiver = true, string receiverId = Receiver,
            List<RelationImpact>? already = null)
        {
            var (evt, obs, facts) = Context(hearerIsParticipant, hop, bindReceiver, receiverId);
            if (already != null) obs.RelationImpacts = already;
            return ConsequenceResolver.Resolve(evt, obs, facts, new[] { def }, Cfg(), budget, inputs);
        }

        // ---------------- 個性倍數 ----------------

        [Theory]
        [InlineData(-2, 0.25)]
        [InlineData(-1, 0.5)]
        [InlineData(0, 1.0)]
        [InlineData(1, 1.5)]
        [InlineData(2, 2.0)]
        public void TraitMultiplier_ByLevel(int level, double expected)
        {
            // 不到最小變動的會被排除，所以量取夠大
            var r = Run(Opinion(-10.0, "mercy"), Inputs(Profile(mercy: level)));
            Assert.Empty(r.Exclusions);
            var change = Assert.Single(r.Changes);
            Assert.Equal(expected, change.TraitMultiplier, 6);
            Assert.Equal(-10.0 * expected, change.FullAmount, 6);
            Assert.Equal("mercy", change.TraitName);
            Assert.Equal(level, change.TraitLevel);
        }

        [Fact]
        public void TraitMultiplier_ReadsTheNamedTraitOnly()
        {
            var r = Run(Opinion(-10.0, "valor"), Inputs(Profile(honor: 2, mercy: 2, valor: -1, generosity: 2, calculating: 2)));
            Assert.Equal(0.5, r.Changes[0].TraitMultiplier, 6);
        }

        [Fact]
        public void TraitMultiplier_OpinionWithoutTrait_IsOne()
        {
            var r = Run(Opinion(-10.0), Inputs(Profile(honor: 2, mercy: 2, valor: 2, generosity: 2, calculating: 2)));
            var change = Assert.Single(r.Changes);
            Assert.Equal(1.0, change.TraitMultiplier);
            Assert.Null(change.TraitName);
            Assert.Null(change.TraitLevel);
            Assert.Equal(-10.0, change.FullAmount, 6);
        }

        [Fact]
        public void TraitMultiplier_HearerNotFound_IsOne()
        {
            var r = Run(Opinion(-10.0, "honor"), Inputs(traits: null));
            var change = Assert.Single(r.Changes);
            Assert.Equal(1.0, change.TraitMultiplier);
            Assert.Equal("honor", change.TraitName);
            Assert.Null(change.TraitLevel);
        }

        // ---------------- 關係倍數 ----------------

        [Fact]
        public void Relation_SameClan_UsesSameClanMultiplier()
        {
            var r = Run(Opinion(-10.0, receiver: "receiver"), Inputs(sameClan: id => id == Receiver));
            var change = Assert.Single(r.Changes);
            Assert.Equal(2.0, change.RelationMultiplier);
            Assert.Equal("same clan", change.RelationReason);
            Assert.Equal(Receiver, change.ReceiverHeroId);
            Assert.Equal(-20.0, change.FullAmount, 6);
        }

        [Fact]
        public void Relation_SameClanBeatsAffection()
        {
            var r = Run(Opinion(-10.0, receiver: "receiver"), Inputs(affection: _ => -50, sameClan: _ => true));
            Assert.Equal("same clan", r.Changes[0].RelationReason);
            Assert.Equal(2.0, r.Changes[0].RelationMultiplier);
        }

        [Theory]
        [InlineData(30, 1.5, "friend")]
        [InlineData(29, 1.0, "none")]
        [InlineData(100, 1.5, "friend")]
        [InlineData(-19, 1.0, "none")]
        [InlineData(-20, 0.5, "hostile")]
        [InlineData(-100, 0.5, "hostile")]
        [InlineData(0, 1.0, "none")]
        public void Relation_AffectionThresholds(int affection, double expected, string reason)
        {
            var r = Run(Opinion(-10.0, receiver: "receiver"), Inputs(affection: _ => affection));
            var change = Assert.Single(r.Changes);
            Assert.Equal(expected, change.RelationMultiplier);
            Assert.Equal(reason, change.RelationReason);
            Assert.Equal(Receiver, change.ReceiverHeroId);
        }

        [Fact]
        public void Relation_AffectionUnknown_IsOne()
        {
            var r = Run(Opinion(-10.0, receiver: "receiver"), Inputs(affection: _ => null));
            Assert.Equal(1.0, r.Changes[0].RelationMultiplier);
            Assert.Equal("none", r.Changes[0].RelationReason);
        }

        [Fact]
        public void Relation_NoWorldGiven_IsOne()
        {
            var r = Run(Opinion(-10.0, receiver: "receiver"), Inputs());
            Assert.Equal(1.0, r.Changes[0].RelationMultiplier);
        }

        [Fact]
        public void Relation_HearerIsTheReceiver_IsOneAndSelf()
        {
            // 聽者自己就是承受的人：就算同家族、好感很高也不乘
            var r = Run(Opinion(-10.0, receiver: "receiver"),
                Inputs(affection: _ => 90, sameClan: _ => true), receiverId: Hearer);
            var change = Assert.Single(r.Changes);
            Assert.Equal(1.0, change.RelationMultiplier);
            Assert.Equal("self", change.RelationReason);
            Assert.Equal(Hearer, change.ReceiverHeroId);
        }

        [Fact]
        public void Relation_OpinionWithoutReceiver_IsOne()
        {
            var r = Run(Opinion(-10.0), Inputs(affection: _ => 90, sameClan: _ => true));
            var change = Assert.Single(r.Changes);
            Assert.Equal(1.0, change.RelationMultiplier);
            Assert.Equal("none", change.RelationReason);
            Assert.Equal(string.Empty, change.ReceiverHeroId);
        }

        [Fact]
        public void Relation_ReceiverRoleNotBound_IsOne()
        {
            var r = Run(Opinion(-10.0, receiver: "receiver"), Inputs(affection: _ => 90, sameClan: _ => true), bindReceiver: false);
            var change = Assert.Single(r.Changes);
            Assert.Equal(1.0, change.RelationMultiplier);
            Assert.Equal("none", change.RelationReason);
            Assert.Equal(string.Empty, change.ReceiverHeroId);
        }

        // ---------------- 相乘、最小變動、每日上限 ----------------

        [Fact]
        public void BothMultipliers_Multiply()
        {
            var r = Run(Opinion(-10.0, "honor", "receiver"), Inputs(Profile(honor: 2), affection: _ => 40));
            var change = Assert.Single(r.Changes);
            Assert.Equal(-10.0 * 2.0 * 1.5, change.FullAmount, 6);
            Assert.Equal(-30.0, change.Requested, 6);
        }

        [Fact]
        public void WorkedExample_AmountMinus6_Hop2_Onlooker_TraitPlus1_Friend()
        {
            var r = Run(Opinion(-6.0, "honor", "receiver"), Inputs(Profile(honor: 1), affection: _ => 35),
                hearerIsParticipant: false, hop: 2);
            var change = Assert.Single(r.Changes);
            double expected = -6.0 * 0.75 * 0.35 * 1.5 * 1.5;
            Assert.Equal(expected, change.FullAmount, 9);
            Assert.Equal(expected, change.Requested, 9);
            Assert.Equal(-6.0, change.TemplateAmount);
        }

        [Fact]
        public void ProductBelowMinimum_IsExcluded_WithTheMultipliedAmount()
        {
            // −2 × 0.25 × 0.5 = −0.25，不到 1
            var r = Run(Opinion(-2.0, "honor", "receiver"), Inputs(Profile(honor: -2), affection: _ => -50));
            Assert.Empty(r.Changes);
            var excl = Assert.Single(r.Exclusions);
            Assert.Equal(OpinionSkipReason.BelowMinimum, excl.Reason);
            Assert.Equal(-0.25, excl.Amount, 9);
        }

        [Fact]
        public void ProductAboveDailyBudget_IsClamped()
        {
            var r = Run(Opinion(-10.0, "honor", "receiver"), Inputs(Profile(honor: 2), affection: _ => 40), budget: 6.0);
            var change = Assert.Single(r.Changes);
            Assert.Equal(-6.0, change.Requested, 6);
            Assert.Equal(-30.0, change.FullAmount, 6);
        }

        [Fact]
        public void PositiveAmount_FollowsTheSameRule()
        {
            var r = Run(Opinion(4.0, "generosity", "receiver"), Inputs(Profile(generosity: 1), sameClan: _ => true));
            var change = Assert.Single(r.Changes);
            Assert.Equal(4.0 * 1.5 * 2.0, change.FullAmount, 6);
            Assert.True(change.Requested > 0);
        }

        [Fact]
        public void PositiveAmount_ShrunkByLowMultipliers_StaysPositive()
        {
            var r = Run(Opinion(8.0, "generosity", "receiver"), Inputs(Profile(generosity: -1), affection: _ => -30));
            var change = Assert.Single(r.Changes);
            Assert.Equal(8.0 * 0.5 * 0.5, change.FullAmount, 6);
            Assert.True(change.Requested > 0);
        }

        // ---------------- 關掉、沒給資料 ----------------

        [Fact]
        public void ReactionDisabled_GivesTheSameResultAsNoInputs()
        {
            var def = Opinion(-10.0, "honor", "receiver");
            var off = Run(def, Inputs(Profile(honor: 2), affection: _ => 90, sameClan: _ => true, enabled: false));
            var none = Run(def, null);

            var a = Assert.Single(off.Changes);
            var b = Assert.Single(none.Changes);
            Assert.Equal(b.FullAmount, a.FullAmount);
            Assert.Equal(b.Requested, a.Requested);
            Assert.Equal(1.0, a.TraitMultiplier);
            Assert.Equal(1.0, a.RelationMultiplier);
            Assert.Equal("none", a.RelationReason);
        }

        [Fact]
        public void NoInputs_MultipliersAreOne_AndOldCallShapeStillWorks()
        {
            var (evt, obs, facts) = Context();
            var r = ConsequenceResolver.Resolve(evt, obs, facts, new[] { Opinion(-10.0, "honor", "receiver") }, Cfg(), 100.0);
            var change = Assert.Single(r.Changes);
            Assert.Equal(-10.0, change.FullAmount);
            Assert.Equal(1.0, change.TraitMultiplier);
            Assert.Equal(1.0, change.RelationMultiplier);
        }

        // ---------------- 冪等：delta = full − already ----------------

        [Fact]
        public void Rehear_WithAHigherMultiplier_AppliesTheDifference()
        {
            // 第一次結算時沒有倍數（記下 −10）；之後重聽，這次聽者是朋友、個性 +2：full = −10 × 2 × 1.5 = −30，差額 −20
            var already = new List<RelationImpact>
            {
                new RelationImpact { AboutHeroId = Subject, Requested = -10.0, Source = GrudgeSource.Rumor }
            };
            var r = Run(Opinion(-10.0, "honor", "receiver"), Inputs(Profile(honor: 2), affection: _ => 40), already: already);
            var change = Assert.Single(r.Changes);
            Assert.Equal(-30.0, change.FullAmount, 6);
            Assert.Equal(-10.0, change.AlreadyApplied, 6);
            Assert.Equal(-20.0, change.Requested, 6);
        }

        [Fact]
        public void Rehear_WithALowerMultiplier_AppliesTheOppositeSign()
        {
            // 這是既有算法的結果，不為它加特例：full 變小，差額會是反方向的正數
            var already = new List<RelationImpact>
            {
                new RelationImpact { AboutHeroId = Subject, Requested = -10.0, Source = GrudgeSource.Rumor }
            };
            var r = Run(Opinion(-10.0, "honor"), Inputs(Profile(honor: -2)), already: already);
            var change = Assert.Single(r.Changes);
            Assert.Equal(-2.5, change.FullAmount, 6);
            Assert.Equal(7.5, change.Requested, 6);
        }

        [Fact]
        public void Rehear_WithTheSameMultipliers_AddsNothing()
        {
            var already = new List<RelationImpact>
            {
                new RelationImpact { AboutHeroId = Subject, Requested = -15.0, Source = GrudgeSource.Rumor }
            };
            var r = Run(Opinion(-10.0, "honor"), Inputs(Profile(honor: 1)), already: already);
            Assert.Empty(r.Changes);
            Assert.Equal(OpinionSkipReason.AlreadyFullyApplied, Assert.Single(r.Exclusions).Reason);
        }

        // ---------------- 更新前就算過的好感 ----------------

        private static KnownByEntry Entry(bool? believes = null, string? reason = null, GrudgeSource? impactSource = null)
        {
            var e = new KnownByEntry { HeroId = Hearer, Believes = believes, BeliefReason = reason };
            if (impactSource != null)
            {
                e.RelationImpacts = new List<RelationImpact>
                {
                    new RelationImpact { AboutHeroId = Subject, Requested = -2.1, Source = impactSource.Value }
                };
            }
            return e;
        }

        [Fact]
        public void IsSettledBeforeBelief_LegacyMarker_IsSettled()
        {
            Assert.True(BeliefJudge.IsSettledBeforeBelief(
                Entry(true, BeliefJudge.LegacyReason, GrudgeSource.Rumor), true));
        }

        [Fact]
        public void IsSettledBeforeBelief_NotJudgedWithRumorSettlement_IsSettled()
        {
            Assert.True(BeliefJudge.IsSettledBeforeBelief(Entry(null, null, GrudgeSource.Rumor), true));
        }

        [Fact]
        public void IsSettledBeforeBelief_NotJudgedWithOnlySituationSettlement_IsNotSettled()
        {
            Assert.False(BeliefJudge.IsSettledBeforeBelief(Entry(null, null, GrudgeSource.Situation), true));
        }

        [Fact]
        public void IsSettledBeforeBelief_JudgedWithSettlement_IsNotSettled()
        {
            Assert.False(BeliefJudge.IsSettledBeforeBelief(
                Entry(true, BeliefReason.SubjectRelation.ToString(), GrudgeSource.Rumor), true));
        }

        [Fact]
        public void IsSettledBeforeBelief_BeliefDisabled_OnlyLegacyCounts()
        {
            Assert.False(BeliefJudge.IsSettledBeforeBelief(Entry(null, null, GrudgeSource.Rumor), false));
            Assert.True(BeliefJudge.IsSettledBeforeBelief(
                Entry(true, BeliefJudge.LegacyReason, GrudgeSource.Rumor), false));
        }

        [Fact]
        public void Rehear_SettledBefore_WithMultipliers_PullsBackPartOfIt_ButWithoutThemNothingChanges()
        {
            var already = new List<RelationImpact>
            {
                new RelationImpact { AboutHeroId = Subject, Requested = -2.1, Source = GrudgeSource.Rumor }
            };

            // 傳了倍數（個性 -1 → x0.5）：full = -1.05，差額 +1.05，把先前的量退回一部分
            var withInputs = Run(Opinion(-2.1, "honor"), Inputs(Profile(honor: -1)), already: already);
            Assert.Equal(1.05, Assert.Single(withInputs.Changes).Requested, 6);

            // 不傳（更新前就算過的）：兩個倍數是 1，full 等於已記的量，被排除
            var without = Run(Opinion(-2.1, "honor"), null, already: already);
            Assert.Empty(without.Changes);
            Assert.Equal(OpinionSkipReason.AlreadyFullyApplied, Assert.Single(without.Exclusions).Reason);
        }

        // ---------------- 設定 ----------------

        [Fact]
        public void Config_Defaults_MatchTheCard()
        {
            var c = new VividWorldConfig().FalseRumors.Reaction;
            Assert.True(c.Enabled);
            Assert.Equal(new[] { 0.25, 0.5, 1.0, 1.5, 2.0 }, c.TraitMultipliers);
            Assert.Equal(2.0, c.ReceiverSameClan);
            Assert.Equal(30, c.ReceiverFriendRelation);
            Assert.Equal(1.5, c.ReceiverFriend);
            Assert.Equal(-20, c.ReceiverHostileRelation);
            Assert.Equal(0.5, c.ReceiverHostile);
        }

        [Fact]
        public void Config_MergeIntoOldFile_AddsOnlyTheMissingReactionKeys()
        {
            var existing = JObject.Parse("{ \"falseRumors\": { \"reaction\": { \"receiverSameClan\": 3.0 } } }");
            var canonical = JObject.Parse(VividJson.Write(new VividWorldConfig().Normalize()));

            var result = ConfigMerge.AddMissingKeys(existing, canonical);

            Assert.Equal(3.0, (double)result.Merged.SelectToken("falseRumors.reaction.receiverSameClan")!);
            Assert.DoesNotContain("falseRumors.reaction.receiverSameClan", result.AddedPaths);
            Assert.Contains("falseRumors.reaction.enabled", result.AddedPaths);
            Assert.Contains("falseRumors.reaction.traitMultipliers", result.AddedPaths);
            Assert.Contains("falseRumors.reaction.receiverFriendRelation", result.AddedPaths);
            Assert.Contains("falseRumors.reaction.receiverFriend", result.AddedPaths);
            Assert.Contains("falseRumors.reaction.receiverHostileRelation", result.AddedPaths);
            Assert.Contains("falseRumors.reaction.receiverHostile", result.AddedPaths);
        }

        [Fact]
        public void Config_Normalize_ClampsMultipliersToZeroTen()
        {
            var cfg = new VividWorldConfig();
            cfg.FalseRumors.Reaction.ReceiverSameClan = 25;
            cfg.FalseRumors.Reaction.ReceiverHostile = -1;
            cfg.FalseRumors.Reaction.TraitMultipliers = new[] { -1.0, 0.5, 1.0, 1.5, 99.0 };
            var notices = new List<ClampNotice>();
            cfg.Normalize(notices);

            var c = cfg.FalseRumors.Reaction;
            Assert.Equal(10.0, c.ReceiverSameClan);
            Assert.Equal(0.0, c.ReceiverHostile);
            Assert.Equal(new[] { 0.0, 0.5, 1.0, 1.5, 10.0 }, c.TraitMultipliers);
            Assert.Contains(notices, n => n.Key == "falseRumors.reaction.receiverSameClan");
            Assert.Contains(notices, n => n.Key == "falseRumors.reaction.receiverHostile");
        }

        [Fact]
        public void Config_Normalize_FixesAWrongLengthTraitList()
        {
            var cfg = new VividWorldConfig();
            cfg.FalseRumors.Reaction.TraitMultipliers = new[] { 0.5, 1.0 };
            cfg.Normalize();
            Assert.Equal(5, cfg.FalseRumors.Reaction.TraitMultipliers.Length);
        }

        // ---------------- 日誌 ----------------

        [Fact]
        public void LogSuffix_PrintsBothMultipliersEvenWhenTheyAreOne()
        {
            string none = GrudgeLogFormatter.FormatReactionSuffix(1.0, null, null, 1.0, "none", "");
            Assert.Equal("trait x1.00 (none), receiver x1.00 (none)", none);

            string full = GrudgeLogFormatter.FormatReactionSuffix(1.5, "honor", 1, 1.5, "friend", "lord_x");
            Assert.Equal("trait x1.50 (honor +1), receiver x1.50 (friend lord_x)", full);

            string unknown = GrudgeLogFormatter.FormatReactionSuffix(1.0, "mercy", null, 2.0, "same clan", "lord_y");
            Assert.Equal("trait x1.00 (mercy n/a), receiver x2.00 (same clan lord_y)", unknown);

            string negative = GrudgeLogFormatter.FormatReactionSuffix(0.25, "valor", -2, 1.0, "self", "lord_z");
            Assert.Equal("trait x0.25 (valor -2), receiver x1.00 (self lord_z)", negative);
        }

        [Fact]
        public void LogLines_WithoutSuffix_AreUnchanged_AndWithSuffixAreAppendedAtTheEnd()
        {
            string plain = GrudgeLogFormatter.FormatOpinionAppliedLedgerOnly(
                "o", "a", "r", "e", -6.0, 1, 1.0, false, 0.35, -2.1, 0.0, -2.1);
            Assert.EndsWith("requested -2.10", plain);

            string withSuffix = GrudgeLogFormatter.FormatOpinionAppliedLedgerOnly(
                "o", "a", "r", "e", -6.0, 1, 1.0, false, 0.35, -2.1, 0.0, -2.1, "trait x1.00 (none), receiver x1.00 (none)");
            Assert.EndsWith("requested -2.10, trait x1.00 (none), receiver x1.00 (none)", withSuffix);
        }

        [Fact]
        public void DevPreviewLine_ShowsTemplateMultipliersAndResult()
        {
            var r = Run(Opinion(-6.0, "honor", "receiver"), Inputs(Profile(honor: 1), affection: _ => 35),
                hearerIsParticipant: false, hop: 2);
            string line = GrudgeLogFormatter.FormatOpinionPreview(r.Changes[0]);
            Assert.Contains("template -6.00", line);
            Assert.Contains("hop 2 x0.75", line);
            Assert.Contains("onlooker x0.35", line);
            Assert.Contains("trait x1.50 (honor +1)", line);
            Assert.Contains("receiver x1.50 (friend " + Receiver + ")", line);
            Assert.Contains("full -3.54", line);
        }
    }
}
