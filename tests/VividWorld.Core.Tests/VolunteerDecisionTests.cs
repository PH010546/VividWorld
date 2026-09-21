using System.Collections.Generic;
using VividWorld.Core.Dialogue;
using VividWorld.Core.Presentation;
using Xunit;

namespace VividWorld.Core.Tests
{
    public sealed class VolunteerDecisionTests
    {
        /// <summary>
        /// 他 2026-09-20 的 M8 實機回報抓到的：同一段日誌表頭說「6 known」，
        /// 底下那行說「忘了 5 件」。`knownCount` 數的是「名字登記在內」，遺忘不在它的判斷裡，
        /// 所以這一行只要有遺忘就必須把兩個數字都講出來，絕不准單獨印一個「N known」。
        /// </summary>
        [Fact]
        public void FormatLog_WhenSomeAreForgotten_SaysRememberedAndOnFile_NeverJustKnown()
        {
            var decision = new VolunteerDecision
            {
                Refusal = VolunteerRefusal.RelationGate,
                Relation = 0,
                RelationGate = 30,
                IsCloseKin = false
            };

            string log = VolunteerDecision.FormatLog(
                "梅拉格", "lord_5_1_1", decision, knownCount: 6, lastVolunteerDesc: null, forgottenCount: 5);

            Assert.Contains("1 remembered of 6 on file", log);
            Assert.DoesNotContain("6 known", log);

            // 一件都沒忘的時候不要硬塞「6 remembered of 6」，那只是噪音。
            string noneForgotten = VolunteerDecision.FormatLog(
                "梅拉格", "lord_5_1_1", decision, knownCount: 6, lastVolunteerDesc: null, forgottenCount: 0);

            Assert.Contains("6 on file", noneForgotten);
            Assert.DoesNotContain("remembered", noneForgotten);
        }

        [Fact]
        public void FormatLog_EachRefusal_NamesTheGateAndTheNumbers()
        {
            // 1. RelationGate
            var relDecision = new VolunteerDecision
            {
                Refusal = VolunteerRefusal.RelationGate,
                Relation = 12,
                RelationGate = 30,
                IsCloseKin = false
            };
            string relLog = VolunteerDecision.FormatLog("埃隆", "lord_5_16", relDecision, knownCount: 3);
            Assert.Contains("silent - relation 12 < gate 30", relLog);
            Assert.Contains("(not close kin)", relLog);
            Assert.Contains("3 on file", relLog);

            // 2. Cooldown
            var cooldownDecision = new VolunteerDecision
            {
                Refusal = VolunteerRefusal.Cooldown,
                Relation = 42,
                Day = 26036.6,
                LastVolunteeredDay = 26035.4,
                CooldownDays = 3.0
            };
            string cooldownLog = VolunteerDecision.FormatLog("埃隆", "lord_5_16", cooldownDecision);
            Assert.Contains("silent - cooldown 1.2d < 3.0", cooldownLog);
            Assert.Contains("(last 26035.4)", cooldownLog);
            Assert.Contains("rel 42", cooldownLog);

            // 3. DailyCap
            var capDecision = new VolunteerDecision
            {
                Refusal = VolunteerRefusal.DailyCap,
                VolunteersToday = 1,
                MaxVolunteersPerDay = 1
            };
            string capLog = VolunteerDecision.FormatLog("埃隆", "lord_5_16", capDecision, lastVolunteerDesc: "梅拉格 evt_26024_52ff");
            Assert.Contains("silent - daily cap 1/1 already used today", capLog);
            Assert.Contains("(last: 梅拉格 evt_26024_52ff)", capLog);

            // 4. NoKnownEvents
            var noKnownDecision = new VolunteerDecision
            {
                Refusal = VolunteerRefusal.NoKnownEvents,
                Relation = 42
            };
            string noKnownLog = VolunteerDecision.FormatLog("埃隆", "lord_5_16", noKnownDecision);
            Assert.Contains("silent - no topic left", noKnownLog);
            Assert.Contains("nothing on file", noKnownLog);
            Assert.Contains("rel 42", noKnownLog);

            // 5. AllCandidatesFiltered
            var filteredDecision = new VolunteerDecision
            {
                Refusal = VolunteerRefusal.AllCandidatesFiltered,
                Relation = 42,
                CandidateCount = 2,
                FilteredPlayerKnows = 2,
                FilterNotes = new List<string>
                {
                    "evt_26023_ce75: teller is at hop 2, so telling lands the player at hop 3 - no closer than the hop 3 they already have"
                }
            };
            string filteredLog = VolunteerDecision.FormatLog("埃隆", "lord_5_16", filteredDecision);
            Assert.Contains("silent - all 2 candidates filtered (2 player already knows)", filteredLog);
            Assert.Contains("rel 42", filteredLog);
            Assert.Contains("evt_26023_ce75: teller is at hop 2", filteredLog);

            // 補充驗證：None (told) 與 FormatLordAboutToAttack
            var toldDecision = new VolunteerDecision
            {
                Refusal = VolunteerRefusal.None,
                Offer = new RumorOffer
                {
                    EventId = "evt_26036_a10c",
                    TellerHop = 1,
                    ResultingPlayerHop = 2,
                    Score = 5.58,
                    Composed = new ComposedRumor { Parts = new List<ComposedFactPart>() }
                },
                Relation = 42,
                RelationGate = 30,
                Day = 26036.6,
                LastVolunteeredDay = 26031.2,
                CooldownDays = 3.0,
                VolunteersToday = 0,
                MaxVolunteersPerDay = 1
            };
            string toldLog = VolunteerDecision.FormatLog("埃隆", "lord_5_16", toldDecision);
            Assert.Contains("told evt_26036_a10c hop 1->2 score 5.58", toldLog);
            Assert.Contains("rel 42 >= 30", toldLog);
            Assert.Contains("last 26031.2 (+5.4d >= 3.0)", toldLog);
            Assert.Contains("0/1 today", toldLog);

            string attackLog = VolunteerDecision.FormatLordAboutToAttack("埃隆", "lord_5_16");
            Assert.Equal("Volunteer 埃隆 (lord_5_16): silent - lord is about to attack (WillLordAttack)", attackLog);
        }
    }
}
