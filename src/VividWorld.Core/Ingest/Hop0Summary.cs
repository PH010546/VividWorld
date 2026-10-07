using System;
using System.Collections.Generic;
using System.Linq;

namespace VividWorld.Core.Ingest
{
    public enum Hop0WitnessKind
    {
        Secret,
        AnchorNotInSettlement,
        Settlement,
        NotDrawn
    }

    public sealed class Hop0WitnessInfo
    {
        public Hop0WitnessKind Kind { get; }
        public string? AnchorRole { get; }
        public string? AnchorHeroId { get; }
        public int WitnessCount { get; }
        public int PresentCount { get; }
        public string? SettlementId { get; }
        public string? WitnessSource { get; }
        public IReadOnlyList<KeyValuePair<string, int>> Rejections { get; }

        private Hop0WitnessInfo(
            Hop0WitnessKind kind,
            string? anchorRole = null,
            string? anchorHeroId = null,
            int witnessCount = 0,
            int presentCount = 0,
            string? settlementId = null,
            IEnumerable<KeyValuePair<string, int>>? rejections = null,
            string? witnessSource = null)
        {
            Kind = kind;
            AnchorRole = anchorRole;
            AnchorHeroId = anchorHeroId;
            WitnessCount = witnessCount;
            PresentCount = presentCount;
            SettlementId = settlementId;
            WitnessSource = witnessSource;
            Rejections = rejections != null ? new List<KeyValuePair<string, int>>(rejections) : Array.Empty<KeyValuePair<string, int>>();
        }

        public static Hop0WitnessInfo Secret()
            => new Hop0WitnessInfo(Hop0WitnessKind.Secret);

        public static Hop0WitnessInfo AnchorNotInSettlement(string anchorRole, string anchorHeroId)
            => new Hop0WitnessInfo(Hop0WitnessKind.AnchorNotInSettlement, anchorRole: anchorRole, anchorHeroId: anchorHeroId);

        // 模板本身就不找在場的人當旁觀者（witnessSource 是 none）；在場的人沒被加進來不是被上限擋掉。
        public static Hop0WitnessInfo NotDrawn(string witnessSource)
            => new Hop0WitnessInfo(Hop0WitnessKind.NotDrawn, witnessSource: witnessSource);

        public static Hop0WitnessInfo AtSettlement(
            int witnessCount,
            int presentCount,
            string settlementId,
            IEnumerable<KeyValuePair<string, int>>? rejections = null)
            => new Hop0WitnessInfo(
                Hop0WitnessKind.Settlement,
                witnessCount: witnessCount,
                presentCount: presentCount,
                settlementId: settlementId,
                rejections: rejections);

        public string Format()
        {
            switch (Kind)
            {
                case Hop0WitnessKind.Secret:
                    return "0 (secret)";
                case Hop0WitnessKind.AnchorNotInSettlement:
                    return $"0 (anchor {AnchorRole}={AnchorHeroId} is not in a settlement)";
                case Hop0WitnessKind.Settlement:
                    return $"{WitnessCount} of {PresentCount} present at {SettlementId} ({FormatRejections(Rejections)})";
                case Hop0WitnessKind.NotDrawn:
                    return $"0 (this kind of event takes no bystanders: witnessSource {WitnessSource})";
                default:
                    return "0";
            }
        }

        private static string FormatRejections(IReadOnlyList<KeyValuePair<string, int>> rejections)
        {
            var active = new List<KeyValuePair<string, int>>();
            if (rejections != null)
            {
                foreach (var kvp in rejections)
                {
                    if (kvp.Value > 0)
                    {
                        active.Add(kvp);
                    }
                }
            }

            if (active.Count == 0)
            {
                return "0 rejected";
            }

            int total = 0;
            foreach (var kvp in active)
            {
                total += kvp.Value;
            }

            if (active.Count == 1)
            {
                return $"{total} rejected: {active[0].Key}";
            }

            var parts = new List<string>(active.Count);
            foreach (var kvp in active)
            {
                parts.Add($"{kvp.Key} {kvp.Value}");
            }
            return $"{total} rejected: " + string.Join(", ", parts);
        }
    }

    public static class Hop0Summary
    {
        public static string Format(
            int totalKnowers,
            IEnumerable<KeyValuePair<string, string>> participants,
            IReadOnlyCollection<string>? knowingRoles,
            Func<string, string?>? cannotTellReasonLookup,
            Hop0WitnessInfo witnessInfo,
            IReadOnlyList<string>? hearsayHeroIds = null)
        {
            if (witnessInfo == null) throw new ArgumentNullException(nameof(witnessInfo));

            string knowerLabel = totalKnowers == 1 ? "knower" : "knowers";
            var seededParts = new List<string>();
            var notSeededParts = new List<string>();

            if (participants != null)
            {
                foreach (var kvp in participants)
                {
                    string role = kvp.Key;
                    string heroId = kvp.Value;
                    if (string.IsNullOrEmpty(heroId)) continue;

                    // 誰會被種入只有 Hop0Seeding 說了算；這裡照抄一份條件，log 就可能跟實際名單對不上。
                    if (Hop0Seeding.IsKnowingRole(knowingRoles, role))
                    {
                        string? cannotTell = cannotTellReasonLookup?.Invoke(heroId);
                        if (!string.IsNullOrEmpty(cannotTell))
                        {
                            seededParts.Add($"{role}={heroId} [cannot tell: {cannotTell}]");
                        }
                        else
                        {
                            seededParts.Add($"{role}={heroId}");
                        }
                    }
                    else
                    {
                        notSeededParts.Add($"{role}={heroId} (not in knowingRoles)");
                    }
                }
            }

            string participantsSection = "participants: " + (seededParts.Count > 0 ? string.Join(", ", seededParts) : "(none)");
            string notSeededSection = notSeededParts.Count > 0 ? $"; not seeded: {string.Join(", ", notSeededParts)}" : string.Empty;
            string witnessSection = "; witnesses: " + witnessInfo.Format();
            string hearsaySection = (hearsayHeroIds != null && hearsayHeroIds.Count > 0)
                ? $"; hearsay: {hearsayHeroIds.Count} ({string.Join(", ", hearsayHeroIds)})"
                : string.Empty;

            return $"hop0: {totalKnowers} {knowerLabel} - {participantsSection}{notSeededSection}{witnessSection}{hearsaySection}";
        }

        public static string Format(
            int totalKnowers,
            IEnumerable<KeyValuePair<string, string>> participants,
            IReadOnlyCollection<string>? knowingRoles,
            IReadOnlyDictionary<string, string?>? cannotTellReasons,
            Hop0WitnessInfo witnessInfo,
            IReadOnlyList<string>? hearsayHeroIds = null)
            => Format(
                totalKnowers,
                participants,
                knowingRoles,
                id => cannotTellReasons != null && cannotTellReasons.TryGetValue(id, out var reason) ? reason : null,
                witnessInfo,
                hearsayHeroIds);
    }
}
