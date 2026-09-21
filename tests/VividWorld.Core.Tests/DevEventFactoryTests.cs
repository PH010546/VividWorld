using System.Collections.Generic;
using VividWorld.Core.Config;
using VividWorld.Core.Events;
using VividWorld.Core.Ingest;
using Xunit;

namespace VividWorld.Core.Tests
{
    public class DevEventFactoryTests
    {
        [Fact]
        public void DevTestSubmission_PassesTheIngestValidator()
        {
            var submission = new EventSubmission
            {
                Type = "dev_test",
                Origin = EventOrigin.Public,
                Day = 10.0,
                Participants = new Dictionary<string, string>
                {
                    ["mastermind"] = "hero_1",
                    ["target"] = "hero_2"
                },
                Facts = new List<Fact>
                {
                    new Fact
                    {
                        Id = "f_who",
                        Category = FactCategory.Who,
                        Fragility = 1,
                        Text = "Mastermind hero_1 targeted hero_2"
                    },
                    new Fact
                    {
                        Id = "f_what",
                        Category = FactCategory.What,
                        Fragility = 3,
                        Text = "A public dispute erupted between the parties"
                    },
                    new Fact
                    {
                        Id = "f_where",
                        Category = FactCategory.Where,
                        Fragility = 5,
                        Text = "The incident occurred in the local territory"
                    }
                }
            };

            var outcome = EventSubmissionValidator.Validate(submission, new PersistenceConfig(), _ => false);
            Assert.True(outcome.IsValid);
            Assert.Null(outcome.RejectionReason);
        }

        [Fact]
        public void DevTestSubmission_WithoutFactId_IsRejected()
        {
            var submission = new EventSubmission
            {
                Type = "dev_test",
                Origin = EventOrigin.Public,
                Day = 10.0,
                Participants = new Dictionary<string, string>
                {
                    ["mastermind"] = "hero_1",
                    ["target"] = "hero_2"
                },
                Facts = new List<Fact>
                {
                    new Fact
                    {
                        Category = FactCategory.Who,
                        Fragility = 1,
                        Text = "Mastermind hero_1 targeted hero_2"
                    },
                    new Fact
                    {
                        Id = "f_what",
                        Category = FactCategory.What,
                        Fragility = 3,
                        Text = "A public dispute erupted between the parties"
                    },
                    new Fact
                    {
                        Id = "f_where",
                        Category = FactCategory.Where,
                        Fragility = 5,
                        Text = "The incident occurred in the local territory"
                    }
                }
            };

            var outcome = EventSubmissionValidator.Validate(submission, new PersistenceConfig(), _ => false);
            Assert.False(outcome.IsValid);
            Assert.NotNull(outcome.RejectionReason);
            Assert.Contains("Fact ID", outcome.RejectionReason!);
        }

        [Fact]
        public void DevTestSubmission_KnowingRoles_NeverNameThePlayer()
        {
            string playerHeroId = "hero_player";
            string conversationHeroId = "hero_npc";
            double day = 10.0;

            // 鏡像 DevEventFactory.CreatePublicTestEvent
            var publicSubmission = new EventSubmission
            {
                Type = "dev_test",
                Origin = EventOrigin.Public,
                Day = day,
                Participants = new Dictionary<string, string>
                {
                    ["mastermind"] = conversationHeroId,
                    ["target"] = playerHeroId
                },
                KnowingRoles = new HashSet<string> { "mastermind" },
                Facts = new List<Fact>
                {
                    new Fact { Id = "f_who", Category = FactCategory.Who, Fragility = 1, Text = $"Mastermind {conversationHeroId} targeted {playerHeroId}" },
                    new Fact { Id = "f_what", Category = FactCategory.What, Fragility = 3, Text = "A public dispute erupted between the parties" },
                    new Fact { Id = "f_where", Category = FactCategory.Where, Fragility = 5, Text = "The incident occurred in the local territory" }
                }
            };

            // 鏡像 DevEventFactory.CreateSecretTestEvent
            var secretSubmission = new EventSubmission
            {
                Type = "dev_test",
                Origin = EventOrigin.Secret,
                Day = day,
                Participants = new Dictionary<string, string>
                {
                    ["mastermind"] = conversationHeroId,
                    ["target"] = playerHeroId
                },
                KnowingRoles = new HashSet<string> { "mastermind" },
                Facts = new List<Fact>
                {
                    new Fact { Id = "f_who", Category = FactCategory.Who, Fragility = 1, Text = $"Mastermind {conversationHeroId} schemed secretly against {playerHeroId}" },
                    new Fact { Id = "f_what", Category = FactCategory.What, Fragility = 3, Text = "A clandestine conspiracy was set in motion" },
                    new Fact { Id = "f_where", Category = FactCategory.Where, Fragility = 5, Text = "The conspiracy was hatched in the shadows of the realm" }
                }
            };

            var submissions = new[] { publicSubmission, secretSubmission };
            foreach (var sub in submissions)
            {
                Assert.NotNull(sub.KnowingRoles);
                Assert.DoesNotContain(playerHeroId, sub.KnowingRoles);
                Assert.Contains("mastermind", sub.KnowingRoles);
                Assert.DoesNotContain("target", sub.KnowingRoles);
                foreach (var role in sub.KnowingRoles)
                {
                    Assert.True(sub.Participants.TryGetValue(role, out var boundHeroId));
                    Assert.NotEqual(playerHeroId, boundHeroId);
                    Assert.Equal(conversationHeroId, boundHeroId);
                }
            }
        }
    }
}
