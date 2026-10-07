using System;
using System.Collections.Generic;
using System.Linq;
using VividWorld.Core.Catalog;
using VividWorld.Core.Config;
using VividWorld.Core.Dialogue;
using VividWorld.Core.Events;
using VividWorld.Core.Feelings;
using VividWorld.Core.Grudges;
using VividWorld.Core.Rumors;
using VividWorld.Core.Util;

namespace VividWorld.Campaign
{
    internal static class ConsequenceRunner
    {
        // 關掉的時候每一輪講述都會走到這裡（預設 8 位講述者 × 24 小時），
        // 一輪印一行等於一天近 200 行。日桶換了才印，一天一行就夠說明「它是關著的」。
        private static int _lastDisabledLogBucket = int.MinValue;

        internal static void ResetDisabledLogThrottle()
        {
            _lastDisabledLogBucket = int.MinValue;
            _lastBeliefDisabledLogBucket = int.MinValue;
        }

        // 信不信的判定關掉時同理：每個遊戲日一行就夠說明「它是關著的」。
        private static int _lastBeliefDisabledLogBucket = int.MinValue;

        // 擲骰是種子的純函數，沒有狀態，整個模組共用一個就好。
        private static readonly IDeterministicRng BeliefRng = new SplitMix64Rng();

        internal static void Settle(
            WorldEvent evt,
            IReadOnlyList<KnownByEntry> hearers,   // 這一輪新知情的 ＋ 重述升級的
            double day,
            RumorEngine engine,
            WorldEventStore store,
            HeroLookup heroLookup,
            IHeroTraitLookup traits,
            DailyRelationBudget budget,
            VividWorldConfig config,
            IFeelingWorld? feelingWorld = null,
            bool forceStepForward = false,
            string? forcedSettlementId = null,
            bool forceDisbelief = false)
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

            VividWorldConfig cfg = config ?? new VividWorldConfig();

            // 2. budget.Advance(day)。
            budget.Advance(day);

            // 3. 查模板：EventCatalogStore.TemplateByType
            var template = EventCatalogStore.TemplateByType(evt.Type);

            // 回應事件（模板有 response 欄位）：執行重判與撤回流程，回應本身不帶 opinion
            if (template != null && !string.IsNullOrEmpty(template.Response))
            {
                HandleResponseHearing(evt, template, hearers, day, engine, traits, cfg, feelingWorld, store, heroLookup, budget, forceDisbelief);
                return;
            }

            if (template?.Opinions == null || template.Opinions.Count == 0)
            {
                return;
            }

            bool beliefOn = cfg.FalseRumors?.BeliefEnabled ?? false;
            if (!beliefOn)
            {
                int beliefBucket = DailyCounter.BucketOf(day);
                if (beliefBucket != _lastBeliefDisabledLogBucket)
                {
                    _lastBeliefDisabledLogBucket = beliefBucket;
                    ModLog.Info(BeliefLogFormatter.FormatBeliefDisabled(hearers.Count));
                }
            }
            bool beliefWritten = false;

            // 若為起頭者不在場的編造傳聞，預先解析真事與知道實情的人清單
            IReadOnlyList<MadeUpTalk.TruthKnower>? truthKnowers = null;
            if (MadeUpTalk.IsHearsayOnly(evt))
            {
                string? linkedId = evt.LinkedEventId;
                WorldEvent? lEvent = (linkedId != null && linkedId.Length > 0) ? store.Load(linkedId) : null;
                if (!string.IsNullOrEmpty(linkedId) && lEvent == null)
                {
                    ModLog.Info($"Real event {linkedId} linked to made-up talk {evt.EventId} not found in store; using participants only");
                }
                truthKnowers = MadeUpTalk.GetTruthKnowers(evt, lEvent, store.PlayerHeroId);
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

                if (MadeUpTalk.IsHearsayOnly(evt))
                {
                    if (string.Equals(hearer.HeroId, evt.OriginatorHeroId, StringComparison.Ordinal))
                    {
                        ModLog.Info($"originator of made-up talk: not judged: {hearer.HeroId} on {evt.EventId}");
                        continue;
                    }

                    var tk = truthKnowers?.FirstOrDefault(t => string.Equals(t.HeroId, hearer.HeroId, StringComparison.Ordinal));
                    if (tk != null)
                    {
                        HandleTruthKnower(evt, hearer, tk, template, day, engine, traits, cfg, feelingWorld, store, heroLookup, budget, ref beliefWritten, allRequests, forceStepForward, forcedSettlementId);
                        continue;
                    }

                    if (evt.RoleOf(hearer.HeroId) != null)
                    {
                        ModLog.Info($"participant of made-up talk: not in truth knowers: {hearer.HeroId} on {evt.EventId}");
                        continue;
                    }
                }

                var first = OpinionSelector.Subject(template.Opinions, evt.Fabricated);
                string subjectId = string.Empty;
                if (first != null && !string.IsNullOrEmpty(first.About) && evt.Participants != null)
                {
                    evt.Participants.TryGetValue(first.About, out var bound);
                    subjectId = bound ?? string.Empty;
                }

                bool believes = true;
                if (beliefOn)
                {
                    bool wroteNow;
                    believes = JudgeBelief(evt, hearer, template.Opinions, day, engine, traits, cfg, feelingWorld, store, out wroteNow);
                    beliefWritten |= wroteNow;
                }

                List<OpinionDef> opinionsToSettle = OpinionSelector.ToSettle(template.Opinions, evt.Fabricated, believes);

                if (!believes)
                {
                    if (opinionsToSettle.Count == 0)
                    {
                        LogSkippedForDisbelief(evt, hearer, template.Opinions);
                    }

                    if (MadeUpTalk.KnowsOriginator(evt, hearer) &&
                        !string.IsNullOrEmpty(evt.OriginatorHeroId) &&
                        !string.Equals(evt.OriginatorHeroId, subjectId, StringComparison.Ordinal))
                    {
                        bool alreadyImpacted = hearer.RelationImpacts != null &&
                            hearer.RelationImpacts.Any(ri => string.Equals(ri.AboutHeroId, evt.OriginatorHeroId, StringComparison.Ordinal) && !ri.Contradicted);
                        if (!alreadyImpacted)
                        {
                            double opinionScore = first?.Amount ?? 0.0;
                            double grudgeScore = MadeUpTalk.OriginatorGrudge(opinionScore);
                            double remainingBudget = budget.Remaining(hearer.HeroId, cfg.Consequences.MaxAbsoluteDeltaPerHeroPerDay);
                            double requested = Math.Max(-remainingBudget, Math.Min(remainingBudget, grudgeScore));
                            if (Math.Abs(requested) > 1e-6)
                            {
                                budget.Consume(hearer.HeroId, Math.Abs(requested));
                                allRequests.Add(new GrudgeRequest
                                {
                                    FromHeroId = hearer.HeroId,
                                    AboutHeroId = evt.OriginatorHeroId!,
                                    Requested = requested,
                                    LedgerOnly = cfg.Consequences.LedgerOnly,
                                    ForceClanEscalation = false,
                                    SourceFactId = null,
                                    FromRole = evt.RoleOf(hearer.HeroId) ?? "observer",
                                    ToRole = "originator",
                                    Source = GrudgeSource.Rumor,
                                    RequireHop0 = false,
                                    ObserverHop = hearer.Hop,
                                    TemplateAmount = grudgeScore,
                                    HopConfidence = 1.0,
                                    WitnessMultiplier = 1.0,
                                    ObserverIsParticipant = false,
                                    FullAmount = grudgeScore,
                                    AlreadyApplied = 0.0,
                                    TraitMultiplier = 1.0,
                                    RelationMultiplier = 1.0,
                                    RelationReason = ReactionMultipliers.ReasonNone,
                                    ReceiverHeroId = string.Empty
                                });
                                string whyKnows = hearer.Hop == 2 ? "heard directly from originator (hop 2)" : "heard originator credited (hop 3)";
                                ModLog.Info($"Originator grudge: {hearer.HeroId} against originator {evt.OriginatorHeroId}: amount {requested:+0.##;-0.##;0} (half of {opinionScore:+0.##;-0.##;0}), {whyKnows} on {evt.EventId}");
                            }
                        }
                    }

                    if (opinionsToSettle.Count == 0)
                    {
                        continue;
                    }
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

                double remainingDailyBudget = budget.Remaining(hearer.HeroId, cfg.Consequences.MaxAbsoluteDeltaPerHeroPerDay);

                // 更新前就算過好感的人：兩個倍數當 1（不傳反應資料），否則重聽時新倍數會把先前的量退回一部分
                bool settledBefore = BeliefJudge.IsSettledBeforeBelief(hearer, beliefOn);
                if (settledBefore)
                {
                    ModLog.Info(GrudgeLogFormatter.FormatSettledBeforeMultipliers(hearer.HeroId, evt.EventId));
                }

                var result = ConsequenceResolver.Resolve(
                    evt, hearer, believed, opinionsToSettle,
                    cfg.Consequences, remainingDailyBudget,
                    settledBefore ? null : BuildReactionInputs(hearer.HeroId, traits, feelingWorld, cfg));

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
                        LedgerOnly = cfg.Consequences.LedgerOnly,
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
                        AlreadyApplied = change.AlreadyApplied,

                        TraitMultiplier = change.TraitMultiplier,
                        TraitName = change.TraitName,
                        TraitLevel = change.TraitLevel,
                        RelationMultiplier = change.RelationMultiplier,
                        RelationReason = change.RelationReason,
                        ReceiverHeroId = change.ReceiverHeroId
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
                    store, heroLookup, traits, cfg);
            }

            // 判定結果寫在知情記錄上；沒有任何結算請求（例如全都不信）時 Apply 不會存檔，這裡補上
            if (beliefWritten)
            {
                store.Upsert(evt);
            }
        }

        /// <summary>
        /// 聽的人反應多大要用的資料：聽者的個性、對任一位英雄的好感、跟任一位英雄是不是同家族。
        /// 結算與開發者工具共用這一份，兩邊算出來才會一致。
        /// </summary>
        internal static ReactionInputs BuildReactionInputs(
            string hearerId,
            IHeroTraitLookup? traits,
            IFeelingWorld? feelingWorld,
            VividWorldConfig config)
        {
            var inputs = new ReactionInputs
            {
                Config = config?.FalseRumors?.Reaction ?? new ReactionConfig(),
                HearerTraits = traits?.Of(hearerId)
            };

            if (feelingWorld != null)
            {
                inputs.AffectionToward = heroId => feelingWorld.Affection(hearerId, heroId);
                inputs.IsSameClan = heroId =>
                {
                    string? mine = feelingWorld.InterestFacts(hearerId)?.ClanId;
                    string? theirs = feelingWorld.InterestFacts(heroId)?.ClanId;
                    return !string.IsNullOrEmpty(mine) && !string.IsNullOrEmpty(theirs) &&
                           string.Equals(mine, theirs, StringComparison.Ordinal);
                };
            }

            return inputs;
        }

        /// <summary>
        /// 這位聽者信不信這則消息。已經判過就沿用；更新前就結算過好感的記成信（Legacy），不回頭重算；
        /// 其餘用第一條 opinion 的 about 當被說的人，交給 Core 的判定。結果寫進他那筆知情記錄。
        /// </summary>
        private static bool JudgeBelief(
            WorldEvent evt,
            KnownByEntry hearer,
            IReadOnlyList<OpinionDef> opinions,
            double day,
            RumorEngine engine,
            IHeroTraitLookup traits,
            VividWorldConfig config,
            IFeelingWorld? feelingWorld,
            WorldEventStore store,
            out bool wrote)
        {
            wrote = false;

            // 已經判過：沿用，不重判
            if (hearer.Believes.HasValue)
            {
                return hearer.Believes.Value;
            }

            // 更新前就結算過的好感不動，也就不能事後說他其實不信
            if (BeliefJudge.HasLegacySettlement(hearer))
            {
                hearer.Believes = true;
                hearer.BeliefDay = day;
                hearer.BeliefReason = BeliefJudge.LegacyReason;
                wrote = true;
                ModLog.Info(BeliefLogFormatter.FormatLegacy(hearer.HeroId, evt.EventId));
                return true;
            }

            var first = OpinionSelector.Subject(opinions, evt.Fabricated) ?? opinions[0];
            string subjectId = string.Empty;
            if (first != null && !string.IsNullOrEmpty(first.About) && evt.Participants != null)
            {
                evt.Participants.TryGetValue(first.About, out var bound);
                subjectId = bound ?? string.Empty;
            }

            double responsePenalty = ComputeResponsePenalty(store, evt.EventId, hearer.HeroId, config.FalseRumors?.Belief);

            string tellerId = hearer.SourceHeroId ?? string.Empty;
            var input = new BeliefInputs
            {
                CampaignSeed = engine.CampaignSeed,
                EventId = evt.EventId,
                HearerId = hearer.HeroId,
                SubjectId = subjectId,
                TellerId = tellerId,
                Hop = hearer.Hop,
                HearerIsParticipant = evt.RoleOf(hearer.HeroId) != null,
                SubjectAffection = (feelingWorld != null && subjectId.Length > 0)
                    ? feelingWorld.Affection(hearer.HeroId, subjectId) : null,
                TellerAffection = (feelingWorld != null && tellerId.Length > 0)
                    ? feelingWorld.Affection(hearer.HeroId, tellerId) : null,
                Trait = first?.Trait,
                SubjectTraitLevel = subjectId.Length > 0 ? OpinionTraits.LevelOf(first?.Trait, traits?.Of(subjectId)) : null,
                OpinionAmount = first?.Amount ?? 0.0,
                ListenerCalculating = traits?.Of(hearer.HeroId)?.Calculating ?? 0,
                ResponsePenalty = responsePenalty,
                Round = 0
            };

            var beliefCfg = config?.FalseRumors?.Belief ?? new BeliefConfig();
            var result = BeliefJudge.Judge(input, beliefCfg, BeliefRng);

            hearer.Believes = result.Believes;
            hearer.BeliefDay = day;
            hearer.BeliefChance = result.Rolled ? result.Chance : (double?)null;
            hearer.BeliefReason = result.Heaviest.ToString();
            hearer.BeliefRound = input.Round;
            wrote = true;

            ModLog.Info(BeliefLogFormatter.FormatJudged(input, result));
            return result.Believes;
        }

        internal static double ComputeResponsePenalty(
            WorldEventStore store,
            string xEventId,
            string hearerHeroId,
            BeliefConfig? beliefConfig,
            string? currentResponse = null)
        {
            if (beliefConfig == null || store?.Index == null || string.IsNullOrEmpty(xEventId) || string.IsNullOrEmpty(hearerHeroId))
            {
                return 0.0;
            }

            bool hasDenial = false;
            bool hasClarification = false;

            if (string.Equals(currentResponse, "denial", StringComparison.OrdinalIgnoreCase))
            {
                hasDenial = true;
            }
            else if (string.Equals(currentResponse, "clarification", StringComparison.OrdinalIgnoreCase))
            {
                hasClarification = true;
            }

            foreach (var entry in store.Index.Entries)
            {
                if (entry == null || !string.Equals(entry.LinkedEventId, xEventId, StringComparison.Ordinal)) continue;
                if (!entry.KnownByHeroIds.Contains(hearerHeroId)) continue;

                var t = EventCatalogStore.TemplateByType(entry.Type);
                if (t != null)
                {
                    if (string.Equals(t.Response, "denial", StringComparison.OrdinalIgnoreCase))
                    {
                        hasDenial = true;
                    }
                    else if (string.Equals(t.Response, "clarification", StringComparison.OrdinalIgnoreCase))
                    {
                        hasClarification = true;
                    }
                }
            }

            double penalty = 0.0;
            if (hasClarification) penalty += beliefConfig.ClarifiedDelta;
            if (hasDenial) penalty += beliefConfig.DeniedDelta;
            return penalty;
        }

        internal sealed class RetractionResult
        {
            public int RetractedCount { get; set; }
            public double TotalRetractedDelta { get; set; }
            public double RefundedToday { get; set; }
            public double DailyUsedBefore { get; set; }
            public double DailyUsedAfter { get; set; }
            public List<string> NonRefundReasons { get; } = new List<string>();
        }

        internal static RetractionResult RetractRelationImpacts(
            KnownByEntry knower,
            WorldEvent xEvent,
            double day,
            WorldEventStore store,
            HeroLookup? heroLookup,
            DailyRelationBudget? budget = null)
        {
            var result = new RetractionResult();
            if (knower.RelationImpacts == null) return result;

            double usedBefore = budget != null ? budget.Used(knower.HeroId) : 0.0;
            result.DailyUsedBefore = usedBefore;

            foreach (var ri in knower.RelationImpacts)
            {
                if (ri.Source != GrudgeSource.Rumor || ri.Contradicted) continue;

                // 只看那一筆自己的 LedgerOnly：設定後來改成只記帳時，當初真的寫進原生的那幾筆仍要寫回
                if (!ri.LedgerOnly && heroLookup != null)
                {
                    var heroA = heroLookup.Get(knower.HeroId);
                    var heroB = heroLookup.Get(ri.AboutHeroId);
                    if (heroA != null && heroB != null)
                    {
                        int before = heroA.GetBaseHeroRelation(heroB);
                        int reverseDelta = -(int)Math.Round((double)ri.Delta, MidpointRounding.AwayFromZero);
                        heroA.SetPersonalRelation(heroB, before + reverseDelta);
                        int after = heroA.GetBaseHeroRelation(heroB);
                        ModLog.Info($"Retracted native relation: {knower.HeroId} toward {ri.AboutHeroId} reversed by {reverseDelta} (original delta {ri.Delta:+0.##;-0.##;0}): before {before}, after {after} on {xEvent.EventId}");
                    }
                }

                if (DailyRelationBudget.CanRefundImpact(ri, day, out var nonRefundReason))
                {
                    if (budget != null)
                    {
                        double refunded = budget.Refund(knower.HeroId, Math.Abs(ri.Requested));
                        result.RefundedToday += refunded;
                        // 讀檔後額度歸零：同一天算的那筆已經不在今天的用量裡，沒有東西可退
                        if (refunded <= 0.0) result.NonRefundReasons.Add("nothing used today to refund");
                    }
                }
                else if (nonRefundReason != null)
                {
                    result.NonRefundReasons.Add(nonRefundReason);
                }

                ri.Contradicted = true;
                ri.ContradictedDay = day;
                store.Grudges.Remove(xEvent.EventId, knower.HeroId, ri.AboutHeroId, GrudgeSource.Rumor);

                result.RetractedCount++;
                result.TotalRetractedDelta += Math.Abs((double)ri.Delta);
            }

            result.DailyUsedAfter = budget != null ? budget.Used(knower.HeroId) : 0.0;
            return result;
        }

        private static void HandleResponseHearing(
            WorldEvent evt,
            EventTemplate template,
            IReadOnlyList<KnownByEntry> hearers,
            double day,
            RumorEngine engine,
            IHeroTraitLookup traits,
            VividWorldConfig config,
            IFeelingWorld? feelingWorld,
            WorldEventStore store,
            HeroLookup heroLookup,
            DailyRelationBudget budget,
            bool forceDisbelief = false)
        {
            string? linkedId = evt.LinkedEventId;
            if (linkedId == null || linkedId.Length == 0) return;
            var xEvent = store.Load(linkedId);
            if (xEvent == null)
            {
                ModLog.Info($"Response event {evt.EventId} links to missing event {linkedId}");
                return;
            }

            bool beliefOn = config?.FalseRumors?.BeliefEnabled ?? false;
            if (!beliefOn)
            {
                return;
            }

            var xTemplate = EventCatalogStore.TemplateByType(xEvent.Type);
            var first = OpinionSelector.Subject(xTemplate?.Opinions, xEvent.Fabricated);
            string subjectId = string.Empty;
            if (first != null && !string.IsNullOrEmpty(first.About) && xEvent.Participants != null)
            {
                xEvent.Participants.TryGetValue(first.About, out var bound);
                subjectId = bound ?? string.Empty;
            }

            bool xModified = false;
            foreach (var hearer in hearers)
            {
                if (hearer == null) continue;
                if (string.Equals(hearer.HeroId, store.PlayerHeroId, StringComparison.Ordinal)) continue;

                var xKnower = xEvent.EntryFor(hearer.HeroId);
                if (xKnower == null)
                {
                    ModLog.Info($"Hearer {hearer.HeroId} heard response {evt.EventId}, does not know original talk {xEvent.EventId} yet");
                    continue;
                }

                // 起頭的人知道那是他自己編的，聽到否認不會「改成不信」；記成不信會讓他之後用「我是不信的」口吻轉述自己編的話
                if (string.Equals(hearer.HeroId, xEvent.OriginatorHeroId, StringComparison.Ordinal))
                {
                    ModLog.Info($"Hearer {hearer.HeroId} is the originator of {xEvent.EventId}, response {evt.EventId} not reconsidered");
                    continue;
                }

                if (xKnower.Believes == false)
                {
                    ModLog.Info($"Hearer {hearer.HeroId} already disbelieves {xEvent.EventId}, response {evt.EventId} ignored for reconsideration");
                    continue;
                }

                if (string.Equals(xKnower.BeliefReason, BeliefJudge.LegacyReason, StringComparison.Ordinal))
                {
                    continue;
                }

                if (string.Equals(xKnower.BeliefReason, BeliefReason.KnowsTruth.ToString(), StringComparison.Ordinal))
                {
                    continue;
                }

                // 重判信任度
                double responsePenalty = ComputeResponsePenalty(store, xEvent.EventId, hearer.HeroId, config?.FalseRumors?.Belief, template.Response);
                int nextRound = (xKnower.BeliefRound ?? 0) + 1;
                string tellerId = xKnower.SourceHeroId ?? string.Empty;

                var input = new BeliefInputs
                {
                    CampaignSeed = engine.CampaignSeed,
                    EventId = xEvent.EventId,
                    HearerId = hearer.HeroId,
                    SubjectId = subjectId,
                    TellerId = tellerId,
                    Hop = xKnower.Hop,
                    HearerIsParticipant = xEvent.RoleOf(hearer.HeroId) != null,
                    SubjectAffection = (feelingWorld != null && subjectId.Length > 0)
                        ? feelingWorld.Affection(hearer.HeroId, subjectId) : null,
                    TellerAffection = (feelingWorld != null && tellerId.Length > 0)
                        ? feelingWorld.Affection(hearer.HeroId, tellerId) : null,
                    Trait = first?.Trait,
                    SubjectTraitLevel = subjectId.Length > 0 ? OpinionTraits.LevelOf(first?.Trait, traits?.Of(subjectId)) : null,
                    OpinionAmount = first?.Amount ?? 0.0,
                    ListenerCalculating = traits?.Of(hearer.HeroId)?.Calculating ?? 0,
                    ResponsePenalty = responsePenalty,
                    Round = nextRound
                };

                var beliefCfg = config?.FalseRumors?.Belief ?? new BeliefConfig();
                var result = BeliefJudge.Judge(input, beliefCfg, BeliefRng);

                if (forceDisbelief)
                {
                    result.Believes = false;
                }

                string oldChanceStr = xKnower.BeliefChance.HasValue ? $"{xKnower.BeliefChance.Value:F1}%" : "none";
                string newChanceStr = result.Rolled ? $"{result.Chance:F1}%" : "none";
                string judgedLog = BeliefLogFormatter.FormatJudged(input, result);
                string forcedTag = forceDisbelief ? " [forced]" : "";

                ModLog.Info($"reconsidered belief{forcedTag} (heard response {evt.EventId}): chance {oldChanceStr} -> {newChanceStr} | {judgedLog}");

                if (result.Believes)
                {
                    // 維持信：更新輪數與機率，不撤銷好感
                    xKnower.BeliefChance = result.Rolled ? result.Chance : (double?)null;
                    xKnower.BeliefRound = nextRound;
                    xKnower.BeliefReason = result.Heaviest.ToString();
                    xModified = true;
                }
                else
                {
                    // 翻盤成不信：撤銷好感影響與 GrudgeIndex 記錄
                    xKnower.Believes = false;
                    xKnower.BeliefDay = day;
                    xKnower.BeliefChance = result.Rolled ? result.Chance : (double?)null;
                    xKnower.BeliefReason = result.Heaviest.ToString();
                    xKnower.BeliefRound = nextRound;
                    xModified = true;

                    var retraction = RetractRelationImpacts(xKnower, xEvent, day, store, heroLookup, budget);
                    string retractionSuffix = GrudgeLogFormatter.FormatRetractionSuffix(
                        retraction.RefundedToday,
                        retraction.DailyUsedBefore,
                        retraction.DailyUsedAfter,
                        retraction.NonRefundReasons);
                    ModLog.Info($"Retracted on {xEvent.EventId} for {hearer.HeroId}: {retraction.RetractedCount} impact(s), total delta {retraction.TotalRetractedDelta:0.##}{retractionSuffix}");

                    // 追溯產生對起頭者的怨恨（折半、吃當日預算）
                    if (MadeUpTalk.KnowsOriginator(xEvent, xKnower) &&
                        !string.IsNullOrEmpty(xEvent.OriginatorHeroId) &&
                        !string.Equals(xEvent.OriginatorHeroId, subjectId, StringComparison.Ordinal) &&
                        !string.Equals(xEvent.OriginatorHeroId, xKnower.HeroId, StringComparison.Ordinal))
                    {
                        bool alreadyImpacted = xKnower.RelationImpacts != null &&
                            xKnower.RelationImpacts.Any(ri => string.Equals(ri.AboutHeroId, xEvent.OriginatorHeroId, StringComparison.Ordinal) && !ri.Contradicted);

                        if (!alreadyImpacted)
                        {
                            double opinionScore = first?.Amount ?? 0.0;
                            double grudgeScore = MadeUpTalk.OriginatorGrudge(opinionScore);
                            double remainingBudget = budget.Remaining(xKnower.HeroId, config?.Consequences?.MaxAbsoluteDeltaPerHeroPerDay ?? 6.0);
                            double requested = Math.Max(-remainingBudget, Math.Min(remainingBudget, grudgeScore));
                            if (Math.Abs(requested) > 1e-6)
                            {
                                budget.Consume(xKnower.HeroId, Math.Abs(requested));
                                var req = new GrudgeRequest
                                {
                                    FromHeroId = xKnower.HeroId,
                                    AboutHeroId = xEvent.OriginatorHeroId!,
                                    Requested = requested,
                                    LedgerOnly = config?.Consequences?.LedgerOnly ?? false,
                                    ForceClanEscalation = false,
                                    SourceFactId = null,
                                    FromRole = xEvent.RoleOf(xKnower.HeroId) ?? "observer",
                                    ToRole = "originator",
                                    Source = GrudgeSource.Rumor,
                                    RequireHop0 = false,
                                    ObserverHop = xKnower.Hop,
                                    TemplateAmount = grudgeScore,
                                    HopConfidence = 1.0,
                                    WitnessMultiplier = 1.0,
                                    ObserverIsParticipant = false,
                                    FullAmount = grudgeScore,
                                    AlreadyApplied = 0.0,
                                    TraitMultiplier = 1.0,
                                    RelationMultiplier = 1.0,
                                    RelationReason = ReactionMultipliers.ReasonNone,
                                    ReceiverHeroId = string.Empty
                                };
                                if (traits != null)
                                {
                                    GrudgeApplier.Apply(xEvent, new[] { req }, day, store, heroLookup, traits, config ?? new VividWorldConfig());
                                }
                                string whyKnows = xKnower.Hop == 2 ? "heard directly from originator (hop 2)" : "heard originator credited (hop 3)";
                                ModLog.Info($"Originator grudge (retracted belief): {xKnower.HeroId} against originator {xEvent.OriginatorHeroId}: amount {requested:+0.##;-0.##;0} (half of {opinionScore:+0.##;-0.##;0}), {whyKnows} on {xEvent.EventId}");
                            }
                        }
                    }
                }
            }

            if (xModified)
            {
                store.Upsert(xEvent);
            }
        }

        private static void HandleTruthKnower(
            WorldEvent evt,
            KnownByEntry hearer,
            MadeUpTalk.TruthKnower tk,
            EventTemplate template,
            double day,
            RumorEngine engine,
            IHeroTraitLookup traits,
            VividWorldConfig config,
            IFeelingWorld? feelingWorld,
            WorldEventStore store,
            HeroLookup heroLookup,
            DailyRelationBudget budget,
            ref bool beliefWritten,
            List<GrudgeRequest> allRequests,
            bool forceStepForward = false,
            string? forcedSettlementId = null)
        {
            bool beliefOn = config?.FalseRumors?.BeliefEnabled ?? false;
            if (beliefOn)
            {
                var input = new BeliefInputs
                {
                    CampaignSeed = engine.CampaignSeed,
                    EventId = evt.EventId,
                    HearerId = hearer.HeroId,
                    KnowsTruth = true,
                    KnowsTruthReason = tk.Reason
                };
                var beliefCfg = config?.FalseRumors?.Belief ?? new BeliefConfig();
                var result = BeliefJudge.Judge(input, beliefCfg, BeliefRng);
                hearer.Believes = result.Believes;
                hearer.BeliefDay = day;
                hearer.BeliefChance = null;
                hearer.BeliefReason = result.Heaviest.ToString();
                hearer.BeliefRound = 0;
                beliefWritten = true;

                ModLog.Info($"knows the truth: {hearer.HeroId} on {evt.EventId} ({tk.Reason}), auto-disbelieved");
            }

            var first = OpinionSelector.Subject(template.Opinions, evt.Fabricated);
            bool isGoodTalk = first != null && first.Amount > 0;

            // 記恨起頭的人
            if (MadeUpTalk.KnowsOriginator(evt, hearer) &&
                !string.IsNullOrEmpty(evt.OriginatorHeroId) &&
                !string.Equals(evt.OriginatorHeroId, hearer.HeroId, StringComparison.Ordinal))
            {
                bool alreadyImpacted = hearer.RelationImpacts != null &&
                    hearer.RelationImpacts.Any(ri => string.Equals(ri.AboutHeroId, evt.OriginatorHeroId, StringComparison.Ordinal) && !ri.Contradicted);

                if (!alreadyImpacted)
                {
                    int honor = traits?.Of(hearer.HeroId)?.Honor ?? 0;
                    double opinionScore = first?.Amount ?? 0.0;
                    double multiplier = config?.FalseRumors?.Denial?.AccusedGrudgeMultiplier ?? 1.5;

                    var (grudgeScore, isCapped) = MadeUpTalk.CalculateKnowerOriginatorGrudge(
                        tk.IsAccused, isGoodTalk, honor, opinionScore, multiplier);

                    if (Math.Abs(grudgeScore) > 1e-6)
                    {
                        double requested = grudgeScore;
                        if (isCapped)
                        {
                            double remainingBudget = budget.Remaining(hearer.HeroId, config?.Consequences?.MaxAbsoluteDeltaPerHeroPerDay ?? 6.0);
                            requested = Math.Max(-remainingBudget, Math.Min(remainingBudget, grudgeScore));
                        }

                        if (Math.Abs(requested) > 1e-6)
                        {
                            if (isCapped)
                            {
                                budget.Consume(hearer.HeroId, Math.Abs(requested));
                            }

                            allRequests.Add(new GrudgeRequest
                            {
                                FromHeroId = hearer.HeroId,
                                AboutHeroId = evt.OriginatorHeroId!,
                                Requested = requested,
                                LedgerOnly = config?.Consequences?.LedgerOnly ?? false,
                                ForceClanEscalation = false,
                                SourceFactId = null,
                                FromRole = evt.RoleOf(hearer.HeroId) ?? (tk.IsAccused ? "accused" : "observer"),
                                ToRole = "originator",
                                Source = GrudgeSource.Rumor,
                                RequireHop0 = false,
                                ObserverHop = hearer.Hop,
                                TemplateAmount = grudgeScore,
                                HopConfidence = 1.0,
                                WitnessMultiplier = 1.0,
                                ObserverIsParticipant = tk.IsAccused,
                                FullAmount = grudgeScore,
                                AlreadyApplied = 0.0,
                                TraitMultiplier = 1.0,
                                RelationMultiplier = 1.0,
                                RelationReason = ReactionMultipliers.ReasonNone,
                                ReceiverHeroId = string.Empty
                            });

                            string whyKnows = hearer.Hop == 2 ? "heard directly from originator (hop 2)" : "heard originator credited (hop 3)";
                            if (tk.IsAccused && isGoodTalk)
                            {
                                ModLog.Info($"Honest praised accused grudge: {hearer.HeroId} (honor {honor}) resents originator {evt.OriginatorHeroId} of made-up praise: amount {requested:+0.##;-0.##;0} (half of {opinionScore:+0.##;-0.##;0}), {whyKnows} on {evt.EventId}");
                            }
                            else if (tk.IsAccused)
                            {
                                ModLog.Info($"Accused grudge: {hearer.HeroId} against originator {evt.OriginatorHeroId}: amount {grudgeScore:+0.##;-0.##;0} (1.5x of {opinionScore:+0.##;-0.##;0}), no daily cap, {whyKnows} on {evt.EventId}");
                            }
                            else
                            {
                                ModLog.Info($"Truth knower originator grudge: {hearer.HeroId} against originator {evt.OriginatorHeroId}: amount {requested:+0.##;-0.##;0} (half of {opinionScore:+0.##;-0.##;0}), {whyKnows} on {evt.EventId}");
                            }
                        }
                    }
                    else if (tk.IsAccused && isGoodTalk)
                    {
                        ModLog.Info($"Accused of praise talk: {hearer.HeroId} (honor {honor} < +1) disbelieves, does not grudge originator {evt.OriginatorHeroId} on {evt.EventId}");
                    }
                }
            }

            // 擲骰要不要出面
            if (config?.FalseRumors?.Enabled == true && tk.ResponseType != null && hearer.StepForward == null)
            {
                double? affectionTowardAccused = null;
                if (!tk.IsAccused && feelingWorld != null)
                {
                    string? accusedId = MadeUpTalk.GetAccusedHeroId(evt);
                    if (!string.IsNullOrEmpty(accusedId))
                    {
                        affectionTowardAccused = feelingWorld.Affection(hearer.HeroId, accusedId!);
                    }
                }

                var traitProfile = traits?.Of(hearer.HeroId);
                string responseCategory = tk.IsAccused ? "denial" : "clarification";
                double chance = StepForwardCalculator.CalculateChance(
                    responseCategory,
                    traitProfile,
                    affectionTowardAccused,
                    config?.FalseRumors,
                    out string formulaDesc);

                bool stepsForward;
                double rolledValue = 0.0;
                if (forceStepForward)
                {
                    stepsForward = true;
                    ModLog.Info($"step forward [forced]: {hearer.HeroId} on {evt.EventId} ({responseCategory}): {formulaDesc}, rolled [forced] => steps forward");
                }
                else
                {
                    stepsForward = StepForwardCalculator.Roll(chance, engine.CampaignSeed, evt.EventId, hearer.HeroId, BeliefRng, out rolledValue);
                    ModLog.Info($"step forward: {hearer.HeroId} on {evt.EventId} ({responseCategory}): {formulaDesc}, rolled {rolledValue:0.#} => {(stepsForward ? "steps forward" : "stays silent")}");
                }

                hearer.StepForwardChance = chance;
                hearer.StepForwardDay = day;
                beliefWritten = true;

                if (stepsForward)
                {
                    string? responseSettlementId = forcedSettlementId;
                    var stepperHero = heroLookup?.Get(hearer.HeroId);
                    if (responseSettlementId == null && stepperHero?.CurrentSettlement != null)
                    {
                        if (stepperHero.CurrentSettlement.IsTown || stepperHero.CurrentSettlement.IsCastle)
                        {
                            responseSettlementId = stepperHero.CurrentSettlement.StringId;
                        }
                    }

                    if (!string.IsNullOrEmpty(responseSettlementId))
                    {
                        string? responseId = (heroLookup != null && traits != null)
                            ? ResponseSender.SendResponse(evt, hearer, tk.ResponseType, day, store, heroLookup, traits, forcedSettlementId: responseSettlementId)
                            : null;
                        if (responseId != null)
                        {
                            hearer.StepForward = "done";
                            hearer.StepForwardEventId = responseId;
                            string forcedSuffix = (responseSettlementId == forcedSettlementId) ? " [forced]" : "";
                            string settlementName = stepperHero?.CurrentSettlement?.Name?.ToString() ?? responseSettlementId ?? "unknown";
                            ModLog.Info($"Truth knower {hearer.HeroId} stepped forward immediately with {tk.ResponseType} in {settlementName}{forcedSuffix} (chance {chance:F1}%) on {evt.EventId} -> response {responseId}");
                        }
                        else
                        {
                            hearer.StepForward = "expired";
                            hearer.StepForwardEventId = null;
                            ModLog.Info($"Truth knower {hearer.HeroId} stepped forward ({tk.ResponseType}), failed to send response on {evt.EventId}; marked expired");
                        }
                    }
                    else
                    {
                        hearer.StepForward = "waiting";
                        ModLog.Info($"Truth knower {hearer.HeroId} stepped forward (chance {chance:F1}%), not in town/castle; marked waiting on {evt.EventId}");
                    }
                }
                else
                {
                    hearer.StepForward = "ignored";
                    ModLog.Info($"Truth knower {hearer.HeroId} ignored stepping forward with {tk.ResponseType} (chance {chance:F1}%) on {evt.EventId}");
                }
            }
        }

        private static void LogSkippedForDisbelief(WorldEvent evt, KnownByEntry hearer, IReadOnlyList<OpinionDef> opinions)
        {
            var roles = new List<string>();
            var heroIds = new List<string>();
            var amounts = new List<double>();
            foreach (var def in opinions)
            {
                if (def == null) continue;
                string heroId = string.Empty;
                if (!string.IsNullOrEmpty(def.About) && evt.Participants != null)
                {
                    evt.Participants.TryGetValue(def.About, out var bound);
                    heroId = bound ?? string.Empty;
                }
                roles.Add(def.About);
                heroIds.Add(heroId);
                amounts.Add(def.Amount);
            }
            ModLog.Info(BeliefLogFormatter.FormatSkipped(hearer.HeroId, evt.EventId, roles, heroIds, amounts));
        }
    }
}
