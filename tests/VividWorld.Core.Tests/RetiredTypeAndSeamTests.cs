#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using VividWorld.Core.Catalog;
using VividWorld.Core.Config;
using VividWorld.Core.Dialogue;
using VividWorld.Core.Events;
using VividWorld.Core.Memory;
using VividWorld.Core.Persistence;
using VividWorld.Core.Presentation;
using VividWorld.Core.Rumors;
using VividWorld.Core.Situations;
using VividWorld.Core.Tests.Fakes;
using VividWorld.Core.Util;
using Xunit;

namespace VividWorld.Core.Tests
{
    public class RetiredTypeAndSeamTests
    {
        private static string FindRepoRoot()
        {
            string? current = AppContext.BaseDirectory;
            while (!string.IsNullOrEmpty(current))
            {
                if (File.Exists(Path.Combine(current, "VividWorld.sln")))
                {
                    return current;
                }
                var parent = Directory.GetParent(current);
                if (parent == null) break;
                current = parent.FullName;
            }
            throw new DirectoryNotFoundException("Could not find repository root containing VividWorld.sln");
        }

        // ============================================================
        // 1. 停用型別測試（三條路各一條 + 正常型別不受影響 + 舊存檔反序列化）
        // ============================================================

        [Fact]
        public void RetiredType_TellerEligibility_ExcludesRetiredEvent()
        {
            var cfg = new VividWorldConfig();
            var traits = new FakeHeroTraitLookup();
            traits.Set(new TraitProfile { HeroId = "hero_teller", Honor = 0, IsAlive = true, IsLord = true });

            var retiredTemplate = new EventTemplate
            {
                Type = "test_retired",
                Origin = EventOrigin.Public,
                Retired = true
            };

            var evt = new WorldEvent
            {
                EventId = "evt_retired_1",
                Type = "test_retired",
                Origin = EventOrigin.Public,
                DramaWeight = 3,
                KnownBy = new List<KnownByEntry>
                {
                    new KnownByEntry { HeroId = "hero_teller", Hop = 0, LearnedDay = 1.0 }
                }
            };

            var reason = TellerEligibility.Check(
                evt,
                "hero_teller",
                day: 2.0,
                maxHop: 3,
                playerHeroId: "hero_player",
                cfg: cfg,
                traits: traits,
                getTemplate: type => type == "test_retired" ? retiredTemplate : null,
                out string? detail);

            Assert.Equal(TellReason.RetiredType, reason);
            Assert.Equal("retired type", TellerEligibility.TellReasonText(reason));
        }

        [Fact]
        public void RetiredType_RumorOfferSelector_ExcludesRetiredEvent_AndRecordsFilterNote()
        {
            var cfg = new VividWorldConfig();
            var engine = new RumorEngine(
                cfg,
                new ThresholdRetentionPolicy(cfg.Retention),
                new NullEmbellishmentPolicy(),
                new FakePropagationChannel(),
                new FakeHeroTraitLookup(),
                new SplitMix64Rng(),
                campaignSeed: 12345L,
                playerHeroId: "hero_player");

            var retiredTemplate = new EventTemplate
            {
                Type = "test_retired",
                Origin = EventOrigin.Public,
                Retired = true
            };

            var selector = new RumorOfferSelector(
                cfg,
                engine,
                playerHeroId: "hero_player",
                mode: RumorMode.Casual,
                getTemplate: type => type == "test_retired" ? retiredTemplate : null);

            var evt = new WorldEvent
            {
                EventId = "evt_retired_offer",
                Type = "test_retired",
                Origin = EventOrigin.Public,
                DramaWeight = 3,
                Facts = new List<Fact>
                {
                    new Fact { Id = "fact1", Category = FactCategory.What, Text = "something happened" }
                },
                KnownBy = new List<KnownByEntry>
                {
                    new KnownByEntry { HeroId = "hero_teller", Hop = 0, LearnedDay = 1.0 }
                }
            };

            var teller = new HeroSocialProfile
            {
                HeroId = "hero_teller",
                RelationWithPlayer = 50
            };

            var candidates = new List<RumorCandidate>
            {
                new RumorCandidate
                {
                    Event = evt,
                    TellerHop = 0,
                    PlayerExistingHop = null
                }
            };

            var decision = selector.DecideOnAsk(teller, candidates, day: 2.0);

            Assert.Equal(AskRefusal.AllCandidatesFiltered, decision.Refusal);
            Assert.Null(decision.Offer);
            Assert.Equal(1, decision.FilteredOther);
            Assert.Contains(decision.FilterNotes, note => note.Contains("evt_retired_offer") && note.Contains("retired type"));
        }

        [Fact]
        public void RetiredType_NpcRecallQuery_ExcludesRetiredEvent_WithRetiredReason()
        {
            var retiredTemplate = new EventTemplate
            {
                Type = "test_retired",
                Origin = EventOrigin.Public,
                Retired = true
            };

            var evt = new WorldEvent
            {
                EventId = "evt_recall_retired",
                Type = "test_retired",
                Origin = EventOrigin.Public,
                DramaWeight = 3,
                Facts = new List<Fact>
                {
                    new Fact { Id = "fact1", Category = FactCategory.What, Text = "something happened" }
                },
                KnownBy = new List<KnownByEntry>
                {
                    new KnownByEntry { HeroId = "hero_ai", Hop = 0, LearnedDay = 1.0 }
                }
            };

            var result = NpcRecallQuery.Query(
                heroId: "hero_ai",
                currentDay: 2.0,
                maxCount: 5,
                candidateEvents: new[] { evt },
                getTemplate: type => type == "test_retired" ? retiredTemplate : null);

            Assert.Empty(result.Items);
            Assert.Single(result.Exclusions);
            Assert.Equal(RecallExclusionReason.RetiredType, result.Exclusions[0].Reason);
            Assert.Equal("retired type", result.Exclusions[0].Detail);
            Assert.Equal(1, result.ExclusionCountByReason(RecallExclusionReason.RetiredType));
            Assert.Contains("retiredType=1", result.ExclusionSummary());
        }

        [Fact]
        public void RetiredType_ActiveEvent_IsNotExcluded()
        {
            var cfg = new VividWorldConfig();
            var traits = new FakeHeroTraitLookup();
            traits.Set(new TraitProfile { HeroId = "hero_teller", Honor = 0, IsAlive = true, IsLord = true });

            var activeTemplate = new EventTemplate
            {
                Type = "test_active",
                Origin = EventOrigin.Public,
                Retired = false
            };

            var evt = new WorldEvent
            {
                EventId = "evt_active_1",
                Type = "test_active",
                Origin = EventOrigin.Public,
                DramaWeight = 3,
                Facts = new List<Fact>
                {
                    new Fact { Id = "fact1", Category = FactCategory.What, Text = "something happened" }
                },
                KnownBy = new List<KnownByEntry>
                {
                    new KnownByEntry { HeroId = "hero_teller", Hop = 0, LearnedDay = 1.0 }
                }
            };

            // 1. TellerEligibility
            var tellReason = TellerEligibility.Check(
                evt,
                "hero_teller",
                day: 2.0,
                maxHop: 3,
                playerHeroId: "hero_player",
                cfg: cfg,
                traits: traits,
                getTemplate: type => type == "test_active" ? activeTemplate : null);
            Assert.Equal(TellReason.Ok, tellReason);

            // 2. NpcRecallQuery
            var recallResult = NpcRecallQuery.Query(
                heroId: "hero_teller",
                currentDay: 2.0,
                maxCount: 5,
                candidateEvents: new[] { evt },
                getTemplate: type => type == "test_active" ? activeTemplate : null);
            Assert.Single(recallResult.Items);
            Assert.Empty(recallResult.Exclusions);
        }

        [Fact]
        public void RetiredType_OldStoredEventWithRetiredType_LoadsWithoutException()
        {
            // 驗證舊事件（磁碟上已有、型別已停用）讀得進來不丟例外
            string json = @"{
                ""eventId"": ""evt_old_tavern_conf"",
                ""type"": ""tavern_confidence"",
                ""origin"": ""secret"",
                ""dramaWeight"": 2,
                ""day"": 10.5,
                ""state"": { ""leaked"": false },
                ""facts"": [
                    {
                        ""id"": ""who"",
                        ""category"": ""WHO"",
                        ""textId"": ""VividWorld_Fact_TavernConfidence_Who"",
                        ""text"": ""Borov confided to Arineth"",
                        ""vars"": { ""SPEAKER"": ""hero:borov"", ""LISTENER"": ""hero:arineth"" }
                    }
                ],
                ""knownBy"": [
                    { ""heroId"": ""borov"", ""hop"": 0, ""learnedDay"": 10.5 }
                ]
            }";

            var evt = VividJson.Read<WorldEvent>(json);
            Assert.NotNull(evt);
            Assert.Equal("evt_old_tavern_conf", evt!.EventId);
            Assert.Equal("tavern_confidence", evt.Type);
            Assert.Equal(EventOrigin.Secret, evt.Origin);
            Assert.Single(evt.Facts);
            Assert.Equal("who", evt.Facts[0].Id);
            Assert.Single(evt.KnownBy);
            Assert.Equal("borov", evt.KnownBy[0].HeroId);
        }

        // ============================================================
        // 2. 出貨情境測試（tavern_words 與 victory_credit 不再產出停用型別）
        // ============================================================

        [Fact]
        public void ShippedSituations_TavernWordsAndVictoryCredit_DoNotProduceRetiredTypes()
        {
            string repoRoot = FindRepoRoot();
            string sitPath = Path.Combine(repoRoot, "module", "ModuleData", "vividworld_situations.json");
            string sitEventsPath = Path.Combine(repoRoot, "module", "ModuleData", "vividworld_situation_events.json");

            var sitCatalog = SituationCatalogLoader.Load(File.ReadAllText(sitPath));
            var eventCatalog = EventCatalogLoader.Load(File.ReadAllText(sitEventsPath), new PersistenceConfig());

            // 1. 驗證事件目錄中這兩項已標記為 retired
            var tmplConf = eventCatalog.ByType("tavern_confidence");
            Assert.NotNull(tmplConf);
            Assert.True(tmplConf!.Retired, "tavern_confidence must have retired = true");

            var tmplBelittle = eventCatalog.ByType("victory_credit_belittled");
            Assert.NotNull(tmplBelittle);
            Assert.True(tmplBelittle!.Retired, "victory_credit_belittled must have retired = true");

            // 2. 驗證 tavern_words 情境的分支清單中沒有任何 tavern_confidence
            var tavernWords = sitCatalog.ById("tavern_words");
            Assert.NotNull(tavernWords);
            foreach (var branch in tavernWords!.Branches)
            {
                Assert.NotEqual("confide", branch.Id);
                foreach (var evtRef in branch.Events)
                {
                    Assert.NotEqual("tavern_confidence", evtRef.Type);
                }
            }

            // 3. 驗證 victory_credit 情境的分支清單中沒有任何 victory_credit_belittled
            var victoryCredit = sitCatalog.ById("victory_credit");
            Assert.NotNull(victoryCredit);
            foreach (var branch in victoryCredit!.Branches)
            {
                Assert.NotEqual("belittle", branch.Id);
                foreach (var evtRef in branch.Events)
                {
                    Assert.NotEqual("victory_credit_belittled", evtRef.Type);
                }
            }
        }

        // ============================================================
        // 3. 碎片接縫測試（……、——、...、… 不插分隔符；一般接縫正常插；最後是刪節號正常句尾）
        // ============================================================

        private static PresentationConfig CreatePresentationConfig(string factSeparator, string sentenceEnd) =>
            new PresentationConfig
            {
                EncyclopediaLinksEnabled = false,
                FactSeparator = factSeparator,
                SentenceEnd = sentenceEnd
            };

        [Fact]
        public void RumorTextAssembler_EllipsisSeam_DoubleEllipsis_OmitsSeparator()
        {
            // 前一塊以「……」結尾，後一塊直接串接，不插「，」
            var cfg = CreatePresentationConfig("，", "。");

            var evt = new WorldEvent
            {
                EventId = "evt_seam_1",
                Type = "test_event",
                Origin = EventOrigin.Public
            };

            var facts = new[]
            {
                new Fact { Id = "part1", TextId = "f1", Text = "那可不是什麼意外……" },
                new Fact { Id = "part2", TextId = "f2", Text = "算了，這話你就當沒聽過" }
            };

            var composed = RumorTextComposer.Compose(evt, facts, cfg, prefix: null);
            var result = RumorTextAssembler.Assemble(composed, cfg, (val, links) => val);

            Assert.Equal("那可不是什麼意外……算了，這話你就當沒聽過。", result.DisplayText);
            Assert.Equal("那可不是什麼意外……算了，這話你就當沒聽過。", result.PlainText);
        }

        [Fact]
        public void RumorTextAssembler_EllipsisSeam_Dash_OmitsSeparator()
        {
            // 前一塊以「——」結尾，後一塊直接串接，不插「，」
            var cfg = CreatePresentationConfig("，", "。");

            var evt = new WorldEvent
            {
                EventId = "evt_seam_2",
                Type = "test_event",
                Origin = EventOrigin.Public
            };

            var facts = new[]
            {
                new Fact { Id = "part1", TextId = "f1", Text = "他本想開口說些什麼——" },
                new Fact { Id = "part2", TextId = "f2", Text = "終究還是咽了回去" }
            };

            var composed = RumorTextComposer.Compose(evt, facts, cfg, prefix: null);
            var result = RumorTextAssembler.Assemble(composed, cfg, (val, links) => val);

            Assert.Equal("他本想開口說些什麼——終究還是咽了回去。", result.DisplayText);
            Assert.Equal("他本想開口說些什麼——終究還是咽了回去。", result.PlainText);
        }

        [Fact]
        public void RumorTextAssembler_EllipsisSeam_EnglishDots_OmitsSeparator()
        {
            // 前一塊以英文「...」結尾，後一塊直接串接，不插「, 」
            var cfg = CreatePresentationConfig(", ", ".");

            var evt = new WorldEvent
            {
                EventId = "evt_seam_3",
                Type = "test_event",
                Origin = EventOrigin.Public
            };

            var facts = new[]
            {
                new Fact { Id = "part1", TextId = "f1", Text = "It was no accident..." },
                new Fact { Id = "part2", TextId = "f2", Text = "forget you heard that" }
            };

            var composed = RumorTextComposer.Compose(evt, facts, cfg, prefix: null);
            var result = RumorTextAssembler.Assemble(composed, cfg, (val, links) => val);

            Assert.Equal("It was no accident...forget you heard that.", result.DisplayText);
            Assert.Equal("It was no accident...forget you heard that.", result.PlainText);
        }

        [Fact]
        public void RumorTextAssembler_EllipsisSeam_SingleEllipsis_OmitsSeparator()
        {
            // 前一塊以單字元「…」結尾，後一塊直接串接，不插「，」
            var cfg = CreatePresentationConfig("，", "。");

            var evt = new WorldEvent
            {
                EventId = "evt_seam_4",
                Type = "test_event",
                Origin = EventOrigin.Public
            };

            var facts = new[]
            {
                new Fact { Id = "part1", TextId = "f1", Text = "就在城外…" },
                new Fact { Id = "part2", TextId = "f2", Text = "血流成河" }
            };

            var composed = RumorTextComposer.Compose(evt, facts, cfg, prefix: null);
            var result = RumorTextAssembler.Assemble(composed, cfg, (val, links) => val);

            Assert.Equal("就在城外…血流成河。", result.DisplayText);
        }

        [Fact]
        public void RumorTextAssembler_NormalSeam_IncludesSeparator()
        {
            // 結尾不是刪節號或破折號時，照舊插入分隔符
            var cfg = CreatePresentationConfig("，", "。");

            var evt = new WorldEvent
            {
                EventId = "evt_normal_seam",
                Type = "test_event",
                Origin = EventOrigin.Public
            };

            var facts = new[]
            {
                new Fact { Id = "part1", TextId = "f1", Text = "雙方在城門前交手" },
                new Fact { Id = "part2", TextId = "f2", Text = "戰況十分激烈" }
            };

            var composed = RumorTextComposer.Compose(evt, facts, cfg, prefix: null);
            var result = RumorTextAssembler.Assemble(composed, cfg, (val, links) => val);

            Assert.Equal("雙方在城門前交手，戰況十分激烈。", result.DisplayText);
        }

        [Fact]
        public void RumorTextAssembler_EndingWithEllipsis_HandlesSentenceEndNormally()
        {
            // 最後一塊結尾是刪節號時句尾照舊處理（因不以 sentenceEnd 結尾，補上 sentenceEnd）
            var cfg = CreatePresentationConfig("，", "。");

            var evt = new WorldEvent
            {
                EventId = "evt_end_ellipsis",
                Type = "test_event",
                Origin = EventOrigin.Public
            };

            var facts = new[]
            {
                new Fact { Id = "part1", TextId = "f1", Text = "事情本該就此作罷" },
                new Fact { Id = "part2", TextId = "f2", Text = "誰知事情還沒完……" }
            };

            var composed = RumorTextComposer.Compose(evt, facts, cfg, prefix: null);
            var result = RumorTextAssembler.Assemble(composed, cfg, (val, links) => val);

            Assert.Equal("事情本該就此作罷，誰知事情還沒完……", result.DisplayText);
        }
    }
}
