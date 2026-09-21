using System;
using VividWorld.Core.Config;

namespace VividWorld.Core.Memory
{
    public sealed class MemorySpanResult
    {
        public double Reinforce { get; set; }
        public double Raw { get; set; }
        public double Days { get; set; }
        public bool RaisedToMin { get; set; }
        public double Candidate { get; set; }
        public bool KeptOld { get; set; }
        public double ForgetDay { get; set; }
    }

    public static class MemorySpan
    {
        public static MemorySpanResult Compute(
            double interest,
            int drama,
            double startDay,
            int heardCount,
            MemoryConfig cfg,
            double? oldForgetDay = null)
        {
            return Compute(startDay, interest, drama, heardCount, oldForgetDay, cfg);
        }

        public static MemorySpanResult Compute(
            double startDay,
            double interest,
            int drama,
            int heardCount,
            double? oldForgetDay,
            MemoryConfig cfg)
        {
            int h = Math.Max(0, heardCount);
            double reinforceGrowthPow = Math.Pow(cfg.ReinforceGrowth, h);
            double reinforce = Math.Min(cfg.ReinforceMaxMultiplier, reinforceGrowthPow);

            int clampedDrama = Math.Max(1, Math.Min(5, drama));
            int dramaRef = Math.Max(1, cfg.DramaReference);
            double raw = cfg.BaseDays * interest * ((double)clampedDrama / dramaRef) * reinforce;
            double days = Math.Max(raw, cfg.MinDays);
            bool raisedToMin = days > raw;

            double candidate = startDay + days;
            bool keptOld = oldForgetDay.HasValue && oldForgetDay.Value > candidate;
            double forgetDay = oldForgetDay.HasValue ? Math.Max(oldForgetDay.Value, candidate) : candidate;

            return new MemorySpanResult
            {
                Reinforce = reinforce,
                Raw = raw,
                Days = days,
                RaisedToMin = raisedToMin,
                Candidate = candidate,
                KeptOld = keptOld,
                ForgetDay = forgetDay
            };
        }
    }
}
