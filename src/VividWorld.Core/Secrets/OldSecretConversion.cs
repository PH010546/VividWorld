#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using VividWorld.Core.Catalog;
using VividWorld.Core.Events;

namespace VividWorld.Core.Secrets
{
    public sealed class SecretConversionRecord
    {
        [JsonProperty("version")]
        public int Version { get; set; } = 1;

        [JsonProperty("day")]
        public double Day { get; set; }

        [JsonProperty("countsByType")]
        public Dictionary<string, int> CountsByType { get; set; } = new(StringComparer.Ordinal);

        [JsonProperty("backupPath", NullValueHandling = NullValueHandling.Ignore)]
        public string? BackupPath { get; set; }

        [JsonProperty("totalConverted")]
        public int TotalConverted { get; set; }

        [JsonProperty("totalProcessed")]
        public int TotalProcessed { get; set; }

        [JsonExtensionData]
        public IDictionary<string, JToken> Extra { get; set; } = new Dictionary<string, JToken>();
    }

    public enum SecretConversionAction
    {
        UnchangedAlreadyLeaked,
        UnchangedUnrecognized,
        EnduredNpcToPublic,
        EnduredPlayerToPublic,
        PoisonedAddedOwner,
        Retired
    }

    public sealed class SecretConversionItemResult
    {
        public string EventId { get; set; } = string.Empty;
        public string Type { get; set; } = string.Empty;
        public SecretConversionAction Action { get; set; }
        public string Details { get; set; } = string.Empty;
        public WorldEvent? ConvertedEvent { get; set; }
        public WorldEvent? GeneratedGrudgeEvent { get; set; }

        public string FormatLogLine()
        {
            return $"secret conversion: {EventId} {Type} -> {Details}";
        }
    }

    public sealed class OldSecretConversionPlan
    {
        public List<SecretConversionItemResult> Items { get; set; } = new();
        public int TotalProcessed => Items.Count;
        public int ConvertedCount => Items.Count(i => i.Action == SecretConversionAction.EnduredNpcToPublic
                                                   || i.Action == SecretConversionAction.EnduredPlayerToPublic
                                                   || i.Action == SecretConversionAction.PoisonedAddedOwner);
        public int RetiredCount => Items.Count(i => i.Action == SecretConversionAction.Retired);
        public int UnchangedCount => Items.Count(i => i.Action == SecretConversionAction.UnchangedAlreadyLeaked
                                                   || i.Action == SecretConversionAction.UnchangedUnrecognized);

        public Dictionary<string, int> CountsByType
        {
            get
            {
                var dict = new Dictionary<string, int>(StringComparer.Ordinal);
                foreach (var item in Items)
                {
                    if (item.Action == SecretConversionAction.EnduredNpcToPublic ||
                        item.Action == SecretConversionAction.EnduredPlayerToPublic ||
                        item.Action == SecretConversionAction.PoisonedAddedOwner ||
                        item.Action == SecretConversionAction.Retired)
                    {
                        dict[item.Type] = dict.TryGetValue(item.Type, out int c) ? c + 1 : 1;
                    }
                }
                return dict;
            }
        }
    }

    public static class OldSecretConverter
    {
        private static readonly HashSet<string> RetiredSecretTypes = new(StringComparer.OrdinalIgnoreCase)
        {
            "wager_secret_stake",
            "brawl_hushed_up",
            "hero_murdered",
            "tavern_confidence",
            "victory_credit_belittled"
        };

        public static bool IsOldShapeSecret(WorldEvent? evt)
        {
            if (evt == null) return false;
            return evt.Origin == EventOrigin.Secret && (evt.SecretOwnerHeroIds == null || evt.SecretOwnerHeroIds.Count == 0);
        }

        public static OldSecretConversionPlan Plan(
            IEnumerable<WorldEvent> events,
            string playerHeroId,
            Func<string, string?>? settlementOwnerClanLeaderLookup = null,
            Func<string, EventTemplate?>? getTemplate = null,
            Func<string, bool>? isEventIdTaken = null,
            bool applyInPlace = false,
            double? conversionDay = null)
        {
            var plan = new OldSecretConversionPlan();
            if (events == null) return plan;

            var existingIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (var originalEvt in events)
            {
                if (originalEvt == null || !IsOldShapeSecret(originalEvt))
                {
                    continue;
                }

                existingIds.Add(originalEvt.EventId);
                var targetEvt = applyInPlace ? originalEvt : CloneEvent(originalEvt);

                var item = ConvertSingleEvent(
                    targetEvt,
                    playerHeroId,
                    settlementOwnerClanLeaderLookup,
                    getTemplate,
                    id => existingIds.Contains(id) || (isEventIdTaken != null && isEventIdTaken(id)),
                    conversionDay);

                if (item.GeneratedGrudgeEvent != null)
                {
                    existingIds.Add(item.GeneratedGrudgeEvent.EventId);
                }

                plan.Items.Add(item);
            }

            return plan;
        }

        private static SecretConversionItemResult ConvertSingleEvent(
            WorldEvent evt,
            string playerHeroId,
            Func<string, string?>? settlementOwnerClanLeaderLookup,
            Func<string, EventTemplate?>? getTemplate,
            Func<string, bool> isEventIdTaken,
            double? conversionDay)
        {
            var res = new SecretConversionItemResult
            {
                EventId = evt.EventId,
                Type = evt.Type,
                ConvertedEvent = evt
            };

            // 1. Already leaked secrets are left unchanged
            if (evt.State?.Leaked == true)
            {
                res.Action = SecretConversionAction.UnchangedAlreadyLeaked;
                res.Details = "unchanged (already leaked)";
                return res;
            }

            // 2. seat_dispute_endured
            if (string.Equals(evt.Type, "seat_dispute_endured", StringComparison.OrdinalIgnoreCase))
            {
                string slightedId = evt.Participants != null && evt.Participants.TryGetValue("slighted", out var sId) ? sId : string.Empty;
                string favoredId = evt.Participants != null && evt.Participants.TryGetValue("favored", out var fId) ? fId : string.Empty;
                bool isPlayerEndured = !string.IsNullOrEmpty(playerHeroId) && string.Equals(slightedId, playerHeroId, StringComparison.Ordinal);

                string settlementId = ExtractSettlementId(evt);
                string? hostId = null;
                string? hostFailureReason = null;

                if (!string.IsNullOrEmpty(settlementId))
                {
                    if (settlementOwnerClanLeaderLookup != null)
                    {
                        hostId = settlementOwnerClanLeaderLookup(settlementId);
                        if (string.IsNullOrEmpty(hostId))
                        {
                            hostFailureReason = $"settlement {settlementId} owner clan leader could not be resolved";
                        }
                    }
                    else
                    {
                        hostFailureReason = "no settlement lookup provider available";
                    }
                }
                else
                {
                    hostFailureReason = "no settlement recorded on event facts";
                }

                // Mutate event origin to Public
                evt.Origin = EventOrigin.Public;

                // It spreads as ordinary news from the conversion day; counted from its old date it would go stale at once.
                if (conversionDay.HasValue && evt.State != null)
                {
                    evt.State.LastNewKnowerDay = conversionDay.Value;
                }

                // Ensure slighted, favored, and host (if known) are hop 0 witnesses in KnownBy
                int addedWitnesses = 0;
                addedWitnesses += EnsureHop0Witness(evt, slightedId);
                addedWitnesses += EnsureHop0Witness(evt, favoredId);
                if (!string.IsNullOrEmpty(hostId))
                {
                    addedWitnesses += EnsureHop0Witness(evt, hostId!);
                }

                if (isPlayerEndured)
                {
                    res.Action = SecretConversionAction.EnduredPlayerToPublic;
                    string hostNote = hostFailureReason != null ? $" (host omitted: {hostFailureReason})" : string.Empty;
                    res.Details = $"public (player endured, added {addedWitnesses} witness(es){hostNote})";
                    return res;
                }
                else
                {
                    res.Action = SecretConversionAction.EnduredNpcToPublic;

                    // Generate seat_dispute_grudge_held
                    var grudgeEvt = CreateGrudgeHeldEvent(evt, slightedId, favoredId, settlementId, getTemplate, isEventIdTaken);
                    res.GeneratedGrudgeEvent = grudgeEvt;

                    string hostNote = hostFailureReason != null ? $" (host omitted: {hostFailureReason})" : string.Empty;
                    res.Details = $"public (added {addedWitnesses} witness(es), generated seat_dispute_grudge_held {grudgeEvt.EventId}{hostNote})";
                    return res;
                }
            }

            // 3. conduct_poisoned
            if (string.Equals(evt.Type, "conduct_poisoned", StringComparison.OrdinalIgnoreCase))
            {
                string poisonerId = string.Empty;
                if (evt.Participants != null && evt.Participants.TryGetValue("poisoner", out var pId) && !string.IsNullOrEmpty(pId))
                {
                    poisonerId = pId;
                }
                else if (!string.IsNullOrEmpty(evt.OriginatorHeroId))
                {
                    poisonerId = evt.OriginatorHeroId!;
                }
                else if (evt.KnownBy != null && evt.KnownBy.Count > 0 && !string.IsNullOrEmpty(evt.KnownBy[0].HeroId))
                {
                    poisonerId = evt.KnownBy[0].HeroId;
                }

                if (string.IsNullOrEmpty(poisonerId))
                {
                    res.Action = SecretConversionAction.UnchangedUnrecognized;
                    res.Details = "unchanged (no poisoner recorded on the event)";
                    return res;
                }

                evt.SecretOwnerHeroIds = new List<string> { poisonerId };

                res.Action = SecretConversionAction.PoisonedAddedOwner;
                res.Details = $"added owner ({poisonerId})";
                return res;
            }

            // 4. Retired secret types
            if (RetiredSecretTypes.Contains(evt.Type))
            {
                evt.SecretRetired = true;
                res.Action = SecretConversionAction.Retired;
                res.Details = "retired";
                return res;
            }

            // 5. Other unlisted secret types
            res.Action = SecretConversionAction.UnchangedUnrecognized;
            res.Details = "unchanged (unrecognized secret type)";
            return res;
        }

        private static int EnsureHop0Witness(WorldEvent evt, string heroId)
        {
            if (string.IsNullOrEmpty(heroId)) return 0;
            evt.KnownBy ??= new List<KnownByEntry>();

            var existing = evt.KnownBy.FirstOrDefault(k => string.Equals(k?.HeroId, heroId, StringComparison.Ordinal));
            if (existing != null)
            {
                existing.Hop = 0;
                return 0;
            }

            evt.KnownBy.Add(new KnownByEntry
            {
                HeroId = heroId,
                Hop = 0,
                LearnedDay = evt.Day
            });
            return 1;
        }

        private static string ExtractSettlementId(WorldEvent evt)
        {
            if (evt.Facts != null)
            {
                foreach (var f in evt.Facts)
                {
                    if (f.Vars != null && f.Vars.TryGetValue("SETTLEMENT", out var sVar) && !string.IsNullOrEmpty(sVar))
                    {
                        return sVar.StartsWith("settlement:", StringComparison.OrdinalIgnoreCase)
                            ? sVar.Substring("settlement:".Length)
                            : sVar;
                    }
                }
            }

            if (evt.Participants != null && evt.Participants.TryGetValue("settlement", out var pSet) && !string.IsNullOrEmpty(pSet))
            {
                return pSet.StartsWith("settlement:", StringComparison.OrdinalIgnoreCase)
                    ? pSet.Substring("settlement:".Length)
                    : pSet;
            }

            return string.Empty;
        }

        private static WorldEvent CreateGrudgeHeldEvent(
            WorldEvent enduredEvt,
            string slightedId,
            string favoredId,
            string settlementId,
            Func<string, EventTemplate?>? getTemplate,
            Func<string, bool> isEventIdTaken)
        {
            var participants = enduredEvt.Participants != null
                ? new Dictionary<string, string>(enduredEvt.Participants)
                : new Dictionary<string, string>();

            string newEventId = EventId.Mint(
                "seat_dispute_grudge_held",
                enduredEvt.Day,
                participants.Values.Where(v => !string.IsNullOrEmpty(v)),
                isEventIdTaken);

            var grudgeEvt = new WorldEvent
            {
                EventId = newEventId,
                Type = "seat_dispute_grudge_held",
                Day = enduredEvt.Day,
                Origin = EventOrigin.Secret,
                LinkedEventId = enduredEvt.EventId,
                SituationId = enduredEvt.SituationId ?? "seat_dispute",
                DramaWeight = 3,
                DramaScale = 10,
                Participants = participants,
                SecretOwnerHeroIds = !string.IsNullOrEmpty(slightedId) ? new List<string> { slightedId } : new List<string>(),
                KnownBy = new List<KnownByEntry>()
            };

            if (!string.IsNullOrEmpty(slightedId))
            {
                grudgeEvt.KnownBy.Add(new KnownByEntry
                {
                    HeroId = slightedId,
                    Hop = 0,
                    LearnedDay = enduredEvt.Day
                });
            }

            var template = getTemplate?.Invoke("seat_dispute_grudge_held");
            if (template != null)
            {
                var bindings = new Dictionary<string, string>();
                if (!string.IsNullOrEmpty(slightedId)) bindings["SLIGHTED"] = slightedId;
                if (!string.IsNullOrEmpty(favoredId)) bindings["FAVORED"] = favoredId;
                if (!string.IsNullOrEmpty(settlementId)) bindings["SETTLEMENT"] = settlementId;

                var sub = TemplateBinder.Bind(template, bindings, enduredEvt.Day, enduredEvt.EventId, out _);
                if (sub?.Facts != null && sub.Facts.Count > 0)
                {
                    grudgeEvt.Facts = sub.Facts;
                }
            }

            if (grudgeEvt.Facts.Count == 0)
            {
                grudgeEvt.Facts.Add(new Fact
                {
                    Id = "who",
                    Category = FactCategory.Who,
                    TextId = "VividWorld_Fact_SeatDisputeGrudgeHeld_Who",
                    Text = "{SLIGHTED} says nothing openly, but holds a bitter grudge against {FAVORED}",
                    Vars = new Dictionary<string, string>
                    {
                        ["SLIGHTED"] = $"hero:{slightedId}",
                        ["FAVORED"] = $"hero:{favoredId}"
                    },
                    Fragility = 1
                });
            }

            return grudgeEvt;
        }

        private static WorldEvent CloneEvent(WorldEvent original)
        {
            string json = JsonConvert.SerializeObject(original);
            return JsonConvert.DeserializeObject<WorldEvent>(json) ?? original;
        }
    }
}
