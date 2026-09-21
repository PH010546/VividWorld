using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using VividWorld.Core.Config;
using VividWorld.Core.Events;
using VividWorld.Core.Persistence;
using VividWorld.Core.Rumors;

namespace VividWorld.Core.Grudges
{
    public static class GrudgeDecay
    {
        public static GrudgeReplayResult Replay(
            IReadOnlyList<GrudgeEntry> entries,
            GrudgeScope scope,
            SituationsConfig cfg,
            TraitProfile? holder,
            double today)
        {
            var decayCfg = cfg?.GrudgeDecay ?? new GrudgeDecayConfig();
            var bandCfg = scope == GrudgeScope.Personal
                ? (decayCfg.Personal ?? new GrudgeBandConfig { Band = 10.0, DaysPerPoint = 3.0 })
                : (decayCfg.Clan ?? new GrudgeBandConfig { Band = 20.0, DaysPerPoint = 6.0 });

            double band = bandCfg.Band;
            double daysPerPoint = bandCfg.DaysPerPoint > 0 ? bandCfg.DaysPerPoint : 1.0;

            // 日期比今天晚的筆數不重播（規格 §2.2.1）：那是一條被抹掉的時間線結下的恩怨。
            // 判斷在 EventVisibility，這裡不自己寫一份。
            int hiddenFuture = 0;
            var visible = new List<GrudgeEntry>();
            if (entries != null)
            {
                foreach (var e in entries)
                {
                    if (e == null) continue;
                    if (!EventVisibility.IsVisibleOn(e.Day, today))
                    {
                        hiddenFuture++;
                        continue;
                    }
                    visible.Add(e);
                }
            }

            if (visible.Count == 0)
            {
                return new GrudgeReplayResult
                {
                    Value = 0.0,
                    Steps = Array.Empty<GrudgeReplayStep>(),
                    EntryCount = 0,
                    HiddenFutureCount = hiddenFuture,
                    Band = band,
                    DaysPerPoint = daysPerPoint
                };
            }

            var sorted = visible.OrderBy(e => e.Day).ThenBy(e => e.EventId, StringComparer.Ordinal).ToList();

            double v = 0.0;
            var steps = new List<GrudgeReplayStep>();

            for (int i = 0; i < sorted.Count; i++)
            {
                var entry = sorted[i];
                v += entry.Requested;

                double currentDay = entry.Day;
                double nextDay = (i < sorted.Count - 1) ? sorted[i + 1].Day : today;
                double daysElapsed = Math.Max(0.0, nextDay - currentDay);

                double valueBefore = v;
                double multiplier = GetMultiplier(v, decayCfg, holder, out string multiplierDetail);
                double pointsDecayed = 0.0;
                bool hitBand = false;
                bool insideBand = false;
                double valueAfterDecay = v;

                if (daysElapsed <= 0.0)
                {
                    insideBand = (Math.Abs(v) <= band);
                    valueAfterDecay = v;
                }
                else if (v == 0.0)
                {
                    insideBand = true;
                    valueAfterDecay = 0.0;
                }
                else if (Math.Abs(v) <= band)
                {
                    insideBand = true;
                    valueAfterDecay = v;
                }
                else
                {
                    insideBand = false;
                    double possiblePoints = daysElapsed / daysPerPoint * multiplier;
                    double distToBand = Math.Abs(v) - band;
                    double actualPoints = Math.Min(possiblePoints, distToBand);

                    hitBand = Math.Abs(actualPoints - distToBand) < 1e-9;
                    pointsDecayed = actualPoints;
                    valueAfterDecay = v - Math.Sign(v) * actualPoints;
                }

                v = valueAfterDecay;

                double? nextRequested = (i < sorted.Count - 1) ? sorted[i + 1].Requested : (double?)null;
                double valueAfter = nextRequested.HasValue ? (v + nextRequested.Value) : v;

                steps.Add(new GrudgeReplayStep
                {
                    Day = nextDay,
                    EntryDay = currentDay,
                    DaysElapsed = daysElapsed,
                    ValueBefore = valueBefore,
                    ValueAfterRequested = valueBefore,
                    Multiplier = multiplier,
                    MultiplierDetail = multiplierDetail,
                    PointsDecayed = pointsDecayed,
                    HitBand = hitBand,
                    InsideBand = insideBand,
                    ValueAfterDecay = valueAfterDecay,
                    Requested = entry.Requested,
                    EventId = entry.EventId,
                    ValueAfter = valueAfter
                });
            }

            return new GrudgeReplayResult
            {
                Value = v,
                Steps = steps,
                EntryCount = sorted.Count,
                HiddenFutureCount = hiddenFuture,
                Band = band,
                DaysPerPoint = daysPerPoint
            };
        }

        public static double GetMultiplier(
            double v,
            GrudgeDecayConfig decayCfg,
            TraitProfile? holder,
            out string detail)
        {
            double min = decayCfg.MultiplierMin;
            double max = decayCfg.MultiplierMax;

            if (holder == null)
            {
                detail = "no trait profile";
                return 1.0;
            }

            var weights = decayCfg.TraitWeights ?? new GrudgeTraitWeightsConfig();

            // 依值的正負選取對應的特質權重：
            // v < 0 ⇒ 只計 mercy 與 calculating
            // v > 0 ⇒ 只計 honorForGratitude
            if (v < 0)
            {
                double sum = holder.Mercy * weights.Mercy + holder.Calculating * weights.Calculating;
                double raw = 1.0 + sum;
                double clamped = Math.Max(min, Math.Min(max, raw));
                detail = string.Format(CultureInfo.InvariantCulture,
                    "clamp(1 + mercy {0} x {1:0.00} + calculating {2} x {3:0.00}, {4:0.00}, {5:0.00})",
                    holder.Mercy, weights.Mercy, holder.Calculating, weights.Calculating, min, max);
                return clamped;
            }
            else if (v > 0)
            {
                double sum = holder.Honor * weights.HonorForGratitude;
                double raw = 1.0 + sum;
                double clamped = Math.Max(min, Math.Min(max, raw));
                detail = string.Format(CultureInfo.InvariantCulture,
                    "clamp(1 + honorForGratitude {0} x {1:0.00}, {2:0.00}, {3:0.00})",
                    holder.Honor, weights.HonorForGratitude, min, max);
                return clamped;
            }
            else
            {
                // v == 0：預設顯示 mercy/calculating
                double sum = holder.Mercy * weights.Mercy + holder.Calculating * weights.Calculating;
                double raw = 1.0 + sum;
                double clamped = Math.Max(min, Math.Min(max, raw));
                detail = string.Format(CultureInfo.InvariantCulture,
                    "clamp(1 + mercy {0} x {1:0.00} + calculating {2} x {3:0.00}, {4:0.00}, {5:0.00})",
                    holder.Mercy, weights.Mercy, holder.Calculating, weights.Calculating, min, max);
                return clamped;
            }
        }
    }
}
