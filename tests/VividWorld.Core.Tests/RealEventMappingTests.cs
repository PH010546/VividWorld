using System;
using VividWorld.Core.Events;
using Xunit;

namespace VividWorld.Core.Tests
{
    public class RealEventMappingTests
    {
        [Fact]
        public void RealEventMapping_Murdered_MapsToHeroMurdered()
        {
            Assert.Equal("hero_murdered", RealEventMapping.TemplateForKill(KillCharacterActionDetail.Murdered));
            Assert.Equal("hero_murdered", RealEventMapping.TemplateForKill(1));
        }

        [Fact]
        public void RealEventMapping_Executed_MapsToHeroExecuted()
        {
            Assert.Equal("hero_executed", RealEventMapping.TemplateForKill(KillCharacterActionDetail.Executed));
            Assert.Equal("hero_executed", RealEventMapping.TemplateForKill(KillCharacterActionDetail.ExecutionAfterMapEvent));
            Assert.Equal("hero_executed", RealEventMapping.TemplateForKill(6));
            Assert.Equal("hero_executed", RealEventMapping.TemplateForKill(7));
        }

        [Fact]
        public void RealEventMapping_DiedInBattle_MapsToHeroDiedInBattle()
        {
            Assert.Equal("hero_died_in_battle", RealEventMapping.TemplateForKill(KillCharacterActionDetail.DiedInBattle));
            Assert.Equal("hero_died_in_battle", RealEventMapping.TemplateForKill(4));
        }

        [Fact]
        public void RealEventMapping_DiedInLabor_MapsToHeroDiedNaturally()
        {
            Assert.Equal("hero_died_naturally", RealEventMapping.TemplateForKill(KillCharacterActionDetail.DiedInLabor));
            Assert.Equal("hero_died_naturally", RealEventMapping.TemplateForKill(2));
        }

        [Fact]
        public void RealEventMapping_DiedOfOldAge_MapsToHeroDiedNaturally()
        {
            Assert.Equal("hero_died_naturally", RealEventMapping.TemplateForKill(KillCharacterActionDetail.DiedOfOldAge));
            Assert.Equal("hero_died_naturally", RealEventMapping.TemplateForKill(3));
        }

        [Fact]
        public void RealEventMapping_WoundedInBattle_MapsToNull()
        {
            Assert.Null(RealEventMapping.TemplateForKill(KillCharacterActionDetail.WoundedInBattle));
            Assert.Null(RealEventMapping.TemplateForKill(5));
        }

        [Fact]
        public void RealEventMapping_Lost_MapsToNull()
        {
            Assert.Null(RealEventMapping.TemplateForKill(KillCharacterActionDetail.Lost));
            Assert.Null(RealEventMapping.TemplateForKill(8));
        }

        [Fact]
        public void RealEventMapping_None_MapsToNull()
        {
            Assert.Null(RealEventMapping.TemplateForKill(KillCharacterActionDetail.None));
            Assert.Null(RealEventMapping.TemplateForKill(0));
        }

        [Fact]
        // 原生列舉沒有 Invalid 這個成員（帳本 D-52），所以只能用界外的整數值來測。
        public void RealEventMapping_OutOfRangeValue_MapsToNull()
        {
            Assert.Null(RealEventMapping.TemplateForKill(-1));
        }

        [Theory]
        [InlineData(-1)]
        [InlineData(9)]
        [InlineData(99)]
        [InlineData(-999)]
        public void RealEventMapping_OutOfBounds_MapsToNull(int detail)
        {
            Assert.Null(RealEventMapping.TemplateForKill(detail));
            Assert.Null(RealEventMapping.TemplateForKill((KillCharacterActionDetail)detail));
        }
    }
}
