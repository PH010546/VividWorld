using System;
using System.Collections.Generic;
using System.Linq;
using VividWorld.Core.Events;

namespace VividWorld.Core.Catalog
{
    public sealed class EventCatalog
    {
        public IReadOnlyList<EventTemplate> Templates { get; }
        public IReadOnlyList<CatalogIssue> Issues { get; }
        public int SkippedCount { get; }

        /// <summary>份量還用舊寫法（沒標 dramaScale、dramaWeight 是 1..5 的段）的模板型別，載入時印一行用。</summary>
        public IReadOnlyList<string> LegacyDramaScaleTypes =>
            Templates.Where(t => t.DramaWeight.HasValue && t.DramaScale != DramaScales.Ten).Select(t => t.Type).ToList();

        private readonly Dictionary<string, EventTemplate> _byType;

        public EventCatalog(
            IReadOnlyList<EventTemplate> templates,
            IReadOnlyList<CatalogIssue> issues,
            int skippedCount)
        {
            Templates = templates ?? Array.Empty<EventTemplate>();
            Issues = issues ?? Array.Empty<CatalogIssue>();
            SkippedCount = skippedCount;

            _byType = new Dictionary<string, EventTemplate>(StringComparer.OrdinalIgnoreCase);
            foreach (var t in Templates)
            {
                if (!string.IsNullOrEmpty(t.Type) && !_byType.ContainsKey(t.Type))
                {
                    _byType[t.Type] = t;
                }
            }
        }

        public EventTemplate? ByType(string type)
        {
            if (string.IsNullOrEmpty(type)) return null;
            return _byType.TryGetValue(type, out var t) ? t : null;
        }
    }
}
