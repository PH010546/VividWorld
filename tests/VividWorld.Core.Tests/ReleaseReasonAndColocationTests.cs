using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using VividWorld.Core.Catalog;
using VividWorld.Core.Channels;
using VividWorld.Core.Config;
using VividWorld.Core.Diagnostics;
using VividWorld.Core.Events;
using VividWorld.Core.Ingest;
using VividWorld.Core.Presentation;
using VividWorld.Core.Rumors;
using VividWorld.Core.Tests.Fakes;
using Xunit;

namespace VividWorld.Core.Tests
{
    /// <summary>
    /// 被領主釋放依原因換碎片、舊事件照舊講、同地者算聽來的、自然死亡分兩種。
    /// </summary>
    public class ReleaseReasonAndColocationTests
    {
        // ────────────── 放人原因的判斷 ──────────────

        [Theory]
        [InlineData(EndCaptivityDetail.Ransom, false, false, ReleaseReasons.Ransom)]
        [InlineData(EndCaptivityDetail.Ransom, true, true, ReleaseReasons.Ransom)]
        [InlineData(EndCaptivityDetail.ReleasedByCompensation, false, false, ReleaseReasons.Ransom)]
        [InlineData(EndCaptivityDetail.ReleasedAfterPeace, false, false, ReleaseReasons.Peace)]
        [InlineData(EndCaptivityDetail.ReleasedAfterBattle, false, false, ReleaseReasons.Defeated)]
        [InlineData(EndCaptivityDetail.ReleasedAfterBattle, true, false, ReleaseReasons.SettlementOwnerChanged)]
        [InlineData(EndCaptivityDetail.ReleasedByChoice, false, true, ReleaseReasons.PlayerChoice)]
        [InlineData(EndCaptivityDetail.ReleasedByChoice, true, true, ReleaseReasons.PlayerChoice)]
        [InlineData(EndCaptivityDetail.ReleasedByChoice, false, false, ReleaseReasons.Disbanded)]
        public void Classify_MapsDetailAndPartyToTheReason(EndCaptivityDetail detail, bool partyIsSettlement, bool actorIsPlayer, string expected)
        {
            Assert.Equal(expected, ReleaseReasons.Classify(detail, partyIsSettlement, actorIsPlayer));
        }

        [Theory]
        [InlineData(EndCaptivityDetail.ReleasedAfterEscape)]
        [InlineData(EndCaptivityDetail.Death)]
        public void Classify_EscapeAndDeath_HaveNoReleaseReason(EndCaptivityDetail detail)
        {
            Assert.Null(ReleaseReasons.Classify(detail, false, false));
        }

        [Fact]
        public void SegmentOf_KnowsEveryReasonAndNothingElse()
        {
            foreach (var reason in new[]
            {
                ReleaseReasons.Ransom, ReleaseReasons.Peace, ReleaseReasons.Defeated,
                ReleaseReasons.SettlementOwnerChanged, ReleaseReasons.PlayerChoice, ReleaseReasons.Disbanded
            })
            {
                Assert.False(string.IsNullOrEmpty(ReleaseReasons.SegmentOf(reason)));
            }
            Assert.Null(ReleaseReasons.SegmentOf(null));
            Assert.Null(ReleaseReasons.SegmentOf("something_else"));
        }

        // ────────────── 放人的碎片形狀 ──────────────

        private static EventTemplate ReleasedTemplate()
        {
            return LoadCatalog("vividworld_events.json").ByType("hero_released")!;
        }

        private static string Segments(EventTemplate t)
            => string.Join("_", t.Facts.Select(f => SentenceCombinationEnumerator.ExtractSegment(f.TextId ?? string.Empty)));

        [Theory]
        [InlineData(ReleaseReasons.Ransom, "Who_Where_Ransom")]
        [InlineData(ReleaseReasons.Peace, "Who_Where_Peace")]
        [InlineData(ReleaseReasons.Defeated, "WhoDefeated_Where_Defeated")]
        [InlineData(ReleaseReasons.PlayerChoice, "WhoPlayerChoice_Where_PlayerChoice")]
        [InlineData(ReleaseReasons.Disbanded, "WhoDisbanded_Where_Disbanded")]
        public void Released_KnownCaptor_UsesTheReasonSpecificFragments(string reason, string expectedSegments)
        {
            var shape = TemplateVariants.Released(ReleasedTemplate(), reason, captorKnown: true);

            Assert.Equal(expectedSegments, Segments(shape));
            Assert.True(shape.Roles.ContainsKey("captor"));
            Assert.True(shape.Facts.First(f => f.Category == FactCategory.Where).Optional);
        }

        [Fact]
        public void Released_PlayerChoice_PlayerIsNotATeller()
        {
            var shape = TemplateVariants.Released(ReleasedTemplate(), ReleaseReasons.PlayerChoice, captorKnown: true);

            Assert.NotNull(shape.SelfTell);
            Assert.True(shape.SelfTell!["captor"].IsNever);
        }

        [Fact]
        public void Released_SettlementOwnerChanged_NamesNoCaptorAndKeepsThePlace()
        {
            var shape = TemplateVariants.Released(ReleasedTemplate(), ReleaseReasons.SettlementOwnerChanged, captorKnown: true);

            Assert.Equal("WhoNoCaptor_Where_SettlementOwnerChanged", Segments(shape));
            Assert.False(shape.Roles.ContainsKey("captor"));
            Assert.True(shape.Roles.ContainsKey("prisoner"));
            // 換了主人的城一定有地點，不是選填
            Assert.False(shape.Facts.First(f => f.Category == FactCategory.Where).Optional);
            Assert.DoesNotContain(shape.Facts, f => f.Vars.ContainsKey("CAPTOR"));
        }

        [Fact]
        public void Released_UnknownCaptor_UsesTheNoCaptorFragmentsWithoutAReason()
        {
            var shape = TemplateVariants.Released(ReleasedTemplate(), ReleaseReasons.Ransom, captorKnown: false);

            Assert.Equal("WhoNoCaptor_Where", Segments(shape));
            Assert.False(shape.Roles.ContainsKey("captor"));
        }

        [Fact]
        public void ReleasedLegacy_HasTheFourOriginalFragmentsSoOldEventsKeepTheirSentences()
        {
            var shape = TemplateVariants.ReleasedLegacy(ReleasedTemplate(), captorKnown: true);

            Assert.Equal("Who_Where_What_Outcome", Segments(shape));
            var noCaptor = TemplateVariants.ReleasedLegacy(ReleasedTemplate(), captorKnown: false);
            Assert.Equal("WhoNoCaptor_Where_What_Outcome", Segments(noCaptor));
        }

        [Fact]
        public void AllShapes_ListsNineReleaseShapes_ThreeEscapeShapes_TwoRescueShapes_OtherTypesOnce()
        {
            var all = new List<EventTemplate>();
            all.AddRange(LoadCatalog("vividworld_events.json").Templates);
            all.AddRange(LoadCatalog("vividworld_situation_events.json").Templates);

            Assert.Equal(9, TemplateVariants.AllShapes(all.First(t => t.Type == "hero_released")).Count);
            Assert.Equal(3, TemplateVariants.AllShapes(all.First(t => t.Type == "hero_escaped_captivity")).Count);
            Assert.Equal(2, TemplateVariants.AllShapes(all.First(t => t.Type == "hero_rescued_from_bandits")).Count);
            Assert.Single(TemplateVariants.AllShapes(all.First(t => t.Type == "hero_murdered")));
        }

        [Fact]
        public void VariantFragments_TextMatchesTheEnglishTableVerbatim_AndEveryTableHasThem()
        {
            // 事件存進去的英文與字串表同一句：英文介面不查字串表，直接用存進事件的字
            string root = FindRepoRoot();
            var en = EnglishStringTable.LoadFromFile(Path.Combine(root, "module", "ModuleData", "Languages", "std_module_strings_xml.xml"));
            var cnt = EnglishStringTable.LoadFromFile(Path.Combine(root, "module", "ModuleData", "Languages", "CNt", "std_module_strings_xml.xml"));

            var shapes = new List<EventTemplate>();
            foreach (var t in LoadCatalog("vividworld_events.json").Templates)
            {
                foreach (var (_, shape) in TemplateVariants.AllShapes(t)) shapes.Add(shape);
            }

            foreach (var fact in shapes.SelectMany(s => s.Facts).GroupBy(f => f.TextId).Select(g => g.First()))
            {
                Assert.False(string.IsNullOrEmpty(fact.TextId));
                Assert.Equal(fact.Text, en.Get(fact.TextId));
                Assert.False(string.IsNullOrEmpty(cnt.Get(fact.TextId)), $"CNt is missing {fact.TextId}");
            }
        }

        // ────────────── 舊事件（碎片是舊的）照舊講 ──────────────

        private static Fact OldFact(string id, FactCategory cat, string textId, int fragility, Dictionary<string, string>? vars = null)
        {
            return new Fact
            {
                Id = id, Category = cat, TextId = textId, Text = textId, Fragility = fragility,
                Vars = vars ?? new Dictionary<string, string>()
            };
        }

        private static string RenderOld(WorldEvent evt, List<Fact> retained, string? speaker, string? source, out bool wholeSentence, bool chinese = false)
        {
            string root = FindRepoRoot();
            var table = EnglishStringTable.LoadFromFile(Path.Combine(root, "module", "ModuleData", "Languages",
                chinese ? "CNt" : string.Empty, "std_module_strings_xml.xml"));
            var cfg = new PresentationConfig { EncyclopediaLinksEnabled = false, FactSeparator = chinese ? "，" : ", ", SentenceEnd = chinese ? "。" : "." };
            var composed = RumorTextComposer.Compose(evt, retained, cfg, prefix: null, speakerHeroId: speaker, sourceHeroId: source);
            var result = RumorTextAssembler.Assemble(
                composed, cfg,
                (val, links) => val.Contains(':') ? val.Substring(val.IndexOf(':') + 1) : val,
                (id, fb) => fb == null ? (table.Get(id) ?? string.Empty) : table.Lookup(id, fb),
                (key, fb) => table.GetWithFallback(key, fb),
                onWarning: null, isFemale: id => false);
            wholeSentence = result.UsedWholeSentence;
            return result.PlainText;
        }

        [Fact]
        public void OldEscapeEvent_OldFragmentCombination_FallsBackToConcatenationWithoutBreaking()
        {
            // 舊存檔的逃脫：第 1 手只剩「誰、地點、經過」三塊（結果易忘度 4 被丟掉），現在的句型沒有這一組
            var evt = new WorldEvent
            {
                EventId = "evt_old_escape", Type = "hero_escaped_captivity", Origin = EventOrigin.Public,
                Participants = new Dictionary<string, string> { ["captor"] = "hero_captor", ["prisoner"] = "hero_prisoner" }
            };
            var facts = new List<Fact>
            {
                OldFact("who", FactCategory.Who, "VividWorld_Fact_HeroEscaped_Who", 1,
                    new Dictionary<string, string> { ["CAPTOR"] = "hero:hero_captor", ["PRISONER"] = "hero:hero_prisoner" }),
                OldFact("where", FactCategory.Where, "VividWorld_Fact_HeroEscaped_Where", 2,
                    new Dictionary<string, string> { ["SETTLEMENT"] = "settlement:Pravend" }),
                OldFact("what", FactCategory.What, "VividWorld_Fact_HeroEscaped_What", 3)
            };

            string text = RenderOld(evt, facts, "hero_listener", "hero_captor", out bool whole);

            Assert.False(whole);
            Assert.Contains("guards were few", text);
            Assert.Contains("Pravend", text);
        }

        [Fact]
        public void OldReleaseEvent_FourOriginalFragments_UsesTheOldEventSentence()
        {
            var evt = new WorldEvent
            {
                EventId = "evt_old_release", Type = "hero_released", Origin = EventOrigin.Public,
                Participants = new Dictionary<string, string> { ["captor"] = "hero_captor", ["prisoner"] = "hero_prisoner" }
            };
            var facts = new List<Fact>
            {
                OldFact("who", FactCategory.Who, "VividWorld_Fact_HeroReleased_Who", 1,
                    new Dictionary<string, string> { ["CAPTOR"] = "hero:hero_captor", ["PRISONER"] = "hero:hero_prisoner" }),
                OldFact("where", FactCategory.Where, "VividWorld_Fact_HeroReleased_Where", 2,
                    new Dictionary<string, string> { ["SETTLEMENT"] = "settlement:Pravend" }),
                OldFact("what", FactCategory.What, "VividWorld_Fact_HeroReleased_What", 3),
                OldFact("outcome", FactCategory.Outcome, "VividWorld_Fact_HeroReleased_Outcome", 4)
            };

            string onlooker = RenderOld(evt, facts, "hero_witness", null, out bool wholeOnlooker);
            Assert.True(wholeOnlooker);
            Assert.Equal("Hero_prisoner was released in Pravend.", onlooker);
            Assert.DoesNotContain("terms were settled", onlooker);

            string captor = RenderOld(evt, facts, "hero_captor", null, out bool wholeCaptor);
            Assert.True(wholeCaptor);
            Assert.StartsWith("I released hero_prisoner near Pravend.", captor);
        }

        [Fact]
        public void OldNaturalDeathEvent_FourOriginalFragments_StillReadsAndSpeaks()
        {
            // 舊戰役裡的自然死亡有「誰、地點、陪伴、致敬」四塊：這一種消息只給舊事件用，
            // 模板保留這四塊的形狀，所以舊事件照樣用不分死法的整句講
            var evt = new WorldEvent
            {
                EventId = "evt_old_death", Type = "hero_died_naturally", Origin = EventOrigin.Public,
                Participants = new Dictionary<string, string> { ["victim"] = "hero_victim" }
            };
            var facts = new List<Fact>
            {
                OldFact("who", FactCategory.Who, "VividWorld_Fact_HeroDiedNaturally_Who", 1,
                    new Dictionary<string, string> { ["VICTIM"] = "hero:hero_victim" }),
                OldFact("where", FactCategory.Where, "VividWorld_Fact_HeroDiedNaturally_Where", 2,
                    new Dictionary<string, string> { ["SETTLEMENT"] = "settlement:Pravend" }),
                OldFact("what", FactCategory.What, "VividWorld_Fact_HeroDiedNaturally_What", 3),
                OldFact("outcome", FactCategory.Outcome, "VividWorld_Fact_HeroDiedNaturally_Outcome", 4,
                    new Dictionary<string, string> { ["VICTIM"] = "hero:hero_victim" })
            };

            string en = RenderOld(evt, facts, "hero_witness", null, out bool wholeEn);
            string zh = RenderOld(evt, facts, "hero_witness", null, out bool wholeZh, chinese: true);

            Assert.True(wholeEn);
            Assert.True(wholeZh);
            Assert.Contains("Pravend", en);
            Assert.Contains("Pravend", zh);
            Assert.Contains("funeral", en);
            Assert.Contains("出殯", zh);
            Assert.DoesNotContain("surrounded by family and kin", en);
        }

        // ────────────── 自然死亡分兩種 ──────────────

        [Fact]
        public void NaturalDeathTemplates_AreRecognizedAsHavingNoKiller()
        {
            Assert.True(RealEventMapping.IsNaturalDeathTemplate("hero_died_of_old_age"));
            Assert.True(RealEventMapping.IsNaturalDeathTemplate("hero_died_in_labor"));
            Assert.True(RealEventMapping.IsNaturalDeathTemplate("hero_died_naturally"));
            Assert.False(RealEventMapping.IsNaturalDeathTemplate("hero_died_in_battle"));
            Assert.False(RealEventMapping.IsNaturalDeathTemplate(null));
        }

        [Fact]
        public void NaturalDeath_NewEventsNeverUseTheOldCombinedTemplate()
        {
            foreach (var detail in new[] { KillCharacterActionDetail.DiedOfOldAge, KillCharacterActionDetail.DiedInLabor })
            {
                Assert.NotEqual("hero_died_naturally", RealEventMapping.TemplateForKill(detail));
            }
        }

        // ────────────── 同地者算聽來的 ──────────────

        private sealed class CapturingSink : ILogSink
        {
            public List<string> Lines { get; } = new();
            public void Info(string message) => Lines.Add(message);
            public void Warn(string message) => Lines.Add(message);
            public void Error(string message, Exception? ex = null) => Lines.Add(message);
        }

        private static (WorldEvent Evt, CapturingSink Log) SeedWithWitness(bool colocatedAsHearsay)
        {
            var channel = new FakePropagationChannel();
            var traits = new FakeHeroTraitLookup();
            traits.Set(new TraitProfile { HeroId = "witness_lord", IsAlive = true, IsPrisoner = false, IsLord = true });
            channel.AddWitness("hero_mother", "witness_lord");

            var submission = new EventSubmission
            {
                Type = "child_born",
                Day = 100.0,
                Origin = EventOrigin.Public,
                AutoResolveWitnesses = true,
                ColocatedWitnessAsHearsay = colocatedAsHearsay,
                Participants = new Dictionary<string, string> { ["mother"] = "hero_mother" }
            };
            var evt = new WorldEvent
            {
                EventId = "evt_0100_birth",
                Type = "child_born",
                Origin = EventOrigin.Public,
                Participants = new Dictionary<string, string>(submission.Participants)
            };
            var log = new CapturingSink();
            Hop0Seeding.Seed(evt, submission, channel, traits, new PropagationConfig(), "player", 100.0, log);
            return (evt, log);
        }

        [Fact]
        public void Seed_ColocatedWitnessAsHearsay_WitnessIsHopOneWithoutSourceAndLogged()
        {
            var (evt, log) = SeedWithWitness(colocatedAsHearsay: true);

            var mother = evt.KnownBy.Single(k => k.HeroId == "hero_mother");
            var witness = evt.KnownBy.Single(k => k.HeroId == "witness_lord");
            Assert.Equal(0, mother.Hop);
            Assert.Equal(1, witness.Hop);
            Assert.Null(witness.SourceHeroId);
            Assert.Contains(log.Lines, l => l.Contains("witness_lord") && l.Contains("hop 1 hearsay") && l.Contains("child_born"));
        }

        [Fact]
        public void Seed_DefaultTemplate_WitnessStaysHopZeroAndNothingIsLogged()
        {
            var (evt, log) = SeedWithWitness(colocatedAsHearsay: false);

            Assert.Equal(0, evt.KnownBy.Single(k => k.HeroId == "witness_lord").Hop);
            Assert.DoesNotContain(log.Lines, l => l.Contains("hop 1 hearsay"));
        }

        [Fact]
        public void CatalogSwitchesOnColocatedHearsay_ForExactlyBirthAndBothEscapes()
        {
            var flagged = LoadCatalog("vividworld_events.json").Templates
                .Where(t => t.ColocatedWitnessAsHearsay).Select(t => t.Type).OrderBy(t => t, StringComparer.Ordinal).ToList();

            Assert.Equal(new[] { "child_born", "hero_escaped_bandits", "hero_escaped_captivity" }, flagged);
        }

        [Fact]
        public void ColocatedHearsay_FlagSurvivesBindingIntoTheSubmission()
        {
            var template = LoadCatalog("vividworld_events.json").ByType("child_born")!;
            var bindings = new Dictionary<string, string>
            {
                ["MOTHER"] = "hero_mother", ["CHILD"] = "hero_child", ["SETTLEMENT"] = "settlement_x"
            };

            var submission = TemplateBinder.Bind(template, bindings, 10.0, null, out var issues);

            Assert.NotNull(submission);
            Assert.Empty(issues.Where(i => i.IsError));
            Assert.True(submission!.ColocatedWitnessAsHearsay);
        }

        // ────────────── 組合列舉與實際保留規則是同一份 ──────────────

        [Fact]
        public void EnumerateCombinations_MatchesTheRealRetentionPolicyForCustomSettings()
        {
            var retention = new RetentionConfig
            {
                HopThresholds = new[] { 4, 3, 2, 1 },
                AlwaysKeepAtOrBelowFragility = 0,
                MinFactsRetained = 2,
                DramaThresholdShift = new[] { 0, 0, 1, 0, 0 }
            };
            var template = new EventTemplate
            {
                Type = "test_template",
                DramaWeight = 3,
                Facts = new List<TemplateFact>
                {
                    new TemplateFact { Id = "who", Category = FactCategory.Who, TextId = "VividWorld_Fact_Test_Who", Fragility = 1 },
                    new TemplateFact { Id = "where", Category = FactCategory.Where, TextId = "VividWorld_Fact_Test_Where", Fragility = 2 },
                    new TemplateFact { Id = "what", Category = FactCategory.What, TextId = "VividWorld_Fact_Test_What", Fragility = 3 },
                    new TemplateFact { Id = "outcome", Category = FactCategory.Outcome, TextId = "VividWorld_Fact_Test_Outcome", Fragility = 5 }
                }
            };

            var combos = SentenceCombinationEnumerator.EnumerateCombinations(template, retention);

            var evt = new WorldEvent
            {
                DramaWeight = 3,
                Facts = template.Facts.Select(tf => new Fact { Id = tf.Id, Category = tf.Category, TextId = tf.TextId!, Fragility = tf.Fragility }).ToList()
            };
            var policy = new ThresholdRetentionPolicy(retention);
            for (int hop = 0; hop < retention.HopThresholds.Length; hop++)
            {
                var kept = policy.Retain(evt, hop, string.Empty).Select(f => f.Id).ToHashSet();
                string expectedKey = string.Join("_", template.Facts.Where(f => kept.Contains(f.Id))
                    .Select(f => SentenceCombinationEnumerator.ExtractSegment(f.TextId!)));

                Assert.Contains(combos, c => c.CombinationKey == expectedKey && c.Hops.Contains(hop));
            }
        }

        // ────────────── 共用 ──────────────

        private static EventCatalog LoadCatalog(string file)
        {
            string path = Path.Combine(FindRepoRoot(), "module", "ModuleData", file);
            return EventCatalogLoader.Load(File.ReadAllText(path), new PersistenceConfig());
        }

        private static string FindRepoRoot()
        {
            string? current = AppContext.BaseDirectory;
            while (!string.IsNullOrEmpty(current))
            {
                if (File.Exists(Path.Combine(current, "VividWorld.sln"))) return current;
                current = Directory.GetParent(current)?.FullName;
            }
            throw new DirectoryNotFoundException("repo root not found");
        }
    }
}
