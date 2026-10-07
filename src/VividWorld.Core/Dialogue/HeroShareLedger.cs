#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace VividWorld.Core.Dialogue
{
    public sealed class HeroShareEntry
    {
        [JsonProperty("day")]
        public int Day { get; set; }

        [JsonProperty("count")]
        public int Count { get; set; }

        [JsonExtensionData]
        public IDictionary<string, JToken> Extra { get; set; } = new Dictionary<string, JToken>();
    }

    /// <summary>
    /// 維護每位 NPC 每天的傳聞分享次數。
    /// 包含主動講述、被繞開補救與玩家詢問三種方式的合算次數。
    /// </summary>
    public sealed class HeroShareLedger
    {
        private readonly Dictionary<string, HeroShareEntry> _entries = new(StringComparer.OrdinalIgnoreCase);

        public int Count => _entries.Count;

        public int SharedOn(string heroId, double day)
        {
            return SharedOn(heroId, DailyCounter.BucketOf(day));
        }

        public int SharedOn(string heroId, int dayBucket)
        {
            if (string.IsNullOrEmpty(heroId)) return 0;
            if (_entries.TryGetValue(heroId, out var entry))
            {
                return entry.Day == dayBucket ? entry.Count : 0;
            }
            return 0;
        }

        public int Record(string heroId, double day)
        {
            return Record(heroId, DailyCounter.BucketOf(day));
        }

        public int Record(string heroId, int dayBucket)
        {
            if (string.IsNullOrEmpty(heroId)) return 0;

            if (_entries.TryGetValue(heroId, out var entry))
            {
                if (entry.Day == dayBucket)
                {
                    entry.Count++;
                    return entry.Count;
                }
                else
                {
                    entry.Day = dayBucket;
                    entry.Count = 1;
                    return 1;
                }
            }
            else
            {
                var newEntry = new HeroShareEntry
                {
                    Day = dayBucket,
                    Count = 1
                };
                _entries[heroId] = newEntry;
                return 1;
            }
        }

        public bool IsAtCap(string heroId, double day, int cap)
        {
            return IsAtCap(heroId, DailyCounter.BucketOf(day), cap);
        }

        public bool IsAtCap(string heroId, int dayBucket, int cap)
        {
            if (cap <= 0) return false;
            return SharedOn(heroId, dayBucket) >= cap;
        }

        public void LoadFrom(IDictionary<string, HeroShareEntry>? data)
        {
            _entries.Clear();
            if (data == null) return;

            foreach (var kvp in data)
            {
                if (!string.IsNullOrEmpty(kvp.Key) && kvp.Value != null)
                {
                    _entries[kvp.Key] = kvp.Value;
                }
            }
        }

        public Dictionary<string, HeroShareEntry> ToDictionary()
        {
            return new Dictionary<string, HeroShareEntry>(_entries, StringComparer.OrdinalIgnoreCase);
        }

        public string TodaySummary(double day, int cap, Func<string, string?>? nameResolver = null)
        {
            return TodaySummary(DailyCounter.BucketOf(day), cap, nameResolver);
        }

        public string TodaySummary(int dayBucket, int cap, Func<string, string?>? nameResolver = null)
        {
            return TodaySummaryWithLabel("Shared today", dayBucket, cap, nameResolver);
        }

        public string TodaySummaryWithLabel(string label, double day, int cap, Func<string, string?>? nameResolver = null)
        {
            return TodaySummaryWithLabel(label, DailyCounter.BucketOf(day), cap, nameResolver);
        }

        public string TodaySummaryWithLabel(string label, int dayBucket, int cap, Func<string, string?>? nameResolver = null)
        {
            var todayEntries = _entries
                .Where(kv => kv.Value.Day == dayBucket && kv.Value.Count > 0)
                .OrderByDescending(kv => kv.Value.Count)
                .ThenBy(kv => kv.Key, StringComparer.Ordinal)
                .ToList();

            int n = todayEntries.Count;
            string capStr = cap.ToString(CultureInfo.InvariantCulture);
            string header = $"{label}: {n} people (cap {capStr} each; 0 = no limit)";

            if (n == 0)
            {
                return header;
            }

            var displayed = todayEntries.Take(10).Select(kv =>
            {
                string name = (nameResolver != null ? nameResolver(kv.Key) : null) ?? kv.Key;
                return $"{name} ({kv.Value.Count})";
            }).ToList();

            string listStr = string.Join(", ", displayed);
            if (n > 10)
            {
                int more = n - 10;
                listStr += $", …and {more} more";
            }

            return $"{header} - {listStr}";
        }
    }
}
