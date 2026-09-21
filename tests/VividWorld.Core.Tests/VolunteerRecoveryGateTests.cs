using System.Collections.Generic;
using VividWorld.Core.Config;
using VividWorld.Core.Dialogue;
using VividWorld.Core.Events;
using VividWorld.Core.Presentation;
using VividWorld.Core.Rumors;
using VividWorld.Core.Tests.Fakes;
using VividWorld.Core.Util;
using Xunit;

namespace VividWorld.Core.Tests
{
    public sealed class VolunteerRecoveryGateTests
    {
        /// <summary>
        /// 1. 有提案 ＋ 被繞開 ＋ 本場未送出 ⇒ 可用（規格 §9.2.3，M6c 卡 §4 第 1 項）
        /// </summary>
        [Fact]
        public void RecoveryGate_WithOffer_WhenBypassed_AndNotDelivered_IsAvailable()
        {
            bool available = VolunteerRecoveryGate.IsAvailable(
                hasOffer: true,
                reachedGreetingToken: false,
                wasBypassed: true,
                alreadyDeliveredThisConversation: false);

            Assert.True(available);
        }

        /// <summary>
        /// 2. 有提案 ＋ 走到了 lord_start ⇒ 不可用（規則正確運作，不得繞過）（M6c 卡 §4 第 2 項）
        /// </summary>
        [Fact]
        public void RecoveryGate_WithOffer_WhenReachedLordStart_IsNotAvailable()
        {
            // 走到了 lord_start（不論是否被判定繞開，只要走到了就不可用）
            bool available1 = VolunteerRecoveryGate.IsAvailable(
                hasOffer: true,
                reachedGreetingToken: true,
                wasBypassed: false,
                alreadyDeliveredThisConversation: false);
            Assert.False(available1);

            bool available2 = VolunteerRecoveryGate.IsAvailable(
                hasOffer: true,
                reachedGreetingToken: true,
                wasBypassed: true,
                alreadyDeliveredThisConversation: false);
            Assert.False(available2);
        }

        /// <summary>
        /// 3. 有提案 ＋ 被繞開 ＋ 本場已送出 ⇒ 不可用（M6c 卡 §4 第 3 項）
        /// </summary>
        [Fact]
        public void RecoveryGate_WithOffer_WhenBypassed_ButAlreadyDelivered_IsNotAvailable()
        {
            bool available = VolunteerRecoveryGate.IsAvailable(
                hasOffer: true,
                reachedGreetingToken: false,
                wasBypassed: true,
                alreadyDeliveredThisConversation: true);

            Assert.False(available);
        }

        /// <summary>
        /// 4. 沒有提案 ＋ 被繞開 ⇒ 不可用（M6c 卡 §4 第 4 項）
        /// </summary>
        [Fact]
        public void RecoveryGate_WithoutOffer_WhenBypassed_IsNotAvailable()
        {
            bool available = VolunteerRecoveryGate.IsAvailable(
                hasOffer: false,
                reachedGreetingToken: false,
                wasBypassed: true,
                alreadyDeliveredThisConversation: false);

            Assert.False(available);
        }

        /// <summary>
        /// 5. 從補救路徑送出後，每日計數與對人冷卻都被消耗（與主動講走同一條結算）（M6c 卡 §4 第 5 項）
        /// </summary>
        [Fact]
        public void RecoveryDelivery_ConsumesDailyCounterAndCooldown_SameAsVolunteer()
        {
            var counter = new DailyCounter();
            var lastVolunteeredDays = new Dictionary<string, double>();
            const string tellerId = "lord_5_16";
            const double day = 26036.0;

            // 尚未送出前：計數為 0，無最後講述日
            Assert.Equal(0, counter.Count);
            Assert.False(lastVolunteeredDays.ContainsKey(tellerId));

            // 執行補救結算
            VolunteerRecoveryGate.ConsumeQuotaAndCooldown(counter, lastVolunteeredDays, tellerId, day);

            // 斷言 1：每日計數已被消耗
            Assert.Equal(1, counter.Count);

            // 斷言 2：對人冷卻紀錄已被更新
            Assert.True(lastVolunteeredDays.TryGetValue(tellerId, out double recordedDay));
            Assert.Equal(day, recordedDay);

            // 斷言 3：相同狀態送入 DecideOnVolunteer，當天因 DailyCap 拒絕
            var cfg = new VividWorldConfig();
            var rng = new SplitMix64Rng();
            var channel = new FakePropagationChannel();
            var traits = new FakeHeroTraitLookup();
            var retention = FactRetentionPolicies.Create(cfg, rng, 42L);
            var embellishment = NullEmbellishmentPolicy.Instance;
            var engine = new RumorEngine(cfg, retention, embellishment, channel, traits, rng, 42L, "hero_main");
            var selector = new RumorOfferSelector(cfg, engine, "hero_main");
            var profile = new HeroSocialProfile
            {
                HeroId = tellerId,
                RelationWithPlayer = 40,
                LastVolunteeredDay = recordedDay,
                Traits = new TraitProfile { HeroId = tellerId }
            };

            var evt = new WorldEvent
            {
                EventId = "evt_duel_1",
                Type = "duel",
                Day = 26030.0,
                DramaWeight = 4,
                KnownBy = new List<KnownByEntry> { new KnownByEntry { HeroId = tellerId, Hop = 1 } },
                Facts = new List<Fact> { new Fact { Id = "f1", Category = FactCategory.What, Text = "fought", TextId = "t1" } }
            };
            var candidates = new List<RumorCandidate>
            {
                new RumorCandidate { Event = evt, TellerHop = 1, PlayerExistingHop = null }
            };

            // 當天由另一位好感足夠且無冷卻的英雄呼叫：因 DailyCap 拒絕（證明每日配額確實被消耗）
            var otherProfile = new HeroSocialProfile
            {
                HeroId = "hero_other",
                RelationWithPlayer = 40,
                LastVolunteeredDay = -1.0,
                Traits = new TraitProfile { HeroId = "hero_other" }
            };
            var decisionOtherOnSameDay = selector.DecideOnVolunteer(otherProfile, candidates, day, counter.Count);
            Assert.Equal(VolunteerRefusal.DailyCap, decisionOtherOnSameDay.Refusal);

            // 隔天呼叫（天數遞進後每日計數器歸零，但 teller 仍在 3 天冷卻期內）：因 Cooldown 拒絕（證明 teller 冷卻確實被記錄）
            var nextDayCounter = new DailyCounter();
            nextDayCounter.Advance(day + 1.0);
            Assert.Equal(0, nextDayCounter.Count);

            var decisionTellerNextDay = selector.DecideOnVolunteer(profile, candidates, day + 1.0, nextDayCounter.Count);
            Assert.Equal(VolunteerRefusal.Cooldown, decisionTellerNextDay.Refusal);
        }
    }
}
