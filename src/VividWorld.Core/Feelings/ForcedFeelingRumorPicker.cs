using System;
using System.Collections.Generic;
using System.Linq;
using VividWorld.Core.Config;
using VividWorld.Core.Events;
using VividWorld.Core.Memory;

namespace VividWorld.Core.Feelings
{
    public enum ForcedFeelingCandidateStatus
    {
        Chosen,
        Eligible,
        NoFeeling,
        AlreadyHeard,
        Forgotten,
        NotLoadable
    }

    public sealed class ForcedFeelingCandidateEvaluation
    {
        public string EventId { get; }
        public WorldEvent? Event { get; }
        public ForcedFeelingCandidateStatus Status { get; set; }
        public string Reason { get; set; }
        public FeelingDecision? Decision { get; }

        public ForcedFeelingCandidateEvaluation(string eventId, WorldEvent? evt, ForcedFeelingCandidateStatus status, string reason, FeelingDecision? decision = null)
        {
            EventId = eventId ?? string.Empty;
            Event = evt;
            Status = status;
            Reason = reason ?? string.Empty;
            Decision = decision;
        }
    }

    public sealed class ForcedFeelingPickResult
    {
        public WorldEvent? ChosenEvent { get; set; }
        public FeelingDecision? ChosenFeeling { get; set; }
        public int TotalKnown { get; set; }
        public int NoFeelingCount { get; set; }
        public int AlreadyHeardCount { get; set; }
        public int ForgottenCount { get; set; }
        public int NotLoadableCount { get; set; }
        public IReadOnlyList<ForcedFeelingCandidateEvaluation> Evaluations { get; set; } = Array.Empty<ForcedFeelingCandidateEvaluation>();
    }

    /// <summary>
    /// 從 NPC 知情的事件中，挑選第一則算得出感想且玩家尚未聽過的事件。
    /// 純函式，不存取遊戲狀態。
    /// </summary>
    public static class ForcedFeelingRumorPicker
    {
        public static ForcedFeelingPickResult Pick(
            IReadOnlyList<string> knownEventIds,
            Func<string, WorldEvent?> loadEvent,
            string tellerHeroId,
            string playerHeroId,
            double day,
            MemoryConfig? memoryConfig,
            Func<WorldEvent, string, FeelingDecision> resolveFeeling,
            Func<string, bool>? isPlayerKnowerExtra = null)
        {
            if (loadEvent == null) throw new ArgumentNullException(nameof(loadEvent));
            if (resolveFeeling == null) throw new ArgumentNullException(nameof(resolveFeeling));

            var result = new ForcedFeelingPickResult
            {
                TotalKnown = knownEventIds?.Count ?? 0
            };

            if (knownEventIds == null || knownEventIds.Count == 0)
            {
                return result;
            }

            var evaluations = new List<ForcedFeelingCandidateEvaluation>(knownEventIds.Count);
            var eligible = new List<(WorldEvent Event, FeelingDecision Decision, ForcedFeelingCandidateEvaluation Eval)>();

            foreach (var eventId in knownEventIds)
            {
                if (string.IsNullOrEmpty(eventId))
                {
                    result.NotLoadableCount++;
                    evaluations.Add(new ForcedFeelingCandidateEvaluation(eventId ?? string.Empty, null, ForcedFeelingCandidateStatus.NotLoadable, "event id is empty"));
                    continue;
                }

                var loaded = loadEvent(eventId);
                if (loaded == null)
                {
                    result.NotLoadableCount++;
                    evaluations.Add(new ForcedFeelingCandidateEvaluation(eventId, null, ForcedFeelingCandidateStatus.NotLoadable, "could not load event"));
                    continue;
                }

                WorldEvent evt = loaded;
                var tellerEntry = evt.EntryFor(tellerHeroId);
                if (tellerEntry == null)
                {
                    result.ForgottenCount++;
                    evaluations.Add(new ForcedFeelingCandidateEvaluation(eventId, evt, ForcedFeelingCandidateStatus.Forgotten, "teller is not recorded as knower"));
                    continue;
                }

                if (Forgetting.IsForgotten(evt, tellerEntry, day, playerHeroId, memoryConfig ?? new MemoryConfig()))
                {
                    result.ForgottenCount++;
                    evaluations.Add(new ForcedFeelingCandidateEvaluation(eventId, evt, ForcedFeelingCandidateStatus.Forgotten, "teller has forgotten event"));
                    continue;
                }

                bool playerAlreadyHeard = evt.IsKnownBy(playerHeroId) || (isPlayerKnowerExtra != null && isPlayerKnowerExtra(eventId));
                if (playerAlreadyHeard)
                {
                    result.AlreadyHeardCount++;
                    evaluations.Add(new ForcedFeelingCandidateEvaluation(eventId, evt, ForcedFeelingCandidateStatus.AlreadyHeard, "player has already heard event"));
                    continue;
                }

                var decision = resolveFeeling(evt, tellerHeroId);
                if (decision == null || !decision.Applied)
                {
                    result.NoFeelingCount++;
                    string reason = !string.IsNullOrEmpty(decision?.Reason) ? decision!.Reason : "no feeling produced";
                    evaluations.Add(new ForcedFeelingCandidateEvaluation(eventId, evt, ForcedFeelingCandidateStatus.NoFeeling, reason, decision));
                    continue;
                }

                var eval = new ForcedFeelingCandidateEvaluation(eventId, evt, ForcedFeelingCandidateStatus.Eligible, string.Empty, decision);
                evaluations.Add(eval);
                eligible.Add((evt, decision, eval));
            }

            result.Evaluations = evaluations;

            if (eligible.Count == 0)
            {
                return result;
            }

            // 確定性排序：發生日新的優先（Day 降序），同日照事件代號字典序（EventId 升序）
            var sorted = eligible.OrderByDescending(c => c.Event.Day)
                                 .ThenBy(c => c.Event.EventId, StringComparer.Ordinal)
                                 .ToList();

            var chosen = sorted[0];
            chosen.Eval.Status = ForcedFeelingCandidateStatus.Chosen;
            result.ChosenEvent = chosen.Event;
            result.ChosenFeeling = chosen.Decision;

            for (int i = 1; i < sorted.Count; i++)
            {
                sorted[i].Eval.Status = ForcedFeelingCandidateStatus.Eligible;
                sorted[i].Eval.Reason = "eligible (higher priority candidate chosen)";
            }

            return result;
        }
    }
}
