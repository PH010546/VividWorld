using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Library;
using TaleWorlds.Localization;
using VividWorld.Api;
using VividWorld.Core.Config;
using VividWorld.Core.Diagnostics;
using VividWorld.Core.Dialogue;
using VividWorld.Core.Events;
using VividWorld.Core.Grudges;
using VividWorld.Core.Persistence;
using VividWorld.Core.Presentation;
using VividWorld.Core.Rumors;
using VividWorld.Core.Util;
using VividWorld.Debug;
using VividWorld.Dialogue;

namespace VividWorld.Campaign
{
    internal sealed class RumorCampaignBehavior : CampaignBehaviorBase
    {
        private readonly VividWorldConfig _config;
        private string _campaignId = string.Empty;

        /// <summary>SNAP1：快照管理的熱鍵在 SubModule 的 tick 上，拿不到 behavior 的私有欄位。</summary>
        internal string CampaignId => _campaignId;
        internal EventShardStore? EventShardStore => _store;
        internal KnownByIndex? KnownByIndex => _knownBy;
        internal RumorEngine? RumorEngine => _engine;
        internal WorldEventStore? WorldEventStore => _eventStore;
        internal HeroLookup? HeroLookup => _heroLookup;
        internal PlayerHeardLogStore? PlayerHeardLog { get; private set; }
        private int _tickCursor;
        private string _tickCursorHeroId = string.Empty;
        private string _pendingIngestJson = string.Empty;
        private string _snapshotToken = string.Empty;
        private string _pendingSnapshotToken = string.Empty;
        private readonly SnapshotSessionState _sessionState = new();

        private EventShardStore? _store;
        private RumorIndex? _index;
        private KnownByIndex? _knownBy;
        private HeroLookup? _heroLookup;
        private GameWorldChannel? _channel;
        private GameTraitLookup? _traitLookup;
        private RumorEngine? _engine;
        private WorldEventStore? _eventStore;
        private RumorPropagationScheduler? _scheduler;
        private int _hoursSinceLastFlush;
        private int _rollbackCount;
        private double _rollbackMaxDay;
        private double _launchDay;
        private readonly RumorDialogBehavior _dialogs;
        private readonly FakeEventProducerBehavior _producer;
        private readonly RealEventSourceBehavior? _realEvents;
        private readonly SituationScanBehavior? _situationScan;

        internal RumorCampaignBehavior(
            VividWorldConfig config,
            RumorDialogBehavior dialogs,
            FakeEventProducerBehavior producer,
            RealEventSourceBehavior? realEvents = null,
            SituationScanBehavior? situationScan = null)
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));
            _dialogs = dialogs ?? throw new ArgumentNullException(nameof(dialogs));
            _producer = producer ?? throw new ArgumentNullException(nameof(producer));
            _realEvents = realEvents;
            _situationScan = situationScan;
        }

        public override void RegisterEvents()
        {
            CampaignEvents.OnSessionLaunchedEvent.AddNonSerializedListener(this, OnSessionLaunched);
            CampaignEvents.OnGameLoadedEvent.AddNonSerializedListener(this, OnGameLoaded);
            CampaignEvents.DailyTickEvent.AddNonSerializedListener(this, OnDailyTick);
            CampaignEvents.HourlyTickEvent.AddNonSerializedListener(this, OnHourlyTick);
            CampaignEvents.OnSaveOverEvent.AddNonSerializedListener(this, OnSaveOver);
        }

        public override void SyncData(IDataStore dataStore)
        {
            dataStore.SyncData("VividWorld_CampaignId", ref _campaignId);

            if (dataStore.IsSaving)
            {
                _snapshotToken = Guid.NewGuid().ToString("N");
                _pendingSnapshotToken = _snapshotToken;
                _eventStore?.SetSaving(true);
                _pendingIngestJson = _eventStore?.SerializePendingIngest(_config.Persistence.MaxPendingIngestChars) ?? string.Empty;
                if (_scheduler != null)
                {
                    _tickCursor = _scheduler.TickCursor;
                    // §7.3.1：真正要還原的是「下一個輪到誰」。位置留著當舊存檔的退路
                    _tickCursorHeroId = _scheduler.TickCursorHeroId ?? string.Empty;
                }
            }

            dataStore.SyncData("VividWorld_SnapshotToken", ref _snapshotToken);
            dataStore.SyncData("VividWorld_TickCursor", ref _tickCursor);
            // 舊存檔沒有這個鍵：SyncData 回傳 false 且不動欄位（帳本 D-61）⇒ 維持空字串，讀檔時退回位置值
            dataStore.SyncData("VividWorld_TickCursorHeroId", ref _tickCursorHeroId);
            dataStore.SyncData("VividWorld_PendingIngest", ref _pendingIngestJson);

            if (dataStore.IsSaving)
            {
                _eventStore?.SetSaving(false);
            }
            else
            {
                RestoreTickCursor();
                if (string.IsNullOrEmpty(_campaignId))
                {
                    _campaignId = "legacy";
                }
            }
        }

        /// <summary>
        /// §7.3.1：讀檔後把游標移回存檔裡那一位講述者身上。輪在讀檔時是從索引重建的，
        /// 成員與順序都跟遊戲中不同（實機 309 → 343，帳本 L-35）⇒ 只還原位置會換到別人身上。
        /// 找不到那個人、或舊存檔沒有這個鍵時，退回舊的位置值。
        /// 兩個呼叫點：`SyncData` 的載入分支（那時排程器通常還沒建好，什麼都不做）與
        /// `OnSessionLaunched` 的 `RebuildFrom` 之後（真正生效的那一次）。
        /// </summary>
        private void RestoreTickCursor()
        {
            if (_scheduler == null) return;

            int ringSize = _scheduler.TellerRingCount;

            if (!string.IsNullOrEmpty(_tickCursorHeroId) && _scheduler.RestoreTickCursorTo(_tickCursorHeroId))
            {
                ModLog.Info(string.Format(CultureInfo.InvariantCulture,
                    "Teller ring: cursor restored to {0} - position {1} of {2} (saved position was {3}).",
                    _tickCursorHeroId, _scheduler.TickCursor, ringSize, _tickCursor));
                return;
            }

            _scheduler.TickCursor = _tickCursor;

            // 新戰役（沒存過東西）不值得印一行
            if (string.IsNullOrEmpty(_tickCursorHeroId) && _tickCursor == 0 && ringSize == 0) return;

            ModLog.Info(string.Format(CultureInfo.InvariantCulture,
                "Teller ring: cursor fell back to saved position {0} of {1} - {2}.",
                _scheduler.TickCursor,
                ringSize,
                string.IsNullOrEmpty(_tickCursorHeroId)
                    ? "the save has no teller id (saved before MF1b-fix1)"
                    : _tickCursorHeroId + " is no longer in the ring"));
        }

        private void OnSessionLaunched(CampaignGameStarter starter)
        {
            try
            {
                ConfigStore.ApplyRuntimeScales(_config);

                var mainHero = Hero.MainHero;

                if (string.IsNullOrEmpty(_campaignId))
                {
                    string playerFirstName = mainHero?.FirstName?.ToString() ?? string.Empty;
                    _campaignId = CampaignIdentity.MintCampaignId(playerFirstName);
                    ModLog.Info($"Minted new campaign ID: {_campaignId}");
                }

                string eventsDir = VividWorldPaths.EventsDirectory(_campaignId);
                var writer = new SystemFileWriter();
                _store = new EventShardStore(eventsDir, _config.Persistence.ShardDays, writer, _config.Persistence.ShardCacheIdleFlushes);
                _index = _store.LoadIndex();
                ModLog.Info($"Event index: {_store.LastLoadNote}, {_index.Count} entries");

                _launchDay = CampaignTime.Now.ToDays;
                var (rollbackCount, rollbackMaxDay) = StoreTimeline.EventsAfter(_index, _launchDay);
                _rollbackCount = rollbackCount;
                _rollbackMaxDay = rollbackMaxDay;

                if (_rollbackCount > 0)
                {
                    string verdict = SnapshotLogFormatter.FormatRollbackVerdict(_sessionState.LastRestore);
                    ModLog.Warn(string.Format(CultureInfo.InvariantCulture,
                        "Event store is ahead of the save: {0} events dated after day {1:F1} (max {2:F1}).\n" +
                        "      Snapshot revert {3}.\n" +
                        "      Those events are still in the store, but every reader hides them until the date catches up (spec 2.2.1); nothing was deleted.",
                        _rollbackCount, _launchDay, _rollbackMaxDay, verdict));
                }

                _knownBy = new KnownByIndex();
                _knownBy.Rebuild(_index);

                var grudgeIndex = GrudgeIndex.RebuildFrom(_index, id => _store.Load(id, _index), _launchDay,
                    out int grudgeEventsRead, out int grudgeSkipped, out int grudgeHiddenFuture);
                ModLog.Info(string.Format(CultureInfo.InvariantCulture,
                    "Grudge ledger: rebuilt from {0} event(s) of {1} indexed, {2} skipped, {6} hidden (dated after day {7:F1}) - {3} pair(s) (personal {4}, clan {5}).",
                    grudgeEventsRead, _index.Entries.Count, grudgeSkipped, grudgeIndex.Pairs.Count,
                    grudgeIndex.PairCount(GrudgeScope.Personal), grudgeIndex.PairCount(GrudgeScope.Clan),
                    grudgeHiddenFuture, _launchDay));

                _heroLookup = new HeroLookup();
                _heroLookup.RebuildAll();

                string playerHeroId = mainHero?.StringId ?? "player";
                _channel = new GameWorldChannel(_config, _heroLookup, playerHeroId);
                _traitLookup = new GameTraitLookup(_heroLookup);

                long campaignSeed = (long)RumorSeed.Of(0, _campaignId);
                var rng = new SplitMix64Rng();
                var retentionPolicy = FactRetentionPolicies.Create(_config, rng, campaignSeed);
                var embellishmentPolicy = NullEmbellishmentPolicy.Instance;
                _engine = new RumorEngine(_config, retentionPolicy, embellishmentPolicy, _channel, _traitLookup, rng, campaignSeed, playerHeroId);

                var stamper = new MemoryStamper(_config, _heroLookup, playerHeroId);

                _eventStore = new WorldEventStore(_config, _store, _index, _knownBy, _channel, _traitLookup, playerHeroId, campaignSeed, stamper, grudgeIndex);
                stamper.SetStore(_eventStore);
                _scheduler = new RumorPropagationScheduler(_config, _eventStore, _engine, _knownBy, _heroLookup, _traitLookup, stamper);
                stamper.SetEngineAndScheduler(_engine, _scheduler);
                _scheduler.RebuildFrom(_index, _launchDay);
                ModLog.Info(TellerLogFormatter.FormatRebuilt(_scheduler.TellerRingCount, _scheduler.RebuildSkippedForgotten));
                // 游標要在講述者輪重建**之後**才還原：TellerRing.Cursor 在 Count == 0 時一律歸零，
                // 而且要對回「同一個人」而不是同一個位置（§7.3.1）
                RestoreTickCursor();

                _eventStore.SetScheduler(_scheduler);
                _eventStore.MarkIndexLoaded();

                string heardPath = VividWorldPaths.PlayerHeardFile(_campaignId);
                PlayerHeardLog = new PlayerHeardLogStore(heardPath, writer);
                var heardLoad = PlayerHeardLog.Load();
                if (heardLoad.Status == PlayerHeardLoadStatus.FileUnreadable && !string.IsNullOrEmpty(heardLoad.ExceptionMessage))
                {
                    ModLog.Warn($"Player heard-log unreadable: {heardLoad.ExceptionMessage} - " + (heardLoad.SetAsidePath != null
                        ? $"moved it to {System.IO.Path.GetFileName(heardLoad.SetAsidePath)} and started empty"
                        : "could not move it aside, the next flush will overwrite it"));
                }
                var heardBackfill = PlayerHeardLog.Backfill(
                    _index,
                    playerHeroId,
                    id => _store.Load(id, _index),
                    (evt, entry) => PlayerKnownFacts.Of(evt, entry, _engine),
                    _launchDay);
                ModLog.Info(PlayerHeardLogFormatter.FormatLoad(heardLoad.Count, heardLoad.How, heardBackfill));

                string markerFile = VividWorldPaths.CampaignMarkerFile(_campaignId);
                if (!writer.Exists(markerFile))
                {
                    string playerName = mainHero?.Name?.ToString() ?? "Unknown";
                    string markerText = string.Format(CultureInfo.InvariantCulture,
                        "Campaign ID: {0}\nCreated: {1:yyyy-MM-dd HH:mm:ss}\nPlayer: {2}\nModule Version: 1.0.0\n",
                        _campaignId,
                        DateTime.Now,
                        playerName);
                    writer.WriteAllText(markerFile, markerText);
                }

                VividWorldEventApi.Store = _eventStore;

                // 3.1 修復：存檔當下的延遲提交，在 OnSessionLaunched（此時 _eventStore 已完全就緒）進行重投
                if (!string.IsNullOrEmpty(_pendingIngestJson))
                {
                    _eventStore.RedeliverPending(_pendingIngestJson);
                    _pendingIngestJson = string.Empty;
                }

                // §6.1 / §6.2: 開發者工具對話掛載
                if (_config.Debug.DebugDialogueEnabled)
                {
                    var devDialogs = new Debug.DevDialogueBehavior(
                        _config,
                        _eventStore,
                        _channel,
                        _scheduler,
                        _heroLookup,
                        _dialogs,
                        _rollbackCount,
                        _rollbackMaxDay,
                        _launchDay,
                        _realEvents,
                        _traitLookup,
                        _situationScan,
                        _sessionState,
                        _campaignId);
                    devDialogs.RegisterDialogues(starter);

                    // M6a 自答探針 1：在地化探針 (D-09)
                    RunLocalizationProbe();
                }

                // M6a-fix: 子行為依賴注入與對話註冊（行為註冊已在 SubModule.OnGameStart，見帳本 D-38）
                if (_engine != null && _store != null && _index != null && _knownBy != null && _traitLookup != null && _heroLookup != null)
                {
                    var offerSelector = new RumorOfferSelector(_config, _engine, playerHeroId);
                    _dialogs.Initialize(offerSelector, _store, _index, _knownBy, _traitLookup, _heroLookup, _campaignId, stamper, PlayerHeardLog);
                    _dialogs.RegisterDialogues(starter);
                }

                if (_eventStore != null && _heroLookup != null && _traitLookup != null)
                {
                    _producer.Initialize(_eventStore, _heroLookup, _traitLookup);
                    _situationScan?.Initialize(_eventStore, _heroLookup, _traitLookup);
                }

                if (_realEvents != null && _eventStore != null)
                {
                    _realEvents.Initialize(_eventStore, _traitLookup);
                }

                ModLog.Info($"VividWorld session launched for campaign {_campaignId}.");
            }
            catch (Exception ex)
            {
                ModLog.Error("Error during OnSessionLaunched", ex);
            }
            finally
            {
                ModLog.Flush();
            }
        }

        private static void RunLocalizationProbe()
        {
            try
            {
                string lang = MBTextManager.ActiveTextLanguage ?? string.Empty;
                string id = "VividWorld_Dev_" + "Contacts";
                string probe = "{=" + id + "}" + "VW_PROBE_FALLBACK";
                string result = new TextObject(probe).ToString();

                string verdict;
                if (string.Equals(lang, "English", StringComparison.OrdinalIgnoreCase))
                {
                    verdict = "INCONCLUSIVE (English returns fallback by design, ledger D-07)";
                }
                else if (!string.Equals(result, "VW_PROBE_FALLBACK", StringComparison.Ordinal))
                {
                    verdict = "D-09 HOLDS";
                }
                else
                {
                    verdict = "D-09 FAILS - see spec 9.5 fallback plan";
                }

                ModLog.Info($"localization probe: language={lang}  result=\"{result}\"  => {verdict}");
            }
            catch (Exception ex)
            {
                ModLog.Error("Error during localization probe", ex);
            }
        }

        private void OnGameLoaded(CampaignGameStarter starter)
        {
            try
            {
                if (string.IsNullOrEmpty(_snapshotToken))
                {
                    var res = new SnapshotResult
                    {
                        Outcome = SnapshotOutcome.NoToken,
                        Token = string.Empty,
                        Reason = SnapshotLogFormatter.GetRestoreSkippedReason(SnapshotOutcome.NoToken)
                    };
                    _sessionState.LastRestore = res;
                    ModLog.Info(SnapshotLogFormatter.FormatSkippedRestore(SnapshotOutcome.NoToken));
                }
                else
                {
                    string campaignId = string.IsNullOrEmpty(_campaignId) ? "legacy" : _campaignId;
                    string campaignRoot = VividWorldPaths.CampaignDirectory(campaignId);
                    SnapshotResult result;
                    using (DevMetrics.Measure("snapshot"))
                    {
                        result = RumorSnapshotStore.Restore(campaignRoot, _snapshotToken);
                    }
                    _sessionState.LastRestore = result;

                    if (result.Outcome == SnapshotOutcome.Restored)
                    {
                        ModLog.Info(SnapshotLogFormatter.FormatRestore(result));
                    }
                    else if (result.Outcome == SnapshotOutcome.Failed)
                    {
                        ModLog.Warn(SnapshotLogFormatter.FormatFailed("restore", result.Token, result.Reason));
                    }
                    else if (result.Outcome == SnapshotOutcome.SnapshotIncomplete)
                    {
                        // 找到照片卻拒絕還原，是真的有異常（上一次拍照沒跑完），不能只當一句 info
                        ModLog.Warn(SnapshotLogFormatter.FormatSkippedRestore(result.Reason));
                    }
                    else
                    {
                        // Restore 填的 Reason 帶著 token 與檔數，比從 Outcome 反推的通用句子準
                        ModLog.Info(string.IsNullOrEmpty(result.Reason)
                            ? SnapshotLogFormatter.FormatSkippedRestore(result.Outcome, result.Token)
                            : SnapshotLogFormatter.FormatSkippedRestore(result.Reason));
                    }
                }
                ModLog.Flush();

                _heroLookup?.RebuildAll();
                _traitLookup?.ClearCache();

                // 3.1 修復：OnGameLoaded 比 OnSessionLaunched 先觸發（S-14）。
                // _eventStore 此時為 null，嚴禁在此投遞或清空 _pendingIngestJson，否則存檔期延遲提交會永久遺失！
            }
            catch (Exception ex)
            {
                ModLog.Error("Error during OnGameLoaded", ex);
            }
        }

        private void OnDailyTick()
        {
            try
            {
                _heroLookup?.RebuildAll();
                _heroLookup?.RebuildKingdomIndex();
                _traitLookup?.ClearCache();

                double day = CampaignTime.Now.ToDays;
                _scheduler?.DailyTick(day);
            }
            catch (Exception ex)
            {
                ModLog.Error("Error during OnDailyTick", ex);
            }
            finally
            {
                ModLog.Flush();
            }
        }

        private void OnHourlyTick()
        {
            try
            {
                double day = CampaignTime.Now.ToDays;
                int hourOfDay = CampaignTime.Now.GetHourOfDay;
                _scheduler?.HourlyTick(day, hourOfDay);

                _hoursSinceLastFlush++;
                if (_hoursSinceLastFlush >= _config.Scheduling.FlushIntervalHours)
                {
                    _hoursSinceLastFlush = 0;
                    Flush();
                }
            }
            catch (Exception ex)
            {
                ModLog.Error("Error during OnHourlyTick", ex);
            }
        }

        private void OnSaveOver(bool isSuccessful, string saveName)
        {
            string token = _pendingSnapshotToken;
            _pendingSnapshotToken = string.Empty;

            if (_config.Persistence.PurgeForgottenEvents)
            {
                try
                {
                    using (DevMetrics.Measure("purge"))
                    {
                        bool heardFlushed = PlayerHeardLog?.Flush() ?? true;
                        if (PlayerHeardLog != null && PlayerHeardLog.IsDirty)
                        {
                            ModLog.Warn(EventPurgeLogFormatter.FormatSkippedHeardLogDirty(saveName));
                        }
                        else if (_store != null && _index != null)
                        {
                            double today = CampaignTime.Now.ToDays;
                            string playerHeroId = _eventStore?.PlayerHeroId ?? Hero.MainHero?.StringId ?? "player";
                            double? situationMinAgeDays = null;
                            var catalog = SituationCatalogStore.Catalog;
                            if (!string.IsNullOrEmpty(SituationCatalogStore.ActiveFilePath) && catalog != null && catalog.Situations.Count > 0)
                            {
                                situationMinAgeDays = catalog.MaxCooldownDays();
                            }

                            var plan = EventPurgePlanner.Plan(
                                _index,
                                today,
                                playerHeroId,
                                _config.Memory,
                                situationMinAgeDays,
                                id => PlayerHeardLog?.Contains(id) ?? false);

                            if (plan.PlayerNotLoggedEventIds.Count > 0)
                            {
                                ModLog.Warn(EventPurgeLogFormatter.FormatPlayerNotLoggedWarn(
                                    plan.PlayerNotLoggedEventIds.Count,
                                    plan.PlayerNotLoggedEventIds));
                            }

                            var sw = System.Diagnostics.Stopwatch.StartNew();
                            var (removedCount, touchedShards) = _store.Remove(plan.PurgeEventIds);
                            sw.Stop();

                            foreach (var id in plan.PurgeEventIds)
                            {
                                _knownBy?.Remove(id);
                                _scheduler?.ForgetEvent(id);
                            }

                            ModLog.Info(EventPurgeLogFormatter.FormatSummary(
                                saveName, today, plan, situationMinAgeDays, touchedShards, sw.ElapsedMilliseconds));

                            int detailCount = 0;
                            foreach (var purged in plan.PurgedEvents)
                            {
                                if (detailCount < 30)
                                {
                                    ModLog.Info(EventPurgeLogFormatter.FormatDetail(
                                        purged.EventId, purged.Type, purged.Day, purged.Why));
                                    detailCount++;
                                }
                                else
                                {
                                    int more = plan.PurgedEvents.Count - 30;
                                    ModLog.Info(string.Format(CultureInfo.InvariantCulture, "  ... and {0} more", more));
                                    break;
                                }
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    ModLog.Error("Error during OnSaveOver purge", ex);
                }
            }
            else
            {
                ModLog.Info(EventPurgeLogFormatter.FormatSkippedDisabled(saveName));
            }

            try
            {
                Flush();
            }
            catch (Exception ex)
            {
                ModLog.Error("Error during OnSaveOver flush", ex);
            }

            if (!isSuccessful)
            {
                var res = new SnapshotResult
                {
                    Outcome = SnapshotOutcome.SaveFailed,
                    Token = token,
                    Reason = SnapshotLogFormatter.GetTakeSkippedReason(SnapshotOutcome.SaveFailed)
                };
                _sessionState.LastTake = res;
                ModLog.Info(SnapshotLogFormatter.FormatSkippedTake(SnapshotOutcome.SaveFailed));
                return;
            }

            if (string.IsNullOrEmpty(token))
            {
                var res = new SnapshotResult
                {
                    Outcome = SnapshotOutcome.NoToken,
                    Token = string.Empty,
                    Reason = SnapshotLogFormatter.GetTakeSkippedReason(SnapshotOutcome.NoToken)
                };
                _sessionState.LastTake = res;
                ModLog.Info(SnapshotLogFormatter.FormatSkippedTake(SnapshotOutcome.NoToken));
                return;
            }

            try
            {
                // 與 OnGameLoaded／dev 工具用同一個算法：空的 id 一律當 legacy。
                // 兩種算法會讓照片寫進 campaign_、還原卻去讀 campaign_legacy，而兩邊各自都印「成功」
                string campaignRoot = VividWorldPaths.CampaignDirectory(
                    string.IsNullOrEmpty(_campaignId) ? "legacy" : _campaignId);
                SnapshotResult result;
                using (DevMetrics.Measure("snapshot"))
                {
                    result = RumorSnapshotStore.Take(
                        campaignRoot,
                        token,
                        saveName,
                        _config.Persistence.MaxSnapshots,
                        _config.Persistence.HardlinkSnapshots);
                }
                _sessionState.LastTake = result;

                if (result.Outcome == SnapshotOutcome.Taken)
                {
                    ModLog.Info(SnapshotLogFormatter.FormatTake(result, saveName, _config.Persistence.MaxSnapshots));
                    if (!string.IsNullOrEmpty(result.HardlinkNote))
                    {
                        ModLog.Info(result.HardlinkNote!);
                    }
                    if (result.Pruned != null)
                    {
                        foreach (var p in result.Pruned)
                        {
                            ModLog.Info(SnapshotLogFormatter.FormatPrune(p, _config.Persistence.MaxSnapshots));
                        }
                    }
                }
                else if (result.Outcome == SnapshotOutcome.Failed)
                {
                    ModLog.Warn(SnapshotLogFormatter.FormatFailed("copy", result.Token, result.Reason));
                }
                else
                {
                    ModLog.Info(SnapshotLogFormatter.FormatSkippedTake(result.Outcome));
                }
            }
            catch (Exception ex)
            {
                ModLog.Error("Error during OnSaveOver snapshot", ex);
            }
        }

        internal void Flush()
        {
            try
            {
                _eventStore?.Flush();
                if (_store != null && _store.LastReleased.Count > 0)
                {
                    ModLog.Info(ShardCacheLogFormatter.FormatReleased(
                        _store.LastReleased.Count,
                        _config.Persistence.ShardCacheIdleFlushes,
                        _store.LastReleased,
                        _store.CachedShardCount,
                        _store.CachedEventCount));
                }

                if (PlayerHeardLog != null)
                {
                    bool ok = PlayerHeardLog.Flush();
                    if (!ok)
                    {
                        ModLog.Warn(PlayerHeardLogFormatter.FormatWriteFailed());
                    }
                }
            }
            catch (Exception ex)
            {
                ModLog.Error("Error during Flush", ex);
            }
            finally
            {
                ModLog.Flush();
            }
        }
    }
}
