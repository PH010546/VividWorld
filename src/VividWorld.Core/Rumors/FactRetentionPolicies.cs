using System;
using VividWorld.Core.Config;
using VividWorld.Core.Util;

namespace VividWorld.Core.Rumors
{
    public static class FactRetentionPolicies
    {
        public static IFactRetentionPolicy Create(VividWorldConfig cfg, IDeterministicRng rng, long campaignSeed)
        {
            if (cfg == null) throw new ArgumentNullException(nameof(cfg));
            var retentionCfg = cfg.Retention ?? new RetentionConfig();

            if (string.Equals(retentionCfg.Policy, "Probabilistic", StringComparison.OrdinalIgnoreCase))
            {
                return new ProbabilisticRetentionPolicy(retentionCfg, rng, campaignSeed);
            }

            return new ThresholdRetentionPolicy(retentionCfg);
        }
    }
}
