namespace VividWorld.Core.Channels
{
    public enum ChannelKind
    {
        // ── 見面：地理決定，你控制不了會遇到誰 ──
        SameParty,        // 同一支隊伍
        SameArmy,         // 同一支軍隊（不同隊伍，一起行軍）
        SameSettlement,   // 同一個聚落

        // ── 遠端：主動決定，你選擇要寫信給誰 ──
        SameClan,         // 同氏族
        KinAbroad,        // 血親，但不在自己氏族（嫁出去的女兒、別家的母親）
        Kingdom           // 同王國的同僚
    }
}
