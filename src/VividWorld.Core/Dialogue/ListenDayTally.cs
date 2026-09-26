#nullable enable
using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace VividWorld.Core.Dialogue
{
    [JsonConverter(typeof(ListenDayTallyJsonConverter))]
    public sealed class ListenDayTally
    {
        private readonly Dictionary<string, int> _counts = new(StringComparer.Ordinal);

        public int DistinctPartners { get; set; }
        public Dictionary<string, int> RelationHist { get; set; } = new(StringComparer.Ordinal);
        public int EventsOnFile { get; set; }
        public int EventsRemembered { get; set; }
        public HashSet<string> Partners { get; set; } = new(StringComparer.Ordinal);

        [JsonExtensionData]
        public IDictionary<string, JToken> Extra { get; set; } = new Dictionary<string, JToken>(StringComparer.Ordinal);

        public IReadOnlyDictionary<string, int> Counts => _counts;

        public int GetCount(string key) => _counts.TryGetValue(key, out int count) ? count : 0;

        public void Increment(string key, int amount = 1)
        {
            if (string.IsNullOrEmpty(key) || amount <= 0) return;
            _counts[key] = GetCount(key) + amount;
        }

        public void RecordPartner(string? partnerId, int relation, int onFile = 0, int remembered = 0)
        {
            if (!string.IsNullOrEmpty(partnerId))
            {
                Partners.Add(partnerId!);
                DistinctPartners = Math.Max(DistinctPartners, Partners.Count);
            }
            string bin = ListenTallyClassifier.GetRelationBin(relation);
            RelationHist[bin] = (RelationHist.TryGetValue(bin, out int count) ? count : 0) + 1;
            EventsOnFile += onFile;
            EventsRemembered += remembered;
        }
    }

    public sealed class ListenDayTallyJsonConverter : JsonConverter
    {
        public override bool CanConvert(Type objectType) => objectType == typeof(ListenDayTally);

        public override void WriteJson(JsonWriter writer, object? value, JsonSerializer serializer)
        {
            if (value is not ListenDayTally tally)
            {
                writer.WriteNull();
                return;
            }

            writer.WriteStartObject();

            // 1. Write counts (<key>: <int>)
            foreach (var kv in tally.Counts)
            {
                writer.WritePropertyName(kv.Key);
                writer.WriteValue(kv.Value);
            }

            // 2. Write relationHist
            writer.WritePropertyName("relationHist");
            serializer.Serialize(writer, tally.RelationHist);

            // 3. Write distinctPartners
            writer.WritePropertyName("distinctPartners");
            writer.WriteValue(tally.DistinctPartners);

            // 4. Optional fields
            if (tally.EventsOnFile > 0)
            {
                writer.WritePropertyName("eventsOnFile");
                writer.WriteValue(tally.EventsOnFile);
            }
            if (tally.EventsRemembered > 0)
            {
                writer.WritePropertyName("eventsRemembered");
                writer.WriteValue(tally.EventsRemembered);
            }
            if (tally.Partners != null && tally.Partners.Count > 0)
            {
                writer.WritePropertyName("partners");
                serializer.Serialize(writer, tally.Partners);
            }

            // 5. Unknown fields from Extra
            if (tally.Extra != null)
            {
                foreach (var kv in tally.Extra)
                {
                    writer.WritePropertyName(kv.Key);
                    kv.Value.WriteTo(writer);
                }
            }

            writer.WriteEndObject();
        }

        public override object? ReadJson(JsonReader reader, Type objectType, object? existingValue, JsonSerializer serializer)
        {
            if (reader.TokenType == JsonToken.Null) return null;

            var jObj = JObject.Load(reader);
            var tally = new ListenDayTally();

            foreach (var prop in jObj.Properties())
            {
                string name = prop.Name;
                var val = prop.Value;

                if (string.Equals(name, "relationHist", StringComparison.OrdinalIgnoreCase))
                {
                    var dict = val.ToObject<Dictionary<string, int>>(serializer);
                    if (dict != null) tally.RelationHist = dict;
                }
                else if (string.Equals(name, "distinctPartners", StringComparison.OrdinalIgnoreCase))
                {
                    tally.DistinctPartners = val.Value<int>();
                }
                else if (string.Equals(name, "eventsOnFile", StringComparison.OrdinalIgnoreCase))
                {
                    tally.EventsOnFile = val.Value<int>();
                }
                else if (string.Equals(name, "eventsRemembered", StringComparison.OrdinalIgnoreCase))
                {
                    tally.EventsRemembered = val.Value<int>();
                }
                else if (string.Equals(name, "partners", StringComparison.OrdinalIgnoreCase))
                {
                    var set = val.ToObject<HashSet<string>>(serializer);
                    if (set != null) tally.Partners = set;
                }
                else if (val.Type == JTokenType.Integer && (name.Contains(".") || string.Equals(name, ListenTallyKeys.NotInNetwork, StringComparison.Ordinal)))
                {
                    tally.Increment(name, val.Value<int>());
                }
                else
                {
                    tally.Extra[name] = val;
                }
            }

            if (tally.Partners != null && tally.Partners.Count > 0)
            {
                tally.DistinctPartners = Math.Max(tally.DistinctPartners, tally.Partners.Count);
            }

            return tally;
        }
    }
}
