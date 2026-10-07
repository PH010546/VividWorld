using System;
using System.Collections.Generic;
using System.Linq;
using VividWorld.Core.Channels;
using VividWorld.Core.Config;
using VividWorld.Core.Diagnostics;
using VividWorld.Core.Events;
using VividWorld.Core.Memory;
using VividWorld.Core.Util;

namespace VividWorld.Core.Rumors
{
    public sealed class RumorEngine
    {
        private readonly VividWorldConfig _config;
        private readonly IFactRetentionPolicy _retention;
        private readonly IFactEmbellishmentPolicy _embellishment;
        private readonly IPropagationChannel _channel;
        private readonly IHeroTraitLookup _traits;
        private readonly IDeterministicRng _rng;
        private readonly long _campaignSeed;
        private readonly string _playerHeroId;
        private readonly Func<string, VividWorld.Core.Catalog.EventTemplate?>? _getTemplate;
        private VividWorld.Core.Feelings.IFeelingWorld? _feelingWorld;
        private ILogSink? _log;
        private bool _warnedNullFeelingWorld;

        public RumorEngine(VividWorldConfig config,
                           IFactRetentionPolicy retention,
                           IFactEmbellishmentPolicy embellishment,
                           IPropagationChannel channel,
                           IHeroTraitLookup traits,
                           IDeterministicRng rng,
                           long campaignSeed,
                           string playerHeroId,
                           Func<string, VividWorld.Core.Catalog.EventTemplate?>? getTemplate = null,
                           VividWorld.Core.Feelings.IFeelingWorld? feelingWorld = null,
                           ILogSink? log = null)
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));
            _retention = retention ?? throw new ArgumentNullException(nameof(retention));
            _embellishment = embellishment ?? throw new ArgumentNullException(nameof(embellishment));
            _channel = channel ?? throw new ArgumentNullException(nameof(channel));
            _traits = traits ?? throw new ArgumentNullException(nameof(traits));
            _rng = rng ?? throw new ArgumentNullException(nameof(rng));
            _campaignSeed = campaignSeed;
            _playerHeroId = playerHeroId ?? string.Empty;
            _getTemplate = getTemplate;
            _feelingWorld = feelingWorld;
            _log = log;
        }

        public VividWorld.Core.Feelings.IFeelingWorld? FeelingWorld
        {
            get => _feelingWorld;
            set => _feelingWorld = value;
        }

        public ILogSink? Log
        {
            get => _log;
            set => _log = value;
        }

        public IHeroTraitLookup Traits => _traits;
        public long CampaignSeed => _campaignSeed;
        public Func<string, VividWorld.Core.Catalog.EventTemplate?>? TemplateProvider => _getTemplate;

        public int MaxHopFor(WorldEvent evt)
        {
            if (evt == null) return 0;
            int drama = evt.DramaBand;
            var dramaMaxHop = _config.Propagation.DramaMaxHop;
            if (dramaMaxHop == null || dramaMaxHop.Length == 0) return 3;
            int index = Math.Min(drama - 1, dramaMaxHop.Length - 1);
            return dramaMaxHop[index];
        }

        /// <summary>FactsAtHop(evt, hop) 的單參數多載用空字串當講述者——門檻策略下與講述者無關，
        /// 機率策略下則是一個固定的匿名講述者。這個多載只給呈現層當「一般而言第 N 手長什麼樣」用，
        /// 傳播與對話一律用三參數版。</summary>
        public IReadOnlyList<Fact> FactsAtHop(WorldEvent evt, int hop)
        {
            if (evt == null || !evt.IsVisibleToRumorSystem)
            {
                return Array.Empty<Fact>();
            }
            return FactsAtHop(evt, hop, string.Empty);
        }

        public IReadOnlyList<Fact> FactsAtHop(WorldEvent evt, int hop, string tellerHeroId)
        {
            if (evt == null || !evt.IsVisibleToRumorSystem)
            {
                return Array.Empty<Fact>();
            }
            var retained = _retention.Retain(evt, hop, tellerHeroId ?? string.Empty);
            var tellerProfile = !string.IsNullOrEmpty(tellerHeroId) ? _traits.Of(tellerHeroId!) : null;
            if (tellerProfile != null)
            {
                return _embellishment.Embellish(evt, retained, hop, tellerProfile);
            }
            return retained;
        }

        public LeakOutcome TryLeak(WorldEvent evt, double day)
        {
            if (evt == null || evt.State.Leaked || evt.Origin != EventOrigin.Secret)
            {
                return LeakOutcome.None;
            }

            var insiders = new List<TraitProfile>();
            foreach (var entry in evt.KnownBy)
            {
                if (string.IsNullOrEmpty(entry.HeroId)) continue;
                if (entry.HeroId == _playerHeroId) continue;
                var profile = _traits.Of(entry.HeroId);
                if (profile != null)
                {
                    insiders.Add(profile);
                }
            }

            var outcome = LeakRoll.Roll(evt, day, insiders, _config.Leak, _rng, _campaignSeed);
            if (outcome.Leaked)
            {
                evt.State.Leaked = true;
                evt.State.LeakedDay = day;
                evt.State.LeakerHeroId = outcome.LeakerHeroId;
            }

            return outcome;
        }

        public PropagationOutcome PropagateOnce(WorldEvent evt, double day, int hourOfDay)
        {
            // 步驟 1：可見性收口
            if (evt == null || !evt.IsVisibleToRumorSystem)
            {
                return PropagationOutcome.Nothing;
            }

            // 步驟 2：挑一個講述者
            int maxHop = MaxHopFor(evt);

            var candidates = evt.KnownBy
                .Where(k => TellerEligibility.Check(evt, k.HeroId, day, maxHop, _playerHeroId, _config, _traits, _getTemplate) == TellReason.Ok)
                .ToList();

            if (candidates.Count == 0)
            {
                evt.State.LastPropagatedDay = day;
                return PropagationOutcome.Nothing;
            }

            // 以 (MaxHop - hop) * TellFactor(c) 為權重呼叫 rng.PickWeighted（不是 Pick）
            var weights = candidates.Select(c => (double)(maxHop - c.Hop) * Forgetting.TellFactor(c, _config.Memory)).ToList();
            long tellerSeed = RumorSeed.Of(_campaignSeed, evt.EventId, "teller", RumorSeed.DayBucket(day), hourOfDay);
            int pickedIndex = _rng.PickWeighted(weights, tellerSeed);
            var teller = candidates[pickedIndex];

            return PropagateFromTellerInternal(evt, teller, day);
        }

        public TopicChoice ChooseTopic(string tellerHeroId, IReadOnlyList<WorldEvent> activeKnownEvents, double day, int hourOfDay)
        {
            var candidates = new List<TopicCandidate>();
            var exclusions = new List<TopicExclusion>();

            if (activeKnownEvents != null)
            {
                foreach (var evt in activeKnownEvents)
                {
                    if (evt == null) continue;
                    int maxHop = MaxHopFor(evt);
                    var reason = TellerEligibility.Check(evt, tellerHeroId, day, maxHop, _playerHeroId, _config, _traits, _getTemplate, out string? detail);
                    if (reason != TellReason.Ok)
                    {
                        exclusions.Add(new TopicExclusion(evt.EventId, reason, detail));
                    }
                    else
                    {
                        var entry = evt.EntryFor(tellerHeroId)!;
                        int drama = evt.DramaBand;
                        double tell = Forgetting.TellFactor(entry, _config.Memory);

                        double rawFreshness = _config.Scheduling.RumorLifetimeDays <= 0.0
                            ? 1.0
                            : 1.0 - (day - evt.Day) / _config.Scheduling.RumorLifetimeDays;
                        double freshness = Math.Max(_config.Propagation.TopicFreshnessFloor, Math.Min(1.0, rawFreshness));

                        var dramaWeights = _config.Propagation.TopicDramaWeight;
                        double topicDramaWeight = (dramaWeights != null && dramaWeights.Length >= drama)
                            ? dramaWeights[drama - 1]
                            : 1.0;

                        double weight = topicDramaWeight * tell * freshness;
                        candidates.Add(new TopicCandidate(evt.EventId, drama, tell, freshness, weight, evt.DramaWeightTen));
                    }
                }
            }

            if (candidates.Count == 0)
            {
                return new TopicChoice(tellerHeroId, candidates, exclusions, -1, null, 0.0);
            }

            var weights = candidates.Select(c => c.Weight).ToList();
            long seed = RumorSeed.Of(_campaignSeed, "topic", tellerHeroId, RumorSeed.DayBucket(day), hourOfDay);
            int pickedIndex = _rng.PickWeighted(weights, seed);

            double sum = weights.Sum();
            double chance = sum <= 0.0
                ? (1.0 / candidates.Count)
                : (candidates[pickedIndex].Weight / sum);

            string pickedEventId = candidates[pickedIndex].EventId;
            return new TopicChoice(tellerHeroId, candidates, exclusions, pickedIndex, pickedEventId, chance);
        }

        public PropagationOutcome PropagateFromTeller(WorldEvent evt, string tellerHeroId, double day, int hourOfDay)
        {
            if (evt == null || !evt.IsVisibleToRumorSystem)
            {
                return PropagationOutcome.Nothing;
            }

            int maxHop = MaxHopFor(evt);
            var reason = TellerEligibility.Check(evt, tellerHeroId, day, maxHop, _playerHeroId, _config, _traits, _getTemplate);
            if (reason != TellReason.Ok)
            {
                return PropagationOutcome.Nothing;
            }

            var teller = evt.EntryFor(tellerHeroId);
            if (teller == null)
            {
                return PropagationOutcome.Nothing;
            }

            return PropagateFromTellerInternal(evt, teller, day);
        }

        private PropagationOutcome PropagateFromTellerInternal(WorldEvent evt, KnownByEntry teller, double day)
        {
            string tellerId = teller.HeroId;
            int tellerHop = teller.Hop;

            // 步驟 3：通道查詢（一次局部查詢。整個方法只有這一處查詢）
            var contacts = _channel.ContactsOf(tellerId, _config.Propagation.MaxContactsPerQuery);

            // 步驟 4：對每個接觸者依回傳順序判定
            var newKnowers = new List<KnownByEntry>();
            var reheard = new List<RehearRecord>();

            var tellerProfile = _traits.Of(tellerId);
            double tellerTraitFactor = 1.0;
            if (tellerProfile != null)
            {
                var tw = _config.Propagation.TellerTraitWeights;
                double sum = (tw.Generosity * tellerProfile.Generosity)
                           + (tw.Honor * tellerProfile.Honor)
                           + (tw.Calculating * tellerProfile.Calculating);
                double m = 1.0 + sum;
                if (m < _config.Propagation.TellerMultiplierMin) m = _config.Propagation.TellerMultiplierMin;
                if (m > _config.Propagation.TellerMultiplierMax) m = _config.Propagation.TellerMultiplierMax;
                tellerTraitFactor = m;
            }

            int drama = evt.DramaBand;
            double dramaMultiplier = 1.0;
            var dtm = _config.Propagation.DramaTellMultiplier;
            if (dtm != null && dtm.Length > 0)
            {
                int dIndex = Math.Min(drama - 1, dtm.Length - 1);
                dramaMultiplier = dtm[dIndex];
            }

            string? tellerRole = evt.RoleOf(tellerId);
            bool tellerIsPerpetrator = string.Equals(tellerRole, "mastermind", StringComparison.OrdinalIgnoreCase)
                                    || string.Equals(tellerRole, "agent", StringComparison.OrdinalIgnoreCase);

            double selfIncriminationMultiplier = tellerIsPerpetrator
                ? _config.Propagation.SelfIncriminationMultiplier
                : 1.0;

            var channelKinds = new Dictionary<string, ChannelKind>(StringComparer.Ordinal);
            double tellerTellFactor = Forgetting.TellFactor(teller, _config.Memory);

            // 講述者自己不信這則消息：照樣會傳，但傳得少。沒判過（null）或信（true）不影響。
            double disbelieverMultiplier = teller.Believes == false
                ? _config.FalseRumors.Belief.DisbelieverTellMultiplier
                : 1.0;

            // 診斷用：每位聽的人的狀況。只在開著講述者日誌時收集，關著時不配置任何東西。
            // 這裡只讀、只記，不擲骰、不改任何判斷，所以開與關傳話的結果完全相同。
            List<ContactObservation>? observed = _config.Debug.LogTellerTurns ? new List<ContactObservation>() : null;
            int visited = 0;

            // 領主之間傳話分層：每位聽的人都算出層與意願（診斷要看得到全貌），
            // 但只有 enabled 為真時才拿來擋人。分層不看 LogTellerTurns。
            var tiersCfg = _config.Propagation.TellTiers;
            int weightTen = evt.DramaWeightTen;
            int bigNewsLine = _config.Dialogue.BigNewsLine;

            ContactObservation Observe(ChannelLink l, double chance, ContactStatus status, string? heldBackReason = null)
            {
                double will = TellTierRule.Willingness(l.OwnRelation, tellerProfile, _config.Dialogue.AskTraitWeights);
                var tier = TellTierRule.Classify(l.IsFamily, l.OwnRelation, will, tiersCfg);
                double rf = RelationFactor.For(l.Kind, l.OwnRelation, _config.Relation);
                return new ContactObservation(l.HeroId, l.Kind, l.Relation, chance, status,
                    l.OwnRelation, l.IsFamily, will, tier, heldBackReason, rf);
            }

            if (contacts != null)
            {
                foreach (var link in contacts)
                {
                    visited++;
                    if (link == null || string.IsNullOrEmpty(link.HeroId)) continue;
                    if (link.HeroId == _playerHeroId) continue;

                    var existing = evt.EntryFor(link.HeroId);
                    bool forgotten = existing != null && Forgetting.IsForgotten(evt, existing, day, _playerHeroId, _config.Memory);
                    if (existing != null && !forgotten && (!_config.Memory.Enabled || existing.Interest == null))
                    {
                        observed?.Add(Observe(link, 0.0, ContactStatus.AlreadyKnows));
                        continue;
                    }
                    if (!IsEligible(link.HeroId))
                    {
                        observed?.Add(Observe(link, 0.0, ContactStatus.NotEligible));
                        continue;
                    }

                    // 分層：被擋的這一位不擲骰、不算機率，所以不會動到別人的骰。
                    if (tiersCfg.Enabled)
                    {
                        double ownWill = TellTierRule.Willingness(link.OwnRelation, tellerProfile, _config.Dialogue.AskTraitWeights);
                        var tier = TellTierRule.Classify(link.IsFamily, link.OwnRelation, ownWill, tiersCfg);
                        if (!TellTierRule.WouldStillTell(tier, weightTen, bigNewsLine))
                        {
                            observed?.Add(Observe(link, 0.0,
                                tier == TellTier.Hostile ? ContactStatus.HeldBackHostile : ContactStatus.HeldBackSmallNews));
                            continue;
                        }
                    }

                    // 醜事講不講：看說話的人與聽的人各自跟出醜的人的關係
                    if (_config.Propagation.ShamefulNews.Enabled)
                    {
                        if (_feelingWorld == null)
                        {
                            if (!_warnedNullFeelingWorld && ShamefulNewsRule.HasShamedPersons(evt, _getTemplate, tellerId))
                            {
                                _warnedNullFeelingWorld = true;
                                _log?.Warn("RumorEngine: IFeelingWorld is not provided; shameful news filtering is inactive.");
                            }
                        }
                        else
                        {
                            var shamefulEval = ShamefulNewsRule.Evaluate(
                                evt,
                                tellerId,
                                link.HeroId,
                                _feelingWorld,
                                _getTemplate,
                                _traits,
                                _config,
                                day,
                                _playerHeroId);

                            if (!shamefulEval.CanTell)
                            {
                                observed?.Add(Observe(link, 0.0, ContactStatus.HeldBackShameful, shamefulEval.HeldBackReason));
                                continue;
                            }
                        }
                    }

                    double channelWeight = _config.Propagation.ChannelWeights.For(link.Kind);
                    double relationFactor = RelationFactor.For(link.Kind, link.OwnRelation, _config.Relation);

                    string? contactRole = evt.RoleOf(link.HeroId);
                    bool contactIsTarget = string.Equals(contactRole, "target", StringComparison.OrdinalIgnoreCase);

                    double tellToSubjectMultiplier = (tellerIsPerpetrator && contactIsTarget)
                        ? _config.Propagation.TellToSubjectMultiplier
                        : 1.0;

                    double p = _config.Propagation.BaseTellChancePerContact
                             * channelWeight * link.Weight
                             * relationFactor
                             * dramaMultiplier
                             * tellerTraitFactor
                             * selfIncriminationMultiplier
                             * tellToSubjectMultiplier
                             * tellerTellFactor
                             * disbelieverMultiplier;

                    long contactSeed = RumorSeed.Of(_campaignSeed, evt.EventId, tellerId, link.HeroId, RumorSeed.DayBucket(day));
                    bool hit = _rng.Chance(p, contactSeed);
                    observed?.Add(Observe(link, p,
                        !hit ? ContactStatus.Missed : (existing != null ? ContactStatus.Reheard : ContactStatus.Told)));
                    if (hit)
                    {
                        if (existing != null)
                        {
                            bool toldBefore = HeardFrom.ToldBefore(existing, tellerId);
                            if (toldBefore && !forgotten)
                            {
                                reheard.Add(RehearRecord.SameTellerIgnored(existing));
                            }
                            else
                            {
                                HeardFrom.Materialize(existing);
                                var rule = RehearRule.Decide(existing, tellerHop, forgotten, _config.Memory);
                                int oldHop = existing.Hop;
                                if (rule.AdoptVersion)
                                {
                                    existing.Hop = tellerHop + 1;
                                    existing.SourceHeroId = tellerId;
                                }
                                if (!toldBefore)
                                {
                                    existing.HeardFromIds!.Add(tellerId);
                                    existing.HeardCount = (existing.HeardCount ?? 0) + 1;
                                }
                                existing.LastHeardDay = day;
                                reheard.Add(new RehearRecord(existing, oldHop, forgotten, rule,
                                    toldBefore ? RehearKind.SameTellerRelearned : RehearKind.Counted));
                            }
                        }
                        else
                        {
                            var newEntry = new KnownByEntry
                            {
                                HeroId = link.HeroId,
                                Hop = tellerHop + 1,
                                LearnedDay = day,
                                SourceHeroId = tellerId,
                                HeardFromIds = new List<string> { tellerId }
                            };
                            evt.KnownBy.Add(newEntry);
                            newKnowers.Add(newEntry);
                            channelKinds[link.HeroId] = link.Kind;

                            // 步驟 5：達到上限即停
                            if (newKnowers.Count >= _config.Propagation.MaxNewKnowersPerEventPerTick)
                            {
                                // 還沒輪到的聯絡人只補記一筆「沒輪到」：不擲骰、不算機率。
                                if (observed != null)
                                {
                                    for (int i = visited; i < contacts.Count; i++)
                                    {
                                        var rest = contacts[i];
                                        if (rest == null || string.IsNullOrEmpty(rest.HeroId)) continue;
                                        if (rest.HeroId == _playerHeroId) continue;
                                        observed.Add(Observe(rest, 0.0, ContactStatus.NotReached));
                                    }
                                }
                                break;
                            }
                        }
                    }
                }
            }

            // 步驟 7：一律設 LastPropagatedDay；若有新增知情者另設 LastNewKnowerDay
            evt.State.LastPropagatedDay = day;
            if (newKnowers.Count > 0)
            {
                evt.State.LastNewKnowerDay = day;
            }

            return new PropagationOutcome(true, tellerId, newKnowers, channelKinds, reheard)
            {
                DisbelieverMultiplier = disbelieverMultiplier,
                Contacts = (IReadOnlyList<ContactObservation>?)observed ?? Array.Empty<ContactObservation>()
            };
        }

        public DormancyReason? DormancyReasonFor(WorldEvent evt, double day)
        {
            if (evt == null || !evt.IsVisibleToRumorSystem)
            {
                return null;
            }

            int maxHop = MaxHopFor(evt);
            // 玩家不會把消息傳下去，他那一筆的手數不該決定傳播停不停：只看 NPC 知情者。
            var npcHolders = evt.KnownBy.Where(k => k.HeroId != _playerHeroId).ToList();
            bool allAtMaxHop = npcHolders.Count > 0 && npcHolders.All(k => k.Hop >= maxHop);
            if (allAtMaxHop)
            {
                return DormancyReason.AllAtMaxHop(npcHolders.Count, maxHop);
            }

            double age = day - evt.Day;
            bool lifetimeExpired = age > _config.Scheduling.RumorLifetimeDays;
            if (lifetimeExpired)
            {
                return DormancyReason.LifetimeExpired(age, _config.Scheduling.RumorLifetimeDays);
            }

            double staleFrom = evt.State.LastNewKnowerDay >= 0 ? evt.State.LastNewKnowerDay : evt.Day;
            double idle = day - staleFrom;
            bool isStale = idle > _config.Scheduling.StaleDays;
            if (isStale)
            {
                return DormancyReason.Stale(idle, _config.Scheduling.StaleDays);
            }

            var npcKnowers = evt.KnownBy.Where(k => k.HeroId != _playerHeroId).ToList();
            if (npcKnowers.Count > 0)
            {
                int outdatedCount = 0;
                int forgottenCount = 0;
                bool allInactive = true;

                foreach (var k in npcKnowers)
                {
                    bool outdated = Outdating.IsOutdated(k);
                    bool forgotten = Forgetting.IsForgotten(evt, k, day, _playerHeroId, _config.Memory);

                    if (outdated)
                    {
                        outdatedCount++;
                    }
                    else if (forgotten)
                    {
                        forgottenCount++;
                    }
                    else
                    {
                        allInactive = false;
                        break;
                    }
                }

                if (allInactive)
                {
                    if (outdatedCount == 0)
                    {
                        return DormancyReason.AllForgotten(npcKnowers.Count);
                    }
                    return DormancyReason.AllOutdated(npcKnowers.Count, outdatedCount, forgottenCount);
                }
            }

            return null;
        }

        public bool ShouldGoDormant(WorldEvent evt, double day)
        {
            return DormancyReasonFor(evt, day) != null;
        }

        private bool IsEligible(string heroId)
        {
            return Eligibility.IsEligible(_traits, heroId);
        }
    }
}
