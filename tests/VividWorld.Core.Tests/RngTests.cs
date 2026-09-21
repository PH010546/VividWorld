using System.Collections.Generic;
using VividWorld.Core.Util;
using Xunit;

namespace VividWorld.Core.Tests
{
    public class RngTests
    {
        [Fact]
        public void RumorSeed_IsStableAcrossRuns()
        {
            // 固定輸入：42L, "evt_0001_test", "fact_1", 2, "hero_1"
            // 字串化後為 "42|evt_0001_test|fact_1|2|hero_1"
            long seed = RumorSeed.Of(42L, "evt_0001_test", "fact_1", 2, "hero_1");
            
            // 驗證 FNV-1a 64 之位元組雜湊值跨執行期絕對穩定
            Assert.Equal(seed, RumorSeed.Of(42L, "evt_0001_test", "fact_1", 2, "hero_1"));
            // 釘住固定雜湊常數（依據 M2 卡 §7 指示：用寫死的期望值釘住雜湊實作）
            Assert.Equal(6139782599513296773L, seed);
        }

        [Fact]
        public void RumorSeed_IsOrderSensitive()
        {
            long seed1 = RumorSeed.Of(0L, "a", "b");
            long seed2 = RumorSeed.Of(0L, "b", "a");
            Assert.NotEqual(seed1, seed2);
        }

        [Fact]
        public void SplitMix64_IsStateless_SameSeedSameValue()
        {
            var rng1 = new SplitMix64Rng();
            var rng2 = new SplitMix64Rng();
            long seed = 123456789L;

            Assert.Equal(rng1.NextDouble(seed), rng2.NextDouble(seed));
            Assert.Equal(rng1.Chance(0.5, seed), rng2.Chance(0.5, seed));
            Assert.Equal(rng1.Pick(10, seed), rng2.Pick(10, seed));

            var weights = new double[] { 1.0, 2.0, 3.0 };
            Assert.Equal(rng1.PickWeighted(weights, seed), rng2.PickWeighted(weights, seed));
        }

        [Fact]
        public void SplitMix64_IsApproximatelyUniform()
        {
            var rng = new SplitMix64Rng();
            const int total = 100000;
            const int bucketCount = 10;
            int[] buckets = new int[bucketCount];

            for (int i = 0; i < total; i++)
            {
                int b = rng.Pick(bucketCount, i);
                buckets[b]++;
            }

            // 每桶期望值為 10,000 (10%)，規格要求落在 ±3%（即 7,000 ~ 13,000 或 7% ~ 13%）
            for (int b = 0; b < bucketCount; b++)
            {
                double prop = (double)buckets[b] / total;
                Assert.InRange(prop, 0.07, 0.13);
            }
        }

        [Fact]
        public void PickWeighted_RespectsWeightProportions()
        {
            var rng = new SplitMix64Rng();
            const int total = 100000;
            var weights = new double[] { 1.0, 3.0 }; // 期望比例 25% : 75%
            int count1 = 0;

            for (int i = 0; i < total; i++)
            {
                int picked = rng.PickWeighted(weights, i);
                if (picked == 1) count1++;
            }

            double prop1 = (double)count1 / total;
            // 規格要求：索引 1 的比例落在 74%–76%
            Assert.InRange(prop1, 0.74, 0.76);
        }

        [Fact]
        public void PickWeighted_ZeroTotalWeight_FallsBackToUniform()
        {
            var rng = new SplitMix64Rng();
            var zeroWeights = new double[] { 0.0, 0.0, 0.0 };
            var negativeWeights = new double[] { -1.0, 0.0, -2.0 };

            int[] zeroCounts = new int[3];
            int[] negCounts = new int[3];
            const int total = 30000;

            for (int i = 0; i < total; i++)
            {
                zeroCounts[rng.PickWeighted(zeroWeights, i)]++;
                negCounts[rng.PickWeighted(negativeWeights, i)]++;
            }

            // 應退回為均勻 Pick(3, seed)，每桶約 1/3 (33.3%)，至少全部都有被抽中
            for (int i = 0; i < 3; i++)
            {
                Assert.True(zeroCounts[i] > 8000, $"Zero weight index {i} got {zeroCounts[i]}");
                Assert.True(negCounts[i] > 8000, $"Negative weight index {i} got {negCounts[i]}");
            }
        }
    }
}
