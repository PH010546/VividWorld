using VividWorld.Core.Events;

namespace VividWorld.Core.Grudges
{
    public sealed class GrudgeEntry
    {
        public string EventId { get; set; } = string.Empty;
        public string FromHeroId { get; set; } = string.Empty;
        public string AboutHeroId { get; set; } = string.Empty;
        public double Day { get; set; }
        public double Requested { get; set; }
        public int Delta { get; set; }
        public GrudgeScope Scope { get; set; }
        public GrudgeSource Source { get; set; } = GrudgeSource.Situation;
        public bool LedgerOnly { get; set; }
        public string? EscalatedFrom { get; set; }
    }
}
