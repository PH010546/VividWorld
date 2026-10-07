#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using VividWorld.Core.Config;
using VividWorld.Core.Memory;

namespace VividWorld.Core.Events
{
    public enum MadeUpTalkCandidateStatus
    {
        Chosen,
        Eligible,
        AlreadyHeard,
        Forgotten,
        NotMadeUpTalk,
        NotLoadable
    }

    public sealed class MadeUpTalkCandidateEvaluation
    {
        public string EventId { get; }
        public WorldEvent? Event { get; }
        public MadeUpTalkCandidateStatus Status { get; set; }
        public string Reason { get; set; }

        public MadeUpTalkCandidateEvaluation(string eventId, WorldEvent? evt, MadeUpTalkCandidateStatus status, string reason)
        {
            EventId = eventId ?? string.Empty;
            Event = evt;
            Status = status;
            Reason = reason ?? string.Empty;
        }
    }

    public sealed class MadeUpTalkPickResult
    {
        public WorldEvent? ChosenEvent { get; set; }
        public int TotalKnown { get; set; }
        public int TotalMadeUpKnown { get; set; }
        public int AlreadyHeardCount { get; set; }
        public int ForgottenCount { get; set; }
        public int NotMadeUpCount { get; set; }
        public int NotLoadableCount { get; set; }
        public IReadOnlyList<MadeUpTalkCandidateEvaluation> Evaluations { get; set; } = Array.Empty<MadeUpTalkCandidateEvaluation>();
    }

    /// <summary>
    /// 從 NPC 知情的傳聞中，挑選其知道且玩家尚未聽過的編造之言；並為後續回應挑選合適的講述者。
    /// 純函式，不存取遊戲狀態。
    /// </summary>
    public static class MadeUpTalkPicker
    {
        public static MadeUpTalkPickResult PickMadeUpTalk(
            IReadOnlyList<string>? knownEventIds,
            Func<string, WorldEvent?> loadEvent,
            string tellerHeroId,
            string playerHeroId,
            double day,
            MemoryConfig? memoryConfig,
            Func<string, bool>? isPlayerKnowerExtra = null)
        {
            if (loadEvent == null) throw new ArgumentNullException(nameof(loadEvent));

            var result = new MadeUpTalkPickResult
            {
                TotalKnown = knownEventIds?.Count ?? 0
            };

            if (knownEventIds == null || knownEventIds.Count == 0)
            {
                return result;
            }

            var evaluations = new List<MadeUpTalkCandidateEvaluation>(knownEventIds.Count);
            var eligible = new List<(WorldEvent evt, MadeUpTalkCandidateEvaluation eval)>();

            foreach (var eventId in knownEventIds)
            {
                if (string.IsNullOrEmpty(eventId))
                {
                    result.NotLoadableCount++;
                    evaluations.Add(new MadeUpTalkCandidateEvaluation(eventId ?? string.Empty, null, MadeUpTalkCandidateStatus.NotLoadable, "event id is empty"));
                    continue;
                }

                var evt = loadEvent(eventId);
                if (evt == null)
                {
                    result.NotLoadableCount++;
                    evaluations.Add(new MadeUpTalkCandidateEvaluation(eventId, null, MadeUpTalkCandidateStatus.NotLoadable, "could not load event"));
                    continue;
                }

                if (!MadeUpTalk.IsHearsayOnly(evt))
                {
                    result.NotMadeUpCount++;
                    evaluations.Add(new MadeUpTalkCandidateEvaluation(eventId, evt, MadeUpTalkCandidateStatus.NotMadeUpTalk, "not hearsay-only made-up talk"));
                    continue;
                }

                result.TotalMadeUpKnown++;

                var tellerEntry = evt.EntryFor(tellerHeroId);
                if (tellerEntry == null)
                {
                    result.ForgottenCount++;
                    evaluations.Add(new MadeUpTalkCandidateEvaluation(eventId, evt, MadeUpTalkCandidateStatus.Forgotten, "teller is not recorded as knower"));
                    continue;
                }

                if (Forgetting.IsForgotten(evt, tellerEntry, day, playerHeroId, memoryConfig ?? new MemoryConfig()))
                {
                    result.ForgottenCount++;
                    evaluations.Add(new MadeUpTalkCandidateEvaluation(eventId, evt, MadeUpTalkCandidateStatus.Forgotten, "teller has forgotten event"));
                    continue;
                }

                bool playerAlreadyHeard = evt.IsKnownBy(playerHeroId) || (isPlayerKnowerExtra != null && isPlayerKnowerExtra(eventId));
                if (playerAlreadyHeard)
                {
                    result.AlreadyHeardCount++;
                    evaluations.Add(new MadeUpTalkCandidateEvaluation(eventId, evt, MadeUpTalkCandidateStatus.AlreadyHeard, "player has already heard event"));
                    continue;
                }

                var eval = new MadeUpTalkCandidateEvaluation(eventId, evt, MadeUpTalkCandidateStatus.Eligible, string.Empty);
                evaluations.Add(eval);
                eligible.Add((evt, eval));
            }

            result.Evaluations = evaluations;

            if (eligible.Count == 0)
            {
                return result;
            }

            var sorted = eligible.OrderByDescending(c => c.evt.Day)
                                 .ThenBy(c => c.evt.EventId, StringComparer.Ordinal)
                                 .ToList();

            var chosen = sorted[0];
            chosen.eval.Status = MadeUpTalkCandidateStatus.Chosen;
            result.ChosenEvent = chosen.evt;

            for (int i = 1; i < sorted.Count; i++)
            {
                sorted[i].eval.Status = MadeUpTalkCandidateStatus.Eligible;
                sorted[i].eval.Reason = "eligible (higher priority candidate chosen)";
            }

            return result;
        }

        public static KnownByEntry? PickResponseSpeaker(
            WorldEvent? responseEvent,
            string playerHeroId,
            double day,
            MemoryConfig? memoryConfig)
        {
            if (responseEvent?.KnownBy == null || responseEvent.KnownBy.Count == 0)
            {
                return null;
            }

            var cfg = memoryConfig ?? new MemoryConfig();

            var candidates = responseEvent.KnownBy
                .Where(k => k != null &&
                            !string.IsNullOrEmpty(k.HeroId) &&
                            !string.Equals(k.HeroId, playerHeroId, StringComparison.Ordinal) &&
                            !Forgetting.IsForgotten(responseEvent, k, day, playerHeroId, cfg))
                .OrderBy(k => k.Hop)
                .ThenBy(k => k.HeroId, StringComparer.Ordinal)
                .ToList();

            return candidates.FirstOrDefault();
        }

        public static IReadOnlyList<WorldEvent> SortResponseEvents(IEnumerable<WorldEvent> responseEvents)
        {
            if (responseEvents == null) return Array.Empty<WorldEvent>();

            return responseEvents
                .Where(e => e != null)
                .OrderBy(e => e.Day)
                .ThenBy(e => e.EventId, StringComparer.Ordinal)
                .ToList();
        }
    }
}
