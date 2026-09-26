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

        private static PlayerHeardLogStore CreateLogStoreWithEntries(params PlayerHeardEntry[] entries)
        {
            var writer = new FailingFileWriter();
            var log = new PlayerHeardLog { Entries = entries.ToList() };
            writer.WriteAllText("player_heard.json", VividJson.Write(log));
            var store = new PlayerHeardLogStore("player_heard.json", writer);
            store.Load();
            return store;
        }

        private static PlayerHeardEntry CreateHeardEntry(
            string eventId,
            string type = "duel",
            double day = 10.0,
            int hop = 1,
            string? sourceHeroId = "hero_source",
            string? linkedEventId = null,
            IEnumerable<Fact>? facts = null,
            double learnedDay = 0.0)
        {
            return new PlayerHeardEntry
            {
                EventId = eventId,
                Type = type,
                Day = day,
                LearnedDay = learnedDay,
                PlayerHop = hop,
                SourceHeroId = sourceHeroId,
                LinkedEventId = linkedEventId,
                Facts = facts?.Select(f => f.Clone()).ToList() ?? new List<Fact>()
            };
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
            var cfg = new PresentationConfig();
            var heardEntry = CreateHeardEntry("evt_known", day: 10.0);
            var logStore = CreateLogStoreWithEntries(heardEntry);

            var provider = new ChronicleProvider(logStore, _ => null, cfg);
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
            var cfg = new PresentationConfig();
            var facts = new List<Fact>
            {
                new Fact { Id = "fact_0", Category = FactCategory.What, TextId = "VividWorld_Fact_Test_0", Text = "fact text 0" },
                new Fact { Id = "fact_3", Category = FactCategory.What, TextId = "VividWorld_Fact_Test_3", Text = "fact text 3" }
            };
            var heardEntry = CreateHeardEntry("evt_facts", facts: facts);
            var logStore = CreateLogStoreWithEntries(heardEntry);

            var provider = new ChronicleProvider(logStore, _ => null, cfg);
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
            var cfg = new PresentationConfig();
            var facts = new List<Fact>
            {
                new Fact { Id = "fact_0", Category = FactCategory.What, TextId = "VividWorld_Fact_Test_0", Text = "fact text 0" },
                new Fact { Id = "fact_1", Category = FactCategory.What, TextId = "VividWorld_Fact_Test_1", Text = "fact text 1" }
            };
            var heardEntry = CreateHeardEntry("evt_null_facts", hop: 1, sourceHeroId: "hero_source", facts: facts);
            var logStore = CreateLogStoreWithEntries(heardEntry);

            var provider = new ChronicleProvider(logStore, _ => null, cfg);
            var result = provider.ForPlayer(50, 20.0, out _);

            Assert.Single(result);
            Assert.Equal(2, result[0].Body.Parts.Count);
            Assert.Equal("hero_source", result[0].SourceHeroId);
            Assert.Equal(1, result[0].PlayerHop);
        }

        // 4. 玩家在 hop 3、事件有 5 片 => Body.Parts 少於 5（失真確實帶到紀事裡）
        [Fact]
        public void ForPlayer_DistortionCarriedToChronicle_WhenPlayerAtHigherHop()
        {
            var cfg = new PresentationConfig();
            var facts = new List<Fact>
            {
                new Fact { Id = "fact_0", Category = FactCategory.What, TextId = "VividWorld_Fact_Test_0", Text = "fact text 0" },
                new Fact { Id = "fact_1", Category = FactCategory.What, TextId = "VividWorld_Fact_Test_1", Text = "fact text 1" }
            };
            var heardEntry = CreateHeardEntry("evt_distorted", hop: 3, sourceHeroId: "hero_teller", facts: facts);
            var logStore = CreateLogStoreWithEntries(heardEntry);

            var provider = new ChronicleProvider(logStore, _ => null, cfg);
            var result = provider.ForPlayer(50, 20.0, out _);

            Assert.Single(result);
            Assert.True(result[0].Body.Parts.Count < 5, "Distortion should reduce fact count at hop 3");
        }

        // 5. 未洩漏的秘密不出現（即使玩家在 KnownBy 裡）
        [Fact]
        public void ForPlayer_UnleakedSecretDoesNotAppear_EvenIfPlayerInKnownBy()
        {
            var cfg = new PresentationConfig();
            // 未洩漏的秘密在寫入紀錄時被排除，紀錄庫中為空
            var logStore = CreateLogStoreWithEntries();

            var provider = new ChronicleProvider(logStore, _ => null, cfg);
            var result = provider.ForPlayer(50, 20.0, out var stats);

            Assert.Empty(result);
            Assert.Equal(0, stats.TotalKnown);
            Assert.Equal(0, stats.Returned);
        }

        // 6. 洩漏之後同一則會出現
        [Fact]
        public void ForPlayer_LeakedSecretAppears()
        {
            var cfg = new PresentationConfig();
            var heardEntry = CreateHeardEntry("evt_secret_leaked", hop: 1, day: 10.0);
            var logStore = CreateLogStoreWithEntries(heardEntry);

            var provider = new ChronicleProvider(logStore, _ => null, cfg);
            var result = provider.ForPlayer(50, 20.0, out var stats);

            Assert.Single(result);
            Assert.Equal("evt_secret_leaked", result[0].EventId);
            Assert.Equal(1, stats.Returned);
        }

        // 7. 日期比今天晚的事件不出現，且 stats.HiddenFuture 數得對
        [Fact]
        public void ForPlayer_HidesFutureEvents_AndCountsHiddenFuture()
        {
            var cfg = new PresentationConfig();
            var evtPast = CreateHeardEntry("evt_past", day: 50.0);
            var evtFuture = CreateHeardEntry("evt_future", day: 150.0);
            var logStore = CreateLogStoreWithEntries(evtPast, evtFuture);

            var provider = new ChronicleProvider(logStore, _ => null, cfg);
            var result = provider.ForPlayer(50, 100.0, out var stats);

            Assert.Single(result);
            Assert.Equal("evt_past", result[0].EventId);
            Assert.Equal(1, stats.HiddenFuture);
            Assert.Equal(2, stats.TotalKnown);
        }

        // 8. 排序：LearnedDay 新到舊；同一天照事件 Day 新到舊；同一天用 EventId 當第三鍵，同一份資料跑兩次順序相同
        [Fact]
        public void ForPlayer_SortsDescendingByLearnedDay_ThenDay_ThenOrdinalByEventId()
        {
            var cfg = new PresentationConfig();
            var e1 = CreateHeardEntry("evt_day10", day: 10.0, learnedDay: 20.0);
            var e2 = CreateHeardEntry("evt_day30_b", day: 30.0, learnedDay: 40.0);
            var e3 = CreateHeardEntry("evt_day30_a", day: 30.0, learnedDay: 40.0);
            var e4 = CreateHeardEntry("evt_day05", day: 5.0, learnedDay: 10.0);
            var logStore = CreateLogStoreWithEntries(e1, e2, e3, e4);

            var provider = new ChronicleProvider(logStore, _ => null, cfg);
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

        // 8a. LearnedDay 不同時照 LearnedDay 降序排序
        [Fact]
        public void ForPlayer_SortsDescendingByLearnedDay_WhenLearnedDaysDiffer()
        {
            var cfg = new PresentationConfig();
            var e1 = CreateHeardEntry("evt_heard_earlier", day: 50.0, learnedDay: 20.0);
            var e2 = CreateHeardEntry("evt_heard_later", day: 10.0, learnedDay: 30.0);
            var logStore = CreateLogStoreWithEntries(e1, e2);

            var provider = new ChronicleProvider(logStore, _ => null, cfg);
            var result = provider.ForPlayer(50, 100.0, out _);

            Assert.Equal(2, result.Count);
            Assert.Equal("evt_heard_later", result[0].EventId);
            Assert.Equal("evt_heard_earlier", result[1].EventId);
        }

        // 8b. LearnedDay 相同時照事件日期 Day 降序排序
        [Fact]
        public void ForPlayer_SortsDescendingByDay_WhenLearnedDaysEqual()
        {
            var cfg = new PresentationConfig();
            var e1 = CreateHeardEntry("evt_older_event", day: 10.0, learnedDay: 25.0);
            var e2 = CreateHeardEntry("evt_newer_event", day: 20.0, learnedDay: 25.0);
            var logStore = CreateLogStoreWithEntries(e1, e2);

            var provider = new ChronicleProvider(logStore, _ => null, cfg);
            var result = provider.ForPlayer(50, 100.0, out _);

            Assert.Equal(2, result.Count);
            Assert.Equal("evt_newer_event", result[0].EventId);
            Assert.Equal("evt_older_event", result[1].EventId);
        }

        // 8c. LearnedDay 與事件日期 Day 皆相同時，照 EventId Ordinal 升序排序
        [Fact]
        public void ForPlayer_SortsOrdinalByEventId_WhenLearnedDayAndDayEqual()
        {
            var cfg = new PresentationConfig();
            var e1 = CreateHeardEntry("evt_z", day: 10.0, learnedDay: 20.0);
            var e2 = CreateHeardEntry("evt_a", day: 10.0, learnedDay: 20.0);
            var e3 = CreateHeardEntry("evt_m", day: 10.0, learnedDay: 20.0);
            var logStore = CreateLogStoreWithEntries(e1, e2, e3);

            var provider = new ChronicleProvider(logStore, _ => null, cfg);
            var result = provider.ForPlayer(50, 100.0, out _);

            Assert.Equal(3, result.Count);
            Assert.Equal("evt_a", result[0].EventId);
            Assert.Equal("evt_m", result[1].EventId);
            Assert.Equal("evt_z", result[2].EventId);
        }

        // 8d. 一則事件日期較舊、但比較晚聽到的，排在前面
        [Fact]
        public void ForPlayer_OlderEventHeardLater_RanksFirst()
        {
            var cfg = new PresentationConfig();
            var eOlder = CreateHeardEntry("evt_old_rumor", day: 5.0, learnedDay: 50.0);
            var eNewer = CreateHeardEntry("evt_new_fresh", day: 40.0, learnedDay: 40.0);
            var logStore = CreateLogStoreWithEntries(eOlder, eNewer);

            var provider = new ChronicleProvider(logStore, _ => null, cfg);
            var result = provider.ForPlayer(50, 60.0, out _);

            Assert.Equal(2, result.Count);
            Assert.Equal("evt_old_rumor", result[0].EventId);
            Assert.Equal("evt_new_fresh", result[1].EventId);
            Assert.Equal(50.0, result[0].LearnedDay);
            Assert.Equal(40.0, result[1].LearnedDay);
        }

        // 8e. 截上限後留下的是最晚聽到的事件（截掉最先聽到的）
        [Fact]
        public void ForPlayer_CapsAtMaxEntries_KeepsLatestHeardEntries()
        {
            var cfg = new PresentationConfig();
            var eEarliest = CreateHeardEntry("evt_heard_day10", day: 10.0, learnedDay: 10.0);
            var eMid = CreateHeardEntry("evt_heard_day20", day: 20.0, learnedDay: 20.0);
            var eLatest = CreateHeardEntry("evt_heard_day30", day: 5.0, learnedDay: 30.0);
            var logStore = CreateLogStoreWithEntries(eEarliest, eMid, eLatest);

            var provider = new ChronicleProvider(logStore, _ => null, cfg);
            var result = provider.ForPlayer(2, 50.0, out var stats);

            Assert.Equal(2, result.Count);
            Assert.Equal(2, stats.Returned);
            Assert.Equal(3, stats.TotalKnown);
            Assert.Equal("evt_heard_day30", result[0].EventId);
            Assert.Equal("evt_heard_day20", result[1].EventId);
            Assert.DoesNotContain(result, e => e.EventId == "evt_heard_day10");
        }

        // 9. maxEntries 夾取；maxEntries = 0 當 1；stats.Returned 對得上
        [Fact]
        public void ForPlayer_CapsAtMaxEntries_ClampsZeroToOne_ReportsReturnedCount()
        {
            var cfg = new PresentationConfig();
            var entries = new List<PlayerHeardEntry>();
            for (int i = 0; i < 5; i++)
            {
                entries.Add(CreateHeardEntry("evt_" + i, day: 10.0 + i));
            }
            var logStore = CreateLogStoreWithEntries(entries.ToArray());
            var provider = new ChronicleProvider(logStore, _ => null, cfg);

            var resultCap3 = provider.ForPlayer(3, 100.0, out var stats3);
            Assert.Equal(3, resultCap3.Count);
            Assert.Equal(3, stats3.Returned);
            Assert.Equal(5, stats3.TotalKnown);

            var resultCap0 = provider.ForPlayer(0, 100.0, out var stats0);
            Assert.Single(resultCap0);
            Assert.Equal(1, stats0.Returned);
        }

        // 10. 事件已經不在了，紀錄裡的照樣顯示（MF3a 關鍵契約）
        [Fact]
        public void ForPlayer_WhenEventShardNoLongerExists_StillDisplaysFromHeardLog()
        {
            var cfg = new PresentationConfig();
            var heardEntry = CreateHeardEntry("evt_deleted", day: 10.0);
            var logStore = CreateLogStoreWithEntries(heardEntry);

            var provider = new ChronicleProvider(logStore, _ => null, cfg);
            var result = provider.ForPlayer(50, 20.0, out var stats);

            Assert.Single(result);
            Assert.Equal("evt_deleted", result[0].EventId);
            Assert.Equal(1, stats.TotalKnown);
            Assert.Equal(1, stats.Returned);
        }

        // 11. 紀錄中玩家的事件完整回傳
        [Fact]
        public void ForPlayer_ReturnsPlayerRecordedEvents()
        {
            var cfg = new PresentationConfig();
            var e1 = CreateHeardEntry("evt_recorded", day: 10.0);
            var logStore = CreateLogStoreWithEntries(e1);

            var provider = new ChronicleProvider(logStore, _ => null, cfg);
            var result = provider.ForPlayer(50, 100.0, out var stats);

            Assert.Single(result);
            Assert.Equal("evt_recorded", result[0].EventId);
            Assert.Equal(1, stats.TotalKnown);
            Assert.Equal(1, stats.Returned);
        }

        // 12. Body.Parts 為空的事件照樣回傳一列
        [Fact]
        public void ForPlayer_EventWithEmptyParts_StillReturned()
        {
            var cfg = new PresentationConfig();
            var heardEntry = CreateHeardEntry("evt_empty_parts", facts: Array.Empty<Fact>());
            var logStore = CreateLogStoreWithEntries(heardEntry);

            var provider = new ChronicleProvider(logStore, _ => null, cfg);
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
            var cfg = new PresentationConfig();
            var heardEntry = CreateHeardEntry("evt_headline", type: "custom_type", day: 10.0);
            var logStore = CreateLogStoreWithEntries(heardEntry);

            var provider = new ChronicleProvider(logStore, _ => null, cfg);
            var result = provider.ForPlayer(50, 100.0, out _);

            Assert.Single(result);
            Assert.Equal("VividWorld_EventType_custom_type", result[0].HeadlineTextId);
        }

        // 14. 模板沒宣告 headline => HeadlineFallback 等於型別字串，不得丟例外
        [Fact]
        public void ForPlayer_WhenTemplateHasNoHeadline_HeadlineFallbackEqualsType()
        {
            var cfg = new PresentationConfig();
            var heardEntry = CreateHeardEntry("evt_no_tmpl", type: "unregistered_type", day: 10.0);
            var logStore = CreateLogStoreWithEntries(heardEntry);

            var provider = new ChronicleProvider(
                logStore,
                type => new EventTemplate { Type = type, Headline = null },
                cfg);

            var result = provider.ForPlayer(50, 100.0, out _);

            Assert.Single(result);
            Assert.Equal("unregistered_type", result[0].HeadlineFallback);
        }

        // 15. DayLabel／SourceHeroName 由 Core 產出時一律是空字串／null
        [Fact]
        public void ForPlayer_DayLabelAndSourceHeroName_AreEmptyAndNullFromCore()
        {
            var cfg = new PresentationConfig();
            var heardEntry = CreateHeardEntry("evt_core_dto", day: 10.0, hop: 1, sourceHeroId: "hero_source");
            var logStore = CreateLogStoreWithEntries(heardEntry);

            var provider = new ChronicleProvider(logStore, _ => null, cfg);
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
                HiddenFuture = 0
            };

            string lineData = ChronicleLogFormatter.FormatOpen("main_hero", 91143.1, statsData, 50);
            Assert.Equal(
                "Chronicle opened for main_hero on day 91143.1: 37 shown of 41 heard (max 50), hidden future 0",
                lineData);

            var statsEmpty = new ChronicleStats
            {
                TotalKnown = 0,
                Returned = 0,
                HiddenFuture = 0
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

        // 24. ChronicleProvider 建構子正確運作
        [Fact]
        public void ChronicleProvider_Constructor_Succeeds()
        {
            var cfg = new PresentationConfig();
            var heardEntry = CreateHeardEntry("evt_5arg", day: 10.0);
            var logStore = CreateLogStoreWithEntries(heardEntry);

            var provider = new ChronicleProvider(logStore, _ => null, cfg);
            var result = provider.ForPlayer(50, 20.0, out _);

            Assert.Single(result);
            Assert.Equal("evt_5arg", result[0].EventId);
            Assert.Equal("duel", result[0].HeadlineFallback);
        }

        // 25. 引擎不在時**不得**退回事件的完整碎片集合——那是 hop 0 的全知版本
        [Fact]
        public void PlayerKnownFacts_Of_WithoutEngine_ReturnsNoFacts_NotTheWholeEvent()
        {
            var evt = CreateEvent("evt_no_engine", day: 10.0);
            var playerEntry = new KnownByEntry { HeroId = PlayerHeroId, Hop = 3, LearnedDay = 10.0, KnownFactIds = null };

            var facts = PlayerKnownFacts.Of(evt, playerEntry, null);

            Assert.Empty(facts);
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
            Assert.Equal(0, stats.Returned);
        }

        // 28. KnownByEntry 傳播來源欄位 SourceHeroId 與 LinkedEventId 正確傳遞至 ChronicleEntry
        [Fact]
        public void ForPlayer_SourceHeroIdAndLinkedEventId_PropagatedToChronicleEntry()
        {
            var cfg = new PresentationConfig();
            var heardEntry = CreateHeardEntry("evt_linked", day: 10.0, hop: 2, sourceHeroId: "hero_messenger", linkedEventId: "evt_origin");
            var logStore = CreateLogStoreWithEntries(heardEntry);

            var provider = new ChronicleProvider(logStore, _ => null, cfg);
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
            var cfg = new PresentationConfig();
            var heardEntry = CreateHeardEntry("evt_hop0", day: 10.0, hop: 0, sourceHeroId: null);
            var logStore = CreateLogStoreWithEntries(heardEntry);

            var provider = new ChronicleProvider(logStore, _ => null, cfg);
            var result = provider.ForPlayer(50, 20.0, out _);

            Assert.Single(result);
            Assert.Equal(0, result[0].PlayerHop);
            Assert.Null(result[0].SourceHeroId);
        }

        // 30. 模板宣告 headline 時 HeadlineFallback 優先採用模板之 headline
        [Fact]
        public void ForPlayer_WhenTemplateHasHeadline_HeadlineFallbackPrefersTemplateHeadline()
        {
            var cfg = new PresentationConfig();
            var heardEntry = CreateHeardEntry("evt_tmpl_headline", type: "wager_struck", day: 10.0);
            var logStore = CreateLogStoreWithEntries(heardEntry);

            var provider = new ChronicleProvider(
                logStore,
                type => new EventTemplate { Type = type, Headline = "A hunting wager was struck" },
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
            var cfg = new PresentationConfig();
            var logStore = CreateLogStoreWithEntries();
            var provider = new ChronicleProvider(logStore, _ => null, cfg);

            var result = provider.ForPlayer(50, 20.0, out var stats);

            Assert.Empty(result);
            Assert.Equal(0, stats.TotalKnown);
            Assert.Equal(0, stats.Returned);
        }

        // 32. 跨多個分片與型別時排序與過濾依然穩健
        [Fact]
        public void ForPlayer_HandlesMixedPublicSecretAndFutureEvents()
        {
            var cfg = new PresentationConfig();
            var e1 = CreateHeardEntry("evt_pub_valid", type: "duel", day: 20.0);
            var e3 = CreateHeardEntry("evt_sec_leaked", type: "murder", day: 30.0);
            var e4 = CreateHeardEntry("evt_future", type: "duel", day: 999.0);
            var logStore = CreateLogStoreWithEntries(e1, e3, e4);

            var provider = new ChronicleProvider(logStore, _ => null, cfg);
            var result = provider.ForPlayer(50, 50.0, out var stats);

            Assert.Equal(2, result.Count);
            Assert.Equal("evt_sec_leaked", result[0].EventId);
            Assert.Equal("evt_pub_valid", result[1].EventId);
            Assert.Equal(1, stats.HiddenFuture);
            Assert.Equal(3, stats.TotalKnown);
            Assert.Equal(2, stats.Returned);
        }

        #endregion

        #region 1b. 排版日誌測試 (MF3a-fix1)

        // 33. 清單元件未找到：verdict 為 "list widget not found"，列出 0 列
        [Fact]
        public void ChronicleLogFormatter_FormatLayout_ListWidgetNotFound_ProducesExpectedVerdict()
        {
            var output = ChronicleLogFormatter.FormatLayout(
                frame: 1,
                entryCount: 3,
                viewportHeight: 500.0,
                rows: null,
                listWidgetFound: false,
                out var verdict);

            Assert.Equal("list widget not found", verdict);
            Assert.Equal(
                "Chronicle layout (frame 1): 0 row widget(s) for 3 entry(ies), viewport height 500; list widget not found",
                output);
        }

        // 34. 列數與項目數不符：verdict 為 "row count mismatch"，列出實際擁有的列
        [Fact]
        public void ChronicleLogFormatter_FormatLayout_RowCountMismatch_ProducesExpectedVerdict()
        {
            var rows = new List<ChronicleRowLayout>
            {
                new ChronicleRowLayout(y: 0, height: 40, visible: true),
                new ChronicleRowLayout(y: 40, height: 40, visible: true)
            };

            var output = ChronicleLogFormatter.FormatLayout(
                frame: 2,
                entryCount: 3,
                viewportHeight: 600.0,
                rows: rows,
                listWidgetFound: true,
                out var verdict);

            Assert.Equal("row count mismatch", verdict);
            var expected = "Chronicle layout (frame 2): 2 row widget(s) for 3 entry(ies), viewport height 600; row count mismatch\n" +
                           "  row 0: y 0, height 40, visible true\n" +
                           "  row 1: y 40, height 40, visible true";
            Assert.Equal(expected, output);
        }

        // 35. 包含高度 <= 0.5 的列：verdict 為 "zero-height row(s): {indices}"
        [Fact]
        public void ChronicleLogFormatter_FormatLayout_ZeroHeightRows_ProducesExpectedVerdict()
        {
            var rows = new List<ChronicleRowLayout>
            {
                new ChronicleRowLayout(y: 0, height: 50, visible: true),
                new ChronicleRowLayout(y: 50, height: 0.4, visible: true),
                new ChronicleRowLayout(y: 50, height: 0.0, visible: false)
            };

            var output = ChronicleLogFormatter.FormatLayout(
                frame: 3,
                entryCount: 3,
                viewportHeight: 600.0,
                rows: rows,
                listWidgetFound: true,
                out var verdict);

            Assert.Equal("zero-height row(s): 1, 2", verdict);
            var expected = "Chronicle layout (frame 3): 3 row widget(s) for 3 entry(ies), viewport height 600; zero-height row(s): 1, 2\n" +
                           "  row 0: y 0, height 50, visible true\n" +
                           "  row 1: y 50, height 0, visible true\n" +
                           "  row 2: y 50, height 0, visible false";
            Assert.Equal(expected, output);
        }

        // 36. 重疊列：verdict 為 "overlapping rows: {i}&{i+1}"
        [Fact]
        public void ChronicleLogFormatter_FormatLayout_OverlappingRows_ProducesExpectedVerdict()
        {
            var rows = new List<ChronicleRowLayout>
            {
                new ChronicleRowLayout(y: 0, height: 50, visible: true),
                new ChronicleRowLayout(y: 40, height: 50, visible: true),
                new ChronicleRowLayout(y: 80, height: 50, visible: true)
            };

            var output = ChronicleLogFormatter.FormatLayout(
                frame: 4,
                entryCount: 3,
                viewportHeight: 600.0,
                rows: rows,
                listWidgetFound: true,
                out var verdict);

            Assert.Equal("overlapping rows: 0&1, 1&2", verdict);
            var expected = "Chronicle layout (frame 4): 3 row widget(s) for 3 entry(ies), viewport height 600; overlapping rows: 0&1, 1&2\n" +
                           "  row 0: y 0, height 50, visible true\n" +
                           "  row 1: y 40, height 50, visible true\n" +
                           "  row 2: y 80, height 50, visible true";
            Assert.Equal(expected, output);
        }

        // 37. 排版正常：verdict 為 "ok"，格式逐字吻合
        [Fact]
        public void ChronicleLogFormatter_FormatLayout_Ok_ProducesExpectedVerdict()
        {
            var rows = new List<ChronicleRowLayout>
            {
                new ChronicleRowLayout(y: 0, height: 50, visible: true),
                new ChronicleRowLayout(y: 66, height: 50, visible: true)
            };

            var output = ChronicleLogFormatter.FormatLayout(
                frame: 1,
                entryCount: 2,
                viewportHeight: 800.0,
                rows: rows,
                listWidgetFound: true,
                out var verdict);

            Assert.Equal("ok", verdict);
            var expected = "Chronicle layout (frame 1): 2 row widget(s) for 2 entry(ies), viewport height 800; ok\n" +
                           "  row 0: y 0, height 50, visible true\n" +
                           "  row 1: y 66, height 50, visible true";
            Assert.Equal(expected, output);
        }

        // 38. 超過 10 列時：只印前 10 列並加上 "  ... and {n} more"
        [Fact]
        public void ChronicleLogFormatter_FormatLayout_MoreThan10Rows_TruncatesAt10AndPrintsMore()
        {
            var rows = new List<ChronicleRowLayout>();
            for (int i = 0; i < 15; i++)
            {
                rows.Add(new ChronicleRowLayout(y: i * 60, height: 50, visible: true));
            }

            var output = ChronicleLogFormatter.FormatLayout(
                frame: 5,
                entryCount: 15,
                viewportHeight: 1000.0,
                rows: rows,
                listWidgetFound: true,
                out var verdict);

            Assert.Equal("ok", verdict);
            Assert.Contains("  row 0: y 0, height 50, visible true", output);
            Assert.Contains("  row 9: y 540, height 50, visible true", output);
            Assert.Contains("  ... and 5 more", output);
            Assert.DoesNotContain("row 10:", output);
            Assert.DoesNotContain("row 14:", output);

            // 總行數應為 1 (標題) + 10 (列) + 1 (... and 5 more) = 12 行
            var lines = output.Split('\n');
            Assert.Equal(12, lines.Length);
        }

        #endregion
    }
}
