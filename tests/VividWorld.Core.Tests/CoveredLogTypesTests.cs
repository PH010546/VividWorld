#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using VividWorld.Core.Config;
using VividWorld.Core.Events;
using Xunit;

namespace VividWorld.Core.Tests
{
    public class CoveredLogTypesTests
    {
        [Fact]
        public void CoveredLogTypes_MatchesAllEventSourcesProperties()
        {
            // 釘住種類清單與真實事件來源的對照：新增來源沒列進對照就失敗
            var sourceProperties = typeof(EventSourcesConfig)
                .GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(p => p.Name != "Extra")
                .Select(p => p.Name)
                .OrderBy(n => n, StringComparer.Ordinal)
                .ToList();

            var expectedProperties = new List<string>
            {
                nameof(EventSourcesConfig.ChildBorn),
                nameof(EventSourcesConfig.HeroKilled),
                nameof(EventSourcesConfig.HeroPrisonerReleased),
                nameof(EventSourcesConfig.HeroPrisonerTaken),
                nameof(EventSourcesConfig.HeroesMarried)
            }.OrderBy(n => n, StringComparer.Ordinal).ToList();

            Assert.Equal(expectedProperties, sourceProperties);

            // 當所有來源啟用時，清單必須剛好有 5 種原生 LogEntry
            var covered = CoveredLogTypes.GetCoveredLogTypes(new EventSourcesConfig());
            Assert.Equal(5, covered.Count);
            Assert.Contains(CoveredLogTypes.CharacterKilled, covered);
            Assert.Contains(CoveredLogTypes.TakePrisoner, covered);
            Assert.Contains(CoveredLogTypes.EndCaptivity, covered);
            Assert.Contains(CoveredLogTypes.CharacterMarried, covered);
            Assert.Contains(CoveredLogTypes.Childbirth, covered);
        }

        [Fact]
        public void CoveredLogTypes_WhenAllEnabled_ReturnsAllFiveNativeLogTypes()
        {
            var config = new EventSourcesConfig
            {
                HeroKilled = true,
                HeroPrisonerTaken = true,
                HeroPrisonerReleased = true,
                HeroesMarried = true,
                ChildBorn = true
            };

            var list = CoveredLogTypes.GetCoveredLogTypes(config);

            Assert.Equal(5, list.Count);
            Assert.Equal("CharacterKilledLogEntry", list[0]);
            Assert.Equal("TakePrisonerLogEntry", list[1]);
            Assert.Equal("EndCaptivityLogEntry", list[2]);
            Assert.Equal("CharacterMarriedLogEntry", list[3]);
            Assert.Equal("ChildbirthLogEntry", list[4]);
        }

        [Fact]
        public void CoveredLogTypes_WhenNullSources_DefaultsToAllEnabled()
        {
            var list = CoveredLogTypes.GetCoveredLogTypes(null);

            Assert.Equal(5, list.Count);
            Assert.Contains(CoveredLogTypes.CharacterKilled, list);
            Assert.Contains(CoveredLogTypes.TakePrisoner, list);
            Assert.Contains(CoveredLogTypes.EndCaptivity, list);
            Assert.Contains(CoveredLogTypes.CharacterMarried, list);
            Assert.Contains(CoveredLogTypes.Childbirth, list);
        }

        [Theory]
        [InlineData(nameof(EventSourcesConfig.HeroKilled), CoveredLogTypes.CharacterKilled)]
        [InlineData(nameof(EventSourcesConfig.HeroPrisonerTaken), CoveredLogTypes.TakePrisoner)]
        [InlineData(nameof(EventSourcesConfig.HeroPrisonerReleased), CoveredLogTypes.EndCaptivity)]
        [InlineData(nameof(EventSourcesConfig.HeroesMarried), CoveredLogTypes.CharacterMarried)]
        [InlineData(nameof(EventSourcesConfig.ChildBorn), CoveredLogTypes.Childbirth)]
        public void CoveredLogTypes_WhenSourceDisabled_ExcludesCorrespondingLogType(string propertyName, string excludedLogType)
        {
            var config = new EventSourcesConfig();
            var prop = typeof(EventSourcesConfig).GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance);
            Assert.NotNull(prop);
            prop!.SetValue(config, false);

            var list = CoveredLogTypes.GetCoveredLogTypes(config);

            Assert.Equal(4, list.Count);
            Assert.DoesNotContain(excludedLogType, list);
        }

        [Fact]
        public void CoveredLogTypes_WhenAllDisabled_ReturnsEmpty()
        {
            var config = new EventSourcesConfig
            {
                HeroKilled = false,
                HeroPrisonerTaken = false,
                HeroPrisonerReleased = false,
                HeroesMarried = false,
                ChildBorn = false
            };

            var list = CoveredLogTypes.GetCoveredLogTypes(config);

            Assert.Empty(list);
        }

        [Fact]
        public void CoveredLogTypes_WhenSessionInactive_ReturnsEmpty()
        {
            var config = new VividWorldConfig();
            config.Events.Sources = new EventSourcesConfig(); // all true by default

            var list = CoveredLogTypes.GetCoveredLogTypes(config, isSessionActive: false);

            Assert.Empty(list);
        }

        [Fact]
        public void CoveredLogTypes_WhenConfigNull_ReturnsEmpty()
        {
            var list = CoveredLogTypes.GetCoveredLogTypes(null, isSessionActive: true);

            Assert.Empty(list);
        }

        [Fact]
        public void CoveredLogTypes_WhenMasterSwitchDisabled_ReturnsEmpty()
        {
            var config = new VividWorldConfig { Enabled = false };

            var list = CoveredLogTypes.GetCoveredLogTypes(config, isSessionActive: true);

            Assert.Empty(list);
        }

        [Fact]
        public void CoveredLogTypes_WhenSessionActiveAndMasterSwitchEnabled_ReturnsCoveredTypes()
        {
            var config = new VividWorldConfig { Enabled = true };

            var list = CoveredLogTypes.GetCoveredLogTypes(config, isSessionActive: true);

            Assert.Equal(5, list.Count);
            Assert.Contains(CoveredLogTypes.CharacterKilled, list);
            Assert.Contains(CoveredLogTypes.TakePrisoner, list);
            Assert.Contains(CoveredLogTypes.EndCaptivity, list);
            Assert.Contains(CoveredLogTypes.CharacterMarried, list);
            Assert.Contains(CoveredLogTypes.Childbirth, list);
        }

        [Fact]
        public void CoveredLogTypes_WhenSessionActive_RespectsIndividualSources()
        {
            var config = new VividWorldConfig { Enabled = true };
            config.Events.Sources.HeroKilled = false;

            var list = CoveredLogTypes.GetCoveredLogTypes(config, isSessionActive: true);

            Assert.Equal(4, list.Count);
            Assert.DoesNotContain(CoveredLogTypes.CharacterKilled, list);
        }
    }
}
