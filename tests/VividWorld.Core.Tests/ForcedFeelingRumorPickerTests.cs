using System;
using System.Collections.Generic;
using VividWorld.Core.Config;
using VividWorld.Core.Events;
using VividWorld.Core.Feelings;
using Xunit;

namespace VividWorld.Core.Tests
{
    public class ForcedFeelingRumorPickerTests
    {
        private const string TellerId = "hero_teller";
        private const string PlayerId = "hero_player";

        private static WorldEvent CreateEvent(string id, double day, bool tellerKnows = true, bool playerKnows = false, double? forgetDay = null, double? outdatedDay = null)
        {
            var evt = new WorldEvent
            {
                EventId = id,
                Type = "hero_escaped_captivity",
                Day = day,
                Origin = EventOrigin.Public
            };

            if (tellerKnows)
            {
                var entry = new KnownByEntry
                {
                    HeroId = TellerId,
                    Hop = 1,
                    ForgetDay = forgetDay,
                    OutdatedDay = outdatedDay
                };
                evt.KnownBy.Add(entry);
            }

            if (playerKnows)
            {
                var entry = new KnownByEntry
                {
                    HeroId = PlayerId,
                    Hop = 2
                };
                evt.KnownBy.Add(entry);
            }

            return evt;
        }

        private static FeelingDecision FeelingApplied(WorldEvent evt, string lineKey = "line_key_1")
        {
            return new FeelingDecision
            {
                EventId = evt.EventId,
                SpeakerId = TellerId,
                LineKey = lineKey,
                LogLine = "applied"
            };
        }

        private static FeelingDecision FeelingNone(WorldEvent evt, string reason = "no feeling")
        {
            return new FeelingDecision
            {
                EventId = evt.EventId,
                SpeakerId = TellerId,
                LineKey = null,
                Reason = reason,
                LogLine = reason
            };
        }

        [Fact]
        public void Pick_WithFeelingAndUnheard_PicksFirst()
        {
            var evt = CreateEvent("evt_01", 10.0);
            var store = new Dictionary<string, WorldEvent> { ["evt_01"] = evt };

            var result = ForcedFeelingRumorPicker.Pick(
                new[] { "evt_01" },
                id => store.TryGetValue(id, out var e) ? e : null,
                TellerId,
                PlayerId,
                day: 10.0,
                memoryConfig: new MemoryConfig { Enabled = true },
                resolveFeeling: (e, speaker) => FeelingApplied(e));

            Assert.NotNull(result.ChosenEvent);
            Assert.Equal("evt_01", result.ChosenEvent.EventId);
            Assert.NotNull(result.ChosenFeeling);
            Assert.True(result.ChosenFeeling.Applied);
            Assert.Equal(1, result.TotalKnown);
            Assert.Equal(0, result.NoFeelingCount);
            Assert.Equal(0, result.AlreadyHeardCount);
            Assert.Equal(0, result.ForgottenCount);
            Assert.Equal(0, result.NotLoadableCount);
        }

        [Fact]
        public void Pick_AllNoFeeling_ReturnsNull()
        {
            var evt1 = CreateEvent("evt_01", 10.0);
            var evt2 = CreateEvent("evt_02", 12.0);
            var store = new Dictionary<string, WorldEvent> { ["evt_01"] = evt1, ["evt_02"] = evt2 };

            var result = ForcedFeelingRumorPicker.Pick(
                new[] { "evt_01", "evt_02" },
                id => store.TryGetValue(id, out var e) ? e : null,
                TellerId,
                PlayerId,
                day: 15.0,
                memoryConfig: new MemoryConfig { Enabled = true },
                resolveFeeling: (e, speaker) => FeelingNone(e, "no focus hero"));

            Assert.Null(result.ChosenEvent);
            Assert.Null(result.ChosenFeeling);
            Assert.Equal(2, result.TotalKnown);
            Assert.Equal(2, result.NoFeelingCount);
            Assert.Equal(0, result.AlreadyHeardCount);
            Assert.Equal(0, result.ForgottenCount);
            Assert.Equal(0, result.NotLoadableCount);
            Assert.Equal(2, result.Evaluations.Count);
            Assert.All(result.Evaluations, eval => Assert.Equal(ForcedFeelingCandidateStatus.NoFeeling, eval.Status));
        }

        [Fact]
        public void Pick_AllAlreadyHeard_ReturnsNull()
        {
            var evt1 = CreateEvent("evt_01", 10.0, playerKnows: true);
            var evt2 = CreateEvent("evt_02", 12.0, playerKnows: false); // will be marked heard via extra check
            var store = new Dictionary<string, WorldEvent> { ["evt_01"] = evt1, ["evt_02"] = evt2 };

            var result = ForcedFeelingRumorPicker.Pick(
                new[] { "evt_01", "evt_02" },
                id => store.TryGetValue(id, out var e) ? e : null,
                TellerId,
                PlayerId,
                day: 15.0,
                memoryConfig: new MemoryConfig { Enabled = true },
                resolveFeeling: (e, speaker) => FeelingApplied(e),
                isPlayerKnowerExtra: id => id == "evt_02");

            Assert.Null(result.ChosenEvent);
            Assert.Equal(2, result.TotalKnown);
            Assert.Equal(0, result.NoFeelingCount);
            Assert.Equal(2, result.AlreadyHeardCount);
            Assert.Equal(0, result.ForgottenCount);
            Assert.Equal(0, result.NotLoadableCount);
            Assert.All(result.Evaluations, eval => Assert.Equal(ForcedFeelingCandidateStatus.AlreadyHeard, eval.Status));
        }

        [Fact]
        public void Pick_OutdatedCandidate_IsEligible()
        {
            // Outdated events must still be eligible and can be chosen
            var evt = CreateEvent("evt_outdated", 10.0, outdatedDay: 12.0);
            var store = new Dictionary<string, WorldEvent> { ["evt_outdated"] = evt };

            var result = ForcedFeelingRumorPicker.Pick(
                new[] { "evt_outdated" },
                id => store.TryGetValue(id, out var e) ? e : null,
                TellerId,
                PlayerId,
                day: 15.0,
                memoryConfig: new MemoryConfig { Enabled = true },
                resolveFeeling: (e, speaker) => FeelingApplied(e));

            Assert.NotNull(result.ChosenEvent);
            Assert.Equal("evt_outdated", result.ChosenEvent.EventId);
            Assert.Equal(1, result.TotalKnown);
            Assert.Equal(0, result.NoFeelingCount);
            Assert.Equal(0, result.AlreadyHeardCount);
            Assert.Equal(0, result.ForgottenCount);
            Assert.Equal(0, result.NotLoadableCount);
        }

        [Fact]
        public void Pick_ForgottenCandidate_IsSkipped()
        {
            // Forgotten event (forgetDay <= day) is skipped; if another eligible event exists, pick that
            var evtForgotten = CreateEvent("evt_forgotten", 10.0, forgetDay: 12.0);
            var evtActive = CreateEvent("evt_active", 14.0);
            var store = new Dictionary<string, WorldEvent>
            {
                ["evt_forgotten"] = evtForgotten,
                ["evt_active"] = evtActive
            };

            var result = ForcedFeelingRumorPicker.Pick(
                new[] { "evt_forgotten", "evt_active" },
                id => store.TryGetValue(id, out var e) ? e : null,
                TellerId,
                PlayerId,
                day: 15.0,
                memoryConfig: new MemoryConfig { Enabled = true },
                resolveFeeling: (e, speaker) => FeelingApplied(e));

            Assert.NotNull(result.ChosenEvent);
            Assert.Equal("evt_active", result.ChosenEvent.EventId);
            Assert.Equal(2, result.TotalKnown);
            Assert.Equal(1, result.ForgottenCount);
            Assert.Equal(0, result.AlreadyHeardCount);
            Assert.Equal(0, result.NoFeelingCount);
            Assert.Equal(0, result.NotLoadableCount);
        }

        [Fact]
        public void Pick_DeterministicOrder_PicksNewestDayThenEventId()
        {
            // Three eligible events:
            // evt_c: day 10.0
            // evt_b: day 20.0, id "evt_b"
            // evt_a: day 20.0, id "evt_a"
            // Expected: day 20.0 beats day 10.0; between evt_a and evt_b on day 20.0, "evt_a" < "evt_b"
            var evtC = CreateEvent("evt_c", 10.0);
            var evtB = CreateEvent("evt_b", 20.0);
            var evtA = CreateEvent("evt_a", 20.0);

            var store = new Dictionary<string, WorldEvent>
            {
                ["evt_c"] = evtC,
                ["evt_b"] = evtB,
                ["evt_a"] = evtA
            };

            // Order in known list is intentionally different from sorted result
            var result = ForcedFeelingRumorPicker.Pick(
                new[] { "evt_c", "evt_b", "evt_a" },
                id => store.TryGetValue(id, out var e) ? e : null,
                TellerId,
                PlayerId,
                day: 25.0,
                memoryConfig: new MemoryConfig { Enabled = true },
                resolveFeeling: (e, speaker) => FeelingApplied(e));

            Assert.NotNull(result.ChosenEvent);
            Assert.Equal("evt_a", result.ChosenEvent.EventId);
            Assert.Equal(3, result.TotalKnown);
        }

        [Fact]
        public void Pick_NotLoadableCandidate_IsSkipped()
        {
            var evtGood = CreateEvent("evt_good", 10.0);
            var store = new Dictionary<string, WorldEvent> { ["evt_good"] = evtGood };

            var result = ForcedFeelingRumorPicker.Pick(
                new[] { "evt_missing", "evt_good" },
                id => store.TryGetValue(id, out var e) ? e : null,
                TellerId,
                PlayerId,
                day: 15.0,
                memoryConfig: new MemoryConfig { Enabled = true },
                resolveFeeling: (e, speaker) => FeelingApplied(e));

            Assert.NotNull(result.ChosenEvent);
            Assert.Equal("evt_good", result.ChosenEvent.EventId);
            Assert.Equal(2, result.TotalKnown);
            Assert.Equal(1, result.NotLoadableCount);
        }

        [Fact]
        public void Pick_EmptyKnownEvents_ReturnsEmpty()
        {
            var result = ForcedFeelingRumorPicker.Pick(
                Array.Empty<string>(),
                id => null,
                TellerId,
                PlayerId,
                day: 10.0,
                memoryConfig: new MemoryConfig(),
                resolveFeeling: (e, speaker) => FeelingApplied(e));

            Assert.Null(result.ChosenEvent);
            Assert.Equal(0, result.TotalKnown);
            Assert.Empty(result.Evaluations);
        }
    }
}
