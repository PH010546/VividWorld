namespace VividWorld.Core.Rumors
{
    public interface IHeroTraitLookup
    {
        /// <summary>查不到時回傳 null。呼叫端一律視為不合格，但絕不從 KnownBy 移除。</summary>
        TraitProfile? Of(string heroId);
    }
}
