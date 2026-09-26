#nullable enable
using System;
using System.Collections.Generic;

namespace VividWorld.Core.Dialogue
{
    public sealed class ListenPreviewPerson
    {
        public string HeroId { get; set; } = string.Empty;
        public string HeroName { get; set; } = string.Empty;
        public bool IsLord { get; set; }
        public HeroSocialProfile Profile { get; set; } = new();
        public IReadOnlyList<RumorCandidate> Candidates { get; set; } = Array.Empty<RumorCandidate>();
        public int KnownCount { get; set; }
        public int ForgottenCount { get; set; }
        public int OutdatedCount { get; set; }
        public int UnstampedCount { get; set; }
    }

    public sealed class ListenPreviewHeroDetail
    {
        public string HeroId { get; set; } = string.Empty;
        public string HeroName { get; set; } = string.Empty;
        public bool IsLord { get; set; }
        public int Relation { get; set; }
        public string VolunteerOutcome { get; set; } = string.Empty;
        public string TopicOnlyOutcome { get; set; } = string.Empty;
        public string AskOutcome { get; set; } = string.Empty;
    }

    public sealed class ListenPreviewResult
    {
        public int Day { get; set; }
        public int TotalNetworkCount { get; set; }
        public int LordCount { get; set; }
        public int WandererCount { get; set; }
        public int OtherCount { get; set; }

        public int VolunteersToday { get; set; }
        public int MaxVolunteersPerDay { get; set; }
        public int VolunteerRelationGate { get; set; }
        public int HeroesMeetingVolunteerRelationGate { get; set; }
        public int ChatRelationGate { get; set; }
        public int HeroesMeetingChatRelationGate { get; set; }
        public int VolunteerToldFullCount { get; set; }
        public int VolunteerToldGistCount { get; set; }
        public string ModeLine { get; set; } = string.Empty;
        public int UnstampedEntriesCount { get; set; }
        public long ElapsedMilliseconds { get; set; }
        public string LordAttackNote { get; set; } = "lordAttack not previewed (conversation-only)";

        // 1. Volunteer path counts & top 5 names
        public Dictionary<string, int> VolunteerCounts { get; } = new(StringComparer.Ordinal);
        public Dictionary<string, List<string>> VolunteerNames { get; } = new(StringComparer.Ordinal);

        // 2. Volunteer Topic-Only counts & top 5 names
        public Dictionary<string, int> TopicOnlyCounts { get; } = new(StringComparer.Ordinal);
        public Dictionary<string, List<string>> TopicOnlyNames { get; } = new(StringComparer.Ordinal);

        // 3. Ask path counts & top 5 names
        public bool AskBlockedByClanTier { get; set; }
        public int PlayerClanTier { get; set; }
        public int AskMinClanTier { get; set; }
        public string AskClanTierStatus { get; set; } = string.Empty;
        public Dictionary<string, int> AskCounts { get; } = new(StringComparer.Ordinal);
        public Dictionary<string, List<string>> AskNames { get; } = new(StringComparer.Ordinal);

        // 3b. Ask refusal lines counts & top 5 names
        public Dictionary<string, int> AskRefusalCounts { get; } = new(StringComparer.Ordinal);
        public Dictionary<string, List<string>> AskRefusalNames { get; } = new(StringComparer.Ordinal);

        // 4. Relation histogram counts & top 5 names
        public Dictionary<string, int> RelationHist { get; } = new(StringComparer.Ordinal);
        public Dictionary<string, List<string>> RelationHistNames { get; } = new(StringComparer.Ordinal);

        // 5. Per-hero details for dev dialogue
        public List<ListenPreviewHeroDetail> HeroDetails { get; } = new();
    }
}
