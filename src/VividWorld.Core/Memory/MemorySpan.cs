using System;
using System.Collections.Generic;
using VividWorld.Core.Config;

namespace VividWorld.Core.Memory
{
    public sealed class MemorySpanResult
    {
        public double Reinforce { get; set; }
        // 當事人倍率實際用到的值：不是當事人時是 1.0
        public double ParticipantMultiplier { get; set; } = 1.0;
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
            double? oldForgetDay = null,
            bool knowerIsParticipant = false)
        {
            return Compute(startDay, interest, drama, heardCount, oldForgetDay, cfg, knowerIsParticipant);
        }

        public static MemorySpanResult Compute(
            double startDay,
            double interest,
            int drama,
            int heardCount,
            double? oldForgetDay,
            MemoryConfig cfg,
            bool knowerIsParticipant = false)
        {
            int h = Math.Max(0, heardCount);
            double reinforceGrowthPow = Math.Pow(cfg.ReinforceGrowth, h);
            double reinforce = Math.Min(cfg.ReinforceMaxMultiplier, reinforceGrowthPow);

            // 當事人記得久：天數乘 ParticipantSpanMultiplier。讀檔時已把小於 1 的值夾成 1.0（見 VividWorldConfig.Normalize）。
            // 已經算好寫進知情紀錄的忘記日子不在這裡重算，只有重新計算的路徑會經過這一行。
            double participantMultiplier = knowerIsParticipant ? cfg.ParticipantSpanMultiplier : 1.0;

            int clampedDrama = Math.Max(1, Math.Min(5, drama));
            int dramaRef = Math.Max(1, cfg.DramaReference);
            double raw = cfg.BaseDays * interest * ((double)clampedDrama / dramaRef) * reinforce * participantMultiplier;
            double days = Math.Max(raw, cfg.MinDays);
            bool raisedToMin = days > raw;

            double candidate = startDay + days;
            bool keptOld = oldForgetDay.HasValue && oldForgetDay.Value > candidate;
            double forgetDay = oldForgetDay.HasValue ? Math.Max(oldForgetDay.Value, candidate) : candidate;

            return new MemorySpanResult
            {
                Reinforce = reinforce,
                ParticipantMultiplier = participantMultiplier,
                Raw = raw,
                Days = days,
                RaisedToMin = raisedToMin,
                Candidate = candidate,
                KeptOld = keptOld,
                ForgetDay = forgetDay
            };
        }

        /// <summary>這位知情者是不是這則事件的角色（participants 是 角色 -> heroId）。空的 heroId 一律不算。</summary>
        public static bool IsParticipant(IReadOnlyDictionary<string, string>? participants, string heroId)
        {
            if (participants == null || string.IsNullOrEmpty(heroId)) return false;
            foreach (var kvp in participants)
            {
                if (string.Equals(kvp.Value, heroId, StringComparison.Ordinal)) return true;
            }
            return false;
        }
    }
}
