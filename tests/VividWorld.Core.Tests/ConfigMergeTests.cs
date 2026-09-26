using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using VividWorld.Core.Config;
using VividWorld.Core.Persistence;
using Xunit;

namespace VividWorld.Core.Tests
{
    public class ConfigMergeTests
    {
        [Fact]
        public void Merge_AddsMissingKeys_AndReportsTheirPaths()
        {
            var existingJson = @"{
                ""configVersion"": 1,
                ""enabled"": true,
                ""propagation"": {
                    ""baseTellChancePerContact"": 0.18
                },
                ""debug"": {
                    ""fakeProducerEnabled"": false
                }
            }";

            var canonicalJson = @"{
                ""configVersion"": 1,
                ""enabled"": true,
                ""propagation"": {
                    ""baseTellChancePerContact"": 0.18,
                    ""maxRemoteContacts"": 2
                },
                ""debug"": {
                    ""fakeProducerEnabled"": false,
                    ""metricsEnabled"": false
                }
            }";

            var existing = JObject.Parse(existingJson);
            var canonical = JObject.Parse(canonicalJson);

            var result = ConfigMerge.AddMissingKeys(existing, canonical);

            Assert.Equal(2, result.AddedPaths.Count);
            Assert.Contains("propagation.maxRemoteContacts", result.AddedPaths);
            Assert.Contains("debug.metricsEnabled", result.AddedPaths);

            Assert.NotNull(result.Merged["propagation"]);
            Assert.Equal(2, (int)result.Merged["propagation"]!["maxRemoteContacts"]!);
            Assert.NotNull(result.Merged["debug"]);
            Assert.False((bool)result.Merged["debug"]!["metricsEnabled"]!);

            Assert.Null(existing["propagation"]!["maxRemoteContacts"]);
            Assert.Null(existing["debug"]!["metricsEnabled"]);
        }

        [Fact]
        public void Merge_NeverChangesAnExistingValue()
        {
            var existingJson = @"{
                ""propagation"": {
                    ""baseTellChancePerContact"": 0.9,
                    ""channelWeights"": {
                        ""sameSettlement"": 0.42
                    }
                }
            }";

            var canonicalJson = @"{
                ""propagation"": {
                    ""baseTellChancePerContact"": 0.18,
                    ""maxRemoteContacts"": 2,
                    ""channelWeights"": {
                        ""sameSettlement"": 0.80,
                        ""sameParty"": 1.25
                    }
                },
                ""debug"": {
                    ""metricsEnabled"": false
                }
            }";

            var existing = JObject.Parse(existingJson);
            var canonical = JObject.Parse(canonicalJson);

            var result = ConfigMerge.AddMissingKeys(existing, canonical);

            Assert.NotNull(result.Merged["propagation"]);
            Assert.Equal(0.9, (double)result.Merged["propagation"]!["baseTellChancePerContact"]!);
            Assert.NotNull(result.Merged["propagation"]!["channelWeights"]);
            Assert.Equal(0.42, (double)result.Merged["propagation"]!["channelWeights"]!["sameSettlement"]!);

            Assert.Equal(2, (int)result.Merged["propagation"]!["maxRemoteContacts"]!);
            Assert.Equal(1.25, (double)result.Merged["propagation"]!["channelWeights"]!["sameParty"]!);
            Assert.NotNull(result.Merged["debug"]);
            Assert.False((bool)result.Merged["debug"]!["metricsEnabled"]!);

            Assert.DoesNotContain("propagation.baseTellChancePerContact", result.AddedPaths);
            Assert.DoesNotContain("propagation.channelWeights.sameSettlement", result.AddedPaths);
        }

        [Fact]
        public void Merge_PreservesUnknownKeys()
        {
            var existingJson = @"{
                ""futureTopLevel"": ""future_val"",
                ""debug"": {
                    ""fakeProducerEnabled"": false,
                    ""somethingFromTheFuture"": 42
                }
            }";

            var canonicalJson = @"{
                ""debug"": {
                    ""fakeProducerEnabled"": false,
                    ""metricsEnabled"": false
                }
            }";

            var existing = JObject.Parse(existingJson);
            var canonical = JObject.Parse(canonicalJson);

            var result = ConfigMerge.AddMissingKeys(existing, canonical);

            Assert.NotNull(result.Merged["futureTopLevel"]);
            Assert.Equal("future_val", (string)result.Merged["futureTopLevel"]!);
            Assert.NotNull(result.Merged["debug"]);
            Assert.Equal(42, (int)result.Merged["debug"]!["somethingFromTheFuture"]!);

            Assert.False((bool)result.Merged["debug"]!["metricsEnabled"]!);
            Assert.Contains("debug.metricsEnabled", result.AddedPaths);
            Assert.DoesNotContain("debug.somethingFromTheFuture", result.AddedPaths);
            Assert.DoesNotContain("futureTopLevel", result.AddedPaths);
        }

        [Fact]
        public void Merge_AddsPersistencePurgeForgottenEvents_PreservingExistingPersistenceValues()
        {
            var existingJson = @"{
                ""persistence"": {
                    ""shardDays"": 150
                }
            }";

            var canonical = JObject.Parse(VividJson.Write(new VividWorldConfig()));

            var existing = JObject.Parse(existingJson);
            var result = ConfigMerge.AddMissingKeys(existing, canonical);

            Assert.Contains("persistence.purgeForgottenEvents", result.AddedPaths);
            Assert.NotNull(result.Merged["persistence"]);
            Assert.Equal(150, (int)result.Merged["persistence"]!["shardDays"]!);
            Assert.True((bool)result.Merged["persistence"]!["purgeForgottenEvents"]!);
        }
    }
}
