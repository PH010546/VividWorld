using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using VividWorld.Core.Config;
using VividWorld.Core.Events;
using VividWorld.Core.Ingest;
using VividWorld.Core.Persistence;
using Xunit;

namespace VividWorld.Core.Tests
{
    public class IngestValidationTests
    {
        private static EventSubmission CreateValidSubmission()
        {
            return new EventSubmission
            {
                Type = "duel",
                Day = 100.0,
                Origin = EventOrigin.Public,
                Participants = new Dictionary<string, string>
                {
                    ["challenger"] = "hero_1",
                    ["defender"] = "hero_2"
                },
                Facts = new List<Fact>
                {
                    new Fact
                    {
                        Id = "fact_who",
                        Category = FactCategory.Who,
                        TextId = "VividWorld_Fact_Who",
                        Text = "Hero {CHALLENGER} challenged {DEFENDER}",
                        Fragility = 2,
                        Vars = new Dictionary<string, string>
                        {
                            ["CHALLENGER"] = "hero:hero_1",
                            ["DEFENDER"] = "hero:hero_2"
                        }
                    },
                    new Fact
                    {
                        Id = "fact_what",
                        Category = FactCategory.What,
                        TextId = "VividWorld_Fact_What",
                        Text = "A duel took place",
                        Fragility = 3
                    }
                }
            };
        }

        [Fact]
        public void Accepts_TheDesignDocSampleEvent()
        {
            string path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "DesignDocSample.json");
            string json = File.ReadAllText(path);
            var evt = VividJson.Read<WorldEvent>(json);
            Assert.NotNull(evt);

            var submission = new EventSubmission
            {
                Type = evt!.Type,
                Day = evt.Day,
                Origin = evt.Origin,
                LinkedEventId = evt.LinkedEventId,
                DramaWeight = evt.DramaWeight,
                Participants = new Dictionary<string, string>(evt.Participants),
                Facts = new List<Fact>(evt.Facts),
                KnowingRoles = new HashSet<string> { "mastermind", "agent" }
            };

            var cfg = new PersistenceConfig();
            var outcome = EventSubmissionValidator.Validate(submission, cfg, id => id == "evt_0409_c1e2");

            Assert.True(outcome.IsValid);
            Assert.Null(outcome.RejectionReason);
        }

        [Fact]
        public void Accepts_DanglingRefersTo_WithWarning()
        {
            var submission = CreateValidSubmission();
            submission.LinkedEventId = "evt_dangling_linked";
            submission.Facts[0].RefersTo = "evt_dangling_fact";

            var cfg = new PersistenceConfig();
            var outcome = EventSubmissionValidator.Validate(submission, cfg, _ => false);

            Assert.True(outcome.IsValid);
            Assert.Null(outcome.RejectionReason);
            Assert.NotEmpty(outcome.Warnings);
            Assert.Contains(outcome.Warnings, w => w.Contains("evt_dangling_linked"));
            Assert.Contains(outcome.Warnings, w => w.Contains("evt_dangling_fact"));
        }

        [Fact]
        public void Accepts_MissingTextId_WithWarning()
        {
            var submission = CreateValidSubmission();
            submission.Facts[0].TextId = string.Empty;

            var cfg = new PersistenceConfig();
            var outcome = EventSubmissionValidator.Validate(submission, cfg, _ => true);

            Assert.True(outcome.IsValid);
            Assert.Null(outcome.RejectionReason);
            Assert.Contains(outcome.Warnings, w => w.Contains("TextId"));
        }

        [Fact]
        public void Rejects_MissingOrigin()
        {
            var submission = CreateValidSubmission();
            submission.Origin = null;

            var outcome = EventSubmissionValidator.Validate(submission, new PersistenceConfig(), _ => true);

            Assert.False(outcome.IsValid);
            Assert.NotNull(outcome.RejectionReason);
            Assert.Contains("origin", outcome.RejectionReason, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void Rejects_EmptyFactList()
        {
            var submission = CreateValidSubmission();
            submission.Facts = new List<Fact>();

            var outcome = EventSubmissionValidator.Validate(submission, new PersistenceConfig(), _ => true);

            Assert.False(outcome.IsValid);
            Assert.NotNull(outcome.RejectionReason);
            Assert.Contains("facts", outcome.RejectionReason, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void Rejects_FragilityOutOfRange()
        {
            var subLow = CreateValidSubmission();
            subLow.Facts[0].Fragility = 0;
            var outLow = EventSubmissionValidator.Validate(subLow, new PersistenceConfig(), _ => true);
            Assert.False(outLow.IsValid);

            var subHigh = CreateValidSubmission();
            subHigh.Facts[0].Fragility = 6;
            var outHigh = EventSubmissionValidator.Validate(subHigh, new PersistenceConfig(), _ => true);
            Assert.False(outHigh.IsValid);
        }

        [Fact]
        public void Rejects_DuplicateFactIds()
        {
            var submission = CreateValidSubmission();
            submission.Facts.Add(new Fact
            {
                Id = submission.Facts[0].Id,
                Category = FactCategory.Where,
                Text = "Duplicate ID fact",
                Fragility = 3
            });

            var outcome = EventSubmissionValidator.Validate(submission, new PersistenceConfig(), _ => true);

            Assert.False(outcome.IsValid);
            Assert.Contains("duplicate", outcome.RejectionReason, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void Rejects_EmptyFactId()
        {
            var submission = CreateValidSubmission();
            submission.Facts[0].Id = "   ";

            var outcome = EventSubmissionValidator.Validate(submission, new PersistenceConfig(), _ => true);

            Assert.False(outcome.IsValid);
            Assert.Contains("Fact ID", outcome.RejectionReason);
        }

        [Fact]
        public void Rejects_EmptyFactText()
        {
            var submission = CreateValidSubmission();
            submission.Facts[0].Text = "";

            var outcome = EventSubmissionValidator.Validate(submission, new PersistenceConfig(), _ => true);

            Assert.False(outcome.IsValid);
            Assert.Contains("text", outcome.RejectionReason, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void Rejects_EmptyParticipants()
        {
            var submission = CreateValidSubmission();
            submission.Participants = new Dictionary<string, string>();

            var outcome = EventSubmissionValidator.Validate(submission, new PersistenceConfig(), _ => true);

            Assert.False(outcome.IsValid);
            Assert.Contains("participants", outcome.RejectionReason, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void Rejects_KnowingRoleNotInParticipants()
        {
            var submission = CreateValidSubmission();
            submission.KnowingRoles = new HashSet<string> { "challenger", "unknown_role" };

            var outcome = EventSubmissionValidator.Validate(submission, new PersistenceConfig(), _ => true);

            Assert.False(outcome.IsValid);
            Assert.Contains("KnowingRoles", outcome.RejectionReason);
        }

        [Fact]
        public void Rejects_TooManyFacts()
        {
            var submission = CreateValidSubmission();
            var cfg = new PersistenceConfig { MaxFactsPerEvent = 1 };

            var outcome = EventSubmissionValidator.Validate(submission, cfg, _ => true);

            Assert.False(outcome.IsValid);
            Assert.Contains("exceeds", outcome.RejectionReason, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void Rejects_UnknownVarPrefix()
        {
            var submission = CreateValidSubmission();
            submission.Facts[0].Vars = new Dictionary<string, string>
            {
                ["CHALLENGER"] = "invalid_prefix:hero_1"
            };

            var outcome = EventSubmissionValidator.Validate(submission, new PersistenceConfig(), _ => true);

            Assert.False(outcome.IsValid);
            Assert.Contains("prefix", outcome.RejectionReason, StringComparison.OrdinalIgnoreCase);
        }
    }
}
