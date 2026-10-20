using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace VividWorld.Core.Diagnostics
{
    /// <summary>
    /// Breakdown timing in milliseconds and batch size for an hourly tick execution.
    /// </summary>
    public readonly struct HourlySubMetrics
    {
        public double LookupMs { get; }
        public double ChooseMs { get; }
        public double PropagateMs { get; }
        public double OtherMs { get; }
        public int TellerCount { get; }

        public HourlySubMetrics(double lookupMs, double chooseMs, double propagateMs, double otherMs, int tellerCount)
        {
            LookupMs = Math.Max(0.0, lookupMs);
            ChooseMs = Math.Max(0.0, chooseMs);
            PropagateMs = Math.Max(0.0, propagateMs);
            OtherMs = Math.Max(0.0, otherMs);
            TellerCount = Math.Max(0, tellerCount);
        }
    }

    /// <summary>
    /// Details about the peak execution for a scope.
    /// </summary>
    public sealed class ScopePeakDetail
    {
        public long CallIndex { get; set; }
        public double Day { get; set; }
        public int HourOfDay { get; set; }
        public bool FirstAfterLoad { get; set; }
        public HourlySubMetrics? HourlySub { get; set; }

        public ScopePeakDetail(long callIndex, double day, int hourOfDay, bool firstAfterLoad, HourlySubMetrics? hourlySub = null)
        {
            CallIndex = callIndex;
            Day = day;
            HourOfDay = hourOfDay;
            FirstAfterLoad = firstAfterLoad;
            HourlySub = hourlySub;
        }
    }

    /// <summary>
    /// Snapshot of statistics for a single scope (either session-wide or daily).
    /// </summary>
    public sealed class ScopeStatsSnapshot
    {
        public string Scope { get; }
        public long CallCount { get; }
        public double TotalMilliseconds { get; }
        public double PeakMilliseconds { get; }
        public ScopePeakDetail? PeakDetail { get; }

        public ScopeStatsSnapshot(string scope, long callCount, double totalMilliseconds, double peakMilliseconds, ScopePeakDetail? peakDetail)
        {
            Scope = scope ?? string.Empty;
            CallCount = callCount;
            TotalMilliseconds = totalMilliseconds;
            PeakMilliseconds = peakMilliseconds;
            PeakDetail = peakDetail;
        }
    }

    /// <summary>
    /// World size counts for daily performance log.
    /// Missing values can be represented with null.
    /// </summary>
    public readonly struct WorldScaleMetrics
    {
        public int? TellerRing { get; }
        public int? ActiveRumors { get; }
        public int? SecretsWatched { get; }
        public int? EventsOnDisk { get; }
        public string? ReasonMissing { get; }

        public WorldScaleMetrics(int? tellerRing, int? activeRumors, int? secretsWatched, int? eventsOnDisk, string? reasonMissing = null)
        {
            TellerRing = tellerRing;
            ActiveRumors = activeRumors;
            SecretsWatched = secretsWatched;
            EventsOnDisk = eventsOnDisk;
            ReasonMissing = reasonMissing;
        }
    }

    /// <summary>
    /// Thread-safe accumulator for a single scope's metrics across session and daily periods.
    /// </summary>
    public sealed class ScopeMetricTracker
    {
        private readonly object _sync = new object();
        private readonly string _scope;

        private long _sessionCalls;
        private double _sessionTotalMs;
        private double _sessionPeakMs;
        private ScopePeakDetail? _sessionPeakDetail;

        private long _dailyCalls;
        private double _dailyTotalMs;
        private double _dailyPeakMs;
        private ScopePeakDetail? _dailyPeakDetail;

        public string Scope => _scope;

        public ScopeMetricTracker(string scope)
        {
            _scope = scope ?? string.Empty;
        }

        public void Record(double elapsedMs, double day, int hourOfDay, bool firstAfterLoad, HourlySubMetrics? hourlySub = null)
        {
            lock (_sync)
            {
                _sessionCalls++;
                _sessionTotalMs += elapsedMs;
                if (_sessionCalls == 1 || elapsedMs > _sessionPeakMs)
                {
                    _sessionPeakMs = elapsedMs;
                    _sessionPeakDetail = new ScopePeakDetail(_sessionCalls, day, hourOfDay, firstAfterLoad, hourlySub);
                }

                _dailyCalls++;
                _dailyTotalMs += elapsedMs;
                if (_dailyCalls == 1 || elapsedMs > _dailyPeakMs)
                {
                    _dailyPeakMs = elapsedMs;
                    _dailyPeakDetail = new ScopePeakDetail(_sessionCalls, day, hourOfDay, firstAfterLoad, hourlySub);
                }
            }
        }

        public ScopeStatsSnapshot SnapshotSession()
        {
            lock (_sync)
            {
                return new ScopeStatsSnapshot(_scope, _sessionCalls, _sessionTotalMs, _sessionPeakMs, _sessionPeakDetail);
            }
        }

        public ScopeStatsSnapshot SnapshotAndResetDaily()
        {
            lock (_sync)
            {
                var snap = new ScopeStatsSnapshot(_scope, _dailyCalls, _dailyTotalMs, _dailyPeakMs, _dailyPeakDetail);
                _dailyCalls = 0;
                _dailyTotalMs = 0.0;
                _dailyPeakMs = 0.0;
                _dailyPeakDetail = null;
                return snap;
            }
        }

        public void ResetAll()
        {
            lock (_sync)
            {
                _sessionCalls = 0;
                _sessionTotalMs = 0.0;
                _sessionPeakMs = 0.0;
                _sessionPeakDetail = null;

                _dailyCalls = 0;
                _dailyTotalMs = 0.0;
                _dailyPeakMs = 0.0;
                _dailyPeakDetail = null;
            }
        }
    }
}
