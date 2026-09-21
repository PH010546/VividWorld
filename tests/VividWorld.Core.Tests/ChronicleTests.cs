#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using Newtonsoft.Json.Linq;
using VividWorld.Core.Catalog;
using VividWorld.Core.Config;
using VividWorld.Core.Events;
using VividWorld.Core.Persistence;
using VividWorld.Core.Presentation;
using VividWorld.Core.Rumors;
using VividWorld.Core.Util;
using VividWorld.Core.Tests.Fakes;
using Xunit;

namespace VividWorld.Core.Tests
{
    public class ChronicleTests
    {
        private const string PlayerHeroId = "hero_player";

        private static (RumorEngine engine, KnownByIndex knownBy, PresentationConfig cfg) CreateTestContext()
        {
            var config = new VividWorldConfig();
            var rng = new SplitMix64Rng();
            var traitLookup = new FakeHeroTraitLookup();
            var channel = new FakePropagationChannel();
            var retention = FactRetentionPolicies.Create(config, rng, 12345L);
            var embellishment = NullEmbellishmentPolicy.Instance;
            var engine = new RumorEngine(config, retention, embellishment, channel, traitLookup, rng, 12345L, PlayerHeroId);
            var knownBy = new KnownByIndex();
            return (engine, knownBy, config.Presentation);
        }

        private static WorldEvent CreateEvent(
            string eventId,
            string type = "duel",
            double day = 10.0,
            EventOrigin origin = EventOrigin.Public,
            bool isLeaked = true,
            int factCount = 5)
        {
            var facts = new List<Fact>();
            for (int i = 0; i < factCount; i++)
            {
                facts.Add(new Fact
                {
                    Id = "fact_" + i,
                    Category = FactCategory.What,
                    TextId = "VividWorld_Fact_Test_" + i,
                    Text = "fact text " + i,
                    Fragility = i + 1
                });
            }

            return new WorldEvent
            {
                EventId = eventId,
                Type = type,
                Day = day,
                Origin = origin,
                State = new RumorState { Leaked = isLeaked },
                DramaWeight = 3,
                Facts = facts
            };
        }

        #region 1. 核心邏輯測試 (1-16)

        // 1. 只含玩家知道的事件（放三則別人知道、玩家不知道的，斷言不出現）
        [Fact]
        public void ForPlayer_OnlyReturnsEventsKnownByPlayer()
        {
            var (engine, knownBy, cfg) = CreateTestContext();
            var store = new Dictionary<string, WorldEvent>();

            var evtKnown = CreateEvent("evt_known", day: 10.0);
            evtKnown.KnownBy.Add(new KnownByEntry { HeroId = PlayerHeroId, Hop = 1, LearnedDay = 10.0 });
            store["evt_known"] = evtKnown;
            knownBy.NoteKnower(PlayerHeroId, "evt_known", 10.0);

            for (int i = 1; i <= 3; i++)
            {
                string otherId = "evt_other_" + i;
                var evtOther = CreateEvent(otherId, day: 10.0);
                evtOther.KnownBy.Add(new KnownByEntry { HeroId = "hero_other", Hop = 1, LearnedDay = 10.0 });
                store[otherId] = evtOther;
                knownBy.NoteKnower("hero_other", otherId, 10.0);
            }

            var provider = new ChronicleProvider(engine, knownBy, id => store.TryGetValue(id, out var e) ? e : null, PlayerHeroId, cfg);
            var result = provider.ForPlayer(50, 20.0, out var stats);

            Assert.Single(result);
            Assert.Equal("evt_known", result[0].EventId);
            Assert.Equal(1, stats.TotalKnown);
            Assert.Equal(1, stats.Returned);
        }

        // 2. KnownFactIds 非 null => Body 逐片等於那個集合，不等於 hop 0 的完整集合
        [Fact]
        public void ForPlayer_WithKnownFactIds_BodyMatchesExactFactIds_NotHopZero()
        {
            var (engine, knownBy, cfg) = CreateTestContext();
            var store = new Dictionary<string, WorldEvent>();

            var evt = CreateEvent("evt_facts", factCount: 5);
            evt.KnownBy.Add(new KnownByEntry
            {
                HeroId = PlayerHeroId,
                Hop = 2,
                LearnedDay = 10.0,
                KnownFactIds = new List<string> { "fact_0", "fact_3" }
            });
            store["evt_facts"] = evt;
            knownBy.NoteKnower(PlayerHeroId, "evt_facts", 10.0);

            var provider = new ChronicleProvider(engine, knownBy, id => store[id], PlayerHeroId, cfg);
            var result = provider.ForPlayer(50, 20.0, out _);

            Assert.Single(result);
            var entry = result[0];
            Assert.Equal(2, entry.Body.Parts.Count);
            Assert.Contains(entry.Body.Parts, p => p.TextId == "VividWorld_Fact_Test_0");
            Assert.Contains(entry.Body.Parts, p => p.TextId == "VividWorld_Fact_Test_3");
            Assert.DoesNotContain(entry.Body.Parts, p => p.TextId == "VividWorld_Fact_Test_1");
        }

        // 3. KnownFactIds 為 null => 退回 FactsAtHop，且用的是玩家那筆的 Hop 與 SourceHeroId
        [Fact]
        public void ForPlayer_WithNullKnownFactIds_FallsBackToFactsAtHopWithPlayerHop()
        {
            var (engine, knownBy, cfg) = CreateTestContext();
            var store = new Dictionary<string, WorldEvent>();

            var evt = CreateEvent("evt_null_facts", factCount: 5);
            evt.KnownBy.Add(new KnownByEntry
            {
                HeroId = PlayerHeroId,
                Hop = 1,
                SourceHeroId = "hero_source",
                LearnedDay = 10.0,
                KnownFactIds = null
            });
            store["evt_null_facts"] = evt;
            knownBy.NoteKnower(PlayerHeroId, "evt_null_facts", 10.0);

            var provider = new ChronicleProvider(engine, knownBy, id => store[id], PlayerHeroId, cfg);
            var result = provider.ForPlayer(50, 20.0, out _);

            Assert.Single(result);
            var expectedFacts = engine.FactsAtHop(evt, 1, "hero_source");
            Assert.Equal(expectedFacts.Count, result[0].Body.Parts.Count);
            Assert.Equal("hero_source", result[0].SourceHeroId);
            Assert.Equal(1, result[0].PlayerHop);
        }

        // 4. 玩家在 hop 3、事件有 5 片 => Body.Parts 少於 5（失真確實帶到紀事裡）
        [Fact]
        public void ForPlayer_DistortionCarriedToChronicle_WhenPlayerAtHigherHop()
        {
            var (engine, knownBy, cfg) = CreateTestContext();
            var store = new Dictionary<string, WorldEvent>();

            var evt = CreateEvent("evt_distorted", factCount: 5);
            evt.KnownBy.Add(new KnownByEntry
            {
                HeroId = PlayerHeroId,
                Hop = 3,
                SourceHeroId = "hero_teller",
                LearnedDay = 10.0
            });
            store["evt_distorted"] = evt;
            knownBy.NoteKnower(PlayerHeroId, "evt_distorted", 10.0);

            var provider = new ChronicleProvider(engine, knownBy, id => store[id], PlayerHeroId, cfg);
            var result = provider.ForPlayer(50, 20.0, out _);

            Assert.Single(result);
            Assert.True(result[0].Body.Parts.Count < 5, "Distortion should reduce fact count at hop 3");
        }

        // 5. 未洩漏的秘密不出現（即使玩家在 KnownBy 裡）
        [Fact]
        public void ForPlayer_UnleakedSecretDoesNotAppear_EvenIfPlayerInKnownBy()
        {
            var (engine, knownBy, cfg) = CreateTestContext();
            var store = new Dictionary<string, WorldEvent>();

            var secretEvt = CreateEvent("evt_secret", origin: EventOrigin.Secret, isLeaked: false);
            secretEvt.KnownBy.Add(new KnownByEntry { HeroId = PlayerHeroId, Hop = 0, LearnedDay = 10.0 });
            store["evt_secret"] = secretEvt;
            knownBy.NoteKnower(PlayerHeroId, "evt_secret", 10.0);

            var provider = new ChronicleProvider(engine, knownBy, id => store[id], PlayerHeroId, cfg);
            var result = provider.ForPlayer(50, 20.0, out var stats);

            Assert.Empty(result);
            Assert.Equal(1, stats.TotalKnown);
            Assert.Equal(0, stats.Returned);
            // 被丟掉的要數得出來，否則 log 那一行加起來對不上總數
            Assert.Equal(1, stats.SkippedSecret);
            Assert.Equal(stats.TotalKnown,
                stats.Returned + stats.SkippedNoShard + stats.SkippedNoEntry + stats.SkippedSecret);
        }

        // 6. 洩漏之後同一則會出現
        [Fact]
        public void ForPlayer_LeakedSecretAppears()
        {
            var (engine, knownBy, cfg) = CreateTestContext();
            var store = new Dictionary<string, WorldEvent>();

            var secretEvt = CreateEvent("evt_secret_leaked", origin: EventOrigin.Secret, isLeaked: true);
            secretEvt.KnownBy.Add(new KnownByEntry { HeroId = PlayerHeroId, Hop = 1, LearnedDay = 10.0 });
            store["evt_secret_leaked"] = secretEvt;
            knownBy.NoteKnower(PlayerHeroId, "evt_secret_leaked", 10.0);

            var provider = new ChronicleProvider(engine, knownBy, id => store[id], PlayerHeroId, cfg);
            var result = provider.ForPlayer(50, 20.0, out var stats);

            Assert.Single(result);
            Assert.Equal("evt_secret_leaked", result[0].EventId);
            Assert.Equal(1, stats.Returned);
        }

        // 7. 日期比今天晚的事件不出現，且 stats.HiddenFuture 數得對
        [Fact]
        public void ForPlayer_HidesFutureEvents_AndCountsHiddenFuture()
        {
            var (engine, knownBy, cfg) = CreateTestContext();
            var store = new Dictionary<string, WorldEvent>();

            var evtPast = CreateEvent("evt_past", day: 50.0);
            evtPast.KnownBy.Add(new KnownByEntry { HeroId = PlayerHeroId, Hop = 1, LearnedDay = 50.0 });
            store["evt_past"] = evtPast;
            knownBy.NoteKnower(PlayerHeroId, "evt_past", 50.0);

            var evtFuture = CreateEvent("evt_future", day: 150.0);
            evtFuture.KnownBy.Add(new KnownByEntry { HeroId = PlayerHeroId, Hop = 1, LearnedDay = 150.0 });
            store["evt_future"] = evtFuture;
            knownBy.NoteKnower(PlayerHeroId, "evt_future", 150.0);

            var provider = new ChronicleProvider(engine, knownBy, id => store[id], PlayerHeroId, cfg);
            var result = provider.ForPlayer(50, 100.0, out var stats);

            Assert.Single(result);
            Assert.Equal("evt_past", result[0].EventId);
            Assert.Equal(1, stats.HiddenFuture);
            Assert.Equal(1, stats.TotalKnown);
        }

        // 8. 排序：新到舊；同一天用 EventId 當第二鍵，同一份資料跑兩次順序相同
        [Fact]
        public void ForPlayer_SortsDescendingByDay_ThenOrdinalByEventId()
        {
            var (engine, knownBy, cfg) = CreateTestContext();
            var store = new Dictionary<string, WorldEvent>();

            void AddEvt(string id, double day)
            {
                var e = CreateEvent(id, day: day);
                e.KnownBy.Add(new KnownByEntry { HeroId = PlayerHeroId, Hop = 1, LearnedDay = day });
                store[id] = e;
                knownBy.NoteKnower(PlayerHeroId, id, day);
            }

            AddEvt("evt_day10", 10.0);
            AddEvt("evt_day30_b", 30.0);
            AddEvt("evt_day30_a", 30.0);
            AddEvt("evt_day05", 5.0);

            var provider = new ChronicleProvider(engine, knownBy, id => store[id], PlayerHeroId, cfg);
            var run1 = provider.ForPlayer(50, 100.0, out _);
            var run2 = provider.ForPlayer(50, 100.0, out _);

            Assert.Equal(4, run1.Count);
            Assert.Equal("evt_day30_a", run1[0].EventId);
            Assert.Equal("evt_day30_b", run1[1].EventId);
            Assert.Equal("evt_day10", run1[2].EventId);
            Assert.Equal("evt_day05", run1[3].EventId);

            // Deterministic stability
            Assert.Equal(run1.Select(e => e.EventId), run2.Select(e => e.EventId));
        }

        // 9. maxEntries 夾取；maxEntries = 0 當 1；stats.Returned 對得上
        [Fact]
        public void ForPlayer_CapsAtMaxEntries_ClampsZeroToOne_ReportsReturnedCount()
        {
            var (engine, knownBy, cfg) = CreateTestContext();
            var store = new Dictionary<string, WorldEvent>();

            for (int i = 0; i < 5; i++)
            {
                string id = "evt_" + i;
                var e = CreateEvent(id, day: 10.0 + i);
                e.KnownBy.Add(new KnownByEntry { HeroId = PlayerHeroId, Hop = 1, LearnedDay = e.Day });
                store[id] = e;
                knownBy.NoteKnower(PlayerHeroId, id, e.Day);
            }

            var provider = new ChronicleProvider(engine, knownBy, id => store[id], PlayerHeroId, cfg);

            var resultCap3 = provider.ForPlayer(3, 100.0, out var stats3);
            Assert.Equal(3, resultCap3.Count);
            Assert.Equal(3, stats3.Returned);
            Assert.Equal(5, stats3.TotalKnown);

            var resultCap0 = provider.ForPlayer(0, 100.0, out var stats0);
            Assert.Single(resultCap0);
            Assert.Equal(1, stats0.Returned);
        }

        // 10. load 回 null => 那一則跳過、SkippedNoShard ＋1、其餘照常回傳
        [Fact]
        public void ForPlayer_WhenShardLoadReturnsNull_SkipsEvent_IncrementsSkippedNoShard()
        {
            var (engine, knownBy, cfg) = CreateTestContext();
            var store = new Dictionary<string, WorldEvent>();

            var e1 = CreateEvent("evt_1", day: 10.0);
            e1.KnownBy.Add(new KnownByEntry { HeroId = PlayerHeroId, Hop = 1, LearnedDay = 10.0 });
            store["evt_1"] = e1;
            knownBy.NoteKnower(PlayerHeroId, "evt_1", 10.0);

            // evt_missing is in index, but load returns null
            knownBy.NoteKnower(PlayerHeroId, "evt_missing", 15.0);

            var e2 = CreateEvent("evt_2", day: 20.0);
            e2.KnownBy.Add(new KnownByEntry { HeroId = PlayerHeroId, Hop = 1, LearnedDay = 20.0 });
            store["evt_2"] = e2;
            knownBy.NoteKnower(PlayerHeroId, "evt_2", 20.0);

            var provider = new ChronicleProvider(engine, knownBy, id => store.TryGetValue(id, out var e) ? e : null, PlayerHeroId, cfg);
            var result = provider.ForPlayer(50, 100.0, out var stats);

            Assert.Equal(2, result.Count);
            Assert.Equal(1, stats.SkippedNoShard);
            Assert.Equal(3, stats.TotalKnown);
        }

        // 11. 分片裡沒有玩家那筆 => SkippedNoEntry ＋1
        [Fact]
        public void ForPlayer_WhenShardMissingPlayerEntry_SkipsEvent_IncrementsSkippedNoEntry()
        {
            var (engine, knownBy, cfg) = CreateTestContext();
            var store = new Dictionary<string, WorldEvent>();

            var e1 = CreateEvent("evt_no_player_entry", day: 10.0);
            // Notice: player is NOT in evt_no_player_entry.KnownBy
            store["evt_no_player_entry"] = e1;
            knownBy.NoteKnower(PlayerHeroId, "evt_no_player_entry", 10.0);

            var provider = new ChronicleProvider(engine, knownBy, id => store[id], PlayerHeroId, cfg);
            var result = provider.ForPlayer(50, 100.0, out var stats);

            Assert.Empty(result);
            Assert.Equal(1, stats.SkippedNoEntry);
            Assert.Equal(1, stats.TotalKnown);
        }

        // 12. Body.Parts 為空的事件照樣回傳一列
        [Fact]
        public void ForPlayer_EventWithEmptyParts_StillReturned()
        {
            var (engine, knownBy, cfg) = CreateTestContext();
            var store = new Dictionary<string, WorldEvent>();

            var evt = CreateEvent("evt_empty_parts", factCount: 0);
            evt.KnownBy.Add(new KnownByEntry { HeroId = PlayerHeroId, Hop = 1, LearnedDay = 10.0 });
            store["evt_empty_parts"] = evt;
            knownBy.NoteKnower(PlayerHeroId, "evt_empty_parts", 10.0);

            var provider = new ChronicleProvider(engine, knownBy, id => store[id], PlayerHeroId, cfg);
            var result = provider.ForPlayer(50, 100.0, out var stats);

            Assert.Single(result);
            Assert.Empty(result[0].Body.Parts);
            Assert.Equal("evt_empty_parts", result[0].EventId);
            Assert.Equal(1, stats.Returned);
        }

        // 13. HeadlineTextId 一律等於 "VividWorld_EventType_" + type
        [Fact]
        public void ForPlayer_HeadlineTextId_AlwaysPrefixedWithEventType()
        {
            var (engine, knownBy, cfg) = CreateTestContext();
            var store = new Dictionary<string, WorldEvent>();

            var evt = CreateEvent("evt_headline", type: "custom_type", day: 10.0);
            evt.KnownBy.Add(new KnownByEntry { HeroId = PlayerHeroId, Hop = 1, LearnedDay = 10.0 });
            store["evt_headline"] = evt;
            knownBy.NoteKnower(PlayerHeroId, "evt_headline", 10.0);

            var provider = new ChronicleProvider(engine, knownBy, id => store[id], PlayerHeroId, cfg);
            var result = provider.ForPlayer(50, 100.0, out _);

            Assert.Single(result);
            Assert.Equal("VividWorld_EventType_custom_type", result[0].HeadlineTextId);
        }

        // 14. 模板沒宣告 headline => HeadlineFallback 等於型別字串，不得丟例外
        [Fact]
        public void ForPlayer_WhenTemplateHasNoHeadline_HeadlineFallbackEqualsType()
        {
            var (engine, knownBy, cfg) = CreateTestContext();
            var store = new Dictionary<string, WorldEvent>();

            var evt = CreateEvent("evt_no_tmpl", type: "unregistered_type", day: 10.0);
            evt.KnownBy.Add(new KnownByEntry { HeroId = PlayerHeroId, Hop = 1, LearnedDay = 10.0 });
            store["evt_no_tmpl"] = evt;
            knownBy.NoteKnower(PlayerHeroId, "evt_no_tmpl", 10.0);

            // templateByType returns null or template with null headline
            var provider = new ChronicleProvider(
                engine,
                knownBy,
                id => store[id],
                type => new EventTemplate { Type = type, Headline = null },
                PlayerHeroId,
                cfg);

            var result = provider.ForPlayer(50, 100.0, out _);

            Assert.Single(result);
            Assert.Equal("unregistered_type", result[0].HeadlineFallback);
        }

        // 15. DayLabel／SourceHeroName 由 Core 產出時一律是空字串／null
        [Fact]
        public void ForPlayer_DayLabelAndSourceHeroName_AreEmptyAndNullFromCore()
        {
            var (engine, knownBy, cfg) = CreateTestContext();
            var store = new Dictionary<string, WorldEvent>();

            var evt = CreateEvent("evt_core_dto", day: 10.0);
            evt.KnownBy.Add(new KnownByEntry { HeroId = PlayerHeroId, Hop = 1, SourceHeroId = "hero_source", LearnedDay = 10.0 });
            store["evt_core_dto"] = evt;
            knownBy.NoteKnower(PlayerHeroId, "evt_core_dto", 10.0);

            var provider = new ChronicleProvider(engine, knownBy, id => store[id], PlayerHeroId, cfg);
            var result = provider.ForPlayer(50, 100.0, out _);

            Assert.Single(result);
            Assert.Equal(string.Empty, result[0].DayLabel);
            Assert.Null(result[0].SourceHeroName);
        }

        // 16. log 版型逐字（有資料、沒資料兩種）
        [Fact]
        public void ChronicleLogFormatter_ProducesExactFormats_ForDataAndEmpty()
        {
            var statsData = new ChronicleStats
            {
                TotalKnown = 41,
                Returned = 37,
                HiddenFuture = 0,
                SkippedNoShard = 3,
                SkippedNoEntry = 1,
                SkippedSecret = 0
            };

            string lineData = ChronicleLogFormatter.FormatOpen("main_hero", 91143.1, statsData, 50);
            Assert.Equal(
                "Chronicle opened for main_hero on day 91143.1: 37 shown of 41 known (max 50), hidden future 0, skipped 4 (no shard 3, no player entry 1, unleaked secret 0)",
                lineData);

            // 秘密也要出現在明細裡，而且總數要加得起來
            var statsSecret = new ChronicleStats
            {
                TotalKnown = 10,
                Returned = 7,
                HiddenFuture = 0,
                SkippedNoShard = 1,
                SkippedNoEntry = 0,
                SkippedSecret = 2
            };

            string lineSecret = ChronicleLogFormatter.FormatOpen("main_hero", 91143.1, statsSecret, 50);
            Assert.Equal(
                "Chronicle opened for main_hero on day 91143.1: 7 shown of 10 known (max 50), hidden future 0, skipped 3 (no shard 1, no player entry 0, unleaked secret 2)",
                lineSecret);

            var statsEmpty = new ChronicleStats
            {
                TotalKnown = 0,
                Returned = 0,
                HiddenFuture = 0,
                SkippedNoShard = 0,
                SkippedNoEntry = 0
            };

            string lineEmpty = ChronicleLogFormatter.FormatOpen("main_hero", 91143.1, statsEmpty, 50);
            Assert.Equal(
                "Chronicle opened for main_hero on day 91143.1: nothing known yet (0 known)",
                lineEmpty);
        }

        #endregion

        #region 2. 出貨資料契約測試 (17-22)

        private static string FindRepoRoot()
        {
            string? dir = AppDomain.CurrentDomain.BaseDirectory;
            while (dir != null)
            {
                if (Directory.Exists(Path.Combine(dir, "module", "ModuleData")))
                    return dir;
                dir = Directory.GetParent(dir)?.FullName;
            }
            throw new DirectoryNotFoundException("Could not find repository root containing module/ModuleData");
        }

        // 17. 兩份目錄的 31 個模板全部有 headline
        [Fact]
        public void Catalog_All31Templates_HaveHeadline()
        {
            string repoRoot = FindRepoRoot();
            string eventsPath = Path.Combine(repoRoot, "module", "ModuleData", "vividworld_events.json");
            string sitEventsPath = Path.Combine(repoRoot, "module", "ModuleData", "vividworld_situation_events.json");

            var eventsArr = JArray.Parse(File.ReadAllText(eventsPath));
            var sitEventsArr = JArray.Parse(File.ReadAllText(sitEventsPath));

            Assert.Equal(9, eventsArr.Count);
            Assert.Equal(22, sitEventsArr.Count);

            var all = eventsArr.Concat(sitEventsArr).ToList();
            Assert.Equal(31, all.Count);

            foreach (var token in all)
            {
                string? type = (string?)token["type"];
                Assert.False(string.IsNullOrEmpty(type), "Template type must not be null/empty");

                string? headline = (string?)token["headline"];
                Assert.False(string.IsNullOrEmpty(headline), $"Template '{type}' must have non-empty 'headline'");
            }
        }

        // 18. 每個型別在英文表與繁中表都有 VividWorld_EventType_<type>
        [Fact]
        public void Localization_All31EventTypes_ExistInBothEnglishAndCNt()
        {
            string repoRoot = FindRepoRoot();
            string enPath = Path.Combine(repoRoot, "module", "ModuleData", "Languages", "std_module_strings_xml.xml");
            string cntPath = Path.Combine(repoRoot, "module", "ModuleData", "Languages", "CNt", "std_module_strings_xml.xml");

            var enDoc = XDocument.Load(enPath);
            var cntDoc = XDocument.Load(cntPath);

            var enKeys = enDoc.Descendants("string").Select(e => (string?)e.Attribute("id")).Where(k => k != null).ToHashSet();
            var cntKeys = cntDoc.Descendants("string").Select(e => (string?)e.Attribute("id")).Where(k => k != null).ToHashSet();

            string eventsPath = Path.Combine(repoRoot, "module", "ModuleData", "vividworld_events.json");
            string sitEventsPath = Path.Combine(repoRoot, "module", "ModuleData", "vividworld_situation_events.json");

            var eventsArr = JArray.Parse(File.ReadAllText(eventsPath));
            var sitEventsArr = JArray.Parse(File.ReadAllText(sitEventsPath));

            var types = eventsArr.Concat(sitEventsArr).Select(t => (string)t["type"]!).ToList();
            Assert.Equal(31, types.Count);

            foreach (var type in types)
            {
                string expectedKey = "VividWorld_EventType_" + type;
                Assert.True(enKeys.Contains(expectedKey), $"Key '{expectedKey}' missing from English table");
                Assert.True(cntKeys.Contains(expectedKey), $"Key '{expectedKey}' missing from CNt table");
            }
        }

        // 19. §7 的 6 條介面字串兩份表都有
        [Fact]
        public void Localization_All6ChronicleInterfaceStrings_ExistInBothTables()
        {
            string repoRoot = FindRepoRoot();
            string enPath = Path.Combine(repoRoot, "module", "ModuleData", "Languages", "std_module_strings_xml.xml");
            string cntPath = Path.Combine(repoRoot, "module", "ModuleData", "Languages", "CNt", "std_module_strings_xml.xml");

            var enDoc = XDocument.Load(enPath);
            var cntDoc = XDocument.Load(cntPath);

            var enDict = enDoc.Descendants("string").ToDictionary(e => (string)e.Attribute("id")!, e => (string)e.Attribute("text")!);
            var cntDict = cntDoc.Descendants("string").ToDictionary(e => (string)e.Attribute("id")!, e => (string)e.Attribute("text")!);

            var interfaceKeys = new[]
            {
                "VividWorld_Chronicle_Title",
                "VividWorld_Chronicle_Empty",
                "VividWorld_Chronicle_Close",
                "VividWorld_Chronicle_Hop",
                "VividWorld_Chronicle_HopZero",
                "VividWorld_Chronicle_SourceUnknown"
            };

            foreach (var key in interfaceKeys)
            {
                Assert.True(enDict.ContainsKey(key), $"Interface key '{key}' missing from English table");
                Assert.True(cntDict.ContainsKey(key), $"Interface key '{key}' missing from CNt table");
                Assert.False(string.IsNullOrWhiteSpace(enDict[key]), $"English text for '{key}' is empty");
                Assert.False(string.IsNullOrWhiteSpace(cntDict[key]), $"CNt text for '{key}' is empty");
            }

            // Verify specific values
            Assert.Equal("What you have heard", enDict["VividWorld_Chronicle_Title"]);
            Assert.Equal("你聽過的事", cntDict["VividWorld_Chronicle_Title"]);

            Assert.Equal("You have not been told anything yet.", enDict["VividWorld_Chronicle_Empty"]);
            Assert.Equal("還沒有人跟你說過什麼。", cntDict["VividWorld_Chronicle_Empty"]);

            Assert.Equal("Close", enDict["VividWorld_Chronicle_Close"]);
            Assert.Equal("關閉", cntDict["VividWorld_Chronicle_Close"]);

            Assert.Equal("{HOPS} tellings removed", enDict["VividWorld_Chronicle_Hop"]);
            Assert.Equal("傳了 {HOPS} 手", cntDict["VividWorld_Chronicle_Hop"]);

            Assert.Equal("you were there", enDict["VividWorld_Chronicle_HopZero"]);
            Assert.Equal("你當時在場", cntDict["VividWorld_Chronicle_HopZero"]);

            Assert.Equal("from someone", enDict["VividWorld_Chronicle_SourceUnknown"]);
            Assert.Equal("不知道是誰說的", cntDict["VividWorld_Chronicle_SourceUnknown"]);
        }

        // 20. 英文表標題文字與 JSON headline 逐字一致
        [Fact]
        public void Catalog_EnglishTableHeadlines_MatchJsonHeadlinesVerbatim()
        {
            string repoRoot = FindRepoRoot();
            string enPath = Path.Combine(repoRoot, "module", "ModuleData", "Languages", "std_module_strings_xml.xml");
            var enDoc = XDocument.Load(enPath);
            var enDict = enDoc.Descendants("string").ToDictionary(e => (string)e.Attribute("id")!, e => (string)e.Attribute("text")!);

            string eventsPath = Path.Combine(repoRoot, "module", "ModuleData", "vividworld_events.json");
            string sitEventsPath = Path.Combine(repoRoot, "module", "ModuleData", "vividworld_situation_events.json");

            var all = JArray.Parse(File.ReadAllText(eventsPath)).Concat(JArray.Parse(File.ReadAllText(sitEventsPath)));

            foreach (var token in all)
            {
                string type = (string)token["type"]!;
                string headline = (string)token["headline"]!;
                string key = "VividWorld_EventType_" + type;

                Assert.True(enDict.TryGetValue(key, out var enText), $"Missing key '{key}' in English strings");
                Assert.Equal(headline, enText);
            }
        }

        // 21. 繁中表的 22 個情境事件 headline 與 M8 規格表逐字一致
        [Fact]
        public void Localization_CNtTableSituationHeadlines_MatchM8SpecVerbatim()
        {
            string repoRoot = FindRepoRoot();
            string cntPath = Path.Combine(repoRoot, "module", "ModuleData", "Languages", "CNt", "std_module_strings_xml.xml");
            var cntDoc = XDocument.Load(cntPath);
            var cntDict = cntDoc.Descendants("string").ToDictionary(e => (string)e.Attribute("id")!, e => (string)e.Attribute("text")!);

            var expected = new Dictionary<string, string>
            {
                { "seat_dispute_demanded", "有人當眾爭座位" },
                { "seat_dispute_endured", "有人忍下了席間的難堪" },
                { "seat_dispute_yielded", "有人讓出了座位" },
                { "seat_dispute_walked_out", "有人拂袖離席" },
                { "advice_given_freely", "老將傾囊相授" },
                { "advice_brushed_off", "討教被打發掉" },
                { "advice_mocked", "討教被當眾譏笑" },
                { "tavern_boast_told", "酒桌上的吹噓" },
                { "tavern_confidence", "酒後說漏的心事" },
                { "tavern_sour_words", "酒後的惡言" },
                { "tavern_good_word", "酒桌上替人說的公道話" },
                { "victory_credit_claimed", "有人把戰功攬到自己身上" },
                { "victory_credit_deferred", "有人把戰功讓了出去" },
                { "victory_credit_belittled", "戰功被貶低" },
                { "victory_credit_judged", "戰功之爭交由主人裁斷" },
                { "brawl_man_handed_over", "鬧事的隨從被交了出去" },
                { "brawl_shielded_own", "有人護短" },
                { "brawl_counter_accused", "鬧事反咬一口" },
                { "brawl_hushed_up", "衝突被壓了下來" },
                { "wager_struck", "打了一場狩獵賭局" },
                { "wager_refused", "狩獵賭局被回絕" },
                { "wager_secret_stake", "狩獵賭局有暗盤" },
                { "hero_released", "俘虜獲釋" },
                { "hero_escaped_captivity", "俘虜脫逃" }
            };

            foreach (var kvp in expected)
            {
                string key = "VividWorld_EventType_" + kvp.Key;
                Assert.True(cntDict.TryGetValue(key, out var text), $"Key '{key}' missing from CNt");
                Assert.Equal(kvp.Value, text);
            }
        }

        // 22. 英文後備文字完全不含任何中文字元（鐵則 06）
        [Fact]
        public void Localization_EnglishTable_ContainsNoChineseCharacters()
        {
            string repoRoot = FindRepoRoot();
            string enPath = Path.Combine(repoRoot, "module", "ModuleData", "Languages", "std_module_strings_xml.xml");
            var enDoc = XDocument.Load(enPath);

            foreach (var elem in enDoc.Descendants("string"))
            {
                string text = (string)elem.Attribute("text")!;
                string id = (string)elem.Attribute("id")!;
                bool hasChinese = text.Any(c => c >= 0x4E00 && c <= 0x9FFF);
                Assert.False(hasChinese, $"English string '{id}' contains Chinese characters: \"{text}\"");
            }
        }

        #endregion

        #region 3. 額外輔助與契約邊界測試 (23-32)

        // 23. ChronicleLogFormatter 傳入 null stats 時安全回退
        [Fact]
        public void ChronicleLogFormatter_HandlesNullStats_Gracefully()
        {
            string line = ChronicleLogFormatter.FormatOpen("main_hero", 10.0, null!, 50);
            Assert.Contains("nothing known yet (0 known)", line);
        }

        // 24. ChronicleProvider 簡便建構子（5個參數）正確運作
        [Fact]
        public void ChronicleProvider_ConvenienceConstructor_Succeeds()
        {
            var (engine, knownBy, cfg) = CreateTestContext();
            var store = new Dictionary<string, WorldEvent>();

            var evt = CreateEvent("evt_5arg", day: 10.0);
            evt.KnownBy.Add(new KnownByEntry { HeroId = PlayerHeroId, Hop = 1, LearnedDay = 10.0 });
            store["evt_5arg"] = evt;
            knownBy.NoteKnower(PlayerHeroId, "evt_5arg", 10.0);

            var provider = new ChronicleProvider(engine, knownBy, id => store[id], PlayerHeroId, cfg);
            var result = provider.ForPlayer(50, 20.0, out _);

            Assert.Single(result);
            Assert.Equal("evt_5arg", result[0].EventId);
            Assert.Equal("duel", result[0].HeadlineFallback);
        }

        // 25. 引擎不在時**不得**退回事件的完整碎片集合——那是 hop 0 的全知版本
        [Fact]
        public void ForPlayer_WithoutEngine_ReturnsNoFacts_NotTheWholeEvent()
        {
            var (_, knownBy, cfg) = CreateTestContext();
            var store = new Dictionary<string, WorldEvent>();

            var evt = CreateEvent("evt_no_engine", day: 10.0);
            // KnownFactIds 為 null ⇒ 走「依手數重算」那條路，而那條路需要引擎
            evt.KnownBy.Add(new KnownByEntry { HeroId = PlayerHeroId, Hop = 3, LearnedDay = 10.0, KnownFactIds = null });
            store["evt_no_engine"] = evt;
            knownBy.NoteKnower(PlayerHeroId, "evt_no_engine", 10.0);

            var provider = new ChronicleProvider(null!, knownBy, id => store[id], PlayerHeroId, cfg);
            var result = provider.ForPlayer(50, 20.0, out _);

            Assert.Single(result);
            Assert.Equal("evt_no_engine", result[0].EventId);
            // 只剩標題，不准把整包碎片交出去
            Assert.Empty(result[0].Body.Parts);
            Assert.NotEmpty(evt.Facts);
        }

        // 26. ChronicleEntry 預設值符合契約
        [Fact]
        public void ChronicleEntry_DefaultsMatchContract()
        {
            var entry = new ChronicleEntry();
            Assert.Equal(string.Empty, entry.EventId);
            Assert.Equal(string.Empty, entry.EventType);
            Assert.Equal(0.0, entry.Day);
            Assert.Equal(0, entry.PlayerHop);
            Assert.Null(entry.SourceHeroId);
            Assert.Null(entry.LinkedEventId);
            Assert.Equal(string.Empty, entry.HeadlineTextId);
            Assert.Equal(string.Empty, entry.HeadlineFallback);
            Assert.NotNull(entry.Body);
            Assert.Empty(entry.Body.Parts);
            Assert.Equal(string.Empty, entry.DayLabel);
            Assert.Null(entry.SourceHeroName);
        }

        // 27. ChronicleStats 初始為零
        [Fact]
        public void ChronicleStats_InitializesWithZeros()
        {
            var stats = new ChronicleStats();
            Assert.Equal(0, stats.TotalKnown);
            Assert.Equal(0, stats.HiddenFuture);
            Assert.Equal(0, stats.SkippedNoShard);
            Assert.Equal(0, stats.SkippedNoEntry);
            Assert.Equal(0, stats.Returned);
        }

        // 28. KnownByEntry 傳播來源欄位 SourceHeroId 與 LinkedEventId 正確傳遞至 ChronicleEntry
        [Fact]
        public void ForPlayer_SourceHeroIdAndLinkedEventId_PropagatedToChronicleEntry()
        {
            var (engine, knownBy, cfg) = CreateTestContext();
            var store = new Dictionary<string, WorldEvent>();

            var evt = CreateEvent("evt_linked", day: 10.0);
            evt.LinkedEventId = "evt_origin";
            evt.KnownBy.Add(new KnownByEntry
            {
                HeroId = PlayerHeroId,
                Hop = 2,
                SourceHeroId = "hero_messenger",
                LearnedDay = 10.0
            });
            store["evt_linked"] = evt;
            knownBy.NoteKnower(PlayerHeroId, "evt_linked", 10.0);

            var provider = new ChronicleProvider(engine, knownBy, id => store[id], PlayerHeroId, cfg);
            var result = provider.ForPlayer(50, 20.0, out _);

            Assert.Single(result);
            Assert.Equal("hero_messenger", result[0].SourceHeroId);
            Assert.Equal("evt_origin", result[0].LinkedEventId);
            Assert.Equal(2, result[0].PlayerHop);
        }

        // 29. 玩家 Hop 為 0 時保留 Hop 0
        [Fact]
        public void ForPlayer_PlayerHopZero_RetainsHopZero()
        {
            var (engine, knownBy, cfg) = CreateTestContext();
            var store = new Dictionary<string, WorldEvent>();

            var evt = CreateEvent("evt_hop0", day: 10.0);
            evt.KnownBy.Add(new KnownByEntry
            {
                HeroId = PlayerHeroId,
                Hop = 0,
                SourceHeroId = null,
                LearnedDay = 10.0
            });
            store["evt_hop0"] = evt;
            knownBy.NoteKnower(PlayerHeroId, "evt_hop0", 10.0);

            var provider = new ChronicleProvider(engine, knownBy, id => store[id], PlayerHeroId, cfg);
            var result = provider.ForPlayer(50, 20.0, out _);

            Assert.Single(result);
            Assert.Equal(0, result[0].PlayerHop);
            Assert.Null(result[0].SourceHeroId);
        }

        // 30. 模板宣告 headline 時 HeadlineFallback 優先採用模板之 headline
        [Fact]
        public void ForPlayer_WhenTemplateHasHeadline_HeadlineFallbackPrefersTemplateHeadline()
        {
            var (engine, knownBy, cfg) = CreateTestContext();
            var store = new Dictionary<string, WorldEvent>();

            var evt = CreateEvent("evt_tmpl_headline", type: "wager_struck", day: 10.0);
            evt.KnownBy.Add(new KnownByEntry { HeroId = PlayerHeroId, Hop = 1, LearnedDay = 10.0 });
            store["evt_tmpl_headline"] = evt;
            knownBy.NoteKnower(PlayerHeroId, "evt_tmpl_headline", 10.0);

            var provider = new ChronicleProvider(
                engine,
                knownBy,
                id => store[id],
                type => new EventTemplate { Type = type, Headline = "A hunting wager was struck" },
                PlayerHeroId,
                cfg);

            var result = provider.ForPlayer(50, 20.0, out _);

            Assert.Single(result);
            Assert.Equal("A hunting wager was struck", result[0].HeadlineFallback);
            Assert.Equal("VividWorld_EventType_wager_struck", result[0].HeadlineTextId);
        }

        // 31. 空的已知清單安全回傳空集合
        [Fact]
        public void ForPlayer_WhenNoEventsKnown_ReturnsEmptyList()
        {
            var (engine, knownBy, cfg) = CreateTestContext();
            var provider = new ChronicleProvider(engine, knownBy, id => null, PlayerHeroId, cfg);

            var result = provider.ForPlayer(50, 20.0, out var stats);

            Assert.Empty(result);
            Assert.Equal(0, stats.TotalKnown);
            Assert.Equal(0, stats.Returned);
        }

        // 32. 跨多個分片與型別時排序與過濾依然穩健
        [Fact]
        public void ForPlayer_HandlesMixedPublicSecretAndFutureEvents()
        {
            var (engine, knownBy, cfg) = CreateTestContext();
            var store = new Dictionary<string, WorldEvent>();

            // 1. Valid public event
            var e1 = CreateEvent("evt_pub_valid", type: "duel", day: 20.0);
            e1.KnownBy.Add(new KnownByEntry { HeroId = PlayerHeroId, Hop = 1, LearnedDay = 20.0 });
            store["evt_pub_valid"] = e1;
            knownBy.NoteKnower(PlayerHeroId, "evt_pub_valid", 20.0);

            // 2. Secret unleaked event (invisible)
            var e2 = CreateEvent("evt_sec_unleaked", type: "murder", day: 25.0, origin: EventOrigin.Secret, isLeaked: false);
            e2.KnownBy.Add(new KnownByEntry { HeroId = PlayerHeroId, Hop = 0, LearnedDay = 25.0 });
            store["evt_sec_unleaked"] = e2;
            knownBy.NoteKnower(PlayerHeroId, "evt_sec_unleaked", 25.0);

            // 3. Secret leaked event (visible)
            var e3 = CreateEvent("evt_sec_leaked", type: "murder", day: 30.0, origin: EventOrigin.Secret, isLeaked: true);
            e3.KnownBy.Add(new KnownByEntry { HeroId = PlayerHeroId, Hop = 1, LearnedDay = 30.0 });
            store["evt_sec_leaked"] = e3;
            knownBy.NoteKnower(PlayerHeroId, "evt_sec_leaked", 30.0);

            // 4. Future event (hidden)
            var e4 = CreateEvent("evt_future", type: "duel", day: 999.0);
            e4.KnownBy.Add(new KnownByEntry { HeroId = PlayerHeroId, Hop = 1, LearnedDay = 999.0 });
            store["evt_future"] = e4;
            knownBy.NoteKnower(PlayerHeroId, "evt_future", 999.0);

            var provider = new ChronicleProvider(engine, knownBy, id => store[id], PlayerHeroId, cfg);
            var result = provider.ForPlayer(50, 50.0, out var stats);

            Assert.Equal(2, result.Count);
            Assert.Equal("evt_sec_leaked", result[0].EventId);
            Assert.Equal("evt_pub_valid", result[1].EventId);
            Assert.Equal(1, stats.HiddenFuture);
            Assert.Equal(3, stats.TotalKnown); // KnownBy excludes future at currentDay=50
            Assert.Equal(2, stats.Returned);
        }

        #endregion
    }
}
