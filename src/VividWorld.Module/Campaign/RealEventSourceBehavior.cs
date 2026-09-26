using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using VividWorld.Core.Catalog;
using VividWorld.Core.Config;
using VividWorld.Core.Events;
using VividWorld.Core.Ingest;
using VividWorld.Core.Rumors;

namespace VividWorld.Campaign
{
    internal sealed class RealEventSourceBehavior : CampaignBehaviorBase
    {
        private readonly VividWorldConfig _config;
        private WorldEventStore? _eventStore;
        private IHeroTraitLookup? _traitLookup;
        private int _eventsSubmittedThisSession;
        private int _whereKeptCount;
        private int _whereDroppedCount;
        private int _prominenceRulerCount;
        private int _prominenceClanLeaderCount;
        private int _prominenceNobleMemberCount;
        private int _prominenceMinorCount;
        private int _prominenceFailedCount;
        private int _releasesCount;
        private int _releaseEscapedCount;
        private int _releaseReleasedCount;
        private int _releaseLinkedCount;
        private int _releaseUnlinkedCount;

        public int EventsSubmittedThisSession => _eventsSubmittedThisSession;
        public int SubscribedCount => 5;
        public int WhereKeptCount => _whereKeptCount;
        public int WhereDroppedCount => _whereDroppedCount;
        public int ProminenceRulerCount => _prominenceRulerCount;
        public int ProminenceClanLeaderCount => _prominenceClanLeaderCount;
        public int ProminenceNobleMemberCount => _prominenceNobleMemberCount;
        public int ProminenceMinorCount => _prominenceMinorCount;
        public int ProminenceFailedCount => _prominenceFailedCount;
        public int ReleasesCount => _releasesCount;
        public int ReleaseEscapedCount => _releaseEscapedCount;
        public int ReleaseReleasedCount => _releaseReleasedCount;
        public int ReleaseLinkedCount => _releaseLinkedCount;
        public int ReleaseUnlinkedCount => _releaseUnlinkedCount;
        public int EntriesMarkedOutdatedCount => _eventStore?.Stamper?.EntriesMarkedOutdatedCount ?? 0;
        public int EventsDormantByOutdatingCount => _eventStore?.Stamper?.EventsDormantByOutdatingCount ?? 0;

        public RealEventSourceBehavior(VividWorldConfig config)
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));
        }

        public void Initialize(WorldEventStore eventStore, IHeroTraitLookup? traitLookup = null)
        {
            _eventStore = eventStore;
            _traitLookup = traitLookup;
        }

        public override void RegisterEvents()
        {
            CampaignEvents.HeroKilledEvent.AddNonSerializedListener(this, OnHeroKilled);
            CampaignEvents.HeroPrisonerTaken.AddNonSerializedListener(this, OnHeroPrisonerTaken);
            CampaignEvents.BeforeHeroesMarried.AddNonSerializedListener(this, OnHeroesMarried);
            CampaignEvents.OnGivenBirthEvent.AddNonSerializedListener(this, OnGivenBirth);
            CampaignEvents.HeroPrisonerReleased.AddNonSerializedListener(this, OnHeroPrisonerReleased);
            ModLog.Info("RealEventSourceBehavior.RegisterEvents: subscribed to 5 campaign events.");
        }

        public override void SyncData(IDataStore dataStore)
        {
            // No custom persistent state needed for real event hook listeners
        }

        private void OnHeroKilled(Hero victim, Hero killer, KillCharacterAction.KillCharacterActionDetail detail, bool showNotification)
        {
            try
            {
                if (_eventStore == null) return;
                if (!_config.Events.Sources.HeroKilled) return;
                if (victim == null) return;

                string? templateType = RealEventMapping.TemplateForKill((int)detail);
                if (templateType == null)
                {
                    string killerStr = killer != null ? killer.StringId : "none";
                    ModLog.Info($"RealEventSource: HeroKilled detail={detail} -> no template, skipped (victim={victim.StringId}, killer={killerStr})");
                    return;
                }

                bool isSecret = string.Equals(templateType, "hero_murdered", StringComparison.OrdinalIgnoreCase);
                ModLog.Info($"RealEventSource: HeroKilled detail={detail} -> template '{templateType}' ({(isSecret ? "secret" : "public")})");

                if (!string.Equals(templateType, "hero_died_naturally", StringComparison.OrdinalIgnoreCase) && killer == null)
                {
                    ModLog.Info($"RealEventSource: HeroKilled template '{templateType}' requires killer but killer is null, skipped (victim={victim.StringId})");
                    return;
                }

                var bindings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["VICTIM"] = victim.StringId
                };
                if (killer != null)
                {
                    bindings["KILLER"] = killer.StringId;
                }

                var probes = new List<SettlementProbe>();
                probes.AddRange(ProbesForHero("victim", victim));
                if (killer != null)
                {
                    probes.AddRange(ProbesForHero("killer", killer));
                }

                var fallbackResult = SettlementFallback.Resolve(probes);
                if (!string.IsNullOrEmpty(fallbackResult.SettlementId))
                {
                    bindings["SETTLEMENT"] = fallbackResult.SettlementId!;
                }

                TrySubmit(templateType, bindings, $"HeroKilled detail={detail}", fallbackResult);
            }
            catch (Exception ex)
            {
                ModLog.Error("RealEventSourceBehavior.OnHeroKilled encountered an exception", ex);
            }
        }

        private void OnHeroPrisonerTaken(PartyBase capturer, Hero prisoner)
        {
            try
            {
                if (_eventStore == null) return;
                if (!_config.Events.Sources.HeroPrisonerTaken) return;
                if (prisoner == null) return;

                Hero? captorHero = capturer?.LeaderHero ?? capturer?.Owner;
                if (captorHero == null)
                {
                    string capturerId = capturer?.Id ?? "none";
                    ModLog.Info($"RealEventSource: HeroPrisonerTaken - capturer party has no leader and no owner, skipped (prisoner={prisoner.StringId}, capturer={capturerId})");
                    return;
                }

                var bindings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["CAPTOR"] = captorHero.StringId,
                    ["PRISONER"] = prisoner.StringId
                };

                var probes = new List<SettlementProbe>();
                probes.AddRange(ProbesForHero("prisoner", prisoner));
                if (capturer != null && capturer.IsSettlement)
                {
                    probes.Add(new SettlementProbe("capturer.Settlement", () => capturer.Settlement?.StringId));
                }
                if (captorHero != null)
                {
                    probes.AddRange(ProbesForHero("captor", captorHero));
                }

                var fallbackResult = SettlementFallback.Resolve(probes);
                if (!string.IsNullOrEmpty(fallbackResult.SettlementId))
                {
                    bindings["SETTLEMENT"] = fallbackResult.SettlementId!;
                }

                ProminenceResult? prominenceResult = null;
                Exception? prominenceException = null;
                try
                {
                    var facts = new ProminenceFacts
                    {
                        HeroId = prisoner.StringId,
                        IsKingdomLeader = prisoner.IsKingdomLeader,
                        KingdomId = prisoner.MapFaction?.StringId,
                        IsClanLeader = prisoner.IsClanLeader,
                        ClanId = prisoner.Clan?.StringId,
                        ClanIsMinorFaction = prisoner.Clan?.IsMinorFaction ?? false,
                        IsLord = prisoner.IsLord
                    };
                    prominenceResult = PrisonerProminence.Classify(facts, _config.Events.PrisonerDramaByProminence);
                }
                catch (Exception ex)
                {
                    prominenceException = ex;
                }

                TrySubmit(
                    "hero_taken_prisoner",
                    bindings,
                    "HeroPrisonerTaken",
                    fallbackResult,
                    prominenceResult,
                    prominenceException,
                    prisoner.StringId);
            }
            catch (Exception ex)
            {
                ModLog.Error("RealEventSourceBehavior.OnHeroPrisonerTaken encountered an exception", ex);
            }
        }

        private void OnHeroPrisonerReleased(Hero prisoner, PartyBase party, IFaction faction, TaleWorlds.CampaignSystem.Actions.EndCaptivityDetail detail, bool showNotification)
        {
            try
            {
                if (_eventStore == null) return;
                if (!_config.Events.Sources.HeroPrisonerReleased) return;
                if (prisoner == null) return;

                string? templateType = RealEventMapping.TemplateForRelease((int)detail);
                if (templateType == null)
                {
                    ModLog.Info($"RealEventSource: HeroPrisonerReleased detail={detail} -> no template, skipped (prisoner={prisoner.StringId})");
                    return;
                }

                Hero? captorHero = party?.LeaderHero ?? party?.Owner;
                string? captorId = captorHero?.StringId;

                ModLog.Info(OutdatingLogFormatter.FormatReleaseHeader(detail.ToString(), templateType, prisoner.StringId, captorId));

                var bindings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["PRISONER"] = prisoner.StringId
                };
                if (captorHero != null)
                {
                    bindings["CAPTOR"] = captorHero.StringId;
                }

                var probes = new List<SettlementProbe>();
                probes.AddRange(ProbesForHero("prisoner", prisoner));
                if (party != null && party.IsSettlement)
                {
                    probes.Add(new SettlementProbe("party.Settlement", () => party.Settlement?.StringId));
                }
                if (captorHero != null)
                {
                    probes.AddRange(ProbesForHero("captor", captorHero));
                }

                var fallbackResult = SettlementFallback.Resolve(probes);
                if (!string.IsNullOrEmpty(fallbackResult.SettlementId))
                {
                    bindings["SETTLEMENT"] = fallbackResult.SettlementId!;
                }

                ProminenceResult? prominenceResult = null;
                Exception? prominenceException = null;
                try
                {
                    var facts = new ProminenceFacts
                    {
                        HeroId = prisoner.StringId,
                        IsKingdomLeader = prisoner.IsKingdomLeader,
                        KingdomId = prisoner.MapFaction?.StringId,
                        IsClanLeader = prisoner.IsClanLeader,
                        ClanId = prisoner.Clan?.StringId,
                        ClanIsMinorFaction = prisoner.Clan?.IsMinorFaction ?? false,
                        IsLord = prisoner.IsLord
                    };
                    prominenceResult = PrisonerProminence.Classify(facts, _config.Events.ReleaseDramaByProminence);
                }
                catch (Exception ex)
                {
                    prominenceException = ex;
                }

                var catalog = EventCatalogStore.Catalog;
                var baseTemplate = catalog.ByType(templateType);
                int templateDrama = baseTemplate?.DramaWeight ?? 4;

                if (prominenceResult != null)
                {
                    ModLog.Info($"  prominence: {prominenceResult.Describe(templateDrama)}");
                }
                else if (prominenceException != null)
                {
                    ModLog.Info($"  prominence: prisoner={prisoner.StringId} failed ({prominenceException.GetType().Name}) => drama {templateDrama} (template {templateDrama}, not overridden)");
                }

                double day = CampaignTime.Now.ToDays;
                var captureEntry = CaptureLookup.FindLatestCapture(_eventStore.Index, prisoner.StringId, day);
                string? linkedEventId = captureEntry?.EventId;
                if (captureEntry != null)
                {
                    ModLog.Info(OutdatingLogFormatter.FormatLinkedCapture(captureEntry.EventId, captureEntry.Type, captureEntry.Day, day));
                    _releaseLinkedCount++;
                }
                else
                {
                    int checkedCount = _eventStore.Index?.Entries?.Count ?? 0;
                    ModLog.Info(OutdatingLogFormatter.FormatLinkedNone(prisoner.StringId, checkedCount));
                    _releaseUnlinkedCount++;
                }

                EventTemplate? adaptedTemplate = null;
                if (captorHero == null && baseTemplate != null)
                {
                    string whoNoCaptorTextId = string.Equals(templateType, "hero_escaped_captivity", StringComparison.Ordinal)
                        ? "VividWorld_Fact_HeroEscaped_WhoNoCaptor"
                        : "VividWorld_Fact_HeroReleased_WhoNoCaptor";

                    adaptedTemplate = new EventTemplate
                    {
                        Type = baseTemplate.Type,
                        Origin = baseTemplate.Origin,
                        DramaWeight = baseTemplate.DramaWeight,
                        LinkedTemplateType = baseTemplate.LinkedTemplateType,
                        Roles = new Dictionary<string, string> { ["prisoner"] = "{PRISONER}" },
                        KnowingRoles = new HashSet<string>(baseTemplate.KnowingRoles),
                        Facts = baseTemplate.Facts.Select(f =>
                        {
                            if (string.Equals(f.Id, "who", StringComparison.OrdinalIgnoreCase))
                            {
                                return new TemplateFact
                                {
                                    Id = f.Id,
                                    Category = f.Category,
                                    TextId = whoNoCaptorTextId,
                                    Text = f.Text,
                                    Vars = new Dictionary<string, string> { ["PRISONER"] = "hero:{PRISONER}" },
                                    Fragility = f.Fragility,
                                    Optional = f.Optional
                                };
                            }
                            return new TemplateFact
                            {
                                Id = f.Id,
                                Category = f.Category,
                                TextId = f.TextId,
                                Text = f.Text,
                                Vars = f.Vars != null ? new Dictionary<string, string>(f.Vars) : new Dictionary<string, string>(),
                                Fragility = f.Fragility,
                                Optional = f.Optional
                            };
                        }).ToList()
                    };
                }

                TrySubmit(
                    templateType,
                    bindings,
                    $"HeroPrisonerReleased detail={detail}",
                    fallbackResult,
                    prominenceResult,
                    prominenceException,
                    prisoner.StringId,
                    linkedEventId,
                    adaptedTemplate,
                    skipProminenceLog: true);

                _releasesCount++;
                if (detail == TaleWorlds.CampaignSystem.Actions.EndCaptivityDetail.ReleasedAfterEscape)
                {
                    _releaseEscapedCount++;
                }
                else
                {
                    _releaseReleasedCount++;
                }
            }
            catch (Exception ex)
            {
                ModLog.Error("RealEventSourceBehavior.OnHeroPrisonerReleased encountered an exception", ex);
            }
        }

        private void OnHeroesMarried(Hero hero1, Hero hero2, bool showNotification)
        {
            try
            {
                if (_eventStore == null) return;
                if (!_config.Events.Sources.HeroesMarried) return;
                if (hero1 == null || hero2 == null)
                {
                    string h1 = hero1?.StringId ?? "none";
                    string h2 = hero2?.StringId ?? "none";
                    ModLog.Info($"RealEventSource: BeforeHeroesMarried - one or both heroes are null, skipped (hero1={h1}, hero2={h2})");
                    return;
                }

                var bindings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["SPOUSE_A"] = hero1.StringId,
                    ["SPOUSE_B"] = hero2.StringId
                };

                var probes = new List<SettlementProbe>();
                probes.AddRange(ProbesForHero("spouse_a", hero1));
                probes.AddRange(ProbesForHero("spouse_b", hero2));

                var fallbackResult = SettlementFallback.Resolve(probes);
                if (!string.IsNullOrEmpty(fallbackResult.SettlementId))
                {
                    bindings["SETTLEMENT"] = fallbackResult.SettlementId!;
                }

                TrySubmit("heroes_married", bindings, "BeforeHeroesMarried", fallbackResult);
            }
            catch (Exception ex)
            {
                ModLog.Error("RealEventSourceBehavior.OnHeroesMarried encountered an exception", ex);
            }
        }

        private void OnGivenBirth(Hero mother, List<Hero> aliveChildren, int stillbornCount)
        {
            try
            {
                if (_eventStore == null) return;
                if (!_config.Events.Sources.ChildBorn) return;
                if (mother == null) return;

                if (aliveChildren == null || aliveChildren.Count == 0)
                {
                    ModLog.Info($"RealEventSource: OnGivenBirth - no alive children, skipped (mother={mother.StringId}, stillborn={stillbornCount})");
                    return;
                }

                Hero child = aliveChildren[0];
                if (child == null) return;

                // 多胞胎只講第一個。這是取捨不是遺漏，但要印出來——
                // 不印的話「為什麼雙胞胎只有一個被談論」永遠查不出來。
                if (aliveChildren.Count > 1)
                {
                    ModLog.Info($"RealEventSource: OnGivenBirth - {aliveChildren.Count} alive children, only the first ({child.StringId}) is used as CHILD");
                }
                if (stillbornCount > 0)
                {
                    ModLog.Info($"RealEventSource: OnGivenBirth - {stillbornCount} stillborn not reported as events");
                }

                var bindings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["MOTHER"] = mother.StringId,
                    ["CHILD"] = child.StringId
                };

                var probes = new List<SettlementProbe>();
                probes.AddRange(ProbesForHero("mother", mother));
                probes.AddRange(ProbesForHero("child", child));

                var fallbackResult = SettlementFallback.Resolve(probes);
                if (!string.IsNullOrEmpty(fallbackResult.SettlementId))
                {
                    bindings["SETTLEMENT"] = fallbackResult.SettlementId!;
                }

                TrySubmit("child_born", bindings, "OnGivenBirth", fallbackResult);
            }
            catch (Exception ex)
            {
                ModLog.Error("RealEventSourceBehavior.OnGivenBirth encountered an exception", ex);
            }
        }

        private static IEnumerable<SettlementProbe> ProbesForHero(string role, Hero? hero)
        {
            if (hero == null) yield break;
            yield return new SettlementProbe($"{role}.CurrentSettlement", () => hero.CurrentSettlement?.StringId);
            yield return new SettlementProbe($"{role}.GetClosestSettlement", () => Helpers.HeroHelper.GetClosestSettlement(hero)?.StringId);
        }

        private void TrySubmit(
            string templateType,
            Dictionary<string, string> bindings,
            string sourceTag,
            SettlementFallbackResult? fallbackResult = null,
            ProminenceResult? prominenceResult = null,
            Exception? prominenceException = null,
            string? prisonerIdForProminence = null,
            string? linkedEventId = null,
            EventTemplate? templateOverride = null,
            bool skipProminenceLog = false)
        {
            var catalog = EventCatalogStore.Catalog;
            var template = templateOverride ?? catalog.ByType(templateType);
            if (template == null)
            {
                ModLog.Warn($"RealEventSource: Template '{templateType}' not found in catalog ({sourceTag}).");
                return;
            }

            double day = CampaignTime.Now.ToDays;
            var submission = TemplateBinder.Bind(template, bindings, day, linkedEventId, out var bindIssues);
            if (submission == null)
            {
                string issueDetails = string.Join("; ", bindIssues.Select(iss => $"{iss.Field}: {iss.Detail}"));
                ModLog.Warn($"RealEventSource: Failed to bind template '{templateType}' ({sourceTag}): {issueDetails}");
                return;
            }

            if (prominenceResult != null)
            {
                submission.DramaWeight = prominenceResult.Drama;
            }

            var result = _eventStore!.Submit(submission, out string? eventId, out string? rejectionReason);
            if (result != IngestResult.Accepted || string.IsNullOrEmpty(eventId))
            {
                ModLog.Warn($"RealEventSource: Failed to submit template '{templateType}' ({sourceTag}): {result} - {rejectionReason}");
                return;
            }

            _eventsSubmittedThisSession++;

            // Only track prisoner prominence for hero_taken_prisoner
            if (string.Equals(templateType, "hero_taken_prisoner", StringComparison.Ordinal))
            {
                if (prominenceResult != null)
                {
                    switch (prominenceResult.Tier)
                    {
                        case ProminenceTier.Ruler:
                            _prominenceRulerCount++;
                            break;
                        case ProminenceTier.ClanLeader:
                            _prominenceClanLeaderCount++;
                            break;
                        case ProminenceTier.NobleMember:
                            _prominenceNobleMemberCount++;
                            break;
                        case ProminenceTier.Minor:
                            _prominenceMinorCount++;
                            break;
                    }
                }
                else if (prominenceException != null)
                {
                    _prominenceFailedCount++;
                }
            }

            var boundParts = new List<string>();
            foreach (var kvp in bindings)
            {
                if (string.Equals(kvp.Key, "SETTLEMENT", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
                boundParts.Add($"{kvp.Key}={kvp.Value}");
            }
            if (fallbackResult != null)
            {
                boundParts.Add(fallbackResult.Describe());
            }
            string boundVarsStr = string.Join(", ", boundParts);
            ModLog.Info($"RealEventSource: bound {templateType} as {eventId} ({boundVarsStr})");

            // 模板沒寫 dramaWeight 時由 WorldEventStore 依設定解析，這裡不能自己猜一個數字（原本寫死 4）。
            if (prominenceResult != null && !skipProminenceLog)
            {
                ModLog.Info($"  prominence: {prominenceResult.Describe(template.DramaWeight)}");
            }
            else if (prominenceException != null && !skipProminenceLog)
            {
                string pid = !string.IsNullOrEmpty(prisonerIdForProminence) ? prisonerIdForProminence! : "unknown";
                string resolved = _eventStore.Load(eventId!)?.DramaWeight.ToString(CultureInfo.InvariantCulture) ?? "?";
                string templateStr = template.DramaWeight?.ToString(CultureInfo.InvariantCulture) ?? "unset";
                ModLog.Info($"  prominence: prisoner={pid} failed ({prominenceException.GetType().Name}) => drama {resolved} (template {templateStr}, not overridden)");
            }

            var submittedFactIds = new HashSet<string>(submission.Facts.Select(f => f.Id), StringComparer.Ordinal);
            foreach (var tf in template.Facts)
            {
                if (submittedFactIds.Contains(tf.Id))
                {
                    if (tf.Optional)
                    {
                        if (string.Equals(tf.Id, "where", StringComparison.OrdinalIgnoreCase))
                        {
                            _whereKeptCount++;
                        }
                        ModLog.Info($"  fact '{tf.Id}' kept");
                    }
                }
                else
                {
                    if (string.Equals(tf.Id, "where", StringComparison.OrdinalIgnoreCase))
                    {
                        _whereDroppedCount++;
                    }
                    var why = bindIssues.FirstOrDefault(iss =>
                        !string.IsNullOrEmpty(iss.Detail) && iss.Detail.Contains($"'{tf.Id}'"));
                    string reason = why != null ? $"{why.Field}: {why.Detail}" : "reason not reported by the binder";
                    ModLog.Info($"  fact '{tf.Id}' dropped - {reason}");
                }
            }

            // §4.1: hop 0 摘要
            try
            {
                LogHop0Summary(template, submission, eventId!);
            }
            catch (Exception ex)
            {
                ModLog.Error($"RealEventSource: Failed to format hop0 summary for {eventId}", ex);
            }
        }

        private void LogHop0Summary(EventTemplate template, EventSubmission submission, string eventId)
        {
            Hop0SummaryLogger.LogHop0Summary(template, submission, eventId, _eventStore, _traitLookup);
        }
    }
}
