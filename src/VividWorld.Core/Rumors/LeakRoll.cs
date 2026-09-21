using System;
using System.Collections.Generic;
using VividWorld.Core.Config;
using VividWorld.Core.Events;
using VividWorld.Core.Util;

namespace VividWorld.Core.Rumors
{
    public static class LeakRoll
    {
        public static LeakOutcome Roll(WorldEvent evt, double day, IReadOnlyList<TraitProfile> insiders,
                                       LeakConfig cfg, IDeterministicRng rng, long campaignSeed)
        {
            if (evt == null || evt.State.Leaked || evt.Origin != EventOrigin.Secret)
            {
                return LeakOutcome.None;
            }

            if (cfg == null || rng == null || insiders == null || insiders.Count == 0)
            {
                return LeakOutcome.None;
            }

            if (day - evt.Day < cfg.GraceDays)
            {
                return LeakOutcome.None;
            }

            double d = Math.Pow(2.0, -(day - evt.Day) / cfg.ChanceDecayHalfLifeDays);

            // 「穩定順序」的定義：依 insiders 傳入的順序，不要排序。呼叫端負責給穩定順序。
            // 嚴禁在這裡 OrderBy(HeroId)——那會讓「第一個成功者」的結果依賴 id 的字典序而非傳入意圖，
            // 且與呼叫端的認知不一致。
            for (int i = 0; i < insiders.Count; i++)
            {
                var insider = insiders[i];
                if (insider == null || string.IsNullOrEmpty(insider.HeroId))
                {
                    continue;
                }

                var w = cfg.TraitWeights;
                double rawMultiplier = 1.0
                    + (w.Honor * insider.Honor)
                    + (w.Calculating * insider.Calculating)
                    + (w.Generosity * insider.Generosity);

                double m = rawMultiplier;
                if (m < cfg.MultiplierMin) m = cfg.MultiplierMin;
                if (m > cfg.MultiplierMax) m = cfg.MultiplierMax;

                double p = cfg.BaseChancePerInsiderPerDay * d * m;
                long seed = RumorSeed.Of(campaignSeed, evt.EventId, insider.HeroId, "leak", RumorSeed.DayBucket(day));

                if (rng.Chance(p, seed))
                {
                    return LeakOutcome.By(insider.HeroId);
                }
            }

            return LeakOutcome.None;
        }
    }
}
