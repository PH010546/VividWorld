using System;
using VividWorld.Core.Config;

namespace VividWorld.Core.Situations
{
    public static class SituationConfigResolver
    {
        public const string EnabledRef = "@falseRumors.enabled";
        public const string MisconductPerDayRef = "@falseRumors.misconductPerDay";
        public const string CaptureMisconductChanceRef = "@falseRumors.captureMisconductChance";
        public const string PoisonChanceRef = "@falseRumors.poisonChance";
        public const string PoisonGrudgeLineRef = "@falseRumors.poisonGrudgeLine";
        public const string PoisonNativeGrudgeLineRef = "@falseRumors.poisonNativeGrudgeLine";
        public const string MaxPerDayRef = "@falseRumors.maxPerDay";
        public const string SlanderMaxPerDayRef = "@falseRumors.slanderMaxPerDay";
        public const string RivalryPerDayRef = "@falseRumors.rivalryPerDay";
        public const string PraisePerDayRef = "@falseRumors.praisePerDay";

        public static bool IsValidMaxPerDayRef(string? reference) =>
            string.Equals(reference, MisconductPerDayRef, StringComparison.Ordinal) ||
            string.Equals(reference, MaxPerDayRef, StringComparison.Ordinal);

        public static bool IsValidShareRef(string? reference) =>
            string.Equals(reference, SlanderMaxPerDayRef, StringComparison.Ordinal) ||
            string.Equals(reference, RivalryPerDayRef, StringComparison.Ordinal) ||
            string.Equals(reference, PraisePerDayRef, StringComparison.Ordinal);

        public static bool IsValidChanceRef(string? reference) =>
            string.Equals(reference, CaptureMisconductChanceRef, StringComparison.Ordinal) ||
            string.Equals(reference, PoisonChanceRef, StringComparison.Ordinal);

        public static bool IsValidEnabledByRef(string? reference) =>
            string.Equals(reference, EnabledRef, StringComparison.Ordinal);

        public static bool IsValidGrudgeLineRef(string? reference) =>
            string.Equals(reference, PoisonGrudgeLineRef, StringComparison.Ordinal);

        public static bool IsValidNativeGrudgeLineRef(string? reference) =>
            string.Equals(reference, PoisonNativeGrudgeLineRef, StringComparison.Ordinal);

        public static bool ResolveBool(string reference, VividWorldConfig? config)
        {
            if (string.Equals(reference, EnabledRef, StringComparison.Ordinal))
            {
                return config?.FalseRumors?.Enabled ?? true;
            }
            return true;
        }

        public static double ResolveDouble(string reference, VividWorldConfig? config)
        {
            if (string.Equals(reference, MisconductPerDayRef, StringComparison.Ordinal))
            {
                return config?.FalseRumors?.MisconductPerDay ?? 0.15;
            }
            if (string.Equals(reference, MaxPerDayRef, StringComparison.Ordinal))
            {
                return config?.FalseRumors?.MaxPerDay ?? 0.8;
            }
            if (string.Equals(reference, SlanderMaxPerDayRef, StringComparison.Ordinal))
            {
                return config?.FalseRumors?.SlanderMaxPerDay ?? 0.5;
            }
            if (string.Equals(reference, RivalryPerDayRef, StringComparison.Ordinal))
            {
                return config?.FalseRumors?.RivalryPerDay ?? 0.15;
            }
            if (string.Equals(reference, PraisePerDayRef, StringComparison.Ordinal))
            {
                return config?.FalseRumors?.PraisePerDay ?? 0.15;
            }
            if (string.Equals(reference, CaptureMisconductChanceRef, StringComparison.Ordinal))
            {
                return config?.FalseRumors?.CaptureMisconductChance ?? 0.3;
            }
            if (string.Equals(reference, PoisonChanceRef, StringComparison.Ordinal))
            {
                return config?.FalseRumors?.PoisonChance ?? 0.2;
            }
            return 0.0;
        }

        public static int ResolveInt(string reference, VividWorldConfig? config)
        {
            if (string.Equals(reference, PoisonGrudgeLineRef, StringComparison.Ordinal))
            {
                return config?.FalseRumors?.PoisonGrudgeLine ?? -10;
            }
            if (string.Equals(reference, PoisonNativeGrudgeLineRef, StringComparison.Ordinal))
            {
                return config?.FalseRumors?.PoisonNativeGrudgeLine ?? -30;
            }
            return 0;
        }
    }
}
