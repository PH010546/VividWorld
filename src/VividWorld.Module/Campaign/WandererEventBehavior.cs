using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Helpers;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.ComponentInterfaces;
using TaleWorlds.CampaignSystem.Encounters;
using TaleWorlds.CampaignSystem.Extensions;
using TaleWorlds.CampaignSystem.MapEvents;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Roster;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Localization;
using VividWorld.Core.Catalog;
using VividWorld.Core.Config;
using VividWorld.Core.Events;
using VividWorld.Core.Grudges;
using VividWorld.Core.Persistence;
using VividWorld.Core.Presentation;
using VividWorld.Core.Rumors;
using VividWorld.Core.Situations;
using VividWorld.Core.Util;
using VividWorld.Presentation;
using VividWorld.UI;

namespace VividWorld.Campaign
{
    /// <summary>
    /// 流浪者事件行為：戰鬥結束與每日在部隊中觸發流浪者情境。
    /// 包含合格候選人挑選、部隊全體知情者綁定、野外就近聚落解析、裁決視窗與袖手旁觀流程。
    /// </summary>
    internal sealed partial class WandererEventBehavior : CampaignBehaviorBase
    {
        private const string LastBattleDayKey = "VividWorld_WandererBattleLastDay";
        private const string LastDailyDayKey = "VividWorld_WandererDailyLastDay";
        private const string LastAnyDayKey = "VividWorld_WandererAnyLastDay";

        internal static WandererEventBehavior? Current { get; private set; }

        private readonly VividWorldConfig _config;
        private WorldEventStore? _eventStore;
        private HeroLookup? _heroLookup;
        private IHeroTraitLookup? _traits;
        private Func<PlayerHeardLogStore?>? _heardLog;
        private Func<RumorEngine?>? _engine;
        private bool _ready;

        private int _generation;
        private bool _windowOpen;

        private string _lastBattleDayRaw = string.Empty;
        private string _lastDailyDayRaw = string.Empty;
        private string _lastAnyDayRaw = string.Empty;

        private int _battleSequence;
        private PendingBattleWandererEvent? _pendingBattleEvent;
        private readonly Dictionary<string, bool> _battlePreWounded = new(StringComparer.Ordinal);
        private HashSet<string>? _lastFoughtHeroes;
        private HashSet<string>? _lastWoundedHeroes;
        private bool? _lastBattleWon;
        private MapEvent? _lastHandledBattle;
        private WandererRoleRecordStore? _roleRecords;
        private bool _roleRecordsDirty;

        internal WandererRollResult? LastRoll { get; private set; }
        internal double? LastRollDay { get; private set; }

        internal sealed class PendingBattleWandererEvent
        {
            public WandererPick Pick = null!;
            public SituationEvaluation Evaluation = null!;
            public double Day;
            public string? NearSettlementId;
            public Dictionary<string, string> CustomBindings = new(StringComparer.Ordinal);
            public List<string> InitialKnowerIds = new();
            public bool WonBattle;
        }

        internal WandererEventBehavior(VividWorldConfig config)
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));
            Current = this;
        }

        internal void Initialize(
            WorldEventStore eventStore,
            HeroLookup heroLookup,
            IHeroTraitLookup traits,
            Func<PlayerHeardLogStore?> heardLog,
            Func<RumorEngine?> engine,
            WandererRoleRecordStore? roleRecords = null)
        {
            _eventStore = eventStore ?? throw new ArgumentNullException(nameof(eventStore));
            _heroLookup = heroLookup ?? throw new ArgumentNullException(nameof(heroLookup));
            _traits = traits ?? throw new ArgumentNullException(nameof(traits));
            _heardLog = heardLog;
            _engine = engine;
            _roleRecords = roleRecords;
            _roleRecordsDirty = false;

            _generation++;
            _windowOpen = false;
            _pendingBattleEvent = null;
            _battlePreWounded.Clear();
            _ready = true;
            Current = this;

            ModLog.Info($"WandererEvents: ready (battleCooldown={_config.Situations?.WandererEvents?.BattleCooldownDays}d, dailyCooldown={_config.Situations?.WandererEvents?.DailyCooldownDays}d).");
        }

        public override void RegisterEvents()
        {
            CampaignEvents.MapEventStarted.AddNonSerializedListener(this, OnMapEventStarted);
            CampaignEvents.OnPlayerBattleEndEvent.AddNonSerializedListener(this, OnPlayerBattleEnd);
            CampaignEvents.MapEventEnded.AddNonSerializedListener(this, OnMapEventEnded);
            CampaignEvents.HourlyTickEvent.AddNonSerializedListener(this, OnHourlyTick);
            CampaignEvents.DailyTickEvent.AddNonSerializedListener(this, OnDailyTick);
            CampaignEvents.ConversationEnded.AddNonSerializedListener(this, OnComplaintConversationEnded);
            ModLog.Info("WandererEventBehavior.RegisterEvents: subscribed to battle and tick events.");
        }

        public override void SyncData(IDataStore dataStore)
        {
            dataStore.SyncData(LastBattleDayKey, ref _lastBattleDayRaw);
            dataStore.SyncData(LastDailyDayKey, ref _lastDailyDayRaw);
            dataStore.SyncData(LastAnyDayKey, ref _lastAnyDayRaw);
        }

        internal double? LastBattleDay
        {
            get
            {
                if (string.IsNullOrEmpty(_lastBattleDayRaw)) return null;
                return double.TryParse(_lastBattleDayRaw, NumberStyles.Float, CultureInfo.InvariantCulture, out double d) ? d : null;
            }
        }

        internal double? LastDailyDay
        {
            get
            {
                if (string.IsNullOrEmpty(_lastDailyDayRaw)) return null;
                return double.TryParse(_lastDailyDayRaw, NumberStyles.Float, CultureInfo.InvariantCulture, out double d) ? d : null;
            }
        }

        internal double? LastAnyDay
        {
            get
            {
                if (string.IsNullOrEmpty(_lastAnyDayRaw)) return null;
                return double.TryParse(_lastAnyDayRaw, NumberStyles.Float, CultureInfo.InvariantCulture, out double d) ? d : null;
            }
        }

        private void OnMapEventStarted(MapEvent mapEvent, PartyBase attackerParty, PartyBase defenderParty)
        {
            if (!_ready || mapEvent == null || !mapEvent.IsPlayerMapEvent) return;
            RecordPreBattleWounded();
        }

        private void RecordPreBattleWounded()
        {
            _battlePreWounded.Clear();
            var mainParty = MobileParty.MainParty;
            if (mainParty?.MemberRoster == null) return;

            foreach (var elem in mainParty.MemberRoster.GetTroopRoster())
            {
                if (elem.Character?.HeroObject != null)
                {
                    var h = elem.Character.HeroObject;
                    _battlePreWounded[h.StringId] = h.IsWounded;
                }
            }
        }

        private void OnPlayerBattleEnd(MapEvent mapEvent)
        {
            ModLog.Info($"WandererEvents: OnPlayerBattleEndEvent fired (game state: {CurrentGameStateName()}).");
            HandleBattleEnd(mapEvent);
        }

        private void OnMapEventEnded(MapEvent mapEvent)
        {
            if (mapEvent != null && mapEvent.IsPlayerMapEvent)
            {
                ModLog.Info($"WandererEvents: MapEventEnded fired (game state: {CurrentGameStateName()}).");
                HandleBattleEnd(mapEvent);
            }
        }

        private static string CurrentGameStateName()
        {
            try
            {
                return Game.Current?.GameStateManager?.ActiveState?.GetType().Name ?? "none";
            }
            catch (Exception ex)
            {
                return "unavailable: " + ex.Message;
            }
        }

        private void HandleBattleEnd(MapEvent mapEvent)
        {
            if (!_ready || mapEvent == null || !mapEvent.IsPlayerMapEvent) return;

            // Both OnPlayerBattleEndEvent and MapEventEnded can announce the same battle; only the first one counts.
            if (ReferenceEquals(mapEvent, _lastHandledBattle))
            {
                ModLog.Info("WandererEvents: battle end signal for a battle already handled - skipped.");
                return;
            }
            _lastHandledBattle = mapEvent;

            if (_pendingBattleEvent != null)
            {
                ModLog.Info($"WandererEvents: battle trigger skipped - event {_pendingBattleEvent.Pick.Template.Id} from an earlier battle is still waiting.");
                return;
            }

            _battleSequence++;
            var cfg = _config.Situations?.WandererEvents;
            if (cfg == null || !cfg.Enabled)
            {
                ModLog.Info("WandererEvents: battle trigger skipped (disabled by config).");
                return;
            }

            var mainParty = MobileParty.MainParty;
            var playerHero = Hero.MainHero;
            if (mainParty == null || playerHero == null) return;

            var foughtHeroes = new HashSet<string>(StringComparer.Ordinal);
            var woundedHeroes = new HashSet<string>(StringComparer.Ordinal);

            bool battleWon = false;
            try
            {
                battleWon = mapEvent.WinningSide == mapEvent.PlayerSide;
            }
            catch
            {
                battleWon = false;
            }

            MapEventParty? playerMapEventParty = null;
            try
            {
                playerMapEventParty = mapEvent.GetMapEventSide(mapEvent.PlayerSide)?.Parties
                    ?.FirstOrDefault(p => p.Party == mainParty.Party);
            }
            catch (Exception ex)
            {
                ModLog.Warn($"WandererEvents: failed to get player map event party: {ex.Message}");
            }

            // 上場的近似：戰鬥開始時在玩家隊伍裡、當時沒受傷。結束時 Troops 的那一筆只拿來印，不決定誰上場（規格 4.2）。
            if (_battlePreWounded.Count > 0)
            {
                foreach (var kv in _battlePreWounded)
                {
                    if (!kv.Value) foughtHeroes.Add(kv.Key);
                }
            }
            else
            {
                ModLog.Warn("WandererEvents: no pre-battle record for this battle (MapEventStarted did not run); using the roster as it is now.");
                if (mainParty.MemberRoster != null)
                {
                    foreach (var elem in mainParty.MemberRoster.GetTroopRoster())
                    {
                        var h = elem.Character?.HeroObject;
                        if (h != null && !h.IsPrisoner) foughtHeroes.Add(h.StringId);
                    }
                }
            }

            // 這一仗受傷：上場、戰鬥開始時沒受傷、結束時受傷（規格 4.2）。
            foreach (var hId in foughtHeroes)
            {
                var h = _heroLookup?.Get(hId) ?? Hero.Find(hId);
                if (h != null && h.IsWounded)
                {
                    woundedHeroes.Add(hId);
                }
            }

            LogBattleDetails(mapEvent, playerMapEventParty, foughtHeroes, woundedHeroes);
            foreach (var kv in _battlePreWounded)
            {
                var h = _heroLookup?.Get(kv.Key) ?? Hero.Find(kv.Key);
                string name = h?.Name?.ToString() ?? kv.Key;
                bool fought = foughtHeroes.Contains(kv.Key);
                ModLog.Info($"  battle member {name}: wounded at start={kv.Value}, fought={fought}, wounded now={woundedHeroes.Contains(kv.Key)}");
            }

            _lastFoughtHeroes = foughtHeroes;
            _lastWoundedHeroes = woundedHeroes;
            _lastBattleWon = battleWon;

            var candidates = FilterCandidates(mainParty, playerHero);
            if (candidates.Count < 2)
            {
                ModLog.Info($"WandererEvents: battle trigger - only {candidates.Count} eligible wanderers (need at least 2).");
                return;
            }

            double day = CampaignTime.Now.ToDays;
            var context = CreateContext(mainParty, playerHero, foughtHeroes, woundedHeroes, battleWon, day);

            var input = new WandererRollInput
            {
                Trigger = WandererTrigger.Battle,
                Today = day,
                LastBattleDay = LastBattleDay,
                LastDailyDay = LastDailyDay,
                LastAnyDay = LastAnyDay,
                AvailableWandererHeroIds = candidates.Select(h => h.StringId).ToList(),
                PlayerHeroId = playerHero.StringId,
                TownWindowShownToday = false,
                LastTownPresenceDay = PlayerPresenceBehavior.Current?.LastOccurrenceDay,
                PlayerCaptured = playerHero.IsPrisoner,
                InBattle = false,
                InSiege = playerHero.PartyBelongedTo?.SiegeEvent != null,
                CampaignSeed = (long)RumorSeed.Of(0, playerHero.StringId),
                BattleSequence = _battleSequence
            };

            var battleTemplates = (SituationCatalogStore.Catalog?.Situations ?? new List<SituationTemplate>())
                .Where(t => string.Equals(t.Trigger, "wandererBattle", StringComparison.OrdinalIgnoreCase))
                .ToList();

            var evaluator = new ModuleEvaluator(this, candidates, day, context);
            var rng = new SplitMix64Rng();

            var roll = WandererEventRoller.Roll(input, _config, battleTemplates, evaluator, rng);
            LastRoll = roll;
            LastRollDay = day;

            ModLog.Info(WandererEventLogFormatter.FormatRoll(roll));
            foreach (var line in WandererEventLogFormatter.FormatCandidates(roll))
            {
                ModLog.Info(line);
            }

            if (roll.Outcome != WandererRollOutcome.Happened || roll.Pick == null)
            {
                return;
            }

            var pick = roll.Pick;
            string? nearSettlementId = FindNearestSettlementId(mainParty);
            var customBindings = BuildCustomBindings(pick.Template, pick.Assignment.NonDerivedRoleHeroIds, nearSettlementId, battleWon, day);
            var knowerIds = candidates.Select(c => c.StringId).ToList();

            var evaluation = (SituationEvaluation)pick.Assignment.Evaluation.State!;
            evaluation.SettlementId = nearSettlementId ?? "none";

            _pendingBattleEvent = new PendingBattleWandererEvent
            {
                Pick = pick,
                Evaluation = evaluation,
                Day = day,
                NearSettlementId = nearSettlementId,
                CustomBindings = customBindings,
                InitialKnowerIds = knowerIds,
                WonBattle = battleWon
            };

            ModLog.Info($"WandererEvents: pending battle event queued ({pick.Template.Id}), waiting for map safe state.");
        }

        private void OnHourlyTick()
        {
            if (!_ready) return;

            TryOpenRoleComplaint();

            if (_pendingBattleEvent == null) return;

            var cfg = _config.Situations?.WandererEvents;
            if (cfg == null || !cfg.Enabled)
            {
                ModLog.Info($"WandererEvents: pending battle event {_pendingBattleEvent.Pick.Template.Id} dropped (disabled by config).");
                _pendingBattleEvent = null;
                return;
            }

            if (_windowOpen) return;

            var mainParty = MobileParty.MainParty;
            var playerHero = Hero.MainHero;
            if (mainParty == null || playerHero == null) return;

            double day = CampaignTime.Now.ToDays;

            if (day - _pendingBattleEvent.Day > 1.0)
            {
                ModLog.Info($"WandererEvents: pending battle event {_pendingBattleEvent.Pick.Template.Id} expired after 24 hours.");
                _pendingBattleEvent = null;
                return;
            }

            if (mainParty.MapEvent != null || mainParty.SiegeEvent != null || PlayerEncounter.Current != null
                || mainParty.IsCurrentlyAtSea || playerHero.IsPrisoner || mainParty.CurrentSettlement != null)
            {
                return;
            }

            var pending = _pendingBattleEvent;
            _pendingBattleEvent = null;

            ProcessWandererEvent(pending.Pick, pending.Evaluation, pending.Day, pending.NearSettlementId,
                pending.CustomBindings, pending.InitialKnowerIds, isBattle: true);
        }

        private void OnDailyTick()
        {
            if (!_ready) return;

            // 換回職位的檢查在擲骰之前，不受「不擲」的條件影響，只受開關影響。
            CheckRoleRevertsToday();

            if (_windowOpen) return;

            var cfg = _config.Situations?.WandererEvents;
            if (cfg == null || !cfg.Enabled)
            {
                ModLog.Info("WandererEvents: daily roll skipped (disabled by config).");
                return;
            }

            var mainParty = MobileParty.MainParty;
            var playerHero = Hero.MainHero;
            if (mainParty == null || playerHero == null) return;

            var candidates = FilterCandidates(mainParty, playerHero);
            if (candidates.Count < 2)
            {
                ModLog.Info($"WandererEvents: daily trigger - only {candidates.Count} eligible wanderers (need at least 2).");
                return;
            }

            double day = CampaignTime.Now.ToDays;
            var context = CreateContext(mainParty, playerHero, new HashSet<string>(), new HashSet<string>(), false, day);

            var input = new WandererRollInput
            {
                Trigger = WandererTrigger.Daily,
                Today = day,
                LastBattleDay = LastBattleDay,
                LastDailyDay = LastDailyDay,
                LastAnyDay = LastAnyDay,
                AvailableWandererHeroIds = candidates.Select(h => h.StringId).ToList(),
                PlayerHeroId = playerHero.StringId,
                TownWindowShownToday = false,
                LastTownPresenceDay = PlayerPresenceBehavior.Current?.LastOccurrenceDay,
                PlayerCaptured = playerHero.IsPrisoner,
                InBattle = mainParty.MapEvent != null,
                InSiege = mainParty.SiegeEvent != null,
                CampaignSeed = (long)RumorSeed.Of(0, playerHero.StringId),
                BattleSequence = 0
            };

            var dailyTemplates = (SituationCatalogStore.Catalog?.Situations ?? new List<SituationTemplate>())
                .Where(t => string.Equals(t.Trigger, "wandererDaily", StringComparison.OrdinalIgnoreCase))
                .ToList();

            var evaluator = new ModuleEvaluator(this, candidates, day, context);
            var rng = new SplitMix64Rng();

            var roll = WandererEventRoller.Roll(input, _config, dailyTemplates, evaluator, rng);
            LastRoll = roll;
            LastRollDay = day;

            ModLog.Info(WandererEventLogFormatter.FormatRoll(roll));
            foreach (var line in WandererEventLogFormatter.FormatCandidates(roll))
            {
                ModLog.Info(line);
            }

            if (roll.Outcome != WandererRollOutcome.Happened || roll.Pick == null)
            {
                return;
            }

            var pick = roll.Pick;
            string? nearSettlementId = FindNearestSettlementId(mainParty);
            var customBindings = BuildCustomBindings(pick.Template, pick.Assignment.NonDerivedRoleHeroIds, nearSettlementId, false, day);
            var knowerIds = candidates.Select(c => c.StringId).ToList();

            var evaluation = (SituationEvaluation)pick.Assignment.Evaluation.State!;
            evaluation.SettlementId = nearSettlementId ?? "none";

            ProcessWandererEvent(pick, evaluation, day, nearSettlementId, customBindings, knowerIds, isBattle: false);
        }

        private void ProcessWandererEvent(
            WandererPick pick,
            SituationEvaluation evaluation,
            double day,
            string? settlementId,
            Dictionary<string, string> customBindings,
            List<string> knowerIds,
            bool isBattle)
        {
            var template = pick.Template;
            string playerId = Hero.MainHero?.StringId ?? "player";

            if (pick.DecisionKind == WandererDecisionKind.Unseen)
            {
                ModLog.Info($"WandererEvents: {template.Id} happened unseen (no window).");
                const bool seen = false;
                var deciderHero = evaluation.AllHeroes.TryGetValue(template.Decider ?? string.Empty, out var dh) ? dh : null;
                var deciderTraits = deciderHero != null ? (_traits?.Of(deciderHero.StringId) ?? new TraitProfile()) : new TraitProfile();

                long seed = RumorSeed.Of((long)RumorSeed.Of(0, playerId), "wanderer", template.Id, RumorSeed.DayBucket(day));
                var rng = new SplitMix64Rng();

                var branchDecision = WandererEventRoller.RollNonJudgeBranch(
                    template,
                    evaluation.FactsByRole,
                    evaluation.BoundHeroIds,
                    evaluation.UnboundReasons,
                    deciderTraits,
                    _config.Situations.MinBranchWeight,
                    rng,
                    seed,
                    evaluation.Context,
                    day,
                    evaluation.History,
                    evaluation.NonDerivedHeroIds);

                if (branchDecision.SelectedBranch != null)
                {
                    var outcome = PrepareOutcome(template, branchDecision.SelectedBranch, evaluation, day, customBindings, false, playerId);
                    var runOpts = new PlayerPresenceRunOptions
                    {
                        PlayerHeroId = playerId,
                        ChosenBranch = outcome.Branch,
                        InitialKnowerHeroIds = knowerIds.Where(k => !string.Equals(k, playerId, StringComparison.Ordinal)).ToList(),
                        Unseen = true,
                        CustomBindings = outcome.Bindings,
                        PlaceNear = PlaceNearFor(settlementId)
                    };

                    SituationRunner.Execute(
                        template,
                        evaluation,
                        day,
                        "wanderer event",
                        _eventStore,
                        _traits,
                        _config,
                        _heroLookup,
                        presence: runOpts);
                    ApplyOutcomeEffects(template, outcome, evaluation, day, false, playerId, seen: seen);
                }

                // 私下的：從執行那天算冷卻（規格 4.2）
                RecordOccurrenceDays(isBattle, day);
                return;
            }

            if (string.Equals(template.Id, "wanderer_battle_rescue", StringComparison.OrdinalIgnoreCase))
            {
                ShowRescueWindow(pick, evaluation, day, settlementId, customBindings, knowerIds, isBattle);
                return;
            }

            ShowDecisionWindow(pick, evaluation, day, settlementId, customBindings, knowerIds, isBattle);
        }

        private void ShowRescueWindow(
            WandererPick pick,
            SituationEvaluation evaluation,
            double day,
            string? settlementId,
            Dictionary<string, string> customBindings,
            List<string> knowerIds,
            bool isBattle)
        {
            var template = pick.Template;
            string playerId = Hero.MainHero?.StringId ?? "player";

            var branch = template.Branches.FirstOrDefault();
            if (branch == null) return;

            var outcome = PrepareOutcome(template, branch, evaluation, day, customBindings, false, playerId);
            var runOpts = new PlayerPresenceRunOptions
            {
                PlayerHeroId = playerId,
                ChosenBranch = outcome.Branch,
                InitialKnowerHeroIds = knowerIds,
                Unseen = false,
                CustomBindings = outcome.Bindings,
                PlaceNear = PlaceNearFor(settlementId)
            };

            var run = SituationRunner.Execute(
                template,
                evaluation,
                day,
                "wanderer event",
                _eventStore,
                _traits,
                _config,
                _heroLookup,
                presence: runOpts);

            ApplyOutcomeEffects(template, outcome, evaluation, day, false, playerId, seen: true);
            PlayerRelationNotifier.NotifyAll(run.PlayerRelationNotices, _heroLookup);
            RecordChronicle(run.EventIds, day);

            string title = PlayerPresenceText.Title(settlementId);
            string scene = PlayerPresenceText.Scene(template.Id, PlayerPresenceRole.Bystander, evaluation.BoundHeroIds);
            string ack = PlayerPresenceText.Ack();

            var vm = new PlayerPresenceWindowVM(
                title,
                scene,
                PlayerPresenceText.Confirm(),
                template.Id,
                _ => { _windowOpen = false; });
            vm.AddOption("ack", ack, string.Empty, true);

            // 冷卻從視窗跳出那天算（規格 4.2）；開不成就不記
            if (!OpenWindowOrWarn(vm, template.Id, "rescue ack")) return;
            RecordOccurrenceDays(isBattle, day);
            ModLog.Info($"WandererEvents: window opened for {template.Id} (rescue ack).");
        }

        /// <summary>開視窗。開不成（回傳 false 或丟例外）印 WARN，呼叫端照「事情照樣算發生」處理。</summary>
        private bool OpenWindowOrWarn(PlayerPresenceWindowVM vm, string situationId, string what)
        {
            _windowOpen = true;
            try
            {
                if (PlayerPresenceWindowManager.Open(vm)) return true;
                ModLog.Warn($"WandererEvents: window for {situationId} ({what}) could not be opened - nothing shown.");
            }
            catch (Exception ex)
            {
                ModLog.Warn($"WandererEvents: window for {situationId} ({what}) threw while opening: {ex.Message}");
            }
            _windowOpen = false;
            return false;
        }

        private static bool PlaceNearFor(string? settlementId)
        {
            // 在野外才寫「附近」；在聚落裡 settlementId 就是那個聚落，不換變數（規格 4.5）
            return !string.IsNullOrEmpty(settlementId) && MobileParty.MainParty?.CurrentSettlement == null;
        }

        private void ShowDecisionWindow(
            WandererPick pick,
            SituationEvaluation evaluation,
            double day,
            string? settlementId,
            Dictionary<string, string> customBindings,
            List<string> knowerIds,
            bool isBattle)
        {
            var template = pick.Template;
            string playerId = Hero.MainHero?.StringId ?? "player";

            var judgeBranches = template.Branches.Where(b => b.Judge).ToList();
            var standAsideBranch = judgeBranches.FirstOrDefault(b => b.StandAside);
            var otherBranches = judgeBranches.Where(b => !b.StandAside).ToList();

            var orderedBranches = new List<SituationBranchDef>();
            if (standAsideBranch != null) orderedBranches.Add(standAsideBranch);
            orderedBranches.AddRange(otherBranches);

            if (orderedBranches.Count == 0)
            {
                ModLog.Warn($"WandererEvents: {template.Id} has no judge branches offered.");
                return;
            }

            int gen = _generation;
            string title = PlayerPresenceText.Title(settlementId);
            Dictionary<string, string>? directTextVars = null;
            bool isPartyRole = string.Equals(template.Id, "wanderer_party_role", StringComparison.OrdinalIgnoreCase);
            string? roleVal = null;
            bool hasBoundRole = isPartyRole && customBindings.TryGetValue("@role", out roleVal) && !string.IsNullOrEmpty(roleVal);
            if (hasBoundRole)
            {
                directTextVars = new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["ROLE"] = roleVal!
                };
            }

            string scene = PlayerPresenceText.Scene(template.Id, PlayerPresenceRole.Decider, evaluation.BoundHeroIds, directTextVars);

            var vm = new PlayerPresenceWindowVM(
                title,
                scene,
                PlayerPresenceText.Confirm(),
                template.Id,
                chosenId => OnDecisionMade(gen, chosenId, pick, evaluation, orderedBranches, day, settlementId, customBindings, knowerIds, isBattle));

            foreach (var b in orderedBranches)
            {
                string optText = PlayerPresenceText.Option(template.Id, b.Id, evaluation.BoundHeroIds, directTextVars);
                string costText = string.Empty;

                if (b.StandAside)
                {
                    costText = FormatStandAsideCost(template, b, evaluation.BoundHeroIds);
                }
                else
                {
                    var directions = PlayerChoiceDirections.Compose(template, b, evaluation.BoundHeroIds, playerId, false, false);
                    costText = PlayerPresenceText.Directions(directions);
                }

                vm.AddOption(b.Id, optText, costText, false);
            }

            // 冷卻從視窗跳出那天算（規格 4.2）；開不成就不記
            if (!OpenWindowOrWarn(vm, template.Id, "decision")) return;
            RecordOccurrenceDays(isBattle, day);
            ModLog.Info($"WandererEvents: decision window opened for {template.Id} with {orderedBranches.Count} options.");
            if (isPartyRole)
            {
                if (hasBoundRole)
                {
                    ModLog.Info($"WandererEvents: decision window used role-specific lines for party role '{roleVal}'.");
                }
                else
                {
                    ModLog.Info("WandererEvents: decision window used fallback lines (no party role bound).");
                }
            }
        }

        private void OnDecisionMade(
            int gen,
            string? chosenBranchId,
            WandererPick pick,
            SituationEvaluation evaluation,
            List<SituationBranchDef> branches,
            double day,
            string? settlementId,
            Dictionary<string, string> customBindings,
            List<string> knowerIds,
            bool isBattle)
        {
            try
            {
                if (gen != _generation || Hero.MainHero == null) return;
                _windowOpen = false;

                var chosen = branches.FirstOrDefault(b => string.Equals(b.Id, chosenBranchId, StringComparison.Ordinal));
                if (chosen == null)
                {
                    ModLog.Info($"WandererEvents: no branch selected for {pick.Template.Id}.");
                    return;
                }

                var template = pick.Template;
                string playerId = Hero.MainHero.StringId;

                if (chosen.StandAside)
                {
                    var deciderHero = evaluation.AllHeroes.TryGetValue(template.Decider ?? string.Empty, out var dh) ? dh : null;
                    var deciderTraits = deciderHero != null ? (_traits?.Of(deciderHero.StringId) ?? new TraitProfile()) : new TraitProfile();

                    const bool seen = true;
                    long seed = RumorSeed.Of((long)RumorSeed.Of(0, playerId), "wanderer_standaside", template.Id, RumorSeed.DayBucket(day));
                    var rng = new SplitMix64Rng();

                    var branchDecision = WandererEventRoller.RollNonJudgeBranch(
                        template,
                        evaluation.FactsByRole,
                        evaluation.BoundHeroIds,
                        evaluation.UnboundReasons,
                        deciderTraits,
                        _config.Situations.MinBranchWeight,
                        rng,
                        seed,
                        evaluation.Context,
                        day,
                        evaluation.History,
                        evaluation.NonDerivedHeroIds);

                    if (branchDecision.SelectedBranch == null)
                    {
                        ModLog.Warn($"WandererEvents: no non-judge branch rolled for {template.Id} after stand-aside; the stand-aside branch itself has no events.");
                    }
                    var actualBranch = branchDecision.SelectedBranch ?? chosen;

                    var extraGrudges = chosen.Grudges != null ? new List<SituationGrudgeDef>(chosen.Grudges) : new List<SituationGrudgeDef>();

                    var outcome = PrepareOutcome(template, actualBranch, evaluation, day, customBindings, false, playerId);
                    var runOpts = new PlayerPresenceRunOptions
                    {
                        PlayerHeroId = playerId,
                        ChosenBranch = outcome.Branch,
                        InitialKnowerHeroIds = knowerIds,
                        Unseen = false,
                        CustomBindings = outcome.Bindings,
                        PlaceNear = PlaceNearFor(settlementId),
                        ExtraGrudges = extraGrudges
                    };

                    var run = SituationRunner.Execute(
                        template,
                        evaluation,
                        day,
                        "wanderer event",
                        _eventStore,
                        _traits,
                        _config,
                        _heroLookup,
                        presence: runOpts);

                    ApplyOutcomeEffects(template, outcome, evaluation, day, false, playerId, seen: seen);
                    PlayerRelationNotifier.NotifyAll(run.PlayerRelationNotices, _heroLookup);
                    string? firstText = RecordChronicle(run.EventIds, day);

                    // 寫擲出那一列的結果句：先用視窗的結果句，沒有就用玩家聽到的第一句事實，再沒有才用寫死的收場句
                    Dictionary<string, string>? outcomeDirectVars = null;
                    if (string.Equals(template.Id, "wanderer_party_role", StringComparison.OrdinalIgnoreCase)
                        && customBindings.TryGetValue("@role", out var outcomeRole)
                        && !string.IsNullOrEmpty(outcomeRole))
                    {
                        outcomeDirectVars = new Dictionary<string, string>(StringComparer.Ordinal)
                        {
                            ["ROLE"] = outcomeRole!
                        };
                    }

                    string outcomeText = PlayerPresenceText.Outcome(template.Id, actualBranch.Id, evaluation.BoundHeroIds, outcomeDirectVars)
                        ?? firstText
                        ?? PlayerPresenceText.StandAsideOutcomeFallback();

                    string title = PlayerPresenceText.Title(settlementId);
                    var secondVm = new PlayerPresenceWindowVM(
                        title,
                        outcomeText,
                        PlayerPresenceText.Confirm(),
                        template.Id,
                        _ => { _windowOpen = false; });
                    secondVm.AddOption("ack", PlayerPresenceText.Ack(), string.Empty, true);

                    ModLog.Info($"WandererEvents: standAside executed with branch {actualBranch.Id}; opening the outcome window.");
                    // 事情已經發生，開不成視窗只印 WARN（OpenWindowOrWarn），冷卻已在第一個視窗記過
                    OpenWindowOrWarn(secondVm, template.Id, "stand-aside outcome");
                }
                else
                {
                    var outcome = PrepareOutcome(template, chosen, evaluation, day, customBindings, true, playerId);
                    var runOpts = new PlayerPresenceRunOptions
                    {
                        PlayerHeroId = playerId,
                        ChosenBranch = outcome.Branch,
                        InitialKnowerHeroIds = knowerIds,
                        Unseen = false,
                        CustomBindings = outcome.Bindings,
                        PlaceNear = PlaceNearFor(settlementId)
                    };

                    var run = SituationRunner.Execute(
                        template,
                        evaluation,
                        day,
                        "wanderer event",
                        _eventStore,
                        _traits,
                        _config,
                        _heroLookup,
                        presence: runOpts);

                    ApplyOutcomeEffects(template, outcome, evaluation, day, true, playerId, seen: true);
                    PlayerRelationNotifier.NotifyAll(run.PlayerRelationNotices, _heroLookup);
                    RecordChronicle(run.EventIds, day);

                    ModLog.Info($"WandererEvents: judge branch {chosen.Id} executed.");
                }
            }
            catch (Exception ex)
            {
                ModLog.Error($"WandererEvents: error carrying out choice for {pick.Template.Id}", ex);
            }
        }

        /// <summary>
        /// 不插手那一列代價的那一行：固定一句，後面接這個做法本身對玩家的影響（例如口角的「埃爾貢對你 ↓」）。
        /// </summary>
        private static string FormatStandAsideCost(SituationTemplate template, SituationBranchDef standAsideBranch, IReadOnlyDictionary<string, string?> boundHeroIds)
        {
            var directions = PlayerChoiceDirections.Compose(
                template,
                standAsideBranch,
                boundHeroIds,
                Hero.MainHero?.StringId ?? "player",
                false,
                false);
            return PlayerPresenceText.StandAsideCost(directions);
        }

        private void RecordOccurrenceDays(bool isBattle, double day)
        {
            string dayStr = day.ToString("R", CultureInfo.InvariantCulture);
            if (isBattle)
            {
                _lastBattleDayRaw = dayStr;
            }
            else
            {
                _lastDailyDayRaw = dayStr;
            }
            _lastAnyDayRaw = dayStr;
        }

        private string? RecordChronicle(IEnumerable<string> eventIds, double day)
        {
            string? firstText = null;
            string playerId = Hero.MainHero?.StringId ?? string.Empty;
            var heardLog = _heardLog?.Invoke();
            var engine = _engine?.Invoke();

            foreach (var eventId in eventIds ?? Enumerable.Empty<string>())
            {
                var evt = _eventStore?.Load(eventId);
                if (evt == null) continue;

                var playerEntry = evt.EntryFor(playerId);
                if (playerEntry == null) continue;
                if (!evt.IsVisibleToRumorSystem) continue;

                var facts = PlayerKnownFacts.Of(evt, playerEntry, engine);
                if (heardLog != null)
                {
                    bool added = heardLog.Record(evt, playerEntry, facts, day, id => _eventStore?.Load(id));
                    if (added && firstText == null && facts != null && facts.Count > 0)
                    {
                        firstText = facts[0].Text;
                    }
                }
            }
            return firstText;
        }

        /// <summary>
        /// 能被挑的人：玩家隊伍裡的英雄，合格、流浪者、玩家家族的夥伴；被排除的每位與原因都印出來（規格 4.1）。
        /// </summary>
        private List<Hero> FilterCandidates(MobileParty mainParty, Hero playerHero)
        {
            var candidates = new List<Hero>();
            if (mainParty?.MemberRoster == null) return candidates;

            foreach (var elem in mainParty.MemberRoster.GetTroopRoster())
            {
                var hero = elem.Character?.HeroObject;
                if (hero == null) continue;
                if (hero == playerHero) continue;

                string? reason = CandidateRejectionReason(hero, playerHero);
                if (reason != null)
                {
                    ModLog.Info($"WandererEvents: {hero.Name} ({hero.StringId}) not a candidate: {reason}.");
                    continue;
                }

                candidates.Add(hero);
            }

            return candidates;
        }

        private string? CandidateRejectionReason(Hero hero, Hero playerHero)
        {
            string? label = EligibilityLabel.GetRejectionReason(hero, _traits);
            if (label != null) return label;
            if (!hero.IsWanderer) return "not a wanderer";
            if (hero.CompanionOf != playerHero.Clan) return "not a companion of the player's clan";
            return null;
        }

        private SituationWorldContext CreateContext(
            MobileParty mainParty,
            Hero playerHero,
            HashSet<string> foughtHeroes,
            HashSet<string> woundedHeroes,
            bool battleWon,
            double day)
        {
            var roleHolders = GetMainPartyRoleHolders(mainParty);

            return new SituationWorldContext
            {
                Traits = _traits,
                PlayerHeroId = playerHero.StringId,
                Config = _config,
                IsPartyRoleHolder = id => roleHolders.Contains(id),
                HasTeachableSkill = (teacherId, learnerId) =>
                {
                    var t = _heroLookup?.Get(teacherId) ?? Hero.Find(teacherId);
                    var l = _heroLookup?.Get(learnerId) ?? Hero.Find(learnerId);
                    if (t == null || l == null) return false;

                    // 能教的只有教的人最高的那項，而且學的人那項比他低（規格 4.4）
                    var taught = TopSkill(t);
                    if (taught == null) return false;
                    return l.GetSkillValue(taught) < t.GetSkillValue(taught);
                },
                IsSkillBelow = (aId, bId, skillName) =>
                {
                    var a = _heroLookup?.Get(aId) ?? Hero.Find(aId);
                    var b = _heroLookup?.Get(bId) ?? Hero.Find(bId);
                    if (a == null || b == null) return false;

                    // "@taught" 是 b（教的人）最高的技能，見 TopSkill
                    SkillObject? skill = string.Equals(skillName, "@taught", StringComparison.Ordinal)
                        ? TopSkill(b)
                        : Skills.All.FirstOrDefault(s => string.Equals(s.StringId, skillName, StringComparison.OrdinalIgnoreCase));
                    if (skill == null) return false;

                    return a.GetSkillValue(skill) < b.GetSkillValue(skill);
                },
                FoughtInBattle = id => foughtHeroes.Contains(id),
                WoundedInBattle = id => woundedHeroes.Contains(id),
                BattleWon = () => battleWon
            };
        }

        private static HashSet<string> GetMainPartyRoleHolders(MobileParty party)
        {
            var holders = new HashSet<string>(StringComparer.Ordinal);
            if (party == null) return holders;

            var scout = party.GetRoleHolder(PartyRole.Scout);
            if (scout != null) holders.Add(scout.StringId);
            var engineer = party.GetRoleHolder(PartyRole.Engineer);
            if (engineer != null) holders.Add(engineer.StringId);
            var quartermaster = party.GetRoleHolder(PartyRole.Quartermaster);
            if (quartermaster != null) holders.Add(quartermaster.StringId);
            var surgeon = party.GetRoleHolder(PartyRole.Surgeon);
            if (surgeon != null) holders.Add(surgeon.StringId);

            return holders;
        }

        private static string? FindNearestSettlementId(MobileParty party)
        {
            if (party == null) return null;
            if (party.CurrentSettlement != null) return party.CurrentSettlement.StringId;

            try
            {
                var nearest = SettlementHelper.FindNearestSettlementToMobileParty(
                    party,
                    MobileParty.NavigationType.All,
                    s => s.IsTown || s.IsCastle || s.IsVillage);
                return nearest?.StringId;
            }
            catch (Exception ex)
            {
                ModLog.Warn($"WandererEvents: failed to find nearest settlement: {ex.Message}");
                return null;
            }
        }

        private Dictionary<string, string> BuildCustomBindings(
            SituationTemplate template,
            Dictionary<string, string> nonDerivedRoles,
            string? nearSettlementId,
            bool battleWon,
            double day)
        {
            var bindings = new Dictionary<string, string>(StringComparer.Ordinal);

            if (nearSettlementId != null)
            {
                bindings["@settlement"] = nearSettlementId;
                bindings["SETTLEMENT"] = nearSettlementId;
            }

            if (string.Equals(template.Id, "wanderer_party_role", StringComparison.OrdinalIgnoreCase))
            {
                string? incumbentId = nonDerivedRoles.TryGetValue("incumbent", out var iId) ? iId : null;
                var mainParty = MobileParty.MainParty;
                if (mainParty != null && incumbentId != null)
                {
                    // 比一比的結果還沒定：先填現任；真的執行時（PrepareOutcome）比一比或換人會再改
                    bindings["@winner"] = incumbentId;

                    var picked = PickIncumbentRole(mainParty, incumbentId, day);
                    if (picked != null)
                    {
                        bindings["@role"] = picked.RoleName;
                        bindings["@skill"] = picked.Skill.Name.ToString();
                        bindings["@disputedRoleSkill"] = picked.Skill.StringId;
                        ModLog.Info($"WandererEvents: party role {picked.RoleName} (skill {picked.Skill.Name}) bound from {picked.MatchCount} held role(s).");
                    }
                }
            }

            if (string.Equals(template.Id, "wanderer_teaching", StringComparison.OrdinalIgnoreCase))
            {
                string? teacherId = nonDerivedRoles.TryGetValue("teacher", out var tId) ? tId : null;
                var teacher = _heroLookup?.Get(teacherId ?? string.Empty) ?? Hero.Find(teacherId ?? string.Empty);
                var taught = teacher != null ? TopSkill(teacher) : null;
                if (taught != null)
                {
                    // 被教的是教的人最高的技能（同分照固定技能順序取第一個）
                    bindings["@skill"] = taught.Name.ToString();
                    bindings["@taught"] = taught.StringId;
                    ModLog.Info($"WandererEvents: teaching skill {taught.StringId} (the teacher's highest).");
                }
            }

            if (string.Equals(template.Id, "wanderer_campfire_jab", StringComparison.OrdinalIgnoreCase))
            {
                string? speakerId = nonDerivedRoles.TryGetValue("speaker", out var spId) ? spId : null;
                string? listenerId = nonDerivedRoles.TryGetValue("listener", out var lsId) ? lsId : null;
                var spHero = _heroLookup?.Get(speakerId ?? string.Empty) ?? Hero.Find(speakerId ?? string.Empty);
                var lsHero = _heroLookup?.Get(listenerId ?? string.Empty) ?? Hero.Find(listenerId ?? string.Empty);

                string spCulture = spHero?.Culture?.StringId ?? string.Empty;
                string lsCulture = lsHero?.Culture?.StringId ?? string.Empty;

                bool sameCulture = string.Equals(spCulture, lsCulture, StringComparison.OrdinalIgnoreCase);
                string? sayingSuffix = JabSayingSuffix(lsCulture);

                // 同文化、或文化不在六個說法裡：退回不點明主題的一句（規格 4.4）
                bool generic = sameCulture || sayingSuffix == null;
                if (!sameCulture && sayingSuffix == null)
                {
                    ModLog.Info($"WandererEvents: campfire jab listener culture '{lsCulture}' has no saying in the table; using the generic line.");
                }

                // 退回的那句要點出是被笑那人的糗事；英文靠所有格代名詞，照他的性別先填好（中文那句不用這個變數）
                string topicText;
                if (generic)
                {
                    string hisKey = "VividWorld_Pronoun_His_" + (lsHero == null ? "N" : lsHero.IsFemale ? "F" : "M");
                    string hisFallback = lsHero == null ? "their" : lsHero.IsFemale ? "her" : "his";
                    var topic = new TextObject("{=VividWorld_CampfireJab_SameTopic}{HIS} past blunders");
                    topic.SetTextVariable("HIS", new TextObject("{=" + hisKey + "}" + hisFallback));
                    topicText = topic.ToString();
                }
                else
                {
                    topicText = (lsHero?.Culture?.Name?.ToString() ?? lsCulture) + new TextObject("{=VividWorld_CampfireJab_FolkSuffix} folk").ToString();
                }
                string sayingKey = generic ? "VividWorld_CampfireJab_SameSaying" : "VividWorld_CampfireJab_Saying_" + sayingSuffix;

                bindings["@jab_topic"] = topicText;
                bindings["@jab_saying"] = new TextObject("{=" + sayingKey + "}saying something out of line").ToString();
                ModLog.Info($"WandererEvents: campfire jab listener culture '{lsCulture}', same culture {sameCulture}, generic {generic}, topic '{topicText}'.");
            }

            return bindings;
        }

        private static string? JabSayingSuffix(string listenerCulture)
        {
            switch (listenerCulture.ToLowerInvariant())
            {
                case "empire": return "Empire";
                case "sturgia": return "Sturgia";
                case "aserai": return "Aserai";
                case "vlandia": return "Vlandia";
                case "khuzait": return "Khuzait";
                case "battania": return "Battania";
                default: return null;
            }
        }

        /// <summary>
        /// 這個人最高的技能；同分照 Skills.All 的順序取第一個。
        /// </summary>
        private static SkillObject? TopSkill(Hero hero)
        {
            SkillObject? best = null;
            int bestValue = int.MinValue;
            foreach (var skill in Skills.All)
            {
                if (skill == null) continue;
                int value = hero.GetSkillValue(skill);
                if (value > bestValue)
                {
                    best = skill;
                    bestValue = value;
                }
            }
            return best;
        }

        private static void LogBattleDetails(
            MapEvent mapEvent,
            MapEventParty? playerParty,
            HashSet<string> foughtHeroes,
            HashSet<string> woundedHeroes)
        {
            var foughtNames = foughtHeroes.Select(id => (Hero.Find(id)?.Name?.ToString() ?? id)).ToList();
            var woundedNames = woundedHeroes.Select(id => (Hero.Find(id)?.Name?.ToString() ?? id)).ToList();

            ModLog.Info($"WandererEvents: battle ended - fought: [{string.Join(", ", foughtNames)}], wounded: [{string.Join(", ", woundedNames)}].");

            if (playerParty?.Troops != null)
            {
                int healthyCount = 0;
                int woundedCount = 0;
                int killedCount = 0;
                foreach (var troop in playerParty.Troops)
                {
                    if (troop.IsWounded) woundedCount++;
                    else if (troop.IsKilled) killedCount++;
                    else healthyCount++;
                }
                ModLog.Info($"  Troops breakdown: healthy={healthyCount}, wounded={woundedCount}, killed={killedCount}");

                // 每位英雄在 Troops 裡那一筆的狀態與 XpGained：用來判斷「上場」的近似準不準
                foreach (var troop in playerParty.Troops)
                {
                    var h = troop.Troop?.HeroObject;
                    if (h == null) continue;
                    ModLog.Info($"  Troops entry {h.Name} ({h.StringId}): wounded={troop.IsWounded}, killed={troop.IsKilled}, xpGained={troop.XpGained}");
                }
            }
        }

        private sealed class ModuleEvaluator : IPlayerPresenceEvaluator
        {
            private readonly WandererEventBehavior _owner;
            private readonly Dictionary<string, Hero> _heroesById;
            private readonly double _day;
            private readonly SituationHistoryIndex _history;
            private readonly SituationWorldContext _context;
            private readonly Dictionary<string, SituationRoleFacts> _factsCache = new(StringComparer.Ordinal);

            internal ModuleEvaluator(WandererEventBehavior owner, IEnumerable<Hero> heroes, double day, SituationWorldContext context)
            {
                _owner = owner;
                _day = day;
                _heroesById = heroes.ToDictionary(h => h.StringId, h => h, StringComparer.Ordinal);
                _history = new SituationHistoryIndex(owner._eventStore?.Index?.Entries ?? new List<RumorIndexEntry>(), day);
                _context = context;

                var main = Hero.MainHero;
                if (main != null)
                {
                    var playerFacts = SituationFactsReader.Read(main);
                    _factsCache[main.StringId] = playerFacts;
                    _heroesById[main.StringId] = main;
                }
            }

            public PlayerPresenceEvaluation Evaluate(
                SituationTemplate template,
                IReadOnlyList<SituationConditionDef> conditions,
                IReadOnlyDictionary<string, string> nonDerivedRoleHeroIds,
                bool forced)
            {
                var heroes = new Dictionary<string, Hero>(StringComparer.OrdinalIgnoreCase);
                foreach (var kv in nonDerivedRoleHeroIds)
                {
                    Hero? hero = _heroesById.TryGetValue(kv.Value, out var known) ? known : _owner._heroLookup?.Get(kv.Value);
                    if (hero == null)
                    {
                        return new PlayerPresenceEvaluation
                        {
                            ConditionResults = new[]
                            {
                                new ConditionEvaluationResult { Ok = false, Type = "heroFound", Label = $"heroFound({kv.Key})", Detail = $"hero {kv.Value} not found" }
                            }
                        };
                    }
                    heroes[kv.Key] = hero;
                }

                var evaluation = SituationRunner.Evaluate(
                    template,
                    heroes,
                    _day,
                    _owner._traits,
                    _history,
                    _factsCache,
                    _owner._heroLookup,
                    _owner._eventStore,
                    _owner._config,
                    context: _context,
                    forced: forced,
                    conditionsOverride: conditions);

                return new PlayerPresenceEvaluation
                {
                    ConditionResults = evaluation.ConditionResults,
                    BoundHeroIds = evaluation.BoundHeroIds,
                    State = evaluation
                };
            }
        }

        /// <summary>
        /// 開發者工具狀態資訊。
        /// </summary>
        internal string FormatDevStatus()
        {
            double today = TaleWorlds.CampaignSystem.Campaign.Current != null ? CampaignTime.Now.ToDays : 0.0;
            var cfg = _config.Situations?.WandererEvents ?? new WandererEventsConfig();
            string status = WandererEventLogFormatter.FormatStatus(
                cfg.Enabled,
                today,
                LastBattleDay,
                LastDailyDay,
                LastAnyDay,
                cfg,
                _roleRecords?.Data ?? new WandererRoleData());
            return status + Environment.NewLine + ComplaintStatusLine();
        }

        /// <summary>
        /// 開發者工具強制擲骰。
        /// </summary>
        internal string ForceRoll(string? situationId, bool? unseen, bool isBattle = false)
        {
            var mainParty = MobileParty.MainParty;
            var playerHero = Hero.MainHero;
            if (mainParty == null || playerHero == null) return "Main party not available.";

            var candidates = FilterCandidates(mainParty, playerHero);
            if (candidates.Count < 2)
            {
                return $"Cannot roll: only {candidates.Count} eligible wanderers (need at least 2).";
            }

            var allWandererTemplates = (SituationCatalogStore.Catalog?.Situations ?? new List<SituationTemplate>())
                .Where(t => string.Equals(t.Trigger, "wandererBattle", StringComparison.OrdinalIgnoreCase) ||
                            string.Equals(t.Trigger, "wandererDaily", StringComparison.OrdinalIgnoreCase))
                .ToList();

            var targetTemplate = !string.IsNullOrEmpty(situationId)
                ? allWandererTemplates.FirstOrDefault(t => string.Equals(t.Id, situationId, StringComparison.OrdinalIgnoreCase))
                : null;

            bool isBattleSituation = targetTemplate != null
                ? string.Equals(targetTemplate.Trigger, "wandererBattle", StringComparison.OrdinalIgnoreCase)
                : isBattle;

            HashSet<string> foughtHeroes;
            HashSet<string> woundedHeroes;
            bool battleWon;

            if (isBattleSituation)
            {
                if (_lastFoughtHeroes == null || _lastFoughtHeroes.Count == 0)
                {
                    return "No recent battle recorded for wanderer battle events.";
                }
                foughtHeroes = _lastFoughtHeroes;
                woundedHeroes = _lastWoundedHeroes ?? new HashSet<string>(StringComparer.Ordinal);
                battleWon = _lastBattleWon ?? false;
            }
            else
            {
                foughtHeroes = new HashSet<string>(StringComparer.Ordinal);
                woundedHeroes = new HashSet<string>(StringComparer.Ordinal);
                battleWon = false;
            }

            double day = CampaignTime.Now.ToDays;
            var context = CreateContext(mainParty, playerHero, foughtHeroes, woundedHeroes, battleWon, day);

            var input = new WandererRollInput
            {
                Trigger = isBattleSituation ? WandererTrigger.Battle : WandererTrigger.Daily,
                Today = day,
                AvailableWandererHeroIds = candidates.Select(h => h.StringId).ToList(),
                PlayerHeroId = playerHero.StringId,
                CampaignSeed = (long)RumorSeed.Of(0, playerHero.StringId),
                Force = new WandererRollForce { SituationId = situationId, Unseen = unseen }
            };

            var evaluator = new ModuleEvaluator(this, candidates, day, context);
            var rng = new SplitMix64Rng();

            var roll = WandererEventRoller.Roll(input, _config, allWandererTemplates, evaluator, rng);
            LastRoll = roll;
            LastRollDay = day;

            ModLog.Info(WandererEventLogFormatter.FormatRoll(roll));

            if (roll.Outcome == WandererRollOutcome.Happened && roll.Pick != null)
            {
                var pick = roll.Pick;
                string? nearSettlementId = FindNearestSettlementId(mainParty);
                var customBindings = BuildCustomBindings(pick.Template, pick.Assignment.NonDerivedRoleHeroIds, nearSettlementId, battleWon, day);
                var knowerIds = candidates.Select(c => c.StringId).ToList();
                var evaluation = (SituationEvaluation)pick.Assignment.Evaluation.State!;
                evaluation.SettlementId = nearSettlementId ?? "none";

                ProcessWandererEvent(pick, evaluation, day, nearSettlementId, customBindings, knowerIds, isBattle: isBattleSituation);
                return $"Forced wanderer event {pick.Template.Id} triggered.";
            }

            return $"Wanderer event roll did not happen: {roll.Outcome} - {roll.Reason}";
        }
    }
}
