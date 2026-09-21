using System;
using System.Collections.Generic;
using System.Linq;
using VividWorld.Core.Channels;

namespace VividWorld.Core.Tests.Fakes
{
    internal sealed class FakePropagationChannel : IPropagationChannel
    {
        public int QueryCount { get; private set; }

        private readonly Dictionary<string, List<ChannelLink>> _links = new();
        private readonly Dictionary<string, List<string>> _witnesses = new();

        public void AddLink(string from, string to, ChannelKind kind, double weight = 1.0, int relation = 0)
        {
            if (!_links.TryGetValue(from, out var list))
            {
                list = new List<ChannelLink>();
                _links[from] = list;
            }
            list.Add(new ChannelLink(to, kind, weight, relation));
        }

        public void AddWitness(string heroId, string witnessHeroId)
        {
            if (!_witnesses.TryGetValue(heroId, out var list))
            {
                list = new List<string>();
                _witnesses[heroId] = list;
            }
            list.Add(witnessHeroId);
        }

        public IReadOnlyList<ChannelLink> ContactsOf(string heroId, int maxResults)
        {
            QueryCount++;
            if (heroId != null && _links.TryGetValue(heroId, out var list))
            {
                return list.Take(maxResults).ToList();
            }
            return Array.Empty<ChannelLink>();
        }

        public int WitnessQueryCount { get; private set; }
        public string? LastWitnessQueryHeroId { get; private set; }

        public IReadOnlyList<string> WitnessesAt(string heroId, int maxResults)
        {
            WitnessQueryCount++;
            LastWitnessQueryHeroId = heroId;
            if (heroId != null && _witnesses.TryGetValue(heroId, out var list))
            {
                return list.Take(maxResults).ToList();
            }
            return Array.Empty<string>();
        }
    }
}
