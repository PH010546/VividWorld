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
        public bool AllowDead { get; set; }
        public string? GrudgeLineRef { get; set; }
        public int? GrudgeLine { get; set; }
        public string? NativeGrudgeLineRef { get; set; }
        public int? NativeGrudgeLine { get; set; }
        public Dictionary<string, int>? AnyTraitAtMost { get; set; }

        public bool IsDerived => !string.IsNullOrEmpty(Derived);

        public int GetGrudgeLine(Config.VividWorldConfig? config, SituationWorldContext? context = null)
        {
            if (!string.IsNullOrEmpty(GrudgeLineRef))
            {
                return SituationConfigResolver.ResolveInt(GrudgeLineRef!, config);
            }
            if (GrudgeLine.HasValue) return GrudgeLine.Value;
            return config?.FalseRumors?.GrudgeLine ?? context?.GrudgeLine ?? -5;
        }

        public int GetNativeGrudgeLine(Config.VividWorldConfig? config, SituationWorldContext? context = null)
        {
            if (!string.IsNullOrEmpty(NativeGrudgeLineRef))
            {
                return SituationConfigResolver.ResolveInt(NativeGrudgeLineRef!, config);
            }
            if (NativeGrudgeLine.HasValue) return NativeGrudgeLine.Value;
            return config?.FalseRumors?.NativeGrudgeLine ?? context?.NativeGrudgeLine ?? -20;
        }

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
        public string? Op { get; set; }
        public double? Days { get; set; }
        public string? Trait { get; set; }
        public int? Line { get; set; }
        public List<string>? SeedRoles { get; set; }
        public string? About { get; set; }
        public List<string>? EventTypes { get; set; }

        [JsonIgnore]
        public bool? Value { get; set; }

        [JsonIgnore]
        public double? Number { get; set; }

        [JsonIgnore]
        public string? ValueRef { get; set; }

        [JsonProperty("value")]
        public JToken? RawValue
        {
            get
            {
                if (ValueRef != null) return ValueRef;
                if (Value.HasValue) return Value.Value;
                if (Number.HasValue) return Number.Value;
                return null;
            }
            set
            {
                if (value == null || value.Type == JTokenType.Null)
                {
                    Value = null;
                    Number = null;
                    ValueRef = null;
                }
                else if (value.Type == JTokenType.Boolean)
                {
                    Value = value.Value<bool>();
                    Number = null;
                    ValueRef = null;
                }
                else if (value.Type == JTokenType.Integer || value.Type == JTokenType.Float)
                {
                    Number = value.Value<double>();
                    Value = null;
                    ValueRef = null;
                }
                else if (value.Type == JTokenType.String)
                {
                    ValueRef = value.Value<string>();
                    Value = null;
                    Number = null;
                }
            }
        }

        [JsonExtensionData]
        public IDictionary<string, JToken> Extra { get; set; } = new Dictionary<string, JToken>();
    }

    public sealed class SituationBranchEventDef
    {
        public string Type { get; set; } = string.Empty;
        public string? LinkTo { get; set; }
        public Dictionary<string, string> Bind { get; set; } = new(StringComparer.Ordinal);

        [JsonExtensionData]
        public IDictionary<string, JToken> Extra { get; set; } = new Dictionary<string, JToken>();
    }

    public sealed class SituationGrudgeWeightDef
    {
        public string From { get; set; } = string.Empty;
        public string Toward { get; set; } = string.Empty;
        public double PerPoint { get; set; }
        public double Max { get; set; }

        [JsonExtensionData]
        public IDictionary<string, JToken> Extra { get; set; } = new Dictionary<string, JToken>();
    }

    public sealed class SituationMadeUpTalkDef
    {
        public string Kind { get; set; } = string.Empty; // "slander" or "praise"
        public string Teller { get; set; } = string.Empty;
        public string Listener { get; set; } = string.Empty;
        public string Target { get; set; } = string.Empty;
        public string? CounterpartRole { get; set; }

        public string About { get => Target; set => Target = value; }
        public string OriginatorRole { get => Teller; set => Teller = value; }
        public string TargetRole { get => Target; set => Target = value; }

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
        public SituationMadeUpTalkDef? MadeUpTalk { get; set; }
        public SituationGrudgeWeightDef? GrudgeWeight { get; set; }

        [JsonExtensionData]
        public IDictionary<string, JToken> Extra { get; set; } = new Dictionary<string, JToken>();
    }

    public sealed class SituationQuotaGroupDef
    {
        public string Id { get; set; } = string.Empty;
        public string? MaxPerDayRef { get; set; }
        public double? MaxPerDay { get; set; }
        public string? ShareRef { get; set; }
        public double Share { get; set; } = 1.0;

        public double? GetMaxPerDay(Config.VividWorldConfig? config)
        {
            if (!string.IsNullOrEmpty(MaxPerDayRef))
            {
                return SituationConfigResolver.ResolveDouble(MaxPerDayRef!, config);
            }
            return MaxPerDay;
        }

        public double GetShare(Config.VividWorldConfig? config)
        {
            if (!string.IsNullOrEmpty(ShareRef))
            {
                return SituationConfigResolver.ResolveDouble(ShareRef!, config);
            }
            return Share;
        }

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
        public string? EnabledBy { get; set; }
        public string? MaxPerDayRef { get; set; }
        public double? MaxPerDay { get; set; }
        public SituationQuotaGroupDef? QuotaGroup { get; set; }
        public List<string>? EventTypes { get; set; }
        public Dictionary<string, string>? BindFromEvent { get; set; }
        public Dictionary<string, SituationRoleDef> Roles { get; set; } = new(StringComparer.OrdinalIgnoreCase);
        public List<SituationConditionDef> Conditions { get; set; } = new();
        public List<SituationBranchDef> Branches { get; set; } = new();

        public bool IsEnabled(Config.VividWorldConfig? config)
        {
            if (string.IsNullOrEmpty(EnabledBy)) return true;
            return SituationConfigResolver.ResolveBool(EnabledBy!, config);
        }

        public double? GetMaxPerDay(Config.VividWorldConfig? config)
        {
            if (!string.IsNullOrEmpty(MaxPerDayRef))
            {
                return SituationConfigResolver.ResolveDouble(MaxPerDayRef!, config);
            }
            return MaxPerDay;
        }

        [JsonExtensionData]
        public IDictionary<string, JToken> Extra { get; set; } = new Dictionary<string, JToken>();
    }
}
