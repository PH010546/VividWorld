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

        private static readonly Dictionary<string, ScopeMetrics> _scopes =
            new Dictionary<string, ScopeMetrics>(StringComparer.OrdinalIgnoreCase);

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

        internal static void RecordGetRelationCall()
        {
            if (!_enabled) return;
            Interlocked.Increment(ref _relationCallCount);
        }

        internal static void Reset()
        {
            lock (Sync)
            {
                _scopes.Clear();
                _relationCallCount = 0;
            }
        }

        internal static string Report()
        {
            if (!_enabled) return "(dev) metrics disabled";

            lock (Sync)
            {
                string hourlyStr = FormatScope("hourly");
                string dailyStr = FormatScope("daily");
                string contactsStr = FormatScope("contacts");

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
                AppendScopeDetail(sb, "situationScan");   // SE3：每日情境掃描，Measure 有量但這份報告原本沒印
                AppendScopeDetail(sb, "snapshot");

                long contactsCount = _scopes.TryGetValue("contacts", out var c) ? c.CallCount : 0;
                double avgGetRelation = contactsCount > 0 ? (double)_relationCallCount / contactsCount : 0.0;
                sb.Append(string.Format(CultureInfo.InvariantCulture,
                    "- GetRelation calls: {0} ({1:0.0} avg per ContactsOf query)",
                    _relationCallCount, avgGetRelation));
            }

            return sb.ToString();
        }

        private static string FormatScope(string scope)
        {
            if (!_scopes.TryGetValue(scope, out var m) || m.CallCount == 0)
            {
                return ScopeMetricsFormatter.Compact(scope, 0, 0.0, 0.0);
            }

            return ScopeMetricsFormatter.Compact(scope, m.CallCount, m.TotalMilliseconds, m.PeakMilliseconds);
        }

        private static void AppendScopeDetail(StringBuilder sb, string scope)
        {
            if (_scopes.TryGetValue(scope, out var m) && m.CallCount > 0)
            {
                sb.AppendLine(ScopeMetricsFormatter.Detail(scope, m.CallCount, m.TotalMilliseconds, m.PeakMilliseconds));
            }
            else
            {
                sb.AppendLine(ScopeMetricsFormatter.Detail(scope, 0, 0.0, 0.0));
            }
        }

        private static void Record(string scope, double elapsedMs)
        {
            lock (Sync)
            {
                if (!_scopes.TryGetValue(scope, out var metrics))
                {
                    metrics = new ScopeMetrics();
                    _scopes[scope] = metrics;
                }
                metrics.Record(elapsedMs);
            }
        }

        private sealed class ScopeMetrics
        {
            public long CallCount;
            public double TotalMilliseconds;
            public double PeakMilliseconds;

            public void Record(double ms)
            {
                CallCount++;
                TotalMilliseconds += ms;
                if (ms > PeakMilliseconds)
                {
                    PeakMilliseconds = ms;
                }
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
                Record(_scope, elapsedMs);
            }
        }
    }
}
