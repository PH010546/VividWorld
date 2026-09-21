using System.Collections.Generic;
using System.Linq;
using VividWorld.Core.Config;
using VividWorld.Core.Events;
using VividWorld.Core.Rumors;
using VividWorld.Core.Util;
using Xunit;

namespace VividWorld.Core.Tests
{
    public class RetentionTests
    {
        private static WorldEvent CreateTestEvent(string eventId = "evt_0001_test", int drama = 3, params int[] fragilities)
        {
            var facts = new List<Fact>();
            for (int i = 0; i < fragilities.Length; i++)
            {
                facts.Add(new Fact
                {
                    Id = $"f_{i + 1}",
                    Category = FactCategory.What,
                    Fragility = fragilities[i],
                    TextId = $"txt_{i + 1}"
                });
            }

            return new WorldEvent
            {
                EventId = eventId,
                Type = "duel",
                DramaWeight = drama,
                Facts = facts
            };
        }

        [Fact]
        public void Threshold_Hop0_KeepsEveryFact()
        {
            // 這條刻意用出貨的預設曲線：hop 0 是當事人／目擊者，不管曲線怎麼調都必須什麼都記得。
            var cfg = new RetentionConfig();
            var policy = new ThresholdRetentionPolicy(cfg);
            var evt = CreateTestEvent("evt_1", 3, 1, 2, 3, 4, 5);

            var retained = policy.Retain(evt, 0, "teller_1");

            Assert.Equal(5, retained.Count);
            Assert.Equal(new[] { "f_1", "f_2", "f_3", "f_4", "f_5" }, retained.Select(f => f.Id));
        }

        [Fact]
        public void Threshold_Hop5_KeepsOnlyFragilityOne()
        {
            var cfg = new RetentionConfig(); // hop 5 threshold = 1
            var policy = new ThresholdRetentionPolicy(cfg);
            var evt = CreateTestEvent("evt_1", 3, 1, 2, 3, 4, 5);

            var retained = policy.Retain(evt, 5, "teller_1");

            Assert.Single(retained);
            Assert.Equal("f_1", retained[0].Id);
            Assert.Equal(1, retained[0].Fragility);
        }

        [Fact]
        public void Threshold_HopBeyondCurve_ClampsToLastThreshold()
        {
            var cfg = new RetentionConfig();
            var policy = new ThresholdRetentionPolicy(cfg);
            var evt = CreateTestEvent("evt_1", 3, 1, 2, 3, 4, 5);

            var hop5 = policy.Retain(evt, 5, "teller_1").Select(f => f.Id).ToList();
            var hop6 = policy.Retain(evt, 6, "teller_1").Select(f => f.Id).ToList();
            var hop10 = policy.Retain(evt, 10, "teller_1").Select(f => f.Id).ToList();
            var hop99 = policy.Retain(evt, 99, "teller_1").Select(f => f.Id).ToList();

            Assert.Equal(hop5, hop6);
            Assert.Equal(hop5, hop10);
            Assert.Equal(hop5, hop99);
        }

        [Fact]
        public void Threshold_BoundaryFragilityEqualsThreshold_IsKept()
        {
            // 門檻寫在測試裡，不借用出貨的曲線——這條測的是邊界是不是 <=（fragility == threshold 要留），
            // 跟曲線調成什麼樣無關。借用預設值會讓調校動到這條的意圖（2026-09-09 就發生過）。
            var cfg = new RetentionConfig { HopThresholds = new[] { 5, 4, 3, 3, 2, 1 } }; // hop 3 threshold = 3
            var policy = new ThresholdRetentionPolicy(cfg);
            var evt = CreateTestEvent("evt_1", 3, 2, 3, 4);

            var retained = policy.Retain(evt, 3, "teller_1");

            Assert.Equal(2, retained.Count);
            Assert.Contains(retained, f => f.Fragility == 3);
            Assert.DoesNotContain(retained, f => f.Fragility == 4);
        }

        [Fact]
        public void Threshold_PreservesAuthoredFactOrder()
        {
            // 同上：門檻寫死在測試裡，這條測的是「保留原始撰寫順序」，與曲線調校無關。
            var cfg = new RetentionConfig { HopThresholds = new[] { 5, 4, 3, 3, 2, 1 } }; // hop 3 threshold = 3
            var policy = new ThresholdRetentionPolicy(cfg);

            // 原始撰寫順序：f1(5), f2(2), f3(1), f4(3), f5(4)
            var evt = new WorldEvent
            {
                EventId = "evt_order_1",
                Type = "duel",
                DramaWeight = 3,
                Facts = new List<Fact>
                {
                    new Fact { Id = "f_1", Fragility = 5 },
                    new Fact { Id = "f_2", Fragility = 2 },
                    new Fact { Id = "f_3", Fragility = 1 },
                    new Fact { Id = "f_4", Fragility = 3 },
                    new Fact { Id = "f_5", Fragility = 4 }
                }
            };

            var retained = policy.Retain(evt, 3, "teller_1");
            Assert.Equal(new[] { "f_2", "f_3", "f_4" }, retained.Select(f => f.Id));

            // 測試 MinFactsRetained 補回時依然維持原始順序
            // 原始順序：f1(5), f2(4), f3(3), f4(5)。hop 5 threshold = 1。
            // 沒有任何碎片 <= 1。MinFactsRetained = 2。
            // 補回挑選最低 fragility: f3(3) 與 f2(4)。
            // 回傳順序必須是 f2(原本索引1) 先於 f3(原本索引2)。
            var cfgFallback = new RetentionConfig { MinFactsRetained = 2 };
            var policyFallback = new ThresholdRetentionPolicy(cfgFallback);
            var evtFallback = new WorldEvent
            {
                EventId = "evt_order_fallback",
                Type = "duel",
                DramaWeight = 3,
                Facts = new List<Fact>
                {
                    new Fact { Id = "f_1", Fragility = 5 },
                    new Fact { Id = "f_2", Fragility = 4 },
                    new Fact { Id = "f_3", Fragility = 3 },
                    new Fact { Id = "f_4", Fragility = 5 }
                }
            };

            var retainedFallback = policyFallback.Retain(evtFallback, 5, "teller_1");
            Assert.Equal(new[] { "f_2", "f_3" }, retainedFallback.Select(f => f.Id));
        }

        [Fact]
        public void Retention_NeverReturnsEmpty_KeepsLowestFragility()
        {
            var cfg = new RetentionConfig { MinFactsRetained = 1 };
            var policy = new ThresholdRetentionPolicy(cfg);
            // Fragility 都高於 threshold 1
            var evt = CreateTestEvent("evt_no_keep", 3, 4, 5, 3);

            var retained = policy.Retain(evt, 5, "teller_1");

            Assert.Single(retained);
            Assert.Equal("f_3", retained[0].Id);
            Assert.Equal(3, retained[0].Fragility);
        }

        [Fact]
        public void Retention_NeverDropsFragilityAtOrBelowAlwaysKeep()
        {
            var cfg = new RetentionConfig
            {
                AlwaysKeepAtOrBelowFragility = 2
            };
            var policy = new ThresholdRetentionPolicy(cfg);
            var evt = CreateTestEvent("evt_always", 3, 1, 2, 3, 4);

            // 即使在 hop 99 (門檻 1)，fragility <= 2 之碎片也絕不丟棄
            var retained = policy.Retain(evt, 99, "teller_1");

            Assert.Equal(2, retained.Count);
            Assert.Equal(new[] { "f_1", "f_2" }, retained.Select(f => f.Id));
        }

        [Fact]
        public void Probabilistic_WithTinySoftness_MatchesThresholdPolicyExactly()
        {
            var threshCfg = new RetentionConfig();
            var probCfg = new RetentionConfig { Softness = 1e-6 };
            var rng = new SplitMix64Rng();
            const long campaignSeed = 987654321L;

            var threshPolicy = new ThresholdRetentionPolicy(threshCfg);
            var probPolicy = new ProbabilisticRetentionPolicy(probCfg, rng, campaignSeed);

            // 對 hop 0..8 × drama 1..5 × fragility 1..5 的完整碎片集，逐一比對兩個策略的輸出
            for (int hop = 0; hop <= 8; hop++)
            {
                for (int drama = 1; drama <= 5; drama++)
                {
                    var evt = CreateTestEvent($"evt_h{hop}_d{drama}", drama, 1, 2, 3, 4, 5);
                    var tResult = threshPolicy.Retain(evt, hop, "teller_match");
                    var pResult = probPolicy.Retain(evt, hop, "teller_match");

                    Assert.Equal(tResult.Select(f => f.Id), pResult.Select(f => f.Id));
                }
            }
        }

        [Fact]
        public void Probabilistic_SameTellerSameHop_IsStable()
        {
            var cfg = new RetentionConfig { Softness = 0.35 };
            var rng = new SplitMix64Rng();
            const long campaignSeed = 42L;
            var policy = new ProbabilisticRetentionPolicy(cfg, rng, campaignSeed);
            var evt = CreateTestEvent("evt_stable", 3, 1, 2, 3, 4, 5);

            var first = policy.Retain(evt, 2, "teller_alpha").Select(f => f.Id).ToList();

            for (int i = 0; i < 10; i++)
            {
                var run = policy.Retain(evt, 2, "teller_alpha").Select(f => f.Id).ToList();
                Assert.Equal(first, run);
            }
        }

        [Fact]
        public void Probabilistic_DifferentTellersSameHop_ProduceDifferentFactSets()
        {
            var cfg = new RetentionConfig { Softness = 0.35 };
            var rng = new SplitMix64Rng();
            const long campaignSeed = 1001L;
            var policy = new ProbabilisticRetentionPolicy(cfg, rng, campaignSeed);

            // hop 2 threshold = 4。放置多個脆弱度 4 (80.7% 機率) 與 5 (19.3% 機率) 的碎片
            var evt = CreateTestEvent("evt_diff_tellers", 3, 3, 4, 5, 4, 5, 4, 5, 4, 4, 5);

            bool foundDifference = false;
            var baseSet = policy.Retain(evt, 2, "teller_0").Select(f => f.Id).ToList();

            for (int t = 1; t <= 20; t++)
            {
                var otherSet = policy.Retain(evt, 2, $"teller_{t}").Select(f => f.Id).ToList();
                if (!baseSet.SequenceEqual(otherSet))
                {
                    foundDifference = true;
                    break;
                }
            }

            Assert.True(foundDifference, "Expected probabilistic policy to yield different fact sets for different tellers at same hop.");
        }

        [Fact]
        public void DramaThresholdShift_AllZeros_IsANoOp()
        {
            var cfg = new RetentionConfig
            {
                DramaThresholdShift = new[] { 0, 0, 0, 0, 0 }
            };
            var policy = new ThresholdRetentionPolicy(cfg);
            var evtBase = CreateTestEvent("evt_drama_base", 1, 1, 2, 3, 4, 5);

            for (int hop = 0; hop <= 5; hop++)
            {
                var baseIds = policy.Retain(evtBase, hop, "teller_1").Select(f => f.Id).ToList();

                for (int drama = 2; drama <= 5; drama++)
                {
                    var evtDrama = CreateTestEvent("evt_drama_base", drama, 1, 2, 3, 4, 5);
                    var dramaIds = policy.Retain(evtDrama, hop, "teller_1").Select(f => f.Id).ToList();
                    Assert.Equal(baseIds, dramaIds);
                }
            }
        }

        [Fact]
        public void FactRetentionPolicies_Create_DispatchesOnConfigString()
        {
            var rng = new SplitMix64Rng();
            const long seed = 42L;

            var cfgThreshold = new VividWorldConfig
            {
                Retention = new RetentionConfig { Policy = "Threshold" }
            };
            var policy1 = FactRetentionPolicies.Create(cfgThreshold, rng, seed);
            Assert.IsType<ThresholdRetentionPolicy>(policy1);

            var cfgProb = new VividWorldConfig
            {
                Retention = new RetentionConfig { Policy = "Probabilistic" }
            };
            var policy2 = FactRetentionPolicies.Create(cfgProb, rng, seed);
            Assert.IsType<ProbabilisticRetentionPolicy>(policy2);

            var cfgUnknown = new VividWorldConfig
            {
                Retention = new RetentionConfig { Policy = "SomethingElse" }
            };
            var policy3 = FactRetentionPolicies.Create(cfgUnknown, rng, seed);
            Assert.IsType<ThresholdRetentionPolicy>(policy3);
        }
    }
}
