using System.Collections.Generic;
using VividWorld.Core.Config;
using VividWorld.Core.Events;
using VividWorld.Core.Ingest;
using VividWorld.Core.Rumors;
using VividWorld.Core.Tests.Fakes;
using Xunit;

namespace VividWorld.Core.Tests
{
    public class Hop0SeedingAnchorTests
    {
        [Fact]
        public void AnchorOf_EmptyKnowingRoles_ReturnsFirstParticipant()
        {
            var submission = new EventSubmission
            {
                Type = "hero_executed",
                Day = 10.0,
                Origin = EventOrigin.Public,
                Participants = new Dictionary<string, string>
                {
                    ["victim"] = "CharacterObject_19957",
                    ["killer"] = "lord_2_5"
                },
                KnowingRoles = new HashSet<string>()
            };

            string? anchor = Hop0Seeding.AnchorOf(submission);

            Assert.Equal("CharacterObject_19957", anchor);
        }

        [Fact]
        public void AnchorOf_KnowingRolesOnlyKiller_ReturnsKillerNotVictim()
        {
            var submission = new EventSubmission
            {
                Type = "hero_murdered",
                Day = 10.0,
                Origin = EventOrigin.Secret,
                Participants = new Dictionary<string, string>
                {
                    ["victim"] = "CharacterObject_19957",
                    ["killer"] = "lord_2_5"
                },
                KnowingRoles = new HashSet<string> { "killer" }
            };

            string? anchor = Hop0Seeding.AnchorOf(submission);

            Assert.Equal("lord_2_5", anchor);
        }

        [Fact]
        public void AnchorOf_MatchesHeroIdPassedToWitnessesAtDuringSeed()
        {
            var channel = new FakePropagationChannel();
            var traits = new FakeHeroTraitLookup();
            var cfg = new PropagationConfig();

            traits.Set(new TraitProfile { HeroId = "hero_witness", IsAlive = true, IsLord = true });
            traits.Set(new TraitProfile { HeroId = "lord_2_5", IsAlive = true, IsLord = true });
            traits.Set(new TraitProfile { HeroId = "CharacterObject_19957", IsAlive = true, IsLord = true });

            var submission = new EventSubmission
            {
                Type = "hero_executed",
                Day = 100.0,
                Origin = EventOrigin.Public,
                AutoResolveWitnesses = true,
                Participants = new Dictionary<string, string>
                {
                    ["victim"] = "CharacterObject_19957",
                    ["killer"] = "lord_2_5"
                },
                KnowingRoles = new HashSet<string>() // empty -> victim is first knowing participant
            };

            channel.AddWitness("CharacterObject_19957", "hero_witness");

            var evt = new WorldEvent
            {
                EventId = "evt_0100_seed_anchor",
                Origin = EventOrigin.Public,
                Participants = new Dictionary<string, string>(submission.Participants)
            };

            string? expectedAnchor = Hop0Seeding.AnchorOf(submission);
            Assert.Equal("CharacterObject_19957", expectedAnchor);

            Hop0Seeding.Seed(evt, submission, channel, traits, cfg, "player", 100.0);

            Assert.Equal(expectedAnchor, channel.LastWitnessQueryHeroId);
        }

        [Fact]
        public void IsKnowingRole_AgreesWithWhoSeedActuallyPutAtHop0_AndSummarySaysTheSame()
        {
            var channel = new FakePropagationChannel();
            var traits = new FakeHeroTraitLookup();
            var submission = new EventSubmission
            {
                Type = "hero_murdered",
                Day = 10.0,
                Origin = EventOrigin.Secret,
                Participants = new Dictionary<string, string>
                {
                    ["victim"] = "CharacterObject_19957",
                    ["killer"] = "lord_2_5"
                },
                KnowingRoles = new HashSet<string> { "killer" }
            };
            var evt = new WorldEvent
            {
                EventId = "evt_0010_knowing",
                Origin = EventOrigin.Secret,
                Participants = new Dictionary<string, string>(submission.Participants)
            };

            Hop0Seeding.Seed(evt, submission, channel, traits, new PropagationConfig(), "player", 10.0);

            var hop0 = new HashSet<string>();
            foreach (var k in evt.KnownBy) if (k.Hop == 0) hop0.Add(k.HeroId);
            foreach (var kvp in submission.Participants)
            {
                Assert.Equal(hop0.Contains(kvp.Value), Hop0Seeding.IsKnowingRole(submission.KnowingRoles, kvp.Key));
            }

            string line = Hop0Summary.Format(hop0.Count, submission.Participants, submission.KnowingRoles,
                (System.Func<string, string?>?)null, Hop0WitnessInfo.Secret());
            Assert.Equal("hop0: 1 knower - participants: killer=lord_2_5; not seeded: victim=CharacterObject_19957 (not in knowingRoles); witnesses: 0 (secret)", line);
        }

        [Fact]
        public void AnchorOf_NullOrEmptyParticipants_ReturnsNull()
        {
            var submissionNull = new EventSubmission
            {
                Participants = null!
            };
            Assert.Null(Hop0Seeding.AnchorOf(submissionNull));

            var submissionEmpty = new EventSubmission
            {
                Participants = new Dictionary<string, string>()
            };
            Assert.Null(Hop0Seeding.AnchorOf(submissionEmpty));
        }
    }
}
