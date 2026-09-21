using System.Collections.Generic;
using VividWorld.Core.Ingest;
using Xunit;

namespace VividWorld.Core.Tests
{
    public class Hop0SummaryTests
    {
        [Fact]
        public void Format_Example1_MatchesDocumentedFormatExactly()
        {
            var participants = new[]
            {
                new KeyValuePair<string, string>("captor", "lord_5_15"),
                new KeyValuePair<string, string>("prisoner", "CharacterObject_19987")
            };
            var cannotTell = new Dictionary<string, string?>
            {
                ["lord_5_15"] = null,
                ["CharacterObject_19987"] = "prisoner"
            };
            var rejections = new[]
            {
                new KeyValuePair<string, int>("notable", 2),
                new KeyValuePair<string, int>("max witnesses cap", 1)
            };
            var witnessInfo = Hop0WitnessInfo.AtSettlement(0, 3, "castle_B2", rejections);

            string summary = Hop0Summary.Format(2, participants, null, cannotTell, witnessInfo);

            Assert.Equal(
                "hop0: 2 knowers - participants: captor=lord_5_15, prisoner=CharacterObject_19987 [cannot tell: prisoner]; witnesses: 0 of 3 present at castle_B2 (3 rejected: notable 2, max witnesses cap 1)",
                summary);
        }

        [Fact]
        public void Format_Example2_MatchesDocumentedFormatExactly()
        {
            var participants = new[]
            {
                new KeyValuePair<string, string>("killer", "lord_2_5"),
                new KeyValuePair<string, string>("victim", "CharacterObject_19957")
            };
            var knowingRoles = new HashSet<string> { "killer" };
            var witnessInfo = Hop0WitnessInfo.Secret();

            string summary = Hop0Summary.Format(1, participants, knowingRoles, (IReadOnlyDictionary<string, string?>?)null, witnessInfo);

            Assert.Equal(
                "hop0: 1 knower - participants: killer=lord_2_5; not seeded: victim=CharacterObject_19957 (not in knowingRoles); witnesses: 0 (secret)",
                summary);
        }

        [Fact]
        public void Format_Example3_MatchesDocumentedFormatExactly()
        {
            var participants = new[]
            {
                new KeyValuePair<string, string>("victim", "CharacterObject_19957"),
                new KeyValuePair<string, string>("killer", "lord_2_5")
            };
            var cannotTell = new Dictionary<string, string?>
            {
                ["CharacterObject_19957"] = "dead",
                ["lord_2_5"] = null
            };
            var witnessInfo = Hop0WitnessInfo.AnchorNotInSettlement("victim", "CharacterObject_19957");

            string summary = Hop0Summary.Format(2, participants, null, cannotTell, witnessInfo);

            Assert.Equal(
                "hop0: 2 knowers - participants: victim=CharacterObject_19957 [cannot tell: dead], killer=lord_2_5; witnesses: 0 (anchor victim=CharacterObject_19957 is not in a settlement)",
                summary);
        }

        [Fact]
        public void Format_SingularKnower_UsesKnowerNotKnowers()
        {
            var participants = new[]
            {
                new KeyValuePair<string, string>("lord", "lord_1")
            };
            var witnessInfo = Hop0WitnessInfo.Secret();

            string singular = Hop0Summary.Format(1, participants, null, (IReadOnlyDictionary<string, string?>?)null, witnessInfo);
            string plural = Hop0Summary.Format(2, participants, null, (IReadOnlyDictionary<string, string?>?)null, witnessInfo);
            string zero = Hop0Summary.Format(0, participants, null, (IReadOnlyDictionary<string, string?>?)null, witnessInfo);

            Assert.StartsWith("hop0: 1 knower - ", singular);
            Assert.StartsWith("hop0: 2 knowers - ", plural);
            Assert.StartsWith("hop0: 0 knowers - ", zero);
        }

        [Fact]
        public void Format_NotSeeded_OmittedWhenAllParticipantsSeeded()
        {
            var participants = new[]
            {
                new KeyValuePair<string, string>("spouse_a", "hero_1"),
                new KeyValuePair<string, string>("spouse_b", "hero_2")
            };
            var witnessInfo = Hop0WitnessInfo.Secret();

            // When knowingRoles is empty/null, all are seeded and "not seeded" section is completely omitted
            string summaryAll = Hop0Summary.Format(2, participants, null, (IReadOnlyDictionary<string, string?>?)null, witnessInfo);
            Assert.DoesNotContain("not seeded", summaryAll);
            Assert.Contains("participants: spouse_a=hero_1, spouse_b=hero_2; witnesses: 0 (secret)", summaryAll);

            // When knowingRoles filters spouse_b out, "not seeded" section appears
            var knowing = new HashSet<string> { "spouse_a" };
            string summaryFiltered = Hop0Summary.Format(1, participants, knowing, (IReadOnlyDictionary<string, string?>?)null, witnessInfo);
            Assert.Contains("; not seeded: spouse_b=hero_2 (not in knowingRoles);", summaryFiltered);
        }

        [Fact]
        public void Format_WitnessRejections_SupportsZeroSingleAndMultipleReasons()
        {
            var participants = new[] { new KeyValuePair<string, string>("hero", "hero_1") };

            // 0 rejected
            var zeroInfo = Hop0WitnessInfo.AtSettlement(2, 2, "town_A", null);
            string sZero = Hop0Summary.Format(3, participants, null, (IReadOnlyDictionary<string, string?>?)null, zeroInfo);
            Assert.Contains("witnesses: 2 of 2 present at town_A (0 rejected)", sZero);

            // 1 reason (e.g. "6 rejected: notable")
            var singleReason = new[] { new KeyValuePair<string, int>("notable", 6) };
            var singleInfo = Hop0WitnessInfo.AtSettlement(0, 6, "town_A", singleReason);
            string sSingle = Hop0Summary.Format(1, participants, null, (IReadOnlyDictionary<string, string?>?)null, singleInfo);
            Assert.Contains("witnesses: 0 of 6 present at town_A (6 rejected: notable)", sSingle);

            // Multiple reasons (e.g. "8 rejected: notable 6, max witnesses cap 2")
            var multiReason = new[]
            {
                new KeyValuePair<string, int>("notable", 6),
                new KeyValuePair<string, int>("max witnesses cap", 2)
            };
            var multiInfo = Hop0WitnessInfo.AtSettlement(0, 8, "town_A", multiReason);
            string sMulti = Hop0Summary.Format(1, participants, null, (IReadOnlyDictionary<string, string?>?)null, multiInfo);
            Assert.Contains("witnesses: 0 of 8 present at town_A (8 rejected: notable 6, max witnesses cap 2)", sMulti);
        }
    }
}
