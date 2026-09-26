#nullable enable
using System;
using VividWorld.Core.Config;
using VividWorld.Core.Persistence;

namespace VividWorld.Core.Memory
{
    /// <summary>
    /// 基於索引的知情者記憶述詞（卡 MF3b §1.3）。
    /// </summary>
    public static class IndexMemory
    {
        public static bool Remembers(RumorIndexEntry entry, string heroId, double today, MemoryConfig? cfg)
        {
            if (cfg == null || !cfg.Enabled) return true;
            if (entry == null || string.IsNullOrEmpty(heroId)) return true;

            if (entry.Secret && !entry.Leaked) return true;

            if (entry.ForgetDays == null || !entry.ForgetDays.TryGetValue(heroId, out double forgetDay))
            {
                return true;
            }

            if (today >= forgetDay) return false;

            return true;
        }
    }
}
