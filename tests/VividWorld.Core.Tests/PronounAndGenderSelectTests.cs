using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using Newtonsoft.Json.Linq;
using VividWorld.Core.Config;
using VividWorld.Core.Feelings;
using VividWorld.Core.Presentation;
using Xunit;

namespace VividWorld.Core.Tests
{
    public class PronounAndGenderSelectTests
    {
        private static string FindRepoRoot()
        {
            string? current = AppContext.BaseDirectory;
            while (!string.IsNullOrEmpty(current))
            {
                if (File.Exists(Path.Combine(current, "VividWorld.sln")))
                {
                    return current;
                }
                var parent = Directory.GetParent(current);
                if (parent == null) break;
                current = parent.FullName;
            }
            throw new InvalidOperationException($"Could not locate repo root from {AppContext.BaseDirectory}");
        }

        private static Dictionary<string, string> CreateRussianPronounTable()
        {
            return new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["VividWorld_Pronoun_He_M"] = "он",
                ["VividWorld_Pronoun_He_F"] = "она",
                ["VividWorld_Pronoun_He_N"] = "оно",
                ["VividWorld_Pronoun_Him_M"] = "его",
                ["VividWorld_Pronoun_Him_F"] = "её",
                ["VividWorld_Pronoun_Him_N"] = "их",
                ["VividWorld_Pronoun_His_M"] = "его_poss",
                ["VividWorld_Pronoun_His_F"] = "её_poss",
                ["VividWorld_Pronoun_His_N"] = "их_poss",
                ["VividWorld_Pronoun_Himself_M"] = "сам",
                ["VividWorld_Pronoun_Himself_F"] = "сама",
                ["VividWorld_Pronoun_Himself_N"] = "сами",
                ["VividWorld_Self_Reflexive"] = "себя"
            };
        }

        [Fact]
        public void Pronouns_ThirdLanguageStringTable_SubstitutesAllKeysCorrectly_FactAndFeeling_AndCapitalizesHe()
        {
            var ruTable = CreateRussianPronounTable();
            Func<string?, string, string> getLocalized = (k, fb) => ruTable.TryGetValue(k ?? string.Empty, out var v) ? v : fb;

            // 1. 事實句：男、女、查不到各換成表裡的字，首字母大寫與自身反身代名詞也替換
            var roles = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["PRISONER"] = "hero_p"
            };

            // 男
            var rMale = new ComposedRumor
            {
                Roles = roles,
                Parts = new[]
                {
                    new ComposedFactPart
                    {
                        TextId = "part1",
                        Fallback = "{PRISONER.He} said {PRISONER.he} saw {PRISONER.him}, took {PRISONER.his} horse, by {PRISONER.himself}."
                    }
                }
            };
            var resMale = RumorTextAssembler.Assemble(
                rMale, new PresentationConfig { WholeSentences = false },
                (v, _) => v, null, getLocalized, null, id => id == "hero_p" ? false : (bool?)null);
            Assert.Equal("Он said он saw его, took его_poss horse, by сам.", resMale.PlainText);

            // 女
            var rFemale = new ComposedRumor
            {
                Roles = roles,
                Parts = new[]
                {
                    new ComposedFactPart
                    {
                        TextId = "part1",
                        Fallback = "{PRISONER.He} said {PRISONER.he} saw {PRISONER.him}, took {PRISONER.his} horse, by {PRISONER.himself}."
                    }
                }
            };
            var resFemale = RumorTextAssembler.Assemble(
                rFemale, new PresentationConfig { WholeSentences = false },
                (v, _) => v, null, getLocalized, null, id => id == "hero_p" ? true : (bool?)null);
            Assert.Equal("Она said она saw её, took её_poss horse, by сама.", resFemale.PlainText);

            // 查不到性別（isFemale 回傳 null）
            var rUnknown = new ComposedRumor
            {
                Roles = roles,
                Parts = new[]
                {
                    new ComposedFactPart
                    {
                        TextId = "part1",
                        Fallback = "{PRISONER.He} said {PRISONER.he} saw {PRISONER.him}, took {PRISONER.his} horse, by {PRISONER.himself}."
                    }
                }
            };
            var resUnknown = RumorTextAssembler.Assemble(
                rUnknown, new PresentationConfig { WholeSentences = false },
                (v, _) => v, null, getLocalized, null, _ => null);
            Assert.Equal("Оно said оно saw их, took их_poss horse, by сами.", resUnknown.PlainText);

            // 說話的人就是當事人：{角色.himself} 換成 VividWorld_Self_Reflexive ("себя")
            var rSelf = new ComposedRumor
            {
                Roles = roles,
                SpeakerHeroId = "hero_p",
                SpeakerRole = "PRISONER",
                Parts = new[]
                {
                    new ComposedFactPart
                    {
                        TextId = "part1",
                        Fallback = "I saw {PRISONER.himself} in the mirror."
                    }
                }
            };
            var resSelf = RumorTextAssembler.Assemble(
                rSelf, new PresentationConfig { WholeSentences = false },
                (v, _) => v, null, getLocalized, null, _ => false);
            Assert.Equal("I saw себя in the mirror.", resSelf.PlainText);

            // 2. 感想句：五個記號（{he}／{He}／{him}／{his}／{himself}）查同一組鍵
            var dMale = new FeelingDecision
            {
                LineKey = "feel_ru",
                FocusHeroId = "hero_f",
                FocusIsFemale = false
            };
            var dFemale = new FeelingDecision
            {
                LineKey = "feel_ru",
                FocusHeroId = "hero_f",
                FocusIsFemale = true
            };
            var dUnk = new FeelingDecision
            {
                LineKey = "feel_ru",
                FocusHeroId = "hero_f",
                FocusIsFemale = null
            };

            Func<string?, string?, string> getTemplate = (id, fb) => id == "feel_ru"
                ? "{He} is here. I saw {he} with {his} friend, helped {him}, by {himself}."
                : (fb ?? string.Empty);

            string? feelMale = RumorTextAssembler.RenderFeeling(dMale, false, (v, _) => v, getTemplate, getLocalized, ".", out _);
            Assert.Equal("Он is here. I saw он with его_poss friend, helped его, by сам.", feelMale);

            string? feelFemale = RumorTextAssembler.RenderFeeling(dFemale, false, (v, _) => v, getTemplate, getLocalized, ".", out _);
            Assert.Equal("Она is here. I saw она with её_poss friend, helped её, by сама.", feelFemale);

            // 焦點人物查不到性別時當成男性（用 _M）
            string? feelUnk = RumorTextAssembler.RenderFeeling(dUnk, false, (v, _) => v, getTemplate, getLocalized, ".", out _);
            Assert.Equal("Он is here. I saw он with его_poss friend, helped его, by сам.", feelUnk);
        }

        [Fact]
        public void Pronouns_WhenNoStringTableProvided_MatchesCurrentEnglishDefaults()
        {
            var roles = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["PRISONER"] = "hero_p"
            };

            // 事實句：男性
            var rMale = new ComposedRumor
            {
                Roles = roles,
                Parts = new[]
                {
                    new ComposedFactPart
                    {
                        TextId = "part1",
                        Fallback = "{PRISONER.He} saw {PRISONER.him}, took {PRISONER.his} weapon, by {PRISONER.himself}."
                    }
                }
            };
            var resMale = RumorTextAssembler.Assemble(
                rMale, new PresentationConfig { WholeSentences = false },
                (v, _) => v, null, null, null, id => false);
            Assert.Equal("He saw him, took his weapon, by himself.", resMale.PlainText);

            // 事實句：女性
            var rFemale = new ComposedRumor
            {
                Roles = roles,
                Parts = new[]
                {
                    new ComposedFactPart
                    {
                        TextId = "part1",
                        Fallback = "{PRISONER.He} saw {PRISONER.him}, took {PRISONER.his} weapon, by {PRISONER.himself}."
                    }
                }
            };
            var resFemale = RumorTextAssembler.Assemble(
                rFemale, new PresentationConfig { WholeSentences = false },
                (v, _) => v, null, null, null, id => true);
            Assert.Equal("She saw her, took her weapon, by herself.", resFemale.PlainText);

            // 事實句：查不到性別退成 they 一組
            var rUnk = new ComposedRumor
            {
                Roles = roles,
                Parts = new[]
                {
                    new ComposedFactPart
                    {
                        TextId = "part1",
                        Fallback = "{PRISONER.He} saw {PRISONER.him}, took {PRISONER.his} weapon, by {PRISONER.himself}."
                    }
                }
            };
            var resUnk = RumorTextAssembler.Assemble(
                rUnk, new PresentationConfig { WholeSentences = false },
                (v, _) => v, null, null, null, _ => null);
            Assert.Equal("They saw them, took their weapon, by themselves.", resUnk.PlainText);

            // 說話者是該角色時自身反身代名詞為 myself
            var rSpeaker = new ComposedRumor
            {
                Roles = roles,
                SpeakerHeroId = "hero_p",
                SpeakerRole = "PRISONER",
                Parts = new[]
                {
                    new ComposedFactPart
                    {
                        TextId = "part1",
                        Fallback = "I did it {PRISONER.himself}."
                    }
                }
            };
            var resSpeaker = RumorTextAssembler.Assemble(
                rSpeaker, new PresentationConfig { WholeSentences = false },
                (v, _) => v, null, null, null, _ => false);
            Assert.Equal("I did it myself.", resSpeaker.PlainText);

            // 感想句：getLocalized 為 null
            var dMale = new FeelingDecision { LineKey = "feel_en", FocusIsFemale = false };
            var dFemale = new FeelingDecision { LineKey = "feel_en", FocusIsFemale = true };
            Func<string?, string?, string> getTemplate = (id, fb) => "{He} said {he} saw {himself}, and {his} friend helped {him}.";

            string? outMale = RumorTextAssembler.RenderFeeling(dMale, false, (v, _) => v, getTemplate, null, ".", out _);
            Assert.Equal("He said he saw himself, and his friend helped him.", outMale);

            string? outFemale = RumorTextAssembler.RenderFeeling(dFemale, false, (v, _) => v, getTemplate, null, ".", out _);
            Assert.Equal("She said she saw herself, and her friend helped her.", outFemale);
        }

        [Fact]
        public void Pronouns_ShippingTraditionalChineseTable_RendersHeAndHimself_MatchesCurrentBehavior()
        {
            string repoRoot = FindRepoRoot();
            string cntPath = Path.Combine(repoRoot, "module", "ModuleData", "Languages", "CNt", "std_module_strings_xml.xml");
            Assert.True(File.Exists(cntPath), $"CNt table not found: {cntPath}");

            var doc = XDocument.Load(cntPath);
            var dict = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var elem in doc.Root?.Element("strings")?.Elements("string") ?? Enumerable.Empty<XElement>())
            {
                string? id = elem.Attribute("id")?.Value;
                if (!string.IsNullOrEmpty(id)) dict[id!] = elem.Attribute("text")?.Value ?? string.Empty;
            }

            Func<string?, string, string> getLocalized = (k, fb) => dict.TryGetValue(k ?? string.Empty, out var v) ? v : fb;

            // 測試包含 {he} 與 {himself} 的感想句
            var dMale = new FeelingDecision
            {
                LineKey = "test_zh_line",
                FocusIsFemale = false
            };
            var dFemale = new FeelingDecision
            {
                LineKey = "test_zh_line",
                FocusIsFemale = true
            };

            Func<string?, string?, string> getTemplate = (id, fb) => id == "test_zh_line"
                ? "這件事{he}自己心裡清楚，{he}可少不了要為{himself}討個公道"
                : (fb ?? string.Empty);

            string? maleResult = RumorTextAssembler.RenderFeeling(dMale, false, (v, _) => v, getTemplate, getLocalized, "。", out _);
            Assert.Equal("這件事他自己心裡清楚，他可少不了要為他自己討個公道。", maleResult);

            string? femaleResult = RumorTextAssembler.RenderFeeling(dFemale, false, (v, _) => v, getTemplate, getLocalized, "。", out _);
            Assert.Equal("這件事她自己心裡清楚，她可少不了要為她自己討個公道。", femaleResult);
        }

        [Fact]
        public void GenderSelect_RolesAndMeAndFocus_AndSixCallSites()
        {
            var roles = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["PRISONER"] = "hero_prisoner"
            };

            Func<string, bool?> isFemaleLookup = id => id switch
            {
                "hero_prisoner_m" => false,
                "hero_prisoner_f" => true,
                "hero_spk_m" => false,
                "hero_spk_f" => true,
                "hero_focus_m" => false,
                "hero_focus_f" => true,
                _ => null
            };

            // 1. 角色男、女、查不到（有第三段、沒第三段）
            string tplRole = "{PRISONER:он|она}";
            string tplRole3 = "{PRISONER:он|она|оно}";

            var rumorM = new ComposedRumor { Roles = new Dictionary<string, string> { ["PRISONER"] = "hero_prisoner_m" } };
            var rumorF = new ComposedRumor { Roles = new Dictionary<string, string> { ["PRISONER"] = "hero_prisoner_f" } };
            var rumorUnk = new ComposedRumor { Roles = new Dictionary<string, string> { ["PRISONER"] = "hero_unknown" } };

            Assert.Equal("он", RumorTextAssembler.ApplyGenderSelectTokens(tplRole, rumorM, null, null, isFemaleLookup, null, null, "key"));
            Assert.Equal("она", RumorTextAssembler.ApplyGenderSelectTokens(tplRole, rumorF, null, null, isFemaleLookup, null, null, "key"));
            Assert.Equal("оно", RumorTextAssembler.ApplyGenderSelectTokens(tplRole3, rumorUnk, null, null, isFemaleLookup, null, null, "key"));
            Assert.Equal("он", RumorTextAssembler.ApplyGenderSelectTokens(tplRole, rumorUnk, null, null, isFemaleLookup, null, null, "key"));

            // 2. ME：看說話的人性別（男、女、查不到）
            string tplMe = "{ME:рад|рада}";
            string tplMe3 = "{ME:рад|рада|радо}";

            Assert.Equal("рад", RumorTextAssembler.ApplyGenderSelectTokens(tplMe, null, null, "hero_spk_m", isFemaleLookup, null, null, "key"));
            Assert.Equal("рада", RumorTextAssembler.ApplyGenderSelectTokens(tplMe, null, null, "hero_spk_f", isFemaleLookup, null, null, "key"));
            Assert.Equal("радо", RumorTextAssembler.ApplyGenderSelectTokens(tplMe3, null, null, "hero_unknown", isFemaleLookup, null, null, "key"));
            Assert.Equal("рад", RumorTextAssembler.ApplyGenderSelectTokens(tplMe, null, null, "hero_unknown", isFemaleLookup, null, null, "key"));
            Assert.Equal("радо", RumorTextAssembler.ApplyGenderSelectTokens(tplMe3, null, null, null, isFemaleLookup, null, null, "key"));

            // 3. FOCUS：看感想句焦點人物性別（男、女、查不到）
            string tplFocus = "{FOCUS:сбежал|сбежала}";
            string tplFocus3 = "{FOCUS:сбежал|сбежала|сбежало}";

            var dFocusM = new FeelingDecision { FocusHeroId = "hero_focus_m" };
            var dFocusF = new FeelingDecision { FocusHeroId = "hero_focus_f" };
            var dFocusFallbackF = new FeelingDecision { FocusHeroId = "hero_unknown", FocusIsFemale = true };
            var dFocusUnk = new FeelingDecision { FocusHeroId = "hero_unknown", FocusIsFemale = null };

            Assert.Equal("сбежал", RumorTextAssembler.ApplyGenderSelectTokens(tplFocus, null, dFocusM, null, isFemaleLookup, null, null, "key"));
            Assert.Equal("сбежала", RumorTextAssembler.ApplyGenderSelectTokens(tplFocus, null, dFocusF, null, isFemaleLookup, null, null, "key"));
            Assert.Equal("сбежала", RumorTextAssembler.ApplyGenderSelectTokens(tplFocus, null, dFocusFallbackF, null, isFemaleLookup, null, null, "key"));
            Assert.Equal("сбежало", RumorTextAssembler.ApplyGenderSelectTokens(tplFocus3, null, dFocusUnk, null, isFemaleLookup, null, null, "key"));
            Assert.Equal("сбежал", RumorTextAssembler.ApplyGenderSelectTokens(tplFocus, null, dFocusUnk, null, isFemaleLookup, null, null, "key"));

            // 4. 說話的人就是那個角色時也一樣照性別選字，不換成第一人稱
            var rumorSelfF = new ComposedRumor
            {
                Roles = new Dictionary<string, string> { ["PRISONER"] = "hero_spk_f" },
                SpeakerHeroId = "hero_spk_f",
                SpeakerRole = "PRISONER"
            };
            string tplSelf = "Я {PRISONER:бежал|бежала}.";
            string resSelfF = RumorTextAssembler.ApplyGenderSelectTokens(tplSelf, rumorSelfF, null, rumorSelfF.SpeakerHeroId, isFemaleLookup, null, null, "key");
            Assert.Equal("Я бежала.", resSelfF);

            // 5. 空字串段：{PRISONER:|а}
            string tplEmpty = "{PRISONER:|а}";
            Assert.Equal("", RumorTextAssembler.ApplyGenderSelectTokens(tplEmpty, rumorM, null, null, isFemaleLookup, null, null, "key"));
            Assert.Equal("а", RumorTextAssembler.ApplyGenderSelectTokens(tplEmpty, rumorF, null, null, isFemaleLookup, null, null, "key"));

            // 6. 六處適用範圍各至少一條測試：
            // (a) 整句事實句 (whole sentence)
            var rWhole = new ComposedRumor
            {
                Roles = new Dictionary<string, string> { ["PRISONER"] = "hero_prisoner_f" },
                SentenceKeyCandidates = new[] { "VividWorld_Sentence_TestWhole" },
                Parts = new[] { new ComposedFactPart { TextId = "p_dummy", Fallback = "dummy" } }
            };
            var resWhole = RumorTextAssembler.Assemble(
                rWhole, new PresentationConfig { WholeSentences = true },
                (v, _) => v,
                (id, _) => id == "VividWorld_Sentence_TestWhole" ? "{PRISONER:Бежал|Бежала} из плена." : "",
                null, null, isFemaleLookup);
            Assert.Equal("Бежала из плена.", resWhole.PlainText);

            // (b) 拼接碎片 (concatenated fragments)
            var rConcat = new ComposedRumor
            {
                Roles = new Dictionary<string, string> { ["PRISONER"] = "hero_prisoner_m" },
                Parts = new[]
                {
                    new ComposedFactPart { TextId = "part_c", Fallback = "{PRISONER:Бежал|Бежала} вчера" }
                }
            };
            var resConcat = RumorTextAssembler.Assemble(
                rConcat, new PresentationConfig { WholeSentences = false },
                (v, _) => v, null, null, null, isFemaleLookup);
            Assert.Equal("Бежал вчера.", resConcat.PlainText);

            // (c) 開頭語 (RenderPrefix)
            var rPrefix = new ComposedRumor
            {
                SpeakerHeroId = "hero_spk_f",
                PrefixTextId = "pref_test",
                PrefixFallback = "Я {ME:видел|видела}:",
                Parts = new[]
                {
                    new ComposedFactPart { TextId = "p", Fallback = "факт" }
                }
            };
            var resPrefix = RumorTextAssembler.Assemble(
                rPrefix, new PresentationConfig { WholeSentences = false },
                (v, _) => v, null, null, null, isFemaleLookup);
            Assert.Equal("Я видела: факт.", resPrefix.PlainText);

            // (d) 當事人句尾 (self feeling)
            var rSelfFeeling = new ComposedRumor
            {
                Roles = new Dictionary<string, string> { ["PRISONER"] = "hero_spk_f" },
                SpeakerHeroId = "hero_spk_f",
                SpeakerRole = "PRISONER",
                SentenceKeyCandidates = new[] { "sent_k" },
                SelfFeelingKeyCandidates = new[] { "self_feel_k" },
                Parts = new[] { new ComposedFactPart { TextId = "p_dummy", Fallback = "dummy" } }
            };
            var resSelfFeeling = RumorTextAssembler.Assemble(
                rSelfFeeling, new PresentationConfig { WholeSentences = true },
                (v, _) => v,
                (id, _) => id switch
                {
                    "sent_k" => "Я на свободе.",
                    "self_feel_k" => "Я {PRISONER:зол|зла} на них.",
                    _ => ""
                },
                null, null, isFemaleLookup);
            Assert.Equal("Я на свободе. Я зла на них.", resSelfFeeling.PlainText);

            // (e) 收尾句 (closing)
            var rClosing = new ComposedRumor
            {
                SpeakerHeroId = "hero_spk_f",
                SentenceKeyCandidates = new[] { "sent_close" },
                ClosingKey = "closing_k",
                Parts = new[] { new ComposedFactPart { TextId = "p_dummy", Fallback = "dummy" } }
            };
            var resClosing = RumorTextAssembler.Assemble(
                rClosing, new PresentationConfig { WholeSentences = true },
                (v, _) => v,
                (id, _) => id switch
                {
                    "sent_close" => "Случилось важное дело.",
                    "closing_k" => "Я {ME:устал|устала} говорить.",
                    _ => ""
                },
                null, null, isFemaleLookup);
            Assert.Equal("Случилось важное дело. Я устала говорить.", resClosing.PlainText);

            // (f) 感想句 (RenderFeeling)
            var dFeel = new FeelingDecision
            {
                LineKey = "feel_six",
                FocusHeroId = "hero_focus_f",
                FocusIsFemale = true
            };
            string? feelRes = RumorTextAssembler.RenderFeeling(
                dFeel, false, (v, _) => v,
                (id, _) => id == "feel_six" ? "{FOCUS:Он сбежал|Она сбежала}, и я {ME:рад|рада}." : "",
                null, ".", out _, isFemaleLookup, "hero_spk_m");
            Assert.Equal("Она сбежала, и я рад.", feelRes);
        }

        [Fact]
        public void GenderSelect_SyntaxErrors_EmitWarning_AndProduceExpectedOutput()
        {
            var warnings = new List<string>();
            Action<string> onWarning = w => warnings.Add(w);

            var roles = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["PRISONER"] = "hero_p"
            };
            var rumor = new ComposedRumor
            {
                Roles = roles,
                Parts = new[]
                {
                    new ComposedFactPart
                    {
                        TextId = "part_err",
                        Fallback = "Text: {PRISONER:only} and {UNKNOWN_ROLE:first|second}."
                    }
                }
            };

            // Assemble runs display pass (onWarning is null) and plain text pass (onWarning is passed).
            // Warnings must be emitted EXACTLY once per error.
            var res = RumorTextAssembler.Assemble(
                rumor, new PresentationConfig { WholeSentences = false },
                (v, _) => v, null, null, onWarning, _ => false);

            Assert.Equal("Text: only and first.", res.PlainText);
            Assert.Equal(2, warnings.Count);

            // 錯誤 1: 只有一段 -> 輸出那一段
            Assert.Contains(warnings, w => w.Contains("part_err") && w.Contains("{PRISONER:only}") && w.Contains("only one segment") && w.Contains("rendered as \"only\""));

            // 錯誤 2: 名字認不得 -> 輸出第一段
            Assert.Contains(warnings, w => w.Contains("part_err") && w.Contains("{UNKNOWN_ROLE:first|second}") && w.Contains("UNKNOWN_ROLE") && w.Contains("rendered as \"first\""));
        }

        [Fact]
        public void ShippingTemplatesAndStrings_DoNotContainReservedRolesOrGenderTokens()
        {
            string repoRoot = FindRepoRoot();

            // 1. 出貨模板不得有叫 ME、YOU 或 FOCUS 的角色（大小寫不敏感）
            foreach (string file in new[] { "vividworld_events.json", "vividworld_situation_events.json" })
            {
                string path = Path.Combine(repoRoot, "module", "ModuleData", file);
                Assert.True(File.Exists(path), $"Template file not found: {path}");

                var jarr = JArray.Parse(File.ReadAllText(path));
                int rolesSeen = 0;
                foreach (var item in jarr)
                {
                    string templateType = (string?)item["type"] ?? "unknown";
                    // roles 是「角色名 → 佔位符」的物件；選字記號認的是佔位符那個名字，兩邊都檢查
                    var roles = item["roles"] as JObject;
                    if (roles == null) continue;
                    foreach (var role in roles.Properties())
                    {
                        rolesSeen++;
                        string placeholder = ((string?)role.Value ?? "").Trim('{', '}');
                        foreach (string roleName in new[] { role.Name, placeholder })
                        {
                            Assert.False(string.Equals(roleName, "ME", StringComparison.OrdinalIgnoreCase)
                                || string.Equals(roleName, "YOU", StringComparison.OrdinalIgnoreCase)
                                || string.Equals(roleName, "FOCUS", StringComparison.OrdinalIgnoreCase),
                                $"Template '{templateType}' has reserved role name '{roleName}' in {file}");
                        }
                    }
                }
                Assert.True(rolesSeen > 0, $"No roles were read from {file} - the check would pass without checking anything");
            }

            // 2. 兩份出貨字串表都不准開始使用選字記號（{[^{}:]+:[^{}]*}）
            var tokenRegex = new System.Text.RegularExpressions.Regex(@"\{[^{}:]+:[^{}]*\}");

            string enPath = Path.Combine(repoRoot, "module", "ModuleData", "Languages", "std_module_strings_xml.xml");
            string cntPath = Path.Combine(repoRoot, "module", "ModuleData", "Languages", "CNt", "std_module_strings_xml.xml");

            foreach (string path in new[] { enPath, cntPath })
            {
                Assert.True(File.Exists(path), $"String file not found: {path}");
                var doc = XDocument.Load(path);
                foreach (var elem in doc.Root?.Element("strings")?.Elements("string") ?? Enumerable.Empty<XElement>())
                {
                    string id = elem.Attribute("id")?.Value ?? "";
                    string text = elem.Attribute("text")?.Value ?? "";
                    // 打探的回答句子是第一批用選字記號的出貨字串：只放行這一批、而且只放行 SOURCE 這個名字
                    // （組字函式 AssembleProbeResponse 認得它；其餘字串仍然不准用）。
                    var match = tokenRegex.Match(text);
                    if (match.Success && id.StartsWith("VividWorld_Probe_", StringComparison.Ordinal))
                    {
                        string name = match.Value.Substring(1, match.Value.IndexOf(':') - 1).Trim();
                        if (string.Equals(name, "SOURCE", StringComparison.Ordinal)) continue;
                    }
                    Assert.False(match.Success,
                        $"Shipping string '{id}' in {Path.GetFileName(path)} must not use gender select tokens yet: found '{match.Value}'");
                }
            }
        }
    }
}
