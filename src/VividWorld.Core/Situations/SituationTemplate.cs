using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using VividWorld.Core.Grudges;

namespace VividWorld.Core.Situations
{
    public sealed class SituationRoleDef
    {
        public string? Derived { get; set; }
        public bool Optional { get; set; }

        public bool IsDerived => !string.IsNullOrEmpty(Derived);

        [JsonExtensionData]
        public IDictionary<string, JToken> Extra { get; set; } = new Dictionary<string, JToken>();
    }

    public sealed class SituationConditionDef
    {
        public string Type { get; set; } = string.Empty;
        public List<string>? Roles { get; set; }
        public List<string>? Kinds { get; set; }
        public string? A { get; set; }
        public string? B { get; set; }
        public string? Role { get; set; }
        public bool? Value { get; set; }
        public string? Op { get; set; }
        public double? Days { get; set; }

        [JsonExtensionData]
        public IDictionary<string, JToken> Extra { get; set; } = new Dictionary<string, JToken>();
    }

    public sealed class SituationBranchEventDef
    {
        public string Type { get; set; } = string.Empty;
        public Dictionary<string, string> Bind { get; set; } = new(StringComparer.Ordinal);

        [JsonExtensionData]
        public IDictionary<string, JToken> Extra { get; set; } = new Dictionary<string, JToken>();
    }

    public sealed class SituationBranchDef
    {
        public string Id { get; set; } = string.Empty;
        public double Base { get; set; }
        public Dictionary<string, double>? Traits { get; set; } = new(StringComparer.OrdinalIgnoreCase);
        public List<SituationConditionDef> Preconditions { get; set; } = new();
        public List<SituationBranchEventDef> Events { get; set; } = new();
        public List<SituationGrudgeDef>? Grudges { get; set; }

        [JsonExtensionData]
        public IDictionary<string, JToken> Extra { get; set; } = new Dictionary<string, JToken>();
    }

    public sealed class SituationTemplate
    {
        public string Id { get; set; } = string.Empty;
        public string Trigger { get; set; } = string.Empty;
        public string Decider { get; set; } = string.Empty;
        public double? MinBranchWeight { get; set; }
        public double Weight { get; set; } = 1.0;
        public bool DevOnly { get; set; }
        public Dictionary<string, SituationRoleDef> Roles { get; set; } = new(StringComparer.OrdinalIgnoreCase);
        public List<SituationConditionDef> Conditions { get; set; } = new();
        public List<SituationBranchDef> Branches { get; set; } = new();

        [JsonExtensionData]
        public IDictionary<string, JToken> Extra { get; set; } = new Dictionary<string, JToken>();
    }
}
