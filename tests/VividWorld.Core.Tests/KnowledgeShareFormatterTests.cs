using System;
using System.Collections.Generic;
using VividWorld.Core.Diagnostics;
using Xunit;

namespace VividWorld.Core.Tests
{
    public class KnowledgeShareFormatterTests
    {
        [Fact]
        public void KnowledgeShareFormatter_FormatsSortedAndPercentage()
        {
            // Total = 870 (540 + 150 + 100 + 80)
            // 540/870 = 62.1%
            // 150/870 = 17.2%
            // 100/870 = 11.5%
            // 80/870  = 9.2%
            var items = new List<(string Type, int Count)>
            {
                ("tavern_quarrel", 150),
                ("child_born", 80),
                ("hero_taken_prisoner", 540),
                ("duel", 100)
            };

            string result = KnowledgeShareFormatter.Format(items);

            Assert.Equal(
                "- Knowledge share by event type (hop entries): hero_taken_prisoner 540 (62.1%), tavern_quarrel 150 (17.2%), duel 100 (11.5%), child_born 80 (9.2%)",
                result);
        }

        [Fact]
        public void KnowledgeShareFormatter_WhenEmptyOrNull_ReturnsNoEvents()
        {
            Assert.Equal(
                "- Knowledge share by event type (hop entries): (no events)",
                KnowledgeShareFormatter.Format((List<(string, int)>?)null));

            Assert.Equal(
                "- Knowledge share by event type (hop entries): (no events)",
                KnowledgeShareFormatter.Format(new List<(string, int)>()));

            Assert.Equal(
                "- Knowledge share by event type (hop entries): (no events)",
                KnowledgeShareFormatter.Format(new Dictionary<string, int>()));
        }

        [Fact]
        public void KnowledgeShareFormatter_WhenSingleItem_Returns100Percent()
        {
            var items = new[] { ("hero_taken_prisoner", 42) };

            string result = KnowledgeShareFormatter.Format(items);

            Assert.Equal(
                "- Knowledge share by event type (hop entries): hero_taken_prisoner 42 (100.0%)",
                result);
        }

        [Fact]
        public void KnowledgeShareFormatter_WhenEqualCounts_SortsByTypeOrdinal()
        {
            var items = new List<(string Type, int Count)>
            {
                ("zebra_event", 10),
                ("alpha_event", 10),
                ("beta_event", 10)
            };

            string result = KnowledgeShareFormatter.Format(items);

            Assert.Equal(
                "- Knowledge share by event type (hop entries): alpha_event 10 (33.3%), beta_event 10 (33.3%), zebra_event 10 (33.3%)",
                result);
        }
    }
}
