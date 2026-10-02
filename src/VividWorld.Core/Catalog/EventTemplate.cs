using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using VividWorld.Core.Events;

namespace VividWorld.Core.Catalog
{
    public sealed class EventTemplate
    {
        public string Type = string.Empty;
        public EventOrigin? Origin;                              // null 一律拒收（§10.1：Secret 是 0 值）
        public int? DramaWeight;                                 // null → 交給 §10.1 步驟 3 依設定解析；怎麼讀看 DramaScale

        /// <summary><see cref="DramaWeight"/> 的尺度（<see cref="DramaScales"/>）：5＝舊寫法的 1..5（當成段）、10＝1..10 的份量。
        /// 模板沒標明尺度就是舊寫法，所以玩家手上留著的舊模板檔照舊能讀。</summary>
        public int DramaScale = DramaScales.Legacy;

        /// <summary>模板的份量（1..10）；沒寫份量時為 null。舊寫法的 1..5 一律 × 2。</summary>
        [JsonIgnore]
        public int? DramaWeightTen => DramaWeight.HasValue ? DramaScales.ToWeight(DramaWeight.Value, DramaScale) : (int?)null;
        public string? LinkedTemplateType;                       // 引用「同一份目錄裡的另一個型別」，不是 eventId
        public Dictionary<string, string> Roles = new();         // 角色名 → 佔位符（例：challenger → "{MASTERMIND}"）
        public HashSet<string> KnowingRoles = new();
        public List<TemplateFact> Facts = new();

        [JsonProperty("headline")]
        public string? Headline;

        [JsonProperty("opinion")]
        public List<OpinionDef>? Opinions;

        [JsonProperty("selfTell")]
        public Dictionary<string, SelfTellRule>? SelfTell;

        [JsonProperty("retired")]
        public bool Retired { get; set; } = false;

        [JsonProperty("colocatedWitnessAsHearsay")]
        public bool ColocatedWitnessAsHearsay { get; set; } = false;

        /// <summary>角色名 → 感想類別（<see cref="VividWorld.Core.Feelings.FeelingCategories"/> 的編號）。
        /// 沒列的角色講給玩家聽時不附感想。</summary>
        [JsonProperty("feelings")]
        public Dictionary<string, string>? Feelings;

        /// <summary>依事件實際帶的碎片改判類別：事件有 <c>WhenFact</c> 那一塊碎片時，該角色改用 <c>Category</c>
        /// （例：放人的原因是戰敗，抓人的人歸「抓的人沒了」而不是「照常放人」）。</summary>
        [JsonProperty("feelingOverrides")]
        public List<FeelingOverride>? FeelingOverrides;

        /// <summary>角色名 → 當事人句尾的個性版本（依宣告順序，第一個符合的勝出）。
        /// 沒列的角色、或當事人的特質都不到門檻，用不帶傾向的句尾。</summary>
        [JsonProperty("selfFeelingVariants")]
        public Dictionary<string, List<SelfFeelingVariantRule>>? SelfFeelingVariants;

        [JsonExtensionData]
        public IDictionary<string, JToken> Extra { get; set; } = new Dictionary<string, JToken>();
    }

    public sealed class FeelingOverride
    {
        [JsonProperty("role")]
        public string Role = string.Empty;

        /// <summary>事件碎片的 <c>textId</c>；事件帶這一塊碎片時這條覆寫才生效。</summary>
        [JsonProperty("whenFact")]
        public string WhenFact = string.Empty;

        [JsonProperty("category")]
        public string Category = string.Empty;
    }

    public sealed class OpinionDef
    {
        public string About = string.Empty;   // 這個模板自己的角色名，例 "favored"
        public double Amount;                 // 正負皆可。這是「第一手聽到、而且是當事人」的滿額

        [JsonExtensionData]
        public IDictionary<string, JToken> Extra { get; set; } = new Dictionary<string, JToken>();
    }
}
