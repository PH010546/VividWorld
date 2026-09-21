namespace VividWorld.Core.Rumors
{
    public static class Eligibility
    {
        /// <summary>傳播網路成員資格的唯一定義（規格 §6.5）。
        /// 第一階段等同「活著、非俘虜、且是領主或流浪者」，名人排除。</summary>
        public static bool IsEligible(TraitProfile? t)
            => t != null && t.IsAlive && !t.IsPrisoner && (t.IsLord || t.IsWanderer);

        public static bool IsEligible(IHeroTraitLookup traits, string heroId)
            => traits != null && IsEligible(traits.Of(heroId));
    }
}
