#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using VividWorld.Core.Channels;
using VividWorld.Core.Config;
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
    /// <summary>玩家紀錄按「誰告訴我」一份一份記：來源清單、舊檔相容、紀事按來源分塊、休眠排除玩家。</summary>
    public class PlayerHeardSourcesTests
    {
        private const string PlayerHeroId = "hero_player";
        private const string File = "player_heard.json";

        private static WorldEvent CreateEvent(string eventId, int factCount = 5, int drama = 3)
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
                Type = "duel",
                Day = 10.0,
                Origin = EventOrigin.Public,
                State = new RumorState { Leaked = true },
                DramaWeight = drama,
                Facts = facts
            };
        }

        private static KnownByEntry PlayerEntry(string source, int hop, double learned = 10.0)
        {
            return new KnownByEntry { HeroId = PlayerHeroId, Hop = hop, SourceHeroId = source, LearnedDay = learned };
        }

        private static PlayerHeardSource Telling(string heroId, int hop, double day, IEnumerable<int> facts, string? feelingKey = null)
        {
            return new PlayerHeardSource
            {
                HeroId = heroId,
                Hop = hop,
                Day = day,
                FactIds = facts.Select(i => "fact_" + i).ToList(),
                FeelingLineKey = feelingKey,
                FeelingAddressKey = feelingKey == null ? null : "VividWorld_Address_Test",
                FeelingFocusHeroId = feelingKey == null ? null : "hero_focus"
            };
        }

        private static IReadOnlyList<Fact> Facts(WorldEvent evt, params int[] indexes)
        {
            return indexes.Select(i => evt.Facts[i]).ToList();
        }

        private static PlayerHeardLogStore NewStore(out FailingFileWriter writer)
        {
            writer = new FailingFileWriter();
            var store = new PlayerHeardLogStore(File, writer);
            store.Load();
            return store;
        }

        // ── 舊檔 ──

        /// <summary>現行格式的檔案（還沒有來源清單）：用現在的序列化器寫一份，再拿掉新欄位、加上未知欄位。</summary>
        private static string CurrentFormatFile(out string eventId)
        {
            var evt = CreateEvent("evt_old", factCount: 3);
            var scratch = new PlayerHeardLogStore(File, new FailingFileWriter());
            scratch.Load();
            scratch.Record(evt, PlayerEntry("hero_old_teller", hop: 3, learned: 12.0), Facts(evt, 0, 1), 12.0);
            var log = new PlayerHeardLog { Entries = new List<PlayerHeardEntry> { scratch.Find("evt_old")! } };

            var root = JObject.Parse(VividJson.Write(log));
            foreach (var entry in root["entries"]!.Children<JObject>())
            {
                entry.Remove("sources");
                entry["futureEntryField"] = 7;
            }
            root["futureTopField"] = "kept";
            eventId = "evt_old";
            return root.ToString();
        }

        [Fact]
        public void OldFormatFile_LoadsAsOneSource_BuiltFromExistingFields()
        {
            string json = CurrentFormatFile(out string eventId);
            Assert.DoesNotContain("\"sources\"", json);

            var writer = new FailingFileWriter();
            writer.WriteAllText(File, json);
            var store = new PlayerHeardLogStore(File, writer);
            var load = store.Load();

            Assert.Equal(PlayerHeardLoadStatus.ReadFromFile, load.Status);
            Assert.False(store.IsDirty);   // 只是讀過，不改寫舊檔

            var entry = store.Find(eventId)!;
            var source = Assert.Single(entry.Sources);
            Assert.Equal("hero_old_teller", source.HeroId);
            Assert.Equal(3, source.Hop);                       // 舊紀錄的手數照舊，不重算
            Assert.Equal(new[] { "fact_0", "fact_1" }, source.FactIds);
            Assert.False(source.HasFeeling);
            Assert.Equal(3, entry.PlayerHop);
        }

        [Fact]
        public void OldFormatFile_UnknownFieldsSurviveAWrite_AndSourcesAreAdded()
        {
            string json = CurrentFormatFile(out string eventId);
            var writer = new FailingFileWriter();
            writer.WriteAllText(File, json);
            var store = new PlayerHeardLogStore(File, writer);
            store.Load();

            // 另一個人後來也講了，補上新碎片
            var evt = CreateEvent(eventId, factCount: 3);
            var result = store.RecordTelling(evt, PlayerEntry("hero_new_teller", hop: 1, learned: 12.0), Facts(evt, 0, 1, 2), 20.0,
                Telling("hero_new_teller", 1, 20.0, new[] { 2 }));
            Assert.True(result.Changed);
            Assert.Equal(PlayerHeardSourceChange.Added, result.SourceChange);
            Assert.True(store.Flush());

            var written = JObject.Parse(writer.ReadAllText(File)!);
            Assert.Equal("kept", (string?)written["futureTopField"]);
            var entryJson = (JObject)written["entries"]![0]!;
            Assert.Equal(7, (int?)entryJson["futureEntryField"]);
            Assert.Equal(2, entryJson["sources"]!.Count());

            // 新格式的檔案照樣讀得回來
            var reread = new PlayerHeardLogStore(File, writer);
            reread.Load();
            Assert.Equal(2, reread.Find(eventId)!.Sources.Count);
        }

        // ── 兩個人、同一個人先大概後完整 ──

        [Fact]
        public void TwoTellers_GetTwoSources_TopLevelFieldsStayTheUnion()
        {
            var store = NewStore(out _);
            var evt = CreateEvent("evt_two");

            var first = store.RecordTelling(evt, PlayerEntry("hero_a", 1), Facts(evt, 0, 1), 10.0, Telling("hero_a", 1, 10.0, new[] { 0, 1 }));
            Assert.True(first.EntryAdded);

            var second = store.RecordTelling(evt, PlayerEntry("hero_b", 2), Facts(evt, 0, 1, 2), 11.0, Telling("hero_b", 2, 11.0, new[] { 2 }, "VividWorld_Feeling_Test"));
            Assert.False(second.EntryAdded);
            Assert.Equal(PlayerHeardSourceChange.Added, second.SourceChange);
            Assert.Equal(2, second.SourceCount);

            var entry = store.Find("evt_two")!;
            Assert.Equal(new[] { "hero_a", "hero_b" }, entry.Sources.Select(s => s.HeroId).ToArray());
            Assert.Equal(new[] { "fact_0", "fact_1" }, entry.Sources[0].FactIds);
            Assert.Equal(new[] { "fact_2" }, entry.Sources[1].FactIds);
            Assert.False(entry.Sources[0].HasFeeling);
            Assert.True(entry.Sources[1].HasFeeling);
            Assert.Equal(new[] { "fact_0", "fact_1", "fact_2" }, entry.Facts.Select(f => f.Id).ToArray());
            Assert.Equal(1, entry.PlayerHop);                 // 取最小
            Assert.Equal("hero_b", entry.SourceHeroId);       // 最上層仍是最近一次講的人
        }

        [Fact]
        public void SameTeller_GistThenFull_UpdatesOneSource()
        {
            var store = NewStore(out _);
            var evt = CreateEvent("evt_gist_full");

            // 先講大概：手數是講者 + 1，碎片少，沒有感想
            store.RecordTelling(evt, PlayerEntry("hero_a", 1), Facts(evt, 0, 1), 10.0, Telling("hero_a", 1, 10.0, new[] { 0, 1 }));

            // 後來交情夠了講完整：更新他那一份
            var result = store.RecordTelling(evt, PlayerEntry("hero_a", 1), Facts(evt, 0, 1, 2, 3), 15.0,
                Telling("hero_a", 1, 15.0, new[] { 0, 1, 2, 3 }, "VividWorld_Feeling_Test"));

            Assert.Equal(PlayerHeardSourceChange.Updated, result.SourceChange);
            Assert.Equal(1, result.SourceCount);

            var entry = store.Find("evt_gist_full")!;
            var source = Assert.Single(entry.Sources);
            Assert.Equal(new[] { "fact_0", "fact_1", "fact_2", "fact_3" }, source.FactIds);
            Assert.Equal(1, source.Hop);
            Assert.Equal(15.0, source.Day);
            Assert.Equal("VividWorld_Feeling_Test", source.FeelingLineKey);
            Assert.Equal("VividWorld_Address_Test", source.FeelingAddressKey);
            Assert.Equal("hero_focus", source.FeelingFocusHeroId);
        }

        [Fact]
        public void SameTeller_LaterTellingWithoutFeeling_KeepsTheEarlierFeeling()
        {
            var store = NewStore(out _);
            var evt = CreateEvent("evt_keep_feeling");
            store.RecordTelling(evt, PlayerEntry("hero_a", 1), Facts(evt, 0, 1), 10.0, Telling("hero_a", 1, 10.0, new[] { 0, 1 }, "VividWorld_Feeling_Test"));
            store.RecordTelling(evt, PlayerEntry("hero_a", 1), Facts(evt, 0, 1, 2), 12.0, Telling("hero_a", 1, 12.0, new[] { 0, 1, 2 }));

            var source = Assert.Single(store.Find("evt_keep_feeling")!.Sources);
            Assert.Equal("VividWorld_Feeling_Test", source.FeelingLineKey);
        }

        [Fact]
        public void Sources_SurviveWriteAndReload_IncludingTheFeelingKeys()
        {
            var store = NewStore(out var writer);
            var evt = CreateEvent("evt_roundtrip");
            store.RecordTelling(evt, PlayerEntry("hero_a", 2), Facts(evt, 0, 1), 10.0, Telling("hero_a", 2, 10.0, new[] { 0, 1 }, "VividWorld_Feeling_Test"));
            Assert.True(store.Flush());

            var reread = new PlayerHeardLogStore(File, writer);
            reread.Load();
            var source = Assert.Single(reread.Find("evt_roundtrip")!.Sources);
            Assert.Equal("hero_a", source.HeroId);
            Assert.Equal(2, source.Hop);
            Assert.Equal("VividWorld_Feeling_Test", source.FeelingLineKey);
            Assert.Equal("VividWorld_Address_Test", source.FeelingAddressKey);
            Assert.Equal("hero_focus", source.FeelingFocusHeroId);
        }

        // ── 紀事按來源分塊 ──

        [Fact]
        public void Chronicle_OneBlockPerSource_FeelingOnlyOnTheSourceThatHadOne()
        {
            var store = NewStore(out _);
            var evt = CreateEvent("evt_chronicle");
            store.RecordTelling(evt, PlayerEntry("hero_a", 1), Facts(evt, 0, 1), 10.0, Telling("hero_a", 1, 10.0, new[] { 0, 1 }));
            store.RecordTelling(evt, PlayerEntry("hero_b", 2), Facts(evt, 0, 1, 2, 3), 11.0, Telling("hero_b", 2, 11.0, new[] { 0, 1, 2, 3 }, "VividWorld_Feeling_Test"));

            var provider = new ChronicleProvider(store, _ => null, new PresentationConfig());
            var entries = provider.ForPlayer(10, currentDay: 50.0, out _);

            var entry = Assert.Single(entries);
            Assert.Equal(2, entry.Sources.Count);

            var gist = entry.Sources[0];
            Assert.Equal("hero_a", gist.HeroId);
            Assert.Equal(1, gist.Hop);
            Assert.Equal(2, gist.FactCount);
            Assert.Null(gist.Feeling);

            var full = entry.Sources[1];
            Assert.Equal("hero_b", full.HeroId);
            Assert.Equal(2, full.Hop);
            Assert.Equal(4, full.FactCount);
            Assert.NotNull(full.Feeling);
            Assert.Equal("VividWorld_Feeling_Test", full.Feeling!.LineKey);
            Assert.Equal("VividWorld_Address_Test", full.Feeling.AddressKey);
            Assert.Equal("hero_focus", full.Feeling.FocusHeroId);

            // 這一份的事實只含他講的那幾塊
            Assert.True(gist.Body.Parts.Count < full.Body.Parts.Count);
        }

        [Fact]
        public void Chronicle_OldEntry_ShowsOneBlockMadeFromTheWholeRecord()
        {
            string json = CurrentFormatFile(out _);
            var writer = new FailingFileWriter();
            writer.WriteAllText(File, json);
            var store = new PlayerHeardLogStore(File, writer);
            store.Load();

            var provider = new ChronicleProvider(store, _ => null, new PresentationConfig());
            var entry = Assert.Single(provider.ForPlayer(10, currentDay: 50.0, out _));

            var source = Assert.Single(entry.Sources);
            Assert.Equal("hero_old_teller", source.HeroId);
            Assert.Equal(3, source.Hop);
            Assert.Equal(2, source.FactCount);
            Assert.Null(source.Feeling);
        }

        // ── 日誌 ──

        [Fact]
        public void LogLines_NameWhoHopFactsAndFeeling()
        {
            Assert.Equal(
                "Player heard-log: source added for evt_1 - Alda (hop 1, 3 fact(s), with a feeling, no spoken line); 2 source(s) on the entry",
                PlayerHeardLogFormatter.FormatSourceChange(PlayerHeardSourceChange.Added, "evt_1", "Alda", 1, 3, true, 2));
            Assert.Equal(
                "Player heard-log: source updated for evt_1 - Alda (hop 1, 4 fact(s), no feeling, spoken line saved, candidate 'key_cand', followed by self feeling); 1 source(s) on the entry",
                PlayerHeardLogFormatter.FormatSourceChange(PlayerHeardSourceChange.Updated, "evt_1", "Alda", 1, 4, false, 1, true, "key_cand", "self feeling"));

            var sources = new List<ChronicleSource>
            {
                new() { HeroId = "hero_a", Hop = 1, FactCount = 2, HasSpokenLine = true },
                new() { HeroId = null, Hop = 3, FactCount = 4, Feeling = new VividWorld.Core.Feelings.FeelingDecision { LineKey = "k" } }
            };
            Assert.Equal(
                "Chronicle entry evt_1: 2 source(s) - hero_a (hop 1, 2 fact(s), no feeling, reconstructed quote); unknown (hop 3, 4 fact(s), with a feeling, facts (reason: no spoken line saved))",
                ChronicleLogFormatter.FormatEntrySources("evt_1", sources));
        }

        // ── 記下講了哪一句：重組與格式 ──

        [Fact]
        public void SpokenLine_BystanderEyewitness_PreservesPrefixAndFactsAndFeeling()
        {
            var store = NewStore(out _);
            var evt = CreateEvent("evt_eyewitness", factCount: 3);
            var telling = new PlayerHeardSource
            {
                HeroId = "hero_bystander",
                Hop = 0,
                Day = 10.0,
                FactIds = new List<string> { "fact_0", "fact_1" },
                PrefixTextId = RumorPrefixSelector.EyewitnessTextId,
                SpeakerHeroId = "hero_bystander",
                SentenceKeyCandidates = new List<string> { "VividWorld_Sentence_Test_0_1" },
                FeelingLineKey = "VividWorld_Feeling_Test",
                FeelingAddressKey = "VividWorld_Address_Test",
                FeelingFocusHeroId = "hero_focus"
            };

            store.RecordTelling(evt, PlayerEntry("hero_bystander", hop: 0), Facts(evt, 0, 1), 10.0, telling);
            var provider = new ChronicleProvider(store, _ => null, new PresentationConfig());
            var entry = Assert.Single(provider.ForPlayer(10, currentDay: 20.0, out _));
            var source = Assert.Single(entry.Sources);

            Assert.True(source.HasSpokenLine);
            Assert.Equal("hero_bystander", source.HeroId);
            Assert.Equal(RumorPrefixSelector.EyewitnessTextId, source.Body.PrefixTextId);
            Assert.NotNull(source.Feeling);
            Assert.Equal("VividWorld_Feeling_Test", source.Feeling!.LineKey);
            Assert.Equal(2, source.FactCount);
            Assert.Equal("feeling", telling.TrailingKind);
        }

        [Fact]
        public void SpokenLine_HeardFromSource_PreservesSourceHeroInPrefix()
        {
            var store = NewStore(out _);
            var evt = CreateEvent("evt_heard_source", factCount: 3);
            var telling = new PlayerHeardSource
            {
                HeroId = "hero_teller",
                Hop = 1,
                Day = 10.0,
                FactIds = new List<string> { "fact_0", "fact_1" },
                PrefixTextId = RumorPrefixSelector.HeardFromSourceTextId,
                PrefixVars = new Dictionary<string, string> { ["SOURCE"] = "hero:hero_informant" },
                SpeakerHeroId = "hero_teller",
                SourceHeroId = "hero_informant",
                SentenceKeyCandidates = new List<string> { "VividWorld_Sentence_Test_Heard" }
            };

            store.RecordTelling(evt, PlayerEntry("hero_teller", hop: 1), Facts(evt, 0, 1), 10.0, telling);
            var provider = new ChronicleProvider(store, _ => null, new PresentationConfig());
            var entry = Assert.Single(provider.ForPlayer(10, currentDay: 20.0, out _));
            var source = Assert.Single(entry.Sources);

            Assert.True(source.HasSpokenLine);
            Assert.Equal(RumorPrefixSelector.HeardFromSourceTextId, source.Body.PrefixTextId);
            Assert.Equal("hero:hero_informant", source.Body.PrefixVars["SOURCE"]);
            Assert.Equal("hero_informant", source.Body.SourceHeroId);
        }

        [Fact]
        public void SpokenLine_Participant_PreservesSelfSentenceAndPersonalityFeeling()
        {
            var store = NewStore(out _);
            var evt = CreateEvent("evt_participant", factCount: 3);
            var telling = new PlayerHeardSource
            {
                HeroId = "hero_actor",
                Hop = 0,
                Day = 10.0,
                FactIds = new List<string> { "fact_0", "fact_1" },
                SpeakerHeroId = "hero_actor",
                SpeakerRole = "challenger",
                Roles = new Dictionary<string, string> { ["challenger"] = "hero_actor", ["target"] = "hero_rival" },
                SentenceKeyCandidates = new List<string> { "VividWorld_Sentence_Duel_Self_CHALLENGER", "VividWorld_Sentence_Duel" },
                SelfFeelingKeyCandidates = new List<string> { "VividWorld_SelfFeeling_Duel_CHALLENGER_Mercy", "VividWorld_SelfFeeling_Duel_CHALLENGER" },
                IsGist = false
            };

            store.RecordTelling(evt, PlayerEntry("hero_actor", hop: 0), Facts(evt, 0, 1), 10.0, telling);
            var provider = new ChronicleProvider(store, _ => null, new PresentationConfig());
            var entry = Assert.Single(provider.ForPlayer(10, currentDay: 20.0, out _));
            var source = Assert.Single(entry.Sources);

            Assert.True(source.HasSpokenLine);
            Assert.Equal("self feeling", telling.TrailingKind);
            Assert.Equal("VividWorld_Sentence_Duel_Self_CHALLENGER", source.Body.SentenceKeyCandidate);
            Assert.Equal(new[] { "VividWorld_SelfFeeling_Duel_CHALLENGER_Mercy", "VividWorld_SelfFeeling_Duel_CHALLENGER" }, source.Body.SelfFeelingKeyCandidates);
            Assert.Equal("challenger", source.Body.SpeakerRole);
        }

        [Fact]
        public void SpokenLine_GistHeldBack_PreservesClosingKey()
        {
            var store = NewStore(out _);
            var evt = CreateEvent("evt_gist_held", factCount: 4);
            var telling = new PlayerHeardSource
            {
                HeroId = "hero_reluctant",
                Hop = 1,
                Day = 10.0,
                FactIds = new List<string> { "fact_0" },
                SpeakerHeroId = "hero_reluctant",
                SentenceKeyCandidates = new List<string> { "VividWorld_Sentence_Duel_Gist" },
                IsGist = true,
                HeldBack = true,
                ClosingKey = "VividWorld_Closing_AskRefuseUnwilling"
            };

            store.RecordTelling(evt, PlayerEntry("hero_reluctant", hop: 1), Facts(evt, 0), 10.0, telling);
            var provider = new ChronicleProvider(store, _ => null, new PresentationConfig());
            var entry = Assert.Single(provider.ForPlayer(10, currentDay: 20.0, out _));
            var source = Assert.Single(entry.Sources);

            Assert.True(source.HasSpokenLine);
            Assert.Equal("closing", telling.TrailingKind);
            Assert.True(source.Body.IsGist);
            Assert.True(source.Body.HeldBack);
            Assert.Equal("VividWorld_Closing_AskRefuseUnwilling", source.Body.ClosingKey);
        }

        [Fact]
        public void SpokenLine_GistNotHeldBack_HasNoClosingKey()
        {
            var store = NewStore(out _);
            var evt = CreateEvent("evt_gist_not_held", factCount: 4);
            var telling = new PlayerHeardSource
            {
                HeroId = "hero_open",
                Hop = 1,
                Day = 10.0,
                FactIds = new List<string> { "fact_0" },
                SpeakerHeroId = "hero_open",
                SentenceKeyCandidates = new List<string> { "VividWorld_Sentence_Duel_Gist" },
                IsGist = true,
                HeldBack = false,
                ClosingKey = null
            };

            store.RecordTelling(evt, PlayerEntry("hero_open", hop: 1), Facts(evt, 0), 10.0, telling);
            var provider = new ChronicleProvider(store, _ => null, new PresentationConfig());
            var entry = Assert.Single(provider.ForPlayer(10, currentDay: 20.0, out _));
            var source = Assert.Single(entry.Sources);

            Assert.True(source.HasSpokenLine);
            Assert.Equal("none", telling.TrailingKind);
            Assert.True(source.Body.IsGist);
            Assert.False(source.Body.HeldBack);
            Assert.Null(source.Body.ClosingKey);
        }

        [Fact]
        public void SpokenLine_SameTeller_GistThenFull_OverwritesSpokenLineWithLatestFull()
        {
            var store = NewStore(out _);
            var evt = CreateEvent("evt_gist_then_full", factCount: 4);

            var gistTelling = new PlayerHeardSource
            {
                HeroId = "hero_friend",
                Hop = 1,
                Day = 10.0,
                FactIds = new List<string> { "fact_0" },
                SpeakerHeroId = "hero_friend",
                SentenceKeyCandidates = new List<string> { "VividWorld_Sentence_Duel_Gist" },
                IsGist = true,
                HeldBack = true,
                ClosingKey = "VividWorld_Closing_Gist"
            };
            store.RecordTelling(evt, PlayerEntry("hero_friend", 1, 10.0), Facts(evt, 0), 10.0, gistTelling);

            var fullTelling = new PlayerHeardSource
            {
                HeroId = "hero_friend",
                Hop = 1,
                Day = 15.0,
                FactIds = new List<string> { "fact_0", "fact_1", "fact_2", "fact_3" },
                SpeakerHeroId = "hero_friend",
                SentenceKeyCandidates = new List<string> { "VividWorld_Sentence_Duel_Full" },
                IsGist = false,
                HeldBack = false,
                ClosingKey = null,
                FeelingLineKey = "VividWorld_Feeling_FriendJoy",
                FeelingAddressKey = "VividWorld_Address_Test",
                FeelingFocusHeroId = "hero_friend"
            };
            var result = store.RecordTelling(evt, PlayerEntry("hero_friend", 1, 15.0), Facts(evt, 0, 1, 2, 3), 15.0, fullTelling);

            Assert.Equal(PlayerHeardSourceChange.Updated, result.SourceChange);
            var entry = store.Find("evt_gist_then_full")!;
            var savedSource = Assert.Single(entry.Sources);
            Assert.False(savedSource.IsGist);
            Assert.False(savedSource.HeldBack);
            Assert.Null(savedSource.ClosingKey);
            Assert.Equal(new[] { "VividWorld_Sentence_Duel_Full" }, savedSource.SentenceKeyCandidates);
            Assert.Equal("VividWorld_Feeling_FriendJoy", savedSource.FeelingLineKey);

            var provider = new ChronicleProvider(store, _ => null, new PresentationConfig());
            var chronicleEntry = Assert.Single(provider.ForPlayer(10, currentDay: 20.0, out _));
            var chSource = Assert.Single(chronicleEntry.Sources);
            Assert.True(chSource.HasSpokenLine);
            Assert.False(chSource.Body.IsGist);
            Assert.Equal("VividWorld_Sentence_Duel_Full", chSource.Body.SentenceKeyCandidate);
            Assert.NotNull(chSource.Feeling);
        }

        [Fact]
        public void SpokenLine_PurgedEvent_StillReconstructsQuoteFromEntryFacts()
        {
            var store = NewStore(out _);
            var evt = CreateEvent("evt_purged", factCount: 3);
            var telling = new PlayerHeardSource
            {
                HeroId = "hero_witness",
                Hop = 0,
                Day = 10.0,
                FactIds = new List<string> { "fact_0", "fact_1" },
                PrefixTextId = RumorPrefixSelector.EyewitnessTextId,
                SpeakerHeroId = "hero_witness",
                SentenceKeyCandidates = new List<string> { "VividWorld_Sentence_Purged_0_1" },
                FeelingLineKey = "VividWorld_Feeling_Purged"
            };

            store.RecordTelling(evt, PlayerEntry("hero_witness", hop: 0), Facts(evt, 0, 1), 10.0, telling);

            // 事件在記憶體中已刪除（lookup 回傳 null），仍能從 entry.Facts 與來源欄位重組出原句
            var provider = new ChronicleProvider(store, _ => null, new PresentationConfig());
            var entry = Assert.Single(provider.ForPlayer(10, currentDay: 20.0, out _));
            var source = Assert.Single(entry.Sources);

            Assert.True(source.HasSpokenLine);
            Assert.Equal("hero_witness", source.HeroId);
            Assert.Equal(RumorPrefixSelector.EyewitnessTextId, source.Body.PrefixTextId);
            Assert.Equal("VividWorld_Sentence_Purged_0_1", source.Body.SentenceKeyCandidate);
            Assert.Equal(2, source.Body.Parts.Count);
        }

        [Fact]
        public void SourcesArrayWithoutSpokenLine_LoadsAndUnknownFieldsSurvive()
        {
            var store = NewStore(out var writer);
            var evt = CreateEvent("evt_legacy_sources", factCount: 3);
            store.RecordTelling(evt, PlayerEntry("hero_legacy", hop: 2), Facts(evt, 0, 1), 10.0,
                Telling("hero_legacy", 2, 10.0, new[] { 0, 1 }, "VividWorld_Feeling_Test"));
            store.Flush();

            // 舊版格式有 sources 陣列與感想，但沒有原句欄位；另外加上未知欄位
            var json = writer.ReadAllText(File)!;
            var root = JObject.Parse(json);
            var entryObj = (JObject)root["entries"]![0]!;
            var srcObj = (JObject)entryObj["sources"]![0]!;
            srcObj.Remove("prefixTextId");
            srcObj.Remove("sentenceKeyCandidates");
            srcObj.Remove("speakerHeroId");
            srcObj["customFutureSourceField"] = "futureValue";
            writer.WriteAllText(File, root.ToString());

            var rereadStore = new PlayerHeardLogStore(File, writer);
            var load = rereadStore.Load();
            Assert.Equal(PlayerHeardLoadStatus.ReadFromFile, load.Status);
            Assert.False(rereadStore.IsDirty);

            var entry = rereadStore.Find("evt_legacy_sources")!;
            var src = Assert.Single(entry.Sources);
            Assert.False(src.HasSpokenLine);
            Assert.True(src.HasFeeling);
            Assert.Equal("futureValue", (string?)src.Extra["customFutureSourceField"]);

            var provider = new ChronicleProvider(rereadStore, _ => null, new PresentationConfig());
            var chEntry = Assert.Single(provider.ForPlayer(10, currentDay: 20.0, out _));
            var chSrc = Assert.Single(chEntry.Sources);
            Assert.False(chSrc.HasSpokenLine);

            // 寫入後未知欄位依然保留
            rereadStore.RecordTelling(evt, PlayerEntry("hero_new", 1), Facts(evt, 0, 1, 2), 12.0,
                Telling("hero_new", 1, 12.0, new[] { 2 }));
            Assert.True(rereadStore.Flush());

            var finalJson = JObject.Parse(writer.ReadAllText(File)!);
            var finalSrcObj = (JObject)finalJson["entries"]![0]!["sources"]![0]!;
            Assert.Equal("futureValue", (string?)finalSrcObj["customFutureSourceField"]);
        }

        [Fact]
        public void DialogueRendering_MatchesChronicleReconstruction_Verbatim()
        {
            var evt = new WorldEvent
            {
                EventId = "evt_duel_verbatim",
                Type = "duel",
                Day = 10.0,
                Origin = EventOrigin.Public,
                Participants = new Dictionary<string, string>
                {
                    ["challenger"] = "hero_challenger",
                    ["target"] = "hero_target"
                }
            };

            var facts = new List<Fact>
            {
                new()
                {
                    Id = "who",
                    Category = FactCategory.Who,
                    TextId = "VividWorld_Fact_DuelArranged_Who",
                    Text = "{CHALLENGER} challenged {TARGET} to a duel",
                    Vars = new Dictionary<string, string> { ["CHALLENGER"] = "hero:hero_challenger", ["TARGET"] = "hero:hero_target" }
                },
                new()
                {
                    Id = "where",
                    Category = FactCategory.Where,
                    TextId = "VividWorld_Fact_DuelArranged_Where",
                    Text = "outside the walls of {SETTLEMENT}",
                    Vars = new Dictionary<string, string> { ["SETTLEMENT"] = "settlement:town" }
                }
            };
            evt.Facts = facts;

            var cfg = new PresentationConfig
            {
                EncyclopediaLinksEnabled = true,
                WholeSentences = true,
                FactSeparator = ", ",
                SentenceEnd = "."
            };

            var prefix = RumorPrefixSelector.SelectPrefix(hop: 1, sourceHeroId: "hero_witness", isRetell: false, isCorrection: false);
            var composed = RumorTextComposer.Compose(
                evt,
                facts,
                cfg,
                prefix: prefix,
                speakerHeroId: "hero_speaker",
                sourceHeroId: "hero_witness",
                isGist: false,
                heldBack: false);

            var source = new PlayerHeardSource
            {
                HeroId = "hero_speaker",
                Hop = 2,
                Day = 10.0,
                FactIds = facts.Select(f => f.Id).ToList(),
                PrefixTextId = composed.PrefixTextId,
                PrefixVars = composed.PrefixVars != null ? new Dictionary<string, string>(composed.PrefixVars) : new Dictionary<string, string>(),
                SpeakerHeroId = composed.SpeakerHeroId,
                SpeakerRole = composed.SpeakerRole,
                SourceHeroId = composed.SourceHeroId,
                SourceRole = composed.SourceRole,
                Roles = composed.Roles != null ? new Dictionary<string, string>(composed.Roles) : new Dictionary<string, string>(),
                SentenceKeyCandidates = composed.SentenceKeyCandidates != null ? new List<string>(composed.SentenceKeyCandidates) : new List<string>(),
                SelfFeelingKeyCandidates = composed.SelfFeelingKeyCandidates != null ? new List<string>(composed.SelfFeelingKeyCandidates) : new List<string>(),
                IsGist = composed.IsGist,
                HeldBack = composed.HeldBack,
                ClosingKey = composed.ClosingKey
            };

            var templates = new Dictionary<string, string>
            {
                ["VividWorld_Prefix_HeardFromSource"] = "{SOURCE} told me that",
                ["VividWorld_Fact_DuelArranged_Who"] = "{CHALLENGER} challenged {TARGET} to a duel",
                ["VividWorld_Fact_DuelArranged_Where"] = "outside the walls of {SETTLEMENT}"
            };

            string ResolveVar(string val, bool useLinks) => val.Contains(':') ? val.Substring(val.IndexOf(':') + 1) : val;
            string GetTemplate(string? key, string? fallback) => key != null && templates.TryGetValue(key, out var t) ? t : fallback ?? string.Empty;

            var dialogueResult = RumorTextAssembler.Assemble(composed, cfg, ResolveVar, GetTemplate);

            var reconstructed = RumorTextComposer.Reconstruct(facts, source, cfg, evt.EventId);
            var chronicleResult = RumorTextAssembler.Assemble(reconstructed, cfg, ResolveVar, GetTemplate);

            Assert.Equal(dialogueResult.DisplayText, chronicleResult.DisplayText);
            Assert.Equal(dialogueResult.PlainText, chronicleResult.PlainText);
        }

        // ── 休眠排除玩家 ──

        private static RumorEngine CreateEngine()
        {
            var cfg = new VividWorldConfig();
            var rng = new SplitMix64Rng();
            var retention = FactRetentionPolicies.Create(cfg, rng, 42L);
            return new RumorEngine(cfg, retention, NullEmbellishmentPolicy.Instance, new FakePropagationChannel(),
                new FakeHeroTraitLookup(), rng, 42L, PlayerHeroId);
        }

        [Fact]
        public void Dormancy_AllNpcKnowersAtMaxHop_IgnoresThePlayersHop()
        {
            var engine = CreateEngine();
            var evt = CreateEvent("evt_dormant");
            int maxHop = engine.MaxHopFor(evt);

            evt.KnownBy.Add(new KnownByEntry { HeroId = "npc_1", Hop = maxHop });
            evt.KnownBy.Add(new KnownByEntry { HeroId = "npc_2", Hop = maxHop });
            // 玩家的手數只是距離，比最遠手數小：以前會因為他而擋住休眠
            evt.KnownBy.Add(new KnownByEntry { HeroId = PlayerHeroId, Hop = 1 });

            var reason = engine.DormancyReasonFor(evt, day: evt.Day + 1);
            Assert.NotNull(reason);
            Assert.Equal(DormancyKind.AllAtMaxHop, reason!.Kind);
            Assert.Equal(2, reason.Count);   // 只算 NPC
            Assert.Contains("the player is not counted", MemoryLogFormatter.FormatDormancy("evt_dormant", reason));
        }

        [Fact]
        public void Dormancy_PlayerAtMaxHopDoesNotPutTheRumorToSleepByItself()
        {
            var engine = CreateEngine();
            var evt = CreateEvent("evt_awake");
            int maxHop = engine.MaxHopFor(evt);

            evt.KnownBy.Add(new KnownByEntry { HeroId = "npc_1", Hop = 0 });
            evt.KnownBy.Add(new KnownByEntry { HeroId = PlayerHeroId, Hop = maxHop });

            var reason = engine.DormancyReasonFor(evt, day: evt.Day + 1);
            Assert.True(reason == null || reason.Kind != DormancyKind.AllAtMaxHop);
        }
    }
}
