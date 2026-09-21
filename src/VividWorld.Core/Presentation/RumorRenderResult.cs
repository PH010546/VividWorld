#nullable enable
namespace VividWorld.Core.Presentation
{
    /// <summary>
    /// 傳聞渲染結果，同時給出帶連結的顯示版本與不帶標記的純文字診斷版本（規格 §9.3.1，M6c）。
    /// </summary>
    public sealed class RumorRenderResult
    {
        public string DisplayText { get; }
        public string PlainText { get; }

        public RumorRenderResult(string displayText, string plainText)
        {
            DisplayText = displayText ?? string.Empty;
            PlainText = plainText ?? string.Empty;
        }
    }
}
