using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using VividWorld.Core.Catalog;
using VividWorld.Core.Channels;
using VividWorld.Core.Config;
using VividWorld.Core.Dialogue;
using VividWorld.Core.Diagnostics;
using VividWorld.Core.Events;
using VividWorld.Core.Ingest;
using VividWorld.Core.Memory;
using VividWorld.Core.Persistence;
using VividWorld.Core.Rumors;
using VividWorld.Core.Tests.Fakes;
using VividWorld.Core.Util;
using Xunit;

namespace VividWorld.Core.Tests
{
    /// <summary>
    /// 份量 1..10（基礎分＋身分或門第加成）與五段傳播：兩個數怎麼換算、每一格的份量、舊資料 ×2、
    /// 同一段的最遠手數／選題權重／記憶天數跟份量只有 1..5 的時代一模一樣（數字是改之前寫死的）。
    /// </summary>
    public class DramaWeightTests
    {
        // 改之前的預設值（段 1..5）：最遠手數、選題權重、記憶天數（baseDays 60、dramaReference 4、興趣 1.0、沒重聽）。講給別人的倍率沒有對外的讀取口，改用「同一段、同一組種子，誰聽到了一模一樣」那條測試對
        private static readonly int[] OldMaxHopByBand = { 2, 3, 4, 5, 6 };
        private static readonly double[] OldTopicWeightByBand = { 0.35, 0.6, 1.0, 1.5, 2.2 };
        private static readonly double[] OldMemoryDaysByBand = { 15.0, 30.0, 45.0, 60.0, 75.0 };

        private static string RepoRoot()
        {
            string current = AppContext.BaseDirectory;
            while (!string.IsNullOrEmpty(current))
            {
                if (Directory.Exists(Path.Combine(current, "module", "ModuleData"))) return current;
                var parent = Directory.GetParent(current);
                if (parent == null) break;
                current = parent.FullName;
            }
            throw new FileNotFoundException("Repository root could not be located from " + AppContext.BaseDirectory);
        }

        private static EventCatalog LoadShipped(string fileName)
        {
            string path = Path.Combine(RepoRoot(), "module", "ModuleData", fileName);
            return EventCatalogLoader.Load(File.ReadAllText(path), new PersistenceConfig { MaxFactsPerEvent = 24 });
        }

        private static (RumorEngine engine, FakePropagationChannel channel, FakeHeroTraitLookup traits, VividWorldConfig cfg) CreateEngine(double baseTellChance = 1.0)
        {
            var cfg = new VividWorldConfig();
            cfg.Propagation.BaseTellChancePerContact = baseTellChance;
            var rng = new SplitMix64Rng();
            var channel = new FakePropagationChannel();
            var traits = new FakeHeroTraitLookup();
            var retention = FactRetentionPolicies.Create(cfg, rng, 42L);
            var engine = new RumorEngine(cfg, retention, NullEmbellishmentPolicy.Instance, channel, traits, rng, 42L, "player");
            return (engine, channel, traits, cfg);
        }

        private static WorldEvent NewEvent(string id, int value, int scale)
        {
            return new WorldEvent
            {
                EventId = id,
                Type = "test_event",
                DramaWeight = value,
                DramaScale = scale,
                Day = 10.0,
                Origin = EventOrigin.Public,
                Facts = new List<Fact> { new Fact { Id = "f1", Text = "Something happened", TextId = "VividWorld_Fact_f1" } },
                KnownBy = new List<KnownByEntry>()
            };
        }

        // ───────────────────────── 兩個數的換算 ─────────────────────────

        [Theory]
        [InlineData(1, 1)]
        [InlineData(2, 1)]
        [InlineData(3, 2)]
        [InlineData(4, 2)]
        [InlineData(5, 3)]
        [InlineData(6, 3)]
        [InlineData(7, 4)]
        [InlineData(8, 4)]
        [InlineData(9, 5)]
        [InlineData(10, 5)]
        public void BandOfWeight_EveryTwoLevelsShareOneBand(int weight, int expectedBand)
        {
            Assert.Equal(expectedBand, DramaScales.BandOfWeight(weight));
            Assert.Equal(expectedBand, DramaScales.ToBand(weight, DramaScales.Ten));
        }

        [Theory]
        [InlineData(0, 1)]
        [InlineData(-4, 1)]
        [InlineData(11, 5)]
        [InlineData(99, 5)]
        public void BandOfWeight_OutOfRangeIsClampedFirst(int weight, int expectedBand)
        {
            Assert.Equal(expectedBand, DramaScales.BandOfWeight(weight));
        }

        [Theory]
        [InlineData(1, 2)]
        [InlineData(2, 4)]
        [InlineData(3, 6)]
        [InlineData(4, 8)]
        [InlineData(5, 10)]
        public void LegacyValues_AreReadAsBand_AndWeightIsBandTimesTwo(int stored, int expectedWeight)
        {
            Assert.Equal(stored, DramaScales.ToBand(stored, DramaScales.Legacy));
            Assert.Equal(expectedWeight, DramaScales.ToWeight(stored, DramaScales.Legacy));
        }

        [Fact]
        public void UnknownScale_IsTreatedAsLegacy()
        {
            Assert.Equal(3, DramaScales.ToBand(3, 0));
            Assert.Equal(6, DramaScales.ToWeight(3, 7));
            Assert.False(DramaScales.IsKnownScale(7));
            Assert.False(DramaScales.IsValid(3, 7));
        }

        [Theory]
        [InlineData(1, 5, true)]
        [InlineData(5, 5, true)]
        [InlineData(6, 5, false)]
        [InlineData(0, 5, false)]
        [InlineData(1, 10, true)]
        [InlineData(10, 10, true)]
        [InlineData(11, 10, false)]
        [InlineData(0, 10, false)]
        public void IsValid_DependsOnTheScale(int value, int scale, bool expected)
        {
            Assert.Equal(expected, DramaScales.IsValid(value, scale));
        }

        [Fact]
        public void Describe_PrintsBothNumbers()
        {
            Assert.Equal("weight 6/10 (band 3)", DramaScales.Describe(6, DramaScales.Ten));
            Assert.Equal("weight 5/10 (band 3)", DramaScales.Describe(5, DramaScales.Ten));
            Assert.Equal("weight 6/10 (band 3)", DramaScales.Describe(3, DramaScales.Legacy));
        }

        [Fact]
        public void ResolveWeight_SubmittedWinsThenTypeDefaultThenDefault_ConfigValuesAreOldBands()
        {
            Assert.Equal(8, DramaScales.ResolveWeight(8, DramaScales.Ten, 2, 3, out var s1));
            Assert.Contains("1..10", s1);

            Assert.Equal(8, DramaScales.ResolveWeight(4, DramaScales.Legacy, 2, 3, out var s2));
            Assert.Contains("old 1..5", s2);

            Assert.Equal(4, DramaScales.ResolveWeight(null, DramaScales.Legacy, 2, 3, out var s3));
            Assert.Contains("type default", s3);

            Assert.Equal(6, DramaScales.ResolveWeight(null, DramaScales.Legacy, null, 3, out var s4));
            Assert.Contains("config default", s4);
        }

        // ───────────────────────── 份量怎麼算：兩張表每一格 ─────────────────────────

        private static ProminenceFacts PersonFacts(ProminenceTier tier)
        {
            return tier switch
            {
                ProminenceTier.Ruler => new ProminenceFacts { HeroId = "h", IsKingdomLeader = true, KingdomId = "k", IsClanLeader = true, ClanId = "c", IsLord = true },
                ProminenceTier.ClanLeader => new ProminenceFacts { HeroId = "h", IsClanLeader = true, ClanId = "c", IsLord = true },
                ProminenceTier.NobleMember => new ProminenceFacts { HeroId = "h", ClanId = "c", IsLord = true },
                _ => new ProminenceFacts { HeroId = "h", ClanId = "c", ClanIsMinorFaction = true, IsLord = true }
            };
        }

        public static IEnumerable<object[]> PersonTable()
        {
            // 事件型別, 基礎分, 國王, 族長, 一般家族成員, 小勢力或沒家族
            yield return new object[] { "hero_executed", 8, 10, 10, 8, 6 };
            yield return new object[] { "hero_murdered", 8, 10, 10, 8, 6 };
            yield return new object[] { "hero_died_in_battle", 6, 10, 8, 6, 4 };
            yield return new object[] { "hero_captured_by_bandits", 5, 9, 7, 5, 3 };
            yield return new object[] { "hero_died_naturally", 4, 8, 6, 4, 2 };
            yield return new object[] { "hero_died_of_old_age", 4, 8, 6, 4, 2 };
            yield return new object[] { "hero_died_in_labor", 4, 8, 6, 4, 2 };
            yield return new object[] { "hero_taken_prisoner", 3, 7, 5, 3, 1 };
            yield return new object[] { "hero_rescued_from_bandits", 3, 7, 5, 3, 1 };
            yield return new object[] { "hero_escaped_bandits", 3, 7, 5, 3, 1 };
            yield return new object[] { "hero_released", 2, 6, 4, 2, 1 };
            yield return new object[] { "hero_escaped_captivity", 2, 6, 4, 2, 1 };
        }

        [Theory]
        [MemberData(nameof(PersonTable))]
        public void PersonEvents_ShippedBaseScorePlusStandingBonus_MatchesTheTable(
            string type, int expectedBase, int ruler, int clanLeader, int member, int minor)
        {
            var catalog = LoadShipped("vividworld_events.json");
            var template = catalog.ByType(type);
            Assert.NotNull(template);
            Assert.Equal(DramaScales.Ten, template!.DramaScale);
            Assert.Equal(expectedBase, template.DramaWeightTen);

            var cfg = new VividWorldConfig().Events;
            var expected = new Dictionary<ProminenceTier, int>
            {
                [ProminenceTier.Ruler] = ruler,
                [ProminenceTier.ClanLeader] = clanLeader,
                [ProminenceTier.NobleMember] = member,
                [ProminenceTier.Minor] = minor
            };
            foreach (var kv in expected)
            {
                var person = PrisonerProminence.Classify(PersonFacts(kv.Key), cfg.WeightBonusByProminence);
                Assert.Equal(kv.Key, person.Tier);
                var computed = DramaWeightCalculator.Compute(template.DramaWeightTen!.Value, person.Bonus);
                Assert.Equal(kv.Value, computed.Weight);
                Assert.Equal((kv.Value + 1) / 2, computed.Band);
            }
        }

        [Fact]
        public void Weight_IsClampedToOneAndTen_AndTheLogSaysSo()
        {
            var high = DramaWeightCalculator.Compute(8, 4);
            Assert.Equal(10, high.Weight);
            Assert.Equal(5, high.Band);
            Assert.Contains("clamped from 12", high.Describe());

            var low = DramaWeightCalculator.Compute(2, -5);
            Assert.Equal(1, low.Weight);
            Assert.Equal(1, low.Band);
            Assert.Contains("clamped from -3", low.Describe());

            var inside = DramaWeightCalculator.Compute(8, 0);
            Assert.Equal("weight 8/10 (band 4) = base 8 bonus +0", inside.Describe());

            Assert.Equal(10, DramaWeightCalculator.Compute(10, 9).Weight);
            Assert.Equal(1, DramaWeightCalculator.Compute(1, -9).Weight);
        }

        private static ClanFacts ClanOf(string id, int tier, bool minorFaction = false, bool ruling = false)
        {
            return new ClanFacts { ClanId = id, Tier = tier, IsMinorFaction = minorFaction, IsRuling = ruling, KingdomId = ruling ? "kingdom_a" : null };
        }

        [Fact]
        public void ClanStanding_RoyalHighOrdinaryMinor_AndTheBoundaries()
        {
            var cfg = new VividWorldConfig().Events;

            Assert.Equal(ClanStanding.Royal, DramaWeightCalculator.ClassifyClan(ClanOf("c1", 6, ruling: true), cfg).Standing);
            Assert.Equal(ClanStanding.High, DramaWeightCalculator.ClassifyClan(ClanOf("c2", 5), cfg).Standing);
            Assert.Equal(ClanStanding.High, DramaWeightCalculator.ClassifyClan(ClanOf("c2", 6), cfg).Standing);
            Assert.Equal(ClanStanding.Ordinary, DramaWeightCalculator.ClassifyClan(ClanOf("c3", 4), cfg).Standing);
            Assert.Equal(ClanStanding.Minor, DramaWeightCalculator.ClassifyClan(ClanOf("c4", 5, minorFaction: true), cfg).Standing);
            Assert.Equal(ClanStanding.Minor, DramaWeightCalculator.ClassifyClan(new ClanFacts(), cfg).Standing);

            // 王族的判定優先於小勢力旗標；沒有家族一律是小勢力
            Assert.Equal(ClanStanding.Royal, DramaWeightCalculator.ClassifyClan(ClanOf("c5", 1, minorFaction: true, ruling: true), cfg).Standing);

            // 高等家族的門檻可以調
            cfg.HighClanMinTier = 4;
            Assert.Equal(ClanStanding.High, DramaWeightCalculator.ClassifyClan(ClanOf("c3", 4), cfg).Standing);
            cfg.HighClanMinTier = 6;
            Assert.Equal(ClanStanding.Ordinary, DramaWeightCalculator.ClassifyClan(ClanOf("c2", 5), cfg).Standing);
        }

        [Fact]
        public void ClanStanding_LogCarriesTheValuesTheJudgementUsed()
        {
            var cfg = new VividWorldConfig().Events;
            var res = DramaWeightCalculator.ClassifyClan(ClanOf("clan_x", 5), cfg);
            Assert.Equal("clan=clan_x, tier=5, isMinorFaction=no, isRuling=no, kingdom=none, highTierFrom=5", res.Facts);
            Assert.Equal("clan clan_x tier 5 >= 5", res.Reason);

            var none = DramaWeightCalculator.ClassifyClan(new ClanFacts(), cfg);
            Assert.Equal("no clan", none.Reason);
            Assert.Contains("clan=none", none.Facts);
        }

        private static FamilyParty Party(string role, ProminenceTier tier, ClanFacts clan, EventsConfig cfg)
        {
            var person = PrisonerProminence.Classify(PersonFacts(tier), cfg.WeightBonusByProminence);
            return new FamilyParty(role, role + "_hero", person, DramaWeightCalculator.ClassifyClan(clan, cfg));
        }

        [Fact]
        public void FamilyEvents_ShippedBaseScorePlusClanBonus_MatchesTheTable()
        {
            var cfg = new VividWorldConfig().Events;
            var catalog = LoadShipped("vividworld_events.json");
            var married = catalog.ByType("heroes_married")!;
            var born = catalog.ByType("child_born")!;
            Assert.Equal(4, married.DramaWeightTen);
            Assert.Equal(2, born.DramaWeightTen);

            var clans = new[]
            {
                (ClanOf("royal", 6, ruling: true), 8, 6),
                (ClanOf("high", 5), 6, 4),
                (ClanOf("ordinary", 3), 4, 2),
                (ClanOf("minor", 2, minorFaction: true), 2, 1)   // 2 + (-2) = 0，夾到 1
            };
            foreach (var (clan, marriageWeight, birthWeight) in clans)
            {
                // 兩位當事人都是一般家族成員，只看門第
                var marriage = DramaWeightCalculator.ComputeForFamily(married.DramaWeightTen!.Value,
                    new[] { Party("a", ProminenceTier.NobleMember, clan, cfg), Party("b", ProminenceTier.NobleMember, clan, cfg) });
                Assert.Equal(marriageWeight, marriage.Computation.Weight);

                var birth = DramaWeightCalculator.ComputeForFamily(born.DramaWeightTen!.Value,
                    new[] { Party("mother", ProminenceTier.NobleMember, clan, cfg), Party("father", ProminenceTier.NobleMember, clan, cfg) });
                Assert.Equal(birthWeight, birth.Computation.Weight);
                Assert.Equal((birthWeight + 1) / 2, birth.Computation.Band);
            }
        }

        [Fact]
        public void Marriage_TakesTheHigherOfTheTwoFamilies_AndLogsBothSides()
        {
            var cfg = new VividWorldConfig().Events;
            var ordinary = Party("spouse_a", ProminenceTier.NobleMember, ClanOf("ordinary", 3), cfg);
            var royal = Party("spouse_b", ProminenceTier.NobleMember, ClanOf("royal", 6, ruling: true), cfg);

            var result = DramaWeightCalculator.ComputeForFamily(4, new[] { ordinary, royal });

            Assert.Equal(1, result.ChosenIndex);
            Assert.Equal(8, result.Computation.Weight);
            var lines = result.DescribeLines().ToList();
            Assert.Equal(3, lines.Count);
            Assert.Contains("spouse_a spouse_a_hero", lines[0]);
            Assert.Contains("clan ordinary", lines[0]);
            Assert.Contains("clan royal", lines[1]);
            Assert.StartsWith("higher side: spouse_b spouse_b_hero -> weight 8/10 (band 4)", lines[2]);

            // 同分取排在前面的一方
            var tie = DramaWeightCalculator.ComputeForFamily(4, new[] { Party("a", ProminenceTier.NobleMember, ClanOf("o1", 3), cfg), Party("b", ProminenceTier.NobleMember, ClanOf("o2", 3), cfg) });
            Assert.Equal(0, tie.ChosenIndex);
        }

        [Fact]
        public void Birth_TakesTheHigherOfTheParents()
        {
            var cfg = new VividWorldConfig().Events;
            var mother = Party("mother", ProminenceTier.NobleMember, ClanOf("minor", 2, minorFaction: true), cfg);
            var father = Party("father", ProminenceTier.NobleMember, ClanOf("high", 5), cfg);

            var result = DramaWeightCalculator.ComputeForFamily(2, new[] { mother, father });

            Assert.Equal(1, result.ChosenIndex);
            Assert.Equal(4, result.Computation.Weight);
            Assert.Equal(2, result.Computation.Band);
        }

        [Fact]
        public void FamilyParty_RulerOrClanLeaderTakesTheHigherOfPersonAndClan()
        {
            var cfg = new VividWorldConfig().Events;

            // 族長（+2）出身一般家族（0）⇒ 取本人的 +2
            var leaderOfOrdinary = Party("a", ProminenceTier.ClanLeader, ClanOf("ordinary", 3), cfg);
            Assert.Equal(2, leaderOfOrdinary.EffectiveBonus);
            Assert.Equal("person clanLeader", leaderOfOrdinary.EffectiveSource);

            // 國王（+4）的家族若沒被判成王族（例如讀不到王國）⇒ 仍取本人的 +4
            var rulerWithUnknownClan = Party("b", ProminenceTier.Ruler, ClanOf("ordinary", 3), cfg);
            Assert.Equal(4, rulerWithUnknownClan.EffectiveBonus);
            Assert.Equal("person ruler", rulerWithUnknownClan.EffectiveSource);

            // 族長（+2）的家族是王族（+4）⇒ 取門第的 +4
            var leaderOfRoyal = Party("c", ProminenceTier.ClanLeader, ClanOf("royal", 6, ruling: true), cfg);
            Assert.Equal(4, leaderOfRoyal.EffectiveBonus);
            Assert.Equal("clan royal", leaderOfRoyal.EffectiveSource);

            // 一般成員本人的加成不算：小勢力家族的成員只看門第（-2），不會因為本人是貴族被抬高
            var memberOfMinor = Party("d", ProminenceTier.NobleMember, ClanOf("minor", 2, minorFaction: true), cfg);
            Assert.Equal(-2, memberOfMinor.EffectiveBonus);

            // 族長本人 +2 比小勢力門第 -2 高 ⇒ 取 +2
            var leaderOfMinor = Party("e", ProminenceTier.ClanLeader, ClanOf("minor", 2, minorFaction: true), cfg);
            Assert.Equal(2, leaderOfMinor.EffectiveBonus);

            var married = DramaWeightCalculator.ComputeForFamily(4, new[] { leaderOfOrdinary, memberOfMinor });
            Assert.Equal(6, married.Computation.Weight);
        }

        [Fact]
        public void EmptyFamilyParties_FallBackToBonusZero()
        {
            var res = DramaWeightCalculator.ComputeForFamily(4, Array.Empty<FamilyParty>());
            Assert.Equal(4, res.Computation.Weight);
            Assert.Equal(-1, res.ChosenIndex);
            Assert.Empty(res.DescribeLines());
        }

        // ───────────────────────── 情境的結局不看地位 ─────────────────────────

        [Fact]
        public void SituationOutcomes_HaveTheirOwnWeightsOnTheTenLevelScale()
        {
            var catalog = LoadShipped("vividworld_situation_events.json");
            var expected = new Dictionary<string, int>
            {
                ["brawl_counter_accused"] = 6, ["seat_dispute_walked_out"] = 6,
                ["seat_dispute_demanded"] = 5, ["victory_credit_claimed"] = 5, ["victory_credit_judged"] = 5, ["advice_mocked"] = 5, ["brawl_shielded_own"] = 5,
                ["brawl_man_handed_over"] = 4, ["tavern_sour_words"] = 4, ["brawl_hushed_up"] = 4, ["wager_secret_stake"] = 4,
                ["seat_dispute_endured"] = 3, ["seat_dispute_yielded"] = 3, ["victory_credit_deferred"] = 3, ["advice_given_freely"] = 3, ["tavern_good_word"] = 3, ["wager_struck"] = 3,
                ["advice_brushed_off"] = 2, ["wager_refused"] = 2,
                ["tavern_boast_told"] = 1,
                // 停用的兩種：照原本的段（3 與 3）換算 × 2；原本寫的是 2 與 3
                ["conduct_poisoned"] = 8,
                ["conduct_spoke_against_ruler"] = 6,
                ["conduct_mistreated_prisoner"] = 5,
                ["conduct_refused_aid"] = 4,
                ["conduct_rash_capture"] = 3,
                ["tavern_confidence"] = 4,
                ["victory_credit_belittled"] = 6,
                // 回應情境：直接繼承它回應的內容現有的權重
                ["talk_denied_spoke_against_ruler"] = 6,
                ["talk_denied_mistreated_prisoner"] = 5,
                ["talk_denied_refused_aid"] = 4,
                ["talk_denied_rash_capture"] = 3,
                ["talk_denied_poisoned"] = 8,
                ["talk_corrected_rash_capture_by_captor"] = 3,
                ["talk_corrected_rash_capture_by_bystander"] = 3,
                ["talk_corrected_mistreated_by_prisoner"] = 5,
                ["talk_corrected_mistreated_by_comrade"] = 5,
                ["talk_corrected_refused_aid_by_asker"] = 4,
                ["talk_not_so_victory_credit_deferred"] = 3,
                ["talk_not_so_advice_given_freely"] = 3,
                ["talk_not_so_brawl_man_handed_over"] = 4,
                ["talk_not_so_seat_dispute_yielded"] = 3,
                ["talk_not_so_tavern_good_word"] = 3
            };
            Assert.Equal(expected.Count, catalog.Templates.Count);
            foreach (var kv in expected)
            {
                var t = catalog.ByType(kv.Key);
                Assert.NotNull(t);
                Assert.Equal(DramaScales.Ten, t!.DramaScale);
                Assert.Equal(kv.Value, t.DramaWeight);
            }
        }

        [Fact]
        public void ShippedTemplates_AreAllWrittenOnTheTenLevelScale()
        {
            foreach (var file in new[] { "vividworld_events.json", "vividworld_situation_events.json", "vividworld_sample_events.json" })
            {
                var catalog = LoadShipped(file);
                Assert.Empty(catalog.Issues.Where(i => i.IsError));
                Assert.All(catalog.Templates, t => Assert.Equal(DramaScales.Ten, t.DramaScale));
                Assert.Empty(catalog.LegacyDramaScaleTypes);
            }
        }

        // ───────────────────────── 模板：新舊寫法 ─────────────────────────

        private static string TemplateJson(string extraFields)
        {
            return @"[
  {
    ""type"": ""sample_event"",
    ""origin"": ""public"",
    " + extraFields + @"
    ""roles"": { ""instigator"": ""{MASTERMIND}"" },
    ""facts"": [ { ""id"": ""fact_who"", ""category"": ""WHO"", ""textId"": ""VividWorld_Fact_Who"", ""text"": ""{INSTIGATOR} did it"",
                   ""vars"": { ""INSTIGATOR"": ""hero:{MASTERMIND}"" }, ""fragility"": 2 } ]
  }
]";
        }

        private static readonly PersistenceConfig Pc = new() { MaxFactsPerEvent = 24 };

        [Fact]
        public void Template_WithoutDramaScale_IsTheOldWriting_ReadAsBandAndWeightIsDoubled()
        {
            var catalog = EventCatalogLoader.Load(TemplateJson(@"""dramaWeight"": 4,"), Pc);

            var t = catalog.ByType("sample_event");
            Assert.NotNull(t);
            Assert.Equal(DramaScales.Legacy, t!.DramaScale);
            Assert.Equal(4, t.DramaWeight);
            Assert.Equal(8, t.DramaWeightTen);
            Assert.Equal(new[] { "sample_event" }, catalog.LegacyDramaScaleTypes);

            // 綁成提交單時帶著尺度，匯入口才知道要 ×2
            var sub = TemplateBinder.Bind(t, new Dictionary<string, string> { ["MASTERMIND"] = "hero_1" }, 5.0, null, out var issues);
            Assert.NotNull(sub);
            Assert.Empty(issues.Where(i => i.IsError));
            Assert.Equal(4, sub!.DramaWeight);
            Assert.Equal(DramaScales.Legacy, sub.DramaScale);
            Assert.Equal(8, DramaScales.ToWeight(sub.DramaWeight!.Value, sub.DramaScale));
        }

        [Fact]
        public void Template_WithDramaScaleTen_IsTheNewWriting()
        {
            var catalog = EventCatalogLoader.Load(TemplateJson(@"""dramaWeight"": 8, ""dramaScale"": 10,"), Pc);

            var t = catalog.ByType("sample_event");
            Assert.NotNull(t);
            Assert.Equal(DramaScales.Ten, t!.DramaScale);
            Assert.Equal(8, t.DramaWeight);
            Assert.Equal(8, t.DramaWeightTen);
            Assert.Empty(catalog.LegacyDramaScaleTypes);

            var sub = TemplateBinder.Bind(t, new Dictionary<string, string> { ["MASTERMIND"] = "hero_1" }, 5.0, null, out _);
            Assert.Equal(8, sub!.DramaWeight);
            Assert.Equal(DramaScales.Ten, sub.DramaScale);
        }

        [Fact]
        public void Template_OutOfRangeDependsOnTheScale()
        {
            // 新寫法：11 超過；舊寫法：6 超過；10 在新寫法裡合法
            var tooHigh = EventCatalogLoader.Load(TemplateJson(@"""dramaWeight"": 11, ""dramaScale"": 10,"), Pc);
            Assert.Empty(tooHigh.Templates);
            Assert.Contains(tooHigh.Issues, i => i.Code == CatalogIssueCode.DramaOutOfRange && i.Field == "dramaWeight" && i.Detail.Contains("1 and 10"));

            var legacyTooHigh = EventCatalogLoader.Load(TemplateJson(@"""dramaWeight"": 6,"), Pc);
            Assert.Empty(legacyTooHigh.Templates);
            Assert.Contains(legacyTooHigh.Issues, i => i.Code == CatalogIssueCode.DramaOutOfRange && i.Field == "dramaWeight" && i.Detail.Contains("1 and 5"));

            var tenOk = EventCatalogLoader.Load(TemplateJson(@"""dramaWeight"": 10, ""dramaScale"": 10,"), Pc);
            Assert.Single(tenOk.Templates);

            var badScale = EventCatalogLoader.Load(TemplateJson(@"""dramaWeight"": 3, ""dramaScale"": 7,"), Pc);
            Assert.Empty(badScale.Templates);
            Assert.Contains(badScale.Issues, i => i.Code == CatalogIssueCode.DramaOutOfRange && i.Field == "dramaScale");
        }

        [Fact]
        public void Template_WithoutAnyWeight_StaysNull()
        {
            var catalog = EventCatalogLoader.Load(TemplateJson(string.Empty), Pc);
            var t = catalog.ByType("sample_event");
            Assert.NotNull(t);
            Assert.Null(t!.DramaWeight);
            Assert.Null(t.DramaWeightTen);
            Assert.Empty(catalog.LegacyDramaScaleTypes);
        }

        // ───────────────────────── 提交單：舊參數當成段 ─────────────────────────

        private static EventSubmission ValidSubmission()
        {
            return new EventSubmission
            {
                Type = "duel",
                Day = 100.0,
                Origin = EventOrigin.Public,
                Participants = new Dictionary<string, string> { ["a"] = "hero_1", ["b"] = "hero_2" },
                Facts = new List<Fact>
                {
                    new Fact { Id = "f1", Category = FactCategory.What, TextId = "VividWorld_Fact_What", Text = "A duel took place", Fragility = 3 }
                }
            };
        }

        [Fact]
        public void Submission_DefaultsToTheOldScale()
        {
            Assert.Equal(DramaScales.Legacy, new EventSubmission().DramaScale);
        }

        [Theory]
        [InlineData(1, 5, true)]
        [InlineData(5, 5, true)]
        [InlineData(6, 5, false)]
        [InlineData(8, 10, true)]
        [InlineData(10, 10, true)]
        [InlineData(11, 10, false)]
        [InlineData(3, 7, false)]
        public void Validator_ChecksTheWeightAgainstItsScale(int value, int scale, bool valid)
        {
            var s = ValidSubmission();
            s.DramaWeight = value;
            s.DramaScale = scale;

            var outcome = EventSubmissionValidator.Validate(s, new PersistenceConfig(), _ => false);

            Assert.Equal(valid, outcome.IsValid);
        }

        [Fact]
        public void Validator_NoWeightGiven_IgnoresTheScale()
        {
            var s = ValidSubmission();
            s.DramaWeight = null;
            s.DramaScale = 7;
            Assert.True(EventSubmissionValidator.Validate(s, new PersistenceConfig(), _ => false).IsValid);
        }

        // ───────────────────────── 舊事件讀進來 ─────────────────────────

        private const string LegacyEventJson = @"{
  ""eventId"": ""evt_legacy"",
  ""type"": ""hero_taken_prisoner"",
  ""day"": 10.0,
  ""origin"": ""public"",
  ""dramaWeight"": 4,
  ""participants"": { ""prisoner"": ""lord_a"" },
  ""facts"": [ { ""id"": ""f1"", ""category"": ""WHAT"", ""text"": ""x"", ""fragility"": 2 } ],
  ""knownBy"": [ { ""heroId"": ""lord_b"", ""hop"": 0 } ]
}";

        [Fact]
        public void LegacyEventJson_ReadsAsBand_SameMaxHopAndMemoryDaysAsBeforeTheUpdate()
        {
            var evt = VividJson.Read<WorldEvent>(LegacyEventJson);

            Assert.NotNull(evt);
            Assert.Equal(DramaScales.Legacy, evt!.DramaScale);
            Assert.Equal(4, evt.DramaWeight);
            Assert.Equal(4, evt.DramaBand);
            Assert.Equal(8, evt.DramaWeightTen);

            var (engine, _, _, cfg) = CreateEngine();
            // 改之前：第 4 段最遠 5 手；記憶天數 60 × 1.0 × 4/4 = 60 天
            Assert.Equal(5, engine.MaxHopFor(evt));
            Assert.Equal(60.0, MemorySpan.Compute(0.0, 1.0, evt.DramaBand, 0, null, cfg.Memory).Days, 6);
        }

        [Fact]
        public void LegacyEvents_AllFiveBands_KeepTheirOldMaxHopTopicWeightAndMemoryDays()
        {
            for (int band = 1; band <= 5; band++)
            {
                var (engine, _, traits, cfg) = CreateEngine();
                traits.Set(new TraitProfile { HeroId = "teller", IsAlive = true, IsLord = true });
                var evt = NewEvent("e" + band, band, DramaScales.Legacy);
                evt.KnownBy.Add(new KnownByEntry { HeroId = "teller", Hop = 0 });

                Assert.Equal(band, evt.DramaBand);
                Assert.Equal(OldMaxHopByBand[band - 1], engine.MaxHopFor(evt));
                Assert.Equal(OldMemoryDaysByBand[band - 1], MemorySpan.Compute(0.0, 1.0, evt.DramaBand, 0, null, cfg.Memory).Days, 6);

                var choice = engine.ChooseTopic("teller", new[] { evt }, 10.0, 8);
                Assert.Single(choice.Candidates);
                Assert.Equal(OldTopicWeightByBand[band - 1], choice.Candidates[0].Weight, 3);
                Assert.Equal(band, choice.Candidates[0].Drama);
                Assert.Equal(band * 2, choice.Candidates[0].DramaWeight);
            }
        }

        [Fact]
        public void TenLevelEvents_EachWeightUsesTheOldValuesOfItsBand()
        {
            for (int weight = 1; weight <= 10; weight++)
            {
                int band = (weight + 1) / 2;
                var (engine, _, traits, cfg) = CreateEngine();
                traits.Set(new TraitProfile { HeroId = "teller", IsAlive = true, IsLord = true });
                var evt = NewEvent("w" + weight, weight, DramaScales.Ten);
                evt.KnownBy.Add(new KnownByEntry { HeroId = "teller", Hop = 0 });

                Assert.Equal(band, evt.DramaBand);
                Assert.Equal(weight, evt.DramaWeightTen);
                Assert.Equal(OldMaxHopByBand[band - 1], engine.MaxHopFor(evt));
                Assert.Equal(OldMemoryDaysByBand[band - 1], MemorySpan.Compute(0.0, 1.0, evt.DramaBand, 0, null, cfg.Memory).Days, 6);

                var choice = engine.ChooseTopic("teller", new[] { evt }, 10.0, 8);
                Assert.Equal(OldTopicWeightByBand[band - 1], choice.Candidates[0].Weight, 3);
                Assert.Equal(weight, choice.Candidates[0].DramaWeight);
            }
        }

        [Fact]
        public void InterestFloorForStrangers_ReadsTheBand()
        {
            // 陌生人的保底 otherByDrama = { 0.05, 0.05, 0.1, 0.2, 0.4 }：份量 9 與 10 都在第 5 段
            var cfg = new VividWorldConfig();
            var expectedByBand = new[] { 0.05, 0.05, 0.1, 0.2, 0.4 };
            for (int weight = 1; weight <= 10; weight++)
            {
                var evt = NewEvent("w", weight, DramaScales.Ten);
                var res = InterestCalculator.Compute(new InterestHeroFacts("stranger", "clan_s"), new List<InterestParticipant>(), evt.DramaBand, cfg.Memory);
                Assert.Equal(expectedByBand[evt.DramaBand - 1], res.Interest, 6);
            }
        }

        [Fact]
        public void SameBand_GivesTheSamePropagationOutcome_AsTheOldBandEvent()
        {
            // 同一個事件編號、同一組種子：舊的段 3、新的份量 5 與 6 都是第 3 段，誰聽到了誰要一模一樣
            var outcomes = new List<string>();
            foreach (var (value, scale) in new[] { (3, DramaScales.Legacy), (5, DramaScales.Ten), (6, DramaScales.Ten) })
            {
                var (engine, channel, traits, _) = CreateEngine(baseTellChance: 0.3);
                traits.Set(new TraitProfile { HeroId = "h0", IsAlive = true, IsLord = true });
                for (int i = 0; i < 12; i++)
                {
                    string id = "c" + i;
                    traits.Set(new TraitProfile { HeroId = id, IsAlive = true, IsLord = true });
                    channel.AddLink("h0", id, ChannelKind.SameParty);
                }
                var evt = NewEvent("evt_same_band", value, scale);
                evt.KnownBy.Add(new KnownByEntry { HeroId = "h0", Hop = 0 });

                var outcome = engine.PropagateOnce(evt, 10.0, 0);
                outcomes.Add(string.Join(",", outcome.NewKnowers.Select(k => k.HeroId)));
            }

            Assert.Equal(outcomes[0], outcomes[1]);
            Assert.Equal(outcomes[0], outcomes[2]);
        }

        [Fact]
        public void RetentionThreshold_ReadsTheBand()
        {
            // 保留幾段：份量 5 與 6（第 3 段）與舊的段 3 留下一樣的碎片
            var cfg = new VividWorldConfig();
            cfg.Retention.DramaThresholdShift = new[] { 4, 3, 2, 1, 0 };
            var rng = new SplitMix64Rng();
            var policy = FactRetentionPolicies.Create(cfg, rng, 42L);

            WorldEvent Make(int value, int scale)
            {
                var evt = NewEvent("evt_ret", value, scale);
                evt.Facts = Enumerable.Range(1, 5).Select(i => new Fact { Id = "f" + i, Text = "t" + i, TextId = "VividWorld_Fact_" + i, Fragility = i }).ToList();
                return evt;
            }

            string Kept(WorldEvent e) => string.Join(",", policy.Retain(e, 3, "teller").Select(f => f.Id));

            string legacy = Kept(Make(3, DramaScales.Legacy));
            Assert.Equal(legacy, Kept(Make(5, DramaScales.Ten)));
            Assert.Equal(legacy, Kept(Make(6, DramaScales.Ten)));
        }

        [Fact]
        public void NewEventJson_CarriesTheScaleField_AndTheOldFieldIsStillTheSame()
        {
            var evt = NewEvent("evt_new", 8, DramaScales.Ten);

            string json = VividJson.Write(evt);
            var obj = JObject.Parse(json);

            Assert.Equal(8, (int?)obj["dramaWeight"]);
            Assert.Equal(10, (int?)obj["dramaScale"]);

            var back = VividJson.Read<WorldEvent>(json)!;
            Assert.Equal(DramaScales.Ten, back.DramaScale);
            Assert.Equal(4, back.DramaBand);

            // 計算屬性不寫進磁碟
            Assert.Null(obj["dramaBand"]);
            Assert.Null(obj["dramaWeightTen"]);
        }

        [Fact]
        public void LegacyEventJson_WithoutTheScaleField_IsStillLegacyAfterAReadWriteReadRoundTrip()
        {
            var evt = VividJson.Read<WorldEvent>(LegacyEventJson)!;

            var again = VividJson.Read<WorldEvent>(VividJson.Write(evt))!;

            Assert.Equal(4, again.DramaWeight);
            Assert.Equal(4, again.DramaBand);
            Assert.Equal(8, again.DramaWeightTen);
        }

        [Fact]
        public void PlayerHeardEntry_CarriesTheScaleOfTheEvent()
        {
            Assert.Equal(DramaScales.Legacy, new PlayerHeardEntry().DramaScale);
        }

        // ───────────────────────── 分片：讀到舊事件印一行 ─────────────────────────

        private sealed class CapturingSink : ILogSink
        {
            public List<string> Lines { get; } = new();
            public void Info(string message) => Lines.Add(message);
            public void Warn(string message) => Lines.Add(message);
            public void Error(string message, Exception? ex = null) => Lines.Add(message);
        }

        [Fact]
        public void ShardStore_CountsLegacyEvents_AndLogsOnlyTheFirstTime()
        {
            var writer = new FailingFileWriter();
            var legacy = JObject.Parse(LegacyEventJson);
            var modern = JObject.Parse(VividJson.Write(NewEvent("evt_modern", 8, DramaScales.Ten)));
            writer.Files["events/d0000-0099.json"] = new JArray(legacy, modern).ToString();
            var legacy2 = JObject.Parse(LegacyEventJson);
            legacy2["eventId"] = "evt_legacy_2";
            legacy2["day"] = 150.0;
            writer.Files["events/d0100-0199.json"] = new JArray(legacy2).ToString();

            var sink = new CapturingSink();
            var store = new EventShardStore("events", 100, writer) { Log = sink };
            var index = store.LoadIndex();

            Assert.Equal(3, index.Count);
            Assert.Equal(2, store.LegacyScaleEventsRead);
            Assert.Single(sink.Lines);
            Assert.Contains("saved before the 1..10 weight scale", sink.Lines[0]);
            Assert.Equal(DramaScales.Legacy, store.Load("evt_legacy", index)!.DramaScale);
            Assert.Equal(DramaScales.Ten, store.Load("evt_modern", index)!.DramaScale);
        }

        // ───────────────────────── 預覽：還沒休眠的事件各級則數 ─────────────────────────

        [Fact]
        public void ListenPreview_WeightTally_PrintsEveryLevelAndTheBandMapping()
        {
            var counts = new[] { 3, 0, 5, 0, 1, 2, 0, 0, 0, 4 };

            string line = ListenPreviewLogFormatter.FormatWeightTally(counts);

            Assert.Equal(
                "[4b. Active events by weight (not dormant)] 1:3 2:0 3:5 4:0 5:1 6:2 7:0 8:0 9:0 10:4 (total 15; weight 1-2 = band 1, 3-4 = band 2, 5-6 = band 3, 7-8 = band 4, 9-10 = band 5)",
                line);
        }

        [Fact]
        public void ListenPreview_Summary_IncludesTheTallyOnlyWhenItWasTaken()
        {
            var result = new ListenPreviewResult { Day = 5 };
            Assert.DoesNotContain("Active events by weight", ListenPreviewLogFormatter.FormatSummary(result));

            result.WeightTallyAvailable = true;
            result.ActiveEventsByWeight[7] = 2;
            string summary = ListenPreviewLogFormatter.FormatSummary(result);
            Assert.Contains("[4b. Active events by weight (not dormant)] 1:0 2:0 3:0 4:0 5:0 6:0 7:0 8:2 9:0 10:0 (total 2;", summary);
        }

        [Fact]
        public void TopicCandidate_WithoutAWeight_DefaultsToBandTimesTwo()
        {
            var c = new TopicCandidate("e1", 3, 1.0, 1.0, 1.0);
            Assert.Equal(6, c.DramaWeight);
            Assert.Equal(7, new TopicCandidate("e1", 4, 1.0, 1.0, 1.0, 7).DramaWeight);
        }
    }
}
