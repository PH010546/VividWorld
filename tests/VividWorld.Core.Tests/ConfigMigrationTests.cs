using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using VividWorld.Core.Config;
using VividWorld.Core.Dialogue;
using VividWorld.Core.Events;
using VividWorld.Core.Feelings;
using VividWorld.Core.Persistence;
using VividWorld.Core.Rumors;
using VividWorld.Core.Tests.Fakes;
using VividWorld.Core.Util;
using Xunit;

namespace VividWorld.Core.Tests
{
    public class ConfigMigrationTests
    {
        [Fact]
        public void Migrate_OldDefaults_ReplacedWithNewDefaults_AndVersionUpgraded()
        {
            var json = JObject.Parse(@"{
                ""configVersion"": 1,
                ""presentation"": {
                    ""feelings"": {
                        ""affectionHigh"": 30,
                        ""affectionLow"": -30
                    }
                }
            }");

            var result = ConfigMigration.Migrate(json, targetVersion: 2);

            Assert.True(result.HasChanges);
            Assert.Equal(1, result.InitialVersion);
            Assert.Equal(2, result.ResultVersion);
            Assert.Equal(2, (int)json["configVersion"]!);
            Assert.Equal(50, (int)json["presentation"]!["feelings"]!["affectionHigh"]!);
            Assert.Equal(-50, (int)json["presentation"]!["feelings"]!["affectionLow"]!);

            Assert.Equal(2, result.Changes.Count);
            Assert.True(result.Changes[0].Migrated);
            Assert.Equal(30, result.Changes[0].OldValue);
            Assert.Equal(50, result.Changes[0].NewValue);
            Assert.True(result.Changes[1].Migrated);
            Assert.Equal(-30, result.Changes[1].OldValue);
            Assert.Equal(-50, result.Changes[1].NewValue);

            Assert.Equal(2, result.LogLines.Count);
            Assert.Equal("config: migrated presentation.feelings.affectionHigh 30 -> 50 (configVersion 1 -> 2)", result.LogLines[0]);
            Assert.Equal("config: migrated presentation.feelings.affectionLow -30 -> -50 (configVersion 1 -> 2)", result.LogLines[1]);
        }

        [Fact]
        public void Migrate_CustomValues_KeptUntouched()
        {
            var json = JObject.Parse(@"{
                ""configVersion"": 1,
                ""presentation"": {
                    ""feelings"": {
                        ""affectionHigh"": 40,
                        ""affectionLow"": -40
                    }
                }
            }");

            var result = ConfigMigration.Migrate(json, targetVersion: 2);

            Assert.True(result.HasChanges);
            Assert.Equal(2, (int)json["configVersion"]!);
            Assert.Equal(40, (int)json["presentation"]!["feelings"]!["affectionHigh"]!);
            Assert.Equal(-40, (int)json["presentation"]!["feelings"]!["affectionLow"]!);

            Assert.Equal(2, result.Changes.Count);
            Assert.False(result.Changes[0].Migrated);
            Assert.Equal(40, result.Changes[0].OldValue);
            Assert.Equal(40, result.Changes[0].NewValue);
            Assert.False(result.Changes[1].Migrated);
            Assert.Equal(-40, result.Changes[1].OldValue);
            Assert.Equal(-40, result.Changes[1].NewValue);

            Assert.Equal(2, result.LogLines.Count);
            Assert.Equal("config: kept presentation.feelings.affectionHigh = 40 (not the old default 30)", result.LogLines[0]);
            Assert.Equal("config: kept presentation.feelings.affectionLow = -40 (not the old default -30)", result.LogLines[1]);
        }

        [Fact]
        public void Migrate_MissingConfigVersion_TreatedAsVersion1()
        {
            var json = JObject.Parse(@"{
                ""presentation"": {
                    ""feelings"": {
                        ""affectionHigh"": 30
                    }
                }
            }");

            var result = ConfigMigration.Migrate(json, targetVersion: 2);

            Assert.True(result.HasChanges);
            Assert.Equal(1, result.InitialVersion);
            Assert.Equal(2, result.ResultVersion);
            Assert.Equal(2, (int)json["configVersion"]!);
            Assert.Equal(50, (int)json["presentation"]!["feelings"]!["affectionHigh"]!);

            Assert.Single(result.Changes);
            Assert.True(result.Changes[0].Migrated);
            Assert.Equal("config: migrated presentation.feelings.affectionHigh 30 -> 50 (configVersion 1 -> 2)", result.LogLines[0]);
        }

        [Fact]
        public void Migrate_ConfigVersion2OrHigher_NoMigrationNeeded()
        {
            var json = JObject.Parse(@"{
                ""configVersion"": 2,
                ""presentation"": {
                    ""feelings"": {
                        ""affectionHigh"": 30,
                        ""affectionLow"": -30
                    }
                }
            }");

            var result = ConfigMigration.Migrate(json, targetVersion: 2);

            Assert.False(result.HasChanges);
            Assert.Equal(2, (int)json["configVersion"]!);
            Assert.Equal(30, (int)json["presentation"]!["feelings"]!["affectionHigh"]!);
            Assert.Equal(-30, (int)json["presentation"]!["feelings"]!["affectionLow"]!);
            Assert.Empty(result.Changes);

            Assert.Single(result.LogLines);
            Assert.Equal("config: no migration needed (configVersion 2)", result.LogLines[0]);
        }

        [Fact]
        public void Migrate_RunTwice_ProducesIdempotentResult()
        {
            var json = JObject.Parse(@"{
                ""configVersion"": 1,
                ""presentation"": {
                    ""feelings"": {
                        ""affectionHigh"": 30,
                        ""affectionLow"": -30
                    }
                }
            }");

            var first = ConfigMigration.Migrate(json, targetVersion: 2);
            Assert.True(first.HasChanges);
            Assert.Equal(2, (int)json["configVersion"]!);
            Assert.Equal(50, (int)json["presentation"]!["feelings"]!["affectionHigh"]!);

            var second = ConfigMigration.Migrate(json, targetVersion: 2);
            Assert.False(second.HasChanges);
            Assert.Equal(2, (int)json["configVersion"]!);
            Assert.Equal(50, (int)json["presentation"]!["feelings"]!["affectionHigh"]!);
            Assert.Equal(-50, (int)json["presentation"]!["feelings"]!["affectionLow"]!);
            Assert.Empty(second.Changes);
            Assert.Single(second.LogLines);
            Assert.Equal("config: no migration needed (configVersion 2)", second.LogLines[0]);
        }

        [Fact]
        public void Migrate_NeitherKeyExists_OnlyUpgradesVersion()
        {
            var json = JObject.Parse(@"{
                ""configVersion"": 1,
                ""enabled"": true
            }");

            var result = ConfigMigration.Migrate(json, targetVersion: 2);

            Assert.True(result.HasChanges);
            Assert.Equal(2, (int)json["configVersion"]!);
            Assert.Null(json["presentation"]);
            Assert.Empty(result.Changes);
            Assert.Empty(result.LogLines);
        }

        [Theory]
        [InlineData(50, AffectionLevel.Neutral)]
        [InlineData(51, AffectionLevel.High)]
        [InlineData(-50, AffectionLevel.Neutral)]
        [InlineData(-51, AffectionLevel.Low)]
        public void FeelingGrid_Boundaries_StrictGreaterThan50AndLessThanMinus50(int affection, AffectionLevel expected)
        {
            var cfg = new FeelingsConfig(); // Default: AffectionHigh = 50, AffectionLow = -50
            Assert.Equal(expected, FeelingGrid.Classify(affection, cfg));
        }

        [Theory]
        [InlineData(50, ShamefulListenerCol.Unrelated)]
        [InlineData(51, ShamefulListenerCol.Friend)]
        [InlineData(-50, ShamefulListenerCol.Unrelated)]
        [InlineData(-51, ShamefulListenerCol.Enemy)]
        public void ShamefulNews_ListenerCol_Boundaries_MatchStrict50AndMinus50(int affection, ShamefulListenerCol expected)
        {
            var world = new FakeDialogueWorld();
            world.SetAffection("listener", "shamed", affection);
            var cfg = new VividWorldConfig(); // Default: 50, -50

            var col = ShamefulNewsRule.ClassifyListenerCol("listener", "shamed", world, cfg);
            Assert.Equal(expected, col);
        }

        [Theory]
        [InlineData(50, false)]
        [InlineData(51, true)]
        [InlineData(-50, false)]
        [InlineData(-51, true)]
        public void RumorOfferSelector_VolunteerTellerRelation_Boundaries_MatchStrict50AndMinus50(int affection, bool expectedRelated)
        {
            var world = new FakeDialogueWorld();
            world.SetAffection("teller", "participant", affection);

            var config = new VividWorldConfig();
            var rng = new SplitMix64Rng();
            var channel = new FakePropagationChannel();
            var traits = new FakeHeroTraitLookup();
            var retention = FactRetentionPolicies.Create(config, rng, 42L);
            var embellishment = NullEmbellishmentPolicy.Instance;
            var engine = new RumorEngine(config, retention, embellishment, channel, traits, rng, 42L, "player");

            var selector = new RumorOfferSelector(config, engine, "player", RumorMode.Casual, dialogueWorld: world);

            var teller = new HeroSocialProfile { HeroId = "teller", RelationWithPlayer = 20 };
            var evt = new WorldEvent
            {
                EventId = "evt1",
                Type = "test_event",
                Day = 10.0,
                Origin = EventOrigin.Public,
                Participants = new Dictionary<string, string>
                {
                    ["actor"] = "participant"
                }
            };

            bool related = selector.IsCloselyRelated(teller, evt, day: 10.0, out var cat, out _);
            Assert.Equal(expectedRelated, related);
            if (expectedRelated)
            {
                Assert.Equal(VolunteerReasonCategory.TellerRelation, cat);
            }
        }
    }
}
