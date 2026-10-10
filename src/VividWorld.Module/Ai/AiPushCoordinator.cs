using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using TaleWorlds.CampaignSystem;
using VividWorld.Campaign;
using VividWorld.Core.Ai;
using VividWorld.Core.Config;
using VividWorld.Core.Events;
using VividWorld.Core.Memory;
using VividWorld.Core.Persistence;
using VividWorld.Core.Presentation;
using VividWorld.Core.Rumors;
using VividWorld.Core.Util;
using VividWorld.Presentation;

namespace VividWorld.Ai
{
    public sealed class AiPushPreviewItem
    {
        public string EventId { get; }
        public string Version { get; }
        public string Text { get; }
        public int CharCount { get; }
        public bool IsTruncated { get; }
        public int DaysAgo { get; }
        public string Kind { get; }
        public bool AlreadyPushed { get; }

        public AiPushPreviewItem(string eventId, string version, string text, int charCount, bool isTruncated, int daysAgo, string kind, bool alreadyPushed)
        {
            EventId = eventId;
            Version = version;
            Text = text;
            CharCount = charCount;
            IsTruncated = isTruncated;
            DaysAgo = daysAgo;
            Kind = kind ?? AiMemoryKind.Other;
            AlreadyPushed = alreadyPushed;
        }
    }

    public sealed class AiTargetPreviewData
    {
        public string TargetId { get; }
        public bool IsEnabled { get; }
        public string? DisabledReason { get; }
        public bool IsPersistent { get; }
        public int MaxPerChat { get; }
        public IReadOnlyList<AiPushPreviewItem> PlannedPushes { get; }
        public IReadOnlyList<string> Exclusions { get; }
        public IReadOnlyDictionary<string, AiPushRecord> ExistingPushes { get; }

        public AiTargetPreviewData(
            string targetId,
            bool isEnabled,
            string? disabledReason,
            bool isPersistent,
            int maxPerChat,
            IReadOnlyList<AiPushPreviewItem> plannedPushes,
            IReadOnlyList<string> exclusions,
            IReadOnlyDictionary<string, AiPushRecord> existingPushes)
        {
            TargetId = targetId;
            IsEnabled = isEnabled;
            DisabledReason = disabledReason;
            IsPersistent = isPersistent;
            MaxPerChat = maxPerChat;
            PlannedPushes = plannedPushes ?? Array.Empty<AiPushPreviewItem>();
            Exclusions = exclusions ?? Array.Empty<string>();
            ExistingPushes = existingPushes ?? new Dictionary<string, AiPushRecord>();
        }
    }

    public sealed class AiPushPreviewData
    {
        public string HeroId { get; }
        public string HeroName { get; }
        public double CurrentDay { get; }
        public string? RejectionReason { get; }
        public int CandidateCount { get; }
        public IReadOnlyList<AiTargetPreviewData> Targets { get; }

        public AiPushPreviewData(
            string heroId,
            string heroName,
            double currentDay,
            string? rejectionReason,
            int candidateCount,
            IReadOnlyList<AiTargetPreviewData> targets)
        {
            HeroId = heroId;
            HeroName = heroName;
            CurrentDay = currentDay;
            RejectionReason = rejectionReason;
            CandidateCount = candidateCount;
            Targets = targets ?? Array.Empty<AiTargetPreviewData>();
        }
    }

    /// <summary>
    /// Coordinates pushing remembered rumors to external AI dialogue mods.
    /// Handles soft target discovery, reflection binding, and event handling without compile-time dependencies.
    /// </summary>
    internal static class AiPushCoordinator
    {
        private sealed class TargetBindingState
        {
            public AiTargetDescription Description { get; }
            public AiTargetBindingResult Result { get; }
            public MethodInfo? AddMemoryMethod { get; }
            public Type? MemoryEntryType { get; }
            public bool IsSubscribed { get; set; }
            public object? Listener { get; set; }

            public TargetBindingState(AiTargetDescription desc, AiTargetBindingResult result, MethodInfo? addMemoryMethod, Type? entryType)
            {
                Description = desc;
                Result = result;
                AddMemoryMethod = addMemoryMethod;
                MemoryEntryType = entryType;
            }
        }

        private static readonly List<AiTargetDescription> _targets = new List<AiTargetDescription>
        {
            AiTargetDescription.CalradiaRemembers(),
            AiTargetDescription.ImmersiveAI()
        };

        private static readonly Dictionary<string, TargetBindingState> _bindings = new Dictionary<string, TargetBindingState>(StringComparer.OrdinalIgnoreCase);
        private static readonly object _lock = new object();
        private static readonly HashSet<string> _subscribedTargets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // 清單要完整交給推送規劃，先扣「已推過的」再取上限；先截前幾筆的話，推久了前面都推過，後面的永遠輪不到
        private const int RecallAll = int.MaxValue;

        private static VividWorldConfig? _config;
        private static WorldEventStore? _store;
        private static string? _campaignId;
        private static string? _pushStorePath;
        private static AiPushedStore? _pushStore;

        internal static AiPushedStore? PushStore => _pushStore;

        public static void Initialize(VividWorldConfig config)
        {
            lock (_lock)
            {
                _config = config;
                if (!config.Enabled || !config.Ai.Enabled)
                {
                    ModLog.Info("AI integration disabled in config (ai.enabled = false).");
                    return;
                }

                foreach (var desc in _targets)
                {
                    Assembly? asm = AppDomain.CurrentDomain.GetAssemblies()
                        .FirstOrDefault(a => string.Equals(a.GetName().Name, desc.AssemblyName, StringComparison.OrdinalIgnoreCase));

                    var result = AiTargetValidator.Validate(desc, asm);
                    if (!result.IsEnabled)
                    {
                        _bindings[desc.TargetId] = new TargetBindingState(desc, result, null, null);
                        ModLog.Info($"AI target '{desc.TargetId}' disabled: {result.Reason} (accepted version: {desc.AcceptedApiVersion}, detected version: {result.DetectedVersion}).");
                        continue;
                    }

                    try
                    {
                        Type memType = asm!.GetType(desc.MemoryTypeName)!;
                        Type entryType = asm.GetType(desc.MemoryEntryTypeName)!;
                        MethodInfo addMethod = memType.GetMethod(desc.AddMemoryMethodName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)!;

                        var state = new TargetBindingState(desc, result, addMethod, entryType);

                        // 每次開始或讀取戰役都會走到這裡，但對方的事件是靜態的、跟著整個遊戲程序活著；
                        // 重複訂閱會讓一次開聊天觸發兩次推送，每場對話塞進上限的兩倍
                        object? listener = null;
                        bool subscribed = _subscribedTargets.Contains(desc.TargetId) || SubscribeEvent(desc, asm, out listener);
                        if (!subscribed)
                        {
                            var failure = new AiTargetBindingResult(false, $"Could not subscribe to '{desc.ConversationTypeName}.{desc.ConversationEventName}'", result.DetectedVersion);
                            _bindings[desc.TargetId] = new TargetBindingState(desc, failure, null, null);
                            ModLog.Info($"AI target '{desc.TargetId}' disabled: {failure.Reason} (accepted version: {desc.AcceptedApiVersion}, detected version: {result.DetectedVersion}).");
                            continue;
                        }
                        _subscribedTargets.Add(desc.TargetId);
                        state.IsSubscribed = true;
                        state.Listener = listener;
                        _bindings[desc.TargetId] = state;

                        ModLog.Info($"AI target '{desc.TargetId}' enabled (accepted version: {desc.AcceptedApiVersion}, detected version: {result.DetectedVersion}).");
                    }
                    catch (Exception ex)
                    {
                        var failure = new AiTargetBindingResult(false, $"Exception during binding: {ex.Message}", result.DetectedVersion);
                        _bindings[desc.TargetId] = new TargetBindingState(desc, failure, null, null);
                        ModLog.Warn($"AI target '{desc.TargetId}' binding threw exception: {ex.Message}");
                    }
                }
            }
        }

        private sealed class TargetEventListener
        {
            private readonly string _targetId;

            public TargetEventListener(string targetId)
            {
                _targetId = targetId;
            }

            public void OnConversationStarted<T>(T info)
            {
                if (info == null) return;
                try
                {
                    var prop = info.GetType().GetProperty("NpcId");
                    string? npcId = prop?.GetValue(info) as string;
                    if (!string.IsNullOrEmpty(npcId))
                    {
                        HandleConversationStarted(_targetId, npcId!);
                    }
                }
                catch (Exception ex)
                {
                    ModLog.Error($"AI target '{_targetId}' conversation handler error", ex);
                }
            }
        }

        private static bool SubscribeEvent(AiTargetDescription desc, Assembly asm, out object? listener)
        {
            listener = null;
            Type? convType = asm.GetType(desc.ConversationTypeName);
            if (convType == null) return false;

            EventInfo? ev = convType.GetEvent(desc.ConversationEventName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
            if (ev == null || ev.EventHandlerType == null) return false;

            Type[] genArgs = ev.EventHandlerType.GetGenericArguments();
            if (genArgs.Length != 1) return false;

            var targetListener = new TargetEventListener(desc.TargetId);
            MethodInfo openMethod = typeof(TargetEventListener).GetMethod(nameof(TargetEventListener.OnConversationStarted), BindingFlags.Public | BindingFlags.Instance)!;
            MethodInfo closedMethod = openMethod.MakeGenericMethod(genArgs[0]);
            Delegate handler = Delegate.CreateDelegate(ev.EventHandlerType, targetListener, closedMethod);
            ev.AddEventHandler(null, handler);
            listener = targetListener;
            return true;
        }

        public static void SetSession(WorldEventStore store, string campaignId, string pushStorePath)
        {
            lock (_lock)
            {
                _store = store;
                _campaignId = campaignId;
                _pushStorePath = pushStorePath;
                _pushStore = AiPushedStore.LoadFromFile(pushStorePath);
            }
        }

        public static void Flush()
        {
            lock (_lock)
            {
                if (_pushStore != null && _pushStore.IsDirty && !string.IsNullOrEmpty(_pushStorePath))
                {
                    _pushStore.SaveToFile(_pushStorePath!);
                }
            }
        }

        public static void ReloadStore()
        {
            lock (_lock)
            {
                if (!string.IsNullOrEmpty(_pushStorePath))
                {
                    _pushStore = AiPushedStore.LoadFromFile(_pushStorePath!);
                }
            }
        }

        public static void Reset()
        {
            lock (_lock)
            {
                _store = null;
                _campaignId = null;
                _pushStorePath = null;
                _pushStore = null;
            }
        }

        private static void HandleConversationStarted(string targetId, string npcId)
        {
            var sw = Stopwatch.StartNew();

            VividWorldConfig? cfg;
            WorldEventStore? store;
            AiPushedStore? pushStore;
            string? pushStorePath;
            TargetBindingState? binding;

            lock (_lock)
            {
                cfg = _config;
                store = _store;
                pushStore = _pushStore;
                pushStorePath = _pushStorePath;
                _bindings.TryGetValue(targetId, out binding);
            }

            if (cfg == null || !cfg.Enabled || !cfg.Ai.Enabled) return;
            if (binding == null || !binding.Result.IsEnabled || binding.AddMemoryMethod == null || binding.MemoryEntryType == null) return;
            if (store == null || pushStore == null) return;

            try
            {
                Hero? hero = Hero.Find(npcId);
                string? rejectReason = EligibilityLabel.GetRejectionReason(hero, store.Traits);
                if (rejectReason != null)
                {
                    ModLog.Info($"AI push skipped for '{npcId}' to '{targetId}': hero is not eligible ({rejectReason}).");
                    return;
                }

                double currentDay = CampaignTime.Now.ToDays;
                var eventIds = store.KnownBy.EventsKnownBy(hero!.StringId, currentDay);
                if (eventIds == null || eventIds.Count == 0)
                {
                    ModLog.Info($"AI push for '{hero.StringId}' to '{targetId}': 0 candidates, 0 pushed, 0 excluded, elapsed {sw.Elapsed.TotalMilliseconds:F1}ms.");
                    return;
                }

                var candidateEvents = new List<WorldEvent>(eventIds.Count);
                foreach (var id in eventIds)
                {
                    var evt = store.Load(id);
                    if (evt != null) candidateEvents.Add(evt);
                }

                var knownSet = new HashSet<string>(eventIds, StringComparer.OrdinalIgnoreCase);
                var retentionPolicy = FactRetentionPolicies.Create(cfg, new SplitMix64Rng(), store.CampaignSeed);

                var recallResult = NpcRecallQuery.Query(
                    hero.StringId,
                    currentDay,
                    maxCount: RecallAll,
                    candidateEvents,
                    retentionPolicy,
                    isEventKnown: id => knownSet.Contains(id),
                    config: cfg,
                    getTemplate: EventCatalogStore.TemplateByType,
                    traits: store.Traits);

                int maxPerChat = binding.Description.IsPersistent
                    ? cfg.Ai.PersistentMaxNewPerChat
                    : cfg.Ai.ChatOnlyMaxPerChat;

                var plan = AiPushPlanner.Plan(
                    targetId,
                    hero.StringId,
                    recallResult.Items,
                    pushStore,
                    binding.Description.IsPersistent,
                    maxPerChat);

                string pushLang = cfg.Ai.PushLanguage ?? "english";
                var pushedEntries = new List<(string EventId, string Version, int Chars, bool Truncated, string Kind, string Result)>();

                foreach (var candidate in plan.ToPush)
                {
                    bool isParticipant = candidate.Memory.Event.RoleOf(hero.StringId) != null;
                    var prefix = RumorPrefixSelector.SelectPrefix(
                        candidate.Memory.Hop,
                        candidate.Memory.SourceHeroId,
                        false,
                        candidate.Memory.IsCorrection,
                        isParticipant);

                    var (text, isTruncated, _) = AiPushTruncator.Truncate(
                        candidate.Memory.Facts,
                        subset =>
                        {
                            var composed = RumorTextComposer.Compose(candidate.Memory.Event, subset, cfg.Presentation, prefix, hero.StringId, candidate.Memory.SourceHeroId);
                            return FallbackTextRenderer.RenderRecallMemory(composed, pushLang, cfg.Presentation);
                        });

                    string kind = AiMemoryKind.For(candidate.Memory.Event, hero.StringId, Hero.MainHero?.StringId, candidate.Memory.Hop);

                    object entry = Activator.CreateInstance(binding.MemoryEntryType);
                    binding.MemoryEntryType.GetProperty("NpcId")?.SetValue(entry, hero.StringId);
                    binding.MemoryEntryType.GetProperty("Summary")?.SetValue(entry, text);
                    binding.MemoryEntryType.GetProperty("Kind")?.SetValue(entry, kind);
                    int daysAgo = (int)Math.Max(0, Math.Floor(currentDay - candidate.Memory.LearnedDay));
                    binding.MemoryEntryType.GetProperty("DaysAgo")?.SetValue(entry, daysAgo);
                    binding.MemoryEntryType.GetProperty("SourceMod")?.SetValue(entry, "VividWorld");

                    object? invokeResult = binding.AddMemoryMethod.Invoke(null, new object[] { entry });
                    string resStr = invokeResult?.ToString() ?? "Unknown";

                    bool succeeded = string.Equals(resStr, "Added", StringComparison.OrdinalIgnoreCase) ||
                                     (invokeResult is int intRes && intRes == 0);

                    if (succeeded && binding.Description.IsPersistent)
                    {
                        pushStore.Record(targetId, hero.StringId, candidate.Memory.EventId, candidate.Version, currentDay);
                        if (!string.IsNullOrEmpty(pushStorePath))
                        {
                            pushStore.SaveToFile(pushStorePath!);
                        }
                    }

                    pushedEntries.Add((candidate.Memory.EventId, candidate.Version, text.Length, isTruncated, kind, resStr));
                }

                // Log structured outcome
                int excludedCount = recallResult.Exclusions.Count + plan.Exclusions.Count;
                var sb = new System.Text.StringBuilder();
                sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                    "AI push for '{0}' to '{1}': {2} candidates, {3} pushed, {4} excluded, elapsed {5:F1}ms.",
                    hero.StringId, targetId, recallResult.CandidateCount, pushedEntries.Count, excludedCount, sw.Elapsed.TotalMilliseconds));

                if (excludedCount > 0)
                {
                    sb.AppendLine("  Excluded:");
                    foreach (var exc in recallResult.Exclusions)
                    {
                        sb.AppendLine($"    {exc}");
                    }
                    foreach (var exc in plan.Exclusions)
                    {
                        sb.AppendLine($"    {exc.EventId}: {exc.Reason} ({exc.Detail})");
                    }
                }

                if (pushedEntries.Count > 0)
                {
                    sb.AppendLine("  Pushed:");
                    foreach (var p in pushedEntries)
                    {
                        sb.AppendLine($"    {p.EventId}: ver={p.Version}, chars={p.Chars}, truncated={p.Truncated}, kind={p.Kind}, result={p.Result}");
                    }
                }

                ModLog.Info(sb.ToString().TrimEnd());
            }
            catch (Exception ex)
            {
                ModLog.Error($"AI push for '{npcId}' to '{targetId}' failed", ex);
            }
        }

        public static AiPushPreviewData GeneratePreview(Hero hero, WorldEventStore store, VividWorldConfig config)
        {
            if (hero == null) throw new ArgumentNullException(nameof(hero));
            if (store == null) throw new ArgumentNullException(nameof(store));
            if (config == null) throw new ArgumentNullException(nameof(config));

            double currentDay = CampaignTime.Now.ToDays;
            string? rejectReason = EligibilityLabel.GetRejectionReason(hero, store.Traits);
            if (rejectReason != null)
            {
                return new AiPushPreviewData(
                    hero.StringId,
                    hero.Name?.ToString() ?? hero.StringId,
                    currentDay,
                    rejectReason,
                    0,
                    Array.Empty<AiTargetPreviewData>());
            }

            var eventIds = store.KnownBy.EventsKnownBy(hero.StringId, currentDay) ?? Array.Empty<string>();
            var candidateEvents = new List<WorldEvent>(eventIds.Count);
            foreach (var id in eventIds)
            {
                var evt = store.Load(id);
                if (evt != null) candidateEvents.Add(evt);
            }

            var knownSet = new HashSet<string>(eventIds, StringComparer.OrdinalIgnoreCase);
            var retentionPolicy = FactRetentionPolicies.Create(config, new SplitMix64Rng(), store.CampaignSeed);

            var recallResult = NpcRecallQuery.Query(
                hero.StringId,
                currentDay,
                maxCount: RecallAll,
                candidateEvents,
                retentionPolicy,
                isEventKnown: id => knownSet.Contains(id),
                config: config,
                getTemplate: EventCatalogStore.TemplateByType,
                traits: store.Traits);

            string pushLang = config.Ai.PushLanguage ?? "english";
            var targetPreviews = new List<AiTargetPreviewData>(_targets.Count);

            foreach (var desc in _targets)
            {
                TargetBindingState? binding;
                lock (_lock)
                {
                    _bindings.TryGetValue(desc.TargetId, out binding);
                }

                bool isEnabled = config.Enabled && config.Ai.Enabled &&
                                 binding != null && binding.Result.IsEnabled;
                string? disabledReason = null;
                if (!config.Enabled || !config.Ai.Enabled)
                {
                    disabledReason = "AI integration disabled in config (ai.enabled = false)";
                }
                else if (binding == null)
                {
                    disabledReason = "Target not initialized";
                }
                else if (!binding.Result.IsEnabled)
                {
                    disabledReason = binding.Result.Reason;
                }

                if (!isEnabled)
                {
                    targetPreviews.Add(new AiTargetPreviewData(
                        desc.TargetId,
                        isEnabled: false,
                        disabledReason: disabledReason,
                        isPersistent: desc.IsPersistent,
                        maxPerChat: desc.IsPersistent ? config.Ai.PersistentMaxNewPerChat : config.Ai.ChatOnlyMaxPerChat,
                        plannedPushes: Array.Empty<AiPushPreviewItem>(),
                        exclusions: Array.Empty<string>(),
                        existingPushes: new Dictionary<string, AiPushRecord>()));
                    continue;
                }

                int maxPerChat = desc.IsPersistent
                    ? config.Ai.PersistentMaxNewPerChat
                    : config.Ai.ChatOnlyMaxPerChat;

                var existingPushes = desc.IsPersistent
                    ? (_pushStore?.GetPushedForHero(desc.TargetId, hero.StringId) ?? new Dictionary<string, AiPushRecord>())
                    : new Dictionary<string, AiPushRecord>();

                var plan = AiPushPlanner.Plan(
                    desc.TargetId,
                    hero.StringId,
                    recallResult.Items,
                    desc.IsPersistent ? _pushStore : null,
                    desc.IsPersistent,
                    maxPerChat);

                var previewItems = new List<AiPushPreviewItem>();

                foreach (var candidate in plan.ToPush)
                {
                    bool isParticipant = candidate.Memory.Event.RoleOf(hero.StringId) != null;
                    var prefix = RumorPrefixSelector.SelectPrefix(
                        candidate.Memory.Hop,
                        candidate.Memory.SourceHeroId,
                        false,
                        candidate.Memory.IsCorrection,
                        isParticipant);

                    var (text, isTruncated, _) = AiPushTruncator.Truncate(
                        candidate.Memory.Facts,
                        subset =>
                        {
                            var composed = RumorTextComposer.Compose(candidate.Memory.Event, subset, config.Presentation, prefix, hero.StringId, candidate.Memory.SourceHeroId);
                            return FallbackTextRenderer.RenderRecallMemory(composed, pushLang, config.Presentation);
                        });

                    int daysAgo = (int)Math.Max(0, Math.Floor(currentDay - candidate.Memory.LearnedDay));
                    bool alreadyPushed = existingPushes.ContainsKey(candidate.Memory.EventId);
                    string kind = AiMemoryKind.For(candidate.Memory.Event, hero.StringId, Hero.MainHero?.StringId, candidate.Memory.Hop);

                    previewItems.Add(new AiPushPreviewItem(
                        candidate.Memory.EventId,
                        candidate.Version,
                        text,
                        text.Length,
                        isTruncated,
                        daysAgo,
                        kind,
                        alreadyPushed));
                }

                var exclusions = new List<string>();
                foreach (var exc in recallResult.Exclusions)
                {
                    exclusions.Add(exc.ToString());
                }
                foreach (var exc in plan.Exclusions)
                {
                    exclusions.Add($"{exc.EventId}: {exc.Reason} ({exc.Detail})");
                }

                targetPreviews.Add(new AiTargetPreviewData(
                    desc.TargetId,
                    isEnabled: true,
                    disabledReason: null,
                    isPersistent: desc.IsPersistent,
                    maxPerChat: maxPerChat,
                    plannedPushes: previewItems,
                    exclusions: exclusions,
                    existingPushes: existingPushes));
            }

            return new AiPushPreviewData(
                hero.StringId,
                hero.Name?.ToString() ?? hero.StringId,
                currentDay,
                null,
                recallResult.CandidateCount,
                targetPreviews);
        }
    }
}
