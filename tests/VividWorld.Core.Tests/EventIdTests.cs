using System.Collections.Generic;
using System.Text.RegularExpressions;
using VividWorld.Core.Events;
using Xunit;

namespace VividWorld.Core.Tests
{
    public class EventIdTests
    {
        [Fact]
        public void EventId_Mint_MatchesShape()
        {
            string id = EventId.Mint(
                "covert_sabotage",
                410.5,
                new[] { "Ivan_hero_id", "Boris_hero_id", "Aldric_hero_id" },
                _ => false);

            Assert.Matches(@"^evt_\d{4}_[0-9a-f]{4}$", id);
            Assert.StartsWith("evt_0410_", id);

            // 參與者順序不應影響 id
            string id2 = EventId.Mint(
                "covert_sabotage",
                410.5,
                new[] { "Boris_hero_id", "Aldric_hero_id", "Ivan_hero_id" },
                _ => false);

            Assert.Equal(id, id2);
        }

        [Fact]
        public void EventId_Mint_ResolvesCollisionsWithIncrementingSalt()
        {
            var seen = new HashSet<string>();
            string firstCandidate = EventId.Mint("test_type", 10.0, new[] { "h1" }, _ => false);

            // 模擬第一次衝突，第二次成功
            seen.Add(firstCandidate);
            string resolvedId = EventId.Mint("test_type", 10.0, new[] { "h1" }, id => seen.Contains(id));

            Assert.NotEqual(firstCandidate, resolvedId);
            Assert.Matches(@"^evt_\d{4}_[0-9a-f]{4}$", resolvedId);
            Assert.StartsWith("evt_0010_", resolvedId);

            // 模擬多次衝突
            var takenSet = new HashSet<string>();
            int collisionCount = 0;
            string multiCollisionId = EventId.Mint("duel", 50.0, new[] { "h2" }, candidate =>
            {
                if (collisionCount < 5)
                {
                    takenSet.Add(candidate);
                    collisionCount++;
                    return true;
                }
                return false;
            });

            Assert.Matches(@"^evt_\d{4}_[0-9a-f]{4}$", multiCollisionId);
            Assert.Equal(5, takenSet.Count);
            Assert.DoesNotContain(multiCollisionId, takenSet);
        }
    }
}
