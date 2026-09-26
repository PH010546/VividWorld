#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;

namespace VividWorld.Core.Dialogue
{
    public enum RumorMode
    {
        Casual,
        Realistic
    }

    public sealed class RumorModeResult
    {
        public RumorMode Mode { get; }
        public string Reason { get; }
        public IReadOnlyList<string> DetectedModules { get; }

        public RumorModeResult(RumorMode mode, string reason, IEnumerable<string>? detectedModules = null)
        {
            Mode = mode;
            Reason = reason ?? string.Empty;
            DetectedModules = detectedModules != null
                ? detectedModules.ToList()
                : Array.Empty<string>();
        }

        public string ModeString => Mode == RumorMode.Casual ? "casual" : "realistic";
    }

    public static class RumorModeResolver
    {
        /// <summary>
        /// 純函式：決定傳聞模式（暢玩／寫實）。
        /// 規格 §12.2、卡片 LISTEN1d §14(2)。
        /// </summary>
        public static RumorModeResult Resolve(string? configuredMode, IEnumerable<string>? detectedModules)
        {
            string mode = (configuredMode ?? string.Empty).Trim().ToLowerInvariant();
            var detectedList = (detectedModules ?? Enumerable.Empty<string>())
                .Where(m => !string.IsNullOrWhiteSpace(m))
                .ToList();

            if (mode == "casual")
            {
                return new RumorModeResult(RumorMode.Casual, "forced by config", detectedList);
            }
            if (mode == "realistic")
            {
                return new RumorModeResult(RumorMode.Realistic, "forced by config", detectedList);
            }

            // "auto" 或未知值皆退回 auto
            if (detectedList.Count > 0)
            {
                return new RumorModeResult(RumorMode.Realistic, $"auto - detected {string.Join(", ", detectedList)}", detectedList);
            }

            return new RumorModeResult(RumorMode.Casual, "auto - none detected", detectedList);
        }

        /// <summary>
        /// 格式化模式行（規格 §12.7）：
        /// Rumor mode: &lt;casual|realistic&gt; [&lt;auto - detected X, Y | auto - none detected | forced by config&gt;] - chat gate N, full gate 30, gist +2 hops, ask clan tier M
        /// </summary>
        public static string FormatModeLine(RumorModeResult result, int chatGate, int fullGate, int gistExtraHops, int askClanTier)
        {
            if (result == null) throw new ArgumentNullException(nameof(result));
            return $"Rumor mode: {result.ModeString} [{result.Reason}] - chat gate {chatGate}, full gate {fullGate}, gist +{gistExtraHops} hops, ask clan tier {askClanTier}";
        }
    }
}
