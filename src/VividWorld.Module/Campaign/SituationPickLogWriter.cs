using VividWorld.Core.Config;
using VividWorld.Core.Situations;

namespace VividWorld.Campaign
{
    /// <summary>
    /// 把共用挑法的紀錄寫進日誌：城裡每天的掃描、玩家在場、流浪者事件三處共用同一套段落。
    /// 英雄名字從 <see cref="HeroLookup"/> 查；查不到就只印代號。
    /// </summary>
    internal static class SituationPickLogWriter
    {
        /// <param name="allCandidates">true＝挑人那段把全部候選逐行印出（乾跑一律 true）。</param>
        public static SituationPickLogOptions Options(HeroLookup? heroes, bool allCandidates)
        {
            return new SituationPickLogOptions
            {
                AllCandidates = allCandidates,
                NameOf = id => heroes?.Get(id)?.Name?.ToString()
            };
        }

        /// <summary>設定裡的 debug.logSituationCandidates。</summary>
        public static bool LogAllCandidates(VividWorldConfig? config)
        {
            return config?.Debug != null && config.Debug.LogSituationCandidates;
        }

        public static void Write(SituationPickRecord? record, SituationPickLogOptions options, string prefix = "")
        {
            if (record == null) return;
            foreach (var line in SituationPickLogFormatter.Format(record, options))
            {
                ModLog.Info(prefix + line);
            }
        }
    }
}
