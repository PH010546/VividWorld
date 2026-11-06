using System;
using System.Collections.Generic;
using System.Linq;
using VividWorld.Core.Catalog;
using VividWorld.Core.Rumors;
using VividWorld.Core.Util;

namespace VividWorld.Core.Situations
{
    /// <summary>名單上的一個人，以及是哪件事把他放進來的。</summary>
    public sealed class SituationRosterEntry
    {
        public string HeroId = string.Empty;

        /// <summary>把他放進名單的那件事（情境 id 與事件 id，或呼叫端給的說明字串）。</summary>
        public string Source = string.Empty;
    }

    /// <summary>
    /// 今天已經捲進事情的人。挑法只讀它（含名單上任何一人的候選會被排掉）；
    /// 事情重新求值成立、真的要執行之後，由呼叫端用 <see cref="AddExecuted"/> 把實際綁到的人放進來。
    /// 名單只活在呼叫端手上，不存檔。
    /// </summary>
    public sealed class SituationRoster
    {
        private readonly List<SituationRosterEntry> _entries = new List<SituationRosterEntry>();
        private readonly HashSet<string> _heroes = new HashSet<string>(StringComparer.Ordinal);

        public IReadOnlyList<SituationRosterEntry> Entries => _entries;
        public int Count => _entries.Count;

        public bool Contains(string? heroId) => !string.IsNullOrEmpty(heroId) && _heroes.Contains(heroId!);

        /// <summary>把一件實際執行的事的所有人（含推導角色）放進名單。已在名單上的人不重複放，也不改來源。</summary>
        public void AddExecuted(IEnumerable<string?> heroIds, string source)
        {
            if (heroIds == null) return;
            foreach (var h in heroIds)
            {
                if (string.IsNullOrEmpty(h)) continue;
                if (!_heroes.Add(h!)) continue;
                _entries.Add(new SituationRosterEntry { HeroId = h!, Source = source ?? string.Empty });
            }
        }
    }

    /// <summary>一組可能發生的事：落在哪座聚落、每個角色綁到誰、挑人比重要用的個性。</summary>
    public sealed class SituationPickCandidate
    {
        public string? SettlementId;

        /// <summary>每個角色（含推導角色）綁到的英雄；沒綁到的值是 null 或空字串。</summary>
        public IReadOnlyDictionary<string, string?> RoleHeroIds = new Dictionary<string, string?>();

        /// <summary>有 pickTraits 的角色 → 它綁到的人的個性；查不到個性是 null。沒有 pickTraits 的角色不用給。</summary>
        public IReadOnlyDictionary<string, TraitProfile?>? RoleTraits;

        /// <summary>呼叫端自己的資料，挑中之後原樣交回去。</summary>
        public object? Payload;

        public IEnumerable<string> HeroIds()
        {
            foreach (var kv in RoleHeroIds)
            {
                if (!string.IsNullOrEmpty(kv.Value)) yield return kv.Value!;
            }
        }
    }

    /// <summary>一種事：比重、比重從哪來、候選（現成的，或挑中才列舉）。</summary>
    public sealed class SituationPickKind
    {
        public string Id = string.Empty;
        public double Weight;

        /// <summary>比重的來源標籤，日誌用（模板 weight、share…）。</summary>
        public string WeightSource = "weight";

        /// <summary>現成的候選；用了 <see cref="CandidateProvider"/> 就不給。</summary>
        public IReadOnlyList<SituationPickCandidate>? Candidates;

        /// <summary>晚一點才列舉候選：還沒列舉時先當作有候選參加挑種類，挑中才呼叫，列舉出來是 0 組（或全被名單排掉）就換一種重挑。</summary>
        public Func<IReadOnlyList<SituationPickCandidate>>? CandidateProvider;

        /// <summary>有 pickTraits 的角色 → (個性名 → 係數)。只放會看個性的角色（不含推導角色、只綁玩家的角色）。</summary>
        public IReadOnlyDictionary<string, IReadOnlyDictionary<string, double>>? PickTraitRoles;

        /// <summary>候選是空的時，呼叫端要印的原因（例如沒有任何一組過條件）。</summary>
        public string? NoCandidateReason;

        /// <summary>呼叫端自己的資料，挑中之後原樣交回去。</summary>
        public object? Payload;

        /// <summary>取出模板裡真的會看個性的角色（有 pickTraits、不是推導角色、不是只綁玩家的角色）。</summary>
        public static IReadOnlyDictionary<string, IReadOnlyDictionary<string, double>>? TraitRolesOf(SituationTemplate? template)
        {
            if (template?.Roles == null) return null;
            Dictionary<string, IReadOnlyDictionary<string, double>>? result = null;
            foreach (var kv in template.Roles)
            {
                var def = kv.Value;
                if (def == null || def.IsDerived || def.PlayerOnly) continue;
                if (def.PickTraits == null || def.PickTraits.Count == 0) continue;
                result ??= new Dictionary<string, IReadOnlyDictionary<string, double>>(StringComparer.OrdinalIgnoreCase);
                result[kv.Key] = def.PickTraits;
            }
            return result;
        }
    }

    public sealed class SituationPickRequest
    {
        public IReadOnlyList<SituationPickKind> Kinds = Array.Empty<SituationPickKind>();

        /// <summary>要挑幾件。</summary>
        public int Count = 1;

        /// <summary>
        /// 這一批第一件在整天的名額裡排第幾（0 起算）。呼叫端一件一件叫（中間要重新求值、執行，
        /// 只把真的執行的人放進名單）時用它，種子與日誌的「第幾件」才接得上。
        /// </summary>
        public int FirstSlot;

        /// <summary>日誌「共幾件」用；null＝就是 <see cref="Count"/>。</summary>
        public int? TotalSlots;

        /// <summary>今天已經捲進事情的人；null＝不看名單（玩家在場、流浪者事件）。</summary>
        public SituationRoster? Roster;

        public IDeterministicRng Rng = null!;

        /// <summary>玩家的英雄 id。綁到玩家的角色，挑人比重那一項算 ×1。</summary>
        public string? PlayerHeroId;

        /// <summary>挑種類用的種子，參數是第幾件（0 起算，含 <see cref="FirstSlot"/>）。</summary>
        public Func<int, long> KindSeed = null!;

        /// <summary>挑人用的種子，參數是第幾件與挑中的種類。</summary>
        public Func<int, SituationPickKind, long> PersonSeed = null!;

        /// <summary>日誌開頭用：在哪一處挑的，以及哪一天。</summary>
        public string Site = string.Empty;
        public double Day;
    }

    public enum PickKindStatus
    {
        /// <summary>有候選（排掉名單後還有），比重大於 0：參加。</summary>
        Eligible,

        /// <summary>候選還沒列舉：先當作有候選參加。</summary>
        NotEnumerated,

        WeightZero,

        /// <summary>列舉出來或本來就是 0 組候選。</summary>
        NoCandidates,

        /// <summary>有候選，但全部含名單上的人。</summary>
        AllExcludedByRoster
    }

    public sealed class PickKindEntry
    {
        public string Id = string.Empty;
        public double Weight;
        public string WeightSource = string.Empty;
        public PickKindStatus Status;

        /// <summary>合格的候選數；還沒列舉是 -1。</summary>
        public int Qualified = -1;
        public int ExcludedByRoster;
        public int Remaining;
        public string? Reason;

        /// <summary>參加挑種類才有。</summary>
        public double Probability;
        public double IntervalStart;
        public double IntervalEnd;

        public bool Participates => Status == PickKindStatus.Eligible || Status == PickKindStatus.NotEnumerated;
    }

    /// <summary>挑種類的一輪。候選晚給的種類挑中卻列舉不出來時，會再挑一輪。</summary>
    public sealed class PickKindRound
    {
        public List<PickKindEntry> Entries = new List<PickKindEntry>();
        public double TotalWeight;
        public double R;
        public double RTimesTotal;
        public string? PickedKindId;

        /// <summary>挑中的種類是晚給候選的：列舉出來的結果。</summary>
        public bool EnumeratedPickedKind;
        public int EnumeratedQualified;
        public int EnumeratedRemaining;
    }

    /// <summary>挑人比重裡，一個有 pickTraits 的角色那一項。</summary>
    public sealed class PickTraitTerm
    {
        public string Role = string.Empty;
        public string? HeroId;

        /// <summary>false＝這一項算 ×1，原因在 <see cref="SkipReason"/>。</summary>
        public bool Applies;
        public string? SkipReason;

        /// <summary>(個性名, 係數, 個性等級)。</summary>
        public List<(string Trait, double Coefficient, int Level)> Parts = new List<(string, double, int)>();

        /// <summary>1 + Σ 係數 × 等級 的原值。</summary>
        public double Raw = 1.0;
        public bool Clamped;
        public double Factor = 1.0;
    }

    public sealed class PickPersonEntry
    {
        public int Index;
        public SituationPickCandidate Candidate = null!;
        public List<PickTraitTerm> Terms = new List<PickTraitTerm>();
        public double Weight = 1.0;
        public double IntervalStart;
        public double IntervalEnd;
    }

    public sealed class PickPersonStage
    {
        public int Remaining;
        public double TotalWeight;

        /// <summary>這種事有沒有任何角色看個性。</summary>
        public bool UsesTraits;

        public List<PickPersonEntry> Entries = new List<PickPersonEntry>();

        /// <summary>每個不同的比重各有幾組，比重由大到小。</summary>
        public List<(double Weight, int Count)> Distribution = new List<(double, int)>();

        public double R;
        public double RTimesTotal;
        public int PickedIndex = -1;
    }

    /// <summary>一件的完整紀錄。<see cref="PickedKindId"/> 是 null＝這一件沒有任何種類可挑，整批在這裡停。</summary>
    public sealed class SituationPickRecord
    {
        public string Site = string.Empty;
        public double Day;
        public int Slot;
        public int SlotCount;

        public List<PickKindRound> Rounds = new List<PickKindRound>();
        public string? StopReason;

        public string? PickedKindId;
        public SituationPickKind? PickedKind;
        public SituationPickCandidate? PickedCandidate;
        public PickPersonStage? Person;

        /// <summary>這一處有沒有看「今天已經捲進事情的人」（玩家在場、流浪者事件不看）。</summary>
        public bool UsesRoster;

        /// <summary>這一件挑之前名單上的人。</summary>
        public List<SituationRosterEntry> RosterBefore = new List<SituationRosterEntry>();

        /// <summary>這一件排掉的候選數（挑中的那一種裡含名單上的人的）。</summary>
        public int ExcludedByRoster;

        /// <summary>挑中那組的所有人（含推導角色）。</summary>
        public List<string> HeroesOfPicked = new List<string>();
    }

    public sealed class SituationPickKindSummary
    {
        public string Id = string.Empty;
        public double Weight;

        /// <summary>合格的候選數（還沒排掉名單）；從頭到尾沒列舉是 -1。</summary>
        public int Qualified = -1;
    }

    public sealed class SituationPickResult
    {
        public List<SituationPickRecord> Records = new List<SituationPickRecord>();

        /// <summary>實際挑中的，依挑中順序。</summary>
        public List<(SituationPickKind Kind, SituationPickCandidate Candidate)> Picks = new List<(SituationPickKind, SituationPickCandidate)>();

        public List<SituationPickKindSummary> KindSummaries = new List<SituationPickKindSummary>();

        /// <summary>名額沒用完就停了。</summary>
        public bool StoppedEarly;
    }

    /// <summary>
    /// 挑事件的共用做法：每件先挑種類（比重），再在那種事排掉名單上的人之後的候選裡挑一組（挑人比重）。
    /// 一種事全世界有一組合格就參加，合格幾組不影響它被挑中的機率。
    /// 候選的順序就是挑的順序：呼叫端要自己排穩定。
    /// </summary>
    public static class SituationPicker
    {
        /// <summary>挑人比重的下限：個性再不合，也還有這麼多的機會。</summary>
        public const double MinPersonWeight = 0.1;

        private sealed class KindState
        {
            public SituationPickKind Kind = null!;
            public IReadOnlyList<SituationPickCandidate>? All;
            public bool Enumerated;
        }

        public static SituationPickResult Pick(SituationPickRequest request)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            if (request.Rng == null) throw new ArgumentNullException(nameof(request.Rng));
            if (request.KindSeed == null) throw new ArgumentNullException(nameof(request.KindSeed));
            if (request.PersonSeed == null) throw new ArgumentNullException(nameof(request.PersonSeed));

            var result = new SituationPickResult();
            var states = new List<KindState>();
            foreach (var k in request.Kinds ?? Array.Empty<SituationPickKind>())
            {
                if (k == null) continue;
                bool lazy = k.CandidateProvider != null;
                states.Add(new KindState
                {
                    Kind = k,
                    All = lazy ? null : (k.Candidates ?? Array.Empty<SituationPickCandidate>()),
                    Enumerated = !lazy
                });
            }

            // 這一批裡前面幾件挑中的人：重新求值之前先不進共用名單，但同一批裡不能再挑到他們。
            var pickedThisCall = new HashSet<string>(StringComparer.Ordinal);

            for (int slotIdx = 0; slotIdx < request.Count; slotIdx++)
            {
                int slot = request.FirstSlot + slotIdx;
                var record = new SituationPickRecord
                {
                    Site = request.Site,
                    Day = request.Day,
                    Slot = slot,
                    SlotCount = request.TotalSlots ?? request.Count,
                    UsesRoster = request.Roster != null
                };
                if (request.Roster != null) record.RosterBefore.AddRange(request.Roster.Entries);
                result.Records.Add(record);

                bool Excluded(SituationPickCandidate c)
                {
                    foreach (var h in c.HeroIds())
                    {
                        if (pickedThisCall.Contains(h) || (request.Roster != null && request.Roster.Contains(h))) return true;
                    }
                    return false;
                }

                KindState? chosen = null;
                List<SituationPickCandidate>? chosenRemaining = null;
                int guard = states.Count + 1;
                while (guard-- > 0)
                {
                    var round = new PickKindRound();
                    var remainingByState = new Dictionary<KindState, List<SituationPickCandidate>>();
                    foreach (var st in states)
                    {
                        var entry = new PickKindEntry
                        {
                            Id = st.Kind.Id,
                            Weight = st.Kind.Weight,
                            WeightSource = st.Kind.WeightSource
                        };
                        if (st.Enumerated)
                        {
                            var all = st.All ?? Array.Empty<SituationPickCandidate>();
                            var left = all.Where(c => !Excluded(c)).ToList();
                            remainingByState[st] = left;
                            entry.Qualified = all.Count;
                            entry.ExcludedByRoster = all.Count - left.Count;
                            entry.Remaining = left.Count;
                        }

                        if (!(st.Kind.Weight > 0.0))
                        {
                            entry.Status = PickKindStatus.WeightZero;
                            entry.Reason = "weight 0";
                        }
                        else if (!st.Enumerated)
                        {
                            entry.Status = PickKindStatus.NotEnumerated;
                            entry.Reason = "not enumerated yet";
                        }
                        else if (entry.Qualified == 0)
                        {
                            entry.Status = PickKindStatus.NoCandidates;
                            entry.Reason = st.Kind.NoCandidateReason ?? "no candidate passed the conditions";
                        }
                        else if (entry.Remaining == 0)
                        {
                            entry.Status = PickKindStatus.AllExcludedByRoster;
                            entry.Reason = "all candidates include someone already involved today";
                        }
                        else
                        {
                            entry.Status = PickKindStatus.Eligible;
                        }
                        round.Entries.Add(entry);
                    }

                    record.Rounds.Add(round);

                    var eligibleIdx = new List<int>();
                    for (int i = 0; i < round.Entries.Count; i++)
                    {
                        if (round.Entries[i].Participates) eligibleIdx.Add(i);
                    }

                    if (eligibleIdx.Count == 0)
                    {
                        record.StopReason = "no kind has a candidate left";
                        break;
                    }

                    var weights = eligibleIdx.Select(i => round.Entries[i].Weight).ToList();
                    double total = weights.Sum();
                    long kindSeed = request.KindSeed(slot);
                    double r = request.Rng.NextDouble(kindSeed);
                    int pickedPos = request.Rng.PickWeighted(weights, kindSeed);
                    if (pickedPos < 0 || pickedPos >= eligibleIdx.Count) pickedPos = eligibleIdx.Count - 1;

                    double acc = 0.0;
                    for (int n = 0; n < eligibleIdx.Count; n++)
                    {
                        var e = round.Entries[eligibleIdx[n]];
                        e.IntervalStart = acc;
                        acc += e.Weight;
                        e.IntervalEnd = acc;
                        e.Probability = total > 0.0 ? e.Weight / total : 0.0;
                    }

                    round.TotalWeight = total;
                    round.R = r;
                    round.RTimesTotal = r * total;

                    var pickedState = states[eligibleIdx[pickedPos]];
                    round.PickedKindId = pickedState.Kind.Id;

                    if (!pickedState.Enumerated)
                    {
                        var listed = pickedState.Kind.CandidateProvider!() ?? Array.Empty<SituationPickCandidate>();
                        pickedState.All = listed;
                        pickedState.Enumerated = true;
                        var left = listed.Where(c => !Excluded(c)).ToList();
                        round.EnumeratedPickedKind = true;
                        round.EnumeratedQualified = listed.Count;
                        round.EnumeratedRemaining = left.Count;
                        if (left.Count == 0)
                        {
                            continue;
                        }
                        chosen = pickedState;
                        chosenRemaining = left;
                        break;
                    }

                    chosen = pickedState;
                    chosenRemaining = remainingByState[pickedState];
                    break;
                }

                if (chosen == null || chosenRemaining == null)
                {
                    if (record.StopReason == null) record.StopReason = "no kind has a candidate left";
                    result.StoppedEarly = true;
                    break;
                }

                var kind = chosen.Kind;
                record.PickedKindId = kind.Id;
                record.PickedKind = kind;
                record.ExcludedByRoster = (chosen.All?.Count ?? 0) - chosenRemaining.Count;

                var stage = BuildPersonStage(kind, chosenRemaining, request.PlayerHeroId);
                record.Person = stage;
                long personSeed = request.PersonSeed(slot, kind);
                double pr = request.Rng.NextDouble(personSeed);
                var personWeights = stage.Entries.Select(e => e.Weight).ToList();
                int pickedIdx = request.Rng.PickWeighted(personWeights, personSeed);
                if (pickedIdx < 0 || pickedIdx >= stage.Entries.Count) pickedIdx = stage.Entries.Count - 1;
                stage.R = pr;
                stage.RTimesTotal = pr * stage.TotalWeight;
                stage.PickedIndex = pickedIdx;

                var picked = stage.Entries[pickedIdx].Candidate;
                record.PickedCandidate = picked;
                record.HeroesOfPicked.AddRange(picked.HeroIds().Distinct(StringComparer.Ordinal));
                foreach (var h in record.HeroesOfPicked) pickedThisCall.Add(h);
                result.Picks.Add((kind, picked));
            }

            foreach (var st in states)
            {
                result.KindSummaries.Add(new SituationPickKindSummary
                {
                    Id = st.Kind.Id,
                    Weight = st.Kind.Weight,
                    Qualified = st.Enumerated ? (st.All?.Count ?? 0) : -1
                });
            }

            return result;
        }

        private static PickPersonStage BuildPersonStage(SituationPickKind kind, List<SituationPickCandidate> remaining, string? playerHeroId)
        {
            var stage = new PickPersonStage
            {
                Remaining = remaining.Count,
                UsesTraits = kind.PickTraitRoles != null && kind.PickTraitRoles.Count > 0
            };

            double acc = 0.0;
            for (int i = 0; i < remaining.Count; i++)
            {
                var entry = new PickPersonEntry { Index = i, Candidate = remaining[i] };
                entry.Weight = ComputePersonWeight(kind, remaining[i], playerHeroId, entry.Terms);
                entry.IntervalStart = acc;
                acc += entry.Weight;
                entry.IntervalEnd = acc;
                stage.Entries.Add(entry);
            }
            stage.TotalWeight = acc;

            stage.Distribution = stage.Entries
                .GroupBy(e => Math.Round(e.Weight, 6))
                .Select(g => (Weight: g.Key, Count: g.Count()))
                .OrderByDescending(x => x.Weight)
                .ToList();

            return stage;
        }

        /// <summary>
        /// 一組候選的挑人比重 = Π（每個有 pickTraits 的角色的 max(0.1, 1 + Σ 係數 × 個性等級)）。
        /// 沒有 pickTraits 的角色、綁玩家的角色、查不到個性的角色算 ×1。
        /// </summary>
        public static double ComputePersonWeight(SituationPickKind kind, SituationPickCandidate candidate, string? playerHeroId, List<PickTraitTerm>? terms = null)
        {
            double weight = 1.0;
            if (kind.PickTraitRoles == null) return weight;

            foreach (var roleKv in kind.PickTraitRoles.OrderBy(k => k.Key, StringComparer.Ordinal))
            {
                var term = new PickTraitTerm { Role = roleKv.Key };
                string? hero = null;
                if (candidate.RoleHeroIds != null && candidate.RoleHeroIds.TryGetValue(roleKv.Key, out var bound)) hero = bound;
                term.HeroId = hero;

                TraitProfile? profile = null;
                bool haveProfile = candidate.RoleTraits != null && candidate.RoleTraits.TryGetValue(roleKv.Key, out profile) && profile != null;

                if (string.IsNullOrEmpty(hero))
                {
                    term.SkipReason = "role not bound";
                }
                else if (!string.IsNullOrEmpty(playerHeroId) && string.Equals(hero, playerHeroId, StringComparison.Ordinal))
                {
                    term.SkipReason = "bound to the player";
                }
                else if (!haveProfile)
                {
                    term.SkipReason = "traits not available";
                }
                else
                {
                    term.Applies = true;
                    double sum = 0.0;
                    foreach (var tKv in roleKv.Value.OrderBy(k => k.Key, StringComparer.Ordinal))
                    {
                        int level = OpinionTraits.LevelOf(tKv.Key, profile) ?? 0;
                        term.Parts.Add((tKv.Key, tKv.Value, level));
                        sum += tKv.Value * level;
                    }
                    term.Raw = 1.0 + sum;
                    term.Clamped = term.Raw < MinPersonWeight;
                    term.Factor = Math.Max(MinPersonWeight, term.Raw);
                    weight *= term.Factor;
                }

                terms?.Add(term);
            }

            return weight;
        }

        /// <summary>
        /// 把一組已經過條件的指派變成候選：推導角色綁到的人（求值結果）加上非推導角色綁到的人。
        /// 玩家在場與流浪者事件兩處共用。
        /// </summary>
        internal static SituationPickCandidate CandidateFromAssignment(
            SituationPickKind kind,
            IReadOnlyDictionary<string, string> nonDerivedRoleHeroIds,
            IReadOnlyDictionary<string, string?>? boundHeroIds,
            IHeroTraitLookup? lookup,
            string? playerHeroId,
            object payload)
        {
            var roles = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
            if (boundHeroIds != null)
            {
                foreach (var kv in boundHeroIds) roles[kv.Key] = kv.Value;
            }
            foreach (var kv in nonDerivedRoleHeroIds) roles[kv.Key] = kv.Value;

            return new SituationPickCandidate
            {
                RoleHeroIds = roles,
                RoleTraits = LookUpRoleTraits(kind, roles, lookup, playerHeroId),
                Payload = payload
            };
        }

        /// <summary>
        /// 替一組候選查出「有 pickTraits 的角色」綁到的人的個性。
        /// 綁玩家的角色不查（挑人比重那一項本來就算 ×1）。
        /// </summary>
        public static Dictionary<string, TraitProfile?>? LookUpRoleTraits(
            SituationPickKind kind,
            IReadOnlyDictionary<string, string?> roleHeroIds,
            IHeroTraitLookup? lookup,
            string? playerHeroId)
        {
            if (kind.PickTraitRoles == null || kind.PickTraitRoles.Count == 0 || lookup == null) return null;
            var result = new Dictionary<string, TraitProfile?>(StringComparer.OrdinalIgnoreCase);
            foreach (var role in kind.PickTraitRoles.Keys)
            {
                if (!roleHeroIds.TryGetValue(role, out var hero) || string.IsNullOrEmpty(hero)) continue;
                if (!string.IsNullOrEmpty(playerHeroId) && string.Equals(hero, playerHeroId, StringComparison.Ordinal)) continue;
                result[role] = lookup.Of(hero!);
            }
            return result;
        }
    }
}
