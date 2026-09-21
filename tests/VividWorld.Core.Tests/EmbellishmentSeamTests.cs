using System.Collections.Generic;
using VividWorld.Core.Config;
using VividWorld.Core.Events;
using VividWorld.Core.Persistence;
using VividWorld.Core.Rumors;
using VividWorld.Core.Util;
using Xunit;

namespace VividWorld.Core.Tests
{
    public class EmbellishmentSeamTests
    {
        [Fact]
        public void NullEmbellishment_ReturnsRetainedUnchanged()
        {
            var evt = new WorldEvent
            {
                EventId = "evt_0001",
                Type = "duel",
                DramaWeight = 3
            };

            var retained = new List<Fact>
            {
                new Fact { Id = "f_1", Category = FactCategory.What, Fragility = 1, TextId = "txt_1" },
                new Fact { Id = "f_2", Category = FactCategory.Where, Fragility = 2, TextId = "txt_2" }
            };

            var teller = new TraitProfile
            {
                HeroId = "hero_teller",
                Honor = 1,
                Valor = 2
            };

            var result = NullEmbellishmentPolicy.Instance.Embellish(evt, retained, 1, teller);

            Assert.Same(retained, result);
        }

        [Fact]
        public void Retain_DoesNotMutateTheEvent()
        {
            var cfg = new RetentionConfig();
            var rng = new SplitMix64Rng();
            var thresholdPolicy = new ThresholdRetentionPolicy(cfg);
            var probPolicy = new ProbabilisticRetentionPolicy(cfg, rng, 12345L);

            var evt = new WorldEvent
            {
                EventId = "evt_0001_immutable",
                Type = "duel",
                DramaWeight = 3,
                Facts = new List<Fact>
                {
                    new Fact
                    {
                        Id = "f_1",
                        Category = FactCategory.Who,
                        Fragility = 1,
                        TextId = "txt_who",
                        Vars = new Dictionary<string, string> { { "subject", "hero:hero_1" } }
                    },
                    new Fact
                    {
                        Id = "f_2",
                        Category = FactCategory.What,
                        Fragility = 4,
                        TextId = "txt_what",
                        Vars = new Dictionary<string, string> { { "action", "text:fought" } }
                    }
                }
            };

            string beforeJson = VividJson.Write(evt);

            // 呼叫 Threshold policy
            thresholdPolicy.Retain(evt, 2, "teller_1");
            string afterThresholdJson = VividJson.Write(evt);
            Assert.Equal(beforeJson, afterThresholdJson);

            // 呼叫 Probabilistic policy
            probPolicy.Retain(evt, 2, "teller_2");
            string afterProbJson = VividJson.Write(evt);
            Assert.Equal(beforeJson, afterProbJson);
        }
    }
}
