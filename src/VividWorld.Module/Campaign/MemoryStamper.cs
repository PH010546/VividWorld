using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using TaleWorlds.CampaignSystem;
using VividWorld.Core.Config;
using VividWorld.Core.Events;
using VividWorld.Core.Memory;
using VividWorld.Core.Persistence;
using VividWorld.Core.Rumors;

namespace VividWorld.Campaign
{
    internal sealed class MemoryStamper
    {
        private readonly VividWorldConfig _config;
        private readonly HeroLookup _heroLookup;
        private readonly string _playerHeroId;
        private WorldEventStore? _store;
        private RumorEngine? _engine;
        private RumorPropagationScheduler? _scheduler;

        public int EntriesMarkedOutdatedCount { get; private set; }
        public int EventsDormantByOutdatingCount { get; private set; }

        public MemoryStamper(VividWorldConfig config, HeroLookup heroLookup, string playerHeroId, WorldEventStore? store = null)
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));
            _heroLookup = heroLookup ?? throw new ArgumentNullException(nameof(heroLookup));
            _playerHeroId = playerHeroId ?? string.Empty;
            _store = store;
        }

        public void SetStore(WorldEventStore store)
        {
            _store = store;
        }

        public void SetEngineAndScheduler(RumorEngine engine, RumorPropagationScheduler scheduler)
        {
            _engine = engine;
            _scheduler = scheduler;
        }

        public void StampLearned(WorldEvent evt, IEnumerable<KnownByEntry>? entries)
        {
            if (!_config.Memory.Enabled || evt == null || entries == null) return;
            if (evt.Origin == EventOrigin.Secret && !evt.State.Leaked) return;

            foreach (var entry in entries)
            {
                if (string.Equals(entry.HeroId, _playerHeroId, StringComparison.Ordinal)) continue;
                StampOne(evt, entry, "learned", entry.LearnedDay, 0, null);
            }
        }

        public int StampOutdated(WorldEvent release, IEnumerable<KnownByEntry>? newKnowers, double day)
        {
            if (release == null || string.IsNullOrEmpty(release.LinkedEventId) || _store == null || newKnowers == null)
            {
                return 0;
            }

            var captureEvent = _store.Load(release.LinkedEventId!);
            if (captureEvent == null)
            {
                return 0;
            }

            var heroIds = newKnowers
                .Where(k => !string.IsNullOrEmpty(k.HeroId))
                .Select(k => k.HeroId)
                .ToList();

            if (heroIds.Count == 0)
            {
                return 0;
            }

            var markedHeroIds = new List<string>();
            int marked = Outdating.MarkOutdated(captureEvent, heroIds, day, markedHeroIds);

            if (marked > 0)
            {
                _store.Upsert(captureEvent);
                EntriesMarkedOutdatedCount += marked;

                foreach (var heroId in markedHeroIds)
                {
                    ModLog.Info(OutdatingLogFormatter.FormatHeroOutdated(heroId, captureEvent.EventId, release.EventId, day));
                }

                int totalNpcKnowers = captureEvent.KnownBy
                    .Count(k => !string.Equals(k.HeroId, _playerHeroId, StringComparison.Ordinal));
                int outdatedNpcKnowers = captureEvent.KnownBy
                    .Count(k => !string.Equals(k.HeroId, _playerHeroId, StringComparison.Ordinal) && Outdating.IsOutdated(k));

                ModLog.Info(OutdatingLogFormatter.FormatOutdatedSummary(
                    outdatedNpcKnowers, totalNpcKnowers, captureEvent.EventId, release.EventId));

                if (_engine != null && !captureEvent.State.Dormant)
                {
                    var dormancyReason = _engine.DormancyReasonFor(captureEvent, day);
                    if (dormancyReason != null)
                    {
                        captureEvent.State.Dormant = true;
                        _store.Upsert(captureEvent);
                        _scheduler?.RemoveActive(captureEvent.EventId);
                        ModLog.Info(MemoryLogFormatter.FormatDormancy(captureEvent.EventId, dormancyReason));
                        if (dormancyReason.Kind == DormancyKind.AllOutdated)
                        {
                            EventsDormantByOutdatingCount++;
                        }
                    }
                }
            }

            return marked;
        }

        public void StampReheard(WorldEvent evt, IReadOnlyList<RehearRecord>? records, string tellerId)
        {
            if (!_config.Memory.Enabled || evt == null || records == null) return;
            if (evt.Origin == EventOrigin.Secret && !evt.State.Leaked) return;

            foreach (var rec in records)
            {
                if (string.Equals(rec.Entry.HeroId, _playerHeroId, StringComparison.Ordinal)) continue;

                if (rec.Kind == RehearKind.SameTellerIgnored)
                {
                    ModLog.Info(TellerLogFormatter.FormatSameTellerIgnored(rec.Entry.HeroId, tellerId, evt.EventId));
                }
                else if (rec.Kind == RehearKind.SameTellerRelearned)
                {
                    string how = TellerLogFormatter.FormatSameTellerRelearnedHow(tellerId, rec.OldHop, rec.Entry.Hop, rec.Rule.Reason);
                    double startDay = rec.Entry.LastHeardDay ?? evt.Day;
                    int heardCount = rec.Entry.HeardCount ?? 0;
                    StampOne(evt, rec.Entry, how, startDay, heardCount, rec.Entry.ForgetDay);
                }
                else
                {
                    string how = rec.Rule.AdoptVersion
                        ? string.Format(CultureInfo.InvariantCulture,
                            "re-heard from {0}, adopted version hop {1}->{2} ({3})",
                            tellerId, rec.OldHop, rec.Entry.Hop, rec.Rule.Reason)
                        : string.Format(CultureInfo.InvariantCulture,
                            "re-heard from {0}, version kept ({1})",
                            tellerId, rec.Rule.Reason);

                    double startDay = rec.Entry.LastHeardDay ?? evt.Day;
                    int heardCount = rec.Entry.HeardCount ?? 0;
                    StampOne(evt, rec.Entry, how, startDay, heardCount, rec.Entry.ForgetDay);
                }
            }
        }

        public bool EnsureStamped(WorldEvent evt)
        {
            if (!_config.Memory.Enabled || evt == null) return false;
            if (evt.Origin == EventOrigin.Secret && !evt.State.Leaked) return false;

            bool any = false;
            foreach (var entry in evt.KnownBy)
            {
                if (string.Equals(entry.HeroId, _playerHeroId, StringComparison.Ordinal)) continue;
                if (entry.Interest == null)
                {
                    double startDay = entry.LastHeardDay ?? entry.LearnedDay;
                    int heardCount = entry.HeardCount ?? 0;
                    if (StampOne(evt, entry, "backfilled", startDay, heardCount, entry.ForgetDay))
                    {
                        any = true;
                    }
                }
            }
            return any;
        }

        public void RestampAfterLeak(WorldEvent evt)
        {
            if (!_config.Memory.Enabled || evt == null) return;

            foreach (var entry in evt.KnownBy)
            {
                if (string.Equals(entry.HeroId, _playerHeroId, StringComparison.Ordinal)) continue;
                double startDay = evt.State.LeakedDay;
                int heardCount = entry.HeardCount ?? 0;
                // 洩漏後重算：ForgetDay 不取舊值的 max
                StampOne(evt, entry, "restamped after leak", startDay, heardCount, null);
            }
        }

        private bool StampOne(
            WorldEvent evt,
            KnownByEntry entry,
            string how,
            double startDay,
            int heardCount,
            double? oldForgetDay)
        {
            if (!_config.Memory.Enabled) return false;
            if (string.Equals(entry.HeroId, _playerHeroId, StringComparison.Ordinal)) return false;
            if (evt.Origin == EventOrigin.Secret && !evt.State.Leaked) return false;

            Hero? knowerHero = _heroLookup.Get(entry.HeroId);
            InterestHeroFacts? knowerFacts = knowerHero != null ? ToFacts(knowerHero) : null;

            var participants = new List<InterestParticipant>(evt.Participants?.Count ?? 0);
            if (evt.Participants != null)
            {
                foreach (var kvp in evt.Participants)
                {
                    Hero? pHero = _heroLookup.Get(kvp.Value);
                    int? rel = null;
                    if (knowerHero != null && pHero != null && knowerHero != pHero)
                    {
                        rel = knowerHero.GetBaseHeroRelation(pHero);
                    }
                    participants.Add(new InterestParticipant
                    {
                        Role = kvp.Key,
                        HeroId = kvp.Value,
                        Facts = pHero != null ? ToFacts(pHero) : null,
                        PersonalRelation = rel
                    });
                }
            }

            var interestRes = InterestCalculator.Compute(knowerFacts, entry.HeroId, participants, evt.DramaWeight, _config.Memory);
            var spanRes = MemorySpan.Compute(startDay, interestRes.Interest, evt.DramaWeight, heardCount, oldForgetDay, _config.Memory);

            entry.Interest = interestRes.Interest;
            entry.InterestSource = interestRes.InterestSource;
            entry.ForgetDay = spanRes.ForgetDay;

            int drama = Math.Max(1, Math.Min(5, evt.DramaWeight));
            string logLine = MemoryLogFormatter.FormatCalculation(
                entry.HeroId,
                how,
                evt.EventId,
                interestRes,
                drama,
                _config.Memory.DramaReference,
                _config.Memory.BaseDays,
                spanRes,
                _config.Memory);

            ModLog.Info(logLine);
            return true;
        }

        private static InterestHeroFacts ToFacts(Hero hero)
        {
            var siblingIds = hero.Siblings != null
                ? hero.Siblings.Select(s => s.StringId).Where(s => !string.IsNullOrEmpty(s)).ToList()
                : (IReadOnlyList<string>)Array.Empty<string>();

            return new InterestHeroFacts
            {
                HeroId = hero.StringId ?? string.Empty,
                ClanId = hero.Clan?.StringId,
                FatherId = hero.Father?.StringId,
                MotherId = hero.Mother?.StringId,
                SpouseId = hero.Spouse?.StringId,
                SiblingIds = siblingIds
            };
        }
    }
}
