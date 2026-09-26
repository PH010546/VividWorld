using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Localization;
using VividWorld.Campaign;
using VividWorld.Core.Channels;
using VividWorld.Core.Config;
using VividWorld.Core.Dialogue;
using VividWorld.Core.Events;
using VividWorld.Core.Memory;
using VividWorld.Core.Persistence;
using VividWorld.Core.Presentation;
using VividWorld.Core.Rumors;
using VividWorld.Presentation;

namespace VividWorld.Dialogue
{
    internal sealed class RumorDialogBehavior : CampaignBehaviorBase
    {
        private readonly VividWorldConfig _config;
        private RumorOfferSelector? _offerSelector;
        private EventShardStore? _store;
        private RumorIndex? _index;
        private KnownByIndex? _knownBy;
        private GameTraitLookup? _traitLookup;
        private HeroLookup? _heroLookup;
        private MemoryStamper? _stamper;
        private string? _campaignId;
        private bool _ready;
        private PlayerHeardLogStore? _playerHeardLog;
        internal PlayerHeardLogStore? PlayerHeardLog => _playerHeardLog;

        private ListenTallyStore? _listenTallyStore;
        private ListenTally? _listenTally;
        internal ListenTally? ListenTally => _listenTally;

        // LISTEN1 每場對話追蹤
        private string? _currentConversationPartnerId;
        private string? _currentConversationPartnerName;
        private int _currentConversationRelation;
        private bool _currentConversationEligible;
        private bool _currentConversationPartnerKnown;
        private bool _currentConversationIsNoHero;

        private bool _volunteerCommonerBlocked;
        private bool _volunteerLordAttackBlocked;
        private VolunteerDecision? _volunteerDecision;
        private int _volunteerKnownCount;
        private int _volunteerForgottenCount;
        private int _volunteerOutdatedCount;
        private int _volunteerMissRivalCount = -1;
        private bool _volunteerToldDelivered;

        private bool _askAsked;
        private bool _askTold;
        private bool _askShown;
        private bool _askBlockedCommonerTier;
        private AskDecision? _cachedAskDecision;
        private int _cachedAskKnownCount;
        private int _cachedAskForgottenCount;
        private int _cachedAskOutdatedCount;

        private bool _recoveryShown;
        private bool _recoveryUsed;

        private readonly DailyCounter _volunteersCounter = new DailyCounter();
        private readonly Dictionary<string, double> _lastVolunteeredDays = new Dictionary<string, double>(StringComparer.Ordinal);
        private string? _lastVolunteerDesc;
        private string? _lastVolunteerSessionInfo;

        // 記憶化快取（規格 §9.2）
        private string? _cachedHeroId;
        private RumorOffer? _cachedOffer;
        private WorldEvent? _cachedEvent;
        private bool _cachedHasOffer;
        private bool _cacheValid;

        // 玩家真正看到的那一句。渲染在 condition（一幀可能跑好幾次），
        // 寫入 log 在 consequence（真的講出口才一次）。少了這一行，「他到底說了什麼」
        // 只能靠他瞄對話框回報，文字有殘缺也檢查不了。
        // M6c：診斷輸出必須是純文字版本（不含任何百科 HTML 錨點）。
        private string? _renderedVolunteerText;
        private string? _renderedVolunteerTextPlain;
        private string? _renderedAskText;
        private string? _renderedAskTextPlain;

        // 規格 §9.2.3（M6c）：這場對話是否已送出過傳聞（主動或補救）。
        private bool _deliveredVolunteerThisConversation;
        private bool _recoveryOfferedLogged;

        private string? _cachedVolunteerHeroId;
        private RumorOffer? _cachedVolunteerOffer;
        private WorldEvent? _cachedVolunteerEvent;
        private bool _cachedHasVolunteerOffer;
        private bool _volunteerCacheValid;

        // 「我們那條台詞這場對話有沒有被評估過」（帳本 L-22）。
        // D-43：同一個 token 上只挑一句、按優先權遞減，挑到第一條 condition 為真的就 return
        // ⇒ 被壓過的行連 condition 都不會被呼叫，什麼都不會寫進 log。M6b 第一輪實機就是這樣
        // 整場沒有半行 Volunteer——而「沒有日誌」在當下看起來跟「條件為假」一模一樣。
        // 這兩個旗標讓那個狀態自己講出來。
        private bool _volunteerConditionRan;
        private bool _volunteerMissReported;
        private bool _tokenContentionReported;
        private bool _commonerGateReported;
        private bool _volunteerPassedNoticeLogged;

        // 這場對話有沒有走到 `start`。跟上面那一組一樣，單位是一場對話。
        private bool _startTokenReached;

        // 這場對話真正被引擎挑中的每一行，依發生順序（帳本 D-50／L-25／X-19）。
        // 探針只答得出「`start` 走到了沒」，答不出「誰在 `start` 上贏了、把對話帶去哪裡」——
        // 掃描清單列的是**有資格**的 198 條，不是贏家。這一份才是贏家名單。
        private readonly List<RouteEntry> _chosenThisConversation = new List<RouteEntry>();
        private List<string>? _lastConversationRoute;
        private bool _consequenceHookInstalled;
        private const int MaxRouteEntries = 24;

        // 「平民不該跟貴族攀談」相容模式（規格 §9.2.1）。註冊時解一次；只有傳聞模式那一部分
        // 會在選單改了之後重算（SyncRumorModeWithConfig，LISTEN1e）。
        private CommonerCompatState _compat = new CommonerCompatState { Reason = "not resolved yet (dialogues not registered)" };
        public CommonerCompatState CompatState => _compat;

        /// <summary>
        /// 選單改了傳聞模式就在這裡接上，不必重新讀檔（LISTEN1e，feature spec §13）。
        /// 主動講、補救、玩家問三個條件，以及預演與 dev 狀態行的開頭都先叫它；沒改就只比一次字串。
        /// 只重算模式與「問」的氏族閘——優先權在對話行註冊時就定了，彈窗與訊息只在讀檔時。
        /// </summary>
        private void SyncRumorModeWithConfig()
        {
            try
            {
                // 對話行還沒註冊（沒算過）時由 RegisterDialogues 負責，不在這裡搶先算。
                if (_offerSelector == null || _compat.RumorModeConfiguredAs == null) return;
                var d = _config.Dialogue;
                if (!CommonerCompat.IsRumorModeStale(_compat, d)) return;

                string before = _compat.RumorModeConfiguredAs;
                CommonerCompat.ApplyRumorMode(_compat, d);
                _offerSelector.Mode = _compat.RumorMode;

                ModLog.Info($"Rumor mode: changed in settings '{before}' -> '{d.VolunteerMode}', applied now without reload");
                if (_compat.RumorModeResult != null)
                {
                    ModLog.Info(RumorModeResolver.FormatModeLine(_compat.RumorModeResult, _offerSelector.ChatRelationGate,
                        d.NpcVolunteerRelationGate, d.GistExtraHops, _compat.AskMinClanTier));
                }
                ModLog.Flush();
            }
            catch (Exception ex)
            {
                ModLog.Error("Error syncing rumor mode with config", ex);
            }
        }

        /// <summary>dev「世界現況」用：設定解成了什麼 ➕ 玩家氏族現在的 Tier ➕ 現在擋不擋。</summary>
        public string CompatStatusLine
        {
            get
            {
                SyncRumorModeWithConfig();
                string baseLine = CommonerCompat.FormatRegistration(
                    _compat, _config.Dialogue.NpcLineInputToken, _config.Dialogue.NpcLinePriority);
                int tier = PlayerClanTier();
                string tierStr = tier < 0 ? "none" : tier.ToString();
                bool askBlocked = CommonerCompat.BlocksAsk(_compat, tier);
                bool volBlocked = CommonerCompat.BlocksVolunteer(_compat, tier);
                return $"{baseLine} Player clan tier now: {tierStr} => asking {(askBlocked ? "BLOCKED" : "allowed")}, " +
                       $"lords volunteering {(volBlocked ? "BLOCKED" : "allowed")}.";
            }
        }

        public int VolunteersToday
        {
            get
            {
                _volunteersCounter.Advance(CampaignTime.Now.ToDays);
                return _volunteersCounter.Count;
            }
        }

        public int MaxVolunteersPerDay => _config.Dialogue.MaxVolunteersPerDay;

        public string LastVolunteerSessionInfo => _lastVolunteerSessionInfo ?? "(none this session)";

        /// <summary>dev「世界現況」用：這場對話有沒有補救選項待用（規格 §9.2.3，M6c）。</summary>
        public string VolunteerRecoveryStatusLine
        {
            get
            {
                var hero = Hero.OneToOneConversationHero;
                if (hero == null) return "(no active conversation partner)";
                if (_volunteerConditionRan) return "not needed (greeting state was evaluated)";
                if (_deliveredVolunteerThisConversation) return "delivered (rumor already told this conversation)";
                string token = _config.Dialogue.NpcLineInputToken;
                string? culprit = FirstLineOffStart(token);
                if (culprit == null) return "not active (start was not routed past greeting)";
                if (_cachedHasVolunteerOffer && _cachedVolunteerOffer != null)
                {
                    return $"available (offer {_cachedVolunteerOffer.EventId} from {hero.Name})";
                }
                return "none (no eligible volunteer offer for partner)";
            }
        }

        public RumorDialogBehavior(VividWorldConfig config)
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));
        }

        public void Initialize(
            RumorOfferSelector offerSelector,
            EventShardStore store,
            RumorIndex index,
            KnownByIndex knownBy,
            GameTraitLookup traitLookup,
            HeroLookup heroLookup,
            string? campaignId = null,
            MemoryStamper? stamper = null,
            PlayerHeardLogStore? playerHeardLog = null)
        {
            _offerSelector = offerSelector ?? throw new ArgumentNullException(nameof(offerSelector));
            _store = store ?? throw new ArgumentNullException(nameof(store));
            _index = index ?? throw new ArgumentNullException(nameof(index));
            _knownBy = knownBy ?? throw new ArgumentNullException(nameof(knownBy));
            _traitLookup = traitLookup ?? throw new ArgumentNullException(nameof(traitLookup));
            _heroLookup = heroLookup ?? throw new ArgumentNullException(nameof(heroLookup));
            _campaignId = campaignId;
            _stamper = stamper;
            _playerHeardLog = playerHeardLog;
            LoadVolunteers();
            if (!string.IsNullOrEmpty(_campaignId))
            {
                _listenTallyStore = new ListenTallyStore(VividWorldPaths.ListenTallyFile(_campaignId!));
                if (_config.Debug.ListenTally)
                {
                    _listenTally = _listenTallyStore.Load();
                    if (_listenTallyStore.LastLoadError != null)
                    {
                        ModLog.Warn($"ListenTally: could not read {_listenTallyStore.FilePath} ({_listenTallyStore.LastLoadError}) - starting from 0; the next save will overwrite it.");
                    }
                    else
                    {
                        ModLog.Info($"ListenTally: loaded {_listenTally.Days.Count} day(s) from {_listenTallyStore.FilePath}.");
                    }
                }
            }
            _ready = true;
        }

        public void SaveListenTally()
        {
            if (!_config.Debug.ListenTally || _listenTally == null || _listenTallyStore == null)
            {
                return;
            }

            try
            {
                bool ok = _listenTallyStore.Save(_listenTally);
                if (!ok)
                {
                    ModLog.Warn("ListenTally: failed to save listen_tally.json.");
                }
            }
            catch (Exception ex)
            {
                ModLog.Error("Error saving ListenTally", ex);
            }
        }

        public override void RegisterEvents()
        {
            CampaignEvents.ConversationEnded.AddNonSerializedListener(this, OnConversationEnded);
            ModLog.Info("RumorDialogBehavior.RegisterEvents: subscribed to ConversationEnded.");
        }

        public override void SyncData(IDataStore dataStore)
        {
        }

        private void OnConversationEnded(IEnumerable<CharacterObject> characters)
        {
            if (!_ready)
            {
                ModLog.Warn("RumorDialogBehavior: conversation ended before Initialize - skipped.");
                return;
            }

            try
            {
                RecordConversationTally(characters);
                InvalidateOfferCache();
                ResetConversationDiagnostics();
            }
            catch (Exception ex)
            {
                ModLog.Error("Error in RumorDialogBehavior.OnConversationEnded", ex);
            }
            finally
            {
                ModLog.Flush();
            }
        }

        /// <summary>
        /// 外部（dev 工具）把世界改掉了，提案要重算。
        /// **不碰診斷旗標**：那幾個旗標的單位是「一場對話」，不是「一次快取失效」。
        /// 兩者綁在一起的後果看帳本 L-24：對話中按一下 dev 的好感度加成，
        /// 旗標被清掉，下一次跑到 hero_main_options 就變成「我們那條沒被評估」的誤報。
        /// </summary>
        public void InvalidateCache()
        {
            // 主動講那條掛在 lord_start（招呼狀態）。這場對話已經走過那一步之後，
            // 重算出來的新提案這場不可能再有機會播出來——引擎不會回到 lord_start。
            // 不寫這一行，「我剛把好感刷到 41 了他怎麼還不講」在 log 裡答不出來。
            if (_volunteerConditionRan && !_volunteerPassedNoticeLogged)
            {
                _volunteerPassedNoticeLogged = true;
                ModLog.Info(
                    $"Volunteer: offer cache invalidated mid-conversation, but the '{_config.Dialogue.NpcLineInputToken}' " +
                    "greeting state has already been passed in this conversation - a recomputed offer can only be " +
                    "spoken the next time a conversation is started.");
                ModLog.Flush();
            }

            InvalidateOfferCache();
        }

        /// <summary>只丟提案快取，診斷旗標不動。</summary>
        private void InvalidateOfferCache()
        {
            _cacheValid = false;
            _cachedHeroId = null;
            _cachedOffer = null;
            _cachedEvent = null;
            _cachedHasOffer = false;

            _volunteerCacheValid = false;
            _cachedVolunteerHeroId = null;
            _cachedVolunteerOffer = null;
            _cachedVolunteerEvent = null;
            _cachedHasVolunteerOffer = false;

            _renderedVolunteerText = null;
            _renderedVolunteerTextPlain = null;
            _renderedAskText = null;
            _renderedAskTextPlain = null;
        }

        /// <summary>
        /// 診斷旗標歸零。單位是一場對話，所以只有 ConversationEnded 該呼叫它。
        /// （`_tokenContentionReported` 不在內：同一個 token 上有誰是一整個 session 不變的事。）
        /// </summary>
        private void ResetConversationDiagnostics()
        {
            _volunteerConditionRan = false;
            _volunteerMissReported = false;
            _commonerGateReported = false;
            _volunteerPassedNoticeLogged = false;
            _startTokenReached = false;
            _deliveredVolunteerThisConversation = false;
            _recoveryOfferedLogged = false;

            _currentConversationPartnerId = null;
            _currentConversationPartnerName = null;
            _currentConversationRelation = 0;
            _currentConversationEligible = false;
            _currentConversationPartnerKnown = false;
            _currentConversationIsNoHero = false;
            _volunteerCommonerBlocked = false;
            _volunteerLordAttackBlocked = false;
            _volunteerDecision = null;
            _volunteerKnownCount = 0;
            _volunteerForgottenCount = 0;
            _volunteerOutdatedCount = 0;
            _volunteerMissRivalCount = -1;
            _volunteerToldDelivered = false;

            _askAsked = false;
            _askTold = false;
            _askShown = false;
            _askBlockedCommonerTier = false;
            _cachedAskDecision = null;
            _cachedAskKnownCount = 0;
            _cachedAskForgottenCount = 0;
            _cachedAskOutdatedCount = 0;

            _recoveryShown = false;
            _recoveryUsed = false;

            // 路線先留一份給 dev 工具，再清空給下一場用。
            if (_chosenThisConversation.Count > 0)
            {
                _lastConversationRoute = _chosenThisConversation.Select(x => x.ToString()).ToList();
            }
            _chosenThisConversation.Clear();

            // 讀新戰役時 ConversationManager 會換一個實例，掛在舊實例上的訂閱就跟著失效。
            EnsureConsequenceHook();
        }

        private void RecordPartnerInfo(Hero? hero)
        {
            if (hero == null) return;
            if (_currentConversationPartnerKnown) return;

            _currentConversationPartnerId = hero.StringId;
            _currentConversationPartnerName = hero.Name?.ToString() ?? hero.StringId;
            _currentConversationRelation = Hero.MainHero != null ? (int)hero.GetRelation(Hero.MainHero) : 0;
            _currentConversationEligible = _traitLookup != null && Eligibility.IsEligible(_traitLookup, hero.StringId);
            _currentConversationIsNoHero = false;
            _currentConversationPartnerKnown = true;
        }

        private void RecordNonHeroPartnerInfo(CharacterObject character)
        {
            if (character == null) return;
            if (_currentConversationPartnerKnown) return;

            _currentConversationPartnerId = character.StringId ?? "unknown";
            _currentConversationPartnerName = character.Name?.ToString() ?? character.StringId ?? "unknown";
            _currentConversationRelation = 0;
            _currentConversationEligible = false;
            _currentConversationIsNoHero = true;
            _currentConversationPartnerKnown = true;
        }

        private void RecordConversationTally(IEnumerable<CharacterObject>? characters)
        {
            if (!_currentConversationPartnerKnown)
            {
                var hero = Hero.OneToOneConversationHero;
                if (hero == null && characters != null)
                {
                    hero = characters.Select(c => c.HeroObject).FirstOrDefault(h => h != null && h != Hero.MainHero);
                }
                if (hero != null)
                {
                    RecordPartnerInfo(hero);
                }
                else
                {
                    CharacterObject? character = CharacterObject.OneToOneConversationCharacter;
                    if (character == null && characters != null)
                    {
                        character = characters.FirstOrDefault(c => c != CharacterObject.PlayerCharacter);
                    }
                    if (character != null)
                    {
                        RecordNonHeroPartnerInfo(character);
                    }
                    else
                    {
                        _currentConversationEligible = false;
                        _currentConversationIsNoHero = true;
                        _currentConversationPartnerId = "unknown";
                        _currentConversationPartnerName = "unknown";
                        _currentConversationPartnerKnown = true;
                    }
                }
            }

            if (!_currentConversationEligible)
            {
                string reason;
                string tallyKey;
                if (_currentConversationIsNoHero)
                {
                    reason = "not a hero";
                    tallyKey = ListenTallyKeys.NotInNetworkNoHero;
                }
                else
                {
                    var h = _heroLookup?.Get(_currentConversationPartnerId ?? string.Empty);
                    reason = EligibilityLabel.GetRejectionReason(h, _traitLookup) ?? "ineligible";
                    tallyKey = ListenTallyKeys.NotInNetwork;
                }

                string notInNetworkMsg = $"Listen: {_currentConversationPartnerName} ({_currentConversationPartnerId}) not in network - {reason}";
                if (_config.Debug.ListenTally)
                {
                    notInNetworkMsg += $" | tally: {tallyKey}";
                }
                ModLog.Info(notInNetworkMsg);
            }

            if (_listenTally == null || !_config.Debug.ListenTally) return;

            int day = (int)CampaignTime.Now.ToDays;
            var dayTally = _listenTally.GetOrCreateDay(day);

            int remembered = Math.Max(0, _volunteerKnownCount - _volunteerForgottenCount - _volunteerOutdatedCount);
            dayTally.RecordPartner(_currentConversationPartnerId, _currentConversationRelation, _volunteerKnownCount, remembered);

            int rivalCount = _volunteerMissRivalCount;
            if (!_volunteerConditionRan && rivalCount < 0 && _currentConversationEligible)
            {
                var scan = ScanTokenContention();
                rivalCount = scan.RivalCount;
            }

            string volunteerKey = ListenTallyClassifier.ClassifyVolunteer(
                _currentConversationEligible,
                _volunteerConditionRan,
                rivalCount,
                _volunteerCommonerBlocked,
                _volunteerLordAttackBlocked,
                _volunteerDecision,
                _volunteerKnownCount,
                _volunteerForgottenCount,
                _volunteerOutdatedCount,
                _volunteerToldDelivered);

            dayTally.Increment(volunteerKey);
            if (string.Equals(volunteerKey, ListenTallyKeys.NotInNetwork, StringComparison.Ordinal) && _currentConversationIsNoHero)
            {
                dayTally.Increment(ListenTallyKeys.NotInNetworkNoHero);
            }

            if (string.Equals(volunteerKey, ListenTallyKeys.VolunteerFiltered, StringComparison.Ordinal) && _volunteerDecision != null)
            {
                if (_volunteerDecision.FilteredNotVisible > 0)
                {
                    dayTally.Increment(ListenTallyKeys.VolunteerFilteredSecret, _volunteerDecision.FilteredNotVisible);
                }
                if (_volunteerDecision.FilteredFutureTimeline > 0)
                {
                    dayTally.Increment(ListenTallyKeys.VolunteerFilteredFuture, _volunteerDecision.FilteredFutureTimeline);
                }
                if (_volunteerDecision.FilteredPlayerKnows > 0)
                {
                    dayTally.Increment(ListenTallyKeys.VolunteerFilteredPlayerKnows, _volunteerDecision.FilteredPlayerKnows);
                }
                if (_volunteerDecision.FilteredOther > 0)
                {
                    dayTally.Increment(ListenTallyKeys.VolunteerFilteredOther, _volunteerDecision.FilteredOther);
                }
            }

            if (_askAsked)
            {
                dayTally.Increment(ListenTallyKeys.AskAsked);
                if (_askTold)
                {
                    dayTally.Increment(ListenTallyKeys.AskTold);
                }
                else if (_cachedAskDecision != null)
                {
                    string askKey = ListenTallyClassifier.ClassifyAsk(
                        true, false, _cachedAskDecision, _cachedAskKnownCount, _cachedAskForgottenCount, _cachedAskOutdatedCount);
                    if (!string.IsNullOrEmpty(askKey) && !string.Equals(askKey, ListenTallyKeys.AskTold, StringComparison.Ordinal))
                    {
                        dayTally.Increment(askKey);
                    }
                }
            }

            if (_askShown)
            {
                dayTally.Increment(ListenTallyKeys.AskShown);
            }
            if (_askBlockedCommonerTier)
            {
                dayTally.Increment(ListenTallyKeys.AskBlockedCommonerTier);
            }

            if (_recoveryShown)
            {
                dayTally.Increment(ListenTallyKeys.RecoveryShown);
            }
            if (_recoveryUsed)
            {
                dayTally.Increment(ListenTallyKeys.RecoveryUsed);
            }
        }

        public void RegisterDialogues(CampaignGameStarter starter)
        {
            if (starter == null) return;

            var d = _config.Dialogue;

            // 哪一套優先權要看他實際裝了什麼（規格 §9.2.1，帳本 D-43／X-17／L-22）。
            // 用「執行期真的載進來的組件」而不是 Modules\ 資料夾：資料夾在、啟動器沒勾選是常態。
            _compat = CommonerCompat.Resolve(d, LoadedAssemblyNames());
            if (_offerSelector != null)
            {
                _offerSelector.Mode = _compat.RumorMode;
            }
            ModLog.Info(CommonerCompat.FormatRegistration(_compat, d.NpcLineInputToken, d.NpcLinePriority));

            // ── §5.1 NPC 主動開口講傳聞（M6b）──
            starter.AddDialogLine(
                "vividworld_npc_rumor",
                d.NpcLineInputToken,        // "lord_start"
                "hero_main_options",        // 直接回主選單，不開中間狀態
                "{=!}{VIVIDWORLD_RUMOR}",
                NpcVolunteersCondition,
                OnRumorDelivered,
                _compat.NpcLinePriority,    // 原版 105；相容模式 155（壓過 NaN 的 150）
                null);

            // ── §7 玩家詢問的對話 ──
            starter.AddPlayerLine(
                "vividworld_ask_news",
                d.PlayerLineInputToken,
                "vividworld_ask_answer",
                "{=VividWorld_AskNews}Any news on the road?",
                PlayerCanAskCondition,
                null,
                d.PlayerLinePriority,
                null);

            starter.AddDialogLine(
                "vividworld_ask_tell",
                "vividworld_ask_answer",
                "hero_main_options",
                "{=!}{VIVIDWORLD_RUMOR}",
                HasAskOfferCondition,
                OnAskAnswered,
                110,
                null);

            // ── §11 問傳聞被拒時，依原因回不同的話（LISTEN1c，決策 0053）──
            starter.AddDialogLine(
                "vividworld_ask_refuse",
                "vividworld_ask_answer",
                "hero_main_options",
                "{=!}{VIVIDWORLD_ASK_REFUSAL}",
                HasAskRefusalLineCondition,
                OnAskRefused,
                105,
                null);

            // vividworld_ask_nothing 的 condition 必須是 null（硬性禁令）。
            // consequence 不在禁令內，而且非有不可：見 OnAskNothing。
            starter.AddDialogLine(
                "vividworld_ask_nothing",
                "vividworld_ask_answer",
                "hero_main_options",
                "{=VividWorld_AskNothing}Nothing worth repeating.",
                null,
                OnAskNothing,
                100,
                null);

            // ── §9.2.3 被繞開時的補救選項（M6c）──
            starter.AddPlayerLine(
                "vividworld_recovery_ask",
                "hero_main_options",
                "vividworld_volunteer_recovery",
                "{=VividWorld_RecoveryAsk}You looked like you were about to say something.",
                RecoveryAvailableCondition,
                null,
                d.PlayerLinePriority,
                null);

            starter.AddDialogLine(
                "vividworld_recovery_tell",
                "vividworld_volunteer_recovery",
                "hero_main_options",
                "{=!}{VIVIDWORLD_RUMOR}",
                null,
                OnRecoveryAnswered,
                110,
                null);

            // ── 診斷探針（帳本 L-25）──
            // 問題：我們那條掛在 `lord_start`，而 `lord_start` 不是每一場對話都會走到的。
            // 走不到的時候，log 看起來跟「條件為假」一模一樣，而 `lord_start` 上的優先權掃描
            // 又會說「沒人壓過我們」——兩邊都正常，卻就是沒輪到我們。
            // 這條探針的 condition **永遠回 false**，永遠不會被選中，只是讓我們知道
            // `start` 有沒有被走到；配上 `lord_start` 那邊的旗標，就分得出
            // 「根本沒開對話」、「走了 start 但被人從 start 直接帶到別處」、「走到 lord_start 但被壓過」。
            // 跟著開發者對話開關走，關掉就完全不碰 `start`。
            if (_config.Debug.DebugDialogueEnabled)
            {
                starter.AddDialogLine(
                    StartProbeLineId,
                    "start",
                    "hero_main_options",   // 永遠用不到：condition 恒為 false
                    "{=!} ",
                    StartTokenProbeCondition,
                    null,
                    StartProbePriority,
                    null);
            }

            EnsureConsequenceHook();

            ModLog.Info($"Registered rumor dialogue lines: volunteer on {d.NpcLineInputToken} (priority {_compat.NpcLinePriority}), ask on {d.PlayerLineInputToken} (priority {d.PlayerLinePriority})."
                + (_config.Debug.DebugDialogueEnabled
                    ? $" Start-token probe on 'start' (priority {StartProbePriority}, never selectable)."
                    : " Start-token probe off (debugDialogueEnabled = false)."));
        }

        private const string StartProbeLineId = "vividworld_start_probe";
        private const int StartProbePriority = 1000;

        /// <summary>
        /// 永遠回 <c>false</c>。優先權比它低的行一條都不會被影響——
        /// 引擎只是多評估一次條件，然後繼續往下找。
        /// </summary>
        private bool StartTokenProbeCondition()
        {
            _startTokenReached = true;
            return false;
        }

        // ── 對話路線追蹤（帳本 D-50）──
        // `ProcessSentence` 每挑中一句就 `ActiveToken = sentence.OutputToken` 再 `RunConsequence()`，
        // 而 `RunConsequence` 無條件轉呼 `ConversationManager.OnConsequence(this)` ⇒
        // `ConsequenceRunned` 收得到**每一句真正被選中的行**，包含開場那一句。
        // 這是探針答不出來的那一半：探針只說「`start` 走到了」，這裡說「是誰在 `start` 上贏了」。

        private object? _hookedManager;

        private void EnsureConsequenceHook()
        {
            try
            {
                var manager = TaleWorlds.CampaignSystem.Campaign.Current?.ConversationManager;
                if (manager == null) return;
                if (_consequenceHookInstalled && ReferenceEquals(_hookedManager, manager)) return;

                var field = manager.GetType().GetField(
                    "ConsequenceRunned",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                if (field == null)
                {
                    ModLog.Warn("Conversation route: ConversationManager.ConsequenceRunned not found (game update?) - " +
                                "the route trace stays off; the token contention list is all we will have.");
                    _consequenceHookInstalled = true;
                    _hookedManager = manager;
                    return;
                }

                var mine = new Action<TaleWorlds.CampaignSystem.Conversation.ConversationSentence>(OnSentenceChosen);
                var existing = field.GetValue(manager) as Delegate;
                field.SetValue(manager, Delegate.Combine(existing, mine));

                _consequenceHookInstalled = true;
                _hookedManager = manager;
                ModLog.Info("Conversation route: subscribed to ConversationManager.ConsequenceRunned - "
                            + "every line the engine actually picks is now recorded for the current conversation.");
            }
            catch (Exception ex)
            {
                ModLog.Error("Error installing the conversation route hook", ex);
                _consequenceHookInstalled = true;
            }
        }

        private void OnSentenceChosen(TaleWorlds.CampaignSystem.Conversation.ConversationSentence sentence)
        {
            // 診斷不得把對話弄掛：這裡吞掉一切。
            try
            {
                if (sentence == null || _chosenThisConversation.Count >= MaxRouteEntries) return;
                var names = TokenNames();
                _chosenThisConversation.Add(new RouteEntry(
                    string.IsNullOrEmpty(sentence.Id) ? "(no id)" : sentence.Id,
                    sentence.Priority,
                    TokenName(names, sentence.InputToken),
                    TokenName(names, sentence.OutputToken),
                    OwnerOf(sentence)));
            }
            catch
            {
            }
        }

        /// <summary>路線上的一句。字串化留到要印的時候，判斷用的是欄位。</summary>
        private sealed class RouteEntry
        {
            public readonly string Id;
            public readonly int Priority;
            public readonly string InputToken;
            public readonly string OutputToken;
            public readonly string Owner;

            public RouteEntry(string id, int priority, string inputToken, string outputToken, string owner)
            {
                Id = id;
                Priority = priority;
                InputToken = inputToken;
                OutputToken = outputToken;
                Owner = owner;
            }

            public override string ToString()
            {
                return $"{Id} (prio {Priority}) {InputToken} -> {OutputToken} [{Owner}]";
            }
        }

        /// <summary>
        /// 這場對話在 `start` 上贏的那一句，**而且它沒有把對話帶去我們要的 token**。
        /// 找不到就回 null（例如路線沒錄到、或 `start` 上贏的那條其實走的是 `lord_start`）。
        /// </summary>
        private string? FirstLineOffStart(string ourToken)
        {
            foreach (var e in _chosenThisConversation)
            {
                if (!string.Equals(e.InputToken, "start", StringComparison.Ordinal)) continue;
                if (string.Equals(e.OutputToken, ourToken, StringComparison.Ordinal)) continue;
                return $"'{e.Id}' (priority {e.Priority}, {e.Owner}) which sent 'start' -> '{e.OutputToken}'";
            }
            return null;
        }

        private static string OwnerOf(TaleWorlds.CampaignSystem.Conversation.ConversationSentence s)
        {
            Delegate? d = s.OnCondition;
            if (d == null) d = s.OnConsequence;
            if (d == null) return "unconditional";
            var m = d.Method;
            return (m.DeclaringType != null ? m.DeclaringType.FullName + "." : string.Empty) + m.Name;
        }

        /// <summary>
        /// token 的 int 是引擎內連出來的 id，行本身不留字串（D-50）。
        /// 唯一的對照表是 `ConversationManager.stateMap`，反轉它才印得出 `hero_main_options` 這種名字。
        /// </summary>
        private static Dictionary<int, string>? _tokenNameCache;
        private static int _tokenNameCacheSize = -1;

        private static Dictionary<int, string> TokenNames()
        {
            try
            {
                var manager = TaleWorlds.CampaignSystem.Campaign.Current?.ConversationManager;
                if (manager == null) return _tokenNameCache ?? new Dictionary<int, string>();

                var field = manager.GetType().GetField(
                    "stateMap",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                if (!(field?.GetValue(manager) is Dictionary<string, int> map))
                {
                    return _tokenNameCache ?? new Dictionary<int, string>();
                }

                if (_tokenNameCache != null && _tokenNameCacheSize == map.Count) return _tokenNameCache;

                var flipped = new Dictionary<int, string>(map.Count);
                foreach (var kvp in map) flipped[kvp.Value] = kvp.Key;
                _tokenNameCache = flipped;
                _tokenNameCacheSize = map.Count;
                return flipped;
            }
            catch
            {
                return _tokenNameCache ?? new Dictionary<int, string>();
            }
        }

        private static string TokenName(Dictionary<int, string> names, int token)
        {
            return names.TryGetValue(token, out string? n) ? n : "token#" + token;
        }

        /// <summary>上一場對話走過的路線，給 dev 工具「世界現況」讀。</summary>
        internal IReadOnlyList<string> LastConversationRoute
        {
            get { return (IReadOnlyList<string>?)_lastConversationRoute ?? Array.Empty<string>(); }
        }

        // ── NPC 主動開口委派（M6b）──

        private bool NpcVolunteersCondition()
        {
            try
            {
                // 引擎真的把這條拿出來評估了。在每一道早退之前記，
                // 才區別得出「被壓過」與「評估了但不講」（帳本 L-22）。
                _volunteerConditionRan = true;
                SyncRumorModeWithConfig();

                // 1. _ready 且所有相依都已 Initialize → 否則 false（不記日誌，這是啟動期）
                if (!_ready || _offerSelector == null || _store == null || _index == null ||
                    _knownBy == null || _traitLookup == null || _heroLookup == null)
                {
                    return false;
                }

                // 2. Hero.OneToOneConversationHero 非 null 且 IsAlive → 否則 false
                var hero = Hero.OneToOneConversationHero;
                if (hero == null || !hero.IsAlive)
                {
                    return false;
                }
                RecordPartnerInfo(hero);

                // 3. 對方過 Eligibility.IsEligible → 否則 false
                if (!Eligibility.IsEligible(_traitLookup, hero.StringId))
                {
                    return false;
                }

                // 4. 記憶化快取（§9.2）：同一位對象只算一次。
                //    WillLordAttack 的守衛（D-48）與三個閘的判定都在裡面，日誌因此是一場對話一行。
                // 5. 有提案 → SetTextVariable("VIVIDWORLD_RUMOR", …) 並回傳 true
                EnsureVolunteerOfferCached(hero);

                if (_cachedHasVolunteerOffer && _cachedVolunteerOffer != null)
                {
                    var renderResult = FallbackTextRenderer.RenderBoth(_cachedVolunteerOffer.Composed, _config.Presentation);
                    if (string.IsNullOrWhiteSpace(renderResult.DisplayText))
                    {
                        // 每一個碎片都被 renderer 丟掉了（上面那一行 WARN 會說是為什麼）。
                        // 這時候回 true 等於讓領主張嘴卻一個字也沒說。
                        ModLog.Warn($"Volunteer: offer {_cachedVolunteerOffer.EventId} rendered to an empty string - not offered.");
                        return false;
                    }
                    _renderedVolunteerText = renderResult.DisplayText;
                    _renderedVolunteerTextPlain = renderResult.PlainText;
                    MBTextManager.SetTextVariable("VIVIDWORLD_RUMOR", renderResult.DisplayText, false);
                    return true;
                }

                // 判定不講時**不要**讓快取失效。這條路徑和玩家詢問不一樣：
                // 玩家詢問是一次按鍵一次評估，主動講的 condition 由引擎在同一個 token 上
                // 自動評估、而且每幀可能好幾次（規格 §9.2 開頭）。在這裡呼叫 InvalidateCache()
                // 會讓「記憶化是強制的」整條失效 ⇒ 每評估一次就重算一次、多印一行。
                // 失效時機是 ConversationEnded（換人／離開）與 OnRumorDelivered（講完了），兩處都已經有。
                return false;
            }
            catch (Exception ex)
            {
                ModLog.Error("Error in NpcVolunteersCondition", ex);
                return false;
            }
            finally
            {
                // §4.1: condition 與 consequence 兩條路徑都要在結束前 ModLog.Flush()
                ModLog.Flush();
            }
        }

        private void ApplyOfferAndRecord(RumorOffer offer, WorldEvent evt, string tellerHeroId, double day, string tellerName)
        {
            if (_offerSelector == null || _store == null) return;

            _offerSelector.ApplyOffer(offer, evt, tellerHeroId, day);
            _store.Upsert(evt);

            // 反向索引也要跟上，否則要等下次載入 KnownByIndex 才看得到玩家知道這則
            // （(dev) 世界現況會說 known events: 0，同一份日誌的 "player already knows" 卻說相反的話）。
            string? playerHeroId = Hero.MainHero?.StringId;
            if (!string.IsNullOrEmpty(playerHeroId))
            {
                _knownBy?.NoteKnower(playerHeroId!, offer.EventId, evt.Day);
            }

            if (_playerHeardLog != null && !string.IsNullOrEmpty(playerHeroId))
            {
                var playerEntry = evt.EntryFor(playerHeroId!);
                if (playerEntry != null)
                {
                    bool isUpdate = _playerHeardLog.Contains(offer.EventId);
                    var facts = PlayerKnownFacts.Of(evt, playerEntry, _offerSelector.Engine);
                    bool recorded = _playerHeardLog.Record(evt, playerEntry, facts, day);
                    if (recorded)
                    {
                        ModLog.Info(PlayerHeardLogFormatter.FormatTold(
                            isUpdate,
                            offer.EventId,
                            playerEntry.Hop,
                            facts.Count,
                            tellerName));
                    }
                }
            }
        }

        private void OnRumorDelivered()
        {
            try
            {
                if (!_ready || _offerSelector == null || _store == null) return;

                if (_cachedVolunteerOffer != null && _cachedVolunteerEvent != null)
                {
                    var hero = Hero.OneToOneConversationHero;
                    if (hero != null)
                    {
                        double day = CampaignTime.Now.ToDays;
                        VolunteerRecoveryGate.ConsumeQuotaAndCooldown(_volunteersCounter, _lastVolunteeredDays, hero.StringId, day);

                        string tellerName = hero.Name?.ToString() ?? hero.StringId;
                        ApplyOfferAndRecord(_cachedVolunteerOffer, _cachedVolunteerEvent, hero.StringId, day, tellerName);

                        SaveVolunteers();

                        _lastVolunteerDesc = $"{tellerName} {_cachedVolunteerOffer.EventId}";
                        _lastVolunteerSessionInfo = $"{tellerName} ({hero.StringId}) told {_cachedVolunteerOffer.EventId} at day {day:F1}";

                        _deliveredVolunteerThisConversation = true;
                        _volunteerToldDelivered = true;

                        string tierStr = _volunteerDecision?.Tier == VolunteerTier.Gist ? "gist" : "full";
                        string deliveredMsg = $"Rumor delivered to player: event {_cachedVolunteerOffer.EventId} ({tierStr}, hop {_cachedVolunteerOffer.ResultingPlayerHop}, isRetell={_cachedVolunteerOffer.IsRetell}) from {hero.Name}";
                        if (_config.Debug.ListenTally)
                        {
                            deliveredMsg += $" | tally: {ListenTallyKeys.VolunteerTold}";
                        }
                        ModLog.Info(deliveredMsg);
                        ModLog.Info($"  text shown: \"{_renderedVolunteerTextPlain ?? _renderedVolunteerText}\"");
                    }
                }
            }
            catch (Exception ex)
            {
                ModLog.Error("Error in OnRumorDelivered", ex);
            }
            finally
            {
                InvalidateOfferCache();
                ModLog.Flush();
            }
        }

        // ── §9.2.3 被繞開時的補救選項（M6c）──

        private bool RecoveryAvailableCondition()
        {
            try
            {
                SyncRumorModeWithConfig();

                // 1. 基本相依檢查
                if (!_ready || _offerSelector == null || _store == null || _index == null ||
                    _knownBy == null || _traitLookup == null || _heroLookup == null)
                {
                    return false;
                }

                var hero = Hero.OneToOneConversationHero;
                if (hero == null || !hero.IsAlive) return false;
                RecordPartnerInfo(hero);

                if (!Eligibility.IsEligible(_traitLookup, hero.StringId)) return false;

                // 2. 這場對話還沒送出過那則傳聞
                if (_deliveredVolunteerThisConversation) return false;

                // 3. 這場對話沒有走到 lord_start——即確實被繞開
                string token = _config.Dialogue.NpcLineInputToken;
                bool reachedGreeting = _volunteerConditionRan || _chosenThisConversation.Any(e =>
                    string.Equals(e.InputToken, token, StringComparison.Ordinal) ||
                    string.Equals(e.OutputToken, token, StringComparison.Ordinal));
                if (reachedGreeting) return false;

                string? culprit = FirstLineOffStart(token);
                bool wasBypassed = !reachedGreeting && culprit != null;
                if (!wasBypassed) return false;

                // 1 & 4. 主動傳聞提案已算出且所有閘通過（沿用快取，不得重算，記憶化保證同一位對象只算一次）
                EnsureVolunteerOfferCached(hero);
                bool hasOffer = _cachedHasVolunteerOffer && _cachedVolunteerOffer != null && _cachedVolunteerEvent != null;

                if (!VolunteerRecoveryGate.IsAvailable(hasOffer, reachedGreeting, wasBypassed, _deliveredVolunteerThisConversation))
                {
                    return false;
                }

                var renderResult = FallbackTextRenderer.RenderBoth(_cachedVolunteerOffer!.Composed, _config.Presentation);
                if (string.IsNullOrWhiteSpace(renderResult.DisplayText))
                {
                    ModLog.Warn($"Volunteer recovery: offer {_cachedVolunteerOffer.EventId} rendered to an empty string - not offered.");
                    return false;
                }

                _renderedVolunteerText = renderResult.DisplayText;
                _renderedVolunteerTextPlain = renderResult.PlainText;
                MBTextManager.SetTextVariable("VIVIDWORLD_RUMOR", renderResult.DisplayText, false);

                if (!_recoveryOfferedLogged)
                {
                    _recoveryOfferedLogged = true;
                    _recoveryShown = true;
                    string recMsg = $"Volunteer recovery: offered {hero.Name} ({hero.StringId}) rumor {_cachedVolunteerOffer.EventId} - bypassed by {culprit ?? "unknown route"}";
                    if (_config.Debug.ListenTally)
                    {
                        recMsg += $" | tally: {ListenTallyKeys.RecoveryShown}";
                    }
                    ModLog.Info(recMsg);
                    ModLog.Flush();
                }

                return true;
            }
            catch (Exception ex)
            {
                ModLog.Error("Error in RecoveryAvailableCondition", ex);
                return false;
            }
            finally
            {
                ModLog.Flush();
            }
        }

        private void OnRecoveryAnswered()
        {
            try
            {
                if (!_ready || _offerSelector == null || _store == null) return;

                if (_cachedVolunteerOffer != null && _cachedVolunteerEvent != null)
                {
                    var hero = Hero.OneToOneConversationHero;
                    if (hero != null)
                    {
                        double day = CampaignTime.Now.ToDays;
                        VolunteerRecoveryGate.ConsumeQuotaAndCooldown(_volunteersCounter, _lastVolunteeredDays, hero.StringId, day);

                        string tellerName = hero.Name?.ToString() ?? hero.StringId;
                        ApplyOfferAndRecord(_cachedVolunteerOffer, _cachedVolunteerEvent, hero.StringId, day, tellerName);

                        SaveVolunteers();

                        _lastVolunteerDesc = $"{tellerName} {_cachedVolunteerOffer.EventId}";
                        _lastVolunteerSessionInfo = $"{tellerName} ({hero.StringId}) told {_cachedVolunteerOffer.EventId} at day {day:F1} (via recovery)";

                        _deliveredVolunteerThisConversation = true;
                        _recoveryUsed = true;

                        string tierStr = _volunteerDecision?.Tier == VolunteerTier.Gist ? "gist" : "full";
                        string recDeliveredMsg = $"Rumor delivered to player (via recovery): event {_cachedVolunteerOffer.EventId} ({tierStr}, hop {_cachedVolunteerOffer.ResultingPlayerHop}, isRetell={_cachedVolunteerOffer.IsRetell}) from {hero.Name}";
                        if (_config.Debug.ListenTally)
                        {
                            recDeliveredMsg += $" | tally: {ListenTallyKeys.RecoveryUsed}";
                        }
                        ModLog.Info(recDeliveredMsg);
                        ModLog.Info($"  text shown: \"{_renderedVolunteerTextPlain ?? _renderedVolunteerText}\"");
                    }
                }
            }
            catch (Exception ex)
            {
                ModLog.Error("Error in OnRecoveryAnswered", ex);
            }
            finally
            {
                InvalidateOfferCache();
                ModLog.Flush();
            }
        }

        private void EnsureVolunteerOfferCached(Hero hero)
        {
            if (_volunteerCacheValid && string.Equals(_cachedVolunteerHeroId, hero.StringId, StringComparison.Ordinal))
            {
                return;
            }

            _cachedVolunteerHeroId = hero.StringId;
            _volunteerCacheValid = true;
            _cachedVolunteerOffer = null;
            _cachedVolunteerEvent = null;
            _cachedHasVolunteerOffer = false;

            RecordPartnerInfo(hero);

            // 相容模式的氏族 Tier 閘（規格 §9.2.1）。放在記憶化之內，理由與下面那道守衛一樣：
            // 一場對話一行，不是一次評估一行。主動講這邊預設不擋（門檻 0）。
            int playerTier = PlayerClanTier();
            if (CommonerCompat.BlocksVolunteer(_compat, playerTier))
            {
                _volunteerCommonerBlocked = true;
                string blockMsg = CommonerCompat.FormatBlocked(
                    "Volunteer", hero.Name?.ToString() ?? hero.StringId, hero.StringId,
                    playerTier, _compat.VolunteerMinClanTier, _compat);
                if (_config.Debug.ListenTally)
                {
                    blockMsg += $" | tally: {ListenTallyKeys.VolunteerBlockedCommonerTier}";
                }
                ModLog.Info(blockMsg);
                return;
            }

            // 硬性守衛（帳本 D-48）：`lord_ask`（動手前的話）與 `lord_ask_3`（要求投降）的唯一前提
            // 就是這個述詞。呼叫同一個，我們就不可能與原生的判斷脫節。
            // 放在記憶化之內，是為了讓它跟其他理由一樣「一場對話一行」，而不是每次評估都印。
            if (Helpers.HeroHelper.WillLordAttack())
            {
                _volunteerLordAttackBlocked = true;
                string atkMsg = VolunteerDecision.FormatLordAboutToAttack(
                    hero.Name?.ToString() ?? hero.StringId, hero.StringId);
                if (_config.Debug.ListenTally)
                {
                    atkMsg += $" | tally: {ListenTallyKeys.VolunteerBlockedLordAttack}";
                }
                ModLog.Info(atkMsg);
                return;
            }

            double day = CampaignTime.Now.ToDays;
            _volunteersCounter.Advance(day);

            var profile = BuildSocialProfile(hero);
            var candidates = BuildCandidates(hero, day, out var forgottenEvents, out var outdatedEvents);

            var decision = _offerSelector!.DecideOnVolunteer(profile, candidates, day, _volunteersCounter.Count);

            if (decision.Offer != null)
            {
                _cachedVolunteerOffer = decision.Offer;
                _cachedVolunteerEvent = candidates.FirstOrDefault(c => c.Event.EventId == decision.Offer.EventId)?.Event;
                _cachedHasVolunteerOffer = true;
            }

            var knownEventIds = _knownBy!.EventsKnownBy(hero.StringId, day);
            int knownCount = knownEventIds?.Count ?? 0;

            _volunteerDecision = decision;
            _volunteerKnownCount = knownCount;
            _volunteerForgottenCount = forgottenEvents.Count;
            _volunteerOutdatedCount = outdatedEvents.Count;

            string logLine = VolunteerDecision.FormatLog(
                hero.Name?.ToString() ?? hero.StringId,
                hero.StringId,
                decision,
                knownCount,
                _lastVolunteerDesc,
                forgottenEvents.Count);

            if (_config.Debug.ListenTally)
            {
                // 選到消息時這一刻還沒講出口：真正落在哪一格要等講出口那一行（`tally: volunteer.told`），
                // 沒講出口就在對話結束時記成 chosenNotDelivered。這裡不能先印成 chosenNotDelivered。
                string tallyKey = decision.Offer != null
                    ? $"{ListenTallyKeys.VolunteerTold} when spoken"
                    : ListenTallyClassifier.ClassifyVolunteer(
                        true, true, 0, false, false, decision, knownCount, forgottenEvents.Count, outdatedEvents.Count, false);
                logLine += $" | tally: {tallyKey}";
            }

            ModLog.Info(logLine);

            if (forgottenEvents.Count > 0)
            {
                ModLog.Info(MemoryLogFormatter.FormatForgottenSummary(
                    hero.Name?.ToString() ?? hero.StringId,
                    hero.StringId,
                    forgottenEvents,
                    knownCount));
            }

            if (outdatedEvents.Count > 0)
            {
                ModLog.Info(MemoryLogFormatter.FormatOutdatedSummary(
                    hero.Name?.ToString() ?? hero.StringId,
                    hero.StringId,
                    outdatedEvents));
            }
        }

        // ── 玩家詢問委派 ──

        private bool PlayerCanAskCondition()
        {
            try
            {
                SyncRumorModeWithConfig();

                var hero = Hero.OneToOneConversationHero;
                if (hero == null || !hero.IsAlive) return false;
                RecordPartnerInfo(hero);

                // 相容模式：還沒成氏族的平民連問都不該問得出口（規格 §9.2.1）。
                // NaN 自己封鎖外交（`lord_politics_request`）與任務（`issue_offer`）的那一層就是 Tier == 0（X-17）。
                int playerTier = PlayerClanTier();
                if (CommonerCompat.BlocksAsk(_compat, playerTier))
                {
                    _askBlockedCommonerTier = true;
                    if (!_commonerGateReported)
                    {
                        _commonerGateReported = true;
                        ModLog.Info(CommonerCompat.FormatBlocked(
                            "Ask", hero.Name?.ToString() ?? hero.StringId, hero.StringId,
                            playerTier, _compat.AskMinClanTier, _compat));
                        ModLog.Flush();
                    }

                    // 詢問被擋不代表主動講也被擋（兩道閘分開），所以這條診斷不能跳過。
                    ReportVolunteerMissIfAny(hero);
                    return false;
                }

                _askShown = true;

                // 這裡是 `hero_main_options`，在對話鏈上一定晚於 `lord_start`。
                // 走到這一步而 NpcVolunteersCondition 連一次都沒被呼叫，就只有一種可能：
                // 同一個 token 上有優先權更高的行先通過了（D-43）。不記這一行，
                // 那個狀態在 log 裡與「條件為假」長得一模一樣（L-22）。
                ReportVolunteerMissIfAny(hero);

                return true;
            }
            catch (Exception ex)
            {
                ModLog.Error("Error in PlayerCanAskCondition", ex);
                return false;
            }
        }

        /// <summary>玩家氏族的 Tier；還沒有氏族就回 -1。與 NaN 讀的是同一個值（X-17）。</summary>
        private static int PlayerClanTier()
        {
            try
            {
                var clan = Clan.PlayerClan;
                return clan == null ? -1 : clan.Tier;
            }
            catch (Exception ex)
            {
                ModLog.Error("Error reading Clan.PlayerClan.Tier", ex);
                return -1;
            }
        }

        /// <summary>執行期真的載進來的組件名，餘者全部丟掉。</summary>
        private static List<string> LoadedAssemblyNames()
        {
            var names = new List<string>();
            try
            {
                foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    try
                    {
                        string? n = asm.GetName()?.Name;
                        if (!string.IsNullOrEmpty(n)) names.Add(n!);
                    }
                    catch
                    {
                        // 單一組件讀不到名字不應該拖垮整個偵測。
                    }
                }
            }
            catch (Exception ex)
            {
                ModLog.Error("Error enumerating loaded assemblies for commoner compat", ex);
            }
            return names;
        }

        /// <summary>
        /// 一場對話最多一行：主動講的 condition 這場沒被評估過。
        /// 只對進得了傳播網的對象（領主／流浪者）講——酒館老闆、鐵匠本來就不走 `lord_start`，
        /// 對他們報「沒被評估」是雜訊。
        /// </summary>
        private void ReportVolunteerMissIfAny(Hero hero)
        {
            if (_volunteerConditionRan || _volunteerMissReported) return;
            if (!_ready || _traitLookup == null) return;
            if (!Eligibility.IsEligible(_traitLookup, hero.StringId)) return;

            _volunteerMissReported = true;
            RecordPartnerInfo(hero);

            string tellerName = hero.Name?.ToString() ?? hero.StringId;
            string token = _config.Dialogue.NpcLineInputToken;
            int prio = _compat.NpcLinePriority;

            // 先掃一次再決定怎麼講。帳本 L-24：舊版不管掃出什麼都寫「被別人壓過」，
            // 結果下一行的掃描結果就是「nothing outranks」——同一時間戳的兩行互相矛盾。
            var scan = ScanTokenContention();
            _volunteerMissRivalCount = scan.RivalCount;
            string cause;
            if (scan.RivalCount > 0)
            {
                cause = $"another dialog line won token '{token}' before ours (ours: priority {prio}); " +
                        "no gate refused, our condition was never called";
            }
            else if (scan.RivalCount == 0)
            {
                // 沒人壓過我們，却還是沒輪到 ⇒ 這場對話根本沒走到 `lord_start`。
                // 探針說得出 `start` 有沒有被走到；路線追蹤說得出**是誰在 `start` 上贏了**（帳本 D-50／X-19）。
                // 順序很重要：**先問路線追蹤**。它不吃 `debugDialogueEnabled`（M6c 起一律訂閱），
                // 所以就算除錯開關關著也指名得出贏家。探針只是補一句「`start` 到底有沒有被走到」，
                // 那才是關掉開關後真正問不出來的部分。
                string? culprit = FirstLineOffStart(token);
                string reached;
                if (culprit != null)
                {
                    reached = $"'start' WAS reached, and the line that won it was {culprit} - that is why '{token}' never happened";
                }
                else if (!_config.Debug.DebugDialogueEnabled)
                {
                    reached = "no line off 'start' was recorded in the route below, and the start-token probe is off "
                              + "(debugDialogueEnabled = false), so we cannot tell whether 'start' itself was reached";
                }
                else if (!_startTokenReached)
                {
                    reached = "'start' was NOT reached either - this conversation began at some other token";
                }
                else
                {
                    reached = "'start' WAS reached, so some line on 'start' won and routed the conversation past "
                              + $"'{token}' - see the route below";
                }

                cause = $"nothing at priority >= {prio} shares '{token}' with us, so this is not a lost race; " + reached;
            }
            else
            {
                cause = $"could not scan '{token}' for rival lines (ours: priority {prio}) - see the next line";
            }

            string recoveryNote;
            if (scan.RivalCount == 0 && FirstLineOffStart(token) != null)
            {
                EnsureVolunteerOfferCached(hero);
                if (_cachedHasVolunteerOffer && _cachedVolunteerOffer != null)
                {
                    recoveryNote = "recovery option offered to player (vividworld_recovery_ask on hero_main_options)";
                }
                else
                {
                    recoveryNote = "recovery option not offered (no eligible rumor offer for this partner)";
                }
            }
            else if (_deliveredVolunteerThisConversation)
            {
                recoveryNote = "recovery option not offered (rumor already delivered in this conversation)";
            }
            else
            {
                recoveryNote = "recovery option not offered (not a bypassed route)";
            }

            string missMsg = $"Volunteer {tellerName} ({hero.StringId}): line not evaluated in this conversation - {cause}. {recoveryNote}.";
            if (_config.Debug.ListenTally)
            {
                string missTally = scan.RivalCount > 0
                    ? ListenTallyKeys.VolunteerNotEvaluatedTokenLost
                    : ListenTallyKeys.VolunteerNotEvaluatedStartNotReached;
                missMsg += $" | tally: {missTally}";
            }
            ModLog.Warn(missMsg);

            // 這一場實際走過的路線。**每次失誤都印**（不吃 `_tokenContentionReported` 的限制）——
            // 它每一場都不一樣，而同一個 token 上有誰是一整個 session 不變的事。
            if (_chosenThisConversation.Count > 0)
            {
                ModLog.Info("Conversation route (every line the engine actually picked, in order):"
                            + Environment.NewLine
                            + string.Join(Environment.NewLine, _chosenThisConversation.Select(x => "    " + x)));
            }
            else if (_config.Debug.DebugDialogueEnabled)
            {
                ModLog.Info("Conversation route: nothing recorded - the ConsequenceRunned hook is not installed "
                            + "(see the warning at registration time).");
            }

            if (!_tokenContentionReported)
            {
                _tokenContentionReported = true;
                ModLog.Info(scan.Description);

                // `start` 上每一條優先權不低於原版預設 100 的行，連它們把對話帶去哪個 token。
                // 路線已經指名兇手了，這一份是背景：同樣繞得過 `lord_start` 的還有誰。
                if (scan.RivalCount == 0 && _config.Debug.DebugDialogueEnabled && _startTokenReached)
                {
                    ModLog.Info(ScanTokenContention(StartProbeLineId, 100).Description);
                }
            }

            ModLog.Flush();
        }

        /// <summary>
        /// 把引擎現場登記著的句子掃一遍，列出跟我們同一個 input token、
        /// 而且優先權不低於我們的行——包括第三方模組的。這是唯一能在**他那份模組組合下**
        /// 回答「我們到底排在誰後面」的方法；離線的 dev/DialogArgs.exe 只看得到裝了什麼，
        /// 看不到執行期真正被註冊起來的那一份。
        /// InputToken 在引擎裡是 int（內連後的 id），所以先用我們自己那條的 Id 找到值，
        /// 再拿那個值去比，不需要字串→int 的對照表。
        /// </summary>
        internal static TokenContentionScanResult ScanTokenContention(
            string lineId = "vividworld_npc_rumor", int? floorPriority = null)
        {
            try
            {
                var manager = TaleWorlds.CampaignSystem.Campaign.Current?.ConversationManager;
                if (manager == null) return TokenContentionScanResult.Failed("Token contention: ConversationManager unavailable.");

                var field = manager.GetType().GetField(
                    "_sentences",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                if (field == null) return TokenContentionScanResult.Failed("Token contention: ConversationManager._sentences not found (game update?).");

                if (!(field.GetValue(manager) is System.Collections.IEnumerable sentences))
                {
                    return TokenContentionScanResult.Failed("Token contention: ConversationManager._sentences was not enumerable.");
                }

                var all = new List<object>();
                foreach (var item in sentences)
                {
                    if (item != null) all.Add(item);
                }
                if (all.Count == 0) return TokenContentionScanResult.Failed("Token contention: no sentences registered.");

                var type = all[0].GetType();
                var idProp = type.GetProperty("Id");
                var prioProp = type.GetProperty("Priority");
                var tokenProp = type.GetProperty("InputToken");
                var condField = type.GetField("OnCondition");
                if (idProp == null || prioProp == null || tokenProp == null)
                {
                    return TokenContentionScanResult.Failed("Token contention: ConversationSentence shape changed (Id/Priority/InputToken).");
                }

                object? ours = all.FirstOrDefault(x => (idProp.GetValue(x, null) as string) == lineId);
                if (ours == null) return TokenContentionScanResult.Failed($"Token contention: our line '{lineId}' is not registered at all.");

                int ourToken = (int)tokenProp.GetValue(ours, null);
                int ourPriority = floorPriority ?? (int)prioProp.GetValue(ours, null);

                var rivals = all
                    .Where(x => !ReferenceEquals(x, ours))
                    .Where(x => (int)tokenProp.GetValue(x, null) == ourToken)
                    .Where(x => (int)prioProp.GetValue(x, null) >= ourPriority)
                    .OrderByDescending(x => (int)prioProp.GetValue(x, null))
                    .ToList();

                if (rivals.Count == 0)
                {
                    return new TokenContentionScanResult(
                        0, $"Token contention: nothing at priority >= {ourPriority} shares the token of '{lineId}'.");
                }

                var parts = new List<string>();
                foreach (var r in rivals)
                {
                    string rid = (idProp.GetValue(r, null) as string) ?? "(no id)";
                    int rprio = (int)prioProp.GetValue(r, null);
                    string owner = "(no condition - unconditional)";
                    var cond = condField?.GetValue(r) as Delegate;
                    if (cond != null)
                    {
                        var m = cond.Method;
                        owner = (m.DeclaringType != null ? m.DeclaringType.FullName + "." : "") + m.Name;
                    }
                    string outTok = string.Empty;
                    var outProp = type.GetProperty("OutputToken");
                    if (outProp != null)
                    {
                        // 輸出 token 才是重點：贏了之後把對話帶到哪裡。
                        outTok = " -> token " + outProp.GetValue(r, null);
                    }
                    parts.Add($"    {rid} priority {rprio}{outTok} [{owner}]");
                }

                return new TokenContentionScanResult(
                    rivals.Count,
                    $"Token contention: {rivals.Count} line(s) at priority >= {ourPriority} share the token of '{lineId}':"
                    + Environment.NewLine + string.Join(Environment.NewLine, parts));
            }
            catch (Exception ex)
            {
                return TokenContentionScanResult.Failed(
                    "Token contention: scan failed - " + ex.GetType().Name + ": " + ex.Message);
            }
        }

        /// <summary>
        /// 掃描結果。<c>RivalCount</c> 是同一個 token 上優先權不低於我們的行數，
        /// <c>-1</c> 代表根本掃不出來。叫的人要拿它決定語氣，不要去剖字串。
        /// </summary>
        internal sealed class TokenContentionScanResult
        {
            public readonly int RivalCount;
            public readonly string Description;

            public TokenContentionScanResult(int rivalCount, string description)
            {
                RivalCount = rivalCount;
                Description = description;
            }

            public static TokenContentionScanResult Failed(string description)
            {
                return new TokenContentionScanResult(-1, description);
            }
        }

        private bool HasAskOfferCondition()
        {
            try
            {
                _askAsked = true;
                EnsureOfferCached();
                if (_cachedHasOffer && _cachedOffer != null)
                {
                    var renderResult = FallbackTextRenderer.RenderBoth(_cachedOffer.Composed, _config.Presentation);
                    if (string.IsNullOrWhiteSpace(renderResult.DisplayText))
                    {
                        // 全部碎片都被丟掉 ⇒ 走 vividworld_ask_nothing，而不是空白回答。
                        ModLog.Warn($"Ask: offer {_cachedOffer.EventId} rendered to an empty string - falling back to 'nothing worth repeating'.");
                        return false;
                    }
                    _renderedAskText = renderResult.DisplayText;
                    _renderedAskTextPlain = renderResult.PlainText;
                    MBTextManager.SetTextVariable("VIVIDWORLD_RUMOR", renderResult.DisplayText, false);
                    return true;
                }
                return false;
            }
            catch (Exception ex)
            {
                ModLog.Error("Error in HasAskOfferCondition", ex);
                return false;
            }
        }

        private void OnAskAnswered()
        {
            try
            {
                if (!_ready || _offerSelector == null || _store == null) return;

                if (_cachedOffer != null && _cachedEvent != null)
                {
                    var hero = Hero.OneToOneConversationHero;
                    if (hero != null)
                    {
                        double day = CampaignTime.Now.ToDays;
                        string tellerName = hero.Name?.ToString() ?? hero.StringId;
                        ApplyOfferAndRecord(_cachedOffer, _cachedEvent, hero.StringId, day, tellerName);

                        _askAsked = true;
                        _askTold = true;

                        string askMsg = $"Rumor delivered to player: event {_cachedOffer.EventId} (hop {_cachedOffer.ResultingPlayerHop}, isRetell={_cachedOffer.IsRetell}) from {hero.Name}";
                        if (_config.Debug.ListenTally)
                        {
                            askMsg += $" | tally: {ListenTallyKeys.AskTold}";
                        }
                        ModLog.Info(askMsg);
                        ModLog.Info($"  text shown: \"{_renderedAskTextPlain ?? _renderedAskText}\"");
                    }
                }
            }
            catch (Exception ex)
            {
                ModLog.Error("Error in OnAskAnswered", ex);
            }
            finally
            {
                InvalidateOfferCache();
                // 對話期間遊戲時間是停的 ⇒ 沒有 hourly/daily tick 來沖洗，而 ConversationEnded
                // 要等他離開才觸發。不在這裡沖，「按下詢問 → 切出去看 log」就一定看不到東西。
                ModLog.Flush();
            }
        }

        private bool HasAskRefusalLineCondition()
        {
            try
            {
                EnsureOfferCached();
                if (_cachedAskDecision == null || _cachedAskDecision.Offer != null)
                {
                    return false;
                }

                var kind = AskRefusalLine.Choose(
                    _cachedAskDecision,
                    _cachedAskKnownCount,
                    _cachedAskForgottenCount,
                    _cachedAskOutdatedCount);

                if (kind == AskRefusalLineKind.Other)
                {
                    return false;
                }

                string? key = AskRefusalLine.GetStringKey(kind);
                string? fallback = AskRefusalLine.GetEnglishFallback(kind);
                if (string.IsNullOrEmpty(key) || fallback == null)
                {
                    return false;
                }

                // 先渲染成玩家語言的字串再注入，跟 HasAskOfferCondition 同一個多載（規格 §9.2，已驗證）。
                string text = new TextObject("{=" + key + "}" + fallback).ToString();
                MBTextManager.SetTextVariable("VIVIDWORLD_ASK_REFUSAL", text, false);
                return true;
            }
            catch (Exception ex)
            {
                ModLog.Error("Error in HasAskRefusalLineCondition", ex);
                return false;
            }
        }

        private void OnAskRefused()
        {
            try
            {
                var hero = Hero.OneToOneConversationHero;
                string heroName = hero?.Name?.ToString() ?? hero?.StringId ?? "unknown";
                string heroId = hero?.StringId ?? "unknown";

                var kind = AskRefusalLine.Choose(
                    _cachedAskDecision,
                    _cachedAskKnownCount,
                    _cachedAskForgottenCount,
                    _cachedAskOutdatedCount);
                string key = AskRefusalLine.GetStringKey(kind) ?? "(unknown)";
                string logLine = AskRefusalLine.FormatRefusedLog(
                    heroName, heroId, key, _cachedAskDecision,
                    _cachedAskKnownCount, _cachedAskForgottenCount, _cachedAskOutdatedCount);
                ModLog.Info(logLine);

                _askAsked = true;
                InvalidateOfferCache();
            }
            catch (Exception ex)
            {
                ModLog.Error("Error in OnAskRefused", ex);
            }
            finally
            {
                ModLog.Flush();
            }
        }

        /// <summary>
        /// 回答「沒什麼值得說的」之後也要讓快取失效。
        /// 少了這一步，同一場對話裡對同一個人再問一次會直接命中 EnsureOfferCached 的
        /// 記憶化早退，一行 log 都不留 ⇒ 「他為什麼不再講」正好在最需要答案的時候答不出來。
        /// 有提案的那條路徑本來就在 OnAskAnswered 失效，這裡補的是沒提案的那一條。
        /// </summary>
        private void OnAskNothing()
        {
            try
            {
                var hero = Hero.OneToOneConversationHero;
                string heroName = hero?.Name?.ToString() ?? hero?.StringId ?? "unknown";
                string heroId = hero?.StringId ?? "unknown";

                bool offerRenderedEmpty = _cachedOffer != null;
                string reason = AskRefusalLine.FormatFallbackReason(_cachedAskDecision, offerRenderedEmpty);
                string logLine = AskRefusalLine.FormatFallbackLog(heroName, heroId, reason);
                ModLog.Info(logLine);

                _askAsked = true;
                InvalidateOfferCache();
            }
            catch (Exception ex)
            {
                ModLog.Error("Error in OnAskNothing", ex);
            }
            finally
            {
                ModLog.Flush();
            }
        }

        private void EnsureOfferCached()
        {
            if (!_ready || _offerSelector == null || _store == null || _index == null || _knownBy == null || _traitLookup == null || _heroLookup == null)
            {
                return;
            }

            var hero = Hero.OneToOneConversationHero;
            if (hero == null || !hero.IsAlive)
            {
                _cacheValid = false;
                _cachedHeroId = null;
                _cachedOffer = null;
                _cachedEvent = null;
                _cachedHasOffer = false;
                return;
            }

            if (_cacheValid && string.Equals(_cachedHeroId, hero.StringId, StringComparison.Ordinal))
            {
                return;
            }

            RecordPartnerInfo(hero);
            _cachedHeroId = hero.StringId;
            _cacheValid = true;
            _cachedOffer = null;
            _cachedEvent = null;
            _cachedHasOffer = false;

            double day = CampaignTime.Now.ToDays;
            var profile = BuildSocialProfile(hero);
            var candidates = BuildCandidates(hero, day, out var forgottenEvents, out var outdatedEvents);

            var decision = _offerSelector.DecideOnAsk(profile, candidates, day);

            if (decision.Offer != null)
            {
                _cachedOffer = decision.Offer;
                _cachedEvent = candidates.FirstOrDefault(c => c.Event.EventId == decision.Offer.EventId)?.Event;
                _cachedHasOffer = true;
            }

            var knownEventIds = _knownBy.EventsKnownBy(hero.StringId, day);
            int knownCount = knownEventIds?.Count ?? 0;

            _cachedAskDecision = decision;
            _cachedAskKnownCount = knownCount;
            _cachedAskForgottenCount = forgottenEvents.Count;
            _cachedAskOutdatedCount = outdatedEvents.Count;

            string logLine = AskDecision.FormatLog(
                hero.Name?.ToString() ?? hero.StringId,
                hero.StringId,
                decision,
                knownCount,
                _config.Dialogue.AskRelationGate,
                profile.Traits.Generosity,
                profile.Traits.Honor,
                profile.Traits.Calculating);

            if (_config.Debug.ListenTally)
            {
                // 有東西可講時 ClassifyAsk(told: false) 會落到最後的預設分支（ask.filtered）——
                // 那是錯的標籤。講出口那一行會印 `tally: ask.told`。
                string askTally = decision.Offer != null
                    ? $"{ListenTallyKeys.AskTold} when spoken"
                    : ListenTallyClassifier.ClassifyAsk(
                        true, false, decision, knownCount, forgottenEvents.Count, outdatedEvents.Count);
                logLine += $" | tally: {askTally}";
            }

            ModLog.Info(logLine);

            if (forgottenEvents.Count > 0)
            {
                ModLog.Info(MemoryLogFormatter.FormatForgottenSummary(
                    hero.Name?.ToString() ?? hero.StringId,
                    hero.StringId,
                    forgottenEvents,
                    knownCount));
            }

            if (outdatedEvents.Count > 0)
            {
                ModLog.Info(MemoryLogFormatter.FormatOutdatedSummary(
                    hero.Name?.ToString() ?? hero.StringId,
                    hero.StringId,
                    outdatedEvents));
            }
        }

        private HeroSocialProfile BuildSocialProfile(Hero hero)
        {
            var mainHero = Hero.MainHero;
            int relation = 0;
            bool isSpouse = false;
            bool isCompanion = false;
            bool isClanMember = false;

            if (mainHero != null)
            {
                relation = (int)hero.GetRelation(mainHero);
                isSpouse = hero.Spouse == mainHero;
                isCompanion = hero.CompanionOf == mainHero.Clan;
                isClanMember = hero.Clan == mainHero.Clan;
            }

            var traits = _traitLookup?.Of(hero.StringId) ?? new VividWorld.Core.Rumors.TraitProfile { HeroId = hero.StringId };

            double lastDay = -1.0;
            if (_lastVolunteeredDays.TryGetValue(hero.StringId, out double recordedDay))
            {
                lastDay = recordedDay;
            }

            return new HeroSocialProfile
            {
                HeroId = hero.StringId,
                RelationWithPlayer = relation,
                IsPlayerSpouse = isSpouse,
                IsPlayerCompanion = isCompanion,
                IsPlayerClanMember = isClanMember,
                Traits = traits,
                LastVolunteeredDay = lastDay
            };
        }

        private void LoadVolunteers()
        {
            try
            {
                _lastVolunteeredDays.Clear();
                if (string.IsNullOrEmpty(_campaignId)) return;
                string path = VividWorldPaths.VolunteersFile(_campaignId!);
                if (File.Exists(path))
                {
                    string json = File.ReadAllText(path);
                    var dict = JsonConvert.DeserializeObject<Dictionary<string, double>>(json);
                    if (dict != null)
                    {
                        foreach (var kv in dict)
                        {
                            _lastVolunteeredDays[kv.Key] = kv.Value;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                ModLog.Warn($"Failed to load volunteers.json for campaign {_campaignId}: {ex.Message}");
            }
        }

        private void SaveVolunteers()
        {
            try
            {
                if (string.IsNullOrEmpty(_campaignId)) return;
                string path = VividWorldPaths.VolunteersFile(_campaignId!);
                string json = JsonConvert.SerializeObject(_lastVolunteeredDays, Formatting.Indented);
                var writer = new SystemFileWriter();
                AtomicFile.Write(writer, path, json);
            }
            catch (Exception ex)
            {
                ModLog.Warn($"Failed to save volunteers.json for campaign {_campaignId}: {ex.Message}");
            }
        }

        private List<RumorCandidate> BuildCandidates(
            Hero teller,
            double day,
            out List<(string EventId, double ForgetDay)> forgottenEvents,
            out List<(string EventId, double OutdatedDay)> outdatedEvents)
        {
            return BuildCandidates(teller, day, out forgottenEvents, out outdatedEvents, stampIfNeeded: true, out _);
        }

        private List<RumorCandidate> BuildCandidates(
            Hero teller,
            double day,
            out List<(string EventId, double ForgetDay)> forgottenEvents,
            out List<(string EventId, double OutdatedDay)> outdatedEvents,
            bool stampIfNeeded,
            out int unstampedCount)
        {
            unstampedCount = 0;
            forgottenEvents = new List<(string EventId, double ForgetDay)>();
            outdatedEvents = new List<(string EventId, double OutdatedDay)>();
            var result = new List<RumorCandidate>();
            if (_knownBy == null || _store == null || _heroLookup == null) return result;

            var knownEventIds = _knownBy.EventsKnownBy(teller.StringId, day);
            if (knownEventIds == null || knownEventIds.Count == 0)
            {
                return result;
            }

            string playerHeroId = Hero.MainHero?.StringId ?? "player";

            foreach (var eventId in knownEventIds)
            {
                var evt = _store.Load(eventId, _index);
                if (evt == null) continue;

                if (stampIfNeeded)
                {
                    if (_stamper != null && _stamper.EnsureStamped(evt))
                    {
                        _store.Upsert(evt);
                    }
                }

                var tellerEntry = evt.EntryFor(teller.StringId);
                if (tellerEntry == null) continue;

                if (!stampIfNeeded && tellerEntry.Interest == null)
                {
                    unstampedCount++;
                }

                if (Forgetting.IsForgotten(evt, tellerEntry, day, playerHeroId, _config.Memory))
                {
                    forgottenEvents.Add((evt.EventId, tellerEntry.ForgetDay ?? 0.0));
                    continue;
                }

                if (Outdating.IsOutdated(tellerEntry))
                {
                    outdatedEvents.Add((evt.EventId, tellerEntry.OutdatedDay ?? 0.0));
                    continue;
                }

                var playerEntry = evt.EntryFor(playerHeroId);

                bool involvesCared = false;
                if (Hero.MainHero != null && evt.Participants != null)
                {
                    foreach (var participantHeroId in evt.Participants.Values)
                    {
                        var h = _heroLookup.Get(participantHeroId);
                        if (h != null && h.GetRelation(Hero.MainHero) >= _config.Dialogue.ScoreRelevanceRelationGate)
                        {
                            involvesCared = true;
                            break;
                        }
                    }
                }

                result.Add(new RumorCandidate
                {
                    Event = evt,
                    TellerHop = tellerEntry.Hop,
                    PlayerExistingHop = playerEntry?.Hop,
                    InvolvesHeroPlayerCaresAbout = involvesCared
                });
            }

            return result;
        }

        public ListenPreviewResult RunPreview(bool includeHeroDetails = false)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            SyncRumorModeWithConfig();
            if (!_ready || _offerSelector == null || _store == null || _index == null ||
                _knownBy == null || _traitLookup == null || _heroLookup == null)
            {
                return new ListenPreviewResult();
            }

            double day = CampaignTime.Now.ToDays;
            int playerClanTier = PlayerClanTier();
            // 不能呼叫 Advance（預演不改狀態），也不能直接讀 Count：它要等下一次 Advance 才歸零。
            int volunteersToday = _volunteersCounter.CountOn(day);

            var persons = new List<ListenPreviewPerson>();
            int totalUnstamped = 0;

            var heroes = _heroLookup.AllAlive;
            foreach (var h in heroes)
            {
                if (h == null || !h.IsAlive || h == Hero.MainHero) continue;
                if (!Eligibility.IsEligible(_traitLookup, h.StringId)) continue;

                var profile = BuildSocialProfile(h);
                var candidates = BuildCandidates(h, day, out var forgotten, out var outdated, stampIfNeeded: false, out int unstamped);
                totalUnstamped += unstamped;

                var knownIds = _knownBy.EventsKnownBy(h.StringId, day);
                int knownCount = knownIds?.Count ?? 0;

                persons.Add(new ListenPreviewPerson
                {
                    HeroId = h.StringId,
                    HeroName = h.Name?.ToString() ?? h.StringId,
                    IsLord = h.IsLord,
                    Profile = profile,
                    Candidates = candidates,
                    KnownCount = knownCount,
                    ForgottenCount = forgotten.Count,
                    OutdatedCount = outdated.Count,
                    UnstampedCount = unstamped
                });
            }

            var result = ListenPreviewAggregator.Generate(
                _offerSelector,
                _compat,
                playerClanTier,
                volunteersToday,
                day,
                _config.Dialogue,
                persons,
                includeHeroDetails: includeHeroDetails);

            // 判定（DecideOnVolunteer／DecideOnAsk）跑在 Generate 裡，耗時要把它算進去。
            sw.Stop();
            result.ElapsedMilliseconds = sw.ElapsedMilliseconds;
            result.UnstampedEntriesCount = totalUnstamped;
            return result;
        }

        public void RunAndLogPreview(string trigger)
        {
            try
            {
                var result = RunPreview(includeHeroDetails: false);
                string summary = ListenPreviewLogFormatter.FormatSummary(result, includeTopNames: false, trigger: trigger);
                ModLog.Info(summary);
            }
            catch (Exception ex)
            {
                ModLog.Error("Error running Listen Preview", ex);
            }
            finally
            {
                ModLog.Flush();
            }
        }
    }
}
