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

        public string SharedTodaySummary { get; set; } = string.Empty;
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

        // Volunteer told by reasons & silent by reasons
        public Dictionary<string, int> VolunteerToldReasons { get; } = new(StringComparer.Ordinal);
        public Dictionary<string, int> VolunteerSilentReasons { get; } = new(StringComparer.Ordinal);

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

        // Ask breakdown counts
        public int AskFamiliarCloselyRelatedCount { get; set; }
        public int AskFamiliarBigNewsCount { get; set; }
        public int AskUnfamiliarBigNewsCount { get; set; }
        public int AskUnwillingCount { get; set; }
        public int AskNoTopicCount { get; set; }

        // Topic distributions for both paths
        public Dictionary<string, int> VolunteerTopicDistribution { get; } = new(StringComparer.Ordinal);
        public Dictionary<string, int> AskTopicDistribution { get; } = new(StringComparer.Ordinal);

        // Willingness distribution
        public int WillingnessPassedActiveLineCount { get; set; }
        public int WillingnessPassedAskThresholdCount { get; set; }
        public int WillingnessPassedSecretLineCount { get; set; }

        // 3b. Ask refusal lines counts & top 5 names
        public Dictionary<string, int> AskRefusalCounts { get; } = new(StringComparer.Ordinal);
        public Dictionary<string, List<string>> AskRefusalNames { get; } = new(StringComparer.Ordinal);

        // 4. Relation histogram counts & top 5 names
        public Dictionary<string, int> RelationHist { get; } = new(StringComparer.Ordinal);
        public Dictionary<string, List<string>> RelationHistNames { get; } = new(StringComparer.Ordinal);

        // 4b. 目前還沒休眠的事件，依份量（1..10）各有幾則
        public bool WeightTallyAvailable { get; set; }
        public int[] ActiveEventsByWeight { get; } = new int[10];

        // 5. Per-hero details for dev dialogue
        public List<ListenPreviewHeroDetail> HeroDetails { get; } = new();
    }
}
