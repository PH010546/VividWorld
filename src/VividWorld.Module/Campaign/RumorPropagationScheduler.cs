using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Library;
using VividWorld.Core.Channels;
using VividWorld.Core.Config;
using VividWorld.Core.Events;
using VividWorld.Core.Memory;
using VividWorld.Core.Persistence;
using VividWorld.Core.Rumors;
using VividWorld.Debug;

namespace VividWorld.Campaign
{
    internal sealed class RumorPropagationScheduler
    {
        private readonly VividWorldConfig _config;
        private readonly WorldEventStore _store;
        private readonly RumorEngine _engine;
        private readonly KnownByIndex _knownBy;
        private readonly HeroLookup? _heroLookup;
        private readonly IHeroTraitLookup? _traitLookup;
        private readonly MemoryStamper? _stamper;
        private readonly DailyRelationBudget _dailyRelationBudget = new();

        private readonly List<string> _activeRing = new();
        private readonly HashSet<string> _activeRingSet = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _secretWatch = new(StringComparer.OrdinalIgnoreCase);
        private readonly TellerRing _tellers = new();

        private int _sessionTurns;
        private readonly int[] _sessionToldByDrama = new int[5];
        private int _sessionSkippedNotEligible;
        private int _sessionLeftRing;
        private int _sessionReheardCounted;
        private int _sessionReheardSameTellerIgnored;
        private int _sessionReheardSameTellerRelearned;

        internal int TickCursor
        {
            get => _tellers.Cursor;
            set => _tellers.Cursor = value;
        }

        /// <summary>存檔要存的是「下一個輪到誰」（§7.3.1）；輪是空的就回 null。</summary>
        internal string? TickCursorHeroId => _tellers.CurrentId;

        /// <summary>讀檔時把游標移回存檔裡那一位身上；他已經不在輪裡就回 false，由呼叫端退回位置值。</summary>
        internal bool RestoreTickCursorTo(string heroId) => _tellers.SetCursorTo(heroId);

        internal int ActiveRingSize => _activeRing.Count;
        internal int SecretWatchSize => _secretWatch.Count;
        internal IReadOnlyList<string> ActiveRing => _activeRing;
        internal RumorEngine Engine => _engine;

        internal int TellerRingCount => _tellers.Count;
        internal bool IsInTellerRing(string heroId) => _tellers.Contains(heroId);
        internal bool IsActiveEvent(string eventId) => _activeRingSet.Contains(eventId);

        internal int SessionTurns => _sessionTurns;
        internal IReadOnlyList<int> SessionToldByDrama => _sessionToldByDrama;
        internal int SessionSkippedNotEligible => _sessionSkippedNotEligible;
        internal int SessionLeftRing => _sessionLeftRing;
        internal int SessionReheardCounted => _sessionReheardCounted;
        internal int SessionReheardSameTellerIgnored => _sessionReheardSameTellerIgnored;
        internal int SessionReheardSameTellerRelearned => _sessionReheardSameTellerRelearned;

        internal DailyRelationBudget DailyRelationBudget => _dailyRelationBudget;
        internal void ResetDailyBudget() => _dailyRelationBudget.Reset();

        internal RumorPropagationScheduler(VividWorldConfig config,
                                           WorldEventStore store,
                                           RumorEngine engine,
                                           KnownByIndex knownBy,
                                           HeroLookup? heroLookup = null,
                                           IHeroTraitLookup? traitLookup = null,
                                           MemoryStamper? stamper = null)
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));
            _store = store ?? throw new ArgumentNullException(nameof(store));
            _engine = engine ?? throw new ArgumentNullException(nameof(engine));
            _knownBy = knownBy ?? throw new ArgumentNullException(nameof(knownBy));
            _heroLookup = heroLookup;
            _traitLookup = traitLookup;
            _stamper = stamper;
        }

        private void AddActive(string eventId)
        {
            if (_activeRingSet.Add(eventId))
            {
                _activeRing.Add(eventId);
            }
        }

        internal void RemoveActive(string eventId)
        {
            if (_activeRingSet.Remove(eventId))
            {
                _activeRing.Remove(eventId);
            }
        }

        private void AddKnowersToTellers(IEnumerable<string>? heroIds)
        {
            if (heroIds == null) return;
            string playerId = _store.PlayerHeroId;
            foreach (var id in heroIds)
            {
                if (!string.IsNullOrEmpty(id) && !string.Equals(id, playerId, StringComparison.Ordinal))
                {
                    _tellers.Add(id);
                }
            }
        }

        internal void RebuildFrom(RumorIndex index)
        {
            _activeRing.Clear();
            _activeRingSet.Clear();
            _secretWatch.Clear();
            var tellerIds = new List<string>();
            string playerId = _store.PlayerHeroId;

            if (index != null)
            {
                foreach (var entry in index.Entries)
                {
                    if (entry == null || string.IsNullOrEmpty(entry.EventId) || entry.Dormant)
                        continue;

                    if (entry.Secret && !entry.Leaked)
                    {
                        _secretWatch.Add(entry.EventId);
                    }
                    else
                    {
                        AddActive(entry.EventId);
                        if (entry.KnownByHeroIds != null)
                        {
                            foreach (var hId in entry.KnownByHeroIds)
                            {
                                if (!string.IsNullOrEmpty(hId) && !string.Equals(hId, playerId, StringComparison.Ordinal))
                                {
                                    tellerIds.Add(hId);
                                }
                            }
                        }
                    }
                }
            }

            _tellers.Rebuild(tellerIds);
            _dailyRelationBudget.Reset();
            ConsequenceRunner.ResetDisabledLogThrottle();
        }

        internal void OnIngested(WorldEvent evt)
        {
            if (evt == null || string.IsNullOrEmpty(evt.EventId) || evt.State?.Dormant == true)
                return;

            if (evt.Origin == EventOrigin.Secret && !(evt.State?.Leaked ?? false))
            {
                _secretWatch.Add(evt.EventId);
            }
            else
            {
                AddActive(evt.EventId);
                if (evt.KnownBy != null)
                {
                    AddKnowersToTellers(evt.KnownBy.Select(k => k.HeroId));
                }
                SettleWitnessOpinions(evt, evt.Day);
            }
        }

        internal void PromoteLeakedEvent(WorldEvent evt)
        {
            if (evt == null || string.IsNullOrEmpty(evt.EventId)) return;
            _secretWatch.Remove(evt.EventId);
            AddActive(evt.EventId);
            if (evt.KnownBy != null)
            {
                AddKnowersToTellers(evt.KnownBy.Select(k => k.HeroId));
            }
            SettleWitnessOpinions(evt, evt.State?.LeakedDay >= 0 ? evt.State.LeakedDay : evt.Day);
        }

        /// <summary>
        /// 現場目擊者（hop 0、但**不是**這則事件的參與者）也會對事件裡的人改觀。
        /// 不做這一段的話方向是反的：人在現場看到的毫無感覺，隔三手聽說的反而扣好感度。
        /// 參與者不走這裡——他們的反應是情境分支自己宣告的恩怨（SE2），量更大也更具體，
        /// 兩邊都算就是同一件事記兩次。
        /// 秘密事件在走漏之前 FactsAtHop 回傳空集合，所以只在這裡（公開）與走漏時才結算。
        /// </summary>
        private void SettleWitnessOpinions(WorldEvent evt, double day)
        {
            if (evt?.KnownBy == null || _heroLookup == null || _traitLookup == null) return;

            var witnesses = new List<KnownByEntry>();
            foreach (var k in evt.KnownBy)
            {
                if (k == null || k.Hop != 0 || string.IsNullOrEmpty(k.HeroId)) continue;
                if (evt.RoleOf(k.HeroId) != null) continue;
                witnesses.Add(k);
            }

            if (witnesses.Count == 0) return;

            ConsequenceRunner.Settle(
                evt, witnesses, day,
                _engine, _store, _heroLookup, _traitLookup,
                _dailyRelationBudget, _config);
        }

        internal void HourlyTick(double day, int hourOfDay)
        {
            using (DevMetrics.Measure("hourly"))
            {
                var batch = _tellers.TakeBatch(_config.Scheduling.TellersPerHourlyTick);
                string playerId = _store.PlayerHeroId;
                var traits = _store.Traits;

                if (_config.Debug.VerboseTickLog)
                {
                    ModLog.Info($"HourlyTick at day {day:0.00} hour {hourOfDay}: {batch.Count} teller(s) this tick, teller ring size {_tellers.Count}, cursor {_tellers.Cursor}");
                }

                foreach (var t in batch)
                {
                    // 1. profile = traits.Of(T)；null、!IsAlive、或 T 是玩家 ⇒ Remove，印 left the ring: hero not found or dead；continue
                    var profile = traits.Of(t);
                    if (profile == null || !profile.IsAlive || string.Equals(t, playerId, StringComparison.Ordinal))
                    {
                        _tellers.Remove(t);
                        _sessionLeftRing++;
                        if (_config.Debug.LogTellerTurns)
                        {
                            ModLog.Info(TellerLogFormatter.FormatLeftRingHeroNotFoundOrDead(t));
                        }
                        continue;
                    }

                    // 2. !Eligibility.IsEligible(profile) ⇒ 計數、印 skipped；continue（留在輪裡）
                    if (!Eligibility.IsEligible(profile))
                    {
                        _sessionSkippedNotEligible++;
                        if (_config.Debug.LogTellerTurns)
                        {
                            ModLog.Info(TellerLogFormatter.FormatSkippedNotEligible(t));
                        }
                        continue;
                    }

                    // 3. ids = _knownBy.EventsKnownBy(T)；不在作用中集合的只計 inactive；在的 _store.Load（null 也計 inactive）
                    //    每則先 _stamper?.EnsureStamped(evt)，回傳 true 就 _store.Upsert(evt)
                    var ids = _knownBy.EventsKnownBy(t, day);
                    int inactiveCount = 0;
                    var activeEvents = new List<WorldEvent>();

                    foreach (var id in ids)
                    {
                        if (!_activeRingSet.Contains(id))
                        {
                            inactiveCount++;
                        }
                        else
                        {
                            var evt = _store.Load(id);
                            if (evt == null)
                            {
                                inactiveCount++;
                            }
                            else
                            {
                                if (_stamper != null && _stamper.EnsureStamped(evt))
                                {
                                    _store.Upsert(evt);
                                }
                                activeEvents.Add(evt);
                            }
                        }
                    }

                    // 4. choice = _engine.ChooseTopic(T, events, day, hourOfDay)
                    var choice = _engine.ChooseTopic(t, activeEvents, day, hourOfDay);

                    // 5. PickedIndex < 0 ⇒ Remove，印 left the ring: no tellable topic …；continue
                    if (choice.PickedIndex < 0)
                    {
                        _tellers.Remove(t);
                        _sessionLeftRing++;
                        if (_config.Debug.LogTellerTurns)
                        {
                            ModLog.Info(TellerLogFormatter.FormatLeftRingNoTellableTopic(
                                t, ids.Count, choice.Exclusions, inactiveCount,
                                _knownBy.HiddenFutureCountKnownBy(t, day)));
                        }
                        continue;
                    }

                    // 6. outcome = _engine.PropagateFromTeller(picked, T, day, hourOfDay)
                    var picked = activeEvents.FirstOrDefault(e =>
                        string.Equals(e.EventId, choice.PickedEventId, StringComparison.Ordinal));
                    if (picked == null)
                    {
                        // 走不到：PickedIndex >= 0 就保證 PickedEventId 來自 activeEvents 裡的某一則。
                        // 真的發生就是上游壞了，記一行 ERROR 並讓這位講述者這一輪什麼都不做（留在輪裡）。
                        ModLog.Error(string.Format(CultureInfo.InvariantCulture,
                            "Teller {0}: picked event {1} not found in active set ({2} active) - turn skipped",
                            t, choice.PickedEventId ?? "(null)", activeEvents.Count));
                        continue;
                    }
                    var outcome = _engine.PropagateFromTeller(picked, t, day, hourOfDay);

                    if (outcome.NewKnowers != null && outcome.NewKnowers.Count > 0)
                    {
                        _stamper?.StampLearned(picked, outcome.NewKnowers);
                        _stamper?.StampOutdated(picked, outcome.NewKnowers, day);
                        foreach (var k in outcome.NewKnowers)
                        {
                            if (!string.IsNullOrEmpty(k.HeroId))
                            {
                                _knownBy.NoteKnower(k.HeroId, picked.EventId, picked.Day);
                                if (!string.Equals(k.HeroId, playerId, StringComparison.Ordinal))
                                {
                                    _tellers.Add(k.HeroId);
                                }
                            }
                        }

                        if (_config.Debug.AnnounceRumorEvents)
                        {
                            foreach (var k in outcome.NewKnowers)
                            {
                                var tellerHero = !string.IsNullOrEmpty(outcome.TellerHeroId) ? Hero.Find(outcome.TellerHeroId) : null;
                                string tellerName = tellerHero?.Name?.ToString() ?? outcome.TellerHeroId ?? "?";
                                var knowerHero = !string.IsNullOrEmpty(k.HeroId) ? Hero.Find(k.HeroId) : null;
                                string knowerName = knowerHero?.Name?.ToString() ?? k.HeroId ?? "?";
                                ChannelKind kind = ChannelKind.SameSettlement;
                                if (!string.IsNullOrEmpty(k.HeroId))
                                {
                                    outcome.ChannelKinds.TryGetValue(k.HeroId!, out kind);
                                }
                                string text = $"[VW] {tellerName} -> {knowerName}  {picked.EventId}  hop {k.Hop}  {kind}";
                                InformationManager.DisplayMessage(new InformationMessage(text));
                            }
                        }
                    }

                    if (outcome.Reheard != null && outcome.Reheard.Count > 0)
                    {
                        _stamper?.StampReheard(picked, outcome.Reheard, t);
                        foreach (var r in outcome.Reheard)
                        {
                            if (r.WasForgotten && !string.Equals(r.Entry.HeroId, playerId, StringComparison.Ordinal))
                            {
                                _tellers.Add(r.Entry.HeroId);
                            }

                            switch (r.Kind)
                            {
                                case RehearKind.Counted:
                                    _sessionReheardCounted++;
                                    break;
                                case RehearKind.SameTellerIgnored:
                                    _sessionReheardSameTellerIgnored++;
                                    break;
                                case RehearKind.SameTellerRelearned:
                                    _sessionReheardSameTellerRelearned++;
                                    break;
                            }
                        }
                    }

                    _sessionTurns++;
                    int drama = Math.Max(1, Math.Min(5, picked.DramaWeight));
                    _sessionToldByDrama[drama - 1]++;

                    // M6.5：結算傳聞好感度變更
                    var affectedKnowers = new List<KnownByEntry>();
                    if (outcome.NewKnowers != null)
                    {
                        affectedKnowers.AddRange(outcome.NewKnowers);
                    }
                    if (outcome.Reheard != null)
                    {
                        foreach (var record in outcome.Reheard)
                        {
                            if (record.Rule.AdoptVersion && record.Entry != null)
                            {
                                affectedKnowers.Add(record.Entry);
                            }
                        }
                    }

                    if (affectedKnowers.Count > 0 && _heroLookup != null && _traitLookup != null)
                    {
                        ConsequenceRunner.Settle(
                            picked, affectedKnowers, day,
                            _engine, _store, _heroLookup, _traitLookup,
                            _dailyRelationBudget, _config);
                    }

                    _store.Upsert(picked);

                    var dormancyReason = _engine.DormancyReasonFor(picked, day);
                    if (dormancyReason != null)
                    {
                        picked.State.Dormant = true;
                        _store.Upsert(picked);
                        RemoveActive(picked.EventId);
                        ModLog.Info(MemoryLogFormatter.FormatDormancy(picked.EventId, dormancyReason));
                    }

                    // 7. 印一輪講述行
                    if (_config.Debug.LogTellerTurns)
                    {
                        ModLog.Info(TellerLogFormatter.FormatTellerTurn(t, picked.EventId, choice, outcome, inactiveCount));
                    }
                }
            }
        }

        internal void DailyTick(double day)
        {
            using (DevMetrics.Measure("daily"))
            {
                var secretIds = _secretWatch.ToList();
                int leakedCount = 0;
                int dormantCount = 0;

                foreach (var eventId in secretIds)
                {
                    var evt = _store.Load(eventId);
                    if (evt == null)
                    {
                        _secretWatch.Remove(eventId);
                        continue;
                    }

                    var outcome = _engine.TryLeak(evt, day);
                    if (outcome.Leaked)
                    {
                        _stamper?.RestampAfterLeak(evt);
                        _store.Upsert(evt);
                        _secretWatch.Remove(eventId);
                        AddActive(eventId);
                        if (evt.KnownBy != null)
                        {
                            AddKnowersToTellers(evt.KnownBy.Select(k => k.HeroId));
                        }
                        WorldEventStore.TriggerPublicEventOccurred(evt);
                        leakedCount++;
                    }
                    else
                    {
                        if ((day - evt.Day) > _config.Scheduling.SecretWatchDays)
                        {
                            evt.State.Dormant = true;
                            _store.Upsert(evt);
                            _secretWatch.Remove(eventId);
                            dormantCount++;
                        }
                    }
                }

                var activeIds = _activeRing.ToList();
                foreach (var eventId in activeIds)
                {
                    var evt = _store.Load(eventId);
                    if (evt == null)
                    {
                        RemoveActive(eventId);
                        continue;
                    }

                    var dormancyReason = _engine.DormancyReasonFor(evt, day);
                    if (dormancyReason != null)
                    {
                        evt.State.Dormant = true;
                        _store.Upsert(evt);
                        RemoveActive(eventId);
                        ModLog.Info(MemoryLogFormatter.FormatDormancy(evt.EventId, dormancyReason));
                    }
                }

                int kingdomScans = _heroLookup?.KingdomScanCountToday ?? 0;
                _heroLookup?.ResetDailyKingdomScan();

                if (_config.Debug.VerboseTickLog)
                {
                    ModLog.Info($"DailyTick at day {day:0.00}: secret watch size {_secretWatch.Count} (leaked: {leakedCount}, dormant: {dormantCount}), active ring size {_activeRing.Count}, kingdom scans today: {kingdomScans}");
                }
            }
        }
    }
}
