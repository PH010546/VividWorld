using System;
using System.Collections.Generic;
using VividWorld.Core.Config;
using VividWorld.Core.Events;
using Xunit;

namespace VividWorld.Core.Tests
{
    public class MadeUpTalkPickerTests
    {
        private const string TellerId = "hero_teller";
        private const string PlayerId = "hero_player";

        private static WorldEvent CreateMadeUpEvent(
            string id,
            double day,
            string originatorId = "hero_originator",
            bool tellerKnows = true,
            bool playerKnows = false,
            double? forgetDay = null,
            double? outdatedDay = null)
        {
            var evt = new WorldEvent
            {
                EventId = id,
                Type = "conduct_mistreated_prisoner",
                Day = day,
                Origin = EventOrigin.Public,
                Fabricated = true,
                OriginatorHeroId = originatorId,
                Participants = new Dictionary<string, string>
                {
                    ["captor"] = "hero_captor",
                    ["prisoner"] = "hero_prisoner"
                }
            };

            if (tellerKnows)
            {
                evt.KnownBy.Add(new KnownByEntry
                {
                    HeroId = TellerId,
                    Hop = 1,
                    ForgetDay = forgetDay,
                    OutdatedDay = outdatedDay
                });
            }

            if (playerKnows)
            {
                evt.KnownBy.Add(new KnownByEntry
                {
                    HeroId = PlayerId,
                    Hop = 2
                });
            }

            return evt;
        }

        [Fact]
        public void PickMadeUpTalk_WhenEligible_SelectsLatestDay()
        {
            var older = CreateMadeUpEvent("evt_01", 10.0);
            var newer = CreateMadeUpEvent("evt_02", 15.0);
            var store = new Dictionary<string, WorldEvent>
            {
                ["evt_01"] = older,
                ["evt_02"] = newer
            };

            var result = MadeUpTalkPicker.PickMadeUpTalk(
                new[] { "evt_01", "evt_02" },
                id => store.TryGetValue(id, out var e) ? e : null,
                TellerId,
                PlayerId,
                day: 15.0,
                memoryConfig: new MemoryConfig { Enabled = true });

            Assert.NotNull(result.ChosenEvent);
            Assert.Equal("evt_02", result.ChosenEvent.EventId);
            Assert.Equal(2, result.TotalKnown);
            Assert.Equal(2, result.TotalMadeUpKnown);
            Assert.Equal(0, result.AlreadyHeardCount);
            Assert.Equal(0, result.ForgottenCount);
            Assert.Equal(0, result.NotMadeUpCount);
        }

        [Fact]
        public void PickMadeUpTalk_WhenSameDay_SelectsSmallestEventIdOrdinal()
        {
            var evtB = CreateMadeUpEvent("evt_b", 10.0);
            var evtA = CreateMadeUpEvent("evt_a", 10.0);
            var store = new Dictionary<string, WorldEvent>
            {
                ["evt_b"] = evtB,
                ["evt_a"] = evtA
            };

            var result = MadeUpTalkPicker.PickMadeUpTalk(
                new[] { "evt_b", "evt_a" },
                id => store.TryGetValue(id, out var e) ? e : null,
                TellerId,
                PlayerId,
                day: 10.0,
                memoryConfig: new MemoryConfig { Enabled = true });

            Assert.NotNull(result.ChosenEvent);
            Assert.Equal("evt_a", result.ChosenEvent.EventId);
        }

        [Fact]
        public void PickMadeUpTalk_WhenNotHearsayOnlyMadeUpTalk_Excluded()
        {
            var realEvent = CreateMadeUpEvent("evt_real", 10.0);
            realEvent.Fabricated = false;

            var selfToldEvent = CreateMadeUpEvent("evt_selftold", 10.0, originatorId: "hero_captor");

            var store = new Dictionary<string, WorldEvent>
            {
                ["evt_real"] = realEvent,
                ["evt_selftold"] = selfToldEvent
            };

            var result = MadeUpTalkPicker.PickMadeUpTalk(
                new[] { "evt_real", "evt_selftold" },
                id => store.TryGetValue(id, out var e) ? e : null,
                TellerId,
                PlayerId,
                day: 10.0,
                memoryConfig: new MemoryConfig { Enabled = true });

            Assert.Null(result.ChosenEvent);
            Assert.Equal(2, result.TotalKnown);
            Assert.Equal(0, result.TotalMadeUpKnown);
            Assert.Equal(2, result.NotMadeUpCount);
        }

        [Fact]
        public void PickMadeUpTalk_WhenPlayerAlreadyHeardInKnownBy_Excluded()
        {
            var heardEvt = CreateMadeUpEvent("evt_01", 10.0, playerKnows: true);
            var store = new Dictionary<string, WorldEvent> { ["evt_01"] = heardEvt };

            var result = MadeUpTalkPicker.PickMadeUpTalk(
                new[] { "evt_01" },
                id => store.TryGetValue(id, out var e) ? e : null,
                TellerId,
                PlayerId,
                day: 10.0,
                memoryConfig: new MemoryConfig { Enabled = true });

            Assert.Null(result.ChosenEvent);
            Assert.Equal(1, result.TotalMadeUpKnown);
            Assert.Equal(1, result.AlreadyHeardCount);
        }

        [Fact]
        public void PickMadeUpTalk_WhenPlayerAlreadyHeardInLog_Excluded()
        {
            var unreadInEvent = CreateMadeUpEvent("evt_01", 10.0, playerKnows: false);
            var store = new Dictionary<string, WorldEvent> { ["evt_01"] = unreadInEvent };

            var result = MadeUpTalkPicker.PickMadeUpTalk(
                new[] { "evt_01" },
                id => store.TryGetValue(id, out var e) ? e : null,
                TellerId,
                PlayerId,
                day: 10.0,
                memoryConfig: new MemoryConfig { Enabled = true },
                isPlayerKnowerExtra: id => id == "evt_01");

            Assert.Null(result.ChosenEvent);
            Assert.Equal(1, result.TotalMadeUpKnown);
            Assert.Equal(1, result.AlreadyHeardCount);
        }

        [Fact]
        public void PickMadeUpTalk_WhenTellerForgot_Excluded()
        {
            var forgottenEvt = CreateMadeUpEvent("evt_01", 10.0, tellerKnows: true, forgetDay: 12.0);
            var notKnownEvt = CreateMadeUpEvent("evt_02", 10.0, tellerKnows: false);
            var store = new Dictionary<string, WorldEvent>
            {
                ["evt_01"] = forgottenEvt,
                ["evt_02"] = notKnownEvt
            };

            var result = MadeUpTalkPicker.PickMadeUpTalk(
                new[] { "evt_01", "evt_02" },
                id => store.TryGetValue(id, out var e) ? e : null,
                TellerId,
                PlayerId,
                day: 13.0,
                memoryConfig: new MemoryConfig { Enabled = true });

            Assert.Null(result.ChosenEvent);
            Assert.Equal(2, result.TotalMadeUpKnown);
            Assert.Equal(2, result.ForgottenCount);
        }

        [Fact]
        public void PickMadeUpTalk_WhenNoCandidates_ReturnsNullWithCounts()
        {
            var result = MadeUpTalkPicker.PickMadeUpTalk(
                Array.Empty<string>(),
                id => null,
                TellerId,
                PlayerId,
                day: 10.0,
                memoryConfig: new MemoryConfig { Enabled = true });

            Assert.Null(result.ChosenEvent);
            Assert.Equal(0, result.TotalKnown);
            Assert.Equal(0, result.TotalMadeUpKnown);
        }

        [Fact]
        public void PickResponseSpeaker_PicksKnowerWithLowestHop()
        {
            var resp = new WorldEvent
            {
                EventId = "resp_01",
                Type = "talk_denied_mistreated_prisoner",
                Day = 11.0
            };
            resp.KnownBy.Add(new KnownByEntry { HeroId = "hero_b", Hop = 2 });
            resp.KnownBy.Add(new KnownByEntry { HeroId = "hero_a", Hop = 0 });
            resp.KnownBy.Add(new KnownByEntry { HeroId = "hero_c", Hop = 1 });

            var speaker = MadeUpTalkPicker.PickResponseSpeaker(resp, PlayerId, day: 11.0, memoryConfig: new MemoryConfig());

            Assert.NotNull(speaker);
            Assert.Equal("hero_a", speaker.HeroId);
            Assert.Equal(0, speaker.Hop);
        }

        [Fact]
        public void PickResponseSpeaker_ExcludesPlayer()
        {
            var resp = new WorldEvent
            {
                EventId = "resp_01",
                Type = "talk_denied_mistreated_prisoner",
                Day = 11.0
            };
            resp.KnownBy.Add(new KnownByEntry { HeroId = PlayerId, Hop = 0 });
            resp.KnownBy.Add(new KnownByEntry { HeroId = "hero_other", Hop = 1 });

            var speaker = MadeUpTalkPicker.PickResponseSpeaker(resp, PlayerId, day: 11.0, memoryConfig: new MemoryConfig());

            Assert.NotNull(speaker);
            Assert.Equal("hero_other", speaker.HeroId);
        }

        [Fact]
        public void PickResponseSpeaker_WhenHopTie_PicksSmallestHeroIdOrdinal()
        {
            var resp = new WorldEvent
            {
                EventId = "resp_01",
                Type = "talk_denied_mistreated_prisoner",
                Day = 11.0
            };
            resp.KnownBy.Add(new KnownByEntry { HeroId = "hero_z", Hop = 1 });
            resp.KnownBy.Add(new KnownByEntry { HeroId = "hero_m", Hop = 1 });
            resp.KnownBy.Add(new KnownByEntry { HeroId = "hero_b", Hop = 1 });

            var speaker = MadeUpTalkPicker.PickResponseSpeaker(resp, PlayerId, day: 11.0, memoryConfig: new MemoryConfig());

            Assert.NotNull(speaker);
            Assert.Equal("hero_b", speaker.HeroId);
        }

        [Fact]
        public void PickResponseSpeaker_ExcludesForgottenKnowers()
        {
            var resp = new WorldEvent
            {
                EventId = "resp_01",
                Type = "talk_denied_mistreated_prisoner",
                Day = 11.0,
                Origin = EventOrigin.Public
            };
            resp.KnownBy.Add(new KnownByEntry { HeroId = "hero_a", Hop = 0, ForgetDay = 12.0 });
            resp.KnownBy.Add(new KnownByEntry { HeroId = "hero_b", Hop = 1, ForgetDay = 20.0 });

            var speaker = MadeUpTalkPicker.PickResponseSpeaker(resp, PlayerId, day: 15.0, memoryConfig: new MemoryConfig { Enabled = true });

            Assert.NotNull(speaker);
            Assert.Equal("hero_b", speaker.HeroId);
        }

        [Fact]
        public void SortResponseEvents_SortsByDayAscendingThenEventId()
        {
            var e3 = new WorldEvent { EventId = "resp_03", Day = 12.0 };
            var e1 = new WorldEvent { EventId = "resp_01", Day = 10.0 };
            var e2 = new WorldEvent { EventId = "resp_02", Day = 10.0 };

            var sorted = MadeUpTalkPicker.SortResponseEvents(new[] { e3, e2, e1 });

            Assert.Equal(3, sorted.Count);
            Assert.Equal("resp_01", sorted[0].EventId);
            Assert.Equal("resp_02", sorted[1].EventId);
            Assert.Equal("resp_03", sorted[2].EventId);
        }
    }
}
