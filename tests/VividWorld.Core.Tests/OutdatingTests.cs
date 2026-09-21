using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using VividWorld.Core.Channels;
using VividWorld.Core.Config;
using VividWorld.Core.Events;
using VividWorld.Core.Memory;
using VividWorld.Core.Persistence;
using VividWorld.Core.Rumors;
using VividWorld.Core.Tests.Fakes;
using VividWorld.Core.Util;
using Xunit;

namespace VividWorld.Core.Tests
{
    public class OutdatingTests
    {
        private (RumorEngine engine, FakePropagationChannel channel, FakeHeroTraitLookup traits, VividWorldConfig cfg) CreateEngine(
            VividWorldConfig? customConfig = null,
            long seed = 42L,
            string playerHeroId = "player")
        {
            var cfg = customConfig ?? new VividWorldConfig();
            cfg.Propagation.BaseTellChancePerContact = 1.0;
            var rng = new SplitMix64Rng();
            var channel = new FakePropagationChannel();
            var traits = new FakeHeroTraitLookup();
            traits.Set(new TraitProfile { HeroId = "lord_teller", IsAlive = true, IsLord = true });
            var retention = FactRetentionPolicies.Create(cfg, rng, seed);
            var embellishment = NullEmbellishmentPolicy.Instance;
            var engine = new RumorEngine(cfg, retention, embellishment, channel, traits, rng, seed, playerHeroId);
            return (engine, channel, traits, cfg);
        }

        private static WorldEvent CreateCaptureEvent(string eventId, double day, params string[] knowerHeroIds)
        {
            var evt = new WorldEvent
            {
                EventId = eventId,
                Type = "hero_taken_prisoner",
                Day = day,
                Origin = EventOrigin.Public,
                DramaWeight = 3,
                Participants = new Dictionary<string, string>
                {
                    ["prisoner"] = "hero_prisoner",
                    ["captor"] = "hero_captor"
                },
                Facts = new List<Fact>
                {
                    new Fact { Id = "f1", Category = FactCategory.Who, TextId = "VividWorld_Fact_HeroCaptured_Who", Text = "who" },
                    new Fact { Id = "f2", Category = FactCategory.Where, TextId = "VividWorld_Fact_HeroCaptured_Where", Text = "where" },
                    new Fact { Id = "f3", Category = FactCategory.What, TextId = "VividWorld_Fact_HeroCaptured_What", Text = "what" },
                    new Fact { Id = "f4", Category = FactCategory.Outcome, TextId = "VividWorld_Fact_HeroCaptured_Outcome", Text = "outcome" }
                },
                KnownBy = new List<KnownByEntry>()
            };

            foreach (var h in knowerHeroIds)
            {
                evt.KnownBy.Add(new KnownByEntry
                {
                    HeroId = h,
                    Hop = 0,
                    LearnedDay = day,
                    Interest = 1.0,
                    ForgetDay = day + 30.0
                });
            }

            return evt;
        }

        #region 1. OutdatedDay 欄位序列化 (4 個測試)

        [Fact]
        public void OutdatedDay_WhenNull_NotSerializedToJson()
        {
            var entry = new KnownByEntry
            {
                HeroId = "lord_a",
                Hop = 1,
                LearnedDay = 5.0,
                OutdatedDay = null
            };

            string json = VividJson.Write(entry);
            Assert.DoesNotContain("outdatedDay", json, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void OutdatedDay_WhenSet_SerializedToJson()
        {
            var entry = new KnownByEntry
            {
                HeroId = "lord_a",
                Hop = 1,
                LearnedDay = 5.0,
                OutdatedDay = 12.5
            };

            string json = VividJson.Write(entry);
            Assert.Contains("\"outdatedDay\": 12.5", json);
        }

        [Fact]
        public void OutdatedDay_RoundTrip_PreservesValue()
        {
            var entryWithVal = new KnownByEntry
            {
                HeroId = "lord_a",
                Hop = 1,
                LearnedDay = 5.0,
                OutdatedDay = 18.25
            };
            string jsonWith = VividJson.Write(entryWithVal);
            var readWith = VividJson.Read<KnownByEntry>(jsonWith);
            Assert.NotNull(readWith);
            Assert.Equal(18.25, readWith!.OutdatedDay);

            var entryNull = new KnownByEntry
            {
                HeroId = "lord_b",
                Hop = 0,
                LearnedDay = 2.0,
                OutdatedDay = null
            };
            string jsonNull = VividJson.Write(entryNull);
            var readNull = VividJson.Read<KnownByEntry>(jsonNull);
            Assert.NotNull(readNull);
            Assert.Null(readNull!.OutdatedDay);
        }

        [Fact]
        public void OutdatedDay_Extra_PreservesUnknownKeys()
        {
            string json = @"{
                ""heroId"": ""lord_a"",
                ""hop"": 1,
                ""learnedDay"": 5.0,
                ""outdatedDay"": 20.0,
                ""futureCustomField"": 999
            }";

            var entry = VividJson.Read<KnownByEntry>(json);
            Assert.NotNull(entry);
            Assert.Equal(20.0, entry!.OutdatedDay);
            Assert.True(entry.Extra.ContainsKey("futureCustomField"));
            Assert.Equal(999, (int)entry.Extra["futureCustomField"]);

            string written = VividJson.Write(entry);
            Assert.Contains("\"futureCustomField\": 999", written);
            Assert.Contains("\"outdatedDay\": 20.0", written);
        }

        #endregion

        #region 2. MarkOutdated (5 個測試)

        [Fact]
        public void MarkOutdated_MarksTargetHeroes_ReturnsCount()
        {
            var evt = CreateCaptureEvent("evt_cap_1", 10.0, "lord_a", "lord_b", "lord_c");
            var marked = new List<string>();

            int count = Outdating.MarkOutdated(evt, new[] { "lord_a", "lord_b" }, 15.0, marked);

            Assert.Equal(2, count);
            Assert.Equal(new[] { "lord_a", "lord_b" }, marked);
            Assert.Equal(15.0, evt.EntryFor("lord_a")!.OutdatedDay);
            Assert.Equal(15.0, evt.EntryFor("lord_b")!.OutdatedDay);
            Assert.Null(evt.EntryFor("lord_c")!.OutdatedDay);
        }

        [Fact]
        public void MarkOutdated_DoesNotOverwriteExistingOutdatedDay()
        {
            var evt = CreateCaptureEvent("evt_cap_1", 10.0, "lord_a", "lord_b");
            evt.EntryFor("lord_a")!.OutdatedDay = 12.0;

            var marked = new List<string>();
            int count = Outdating.MarkOutdated(evt, new[] { "lord_a", "lord_b" }, 20.0, marked);

            Assert.Equal(1, count);
            Assert.Single(marked);
            Assert.Equal("lord_b", marked[0]);
            Assert.Equal(12.0, evt.EntryFor("lord_a")!.OutdatedDay);
            Assert.Equal(20.0, evt.EntryFor("lord_b")!.OutdatedDay);
        }

        [Fact]
        public void MarkOutdated_UnknownHeroes_ReturnsZero_NoError()
        {
            var evt = CreateCaptureEvent("evt_cap_1", 10.0, "lord_a");
            var marked = new List<string>();

            int count = Outdating.MarkOutdated(evt, new[] { "lord_unknown1", "lord_unknown2" }, 20.0, marked);

            Assert.Equal(0, count);
            Assert.Empty(marked);
            Assert.Null(evt.EntryFor("lord_a")!.OutdatedDay);
        }

        [Fact]
        public void MarkOutdated_PartialIntersection_MarksOnlyIntersection()
        {
            var evt = CreateCaptureEvent("evt_cap_1", 10.0, "lord_a", "lord_b");
            var marked = new List<string>();

            int count = Outdating.MarkOutdated(evt, new[] { "lord_b", "lord_nonexistent" }, 25.0, marked);

            Assert.Equal(1, count);
            Assert.Equal(new[] { "lord_b" }, marked);
            Assert.Null(evt.EntryFor("lord_a")!.OutdatedDay);
            Assert.Equal(25.0, evt.EntryFor("lord_b")!.OutdatedDay);
        }

        [Fact]
        public void MarkOutdated_DoesNotMutateOtherFields()
        {
            var evt = CreateCaptureEvent("evt_cap_1", 10.0, "lord_a");
            var entry = evt.EntryFor("lord_a")!;
            entry.Hop = 2;
            entry.LearnedDay = 10.0;
            entry.Interest = 0.85;
            entry.ForgetDay = 45.0;
            entry.HeardCount = 3;
            entry.InterestSource = "first_hand";

            Outdating.MarkOutdated(evt, new[] { "lord_a" }, 22.0);

            Assert.Equal(22.0, entry.OutdatedDay);
            Assert.Equal(2, entry.Hop);
            Assert.Equal(10.0, entry.LearnedDay);
            Assert.Equal(0.85, entry.Interest);
            Assert.Equal(45.0, entry.ForgetDay);
            Assert.Equal(3, entry.HeardCount);
            Assert.Equal("first_hand", entry.InterestSource);
        }

        #endregion

        #region 3. TellerEligibility.Check (4 個測試)

        [Fact]
        public void TellerEligibility_ForgottenTakesPrecedenceOverOutdated()
        {
            var evt = CreateCaptureEvent("evt_cap_1", 10.0, "lord_a");
            var entry = evt.EntryFor("lord_a")!;
            entry.ForgetDay = 15.0;
            entry.OutdatedDay = 16.0;

            var cfg = new VividWorldConfig();
            var traits = new FakeHeroTraitLookup();
            traits.Set(new TraitProfile { HeroId = "lord_a", IsAlive = true, IsLord = true });

            // At day 20.0: both forgotten (20 >= 15) and outdated (OutdatedDay != null).
            // Precedence rule: Forgotten must be returned first.
            var reason = TellerEligibility.Check(evt, "lord_a", 20.0, 5, "player", cfg, traits);

            Assert.Equal(TellReason.Forgotten, reason);
        }

        [Fact]
        public void TellerEligibility_OutdatedKnower_ReturnsOutdated()
        {
            var evt = CreateCaptureEvent("evt_cap_1", 10.0, "lord_a");
            var entry = evt.EntryFor("lord_a")!;
            entry.ForgetDay = 50.0;
            entry.OutdatedDay = 16.0;

            var cfg = new VividWorldConfig();
            var traits = new FakeHeroTraitLookup();
            traits.Set(new TraitProfile { HeroId = "lord_a", IsAlive = true, IsLord = true });

            // At day 20.0: not forgotten (20 < 50), but outdated.
            var reason = TellerEligibility.Check(evt, "lord_a", 20.0, 5, "player", cfg, traits);

            Assert.Equal(TellReason.Outdated, reason);
        }

        [Fact]
        public void TellerEligibility_NormalActiveKnower_ReturnsNone()
        {
            var evt = CreateCaptureEvent("evt_cap_1", 10.0, "lord_a");
            var entry = evt.EntryFor("lord_a")!;
            entry.ForgetDay = 50.0;
            entry.OutdatedDay = null;

            var cfg = new VividWorldConfig();
            var traits = new FakeHeroTraitLookup();
            traits.Set(new TraitProfile { HeroId = "lord_a", IsAlive = true, IsLord = true });

            var reason = TellerEligibility.Check(evt, "lord_a", 20.0, 5, "player", cfg, traits);

            Assert.Equal(TellReason.Ok, reason);
        }

        [Fact]
        public void TellerEligibility_TellReasonText_OutdatedIsOutdated()
        {
            Assert.Equal("outdated", TellerEligibility.TellReasonText(TellReason.Outdated));
        }

        #endregion

        #region 4. ChooseTopic (3 個測試)

        [Fact]
        public void ChooseTopic_ExcludesOutdatedCandidate_WithReasonOutdated()
        {
            var (engine, _, _, _) = CreateEngine();
            var evtActive = CreateCaptureEvent("evt_active", 10.0, "lord_teller");
            var evtOutdated = CreateCaptureEvent("evt_outdated", 10.0, "lord_teller");
            evtOutdated.EntryFor("lord_teller")!.OutdatedDay = 15.0;

            var choice = engine.ChooseTopic("lord_teller", new[] { evtActive, evtOutdated }, 20.0, 12);

            Assert.NotNull(choice);
            Assert.Equal("evt_active", choice.PickedEventId);
            var exclusion = choice.Exclusions.FirstOrDefault(e => e.EventId == "evt_outdated");
            Assert.NotNull(exclusion);
            Assert.Equal(TellReason.Outdated, exclusion!.Reason);
        }

        [Fact]
        public void ChooseTopic_WhenAllCandidatesOutdated_ReturnsNull()
        {
            var (engine, _, _, _) = CreateEngine();
            var evtOutdated1 = CreateCaptureEvent("evt_outdated1", 10.0, "lord_teller");
            evtOutdated1.EntryFor("lord_teller")!.OutdatedDay = 12.0;

            var evtOutdated2 = CreateCaptureEvent("evt_outdated2", 10.0, "lord_teller");
            evtOutdated2.EntryFor("lord_teller")!.OutdatedDay = 14.0;

            var choice = engine.ChooseTopic("lord_teller", new[] { evtOutdated1, evtOutdated2 }, 20.0, 12);

            Assert.NotNull(choice);
            Assert.Null(choice.PickedEventId);
            Assert.Empty(choice.Candidates);
            Assert.Equal(2, choice.Exclusions.Count);
            Assert.All(choice.Exclusions, ex => Assert.Equal(TellReason.Outdated, ex.Reason));
        }

        [Fact]
        public void ChooseTopic_MultipleEvents_FiltersOutdated_PicksActive()
        {
            var (engine, _, _, _) = CreateEngine();
            var evt1 = CreateCaptureEvent("evt_1", 10.0, "lord_teller");
            evt1.EntryFor("lord_teller")!.OutdatedDay = 15.0;

            var evt2 = CreateCaptureEvent("evt_2", 10.0, "lord_teller");

            var evt3 = CreateCaptureEvent("evt_3", 10.0, "lord_teller");
            evt3.EntryFor("lord_teller")!.OutdatedDay = 16.0;

            var choice = engine.ChooseTopic("lord_teller", new[] { evt1, evt2, evt3 }, 20.0, 12);

            Assert.NotNull(choice);
            Assert.Equal("evt_2", choice.PickedEventId);
            Assert.Single(choice.Candidates);
            Assert.Equal("evt_2", choice.Candidates[0].EventId);
            Assert.Equal(2, choice.Exclusions.Count(e => e.Reason == TellReason.Outdated));
        }

        #endregion

        #region 5. DormancyReasonFor (5 個測試)

        [Fact]
        public void DormancyReasonFor_AllForgotten_ReturnsAllForgotten()
        {
            var (engine, _, _, _) = CreateEngine();
            var evt = CreateCaptureEvent("evt_cap", 10.0, "lord_a", "lord_b");
            evt.EntryFor("lord_a")!.ForgetDay = 15.0;
            evt.EntryFor("lord_b")!.ForgetDay = 18.0;

            var reason = engine.DormancyReasonFor(evt, 25.0);

            Assert.NotNull(reason);
            Assert.Equal(DormancyKind.AllForgotten, reason!.Kind);
        }

        [Fact]
        public void DormancyReasonFor_AllOutdated_ReturnsAllOutdated()
        {
            var (engine, _, _, _) = CreateEngine();
            var evt = CreateCaptureEvent("evt_cap", 10.0, "lord_a", "lord_b");
            evt.EntryFor("lord_a")!.OutdatedDay = 15.0;
            evt.EntryFor("lord_b")!.OutdatedDay = 18.0;

            var reason = engine.DormancyReasonFor(evt, 25.0);

            Assert.NotNull(reason);
            Assert.Equal(DormancyKind.AllOutdated, reason!.Kind);
            Assert.Equal(2, reason.OutdatedCount);
            Assert.Equal(0, reason.ForgottenCount);
            Assert.Equal(2, reason.Count);
        }

        [Fact]
        public void DormancyReasonFor_HalfForgottenHalfOutdated_ReturnsAllOutdated()
        {
            var (engine, _, _, _) = CreateEngine();
            var evt = CreateCaptureEvent("evt_cap", 10.0, "lord_a", "lord_b");
            evt.EntryFor("lord_a")!.ForgetDay = 15.0; // forgotten
            evt.EntryFor("lord_b")!.OutdatedDay = 18.0; // outdated

            var reason = engine.DormancyReasonFor(evt, 25.0);

            Assert.NotNull(reason);
            Assert.Equal(DormancyKind.AllOutdated, reason!.Kind);
            Assert.Equal(1, reason.OutdatedCount);
            Assert.Equal(1, reason.ForgottenCount);
            Assert.Equal(2, reason.Count);
        }

        [Fact]
        public void DormancyReasonFor_HasActiveKnower_ReturnsNull()
        {
            var (engine, _, _, _) = CreateEngine();
            var evt = CreateCaptureEvent("evt_cap", 10.0, "lord_a", "lord_b");
            evt.EntryFor("lord_a")!.OutdatedDay = 15.0;
            // lord_b is active: not forgotten, not outdated
            evt.EntryFor("lord_b")!.ForgetDay = 60.0;
            evt.EntryFor("lord_b")!.OutdatedDay = null;

            var reason = engine.DormancyReasonFor(evt, 25.0);

            Assert.Null(reason);
        }

        [Fact]
        public void DormancyReasonFor_PlayerKnowerIgnored_ReturnsAllOutdated()
        {
            var (engine, _, _, _) = CreateEngine(playerHeroId: "player");
            var evt = CreateCaptureEvent("evt_cap", 10.0, "lord_a", "player");
            evt.EntryFor("lord_a")!.OutdatedDay = 15.0;
            // Player is never forgotten and not outdated, but player is ignored for NPC dormancy
            evt.EntryFor("player")!.OutdatedDay = null;

            var reason = engine.DormancyReasonFor(evt, 25.0);

            Assert.NotNull(reason);
            Assert.Equal(DormancyKind.AllOutdated, reason!.Kind);
            Assert.Equal(1, reason.OutdatedCount);
            Assert.Equal(1, reason.Count);
        }

        #endregion

        #region 6. FindLatestCapture (5 個測試)

        [Fact]
        public void FindLatestCapture_MultipleCaptures_ReturnsLatestDay()
        {
            var index = new RumorIndex
            {
                Entries = new List<RumorIndexEntry>
                {
                    new RumorIndexEntry { EventId = "cap_1", Type = "hero_taken_prisoner", Day = 5.0, ParticipantHeroIds = new List<string> { "lord_p" } },
                    new RumorIndexEntry { EventId = "cap_2", Type = "hero_taken_prisoner", Day = 12.0, ParticipantHeroIds = new List<string> { "lord_p" } },
                    new RumorIndexEntry { EventId = "cap_3", Type = "hero_taken_prisoner", Day = 8.0, ParticipantHeroIds = new List<string> { "lord_p" } }
                }
            };

            var found = CaptureLookup.FindLatestCapture(index, "lord_p", 15.0);

            Assert.NotNull(found);
            Assert.Equal("cap_2", found!.EventId);
            Assert.Equal(12.0, found.Day);
        }

        [Fact]
        public void FindLatestCapture_SameDayTieBreak_UsesEventIdDescending()
        {
            var index = new RumorIndex
            {
                Entries = new List<RumorIndexEntry>
                {
                    new RumorIndexEntry { EventId = "cap_01", Type = "hero_taken_prisoner", Day = 10.0, ParticipantHeroIds = new List<string> { "lord_p" } },
                    new RumorIndexEntry { EventId = "cap_03", Type = "hero_taken_prisoner", Day = 10.0, ParticipantHeroIds = new List<string> { "lord_p" } },
                    new RumorIndexEntry { EventId = "cap_02", Type = "hero_taken_prisoner", Day = 10.0, ParticipantHeroIds = new List<string> { "lord_p" } }
                }
            };

            var found = CaptureLookup.FindLatestCapture(index, "lord_p", 15.0);

            Assert.NotNull(found);
            Assert.Equal("cap_03", found!.EventId);
        }

        [Fact]
        public void FindLatestCapture_DormantCapturesIgnored()
        {
            var index = new RumorIndex
            {
                Entries = new List<RumorIndexEntry>
                {
                    new RumorIndexEntry { EventId = "cap_1", Type = "hero_taken_prisoner", Day = 5.0, ParticipantHeroIds = new List<string> { "lord_p" } },
                    new RumorIndexEntry { EventId = "cap_2", Type = "hero_taken_prisoner", Day = 12.0, Dormant = true, ParticipantHeroIds = new List<string> { "lord_p" } }
                }
            };

            var found = CaptureLookup.FindLatestCapture(index, "lord_p", 15.0);

            Assert.NotNull(found);
            Assert.Equal("cap_1", found!.EventId);
        }

        [Fact]
        public void FindLatestCapture_FutureCapturesIgnored()
        {
            var index = new RumorIndex
            {
                Entries = new List<RumorIndexEntry>
                {
                    new RumorIndexEntry { EventId = "cap_past", Type = "hero_taken_prisoner", Day = 8.0, ParticipantHeroIds = new List<string> { "lord_p" } },
                    new RumorIndexEntry { EventId = "cap_future", Type = "hero_taken_prisoner", Day = 20.0, ParticipantHeroIds = new List<string> { "lord_p" } }
                }
            };

            var found = CaptureLookup.FindLatestCapture(index, "lord_p", 15.0);

            Assert.NotNull(found);
            Assert.Equal("cap_past", found!.EventId);
        }

        [Fact]
        public void FindLatestCapture_NoMatching_ReturnsNull()
        {
            var index = new RumorIndex
            {
                Entries = new List<RumorIndexEntry>
                {
                    new RumorIndexEntry { EventId = "cap_other", Type = "hero_taken_prisoner", Day = 8.0, ParticipantHeroIds = new List<string> { "other_lord" } },
                    new RumorIndexEntry { EventId = "diff_type", Type = "heroes_married", Day = 10.0, ParticipantHeroIds = new List<string> { "lord_p" } }
                }
            };

            var found = CaptureLookup.FindLatestCapture(index, "lord_p", 15.0);

            Assert.Null(found);
        }

        #endregion

        #region 7. TemplateForRelease (7 個測試)

        [Fact]
        public void TemplateForRelease_ReleasedAfterEscape_MapsToEscaped()
        {
            Assert.Equal("hero_escaped_captivity", RealEventMapping.TemplateForRelease(EndCaptivityDetail.ReleasedAfterEscape));
            Assert.Equal("hero_escaped_captivity", RealEventMapping.TemplateForRelease((int)EndCaptivityDetail.ReleasedAfterEscape));
        }

        [Fact]
        public void TemplateForRelease_Ransom_MapsToReleased()
        {
            Assert.Equal("hero_released", RealEventMapping.TemplateForRelease(EndCaptivityDetail.Ransom));
            Assert.Equal("hero_released", RealEventMapping.TemplateForRelease((int)EndCaptivityDetail.Ransom));
        }

        [Fact]
        public void TemplateForRelease_ReleasedAfterPeace_MapsToReleased()
        {
            Assert.Equal("hero_released", RealEventMapping.TemplateForRelease(EndCaptivityDetail.ReleasedAfterPeace));
            Assert.Equal("hero_released", RealEventMapping.TemplateForRelease((int)EndCaptivityDetail.ReleasedAfterPeace));
        }

        [Fact]
        public void TemplateForRelease_ReleasedAfterBattle_MapsToReleased()
        {
            Assert.Equal("hero_released", RealEventMapping.TemplateForRelease(EndCaptivityDetail.ReleasedAfterBattle));
            Assert.Equal("hero_released", RealEventMapping.TemplateForRelease((int)EndCaptivityDetail.ReleasedAfterBattle));
        }

        [Fact]
        public void TemplateForRelease_ReleasedByChoice_MapsToReleased()
        {
            Assert.Equal("hero_released", RealEventMapping.TemplateForRelease(EndCaptivityDetail.ReleasedByChoice));
            Assert.Equal("hero_released", RealEventMapping.TemplateForRelease((int)EndCaptivityDetail.ReleasedByChoice));
        }

        [Fact]
        public void TemplateForRelease_ReleasedByCompensation_MapsToReleased()
        {
            Assert.Equal("hero_released", RealEventMapping.TemplateForRelease(EndCaptivityDetail.ReleasedByCompensation));
            Assert.Equal("hero_released", RealEventMapping.TemplateForRelease((int)EndCaptivityDetail.ReleasedByCompensation));
        }

        [Fact]
        public void TemplateForRelease_DeathAndOther_ReturnsNull()
        {
            Assert.Null(RealEventMapping.TemplateForRelease(EndCaptivityDetail.Death));
            Assert.Null(RealEventMapping.TemplateForRelease((int)EndCaptivityDetail.Death));
            Assert.Null(RealEventMapping.TemplateForRelease(999));
            Assert.Null(RealEventMapping.TemplateForRelease(-1));
        }

        #endregion

        #region 8. 記憶統計一致性 (3 個測試)

        [Fact]
        public void OutdatedEntry_IsNotForgotten()
        {
            var evt = CreateCaptureEvent("evt_cap", 10.0, "lord_a");
            var entry = evt.EntryFor("lord_a")!;
            entry.ForgetDay = 50.0;
            entry.OutdatedDay = 20.0;

            var cfg = new MemoryConfig { Enabled = true };
            bool forgotten = Forgetting.IsForgotten(evt, entry, 25.0, "player", cfg);

            Assert.False(forgotten);
        }

        [Fact]
        public void OutdatedEntry_StillCountsAsRemembered()
        {
            var evt = CreateCaptureEvent("evt_cap", 10.0, "lord_a");
            var entry = evt.EntryFor("lord_a")!;
            entry.ForgetDay = 50.0;
            entry.OutdatedDay = 20.0;

            var cfg = new MemoryConfig { Enabled = true };
            // Memory system status classification check:
            // Since 25.0 < 50.0 and Interest is stamped, entry is remembered
            bool forgotten = Forgetting.IsForgotten(evt, entry, 25.0, "player", cfg);
            Assert.False(forgotten);
            Assert.True(entry.Interest.HasValue);
            Assert.True(Outdating.IsOutdated(entry));
        }

        [Fact]
        public void Outdating_DoesNotMutateInterestOrForgetFields()
        {
            var evt = CreateCaptureEvent("evt_cap", 10.0, "lord_a");
            var entry = evt.EntryFor("lord_a")!;
            entry.Interest = 0.92;
            entry.ForgetDay = 45.0;

            Outdating.MarkOutdated(evt, new[] { "lord_a" }, 22.0);

            Assert.Equal(0.92, entry.Interest);
            Assert.Equal(45.0, entry.ForgetDay);
            Assert.Equal(22.0, entry.OutdatedDay);
        }

        #endregion

        #region 9. ConfigMerge (1 個測試)

        [Fact]
        public void ConfigMerge_AddsHeroPrisonerReleasedAndReleaseDramaByProminence()
        {
            string oldJson = @"{
                ""configVersion"": 1,
                ""events"": {
                    ""sources"": {
                        ""heroKilled"": true,
                        ""heroPrisonerTaken"": true
                    }
                }
            }";

            var existingJObj = JObject.Parse(oldJson);
            var canonical = new VividWorldConfig().Normalize();
            var canonicalJObj = JObject.Parse(VividJson.Write(canonical));

            var result = ConfigMerge.AddMissingKeys(existingJObj, canonicalJObj);

            Assert.Contains("events.sources.heroPrisonerReleased", result.AddedPaths);
            Assert.Contains("events.releaseDramaByProminence.ruler", result.AddedPaths);
            Assert.Contains("events.releaseDramaByProminence.clanLeader", result.AddedPaths);
            Assert.Contains("events.releaseDramaByProminence.nobleMember", result.AddedPaths);
            Assert.Contains("events.releaseDramaByProminence.minor", result.AddedPaths);

            Assert.True((bool?)result.Merged.SelectToken("events.sources.heroPrisonerReleased"));
            Assert.Equal(4, (int?)result.Merged.SelectToken("events.releaseDramaByProminence.ruler"));
            Assert.Equal(3, (int?)result.Merged.SelectToken("events.releaseDramaByProminence.clanLeader"));
            Assert.Equal(1, (int?)result.Merged.SelectToken("events.releaseDramaByProminence.nobleMember"));
            Assert.Equal(1, (int?)result.Merged.SelectToken("events.releaseDramaByProminence.minor"));
        }

        #endregion

        #region 10. 日誌格式化 (3 個測試)

        [Fact]
        public void FormatReleaseHeader_ProducesExpectedFormat()
        {
            string log1 = OutdatingLogFormatter.FormatReleaseHeader("Ransom", "hero_released", "lord_a", "lord_b");
            Assert.Equal("RealEventSource: HeroPrisonerReleased detail=Ransom -> template 'hero_released' (prisoner=lord_a, captor=lord_b)", log1);

            string log2 = OutdatingLogFormatter.FormatReleaseHeader("ReleasedAfterEscape", "hero_escaped_captivity", "lord_a", null);
            Assert.Equal("RealEventSource: HeroPrisonerReleased detail=ReleasedAfterEscape -> template 'hero_escaped_captivity' (prisoner=lord_a, captor=none)", log2);
        }

        [Fact]
        public void FormatHeroOutdated_ProducesExpectedFormat()
        {
            string log = OutdatingLogFormatter.FormatHeroOutdated("lord_c", "evt_capture_1", "evt_release_1", 15.5);
            Assert.Equal("Outdated: lord_c no longer spreads evt_capture_1 (heard evt_release_1 on day 15.5)", log);
        }

        [Fact]
        public void FormatOutdatedSummary_ProducesExpectedFormat_AndTruncatesAbove5()
        {
            var list3 = new List<(string EventId, double OutdatedDay)>
            {
                ("evt_1", 10.0),
                ("evt_2", 12.0),
                ("evt_3", 14.0)
            };

            string s3 = MemoryLogFormatter.FormatOutdatedSummary("Lord Val", "lord_val", list3);
            Assert.Equal("  memory: Lord Val (lord_val) has 3 outdated event(s): evt_3 (outdated day 14.0), evt_2 (outdated day 12.0), evt_1 (outdated day 10.0)", s3);

            var list7 = new List<(string EventId, double OutdatedDay)>
            {
                ("evt_1", 10.0),
                ("evt_2", 11.0),
                ("evt_3", 12.0),
                ("evt_4", 13.0),
                ("evt_5", 14.0),
                ("evt_6", 15.0),
                ("evt_7", 16.0)
            };

            string s7 = MemoryLogFormatter.FormatOutdatedSummary("Lord Val", "lord_val", list7);
            Assert.Equal("  memory: Lord Val (lord_val) has 7 outdated event(s): evt_7 (outdated day 16.0), evt_6 (outdated day 15.0), evt_5 (outdated day 14.0), evt_4 (outdated day 13.0), evt_3 (outdated day 12.0) (+2 more)", s7);
        }

        [Fact]
        public void FormatRosterLine_NotOutdated_MatchesLegacyLayout()
        {
            string line = MemoryLogFormatter.FormatRosterLine(1, "馬里溫", "CharacterObject_4050", "travelling", "lord_5_17_1", null);
            Assert.Equal("  hop 1  馬里溫            (CharacterObject_4050  ) at travelling   source: lord_5_17_1", line);
        }

        [Fact]
        public void FormatRosterLine_Outdated_AppendsSuffix()
        {
            string line = MemoryLogFormatter.FormatRosterLine(1, "馬里溫", "CharacterObject_4050", "travelling", "lord_5_17_1", 91098.4);
            Assert.Equal("  hop 1  馬里溫            (CharacterObject_4050  ) at travelling   source: lord_5_17_1 [outdated day 91098.4]", line);
        }

        [Fact]
        public void FormatRosterLine_OutdatedDayRoundsToOneDecimal()
        {
            string line = MemoryLogFormatter.FormatRosterLine(1, "馬里溫", "CharacterObject_4050", "travelling", "lord_5_17_1", 91098.44);
            Assert.Equal("  hop 1  馬里溫            (CharacterObject_4050  ) at travelling   source: lord_5_17_1 [outdated day 91098.4]", line);
        }

        #endregion

        #region 11. 回歸驗證 (1 個測試)

        [Fact]
        public void Regression_ExistingCoreComponents_FunctionWithOutdatingPresent()
        {
            var evt = CreateCaptureEvent("evt_cap", 10.0, "lord_a");
            var entry = evt.EntryFor("lord_a")!;
            entry.OutdatedDay = 15.0;

            Assert.True(Outdating.IsOutdated(entry));
            Assert.False(Outdating.ShouldMark(entry));
            Assert.NotNull(evt.KnownBy);
            Assert.Single(evt.KnownBy);
        }

        #endregion
    }
}
