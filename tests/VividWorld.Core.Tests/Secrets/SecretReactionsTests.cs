#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using VividWorld.Core.Catalog;
using VividWorld.Core.Channels;
using VividWorld.Core.Config;
using VividWorld.Core.Events;
using VividWorld.Core.Rumors;
using VividWorld.Core.Secrets;
using VividWorld.Core.Tests.Fakes;
using VividWorld.Core.Util;
using Xunit;

namespace VividWorld.Core.Tests.Secrets
{
    public sealed class SecretReactionsTests
    {
        private static EventTemplate CreateEnduredTemplate()
        {
            return new EventTemplate
            {
                Type = "seat_dispute_grudge_held",
                Origin = EventOrigin.Secret,
                Roles = new Dictionary<string, string>
                {
                    ["slighted"] = "{SLIGHTED}",
                    ["favored"] = "{FAVORED}"
                },
                OwnerRoles = new List<string> { "slighted" },
                OnHeard = new List<TemplateOnHeardRule>
                {
                    new TemplateOnHeardRule
                    {
                        ListenerRole = "favored",
                        TargetRole = "slighted",
                        AmountRef = "@secrets.endured.favoredOnHeard"
                    }
                }
            };
        }

        private static EventTemplate CreatePoisonedTemplate()
        {
            return new EventTemplate
            {
                Type = "conduct_poisoned",
                Origin = EventOrigin.Secret,
                Roles = new Dictionary<string, string>
                {
                    ["poisoner"] = "{POISONER}",
                    ["victim"] = "{VICTIM}"
                },
                OwnerRoles = new List<string> { "poisoner" },
                OnHeard = new List<TemplateOnHeardRule>
                {
                    new TemplateOnHeardRule
                    {
                        ListenerKinOf = "victim",
                        TargetRole = "poisoner",
                        AmountRef = "@secrets.poisoned.kinOnHeard",
                        Escalate = "clan"
                    },
                    new TemplateOnHeardRule
                    {
                        ListenerFriendsOf = "victim",
                        TargetRole = "poisoner",
                        AmountRef = "@secrets.poisoned.friendOnHeard"
                    }
                }
            };
        }

        [Fact]
        public void OnHeard_Endured_FavoredHears_DeductsFavoredOnHeard_NoEscalation()
        {
            var tmpl = CreateEnduredTemplate();
            var evt = new WorldEvent
            {
                EventId = "evt_endured_1",
                Type = "seat_dispute_grudge_held",
                Origin = EventOrigin.Secret,
                SecretOwnerHeroIds = new List<string> { "lord_slighted" },
                Participants = new Dictionary<string, string>
                {
                    ["slighted"] = "lord_slighted",
                    ["favored"] = "lord_favored"
                },
                State = new RumorState
                {
                    Leaked = true,
                    LeakedDay = 10.0,
                    LeakerHeroId = "lord_leaker"
                }
            };

            var hearer = new KnownByEntry
            {
                HeroId = "lord_favored",
                Hop = 2,
                LearnedDay = 12.0,
                SourceHeroId = "lord_teller",
                Believes = true
            };
            evt.KnownBy.Add(hearer);

            var cfg = new VividWorldConfig();
            var rel = new FakeSecretRelationshipLookup();
            var traits = new FakeHeroTraitLookup();

            var outcome = SecretReactionEvaluator.Evaluate(
                evt, hearer, tmpl, rel, traits, cfg, null, "player_hero", 12.0);

            Assert.True(outcome.Handled);
            Assert.False(outcome.IsOwner);
            Assert.Single(outcome.Changes);

            var change = outcome.Changes[0];
            Assert.Equal("lord_favored", change.ObserverHeroId);
            Assert.Equal("lord_slighted", change.TargetHeroId);
            Assert.Equal(-3.0, change.Amount);
            Assert.False(change.EscalateClan);
            Assert.False(change.LedgerOnly);
        }

        [Fact]
        public void OnHeard_Poisoned_KinHears_DeductsKinOnHeard_EscalatesClan()
        {
            var tmpl = CreatePoisonedTemplate();
            var evt = new WorldEvent
            {
                EventId = "evt_poison_1",
                Type = "conduct_poisoned",
                Origin = EventOrigin.Secret,
                SecretOwnerHeroIds = new List<string> { "lord_poisoner" },
                Participants = new Dictionary<string, string>
                {
                    ["poisoner"] = "lord_poisoner",
                    ["victim"] = "lord_victim"
                },
                State = new RumorState
                {
                    Leaked = true,
                    LeakedDay = 5.0,
                    LeakerHeroId = "lord_leaker"
                }
            };

            var hearer = new KnownByEntry
            {
                HeroId = "lord_kin",
                Hop = 2,
                LearnedDay = 6.0,
                SourceHeroId = "lord_teller",
                Believes = true
            };
            evt.KnownBy.Add(hearer);

            var cfg = new VividWorldConfig();
            var rel = new FakeSecretRelationshipLookup();
            rel.SetKin("lord_kin", "lord_victim");
            var traits = new FakeHeroTraitLookup();

            var outcome = SecretReactionEvaluator.Evaluate(
                evt, hearer, tmpl, rel, traits, cfg, null, "player_hero", 6.0);

            Assert.True(outcome.Handled);
            Assert.Single(outcome.Changes);

            var change = outcome.Changes[0];
            Assert.Equal("lord_kin", change.ObserverHeroId);
            Assert.Equal("lord_poisoner", change.TargetHeroId);
            Assert.Equal(-20.0, change.Amount);
            Assert.True(change.EscalateClan);
            Assert.False(change.LedgerOnly);
        }

        [Fact]
        public void OnHeard_Poisoned_FriendHears_DeductsFriendOnHeard_NoEscalation()
        {
            var tmpl = CreatePoisonedTemplate();
            var evt = new WorldEvent
            {
                EventId = "evt_poison_2",
                Type = "conduct_poisoned",
                Origin = EventOrigin.Secret,
                SecretOwnerHeroIds = new List<string> { "lord_poisoner" },
                Participants = new Dictionary<string, string>
                {
                    ["poisoner"] = "lord_poisoner",
                    ["victim"] = "lord_victim"
                },
                State = new RumorState
                {
                    Leaked = true,
                    LeakedDay = 5.0,
                    LeakerHeroId = "lord_leaker"
                }
            };

            var hearer = new KnownByEntry
            {
                HeroId = "lord_friend",
                Hop = 2,
                LearnedDay = 7.0,
                SourceHeroId = "lord_teller",
                Believes = true
            };
            evt.KnownBy.Add(hearer);

            var cfg = new VividWorldConfig();
            var rel = new FakeSecretRelationshipLookup();
            rel.SetAffection("lord_friend", "lord_victim", 55);
            var traits = new FakeHeroTraitLookup();

            var outcome = SecretReactionEvaluator.Evaluate(
                evt, hearer, tmpl, rel, traits, cfg, null, "player_hero", 7.0);

            Assert.True(outcome.Handled);
            Assert.Single(outcome.Changes);

            var change = outcome.Changes[0];
            Assert.Equal("lord_friend", change.ObserverHeroId);
            Assert.Equal("lord_poisoner", change.TargetHeroId);
            Assert.Equal(-8.0, change.Amount);
            Assert.False(change.EscalateClan);
            Assert.False(change.LedgerOnly);
        }

        [Fact]
        public void OnHeard_KinAndFriendThresholds_ExactBoundaryDoesNotTrigger()
        {
            var tmpl = CreatePoisonedTemplate();
            var evt = new WorldEvent
            {
                EventId = "evt_poison_3",
                Type = "conduct_poisoned",
                Origin = EventOrigin.Secret,
                SecretOwnerHeroIds = new List<string> { "lord_poisoner" },
                Participants = new Dictionary<string, string>
                {
                    ["poisoner"] = "lord_poisoner",
                    ["victim"] = "lord_victim"
                },
                State = new RumorState
                {
                    Leaked = true,
                    LeakedDay = 5.0,
                    LeakerHeroId = "lord_leaker"
                }
            };

            var hearer = new KnownByEntry
            {
                HeroId = "lord_boundary",
                Hop = 2,
                LearnedDay = 8.0,
                SourceHeroId = "lord_teller",
                Believes = true
            };
            evt.KnownBy.Add(hearer);

            var cfg = new VividWorldConfig();
            var rel = new FakeSecretRelationshipLookup();
            // Exactly 50: does not exceed 50, so friend rule must not trigger
            rel.SetAffection("lord_boundary", "lord_victim", 50);
            // Not kin: kin rule must not trigger
            var traits = new FakeHeroTraitLookup();

            var outcome = SecretReactionEvaluator.Evaluate(
                evt, hearer, tmpl, rel, traits, cfg, null, "player_hero", 8.0);

            Assert.True(outcome.Handled);
            Assert.Empty(outcome.Changes);
        }

        [Fact]
        public void OnHeard_ClanEscalation_SamePairOnlyEscalatesOnce()
        {
            // Verify evaluator only generates one reaction change per unique target
            var tmpl = new EventTemplate
            {
                Type = "conduct_poisoned",
                Origin = EventOrigin.Secret,
                Roles = new Dictionary<string, string>
                {
                    ["poisoner"] = "{POISONER}",
                    ["victim"] = "{VICTIM}"
                },
                OwnerRoles = new List<string> { "poisoner" },
                OnHeard = new List<TemplateOnHeardRule>
                {
                    new TemplateOnHeardRule
                    {
                        ListenerKinOf = "victim",
                        TargetRole = "poisoner",
                        Amount = -20.0,
                        Escalate = "clan"
                    },
                    new TemplateOnHeardRule
                    {
                        ListenerFriendsOf = "victim",
                        TargetRole = "poisoner",
                        Amount = -8.0
                    }
                }
            };

            var evt = new WorldEvent
            {
                EventId = "evt_poison_clan",
                Type = "conduct_poisoned",
                Origin = EventOrigin.Secret,
                SecretOwnerHeroIds = new List<string> { "lord_poisoner" },
                Participants = new Dictionary<string, string>
                {
                    ["poisoner"] = "lord_poisoner",
                    ["victim"] = "lord_victim"
                },
                State = new RumorState
                {
                    Leaked = true,
                    LeakedDay = 2.0,
                    LeakerHeroId = "lord_leaker"
                }
            };

            var hearer = new KnownByEntry
            {
                HeroId = "lord_kin_and_friend",
                Hop = 2,
                LearnedDay = 4.0,
                SourceHeroId = "lord_teller",
                Believes = true
            };
            evt.KnownBy.Add(hearer);

            var cfg = new VividWorldConfig();
            var rel = new FakeSecretRelationshipLookup();
            rel.SetKin("lord_kin_and_friend", "lord_victim");
            rel.SetAffection("lord_kin_and_friend", "lord_victim", 80);

            var outcome = SecretReactionEvaluator.Evaluate(
                evt, hearer, tmpl, rel, new FakeHeroTraitLookup(), cfg, null, "player_hero", 4.0);

            // Even if both kin and friends match, only the first matching rule triggers for that target
            Assert.Single(outcome.Changes);
            Assert.Equal(-20.0, outcome.Changes[0].Amount);
            Assert.True(outcome.Changes[0].EscalateClan);
        }

        [Fact]
        public void OnHeard_Confided_DoesNotTrigger()
        {
            var tmpl = CreateEnduredTemplate();
            var evt = new WorldEvent
            {
                EventId = "evt_confided_1",
                Type = "seat_dispute_grudge_held",
                Origin = EventOrigin.Secret,
                SecretOwnerHeroIds = new List<string> { "lord_slighted" },
                Participants = new Dictionary<string, string>
                {
                    ["slighted"] = "lord_slighted",
                    ["favored"] = "lord_favored"
                },
                State = new RumorState
                {
                    Leaked = true,
                    LeakedDay = 15.0,
                    LeakerHeroId = "lord_leaker"
                }
            };

            // Learned on day 10, before secret leaked on day 15 (confided knowledge)
            var hearer = new KnownByEntry
            {
                HeroId = "lord_favored",
                Hop = 1,
                LearnedDay = 10.0,
                SourceHeroId = "lord_slighted",
                Believes = true
            };
            evt.KnownBy.Add(hearer);

            var outcome = SecretReactionEvaluator.Evaluate(
                evt, hearer, tmpl, new FakeSecretRelationshipLookup(), new FakeHeroTraitLookup(),
                new VividWorldConfig(), null, "player_hero", 10.0);

            Assert.Empty(outcome.Changes);
            Assert.Contains(outcome.Exclusions, e => e.Reason == "confided");
        }

        [Fact]
        public void OnHeard_Player_DoesNotChangeRelation()
        {
            var tmpl = CreateEnduredTemplate();
            var evt = new WorldEvent
            {
                EventId = "evt_player_hear",
                Type = "seat_dispute_grudge_held",
                Origin = EventOrigin.Secret,
                SecretOwnerHeroIds = new List<string> { "lord_slighted" },
                Participants = new Dictionary<string, string>
                {
                    ["slighted"] = "lord_slighted",
                    ["favored"] = "player_hero"
                },
                State = new RumorState
                {
                    Leaked = true,
                    LeakedDay = 1.0,
                    LeakerHeroId = "lord_leaker"
                }
            };

            var playerKnower = new KnownByEntry
            {
                HeroId = "player_hero",
                Hop = 2,
                LearnedDay = 3.0,
                SourceHeroId = "lord_teller",
                Believes = true
            };
            evt.KnownBy.Add(playerKnower);

            var outcome = SecretReactionEvaluator.Evaluate(
                evt, playerKnower, tmpl, new FakeSecretRelationshipLookup(), new FakeHeroTraitLookup(),
                new VividWorldConfig(), null, "player_hero", 3.0);

            Assert.Empty(outcome.Changes);
            Assert.Contains(outcome.Exclusions, e => e.Reason == "player");
        }

        [Fact]
        public void OwnerReaction_OriginatorRetained_FirstAndSecondHand_GrudgesOriginator()
        {
            var tmpl = CreateEnduredTemplate();
            var evt = new WorldEvent
            {
                EventId = "evt_owner_react_1",
                Type = "seat_dispute_grudge_held",
                Origin = EventOrigin.Secret,
                SecretOwnerHeroIds = new List<string> { "lord_slighted" },
                Participants = new Dictionary<string, string>
                {
                    ["slighted"] = "lord_slighted",
                    ["favored"] = "lord_favored"
                },
                State = new RumorState
                {
                    Leaked = true,
                    LeakedDay = 5.0,
                    LeakerHeroId = "lord_leaker"
                }
            };

            // Leaker at hop 1
            evt.KnownBy.Add(new KnownByEntry { HeroId = "lord_leaker", Hop = 1, LearnedDay = 5.0 });

            // Owner hears at hop 3 (hands = 3 - 1 = 2: retained!)
            var ownerEntry = new KnownByEntry
            {
                HeroId = "lord_slighted",
                Hop = 3,
                LearnedDay = 8.0,
                SourceHeroId = "lord_intermediate"
            };
            evt.KnownBy.Add(ownerEntry);

            var traits = new FakeHeroTraitLookup();
            // Negative valor -> hidden
            traits.Set(new TraitProfile { HeroId = "lord_slighted", Valor = -2 });

            var outcome = SecretReactionEvaluator.Evaluate(
                evt, ownerEntry, tmpl, new FakeSecretRelationshipLookup(), traits,
                new VividWorldConfig(), null, "player_hero", 8.0);

            Assert.True(outcome.IsOwner);
            Assert.Equal("lord_leaker", outcome.GrudgeAgainstHeroId);
            Assert.Null(outcome.SuspectedHeroIds);
            Assert.Equal("lord_leaker", ownerEntry.GrudgeAgainstHeroId);
            Assert.Single(outcome.Changes);
            Assert.Equal("lord_leaker", outcome.Changes[0].TargetHeroId);
            Assert.Equal(-6.0, outcome.Changes[0].Amount);
        }

        [Fact]
        public void OwnerReaction_OriginatorNotRetained_ThirdHandAndLater_SuspectsList()
        {
            var tmpl = CreateEnduredTemplate();
            var evt = new WorldEvent
            {
                EventId = "evt_owner_react_2",
                Type = "seat_dispute_grudge_held",
                Origin = EventOrigin.Secret,
                SecretOwnerHeroIds = new List<string> { "lord_slighted" },
                Participants = new Dictionary<string, string>
                {
                    ["slighted"] = "lord_slighted",
                    ["favored"] = "lord_favored"
                },
                State = new RumorState
                {
                    Leaked = true,
                    LeakedDay = 5.0,
                    LeakerHeroId = "lord_leaker"
                }
            };

            evt.KnownBy.Add(new KnownByEntry { HeroId = "lord_leaker", Hop = 1, LearnedDay = 5.0 });
            evt.KnownBy.Add(new KnownByEntry { HeroId = "lord_confidant", Hop = 1, LearnedDay = 2.0, SourceHeroId = "lord_slighted" });

            // Owner hears at hop 4 (hands = 4 - 1 = 3: not retained!)
            var ownerEntry = new KnownByEntry
            {
                HeroId = "lord_slighted",
                Hop = 4,
                LearnedDay = 10.0,
                SourceHeroId = "lord_someone"
            };
            evt.KnownBy.Add(ownerEntry);

            var traits = new FakeHeroTraitLookup();
            traits.Set(new TraitProfile { HeroId = "lord_slighted", Valor = -2 });

            var outcome = SecretReactionEvaluator.Evaluate(
                evt, ownerEntry, tmpl, new FakeSecretRelationshipLookup(), traits,
                new VividWorldConfig(), null, "player_hero", 10.0);

            Assert.True(outcome.IsOwner);
            Assert.Null(outcome.GrudgeAgainstHeroId);
            Assert.NotNull(outcome.SuspectedHeroIds);
            Assert.Contains("lord_confidant", outcome.SuspectedHeroIds);
            Assert.Equal(-3.0, outcome.Changes.First(c => c.TargetHeroId == "lord_confidant").Amount);
        }

        [Fact]
        public void OwnerReaction_SuspectList_IncludesPlayerDeadAndCoOwner_Distinct()
        {
            var tmpl = CreateEnduredTemplate();
            var evt = new WorldEvent
            {
                EventId = "evt_suspect_test",
                Type = "seat_dispute_grudge_held",
                Origin = EventOrigin.Secret,
                SecretOwnerHeroIds = new List<string> { "lord_owner", "lord_coowner" },
                State = new RumorState
                {
                    Leaked = true,
                    LeakedDay = 5.0,
                    LeakerHeroId = "lord_leaker"
                }
            };

            // Hop 0 knowers at creation
            evt.KnownBy.Add(new KnownByEntry { HeroId = "lord_owner", Hop = 0, LearnedDay = 1.0 });
            evt.KnownBy.Add(new KnownByEntry { HeroId = "lord_coowner", Hop = 0, LearnedDay = 1.0 });
            evt.KnownBy.Add(new KnownByEntry { HeroId = "lord_dead", Hop = 0, LearnedDay = 1.0 });

            // Personally confided by lord_owner, including player and duplicate confidant
            evt.KnownBy.Add(new KnownByEntry { HeroId = "player_hero", Hop = 1, LearnedDay = 2.0, SourceHeroId = "lord_owner" });
            evt.KnownBy.Add(new KnownByEntry { HeroId = "lord_coowner", Hop = 1, LearnedDay = 2.0, SourceHeroId = "lord_owner" }); // duplicate
            evt.KnownBy.Add(new KnownByEntry { HeroId = "lord_friend", Hop = 1, LearnedDay = 3.0, SourceHeroId = "lord_owner" });

            // Leaker is unknown to owner
            var ownerEntry = evt.KnownBy[0];
            ownerEntry.OriginatorKnownOverride = false;

            var outcome = SecretReactionEvaluator.Evaluate(
                evt, ownerEntry, tmpl, new FakeSecretRelationshipLookup(), new FakeHeroTraitLookup(),
                new VividWorldConfig(), null, "player_hero", 10.0);

            Assert.NotNull(outcome.SuspectedHeroIds);
            var suspects = outcome.SuspectedHeroIds!;

            // Distinct: lord_coowner appears once
            Assert.Equal(suspects.Distinct().Count(), suspects.Count);

            // Includes coowner, dead, player, friend
            Assert.Contains("lord_coowner", suspects);
            Assert.Contains("lord_dead", suspects);
            Assert.Contains("player_hero", suspects);
            Assert.Contains("lord_friend", suspects);

            // Excludes self
            Assert.DoesNotContain("lord_owner", suspects);
        }

        [Fact]
        public void OwnerReaction_Poisoner_AlwaysHidden()
        {
            var tmpl = CreatePoisonedTemplate();
            var evt = new WorldEvent
            {
                EventId = "evt_poison_owner",
                Type = "conduct_poisoned",
                Origin = EventOrigin.Secret,
                SecretOwnerHeroIds = new List<string> { "lord_poisoner" },
                Participants = new Dictionary<string, string>
                {
                    ["poisoner"] = "lord_poisoner",
                    ["victim"] = "lord_victim"
                },
                State = new RumorState
                {
                    Leaked = true,
                    LeakedDay = 5.0,
                    LeakerHeroId = "lord_witness"
                }
            };

            var ownerEntry = new KnownByEntry
            {
                HeroId = "lord_poisoner",
                Hop = 0,
                LearnedDay = 1.0,
                OriginatorKnownOverride = true
            };
            evt.KnownBy.Add(ownerEntry);

            var traits = new FakeHeroTraitLookup();
            // Maximum valor!
            traits.Set(new TraitProfile { HeroId = "lord_poisoner", Valor = 2 });

            var outcome = SecretReactionEvaluator.Evaluate(
                evt, ownerEntry, tmpl, new FakeSecretRelationshipLookup(), traits,
                new VividWorldConfig(), null, "player_hero", 10.0);

            Assert.Equal("hidden", outcome.OwnerReaction);
            Assert.Equal("hidden", ownerEntry.OwnerReaction);
            Assert.All(outcome.Changes, c => Assert.True(c.LedgerOnly));
        }

        [Fact]
        public void OwnerReaction_Openly_ConvertsEnduredLedgerOnlyGrudgeToNative()
        {
            var tmpl = CreateEnduredTemplate();
            var evt = new WorldEvent
            {
                EventId = "evt_secret_held",
                Type = "seat_dispute_grudge_held",
                Origin = EventOrigin.Secret,
                LinkedEventId = "evt_endured_origin",
                SecretOwnerHeroIds = new List<string> { "lord_slighted" },
                Participants = new Dictionary<string, string>
                {
                    ["slighted"] = "lord_slighted",
                    ["favored"] = "lord_favored"
                },
                State = new RumorState
                {
                    Leaked = true,
                    LeakedDay = 5.0,
                    LeakerHeroId = "lord_leaker"
                }
            };

            var ownerEntry = new KnownByEntry
            {
                HeroId = "lord_slighted",
                Hop = 0,
                LearnedDay = 1.0,
                OriginatorKnownOverride = true
            };
            evt.KnownBy.Add(ownerEntry);

            var linkedEvent = new WorldEvent
            {
                EventId = "evt_endured_origin",
                Type = "seat_dispute_endured",
                Participants = new Dictionary<string, string>
                {
                    ["slighted"] = "lord_slighted",
                    ["favored"] = "lord_favored"
                }
            };
            var linkedEntry = new KnownByEntry
            {
                HeroId = "lord_slighted",
                Hop = 0,
                LearnedDay = 1.0,
                RelationImpacts = new List<RelationImpact>
                {
                    new RelationImpact
                    {
                        AboutHeroId = "lord_favored",
                        Requested = -6.0,
                        Delta = 0,
                        LedgerOnly = true
                    }
                }
            };
            linkedEvent.KnownBy.Add(linkedEntry);

            var traits = new FakeHeroTraitLookup();
            // Maximum valor forces openly
            traits.Set(new TraitProfile { HeroId = "lord_slighted", Valor = 2 });

            var outcome = SecretReactionEvaluator.Evaluate(
                evt, ownerEntry, tmpl, new FakeSecretRelationshipLookup(), traits,
                new VividWorldConfig(), null, "player_hero", 10.0,
                loadEvent: id => id == "evt_endured_origin" ? linkedEvent : null);

            Assert.Equal("openly", outcome.OwnerReaction);
            Assert.Equal("openly", ownerEntry.OwnerReaction);

            // Grudge on originator is not ledger only
            var originatorGrudge = outcome.Changes.First(c => c.TargetHeroId == "lord_leaker");
            Assert.False(originatorGrudge.LedgerOnly);

            // Converted endured grudge is present and not ledger only
            var converted = outcome.Changes.First(c => c.IsEnduredGrudgeConversion);
            Assert.Equal("lord_favored", converted.TargetHeroId);
            Assert.Equal(-6.0, converted.Amount);
            Assert.False(converted.LedgerOnly);

            // The impact on linked entry was converted to LedgerOnly = false
            Assert.False(linkedEntry.RelationImpacts[0].LedgerOnly);
        }

        private static (RumorEngine engine, FakePropagationChannel channel, FakeHeroTraitLookup traits, VividWorldConfig cfg)
            CreateRumorEngine(EventTemplate? tmpl = null, long seed = 42L)
        {
            var cfg = new VividWorldConfig();
            cfg.Propagation.BaseTellChancePerContact = 1.0;
            cfg.Debug.LogTellerTurns = true;
            var rng = new SplitMix64Rng();
            var channel = new FakePropagationChannel();
            var traits = new FakeHeroTraitLookup();
            var retention = FactRetentionPolicies.Create(cfg, rng, seed);
            var embellishment = NullEmbellishmentPolicy.Instance;
            var engine = new RumorEngine(cfg, retention, embellishment, channel, traits, rng, seed, "player_hero", t => tmpl);
            return (engine, channel, traits, cfg);
        }

        [Fact]
        public void RumorEngine_OwnerReaction_TriggersOnce_NotSecondTime_NotBeforeLeak_OtherKnownUnaffected()
        {
            var tmpl = CreateEnduredTemplate();
            var (engine, channel, traits, cfg) = CreateRumorEngine(tmpl);

            traits.Set(new TraitProfile { HeroId = "owner", IsAlive = true, IsLord = true, Valor = -2 });
            traits.Set(new TraitProfile { HeroId = "leaker", IsAlive = true, IsLord = true });
            traits.Set(new TraitProfile { HeroId = "teller", IsAlive = true, IsLord = true });
            traits.Set(new TraitProfile { HeroId = "other_knower", IsAlive = true, IsLord = true });

            channel.AddLink("teller", "owner", ChannelKind.SameSettlement);
            channel.AddLink("teller", "other_knower", ChannelKind.SameSettlement);

            var evt = new WorldEvent
            {
                EventId = "evt_leak_run_1",
                Type = "seat_dispute_grudge_held",
                Origin = EventOrigin.Secret,
                DramaWeight = 3,
                Day = 1.0,
                SecretOwnerHeroIds = new List<string> { "owner" },
                Participants = new Dictionary<string, string>
                {
                    ["slighted"] = "owner",
                    ["favored"] = "lord_favored"
                },
                State = new RumorState { Leaked = false },
                KnownBy = new List<KnownByEntry>
                {
                    new KnownByEntry { HeroId = "owner", Hop = 0, LearnedDay = 1.0 },
                    new KnownByEntry { HeroId = "leaker", Hop = 0, LearnedDay = 1.0 },
                    new KnownByEntry { HeroId = "other_knower", Hop = 1, LearnedDay = 2.0, SourceHeroId = "leaker" },
                    new KnownByEntry { HeroId = "teller", Hop = 1, LearnedDay = 2.0, SourceHeroId = "leaker" }
                }
            };

            // Phase 1: Before leak, telling teller's contacts does not trigger owner reaction
            var outcomeBefore = engine.PropagateFromTeller(evt, "teller", 3.0, 10);
            var ownerEntry = evt.EntryFor("owner");
            Assert.NotNull(ownerEntry);
            Assert.Null(ownerEntry!.OwnerReaction);
            Assert.Empty(outcomeBefore.SecretReactions);
            Assert.False(outcomeBefore.DidAnything);

            // Phase 2: Secret leaks; owner hears it from teller (hand 2: tellerHop 1 + 1)
            evt.State.Leaked = true;
            evt.State.LeakedDay = 3.5;
            evt.State.LeakerHeroId = "leaker";

            var outcomeLeaked = engine.PropagateFromTeller(evt, "teller", 4.0, 10);
            Assert.Single(outcomeLeaked.SecretReactions);
            Assert.NotNull(ownerEntry.OwnerReaction);
            Assert.Equal("hidden", ownerEntry.OwnerReaction);
            Assert.Equal(0, ownerEntry.Hop); // Hop stays 0
            Assert.Equal(4, evt.KnownBy.Count); // Does not add a new KnownBy entry
            Assert.Empty(outcomeLeaked.NewKnowers); // Not a new knower
            Assert.Equal(-1.0, evt.State.LastNewKnowerDay); // Not treated as a new rumor

            // Other knower is still AlreadyKnows and unaffected
            var otherKnowerEntry = evt.EntryFor("other_knower");
            Assert.NotNull(otherKnowerEntry);
            Assert.Equal(1, otherKnowerEntry!.Hop);
            var otherObsLeaked = outcomeLeaked.Contacts.FirstOrDefault(c => c.HeroId == "other_knower");
            Assert.NotNull(otherObsLeaked);
            Assert.Equal(ContactStatus.AlreadyKnows, otherObsLeaked!.Status);

            // Phase 3: Second propagation; owner already reacted, does not trigger a second time
            var outcomeSecond = engine.PropagateFromTeller(evt, "teller", 5.0, 10);
            Assert.Empty(outcomeSecond.SecretReactions);
            var ownerObsSecond = outcomeSecond.Contacts.FirstOrDefault(c => c.HeroId == "owner");
            Assert.NotNull(ownerObsSecond);
            Assert.Equal(ContactStatus.AlreadyKnows, ownerObsSecond!.Status);
        }

        [Fact]
        public void RumorEngine_OwnerReaction_Hands1And2_GrudgesOriginator()
        {
            var tmpl = CreateEnduredTemplate();

            // Hand 1: teller is the leaker (hop 0), owner hears at hand 1 (0 + 1 = 1)
            {
                var (engine1, channel1, traits1, _) = CreateRumorEngine(tmpl, seed: 101L);
                traits1.Set(new TraitProfile { HeroId = "owner", IsAlive = true, IsLord = true, Valor = -2 });
                traits1.Set(new TraitProfile { HeroId = "leaker", IsAlive = true, IsLord = true });
                channel1.AddLink("leaker", "owner", ChannelKind.SameSettlement);

                var evt1 = new WorldEvent
                {
                    EventId = "evt_hand1",
                    Type = "seat_dispute_grudge_held",
                    Origin = EventOrigin.Secret,
                    DramaWeight = 3,
                    Day = 1.0,
                    SecretOwnerHeroIds = new List<string> { "owner" },
                    State = new RumorState { Leaked = true, LeakedDay = 2.0, LeakerHeroId = "leaker" },
                    KnownBy = new List<KnownByEntry>
                    {
                        new KnownByEntry { HeroId = "owner", Hop = 0, LearnedDay = 1.0 },
                        new KnownByEntry { HeroId = "leaker", Hop = 0, LearnedDay = 1.0 }
                    }
                };

                var outcome1 = engine1.PropagateFromTeller(evt1, "leaker", 3.0, 10);
                Assert.Single(outcome1.SecretReactions);
                var entry1 = evt1.EntryFor("owner");
                Assert.NotNull(entry1);
                Assert.Equal("leaker", entry1!.GrudgeAgainstHeroId);
                Assert.Null(entry1.SuspectedHeroIds);
            }

            // Hand 2: teller is hop 1, owner hears at hand 2 (1 + 1 = 2)
            {
                var (engine2, channel2, traits2, _) = CreateRumorEngine(tmpl, seed: 102L);
                traits2.Set(new TraitProfile { HeroId = "owner", IsAlive = true, IsLord = true, Valor = -2 });
                traits2.Set(new TraitProfile { HeroId = "leaker", IsAlive = true, IsLord = true });
                traits2.Set(new TraitProfile { HeroId = "teller_hop1", IsAlive = true, IsLord = true });
                channel2.AddLink("teller_hop1", "owner", ChannelKind.SameSettlement);

                var evt2 = new WorldEvent
                {
                    EventId = "evt_hand2",
                    Type = "seat_dispute_grudge_held",
                    Origin = EventOrigin.Secret,
                    DramaWeight = 3,
                    Day = 1.0,
                    SecretOwnerHeroIds = new List<string> { "owner" },
                    State = new RumorState { Leaked = true, LeakedDay = 2.0, LeakerHeroId = "leaker" },
                    KnownBy = new List<KnownByEntry>
                    {
                        new KnownByEntry { HeroId = "owner", Hop = 0, LearnedDay = 1.0 },
                        new KnownByEntry { HeroId = "leaker", Hop = 0, LearnedDay = 1.0 },
                        new KnownByEntry { HeroId = "teller_hop1", Hop = 1, LearnedDay = 2.5, SourceHeroId = "leaker" }
                    }
                };

                var outcome2 = engine2.PropagateFromTeller(evt2, "teller_hop1", 3.0, 10);
                Assert.Single(outcome2.SecretReactions);
                var entry2 = evt2.EntryFor("owner");
                Assert.NotNull(entry2);
                Assert.Equal("leaker", entry2!.GrudgeAgainstHeroId);
                Assert.Null(entry2.SuspectedHeroIds);
            }
        }

        [Fact]
        public void RumorEngine_OwnerReaction_Hands3AndLater_SuspectsList()
        {
            var tmpl = CreateEnduredTemplate();
            var (engine, channel, traits, _) = CreateRumorEngine(tmpl, seed: 103L);

            traits.Set(new TraitProfile { HeroId = "owner", IsAlive = true, IsLord = true, Valor = -2 });
            traits.Set(new TraitProfile { HeroId = "leaker", IsAlive = true, IsLord = true });
            traits.Set(new TraitProfile { HeroId = "teller_hop2", IsAlive = true, IsLord = true });
            channel.AddLink("teller_hop2", "owner", ChannelKind.SameSettlement);

            var evt = new WorldEvent
            {
                EventId = "evt_hand3",
                Type = "seat_dispute_grudge_held",
                Origin = EventOrigin.Secret,
                DramaWeight = 3,
                Day = 1.0,
                SecretOwnerHeroIds = new List<string> { "owner" },
                State = new RumorState { Leaked = true, LeakedDay = 2.0, LeakerHeroId = "leaker" },
                KnownBy = new List<KnownByEntry>
                {
                    new KnownByEntry { HeroId = "owner", Hop = 0, LearnedDay = 1.0 },
                    new KnownByEntry { HeroId = "leaker", Hop = 0, LearnedDay = 1.0 },
                    new KnownByEntry { HeroId = "teller_hop2", Hop = 2, LearnedDay = 3.0, SourceHeroId = "someone" }
                }
            };

            // Hand 3: teller is hop 2, heardHop = 2 + 1 = 3 -> hands = 3 - 0 = 3 >= 3
            var outcome = engine.PropagateFromTeller(evt, "teller_hop2", 4.0, 10);
            Assert.Single(outcome.SecretReactions);
            var entry = evt.EntryFor("owner");
            Assert.NotNull(entry);
            Assert.Null(entry!.GrudgeAgainstHeroId);
            Assert.NotNull(entry.SuspectedHeroIds);
            Assert.Contains("leaker", entry.SuspectedHeroIds!);
        }
    }
}
