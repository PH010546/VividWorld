#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using VividWorld.Core.Catalog;
using VividWorld.Core.Config;
using VividWorld.Core.Events;
using VividWorld.Core.Feelings;

namespace VividWorld.Core.Secrets
{
    public enum SecretProbeDecision
    {
        Tell,
        Deflect,
        Untrusted
    }

    public enum SecretMemoryState
    {
        None,
        Own,
        OwnLeaked,
        Confided
    }

    public sealed class SecretProbeEvaluationResult
    {
        public SecretProbeDecision Decision { get; set; }
        public bool TrustsPlayer { get; set; }
        public int AffectionWithPlayer { get; set; }
        public bool IsKinWithPlayer { get; set; }
        public int PlayerTrustLine { get; set; }
        public bool IsOwner { get; set; }
        public bool ProtectsOwner { get; set; }
        public List<string> ProtectReasons { get; set; } = new();
        public IReadOnlyList<string> OwnerHeroIds { get; set; } = Array.Empty<string>();
        public Dictionary<string, int> OwnerRelations { get; set; } = new(StringComparer.Ordinal);
        public Dictionary<string, bool> OwnerKin { get; set; } = new(StringComparer.Ordinal);
        public string LogDetail { get; set; } = string.Empty;

        public SecretMemoryState DetermineAiMemoryState(WorldEvent secretEvt, string heroId)
        {
            if (secretEvt == null) return SecretMemoryState.None;
            if (IsOwner)
            {
                bool heardLeaked = secretEvt.KnownBy != null && secretEvt.KnownBy.Any(k =>
                    k != null
                    && string.Equals(k.HeroId, heroId, StringComparison.Ordinal)
                    && (k.Hop > 0 || !string.IsNullOrEmpty(k.OwnerReaction)));
                return heardLeaked ? SecretMemoryState.OwnLeaked : SecretMemoryState.Own;
            }
            return SecretMemoryState.Confided;
        }

        public string DetermineAiSentenceKey(SecretMemoryState state)
        {
            return state switch
            {
                SecretMemoryState.Own => "VividWorld_SecretMemory_Own",
                SecretMemoryState.OwnLeaked => "VividWorld_SecretMemory_OwnLeaked",
                SecretMemoryState.Confided => "VividWorld_SecretMemory_Confided",
                _ => string.Empty
            };
        }
    }

    public static class SecretProbeEvaluation
    {
        public static IReadOnlyList<string> ResolveOwners(WorldEvent secretEvt, Func<string, EventTemplate?>? getTemplate)
        {
            if (secretEvt.SecretOwnerHeroIds != null && secretEvt.SecretOwnerHeroIds.Count > 0)
            {
                return secretEvt.SecretOwnerHeroIds;
            }

            var owners = new List<string>();
            var tmpl = getTemplate?.Invoke(secretEvt.Type);
            if (tmpl?.OwnerRoles != null && secretEvt.Participants != null)
            {
                foreach (var r in tmpl.OwnerRoles)
                {
                    if (secretEvt.Participants.TryGetValue(r, out var hid) && !string.IsNullOrEmpty(hid))
                    {
                        owners.Add(hid);
                    }
                }
            }
            if (owners.Count == 0 && !string.IsNullOrEmpty(secretEvt.OriginatorHeroId))
            {
                owners.Add(secretEvt.OriginatorHeroId!);
            }
            return owners;
        }

        public static SecretProbeEvaluationResult Evaluate(
            string heroId,
            string? playerHeroId,
            WorldEvent secretEvt,
            SecretsConfig? config,
            ISecretRelationshipLookup? secretLookup,
            IFeelingWorld? feelingWorld,
            Func<string, EventTemplate?>? getTemplate)
        {
            var res = new SecretProbeEvaluationResult();
            if (string.IsNullOrEmpty(heroId) || secretEvt == null)
            {
                res.Decision = SecretProbeDecision.Untrusted;
                res.LogDetail = "invalid hero or event";
                return res;
            }

            int trustKin = config?.ConfideTrustKin ?? 30;
            int trustOther = config?.ConfideTrustOther ?? 50;

            bool isKinWithPlayer = !string.IsNullOrEmpty(playerHeroId) && (secretLookup?.IsKin(heroId, playerHeroId!) ?? false);
            int affWithPlayer = !string.IsNullOrEmpty(playerHeroId)
                ? (secretLookup?.GetAffection(heroId, playerHeroId!) ?? feelingWorld?.Affection(heroId, playerHeroId!) ?? 0)
                : 0;
            int playerTrustLine = isKinWithPlayer ? trustKin : trustOther;
            bool trustsPlayer = !string.IsNullOrEmpty(playerHeroId) && (affWithPlayer > playerTrustLine);

            res.TrustsPlayer = trustsPlayer;
            res.AffectionWithPlayer = affWithPlayer;
            res.IsKinWithPlayer = isKinWithPlayer;
            res.PlayerTrustLine = playerTrustLine;

            var owners = ResolveOwners(secretEvt, getTemplate);
            res.OwnerHeroIds = owners;

            bool isOwner = owners.Contains(heroId, StringComparer.Ordinal);
            res.IsOwner = isOwner;

            bool protectsOwner = false;
            var reasons = new List<string>();

            if (!isOwner)
            {
                foreach (var ownerId in owners)
                {
                    if (string.IsNullOrEmpty(ownerId)) continue;
                    bool isKin = secretLookup?.IsKin(heroId, ownerId) ?? false;
                    int aff = secretLookup?.GetAffection(heroId, ownerId) ?? feelingWorld?.Affection(heroId, ownerId) ?? 0;
                    res.OwnerKin[ownerId] = isKin;
                    res.OwnerRelations[ownerId] = aff;

                    if (isKin)
                    {
                        protectsOwner = true;
                        reasons.Add($"kin of owner {ownerId}");
                    }
                    else if (aff > 50)
                    {
                        protectsOwner = true;
                        reasons.Add($"friend of owner {ownerId} ({aff} > 50)");
                    }
                }
            }
            res.ProtectsOwner = protectsOwner;
            res.ProtectReasons = reasons;

            if (!trustsPlayer)
            {
                res.Decision = SecretProbeDecision.Untrusted;
                res.LogDetail = $"does not trust player (kin={isKinWithPlayer}, affection={affWithPlayer} <= line={playerTrustLine})";
            }
            else if (protectsOwner)
            {
                res.Decision = SecretProbeDecision.Deflect;
                res.LogDetail = $"trusts player (kin={isKinWithPlayer}, affection={affWithPlayer} > line={playerTrustLine}), but protects owner: {string.Join(", ", reasons)}";
            }
            else
            {
                res.Decision = SecretProbeDecision.Tell;
                res.LogDetail = $"trusts player (kin={isKinWithPlayer}, affection={affWithPlayer} > line={playerTrustLine}) and does not protect owner (isOwner={isOwner})";
            }

            return res;
        }
    }
}
