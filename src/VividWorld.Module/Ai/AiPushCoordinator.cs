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

            /// <summary>記憶物件上找得到的屬性（舊版對方沒有 Private、SourceMod）；沒找到的寫入時跳過。</summary>
            public HashSet<string> EntryProperties { get; } = new HashSet<string>(StringComparer.Ordinal);

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
        private static readonly HashSet<string> _liveNewsTargets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        // 沒裝 CR 時留空：開發者預覽照舊變數的上限算，ImmersiveAI 那邊實際就是那個上限
        private static string? _crActiveVariable;
        private static bool _livePromptVariablesRegistered;

        // 清單要完整交給推送規劃，先扣「已推過的」再取上限；先截前幾筆的話，推久了前面都推過，後面的永遠輪不到
        private const int RecallAll = int.MaxValue;

        private static readonly string[] MemoryEntryPropertyNames = { "NpcId", "Summary", "Kind", "DaysAgo", "Private", "SourceMod" };

        private static VividWorldConfig? _config;
        private static WorldEventStore? _store;
        private static string? _campaignId;
        private static string? _pushStorePath;
        private static AiPushedStore? _pushStore;

        // 已經寫進 AI 模組記憶的紀錄；格式沿用推送紀錄，目標與版本欄位見 AiMemoryWritePolicy
        private static string? _memoryWrittenPath;
        private static AiPushedStore? _memoryWritten;
        // 寫不了的原因每個戰役階段只印一次，不要每則事件印
        private static string? _memoryOffReasonLogged;

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

                        var missingProps = new List<string>();
                        foreach (var propName in MemoryEntryPropertyNames)
                        {
                            if (entryType.GetProperty(propName) != null) state.EntryProperties.Add(propName);
                            else missingProps.Add(propName);
                        }
                        ModLog.Info($"AI target '{desc.TargetId}': memory entry properties found: {string.Join(", ", MemoryEntryPropertyNames.Where(state.EntryProperties.Contains))}; missing: {(missingProps.Count == 0 ? "none" : string.Join(", ", missingProps))} (missing ones are left out when writing).");

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

                        // 即時提示變數已在主選單前登記；此處僅記錄目標是否處於即時模式
                        state.IsLive = _liveNewsTargets.Contains(desc.TargetId);
                        if (state.IsLive)
                        {
                            ModLog.Info($"AI target '{desc.TargetId}': live prompt variables registered; adding memories is off for this target.");
                        }

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

        /// <summary>
        /// 在遊戲載入至主選單前登記外部 AI 模組的即時提示變數。
        /// 只要外部組件存在且具有變數登記介面即予以登記，避免外部提示模板中殘留未替換的標記。
        /// 同一遊戲程序僅執行一次。
        /// </summary>
        public static void RegisterLivePromptVariables()
        {
            lock (_lock)
            {
                if (_livePromptVariablesRegistered)
                {
                    return;
                }
                _livePromptVariablesRegistered = true;

                foreach (var desc in _targets)
                {
                    if (string.IsNullOrEmpty(desc.LivePromptTypeName) || string.IsNullOrEmpty(desc.LivePromptMethodName))
                    {
                        continue;
                    }

                    if (_liveRegisteredTargets.Contains(desc.TargetId))
                    {
                        continue;
                    }

                    Assembly? asm = AppDomain.CurrentDomain.GetAssemblies()
                        .FirstOrDefault(a => string.Equals(a.GetName().Name, desc.AssemblyName, StringComparison.OrdinalIgnoreCase));
                    if (asm == null)
                    {
                        continue;
                    }

                    bool isCr = string.Equals(desc.TargetId, "CalradiaRemembers", StringComparison.OrdinalIgnoreCase);
                    try
                    {
                        Type? promptType = asm.GetType(desc.LivePromptTypeName);
                        if (promptType == null)
                        {
                            ModLog.Info($"AI target '{desc.TargetId}': no live prompt variable ({desc.LivePromptTypeName}.{desc.LivePromptMethodName} not found); keeps adding memories.");
                            continue;
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
                            continue;
                        }

                        bool isLiveNews = true;
                        bool isHeardNow = true;
                        string? activeVar = null;
                        TaleWorlds.Library.ApplicationVersion? crVersion = null;

                        if (isCr)
                        {
                            try
                            {
                                var modInfo = TaleWorlds.ModuleManager.ModuleHelper.GetModuleInfo("CalradiaRemembers");
                                if (modInfo != null)
                                {
                                    crVersion = modInfo.Version;
                                    isLiveNews = CrLiveVersionGate.IsLiveVersion(crVersion.Value.Major, crVersion.Value.Minor, crVersion.Value.Revision);
                                    isHeardNow = CrLiveVersionGate.IsHeardNowVersion(crVersion.Value.Major, crVersion.Value.Minor, crVersion.Value.Revision);
                                    activeVar = CrLiveVersionGate.ResolveActiveVariable(crVersion.Value.Major, crVersion.Value.Minor, crVersion.Value.Revision);
                                }
                                else
                                {
                                    isLiveNews = true;
                                    isHeardNow = true;
                                    activeVar = AiLiveNewsBlock.HeardNowVariableName;
                                    ModLog.Warn($"AI target '{desc.TargetId}': unable to read Calradia Remembers module version; assuming v2.7.9 or newer and enabling live news.");
                                }
                            }
                            catch (Exception ex)
                            {
                                isLiveNews = true;
                                isHeardNow = true;
                                activeVar = AiLiveNewsBlock.HeardNowVariableName;
                                ModLog.Warn($"AI target '{desc.TargetId}': unable to read Calradia Remembers module version ({ex.Message}); assuming v2.7.9 or newer and enabling live news.");
                            }

                            _crActiveVariable = activeVar;
                        }

                        Type funcType = registerMethod.GetParameters()[1].ParameterType;
                        Type tFacts = funcType.GetGenericArguments()[0];

                        MethodInfo openNews = typeof(AiPushCoordinator).GetMethod(nameof(ProvideNewsVariable), BindingFlags.NonPublic | BindingFlags.Static)!;
                        MethodInfo closedNews = openNews.MakeGenericMethod(tFacts);
                        Delegate newsDelegate = Delegate.CreateDelegate(funcType, closedNews);

                        if (isCr)
                        {
                            MethodInfo openHeardNow = typeof(AiPushCoordinator).GetMethod(nameof(ProvideHeardNowVariable), BindingFlags.NonPublic | BindingFlags.Static)!;
                            MethodInfo closedHeardNow = openHeardNow.MakeGenericMethod(tFacts);
                            Delegate heardNowDelegate = Delegate.CreateDelegate(funcType, closedHeardNow);

                            registerMethod.Invoke(null, new object[] { AiLiveNewsBlock.VariableName, newsDelegate });
                            registerMethod.Invoke(null, new object[] { AiLiveNewsBlock.HeardNowVariableName, heardNowDelegate });
                        }
                        else
                        {
                            registerMethod.Invoke(null, new object[] { AiLiveNewsBlock.VariableName, newsDelegate });
                        }

                        _liveRegisteredTargets.Add(desc.TargetId);
                        if (isLiveNews)
                        {
                            _liveNewsTargets.Add(desc.TargetId);
                        }

                        if (isCr)
                        {
                            string fileUsed = isHeardNow ? "heard_now.txt" : "post_history_instructions.txt";
                            if (crVersion != null)
                            {
                                var v = crVersion.Value;
                                if (isLiveNews)
                                {
                                    ModLog.Info($"AI target '{desc.TargetId}': live prompt variables '{{{{{AiLiveNewsBlock.VariableName}}}}}' and '{{{{{AiLiveNewsBlock.HeardNowVariableName}}}}}' registered before the main menu (Calradia Remembers v{v.Major}.{v.Minor}.{v.Revision}: news is given live through {fileUsed}, memories are not added; it returns an empty string while Vivid World or AI integration is off).");
                                }
                                else
                                {
                                    ModLog.Info($"AI target '{desc.TargetId}': live prompt variables '{{{{{AiLiveNewsBlock.VariableName}}}}}' and '{{{{{AiLiveNewsBlock.HeardNowVariableName}}}}}' registered before the main menu (Calradia Remembers v{v.Major}.{v.Minor}.{v.Revision}: older than v2.7.7, variables stay empty and memories are added as before).");
                                }
                            }
                            else
                            {
                                ModLog.Info($"AI target '{desc.TargetId}': live prompt variables '{{{{{AiLiveNewsBlock.VariableName}}}}}' and '{{{{{AiLiveNewsBlock.HeardNowVariableName}}}}}' registered before the main menu (Calradia Remembers: news is given live through heard_now.txt, memories are not added; it returns an empty string while Vivid World or AI integration is off).");
                            }
                        }
                        else
                        {
                            ModLog.Info($"AI target '{desc.TargetId}': live prompt variable '{{{{{AiLiveNewsBlock.VariableName}}}}}' registered before the main menu; it returns an empty string while Vivid World or AI integration is off.");
                        }
                    }
                    catch (Exception ex)
                    {
                        if (isCr)
                        {
                            ModLog.Warn($"AI target '{desc.TargetId}': registering live prompt variables failed ({ex.Message}); CR prompts will show literal tokens and memories will be added instead.");
                        }
                        else
                        {
                            ModLog.Warn($"AI target '{desc.TargetId}': registering live prompt variable '{{{{{AiLiveNewsBlock.VariableName}}}}}' failed ({ex.Message}); prompts will show literal tokens and memories will be added instead.");
                        }
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

        public static void SetSession(WorldEventStore store, string campaignId, string pushStorePath, string memoryWrittenPath)
        {
            lock (_lock)
            {
                _store = store;
                _campaignId = campaignId;
                _pushStorePath = pushStorePath;
                _pushStore = AiPushedStore.LoadFromFile(pushStorePath);
                _memoryWrittenPath = memoryWrittenPath;
                _memoryWritten = AiPushedStore.LoadFromFile(memoryWrittenPath);
                _memoryOffReasonLogged = null;
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
                if (_memoryWritten != null && _memoryWritten.IsDirty && !string.IsNullOrEmpty(_memoryWrittenPath))
                {
                    _memoryWritten.SaveToFile(_memoryWrittenPath!);
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
                if (!string.IsNullOrEmpty(_memoryWrittenPath))
                {
                    _memoryWritten = AiPushedStore.LoadFromFile(_memoryWrittenPath!);
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
                _memoryWrittenPath = null;
                _memoryWritten = null;
                _memoryOffReasonLogged = null;
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

        /// <summary>
        /// 這位 NPC 在 VW 裡記得的事（已排序、已篩過知情、遺忘、過時、停用、自己不講）。
        /// 即時交消息、對話開始補寫與開發者預覽都從這裡拿清單。
        /// </summary>
        private static NpcRecallResult QueryRecall(
            Hero hero,
            double currentDay,
            VividWorldConfig cfg,
            WorldEventStore store,
            bool logSecrets)
        {
            var eventIds = store.KnownBy.EventsKnownBy(hero.StringId, currentDay);
            if (eventIds == null || eventIds.Count == 0)
            {
                return new NpcRecallResult(hero.StringId, currentDay, Array.Empty<NpcRecalledMemory>(), Array.Empty<RecallExclusion>(), 0);
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
                traits: store.Traits,
                playerHeroId: Hero.MainHero?.StringId,
                secretLookup: new VividWorld.Campaign.GameSecretRelationshipLookup());

            if (logSecrets)
            {
                foreach (var cand in candidateEvents)
                {
                    if (cand.Origin == EventOrigin.Secret)
                    {
                        var handed = recallResult.Items.FirstOrDefault(i => string.Equals(i.EventId, cand.EventId, StringComparison.Ordinal));
                        if (handed != null)
                        {
                            ModLog.Info($"AI secret candidate {cand.EventId} for {hero.StringId}: handed over (state={handed.SecretState}, key={handed.SecretSentenceKey})");
                        }
                        else
                        {
                            var excl = recallResult.Exclusions.FirstOrDefault(e => string.Equals(e.EventId, cand.EventId, StringComparison.Ordinal));
                            ModLog.Info($"AI secret candidate {cand.EventId} for {hero.StringId}: not handed over (reason={excl?.Reason.ToString() ?? "unknown"}{(excl?.Detail != null ? $": {excl.Detail}" : "")})");
                        }
                    }
                }
            }

            return recallResult;
        }

        /// <summary>
        /// 把一則記得的事組成交給 AI 模組的文字：開頭語照這位 NPC 是不是事件角色挑，事實超過上限時整塊拿掉。
        /// 即時交消息、對話開始加記憶與寫進對方記憶都走這一個，當事人講的那一版才會三處一致。
        /// </summary>
        private static (string Text, bool IsTruncated) RenderRecallText(
            NpcRecalledMemory candidate,
            string heroId,
            VividWorldConfig cfg,
            string pushLang)
        {
            bool isParticipant = candidate.Event.RoleOf(heroId) != null;
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
                    var composed = RumorTextComposer.Compose(candidate.Event, subset, cfg.Presentation, prefix, heroId, candidate.SourceHeroId);
                    if (!string.IsNullOrEmpty(candidate.SecretSentenceKey))
                    {
                        string secKey = candidate.SecretSentenceKey!;
                        composed.SentenceKeyCandidate = secKey;
                        composed.SentenceKeyCandidates = new[] { secKey };
                    }
                    return FallbackTextRenderer.RenderRecallMemory(composed, pushLang, cfg.Presentation);
                });

            return (text, isTruncated);
        }

        /// <param name="excludeWrittenMemories">
        /// 為 true 時，已經寫進 Calradia Remembers 記憶的那幾則從清單拿掉並記下排除原因；
        /// 只有交給它的路徑要這樣做，交給別的 AI 模組的不能因為 CR 寫過就跳過。
        /// </param>
        private static bool ExtractRecallNewsItems(
            Hero hero,
            double currentDay,
            VividWorldConfig cfg,
            WorldEventStore store,
            bool excludeWrittenMemories,
            out NpcRecallResult recallResult,
            out List<RecallRenderedItem> renderedItems)
        {
            renderedItems = new List<RecallRenderedItem>();
            recallResult = QueryRecall(hero, currentDay, cfg, store, logSecrets: true);

            if (excludeWrittenMemories)
            {
                AiPushedStore? written;
                lock (_lock)
                {
                    written = _memoryWritten;
                }
                recallResult = AiMemoryWritePolicy.ExcludeWritten(recallResult, written);
            }

            if (recallResult.Items.Count == 0)
            {
                return false;
            }

            string pushLang = cfg.Ai.PushLanguage ?? "english";
            string? mainHeroId = Hero.MainHero?.StringId;

            foreach (var candidate in recallResult.Items)
            {
                var (text, isTruncated) = RenderRecallText(candidate, hero.StringId, cfg, pushLang);

                string kind = AiMemoryKind.For(candidate.Event, hero.StringId, mainHeroId, candidate.Hop);
                int daysAgo = (int)Math.Max(0, Math.Floor(currentDay - candidate.LearnedDay));
                var newsItem = new AiLiveNewsItem(candidate.EventId, kind, daysAgo, text);

                renderedItems.Add(new RecallRenderedItem(candidate, text, isTruncated, kind, daysAgo, newsItem));
            }

            return true;
        }

        // ---- 把當事人自己的親身經歷寫進 Calradia Remembers 的記憶 ----

        private sealed class MemoryWriteContext
        {
            public VividWorldConfig Config { get; }
            public WorldEventStore Store { get; }
            public TargetBindingState Binding { get; }
            public AiPushedStore Written { get; }
            public string? WrittenPath { get; }
            public string? PlayerHeroId { get; }
            public IFactRetentionPolicy Retention { get; }

            public MemoryWriteContext(VividWorldConfig config, WorldEventStore store, TargetBindingState binding, AiPushedStore written, string? writtenPath)
            {
                Config = config;
                Store = store;
                Binding = binding;
                Written = written;
                WrittenPath = writtenPath;
                PlayerHeroId = !string.IsNullOrEmpty(store.PlayerHeroId) ? store.PlayerHeroId : Hero.MainHero?.StringId;
                Retention = FactRetentionPolicies.Create(config, new SplitMix64Rng(), store.CampaignSeed);
            }
        }

        private static void LogMemoryWritingOffOnce(string reason)
        {
            lock (_lock)
            {
                if (_memoryOffReasonLogged != null) return;
                _memoryOffReasonLogged = reason;
            }
            ModLog.Info($"AI memory writing to '{AiMemoryWritePolicy.TargetId}' is off: {reason}.");
        }

        private static MemoryWriteContext? GetMemoryWriteContext(WorldEventStore? storeOverride)
        {
            VividWorldConfig? cfg;
            WorldEventStore? store;
            AiPushedStore? written;
            string? writtenPath;
            TargetBindingState? binding;

            lock (_lock)
            {
                cfg = _config;
                store = storeOverride ?? _store;
                written = _memoryWritten;
                writtenPath = _memoryWrittenPath;
                _bindings.TryGetValue(AiMemoryWritePolicy.TargetId, out binding);
            }

            string? off = null;
            if (cfg == null || !cfg.Enabled || !cfg.Ai.Enabled)
            {
                off = "VividWorld or AI integration is off in config";
            }
            else if (binding == null || !binding.Result.IsEnabled || binding.AddMemoryMethod == null || binding.MemoryEntryType == null)
            {
                off = "Calradia Remembers is not bound";
            }
            else if (store == null || written == null)
            {
                off = "no campaign session yet";
            }

            if (off != null)
            {
                LogMemoryWritingOffOnce(off);
                return null;
            }

            return new MemoryWriteContext(cfg!, store!, binding!, written!, writtenPath);
        }

        /// <summary>
        /// 他知情紀錄上保留的那幾塊事實；紀錄沒有明列時照保留規則算，跟回想查詢的算法一樣。
        /// 沒有紀錄、或一塊事實都沒有就回傳 null。
        /// </summary>
        private static NpcRecalledMemory? BuildMemoryCandidate(WorldEvent evt, string heroId, MemoryWriteContext ctx)
        {
            var entry = evt.EntryFor(heroId);
            if (entry == null) return null;

            IReadOnlyList<Fact> facts;
            if (entry.KnownFactIds != null)
            {
                var factSet = new HashSet<string>(entry.KnownFactIds, StringComparer.OrdinalIgnoreCase);
                facts = evt.Facts.Where(f => factSet.Contains(f.Id)).ToList();
            }
            else
            {
                facts = ctx.Retention.Retain(evt, entry.Hop, heroId);
            }

            if (facts.Count == 0) return null;

            bool isCorrection = false;
            if (!string.IsNullOrEmpty(evt.LinkedEventId))
            {
                isCorrection = ctx.Store.Load(evt.LinkedEventId!)?.IsKnownBy(heroId) == true;
            }

            return new NpcRecalledMemory
            {
                EventId = evt.EventId,
                Event = evt,
                Entry = entry,
                Facts = facts,
                Hop = entry.Hop,
                SourceHeroId = entry.SourceHeroId,
                LearnedDay = entry.LearnedDay,
                Interest = entry.Interest,
                IsCorrection = isCorrection
            };
        }

        private static void SetEntryProperty(TargetBindingState binding, object entry, string name, object? value)
        {
            if (!binding.EntryProperties.Contains(name)) return;
            binding.MemoryEntryType!.GetProperty(name)?.SetValue(entry, value);
        }

        /// <summary>
        /// 判斷一位英雄要不要寫、能寫就寫進對方的記憶。
        /// </summary>
        /// <param name="recalled">對話開始補寫時，回想查詢已經交出來的那一則；沒有就照知情紀錄現組。</param>
        /// <param name="when">"at event" 或 "backfill"，只用在日誌。</param>
        private static void WriteMemoryForHero(MemoryWriteContext ctx, WorldEvent evt, string heroId, NpcRecalledMemory? recalled, double currentDay, string when)
        {
            string head = $"AI memory for '{heroId}' to '{AiMemoryWritePolicy.TargetId}': {evt.EventId}";

            var decision = AiMemoryWritePolicy.Decide(
                evt,
                heroId,
                ctx.PlayerHeroId,
                EventCatalogStore.TemplateByType,
                (h, e) => ctx.Written.HasPushedVersion(AiMemoryWritePolicy.TargetId, h, e, AiMemoryWritePolicy.RecordVersion));

            if (!decision.Write)
            {
                // 事件當下每位知情者都會碰到「不是角色」，印出來只是雜訊
                if (!(decision.Reason == AiMemorySkipReason.NotParticipant && when == "at event"))
                {
                    ModLog.Info($"{head} skipped: {decision.ReasonText}");
                }
                return;
            }

            Hero? hero = Hero.Find(heroId);
            if (hero == null)
            {
                ModLog.Info($"{head} skipped: hero not found");
                return;
            }

            string? rejectReason = EligibilityLabel.GetRejectionReason(hero, ctx.Store.Traits);
            if (rejectReason != null)
            {
                ModLog.Info($"{head} skipped: {rejectReason}");
                return;
            }

            var candidate = recalled ?? BuildMemoryCandidate(evt, heroId, ctx);
            if (candidate == null)
            {
                ModLog.Info($"{head} skipped: no facts retained");
                return;
            }

            string pushLang = ctx.Config.Ai.PushLanguage ?? "english";
            var (text, isTruncated) = RenderRecallText(candidate, heroId, ctx.Config, pushLang);
            if (string.IsNullOrEmpty(text))
            {
                ModLog.Info($"{head} skipped: empty text");
                return;
            }

            string kind = AiMemoryKind.For(evt, heroId, ctx.PlayerHeroId, 0);
            bool isPrivate = AiMemoryWritePolicy.IsPrivate(evt, heroId, EventCatalogStore.TemplateByType, ctx.Store.Traits);
            int daysAgo = AiMemoryWritePolicy.DaysAgo(currentDay, candidate.LearnedDay);

            string resStr;
            bool succeeded;
            try
            {
                var binding = ctx.Binding;
                object entry = Activator.CreateInstance(binding.MemoryEntryType!);
                SetEntryProperty(binding, entry, "NpcId", heroId);
                SetEntryProperty(binding, entry, "Summary", text);
                SetEntryProperty(binding, entry, "Kind", kind);
                SetEntryProperty(binding, entry, "DaysAgo", daysAgo);
                SetEntryProperty(binding, entry, "Private", isPrivate);
                SetEntryProperty(binding, entry, "SourceMod", "VividWorld");

                object? invokeResult = binding.AddMemoryMethod!.Invoke(null, new object[] { entry });
                resStr = invokeResult?.ToString() ?? "Unknown";
                succeeded = string.Equals(resStr, "Added", StringComparison.OrdinalIgnoreCase) ||
                            (invokeResult is int intRes && intRes == 0);
            }
            catch (Exception ex)
            {
                ModLog.Error($"{head} write threw ({when})", ex);
                return;
            }

            string detail = string.Format(CultureInfo.InvariantCulture,
                "({0}), kind={1}, private={2}, chars={3}, truncated={4}, result={5}",
                when, kind, isPrivate.ToString().ToLowerInvariant(), text.Length, isTruncated.ToString().ToLowerInvariant(), resStr);

            if (!succeeded)
            {
                ModLog.Info($"{head} not written {detail}; it will be tried again at the next chance.");
                return;
            }

            ctx.Written.Record(AiMemoryWritePolicy.TargetId, heroId, evt.EventId, AiMemoryWritePolicy.RecordVersion, currentDay);
            if (!string.IsNullOrEmpty(ctx.WrittenPath))
            {
                ctx.Written.SaveToFile(ctx.WrittenPath!);
            }
            ModLog.Info($"{head} written {detail}");
        }

        /// <summary>
        /// 事件剛進事件庫、當事人的知情紀錄剛種下的那一刻：把每位事件角色自己講的那一版寫進他的記憶。
        /// </summary>
        internal static void WriteMemoriesForNewEvent(WorldEvent evt, WorldEventStore store)
        {
            if (evt == null) return;
            try
            {
                var ctx = GetMemoryWriteContext(store);
                if (ctx == null) return;

                double currentDay = CampaignTime.Now.ToDays;
                var seen = new HashSet<string>(StringComparer.Ordinal);
                foreach (var heroId in evt.Participants.Values.ToList())
                {
                    if (string.IsNullOrEmpty(heroId) || !seen.Add(heroId)) continue;
                    WriteMemoryForHero(ctx, evt, heroId, null, currentDay, "at event");
                }
            }
            catch (Exception ex)
            {
                ModLog.Error($"AI memory write for new event {evt.EventId} failed", ex);
            }
        }

        /// <summary>
        /// 玩家用 Calradia Remembers 找這位 NPC 說話時，補寫他還記得、自己是角色、還沒寫過的事
        /// （更新前發生的，或當時 CR 沒綁上而沒寫成的）。
        /// 回想查詢把「自己不講」的事擋在清單外，但那一類正是要標成私下才講寫進去的，所以也一併補。
        /// </summary>
        private static void BackfillMemories(string npcId)
        {
            try
            {
                var ctx = GetMemoryWriteContext(null);
                if (ctx == null) return;

                Hero? hero = Hero.Find(npcId);
                if (hero == null)
                {
                    ModLog.Info($"AI memory backfill for '{npcId}' to '{AiMemoryWritePolicy.TargetId}': hero not found.");
                    return;
                }

                double currentDay = CampaignTime.Now.ToDays;
                var recall = QueryRecall(hero, currentDay, ctx.Config, ctx.Store, logSecrets: false);

                foreach (var item in recall.Items)
                {
                    if (item.Event.RoleOf(npcId) == null) continue;
                    WriteMemoryForHero(ctx, item.Event, npcId, item, currentDay, "backfill");
                }

                foreach (var exc in recall.Exclusions)
                {
                    if (exc.Reason != RecallExclusionReason.WontTellOwn) continue;
                    var evt = ctx.Store.Load(exc.EventId);
                    if (evt == null || evt.RoleOf(npcId) == null) continue;
                    WriteMemoryForHero(ctx, evt, npcId, null, currentDay, "backfill");
                }
            }
            catch (Exception ex)
            {
                ModLog.Error($"AI memory backfill for '{npcId}' failed", ex);
            }
        }

        /// <summary>開發者工具用：VW 寫進這位 NPC 的 Calradia Remembers 記憶的每一則，文字是現在照知情紀錄重新組出來的。</summary>
        internal static string DescribeWrittenMemories(Hero hero, WorldEventStore store, VividWorldConfig cfg)
        {
            AiPushedStore? written;
            TargetBindingState? binding;
            string? writtenPath;
            lock (_lock)
            {
                written = _memoryWritten;
                writtenPath = _memoryWrittenPath;
                _bindings.TryGetValue(AiMemoryWritePolicy.TargetId, out binding);
            }

            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"What Vivid World wrote into the '{AiMemoryWritePolicy.TargetId}' memory of {hero.Name} ({hero.StringId}):");

            var records = written?.GetPushedForHero(AiMemoryWritePolicy.TargetId, hero.StringId)
                          ?? new Dictionary<string, AiPushRecord>();
            if (records.Count == 0)
            {
                sb.AppendLine("  none");
                return sb.ToString().TrimEnd();
            }

            MemoryWriteContext? ctx = null;
            if (written != null && binding != null)
            {
                ctx = new MemoryWriteContext(cfg, store, binding, written, writtenPath);
            }

            string pushLang = cfg.Ai.PushLanguage ?? "english";
            foreach (var kv in records.OrderBy(r => r.Value.PushedDay).ThenBy(r => r.Key, StringComparer.Ordinal))
            {
                var evt = store.Load(kv.Key);
                if (evt == null)
                {
                    sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                        "  {0}: written on day {1:F1}; the event is no longer in the store", kv.Key, kv.Value.PushedDay));
                    continue;
                }

                string kind = AiMemoryKind.For(evt, hero.StringId, Hero.MainHero?.StringId, 0);
                bool isPrivate = AiMemoryWritePolicy.IsPrivate(evt, hero.StringId, EventCatalogStore.TemplateByType, store.Traits);
                string textLine = "(text could not be rebuilt)";
                if (ctx != null)
                {
                    var candidate = BuildMemoryCandidate(evt, hero.StringId, ctx);
                    if (candidate != null)
                    {
                        textLine = RenderRecallText(candidate, hero.StringId, cfg, pushLang).Text;
                    }
                }

                sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                    "  {0}: kind={1}, private={2}, written on day {3:F1}",
                    kv.Key, kind, isPrivate.ToString().ToLowerInvariant(), kv.Value.PushedDay));
                sb.AppendLine($"    {textLine}");
            }

            return sb.ToString().TrimEnd();
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

            // 補寫在「即時模式就跳過」之前：即時模式只是不再另外加記憶，
            // 當事人自己的親身經歷 CR 記不到，每個版本都要寫
            if (string.Equals(targetId, AiMemoryWritePolicy.TargetId, StringComparison.OrdinalIgnoreCase))
            {
                BackfillMemories(npcId);
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
                bool excludeWritten = string.Equals(targetId, AiMemoryWritePolicy.TargetId, StringComparison.OrdinalIgnoreCase);
                if (!ExtractRecallNewsItems(hero!, currentDay, cfg, store, excludeWritten, out var recallResult, out var renderedItems))
                {
                    var emptySb = new System.Text.StringBuilder();
                    emptySb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                        "AI push for '{0}' to '{1}': {2} candidates, 0 pushed, {3} excluded, elapsed {4:F1}ms.",
                        hero!.StringId, targetId, recallResult.CandidateCount, recallResult.Exclusions.Count, sw.Elapsed.TotalMilliseconds));
                    if (recallResult.Exclusions.Count > 0)
                    {
                        emptySb.AppendLine("  Excluded:");
                        foreach (var exc in recallResult.Exclusions)
                        {
                            emptySb.AppendLine($"    {exc}");
                        }
                    }
                    ModLog.Info(emptySb.ToString().TrimEnd());
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

        private static string ProvideNewsVariable<TFacts>(TFacts facts)
        {
            return ProvideLiveNews(facts, AiLiveNewsBlock.VariableName);
        }

        private static string ProvideHeardNowVariable<TFacts>(TFacts facts)
        {
            return ProvideLiveNews(facts, AiLiveNewsBlock.HeardNowVariableName);
        }

        private static string ProvideLiveNews<TFacts>(TFacts facts, string variableName)
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

            bool isCrLive;
            string? crActiveVar;
            lock (_lock)
            {
                isCrLive = _liveNewsTargets.Contains("CalradiaRemembers");
                crActiveVar = _crActiveVariable;
            }

            if (!isCrLive)
            {
                ModLog.Info($"AI live news for '{npcId}' [{variableName}] (lean={lean.ToString().ToLowerInvariant()}): empty (Calradia Remembers older than v2.7.7; memories are added instead).");
                return string.Empty;
            }

            if (!string.Equals(variableName, crActiveVar, StringComparison.OrdinalIgnoreCase))
            {
                ModLog.Info($"AI live news for '{npcId}' [{variableName}] (lean={lean.ToString().ToLowerInvariant()}): empty (given through the other file).");
                return string.Empty;
            }

            VividWorldConfig? cfg;
            WorldEventStore? store;

            lock (_lock)
            {
                cfg = _config;
                store = _store;
            }

            if (cfg == null || !cfg.Enabled || !cfg.Ai.Enabled)
            {
                ModLog.Info($"AI live news for '{npcId}' [{variableName}] (lean={lean.ToString().ToLowerInvariant()}): empty (AI integration or mod disabled in config).");
                return string.Empty;
            }

            if (store == null || TaleWorlds.CampaignSystem.Campaign.Current == null)
            {
                ModLog.Info($"AI live news for '{npcId}' [{variableName}] (lean={lean.ToString().ToLowerInvariant()}): empty (no active campaign or store).");
                return string.Empty;
            }

            try
            {
                Hero? hero = Hero.Find(npcId);
                if (hero == null)
                {
                    ModLog.Info($"AI live news for '{npcId}' [{variableName}] (lean={lean.ToString().ToLowerInvariant()}): empty (hero not found).");
                    return string.Empty;
                }

                string? rejectReason = EligibilityLabel.GetRejectionReason(hero, store.Traits);
                if (rejectReason != null)
                {
                    ModLog.Info($"AI live news for '{npcId}' [{variableName}] (lean={lean.ToString().ToLowerInvariant()}): empty (hero ineligible: {rejectReason}).");
                    return string.Empty;
                }

                double currentDay = CampaignTime.Now.ToDays;
                int maxCharLimit = AiLiveNewsBlock.GetMaxChars(variableName, lean);

                if (!ExtractRecallNewsItems(hero, currentDay, cfg, store, true, out var recallResult, out var renderedItems) || renderedItems.Count == 0)
                {
                    var emptySb = new System.Text.StringBuilder();
                    emptySb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                        "AI live news for '{0}' [{1}] (lean={2}): {3} candidates, 0 given, {4} excluded, 0 chars of {5}, elapsed {6:F1}ms.",
                        npcId, variableName, lean.ToString().ToLowerInvariant(), recallResult.CandidateCount, recallResult.Exclusions.Count, maxCharLimit, sw.Elapsed.TotalMilliseconds));
                    if (recallResult.Exclusions.Count > 0)
                    {
                        emptySb.AppendLine("  Excluded:");
                        foreach (var exc in recallResult.Exclusions)
                        {
                            emptySb.AppendLine($"    {exc}");
                        }
                    }
                    ModLog.Info(emptySb.ToString().TrimEnd());
                    return string.Empty;
                }

                var liveItems = new List<AiLiveNewsItem>(renderedItems.Count);
                for (int i = 0; i < renderedItems.Count; i++)
                {
                    liveItems.Add(renderedItems[i].NewsItem);
                }

                string heroName = hero.Name?.ToString() ?? hero.StringId;
                var blockResult = AiLiveNewsBlock.BuildForVariable(variableName, heroName, lean, liveItems);

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
                    "AI live news for '{0}' [{1}] (lean={2}): {3} candidates, {4} given, {5} excluded, {6} chars of {7}, elapsed {8:F1}ms.",
                    hero.StringId,
                    variableName,
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
                ModLog.Error($"AI live news for '{npcId}' [{variableName}] failed", ex);
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

            ExtractRecallNewsItems(hero, currentDay, config, store, true, out var recallResult, out var renderedItems);

            var liveItems = new List<AiLiveNewsItem>(renderedItems.Count);
            for (int i = 0; i < renderedItems.Count; i++)
            {
                liveItems.Add(renderedItems[i].NewsItem);
            }

            string heroName = hero.Name?.ToString() ?? hero.StringId;
            var standardBlock = AiLiveNewsBlock.BuildForVariable(_crActiveVariable, heroName, lean: false, liveItems);
            var leanBlock = AiLiveNewsBlock.BuildForVariable(_crActiveVariable, heroName, lean: true, liveItems);

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
