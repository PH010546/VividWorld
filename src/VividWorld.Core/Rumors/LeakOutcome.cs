namespace VividWorld.Core.Rumors
{
    public sealed class LeakOutcome
    {
        public bool Leaked { get; }
        public string? LeakerHeroId { get; }

        public static LeakOutcome None { get; } = new LeakOutcome(false, null);

        public static LeakOutcome By(string heroId) => new LeakOutcome(true, heroId);

        private LeakOutcome(bool leaked, string? leakerHeroId)
        {
            Leaked = leaked;
            LeakerHeroId = leakerHeroId;
        }
    }
}
