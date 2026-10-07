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
        OpinionInvalid,

        /// <summary>模板 selfTell 欄位宣告無效（角色未在 roles 宣告、性格名非五種之一、min 非整數、秘密類模板不可宣告等）。</summary>
        SelfTellInvalid,

        /// <summary>模板 feelings／feelingOverrides 欄位宣告無效（角色未在 roles 宣告、類別不在清單裡等）。</summary>
        FeelingInvalid,

        /// <summary>模板 selfFeelingVariants 欄位宣告無效（角色未在 roles 宣告、傾向名空白或重複、特質名不對、門檻不是只有一邊的整數等）。</summary>
        SelfFeelingVariantInvalid,

        /// <summary>模板 madeUpBy 欄位宣告無效（角色未在 roles 宣告等）。</summary>
        MadeUpByInvalid,

        /// <summary>模板 response 欄位宣告無效（非 'denial'/'clarification' 或缺 linkedTemplateType）。</summary>
        ResponseInvalid
    }
}
