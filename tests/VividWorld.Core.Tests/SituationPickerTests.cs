using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using VividWorld.Core.Config;
using VividWorld.Core.Persistence;
using VividWorld.Core.Rumors;
using VividWorld.Core.Situations;
using VividWorld.Core.Tests.Fakes;
using VividWorld.Core.Util;
using Xunit;

namespace VividWorld.Core.Tests
{
    public class SituationPickerTests
    {
        private static readonly SplitMix64Rng Rng = new SplitMix64Rng();

        private readonly Xunit.Abstractions.ITestOutputHelper _output;

        public SituationPickerTests(Xunit.Abstractions.ITestOutputHelper output)
        {
            _output = output;
        }

        // ── 小工具 ────────────────────────────────────────────────────────────────

        private static SituationPickCandidate Cand(string? settlement, params (string Role, string? Hero)[] roles)
        {
            return new SituationPickCandidate
            {
                SettlementId = settlement,
                RoleHeroIds = roles.ToDictionary(r => r.Role, r => r.Hero, StringComparer.OrdinalIgnoreCase)
            };
        }

        private static SituationPickCandidate CandWithTraits(string hero, string other, TraitProfile? profile, string role = "a")
        {
            var c = Cand("town_1", (role, hero), ("b", other));
            c.RoleTraits = new Dictionary<string, TraitProfile?> { [role] = profile };
            return c;
        }

        private static SituationPickKind Kind(string id, double weight, params SituationPickCandidate[] candidates)
        {
            return new SituationPickKind { Id = id, Weight = weight, WeightSource = "template weight", Candidates = candidates };
        }

        private static IReadOnlyDictionary<string, IReadOnlyDictionary<string, double>> Coef(string role, params (string Trait, double Value)[] coefs)
        {
            return new Dictionary<string, IReadOnlyDictionary<string, double>>(StringComparer.OrdinalIgnoreCase)
            {
                [role] = coefs.ToDictionary(c => c.Trait, c => c.Value, StringComparer.OrdinalIgnoreCase)
            };
        }

        private static SituationPickResult PickOnce(IReadOnlyList<SituationPickKind> kinds, long seed, SituationRoster? roster = null, int count = 1, string? player = null)
        {
            return SituationPicker.Pick(new SituationPickRequest
            {
                Kinds = kinds,
                Count = count,
                Roster = roster,
                Rng = Rng,
                PlayerHeroId = player,
                KindSeed = slot => RumorSeed.Of(seed, "kind", slot),
                PersonSeed = (slot, k) => RumorSeed.Of(seed, "person", slot, k.Id),
                Site = "test",
                Day = 5
            });
        }

        private static TraitProfile Profile(string id, int honor = 0, int mercy = 0, int valor = 0, int calculating = 0, int generosity = 0)
        {
            return new TraitProfile { HeroId = id, Honor = honor, Mercy = mercy, Valor = valor, Calculating = calculating, Generosity = generosity };
        }

        // ── 先挑種類再挑人 ────────────────────────────────────────────────────────

        [Fact]
        public void Pick_KindWithManyCandidates_IsNotPickedMoreOften()
        {
            var many = Enumerable.Range(0, 60).Select(i => Cand("t", ("a", $"m{i}a"), ("b", $"m{i}b"))).ToArray();
            var few = new[] { Cand("t", ("a", "f1"), ("b", "f2")) };
            var kinds = new[] { Kind("many", 1.0, many), Kind("few", 1.0, few) };

            int manyCount = 0;
            const int n = 4000;
            for (int s = 0; s < n; s++)
            {
                var r = PickOnce(kinds, s);
                if (r.Picks[0].Kind.Id == "many") manyCount++;
            }

            double share = manyCount / (double)n;
            Assert.InRange(share, 0.46, 0.54);
        }

        [Fact]
        public void Pick_KindProbabilityFollowsWeight()
        {
            var kinds = new[]
            {
                Kind("heavy", 3.0, Cand("t", ("a", "h1"), ("b", "h2"))),
                Kind("light", 1.0, Cand("t", ("a", "l1"), ("b", "l2")))
            };

            int heavy = 0;
            const int n = 4000;
            for (int s = 0; s < n; s++)
            {
                if (PickOnce(kinds, s).Picks[0].Kind.Id == "heavy") heavy++;
            }

            Assert.InRange(heavy / (double)n, 0.71, 0.79);
        }

        [Fact]
        public void Pick_KindWithoutCandidates_OrWeightZero_NeverParticipates()
        {
            var kinds = new[]
            {
                Kind("empty", 5.0),
                Kind("zero", 0.0, Cand("t", ("a", "z1"), ("b", "z2"))),
                Kind("ok", 1.0, Cand("t", ("a", "o1"), ("b", "o2")))
            };

            for (int s = 0; s < 200; s++)
            {
                var r = PickOnce(kinds, s);
                Assert.Equal("ok", r.Picks[0].Kind.Id);
            }

            var entries = PickOnce(kinds, 1).Records[0].Rounds[0].Entries;
            Assert.Equal(PickKindStatus.NoCandidates, entries.Single(e => e.Id == "empty").Status);
            Assert.Equal(PickKindStatus.WeightZero, entries.Single(e => e.Id == "zero").Status);
        }

        [Fact]
        public void Pick_NoKindHasCandidates_StopsAndDoesNotFillTheQuota()
        {
            var kinds = new[] { Kind("empty", 1.0), Kind("zero", 0.0, Cand("t", ("a", "z1"), ("b", "z2"))) };
            var r = PickOnce(kinds, 1, count: 3);

            Assert.Empty(r.Picks);
            Assert.True(r.StoppedEarly);
            Assert.Single(r.Records);
            Assert.Null(r.Records[0].PickedKindId);
            Assert.NotNull(r.Records[0].StopReason);
        }

        [Fact]
        public void Pick_QuotaOfThree_NeverPicksSomeoneAlreadyPickedInTheSameCall()
        {
            var cands = new[]
            {
                Cand("t", ("a", "x1"), ("b", "x2")),
                Cand("t", ("a", "x1"), ("b", "x3")),
                Cand("t", ("a", "x4"), ("b", "x5")),
                Cand("t", ("a", "x6"), ("b", "x2"))
            };
            for (int s = 0; s < 100; s++)
            {
                var r = PickOnce(new[] { Kind("k", 1.0, cands) }, s, count: 3);
                var heroes = r.Picks.SelectMany(p => p.Candidate.HeroIds()).ToList();
                Assert.Equal(heroes.Count, heroes.Distinct().Count());
            }
        }

        // ── 挑人比重 ──────────────────────────────────────────────────────────────

        [Fact]
        public void PersonWeight_CoefficientTimesLevel_AddsUpAcrossTraits()
        {
            var kind = Kind("k", 1.0);
            kind.PickTraitRoles = Coef("a", ("mercy", 0.5), ("valor", 0.5));
            var c = CandWithTraits("h1", "h2", Profile("h1", mercy: 2, valor: 1));

            var terms = new List<PickTraitTerm>();
            double w = SituationPicker.ComputePersonWeight(kind, c, null, terms);

            Assert.Equal(2.5, w, 6);
            Assert.Single(terms);
            Assert.True(terms[0].Applies);
            Assert.Equal(2.5, terms[0].Raw, 6);
            Assert.False(terms[0].Clamped);
        }

        [Fact]
        public void PersonWeight_ClampedUpToOneTenth()
        {
            var kind = Kind("k", 1.0);
            kind.PickTraitRoles = Coef("a", ("mercy", -0.5));
            var c = CandWithTraits("h1", "h2", Profile("h1", mercy: 2));

            var terms = new List<PickTraitTerm>();
            double w = SituationPicker.ComputePersonWeight(kind, c, null, terms);

            Assert.Equal(0.1, w, 6);
            Assert.Equal(0.0, terms[0].Raw, 6);
            Assert.True(terms[0].Clamped);
        }

        [Fact]
        public void PersonWeight_RoleBoundToThePlayer_CountsAsOne()
        {
            var kind = Kind("k", 1.0);
            kind.PickTraitRoles = Coef("a", ("valor", 0.5));
            var c = CandWithTraits("player", "h2", Profile("player", valor: 2));

            var terms = new List<PickTraitTerm>();
            Assert.Equal(1.0, SituationPicker.ComputePersonWeight(kind, c, "player", terms), 6);
            Assert.False(terms[0].Applies);
            Assert.Equal("bound to the player", terms[0].SkipReason);
        }

        [Fact]
        public void PersonWeight_MissingTraits_OrUnboundRole_CountsAsOne()
        {
            var kind = Kind("k", 1.0);
            kind.PickTraitRoles = Coef("a", ("valor", 0.5));

            Assert.Equal(1.0, SituationPicker.ComputePersonWeight(kind, CandWithTraits("h1", "h2", null), null), 6);
            Assert.Equal(1.0, SituationPicker.ComputePersonWeight(kind, Cand("t", ("b", "h2")), null), 6);
        }

        [Fact]
        public void TraitRolesOf_LeavesOutDerivedRolesAndPlayerOnlyRoles()
        {
            var t = new SituationTemplate { Id = "x" };
            t.Roles["a"] = new SituationRoleDef { PickTraits = new Dictionary<string, double> { ["valor"] = 0.5 } };
            t.Roles["d"] = new SituationRoleDef { Derived = "settlementOwnerClanLeader", PickTraits = new Dictionary<string, double> { ["valor"] = 0.5 } };
            t.Roles["p"] = new SituationRoleDef { PlayerOnly = true, PickTraits = new Dictionary<string, double> { ["valor"] = 0.5 } };
            t.Roles["n"] = new SituationRoleDef();

            var roles = SituationPickKind.TraitRolesOf(t);
            Assert.NotNull(roles);
            Assert.Equal(new[] { "a" }, roles!.Keys.ToArray());
        }

        [Fact]
        public void Pick_BoldHeroIsPickedMoreOften_ByTheCoefficient()
        {
            var kind = Kind("k", 1.0,
                CandWithTraits("bold", "o1", Profile("bold", valor: 2)),
                CandWithTraits("meek", "o2", Profile("meek", valor: -2)));
            kind.PickTraitRoles = Coef("a", ("valor", 0.5));

            int bold = 0;
            const int n = 4000;
            for (int s = 0; s < n; s++)
            {
                if (PickOnce(new[] { kind }, s).Picks[0].Candidate.RoleHeroIds["a"] == "bold") bold++;
            }

            // 比重 2.0 對 0.1（夾住）：2 / 2.1 ≈ 0.952
            Assert.InRange(bold / (double)n, 0.93, 0.97);
        }

        [Fact]
        public void Pick_WithoutPickTraits_SamePicksAsUniformDraw()
        {
            var cands = Enumerable.Range(0, 7).Select(i => Cand("t", ("a", $"u{i}"), ("b", $"v{i}"))).ToArray();
            var kind = Kind("k", 1.0, cands);

            var counts = new int[cands.Length];
            const int n = 7000;
            for (int s = 0; s < n; s++)
            {
                var r = PickOnce(new[] { kind }, s);
                int idx = Array.IndexOf(cands, r.Picks[0].Candidate);
                counts[idx]++;

                // 跟原本的平均抽同一個種子得到同一個答案
                long personSeed = RumorSeed.Of((long)s, "person", 0, "k");
                Assert.Equal(Rng.Pick(cands.Length, personSeed), idx);
            }

            foreach (int c in counts) Assert.InRange(c / (double)n, 1.0 / 7 - 0.02, 1.0 / 7 + 0.02);
            Assert.False(PickOnce(new[] { kind }, 1).Records[0].Person!.UsesTraits);
        }

        // ── 名單 ──────────────────────────────────────────────────────────────────

        [Fact]
        public void Roster_ExcludesAnyCandidateContainingARosterHero_IncludingDerivedRoles()
        {
            var roster = new SituationRoster();
            roster.AddExecuted(new[] { "lord_x" }, "tavern_words #1");

            var viaDerived = Cand("t", ("a", "p1"), ("b", "p2"), ("host", "lord_x"));
            var clean = Cand("t", ("a", "p3"), ("b", "p4"), ("host", "lord_y"));
            var kinds = new[] { Kind("k", 1.0, viaDerived, clean) };

            for (int s = 0; s < 100; s++)
            {
                Assert.Same(clean, PickOnce(kinds, s, roster).Picks[0].Candidate);
            }

            var round = PickOnce(kinds, 1, roster).Records[0].Rounds[0];
            Assert.Equal(2, round.Entries[0].Qualified);
            Assert.Equal(1, round.Entries[0].ExcludedByRoster);
            Assert.Equal(1, round.Entries[0].Remaining);
        }

        [Fact]
        public void Roster_PickDoesNotWriteToIt_UntilTheCallerAddsTheExecutedHeroes()
        {
            var roster = new SituationRoster();
            var kinds = new[] { Kind("k", 1.0, Cand("t", ("a", "p1"), ("b", "p2"))) };
            var r = PickOnce(kinds, 1, roster);

            Assert.Equal(0, roster.Count);

            roster.AddExecuted(r.Picks[0].Candidate.HeroIds(), "k/evt_1");
            Assert.Equal(2, roster.Count);
            Assert.Equal("k/evt_1", roster.Entries[0].Source);
            Assert.True(roster.Contains("p1"));

            Assert.Empty(PickOnce(kinds, 1, roster).Picks);
        }

        [Fact]
        public void Roster_ContinuesAcrossCalls_LikeTheSecondStepAfterTheMainSix()
        {
            var roster = new SituationRoster();
            var main = new[] { Kind("main", 1.0, Cand("t", ("a", "p1"), ("b", "p2"))) };
            var follow = new[] { Kind("follow", 1.0, Cand("t", ("a", "p2"), ("b", "p3")), Cand("t", ("a", "p4"), ("b", "p5"))) };

            var first = PickOnce(main, 1, roster);
            roster.AddExecuted(first.Picks[0].Candidate.HeroIds(), "main");
            var second = PickOnce(follow, 1, roster);

            Assert.Equal("p4", second.Picks[0].Candidate.RoleHeroIds["a"]);
        }

        [Fact]
        public void FirstSlot_OneCallPerSlot_UsesTheSameSeedsAndLabelsAsOneBatch()
        {
            var kinds = new[]
            {
                Kind("x", 1.0, Cand("t", ("a", "h1"), ("b", "h2")), Cand("t", ("a", "h3"), ("b", "h4"))),
                Kind("y", 2.0, Cand("t", ("a", "h5"), ("b", "h6")))
            };
            var batch = PickOnce(kinds, 77, count: 1);
            var single = SituationPicker.Pick(new SituationPickRequest
            {
                Kinds = kinds,
                Count = 1,
                FirstSlot = 0,
                TotalSlots = 3,
                Rng = Rng,
                KindSeed = slot => RumorSeed.Of(77L, "kind", slot),
                PersonSeed = (slot, k) => RumorSeed.Of(77L, "person", slot, k.Id),
                Site = "test",
                Day = 5
            });
            Assert.Equal(batch.Records[0].PickedKindId, single.Records[0].PickedKindId);
            Assert.Same(batch.Picks[0].Candidate, single.Picks[0].Candidate);
            Assert.Equal(3, single.Records[0].SlotCount);

            var seenSlots = new List<int>();
            var third = SituationPicker.Pick(new SituationPickRequest
            {
                Kinds = kinds,
                Count = 1,
                FirstSlot = 2,
                TotalSlots = 3,
                Rng = Rng,
                KindSeed = slot => { seenSlots.Add(slot); return RumorSeed.Of(77L, "kind", slot); },
                PersonSeed = (slot, k) => RumorSeed.Of(77L, "person", slot, k.Id),
                Site = "test",
                Day = 5
            });
            Assert.Equal(new[] { 2 }, seenSlots);
            Assert.Equal(2, third.Records[0].Slot);
            Assert.Contains("item 3/3", SituationPickLogFormatter.Format(third.Records[0])[0]);
        }

        [Fact]
        public void NoRoster_DoesNotExclude()
        {
            var roster = new SituationRoster();
            roster.AddExecuted(new[] { "p1" }, "something");
            var kinds = new[] { Kind("k", 1.0, Cand("t", ("a", "p1"), ("b", "p2"))) };

            Assert.Single(PickOnce(kinds, 1, roster: null).Picks);
            Assert.Empty(PickOnce(kinds, 1, roster).Picks);
        }

        // ── 晚一點才給候選 ─────────────────────────────────────────────────────────

        [Fact]
        public void LazyKind_NotPickedMeansNeverEnumerated()
        {
            int calls = 0;
            var lazy = new SituationPickKind
            {
                Id = "lazy",
                Weight = 0.0001,
                WeightSource = "share",
                CandidateProvider = () => { calls++; return new[] { Cand("t", ("a", "l1"), ("b", "l2")) }; }
            };
            var kinds = new[] { Kind("main", 1000.0, Cand("t", ("a", "m1"), ("b", "m2"))), lazy };

            var r = PickOnce(kinds, 1);

            Assert.Equal("main", r.Picks[0].Kind.Id);
            Assert.Equal(0, calls);
            Assert.Equal(-1, r.KindSummaries.Single(s => s.Id == "lazy").Qualified);
        }

        [Fact]
        public void LazyKind_EnumeratesZero_IsMarkedAndAnotherKindIsRepicked()
        {
            int calls = 0;
            var lazy = new SituationPickKind
            {
                Id = "lazy",
                Weight = 1000.0,
                WeightSource = "share",
                CandidateProvider = () => { calls++; return Array.Empty<SituationPickCandidate>(); }
            };
            var kinds = new[] { lazy, Kind("other", 1.0, Cand("t", ("a", "o1"), ("b", "o2"))) };

            var r = PickOnce(kinds, 1);

            Assert.Equal("other", r.Picks[0].Kind.Id);
            Assert.Equal(1, calls);
            var record = r.Records[0];
            Assert.Equal(2, record.Rounds.Count);
            Assert.Equal(PickKindStatus.NotEnumerated, record.Rounds[0].Entries[0].Status);
            Assert.True(record.Rounds[0].EnumeratedPickedKind);
            Assert.Equal(PickKindStatus.NoCandidates, record.Rounds[1].Entries[0].Status);
            Assert.Equal(0, r.KindSummaries.Single(s => s.Id == "lazy").Qualified);
        }

        [Fact]
        public void LazyKind_AllEnumeratedGroupsExcludedByRoster_IsRepickedToo()
        {
            var roster = new SituationRoster();
            roster.AddExecuted(new[] { "busy" }, "earlier");
            var lazy = new SituationPickKind
            {
                Id = "lazy",
                Weight = 1000.0,
                WeightSource = "share",
                CandidateProvider = () => new[] { Cand("t", ("a", "busy"), ("b", "l2")) }
            };
            var kinds = new[] { lazy, Kind("other", 1.0, Cand("t", ("a", "o1"), ("b", "o2"))) };

            var r = PickOnce(kinds, 1, roster);

            Assert.Equal("other", r.Picks[0].Kind.Id);
            var round1 = r.Records[0].Rounds[0];
            Assert.Equal(1, round1.EnumeratedQualified);
            Assert.Equal(0, round1.EnumeratedRemaining);
            Assert.Equal(PickKindStatus.AllExcludedByRoster, r.Records[0].Rounds[1].Entries[0].Status);
        }

        [Fact]
        public void LazyKind_EnumeratedOnce_EvenWhenPickedAgainInALaterSlot()
        {
            int calls = 0;
            var lazy = new SituationPickKind
            {
                Id = "lazy",
                Weight = 1.0,
                WeightSource = "share",
                CandidateProvider = () =>
                {
                    calls++;
                    return new[] { Cand("t", ("a", "l1"), ("b", "l2")), Cand("t", ("a", "l3"), ("b", "l4")) };
                }
            };

            var r = PickOnce(new[] { lazy }, 1, count: 2);

            Assert.Equal(2, r.Picks.Count);
            Assert.Equal(1, calls);
        }

        // ── 日誌 ──────────────────────────────────────────────────────────────────

        private static SituationPickResult BigResult(out SituationPickKind kind)
        {
            // 十組：最後一組比重最高，挑中它的機會最大，換種子找一個挑中前 5 名之外的情況來測
            var cands = new List<SituationPickCandidate>();
            for (int i = 0; i < 10; i++)
            {
                cands.Add(CandWithTraits($"h{i}", $"o{i}", Profile($"h{i}", valor: i < 9 ? 0 : 2)));
            }
            kind = Kind("big", 2.0, cands.ToArray());
            kind.PickTraitRoles = Coef("a", ("valor", 0.5));

            for (int s = 0; s < 500; s++)
            {
                var r = PickOnce(new[] { kind }, s);
                var stage = r.Records[0].Person!;
                var top = stage.Entries.OrderByDescending(e => e.Weight).ThenBy(e => e.Index).Take(5).Select(e => e.Index).ToList();
                if (!top.Contains(stage.PickedIndex)) return r;
            }
            throw new InvalidOperationException("no seed picked outside the top five");
        }

        [Fact]
        public void Format_ListsEveryKindWithItsReason_AndTheIntervalsMatchR()
        {
            var roster = new SituationRoster();
            roster.AddExecuted(new[] { "busy" }, "tavern_words #1");
            var kinds = new[]
            {
                Kind("alpha", 3.0, Cand("t", ("a", "a1"), ("b", "a2")), Cand("t", ("a", "busy"), ("b", "a3"))),
                Kind("zero", 0.0, Cand("t", ("a", "z1"), ("b", "z2"))),
                Kind("none", 1.0),
                Kind("blocked", 1.0, Cand("t", ("a", "busy"), ("b", "b2"))),
                new SituationPickKind { Id = "later", Weight = 1.0, WeightSource = "share", CandidateProvider = () => Array.Empty<SituationPickCandidate>() }
            };

            var r = PickOnce(kinds, 7, roster);
            var text = string.Join("\n", SituationPickLogFormatter.Format(r.Records[0], new SituationPickLogOptions { NameOf = id => id == "a1" ? "Dessert" : null }));

            Assert.Contains("Pick [test] day 5 item 1/1", text);
            Assert.Contains("alpha weight 3.00 (template weight): 2 qualified, 1 excluded by roster, 1 left", text);
            Assert.Contains("zero weight 0.00 (template weight): skipped, weight 0", text);
            Assert.Contains("none weight 1.00 (template weight): skipped, 0 qualified", text);
            Assert.Contains("blocked weight 1.00 (template weight): skipped, 1 qualified, 1 excluded by roster, 0 left", text);
            Assert.Contains("later weight 1.00 (share): not enumerated yet", text);
            Assert.Contains("roster before: busy [tavern_words #1]", text);
            Assert.Contains("people: 1 group(s) left", text);
            Assert.Contains("this kind ignores personality", text);

            var round = r.Records[0].Rounds[0];
            Assert.Equal(round.R * round.TotalWeight, round.RTimesTotal, 9);
            var pickedEntry = round.Entries.Single(e => e.Id == round.PickedKindId);
            Assert.True(round.RTimesTotal >= pickedEntry.IntervalStart && round.RTimesTotal < pickedEntry.IntervalEnd);
        }

        [Fact]
        public void Format_PersonSection_ShowsTraitsCoefficientsRawValueClampAndInterval()
        {
            var kind = Kind("k", 1.0,
                CandWithTraits("bold", "o1", Profile("bold", mercy: 1, valor: 2)),
                CandWithTraits("harsh", "o2", Profile("harsh", mercy: 2, valor: -2)));
            kind.PickTraitRoles = Coef("a", ("mercy", 0.5), ("valor", 0.5));

            var r = PickOnce(new[] { kind }, 3);
            var text = string.Join("\n", SituationPickLogFormatter.Format(r.Records[0], new SituationPickLogOptions { AllCandidates = true }));

            Assert.Contains("mercy 1*0.50 valor 2*0.50 -> 1+sum=2.50", text);
            Assert.Contains("mercy 2*0.50 valor -2*0.50 -> 1+sum=1.00", text);
            Assert.Contains("weights 2.50x1, 1.00x1", text);
            Assert.Contains("[0.00, 2.50)", text);

            var clampKind = Kind("k2", 1.0, CandWithTraits("bold", "o1", Profile("bold", mercy: 2)));
            clampKind.PickTraitRoles = Coef("a", ("mercy", -0.5));
            var clampText = string.Join("\n", SituationPickLogFormatter.Format(PickOnce(new[] { clampKind }, 3).Records[0]));
            Assert.Contains("1+sum=0.00, clamped to 0.10", clampText);
        }

        [Fact]
        public void Format_PickedGroupOutsideTheTopFive_IsStillPrinted_AndAllModePrintsEveryGroup()
        {
            var r = BigResult(out _);
            var record = r.Records[0];
            var stage = record.Person!;

            var normal = SituationPickLogFormatter.Format(record);
            var normalGroupLines = normal.Where(l => l.TrimStart().StartsWith("#", StringComparison.Ordinal)).ToList();
            Assert.Equal(SituationPickLogFormatter.TopCandidateCount + 1, normalGroupLines.Count);
            Assert.Contains(normalGroupLines, l => l.Contains($"#{stage.PickedIndex + 1} ") && l.Contains("<- picked"));
            Assert.Contains(normal, l => l.Contains("10 group(s) in total"));

            var all = SituationPickLogFormatter.Format(record, new SituationPickLogOptions { AllCandidates = true });
            Assert.Equal(10, all.Count(l => l.TrimStart().StartsWith("#", StringComparison.Ordinal)));

            Assert.Contains(all, l => l.Contains($"r {stage.R:0.0000}, r*total {stage.RTimesTotal:0.0000} -> group #{stage.PickedIndex + 1}"));
            var picked = stage.Entries[stage.PickedIndex];
            Assert.True(stage.RTimesTotal >= picked.IntervalStart && stage.RTimesTotal < picked.IntervalEnd);
        }

        [Fact]
        public void Format_RosterSection_ListsWhoWasAlreadyInAndWhoWouldBeAdded()
        {
            var roster = new SituationRoster();
            roster.AddExecuted(new[] { "busy" }, "victory_credit evt_9");
            var kinds = new[] { Kind("k", 1.0, Cand("t", ("a", "p1"), ("b", "p2"), ("host", "p3")), Cand("t", ("a", "busy"), ("b", "p9"))) };

            var text = string.Join("\n", SituationPickLogFormatter.Format(PickOnce(kinds, 1, roster).Records[0]));

            Assert.Contains("roster before: busy [victory_credit evt_9]", text);
            Assert.Contains("excluded by roster: 1 group(s) of k", text);
            Assert.Contains("to be added if it goes ahead: ", text);
            Assert.Contains("p3", text);
        }

        [Fact]
        public void Format_QualifiedCounts_PerKindAndRoster()
        {
            var lazy = new SituationPickKind { Id = "later", Weight = 0.0001, WeightSource = "share", CandidateProvider = () => Array.Empty<SituationPickCandidate>() };
            var kinds = new[] { Kind("a", 1000.0, Cand("t", ("a", "x1"), ("b", "x2"))), Kind("b", 0.0), lazy };
            var r = PickOnce(kinds, 1);

            string line = SituationPickLogFormatter.FormatQualifiedCounts(r.KindSummaries, 4);
            Assert.Equal("qualified per kind: a 1, b 0, later not enumerated; roster 4 hero(es)", line);
        }

        // ── 資料檔 ────────────────────────────────────────────────────────────────

        private static string FindRepoFile(params string[] parts)
        {
            string? current = AppContext.BaseDirectory;
            while (!string.IsNullOrEmpty(current))
            {
                if (File.Exists(Path.Combine(current, "VividWorld.sln")))
                {
                    string path = Path.Combine(new[] { current }.Concat(parts).ToArray());
                    if (File.Exists(path)) return path;
                }
                var parent = Directory.GetParent(current);
                if (parent == null) break;
                current = parent.FullName;
            }
            throw new FileNotFoundException($"Could not find '{string.Join("/", parts)}'");
        }

        [Fact]
        public void DataFile_CityWeights_AndPickTraits_MatchTheDecidedValues()
        {
            var catalog = SituationCatalogLoader.Load(File.ReadAllText(FindRepoFile("module", "ModuleData", "vividworld_situations.json")));

            var weights = new Dictionary<string, double>
            {
                ["tavern_words"] = 3, ["advice_sought"] = 2, ["hunt_wager"] = 2,
                ["victory_credit"] = 1.5, ["seat_dispute"] = 1, ["market_brawl"] = 1
            };
            foreach (var kv in weights)
            {
                Assert.Equal(kv.Value, catalog.ById(kv.Key)!.Weight, 6);
            }

            void Check(string situation, string role, params (string Trait, double Value)[] expected)
            {
                var pt = catalog.ById(situation)!.Roles[role].PickTraits;
                Assert.NotNull(pt);
                Assert.Equal(expected.Length, pt!.Count);
                foreach (var e in expected) Assert.Equal(e.Value, pt[e.Trait], 6);
            }

            Check("wanderer_campfire_jab", "mocker", ("mercy", -0.5));
            Check("wanderer_battle_rescue", "rescuer", ("mercy", 0.5), ("valor", 0.5));
            Check("wanderer_battle_credit", "claimant", ("valor", 0.5), ("honor", -0.5));
            Check("wanderer_teaching", "teacher", ("generosity", 0.5));
            Check("hunt_wager", "challenger", ("valor", 0.5));
            Check("advice_sought", "student", ("calculating", 0.5));

            int withPickTraits = catalog.Situations.SelectMany(s => s.Roles.Values).Count(r => r.PickTraits != null);
            Assert.Equal(6, withPickTraits);
            Assert.DoesNotContain(catalog.Issues, i => i.Field.Contains("pickTraits"));
        }

        private static SituationCatalog LoadWithRoles(string rolesJson, string extra = "")
        {
            string json = @"{ ""situations"": [ {
                ""id"": ""t1"", ""trigger"": ""direct"", ""weight"": 1, ""decider"": ""a"",
                ""roles"": " + rolesJson + @",
                ""conditions"": [],
                ""branches"": [ { ""id"": ""b1"", ""base"": 1, ""events"": [ { ""type"": ""seat_dispute_demanded"", ""bind"": { ""SLIGHTED"": ""a"", ""FAVORED"": ""b"", ""SETTLEMENT"": ""@settlement"" } } ] } ] " + extra + @"
            } ] }";
            return SituationCatalogLoader.Load(json);
        }

        [Fact]
        public void Loader_UnknownPickTraitName_ReportsAndIgnoresThatItem_AndKeepsLoading()
        {
            var catalog = LoadWithRoles(@"{ ""a"": { ""pickTraits"": { ""valor"": 0.5, ""charm"": 1 } }, ""b"": {} }");

            Assert.Contains(catalog.Issues, i => i.Code == SituationIssueCode.UnknownPickTrait && !i.IsError);
            var t = catalog.ById("t1");
            Assert.NotNull(t);
            Assert.Equal(new[] { "valor" }, t!.Roles["a"].PickTraits!.Keys.ToArray());
        }

        [Fact]
        public void Loader_NonFiniteOrNonNumericCoefficient_ReportsAndIgnoresThatItem()
        {
            var catalog = LoadWithRoles(@"{ ""a"": { ""pickTraits"": { ""valor"": NaN, ""honor"": ""lots"", ""mercy"": 0.5 } }, ""b"": {} }");

            Assert.Equal(2, catalog.Issues.Count(i => i.Code == SituationIssueCode.InvalidPickTraitCoefficient && !i.IsError));
            var pt = catalog.ById("t1")!.Roles["a"].PickTraits!;
            Assert.Equal(new[] { "mercy" }, pt.Keys.ToArray());
        }

        [Fact]
        public void Loader_PickTraitsOnADerivedRole_ReportsAndIgnoresIt()
        {
            var catalog = LoadWithRoles(@"{ ""a"": {}, ""b"": {}, ""host"": { ""derived"": ""settlementOwnerClanLeader"", ""pickTraits"": { ""valor"": 0.5 } } }");

            Assert.Contains(catalog.Issues, i => i.Code == SituationIssueCode.PickTraitsOnWrongRole && i.Field == "roles.host.pickTraits");
            Assert.Null(catalog.ById("t1")!.Roles["host"].PickTraits);
        }

        [Fact]
        public void Loader_PickTraitsOnAPlayerOnlyRole_ReportsAndIgnoresIt()
        {
            var catalog = LoadWithRoles(@"{ ""a"": {}, ""b"": {}, ""employer"": { ""player"": true, ""pickTraits"": { ""valor"": 0.5 } } }");

            Assert.Contains(catalog.Issues, i => i.Code == SituationIssueCode.PickTraitsOnWrongRole && i.Field == "roles.employer.pickTraits");
            Assert.Null(catalog.ById("t1")!.Roles["employer"].PickTraits);
        }

        [Fact]
        public void Loader_PickTraitsIsAKnownRoleKey_NoUnknownPropertyWarning()
        {
            var catalog = LoadWithRoles(@"{ ""a"": { ""pickTraits"": { ""valor"": 0.5 } }, ""b"": {} }");
            Assert.DoesNotContain(catalog.Issues, i => i.Code == SituationIssueCode.UnknownProperty);
        }

        // ── 設定鍵 ────────────────────────────────────────────────────────────────

        [Fact]
        public void Config_LogSituationCandidates_DefaultsFalse_AndAnOldConfigWithoutTheKeyReadsAsFalse()
        {
            Assert.False(new VividWorldConfig().Debug.LogSituationCandidates);

            string oldJson = @"{ ""debug"": { ""verboseTickLog"": true, ""devRelationBoost"": 33 } }";
            var cfg = VividJson.Read<VividWorldConfig>(oldJson)!;
            Assert.False(cfg.Debug.LogSituationCandidates);
            Assert.True(cfg.Debug.VerboseTickLog);
            Assert.Equal(33, cfg.Debug.DevRelationBoost);
            Assert.Equal(64, cfg.Debug.LogFlushEveryLines);

            // 既有的設定合併只補缺的鍵：補進 false，其他既有值不動
            var existing = JObject.Parse(oldJson);
            var canonical = JObject.Parse(VividJson.Write(new VividWorldConfig()));
            var merged = ConfigMerge.AddMissingKeys(existing, canonical);
            Assert.Contains("debug.logSituationCandidates", merged.AddedPaths);
            Assert.False((bool)merged.Merged["debug"]!["logSituationCandidates"]!);
            Assert.True((bool)merged.Merged["debug"]!["verboseTickLog"]!);
            Assert.Equal(33, (int)merged.Merged["debug"]!["devRelationBoost"]!);
        }

        // ── 玩家在場與流浪者事件改用共用挑法 ────────────────────────────────────────

        private sealed class PassEvaluator : IPlayerPresenceEvaluator
        {
            public PlayerPresenceEvaluation Evaluate(SituationTemplate template, IReadOnlyList<SituationConditionDef> conditions,
                IReadOnlyDictionary<string, string> nonDerivedRoleHeroIds, bool forced)
            {
                return new PlayerPresenceEvaluation
                {
                    ConditionResults = Array.Empty<ConditionEvaluationResult>(),
                    BoundHeroIds = nonDerivedRoleHeroIds.ToDictionary(k => k.Key, k => (string?)k.Value)
                };
            }
        }

        private static SituationTemplate WandererDaily(string id, double weight, Dictionary<string, double>? challengerTraits)
        {
            var t = new SituationTemplate { Id = id, Trigger = "wandererDaily", Weight = weight, Decider = "challenger" };
            t.Roles["incumbent"] = new SituationRoleDef();
            t.Roles["challenger"] = new SituationRoleDef { PickTraits = challengerTraits };
            t.Roles["employer"] = new SituationRoleDef { PlayerOnly = true };
            t.Player = new SituationPlayerDef { Judge = true, JudgeRole = "employer" };
            t.Branches.Add(new SituationBranchDef { Id = "plain_keep", Base = 1.0 });
            t.Branches.Add(new SituationBranchDef { Id = "decide_swap", Base = 1.0, Judge = true });
            return t;
        }

        private static VividWorldConfig WandererConfig()
        {
            var cfg = new VividWorldConfig();
            cfg.Situations.WandererEvents.Enabled = true;
            cfg.Situations.WandererEvents.DailyChance = 1.0;
            cfg.Situations.WandererEvents.UnseenShare = 0.0;
            return cfg;
        }

        private static WandererRollInput WandererInput(long seed)
        {
            return new WandererRollInput
            {
                Trigger = WandererTrigger.Daily,
                Today = 20.0,
                AvailableWandererHeroIds = new[] { "w_1", "w_2", "w_3" },
                PlayerHeroId = "player_hero",
                CampaignSeed = seed
            };
        }

        [Fact]
        public void WandererRoller_PicksTheBoldChallengerMoreOften_AndKeepsAPickRecord()
        {
            var traits = new FakeHeroTraitLookup();
            traits.Set(Profile("w_1", valor: 2));
            traits.Set(Profile("w_2", valor: -2));
            traits.Set(Profile("w_3", valor: -2));
            var templates = new[] { WandererDaily("camp", 3.0, new Dictionary<string, double> { ["valor"] = 0.5 }) };

            int bold = 0;
            const int n = 600;
            for (int s = 0; s < n; s++)
            {
                var r = WandererEventRoller.Roll(WandererInput(s), WandererConfig(), templates, new PassEvaluator(), Rng, traits);
                Assert.Equal(WandererRollOutcome.Happened, r.Outcome);
                Assert.NotNull(r.PickRecord);
                Assert.Equal("camp", r.PickRecord!.PickedKindId);
                if (r.Pick!.Assignment.NonDerivedRoleHeroIds["challenger"] == "w_1") bold++;
            }

            // 比重 2.0 的 2 組、0.1（夾住）的 4 組：4 / 4.4 ≈ 0.909；平均抽是 1/3
            Assert.InRange(bold / (double)n, 0.86, 0.95);
        }

        [Fact]
        public void WandererRoller_LogSample_PickRecordIsPrintedWithTheSharedFormatter()
        {
            var traits = new FakeHeroTraitLookup();
            traits.Set(Profile("w_1", valor: 2));
            traits.Set(Profile("w_2", valor: -2));
            traits.Set(Profile("w_3", valor: 0));
            var templates = new[] { WandererDaily("wanderer_campfire_jab", 3.0, new Dictionary<string, double> { ["valor"] = 0.5 }) };

            var r = WandererEventRoller.Roll(WandererInput(3), WandererConfig(), templates, new PassEvaluator(), Rng, traits);

            Assert.NotNull(r.PickRecord);
            var names = new Dictionary<string, string> { ["w_1"] = "Bold wanderer", ["w_2"] = "Timid wanderer", ["w_3"] = "Plain wanderer", ["player_hero"] = "You" };
            var lines = SituationPickLogFormatter.Format(r.PickRecord!, new SituationPickLogOptions { NameOf = id => names.TryGetValue(id, out var n) ? n : null });
            foreach (var l in lines) _output.WriteLine(l);
            Assert.Contains(lines, l => l.StartsWith("Pick [wanderer event"));
            Assert.Contains(lines, l => l.Contains("valor"));
        }

        [Fact]
        public void WandererRoller_WithoutTraitLookup_PicksLikeTheUniformDraw()
        {
            var templates = new[] { WandererDaily("camp", 3.0, new Dictionary<string, double> { ["valor"] = 0.5 }) };
            var counts = new Dictionary<string, int>();
            const int n = 1200;
            for (int s = 0; s < n; s++)
            {
                var r = WandererEventRoller.Roll(WandererInput(s), WandererConfig(), templates, new PassEvaluator(), Rng);
                string c = r.Pick!.Assignment.NonDerivedRoleHeroIds["challenger"];
                counts[c] = counts.TryGetValue(c, out int v) ? v + 1 : 1;
            }

            Assert.Equal(3, counts.Count);
            foreach (var kv in counts) Assert.InRange(kv.Value / (double)n, 1.0 / 3 - 0.05, 1.0 / 3 + 0.05);
        }

        [Fact]
        public void WandererRoller_ListsTemplatesThatDidNotPassInTheRecordToo()
        {
            var failing = WandererDaily("never", 2.0, null);
            failing.Roles["extra"] = new SituationRoleDef(); // a third non-derived role makes the structure invalid
            var templates = new[] { failing, WandererDaily("camp", 1.0, null) };

            var r = WandererEventRoller.Roll(WandererInput(1), WandererConfig(), templates, new PassEvaluator(), Rng);

            Assert.Equal("camp", r.Pick!.Template.Id);
            var entries = r.PickRecord!.Rounds[0].Entries;
            Assert.Equal(2, entries.Count);
            Assert.Equal(PickKindStatus.NoCandidates, entries.Single(e => e.Id == "never").Status);
            Assert.Contains("needs exactly 2 wanderer roles", entries.Single(e => e.Id == "never").Reason);
        }
    }
}
