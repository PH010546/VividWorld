#nullable enable
using System.Collections.Generic;
using VividWorld.Core.Presentation;
using Xunit;

namespace VividWorld.Core.Tests
{
    public class RumorPrefixSelectorTests
    {
        [Fact]
        public void Hop0_WithoutSource_ReturnsEyewitnessPrefix()
        {
            var prefix = RumorPrefixSelector.SelectPrefix(hop: 0, sourceHeroId: null, isRetell: false, isCorrection: false);

            Assert.Equal(RumorPrefixKind.Eyewitness, prefix.Kind);
            Assert.Equal(RumorPrefixSelector.EyewitnessTextId, prefix.TextId);
            Assert.Equal("VividWorld_Prefix_Eyewitness", prefix.TextId);
            Assert.Equal("I saw it myself:", prefix.Fallback);
            Assert.Empty(prefix.Vars);
        }

        [Fact]
        public void Hop0_WithSource_StillReturnsEyewitnessPrefix()
        {
            var prefix = RumorPrefixSelector.SelectPrefix(hop: 0, sourceHeroId: "lord_1", isRetell: false, isCorrection: false);

            Assert.Equal(RumorPrefixKind.Eyewitness, prefix.Kind);
            Assert.Equal(RumorPrefixSelector.EyewitnessTextId, prefix.TextId);
            Assert.Equal("I saw it myself:", prefix.Fallback);
            Assert.Empty(prefix.Vars);
        }

        [Theory]
        [InlineData(1)]
        [InlineData(2)]
        public void Hop1And2_WithSource_ReturnsHeardFromSourcePrefix(int hop)
        {
            var prefix = RumorPrefixSelector.SelectPrefix(hop: hop, sourceHeroId: "lord_source", isRetell: false, isCorrection: false);

            Assert.Equal(RumorPrefixKind.HeardFromSource, prefix.Kind);
            Assert.Equal(RumorPrefixSelector.HeardFromSourceTextId, prefix.TextId);
            Assert.Equal("VividWorld_Prefix_HeardFromSource", prefix.TextId);
            Assert.Equal("{SOURCE} told me that", prefix.Fallback);
            Assert.True(prefix.Vars.ContainsKey("SOURCE"));
            Assert.Equal("hero:lord_source", prefix.Vars["SOURCE"]);
        }

        [Theory]
        [InlineData(1)]
        [InlineData(2)]
        public void Hop1And2_WithoutSource_ReturnsHeardGeneralPrefix(int hop)
        {
            var prefix = RumorPrefixSelector.SelectPrefix(hop: hop, sourceHeroId: null, isRetell: false, isCorrection: false);

            Assert.Equal(RumorPrefixKind.HeardGeneral, prefix.Kind);
            Assert.Equal(RumorPrefixSelector.HeardGeneralTextId, prefix.TextId);
            Assert.Equal("VividWorld_Prefix_HeardGeneral", prefix.TextId);
            Assert.Equal("I heard it said that", prefix.Fallback);
            Assert.Empty(prefix.Vars);
        }

        [Fact]
        public void Hop3_WithSource_DropsSourceAndReturnsHeardGeneralPrefix()
        {
            var prefix = RumorPrefixSelector.SelectPrefix(hop: 3, sourceHeroId: "lord_source", isRetell: false, isCorrection: false);

            Assert.Equal(RumorPrefixKind.HeardGeneral, prefix.Kind);
            Assert.Equal(RumorPrefixSelector.HeardGeneralTextId, prefix.TextId);
            Assert.Equal("I heard it said that", prefix.Fallback);
            Assert.Empty(prefix.Vars);
        }

        [Fact]
        public void Hop3_WithoutSource_ReturnsHeardGeneralPrefix()
        {
            var prefix = RumorPrefixSelector.SelectPrefix(hop: 3, sourceHeroId: null, isRetell: false, isCorrection: false);

            Assert.Equal(RumorPrefixKind.HeardGeneral, prefix.Kind);
            Assert.Equal(RumorPrefixSelector.HeardGeneralTextId, prefix.TextId);
            Assert.Equal("I heard it said that", prefix.Fallback);
            Assert.Empty(prefix.Vars);
        }

        [Theory]
        [InlineData(4)]
        [InlineData(5)]
        [InlineData(10)]
        public void HopGreaterThan3_ReturnsHeardGeneralPrefix(int hop)
        {
            var prefixWithSource = RumorPrefixSelector.SelectPrefix(hop: hop, sourceHeroId: "lord_source", isRetell: false, isCorrection: false);
            Assert.Equal(RumorPrefixKind.HeardGeneral, prefixWithSource.Kind);

            var prefixWithoutSource = RumorPrefixSelector.SelectPrefix(hop: hop, sourceHeroId: null, isRetell: false, isCorrection: false);
            Assert.Equal(RumorPrefixKind.HeardGeneral, prefixWithoutSource.Kind);
        }

        [Fact]
        public void Retell_Hop0_ReturnsRetellPrefix()
        {
            var prefix = RumorPrefixSelector.SelectPrefix(hop: 0, sourceHeroId: null, isRetell: true, isCorrection: false);

            Assert.Equal(RumorPrefixKind.Retell, prefix.Kind);
            Assert.Equal(RumorTextComposer.RetellPrefixTextId, prefix.TextId);
            Assert.Equal("VividWorld_RetellPrefix", prefix.TextId);
            Assert.Equal(RumorTextComposer.RetellPrefixFallback, prefix.Fallback);
            Assert.Equal("I was there, in fact—", prefix.Fallback);
            Assert.Empty(prefix.Vars);
        }

        [Theory]
        [InlineData(1)]
        [InlineData(2)]
        [InlineData(3)]
        public void Retell_HopGreaterThan0_FallsBackToSourceTable(int hop)
        {
            var prefix = RumorPrefixSelector.SelectPrefix(hop: hop, sourceHeroId: "lord_source", isRetell: true, isCorrection: false);

            var expected = hop <= 2 ? RumorPrefixKind.HeardFromSource : RumorPrefixKind.HeardGeneral;
            Assert.Equal(expected, prefix.Kind);
            Assert.NotEqual(RumorTextComposer.RetellPrefixTextId, prefix.TextId);
        }

        [Fact]
        public void Retell_OverridesCorrection_DoesNotStack()
        {
            var prefixHop0 = RumorPrefixSelector.SelectPrefix(hop: 0, sourceHeroId: "lord_source", isRetell: true, isCorrection: true);
            Assert.Equal(RumorPrefixKind.Retell, prefixHop0.Kind);
            Assert.Equal("VividWorld_RetellPrefix", prefixHop0.TextId);

            var prefixHop1 = RumorPrefixSelector.SelectPrefix(hop: 1, sourceHeroId: "lord_source", isRetell: true, isCorrection: true);
            Assert.Equal(RumorPrefixKind.CorrectionSource, prefixHop1.Kind);
            Assert.NotEqual("VividWorld_RetellPrefix", prefixHop1.TextId);
        }

        [Fact]
        public void Correction_WithSource_ReturnsCorrectionSourcePrefix()
        {
            var prefix = RumorPrefixSelector.SelectPrefix(hop: 1, sourceHeroId: "lord_captor", isRetell: false, isCorrection: true);

            Assert.Equal(RumorPrefixKind.CorrectionSource, prefix.Kind);
            Assert.Equal(RumorPrefixSelector.CorrectionSourceTextId, prefix.TextId);
            Assert.Equal("VividWorld_Prefix_CorrectionSource", prefix.TextId);
            Assert.Equal("Later, {SOURCE} told me that", prefix.Fallback);
            Assert.True(prefix.Vars.ContainsKey("SOURCE"));
            Assert.Equal("hero:lord_captor", prefix.Vars["SOURCE"]);
        }

        [Fact]
        public void Correction_WithoutSource_ReturnsCorrectionGeneralPrefix()
        {
            var prefix = RumorPrefixSelector.SelectPrefix(hop: 1, sourceHeroId: null, isRetell: false, isCorrection: true);

            Assert.Equal(RumorPrefixKind.CorrectionGeneral, prefix.Kind);
            Assert.Equal(RumorPrefixSelector.CorrectionGeneralTextId, prefix.TextId);
            Assert.Equal("VividWorld_Prefix_CorrectionGeneral", prefix.TextId);
            Assert.Equal("Later, I heard it said that", prefix.Fallback);
            Assert.Empty(prefix.Vars);
        }

        // 第 0 手是自己在場的人：就算對聽者是更正，也不能說成「後來又聽人說」
        // （實機：被脫逃的看守者講自己看丟了人，開頭卻是「後來又聽人說」）。
        [Fact]
        public void Correction_Hop0_WithSource_UsesFirstHandPrefix()
        {
            var prefix = RumorPrefixSelector.SelectPrefix(hop: 0, sourceHeroId: "lord_captor", isRetell: false, isCorrection: true);

            Assert.Equal(RumorPrefixKind.Eyewitness, prefix.Kind);
            Assert.Equal("VividWorld_Prefix_Eyewitness", prefix.TextId);
        }

        [Fact]
        public void Correction_Hop0_WithoutSource_UsesFirstHandPrefix()
        {
            var prefix = RumorPrefixSelector.SelectPrefix(hop: 0, sourceHeroId: null, isRetell: false, isCorrection: true);

            Assert.Equal(RumorPrefixKind.Eyewitness, prefix.Kind);
            Assert.Equal("VividWorld_Prefix_Eyewitness", prefix.TextId);
        }

        [Fact]
        public void SelectPrefix_BoolOverload_WorksEquivalently()
        {
            var p1 = RumorPrefixSelector.SelectPrefix(hop: 1, hasSource: true, isRetell: false, isCorrection: false, sourceHeroId: "lord_x");
            Assert.Equal(RumorPrefixKind.HeardFromSource, p1.Kind);
            Assert.Equal("hero:lord_x", p1.Vars["SOURCE"]);

            var p2 = RumorPrefixSelector.SelectPrefix(hop: 1, hasSource: false, isRetell: false, isCorrection: false);
            Assert.Equal(RumorPrefixKind.HeardGeneral, p2.Kind);

            var p3 = RumorPrefixSelector.SelectPrefix(hop: 1, hasSource: true, isRetell: false, isCorrection: true, sourceHeroId: "lord_y");
            Assert.Equal(RumorPrefixKind.CorrectionSource, p3.Kind);
            Assert.Equal("hero:lord_y", p3.Vars["SOURCE"]);

            var p4 = RumorPrefixSelector.SelectPrefix(hop: 1, hasSource: false, isRetell: false, isCorrection: true);
            Assert.Equal(RumorPrefixKind.CorrectionGeneral, p4.Kind);
        }

        [Fact]
        public void Hop0_Participant_ReturnsSelfPrefix()
        {
            var prefix = RumorPrefixSelector.SelectPrefix(hop: 0, sourceHeroId: null, isRetell: false, isCorrection: false, isParticipant: true);
            Assert.Equal(RumorPrefixKind.Self, prefix.Kind);
            Assert.Null(prefix.TextId);
            Assert.Null(prefix.Fallback);
            Assert.Empty(prefix.Vars);
        }

        [Fact]
        public void Hop0_Participant_Correction_StillReturnsSelfPrefix()
        {
            var prefix = RumorPrefixSelector.SelectPrefix(hop: 0, sourceHeroId: null, isRetell: false, isCorrection: true, isParticipant: true);
            Assert.Equal(RumorPrefixKind.Self, prefix.Kind);
            Assert.Null(prefix.TextId);
            Assert.Null(prefix.Fallback);
        }

        [Fact]
        public void Hop0_Participant_Retell_ReturnsRetellSelfPrefix()
        {
            var prefix = RumorPrefixSelector.SelectPrefix(hop: 0, sourceHeroId: null, isRetell: true, isCorrection: false, isParticipant: true);
            Assert.Equal(RumorPrefixKind.RetellSelf, prefix.Kind);
            Assert.Equal(RumorPrefixSelector.RetellSelfTextId, prefix.TextId);
            Assert.Equal("VividWorld_RetellPrefix_Self", prefix.TextId);
            Assert.Equal("You may have heard about this already—", prefix.Fallback);
        }

        [Fact]
        public void Hop0_Onlooker_Retell_ReturnsRetellPrefix()
        {
            var prefix = RumorPrefixSelector.SelectPrefix(hop: 0, sourceHeroId: null, isRetell: true, isCorrection: false, isParticipant: false);
            Assert.Equal(RumorPrefixKind.Retell, prefix.Kind);
            Assert.Equal(RumorTextComposer.RetellPrefixTextId, prefix.TextId);
            Assert.Equal("VividWorld_RetellPrefix", prefix.TextId);
            Assert.Equal("I was there, in fact—", prefix.Fallback);
        }

        [Fact]
        public void Hop1_Participant_Retell_Unaffected()
        {
            var prefix = RumorPrefixSelector.SelectPrefix(hop: 1, sourceHeroId: "lord_source", isRetell: true, isCorrection: false, isParticipant: true);
            Assert.Equal(RumorPrefixKind.HeardFromSource, prefix.Kind);
            Assert.Equal("{SOURCE} told me that", prefix.Fallback);
        }

        [Fact]
        public void Hop1_Participant_ReturnsNormalHeardPrefix()
        {
            var prefix = RumorPrefixSelector.SelectPrefix(hop: 1, sourceHeroId: "lord_source", isRetell: false, isCorrection: false, isParticipant: true);
            Assert.Equal(RumorPrefixKind.HeardFromSource, prefix.Kind);
            Assert.Equal("{SOURCE} told me that", prefix.Fallback);
        }
    }
}
