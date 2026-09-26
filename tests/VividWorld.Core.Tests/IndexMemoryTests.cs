using System.Collections.Generic;
using VividWorld.Core.Config;
using VividWorld.Core.Events;
using VividWorld.Core.Memory;
using VividWorld.Core.Persistence;
using Xunit;

namespace VividWorld.Core.Tests
{
    public class IndexMemoryTests
    {
        [Theory]
        [InlineData(false, false, false, 10.0, 15.0)]   // 記憶關閉
        [InlineData(true, true, false, 10.0, 15.0)]    // 未洩漏秘密
        [InlineData(true, true, true, 10.0, 15.0)]     // 已洩漏秘密，過了遺忘日
        [InlineData(true, true, true, 10.0, 5.0)]      // 已洩漏秘密，未到遺忘日
        [InlineData(true, false, false, null, 15.0)]   // 沒有遺忘日
        [InlineData(true, false, false, 10.0, 10.0)]   // 遺忘日當天 (today == forgetDay)
        [InlineData(true, false, false, 10.0, 9.999)]  // 遺忘日前一刻 (today < forgetDay)
        [InlineData(true, false, false, 10.0, 10.001)] // 遺忘日過後 (today > forgetDay)
        public void Remembers_MatchesIsForgotten_UnderAllConditions(
            bool memoryEnabled,
            bool isSecret,
            bool isLeaked,
            double? forgetDay,
            double today)
        {
            var cfg = new MemoryConfig { Enabled = memoryEnabled };
            string heroId = "lord_1";
            string playerHeroId = "player";

            var evt = new WorldEvent
            {
                EventId = "evt_1",
                Day = 1.0,
                Origin = isSecret ? EventOrigin.Secret : EventOrigin.Public,
                State = new RumorState { Leaked = isLeaked }
            };

            var entry = new KnownByEntry
            {
                HeroId = heroId,
                ForgetDay = forgetDay
            };

            var indexEntry = new RumorIndexEntry
            {
                EventId = evt.EventId,
                Day = evt.Day,
                Secret = isSecret,
                Leaked = isLeaked,
                ForgetDays = forgetDay.HasValue
                    ? new Dictionary<string, double> { [heroId] = forgetDay.Value }
                    : null
            };

            bool forgotten = Forgetting.IsForgotten(evt, entry, today, playerHeroId, cfg);
            bool remembers = IndexMemory.Remembers(indexEntry, heroId, today, cfg);

            Assert.Equal(!forgotten, remembers);
        }

        [Fact]
        public void Remembers_MissingHeroInForgetDays_ReturnsTrue()
        {
            var cfg = new MemoryConfig { Enabled = true };
            var indexEntry = new RumorIndexEntry
            {
                EventId = "evt_1",
                Day = 1.0,
                ForgetDays = new Dictionary<string, double> { ["lord_other"] = 10.0 }
            };

            Assert.True(IndexMemory.Remembers(indexEntry, "lord_1", 20.0, cfg));
        }

        [Fact]
        public void Remembers_NullForgetDays_ReturnsTrue()
        {
            var cfg = new MemoryConfig { Enabled = true };
            var indexEntry = new RumorIndexEntry
            {
                EventId = "evt_1",
                Day = 1.0,
                ForgetDays = null
            };

            Assert.True(IndexMemory.Remembers(indexEntry, "lord_1", 20.0, cfg));
        }
    }
}
