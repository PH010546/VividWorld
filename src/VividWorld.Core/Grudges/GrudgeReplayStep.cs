using System;
using System.Collections.Generic;

namespace VividWorld.Core.Grudges
{
    public sealed class GrudgeReplayStep
    {
        public double Day { get; set; }                 // 這一段結束的日子
        public double EntryDay { get; set; }            // 這一段開頭那一筆是哪一天記的（＝淡化的起點）
        public double DaysElapsed { get; set; }         // 這一段經過幾天
        public double ValueBefore { get; set; }         // 淡化前
        public double ValueAfterRequested { get; set; } // 加上這一段那一筆之後、還沒淡化的值
        public double Multiplier { get; set; }          // 這一段用的倍率
        public string MultiplierDetail { get; set; } = string.Empty; // 逐字，見 §3.7
        public double PointsDecayed { get; set; }       // 實際淡掉幾點（撞邊界時是被截短的那個值）
        public bool HitBand { get; set; }               // 這一段撞到邊界
        public bool InsideBand { get; set; }            // 這一段開頭就在區間內 ⇒ 不動
        public double ValueAfterDecay { get; set; }
        public double? Requested { get; set; }          // 這一段結束時加上的那一筆；最後一段（淡化到今天）為 null
        public string? EventId { get; set; }
        public double ValueAfter { get; set; }          // 加上 Requested 之後
    }

    public sealed class GrudgeReplayResult
    {
        public double Value { get; set; }               // 今天的恩怨值
        public IReadOnlyList<GrudgeReplayStep> Steps { get; set; } = Array.Empty<GrudgeReplayStep>();
        public int EntryCount { get; set; }
        public int HiddenFutureCount { get; set; }      // 日期比今天晚、沒有重播的筆數（規格 §2.2.1）
        public double Band { get; set; }
        public double DaysPerPoint { get; set; }
    }
}
