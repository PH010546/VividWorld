using System;
using System.Collections.Generic;

namespace VividWorld.Core.Util
{
    public sealed class SplitMix64Rng : IDeterministicRng
    {
        private static ulong Mix(long seed)
        {
            unchecked
            {
                ulong z = (ulong)seed + 0x9E3779B97F4A7C15UL;
                z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
                z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
                return z ^ (z >> 31);
            }
        }

        public double NextDouble(long seed)
        {
            ulong z = Mix(seed);
            return (z >> 11) * (1.0 / (1UL << 53));
        }

        public bool Chance(double p, long seed)
        {
            if (p <= 0.0) return false;
            if (p >= 1.0) return true;
            return NextDouble(seed) < p;
        }

        public int Pick(int count, long seed)
        {
            if (count <= 0) return 0;
            int idx = (int)(NextDouble(seed) * count);
            return idx >= count ? count - 1 : (idx < 0 ? 0 : idx);
        }

        public int PickWeighted(IReadOnlyList<double> weights, long seed)
        {
            if (weights == null || weights.Count == 0) return 0;
            int n = weights.Count;
            double s = 0.0;
            for (int i = 0; i < n; i++)
            {
                double w = weights[i];
                if (w > 0.0) s += w;
            }

            if (s <= 0.0)
            {
                return Pick(n, seed);
            }

            double r = NextDouble(seed) * s;
            double acc = 0.0;
            for (int i = 0; i < n; i++)
            {
                double w = weights[i];
                if (w > 0.0)
                {
                    acc += w;
                    if (acc > r)
                    {
                        return i;
                    }
                }
            }

            return n - 1;
        }
    }
}
