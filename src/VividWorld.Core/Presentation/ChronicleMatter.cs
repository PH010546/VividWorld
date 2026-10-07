#nullable enable
using System;
using System.Collections.Generic;

namespace VividWorld.Core.Presentation
{
    /// <summary>
    /// 紀事區塊裡的一件事（源頭算第一件事，後續脫逃／獲釋／獲救各算一件事）。
    /// </summary>
    public sealed class ChronicleMatter
    {
        public string EventId { get; set; } = string.Empty;
        public string EventType { get; set; } = string.Empty;
        public double Day { get; set; }
        public bool HasHeading { get; set; }
        public string HeadlineTextId { get; set; } = string.Empty;
        public string HeadlineFallback { get; set; } = string.Empty;
        public string? NamedHeadlineTextId { get; set; }
        public Dictionary<string, string> Participants { get; set; } = new(StringComparer.Ordinal);
        public IReadOnlyList<ChronicleSource> Sources { get; set; } = Array.Empty<ChronicleSource>();

        // 以下由 Module 建立 VM 時填入
        public string? HeadingText { get; set; }
        public string DayLabel { get; set; } = string.Empty;
    }
}
