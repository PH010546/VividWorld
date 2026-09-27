#nullable enable
using System;
using VividWorld.Core.Events;

namespace VividWorld.Core.Catalog
{
    /// <summary>
    /// 事件型別停用判定（純函式）。
    /// 當事件模板的 retired 欄位宣告為 true 時，該型別已停用。
    /// 停用的型別不在 NPC 之間傳播、不講給玩家、不交給 AI 模組；紀事視窗不受影響。
    /// 停用名單只在模板 JSON 定義，程式內不硬編碼。
    /// </summary>
    public static class RetiredTypeEvaluator
    {
        public static bool IsRetired(WorldEvent? evt, Func<string, EventTemplate?>? getTemplate)
        {
            if (evt == null || getTemplate == null) return false;
            return IsRetired(evt.Type, getTemplate);
        }

        public static bool IsRetired(string? eventType, Func<string, EventTemplate?>? getTemplate)
        {
            if (string.IsNullOrEmpty(eventType) || getTemplate == null) return false;
            var template = getTemplate(eventType!);
            return template != null && template.Retired;
        }
    }
}
