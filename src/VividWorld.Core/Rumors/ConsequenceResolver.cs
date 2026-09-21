using System;
using System.Collections.Generic;
using System.Linq;
using VividWorld.Core.Catalog;
using VividWorld.Core.Config;
using VividWorld.Core.Events;

namespace VividWorld.Core.Rumors
{
    public sealed class RelationChange
    {
        public string ObserverHeroId = string.Empty;
        public string AboutHeroId = string.Empty;
        public string AboutRole = string.Empty;
        public double Requested;        // 這一次實際要送進施加器的量（已扣掉先前記過的、已夾過當日額度）
        public double FullAmount;       // 這一手應有的滿額（給 log 與冪等用）
        public double AlreadyApplied;   // 這位觀察者在這則事件上、對這個人、先前已記過的量
        public string SourceFactId = string.Empty;     // 點到這個人名字的那一塊碎片
        public int ObserverHop;
        public double HopConfidence;
        public double WitnessMultiplier;
        public bool ObserverIsParticipant;
        public double TemplateAmount;   // 模板原始宣告量
    }

    public enum OpinionSkipReason
    {
        NotNamedByAnyFact,
        AboutIsObserver,
        AboutNotBound,
        BelowMinimum,
        DailyBudgetExhausted,
        AlreadyFullyApplied
    }

    public sealed class OpinionExclusion
    {
        public string ObserverHeroId = string.Empty;
        public string AboutRole = string.Empty;
        public string AboutHeroId = string.Empty;
        public OpinionSkipReason Reason;
        public double Amount;
    }

    public sealed class ConsequenceResult
    {
        public IReadOnlyList<RelationChange> Changes = Array.Empty<RelationChange>();
        public IReadOnlyList<OpinionExclusion> Exclusions = Array.Empty<OpinionExclusion>();
    }

    public static class ConsequenceResolver
    {
        /// <summary>這位觀察者手上的碎片點到名的英雄 id 集合。規格 §6.7.1：只讀他相信什麼。</summary>
        public static IReadOnlyCollection<string> NamedHeroIds(IReadOnlyList<Fact> believed)
        {
            var set = new HashSet<string>(StringComparer.Ordinal);
            if (believed == null || believed.Count == 0)
            {
                return set;
            }

            const string prefix = "hero:";
            foreach (var fact in believed)
            {
                if (fact?.Vars == null) continue;
                foreach (var val in fact.Vars.Values)
                {
                    if (val != null && val.StartsWith(prefix, StringComparison.Ordinal))
                    {
                        string heroId = val.Substring(prefix.Length);
                        if (!string.IsNullOrEmpty(heroId))
                        {
                            set.Add(heroId);
                        }
                    }
                }
            }

            return set;
        }

        public static ConsequenceResult Resolve(
            WorldEvent evt,
            KnownByEntry observer,
            IReadOnlyList<Fact> believed,
            IReadOnlyList<OpinionDef> opinions,
            ConsequenceConfig cfg,
            double remainingDailyBudget)
        {
            var changes = new List<RelationChange>();
            var exclusions = new List<OpinionExclusion>();

            if (evt == null || observer == null || opinions == null || opinions.Count == 0 || cfg == null)
            {
                return new ConsequenceResult
                {
                    Changes = changes,
                    Exclusions = exclusions
                };
            }

            var namedHeroes = NamedHeroIds(believed);
            remainingDailyBudget = Math.Max(0.0, remainingDailyBudget);

            foreach (var def in opinions)
            {
                if (def == null || string.IsNullOrWhiteSpace(def.About)) continue;

                // 1. aboutHeroId = evt.Participants[def.About] // 綁不到 ⇒ 排除 AboutNotBound
                if (evt.Participants == null || !evt.Participants.TryGetValue(def.About, out var aboutHeroId) || string.IsNullOrEmpty(aboutHeroId))
                {
                    exclusions.Add(new OpinionExclusion
                    {
                        ObserverHeroId = observer.HeroId,
                        AboutRole = def.About,
                        AboutHeroId = string.Empty,
                        Reason = OpinionSkipReason.AboutNotBound,
                        Amount = def.Amount
                    });
                    continue;
                }

                // 2. aboutHeroId == observer.HeroId // ⇒ 排除 AboutIsObserver（永不對自己結算）
                if (string.Equals(aboutHeroId, observer.HeroId, StringComparison.Ordinal))
                {
                    exclusions.Add(new OpinionExclusion
                    {
                        ObserverHeroId = observer.HeroId,
                        AboutRole = def.About,
                        AboutHeroId = aboutHeroId,
                        Reason = OpinionSkipReason.AboutIsObserver,
                        Amount = def.Amount
                    });
                    continue;
                }

                // 3. aboutHeroId ∉ NamedHeroIds(believed) // ⇒ 排除 NotNamedByAnyFact
                if (!namedHeroes.Contains(aboutHeroId))
                {
                    exclusions.Add(new OpinionExclusion
                    {
                        ObserverHeroId = observer.HeroId,
                        AboutRole = def.About,
                        AboutHeroId = aboutHeroId,
                        Reason = OpinionSkipReason.NotNamedByAnyFact,
                        Amount = def.Amount
                    });
                    continue;
                }

                // 4. Calculate full
                int hopCount = cfg.HopConfidence?.Length ?? 0;
                int hop = hopCount > 0 ? Math.Max(0, Math.Min(observer.Hop, hopCount - 1)) : 0;
                double hopConf = hopCount > 0 ? cfg.HopConfidence![hop] : 1.0;
                bool observerIsPart = evt.RoleOf(observer.HeroId) != null;
                double witnessMult = observerIsPart ? 1.0 : cfg.BystanderMultiplier;
                double full = def.Amount * hopConf * witnessMult;

                // 5. Calculate already applied
                double already = 0.0;
                if (observer.RelationImpacts != null)
                {
                    foreach (var impact in observer.RelationImpacts)
                    {
                        if (impact != null && impact.Source == GrudgeSource.Rumor &&
                            string.Equals(impact.AboutHeroId, aboutHeroId, StringComparison.Ordinal))
                        {
                            already += impact.Requested;
                        }
                    }
                }

                double delta = full - already;

                // 6. Check minAbsoluteDelta
                if (Math.Abs(delta) < cfg.MinAbsoluteDelta)
                {
                    exclusions.Add(new OpinionExclusion
                    {
                        ObserverHeroId = observer.HeroId,
                        AboutRole = def.About,
                        AboutHeroId = aboutHeroId,
                        Reason = (already == 0.0 ? OpinionSkipReason.BelowMinimum : OpinionSkipReason.AlreadyFullyApplied),
                        Amount = delta
                    });
                    continue;
                }

                // 7. Check daily budget
                double clampedDelta = delta;
                if (Math.Abs(clampedDelta) > remainingDailyBudget)
                {
                    clampedDelta = Math.Sign(delta) * remainingDailyBudget;
                }

                if (Math.Abs(clampedDelta) < cfg.MinAbsoluteDelta)
                {
                    exclusions.Add(new OpinionExclusion
                    {
                        ObserverHeroId = observer.HeroId,
                        AboutRole = def.About,
                        AboutHeroId = aboutHeroId,
                        Reason = OpinionSkipReason.DailyBudgetExhausted,
                        Amount = delta
                    });
                    continue;
                }

                remainingDailyBudget -= Math.Abs(clampedDelta);

                // Find SourceFactId: first fact in believed mentioning hero:aboutHeroId
                string sourceFactId = string.Empty;
                if (believed != null)
                {
                    string heroRef = "hero:" + aboutHeroId;
                    foreach (var f in believed)
                    {
                        if (f?.Vars != null && f.Vars.Values.Any(v => string.Equals(v, heroRef, StringComparison.Ordinal)))
                        {
                            sourceFactId = f.Id ?? string.Empty;
                            break;
                        }
                    }
                }

                changes.Add(new RelationChange
                {
                    ObserverHeroId = observer.HeroId,
                    AboutHeroId = aboutHeroId,
                    AboutRole = def.About,
                    Requested = clampedDelta,
                    FullAmount = full,
                    AlreadyApplied = already,
                    SourceFactId = sourceFactId,
                    ObserverHop = observer.Hop,
                    HopConfidence = hopConf,
                    WitnessMultiplier = witnessMult,
                    ObserverIsParticipant = observerIsPart,
                    TemplateAmount = def.Amount
                });
            }

            return new ConsequenceResult
            {
                Changes = changes,
                Exclusions = exclusions
            };
        }
    }
}
