using System;
using System.Collections.Generic;
using VividWorld.Core.Dialogue;
using VividWorld.Core.Events;

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

        /// <summary>
        /// 退回已消耗的額度，最低至 0。回傳實際退回的量。
        /// </summary>
        public double Refund(string heroId, double amount)
        {
            if (string.IsNullOrEmpty(heroId)) return 0.0;
            double abs = Math.Abs(amount);
            if (abs <= 0.0) return 0.0;
            if (!_used.TryGetValue(heroId, out var val) || val <= 0.0)
            {
                return 0.0;
            }
            double actual = Math.Min(val, abs);
            _used[heroId] = Math.Max(0.0, val - actual);
            return actual;
        }

        /// <summary>
        /// 判定一筆關係影響是否符合退回當日額度的條件。
        /// 僅限個人層次且為當日結算之傳聞影響。
        /// </summary>
        public static bool CanRefundImpact(RelationImpact? ri, double currentDay, out string? nonRefundReason)
        {
            if (ri == null)
            {
                nonRefundReason = null;
                return false;
            }

            if (ri.Scope != GrudgeScope.Personal)
            {
                nonRefundReason = "clan-level";
                return false;
            }

            if (DailyCounter.BucketOf(ri.AppliedDay) != DailyCounter.BucketOf(currentDay))
            {
                nonRefundReason = "applied on an earlier day";
                return false;
            }

            nonRefundReason = null;
            return true;
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
