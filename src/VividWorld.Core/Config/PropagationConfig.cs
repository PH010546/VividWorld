using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using VividWorld.Core.Channels;

namespace VividWorld.Core.Config
{
    public sealed class ChannelWeights
    {
        public double SameParty      { get; set; } = 1.25;
        public double SameArmy       { get; set; } = 1.00;
        public double SameSettlement { get; set; } = 0.80;
        public double SameClan       { get; set; } = 1.00;
        public double KinAbroad      { get; set; } = 0.80;
        public double Kingdom        { get; set; } = 0.50;

        [JsonExtensionData]
        public IDictionary<string, JToken> Extra { get; set; } = new Dictionary<string, JToken>();

        public double For(ChannelKind kind) => kind switch
        {
            ChannelKind.SameParty => SameParty,
            ChannelKind.SameArmy => SameArmy,
            ChannelKind.SameSettlement => SameSettlement,
            ChannelKind.SameClan => SameClan,
            ChannelKind.KinAbroad => KinAbroad,
            ChannelKind.Kingdom => Kingdom,
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null)
        };
    }

    public sealed class TellerTraitWeights
    {
        public double Generosity  { get; set; } =  0.10;
        public double Honor       { get; set; } = -0.05;
        public double Calculating { get; set; } = -0.12;

        [JsonExtensionData]
        public IDictionary<string, JToken> Extra { get; set; } = new Dictionary<string, JToken>();
    }

    public sealed class PropagationConfig
    {
        public double BaseTellChancePerContact { get; set; } = 0.18;
        public ChannelWeights ChannelWeights { get; set; } = new();
        public double[] DramaTellMultiplier { get; set; } = { 0.35, 0.6, 1.0, 1.5, 2.2 };
        public int[]    DramaMaxHop         { get; set; } = { 2,    3,   4,   5,   6   };
        public double[] TopicDramaWeight    { get; set; } = { 0.35, 0.6, 1.0, 1.5, 2.2 };
        public double   TopicFreshnessFloor { get; set; } = 0.1;
        public TellerTraitWeights TellerTraitWeights { get; set; } = new();
        public double TellerMultiplierMin { get; set; } = 0.1;
        public double TellerMultiplierMax { get; set; } = 3.0;
        public int MaxContactsPerQuery { get; set; } = 6;
        public int MaxInPersonContacts { get; set; } = 4;
        public int MaxRemoteContacts { get; set; } = 2;
        public int KingdomCorrespondentCacheSize { get; set; } = 8;
        public int MaxNewKnowersPerEventPerTick { get; set; } = 2;
        public int MaxInitialWitnesses { get; set; } = 8;
        public bool PlayerCanTell { get; set; } = false;              // §6.6.2
        public double SelfIncriminationMultiplier { get; set; } = 0.15;
        public double TellToSubjectMultiplier { get; set; } = 0.03;
        public bool AllowParticipantRetell { get; set; } = true;      // §9.1.1 (1)
        public int DefaultDrama { get; set; } = 3;
        public Dictionary<string, int> DefaultDramaByEventType { get; set; } = new()
        {
            ["covert_sabotage"] = 4,
            ["duel"] = 5,
            ["duel_arranged"] = 3,
            ["tavern_quarrel"] = 4,
            ["shared_meal"] = 1,
        };

        [JsonExtensionData]
        public IDictionary<string, JToken> Extra { get; set; } = new Dictionary<string, JToken>();
    }
}
