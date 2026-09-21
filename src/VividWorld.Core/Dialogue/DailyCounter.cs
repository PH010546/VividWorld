using System;

namespace VividWorld.Core.Dialogue
{
    /// <summary>
    /// 每日計數器（規格 §5.1 / M6b §3.4）。
    /// 負責維護全域每日主動講述次數，在跨日（day 整數部分變更）或時間倒退（讀舊檔）時自動歸零。
    /// </summary>
    public sealed class DailyCounter
    {
        private int _lastDayBucket = int.MinValue;

        public int Count { get; private set; }

        public static int BucketOf(double day) => (int)Math.Floor(day);

        public void Advance(double day)
        {
            int bucket = BucketOf(day);
            if (_lastDayBucket != bucket)
            {
                Count = 0;
                _lastDayBucket = bucket;
            }
        }

        public void Increment()
        {
            Count++;
        }

        public void Reset()
        {
            Count = 0;
            _lastDayBucket = int.MinValue;
        }
    }
}
