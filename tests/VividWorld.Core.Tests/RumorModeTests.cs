using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using VividWorld.Core.Config;
using VividWorld.Core.Dialogue;
using VividWorld.Core.Events;
using VividWorld.Core.Persistence;
using VividWorld.Core.Presentation;
using VividWorld.Core.Rumors;
using VividWorld.Core.Tests.Fakes;
using VividWorld.Core.Util;
using Xunit;

namespace VividWorld.Core.Tests
{
    public class RumorModeTests
    {
        private (RumorOfferSelector selector, RumorEngine engine, VividWorldConfig cfg)
            CreateSelector(string playerHeroId = "player", long seed = 42L, VividWorldConfig? customConfig = null, RumorMode? mode = null)
        {
            var cfg = customConfig ?? new VividWorldConfig();
            var rng = new SplitMix64Rng();
            var channel = new FakePropagationChannel();
            var traits = new FakeHeroTraitLookup();
            var retention = FactRetentionPolicies.Create(cfg, rng, seed);
            var embellishment = NullEmbellishmentPolicy.Instance;
            var engine = new RumorEngine(cfg, retention, embellishment, channel, traits, rng, seed, playerHeroId);
            var selector = new RumorOfferSelector(cfg, engine, playerHeroId, mode);
            return (selector, engine, cfg);
        }

        private WorldEvent CreateSampleEvent(string eventId, double day = 10.0, int drama = 3)
        {
            return new WorldEvent
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
        }

        // ── 1. 模式判定（auto 有／無偵測、明選、未知值） ──

        [Fact]
        public void Resolve_Auto_WithDetected_ReturnsRealistic()
        {
            var result = RumorModeResolver.Resolve("auto", new[] { "NaN" });
            Assert.Equal(RumorMode.Realistic, result.Mode);
            Assert.Equal("realistic", result.ModeString);
            Assert.Equal("auto - detected NaN", result.Reason);
            Assert.Equal(new List<string> { "NaN" }, result.DetectedModules);
        }

        [Fact]
        public void Resolve_Auto_WithoutDetected_ReturnsCasual()
        {
            var result = RumorModeResolver.Resolve("auto", new string[0]);
            Assert.Equal(RumorMode.Casual, result.Mode);
            Assert.Equal("casual", result.ModeString);
            Assert.Equal("auto - none detected", result.Reason);
            Assert.Empty(result.DetectedModules);
        }

        [Fact]
        public void Resolve_ForcedCasual_IgnoresDetected_ReturnsCasual()
        {
            var result = RumorModeResolver.Resolve("casual", new[] { "NaN", "Lowborn" });
            Assert.Equal(RumorMode.Casual, result.Mode);
            Assert.Equal("forced by config", result.Reason);
            Assert.Equal(new List<string> { "NaN", "Lowborn" }, result.DetectedModules);
        }

        [Fact]
        public void Resolve_ForcedRealistic_WithoutDetected_ReturnsRealistic()
        {
            var result = RumorModeResolver.Resolve("realistic", null);
            Assert.Equal(RumorMode.Realistic, result.Mode);
            Assert.Equal("forced by config", result.Reason);
            Assert.Empty(result.DetectedModules);
        }

        [Fact]
        public void Resolve_UnknownOrCaseInsensitive_FallsBackToAuto()
        {
            var upperCasual = RumorModeResolver.Resolve("CASUAL", null);
            Assert.Equal(RumorMode.Casual, upperCasual.Mode);
            Assert.Equal("forced by config", upperCasual.Reason);

            var unknownWithMod = RumorModeResolver.Resolve("some_unknown_mode", new[] { "Lowborn" });
            Assert.Equal(RumorMode.Realistic, unknownWithMod.Mode);
            Assert.Equal("auto - detected Lowborn", unknownWithMod.Reason);

            var unknownNoMod = RumorModeResolver.Resolve("invalid", new string[0]);
            Assert.Equal(RumorMode.Casual, unknownNoMod.Mode);
            Assert.Equal("auto - none detected", unknownNoMod.Reason);
        }

        [Fact]
        public void FormatModeLine_FormatsExpectedString()
        {
            var res = new RumorModeResult(RumorMode.Realistic, "auto - detected NaN", new[] { "NaN" });
            string line = RumorModeResolver.FormatModeLine(res, chatGate: 10, fullGate: 30, gistExtraHops: 2, askClanTier: 1);
            Assert.Equal("Rumor mode: realistic [auto - detected NaN] - chat gate 10, full gate 30, gist +2 hops, ask clan tier 1", line);
        }

        // ── 2. 名單聯集（大小寫、去重、設定是空的） ──

        [Fact]
        public void CommonerModules_Union_CaseInsensitiveDeduplication()
        {
            var configured = new[] { "nan", "LOWBORN", "CustomCommonerMod", "  nan  " };
            var union = CommonerCompat.GetEffectiveCommonerModules(configured);

            // 內建名單 "NaN", "Lowborn" 保留順序，自訂名單附加在後，不分大小寫去重
            Assert.Equal(3, union.Count);
            Assert.Equal("NaN", union[0]);
            Assert.Equal("Lowborn", union[1]);
            Assert.Equal("CustomCommonerMod", union[2]);
        }

        [Fact]
        public void CommonerModules_Union_EmptyOrNullConfigured_ReturnsBuiltIns()
        {
            var unionNull = CommonerCompat.GetEffectiveCommonerModules(null);
            Assert.Equal(new List<string> { "NaN", "Lowborn" }, unionNull);

            var unionEmpty = CommonerCompat.GetEffectiveCommonerModules(new string[0]);
            Assert.Equal(new List<string> { "NaN", "Lowborn" }, unionEmpty);

            var unionWhitespaceOnly = CommonerCompat.GetEffectiveCommonerModules(new[] { "   ", "" });
            Assert.Equal(new List<string> { "NaN", "Lowborn" }, unionWhitespaceOnly);
        }

        // ── 3. 暢玩下問的氏族等級 0／寫實照設定 ──

        [Fact]
        public void ClanTierGate_CasualMode_AskMinClanTierIsZero()
        {
            var cfg = new DialogueConfig
            {
                VolunteerMode = "casual",
                CommonerCompatAskMinClanTier = 2
            };

            var state = CommonerCompat.Resolve(cfg, new[] { "NaN" });
            Assert.True(state.Active);
            Assert.Equal(0, state.AskMinClanTier);
        }

        [Fact]
        public void ClanTierGate_RealisticMode_AskMinClanTierFollowsConfig()
        {
            var cfg = new DialogueConfig
            {
                VolunteerMode = "realistic",
                CommonerCompatAskMinClanTier = 2
            };

            var state = CommonerCompat.Resolve(cfg, new[] { "NaN" });
            Assert.True(state.Active);
            Assert.Equal(2, state.AskMinClanTier);
        }

        // ── 4. 兩層門檻（29／30、0／-1、9／10、近親） ──

        [Fact]
        public void TwoTierGates_CasualMode_29Gist_30Full_0Gist_NegativeOneRefused()
        {
            var (selector, _, _) = CreateSelector(mode: RumorMode.Casual);
            var candidates = new List<RumorCandidate>();

            // -1: 低於閒聊門檻 (0) -> 拒絕 (RelationGate)
            var pNeg = new HeroSocialProfile { HeroId = "p_neg", RelationWithPlayer = -1, LastVolunteeredDay = -1 };
            var dNeg = selector.DecideOnVolunteer(pNeg, candidates, day: 10.0, volunteersAlreadyToday: 0);
            Assert.Equal(VolunteerRefusal.RelationGate, dNeg.Refusal);
            Assert.Equal(VolunteerTier.None, dNeg.Tier);

            // 0: 等於閒聊門檻 (0) -> 大概 (Gist)
            var p0 = new HeroSocialProfile { HeroId = "p_0", RelationWithPlayer = 0, LastVolunteeredDay = -1 };
            var d0 = selector.DecideOnVolunteer(p0, candidates, day: 10.0, volunteersAlreadyToday: 0);
            Assert.Equal(VolunteerRefusal.NoKnownEvents, d0.Refusal);
            Assert.Equal(VolunteerTier.Gist, d0.Tier);

            // 29: 低於完整門檻 (30)，高於閒聊門檻 (0) -> 大概 (Gist)
            var p29 = new HeroSocialProfile { HeroId = "p_29", RelationWithPlayer = 29, LastVolunteeredDay = -1 };
            var d29 = selector.DecideOnVolunteer(p29, candidates, day: 10.0, volunteersAlreadyToday: 0);
            Assert.Equal(VolunteerRefusal.NoKnownEvents, d29.Refusal);
            Assert.Equal(VolunteerTier.Gist, d29.Tier);

            // 30: 達到完整門檻 (30) -> 完整 (Full)
            var p30 = new HeroSocialProfile { HeroId = "p_30", RelationWithPlayer = 30, LastVolunteeredDay = -1 };
            var d30 = selector.DecideOnVolunteer(p30, candidates, day: 10.0, volunteersAlreadyToday: 0);
            Assert.Equal(VolunteerRefusal.NoKnownEvents, d30.Refusal);
            Assert.Equal(VolunteerTier.Full, d30.Tier);
        }

        [Fact]
        public void TwoTierGates_RealisticMode_9Refused_10Gist_29Gist_30Full()
        {
            var (selector, _, _) = CreateSelector(mode: RumorMode.Realistic);
            var candidates = new List<RumorCandidate>();

            // 9: 低於寫實閒聊門檻 (10) -> 拒絕 (RelationGate)
            var p9 = new HeroSocialProfile { HeroId = "p_9", RelationWithPlayer = 9, LastVolunteeredDay = -1 };
            var d9 = selector.DecideOnVolunteer(p9, candidates, day: 10.0, volunteersAlreadyToday: 0);
            Assert.Equal(VolunteerRefusal.RelationGate, d9.Refusal);
            Assert.Equal(VolunteerTier.None, d9.Tier);

            // 10: 達到寫實閒聊門檻 (10) -> 大概 (Gist)
            var p10 = new HeroSocialProfile { HeroId = "p_10", RelationWithPlayer = 10, LastVolunteeredDay = -1 };
            var d10 = selector.DecideOnVolunteer(p10, candidates, day: 10.0, volunteersAlreadyToday: 0);
            Assert.Equal(VolunteerRefusal.NoKnownEvents, d10.Refusal);
            Assert.Equal(VolunteerTier.Gist, d10.Tier);

            // 29: 低於完整門檻 (30)，達到閒聊門檻 (10) -> 大概 (Gist)
            var p29 = new HeroSocialProfile { HeroId = "p_29", RelationWithPlayer = 29, LastVolunteeredDay = -1 };
            var d29 = selector.DecideOnVolunteer(p29, candidates, day: 10.0, volunteersAlreadyToday: 0);
            Assert.Equal(VolunteerRefusal.NoKnownEvents, d29.Refusal);
            Assert.Equal(VolunteerTier.Gist, d29.Tier);

            // 30: 達到完整門檻 (30) -> 完整 (Full)
            var p30 = new HeroSocialProfile { HeroId = "p_30", RelationWithPlayer = 30, LastVolunteeredDay = -1 };
            var d30 = selector.DecideOnVolunteer(p30, candidates, day: 10.0, volunteersAlreadyToday: 0);
            Assert.Equal(VolunteerRefusal.NoKnownEvents, d30.Refusal);
            Assert.Equal(VolunteerTier.Full, d30.Tier);
        }

        [Fact]
        public void TwoTierGates_CloseKin_ExemptFromRelationGate_GetsFullTier()
        {
            var (selector, _, _) = CreateSelector(mode: RumorMode.Realistic);
            var candidates = new List<RumorCandidate>();

            var spouse = new HeroSocialProfile
            {
                HeroId = "spouse",
                RelationWithPlayer = -50,
                IsPlayerSpouse = true,
                LastVolunteeredDay = -1
            };
            var decision = selector.DecideOnVolunteer(spouse, candidates, day: 10.0, volunteersAlreadyToday: 0);
            Assert.Equal(VolunteerTier.Full, decision.Tier);
            Assert.True(decision.IsCloseKin);
            Assert.Equal(VolunteerRefusal.NoKnownEvents, decision.Refusal);
        }

        // ── 5. 落點（一般、碰到最大手數、講述者已在最大手數前一手） ──

        [Fact]
        public void LandingHop_FullTier_AlwaysTellerHopPlusOne()
        {
            Assert.Equal(1, RumorOfferSelector.ComputeLandingHop(tellerHop: 0, maxHop: 5, VolunteerTier.Full, gistExtraHops: 2));
            Assert.Equal(3, RumorOfferSelector.ComputeLandingHop(tellerHop: 2, maxHop: 5, VolunteerTier.Full, gistExtraHops: 2));
            Assert.Equal(5, RumorOfferSelector.ComputeLandingHop(tellerHop: 4, maxHop: 5, VolunteerTier.Full, gistExtraHops: 2));
        }

        [Fact]
        public void LandingHop_GistTier_RegularAddsGistExtraHops()
        {
            // tellerHop = 0, maxHop = 5: direct = 1, gist = 1 + 2 = 3 <= 5 -> 3
            Assert.Equal(3, RumorOfferSelector.ComputeLandingHop(tellerHop: 0, maxHop: 5, VolunteerTier.Gist, gistExtraHops: 2));
            // tellerHop = 1, maxHop = 5: direct = 2, gist = 2 + 2 = 4 <= 5 -> 4
            Assert.Equal(4, RumorOfferSelector.ComputeLandingHop(tellerHop: 1, maxHop: 5, VolunteerTier.Gist, gistExtraHops: 2));
        }

        [Fact]
        public void LandingHop_GistTier_ClampedAtMaxHop()
        {
            // tellerHop = 1, maxHop = 3: direct = 2, gist = 2 + 2 = 4, ceiling = max(2, 3) = 3 -> min(4, 3) = 3
            Assert.Equal(3, RumorOfferSelector.ComputeLandingHop(tellerHop: 1, maxHop: 3, VolunteerTier.Gist, gistExtraHops: 2));
        }

        [Fact]
        public void LandingHop_GistTier_TellerAlreadyOneHopBeforeMax()
        {
            // tellerHop = 4, maxHop = 5: direct = 5, gist = 5 + 2 = 7, ceiling = max(5, 5) = 5 -> min(7, 5) = 5
            Assert.Equal(5, RumorOfferSelector.ComputeLandingHop(tellerHop: 4, maxHop: 5, VolunteerTier.Gist, gistExtraHops: 2));
        }

        [Fact]
        public void LandingHop_GistTier_TellerAlreadyAtMaxHop()
        {
            // tellerHop = 5, maxHop = 5: direct = 6, gist = 6 + 2 = 8, ceiling = max(6, 5) = 6 -> min(8, 6) = 6
            Assert.Equal(6, RumorOfferSelector.ComputeLandingHop(tellerHop: 5, maxHop: 5, VolunteerTier.Gist, gistExtraHops: 2));
        }

        // ── 6. 重述判定用落點 ──

        [Fact]
        public void RetellEvaluation_UsesLandingHop_GistLandsFarther_FilteredAsNotCloser()
        {
            // 玩家目前在 hop 3。
            // 講述者在 hop 1。
            // 完整版落點 = 1 + 1 = 2 < 3 (更近，允許重述)。
            // 大概版落點 = 1 + 1 + 2 = 4 >= 3 (不比已知的近，RetellNotCloser 擋下)。
            var (selector, _, _) = CreateSelector(mode: RumorMode.Casual);
            var evt = CreateSampleEvent("evt_retell_hop", drama: 4);

            var teller = new HeroSocialProfile { HeroId = "teller", RelationWithPlayer = 15, LastVolunteeredDay = -1 };
            evt.KnownBy.Add(new KnownByEntry { HeroId = teller.HeroId, Hop = 1 });
            evt.KnownBy.Add(new KnownByEntry { HeroId = "player", Hop = 3, KnownFactIds = new List<string> { "f_who" } });

            var candidate = new RumorCandidate
            {
                Event = evt,
                TellerHop = 1,
                PlayerExistingHop = 3
            };

            var classificationFull = selector.ClassifyCandidates(teller, new[] { candidate }, day: 10.0, VolunteerTier.Full);
            Assert.Single(classificationFull.Eligible);

            var classificationGist = selector.ClassifyCandidates(teller, new[] { candidate }, day: 10.0, VolunteerTier.Gist);
            Assert.Empty(classificationGist.Eligible);
            Assert.Equal(1, classificationGist.FilteredPlayerKnows);
            Assert.Contains("no closer than the hop 3", classificationGist.FilterNotes[0]);
        }

        // ── 7. 玩家紀錄寫落點 ──

        [Fact]
        public void ApplyOffer_WritesLandingHopIntoPlayerKnownByEntry()
        {
            var (selector, _, _) = CreateSelector(mode: RumorMode.Casual);
            var evt = CreateSampleEvent("evt_apply_landing", drama: 4);

            var teller = new HeroSocialProfile { HeroId = "teller", RelationWithPlayer = 15, LastVolunteeredDay = -1 };
            evt.KnownBy.Add(new KnownByEntry { HeroId = teller.HeroId, Hop = 0 });

            var candidates = new List<RumorCandidate>
            {
                new() { Event = evt, TellerHop = 0, PlayerExistingHop = null }
            };

            // 15 好感在 Casual 下為 Gist，tellerHop 0 + 1 + 2 = 3
            var decision = selector.DecideOnVolunteer(teller, candidates, day: 10.0, volunteersAlreadyToday: 0);
            Assert.Equal(VolunteerTier.Gist, decision.Tier);
            Assert.NotNull(decision.Offer);
            Assert.Equal(3, decision.Offer!.ResultingPlayerHop);

            selector.ApplyOffer(decision.Offer, evt, teller.HeroId, day: 10.0);
            var playerEntry = evt.EntryFor("player");
            Assert.NotNull(playerEntry);
            Assert.Equal(3, playerEntry!.Hop);
        }

        // ── 8. 前綴只在手數 0 ──

        [Fact]
        public void EyewitnessPrefix_AddedOnlyWhenTellerHopIsZero()
        {
            var (selector, _, _) = CreateSelector();
            var evt = CreateSampleEvent("evt_prefix", drama: 4);
            evt.KnownBy.Add(new KnownByEntry { HeroId = "player", Hop = 3, KnownFactIds = new List<string> { "f_who" } });

            // 目擊者 (tellerHop == 0) 的重述 -> 帶前綴
            var teller0 = new HeroSocialProfile { HeroId = "teller_0", RelationWithPlayer = 30, LastVolunteeredDay = -1 };
            evt.KnownBy.Add(new KnownByEntry { HeroId = teller0.HeroId, Hop = 0 });
            var candidates0 = new List<RumorCandidate>
            {
                new() { Event = evt, TellerHop = 0, PlayerExistingHop = 3 }
            };
            var d0 = selector.DecideOnVolunteer(teller0, candidates0, day: 10.0, volunteersAlreadyToday: 0);
            Assert.NotNull(d0.Offer);
            Assert.NotNull(d0.Offer!.Composed.PrefixFallback);
            Assert.Equal(RumorTextComposer.RetellPrefixFallback, d0.Offer!.Composed.PrefixFallback);
            Assert.Equal("VividWorld_RetellPrefix", d0.Offer!.Composed.PrefixTextId);

            // 非目擊者 (tellerHop == 1) 的重述 -> 不帶前綴
            var teller1 = new HeroSocialProfile { HeroId = "teller_1", RelationWithPlayer = 30, LastVolunteeredDay = -1 };
            evt.KnownBy.Add(new KnownByEntry { HeroId = teller1.HeroId, Hop = 1 });
            var candidates1 = new List<RumorCandidate>
            {
                new() { Event = evt, TellerHop = 1, PlayerExistingHop = 3 }
            };
            var d1 = selector.DecideOnVolunteer(teller1, candidates1, day: 10.0, volunteersAlreadyToday: 0);
            Assert.NotNull(d1.Offer);
            Assert.Null(d1.Offer!.Composed.PrefixFallback);
            Assert.Null(d1.Offer!.Composed.PrefixTextId);
        }

        // ── 9. DecideOnAsk 不變 ──

        [Fact]
        public void DecideOnAsk_AlwaysUsesFullTier_LandingHopEqualsTellerHopPlusOne()
        {
            var (selector, _, _) = CreateSelector(mode: RumorMode.Casual);
            var evt = CreateSampleEvent("evt_ask_tier", drama: 4);

            var teller = new HeroSocialProfile
            {
                HeroId = "teller",
                RelationWithPlayer = 20,
                Traits = new TraitProfile { Generosity = 1 }
            };
            evt.KnownBy.Add(new KnownByEntry { HeroId = teller.HeroId, Hop = 0 });

            var candidates = new List<RumorCandidate>
            {
                new() { Event = evt, TellerHop = 0, PlayerExistingHop = null }
            };

            var askDecision = selector.DecideOnAsk(teller, candidates, day: 10.0);
            Assert.Equal(AskRefusal.None, askDecision.Refusal);
            Assert.NotNull(askDecision.Offer);
            // 詢問永遠是完整版，直接降 1 手 (0 + 1 = 1)，絕不受 GistExtraHops 影響
            Assert.Equal(1, askDecision.Offer!.ResultingPlayerHop);
        }

        // ── 10. 告知判斷（第一次、一樣、模式變、名單變、明選、紀錄壞掉） ──

        [Fact]
        public void NoticeEvaluation_FirstTime_ReturnsPopup()
        {
            var action = ModeNotice.Evaluate(
                configuredMode: "auto",
                actualMode: RumorMode.Realistic,
                detectedModules: new[] { "NaN" },
                lastRecord: null);

            Assert.Equal(ModeNoticeAction.Popup, action);
        }

        [Fact]
        public void NoticeEvaluation_Identical_ReturnsMessage()
        {
            var last = new ModeNoticeRecord
            {
                Mode = "realistic",
                Detected = new List<string> { "Lowborn", "NaN" }
            };

            // detectedModules 順序不同但元素相同時，排序後視為一致
            var action = ModeNotice.Evaluate(
                configuredMode: "auto",
                actualMode: RumorMode.Realistic,
                detectedModules: new[] { "NaN", "Lowborn" },
                lastRecord: last);

            Assert.Equal(ModeNoticeAction.Message, action);
        }

        [Fact]
        public void NoticeEvaluation_ModeChanged_ReturnsPopup()
        {
            var last = new ModeNoticeRecord
            {
                Mode = "casual",
                Detected = new List<string>()
            };

            var action = ModeNotice.Evaluate(
                configuredMode: "auto",
                actualMode: RumorMode.Realistic,
                detectedModules: new[] { "NaN" },
                lastRecord: last);

            Assert.Equal(ModeNoticeAction.Popup, action);
        }

        [Fact]
        public void NoticeEvaluation_DetectedListChanged_ReturnsPopup()
        {
            var last = new ModeNoticeRecord
            {
                Mode = "realistic",
                Detected = new List<string> { "NaN" }
            };

            var action = ModeNotice.Evaluate(
                configuredMode: "auto",
                actualMode: RumorMode.Realistic,
                detectedModules: new[] { "NaN", "Lowborn" },
                lastRecord: last);

            Assert.Equal(ModeNoticeAction.Popup, action);
        }

        [Fact]
        public void NoticeEvaluation_ForcedMode_ReturnsNone()
        {
            var actionCasual = ModeNotice.Evaluate(
                configuredMode: "casual",
                actualMode: RumorMode.Casual,
                detectedModules: new string[0],
                lastRecord: null);
            Assert.Equal(ModeNoticeAction.None, actionCasual);

            var actionRealistic = ModeNotice.Evaluate(
                configuredMode: "realistic",
                actualMode: RumorMode.Realistic,
                detectedModules: new[] { "NaN" },
                lastRecord: null);
            Assert.Equal(ModeNoticeAction.None, actionRealistic);
        }

        [Fact]
        public void NoticeEvaluation_CorruptedOrEmptyRecord_ReturnsPopup()
        {
            var emptyRecord = new ModeNoticeRecord { Mode = "" };
            var action = ModeNotice.Evaluate(
                configuredMode: "auto",
                actualMode: RumorMode.Casual,
                detectedModules: new string[0],
                lastRecord: emptyRecord);

            Assert.Equal(ModeNoticeAction.Popup, action);
        }

        // ── 11. 紀錄檔往返 ──

        [Fact]
        public void NoticeFile_RoundTrip_SaveAndLoad()
        {
            var fakeFs = new FailingFileWriter();
            string path = "mode_notice.json";

            var record = new ModeNoticeRecord
            {
                Mode = "realistic",
                Detected = new List<string> { "NaN", "Lowborn" }
            };

            bool saved = ModeNotice.Save(path, record, fakeFs);
            Assert.True(saved);

            var loaded = ModeNotice.Load(path, fakeFs);
            Assert.NotNull(loaded);
            Assert.Equal("realistic", loaded!.Mode);
            // 儲存時排序: Lowborn, NaN
            Assert.Equal(new List<string> { "Lowborn", "NaN" }, loaded.Detected);
        }

        [Fact]
        public void NoticeFile_MissingOrCorrupted_ReturnsNullWithoutException()
        {
            var fakeFs = new FailingFileWriter();

            // 檔案不存在
            var notFound = ModeNotice.Load("missing.json", fakeFs);
            Assert.Null(notFound);

            // 檔案壞損
            fakeFs.Files["bad.json"] = "{ not valid json ... ";
            var bad = ModeNotice.Load("bad.json", fakeFs);
            Assert.Null(bad);
        }

        // ── 12. 新字串鍵英文與 CNt 都在 ──

        [Fact]
        public void LocalizationStrings_ModeNoticeAndMcmKeys_PresentInBothStdAndCNt()
        {
            string repoRoot = FindRepoRoot();
            string stdPath = Path.Combine(repoRoot, "module", "ModuleData", "Languages", "std_module_strings_xml.xml");
            string cntPath = Path.Combine(repoRoot, "module", "ModuleData", "Languages", "CNt", "std_module_strings_xml.xml");

            Assert.True(File.Exists(stdPath), $"Missing std string table: {stdPath}");
            Assert.True(File.Exists(cntPath), $"Missing CNt string table: {cntPath}");

            var stdDoc = XDocument.Load(stdPath);
            var cntDoc = XDocument.Load(cntPath);

            var requiredKeys = new[]
            {
                "VividWorld_ModeNotice_Title",
                "VividWorld_ModeNotice_Button",
                "VividWorld_ModeNotice_PopupRealistic_Mcm",
                "VividWorld_ModeNotice_PopupRealistic_NoMcm",
                "VividWorld_ModeNotice_PopupCasual_Mcm",
                "VividWorld_ModeNotice_PopupCasual_NoMcm",
                "VividWorld_ModeNotice_MessageRealistic_Mcm",
                "VividWorld_ModeNotice_MessageRealistic_NoMcm",
                "VividWorld_ModeNotice_MessageCasual_Mcm",
                "VividWorld_ModeNotice_MessageCasual_NoMcm",
                "VividWorld_MCM_VolunteerMode",
                "VividWorld_MCM_VolunteerModeHint",
                "VividWorld_MCM_NpcVolunteerRelationGateHint"
            };

            var stdKeys = stdDoc.Descendants("string").Select(e => (string)e.Attribute("id")!).ToHashSet();
            var cntKeys = cntDoc.Descendants("string").Select(e => (string)e.Attribute("id")!).ToHashSet();

            foreach (var key in requiredKeys)
            {
                Assert.True(stdKeys.Contains(key), $"Missing key in std_module_strings_xml: {key}");
                Assert.True(cntKeys.Contains(key), $"Missing key in CNt/std_module_strings_xml: {key}");
            }
        }

        // ── 13. MCM 鍵清單（多一個、少一個） ──

        [Fact]
        public void McmExposedKeys_VolunteerModeAdded_CommonerCompatModeRemoved()
        {
            Assert.Contains(McmExposedKeys.All, k => k.Path == "dialogue.volunteerMode");
            Assert.DoesNotContain(McmExposedKeys.All, k => k.Path == "dialogue.commonerCompatMode");
            Assert.Equal(24, McmExposedKeys.All.Count);
        }

        // ── 14. 預演多出來的格式化 ──

        [Fact]
        public void ListenPreview_Formatting_IncludesModeLineAndFullGistCounts()
        {
            var res = new ListenPreviewResult
            {
                Day = 15,
                TotalNetworkCount = 2,
                LordCount = 2,
                VolunteersToday = 0,
                MaxVolunteersPerDay = 1,
                VolunteerRelationGate = 30,
                HeroesMeetingVolunteerRelationGate = 1,
                ChatRelationGate = 0,
                HeroesMeetingChatRelationGate = 2,
                VolunteerToldFullCount = 1,
                VolunteerToldGistCount = 1,
                ModeLine = "Rumor mode: casual [auto - none detected] - chat gate 0, full gate 30, gist +2 hops, ask clan tier 0"
            };

            res.VolunteerCounts[ListenTallyKeys.VolunteerTold] = 2;

            string summary = ListenPreviewLogFormatter.FormatSummary(res);
            Assert.Contains("Rumor mode: casual [auto - none detected]", summary);
            Assert.Contains("Relation >= 0 (chat gate): 2 heroes", summary);
            Assert.Contains("- volunteer.told: 2 (full 1, gist 1)", summary);
        }

        private static string FindRepoRoot()
        {
            string dir = AppContext.BaseDirectory;
            while (!string.IsNullOrEmpty(dir))
            {
                if (File.Exists(Path.Combine(dir, "VividWorld.sln")))
                {
                    return dir;
                }
                dir = Path.GetDirectoryName(dir)!;
            }
            throw new InvalidOperationException("Could not find repository root containing VividWorld.sln");
        }
    }
}
