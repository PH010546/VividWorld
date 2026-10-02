using System;
using System.Collections.Generic;
using System.Linq;
using VividWorld.Core.Config;
using VividWorld.Core.Events;
using VividWorld.Core.Grudges;
using VividWorld.Core.Memory;
using VividWorld.Core.Persistence;
using VividWorld.Core.Presentation;
using VividWorld.Core.Rumors;

namespace VividWorld.Core.Dialogue
{
    public sealed class RumorOfferSelector
    {
        private readonly VividWorldConfig _config;
        private readonly RumorEngine _engine;
        private readonly string _playerHeroId;
        private readonly Func<string, VividWorld.Core.Catalog.EventTemplate?>? _getTemplate;
        private readonly IHeroTraitLookup? _traits;
        private readonly VividWorld.Core.Feelings.FeelingResolver? _feelings;
        private readonly IDialogueWorld? _dialogueWorld;
        private readonly PlayerHeardLogStore? _playerHeardLog;
        private readonly Func<string, WorldEvent?>? _getEvent;

        public RumorMode Mode { get; set; } = RumorMode.Casual;

        public RumorOfferSelector(
            VividWorldConfig cfg,
            RumorEngine engine,
            string playerHeroId,
            RumorMode? mode = null,
            Func<string, VividWorld.Core.Catalog.EventTemplate?>? getTemplate = null,
            IHeroTraitLookup? traits = null,
            VividWorld.Core.Feelings.FeelingResolver? feelings = null,
            IDialogueWorld? dialogueWorld = null,
            PlayerHeardLogStore? playerHeardLog = null,
            Func<string, WorldEvent?>? getEvent = null)
        {
            _config = cfg ?? throw new ArgumentNullException(nameof(cfg));
            _engine = engine ?? throw new ArgumentNullException(nameof(engine));
            _playerHeroId = playerHeroId ?? string.Empty;
            _getTemplate = getTemplate ?? _engine.TemplateProvider;
            _traits = traits ?? _engine.Traits;
            _feelings = feelings;
            _dialogueWorld = dialogueWorld;
            _playerHeardLog = playerHeardLog;
            _getEvent = getEvent;

            if (mode.HasValue)
            {
                Mode = mode.Value;
            }
            else
            {
                Mode = RumorModeResolver.Resolve(_config.Dialogue.VolunteerMode, null).Mode;
            }
        }

        public RumorEngine Engine => _engine;
        public IDialogueWorld? DialogueWorld => _dialogueWorld;
        public PlayerHeardLogStore? PlayerHeardLog => _playerHeardLog;
        public Func<string, WorldEvent?>? GetEvent => _getEvent;

        /// <summary>依當前傳聞模式決定的主動講述門檻線。</summary>
        public double ActiveVolunteerLine => Mode == RumorMode.Casual
            ? _config.Dialogue.CasualVolunteerLine
            : _config.Dialogue.RealisticVolunteerLine;

        /// <summary>相容既有呼叫端的整數閒聊門檻屬性。</summary>
        public int ChatRelationGate => (int)ActiveVolunteerLine;

        /// <summary>
        /// 計算傳聞到達玩家時的落點手數（單一真相來源）。
        /// 完整版固定為 TellerHop + 1。
        /// </summary>
        public static int ComputeLandingHop(int tellerHop, int maxHop, VolunteerTier tier, int gistExtraHops)
        {
            if (tier == VolunteerTier.Gist)
            {
                int directHop = tellerHop + 1;
                int gistHop = directHop + Math.Max(0, gistExtraHops);
                int ceiling = Math.Max(directHop, maxHop);
                return Math.Min(gistHop, ceiling);
            }
            return tellerHop + 1;
        }

        /// <summary>實例輔助函式：取得指定事件在講給玩家時的落點手數（固定為講述者手數 + 1）。</summary>
        public int ComputeLandingHop(WorldEvent evt, int tellerHop, VolunteerTier tier = VolunteerTier.Full)
        {
            return tellerHop + 1;
        }

        /// <summary>
        /// 計算講述者對玩家的溝通意願。
        /// 意願 = 對玩家好感 + 6 × 仗義 + 4 × 榮譽 - 8 × 理性。
        /// </summary>
        public void ComputeWillingness(
            HeroSocialProfile? teller,
            out int relation,
            out double generosityBonus,
            out double honorBonus,
            out double calculatingPenalty,
            out double willingness)
        {
            var d = _config.Dialogue;
            var w = d.AskTraitWeights ?? new AskTraitWeights();

            relation = teller?.RelationWithPlayer ?? 0;
            double generosity = _traits?.Of(teller?.HeroId ?? string.Empty)?.Generosity ?? teller?.Traits?.Generosity ?? 0;
            double honor = _traits?.Of(teller?.HeroId ?? string.Empty)?.Honor ?? teller?.Traits?.Honor ?? 0;
            double calculating = _traits?.Of(teller?.HeroId ?? string.Empty)?.Calculating ?? teller?.Traits?.Calculating ?? 0;

            generosityBonus = w.Generosity * generosity;
            honorBonus = w.Honor * honor;
            calculatingPenalty = w.Calculating * calculating;

            willingness = relation + generosityBonus + honorBonus + calculatingPenalty;
        }

        /// <summary>說話的人跟玩家熟不熟、肯不肯答的判定結果，以及算出它的每一項。</summary>
        public readonly struct PlayerStanding
        {
            public PlayerStanding(int relation, double generosityTerm, double honorTerm, double calculatingTerm,
                                  double willingness, bool isCloseKin, double volunteerLine, double askThreshold)
            {
                Relation = relation;
                GenerosityTerm = generosityTerm;
                HonorTerm = honorTerm;
                CalculatingTerm = calculatingTerm;
                Willingness = willingness;
                IsCloseKin = isCloseKin;
                VolunteerLine = volunteerLine;
                AskThreshold = askThreshold;
            }

            public int Relation { get; }
            public double GenerosityTerm { get; }
            public double HonorTerm { get; }
            public double CalculatingTerm { get; }
            public double Willingness { get; }
            /// <summary>玩家的配偶、夥伴、家族成員：不看意願，直接算熟、也算信得過。</summary>
            public bool IsCloseKin { get; }
            public double VolunteerLine { get; }
            public double AskThreshold { get; }

            /// <summary>熟：會主動跟玩家講切身的事。</summary>
            public bool IsFamiliar => IsCloseKin || Willingness >= VolunteerLine;

            /// <summary>肯答：過了打聽的門檻，或已經算熟（會主動講的人被問不會不理）。</summary>
            public bool CanAnswer => IsFamiliar || Willingness >= AskThreshold;
        }

        /// <summary>
        /// 「熟不熟、肯不肯答」只在這裡算一次；主動講、被問、候選過濾、預覽都呼叫它，
        /// 同一個判斷寫兩份遲早會各走各的。
        /// </summary>
        public PlayerStanding StandingWithPlayer(HeroSocialProfile? teller)
        {
            ComputeWillingness(teller, out int relation, out double gen, out double hon, out double calc, out double willingness);
            bool isCloseKin = teller != null && _config.Dialogue.NpcVolunteerAlwaysForCloseKin &&
                (teller.IsPlayerSpouse || teller.IsPlayerCompanion || teller.IsPlayerClanMember);
            return new PlayerStanding(relation, gen, hon, calc, willingness, isCloseKin,
                ActiveVolunteerLine, _config.Dialogue.AskWillingnessThreshold);
        }

        public bool WillVolunteer(HeroSocialProfile teller, double day)
        {
            if (teller == null) return false;
            if (!StandingWithPlayer(teller).IsFamiliar) return false;

            int cap = _config.Dialogue.SharesPerHeroPerDay;
            if (cap > 0 && teller.SharedToday >= cap) return false;

            return true;
        }

        public bool WillAnswerAsk(HeroSocialProfile teller)
        {
            if (teller == null) return false;

            var standing = StandingWithPlayer(teller);
            return standing.Relation >= _config.Dialogue.AskRelationGate && standing.CanAnswer;
        }

        public VolunteerDecision DecideOnVolunteer(HeroSocialProfile teller, IReadOnlyList<RumorCandidate> candidates, double day)
        {
            var d = _config.Dialogue;
            var standing = StandingWithPlayer(teller);
            int relation = standing.Relation;
            double gen = standing.GenerosityTerm, hon = standing.HonorTerm, calc = standing.CalculatingTerm;
            double willingness = standing.Willingness;
            double activeLine = standing.VolunteerLine;
            bool isCloseKin = standing.IsCloseKin;

            var decision = new VolunteerDecision
            {
                Relation = relation,
                RelationGate = (int)activeLine,
                ChatRelationGate = (int)activeLine,
                Willingness = willingness,
                WillingnessLine = activeLine,
                WillingnessPassed = standing.IsFamiliar,
                GenerosityTerm = gen,
                HonorTerm = hon,
                CalculatingTerm = calc,
                Tier = VolunteerTier.Full,
                IsCloseKin = isCloseKin,
                Day = day,
                SharedToday = teller?.SharedToday ?? 0,
                SharesPerHeroPerDay = d.SharesPerHeroPerDay,
                CandidateCount = candidates?.Count ?? 0
            };

            if (teller == null || !standing.IsFamiliar)
            {
                decision.Tier = VolunteerTier.None;
                decision.Refusal = VolunteerRefusal.RelationGate;
                return decision;
            }

            int cap = d.SharesPerHeroPerDay;
            if (cap > 0 && teller.SharedToday >= cap)
            {
                decision.Refusal = VolunteerRefusal.SharedToday;
                return decision;
            }

            if (candidates == null || candidates.Count == 0)
            {
                decision.Refusal = VolunteerRefusal.NoKnownEvents;
                return decision;
            }

            var classification = ClassifyCandidates(teller, candidates, day, VolunteerTier.Full);
            decision.FilteredNotVisible = classification.FilteredNotVisible;
            decision.FilteredFutureTimeline = classification.FilteredFutureTimeline;
            decision.FilteredPlayerKnows = classification.FilteredPlayerKnows;
            decision.FilteredOther = classification.FilteredOther;
            foreach (var note in classification.FilterNotes)
            {
                decision.FilterNotes.Add(note);
            }

            if (classification.Eligible.Count == 0)
            {
                decision.Refusal = VolunteerRefusal.AllCandidatesFiltered;
                return decision;
            }

            // 只有切身相關的三類才會主動講
            var closelyRelated = new List<(RumorCandidate Candidate, VolunteerReasonCategory Category, string Why)>();
            foreach (var c in classification.Eligible)
            {
                if (IsCloselyRelated(teller, c.Event, day, out var cat, out var why))
                {
                    closelyRelated.Add((c, cat, why));
                }
                else
                {
                    decision.FilterNotes.Add($"{c.Event.EventId}: eligible but not closely related to teller or player");
                }
            }

            if (closelyRelated.Count == 0)
            {
                decision.Refusal = VolunteerRefusal.NoCloselyRelatedEvent;
                return decision;
            }

            var sorted = closelyRelated
                .OrderBy(x => x.Candidate.PlayerExistingHop.HasValue)
                .ThenByDescending(x => GetEffectiveWeight(x.Candidate.Event))
                .ThenByDescending(x => x.Candidate.Event.Day)
                .ThenBy(x => x.Candidate.Event.EventId, StringComparer.Ordinal)
                .ToList();

            var best = sorted[0];
            for (int i = 1; i < sorted.Count; i++)
            {
                var other = sorted[i];
                decision.FilterNotes.Add($"{other.Candidate.Event.EventId}: closely related ({other.Category}) but lost to {best.Candidate.Event.EventId} (unread/weight/day/id)");
            }

            decision.Refusal = VolunteerRefusal.None;
            decision.Offer = BuildOffer(best.Candidate, teller, day, VolunteerTier.Full, withFeeling: true);
            decision.ReasonCategory = best.Category.ToString();
            decision.ChosenTopicWeight = GetEffectiveWeight(best.Candidate.Event);
            decision.ChosenTopicScale = DramaScales.BandOfWeight(decision.ChosenTopicWeight);
            decision.ChosenTopicWhy = best.Why;

            return decision;
        }

        public RumorOffer? SelectVolunteered(HeroSocialProfile teller, IReadOnlyList<RumorCandidate> candidates, double day)
        {
            return DecideOnVolunteer(teller, candidates, day).Offer;
        }

        public AskDecision DecideOnAsk(HeroSocialProfile teller, IReadOnlyList<RumorCandidate> candidates, double day)
        {
            var d = _config.Dialogue;
            int cap = d.SharesPerHeroPerDay;
            int sharedToday = teller?.SharedToday ?? 0;

            var standing = StandingWithPlayer(teller);
            int relation = standing.Relation;
            double gen = standing.GenerosityTerm, hon = standing.HonorTerm, calc = standing.CalculatingTerm;
            double willingness = standing.Willingness;
            double threshold = standing.AskThreshold;
            double activeLine = standing.VolunteerLine;
            bool isFamiliar = standing.IsFamiliar;
            bool canAnswer = standing.CanAnswer;

            var decision = new AskDecision
            {
                Relation = relation,
                Willingness = willingness,
                Threshold = threshold,
                ActiveVolunteerLine = activeLine,
                IsFamiliar = isFamiliar,
                CanAnswer = canAnswer,
                GenerosityTerm = gen,
                HonorTerm = hon,
                CalculatingTerm = calc,
                SharedToday = sharedToday,
                SharesPerHeroPerDay = cap,
                CandidateCount = candidates?.Count ?? 0
            };

            if (cap > 0 && sharedToday >= cap)
            {
                decision.Refusal = AskRefusal.SharedToday;
                return decision;
            }

            if (teller == null || relation < d.AskRelationGate)
            {
                decision.Refusal = AskRefusal.RelationGate;
                return decision;
            }

            if (!canAnswer)
            {
                decision.Refusal = AskRefusal.WillingnessGate;
                return decision;
            }

            if (candidates == null || candidates.Count == 0)
            {
                decision.Refusal = AskRefusal.NoKnownEvents;
                return decision;
            }

            var classification = ClassifyCandidates(teller, candidates, day, VolunteerTier.Full);
            decision.FilteredNotVisible = classification.FilteredNotVisible;
            decision.FilteredFutureTimeline = classification.FilteredFutureTimeline;
            decision.FilteredPlayerKnows = classification.FilteredPlayerKnows;
            decision.FilteredOther = classification.FilteredOther;
            foreach (var note in classification.FilterNotes)
            {
                decision.FilterNotes.Add(note);
            }

            if (classification.Eligible.Count == 0)
            {
                decision.Refusal = AskRefusal.AllCandidatesFiltered;
                return decision;
            }

            if (isFamiliar)
            {
                // 熟的人：先照三類切身挑；三類都沒有，就照大事挑，帶感想
                var closelyRelated = new List<(RumorCandidate Candidate, VolunteerReasonCategory Category, string Why)>();
                foreach (var c in classification.Eligible)
                {
                    if (IsCloselyRelated(teller, c.Event, day, out var cat, out var why))
                    {
                        closelyRelated.Add((c, cat, why));
                    }
                }

                if (closelyRelated.Count > 0)
                {
                    var sorted = closelyRelated
                        .OrderBy(x => x.Candidate.PlayerExistingHop.HasValue)
                        .ThenByDescending(x => GetEffectiveWeight(x.Candidate.Event))
                        .ThenByDescending(x => x.Candidate.Event.Day)
                        .ThenBy(x => x.Candidate.Event.EventId, StringComparer.Ordinal)
                        .ToList();

                    var best = sorted[0];
                    for (int i = 1; i < sorted.Count; i++)
                    {
                        decision.FilterNotes.Add($"{sorted[i].Candidate.Event.EventId}: closely related but lost to {best.Candidate.Event.EventId}");
                    }

                    decision.Refusal = AskRefusal.None;
                    decision.Offer = BuildOffer(best.Candidate, teller, day, VolunteerTier.Full, withFeeling: true);
                    decision.AnswerMode = "familiar_closely_related";
                    decision.ReasonCategory = best.Category.ToString();
                    decision.ChosenTopicWeight = GetEffectiveWeight(best.Candidate.Event);
                    decision.ChosenTopicScale = DramaScales.BandOfWeight(decision.ChosenTopicWeight);
                    decision.ChosenTopicWhy = best.Why;
                    return decision;
                }

                decision.FilterNotes.Add("familiar hero has no closely related events; falling back to big news");

                int bigNewsLine = d.BigNewsLine;
                var bigNews = new List<RumorCandidate>();
                foreach (var c in classification.Eligible)
                {
                    if (GetEffectiveWeight(c.Event) >= bigNewsLine)
                    {
                        bigNews.Add(c);
                    }
                    else
                    {
                        decision.FilterNotes.Add($"{c.Event.EventId}: weight {GetEffectiveWeight(c.Event)} < big news line {bigNewsLine}");
                    }
                }

                if (bigNews.Count > 0)
                {
                    var sorted = SortCandidates(bigNews);
                    var best = sorted[0];
                    for (int i = 1; i < sorted.Count; i++)
                    {
                        decision.FilterNotes.Add($"{sorted[i].Event.EventId}: big news but lost to {best.Event.EventId}");
                    }

                    decision.Refusal = AskRefusal.None;
                    decision.Offer = BuildOffer(best, teller, day, VolunteerTier.Full, withFeeling: true);
                    decision.AnswerMode = "familiar_big_news";
                    decision.ReasonCategory = "BigNews";
                    decision.ChosenTopicWeight = GetEffectiveWeight(best.Event);
                    decision.ChosenTopicScale = DramaScales.BandOfWeight(decision.ChosenTopicWeight);
                    decision.ChosenTopicWhy = $"big news (weight {decision.ChosenTopicWeight} >= {bigNewsLine})";
                    return decision;
                }

                decision.Refusal = AskRefusal.AllCandidatesFiltered;
                return decision;
            }
            else
            {
                // 不熟但肯答的人：只講大事，不帶感想
                int bigNewsLine = d.BigNewsLine;
                var bigNews = new List<RumorCandidate>();
                foreach (var c in classification.Eligible)
                {
                    if (GetEffectiveWeight(c.Event) >= bigNewsLine)
                    {
                        bigNews.Add(c);
                    }
                    else
                    {
                        decision.FilterNotes.Add($"{c.Event.EventId}: weight {GetEffectiveWeight(c.Event)} < big news line {bigNewsLine} (unfamiliar stranger only tells big news)");
                    }
                }

                if (bigNews.Count > 0)
                {
                    var sorted = SortCandidates(bigNews);
                    var best = sorted[0];
                    for (int i = 1; i < sorted.Count; i++)
                    {
                        decision.FilterNotes.Add($"{sorted[i].Event.EventId}: big news but lost to {best.Event.EventId}");
                    }

                    decision.Refusal = AskRefusal.None;
                    decision.Offer = BuildOffer(best, teller, day, VolunteerTier.Full, withFeeling: false);
                    decision.AnswerMode = "unfamiliar_big_news";
                    decision.ReasonCategory = "BigNews";
                    decision.ChosenTopicWeight = GetEffectiveWeight(best.Event);
                    decision.ChosenTopicScale = DramaScales.BandOfWeight(decision.ChosenTopicWeight);
                    decision.ChosenTopicWhy = $"big news for stranger (weight {decision.ChosenTopicWeight} >= {bigNewsLine}, no feeling)";
                    return decision;
                }

                decision.Refusal = AskRefusal.AllCandidatesFiltered;
                return decision;
            }
        }

        public RumorOffer? SelectOnAsk(HeroSocialProfile teller, IReadOnlyList<RumorCandidate> candidates, double day)
        {
            return DecideOnAsk(teller, candidates, day).Offer;
        }

        private List<RumorCandidate> SortCandidates(IEnumerable<RumorCandidate> candidates)
        {
            return candidates
                .OrderBy(c => c.PlayerExistingHop.HasValue)
                .ThenByDescending(c => GetEffectiveWeight(c.Event))
                .ThenByDescending(c => c.Event.Day)
                .ThenBy(c => c.Event.EventId, StringComparer.Ordinal)
                .ToList();
        }

        public CandidateClassification ClassifyCandidates(HeroSocialProfile teller, IReadOnlyList<RumorCandidate>? candidates, double day, VolunteerTier tier = VolunteerTier.Full)
        {
            var classification = new CandidateClassification();
            if (teller == null || candidates == null || candidates.Count == 0)
            {
                return classification;
            }

            foreach (var c in candidates)
            {
                var rejection = Evaluate(teller, c, day, out string note);
                switch (rejection)
                {
                    case CandidateRejection.None:
                        classification.Eligible.Add(c);
                        break;

                    case CandidateRejection.NotVisible:
                        classification.FilteredNotVisible++;
                        break;

                    case CandidateRejection.FutureTimeline:
                        classification.FilteredFutureTimeline++;
                        break;

                    case CandidateRejection.RetellDisabled:
                    case CandidateRejection.RetellNoNewFacts:
                    case CandidateRejection.PlayerHeardEnding:
                        classification.FilteredPlayerKnows++;
                        if (!string.IsNullOrEmpty(note)) classification.FilterNotes.Add(note);
                        break;

                    case CandidateRejection.WontTellOwn:
                    case CandidateRejection.RetiredType:
                    case CandidateRejection.SecretHolderGist:
                    case CandidateRejection.LeakedSecretHonorable:
                    case CandidateRejection.LeakedSecretCautiousStranger:
                    case CandidateRejection.SecretHolderNotWilling:
                        classification.FilteredOther++;
                        if (!string.IsNullOrEmpty(note)) classification.FilterNotes.Add(note);
                        break;

                    default:
                        classification.FilteredOther++;
                        break;
                }
            }

            return classification;
        }

        public int GetEffectiveWeight(WorldEvent evt)
        {
            if (evt == null) return DramaScales.MinWeight;
            int weight = DramaScales.ToWeight(evt.DramaWeight, evt.DramaScale);
            if (IsCaptivityEnding(evt) && !string.IsNullOrEmpty(evt.LinkedEventId))
            {
                WorldEvent? opening = _getEvent?.Invoke(evt.LinkedEventId!);
                if (opening != null)
                {
                    int openingWeight = DramaScales.ToWeight(opening.DramaWeight, opening.DramaScale);
                    if (openingWeight >= _config.Dialogue.BigNewsLine)
                    {
                        weight = Math.Max(weight, openingWeight);
                    }
                }
            }
            return weight;
        }

        private static bool IsCaptivityEnding(WorldEvent evt)
        {
            if (evt == null) return false;
            return string.Equals(evt.Type, "hero_released", StringComparison.Ordinal)
                || string.Equals(evt.Type, "hero_escaped_captivity", StringComparison.Ordinal)
                || string.Equals(evt.Type, "hero_escaped_bandits", StringComparison.Ordinal)
                || string.Equals(evt.Type, "hero_rescued_from_bandits", StringComparison.Ordinal);
        }

        public bool IsCloselyRelated(HeroSocialProfile teller, WorldEvent evt, double day, out VolunteerReasonCategory category, out string why)
        {
            category = VolunteerReasonCategory.None;
            why = string.Empty;
            if (teller == null || evt == null) return false;

            string tellerId = teller.HeroId ?? string.Empty;
            var involved = evt.Participants != null
                ? evt.Participants.Values.ToList()
                : new List<string>();

            // 1. 跟說話的人密切相關
            // a. 他自己
            if (evt.RoleOf(tellerId) != null)
            {
                category = VolunteerReasonCategory.TellerSelf;
                why = $"teller {tellerId} is involved as {evt.RoleOf(tellerId)}";
                return true;
            }

            InterestHeroFacts? tellerFacts = _dialogueWorld?.InterestFacts(tellerId);
            InterestHeroFacts? playerFacts = _dialogueWorld?.InterestFacts(_playerHeroId);

            int highAffection = _config.Presentation?.Feelings?.AffectionHigh ?? 30;
            int lowAffection = _config.Presentation?.Feelings?.AffectionLow ?? -30;
            double grudgeThreshold = _config.Presentation?.Feelings?.GrudgeThreshold ?? 4.0;

            foreach (var p in involved)
            {
                if (string.Equals(p, tellerId, StringComparison.Ordinal)) continue;
                InterestHeroFacts? pFacts = _dialogueWorld?.InterestFacts(p);

                // b. 他的親人 (父母、配偶、兄弟姊妹、子女)
                if (InterestCalculator.IsKin(tellerFacts, tellerId, pFacts, p))
                {
                    category = VolunteerReasonCategory.TellerKin;
                    why = $"teller's kin {p} is involved";
                    return true;
                }

                // c. 他同家族的人
                if (tellerFacts != null && pFacts != null &&
                    !string.IsNullOrEmpty(tellerFacts.ClanId) &&
                    string.Equals(tellerFacts.ClanId, pFacts.ClanId, StringComparison.Ordinal))
                {
                    category = VolunteerReasonCategory.TellerClan;
                    why = $"teller's clan member {p} is involved (clan {tellerFacts.ClanId})";
                    return true;
                }

                // d. 他好感 >= 30 或 <= -30 的人
                if (_dialogueWorld != null)
                {
                    int? aff = _dialogueWorld.Affection(tellerId, p);
                    if (aff.HasValue && (aff.Value >= highAffection || aff.Value <= lowAffection))
                    {
                        category = VolunteerReasonCategory.TellerRelation;
                        why = $"teller has strong feeling toward participant {p} (relation {aff.Value})";
                        return true;
                    }
                }

                // e. 他記著恩怨淨額絕對值 >= 4 的人
                if (_dialogueWorld != null)
                {
                    var all = _dialogueWorld.PersonalGrudges(tellerId, p);
                    if (all != null && all.Count > 0)
                    {
                        var kept = all.Where(e => !string.Equals(e.EventId, evt.EventId, StringComparison.Ordinal)).ToList();
                        var replay = GrudgeDecay.Replay(kept, GrudgeScope.Personal, _config.Situations, _traits?.Of(tellerId) ?? teller.Traits, day);
                        if (Math.Abs(replay.Value) >= grudgeThreshold)
                        {
                            category = VolunteerReasonCategory.TellerGrudge;
                            why = $"teller holds grudge against participant {p} (net {replay.Value:F1})";
                            return true;
                        }
                    }
                }
            }

            // 2. 跟玩家密切相關
            foreach (var p in involved)
            {
                InterestHeroFacts? pFacts = _dialogueWorld?.InterestFacts(p);

                // a. 玩家的家族成員 (含夥伴)
                if (_dialogueWorld != null && (_dialogueWorld.IsPlayerClanMember(p) || _dialogueWorld.IsPlayerCompanion(p)))
                {
                    category = VolunteerReasonCategory.PlayerRelated;
                    why = $"participant {p} is player clan member or companion";
                    return true;
                }

                // b. 玩家的親人、配偶
                if (_dialogueWorld != null && (_dialogueWorld.IsPlayerSpouse(p) || InterestCalculator.IsKin(playerFacts, _playerHeroId, pFacts, p)))
                {
                    category = VolunteerReasonCategory.PlayerRelated;
                    why = $"participant {p} is player's spouse or kin";
                    return true;
                }

                // c. 跟玩家好感 >= 30 或 <= -30 的人
                if (_dialogueWorld != null)
                {
                    int? aff = _dialogueWorld.Affection(_playerHeroId, p);
                    if (aff.HasValue && (aff.Value >= highAffection || aff.Value <= lowAffection))
                    {
                        category = VolunteerReasonCategory.PlayerRelated;
                        why = $"participant {p} has strong relation with player ({aff.Value})";
                        return true;
                    }
                }

                // d. 玩家效忠的國王
                if (_dialogueWorld != null && !string.IsNullOrEmpty(_dialogueWorld.PlayerKingdomLeaderId))
                {
                    if (string.Equals(p, _dialogueWorld.PlayerKingdomLeaderId, StringComparison.Ordinal))
                    {
                        category = VolunteerReasonCategory.PlayerRelated;
                        why = $"participant {p} is player's sovereign king";
                        return true;
                    }
                }
            }

            // 3. 他上次親口告訴玩家的那件事的後續
            if (!string.IsNullOrEmpty(evt.LinkedEventId) && _playerHeardLog?.DidTellerTellPlayer(evt.LinkedEventId!, tellerId) == true)
            {
                category = VolunteerReasonCategory.Sequel;
                why = $"sequel to event {evt.LinkedEventId} which teller previously told player";
                return true;
            }

            return false;
        }

        public double Score(RumorCandidate candidate, double day)
        {
            if (candidate?.Event == null) return 0.0;
            return GetEffectiveWeight(candidate.Event);
        }

        public RumorOffer BuildOffer(RumorCandidate candidate, HeroSocialProfile teller, double day, VolunteerTier tier = VolunteerTier.Full, bool withFeeling = true)
        {
            if (candidate?.Event == null) throw new ArgumentNullException(nameof(candidate));
            bool isRetell = candidate.PlayerExistingHop.HasValue;
            int resultingPlayerHop = candidate.TellerHop + 1;
            var retainedFacts = _engine.FactsAtHop(candidate.Event, resultingPlayerHop, teller.HeroId);
            bool isParticipant = candidate.Event.RoleOf(teller.HeroId) != null;
            var prefix = RumorPrefixSelector.SelectPrefix(candidate.TellerHop, candidate.SourceHeroId, isRetell, candidate.IsCorrection, isParticipant);

            var composed = RumorTextComposer.Compose(
                candidate.Event,
                retainedFacts,
                _config.Presentation,
                prefix,
                teller.HeroId,
                candidate.SourceHeroId,
                // 不帶感想的那種回答（不熟的人被問）連當事人自己的句尾也不接：不夠熟就不講心情。
                // 這個旗標也記進玩家紀錄，紀事重組原句時才會是同一句。
                isGist: !withFeeling,
                heldBack: false,
                template: _getTemplate?.Invoke(candidate.Event.Type),
                speakerTraits: _traits?.Of(teller.HeroId));

            if (withFeeling && _feelings != null)
            {
                composed.Feeling = _feelings.Resolve(candidate.Event, teller.HeroId, isGist: false);
            }
            else
            {
                composed.Feeling = null;
            }

            return new RumorOffer
            {
                EventId = candidate.Event.EventId,
                TellerHop = candidate.TellerHop,
                ResultingPlayerHop = resultingPlayerHop,
                ToldFactIds = retainedFacts.Select(f => f.Id).ToList(),
                Composed = composed,
                IsRetell = isRetell,
                IsCorrection = candidate.IsCorrection,
                SourceHeroId = candidate.SourceHeroId,
                Prefix = prefix,
                Score = Score(candidate, day),
                SpeakerRole = composed.SpeakerRole,
                IsGist = false,
                HeldBack = false
            };
        }

        private CandidateRejection Evaluate(HeroSocialProfile teller, RumorCandidate candidate, double day, out string note)
        {
            note = string.Empty;
            if (candidate?.Event == null) return CandidateRejection.EventMissing;
            var evt = candidate.Event;

            // 事件必須對傳聞系統可見
            if (!evt.IsVisibleToRumorSystem) return CandidateRejection.NotVisible;

            // 日期比今天晚 ⇒ 來自一條被抹掉的時間線
            if (!EventVisibility.IsVisibleOn(evt, day)) return CandidateRejection.FutureTimeline;

            // 講述者必須知情
            if (!evt.IsKnownBy(teller.HeroId)) return CandidateRejection.TellerDoesNotKnow;

            // 停用的事件型別
            if (_getTemplate != null && VividWorld.Core.Catalog.RetiredTypeEvaluator.IsRetired(evt, _getTemplate))
            {
                note = $"{evt.EventId}: retired type";
                return CandidateRejection.RetiredType;
            }

            // 當事人自己不講 (selfTell)
            if (_getTemplate != null)
            {
                var selfTell = VividWorld.Core.Catalog.SelfTellEvaluator.Evaluate(evt, teller.HeroId, _getTemplate, _traits);
                if (!selfTell.CanTell)
                {
                    note = $"{evt.EventId}: {selfTell.ReasonText}";
                    return CandidateRejection.WontTellOwn;
                }
            }

            // 玩家已經聽過某次被俘的結局 ⇒ 那次被俘的開頭不再講給他
            if (_playerHeardLog?.HasHeardEndingFor(evt.EventId) == true)
            {
                note = $"{evt.EventId}: player already heard ending for this captivity event";
                return CandidateRejection.PlayerHeardEnding;
            }

            // 秘密過濾
            var standing = StandingWithPlayer(teller);
            double will = standing.Willingness;
            bool isFamiliar = standing.IsFamiliar;

            if (evt.Origin == EventOrigin.Secret)
            {
                bool isHolder = IsSecretHolder(evt, teller.HeroId);
                if (!isHolder)
                {
                    // 走漏的秘密，旁人講
                    double honor = _traits?.Of(teller.HeroId)?.Honor ?? teller.Traits?.Honor ?? 0;
                    double valor = _traits?.Of(teller.HeroId)?.Valor ?? teller.Traits?.Valor ?? 0;

                    if (honor >= 1)
                    {
                        note = $"{evt.EventId}: leaked secret, teller honor {honor:+0;-0;0} >= 1 - honorable people do not spread secrets";
                        return CandidateRejection.LeakedSecretHonorable;
                    }

                    if (valor <= -1 && !isFamiliar)
                    {
                        note = $"{evt.EventId}: leaked secret, teller valor {valor:+0;-0;0} <= -1 and not familiar with player";
                        return CandidateRejection.LeakedSecretCautiousStranger;
                    }
                }
                else
                {
                    // 秘密的當事人講自己的秘密
                    bool isCloseTrust = teller.IsPlayerSpouse || teller.IsPlayerCompanion || teller.IsPlayerClanMember;
                    if (_dialogueWorld != null)
                    {
                        isCloseTrust = isCloseTrust
                            || _dialogueWorld.IsPlayerSpouse(teller.HeroId)
                            || _dialogueWorld.IsPlayerCompanion(teller.HeroId)
                            || _dialogueWorld.IsPlayerClanMember(teller.HeroId);
                    }

                    if (!isCloseTrust && will < _config.Dialogue.SecretLine)
                    {
                        note = $"{evt.EventId}: secret holder willingness {will:F1} < secret line {_config.Dialogue.SecretLine:F1}";
                        return CandidateRejection.SecretHolderNotWilling;
                    }
                }
            }

            // 玩家未知 -> 合格
            if (!candidate.PlayerExistingHop.HasValue)
            {
                return CandidateRejection.None;
            }

            int playerHop = candidate.PlayerExistingHop.Value;

            // 玩家已知 -> 重述升級判定
            if (!_config.Dialogue.AllowRicherRetell)
            {
                note = $"{evt.EventId}: player already knows it (hop {playerHop}); richer retell is switched off";
                return CandidateRejection.RetellDisabled;
            }

            int newHop = candidate.TellerHop + 1;
            var newFacts = _engine.FactsAtHop(evt, newHop, teller.HeroId);
            var newFactIds = new HashSet<string>(newFacts.Select(f => f.Id));

            var entry = evt.EntryFor(_playerHeroId);
            HashSet<string> knownFactIds;
            if (entry != null && entry.KnownFactIds != null)
            {
                knownFactIds = new HashSet<string>(entry.KnownFactIds);
            }
            else if (entry != null)
            {
                var oldFacts = _engine.FactsAtHop(evt, entry.Hop, entry.SourceHeroId ?? string.Empty);
                knownFactIds = new HashSet<string>(oldFacts.Select(f => f.Id));
            }
            else
            {
                var oldFacts = _engine.FactsAtHop(evt, playerHop, string.Empty);
                knownFactIds = new HashSet<string>(oldFacts.Select(f => f.Id));
            }

            if (newFactIds.Any(id => !knownFactIds.Contains(id)))
            {
                return CandidateRejection.None;
            }

            note = $"{evt.EventId}: teller at hop {candidate.TellerHop} would give {newFactIds.Count} fact(s) (detail level {newHop}), "
                 + $"but the player (heard at hop {playerHop}) already knows all {knownFactIds.Count} of them - nothing new to add";
            return CandidateRejection.RetellNoNewFacts;
        }

        private bool IsSecretHolder(WorldEvent evt, string heroId)
        {
            if (evt.Origin != EventOrigin.Secret) return false;
            string? role = evt.RoleOf(heroId);
            if (role == null) return false;
            var knowing = _getTemplate?.Invoke(evt.Type)?.KnowingRoles;
            if (knowing == null || knowing.Count == 0) return true;
            return knowing.Any(k => string.Equals(k, role, StringComparison.OrdinalIgnoreCase));
        }

        public List<string> ToldFactIdsOf(RumorOffer offer, WorldEvent evt, string tellerHeroId)
        {
            if (offer.ToldFactIds != null) return offer.ToldFactIds.ToList();
            return _engine.FactsAtHop(evt, offer.ResultingPlayerHop, tellerHeroId).Select(f => f.Id).ToList();
        }

        public void ApplyOffer(RumorOffer offer, WorldEvent evt, string tellerHeroId, double day)
        {
            if (offer == null || evt == null) return;

            var entry = evt.EntryFor(_playerHeroId);
            if (entry == null)
            {
                evt.KnownBy.Add(new KnownByEntry
                {
                    HeroId = _playerHeroId,
                    Hop = offer.PlayerHop,
                    LearnedDay = day,
                    SourceHeroId = tellerHeroId,
                    KnownFactIds = ToldFactIdsOf(offer, evt, tellerHeroId)
                });
                return;
            }

            int oldHop = entry.Hop;
            string oldSource = entry.SourceHeroId ?? string.Empty;

            var oldFactIds = entry.KnownFactIds != null
                ? new HashSet<string>(entry.KnownFactIds)
                : new HashSet<string>(_engine.FactsAtHop(evt, oldHop, oldSource).Select(f => f.Id));

            var newFactIds = ToldFactIdsOf(offer, evt, tellerHeroId);
            foreach (var id in newFactIds)
            {
                oldFactIds.Add(id);
            }

            entry.KnownFactIds = oldFactIds.ToList();
            entry.Hop = Math.Min(entry.Hop, offer.PlayerHop);
            entry.SourceHeroId = tellerHeroId;
        }
    }
}
