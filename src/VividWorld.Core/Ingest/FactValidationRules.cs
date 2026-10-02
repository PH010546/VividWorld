using System;

namespace VividWorld.Core.Ingest
{
    public static class FactValidationRules
    {
        public static readonly string[] ValidVarPrefixes =
            { "hero:", "settlement:", "faction:", "key:", "num:", "text:" };

        public const int MinFragility = 1;
        public const int MaxFragility = 5;
        public const int MinDramaWeight = 1;
        public const int MaxDramaWeight = 5;

        public static bool IsValidFragility(int fragility) =>
            fragility >= MinFragility && fragility <= MaxFragility;

        public static bool IsValidDramaWeight(int dramaWeight) =>
            dramaWeight >= MinDramaWeight && dramaWeight <= MaxDramaWeight;

        /// <summary>依尺度檢查：舊尺度 1..5、新尺度 1..10，其餘尺度不合法。</summary>
        public static bool IsValidDramaWeight(int dramaWeight, int scale) =>
            VividWorld.Core.Events.DramaScales.IsValid(dramaWeight, scale);

        public static bool HasValidVarPrefix(string? val)
        {
            if (val == null) return false;
            for (int i = 0; i < ValidVarPrefixes.Length; i++)
            {
                if (val.StartsWith(ValidVarPrefixes[i], StringComparison.Ordinal))
                {
                    return true;
                }
            }
            return false;
        }
    }
}
