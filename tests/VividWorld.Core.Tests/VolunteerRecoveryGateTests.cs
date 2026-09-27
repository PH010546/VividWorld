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
    }
}
