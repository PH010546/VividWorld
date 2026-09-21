using System;

namespace VividWorld.Mcm
{
    /// <summary>
    /// Dropdown choice lists for the MCM menu. Kept apart from VividWorldMcmSettings
    /// so plain string values can be mapped without relying on MCM types.
    /// </summary>
    internal static class McmChoiceLists
    {
        public static readonly string[] LogLevels = { "Error", "Warn", "Info" };
        public static readonly string[] CommonerCompatModes = { "auto", "on", "off" };

        public static int IndexOf(string[] choices, string? value, int defaultIndex = 0)
        {
            if (value != null)
            {
                for (int i = 0; i < choices.Length; i++)
                {
                    if (string.Equals(choices[i], value, StringComparison.OrdinalIgnoreCase))
                        return i;
                }
            }
            return defaultIndex >= 0 && defaultIndex < choices.Length ? defaultIndex : 0;
        }

        public static string? AtIndex(string[] choices, int? index) =>
            index.HasValue && index.Value >= 0 && index.Value < choices.Length ? choices[index.Value] : null;
    }
}
