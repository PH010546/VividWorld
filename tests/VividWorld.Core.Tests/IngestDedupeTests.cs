#nullable enable

using System.Collections.Generic;
using VividWorld.Core.Events;
using VividWorld.Core.Ingest;
using VividWorld.Core.Persistence;
using Xunit;

namespace VividWorld.Core.Tests
{
    public class IngestDedupeTests
    {
        [Fact]
        public void FindSameSubmission_SameTypeSameDaySameParticipants_ReturnsEventId()
        {
            var index = new RumorIndex
            {
                Entries = new List<RumorIndexEntry>
                {
                    new RumorIndexEntry
                    {
                        EventId = "evt_duel_123",
                        Type = "duel",
                        Day = 45.2,
                        ParticipantHeroIds = new List<string> { "hero_a", "hero_b" }
                    }
                }
            };

            var submission = new EventSubmission
            {
                Type = "duel",
                Day = 45.8,
                Participants = new Dictionary<string, string>
                {
                    { "challenger", "hero_a" },
                    { "target", "hero_b" }
                }
            };

            string? found = IngestDedupe.FindSameSubmission(index, submission);
            Assert.Equal("evt_duel_123", found);
        }

        [Fact]
        public void FindSameSubmission_DifferentType_ReturnsNull()
        {
            var index = new RumorIndex
            {
                Entries = new List<RumorIndexEntry>
                {
                    new RumorIndexEntry
                    {
                        EventId = "evt_duel_123",
                        Type = "duel",
                        Day = 45.2,
                        ParticipantHeroIds = new List<string> { "hero_a", "hero_b" }
                    }
                }
            };

            var submission = new EventSubmission
            {
                Type = "tournament_win",
                Day = 45.2,
                Participants = new Dictionary<string, string>
                {
                    { "challenger", "hero_a" },
                    { "target", "hero_b" }
                }
            };

            string? found = IngestDedupe.FindSameSubmission(index, submission);
            Assert.Null(found);
        }

        [Fact]
        public void FindSameSubmission_DifferentIntegerDay_ReturnsNull()
        {
            var index = new RumorIndex
            {
                Entries = new List<RumorIndexEntry>
                {
                    new RumorIndexEntry
                    {
                        EventId = "evt_duel_123",
                        Type = "duel",
                        Day = 45.9,
                        ParticipantHeroIds = new List<string> { "hero_a", "hero_b" }
                    }
                }
            };

            var submission = new EventSubmission
            {
                Type = "duel",
                Day = 46.1,
                Participants = new Dictionary<string, string>
                {
                    { "challenger", "hero_a" },
                    { "target", "hero_b" }
                }
            };

            string? found = IngestDedupe.FindSameSubmission(index, submission);
            Assert.Null(found);
        }

        [Fact]
        public void FindSameSubmission_SameIntegerDayDifferentFraction_ReturnsEventId()
        {
            var index = new RumorIndex
            {
                Entries = new List<RumorIndexEntry>
                {
                    new RumorIndexEntry
                    {
                        EventId = "evt_duel_45",
                        Type = "duel",
                        Day = 45.01,
                        ParticipantHeroIds = new List<string> { "hero_a" }
                    }
                }
            };

            var submission = new EventSubmission
            {
                Type = "duel",
                Day = 45.99,
                Participants = new Dictionary<string, string>
                {
                    { "hero", "hero_a" }
                }
            };

            string? found = IngestDedupe.FindSameSubmission(index, submission);
            Assert.Equal("evt_duel_45", found);
        }

        [Fact]
        public void FindSameSubmission_ParticipantOrderDifferent_DuplicatesAndEmptyIgnored_ReturnsEventId()
        {
            var index = new RumorIndex
            {
                Entries = new List<RumorIndexEntry>
                {
                    new RumorIndexEntry
                    {
                        EventId = "evt_party_1",
                        Type = "feast",
                        Day = 10.0,
                        ParticipantHeroIds = new List<string> { "hero_b", "hero_a", "hero_b", "" }
                    }
                }
            };

            var submission = new EventSubmission
            {
                Type = "feast",
                Day = 10.5,
                Participants = new Dictionary<string, string>
                {
                    { "p1", "hero_a" },
                    { "p2", "hero_b" },
                    { "p3", "" },
                    { "p4", "hero_a" }
                }
            };

            string? found = IngestDedupe.FindSameSubmission(index, submission);
            Assert.Equal("evt_party_1", found);
        }

        [Fact]
        public void FindSameSubmission_ParticipantCountDifferent_ReturnsNull()
        {
            var index = new RumorIndex
            {
                Entries = new List<RumorIndexEntry>
                {
                    new RumorIndexEntry
                    {
                        EventId = "evt_feast_1",
                        Type = "feast",
                        Day = 10.0,
                        ParticipantHeroIds = new List<string> { "hero_a", "hero_b" }
                    }
                }
            };

            // More participants
            var subMore = new EventSubmission
            {
                Type = "feast",
                Day = 10.0,
                Participants = new Dictionary<string, string>
                {
                    { "p1", "hero_a" },
                    { "p2", "hero_b" },
                    { "p3", "hero_c" }
                }
            };
            Assert.Null(IngestDedupe.FindSameSubmission(index, subMore));

            // Fewer participants
            var subFewer = new EventSubmission
            {
                Type = "feast",
                Day = 10.0,
                Participants = new Dictionary<string, string>
                {
                    { "p1", "hero_a" }
                }
            };
            Assert.Null(IngestDedupe.FindSameSubmission(index, subFewer));
        }

        [Fact]
        public void FindSameSubmission_ParticipantCaseDifferent_ReturnsNull_AndNullInputsDoNotThrow()
        {
            var index = new RumorIndex
            {
                Entries = new List<RumorIndexEntry>
                {
                    new RumorIndexEntry
                    {
                        EventId = "evt_1",
                        Type = "duel",
                        Day = 20.0,
                        ParticipantHeroIds = new List<string> { "Hero_A" }
                    }
                }
            };

            var subCase = new EventSubmission
            {
                Type = "duel",
                Day = 20.0,
                Participants = new Dictionary<string, string>
                {
                    { "hero", "hero_a" }
                }
            };

            // Ordinal case-sensitivity: "Hero_A" != "hero_a"
            Assert.Null(IngestDedupe.FindSameSubmission(index, subCase));

            // Null checks do not throw
            Assert.Null(IngestDedupe.FindSameSubmission(null, subCase));
            Assert.Null(IngestDedupe.FindSameSubmission(index, null));
            Assert.Null(IngestDedupe.FindSameSubmission(null, null));
        }
    }
}
