using System;
using VividWorld.Core.Channels;
using VividWorld.Core.Config;

namespace VividWorld.Core.Rumors
{
    public static class RelationFactor
    {
        public static double For(ChannelKind kind, int relation, RelationConfig cfg)
        {
            if (cfg == null) throw new ArgumentNullException(nameof(cfg));

            double scale = cfg.ScaleOverride > 0 ? cfg.ScaleOverride : (cfg.EffectiveScale > 0 ? cfg.EffectiveScale : 100.0);
            double r = Math.Max(-1.0, Math.Min(1.0, relation / scale));

            if (ChannelClass.IsInPerson(kind))
            {
                double weight = r >= 0 ? cfg.MeetPositiveWeight : cfg.MeetNegativeWeight;
                double factor = 1.0 + weight * r;
                return Math.Max(cfg.MeetFactorMin, Math.Min(cfg.MeetFactorMax, factor));
            }
            else
            {
                double factor = cfg.RemoteBaseFactor + cfg.RemoteRelationWeight * r;
                return Math.Max(0.0, Math.Min(cfg.RemoteFactorMax, factor));
            }
        }
    }
}
