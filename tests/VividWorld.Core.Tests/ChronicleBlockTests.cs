#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using VividWorld.Core.Catalog;
using VividWorld.Core.Config;
using VividWorld.Core.Events;
using VividWorld.Core.Persistence;
using VividWorld.Core.Presentation;
using VividWorld.Core.Tests.Fakes;
using VividWorld.Core.Util;
using Xunit;

namespace VividWorld.Core.Tests
{
    public class ChronicleBlockTests
    {
        private static EventTemplate SimpleTemplate(string type) => new EventTemplate
        {
            Type = type,
            Headline = "plain " + type
        };

        #region 1. 找源頭測試 (Root Resolution)

        [Fact]
        public void RootResolution_ZeroHops_ReturnsSelfAsRoot()
        {
            var evt = new WorldEvent
            {
                EventId = "evt_root_1",
                Type = "hero_taken_prisoner",
                Day = 10.0,
                Participants = new Dictionary<string, string> { ["prisoner"] = "p1", ["captor"] = "c1" }
            };

            var (rootId, rootType, rootDay, rootParts, hops, stop) = PlayerHeardLogStore.ResolveRoot(evt, null);

            Assert.Equal("evt_root_1", rootId);
            Assert.Equal("hero_taken_prisoner", rootType);
            Assert.Equal(10.0, rootDay);
            Assert.Equal("p1", rootParts["prisoner"]);
            Assert.Equal(0, hops);
            Assert.Equal("no linked event (is root)", stop);
        }

        [Fact]
        public void RootResolution_OneAndTwoHops_TracesBackToRoot()
        {
            var root = new WorldEvent
            {
                EventId = "evt_root",
                Type = "hero_taken_prisoner",
                Day = 5.0,
                Participants = new Dictionary<string, string> { ["prisoner"] = "hero_p" }
            };
            var hop1 = new WorldEvent
            {
                EventId = "evt_hop1",
                Type = "hero_escaped_captivity",
                Day = 12.0,
                LinkedEventId = "evt_root"
            };
            var hop2 = new WorldEvent
            {
                EventId = "evt_hop2",
                Type = "talk_denied_rash_capture",
                Day = 15.0,
                LinkedEventId = "evt_hop1"
            };

            var store = new Dictionary<string, WorldEvent>
            {
                ["evt_root"] = root,
                ["evt_hop1"] = hop1,
                ["evt_hop2"] = hop2
            };

            var (rootId, rootType, rootDay, rootParts, hops, stop) = PlayerHeardLogStore.ResolveRoot(hop2, id => store.TryGetValue(id, out var e) ? e : null);

            Assert.Equal("evt_root", rootId);
            Assert.Equal("hero_taken_prisoner", rootType);
            Assert.Equal(5.0, rootDay);
            Assert.Equal("hero_p", rootParts["prisoner"]);
            Assert.Equal(2, hops);
            Assert.Equal("reached root event", stop);
        }

        [Fact]
        public void RootResolution_ParentMissingInStore_StopsAtLastReachableEvent()
        {
            var hop1 = new WorldEvent
            {
                EventId = "evt_hop1",
                Type = "hero_escaped_captivity",
                Day = 12.0,
                LinkedEventId = "evt_missing_root"
            };
            var hop2 = new WorldEvent
            {
                EventId = "evt_hop2",
                Type = "talk_denied_rash_capture",
                Day = 15.0,
                LinkedEventId = "evt_hop1"
            };

            var store = new Dictionary<string, WorldEvent> { ["evt_hop1"] = hop1 };

            var (rootId, rootType, rootDay, _, hops, stop) = PlayerHeardLogStore.ResolveRoot(hop2, id => store.TryGetValue(id, out var e) ? e : null);

            Assert.Equal("evt_hop1", rootId);
            Assert.Equal("hero_escaped_captivity", rootType);
            Assert.Equal(12.0, rootDay);
            Assert.Equal(1, hops);
            Assert.Contains("not found in event store", stop);
        }

        [Fact]
        public void RootResolution_CycleDetected_StopsSafelyWithoutInfiniteLoop()
        {
            var evtA = new WorldEvent
            {
                EventId = "evt_a",
                Type = "type_a",
                Day = 10.0,
                LinkedEventId = "evt_b"
            };
            var evtB = new WorldEvent
            {
                EventId = "evt_b",
                Type = "type_b",
                Day = 12.0,
                LinkedEventId = "evt_a"
            };

            var store = new Dictionary<string, WorldEvent>
            {
                ["evt_a"] = evtA,
                ["evt_b"] = evtB
            };

            var (rootId, _, _, _, _, stop) = PlayerHeardLogStore.ResolveRoot(evtA, id => store.TryGetValue(id, out var e) ? e : null);

            Assert.Equal("evt_b", rootId);
            Assert.Contains("cycle detected", stop);
        }

        [Fact]
        public void RootResolution_DepthExceedingFive_StopsAtFiveHops()
        {
            var events = new List<WorldEvent>();
            for (int i = 0; i < 8; i++)
            {
                events.Add(new WorldEvent
                {
                    EventId = $"evt_{i}",
                    Type = $"type_{i}",
                    Day = i + 1.0,
                    LinkedEventId = i > 0 ? $"evt_{i - 1}" : null
                });
            }

            var store = events.ToDictionary(e => e.EventId);

            var (rootId, _, _, _, hops, stop) = PlayerHeardLogStore.ResolveRoot(events[7], id => store.TryGetValue(id, out var e) ? e : null);

            Assert.Equal(5, hops);
            Assert.Equal("evt_2", rootId);
            Assert.Equal("max depth reached (5 hops)", stop);
        }

        #endregion

        #region 2. 區塊分組與排序測試 (Block Grouping & Ordering)

        [Fact]
        public void BlockGrouping_GroupsEntriesByRootEventId()
        {
            var writer = new FailingFileWriter();
            var log = new PlayerHeardLog
            {
                Entries = new List<PlayerHeardEntry>
                {
                    new PlayerHeardEntry
                    {
                        EventId = "evt_root_1", Type = "hero_taken_prisoner", Day = 10.0, LearnedDay = 10.0,
                        RootEventId = "evt_root_1", RootType = "hero_taken_prisoner", RootDay = 10.0,
                        Sources = new List<PlayerHeardSource> { new PlayerHeardSource { HeroId = "h1", Day = 10.0 } }
                    },
                    new PlayerHeardEntry
                    {
                        EventId = "evt_escape_1", Type = "hero_escaped_captivity", Day = 15.0, LearnedDay = 16.0,
                        LinkedEventId = "evt_root_1",
                        RootEventId = "evt_root_1", RootType = "hero_taken_prisoner", RootDay = 10.0,
                        Sources = new List<PlayerHeardSource> { new PlayerHeardSource { HeroId = "h2", Day = 16.0 } }
                    },
                    new PlayerHeardEntry
                    {
                        EventId = "evt_other", Type = "child_born", Day = 14.0, LearnedDay = 14.0,
                        RootEventId = "evt_other", RootType = "child_born", RootDay = 14.0,
                        Sources = new List<PlayerHeardSource> { new PlayerHeardSource { HeroId = "h3", Day = 14.0 } }
                    }
                }
            };
            writer.WriteAllText("player_heard.json", VividJson.Write(log));
            var store = new PlayerHeardLogStore("player_heard.json", writer);
            store.Load();

            var provider = new ChronicleProvider(store, SimpleTemplate, new PresentationConfig());
            var blocks = provider.ForPlayer(50, 100.0, out var stats);

            Assert.Equal(2, blocks.Count);
            Assert.Equal(2, stats.TotalBlocks);

            var root1Block = blocks.FirstOrDefault(b => b.EventId == "evt_root_1");
            Assert.NotNull(root1Block);
            Assert.Equal(2, root1Block!.Matters.Count);
            Assert.Equal("evt_root_1", root1Block.Matters[0].EventId);
            Assert.Equal("evt_escape_1", root1Block.Matters[1].EventId);

            var otherBlock = blocks.FirstOrDefault(b => b.EventId == "evt_other");
            Assert.NotNull(otherBlock);
            Assert.Single(otherBlock!.Matters);
        }

        [Fact]
        public void BlockOrdering_NewerTellingBringsOldBlockToTop()
        {
            var writer = new FailingFileWriter();
            var log = new PlayerHeardLog
            {
                Entries = new List<PlayerHeardEntry>
                {
                    // Block A: 發生於 Day 1.0，但最新講述在 Day 20.0
                    new PlayerHeardEntry
                    {
                        EventId = "evt_a", Type = "hero_taken_prisoner", Day = 1.0, LearnedDay = 2.0,
                        RootEventId = "evt_a", RootType = "hero_taken_prisoner", RootDay = 1.0,
                        Sources = new List<PlayerHeardSource>
                        {
                            new PlayerHeardSource { HeroId = "h1", Day = 2.0 },
                            new PlayerHeardSource { HeroId = "h2", Day = 20.0 }
                        }
                    },
                    // Block B: 發生於 Day 10.0，最新講述在 Day 12.0
                    new PlayerHeardEntry
                    {
                        EventId = "evt_b", Type = "child_born", Day = 10.0, LearnedDay = 12.0,
                        RootEventId = "evt_b", RootType = "child_born", RootDay = 10.0,
                        Sources = new List<PlayerHeardSource>
                        {
                            new PlayerHeardSource { HeroId = "h3", Day = 12.0 }
                        }
                    }
                }
            };
            writer.WriteAllText("player_heard.json", VividJson.Write(log));
            var store = new PlayerHeardLogStore("player_heard.json", writer);
            store.Load();

            var provider = new ChronicleProvider(store, SimpleTemplate, new PresentationConfig());
            var blocks = provider.ForPlayer(50, 100.0, out _);

            Assert.Equal(2, blocks.Count);
            // Block A 有 Day 20.0 的來源，排在第一名
            Assert.Equal("evt_a", blocks[0].EventId);
            Assert.Equal("evt_b", blocks[1].EventId);
        }

        [Fact]
        public void BlockOrdering_SortsByMaxSourceDayDesc_ThenRootDayDesc_ThenRootIdAsc()
        {
            var writer = new FailingFileWriter();
            var log = new PlayerHeardLog
            {
                Entries = new List<PlayerHeardEntry>
                {
                    new PlayerHeardEntry
                    {
                        EventId = "evt_z", Type = "conduct_poisoned", Day = 5.0, LearnedDay = 10.0,
                        RootEventId = "evt_z", RootType = "conduct_poisoned", RootDay = 5.0,
                        Sources = new List<PlayerHeardSource> { new PlayerHeardSource { HeroId = "h1", Day = 10.0 } }
                    },
                    new PlayerHeardEntry
                    {
                        EventId = "evt_a", Type = "conduct_poisoned", Day = 5.0, LearnedDay = 10.0,
                        RootEventId = "evt_a", RootType = "conduct_poisoned", RootDay = 5.0,
                        Sources = new List<PlayerHeardSource> { new PlayerHeardSource { HeroId = "h2", Day = 10.0 } }
                    },
                    new PlayerHeardEntry
                    {
                        EventId = "evt_c", Type = "conduct_poisoned", Day = 8.0, LearnedDay = 10.0,
                        RootEventId = "evt_c", RootType = "conduct_poisoned", RootDay = 8.0,
                        Sources = new List<PlayerHeardSource> { new PlayerHeardSource { HeroId = "h3", Day = 10.0 } }
                    }
                }
            };
            writer.WriteAllText("player_heard.json", VividJson.Write(log));
            var store = new PlayerHeardLogStore("player_heard.json", writer);
            store.Load();

            var provider = new ChronicleProvider(store, SimpleTemplate, new PresentationConfig());
            var blocks = provider.ForPlayer(50, 100.0, out _);

            Assert.Equal(3, blocks.Count);
            // MaxSourceDay 都是 10.0；evt_c 的 RootDay 8.0 最大，排第一
            Assert.Equal("evt_c", blocks[0].EventId);
            // evt_a 與 evt_z 的 RootDay 都是 5.0；依 RootEventId (Ordinal) evt_a < evt_z
            Assert.Equal("evt_a", blocks[1].EventId);
            Assert.Equal("evt_z", blocks[2].EventId);
        }

        #endregion

        #region 3. 事的順序與說法攤平 (Matter Sequence & Flattening)

        [Fact]
        public void MatterSequence_RootMatterFirst_LaterMattersOrderedByEventDay()
        {
            var writer = new FailingFileWriter();
            var log = new PlayerHeardLog
            {
                Entries = new List<PlayerHeardEntry>
                {
                    // 源頭 (Day 5.0)
                    new PlayerHeardEntry
                    {
                        EventId = "evt_root", Type = "hero_taken_prisoner", Day = 5.0, LearnedDay = 6.0,
                        RootEventId = "evt_root", RootType = "hero_taken_prisoner", RootDay = 5.0,
                        Sources = new List<PlayerHeardSource> { new PlayerHeardSource { HeroId = "h1", Day = 6.0 } }
                    },
                    // 後續 2: 獲釋 (Day 25.0)
                    new PlayerHeardEntry
                    {
                        EventId = "evt_released", Type = "hero_released", Day = 25.0, LearnedDay = 26.0,
                        LinkedEventId = "evt_root",
                        RootEventId = "evt_root", RootType = "hero_taken_prisoner", RootDay = 5.0,
                        Sources = new List<PlayerHeardSource> { new PlayerHeardSource { HeroId = "h2", Day = 26.0 } }
                    },
                    // 後續 1: 脫逃 (Day 15.0)
                    new PlayerHeardEntry
                    {
                        EventId = "evt_escaped", Type = "hero_escaped_captivity", Day = 15.0, LearnedDay = 16.0,
                        LinkedEventId = "evt_root",
                        RootEventId = "evt_root", RootType = "hero_taken_prisoner", RootDay = 5.0,
                        Sources = new List<PlayerHeardSource> { new PlayerHeardSource { HeroId = "h3", Day = 16.0 } }
                    }
                }
            };
            writer.WriteAllText("player_heard.json", VividJson.Write(log));
            var store = new PlayerHeardLogStore("player_heard.json", writer);
            store.Load();

            var provider = new ChronicleProvider(store, SimpleTemplate, new PresentationConfig());
            var blocks = provider.ForPlayer(50, 100.0, out _);

            var block = Assert.Single(blocks);
            Assert.Equal(3, block.Matters.Count);
            // 第一件事永遠是源頭
            Assert.Equal("evt_root", block.Matters[0].EventId);
            Assert.False(block.Matters[0].HasHeading);

            // 第二件事起依 Day 由小到大排
            Assert.Equal("evt_escaped", block.Matters[1].EventId);
            Assert.Equal(15.0, block.Matters[1].Day);
            Assert.True(block.Matters[1].HasHeading);

            Assert.Equal("evt_released", block.Matters[2].EventId);
            Assert.Equal(25.0, block.Matters[2].Day);
            Assert.True(block.Matters[2].HasHeading);
        }

        [Fact]
        public void MatterSequence_HearingOrderDoesNotAffectMatterOrder()
        {
            var writer = new FailingFileWriter();
            var log = new PlayerHeardLog
            {
                Entries = new List<PlayerHeardEntry>
                {
                    // 先聽到脫逃 (LearnedDay 10.0, Event Day 12.0)
                    new PlayerHeardEntry
                    {
                        EventId = "evt_escaped", Type = "hero_escaped_captivity", Day = 12.0, LearnedDay = 10.0,
                        LinkedEventId = "evt_root",
                        RootEventId = "evt_root", RootType = "hero_taken_prisoner", RootDay = 5.0,
                        Sources = new List<PlayerHeardSource> { new PlayerHeardSource { HeroId = "h1", Day = 10.0 } }
                    },
                    // 後聽到被俘 (LearnedDay 15.0, Event Day 5.0)
                    new PlayerHeardEntry
                    {
                        EventId = "evt_root", Type = "hero_taken_prisoner", Day = 5.0, LearnedDay = 15.0,
                        RootEventId = "evt_root", RootType = "hero_taken_prisoner", RootDay = 5.0,
                        Sources = new List<PlayerHeardSource> { new PlayerHeardSource { HeroId = "h2", Day = 15.0 } }
                    }
                }
            };
            writer.WriteAllText("player_heard.json", VividJson.Write(log));
            var store = new PlayerHeardLogStore("player_heard.json", writer);
            store.Load();

            var provider = new ChronicleProvider(store, SimpleTemplate, new PresentationConfig());
            var block = Assert.Single(provider.ForPlayer(50, 100.0, out _));

            Assert.Equal(2, block.Matters.Count);
            // 源頭事件依然在第一位
            Assert.Equal("evt_root", block.Matters[0].EventId);
            Assert.Equal("evt_escaped", block.Matters[1].EventId);
        }

        [Fact]
        public void MatterSequence_RootNotHeard_LaterMatterStillGetsHeading()
        {
            var writer = new FailingFileWriter();
            var log = new PlayerHeardLog
            {
                Entries = new List<PlayerHeardEntry>
                {
                    // 只聽過脫逃，沒聽過被俘：區塊標題是被俘，這一段要有「脫逃」小標，才不會被當成被俘的說法
                    new PlayerHeardEntry
                    {
                        EventId = "evt_escaped", Type = "hero_escaped_captivity", Day = 12.0, LearnedDay = 13.0,
                        LinkedEventId = "evt_root",
                        RootEventId = "evt_root", RootType = "hero_taken_prisoner", RootDay = 5.0,
                        Sources = new List<PlayerHeardSource> { new PlayerHeardSource { HeroId = "h1", Day = 13.0 } }
                    }
                }
            };
            writer.WriteAllText("player_heard.json", VividJson.Write(log));
            var store = new PlayerHeardLogStore("player_heard.json", writer);
            store.Load();

            var provider = new ChronicleProvider(store, SimpleTemplate, new PresentationConfig());
            var block = Assert.Single(provider.ForPlayer(50, 100.0, out _));

            Assert.Equal("hero_taken_prisoner", block.EventType);
            var matter = Assert.Single(block.Matters);
            Assert.Equal("evt_escaped", matter.EventId);
            Assert.True(matter.HasHeading);
        }

        [Fact]
        public void TellingOrdering_FlattensTellingsWithinMatterBySourceDay()
        {
            var writer = new FailingFileWriter();
            var log = new PlayerHeardLog
            {
                Entries = new List<PlayerHeardEntry>
                {
                    new PlayerHeardEntry
                    {
                        EventId = "evt_1", Type = "child_born", Day = 10.0, LearnedDay = 12.0,
                        RootEventId = "evt_1", RootType = "child_born", RootDay = 10.0,
                        Sources = new List<PlayerHeardSource>
                        {
                            new PlayerHeardSource { HeroId = "teller_c", Day = 18.0 },
                            new PlayerHeardSource { HeroId = "teller_a", Day = 11.0 },
                            new PlayerHeardSource { HeroId = "teller_b", Day = 14.0 }
                        }
                    }
                }
            };
            writer.WriteAllText("player_heard.json", VividJson.Write(log));
            var store = new PlayerHeardLogStore("player_heard.json", writer);
            store.Load();

            var provider = new ChronicleProvider(store, SimpleTemplate, new PresentationConfig());
            var block = Assert.Single(provider.ForPlayer(50, 100.0, out _));

            var matter = Assert.Single(block.Matters);
            Assert.Equal(3, matter.Sources.Count);
            // 來源依初次聽到的日子由舊到新
            Assert.Equal("teller_a", matter.Sources[0].HeroId);
            Assert.Equal("teller_b", matter.Sources[1].HeroId);
            Assert.Equal("teller_c", matter.Sources[2].HeroId);
        }

        #endregion

        #region 4. 有矛盾判定 (Conflict Detection)

        [Fact]
        public void Conflict_BothHeard_MarksBlockAsConflicting()
        {
            var writer = new FailingFileWriter();
            var log = new PlayerHeardLog
            {
                Entries = new List<PlayerHeardEntry>
                {
                    new PlayerHeardEntry
                    {
                        EventId = "evt_rash", Type = "conduct_rash_capture", Day = 5.0, LearnedDay = 6.0,
                        RootEventId = "evt_root", RootType = "hero_taken_prisoner", RootDay = 5.0,
                        Sources = new List<PlayerHeardSource> { new PlayerHeardSource { HeroId = "h1", Day = 6.0 } }
                    },
                    new PlayerHeardEntry
                    {
                        EventId = "evt_deny", Type = "talk_denied_rash_capture", Day = 10.0, LearnedDay = 11.0,
                        LinkedEventId = "evt_rash",
                        RootEventId = "evt_root", RootType = "hero_taken_prisoner", RootDay = 5.0,
                        Sources = new List<PlayerHeardSource> { new PlayerHeardSource { HeroId = "h2", Day = 11.0 } }
                    }
                }
            };
            writer.WriteAllText("player_heard.json", VividJson.Write(log));
            var store = new PlayerHeardLogStore("player_heard.json", writer);
            store.Load();

            var provider = new ChronicleProvider(store, SimpleTemplate, new PresentationConfig());
            var block = Assert.Single(provider.ForPlayer(50, 100.0, out var stats));

            Assert.True(block.HasConflict);
            Assert.Contains("evt_deny", block.ConflictReason);
            Assert.Contains("evt_rash", block.ConflictReason);
            Assert.Equal(1, stats.ConflictBlocks);
        }

        [Fact]
        public void Conflict_OnlyResponseHeard_DoesNotMarkConflict()
        {
            var writer = new FailingFileWriter();
            var log = new PlayerHeardLog
            {
                Entries = new List<PlayerHeardEntry>
                {
                    // 玩家只聽過否認，沒聽過原話
                    new PlayerHeardEntry
                    {
                        EventId = "evt_deny", Type = "talk_denied_rash_capture", Day = 10.0, LearnedDay = 11.0,
                        LinkedEventId = "evt_rash",
                        RootEventId = "evt_root", RootType = "hero_taken_prisoner", RootDay = 5.0,
                        Sources = new List<PlayerHeardSource> { new PlayerHeardSource { HeroId = "h2", Day = 11.0 } }
                    }
                }
            };
            writer.WriteAllText("player_heard.json", VividJson.Write(log));
            var store = new PlayerHeardLogStore("player_heard.json", writer);
            store.Load();

            var provider = new ChronicleProvider(store, SimpleTemplate, new PresentationConfig());
            var block = Assert.Single(provider.ForPlayer(50, 100.0, out var stats));

            Assert.False(block.HasConflict);
            Assert.Null(block.ConflictReason);
            Assert.Equal(0, stats.ConflictBlocks);
        }

        [Fact]
        public void Conflict_OnlyOriginalHeard_DoesNotMarkConflict()
        {
            var writer = new FailingFileWriter();
            var log = new PlayerHeardLog
            {
                Entries = new List<PlayerHeardEntry>
                {
                    new PlayerHeardEntry
                    {
                        EventId = "evt_rash", Type = "conduct_rash_capture", Day = 5.0, LearnedDay = 6.0,
                        RootEventId = "evt_root", RootType = "hero_taken_prisoner", RootDay = 5.0,
                        Sources = new List<PlayerHeardSource> { new PlayerHeardSource { HeroId = "h1", Day = 6.0 } }
                    }
                }
            };
            writer.WriteAllText("player_heard.json", VividJson.Write(log));
            var store = new PlayerHeardLogStore("player_heard.json", writer);
            store.Load();

            var provider = new ChronicleProvider(store, SimpleTemplate, new PresentationConfig());
            var block = Assert.Single(provider.ForPlayer(50, 100.0, out _));

            Assert.False(block.HasConflict);
        }

        [Fact]
        public void Conflict_NaturalDeathAndPoisoning_MarksConflict()
        {
            var writer = new FailingFileWriter();
            var log = new PlayerHeardLog
            {
                Entries = new List<PlayerHeardEntry>
                {
                    new PlayerHeardEntry
                    {
                        EventId = "evt_death", Type = "hero_died_naturally", Day = 10.0, LearnedDay = 11.0,
                        RootEventId = "evt_death", RootType = "hero_died_naturally", RootDay = 10.0,
                        Sources = new List<PlayerHeardSource> { new PlayerHeardSource { HeroId = "h1", Day = 11.0 } }
                    },
                    new PlayerHeardEntry
                    {
                        EventId = "evt_poison", Type = "conduct_poisoned", Day = 10.0, LearnedDay = 12.0,
                        LinkedEventId = "evt_death",
                        RootEventId = "evt_death", RootType = "hero_died_naturally", RootDay = 10.0,
                        Sources = new List<PlayerHeardSource> { new PlayerHeardSource { HeroId = "h2", Day = 12.0 } }
                    }
                }
            };
            writer.WriteAllText("player_heard.json", VividJson.Write(log));
            var store = new PlayerHeardLogStore("player_heard.json", writer);
            store.Load();

            var provider = new ChronicleProvider(store, SimpleTemplate, new PresentationConfig());
            var block = Assert.Single(provider.ForPlayer(50, 100.0, out _));

            Assert.True(block.HasConflict);
            Assert.Contains("evt_death", block.ConflictReason);
            Assert.Contains("evt_poison", block.ConflictReason);
        }

        #endregion

        #region 5. 標題與退回 (Headlines & Fallback)

        [Fact]
        public void Headline_NamedTitle_ReplacesAllPlaceholders()
        {
            var entry = new ChronicleEntry
            {
                EventType = "seat_dispute_demanded",
                HeadlineTextId = "VividWorld_EventType_seat_dispute_demanded",
                HeadlineFallback = "plain",
                NamedHeadlineTextId = "VividWorld_EventTypeNamed_seat_dispute_demanded",
                RootParticipants = new Dictionary<string, string>
                {
                    ["slighted"] = "hero_a",
                    ["favored"] = "hero_b"
                }
            };

            var templates = new Dictionary<string, string>
            {
                ["VividWorld_EventType_seat_dispute_demanded"] = "席間起爭執",
                ["VividWorld_EventTypeNamed_seat_dispute_demanded"] = "{SLIGHTED}當眾要{FAVORED}讓座"
            };

            var result = ChronicleHeadline.Resolve(
                entry,
                (id, fb) => templates.TryGetValue(id ?? string.Empty, out var t) ? t : (fb ?? string.Empty),
                varRef => varRef == "hero:hero_a" ? "阿爾" : (varRef == "hero:hero_b" ? "貝爾" : null));

            Assert.True(result.UsedNamed);
            Assert.Equal("阿爾當眾要貝爾讓座", result.Text);
            Assert.Contains("named (hero:hero_a, hero:hero_b)", result.Note);
        }

        [Fact]
        public void Headline_MissingParticipant_FallsBackToPlainTitle()
        {
            var entry = new ChronicleEntry
            {
                EventType = "seat_dispute_demanded",
                HeadlineTextId = "VividWorld_EventType_seat_dispute_demanded",
                HeadlineFallback = "席間爭執",
                NamedHeadlineTextId = "VividWorld_EventTypeNamed_seat_dispute_demanded",
                RootParticipants = new Dictionary<string, string>
                {
                    ["slighted"] = "hero_a"
                    // favored 缺少
                }
            };

            var templates = new Dictionary<string, string>
            {
                ["VividWorld_EventType_seat_dispute_demanded"] = "席間爭執",
                ["VividWorld_EventTypeNamed_seat_dispute_demanded"] = "{SLIGHTED}當眾要{FAVORED}讓座"
            };

            var result = ChronicleHeadline.Resolve(
                entry,
                (id, fb) => templates.TryGetValue(id ?? string.Empty, out var t) ? t : (fb ?? string.Empty),
                varRef => "阿爾");

            Assert.False(result.UsedNamed);
            Assert.Equal("席間爭執", result.Text);
            Assert.Contains("does not say who the favored is", result.Note);
        }

        [Fact]
        public void Headline_MissingKeyInLanguage_FallsBackToPlainTitle()
        {
            var entry = new ChronicleEntry
            {
                EventType = "hero_murdered",
                HeadlineTextId = "VividWorld_EventType_hero_murdered",
                HeadlineFallback = "謀殺",
                NamedHeadlineTextId = "VividWorld_EventTypeNamed_hero_murdered",
                RootParticipants = new Dictionary<string, string> { ["victim"] = "hero_v" }
            };

            var templates = new Dictionary<string, string>
            {
                ["VividWorld_EventType_hero_murdered"] = "謀殺"
                // 缺少 VividWorld_EventTypeNamed_hero_murdered
            };

            var result = ChronicleHeadline.Resolve(
                entry,
                (id, fb) => templates.TryGetValue(id ?? string.Empty, out var t) ? t : (fb ?? string.Empty),
                varRef => "維克");

            Assert.False(result.UsedNamed);
            Assert.Equal("謀殺", result.Text);
            Assert.Contains("no string 'VividWorld_EventTypeNamed_hero_murdered'", result.Note);
        }

        [Fact]
        public void Headline_TalkResponse_HasNoNamedTitle()
        {
            Assert.Null(ChronicleHeadline.NamedTextIdFor("talk_denied_rash_capture"));
            Assert.Null(ChronicleHeadline.NamedTextIdFor("talk_not_so_seat_dispute_yielded"));
        }

        #endregion

        #region 6. 區塊上限與補源頭 (Cap & Backfill)

        [Fact]
        public void Cap_LimitsBlockCount_NotIndividualRecordsCount()
        {
            var writer = new FailingFileWriter();
            var entries = new List<PlayerHeardEntry>();

            for (int i = 0; i < 5; i++)
            {
                string rootId = $"root_{i}";
                // 每個區塊 2 則紀錄
                entries.Add(new PlayerHeardEntry
                {
                    EventId = rootId, Type = "conduct_poisoned", Day = i + 1.0, LearnedDay = i + 1.0,
                    RootEventId = rootId, RootType = "conduct_poisoned", RootDay = i + 1.0,
                    Sources = new List<PlayerHeardSource> { new PlayerHeardSource { HeroId = $"h_{i}", Day = i + 1.0 } }
                });
                entries.Add(new PlayerHeardEntry
                {
                    EventId = $"sub_{i}", Type = "talk_denied_poisoned", Day = i + 2.0, LearnedDay = i + 2.0,
                    LinkedEventId = rootId,
                    RootEventId = rootId, RootType = "conduct_poisoned", RootDay = i + 1.0,
                    Sources = new List<PlayerHeardSource> { new PlayerHeardSource { HeroId = $"h_{i}_b", Day = i + 2.0 } }
                });
            }

            writer.WriteAllText("player_heard.json", VividJson.Write(new PlayerHeardLog { Entries = entries }));
            var store = new PlayerHeardLogStore("player_heard.json", writer);
            store.Load();

            var provider = new ChronicleProvider(store, SimpleTemplate, new PresentationConfig());
            var blocks = provider.ForPlayer(3, 100.0, out var stats);

            Assert.Equal(3, blocks.Count);
            Assert.Equal(5, stats.TotalBlocks);
            Assert.Equal(3, stats.Returned);
            Assert.Equal(2, stats.CappedOutBlocks);
        }

        [Fact]
        public void Backfill_ThreeMethods_ResolvesCorrectly()
        {
            var writer = new FailingFileWriter();
            var log = new PlayerHeardLog
            {
                Entries = new List<PlayerHeardEntry>
                {
                    // 1. 從 EventStore 查得到 (evt_1 -> root_in_store)
                    new PlayerHeardEntry
                    {
                        EventId = "evt_1", Type = "talk_denied_rash_capture", Day = 10.0,
                        LinkedEventId = "root_in_store"
                    },
                    // 2. 從 HeardLog 鏈查得到 (evt_2 -> evt_heard_parent，後者在 heard log 但不在 store)
                    new PlayerHeardEntry
                    {
                        EventId = "evt_2", Type = "talk_denied_mistreated_prisoner", Day = 12.0,
                        LinkedEventId = "evt_heard_parent"
                    },
                    new PlayerHeardEntry
                    {
                        EventId = "evt_heard_parent", Type = "conduct_mistreated_prisoner", Day = 8.0,
                        RootEventId = "evt_heard_parent", RootType = "conduct_mistreated_prisoner", RootDay = 8.0
                    },
                    // 3. 查不到 (evt_3 無 linked 也無 store)
                    new PlayerHeardEntry
                    {
                        EventId = "evt_3", Type = "conduct_spoke_against_ruler", Day = 6.0
                    }
                }
            };

            writer.WriteAllText("player_heard.json", VividJson.Write(log));
            var store = new PlayerHeardLogStore("player_heard.json", writer);
            store.EventLookup = id => id == "root_in_store"
                ? new WorldEvent { EventId = "root_in_store", Type = "hero_taken_prisoner", Day = 4.0 }
                : null;
            store.Load();

            var stats = store.EnsureRoots();

            Assert.Equal(1, stats.FromEventStore);
            Assert.Equal(1, stats.FromHeardLog);
            Assert.Equal(1, stats.AsSelf);
            Assert.Equal(3, stats.TotalBackfilled);

            var e1 = store.Find("evt_1");
            Assert.Equal("root_in_store", e1?.RootEventId);
            Assert.Equal("hero_taken_prisoner", e1?.RootType);
            Assert.Equal(4.0, e1?.RootDay);

            var e2 = store.Find("evt_2");
            Assert.Equal("evt_heard_parent", e2?.RootEventId);
            Assert.Equal("conduct_mistreated_prisoner", e2?.RootType);
            Assert.Equal(8.0, e2?.RootDay);

            var e3 = store.Find("evt_3");
            Assert.Equal("evt_3", e3?.RootEventId);
            Assert.Equal("conduct_spoke_against_ruler", e3?.RootType);
            Assert.Equal(6.0, e3?.RootDay);
        }

        [Fact]
        public void HiddenFuture_ExcludesRecordsBeyondCurrentDay()
        {
            var writer = new FailingFileWriter();
            var log = new PlayerHeardLog
            {
                Entries = new List<PlayerHeardEntry>
                {
                    new PlayerHeardEntry
                    {
                        EventId = "evt_past", Type = "child_born", Day = 10.0, LearnedDay = 12.0,
                        RootEventId = "evt_past", RootType = "child_born", RootDay = 10.0,
                        Sources = new List<PlayerHeardSource> { new PlayerHeardSource { HeroId = "h1", Day = 12.0 } }
                    },
                    new PlayerHeardEntry
                    {
                        EventId = "evt_future", Type = "child_born", Day = 50.0, LearnedDay = 52.0,
                        RootEventId = "evt_future", RootType = "child_born", RootDay = 50.0,
                        Sources = new List<PlayerHeardSource> { new PlayerHeardSource { HeroId = "h2", Day = 52.0 } }
                    }
                }
            };
            writer.WriteAllText("player_heard.json", VividJson.Write(log));
            var store = new PlayerHeardLogStore("player_heard.json", writer);
            store.Load();

            var provider = new ChronicleProvider(store, SimpleTemplate, new PresentationConfig());
            var blocks = provider.ForPlayer(50, 30.0, out var stats);

            Assert.Single(blocks);
            Assert.Equal("evt_past", blocks[0].EventId);
            Assert.Equal(1, stats.HiddenFuture);
        }

        #endregion
    }
}
