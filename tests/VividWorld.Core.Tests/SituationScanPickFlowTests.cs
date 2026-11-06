using System;
using System.Collections.Generic;
using System.Linq;
using VividWorld.Core.Rumors;
using VividWorld.Core.Situations;
using VividWorld.Core.Util;
using Xunit;
using Xunit.Abstractions;

namespace VividWorld.Core.Tests
{
    /// <summary>
    /// 城裡每天掃描的呼叫順序（主要六種、背地說君主壞話與拒絕援手、編的話三種）接著用同一份名單，
    /// 以及編的話重新求值不成立時同一個名額換一種重挑。Module 那側照同一個順序叫共用挑法。
    /// </summary>
    public class SituationScanPickFlowTests
    {
        private readonly ITestOutputHelper _output;
        private static readonly SplitMix64Rng Rng = new SplitMix64Rng();

        public SituationScanPickFlowTests(ITestOutputHelper output)
        {
            _output = output;
        }

        private static SituationPickCandidate Cand(string settlement, params (string Role, string Hero)[] roles)
        {
            return new SituationPickCandidate
            {
                SettlementId = settlement,
                RoleHeroIds = roles.ToDictionary(r => r.Role, r => (string?)r.Hero, StringComparer.OrdinalIgnoreCase)
            };
        }

        private static SituationPickKind Kind(string id, double weight, string source, params SituationPickCandidate[] candidates)
        {
            return new SituationPickKind { Id = id, Weight = weight, WeightSource = source, Candidates = candidates };
        }

        private static SituationPickResult PickSlot(
            IReadOnlyList<SituationPickKind> kinds,
            SituationRoster roster,
            string site,
            int slot,
            int totalSlots,
            long seed,
            int attempt = 0)
        {
            return SituationPicker.Pick(new SituationPickRequest
            {
                Kinds = kinds,
                Count = 1,
                FirstSlot = slot,
                TotalSlots = totalSlots,
                Roster = roster,
                Rng = Rng,
                KindSeed = s => RumorSeed.Of(seed, site, "kind", s, attempt),
                PersonSeed = (s, k) => RumorSeed.Of(seed, site, "person", s, attempt),
                Site = site,
                Day = 12.4
            });
        }

        private static IEnumerable<string> HeroesOf(SituationPickResult r) =>
            r.Picks.SelectMany(p => p.Candidate.HeroIds());

        [Fact]
        public void SevenB_AndSevenC_ContinueWithTheRosterTheMainSixLeftBehind()
        {
            // 主要六種：每個人都只出現在一組裡以外，另外安排 hero_x 同時出現在 7b、7c 的候選裡
            var main = new[]
            {
                Kind("tavern_words", 3.0, "template weight", Cand("town_a", ("speaker", "hero_x"), ("listener", "hero_1")))
            };
            var sevenB = Kind("rebuke_lord", 1.0, "template weight",
                Cand("town_a", ("a", "hero_x"), ("b", "hero_2")),
                Cand("town_a", ("a", "hero_3"), ("b", "hero_4")));
            var sevenC = new[]
            {
                Kind("madeup_slander", 1.0, "share", Cand("town_a", ("a", "hero_1"), ("b", "hero_5"))),
                Kind("madeup_praise", 1.0, "share", Cand("town_a", ("a", "hero_6"), ("b", "hero_7")))
            };

            for (long seed = 0; seed < 200; seed++)
            {
                var roster = new SituationRoster();

                var m = PickSlot(main, roster, "main", 0, 1, seed);
                Assert.Single(m.Picks);
                roster.AddExecuted(m.Picks[0].Candidate.HeroIds(), "tavern_words ev1");

                var b = PickSlot(new[] { sevenB }, roster, "7b", 0, 1, seed);
                Assert.Single(b.Picks);
                // hero_x 在名單上，7b 只剩 hero_3 / hero_4 那一組
                Assert.Equal("hero_3", b.Picks[0].Candidate.RoleHeroIds["a"]);
                Assert.Equal(1, b.Records[0].ExcludedByRoster);
                roster.AddExecuted(b.Picks[0].Candidate.HeroIds(), "rebuke_lord ev2");

                var c = PickSlot(sevenC, roster, "7c", 0, 1, seed);
                Assert.Single(c.Picks);
                // hero_1 在名單上：madeup_slander 全被排掉，只剩 madeup_praise
                Assert.Equal("madeup_praise", c.Picks[0].Kind.Id);

                var everyone = HeroesOf(m).Concat(HeroesOf(b)).Concat(HeroesOf(c)).ToList();
                Assert.Equal(everyone.Count, everyone.Distinct().Count());
            }
        }

        [Fact]
        public void SevenC_PickedKindFullyExcludedByRoster_PicksAnotherKindInTheSameSlot()
        {
            var roster = new SituationRoster();
            roster.AddExecuted(new string?[] { "hero_1", "hero_5" }, "tavern_words ev1");

            int enumeratedSlander = 0;
            var slander = new SituationPickKind
            {
                Id = "madeup_slander",
                Weight = 100.0,
                WeightSource = "share",
                CandidateProvider = () =>
                {
                    enumeratedSlander++;
                    return new[] { Cand("town_a", ("a", "hero_1"), ("b", "hero_9")) };
                }
            };
            var praise = Kind("madeup_praise", 1.0, "share", Cand("town_a", ("a", "hero_6"), ("b", "hero_7")));

            var r = PickSlot(new[] { slander, praise }, roster, "7c", 0, 1, 7);

            Assert.Single(r.Picks);
            Assert.Equal("madeup_praise", r.Picks[0].Kind.Id);
            Assert.Equal(1, enumeratedSlander);
            Assert.True(r.Records[0].Rounds.Count >= 2);
            Assert.True(r.Records[0].Rounds[0].EnumeratedPickedKind);
            Assert.Equal(0, r.Records[0].Rounds[0].EnumeratedRemaining);
        }

        [Fact]
        public void SevenC_ReevaluationFails_SameSlotRepicksWithTheFailedKindMarkedAsNoCandidate()
        {
            var roster = new SituationRoster();
            var slander = Kind("madeup_slander", 100.0, "share", Cand("town_a", ("a", "hero_1"), ("b", "hero_2")));
            var praise = Kind("madeup_praise", 1.0, "share", Cand("town_a", ("a", "hero_6"), ("b", "hero_7")));

            var first = PickSlot(new[] { slander, praise }, roster, "7c", 0, 1, 3, attempt: 0);
            Assert.Equal("madeup_slander", first.Picks[0].Kind.Id);

            // 重新求值不成立：那一種換成沒有候選、寫下原因，名單沒有動
            Assert.Equal(0, roster.Count);
            var failedSlander = new SituationPickKind
            {
                Id = slander.Id,
                Weight = slander.Weight,
                WeightSource = slander.WeightSource,
                Candidates = new List<SituationPickCandidate>(),
                NoCandidateReason = "conditions no longer passed"
            };

            var second = PickSlot(new[] { failedSlander, praise }, roster, "7c", 0, 1, 3, attempt: 1);
            Assert.Equal("madeup_praise", second.Picks[0].Kind.Id);

            string text = string.Join("\n", SituationPickLogFormatter.Format(second.Records[0]));
            Assert.Contains("conditions no longer passed", text);
            // 失敗那一件的人沒進名單，所以還能被別件挑到
            Assert.False(roster.Contains("hero_1"));
        }

        [Fact]
        public void OneCallPerSlot_TheHeroesOfASkippedPickStayAvailableForTheNextSlot()
        {
            var roster = new SituationRoster();
            var kind = Kind("tavern_words", 1.0, "template weight",
                Cand("town_a", ("a", "hero_1"), ("b", "hero_2")),
                Cand("town_a", ("a", "hero_1"), ("b", "hero_3")));

            var slot0 = PickSlot(new[] { kind }, roster, "main", 0, 2, 11);
            Assert.Single(slot0.Picks);
            // 第 0 件重新求值不成立 ⇒ 呼叫端不把人放進名單
            var slot1 = PickSlot(new[] { kind }, roster, "main", 1, 2, 11);
            Assert.Single(slot1.Picks);
            Assert.Equal(2, slot1.Records[0].Person!.Remaining);
            Assert.Equal(1, slot1.Records[0].Slot);
            Assert.Equal(2, slot1.Records[0].SlotCount);
        }

        [Fact]
        public void LogSample_CityDay()
        {
            var roster = new SituationRoster();
            var traitsA = new TraitProfile { HeroId = "lord_a", Calculating = 2 };
            var student1 = Cand("town_a", ("student", "lord_a"), ("teacher", "lord_b"));
            student1.RoleTraits = new Dictionary<string, TraitProfile?> { ["student"] = traitsA };
            var student2 = Cand("town_b", ("student", "lord_c"), ("teacher", "lord_d"));
            student2.RoleTraits = new Dictionary<string, TraitProfile?> { ["student"] = new TraitProfile { HeroId = "lord_c", Calculating = -2 } };
            var advice = new SituationPickKind
            {
                Id = "advice_sought",
                Weight = 2.0,
                WeightSource = "template weight",
                Candidates = new[] { student1, student2 },
                PickTraitRoles = new Dictionary<string, IReadOnlyDictionary<string, double>>
                {
                    ["student"] = new Dictionary<string, double> { ["calculating"] = 0.5 }
                }
            };
            var tavern = Kind("tavern_words", 3.0, "template weight", Cand("town_a", ("speaker", "lord_a"), ("listener", "lord_e")), Cand("town_c", ("speaker", "lord_f"), ("listener", "lord_g")));
            var brawl = Kind("market_brawl", 1.0, "template weight");
            var main = new[] { advice, brawl, tavern };

            var names = new Dictionary<string, string> { ["lord_a"] = "Deshert", ["lord_b"] = "Caradog", ["lord_c"] = "Pol", ["lord_d"] = "Ergun", ["lord_e"] = "Boyar", ["lord_f"] = "Ulfar", ["lord_g"] = "Dreng" };
            var options = new SituationPickLogOptions { AllCandidates = true, NameOf = id => names.TryGetValue(id, out var n) ? n : null };

            var lines = new List<string>();
            for (int slot = 0; slot < 2; slot++)
            {
                var r = PickSlot(main, roster, "city scan, main", slot, 2, 5);
                lines.AddRange(SituationPickLogFormatter.Format(r.Records[0], options));
                if (r.Picks.Count == 0) break;
                roster.AddExecuted(r.Picks[0].Candidate.HeroIds(), r.Picks[0].Kind.Id + " ev" + (slot + 1));
            }

            var summaries = new List<SituationPickKindSummary>
            {
                new SituationPickKindSummary { Id = "advice_sought", Weight = 2.0, Qualified = 2 },
                new SituationPickKindSummary { Id = "market_brawl", Weight = 1.0, Qualified = 0 },
                new SituationPickKindSummary { Id = "tavern_words", Weight = 3.0, Qualified = 2 },
                new SituationPickKindSummary { Id = "madeup_slander", Weight = 1.0, Qualified = -1 }
            };
            lines.Add("SituationScan: " + SituationPickLogFormatter.FormatQualifiedCounts(summaries, roster.Count));

            foreach (var l in lines) _output.WriteLine(l);
            Assert.Contains(lines, l => l.StartsWith("Pick [city scan, main]"));
            Assert.Contains(lines, l => l.Contains("qualified per kind"));
        }
    }
}
