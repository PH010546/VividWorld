using System;
using System.Collections.Generic;

namespace VividWorld.Core.Situations
{
    public sealed class SituationCatalog
    {
        public IReadOnlyList<SituationTemplate> Situations { get; }
        public IReadOnlyList<SituationIssue> Issues { get; }
        public int SkippedCount { get; }

        private readonly Dictionary<string, SituationTemplate> _byId;

        public SituationCatalog(
            IReadOnlyList<SituationTemplate> situations,
            IReadOnlyList<SituationIssue> issues,
            int skippedCount)
        {
            Situations = situations ?? Array.Empty<SituationTemplate>();
            Issues = issues ?? Array.Empty<SituationIssue>();
            SkippedCount = skippedCount;

            _byId = new Dictionary<string, SituationTemplate>(StringComparer.OrdinalIgnoreCase);
            foreach (var s in Situations)
            {
                if (!string.IsNullOrEmpty(s.Id) && !_byId.ContainsKey(s.Id))
                {
                    _byId[s.Id] = s;
                }
            }
        }

        public SituationTemplate? ById(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            return _byId.TryGetValue(id, out var s) ? s : null;
        }

        public SituationTemplate? Get(string id) => ById(id);
    }
}
