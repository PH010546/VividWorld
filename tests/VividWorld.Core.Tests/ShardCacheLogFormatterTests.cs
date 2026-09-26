#nullable enable
using VividWorld.Core.Persistence;
using VividWorld.Core.Rumors;
using Xunit;

namespace VividWorld.Core.Tests
{
    public class ShardCacheLogFormatterTests
    {
        [Fact]
        public void ShardCacheLogFormatter_FormatReleased_MatchesDocumentedPattern()
        {
            var keys = new[] { "d0000-0099", "d0100-0199" };
            string formatted = ShardCacheLogFormatter.FormatReleased(2, 8, keys, 3, 25);
            Assert.Equal(
                "Shard cache: released 2 shard(s) idle for 8+ flushes (d0000-0099, d0100-0199); 3 shard(s) / 25 event(s) still cached",
                formatted);
        }

        [Fact]
        public void ShardCacheLogFormatter_FormatWorldStatus_MatchesDocumentedPattern()
        {
            // k > 0
            string active = ShardCacheLogFormatter.FormatWorldStatus(3, 25, 5, 8);
            Assert.Equal(
                "Shard cache: 3 shard(s) / 25 event(s) in memory, 5 released so far (release after 8 idle flushes)",
                active);

            // k == 0 (disabled)
            string disabled = ShardCacheLogFormatter.FormatWorldStatus(3, 25, 5, 0);
            Assert.Equal(
                "Shard cache: 3 shard(s) / 25 event(s) in memory, 5 released so far (release after 0 idle flushes, disabled)",
                disabled);
        }

        [Fact]
        public void TellerLogFormatter_FormatRebuilt_MatchesDocumentedPattern()
        {
            string formatted = TellerLogFormatter.FormatRebuilt(309, 34);
            Assert.Equal(
                "Teller ring: rebuilt with 309 teller(s) from the index, 34 knower(s) left out because they forgot everything they knew",
                formatted);

            // k 為 0 也印
            string zeroSkipped = TellerLogFormatter.FormatRebuilt(343, 0);
            Assert.Equal(
                "Teller ring: rebuilt with 343 teller(s) from the index, 0 knower(s) left out because they forgot everything they knew",
                zeroSkipped);
        }
    }
}
