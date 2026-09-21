using VividWorld.Core.Config;
using VividWorld.Core.Events;

namespace VividWorld.Core.Rumors
{
    public sealed class ThresholdRetentionPolicy : RetentionPolicyBase
    {
        public ThresholdRetentionPolicy(RetentionConfig cfg) : base(cfg)
        {
        }

        protected override bool Survives(WorldEvent evt, Fact fact, int hop, string tellerHeroId, int threshold)
        {
            return fact.Fragility <= threshold;
        }
    }
}
