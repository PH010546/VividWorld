#nullable enable
using System.Collections.Generic;
using VividWorld.Core.Config;
using VividWorld.Core.Persistence;
using VividWorld.Core.Rumors;
using Xunit;

namespace VividWorld.Core.Tests
{
    public class TellerRosterTests
    {
        private static RumorIndexEntry CreateEntry(
            string eventId,
            double day,
            bool dormant = false,
            bool secret = false,
            bool leaked = false,
            List<string>? knowers = null,
            Dictionary<string, double>? forgetDays = null)
        {
            var entry = new RumorIndexEntry
            {
                EventId = eventId,
                Day = day,
                Dormant = dormant,
                Secret = secret,
                Leaked = leaked,
                KnownByHeroIds = knowers ?? new List<string>(),
                ForgetDays = forgetDays ?? new Dictionary<string, double>()
            };
            return entry;
        }

        [Fact]
        public void TellerRoster_ForgottenInOneEvent_RememberedInAnother_EntersRing()
        {
            // A 在未休眠事件 X 裡已忘 (today 20 >= 10)、在 Y 裡還記得 (today 20 < 30) ⇒ 進輪，不計入 skipped
            var index = new RumorIndex();
            index.Upsert(CreateEntry("evt_X", 5, dormant: false, knowers: new List<string> { "hero_A" },
                forgetDays: new Dictionary<string, double> { { "hero_A", 10.0 } }));
            index.Upsert(CreateEntry("evt_Y", 15, dormant: false, knowers: new List<string> { "hero_A" },
                forgetDays: new Dictionary<string, double> { { "hero_A", 30.0 } }));

            var memoryConfig = new MemoryConfig { Enabled = true };
            var (tellerIds, skipped) = TellerRoster.Collect(index, today: 20.0, playerHeroId: "player", memoryConfig);

            Assert.Contains("hero_A", tellerIds);
            Assert.Equal(0, skipped);
        }

        [Fact]
        public void TellerRoster_OnlyInOneEventAndForgotten_LeftOutOfRing_IncrementsSkipped()
        {
            // B 只在 X 裡且已忘 (today 20 >= 10) ⇒ 不進輪、計數 1
            var index = new RumorIndex();
            index.Upsert(CreateEntry("evt_X", 5, dormant: false, knowers: new List<string> { "hero_B" },
                forgetDays: new Dictionary<string, double> { { "hero_B", 10.0 } }));

            var memoryConfig = new MemoryConfig { Enabled = true };
            var (tellerIds, skipped) = TellerRoster.Collect(index, today: 20.0, playerHeroId: "player", memoryConfig);

            Assert.DoesNotContain("hero_B", tellerIds);
            Assert.Equal(1, skipped);
        }

        [Fact]
        public void TellerRoster_WithoutForgetDays_EntersRing()
        {
            // C 沒有 forgetDays ⇒ 進輪
            var index = new RumorIndex();
            index.Upsert(CreateEntry("evt_X", 5, dormant: false, knowers: new List<string> { "hero_C" },
                forgetDays: null));

            var memoryConfig = new MemoryConfig { Enabled = true };
            var (tellerIds, skipped) = TellerRoster.Collect(index, today: 20.0, playerHeroId: "player", memoryConfig);

            Assert.Contains("hero_C", tellerIds);
            Assert.Equal(0, skipped);
        }

        [Fact]
        public void TellerRoster_WhenMemoryDisabled_AllKnowersEnterRing_SkippedIsZero()
        {
            // 記憶關掉 ⇒ 全部進輪
            var index = new RumorIndex();
            index.Upsert(CreateEntry("evt_X", 5, dormant: false, knowers: new List<string> { "hero_A", "hero_B" },
                forgetDays: new Dictionary<string, double> { { "hero_A", 10.0 }, { "hero_B", 10.0 } }));

            var memoryConfig = new MemoryConfig { Enabled = false };
            var (tellerIds, skipped) = TellerRoster.Collect(index, today: 20.0, playerHeroId: "player", memoryConfig);

            Assert.Contains("hero_A", tellerIds);
            Assert.Contains("hero_B", tellerIds);
            Assert.Equal(0, skipped);
        }

        [Fact]
        public void TellerRoster_KnowerOnlyInDormantEvent_DoesNotEnterRing_AndNotCountedAsSkipped()
        {
            // 只出現在休眠事件裡的人 ⇒ 跟現在一樣不進輪（且不計為 skipped）
            var index = new RumorIndex();
            index.Upsert(CreateEntry("evt_Dormant", 5, dormant: true, knowers: new List<string> { "hero_D" },
                forgetDays: new Dictionary<string, double> { { "hero_D", 5.0 } }));

            var memoryConfig = new MemoryConfig { Enabled = true };
            var (tellerIds, skipped) = TellerRoster.Collect(index, today: 20.0, playerHeroId: "player", memoryConfig);

            Assert.DoesNotContain("hero_D", tellerIds);
            Assert.Equal(0, skipped);
        }

        [Fact]
        public void TellerRoster_ExcludesPlayer_AndExcludesUnleakedSecret()
        {
            var index = new RumorIndex();
            // 玩家知情者
            index.Upsert(CreateEntry("evt_1", 5, dormant: false, knowers: new List<string> { "player", "hero_E" }));
            // 未洩漏的秘密
            index.Upsert(CreateEntry("evt_Secret", 5, dormant: false, secret: true, leaked: false, knowers: new List<string> { "hero_Secret" }));

            var memoryConfig = new MemoryConfig { Enabled = true };
            var (tellerIds, skipped) = TellerRoster.Collect(index, today: 10.0, playerHeroId: "player", memoryConfig);

            Assert.DoesNotContain("player", tellerIds);
            Assert.Contains("hero_E", tellerIds);
            Assert.DoesNotContain("hero_Secret", tellerIds);
            Assert.Equal(0, skipped);
        }
    }
}
