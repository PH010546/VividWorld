using System;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Core;
using TaleWorlds.InputSystem;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;
using VividWorld.Campaign;
using VividWorld.Core.Config;
using VividWorld.Core.Diagnostics;
using VividWorld.Core.Presentation;
using VividWorld.Debug;
using VividWorld.Dialogue;
using VividWorld.Presentation;
using VividWorld.UI;

namespace VividWorld
{
    public class SubModule : MBSubModuleBase
    {
        internal const string HarmonyId = "mod.vividworld";
        private static RumorCampaignBehavior? _activeBehavior;

        // SNAP1 / M8：熱鍵。戰役行為沒有逐幀的 tick，所以掛在這裡（帳本 S-18）。
        private static VividWorldConfig? _config;
        // MCM 的設定畫面在**主選單**就開得到，而 `_config` 要到戰役開始才有值、
        // 而且 `OnGameEnd` 與熱鍵的例外保險都會把它清成 null。橋接另外持有一份，
        // 從模組載入起就在，戰役期間指向同一個活的設定物件。
        private static VividWorldConfig? _menuConfig;
        private static HotkeyBinding _snapshotKey = DefaultSnapshotKey;
        private static HotkeyBinding _chronicleKey = DefaultChronicleKey;
        private static bool _snapshotKeyWasDown;
        private static bool _chronicleKeyWasDown;

        // 預設值。紀事視窗是 Ctrl+L（單獨的 L 是原生的氏族視窗，帳本 D-73）。
        private static HotkeyBinding DefaultSnapshotKey => new HotkeyBinding(InputKey.F9);
        private static HotkeyBinding DefaultChronicleKey => new HotkeyBinding(InputKey.L, ctrl: true);

        protected override void OnSubModuleLoad()
        {
            ModLog.Info("Vivid World loaded.");
            try
            {
                // 主選單的設定畫面要用它。讀檔失敗不准擋住模組載入。
                _menuConfig = ConfigStore.LoadOrCreate();
            }
            catch (Exception ex)
            {
                ModLog.Error("Failed to load config at submodule load; the settings menu will bind later.", ex);
            }
        }

        protected override void OnGameStart(Game game, IGameStarter gameStarterObject)
        {
            if (!(gameStarterObject is CampaignGameStarter starter)) return;
            var config = ConfigStore.LoadOrCreate();
            if (!config.Enabled)
            {
                ModLog.Info("Vivid World disabled by config.");
                return;
            }

            _config = config;
            _menuConfig = config;   // 戰役期間選單改的就是這一份
            RefreshHotkeys();
            ModLog.Info(HotkeyLogFormatter.FormatBindings(_chronicleKey.ToString(), _snapshotKey.ToString()));

            var dialogs  = new RumorDialogBehavior(config);        // 建構子只收 config
            var producer = new FakeEventProducerBehavior(config);  // 同上
            var realEvents = new RealEventSourceBehavior(config);  // MS2: 真事件掛鉤
            var situationScan = new SituationScanBehavior(config); // SE3: 每日情境掃描
            EventCatalogStore.Load(config);
            SituationCatalogStore.Load(config);
            SituationRunner.ResetSessionCounters();
            GrudgeApplier.ResetSessionCounters();
            SituationScanBehavior.ResetSessionState();
            _activeBehavior = new RumorCampaignBehavior(config, dialogs, producer, realEvents, situationScan);

            starter.AddBehavior(_activeBehavior);
            starter.AddBehavior(dialogs);
            starter.AddBehavior(producer);
            starter.AddBehavior(realEvents);
            starter.AddBehavior(situationScan);

            ModLog.Info("Registered 5 campaign behaviors on the game starter (see ledger D-38).");
            ModLog.Flush();
        }

        internal static void RefreshHotkeys()
        {
            if (_config == null) return;
            _snapshotKey = ParseHotkey("snapshot manager", _config.Presentation?.SnapshotManagerHotkey, DefaultSnapshotKey);
            _chronicleKey = ParseHotkey("chronicle", _config.Presentation?.ChronicleHotkey, DefaultChronicleKey);
        }

        /// <summary>
        /// 熱鍵輪詢。**只在戰役進行中、而且沒有別的視窗開著時才算數**——
        /// 主選單按到不該有反應。自己記上一幀有沒有按著，避免長按連開好幾次。
        /// </summary>
        protected override void OnApplicationTick(float dt)
        {
            try
            {
                VividWorld.Mcm.McmBridge.TryBind(_menuConfig);

                if (ChronicleWindowManager.IsOpen)
                {
                    ChronicleWindowManager.Tick();
                    bool chronicleDownWhileOpen = _chronicleKey.IsPressed();
                    if (chronicleDownWhileOpen && !_chronicleKeyWasDown)
                    {
                        ChronicleWindowManager.Close();
                    }
                    _chronicleKeyWasDown = chronicleDownWhileOpen;
                    return;
                }

                if (ChronicleWindowManager.ReturnTracker.State != EncyclopediaReturnState.Idle)
                {
                    TickEncyclopediaReturnTracker();
                    if (ChronicleWindowManager.IsOpen)
                    {
                        return;
                    }
                }

                if (_config == null || _activeBehavior == null) return;
                if (TaleWorlds.CampaignSystem.Campaign.Current == null) return;   // VividWorld.Campaign 命名空間會蓋掉短名字
                if (SnapshotManagerScreen.IsOpen) return;
                if (InformationManager.IsAnyInquiryActive()) return;
                if (TaleWorlds.CampaignSystem.Campaign.Current.ConversationManager?.IsConversationInProgress == true) return;
                if (Mission.Current != null) return;

                bool snapshotDown = _snapshotKey.IsPressed();
                if (snapshotDown && !_snapshotKeyWasDown)
                {
                    SnapshotManagerScreen.Show(_activeBehavior.CampaignId, _config);
                }
                _snapshotKeyWasDown = snapshotDown;

                bool chronicleDown = _chronicleKey.IsPressed();
                if (chronicleDown && !_chronicleKeyWasDown)
                {
                    OpenChronicleWindow();
                }
                _chronicleKeyWasDown = chronicleDown;
            }
            catch (Exception ex)
            {
                // SNAP1 就定下的保護：這個方法**每一幀**都會跑，例外只要會重現就是每秒
                // 幾十行錯誤把 log 洗掉。一直噴例外比沒有熱鍵更糟，所以整組熱鍵關掉。
                ModLog.Error("Hotkey polling failed in OnApplicationTick - hotkeys disabled for this session", ex);
                ChronicleWindowManager.Close();
                _config = null;
            }
        }

        private static void TickEncyclopediaReturnTracker()
        {
            if (TaleWorlds.CampaignSystem.Campaign.Current == null)
            {
                ChronicleWindowManager.ReturnTracker.Cancel("campaign ended");
                ModLog.Info(ChronicleLogFormatter.FormatNotReopening("campaign ended"));
                return;
            }

            if (Mission.Current != null)
            {
                ChronicleWindowManager.ReturnTracker.Cancel("a mission started");
                ModLog.Info(ChronicleLogFormatter.FormatNotReopening("a mission started"));
                return;
            }

            if (TaleWorlds.CampaignSystem.Campaign.Current.ConversationManager?.IsConversationInProgress == true)
            {
                ChronicleWindowManager.ReturnTracker.Cancel("a conversation started");
                ModLog.Info(ChronicleLogFormatter.FormatNotReopening("a conversation started"));
                return;
            }

            bool chronicleDown = _chronicleKey.IsPressed();
            if (chronicleDown && !_chronicleKeyWasDown)
            {
                _chronicleKeyWasDown = chronicleDown;
                ChronicleWindowManager.ReturnTracker.Cancel("the player reopened it");
                ModLog.Info(ChronicleLogFormatter.FormatNotReopening("the player reopened it"));
                OpenChronicleWindow();
                return;
            }

            bool? isEncyclopediaOpen = null;
            try
            {
                var mapScreen = SandBox.View.Map.MapScreen.Instance;
                var encyclopediaView = mapScreen?.EncyclopediaScreenManager;
                isEncyclopediaOpen = encyclopediaView?.IsEncyclopediaOpen;
            }
            catch (Exception ex)
            {
                string reason = "error: " + ex.Message;
                ChronicleWindowManager.ReturnTracker.Cancel(reason);
                ModLog.Info(ChronicleLogFormatter.FormatNotReopening(reason));
                return;
            }

            var action = ChronicleWindowManager.ReturnTracker.Step(isEncyclopediaOpen, out var stepReason);
            if (ChronicleWindowManager.ReturnTracker.JustOpened)
            {
                ModLog.Info(ChronicleLogFormatter.FormatEncyclopediaOpened(ChronicleWindowManager.ReturnTracker.OpenedAfterFrames));
                ModLog.Flush();
            }

            if (action == EncyclopediaReturnAction.Reopen)
            {
                ModLog.Info(ChronicleLogFormatter.FormatEncyclopediaClosedReopening());
                _chronicleKeyWasDown = _chronicleKey.IsPressed();
                OpenChronicleWindow();
            }
            else if (action == EncyclopediaReturnAction.GaveUp)
            {
                ModLog.Info(ChronicleLogFormatter.FormatNotReopening(stepReason ?? ChronicleWindowManager.ReturnTracker.LastReason ?? "unknown"));
                ModLog.Flush();
            }
        }

        private static void OpenChronicleWindow()
        {
            try
            {
                if (_config == null || _activeBehavior == null) return;
                var heardLog = _activeBehavior.PlayerHeardLog;
                var heroLookup = _activeBehavior.HeroLookup;
                if (heardLog == null) return;

                string playerId = Hero.MainHero?.StringId ?? "player";
                double currentDay = TaleWorlds.CampaignSystem.Campaign.Current != null ? CampaignTime.Now.ToDays : 0.0;
                int maxEntries = _config.Presentation?.ChronicleMaxEntries ?? 50;

                var provider = new ChronicleProvider(
                    heardLog,
                    EventCatalogStore.TemplateByType,
                    _config.Presentation ?? new PresentationConfig());

                var entries = provider.ForPlayer(maxEntries, currentDay, out var stats);

                string logLine = ChronicleLogFormatter.FormatOpen(playerId, currentDay, stats, maxEntries);
                ModLog.Info(logLine);
                ModLog.Flush();

                var vm = new ChronicleWindowVM(entries, _config.Presentation, heroLookup);
                ChronicleWindowManager.Open(vm, _config.Presentation);
            }
            catch (Exception ex)
            {
                ModLog.Error("Failed to open chronicle window", ex);
            }
        }

        /// <summary>
        /// 設定值是 InputKey 的名字，前面可以帶 <c>Ctrl+</c>／<c>Alt+</c>／<c>Shift+</c>（帳本 S-18 的做法）。
        /// 解析不出來就退回 fallback 並說出來是哪一把鑰匙、原本寫的是什麼。
        /// </summary>
        private static HotkeyBinding ParseHotkey(string which, string? name, HotkeyBinding fallback)
        {
            var binding = HotkeyBinding.FromSpec(HotkeySpec.Parse(name));
            if (binding.HasValue) return binding.Value;

            ModLog.Info(HotkeyLogFormatter.FormatUnknown(which, name, fallback.ToString()));
            return fallback;
        }

        public override void OnGameEnd(Game game)
        {
            try
            {
                ChronicleWindowManager.ReturnTracker.Cancel("campaign ended");
                ChronicleWindowManager.Close();
                _activeBehavior?.Flush();
                _activeBehavior = null;
                _config = null;
                // 回到主選單之後設定畫面還開得到，所以另外重讀一份給它用。
                try { _menuConfig = ConfigStore.LoadOrCreate(); } catch { /* 選單少一輪同步，不值得讓 OnGameEnd 失敗 */ }
                Api.VividWorldEventApi.Store = null;
            }
            catch (Exception ex)
            {
                ModLog.Error("Error during OnGameEnd", ex);
            }
            finally
            {
                ModLog.Flush();
            }
        }
    }
}
