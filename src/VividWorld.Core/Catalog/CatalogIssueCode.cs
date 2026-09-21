namespace VividWorld.Core.Catalog
{
    public enum CatalogIssueCode
    {
        BadJson,
        MissingType,
        DuplicateType,
        BadOrigin,
        DramaOutOfRange,
        NoRoles,
        NoFacts,
        TooManyFacts,
        EmptyFactId,
        DuplicateFactId,
        EmptyFactText,
        MissingTextId,
        FragilityOutOfRange,
        BadVarPrefix,
        KnowingRoleNotARole,
        UnknownPlaceholder,
        DanglingTemplateRef,
        UnboundPlaceholder,

        /// <summary>碎片的 refersToTemplateType 指向的模板，不是這次特化時解析出來的那一個。
        /// Bind() 只拿得到一個 linkedEventId，沿用它就是靜默指錯事件。</summary>
        RefersToLinkMismatch,

        /// <summary>M6.5: 模板 opinion 欄位宣告無效（角色未在 roles 宣告、量為 0/NaN/Infinity、重複 about 等）。</summary>
        OpinionInvalid
    }
}
