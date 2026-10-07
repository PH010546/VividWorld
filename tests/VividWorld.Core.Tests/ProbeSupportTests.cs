#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using Newtonsoft.Json.Linq;
using VividWorld.Core.Catalog;
using VividWorld.Core.Config;
using VividWorld.Core.Dialogue;
using VividWorld.Core.Events;
using VividWorld.Core.Persistence;
using VividWorld.Core.Presentation;
using VividWorld.Core.Tests.Fakes;
using Xunit;

namespace VividWorld.Core.Tests
{
    /// <summary>打探的周邊：打探的回答記進紀事、每天的次數、組字、設定、字串表。</summary>
    public class ProbeSupportTests
    {
        private const string File = "player_heard.json";
        private const string Player = "hero_player";

        private static string RepoRoot()
        {
            string? current = AppContext.BaseDirectory;
            while (!string.IsNullOrEmpty(current))
            {
                if (System.IO.File.Exists(Path.Combine(current, "VividWorld.sln"))) return current!;
                current = Directory.GetParent(current!)?.FullName;
            }
            throw new DirectoryNotFoundException("repo root not found");
        }

        // ───────────── 打探的回答記進紀事 ─────────────

        private static WorldEvent Event(string id, string type = "duel", double day = 10.0)
        {
            return new WorldEvent
            {
                EventId = id,
                Type = type,
                Day = day,
                Origin = EventOrigin.Public,
                State = new RumorState { Leaked = true },
                Facts = Enumerable.Range(0, 4).Select(i => new Fact { Id = "fact_" + i, Text = "fact " + i, TextId = "VividWorld_Fact_Test_" + i, Category = FactCategory.What, Fragility = i + 1 }).ToList()
            };
        }

        private static PlayerHeardLogStore NewStore(out FailingFileWriter writer)
        {
            writer = new FailingFileWriter();
            var store = new PlayerHeardLogStore(File, writer);
            store.Load();
            return store;
        }

        private static void Told(PlayerHeardLogStore store, WorldEvent evt, string teller, int hop, double day, params int[] facts)
        {
            var playerEntry = new KnownByEntry { HeroId = Player, Hop = hop, SourceHeroId = teller, LearnedDay = day };
            var told = facts.Select(i => evt.Facts[i]).ToList();
            store.RecordTelling(evt, playerEntry, told, day, new PlayerHeardSource
            {
                HeroId = teller,
                Hop = hop,
                Day = day,
                FactIds = told.Select(f => f.Id).ToList()
            });
        }

        [Fact]
        public void ProbeAnswer_IsAddedToTheRightEntry_WithoutTouchingHopFactsOrDays()
        {
            var store = NewStore(out _);
            var evt = Event("evt_a");
            Told(store, evt, "hero_a", 2, 20.0, 0, 1);
            var other = Event("evt_b");
            Told(store, other, "hero_b", 1, 21.0, 0);

            var before = store.Find("evt_a")!;
            int hop = before.PlayerHop;
            double learned = before.LearnedDay;
            double updated = before.UpdatedDay;
            var factIds = before.Facts.Select(f => f.Id).ToList();

            bool added = store.RecordProbeAnswer("evt_a", "hero_probed", 30.0, "VividWorld_Probe_Accused_DenyNamed",
                new Dictionary<string, string> { ["ORIGINATOR"] = "hero_orig" }, "VividWorld_Address_Equal_Neutral", "hero_x");

            Assert.True(added);
            var entry = store.Find("evt_a")!;
            Assert.Equal(2, entry.Sources.Count);
            var probe = entry.Sources[1];
            Assert.True(probe.HasProbeAnswer);
            Assert.Equal("hero_probed", probe.HeroId);
            Assert.Equal(30.0, probe.Day);
            Assert.Equal("VividWorld_Probe_Accused_DenyNamed", probe.ProbeAnswerKey);
            Assert.Equal("hero_orig", probe.ProbeAnswerVars!["ORIGINATOR"]);
            Assert.Equal("VividWorld_Address_Equal_Neutral", probe.ProbeAddressKey);
            Assert.Equal("hero_x", probe.ProbeAddressHeroId);
            Assert.False(probe.HasSpokenLine);

            Assert.Equal(hop, entry.PlayerHop);
            Assert.Equal(learned, entry.LearnedDay);
            Assert.Equal(updated, entry.UpdatedDay);
            Assert.Equal(factIds, entry.Facts.Select(f => f.Id).ToList());
            Assert.Single(store.Find("evt_b")!.Sources);          // 別的那則沒有被動到
            Assert.True(store.IsDirty);
        }

        [Fact]
        public void ProbeAnswer_ForAnUnknownEvent_RecordsNothing()
        {
            var store = NewStore(out _);
            Assert.False(store.RecordProbeAnswer("nope", "hero", 1.0, "VividWorld_Probe_NotHeard_1", null, null, null));
            Assert.False(store.IsDirty);
        }

        [Fact]
        public void ProbeAnswer_SameAnswerAgain_OnlyRefreshesTheDay_AndADifferentOneIsAnotherSource()
        {
            var store = NewStore(out _);
            Told(store, Event("evt_a"), "hero_a", 2, 20.0, 0);

            Assert.True(store.RecordProbeAnswer("evt_a", "h", 30.0, "VividWorld_Probe_Refuse_1", null, null, null));
            Assert.False(store.RecordProbeAnswer("evt_a", "h", 31.0, "VividWorld_Probe_Refuse_1", null, null, null));
            Assert.Equal(2, store.Find("evt_a")!.Sources.Count);
            Assert.Equal(31.0, store.Find("evt_a")!.Sources[1].Day);

            Assert.True(store.RecordProbeAnswer("evt_a", "h", 32.0, "VividWorld_Probe_Refuse_2", null, null, null));
            Assert.Equal(3, store.Find("evt_a")!.Sources.Count);
        }

        [Fact]
        public void ProbeAnswer_IsNotMergedIntoANewTellingFromTheSameMan_AndDoesNotCountAsHavingToldTheNews()
        {
            var store = NewStore(out _);
            var evt = Event("evt_a");
            Told(store, evt, "hero_a", 2, 20.0, 0);
            store.RecordProbeAnswer("evt_a", "hero_b", 25.0, "VividWorld_Probe_Refuse_1", null, null, null);
            Assert.False(store.DidTellerTellPlayer("evt_a", "hero_b"));     // 打探的回答不算他告訴過玩家這件事
            Assert.True(store.DidTellerTellPlayer("evt_a", "hero_a"));

            // 同一個人之後真的講了這件事：另開一份，打探的那一份原樣留著
            Told(store, evt, "hero_b", 1, 40.0, 0, 1);
            var sources = store.Find("evt_a")!.Sources;
            Assert.Equal(3, sources.Count);
            var probe = sources.Single(s => s.HasProbeAnswer);
            Assert.Equal("VividWorld_Probe_Refuse_1", probe.ProbeAnswerKey);
            Assert.Equal(25.0, probe.Day);
            Assert.True(store.DidTellerTellPlayer("evt_a", "hero_b"));
        }

        [Fact]
        public void ProbeAnswer_OnAnOldRecordWithoutSources_KeepsTheOriginalSource()
        {
            // 舊檔：沒有來源清單
            var scratch = NewStore(out _);
            Told(scratch, Event("evt_old"), "hero_old", 3, 12.0, 0, 1);
            var log = new PlayerHeardLog { Entries = new List<PlayerHeardEntry> { scratch.Find("evt_old")! } };
            var root = JObject.Parse(VividJson.Write(log));
            foreach (var e in root["entries"]!.Children<JObject>()) e.Remove("sources");

            var writer = new FailingFileWriter();
            writer.WriteAllText(File, root.ToString());
            var store = new PlayerHeardLogStore(File, writer);
            store.Load();
            Assert.False(store.IsDirty);

            Assert.True(store.RecordProbeAnswer("evt_old", "hero_probed", 30.0, "VividWorld_Probe_NotHeard_1", null, null, null));
            var sources = store.Find("evt_old")!.Sources;
            Assert.Equal(2, sources.Count);
            Assert.Equal("hero_old", sources[0].HeroId);          // 原本那一份還在
            Assert.False(sources[0].HasProbeAnswer);
            Assert.Equal(3, sources[0].Hop);
            Assert.True(sources[1].HasProbeAnswer);
        }

        [Fact]
        public void ProbeAnswer_SurvivesSaveAndLoad_AndOldFilesWithoutTheFieldsLoadAsNotProbeAnswers()
        {
            var store = NewStore(out var writer);
            Told(store, Event("evt_a"), "hero_a", 2, 20.0, 0);
            store.RecordProbeAnswer("evt_a", "hero_p", 30.0, "VividWorld_Probe_Reason_TraitFit_Believe",
                new Dictionary<string, string> { ["SOURCE"] = "hero_s" }, "VividWorld_Address_Higher_High", "hero_acc");
            Assert.True(store.Flush());

            string json = writer.ReadAllText(File)!;
            var sourcesJson = JObject.Parse(json)["entries"]![0]!["sources"]!;
            // 沒有打探欄位的那一份不寫這些欄位（null 與空的都不進檔案）
            Assert.DoesNotContain("probe", sourcesJson[0]!.ToString(), StringComparison.OrdinalIgnoreCase);
            Assert.Contains("probe", sourcesJson[1]!.ToString(), StringComparison.OrdinalIgnoreCase);

            var reread = new PlayerHeardLogStore(File, writer);
            reread.Load();
            var sources = reread.Find("evt_a")!.Sources;
            Assert.False(sources[0].HasProbeAnswer);
            Assert.Null(sources[0].ProbeAnswerVars);
            Assert.True(sources[1].HasProbeAnswer);
            Assert.Equal("VividWorld_Probe_Reason_TraitFit_Believe", sources[1].ProbeAnswerKey);
            Assert.Equal("hero_s", sources[1].ProbeAnswerVars!["SOURCE"]);
            Assert.Equal("VividWorld_Address_Higher_High", sources[1].ProbeAddressKey);
            Assert.Equal("hero_acc", sources[1].ProbeAddressHeroId);
        }

        [Fact]
        public void Chronicle_ShowsTheProbeAnswerAsASource_RebuildsTheSentence_AndDoesNotMoveTheBlock()
        {
            var store = NewStore(out _);
            var a = Event("evt_a");
            var b = Event("evt_b");
            Told(store, a, "hero_a", 2, 20.0, 0, 1);
            Told(store, b, "hero_b", 1, 25.0, 0);

            ChronicleProvider Provider() => new ChronicleProvider(store, _ => new EventTemplate { Type = "duel", Headline = "A duel" }, new PresentationConfig());
            var before = Provider().ForPlayer(10, 100.0, out _);
            Assert.Equal(new[] { "evt_b", "evt_a" }, before.Select(x => x.EventId).ToArray());   // 新的在前

            store.RecordProbeAnswer("evt_a", "hero_p", 90.0, "VividWorld_Probe_Accused_DenyNamed",
                new Dictionary<string, string> { ["ORIGINATOR"] = "hero_o" }, null, null);

            var after = Provider().ForPlayer(10, 100.0, out _);
            Assert.Equal(new[] { "evt_b", "evt_a" }, after.Select(x => x.EventId).ToArray());    // 打探沒有把區塊推到最前面
            var block = after.Single(x => x.EventId == "evt_a");
            Assert.Equal(20.0, block.LearnedDay);
            Assert.Equal(2, block.Sources.Count);
            Assert.False(block.Sources[0].HasProbeAnswer);

            var probe = block.Sources[1];
            Assert.True(probe.HasProbeAnswer);
            Assert.Equal("hero_p", probe.HeroId);
            Assert.Equal(0, probe.Hop);

            // 重組得出原句（跟對話當下用同一個組字函式）
            var table = new Dictionary<string, string>
            {
                ["VividWorld_Probe_Accused_DenyNamed"] = "There's no truth to it. I know it's {ORIGINATOR} spreading this, and I never did any such thing."
            };
            var rebuilt = RumorTextAssembler.AssembleProbeResponse(
                probe.ProbeAnswerKey!, probe.ProbeAnswerVars, probe.ProbeAddressKey, probe.ProbeAddressHeroId,
                probe.HeroId, Player, false,
                (v, links) => v.StartsWith("hero:") ? "Name_" + v.Substring(5) : v,
                (k, fb) => table.TryGetValue(k ?? "", out var t) ? t : fb,
                null, null, null);
            Assert.Equal("There's no truth to it. I know it's Name_hero_o spreading this, and I never did any such thing.", rebuilt.PlainText);
        }

        // ───────────── 每天的次數 ─────────────

        [Fact]
        public void ProbeLimit_CapAndUnlimited_AndNewDayStartsOverAndNotCountedAsShares()
        {
            var probes = new HeroShareLedger();
            var shares = new HeroShareLedger();
            double day = 10.4;

            Assert.False(probes.IsAtCap("h", day, 1));
            probes.Record("h", day);
            Assert.True(probes.IsAtCap("h", day, 1));            // 預設每天 1 件
            Assert.False(probes.IsAtCap("h", day, 2));
            probes.Record("h", day);
            Assert.True(probes.IsAtCap("h", day, 2));

            Assert.False(probes.IsAtCap("h", day, 0));           // ≤ 0 ＝ 不限
            Assert.False(probes.IsAtCap("h", day, -3));

            Assert.False(probes.IsAtCap("h", day + 1, 1));       // 隔天歸零
            Assert.False(probes.IsAtCap("other", day, 1));       // 別人不受影響

            Assert.Equal(0, shares.SharedOn("h", day));          // 打探不佔每天分享的那一則（兩本帳分開）
        }

        [Fact]
        public void ProbeLedger_IsItsOwnFile_AndSurvivesSaveAndLoad()
        {
            var writer = new FailingFileWriter();
            var store = new HeroShareStore("probes.json", writer);
            var ledger = store.Load();
            Assert.False(store.FileExisted);
            ledger.Record("h", 10.2);
            Assert.True(store.Save(ledger));

            var again = new HeroShareStore("probes.json", writer);
            var loaded = again.Load();
            Assert.True(again.FileExisted);
            Assert.Equal(1, loaded.SharedOn("h", 10.7));
        }

        // ───────────── 組字 ─────────────

        private static readonly Dictionary<string, string> Strings = new()
        {
            ["VividWorld_Address_Test"] = "{NAME}",
            ["VividWorld_Address_Higher_Neutral"] = "Lord {NAME}",
            ["T_Address"] = "I know what kind of person {ADDRESS} is. {He} did it, I'd say {his} word is no good.",
            ["T_Source"] = "It was {SOURCE} who told me, and {SOURCE:his|her} word has always been good.",
            ["T_SourceThree"] = "{SOURCE:he|she|they} said so.",
            ["T_Roles"] = "I captured {PRISONER}, and {PRISONER.he} fought {PRISONER.his} way to the end. {CAPTOR} saw {PRISONER.him}.",
            ["T_Broken"] = "I never heard {NOBODY} say that.",
            ["T_BrokenWithAddress"] = "{ADDRESS} did nothing of the kind."
        };

        private static RumorRenderResult Assemble(string key, Dictionary<string, string>? vars, string? addressKey, string? addressHeroId,
            Func<string, bool?>? isFemale = null, string? english = null, List<string>? warnings = null)
        {
            return RumorTextAssembler.AssembleProbeResponse(
                key, vars, addressKey, addressHeroId, "speaker", Player, false,
                (v, links) => v.StartsWith("hero:", StringComparison.Ordinal) ? "N(" + v.Substring(5) + ")" : v,
                (k, fb) => Strings.TryGetValue(k ?? "", out var t) ? t : fb,
                isFemale, w => warnings?.Add(w), english);
        }

        [Fact]
        public void Assemble_AddressAndFocusPronouns_FollowTheAddressedHero()
        {
            var male = Assemble("T_Address", null, "VividWorld_Address_Higher_Neutral", "acc", id => false);
            Assert.Equal("I know what kind of person Lord N(acc) is. He did it, I'd say his word is no good.", male.PlainText);

            var female = Assemble("T_Address", null, "VividWorld_Address_Higher_Neutral", "acc", id => true);
            Assert.Equal("I know what kind of person Lord N(acc) is. She did it, I'd say her word is no good.", female.PlainText);
        }

        [Fact]
        public void Assemble_GenderSelectTokens_ForTheSourceHero()
        {
            var vars = new Dictionary<string, string> { ["SOURCE"] = "src" };
            Assert.Equal("It was N(src) who told me, and his word has always been good.",
                Assemble("T_Source", vars, null, null, id => id == "src" ? false : (bool?)null).PlainText);
            Assert.Equal("It was N(src) who told me, and her word has always been good.",
                Assemble("T_Source", vars, null, null, id => id == "src" ? true : (bool?)null).PlainText);
            // 查不到性別：有第三段用第三段、沒有用第一段
            Assert.Equal("they said so.", Assemble("T_SourceThree", vars, null, null, id => null).PlainText);
            Assert.Equal("It was N(src) who told me, and his word has always been good.",
                Assemble("T_Source", vars, null, null, id => null).PlainText);
        }

        [Fact]
        public void Assemble_RolePlaceholdersAndRolePronouns()
        {
            var vars = new Dictionary<string, string> { ["PRISONER"] = "pr", ["CAPTOR"] = "cp" };
            Func<string, bool?> female = id => id == "pr";
            Assert.Equal("I captured N(pr), and she fought her way to the end. N(cp) saw her.",
                Assemble("T_Roles", vars, null, null, female).PlainText);
            Assert.Equal("I captured N(pr), and he fought his way to the end. N(cp) saw him.",
                Assemble("T_Roles", vars, null, null, id => false).PlainText);
        }

        [Fact]
        public void Assemble_LeftoverBrace_FallsBackToTheEnglishDefault_AndWarns_NeverLeavesABrace()
        {
            var warnings = new List<string>();
            var result = Assemble("T_Broken", null, null, null, null, english: "I never heard anyone say that.", warnings: warnings);
            Assert.Equal("I never heard anyone say that.", result.PlainText);
            Assert.DoesNotContain("{", result.PlainText);
            Assert.Contains(warnings, w => w.Contains("NOBODY"));

            // 英文預設也有殘留，或根本沒有英文預設：記號被拿掉，不把 { 送給玩家
            var noEnglish = Assemble("T_Broken", null, null, null, null, english: null);
            Assert.DoesNotContain("{", noEnglish.PlainText);
            Assert.DoesNotContain("}", noEnglish.PlainText);
        }

        [Fact]
        public void Assemble_MissingAddress_DoesNotProduceABrokenSentence_OrAStrayBrace()
        {
            var warnings = new List<string>();
            var result = Assemble("T_BrokenWithAddress", null, null, null, null, english: null, warnings: warnings);
            Assert.DoesNotContain("{", result.PlainText);
            Assert.Contains(warnings, w => w.Contains("ADDRESS"));
        }

        [Fact]
        public void Assemble_NeverAddsAClosingPunctuation_TheStringAlreadyHasOne()
        {
            Strings["T_EndsOnce"] = "There's no truth to it.";
            Assert.Equal("There's no truth to it.", Assemble("T_EndsOnce", null, null, null).PlainText);
            Strings["T_EndsQuote"] = "Ask me something else.";
            Assert.Equal("Ask me something else.", Assemble("T_EndsQuote", null, null, null).PlainText);
        }

        [Fact]
        public void Assemble_AddressAtTheStart_IsCapitalizedInEnglish()
        {
            Strings["T_StartsWithAddress"] = "{ADDRESS} did nothing of the kind.";
            Strings["VividWorld_Address_Lower"] = "my friend {NAME}";
            var result = Assemble("T_StartsWithAddress", null, "VividWorld_Address_Lower", "acc");
            Assert.Equal("My friend N(acc) did nothing of the kind.", result.PlainText);
        }

        // ───────────── 設定 ─────────────

        [Fact]
        public void Config_NewKeys_Defaults_Clamp_AndGrowIntoAnExistingFile()
        {
            var cfg = new VividWorldConfig();
            Assert.Equal(1, cfg.Dialogue.ProbesPerHeroPerDay);
            Assert.Equal(10.0, cfg.FalseRumors.Slip.BaseChance);
            Assert.Equal(20.0, cfg.FalseRumors.Slip.RashBonus);
            Assert.Equal(-10.0, cfg.FalseRumors.Slip.CalculatingBonus);

            // 夾限
            var wild = new VividWorldConfig();
            wild.Dialogue.ProbesPerHeroPerDay = 99;
            wild.FalseRumors.Slip.BaseChance = 500;
            wild.FalseRumors.Slip.RashBonus = -500;
            wild.FalseRumors.Slip.CalculatingBonus = 500;
            wild.Normalize();
            Assert.Equal(10, wild.Dialogue.ProbesPerHeroPerDay);
            Assert.Equal(100.0, wild.FalseRumors.Slip.BaseChance);
            Assert.Equal(-100.0, wild.FalseRumors.Slip.RashBonus);
            Assert.Equal(100.0, wild.FalseRumors.Slip.CalculatingBonus);

            var negative = new VividWorldConfig();
            negative.Dialogue.ProbesPerHeroPerDay = -4;
            negative.Normalize();
            Assert.Equal(0, negative.Dialogue.ProbesPerHeroPerDay);          // ≤ 0 ＝ 不限，夾在 0

            // 既有檔案沒有這幾個鍵：補進去，而且不動既有的值
            var existing = JObject.Parse(@"{ ""configVersion"": 1, ""dialogue"": { ""sharesPerHeroPerDay"": 3 }, ""falseRumors"": { ""maxPerDay"": 7 } }");
            var canonical = JObject.Parse(VividJson.Write(new VividWorldConfig()));
            var merged = ConfigMerge.AddMissingKeys(existing, canonical);
            Assert.Contains("dialogue.probesPerHeroPerDay", merged.AddedPaths);
            Assert.Contains("falseRumors.slip.baseChance", merged.AddedPaths);
            Assert.Contains("falseRumors.slip.rashBonus", merged.AddedPaths);
            Assert.Contains("falseRumors.slip.calculatingBonus", merged.AddedPaths);
            Assert.Equal(1, (int)merged.Merged["dialogue"]!["probesPerHeroPerDay"]!);
            Assert.Equal(10.0, (double)merged.Merged["falseRumors"]!["slip"]!["baseChance"]!);
            Assert.Equal(3, (int)merged.Merged["dialogue"]!["sharesPerHeroPerDay"]!);
            Assert.Equal(7, (int)merged.Merged["falseRumors"]!["maxPerDay"]!);

            // 讀回來
            var read = VividJson.Read<VividWorldConfig>(merged.Merged.ToString())!;
            Assert.Equal(1, read.Dialogue.ProbesPerHeroPerDay);
            Assert.Equal(-10.0, read.FalseRumors.Slip.CalculatingBonus);
        }

        [Fact]
        public void Config_ProbesPerHeroPerDay_IsInTheSettingsMenu_WithRangeOneToTen_AndRoundTrips()
        {
            var key = McmExposedKeys.All.Single(k => k.Path == "dialogue.probesPerHeroPerDay");
            Assert.Equal(McmKeyKind.Int, key.Kind);

            var cfg = new VividWorldConfig();
            Assert.Equal(1, McmExposedKeys.Read(cfg, "dialogue.probesPerHeroPerDay"));
            Assert.True(McmExposedKeys.Write(cfg, "dialogue.probesPerHeroPerDay", 4));
            Assert.Equal(4, cfg.Dialogue.ProbesPerHeroPerDay);
        }

        // ───────────── 字串表 ─────────────

        private static readonly string[] ProbeKeys =
        {
            "VividWorld_Probe_Option", "VividWorld_Probe_Prompt", "VividWorld_Probe_WindowTitle", "VividWorld_Probe_Confirm",
            "VividWorld_Probe_Cancel", "VividWorld_Probe_CancelReply", "VividWorld_Probe_NotHeard_1", "VividWorld_Probe_NotHeard_2",
            "VividWorld_Probe_Reason_SubjectRelation_Believe", "VividWorld_Probe_Reason_SubjectRelation_Disbelieve",
            "VividWorld_Probe_Reason_TellerRelation_Believe", "VividWorld_Probe_Reason_TellerRelation_Disbelieve",
            "VividWorld_Probe_Reason_TraitFit_Believe", "VividWorld_Probe_Reason_TraitFit_Disbelieve",
            "VividWorld_Probe_Reason_ListenerNature_Believe", "VividWorld_Probe_Reason_ListenerNature_Disbelieve",
            "VividWorld_Probe_Reason_Distance_Disbelieve", "VividWorld_Probe_Reason_None_Believe", "VividWorld_Probe_Reason_None_Disbelieve",
            "VividWorld_Probe_Truth_RashCaptureByCaptor", "VividWorld_Probe_Truth_RashCaptureByBystander",
            "VividWorld_Probe_Truth_MistreatedByPrisoner", "VividWorld_Probe_Truth_RefusedAidByAsker",
            "VividWorld_Probe_Truth_NotSo_VictoryCreditDeferred", "VividWorld_Probe_Truth_NotSo_AdviceGivenFreely",
            "VividWorld_Probe_Truth_NotSo_BrawlManHandedOver", "VividWorld_Probe_Truth_NotSo_SeatDisputeYielded",
            "VividWorld_Probe_Truth_NotSo_TavernGoodWord",
            "VividWorld_Probe_Accused_DenyNamed", "VividWorld_Probe_Accused_DenyUnnamed", "VividWorld_Probe_Accused_PraiseSubject",
            "VividWorld_Probe_Refuse_1", "VividWorld_Probe_Refuse_2",
            "VividWorld_Probe_Insist_1", "VividWorld_Probe_Insist_2",
            "VividWorld_Probe_Slip_Grudge_1", "VividWorld_Probe_Slip_Grudge_2", "VividWorld_Probe_Slip_Rivalry", "VividWorld_Probe_Slip_Praise",
            "VividWorld_Probe_Insist_Boast", "VividWorld_Probe_Slip_Boast",
            "VividWorld_MCM_ProbesPerHeroPerDay", "VividWorld_MCM_ProbesPerHeroPerDayHint", "VividWorld_Dev_ProbePreview"
        };

        private static Dictionary<string, string> LoadStrings(params string[] relative)
        {
            string path = Path.Combine(new[] { RepoRoot(), "module", "ModuleData", "Languages" }.Concat(relative).ToArray());
            var doc = XDocument.Load(path);
            return doc.Descendants("string")
                .Where(e => e.Attribute("id") != null)
                .ToDictionary(e => e.Attribute("id")!.Value, e => e.Attribute("text")?.Value ?? string.Empty);
        }

        [Fact]
        public void EveryProbeStringKey_IsInBothTables_AndTheChineseOneIsChinese()
        {
            var en = LoadStrings("std_module_strings_xml.xml");
            var zh = LoadStrings("CNt", "std_module_strings_xml.xml");
            foreach (var key in ProbeKeys)
            {
                Assert.True(en.TryGetValue(key, out var enText) && enText.Length > 0, $"missing or empty in English table: {key}");
                Assert.True(zh.TryGetValue(key, out var zhText) && zhText.Length > 0, $"missing or empty in Chinese table: {key}");
                Assert.True(zhText!.Any(c => c >= '一' && c <= '鿿'), $"Chinese table text has no Chinese characters: {key}");
                Assert.False(enText!.Any(c => c >= '一' && c <= '鿿'), $"English table text has Chinese characters: {key}");
            }
        }

        [Fact]
        public void ProbeStrings_PlaceholdersMatchBetweenTheTwoTables_AndTheOldPickListExperimentIsGone()
        {
            var en = LoadStrings("std_module_strings_xml.xml");
            var zh = LoadStrings("CNt", "std_module_strings_xml.xml");
            // 代換用的佔位符（全大寫的名字）兩邊要一致；{he}／{his} 這類代名詞記號與 {X:a|b}、{角色.he} 另算
            var braces = new System.Text.RegularExpressions.Regex(@"\{[A-Z_]+\}");
            foreach (var key in ProbeKeys)
            {
                var enSet = braces.Matches(en[key]).Select(m => m.Value).Distinct().OrderBy(x => x, StringComparer.Ordinal).ToArray();
                var zhSet = braces.Matches(zh[key]).Select(m => m.Value).Distinct().OrderBy(x => x, StringComparer.Ordinal).ToArray();
                Assert.True(enSet.SequenceEqual(zhSet), $"placeholders differ in {key}: en [{string.Join(",", enSet)}] zh [{string.Join(",", zhSet)}]");
            }

            Assert.False(en.ContainsKey("VividWorld_Dev_PickList"));
            Assert.False(zh.ContainsKey("VividWorld_Dev_PickList"));
        }

        [Fact]
        public void EveryProbeSentenceKeyTheClassifierCanReturn_IsInTheStringTable()
        {
            var en = LoadStrings("std_module_strings_xml.xml");
            var returned = new List<string>
            {
                "VividWorld_Probe_Accused_DenyNamed", "VividWorld_Probe_Accused_DenyUnnamed", "VividWorld_Probe_Accused_PraiseSubject",
                "VividWorld_Probe_Refuse_1", "VividWorld_Probe_Refuse_2", "VividWorld_Probe_NotHeard_1", "VividWorld_Probe_NotHeard_2"
            };
            foreach (var reason in new[] { "SubjectRelation", "TellerRelation", "TraitFit", "ListenerNature", "Distance", "None", null })
            {
                foreach (var believes in new[] { true, false })
                {
                    foreach (var source in new[] { "src", null })
                    {
                        returned.Add(VividWorld.Core.Rumors.ProbeClassifier.MapTwoVersionsSentenceKey(believes, reason, source, Player));
                    }
                }
            }
            foreach (var response in new[] { "talk_corrected_rash_capture_by_captor", "talk_corrected_rash_capture_by_bystander", "talk_corrected_mistreated_by_prisoner",
                "talk_corrected_refused_aid_by_asker", "talk_not_so_victory_credit_deferred", "talk_not_so_advice_given_freely", "talk_not_so_brawl_man_handed_over",
                "talk_not_so_seat_dispute_yielded", "talk_not_so_tavern_good_word" })
            {
                returned.Add(VividWorld.Core.Rumors.ProbeClassifier.TruthFaceKey(response)!);
            }

            foreach (var key in returned.Distinct())
            {
                Assert.True(en.ContainsKey(key), $"the classifier can return '{key}' but the string table has no such key");
            }
        }
    }
}
