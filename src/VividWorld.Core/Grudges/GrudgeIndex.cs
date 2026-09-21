using System;
using System.Collections.Generic;
using System.Linq;
using VividWorld.Core.Events;
using VividWorld.Core.Persistence;

namespace VividWorld.Core.Grudges
{
    public sealed class GrudgeIndex
    {
        private readonly List<GrudgeEntry> _entries = new();
        private readonly Dictionary<(string From, string About, GrudgeScope Scope), List<GrudgeEntry>> _byPair
            = new();

        public void Note(GrudgeEntry entry)
        {
            if (entry == null) return;
            _entries.Add(entry);
            var key = (entry.FromHeroId, entry.AboutHeroId, entry.Scope);
            if (!_byPair.TryGetValue(key, out var list))
            {
                list = new List<GrudgeEntry>();
                _byPair[key] = list;
            }
            list.Add(entry);
        }

        public IReadOnlyList<GrudgeEntry> Between(string from, string about, GrudgeScope scope)
        {
            if (from == null || about == null) return Array.Empty<GrudgeEntry>();
            var key = (from, about, scope);
            if (!_byPair.TryGetValue(key, out var list) || list.Count == 0)
            {
                return Array.Empty<GrudgeEntry>();
            }
            return list.OrderBy(e => e.Day).ThenBy(e => e.EventId, StringComparer.Ordinal).ToList();
        }

        public IReadOnlyList<(string From, string About, GrudgeScope Scope)> Pairs
        {
            get
            {
                return _byPair.Keys
                    .OrderBy(k => k.From, StringComparer.Ordinal)
                    .ThenBy(k => k.About, StringComparer.Ordinal)
                    .ThenBy(k => (int)k.Scope)
                    .ToList();
            }
        }

        public bool TryGetEscalation(string a, string b, out string eventId, out double day)
        {
            eventId = string.Empty;
            day = 0.0;
            if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b)) return false;

            string expectedEscalatedFrom = $"{a}|{b}";
            var match = _entries.FirstOrDefault(e =>
                e.Scope == GrudgeScope.Clan &&
                string.Equals(e.EscalatedFrom, expectedEscalatedFrom, StringComparison.Ordinal));

            if (match != null)
            {
                eventId = match.EventId;
                day = match.Day;
                return true;
            }
            return false;
        }

        public int PairCount(GrudgeScope scope)
        {
            return _byPair.Keys.Count(k => k.Scope == scope);
        }

        /// <summary>載入時重建恩怨帳本。
        /// **日期比 <paramref name="today"/> 晚的事件不重建**（規格 §2.2.1）——
        /// 那是一條被抹掉的時間線結下的恩怨，不該回到今天的帳本上。
        /// 事件本身留在索引裡，日子走到那一天，下一次重建就會把它算回來。</summary>
        public static GrudgeIndex RebuildFrom(
            RumorIndex index,
            Func<string, WorldEvent?> load,
            double today,
            out int eventsRead,
            out int skipped,
            out int hiddenFuture)
        {
            var grudgeIndex = new GrudgeIndex();
            eventsRead = 0;
            skipped = 0;
            hiddenFuture = 0;

            if (index?.Entries == null || load == null) return grudgeIndex;

            foreach (var entry in index.Entries)
            {
                if (entry == null || !entry.HasGrudges) continue;

                if (!EventVisibility.IsVisibleOn(entry, today))
                {
                    hiddenFuture++;
                    continue;
                }

                var evt = load(entry.EventId);
                if (evt == null)
                {
                    skipped++;
                    continue;
                }

                eventsRead++;

                if (evt.KnownBy == null) continue;

                foreach (var kn in evt.KnownBy)
                {
                    if (kn?.RelationImpacts == null || kn.RelationImpacts.Count == 0) continue;

                    foreach (var ri in kn.RelationImpacts)
                    {
                        if (ri == null) continue;

                        string fromHeroId = ri.Scope == GrudgeScope.Clan
                            ? ((ri.NativePair != null && ri.NativePair.Count > 0) ? ri.NativePair[0] : kn.HeroId)
                            : kn.HeroId;

                        var gEntry = new GrudgeEntry
                        {
                            EventId = evt.EventId,
                            FromHeroId = fromHeroId,
                            AboutHeroId = ri.AboutHeroId,
                            Day = ri.AppliedDay > 0 ? ri.AppliedDay : evt.Day,
                            Requested = ri.Requested,
                            Delta = ri.Delta,
                            Scope = ri.Scope,
                            LedgerOnly = ri.LedgerOnly,
                            EscalatedFrom = ri.EscalatedFrom
                        };

                        grudgeIndex.Note(gEntry);
                    }
                }
            }

            return grudgeIndex;
        }
    }
}
