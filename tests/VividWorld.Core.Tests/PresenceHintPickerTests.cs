#nullable enable
using VividWorld.Core.Presentation;
using Xunit;

namespace VividWorld.Core.Tests
{
    public class PresenceHintPickerTests
    {
        [Fact]
        public void HoveredWinsOverSelected()
        {
            Assert.Equal("hover", PresenceHintPicker.Pick("hover", "chosen"));
        }

        [Fact]
        public void SelectedShownWhenNothingHovered()
        {
            Assert.Equal("chosen", PresenceHintPicker.Pick(null, "chosen"));
        }

        [Fact]
        public void EmptyWhenNeither()
        {
            Assert.Equal(string.Empty, PresenceHintPicker.Pick(null, null));
        }

        [Fact]
        public void HoveredEmptyHintStillWins()
        {
            // 指著的那項沒有說明時顯示空白，不退回已選那項的說明
            Assert.Equal(string.Empty, PresenceHintPicker.Pick(string.Empty, "chosen"));
        }
    }
}
