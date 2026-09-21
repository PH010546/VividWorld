#nullable enable

using VividWorld.Core.Presentation;
using Xunit;

namespace VividWorld.Core.Tests
{
    /// <summary>
    /// 規格 §9.4：紀事視窗的熱鍵設定值可以帶修飾鍵（預設 Ctrl+L）。
    /// 這裡只驗字串怎麼拆——按鍵名是不是真的 InputKey 由 Module 側解，Core 不參照引擎。
    /// </summary>
    public class HotkeySpecTests
    {
        [Fact]
        public void Parse_PlainKey_HasNoModifiers()
        {
            var spec = HotkeySpec.Parse("F9");

            Assert.True(spec.IsValid);
            Assert.Equal("F9", spec.KeyName);
            Assert.False(spec.Ctrl);
            Assert.False(spec.Alt);
            Assert.False(spec.Shift);
            Assert.Empty(spec.Modifiers);
            Assert.Equal("F9", spec.ToString());
        }

        [Theory]
        [InlineData("Ctrl+L")]
        [InlineData("ctrl+l")]
        [InlineData("CONTROL + L")]
        [InlineData(" Ctrl +  L ")]
        public void Parse_AcceptsCtrlSpelledSeveralWays(string text)
        {
            var spec = HotkeySpec.Parse(text);

            Assert.True(spec.IsValid);
            Assert.True(spec.Ctrl);
            Assert.False(spec.Alt);
            Assert.False(spec.Shift);
            Assert.Equal("L", spec.KeyName.ToUpperInvariant());
        }

        [Fact]
        public void Parse_ManyModifiers_KeepsThemAll_AndPrintsInFixedOrder()
        {
            var spec = HotkeySpec.Parse("shift+alt+ctrl+K");

            Assert.True(spec.IsValid);
            Assert.True(spec.Ctrl);
            Assert.True(spec.Alt);
            Assert.True(spec.Shift);
            Assert.Equal("K", spec.KeyName);
            Assert.Equal(new[] { "Ctrl", "Alt", "Shift" }, spec.Modifiers);
            Assert.Equal("Ctrl+Alt+Shift+K", spec.ToString());
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData("Ctrl+")]          // 只有修飾鍵，沒有按鍵
        [InlineData("Ctrl+ ")]
        [InlineData("Meta+L")]         // 不認得的修飾鍵：不要猜，整串作廢
        [InlineData("L+Ctrl")]         // 反過來寫：L 被當成修飾鍵 ⇒ 不認得
        public void Parse_RejectsWhatItCannotRead(string? text)
        {
            var spec = HotkeySpec.Parse(text);

            Assert.False(spec.IsValid);
            Assert.Equal(string.Empty, spec.ToString());
        }

        [Fact]
        public void Create_RoundTripsThroughParse()
        {
            var made = HotkeySpec.Create("L", ctrl: true);
            var parsed = HotkeySpec.Parse(made.ToString());

            Assert.Equal("Ctrl+L", made.ToString());
            Assert.True(parsed.IsValid);
            Assert.True(parsed.Ctrl);
            Assert.Equal("L", parsed.KeyName);
        }
    }
}
