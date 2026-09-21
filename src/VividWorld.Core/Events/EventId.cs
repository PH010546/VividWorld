using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace VividWorld.Core.Events
{
    public static class EventId
    {
        private const ulong FnvOffsetBasis = 14695981039346656037UL;
        private const ulong FnvPrime = 1099511628211UL;

        /// <summary>"evt_{day:0000}_{hash4}"，hash4 = FNV-1a 64 over
        /// (type|排序後的參與者 heroId|day) 取小寫十六進位末 4 碼。</summary>
        public static string Mint(string type, double day, IEnumerable<string> participantHeroIds,
                                  Func<string, bool> isTaken)
        {
            long dayInt = (long)Math.Floor(day);
            string dayStr = dayInt.ToString("0000", CultureInfo.InvariantCulture);

            var sortedParticipants = (participantHeroIds ?? Enumerable.Empty<string>())
                .OrderBy(x => x, StringComparer.Ordinal);
            string participantPart = string.Join(",", sortedParticipants);
            string payload = $"{type}|{participantPart}|{dayInt}";

            string hash4 = ComputeHash4(payload);
            string initialId = $"evt_{dayStr}_{hash4}";

            if (isTaken == null || !isTaken(initialId))
            {
                return initialId;
            }

            for (int n = 1; n <= 10000; n++)
            {
                string saltedPayload = $"{initialId}:{n}";
                string candidateHash4 = ComputeHash4(saltedPayload);
                string candidateId = $"evt_{dayStr}_{candidateHash4}";

                if (!isTaken(candidateId))
                {
                    return candidateId;
                }
            }

            throw new InvalidOperationException($"Could not generate unique event ID after 10000 attempts for type '{type}' on day {day}.");
        }

        private static string ComputeHash4(string text)
        {
            ulong hash = FnvOffsetBasis;
            byte[] bytes = Encoding.UTF8.GetBytes(text);
            for (int i = 0; i < bytes.Length; i++)
            {
                hash ^= bytes[i];
                hash *= FnvPrime;
            }
            return (hash & 0xFFFF).ToString("x4", CultureInfo.InvariantCulture);
        }
    }
}
