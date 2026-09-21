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
    }
}
