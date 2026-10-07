#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using VividWorld.Core.Catalog;
using VividWorld.Core.Config;
using VividWorld.Core.Dialogue;
using VividWorld.Core.Events;
using VividWorld.Core.Memory;
using VividWorld.Core.Persistence;
using VividWorld.Core.Presentation;
using VividWorld.Core.Rumors;
using VividWorld.Core.Tests.Fakes;
using VividWorld.Core.Util;
using Xunit;

namespace VividWorld.Core.Tests
{
    /// <summary>打探時對方記得自己跟玩家講過什麼：講過的跳過、全講過就反問；打探回答不用重述開頭語。</summary>
    public class ProbeAlreadyToldTests
    {
        private const string He = "he";
        private const string Player = "player";
        private const double Today = 100.0;

        private static KnownByEntry K(string hero, int hop, string? source = null)
            => new KnownByEntry { HeroId = hero, Hop = hop, SourceHeroId = source, LearnedDay = 90 };

        private static WorldEvent Captured(string id, double day, params KnownByEntry[] knownBy)
            => new WorldEvent
            {
                EventId = id,
                Type = "hero_captured",
                Day = day,
                Origin = EventOrigin.Public,
                State = new RumorState { Leaked = true },
                Participants = new Dictionary<string, string>(StringComparer.Ordinal) { ["prisoner"] = "pr_" + id },
                KnownBy = knownBy.ToList()
            };

        private static PlayerHeardEntry Heard(string eventId, string root, params PlayerHeardSource[] sources)
            => new PlayerHeardEntry
            {
                EventId = eventId,
                Type = "hero_captured",
                RootEventId = root,
                PlayerHop = 1,
                UpdatedDay = 90,
                Sources = sources.ToList()
            };

        private static PlayerHeardSource Told(string hero, double day = 95)
            => new PlayerHeardSource { HeroId = hero, Day = day, Hop = 1 };

        private static PlayerHeardSource ProbeAnswer(string hero, double day = 96)
            => new PlayerHeardSource { HeroId = hero, Day = day, ProbeAnswerKey = "VividWorld_Probe_Refuse_1" };

        private sealed class Run
        {
            public ProbePlan Plan = null!;
            public List<string> Asked = new();
            public int OffersBuilt;
        }

        private static Run Go(IEnumerable<WorldEvent> c, IEnumerable<PlayerHeardEntry> e, string root, Func<string, CandidateRejection>? canTell = null)
        {
            var run = new Run();
            var tpl = new Dictionary<string, EventTemplate>(StringComparer.Ordinal)
            {
                ["hero_captured"] = new EventTemplate { Type = "hero_captured", Origin = EventOrigin.Public }
            };
            run.Plan = ProbeClassifier.Classify(
                He, Player, new ChronicleEntry { EventId = root }, e.ToList(), c.ToList(), Today, 7, new VividWorldConfig(),
                t => tpl.TryGetValue(t, out var v) ? v : null,
                id => { run.Asked.Add(id); return canTell != null ? canTell(id) : CandidateRejection.None; },
                id => { run.OffersBuilt++; return new RumorOffer { EventId = id }; },
                null, null);
            return run;
        }

        private static readonly string[] Pair = { "VividWorld_Probe_AlreadyTold_1", "VividWorld_Probe_AlreadyTold_2" };

        [Fact]
        public void Row8_EverythingHeKnowsWasToldByHim_AnswersAlreadyTold_WithoutAnOffer()
        {
            var a = Captured("a1", 30, K(He, 0));
            var run = Go(new[] { a }, new[] { Heard("a1", "a1", Told(He)) }, "a1");

            Assert.Equal(ProbeResultKind.AlreadyTold, run.Plan.ResultKind);
            Assert.Equal("already-told", run.Plan.ResultKindLabel);
            Assert.Contains(run.Plan.SentenceKey, Pair);
            Assert.Null(run.Plan.Offer);
            Assert.Equal("a1", run.Plan.EventId);
            Assert.Contains("already told the player on day 95", run.Plan.LogReason);
            Assert.Empty(run.Asked);
            Assert.Equal(0, run.OffersBuilt);
        }

        [Fact]
        public void AlreadyTold_RotatesBetweenTheTwoSentences_ByHeroAndDay()
        {
            var a = Captured("a1", 30, K(He, 0));
            var heard = new[] { Heard("a1", "a1", Told(He)) };
            var keys = new HashSet<string>();
            for (int day = 100; day < 110; day++)
            {
                var plan = ProbeClassifier.Classify(He, Player, new ChronicleEntry { EventId = "a1" }, heard, new[] { a }, day, 7,
                    new VividWorldConfig(), _ => null, _ => CandidateRejection.None, id => new RumorOffer { EventId = id }, null, null);
                Assert.Equal(ProbeResultKind.AlreadyTold, plan.ResultKind);
                keys.Add(plan.SentenceKey!);
            }
            Assert.Equal(2, keys.Count);
            Assert.All(keys, k => Assert.Contains(k, Pair));
        }

        [Fact]
        public void Row8_OneToldOneNot_TellsTheOneNotTold_AndLogSaysWhyTheOtherWasSkipped()
        {
            var a = Captured("a1", 30, K(He, 0));
            var b = Captured("b1", 20, K(He, 2, "s"));
            var run = Go(new[] { a, b }, new[] { Heard("a1", "a1", Told(He)) }, "a1");

            Assert.Equal(ProbeResultKind.Tell, run.Plan.ResultKind);
            Assert.Equal("b1", run.Plan.EventId);
            Assert.NotNull(run.Plan.Offer);
            Assert.Contains("a1: already told the player on day 95", run.Plan.LogReason);
            Assert.DoesNotContain("a1", run.Asked);
        }

        [Fact]
        public void Row8_ToldPlusReluctant_AnswersAlreadyTold_NotRefuse()
        {
            var a = Captured("a1", 30, K(He, 0));
            var b = Captured("b1", 20, K(He, 2, "s"));
            var run = Go(new[] { a, b }, new[] { Heard("a1", "a1", Told(He)) }, "a1",
                canTell: _ => CandidateRejection.HeldBackShameful);

            Assert.Equal(ProbeResultKind.AlreadyTold, run.Plan.ResultKind);
            Assert.Equal("a1", run.Plan.EventId);
            Assert.Contains("would rather not tell b1", run.Plan.LogReason);
            Assert.Null(run.Plan.Offer);
        }

        [Fact]
        public void Row8_ReluctantOnly_StillRefusesToTell()
        {
            var a = Captured("a1", 30, K(He, 2, "s"));
            var run = Go(new[] { a }, new[] { Heard("a1", "a1") }, "a1", canTell: _ => CandidateRejection.HeldBackShameful);
            Assert.Equal(ProbeResultKind.RefuseToTell, run.Plan.ResultKind);
        }

        [Fact]
        public void Row6_TheRealEventHeSawWasAlreadyToldByHim_AnswersAlreadyTold()
        {
            var l = Captured("l1", 30, K(He, 0));
            l.CaptorArmyLeaderHeroIds = new List<string> { He };
            var x = new WorldEvent
            {
                EventId = "x1",
                Type = "conduct_mistreated_prisoner",
                Day = 50,
                Origin = EventOrigin.Public,
                State = new RumorState { Leaked = true },
                Participants = new Dictionary<string, string>(StringComparer.Ordinal) { ["prisoner"] = "pr", ["captor"] = "cp" },
                Fabricated = true,
                OriginatorHeroId = "orig",
                LinkedEventId = "l1",
                SituationId = "madeup_slander",
                KnownBy = new List<KnownByEntry> { K(Player, 2, "s") }
            };
            var tpl = new Dictionary<string, EventTemplate>(StringComparer.Ordinal)
            {
                ["conduct_mistreated_prisoner"] = new EventTemplate
                {
                    Type = "conduct_mistreated_prisoner",
                    Origin = EventOrigin.Public,
                    Opinions = new List<OpinionDef> { new OpinionDef { About = "x", Amount = -10 } }
                },
                ["hero_captured"] = new EventTemplate { Type = "hero_captured", Origin = EventOrigin.Public }
            };
            var asked = new List<string>();
            var heard = new[]
            {
                new PlayerHeardEntry { EventId = "x1", Type = x.Type, RootEventId = "x1", PlayerHop = 2, UpdatedDay = 90 },
                Heard("l1", "x1", Told(He, 97))
            };
            var plan = ProbeClassifier.Classify(He, Player, new ChronicleEntry { EventId = "x1" }, heard, new[] { x, l }, Today, 7,
                new VividWorldConfig(), t => tpl.TryGetValue(t, out var v) ? v : null,
                id => { asked.Add(id); return CandidateRejection.None; }, id => new RumorOffer { EventId = id }, null, null);

            Assert.Equal(ProbeResultKind.AlreadyTold, plan.ResultKind);
            Assert.Equal("l1", plan.EventId);
            Assert.Null(plan.Offer);
            Assert.Contains("day 97", plan.LogReason);
            Assert.Empty(asked);
        }

        [Fact]
        public void OnlyAProbeAnswer_DoesNotCountAsToldByHim_AndStillTells()
        {
            var a = Captured("a1", 30, K(He, 0));
            var run = Go(new[] { a }, new[] { Heard("a1", "a1", ProbeAnswer(He)) }, "a1");

            Assert.Equal(ProbeResultKind.Tell, run.Plan.ResultKind);
            Assert.NotNull(run.Plan.Offer);
            Assert.Null(ProbeClassifier.ToldByHim(He, "a1", new[] { Heard("a1", "a1", ProbeAnswer(He)) }));
        }

        [Fact]
        public void SomeoneElseToldThePlayer_StillTells()
        {
            var a = Captured("a1", 30, K(He, 0));
            var run = Go(new[] { a }, new[] { Heard("a1", "a1", Told("other")) }, "a1");

            Assert.Equal(ProbeResultKind.Tell, run.Plan.ResultKind);
            Assert.NotNull(run.Plan.Offer);
        }

        [Fact]
        public void ToldByHim_PicksTheEarliestPlainTelling_AndIgnoresOtherEvents()
        {
            var entries = new[]
            {
                Heard("a1", "a1", Told("other", 90), ProbeAnswer(He, 91), Told(He, 97), Told(He, 94)),
                Heard("b1", "a1", Told(He, 80))
            };
            Assert.Equal(94, ProbeClassifier.ToldByHim(He, "a1", entries)!.Day);
            Assert.Null(ProbeClassifier.ToldByHim(He, "zz", entries));
            Assert.Null(ProbeClassifier.ToldByHim("nobody", "a1", entries));
        }

        // ───────────── 開頭語 ─────────────

        private static (RumorOfferSelector selector, WorldEvent evt) NewSelector()
        {
            var cfg = new VividWorldConfig();
            var rng = new SplitMix64Rng();
            var traits = new FakeHeroTraitLookup();
            var engine = new RumorEngine(cfg, FactRetentionPolicies.Create(cfg, rng, 42L), NullEmbellishmentPolicy.Instance,
                new FakePropagationChannel(), traits, rng, 42L, Player);
            var selector = new RumorOfferSelector(cfg, engine, Player, RumorMode.Casual, traits: traits);
            var evt = Captured("a1", 30, K(He, 0));
            evt.Participants["captor"] = "cp";
            evt.Facts = new List<Fact>
            {
                new() { Id = "f_who", Category = FactCategory.Who, Text = "Lord Bob", Fragility = 1 },
                new() { Id = "f_what", Category = FactCategory.What, Text = "was captured", Fragility = 3 }
            };
            return (selector, evt);
        }

        private static RumorCandidate Candidate(WorldEvent evt, int tellerHop, int? playerHop)
            => new RumorCandidate { Event = evt, TellerHop = tellerHop, PlayerExistingHop = playerHop };

        [Fact]
        public void BuildOffer_ForProbe_TheParticipantWhoseListenerAlreadyHeard_DoesNotOpenWithTheSelfRetell()
        {
            var (selector, evt) = NewSelector();
            evt.Participants["prisoner"] = He;
            var profile = new HeroSocialProfile { HeroId = He };

            var normal = selector.BuildOffer(Candidate(evt, 0, 1), profile, Today);
            Assert.Equal(RumorPrefixKind.RetellSelf, normal.Prefix!.Kind);

            var probe = selector.BuildOffer(Candidate(evt, 0, 1), profile, Today, forProbe: true);
            Assert.NotEqual(RumorPrefixKind.RetellSelf, probe.Prefix!.Kind);
            Assert.NotEqual(RumorPrefixKind.Retell, probe.Prefix!.Kind);
            Assert.Equal(RumorPrefixKind.Self, probe.Prefix!.Kind);

            var explicitFalse = selector.BuildOffer(Candidate(evt, 0, 1), profile, Today, forProbe: false);
            Assert.Equal(normal.Prefix!.Kind, explicitFalse.Prefix!.Kind);
            Assert.Equal(normal.Prefix!.TextId, explicitFalse.Prefix!.TextId);
        }

        [Fact]
        public void BuildOffer_ForProbe_ABystanderWhoseListenerAlreadyHeard_StillOpensWithTheRetell()
        {
            var (selector, evt) = NewSelector();
            var profile = new HeroSocialProfile { HeroId = He };

            var normal = selector.BuildOffer(Candidate(evt, 0, 1), profile, Today);
            var probe = selector.BuildOffer(Candidate(evt, 0, 1), profile, Today, forProbe: true);

            Assert.Equal(RumorPrefixKind.Retell, normal.Prefix!.Kind);
            Assert.Equal(RumorPrefixKind.Retell, probe.Prefix!.Kind);
            Assert.Equal(normal.Prefix!.TextId, probe.Prefix!.TextId);
        }

        [Fact]
        public void BuildOffer_ForProbe_ListenerHasNotHeardIt_IsUnchanged()
        {
            var (selector, evt) = NewSelector();
            evt.Participants["prisoner"] = He;
            var profile = new HeroSocialProfile { HeroId = He };

            var normal = selector.BuildOffer(Candidate(evt, 0, null), profile, Today);
            var probe = selector.BuildOffer(Candidate(evt, 0, null), profile, Today, forProbe: true);
            Assert.Equal(normal.Prefix!.Kind, probe.Prefix!.Kind);
            Assert.Equal(normal.Prefix!.TextId, probe.Prefix!.TextId);
        }
    }
}
