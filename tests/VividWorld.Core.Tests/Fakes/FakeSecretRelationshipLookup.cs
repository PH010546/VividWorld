#nullable enable
using System;
using System.Collections.Generic;
using VividWorld.Core.Secrets;

namespace VividWorld.Core.Tests.Fakes
{
    public sealed class FakeSecretRelationshipLookup : ISecretRelationshipLookup
    {
        public Dictionary<string, List<string>> Kin { get; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, List<string>> Friends { get; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<(string, string), int> Affection { get; } = new();

        public IReadOnlyCollection<string> GetKin(string heroId)
        {
            if (Kin.TryGetValue(heroId, out var list)) return list;
            return Array.Empty<string>();
        }

        public IReadOnlyCollection<string> GetFriends(string heroId)
        {
            if (Friends.TryGetValue(heroId, out var list)) return list;
            return Array.Empty<string>();
        }

        public int? GetAffection(string heroA, string heroB)
        {
            if (Affection.TryGetValue((heroA, heroB), out var val)) return val;
            if (Affection.TryGetValue((heroB, heroA), out var rev)) return rev;
            return null;
        }

        public bool IsKin(string heroA, string heroB)
        {
            if (Kin.TryGetValue(heroA, out var list) && list.Contains(heroB)) return true;
            if (Kin.TryGetValue(heroB, out var listB) && listB.Contains(heroA)) return true;
            return false;
        }

        public void SetKin(string a, string b)
        {
            if (!Kin.TryGetValue(a, out var la)) { la = new List<string>(); Kin[a] = la; }
            if (!la.Contains(b)) la.Add(b);
            if (!Kin.TryGetValue(b, out var lb)) { lb = new List<string>(); Kin[b] = lb; }
            if (!lb.Contains(a)) lb.Add(a);
        }

        public void SetAffection(string a, string b, int val)
        {
            Affection[(a, b)] = val;
            Affection[(b, a)] = val;
        }
    }
}
