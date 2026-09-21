using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using VividWorld.Campaign;
using VividWorld.Core.Catalog;
using VividWorld.Core.Config;
using VividWorld.Core.Events;
using VividWorld.Core.Ingest;
using VividWorld.Core.Rumors;
using VividWorld.Core.Util;

namespace VividWorld.Debug
{
    internal sealed class FakeEventProducerBehavior : CampaignBehaviorBase
    {
        private readonly VividWorldConfig _config;
        private WorldEventStore? _eventStore;
        private HeroLookup? _heroLookup;
        private IHeroTraitLookup? _traits;
        private bool _ready;

        private int _templateIndex = 0;
        private string _lastDuelArrangedEventId = string.Empty;
        private string _lastTavernQuarrelEventId = string.Empty;

        public FakeEventProducerBehavior(VividWorldConfig config)
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));
        }

        public void Initialize(WorldEventStore eventStore, HeroLookup heroLookup, IHeroTraitLookup traits)
        {
            _eventStore = eventStore ?? throw new ArgumentNullException(nameof(eventStore));
            _heroLookup = heroLookup ?? throw new ArgumentNullException(nameof(heroLookup));
            _traits = traits ?? throw new ArgumentNullException(nameof(traits));
            _ready = true;
        }

        public override void RegisterEvents()
        {
            CampaignEvents.DailyTickEvent.AddNonSerializedListener(this, OnDailyTick);
            ModLog.Info("FakeEventProducerBehavior.RegisterEvents: subscribed to DailyTickEvent.");
        }

        public override void SyncData(IDataStore dataStore)
        {
            dataStore.SyncData("VividWorld_FakeProducerIndex", ref _templateIndex);
            dataStore.SyncData("VividWorld_LastDuelArrangedId", ref _lastDuelArrangedEventId);
            dataStore.SyncData("VividWorld_LastTavernQuarrelId", ref _lastTavernQuarrelEventId);
        }

        private void OnDailyTick()
        {
            if (!_ready)
            {
                ModLog.Warn("FakeEventProducer: daily tick before Initialize - skipped.");
                return;
            }
            if (!_config.Debug.FakeProducerEnabled)
            {
                if (_config.Debug.VerboseTickLog) ModLog.Info("FakeEventProducer: disabled by config.");
                return;
            }

            try
            {
                double perDay = Math.Max(0.0, _config.Debug.FakeProducerEventsPerDay);
                int count = (int)Math.Floor(perDay);
                if (MBRandom.RandomFloat < (perDay - count)) count++;

                const int HardCapPerDay = 100;
                if (count > HardCapPerDay)
                {
                    ModLog.Warn($"FakeEventProducer: eventsPerDay={perDay} clamped to {HardCapPerDay} for this tick.");
                    count = HardCapPerDay;
                }

                if (count == 0)
                {
                    if (_config.Debug.VerboseTickLog)
                    {
                        ModLog.Info($"FakeEventProducer: rolled 0 events today (eventsPerDay={perDay}).");
                    }
                    return;
                }

                var catalog = (EventCatalogStore.SampleCatalog != null && EventCatalogStore.SampleCatalog.Templates.Count > 0)
                    ? EventCatalogStore.SampleCatalog
                    : EventCatalogStore.Catalog;
                var templates = catalog.Templates;
                if (templates == null || templates.Count == 0)
                {
                    ModLog.Warn($"FakeEventProducer: template list is empty at {(EventCatalogStore.SampleCatalog?.Templates.Count > 0 ? EventCatalogStore.SampleFilePath : EventCatalogStore.ActiveFilePath)} - nothing to inject.");
                    return;
                }

                var candidateSettlements = Settlement.All
                    .Where(s => s.IsTown || s.IsCastle || s.IsVillage)
                    .OrderBy(_ => MBRandom.RandomInt())
                    .ToList();

                int scannedCount = candidateSettlements.Count;
                int bestEligibleCount = 0;
                int bestPresentCount = 0;
                var eligibleSettlementPairs = new List<(Settlement Settlement, List<Hero> EligibleHeroes, List<Hero> PresentHeroes)>();
                var totalRejections = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

                foreach (var s in candidateSettlements)
                {
                    var (eligible, present, rejections) = AnalyzeSettlementHeroes(s);
                    if (eligible.Count > bestEligibleCount)
                    {
                        bestEligibleCount = eligible.Count;
                        bestPresentCount = present.Count;
                    }
                    else if (bestEligibleCount == 0 && present.Count > bestPresentCount)
                    {
                        bestPresentCount = present.Count;
                    }

                    foreach (var kvp in rejections)
                    {
                        totalRejections.TryGetValue(kvp.Key, out int c);
                        totalRejections[kvp.Key] = c + kvp.Value;
                    }

                    if (eligible.Count >= 2)
                    {
                        eligibleSettlementPairs.Add((s, eligible, present));
                    }
                }

                if (eligibleSettlementPairs.Count == 0)
                {
                    int totalRejected = totalRejections.Values.Sum();
                    string rejectionDetails = string.Join(", ", totalRejections
                        .Where(kvp => kvp.Value > 0)
                        .OrderByDescending(kvp => kvp.Value)
                        .Select(kvp => $"{kvp.Key} {kvp.Value}"));

                    ModLog.Info($"FakeEventProducer: no settlement with >=2 eligible heroes\n  (scanned {scannedCount} settlements, best had {bestEligibleCount} eligible of {bestPresentCount} present; {totalRejected} present heroes rejected: {rejectionDetails})");
                    return;
                }

                for (int i = 0; i < count; i++)
                {
                    var (chosenSettlement, chosenHeroes, presentHeroes) = eligibleSettlementPairs[i % eligibleSettlementPairs.Count];
                    var template = templates[_templateIndex % templates.Count];
                    _templateIndex++;

                    Hero mastermind = chosenHeroes[0];
                    Hero target = chosenHeroes[1];
                    Hero? agent = chosenHeroes.Count > 2 ? chosenHeroes[2] : null;

                    if (agent == null)
                    {
                        var pool = TaleWorlds.CampaignSystem.Campaign.Current.AliveHeroes
                            .Where(h => h != mastermind && h != target && IsUsableAsTestSubject(h)
                                        && !h.IsPrisoner && !h.IsChild
                                        && _traits != null && Eligibility.IsEligible(_traits, h.StringId))
                            .ToList();
                        agent = pool.Count > 0 ? pool[MBRandom.RandomInt(pool.Count)] : null;
                    }

                    string agentId = agent?.StringId ?? target.StringId;

                    var bindings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["MASTERMIND"] = mastermind.StringId,
                        ["TARGET"] = target.StringId,
                        ["AGENT"] = agentId,
                        ["SETTLEMENT"] = chosenSettlement.StringId
                    };

                    string? linkedEventId = null;
                    if (string.Equals(template.LinkedTemplateType, "duel_arranged", StringComparison.OrdinalIgnoreCase))
                    {
                        linkedEventId = _lastDuelArrangedEventId;
                    }
                    else if (string.Equals(template.LinkedTemplateType, "tavern_quarrel", StringComparison.OrdinalIgnoreCase))
                    {
                        linkedEventId = _lastTavernQuarrelEventId;
                    }

                    var submission = TemplateBinder.Bind(template, bindings, CampaignTime.Now.ToDays, linkedEventId, out var bindIssues);
                    if (submission == null)
                    {
                        string issueDetails = string.Join("; ", bindIssues.Select(iss => $"{iss.Field}: {iss.Detail}"));
                        ModLog.Warn($"FakeEventProducer: Failed to bind template {template.Type}: {issueDetails}");
                        continue;
                    }

                    var result = _eventStore!.Submit(submission, out string? eventId, out string? rejectionReason);

                    if (result == IngestResult.Accepted && !string.IsNullOrEmpty(eventId))
                    {
                        if (template.Type == "duel_arranged")
                        {
                            _lastDuelArrangedEventId = eventId!;
                        }
                        else if (template.Type == "tavern_quarrel")
                        {
                            _lastTavernQuarrelEventId = eventId!;
                        }

                        string boundVarsStr;
                        if (template.Roles.ContainsKey("agent") || template.Type == "covert_sabotage")
                        {
                            boundVarsStr = $"MASTERMIND={mastermind.StringId}, TARGET={target.StringId}, AGENT={agentId}, SETTLEMENT={chosenSettlement.StringId}";
                        }
                        else
                        {
                            boundVarsStr = $"MASTERMIND={mastermind.StringId}, TARGET={target.StringId}, SETTLEMENT={chosenSettlement.StringId}";
                        }
                        ModLog.Info($"FakeEventProducer: bound {template.Type} as {eventId} ({boundVarsStr})");

                        var evt = _eventStore.Load(eventId!);
                        var participantIds = submission.Participants.Values.Where(v => !string.IsNullOrEmpty(v)).Distinct().ToList();
                        var hop0Entries = evt?.KnownBy?.Where(k => k.Hop == 0).ToList() ?? new List<KnownByEntry>();
                        var hop0HeroIds = hop0Entries.Select(k => k.HeroId).Where(id => !string.IsNullOrEmpty(id)).ToHashSet();

                        var pKnowers = participantIds.Where(id => hop0HeroIds.Contains(id)).ToList();
                        var witnessEntries = hop0Entries.Where(k => !participantIds.Contains(k.HeroId)).ToList();

                        string witnessPart;
                        if (template.Origin == EventOrigin.Secret)
                        {
                            witnessPart = "0 witnesses (secret)";
                        }
                        else
                        {
                            string? playerHeroId = Hero.MainHero?.StringId;
                            var nonParticipantPresent = presentHeroes
                                .Where(h => !participantIds.Contains(h.StringId) && h.StringId != playerHeroId)
                                .ToList();

                            var witnessRejections = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                            foreach (var h in nonParticipantPresent)
                            {
                                if (!hop0HeroIds.Contains(h.StringId))
                                {
                                    string reason = GetRejectionReason(h) ?? "max witnesses cap";
                                    witnessRejections.TryGetValue(reason, out int rc);
                                    witnessRejections[reason] = rc + 1;
                                }
                            }

                            int rejectedCount = witnessRejections.Values.Sum();
                            string rejectionStr;
                            if (witnessRejections.Count == 1)
                            {
                                var firstKvp = witnessRejections.First();
                                rejectionStr = $"{rejectedCount} rejected: {firstKvp.Key}";
                            }
                            else if (witnessRejections.Count > 1)
                            {
                                rejectionStr = $"{rejectedCount} rejected: " + string.Join(", ", witnessRejections.Select(kvp => $"{kvp.Key} {kvp.Value}"));
                            }
                            else
                            {
                                rejectionStr = "0 rejected";
                            }

                            witnessPart = $"{witnessEntries.Count} witnesses of {nonParticipantPresent.Count} present ({rejectionStr})";
                        }

                        ModLog.Info($"FakeEventProducer: Injected {eventId} ({template.Type}) at {chosenSettlement.Name}\n  hop0 knowers: {pKnowers.Count} participants ({string.Join(", ", pKnowers)}) + {witnessPart}");
                    }
                    else
                    {
                        ModLog.Warn($"FakeEventProducer: Failed to inject event ({template.Type}): result={result}, reason={rejectionReason}");
                    }
                }
            }
            catch (Exception ex)
            {
                ModLog.Error("Error in FakeEventProducerBehavior.OnDailyTick", ex);
            }
        }

        private List<Hero> GetEligibleHeroesAtSettlement(Settlement settlement)
        {
            var (eligible, _, _) = AnalyzeSettlementHeroes(settlement);
            return eligible;
        }

        private (List<Hero> Eligible, List<Hero> Present, Dictionary<string, int> Rejections) AnalyzeSettlementHeroes(Settlement settlement)
        {
            var present = GetPresentHeroesAtSettlement(settlement);
            var eligible = new List<Hero>();
            var rejections = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            foreach (var h in present)
            {
                string? reason = GetRejectionReason(h);
                if (reason == null)
                {
                    eligible.Add(h);
                }
                else
                {
                    rejections.TryGetValue(reason, out int count);
                    rejections[reason] = count + 1;
                }
            }

            return (eligible, present, rejections);
        }

        private string? GetRejectionReason(Hero h)
            => EligibilityLabel.GetRejectionReason(h, _traits);

        private static bool IsUsableAsTestSubject(Hero? h)
            => EligibilityLabel.IsUsableAsTestSubject(h);

        private List<Hero> GetPresentHeroesAtSettlement(Settlement settlement)
            => EligibilityLabel.GetPresentHeroesAtSettlement(settlement);
    }
}
