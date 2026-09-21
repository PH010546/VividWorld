#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using VividWorld.Core.Config;
using VividWorld.Core.Dialogue;
using VividWorld.Core.Events;
using VividWorld.Core.Grudges;
using VividWorld.Core.Persistence;
using VividWorld.Core.Rumors;
using VividWorld.Core.Situations;
using VividWorld.Core.Tests.Fakes;
using VividWorld.Core.Util;
using Xunit;

namespace VividWorld.Core.Tests
{
    /// <summary>「這則事件今天看得見嗎」的共用判斷，以及規格 §2.2.1 清點的七個讀事件的地方。
    ///
    /// 每一組都驗兩件事：
    /// (1) 日期比今天晚的時候，那個地方看不到它；
    /// (2) 日子走到那一天之後，**同一則事件自己回來**——因為誰都沒有刪除它。</summary>
    public class EventVisibilityTests
    {
        #region 1. 共用判斷本身

        [Theory]
        [InlineData(10.0, 10.0, true)]    // 今天發生的
        [InlineData(9.0, 10.0, true)]     // 以前發生的
        [InlineData(10.5, 10.0, true)]    // 容差邊界上：還算看得見
        [InlineData(10.51, 10.0, false)]  // 過了容差：看不見
        [InlineData(200.0, 10.0, false)]  // 被抹掉的時間線
        public void IsVisibleOn_BoundaryIsToleranceDays(double eventDay, double today, bool expected)
        {
            Assert.Equal(expected, EventVisibility.IsVisibleOn(eventDay, today));
        }

        [Fact]
        public void ToleranceDays_StoreTimelineSharesTheSameValue()
        {
            // 兩個常數必須是同一個值，否則 WARN 數出來的則數與實際被藏起來的則數對不上。
            Assert.Equal(EventVisibility.ToleranceDays, StoreTimeline.ToleranceDays);
        }

        #endregion

        #region 2. 索引層的視圖

        private static RumorIndex IndexWithPastAndFuture()
        {
            var index = new RumorIndex();
            index.Upsert(new RumorIndexEntry { EventId = "e_past", Type = "duel", Day = 10.0 });
            index.Upsert(new RumorIndexEntry { EventId = "e_future", Type = "duel", Day = 200.0 });
            return index;
        }

        [Fact]
        public void VisibleEntries_HidesFutureButKeepsItInTheIndex()
        {
            var index = IndexWithPastAndFuture();

            var visible = index.VisibleEntries(50.0).Select(e => e.EventId).ToList();
            Assert.Equal(new[] { "e_past" }, visible);
            Assert.Equal(1, index.HiddenFutureCount(50.0));

            // 沒有刪除：項目還在，Find 也還找得到（事件編號的唯一性檢查靠它）
            Assert.Equal(2, index.Entries.Count);
            Assert.NotNull(index.Find("e_future"));
        }

        [Fact]
        public void VisibleEntries_ComesBackWhenTheDateCatchesUp()
        {
            var index = IndexWithPastAndFuture();

            Assert.DoesNotContain(index.VisibleEntries(50.0), e => e.EventId == "e_future");
            Assert.Contains(index.VisibleEntries(200.0), e => e.EventId == "e_future");
            Assert.Equal(0, index.HiddenFutureCount(200.0));
        }

        #endregion

        #region 3. 被俘查詢（CaptureLookup）

        private static RumorIndex CaptureIndex()
        {
            var index = new RumorIndex();
            index.Upsert(new RumorIndexEntry
            {
                EventId = "cap_old",
                Type = "hero_taken_prisoner",
                Day = 10.0,
                ParticipantHeroIds = new List<string> { "h_prisoner" }
            });
            index.Upsert(new RumorIndexEntry
            {
                EventId = "cap_future",
                Type = "hero_taken_prisoner",
                Day = 200.0,
                ParticipantHeroIds = new List<string> { "h_prisoner" }
            });
            return index;
        }

        [Fact]
        public void CaptureLookup_IgnoresFutureCapture()
        {
            var found = CaptureLookup.FindLatestCapture(CaptureIndex(), "h_prisoner", 50.0);
            Assert.NotNull(found);
            Assert.Equal("cap_old", found!.EventId);
        }

        [Fact]
        public void CaptureLookup_FindsItOnceTheDateCatchesUp()
        {
            var found = CaptureLookup.FindLatestCapture(CaptureIndex(), "h_prisoner", 200.0);
            Assert.NotNull(found);
            Assert.Equal("cap_future", found!.EventId);
        }

        #endregion

        #region 4. 挑今天要講什麼（TellerEligibility／ChooseTopic）

        private static (RumorEngine engine, FakeHeroTraitLookup traits, VividWorldConfig cfg) CreateEngine()
        {
            var cfg = new VividWorldConfig();
            var rng = new SplitMix64Rng();
            var traits = new FakeHeroTraitLookup();
            var engine = new RumorEngine(
                cfg,
                FactRetentionPolicies.Create(cfg, rng, 42L),
                NullEmbellishmentPolicy.Instance,
                new FakePropagationChannel(),
                traits,
                rng,
                42L,
                "player");
            return (engine, traits, cfg);
        }

        private static WorldEvent KnownEvent(string id, double day, string tellerHeroId)
        {
            return new WorldEvent
            {
                EventId = id,
                Type = "test_event",
                Day = day,
                DramaWeight = 4,
                Origin = EventOrigin.Public,
                Facts = new List<Fact> { new Fact { Id = "f1", Text = "Something happened" } },
                KnownBy = new List<KnownByEntry> { new KnownByEntry { HeroId = tellerHeroId, Hop = 0 } }
            };
        }

        [Fact]
        public void TellerEligibility_FutureEventIsNotTellable()
        {
            var (_, traits, cfg) = CreateEngine();
            traits.Set(new TraitProfile { HeroId = "teller", IsAlive = true, IsLord = true });
            var evt = KnownEvent("e_future", 200.0, "teller");

            var reason = TellerEligibility.Check(evt, "teller", 50.0, 4, "player", cfg, traits);
            Assert.Equal(TellReason.FutureEvent, reason);

            // 日子走到那一天 ⇒ 同一則事件講得出來了
            Assert.Equal(TellReason.Ok, TellerEligibility.Check(evt, "teller", 200.0, 4, "player", cfg, traits));
        }

        [Fact]
        public void ChooseTopic_ExcludesFutureEventAndSaysWhy()
        {
            var (engine, traits, _) = CreateEngine();
            traits.Set(new TraitProfile { HeroId = "teller", IsAlive = true, IsLord = true });

            var past = KnownEvent("e_past", 10.0, "teller");
            var future = KnownEvent("e_future", 200.0, "teller");

            var choice = engine.ChooseTopic("teller", new[] { past, future }, 50.0, 12);

            Assert.Single(choice.Candidates);
            Assert.Equal("e_past", choice.Candidates[0].EventId);

            // 被排掉的那一則要講得出理由，不能靜靜消失
            Assert.Contains(choice.Exclusions, e => e.EventId == "e_future" && e.Reason == TellReason.FutureEvent);
            Assert.Contains("future timeline", TellerEligibility.TellReasonText(TellReason.FutureEvent));
        }

        [Fact]
        public void ChooseTopic_FutureEventBecomesACandidateOnlyOnceTheDateCatchesUp()
        {
            // 這一條守的是規格 §2.2.1 指出的那個具體缺陷：
            // 新鮮度 clamp(1-(今天-事件日)/壽命, floor, 1.0) 會把未來事件夾成 1.0＝滿分新鮮，
            // 讓被抹掉那條時間線的事件反而是 NPC 最想講的話題。
            // 擋在資格那一關之後，它連候選都進不去；日子到了才進得去。
            var (engine, traits, _) = CreateEngine();
            traits.Set(new TraitProfile { HeroId = "teller", IsAlive = true, IsLord = true });

            var future = KnownEvent("e_future", 200.0, "teller");

            Assert.Empty(engine.ChooseTopic("teller", new[] { future }, 50.0, 12).Candidates);
            Assert.Single(engine.ChooseTopic("teller", new[] { future }, 200.0, 12).Candidates);
        }

        #endregion

        #region 5. 給玩家的傳聞（RumorOfferSelector）

        private static (RumorOfferSelector selector, HeroSocialProfile teller, List<RumorCandidate> candidates)
            FutureOfferSetup()
        {
            var cfg = new VividWorldConfig();
            var rng = new SplitMix64Rng();
            var engine = new RumorEngine(
                cfg,
                FactRetentionPolicies.Create(cfg, rng, 42L),
                NullEmbellishmentPolicy.Instance,
                new FakePropagationChannel(),
                new FakeHeroTraitLookup(),
                rng,
                42L,
                "player");
            var selector = new RumorOfferSelector(cfg, engine, "player");

            var evt = new WorldEvent
            {
                EventId = "e_future",
                Origin = EventOrigin.Public,
                Day = 200.0,
                DramaWeight = 3,
                Facts = new List<Fact>
                {
                    new Fact { Id = "f_who", Category = FactCategory.Who, Text = "Lord Bob", Fragility = 1 }
                }
            };
            evt.KnownBy.Add(new KnownByEntry { HeroId = "teller_1", Hop = 0 });

            var teller = new HeroSocialProfile { HeroId = "teller_1", RelationWithPlayer = 50 };
            var candidates = new List<RumorCandidate>
            {
                new RumorCandidate { Event = evt, TellerHop = 0 }
            };
            return (selector, teller, candidates);
        }

        [Fact]
        public void DecideOnAsk_FutureEventGoesIntoItsOwnBucket()
        {
            var (selector, teller, candidates) = FutureOfferSetup();

            var decision = selector.DecideOnAsk(teller, candidates, 50.0);

            Assert.Null(decision.Offer);
            Assert.Equal(AskRefusal.AllCandidatesFiltered, decision.Refusal);
            Assert.Equal(1, decision.FilteredFutureTimeline);
            Assert.Equal(0, decision.FilteredNotVisible);
            Assert.Equal(0, decision.FilteredPlayerKnows);
            Assert.Equal(0, decision.FilteredOther);

            // 診斷行要講得出是哪一種排除（規格 §9.5.6：回報資料用英文）
            string log = AskDecision.FormatLog("Bob", "teller_1", decision, 1, 0);
            Assert.Contains("1 from a future timeline", log);
        }

        [Fact]
        public void DecideOnAsk_OffersItOnceTheDateCatchesUp()
        {
            var (selector, teller, candidates) = FutureOfferSetup();

            var decision = selector.DecideOnAsk(teller, candidates, 200.0);

            Assert.Equal(AskRefusal.None, decision.Refusal);
            Assert.NotNull(decision.Offer);
            Assert.Equal(0, decision.FilteredFutureTimeline);
        }

        [Fact]
        public void DecideOnVolunteer_FutureEventGoesIntoItsOwnBucket()
        {
            var (selector, teller, candidates) = FutureOfferSetup();

            var decision = selector.DecideOnVolunteer(teller, candidates, 50.0, 0);

            Assert.Null(decision.Offer);
            Assert.Equal(VolunteerRefusal.AllCandidatesFiltered, decision.Refusal);
            Assert.Equal(1, decision.FilteredFutureTimeline);

            string log = VolunteerDecision.FormatLog("Bob", "teller_1", decision, 1);
            Assert.Contains("1 from a future timeline", log);
        }

        #endregion

        #region 6. 載入時重建恩怨帳本（GrudgeIndex.RebuildFrom）

        private static (RumorIndex index, Func<string, WorldEvent?> load) GrudgeSetup()
        {
            var index = new RumorIndex();
            index.Upsert(new RumorIndexEntry { EventId = "g_past", Day = 10.0, HasGrudges = true });
            index.Upsert(new RumorIndexEntry { EventId = "g_future", Day = 200.0, HasGrudges = true });

            Func<string, WorldEvent?> load = id => new WorldEvent
            {
                EventId = id,
                Day = id == "g_future" ? 200.0 : 10.0,
                KnownBy = new List<KnownByEntry>
                {
                    new KnownByEntry
                    {
                        HeroId = id == "g_future" ? "h_future" : "h_past",
                        RelationImpacts = new List<RelationImpact>
                        {
                            new RelationImpact { Scope = GrudgeScope.Personal, AboutHeroId = "h_target", Requested = -5.0 }
                        }
                    }
                }
            };
            return (index, load);
        }

        [Fact]
        public void GrudgeRebuild_DoesNotRebuildGrudgesFromAnErasedTimeline()
        {
            var (index, load) = GrudgeSetup();

            var rebuilt = GrudgeIndex.RebuildFrom(index, load, 50.0, out int read, out int skipped, out int hidden);

            Assert.Equal(1, read);
            Assert.Equal(0, skipped);
            Assert.Equal(1, hidden);
            Assert.Single(rebuilt.Pairs);
            Assert.Empty(rebuilt.Between("h_future", "h_target", GrudgeScope.Personal));
            Assert.Single(rebuilt.Between("h_past", "h_target", GrudgeScope.Personal));
        }

        [Fact]
        public void GrudgeRebuild_RebuildsItOnceTheDateCatchesUp()
        {
            var (index, load) = GrudgeSetup();

            var rebuilt = GrudgeIndex.RebuildFrom(index, load, 200.0, out int read, out _, out int hidden);

            Assert.Equal(2, read);
            Assert.Equal(0, hidden);
            Assert.Single(rebuilt.Between("h_future", "h_target", GrudgeScope.Personal));
        }

        #endregion

        #region 7. 恩怨淡化（GrudgeDecay）

        private static List<GrudgeEntry> DecayEntries()
        {
            return new List<GrudgeEntry>
            {
                new GrudgeEntry { EventId = "g_past", Day = 10.0, Requested = -5.0, Scope = GrudgeScope.Personal },
                new GrudgeEntry { EventId = "g_future", Day = 200.0, Requested = -40.0, Scope = GrudgeScope.Personal }
            };
        }

        [Fact]
        public void GrudgeDecay_DoesNotReplayEntriesFromAnErasedTimeline()
        {
            var result = GrudgeDecay.Replay(DecayEntries(), GrudgeScope.Personal, new SituationsConfig(), null, 50.0);

            Assert.Equal(1, result.EntryCount);
            Assert.Equal(1, result.HiddenFutureCount);
            Assert.Equal(-5.0, result.Value, 3);   // 在 ±10 區間內 ⇒ 不淡化
        }

        [Fact]
        public void GrudgeDecay_CountsItOnceTheDateCatchesUp()
        {
            var result = GrudgeDecay.Replay(DecayEntries(), GrudgeScope.Personal, new SituationsConfig(), null, 200.0);

            Assert.Equal(2, result.EntryCount);
            Assert.Equal(0, result.HiddenFutureCount);
        }

        #endregion

        #region 8. 誰知道什麼（KnownByIndex）

        private static KnownByIndex KnownBySetup()
        {
            var index = new RumorIndex();
            index.Upsert(new RumorIndexEntry
            {
                EventId = "e_past",
                Day = 10.0,
                KnownByHeroIds = new List<string> { "h1" },
                ParticipantHeroIds = new List<string> { "h2" }
            });
            index.Upsert(new RumorIndexEntry
            {
                EventId = "e_future",
                Day = 200.0,
                KnownByHeroIds = new List<string> { "h1" },
                ParticipantHeroIds = new List<string> { "h2" }
            });

            var knownBy = new KnownByIndex();
            knownBy.Rebuild(index);
            return knownBy;
        }

        [Fact]
        public void KnownByIndex_HidesFutureEventsFromBothDirections()
        {
            var knownBy = KnownBySetup();

            Assert.Equal(new[] { "e_past" }, knownBy.EventsKnownBy("h1", 50.0));
            Assert.Equal(new[] { "e_past" }, knownBy.EventsAbout("h2", 50.0));
            Assert.Equal(1, knownBy.HiddenFutureCountKnownBy("h1", 50.0));
        }

        [Fact]
        public void KnownByIndex_ReturnsThemOnceTheDateCatchesUp()
        {
            var knownBy = KnownBySetup();

            Assert.Equal(2, knownBy.EventsKnownBy("h1", 200.0).Count);
            Assert.Equal(2, knownBy.EventsAbout("h2", 200.0).Count);
            Assert.Equal(0, knownBy.HiddenFutureCountKnownBy("h1", 200.0));
        }

        [Fact]
        public void KnownByIndex_NoteKnowerCarriesTheEventDay()
        {
            var knownBy = new KnownByIndex();
            knownBy.NoteKnower("h1", "e_future", 200.0);

            Assert.Empty(knownBy.EventsKnownBy("h1", 50.0));
            Assert.Single(knownBy.EventsKnownBy("h1", 200.0));
        }

        [Fact]
        public void LeftRingLog_SaysHowManyWereHidden()
        {
            // 只知道未來事件的人，EventsKnownBy 會回空清單 ⇒ 這一行會印成「0 known」。
            // 沒有這個數字，那句話讀起來就是「他什麼都不知道」，而那是假話。
            string line = TellerLogFormatter.FormatLeftRingNoTellableTopic(
                "h_teller", totalKnown: 0, exclusions: new List<TopicExclusion>(), inactiveCount: 0, hiddenFuture: 3);

            Assert.Contains("3 hidden from a future timeline", line);

            string clean = TellerLogFormatter.FormatLeftRingNoTellableTopic(
                "h_teller", totalKnown: 2, exclusions: new List<TopicExclusion>(), inactiveCount: 0, hiddenFuture: 0);
            Assert.DoesNotContain("hidden", clean);
        }

        #endregion

        #region 9. 情境冷卻（SituationHistoryIndex）

        private static List<RumorIndexEntry> SituationEntries()
        {
            return new List<RumorIndexEntry>
            {
                new RumorIndexEntry
                {
                    EventId = "s_future",
                    Day = 200.0,
                    SituationId = "seat_dispute",
                    ParticipantHeroIds = new List<string> { "h1", "h2" }
                }
            };
        }

        [Fact]
        public void SituationCooldown_FutureOccurrenceDoesNotBlockToday()
        {
            var history = new SituationHistoryIndex(SituationEntries(), 50.0);

            Assert.Null(history.LastOccurrence("seat_dispute", new[] { "h1", "h2" }));
        }

        [Fact]
        public void SituationCooldown_CountsItOnceTheDateCatchesUp()
        {
            var history = new SituationHistoryIndex(SituationEntries(), 200.0);

            var occ = history.LastOccurrence("seat_dispute", new[] { "h1", "h2" });
            Assert.NotNull(occ);
            Assert.Equal("s_future", occ!.EventId);
        }

        #endregion
    }
}
