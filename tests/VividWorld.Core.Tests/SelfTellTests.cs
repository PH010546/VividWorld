#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using VividWorld.Core.Catalog;
using VividWorld.Core.Config;
using VividWorld.Core.Dialogue;
using VividWorld.Core.Events;
using VividWorld.Core.Memory;
using VividWorld.Core.Rumors;
using VividWorld.Core.Tests.Fakes;
using VividWorld.Core.Util;
using Xunit;

namespace VividWorld.Core.Tests
{
    public class SelfTellTests
    {
        [Fact]
        public void SelfTellRule_RoundTrips_AndPreservesExtra()
        {
            string json = @"{ ""never"": true, ""customField"": 42 }";
            var rule = JsonConvert.DeserializeObject<SelfTellRule>(json);
            Assert.NotNull(rule);
            Assert.True(rule!.IsNever);
            Assert.True(rule.Extra.ContainsKey("customField"));
            Assert.Equal(42, (int)rule.Extra["customField"]);

            string reserialized = JsonConvert.SerializeObject(rule);
            Assert.Contains(@"""never"":true", reserialized.Replace(" ", ""));
            Assert.Contains(@"""customField"":42", reserialized.Replace(" ", ""));
        }

        [Fact]
        public void SelfTellEvaluator_NonParticipant_Allowed()
        {
            var evt = new WorldEvent
            {
                Type = "test_event",
                Origin = EventOrigin.Public,
                Participants = new Dictionary<string, string> { ["claimant"] = "hero_claimant" }
            };

            var template = new EventTemplate
            {
                Type = "test_event",
                Origin = EventOrigin.Public,
                SelfTell = new Dictionary<string, SelfTellRule>
                {
                    ["claimant"] = SelfTellRule.Never
                }
            };

            var res = SelfTellEvaluator.Evaluate(evt, "hero_outsider", _ => template, (IHeroTraitLookup?)null);
            Assert.True(res.CanTell);
            Assert.Null(res.ReasonText);
        }

        [Fact]
        public void SelfTellEvaluator_SecretEvent_AlwaysAllowed()
        {
            var evt = new WorldEvent
            {
                Type = "test_secret",
                Origin = EventOrigin.Secret,
                Participants = new Dictionary<string, string> { ["speaker"] = "hero_speaker" }
            };

            var template = new EventTemplate
            {
                Type = "test_secret",
                Origin = EventOrigin.Secret,
                SelfTell = new Dictionary<string, SelfTellRule>
                {
                    ["speaker"] = SelfTellRule.Never
                }
            };

            var res = SelfTellEvaluator.Evaluate(evt, "hero_speaker", _ => template, (IHeroTraitLookup?)null);
            Assert.True(res.CanTell);
        }

        [Fact]
        public void SelfTellEvaluator_NeverRule_ReturnsNever()
        {
            var evt = new WorldEvent
            {
                Type = "victory_credit_claimed",
                Origin = EventOrigin.Public,
                Participants = new Dictionary<string, string> { ["claimant"] = "hero_1" }
            };

            var template = new EventTemplate
            {
                Type = "victory_credit_claimed",
                Origin = EventOrigin.Public,
                SelfTell = new Dictionary<string, SelfTellRule>
                {
                    ["claimant"] = SelfTellRule.Never
                }
            };

            var res = SelfTellEvaluator.Evaluate(evt, "hero_1", _ => template, (IHeroTraitLookup?)null);
            Assert.False(res.CanTell);
            Assert.True(res.IsNever);
            Assert.Equal("claimant", res.Role);
            Assert.Equal("won't tell own (role claimant)", res.ReasonText);
        }

        [Fact]
        public void SelfTellEvaluator_TraitRule_TraitBelowMin_ReturnsTraitTooLow()
        {
            var evt = new WorldEvent
            {
                Type = "advice_mocked",
                Origin = EventOrigin.Public,
                Participants = new Dictionary<string, string> { ["student"] = "hero_student" }
            };

            var template = new EventTemplate
            {
                Type = "advice_mocked",
                Origin = EventOrigin.Public,
                SelfTell = new Dictionary<string, SelfTellRule>
                {
                    ["student"] = new SelfTellRule("valor", 1)
                }
            };

            var traits = new FakeHeroTraitLookup();
            traits.Set(new TraitProfile { HeroId = "hero_student", Valor = 0 });

            var res = SelfTellEvaluator.Evaluate(evt, "hero_student", _ => template, traits);
            Assert.False(res.CanTell);
            Assert.False(res.IsNever);
            Assert.Equal("student", res.Role);
            Assert.Equal("valor", res.Trait);
            Assert.Equal(0, res.ActualValue);
            Assert.Equal(1, res.MinValue);
            Assert.Equal("won't tell own (role student, valor 0 < 1)", res.ReasonText);
        }

        [Fact]
        public void SelfTellEvaluator_TraitRule_TraitAtOrAboveMin_Allowed()
        {
            var evt = new WorldEvent
            {
                Type = "advice_mocked",
                Origin = EventOrigin.Public,
                Participants = new Dictionary<string, string> { ["student"] = "hero_student" }
            };

            var template = new EventTemplate
            {
                Type = "advice_mocked",
                Origin = EventOrigin.Public,
                SelfTell = new Dictionary<string, SelfTellRule>
                {
                    ["student"] = new SelfTellRule("valor", 1)
                }
            };

            var traits = new FakeHeroTraitLookup();
            traits.Set(new TraitProfile { HeroId = "hero_student", Valor = 1 });

            var res = SelfTellEvaluator.Evaluate(evt, "hero_student", _ => template, traits);
            Assert.True(res.CanTell);
        }

        [Fact]
        public void EventCatalogLoader_ParsesSelfTell_Successfully()
        {
            string json = @"[
  {
    ""type"": ""test_self_tell"",
    ""headline"": ""Test"",
    ""origin"": ""public"",
    ""dramaWeight"": 3,
    ""roles"": { ""actor"": ""{ACTOR}"" },
    ""selfTell"": {
      ""actor"": { ""trait"": ""honor"", ""min"": 1 }
    },
    ""facts"": [
      { ""id"": ""who"", ""category"": ""WHO"", ""text"": ""{ACTOR} did something"", ""vars"": { ""ACTOR"": ""hero:{ACTOR}"" } }
    ]
  }
]";

            var catalog = EventCatalogLoader.Load(json, new PersistenceConfig());
            Assert.Empty(catalog.Issues.Where(i => i.IsError));
            Assert.Single(catalog.Templates);
            var tmpl = catalog.Templates[0];
            Assert.NotNull(tmpl.SelfTell);
            Assert.True(tmpl.SelfTell!.ContainsKey("actor"));
            Assert.Equal("honor", tmpl.SelfTell["actor"].Trait);
            Assert.Equal(1, tmpl.SelfTell["actor"].Min);
        }

        [Fact]
        public void EventCatalogLoader_SelfTell_UnknownRole_ReportsSelfTellInvalid()
        {
            string json = @"[
  {
    ""type"": ""test_invalid_role"",
    ""headline"": ""Test"",
    ""origin"": ""public"",
    ""dramaWeight"": 3,
    ""roles"": { ""actor"": ""{ACTOR}"" },
    ""selfTell"": {
      ""nonexistent_role"": ""never""
    },
    ""facts"": [
      { ""id"": ""who"", ""category"": ""WHO"", ""text"": ""{ACTOR} did something"", ""vars"": { ""ACTOR"": ""hero:{ACTOR}"" } }
    ]
  }
]";

            var catalog = EventCatalogLoader.Load(json, new PersistenceConfig());
            var err = catalog.Issues.FirstOrDefault(i => i.Code == CatalogIssueCode.SelfTellInvalid);
            Assert.NotNull(err);
            Assert.True(err!.IsError);
            Assert.Contains("nonexistent_role", err.Detail);
        }

        [Fact]
        public void EventCatalogLoader_SelfTell_UnknownTrait_ReportsSelfTellInvalid()
        {
            string json = @"[
  {
    ""type"": ""test_invalid_trait"",
    ""headline"": ""Test"",
    ""origin"": ""public"",
    ""dramaWeight"": 3,
    ""roles"": { ""actor"": ""{ACTOR}"" },
    ""selfTell"": {
      ""actor"": { ""trait"": ""intelligence"", ""min"": 1 }
    },
    ""facts"": [
      { ""id"": ""who"", ""category"": ""WHO"", ""text"": ""{ACTOR} did something"", ""vars"": { ""ACTOR"": ""hero:{ACTOR}"" } }
    ]
  }
]";

            var catalog = EventCatalogLoader.Load(json, new PersistenceConfig());
            var err = catalog.Issues.FirstOrDefault(i => i.Code == CatalogIssueCode.SelfTellInvalid);
            Assert.NotNull(err);
            Assert.True(err!.IsError);
            Assert.Contains("intelligence", err.Detail);
        }

        [Fact]
        public void EventCatalogLoader_SelfTell_SecretEvent_ReportsSelfTellInvalidAndIgnores()
        {
            string json = @"[
  {
    ""type"": ""test_secret_self_tell"",
    ""headline"": ""Test Secret"",
    ""origin"": ""secret"",
    ""dramaWeight"": 3,
    ""roles"": { ""actor"": ""{ACTOR}"" },
    ""selfTell"": {
      ""actor"": ""never""
    },
    ""facts"": [
      { ""id"": ""who"", ""category"": ""WHO"", ""text"": ""{ACTOR} kept a secret"", ""vars"": { ""ACTOR"": ""hero:{ACTOR}"" } }
    ]
  }
]";

            var catalog = EventCatalogLoader.Load(json, new PersistenceConfig());
            var err = catalog.Issues.FirstOrDefault(i => i.Code == CatalogIssueCode.SelfTellInvalid);
            Assert.NotNull(err);
            Assert.True(err!.IsError);
            Assert.Contains("Secret event template", err.Detail);
            // Must ignore selfTell for secret
            Assert.Null(catalog.Templates[0].SelfTell);
        }

        [Fact]
        public void TellerEligibility_Participant_WontTellOwn_WhenNever()
        {
            var evt = new WorldEvent
            {
                EventId = "evt_1",
                Type = "victory_credit_claimed",
                Origin = EventOrigin.Public,
                Participants = new Dictionary<string, string> { ["claimant"] = "hero_claimant" }
            };
            evt.KnownBy.Add(new KnownByEntry { HeroId = "hero_claimant", Hop = 0, LearnedDay = 1.0 });

            var template = new EventTemplate
            {
                Type = "victory_credit_claimed",
                Roles = new Dictionary<string, string> { ["claimant"] = "{CLAIMANT}" },
                SelfTell = new Dictionary<string, SelfTellRule>
                {
                    ["claimant"] = SelfTellRule.Never
                }
            };

            var cfg = new VividWorldConfig();
            var traits = new FakeHeroTraitLookup();
            traits.Set(new TraitProfile { HeroId = "hero_claimant", IsAlive = true, IsLord = true });

            var reason = TellerEligibility.Check(
                evt, "hero_claimant", 2.0, 4, "player", cfg, traits,
                type => template,
                out string? detail);

            Assert.Equal(TellReason.WontTellOwn, reason);
            Assert.Equal("won't tell own (role claimant)", detail);
        }

        [Fact]
        public void TellerEligibility_Participant_WontTellOwn_WhenTraitTooLow()
        {
            var evt = new WorldEvent
            {
                EventId = "evt_1",
                Type = "advice_mocked",
                Origin = EventOrigin.Public,
                Participants = new Dictionary<string, string> { ["student"] = "hero_student" }
            };
            evt.KnownBy.Add(new KnownByEntry { HeroId = "hero_student", Hop = 0, LearnedDay = 1.0 });

            var template = new EventTemplate
            {
                Type = "advice_mocked",
                Roles = new Dictionary<string, string> { ["student"] = "{STUDENT}" },
                SelfTell = new Dictionary<string, SelfTellRule>
                {
                    ["student"] = new SelfTellRule("valor", 1)
                }
            };

            var cfg = new VividWorldConfig();
            var traits = new FakeHeroTraitLookup();
            traits.Set(new TraitProfile { HeroId = "hero_student", IsAlive = true, IsLord = true, Valor = 0 });

            var reason = TellerEligibility.Check(
                evt, "hero_student", 2.0, 4, "player", cfg, traits,
                type => template,
                out string? detail);

            Assert.Equal(TellReason.WontTellOwn, reason);
            Assert.Equal("won't tell own (role student, valor 0 < 1)", detail);
        }

        [Fact]
        public void TellerEligibility_Participant_Eligible_WhenTraitMet()
        {
            var evt = new WorldEvent
            {
                EventId = "evt_1",
                Type = "advice_mocked",
                Origin = EventOrigin.Public,
                Participants = new Dictionary<string, string> { ["student"] = "hero_student" }
            };
            evt.KnownBy.Add(new KnownByEntry { HeroId = "hero_student", Hop = 0, LearnedDay = 1.0 });

            var template = new EventTemplate
            {
                Type = "advice_mocked",
                Roles = new Dictionary<string, string> { ["student"] = "{STUDENT}" },
                SelfTell = new Dictionary<string, SelfTellRule>
                {
                    ["student"] = new SelfTellRule("valor", 1)
                }
            };

            var cfg = new VividWorldConfig();
            var traits = new FakeHeroTraitLookup();
            traits.Set(new TraitProfile { HeroId = "hero_student", IsAlive = true, IsLord = true, Valor = 1 });

            var reason = TellerEligibility.Check(
                evt, "hero_student", 2.0, 4, "player", cfg, traits,
                type => template,
                out string? detail);

            Assert.Equal(TellReason.Ok, reason);
        }

        [Fact]
        public void RumorOfferSelector_Candidate_RejectsWontTellOwn()
        {
            var cfg = new VividWorldConfig();
            var evt = new WorldEvent
            {
                EventId = "evt_1",
                Type = "victory_credit_claimed",
                Origin = EventOrigin.Public,
                Participants = new Dictionary<string, string> { ["claimant"] = "hero_claimant" },
                Facts = new List<Fact> { new() { Id = "f1", Category = FactCategory.Who, Text = "who" } }
            };
            evt.KnownBy.Add(new KnownByEntry { HeroId = "hero_claimant", Hop = 0, LearnedDay = 1.0 });

            var template = new EventTemplate
            {
                Type = "victory_credit_claimed",
                Roles = new Dictionary<string, string> { ["claimant"] = "{CLAIMANT}" },
                SelfTell = new Dictionary<string, SelfTellRule>
                {
                    ["claimant"] = SelfTellRule.Never
                }
            };

            var channel = new FakePropagationChannel();
            var traits = new FakeHeroTraitLookup();
            traits.Set(new TraitProfile { HeroId = "hero_claimant", IsAlive = true, IsLord = true });
            var engine = new RumorEngine(
                cfg,
                new ThresholdRetentionPolicy(cfg.Retention),
                NullEmbellishmentPolicy.Instance,
                channel,
                traits,
                new SplitMix64Rng(),
                1L,
                "player",
                _ => template);

            var selector = new RumorOfferSelector(cfg, engine, "player", RumorMode.Casual, _ => template, traits);

            var teller = new HeroSocialProfile
            {
                HeroId = "hero_claimant",
                RelationWithPlayer = 20,
                Traits = new TraitProfile { Generosity = 2, Honor = 2, Calculating = 2 }
            };
            var candidates = new List<RumorCandidate>
            {
                new() { Event = evt, TellerHop = 0, PlayerExistingHop = null }
            };
            var decision = selector.DecideOnAsk(teller, candidates, day: 2.0);

            Assert.Null(decision.Offer);
            Assert.Equal(1, decision.FilteredOther);
            Assert.Contains(decision.FilterNotes, note => note.Contains("won't tell own (role claimant)"));
        }

        [Fact]
        public void NpcRecallQuery_Candidate_ExcludesWontTellOwn()
        {
            var cfg = new VividWorldConfig();
            var evt = new WorldEvent
            {
                EventId = "evt_1",
                Type = "victory_credit_claimed",
                Origin = EventOrigin.Public,
                Participants = new Dictionary<string, string> { ["claimant"] = "hero_claimant" },
                Facts = new List<Fact> { new() { Id = "f1", Category = FactCategory.Who, Text = "who" } }
            };
            evt.KnownBy.Add(new KnownByEntry { HeroId = "hero_claimant", Hop = 0, LearnedDay = 1.0 });

            var template = new EventTemplate
            {
                Type = "victory_credit_claimed",
                Roles = new Dictionary<string, string> { ["claimant"] = "{CLAIMANT}" },
                SelfTell = new Dictionary<string, SelfTellRule>
                {
                    ["claimant"] = SelfTellRule.Never
                }
            };

            var result = NpcRecallQuery.Query(
                "hero_claimant",
                currentDay: 2.0,
                maxCount: 10,
                candidateEvents: new[] { evt },
                retentionPolicy: new ThresholdRetentionPolicy(cfg.Retention),
                isEventKnown: _ => false,
                config: cfg,
                getTemplate: _ => template,
                traits: null);

            Assert.Empty(result.Items);
            Assert.Single(result.Exclusions);
            Assert.Equal(RecallExclusionReason.WontTellOwn, result.Exclusions[0].Reason);
            Assert.Equal("won't tell own (role claimant)", result.Exclusions[0].Detail);
            Assert.Contains("wontTellOwn=1", result.ExclusionSummary());
        }
    }
}
