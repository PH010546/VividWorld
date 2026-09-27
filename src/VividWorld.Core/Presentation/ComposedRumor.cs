using System;
using System.Collections.Generic;

namespace VividWorld.Core.Presentation
{
    public sealed class ComposedRumor
    {
        public IReadOnlyList<ComposedFactPart> Parts = Array.Empty<ComposedFactPart>();
        public string? PrefixTextId;                // 重述前綴等，可為 null
        public string? PrefixFallback;
        public IReadOnlyDictionary<string, string> PrefixVars = new Dictionary<string, string>();
        public string? SpeakerHeroId;               // 講述者英雄 id（第一人稱當事人判定）
        public string? SpeakerRole;                 // 講述者在事件中的角色名（小寫，例 claimant、student）
        public IReadOnlyDictionary<string, string> Roles = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase); // 角色小寫 -> 英雄 id
    }
}
