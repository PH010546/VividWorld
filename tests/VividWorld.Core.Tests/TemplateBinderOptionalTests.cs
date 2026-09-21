using System;
using System.Collections.Generic;
using System.Linq;
using VividWorld.Core.Catalog;
using VividWorld.Core.Events;
using VividWorld.Core.Ingest;
using Xunit;

namespace VividWorld.Core.Tests
{
    public class TemplateBinderOptionalTests
    {
        private static EventTemplate CreateTemplateWithOptionalWhere()
        {
            return new EventTemplate
            {
                Type = "test_event",
                Origin = EventOrigin.Public,
                DramaWeight = 3,
                Roles = new Dictionary<string, string>
                {
                    ["hero"] = "{HERO}"
                },
                KnowingRoles = new HashSet<string> { "hero" },
                Facts = new List<TemplateFact>
                {
                    new TemplateFact
                    {
                        Id = "fact_who",
                        Category = FactCategory.Who,
                        TextId = "VividWorld_Test_Who",
                        Text = "{HERO} did something.",
                        Vars = new Dictionary<string, string>
                        {
                            ["HERO"] = "hero:{HERO}"
                        },
                        Fragility = 1,
                        Optional = false
                    },
                    new TemplateFact
                    {
                        Id = "fact_where",
                        Category = FactCategory.Where,
                        TextId = "VividWorld_Test_Where",
                        Text = "This happened in {SETTLEMENT}.",
                        Vars = new Dictionary<string, string>
                        {
                            ["SETTLEMENT"] = "settlement:{SETTLEMENT}"
                        },
                        Fragility = 3,
                        Optional = true
                    }
                }
            };
        }

        [Fact]
        public void OptionalFact_UnboundPlaceholder_DropsFactAndKeepsTemplate()
        {
            var template = CreateTemplateWithOptionalWhere();
            var bindings = new Dictionary<string, string>
            {
                ["HERO"] = "hero_1"
                // SETTLEMENT is deliberately omitted
            };

            var sub = TemplateBinder.Bind(template, bindings, 10.0, null, out var issues);

            Assert.NotNull(sub);
            Assert.Single(sub!.Facts);
            Assert.Equal("fact_who", sub.Facts[0].Id);
            Assert.Equal(FactCategory.Who, sub.Facts[0].Category);
        }

        [Fact]
        public void OptionalFact_UnboundPlaceholder_EmitsWarningIssue()
        {
            var template = CreateTemplateWithOptionalWhere();
            var bindings = new Dictionary<string, string>
            {
                ["HERO"] = "hero_1"
                // SETTLEMENT omitted
            };

            var sub = TemplateBinder.Bind(template, bindings, 10.0, null, out var issues);

            Assert.NotNull(sub);
            var unboundIssue = issues.FirstOrDefault(i => i.Code == CatalogIssueCode.UnboundPlaceholder);
            Assert.NotNull(unboundIssue);
            Assert.False(unboundIssue!.IsError);
            Assert.Contains("SETTLEMENT", unboundIssue.Detail);
        }

        [Fact]
        public void RequiredFact_UnboundPlaceholder_FailsBinding()
        {
            var template = CreateTemplateWithOptionalWhere();
            var bindings = new Dictionary<string, string>
            {
                // HERO is required and omitted
                ["SETTLEMENT"] = "settlement_1"
            };

            var sub = TemplateBinder.Bind(template, bindings, 10.0, null, out var issues);

            Assert.Null(sub);
            var errorIssue = issues.FirstOrDefault(i => i.Code == CatalogIssueCode.UnboundPlaceholder && i.IsError);
            Assert.NotNull(errorIssue);
            Assert.Contains("HERO", errorIssue!.Detail);
        }

        [Fact]
        public void OptionalFact_AllFactsDropped_FailsBinding()
        {
            var template = new EventTemplate
            {
                Type = "all_optional",
                Origin = EventOrigin.Public,
                DramaWeight = 2,
                Roles = new Dictionary<string, string>(),
                Facts = new List<TemplateFact>
                {
                    new TemplateFact
                    {
                        Id = "fact_where",
                        Category = FactCategory.Where,
                        TextId = "VividWorld_Test_Where",
                        Text = "In {SETTLEMENT}.",
                        Vars = new Dictionary<string, string>
                        {
                            ["SETTLEMENT"] = "settlement:{SETTLEMENT}"
                        },
                        Fragility = 2,
                        Optional = true
                    }
                }
            };

            var bindings = new Dictionary<string, string>(); // SETTLEMENT missing

            var sub = TemplateBinder.Bind(template, bindings, 10.0, null, out var issues);

            Assert.Null(sub);
            var noFactsIssue = issues.FirstOrDefault(i => i.Code == CatalogIssueCode.NoFacts && i.IsError);
            Assert.NotNull(noFactsIssue);
        }

        [Fact]
        public void OptionalFact_BoundPlaceholder_KeepsFact()
        {
            var template = CreateTemplateWithOptionalWhere();
            var bindings = new Dictionary<string, string>
            {
                ["HERO"] = "hero_1",
                ["SETTLEMENT"] = "town_A"
            };

            var sub = TemplateBinder.Bind(template, bindings, 10.0, null, out var issues);

            Assert.NotNull(sub);
            Assert.Equal(2, sub!.Facts.Count);
            Assert.Equal("fact_who", sub.Facts[0].Id);
            Assert.Equal("fact_where", sub.Facts[1].Id);
            Assert.Empty(issues.Where(i => i.Code == CatalogIssueCode.UnboundPlaceholder));
        }
    }
}
