using System;
using System.Collections.Generic;
using System.Globalization;
using TaleWorlds.CampaignSystem;
using VividWorld.Campaign;
using VividWorld.Core.Config;
using VividWorld.Core.Events;
using VividWorld.Core.Memory;
using VividWorld.Core.Persistence;
using VividWorld.Core.Presentation;
using VividWorld.Core.Rumors;
using VividWorld.Core.Util;
using VividWorld.Presentation;

namespace VividWorld.Api
{
    /// <summary>
    /// 反射友善的公開門面。只用基本型別，消費端不需要組件參照。
    /// 秘密事件在洩漏之前絕不觸發。
    /// </summary>
    public static class VividWorldEventApi
    {
        public const int ApiVersion = 1;

        // (heroId, eventId, type, role, day, summary, detailJson)
        public static event Action<string, string, string, string, double, string, string>? EventOccurred;

#pragma warning disable CS0067
        // (heroId, eventId, hop, renderedText) —— M6 接上，M5 宣告但不觸發
        public static event Action<string, string, int, string>? RumorLearned;
#pragma warning restore CS0067

        internal static WorldEventStore? Store { get; set; }

        /// <summary>
        /// 查詢指定事件的完整 JSON。
        /// 查無此事件、秘密未走漏、或存檔時被清掉的事件查到 null。
        /// </summary>
        public static string? GetEventJson(string eventId)
        {
            if (Store == null || string.IsNullOrEmpty(eventId)) return null;
            try
            {
                var entry = Store.Index.Find(eventId);
                if (entry == null) return null;
                if (entry.Secret && !entry.Leaked) return null;

                var evt = Store.Load(eventId);
                return evt != null ? VividJson.Write(evt) : null;
            }
            catch
            {
                return null;
            }
        }

        public static string[] EventIdsKnownBy(string heroId)
        {
            if (Store == null || string.IsNullOrEmpty(heroId)) return Array.Empty<string>();
            try
            {
                var ids = Store.KnownBy.EventsKnownBy(heroId, CampaignTime.Now.ToDays);
                if (ids == null || ids.Count == 0) return Array.Empty<string>();

                var result = new List<string>();
                foreach (var id in ids)
                {
                    var entry = Store.Index.Find(id);
                    if (entry != null && (!entry.Secret || entry.Leaked))
                    {
                        result.Add(id);
                    }
                }
                return result.ToArray();
            }
            catch
            {
                return Array.Empty<string>();
            }
        }

        public static string[] EventIdsAbout(string heroId)
        {
            if (Store == null || string.IsNullOrEmpty(heroId)) return Array.Empty<string>();
            try
            {
                var ids = Store.KnownBy.EventsAbout(heroId, CampaignTime.Now.ToDays);
                if (ids == null || ids.Count == 0) return Array.Empty<string>();

                var result = new List<string>();
                foreach (var id in ids)
                {
                    var entry = Store.Index.Find(id);
                    if (entry != null && (!entry.Secret || entry.Leaked))
                    {
                        result.Add(id);
                    }
                }
                return result.ToArray();
            }
            catch
            {
                return Array.Empty<string>();
            }
        }

        /// <summary>
        /// 取得當前設定下模組有涵蓋的原生 LogEntry 型別名稱。
        /// 當戰役尚未載入或總開關關閉時回傳空陣列，避免外部模組誤讓世界消息。
        /// </summary>
        public static string[] CoveredNativeLogTypes()
        {
            bool isSessionActive = Store != null;
            var config = Store?.Config;
            var list = CoveredLogTypes.GetCoveredLogTypes(config, isSessionActive);
            if (list.Count == 0) return Array.Empty<string>();

            var result = new string[list.Count];
            for (int i = 0; i < list.Count; i++)
            {
                result[i] = list[i];
            }
            return result;
        }

        /// <summary>
        /// 查詢指定 NPC 當前記得的傳聞清單與已組好的一行純文字。
        /// 依更正優先、興趣高、學習日期近、事件編號遞增排序，最多回傳 maxCount 筆。
        /// 參數 language 可傳入 "english" 或 "game"。
        /// </summary>
        public static (string EventId, string Text)[] GetRememberedRumors(string heroId, int maxCount = 8, string language = "english")
        {
            if (Store == null || string.IsNullOrEmpty(heroId)) return Array.Empty<(string EventId, string Text)>();
            try
            {
                double currentDay = CampaignTime.Now.ToDays;
                var eventIds = Store.KnownBy.EventsKnownBy(heroId, currentDay);
                if (eventIds == null || eventIds.Count == 0) return Array.Empty<(string EventId, string Text)>();

                var candidateEvents = new List<WorldEvent>(eventIds.Count);
                foreach (var id in eventIds)
                {
                    var evt = Store.Load(id);
                    if (evt != null)
                    {
                        candidateEvents.Add(evt);
                    }
                }

                var knownSet = new HashSet<string>(eventIds, StringComparer.OrdinalIgnoreCase);
                var retentionPolicy = FactRetentionPolicies.Create(Store.Config, new SplitMix64Rng(), Store.CampaignSeed);

                var queryResult = NpcRecallQuery.Query(
                    heroId,
                    currentDay,
                    maxCount,
                    candidateEvents,
                    retentionPolicy,
                    isEventKnown: id => knownSet.Contains(id),
                    config: Store.Config,
                    getTemplate: EventCatalogStore.TemplateByType,
                    traits: Store.Traits);

                ModLog.Info(string.Format(CultureInfo.InvariantCulture,
                    "Npc recall query for '{0}': {1} candidates, {2} recalled, {3} excluded ({4})",
                    heroId,
                    queryResult.CandidateCount,
                    queryResult.Items.Count,
                    queryResult.Exclusions.Count,
                    queryResult.ExclusionSummary()));

                var cfg = Store.Config?.Presentation;
                var results = new List<(string EventId, string Text)>(queryResult.Items.Count);

                foreach (var item in queryResult.Items)
                {
                    bool isParticipant = item.Event.RoleOf(heroId) != null;
                    var prefix = RumorPrefixSelector.SelectPrefix(item.Hop, item.SourceHeroId, false, item.IsCorrection, isParticipant);
                    var composed = RumorTextComposer.Compose(item.Event, item.Facts, cfg ?? new PresentationConfig(), prefix, heroId);
                    string text = FallbackTextRenderer.RenderRecallMemory(composed, language, cfg);
                    results.Add((item.EventId, text));
                }

                return results.ToArray();
            }
            catch (Exception ex)
            {
                ModLog.Error($"Error querying remembered rumors for hero '{heroId}'", ex);
                return Array.Empty<(string EventId, string Text)>();
            }
        }

        internal static void RaiseEventOccurred(string heroId, string eventId, string type, string role, double day, string summary, string json)
        {
            var handlers = EventOccurred;
            if (handlers == null) return;
            foreach (var d in handlers.GetInvocationList())
            {
                try
                {
                    d.DynamicInvoke(heroId, eventId, type, role, day, summary, json);
                }
                catch
                {
                    // 第三方訂閱者拋例外不得弄壞 tick
                }
            }
        }
    }
}
