using VividWorld.Core.Presentation;
using Xunit;

namespace VividWorld.Core.Tests
{
    public sealed class EncyclopediaReturnTrackerTests
    {
        [Fact]
        public void EncyclopediaReturnTracker_OpenThenClose_Reopens()
        {
            var tracker = new EncyclopediaReturnTracker();
            Assert.Equal(EncyclopediaReturnState.Idle, tracker.State);

            tracker.Arm("hero:lord_1");
            Assert.Equal(EncyclopediaReturnState.WaitingForOpen, tracker.State);
            Assert.Equal("hero:lord_1", tracker.CurrentLink);

            var action1 = tracker.Step(true, out var reason1);
            Assert.Equal(EncyclopediaReturnAction.None, action1);
            Assert.Null(reason1);
            Assert.Equal(EncyclopediaReturnState.WaitingForClose, tracker.State);
            Assert.True(tracker.JustOpened);
            Assert.Equal(1, tracker.OpenedAfterFrames);

            var action2 = tracker.Step(false, out var reason2);
            Assert.Equal(EncyclopediaReturnAction.Reopen, action2);
            Assert.Null(reason2);
            Assert.Equal(EncyclopediaReturnState.Idle, tracker.State);
        }

        [Fact]
        public void EncyclopediaReturnTracker_MultipleFramesOpen_ReopensOnlyAfterClose()
        {
            var tracker = new EncyclopediaReturnTracker();
            tracker.Arm("hero:lord_1");

            var action1 = tracker.Step(true);
            Assert.Equal(EncyclopediaReturnAction.None, action1);
            Assert.Equal(EncyclopediaReturnState.WaitingForClose, tracker.State);
            Assert.True(tracker.JustOpened);

            for (int i = 0; i < 50; i++)
            {
                var actionWaiting = tracker.Step(true);
                Assert.Equal(EncyclopediaReturnAction.None, actionWaiting);
                Assert.Equal(EncyclopediaReturnState.WaitingForClose, tracker.State);
                Assert.False(tracker.JustOpened);
            }

            var actionClose = tracker.Step(false);
            Assert.Equal(EncyclopediaReturnAction.Reopen, actionClose);
            Assert.Equal(EncyclopediaReturnState.Idle, tracker.State);
        }

        [Fact]
        public void EncyclopediaReturnTracker_ThirtyFalseFrames_GivesUp()
        {
            var tracker = new EncyclopediaReturnTracker();
            tracker.Arm("settlement:town_A");

            for (int i = 1; i <= 29; i++)
            {
                var action = tracker.Step(false, out var reason);
                Assert.Equal(EncyclopediaReturnAction.None, action);
                Assert.Null(reason);
                Assert.Equal(EncyclopediaReturnState.WaitingForOpen, tracker.State);
                Assert.Equal(i, tracker.OpenWaitFrames);
            }

            var action30 = tracker.Step(false, out var reason30);
            Assert.Equal(EncyclopediaReturnAction.GaveUp, action30);
            Assert.Equal("encyclopedia never opened", reason30);
            Assert.Equal("encyclopedia never opened", tracker.LastReason);
            Assert.Equal(EncyclopediaReturnState.Idle, tracker.State);
        }

        [Fact]
        public void EncyclopediaReturnTracker_FrameTwentyNine_DoesNotGiveUp()
        {
            var tracker = new EncyclopediaReturnTracker();
            tracker.Arm("hero:lord_2");

            for (int i = 1; i <= 28; i++)
            {
                var action = tracker.Step(false);
                Assert.Equal(EncyclopediaReturnAction.None, action);
                Assert.Equal(EncyclopediaReturnState.WaitingForOpen, tracker.State);
            }

            // 第 29 幀開了不放棄
            var action29 = tracker.Step(true);
            Assert.Equal(EncyclopediaReturnAction.None, action29);
            Assert.Equal(EncyclopediaReturnState.WaitingForClose, tracker.State);
            Assert.Equal(29, tracker.OpenedAfterFrames);
            Assert.True(tracker.JustOpened);

            var actionClose = tracker.Step(false);
            Assert.Equal(EncyclopediaReturnAction.Reopen, actionClose);
            Assert.Equal(EncyclopediaReturnState.Idle, tracker.State);
        }

        [Fact]
        public void EncyclopediaReturnTracker_NullObservation_GivesUp()
        {
            // From WaitingForOpen
            var tracker = new EncyclopediaReturnTracker();
            tracker.Arm("hero:lord_3");

            var actionNull = tracker.Step(null, out var reason);
            Assert.Equal(EncyclopediaReturnAction.GaveUp, actionNull);
            Assert.Equal("map screen or encyclopedia view not available", reason);
            Assert.Equal("map screen or encyclopedia view not available", tracker.LastReason);
            Assert.Equal(EncyclopediaReturnState.Idle, tracker.State);

            // From WaitingForClose
            tracker.Arm("hero:lord_3");
            tracker.Step(true);
            Assert.Equal(EncyclopediaReturnState.WaitingForClose, tracker.State);

            var actionNull2 = tracker.Step(null, out var reason2);
            Assert.Equal(EncyclopediaReturnAction.GaveUp, actionNull2);
            Assert.Equal("map screen or encyclopedia view not available", reason2);
            Assert.Equal(EncyclopediaReturnState.Idle, tracker.State);
        }

        [Fact]
        public void EncyclopediaReturnTracker_AfterCancel_StepReturnsNone()
        {
            var tracker = new EncyclopediaReturnTracker();
            tracker.Arm("hero:lord_4");

            tracker.Cancel("a mission started");
            Assert.Equal(EncyclopediaReturnState.Idle, tracker.State);
            Assert.Equal("a mission started", tracker.LastReason);

            Assert.Equal(EncyclopediaReturnAction.None, tracker.Step(false));
            Assert.Equal(EncyclopediaReturnAction.None, tracker.Step(true));
            Assert.Equal(EncyclopediaReturnAction.None, tracker.Step(null));
        }

        [Fact]
        public void EncyclopediaReturnTracker_WhenIdle_StepReturnsNone()
        {
            var tracker = new EncyclopediaReturnTracker();
            Assert.Equal(EncyclopediaReturnState.Idle, tracker.State);

            Assert.Equal(EncyclopediaReturnAction.None, tracker.Step(false));
            Assert.Equal(EncyclopediaReturnAction.None, tracker.Step(true));
            Assert.Equal(EncyclopediaReturnAction.None, tracker.Step(null));
        }

        [Fact]
        public void EncyclopediaReturnTracker_RepeatedArm_UsesLatestArm()
        {
            var tracker = new EncyclopediaReturnTracker();
            tracker.Arm("link_first");

            for (int i = 0; i < 20; i++)
            {
                tracker.Step(false);
            }
            Assert.Equal(20, tracker.OpenWaitFrames);

            // 重複 Arm，重設為新連結且幀數歸零
            tracker.Arm("link_second");
            Assert.Equal(EncyclopediaReturnState.WaitingForOpen, tracker.State);
            Assert.Equal("link_second", tracker.CurrentLink);
            Assert.Equal(0, tracker.OpenWaitFrames);

            for (int i = 1; i <= 29; i++)
            {
                var action = tracker.Step(false);
                Assert.Equal(EncyclopediaReturnAction.None, action);
            }

            var action30 = tracker.Step(false, out var reason);
            Assert.Equal(EncyclopediaReturnAction.GaveUp, action30);
            Assert.Equal("encyclopedia never opened", reason);
        }

        [Fact]
        public void ChronicleLogFormatter_FormatLinkQueued_MatchesVerbatim()
        {
            string line = ChronicleLogFormatter.FormatLinkQueued("hero:lord_4_6");
            Assert.Equal("Chronicle link: hero:lord_4_6 clicked - handled on the next application tick (not inside the widget's own update).", line);
        }

        [Fact]
        public void ChronicleLogFormatter_FormatOpeningLink_MatchesVerbatim()
        {
            string line = ChronicleLogFormatter.FormatOpeningLink("hero:lord_4_6");
            Assert.Equal("Chronicle link: hero:lord_4_6 - closing the chronicle and opening the encyclopedia; will reopen when it closes.", line);
        }

        [Fact]
        public void ChronicleLogFormatter_FormatEncyclopediaOpened_MatchesVerbatim()
        {
            string line1 = ChronicleLogFormatter.FormatEncyclopediaOpened(1);
            Assert.Equal("Chronicle link: encyclopedia opened after 1 frame(s).", line1);

            string line5 = ChronicleLogFormatter.FormatEncyclopediaOpened(5);
            Assert.Equal("Chronicle link: encyclopedia opened after 5 frame(s).", line5);
        }

        [Fact]
        public void ChronicleLogFormatter_FormatEncyclopediaClosedReopening_MatchesVerbatim()
        {
            string line = ChronicleLogFormatter.FormatEncyclopediaClosedReopening();
            Assert.Equal("Chronicle link: encyclopedia closed - reopening the chronicle.", line);
        }

        [Fact]
        public void ChronicleLogFormatter_FormatNotReopening_MatchesVerbatim()
        {
            string line1 = ChronicleLogFormatter.FormatNotReopening("encyclopedia never opened");
            Assert.Equal("Chronicle link: not reopening - encyclopedia never opened.", line1);

            string line2 = ChronicleLogFormatter.FormatNotReopening("map screen or encyclopedia view not available");
            Assert.Equal("Chronicle link: not reopening - map screen or encyclopedia view not available.", line2);

            string line3 = ChronicleLogFormatter.FormatNotReopening("the player reopened it");
            Assert.Equal("Chronicle link: not reopening - the player reopened it.", line3);

            string line4 = ChronicleLogFormatter.FormatNotReopening("a mission started");
            Assert.Equal("Chronicle link: not reopening - a mission started.", line4);
        }

        [Fact]
        public void ChronicleLogFormatter_FormatIgnoredLink_MatchesVerbatim()
        {
            string line = ChronicleLogFormatter.FormatIgnoredLink("settlement:town_V6");
            Assert.Equal("Chronicle link: ignored settlement:town_V6 - encyclopedia links are disabled (presentation.encyclopediaLinksEnabled = false).", line);
        }
    }
}
