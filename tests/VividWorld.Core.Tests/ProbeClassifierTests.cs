#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using VividWorld.Core.Catalog;
using VividWorld.Core.Config;
using VividWorld.Core.Dialogue;
using VividWorld.Core.Events;
using VividWorld.Core.Feelings;
using VividWorld.Core.Grudges;
using VividWorld.Core.Memory;
using VividWorld.Core.Persistence;
using VividWorld.Core.Presentation;
using VividWorld.Core.Rumors;
using VividWorld.Core.Tests.Fakes;
using Xunit;

namespace VividWorld.Core.Tests
{
    /// <summary>打探時對方怎麼答：九列判定、優先順序、說溜嘴、輪替、讓被說的人知道、稱呼。</summary>
    public class ProbeClassifierTests
    {
        private const string He = "he";
        private const string Player = "player";
        private const double Today = 100.0;

        // ───────────── 手造資料 ─────────────

        private static EventTemplate Tpl(string type, double opinion = 0, string? response = null,
            Dictionary<string, SelfTellRule>? selfTell = null, EventOrigin origin = EventOrigin.Public)
        {
            var t = new EventTemplate { Type = type, Origin = origin, Response = response, SelfTell = selfTell };
            if (opinion != 0)
            {
                t.Opinions = new List<OpinionDef> { new OpinionDef { About = "x", Amount = opinion } };
            }
            return t;
        }

        private static Dictionary<string, EventTemplate> Templates() => new(StringComparer.Ordinal)
        {
            // 壞話
            ["conduct_spoke_against_ruler"] = Tpl("conduct_spoke_against_ruler", -10,
                selfTell: new Dictionary<string, SelfTellRule> { ["speaker"] = SelfTellRule.Never }),
            ["conduct_refused_aid"] = Tpl("conduct_refused_aid", -10),
            ["conduct_rash_capture"] = Tpl("conduct_rash_capture", -10),
            ["conduct_mistreated_prisoner"] = Tpl("conduct_mistreated_prisoner", -10),
            ["conduct_poisoned"] = Tpl("conduct_poisoned", -10, origin: EventOrigin.Secret),
            // 好話
            ["victory_credit_deferred"] = Tpl("victory_credit_deferred", +10),
            ["tavern_boast_told"] = Tpl("tavern_boast_told", +5),
            // 回應
            ["talk_denied_refused_aid"] = Tpl("talk_denied_refused_aid", 0, response: "deny"),
            // 其他
            ["hero_captured"] = Tpl("hero_captured"),
            ["hero_died_naturally"] = Tpl("hero_died_naturally")
        };

        private static KnownByEntry K(string hero, int hop, string? source = null, bool? believes = null, string? reason = null, double? forget = null)
            => new KnownByEntry { HeroId = hero, Hop = hop, SourceHeroId = source, Believes = believes, BeliefReason = reason, ForgetDay = forget, LearnedDay = 90 };

        private static WorldEvent Evt(string id, string type, Dictionary<string, string> parts, double day = 50,
            bool fabricated = false, string? originator = null, string? linked = null, string? situation = null,
            EventOrigin origin = EventOrigin.Public, params KnownByEntry[] knownBy)
        {
            return new WorldEvent
            {
                EventId = id,
                Type = type,
                Day = day,
                Origin = origin,
                State = new RumorState { Leaked = true },
                Participants = parts,
                Fabricated = fabricated,
                OriginatorHeroId = originator,
                LinkedEventId = linked,
                SituationId = situation,
                KnownBy = knownBy.ToList()
            };
        }

        private static Dictionary<string, string> P(params (string role, string hero)[] parts)
            => parts.ToDictionary(p => p.role, p => p.hero, StringComparer.Ordinal);

        private static PlayerHeardEntry Heard(string eventId, string type, string root, int hop = 2, double updated = 90, params string[] factIds)
            => new PlayerHeardEntry
            {
                EventId = eventId,
                Type = type,
                RootEventId = root,
                PlayerHop = hop,
                UpdatedDay = updated,
                Facts = factIds.Select(f => new Fact { Id = f }).ToList()
            };

        private sealed class Run
        {
            public ProbePlan Plan = null!;
            public List<string> Asked = new();
            public int OffersBuilt;
        }

        private static Run Go(IEnumerable<WorldEvent> c, IEnumerable<PlayerHeardEntry> e, string root,
            Func<string, CandidateRejection>? canTell = null, IHeroTraitLookup? traits = null,
            IFeelingWorld? world = null, double today = Today, long seed = 7, VividWorldConfig? cfg = null,
            string he = He)
        {
            var run = new Run();
            var tpl = Templates();
            cfg ??= new VividWorldConfig();
            run.Plan = ProbeClassifier.Classify(
                he, Player, new ChronicleEntry { EventId = root }, e.ToList(), c.ToList(), today, seed, cfg,
                t => tpl.TryGetValue(t, out var v) ? v : null,
                id => { run.Asked.Add(id); return canTell != null ? canTell(id) : CandidateRejection.None; },
                id => { run.OffersBuilt++; return new RumorOffer { EventId = id }; },
                world, traits);
            return run;
        }

        private sealed class StubWorld : IFeelingWorld
        {
            public double Today => 100.0;
            public int? Affection(string speakerId, string heroId) => 0;
            public int? StandingRank(string heroId) => 0;
            public InterestHeroFacts? InterestFacts(string heroId) => new InterestHeroFacts(heroId, clanId: null);
            public IReadOnlyList<GrudgeEntry> PersonalGrudges(string speakerId, string heroId) => new List<GrudgeEntry>();
            public string NameOf(string heroId) => heroId;
            public bool? IsFemale(string heroId) => null;
        }

        private static FakeHeroTraitLookup Traits(string hero, int calculating)
        {
            var t = new FakeHeroTraitLookup();
            t.Set(new TraitProfile { HeroId = hero, Calculating = calculating });
            return t;
        }

        private static VividWorldConfig Slip(double baseChance, double rash = 20, double calc = -10)
        {
            var cfg = new VividWorldConfig();
            cfg.FalseRumors.Slip.BaseChance = baseChance;
            cfg.FalseRumors.Slip.RashBonus = rash;
            cfg.FalseRumors.Slip.CalculatingBonus = calc;
            return cfg;
        }

        // ───────────── 九列各一條 ─────────────

        // 一件編的話：說他（he）在背後說君主
        private static WorldEvent SlanderAboutHe(string id = "x1", string? originator = "orig", string situation = "madeup_slander", params KnownByEntry[] knownBy)
            => Evt(id, "conduct_spoke_against_ruler", P(("speaker", He), ("ruler", "king")), 50, true, originator, null, situation, EventOrigin.Public, knownBy);

        [Fact]
        public void Row1_Accused_BadTalk_DeniesNamed_WhenPlayerKnowsWhoStartedIt()
        {
            var x = SlanderAboutHe(knownBy: K(Player, 2, "someone"));
            var run = Go(new[] { x }, new[] { Heard("x1", x.Type, "x1", hop: 2, factIds: new[] { "f1", "f2" }) }, "x1");

            Assert.Equal(ProbeResultKind.AccusedDeny, run.Plan.ResultKind);
            Assert.Equal("accused-deny", run.Plan.ResultKindLabel);
            Assert.Equal("VividWorld_Probe_Accused_DenyNamed", run.Plan.SentenceKey);
            Assert.Equal("orig", run.Plan.Vars["ORIGINATOR"]);
            Assert.True(run.Plan.ShouldDeliverToAccused);
        }

        [Fact]
        public void Row1_Accused_BadTalk_DeniesUnnamed_WhenPlayerDoesNotKnowWhoStartedIt()
        {
            var x = SlanderAboutHe(knownBy: K(Player, 1, "someone"));
            var run = Go(new[] { x }, new[] { Heard("x1", x.Type, "x1", hop: 1) }, "x1");

            Assert.Equal("VividWorld_Probe_Accused_DenyUnnamed", run.Plan.SentenceKey);
            Assert.False(run.Plan.Vars.ContainsKey("ORIGINATOR"));
        }

        [Fact]
        public void Row2_Accused_GoodTalk_PraiseSubject()
        {
            var x = Evt("x2", "victory_credit_deferred", P(("claimant", He), ("rival", "rv")), 50, true, "orig", null, "madeup_praise",
                knownBy: K(Player, 2, "s"));
            var run = Go(new[] { x }, new[] { Heard("x2", x.Type, "x2") }, "x2");

            Assert.Equal(ProbeResultKind.AccusedPraise, run.Plan.ResultKind);
            Assert.Equal("VividWorld_Probe_Accused_PraiseSubject", run.Plan.SentenceKey);
            Assert.True(run.Plan.ShouldDeliverToAccused);
        }

        [Fact]
        public void Row3_RealBadDeed_WhereSelfTellIsNever_RefusesToTalk()
        {
            // 說君主的人：模板裡 speaker 是 never；是真的發生過的事
            var real = Evt("r1", "conduct_spoke_against_ruler", P(("speaker", He), ("ruler", "king")), 40, false, null, null, null,
                EventOrigin.Public, K(He, 0));
            var run = Go(new[] { real }, new[] { Heard("r1", real.Type, "r1") }, "r1");

            Assert.Equal(ProbeResultKind.AccusedRefuse, run.Plan.ResultKind);
            Assert.Equal("accused-refuse", run.Plan.ResultKindLabel);
            Assert.StartsWith("VividWorld_Probe_Refuse_", run.Plan.SentenceKey);
            Assert.Contains("selfTell never", run.Plan.LogReason);
            Assert.Empty(run.Asked);       // 第 3 列成立，後面的「肯不肯講」不用問
        }

        [Fact]
        public void Row3_RealBadDeed_SecretPoisoner_IsNotRefusedByRow3_TrustedAsker_Tells()
        {
            // 下毒的人是秘密：不走第 3 列。信得過的人問（既有秘密規則放行）⇒ 講
            var real = Evt("p1", "conduct_poisoned", P(("poisoner", He), ("victim", "v")), 40, false, null, null, null,
                EventOrigin.Secret, K(He, 0));
            real.State.Leaked = true;
            var run = Go(new[] { real }, new[] { Heard("p1", real.Type, "p1") }, "p1", canTell: _ => CandidateRejection.None);

            Assert.Equal(ProbeResultKind.Tell, run.Plan.ResultKind);
            Assert.Equal("p1", run.Plan.EventId);
            Assert.NotNull(run.Plan.Offer);
        }

        [Fact]
        public void Row3_RealBadDeed_SecretPoisoner_NotTrustedEnough_RefusesToTell()
        {
            var real = Evt("p1", "conduct_poisoned", P(("poisoner", He), ("victim", "v")), 40, false, null, null, null,
                EventOrigin.Secret, K(He, 0));
            real.State.Leaked = true;
            var run = Go(new[] { real }, new[] { Heard("p1", real.Type, "p1") }, "p1",
                canTell: _ => CandidateRejection.SecretHolderNotWilling);

            Assert.Equal(ProbeResultKind.RefuseToTell, run.Plan.ResultKind);
            Assert.Equal("refuse-to-tell", run.Plan.ResultKindLabel);
            Assert.Contains("SecretHolderNotWilling", run.Plan.LogReason);
            Assert.DoesNotContain("selfTell never for role", run.Plan.LogReason);
        }

        [Fact]
        public void Row3_UsesTheTemplateRule_NotTheRoleName()
        {
            // 同一個角色名 speaker，模板改成「要有誠實才講」（不是 never）⇒ 不走第 3 列
            var tpl = Templates();
            tpl["conduct_spoke_against_ruler"] = Tpl("conduct_spoke_against_ruler", -10,
                selfTell: new Dictionary<string, SelfTellRule> { ["speaker"] = new SelfTellRule("honor", 1) });
            var real = Evt("r1", "conduct_spoke_against_ruler", P(("speaker", He), ("ruler", "king")), 40, false, null, null, null,
                EventOrigin.Public, K(He, 0));

            var plan = ProbeClassifier.Classify(He, Player, new ChronicleEntry { EventId = "r1" },
                new[] { Heard("r1", real.Type, "r1") }, new[] { real }, Today, 7, new VividWorldConfig(),
                t => tpl.TryGetValue(t, out var v) ? v : null, _ => CandidateRejection.None,
                id => new RumorOffer { EventId = id }, null, null);

            Assert.NotEqual(ProbeResultKind.AccusedRefuse, plan.ResultKind);
            Assert.Equal(ProbeResultKind.Tell, plan.ResultKind);
        }

        [Fact]
        public void Row4_Originator_Slander_InsistsOrSlips_WithTheMatchingSentences()
        {
            var x = Evt("x1", "conduct_spoke_against_ruler", P(("speaker", "acc"), ("ruler", "king")), 50, true, He, null, "madeup_slander", EventOrigin.Public, K(He, 0));
            var heard = new[] { Heard("x1", x.Type, "x1") };

            var insist = Go(new[] { x }, heard, "x1", cfg: Slip(0), world: new StubWorld());
            Assert.Equal(ProbeResultKind.OriginatorInsist, insist.Plan.ResultKind);
            Assert.Contains(insist.Plan.SentenceKey, new[] { "VividWorld_Probe_Insist_1", "VividWorld_Probe_Insist_2" });

            var slip = Go(new[] { x }, heard, "x1", cfg: Slip(100), world: new StubWorld());
            Assert.Equal(ProbeResultKind.OriginatorSlip, slip.Plan.ResultKind);
            Assert.Contains(slip.Plan.SentenceKey, new[] { "VividWorld_Probe_Slip_Grudge_1", "VividWorld_Probe_Slip_Grudge_2" });
            Assert.Equal("acc", slip.Plan.AddressHeroId);
            Assert.False(string.IsNullOrEmpty(slip.Plan.AddressKey));
        }

        [Fact]
        public void Row4_Originator_Rivalry_And_Praise_And_Boast_AndUnknownSituation()
        {
            var rivalry = Evt("x1", "conduct_spoke_against_ruler", P(("speaker", "acc"), ("ruler", "k")), 50, true, He, null, "madeup_rivalry", EventOrigin.Public, K(He, 0));
            var rSlip = Go(new[] { rivalry }, new[] { Heard("x1", rivalry.Type, "x1") }, "x1", cfg: Slip(100), world: new StubWorld());
            Assert.Equal("VividWorld_Probe_Slip_Rivalry", rSlip.Plan.SentenceKey);
            Assert.Equal("acc", rSlip.Plan.AddressHeroId);
            var rInsist = Go(new[] { rivalry }, new[] { Heard("x1", rivalry.Type, "x1") }, "x1", cfg: Slip(0), world: new StubWorld());
            Assert.Contains(rInsist.Plan.SentenceKey, new[] { "VividWorld_Probe_Insist_1", "VividWorld_Probe_Insist_2" });

            var praise = Evt("x2", "victory_credit_deferred", P(("claimant", "friend"), ("rival", "rv")), 50, true, He, null, "madeup_praise", EventOrigin.Public, K(He, 0));
            Assert.Equal("VividWorld_Probe_Slip_Praise", Go(new[] { praise }, new[] { Heard("x2", praise.Type, "x2") }, "x2", cfg: Slip(100)).Plan.SentenceKey);
            Assert.Contains(Go(new[] { praise }, new[] { Heard("x2", praise.Type, "x2") }, "x2", cfg: Slip(0)).Plan.SentenceKey,
                new[] { "VividWorld_Probe_Insist_1", "VividWorld_Probe_Insist_2" });

            // 誇戰功：起頭的人就是誇的那個人（他在參與者裡，所以不是「只聽來的」）
            var boast = Evt("b1", "tavern_boast_told", P(("speaker", He)), 50, true, He, null, null, EventOrigin.Public, K(He, 0));
            Assert.Equal("VividWorld_Probe_Slip_Boast", Go(new[] { boast }, new[] { Heard("b1", boast.Type, "b1") }, "b1", cfg: Slip(100)).Plan.SentenceKey);
            Assert.Equal("VividWorld_Probe_Insist_Boast", Go(new[] { boast }, new[] { Heard("b1", boast.Type, "b1") }, "b1", cfg: Slip(0)).Plan.SentenceKey);

            // 查不到起因：不擲，一律堅持，日誌寫明
            var unknown = Evt("x3", "conduct_spoke_against_ruler", P(("speaker", "acc"), ("ruler", "k")), 50, true, He, null, "something_else", EventOrigin.Public, K(He, 0));
            var u = Go(new[] { unknown }, new[] { Heard("x3", unknown.Type, "x3") }, "x3", cfg: Slip(100));
            Assert.Equal(ProbeResultKind.OriginatorInsist, u.Plan.ResultKind);
            Assert.Contains(u.Plan.SentenceKey, new[] { "VividWorld_Probe_Insist_1", "VividWorld_Probe_Insist_2" });
            Assert.Contains("no slip roll", u.Plan.LogReason);
        }

        [Fact]
        public void Row5_TruthKnower_WithAFaceToFaceAnswer_GetsThatSentence_WithRolePlaceholders()
        {
            // 說「拒絕求援」的編的話；他是求援的那一個（asker），當面回答
            var x = Evt("x1", "conduct_refused_aid", P(("refuser", "acc"), ("asker", He)), 50, true, "orig", null, "madeup_slander", EventOrigin.Public, K(Player, 2, "s"));
            var run = Go(new[] { x }, new[] { Heard("x1", x.Type, "x1") }, "x1");

            Assert.Equal(ProbeResultKind.TruthFace, run.Plan.ResultKind);
            Assert.Equal("VividWorld_Probe_Truth_RefusedAidByAsker", run.Plan.SentenceKey);
            Assert.Equal("acc", run.Plan.Vars["REFUSER"]);
            Assert.Equal(He, run.Plan.Vars["ASKER"]);
        }

        [Fact]
        public void TruthFaceKey_MapsEveryFaceToFaceResponse_AndNothingElse()
        {
            var expected = new Dictionary<string, string>
            {
                ["talk_corrected_rash_capture_by_captor"] = "VividWorld_Probe_Truth_RashCaptureByCaptor",
                ["talk_corrected_rash_capture_by_bystander"] = "VividWorld_Probe_Truth_RashCaptureByBystander",
                ["talk_corrected_mistreated_by_prisoner"] = "VividWorld_Probe_Truth_MistreatedByPrisoner",
                ["talk_corrected_refused_aid_by_asker"] = "VividWorld_Probe_Truth_RefusedAidByAsker",
                ["talk_not_so_victory_credit_deferred"] = "VividWorld_Probe_Truth_NotSo_VictoryCreditDeferred",
                ["talk_not_so_advice_given_freely"] = "VividWorld_Probe_Truth_NotSo_AdviceGivenFreely",
                ["talk_not_so_brawl_man_handed_over"] = "VividWorld_Probe_Truth_NotSo_BrawlManHandedOver",
                ["talk_not_so_seat_dispute_yielded"] = "VividWorld_Probe_Truth_NotSo_SeatDisputeYielded",
                ["talk_not_so_tavern_good_word"] = "VividWorld_Probe_Truth_NotSo_TavernGoodWord"
            };
            foreach (var kv in expected) Assert.Equal(kv.Value, ProbeClassifier.TruthFaceKey(kv.Key));
            Assert.Null(ProbeClassifier.TruthFaceKey("talk_corrected_mistreated_by_comrade"));
            Assert.Null(ProbeClassifier.TruthFaceKey(null));
            Assert.Null(ProbeClassifier.TruthFaceKey("talk_denied_poisoned"));
        }

        [Fact]
        public void Row6_TruthKnower_WithoutAFaceToFaceAnswer_TellsWhatHeSaw()
        {
            // 說「苛待俘虜」的編的話；他是同軍團的人（沒有當面那一句），講他看到的那件真事
            var l = Evt("l1", "hero_captured", P(("prisoner", "pr"), ("captor", "cp")), 30, false, null, null, null, EventOrigin.Public, K(He, 0));
            l.CaptorArmyLeaderHeroIds = new List<string> { He };
            var x = Evt("x1", "conduct_mistreated_prisoner", P(("prisoner", "pr"), ("captor", "cp")), 50, true, "orig", "l1", "madeup_slander", EventOrigin.Public, K(Player, 2, "s"));
            var run = Go(new[] { x, l }, new[] { Heard("x1", x.Type, "x1") }, "x1");

            Assert.Equal(ProbeResultKind.TruthTell, run.Plan.ResultKind);
            Assert.Equal("l1", run.Plan.EventId);
            Assert.Equal("l1", run.Plan.Offer!.EventId);
            Assert.Equal(new[] { "l1" }, run.Asked);
        }

        [Fact]
        public void Row6_TruthKnower_ReluctantToTell_RefusesToTell_AndInvisibleFallsThrough()
        {
            var l = Evt("l1", "hero_captured", P(("prisoner", "pr"), ("captor", "cp")), 30, false, null, null, null, EventOrigin.Public, K(He, 0));
            l.CaptorArmyLeaderHeroIds = new List<string> { He };
            var x = Evt("x1", "conduct_mistreated_prisoner", P(("prisoner", "pr"), ("captor", "cp")), 50, true, "orig", "l1", "madeup_slander", EventOrigin.Public, K(Player, 2, "s"));
            var heard = new[] { Heard("x1", x.Type, "x1") };

            var reluctant = Go(new[] { x, l }, heard, "x1", canTell: _ => CandidateRejection.HeldBackShameful);
            Assert.Equal(ProbeResultKind.RefuseToTell, reluctant.Plan.ResultKind);

            var invisible = Go(new[] { x, l }, heard, "x1", canTell: _ => CandidateRejection.NotVisible);
            Assert.Equal(ProbeResultKind.NotHeard, invisible.Plan.ResultKind);
            Assert.Contains("NotVisible", invisible.Plan.LogReason);
        }

        [Fact]
        public void Row7_TwoVersions_BelieveAndReasonPickTheSentence()
        {
            var x = Evt("x1", "conduct_refused_aid", P(("refuser", "acc"), ("asker", "ask")), 50, true, "orig", null, "madeup_slander", EventOrigin.Public,
                K(He, 2, "src", believes: false, reason: "TraitFit"));
            var resp = Evt("y1", "talk_denied_refused_aid", P(("refuser", "acc")), 60, false, null, "x1", null, EventOrigin.Public, K(He, 1, "src2"));
            var heard = new[] { Heard("x1", x.Type, "x1"), Heard("y1", resp.Type, "x1") };
            var run = Go(new[] { x, resp }, heard, "x1", world: new StubWorld());

            Assert.Equal(ProbeResultKind.TwoVersions, run.Plan.ResultKind);
            Assert.Equal("two-versions", run.Plan.ResultKindLabel);
            Assert.Equal("VividWorld_Probe_Reason_TraitFit_Disbelieve", run.Plan.SentenceKey);
            Assert.Equal("src", run.Plan.Vars["SOURCE"]);
            Assert.Equal("acc", run.Plan.AddressHeroId);
            Assert.Contains("belief false", run.Plan.LogReason);
            Assert.Contains("TraitFit", run.Plan.LogReason);
        }

        [Fact]
        public void Row7_TwoVersions_PoisonedAndDeath_AndNeverJudgedCountsAsBelieving()
        {
            var death = Evt("d1", "hero_died_naturally", P(("hero", "v")), 40, false, null, null, null, EventOrigin.Public, K(He, 1, "s0"));
            var x = Evt("x1", "conduct_poisoned", P(("poisoner", "acc"), ("victim", "v")), 50, true, "orig", "d1", "madeup_slander", EventOrigin.Public, K(He, 2, "src"));
            var run = Go(new[] { x, death }, new[] { Heard("x1", x.Type, "x1") }, "x1", world: new StubWorld());

            Assert.Equal(ProbeResultKind.TwoVersions, run.Plan.ResultKind);
            Assert.Equal("VividWorld_Probe_Reason_None_Believe", run.Plan.SentenceKey);
            Assert.Contains("never judged", run.Plan.LogReason);
        }

        [Fact]
        public void Row8_Tell_HeKnowsOneThing_AndTheFirstOneIsSkippedForAReason()
        {
            var a = Evt("a1", "hero_captured", P(("prisoner", "pr")), 30, false, null, null, null, EventOrigin.Public, K(He, 2, "s"));
            var b = Evt("b1", "hero_captured", P(("prisoner", "pr2")), 20, false, null, null, null, EventOrigin.Public, K(He, 2, "s"));
            var run = Go(new[] { a, b }, new[] { Heard("a1", a.Type, "a1") }, "a1",
                canTell: id => id == "a1" ? CandidateRejection.NotVisible : CandidateRejection.None);

            Assert.Equal(ProbeResultKind.Tell, run.Plan.ResultKind);
            Assert.Equal("b1", run.Plan.EventId);
            Assert.Contains("a1: NotVisible", run.Plan.LogReason);      // 被跳過的那一則與原因進了日誌
            Assert.Equal(1, run.OffersBuilt);
        }

        [Fact]
        public void Row8_AllReluctant_RefusesToTell_AndAllInvisible_IsNotHeard()
        {
            var a = Evt("a1", "hero_captured", P(("prisoner", "pr")), 30, false, null, null, null, EventOrigin.Public, K(He, 2, "s"));
            var b = Evt("b1", "hero_captured", P(("prisoner", "pr2")), 20, false, null, null, null, EventOrigin.Public, K(He, 2, "s"));
            var heard = new[] { Heard("a1", a.Type, "a1") };

            var reluctant = Go(new[] { a, b }, heard, "a1", canTell: _ => CandidateRejection.LeakedSecretHonorable);
            Assert.Equal(ProbeResultKind.RefuseToTell, reluctant.Plan.ResultKind);
            Assert.Contains("LeakedSecretHonorable", reluctant.Plan.LogReason);
            Assert.Contains("a1:", reluctant.Plan.LogReason);
            Assert.Contains("b1:", reluctant.Plan.LogReason);
            Assert.Equal(0, reluctant.OffersBuilt);

            var invisible = Go(new[] { a, b }, heard, "a1", canTell: _ => CandidateRejection.FutureTimeline);
            Assert.Equal(ProbeResultKind.NotHeard, invisible.Plan.ResultKind);
            Assert.Contains("FutureTimeline", invisible.Plan.LogReason);
        }

        [Fact]
        public void Row9_NotHeard_WhenHeKnowsNone_AndLogSaysWhyEveryRowFailed()
        {
            var a = Evt("a1", "hero_captured", P(("prisoner", "pr")), 30, false, null, null, null, EventOrigin.Public, K("someone_else", 1));
            var run = Go(new[] { a }, new[] { Heard("a1", a.Type, "a1") }, "a1");

            Assert.Equal(ProbeResultKind.NotHeard, run.Plan.ResultKind);
            Assert.Equal("not-heard", run.Plan.ResultKindLabel);
            Assert.Contains(run.Plan.SentenceKey, new[] { "VividWorld_Probe_NotHeard_1", "VividWorld_Probe_NotHeard_2" });
            foreach (var row in new[] { "1 accused-deny", "2 accused-praise", "3 accused-refuse", "4 originator", "5 truth-face", "6 truth-tell", "7 two-versions", "8 tell" })
            {
                Assert.Contains(row, run.Plan.LogReason);
            }
            Assert.Empty(run.Asked);
        }

        [Fact]
        public void ForgottenEvents_DoNotCountAsKnown()
        {
            var cfg = new VividWorldConfig();
            cfg.Memory.Enabled = true;
            var a = Evt("a1", "hero_captured", P(("prisoner", "pr")), 30, false, null, null, null, EventOrigin.Public, K(He, 2, "s", forget: 80));
            var run = Go(new[] { a }, new[] { Heard("a1", a.Type, "a1") }, "a1", cfg: cfg);
            Assert.Equal(ProbeResultKind.NotHeard, run.Plan.ResultKind);

            var remembered = Evt("a1", "hero_captured", P(("prisoner", "pr")), 30, false, null, null, null, EventOrigin.Public, K(He, 2, "s", forget: 150));
            Assert.Equal(ProbeResultKind.Tell, Go(new[] { remembered }, new[] { Heard("a1", a.Type, "a1") }, "a1", cfg: cfg).Plan.ResultKind);
        }

        // ───────────── 同時符合兩列：取上面那列 ─────────────

        [Fact]
        public void AccusedAndTwoVersions_TakesTheAccusedRow()
        {
            var x = SlanderAboutHe("x1", "orig", "madeup_slander", K(He, 2, "src", false, "TraitFit"));
            var resp = Evt("y1", "talk_denied_refused_aid", P(("refuser", He)), 60, false, null, "x1", null, EventOrigin.Public, K(He, 1));
            var run = Go(new[] { x, resp }, new[] { Heard("x1", x.Type, "x1"), Heard("y1", resp.Type, "x1") }, "x1");
            Assert.Equal(ProbeResultKind.AccusedDeny, run.Plan.ResultKind);
            Assert.False(run.Plan.ShouldDeliverToAccused);                 // 他已經有那句話的記錄
        }

        [Fact]
        public void OriginatorAndTruthKnower_TakesTheOriginatorRow()
        {
            var own = Evt("x1", "conduct_spoke_against_ruler", P(("speaker", "acc"), ("ruler", "k")), 50, true, He, null, "madeup_slander", EventOrigin.Public, K(He, 0));
            var other = Evt("x2", "conduct_refused_aid", P(("refuser", "acc"), ("asker", He)), 51, true, "orig", null, "madeup_slander", EventOrigin.Public, K(Player, 2, "s"));
            var run = Go(new[] { own, other }, new[] { Heard("x1", own.Type, "x1", updated: 80), Heard("x2", other.Type, "x1", updated: 95) }, "x1", cfg: Slip(0));
            Assert.Equal(ProbeResultKind.OriginatorInsist, run.Plan.ResultKind);
        }

        [Fact]
        public void TruthKnowerAndTwoVersions_TakesTheTruthRow()
        {
            var x = Evt("x1", "conduct_refused_aid", P(("refuser", "acc"), ("asker", He)), 50, true, "orig", null, "madeup_slander", EventOrigin.Public, K(He, 1, "s", true, "None"));
            var resp = Evt("y1", "talk_denied_refused_aid", P(("refuser", "acc")), 60, false, null, "x1", null, EventOrigin.Public, K(He, 1));
            var run = Go(new[] { x, resp }, new[] { Heard("x1", x.Type, "x1"), Heard("y1", resp.Type, "x1") }, "x1");
            Assert.Equal(ProbeResultKind.TruthFace, run.Plan.ResultKind);
        }

        [Fact]
        public void TwoVersionsAndTell_TakesTheTwoVersionsRow()
        {
            var x = Evt("x1", "conduct_refused_aid", P(("refuser", "acc"), ("asker", "ask")), 50, true, "orig", null, "madeup_slander", EventOrigin.Public, K(He, 2, "src", true, "None"));
            var resp = Evt("y1", "talk_denied_refused_aid", P(("refuser", "acc")), 60, false, null, "x1", null, EventOrigin.Public, K(He, 1));
            var run = Go(new[] { x, resp }, new[] { Heard("x1", x.Type, "x1") }, "x1", world: new StubWorld());
            Assert.Equal(ProbeResultKind.TwoVersions, run.Plan.ResultKind);
        }

        [Fact]
        public void SeveralOnTheSameRow_TakeTheOneThePlayerHeardMostRecently_ThenEventIdOrder()
        {
            var x1 = SlanderAboutHe("xa", "o1", "madeup_slander", K(Player, 2, "s"));
            var x2 = SlanderAboutHe("xb", "o2", "madeup_slander", K(Player, 2, "s"));
            var older = Go(new[] { x1, x2 }, new[] { Heard("xa", x1.Type, "xa", updated: 95), Heard("xb", x2.Type, "xa", updated: 90) }, "xa");
            Assert.Equal("xa", older.Plan.EventId);
            Assert.Equal("o1", older.Plan.Vars["ORIGINATOR"]);

            var newer = Go(new[] { x1, x2 }, new[] { Heard("xa", x1.Type, "xa", updated: 90), Heard("xb", x2.Type, "xa", updated: 95) }, "xa");
            Assert.Equal("xb", newer.Plan.EventId);

            var tie = Go(new[] { x1, x2 }, new[] { Heard("xb", x2.Type, "xa", updated: 90), Heard("xa", x1.Type, "xa", updated: 90) }, "xa");
            Assert.Equal("xa", tie.Plan.EventId);
        }

        // ───────────── 說溜嘴 ─────────────

        [Fact]
        public void SlipChance_ThreePersonalities_AndClamp()
        {
            var slip = new SlipConfig();      // 10 / +20 / -10
            Assert.Equal(30.0, ProbeClassifier.SlipChancePercent(slip, -1));
            Assert.Equal(30.0, ProbeClassifier.SlipChancePercent(slip, -2));
            Assert.Equal(10.0, ProbeClassifier.SlipChancePercent(slip, 0));
            Assert.Equal(0.0, ProbeClassifier.SlipChancePercent(slip, +1));

            Assert.Equal(0.0, ProbeClassifier.SlipChancePercent(new SlipConfig { BaseChance = 5, CalculatingBonus = -10 }, +2));
            Assert.Equal(100.0, ProbeClassifier.SlipChancePercent(new SlipConfig { BaseChance = 90, RashBonus = 50 }, -1));
        }

        [Fact]
        public void SlipRoll_SameManSameEvent_AlwaysTheSame_AndStaysBetween0And100()
        {
            double first = ProbeClassifier.SlipRoll(7, "x1", He);
            for (int i = 0; i < 5; i++) Assert.Equal(first, ProbeClassifier.SlipRoll(7, "x1", He));
            for (int i = 0; i < 300; i++)
            {
                double r = ProbeClassifier.SlipRoll(i, "x" + i, "h" + i);
                Assert.InRange(r, 0.0, 99.99);
            }
        }

        [Fact]
        public void Slip_OutcomeDoesNotChange_WhenAskedAgainTomorrow_AndRashMenSlipMoreOften()
        {
            var x = Evt("x1", "conduct_spoke_against_ruler", P(("speaker", "acc"), ("ruler", "k")), 50, true, He, null, "madeup_slander", EventOrigin.Public, K(He, 0));
            var heard = new[] { Heard("x1", x.Type, "x1") };

            // 三種個性：對 300 個不同戰役種子各擲一次，魯莽的說溜嘴的次數最多、謀略的最少
            int Slips(int calc)
            {
                int n = 0;
                for (int seed = 0; seed < 300; seed++)
                {
                    var plan = Go(new[] { x }, heard, "x1", traits: Traits(He, calc), seed: seed, world: new StubWorld()).Plan;
                    if (plan.ResultKind == ProbeResultKind.OriginatorSlip) n++;
                }
                return n;
            }

            int rash = Slips(-1), plain = Slips(0), cool = Slips(+1);
            Assert.True(rash > plain, $"rash {rash} should exceed plain {plain}");
            Assert.True(plain > cool, $"plain {plain} should exceed calculating {cool}");
            Assert.Equal(0, cool);                    // 10 - 10 = 0%

            for (int seed = 0; seed < 50; seed++)
            {
                var today = Go(new[] { x }, heard, "x1", seed: seed, today: 100, world: new StubWorld()).Plan.ResultKind;
                var tomorrow = Go(new[] { x }, heard, "x1", seed: seed, today: 101, world: new StubWorld()).Plan.ResultKind;
                Assert.Equal(today, tomorrow);
            }
        }

        // ───────────── 輪替 ─────────────

        [Fact]
        public void Rotation_SameDaySameBlockSameMan_IsFixed_AndBothLinesAppearAcrossDays()
        {
            int first = ProbeClassifier.RotationIndex(7, "x1", He, 100);
            for (int i = 0; i < 5; i++) Assert.Equal(first, ProbeClassifier.RotationIndex(7, "x1", He, 100.9));

            var seen = new HashSet<int>();
            for (int d = 0; d < 60; d++) seen.Add(ProbeClassifier.RotationIndex(7, "x1", He, d));
            Assert.Equal(new HashSet<int> { 0, 1 }, seen);

            var a = Evt("a1", "hero_captured", P(("prisoner", "pr")), 30, false, null, null, null, EventOrigin.Public, K("other", 1));
            string k1 = Go(new[] { a }, new[] { Heard("a1", a.Type, "a1") }, "a1", today: 100).Plan.SentenceKey!;
            string k2 = Go(new[] { a }, new[] { Heard("a1", a.Type, "a1") }, "a1", today: 100).Plan.SentenceKey!;
            Assert.Equal(k1, k2);
        }

        // ───────────── 稱呼 ─────────────

        [Fact]
        public void NoFeelingWorld_DoesNotCrash_UsesNeutralAddress_AndSaysSoInTheLog()
        {
            var x = Evt("x1", "conduct_refused_aid", P(("refuser", "acc"), ("asker", "ask")), 50, true, "orig", null, "madeup_slander", EventOrigin.Public, K(He, 2, "src", true, "SubjectRelation"));
            var resp = Evt("y1", "talk_denied_refused_aid", P(("refuser", "acc")), 60, false, null, "x1", null, EventOrigin.Public, K(He, 1));
            var run = Go(new[] { x, resp }, new[] { Heard("x1", x.Type, "x1") }, "x1", world: null);

            Assert.Equal(ProbeResultKind.TwoVersions, run.Plan.ResultKind);
            Assert.Equal(FeelingGrid.AddressKey(StandingComparison.Equal, AffectionLevel.Neutral), run.Plan.AddressKey);
            Assert.True(run.Plan.NeutralAddress);
            Assert.Contains("neutral address", run.Plan.LogReason);
        }

        // ───────────── 理由對句子 ─────────────

        public static IEnumerable<object?[]> ReasonCases()
        {
            // 理由, 信, 來源, 期望
            yield return new object?[] { "SubjectRelation", true, "src", "VividWorld_Probe_Reason_SubjectRelation_Believe" };
            yield return new object?[] { "SubjectRelation", false, "src", "VividWorld_Probe_Reason_SubjectRelation_Disbelieve" };
            yield return new object?[] { "TellerRelation", true, "src", "VividWorld_Probe_Reason_TellerRelation_Believe" };
            yield return new object?[] { "TellerRelation", false, "src", "VividWorld_Probe_Reason_TellerRelation_Disbelieve" };
            yield return new object?[] { "TraitFit", true, "src", "VividWorld_Probe_Reason_TraitFit_Believe" };
            yield return new object?[] { "TraitFit", false, "src", "VividWorld_Probe_Reason_TraitFit_Disbelieve" };
            yield return new object?[] { "ListenerNature", true, "src", "VividWorld_Probe_Reason_ListenerNature_Believe" };
            yield return new object?[] { "ListenerNature", false, "src", "VividWorld_Probe_Reason_ListenerNature_Disbelieve" };
            yield return new object?[] { "Distance", true, "src", "VividWorld_Probe_Reason_None_Believe" };
            yield return new object?[] { "Distance", false, "src", "VividWorld_Probe_Reason_Distance_Disbelieve" };
            foreach (var other in new[] { "None", "Witness", "KnowsTruth", "Response", "Legacy", null, "", "subjectrelation", "NoSuchReason" })
            {
                yield return new object?[] { other, true, "src", "VividWorld_Probe_Reason_None_Believe" };
                yield return new object?[] { other, false, "src", "VividWorld_Probe_Reason_None_Disbelieve" };
            }
            // 來源是玩家或沒有來源：「說的人」那一種退回 None
            yield return new object?[] { "TellerRelation", true, Player, "VividWorld_Probe_Reason_None_Believe" };
            yield return new object?[] { "TellerRelation", false, Player, "VividWorld_Probe_Reason_None_Disbelieve" };
            yield return new object?[] { "TellerRelation", true, null, "VividWorld_Probe_Reason_None_Believe" };
            yield return new object?[] { "TellerRelation", false, "", "VividWorld_Probe_Reason_None_Disbelieve" };
        }

        [Theory]
        [MemberData(nameof(ReasonCases))]
        public void ReasonToSentence_EveryCell(string? reason, bool believes, string? source, string expected)
        {
            Assert.Equal(expected, ProbeClassifier.MapTwoVersionsSentenceKey(believes, reason, source, Player));
        }

        // ───────────── §5：讓被說的人知道 ─────────────

        [Theory]
        [InlineData(1, false)]
        [InlineData(2, true)]
        [InlineData(3, true)]
        public void Accused_LearnsItNow_HopSourceFactsAndOriginatorFollowThePlayersVersion(int playerHop, bool knowsOriginator)
        {
            var playerEntry = K(Player, playerHop, "someone");
            playerEntry.KnownFactIds = new List<string> { "f1", "f3" };
            var x = SlanderAboutHe("x1", "orig", "madeup_slander", playerEntry);
            var run = Go(new[] { x }, new[] { Heard("x1", x.Type, "x1", hop: playerHop, factIds: new[] { "f1", "f2", "f3" }) }, "x1");

            var plan = run.Plan;
            Assert.True(plan.ShouldDeliverToAccused);
            Assert.Equal("x1", plan.DeliverEventId);
            Assert.Equal(playerHop + 1, plan.DeliverHop);
            Assert.Equal(new[] { "f1", "f3" }, plan.DeliverKnownFactIds);          // 玩家那筆知情記錄的段落
            Assert.Equal(knowsOriginator, plan.DeliverKnowsOriginator);
            Assert.Equal(knowsOriginator, plan.KnowsOriginator);
            Assert.Equal(knowsOriginator ? "VividWorld_Probe_Accused_DenyNamed" : "VividWorld_Probe_Accused_DenyUnnamed", plan.SentenceKey);

            var entry = ProbeClassifier.BuildAccusedKnowledge(plan, He, Player, Today);
            Assert.Equal(He, entry.HeroId);
            Assert.Equal(playerHop + 1, entry.Hop);
            Assert.Equal(Player, entry.SourceHeroId);
            Assert.Equal(Today, entry.LearnedDay);
            Assert.Equal(new[] { "f1", "f3" }, entry.KnownFactIds);
            Assert.Equal(knowsOriginator, entry.OriginatorKnownOverride);

            // 照玩家的版本：就算他的手數是 4（照手數推會算成不知道），覆寫值讓他仍然知道
            x.KnownBy.Add(entry);
            Assert.Equal(knowsOriginator, MadeUpTalk.KnowsOriginator(x, entry));
        }

        [Fact]
        public void Accused_FallsBackToTheHeardLogFacts_WhenThePlayersEntryHasNoFactList()
        {
            var x = SlanderAboutHe("x1", "orig", "madeup_slander", K(Player, 2, "s"));
            var run = Go(new[] { x }, new[] { Heard("x1", x.Type, "x1", factIds: new[] { "g1", "g2" }) }, "x1");
            Assert.Equal(new[] { "g1", "g2" }, run.Plan.DeliverKnownFactIds);
        }

        [Fact]
        public void Accused_AlreadyKnows_NothingIsChanged_AndTheirOwnRecordIsUsed()
        {
            var his = K(He, 4, "src");
            var x = SlanderAboutHe("x1", "orig", "madeup_slander", K(Player, 2, "s"), his);
            var run = Go(new[] { x }, new[] { Heard("x1", x.Type, "x1") }, "x1");
            Assert.False(run.Plan.ShouldDeliverToAccused);
            Assert.Null(run.Plan.DeliverEventId);
            Assert.False(run.Plan.KnowsOriginator);             // 手數 4：照他自己的記錄是不知道
            Assert.Same(his, x.EntryFor(He));
            Assert.Equal(4, his.Hop);
            Assert.Null(his.OriginatorKnownOverride);

            his.OriginatorKnownOverride = true;
            Assert.True(MadeUpTalk.KnowsOriginator(x, his));    // 覆寫值優先
            Assert.True(Go(new[] { x }, new[] { Heard("x1", x.Type, "x1") }, "x1").Plan.KnowsOriginator);
        }

        [Fact]
        public void KnowsOriginator_WithoutOverride_StillFollowsTheHopRule_AndOverrideIsNotWrittenWhenNull()
        {
            var x = SlanderAboutHe("x1", "orig", "madeup_slander");
            Assert.True(MadeUpTalk.KnowsOriginator(x, K("a", 2)));
            Assert.True(MadeUpTalk.KnowsOriginator(x, K("a", 3)));
            Assert.False(MadeUpTalk.KnowsOriginator(x, K("a", 1)));
            Assert.False(MadeUpTalk.KnowsOriginator(x, K("a", 4)));

            Assert.DoesNotContain("OriginatorKnownOverride", VividJson.Write(K("a", 2)), StringComparison.OrdinalIgnoreCase);
            var withOverride = K("a", 2);
            withOverride.OriginatorKnownOverride = false;
            string json = VividJson.Write(withOverride);
            Assert.Contains("originatorKnownOverride", json, StringComparison.OrdinalIgnoreCase);
            Assert.False(VividJson.Read<KnownByEntry>(json)!.OriginatorKnownOverride);
        }

        [Fact]
        public void Classifier_ChangesNothing()
        {
            var x = SlanderAboutHe("x1", "orig", "madeup_slander", K(Player, 2, "s"));
            string before = VividJson.Write(x);
            var heard = Heard("x1", x.Type, "x1");
            string heardBefore = VividJson.Write(heard);
            Go(new[] { x }, new[] { heard }, "x1");
            Assert.Equal(before, VividJson.Write(x));
            Assert.Equal(heardBefore, VividJson.Write(heard));
        }

        // ───────────── 區塊、整條鏈 ─────────────

        [Fact]
        public void CollectBlockEntries_UsesRootEventId_AndFallsBackToOwnEventId()
        {
            var entries = new[]
            {
                new PlayerHeardEntry { EventId = "root" },
                new PlayerHeardEntry { EventId = "child", RootEventId = "root" },
                new PlayerHeardEntry { EventId = "other", RootEventId = "elsewhere" },
                new PlayerHeardEntry { EventId = "lonely" }
            };
            Assert.Equal(new[] { "root", "child" }, ProbeClassifier.CollectBlockEntries("root", entries).Select(e => e.EventId));
            Assert.Equal(new[] { "lonely" }, ProbeClassifier.CollectBlockEntries("lonely", entries).Select(e => e.EventId));
            Assert.Empty(ProbeClassifier.CollectBlockEntries("nobody", entries));
        }

        [Fact]
        public void CollectChain_FollowsLinksLayerByLayer_AndNeverLoops()
        {
            RumorIndexEntry I(string id, string? linked) => new RumorIndexEntry { EventId = id, LinkedEventId = linked };
            var index = new[] { I("root", null), I("a", "root"), I("b", "a"), I("c", "root"), I("zzz", "elsewhere"), I("loop1", "loop2"), I("loop2", "loop1") };
            var chain = ProbeClassifier.CollectChain("root", index);
            Assert.Equal(new[] { "root", "a", "c", "b" }, chain);
            Assert.Equal(new[] { "loop1", "loop2" }, ProbeClassifier.CollectChain("loop1", index).OrderBy(x => x).ToArray());
            Assert.Equal(new[] { "solo" }, ProbeClassifier.CollectChain("solo", index));
        }
    }
}
