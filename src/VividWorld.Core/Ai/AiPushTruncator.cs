using System;
using System.Collections.Generic;
using System.Linq;
using VividWorld.Core.Events;

namespace VividWorld.Core.Ai
{
    public static class AiPushTruncator
    {
        public const int MaxChars = 300;

        /// <summary>
        /// Truncates facts so the assembled rumor does not exceed MaxChars (300).
        /// Truncation only happens between facts, never in the middle of a fact.
        /// </summary>
        public static (string Text, bool IsTruncated, IReadOnlyList<Fact> KeptFacts) Truncate(
            IReadOnlyList<Fact> facts,
            Func<IReadOnlyList<Fact>, string> render)
        {
            if (facts == null || facts.Count == 0)
            {
                return (string.Empty, false, Array.Empty<Fact>());
            }

            if (render == null) throw new ArgumentNullException(nameof(render));

            string fullText = render(facts);
            if (fullText.Length <= MaxChars)
            {
                return (fullText, false, facts);
            }

            // Try fewer whole facts from N-1 down to 1
            for (int count = facts.Count - 1; count >= 1; count--)
            {
                var subset = facts.Take(count).ToList();
                string subText = render(subset);
                if (subText.Length <= MaxChars)
                {
                    return (subText, true, subset);
                }
            }

            // Even 1 fact exceeds 300 chars, keep that 1 whole fact without cutting it in half
            var single = new[] { facts[0] };
            return (render(single), true, single);
        }
    }
}
