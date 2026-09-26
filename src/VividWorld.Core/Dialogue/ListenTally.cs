#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace VividWorld.Core.Dialogue
{
    public sealed class ListenTally
    {
        [JsonProperty("version")]
        public int Version { get; set; } = 1;

        [JsonProperty("days")]
        public Dictionary<string, ListenDayTally> Days { get; set; } = new(StringComparer.Ordinal);

        [JsonExtensionData]
        public IDictionary<string, JToken> Extra { get; set; } = new Dictionary<string, JToken>(StringComparer.Ordinal);

        public ListenDayTally GetOrCreateDay(int day)
        {
            string key = day.ToString(CultureInfo.InvariantCulture);
            if (!Days.TryGetValue(key, out var tally))
            {
                tally = new ListenDayTally();
                Days[key] = tally;
            }
            return tally;
        }

        public ListenDayTally? GetDay(int day)
        {
            string key = day.ToString(CultureInfo.InvariantCulture);
            return Days.TryGetValue(key, out var tally) ? tally : null;
        }
    }
}
