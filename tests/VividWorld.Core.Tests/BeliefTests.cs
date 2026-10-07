using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using VividWorld.Core.Catalog;
using VividWorld.Core.Channels;
using VividWorld.Core.Config;
using VividWorld.Core.Events;
using VividWorld.Core.Memory;
using VividWorld.Core.Persistence;
using VividWorld.Core.Rumors;
using VividWorld.Core.Tests.Fakes;
using VividWorld.Core.Util;
using Xunit;

namespace VividWorld.Core.Tests
{
    public class BeliefTests
    {
        private static readonly IDeterministicRng Rng = new SplitMix64Rng();

        private static BeliefInputs Input(
            int hop = 2, int? subjectAffection = null, int? tellerAffection = null,
            string? trait = null, int? traitLevel = null, double amount = -5.0,
            int calculating = 0, bool participant = false, string hearer = "h1")
        {
            return new BeliefInputs
            {
                CampaignSeed = 99,
                EventId = "evt_1",
                HearerId = hearer,
                SubjectId = "subject",
                TellerId = "teller",
                Hop = hop,
                HearerIsParticipant = participant,
                SubjectAffection = subjectAffection,
                TellerAffection = tellerAffection,
                Trait = trait,
                SubjectTraitLevel = traitLevel,
                OpinionAmount = amount,
                ListenerCalculating = calculating,
                Round = 0
            };
        }

        private static BeliefResult Judge(BeliefInputs input, BeliefConfig? cfg = null)
            => BeliefJudge.Judge(input, cfg ?? new BeliefConfig(), Rng);

        private sealed class ThrowingRng : IDeterministicRng
        {
            public double NextDouble(long seed) => throw new InvalidOperationException("rolled");
            public bool Chance(double p, long seed) => throw new InvalidOperationException("rolled");
            public int Pick(int count, long seed) => throw new InvalidOperationException("rolled");
            public int PickWeighted(IReadOnlyList<double> weights, long seed) => throw new InvalidOperationException("rolled");
        }

        private sealed class RecordingRng : IDeterministicRng
        {
            private readonly SplitMix64Rng _inner = new();
            public readonly List<double> ChanceCalls = new();
            public double NextDouble(long seed) => _inner.NextDouble(seed);
            public bool Chance(double p, long seed) { ChanceCalls.Add(p); return _inner.Chance(p, seed); }
            public int Pick(int count, long seed) => _inner.Pick(count, seed);
            public int PickWeighted(IReadOnlyList<double> weights, long seed) => _inner.PickWeighted(weights, seed);
        }

        // ── 各項加減 ──

        [Theory]
        [InlineData(30, 30.0)]    // ≥30：-40
        [InlineData(29, 50.0)]    // 10..29：-20
        [InlineData(10, 50.0)]
        [InlineData(9, 70.0)]     // 其餘：0
        [InlineData(-19, 70.0)]
        [InlineData(-20, 90.0)]   // ≤-20：+20
        public void SubjectRelation_Thresholds(int affection, double expectedChance)
        {
            var r = Judge(Input(subjectAffection: affection));
            Assert.Equal(expectedChance, r.Chance);
        }

        [Fact]
        public void SubjectRelation_Unknown_CountsAsZero()
        {
            var r = Judge(Input(subjectAffection: null));
            Assert.Equal(0, r.SubjectRelation);
            Assert.Equal(70.0, r.Chance);
        }

        [Theory]
        [InlineData(30, 85.0)]
        [InlineData(29, 70.0)]
        [InlineData(-19, 70.0)]
        [InlineData(-20, 45.0)]
        [InlineData(null, 70.0)]
        public void TellerRelation_Thresholds(int? affection, double expectedChance)
        {
            var r = Judge(Input(tellerAffection: affection));
            Assert.Equal(expectedChance, r.Chance);
        }

        [Theory]
        [InlineData(-5.0, -1, 90.0)]   // 壞事＋負：對得上 +20
        [InlineData(-5.0, 1, 40.0)]    // 壞事＋正：相反 -30
        [InlineData(5.0, 1, 90.0)]     // 好事＋正：對得上 +20
        [InlineData(5.0, -2, 40.0)]    // 好事＋負：相反 -30
        [InlineData(-5.0, 0, 70.0)]    // 那一項是 0
        public void TraitFit_FourCombinations(double amount, int level, double expectedChance)
        {
            var r = Judge(Input(trait: "honor", traitLevel: level, amount: amount));
            Assert.Equal(expectedChance, r.Chance);
        }

        [Fact]
        public void TraitFit_NoTraitOrSubjectUnknown_CountsAsZero()
        {
            Assert.Equal(0, Judge(Input(trait: null, traitLevel: null, amount: -5)).TraitFit);
            Assert.Equal(0, Judge(Input(trait: "honor", traitLevel: null, amount: -5)).TraitFit);
        }

        [Theory]
        [InlineData(2, 50.0)]
        [InlineData(1, 60.0)]
        [InlineData(0, 70.0)]
        [InlineData(-1, 80.0)]
        [InlineData(-2, 90.0)]
        public void ListenerNature_StepPerLevel(int calculating, double expectedChance)
        {
            var r = Judge(Input(calculating: calculating));
            Assert.Equal(expectedChance, r.Chance);
        }

        [Theory]
        [InlineData(1, 70.0)]
        [InlineData(2, 70.0)]
        [InlineData(3, 65.0)]
        [InlineData(4, 60.0)]
        [InlineData(5, 55.0)]
        [InlineData(9, 55.0)]   // 超過最後一格用最後一格
        public void Distance_UsesHopDeltas(int hop, double expectedChance)
        {
            var r = Judge(Input(hop: hop));
            Assert.Equal(expectedChance, r.Chance);
        }

        [Fact]
        public void Chance_ClampedToMinAndMax()
        {
            var high = new BeliefConfig { BaseChance = 100 };
            var rHigh = Judge(Input(subjectAffection: -50), high);   // 100 + 20 = 120
            Assert.Equal(120.0, rHigh.RawChance);
            Assert.Equal(95.0, rHigh.Chance);

            var low = new BeliefConfig { BaseChance = 0 };
            var rLow = Judge(Input(subjectAffection: 80), low);      // 0 - 40 = -40
            Assert.Equal(-40.0, rLow.RawChance);
            Assert.Equal(5.0, rLow.Chance);
        }

        // ── 一律信 ──

        [Fact]
        public void Participant_AlwaysBelieves_WithoutRolling()
        {
            var r = BeliefJudge.Judge(Input(hop: 3, participant: true), new BeliefConfig(), new ThrowingRng());
            Assert.True(r.Believes);
            Assert.False(r.Rolled);
            Assert.Equal(BeliefReason.Witness, r.Heaviest);
        }

        [Fact]
        public void HopZero_AlwaysBelieves_WithoutRolling()
        {
            var r = BeliefJudge.Judge(Input(hop: 0), new BeliefConfig(), new ThrowingRng());
            Assert.True(r.Believes);
            Assert.False(r.Rolled);
        }

        // ── 確定性 ──

        [Fact]
        public void SameInputs_SameResult()
        {
            var a = Judge(Input(hop: 3, subjectAffection: 5));
            var b = Judge(Input(hop: 3, subjectAffection: 5));
            Assert.Equal(a.Believes, b.Believes);
            Assert.Equal(a.Roll, b.Roll);
            Assert.Equal(a.Chance, b.Chance);
        }

        [Fact]
        public void DifferentHearers_GetDifferentSeeds()
        {
            var rolls = new HashSet<double>();
            for (int i = 0; i < 8; i++)
            {
                rolls.Add(Judge(Input(hearer: "hearer_" + i)).Roll);
            }
            Assert.True(rolls.Count > 1);
        }

        [Fact]
        public void Roll_AgreesWithBelievesAndChance()
        {
            for (int i = 0; i < 40; i++)
            {
                var r = Judge(Input(hearer: "x" + i));
                Assert.Equal(r.Roll < r.Chance, r.Believes);
            }
        }

        // ── 最重的一項 ──

        [Fact]
        public void Heaviest_PicksLargestAbsolute()
        {
            // 信了只在正向項目裡挑最大：告訴他的人 +15、像不像 +20 -> TraitFit
            var r = Judge(Input(subjectAffection: 40, tellerAffection: 40, trait: "honor", traitLevel: -1, amount: -5));
            Assert.Equal(BeliefReason.TraitFit, r.Heaviest);
        }

        [Fact]
        public void Heaviest_NegativeAndPositiveCompareByAbsolute()
        {
            // 信了：同方向（正向）的項目只有像不像 +20 -> TraitFit
            var r = Judge(Input(hop: 3, tellerAffection: -30, trait: "honor", traitLevel: -1, amount: -5));
            Assert.Equal(BeliefReason.TraitFit, r.Heaviest);
        }

        [Fact]
        public void Heaviest_TieTakesTheEarlierReason()
        {
            var cfg = new BeliefConfig { TellerTrustedDelta = 20, FitsTraitDelta = 20 };
            var r = Judge(Input(tellerAffection: 50, trait: "honor", traitLevel: -1, amount: -5), cfg);
            Assert.Equal(BeliefReason.TellerRelation, r.Heaviest);

            var cfg2 = new BeliefConfig { ListenerCalculatingStep = 5, HopDeltas = new double[] { 0, 0, 0, 5 } };
            var r2 = Judge(Input(hop: 3, calculating: 1), cfg2);
            Assert.Equal(BeliefReason.ListenerNature, r2.Heaviest);
        }

        [Fact]
        public void Heaviest_AllZero_IsNone()
        {
            var r = Judge(Input());
            Assert.Equal(BeliefReason.None, r.Heaviest);
        }

        // ── 知情記錄 ──

        [Fact]
        public void KnownByEntry_OldJsonWithoutBeliefFields_RoundTripsUnchanged()
        {
            const string oldJson = "{\"heroId\":\"h1\",\"hop\":2,\"learnedDay\":10.5,\"sourceHeroId\":\"h0\"}";
            var entry = VividJson.Read<KnownByEntry>(oldJson);
            Assert.NotNull(entry);
            Assert.Null(entry!.Believes);

            string written = VividJson.Write(entry);
            Assert.True(JToken.DeepEquals(JObject.Parse(oldJson), JObject.Parse(written)));
            Assert.DoesNotContain("believes", written);
            Assert.DoesNotContain("belief", written);
        }

        [Fact]
        public void KnownByEntry_BeliefFields_RoundTrip()
        {
            var entry = new KnownByEntry
            {
                HeroId = "h1",
                Hop = 3,
                Believes = false,
                BeliefDay = 12.25,
                BeliefChance = 45.0,
                BeliefReason = "TellerRelation",
                BeliefRound = 0
            };
            var back = VividJson.Read<KnownByEntry>(VividJson.Write(entry));
            Assert.NotNull(back);
            Assert.False(back!.Believes);
            Assert.Equal(12.25, back.BeliefDay);
            Assert.Equal(45.0, back.BeliefChance);
            Assert.Equal("TellerRelation", back.BeliefReason);
            Assert.Equal(0, back.BeliefRound);
        }

        // ── 模板的 trait／receiver ──

        private static string TemplateJson(string opinionEntry) => @"[
  {
    ""type"": ""sample_event"",
    ""origin"": ""public"",
    ""dramaWeight"": 3,
    ""roles"": { ""instigator"": ""{MASTERMIND}"", ""target"": ""{TARGET}"" },
    ""knowingRoles"": [""instigator""],
    ""opinion"": [ " + opinionEntry + @" ],
    ""facts"": [
      { ""id"": ""fact_who"", ""category"": ""WHO"", ""textId"": ""VividWorld_Fact_Who"",
        ""text"": ""{INSTIGATOR} confronted {TARGET}"",
        ""vars"": { ""INSTIGATOR"": ""hero:{MASTERMIND}"", ""TARGET"": ""hero:{TARGET}"" }, ""fragility"": 2 }
    ]
  }
]";

        private static readonly PersistenceConfig PersistenceCfg = new() { MaxFactsPerEvent = 24 };

        [Fact]
        public void Loader_ValidTraitAndReceiver_AreStored_CaseInsensitive()
        {
            var catalog = EventCatalogLoader.Load(
                TemplateJson("{ \"about\": \"instigator\", \"amount\": -5, \"trait\": \"Honor\", \"receiver\": \"target\" }"), PersistenceCfg);
            Assert.Empty(catalog.Issues.Where(i => i.IsError));
            var def = catalog.ByType("sample_event")!.Opinions![0];
            Assert.Equal("honor", def.Trait);
            Assert.Equal("target", def.Receiver);
            Assert.Empty(def.Extra);
            Assert.Empty(catalog.Issues);
        }

        [Fact]
        public void Loader_TraitAndReceiverOmitted_AreNull()
        {
            var catalog = EventCatalogLoader.Load(
                TemplateJson("{ \"about\": \"instigator\", \"amount\": -5 }"), PersistenceCfg);
            Assert.Empty(catalog.Issues);
            var def = catalog.ByType("sample_event")!.Opinions![0];
            Assert.Null(def.Trait);
            Assert.Null(def.Receiver);
        }

        [Fact]
        public void Loader_UnknownTrait_IsReportedAsOpinionIssue()
        {
            var catalog = EventCatalogLoader.Load(
                TemplateJson("{ \"about\": \"instigator\", \"amount\": -5, \"trait\": \"charm\" }"), PersistenceCfg);
            Assert.Contains(catalog.Issues, i => i.IsError && i.Code == CatalogIssueCode.OpinionInvalid && i.Field == "opinion[0].trait");
        }

        [Fact]
        public void Loader_ReceiverNotARole_IsReportedAsOpinionIssue()
        {
            var catalog = EventCatalogLoader.Load(
                TemplateJson("{ \"about\": \"instigator\", \"amount\": -5, \"receiver\": \"stranger\" }"), PersistenceCfg);
            Assert.Contains(catalog.Issues, i => i.IsError && i.Code == CatalogIssueCode.OpinionInvalid && i.Field == "opinion[0].receiver");
        }

        // ── 出貨的兩份模板檔 ──

        private static string ReadShipped(string fileName)
        {
            string? current = AppContext.BaseDirectory;
            while (!string.IsNullOrEmpty(current))
            {
                if (File.Exists(Path.Combine(current, "VividWorld.sln")))
                {
                    return File.ReadAllText(Path.Combine(current, "module", "ModuleData", fileName));
                }
                current = Directory.GetParent(current)?.FullName;
            }
            throw new FileNotFoundException(fileName);
        }

        // 型別 -> (about, trait, receiver)；receiver 為 null 表示不寫
        private static readonly (string Type, string About, string Trait, string? Receiver)[] ShippedTable =
        {
            ("hero_murdered", "killer", "honor", "victim"),
            ("hero_executed", "killer", "mercy", "victim"),
            ("seat_dispute_demanded", "slighted", "calculating", "favored"),
            ("seat_dispute_endured", "slighted", "calculating", null),
            ("seat_dispute_yielded", "slighted", "generosity", "favored"),
            ("seat_dispute_walked_out", "slighted", "calculating", "host"),
            ("advice_given_freely", "veteran", "generosity", "student"),
            ("advice_brushed_off", "veteran", "generosity", "student"),
            ("advice_mocked", "veteran", "mercy", "student"),
            ("tavern_boast_told", "speaker", "valor", null),
            ("tavern_boast_told", "speaker", "honor", null),
            ("tavern_sour_words", "speaker", "mercy", "listener"),
            ("tavern_good_word", "speaker", "generosity", "listener"),
            ("victory_credit_claimed", "claimant", "honor", "rival"),
            ("victory_credit_deferred", "claimant", "generosity", "rival"),
            ("victory_credit_judged", "host", "honor", "rival"),
            ("brawl_man_handed_over", "patron", "honor", "aggrieved"),
            ("brawl_shielded_own", "patron", "honor", "aggrieved"),
            ("brawl_counter_accused", "patron", "honor", "aggrieved"),
            ("brawl_hushed_up", "aggrieved", "honor", null),
            ("brawl_hushed_up", "patron", "honor", null),
            ("wager_refused", "challenged", "valor", "challenger"),
            ("conduct_spoke_against_ruler", "speaker", "honor", "ruler"),
            ("conduct_mistreated_prisoner", "captor", "mercy", "prisoner"),
            ("conduct_refused_aid", "refuser", "generosity", "asker"),
            ("conduct_rash_capture", "prisoner", "calculating", null),
            ("conduct_poisoned", "poisoner", "honor", "victim"),
        };

        [Fact]
        public void ShippedTemplates_CarryTheDeclaredTraitAndReceiver()
        {
            var events = EventCatalogLoader.Load(ReadShipped("vividworld_events.json"), PersistenceCfg);
            var situations = EventCatalogLoader.Load(ReadShipped("vividworld_situation_events.json"), PersistenceCfg);
            Assert.Empty(events.Issues.Where(i => i.IsError && i.Code == CatalogIssueCode.OpinionInvalid));
            Assert.Empty(situations.Issues.Where(i => i.IsError && i.Code == CatalogIssueCode.OpinionInvalid));

            foreach (var row in ShippedTable)
            {
                var template = events.ByType(row.Type) ?? situations.ByType(row.Type);
                Assert.True(template != null, "missing template " + row.Type);
                var def = template!.Opinions!.SingleOrDefault(o => o.About == row.About && o.Trait == row.Trait);
                Assert.True(def != null, row.Type + " has no opinion about " + row.About + " with trait " + row.Trait);
                Assert.Equal(row.Trait, def!.Trait);
                Assert.Equal(row.Receiver, def.Receiver);
            }
        }

        [Fact]
        public void ShippedTemplates_EveryOpinionTemplateInTheTableIsCovered()
        {
            // 表之外只允許停用的兩個（沒標 trait）
            var events = EventCatalogLoader.Load(ReadShipped("vividworld_events.json"), PersistenceCfg);
            var situations = EventCatalogLoader.Load(ReadShipped("vividworld_situation_events.json"), PersistenceCfg);
            var tableTypes = new HashSet<string>(ShippedTable.Select(r => r.Type));
            var untagged = events.Templates.Concat(situations.Templates)
                .Where(t => t.Opinions != null && t.Opinions.Count > 0 && !tableTypes.Contains(t.Type))
                .Select(t => t.Type)
                .OrderBy(x => x)
                .ToList();
            Assert.Equal(new[] { "tavern_confidence", "victory_credit_belittled" }, untagged);
        }

        // ── 不信的人少傳 ──

        private double FirstChanceWith(bool? believes)
        {
            var cfg = new VividWorldConfig();
            cfg.Propagation.BaseTellChancePerContact = 0.1;   // 乘完仍小於 1，才看得出倍數
            var rng = new RecordingRng();
            var channel = new FakePropagationChannel();
            var traits = new FakeHeroTraitLookup();
            traits.Set(new TraitProfile { HeroId = "teller", IsAlive = true, IsLord = true });
            traits.Set(new TraitProfile { HeroId = "contact", IsAlive = true, IsLord = true });
            channel.AddLink("teller", "contact", ChannelKind.SameParty);
            var retention = FactRetentionPolicies.Create(cfg, rng, 7L);
            var engine = new RumorEngine(cfg, retention, NullEmbellishmentPolicy.Instance, channel, traits, rng, 7L, "player");

            var evt = new WorldEvent
            {
                EventId = "e1",
                Type = "test_event",
                DramaWeight = 3,
                Day = 10.0,
                Origin = EventOrigin.Public,
                Facts = new List<Fact> { new Fact { Id = "f1", Text = "x", TextId = "VividWorld_Fact_f1" } },
                KnownBy = new List<KnownByEntry> { new KnownByEntry { HeroId = "teller", Hop = 1, Believes = believes } }
            };
            engine.PropagateFromTeller(evt, "teller", 10.0, 8);
            Assert.NotEmpty(rng.ChanceCalls);
            return rng.ChanceCalls[0];
        }

        [Fact]
        public void DisbelievingTeller_TellsWithTheConfiguredMultiplier()
        {
            double baseline = FirstChanceWith(null);
            double disbelieves = FirstChanceWith(false);
            Assert.True(baseline > 0 && baseline < 1);
            Assert.Equal(baseline * 0.3, disbelieves, 9);
        }

        [Fact]
        public void BelievingOrUnjudgedTeller_IsNotSlowedDown()
        {
            Assert.Equal(FirstChanceWith(null), FirstChanceWith(true), 9);
        }

        [Fact]
        public void TellerLog_MentionsTheMultiplierOnlyWhenItApplies()
        {
            Assert.Contains("x0.3", BeliefLogFormatter.FormatTellerMultiplier(0.3));
        }

        // ── 設定 ──

        [Fact]
        public void Config_Defaults_MatchTheCard()
        {
            var cfg = new VividWorldConfig();
            Assert.True(cfg.FalseRumors.BeliefEnabled);
            var b = cfg.FalseRumors.Belief;
            Assert.Equal(70, b.BaseChance);
            Assert.Equal(30, b.SubjectRelationFriend);
            Assert.Equal(-40, b.SubjectFriendDelta);
            Assert.Equal(10, b.SubjectRelationWarm);
            Assert.Equal(-20, b.SubjectWarmDelta);
            Assert.Equal(-20, b.SubjectRelationHostile);
            Assert.Equal(20, b.SubjectHostileDelta);
            Assert.Equal(30, b.TellerRelationTrusted);
            Assert.Equal(15, b.TellerTrustedDelta);
            Assert.Equal(-20, b.TellerRelationDistrusted);
            Assert.Equal(-25, b.TellerDistrustedDelta);
            Assert.Equal(20, b.FitsTraitDelta);
            Assert.Equal(-30, b.ContradictsTraitDelta);
            Assert.Equal(-10, b.ListenerCalculatingStep);
            Assert.Equal(new double[] { 0, 0, 0, -5, -10, -15 }, b.HopDeltas);
            Assert.Equal(5, b.MinChance);
            Assert.Equal(95, b.MaxChance);
            Assert.Equal(0.3, b.DisbelieverTellMultiplier);
        }

        [Fact]
        public void Config_MergeIntoOldFile_AddsNewKeysAndKeepsExistingValues()
        {
            var existing = JObject.Parse("{ \"configVersion\": 1, \"propagation\": { \"baseTellChancePerContact\": 0.9 } }");
            var canonical = JObject.Parse(VividJson.Write(new VividWorldConfig().Normalize()));

            var result = ConfigMerge.AddMissingKeys(existing, canonical);

            Assert.Contains("falseRumors.beliefEnabled", result.AddedPaths);
            Assert.Contains("falseRumors.belief.baseChance", result.AddedPaths);
            Assert.Contains("falseRumors.belief.disbelieverTellMultiplier", result.AddedPaths);
            Assert.Contains("falseRumors.belief.hopDeltas", result.AddedPaths);
            Assert.Equal(70, (double)result.Merged.SelectToken("falseRumors.belief.baseChance")!);
            Assert.Equal(0.9, (double)result.Merged.SelectToken("propagation.baseTellChancePerContact")!);
        }

        [Fact]
        public void Config_MergeKeepsAnEditedBeliefValue()
        {
            var existing = JObject.Parse("{ \"falseRumors\": { \"belief\": { \"baseChance\": 55 } } }");
            var canonical = JObject.Parse(VividJson.Write(new VividWorldConfig().Normalize()));

            var result = ConfigMerge.AddMissingKeys(existing, canonical);

            Assert.Equal(55, (double)result.Merged.SelectToken("falseRumors.belief.baseChance")!);
            Assert.DoesNotContain("falseRumors.belief.baseChance", result.AddedPaths);
            Assert.Contains("falseRumors.belief.minChance", result.AddedPaths);
        }

        [Fact]
        public void Config_Normalize_ClampsChancesAndMultiplier()
        {
            var cfg = new VividWorldConfig();
            cfg.FalseRumors.Belief.BaseChance = 250;
            cfg.FalseRumors.Belief.MinChance = -10;
            cfg.FalseRumors.Belief.MaxChance = 400;
            cfg.FalseRumors.Belief.DisbelieverTellMultiplier = 1.7;
            var notices = new List<ClampNotice>();
            cfg.Normalize(notices);

            Assert.Equal(100, cfg.FalseRumors.Belief.BaseChance);
            Assert.Equal(0, cfg.FalseRumors.Belief.MinChance);
            Assert.Equal(100, cfg.FalseRumors.Belief.MaxChance);
            Assert.Equal(1.0, cfg.FalseRumors.Belief.DisbelieverTellMultiplier);
            Assert.Contains(notices, n => n.Key == "falseRumors.belief.baseChance");
            Assert.Contains(notices, n => n.Key == "falseRumors.belief.disbelieverTellMultiplier");
        }

        [Fact]
        public void Config_Normalize_RestoresAnEmptyHopDeltaList()
        {
            var cfg = new VividWorldConfig();
            cfg.FalseRumors.Belief.HopDeltas = new double[0];
            cfg.Normalize();
            Assert.Equal(new double[] { 0, 0, 0, -5, -10, -15 }, cfg.FalseRumors.Belief.HopDeltas);
        }

        [Fact]
        public void Config_UnknownKeysUnderFalseRumorsAreKept()
        {
            var cfg = VividJson.Read<VividWorldConfig>("{ \"falseRumors\": { \"futureKey\": 5, \"belief\": { \"futureKey2\": 6 } } }");
            Assert.NotNull(cfg);
            Assert.True(cfg!.FalseRumors.Extra.ContainsKey("futureKey"));
            Assert.True(cfg.FalseRumors.Belief.Extra.ContainsKey("futureKey2"));
        }
    }
}
