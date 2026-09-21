namespace VividWorld.Core.Situations
{
    public sealed class SituationRoleFacts
    {
        public string HeroId = string.Empty;
        public string? ClanId;
        public string? KingdomId;          // Clan.Kingdom?.StringId
        public bool IsClanLeader;
        public int? ClanTier;              // Clan 為 null 時 null
        public string? SettlementId;       // CurrentSettlement?.StringId
        public string? SettlementKind;     // "town" / "castle" / "other" / null
    }
}
