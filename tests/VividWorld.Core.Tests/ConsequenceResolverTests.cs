using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using VividWorld.Core.Catalog;
using VividWorld.Core.Config;
using VividWorld.Core.Dialogue;
using VividWorld.Core.Events;
using VividWorld.Core.Grudges;
using VividWorld.Core.Persistence;
using VividWorld.Core.Rumors;
using Xunit;

namespace VividWorld.Core.Tests
{
    public class ConsequenceResolverTests
    {
        // -------------------------------------------------------------
        // Group 1: NamedHeroIds (6+ tests)
        // -------------------------------------------------------------

        [Fact]
        public void NamedHeroIds_VarsWithHeroPrefix_IsRecognized()
        {
            var facts = new[]
            {
                new Fact
                {
                    Id = "f1",
                    Vars = new Dictionary<string, string>
                    {
                        ["KILLER"] = "hero:lord_killer_1"
                    }
                }
            };

            var names = ConsequenceResolver.NamedHeroIds(facts);
            Assert.Contains("lord_killer_1", names);
            Assert.Single(names);
        }

        [Fact]
        public void NamedHeroIds_NonHeroPrefix_IsNotRecognized()
        {
            var facts = new[]
            {
                new Fact
                {
                    Id = "f1",
                    Vars = new Dictionary<string, string>
                    {
                        ["PLACE"] = "settlement:town_v1",
                        ["FACTION"] = "faction:vlandia",
                        ["NUMBER"] = "num:100",
                        ["TEXT"] = "text:battle"
                    }
                }
            };

            var names = ConsequenceResolver.NamedHeroIds(facts);
            Assert.Empty(names);
        }

        [Fact]
        public void NamedHeroIds_MultipleHeroVarsInOneFact_BothRecognized()
        {
            var facts = new[]
            {
                new Fact
                {
                    Id = "f1",
                    Vars = new Dictionary<string, string>
                    {
                        ["HERO_A"] = "hero:lord_alpha",
                        ["HERO_B"] = "hero:lord_beta"
                    }
                }
            };

            var names = ConsequenceResolver.NamedHeroIds(facts);
            Assert.Equal(2, names.Count);
            Assert.Contains("lord_alpha", names);
            Assert.Contains("lord_beta", names);
        }

        [Fact]
        public void NamedHeroIds_NullVars_DoesNotThrowAndReturnsEmpty()
        {
            var facts = new[]
            {
                new Fact { Id = "f1", Vars = null },
                new Fact { Id = "f2", Vars = new Dictionary<string, string>() }
            };

            var names = ConsequenceResolver.NamedHeroIds(facts);
            Assert.Empty(names);
        }

        [Fact]
        public void NamedHeroIds_EmptyList_ReturnsEmptyCollection()
        {
            var names = ConsequenceResolver.NamedHeroIds(Array.Empty<Fact>());
            Assert.Empty(names);
        }

        [Fact]
        public void NamedHeroIds_CaseSensitivity_HeroPrefixIsOrdinal()
        {
            var facts = new[]
            {
                new Fact
                {
                    Id = "f1",
                    Vars = new Dictionary<string, string>
                    {
                        ["LOWER"] = "hero:lord_good",
                        ["UPPER"] = "Hero:lord_bad1",
                        ["ALL_CAPS"] = "HERO:lord_bad2"
                    }
                }
            };

            var names = ConsequenceResolver.NamedHeroIds(facts);
            Assert.Single(names);
            Assert.Contains("lord_good", names);
            Assert.DoesNotContain("lord_bad1", names);
            Assert.DoesNotContain("lord_bad2", names);
        }

        // -------------------------------------------------------------
        // Group 2: Resolve Mainline (12+ tests)
        // -------------------------------------------------------------

        private static (WorldEvent evt, KnownByEntry observer, List<Fact> facts, ConsequenceConfig cfg) CreateTestContext(
            string observerHeroId = "hero_observer",
            string targetHeroId = "hero_target",
            string targetRole = "target",
            int hop = 1,
            bool observerIsParticipant = false)
        {
            var participants = new Dictionary<string, string>
            {
                [targetRole] = targetHeroId
            };
            if (observerIsParticipant)
            {
                participants["observer_role"] = observerHeroId;
            }

            var evt = new WorldEvent
            {
                EventId = "evt_001",
                Type = "test_event",
                Day = 10.0,
                Participants = participants
            };

            var observer = new KnownByEntry
            {
                HeroId = observerHeroId,
                Hop = hop
            };

            var facts = new List<Fact>
            {
                new Fact
                {
                    Id = "fact_who",
                    Vars = new Dictionary<string, string>
                    {
                        ["TARGET"] = "hero:" + targetHeroId
                    }
                }
            };

            var cfg = new ConsequenceConfig
            {
                Enabled = true,
                HopConfidence = new[] { 1.0, 1.0, 0.75, 0.5, 0.3, 0.2 },
                BystanderMultiplier = 0.35,
                MaxAbsoluteDeltaPerHeroPerDay = 6.0,
                MinAbsoluteDelta = 1,
                LedgerOnly = false
            };

            return (evt, observer, facts, cfg);
        }

        [Fact]
        public void Resolve_Hop0Participant_ReceivesFullAmount()
        {
            var (evt, observer, facts, cfg) = CreateTestContext(
                observerHeroId: "hero_obs",
                targetHeroId: "hero_tgt",
                targetRole: "killer",
                hop: 0,
                observerIsParticipant: true);

            var opinions = new[] { new OpinionDef { About = "killer", Amount = -10.0 } };

            var result = ConsequenceResolver.Resolve(evt, observer, facts, opinions, cfg, remainingDailyBudget: 10.0);

            Assert.Empty(result.Exclusions);
            Assert.Single(result.Changes);
            var change = result.Changes[0];
            Assert.Equal(-10.0, change.Requested);
            Assert.Equal(-10.0, change.FullAmount);
            Assert.Equal(0.0, change.AlreadyApplied);
            Assert.Equal(1.0, change.WitnessMultiplier);
            Assert.Equal(1.0, change.HopConfidence);
            Assert.True(change.ObserverIsParticipant);
            Assert.Equal(0, change.ObserverHop);
            Assert.Equal("fact_who", change.SourceFactId);
        }

        [Fact]
        public void Resolve_BystanderHop1_ReceivesBystanderMultiplierAndHopConfidence()
        {
            var (evt, observer, facts, cfg) = CreateTestContext(
                observerHeroId: "hero_obs",
                targetHeroId: "hero_tgt",
                targetRole: "claimant",
                hop: 1,
                observerIsParticipant: false);

            var opinions = new[] { new OpinionDef { About = "claimant", Amount = -6.0 } };

            var result = ConsequenceResolver.Resolve(evt, observer, facts, opinions, cfg, remainingDailyBudget: 6.0);

            Assert.Empty(result.Exclusions);
            Assert.Single(result.Changes);
            var change = result.Changes[0];
            // full = -6.0 * 1.0 * 0.35 = -2.1
            Assert.Equal(-2.1, change.FullAmount, 4);
            Assert.Equal(-2.1, change.Requested, 4);
            Assert.Equal(0.35, change.WitnessMultiplier);
            Assert.Equal(1.0, change.HopConfidence);
            Assert.False(change.ObserverIsParticipant);
        }

        [Fact]
        public void Resolve_BystanderHop3_CalculatesCorrectDecay()
        {
            var (evt, observer, facts, cfg) = CreateTestContext(
                observerHeroId: "hero_obs",
                targetHeroId: "hero_tgt",
                targetRole: "claimant",
                hop: 3,
                observerIsParticipant: false);

            var opinions = new[] { new OpinionDef { About = "claimant", Amount = -6.0 } };

            var result = ConsequenceResolver.Resolve(evt, observer, facts, opinions, cfg, remainingDailyBudget: 6.0);

            Assert.Empty(result.Exclusions);
            Assert.Single(result.Changes);
            var change = result.Changes[0];
            // full = -6.0 * 0.5 * 0.35 = -1.05
            Assert.Equal(-1.05, change.FullAmount, 4);
            Assert.Equal(-1.05, change.Requested, 4);
            Assert.Equal(0.5, change.HopConfidence);
            Assert.Equal(0.35, change.WitnessMultiplier);
        }

        [Fact]
        public void Resolve_HopBeyondCurve_ClampedToLastValue()
        {
            var (evt, observer, facts, cfg) = CreateTestContext(
                observerHeroId: "hero_obs",
                targetHeroId: "hero_tgt",
                targetRole: "killer",
                hop: 10,
                observerIsParticipant: false);

            var opinions = new[] { new OpinionDef { About = "killer", Amount = -20.0 } };

            var result = ConsequenceResolver.Resolve(evt, observer, facts, opinions, cfg, remainingDailyBudget: 6.0);

            Assert.Empty(result.Exclusions);
            Assert.Single(result.Changes);
            var change = result.Changes[0];
            // hop clamped to 5: confidence = 0.2. full = -20.0 * 0.2 * 0.35 = -1.4
            Assert.Equal(0.2, change.HopConfidence);
            Assert.Equal(-1.4, change.FullAmount, 4);
            Assert.Equal(-1.4, change.Requested, 4);
        }

        [Fact]
        public void Resolve_PositiveAmount_CalculatesPositiveDelta()
        {
            var (evt, observer, facts, cfg) = CreateTestContext(
                observerHeroId: "hero_obs",
                targetHeroId: "hero_tgt",
                targetRole: "veteran",
                hop: 0,
                observerIsParticipant: true);

            var opinions = new[] { new OpinionDef { About = "veteran", Amount = 5.0 } };

            var result = ConsequenceResolver.Resolve(evt, observer, facts, opinions, cfg, remainingDailyBudget: 6.0);

            Assert.Empty(result.Exclusions);
            Assert.Single(result.Changes);
            Assert.Equal(5.0, result.Changes[0].Requested);
            Assert.Equal(5.0, result.Changes[0].FullAmount);
        }

        [Fact]
        public void Resolve_AboutNotBoundInEvent_ExcludedWithAboutNotBound()
        {
            var (evt, observer, facts, cfg) = CreateTestContext();
            var opinions = new[] { new OpinionDef { About = "unbound_role", Amount = -5.0 } };

            var result = ConsequenceResolver.Resolve(evt, observer, facts, opinions, cfg, remainingDailyBudget: 6.0);

            Assert.Empty(result.Changes);
            Assert.Single(result.Exclusions);
            Assert.Equal(OpinionSkipReason.AboutNotBound, result.Exclusions[0].Reason);
            Assert.Equal("unbound_role", result.Exclusions[0].AboutRole);
        }

        [Fact]
        public void Resolve_AboutIsObserverSelf_ExcludedWithAboutIsObserver()
        {
            var (evt, observer, facts, cfg) = CreateTestContext(
                observerHeroId: "hero_self",
                targetHeroId: "hero_self",
                targetRole: "speaker");

            var opinions = new[] { new OpinionDef { About = "speaker", Amount = -5.0 } };

            var result = ConsequenceResolver.Resolve(evt, observer, facts, opinions, cfg, remainingDailyBudget: 6.0);

            Assert.Empty(result.Changes);
            Assert.Single(result.Exclusions);
            Assert.Equal(OpinionSkipReason.AboutIsObserver, result.Exclusions[0].Reason);
            Assert.Equal("hero_self", result.Exclusions[0].AboutHeroId);
        }

        [Fact]
        public void Resolve_NotNamedByAnyBelievedFact_ExcludedWithNotNamedByAnyFact()
        {
            var (evt, observer, facts, cfg) = CreateTestContext(targetHeroId: "hero_target");
            // 碎片裡點到的是別人，不是 hero_target
            facts[0].Vars!["TARGET"] = "hero:someone_else";

            var opinions = new[] { new OpinionDef { About = "target", Amount = -5.0 } };

            var result = ConsequenceResolver.Resolve(evt, observer, facts, opinions, cfg, remainingDailyBudget: 6.0);

            Assert.Empty(result.Changes);
            Assert.Single(result.Exclusions);
            Assert.Equal(OpinionSkipReason.NotNamedByAnyFact, result.Exclusions[0].Reason);
            Assert.Equal("hero_target", result.Exclusions[0].AboutHeroId);
        }

        [Fact]
        public void Resolve_TwoOpinionsInTemplate_BothProduceChanges()
        {
            var evt = new WorldEvent
            {
                EventId = "evt_brawl",
                Type = "brawl_hushed_up",
                Participants = new Dictionary<string, string>
                {
                    ["aggrieved"] = "hero_aggrieved",
                    ["patron"] = "hero_patron"
                }
            };
            var observer = new KnownByEntry { HeroId = "hero_bystander", Hop = 0 };
            var facts = new List<Fact>
            {
                new Fact
                {
                    Id = "f1",
                    Vars = new Dictionary<string, string>
                    {
                        ["A"] = "hero:hero_aggrieved",
                        ["B"] = "hero:hero_patron"
                    }
                }
            };
            var cfg = new ConsequenceConfig { BystanderMultiplier = 0.35, MinAbsoluteDelta = 1 };

            var opinions = new[]
            {
                new OpinionDef { About = "aggrieved", Amount = -4.0 },
                new OpinionDef { About = "patron", Amount = -4.0 }
            };

            var result = ConsequenceResolver.Resolve(evt, observer, facts, opinions, cfg, remainingDailyBudget: 10.0);

            Assert.Equal(2, result.Changes.Count);
            Assert.Equal("hero_aggrieved", result.Changes[0].AboutHeroId);
            Assert.Equal("hero_patron", result.Changes[1].AboutHeroId);
            Assert.Empty(result.Exclusions);
        }

        [Fact]
        public void Resolve_EmptyOpinions_ProducesZeroChangesAndZeroExclusions()
        {
            var (evt, observer, facts, cfg) = CreateTestContext();
            var opinions = Array.Empty<OpinionDef>();

            var result = ConsequenceResolver.Resolve(evt, observer, facts, opinions, cfg, remainingDailyBudget: 6.0);

            Assert.Empty(result.Changes);
            Assert.Empty(result.Exclusions);
        }

        [Fact]
        public void Resolve_EnabledIrrelevantToResolver_PureFunctionCalculatesRegardless()
        {
            var (evt, observer, facts, cfg) = CreateTestContext();
            cfg.Enabled = false; // Resolver 函式本身不管 enabled（由呼叫端 ConsequenceRunner 擋）

            var opinions = new[] { new OpinionDef { About = "target", Amount = -6.0 } };

            var result = ConsequenceResolver.Resolve(evt, observer, facts, opinions, cfg, remainingDailyBudget: 6.0);

            Assert.Single(result.Changes);
            Assert.Empty(result.Exclusions);
        }

        [Fact]
        public void Resolve_PopulatesAllMetadataFieldsCorrectly()
        {
            var (evt, observer, facts, cfg) = CreateTestContext(
                observerHeroId: "lord_obs",
                targetHeroId: "lord_tgt",
                targetRole: "target_role",
                hop: 2,
                observerIsParticipant: false);

            var opinions = new[] { new OpinionDef { About = "target_role", Amount = -8.0 } };

            var result = ConsequenceResolver.Resolve(evt, observer, facts, opinions, cfg, remainingDailyBudget: 6.0);

            var ch = result.Changes[0];
            Assert.Equal("lord_obs", ch.ObserverHeroId);
            Assert.Equal("lord_tgt", ch.AboutHeroId);
            Assert.Equal("target_role", ch.AboutRole);
            Assert.Equal(2, ch.ObserverHop);
            Assert.Equal(0.75, ch.HopConfidence);
            Assert.Equal(0.35, ch.WitnessMultiplier);
            Assert.False(ch.ObserverIsParticipant);
            Assert.Equal("fact_who", ch.SourceFactId);
            Assert.Equal(0.0, ch.AlreadyApplied);
            // full = -8.0 * 0.75 * 0.35 = -2.1
            Assert.Equal(-2.1, ch.FullAmount, 4);
            Assert.Equal(-2.1, ch.Requested, 4);
        }

        // -------------------------------------------------------------
        // Group 3: MinAbsoluteDelta and Idempotence (8+ tests)
        // -------------------------------------------------------------

        [Fact]
        public void Resolve_MagnitudeBelowMinAbsoluteDelta_ExcludedWithBelowMinimum()
        {
            var (evt, observer, facts, cfg) = CreateTestContext(hop: 4); // hop 4 confidence = 0.3
            var opinions = new[] { new OpinionDef { About = "target", Amount = -6.0 } };
            // full = -6.0 * 0.3 * 0.35 = -0.63. | -0.63 | < minAbsoluteDelta(1)

            var result = ConsequenceResolver.Resolve(evt, observer, facts, opinions, cfg, remainingDailyBudget: 6.0);

            Assert.Empty(result.Changes);
            Assert.Single(result.Exclusions);
            Assert.Equal(OpinionSkipReason.BelowMinimum, result.Exclusions[0].Reason);
            Assert.Equal(-0.63, result.Exclusions[0].Amount, 2);
        }

        [Fact]
        public void Resolve_AlreadyFullyApplied_ExcludedWithAlreadyFullyApplied()
        {
            var (evt, observer, facts, cfg) = CreateTestContext(hop: 1);
            observer.RelationImpacts = new List<RelationImpact>
            {
                new RelationImpact
                {
                    AboutHeroId = "hero_target",
                    Requested = -2.1,
                    Delta = -2,
                    Source = GrudgeSource.Rumor
                }
            };
            var opinions = new[] { new OpinionDef { About = "target", Amount = -6.0 } };
            // full = -2.1. already = -2.1. delta = 0.0

            var result = ConsequenceResolver.Resolve(evt, observer, facts, opinions, cfg, remainingDailyBudget: 6.0);

            Assert.Empty(result.Changes);
            Assert.Single(result.Exclusions);
            Assert.Equal(OpinionSkipReason.AlreadyFullyApplied, result.Exclusions[0].Reason);
        }

        [Fact]
        public void Resolve_RehearUpgradeHop3ToHop1_AppliesPartialDelta()
        {
            var (evt, observer, facts, cfg) = CreateTestContext(hop: 1);
            // 先前在 hop 3 已記過 -1.05
            observer.RelationImpacts = new List<RelationImpact>
            {
                new RelationImpact
                {
                    AboutHeroId = "hero_target",
                    Requested = -1.05,
                    Delta = -1,
                    Source = GrudgeSource.Rumor
                }
            };

            var opinions = new[] { new OpinionDef { About = "target", Amount = -6.0 } };
            // 在 hop 1：full = -6.0 * 1.0 * 0.35 = -2.10
            // already = -1.05
            // delta = -2.10 - (-1.05) = -1.05
            // | -1.05 | >= min(1) => 施加差額 -1.05

            var result = ConsequenceResolver.Resolve(evt, observer, facts, opinions, cfg, remainingDailyBudget: 6.0);

            Assert.Empty(result.Exclusions);
            Assert.Single(result.Changes);
            var ch = result.Changes[0];
            Assert.Equal(-1.05, ch.Requested, 4);
            Assert.Equal(-2.10, ch.FullAmount, 4);
            Assert.Equal(-1.05, ch.AlreadyApplied, 4);
        }

        [Fact]
        public void Resolve_AlreadyIgnoresSituationSource()
        {
            var (evt, observer, facts, cfg) = CreateTestContext(hop: 1);
            // 同一個人身上有情境當場結下的個人恩怨 -5.0
            observer.RelationImpacts = new List<RelationImpact>
            {
                new RelationImpact
                {
                    AboutHeroId = "hero_target",
                    Requested = -5.0,
                    Delta = -5,
                    Source = GrudgeSource.Situation
                }
            };

            var opinions = new[] { new OpinionDef { About = "target", Amount = -6.0 } };
            // full = -2.1. already 只算 Rumor => already = 0.0. delta = -2.1

            var result = ConsequenceResolver.Resolve(evt, observer, facts, opinions, cfg, remainingDailyBudget: 6.0);

            Assert.Empty(result.Exclusions);
            Assert.Single(result.Changes);
            Assert.Equal(-2.1, result.Changes[0].Requested, 4);
            Assert.Equal(0.0, result.Changes[0].AlreadyApplied);
        }

        [Fact]
        public void Resolve_NullRelationImpacts_TreatsAlreadyAsZero()
        {
            var (evt, observer, facts, cfg) = CreateTestContext(hop: 1);
            observer.RelationImpacts = null;

            var opinions = new[] { new OpinionDef { About = "target", Amount = -6.0 } };

            var result = ConsequenceResolver.Resolve(evt, observer, facts, opinions, cfg, remainingDailyBudget: 6.0);

            Assert.Single(result.Changes);
            Assert.Equal(0.0, result.Changes[0].AlreadyApplied);
        }

        [Fact]
        public void Resolve_DeltaBelowMinAbsoluteDelta_ExcludedWithAlreadyFullyApplied()
        {
            var (evt, observer, facts, cfg) = CreateTestContext(hop: 1);
            observer.RelationImpacts = new List<RelationImpact>
            {
                new RelationImpact
                {
                    AboutHeroId = "hero_target",
                    Requested = -1.6,
                    Source = GrudgeSource.Rumor
                }
            };
            var opinions = new[] { new OpinionDef { About = "target", Amount = -6.0 } };
            // full = -2.1. already = -1.6. delta = -0.5. |delta| < 1 => already != 0 => AlreadyFullyApplied

            var result = ConsequenceResolver.Resolve(evt, observer, facts, opinions, cfg, remainingDailyBudget: 6.0);

            Assert.Empty(result.Changes);
            Assert.Single(result.Exclusions);
            Assert.Equal(OpinionSkipReason.AlreadyFullyApplied, result.Exclusions[0].Reason);
        }

        [Fact]
        public void Resolve_DeltaOppositeSign_CalculatesDifference()
        {
            var (evt, observer, facts, cfg) = CreateTestContext(hop: 1);
            // 假設先前量較大 (-5.0)，現在滿額為 -2.0
            observer.RelationImpacts = new List<RelationImpact>
            {
                new RelationImpact
                {
                    AboutHeroId = "hero_target",
                    Requested = -5.0,
                    Source = GrudgeSource.Rumor
                }
            };
            var opinions = new[] { new OpinionDef { About = "target", Amount = -2.0 / 0.35 } }; // full = -2.0
            // delta = -2.0 - (-5.0) = +3.0

            var result = ConsequenceResolver.Resolve(evt, observer, facts, opinions, cfg, remainingDailyBudget: 6.0);

            Assert.Single(result.Changes);
            Assert.Equal(3.0, result.Changes[0].Requested, 4);
        }

        [Fact]
        public void Resolve_FirstCallBelowMin_AlreadyIsZero_ReportsBelowMinimum()
        {
            var (evt, observer, facts, cfg) = CreateTestContext(hop: 3);
            var opinions = new[] { new OpinionDef { About = "target", Amount = -2.0 } };
            // full = -2.0 * 0.5 * 0.35 = -0.35. | -0.35 | < 1. already = 0 => BelowMinimum

            var result = ConsequenceResolver.Resolve(evt, observer, facts, opinions, cfg, remainingDailyBudget: 6.0);

            Assert.Empty(result.Changes);
            Assert.Single(result.Exclusions);
            Assert.Equal(OpinionSkipReason.BelowMinimum, result.Exclusions[0].Reason);
        }

        // -------------------------------------------------------------
        // Group 4: DailyRelationBudget (8+ tests)
        // -------------------------------------------------------------

        [Fact]
        public void Budget_InitialRemaining_EqualsCap()
        {
            var budget = new DailyRelationBudget();
            Assert.Equal(6.0, budget.Remaining("hero1", 6.0));
            Assert.Equal(10.0, budget.Remaining("hero2", 10.0));
        }

        [Fact]
        public void Budget_Consume_DecreasesRemaining()
        {
            var budget = new DailyRelationBudget();
            budget.Consume("hero1", 2.5);
            Assert.Equal(3.5, budget.Remaining("hero1", 6.0));

            // 負數自動取絕對值
            budget.Consume("hero1", -1.5);
            Assert.Equal(2.0, budget.Remaining("hero1", 6.0));
        }

        [Fact]
        public void Budget_AdvanceDay_ResetsTrackedHeroes()
        {
            var budget = new DailyRelationBudget();
            budget.Advance(1.2);
            budget.Consume("hero1", 4.0);
            Assert.Equal(2.0, budget.Remaining("hero1", 6.0));
            Assert.Equal(1, budget.TrackedHeroes);

            // 跨日（天桶 1 -> 2）
            budget.Advance(2.1);
            Assert.Equal(6.0, budget.Remaining("hero1", 6.0));
            Assert.Equal(0, budget.TrackedHeroes);
        }

        [Fact]
        public void Budget_RewindDay_ResetsTrackedHeroes()
        {
            var budget = new DailyRelationBudget();
            budget.Advance(10.5);
            budget.Consume("hero1", 4.0);

            // 讀舊檔，天數倒退
            budget.Advance(5.0);
            Assert.Equal(6.0, budget.Remaining("hero1", 6.0));
            Assert.Equal(0, budget.TrackedHeroes);
        }

        [Fact]
        public void Budget_PerHeroIsolation_HeroesDoNotAffectEachOther()
        {
            var budget = new DailyRelationBudget();
            budget.Consume("hero1", 5.0);

            Assert.Equal(1.0, budget.Remaining("hero1", 6.0));
            Assert.Equal(6.0, budget.Remaining("hero2", 6.0));
            Assert.Equal(1, budget.TrackedHeroes);
        }

        [Fact]
        public void Budget_RemainingInsufficient_DeltaClampedInResolve()
        {
            var (evt, observer, facts, cfg) = CreateTestContext(hop: 0, observerIsParticipant: true);
            var opinions = new[] { new OpinionDef { About = "target", Amount = -10.0 } };
            // remainingDailyBudget 只有 2.5 => delta -10.0 被夾成 -2.5

            var result = ConsequenceResolver.Resolve(evt, observer, facts, opinions, cfg, remainingDailyBudget: 2.5);

            Assert.Single(result.Changes);
            Assert.Equal(-2.5, result.Changes[0].Requested);
            Assert.Equal(-10.0, result.Changes[0].FullAmount);
        }

        [Fact]
        public void Budget_ClampedDeltaBelowMinAbsoluteDelta_ExcludedWithDailyBudgetExhausted()
        {
            var (evt, observer, facts, cfg) = CreateTestContext(hop: 0, observerIsParticipant: true);
            var opinions = new[] { new OpinionDef { About = "target", Amount = -10.0 } };
            // remainingDailyBudget 只有 0.5 => 夾成 -0.5 => | -0.5 | < minAbsoluteDelta(1) => 排除 DailyBudgetExhausted

            var result = ConsequenceResolver.Resolve(evt, observer, facts, opinions, cfg, remainingDailyBudget: 0.5);

            Assert.Empty(result.Changes);
            Assert.Single(result.Exclusions);
            Assert.Equal(OpinionSkipReason.DailyBudgetExhausted, result.Exclusions[0].Reason);
        }

        [Fact]
        public void Budget_CapZero_DailyBudgetExhausted()
        {
            var (evt, observer, facts, cfg) = CreateTestContext();
            var opinions = new[] { new OpinionDef { About = "target", Amount = -5.0 } };

            var result = ConsequenceResolver.Resolve(evt, observer, facts, opinions, cfg, remainingDailyBudget: 0.0);

            Assert.Empty(result.Changes);
            Assert.Single(result.Exclusions);
            Assert.Equal(OpinionSkipReason.DailyBudgetExhausted, result.Exclusions[0].Reason);
        }

        // -------------------------------------------------------------
        // Group 5: Catalog Template Validation (6+ tests)
        // -------------------------------------------------------------

        private static EventCatalog LoadCatalogFromJson(string json)
        {
            return EventCatalogLoader.Load(json, new PersistenceConfig { MaxFactsPerEvent = 24 });
        }

        [Fact]
        public void CatalogValidation_InvalidAboutRole_ProducesErrorIssue()
        {
            string json = @"{
                ""templates"": [
                    {
                        ""type"": ""test_invalid_role"",
                        ""origin"": ""Public"",
                        ""drama"": 3,
                        ""roles"": { ""actor"": ""{ACTOR}"" },
                        ""facts"": [ { ""id"": ""f1"", ""category"": ""WHO"", ""text"": ""T"" } ],
                        ""opinion"": [ { ""about"": ""not_a_role"", ""amount"": -5.0 } ]
                    }
                ]
            }";

            var catalog = LoadCatalogFromJson(json);
            Assert.Contains(catalog.Issues, i => i.Code == CatalogIssueCode.OpinionInvalid && i.IsError);
            Assert.Null(catalog.ByType("test_invalid_role"));
        }

        [Fact]
        public void CatalogValidation_AmountZero_ProducesErrorIssue()
        {
            string json = @"{
                ""templates"": [
                    {
                        ""type"": ""test_zero_amount"",
                        ""origin"": ""Public"",
                        ""drama"": 3,
                        ""roles"": { ""actor"": ""{ACTOR}"" },
                        ""facts"": [ { ""id"": ""f1"", ""category"": ""WHO"", ""text"": ""T"" } ],
                        ""opinion"": [ { ""about"": ""actor"", ""amount"": 0.0 } ]
                    }
                ]
            }";

            var catalog = LoadCatalogFromJson(json);
            Assert.Contains(catalog.Issues, i => i.Code == CatalogIssueCode.OpinionInvalid && i.IsError);
        }

        [Fact]
        public void CatalogValidation_AmountNaN_ProducesErrorIssue()
        {
            string json = @"{
                ""templates"": [
                    {
                        ""type"": ""test_nan"",
                        ""origin"": ""Public"",
                        ""drama"": 3,
                        ""roles"": { ""actor"": ""{ACTOR}"" },
                        ""facts"": [ { ""id"": ""f1"", ""category"": ""WHO"", ""text"": ""T"" } ],
                        ""opinion"": [ { ""about"": ""actor"", ""amount"": ""NaN"" } ]
                    }
                ]
            }";

            var catalog = LoadCatalogFromJson(json);
            Assert.Contains(catalog.Issues, i => i.Code == CatalogIssueCode.OpinionInvalid && i.IsError);
        }

        [Fact]
        public void CatalogValidation_AmountInfinity_ProducesErrorIssue()
        {
            string json = @"{
                ""templates"": [
                    {
                        ""type"": ""test_inf"",
                        ""origin"": ""Public"",
                        ""drama"": 3,
                        ""roles"": { ""actor"": ""{ACTOR}"" },
                        ""facts"": [ { ""id"": ""f1"", ""category"": ""WHO"", ""text"": ""T"" } ],
                        ""opinion"": [ { ""about"": ""actor"", ""amount"": ""Infinity"" } ]
                    }
                ]
            }";

            var catalog = LoadCatalogFromJson(json);
            Assert.Contains(catalog.Issues, i => i.Code == CatalogIssueCode.OpinionInvalid && i.IsError);
        }

        [Fact]
        public void CatalogValidation_DuplicateAbout_ProducesErrorIssue()
        {
            string json = @"{
                ""templates"": [
                    {
                        ""type"": ""test_dup_about"",
                        ""origin"": ""Public"",
                        ""drama"": 3,
                        ""roles"": { ""actor"": ""{ACTOR}"" },
                        ""facts"": [ { ""id"": ""f1"", ""category"": ""WHO"", ""text"": ""T"" } ],
                        ""opinion"": [
                            { ""about"": ""actor"", ""amount"": -3.0 },
                            { ""about"": ""actor"", ""amount"": -5.0 }
                        ]
                    }
                ]
            }";

            var catalog = LoadCatalogFromJson(json);
            Assert.Contains(catalog.Issues, i => i.Code == CatalogIssueCode.OpinionInvalid && i.IsError);
        }

        [Fact]
        public void CatalogValidation_UnknownFieldsInOpinion_ProducesWarning()
        {
            string json = @"{
                ""templates"": [
                    {
                        ""type"": ""test_unknown_opinion_fields"",
                        ""origin"": ""Public"",
                        ""drama"": 3,
                        ""roles"": { ""actor"": ""{ACTOR}"" },
                        ""facts"": [ { ""id"": ""f1"", ""category"": ""WHO"", ""text"": ""T"" } ],
                        ""opinion"": [
                            { ""about"": ""actor"", ""amount"": -4.0, ""futureCustomKey"": 123 }
                        ]
                    }
                ]
            }";

            var catalog = LoadCatalogFromJson(json);
            Assert.Contains(catalog.Issues, i => i.Code == CatalogIssueCode.OpinionInvalid && !i.IsError);
            Assert.NotNull(catalog.ByType("test_unknown_opinion_fields"));
        }

        // -------------------------------------------------------------
        // Group 6: Log Formatters (5+ tests)
        // -------------------------------------------------------------

        [Fact]
        public void GrudgeLogFormatter_FormatOpinionAppliedNative_MatchesExactPattern()
        {
            string log = GrudgeLogFormatter.FormatOpinionAppliedNative(
                observer: "lord_obs",
                aboutHeroId: "lord_tgt",
                aboutRole: "killer",
                eventId: "evt_100",
                amount: -10.0,
                hop: 0,
                hopConfidence: 1.0,
                isWitness: true,
                multiplier: 1.0,
                fullAmount: -10.0,
                alreadyApplied: 0.0,
                requested: -10.0,
                nativeBefore: 20,
                nativeAfter: 10,
                nativeDelta: -10);

            Assert.Equal(
                "opinion applied lord_obs -> lord_tgt (killer) on evt_100: template -10.00, hop 0 x1.00, participant x1.00 => full -10.00, already 0.00, requested -10.00, native 20 -> 10 (-10)",
                log);
        }

        [Fact]
        public void GrudgeLogFormatter_FormatOpinionAppliedLedgerOnly_MatchesExactPattern()
        {
            string log = GrudgeLogFormatter.FormatOpinionAppliedLedgerOnly(
                observer: "lord_obs",
                aboutHeroId: "lord_tgt",
                aboutRole: "claimant",
                eventId: "evt_101",
                amount: -6.0,
                hop: 1,
                hopConfidence: 1.0,
                isWitness: false,
                multiplier: 0.35,
                fullAmount: -2.1,
                alreadyApplied: 0.0,
                requested: -2.1);

            Assert.Equal(
                "opinion ledger-only lord_obs -> lord_tgt (claimant) on evt_101: template -6.00, hop 1 x1.00, onlooker x0.35 => full -2.10, already 0.00, requested -2.10",
                log);
        }

        [Theory]
        [InlineData(OpinionSkipReason.NotNamedByAnyFact, "opinion skipped lord_obs -> target on evt_102: no retained fragment names lord_tgt")]
        [InlineData(OpinionSkipReason.AboutIsObserver, "opinion skipped lord_obs -> target on evt_102: target is the observer self")]
        [InlineData(OpinionSkipReason.AboutNotBound, "opinion skipped lord_obs -> target on evt_102: target role target is not bound in event")]
        [InlineData(OpinionSkipReason.BelowMinimum, "opinion skipped lord_obs -> target on evt_102: calculated amount 0.63 is below minimum threshold")]
        [InlineData(OpinionSkipReason.DailyBudgetExhausted, "opinion skipped lord_obs -> target on evt_102: daily relation shift budget exhausted")]
        [InlineData(OpinionSkipReason.AlreadyFullyApplied, "opinion skipped lord_obs -> target on evt_102: shift has already been fully applied")]
        public void GrudgeLogFormatter_FormatOpinionSkipped_AllSixReasons_MatchExactPattern(OpinionSkipReason reason, string expected)
        {
            string reasonStr = GrudgeLogFormatter.FormatOpinionSkipReason(reason, "lord_tgt", "target", 0.63);
            string log = GrudgeLogFormatter.FormatOpinionSkipped(
                observer: "lord_obs",
                aboutRole: "target",
                eventId: "evt_102",
                reason: reasonStr);

            Assert.Equal(expected, log);
        }

        [Fact]
        public void GrudgeLogFormatter_FormatOpinionBudget_MatchesExactPattern()
        {
            string log = GrudgeLogFormatter.FormatOpinionBudget("lord_obs", 42.5, 4.50, 6.0, 1);
            Assert.Equal("opinion budget lord_obs on day 42: 4.50 of 6.00 used, 1 change(s) clamped", log);
        }

        [Fact]
        public void GrudgeLogFormatter_FormatOpinionsDisabled_MatchesExactPattern()
        {
            string log = GrudgeLogFormatter.FormatOpinionsDisabled(5);
            Assert.Equal("opinions disabled: 5 hearer(s) skipped this tick", log);
        }

        // -------------------------------------------------------------
        // Group 7: Actual Catalog Content Validation (3+ tests)
        // -------------------------------------------------------------

        private static string FindRepoFile(string relativePath)
        {
            string? current = AppContext.BaseDirectory;
            while (!string.IsNullOrEmpty(current))
            {
                if (File.Exists(Path.Combine(current, "VividWorld.sln")))
                {
                    string path = Path.Combine(current, relativePath);
                    if (File.Exists(path)) return path;
                }
                var parent = Directory.GetParent(current);
                if (parent == null) break;
                current = parent.FullName;
            }
            throw new FileNotFoundException($"Could not find {relativePath} relative to {AppContext.BaseDirectory}");
        }

        [Fact]
        public void RealCatalogs_LoadWithZeroErrors()
        {
            string eventsPath = FindRepoFile(Path.Combine("module", "ModuleData", "vividworld_events.json"));
            string situationEventsPath = FindRepoFile(Path.Combine("module", "ModuleData", "vividworld_situation_events.json"));

            var pCfg = new PersistenceConfig { MaxFactsPerEvent = 24 };

            var catEvents = EventCatalogLoader.Load(File.ReadAllText(eventsPath), pCfg);
            Assert.DoesNotContain(catEvents.Issues, i => i.IsError);

            var catSitEvents = EventCatalogLoader.Load(File.ReadAllText(situationEventsPath), pCfg);
            Assert.DoesNotContain(catSitEvents.Issues, i => i.IsError);
        }

        [Fact]
        public void RealCatalogs_OpinionDeclarations_MatchCardSection5Table()
        {
            string eventsPath = FindRepoFile(Path.Combine("module", "ModuleData", "vividworld_events.json"));
            string situationEventsPath = FindRepoFile(Path.Combine("module", "ModuleData", "vividworld_situation_events.json"));
            var pCfg = new PersistenceConfig { MaxFactsPerEvent = 24 };

            var catEvents = EventCatalogLoader.Load(File.ReadAllText(eventsPath), pCfg);
            var catSitEvents = EventCatalogLoader.Load(File.ReadAllText(situationEventsPath), pCfg);

            // vividworld_events.json: hero_murdered and hero_executed
            var murdered = catEvents.ByType("hero_murdered");
            Assert.NotNull(murdered);
            Assert.NotNull(murdered!.Opinions);
            Assert.Single(murdered.Opinions!);
            Assert.Equal("killer", murdered.Opinions![0].About);
            Assert.Equal(-10.0, murdered.Opinions[0].Amount);

            var executed = catEvents.ByType("hero_executed");
            Assert.NotNull(executed);
            Assert.NotNull(executed!.Opinions);
            Assert.Single(executed.Opinions!);
            Assert.Equal("killer", executed.Opinions![0].About);
            Assert.Equal(-7.0, executed.Opinions[0].Amount);

            // vividworld_situation_events.json (18 templates per Card §5.2)
            var expectedSitOpinions = new Dictionary<string, (string about, double amount)[]>
            {
                ["seat_dispute_demanded"] = new[] { ("slighted", -2.0) },
                ["seat_dispute_endured"] = new[] { ("slighted", 2.0) },
                ["seat_dispute_yielded"] = new[] { ("slighted", 4.0) },
                ["seat_dispute_walked_out"] = new[] { ("slighted", -5.0) },
                ["advice_given_freely"] = new[] { ("veteran", 5.0) },
                ["advice_brushed_off"] = new[] { ("veteran", -2.0) },
                ["advice_mocked"] = new[] { ("veteran", -6.0) },
                ["tavern_boast_told"] = new[] { ("speaker", -3.0) },
                ["tavern_confidence"] = new[] { ("speaker", -4.0) },
                ["tavern_sour_words"] = new[] { ("speaker", -5.0) },
                ["victory_credit_claimed"] = new[] { ("claimant", -6.0) },
                ["victory_credit_deferred"] = new[] { ("claimant", 6.0) },
                ["victory_credit_belittled"] = new[] { ("claimant", -5.0) },
                ["victory_credit_judged"] = new[] { ("host", 3.0) },
                ["brawl_man_handed_over"] = new[] { ("patron", 5.0) },
                ["brawl_shielded_own"] = new[] { ("patron", -4.0) },
                ["brawl_counter_accused"] = new[] { ("patron", -6.0) },
                ["brawl_hushed_up"] = new[] { ("aggrieved", -2.0), ("patron", -2.0) },
                ["wager_refused"] = new[] { ("challenged", -3.0) }
            };

            foreach (var kvp in expectedSitOpinions)
            {
                var tmpl = catSitEvents.ByType(kvp.Key);
                Assert.NotNull(tmpl);
                Assert.NotNull(tmpl!.Opinions);
                Assert.Equal(kvp.Value.Length, tmpl.Opinions!.Count);
                for (int i = 0; i < kvp.Value.Length; i++)
                {
                    Assert.Equal(kvp.Value[i].about, tmpl.Opinions[i].About);
                    Assert.Equal(kvp.Value[i].amount, tmpl.Opinions[i].Amount);
                }
            }
        }

        [Fact]
        public void RealCatalogs_NonOpinionatedTemplates_HaveNullOrEmptyOpinions()
        {
            string eventsPath = FindRepoFile(Path.Combine("module", "ModuleData", "vividworld_events.json"));
            string situationEventsPath = FindRepoFile(Path.Combine("module", "ModuleData", "vividworld_situation_events.json"));
            var pCfg = new PersistenceConfig { MaxFactsPerEvent = 24 };

            var catEvents = EventCatalogLoader.Load(File.ReadAllText(eventsPath), pCfg);
            var catSitEvents = EventCatalogLoader.Load(File.ReadAllText(situationEventsPath), pCfg);

            var unopinionatedEvents = new[]
            {
                "hero_died_in_battle",
                "hero_died_naturally",
                "hero_taken_prisoner",
                "hero_released",
                "hero_escaped_captivity",
                "heroes_married",
                "child_born"
            };

            foreach (string type in unopinionatedEvents)
            {
                var tmpl = catEvents.ByType(type);
                Assert.NotNull(tmpl);
                Assert.True(tmpl!.Opinions == null || tmpl.Opinions.Count == 0,
                    $"Template {type} in vividworld_events.json must not have opinion declared.");
            }

            var unopinionatedSituationEvents = new[]
            {
                "wager_struck",
                "wager_secret_stake"
            };

            foreach (string type in unopinionatedSituationEvents)
            {
                var tmpl = catSitEvents.ByType(type);
                Assert.NotNull(tmpl);
                Assert.True(tmpl!.Opinions == null || tmpl.Opinions.Count == 0,
                    $"Template {type} in vividworld_situation_events.json must not have opinion declared.");
            }
        }
    }
}
