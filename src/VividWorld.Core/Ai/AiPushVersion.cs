using System;
using System.Collections.Generic;
using System.Linq;

namespace VividWorld.Core.Ai
{
    public static class AiPushVersion
    {
        public static string Compute(IEnumerable<string>? factIds, bool isCorrection)
        {
            var sorted = (factIds ?? Enumerable.Empty<string>())
                .OrderBy(id => id, StringComparer.Ordinal);
            string baseVer = string.Join(",", sorted);
            return isCorrection ? baseVer + ":corr" : baseVer;
        }
    }
}
