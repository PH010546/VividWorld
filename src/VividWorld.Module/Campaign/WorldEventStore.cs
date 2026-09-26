using System;
using System.Collections.Generic;
using VividWorld.Api;
using VividWorld.Core.Channels;
using VividWorld.Core.Config;
using VividWorld.Core.Events;
using VividWorld.Core.Grudges;
using VividWorld.Core.Ingest;
using VividWorld.Core.Persistence;
using VividWorld.Core.Rumors;

namespace VividWorld.Campaign
{
    internal sealed class WorldEventStore : IWorldEventSink
    {
        private readonly VividWorldConfig _config;
        private readonly EventShardStore _store;
        private readonly RumorIndex _index;
        private readonly KnownByIndex _knownBy;
        private readonly IPropagationChannel _channel;
        private readonly IHeroTraitLookup _traits;
        private readonly string _playerHeroId;
        private readonly long _campaignSeed;
        private readonly MemoryStamper? _stamper;

        private readonly GrudgeIndex _grudges;
        private readonly List<EventSubmission> _pending = new();
        private bool _isSaving;
        private bool _isIndexLoaded;
        private RumorPropagationScheduler? _scheduler;

        internal WorldEventStore(VividWorldConfig config,
                                 EventShardStore store,
                                 RumorIndex index,
                                 KnownByIndex knownBy,
                                 IPropagationChannel channel,
                                 IHeroTraitLookup traits,
                                 string playerHeroId,
                                 long campaignSeed,
                                 MemoryStamper? stamper = null,
                                 GrudgeIndex? grudges = null)
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));
            _store = store ?? throw new ArgumentNullException(nameof(store));
            _index = index ?? throw new ArgumentNullException(nameof(index));
            _knownBy = knownBy ?? throw new ArgumentNullException(nameof(knownBy));
            _channel = channel ?? throw new ArgumentNullException(nameof(channel));
            _traits = traits ?? throw new ArgumentNullException(nameof(traits));
            _playerHeroId = playerHeroId ?? string.Empty;
            _campaignSeed = campaignSeed;
            _stamper = stamper;
            _grudges = grudges ?? new GrudgeIndex();
        }

        public long CampaignSeed => _campaignSeed;
        public VividWorldConfig Config => _config;
        public string PlayerHeroId => _playerHeroId;
        internal MemoryStamper? Stamper => _stamper;

        internal void SetScheduler(RumorPropagationScheduler scheduler)
        {
            _scheduler = scheduler;
        }

        internal void MarkIndexLoaded()
        {
            _isIndexLoaded = true;
        }

        public IngestResult Submit(EventSubmission submission, out string? eventId, out string? rejectionReason)
        {
            eventId = null;
            rejectionReason = null;

            if (submission == null)
            {
                rejectionReason = "Submission is null";
                return IngestResult.RejectedInvalid;
            }

            // Deferred: SetSaving(true) 期間 或 索引尚未載入完成
            if (_isSaving || !_isIndexLoaded)
            {
                _pending.Add(submission);
                ModLog.Info($"Event submission deferred (saving={_isSaving}, indexLoaded={_isIndexLoaded}).");
                return IngestResult.Deferred;
            }

            // 1. 驗證
            var outcome = EventSubmissionValidator.Validate(submission, _config.Persistence, id => _index.Find(id) != null);
            if (!outcome.IsValid)
            {
                rejectionReason = outcome.RejectionReason;
                ModLog.Info($"Event submission rejected: {rejectionReason}");
                return IngestResult.RejectedInvalid;
            }

            // 2. 鑄 id
            string mintedId;
            try
            {
                mintedId = EventId.Mint(submission.Type, submission.Day, submission.Participants.Values, id => _index.Find(id) != null);
            }
            catch (Exception ex)
            {
                rejectionReason = "Could not generate unique event ID: " + ex.Message;
                return IngestResult.RejectedDuplicate;
            }

            if (_index.Find(mintedId) != null)
            {
                rejectionReason = "Event ID already exists";
                return IngestResult.RejectedDuplicate;
            }

            // 3. 解析 DramaWeight
            int drama;
            if (submission.DramaWeight.HasValue)
            {
                drama = submission.DramaWeight.Value;
            }
            else if (_config.Propagation.DefaultDramaByEventType != null
                     && _config.Propagation.DefaultDramaByEventType.TryGetValue(submission.Type, out int typeDrama))
            {
                drama = typeDrama;
            }
            else
            {
                drama = _config.Propagation.DefaultDrama;
            }
            drama = Math.Max(1, Math.Min(5, drama));

            // 4. Hop0Seeding.Seed(...)
            var evt = new WorldEvent
            {
                EventId = mintedId,
                Type = submission.Type,
                Day = submission.Day,
                Origin = submission.Origin ?? EventOrigin.Secret,
                LinkedEventId = submission.LinkedEventId,
                SituationId = submission.SituationId,
                DramaWeight = drama,
                Participants = new Dictionary<string, string>(submission.Participants),
                Facts = new List<Fact>(submission.Facts),
                KnownBy = new List<KnownByEntry>(),
                State = new RumorState()
            };

            Hop0Seeding.Seed(evt, submission, _channel, _traits, _config.Propagation, _playerHeroId, submission.Day);
            _stamper?.StampLearned(evt, evt.KnownBy);
            _stamper?.StampOutdated(evt, evt.KnownBy, evt.Day);

            // 5. store.Upsert(evt) -> KnownBy.NoteKnower -> scheduler.OnIngested(evt)
            _store.Upsert(evt);
            foreach (var knower in evt.KnownBy)
            {
                if (!string.IsNullOrEmpty(knower.HeroId))
                {
                    _knownBy.NoteKnower(knower.HeroId, evt.EventId, evt.Day);
                }
            }
            _scheduler?.OnIngested(evt);

            // 6. 觸發公開 API：僅 Origin == Public
            if (evt.Origin == EventOrigin.Public)
            {
                TriggerPublicEventOccurred(evt);
            }

            eventId = mintedId;
            return IngestResult.Accepted;
        }

        internal static void TriggerPublicEventOccurred(WorldEvent evt)
        {
            if (evt == null) return;
            string detailJson = VividJson.Write(evt);
            string summary = evt.Type;
            foreach (var kvp in evt.Participants)
            {
                string role = kvp.Key;
                string heroId = kvp.Value;
                if (!string.IsNullOrEmpty(heroId))
                {
                    VividWorldEventApi.RaiseEventOccurred(heroId, evt.EventId, evt.Type, role, evt.Day, summary, detailJson);
                }
            }
        }

        internal void SetSaving(bool saving)
        {
            _isSaving = saving;
        }

        internal string SerializePendingIngest(int maxChars)
        {
            if (_pending.Count == 0) return "";

            string json = VividJson.Write(_pending);
            if (json.Length <= maxChars)
            {
                return json;
            }

            ModLog.Warn($"Pending ingest queue exceeded {maxChars} characters; dropping oldest submissions.");
            while (_pending.Count > 0 && json.Length > maxChars)
            {
                _pending.RemoveAt(0);
                json = _pending.Count > 0 ? VividJson.Write(_pending) : "";
            }

            return json;
        }

        internal void RedeliverPending(string? json)
        {
            if (string.IsNullOrEmpty(json)) return;
            try
            {
                var list = VividJson.Read<List<EventSubmission>>(json!);
                if (list != null)
                {
                    int submitted = 0;
                    int skipped = 0;
                    foreach (var submission in list)
                    {
                        string? existingId = IngestDedupe.FindSameSubmission(_index, submission);
                        if (existingId != null)
                        {
                            skipped++;
                            ModLog.Info(string.Format(System.Globalization.CultureInfo.InvariantCulture,
                                "Pending ingest skipped: {0} on day {1:F1} is already in the store as {2} (it was flushed before the snapshot was taken).",
                                submission.Type, submission.Day, existingId));
                        }
                        else
                        {
                            submitted++;
                            Submit(submission, out _, out _);
                        }
                    }

                    ModLog.Info(string.Format(System.Globalization.CultureInfo.InvariantCulture,
                        "Pending ingest redelivered: {0} submitted, {1} already in the store.",
                        submitted, skipped));
                }
            }
            catch (Exception ex)
            {
                ModLog.Error("Failed to redeliver pending ingest submissions.", ex);
            }
        }

        internal void Flush()
        {
            if (_pending.Count > 0)
            {
                var copy = new List<EventSubmission>(_pending);
                _pending.Clear();
                foreach (var submission in copy)
                {
                    Submit(submission, out _, out _);
                }
            }
            _store.Flush();
        }

        internal WorldEvent? Load(string eventId)
        {
            return _store.Load(eventId, _index);
        }

        internal void Upsert(WorldEvent evt)
        {
            _store.Upsert(evt);
        }

        internal RumorIndex Index => _index;
        internal KnownByIndex KnownBy => _knownBy;
        internal GrudgeIndex Grudges => _grudges;
        internal IHeroTraitLookup Traits => _traits;
        internal EventShardStore ShardStore => _store;
    }
}
