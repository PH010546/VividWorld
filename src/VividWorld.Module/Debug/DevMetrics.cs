using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Threading;
using VividWorld.Core.Diagnostics;

namespace VividWorld.Debug
{
    internal static class DevMetrics
    {
        private static readonly object Sync = new object();
        private static bool _enabled = false;
        private static long _relationCallCount = 0;

        private static readonly Dictionary<string, ScopeMetricTracker> _trackers =
            new Dictionary<string, ScopeMetricTracker>(StringComparer.OrdinalIgnoreCase);

        // 每小時那一輪會更新這兩個值；其他範圍沒有自己的遊戲時間，最大值的細節就記最近一次每小時那輪的時間。
        private static double _lastDay = -1.0;
        private static int _lastHour = -1;

        private static readonly HashSet<string> _calledSinceLoad =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Standard ordered scopes for daily summary report
        private static readonly string[] StandardDailyScopes = new[]
        {
            "hourly",
            "daily",
            "contacts",
            "situationScan",
            "snapshot",
            "purge",
            "load"
        };

        internal static bool Enabled
        {
            get => _enabled;
            set => _enabled = value;
        }

        internal static IDisposable Measure(string scope)
        {
            if (!_enabled) return NullScope.Instance;
            return new ActiveScope(scope);
        }

        internal static void RecordHourlyTick(double elapsedMs, double day, int hourOfDay, HourlySubMetrics subMetrics)
        {
            if (!_enabled) return;
            RecordInternal("hourly", elapsedMs, day, hourOfDay, subMetrics);
        }


        internal static void NotifyGameLoaded()
        {
            lock (Sync)
            {
                _calledSinceLoad.Clear();
            }
        }

        internal static void RecordGetRelationCall()
        {
            if (!_enabled) return;
            Interlocked.Increment(ref _relationCallCount);
        }

        internal static void Reset()
        {
            lock (Sync)
            {
                _trackers.Clear();
                _calledSinceLoad.Clear();
                _relationCallCount = 0;
            }
        }

        internal static string Report()
        {
            if (!_enabled) return "(dev) metrics disabled";

            lock (Sync)
            {
                string hourlyStr = FormatScopeCompact("hourly");
                string dailyStr = FormatScopeCompact("daily");
                string contactsStr = FormatScopeCompact("contacts");

                return $"(dev) {hourlyStr} | {dailyStr} | {contactsStr}";
            }
        }

        internal static string DetailedReport()
        {
            var sb = new StringBuilder();
            sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                "Performance Metrics (enabled={0}):", _enabled));

            lock (Sync)
            {
                AppendScopeDetail(sb, "hourly");
                AppendScopeDetail(sb, "daily");
                AppendScopeDetail(sb, "contacts");
                AppendScopeDetail(sb, "situationScan");
                AppendScopeDetail(sb, "snapshot");
                AppendScopeDetail(sb, "purge");
                AppendScopeDetail(sb, "load");

                long contactsCount = _trackers.TryGetValue("contacts", out var c) ? c.SnapshotSession().CallCount : 0;
                double avgGetRelation = contactsCount > 0 ? (double)_relationCallCount / contactsCount : 0.0;
                sb.Append(string.Format(CultureInfo.InvariantCulture,
                    "- GetRelation calls: {0} ({1:0.0} avg per ContactsOf query)",
                    _relationCallCount, avgGetRelation));
            }

            return sb.ToString();
        }

        internal static string DailySummary(int dayNumber, WorldScaleMetrics worldScale)
        {
            var snapshots = new List<ScopeStatsSnapshot>(StandardDailyScopes.Length);
            lock (Sync)
            {
                foreach (var scope in StandardDailyScopes)
                {
                    if (_trackers.TryGetValue(scope, out var tracker))
                    {
                        snapshots.Add(tracker.SnapshotAndResetDaily());
                    }
                    else
                    {
                        snapshots.Add(new ScopeStatsSnapshot(scope, 0, 0.0, 0.0, null));
                    }
                }
            }

            return ScopeMetricsFormatter.FormatDailyLine(dayNumber, snapshots, worldScale);
        }

        private static string FormatScopeCompact(string scope)
        {
            if (!_trackers.TryGetValue(scope, out var tracker))
            {
                return ScopeMetricsFormatter.Compact(scope, 0, 0.0, 0.0);
            }

            var snap = tracker.SnapshotSession();
            return ScopeMetricsFormatter.Compact(scope, snap.CallCount, snap.TotalMilliseconds, snap.PeakMilliseconds);
        }

        private static void AppendScopeDetail(StringBuilder sb, string scope)
        {
            if (_trackers.TryGetValue(scope, out var tracker))
            {
                var snap = tracker.SnapshotSession();
                sb.AppendLine(ScopeMetricsFormatter.Detail(scope, snap.CallCount, snap.TotalMilliseconds, snap.PeakMilliseconds, snap.PeakDetail));
            }
            else
            {
                sb.AppendLine(ScopeMetricsFormatter.Detail(scope, 0, 0.0, 0.0, null));
            }
        }

        private static void RecordInternal(string scope, double elapsedMs, double day, int hourOfDay, HourlySubMetrics? hourlySub)
        {
            lock (Sync)
            {
                if (day >= 0)
                {
                    _lastDay = day;
                    _lastHour = hourOfDay;
                }
                else
                {
                    day = _lastDay;
                    hourOfDay = _lastHour;
                }

                bool firstAfterLoad = _calledSinceLoad.Add(scope);
                if (!_trackers.TryGetValue(scope, out var tracker))
                {
                    tracker = new ScopeMetricTracker(scope);
                    _trackers[scope] = tracker;
                }
                tracker.Record(elapsedMs, day, hourOfDay, firstAfterLoad, hourlySub);
            }
        }

        private sealed class NullScope : IDisposable
        {
            internal static readonly NullScope Instance = new NullScope();
            private NullScope() { }
            public void Dispose() { }
        }

        private sealed class ActiveScope : IDisposable
        {
            private readonly string _scope;
            private readonly long _startTimestamp;

            public ActiveScope(string scope)
            {
                _scope = scope;
                _startTimestamp = Stopwatch.GetTimestamp();
            }

            public void Dispose()
            {
                long elapsedTicks = Stopwatch.GetTimestamp() - _startTimestamp;
                double elapsedMs = (double)elapsedTicks / Stopwatch.Frequency * 1000.0;
                RecordInternal(_scope, elapsedMs, -1.0, -1, null);
            }
        }
    }
}
