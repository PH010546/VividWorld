using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using VividWorld.Core.Catalog;
using VividWorld.Core.Config;
using VividWorld.Core.Diagnostics;
using VividWorld.Core.Events;
using VividWorld.Core.Grudges;
using VividWorld.Core.Memory;
using VividWorld.Core.Persistence;
using VividWorld.Core.Presentation;
using VividWorld.Core.Rumors;
using VividWorld.Core.Util;
using Xunit;
using static VividWorld.Core.Events.MadeUpTalk;

namespace VividWorld.Core.Tests
{
    public class FalseRumorsFTests
    {
        private sealed class ThrowingRng : IDeterministicRng
        {
            public double NextDouble(long seed) => throw new InvalidOperationException("rolled");
            public bool Chance(double p, long seed) => throw new InvalidOperationException("rolled");
            public int Pick(int count, long seed) => throw new InvalidOperationException("rolled");
            public int PickWeighted(IReadOnlyList<double> weights, long seed) => throw new InvalidOperationException("rolled");
        }

        private sealed class FixedRng : IDeterministicRng
        {
            private readonly bool _chanceResult;
            public FixedRng(bool chanceResult) => _chanceResult = chanceResult;
            public double NextDouble(long seed) => 0.5;
            public bool Chance(double p, long seed) => _chanceResult;
            public int Pick(int count, long seed) => 0;
            public int PickWeighted(IReadOnlyList<double> weights, long seed) => 0;
        }

        // 1. TruthKnowers: 說君主壞話，掛鉤真事件不存在時，僅當事人出面否認
        [Fact]
        public void TruthKnowers_SpokeAgainstRuler_HungLMissing_OnlyAccusedReturned()
        {
            var x = new WorldEvent
            {
                EventId = "evt_x1",
                Type = "conduct_spoke_against_ruler",
                Fabricated = true,
                OriginatorHeroId = "originator_1",
                Participants = new Dictionary<string, string>
                {
                    ["speaker"] = "lord_accused",
                    ["ruler"] = "king_caladog"
                }
            };

            var knowers = GetTruthKnowers(x, null);
            Assert.Single(knowers);
            Assert.Equal("lord_accused", knowers[0].HeroId);
            Assert.Equal("talk_denied_spoke_against_ruler", knowers[0].ResponseType);
            Assert.True(knowers[0].IsAccused);
            Assert.False(knowers[0].IsPlayer);
        }

        // 2. TruthKnowers: 說君主壞話，掛鉤真事件存在時，君主排除（私下議論君主並不知情），僅被指控者出面
        [Fact]
        public void TruthKnowers_SpokeAgainstRuler_HungLPresent_RulerExcluded_OnlyAccusedReturned()
        {
            var x = new WorldEvent
            {
                EventId = "evt_x2",
                Type = "conduct_spoke_against_ruler",
                Fabricated = true,
                OriginatorHeroId = "originator_1",
                Participants = new Dictionary<string, string>
                {
                    ["speaker"] = "lord_accused",
                    ["ruler"] = "king_caladog"
                }
            };
            var hungL = new WorldEvent
            {
                EventId = "evt_l2",
                Type = "conduct_spoke_against_ruler",
                Participants = new Dictionary<string, string>
                {
                    ["speaker"] = "lord_accused",
                    ["ruler"] = "king_caladog"
                }
            };

            var knowers = GetTruthKnowers(x, hungL);
            Assert.Single(knowers);
            Assert.Equal("lord_accused", knowers[0].HeroId);
            Assert.DoesNotContain(knowers, k => k.HeroId == "king_caladog");
        }

        // 3. TruthKnowers: 苛待俘虜，真事件存在時，被指控者否認、俘虜與同行同袍澄清，且依優先序排列
        [Fact]
        public void TruthKnowers_MistreatedPrisoner_HungLPresent_BothAccusedAndComradesReturned()
        {
            var x = new WorldEvent
            {
                EventId = "evt_x3",
                Type = "conduct_mistreated_prisoner",
                Fabricated = true,
                OriginatorHeroId = "originator_1",
                Participants = new Dictionary<string, string>
                {
                    ["captor"] = "captor_hero",
                    ["prisoner"] = "prisoner_hero"
                }
            };
            var hungL = new WorldEvent
            {
                EventId = "evt_l3",
                Type = "conduct_mistreated_prisoner",
                Participants = new Dictionary<string, string>
                {
                    ["captor"] = "captor_hero",
                    ["prisoner"] = "prisoner_hero"
                },
                CaptorArmyLeaderHeroIds = new List<string> { "comrade_1", "comrade_2" }
            };

            var knowers = GetTruthKnowers(x, hungL);
            Assert.Equal(4, knowers.Count);

            // 第一順位：被指控者（否認）
            Assert.Equal("captor_hero", knowers[0].HeroId);
            Assert.Equal("talk_denied_mistreated_prisoner", knowers[0].ResponseType);
            Assert.True(knowers[0].IsAccused);

            // 第二順位：俘虜（澄清）
            Assert.Equal("prisoner_hero", knowers[1].HeroId);
            Assert.Equal("talk_corrected_mistreated_by_prisoner", knowers[1].ResponseType);
            Assert.False(knowers[1].IsAccused);

            // 第三順位：同袍（澄清）
            Assert.Equal("comrade_1", knowers[2].HeroId);
            Assert.Equal("talk_corrected_mistreated_by_comrade", knowers[2].ResponseType);
            Assert.False(knowers[2].IsAccused);

            Assert.Equal("comrade_2", knowers[3].HeroId);
            Assert.Equal("talk_corrected_mistreated_by_comrade", knowers[3].ResponseType);
            Assert.False(knowers[3].IsAccused);
        }

        // 4. TruthKnowers: 好話的被誇者不出面，但算知情人（responseType 為 null）
        [Fact]
        public void TruthKnowers_PraiseTalk_AccusedHasNullResponseType_DoesNotStepForward()
        {
            var x = new WorldEvent
            {
                EventId = "evt_x4",
                Type = "victory_credit_deferred",
                Fabricated = true,
                OriginatorHeroId = "originator_1",
                Participants = new Dictionary<string, string>
                {
                    ["claimant"] = "lord_claimant",
                    ["rival"] = "lord_rival"
                }
            };

            var knowers = GetTruthKnowers(x, null);
            Assert.Equal(2, knowers.Count);

            // 競爭者澄清
            var rivalKnower = knowers.FirstOrDefault(k => k.HeroId == "lord_rival");
            Assert.NotNull(rivalKnower);
            Assert.Equal("talk_not_so_victory_credit_deferred", rivalKnower!.ResponseType);
            Assert.False(rivalKnower.IsAccused);

            // 被誇者（claimant）responseType 為 null
            var claimantKnower = knowers.FirstOrDefault(k => k.HeroId == "lord_claimant");
            Assert.NotNull(claimantKnower);
            Assert.Null(claimantKnower!.ResponseType);
            Assert.True(claimantKnower.IsAccused);
        }

        // 5. TruthKnowers: 當玩家是被指控者時，IsPlayer 標為 true 且 ResponseType 為 null
        [Fact]
        public void TruthKnowers_PlayerIsAccused_ReturnedWithIsPlayerTrue()
        {
            var x = new WorldEvent
            {
                EventId = "evt_x5",
                Type = "conduct_rash_capture",
                Fabricated = true,
                OriginatorHeroId = "originator_1",
                Participants = new Dictionary<string, string>
                {
                    ["prisoner"] = "player_hero",
                    ["captor"] = "captor_lord"
                }
            };

            var knowers = GetTruthKnowers(x, null, playerHeroId: "player_hero");
            var playerKnower = knowers.FirstOrDefault(k => k.HeroId == "player_hero");
            Assert.NotNull(playerKnower);
            Assert.True(playerKnower!.IsPlayer);
            Assert.Null(playerKnower.ResponseType);
        }

        // 6. BeliefJudge: 知情人直接不信，不擲骰
        [Fact]
        public void BeliefJudge_KnowsTruth_AutoDisbelievesWithoutRolling()
        {
            var input = new BeliefInputs
            {
                CampaignSeed = 12345,
                EventId = "evt_test",
                HearerId = "hero_accused",
                SubjectId = "hero_accused",
                TellerId = "teller_hero",
                Hop = 1,
                HearerIsParticipant = true,
                KnowsTruth = true,
                KnowsTruthReason = "accused",
                ResponsePenalty = 0,
                Round = 0
            };

            // 使用 ThrowingRng：若有任何擲骰動作將拋出例外
            var result = BeliefJudge.Judge(input, new BeliefConfig(), new ThrowingRng());

            Assert.False(result.Believes);
            Assert.False(result.Rolled);
            Assert.Equal(BeliefReason.KnowsTruth, result.Heaviest);
        }

        // 7. BeliefJudge: 否認懲罰使相信機率降低 25
        [Fact]
        public void BeliefJudge_ResponsePenalty_DenialReducesBeliefBy25()
        {
            var rng = new FixedRng(true);
            var cfg = new BeliefConfig();

            var baseInput = new BeliefInputs
            {
                CampaignSeed = 999,
                EventId = "evt_p1",
                HearerId = "listener_1",
                SubjectId = "subject_1",
                TellerId = "teller_1",
                Hop = 1,
                ResponsePenalty = 0,
                Round = 0
            };

            var penaltyInput = new BeliefInputs
            {
                CampaignSeed = 999,
                EventId = "evt_p1",
                HearerId = "listener_1",
                SubjectId = "subject_1",
                TellerId = "teller_1",
                Hop = 1,
                ResponsePenalty = -25.0,
                Round = 0
            };

            var resBase = BeliefJudge.Judge(baseInput, cfg, rng);
            var resPenalty = BeliefJudge.Judge(penaltyInput, cfg, rng);

            Assert.Equal(resBase.Chance - 25.0, resPenalty.Chance);
            Assert.Equal(-25.0, resPenalty.Response);
        }

        // 8. BeliefJudge: 澄清懲罰使相信機率降低 50
        [Fact]
        public void BeliefJudge_ResponsePenalty_ClarifyReducesBeliefBy50()
        {
            var rng = new FixedRng(true);
            var cfg = new BeliefConfig();

            var baseInput = new BeliefInputs
            {
                CampaignSeed = 999,
                EventId = "evt_p2",
                HearerId = "listener_1",
                SubjectId = "subject_1",
                TellerId = "teller_1",
                Hop = 1,
                ResponsePenalty = 0,
                Round = 0
            };

            var penaltyInput = new BeliefInputs
            {
                CampaignSeed = 999,
                EventId = "evt_p2",
                HearerId = "listener_1",
                SubjectId = "subject_1",
                TellerId = "teller_1",
                Hop = 1,
                ResponsePenalty = -50.0,
                Round = 0
            };

            var resBase = BeliefJudge.Judge(baseInput, cfg, rng);
            var resPenalty = BeliefJudge.Judge(penaltyInput, cfg, rng);

            Assert.Equal(resBase.Chance - 50.0, resPenalty.Chance);
            Assert.Equal(-50.0, resPenalty.Response);
        }

        // 9. BeliefJudge: 兩者皆有時懲罰累加達 75
        [Fact]
        public void BeliefJudge_ResponsePenalty_BothReducesBeliefBy75()
        {
            var rng = new FixedRng(true);
            var cfg = new BeliefConfig { BaseChance = 90.0 };

            var baseInput = new BeliefInputs
            {
                CampaignSeed = 999,
                EventId = "evt_p3",
                HearerId = "listener_1",
                SubjectId = "subject_1",
                TellerId = "teller_1",
                Hop = 1,
                ResponsePenalty = 0,
                Round = 0
            };

            var penaltyInput = new BeliefInputs
            {
                CampaignSeed = 999,
                EventId = "evt_p3",
                HearerId = "listener_1",
                SubjectId = "subject_1",
                TellerId = "teller_1",
                Hop = 1,
                ResponsePenalty = -75.0,
                Round = 0
            };

            var resBase = BeliefJudge.Judge(baseInput, cfg, rng);
            var resPenalty = BeliefJudge.Judge(penaltyInput, cfg, rng);

            Assert.Equal(resBase.Chance - 75.0, resPenalty.Chance);
            Assert.Equal(-75.0, resPenalty.Response);
        }

        // 10. StepForward: 否認機率計算正確（膽識、誠實、謹慎）且夾取在 5..95
        [Fact]
        public void StepForward_DenialChance_CalculatedCorrectly()
        {
            var cfg = new FalseRumorsConfig();

            // 基準：valor=0, honor=0 -> 50%
            var normalTraits = new TraitProfile();
            double chanceBase = StepForwardCalculator.CalculateChance("denial", normalTraits, config: cfg);
            Assert.Equal(50.0, chanceBase);

            // 高膽識 (+1) 與高榮譽 (+1) -> 50 + 25 + 25 = 100 -> 夾取至 95
            var braveTraits = new TraitProfile { Valor = 1, Honor = 1 };
            double chanceBrave = StepForwardCalculator.CalculateChance("denial", braveTraits, config: cfg);
            Assert.Equal(95.0, chanceBrave);

            // 膽小 (-1) -> 50 - 30 = 20
            var cowardlyTraits = new TraitProfile { Valor = -1 };
            double chanceCowardly = StepForwardCalculator.CalculateChance("denial", cowardlyTraits, config: cfg);
            Assert.Equal(20.0, chanceCowardly);
        }

        // 11. StepForward: 澄清機率計算正確（誠實、關係好壞加成與扣分）且夾取在 5..95
        [Fact]
        public void StepForward_ClarifyChance_CalculatedCorrectly()
        {
            var cfg = new FalseRumorsConfig();

            // 基準：honor=0, relation=0 -> 40%
            var neutralTraits = new TraitProfile();
            double chanceBase = StepForwardCalculator.CalculateChance("clarification", neutralTraits, relationTowardSubject: 0, config: cfg);
            Assert.Equal(40.0, chanceBase);

            // 好友 (+40 關係) 且誠實 (+1) -> 40 + 30 + 20 = 90%
            var honestTraits = new TraitProfile { Honor = 1 };
            double chanceFriend = StepForwardCalculator.CalculateChance("clarification", honestTraits, relationTowardSubject: 40, config: cfg);
            Assert.Equal(90.0, chanceFriend);

            // 敵對 (-30 關係) -> 40 - 40 = 0 -> 夾取至 5
            var hostileTraits = new TraitProfile();
            double chanceHostile = StepForwardCalculator.CalculateChance("clarification", hostileTraits, relationTowardSubject: -30, config: cfg);
            Assert.Equal(5.0, chanceHostile);
        }

        // 12. StepForward: 確定性種子產生一致結果
        [Fact]
        public void StepForward_DeterministicSeed_SameSeedProducesSameResult()
        {
            bool r1 = StepForwardCalculator.Roll(50.0, 12345, "evt_x", "lord_a");
            bool r2 = StepForwardCalculator.Roll(50.0, 12345, "evt_x", "lord_a");

            Assert.Equal(r1, r2);
        }

        // 13. Retraction: 撤銷標記 Contradicted、自 GrudgeIndex 移除、結算時略過 Contradicted
        [Fact]
        public void Retraction_HearingResponse_ContradictsRumorImpacts()
        {
            var evt = new WorldEvent
            {
                EventId = "evt_rumor_1",
                Type = "conduct_poisoned",
                Participants = new Dictionary<string, string>
                {
                    ["poisoner"] = "poisoner_1",
                    ["listener"] = "listener_1"
                }
            };

            var impact = new RelationImpact
            {
                AboutHeroId = "poisoner_1",
                Requested = -5,
                Delta = -5,
                AppliedDay = 10,
                Source = GrudgeSource.Rumor,
                Contradicted = false
            };

            var entry = new KnownByEntry
            {
                HeroId = "listener_1",
                RelationImpacts = new List<RelationImpact> { impact }
            };

            var grudgeIndex = new GrudgeIndex();
            grudgeIndex.Note(new GrudgeEntry
            {
                EventId = evt.EventId,
                FromHeroId = "listener_1",
                AboutHeroId = "poisoner_1",
                Source = GrudgeSource.Rumor,
                Delta = -5,
                Day = 10,
                Scope = GrudgeScope.Personal
            });

            // 驗證原本在 GrudgeIndex 中
            Assert.NotEmpty(grudgeIndex.Between("listener_1", "poisoner_1", GrudgeScope.Personal));

            // 模擬聽到回應後撤銷：標記 Contradicted 並自 GrudgeIndex 移除
            impact.Contradicted = true;
            grudgeIndex.Remove(evt.EventId, "listener_1", "poisoner_1", GrudgeSource.Rumor);

            // 驗證已自 GrudgeIndex 移除
            Assert.Empty(grudgeIndex.Between("listener_1", "poisoner_1", GrudgeScope.Personal));

            // 驗證 ConsequenceResolver 計算已結算好感時忽略 Contradicted
            var fact = new Fact
            {
                Id = "who",
                Vars = new Dictionary<string, string> { ["POISONER"] = "hero:poisoner_1" }
            };
            var opinions = new List<OpinionDef>
            {
                new OpinionDef { About = "poisoner", Amount = -5, Receiver = "listener" }
            };

            var result = ConsequenceResolver.Resolve(
                evt,
                entry,
                new[] { fact },
                opinions,
                new ConsequenceConfig(),
                remainingDailyBudget: 10.0);

            // 由於前一次 Rumor impact 已 Contradicted，不計入 already，本次完整結算 -5
            Assert.Single(result.Changes);
            Assert.Equal(-5.0, result.Changes[0].Requested);
        }

        // 14. Config: 舊的 misconception 被 LegacyConfigKeys 記錄且已無該屬性
        [Fact]
        public void Config_Misconception_LegacyKeysLoggedAndNoMisconceptionProperty()
        {
            Assert.Contains("misconception", LegacyConfigKeys.UnusedConsequenceKeys);
            var prop = typeof(ConsequenceConfig).GetProperty("Misconception");
            Assert.Null(prop);
        }

        // 15. Config: FalseRumors 新欄位正規化夾取正確
        [Fact]
        public void Config_FalseRumors_LoadsAndClampsCorrectly()
        {
            var cfg = new VividWorldConfig();
            cfg.FalseRumors.Belief.DeniedDelta = -150;
            cfg.FalseRumors.Belief.ClarifiedDelta = 50;
            cfg.FalseRumors.StepForwardMinChance = -10;
            cfg.FalseRumors.StepForwardMaxChance = 150;
            cfg.FalseRumors.StepForwardWaitDays = 0;

            var notices = new List<ClampNotice>();
            cfg.Normalize(notices);

            Assert.Equal(-100.0, cfg.FalseRumors.Belief.DeniedDelta);
            Assert.Equal(0.0, cfg.FalseRumors.Belief.ClarifiedDelta);
            Assert.Equal(0.0, cfg.FalseRumors.StepForwardMinChance);
            Assert.Equal(100.0, cfg.FalseRumors.StepForwardMaxChance);
            Assert.Equal(1, cfg.FalseRumors.StepForwardWaitDays);
        }

        // 16. TemplateVariants: 5 種具名否認模板變體正確產出且保有 Response 欄位
        [Fact]
        public void TemplateVariants_DenialVariants_GeneratedCorrectly()
        {
            var baseTmpl = new EventTemplate
            {
                Type = "talk_denied_poisoned",
                Response = "denial",
                Roles = new Dictionary<string, string>
                {
                    ["denier"] = "{DENIER}",
                    ["victim"] = "{VICTIM}"
                },
                Facts = new List<TemplateFact>
                {
                    new TemplateFact
                    {
                        Id = "who",
                        Category = FactCategory.Who,
                        TextId = "VividWorld_Fact_TalkDeniedPoisoned_Who",
                        Text = "who text",
                        Vars = new Dictionary<string, string> { ["DENIER"] = "hero:{DENIER}", ["VICTIM"] = "hero:{VICTIM}" }
                    }
                }
            };

            var shapes = TemplateVariants.AllShapes(baseTmpl);
            Assert.Equal(2, shapes.Count);

            var namedShape = shapes.FirstOrDefault(s => s.Label.Contains("named"));
            Assert.NotNull(namedShape.Template);
            Assert.Equal("denial", namedShape.Template.Response);
            Assert.Equal("VividWorld_Fact_TalkDeniedPoisoned_WhoNamed", namedShape.Template.Facts[0].TextId);
        }

        // 17. SentenceCombinations: talk_denied_poisoned 的 DeadRoles 包含 victim
        [Fact]
        public void SentenceCombinations_TalkDeniedPoisoned_DeadRolesContainsVictim()
        {
            Assert.True(VividWorld.Core.Presentation.SentenceCombinationEnumerator.DeadRoles.TryGetValue("talk_denied_poisoned", out var deadRoles));
            Assert.NotNull(deadRoles);
            Assert.Contains("victim", deadRoles);
        }

        // 18. Catalog: 模板 response 欄位格式錯誤或缺少 linkedTemplateType 時回報 ResponseInvalid
        [Fact]
        public void EventCatalogLoader_ResponseInvalid_ReportedWhenResponseMalformedOrNoLinkedTemplate()
        {
            string malformedJson = @"[
                {
                    ""type"": ""talk_test_1"",
                    ""headline"": ""Test"",
                    ""origin"": ""public"",
                    ""dramaWeight"": 3,
                    ""response"": ""wrong_response"",
                    ""linkedTemplateType"": ""some_event"",
                    ""roles"": { ""speaker"": ""{SPEAKER}"" },
                    ""facts"": [
                        { ""id"": ""who"", ""category"": ""WHO"", ""textId"": ""k1"", ""text"": ""t1"", ""vars"": {}, ""fragility"": 1 }
                    ]
                },
                {
                    ""type"": ""talk_test_2"",
                    ""headline"": ""Test 2"",
                    ""origin"": ""public"",
                    ""dramaWeight"": 3,
                    ""response"": ""denial"",
                    ""linkedTemplateType"": null,
                    ""roles"": { ""speaker"": ""{SPEAKER}"" },
                    ""facts"": [
                        { ""id"": ""who"", ""category"": ""WHO"", ""textId"": ""k2"", ""text"": ""t2"", ""vars"": {}, ""fragility"": 1 }
                    ]
                }
            ]";

            var catalog = EventCatalogLoader.Load(malformedJson, new PersistenceConfig());
            Assert.Contains(catalog.Issues, i => i.Code == CatalogIssueCode.ResponseInvalid && i.TemplateType == "talk_test_1");
            Assert.Contains(catalog.Issues, i => i.Code == CatalogIssueCode.ResponseInvalid && i.TemplateType == "talk_test_2");
        }

        // 19. TruthKnowers: 莽撞被俘，抓人的與真事第 0 手的旁人出面澄清
        [Fact]
        public void TruthKnowers_RashCapture_CaptorAndHop0Bystander_StepForward()
        {
            var x = new WorldEvent
            {
                EventId = "evt_x_rash",
                Type = "conduct_rash_capture",
                Fabricated = true,
                OriginatorHeroId = "orig_1",
                Participants = new Dictionary<string, string>
                {
                    ["prisoner"] = "prisoner_hero",
                    ["captor"] = "captor_hero"
                }
            };
            var hungL = new WorldEvent
            {
                EventId = "evt_l_rash",
                Type = "conduct_rash_capture",
                Participants = new Dictionary<string, string>
                {
                    ["prisoner"] = "prisoner_hero",
                    ["captor"] = "captor_hero"
                },
                KnownBy = new List<KnownByEntry>
                {
                    new KnownByEntry { HeroId = "bystander_1", Hop = 0 },
                    new KnownByEntry { HeroId = "hop1_hero", Hop = 1 }
                }
            };

            var knowers = GetTruthKnowers(x, hungL);
            Assert.Contains(knowers, k => k.HeroId == "captor_hero" && k.ResponseType == "talk_corrected_rash_capture_by_captor" && k.Reason == "captor");
            Assert.Contains(knowers, k => k.HeroId == "bystander_1" && k.ResponseType == "talk_corrected_rash_capture_by_bystander" && k.Reason == "bystander");
            Assert.DoesNotContain(knowers, k => k.HeroId == "hop1_hero");
        }

        // 20. TruthKnowers: 求援遭拒，求援的人出面澄清
        [Fact]
        public void TruthKnowers_RefusedAid_Asker_StepsForward()
        {
            var x = new WorldEvent
            {
                EventId = "evt_x_aid",
                Type = "conduct_refused_aid",
                Fabricated = true,
                OriginatorHeroId = "orig_1",
                Participants = new Dictionary<string, string>
                {
                    ["refuser"] = "refuser_hero",
                    ["asker"] = "asker_hero"
                }
            };

            var knowers = GetTruthKnowers(x, null);
            var askerKnower = knowers.FirstOrDefault(k => k.HeroId == "asker_hero");
            Assert.NotNull(askerKnower);
            Assert.Equal("talk_corrected_refused_aid_by_asker", askerKnower!.ResponseType);
            Assert.Equal("asker", askerKnower.Reason);
            Assert.False(askerKnower.IsAccused);
        }

        // 21. TruthKnowers: 五種好事的受惠者出面
        [Fact]
        public void TruthKnowers_FiveGoodTalks_Beneficiaries_StepForward()
        {
            var testCases = new[]
            {
                ("victory_credit_deferred", "rival", "talk_not_so_victory_credit_deferred"),
                ("advice_given_freely", "student", "talk_not_so_advice_given_freely"),
                ("brawl_man_handed_over", "aggrieved", "talk_not_so_brawl_man_handed_over"),
                ("seat_dispute_yielded", "favored", "talk_not_so_seat_dispute_yielded"),
                ("tavern_good_word", "listener", "talk_not_so_tavern_good_word"),
            };

            foreach (var (evtType, role, expectedResponse) in testCases)
            {
                var x = new WorldEvent
                {
                    EventId = "evt_x_" + evtType,
                    Type = evtType,
                    Fabricated = true,
                    OriginatorHeroId = "orig_1",
                    Participants = new Dictionary<string, string>
                    {
                        [role] = "beneficiary_hero"
                    }
                };

                var knowers = GetTruthKnowers(x, null);
                var beneficiary = knowers.FirstOrDefault(k => k.HeroId == "beneficiary_hero");
                Assert.NotNull(beneficiary);
                Assert.Equal(expectedResponse, beneficiary!.ResponseType);
                Assert.Equal("beneficiary", beneficiary.Reason);
            }
        }

        // 22. TruthKnowers: 同一人符合多重身分時，取會出面者或順位較高者
        [Fact]
        public void TruthKnowers_SameHeroQualifiesForMultiple_TakesSteppingForwardOrHigherRow()
        {
            var x = new WorldEvent
            {
                EventId = "evt_x_multi",
                Type = "conduct_rash_capture",
                Fabricated = true,
                OriginatorHeroId = "orig_1",
                Participants = new Dictionary<string, string>
                {
                    ["prisoner"] = "accused_hero",
                    ["captor"] = "captor_hero"
                }
            };
            // 讓 captor_hero 同時出現在掛鉤真事的被動知情者中
            var hungL = new WorldEvent
            {
                EventId = "evt_l_multi",
                Type = "conduct_rash_capture",
                Participants = new Dictionary<string, string>
                {
                    ["prisoner"] = "accused_hero",
                    ["captor"] = "captor_hero"
                }
            };

            var knowers = GetTruthKnowers(x, hungL);
            var captorKnower = knowers.FirstOrDefault(k => k.HeroId == "captor_hero");
            Assert.NotNull(captorKnower);
            // 應取得有 ResponseType 的版本（主動出面），而不是被動參與者（ResponseType == null）
            Assert.Equal("talk_corrected_rash_capture_by_captor", captorKnower!.ResponseType);
            Assert.Equal("captor", captorKnower.Reason);
        }

        // 23. TruthKnowers: 掛鉤真事件載入失敗時，僅依編造事件參與者回傳
        [Fact]
        public void TruthKnowers_HungLLoadFails_UsesParticipantsOnly()
        {
            var x = new WorldEvent
            {
                EventId = "evt_x_nofile",
                Type = "conduct_mistreated_prisoner",
                Fabricated = true,
                OriginatorHeroId = "orig_1",
                Participants = new Dictionary<string, string>
                {
                    ["captor"] = "captor_hero",
                    ["prisoner"] = "prisoner_hero"
                }
            };

            // l 為 null（模擬找不到或載入失敗）
            var knowers = GetTruthKnowers(x, null);
            Assert.Equal(2, knowers.Count);
            Assert.Contains(knowers, k => k.HeroId == "captor_hero" && k.IsAccused);
            Assert.Contains(knowers, k => k.HeroId == "prisoner_hero" && k.ResponseType == "talk_corrected_mistreated_by_prisoner");
        }

        // 24. Grudge: 被指控者聽到壞話，1.5 倍且不受每日上限限制
        [Fact]
        public void Grudge_AccusedBadTalk_1Point5Multiplier_NotCapped()
        {
            var (grudge, isCapped) = CalculateKnowerOriginatorGrudge(
                isAccused: true,
                isGoodTalk: false,
                honor: 0,
                opinionScore: -10.0,
                accusedMultiplier: 1.5);

            Assert.Equal(-15.0, grudge);
            Assert.False(isCapped);
        }

        // 25. Grudge: 被指控者聽到好話，榮譽 >= +1 記恨起頭者，量為半數且受每日上限
        [Fact]
        public void Grudge_AccusedGoodTalk_HonorPositive_ResentsOriginator_Capped()
        {
            var (grudge, isCapped) = CalculateKnowerOriginatorGrudge(
                isAccused: true,
                isGoodTalk: true,
                honor: 1,
                opinionScore: 10.0);

            Assert.Equal(-5.0, grudge);
            Assert.True(isCapped);

            var (grudgeHighHonor, isCappedHigh) = CalculateKnowerOriginatorGrudge(
                isAccused: true,
                isGoodTalk: true,
                honor: 2,
                opinionScore: 8.0);

            Assert.Equal(-4.0, grudgeHighHonor);
            Assert.True(isCappedHigh);
        }

        // 26. Grudge: 被指控者聽到好話，榮譽 <= 0 不記恨
        [Fact]
        public void Grudge_AccusedGoodTalk_HonorZeroOrNegative_NoGrudge()
        {
            var (grudge0, isCapped0) = CalculateKnowerOriginatorGrudge(
                isAccused: true,
                isGoodTalk: true,
                honor: 0,
                opinionScore: 10.0);

            Assert.Equal(0.0, grudge0);
            Assert.True(isCapped0);

            var (grudgeNeg, isCappedNeg) = CalculateKnowerOriginatorGrudge(
                isAccused: true,
                isGoodTalk: true,
                honor: -1,
                opinionScore: 10.0);

            Assert.Equal(0.0, grudgeNeg);
            Assert.True(isCappedNeg);
        }

        // 27. Grudge: 五種好話的被誇者絕不出面
        [Fact]
        public void Grudge_TruthKnowers_PraiseTalkAccused_NeverStepsForward()
        {
            var praiseCases = new[]
            {
                ("victory_credit_deferred", "claimant"),
                ("advice_given_freely", "veteran"),
                ("brawl_man_handed_over", "patron"),
                ("seat_dispute_yielded", "slighted"),
                ("tavern_good_word", "speaker"),
            };

            foreach (var (evtType, role) in praiseCases)
            {
                var x = new WorldEvent
                {
                    EventId = "evt_x_praise_" + evtType,
                    Type = evtType,
                    Fabricated = true,
                    OriginatorHeroId = "orig_1",
                    Participants = new Dictionary<string, string>
                    {
                        [role] = "praised_hero"
                    }
                };

                var knowers = GetTruthKnowers(x, null);
                var praisedKnower = knowers.FirstOrDefault(k => k.HeroId == "praised_hero");
                Assert.NotNull(praisedKnower);
                Assert.True(praisedKnower!.IsAccused);
                Assert.Null(praisedKnower.ResponseType);
            }
        }

        [Fact]
        public void DeniedNamed_WhoFactCarriesOriginator_ForEveryDenialTemplate()
        {
            // 點名的否認句裡有 {ORIGINATOR}；整句句型只拿碎片帶的變數，所以這個名字必須掛在「誰」那一塊上
            string repoRoot = AppContext.BaseDirectory;
            while (!File.Exists(Path.Combine(repoRoot, "VividWorld.sln")))
                repoRoot = Directory.GetParent(repoRoot)!.FullName;

            string sitEventsPath = Path.Combine(repoRoot, "module", "ModuleData", "vividworld_situation_events.json");
            var cat = EventCatalogLoader.Load(File.ReadAllText(sitEventsPath), new PersistenceConfig());
            var denials = cat.Templates.Where(t => string.Equals(t.Response, "denial", StringComparison.OrdinalIgnoreCase)).ToList();
            Assert.NotEmpty(denials);

            foreach (var template in denials)
            {
                string stem = string.Concat(template.Type.Split('_').Select(w => char.ToUpperInvariant(w[0]) + w.Substring(1)));
                var named = TemplateVariants.DeniedNamed(template, stem);

                var bindings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (var role in template.Roles.Keys) bindings[role] = "lord_" + role;
                bindings["originator"] = "lord_originator";
                bindings["settlement"] = "town_test";

                var submission = TemplateBinder.Bind(named, bindings, 100, "evt_root", out var issues);
                Assert.True(submission != null, template.Type + ": " + string.Join("; ", issues.Select(i => i.Detail)));

                var who = submission!.Facts.Single(f => f.TextId.EndsWith("_WhoNamed", StringComparison.Ordinal));
                Assert.Equal("hero:lord_originator", who.Vars!["ORIGINATOR"]);
            }
        }

        [Fact]
        public void ResponseTemplates_RenderRealSamples()
        {
            string repoRoot = "";
            string? current = AppContext.BaseDirectory;
            while (!string.IsNullOrEmpty(current))
            {
                if (File.Exists(Path.Combine(current, "VividWorld.sln")))
                {
                    repoRoot = current;
                    break;
                }
                var parent = Directory.GetParent(current);
                if (parent == null) break;
                current = parent.FullName;
            }

            var pCfg = new PersistenceConfig();
            string sitEventsPath = Path.Combine(repoRoot, "module", "ModuleData", "vividworld_situation_events.json");
            string enPath = Path.Combine(repoRoot, "module", "ModuleData", "Languages", "std_module_strings_xml.xml");
            string cntPath = Path.Combine(repoRoot, "module", "ModuleData", "Languages", "CNt", "std_module_strings_xml.xml");

            var catSit = EventCatalogLoader.Load(File.ReadAllText(sitEventsPath), pCfg);
            var responseTemplates = catSit.Templates.Where(t => !string.IsNullOrEmpty(t.Response)).ToList();

            var enTable = EnglishStringTable.LoadFromFile(enPath);
            var cntTable = EnglishStringTable.LoadFromFile(cntPath);

            string output = TemplateRenderSampleFormatter.FormatAll(responseTemplates, enTable, cntTable);
            Assert.NotEmpty(output);
            string tempFile = Path.Combine(repoRoot, "response_renders_temp.txt");
            if (File.Exists(tempFile))
            {
                File.Delete(tempFile);
            }
        }
    }
}
