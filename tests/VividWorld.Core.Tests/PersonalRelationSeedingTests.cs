using System.Collections.Generic;
using System.Linq;
using VividWorld.Core.Relations;
using Xunit;

namespace VividWorld.Core.Tests
{
    public class PersonalRelationSeedingTests
    {
        private const string PlayerClan = "clan_player";

        private static SeedCandidate Hero(string id, string? clan, string? leader, bool alive = true, bool isPlayer = false) =>
            new SeedCandidate(id, id, clan, leader, alive, isPlayer);

        [Fact]
        public void SelectTargets_PicksOtherClanNonLeaders()
        {
            var heroes = new[]
            {
                Hero("ergun", "clan_5", "ergun"),
                Hero("nirwen", "clan_5", "ergun"),
                Hero("child", "clan_5", "ergun")
            };

            var ids = PersonalRelationSeeding.SelectTargets(heroes, PlayerClan).Select(h => h.Id).ToList();

            Assert.Equal(new[] { "nirwen", "child" }, ids);
        }

        [Fact]
        public void SelectTargets_SkipsClanLeaders()
        {
            var heroes = new[] { Hero("ergun", "clan_5", "ergun") };
            Assert.Empty(PersonalRelationSeeding.SelectTargets(heroes, PlayerClan));
        }

        [Fact]
        public void SelectTargets_SkipsThePlayersOwnClan()
        {
            var heroes = new[]
            {
                Hero("companion", PlayerClan, "player"),
                Hero("sibling", PlayerClan, "player")
            };
            Assert.Empty(PersonalRelationSeeding.SelectTargets(heroes, PlayerClan));
        }

        [Fact]
        public void SelectTargets_SkipsHeroesWithoutClan()
        {
            var heroes = new[] { Hero("wanderer", null, null), Hero("wanderer2", "", null) };
            Assert.Empty(PersonalRelationSeeding.SelectTargets(heroes, PlayerClan));
        }

        [Fact]
        public void SelectTargets_SkipsClansWithoutLeader()
        {
            var heroes = new[] { Hero("orphan", "clan_9", null) };
            Assert.Empty(PersonalRelationSeeding.SelectTargets(heroes, PlayerClan));
        }

        [Fact]
        public void SelectTargets_SkipsTheDeadAndThePlayer()
        {
            var heroes = new[]
            {
                Hero("dead", "clan_5", "ergun", alive: false),
                Hero("player", "clan_5", "ergun", isPlayer: true)
            };
            Assert.Empty(PersonalRelationSeeding.SelectTargets(heroes, PlayerClan));
        }

        [Fact]
        public void SelectTargets_WithNoPlayerClan_StillSkipsLeadersAndClanless()
        {
            var heroes = new[]
            {
                Hero("a", "clan_1", "boss"),
                Hero("boss", "clan_1", "boss"),
                Hero("c", null, null)
            };
            var ids = PersonalRelationSeeding.SelectTargets(heroes, null).Select(h => h.Id).ToList();
            Assert.Equal(new[] { "a" }, ids);
        }

        [Fact]
        public void Gate_SwitchOff_WinsOverEverything()
        {
            Assert.Equal(SeedGate.SwitchOff, PersonalRelationSeeding.Gate(false, ""));
            Assert.Equal(SeedGate.SwitchOff, PersonalRelationSeeding.Gate(false, "seeded:10"));
        }

        [Fact]
        public void Gate_EmptyMarker_Proceeds_AsOldSavesDo()
        {
            Assert.Equal(SeedGate.Proceed, PersonalRelationSeeding.Gate(true, ""));
            Assert.Equal(SeedGate.Proceed, PersonalRelationSeeding.Gate(true, null));
        }

        [Theory]
        [InlineData("seeded:91120")]
        [InlineData("skipped-other-mod:5")]
        [InlineData("skipped-no-candidates:0")]
        [InlineData("something-unrecognised")]
        public void Gate_AnyNonEmptyMarker_MeansDone(string marker)
        {
            Assert.Equal(SeedGate.AlreadyDone, PersonalRelationSeeding.Gate(true, marker));
        }

        [Fact]
        public void OtherModAlreadySeparates_WhenSecondHeroIsTheProbeHimself()
        {
            Assert.True(PersonalRelationSeeding.OtherModAlreadySeparates("nirwen", "nirwen"));
        }

        [Fact]
        public void OtherModAlreadySeparates_FalseWhenSwappedForTheClanLeader()
        {
            Assert.False(PersonalRelationSeeding.OtherModAlreadySeparates("nirwen", "ergun"));
            Assert.False(PersonalRelationSeeding.OtherModAlreadySeparates("nirwen", null));
        }

        [Theory]
        [InlineData(SeedOutcome.Seeded, 91120, "seeded:91120")]
        [InlineData(SeedOutcome.SkippedOtherMod, 7, "skipped-other-mod:7")]
        [InlineData(SeedOutcome.SkippedNoCandidates, 0, "skipped-no-candidates:0")]
        public void Marker_RoundTrips(SeedOutcome outcome, int day, string expected)
        {
            string marker = PersonalRelationSeeding.MakeMarker(outcome, day);
            Assert.Equal(expected, marker);

            Assert.True(PersonalRelationSeeding.TryParseMarker(marker, out var parsedOutcome, out int parsedDay));
            Assert.Equal(outcome, parsedOutcome);
            Assert.Equal(day, parsedDay);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("seeded")]
        [InlineData("seeded:")]
        [InlineData(":12")]
        [InlineData("seeded:abc")]
        [InlineData("unknown:12")]
        public void TryParseMarker_RejectsMalformed(string? marker)
        {
            Assert.False(PersonalRelationSeeding.TryParseMarker(marker, out _, out _));
        }

        [Fact]
        public void FormatOverwrittenLines_ListsOnlyNonZeroOldValues()
        {
            var changes = new List<SeedChange>
            {
                new SeedChange("lord_5_4", "Nirwen", 0, 23, "Ergun"),
                new SeedChange("lord_5_5", "Kid", 4, 23, "Ergun")
            };

            var lines = PersonalRelationSeeding.FormatOverwrittenLines(changes);

            Assert.Single(lines);
            Assert.Equal("  Kid (lord_5_5): personal 4 -> 23 (clan leader Ergun 23)", lines[0]);
        }

        [Fact]
        public void FormatOverwrittenLines_CapsAtThirtyAndCountsTheRest()
        {
            var changes = Enumerable.Range(1, 35)
                .Select(i => new SeedChange("h" + i, "Name" + i, i, 50, "Boss"))
                .ToList();

            var lines = PersonalRelationSeeding.FormatOverwrittenLines(changes);

            Assert.Equal(31, lines.Count);
            Assert.Equal("  ... and 5 more", lines[30]);
        }

        [Fact]
        public void LogLines_MatchTheDocumentedShapes()
        {
            Assert.Equal("Personal relations with the player: seeded 12 heroes", PersonalRelationSeeding.FormatSeeded(12));
            Assert.Equal("Personal relations with the player: skipped - another mod already keeps player pairs per person", PersonalRelationSeeding.FormatSkippedOtherMod());
            Assert.Equal("Personal relations with the player: skipped - no other-clan non-leaders", PersonalRelationSeeding.FormatSkippedNoCandidates());
            Assert.Equal("Personal relations with the player: already done (seeded:3)", PersonalRelationSeeding.FormatAlreadyDone("seeded:3"));
            Assert.Equal("Personal relations with the player: switch is off", PersonalRelationSeeding.FormatSwitchOffNoop());
            Assert.Equal("Personal relations with the player: switched on in settings", PersonalRelationSeeding.FormatSwitched(true));
            Assert.Equal("Personal relations with the player: switched off in settings", PersonalRelationSeeding.FormatSwitched(false));
        }
    }
}
