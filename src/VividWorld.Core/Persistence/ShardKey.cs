#nullable enable
using System;

namespace VividWorld.Core.Persistence
{
    public static class ShardKey
    {
        /// <summary>
        /// 依天數與分片跨度產生分片鍵。格式例如："d0100-0199"。
        /// </summary>
        public static string For(double day, int shardDays)
        {
            if (shardDays <= 0) shardDays = 1;
            if (day < 0) day = 0;

            var bucket = (int)Math.Floor(day) / shardDays;
            var lo = bucket * shardDays;
            var hi = lo + shardDays - 1;
            return $"d{lo:D4}-{hi:D4}";
        }
    }
}
