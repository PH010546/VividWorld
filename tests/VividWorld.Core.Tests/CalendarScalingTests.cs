using VividWorld.Core.Config;
using Xunit;

namespace VividWorld.Core.Tests
{
    public class CalendarScalingTests
    {
        [Fact]
        public void CalendarScale_NativeCalendar_LeavesEveryDurationUnchanged()
        {
            var config = new VividWorldConfig();
            config.Normalize();

            double origHalfLife = config.Leak.ChanceDecayHalfLifeDays;
            double origLifetime = config.Scheduling.RumorLifetimeDays;
            double origStale = config.Scheduling.StaleDays;
            double origSecretWatch = config.Scheduling.SecretWatchDays;
            double origCooldown = config.Dialogue.VolunteerCooldownDays;
            double origMemoryBaseDays = config.Memory.BaseDays;
            double origMemoryMinDays = config.Memory.MinDays;

            CalendarScaling.Apply(config, 84.0);

            Assert.Equal(origHalfLife, config.Leak.ChanceDecayHalfLifeDays);
            Assert.Equal(origLifetime, config.Scheduling.RumorLifetimeDays);
            Assert.Equal(origStale, config.Scheduling.StaleDays);
            Assert.Equal(origSecretWatch, config.Scheduling.SecretWatchDays);
            Assert.Equal(origCooldown, config.Dialogue.VolunteerCooldownDays);
            Assert.Equal(origMemoryBaseDays, config.Memory.BaseDays);
            Assert.Equal(origMemoryMinDays, config.Memory.MinDays);
        }

        [Fact]
        public void CalendarScale_FastModeCalendar_ScalesNarrativeDurationsOnly()
        {
            var config = new VividWorldConfig();
            config.Normalize();

            CalendarScaling.Apply(config, 28.0);

            // 84 -> 28 => scale = 1/3
            Assert.Equal(40.0, config.Scheduling.RumorLifetimeDays, 4);
            Assert.Equal(10.0, config.Scheduling.StaleDays, 4);
            Assert.Equal(84.0, config.Scheduling.SecretWatchDays, 4);
            Assert.Equal(20.0, config.Leak.ChanceDecayHalfLifeDays, 4);
            Assert.Equal(1.0, config.Dialogue.VolunteerCooldownDays, 4);
            Assert.Equal(20.0, config.Memory.BaseDays, 4);

            // 非敘事型時長原封不動
            Assert.Equal(1.0, config.Memory.MinDays);
            Assert.Equal(1.0, config.Leak.GraceDays);
            Assert.Equal(6, config.Scheduling.FlushIntervalHours);
            Assert.Equal(4, config.Scheduling.EventsPerHourlyTick);
        }

        [Fact]
        public void CalendarScale_Disabled_LeavesConfigLiteral()
        {
            var config = new VividWorldConfig();
            config.Normalize();
            config.Scheduling.ScaleDurationsToGameCalendar = false;

            double origLifetime = config.Scheduling.RumorLifetimeDays;
            double origHalfLife = config.Leak.ChanceDecayHalfLifeDays;
            double origMemoryBaseDays = config.Memory.BaseDays;
            double origMemoryMinDays = config.Memory.MinDays;

            CalendarScaling.Apply(config, 28.0);

            Assert.Equal(origLifetime, config.Scheduling.RumorLifetimeDays);
            Assert.Equal(origHalfLife, config.Leak.ChanceDecayHalfLifeDays);
            Assert.Equal(origMemoryBaseDays, config.Memory.BaseDays);
            Assert.Equal(origMemoryMinDays, config.Memory.MinDays);
        }

        [Fact]
        public void CalendarScale_NonPositiveDaysInYear_IsIgnored()
        {
            var config = new VividWorldConfig();
            config.Normalize();

            double origLifetime = config.Scheduling.RumorLifetimeDays;
            double origHalfLife = config.Leak.ChanceDecayHalfLifeDays;

            CalendarScaling.Apply(config, 0.0);
            Assert.Equal(origLifetime, config.Scheduling.RumorLifetimeDays);
            Assert.Equal(origHalfLife, config.Leak.ChanceDecayHalfLifeDays);

            CalendarScaling.Apply(config, -50.0);
            Assert.Equal(origLifetime, config.Scheduling.RumorLifetimeDays);
            Assert.Equal(origHalfLife, config.Leak.ChanceDecayHalfLifeDays);
        }

        [Fact]
        public void CalendarScale_ConsequencesSettings_AreNeverScaled()
        {
            var config = new VividWorldConfig();
            config.Normalize();

            double origBystander = config.Consequences.BystanderMultiplier;
            double origDailyBudget = config.Consequences.MaxAbsoluteDeltaPerHeroPerDay;
            int origMinDelta = config.Consequences.MinAbsoluteDelta;
            double[] origHopConfidence = (double[])config.Consequences.HopConfidence.Clone();

            // 84 -> 28 => scale = 1/3 in FastMode
            CalendarScaling.Apply(config, 28.0);

            Assert.Equal(origBystander, config.Consequences.BystanderMultiplier);
            Assert.Equal(origDailyBudget, config.Consequences.MaxAbsoluteDeltaPerHeroPerDay);
            Assert.Equal(origMinDelta, config.Consequences.MinAbsoluteDelta);
            Assert.Equal(origHopConfidence, config.Consequences.HopConfidence);
        }
    }
}
