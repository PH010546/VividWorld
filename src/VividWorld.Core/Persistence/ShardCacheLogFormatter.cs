#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;

namespace VividWorld.Core.Persistence
{
    /// <summary>
    /// 分片快取釋放日誌版型（卡 MF3c §3）。
    /// </summary>
    public static class ShardCacheLogFormatter
    {
        public static string FormatReleased(int n, int k, IEnumerable<string> releasedKeys, int m, int e)
        {
            string keysStr = string.Join(", ", releasedKeys ?? Array.Empty<string>());
            return string.Format(CultureInfo.InvariantCulture,
                "Shard cache: released {0} shard(s) idle for {1}+ flushes ({2}); {3} shard(s) / {4} event(s) still cached",
                n, k, keysStr, m, e);
        }

        public static string FormatWorldStatus(int m, int e, int r, int k)
        {
            string disabledPart = k == 0 ? ", disabled" : string.Empty;
            return string.Format(CultureInfo.InvariantCulture,
                "Shard cache: {0} shard(s) / {1} event(s) in memory, {2} released so far (release after {3} idle flushes{4})",
                m, e, r, k, disabledPart);
        }
    }
}
