#nullable enable
using System.Collections.Generic;
using VividWorld.Core.Presentation;
using Xunit;

namespace VividWorld.Core.Tests.Presentation
{
    public class NativeNotificationFilterTests
    {
        [Theory]
        [InlineData("TakePrisonerLogEntry")]
        [InlineData("EndCaptivityLogEntry")]
        [InlineData("CharacterKilledLogEntry")]
        [InlineData("CharacterMarriedLogEntry")]
        [InlineData("CharacterBornLogEntry")]
        [InlineData("ChildbirthLogEntry")]
        [InlineData("TournamentWonLogEntry")]
        public void Mute_AllSixCoveredTypes_MutedWhenNoPlayerClanParticipants(string logType)
        {
            var participants = new List<NativeNotificationParticipant>
            {
                new NativeNotificationParticipant("Derthert", "lord_1_1", false),
                new NativeNotificationParticipant("Caladog", "lord_2_1", false)
            };

            var result = NativeNotificationFilter.Evaluate(
                logType,
                enabled: true,
                participants: participants,
                isVictimClanPlayerClan: false);

            Assert.True(result.ShouldMute);
            Assert.Equal("muted", result.Reason);
            Assert.Equal($"native notification muted: {logType} (Derthert (lord_1_1), Caladog (lord_2_1))", result.LogLine);
        }

        [Fact]
        public void Show_WhenAnyParticipantIsPlayerOrPlayerClan()
        {
            var participants = new List<NativeNotificationParticipant>
            {
                new NativeNotificationParticipant("Derthert", "lord_1_1", false),
                new NativeNotificationParticipant("Player Brother", "companion_1", true),
                new NativeNotificationParticipant("Caladog", "lord_2_1", false)
            };

            var result = NativeNotificationFilter.Evaluate(
                "TakePrisonerLogEntry",
                enabled: true,
                participants: participants,
                isVictimClanPlayerClan: false);

            Assert.False(result.ShouldMute);
            Assert.Equal("Player Brother (companion_1) is the player or in the player's clan", result.Reason);
            Assert.Equal("native notification shown: TakePrisonerLogEntry, Player Brother (companion_1) is the player or in the player's clan", result.LogLine);
        }

        [Fact]
        public void Show_WhenVictimClanIsPlayerClan()
        {
            var participants = new List<NativeNotificationParticipant>
            {
                new NativeNotificationParticipant("Victim", "lord_1_1", false),
                new NativeNotificationParticipant("Killer", "lord_2_1", false)
            };

            var result = NativeNotificationFilter.Evaluate(
                "CharacterKilledLogEntry",
                enabled: true,
                participants: participants,
                isVictimClanPlayerClan: true);

            Assert.False(result.ShouldMute);
            Assert.Equal("victim clan is the player's clan", result.Reason);
            Assert.Equal("native notification shown: CharacterKilledLogEntry, victim clan is the player's clan", result.LogLine);
        }

        [Theory]
        [InlineData("KingdomDecisionLogEntry")]
        [InlineData("BattleStartedLogEntry")]
        [InlineData("DeclareWarLogEntry")]
        [InlineData("NonExistentLogEntry")]
        public void Show_WhenUncoveredType_ReturnsNoLog(string uncoveredType)
        {
            var participants = new List<NativeNotificationParticipant>
            {
                new NativeNotificationParticipant("Derthert", "lord_1_1", false)
            };

            var result = NativeNotificationFilter.Evaluate(
                uncoveredType,
                enabled: true,
                participants: participants,
                isVictimClanPlayerClan: false);

            Assert.False(result.ShouldMute);
            Assert.Equal("not covered", result.Reason);
            Assert.Null(result.LogLine);
        }

        [Fact]
        public void Show_WhenSwitchOff()
        {
            var participants = new List<NativeNotificationParticipant>
            {
                new NativeNotificationParticipant("Derthert", "lord_1_1", false)
            };

            var result = NativeNotificationFilter.Evaluate(
                "TakePrisonerLogEntry",
                enabled: false,
                participants: participants,
                isVictimClanPlayerClan: false);

            Assert.False(result.ShouldMute);
            Assert.Equal("switch off", result.Reason);
            Assert.Equal("native notification shown: TakePrisonerLogEntry, switch off", result.LogLine);
        }

        [Fact]
        public void Safe_WhenParticipantsEmptyOrNull()
        {
            // null participants list
            var resultNull = NativeNotificationFilter.Evaluate(
                "TakePrisonerLogEntry",
                enabled: true,
                participants: null,
                isVictimClanPlayerClan: false);

            Assert.True(resultNull.ShouldMute);
            Assert.Equal("native notification muted: TakePrisonerLogEntry ()", resultNull.LogLine);

            // empty participants list
            var resultEmpty = NativeNotificationFilter.Evaluate(
                "EndCaptivityLogEntry",
                enabled: true,
                participants: new List<NativeNotificationParticipant>(),
                isVictimClanPlayerClan: false);

            Assert.True(resultEmpty.ShouldMute);
            Assert.Equal("native notification muted: EndCaptivityLogEntry ()", resultEmpty.LogLine);

            // participants list containing null elements
            var participantsWithNull = new List<NativeNotificationParticipant?>
            {
                null,
                new NativeNotificationParticipant("Derthert", "lord_1_1", false),
                null
            };

            var resultWithNull = NativeNotificationFilter.Evaluate(
                "CharacterMarriedLogEntry",
                enabled: true,
                participants: participantsWithNull,
                isVictimClanPlayerClan: false);

            Assert.True(resultWithNull.ShouldMute);
            Assert.Equal("native notification muted: CharacterMarriedLogEntry (Derthert (lord_1_1))", resultWithNull.LogLine);
        }

        [Fact]
        public void TournamentWonLogEntry_Muted_WhenNoPlayerClanParticipants()
        {
            var participants = new List<NativeNotificationParticipant>
            {
                new NativeNotificationParticipant("Derthert", "lord_1_1", false)
            };

            var result = NativeNotificationFilter.Evaluate(
                "TournamentWonLogEntry",
                enabled: true,
                participants: participants,
                isVictimClanPlayerClan: false);

            Assert.True(result.ShouldMute);
            Assert.Equal("muted", result.Reason);
            Assert.Equal("native notification muted: TournamentWonLogEntry (Derthert (lord_1_1))", result.LogLine);
        }

        [Fact]
        public void TournamentWonLogEntry_Shown_WhenWinnerIsPlayerOrPlayerClan()
        {
            var participants = new List<NativeNotificationParticipant>
            {
                new NativeNotificationParticipant("Player", "player_char", true)
            };

            var result = NativeNotificationFilter.Evaluate(
                "TournamentWonLogEntry",
                enabled: true,
                participants: participants,
                isVictimClanPlayerClan: false);

            Assert.False(result.ShouldMute);
            Assert.Equal("Player (player_char) is the player or in the player's clan", result.Reason);
            Assert.Equal("native notification shown: TournamentWonLogEntry, Player (player_char) is the player or in the player's clan", result.LogLine);
        }
    }
}
