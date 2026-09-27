#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using VividWorld.Core.Config;
using VividWorld.Core.Events;
using VividWorld.Core.Memory;
using VividWorld.Core.Rumors;
using Xunit;

namespace VividWorld.Core.Tests
{
    public class NpcRecallQueryTests
    {
        private static WorldEvent CreateEvent(
            string eventId,
            double day = 1.0,
            EventOrigin origin = EventOrigin.Public,
            bool leaked = true,
            string? linkedEventId = null,
            params (string heroId, double learnedDay, double? interest, double? forgetDay, double? outdatedDay, List<string>? knownFactIds)[] knowers)
        {
            var evt = new WorldEvent
            {
                EventId = eventId,
                Type = "hero_killed",
                Day = day,
                Origin = origin,
                LinkedEventId = linkedEventId,
                State = new RumorState { Leaked = leaked },
                Facts = new List<Fact>
                {
                    new Fact { Id = "f1", Category = FactCategory.Who, TextId = "VividWorld_Fact_HeroKilled_Who", Text = "Who" },
                    new Fact { Id = "f2", Category = FactCategory.Where, TextId = "VividWorld_Fact_HeroKilled_Where", Text = "Where" },
                    new Fact { Id = "f3", Category = FactCategory.What, TextId = "VividWorld_Fact_HeroKilled_What", Text = "What" },
                },
                KnownBy = new List<KnownByEntry>()
            };

            foreach (var k in knowers)
            {
                evt.KnownBy.Add(new KnownByEntry
                {
                    HeroId = k.heroId,
                    Hop = 1,
                    LearnedDay = k.learnedDay,
                    Interest = k.interest,
                    ForgetDay = k.forgetDay,
                    OutdatedDay = k.outdatedDay,
                    KnownFactIds = k.knownFactIds
                });
            }

            return evt;
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        public void Query_NullOrEmptyHeroId_ReturnsEmptyResult(string? heroId)
        {
            var evt = CreateEvent("evt_1", knowers: ("hero_1", 1.0, 0.5, null, null, null));
            var result = NpcRecallQuery.Query(heroId!, 1.0, 10, new[] { evt });

            Assert.Empty(result.Items);
            Assert.Empty(result.Exclusions);
            Assert.Equal(0, result.CandidateCount);
        }

        [Fact]
        public void Query_NullCandidates_ReturnsEmptyResult()
        {
            var result = NpcRecallQuery.Query("hero_1", 1.0, 10, null);

            Assert.Empty(result.Items);
            Assert.Empty(result.Exclusions);
            Assert.Equal(0, result.CandidateCount);
        }

        [Fact]
        public void Exclusion_NotVisible_WhenSecretNotLeaked_IsExcluded()
        {
            var unleakedSecret = CreateEvent("secret_1", origin: EventOrigin.Secret, leaked: false,
                knowers: ("hero_1", 1.0, 0.8, null, null, null));
            var leakedSecret = CreateEvent("secret_2", origin: EventOrigin.Secret, leaked: true,
                knowers: ("hero_1", 1.0, 0.8, null, null, null));
            var publicEvent = CreateEvent("public_1", origin: EventOrigin.Public, leaked: false,
                knowers: ("hero_1", 1.0, 0.8, null, null, null));

            var result = NpcRecallQuery.Query("hero_1", 1.0, 10, new[] { unleakedSecret, leakedSecret, publicEvent });

            Assert.Equal(2, result.Items.Count);
            Assert.Contains(result.Items, m => m.EventId == "secret_2");
            Assert.Contains(result.Items, m => m.EventId == "public_1");

            Assert.Single(result.Exclusions);
            var exclusion = result.Exclusions[0];
            Assert.Equal("secret_1", exclusion.EventId);
            Assert.Equal(RecallExclusionReason.NotVisible, exclusion.Reason);
            Assert.Equal(1, result.ExclusionCountByReason(RecallExclusionReason.NotVisible));
        }

        [Fact]
        public void Exclusion_NotKnown_WhenHeroHasNoEntry_IsExcluded()
        {
            var evt = CreateEvent("evt_1", knowers: ("other_hero", 1.0, 0.5, null, null, null));

            var result = NpcRecallQuery.Query("hero_1", 1.0, 10, new[] { evt });

            Assert.Empty(result.Items);
            Assert.Single(result.Exclusions);
            Assert.Equal("evt_1", result.Exclusions[0].EventId);
            Assert.Equal(RecallExclusionReason.NotKnown, result.Exclusions[0].Reason);
        }

        [Fact]
        public void Exclusion_Forgotten_WhenForgetDayEarlierOrEqualToCurrentDay_IsExcluded()
        {
            // ForgetDay <= currentDay is forgotten; ForgetDay > currentDay or null is remembered
            var forgottenEqual = CreateEvent("evt_forgot_eq",
                knowers: ("hero_1", 1.0, 0.5, forgetDay: 10.0, null, null));
            var forgottenPast = CreateEvent("evt_forgot_past",
                knowers: ("hero_1", 1.0, 0.5, forgetDay: 8.0, null, null));
            var rememberedFuture = CreateEvent("evt_remembered_future",
                knowers: ("hero_1", 1.0, 0.5, forgetDay: 10.1, null, null));
            var rememberedIndefinite = CreateEvent("evt_remembered_null",
                knowers: ("hero_1", 1.0, 0.5, forgetDay: null, null, null));

            var result = NpcRecallQuery.Query("hero_1", currentDay: 10.0, maxCount: 10,
                new[] { forgottenEqual, forgottenPast, rememberedFuture, rememberedIndefinite });

            Assert.Equal(2, result.Items.Count);
            Assert.Contains(result.Items, m => m.EventId == "evt_remembered_future");
            Assert.Contains(result.Items, m => m.EventId == "evt_remembered_null");

            Assert.Equal(2, result.Exclusions.Count);
            Assert.All(result.Exclusions, e => Assert.Equal(RecallExclusionReason.Forgotten, e.Reason));
            Assert.Contains(result.Exclusions, e => e.EventId == "evt_forgot_eq");
            Assert.Contains(result.Exclusions, e => e.EventId == "evt_forgot_past");
        }

        [Fact]
        public void Exclusion_Outdated_WhenOutdatedDayIsNotNull_IsExcluded()
        {
            var outdatedEvt = CreateEvent("evt_outdated",
                knowers: ("hero_1", 1.0, 0.5, null, outdatedDay: 5.0, null));
            var freshEvt = CreateEvent("evt_fresh",
                knowers: ("hero_1", 1.0, 0.5, null, outdatedDay: null, null));

            var result = NpcRecallQuery.Query("hero_1", currentDay: 6.0, maxCount: 10,
                new[] { outdatedEvt, freshEvt });

            Assert.Single(result.Items);
            Assert.Equal("evt_fresh", result.Items[0].EventId);

            Assert.Single(result.Exclusions);
            Assert.Equal("evt_outdated", result.Exclusions[0].EventId);
            Assert.Equal(RecallExclusionReason.Outdated, result.Exclusions[0].Reason);
        }

        [Fact]
        public void Exclusion_NoFacts_WhenNoFactsRetained_IsExcluded()
        {
            var noFactsEvt = CreateEvent("evt_nofacts",
                knowers: ("hero_1", 1.0, 0.5, null, null, knownFactIds: new List<string>())); // empty fact list
            var hasFactsEvt = CreateEvent("evt_hasfacts",
                knowers: ("hero_1", 1.0, 0.5, null, null, knownFactIds: new List<string> { "f1" }));

            var result = NpcRecallQuery.Query("hero_1", currentDay: 1.0, maxCount: 10,
                new[] { noFactsEvt, hasFactsEvt });

            Assert.Single(result.Items);
            Assert.Equal("evt_hasfacts", result.Items[0].EventId);
            Assert.Single(result.Items[0].Facts);
            Assert.Equal("f1", result.Items[0].Facts[0].Id);

            Assert.Single(result.Exclusions);
            Assert.Equal("evt_nofacts", result.Exclusions[0].EventId);
            Assert.Equal(RecallExclusionReason.NoFacts, result.Exclusions[0].Reason);
        }

        [Fact]
        public void Sorting_IsCorrection_PrioritizedOverHigherInterest()
        {
            // Event A is a correction (linked event is known by hero_1) with low interest (0.1)
            // Event B is not a correction with high interest (0.9)
            var rootEvt = CreateEvent("evt_root", knowers: ("hero_1", 1.0, 0.5, null, null, null));
            var correctionEvt = CreateEvent("evt_correction", linkedEventId: "evt_root",
                knowers: ("hero_1", 3.0, 0.1, null, null, null));
            var highInterestEvt = CreateEvent("evt_high_interest",
                knowers: ("hero_1", 3.0, 0.9, null, null, null));

            var result = NpcRecallQuery.Query(
                "hero_1",
                currentDay: 5.0,
                maxCount: 10,
                new[] { highInterestEvt, correctionEvt },
                isEventKnown: id => id == "evt_root");

            Assert.Equal(2, result.Items.Count);
            Assert.True(result.Items[0].IsCorrection);
            Assert.Equal("evt_correction", result.Items[0].EventId);
            Assert.False(result.Items[1].IsCorrection);
            Assert.Equal("evt_high_interest", result.Items[1].EventId);
        }

        [Fact]
        public void Sorting_Interest_PrioritizedOverLearnedDay()
        {
            // Both non-correction. Higher interest comes first even if learned earlier.
            var highInterestOld = CreateEvent("evt_high_old",
                knowers: ("hero_1", learnedDay: 2.0, interest: 0.85, null, null, null));
            var lowInterestRecent = CreateEvent("evt_low_recent",
                knowers: ("hero_1", learnedDay: 8.0, interest: 0.20, null, null, null));
            var nullInterest = CreateEvent("evt_null_interest",
                knowers: ("hero_1", learnedDay: 9.0, interest: null, null, null, null));

            var result = NpcRecallQuery.Query("hero_1", currentDay: 10.0, maxCount: 10,
                new[] { lowInterestRecent, nullInterest, highInterestOld });

            Assert.Equal(3, result.Items.Count);
            Assert.Equal("evt_high_old", result.Items[0].EventId);
            Assert.Equal("evt_low_recent", result.Items[1].EventId);
            Assert.Equal("evt_null_interest", result.Items[2].EventId);
        }

        [Fact]
        public void Sorting_LearnedDay_PrioritizedOverEventId()
        {
            // Both same interest (0.5). More recent learnedDay comes first.
            var learnedOlder = CreateEvent("a_older",
                knowers: ("hero_1", learnedDay: 3.0, interest: 0.5, null, null, null));
            var learnedNewer = CreateEvent("z_newer",
                knowers: ("hero_1", learnedDay: 7.0, interest: 0.5, null, null, null));

            var result = NpcRecallQuery.Query("hero_1", currentDay: 10.0, maxCount: 10,
                new[] { learnedOlder, learnedNewer });

            Assert.Equal(2, result.Items.Count);
            Assert.Equal("z_newer", result.Items[0].EventId);
            Assert.Equal("a_older", result.Items[1].EventId);
        }

        [Fact]
        public void Sorting_EventId_AscendingStableTieBreaker()
        {
            // Same correction, same interest, same learnedDay.
            var evtB = CreateEvent("evt_bravo",
                knowers: ("hero_1", learnedDay: 5.0, interest: 0.5, null, null, null));
            var evtA = CreateEvent("evt_alpha",
                knowers: ("hero_1", learnedDay: 5.0, interest: 0.5, null, null, null));

            var result = NpcRecallQuery.Query("hero_1", currentDay: 10.0, maxCount: 10,
                new[] { evtB, evtA });

            Assert.Equal(2, result.Items.Count);
            Assert.Equal("evt_alpha", result.Items[0].EventId);
            Assert.Equal("evt_bravo", result.Items[1].EventId);
        }

        [Fact]
        public void MaxCount_LimitsReturnedItems_WhilePreservingExclusionsAndCandidateCount()
        {
            var e1 = CreateEvent("e1", knowers: ("hero_1", 1.0, 0.9, null, null, null));
            var e2 = CreateEvent("e2", knowers: ("hero_1", 2.0, 0.8, null, null, null));
            var e3 = CreateEvent("e3", knowers: ("hero_1", 3.0, 0.7, null, null, null));
            var eOutdated = CreateEvent("e_outdated", knowers: ("hero_1", 1.0, 0.9, null, 2.0, null));

            var result = NpcRecallQuery.Query("hero_1", currentDay: 5.0, maxCount: 2,
                new[] { e1, e2, e3, eOutdated });

            Assert.Equal(2, result.Items.Count);
            Assert.Equal("e1", result.Items[0].EventId);
            Assert.Equal("e2", result.Items[1].EventId);
            Assert.Equal(4, result.CandidateCount);
            Assert.Single(result.Exclusions);
            Assert.Equal("e_outdated", result.Exclusions[0].EventId);

            var zeroResult = NpcRecallQuery.Query("hero_1", currentDay: 5.0, maxCount: 0,
                new[] { e1, e2, e3 });
            Assert.Empty(zeroResult.Items);
            Assert.Equal(3, zeroResult.CandidateCount);
        }

        [Fact]
        public void KnownFactIds_WhenSpecified_OverridesRetentionPolicy()
        {
            // Event has facts f1, f2, f3. KnownFactIds specifies only f2.
            var evt = CreateEvent("evt_custom_facts",
                knowers: ("hero_1", 1.0, 0.5, null, null, new List<string> { "f2" }));

            var result = NpcRecallQuery.Query("hero_1", 1.0, 10, new[] { evt });

            Assert.Single(result.Items);
            Assert.Single(result.Items[0].Facts);
            Assert.Equal("f2", result.Items[0].Facts[0].Id);
        }

        private sealed class CapturingRetentionPolicy : IFactRetentionPolicy
        {
            public string? LastTeller;
            public IReadOnlyList<Fact> Retain(WorldEvent evt, int hop, string tellerHeroId)
            {
                LastTeller = tellerHeroId;
                return evt.Facts;
            }
        }

        [Fact]
        public void RetentionPolicy_IsAskedForTheHoldersOwnVersion_NotTheSources()
        {
            // 對話那邊算講述者手上的碎片時，傳的是講述者自己；這裡要算同一份，不能傳來源
            var evt = CreateEvent("evt_holder", knowers: ("hero_1", 1.0, 0.5, null, null, null));
            evt.KnownBy[0].SourceHeroId = "hero_source";
            var policy = new CapturingRetentionPolicy();

            NpcRecallQuery.Query("hero_1", 1.0, 10, new[] { evt }, policy);

            Assert.Equal("hero_1", policy.LastTeller);
        }

        [Fact]
        public void LinkedCorrection_WhenCandidateListContainsLinkedEvent_DetectsCorrectionAutomatically()
        {
            var rootEvt = CreateEvent("evt_parent", knowers: ("hero_1", 1.0, 0.5, null, null, null));
            var childEvt = CreateEvent("evt_child", linkedEventId: "evt_parent",
                knowers: ("hero_1", 2.0, 0.5, null, null, null));

            // Do not pass isEventKnown delegate; verify it finds it in candidate list
            var result = NpcRecallQuery.Query("hero_1", currentDay: 3.0, maxCount: 10,
                new[] { childEvt, rootEvt });

            var childMemory = result.Items.FirstOrDefault(m => m.EventId == "evt_child");
            Assert.NotNull(childMemory);
            Assert.True(childMemory!.IsCorrection);
        }

        [Fact]
        public void ExclusionSummary_FormatsAllReasonCountsCorrectly()
        {
            var notVisible = CreateEvent("e_nv", origin: EventOrigin.Secret, leaked: false, knowers: ("hero_1", 1.0, 0.5, null, null, null));
            var notKnown = CreateEvent("e_nk", knowers: ("other", 1.0, 0.5, null, null, null));
            var forgotten = CreateEvent("e_fg", knowers: ("hero_1", 1.0, 0.5, forgetDay: 1.0, null, null));
            var outdated = CreateEvent("e_od", knowers: ("hero_1", 1.0, 0.5, null, outdatedDay: 1.0, null));
            var noFacts = CreateEvent("e_nf", knowers: ("hero_1", 1.0, 0.5, null, null, knownFactIds: new List<string>()));

            var result = NpcRecallQuery.Query("hero_1", currentDay: 2.0, maxCount: 10,
                new[] { notVisible, notKnown, forgotten, outdated, noFacts });

            Assert.Equal(5, result.Exclusions.Count);
            Assert.Equal("forgotten=1, outdated=1, notVisible=1, notKnown=1, noFacts=1", result.ExclusionSummary());
        }
    }
}
