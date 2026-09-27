#nullable enable
using System.Collections.Generic;
using VividWorld.Core.Dialogue;
using Xunit;

namespace VividWorld.Core.Tests
{
    public sealed class HeroShareLedgerTests
    {
        [Fact]
        public void HeroShareLedger_RecordsAndLimits_PerHero()
        {
            var ledger = new HeroShareLedger();
            double day1 = 10.2;
            double day2 = 11.1;

            // Initially 0
            Assert.Equal(0, ledger.SharedOn("hero_1", day1));
            Assert.False(ledger.IsAtCap("hero_1", day1, cap: 1));

            // Record once for hero_1
            int count1 = ledger.Record("hero_1", day1);
            Assert.Equal(1, count1);
            Assert.Equal(1, ledger.SharedOn("hero_1", day1));
            Assert.True(ledger.IsAtCap("hero_1", day1, cap: 1));

            // Record second time for hero_1 on same day
            int count2 = ledger.Record("hero_1", day1);
            Assert.Equal(2, count2);
            Assert.Equal(2, ledger.SharedOn("hero_1", day1));

            // Next day: resets for hero_1
            Assert.Equal(0, ledger.SharedOn("hero_1", day2));
            Assert.False(ledger.IsAtCap("hero_1", day2, cap: 1));

            // Another hero on day 1 is unaffected
            Assert.Equal(0, ledger.SharedOn("hero_2", day1));
            Assert.False(ledger.IsAtCap("hero_2", day1, cap: 1));

            // Cap <= 0 means unlimited
            Assert.False(ledger.IsAtCap("hero_1", day1, cap: 0));
            Assert.False(ledger.IsAtCap("hero_1", day1, cap: -1));

            // Record day later than today returns 0
            ledger.Record("hero_future", day2);
            Assert.Equal(0, ledger.SharedOn("hero_future", day1));
            Assert.False(ledger.IsAtCap("hero_future", day1, cap: 1));

            // Cap 2: 1st allows, 2nd allows, 3rd blocks
            var cap2Hero = "hero_cap2";
            Assert.False(ledger.IsAtCap(cap2Hero, day1, cap: 2));
            ledger.Record(cap2Hero, day1);
            Assert.False(ledger.IsAtCap(cap2Hero, day1, cap: 2));
            ledger.Record(cap2Hero, day1);
            Assert.True(ledger.IsAtCap(cap2Hero, day1, cap: 2));
        }

        [Fact]
        public void HeroShareLedger_TodaySummary_FormatsCorrectly()
        {
            var ledger = new HeroShareLedger();
            double day = 25.5;

            // Empty ledger
            string summaryEmpty = ledger.TodaySummary(day, cap: 1, _ => (string?)null);
            Assert.Equal("Shared today: 0 people (cap 1 each; 0 = no limit)", summaryEmpty);

            // Record for 3 heroes
            ledger.Record("hero_a", day);
            ledger.Record("hero_b", day);
            ledger.Record("hero_b", day);
            ledger.Record("hero_c", day);

            // Also record someone on yesterday (should not appear in today's summary)
            ledger.Record("hero_yesterday", day - 1.0);

            var names = new Dictionary<string, string>
            {
                ["hero_a"] = "Alice",
                ["hero_b"] = "Bob",
                ["hero_c"] = "Charlie"
            };

            string summary = ledger.TodaySummary(day, cap: 1, id => names.TryGetValue(id, out var n) ? n : null);
            Assert.StartsWith("Shared today: 3 people (cap 1 each; 0 = no limit) - ", summary);
            Assert.Contains("Alice (1)", summary);
            Assert.Contains("Bob (2)", summary);
            Assert.Contains("Charlie (1)", summary);
            Assert.DoesNotContain("hero_yesterday", summary);
        }

        [Fact]
        public void HeroShareLedger_TodaySummary_TruncatesAfterTenEntries()
        {
            var ledger = new HeroShareLedger();
            double day = 100.0;

            for (int i = 0; i < 15; i++)
            {
                ledger.Record($"hero_{i:D2}", day);
            }

            string summary = ledger.TodaySummary(day, cap: 2, id => $"Name_{id}");
            Assert.StartsWith("Shared today: 15 people (cap 2 each; 0 = no limit) - ", summary);
            Assert.Contains(", …and 5 more", summary);
        }

        [Fact]
        public void HeroShareLedger_LoadFrom_And_ToDictionary_PreservesEntries()
        {
            var initial = new Dictionary<string, HeroShareEntry>
            {
                ["h1"] = new HeroShareEntry { Day = 10, Count = 1 },
                ["h2"] = new HeroShareEntry { Day = 11, Count = 3 }
            };

            var ledger = new HeroShareLedger();
            ledger.LoadFrom(initial);

            var dict = ledger.ToDictionary();
            Assert.Equal(2, dict.Count);
            Assert.Equal(10, dict["h1"].Day);
            Assert.Equal(1, dict["h1"].Count);
            Assert.Equal(11, dict["h2"].Day);
            Assert.Equal(3, dict["h2"].Count);
        }

        [Fact]
        public void HeroShareLedger_CapTwo_AllowsSecondBlocksThird()
        {
            var ledger = new HeroShareLedger();
            double day = 20.4;

            ledger.Record("hero_1", day);
            Assert.False(ledger.IsAtCap("hero_1", day, 2));

            ledger.Record("hero_1", day);
            Assert.True(ledger.IsAtCap("hero_1", day, 2));

            // 上限 0 ＝ 不限：分享幾次都不會擋
            Assert.False(ledger.IsAtCap("hero_1", day, 0));
        }
    }
}
