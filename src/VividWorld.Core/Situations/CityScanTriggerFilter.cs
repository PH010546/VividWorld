using System;

namespace VividWorld.Core.Situations
{
    /// <summary>城裡的每日情境掃描可以收的觸發種類。只收 direct：afterEvent 要靠一件事件才會發生，
    /// 流浪者的觸發（wandererBattle、wandererDaily）有它們自己的擲骰，不該被城裡的掃描再擲一次。</summary>
    public static class CityScanTriggerFilter
    {
        public static bool IsCityScanTrigger(string? trigger) =>
            string.Equals(trigger, "direct", StringComparison.OrdinalIgnoreCase);
    }
}
