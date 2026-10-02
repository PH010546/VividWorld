#nullable enable
namespace VividWorld.Core.Events
{
    /// <summary>
    /// 「被領主釋放」的真正原因（六種）與判斷規則。
    /// 遊戲事件傳出來的 <c>party</c> 是俘虜放人當下所在的隊伍：
    /// 戰後放人時它是城鎮城堡＝關人的城換了主人，否則＝關人的隊伍打輸；
    /// 主動放人時它是玩家的隊伍（或隊長、主人是玩家）＝玩家放的，否則＝押人的隊伍沒了（帳本 D-100、D-101、D-102）。
    /// </summary>
    public static class ReleaseReasons
    {
        public const string Ransom = "ransom";
        public const string Peace = "peace";
        public const string Defeated = "defeated";
        public const string SettlementOwnerChanged = "settlement_owner_changed";
        public const string PlayerChoice = "player_choice";
        public const string Disbanded = "disbanded";

        /// <summary>
        /// 由遊戲的放人事件判出原因；不屬於「被領主釋放」的細節（逃脫、死亡）回傳 null。
        /// 贖金與賠償是同一件事（賠償是俘虜屬於玩家家族時遊戲換的名字）。
        /// </summary>
        public static string? Classify(EndCaptivityDetail detail, bool partyIsSettlement, bool actorIsPlayer)
        {
            switch (detail)
            {
                case EndCaptivityDetail.Ransom:
                case EndCaptivityDetail.ReleasedByCompensation:
                    return Ransom;
                case EndCaptivityDetail.ReleasedAfterPeace:
                    return Peace;
                case EndCaptivityDetail.ReleasedAfterBattle:
                    return partyIsSettlement ? SettlementOwnerChanged : Defeated;
                case EndCaptivityDetail.ReleasedByChoice:
                    return actorIsPlayer ? PlayerChoice : Disbanded;
                default:
                    return null;
            }
        }

        /// <summary>原因對應的碎片字尾（例：<c>Ransom</c>），未知的原因回傳 null。</summary>
        public static string? SegmentOf(string? reason)
        {
            switch (reason)
            {
                case Ransom: return "Ransom";
                case Peace: return "Peace";
                case Defeated: return "Defeated";
                case SettlementOwnerChanged: return "SettlementOwnerChanged";
                case PlayerChoice: return "PlayerChoice";
                case Disbanded: return "Disbanded";
                default: return null;
            }
        }
    }
}
