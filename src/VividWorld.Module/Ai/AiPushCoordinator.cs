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
        public bool IsLive { get; }
        public AiLiveNewsBlockResult? StandardBlock { get; }
        public AiLiveNewsBlockResult? LeanBlock { get; }

        public AiTargetPreviewData(
            string targetId,
            bool isEnabled,
            string? disabledReason,
            bool isPersistent,
            int maxPerChat,
            IReadOnlyList<AiPushPreviewItem> plannedPushes,
            IReadOnlyList<string> exclusions,
            IReadOnlyDictionary<string, AiPushRecord> existingPushes,
            bool isLive = false,
            AiLiveNewsBlockResult? standardBlock = null,
            AiLiveNewsBlockResult? leanBlock = null)
        {
            TargetId = targetId;
            IsEnabled = isEnabled;
            DisabledReason = disabledReason;
            IsPersistent = isPersistent;
            MaxPerChat = maxPerChat;
            PlannedPushes = plannedPushes ?? Array.Empty<AiPushPreviewItem>();
            Exclusions = exclusions ?? Array.Empty<string>();
            ExistingPushes = existingPushes ?? new Dictionary<string, AiPushRecord>();
            IsLive = isLive;
            StandardBlock = standardBlock;
            LeanBlock = leanBlock;
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
            public bool IsLive { get; set; }

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
        private static readonly HashSet<string> _liveRegisteredTargets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

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

                        // 對方有即時提示變數就改成每句回覆即時交，沒有就照舊加記憶
                        TryRegisterLivePromptVariable(desc, asm, state);

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

        private static void TryRegisterLivePromptVariable(AiTargetDescription desc, Assembly asm, TargetBindingState state)
        {
            if (string.IsNullOrEmpty(desc.LivePromptTypeName) || string.IsNullOrEmpty(desc.LivePromptMethodName))
            {
                return;
            }

            try
            {
                Type? promptType = asm.GetType(desc.LivePromptTypeName);
                if (promptType == null)
                {
                    ModLog.Info($"AI target '{desc.TargetId}': no live prompt variable ({desc.LivePromptTypeName}.{desc.LivePromptMethodName} not found); keeps adding memories.");
                    return;
                }

                MethodInfo[] methods = promptType.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                MethodInfo? registerMethod = methods.FirstOrDefault(m =>
                    m.Name == desc.LivePromptMethodName &&
                    m.GetParameters().Length == 2 &&
                    m.GetParameters()[0].ParameterType == typeof(string) &&
                    m.GetParameters()[1].ParameterType.IsGenericType &&
                    m.GetParameters()[1].ParameterType.GetGenericTypeDefinition() == typeof(Func<,>));

                if (registerMethod == null)
                {
                    ModLog.Info($"AI target '{desc.TargetId}': no live prompt variable ({desc.LivePromptTypeName}.{desc.LivePromptMethodName} not found); keeps adding memories.");
                    return;
                }

                Type funcType = registerMethod.GetParameters()[1].ParameterType;
                Type tFacts = funcType.GetGenericArguments()[0];

                if (!_liveRegisteredTargets.Contains(desc.TargetId))
                {
                    MethodInfo openProvider = typeof(AiPushCoordinator).GetMethod(nameof(ProvideLiveNews), BindingFlags.NonPublic | BindingFlags.Static)!;
                    MethodInfo closedProvider = openProvider.MakeGenericMethod(tFacts);
                    Delegate providerDelegate = Delegate.CreateDelegate(funcType, closedProvider);

                    registerMethod.Invoke(null, new object[] { AiLiveNewsBlock.VariableName, providerDelegate });
                    _liveRegisteredTargets.Add(desc.TargetId);
                }

                state.IsLive = true;
                ModLog.Info($"AI target '{desc.TargetId}': live prompt variable '{{{{{AiLiveNewsBlock.VariableName}}}}}' registered; adding memories is off for this target.");
            }
            catch (Exception ex)
            {
                state.IsLive = false;
                ModLog.Info($"AI target '{desc.TargetId}': no live prompt variable ({ex.Message}); keeps adding memories.");
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

        private sealed class RecallRenderedItem
        {
            public NpcRecalledMemory Candidate { get; }
            public string Text { get; }
            public bool IsTruncated { get; }
            public string Kind { get; }
            public int DaysAgo { get; }
            public AiLiveNewsItem NewsItem { get; }

            public RecallRenderedItem(
                NpcRecalledMemory candidate,
                string text,
                bool isTruncated,
                string kind,
                int daysAgo,
                AiLiveNewsItem newsItem)
            {
                Candidate = candidate;
                Text = text;
                IsTruncated = isTruncated;
                Kind = kind;
                DaysAgo = daysAgo;
                NewsItem = newsItem;
            }
        }

        private static bool ExtractRecallNewsItems(
            Hero hero,
            double currentDay,
            VividWorldConfig cfg,
            WorldEventStore store,
            out NpcRecallResult recallResult,
            out List<RecallRenderedItem> renderedItems)
        {
            renderedItems = new List<RecallRenderedItem>();
            var eventIds = store.KnownBy.EventsKnownBy(hero.StringId, currentDay);
            if (eventIds == null || eventIds.Count == 0)
            {
                recallResult = new NpcRecallResult(hero.StringId, currentDay, Array.Empty<NpcRecalledMemory>(), Array.Empty<RecallExclusion>(), 0);
                return false;
            }

            var candidateEvents = new List<WorldEvent>(eventIds.Count);
            foreach (var id in eventIds)
            {
                var evt = store.Load(id);
                if (evt != null) candidateEvents.Add(evt);
            }

            var knownSet = new HashSet<string>(eventIds, StringComparer.OrdinalIgnoreCase);
            var retentionPolicy = FactRetentionPolicies.Create(cfg, new SplitMix64Rng(), store.CampaignSeed);

            recallResult = NpcRecallQuery.Query(
                hero.StringId,
                currentDay,
                maxCount: RecallAll,
                candidateEvents,
                retentionPolicy,
                isEventKnown: id => knownSet.Contains(id),
                config: cfg,
                getTemplate: EventCatalogStore.TemplateByType,
                traits: store.Traits);

            if (recallResult.Items.Count == 0)
            {
                return false;
            }

            string pushLang = cfg.Ai.PushLanguage ?? "english";
            string? mainHeroId = Hero.MainHero?.StringId;

            foreach (var candidate in recallResult.Items)
            {
                bool isParticipant = candidate.Event.RoleOf(hero.StringId) != null;
                var prefix = RumorPrefixSelector.SelectPrefix(
                    candidate.Hop,
                    candidate.SourceHeroId,
                    false,
                    candidate.IsCorrection,
                    isParticipant);

                var (text, isTruncated, _) = AiPushTruncator.Truncate(
                    candidate.Facts,
                    subset =>
                    {
                        var composed = RumorTextComposer.Compose(candidate.Event, subset, cfg.Presentation, prefix, hero.StringId, candidate.SourceHeroId);
                        return FallbackTextRenderer.RenderRecallMemory(composed, pushLang, cfg.Presentation);
                    });

                string kind = AiMemoryKind.For(candidate.Event, hero.StringId, mainHeroId, candidate.Hop);
                int daysAgo = (int)Math.Max(0, Math.Floor(currentDay - candidate.LearnedDay));
                var newsItem = new AiLiveNewsItem(candidate.EventId, kind, daysAgo, text);

                renderedItems.Add(new RecallRenderedItem(candidate, text, isTruncated, kind, daysAgo, newsItem));
            }

            return true;
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

            // 即時模式下消息每句回覆都會交，再加記憶會讓同一件事在對方那裡有兩份
            if (binding.IsLive)
            {
                ModLog.Info($"AI push for '{npcId}' to '{targetId}' skipped: live prompt variable in use (memory text is given on every reply).");
                return;
            }

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
                if (!ExtractRecallNewsItems(hero!, currentDay, cfg, store, out var recallResult, out var renderedItems))
                {
                    ModLog.Info($"AI push for '{hero!.StringId}' to '{targetId}': 0 candidates, 0 pushed, 0 excluded, elapsed {sw.Elapsed.TotalMilliseconds:F1}ms.");
                    return;
                }

                int maxPerChat = binding.Description.IsPersistent
                    ? cfg.Ai.PersistentMaxNewPerChat
                    : cfg.Ai.ChatOnlyMaxPerChat;

                var plan = AiPushPlanner.Plan(
                    targetId,
                    hero!.StringId,
                    recallResult.Items,
                    pushStore,
                    binding.Description.IsPersistent,
                    maxPerChat);

                var renderedByEventId = renderedItems.ToDictionary(r => r.Candidate.EventId, StringComparer.OrdinalIgnoreCase);
                var pushedEntries = new List<(string EventId, string Version, int Chars, bool Truncated, string Kind, string Result)>();

                foreach (var candidate in plan.ToPush)
                {
                    if (!renderedByEventId.TryGetValue(candidate.Memory.EventId, out var rendered))
                    {
                        continue;
                    }

                    object entry = Activator.CreateInstance(binding.MemoryEntryType);
                    binding.MemoryEntryType.GetProperty("NpcId")?.SetValue(entry, hero.StringId);
                    binding.MemoryEntryType.GetProperty("Summary")?.SetValue(entry, rendered.Text);
                    binding.MemoryEntryType.GetProperty("Kind")?.SetValue(entry, rendered.Kind);
                    binding.MemoryEntryType.GetProperty("DaysAgo")?.SetValue(entry, rendered.DaysAgo);
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

                    pushedEntries.Add((candidate.Memory.EventId, candidate.Version, rendered.Text.Length, rendered.IsTruncated, rendered.Kind, resStr));
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

        private static string ProvideLiveNews<TFacts>(TFacts facts)
        {
            var sw = Stopwatch.StartNew();
            if (facts == null) return string.Empty;

            string? npcId = null;
            bool lean = false;

            try
            {
                Type factsType = facts.GetType();
                var npcIdProp = factsType.GetProperty("NpcId");
                var leanProp = factsType.GetProperty("Lean");

                npcId = npcIdProp?.GetValue(facts) as string;
                if (leanProp != null)
                {
                    object? leanVal = leanProp.GetValue(facts);
                    if (leanVal is bool b) lean = b;
                }
            }
            catch (Exception ex)
            {
                ModLog.Error("Error reading prompt variable facts", ex);
                return string.Empty;
            }

            if (string.IsNullOrEmpty(npcId)) return string.Empty;

            VividWorldConfig? cfg;
            WorldEventStore? store;

            lock (_lock)
            {
                cfg = _config;
                store = _store;
            }

            if (cfg == null || !cfg.Enabled || !cfg.Ai.Enabled)
            {
                ModLog.Info($"AI live news for '{npcId}' (lean={lean.ToString().ToLowerInvariant()}): empty (AI integration or mod disabled in config).");
                return string.Empty;
            }

            if (store == null || TaleWorlds.CampaignSystem.Campaign.Current == null)
            {
                ModLog.Info($"AI live news for '{npcId}' (lean={lean.ToString().ToLowerInvariant()}): empty (no active campaign or store).");
                return string.Empty;
            }

            try
            {
                Hero? hero = Hero.Find(npcId);
                if (hero == null)
                {
                    ModLog.Info($"AI live news for '{npcId}' (lean={lean.ToString().ToLowerInvariant()}): empty (hero not found).");
                    return string.Empty;
                }

                string? rejectReason = EligibilityLabel.GetRejectionReason(hero, store.Traits);
                if (rejectReason != null)
                {
                    ModLog.Info($"AI live news for '{npcId}' (lean={lean.ToString().ToLowerInvariant()}): empty (hero ineligible: {rejectReason}).");
                    return string.Empty;
                }

                double currentDay = CampaignTime.Now.ToDays;
                if (!ExtractRecallNewsItems(hero, currentDay, cfg, store, out var recallResult, out var renderedItems) || renderedItems.Count == 0)
                {
                    ModLog.Info($"AI live news for '{npcId}' (lean={lean.ToString().ToLowerInvariant()}): 0 candidates, 0 given, 0 excluded, 0 chars of {(lean ? AiLiveNewsBlock.LeanMaxChars : AiLiveNewsBlock.MaxChars)}, elapsed {sw.Elapsed.TotalMilliseconds:F1}ms.");
                    return string.Empty;
                }

                var liveItems = new List<AiLiveNewsItem>(renderedItems.Count);
                for (int i = 0; i < renderedItems.Count; i++)
                {
                    liveItems.Add(renderedItems[i].NewsItem);
                }

                string heroName = hero.Name?.ToString() ?? hero.StringId;
                var blockResult = AiLiveNewsBlock.Build(heroName, lean, liveItems);

                int givenCount = blockResult.IncludedEventIds.Count;
                int excludedByLimitCount = blockResult.ExcludedItems.Count;
                int totalExcluded = recallResult.Exclusions.Count + excludedByLimitCount;

                // Message lines length (excluding header)
                int messageChars = 0;
                if (givenCount > 0)
                {
                    string header = lean
                        ? string.Format(AiLiveNewsBlock.HeaderLeanTemplate, heroName)
                        : string.Format(AiLiveNewsBlock.HeaderStandardTemplate, heroName);
                    messageChars = Math.Max(0, blockResult.Text.Length - header.Length - 1);
                }

                var sb = new System.Text.StringBuilder();
                sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                    "AI live news for '{0}' (lean={1}): {2} candidates, {3} given, {4} excluded, {5} chars of {6}, elapsed {7:F1}ms.",
                    hero.StringId,
                    lean.ToString().ToLowerInvariant(),
                    recallResult.CandidateCount,
                    givenCount,
                    totalExcluded,
                    messageChars,
                    blockResult.MaxChars,
                    sw.Elapsed.TotalMilliseconds));

                if (totalExcluded > 0)
                {
                    sb.AppendLine("  Excluded:");
                    foreach (var exc in recallResult.Exclusions)
                    {
                        sb.AppendLine($"    {exc}");
                    }
                    foreach (var exc in blockResult.ExcludedItems)
                    {
                        sb.AppendLine($"    {exc.EventId}: over the length limit ({exc.ItemChars} chars, {exc.UsedCharsBefore} used)");
                    }
                }

                if (givenCount > 0)
                {
                    sb.AppendLine("  Given:");
                    var renderedByEventId = renderedItems.ToDictionary(r => r.Candidate.EventId, StringComparer.OrdinalIgnoreCase);
                    foreach (var evtId in blockResult.IncludedEventIds)
                    {
                        if (renderedByEventId.TryGetValue(evtId, out var item))
                        {
                            sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                                "    {0}: kind={1}, days={2}, chars={3}, truncated={4}",
                                evtId,
                                item.Kind,
                                item.DaysAgo,
                                item.Text.Length,
                                item.IsTruncated.ToString().ToLowerInvariant()));
                        }
                    }
                }

                ModLog.Info(sb.ToString().TrimEnd());
                return blockResult.Text;
            }
            catch (Exception ex)
            {
                ModLog.Error($"AI live news for '{npcId}' failed", ex);
                return string.Empty;
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

            ExtractRecallNewsItems(hero, currentDay, config, store, out var recallResult, out var renderedItems);

            var liveItems = new List<AiLiveNewsItem>(renderedItems.Count);
            for (int i = 0; i < renderedItems.Count; i++)
            {
                liveItems.Add(renderedItems[i].NewsItem);
            }

            string heroName = hero.Name?.ToString() ?? hero.StringId;
            var standardBlock = AiLiveNewsBlock.Build(heroName, lean: false, liveItems);
            var leanBlock = AiLiveNewsBlock.Build(heroName, lean: true, liveItems);

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

                bool isLive = binding != null && binding.IsLive;

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
                        existingPushes: new Dictionary<string, AiPushRecord>(),
                        isLive: isLive,
                        standardBlock: null,
                        leanBlock: null));
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

                var renderedByEventId = renderedItems.ToDictionary(r => r.Candidate.EventId, StringComparer.OrdinalIgnoreCase);
                var previewItems = new List<AiPushPreviewItem>();

                foreach (var candidate in plan.ToPush)
                {
                    if (!renderedByEventId.TryGetValue(candidate.Memory.EventId, out var rendered))
                    {
                        continue;
                    }

                    bool alreadyPushed = existingPushes.ContainsKey(candidate.Memory.EventId);

                    previewItems.Add(new AiPushPreviewItem(
                        candidate.Memory.EventId,
                        candidate.Version,
                        rendered.Text,
                        rendered.Text.Length,
                        rendered.IsTruncated,
                        rendered.DaysAgo,
                        rendered.Kind,
                        alreadyPushed));
                }

                var exclusions = new List<string>();
                foreach (var exc in recallResult.Exclusions)
                {
                    exclusions.Add(exc.ToString());
                }
                // 即時模式不經過推送規劃，「已推過」那類排除對它沒有意義，列出來會誤導
                if (!isLive)
                {
                    foreach (var exc in plan.Exclusions)
                    {
                        exclusions.Add($"{exc.EventId}: {exc.Reason} ({exc.Detail})");
                    }
                }

                targetPreviews.Add(new AiTargetPreviewData(
                    desc.TargetId,
                    isEnabled: true,
                    disabledReason: null,
                    isPersistent: desc.IsPersistent,
                    maxPerChat: maxPerChat,
                    plannedPushes: previewItems,
                    exclusions: exclusions,
                    existingPushes: existingPushes,
                    isLive: isLive,
                    standardBlock: isLive ? standardBlock : null,
                    leanBlock: isLive ? leanBlock : null));
            }

            return new AiPushPreviewData(
                hero.StringId,
                heroName,
                currentDay,
                null,
                recallResult.CandidateCount,
                targetPreviews);
        }
    }
}
