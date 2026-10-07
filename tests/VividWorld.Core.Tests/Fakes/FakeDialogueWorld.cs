#nullable enable
using System;
using System.Collections.Generic;
using VividWorld.Core.Dialogue;
using VividWorld.Core.Feelings;
using VividWorld.Core.Grudges;
using VividWorld.Core.Memory;

namespace VividWorld.Core.Tests.Fakes
{
    public sealed class FakeDialogueWorld : IDialogueWorld
    {
        public double Today { get; set; } = 100.0;
        public string? PlayerKingdomLeaderId { get; set; }

        public Dictionary<(string Speaker, string Target), int> Affections { get; } = new();
        public Dictionary<string, int> StandingRanks { get; } = new();
        public Dictionary<string, InterestHeroFacts> Facts { get; } = new();
        public Dictionary<(string Speaker, string Target), List<GrudgeEntry>> Grudges { get; } = new();
        public Dictionary<string, string> Names { get; } = new();
        public Dictionary<string, bool> Females { get; } = new();

        public HashSet<string> PlayerCompanions { get; } = new();
        public HashSet<string> PlayerClanMembers { get; } = new();
        public HashSet<string> PlayerSpouses { get; } = new();

        public int? Affection(string speakerId, string heroId)
        {
            if (Affections.TryGetValue((speakerId, heroId), out int val)) return val;
            return 0;
        }

        public int? StandingRank(string heroId)
        {
            if (StandingRanks.TryGetValue(heroId, out int val)) return val;
            return 0;
        }

        public InterestHeroFacts? InterestFacts(string heroId)
        {
            if (Facts.TryGetValue(heroId, out var facts)) return facts;
            return new InterestHeroFacts(heroId, clanId: null);
        }

        public IReadOnlyList<GrudgeEntry> PersonalGrudges(string speakerId, string heroId)
        {
            if (Grudges.TryGetValue((speakerId, heroId), out var list)) return list;
            return Array.Empty<GrudgeEntry>();
        }

        public string NameOf(string heroId)
        {
            if (Names.TryGetValue(heroId, out string? name)) return name;
            return heroId;
        }

        public bool? IsFemale(string heroId)
        {
            if (Females.TryGetValue(heroId, out bool female)) return female;
            return null;
        }

        public bool IsPlayerCompanion(string heroId) => PlayerCompanions.Contains(heroId);
        public bool IsPlayerClanMember(string heroId) => PlayerClanMembers.Contains(heroId);
        public bool IsPlayerSpouse(string heroId) => PlayerSpouses.Contains(heroId);

        public void SetAffection(string a, string b, int value)
        {
            Affections[(a, b)] = value;
            Affections[(b, a)] = value;
        }

        public void SetFacts(string heroId, string? clanId, string? fatherId = null, string? motherId = null, string? spouseId = null, IReadOnlyList<string>? siblingIds = null)
        {
            Facts[heroId] = new InterestHeroFacts(heroId, clanId, fatherId, motherId, spouseId, siblingIds);
        }
    }
}
