using VividWorld.Core.Rumors;
using Xunit;

namespace VividWorld.Core.Tests
{
    public class EligibilityTests
    {
        [Fact]
        public void Eligibility_ExcludesNullDeadImprisonedAndNonNetworkHeroes()
        {
            // null
            Assert.False(Eligibility.IsEligible(null));

            // !IsAlive
            var deadLord = new TraitProfile { IsAlive = false, IsPrisoner = false, IsLord = true, IsWanderer = false };
            Assert.False(Eligibility.IsEligible(deadLord));

            // IsPrisoner
            var prisonerLord = new TraitProfile { IsAlive = true, IsPrisoner = true, IsLord = true, IsWanderer = false };
            Assert.False(Eligibility.IsEligible(prisonerLord));

            // 既非領主亦非流浪者（如名人、平民）
            var notable = new TraitProfile { IsAlive = true, IsPrisoner = false, IsLord = false, IsWanderer = false };
            Assert.False(Eligibility.IsEligible(notable));

            // 領主
            var lord = new TraitProfile { IsAlive = true, IsPrisoner = false, IsLord = true, IsWanderer = false };
            Assert.True(Eligibility.IsEligible(lord));

            // 流浪者
            var wanderer = new TraitProfile { IsAlive = true, IsPrisoner = false, IsLord = false, IsWanderer = true };
            Assert.True(Eligibility.IsEligible(wanderer));
        }
    }
}
