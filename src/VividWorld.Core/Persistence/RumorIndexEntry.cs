#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using VividWorld.Core.Events;

namespace VividWorld.Core.Persistence
{
    public sealed class RumorIndexEntry
    {
        public string EventId { get; set; } = string.Empty;
        public string Type { get; set; } = string.Empty;
        public double Day { get; set; }
        public string ShardKey { get; set; } = string.Empty;
        public bool Secret { get; set; }
        public bool Leaked { get; set; }
        public bool Dormant { get; set; }
        public int MinHop { get; set; }
        public int MaxHop { get; set; }
        public List<string> KnownByHeroIds { get; set; } = new();
        public List<string> ParticipantHeroIds { get; set; } = new();
        public string? LinkedEventId { get; set; }
        public string? SituationId { get; set; }
        public bool HasGrudges { get; set; }

        [JsonExtensionData]
        public IDictionary<string, JToken> Extra { get; set; } = new Dictionary<string, JToken>();

        public static RumorIndexEntry From(WorldEvent evt, int shardDays)
        {
            if (evt == null) throw new ArgumentNullException(nameof(evt));

            return new RumorIndexEntry
            {
                EventId = evt.EventId,
                Type = evt.Type,
                Day = evt.Day,
                ShardKey = Persistence.ShardKey.For(evt.Day, shardDays),
                Secret = evt.Origin == EventOrigin.Secret,
                Leaked = evt.State?.Leaked ?? false,
                Dormant = evt.State?.Dormant ?? false,
                MinHop = evt.MinHop(),
                MaxHop = evt.MaxHop(),
                KnownByHeroIds = evt.KnownBy?
                    .Where(k => !string.IsNullOrEmpty(k.HeroId))
                    .Select(k => k.HeroId)
                    .Distinct()
                    .ToList() ?? new List<string>(),
                ParticipantHeroIds = evt.Participants?.Values
                    .Where(h => !string.IsNullOrEmpty(h))
                    .Distinct()
                    .ToList() ?? new List<string>(),
                LinkedEventId = evt.LinkedEventId,
                SituationId = evt.SituationId,
                HasGrudges = evt.KnownBy?.Any(k => k.RelationImpacts != null && k.RelationImpacts.Count > 0) ?? false
            };
        }
    }
}
