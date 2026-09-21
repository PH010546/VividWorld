namespace VividWorld.Core.Rumors
{
    public sealed class TraitProfile
    {
        public string HeroId { get; set; } = string.Empty;
        public int Honor, Mercy, Valor, Calculating, Generosity;   // -2..2
        public bool IsAlive, IsPrisoner, IsLord, IsWanderer;
    }
}
