using System;
using System.Collections.Generic;

namespace VividWorld.Core.Presentation
{
    public sealed class ComposedRumor
    {
        public IReadOnlyList<ComposedFactPart> Parts = Array.Empty<ComposedFactPart>();
        public string? PrefixTextId;                // 重述前綴等，可為 null
        public string? PrefixFallback;
    }
}
