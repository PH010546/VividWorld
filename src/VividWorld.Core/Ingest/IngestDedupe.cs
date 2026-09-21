#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using VividWorld.Core.Events;
using VividWorld.Core.Persistence;

namespace VividWorld.Core.Ingest
{
    public static class IngestDedupe
    {
        /// <summary>索引裡是否已經有「同型別、同一整數天、同一組參與者」的事件；有就回傳它的 eventId。</summary>
        public static string? FindSameSubmission(RumorIndex? index, EventSubmission? submission)
        {
            if (index == null || submission == null || index.Entries == null || string.IsNullOrEmpty(submission.Type))
            {
                return null;
            }

            var subParticipants = new HashSet<string>(
                submission.Participants != null
                    ? submission.Participants.Values.Where(id => !string.IsNullOrEmpty(id))
                    : Enumerable.Empty<string>(),
                StringComparer.Ordinal);

            double subFloorDay = Math.Floor(submission.Day);

            foreach (var entry in index.Entries)
            {
                if (entry == null) continue;
                if (!string.Equals(entry.Type, submission.Type, StringComparison.Ordinal)) continue;
                if (Math.Floor(entry.Day) != subFloorDay) continue;

                var entryParticipants = new HashSet<string>(
                    entry.ParticipantHeroIds != null
                        ? entry.ParticipantHeroIds.Where(id => !string.IsNullOrEmpty(id))
                        : Enumerable.Empty<string>(),
                    StringComparer.Ordinal);

                if (subParticipants.SetEquals(entryParticipants))
                {
                    return entry.EventId;
                }
            }

            return null;
        }
    }
}
