using System;
using System.Collections.Generic;

namespace VividWorld.Core.Ingest
{
    public sealed class SettlementProbe
    {
        public SettlementProbe(string label, Func<string?> resolve)
        {
            Label = label ?? throw new ArgumentNullException(nameof(label));
            Resolve = resolve ?? throw new ArgumentNullException(nameof(resolve));
        }

        public string Label { get; }
        public Func<string?> Resolve { get; }
    }

    public sealed class SettlementFallbackResult
    {
        public string? SettlementId { get; }
        public string? Via { get; }
        public IReadOnlyList<string> Misses { get; }

        public SettlementFallbackResult(string? settlementId, string? via, IReadOnlyList<string> misses)
        {
            SettlementId = settlementId;
            Via = via;
            Misses = misses ?? Array.Empty<string>();
        }

        public string Describe()
        {
            if (!string.IsNullOrEmpty(SettlementId))
            {
                if (Misses.Count == 0)
                {
                    return $"SETTLEMENT={SettlementId} via {Via}";
                }
                return $"SETTLEMENT={SettlementId} via {Via} (missed: {string.Join(", ", Misses)})";
            }

            if (Misses.Count == 0)
            {
                return "SETTLEMENT unbound";
            }
            return $"SETTLEMENT unbound (missed: {string.Join(", ", Misses)})";
        }
    }

    public static class SettlementFallback
    {
        public static SettlementFallbackResult Resolve(IEnumerable<SettlementProbe>? probes)
        {
            var misses = new List<string>();
            if (probes != null)
            {
                foreach (var probe in probes)
                {
                    if (probe == null) continue;
                    try
                    {
                        string? result = probe.Resolve?.Invoke();
                        if (!string.IsNullOrWhiteSpace(result))
                        {
                            return new SettlementFallbackResult(result, probe.Label, misses);
                        }
                        misses.Add($"{probe.Label}=null");
                    }
                    catch (Exception ex)
                    {
                        misses.Add($"{probe.Label} threw {ex.GetType().Name}");
                    }
                }
            }
            return new SettlementFallbackResult(null, null, misses);
        }
    }
}
