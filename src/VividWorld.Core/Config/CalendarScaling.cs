namespace VividWorld.Core.Config
{
    public static class CalendarScaling
    {
        public const double NativeDaysInYear = 84.0;

        /// <summary>依實際曆法縮放敘事型時長（規格 §11.1）。就地修改並回傳同一個物件。
        /// Core 不認識 CampaignTime——呼叫端負責把 DaysInYear 讀出來傳進來。</summary>
        /// <summary>這些設定鍵在戰役載入時會被 <see cref="Apply"/> 乘上曆法比例，
        /// 記憶體裡的值因此**不等於** config.json 裡的值。
        /// 任何會把設定寫回檔案的路徑（例如 MCM 選單）都不得碰這些鍵，
        /// 否則縮放後的值會被寫進檔案，下次啟動再縮放一次，一輪一輪縮下去。
        /// `McmExposedKeys_DoesNotExposeAnyCalendarScaledKey` 釘住這條規則。</summary>
        public static readonly string[] ScaledPaths =
        {
            "leak.chanceDecayHalfLifeDays",
            "scheduling.rumorLifetimeDays",
            "scheduling.staleDays",
            "scheduling.secretWatchDays",
            "dialogue.volunteerCooldownDays",
            "memory.baseDays",
            "situations.grudgeDecay.personal.daysPerPoint",
            "situations.grudgeDecay.clan.daysPerPoint",
        };

        public static VividWorldConfig Apply(VividWorldConfig config, double daysInYear)
        {
            if (config == null) return config!;
            if (config.Scheduling == null || !config.Scheduling.ScaleDurationsToGameCalendar) return config;
            if (daysInYear <= 0) return config;

            double scale = daysInYear / NativeDaysInYear;
            if (scale == 1.0) return config;

            if (config.Leak != null)
            {
                config.Leak.ChanceDecayHalfLifeDays *= scale;
            }
            if (config.Scheduling != null)
            {
                config.Scheduling.RumorLifetimeDays *= scale;
                config.Scheduling.StaleDays *= scale;
                config.Scheduling.SecretWatchDays *= scale;
            }
            if (config.Dialogue != null)
            {
                config.Dialogue.VolunteerCooldownDays *= scale;
            }
            if (config.Memory != null)
            {
                config.Memory.BaseDays *= scale;
            }
            if (config.Situations?.GrudgeDecay != null)
            {
                if (config.Situations.GrudgeDecay.Personal != null)
                {
                    config.Situations.GrudgeDecay.Personal.DaysPerPoint *= scale;
                }
                if (config.Situations.GrudgeDecay.Clan != null)
                {
                    config.Situations.GrudgeDecay.Clan.DaysPerPoint *= scale;
                }
            }

            return config;
        }
    }
}
