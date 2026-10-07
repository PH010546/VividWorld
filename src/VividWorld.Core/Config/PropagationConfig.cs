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

    /// <summary>領主之間傳話的三層：自家人與熟人照常傳、不熟的只傳大事、交惡的不傳。</summary>
    public sealed class TellTiersConfig
    {
        /// <summary>關掉時，領主之間傳話不分層，行為與沒有這一層時完全相同。</summary>
        public bool Enabled { get; set; } = true;

        /// <summary>兩個人自己的好感小於等於這個值算交惡，不傳。預設 -1。</summary>
        public int HostileAtOrBelow { get; set; } = -1;

        /// <summary>意願（好感加個性）達到這個值算熟人。預設 10。</summary>
        public double FamiliarWillingness { get; set; } = 10.0;

        [JsonExtensionData]
        public IDictionary<string, JToken> Extra { get; set; } = new Dictionary<string, JToken>();
    }

    /// <summary>醜事講不講：看說話的人與聽的人各自跟出醜的人的關係。</summary>
    public sealed class ShamefulNewsConfig
    {
        /// <summary>關掉時，不檢查醜事關係表，行為與沒有這一層時完全相同。</summary>
        public bool Enabled { get; set; } = true;

        [JsonExtensionData]
        public IDictionary<string, JToken> Extra { get; set; } = new Dictionary<string, JToken>();
    }

    public sealed class PropagationConfig
    {
        public TellTiersConfig TellTiers { get; set; } = new();
        public ShamefulNewsConfig ShamefulNews { get; set; } = new();
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
