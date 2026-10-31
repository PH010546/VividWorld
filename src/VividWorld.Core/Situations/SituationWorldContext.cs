using System;
using System.Collections.Generic;
using VividWorld.Core.Rumors;
using VividWorld.Core.Util;

namespace VividWorld.Core.Situations
{
    public sealed class SituationWorldContext
    {
        public IHeroTraitLookup? Traits { get; set; }
        public Func<string, string, int>? GrudgeSum { get; set; }
        public Func<string, string, int?>? Affection { get; set; }
        public Func<string, IReadOnlyList<string>, string, bool>? RemembersEventAbout { get; set; }
        public Func<string, IReadOnlyList<string>, string, string?>? FindRememberedEventAbout { get; set; }
        public IDeterministicRng? Rng { get; set; }
        public long Seed { get; set; }
        public int GrudgeLine { get; set; } = -5;
        public int NativeGrudgeLine { get; set; } = -20;
        public string? PlayerHeroId { get; set; }
        public Config.VividWorldConfig? Config { get; set; }

        public Func<string, bool>? IsPartyRoleHolder { get; set; }
        public Func<string, string, bool>? HasTeachableSkill { get; set; }
        public Func<string, string, string, bool>? IsSkillBelow { get; set; }
        public Func<string, bool>? FoughtInBattle { get; set; }
        public Func<string, bool>? WoundedInBattle { get; set; }
        public Func<bool>? BattleWon { get; set; }
    }
}
