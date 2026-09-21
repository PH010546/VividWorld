using System;
using VividWorld.Core.Config;
using VividWorld.Core.Events;
using VividWorld.Core.Util;

namespace VividWorld.Core.Rumors
{
    public sealed class ProbabilisticRetentionPolicy : RetentionPolicyBase
    {
        private readonly IDeterministicRng _rng;
        private readonly long _campaignSeed;

        public ProbabilisticRetentionPolicy(RetentionConfig cfg,
                                            IDeterministicRng rng, long campaignSeed)
            : base(cfg)
        {
            _rng = rng ?? throw new ArgumentNullException(nameof(rng));
            _campaignSeed = campaignSeed;
        }

        protected override bool Survives(WorldEvent evt, Fact fact, int hop, string tellerHeroId, int threshold)
        {
            double x = (threshold - fact.Fragility + 0.5) / _cfg.Softness;
            double p = 1.0 / (1.0 + Math.Exp(-x));
            long seed = RumorSeed.Of(_campaignSeed, evt.EventId, fact.Id, hop, tellerHeroId);
            return _rng.Chance(p, seed);
        }
    }
}
