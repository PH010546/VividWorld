using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using VividWorld.Core.Catalog;
using VividWorld.Core.Config;
using VividWorld.Core.Diagnostics;
using VividWorld.Core.Events;
using VividWorld.Core.Presentation;
using Xunit;

namespace VividWorld.Core.Tests
{
    /// <summary>
    /// 整句句型的查鍵順序（說話的人是當事人、從當事人那裡聽來、旁人）、當事人句尾、
    /// 句子結尾的標點與英文代名詞。
    /// </summary>
    public class SentenceSourceAndFeelingTests
    {
        private const string Stem = "HeroTakenPrisoner";
        private const string SentenceWhere = "VividWorld_Sentence_HeroTakenPrisoner_Who_Where_What";

        private static WorldEvent PrisonerEvent()
        {
            return new WorldEvent
            {
                EventId = "evt_test_prisoner",
                Type = "hero_taken_prisoner",
                Origin = EventOrigin.Public,
                Participants = new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["captor"] = "hero_captor",
                    ["prisoner"] = "hero_prisoner"
                }
            };
        }

        private static List<Fact> PrisonerFacts()
        {
            return new List<Fact>
            {
                new Fact
                {
                    Id = "who", Category = FactCategory.Who, TextId = "VividWorld_Fact_HeroTakenPrisoner_Who",
                    Text = "{CAPTOR} captured {PRISONER}", Fragility = 1,
                    Vars = new Dictionary<string, string> { ["CAPTOR"] = "hero:hero_captor", ["PRISONER"] = "hero:hero_prisoner" }
                },
                new Fact
                {
                    Id = "where", Category = FactCategory.Where, TextId = "VividWorld_Fact_HeroTakenPrisoner_Where",
                    Text = "near {SETTLEMENT}", Fragility = 2,
                    Vars = new Dictionary<string, string> { ["SETTLEMENT"] = "settlement:town" }
                },
                new Fact
                {
                    Id = "what", Category = FactCategory.What, TextId = "VividWorld_Fact_HeroTakenPrisoner_What",
                    Text = "stripped of weapons", Fragility = 3, Vars = new Dictionary<string, string>()
                }
            };
        }

        private static RumorRenderResult Render(
            ComposedRumor composed,
            Dictionary<string, string> table,
            List<string>? info = null,
            PresentationConfig? cfg = null,
            Func<string, bool?>? isFemale = null)
        {
            var t = new EnglishStringTable(table);
            cfg ??= new PresentationConfig { EncyclopediaLinksEnabled = false, SentenceEnd = ".", FactSeparator = ", " };
            RumorTextAssembler.ResetSessionMissingSentenceKeys();
            return RumorTextAssembler.Assemble(
                composed,
                cfg,
                (val, links) => val.Contains(':') ? val.Substring(val.IndexOf(':') + 1) : val,
                (id, fb) => fb == null ? (t.Get(id) ?? string.Empty) : t.Lookup(id, fb),
                (key, fb) => t.GetWithFallback(key, fb),
                onWarning: null,
                isFemale: isFemale,
                onInfo: info == null ? null : info.Add);
        }

        private static ComposedRumor Compose(string? speaker, string? source, WorldEvent? evt = null)
        {
            return RumorTextComposer.Compose(evt ?? PrisonerEvent(), PrisonerFacts(), new PresentationConfig(), prefix: null,
                speakerHeroId: speaker, sourceHeroId: source);
        }

        [Fact]
        public void KeyOrder_SourceIsParticipant_FromKeyFirstThenPlainKey()
        {
            var composed = Compose(speaker: "hero_listener", source: "hero_captor");

            Assert.Equal(new[]
            {
                SentenceWhere + "_From_CAPTOR",
                SentenceWhere
            }, composed.SentenceKeyCandidates);
            Assert.Empty(composed.SelfFeelingKeyCandidates);
        }

        [Fact]
        public void KeyOrder_SourceIsNotAParticipant_OnlyPlainKey()
        {
            var composed = Compose(speaker: "hero_listener", source: "hero_bystander");
            Assert.Equal(new[] { SentenceWhere }, composed.SentenceKeyCandidates);
        }

        [Fact]
        public void KeyOrder_SpeakerIsParticipant_OnlySelfKeyEvenWhenSourceIsAlsoParticipant()
        {
            var composed = Compose(speaker: "hero_captor", source: "hero_prisoner");

            Assert.Equal(new[] { SentenceWhere + "_Self_CAPTOR" }, composed.SentenceKeyCandidates);
            Assert.Equal(new[] { "VividWorld_SelfFeeling_" + Stem + "_CAPTOR" }, composed.SelfFeelingKeyCandidates);
        }

        [Fact]
        public void KeyOrder_Eyewitness_WitnessKeyFirstThenPlainKey()
        {
            var prefix = RumorPrefixSelector.SelectPrefix(0, null, isRetell: false, isCorrection: false, isParticipant: false);
            var composed = RumorTextComposer.Compose(PrisonerEvent(), PrisonerFacts(), new PresentationConfig(), prefix,
                speakerHeroId: "hero_onlooker", sourceHeroId: null);

            Assert.Equal(new[] { SentenceWhere + "_Witness", SentenceWhere }, composed.SentenceKeyCandidates);
        }

        [Fact]
        public void KeyOrder_EyewitnessRetelling_AlsoWitnessKeyFirst()
        {
            var prefix = RumorPrefixSelector.SelectPrefix(0, null, isRetell: true, isCorrection: false, isParticipant: false);
            var composed = RumorTextComposer.Compose(PrisonerEvent(), PrisonerFacts(), new PresentationConfig(), prefix,
                speakerHeroId: "hero_onlooker", sourceHeroId: null);

            Assert.Equal(new[] { SentenceWhere + "_Witness", SentenceWhere }, composed.SentenceKeyCandidates);
        }

        [Fact]
        public void Assemble_WitnessAndHearsayShareFacts_EachGetsOwnSentence()
        {
            var table = new Dictionary<string, string>
            {
                [SentenceWhere + "_Witness"] = "{PRISONER} was led away. I heard {CAPTOR} did it",
                [SentenceWhere] = "{CAPTOR} captured {PRISONER}"
            };

            var witnessPrefix = RumorPrefixSelector.SelectPrefix(0, null, isRetell: false, isCorrection: false, isParticipant: false);
            var witness = RumorTextComposer.Compose(PrisonerEvent(), PrisonerFacts(), new PresentationConfig(), witnessPrefix,
                speakerHeroId: "hero_onlooker", sourceHeroId: null);
            var hearsayPrefix = RumorPrefixSelector.SelectPrefix(1, "hero_bystander", isRetell: false, isCorrection: false, isParticipant: false);
            var hearsay = RumorTextComposer.Compose(PrisonerEvent(), PrisonerFacts(), new PresentationConfig(), hearsayPrefix,
                speakerHeroId: "hero_listener", sourceHeroId: "hero_bystander");

            Assert.Equal(SentenceWhere + "_Witness", Render(witness, table).SentenceKeyUsed);
            Assert.Equal(SentenceWhere, Render(hearsay, table).SentenceKeyUsed);
        }

        [Fact]
        public void KeyOrder_NoEvent_ChronicleUsesPlainKey()
        {
            var composed = RumorTextComposer.Compose(null, PrisonerFacts(), new PresentationConfig(), isRetell: false);
            Assert.Equal(new[] { SentenceWhere }, composed.SentenceKeyCandidates);
        }

        [Fact]
        public void Assemble_FromKeyPresent_UsesItAndLogsAllKeysChecked()
        {
            var composed = Compose(speaker: "hero_listener", source: "hero_captor");
            var info = new List<string>();
            var table = new Dictionary<string, string>
            {
                [SentenceWhere + "_From_CAPTOR"] = "{CAPTOR.he} said it",
                [SentenceWhere] = "{CAPTOR} did it"
            };

            var result = Render(composed, table, info, isFemale: id => false);

            Assert.True(result.UsedWholeSentence);
            Assert.Equal(SentenceWhere + "_From_CAPTOR", result.SentenceKeyUsed);
            Assert.Equal("He said it.", result.PlainText);
            Assert.Contains(info, l => l.Contains(SentenceWhere + "_From_CAPTOR") && l.Contains(SentenceWhere) && l.Contains("keys checked"));
        }

        [Fact]
        public void Assemble_FromKeyMissing_FallsBackToPlainKey()
        {
            var composed = Compose(speaker: "hero_listener", source: "hero_captor");
            var info = new List<string>();
            var table = new Dictionary<string, string> { [SentenceWhere] = "{CAPTOR} did it" };

            var result = Render(composed, table, info);

            Assert.True(result.UsedWholeSentence);
            Assert.Equal(SentenceWhere, result.SentenceKeyUsed);
            Assert.Equal(2, result.SentenceKeysChecked.Count);
            Assert.Equal(SentenceWhere + "_From_CAPTOR", result.SentenceKeysChecked[0]);
        }

        [Fact]
        public void Assemble_NoKeyAtAll_FallsBackToConcatenationAndLogsTheKeys()
        {
            var composed = Compose(speaker: "hero_listener", source: "hero_captor");
            var info = new List<string>();

            var result = Render(composed, new Dictionary<string, string>(), info);

            Assert.False(result.UsedWholeSentence);
            Assert.Contains(info, l => l.Contains("fallback to concatenation") && l.Contains("_From_CAPTOR"));
        }

        [Fact]
        public void Feeling_SpeakerIsParticipant_AppendedAfterTheFactSentence()
        {
            var composed = Compose(speaker: "hero_captor", source: null);
            var info = new List<string>();
            var table = new Dictionary<string, string>
            {
                [SentenceWhere + "_Self_CAPTOR"] = "I captured {PRISONER}",
                ["VividWorld_SelfFeeling_" + Stem + "_CAPTOR"] = "At least it was not for nothing"
            };

            var result = Render(composed, table, info);

            Assert.Equal("I captured hero_prisoner. At least it was not for nothing.", result.PlainText);
            Assert.Equal("VividWorld_SelfFeeling_" + Stem + "_CAPTOR", result.SelfFeelingKeyUsed);
            Assert.Contains(info, l => l.Contains("self feeling") && l.Contains("using 'VividWorld_SelfFeeling_" + Stem + "_CAPTOR'"));
        }

        [Fact]
        public void Feeling_SpeakerIsNotParticipant_NotAppendedAndLogSaysWhy()
        {
            var composed = Compose(speaker: "hero_listener", source: null);
            var info = new List<string>();
            var table = new Dictionary<string, string>
            {
                [SentenceWhere] = "{CAPTOR} captured {PRISONER}",
                ["VividWorld_SelfFeeling_" + Stem + "_CAPTOR"] = "should not appear"
            };

            var result = Render(composed, table, info);

            Assert.DoesNotContain("should not appear", result.PlainText);
            Assert.Null(result.SelfFeelingKeyUsed);
            Assert.Contains(info, l => l.Contains("self feeling: omitted") && l.Contains("not the person this happened to"));
        }

        [Fact]
        public void Feeling_ConcatenationFallback_DoesNotAppendFeelingTwice()
        {
            var composed = Compose(speaker: "hero_captor", source: null);
            var info = new List<string>();
            var table = new Dictionary<string, string>
            {
                ["VividWorld_SelfFeeling_" + Stem + "_CAPTOR"] = "should not appear"
            };

            var result = Render(composed, table, info);

            Assert.False(result.UsedWholeSentence);
            Assert.DoesNotContain("should not appear", result.PlainText);
            Assert.Contains(info, l => l.Contains("self feeling: omitted") && l.Contains("no whole sentence"));
        }

        [Fact]
        public void Feeling_MissingKey_SentenceStillUsedAndLogListsTheMissingKeys()
        {
            var composed = Compose(speaker: "hero_captor", source: null);
            var info = new List<string>();
            var table = new Dictionary<string, string> { [SentenceWhere + "_Self_CAPTOR"] = "I captured {PRISONER}" };

            var result = Render(composed, table, info);

            Assert.True(result.UsedWholeSentence);
            Assert.Equal("I captured hero_prisoner.", result.PlainText);
            Assert.Contains(info, l => l.Contains("self feeling: omitted (missing keys"));
        }

        [Fact]
        public void Feeling_VariantSegmentKeyIsCheckedBeforeTheGeneralKey()
        {
            // 放人的原因是碎片裡專屬的一段：句尾先找帶原因的鍵，沒有再用一般的鍵
            var keys = SentenceCombinationEnumerator.ComputeSelfFeelingKeys("HeroReleased", new[] { "Who", "Where", "Ransom" }, "CAPTOR");

            Assert.Equal(new[]
            {
                "VividWorld_SelfFeeling_HeroReleased_Ransom_CAPTOR",
                "VividWorld_SelfFeeling_HeroReleased_CAPTOR"
            }, keys);

            var plain = SentenceCombinationEnumerator.ComputeSelfFeelingKeys("HeroTakenPrisoner", new[] { "Who", "Where", "What", "Outcome" }, "CAPTOR");
            Assert.Equal(new[] { "VividWorld_SelfFeeling_HeroTakenPrisoner_CAPTOR" }, plain);
        }

        [Fact]
        public void SentenceEnd_QuestionMarkAndEllipsis_NoExtraPeriod()
        {
            var composed = Compose(speaker: "hero_captor", source: null);
            var table = new Dictionary<string, string>
            {
                [SentenceWhere + "_Self_CAPTOR"] = "I know how it went...",
                ["VividWorld_SelfFeeling_" + Stem + "_CAPTOR"] = "What does that make me?"
            };

            var result = Render(composed, table);

            Assert.Equal("I know how it went... What does that make me?", result.PlainText);
        }

        [Fact]
        public void Pronouns_FemaleMaleUnknown_AndReflexive()
        {
            var composed = Compose(speaker: "hero_listener", source: "hero_captor");
            var table = new Dictionary<string, string>
            {
                [SentenceWhere + "_From_CAPTOR"] = "{CAPTOR.he} was near, {CAPTOR.his} men were too, and {CAPTOR.he} did it {CAPTOR.himself}"
            };

            string female = Render(composed, table, isFemale: id => true).PlainText;
            string male = Render(composed, table, isFemale: id => false).PlainText;
            string unknown = Render(composed, table, isFemale: id => null).PlainText;

            Assert.Equal("She was near, her men were too, and she did it herself.", female);
            Assert.Equal("He was near, his men were too, and he did it himself.", male);
            // 查不到性別時是 they，動詞要跟著換複數形，不能出現 they was
            Assert.Equal("They were near, their men were too, and they did it themselves.", unknown);
        }

        [Fact]
        public void Pronouns_NeutralAgreement_CoversIsAndHasAndNegatives()
        {
            var composed = Compose(speaker: "hero_listener", source: "hero_captor");
            var table = new Dictionary<string, string>
            {
                [SentenceWhere + "_From_CAPTOR"] = "{CAPTOR.he} wasn't there, {CAPTOR.he} isn't sure and {CAPTOR.he} has nothing to add"
            };

            string unknown = Render(composed, table, isFemale: id => null).PlainText;

            Assert.Equal("They weren't there, they aren't sure and they have nothing to add.", unknown);
        }

        [Fact]
        public void Pronouns_SpeakerReferringToSelf_UsesFirstPerson()
        {
            var composed = Compose(speaker: "hero_captor", source: null);
            var table = new Dictionary<string, string>
            {
                [SentenceWhere + "_Self_CAPTOR"] = "{CAPTOR.he} did it {CAPTOR.himself}"
            };

            var text = Render(composed, table, isFemale: id => true).PlainText;

            Assert.Equal("I did it myself.", text);
        }

        [Fact]
        public void EnglishSentenceAfterOpening_KeepsLowercaseFirstWord()
        {
            var facts = PrisonerFacts();
            var evt = PrisonerEvent();
            var prefix = RumorPrefixSelector.SelectPrefix(2, "hero_someone", false, false, false);
            var composed = RumorTextComposer.Compose(evt, facts, new PresentationConfig(), prefix, "hero_listener", null);
            var table = new Dictionary<string, string>
            {
                [SentenceWhere] = "in that fight near {SETTLEMENT}, {CAPTOR} took {PRISONER}",
                ["VividWorld_Prefix_HeardFromSource"] = "{SOURCE} told me that"
            };

            var text = Render(composed, table).PlainText;

            Assert.StartsWith("hero_someone told me that in that fight near town", text);
        }

        [Fact]
        public void FormatAll_EveryRealTemplate_UsesWholeSentencesAndHasNoLeftovers()
        {
            var (output, _) = RunFormatter();

            Assert.DoesNotContain("[Concat]", output);
            Assert.DoesNotContain("[拼接]", output);
            Assert.DoesNotContain("they was", output);
            Assert.DoesNotContain("they is", output);
            Assert.DoesNotContain("?.", output);
            Assert.DoesNotContain("{", output);
            Assert.DoesNotContain("(no self feeling)", output);
            Assert.Contains("Missing Sentence Keys in English (Count: 0)", output);
            Assert.Contains("Missing Sentence Keys in Traditional Chinese (Count: 0)", output);
            Assert.Contains("Unreferenced Sentence Keys in English (Count: 0)", output);
            Assert.Contains("Unreferenced Sentence Keys in Traditional Chinese (Count: 0)", output);
        }

        [Fact]
        public void FormatAll_ReportsHowManyCombinationsHaveAFromSentence()
        {
            var (output, _) = RunFormatter();

            var m = System.Text.RegularExpressions.Regex.Match(output, @"Sentence Combinations with _From_ Key in English \(Count: (\d+)\)");
            Assert.True(m.Success);
            Assert.True(int.Parse(m.Groups[1].Value) > 0);
            Assert.Contains("Sentence Counts in English: onlooker", output);
        }

        [Fact]
        public void FormatAll_ListsEveryReleaseReasonShapeIncludingNoCaptorAndOldEvents()
        {
            var (output, _) = RunFormatter();

            foreach (var label in new[]
            {
                "[hero_released (ransom)]", "[hero_released (peace)]", "[hero_released (defeated)]",
                "[hero_released (settlement_owner_changed)]", "[hero_released (player_choice)]", "[hero_released (disbanded)]",
                "[hero_released (no captor, any reason)]", "[hero_released (old event, no reason)]",
                "[hero_released (old event, no reason, no captor)]",
                "[hero_escaped_captivity (WhoNoCaptor)]", "[hero_rescued_from_bandits (WhoNoRescuer)]"
            })
            {
                Assert.Contains(label, output);
            }
        }

        [Fact]
        public void SentenceKeys_EveryKeyIsReferencedByTheEnumeratedCombinations_InBothLanguages()
        {
            // 沒有任何組合會用到的句子是死字串（多半是碎片改了、句子沒跟著改）
            var (output, _) = RunFormatter();
            Assert.DoesNotContain("  VividWorld_Sentence_", output.Substring(output.IndexOf("=== Unreferenced", StringComparison.Ordinal)));
        }

        private static (string Output, List<EventTemplate> Templates) RunFormatter()
        {
            string root = FindRepoRoot();
            var pCfg = new PersistenceConfig();
            var all = new List<EventTemplate>();
            foreach (var f in new[] { "vividworld_events.json", "vividworld_situation_events.json" })
            {
                all.AddRange(EventCatalogLoader.Load(File.ReadAllText(Path.Combine(root, "module", "ModuleData", f)), pCfg).Templates);
            }
            var en = EnglishStringTable.LoadFromFile(Path.Combine(root, "module", "ModuleData", "Languages", "std_module_strings_xml.xml"));
            var cnt = EnglishStringTable.LoadFromFile(Path.Combine(root, "module", "ModuleData", "Languages", "CNt", "std_module_strings_xml.xml"));
            return (TemplateRenderSampleFormatter.FormatAll(all, en, cnt), all);
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
