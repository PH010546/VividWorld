namespace VividWorld.Core.Grudges
{
    public sealed class EscalationFacts
    {
        public string AHeroId { get; set; } = string.Empty;
        public string BHeroId { get; set; } = string.Empty;
        public string? AClanId { get; set; }
        public string? BClanId { get; set; }
        public string? ALeaderId { get; set; }
        public string? BLeaderId { get; set; }
        public bool AIsClanLeader { get; set; }
        public bool AIsKinOfOwnLeader { get; set; }     // 父母／子女／兄弟姊妹／配偶
        public string? AKinRelation { get; set; }       // "parent"/"child"/"sibling"/"spouse"/null，只為了印理由
    }

    public sealed class EscalationDecision
    {
        public bool Escalate { get; set; }
        public string Reason { get; set; } = string.Empty;              // 逐字，見 §3.7
        public double Amount { get; set; }                              // 觸發那一筆的 Requested × clanEscalationFactor
        public string? LeaderA { get; set; }
        public string? LeaderB { get; set; }
    }
}
