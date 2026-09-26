#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using VividWorld.Core.Config;
using VividWorld.Core.Dialogue;
using VividWorld.Core.Events;
using VividWorld.Core.Persistence;
using VividWorld.Core.Rumors;
using VividWorld.Core.Tests.Fakes;
using VividWorld.Core.Util;
using Xunit;

namespace VividWorld.Core.Tests
{
    public class ListenPreviewTests
    {
        private WorldEvent CreateSampleEvent(string id, double day = 10.0, EventOrigin origin = EventOrigin.Public, string? tellerHeroId = "teller_1")
        {
            var evt = new WorldEvent
            {
                EventId = id,
                Origin = origin,
                Day = day,
                DramaWeight = 3,
                Facts = new List<Fact>
                {
                    new() { Id = "f_who", Category = FactCategory.Who, Text = "Lord Bob", Fragility = 1 }
                }
            };
            if (!string.IsNullOrEmpty(tellerHeroId))
            {
                evt.KnownBy.Add(new KnownByEntry { HeroId = tellerHeroId!, Hop = 1 });
            }
            return evt;
        }

        private (RumorOfferSelector selector, RumorEngine engine, VividWorldConfig cfg)
            CreateSelector(string playerHeroId = "player", long seed = 42L)
        {
            var cfg = new VividWorldConfig();
            var rng = new SplitMix64Rng();
            var channel = new FakePropagationChannel();
            var traits = new FakeHeroTraitLookup();
            var retention = FactRetentionPolicies.Create(cfg, rng, seed);
            var embellishment = NullEmbellishmentPolicy.Instance;
            var engine = new RumorEngine(cfg, retention, embellishment, channel, traits, rng, seed, playerHeroId);
            var selector = new RumorOfferSelector(cfg, engine, playerHeroId);
            return (selector, engine, cfg);
        }

        [Fact]
        public void ListenPreview_Aggregator_CoversEveryBucket()
        {
            // 摘要聚合每一格一條: covers each bucket in volunteer, topic-only, ask, relation histogram, top 5 names
            var (selector, _, cfg) = CreateSelector();
            var people = new List<ListenPreviewPerson>();

            // 1. Six lords with valid candidate and relation >= 30 -> VolunteerTold, TopicOnly hasTopic, AskTold
            // 6 heroes ensures top-5 names cap is exercised
            for (int i = 0; i < 6; i++)
            {
                string hid = $"hero_told_{i}";
                var evt = CreateSampleEvent($"evt_told_{i}", day: 10.0, tellerHeroId: hid);
                var cand = new RumorCandidate { Event = evt, TellerHop = 1, PlayerExistingHop = null };
                people.Add(new ListenPreviewPerson
                {
                    HeroId = hid,
                    HeroName = $"ToldLord_{i}",
                    IsLord = true,
                    Profile = new HeroSocialProfile { HeroId = hid, RelationWithPlayer = 35 },
                    Candidates = new[] { cand },
                    KnownCount = 1,
                    ForgottenCount = 0,
                    OutdatedCount = 0,
                    UnstampedCount = 0
                });
            }

            // 2. Low relation wanderer (-15) with candidate -> VolunteerBlockedRelationGate, TopicOnly hasTopic, AskRefusedRelationGate
            {
                string hid = "hero_low_rel";
                var evt = CreateSampleEvent("evt_low_rel", day: 10.0, tellerHeroId: hid);
                var cand = new RumorCandidate { Event = evt, TellerHop = 1, PlayerExistingHop = null };
                people.Add(new ListenPreviewPerson
                {
                    HeroId = hid,
                    HeroName = "LowRelWanderer",
                    IsLord = false,
                    Profile = new HeroSocialProfile { HeroId = hid, RelationWithPlayer = -15 },
                    Candidates = new[] { cand },
                    KnownCount = 1,
                    ForgottenCount = 0,
                    OutdatedCount = 0,
                    UnstampedCount = 1
                });
            }

            // 3. Wanderer on cooldown -> VolunteerBlockedCooldown, TopicOnly hasTopic, AskTold
            {
                string hid = "hero_cd";
                var evt = CreateSampleEvent("evt_cd", day: 10.0, tellerHeroId: hid);
                var cand = new RumorCandidate { Event = evt, TellerHop = 1, PlayerExistingHop = null };
                people.Add(new ListenPreviewPerson
                {
                    HeroId = hid,
                    HeroName = "CooldownWanderer",
                    IsLord = false,
                    Profile = new HeroSocialProfile
                    {
                        HeroId = hid,
                        RelationWithPlayer = 35,
                        LastVolunteeredDay = 9.5 // Day is 10.0, cooldown is 3.0 days -> Cooldown
                    },
                    Candidates = new[] { cand },
                    KnownCount = 1,
                    ForgottenCount = 0,
                    OutdatedCount = 0,
                    UnstampedCount = 0
                });
            }

            // 4. Zero known events -> VolunteerNoTopicNothingOnFile, TopicOnly nothingOnFile, AskNoTopicNothingOnFile
            people.Add(new ListenPreviewPerson
            {
                HeroId = "hero_nothing",
                HeroName = "NothingHero",
                IsLord = true,
                Profile = new HeroSocialProfile { HeroId = "hero_nothing", RelationWithPlayer = 35 },
                Candidates = Array.Empty<RumorCandidate>(),
                KnownCount = 0,
                ForgottenCount = 0,
                OutdatedCount = 0,
                UnstampedCount = 0
            });

            // 5. All forgotten -> VolunteerNoTopicAllForgotten, TopicOnly allForgotten, AskNoTopicAllForgotten
            people.Add(new ListenPreviewPerson
            {
                HeroId = "hero_forgotten",
                HeroName = "ForgottenHero",
                IsLord = false,
                Profile = new HeroSocialProfile { HeroId = "hero_forgotten", RelationWithPlayer = 35 },
                Candidates = Array.Empty<RumorCandidate>(),
                KnownCount = 2,
                ForgottenCount = 2,
                OutdatedCount = 0,
                UnstampedCount = 0
            });

            // 6. All outdated -> VolunteerNoTopicAllOutdated, TopicOnly allOutdated, AskNoTopicAllOutdated
            people.Add(new ListenPreviewPerson
            {
                HeroId = "hero_outdated",
                HeroName = "OutdatedHero",
                IsLord = true,
                Profile = new HeroSocialProfile { HeroId = "hero_outdated", RelationWithPlayer = 35 },
                Candidates = Array.Empty<RumorCandidate>(),
                KnownCount = 2,
                ForgottenCount = 0,
                OutdatedCount = 2,
                UnstampedCount = 0
            });

            // 7. Forgotten or outdated mixed -> VolunteerNoTopicForgottenOrOutdated, TopicOnly forgottenOrOutdated, AskNoTopicForgottenOrOutdated
            people.Add(new ListenPreviewPerson
            {
                HeroId = "hero_mixed",
                HeroName = "MixedHero",
                IsLord = true,
                Profile = new HeroSocialProfile { HeroId = "hero_mixed", RelationWithPlayer = 35 },
                Candidates = Array.Empty<RumorCandidate>(),
                KnownCount = 2,
                ForgottenCount = 1,
                OutdatedCount = 1,
                UnstampedCount = 0
            });

            // 8. Candidates exist but all filtered (secret event) -> VolunteerFiltered, TopicOnly filtered, AskFiltered
            {
                string hid = "hero_filt";
                var evt = CreateSampleEvent("evt_sec", day: 10.0, origin: EventOrigin.Secret, tellerHeroId: hid);
                var cand = new RumorCandidate { Event = evt, TellerHop = 1, PlayerExistingHop = null };
                people.Add(new ListenPreviewPerson
                {
                    HeroId = hid,
                    HeroName = "FilteredHero",
                    IsLord = false,
                    Profile = new HeroSocialProfile { HeroId = hid, RelationWithPlayer = 35 },
                    Candidates = new[] { cand },
                    KnownCount = 1,
                    ForgottenCount = 0,
                    OutdatedCount = 0,
                    UnstampedCount = 1
                });
            }

            var result = ListenPreviewAggregator.Generate(
                selector,
                compat: null,
                playerClanTier: 2,
                volunteersAlreadyToday: 0,
                day: 10.0,
                cfg.Dialogue,
                people,
                elapsedMs: 25,
                includeHeroDetails: true);

            Assert.Equal(13, result.TotalNetworkCount);
            Assert.Equal(9, result.LordCount); // 6 + 1 (nothing) + 1 (outdated) + 1 (mixed) = 9
            Assert.Equal(4, result.WandererCount); // 1 (low_rel) + 1 (cd) + 1 (forgotten) + 1 (filt) = 4
            Assert.Equal(0, result.VolunteersToday);
            Assert.Equal(cfg.Dialogue.MaxVolunteersPerDay, result.MaxVolunteersPerDay);
            Assert.Equal(25, result.ElapsedMilliseconds);
            Assert.Equal(2, result.UnstampedEntriesCount); // 1 from low_rel, 1 from filt
            Assert.False(result.AskBlockedByClanTier);

            // Volunteer Counts
            Assert.Equal(6, result.VolunteerCounts[ListenTallyKeys.VolunteerTold]);
            Assert.Equal(5, result.VolunteerNames[ListenTallyKeys.VolunteerTold].Count); // Top 5 capped
            Assert.Equal(1, result.VolunteerCounts[ListenTallyKeys.VolunteerBlockedRelationGate]);
            Assert.Equal(1, result.VolunteerCounts[ListenTallyKeys.VolunteerBlockedCooldown]);
            Assert.Equal(1, result.VolunteerCounts[ListenTallyKeys.VolunteerNoTopicNothingOnFile]);
            Assert.Equal(1, result.VolunteerCounts[ListenTallyKeys.VolunteerNoTopicAllForgotten]);
            Assert.Equal(1, result.VolunteerCounts[ListenTallyKeys.VolunteerNoTopicAllOutdated]);
            Assert.Equal(1, result.VolunteerCounts[ListenTallyKeys.VolunteerNoTopicForgottenOrOutdated]);
            Assert.Equal(1, result.VolunteerCounts[ListenTallyKeys.VolunteerFiltered]);

            // Topic-Only Counts
            Assert.Equal(8, result.TopicOnlyCounts["hasTopic"]); // 6 told + 1 low_rel + 1 cd
            Assert.Equal(1, result.TopicOnlyCounts["nothingOnFile"]);
            Assert.Equal(1, result.TopicOnlyCounts["allForgotten"]);
            Assert.Equal(1, result.TopicOnlyCounts["allOutdated"]);
            Assert.Equal(1, result.TopicOnlyCounts["forgottenOrOutdated"]);
            Assert.Equal(1, result.TopicOnlyCounts["filtered"]);

            // Ask Counts
            Assert.Equal(7, result.AskCounts[ListenTallyKeys.AskTold]); // 6 told + 1 cd (cd only blocks volunteer, not ask)
            Assert.Equal(1, result.AskCounts[ListenTallyKeys.AskRefusedRelationGate]);
            Assert.Equal(1, result.AskCounts[ListenTallyKeys.AskNoTopicNothingOnFile]);
            Assert.Equal(1, result.AskCounts[ListenTallyKeys.AskNoTopicAllForgotten]);
            Assert.Equal(1, result.AskCounts[ListenTallyKeys.AskNoTopicAllOutdated]);
            Assert.Equal(1, result.AskCounts[ListenTallyKeys.AskNoTopicForgottenOrOutdated]);
            Assert.Equal(1, result.AskCounts[ListenTallyKeys.AskFiltered]);

            // Ask Refusal Lines (3b)
            Assert.Equal(7, result.AskRefusalCounts["told"]);
            Assert.Equal(5, result.AskRefusalNames["told"].Count);
            Assert.Equal(1, result.AskRefusalCounts[nameof(AskRefusalLineKind.Unwilling)]);
            Assert.Equal(1, result.AskRefusalCounts[nameof(AskRefusalLineKind.NothingHeard)]);
            Assert.Equal(2, result.AskRefusalCounts[nameof(AskRefusalLineKind.Forgotten)]); // 1 allForgotten + 1 forgottenOrOutdated
            Assert.Equal(1, result.AskRefusalCounts[nameof(AskRefusalLineKind.Outdated)]);
            Assert.Equal(1, result.AskRefusalCounts[nameof(AskRefusalLineKind.Other)]);

            // Hero Details
            Assert.Equal(13, result.HeroDetails.Count);
        }

        [Fact]
        public void ListenPreview_AskCountedEvenWhenCommonerTierBlocks()
        {
            // 「等級擋住但 DecideOnAsk 會講」算進 ask 的兩欄
            // When CommonerCompat.BlocksAsk is true, heroes qualifying for ask are still counted
            // in AskCounts (e.g. AskTold and AskRefusedRelationGate) to show post-tier distribution.
            var (selector, _, cfg) = CreateSelector();
            var compat = new CommonerCompatState { Active = true, AskMinClanTier = 3 };

            var evtA = CreateSampleEvent("evt_ask_friendly", day: 10.0, tellerHeroId: "lord_friendly");
            var candA = new RumorCandidate { Event = evtA, TellerHop = 1, PlayerExistingHop = null };

            var evtB = CreateSampleEvent("evt_ask_unfriendly", day: 10.0, tellerHeroId: "lord_unfriendly");
            var candB = new RumorCandidate { Event = evtB, TellerHop = 1, PlayerExistingHop = null };

            var people = new List<ListenPreviewPerson>
            {
                new()
                {
                    HeroId = "lord_friendly",
                    HeroName = "Friendly Lord",
                    IsLord = true,
                    Profile = new HeroSocialProfile { HeroId = "lord_friendly", RelationWithPlayer = 30 },
                    Candidates = new[] { candA },
                    KnownCount = 1
                },
                new()
                {
                    HeroId = "lord_unfriendly",
                    HeroName = "Unfriendly Lord",
                    IsLord = true,
                    Profile = new HeroSocialProfile { HeroId = "lord_unfriendly", RelationWithPlayer = -20 },
                    Candidates = new[] { candB },
                    KnownCount = 1
                }
            };

            // Player clan tier is 1, below AskMinClanTier 3 -> BLOCKED
            var result = ListenPreviewAggregator.Generate(
                selector,
                compat,
                playerClanTier: 1,
                volunteersAlreadyToday: 0,
                day: 10.0,
                cfg.Dialogue,
                people);

            Assert.True(result.AskBlockedByClanTier);
            Assert.Contains("BLOCKED", result.AskClanTierStatus);

            // Despite clan tier being blocked, post-gate ask counts are populated
            Assert.Equal(1, result.AskCounts[ListenTallyKeys.AskTold]);
            Assert.Equal(1, result.AskCounts[ListenTallyKeys.AskRefusedRelationGate]);
            Assert.Equal("Friendly Lord", result.AskNames[ListenTallyKeys.AskTold][0]);
            Assert.Equal("Unfriendly Lord", result.AskNames[ListenTallyKeys.AskRefusedRelationGate][0]);
        }

        [Fact]
        public void RumorOfferSelector_ClassifyCandidates_MatchesPreExtractionBehavior()
        {
            // ClassifyCandidates 抽出前後計數一致
            var (selector, _, _) = CreateSelector();
            var teller = new HeroSocialProfile { HeroId = "teller_1", RelationWithPlayer = 35 };

            var evtVisible = CreateSampleEvent("evt_vis", day: 10.0, origin: EventOrigin.Public, tellerHeroId: "teller_1");
            var evtSecret = CreateSampleEvent("evt_sec", day: 10.0, origin: EventOrigin.Secret, tellerHeroId: "teller_1");
            var evtFuture = CreateSampleEvent("evt_fut", day: 50.0, origin: EventOrigin.Public, tellerHeroId: "teller_1");
            var evtPlayerKnows = CreateSampleEvent("evt_pk", day: 10.0, origin: EventOrigin.Public, tellerHeroId: "teller_1");
            evtPlayerKnows.KnownBy.Add(new KnownByEntry { HeroId = "player", Hop = 2 });
            var evtNotKnownByTeller = CreateSampleEvent("evt_nk", day: 10.0, origin: EventOrigin.Public, tellerHeroId: null);

            var candidates = new List<RumorCandidate>
            {
                new() { Event = evtVisible, TellerHop = 1, PlayerExistingHop = null },
                new() { Event = evtSecret, TellerHop = 0, PlayerExistingHop = null },
                new() { Event = evtFuture, TellerHop = 1, PlayerExistingHop = null },
                new() { Event = evtPlayerKnows, TellerHop = 2, PlayerExistingHop = 2 }, // TellerHop+1 = 3 >= 2 -> filtered
                new() { Event = evtNotKnownByTeller, TellerHop = 1, PlayerExistingHop = null } // Teller does not know -> other
            };

            double day = 15.0;

            // Pre-extraction manual classification simulation matching Evaluate()
            int expectedEligible = 0;
            int expectedFilteredNotVisible = 0;
            int expectedFilteredFuture = 0;
            int expectedFilteredPlayerKnows = 0;
            int expectedFilteredOther = 0;

            foreach (var c in candidates)
            {
                if (c?.Event == null)
                {
                    expectedFilteredOther++;
                    continue;
                }
                if (!c.Event.IsVisibleToRumorSystem)
                {
                    expectedFilteredNotVisible++;
                    continue;
                }
                if (!EventVisibility.IsVisibleOn(c.Event, day))
                {
                    expectedFilteredFuture++;
                    continue;
                }
                if (!c.Event.IsKnownBy(teller.HeroId))
                {
                    expectedFilteredOther++;
                    continue;
                }
                if (c.PlayerExistingHop.HasValue && c.TellerHop + 1 >= c.PlayerExistingHop.Value)
                {
                    expectedFilteredPlayerKnows++;
                    continue;
                }
                expectedEligible++;
            }

            // Call extracted ClassifyCandidates
            var classification = selector.ClassifyCandidates(teller, candidates, day);

            Assert.Equal(expectedEligible, classification.Eligible.Count);
            Assert.Equal(expectedFilteredNotVisible, classification.FilteredNotVisible);
            Assert.Equal(expectedFilteredFuture, classification.FilteredFutureTimeline);
            Assert.Equal(expectedFilteredPlayerKnows, classification.FilteredPlayerKnows);
            Assert.Equal(expectedFilteredOther, classification.FilteredOther);
            Assert.Equal(expectedEligible + expectedFilteredNotVisible + expectedFilteredFuture + expectedFilteredPlayerKnows + expectedFilteredOther,
                         classification.Eligible.Count + classification.FilteredTotal);

            Assert.Single(classification.Eligible);
            Assert.Equal("evt_vis", classification.Eligible[0].Event.EventId);
            Assert.Equal(1, classification.FilteredNotVisible);
            Assert.Equal(1, classification.FilteredFutureTimeline);
            Assert.Equal(1, classification.FilteredPlayerKnows);
            Assert.Equal(1, classification.FilteredOther);
            Assert.Equal(4, classification.FilteredTotal);

            // Also verify that DecideOnVolunteer and DecideOnAsk execute cleanly using this classification
            var volDecision = selector.DecideOnVolunteer(teller, candidates, day, volunteersAlreadyToday: 0);
            Assert.Equal(VolunteerRefusal.None, volDecision.Refusal);
            Assert.NotNull(volDecision.Offer);
            Assert.Equal("evt_vis", volDecision.Offer!.EventId);

            var askDecision = selector.DecideOnAsk(teller, candidates, day);
            Assert.Equal(AskRefusal.None, askDecision.Refusal);
            Assert.NotNull(askDecision.Offer);
            Assert.Equal("evt_vis", askDecision.Offer!.EventId);
        }

        [Fact]
        public void ListenTally_NewKeys_IncrementAndRoundTrip()
        {
            // 三個新鍵的分類與往返
            var tally = new ListenTally { Version = 1 };
            var dayTally = tally.GetOrCreateDay(25);

            dayTally.Increment(ListenTallyKeys.NotInNetwork, 5);
            dayTally.Increment(ListenTallyKeys.NotInNetworkNoHero, 3);
            dayTally.Increment(ListenTallyKeys.AskShown, 4);
            dayTally.Increment(ListenTallyKeys.AskBlockedCommonerTier, 2);

            Assert.Equal(5, dayTally.GetCount(ListenTallyKeys.NotInNetwork));
            Assert.Equal(3, dayTally.GetCount(ListenTallyKeys.NotInNetworkNoHero));
            Assert.Equal(4, dayTally.GetCount(ListenTallyKeys.AskShown));
            Assert.Equal(2, dayTally.GetCount(ListenTallyKeys.AskBlockedCommonerTier));

            // Round-trip through JSON
            string json = VividJson.Write(tally);
            var loaded = VividJson.Read<ListenTally>(json);

            Assert.NotNull(loaded);
            var loadedDay = loaded!.GetDay(25);
            Assert.NotNull(loadedDay);
            Assert.Equal(5, loadedDay!.GetCount(ListenTallyKeys.NotInNetwork));
            Assert.Equal(3, loadedDay.GetCount(ListenTallyKeys.NotInNetworkNoHero));
            Assert.Equal(4, loadedDay.GetCount(ListenTallyKeys.AskShown));
            Assert.Equal(2, loadedDay.GetCount(ListenTallyKeys.AskBlockedCommonerTier));
        }

        [Fact]
        public void ListenTally_BackwardCompatibility_LoadsOldFormatWithoutNewKeys()
        {
            // 舊格式 listen_tally.json 讀得進來
            string oldJson = @"{
  ""version"": 1,
  ""days"": {
    ""10"": {
      ""volunteer.told"": 2,
      ""volunteer.blocked.relationGate"": 3,
      ""notInNetwork"": 1,
      ""ask.asked"": 1,
      ""ask.told"": 1,
      ""relationHist"": {
        ""10..19"": 2
      },
      ""distinctPartners"": 2,
      ""eventsOnFile"": 4,
      ""eventsRemembered"": 3
    }
  }
}";

            var loaded = VividJson.Read<ListenTally>(oldJson);

            Assert.NotNull(loaded);
            Assert.Equal(1, loaded!.Version);
            var day10 = loaded.GetDay(10);
            Assert.NotNull(day10);
            Assert.Equal(2, day10!.GetCount(ListenTallyKeys.VolunteerTold));
            Assert.Equal(3, day10.GetCount(ListenTallyKeys.VolunteerBlockedRelationGate));
            Assert.Equal(1, day10.GetCount(ListenTallyKeys.NotInNetwork));
            Assert.Equal(1, day10.GetCount(ListenTallyKeys.AskAsked));
            Assert.Equal(1, day10.GetCount(ListenTallyKeys.AskTold));

            // Verify the 3 new keys default to 0 without errors or missing key exceptions
            Assert.Equal(0, day10.GetCount(ListenTallyKeys.NotInNetworkNoHero));
            Assert.Equal(0, day10.GetCount(ListenTallyKeys.AskShown));
            Assert.Equal(0, day10.GetCount(ListenTallyKeys.AskBlockedCommonerTier));
        }

        [Fact]
        public void ListenPreview_FormatSummary_OutputsExpectedSections()
        {
            // 摘要格式化一條
            var (selector, _, cfg) = CreateSelector();
            cfg.Dialogue.MaxVolunteersPerDay = 4;

            var evt = CreateSampleEvent("evt_derthert", day: 10.0, tellerHeroId: "lord_derthert");
            var cand = new RumorCandidate { Event = evt, TellerHop = 1, PlayerExistingHop = null };

            var people = new List<ListenPreviewPerson>
            {
                new()
                {
                    HeroId = "lord_derthert",
                    HeroName = "King Derthert",
                    IsLord = true,
                    Profile = new HeroSocialProfile { HeroId = "lord_derthert", RelationWithPlayer = 35 },
                    Candidates = new[] { cand },
                    KnownCount = 1
                },
                new()
                {
                    HeroId = "wanderer_bob",
                    HeroName = "Bob the Wanderer",
                    IsLord = false,
                    Profile = new HeroSocialProfile { HeroId = "wanderer_bob", RelationWithPlayer = -10 },
                    Candidates = Array.Empty<RumorCandidate>(),
                    KnownCount = 0
                }
            };

            var result = ListenPreviewAggregator.Generate(
                selector,
                compat: new CommonerCompatState { Active = true, AskMinClanTier = 2 },
                playerClanTier: 1,
                volunteersAlreadyToday: 1,
                day: 10.0,
                cfg.Dialogue,
                people,
                elapsedMs: 8,
                includeHeroDetails: true);

            string summary = ListenPreviewLogFormatter.FormatSummary(result, includeTopNames: true);

            Assert.Contains("=== Listen Preview (What if I talk to everyone right now?) ===", summary);
            Assert.Contains("Network: 2 heroes (1 lords, 1 wanderers) | Elapsed: 8ms", summary);
            Assert.Contains("Volunteers today: 1/4", summary);
            Assert.Contains("Notice: lordAttack not previewed (conversation-only)", summary);

            Assert.Contains("[1. Volunteer Path (as-is)]", summary);
            Assert.Contains("told: 1 (King Derthert)", summary);
            Assert.Contains("blocked.relationGate: 1 (Bob the Wanderer)", summary);

            Assert.Contains("[2. Volunteer Path (Topic Only - ignoring relation, cooldown, daily cap)]", summary);
            Assert.Contains("hasTopic: 1 (King Derthert)", summary);
            Assert.Contains("nothingOnFile: 1 (Bob the Wanderer)", summary);

            Assert.Contains("[3. Ask Path (Commoner clan tier: tier 1 < 2 (BLOCKED))]", summary);
            Assert.Contains("told: 1 (King Derthert)", summary);
            Assert.Contains("refused.relationGate: 1 (Bob the Wanderer)", summary);

            Assert.Contains("[3b. Ask refusal lines (as if the clan-tier gate passed)]", summary);
            Assert.Contains("told: 1 (King Derthert)", summary);
            Assert.Contains("Unwilling: 1 (Bob the Wanderer)", summary);

            Assert.Contains("[4. Relation Histogram]", summary);
            Assert.Contains("-10..-1: 1", summary);
            Assert.Contains("30..39: 1", summary);

            // Also test hero table output
            string table = ListenPreviewLogFormatter.FormatHeroTable(result);
            Assert.Contains("=== Listen Preview Person Details (2 heroes) ===", table);
            Assert.Contains("King Derthert (lord_derthert) | Lord | Rel: 35 | volunteer.told | hasTopic | ask.told", table);
            Assert.Contains("Bob the Wanderer (wanderer_bob) | Wanderer | Rel: -10 | volunteer.blocked.relationGate | nothingOnFile | ask.refused.relationGate", table);
        }

        [Fact]
        public void ListenTally_FormatDailyLine_IncludesAskShownAndClanTierBlocked()
        {
            var dayTally = new ListenDayTally();
            dayTally.Increment(ListenTallyKeys.VolunteerTold, 1);
            dayTally.Increment(ListenTallyKeys.AskTold, 2);
            dayTally.Increment(ListenTallyKeys.AskShown, 5);
            dayTally.Increment(ListenTallyKeys.AskBlockedCommonerTier, 3);
            dayTally.RecordPartner("hero_1", 20);

            string line = ListenTallyLogFormatter.FormatDailyLine(15, dayTally);

            Assert.StartsWith("Listen tally day 15: talked 1 (network 1, distinct 1) | told 1 volunteered + 2 asked | top blockers:", line);
            Assert.EndsWith("| ask shown 5, ask blocked by clan tier 3", line);
        }

        [Fact]
        public void DailyCounter_CountOn_DoesNotReportYesterdaysCountAfterDayChange()
        {
            // 預演不准呼叫 Advance；換日那一刻 Count 還是昨天的值，CountOn 必須回 0
            var counter = new DailyCounter();
            counter.Advance(41.3);
            counter.Increment();

            Assert.Equal(1, counter.CountOn(41.9));
            Assert.Equal(0, counter.CountOn(42.0));
            Assert.Equal(1, counter.Count); // 沒被動到
            Assert.Equal(0, new DailyCounter().CountOn(10.0));
        }

        [Fact]
        public void ClassifyNoTopic_SameAnswerForVolunteerAskAndPreview()
        {
            Assert.Equal(NoTopicReason.NothingOnFile, ListenTallyClassifier.ClassifyNoTopic(0, 0, 0));
            Assert.Equal(NoTopicReason.AllForgotten, ListenTallyClassifier.ClassifyNoTopic(3, 3, 0));
            Assert.Equal(NoTopicReason.AllOutdated, ListenTallyClassifier.ClassifyNoTopic(2, 0, 2));
            Assert.Equal(NoTopicReason.ForgottenOrOutdated, ListenTallyClassifier.ClassifyNoTopic(3, 1, 2));

            var vol = new VolunteerDecision { Refusal = VolunteerRefusal.NoKnownEvents };
            Assert.Equal(ListenTallyKeys.VolunteerNoTopicAllOutdated,
                ListenTallyClassifier.ClassifyVolunteer(true, true, 0, false, false, vol, 2, 0, 2, false));
            var ask = new AskDecision { Refusal = AskRefusal.NoKnownEvents };
            Assert.Equal(ListenTallyKeys.AskNoTopicForgottenOrOutdated,
                ListenTallyClassifier.ClassifyAsk(true, false, ask, 3, 1, 2));
        }

        [Fact]
        public void ListenPreview_FormatSummary_ShowsDayTriggerAndTierNote()
        {
            var result = new ListenPreviewResult { Day = 91120, AskBlockedByClanTier = true, AskClanTierStatus = "tier 0 < 1 (BLOCKED)" };
            string summary = ListenPreviewLogFormatter.FormatSummary(result, includeTopNames: false, trigger: "on load");

            Assert.Contains("Day 91120 | trigger: on load", summary);
            Assert.Contains("counts below are as if the clan-tier gate passed", summary);

            string allowed = ListenPreviewLogFormatter.FormatSummary(new ListenPreviewResult { Day = 5 }, trigger: "daily");
            Assert.DoesNotContain("as if the clan-tier gate passed", allowed);
        }

        [Fact]
        public void ListenPreview_AskRefusalLines_CoversAllCategories()
        {
            var (selector, _, cfg) = CreateSelector();
            var people = new List<ListenPreviewPerson>();

            // 1. told
            {
                var evt = CreateSampleEvent("evt_told", day: 10.0, tellerHeroId: "h_told");
                people.Add(new ListenPreviewPerson
                {
                    HeroId = "h_told",
                    HeroName = "ToldHero",
                    Profile = new HeroSocialProfile { HeroId = "h_told", RelationWithPlayer = 35 },
                    Candidates = new[] { new RumorCandidate { Event = evt, TellerHop = 1, PlayerExistingHop = null } },
                    KnownCount = 1
                });
            }

            // 2. Unwilling (relation < 0)
            {
                var evt = CreateSampleEvent("evt_unwill", day: 10.0, tellerHeroId: "h_unwill");
                people.Add(new ListenPreviewPerson
                {
                    HeroId = "h_unwill",
                    HeroName = "UnwillingHero",
                    Profile = new HeroSocialProfile { HeroId = "h_unwill", RelationWithPlayer = -10 },
                    Candidates = new[] { new RumorCandidate { Event = evt, TellerHop = 1, PlayerExistingHop = null } },
                    KnownCount = 1
                });
            }

            // 3. NothingHeard (0 known)
            people.Add(new ListenPreviewPerson
            {
                HeroId = "h_nothing",
                HeroName = "NothingHero",
                Profile = new HeroSocialProfile { HeroId = "h_nothing", RelationWithPlayer = 35 },
                Candidates = Array.Empty<RumorCandidate>(),
                KnownCount = 0
            });

            // 4. Forgotten (all forgotten)
            people.Add(new ListenPreviewPerson
            {
                HeroId = "h_forgotten",
                HeroName = "ForgottenHero",
                Profile = new HeroSocialProfile { HeroId = "h_forgotten", RelationWithPlayer = 35 },
                Candidates = Array.Empty<RumorCandidate>(),
                KnownCount = 2,
                ForgottenCount = 2
            });

            // 5. Outdated (all outdated)
            people.Add(new ListenPreviewPerson
            {
                HeroId = "h_outdated",
                HeroName = "OutdatedHero",
                Profile = new HeroSocialProfile { HeroId = "h_outdated", RelationWithPlayer = 35 },
                Candidates = Array.Empty<RumorCandidate>(),
                KnownCount = 2,
                OutdatedCount = 2
            });

            // 6. PlayerKnowsAll
            {
                var evt = CreateSampleEvent("evt_pk", day: 10.0, tellerHeroId: "h_pk");
                people.Add(new ListenPreviewPerson
                {
                    HeroId = "h_pk",
                    HeroName = "PlayerKnowsHero",
                    Profile = new HeroSocialProfile { HeroId = "h_pk", RelationWithPlayer = 35 },
                    Candidates = new[] { new RumorCandidate { Event = evt, TellerHop = 2, PlayerExistingHop = 2 } },
                    KnownCount = 1
                });
            }

            // 7. Other (secret event only)
            {
                var evt = CreateSampleEvent("evt_sec", day: 10.0, origin: EventOrigin.Secret, tellerHeroId: "h_other");
                people.Add(new ListenPreviewPerson
                {
                    HeroId = "h_other",
                    HeroName = "OtherHero",
                    Profile = new HeroSocialProfile { HeroId = "h_other", RelationWithPlayer = 35 },
                    Candidates = new[] { new RumorCandidate { Event = evt, TellerHop = 1, PlayerExistingHop = null } },
                    KnownCount = 1
                });
            }

            var result = ListenPreviewAggregator.Generate(
                selector,
                compat: null,
                playerClanTier: 2,
                volunteersAlreadyToday: 0,
                day: 10.0,
                cfg.Dialogue,
                people);

            Assert.Equal(1, result.AskRefusalCounts["told"]);
            Assert.Equal(1, result.AskRefusalCounts[nameof(AskRefusalLineKind.Unwilling)]);
            Assert.Equal(1, result.AskRefusalCounts[nameof(AskRefusalLineKind.NothingHeard)]);
            Assert.Equal(1, result.AskRefusalCounts[nameof(AskRefusalLineKind.Forgotten)]);
            Assert.Equal(1, result.AskRefusalCounts[nameof(AskRefusalLineKind.Outdated)]);
            Assert.Equal(1, result.AskRefusalCounts[nameof(AskRefusalLineKind.PlayerKnowsAll)]);
            Assert.Equal(1, result.AskRefusalCounts[nameof(AskRefusalLineKind.Other)]);

            string summary = ListenPreviewLogFormatter.FormatSummary(result, includeTopNames: true);
            Assert.Contains("  - told: 1 (ToldHero)", summary);
            Assert.Contains("  - Unwilling: 1 (UnwillingHero)", summary);
            Assert.Contains("  - NothingHeard: 1 (NothingHero)", summary);
            Assert.Contains("  - Forgotten: 1 (ForgottenHero)", summary);
            Assert.Contains("  - Outdated: 1 (OutdatedHero)", summary);
            Assert.Contains("  - PlayerKnowsAll: 1 (PlayerKnowsHero)", summary);
            Assert.Contains("  - Other: 1 (OtherHero)", summary);
        }
    }
}
