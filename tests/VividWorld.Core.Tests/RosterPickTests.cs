using System;
using System.Collections.Generic;
using VividWorld.Core.Events;
using VividWorld.Core.Memory;
using Xunit;

namespace VividWorld.Core.Tests
{
    public class RosterPickTests
    {
        private static WorldEvent CreateEvent(string eventId, double day, int totalKnowers, int outdatedKnowers)
        {
            var knowers = new List<KnownByEntry>();
            for (int i = 0; i < totalKnowers; i++)
            {
                var entry = new KnownByEntry
                {
                    HeroId = $"hero_{i}",
                    Hop = 1,
                    OutdatedDay = i < outdatedKnowers ? day : null
                };
                knowers.Add(entry);
            }

            return new WorldEvent
            {
                EventId = eventId,
                Type = "test_event",
                Day = day,
                DramaWeight = 3,
                KnownBy = knowers
            };
        }

        [Fact]
        public void Choose_PrefersMixedRoster_OverLargerAllFresh()
        {
            // 一則 20 位知情者全沒過時（Layer 3）、一則 4 位裡 2 位過時（Layer 1）
            // ⇒ 挑後者（證明第 1 層蓋過知情者數）
            var eAllFresh = CreateEvent("evt_fresh_20", day: 10.0, totalKnowers: 20, outdatedKnowers: 0);
            var eMixed = CreateEvent("evt_mixed_4", day: 10.0, totalKnowers: 4, outdatedKnowers: 2);

            string? pickedForward = RosterPick.Choose(new[] { eAllFresh, eMixed });
            Assert.Equal("evt_mixed_4", pickedForward);

            string? pickedReverse = RosterPick.Choose(new[] { eMixed, eAllFresh });
            Assert.Equal("evt_mixed_4", pickedReverse);
        }

        [Fact]
        public void Choose_WhenNoOutdatedAnywhere_FallsBackToMostKnowers()
        {
            // 全部沒過時 ⇒ 挑知情者最多的
            var eFew = CreateEvent("evt_few", day: 10.0, totalKnowers: 5, outdatedKnowers: 0);
            var eMany = CreateEvent("evt_many", day: 10.0, totalKnowers: 10, outdatedKnowers: 0);

            string? picked = RosterPick.Choose(new[] { eFew, eMany });
            Assert.Equal("evt_many", picked);

            // Tiebreak 1: 同數不同日 ⇒ Day 大的優先
            var eEarly = CreateEvent("evt_early", day: 10.0, totalKnowers: 8, outdatedKnowers: 0);
            var eLate = CreateEvent("evt_late", day: 15.0, totalKnowers: 8, outdatedKnowers: 0);

            string? pickedByDay = RosterPick.Choose(new[] { eEarly, eLate });
            Assert.Equal("evt_late", pickedByDay);

            string? pickedByDayReverse = RosterPick.Choose(new[] { eLate, eEarly });
            Assert.Equal("evt_late", pickedByDayReverse);

            // Tiebreak 2: 同數同日（Math.Abs < 0.0001）不同 id ⇒ EventId 序數較小的優先
            var eIdB = CreateEvent("evt_b", day: 10.0, totalKnowers: 8, outdatedKnowers: 0);
            var eIdA = CreateEvent("evt_a", day: 10.0, totalKnowers: 8, outdatedKnowers: 0);

            string? pickedById = RosterPick.Choose(new[] { eIdB, eIdA });
            Assert.Equal("evt_a", pickedById);

            string? pickedByIdReverse = RosterPick.Choose(new[] { eIdA, eIdB });
            Assert.Equal("evt_a", pickedByIdReverse);
        }

        [Fact]
        public void Choose_PrefersMixed_OverAllOutdated()
        {
            // 一則全員過時（Layer 2）、一則混合（Layer 1） ⇒ 挑混合那則
            var eAllOutdated = CreateEvent("evt_all_outdated", day: 10.0, totalKnowers: 10, outdatedKnowers: 10);
            var eMixed = CreateEvent("evt_mixed", day: 10.0, totalKnowers: 3, outdatedKnowers: 1);

            string? picked = RosterPick.Choose(new[] { eAllOutdated, eMixed });
            Assert.Equal("evt_mixed", picked);

            string? pickedReverse = RosterPick.Choose(new[] { eMixed, eAllOutdated });
            Assert.Equal("evt_mixed", pickedReverse);
        }

        [Fact]
        public void Choose_PrefersAllOutdated_OverAllFresh()
        {
            // Layer 2（至少一位過時但非混合）優先於 Layer 3（全沒過時）
            var eAllOutdated = CreateEvent("evt_all_outdated", day: 10.0, totalKnowers: 5, outdatedKnowers: 5);
            var eAllFresh = CreateEvent("evt_fresh", day: 10.0, totalKnowers: 15, outdatedKnowers: 0);

            string? picked = RosterPick.Choose(new[] { eAllFresh, eAllOutdated });
            Assert.Equal("evt_all_outdated", picked);
        }

        [Fact]
        public void Choose_EmptyOrNull_ReturnsNull()
        {
            Assert.Null(RosterPick.Choose(null));
            Assert.Null(RosterPick.Choose(Array.Empty<WorldEvent>()));
            Assert.Null(RosterPick.Choose(new WorldEvent[] { null! }));
        }
    }
}
