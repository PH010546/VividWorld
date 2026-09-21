using System;
using System.Collections.Generic;
using VividWorld.Core.Dialogue;

namespace VividWorld.Core.Rumors
{
    /// <summary>
    /// 每人每天一份好感度變動預算（規格 §6.7.2 / M6.5 §4.3）。
    /// 跨日（日桶變更）或時間倒退（讀舊檔）時自動歸零。
    /// 不進存檔，讀檔即歸零。
    /// </summary>
    public sealed class DailyRelationBudget
    {
        private readonly Dictionary<string, double> _used = new(StringComparer.Ordinal);
        private int _lastDayBucket = int.MinValue;

        public void Advance(double day)
        {
            int bucket = DailyCounter.BucketOf(day);
            if (_lastDayBucket != bucket)
            {
                _used.Clear();
                _lastDayBucket = bucket;
            }
        }

        public double Remaining(string heroId, double cap)
        {
            if (string.IsNullOrEmpty(heroId) || cap <= 0) return 0.0;
            double used = _used.TryGetValue(heroId, out var val) ? val : 0.0;
            return Math.Max(0.0, cap - used);
        }

        public void Consume(string heroId, double amount)
        {
            if (string.IsNullOrEmpty(heroId)) return;
            double abs = Math.Abs(amount);
            if (_used.TryGetValue(heroId, out var val))
            {
                _used[heroId] = val + abs;
            }
            else
            {
                _used[heroId] = abs;
            }
        }

        public double Used(string heroId)
        {
            if (string.IsNullOrEmpty(heroId)) return 0.0;
            return _used.TryGetValue(heroId, out var val) ? val : 0.0;
        }

        public double ConsumedToday(string heroId, double day)
        {
            Advance(day);
            return Used(heroId);
        }

        public void Reset()
        {
            _used.Clear();
            _lastDayBucket = int.MinValue;
        }

        public int TrackedHeroes => _used.Count;
    }
}
