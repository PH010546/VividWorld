using System;
using System.Collections.Generic;
using System.Linq;
using VividWorld.Core.Config;
using VividWorld.Core.Events;
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

        public RumorOfferSelector(VividWorldConfig cfg, RumorEngine engine, string playerHeroId)
        {
            _config = cfg ?? throw new ArgumentNullException(nameof(cfg));
            _engine = engine ?? throw new ArgumentNullException(nameof(engine));
            _playerHeroId = playerHeroId ?? string.Empty;
        }

        /// <summary>主動講述三個閘的**唯一**計算處（規格 §5.1、§7 行 1291-1293）。
        /// `WillVolunteer` 只回答「行不行」，`DecideOnVolunteer` 還要回答「是哪一個閘擋的」——
        /// 兩者都只准呼叫這裡。閘的述詞寫兩份，正是 M6a-fix2 那個 C-1 的成因。</summary>
        private VolunteerRefusal EvaluateVolunteerGates(HeroSocialProfile? teller, double day,
                                                        int volunteersAlreadyToday, out bool isCloseKin)
        {
            var d = _config.Dialogue;
            isCloseKin = d.NpcVolunteerAlwaysForCloseKin &&
                         (teller?.IsPlayerSpouse == true || teller?.IsPlayerCompanion == true || teller?.IsPlayerClanMember == true);

            // 1. 好感度或近親/夥伴
            if (teller == null || !(teller.RelationWithPlayer >= d.NpcVolunteerRelationGate || isCloseKin))
            {
                return VolunteerRefusal.RelationGate;
            }

            // 2. 冷卻 (LastVolunteeredDay < 0 時視為未曾主動講過)
            if (teller.LastVolunteeredDay >= 0 && (day - teller.LastVolunteeredDay) < d.VolunteerCooldownDays)
            {
                return VolunteerRefusal.Cooldown;
            }

            // 3. 每日上限
            if (volunteersAlreadyToday >= d.MaxVolunteersPerDay)
            {
                return VolunteerRefusal.DailyCap;
            }

            return VolunteerRefusal.None;
        }

        public bool WillVolunteer(HeroSocialProfile teller, double day, int volunteersAlreadyToday)
        {
            return EvaluateVolunteerGates(teller, day, volunteersAlreadyToday, out _) == VolunteerRefusal.None;
        }

        /// <summary>詢問意願的**唯一**計算處（規格 §7）。
        /// `WillAnswerAsk` 與 `DecideOnAsk` 都只准呼叫它，不准各寫一份——
        /// 同一個述詞有兩份定義，正是 M6a-fix2 那個 C-1 的成因。</summary>
        private void ComputeAskWillingness(HeroSocialProfile? teller,
                                           out int relation, out double willingness, out double threshold)
        {
            var d = _config.Dialogue;
            var w = d.AskTraitWeights ?? new AskTraitWeights();

            relation = teller?.RelationWithPlayer ?? 0;
            double generosity = teller?.Traits?.Generosity ?? 0;
            double honor = teller?.Traits?.Honor ?? 0;
            double calculating = teller?.Traits?.Calculating ?? 0;

            willingness = relation
                        + w.Generosity * generosity
                        + w.Honor * honor
                        + w.Calculating * calculating;
            threshold = d.AskWillingnessThreshold;
        }

        public bool WillAnswerAsk(HeroSocialProfile teller)
        {
            if (teller == null) return false;

            ComputeAskWillingness(teller, out int relation, out double willingness, out double threshold);
            return relation >= _config.Dialogue.AskRelationGate && willingness >= threshold;
        }

        public VolunteerDecision DecideOnVolunteer(HeroSocialProfile teller, IReadOnlyList<RumorCandidate> candidates,
                                                     double day, int volunteersAlreadyToday)
        {
            var d = _config.Dialogue;
            int relation = teller?.RelationWithPlayer ?? 0;
            var gate = EvaluateVolunteerGates(teller, day, volunteersAlreadyToday, out bool isCloseKin);

            var decision = new VolunteerDecision
            {
                Relation = relation,
                RelationGate = d.NpcVolunteerRelationGate,
                IsCloseKin = isCloseKin,
                Day = day,
                LastVolunteeredDay = teller?.LastVolunteeredDay ?? -1.0,
                CooldownDays = d.VolunteerCooldownDays,
                VolunteersToday = volunteersAlreadyToday,
                MaxVolunteersPerDay = d.MaxVolunteersPerDay,
                CandidateCount = candidates?.Count ?? 0
            };

            // `teller == null` 時 EvaluateVolunteerGates 必定回 RelationGate，所以第二個條件是恆真的，
            // 寫出來只為了讓可空性分析知道底下的 teller 不會是 null。
            if (gate != VolunteerRefusal.None || teller == null)
            {
                decision.Refusal = gate;
                return decision;
            }

            if (candidates == null || candidates.Count == 0)
            {
                decision.Refusal = VolunteerRefusal.NoKnownEvents;
                return decision;
            }

            var eligible = new List<RumorCandidate>();
            foreach (var c in candidates)
            {
                var rejection = Evaluate(teller, c, day, out string note);
                switch (rejection)
                {
                    case CandidateRejection.None:
                        eligible.Add(c);
                        break;

                    case CandidateRejection.NotVisible:
                        decision.FilteredNotVisible++;
                        break;

                    case CandidateRejection.FutureTimeline:
                        decision.FilteredFutureTimeline++;
                        break;

                    case CandidateRejection.RetellDisabled:
                    case CandidateRejection.RetellNotCloser:
                    case CandidateRejection.RetellNoNewFacts:
                        decision.FilteredPlayerKnows++;
                        if (!string.IsNullOrEmpty(note)) decision.FilterNotes.Add(note);
                        break;

                    default:
                        decision.FilteredOther++;
                        break;
                }
            }

            if (eligible.Count == 0)
            {
                decision.Refusal = VolunteerRefusal.AllCandidatesFiltered;
                return decision;
            }

            decision.Refusal = VolunteerRefusal.None;
            decision.Offer = CreateBestOffer(teller, eligible, day);
            return decision;
        }

        public RumorOffer? SelectVolunteered(HeroSocialProfile teller, IReadOnlyList<RumorCandidate> candidates,
                                             double day, int volunteersAlreadyToday)
        {
            return DecideOnVolunteer(teller, candidates, day, volunteersAlreadyToday).Offer;
        }

        public AskDecision DecideOnAsk(HeroSocialProfile teller, IReadOnlyList<RumorCandidate> candidates, double day)
        {
            var d = _config.Dialogue;
            ComputeAskWillingness(teller, out int relation, out double willingness, out double threshold);

            var decision = new AskDecision
            {
                Relation = relation,
                Willingness = willingness,
                Threshold = threshold,
                CandidateCount = candidates?.Count ?? 0
            };

            if (teller == null || relation < d.AskRelationGate)
            {
                decision.Refusal = AskRefusal.RelationGate;
                return decision;
            }

            if (willingness < threshold)
            {
                decision.Refusal = AskRefusal.WillingnessGate;
                return decision;
            }

            if (candidates == null || candidates.Count == 0)
            {
                decision.Refusal = AskRefusal.NoKnownEvents;
                return decision;
            }

            // 每一則候選都必須落進四個桶的其中一個：合格、秘密未洩漏、玩家已知、其他。
            // 這一行是本卡的重點——診斷行印出來的數字要加得起來，
            // 「三則全被濾掉，其中一則是因為玩家已知」這種話會讓人去追不存在的第二個原因。
            var eligible = new List<RumorCandidate>();
            foreach (var c in candidates)
            {
                var rejection = Evaluate(teller, c, day, out string note);
                switch (rejection)
                {
                    case CandidateRejection.None:
                        eligible.Add(c);
                        break;

                    case CandidateRejection.NotVisible:
                        decision.FilteredNotVisible++;
                        break;

                    case CandidateRejection.FutureTimeline:
                        decision.FilteredFutureTimeline++;
                        break;

                    // 三種都是「玩家已知」，桶維持一個（數字跟以往對得起來），
                    // 但成因完全不同，理由寫進 FilterNotes——
                    // 讓日誌答得出「他明明是目擊者，為什麼還是不講」。
                    case CandidateRejection.RetellDisabled:
                    case CandidateRejection.RetellNotCloser:
                    case CandidateRejection.RetellNoNewFacts:
                        decision.FilteredPlayerKnows++;
                        if (!string.IsNullOrEmpty(note)) decision.FilterNotes.Add(note);
                        break;

                    // 候選壞掉、或講述者自己不知情——理論上進不了候選，但別讓它消失
                    default:
                        decision.FilteredOther++;
                        break;
                }
            }

            if (eligible.Count == 0)
            {
                decision.Refusal = AskRefusal.AllCandidatesFiltered;
                return decision;
            }

            decision.Refusal = AskRefusal.None;
            decision.Offer = CreateBestOffer(teller, eligible, day);
            return decision;
        }

        public RumorOffer? SelectOnAsk(HeroSocialProfile teller, IReadOnlyList<RumorCandidate> candidates, double day)
        {
            return DecideOnAsk(teller, candidates, day).Offer;
        }

        public double Score(RumorCandidate candidate, double day)
        {
            if (candidate?.Event == null) return 0.0;

            var evt = candidate.Event;
            var d = _config.Dialogue;
            var s = _config.Scheduling;

            double lifetime = s.RumorLifetimeDays > 0 ? s.RumorLifetimeDays : 120.0;
            double freshness = Math.Max(0.0, Math.Min(1.0, 1.0 - (day - evt.Day) / lifetime));

            int maxHop = _engine.MaxHopFor(evt);
            double detail = Math.Max(0.0, Math.Min(1.0, (double)(maxHop - candidate.TellerHop) / Math.Max(1, maxHop)));

            double relevance = candidate.InvolvesHeroPlayerCaresAbout ? 1.0 : 0.0;

            double drama = Math.Max(1, Math.Min(5, evt.DramaWeight));
            double dramaNorm = drama / 5.0;

            double baseScore = d.ScoreDrama * dramaNorm
                             + d.ScoreFreshness * freshness
                             + d.ScoreDetail * detail
                             + d.ScoreRelevance * relevance;

            bool isRetell = candidate.PlayerExistingHop.HasValue;
            return baseScore * (isRetell ? d.ScoreRetellMultiplier : 1.0);
        }

        private RumorOffer CreateBestOffer(HeroSocialProfile teller, List<RumorCandidate> eligible, double day)
        {
            // 確定性排序：Score 降序 -> Day 降序 -> EventId 升序 (字典序)
            var sorted = eligible.OrderByDescending(c => Score(c, day))
                                 .ThenByDescending(c => c.Event.Day)
                                 .ThenBy(c => c.Event.EventId, StringComparer.Ordinal)
                                 .ToList();

            var best = sorted[0];
            bool isRetell = best.PlayerExistingHop.HasValue;
            int resultingPlayerHop = best.TellerHop + 1;
            var retainedFacts = _engine.FactsAtHop(best.Event, resultingPlayerHop, teller.HeroId);
            var composed = RumorTextComposer.Compose(best.Event, retainedFacts, _config.Presentation, isRetell: isRetell);

            return new RumorOffer
            {
                EventId = best.Event.EventId,
                TellerHop = best.TellerHop,
                ResultingPlayerHop = resultingPlayerHop,
                Composed = composed,
                IsRetell = isRetell,
                Score = Score(best, day)
            };
        }

        /// <summary>候選資格的**唯一**判定處：`DecideOnAsk` 與 `DecideOnVolunteer` 都只准呼叫它。
        /// 同一個述詞有兩份定義，正是 M6a-fix2 那個 C-1 的成因。</summary>
        private CandidateRejection Evaluate(HeroSocialProfile teller, RumorCandidate candidate, double day, out string note)
        {
            note = string.Empty;
            if (candidate?.Event == null) return CandidateRejection.EventMissing;
            var evt = candidate.Event;

            // 事件必須對傳聞系統可見
            if (!evt.IsVisibleToRumorSystem) return CandidateRejection.NotVisible;

            // 日期比今天晚 ⇒ 來自一條被抹掉的時間線（規格 §2.2.1）。
            // 判斷在 EventVisibility，這裡不自己寫一份。
            if (!EventVisibility.IsVisibleOn(evt, day)) return CandidateRejection.FutureTimeline;

            // 講述者必須知情
            if (!evt.IsKnownBy(teller.HeroId)) return CandidateRejection.TellerDoesNotKnow;

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

            // TellerHop + 1 < PlayerExistingHop
            if (candidate.TellerHop + 1 >= playerHop)
            {
                note = $"{evt.EventId}: teller is at hop {candidate.TellerHop}, so retelling lands the player at hop {candidate.TellerHop + 1} - no closer than the hop {playerHop} they already have";
                return CandidateRejection.RetellNotCloser;
            }

            // 檢查新碎片：newFactIds \ knownFactIds ≠ ∅
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

            // newFactIds 中是否有任何不在 knownFactIds 中的 Fact
            if (newFactIds.Any(id => !knownFactIds.Contains(id)))
            {
                return CandidateRejection.None;
            }

            // 更近的來源，卻一個新碎片都給不出來——這不是缺陷，是保留曲線在這兩個 hop 之間還沒開始失真。
            // 把兩邊的碎片數印出來，看的人才不用去猜。
            note = $"{evt.EventId}: teller at hop {candidate.TellerHop} would give hop {newHop} ({newFactIds.Count} facts), "
                 + $"but the player's hop {playerHop} already holds all {knownFactIds.Count} of them - nothing new to add";
            return CandidateRejection.RetellNoNewFacts;
        }

        public void ApplyOffer(RumorOffer offer, WorldEvent evt, string tellerHeroId, double day)
        {
            if (offer == null || evt == null) return;

            var entry = evt.EntryFor(_playerHeroId);
            if (entry == null)
            {
                // KnownFactIds 當場寫下來，不要留給日後重述判定去從 (hop, 來源) 反推第二次。
                // 反推目前算得出同一個答案，但那是同一件事的第二份定義（C-1 的形狀），
                // 而且潤色策略一旦不是純函數就會兩邊對不上。
                evt.KnownBy.Add(new KnownByEntry
                {
                    HeroId = _playerHeroId,
                    Hop = offer.ResultingPlayerHop,
                    LearnedDay = day,
                    SourceHeroId = tellerHeroId,
                    KnownFactIds = _engine.FactsAtHop(evt, offer.ResultingPlayerHop, tellerHeroId)
                                          .Select(f => f.Id).ToList()
                });
                return;
            }

            // 重述更新五條
            int oldHop = entry.Hop;
            string oldSource = entry.SourceHeroId ?? string.Empty;

            var oldFactIds = entry.KnownFactIds != null
                ? new HashSet<string>(entry.KnownFactIds)
                : new HashSet<string>(_engine.FactsAtHop(evt, oldHop, oldSource).Select(f => f.Id));

            var newFactIds = _engine.FactsAtHop(evt, offer.ResultingPlayerHop, tellerHeroId).Select(f => f.Id);
            foreach (var id in newFactIds)
            {
                oldFactIds.Add(id);
            }

            // (2) KnownFactIds 具體化為聯集
            entry.KnownFactIds = oldFactIds.ToList();

            // (5) Hop = min(舊, TellerHop + 1)
            entry.Hop = Math.Min(entry.Hop, offer.ResultingPlayerHop);

            // (5) LearnedDay 不動
            // (5) SourceHeroId 更新為重述者
            entry.SourceHeroId = tellerHeroId;
        }
    }
}
