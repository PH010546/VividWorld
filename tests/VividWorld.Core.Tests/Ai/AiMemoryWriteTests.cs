#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using VividWorld.Core.Ai;
using VividWorld.Core.Catalog;
using VividWorld.Core.Config;
using VividWorld.Core.Events;
using VividWorld.Core.Memory;
using VividWorld.Core.Persistence;
using VividWorld.Core.Rumors;
using VividWorld.Core.Tests.Fakes;
using Xunit;

namespace VividWorld.Core.Tests
{
    public class AiMemoryWriteTests
    {
        private const string Hero = "lord_a";
        private const string Player = "player_hero";

        private static WorldEvent MakeEvent(
            string type = "sample_event",
            EventOrigin origin = EventOrigin.Public,
            bool fabricated = false,
            bool heroKnows = true,
            bool playerIsParticipant = false,
            string eventId = "evt_1")
        {
            var participants = new Dictionary<string, string> { ["actor"] = Hero, ["target"] = "lord_b" };
            if (playerIsParticipant) participants["helper"] = Player;

            var evt = new WorldEvent
            {
                EventId = eventId,
                Type = type,
                Day = 10.0,
                Origin = origin,
                Fabricated = fabricated,
                Participants = participants,
                Facts = new List<Fact>
                {
                    new Fact { Id = "f1", Category = FactCategory.Who, TextId = "VividWorld_Fact_Who", Text = "Who" }
                },
                KnownBy = new List<KnownByEntry>()
            };
            if (heroKnows)
            {
                evt.KnownBy.Add(new KnownByEntry { HeroId = Hero, Hop = 0, LearnedDay = 10.0 });
            }
            return evt;
        }

        private static Func<string, EventTemplate?> Templates(EventTemplate? template)
            => _ => template;

        private static readonly Func<string, string, bool> NothingWritten = (_, __) => false;

        private static AiMemoryWriteDecision Decide(
            WorldEvent evt,
            string heroId = Hero,
            EventTemplate? template = null,
            Func<string, string, bool>? written = null)
            => AiMemoryWritePolicy.Decide(evt, heroId, Player, Templates(template), written ?? NothingWritten);

        // ---- 八條原因，各一條 ----

        [Fact]
        public void Reason1_Player_IsSkipped()
        {
            var evt = MakeEvent(playerIsParticipant: true);
            var d = Decide(evt, Player);
            Assert.False(d.Write);
            Assert.Equal(AiMemorySkipReason.Player, d.Reason);
            Assert.Equal("player", d.ReasonText);
        }

        [Fact]
        public void Reason2_NotAParticipant_IsSkipped()
        {
            var evt = MakeEvent();
            evt.KnownBy.Add(new KnownByEntry { HeroId = "bystander", Hop = 0, LearnedDay = 10.0 });
            var d = Decide(evt, "bystander");
            Assert.False(d.Write);
            Assert.Equal(AiMemorySkipReason.NotParticipant, d.Reason);
            Assert.Equal("not a participant", d.ReasonText);
        }

        [Fact]
        public void Reason3_NativeEvent_IsSkipped()
        {
            var d = Decide(MakeEvent(), template: new EventTemplate { Type = "sample_event", FromNativeEvent = true });
            Assert.False(d.Write);
            Assert.Equal(AiMemorySkipReason.NativeEvent, d.Reason);
            Assert.Equal("native event, recorded by the AI mod itself", d.ReasonText);
        }

        [Fact]
        public void Reason4_MadeUpTalk_IsSkipped()
        {
            var d = Decide(MakeEvent(fabricated: true));
            Assert.False(d.Write);
            Assert.Equal(AiMemorySkipReason.MadeUpTalk, d.Reason);
            Assert.Equal("made-up talk", d.ReasonText);
        }

        [Fact]
        public void Reason5_Secret_IsSkipped_EvenWhenLeaked()
        {
            var evt = MakeEvent(origin: EventOrigin.Secret);
            evt.State = new RumorState { Leaked = true };
            var d = Decide(evt);
            Assert.False(d.Write);
            Assert.Equal(AiMemorySkipReason.Secret, d.Reason);
            Assert.Equal("secret", d.ReasonText);
        }

        [Fact]
        public void Reason6_RetiredType_IsSkipped()
        {
            var d = Decide(MakeEvent(), template: new EventTemplate { Type = "sample_event", Retired = true });
            Assert.False(d.Write);
            Assert.Equal(AiMemorySkipReason.RetiredType, d.Reason);
            Assert.Equal("retired type", d.ReasonText);
        }

        [Fact]
        public void Reason7_DoesNotKnowIt_IsSkipped()
        {
            var d = Decide(MakeEvent(heroKnows: false));
            Assert.False(d.Write);
            Assert.Equal(AiMemorySkipReason.DoesNotKnow, d.Reason);
            Assert.Equal("does not know it", d.ReasonText);
        }

        [Fact]
        public void Reason8_AlreadyWritten_IsSkipped_AndNotWrittenTwice()
        {
            var store = new AiPushedStore();
            var evt = MakeEvent();
            Func<string, string, bool> isWritten = (h, e) =>
                store.HasPushedVersion(AiMemoryWritePolicy.TargetId, h, e, AiMemoryWritePolicy.RecordVersion);

            var first = Decide(evt, written: isWritten);
            Assert.True(first.Write);
            Assert.Equal(AiMemorySkipReason.None, first.Reason);

            store.Record(AiMemoryWritePolicy.TargetId, Hero, evt.EventId, AiMemoryWritePolicy.RecordVersion, 10.0);

            var second = Decide(evt, written: isWritten);
            Assert.False(second.Write);
            Assert.Equal(AiMemorySkipReason.AlreadyWritten, second.Reason);
            Assert.Equal("already written", second.ReasonText);
        }

        [Fact]
        public void Decide_ChecksInTheDocumentedOrder()
        {
            // 每一條同時成立時，勝出的是排最前面的那一條
            var template = new EventTemplate { Type = "sample_event", FromNativeEvent = true, Retired = true };
            var evt = MakeEvent(origin: EventOrigin.Secret, fabricated: true, heroKnows: false, playerIsParticipant: true);

            Assert.Equal(AiMemorySkipReason.Player, Decide(evt, Player, template, (_, __) => true).Reason);
            Assert.Equal(AiMemorySkipReason.NotParticipant, Decide(evt, "nobody", template, (_, __) => true).Reason);
            Assert.Equal(AiMemorySkipReason.NativeEvent, Decide(evt, Hero, template, (_, __) => true).Reason);
            template.FromNativeEvent = false;
            Assert.Equal(AiMemorySkipReason.MadeUpTalk, Decide(evt, Hero, template, (_, __) => true).Reason);
            evt.Fabricated = false;
            Assert.Equal(AiMemorySkipReason.Secret, Decide(evt, Hero, template, (_, __) => true).Reason);
            evt.Origin = EventOrigin.Public;
            Assert.Equal(AiMemorySkipReason.RetiredType, Decide(evt, Hero, template, (_, __) => true).Reason);
            template.Retired = false;
            Assert.Equal(AiMemorySkipReason.DoesNotKnow, Decide(evt, Hero, template, (_, __) => true).Reason);
            evt.KnownBy.Add(new KnownByEntry { HeroId = Hero, Hop = 0, LearnedDay = 10.0 });
            Assert.Equal(AiMemorySkipReason.AlreadyWritten, Decide(evt, Hero, template, (_, __) => true).Reason);
        }

        // ---- 種類、私下、天數 ----

        [Fact]
        public void Kind_PlayerAlsoAParticipant_IsOther_OtherwiseOwnLife()
        {
            Assert.Equal(AiMemoryKind.Other, AiMemoryKind.For(MakeEvent(playerIsParticipant: true), Hero, Player, 0));
            Assert.Equal(AiMemoryKind.OwnLife, AiMemoryKind.For(MakeEvent(), Hero, Player, 0));
        }

        [Fact]
        public void Private_WhenHeWontTellItHimself()
        {
            var evt = MakeEvent();
            var never = new EventTemplate
            {
                Type = "sample_event",
                SelfTell = new Dictionary<string, SelfTellRule> { ["actor"] = SelfTellRule.Never }
            };
            var open = new EventTemplate { Type = "sample_event" };

            Assert.True(AiMemoryWritePolicy.IsPrivate(evt, Hero, Templates(never), new FakeHeroTraitLookup()));
            Assert.False(AiMemoryWritePolicy.IsPrivate(evt, Hero, Templates(open), new FakeHeroTraitLookup()));
            // 他不講的那一則照樣可以寫（決定的七條裡沒有「自己不講」）
            Assert.True(Decide(evt, template: never).Write);
        }

        [Theory]
        [InlineData(10.0, 10.0, 0)]
        [InlineData(13.9, 10.0, 3)]
        [InlineData(10.0, 12.5, 0)]   // 知道的日子比現在晚（時鐘倒退、讀檔）也不得出現負數
        [InlineData(100.0, 10.0, 90)]
        public void DaysAgo_IsFlooredAndNeverNegative(double today, double learned, int expected)
        {
            Assert.Equal(expected, AiMemoryWritePolicy.DaysAgo(today, learned));
        }

        // ---- 寫過的不再即時交 ----

        private static NpcRecalledMemory Recalled(string eventId) => new NpcRecalledMemory
        {
            EventId = eventId,
            Event = MakeEvent(eventId: eventId),
            Entry = new KnownByEntry { HeroId = Hero },
            Facts = new List<Fact> { new Fact { Id = "f1", Category = FactCategory.Who, TextId = "t", Text = "x" } },
            LearnedDay = 10.0
        };

        [Fact]
        public void ExcludeWritten_DropsWrittenItems_AndRecordsTheReason()
        {
            var result = new NpcRecallResult(Hero, 20.0,
                new[] { Recalled("evt_1"), Recalled("evt_2"), Recalled("evt_3") },
                new[] { new RecallExclusion("evt_old", RecallExclusionReason.Forgotten) },
                4);

            var store = new AiPushedStore();
            store.Record(AiMemoryWritePolicy.TargetId, Hero, "evt_2", AiMemoryWritePolicy.RecordVersion, 10.0);
            // 另一位 NPC 寫過的不影響這一位
            store.Record(AiMemoryWritePolicy.TargetId, "lord_b", "evt_3", AiMemoryWritePolicy.RecordVersion, 10.0);

            var filtered = AiMemoryWritePolicy.ExcludeWritten(result, store);

            Assert.Equal(new[] { "evt_1", "evt_3" }, filtered.Items.Select(i => i.EventId).ToArray());
            Assert.Equal(4, filtered.CandidateCount);
            Assert.Equal(2, filtered.Exclusions.Count);
            var added = filtered.Exclusions.Single(e => e.EventId == "evt_2");
            Assert.Equal(RecallExclusionReason.AlreadyInAiMemory, added.Reason);
            Assert.Equal("already in the AI mod's memory", added.Detail);
            Assert.Contains("already in the AI mod's memory", added.ToString());
            Assert.Contains("alreadyInAiMemory=1", filtered.ExclusionSummary());
        }

        [Fact]
        public void ExcludeWritten_NothingWritten_ReturnsTheSameResult()
        {
            var result = new NpcRecallResult(Hero, 20.0, new[] { Recalled("evt_1") }, Array.Empty<RecallExclusion>(), 1);
            Assert.Same(result, AiMemoryWritePolicy.ExcludeWritten(result, new AiPushedStore()));
            Assert.Same(result, AiMemoryWritePolicy.ExcludeWritten(result, null));
        }

        // ---- 模板的 fromNativeEvent ----

        private static readonly PersistenceConfig PersistenceCfg = new() { MaxFactsPerEvent = 24 };

        private static string TemplateJson(string extraField) => @"[
  {
    ""type"": ""sample_event"",
    ""origin"": ""public"",
    ""dramaWeight"": 3,
    " + extraField + @"
    ""roles"": { ""instigator"": ""{MASTERMIND}"", ""target"": ""{TARGET}"" },
    ""knowingRoles"": [""instigator""],
    ""facts"": [
      { ""id"": ""fact_who"", ""category"": ""WHO"", ""textId"": ""VividWorld_Fact_Who"",
        ""text"": ""{INSTIGATOR} confronted {TARGET}"",
        ""vars"": { ""INSTIGATOR"": ""hero:{MASTERMIND}"", ""TARGET"": ""hero:{TARGET}"" }, ""fragility"": 2 }
    ]
  }
]";

        [Fact]
        public void Loader_ReadsFromNativeEvent_AndDefaultsToFalse()
        {
            var marked = EventCatalogLoader.Load(TemplateJson("\"fromNativeEvent\": true,"), PersistenceCfg);
            Assert.Empty(marked.Issues);
            Assert.True(marked.ByType("sample_event")!.FromNativeEvent);

            var unmarked = EventCatalogLoader.Load(TemplateJson(""), PersistenceCfg);
            Assert.Empty(unmarked.Issues);
            Assert.False(unmarked.ByType("sample_event")!.FromNativeEvent);
        }

        private static string ReadShipped(string fileName)
        {
            string? current = AppContext.BaseDirectory;
            while (!string.IsNullOrEmpty(current))
            {
                if (File.Exists(Path.Combine(current, "VividWorld.sln")))
                {
                    return File.ReadAllText(Path.Combine(current, "module", "ModuleData", fileName));
                }
                current = Directory.GetParent(current)?.FullName;
            }
            throw new FileNotFoundException(fileName);
        }

        private static readonly string[] NativeTypes =
        {
            "hero_murdered", "hero_executed", "hero_died_in_battle", "hero_died_naturally", "hero_died_of_old_age",
            "hero_died_in_labor", "hero_taken_prisoner", "heroes_married", "child_born", "hero_released",
            "hero_escaped_captivity", "hero_captured_by_bandits", "hero_rescued_from_bandits", "hero_escaped_bandits"
        };

        [Fact]
        public void ShippedTemplates_Exactly14AreMarkedAsNativeEvents()
        {
            var events = EventCatalogLoader.Load(ReadShipped("vividworld_events.json"), PersistenceCfg);
            var marked = events.Templates.Where(t => t.FromNativeEvent).Select(t => t.Type).ToList();

            var expected = NativeTypes.Concat(events.Templates.Where(t => t.Type.StartsWith("realm_", StringComparison.Ordinal)).Select(t => t.Type)).ToList();
            Assert.Equal(14, expected.Count);
            Assert.Equal(14, marked.Count);
            Assert.Equal(expected.OrderBy(x => x, StringComparer.Ordinal), marked.OrderBy(x => x, StringComparer.Ordinal));

            foreach (var t in events.Templates.Where(t => t.Type.StartsWith("bc_", StringComparison.Ordinal) || t.Type.StartsWith("he_", StringComparison.Ordinal)))
            {
                Assert.False(t.FromNativeEvent, t.Type);
            }
        }

        [Fact]
        public void ShippedSituationTemplates_AreNotMarkedAsNativeEvents()
        {
            var situations = EventCatalogLoader.Load(ReadShipped("vividworld_situation_events.json"), PersistenceCfg);
            Assert.NotEmpty(situations.Templates);
            Assert.DoesNotContain(situations.Templates, t => t.FromNativeEvent);
        }

        // ---- 寫過的紀錄檔 ----

        [Fact]
        public void WrittenFile_MissingOrCorrupt_IsEmpty_AndRoundTrips()
        {
            string dir = Path.Combine(Path.GetTempPath(), "vw_memwritten_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                string path = Path.Combine(dir, "ai_memory_written.json");

                var missing = AiPushedStore.LoadFromFile(path);
                Assert.Empty(missing.GetPushedForHero(AiMemoryWritePolicy.TargetId, Hero));

                File.WriteAllText(path, "{ this is not json");
                var corrupt = AiPushedStore.LoadFromFile(path);
                Assert.Empty(corrupt.GetPushedForHero(AiMemoryWritePolicy.TargetId, Hero));

                var store = new AiPushedStore();
                store.Record(AiMemoryWritePolicy.TargetId, Hero, "evt_1", AiMemoryWritePolicy.RecordVersion, 12.0);
                Assert.True(store.SaveToFile(path));

                var back = AiPushedStore.LoadFromFile(path);
                Assert.True(back.HasPushedVersion("CalradiaRemembers", Hero, "evt_1", "memory"));
                Assert.Equal(12.0, back.GetPushedForHero("CalradiaRemembers", Hero)["evt_1"].PushedDay);
            }
            finally
            {
                Directory.Delete(dir, true);
            }
        }
    }
}
