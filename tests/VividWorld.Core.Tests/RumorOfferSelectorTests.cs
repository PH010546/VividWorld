using System;
using System.Collections.Generic;
using System.Linq;
using VividWorld.Core.Config;
using VividWorld.Core.Dialogue;
using VividWorld.Core.Events;
using VividWorld.Core.Rumors;
using VividWorld.Core.Tests.Fakes;
using VividWorld.Core.Util;
using Xunit;

namespace VividWorld.Core.Tests
{
    public class RumorOfferSelectorTests
    {
        private (RumorOfferSelector selector, RumorEngine engine, VividWorldConfig cfg)
            CreateSelector(string playerHeroId = "player", long seed = 42L, VividWorldConfig? customConfig = null)
        {
            var cfg = customConfig ?? new VividWorldConfig();
            var rng = new SplitMix64Rng();
            var channel = new FakePropagationChannel();
            var traits = new FakeHeroTraitLookup();
            var retention = FactRetentionPolicies.Create(cfg, rng, seed);
            var embellishment = NullEmbellishmentPolicy.Instance;
            var engine = new RumorEngine(cfg, retention, embellishment, channel, traits, rng, seed, playerHeroId);
            var selector = new RumorOfferSelector(cfg, engine, playerHeroId);
            return (selector, engine, cfg);
        }

        private WorldEvent CreateSampleEvent(string eventId, double day = 10.0, int drama = 3)
        {
            var evt = new WorldEvent
            {
                EventId = eventId,
                Origin = EventOrigin.Public,
                Day = day,
                DramaWeight = drama,
                Facts = new List<Fact>
                {
                    new() { Id = "f_who", Category = FactCategory.Who, Text = "Lord Bob", Fragility = 1 },
                    new() { Id = "f_where", Category = FactCategory.Where, Text = "Pravend", Fragility = 2 },
                    new() { Id = "f_what", Category = FactCategory.What, Text = "fought a duel", Fragility = 3 }
                }
            };
            return evt;
        }

        [Fact]
        public void Offer_SkipsEventsThePlayerAlreadyKnows()
        {
            var (selector, _, _) = CreateSelector();
            var evt = CreateSampleEvent("evt_known");
            evt.KnownBy.Add(new KnownByEntry { HeroId = "teller_1", Hop = 3 });
            evt.KnownBy.Add(new KnownByEntry { HeroId = "player", Hop = 2 });

            var teller = new HeroSocialProfile
            {
                HeroId = "teller_1",
                RelationWithPlayer = 20
            };

            var candidates = new List<RumorCandidate>
            {
                new()
                {
                    Event = evt,
                    TellerHop = 3,
                    PlayerExistingHop = 2,
                    InvolvesHeroPlayerCaresAbout = false
                }
            };

            // tellerHop + 1 = 4 >= playerExistingHop(2) -> 不得重述，跳過
            var offer = selector.SelectOnAsk(teller, candidates, day: 15.0);
            Assert.Null(offer);
        }

        [Fact]
        public void Offer_Retell_RequiresStrictlyLowerHop()
        {
            var (selector, _, _) = CreateSelector();
            var evt = CreateSampleEvent("evt_retell_hop");
            evt.KnownBy.Add(new KnownByEntry { HeroId = "teller_equal", Hop = 2 });
            evt.KnownBy.Add(new KnownByEntry { HeroId = "teller_better", Hop = 0 });
            evt.KnownBy.Add(new KnownByEntry { HeroId = "player", Hop = 2, KnownFactIds = new List<string> { "f_who" } });

            // 1. teller_equal: TellerHop + 1 = 3 >= 2 -> 拒絕
            var profileEqual = new HeroSocialProfile { HeroId = "teller_equal", RelationWithPlayer = 20 };
            var candidateEqual = new List<RumorCandidate>
            {
                new() { Event = evt, TellerHop = 2, PlayerExistingHop = 2 }
            };
            Assert.Null(selector.SelectOnAsk(profileEqual, candidateEqual, day: 15.0));

            // 2. teller_better: TellerHop + 1 = 1 < 2 -> 允許重述
            var profileBetter = new HeroSocialProfile { HeroId = "teller_better", RelationWithPlayer = 20 };
            var candidateBetter = new List<RumorCandidate>
            {
                new() { Event = evt, TellerHop = 0, PlayerExistingHop = 2 }
            };
            var offer = selector.SelectOnAsk(profileBetter, candidateBetter, day: 15.0);
            Assert.NotNull(offer);
            Assert.True(offer!.IsRetell);
            Assert.Equal(1, offer.ResultingPlayerHop);
        }

        [Fact]
        public void Offer_Retell_RequiresNewFactIds()
        {
            var (selector, _, _) = CreateSelector();
            var evt = CreateSampleEvent("evt_retell_new_facts");
            // 讓事件只有 1 個 fragility 1 的 fact
            evt.Facts = new List<Fact>
            {
                new() { Id = "f_core", Category = FactCategory.Who, Text = "Only Fact", Fragility = 1 }
            };

            evt.KnownBy.Add(new KnownByEntry { HeroId = "teller_better", Hop = 0 });
            // 玩家已經持有 "f_core"
            evt.KnownBy.Add(new KnownByEntry
            {
                HeroId = "player",
                Hop = 2,
                KnownFactIds = new List<string> { "f_core" }
            });

            var profile = new HeroSocialProfile { HeroId = "teller_better", RelationWithPlayer = 20 };
            var candidates = new List<RumorCandidate>
            {
                // TellerHop + 1 = 1 < 2，但 hop 1 依然只有 "f_core"，沒有任何新碎片
                new() { Event = evt, TellerHop = 0, PlayerExistingHop = 2 }
            };

            var offer = selector.SelectOnAsk(profile, candidates, day: 15.0);
            Assert.Null(offer);
        }

        [Fact]
        public void Offer_Retell_MaterializesKnownFactIdsAsUnion()
        {
            var (selector, _, _) = CreateSelector();
            var evt = CreateSampleEvent("evt_union");
            evt.Facts = new List<Fact>
            {
                new() { Id = "f_1", Category = FactCategory.Who, Text = "F1", Fragility = 1 },
                new() { Id = "f_2", Category = FactCategory.What, Text = "F2", Fragility = 1 },
                new() { Id = "f_3", Category = FactCategory.Where, Text = "F3", Fragility = 1 }
            };

            evt.KnownBy.Add(new KnownByEntry { HeroId = "teller_0", Hop = 0 });
            // 玩家先前只知道 f_1
            var playerEntry = new KnownByEntry
            {
                HeroId = "player",
                Hop = 3,
                LearnedDay = 10.0,
                SourceHeroId = "old_source",
                KnownFactIds = new List<string> { "f_1" }
            };
            evt.KnownBy.Add(playerEntry);

            var teller = new HeroSocialProfile { HeroId = "teller_0", RelationWithPlayer = 20 };
            var candidates = new List<RumorCandidate>
            {
                new() { Event = evt, TellerHop = 0, PlayerExistingHop = 3 }
            };

            var offer = selector.SelectOnAsk(teller, candidates, day: 15.0);
            Assert.NotNull(offer);
            Assert.True(offer!.IsRetell);

            // 應用重述
            selector.ApplyOffer(offer, evt, "teller_0", day: 15.0);

            Assert.NotNull(playerEntry.KnownFactIds);
            // 聯集必須包含舊有的 f_1 以及 hop 1 新帶來的 f_2, f_3
            Assert.Contains("f_1", playerEntry.KnownFactIds!);
            Assert.Contains("f_2", playerEntry.KnownFactIds!);
            Assert.Contains("f_3", playerEntry.KnownFactIds!);
        }

        [Fact]
        public void Offer_Retell_UpdatesSourceButNotLearnedDay()
        {
            var (selector, _, _) = CreateSelector();
            var evt = CreateSampleEvent("evt_retell_update");
            evt.KnownBy.Add(new KnownByEntry { HeroId = "teller_new", Hop = 0 });
            var playerEntry = new KnownByEntry
            {
                HeroId = "player",
                Hop = 3,
                LearnedDay = 5.0,
                SourceHeroId = "teller_old",
                KnownFactIds = new List<string> { "f_who" }
            };
            evt.KnownBy.Add(playerEntry);

            var teller = new HeroSocialProfile { HeroId = "teller_new", RelationWithPlayer = 20 };
            var candidates = new List<RumorCandidate>
            {
                new() { Event = evt, TellerHop = 0, PlayerExistingHop = 3 }
            };

            var offer = selector.SelectOnAsk(teller, candidates, day: 25.0);
            Assert.NotNull(offer);

            selector.ApplyOffer(offer!, evt, "teller_new", day: 25.0);

            Assert.Equal(1, playerEntry.Hop);
            Assert.Equal(5.0, playerEntry.LearnedDay); // 不動
            Assert.Equal("teller_new", playerEntry.SourceHeroId); // 更新為重述者
        }

        [Fact]
        public void Offer_Retell_IsScoredLower()
        {
            var (selector, _, cfg) = CreateSelector();
            var evt = CreateSampleEvent("evt_score", day: 10.0, drama: 4);

            var freshCandidate = new RumorCandidate
            {
                Event = evt,
                TellerHop = 1,
                PlayerExistingHop = null,
                InvolvesHeroPlayerCaresAbout = false
            };

            var retellCandidate = new RumorCandidate
            {
                Event = evt,
                TellerHop = 1,
                PlayerExistingHop = 3,
                InvolvesHeroPlayerCaresAbout = false
            };

            double freshScore = selector.Score(freshCandidate, day: 20.0);
            double retellScore = selector.Score(retellCandidate, day: 20.0);

            Assert.True(freshScore > 0);
            Assert.Equal(freshScore * cfg.Dialogue.ScoreRetellMultiplier, retellScore, precision: 6);
        }

        [Fact]
        public void Offer_TieBreak_IsDeterministic()
        {
            var (selector, _, _) = CreateSelector();

            // 1. 同分時 Day 較新者優先
            var evtOlder = CreateSampleEvent("evt_older", day: 10.0, drama: 3);
            evtOlder.KnownBy.Add(new KnownByEntry { HeroId = "teller", Hop = 1 });

            var evtNewer = CreateSampleEvent("evt_newer", day: 20.0, drama: 3);
            evtNewer.KnownBy.Add(new KnownByEntry { HeroId = "teller", Hop = 1 });

            var candOlder = new RumorCandidate { Event = evtOlder, TellerHop = 1, PlayerExistingHop = null };
            var candNewer = new RumorCandidate { Event = evtNewer, TellerHop = 1, PlayerExistingHop = null };

            var teller = new HeroSocialProfile { HeroId = "teller", RelationWithPlayer = 20 };

            var offer1 = selector.SelectOnAsk(teller, new[] { candOlder, candNewer }, day: 25.0);
            Assert.NotNull(offer1);
            Assert.Equal("evt_newer", offer1!.EventId);

            // 2. 同分且同 Day 時，字典序優先
            var evtA = CreateSampleEvent("evt_aaa", day: 20.0, drama: 3);
            evtA.KnownBy.Add(new KnownByEntry { HeroId = "teller", Hop = 1 });
            var evtB = CreateSampleEvent("evt_bbb", day: 20.0, drama: 3);
            evtB.KnownBy.Add(new KnownByEntry { HeroId = "teller", Hop = 1 });

            var candA = new RumorCandidate { Event = evtA, TellerHop = 1, PlayerExistingHop = null };
            var candB = new RumorCandidate { Event = evtB, TellerHop = 1, PlayerExistingHop = null };

            var offerA1 = selector.SelectOnAsk(teller, new[] { candB, candA }, day: 25.0);
            var offerA2 = selector.SelectOnAsk(teller, new[] { candA, candB }, day: 25.0);

            Assert.Equal("evt_aaa", offerA1!.EventId);
            Assert.Equal("evt_aaa", offerA2!.EventId);
        }

        [Fact]
        public void Volunteer_RequiresAllThreeGates()
        {
            var (selector, _, cfg) = CreateSelector();
            // 預設 cfg.Dialogue: NpcVolunteerRelationGate = 30, VolunteerCooldownDays = 3.0, MaxVolunteersPerDay = 1
            cfg.Dialogue.CasualChatRelationGate = 30;

            // 閘門 1: 好感度
            var lowRelation = new HeroSocialProfile
            {
                HeroId = "h_low",
                RelationWithPlayer = 29,
                LastVolunteeredDay = -1
            };
            Assert.False(selector.WillVolunteer(lowRelation, day: 10.0, volunteersAlreadyToday: 0));

            // 近親豁免好感度
            var spouseLowRelation = new HeroSocialProfile
            {
                HeroId = "h_spouse",
                RelationWithPlayer = 0,
                IsPlayerSpouse = true,
                LastVolunteeredDay = -1
            };
            Assert.True(selector.WillVolunteer(spouseLowRelation, day: 10.0, volunteersAlreadyToday: 0));

            // 閘門 2: 冷卻
            var onCooldown = new HeroSocialProfile
            {
                HeroId = "h_cooldown",
                RelationWithPlayer = 50,
                LastVolunteeredDay = 8.0 // day 10 - 8 = 2 < 3.0
            };
            Assert.False(selector.WillVolunteer(onCooldown, day: 10.0, volunteersAlreadyToday: 0));

            var cooldownPassed = new HeroSocialProfile
            {
                HeroId = "h_ready",
                RelationWithPlayer = 50,
                LastVolunteeredDay = 7.0 // day 10 - 7 = 3.0 >= 3.0
            };
            Assert.True(selector.WillVolunteer(cooldownPassed, day: 10.0, volunteersAlreadyToday: 0));

            // 閘門 3: 每日上限
            var normalReady = new HeroSocialProfile
            {
                HeroId = "h_normal",
                RelationWithPlayer = 50,
                LastVolunteeredDay = -1
            };
            Assert.True(selector.WillVolunteer(normalReady, day: 10.0, volunteersAlreadyToday: 0));
            Assert.False(selector.WillVolunteer(normalReady, day: 10.0, volunteersAlreadyToday: 1)); // 上限已滿
        }

        [Fact]
        public void Ask_IsDeterministic_NeverRolls()
        {
            var (selector, _, _) = CreateSelector();
            var evt1 = CreateSampleEvent("evt_1", day: 10.0, drama: 3);
            evt1.KnownBy.Add(new KnownByEntry { HeroId = "teller", Hop = 1 });
            var evt2 = CreateSampleEvent("evt_2", day: 12.0, drama: 4);
            evt2.KnownBy.Add(new KnownByEntry { HeroId = "teller", Hop = 1 });

            var candidates = new List<RumorCandidate>
            {
                new() { Event = evt1, TellerHop = 1, PlayerExistingHop = null },
                new() { Event = evt2, TellerHop = 1, PlayerExistingHop = null }
            };

            var teller = new HeroSocialProfile
            {
                HeroId = "teller",
                RelationWithPlayer = 10,
                Traits = new TraitProfile { Generosity = 1, Honor = 1, Calculating = 0 }
            };

            var firstOffer = selector.SelectOnAsk(teller, candidates, day: 15.0);
            Assert.NotNull(firstOffer);

            for (int i = 0; i < 100; i++)
            {
                var offer = selector.SelectOnAsk(teller, candidates, day: 15.0);
                Assert.NotNull(offer);
                Assert.Equal(firstOffer!.EventId, offer!.EventId);
                Assert.Equal(firstOffer.Score, offer.Score);
                Assert.Equal(firstOffer.ResultingPlayerHop, offer.ResultingPlayerHop);
            }
        }

        [Fact]
        public void Decide_BelowRelationGate_ReportsRelationGate()
        {
            var cfg = new VividWorldConfig();
            cfg.Dialogue.AskRelationGate = 10;
            var (selector, _, _) = CreateSelector(customConfig: cfg);

            var teller = new HeroSocialProfile
            {
                HeroId = "teller",
                RelationWithPlayer = 5,
                Traits = new TraitProfile { Generosity = 0, Honor = 0, Calculating = 0 }
            };

            var evt = CreateSampleEvent("evt_1");
            evt.KnownBy.Add(new KnownByEntry { HeroId = "teller", Hop = 1 });
            var candidates = new List<RumorCandidate>
            {
                new() { Event = evt, TellerHop = 1, PlayerExistingHop = null }
            };

            var decision = selector.DecideOnAsk(teller, candidates, day: 15.0);

            Assert.Equal(AskRefusal.RelationGate, decision.Refusal);
            Assert.Null(decision.Offer);
            Assert.Equal(5, decision.Relation);
        }

        [Fact]
        public void Decide_BelowWillingness_ReportsWillingnessGate()
        {
            var (selector, _, _) = CreateSelector(); // default AskRelationGate = 0, AskWillingnessThreshold = 5.0

            var teller = new HeroSocialProfile
            {
                HeroId = "teller",
                RelationWithPlayer = 0,
                Traits = new TraitProfile { Generosity = 0, Honor = 0, Calculating = 0 }
            };

            var evt = CreateSampleEvent("evt_1");
            evt.KnownBy.Add(new KnownByEntry { HeroId = "teller", Hop = 1 });
            var candidates = new List<RumorCandidate>
            {
                new() { Event = evt, TellerHop = 1, PlayerExistingHop = null }
            };

            var decision = selector.DecideOnAsk(teller, candidates, day: 15.0);

            Assert.Equal(AskRefusal.WillingnessGate, decision.Refusal);
            Assert.Null(decision.Offer);
            Assert.Equal(0.0, decision.Willingness);
            Assert.Equal(5.0, decision.Threshold);
        }

        [Fact]
        public void Decide_NoCandidates_ReportsNoKnownEvents()
        {
            var (selector, _, _) = CreateSelector();

            var teller = new HeroSocialProfile
            {
                HeroId = "teller",
                RelationWithPlayer = 20,
                Traits = new TraitProfile { Generosity = 0, Honor = 0, Calculating = 0 }
            };

            var candidates = new List<RumorCandidate>();

            var decision = selector.DecideOnAsk(teller, candidates, day: 15.0);

            Assert.Equal(AskRefusal.NoKnownEvents, decision.Refusal);
            Assert.Null(decision.Offer);
            Assert.Equal(0, decision.CandidateCount);
        }

        [Fact]
        public void Decide_AllFiltered_CountsWhy()
        {
            var (selector, _, _) = CreateSelector();

            var teller = new HeroSocialProfile
            {
                HeroId = "teller",
                RelationWithPlayer = 20,
                Traits = new TraitProfile { Generosity = 0, Honor = 0, Calculating = 0 }
            };

            // 1. 玩家已知同 hop
            var evt1 = CreateSampleEvent("evt_1");
            evt1.KnownBy.Add(new KnownByEntry { HeroId = "teller", Hop = 1 });
            evt1.KnownBy.Add(new KnownByEntry { HeroId = "player", Hop = 1 });
            var c1 = new RumorCandidate { Event = evt1, TellerHop = 1, PlayerExistingHop = 1 };

            // 2. 未洩漏的秘密
            var evt2 = CreateSampleEvent("evt_2");
            evt2.Origin = EventOrigin.Secret;
            evt2.State.Leaked = false;
            evt2.KnownBy.Add(new KnownByEntry { HeroId = "teller", Hop = 0 });
            var c2 = new RumorCandidate { Event = evt2, TellerHop = 0, PlayerExistingHop = null };

            // 3. 玩家已知且重述不夠豐富 (hop 較低但無新碎片)
            var evt3 = CreateSampleEvent("evt_3");
            evt3.KnownBy.Add(new KnownByEntry { HeroId = "teller", Hop = 0 });
            evt3.KnownBy.Add(new KnownByEntry
            {
                HeroId = "player",
                Hop = 2,
                KnownFactIds = evt3.Facts.Select(f => f.Id).ToList()
            });
            var c3 = new RumorCandidate { Event = evt3, TellerHop = 0, PlayerExistingHop = 2 };

            var candidates = new List<RumorCandidate> { c1, c2, c3 };

            var decision = selector.DecideOnAsk(teller, candidates, day: 15.0);

            Assert.Equal(AskRefusal.AllCandidatesFiltered, decision.Refusal);
            Assert.Null(decision.Offer);
            Assert.Equal(1, decision.FilteredNotVisible);
            Assert.Equal(2, decision.FilteredPlayerKnows);
            Assert.Equal(0, decision.FilteredOther);
            Assert.Equal(3, decision.CandidateCount);

            // 四個桶必須把候選數分光。診斷行印出來的數字加不起來的話，
            // 讀的人會去追一個不存在的第二個原因——那比沒有日誌更糟。
            Assert.Equal(
                decision.CandidateCount,
                decision.FilteredNotVisible + decision.FilteredPlayerKnows + decision.FilteredOther);

            AssertFormatLogExamples();
        }

        /// <summary>M6a-fix3 實機驗收第 6／7 項的情境：好感度 100、意願遠超門檻的**目擊者**（hop 0）
        /// 還是不肯開口，因為玩家手上的 hop 3 版本已經握有同樣的碎片。
        /// 「1 player already knows」這個數字答不出這件事，所以未通過的判定其實追錯了方向——
        /// 日誌必須自己說出「更近，但沒有新東西可加」，而且要跟「同一手的舊消息」分得開。</summary>
        [Fact]
        public void Decide_AllFiltered_NotesSayWhyEachRetellWasRefused()
        {
            var (selector, _, _) = CreateSelector();

            var teller = new HeroSocialProfile
            {
                HeroId = "teller",
                RelationWithPlayer = 100,
                Traits = new TraitProfile { Generosity = 0, Honor = 0, Calculating = 0 }
            };

            // 不夠近：講述者 hop 1，重述後玩家還是 hop 2，並沒有比手上的 hop 1 更近
            var notCloser = CreateSampleEvent("evt_notcloser");
            notCloser.KnownBy.Add(new KnownByEntry { HeroId = "teller", Hop = 1 });
            notCloser.KnownBy.Add(new KnownByEntry { HeroId = "player", Hop = 1 });

            // 夠近，但沒有新碎片：目擊者 hop 0，玩家 hop 2 卻已經持有全部碎片
            var noNewFacts = CreateSampleEvent("evt_nonew");
            noNewFacts.KnownBy.Add(new KnownByEntry { HeroId = "teller", Hop = 0 });
            noNewFacts.KnownBy.Add(new KnownByEntry
            {
                HeroId = "player",
                Hop = 2,
                KnownFactIds = noNewFacts.Facts.Select(f => f.Id).ToList()
            });

            var candidates = new List<RumorCandidate>
            {
                new RumorCandidate { Event = notCloser, TellerHop = 1, PlayerExistingHop = 1 },
                new RumorCandidate { Event = noNewFacts, TellerHop = 0, PlayerExistingHop = 2 }
            };

            var decision = selector.DecideOnAsk(teller, candidates, day: 15.0);

            Assert.Equal(AskRefusal.AllCandidatesFiltered, decision.Refusal);
            Assert.Equal(2, decision.FilteredPlayerKnows);

            // 兩則各留一句話，而且兩句話講的是不同的理由
            Assert.Equal(2, decision.FilterNotes.Count);

            var closerNote = Assert.Single(decision.FilterNotes, n => n.StartsWith("evt_notcloser:"));
            Assert.Contains("hop 1", closerNote);
            Assert.Contains("no closer", closerNote);

            var newFactsNote = Assert.Single(decision.FilterNotes, n => n.StartsWith("evt_nonew:"));
            Assert.Contains("hop 0", newFactsNote);
            Assert.Contains("hop 2", newFactsNote);
            Assert.Contains("nothing new to add", newFactsNote);

            // 兩句話都要出現在實際印出去的那一行裡，否則等於沒寫
            string line = AskDecision.FormatLog("索納格", "lord_5_18_1", decision, knownCount: 2, relationGate: 0);
            Assert.Contains(closerNote, line);
            Assert.Contains(newFactsNote, line);
        }

        /// <summary>玩家第一次學到一則傳聞時，碎片集合要當場寫進 KnownFactIds，
        /// 不要留給日後的重述判定去從 (hop, 來源) 反推第二份答案。</summary>
        [Fact]
        public void ApplyOffer_FirstTimeLearn_RecordsKnownFactIds()
        {
            var (selector, engine, _) = CreateSelector();

            var evt = CreateSampleEvent("evt_first");
            evt.KnownBy.Add(new KnownByEntry { HeroId = "teller", Hop = 0 });

            var offer = new RumorOffer { EventId = evt.EventId, TellerHop = 0, ResultingPlayerHop = 1 };
            selector.ApplyOffer(offer, evt, "teller", day: 15.0);

            var entry = evt.EntryFor("player");
            Assert.NotNull(entry);
            Assert.Equal(1, entry!.Hop);
            Assert.NotNull(entry.KnownFactIds);

            var expected = engine.FactsAtHop(evt, 1, "teller").Select(f => f.Id).OrderBy(id => id).ToList();
            Assert.Equal(expected, entry.KnownFactIds!.OrderBy(id => id).ToList());
        }

        private static void AssertFormatLogExamples()
        {
            // 1. 有提案
            var offerDecision = new AskDecision
            {
                Refusal = AskRefusal.None,
                Relation = 100,
                Willingness = 104.0,
                Threshold = 5.0,
                CandidateCount = 1,
                FilteredNotVisible = 0,
                FilteredPlayerKnows = 0,
                Offer = new RumorOffer
                {
                    EventId = "evt_26029_80b8",
                    TellerHop = 2,
                    ResultingPlayerHop = 3,
                    Score = 4.21
                }
            };
            string line1 = AskDecision.FormatLog("特羅斯", "lord_5_21_2", offerDecision, knownCount: 1, relationGate: 0);
            Assert.Equal("Ask 特羅斯 (lord_5_21_2): offer evt_26029_80b8 hop 2->3 score 4.21 | rel 100, willingness 104.0 >= 5.0 | 1 known, 1 candidate, 0 filtered", line1);

            // 2. WillingnessGate
            var willDecision = new AskDecision
            {
                Refusal = AskRefusal.WillingnessGate,
                Relation = 0,
                Willingness = 4.0,
                Threshold = 5.0,
                CandidateCount = 1
            };
            string line2 = AskDecision.FormatLog("埃爾瑟特", "CharacterObject_2763", willDecision, knownCount: 1, relationGate: 0, gen: 0, hon: 1, calc: 0);
            Assert.Equal("Ask 埃爾瑟特 (CharacterObject_2763): no offer - willingness 4.0 < 5.0 | rel 0 (gen 0, hon 1, calc 0) | 1 known", line2);

            // 3. NoKnownEvents
            var emptyDecision = new AskDecision
            {
                Refusal = AskRefusal.NoKnownEvents,
                Relation = 0,
                Willingness = 10.0,
                Threshold = 5.0,
                CandidateCount = 0
            };
            string line3 = AskDecision.FormatLog("阿貝爾", "CharacterObject_4053", emptyDecision, knownCount: 0, relationGate: 0);
            Assert.Equal("Ask 阿貝爾 (CharacterObject_4053): no offer - knows nothing | rel 0", line3);

            // 4. AllCandidatesFiltered
            var filteredDecision = new AskDecision
            {
                Refusal = AskRefusal.AllCandidatesFiltered,
                Relation = 100,
                Willingness = 106.0,
                Threshold = 5.0,
                CandidateCount = 3,
                FilteredPlayerKnows = 2,
                FilteredNotVisible = 1
            };
            string line4 = AskDecision.FormatLog("貝薩格", "lord_5_13_1", filteredDecision, knownCount: 3, relationGate: 0);
            Assert.Equal("Ask 貝薩格 (lord_5_13_1): no offer - all 3 candidates filtered (2 player already knows, 1 secret not leaked) | rel 100, willingness 106.0 >= 5.0", line4);

            // 5. RelationGate
            var relDecision = new AskDecision
            {
                Refusal = AskRefusal.RelationGate,
                Relation = 0,
                Willingness = 0.0,
                Threshold = 5.0,
                CandidateCount = 4
            };
            string line5 = AskDecision.FormatLog("某人", "lord_x", relDecision, knownCount: 4, relationGate: 10);
            Assert.Equal("Ask 某人 (lord_x): no offer - relation 0 < gate 10 | 4 known", line5);
        }

        [Fact]
        public void DecideOnVolunteer_EachGate_ReportsWhichOneRefused()
        {
            var (selector, _, cfg) = CreateSelector();
            cfg.Dialogue.NpcVolunteerRelationGate = 25;
            cfg.Dialogue.CasualChatRelationGate = 20;
            cfg.Dialogue.VolunteerCooldownDays = 4.0;
            cfg.Dialogue.MaxVolunteersPerDay = 2;

            var candidates = new List<RumorCandidate>();

            // 1. 好感度不足 (RelationGate)
            var lowRel = new HeroSocialProfile
            {
                HeroId = "h_low",
                RelationWithPlayer = 15,
                LastVolunteeredDay = -1
            };
            var d1 = selector.DecideOnVolunteer(lowRel, candidates, day: 10.0, volunteersAlreadyToday: 0);
            Assert.Equal(VolunteerRefusal.RelationGate, d1.Refusal);
            Assert.Null(d1.Offer);
            Assert.Equal(15, d1.Relation);
            Assert.Equal(25, d1.RelationGate);
            Assert.False(d1.IsCloseKin);

            // 2. 冷卻中 (Cooldown)
            var onCooldown = new HeroSocialProfile
            {
                HeroId = "h_cd",
                RelationWithPlayer = 50,
                LastVolunteeredDay = 8.0 // day 10 - 8 = 2.0 < 4.0
            };
            var d2 = selector.DecideOnVolunteer(onCooldown, candidates, day: 10.0, volunteersAlreadyToday: 0);
            Assert.Equal(VolunteerRefusal.Cooldown, d2.Refusal);
            Assert.Null(d2.Offer);
            Assert.Equal(8.0, d2.LastVolunteeredDay);
            Assert.Equal(4.0, d2.CooldownDays);
            Assert.Equal(10.0, d2.Day);

            // 3. 每日上限已滿 (DailyCap)
            var ready = new HeroSocialProfile
            {
                HeroId = "h_ready",
                RelationWithPlayer = 50,
                LastVolunteeredDay = -1
            };
            var d3 = selector.DecideOnVolunteer(ready, candidates, day: 10.0, volunteersAlreadyToday: 2);
            Assert.Equal(VolunteerRefusal.DailyCap, d3.Refusal);
            Assert.Null(d3.Offer);
            Assert.Equal(2, d3.VolunteersToday);
            Assert.Equal(2, d3.MaxVolunteersPerDay);
        }

        [Fact]
        public void DecideOnVolunteer_AllCandidatesFiltered_FillsFilterNotes()
        {
            var (selector, _, cfg) = CreateSelector();
            cfg.Dialogue.NpcVolunteerRelationGate = 20;
            cfg.Dialogue.VolunteerCooldownDays = 3.0;
            cfg.Dialogue.MaxVolunteersPerDay = 1;

            var teller = new HeroSocialProfile
            {
                HeroId = "teller",
                RelationWithPlayer = 30,
                LastVolunteeredDay = -1
            };

            // 候選 1: 玩家已知且 teller 距離沒有更近 (hop 1 vs hop 1)
            var evt1 = CreateSampleEvent("evt_notcloser");
            evt1.KnownBy.Add(new KnownByEntry { HeroId = "teller", Hop = 1 });
            evt1.KnownBy.Add(new KnownByEntry { HeroId = "player", Hop = 1 });

            // 候選 2: 更近但沒有新碎片 (teller hop 0, player hop 2 持有所有碎片)
            var evt2 = CreateSampleEvent("evt_nonew");
            evt2.KnownBy.Add(new KnownByEntry { HeroId = "teller", Hop = 0 });
            evt2.KnownBy.Add(new KnownByEntry
            {
                HeroId = "player",
                Hop = 2,
                KnownFactIds = evt2.Facts.Select(f => f.Id).ToList()
            });

            var candidates = new List<RumorCandidate>
            {
                new RumorCandidate { Event = evt1, TellerHop = 1, PlayerExistingHop = 1 },
                new RumorCandidate { Event = evt2, TellerHop = 0, PlayerExistingHop = 2 }
            };

            var decision = selector.DecideOnVolunteer(teller, candidates, day: 10.0, volunteersAlreadyToday: 0);

            Assert.Equal(VolunteerRefusal.AllCandidatesFiltered, decision.Refusal);
            Assert.Null(decision.Offer);
            Assert.Equal(2, decision.CandidateCount);
            Assert.Equal(2, decision.FilteredPlayerKnows);
            Assert.Equal(2, decision.FilterNotes.Count);

            var note1 = Assert.Single(decision.FilterNotes, n => n.StartsWith("evt_notcloser:"));
            Assert.Contains("no closer", note1);

            var note2 = Assert.Single(decision.FilterNotes, n => n.StartsWith("evt_nonew:"));
            Assert.Contains("nothing new to add", note2);
        }
    }
}
