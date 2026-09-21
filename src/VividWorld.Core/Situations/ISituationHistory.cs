using System.Collections.Generic;

namespace VividWorld.Core.Situations
{
    public interface ISituationHistory
    {
        /// <summary>這組英雄最近一次跑過這個情境是哪一天、哪一則事件；沒有就回 null。</summary>
        SituationOccurrence? LastOccurrence(string situationId, IReadOnlyCollection<string> heroIds);
    }

    public sealed class SituationOccurrence
    {
        public double Day;
        public string EventId = string.Empty;
    }
}
