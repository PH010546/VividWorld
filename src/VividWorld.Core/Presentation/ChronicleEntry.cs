namespace VividWorld.Core.Presentation
{
    public sealed class ChronicleEntry
    {
        public string EventId = string.Empty;
        public string EventType = string.Empty;
        public double Day;
        public int PlayerHop;
        public string? SourceHeroId;
        public string? LinkedEventId;

        public string HeadlineTextId = string.Empty;   // 慣例：VividWorld_EventType_<type>
        public string HeadlineFallback = string.Empty; // 英文，來自模板的 headline 欄位

        public ComposedRumor Body = new();

        // 以下由 Module 建 VM 時填，Core 一律留空字串／null
        public string DayLabel = string.Empty;
        public string? SourceHeroName;
    }
}
