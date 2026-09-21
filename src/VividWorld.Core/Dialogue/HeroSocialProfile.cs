using VividWorld.Core.Rumors;

namespace VividWorld.Core.Dialogue
{
    public sealed class HeroSocialProfile
    {
        public string HeroId = string.Empty;
        public int RelationWithPlayer;
        public bool IsPlayerSpouse, IsPlayerCompanion, IsPlayerClanMember;
        public TraitProfile Traits = new();
        public double LastVolunteeredDay = -1;
    }
}
