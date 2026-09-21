using System;
using VividWorld.Core.Config;
using VividWorld.Core.Events;

namespace VividWorld.Core.Memory
{
    public static class Forgetting
    {
        public static double TellFactor(double? interest, MemoryConfig cfg)
        {
            if (cfg == null || !cfg.Enabled || interest == null)
            {
                return 1.0;
            }

            return cfg.TellFactorMin + (1.0 - cfg.TellFactorMin) * interest.Value;
        }

        public static double TellFactor(KnownByEntry? entry, MemoryConfig cfg)
        {
            return TellFactor(entry?.Interest, cfg);
        }

        public static bool IsForgotten(WorldEvent evt, KnownByEntry entry, double day, string playerHeroId, MemoryConfig cfg)
        {
            if (cfg == null || !cfg.Enabled) return false;
            if (entry == null) return false;
            if (string.Equals(entry.HeroId, playerHeroId, StringComparison.Ordinal)) return false;

            // 未洩漏的秘密：知情者不遺忘
            if (evt != null && evt.Origin == EventOrigin.Secret && !evt.State.Leaked) return false;

            if (!entry.ForgetDay.HasValue) return false;

            return day >= entry.ForgetDay.Value;
        }

        public static bool IsForgotten(KnownByEntry entry, double day, MemoryConfig cfg, bool isSecretUnleaked = false, string playerHeroId = "")
        {
            if (cfg == null || !cfg.Enabled) return false;
            if (entry == null) return false;
            if (!string.IsNullOrEmpty(playerHeroId) && string.Equals(entry.HeroId, playerHeroId, StringComparison.Ordinal)) return false;

            if (isSecretUnleaked) return false;
            if (!entry.ForgetDay.HasValue) return false;

            return day >= entry.ForgetDay.Value;
        }
    }
}
