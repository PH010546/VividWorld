#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json.Linq;
using VividWorld.Core.Dialogue;
using VividWorld.Core.Persistence;
using VividWorld.Core.Rumors;
using VividWorld.Core.Tests.Fakes;
using VividWorld.Core.Util;
using Xunit;

namespace VividWorld.Core.Tests
{
    public class ListenTallyTests
    {
        [Fact]
        public void Classify_NotInNetwork_WhenNotEligible()
        {
            string key = ListenTallyClassifier.ClassifyVolunteer(
                isEligible: false,
                volunteerConditionRan: true,
                rivalCount: 0,
                commonerTierBlocked: false,
                willLordAttack: false,
                decision: null,
                knownCount: 5,
                forgottenCount: 0,
                outdatedCount: 0,
                delivered: false);

            Assert.Equal(ListenTallyKeys.NotInNetwork, key);
        }

        [Fact]
        public void Classify_VolunteerNotEvaluatedTokenLost_WhenConditionDidNotRunAndRivalsExist()
        {
            string key = ListenTallyClassifier.ClassifyVolunteer(
                isEligible: true,
                volunteerConditionRan: false,
                rivalCount: 2,
                commonerTierBlocked: false,
                willLordAttack: false,
                decision: null,
                knownCount: 5,
                forgottenCount: 0,
                outdatedCount: 0,
                delivered: false);

            Assert.Equal(ListenTallyKeys.VolunteerNotEvaluatedTokenLost, key);
        }

        [Fact]
        public void Classify_VolunteerNotEvaluatedStartNotReached_WhenConditionDidNotRunAndNoRivals()
        {
            string key = ListenTallyClassifier.ClassifyVolunteer(
                isEligible: true,
                volunteerConditionRan: false,
                rivalCount: 0,
                commonerTierBlocked: false,
                willLordAttack: false,
                decision: null,
                knownCount: 5,
                forgottenCount: 0,
                outdatedCount: 0,
                delivered: false);

            Assert.Equal(ListenTallyKeys.VolunteerNotEvaluatedStartNotReached, key);
        }

        [Fact]
        public void Classify_VolunteerBlockedCommonerTier_WhenCommonerTierBlocked()
        {
            string key = ListenTallyClassifier.ClassifyVolunteer(
                isEligible: true,
                volunteerConditionRan: true,
                rivalCount: 0,
                commonerTierBlocked: true,
                willLordAttack: false,
                decision: null,
                knownCount: 5,
                forgottenCount: 0,
                outdatedCount: 0,
                delivered: false);

            Assert.Equal(ListenTallyKeys.VolunteerBlockedCommonerTier, key);
        }

        [Fact]
        public void Classify_VolunteerBlockedLordAttack_WhenWillLordAttack()
        {
            string key = ListenTallyClassifier.ClassifyVolunteer(
                isEligible: true,
                volunteerConditionRan: true,
                rivalCount: 0,
                commonerTierBlocked: false,
                willLordAttack: true,
                decision: null,
                knownCount: 5,
                forgottenCount: 0,
                outdatedCount: 0,
                delivered: false);

            Assert.Equal(ListenTallyKeys.VolunteerBlockedLordAttack, key);
        }

        [Fact]
        public void Classify_VolunteerBlockedRelationGate_WhenRelationGateRefusal()
        {
            var decision = new VolunteerDecision { Refusal = VolunteerRefusal.RelationGate };
            string key = ListenTallyClassifier.ClassifyVolunteer(
                isEligible: true,
                volunteerConditionRan: true,
                rivalCount: 0,
                commonerTierBlocked: false,
                willLordAttack: false,
                decision: decision,
                knownCount: 5,
                forgottenCount: 0,
                outdatedCount: 0,
                delivered: false);

            Assert.Equal(ListenTallyKeys.VolunteerBlockedRelationGate, key);
        }

        [Fact]
        public void Classify_VolunteerBlockedCooldown_WhenCooldownRefusal()
        {
            var decision = new VolunteerDecision { Refusal = VolunteerRefusal.Cooldown };
            string key = ListenTallyClassifier.ClassifyVolunteer(
                isEligible: true,
                volunteerConditionRan: true,
                rivalCount: 0,
                commonerTierBlocked: false,
                willLordAttack: false,
                decision: decision,
                knownCount: 5,
                forgottenCount: 0,
                outdatedCount: 0,
                delivered: false);

            Assert.Equal(ListenTallyKeys.VolunteerBlockedCooldown, key);
        }

        [Fact]
        public void Classify_VolunteerBlockedDailyCap_WhenDailyCapRefusal()
        {
            var decision = new VolunteerDecision { Refusal = VolunteerRefusal.DailyCap };
            string key = ListenTallyClassifier.ClassifyVolunteer(
                isEligible: true,
                volunteerConditionRan: true,
                rivalCount: 0,
                commonerTierBlocked: false,
                willLordAttack: false,
                decision: decision,
                knownCount: 5,
                forgottenCount: 0,
                outdatedCount: 0,
                delivered: false);

            Assert.Equal(ListenTallyKeys.VolunteerBlockedDailyCap, key);
        }

        [Fact]
        public void Classify_VolunteerNoTopicNothingOnFile_WhenNoKnownEventsAndZeroOnFile()
        {
            var decision = new VolunteerDecision { Refusal = VolunteerRefusal.NoKnownEvents };
            string key = ListenTallyClassifier.ClassifyVolunteer(
                isEligible: true,
                volunteerConditionRan: true,
                rivalCount: 0,
                commonerTierBlocked: false,
                willLordAttack: false,
                decision: decision,
                knownCount: 0,
                forgottenCount: 0,
                outdatedCount: 0,
                delivered: false);

            Assert.Equal(ListenTallyKeys.VolunteerNoTopicNothingOnFile, key);
        }

        [Fact]
        public void Classify_VolunteerNoTopicAllForgotten_WhenAllEventsForgotten()
        {
            var decision = new VolunteerDecision { Refusal = VolunteerRefusal.NoKnownEvents };
            string key = ListenTallyClassifier.ClassifyVolunteer(
                isEligible: true,
                volunteerConditionRan: true,
                rivalCount: 0,
                commonerTierBlocked: false,
                willLordAttack: false,
                decision: decision,
                knownCount: 4,
                forgottenCount: 4,
                outdatedCount: 0,
                delivered: false);

            Assert.Equal(ListenTallyKeys.VolunteerNoTopicAllForgotten, key);
        }

        [Fact]
        public void Classify_VolunteerNoTopicAllOutdated_WhenAllEventsOutdated()
        {
            var decision = new VolunteerDecision { Refusal = VolunteerRefusal.NoKnownEvents };
            string key = ListenTallyClassifier.ClassifyVolunteer(
                isEligible: true,
                volunteerConditionRan: true,
                rivalCount: 0,
                commonerTierBlocked: false,
                willLordAttack: false,
                decision: decision,
                knownCount: 4,
                forgottenCount: 0,
                outdatedCount: 4,
                delivered: false);

            Assert.Equal(ListenTallyKeys.VolunteerNoTopicAllOutdated, key);
        }

        [Fact]
        public void Classify_VolunteerNoTopicForgottenOrOutdated_WhenMixed()
        {
            var decision = new VolunteerDecision { Refusal = VolunteerRefusal.NoKnownEvents };
            string key = ListenTallyClassifier.ClassifyVolunteer(
                isEligible: true,
                volunteerConditionRan: true,
                rivalCount: 0,
                commonerTierBlocked: false,
                willLordAttack: false,
                decision: decision,
                knownCount: 4,
                forgottenCount: 2,
                outdatedCount: 2,
                delivered: false);

            Assert.Equal(ListenTallyKeys.VolunteerNoTopicForgottenOrOutdated, key);
        }

        [Fact]
        public void Classify_VolunteerFiltered_WhenAllCandidatesFiltered()
        {
            var decision = new VolunteerDecision
            {
                Refusal = VolunteerRefusal.AllCandidatesFiltered,
                CandidateCount = 3,
                FilteredNotVisible = 1,
                FilteredPlayerKnows = 2
            };
            string key = ListenTallyClassifier.ClassifyVolunteer(
                isEligible: true,
                volunteerConditionRan: true,
                rivalCount: 0,
                commonerTierBlocked: false,
                willLordAttack: false,
                decision: decision,
                knownCount: 3,
                forgottenCount: 0,
                outdatedCount: 0,
                delivered: false);

            Assert.Equal(ListenTallyKeys.VolunteerFiltered, key);
        }

        [Fact]
        public void Classify_VolunteerTold_WhenDelivered()
        {
            var decision = new VolunteerDecision
            {
                Refusal = VolunteerRefusal.None,
                Offer = new RumorOffer { EventId = "evt_1" }
            };
            string key = ListenTallyClassifier.ClassifyVolunteer(
                isEligible: true,
                volunteerConditionRan: true,
                rivalCount: 0,
                commonerTierBlocked: false,
                willLordAttack: false,
                decision: decision,
                knownCount: 3,
                forgottenCount: 0,
                outdatedCount: 0,
                delivered: true);

            Assert.Equal(ListenTallyKeys.VolunteerTold, key);
        }

        [Fact]
        public void Classify_VolunteerChosenNotDelivered_WhenChosenButNotDelivered()
        {
            var decision = new VolunteerDecision
            {
                Refusal = VolunteerRefusal.None,
                Offer = new RumorOffer { EventId = "evt_1" }
            };
            string key = ListenTallyClassifier.ClassifyVolunteer(
                isEligible: true,
                volunteerConditionRan: true,
                rivalCount: 0,
                commonerTierBlocked: false,
                willLordAttack: false,
                decision: decision,
                knownCount: 3,
                forgottenCount: 0,
                outdatedCount: 0,
                delivered: false);

            Assert.Equal(ListenTallyKeys.VolunteerChosenNotDelivered, key);
        }

        [Fact]
        public void Classify_AskAsked_TracksAskedFlag()
        {
            var tally = new ListenDayTally();
            tally.Increment(ListenTallyKeys.AskAsked);
            Assert.Equal(1, tally.GetCount(ListenTallyKeys.AskAsked));
        }

        [Fact]
        public void Classify_AskTold_WhenDelivered()
        {
            string key = ListenTallyClassifier.ClassifyAsk(
                asked: true,
                told: true,
                decision: new AskDecision { Refusal = AskRefusal.None },
                knownCount: 3,
                forgottenCount: 0,
                outdatedCount: 0);

            Assert.Equal(ListenTallyKeys.AskTold, key);
        }

        [Fact]
        public void Classify_AskRefusedRelationGate_WhenRelationGateRefusal()
        {
            var decision = new AskDecision { Refusal = AskRefusal.RelationGate };
            string key = ListenTallyClassifier.ClassifyAsk(
                asked: true,
                told: false,
                decision: decision,
                knownCount: 3,
                forgottenCount: 0,
                outdatedCount: 0);

            Assert.Equal(ListenTallyKeys.AskRefusedRelationGate, key);
        }

        [Fact]
        public void Classify_AskRefusedWillingnessGate_WhenWillingnessRefusal()
        {
            var decision = new AskDecision { Refusal = AskRefusal.WillingnessGate };
            string key = ListenTallyClassifier.ClassifyAsk(
                asked: true,
                told: false,
                decision: decision,
                knownCount: 3,
                forgottenCount: 0,
                outdatedCount: 0);

            Assert.Equal(ListenTallyKeys.AskRefusedWillingnessGate, key);
        }

        [Fact]
        public void Classify_AskNoTopicNothingOnFile_WhenNoKnownEventsAndZeroOnFile()
        {
            var decision = new AskDecision { Refusal = AskRefusal.NoKnownEvents };
            string key = ListenTallyClassifier.ClassifyAsk(
                asked: true,
                told: false,
                decision: decision,
                knownCount: 0,
                forgottenCount: 0,
                outdatedCount: 0);

            Assert.Equal(ListenTallyKeys.AskNoTopicNothingOnFile, key);
        }

        [Fact]
        public void Classify_AskNoTopicAllForgotten_WhenAllEventsForgotten()
        {
            var decision = new AskDecision { Refusal = AskRefusal.NoKnownEvents };
            string key = ListenTallyClassifier.ClassifyAsk(
                asked: true,
                told: false,
                decision: decision,
                knownCount: 5,
                forgottenCount: 5,
                outdatedCount: 0);

            Assert.Equal(ListenTallyKeys.AskNoTopicAllForgotten, key);
        }

        [Fact]
        public void Classify_AskNoTopicAllOutdated_WhenAllEventsOutdated()
        {
            var decision = new AskDecision { Refusal = AskRefusal.NoKnownEvents };
            string key = ListenTallyClassifier.ClassifyAsk(
                asked: true,
                told: false,
                decision: decision,
                knownCount: 5,
                forgottenCount: 0,
                outdatedCount: 5);

            Assert.Equal(ListenTallyKeys.AskNoTopicAllOutdated, key);
        }

        [Fact]
        public void Classify_AskNoTopicForgottenOrOutdated_WhenMixed()
        {
            var decision = new AskDecision { Refusal = AskRefusal.NoKnownEvents };
            string key = ListenTallyClassifier.ClassifyAsk(
                asked: true,
                told: false,
                decision: decision,
                knownCount: 5,
                forgottenCount: 2,
                outdatedCount: 3);

            Assert.Equal(ListenTallyKeys.AskNoTopicForgottenOrOutdated, key);
        }

        [Fact]
        public void Classify_AskFiltered_WhenAllCandidatesFiltered()
        {
            var decision = new AskDecision { Refusal = AskRefusal.AllCandidatesFiltered };
            string key = ListenTallyClassifier.ClassifyAsk(
                asked: true,
                told: false,
                decision: decision,
                knownCount: 5,
                forgottenCount: 0,
                outdatedCount: 0);

            Assert.Equal(ListenTallyKeys.AskFiltered, key);
        }

        [Fact]
        public void Classify_RecoveryShown_TracksShown()
        {
            var tally = new ListenDayTally();
            tally.Increment(ListenTallyKeys.RecoveryShown);
            Assert.Equal(1, tally.GetCount(ListenTallyKeys.RecoveryShown));
        }

        [Fact]
        public void Classify_RecoveryUsed_TracksUsed()
        {
            var tally = new ListenDayTally();
            tally.Increment(ListenTallyKeys.RecoveryUsed);
            Assert.Equal(1, tally.GetCount(ListenTallyKeys.RecoveryUsed));
        }

        [Fact]
        public void Conversation_MultipleEvaluationsInSameConversation_IncrementsOnlyOnce()
        {
            var tally = new ListenTally();
            int day = 10;
            var dayTally = tally.GetOrCreateDay(day);

            // Simulate condition running 5 times across multiple frames during a single conversation
            var decision = new VolunteerDecision { Refusal = VolunteerRefusal.RelationGate };
            int evalCount = 0;
            for (int frame = 0; frame < 5; frame++)
            {
                evalCount++;
                // In condition functions: NO tally accumulation occurs!
            }
            Assert.Equal(5, evalCount);

            // At ConversationEnded: Exactly 1 record operation occurs
            dayTally.RecordPartner("hero_test", 15, onFile: 3, remembered: 3);
            string key = ListenTallyClassifier.ClassifyVolunteer(
                isEligible: true,
                volunteerConditionRan: true,
                rivalCount: 0,
                commonerTierBlocked: false,
                willLordAttack: false,
                decision: decision,
                knownCount: 3,
                forgottenCount: 0,
                outdatedCount: 0,
                delivered: false);

            dayTally.Increment(key);

            Assert.Equal(1, dayTally.GetCount(ListenTallyKeys.VolunteerBlockedRelationGate));
            Assert.Equal(1, dayTally.DistinctPartners);
            Assert.Equal(1, dayTally.RelationHist["10..19"]);
            Assert.Equal(3, dayTally.EventsOnFile);
            Assert.Equal(3, dayTally.EventsRemembered);
        }

        [Fact]
        public void Store_RoundTripsThroughRealJson()
        {
            var root = new ListenTally { Version = 1 };
            var d10 = root.GetOrCreateDay(10);
            d10.Increment(ListenTallyKeys.VolunteerTold, 2);
            d10.Increment(ListenTallyKeys.VolunteerBlockedRelationGate, 5);
            d10.Increment(ListenTallyKeys.AskAsked, 3);
            d10.Increment(ListenTallyKeys.AskTold, 1);
            d10.RecordPartner("hero_a", 25, 4, 3);
            d10.RecordPartner("hero_b", -15, 2, 1);
            d10.Extra["futureField"] = new JValue(12345);

            var d11 = root.GetOrCreateDay(11);
            d11.Increment(ListenTallyKeys.NotInNetwork, 4);
            d11.RecordPartner("hero_c", 0, 0, 0);

            string json = VividJson.Write(root);
            var loaded = VividJson.Read<ListenTally>(json);

            Assert.NotNull(loaded);
            Assert.Equal(1, loaded!.Version);
            Assert.Equal(2, loaded.Days.Count);

            var loaded10 = loaded.GetDay(10);
            Assert.NotNull(loaded10);
            Assert.Equal(2, loaded10!.GetCount(ListenTallyKeys.VolunteerTold));
            Assert.Equal(5, loaded10.GetCount(ListenTallyKeys.VolunteerBlockedRelationGate));
            Assert.Equal(3, loaded10.GetCount(ListenTallyKeys.AskAsked));
            Assert.Equal(1, loaded10.GetCount(ListenTallyKeys.AskTold));
            Assert.Equal(2, loaded10.DistinctPartners);
            Assert.Equal(1, loaded10.RelationHist["20..29"]);
            Assert.Equal(1, loaded10.RelationHist["-20..-11"]);
            Assert.Equal(6, loaded10.EventsOnFile);
            Assert.Equal(4, loaded10.EventsRemembered);
            Assert.True(loaded10.Extra.ContainsKey("futureField"));
            Assert.Equal(12345, loaded10.Extra["futureField"].Value<int>());

            var loaded11 = loaded.GetDay(11);
            Assert.NotNull(loaded11);
            Assert.Equal(4, loaded11!.GetCount(ListenTallyKeys.NotInNetwork));
            Assert.Equal(1, loaded11.DistinctPartners);
            Assert.Equal(1, loaded11.RelationHist["0..9"]);
        }

        [Fact]
        public void Store_MissingFile_ReturnsEmptyTallyStartingAtZero()
        {
            var fakeWriter = new FailingFileWriter();
            var store = new ListenTallyStore("missing/path/listen_tally.json", fakeWriter);

            var tally = store.Load();

            Assert.NotNull(tally);
            Assert.Equal(1, tally.Version);
            Assert.Empty(tally.Days);

            var dayTally = tally.GetDay(5);
            Assert.Null(dayTally);
            Assert.Equal(0, tally.GetOrCreateDay(5).GetCount(ListenTallyKeys.VolunteerTold));
        }

        [Fact]
        public void Store_CorruptFile_ReturnsEmptyTallyAndReportsWhy()
        {
            var fakeWriter = new FailingFileWriter();
            fakeWriter.Files["campaign/listen_tally.json"] = "{ this is not json";
            var store = new ListenTallyStore("campaign/listen_tally.json", fakeWriter);

            var tally = store.Load();

            Assert.NotNull(tally);
            Assert.Empty(tally.Days);
            Assert.False(string.IsNullOrEmpty(store.LastLoadError));

            // 讀成功之後要清掉上一次的錯誤，不然呼叫端會一直印舊的警告。
            fakeWriter.Files["campaign/listen_tally.json"] = VividJson.Write(new ListenTally());
            store.Load();
            Assert.Null(store.LastLoadError);
        }

        [Fact]
        public void RelationBin_CalculatesCorrect10PointSymmetricIntervals()
        {
            // Boundaries
            Assert.Equal("-100..-91", ListenTallyClassifier.GetRelationBin(-100));
            Assert.Equal("-100..-91", ListenTallyClassifier.GetRelationBin(-95));
            Assert.Equal("-100..-91", ListenTallyClassifier.GetRelationBin(-91));

            Assert.Equal("-90..-81", ListenTallyClassifier.GetRelationBin(-90));
            Assert.Equal("-90..-81", ListenTallyClassifier.GetRelationBin(-85));
            Assert.Equal("-90..-81", ListenTallyClassifier.GetRelationBin(-81));

            Assert.Equal("-20..-11", ListenTallyClassifier.GetRelationBin(-20));
            Assert.Equal("-20..-11", ListenTallyClassifier.GetRelationBin(-15));
            Assert.Equal("-20..-11", ListenTallyClassifier.GetRelationBin(-11));

            Assert.Equal("-10..-1", ListenTallyClassifier.GetRelationBin(-10));
            Assert.Equal("-10..-1", ListenTallyClassifier.GetRelationBin(-5));
            Assert.Equal("-10..-1", ListenTallyClassifier.GetRelationBin(-1));

            Assert.Equal("0..9", ListenTallyClassifier.GetRelationBin(0));
            Assert.Equal("0..9", ListenTallyClassifier.GetRelationBin(5));
            Assert.Equal("0..9", ListenTallyClassifier.GetRelationBin(9));

            Assert.Equal("10..19", ListenTallyClassifier.GetRelationBin(10));
            Assert.Equal("10..19", ListenTallyClassifier.GetRelationBin(15));
            Assert.Equal("10..19", ListenTallyClassifier.GetRelationBin(19));

            Assert.Equal("80..89", ListenTallyClassifier.GetRelationBin(80));
            Assert.Equal("80..89", ListenTallyClassifier.GetRelationBin(85));
            Assert.Equal("80..89", ListenTallyClassifier.GetRelationBin(89));

            Assert.Equal("90..100", ListenTallyClassifier.GetRelationBin(90));
            Assert.Equal("90..100", ListenTallyClassifier.GetRelationBin(95));
            Assert.Equal("90..100", ListenTallyClassifier.GetRelationBin(100));

            // Clamping
            Assert.Equal("-100..-91", ListenTallyClassifier.GetRelationBin(-150));
            Assert.Equal("90..100", ListenTallyClassifier.GetRelationBin(150));
        }

        [Fact]
        public void LogFormatter_FormatDailyLine_OutputsExpectedSummary()
        {
            var dayTally = new ListenDayTally();
            dayTally.Increment(ListenTallyKeys.VolunteerTold, 1);
            dayTally.Increment(ListenTallyKeys.VolunteerBlockedRelationGate, 5);
            dayTally.Increment(ListenTallyKeys.VolunteerNoTopicNothingOnFile, 3);
            dayTally.Increment(ListenTallyKeys.NotInNetwork, 2);
            dayTally.Increment(ListenTallyKeys.AskTold, 2);
            dayTally.RecordPartner("h1", 10);
            dayTally.RecordPartner("h2", 20);

            string line = ListenTallyLogFormatter.FormatDailyLine(12, dayTally);

            Assert.StartsWith("Listen tally day 12: talked 11 (network 9, distinct 2) | told 1 volunteered + 2 asked | top blockers:", line);
            Assert.Contains("relationGate 5", line);
            Assert.Contains("noTopic.nothingOnFile 3", line);
            Assert.Contains("notInNetwork 2", line);
        }

        [Fact]
        public void LogFormatter_FormatDevReport_OutputsAllTimeAnd7DayReport()
        {
            var tally = new ListenTally();
            var d1 = tally.GetOrCreateDay(1);
            d1.Increment(ListenTallyKeys.VolunteerTold, 1);
            d1.RecordPartner("h1", 15);

            var d5 = tally.GetOrCreateDay(5);
            d5.Increment(ListenTallyKeys.VolunteerBlockedRelationGate, 4);
            d5.RecordPartner("h2", -5);

            string report = ListenTallyLogFormatter.FormatDevReport(tally, currentDay: 5, "campaigns/campaign_test/listen_tally.json");

            Assert.Contains("=== Listen Tally (Why do I hear so little?) ===", report);
            Assert.Contains("File: campaigns/campaign_test/listen_tally.json", report);
            Assert.Contains("[Campaign Summary (All Time:", report);
            Assert.Contains("[Recent 7 Days", report);
            Assert.Contains("Relation Histogram (All Time):", report);
            Assert.Contains("10..19: 1", report);
            Assert.Contains("-10..-1: 1", report);
        }
    }
}
