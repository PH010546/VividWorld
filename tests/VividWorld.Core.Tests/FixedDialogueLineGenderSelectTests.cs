using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using VividWorld.Core.Config;
using VividWorld.Core.Dialogue;
using VividWorld.Core.Feelings;
using VividWorld.Core.Presentation;
using Xunit;

namespace VividWorld.Core.Tests
{
    /// <summary>
    /// 選字記號的 <c>YOU</c>（聽的人）與對話選單固定句子用的函式。
    /// </summary>
    public class FixedDialogueLineGenderSelectTests
    {
        private static readonly Func<string, bool?> IsFemaleLookup = id => id switch
        {
            "hero_m" => false,
            "hero_f" => true,
            "hero_spk_m" => false,
            "hero_spk_f" => true,
            "hero_player_m" => false,
            "hero_player_f" => true,
            _ => null
        };

        private static string FindRepoRoot()
        {
            string? current = AppContext.BaseDirectory;
            while (!string.IsNullOrEmpty(current))
            {
                if (File.Exists(Path.Combine(current, "VividWorld.sln"))) return current;
                var parent = Directory.GetParent(current);
                if (parent == null) break;
                current = parent.FullName;
            }
            throw new InvalidOperationException($"Could not locate repo root from {AppContext.BaseDirectory}");
        }

        [Fact]
        public void You_MaleFemaleUnknownAndMissingListener()
        {
            const string two = "{YOU:он|она}";
            const string three = "{YOU:он|она|оно}";

            Assert.Equal("он", RumorTextAssembler.ApplyGenderSelectTokens(two, null, null, null, IsFemaleLookup, null, null, "key", "hero_m"));
            Assert.Equal("она", RumorTextAssembler.ApplyGenderSelectTokens(two, null, null, null, IsFemaleLookup, null, null, "key", "hero_f"));
            Assert.Equal("оно", RumorTextAssembler.ApplyGenderSelectTokens(three, null, null, null, IsFemaleLookup, null, null, "key", "hero_unknown"));
            Assert.Equal("он", RumorTextAssembler.ApplyGenderSelectTokens(two, null, null, null, IsFemaleLookup, null, null, "key", "hero_unknown"));
            Assert.Equal("оно", RumorTextAssembler.ApplyGenderSelectTokens(three, null, null, null, IsFemaleLookup, null, null, "key", null));
            Assert.Equal("он", RumorTextAssembler.ApplyGenderSelectTokens(two, null, null, null, IsFemaleLookup, null, null, "key", null));
            // 名字不分大小寫
            Assert.Equal("она", RumorTextAssembler.ApplyGenderSelectTokens("{you:он|она}", null, null, null, IsFemaleLookup, null, null, "key", "hero_f"));
        }

        [Fact]
        public void You_NoWarningWhenListenerMissingOrUnknown()
        {
            var warnings = new List<string>();
            RumorTextAssembler.ApplyGenderSelectTokens("{YOU:a|b}", null, null, null, IsFemaleLookup, null, warnings.Add, "key", null);
            RumorTextAssembler.ApplyGenderSelectTokens("{YOU:a|b}", null, null, null, IsFemaleLookup, null, warnings.Add, "key", "hero_unknown");
            RumorTextAssembler.ApplyGenderSelectTokens("{YOU:a|b}", null, null, null, null, null, warnings.Add, "key", "hero_f");
            Assert.Empty(warnings);
        }

        [Fact]
        public void You_InAllSixAssemblyPlaces()
        {
            const string listener = "hero_player_f";

            // (a) 整句事實句
            var rWhole = new ComposedRumor
            {
                SentenceKeyCandidates = new[] { "sent_whole" },
                Parts = new[] { new ComposedFactPart { TextId = "p_dummy", Fallback = "dummy" } }
            };
            var whole = RumorTextAssembler.Assemble(
                rWhole, new PresentationConfig { WholeSentences = true }, (v, _) => v,
                (id, _) => id == "sent_whole" ? "Ты {YOU:знал|знала} это." : "",
                null, null, IsFemaleLookup, null, listener);
            Assert.Equal("Ты знала это.", whole.PlainText);

            // (b) 拼接碎片
            var rConcat = new ComposedRumor
            {
                Parts = new[] { new ComposedFactPart { TextId = "part_c", Fallback = "ты {YOU:ушёл|ушла}" } }
            };
            var concat = RumorTextAssembler.Assemble(
                rConcat, new PresentationConfig { WholeSentences = false }, (v, _) => v,
                null, null, null, IsFemaleLookup, null, "hero_player_m");
            Assert.Equal("ты ушёл.", concat.PlainText);

            // (c) 開頭語
            var rPrefix = new ComposedRumor
            {
                PrefixTextId = "pref",
                PrefixFallback = "Слушай, {YOU:друг|подруга}:",
                Parts = new[] { new ComposedFactPart { TextId = "p", Fallback = "факт" } }
            };
            var prefix = RumorTextAssembler.Assemble(
                rPrefix, new PresentationConfig { WholeSentences = false }, (v, _) => v,
                null, null, null, IsFemaleLookup, null, listener);
            Assert.Equal("Слушай, подруга: факт.", prefix.PlainText);

            // (d) 當事人句尾
            var rSelf = new ComposedRumor
            {
                SpeakerHeroId = "hero_spk_m",
                SpeakerRole = "PRISONER",
                Roles = new Dictionary<string, string> { ["PRISONER"] = "hero_spk_m" },
                SentenceKeyCandidates = new[] { "sent_k" },
                SelfFeelingKeyCandidates = new[] { "self_k" },
                Parts = new[] { new ComposedFactPart { TextId = "p_dummy", Fallback = "dummy" } }
            };
            var self = RumorTextAssembler.Assemble(
                rSelf, new PresentationConfig { WholeSentences = true }, (v, _) => v,
                (id, _) => id switch { "sent_k" => "Я на свободе.", "self_k" => "Ты {YOU:рад|рада}?", _ => "" },
                null, null, IsFemaleLookup, null, listener);
            Assert.Equal("Я на свободе. Ты рада?", self.PlainText);

            // (e) 收尾句
            var rClosing = new ComposedRumor
            {
                SentenceKeyCandidates = new[] { "sent_close" },
                ClosingKey = "closing_k",
                Parts = new[] { new ComposedFactPart { TextId = "p_dummy", Fallback = "dummy" } }
            };
            var closing = RumorTextAssembler.Assemble(
                rClosing, new PresentationConfig { WholeSentences = true }, (v, _) => v,
                (id, _) => id switch { "sent_close" => "Случилось дело.", "closing_k" => "Ты {YOU:понял|поняла}.", _ => "" },
                null, null, IsFemaleLookup, null, listener);
            Assert.Equal("Случилось дело. Ты поняла.", closing.PlainText);

            // (f) 感想句
            var decision = new FeelingDecision { LineKey = "feel_k", FocusHeroId = "hero_m", FocusIsFemale = false };
            string? feeling = RumorTextAssembler.RenderFeeling(
                decision, false, (v, _) => v,
                (id, _) => id == "feel_k" ? "{FOCUS:Он|Она} вернулся, ты {YOU:рад|рада}, я {ME:рад|рада}." : "",
                null, ".", out _, IsFemaleLookup, "hero_spk_m", null, listener);
            Assert.Equal("Он вернулся, ты рада, я рад.", feeling);
        }

        [Fact]
        public void FixedLine_MeAndYouEachFollowTheirOwnGender()
        {
            const string tpl = "{ME:Я рад|Я рада}, что {YOU:ты пришёл|ты пришла}.";
            Assert.Equal("Я рад, что ты пришёл.", RumorTextAssembler.ApplyFixedLineGenderSelect(tpl, "k", "hero_spk_m", "hero_player_m", IsFemaleLookup, null));
            Assert.Equal("Я рад, что ты пришла.", RumorTextAssembler.ApplyFixedLineGenderSelect(tpl, "k", "hero_spk_m", "hero_player_f", IsFemaleLookup, null));
            Assert.Equal("Я рада, что ты пришёл.", RumorTextAssembler.ApplyFixedLineGenderSelect(tpl, "k", "hero_spk_f", "hero_player_m", IsFemaleLookup, null));
            Assert.Equal("Я рада, что ты пришла.", RumorTextAssembler.ApplyFixedLineGenderSelect(tpl, "k", "hero_spk_f", "hero_player_f", IsFemaleLookup, null));
        }

        [Fact]
        public void FixedLine_RoleNamesAndFocusWarnAndUseFirstSegment()
        {
            var warnings = new List<string>();
            string result = RumorTextAssembler.ApplyFixedLineGenderSelect(
                "{PRISONER:a|b} {FOCUS:c|d} {ME:e|f}", "VividWorld_Test", "hero_f", "hero_m", IsFemaleLookup, warnings.Add);
            Assert.Equal("a c f", result);
            Assert.Equal(2, warnings.Count);
            Assert.All(warnings, w => Assert.Contains("VividWorld_Test", w));
            Assert.Contains(warnings, w => w.Contains("{PRISONER:a|b}"));
            Assert.Contains(warnings, w => w.Contains("{FOCUS:c|d}"));
        }

        [Fact]
        public void FixedLine_StringWithoutTokensIsReturnedAsIs()
        {
            foreach (string s in new[] { "Any news on the road?", "最近路上有什麼消息嗎？", "Time: now", "{PLACE}", "" })
            {
                Assert.Equal(s, RumorTextAssembler.ApplyFixedLineGenderSelect(s, "k", "hero_f", "hero_m", IsFemaleLookup, null));
            }
        }

        [Fact]
        public void FixedLine_TenShippingStrings_RenderIdenticallyForMaleAndFemale()
        {
            string repoRoot = FindRepoRoot();
            string[] keys =
            {
                "VividWorld_AskNews", "VividWorld_RecoveryAsk", "VividWorld_AskNothing",
                AskRefusalLine.KeyUnwilling, AskRefusalLine.KeyDisliked, AskRefusalLine.KeyNothingHeard, AskRefusalLine.KeyForgotten,
                AskRefusalLine.KeyOutdated, AskRefusalLine.KeyPlayerKnows, AskRefusalLine.KeySharedToday
            };
            Assert.Equal(10, keys.Distinct().Count());

            foreach (string rel in new[] { "std_module_strings_xml.xml", Path.Combine("CNt", "std_module_strings_xml.xml") })
            {
                string path = Path.Combine(repoRoot, "module", "ModuleData", "Languages", rel);
                var table = XDocument.Load(path).Root!.Element("strings")!.Elements("string")
                    .ToDictionary(e => e.Attribute("id")!.Value, e => e.Attribute("text")!.Value, StringComparer.Ordinal);

                foreach (string key in keys)
                {
                    Assert.True(table.TryGetValue(key, out string? text), $"{key} missing from {rel}");
                    var warnings = new List<string>();
                    foreach (var (speaker, listener) in new[] { ("hero_spk_m", "hero_player_m"), ("hero_spk_f", "hero_player_f") })
                    {
                        string result = RumorTextAssembler.ApplyFixedLineGenderSelect(text!, key, speaker, listener, IsFemaleLookup, warnings.Add);
                        Assert.Equal(text, result);
                    }
                    Assert.Empty(warnings);
                }
            }
        }
    }
}
