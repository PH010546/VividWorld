#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using VividWorld.Core.Catalog;
using VividWorld.Core.Events;
using VividWorld.Core.Feelings;
using VividWorld.Core.Rumors;

namespace VividWorld.Core.Secrets
{
    public sealed class PublicSideSecretCandidate
    {
        public WorldEvent Event { get; set; } = null!;
        public bool IsSecret { get; set; }
        public bool NotRetired { get; set; }
        public bool NotLeaked { get; set; }
        public bool HasLinkedEvent { get; set; }
        public bool LinkedEventExists { get; set; }
        public bool PlayerNotHeardPublic { get; set; }
        public bool IsEligible => IsSecret && NotRetired && NotLeaked && HasLinkedEvent && LinkedEventExists && PlayerNotHeardPublic;
        public string DisqualifiedReason { get; set; } = string.Empty;
    }

    public sealed class PublicSideSecretResult
    {
        public bool IsEligible { get; set; }
        public WorldEvent? ChosenSecret { get; set; }
        public WorldEvent? LinkedPublicEvent { get; set; }
        public int TotalExamined { get; set; }
        public string SummaryReason { get; set; } = string.Empty;
        public List<PublicSideSecretCandidate> Candidates { get; set; } = new();
    }

    public sealed class FriendSecretCandidate
    {
        public WorldEvent Event { get; set; } = null!;
        public bool IsSecret { get; set; }
        public bool NotRetired { get; set; }
        public bool NotLeaked { get; set; }
        public bool HasLinkedEvent { get; set; }
        public bool HeDoesNotKnow { get; set; }
        public bool NotOwner { get; set; }
        public bool NotHarmed { get; set; }
        public bool ProtectsOwner { get; set; }
        public string? ProtectingOwnerId { get; set; }
        public bool IsEligible => IsSecret && NotRetired && NotLeaked && HasLinkedEvent && HeDoesNotKnow && NotOwner && NotHarmed && ProtectsOwner;
        public bool IsFallbackEligible => IsSecret && NotRetired && NotLeaked && HasLinkedEvent && HeDoesNotKnow && NotOwner && NotHarmed;
        public string DisqualifiedReason { get; set; } = string.Empty;
    }

    public sealed class FriendSecretResult
    {
        public bool IsEligible { get; set; }
        public bool RequiresRelationBoost { get; set; }
        public WorldEvent? ChosenSecret { get; set; }
        public string? SourceOwnerHeroId { get; set; }
        public string? TargetOwnerHeroId { get; set; }
        public int TotalExamined { get; set; }
        public string SummaryReason { get; set; } = string.Empty;
        public List<FriendSecretCandidate> Candidates { get; set; } = new();
    }

    public sealed class UnrelatedSecretCandidate
    {
        public WorldEvent Event { get; set; } = null!;
        public bool IsSecret { get; set; }
        public bool NotRetired { get; set; }
        public bool NotLeaked { get; set; }
        public bool HasLinkedEvent { get; set; }
        public bool HeDoesNotKnow { get; set; }
        public bool HasOwner { get; set; }
        public bool NotOwner { get; set; }
        public bool NotHarmed { get; set; }
        public bool NotProtectingOwner { get; set; }
        public bool IsEligible => IsSecret && NotRetired && NotLeaked && HasLinkedEvent && HeDoesNotKnow && HasOwner && NotOwner && NotHarmed && NotProtectingOwner;
        public string DisqualifiedReason { get; set; } = string.Empty;
    }

    public sealed class UnrelatedSecretResult
    {
        public bool IsEligible { get; set; }
        public WorldEvent? ChosenSecret { get; set; }
        public string? FirstOwnerHeroId { get; set; }
        public int TotalExamined { get; set; }
        public string SummaryReason { get; set; } = string.Empty;
        public List<UnrelatedSecretCandidate> Candidates { get; set; } = new();
    }

    public sealed class LeakedSecretHearerCandidate
    {
        public WorldEvent Event { get; set; } = null!;
        public bool IsSecret { get; set; }
        public bool IsLeaked { get; set; }
        public bool NotRetired { get; set; }
        public bool IsNewShape { get; set; }
        public bool IsOwner { get; set; }
        public bool HasReaction { get; set; }
        public bool IsEligible => IsSecret && IsLeaked && NotRetired && IsNewShape;
        public bool IsUnleakedEligible => IsSecret && !IsLeaked && NotRetired && IsNewShape && HasReaction;
        public string DisqualifiedReason { get; set; } = string.Empty;
    }

    public sealed class LeakedSecretHearerResult
    {
        public bool IsEligible { get; set; }
        public bool WasForcedLeak { get; set; }
        public string? LeakerHeroId { get; set; }
        public WorldEvent? ChosenSecret { get; set; }
        public bool HasReaction { get; set; }
        public bool IsOwner { get; set; }
        public int TotalExamined { get; set; }
        public string SummaryReason { get; set; } = string.Empty;
        public List<LeakedSecretHearerCandidate> Candidates { get; set; } = new();
    }

    public sealed class TestedOpponentInfo
    {
        public string OpponentHeroId { get; set; } = string.Empty;
        public StakeableSecretResult Evaluation { get; set; } = null!;
    }

    public sealed class StakeablePairResult
    {
        public bool IsEligible { get; set; }
        public string? ChosenOpponentHeroId { get; set; }
        public WorldEvent? ChosenSecret { get; set; }
        public List<TestedOpponentInfo> TestedOpponents { get; set; } = new();
        public string SummaryReason { get; set; } = string.Empty;
    }

    public sealed class FreePrisonerCandidate
    {
        public string PrisonerHeroId { get; set; } = string.Empty;
        public string SettlementId { get; set; } = string.Empty;
        public bool IsKin { get; set; }
        public int Relation { get; set; }
        public bool IsKinOrFriend => (IsKin && Relation > 30) || (!IsKin && Relation > 50);
        public bool IsEligible => IsKinOrFriend;
        public string DisqualifiedReason { get; set; } = string.Empty;
    }

    public sealed class FreePrisonerResult
    {
        public bool IsEligible { get; set; }
        public bool IsForcedNonFriend { get; set; }
        public FreePrisonerCandidate? ChosenPrisoner { get; set; }
        public int TotalExamined { get; set; }
        public string SummaryReason { get; set; } = string.Empty;
        public List<FreePrisonerCandidate> Candidates { get; set; } = new();
    }

    public static class SecretDevPicker
    {
        public static PublicSideSecretResult SelectPublicSideSecret(
            IEnumerable<WorldEvent>? speakerKnownSecrets,
            Func<string, bool>? isPlayerHeard,
            Func<string, WorldEvent?>? loadEvent)
        {
            var result = new PublicSideSecretResult();
            if (speakerKnownSecrets == null)
            {
                result.SummaryReason = "no events provided";
                return result;
            }

            var candidates = new List<PublicSideSecretCandidate>();
            int notSecretCount = 0;
            int retiredCount = 0;
            int leakedCount = 0;
            int noLinkedCount = 0;
            int linkedNotFoundCount = 0;
            int playerAlreadyHeardCount = 0;

            foreach (var evt in speakerKnownSecrets)
            {
                if (evt == null) continue;
                result.TotalExamined++;

                bool isSecret = evt.Origin == EventOrigin.Secret;
                bool notRetired = !evt.SecretRetired;
                bool notLeaked = evt.State == null || !evt.State.Leaked;
                bool hasLinked = !string.IsNullOrEmpty(evt.LinkedEventId);
                WorldEvent? linkedEvt = hasLinked && loadEvent != null ? loadEvent(evt.LinkedEventId!) : null;
                bool linkedExists = hasLinked && linkedEvt != null;
                bool playerNotHeard = hasLinked && (isPlayerHeard == null || !isPlayerHeard(evt.LinkedEventId!));

                var cand = new PublicSideSecretCandidate
                {
                    Event = evt,
                    IsSecret = isSecret,
                    NotRetired = notRetired,
                    NotLeaked = notLeaked,
                    HasLinkedEvent = hasLinked,
                    LinkedEventExists = linkedExists,
                    PlayerNotHeardPublic = playerNotHeard
                };

                var reasons = new List<string>();
                if (!isSecret) { reasons.Add("not a secret"); notSecretCount++; }
                if (!notRetired) { reasons.Add("secret retired"); retiredCount++; }
                if (!notLeaked) { reasons.Add("already leaked"); leakedCount++; }
                if (!hasLinked) { reasons.Add("missing linked public event"); noLinkedCount++; }
                else if (!linkedExists) { reasons.Add($"linked public event '{evt.LinkedEventId}' not found"); linkedNotFoundCount++; }
                else if (!playerNotHeard) { reasons.Add($"player already heard linked public event '{evt.LinkedEventId}'"); playerAlreadyHeardCount++; }

                cand.DisqualifiedReason = reasons.Count > 0 ? string.Join(", ", reasons) : string.Empty;
                candidates.Add(cand);
            }

            result.Candidates = candidates;
            var qualified = candidates.Where(c => c.IsEligible).ToList();
            if (qualified.Count == 0)
            {
                result.SummaryReason = result.TotalExamined == 0
                    ? "speaker knows no secrets"
                    : $"no eligible secrets out of {result.TotalExamined} examined (not secret: {notSecretCount}, retired: {retiredCount}, leaked: {leakedCount}, no linked: {noLinkedCount}, linked missing: {linkedNotFoundCount}, player already heard: {playerAlreadyHeardCount})";
                return result;
            }

            var chosenCandidate = qualified
                .OrderByDescending(c => c.Event.Day)
                .ThenBy(c => c.Event.EventId, StringComparer.Ordinal)
                .First();

            result.ChosenSecret = chosenCandidate.Event;
            result.LinkedPublicEvent = loadEvent?.Invoke(chosenCandidate.Event.LinkedEventId!);
            result.IsEligible = true;
            result.SummaryReason = "eligible";
            return result;
        }

        public static FriendSecretResult SelectFriendSecret(
            IEnumerable<WorldEvent>? worldSecrets,
            string heroId,
            ISecretRelationshipLookup? secretLookup,
            IFeelingWorld? feelingWorld = null,
            Func<string, EventTemplate?>? getTemplate = null)
        {
            var result = new FriendSecretResult();
            if (worldSecrets == null || string.IsNullOrEmpty(heroId))
            {
                result.SummaryReason = "no secrets or hero id empty";
                return result;
            }

            var candidates = new List<FriendSecretCandidate>();
            int notSecretCount = 0;
            int retiredCount = 0;
            int leakedCount = 0;
            int noLinkedCount = 0;
            int alreadyKnowsCount = 0;
            int isOwnerCount = 0;
            int wouldBeHarmedCount = 0;
            int doesNotProtectCount = 0;

            foreach (var evt in worldSecrets)
            {
                if (evt == null) continue;
                result.TotalExamined++;

                bool isSecret = evt.Origin == EventOrigin.Secret;
                bool notRetired = !evt.SecretRetired;
                bool notLeaked = evt.State == null || !evt.State.Leaked;
                bool hasLinked = !string.IsNullOrEmpty(evt.LinkedEventId);
                bool heDoesNotKnow = !evt.IsKnownBy(heroId);

                var owners = SecretProbeEvaluation.ResolveOwners(evt, getTemplate);
                bool notOwner = !owners.Contains(heroId, StringComparer.Ordinal);
                bool notHarmed = evt.SecretHarmedHeroIds == null || !evt.SecretHarmedHeroIds.Contains(heroId, StringComparer.Ordinal);

                bool protectsOwner = false;
                string? protectingOwnerId = null;
                int bestAffection = int.MinValue;

                if (notOwner)
                {
                    foreach (var ownerId in owners)
                    {
                        if (string.IsNullOrEmpty(ownerId)) continue;
                        bool isKin = secretLookup?.IsKin(heroId, ownerId) ?? false;
                        int aff = secretLookup?.GetAffection(heroId, ownerId) ?? feelingWorld?.Affection(heroId, ownerId) ?? 0;

                        if (isKin)
                        {
                            protectsOwner = true;
                            if (aff > bestAffection || protectingOwnerId == null)
                            {
                                bestAffection = aff;
                                protectingOwnerId = ownerId;
                            }
                        }
                        else if (aff > 50)
                        {
                            protectsOwner = true;
                            if (aff > bestAffection || protectingOwnerId == null)
                            {
                                bestAffection = aff;
                                protectingOwnerId = ownerId;
                            }
                        }
                    }
                }

                var cand = new FriendSecretCandidate
                {
                    Event = evt,
                    IsSecret = isSecret,
                    NotRetired = notRetired,
                    NotLeaked = notLeaked,
                    HasLinkedEvent = hasLinked,
                    HeDoesNotKnow = heDoesNotKnow,
                    NotOwner = notOwner,
                    NotHarmed = notHarmed,
                    ProtectsOwner = protectsOwner,
                    ProtectingOwnerId = protectingOwnerId
                };

                var reasons = new List<string>();
                if (!isSecret) { reasons.Add("not a secret"); notSecretCount++; }
                if (!notRetired) { reasons.Add("secret retired"); retiredCount++; }
                if (!notLeaked) { reasons.Add("already leaked"); leakedCount++; }
                if (!hasLinked) { reasons.Add("missing linked public event"); noLinkedCount++; }
                if (!heDoesNotKnow) { reasons.Add("hero already knows"); alreadyKnowsCount++; }
                if (!notOwner) { reasons.Add("hero is owner"); isOwnerCount++; }
                if (!notHarmed) { reasons.Add("hero would be harmed"); wouldBeHarmedCount++; }
                if (notOwner && !protectsOwner) { reasons.Add("does not protect any owner (neither kin nor relation > 50)"); doesNotProtectCount++; }

                cand.DisqualifiedReason = reasons.Count > 0 ? string.Join(", ", reasons) : string.Empty;
                candidates.Add(cand);
            }

            result.Candidates = candidates;
            var qualified = candidates.Where(c => c.IsEligible).ToList();
            if (qualified.Count > 0)
            {
                var chosenCandidate = qualified
                    .OrderByDescending(c => c.Event.Day)
                    .ThenBy(c => c.Event.EventId, StringComparer.Ordinal)
                    .First();

                result.ChosenSecret = chosenCandidate.Event;
                result.SourceOwnerHeroId = chosenCandidate.ProtectingOwnerId;
                result.TargetOwnerHeroId = chosenCandidate.ProtectingOwnerId;
                result.RequiresRelationBoost = false;
                result.IsEligible = true;
                result.SummaryReason = "eligible";
                return result;
            }

            var fallbackQualified = candidates.Where(c => c.IsFallbackEligible).ToList();
            if (fallbackQualified.Count > 0)
            {
                var chosenCandidate = fallbackQualified
                    .OrderByDescending(c => c.Event.Day)
                    .ThenBy(c => c.Event.EventId, StringComparer.Ordinal)
                    .First();

                var owners = SecretProbeEvaluation.ResolveOwners(chosenCandidate.Event, getTemplate);
                string? firstOwnerId = owners.FirstOrDefault();
                if (!string.IsNullOrEmpty(firstOwnerId))
                {
                    result.ChosenSecret = chosenCandidate.Event;
                    result.SourceOwnerHeroId = firstOwnerId;
                    result.TargetOwnerHeroId = firstOwnerId;
                    result.RequiresRelationBoost = true;
                    result.IsEligible = true;
                    result.SummaryReason = "eligible (fallback: requires relation boost to 51)";
                    return result;
                }
            }

            result.SummaryReason = result.TotalExamined == 0
                ? "no world secrets"
                : $"no eligible friend secrets out of {result.TotalExamined} examined (not secret: {notSecretCount}, retired: {retiredCount}, leaked: {leakedCount}, no linked: {noLinkedCount}, already knows: {alreadyKnowsCount}, owner: {isOwnerCount}, harmed: {wouldBeHarmedCount}, does not protect: {doesNotProtectCount})";
            return result;
        }

        public static UnrelatedSecretResult SelectUnrelatedSecret(
            IEnumerable<WorldEvent>? worldSecrets,
            string heroId,
            ISecretRelationshipLookup? secretLookup,
            IFeelingWorld? feelingWorld = null,
            Func<string, EventTemplate?>? getTemplate = null)
        {
            var result = new UnrelatedSecretResult();
            if (worldSecrets == null || string.IsNullOrEmpty(heroId))
            {
                result.SummaryReason = "no secrets or hero id empty";
                return result;
            }

            var candidates = new List<UnrelatedSecretCandidate>();
            int notSecretCount = 0;
            int retiredCount = 0;
            int leakedCount = 0;
            int noLinkedCount = 0;
            int alreadyKnowsCount = 0;
            int noOwnerCount = 0;
            int isOwnerCount = 0;
            int wouldBeHarmedCount = 0;
            int protectsOwnerCount = 0;

            foreach (var evt in worldSecrets)
            {
                if (evt == null) continue;
                result.TotalExamined++;

                bool isSecret = evt.Origin == EventOrigin.Secret;
                bool notRetired = !evt.SecretRetired;
                bool notLeaked = evt.State == null || !evt.State.Leaked;
                bool hasLinked = !string.IsNullOrEmpty(evt.LinkedEventId);
                bool heDoesNotKnow = !evt.IsKnownBy(heroId);

                var owners = SecretProbeEvaluation.ResolveOwners(evt, getTemplate);
                bool hasOwner = owners.Count > 0;
                bool notOwner = hasOwner && !owners.Contains(heroId, StringComparer.Ordinal);
                bool notHarmed = evt.SecretHarmedHeroIds == null || !evt.SecretHarmedHeroIds.Contains(heroId, StringComparer.Ordinal);

                bool protectsOwner = false;
                if (hasOwner && notOwner)
                {
                    foreach (var ownerId in owners)
                    {
                        if (string.IsNullOrEmpty(ownerId)) continue;
                        bool isKin = secretLookup?.IsKin(heroId, ownerId) ?? false;
                        int aff = secretLookup?.GetAffection(heroId, ownerId) ?? feelingWorld?.Affection(heroId, ownerId) ?? 0;

                        if (isKin || aff > 50)
                        {
                            protectsOwner = true;
                            break;
                        }
                    }
                }

                bool notProtectingOwner = !protectsOwner;

                var cand = new UnrelatedSecretCandidate
                {
                    Event = evt,
                    IsSecret = isSecret,
                    NotRetired = notRetired,
                    NotLeaked = notLeaked,
                    HasLinkedEvent = hasLinked,
                    HeDoesNotKnow = heDoesNotKnow,
                    HasOwner = hasOwner,
                    NotOwner = notOwner,
                    NotHarmed = notHarmed,
                    NotProtectingOwner = notProtectingOwner
                };

                var reasons = new List<string>();
                if (!isSecret) { reasons.Add("not a secret"); notSecretCount++; }
                if (!notRetired) { reasons.Add("secret retired"); retiredCount++; }
                if (!notLeaked) { reasons.Add("already leaked"); leakedCount++; }
                if (!hasLinked) { reasons.Add("missing linked public event"); noLinkedCount++; }
                if (!heDoesNotKnow) { reasons.Add("hero already knows"); alreadyKnowsCount++; }
                if (!hasOwner) { reasons.Add("missing secret owners"); noOwnerCount++; }
                else if (!notOwner) { reasons.Add("hero is owner"); isOwnerCount++; }
                if (!notHarmed) { reasons.Add("hero would be harmed"); wouldBeHarmedCount++; }
                if (hasOwner && notOwner && protectsOwner) { reasons.Add("protects owner (kin or relation > 50)"); protectsOwnerCount++; }

                cand.DisqualifiedReason = reasons.Count > 0 ? string.Join(", ", reasons) : string.Empty;
                candidates.Add(cand);
            }

            result.Candidates = candidates;
            var qualified = candidates.Where(c => c.IsEligible).ToList();
            if (qualified.Count > 0)
            {
                var chosenCandidate = qualified
                    .OrderByDescending(c => c.Event.Day)
                    .ThenBy(c => c.Event.EventId, StringComparer.Ordinal)
                    .First();

                var owners = SecretProbeEvaluation.ResolveOwners(chosenCandidate.Event, getTemplate);
                result.ChosenSecret = chosenCandidate.Event;
                result.FirstOwnerHeroId = owners.FirstOrDefault();
                result.IsEligible = true;
                result.SummaryReason = "eligible";
                return result;
            }

            result.SummaryReason = result.TotalExamined == 0
                ? "no world secrets"
                : $"no eligible unrelated secrets out of {result.TotalExamined} examined (not secret: {notSecretCount}, retired: {retiredCount}, leaked: {leakedCount}, no linked: {noLinkedCount}, already knows: {alreadyKnowsCount}, owner: {isOwnerCount}, harmed: {wouldBeHarmedCount}, protects owner: {protectsOwnerCount})";
            return result;
        }

        public static LeakedSecretHearerResult SelectLeakedSecretForHearer(
            IEnumerable<WorldEvent>? worldSecrets,
            string heroId,
            ISecretRelationshipLookup? relationshipLookup,
            IHeroTraitLookup? traitLookup,
            Func<string, EventTemplate?>? getTemplate = null)
        {
            var result = new LeakedSecretHearerResult();
            if (worldSecrets == null || string.IsNullOrEmpty(heroId))
            {
                result.SummaryReason = "no secrets or hero id empty";
                return result;
            }

            var candidates = new List<LeakedSecretHearerCandidate>();
            int notSecretCount = 0;
            int notLeakedCount = 0;
            int retiredCount = 0;
            int oldShapeCount = 0;

            foreach (var evt in worldSecrets)
            {
                if (evt == null) continue;
                result.TotalExamined++;

                bool isSecret = evt.Origin == EventOrigin.Secret;
                bool isLeaked = evt.State != null && evt.State.Leaked;
                bool notRetired = !evt.SecretRetired;
                bool isNewShape = evt.SecretOwnerHeroIds != null && evt.SecretOwnerHeroIds.Count > 0;

                bool isOwner = isNewShape && evt.SecretOwnerHeroIds!.Contains(heroId, StringComparer.Ordinal);
                bool hasReaction = false;

                if (isSecret && notRetired && isNewShape)
                {
                    if (isOwner)
                    {
                        var entry = evt.EntryFor(heroId);
                        hasReaction = entry == null || string.IsNullOrEmpty(entry.OwnerReaction);
                    }
                    else
                    {
                        var tmpl = getTemplate?.Invoke(evt.Type);
                        hasReaction = CheckWillHearerReact(evt, heroId, tmpl, relationshipLookup, traitLookup);
                    }
                }

                var cand = new LeakedSecretHearerCandidate
                {
                    Event = evt,
                    IsSecret = isSecret,
                    IsLeaked = isLeaked,
                    NotRetired = notRetired,
                    IsNewShape = isNewShape,
                    IsOwner = isOwner,
                    HasReaction = hasReaction
                };

                var reasons = new List<string>();
                if (!isSecret) { reasons.Add("not a secret"); notSecretCount++; }
                if (!isLeaked) { reasons.Add("not leaked"); notLeakedCount++; }
                if (!notRetired) { reasons.Add("secret retired"); retiredCount++; }
                if (!isNewShape) { reasons.Add("old shape (missing owners)"); oldShapeCount++; }

                cand.DisqualifiedReason = reasons.Count > 0 ? string.Join(", ", reasons) : string.Empty;
                candidates.Add(cand);
            }

            result.Candidates = candidates;

            // 1. 全世界有他聽到會有反應的已走漏秘密：優先挑，最新優先
            var leakedWithReaction = candidates.Where(c => c.IsEligible && c.HasReaction).ToList();
            if (leakedWithReaction.Count > 0)
            {
                var chosenCandidate = leakedWithReaction
                    .OrderByDescending(c => c.Event.Day)
                    .ThenBy(c => c.Event.EventId, StringComparer.Ordinal)
                    .First();

                result.ChosenSecret = chosenCandidate.Event;
                result.HasReaction = true;
                result.IsOwner = chosenCandidate.IsOwner;
                result.WasForcedLeak = false;
                result.IsEligible = true;
                result.SummaryReason = "eligible";
                return result;
            }

            // 2. 全世界沒有他聽到會有反應的已走漏秘密時，改找他聽到會有反應、還沒走漏的新形狀秘密
            var unleakedWithReaction = candidates.Where(c => c.IsUnleakedEligible).ToList();
            if (unleakedWithReaction.Count > 0)
            {
                var chosenCandidate = unleakedWithReaction
                    .OrderByDescending(c => c.Event.Day)
                    .ThenBy(c => c.Event.EventId, StringComparer.Ordinal)
                    .First();

                result.ChosenSecret = chosenCandidate.Event;
                result.HasReaction = true;
                result.IsOwner = chosenCandidate.IsOwner;
                result.WasForcedLeak = true;
                result.LeakerHeroId = DetermineLeakerHeroId(chosenCandidate.Event, heroId);
                result.IsEligible = true;
                result.SummaryReason = "eligible (fallback: unleaked secret chosen for forced leak)";
                return result;
            }

            // 3. 既無反應的已走漏秘密，也無反應的未走漏秘密：若有已走漏秘密（無反應），退回它
            var leakedWithoutReaction = candidates.Where(c => c.IsEligible).ToList();
            if (leakedWithoutReaction.Count > 0)
            {
                var chosenCandidate = leakedWithoutReaction
                    .OrderByDescending(c => c.Event.Day)
                    .ThenBy(c => c.Event.EventId, StringComparer.Ordinal)
                    .First();

                result.ChosenSecret = chosenCandidate.Event;
                result.HasReaction = false;
                result.IsOwner = chosenCandidate.IsOwner;
                result.WasForcedLeak = false;
                result.IsEligible = true;
                result.SummaryReason = "eligible (leaked secret without reaction)";
                return result;
            }

            result.SummaryReason = result.TotalExamined == 0
                ? "no world secrets"
                : $"no eligible leaked or unleaked secrets out of {result.TotalExamined} examined (not secret: {notSecretCount}, not leaked: {notLeakedCount}, retired: {retiredCount}, old shape: {oldShapeCount})";
            return result;
        }

        public static string DetermineLeakerHeroId(WorldEvent evt, string hearerHeroId)
        {
            var owners = evt.SecretOwnerHeroIds ?? new List<string>();

            // 1. 第一位不是當事人的知道的人
            if (evt.KnownBy != null)
            {
                var nonOwnerKnower = evt.KnownBy.FirstOrDefault(k => !string.IsNullOrEmpty(k.HeroId) && !owners.Contains(k.HeroId, StringComparer.Ordinal));
                if (nonOwnerKnower != null)
                {
                    return nonOwnerKnower.HeroId;
                }
            }

            // 2. 沒有就用另一位當事人
            var otherOwner = owners.FirstOrDefault(id => !string.IsNullOrEmpty(id) && !string.Equals(id, hearerHeroId, StringComparison.Ordinal));
            if (otherOwner != null)
            {
                return otherOwner;
            }

            // 3. 都沒有就用他自己
            return hearerHeroId;
        }

        private static bool CheckWillHearerReact(
            WorldEvent evt,
            string hearerHeroId,
            EventTemplate? template,
            ISecretRelationshipLookup? relationshipLookup,
            IHeroTraitLookup? traitLookup)
        {
            if (template?.OnHeard == null || template.OnHeard.Count == 0) return false;

            foreach (var rule in template.OnHeard)
            {
                if (rule == null) continue;

                bool listenerMatches = false;
                if (!string.IsNullOrEmpty(rule.ListenerRole))
                {
                    if (evt.Participants != null &&
                        evt.Participants.TryGetValue(rule.ListenerRole!, out var rHero) &&
                        string.Equals(rHero, hearerHeroId, StringComparison.Ordinal))
                    {
                        listenerMatches = true;
                    }
                }
                else if (!string.IsNullOrEmpty(rule.ListenerKinOf))
                {
                    if (evt.Participants != null &&
                        evt.Participants.TryGetValue(rule.ListenerKinOf!, out var rHero) &&
                        !string.IsNullOrEmpty(rHero) &&
                        relationshipLookup != null &&
                        relationshipLookup.IsKin(hearerHeroId, rHero!))
                    {
                        listenerMatches = true;
                    }
                }
                else if (!string.IsNullOrEmpty(rule.ListenerFriendsOf))
                {
                    if (evt.Participants != null &&
                        evt.Participants.TryGetValue(rule.ListenerFriendsOf!, out var rHero) &&
                        !string.IsNullOrEmpty(rHero) &&
                        relationshipLookup != null)
                    {
                        bool isKin = relationshipLookup.IsKin(hearerHeroId, rHero!);
                        int aff = relationshipLookup.GetAffection(hearerHeroId, rHero!) ?? 0;
                        if (!isKin && aff > 50)
                        {
                            listenerMatches = true;
                        }
                    }
                }
                else if (!string.IsNullOrEmpty(rule.ListenerCategory))
                {
                    string cat = rule.ListenerCategory!.Trim();
                    if (string.Equals(cat, "secretHarmed", StringComparison.OrdinalIgnoreCase))
                    {
                        listenerMatches = evt.SecretHarmedHeroIds != null && evt.SecretHarmedHeroIds.Contains(hearerHeroId);
                    }
                    else if (string.Equals(cat, "secretHarmed[0]", StringComparison.OrdinalIgnoreCase) ||
                             string.Equals(cat, "secretHarmedKing", StringComparison.OrdinalIgnoreCase))
                    {
                        listenerMatches = evt.SecretHarmedHeroIds != null && evt.SecretHarmedHeroIds.Count > 0 &&
                                          string.Equals(evt.SecretHarmedHeroIds[0], hearerHeroId, StringComparison.Ordinal);
                    }
                    else if (string.Equals(cat, "secretHarmed[1]", StringComparison.OrdinalIgnoreCase) ||
                             string.Equals(cat, "secretHarmedOwner", StringComparison.OrdinalIgnoreCase))
                    {
                        listenerMatches = evt.SecretHarmedHeroIds != null && evt.SecretHarmedHeroIds.Count > 1 &&
                                          string.Equals(evt.SecretHarmedHeroIds[1], hearerHeroId, StringComparison.Ordinal);
                    }
                    else if (string.Equals(cat, "all", StringComparison.OrdinalIgnoreCase))
                    {
                        listenerMatches = true;
                    }
                }

                if (listenerMatches)
                {
                    return true;
                }
            }

            return false;
        }

        public static StakeablePairResult FindFirstStakeablePair(
            string bettorHeroId,
            IEnumerable<string> candidateOpponentHeroIds,
            string? playerHeroId,
            IReadOnlyList<WorldEvent> bettorKnownSecrets)
        {
            var result = new StakeablePairResult();
            if (string.IsNullOrEmpty(bettorHeroId))
            {
                result.SummaryReason = "bettor is empty";
                return result;
            }

            foreach (var oppId in candidateOpponentHeroIds)
            {
                if (string.IsNullOrEmpty(oppId)) continue;
                var eval = StakeableSecretEvaluation.Evaluate(bettorHeroId, oppId, playerHeroId, bettorKnownSecrets);
                result.TestedOpponents.Add(new TestedOpponentInfo
                {
                    OpponentHeroId = oppId,
                    Evaluation = eval
                });

                if (eval.IsEligible && eval.ChosenSecret != null)
                {
                    result.ChosenOpponentHeroId = oppId;
                    result.ChosenSecret = eval.ChosenSecret;
                    result.IsEligible = true;
                    result.SummaryReason = "eligible";
                    return result;
                }
            }

            if (result.TestedOpponents.Count == 0)
            {
                result.SummaryReason = "no candidate challengers available in settlement";
            }
            else
            {
                var failureSummaries = result.TestedOpponents.Select(t =>
                {
                    string reasons = t.Evaluation.Candidates.Count > 0
                        ? string.Join("; ", t.Evaluation.Candidates.Where(c => !c.IsEligible).Select(c => $"[{c.Event.EventId}]: {c.DisqualifiedReason}"))
                        : t.Evaluation.Reason;
                    return $"{t.OpponentHeroId} ({t.Evaluation.Candidates.Count} secrets evaluated: {reasons})";
                });
                result.SummaryReason = $"none of {result.TestedOpponents.Count} candidate challenger(s) qualified: {string.Join(" | ", failureSummaries)}";
            }

            return result;
        }

        public static FreePrisonerResult SelectPrisonerToFree(
            string freerHeroId,
            IEnumerable<FreePrisonerCandidate>? candidates)
        {
            var result = new FreePrisonerResult();
            if (candidates == null)
            {
                result.SummaryReason = "no candidates";
                return result;
            }

            var list = new List<FreePrisonerCandidate>();
            foreach (var c in candidates)
            {
                if (c == null) continue;
                result.TotalExamined++;

                var reasons = new List<string>();
                if (c.IsKin && c.Relation <= 30)
                {
                    reasons.Add($"kin relation {c.Relation} <= 30");
                }
                else if (!c.IsKin && c.Relation <= 50)
                {
                    reasons.Add($"friend relation {c.Relation} <= 50");
                }

                c.DisqualifiedReason = reasons.Count > 0 ? string.Join(", ", reasons) : string.Empty;
                list.Add(c);
            }

            result.Candidates = list;
            if (list.Count == 0)
            {
                result.SummaryReason = "no imprisoned lords";
                return result;
            }

            var qualified = list.Where(c => c.IsKinOrFriend).ToList();
            if (qualified.Count > 0)
            {
                var chosen = qualified
                    .OrderByDescending(c => c.Relation)
                    .ThenBy(c => c.PrisonerHeroId, StringComparer.Ordinal)
                    .First();

                result.ChosenPrisoner = chosen;
                result.IsForcedNonFriend = false;
                result.IsEligible = true;
                result.SummaryReason = "eligible";
                return result;
            }

            // Fallback when neither kin nor friend exists:
            // 親友都沒有時不要停，挑好感最高的那位俘虜
            var fallbackChosen = list
                .OrderByDescending(c => c.Relation)
                .ThenBy(c => c.PrisonerHeroId, StringComparer.Ordinal)
                .First();

            result.ChosenPrisoner = fallbackChosen;
            result.IsForcedNonFriend = true;
            result.IsEligible = true;
            result.SummaryReason = "eligible (fallback: forced non-friend with highest relation)";
            return result;
        }
    }
}
