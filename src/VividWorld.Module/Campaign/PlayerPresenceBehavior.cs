using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.CharacterDevelopment;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using VividWorld.Core.Config;
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
    /// 玩家在城鎮或城堡時，另外替他擲「一件情境在他面前發生」：進城當下擲一次、待在城裡每天再擲一次。
    /// 發生了就暫停遊戲跳自己的視窗（版面像原版隨機事件視窗）：旁觀與聽的人只有「知道了」，做決定則列出做法讓玩家選。
    /// 不佔全世界每天的情境量；NPC 之間的情境不經過這裡。
    /// </summary>
    internal sealed class PlayerPresenceBehavior : CampaignBehaviorBase
    {
        /// <summary>存「上一次真的跳出視窗是第幾天」。舊存檔沒有這個鍵時 SyncData 不動欄位，維持空字串，也就是沒有冷卻（帳本 D-61）。</summary>
        private const string LastDayKey = "VividWorld_PlayerPresenceLastDay";

        /// <summary>dev 工具拿得到這一份；每次讀檔 Initialize 時換成新的。</summary>
        internal static PlayerPresenceBehavior? Current { get; private set; }

        private readonly VividWorldConfig _config;
        private WorldEventStore? _eventStore;
        private HeroLookup? _heroLookup;
        private IHeroTraitLookup? _traits;
        private Func<PlayerHeardLogStore?>? _heardLog;
        private Func<RumorEngine?>? _engine;
        private bool _ready;

        /// <summary>每次 Initialize 加一；視窗的回呼對不上就是讀檔或換局之後才回來，不准再動任何東西。</summary>
        private int _generation;

        private bool _windowOpen;
        private string _lastDayRaw = string.Empty;

        internal PlayerPresenceRollResult? LastRoll { get; private set; }
        internal double? LastRollDay { get; private set; }

        internal PlayerPresenceBehavior(VividWorldConfig config)
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));
        }

        internal void Initialize(
            WorldEventStore eventStore,
            HeroLookup heroLookup,
            IHeroTraitLookup traits,
            Func<PlayerHeardLogStore?> heardLog,
            Func<RumorEngine?> engine)
        {
            _eventStore = eventStore ?? throw new ArgumentNullException(nameof(eventStore));
            _heroLookup = heroLookup ?? throw new ArgumentNullException(nameof(heroLookup));
            _traits = traits ?? throw new ArgumentNullException(nameof(traits));
            _heardLog = heardLog;
            _engine = engine;
            // 讀檔或換局時上一局的視窗可能還開著：關掉並還原暫停與時間，它的回呼不會再被叫
            PlayerPresenceWindowManager.Close();
            _generation++;
            _windowOpen = false;
            _ready = true;
            Current = this;
            ModLog.Info($"PlayerPresence: ready ({CooldownStatus()}).");
        }

        public override void RegisterEvents()
        {
            CampaignEvents.SettlementEntered.AddNonSerializedListener(this, OnSettlementEntered);
            CampaignEvents.DailyTickEvent.AddNonSerializedListener(this, OnDailyTick);
            ModLog.Info("PlayerPresenceBehavior.RegisterEvents: subscribed to SettlementEntered and DailyTickEvent.");
        }

        public override void SyncData(IDataStore dataStore)
        {
            dataStore.SyncData(LastDayKey, ref _lastDayRaw);
        }

        /// <summary>上一次真的發生是第幾天；沒有紀錄（含舊存檔）是 null。</summary>
        internal double? LastOccurrenceDay
        {
            get
            {
                if (string.IsNullOrEmpty(_lastDayRaw)) return null;
                return double.TryParse(_lastDayRaw, NumberStyles.Float, CultureInfo.InvariantCulture, out double d) ? d : (double?)null;
            }
        }

        internal string CooldownStatus()
        {
            var settings = TownPresenceTrigger.Instance.Settings(_config);
            var last = LastOccurrenceDay;
            string onOff = settings.Enabled ? "on" : "off";
            if (!last.HasValue)
            {
                return string.Format(CultureInfo.InvariantCulture, "{0}, chance {1:0.###}, cooldown {2}d, never happened", onOff, settings.Chance, settings.CooldownDays);
            }
            double today = TaleWorlds.CampaignSystem.Campaign.Current != null ? CampaignTime.Now.ToDays : last.Value;
            double left = Math.Max(0.0, settings.CooldownDays - (today - last.Value));
            return string.Format(CultureInfo.InvariantCulture, "{0}, chance {1:0.###}, cooldown {2}d, last on day {3:0.0}, {4:0.0}d left",
                onOff, settings.Chance, settings.CooldownDays, last.Value, left);
        }

        private void OnSettlementEntered(MobileParty party, Settlement settlement, Hero hero)
        {
            try
            {
                if (party == null || party != MobileParty.MainParty) return;
                TryRoll(PlayerPresenceMoment.Entry, settlement, null);
            }
            catch (Exception ex)
            {
                ModLog.Error("Error in PlayerPresenceBehavior.OnSettlementEntered", ex);
            }
        }

        private void OnDailyTick()
        {
            try
            {
                var settlement = MobileParty.MainParty?.CurrentSettlement;
                if (settlement == null) return;   // 在野外：不擲，也不每天印一行
                TryRoll(PlayerPresenceMoment.Daily, settlement, null);
            }
            catch (Exception ex)
            {
                ModLog.Error("Error in PlayerPresenceBehavior.OnDailyTick", ex);
            }
        }

        /// <summary>dev 工具：立即替玩家擲一次，無視冷卻與機率；可以指定情境與身分。</summary>
        internal string ForceRoll(string? situationId, PlayerPresenceRole? role)
        {
            var settlement = MobileParty.MainParty?.CurrentSettlement;
            var result = TryRoll(PlayerPresenceMoment.Dev, settlement, new PlayerPresenceForce { SituationId = situationId, Role = role });
            ModLog.Flush();
            if (result == null) return "(dev) Not rolled - see log.txt.";
            return "(dev) " + PlayerPresenceLogFormatter.FormatRoll(result);
        }

        /// <summary>dev 工具：試算這裡現在叫得出來的情境 × 身分，不跳視窗、不寫冷卻、不改 LastRoll/LastRollDay、不執行情境。</summary>
        internal PlayerPresenceRollResult? PreviewRoll(Settlement? settlement)
        {
            if (!_ready || _eventStore == null || _heroLookup == null || _traits == null)
            {
                ModLog.Warn("PlayerPresence: dev preview roll before Initialize - skipped.");
                return null;
            }

            var main = Hero.MainHero;
            if (main == null || string.IsNullOrEmpty(main.StringId)) return null;

            double day = CampaignTime.Now.ToDays;
            string playerId = main.StringId;
            var heroes = GetUsableHeroes(settlement, playerId);

            var input = new PlayerPresenceRollInput
            {
                PlayerHeroId = playerId,
                Place = new PlayerPresencePlace { SettlementId = settlement?.StringId, SettlementKind = KindOf(settlement) },
                AvailableHeroIds = heroes.Select(h => h.StringId).ToList(),
                Today = day,
                LastOccurrenceDay = LastOccurrenceDay,
                CampaignSeed = _eventStore.CampaignSeed,
                Moment = PlayerPresenceMoment.Dev,
                Force = new PlayerPresenceForce()
            };

            var evaluator = new ModuleEvaluator(this, settlement, heroes, day);
            var templates = SituationCatalogStore.Catalog?.Situations ?? new List<SituationTemplate>();
            var result = PlayerPresenceRoller.Roll(input, TownPresenceTrigger.Instance, _config, templates, evaluator, new SplitMix64Rng());

            ModLog.Info("(dev preview) " + PlayerPresenceLogFormatter.FormatRoll(result));
            foreach (var line in PlayerPresenceLogFormatter.FormatCandidates(result))
            {
                ModLog.Info(line);
            }

            return result;
        }

        private PlayerPresenceRollResult? TryRoll(PlayerPresenceMoment moment, Settlement? settlement, PlayerPresenceForce? force)
        {
            if (!_ready || _eventStore == null || _heroLookup == null || _traits == null)
            {
                ModLog.Warn($"PlayerPresence: {moment.ToString().ToLowerInvariant()} roll before Initialize - skipped.");
                return null;
            }

            var settings = TownPresenceTrigger.Instance.Settings(_config);
            if (!settings.Enabled && force == null) return null;   // 關掉就完全不擲

            if (_windowOpen)
            {
                ModLog.Info($"PlayerPresence: {moment.ToString().ToLowerInvariant()} roll skipped, the last window is still open.");
                return null;
            }

            var main = Hero.MainHero;
            if (main == null || string.IsNullOrEmpty(main.StringId)) return null;

            double day = CampaignTime.Now.ToDays;
            string playerId = main.StringId;

            // 在場可用的領主：跟每日掃描同一套篩法（排除玩家與不合格的人）
            var heroes = GetUsableHeroes(settlement, playerId);

            var input = new PlayerPresenceRollInput
            {
                PlayerHeroId = playerId,
                Place = new PlayerPresencePlace { SettlementId = settlement?.StringId, SettlementKind = KindOf(settlement) },
                AvailableHeroIds = heroes.Select(h => h.StringId).ToList(),
                Today = day,
                LastOccurrenceDay = LastOccurrenceDay,
                CampaignSeed = _eventStore.CampaignSeed,
                Moment = moment,
                Force = force
            };

            var evaluator = new ModuleEvaluator(this, settlement, heroes, day);
            var templates = SituationCatalogStore.Catalog?.Situations ?? new List<SituationTemplate>();
            var result = PlayerPresenceRoller.Roll(input, TownPresenceTrigger.Instance, _config, templates, evaluator, new SplitMix64Rng());
            LastRoll = result;
            LastRollDay = day;

            ModLog.Info(PlayerPresenceLogFormatter.FormatRoll(result));
            foreach (var line in PlayerPresenceLogFormatter.FormatCandidates(result))
            {
                ModLog.Info(line);
            }

            if (result.Outcome == PlayerPresenceOutcome.Happened && result.Pick != null)
            {
                bool shown = Present(result.Pick, day, settlement);
                if (shown)
                {
                    // 冷卻從視窗真的跳出來那天算起
                    _lastDayRaw = day.ToString("R", CultureInfo.InvariantCulture);
                }
            }

            return result;
        }

        /// <summary>在場可用的人：跟每日掃描同一套篩法（排除玩家與不合格者）。</summary>
        internal List<Hero> GetUsableHeroes(Settlement? settlement, string? playerId = null)
        {
            var main = Hero.MainHero;
            string pid = playerId ?? main?.StringId ?? string.Empty;
            var present = settlement != null ? EligibilityLabel.GetPresentHeroesAtSettlement(settlement) : new List<Hero>();
            return present
                .Where(h => h != null
                            && !string.IsNullOrEmpty(h.StringId)
                            && h != main
                            && (string.IsNullOrEmpty(pid) || !string.Equals(h.StringId, pid, StringComparison.Ordinal))
                            && EligibilityLabel.GetRejectionReason(h, _traits) == null)
                .OrderBy(h => h.StringId, StringComparer.Ordinal)
                .ToList();
        }

        private static string? KindOf(Settlement? settlement)
        {
            if (settlement == null) return null;
            if (settlement.IsTown) return "town";
            if (settlement.IsCastle) return "castle";
            if (settlement.IsVillage) return "village";
            return "other";
        }

        // ── 呈現 ────────────────────────────────────────────────────────────────────

        private bool Present(PlayerPresencePick pick, double day, Settlement? settlement)
        {
            if (!(pick.Assignment.Evaluation.State is SituationEvaluation evaluation))
            {
                ModLog.Warn($"PlayerPresence: {pick.Template.Id} picked without an evaluation - nothing shown.");
                return false;
            }

            string playerId = Hero.MainHero?.StringId ?? string.Empty;
            if (pick.Role == PlayerPresenceRole.Decider)
            {
                return ShowDecision(pick, evaluation, day, settlement, playerId);
            }

            // 旁觀與聽的人：照原本擲分支，事情發生了才把結果演給玩家看
            var run = SituationRunner.Execute(
                pick.Template,
                evaluation,
                day,
                "player presence",
                _eventStore,
                _traits,
                _config,
                _heroLookup,
                presence: new PlayerPresenceRunOptions
                {
                    PlayerHeroId = playerId,
                    ExcludeSecretOnlyBranches = pick.Role == PlayerPresenceRole.Bystander
                });

            PlayerRelationNotifier.NotifyAll(run.PlayerRelationNotices, _heroLookup);

            if (!run.Started || run.SelectedBranchId == null)
            {
                ModLog.Info($"PlayerPresence: {pick.Template.Id} did not run ({run.AbortReason ?? "unknown"}) - nothing shown.");
                return false;
            }

            string? factText = RecordChronicle(run.EventIds, day);
            string scene = PlayerPresenceText.Scene(pick.Template.Id, pick.Role, evaluation.BoundHeroIds);
            string? secondParagraph;

            if (pick.Role == PlayerPresenceRole.Bystander)
            {
                string? outcome = PlayerPresenceText.Outcome(pick.Template.Id, run.SelectedBranchId, evaluation.BoundHeroIds);
                if (outcome != null)
                {
                    secondParagraph = outcome;
                    ModLog.Info($"  player presence: bystander window shows the outcome sentence for {pick.Template.Id}/{run.SelectedBranchId}.");
                }
                else
                {
                    ModLog.Info($"  player presence: no outcome sentence for {pick.Template.Id}/{run.SelectedBranchId}; showing the fact sentence instead.");
                    secondParagraph = factText;
                }
            }
            else
            {
                secondParagraph = factText;
            }

            string description;
            if (string.IsNullOrEmpty(secondParagraph))
            {
                description = scene;
                ModLog.Info($"  player presence: {pick.Template.Id}/{run.SelectedBranchId} left nothing the player saw first-hand; the window shows the scene only.");
            }
            else
            {
                description = scene + "\n\n" + secondParagraph;
            }

            int generation = _generation;
            string situationId = pick.Template.Id;
            var vm = new PlayerPresenceWindowVM(
                PlayerPresenceText.Title(settlement?.StringId),
                description,
                PlayerPresenceText.Confirm(),
                situationId,
                _ => OnAcknowledged(generation, situationId));
            // 旁觀只有一個「知道了」，開窗時就已選，玩家直接按確認鈕
            vm.AddOption("ack", PlayerPresenceText.Ack(), string.Empty, true);

            if (!OpenWindow(vm, situationId, PlayerPresenceLogFormatter.RoleToken(pick.Role)))
            {
                return false;
            }
            ModLog.Info($"  player presence window: {pick.Template.Id} as {PlayerPresenceLogFormatter.RoleToken(pick.Role)}, branch {run.SelectedBranchId}");
            return true;
        }

        /// <summary>開自己的視窗。開不起來（例外）時不留下「視窗開著」的狀態，呼叫端據此當作沒跳出來。</summary>
        private bool OpenWindow(PlayerPresenceWindowVM vm, string situationId, string roleToken)
        {
            _windowOpen = true;
            if (!PlayerPresenceWindowManager.Open(vm))
            {
                _windowOpen = false;
                ModLog.Warn($"PlayerPresence: window for {situationId} could not be opened - nothing shown.");
                return false;
            }
            ModLog.Info($"  player presence window opened: {situationId} as {roleToken}, {vm.OptionCount} option(s), illustration {vm.IllustrationId}");
            return true;
        }

        private void OnAcknowledged(int generation, string situationId)
        {
            if (generation == _generation) _windowOpen = false;
            ModLog.Info($"PlayerPresence: window for {situationId} closed.");
        }

        private bool ShowDecision(PlayerPresencePick pick, SituationEvaluation evaluation, double day, Settlement? settlement, string playerId)
        {
            var template = pick.Template;
            var options = PlayerChoiceOptions.List(
                template,
                evaluation.FactsByRole,
                evaluation.BoundHeroIds,
                evaluation.UnboundReasons,
                evaluation.Context,
                day,
                evaluation.History,
                evaluation.NonDerivedHeroIds);
            var offered = options.Where(o => o.Offered).ToList();
            if (offered.Count == 0)
            {
                ModLog.Info(PlayerPresenceLogFormatter.FormatChoice(template.Id, options, null) + " - nothing offered, no window");
                return false;
            }

            var playerKingdom = Hero.MainHero?.Clan?.Kingdom;
            bool hasKingdom = playerKingdom != null;
            string? counterpartRole = PlayerPresenceRoller.CounterpartRole(template, pick.PlayerRoleName);
            Hero? counterpart = null;
            if (counterpartRole != null) evaluation.AllHeroes.TryGetValue(counterpartRole, out counterpart);
            var counterpartKingdom = counterpart?.Clan?.Kingdom;
            bool sameKingdom = hasKingdom && counterpartKingdom != null && counterpartKingdom == playerKingdom;

            int generation = _generation;
            var vm = new PlayerPresenceWindowVM(
                PlayerPresenceText.Title(settlement?.StringId),
                PlayerPresenceText.Scene(template.Id, pick.Role, evaluation.BoundHeroIds),
                PlayerPresenceText.Confirm(),
                template.Id,
                chosenBranchId => OnDecided(generation, chosenBranchId, pick, evaluation, options, day, hasKingdom, sameKingdom, playerId));
            foreach (var option in offered)
            {
                var directions = PlayerChoiceDirections.Compose(template, option.Branch, evaluation.BoundHeroIds, playerId, hasKingdom, sameKingdom);
                ModLog.Info(PlayerPresenceLogFormatter.FormatDirections(option.Branch.Id, directions));
                // 做決定的選項開窗時都不選，玩家一定要自己點一項
                vm.AddOption(
                    option.Branch.Id,
                    PlayerPresenceText.Option(template.Id, option.Branch.Id, evaluation.BoundHeroIds),
                    PlayerPresenceText.Directions(directions),
                    false);
            }

            if (!OpenWindow(vm, template.Id, "decider"))
            {
                return false;
            }
            ModLog.Info($"  player presence window: {template.Id} as decider ({pick.PlayerRoleName}), {offered.Count} option(s)");
            return true;
        }

        private void OnDecided(
            int generation,
            string? branchId,
            PlayerPresencePick pick,
            SituationEvaluation evaluation,
            IReadOnlyList<PlayerChoiceOption> options,
            double day,
            bool hasKingdom,
            bool sameKingdom,
            string playerId)
        {
            try
            {
                if (generation != _generation || TaleWorlds.CampaignSystem.Campaign.Current == null || Hero.MainHero == null)
                {
                    ModLog.Info($"PlayerPresence: choice for {pick.Template.Id} came back after the campaign changed - ignored.");
                    return;
                }
                _windowOpen = false;

                var chosen = options.FirstOrDefault(o => o.Offered && string.Equals(o.Branch.Id, branchId, StringComparison.Ordinal));
                ModLog.Info(PlayerPresenceLogFormatter.FormatChoice(pick.Template.Id, options, chosen?.Branch.Id));
                if (chosen == null)
                {
                    ModLog.Info($"PlayerPresence: no usable choice for {pick.Template.Id} (picked {branchId ?? "none"}) - nothing happened.");
                    return;
                }

                var run = SituationRunner.Execute(
                    pick.Template,
                    evaluation,
                    day,
                    "player presence",
                    _eventStore,
                    _traits,
                    _config,
                    _heroLookup,
                    presence: new PlayerPresenceRunOptions { PlayerHeroId = playerId, ChosenBranch = chosen.Branch });

                PlayerRelationNotifier.NotifyAll(run.PlayerRelationNotices, _heroLookup);

                ApplyPlayerEffects(pick.Template, chosen.Branch, hasKingdom, sameKingdom);
                RecordChronicle(run.EventIds, day);
            }
            catch (Exception ex)
            {
                ModLog.Error($"PlayerPresence: failed to carry out the choice for {pick.Template.Id}", ex);
            }
            finally
            {
                ModLog.Flush();
            }
        }

        // ── 玩家自己的數值（只在做決定時）──────────────────────────────────────────────

        private static void ApplyPlayerEffects(SituationTemplate template, SituationBranchDef branch, bool hasKingdom, bool sameKingdom)
        {
            var plan = PlayerEffectsResolver.Resolve(template, branch, hasKingdom, sameKingdom);
            var main = Hero.MainHero;
            if (main == null) return;

            if (plan.Renown != 0.0)
            {
                // 加在家族上；印實際讀到的前後值，不假設加到的就是要求的量（帳本 D-58、D-127）
                double? before = main.Clan?.Renown;
                GainRenownAction.Apply(main, (float)plan.Renown, false);
                ModLog.Info(PlayerPresenceLogFormatter.FormatValueChange("renown", plan.Renown, before, main.Clan?.Renown));
            }

            if (plan.Influence != 0.0 && main.Clan != null)
            {
                float before = main.Clan.Influence;
                ChangeClanInfluenceAction.Apply(main.Clan, (float)plan.Influence);
                ModLog.Info(PlayerPresenceLogFormatter.FormatValueChange("influence", plan.Influence, before, main.Clan.Influence));
            }
            else if (plan.InfluenceSkippedReason != null)
            {
                ModLog.Info(PlayerPresenceLogFormatter.FormatValueSkipped("influence", plan.InfluenceSkippedReason));
            }

            if (plan.Morale != 0.0)
            {
                var party = MobileParty.MainParty;
                if (party != null)
                {
                    // 原版事件改士氣的做法：加在主角隊伍近期事件的士氣上（帳本 D-125、D-126）
                    float before = party.RecentEventsMorale;
                    party.RecentEventsMorale = before + (float)plan.Morale;
                    ModLog.Info(PlayerPresenceLogFormatter.FormatValueChange("morale", plan.Morale, before, party.RecentEventsMorale));
                }
                else
                {
                    ModLog.Info(PlayerPresenceLogFormatter.FormatValueSkipped("morale", "no main party"));
                }
            }

            foreach (var t in plan.Traits)
            {
                var trait = TraitOf(t.Key);
                if (trait == null)
                {
                    ModLog.Info(PlayerPresenceLogFormatter.FormatValueSkipped("trait " + t.Key, "unknown trait"));
                    continue;
                }
                // 原版給事件用的個性經驗值：換算等級、在聲譽紀錄留一筆（帳本 D-123）；經驗值跟原版同一處讀（帳本 D-127）
                var developer = TaleWorlds.CampaignSystem.Campaign.Current?.PlayerTraitDeveloper;
                int? xpBefore = developer?.GetPropertyValue(trait);
                int levelBefore = main.GetTraitLevel(trait);
                TraitLevelingHelper.OnIncidentResolved(trait, t.Value);
                int levelAfter = main.GetTraitLevel(trait);
                int? xpAfter = developer?.GetPropertyValue(trait);
                ModLog.Info(PlayerPresenceLogFormatter.FormatTraitChange(t.Key, t.Value, xpBefore, xpAfter, levelBefore, levelAfter));
            }

            if (plan.IsEmpty && plan.InfluenceSkippedReason == null)
            {
                ModLog.Info($"  player values: {branch.Id} moves none of them");
            }
        }

        private static TraitObject? TraitOf(string name)
        {
            switch (name)
            {
                case "honor": return DefaultTraits.Honor;
                case "mercy": return DefaultTraits.Mercy;
                case "valor": return DefaultTraits.Valor;
                case "generosity": return DefaultTraits.Generosity;
                case "calculating": return DefaultTraits.Calculating;
                default: return null;
            }
        }

        // ── 紀事 ────────────────────────────────────────────────────────────────────

        /// <summary>
        /// 玩家是第 0 手知情者的那幾則，當下寫進紀事紀錄（不等下一次讀檔的補寫）。
        /// 回傳第一則寫進去（或本來就在）的事實句，給旁觀與聽的人的視窗用。
        /// </summary>
        private string? RecordChronicle(IEnumerable<string> eventIds, double day)
        {
            string? firstText = null;
            string playerId = Hero.MainHero?.StringId ?? string.Empty;
            var heardLog = _heardLog?.Invoke();
            var engine = _engine?.Invoke();

            foreach (var eventId in eventIds ?? Enumerable.Empty<string>())
            {
                var evt = _eventStore?.Load(eventId);
                if (evt == null)
                {
                    ModLog.Info(PlayerPresenceLogFormatter.FormatChronicle(eventId, false, "event not found"));
                    continue;
                }

                var playerEntry = evt.EntryFor(playerId);
                if (playerEntry == null)
                {
                    ModLog.Info(PlayerPresenceLogFormatter.FormatChronicle(eventId, false, "the player is not a knower"));
                    continue;
                }

                if (!evt.IsVisibleToRumorSystem)
                {
                    ModLog.Info(PlayerPresenceLogFormatter.FormatChronicle(eventId, false, "secret"));
                    continue;
                }

                var facts = PlayerKnownFacts.Of(evt, playerEntry, engine);
                if (heardLog == null)
                {
                    ModLog.Info(PlayerPresenceLogFormatter.FormatChronicle(eventId, false, "chronicle record not loaded"));
                }
                else
                {
                    bool added = heardLog.Record(evt, playerEntry, facts, day, id => _eventStore?.Load(id));
                    ModLog.Info(added
                        ? PlayerPresenceLogFormatter.FormatChronicle(eventId, true)
                        : PlayerPresenceLogFormatter.FormatChronicle(eventId, false, "already in the chronicle"));
                }

                if (firstText == null)
                {
                    var body = RumorTextComposer.Compose(facts, _config.Presentation, isRetell: false);
                    firstText = FallbackTextRenderer.RenderBoth(body, _config.Presentation).PlainText;
                }
            }

            return firstText;
        }

        // ── 條件求值：照 NPC 情境原本那一套 ───────────────────────────────────────────

        /// <summary>
        /// 把角色指派交給 <see cref="SituationRunner.Evaluate"/>：讀英雄事實、推導 host／ruler、評估條件都照原本的規則。
        /// 玩家的「在哪座聚落」改讀主角隊伍所在的聚落，跟城裡的領主同一個來源。
        /// </summary>
        private sealed class ModuleEvaluator : IPlayerPresenceEvaluator
        {
            private readonly PlayerPresenceBehavior _owner;
            private readonly Dictionary<string, Hero> _heroesById;
            private readonly double _day;
            private readonly SituationHistoryIndex _history;
            private readonly Dictionary<string, SituationRoleFacts> _factsCache = new Dictionary<string, SituationRoleFacts>(StringComparer.Ordinal);

            internal ModuleEvaluator(PlayerPresenceBehavior owner, Settlement? settlement, IEnumerable<Hero> heroes, double day)
            {
                _owner = owner;
                _day = day;
                _heroesById = heroes.ToDictionary(h => h.StringId, h => h, StringComparer.Ordinal);
                _history = new SituationHistoryIndex(owner._eventStore?.Index?.Entries ?? new List<RumorIndexEntry>(), day);

                var main = Hero.MainHero;
                if (main != null)
                {
                    var playerFacts = SituationFactsReader.Read(main);
                    playerFacts.SettlementId = settlement?.StringId;
                    playerFacts.SettlementKind = settlement == null ? null : settlement.IsTown ? "town" : settlement.IsCastle ? "castle" : "other";
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
    }
}
