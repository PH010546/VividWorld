using System;
using VividWorld.Core.Config;

namespace VividWorld.Core.Rumors
{
    /// <summary>領主之間傳話的層：交惡的不講、不熟的只講大事、熟人什麼都講。
    /// 自家人算熟人；他們另外由 <see cref="ContactObservation.IsFamily"/> 標記，統計時分開算。</summary>
    public enum TellTier
    {
        Hostile,
        Unfamiliar,
        Familiar
    }

    public static class TellTierRule
    {
        /// <summary>講的人對聽的人的意願 = 好感 + 仗義項 + 榮譽項 + 理性項。
        /// 跟對玩家的意願（<c>RumorOfferSelector.ComputeWillingness</c>）同一條算式，
        /// 只是好感換成講的人對聽的人。講的人沒有個性資料時，三項個性都當 0。</summary>
        public static double Willingness(int relation, TraitProfile? teller, AskTraitWeights w)
        {
            if (teller == null || w == null) return relation;
            return relation
                 + (w.Generosity * teller.Generosity)
                 + (w.Honor * teller.Honor)
                 + (w.Calculating * teller.Calculating);
        }

        /// <summary>照順序判：自家人 → 交惡（兩個人自己的好感小於等於線）→ 意願達線的熟人 → 不熟。
        /// 自家人不看任何數字，好感再低也照常傳。</summary>
        public static TellTier Classify(bool isFamily, int ownRelation, double willingness, TellTiersConfig cfg)
        {
            if (isFamily) return TellTier.Familiar;
            if (ownRelation <= cfg.HostileAtOrBelow) return TellTier.Hostile;
            if (willingness >= cfg.FamiliarWillingness) return TellTier.Familiar;
            return TellTier.Unfamiliar;
        }

        /// <summary>這一筆傳話在這一層會不會發生。不熟的只講份量達到大事線的消息。</summary>
        public static bool WouldStillTell(TellTier tier, int weightTen, int bigNewsLine)
        {
            switch (tier)
            {
                case TellTier.Hostile: return false;
                case TellTier.Unfamiliar: return weightTen >= bigNewsLine;
                case TellTier.Familiar: return true;
                default: throw new ArgumentOutOfRangeException(nameof(tier), tier, null);
            }
        }
    }
}
