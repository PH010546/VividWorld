using System.Collections.Generic;

namespace VividWorld.Core.Presentation
{
    public sealed class ComposedFactPart
    {
        public string TextId = string.Empty;        // 空 = 用 Fallback 字面
        public string Fallback = string.Empty;
        public IReadOnlyDictionary<string, string> Vars = new Dictionary<string, string>();
    }
}
