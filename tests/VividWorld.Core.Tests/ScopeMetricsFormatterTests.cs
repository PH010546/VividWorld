using System.Collections.Generic;
using VividWorld.Core.Diagnostics;
using Xunit;

namespace VividWorld.Core.Tests
{
    public class ScopeMetricsFormatterTests
    {
        [Fact]
        public void Detail_SubMillisecondAverage_KeepsTheDecimalPoint()
        {
            string result = ScopeMetricsFormatter.Detail("hourly", 100, 32.6, 1.1);
            Assert.Contains("avg 0.33ms", result);
            Assert.Contains("peak 1.10ms", result);
            Assert.DoesNotContain("avg 01ms", result);
        }

        [Fact]
        public void Detail_ZeroCalls_RendersZerosNotBlanks()
        {
            string result = ScopeMetricsFormatter.Detail("daily", 0, 0.0, 0.0);
            Assert.Contains("0 calls, total 0.00ms, avg 0.00ms, peak 0.00ms", result);
        }

        [Fact]
        public void Compact_OmitsPeakWhenItMatchesTheAverage()
        {
            string result = ScopeMetricsFormatter.Compact("contacts", 300, 15.0, 0.052);
            Assert.Equal("contacts 0.1ms x 300", result);
            Assert.DoesNotContain("peak", result);
        }

        [Fact]
        public void Tracker_DailyResets_WhileSessionAccumulates()
        {
            var tracker = new ScopeMetricTracker("hourly");

            // Day 1: two calls
            tracker.Record(2.0, 10.0, 1, false, null);
            tracker.Record(5.0, 10.0, 2, false, null);

            var day1Snap = tracker.SnapshotAndResetDaily();
            Assert.Equal(2, day1Snap.CallCount);
            Assert.Equal(7.0, day1Snap.TotalMilliseconds);
            Assert.Equal(5.0, day1Snap.PeakMilliseconds);

            // Day 2: one call
            tracker.Record(3.0, 11.0, 0, false, null);

            var day2Snap = tracker.SnapshotAndResetDaily();
            Assert.Equal(1, day2Snap.CallCount);
            Assert.Equal(3.0, day2Snap.TotalMilliseconds);
            Assert.Equal(3.0, day2Snap.PeakMilliseconds);

            // Session snapshot should have all 3 calls and peak of 5.0
            var sessionSnap = tracker.SnapshotSession();
            Assert.Equal(3, sessionSnap.CallCount);
            Assert.Equal(10.0, sessionSnap.TotalMilliseconds);
            Assert.Equal(5.0, sessionSnap.PeakMilliseconds);
            Assert.NotNull(sessionSnap.PeakDetail);
            Assert.Equal(2, sessionSnap.PeakDetail.CallIndex); // peak was call #2
        }

        [Fact]
        public void Tracker_PeakDetailUpdatesWithNewPeak()
        {
            var tracker = new ScopeMetricTracker("hourly");

            var sub1 = new HourlySubMetrics(1.0, 0.5, 0.5, 0.2, 4);
            tracker.Record(2.2, 5.0, 10, true, sub1);

            var snap1 = tracker.SnapshotSession();
            Assert.Equal(2.2, snap1.PeakMilliseconds);
            Assert.NotNull(snap1.PeakDetail);
            Assert.Equal(1, snap1.PeakDetail.CallIndex);
            Assert.Equal(10, snap1.PeakDetail.HourOfDay);
            Assert.True(snap1.PeakDetail.FirstAfterLoad);
            Assert.NotNull(snap1.PeakDetail.HourlySub);
            Assert.Equal(4, snap1.PeakDetail.HourlySub.Value.TellerCount);

            // A lower call should not replace peak detail
            var sub2 = new HourlySubMetrics(0.5, 0.2, 0.3, 0.1, 2);
            tracker.Record(1.1, 5.0, 11, false, sub2);

            var snap2 = tracker.SnapshotSession();
            Assert.Equal(2.2, snap2.PeakMilliseconds);
            Assert.NotNull(snap2.PeakDetail);
            Assert.Equal(1, snap2.PeakDetail.CallIndex);

            // A higher call should replace peak detail
            var sub3 = new HourlySubMetrics(3.2, 1.1, 5.0, 0.5, 6);
            tracker.Record(9.8, 5.0, 14, false, sub3);

            var snap3 = tracker.SnapshotSession();
            Assert.Equal(9.8, snap3.PeakMilliseconds);
            Assert.NotNull(snap3.PeakDetail);
            Assert.Equal(3, snap3.PeakDetail.CallIndex);
            Assert.Equal(14, snap3.PeakDetail.HourOfDay);
            Assert.False(snap3.PeakDetail.FirstAfterLoad);
            Assert.NotNull(snap3.PeakDetail.HourlySub);
            Assert.Equal(6, snap3.PeakDetail.HourlySub.Value.TellerCount);
            Assert.Equal(3.2, snap3.PeakDetail.HourlySub.Value.LookupMs);
        }

        [Fact]
        public void PeakDetail_FirstAfterLoadMarker_IsFormattedCorrectly()
        {
            var detail = new ScopePeakDetail(1, 20.0, 8, true, null);
            string suffix = ScopeMetricsFormatter.FormatPeakDetailSuffix(detail);

            Assert.Contains("h8", suffix);
            Assert.Contains("call #1", suffix);
            Assert.Contains("first after load", suffix);
        }

        [Fact]
        public void DailyLine_ZeroCallScopes_RenderAs0x()
        {
            var scopes = new List<ScopeStatsSnapshot>
            {
                new ScopeStatsSnapshot("hourly", 24, 50.4, 9.8, new ScopePeakDetail(15, 131, 14, false, new HourlySubMetrics(3.2, 1.1, 5.0, 0.5, 6))),
                new ScopeStatsSnapshot("daily", 1, 4.0, 4.0, null),
                new ScopeStatsSnapshot("snapshot", 0, 0.0, 0.0, null),
                new ScopeStatsSnapshot("purge", 0, 0.0, 0.0, null),
                new ScopeStatsSnapshot("load", 0, 0.0, 0.0, null)
            };

            var world = new WorldScaleMetrics(471, 380, 40, 2210);
            string line = ScopeMetricsFormatter.FormatDailyLine(131, scopes, world);

            Assert.StartsWith("Perf day 131:", line);
            Assert.Contains("hourly 24x avg 2.10 max 9.80ms", line);
            Assert.Contains("daily 1x 4.00ms", line);
            Assert.Contains("snapshot 0x", line);
            Assert.Contains("purge 0x", line);
            Assert.Contains("load 0x", line);
            Assert.Contains("world: teller ring 471, active rumors 380, secrets watched 40, events on disk 2210", line);
        }

        [Fact]
        public void DailyLine_UnreadableWorldScale_RendersQuestionMarkWithReason()
        {
            var scopes = new List<ScopeStatsSnapshot>
            {
                new ScopeStatsSnapshot("load", 0, 0.0, 0.0, null)
            };

            var world = new WorldScaleMetrics(null, 380, 40, 2210, "scheduler not ready");
            string line = ScopeMetricsFormatter.FormatDailyLine(1, scopes, world);

            Assert.Contains("teller ring ?", line);
            Assert.Contains("active rumors 380", line);
            Assert.Contains("(scheduler not ready)", line);
        }

        [Fact]
        public void LoadLine_RendersDurationAndCounts()
        {
            string line = ScopeMetricsFormatter.FormatLoadLine(12.50, 380, 471);
            Assert.Equal("Load: 12.50ms (index 380 events, roster 471 tellers)", line);
        }
    }
}
