#nullable enable
using System;
using VividWorld.Core.Config;

namespace VividWorld.Core.Secrets
{
    public static class SecretConfigResolver
    {
        public const string EnduredFavoredOnHeardRef = "@secrets.endured.favoredOnHeard";
        public const string PoisonedKinOnHeardRef = "@secrets.poisoned.kinOnHeard";
        public const string PoisonedFriendOnHeardRef = "@secrets.poisoned.friendOnHeard";
        public const string OwnerGrudgeOnOriginatorRef = "@secrets.owner.grudgeOnOriginator";
        public const string OwnerSuspicionRef = "@secrets.owner.suspicion";
        public const string OwnerOpenlyValorWeightRef = "@secrets.owner.openlyValorWeight";
        public const string FreedPrisonerChanceRef = "@secrets.freedPrisoner.chance";
        public const string FreedPrisonerGratitudeRef = "@secrets.freedPrisoner.gratitude";
        public const string FreedPrisonerKingOnHeardRef = "@secrets.freedPrisoner.kingOnHeard";
        public const string FreedPrisonerOwnerOnHeardRef = "@secrets.freedPrisoner.ownerOnHeard";
        public const string TournamentThrowChanceRef = "@secrets.tournamentThrow.chance";
        public const string TournamentThrowReluctantGrudgeRef = "@secrets.tournamentThrow.reluctantGrudge";
        public const string TournamentThrowOnHeardChampionRef = "@secrets.tournamentThrow.onHeardChampion";
        public const string TournamentThrowOnHeardFriendThrowerRef = "@secrets.tournamentThrow.onHeardFriendThrower";

        public static double? ResolveDouble(string? reference, VividWorldConfig? config)
        {
            if (string.IsNullOrWhiteSpace(reference)) return null;
            string key = reference!.Trim();

            if (string.Equals(key, EnduredFavoredOnHeardRef, StringComparison.OrdinalIgnoreCase))
            {
                return config?.Secrets?.Endured?.FavoredOnHeard ?? -3.0;
            }
            if (string.Equals(key, PoisonedKinOnHeardRef, StringComparison.OrdinalIgnoreCase))
            {
                return config?.Secrets?.Poisoned?.KinOnHeard ?? -20.0;
            }
            if (string.Equals(key, PoisonedFriendOnHeardRef, StringComparison.OrdinalIgnoreCase))
            {
                return config?.Secrets?.Poisoned?.FriendOnHeard ?? -8.0;
            }
            if (string.Equals(key, OwnerGrudgeOnOriginatorRef, StringComparison.OrdinalIgnoreCase))
            {
                return config?.Secrets?.Owner?.GrudgeOnOriginator ?? -6.0;
            }
            if (string.Equals(key, OwnerSuspicionRef, StringComparison.OrdinalIgnoreCase))
            {
                return config?.Secrets?.Owner?.Suspicion ?? -3.0;
            }
            if (string.Equals(key, OwnerOpenlyValorWeightRef, StringComparison.OrdinalIgnoreCase))
            {
                return config?.Secrets?.Owner?.OpenlyValorWeight ?? 0.5;
            }
            if (string.Equals(key, FreedPrisonerChanceRef, StringComparison.OrdinalIgnoreCase))
            {
                return config?.Secrets?.FreedPrisoner?.Chance ?? 0.5;
            }
            if (string.Equals(key, FreedPrisonerGratitudeRef, StringComparison.OrdinalIgnoreCase))
            {
                return config?.Secrets?.FreedPrisoner?.Gratitude ?? 10.0;
            }
            if (string.Equals(key, FreedPrisonerKingOnHeardRef, StringComparison.OrdinalIgnoreCase))
            {
                return config?.Secrets?.FreedPrisoner?.KingOnHeard ?? -8.0;
            }
            if (string.Equals(key, FreedPrisonerOwnerOnHeardRef, StringComparison.OrdinalIgnoreCase))
            {
                return config?.Secrets?.FreedPrisoner?.OwnerOnHeard ?? -8.0;
            }
            if (string.Equals(key, TournamentThrowChanceRef, StringComparison.OrdinalIgnoreCase))
            {
                return config?.Secrets?.TournamentThrow?.Chance ?? 1.0;
            }
            if (string.Equals(key, TournamentThrowReluctantGrudgeRef, StringComparison.OrdinalIgnoreCase))
            {
                return config?.Secrets?.TournamentThrow?.ReluctantGrudge ?? -6.0;
            }

            return null;
        }

        public static double[]? ResolveDoubleArray(string? reference, VividWorldConfig? config)
        {
            if (string.IsNullOrWhiteSpace(reference)) return null;
            string key = reference!.Trim();

            if (string.Equals(key, TournamentThrowOnHeardChampionRef, StringComparison.OrdinalIgnoreCase))
            {
                return config?.Secrets?.TournamentThrow?.OnHeardChampion ?? new[] { -5.0, -3.0, -1.0 };
            }
            if (string.Equals(key, TournamentThrowOnHeardFriendThrowerRef, StringComparison.OrdinalIgnoreCase))
            {
                return config?.Secrets?.TournamentThrow?.OnHeardFriendThrower ?? new[] { -3.0, -2.0, 0.0 };
            }

            return null;
        }
    }
}
