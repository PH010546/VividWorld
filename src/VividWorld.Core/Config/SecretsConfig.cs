using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace VividWorld.Core.Config
{
    public sealed class SecretsConfig
    {
        public double ConfideChancePerDay { get; set; } = 0.02;
        public int ConfideTrustKin { get; set; } = 30;
        public int ConfideTrustOther { get; set; } = 50;

        public SecretEnduredConfig Endured { get; set; } = new();
        public SecretPoisonedConfig Poisoned { get; set; } = new();
        public SecretOwnerConfig Owner { get; set; } = new();
        public SecretFreedPrisonerConfig FreedPrisoner { get; set; } = new();
        public SecretTournamentThrowConfig TournamentThrow { get; set; } = new();

        [JsonExtensionData]
        public IDictionary<string, JToken> Extra { get; set; } = new Dictionary<string, JToken>();
    }

    public sealed class SecretFreedPrisonerConfig
    {
        public double Chance { get; set; } = 0.5;
        public double Gratitude { get; set; } = 10.0;
        public double KingOnHeard { get; set; } = -8.0;
        public double OwnerOnHeard { get; set; } = -8.0;

        [JsonExtensionData]
        public IDictionary<string, JToken> Extra { get; set; } = new Dictionary<string, JToken>();
    }

    public sealed class SecretEnduredConfig
    {
        public double FavoredOnHeard { get; set; } = -3.0;

        [JsonExtensionData]
        public IDictionary<string, JToken> Extra { get; set; } = new Dictionary<string, JToken>();
    }

    public sealed class SecretPoisonedConfig
    {
        public double KinOnHeard { get; set; } = -20.0;
        public double FriendOnHeard { get; set; } = -8.0;

        [JsonExtensionData]
        public IDictionary<string, JToken> Extra { get; set; } = new Dictionary<string, JToken>();
    }

    public sealed class SecretOwnerConfig
    {
        public double GrudgeOnOriginator { get; set; } = -6.0;
        public double Suspicion { get; set; } = -3.0;
        public double OpenlyValorWeight { get; set; } = 0.5;

        [JsonExtensionData]
        public IDictionary<string, JToken> Extra { get; set; } = new Dictionary<string, JToken>();
    }

    public sealed class SecretTournamentThrowConfig
    {
        public double Chance { get; set; } = 1.0;
        public double ReluctantGrudge { get; set; } = -6.0;
        public double[] OnHeardChampion { get; set; } = new[] { -5.0, -3.0, -1.0 };
        public double[] OnHeardFriendThrower { get; set; } = new[] { -3.0, -2.0, 0.0 };

        [JsonExtensionData]
        public IDictionary<string, JToken> Extra { get; set; } = new Dictionary<string, JToken>();
    }
}
