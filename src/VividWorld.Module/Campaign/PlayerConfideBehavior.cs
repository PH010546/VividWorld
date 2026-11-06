#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Encounters;
using TaleWorlds.CampaignSystem.GameState;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.Localization;
using TaleWorlds.MountAndBlade;
using VividWorld.Core.Config;
using VividWorld.Core.Events;
using VividWorld.Core.Feelings;
using VividWorld.Core.Persistence;
using VividWorld.Core.Presentation;
using VividWorld.Core.Rumors;
using VividWorld.Core.Secrets;
using VividWorld.Presentation;
using VividWorld.UI;

namespace VividWorld.Campaign
{
    /// <summary>
    /// 秘密私下吐露給玩家的處理：NPC 決定吐露時排隊進戰役資料夾的 secret_confide_queue.json；
    /// 玩家處於安全時機（回到大地圖或城鎮、不在對話/戰鬥/選單、當天尚未跳過入局或流浪者事件視窗）時彈出視窗；
    /// 確認後將玩家加入 KnownBy、收進見聞日誌並自排隊中移除。
    /// </summary>
    internal sealed class PlayerConfideBehavior : CampaignBehaviorBase, IConfideToPlayerSink
    {
        internal static PlayerConfideBehavior? Current { get; private set; }

        private readonly VividWorldConfig _config;
        private string _campaignId = string.Empty;
        private WorldEventStore? _eventStore;
        private HeroLookup? _heroLookup;
        private IHeroTraitLookup? _traits;
        private FeelingResolver? _feelingResolver;
        private Func<PlayerHeardLogStore?>? _heardLog;
        private Func<RumorEngine?>? _engine;
        private PlayerConfideQueueStore? _queueStore;

        private int _generation;
        private bool _windowOpen;
        private bool _ready;
        private double? _lastWindowDay;
        private string _lastDeferReason = string.Empty;

        internal PlayerConfideBehavior(VividWorldConfig config)
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));
            Current = this;
        }

        internal void Initialize(
            string campaignId,
            WorldEventStore eventStore,
            HeroLookup heroLookup,
            IHeroTraitLookup traits,
            FeelingResolver? feelingResolver,
            Func<PlayerHeardLogStore?> heardLog,
            Func<RumorEngine?> engine)
        {
            _campaignId = campaignId ?? throw new ArgumentNullException(nameof(campaignId));
            _eventStore = eventStore ?? throw new ArgumentNullException(nameof(eventStore));
            _heroLookup = heroLookup ?? throw new ArgumentNullException(nameof(heroLookup));
            _traits = traits ?? throw new ArgumentNullException(nameof(traits));
            _feelingResolver = feelingResolver;
            _heardLog = heardLog;
            _engine = engine;

            string queuePath = VividWorldPaths.ConfideQueueFile(_campaignId);
            _queueStore = new PlayerConfideQueueStore(queuePath, new SystemFileWriter(), ModLogSink.Instance);
            var loadResult = _queueStore.Load();
            int pendingCount = loadResult.Count;
            ModLog.Info($"secret confide queue loaded: {pendingCount} pending window(s)");

            PlayerPresenceWindowManager.Close();
            _generation++;
            _windowOpen = false;
            _ready = true;
            Current = this;
        }

        public override void RegisterEvents()
        {
            CampaignEvents.HourlyTickEvent.AddNonSerializedListener(this, OnHourlyTick);
            CampaignEvents.DailyTickEvent.AddNonSerializedListener(this, OnDailyTick);
            CampaignEvents.SettlementEntered.AddNonSerializedListener(this, OnSettlementEntered);
            CampaignEvents.ConversationEnded.AddNonSerializedListener(this, OnConversationEnded);
        }

        public override void SyncData(IDataStore dataStore)
        {
            // 排隊紀錄存在戰役資料夾的獨立檔案，不新增 SyncData 鍵保持存檔相容
        }

        public void OnConfideToPlayer(WorldEvent evt, string tellerHeroId, double day)
        {
            if (evt == null || string.IsNullOrEmpty(tellerHeroId)) return;
            if (_queueStore == null) return;

            var item = new PlayerConfideQueueItem
            {
                EventId = evt.EventId,
                TellerHeroId = tellerHeroId,
                Day = day
            };

            _queueStore.Enqueue(item);
            ModLog.Info($"secret confide to player queued: {evt.EventId} from {tellerHeroId} on day {day:0.00}");

            TryProcessQueue(day);
        }

        private void OnHourlyTick()
        {
            if (!_ready) return;
            double day = CampaignTime.Now.ToDays;
            TryProcessQueue(day);
        }

        private void OnDailyTick()
        {
            if (!_ready) return;
            double day = CampaignTime.Now.ToDays;
            TryProcessQueue(day);
        }

        private void OnSettlementEntered(MobileParty party, Settlement settlement, Hero hero)
        {
            if (!_ready || party != MobileParty.MainParty) return;
            double day = CampaignTime.Now.ToDays;
            TryProcessQueue(day);
        }

        private void OnConversationEnded(IEnumerable<CharacterObject> characters)
        {
            if (!_ready) return;
            double day = CampaignTime.Now.ToDays;
            TryProcessQueue(day);
        }

        internal void TryTickQueue()
        {
            if (!_ready || _queueStore == null || _queueStore.Count == 0) return;
            if (TaleWorlds.CampaignSystem.Campaign.Current == null) return;
            TryProcessQueue(CampaignTime.Now.ToDays);
        }

        internal void TryProcessQueue(double day)
        {
            if (!_ready || _queueStore == null || _queueStore.Count == 0) return;
            if (_windowOpen || PlayerPresenceWindowManager.IsOpen) return;

            if (TaleWorlds.CampaignSystem.Campaign.Current?.ConversationManager?.IsConversationInProgress == true)
            {
                NoteDeferReason("in conversation");
                return;
            }

            if (Mission.Current != null ||
                (MobileParty.MainParty != null && (MobileParty.MainParty.MapEvent != null || MobileParty.MainParty.SiegeEvent != null)) ||
                PlayerEncounter.Current != null)
            {
                NoteDeferReason("in battle");
                return;
            }

            // 守在被圍的城裡時，隊伍自己的圍城事件是空的（它只讀攻城那一方的營地），所以要看玩家所在的城本身是不是正被圍（帳本 D-173）。
            if (MobileParty.MainParty?.CurrentSettlement != null && MobileParty.MainParty.CurrentSettlement.IsUnderSiege)
            {
                NoteDeferReason("in a besieged settlement");
                return;
            }

            if (InformationManager.IsAnyInquiryActive() || !(Game.Current?.GameStateManager?.ActiveState is MapState))
            {
                NoteDeferReason("in menu");
                return;
            }

            int todayInt = (int)day;
            bool showedToday = false;

            if (PlayerPresenceBehavior.Current?.LastOccurrenceDay != null &&
                (int)PlayerPresenceBehavior.Current.LastOccurrenceDay.Value == todayInt)
            {
                showedToday = true;
            }
            else if (WandererEventBehavior.Current?.LastAnyDay != null &&
                     (int)WandererEventBehavior.Current.LastAnyDay.Value == todayInt)
            {
                showedToday = true;
            }
            else if (_lastWindowDay.HasValue && (int)_lastWindowDay.Value == todayInt)
            {
                showedToday = true;
            }

            if (showedToday)
            {
                NoteDeferReason("already showed a window today");
                return;
            }

            _lastDeferReason = string.Empty;

            var item = _queueStore.Peek();
            if (item == null) return;

            var evt = _eventStore?.Load(item.EventId);
            if (evt == null || evt.SecretRetired)
            {
                _queueStore.Dequeue();
                ModLog.Warn($"secret confide to player dropped: event {item.EventId} not found or retired");
                return;
            }

            Present(item, evt, day);
        }

        private void NoteDeferReason(string reason)
        {
            if (!string.Equals(_lastDeferReason, reason, StringComparison.Ordinal))
            {
                _lastDeferReason = reason;
                ModLog.Info($"secret confide deferred: {reason}");
            }
        }

        private bool Present(PlayerConfideQueueItem item, WorldEvent evt, double day)
        {
            var tellerHero = Hero.Find(item.TellerHeroId);
            string tellerName = tellerHero?.Name?.ToString() ?? item.TellerHeroId;

            var openingObj = new TextObject("{=VividWorld_SecretConfide_Opening}{TELLER} draws you aside and lowers {TELLER.his} voice.");
            if (tellerHero?.Name != null)
            {
                openingObj.SetTextVariable("TELLER", tellerHero.Name);
            }
            else
            {
                openingObj.SetTextVariable("TELLER", new TextObject(tellerName));
            }
            string openingText = openingObj.ToString();

            var tellerEntry = evt.EntryFor(item.TellerHeroId);
            var facts = evt.Facts ?? (IReadOnlyList<Fact>)Array.Empty<Fact>();
            if (tellerEntry?.KnownFactIds != null && tellerEntry.KnownFactIds.Count > 0)
            {
                facts = facts.Where(f => tellerEntry.KnownFactIds.Contains(f.Id)).ToList();
            }

            var template = EventCatalogStore.TemplateByType(evt.Type);
            var traits = _traits?.Of(item.TellerHeroId);

            var composed = RumorTextComposer.Compose(
                evt,
                facts,
                _config.Presentation,
                prefix: null,
                speakerHeroId: item.TellerHeroId,
                sourceHeroId: tellerEntry?.SourceHeroId,
                isGist: false,
                heldBack: false,
                template: template,
                speakerTraits: traits);

            if (_feelingResolver != null)
            {
                composed.Feeling = _feelingResolver.Resolve(evt, item.TellerHeroId, isGist: false);
            }

            var rendered = FallbackTextRenderer.RenderBoth(composed, _config.Presentation);
            string spokenLine = rendered.DisplayText;

            string description = openingText + "\n\n" + spokenLine;

            string? settlementId = tellerHero?.CurrentSettlement?.StringId ?? MobileParty.MainParty?.CurrentSettlement?.StringId;
            string title = PlayerPresenceText.Title(settlementId);

            var okText = new TextObject("{=VividWorld_SecretConfide_Ok}I'll remember it.").ToString();

            int generation = _generation;
            var vm = new PlayerPresenceWindowVM(
                title,
                description,
                PlayerPresenceText.Confirm(),
                "secret_confided",
                _ => OnAcknowledged(generation, item, evt, day, composed, facts));

            vm.AddOption("ack", okText, string.Empty, true);

            _windowOpen = true;
            if (!PlayerPresenceWindowManager.Open(vm))
            {
                _windowOpen = false;
                ModLog.Warn($"secret confide window failed to open for {evt.EventId} from {item.TellerHeroId}");
                return false;
            }

            ModLog.Info($"secret confide window opened: {evt.EventId} from {tellerName} ({item.TellerHeroId}), illustration secret_confided");
            return true;
        }

        private void OnAcknowledged(int generation, PlayerConfideQueueItem item, WorldEvent evt, double day, ComposedRumor composed, IReadOnlyList<Fact> facts)
        {
            if (generation == _generation)
            {
                _windowOpen = false;
            }

            string playerHeroId = Hero.MainHero?.StringId ?? "player";
            var tellerEntry = evt.EntryFor(item.TellerHeroId);
            int hop = (tellerEntry?.Hop ?? 0) + 1;

            var playerEntry = evt.EntryFor(playerHeroId);
            if (playerEntry == null)
            {
                playerEntry = new KnownByEntry
                {
                    HeroId = playerHeroId,
                    Hop = hop,
                    SourceHeroId = item.TellerHeroId,
                    LearnedDay = day,
                    KnownFactIds = facts.Select(f => f.Id).ToList()
                };
                evt.KnownBy ??= new List<KnownByEntry>();
                evt.KnownBy.Add(playerEntry);
            }
            else
            {
                if (hop < playerEntry.Hop)
                {
                    playerEntry.Hop = hop;
                    playerEntry.SourceHeroId = item.TellerHeroId;
                }

                if (playerEntry.KnownFactIds != null)
                {
                    var factSet = new HashSet<string>(playerEntry.KnownFactIds, StringComparer.Ordinal);
                    foreach (var f in facts) factSet.Add(f.Id);
                    playerEntry.KnownFactIds = factSet.ToList();
                }
                else
                {
                    playerEntry.KnownFactIds = facts.Select(f => f.Id).ToList();
                }
            }

            _eventStore?.Upsert(evt);

            var heardLog = _heardLog?.Invoke();
            if (heardLog != null)
            {
                var telling = new PlayerHeardSource
                {
                    HeroId = item.TellerHeroId,
                    Day = day,
                    Hop = hop,
                    FactIds = facts.Select(f => f.Id).ToList()
                };

                if (composed.Feeling != null)
                {
                    telling.FeelingLineKey = composed.Feeling.LineKey;
                    telling.FeelingAddressKey = composed.Feeling.AddressKey;
                    telling.FeelingFocusHeroId = composed.Feeling.FocusHeroId;
                }

                telling.CaptureSpokenLine(composed);

                heardLog.RecordTelling(evt, playerEntry, facts, day, telling, id => _eventStore?.Load(id));
            }

            _queueStore?.Dequeue();
            _lastWindowDay = day;

            ModLog.Info($"secret confide window confirmed: {evt.EventId} from {item.TellerHeroId} (player hop {hop})");
        }
    }
}
