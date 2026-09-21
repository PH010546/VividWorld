using System;
using System.Collections.Generic;
using VividWorld.Core.Config;
using VividWorld.Core.Events;

namespace VividWorld.Core.Rumors
{
    public abstract class RetentionPolicyBase : IFactRetentionPolicy
    {
        protected readonly RetentionConfig _cfg;

        protected RetentionPolicyBase(RetentionConfig cfg)
        {
            _cfg = cfg ?? throw new ArgumentNullException(nameof(cfg));
        }

        public IReadOnlyList<Fact> Retain(WorldEvent evt, int hop, string tellerHeroId)
        {
            if (evt == null || evt.Facts == null || evt.Facts.Count == 0)
            {
                return Array.Empty<Fact>();
            }

            int threshold = ThresholdAt(hop, evt.DramaWeight);
            int count = evt.Facts.Count;
            bool[] keep = new bool[count];
            int keptCount = 0;

            for (int i = 0; i < count; i++)
            {
                var fact = evt.Facts[i];
                if (fact.Fragility <= _cfg.AlwaysKeepAtOrBelowFragility)
                {
                    keep[i] = true;
                    keptCount++;
                }
                else if (Survives(evt, fact, hop, tellerHeroId, threshold))
                {
                    keep[i] = true;
                    keptCount++;
                }
            }

            if (keptCount < _cfg.MinFactsRetained && keptCount < count)
            {
                var candidates = new List<(int index, int fragility)>();
                for (int i = 0; i < count; i++)
                {
                    if (!keep[i])
                    {
                        candidates.Add((i, evt.Facts[i].Fragility));
                    }
                }

                candidates.Sort((a, b) =>
                {
                    int cmp = a.fragility.CompareTo(b.fragility);
                    if (cmp != 0) return cmp;
                    return a.index.CompareTo(b.index);
                });

                int needed = Math.Min(_cfg.MinFactsRetained - keptCount, candidates.Count);
                for (int k = 0; k < needed; k++)
                {
                    keep[candidates[k].index] = true;
                    keptCount++;
                }
            }

            var result = new List<Fact>(keptCount);
            for (int i = 0; i < count; i++)
            {
                if (keep[i])
                {
                    result.Add(evt.Facts[i]);
                }
            }

            return result;
        }

        /// <summary>子類別只需要回答「這個碎片活不活」。base 負責兩條不變式與順序。</summary>
        protected abstract bool Survives(WorldEvent evt, Fact fact, int hop, string tellerHeroId, int threshold);

        protected int ThresholdAt(int hop, int drama)
        {
            int hopLen = _cfg.HopThresholds != null && _cfg.HopThresholds.Length > 0 ? _cfg.HopThresholds.Length : 1;
            int h = Math.Max(0, Math.Min(hop, hopLen - 1));
            int hopVal = _cfg.HopThresholds != null && _cfg.HopThresholds.Length > 0 ? _cfg.HopThresholds[h] : 0;

            int dramaLen = _cfg.DramaThresholdShift != null && _cfg.DramaThresholdShift.Length > 0 ? _cfg.DramaThresholdShift.Length : 1;
            int d = Math.Max(1, Math.Min(drama, dramaLen)) - 1;
            int dramaVal = _cfg.DramaThresholdShift != null && _cfg.DramaThresholdShift.Length > 0 ? _cfg.DramaThresholdShift[d] : 0;

            return hopVal + dramaVal;
        }
    }
}
