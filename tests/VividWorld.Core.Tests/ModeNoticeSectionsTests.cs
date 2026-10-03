using VividWorld.Core.Dialogue;
using Xunit;

namespace VividWorld.Core.Tests
{
    public class ModeNoticeSectionsTests
    {
        [Theory]
        [InlineData("auto", true)]
        [InlineData("casual", true)]
        [InlineData("realistic", true)]
        [InlineData("auto", false)]
        [InlineData("realistic", false)]
        public void NewCampaign_AlwaysPopsUp_PersonalSectionFollowsSwitch(string conf, bool switchOn)
        {
            var plan = ModeNoticeSections.Decide(conf, RumorMode.Casual, ModeNoticeAction.None, true, true, switchOn);
            Assert.Equal(ModeNoticeAction.Popup, plan.Action);
            Assert.True(plan.ShowModeSection);
            Assert.Equal(switchOn, plan.ShowPersonalRelationsSection);
            Assert.False(plan.ShowSeededNote);
            Assert.Equal(!switchOn, plan.PersonalSectionOmittedBecauseSwitchOff);
            Assert.Contains("new campaign", plan.Reasons);
        }

        [Theory]
        [InlineData("auto")]
        [InlineData("casual")]
        [InlineData("realistic")]
        public void OldCampaignJustSeeded_AllThreeParts_EvenWhenModeIsExplicit(string conf)
        {
            var plan = ModeNoticeSections.Decide(conf, RumorMode.Casual, ModeNoticeAction.None, false, true, true);
            Assert.Equal(ModeNoticeAction.Popup, plan.Action);
            Assert.True(plan.ShowModeSection);
            Assert.True(plan.ShowPersonalRelationsSection);
            Assert.True(plan.ShowSeededNote);
            Assert.Contains("relations just seeded", plan.Reasons);
        }

        [Fact]
        public void OldCampaignJustSeeded_SwitchOff_FallsBackToBaseRule()
        {
            var plan = ModeNoticeSections.Decide("auto", RumorMode.Casual, ModeNoticeAction.Message, false, true, false);
            Assert.Equal(ModeNoticeAction.Message, plan.Action);
            Assert.False(plan.ShowPersonalRelationsSection);
            Assert.False(plan.ShowSeededNote);
        }

        [Fact]
        public void ModeChanged_OldCampaign_ModeSectionOnly()
        {
            var plan = ModeNoticeSections.Decide("auto", RumorMode.Realistic, ModeNoticeAction.Popup, false, false, true);
            Assert.Equal(ModeNoticeAction.Popup, plan.Action);
            Assert.True(plan.ShowModeSection);
            Assert.False(plan.ShowPersonalRelationsSection);
            Assert.False(plan.ShowSeededNote);
            Assert.Contains("mode changed", plan.Reasons);
        }

        [Fact]
        public void NothingNew_KeepsBaseAction()
        {
            var msg = ModeNoticeSections.Decide("auto", RumorMode.Casual, ModeNoticeAction.Message, false, false, true);
            Assert.Equal(ModeNoticeAction.Message, msg.Action);
            Assert.False(msg.ShowModeSection);

            var none = ModeNoticeSections.Decide("casual", RumorMode.Casual, ModeNoticeAction.None, false, false, true);
            Assert.Equal(ModeNoticeAction.None, none.Action);
        }

        [Fact]
        public void ModeChangedAndNewCampaign_RecordsBothReasons()
        {
            var plan = ModeNoticeSections.Decide("auto", RumorMode.Realistic, ModeNoticeAction.Popup, true, true, true);
            Assert.Contains("new campaign", plan.Reasons);
            Assert.Contains("mode changed", plan.Reasons);
            Assert.False(plan.ShowSeededNote);
        }

        [Theory]
        [InlineData("auto", RumorMode.Realistic, ModeSectionKind.AutoRealistic)]
        [InlineData("auto", RumorMode.Casual, ModeSectionKind.Casual)]
        [InlineData("casual", RumorMode.Casual, ModeSectionKind.Casual)]
        [InlineData("realistic", RumorMode.Realistic, ModeSectionKind.ForcedRealistic)]
        [InlineData(null, RumorMode.Realistic, ModeSectionKind.AutoRealistic)]
        public void ModeKind_PicksTextVariant(string conf, RumorMode actual, ModeSectionKind expected)
        {
            var plan = ModeNoticeSections.Decide(conf, actual, ModeNoticeAction.Popup, false, false, true);
            Assert.Equal(expected, plan.ModeKind);
        }

        [Fact]
        public void DescribeSections_ListsParts()
        {
            var plan = ModeNoticeSections.Decide("auto", RumorMode.Casual, ModeNoticeAction.None, false, true, true);
            Assert.Equal("mode, personal relations, seeded note", plan.DescribeSections());
        }
    }
}
