using System;
using System.Globalization;

namespace VividWorld.Core.Events
{
    /// <summary>
    /// 份量有兩個數：份量（1..10，存在事件上）與段（1..5）。傳多遠、多容易被講、記多久、保留幾段
    /// 都只讀段，所以同一段的行為跟份量只有 1..5 的時代完全一樣。
    /// 帶份量的物件（事件、模板、提交單）另外存一個「尺度」欄位：<see cref="Legacy"/>（5）代表欄位裡是舊的 1..5 值（當成段），
    /// <see cref="Ten"/>（10）代表 1..10 的份量。沒有這個欄位的舊資料自然落在 <see cref="Legacy"/>。
    /// </summary>
    public static class DramaScales
    {
        /// <summary>欄位裡放的是舊的 1..5（當成段，份量＝段 × 2）。缺欄位的舊資料的預設值。</summary>
        public const int Legacy = 5;

        /// <summary>欄位裡放的是 1..10 的份量。</summary>
        public const int Ten = 10;

        public const int MinWeight = 1;
        public const int MaxWeight = 10;
        public const int MinBand = 1;
        public const int MaxBand = 5;

        public static bool IsKnownScale(int scale) => scale == Legacy || scale == Ten;

        /// <summary>某個尺度下，這個值合不合法（舊尺度 1..5、新尺度 1..10）。</summary>
        public static bool IsValid(int value, int scale)
        {
            if (scale == Ten) return value >= MinWeight && value <= MaxWeight;
            if (scale == Legacy) return value >= MinBand && value <= MaxBand;
            return false;
        }

        public static int ClampWeight(int weight) => Math.Max(MinWeight, Math.Min(MaxWeight, weight));

        public static int ClampBand(int band) => Math.Max(MinBand, Math.Min(MaxBand, band));

        /// <summary>份量（1..10）換成段（1..5）：1–2→1、3–4→2、5–6→3、7–8→4、9–10→5。</summary>
        public static int BandOfWeight(int weight) => (ClampWeight(weight) + 1) / 2;

        /// <summary>舊的段（1..5）換成份量：段 × 2。</summary>
        public static int WeightOfBand(int band) => ClampBand(band) * 2;

        /// <summary>依尺度把欄位值換成段（1..5）。不認得的尺度當成舊尺度。</summary>
        public static int ToBand(int value, int scale) =>
            scale == Ten ? BandOfWeight(value) : ClampBand(value);

        /// <summary>依尺度把欄位值換成份量（1..10）。不認得的尺度當成舊尺度。</summary>
        public static int ToWeight(int value, int scale) =>
            scale == Ten ? ClampWeight(value) : WeightOfBand(value);

        /// <summary>
        /// 匯入時決定新事件的份量（1..10）：呼叫的人有給值就依他給的尺度換算（舊尺度 1..5 當成段 × 2）；
        /// 沒給就用設定：先看這種事件的預設、再看全域預設，這兩個設定值是舊尺度（1..5，當成段，× 2）。
        /// </summary>
        /// <param name="source">份量從哪裡來，供日誌：submitted／config type default／config default。</param>
        public static int ResolveWeight(int? submittedValue, int submittedScale, int? typeDefaultBand, int defaultBand, out string source)
        {
            if (submittedValue.HasValue)
            {
                source = submittedScale == Ten ? "submitted (1..10)" : "submitted (old 1..5, x2)";
                return ToWeight(submittedValue.Value, submittedScale);
            }
            if (typeDefaultBand.HasValue)
            {
                source = "config type default (band, x2)";
                return WeightOfBand(typeDefaultBand.Value);
            }
            source = "config default (band, x2)";
            return WeightOfBand(defaultBand);
        }

        /// <summary>日誌用：兩個數都印，例 <c>weight 6/10 (band 3)</c>。</summary>
        public static string Describe(int value, int scale)
        {
            return string.Format(
                CultureInfo.InvariantCulture,
                "weight {0}/10 (band {1})",
                ToWeight(value, scale),
                ToBand(value, scale));
        }
    }
}
