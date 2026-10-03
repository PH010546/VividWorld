using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.Encounters;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.Library;
using TaleWorlds.Localization;
using System.IO;
using VividWorld.Ai;
using VividWorld.Campaign;
using VividWorld.Core.Config;
using VividWorld.Core.Diagnostics;
using VividWorld.Core.Dialogue;
using VividWorld.Core.Events;
using VividWorld.Core.Feelings;
using VividWorld.Core.Grudges;
using VividWorld.Core.Ingest;
using VividWorld.Core.Memory;
using VividWorld.Core.Persistence;
using VividWorld.Core.Rumors;
using VividWorld.Core.Situations;
using VividWorld.Core.Util;
using VividWorld.Dialogue;
using VividWorld.Patches;
using VividWorld.Presentation;

namespace VividWorld.Debug
{
    internal sealed class DevDialogueBehavior
    {
        private const int DevDialoguePriority = 90;
        private const string TokenHeroMainOptions = "hero_main_options";
        private const string TokenCloseWindow = "close_window";

        private const string TokenDevCategoriesPrompt = "vividworld_dev_categories_prompt";
        private const string TokenDevCategories = "vividworld_dev_categories";
        private const string TokenDevBackToMain = "vividworld_dev_back_to_main";

        private const string TokenDevPersonPrompt = "vividworld_dev_person_prompt";
        private const string TokenDevPerson = "vividworld_dev_person";
        private const string TokenDevResultPerson = "vividworld_dev_result_person";

        private const string TokenDevWorldPrompt = "vividworld_dev_world_prompt";
        private const string TokenDevWorld = "vividworld_dev_world";
        private const string TokenDevResultWorld = "vividworld_dev_result_world";

        private const string TokenDevActPrompt = "vividworld_dev_act_prompt";
        private const string TokenDevAct = "vividworld_dev_act";
        private const string TokenDevResultAct = "vividworld_dev_result_act";
        private const string TokenDevCaptureResult = "vividworld_dev_capture_result";

        private readonly VividWorldConfig _config;
        private readonly WorldEventStore _eventStore;
        private readonly GameWorldChannel _channel;
        private readonly RumorPropagationScheduler _scheduler;
        private readonly HeroLookup? _heroLookup;
        private readonly RumorDialogBehavior? _dialogs;
        private readonly RealEventSourceBehavior? _realEvents;
        private readonly IHeroTraitLookup? _traitLookup;
        private readonly SituationScanBehavior? _situationScan;
        private readonly SnapshotSessionState? _sessionState;
        private readonly string? _campaignId;
        private readonly FeelingResolver? _feelings;
        private readonly Func<string>? _personalSeedMarker;
        private readonly int _rollbackCount;
        private readonly double _rollbackMaxDay;
        private readonly double _launchDay;

        private double _lastSimulatedDay = -1;
        private int _lastSimulatedHour = -1;
        private Hero? _pendingBanditCapture;

        public DevDialogueBehavior(
            VividWorldConfig config,
            WorldEventStore eventStore,
            GameWorldChannel channel,
            RumorPropagationScheduler scheduler,
            HeroLookup? heroLookup = null,
            RumorDialogBehavior? dialogs = null,
            int rollbackCount = 0,
            double rollbackMaxDay = 0.0,
            double launchDay = 0.0,
            RealEventSourceBehavior? realEvents = null,
            IHeroTraitLookup? traitLookup = null,
            SituationScanBehavior? situationScan = null,
            SnapshotSessionState? sessionState = null,
            string? campaignId = null,
            FeelingResolver? feelings = null,
            Func<string>? personalSeedMarker = null)
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));
            _eventStore = eventStore ?? throw new ArgumentNullException(nameof(eventStore));
            _channel = channel ?? throw new ArgumentNullException(nameof(channel));
            _scheduler = scheduler ?? throw new ArgumentNullException(nameof(scheduler));
            _heroLookup = heroLookup;
            _dialogs = dialogs;
            _rollbackCount = rollbackCount;
            _rollbackMaxDay = rollbackMaxDay;
            _launchDay = launchDay;
            _realEvents = realEvents;
            _traitLookup = traitLookup;
            _situationScan = situationScan;
            _sessionState = sessionState;
            _campaignId = campaignId;
            _feelings = feelings;
            _personalSeedMarker = personalSeedMarker;
        }

        public void RegisterDialogues(CampaignGameStarter starter)
        {
            if (starter == null) return;
            // Registration gate: if disabled, do not register nodes at all (release-build inertness).
            // - Registration gate: for players who never enabled debug; dialogue tree has zero dev nodes.
            // - Condition gate: for developers tuning in-game; changing JSON and reloading takes effect immediately.
            if (!_config.Debug.DebugDialogueEnabled) return;

            // ── Single Main Options Entry ──
            starter.AddPlayerLine(
                "vividworld_dev_menu_entry",
                TokenHeroMainOptions,
                TokenDevCategoriesPrompt,
                "{=VividWorld_Dev_Menu}(dev) VW developer tools",
                Condition_AlwaysAvailable,
                null,
                DevDialoguePriority);

            starter.AddDialogLine(
                "vividworld_dev_categories_prompt_line",
                TokenDevCategoriesPrompt,
                TokenDevCategories,
                "{=!}...",
                null,
                null,
                100,
                null);

            // ── Category Level ──
            starter.AddPlayerLine(
                "vividworld_dev_menu_person",
                TokenDevCategories,
                TokenDevPersonPrompt,
                "{=VividWorld_Dev_Menu_Person}(dev) About this person",
                Condition_AlwaysAvailable,
                null,
                DevDialoguePriority);

            starter.AddDialogLine(
                "vividworld_dev_person_prompt_line",
                TokenDevPersonPrompt,
                TokenDevPerson,
                "{=!}...",
                null,
                null,
                100,
                null);

            starter.AddPlayerLine(
                "vividworld_dev_menu_world",
                TokenDevCategories,
                TokenDevWorldPrompt,
                "{=VividWorld_Dev_Menu_World}(dev) About the world",
                Condition_AlwaysAvailable,
                null,
                DevDialoguePriority);

            starter.AddDialogLine(
                "vividworld_dev_world_prompt_line",
                TokenDevWorldPrompt,
                TokenDevWorld,
                "{=!}...",
                null,
                null,
                100,
                null);

            starter.AddPlayerLine(
                "vividworld_dev_menu_act",
                TokenDevCategories,
                TokenDevActPrompt,
                "{=VividWorld_Dev_Menu_Act}(dev) Actions and injections",
                Condition_AlwaysAvailable,
                null,
                DevDialoguePriority);

            starter.AddDialogLine(
                "vividworld_dev_act_prompt_line",
                TokenDevActPrompt,
                TokenDevAct,
                "{=!}...",
                null,
                null,
                100,
                null);

            // Category return to main options.
            // A player line must not lead straight to the main options: after the player picks a line,
            // the engine runs the single highest-priority line waiting on the next token, player lines
            // included, so the top option of the main menu would fire as if it had been clicked (D-43).
            // Going through an NPC line first makes the engine show the main options as a normal list.
            starter.AddPlayerLine(
                "vividworld_dev_menu_back",
                TokenDevCategories,
                TokenDevBackToMain,
                "{=VividWorld_Dev_Menu_Back}(dev) Back",
                null,
                null,
                DevDialoguePriority);

            starter.AddDialogLine(
                "vividworld_dev_back_to_main_line",
                TokenDevBackToMain,
                TokenHeroMainOptions,
                "{=!}...",
                null,
                () => ModLog.Info("[DevDialogue] back to the main options (through an NPC line, so no main-menu option is auto-selected)."),
                100,
                null);

            // ── Category 1: About this person (9 tools) ──
            // 1. (dev) What do you know?
            starter.AddPlayerLine(
                "vividworld_dev_known_events",
                TokenDevPerson,
                TokenDevResultPerson,
                "{=VividWorld_Dev_KnownEvents}(dev) What do you know?",
                Condition_AlwaysAvailable,
                Consequence_KnownEvents,
                DevDialoguePriority);

            // 2. (dev) Who can you reach right now?
            starter.AddPlayerLine(
                "vividworld_dev_contacts",
                TokenDevPerson,
                TokenDevResultPerson,
                "{=VividWorld_Dev_Contacts}(dev) Who can you reach right now?",
                Condition_AlwaysAvailable,
                Consequence_Contacts,
                DevDialoguePriority);

            // 3. (dev) Who else knows about this?
            starter.AddPlayerLine(
                "vividworld_dev_event_roster",
                TokenDevPerson,
                TokenDevResultPerson,
                "{=VividWorld_Dev_EventRoster}(dev) Who else knows about this?",
                Condition_AlwaysAvailable,
                Consequence_EventRoster,
                DevDialoguePriority);

            // 4. (dev) His ledger of grudges
            starter.AddPlayerLine(
                "vividworld_dev_grudge_ledger",
                TokenDevPerson,
                TokenDevResultPerson,
                "{=VividWorld_Dev_GrudgeLedger}(dev) His ledger of grudges",
                Condition_AlwaysAvailable,
                Consequence_GrudgeLedger,
                DevDialoguePriority);

            // 5. (dev) What the rumors he heard changed
            starter.AddPlayerLine(
                "vividworld_dev_opinion_shifts",
                TokenDevPerson,
                TokenDevResultPerson,
                "{=VividWorld_Dev_OpinionShifts}(dev) What the rumors he heard changed",
                Condition_AlwaysAvailable,
                Consequence_OpinionShifts,
                DevDialoguePriority);

            // 6. (dev) What does this NPC remember?
            starter.AddPlayerLine(
                "vividworld_dev_npc_recall",
                TokenDevPerson,
                TokenDevResultPerson,
                "{=VividWorld_Dev_NpcRecall}(dev) What does this NPC remember?",
                Condition_AlwaysAvailable,
                Consequence_NpcRecall,
                DevDialoguePriority);

            // 7. (dev) AI integration: what would be handed over for this NPC
            starter.AddPlayerLine(
                "vividworld_dev_ai_push_preview",
                TokenDevPerson,
                TokenDevResultPerson,
                "{=VividWorld_Dev_AiPushPreview}(dev) AI integration: what would be handed over for this NPC",
                Condition_AlwaysAvailable,
                Consequence_AiPushPreview,
                DevDialoguePriority);

            // 8. (dev) What would he add to each rumor he knows?
            starter.AddPlayerLine(
                "vividworld_dev_feelings",
                TokenDevPerson,
                TokenDevResultPerson,
                "{=VividWorld_Dev_Feelings}(dev) What would he add to each rumor he knows?",
                Condition_AlwaysAvailable,
                Consequence_Feelings,
                DevDialoguePriority);

            // 9. (dev) How does he feel about me, per person
            starter.AddPlayerLine(
                "vividworld_dev_player_relation",
                TokenDevPerson,
                TokenDevResultPerson,
                "{=VividWorld_Dev_PlayerRelation}(dev) His relation with you",
                Condition_AlwaysAvailable,
                Consequence_PlayerRelation,
                DevDialoguePriority);

            // Return from person tools to categories
            starter.AddPlayerLine(
                "vividworld_dev_back_person",
                TokenDevPerson,
                TokenDevCategoriesPrompt,
                "{=VividWorld_Dev_Menu_Back}(dev) Back",
                null,
                null,
                DevDialoguePriority);

            starter.AddDialogLine(
                "vividworld_dev_result_person_line",
                TokenDevResultPerson,
                TokenDevPerson,
                "{=!}{VIVIDWORLD_DEV_RESULT}",
                null,
                null,
                100,
                null);

            // ── Category 2: About the world (8 tools) ──
            // 1. (dev) World status
            starter.AddPlayerLine(
                "vividworld_dev_world_status",
                TokenDevWorld,
                TokenDevResultWorld,
                "{=VividWorld_Dev_WorldStatus}(dev) World status",
                Condition_AlwaysAvailable,
                Consequence_WorldStatus,
                DevDialoguePriority);

            // 2. (dev) Event catalog
            starter.AddPlayerLine(
                "vividworld_dev_event_catalog",
                TokenDevWorld,
                TokenDevResultWorld,
                "{=VividWorld_Dev_EventCatalog}(dev) Event catalog",
                Condition_AlwaysAvailable,
                Consequence_EventCatalog,
                DevDialoguePriority);

            // 3. (dev) Today's situation scan
            starter.AddPlayerLine(
                "vividworld_dev_scan_today",
                TokenDevWorld,
                TokenDevResultWorld,
                "{=VividWorld_Dev_ScanToday}(dev) Today's situation scan",
                Condition_AlwaysAvailable,
                Consequence_ScanToday,
                DevDialoguePriority);

            // 4. (dev) Snapshot state
            starter.AddPlayerLine(
                "vividworld_dev_snapshots",
                TokenDevWorld,
                TokenDevResultWorld,
                "{=VividWorld_Dev_Snapshots}(dev) Snapshot state",
                Condition_AlwaysAvailable,
                Consequence_Snapshots,
                DevDialoguePriority);

            // 5. (dev) Why do I hear so little?
            starter.AddPlayerLine(
                "vividworld_dev_why_hear_few",
                TokenDevWorld,
                TokenDevResultWorld,
                "{=VividWorld_Dev_WhyHearFew}(dev) Why do I hear so little?",
                Condition_AlwaysAvailable,
                Consequence_WhyHearFew,
                DevDialoguePriority);

            // 6. (dev) What happens if I talk to everyone right now?
            starter.AddPlayerLine(
                "vividworld_dev_preview_talk_all",
                TokenDevWorld,
                TokenDevResultWorld,
                "{=VividWorld_Dev_PreviewTalkAll}(dev) What happens if I talk to everyone right now?",
                Condition_AlwaysAvailable,
                Consequence_PreviewTalkAll,
                DevDialoguePriority);

            // 7. (dev) Reload config.json
            starter.AddPlayerLine(
                "vividworld_dev_reload_config",
                TokenDevWorld,
                TokenDevResultWorld,
                "{=VividWorld_Dev_ReloadConfig}(dev) Reload config.json",
                Condition_AlwaysAvailable,
                Consequence_ReloadConfig,
                DevDialoguePriority);

            // 8. (dev) Reset performance counters
            starter.AddPlayerLine(
                "vividworld_dev_reset_metrics",
                TokenDevWorld,
                TokenDevResultWorld,
                "{=VividWorld_Dev_ResetMetrics}(dev) Reset performance counters",
                Condition_AlwaysAvailable,
                Consequence_ResetMetrics,
                DevDialoguePriority);

            // Return from world tools to categories
            starter.AddPlayerLine(
                "vividworld_dev_back_world",
                TokenDevWorld,
                TokenDevCategoriesPrompt,
                "{=VividWorld_Dev_Menu_Back}(dev) Back",
                null,
                null,
                DevDialoguePriority);

            starter.AddDialogLine(
                "vividworld_dev_result_world_line",
                TokenDevResultWorld,
                TokenDevWorld,
                "{=!}{VIVIDWORLD_DEV_RESULT}",
                null,
                null,
                100,
                null);

            // ── Category 3: Actions and injections (8 tools) ──
            // 1. (dev) Stage a public incident between us
            starter.AddPlayerLine(
                "vividworld_dev_inject_public",
                TokenDevAct,
                TokenDevResultAct,
                "{=VividWorld_Dev_InjectPublic}(dev) Stage a public incident between us",
                Condition_AllowInjection,
                Consequence_InjectPublic,
                DevDialoguePriority);

            // 2. (dev) Stage a secret between us
            starter.AddPlayerLine(
                "vividworld_dev_inject_secret",
                TokenDevAct,
                TokenDevResultAct,
                "{=VividWorld_Dev_InjectSecret}(dev) Stage a secret between us",
                Condition_AllowInjection,
                Consequence_InjectSecret,
                DevDialoguePriority);

            // 3. (dev) Force the newest secret to leak
            starter.AddPlayerLine(
                "vividworld_dev_force_leak",
                TokenDevAct,
                TokenDevResultAct,
                "{=VividWorld_Dev_ForceLeak}(dev) Force the newest secret to leak",
                Condition_AllowInjection,
                Consequence_ForceLeak,
                DevDialoguePriority);

            // 4. (dev) Make this person like me more
            starter.AddPlayerLine(
                "vividworld_dev_boost_relation",
                TokenDevAct,
                TokenDevResultAct,
                "{=VividWorld_Dev_BoostRelation}(dev) Make this person like me more",
                Condition_AllowInjection,
                Consequence_BoostRelation,
                DevDialoguePriority);

            // 5. (dev) Trigger a situation here
            starter.AddPlayerLine(
                "vividworld_dev_trigger_situation",
                TokenDevAct,
                TokenDevResultAct,
                "{=VividWorld_Dev_TriggerSituation}(dev) Trigger a situation here",
                Condition_AllowInjection,
                Consequence_TriggerSituation,
                DevDialoguePriority);

            // 6. (dev) Simulate 24h of propagation
            starter.AddPlayerLine(
                "vividworld_dev_tick_24h",
                TokenDevAct,
                TokenDevResultAct,
                "{=VividWorld_Dev_Tick24h}(dev) Simulate 24h of propagation",
                Condition_AlwaysAvailable,
                Consequence_Tick24h,
                DevDialoguePriority);

            // 7. (dev) Have closest bandits capture this NPC.
            // Ends the conversation instead of returning to main options: once the conversation hero is a
            // bandit prisoner, a native main-option condition reads the captor party's owner clan, which
            // bandit parties do not have, and the conversation freezes.
            starter.AddPlayerLine(
                "vividworld_dev_capture_by_bandits",
                TokenDevAct,
                TokenDevCaptureResult,
                "{=VividWorld_Dev_CaptureByBandits}(dev) Have the closest bandits capture this person",
                Condition_AllowInjection,
                Consequence_CaptureByBandits,
                DevDialoguePriority);

            starter.AddDialogLine(
                "vividworld_dev_capture_result_line",
                TokenDevCaptureResult,
                TokenCloseWindow,
                "{=!}{VIVIDWORLD_DEV_RESULT}",
                null,
                null,
                100,
                null);

            // 8. (dev) Have him tell me a rumor with feeling
            starter.AddPlayerLine(
                "vividworld_dev_tell_with_feeling",
                TokenDevAct,
                TokenDevResultAct,
                "{=VividWorld_Dev_TellWithFeeling}(dev) Have him tell me a rumor with feeling",
                Condition_AllowInjection,
                Consequence_TellWithFeeling,
                DevDialoguePriority);

            // Return from action tools to categories
            starter.AddPlayerLine(
                "vividworld_dev_back_act",
                TokenDevAct,
                TokenDevCategoriesPrompt,
                "{=VividWorld_Dev_Menu_Back}(dev) Back",
                null,
                null,
                DevDialoguePriority);

            starter.AddDialogLine(
                "vividworld_dev_result_act_line",
                TokenDevResultAct,
                TokenDevAct,
                "{=!}{VIVIDWORLD_DEV_RESULT}",
                null,
                null,
                100,
                null);

            ModLog.Info("Registered 25 developer dialogue lines in 3 categories (person: 9, world: 8, act: 8) behind main menu entry.");
        }

        private static void SetResult(string line)
        {
            MBTextManager.SetTextVariable("VIVIDWORLD_DEV_RESULT", line, false);
        }

        private static void Finish(string result)
        {
            SetResult(result);
            ModLog.Flush();
        }

        private bool Condition_AlwaysAvailable()
        {
            // Condition gate: re-checks current config every time dialogue opens,
            // allowing '(dev) Reload config' to immediately hide dialogue lines when DebugDialogueEnabled is set to false.
            if (!_config.Debug.DebugDialogueEnabled) return false;
            var hero = Hero.OneToOneConversationHero;
            return hero != null && hero.IsAlive && !hero.IsPrisoner;
        }

        private bool Condition_AllowInjection()
        {
            return Condition_AlwaysAvailable() && _config.Debug.AllowTestEventInjection;
        }

        private void Consequence_KnownEvents()
        {
            SetResult("(dev) no result - see log.txt");
            try
            {
                var hero = Hero.OneToOneConversationHero;
                if (hero == null) return;

                string report = DevReport.FormatKnownEvents(hero, _eventStore.KnownBy, _eventStore, _scheduler);
                InformationManager.DisplayMessage(new InformationMessage(report));
                ModLog.Info($"[DevDialogue]\n{report}");

                string summary = DevReport.FormatKnownEventsSummary(hero, _eventStore.KnownBy, _eventStore);
                Finish(summary);
            }
            catch (Exception ex)
            {
                ModLog.Error("Dev known events dump failed", ex);
                Finish("(dev) failed - see log.txt");
            }
        }

        private void Consequence_Contacts()
        {
            SetResult("(dev) no result - see log.txt");
            try
            {
                var hero = Hero.OneToOneConversationHero;
                if (hero == null) return;

                string report = DevReport.FormatContactMultipliers(hero, _channel, _config);
                InformationManager.DisplayMessage(new InformationMessage(report));
                ModLog.Info($"[DevDialogue]\n{report}");

                string summary = DevReport.FormatContactSummary(hero, _channel);
                Finish(summary);
            }
            catch (Exception ex)
            {
                ModLog.Error("Dev contacts dump failed", ex);
                Finish("(dev) failed - see log.txt");
            }
        }

        private void Consequence_InjectPublic()
        {
            SetResult("(dev) no result - see log.txt");
            try
            {
                if (!_config.Debug.AllowTestEventInjection)
                {
                    Finish("(dev) injection rejected: allowTestEventInjection is false");
                    return;
                }

                var hero = Hero.OneToOneConversationHero;
                var player = Hero.MainHero;
                if (hero == null || player == null) return;

                double day = CampaignTime.Now.ToDays;
                var sub = DevEventFactory.CreatePublicTestEvent(player.StringId, hero.StringId, day);
                var ingestResult = _eventStore.Submit(sub, out var eventId, out var rejectionReason);
                if (ingestResult == IngestResult.Accepted || ingestResult == IngestResult.Deferred)
                {
                    string msg = $"[VW] Injected public test event: {eventId} (result: {ingestResult})";
                    InformationManager.DisplayMessage(new InformationMessage(msg));
                    ModLog.Info($"[DevDialogue] {msg}");

                    var evt = eventId != null ? _eventStore.Load(eventId) : null;
                    string drama = evt != null
                        ? DramaScales.Describe(evt.DramaWeight, evt.DramaScale)
                        : (sub.DramaWeight.HasValue
                            ? DramaScales.Describe(sub.DramaWeight.Value, sub.DramaScale)
                            : "weight (config default)");
                    Finish($"(dev) injected {eventId} (public, {drama})");
                    _dialogs?.InvalidateCache();
                }
                else
                {
                    string reason = rejectionReason ?? "unknown reason";
                    string msg = $"[VW] Public test event rejected: {reason} (result: {ingestResult})";
                    InformationManager.DisplayMessage(new InformationMessage(msg));
                    ModLog.Warn($"[DevDialogue] {msg}");
                    Finish($"(dev) injection rejected: {reason}");
                }
            }
            catch (Exception ex)
            {
                ModLog.Error("Dev inject public failed", ex);
                Finish("(dev) failed - see log.txt");
            }
        }

        private void Consequence_InjectSecret()
        {
            SetResult("(dev) no result - see log.txt");
            try
            {
                if (!_config.Debug.AllowTestEventInjection)
                {
                    Finish("(dev) injection rejected: allowTestEventInjection is false");
                    return;
                }

                var hero = Hero.OneToOneConversationHero;
                var player = Hero.MainHero;
                if (hero == null || player == null) return;

                double day = CampaignTime.Now.ToDays;
                var sub = DevEventFactory.CreateSecretTestEvent(player.StringId, hero.StringId, day);
                var ingestResult = _eventStore.Submit(sub, out var eventId, out var rejectionReason);
                if (ingestResult == IngestResult.Accepted || ingestResult == IngestResult.Deferred)
                {
                    string msg = $"[VW] Injected secret test event: {eventId} (result: {ingestResult})";
                    InformationManager.DisplayMessage(new InformationMessage(msg));
                    ModLog.Info($"[DevDialogue] {msg}");

                    var evt = eventId != null ? _eventStore.Load(eventId) : null;
                    int insiders = evt?.KnownBy?.Count ?? sub.KnowingRoles?.Count ?? 0;
                    string insiderLabel = insiders == 1 ? "insider" : "insiders";
                    Finish($"(dev) injected {eventId} (secret, {insiders} {insiderLabel})");
                    _dialogs?.InvalidateCache();
                }
                else
                {
                    string reason = rejectionReason ?? "unknown reason";
                    string msg = $"[VW] Secret test event rejected: {reason} (result: {ingestResult})";
                    InformationManager.DisplayMessage(new InformationMessage(msg));
                    ModLog.Warn($"[DevDialogue] {msg}");
                    Finish($"(dev) injection rejected: {reason}");
                }
            }
            catch (Exception ex)
            {
                ModLog.Error("Dev inject secret failed", ex);
                Finish("(dev) failed - see log.txt");
            }
        }

        private void Consequence_Tick24h()
        {
            SetResult("(dev) no result - see log.txt");
            try
            {
                double nowDay = CampaignTime.Now.ToDays;
                int nowHour = CampaignTime.Now.GetHourOfDay;

                double startDay = nowDay;
                int startHour = nowHour;

                if (_lastSimulatedDay >= nowDay)
                {
                    startDay = _lastSimulatedDay;
                    startHour = _lastSimulatedHour;
                }

                string summary = ExecuteDevFastForward(startDay, startHour, 24);
                Finish(summary);
            }
            catch (Exception ex)
            {
                ModLog.Error("Dev tick 24h failed", ex);
                Finish("(dev) failed - see log.txt");
            }
        }

        /// <summary>
        /// Executes fast-forward propagation simulation for developer tuning.
        /// 
        /// Trade-off and caveats:
        /// Simulation advances State.LastPropagatedDay, LearnedDay, and LeakedDay ahead of
        /// actual CampaignTime (by up to hours / 24.0 days). As real in-game time catches up
        /// through normal gameplay, all timestamps resynchronize seamlessly.
        /// This is an acceptable compromise for dev tools to test propagation and leak decay
        /// without forcing game clock advancement. Production tick paths (OnHourlyTick/OnDailyTick)
        /// never apply forward day offsets.
        /// </summary>
        private string ExecuteDevFastForward(double startDay, int startHour, int hours)
        {
            if (_scheduler == null || _eventStore == null) return "(dev) scheduler unavailable";

            int initialKnowers = _eventStore.Index?.Entries?.Sum(e => e.KnownByHeroIds?.Count ?? 0) ?? 0;
            int initialLeaks = _eventStore.Index?.Entries?.Count(e => e.Leaked) ?? 0;

            int dailyTicks = 0;
            for (int h = 0; h < hours; h++)
            {
                double simDay = startDay + (h / 24.0);
                int simHour = (startHour + h) % 24;

                _scheduler.HourlyTick(simDay, simHour);

                // Daily tick triggered when crossing midnight, consistent with real tick cadence
                if (simHour == 23)
                {
                    _scheduler.DailyTick(simDay);
                    dailyTicks++;
                }
            }

            _lastSimulatedDay = startDay + (hours / 24.0);
            _lastSimulatedHour = (startHour + hours) % 24;

            int finalKnowers = _eventStore.Index?.Entries?.Sum(e => e.KnownByHeroIds?.Count ?? 0) ?? 0;
            int finalLeaks = _eventStore.Index?.Entries?.Count(e => e.Leaked) ?? 0;
            int newKnowers = Math.Max(0, finalKnowers - initialKnowers);
            int newLeaks = Math.Max(0, finalLeaks - initialLeaks);

            string knowerText = newKnowers == 1 ? "1 new knower" : $"{newKnowers} new knowers";
            string leakText = newLeaks == 1 ? "1 leak" : $"{newLeaks} leaks";
            string summary = string.Format(CultureInfo.InvariantCulture,
                "(dev) simulated {0}h: {1} hourly, {2} daily - {3}, {4}",
                hours, hours, dailyTicks, knowerText, leakText);

            string logMsg = string.Format(CultureInfo.InvariantCulture,
                "[VW] Simulated {0}h ({1} hourly ticks, {2} daily ticks): {3}, {4}.",
                hours, hours, dailyTicks, knowerText, leakText);
            InformationManager.DisplayMessage(new InformationMessage(logMsg));
            ModLog.Info($"[DevDialogue] {logMsg}");

            return summary;
        }

        private void Consequence_ForceLeak()
        {
            SetResult("(dev) no result - see log.txt");
            try
            {
                if (!_config.Debug.AllowTestEventInjection)
                {
                    Finish("(dev) force leak rejected: allowTestEventInjection is false");
                    return;
                }

                var target = DevReport.FindNewestUnleakedSecret(_eventStore);
                if (target == null)
                {
                    string msg = "[VW] no unleaked secret found";
                    InformationManager.DisplayMessage(new InformationMessage(msg));
                    ModLog.Info($"[DevDialogue] {msg}");
                    Finish("(dev) no unleaked secret found");
                    return;
                }

                var evt = _eventStore.Load(target.EventId);
                if (evt == null)
                {
                    string msg = $"[VW] Could not load event '{target.EventId}' to force leak.";
                    InformationManager.DisplayMessage(new InformationMessage(msg));
                    ModLog.Warn($"[DevDialogue] {msg}");
                    Finish($"(dev) failed to load {target.EventId}");
                    return;
                }

                double nowDay = CampaignTime.Now.ToDays;
                double day = _lastSimulatedDay >= nowDay ? _lastSimulatedDay : nowDay;
                string playerHeroId = Hero.MainHero?.StringId ?? "player";
                var firstKnower = evt.KnownBy.FirstOrDefault(k => !string.IsNullOrEmpty(k.HeroId) && k.HeroId != playerHeroId)
                    ?? evt.KnownBy.FirstOrDefault();
                string leakerHeroId = firstKnower?.HeroId ?? string.Empty;

                evt.State.Leaked = true;
                evt.State.LeakedDay = day;
                evt.State.LeakerHeroId = leakerHeroId;

                _eventStore.Stamper?.RestampAfterLeak(evt);
                _eventStore.Upsert(evt);
                _scheduler.PromoteLeakedEvent(evt);
                WorldEventStore.TriggerPublicEventOccurred(evt);

                string successMsg = $"[VW] Forced secret '{evt.EventId}' to leak (leaker: {leakerHeroId}).";
                InformationManager.DisplayMessage(new InformationMessage(successMsg));
                ModLog.Info($"[DevDialogue] {successMsg}");
                Finish($"(dev) forced leak of {evt.EventId} - now in the propagation ring");
            }
            catch (Exception ex)
            {
                ModLog.Error("Dev force leak failed", ex);
                Finish("(dev) failed - see log.txt");
            }
        }

        private void Consequence_WorldStatus()
        {
            SetResult("(dev) no result - see log.txt");
            try
            {
                string? sharedSummary = _dialogs?.SharedTodaySummary;
                string lastVol = _dialogs?.LastVolunteerSessionInfo ?? "(none this session)";

                string? compatInfo = _dialogs?.CompatStatusLine;
                string? recoveryInfo = _dialogs?.VolunteerRecoveryStatusLine;
                var route = _dialogs?.LastConversationRoute;
                string report = DevReport.FormatWorldStatus(_scheduler, _eventStore, _config, _rollbackCount, _rollbackMaxDay, _launchDay, sharedSummary, lastVol, compatInfo, route, recoveryInfo, _realEvents, _eventStore.Stamper, _sessionState, _campaignId, _dialogs?.PlayerHeardLog);
                InformationManager.DisplayMessage(new InformationMessage(report));
                ModLog.Info($"[DevDialogue]\n{report}");
                // 每種消息渲染一次有好幾百行，只寫進日誌；放進上面那則畫面訊息會把訊息欄洗掉。
                ModLog.Info($"[DevDialogue] template renders\n{DevReport.FormatAllTemplatesRendered(_config)}");

                string summary = DevReport.FormatWorldStatusSummary(_scheduler, _eventStore);
                string metricsSummary = DevMetrics.Report();
                if (!string.IsNullOrEmpty(metricsSummary))
                {
                    summary = summary + "\n" + metricsSummary;
                }
                Finish(summary);
            }
            catch (Exception ex)
            {
                ModLog.Error("Dev world status failed", ex);
                Finish("(dev) failed - see log.txt");
            }
        }

        private void Consequence_ResetMetrics()
        {
            SetResult("(dev) no result - see log.txt");
            try
            {
                DevMetrics.Reset();
                string msg = "[VW] Performance counters reset.";
                InformationManager.DisplayMessage(new InformationMessage(msg));
                ModLog.Info($"[DevDialogue] {msg}");
                Finish("(dev) performance counters reset");
            }
            catch (Exception ex)
            {
                ModLog.Error("Dev reset metrics failed", ex);
                Finish("(dev) reset metrics failed - see log.txt");
            }
        }

        private void Consequence_ReloadConfig()
        {
            SetResult("(dev) no result - see log.txt");
            try
            {
                bool ok = ConfigStore.Reload(_config);
                string mcmStatus = VividWorld.Mcm.McmBridge.GetStatusString();
                string msg = ok
                    ? $"[VW] config.json reloaded successfully. {mcmStatus}"
                    : $"[VW] Failed to reload config.json, see log.txt for details. {mcmStatus}";
                InformationManager.DisplayMessage(new InformationMessage(msg));
                ModLog.Info($"[DevDialogue] {msg}");

                if (ok)
                {
                    Finish($"(dev) config reloaded - {mcmStatus}");
                }
                else
                {
                    Finish($"(dev) config reload failed - {mcmStatus} - see log.txt");
                }
            }
            catch (Exception ex)
            {
                ModLog.Error("Dev reload config failed", ex);
                Finish("(dev) config reload failed - see log.txt");
            }
        }

        private void Consequence_PlayerRelation()
        {
            SetResult("(dev) no result - see log.txt");
            try
            {
                var hero = Hero.OneToOneConversationHero;
                var player = Hero.MainHero;
                if (hero == null || player == null) return;

                string name = hero.Name?.ToString() ?? hero.StringId;
                int shown = player.GetRelation(hero);
                int personal = CharacterRelationManager.GetHeroRelation(player, hero);
                var leader = hero.Clan?.Leader;
                string clanLevel = leader == null
                    ? "no clan leader"
                    : $"{CharacterRelationManager.GetHeroRelation(player, leader)} (leader {leader.Name})";
                bool switchOn = SubModule.PersonalWithPlayerEnabled;
                string marker = _personalSeedMarker?.Invoke() ?? string.Empty;
                string markerText = string.IsNullOrEmpty(marker) ? "(not set)" : marker;

                string vwReads = switchOn
                    ? $"personal {personal}"
                    : $"between clans {shown}";

                string line = $"(dev) {name}: shown to you {shown}, personal {personal}, between clans {clanLevel}, " +
                              $"switch {(switchOn ? "on" : "off")}, VW reads: {vwReads}, seeded {markerText}, " +
                              $"pair patch applied={PlayerPairEffectiveHeroesPatch.Applied}, " +
                              $"leader-change patch applied={ClanLeaderChangeKeepsPlayerRelationPatch.Applied}";
                ModLog.Info($"[DevDialogue] relation with the player: {line}");
                Finish(line);
            }
            catch (Exception ex)
            {
                ModLog.Error("Dev player relation failed", ex);
                Finish("(dev) failed - see log.txt");
            }
        }

        private void Consequence_BoostRelation()
        {
            SetResult("(dev) no result - see log.txt");
            try
            {
                var hero = Hero.OneToOneConversationHero;
                var player = Hero.MainHero;
                if (hero == null || player == null) return;

                int before = hero.GetRelation(player);
                int asked = _config.Debug.DevRelationBoost;
                ChangeRelationAction.ApplyPlayerRelation(hero, asked, false, false);
                int after = hero.GetRelation(player);

                Finish($"(dev) relation {before} -> {after} (asked +{asked}, actual {after - before:+#;-#;0})");
                ModLog.Info($"[DevDialogue] relation boost on {hero.Name}: before={before} asked={asked} after={after} actual={after - before}");
                _dialogs?.InvalidateCache();
            }
            catch (Exception ex)
            {
                ModLog.Error("Dev boost relation failed", ex);
                Finish("(dev) failed - see log.txt");
            }
        }

        private void Consequence_EventRoster()
        {
            SetResult("(dev) no result - see log.txt");
            try
            {
                var hero = Hero.OneToOneConversationHero;
                if (hero == null) return;

                var knownEventIds = _eventStore.KnownBy.EventsKnownBy(hero.StringId, CampaignTime.Now.ToDays);
                if (knownEventIds == null || knownEventIds.Count == 0)
                {
                    string emptyMsg = $"(dev) {hero.Name} knows 0 events";
                    ModLog.Info($"[DevDialogue] {hero.Name} ({hero.StringId}) knows 0 events.");
                    Finish(emptyMsg);
                    return;
                }

                var loaded = new List<WorldEvent>();
                foreach (var eid in knownEventIds)
                {
                    var evt = _eventStore.Load(eid);
                    if (evt != null)
                    {
                        loaded.Add(evt);
                    }
                }

                if (loaded.Count == 0)
                {
                    Finish("(dev) no valid events found");
                    return;
                }

                string? pickedId = RosterPick.Choose(loaded);
                var bestEvent = loaded.FirstOrDefault(e => string.Equals(e.EventId, pickedId, StringComparison.Ordinal));
                if (bestEvent == null)
                {
                    Finish("(dev) no valid events found");
                    return;
                }

                int bestKnowerCount = bestEvent.KnownBy?.Count ?? 0;

                string rosterReport = DevReport.FormatEventRoster(bestEvent, _scheduler.Engine, _heroLookup);
                InformationManager.DisplayMessage(new InformationMessage($"[VW] (dev) {bestEvent.EventId} known by {bestKnowerCount} - roster in log"));
                ModLog.Info($"[DevDialogue]\n{rosterReport}");

                Finish($"(dev) {bestEvent.EventId} known by {bestKnowerCount} - roster in log");
            }
            catch (Exception ex)
            {
                ModLog.Error("Dev event roster dump failed", ex);
                Finish("(dev) failed - see log.txt");
            }
        }

        private void Consequence_EventCatalog()
        {
            SetResult("(dev) no result - see log.txt");
            try
            {
                var catalog = EventCatalogStore.Catalog;
                string path = EventCatalogStore.ActiveFilePath;
                int loaded = catalog.Templates.Count;
                int skipped = catalog.SkippedCount;
                int errors = catalog.Issues.Count(i => i.IsError);
                int warnings = catalog.Issues.Count(i => !i.IsError);

                var sb = new System.Text.StringBuilder();
                sb.AppendLine($"Event catalog: {path}");
                sb.AppendLine($"Loaded: {loaded}, Skipped: {skipped}, Errors: {errors}, Warnings: {warnings}");

                // 「載了 5 個」不算診斷，「載了哪 5 個」才算——MS2 接上真事件之後，
                // 第一個要問的就是「這個型別在不在目錄裡」。
                foreach (var t in catalog.Templates)
                {
                    string linked = string.IsNullOrEmpty(t.LinkedTemplateType) ? "-" : t.LinkedTemplateType!;
                    sb.AppendLine($"  {t.Type} | {t.Origin} | {(t.DramaWeight.HasValue ? DramaScales.Describe(t.DramaWeight.Value, t.DramaScale) + (t.DramaScale == DramaScales.Ten ? " base" : " (old 1..5 writing)") : "weight (config)")} | " +
                                  $"{t.Roles.Count} role(s) | {t.Facts.Count} fact(s) | linked: {linked}");
                }

                foreach (var issue in catalog.Issues)
                {
                    string status = issue.IsError ? "Error (skipped)" : "Warning (kept)";
                    sb.AppendLine($"  template[{issue.TemplateIndex}] '{issue.TemplateType}' {issue.Field} - {issue.Code}: {issue.Detail} [{status}]");
                }

                string fullReport = sb.ToString().TrimEnd();
                InformationManager.DisplayMessage(new InformationMessage(fullReport));
                ModLog.Info($"[DevDialogue]\n{fullReport}");

                string summary = $"(dev) catalog: loaded {loaded}, skipped {skipped} ({errors} errors, {warnings} warnings)";
                Finish(summary);
            }
            catch (Exception ex)
            {
                ModLog.Error("Dev event catalog dump failed", ex);
                Finish("(dev) failed - see log.txt");
            }
        }

        private void Consequence_TriggerSituation()
        {
            SetResult("(dev) no result - see log.txt");
            try
            {
                var partner = Hero.OneToOneConversationHero;
                if (partner == null)
                {
                    Finish("(dev) No conversation partner found.");
                    return;
                }

                var traits = _traitLookup ?? (_heroLookup != null ? new GameTraitLookup(_heroLookup) : null);
                if (traits == null || !Eligibility.IsEligible(traits, partner.StringId))
                {
                    string reason = EligibilityLabel.GetRejectionReason(partner, traits) ?? "ineligible";
                    Finish($"(dev) Partner {partner.Name} ({partner.StringId}) is ineligible: {reason}.");
                    return;
                }

                var settlement = partner.CurrentSettlement;
                if (settlement == null)
                {
                    Finish($"(dev) Partner {partner.Name} ({partner.StringId}) is not in a settlement.");
                    return;
                }

                var situations = SituationCatalogStore.Catalog.Situations;
                if (situations.Count == 0)
                {
                    Finish("(dev) No situations loaded in catalog.");
                    return;
                }

                double day = CampaignTime.Now.ToDays;
                var presentHeroes = EligibilityLabel.GetPresentHeroesAtSettlement(settlement);
                string? playerHeroId = Hero.MainHero?.StringId;

                var candidates = presentHeroes
                    .Where(h => h != null && !string.IsNullOrEmpty(h.StringId) &&
                                h != Hero.MainHero && h.StringId != playerHeroId &&
                                h != partner && h.StringId != partner.StringId &&
                                Eligibility.IsEligible(traits, h.StringId))
                    .OrderBy(h => h.StringId, StringComparer.Ordinal)
                    .ToList();

                bool situationStarted = false;
                SituationRunResult? successfulRunResult = null;
                SituationTemplate? matchedSituation = null;
                Dictionary<string, Hero>? matchedRoleHeroes = null;

                var rejectedLines = new List<string>();

                foreach (var situation in situations)
                {
                    var nonDerivedRoles = situation.Roles
                        .Where(r => !r.Value.IsDerived)
                        .Select(r => r.Key)
                        .ToList();

                    if (nonDerivedRoles.Count != 2)
                    {
                        continue;
                    }

                    // Try partner as each non-derived role
                    foreach (var pRole in nonDerivedRoles)
                    {
                        string cRole = nonDerivedRoles.First(r => r != pRole);

                        foreach (var cand in candidates)
                        {
                            var roleAssignments = new Dictionary<string, Hero>
                            {
                                [pRole] = partner,
                                [cRole] = cand
                            };

                            var runResult = SituationRunner.Run(
                                situation,
                                roleAssignments,
                                day,
                                "forced by dev",
                                _eventStore,
                                traits,
                                _config,
                                _heroLookup);

                            if (runResult.Started)
                            {
                                situationStarted = true;
                                successfulRunResult = runResult;
                                matchedSituation = situation;
                                matchedRoleHeroes = roleAssignments;
                                break;
                            }
                            else
                            {
                                var failedConds = runResult.ConditionResults.Where(c => !c.Ok).ToList();
                                string rejLine = SituationLogFormatter.FormatCandidateRejected(
                                    situation.Id,
                                    cRole,
                                    cand.StringId,
                                    failedConds);
                                rejectedLines.Add(rejLine);
                            }
                        }

                        if (situationStarted) break;
                    }

                    if (situationStarted) break;
                }

                // 卡片 §6.4 第 4 步：被排除的候選一律印，成功的情況也印——否則「為什麼挑中的是他」看不出來
                {
                    int maxCrowdedOut = _config.Debug.DevReportMaxCrowdedOut;
                    int linesToPrint = Math.Min(rejectedLines.Count, maxCrowdedOut);
                    for (int i = 0; i < linesToPrint; i++)
                    {
                        ModLog.Info(rejectedLines[i]);
                    }
                    if (rejectedLines.Count > maxCrowdedOut)
                    {
                        ModLog.Info($"  ... and {rejectedLines.Count - maxCrowdedOut} more candidate(s) rejected");
                    }
                }

                if (situationStarted && successfulRunResult != null && matchedSituation != null && matchedRoleHeroes != null)
                {
                    string eventOrRejected = successfulRunResult.EventIds.Count > 0
                        ? successfulRunResult.EventIds[0]
                        : "rejected";

                    var nonDerived = matchedSituation.Roles.Where(r => !r.Value.IsDerived).Select(r => r.Key).ToList();
                    string role1 = nonDerived[0];
                    string role2 = nonDerived[1];

                    string hero1Name = matchedRoleHeroes.TryGetValue(role1, out var h1) ? (h1?.Name?.ToString() ?? role1) : role1;
                    string hero2Name = matchedRoleHeroes.TryGetValue(role2, out var h2) ? (h2?.Name?.ToString() ?? role2) : role2;

                    string msg = $"(dev) {matchedSituation.Id}: {role1}={hero1Name}, {role2}={hero2Name} -> {successfulRunResult.SelectedBranchId ?? "none"} -> {eventOrRejected}. See log.txt";
                    Finish(msg);
                }
                else
                {
                    string msg = $"(dev) no situation could start here ({rejectedLines.Count} candidate pair(s) rejected). See log.txt";
                    Finish(msg);
                }
            }
            catch (Exception ex)
            {
                ModLog.Error("Dev trigger situation failed", ex);
                Finish("(dev) failed - see log.txt");
            }
        }

        private void Consequence_GrudgeLedger()
        {
            SetResult("(dev) no result - see log.txt");
            try
            {
                var partner = Hero.OneToOneConversationHero;
                if (partner == null)
                {
                    Finish("(dev) no conversation partner. See log.txt");
                    return;
                }

                string partnerName = partner.Name?.ToString() ?? partner.StringId;
                double day = CampaignTime.Now.ToDays;

                var partnerPairs = _eventStore.Grudges.Pairs
                    .Where(p => string.Equals(p.From, partner.StringId, StringComparison.Ordinal))
                    .ToList();

                if (partnerPairs.Count == 0)
                {
                    ModLog.Info(GrudgeLogFormatter.FormatReplayHeaderNoGrudges(partner.StringId, day));
                    Finish($"(dev) {partnerName} holds no grudges. See log.txt");
                    return;
                }

                ModLog.Info(GrudgeLogFormatter.FormatReplayHeader(partner.StringId, day, partnerPairs.Count));

                var summaryParts = new List<string>();
                foreach (var pair in partnerPairs)
                {
                    var entries = _eventStore.Grudges.Between(pair.From, pair.About, pair.Scope);
                    var traitProfile = _traitLookup?.Of(pair.From);
                    var replay = GrudgeDecay.Replay(entries, pair.Scope, _config.Situations, traitProfile, day);

                    var replayLines = GrudgeLogFormatter.FormatPairReplay(pair.About, pair.Scope, replay);
                    foreach (var line in replayLines)
                    {
                        ModLog.Info(line);
                    }

                    if (summaryParts.Count < 3)
                    {
                        var aboutHero = _heroLookup?.Get(pair.About) ?? Hero.Find(pair.About);
                        string aboutName = aboutHero?.Name?.ToString() ?? pair.About;
                        string scopeStr = pair.Scope == GrudgeScope.Personal ? "personal" : "clan";
                        summaryParts.Add(string.Format(CultureInfo.InvariantCulture, "{0} {1:0.0} vs {2}", scopeStr, replay.Value, aboutName));
                    }
                }

                string pairsSummary = string.Join(", ", summaryParts);
                if (partnerPairs.Count > 3)
                {
                    pairsSummary += $" and {partnerPairs.Count - 3} more";
                }

                Finish(string.Format(CultureInfo.InvariantCulture, "(dev) {0}: {1} pair(s) - {2}. See log.txt", partnerName, partnerPairs.Count, pairsSummary));
            }
            catch (Exception ex)
            {
                ModLog.Error("Dev grudge ledger failed", ex);
                Finish("(dev) failed - see log.txt");
            }
        }

        private void Consequence_ScanToday()
        {
            try
            {
                if (_situationScan == null)
                {
                    Finish("(dev) Situation scan behavior is not initialized. See log.txt");
                    return;
                }

                var scanResult = _situationScan.ExecuteScan(isDryRun: true);
                if (scanResult.Disabled)
                {
                    Finish("(dev) Scan is disabled (situations.dailyScanEnabled = false). See log.txt");
                    return;
                }

                if (scanResult.CandidatesPassed > 0)
                {
                    string triggerPart = !string.IsNullOrEmpty(scanResult.WouldTriggerSummary)
                        ? $"Would trigger: {scanResult.WouldTriggerSummary}. "
                        : string.Empty;

                    Finish(string.Format(CultureInfo.InvariantCulture,
                        "(dev) Scan: {0} settlement(s) with pairs, {1} pair(s), {2} candidate(s) passed, quota {3}. {4}Nothing was triggered. See log.txt",
                        scanResult.SettlementsWithPairs,
                        scanResult.PairsEvaluated,
                        scanResult.CandidatesPassed,
                        scanResult.Quota,
                        triggerPart));
                }
                else
                {
                    string blockersPart = !string.IsNullOrEmpty(scanResult.TopBlockersSummary)
                        ? $" Top blockers: {scanResult.TopBlockersSummary}."
                        : string.Empty;

                    Finish(string.Format(CultureInfo.InvariantCulture,
                        "(dev) Scan: {0} settlement(s) with pairs, {1} pair(s), 0 candidate(s) passed.{2} See log.txt",
                        scanResult.SettlementsWithPairs,
                        scanResult.PairsEvaluated,
                        blockersPart));
                }
            }
            catch (Exception ex)
            {
                ModLog.Error("Dev scan today failed", ex);
                Finish("(dev) failed - see log.txt");
            }
        }

        private void Consequence_Snapshots()
        {
            SetResult("(dev) no result - see log.txt");
            try
            {
                string campaignId = string.IsNullOrEmpty(_campaignId) ? "legacy" : _campaignId!;
                string campaignRoot = VividWorldPaths.CampaignDirectory(campaignId);
                var inv = RumorSnapshotStore.Inventory(campaignRoot);
                string report = SnapshotLogFormatter.FormatInventory(
                    inv,
                    campaignId,
                    _sessionState?.LastRestore?.Token,
                    _sessionState?.LastTake,
                    _sessionState?.LastRestore,
                    _config.Persistence.MaxSnapshots);

                InformationManager.DisplayMessage(new InformationMessage(report));
                ModLog.Info($"[DevDialogue]\n{report}");

                int inUse = inv.Slots.Count;
                int max = _config.Persistence.MaxSnapshots;
                long kb = inv.TotalBytes / 1024;
                Finish(string.Format(CultureInfo.InvariantCulture, "(dev) snapshots: {0}/{1} kept ({2} KB) - see log.txt", inUse, max, kb));
            }
            catch (Exception ex)
            {
                ModLog.Error("Dev snapshot inventory dump failed", ex);
                Finish("(dev) failed - see log.txt");
            }
        }

        private void Consequence_OpinionShifts()
        {
            SetResult("(dev) no result - see log.txt");
            try
            {
                var hero = Hero.OneToOneConversationHero;
                if (hero == null)
                {
                    Finish("(dev) no active conversation hero.");
                    return;
                }

                var store = _eventStore;
                if (store == null)
                {
                    Finish("(dev) event store not initialized.");
                    return;
                }

                double now = CampaignTime.Now.ToDays;
                string report = DevReport.FormatOpinionShifts(
                    hero.StringId, store, _scheduler?.DailyRelationBudget,
                    _config, now);
                InformationManager.DisplayMessage(new InformationMessage(report));
                ModLog.Info($"[DevDialogue]\n{report}");
                Finish(report);
            }
            catch (Exception ex)
            {
                ModLog.Error("Dev opinion shifts failed", ex);
                Finish("(dev) failed - see log.txt");
            }
        }

        private void Consequence_WhyHearFew()
        {
            SetResult("(dev) no result - see log.txt");
            try
            {
                var tally = _dialogs?.ListenTally;
                string filePath = !string.IsNullOrEmpty(_campaignId)
                    ? VividWorldPaths.ListenTallyFile(_campaignId!)
                    : "(no campaign id)";

                if (tally == null)
                {
                    string emptyMsg = $"Listen tally is not active or empty. File: {filePath}";
                    InformationManager.DisplayMessage(new InformationMessage(emptyMsg));
                    ModLog.Info($"[DevDialogue] {emptyMsg}");
                    Finish("(dev) listen tally empty or inactive");
                    return;
                }

                int currentDay = (int)CampaignTime.Now.ToDays;
                string report = ListenTallyLogFormatter.FormatDevReport(tally, currentDay, filePath);
                InformationManager.DisplayMessage(new InformationMessage(report));
                ModLog.Info($"[DevDialogue]\n{report}");

                string summary = $"Listen tally: {tally.Days.Count} day(s) recorded, file: {Path.GetFileName(filePath)}";
                Finish(summary);
            }
            catch (Exception ex)
            {
                ModLog.Error("Dev why hear few failed", ex);
                Finish("(dev) failed - see log.txt");
            }
        }

        private void Consequence_PreviewTalkAll()
        {
            SetResult("(dev) no result - see log.txt");
            try
            {
                if (_dialogs == null)
                {
                    Finish("(dev) rumor dialog behavior unavailable");
                    return;
                }

                var result = _dialogs.RunPreview(includeHeroDetails: true);
                string summary = ListenPreviewLogFormatter.FormatSummary(result, includeTopNames: true);
                string table = ListenPreviewLogFormatter.FormatHeroTable(result);

                InformationManager.DisplayMessage(new InformationMessage(summary));
                ModLog.Info($"[DevDialogue]\n{summary}");
                ModLog.Info($"[DevDialogue]\n{table}");

                string shortSummary = $"(dev) preview: {result.TotalNetworkCount} heroes in network | {result.ElapsedMilliseconds}ms - see log.txt for full table";
                Finish(shortSummary);
            }
            catch (Exception ex)
            {
                ModLog.Error("Dev preview talk all failed", ex);
                Finish("(dev) failed - see log.txt");
            }
        }

        private void Consequence_NpcRecall()
        {
            SetResult("(dev) no result - see log.txt");
            try
            {
                var hero = Hero.OneToOneConversationHero;
                if (hero == null) return;

                double currentDay = CampaignTime.Now.ToDays;
                var eventIds = _eventStore.KnownBy.EventsKnownBy(hero.StringId, currentDay);
                var candidateEvents = new List<WorldEvent>(eventIds.Count);
                foreach (var id in eventIds)
                {
                    var evt = _eventStore.Load(id);
                    if (evt != null) candidateEvents.Add(evt);
                }

                var knownSet = new HashSet<string>(eventIds, StringComparer.OrdinalIgnoreCase);
                var retention = FactRetentionPolicies.Create(_config, new SplitMix64Rng(), _eventStore.CampaignSeed);

                var result = NpcRecallQuery.Query(
                    hero.StringId,
                    currentDay,
                    maxCount: 20,
                    candidateEvents,
                    retention,
                    isEventKnown: id => knownSet.Contains(id),
                    config: _config,
                    getTemplate: EventCatalogStore.TemplateByType,
                    traits: _traitLookup);

                string report = DevReport.FormatNpcRecall(hero, result);
                InformationManager.DisplayMessage(new InformationMessage(report));
                ModLog.Info($"[DevDialogue]\n{report}");

                string summary = string.Format(CultureInfo.InvariantCulture,
                    "(dev) {0} remembers {1} / {2} candidates ({3} excluded). See log.txt.",
                    hero.Name, result.Items.Count, result.CandidateCount, result.Exclusions.Count);
                Finish(summary);
            }
            catch (Exception ex)
            {
                ModLog.Error("Dev NPC recall dump failed", ex);
                Finish("(dev) failed - see log.txt");
            }
        }

        /// <summary>
        /// 對話對象對他知道的每一則消息：焦點人物、好感／恩怨／地位怎麼算、會講的感想那一句。
        /// 只讀，不改任何好感、恩怨或紀錄；一律照「完整分享講給玩家聽」算，不看他現在對玩家的交情夠不夠。
        /// </summary>
        private void Consequence_Feelings()
        {
            SetResult("(dev) no result - see log.txt");
            try
            {
                var hero = Hero.OneToOneConversationHero;
                if (hero == null)
                {
                    Finish("(dev) no conversation partner. See log.txt");
                    return;
                }

                string heroName = hero.Name?.ToString() ?? hero.StringId;
                if (_feelings == null)
                {
                    ModLog.Info("[DevDialogue] feelings: the feeling resolver is not available in this session.");
                    Finish("(dev) feelings are not available in this session. See log.txt");
                    return;
                }

                double day = CampaignTime.Now.ToDays;
                var eventIds = _eventStore.KnownBy.EventsKnownBy(hero.StringId, day);
                var sb = new System.Text.StringBuilder();
                sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                    "[DevDialogue] feelings {0} ({1}) on day {2:F1}: {3} known event(s), each computed as a full share to the player (read-only)",
                    heroName, hero.StringId, day, eventIds.Count));

                int applied = 0, none = 0, missing = 0;
                foreach (var id in eventIds)
                {
                    var evt = _eventStore.Load(id);
                    if (evt == null) { missing++; continue; }

                    var decision = _feelings.Resolve(evt, hero.StringId, isGist: false);
                    sb.AppendLine("  " + decision.LogLine);
                    if (decision.Applied)
                    {
                        applied++;
                        string? said = FallbackTextRenderer.RenderFeelingLine(decision, speakerHeroId: hero.StringId);
                        sb.AppendLine("    would say: " + (said ?? "(the string table has no text for this line)"));
                    }
                    else
                    {
                        none++;
                    }
                }

                ModLog.Info(sb.ToString().TrimEnd());
                Finish(string.Format(CultureInfo.InvariantCulture,
                    "(dev) {0}: feeling for {1} of {2} known rumor(s), none for {3}{4}. See log.txt",
                    heroName, applied, eventIds.Count, none, missing > 0 ? $", {missing} not loadable" : string.Empty));
            }
            catch (Exception ex)
            {
                ModLog.Error("Dev feelings dump failed", ex);
                Finish("(dev) failed - see log.txt");
            }
        }

        private void Consequence_TellWithFeeling()
        {
            SetResult("(dev) no result - see log.txt");
            try
            {
                var hero = Hero.OneToOneConversationHero;
                if (hero == null)
                {
                    Finish("(dev) no conversation partner. See log.txt");
                    return;
                }

                string heroName = hero.Name?.ToString() ?? hero.StringId;
                string? playerHeroId = Hero.MainHero?.StringId;
                if (string.IsNullOrEmpty(playerHeroId))
                {
                    Finish("(dev) main hero is not available. See log.txt");
                    return;
                }

                if (_feelings == null)
                {
                    ModLog.Info("[DevDialogue] tell with feeling: feelings resolver is not available in this session.");
                    Finish("(dev) feelings are not available in this session. See log.txt");
                    return;
                }

                if (_dialogs == null || _dialogs.OfferSelector == null)
                {
                    ModLog.Info("[DevDialogue] tell with feeling: rumor dialog behavior is not available in this session.");
                    Finish("(dev) rumor dialog behavior is not available in this session. See log.txt");
                    return;
                }

                double day = CampaignTime.Now.ToDays;
                var eventIds = _eventStore.KnownBy.EventsKnownBy(hero.StringId, day);

                ModLog.Info($"[DevDialogue] forced feeling tell for {heroName} ({hero.StringId}) on day {day:F1}: {eventIds.Count} known event(s)");

                var pickResult = ForcedFeelingRumorPicker.Pick(
                    eventIds,
                    id => _eventStore.Load(id),
                    hero.StringId,
                    playerHeroId!,
                    day,
                    _config.Memory,
                    (evt, speakerId) => _feelings.Resolve(evt, speakerId, isGist: false),
                    id => _dialogs.PlayerHeardLog != null && _dialogs.PlayerHeardLog.Contains(id));

                foreach (var eval in pickResult.Evaluations)
                {
                    string detail = !string.IsNullOrEmpty(eval.Reason) ? $" ({eval.Reason})" : string.Empty;
                    ModLog.Info($"  [{eval.Status}] {eval.EventId}{detail}");
                }

                if (pickResult.ChosenEvent == null)
                {
                    string noRumorMsg = string.Format(
                        CultureInfo.InvariantCulture,
                        "(dev) {0}: no rumor with a feeling that you have not heard (known {1}: no feeling {2}, already heard {3}, forgotten {4}, not loadable {5})",
                        heroName,
                        pickResult.TotalKnown,
                        pickResult.NoFeelingCount,
                        pickResult.AlreadyHeardCount,
                        pickResult.ForgottenCount,
                        pickResult.NotLoadableCount);

                    Finish(noRumorMsg);
                    return;
                }

                var chosenEvent = pickResult.ChosenEvent;
                var tellerEntry = chosenEvent.EntryFor(hero.StringId);
                int tellerHop = tellerEntry?.Hop ?? 0;
                string? sourceHeroId = tellerEntry?.SourceHeroId;

                string? linkedId = chosenEvent.LinkedEventId;
                bool isCorrection = !string.IsNullOrEmpty(linkedId) &&
                    ((_dialogs.PlayerHeardLog != null && _dialogs.PlayerHeardLog.Contains(linkedId!)) ||
                     (_eventStore.KnownBy != null && _eventStore.KnownBy.EventsKnownBy(playerHeroId!, day).Contains(linkedId!)));

                var candidate = new RumorCandidate
                {
                    Event = chosenEvent,
                    TellerHop = tellerHop,
                    PlayerExistingHop = null,
                    InvolvesHeroPlayerCaresAbout = false,
                    SourceHeroId = sourceHeroId,
                    IsCorrection = isCorrection
                };

                var tellerProfile = _dialogs.BuildSocialProfile(hero);
                var offer = _dialogs.OfferSelector.BuildOffer(candidate, tellerProfile, day, VolunteerTier.Full);

                var renderResult = FallbackTextRenderer.RenderBoth(offer.Composed, _config.Presentation);

                _dialogs.ApplyOfferAndRecord(offer, chosenEvent, hero.StringId, day, heroName);

                string deliveredMsg = $"Rumor delivered to player: event {offer.EventId} (full, hop {offer.ResultingPlayerHop}, isRetell={offer.IsRetell}, heldBack={offer.HeldBack}, closing={offer.Composed.ClosingKey ?? "none"}) from {hero.Name} (triggered via dev command; does not count against daily share limit)";
                ModLog.Info(deliveredMsg);
                ModLog.Info($"  text shown: \"{renderResult.PlainText ?? renderResult.DisplayText}\"");
                RumorDialogBehavior.LogDeliveredPrefix(offer);
                RumorDialogBehavior.LogDeliveredFeeling(offer);

                Finish(renderResult.DisplayText);
            }
            catch (Exception ex)
            {
                ModLog.Error("Dev forced feeling tell failed", ex);
                Finish("(dev) failed - see log.txt");
            }
        }

        private void Consequence_AiPushPreview()
        {
            SetResult("(dev) no result - see log.txt");
            try
            {
                var hero = Hero.OneToOneConversationHero;
                if (hero == null) return;

                var previewData = AiPushCoordinator.GeneratePreview(hero, _eventStore, _config);
                string report = DevReport.FormatAiPushPreview(previewData);
                InformationManager.DisplayMessage(new InformationMessage(report));
                ModLog.Info($"[DevDialogue]\n{report}");

                string summary = previewData.RejectionReason != null
                    ? string.Format(CultureInfo.InvariantCulture, "(dev) {0} is ineligible: {1}. See log.txt.", hero.Name, previewData.RejectionReason)
                    : string.Format(CultureInfo.InvariantCulture, "(dev) {0}: {1} candidates. Planned: {2}. See log.txt.",
                        hero.Name,
                        previewData.CandidateCount,
                        string.Join(", ", previewData.Targets.Select(t => t.IsEnabled ? $"{t.TargetId}={t.PlannedPushes.Count}" : $"{t.TargetId}=(disabled)")));

                Finish(summary);
            }
            catch (Exception ex)
            {
                ModLog.Error("Dev AI push preview dump failed", ex);
                Finish("(dev) failed - see log.txt");
            }
        }

        private void Consequence_CaptureByBandits()
        {
            SetResult("(dev) no result - see log.txt");
            try
            {
                var target = Hero.OneToOneConversationHero;
                if (target == null)
                {
                    Finish("Dev: blocked - no conversation hero.");
                    return;
                }

                if (target == Hero.MainHero)
                {
                    Finish("Dev: blocked - target is player.");
                    return;
                }

                if (target.IsPrisoner)
                {
                    Finish("Dev: blocked - target is already a prisoner.");
                    return;
                }

                if (target.CurrentSettlement != null)
                {
                    Finish("Dev: blocked - target is in a settlement.");
                    return;
                }

                if (FindClosestIdleBanditParty() == null)
                {
                    Finish("Dev: blocked - no active bandit party outside battle.");
                    return;
                }

                // Capturing mid-conversation freezes the native main options (ledger D-98), and the player's
                // encounter with the target's party would be left pointing at a leaderless party. So end the
                // conversation, leave the encounter, and capture on the first tick where neither is active.
                _pendingBanditCapture = target;
                if (PlayerEncounter.Current != null) PlayerEncounter.LeaveEncounter = true;
                CampaignEvents.TickEvent.ClearListeners(this);
                CampaignEvents.TickEvent.AddNonSerializedListener(this, OnTickPendingBanditCapture);

                string msg = $"Dev: {target.StringId} will be taken by the closest bandits once this conversation closes.";
                ModLog.Info(msg);
                Finish(msg);
            }
            catch (Exception ex)
            {
                ModLog.Error("Dev capture by bandits failed", ex);
                Finish("(dev) failed - see log.txt");
            }
        }

        private void OnTickPendingBanditCapture(float dt)
        {
            var target = _pendingBanditCapture;
            if (target == null)
            {
                CampaignEvents.TickEvent.ClearListeners(this);
                return;
            }

            if (TaleWorlds.CampaignSystem.Campaign.Current?.ConversationManager?.IsConversationInProgress == true) return;
            if (PlayerEncounter.Current != null) return;

            CampaignEvents.TickEvent.ClearListeners(this);
            _pendingBanditCapture = null;

            try
            {
                string? blocked =
                    !target.IsAlive ? "target is dead"
                    : target.IsPrisoner ? "target is already a prisoner"
                    : target.CurrentSettlement != null ? "target entered a settlement"
                    : null;

                MobileParty? closestBandit = blocked == null ? FindClosestIdleBanditParty() : null;
                if (blocked == null && closestBandit == null) blocked = "no active bandit party outside battle";

                string msg;
                if (blocked != null)
                {
                    msg = $"Dev: capture of {target.StringId} skipped - {blocked}.";
                }
                else
                {
                    TakePrisonerAction.Apply(closestBandit!.Party, target);
                    string factionId = closestBandit.MapFaction?.StringId ?? "unknown";
                    msg = $"Dev: {target.StringId} taken by {closestBandit.Party.Id} ({factionId})";
                }

                ModLog.Info(msg);
                ModLog.Flush();
                InformationManager.DisplayMessage(new InformationMessage(msg));
            }
            catch (Exception ex)
            {
                ModLog.Error("Dev capture by bandits failed", ex);
                ModLog.Flush();
            }
        }

        private static MobileParty? FindClosestIdleBanditParty()
        {
            var playerParty = MobileParty.MainParty;
            if (playerParty == null) return null;

            var playerPos = playerParty.Position;
            MobileParty? closestBandit = null;
            float minDistanceSq = float.MaxValue;
            var banditParties = MobileParty.AllBanditParties;
            if (banditParties == null) return null;

            foreach (var bp in banditParties)
            {
                if (bp != null && bp.IsActive && bp.MapEvent == null)
                {
                    float distSq = bp.Position.DistanceSquared(playerPos);
                    if (distSq < minDistanceSq)
                    {
                        minDistanceSq = distSq;
                        closestBandit = bp;
                    }
                }
            }

            return closestBandit;
        }
    }
}
