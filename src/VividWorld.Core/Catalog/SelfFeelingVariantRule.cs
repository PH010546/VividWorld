#nullable enable
using Newtonsoft.Json;

namespace VividWorld.Core.Catalog
{
    /// <summary>
    /// 當事人句尾的一個個性版本：當事人的某一項特質達到門檻時，句尾鍵後面加 <c>_傾向</c>。
    /// 門檻只寫一邊：<see cref="Min"/>（特質值大於等於）或 <see cref="Max"/>（特質值小於等於）。
    /// </summary>
    public sealed class SelfFeelingVariantRule
    {
        /// <summary>傾向名，接在句尾鍵後面（例：<c>Cruel</c>）。</summary>
        [JsonProperty("tendency")]
        public string Tendency = string.Empty;

        /// <summary>看哪一項特質：honor、mercy、valor、calculating、generosity。</summary>
        [JsonProperty("trait")]
        public string Trait = string.Empty;

        /// <summary>特質值大於等於這個數才算；不用時為 null。</summary>
        [JsonProperty("min")]
        public int? Min;

        /// <summary>特質值小於等於這個數才算；不用時為 null。</summary>
        [JsonProperty("max")]
        public int? Max;

        public string Describe()
        {
            return Min.HasValue ? $"{Trait} >= {Min.Value}" : $"{Trait} <= {Max}";
        }
    }
}
