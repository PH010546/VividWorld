using System;
using VividWorld.Core.Util;

namespace VividWorld.Core.Situations
{
    public sealed class QuotaRoll
    {
        public int Quota;
        public int Guaranteed;
        public double MaxPerDay;
        public double Fraction;
        public double RollValue;
        public bool FractionWon;
        public bool Clamped;
    }

    public static class SituationQuota
    {
        public const int HardCap = 20;

        public static QuotaRoll Roll(double maxPerDay, IDeterministicRng rng, long seed)
        {
            if (maxPerDay <= 0.0)
            {
                return new QuotaRoll
                {
                    Quota = 0,
                    Guaranteed = 0,
                    MaxPerDay = 0.0,
                    Fraction = 0.0,
                    RollValue = 0.0,
                    FractionWon = false,
                    Clamped = false
                };
            }

            int guaranteed = (int)Math.Floor(maxPerDay);
            double fraction = maxPerDay - guaranteed;
            double rollValue = 0.0;
            bool fractionWon = false;

            if (fraction > 1e-9)
            {
                rollValue = rng.NextDouble(seed);
                fractionWon = rollValue < fraction;
            }

            int rawQuota = guaranteed + (fractionWon ? 1 : 0);
            bool clamped = rawQuota > HardCap;
            int quota = clamped ? HardCap : rawQuota;

            return new QuotaRoll
            {
                Quota = quota,
                Guaranteed = guaranteed,
                MaxPerDay = maxPerDay,
                Fraction = fraction,
                RollValue = rollValue,
                FractionWon = fractionWon,
                Clamped = clamped
            };
        }
    }
}
