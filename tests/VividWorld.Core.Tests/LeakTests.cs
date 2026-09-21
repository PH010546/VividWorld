using System;
using System.Collections.Generic;
using VividWorld.Core.Config;
using VividWorld.Core.Events;
using VividWorld.Core.Rumors;
using VividWorld.Core.Tests.Fakes;
using VividWorld.Core.Util;
using Xunit;

namespace VividWorld.Core.Tests
{
    public class LeakTests
    {
        [Fact]
        public void Leak_WithinGraceDays_NeverFires()
        {
            var cfg = new LeakConfig { GraceDays = 1.0, BaseChancePerInsiderPerDay = 1.0 };
            var rng = new SplitMix64Rng();
            var evt = new WorldEvent
            {
                EventId = "evt_secret_1",
                Origin = EventOrigin.Secret,
                Day = 10.0
            };

            var insiders = new List<TraitProfile>
            {
                new TraitProfile
                {
                    HeroId = "hero_dishonourable",
                    Honor = -2,
                    Calculating = -2,
                    Generosity = 2
                }
            };

            // day within grace period [10.0, 11.0)
            var outcome1 = LeakRoll.Roll(evt, 10.0, insiders, cfg, rng, 12345L);
            var outcome2 = LeakRoll.Roll(evt, 10.5, insiders, cfg, rng, 12345L);
            var outcome3 = LeakRoll.Roll(evt, 10.999, insiders, cfg, rng, 12345L);

            Assert.False(outcome1.Leaked);
            Assert.False(outcome2.Leaked);
            Assert.False(outcome3.Leaked);
        }

        [Fact]
        public void Leak_SameDayReRoll_ReturnsTheSameAnswer()
        {
            var cfg = new LeakConfig();
            var rng = new SplitMix64Rng();
            var evt = new WorldEvent
            {
                EventId = "evt_secret_2",
                Origin = EventOrigin.Secret,
                Day = 1.0
            };

            var insiders = new List<TraitProfile>
            {
                new TraitProfile { HeroId = "hero_1", Honor = -1, Calculating = 0, Generosity = 1 }
            };

            long campaignSeed = 987654321L;
            var outcomeMorning = LeakRoll.Roll(evt, 5.2, insiders, cfg, rng, campaignSeed);
            var outcomeEvening = LeakRoll.Roll(evt, 5.8, insiders, cfg, rng, campaignSeed);

            Assert.Equal(outcomeMorning.Leaked, outcomeEvening.Leaked);
            Assert.Equal(outcomeMorning.LeakerHeroId, outcomeEvening.LeakerHeroId);
        }

        [Fact]
        public void Leak_DishonourableInsider_LeaksSoonerThanHonourable()
        {
            var cfg = new LeakConfig();
            var rng = new SplitMix64Rng();
            var evt = new WorldEvent
            {
                EventId = "evt_secret_3",
                Origin = EventOrigin.Secret,
                Day = 0.0
            };

            var dishonourable = new List<TraitProfile>
            {
                new TraitProfile { HeroId = "dishonourable", Honor = -2, Calculating = -2, Generosity = 2 }
            };

            var honourable = new List<TraitProfile>
            {
                new TraitProfile { HeroId = "honourable", Honor = 2, Calculating = 2, Generosity = -2 }
            };

            int dishonourableLeaks = 0;
            int honourableLeaks = 0;
            long dishonourableDaysSum = 0;
            long honourableDaysSum = 0;

            const int totalSeeds = 500;
            const int maxDays = 150;

            for (long seed = 1; seed <= totalSeeds; seed++)
            {
                // Dishonourable run
                for (int d = 2; d <= maxDays; d++)
                {
                    var outcome = LeakRoll.Roll(evt, d, dishonourable, cfg, rng, seed);
                    if (outcome.Leaked)
                    {
                        dishonourableLeaks++;
                        dishonourableDaysSum += d;
                        break;
                    }
                }

                // Honourable run
                for (int d = 2; d <= maxDays; d++)
                {
                    var outcome = LeakRoll.Roll(evt, d, honourable, cfg, rng, seed);
                    if (outcome.Leaked)
                    {
                        honourableLeaks++;
                        honourableDaysSum += d;
                        break;
                    }
                }
            }

            Assert.True(dishonourableLeaks > honourableLeaks,
                $"Dishonourable ({dishonourableLeaks}) should leak in more seeds than Honourable ({honourableLeaks}).");

            double avgDishonourableDays = (double)dishonourableDaysSum / Math.Max(1, dishonourableLeaks);
            double avgHonourableDays = (double)honourableDaysSum / Math.Max(1, honourableLeaks);
            Assert.True(avgDishonourableDays < avgHonourableDays,
                $"Dishonourable avg leak day ({avgDishonourableDays:F1}) should be sooner than Honourable ({avgHonourableDays:F1}).");
        }

        [Fact]
        public void Leak_CalculatingInsider_KeepsTheSecretLonger()
        {
            var cfg = new LeakConfig();
            var rng = new SplitMix64Rng();
            var evt = new WorldEvent
            {
                EventId = "evt_secret_4",
                Origin = EventOrigin.Secret,
                Day = 0.0
            };

            var highCalculating = new List<TraitProfile>
            {
                new TraitProfile { HeroId = "high_calc", Honor = 0, Calculating = 2, Generosity = 0 }
            };

            var lowCalculating = new List<TraitProfile>
            {
                new TraitProfile { HeroId = "low_calc", Honor = 0, Calculating = -2, Generosity = 0 }
            };

            int highCalcLeaks = 0;
            int lowCalcLeaks = 0;

            const int totalSeeds = 500;
            const int maxDays = 150;

            for (long seed = 1; seed <= totalSeeds; seed++)
            {
                for (int d = 2; d <= maxDays; d++)
                {
                    var outcomeLow = LeakRoll.Roll(evt, d, lowCalculating, cfg, rng, seed);
                    if (outcomeLow.Leaked)
                    {
                        lowCalcLeaks++;
                        break;
                    }
                }

                for (int d = 2; d <= maxDays; d++)
                {
                    var outcomeHigh = LeakRoll.Roll(evt, d, highCalculating, cfg, rng, seed);
                    if (outcomeHigh.Leaked)
                    {
                        highCalcLeaks++;
                        break;
                    }
                }
            }

            Assert.True(lowCalcLeaks > highCalcLeaks,
                $"Low calculating ({lowCalcLeaks}) should leak more often than High calculating ({highCalcLeaks}).");
        }

        [Fact]
        public void Leak_HighHonorHighCalculatingSoleInsider_OftenSurvivesAThousandDays()
        {
            var cfg = new LeakConfig();
            var rng = new SplitMix64Rng();
            var evt = new WorldEvent
            {
                EventId = "evt_secret_thousand_days",
                Origin = EventOrigin.Secret,
                Day = 0.0
            };

            // 明確指定特質組合：Honor = 2, Calculating = 2, Generosity = 0 (乘數 0.3)
            var soleInsider = new List<TraitProfile>
            {
                new TraitProfile { HeroId = "hero_cautious_lord", Honor = 2, Calculating = 2, Generosity = 0 }
            };

            const int totalSeeds = 500;
            int survivedCount = 0;

            for (long seed = 1; seed <= totalSeeds; seed++)
            {
                bool survived = true;
                for (int d = 2; d <= 1000; d++)
                {
                    var outcome = LeakRoll.Roll(evt, d, soleInsider, cfg, rng, seed);
                    if (outcome.Leaked)
                    {
                        survived = false;
                        break;
                    }
                }
                if (survived)
                {
                    survivedCount++;
                }
            }

            double survivalRate = (double)survivedCount / totalSeeds;
            // 規格 §6.4 與卡片 §5.3：斷言保密率落在 60–85%（理論值約 73.2%）
            Assert.InRange(survivalRate, 0.60, 0.85);
        }

        [Fact]
        public void Leak_OnSuccess_RecordsLeakerAndDay_ButDoesNotAddKnowers()
        {
            var cfg = new VividWorldConfig();
            var rng = new SplitMix64Rng();
            var traits = new FakeHeroTraitLookup();
            var channel = new FakePropagationChannel();
            var retention = FactRetentionPolicies.Create(cfg, rng, 1L);
            var embellishment = NullEmbellishmentPolicy.Instance;

            traits.Set(new TraitProfile { HeroId = "insider_1", Honor = -2, Calculating = -2, Generosity = 2, IsAlive = true, IsLord = true });
            traits.Set(new TraitProfile { HeroId = "insider_2", Honor = -2, Calculating = -2, Generosity = 2, IsAlive = true, IsLord = true });

            var evt = new WorldEvent
            {
                EventId = "evt_secret_test",
                Origin = EventOrigin.Secret,
                Day = 0.0,
                KnownBy = new List<KnownByEntry>
                {
                    new KnownByEntry { HeroId = "insider_1", Hop = 0, LearnedDay = 0.0 },
                    new KnownByEntry { HeroId = "insider_2", Hop = 0, LearnedDay = 0.0 }
                }
            };

            // Find a campaign seed where leak triggers at day 2
            long winningSeed = -1;
            for (long s = 1; s <= 1000; s++)
            {
                var testEngine = new RumorEngine(cfg, retention, embellishment, channel, traits, rng, s, "player");
                var testEvt = new WorldEvent
                {
                    EventId = evt.EventId,
                    Origin = EventOrigin.Secret,
                    Day = 0.0,
                    KnownBy = new List<KnownByEntry>(evt.KnownBy)
                };

                var outcome = testEngine.TryLeak(testEvt, 2.0);
                if (outcome.Leaked)
                {
                    winningSeed = s;
                    break;
                }
            }

            Assert.True(winningSeed > 0, "Should find a winning campaign seed that triggers leak.");

            var engine = new RumorEngine(cfg, retention, embellishment, channel, traits, rng, winningSeed, "player");
            var result = engine.TryLeak(evt, 2.0);

            Assert.True(result.Leaked);
            Assert.NotNull(result.LeakerHeroId);
            Assert.True(evt.State.Leaked);
            Assert.Equal(2.0, evt.State.LeakedDay);
            Assert.Equal(result.LeakerHeroId, evt.State.LeakerHeroId);

            // 斷言不動 KnownBy
            Assert.Equal(2, evt.KnownBy.Count);
            Assert.Equal("insider_1", evt.KnownBy[0].HeroId);
            Assert.Equal("insider_2", evt.KnownBy[1].HeroId);
        }
    }
}
