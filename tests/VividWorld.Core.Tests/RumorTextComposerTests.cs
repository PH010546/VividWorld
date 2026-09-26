using System;
using System.Collections.Generic;
using System.Linq;
using VividWorld.Core.Config;
using VividWorld.Core.Events;
using VividWorld.Core.Presentation;
using Xunit;

namespace VividWorld.Core.Tests
{
    public class RumorTextComposerTests
    {
        [Fact]
        public void Compose_OrdersByFactOrder()
        {
            var cfg = new PresentationConfig();
            // 預設 cfg.FactOrder: { "WHO", "CONTEXT", "WHEN", "WHERE", "WHAT", "WHY", "OUTCOME" }
            var evt = new WorldEvent { EventId = "evt_test", Day = 10.0 };
            var facts = new List<Fact>
            {
                new() { Id = "f_outcome", Category = FactCategory.Outcome, Text = "outcome text" },
                new() { Id = "f_who", Category = FactCategory.Who, Text = "who text" },
                new() { Id = "f_when", Category = FactCategory.When, Text = "when text" },
                new() { Id = "f_what", Category = FactCategory.What, Text = "what text" },
            };

            var composed = RumorTextComposer.Compose(evt, facts, cfg, isRetell: false);

            Assert.Equal(4, composed.Parts.Count);
            Assert.Equal("who text", composed.Parts[0].Fallback);
            Assert.Equal("when text", composed.Parts[1].Fallback);
            Assert.Equal("what text", composed.Parts[2].Fallback);
            Assert.Equal("outcome text", composed.Parts[3].Fallback);
        }

        [Fact]
        public void Compose_KeepsAuthoredOrderWithinACategory()
        {
            var cfg = new PresentationConfig();
            var evt = new WorldEvent { EventId = "evt_test", Day = 10.0 };
            var facts = new List<Fact>
            {
                new() { Id = "f_what_step1", Category = FactCategory.What, Text = "first action" },
                new() { Id = "f_who", Category = FactCategory.Who, Text = "the actor" },
                new() { Id = "f_what_step2", Category = FactCategory.What, Text = "second action" },
                new() { Id = "f_what_step3", Category = FactCategory.What, Text = "third action" },
            };

            var composed = RumorTextComposer.Compose(evt, facts, cfg, isRetell: false);

            Assert.Equal(4, composed.Parts.Count);
            Assert.Equal("the actor", composed.Parts[0].Fallback);
            Assert.Equal("first action", composed.Parts[1].Fallback);
            Assert.Equal("second action", composed.Parts[2].Fallback);
            Assert.Equal("third action", composed.Parts[3].Fallback);
        }

        [Fact]
        public void Compose_EmptyInput_ReturnsEmptyParts()
        {
            var cfg = new PresentationConfig();
            var evt = new WorldEvent { EventId = "evt_test", Day = 10.0 };

            var composed1 = RumorTextComposer.Compose(evt, Array.Empty<Fact>(), cfg, isRetell: false);
            Assert.NotNull(composed1);
            Assert.Empty(composed1.Parts);

            var composed2 = RumorTextComposer.Compose(evt, null!, cfg, isRetell: false);
            Assert.NotNull(composed2);
            Assert.Empty(composed2.Parts);
        }

        [Fact]
        public void Compose_Retell_AddsPrefix()
        {
            var cfg = new PresentationConfig();
            var evt = new WorldEvent { EventId = "evt_test", Day = 10.0 };
            var facts = new List<Fact>
            {
                new() { Id = "f_who", Category = FactCategory.Who, Text = "someone" }
            };

            var notRetell = RumorTextComposer.Compose(evt, facts, cfg, isRetell: false);
            Assert.Null(notRetell.PrefixTextId);
            Assert.Null(notRetell.PrefixFallback);

            var retell = RumorTextComposer.Compose(evt, facts, cfg, isRetell: true);
            Assert.Equal(RumorTextComposer.RetellPrefixTextId, retell.PrefixTextId);
            Assert.Equal("VividWorld_RetellPrefix", retell.PrefixTextId);
            Assert.Equal(RumorTextComposer.RetellPrefixFallback, retell.PrefixFallback);
            Assert.Equal("I was there, in fact—", retell.PrefixFallback);
        }

        [Fact]
        public void Compose_NeverReturnsAString()
        {
            var methods = typeof(RumorTextComposer).GetMethods().Where(m => m.Name == "Compose").ToList();
            Assert.NotEmpty(methods);
            foreach (var method in methods)
            {
                Assert.Equal(typeof(ComposedRumor), method.ReturnType);
                Assert.NotEqual(typeof(string), method.ReturnType);
            }
        }
    }
}
