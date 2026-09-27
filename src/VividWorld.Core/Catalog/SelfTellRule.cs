#nullable enable
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace VividWorld.Core.Catalog
{
    public sealed class SelfTellRule
    {
        public bool IsNever { get; }
        public string? Trait { get; }
        public int Min { get; }

        [JsonExtensionData]
        public IDictionary<string, JToken> Extra { get; set; } = new Dictionary<string, JToken>();

        public SelfTellRule()
        {
            IsNever = true;
        }

        public SelfTellRule(string trait, int min)
        {
            IsNever = false;
            Trait = trait;
            Min = min;
        }

        public static SelfTellRule Never => new();
    }
}
