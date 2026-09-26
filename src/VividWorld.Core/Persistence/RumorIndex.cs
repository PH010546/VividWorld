#nullable enable
using System;
using System.Collections.Generic;
using System.Runtime.Serialization;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace VividWorld.Core.Persistence
{
    public sealed class RumorIndex
    {
        public const int CurrentFormatVersion = 1;
        public int FormatVersion { get; set; }

        public List<RumorIndexEntry> Entries { get; set; } = new();

        [JsonExtensionData]
        public IDictionary<string, JToken> Extra { get; set; } = new Dictionary<string, JToken>();

        [JsonIgnore]
        public int Count => Entries.Count;

        [JsonIgnore]
        private readonly Dictionary<string, RumorIndexEntry> _lookup = new(StringComparer.OrdinalIgnoreCase);

        [OnDeserialized]
        internal void OnDeserializedMethod(StreamingContext context)
        {
            RebuildLookup();
        }

        public void RebuildLookup()
        {
            _lookup.Clear();
            foreach (var entry in Entries)
            {
                if (entry != null && !string.IsNullOrEmpty(entry.EventId))
                {
                    _lookup[entry.EventId] = entry;
                }
            }
        }

        /// <summary>今天看得見的項目（規格 §2.2.1）。日期比今天晚的不會出現在這裡，
        /// 但**仍然留在 <see cref="Entries"/> 裡**——日子走到那一天就自己回來了。
        /// 讀事件的地方一律走這個視圖，不要自己去走 <see cref="Entries"/>。</summary>
        public IEnumerable<RumorIndexEntry> VisibleEntries(double currentDay)
        {
            foreach (var entry in Entries)
            {
                if (EventVisibility.IsVisibleOn(entry, currentDay))
                {
                    yield return entry;
                }
            }
        }

        /// <summary>今天被隱藏起來的項目數（診斷用：要講得出「藏了幾則」）。</summary>
        public int HiddenFutureCount(double currentDay)
        {
            int count = 0;
            foreach (var entry in Entries)
            {
                if (!EventVisibility.IsVisibleOn(entry, currentDay))
                {
                    count++;
                }
            }
            return count;
        }

        public RumorIndexEntry? Find(string eventId)
        {
            if (string.IsNullOrEmpty(eventId)) return null;
            if (_lookup.Count != Entries.Count && Entries.Count > 0 && _lookup.Count == 0)
            {
                RebuildLookup();
            }
            return _lookup.TryGetValue(eventId, out var entry) ? entry : null;
        }

        public void Upsert(RumorIndexEntry entry)
        {
            if (entry == null || string.IsNullOrEmpty(entry.EventId)) return;

            if (_lookup.TryGetValue(entry.EventId, out var existing))
            {
                var index = Entries.IndexOf(existing);
                if (index >= 0)
                {
                    Entries[index] = entry;
                }
                else
                {
                    Entries.Add(entry);
                }
                _lookup[entry.EventId] = entry;
            }
            else
            {
                Entries.Add(entry);
                _lookup[entry.EventId] = entry;
            }
        }

        public bool Remove(string eventId)
        {
            if (string.IsNullOrEmpty(eventId)) return false;

            if (_lookup.TryGetValue(eventId, out var existing))
            {
                _lookup.Remove(eventId);
                Entries.Remove(existing);
                return true;
            }
            return false;
        }
    }
}
