using System.Collections.Generic;
using VividWorld.Core.Rumors;

namespace VividWorld.Core.Tests.Fakes
{
    internal sealed class FakeHeroTraitLookup : IHeroTraitLookup
    {
        private readonly Dictionary<string, TraitProfile> _profiles = new();

        public void Set(TraitProfile profile)
        {
            if (profile != null && !string.IsNullOrEmpty(profile.HeroId))
            {
                _profiles[profile.HeroId] = profile;
            }
        }

        public TraitProfile? Of(string heroId)
        {
            if (heroId != null && _profiles.TryGetValue(heroId, out var profile))
            {
                return profile;
            }
            return null;
        }
    }
}
