using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.CharacterDevelopment;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Library;
using VividWorld.Core.Catalog;
using VividWorld.Core.Config;
using VividWorld.Core.Events;
using VividWorld.Core.Grudges;
using VividWorld.Core.Ingest;
using VividWorld.Core.Persistence;
using VividWorld.Core.Presentation;
using VividWorld.Core.Rumors;
using VividWorld.Core.Secrets;

namespace VividWorld.Campaign
{
    internal sealed class TournamentMeasureBehavior : CampaignBehaviorBase
    {
        public static TournamentMeasureTally Tally { get; } = new TournamentMeasureTally();

        public static Func<CharacterObject, MBReadOnlyList<CharacterObject>?, Town?, string?>? FinalOpponentResolver { get; set; }

        private WorldEventStore? _eventStore;
        private HeroLookup? _heroLookup;
        private IHeroTraitLookup? _traits;
        private VividWorldConfig? _config;
        private long _campaignSeed;
        private Func<PlayerHeardLogStore?>? _heardLog;
        private Func<RumorEngine?>? _engine;

        public void Initialize(
            WorldEventStore eventStore,
            HeroLookup? heroLookup,
            IHeroTraitLookup? traits,
            VividWorldConfig? config,
            long campaignSeed,
            Func<PlayerHeardLogStore?> heardLog,
            Func<RumorEngine?> engine)
        {
            _eventStore = eventStore;
            _heroLookup = heroLookup;
            _traits = traits;
            _config = config;
            _campaignSeed = campaignSeed;
            _heardLog = heardLog;
            _engine = engine;
        }

        public override void RegisterEvents()
        {
            CampaignEvents.TournamentFinished.AddNonSerializedListener(this, OnTournamentFinished);
        }

        public override void SyncData(IDataStore dataStore)
        {
            // In-memory tally only, resets across game restarts
        }

        private void OnTournamentFinished(
            CharacterObject winner,
            MBReadOnlyList<CharacterObject> participants,
            Town town,
            ItemObject prize)
        {
            try
            {
                double currentDay = CampaignTime.Now.ToDays;

                // 1. 參賽者裡有玩家 => 印一行「玩家下場，不算」就結束
                bool playerInvolved = false;
                if (participants != null)
                {
                    for (int i = 0; i < participants.Count; i++)
                    {
                        var p = participants[i];
                        if (p != null && (p.IsPlayerCharacter || p == CharacterObject.PlayerCharacter || (p.IsHero && p.HeroObject == Hero.MainHero)))
                        {
                            playerInvolved = true;
                            break;
                        }
                    }
                }

                if (playerInvolved)
                {
                    Tally.RecordPlayerParticipated(currentDay);
                    ModLog.Info("tournament measure: player participated, excluded (玩家下場，不算)");
                    return;
                }

                // 2. 冠軍不是英雄 => 印一行「冠軍不是英雄」就結束
                if (winner == null || !winner.IsHero || winner.HeroObject == null)
                {
                    Tally.RecordWinnerNotHero(currentDay);
                    ModLog.Info("tournament measure: winner is not hero (冠軍不是英雄)");
                    return;
                }

                var winnerHero = winner.HeroObject;
                if (!winnerHero.IsLord && !winnerHero.IsWanderer)
                {
                    Tally.RecordWinnerNotHero(currentDay);
                    ModLog.Info("tournament news skipped: winner is not lord or wanderer");
                    return;
                }
                var tournamentModel = TaleWorlds.CampaignSystem.Campaign.Current?.Models?.TournamentModel;
                float winnerScore = tournamentModel != null ? tournamentModel.GetTournamentSimulationScore(winner) : 0f;
                int winnerHonor = winnerHero.GetTraitLevel(DefaultTraits.Honor);
                int winnerCalc = winnerHero.GetTraitLevel(DefaultTraits.Calculating);
                string townId = town?.StringId ?? string.Empty;
                string townName = town?.Name?.ToString() ?? "unknown";

                var winnerFact = new TournamentWinnerFact(
                    winnerHero.StringId ?? string.Empty,
                    winnerHero.Name?.ToString() ?? "unknown",
                    winnerHero.IsLord,
                    winnerHero.IsWanderer,
                    winnerScore,
                    winnerHonor,
                    winnerCalc,
                    townId,
                    townName);

                ModLog.Info(string.Format(
                    CultureInfo.InvariantCulture,
                    "tournament measure: winner {0} ({1}), role: {2}, score {3:F1}, honor {4}, town {5} ({6})",
                    winnerFact.Name,
                    winnerFact.HeroId,
                    winnerFact.RoleDescription,
                    winnerFact.Score,
                    winnerFact.Honor,
                    winnerFact.TownName,
                    winnerFact.TownId));

                if (winnerFact.IsHonorPositive)
                {
                    ModLog.Info("tournament measure:   winner honor positive, will not ask (冠軍榮譽為正，不會開口)");
                }

                // 4. 每位是英雄的參賽者（冠軍除外）印一行：名字、id、分數、榮譽、謀略、跟冠軍的好感、是不是自家人、冠軍是不是他的族長、冠軍是不是他的國王；判定
                var participantEvaluations = new List<TournamentParticipantEvaluation>();
                var participantFacts = new List<TournamentParticipantFact>();
                if (participants != null)
                {
                    for (int i = 0; i < participants.Count; i++)
                    {
                        var pChar = participants[i];
                        if (pChar == null || !pChar.IsHero || pChar.HeroObject == null) continue;
                        var pHero = pChar.HeroObject;
                        if (pHero == winnerHero || pHero == Hero.MainHero) continue;

                        float pScore = tournamentModel != null ? tournamentModel.GetTournamentSimulationScore(pChar) : 0f;
                        int pHonor = pHero.GetTraitLevel(DefaultTraits.Honor);
                        int pCalc = pHero.GetTraitLevel(DefaultTraits.Calculating);
                        int relation = pHero.GetBaseHeroRelation(winnerHero);
                        bool isKin = GameWorldChannel.IsFamilyOf(pHero, winnerHero);
                        bool isClanLeader = pHero.Clan != null && pHero.Clan.Leader == winnerHero;
                        bool isKingdomLeader = pHero.Clan?.Kingdom != null && pHero.Clan.Kingdom.Leader == winnerHero;

                        var pFact = new TournamentParticipantFact(
                            pHero.StringId ?? string.Empty,
                            pHero.Name?.ToString() ?? "unknown",
                            pScore,
                            pHonor,
                            pCalc,
                            relation,
                            isKin,
                            isClanLeader,
                            isKingdomLeader);

                        participantFacts.Add(pFact);

                        var eval = TournamentAndKinMeasure.EvaluateParticipant(winnerFact, pFact);
                        participantEvaluations.Add(eval);

                        ModLog.Info(string.Format(
                            CultureInfo.InvariantCulture,
                            "tournament measure:   participant {0} ({1}): score {2:F1} ({10:F2}x), honor {3}, calc {4}, rel {5}, kin={6}, clanLeaderWinner={7}, kingLeaderWinner={8} => {9}",
                            pFact.Name,
                            pFact.HeroId,
                            pFact.Score,
                            pFact.Honor,
                            pFact.Calculating,
                            pFact.RelationWithWinner,
                            pFact.IsKinWithWinner,
                            pFact.IsClanLeaderWinner,
                            pFact.IsKingdomLeaderWinner,
                            eval.FormatVerdict(),
                            eval.ScoreRatio));
                    }
                }

                // 5. 最後一行：這場夠格的幾位（主從幾位、交情幾位）、冠軍榮譽為正時另註「冠軍不會開口」
                Tally.RecordWinnerHeroMatch(currentDay, winnerFact, participantEvaluations);

                int qualifiedCount = participantEvaluations.Count(e => e.IsQualified);
                int masterServantCount = participantEvaluations.Count(e => e.QualifiesMasterServant);
                int kinFriendshipCount = participantEvaluations.Count(e => e.QualifiesKinFriendship);
                int otherFriendshipCount = participantEvaluations.Count(e => e.QualifiesOtherFriendship);

                string winnerNote = winnerFact.IsHonorPositive
                    ? " - winner honor positive, will not ask (冠軍榮譽為正，不會開口)"
                    : string.Empty;

                ModLog.Info(string.Format(
                    CultureInfo.InvariantCulture,
                    "tournament measure: qualified: {0} (master-servant: {1}, kin-friendship: {2}, other-friendship: {3}){4}",
                    qualifiedCount,
                    masterServantCount,
                    kinFriendshipCount,
                    otherFriendshipCount,
                    winnerNote));

                // 5.1 比武大會決賽放水判定
                var throwResult = TournamentThrowEvaluation.Evaluate(
                    winnerFact,
                    participantFacts,
                    _config,
                    _campaignSeed,
                    townId,
                    currentDay,
                    _engine?.Invoke()?.Rng);

                foreach (var cEval in throwResult.Evaluations)
                {
                    ModLog.Info(string.Format(
                        CultureInfo.InvariantCulture,
                        "tournament throw candidate {0} ({1}): chanceToWin={2}, masterServant={3}, kinFriend={4}, otherFriend={5}, willing={6}{7}",
                        cEval.Participant.Name,
                        cEval.Participant.HeroId,
                        cEval.HasChanceToWin,
                        cEval.QualifiesMasterServant,
                        cEval.QualifiesKinFriendship,
                        cEval.QualifiesOtherFriendship,
                        cEval.IsWilling,
                        string.IsNullOrEmpty(cEval.DisqualifiedReason) ? string.Empty : " (disqualified: " + cEval.DisqualifiedReason + ")"));
                }
                ModLog.Info(throwResult.LogMessage);

                if (throwResult.IsThrown)
                {
                    Tally.RecordThrown(throwResult.SecretPath == "liege", !throwResult.IsWilling);
                }

                // 6. 決賽對手再比與公開奪冠消息生成
                var candidateInputs = new List<TournamentCandidateInput>();
                if (participants != null)
                {
                    for (int i = 0; i < participants.Count; i++)
                    {
                        var pChar = participants[i];
                        if (pChar == null || !pChar.IsHero || pChar.HeroObject == null) continue;
                        var pHero = pChar.HeroObject;
                        if (pHero == winnerHero || pHero == Hero.MainHero) continue;

                        float pScore = tournamentModel != null ? tournamentModel.GetTournamentSimulationScore(pChar) : 0f;
                        candidateInputs.Add(new TournamentCandidateInput(
                            pHero.StringId ?? string.Empty,
                            pHero.Name?.ToString() ?? "unknown",
                            pScore));
                    }
                }

                string? designatedOpponentHeroId = FinalOpponentResolver?.Invoke(winner, participants, town);
                if (throwResult.IsThrown && !string.IsNullOrEmpty(throwResult.ThrowerHeroId))
                {
                    designatedOpponentHeroId = throwResult.ThrowerHeroId;
                }

                var rematchResult = TournamentFinalRematch.DetermineFinalOpponent(
                    candidateInputs,
                    winnerHero.StringId ?? string.Empty,
                    _campaignSeed,
                    townId,
                    currentDay,
                    designatedOpponentHeroId);

                foreach (var cand in rematchResult.Candidates)
                {
                    ModLog.Info(string.Format(
                        CultureInfo.InvariantCulture,
                        "tournament rematch candidate {0} ({1}): base {2:F1}, roll {3:F3}, final {4:F2}",
                        cand.Name,
                        cand.HeroId,
                        cand.BaseScore,
                        cand.RandomRoll,
                        cand.FinalScore));
                }

                if (rematchResult.SelectedOpponent != null)
                {
                    ModLog.Info(string.Format(
                        CultureInfo.InvariantCulture,
                        "tournament rematch opponent selected: {0} ({1}){2}",
                        rematchResult.SelectedOpponent.Name,
                        rematchResult.SelectedOpponent.HeroId,
                        rematchResult.WasDesignated ? " (designated)" : string.Empty));
                }
                else
                {
                    ModLog.Info("tournament rematch: no opponent (only champion participated)");
                }

                if (_eventStore == null)
                {
                    ModLog.Warn("tournament news skipped: event store not initialized");
                    return;
                }

                var baseTemplate = EventCatalogStore.TemplateByType("tournament_won");
                if (baseTemplate == null)
                {
                    ModLog.Warn("tournament news skipped: template 'tournament_won' not found in catalog");
                    return;
                }

                EventTemplate templateToUse;
                var bindings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["CHAMPION"] = winnerHero.StringId ?? string.Empty,
                    ["SETTLEMENT"] = town?.Settlement?.StringId ?? townId
                };

                if (rematchResult.SelectedOpponent != null)
                {
                    templateToUse = baseTemplate;
                    bindings["FINAL_OPPONENT"] = rematchResult.SelectedOpponent.HeroId;
                }
                else
                {
                    templateToUse = TemplateVariants.TournamentWonWithoutFinalOpponent(baseTemplate);
                }

                var submission = TemplateBinder.Bind(templateToUse, bindings, currentDay, null, out var bindIssues);
                if (submission == null)
                {
                    string issueDetails = string.Join("; ", bindIssues.Select(iss => $"{iss.Field}: {iss.Detail}"));
                    ModLog.Warn($"tournament news skipped: binding failed: {issueDetails}");
                    return;
                }

                submission.AutoResolveWitnesses = false;

                var presentHeroes = town?.Settlement != null
                    ? EligibilityLabel.GetPresentHeroesAtSettlement(town.Settlement)
                    : new List<Hero>();

                foreach (var h in presentHeroes)
                {
                    if (h != null && !string.IsNullOrEmpty(h.StringId) && !submission.InitialKnowerHeroIds.Contains(h.StringId))
                    {
                        submission.InitialKnowerHeroIds.Add(h.StringId);
                    }
                }

                bool playerPresent = (Hero.MainHero?.CurrentSettlement != null && town?.Settlement != null && Hero.MainHero.CurrentSettlement == town.Settlement)
                    || (MobileParty.MainParty?.CurrentSettlement != null && town?.Settlement != null && MobileParty.MainParty.CurrentSettlement == town.Settlement);

                if (playerPresent && Hero.MainHero != null && !string.IsNullOrEmpty(Hero.MainHero.StringId))
                {
                    if (!submission.InitialKnowerHeroIds.Contains(Hero.MainHero.StringId))
                    {
                        submission.InitialKnowerHeroIds.Add(Hero.MainHero.StringId);
                    }
                }

                var ingestResult = _eventStore.Submit(submission, out string? eventId, out string? rejectionReason);
                if (ingestResult != IngestResult.Accepted || string.IsNullOrEmpty(eventId))
                {
                    ModLog.Warn($"tournament news submission failed: {ingestResult} - {rejectionReason}");
                    return;
                }

                ModLog.Info($"tournament news created: {eventId} (champion={winnerHero.StringId}, opponent={rematchResult.SelectedOpponent?.HeroId ?? "none"}, witnesses={submission.InitialKnowerHeroIds.Count}, playerWitness={playerPresent})");

                if (playerPresent && Hero.MainHero != null && !string.IsNullOrEmpty(eventId))
                {
                    var evt = _eventStore.Load(eventId!);
                    var playerEntry = evt?.EntryFor(Hero.MainHero.StringId);
                    var heardLog = _heardLog?.Invoke();
                    var engine = _engine?.Invoke();
                    if (evt != null && playerEntry != null && heardLog != null && engine != null)
                    {
                        var facts = PlayerKnownFacts.Of(evt, playerEntry, engine);
                        heardLog.Record(evt, playerEntry, facts, currentDay, id => _eventStore?.Load(id));
                    }
                }

                if (throwResult.IsThrown && !string.IsNullOrEmpty(throwResult.ThrowerHeroId))
                {
                    var thrownTemplate = EventCatalogStore.TemplateByType("tournament_final_thrown");
                    if (thrownTemplate == null)
                    {
                        ModLog.Warn("tournament throw news skipped: template 'tournament_final_thrown' not found in catalog");
                    }
                    else
                    {
                        string throwerHeroId = throwResult.ThrowerHeroId!;
                        var thrownBindings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                        {
                            ["CHAMPION"] = winnerHero.StringId ?? string.Empty,
                            ["THROWER"] = throwerHeroId
                        };

                        var thrownSubmission = TemplateBinder.Bind(thrownTemplate, thrownBindings, currentDay, eventId, out var thrownIssues);
                        if (thrownSubmission == null)
                        {
                            string issueDetails = string.Join("; ", thrownIssues.Select(iss => $"{iss.Field}: {iss.Detail}"));
                            ModLog.Warn($"tournament throw news skipped: binding failed: {issueDetails}");
                        }
                        else
                        {
                            thrownSubmission.SecretPath = throwResult.SecretPath;
                            thrownSubmission.SecretOwnerHeroIds = new List<string> { winnerHero.StringId ?? string.Empty, throwerHeroId };
                            thrownSubmission.InitialKnowerHeroIds = new List<string> { winnerHero.StringId ?? string.Empty, throwerHeroId };
                            thrownSubmission.SecretHarmedHeroIds = new List<string>(throwResult.HarmedHeroIds);
                            thrownSubmission.AutoResolveWitnesses = false;

                            var thrownIngestResult = _eventStore.Submit(thrownSubmission, out string? thrownEventId, out string? thrownReason);
                            if (thrownIngestResult != IngestResult.Accepted || string.IsNullOrEmpty(thrownEventId))
                            {
                                ModLog.Warn($"tournament throw news submission failed: {thrownIngestResult} - {thrownReason}");
                            }
                            else
                            {
                                ModLog.Info($"tournament throw news created: {thrownEventId} (champion={winnerHero.StringId}, thrower={throwResult.ThrowerHeroId}, path={throwResult.SecretPath}, willing={throwResult.IsWilling})");

                                if (!throwResult.IsWilling && _heroLookup != null)
                                {
                                    var thrownEvt = _eventStore.Load(thrownEventId!);
                                    if (thrownEvt != null)
                                    {
                                        var req = new GrudgeRequest
                                        {
                                            FromHeroId = throwerHeroId,
                                            AboutHeroId = winnerHero.StringId ?? string.Empty,
                                            FromRole = "thrower",
                                            ToRole = "champion",
                                            Requested = throwResult.ReluctantGrudge,
                                            LedgerOnly = false,
                                            ForceClanEscalation = false,
                                            Source = GrudgeSource.Situation,
                                            RequireHop0 = true
                                        };

                                        GrudgeApplier.Apply(
                                            thrownEvt,
                                            new[] { req },
                                            currentDay,
                                            _eventStore,
                                            _heroLookup,
                                            _traits ?? _eventStore.Traits,
                                            _config ?? new VividWorldConfig());
                                    }
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                ModLog.Error("TournamentMeasureBehavior.OnTournamentFinished encountered an exception", ex);
            }
        }
    }
}
