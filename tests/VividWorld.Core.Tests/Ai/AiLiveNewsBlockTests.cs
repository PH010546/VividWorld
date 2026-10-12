using System.Collections.Generic;
using VividWorld.Core.Ai;
using Xunit;

namespace VividWorld.Core.Tests.Ai
{
    public class AiLiveNewsBlockTests
    {
        [Fact]
        public void Build_NoItems_ReturnsEmptyString()
        {
            var result = AiLiveNewsBlock.Build("John", lean: false, new List<AiLiveNewsItem>());
            Assert.Equal(string.Empty, result.Text);
            Assert.Empty(result.IncludedEventIds);
            Assert.Empty(result.ExcludedItems);
            Assert.Equal(500, result.MaxChars);
        }

        [Fact]
        public void Build_SingleItem_StandardHeader_MatchesSpecFormat()
        {
            var items = new List<AiLiveNewsItem>
            {
                new AiLiveNewsItem("evt_1", AiMemoryKind.Hearsay, 0, "Calradog captured someone.")
            };

            var result = AiLiveNewsBlock.Build("John", lean: false, items);

            string expected = "What John knows right now about recent happenings in Calradia. This is current: where an older memory disagrees, go by this.\n" +
                              "- [heard, today] Calradog captured someone.";
            Assert.Equal(expected, result.Text);
            Assert.Equal(new[] { "evt_1" }, result.IncludedEventIds);
            Assert.Empty(result.ExcludedItems);
            Assert.Equal(500, result.MaxChars);
        }

        [Fact]
        public void Build_SingleItem_LeanHeader_MatchesSpecFormat()
        {
            var items = new List<AiLiveNewsItem>
            {
                new AiLiveNewsItem("evt_1", AiMemoryKind.OwnLife, 1, "I was released from captivity.")
            };

            var result = AiLiveNewsBlock.Build("John", lean: true, items);

            string expected = "John knows right now (current, overrides older memories):\n" +
                              "- [own life, 1 day ago] I was released from captivity.";
            Assert.Equal(expected, result.Text);
            Assert.Equal(new[] { "evt_1" }, result.IncludedEventIds);
            Assert.Empty(result.ExcludedItems);
            Assert.Equal(300, result.MaxChars);
        }

        [Fact]
        public void Build_TimeAndKindTags_Formattings()
        {
            var items = new List<AiLiveNewsItem>
            {
                new AiLiveNewsItem("evt_1", AiMemoryKind.Other, 5, "Fought together at sea.")
            };

            var result = AiLiveNewsBlock.Build("John", lean: false, items);

            string expected = "What John knows right now about recent happenings in Calradia. This is current: where an older memory disagrees, go by this.\n" +
                              "- [with the player, 5 days ago] Fought together at sea.";
            Assert.Equal(expected, result.Text);
        }

        [Fact]
        public void Build_FirstItemExceedsLimit_StillIncluded()
        {
            // First item length = "- [heard, today] " (17 chars) + 600 chars = 617 chars
            string longText = new string('A', 600);
            var items = new List<AiLiveNewsItem>
            {
                new AiLiveNewsItem("evt_first", AiMemoryKind.Hearsay, 0, longText),
                new AiLiveNewsItem("evt_second", AiMemoryKind.Hearsay, 0, "Short text")
            };

            var result = AiLiveNewsBlock.Build("John", lean: false, items);

            Assert.Single(result.IncludedEventIds);
            Assert.Equal("evt_first", result.IncludedEventIds[0]);
            Assert.Single(result.ExcludedItems);
            Assert.Equal("evt_second", result.ExcludedItems[0].EventId);
            Assert.Equal(617, result.ExcludedItems[0].UsedCharsBefore);
            Assert.Equal("- [heard, today] Short text".Length, result.ExcludedItems[0].ItemChars);
        }

        [Fact]
        public void Build_SkipsMiddleItemWhenTooLong_IncludesLaterShorterItem()
        {
            // Standard limit = 500 chars.
            // item1: line length 200
            // item2: line length 350 -> 200 + 1 + 350 = 551 > 500 (skipped)
            // item3: line length 50 -> 200 + 1 + 50 = 251 <= 500 (included)

            string text1 = new string('A', 200 - "- [heard, today] ".Length);
            string text2 = new string('B', 350 - "- [heard, today] ".Length);
            string text3 = new string('C', 50 - "- [heard, today] ".Length);

            var items = new List<AiLiveNewsItem>
            {
                new AiLiveNewsItem("evt_1", AiMemoryKind.Hearsay, 0, text1),
                new AiLiveNewsItem("evt_2", AiMemoryKind.Hearsay, 0, text2),
                new AiLiveNewsItem("evt_3", AiMemoryKind.Hearsay, 0, text3)
            };

            var result = AiLiveNewsBlock.Build("John", lean: false, items);

            Assert.Equal(new[] { "evt_1", "evt_3" }, result.IncludedEventIds);
            Assert.Single(result.ExcludedItems);
            Assert.Equal("evt_2", result.ExcludedItems[0].EventId);
            Assert.Equal(350, result.ExcludedItems[0].ItemChars);
            Assert.Equal(200, result.ExcludedItems[0].UsedCharsBefore);
        }

        [Fact]
        public void Build_LimitCalculatesOnlyNewsLines_NotHeader()
        {
            // Limit is 500. Exactly 500 chars for news lines.
            // Line 1: 300 chars
            // Line 2: \n (1 char) + 199 chars = 200 chars. Total news chars = 500.
            string text1 = new string('A', 300 - "- [heard, today] ".Length);
            string text2 = new string('B', 199 - "- [heard, today] ".Length);

            var items = new List<AiLiveNewsItem>
            {
                new AiLiveNewsItem("evt_1", AiMemoryKind.Hearsay, 0, text1),
                new AiLiveNewsItem("evt_2", AiMemoryKind.Hearsay, 0, text2)
            };

            var result = AiLiveNewsBlock.Build("John", lean: false, items);

            Assert.Equal(2, result.IncludedEventIds.Count);
            Assert.Empty(result.ExcludedItems);

            // Verify lines only length
            string expectedHeader = string.Format(AiLiveNewsBlock.HeaderStandardTemplate, "John");
            Assert.StartsWith(expectedHeader + "\n", result.Text);
            string linesOnly = result.Text.Substring(expectedHeader.Length + 1); // strip header and first \n
            Assert.Equal(500, linesOnly.Length);
        }

        [Fact]
        public void Build_LeanUses300Limit()
        {
            // Lean limit = 300.
            // item1: 200 chars
            // item2: \n (1 char) + 120 chars = 121 chars -> Total 321 > 300 (skipped)
            string text1 = new string('A', 200 - "- [heard, today] ".Length);
            string text2 = new string('B', 120 - "- [heard, today] ".Length);

            var items = new List<AiLiveNewsItem>
            {
                new AiLiveNewsItem("evt_1", AiMemoryKind.Hearsay, 0, text1),
                new AiLiveNewsItem("evt_2", AiMemoryKind.Hearsay, 0, text2)
            };

            var result = AiLiveNewsBlock.Build("John", lean: true, items);

            Assert.Equal(300, result.MaxChars);
            Assert.Single(result.IncludedEventIds);
            Assert.Equal("evt_1", result.IncludedEventIds[0]);
            Assert.Single(result.ExcludedItems);
            Assert.Equal("evt_2", result.ExcludedItems[0].EventId);
        }
    }
}
