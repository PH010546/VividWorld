using System;
using System.Globalization;
using VividWorld.Core.Config;
using VividWorld.Core.Events;

namespace VividWorld.Core.Memory
{
    public sealed class RehearDecision
    {
        public bool AdoptVersion { get; }
        public string Reason { get; }

        public RehearDecision(bool adoptVersion, string reason)
        {
            AdoptVersion = adoptVersion;
            Reason = reason ?? string.Empty;
        }
    }

    public enum RehearKind
    {
        Counted,
        SameTellerIgnored,
        SameTellerRelearned
    }

    public sealed class RehearRecord
    {
        public KnownByEntry Entry { get; }
        public int OldHop { get; }
        public bool WasForgotten { get; }
        public RehearDecision Rule { get; }
        public RehearKind Kind { get; }

        public RehearRecord(KnownByEntry entry, int oldHop, bool wasForgotten, RehearDecision rule)
            : this(entry, oldHop, wasForgotten, rule, RehearKind.Counted)
        {
        }

        public RehearRecord(KnownByEntry entry, int oldHop, bool wasForgotten, RehearDecision rule, RehearKind kind)
        {
            Entry = entry ?? throw new ArgumentNullException(nameof(entry));
            OldHop = oldHop;
            WasForgotten = wasForgotten;
            Rule = rule ?? throw new ArgumentNullException(nameof(rule));
            Kind = kind;
        }

        public static RehearRecord SameTellerIgnored(KnownByEntry existing)
        {
            if (existing == null) throw new ArgumentNullException(nameof(existing));
            return new RehearRecord(
                existing,
                existing.Hop,
                false,
                new RehearDecision(false, "same teller ignored"),
                RehearKind.SameTellerIgnored);
        }
    }

    public static class RehearRule
    {
        public static RehearDecision Decide(KnownByEntry entry, int tellerHop, string tellerId, bool isForgotten, MemoryConfig cfg)
        {
            return Decide(entry, tellerHop, isForgotten, cfg);
        }

        public static RehearDecision Decide(KnownByEntry entry, int tellerHop, bool isForgotten, MemoryConfig cfg)
        {
            if (isForgotten)
            {
                return new RehearDecision(true, "forgotten");
            }

            bool isCloser = tellerHop + 1 < entry.Hop;
            double interest = entry.Interest ?? 0.0;

            if (isCloser)
            {
                if (interest >= cfg.UpgradeMinInterest)
                {
                    string reason = string.Format(CultureInfo.InvariantCulture,
                        "closer source and interest {0:0.00} >= {1:0.00}",
                        interest, cfg.UpgradeMinInterest);
                    return new RehearDecision(true, reason);
                }
                else
                {
                    string reason = string.Format(CultureInfo.InvariantCulture,
                        "kept: interest {0:0.00} < {1:0.00}",
                        interest, cfg.UpgradeMinInterest);
                    return new RehearDecision(false, reason);
                }
            }
            else
            {
                return new RehearDecision(false, "kept: source not closer");
            }
        }
    }
}
