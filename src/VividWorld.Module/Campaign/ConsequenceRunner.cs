using System;
using System.Collections.Generic;
using System.Linq;
using VividWorld.Core.Config;
using VividWorld.Core.Dialogue;
using VividWorld.Core.Events;
using VividWorld.Core.Grudges;
using VividWorld.Core.Rumors;

namespace VividWorld.Campaign
{
    internal static class ConsequenceRunner
    {
        // 關掉的時候每一輪講述都會走到這裡（預設 8 位講述者 × 24 小時），
        // 一輪印一行等於一天近 200 行。日桶換了才印，一天一行就夠說明「它是關著的」。
        private static int _lastDisabledLogBucket = int.MinValue;

        internal static void ResetDisabledLogThrottle() => _lastDisabledLogBucket = int.MinValue;

        internal static void Settle(
            WorldEvent evt,
            IReadOnlyList<KnownByEntry> hearers,   // 這一輪新知情的 ＋ 重述升級的
            double day,
            RumorEngine engine,
            WorldEventStore store,
            HeroLookup heroLookup,
            IHeroTraitLookup traits,
            DailyRelationBudget budget,
            VividWorldConfig config)
        {
            // 1. config.Consequences.Enabled == false ⇒ 印一行合計就回傳，不得做任何計算（§0 第 6 條）。
            if (config?.Consequences == null || !config.Consequences.Enabled)
            {
                int bucket = DailyCounter.BucketOf(day);
                if (bucket != _lastDisabledLogBucket)
                {
                    _lastDisabledLogBucket = bucket;
                    ModLog.Info(GrudgeLogFormatter.FormatOpinionsDisabled(hearers?.Count ?? 0));
                }
                return;
            }

            if (evt == null || hearers == null || hearers.Count == 0 || store == null || budget == null || engine == null)
            {
                return;
            }

            // 2. budget.Advance(day)。
            budget.Advance(day);

            // 3. 查模板：EventCatalogStore.TemplateByType
            var template = EventCatalogStore.TemplateByType(evt.Type);
            if (template?.Opinions == null || template.Opinions.Count == 0)
            {
                return;
            }

            // 4. 對每一位 hearer：
            var allRequests = new List<GrudgeRequest>();
            foreach (var hearer in hearers)
            {
                if (hearer == null) continue;

                // 玩家不會因為聽到傳聞而被改好感度（規格 §6.6、M6.5 卡 §1）
                if (string.Equals(hearer.HeroId, store.PlayerHeroId, StringComparison.Ordinal))
                {
                    continue;
                }

                IReadOnlyList<Fact> believed;
                if (hearer.KnownFactIds != null)
                {
                    var factSet = new HashSet<string>(hearer.KnownFactIds, StringComparer.Ordinal);
                    believed = evt.Facts.Where(f => factSet.Contains(f.Id)).ToList();
                }
                else
                {
                    believed = engine.FactsAtHop(evt, hearer.Hop, hearer.SourceHeroId ?? "");
                }

                double remainingDailyBudget = budget.Remaining(hearer.HeroId, config.Consequences.MaxAbsoluteDeltaPerHeroPerDay);

                var result = ConsequenceResolver.Resolve(
                    evt, hearer, believed, template.Opinions,
                    config.Consequences, remainingDailyBudget);

                // 每一筆 OpinionExclusion 印一行
                foreach (var excl in result.Exclusions)
                {
                    GrudgeApplier.OpinionSkippedThisSession++;
                    string reasonStr = GrudgeLogFormatter.FormatOpinionSkipReason(excl.Reason, excl.AboutHeroId, excl.AboutRole, excl.Amount);
                    ModLog.Info(GrudgeLogFormatter.FormatOpinionSkipped(
                        excl.ObserverHeroId, excl.AboutRole, evt.EventId, reasonStr));
                }

                if (result.Changes.Count == 0) continue;

                // 每一筆 RelationChange 轉成 GrudgeRequest
                foreach (var change in result.Changes)
                {
                    allRequests.Add(new GrudgeRequest
                    {
                        FromHeroId = change.ObserverHeroId,
                        AboutHeroId = change.AboutHeroId,
                        Requested = change.Requested,
                        LedgerOnly = config.Consequences.LedgerOnly,
                        ForceClanEscalation = false,
                        SourceFactId = change.SourceFactId,
                        FromRole = evt.RoleOf(change.ObserverHeroId) ?? "observer",
                        ToRole = change.AboutRole,

                        Source = GrudgeSource.Rumor,
                        RequireHop0 = false,

                        ObserverHop = change.ObserverHop,
                        TemplateAmount = change.TemplateAmount,
                        HopConfidence = change.HopConfidence,
                        WitnessMultiplier = change.WitnessMultiplier,
                        ObserverIsParticipant = change.ObserverIsParticipant,
                        FullAmount = change.FullAmount,
                        AlreadyApplied = change.AlreadyApplied
                    });

                    // 扣減當日預算
                    budget.Consume(hearer.HeroId, Math.Abs(change.Requested));
                }
            }

            // 5. 同一則事件的所有請求一次呼叫 GrudgeApplier.Apply
            if (allRequests.Count > 0)
            {
                GrudgeApplier.Apply(
                    evt, allRequests, day,
                    store, heroLookup, traits, config);
            }
        }
    }
}
