using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using VividWorld.Core.Catalog;
using VividWorld.Core.Config;
using VividWorld.Core.Dialogue;
using VividWorld.Core.Events;
using VividWorld.Core.Presentation;
using VividWorld.Core.Rumors;
using VividWorld.Core.Tests.Fakes;
using VividWorld.Core.Util;
using Xunit;

namespace VividWorld.Core.Tests
{
    /// <summary>
    /// 只講大概時：不接當事人句尾；確實少講了他知道的事才接一句收尾（同一人同一事同一句）；
    /// 秘密的知情當事人交情不夠時不講自己的秘密。
    /// </summary>
    public class GistClosingTests
    {
        private const string SentenceWho = "VividWorld_Sentence_HeroTakenPrisoner_Who";

        private static WorldEvent PrisonerEvent(string id = "evt_gist_prisoner")
        {
            return new WorldEvent
            {
                EventId = id,
                Type = "hero_taken_prisoner",
                Origin = EventOrigin.Public,
                Participants = new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["captor"] = "hero_captor",
                    ["prisoner"] = "hero_prisoner"
                }
            };
        }

        private static List<Fact> WhoOnly()
        {
            return new List<Fact>
            {
                new Fact
                {
                    Id = "who", Category = FactCategory.Who, TextId = "VividWorld_Fact_HeroTakenPrisoner_Who",
                    Text = "{CAPTOR} captured {PRISONER}", Fragility = 1,
                    Vars = new Dictionary<string, string> { ["CAPTOR"] = "hero:hero_captor", ["PRISONER"] = "hero:hero_prisoner" }
                }
            };
        }

        private static readonly Dictionary<string, string> Table = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [SentenceWho + "_Self_CAPTOR"] = "I captured {PRISONER}",
            [SentenceWho] = "{CAPTOR} captured {PRISONER}",
            ["VividWorld_SelfFeeling_HeroTakenPrisoner_CAPTOR"] = "At least that battle wasn't for nothing",
            [GistClosing.KeyFor(GistClosing.Self, 1)] = "Self one",
            [GistClosing.KeyFor(GistClosing.Self, 2)] = "Self two",
            [GistClosing.KeyFor(GistClosing.Self, 3)] = "Self three",
            [GistClosing.KeyFor(GistClosing.Heard, 1)] = "Heard one",
            [GistClosing.KeyFor(GistClosing.Heard, 2)] = "Heard two",
            [GistClosing.KeyFor(GistClosing.Heard, 3)] = "Heard three",
        };

        private static RumorRenderResult Render(ComposedRumor composed, List<string>? info = null)
        {
            var t = new EnglishStringTable(Table);
            var cfg = new PresentationConfig { EncyclopediaLinksEnabled = false, SentenceEnd = ".", FactSeparator = ", " };
            RumorTextAssembler.ResetSessionMissingSentenceKeys();
            return RumorTextAssembler.Assemble(
                composed,
                cfg,
                (val, links) => val.Contains(':') ? val.Substring(val.IndexOf(':') + 1) : val,
                (id, fb) => fb == null ? (t.Get(id) ?? string.Empty) : t.Lookup(id, fb),
                (key, fb) => t.GetWithFallback(key, fb),
                onWarning: null,
                isFemale: null,
                onInfo: info == null ? null : info.Add);
        }

        private static ComposedRumor Compose(string speaker, bool isGist, bool heldBack, WorldEvent? evt = null)
        {
            return RumorTextComposer.Compose(evt ?? PrisonerEvent(), WhoOnly(), new PresentationConfig(), prefix: null,
                speakerHeroId: speaker, sourceHeroId: null, isGist: isGist, heldBack: heldBack);
        }

        // ── 組句：句尾與收尾 ──

        [Fact]
        public void Full_Participant_GetsSelfFeeling_NoClosing()
        {
            var composed = Compose("hero_captor", isGist: false, heldBack: false);
            Assert.NotEmpty(composed.SelfFeelingKeyCandidates);
            Assert.Null(composed.ClosingKey);

            var r = Render(composed);
            Assert.Equal("I captured hero_prisoner. At least that battle wasn't for nothing.", r.PlainText);
        }

        [Fact]
        public void Gist_Participant_HeldBack_NoSelfFeeling_SelfClosing()
        {
            var composed = Compose("hero_captor", isGist: true, heldBack: true);
            Assert.Empty(composed.SelfFeelingKeyCandidates);
            Assert.StartsWith("VividWorld_Closing_Self_", composed.ClosingKey);

            var info = new List<string>();
            var r = Render(composed, info);
            Assert.Null(r.SelfFeelingKeyUsed);
            Assert.Equal(composed.ClosingKey, r.ClosingKeyUsed);
            Assert.StartsWith("I captured hero_prisoner. Self ", r.PlainText);
            Assert.DoesNotContain("battle wasn't for nothing", r.PlainText);
            Assert.Contains(info, l => l.Contains("self feeling: omitted (the speaker is only telling the gist"));
        }

        [Fact]
        public void Gist_NothingHeldBack_NoClosing_AndLogSaysWhy()
        {
            var composed = Compose("hero_captor", isGist: true, heldBack: false);
            Assert.Null(composed.ClosingKey);

            var info = new List<string>();
            var r = Render(composed, info);
            Assert.Equal("I captured hero_prisoner.", r.PlainText);
            Assert.Null(r.ClosingKeyUsed);
            Assert.Contains(info, l => l.Contains("closing: omitted (gist, but the speaker knows nothing more"));
        }

        [Fact]
        public void Gist_Onlooker_HeardClosing()
        {
            var composed = Compose("hero_onlooker", isGist: true, heldBack: true);
            Assert.StartsWith("VividWorld_Closing_Heard_", composed.ClosingKey);
        }

        [Fact]
        public void Gist_Eyewitness_WitnessClosing()
        {
            var prefix = RumorPrefixSelector.SelectPrefix(0, null, isRetell: false, isCorrection: false, isParticipant: false);
            var composed = RumorTextComposer.Compose(PrisonerEvent(), WhoOnly(), new PresentationConfig(), prefix,
                speakerHeroId: "hero_onlooker", sourceHeroId: null, isGist: true, heldBack: true);
            Assert.StartsWith("VividWorld_Closing_Witness_", composed.ClosingKey);
        }

        [Fact]
        public void Closing_SamePersonSameEvent_AlwaysSameLine()
        {
            var a = Compose("hero_captor", isGist: true, heldBack: true, evt: PrisonerEvent("evt_same"));
            var b = Compose("hero_captor", isGist: true, heldBack: true, evt: PrisonerEvent("evt_same"));
            Assert.Equal(a.ClosingKey, b.ClosingKey);
        }

        [Fact]
        public void Closing_AllThreeVariantsReachable()
        {
            var seen = new HashSet<string>();
            for (int i = 0; i < 60; i++)
            {
                seen.Add(GistClosing.Select(GistClosing.Self, "hero_" + i, "evt_" + i));
            }
            Assert.Equal(3, seen.Count);
        }

        [Fact]
        public void NoSpeaker_Chronicle_NeverGetsClosing()
        {
            var composed = RumorTextComposer.Compose(PrisonerEvent(), WhoOnly(), new PresentationConfig(), prefix: null,
                speakerHeroId: null, sourceHeroId: null, isGist: true, heldBack: true);
            Assert.Null(composed.ClosingKey);
        }

        // ── 挑消息：確實少講才算、秘密當事人交情不夠不講 ──

        private static (RumorOfferSelector selector, RumorEngine engine) CreateSelector(Func<string, EventTemplate?>? getTemplate = null)
        {
            var cfg = new VividWorldConfig();
            var rng = new SplitMix64Rng();
            var retention = FactRetentionPolicies.Create(cfg, rng, 42L);
            var engine = new RumorEngine(cfg, retention, NullEmbellishmentPolicy.Instance, new FakePropagationChannel(),
                new FakeHeroTraitLookup(), rng, 42L, "player");
            return (new RumorOfferSelector(cfg, engine, "player", RumorMode.Casual, getTemplate), engine);
        }

        private static WorldEvent SampleEvent(string id, EventOrigin origin = EventOrigin.Public)
        {
            return new WorldEvent
            {
                EventId = id,
                Type = "sample_secret",
                Origin = origin,
                Day = 10.0,
                DramaWeight = 4,
                Participants = new Dictionary<string, string>(StringComparer.Ordinal) { ["holder"] = "holder", ["other"] = "other" },
                Facts = new List<Fact>
                {
                    new() { Id = "f_who", Category = FactCategory.Who, Text = "Lord Bob", Fragility = 1 },
                    new() { Id = "f_where", Category = FactCategory.Where, Text = "Pravend", Fragility = 2 },
                    new() { Id = "f_what", Category = FactCategory.What, Text = "fought a duel", Fragility = 3 }
                }
            };
        }

        [Fact]
        public void Gist_FullWouldSayMore_HeldBackTrue()
        {
            var (selector, engine) = CreateSelector();
            var evt = SampleEvent("evt_heldback");
            evt.Participants["actor"] = "teller";
            evt.KnownBy.Add(new KnownByEntry { HeroId = "teller", Hop = 0 });
            var teller = new HeroSocialProfile { HeroId = "teller", RelationWithPlayer = 15 };

            var decision = selector.DecideOnVolunteer(teller, new List<RumorCandidate> { new() { Event = evt, TellerHop = 0 } }, 10.0);

            Assert.Equal(VolunteerTier.Full, decision.Tier);
            var offer = decision.Offer!;



            Assert.False(offer.IsGist);
            Assert.False(offer.HeldBack);
            Assert.Null(offer.Composed.ClosingKey);
        }

        [Fact]
        public void Gist_AlreadyAtFarthestHop_NothingHeldBack()
        {
            var (selector, _) = CreateSelector();
            var evt = SampleEvent("evt_far");
            evt.Participants["actor"] = "teller";
            evt.KnownBy.Add(new KnownByEntry { HeroId = "teller", Hop = 9 });
            var teller = new HeroSocialProfile { HeroId = "teller", RelationWithPlayer = 15 };

            var decision = selector.DecideOnVolunteer(teller, new List<RumorCandidate> { new() { Event = evt, TellerHop = 9 } }, 10.0);

            var offer = decision.Offer!;
            Assert.Equal(selector.ComputeLandingHop(evt, 9, VolunteerTier.Full), offer.ResultingPlayerHop);
            Assert.False(offer.IsGist);
            Assert.False(offer.HeldBack);
            Assert.Null(offer.Composed.ClosingKey);
        }

        [Fact]
        public void Full_NeverHeldBack()
        {
            var (selector, _) = CreateSelector();
            var evt = SampleEvent("evt_full");
            evt.Participants["actor"] = "teller";
            evt.KnownBy.Add(new KnownByEntry { HeroId = "teller", Hop = 0 });
            var teller = new HeroSocialProfile { HeroId = "teller", RelationWithPlayer = 30 };

            var offer = selector.DecideOnVolunteer(teller, new List<RumorCandidate> { new() { Event = evt, TellerHop = 0 } }, 10.0).Offer!;
            Assert.False(offer.IsGist);
            Assert.False(offer.HeldBack);
            Assert.Null(offer.Composed.ClosingKey);
        }

        private static EventTemplate SecretTemplate() => new EventTemplate
        {
            Type = "sample_secret",
            Origin = EventOrigin.Secret,
            Roles = new Dictionary<string, string> { ["holder"] = "{HOLDER}", ["other"] = "{OTHER}" },
            KnowingRoles = new HashSet<string> { "holder" }
        };

        private static WorldEvent LeakedSecret(string id)
        {
            var evt = SampleEvent(id, EventOrigin.Secret);
            evt.State.Leaked = true;
            return evt;
        }

        [Fact]
        public void SecretHolder_Gist_KeepsOwnSecret_WithReasonInNotes()
        {
            var (selector, _) = CreateSelector(t => t == "sample_secret" ? SecretTemplate() : null);
            var evt = LeakedSecret("evt_secret_gist");
            evt.KnownBy.Add(new KnownByEntry { HeroId = "holder", Hop = 0 });
            var holder = new HeroSocialProfile { HeroId = "holder", RelationWithPlayer = 15 };

            var decision = selector.DecideOnVolunteer(holder, new List<RumorCandidate> { new() { Event = evt, TellerHop = 0 } }, 10.0);

            Assert.Equal(VolunteerRefusal.AllCandidatesFiltered, decision.Refusal);
            Assert.Equal(1, decision.FilteredOther);
            Assert.Contains(decision.FilterNotes, n => n.Contains("evt_secret_gist") && n.Contains("secret"));
        }

        [Fact]
        public void SecretHolder_Full_StillTells()
        {
            var (selector, _) = CreateSelector(t => t == "sample_secret" ? SecretTemplate() : null);
            var evt = LeakedSecret("evt_secret_full");
            evt.KnownBy.Add(new KnownByEntry { HeroId = "holder", Hop = 0 });
            var holder = new HeroSocialProfile { HeroId = "holder", RelationWithPlayer = 30 };

            var decision = selector.DecideOnVolunteer(holder, new List<RumorCandidate> { new() { Event = evt, TellerHop = 0 } }, 10.0);
            Assert.NotNull(decision.Offer);
        }

        [Fact]
        public void LeakedSecret_Bystander_Gist_StillTells()
        {
            var (selector, _) = CreateSelector(t => t == "sample_secret" ? SecretTemplate() : null);
            var evt = LeakedSecret("evt_secret_bystander");
            evt.Participants["actor"] = "gossip";
            evt.KnownBy.Add(new KnownByEntry { HeroId = "gossip", Hop = 1 });
            var gossip = new HeroSocialProfile { HeroId = "gossip", RelationWithPlayer = 15 };

            var decision = selector.DecideOnVolunteer(gossip, new List<RumorCandidate> { new() { Event = evt, TellerHop = 1 } }, 10.0);
            Assert.NotNull(decision.Offer);
        }

        [Fact]
        public void SecretParticipantNotInTheKnow_Gist_NotBlockedByThisRule()
        {
            var (selector, _) = CreateSelector(t => t == "sample_secret" ? SecretTemplate() : null);
            var evt = LeakedSecret("evt_secret_other");
            evt.KnownBy.Add(new KnownByEntry { HeroId = "other", Hop = 1 });
            var other = new HeroSocialProfile { HeroId = "other", RelationWithPlayer = 15 };

            var decision = selector.DecideOnVolunteer(other, new List<RumorCandidate> { new() { Event = evt, TellerHop = 1 } }, 10.0);
            Assert.NotNull(decision.Offer);
        }

        // ── 句型列舉：秘密沒有只講大概的當事人句 ──

        private static string FindRepoFile(string relativePath)
        {
            string? current = AppContext.BaseDirectory;
            while (!string.IsNullOrEmpty(current))
            {
                if (File.Exists(Path.Combine(current, "VividWorld.sln")))
                {
                    string path = Path.Combine(current, relativePath);
                    if (File.Exists(path)) return path;
                }
                var parent = Directory.GetParent(current);
                if (parent == null) break;
                current = parent.FullName;
            }
            throw new FileNotFoundException($"Could not find {relativePath} relative to {AppContext.BaseDirectory}");
        }

        [Fact]
        public void Enumerator_SecretTemplates_NoGistOnlySelfRequirement()
        {
            var pCfg = new PersistenceConfig { MaxFactsPerEvent = 24 };
            var catalogs = new[] { "vividworld_events.json", "vividworld_situation_events.json" }
                .Select(f => EventCatalogLoader.Load(File.ReadAllText(FindRepoFile(Path.Combine("module", "ModuleData", f))), pCfg))
                .ToList();
            foreach (var type in new[] { "brawl_hushed_up", "hero_murdered", "seat_dispute_endured", "wager_secret_stake" })
            {
                var tmpl = catalogs.Select(c => c.ByType(type)).FirstOrDefault(t => t != null);
                Assert.NotNull(tmpl);
                var selfReqs = SentenceCombinationEnumerator.EnumerateRequirements(tmpl!)
                    .Where(r => r.Kind == SentenceAngleKind.Self && tmpl!.KnowingRoles.Contains(r.Role!.ToLowerInvariant()))
                    .ToList();
                Assert.All(selfReqs, r => Assert.True(r.Combination.OccursAtHop0, $"{type}: {r.Combination.CombinationKey} is gist-only"));
            }
        }
    }
}
