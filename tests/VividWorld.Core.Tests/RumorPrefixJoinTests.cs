using System.Collections.Generic;
using VividWorld.Core.Config;
using VividWorld.Core.Presentation;
using Xunit;

namespace VividWorld.Core.Tests
{
    /// <summary>
    /// 重述前綴語與本文的接縫（規格 §9.3）。
    /// 中日韓文字之間不加空白：繁中前綴語結尾是全形破折號，硬插半形空格會變成
    /// 「當時我也在場—— 索埃拉斯與曼格斯…」（實機 log-20260910-ms1.txt）。
    /// </summary>
    public class RumorPrefixJoinTests
    {
        private static PresentationConfig Cfg() => new PresentationConfig
        {
            EncyclopediaLinksEnabled = false,
            FactSeparator = "，",
            SentenceEnd = "。"
        };

        private static string Assemble(string prefix, string body, string separator = "，", string sentenceEnd = "。")
        {
            var cfg = new PresentationConfig
            {
                EncyclopediaLinksEnabled = false,
                FactSeparator = separator,
                SentenceEnd = sentenceEnd
            };

            var rumor = new ComposedRumor
            {
                PrefixFallback = prefix,
                Parts = new List<ComposedFactPart>
                {
                    new ComposedFactPart { TextId = string.Empty, Fallback = body, Vars = new Dictionary<string, string>() }
                }
            };

            // getLocalized 為 null ⇒ 前綴語直接用 PrefixFallback，正是離線要釘的那一段
            return RumorTextAssembler.Assemble(rumor, cfg, (val, links) => val).PlainText;
        }

        [Fact]
        public void PrefixJoin_ChinesePrefixAndChineseBody_HasNoSpaceBetween()
        {
            string text = Assemble("事實上，當時我也在場——", "索埃拉斯與曼格斯進行了慘烈的決鬥");
            Assert.Equal("事實上，當時我也在場——索埃拉斯與曼格斯進行了慘烈的決鬥。", text);
            Assert.DoesNotContain("—— ", text);
        }

        [Fact]
        public void PrefixJoin_EnglishPrefixAndEnglishBody_KeepsTheSpace()
        {
            string text = Assemble("I was there, in fact—", "Meirag murdered Deiltus in cold blood", ", ", ".");
            Assert.Equal("I was there, in fact— Meirag murdered Deiltus in cold blood.", text);
        }

        [Fact]
        public void PrefixJoin_ChinesePrefixAndEnglishBody_KeepsTheSpace()
        {
            // 字串表翻了前綴語、碎片還是英文後備（只翻一半的語言會長這樣）。
            // 繁中前綴語結尾的破折號是 U+2014，不在中日韓區段，而後面接的是拉丁字
            // ⇒ 兩邊都不是中日韓 ⇒ 照規則留空白，這裡本來就該留。
            string text = Assemble("事實上，當時我也在場——", "Meirag murdered Deiltus in cold blood");
            Assert.Equal("事實上，當時我也在場—— Meirag murdered Deiltus in cold blood。", text);
        }

        [Fact]
        public void PrefixJoin_EnglishPrefixAndChineseBody_HasNoSpaceBetween()
        {
            string text = Assemble("I was there, in fact—", "梅拉格殘忍地謀殺了戴爾圖斯");
            Assert.Equal("I was there, in fact—梅拉格殘忍地謀殺了戴爾圖斯。", text);
        }

        // 中文介面下推給 AI 的英文句子：開頭語是英文、人名是遊戲介面上的中文。
        // 前綴語以半形字元結尾就要留空白（實機 AI 推送預覽印出 "that科爾"、"myself:烏德里斯"）。
        [Theory]
        [InlineData("I saw it myself:", "I saw it myself: 烏德里斯 slipped out of captivity.")]
        [InlineData("Later, I heard it said that", "Later, I heard it said that 烏德里斯 slipped out of captivity.")]
        public void PrefixJoin_EnglishPrefixEndingInAsciiAndChineseName_KeepsTheSpace(string prefix, string expected)
        {
            string text = Assemble(prefix, "烏德里斯 slipped out of captivity", ", ", ".");
            Assert.Equal(expected, text);
        }

        [Fact]
        public void PrefixJoin_PrefixAlreadyEndsWithSpace_DoesNotAddASecondOne()
        {
            string text = Assemble("I was there, in fact— ", "Meirag murdered Deiltus in cold blood", ", ", ".");
            Assert.Equal("I was there, in fact— Meirag murdered Deiltus in cold blood.", text);
        }

        [Fact]
        public void PrefixJoin_NoPrefix_LeavesTheBodyAlone()
        {
            var cfg = Cfg();
            var rumor = new ComposedRumor
            {
                Parts = new List<ComposedFactPart>
                {
                    new ComposedFactPart { TextId = string.Empty, Fallback = "梅拉格殘忍地謀殺了戴爾圖斯", Vars = new Dictionary<string, string>() }
                }
            };

            Assert.Equal("梅拉格殘忍地謀殺了戴爾圖斯。",
                RumorTextAssembler.Assemble(rumor, cfg, (val, links) => val).PlainText);
        }

        [Fact]
        public void PrefixJoin_EyewitnessChinese_HasNoSpace()
        {
            string text = Assemble("我親眼看到的：", "索埃拉斯與曼格斯進行了慘烈的決鬥");
            Assert.Equal("我親眼看到的：索埃拉斯與曼格斯進行了慘烈的決鬥。", text);
        }

        [Fact]
        public void PrefixJoin_EyewitnessEnglish_KeepsTheSpace()
        {
            string text = Assemble("I saw it myself:", "Meirag murdered Deiltus in cold blood", ", ", ".");
            Assert.Equal("I saw it myself: Meirag murdered Deiltus in cold blood.", text);
        }

        [Fact]
        public void PrefixJoin_HeardFromSourceChinese_HasNoSpace()
        {
            var cfg = Cfg();
            var rumor = new ComposedRumor
            {
                PrefixFallback = "聽{SOURCE}說，",
                PrefixVars = new Dictionary<string, string> { ["SOURCE"] = "hero:derthert" },
                Parts = new List<ComposedFactPart>
                {
                    new ComposedFactPart { TextId = string.Empty, Fallback = "索埃拉斯與曼格斯進行了慘烈的決鬥", Vars = new Dictionary<string, string>() }
                }
            };

            string text = RumorTextAssembler.Assemble(rumor, cfg, (val, links) => "德瑟特").PlainText;
            Assert.Equal("聽德瑟特說，索埃拉斯與曼格斯進行了慘烈的決鬥。", text);
        }

        [Fact]
        public void PrefixJoin_HeardFromSourceEnglish_KeepsTheSpace()
        {
            var cfg = new PresentationConfig
            {
                EncyclopediaLinksEnabled = false,
                FactSeparator = ", ",
                SentenceEnd = "."
            };
            var rumor = new ComposedRumor
            {
                PrefixFallback = "{SOURCE} told me that",
                PrefixVars = new Dictionary<string, string> { ["SOURCE"] = "hero:derthert" },
                Parts = new List<ComposedFactPart>
                {
                    new ComposedFactPart { TextId = string.Empty, Fallback = "Meirag murdered Deiltus in cold blood", Vars = new Dictionary<string, string>() }
                }
            };

            string text = RumorTextAssembler.Assemble(rumor, cfg, (val, links) => "Derthert").PlainText;
            Assert.Equal("{SOURCE} told me that Meirag murdered Deiltus in cold blood."
                .Replace("{SOURCE}", "Derthert"), text);
        }

        [Fact]
        public void Prefix_Variables_RenderLinksInDisplayTextAndPlainInPlainText()
        {
            var cfg = new PresentationConfig
            {
                EncyclopediaLinksEnabled = true,
                FactSeparator = ", ",
                SentenceEnd = "."
            };
            var rumor = new ComposedRumor
            {
                PrefixFallback = "{SOURCE} told me that",
                PrefixVars = new Dictionary<string, string> { ["SOURCE"] = "hero:derthert" },
                Parts = new List<ComposedFactPart>
                {
                    new ComposedFactPart { TextId = string.Empty, Fallback = "peace was declared", Vars = new Dictionary<string, string>() }
                }
            };

            string ResolveVar(string val, bool links)
            {
                if (val == "hero:derthert")
                {
                    return links ? "<a href=\"hero:derthert\">Derthert</a>" : "Derthert";
                }
                return val;
            }

            var result = RumorTextAssembler.Assemble(rumor, cfg, ResolveVar);
            Assert.Equal("<a href=\"hero:derthert\">Derthert</a> told me that peace was declared.", result.DisplayText);
            Assert.Equal("Derthert told me that peace was declared.", result.PlainText);
        }

        [Fact]
        public void Prefix_UnresolvedPlaceholder_EmitsWarningAndFallsBackToSomeone()
        {
            var cfg = new PresentationConfig
            {
                EncyclopediaLinksEnabled = false,
                FactSeparator = ", ",
                SentenceEnd = "."
            };
            var rumor = new ComposedRumor
            {
                PrefixTextId = "VividWorld_Prefix_HeardFromSource",
                PrefixFallback = "{SOURCE} told me that",
                PrefixVars = new Dictionary<string, string>(), // Empty vars -> unresolved SOURCE
                Parts = new List<ComposedFactPart>
                {
                    new ComposedFactPart { TextId = string.Empty, Fallback = "peace was declared", Vars = new Dictionary<string, string>() }
                }
            };

            var warnings = new List<string>();
            var result = RumorTextAssembler.Assemble(
                rumor,
                cfg,
                (val, links) => val,
                getTemplate: null,
                getLocalized: (id, fallback) => id == "VividWorld_UnknownSubject" ? "someone" : fallback,
                onWarning: warnings.Add);

            Assert.Equal("someone told me that peace was declared.", result.PlainText);
            Assert.Single(warnings);
            Assert.Contains("SOURCE", warnings[0]);
        }
    }
}
