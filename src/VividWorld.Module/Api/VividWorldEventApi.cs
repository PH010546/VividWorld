using System;
using System.Collections.Generic;
using TaleWorlds.CampaignSystem;
using VividWorld.Campaign;
using VividWorld.Core.Events;
using VividWorld.Core.Persistence;

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
