using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;
using VividWorld.Core.Config;
using VividWorld.Core.Events;
using VividWorld.Core.Grudges;
using VividWorld.Core.Rumors;
using GameCampaign = TaleWorlds.CampaignSystem.Campaign;

namespace VividWorld.Campaign
{
    internal sealed class GrudgeRequest
    {
        public string FromHeroId { get; set; } = string.Empty;
        public string AboutHeroId { get; set; } = string.Empty;
        public double Requested { get; set; }
        public bool LedgerOnly { get; set; }
        public bool ForceClanEscalation { get; set; }
        public string? SourceFactId { get; set; }
        public string FromRole { get; set; } = string.Empty;
        public string ToRole { get; set; } = string.Empty;

        public GrudgeSource Source { get; set; } = GrudgeSource.Situation;
        public bool RequireHop0 { get; set; } = true;

        // 以下供日誌用（Source == Rumor 時有值）
        public int ObserverHop { get; set; }
        public double TemplateAmount { get; set; }
        public double HopConfidence { get; set; } = 1.0;
        public double WitnessMultiplier { get; set; } = 1.0;
        public bool ObserverIsParticipant { get; set; }
        public double FullAmount { get; set; }
        public double AlreadyApplied { get; set; }

        // 聽的人反應多大（已經乘進 FullAmount），日誌用
        public double TraitMultiplier { get; set; } = 1.0;
        public string? TraitName { get; set; }
        public int? TraitLevel { get; set; }
        public double RelationMultiplier { get; set; } = 1.0;
        public string RelationReason { get; set; } = ReactionMultipliers.ReasonNone;
        public string ReceiverHeroId { get; set; } = string.Empty;
    }

    internal static class GrudgeApplier
    {
        public static int AppliedThisSession { get; internal set; }
        public static int LedgerOnlyThisSession { get; internal set; }
        public static int EscalatedThisSession { get; internal set; }
        public static int SkippedThisSession { get; internal set; }
        public static int OpinionAppliedThisSession { get; internal set; }
        public static int OpinionLedgerOnlyThisSession { get; internal set; }
        public static int OpinionSkippedThisSession { get; internal set; }

        public static void ResetSessionCounters()
        {
            AppliedThisSession = 0;
            LedgerOnlyThisSession = 0;
            EscalatedThisSession = 0;
            SkippedThisSession = 0;
            OpinionAppliedThisSession = 0;
            OpinionLedgerOnlyThisSession = 0;
            OpinionSkippedThisSession = 0;
        }

        public static void Apply(
            WorldEvent evt,
            IReadOnlyList<GrudgeRequest> requests,
            double day,
            WorldEventStore eventStore,
            HeroLookup heroLookup,
            IHeroTraitLookup traitLookup,
            VividWorldConfig config,
            List<PlayerRelationNotice>? playerRelationNotices = null)
        {
            if (evt == null || requests == null || requests.Count == 0 || eventStore == null) return;

            // 1. 開關檢查分流
            bool allSituation = requests.All(r => r.Source == GrudgeSource.Situation);
            bool allRumor = requests.All(r => r.Source == GrudgeSource.Rumor);

            if (allSituation && config?.Situations?.GrudgesEnabled == false)
            {
                ModLog.Info(GrudgeLogFormatter.FormatDisabled(requests.Count));
                return;
            }

            if (allRumor && config?.Consequences?.Enabled == false)
            {
                ModLog.Info(GrudgeLogFormatter.FormatOpinionsDisabled(requests.Count));
                return;
            }

            string playerHeroId = eventStore.PlayerHeroId;
            var index = eventStore.Grudges;
            var cfg = config?.Situations ?? new SituationsConfig();

            bool anyModified = false;

            foreach (var req in requests)
            {
                if (req == null) continue;

                if (req.Source == GrudgeSource.Situation && config?.Situations?.GrudgesEnabled == false)
                {
                    SkippedThisSession++;
                    ModLog.Info(GrudgeLogFormatter.FormatDisabled(1));
                    continue;
                }
                if (req.Source == GrudgeSource.Rumor && config?.Consequences?.Enabled == false)
                {
                    OpinionSkippedThisSession++;
                    ModLog.Info(GrudgeLogFormatter.FormatOpinionsDisabled(1));
                    continue;
                }

                // 2. 檢查是否跳過：未綁定、同一人、出發者為玩家 => 跳過並記錄原因
                var skipDecision = GrudgeSkipRule.Evaluate(
                    req.Source,
                    req.FromHeroId,
                    req.AboutHeroId,
                    req.FromRole,
                    req.ToRole,
                    playerHeroId,
                    evt.EventId);

                if (skipDecision.ShouldSkip)
                {
                    if (req.Source == GrudgeSource.Rumor)
                    {
                        OpinionSkippedThisSession++;
                    }
                    else
                    {
                        SkippedThisSession++;
                    }

                    if (!string.IsNullOrEmpty(skipDecision.LogMessage))
                    {
                        ModLog.Info(skipDecision.LogMessage!);
                    }
                    continue;
                }

                // 3. 知情者查找支援 hop > 0
                KnownByEntry? knowerEntry;
                if (req.RequireHop0)
                {
                    knowerEntry = evt.KnownBy?.FirstOrDefault(k =>
                        string.Equals(k.HeroId, req.FromHeroId, StringComparison.Ordinal) && k.Hop == 0);
                    if (knowerEntry == null)
                    {
                        SkippedThisSession++;
                        ModLog.Info(GrudgeLogFormatter.FormatSkippedNotHop0(
                            req.FromRole, req.FromHeroId, req.ToRole, req.AboutHeroId, evt.EventId));
                        continue;
                    }
                }
                else
                {
                    knowerEntry = evt.EntryFor(req.FromHeroId);
                    if (knowerEntry == null)
                    {
                        OpinionSkippedThisSession++;
                        ModLog.Info(GrudgeLogFormatter.FormatOpinionSkipped(
                            req.FromHeroId, req.ToRole, evt.EventId,
                            $"not a knower of {evt.EventId}"));
                        continue;
                    }
                }

                // 4. 個人那一筆
                var targetDecision = EffectiveGrudgePairRule.Evaluate(
                    SubModule.PersonalWithPlayerEnabled, req.Source, req.AboutHeroId, playerHeroId);

                int delta = 0;
                List<string>? nativePair = null;
                int before = 0;
                int after = 0;

                var heroA = heroLookup.Get(req.FromHeroId);
                var heroB = heroLookup.Get(req.AboutHeroId);
                Hero? targetA = null;
                Hero? targetB = null;

                if (req.LedgerOnly)
                {
                    delta = 0;
                    nativePair = null;
                    if (req.Source == GrudgeSource.Rumor)
                    {
                        OpinionLedgerOnlyThisSession++;
                        ModLog.Info(GrudgeLogFormatter.FormatOpinionAppliedLedgerOnly(
                            req.FromHeroId, req.AboutHeroId, req.ToRole, evt.EventId,
                            req.TemplateAmount, req.ObserverHop, req.HopConfidence, req.ObserverIsParticipant, req.WitnessMultiplier,
                            req.FullAmount, req.AlreadyApplied, req.Requested,
                            GrudgeLogFormatter.FormatReactionSuffix(
                                req.TraitMultiplier, req.TraitName, req.TraitLevel,
                                req.RelationMultiplier, req.RelationReason, req.ReceiverHeroId)));
                    }
                    else
                    {
                        LedgerOnlyThisSession++;
                        ModLog.Info(GrudgeLogFormatter.FormatAppliedLedgerOnly(
                            req.FromRole, req.FromHeroId, req.ToRole, req.AboutHeroId, req.Requested));
                    }
                }
                else
                {
                    if (heroA == null || heroB == null)
                    {
                        string missingId = heroA == null ? req.FromHeroId : req.AboutHeroId;
                        if (req.Source == GrudgeSource.Rumor)
                        {
                            OpinionSkippedThisSession++;
                            ModLog.Info(GrudgeLogFormatter.FormatOpinionSkipped(
                                req.FromHeroId, req.ToRole, evt.EventId,
                                $"hero {missingId} not found"));
                        }
                        else
                        {
                            SkippedThisSession++;
                            ModLog.Info($"grudge skipped {req.FromRole} {req.FromHeroId} -> {req.ToRole} {req.AboutHeroId}: hero {missingId} not found");
                        }
                        continue;
                    }

                    targetA = heroA;
                    targetB = heroB;
                    bool isEffectivePair = false;

                    if (targetDecision.UseEffectivePair)
                    {
                        var diplomacyModel = GameCampaign.Current?.Models?.DiplomacyModel;
                        if (diplomacyModel != null)
                        {
                            diplomacyModel.GetHeroesForEffectiveRelation(heroA, heroB, out Hero e1, out Hero e2);
                            if (e1 != null && e2 != null)
                            {
                                targetA = e1;
                                targetB = e2;
                                isEffectivePair = true;
                            }
                            else
                            {
                                string nullHero = e1 == null ? (e2 == null ? "both e1 and e2" : "e1") : "e2";
                                ModLog.Info($"grudge {req.FromRole} {req.FromHeroId} -> {req.ToRole} {req.AboutHeroId}: effective hero {nullHero} was null, falling back to direct pair");
                            }
                        }
                        else
                        {
                            ModLog.Info($"grudge {req.FromRole} {req.FromHeroId} -> {req.ToRole} {req.AboutHeroId}: DiplomacyModel was null, falling back to direct pair");
                        }
                    }

                    before = targetA.GetBaseHeroRelation(targetB);
                    // 規格 §6.7.4 的三行形狀，不自己夾取：SetPersonalRelation 內部就會夾到
                    // DiplomacyModel 的 Min/MaxRelationLimit（帳本 D-03），而那個上限是執行期讀出來的。
                    // 寫死 ±100 會在別的模組把上限調高時，把本來就高於 100 的好感度反而拉低。
                    targetA.SetPersonalRelation(targetB, before + (int)Math.Round(req.Requested, MidpointRounding.AwayFromZero));
                    after = targetA.GetBaseHeroRelation(targetB);
                    delta = after - before;
                    nativePair = new List<string> { targetA.StringId, targetB.StringId };

                    bool pairDiffersFromDirect = !string.Equals(targetA.StringId, req.FromHeroId, StringComparison.Ordinal) ||
                                                 !string.Equals(targetB.StringId, req.AboutHeroId, StringComparison.Ordinal);

                    if (req.Source == GrudgeSource.Rumor)
                    {
                        OpinionAppliedThisSession++;
                        if (isEffectivePair && pairDiffersFromDirect)
                        {
                            ModLog.Info(GrudgeLogFormatter.FormatOpinionAppliedNativeOnEffectivePair(
                                req.FromHeroId, req.AboutHeroId, req.ToRole, evt.EventId,
                                req.TemplateAmount, req.ObserverHop, req.HopConfidence, req.ObserverIsParticipant, req.WitnessMultiplier,
                                req.FullAmount, req.AlreadyApplied, req.Requested,
                                before, after, delta,
                                targetA.StringId, targetB.StringId,
                                GrudgeLogFormatter.FormatReactionSuffix(
                                    req.TraitMultiplier, req.TraitName, req.TraitLevel,
                                    req.RelationMultiplier, req.RelationReason, req.ReceiverHeroId)));
                        }
                        else
                        {
                            ModLog.Info(GrudgeLogFormatter.FormatOpinionAppliedNative(
                                req.FromHeroId, req.AboutHeroId, req.ToRole, evt.EventId,
                                req.TemplateAmount, req.ObserverHop, req.HopConfidence, req.ObserverIsParticipant, req.WitnessMultiplier,
                                req.FullAmount, req.AlreadyApplied, req.Requested,
                                before, after, delta,
                                GrudgeLogFormatter.FormatReactionSuffix(
                                    req.TraitMultiplier, req.TraitName, req.TraitLevel,
                                    req.RelationMultiplier, req.RelationReason, req.ReceiverHeroId)));
                        }
                    }
                    else
                    {
                        AppliedThisSession++;
                        if (isEffectivePair && pairDiffersFromDirect)
                        {
                            ModLog.Info(GrudgeLogFormatter.FormatAppliedNativeOnEffectivePair(
                                req.FromRole, req.FromHeroId, req.ToRole, req.AboutHeroId, req.Requested, delta, before, after,
                                targetA.StringId, targetB.StringId));
                        }
                        else
                        {
                            ModLog.Info(GrudgeLogFormatter.FormatAppliedNative(
                                req.FromRole, req.FromHeroId, req.ToRole, req.AboutHeroId, req.Requested, delta, before, after));
                        }
                    }
                }

                string personalTargetAId = req.LedgerOnly ? req.FromHeroId : (targetA?.StringId ?? req.FromHeroId);
                string personalTargetBId = req.LedgerOnly ? req.AboutHeroId : (targetB?.StringId ?? req.AboutHeroId);

                var personalNotice = PlayerRelationNoticeRule.Evaluate(
                    req.Source,
                    req.LedgerOnly,
                    personalTargetAId,
                    personalTargetBId,
                    req.FromHeroId,
                    req.AboutHeroId,
                    playerHeroId,
                    delta);

                if (personalNotice.ShouldNotify)
                {
                    playerRelationNotices?.Add(personalNotice);
                }
                else if (!string.IsNullOrEmpty(personalNotice.SkipReason) &&
                         personalNotice.SkipReason != PlayerRelationNoticeRule.ReasonNotAboutPlayer)
                {
                    ModLog.Info(GrudgeLogFormatter.FormatPlayerRelationNoticeSkipped(personalNotice.OtherHeroId, personalNotice.SkipReason!));
                }

                knowerEntry.RelationImpacts ??= new List<RelationImpact>();
                knowerEntry.RelationImpacts.Add(new RelationImpact
                {
                    Source = req.Source,
                    Scope = GrudgeScope.Personal,
                    AboutHeroId = req.AboutHeroId,
                    Requested = req.Requested,
                    Delta = delta,
                    NativePair = nativePair,
                    LedgerOnly = req.LedgerOnly,
                    AppliedDay = day,
                    SourceFactId = req.SourceFactId
                });

                index.Note(new GrudgeEntry
                {
                    Source = req.Source,
                    EventId = evt.EventId,
                    FromHeroId = req.FromHeroId,
                    AboutHeroId = req.AboutHeroId,
                    Day = day,
                    Requested = req.Requested,
                    Delta = delta,
                    Scope = GrudgeScope.Personal,
                    LedgerOnly = req.LedgerOnly
                });

                anyModified = true;

                // 5. 升級：傳聞的好感度變化是純個人層次的印象，絕不升級為家族世仇
                if (req.Source == GrudgeSource.Rumor)
                {
                    continue;
                }

                if (!targetDecision.ShouldEvaluateClanEscalation)
                {
                    ModLog.Info(GrudgeLogFormatter.FormatEscalationNotEscalatedEffectivePair(req.FromHeroId, req.AboutHeroId));
                    continue;
                }

                var facts = GrudgeFactsReader.Read(heroA, heroB);
                var personalEntries = index.Between(req.FromHeroId, req.AboutHeroId, GrudgeScope.Personal);
                var traitProfile = traitLookup.Of(req.FromHeroId);
                double personalValueAfterDecay = GrudgeDecay.Replay(personalEntries, GrudgeScope.Personal, cfg, traitProfile, day).Value;

                bool alreadyEscalated = index.TryGetEscalation(req.FromHeroId, req.AboutHeroId, out string escEventId, out double escDay);
                var decision = GrudgeEscalation.Decide(facts, req.Requested, personalValueAfterDecay, req.ForceClanEscalation, alreadyEscalated, escEventId, escDay, cfg);

                if (!decision.Escalate)
                {
                    ModLog.Info(GrudgeLogFormatter.FormatEscalationNotEscalated(req.FromHeroId, req.AboutHeroId, decision.Reason));
                }
                else
                {
                    string leaderA = decision.LeaderA ?? string.Empty;
                    string leaderB = decision.LeaderB ?? string.Empty;
                    var heroLeaderA = heroLookup.Get(leaderA);
                    var heroLeaderB = heroLookup.Get(leaderB);

                    if (heroLeaderA == null || heroLeaderB == null)
                    {
                        string missingLeader = heroLeaderA == null ? leaderA : leaderB;
                        ModLog.Info($"escalation {req.FromHeroId} -> {req.AboutHeroId}: skipped clan relation apply, leader {missingLeader} not found");
                    }
                    else
                    {
                        EscalatedThisSession++;
                        int clanDelta = 0;
                        List<string>? clanNativePair = new List<string> { leaderA, leaderB };
                        int beforeClan = 0;
                        int afterClan = 0;

                        string effectiveReason = decision.Reason;
                        if (string.Equals(leaderA, req.FromHeroId, StringComparison.Ordinal) &&
                            string.Equals(leaderB, req.AboutHeroId, StringComparison.Ordinal))
                        {
                            effectiveReason += " (same pair as the personal grudge)";
                        }

                        if (req.LedgerOnly)
                        {
                            clanDelta = 0;
                            ModLog.Info(GrudgeLogFormatter.FormatEscalationEscalatedLedgerOnly(
                                req.FromHeroId, req.AboutHeroId, effectiveReason, leaderA, leaderB, decision.Amount));
                        }
                        else
                        {
                            beforeClan = heroLeaderA.GetBaseHeroRelation(heroLeaderB);
                            heroLeaderA.SetPersonalRelation(heroLeaderB, beforeClan + (int)Math.Round(decision.Amount, MidpointRounding.AwayFromZero));
                            afterClan = heroLeaderA.GetBaseHeroRelation(heroLeaderB);
                            clanDelta = afterClan - beforeClan;

                            ModLog.Info(GrudgeLogFormatter.FormatEscalationEscalatedNative(
                                req.FromHeroId, req.AboutHeroId, effectiveReason, leaderA, leaderB, decision.Amount, clanDelta, beforeClan, afterClan));
                        }

                        knowerEntry.RelationImpacts.Add(new RelationImpact
                        {
                            Scope = GrudgeScope.Clan,
                            AboutHeroId = leaderB,
                            Requested = decision.Amount,
                            Delta = clanDelta,
                            NativePair = clanNativePair,
                            LedgerOnly = req.LedgerOnly,
                            AppliedDay = day,
                            EscalatedFrom = $"{req.FromHeroId}|{req.AboutHeroId}",
                            SourceFactId = req.SourceFactId
                        });

                        var clanNotice = PlayerRelationNoticeRule.Evaluate(
                            req.Source,
                            req.LedgerOnly,
                            leaderA,
                            leaderB,
                            req.FromHeroId,
                            req.AboutHeroId,
                            playerHeroId,
                            clanDelta);

                        if (clanNotice.ShouldNotify)
                        {
                            playerRelationNotices?.Add(clanNotice);
                        }
                        else if (!string.IsNullOrEmpty(clanNotice.SkipReason) &&
                                 clanNotice.SkipReason != PlayerRelationNoticeRule.ReasonNotAboutPlayer)
                        {
                            ModLog.Info(GrudgeLogFormatter.FormatPlayerRelationNoticeSkipped(clanNotice.OtherHeroId, clanNotice.SkipReason!));
                        }

                        index.Note(new GrudgeEntry
                        {
                            EventId = evt.EventId,
                            FromHeroId = leaderA,
                            AboutHeroId = leaderB,
                            Day = day,
                            Requested = decision.Amount,
                            Delta = clanDelta,
                            Scope = GrudgeScope.Clan,
                            LedgerOnly = req.LedgerOnly,
                            EscalatedFrom = $"{req.FromHeroId}|{req.AboutHeroId}"
                        });
                    }
                }
            }

            // 6. 全部做完後 eventStore.Upsert(evt)
            if (anyModified)
            {
                eventStore.Upsert(evt);
            }
        }
    }
}
