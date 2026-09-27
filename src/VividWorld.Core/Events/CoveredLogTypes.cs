#nullable enable
using System.Collections.Generic;
using VividWorld.Core.Config;

namespace VividWorld.Core.Events
{
    /// <summary>
    /// Vivid World 與原生事件紀錄種類的對照。
    /// 依據事件來源設定動態回傳啟用的原生紀錄種類名稱。
    /// </summary>
    public static class CoveredLogTypes
    {
        public const string CharacterKilled = "CharacterKilledLogEntry";
        public const string TakePrisoner = "TakePrisonerLogEntry";
        public const string EndCaptivity = "EndCaptivityLogEntry";
        public const string CharacterMarried = "CharacterMarriedLogEntry";
        public const string Childbirth = "ChildbirthLogEntry";

        public static IReadOnlyList<string> GetCoveredLogTypes(EventSourcesConfig? sources)
        {
            sources ??= new EventSourcesConfig();

            var list = new List<string>(5);
            if (sources.HeroKilled) list.Add(CharacterKilled);
            if (sources.HeroPrisonerTaken) list.Add(TakePrisoner);
            if (sources.HeroPrisonerReleased) list.Add(EndCaptivity);
            if (sources.HeroesMarried) list.Add(CharacterMarried);
            if (sources.ChildBorn) list.Add(Childbirth);

            return list;
        }

        /// <summary>
        /// 依據戰役狀態與設定動態回傳啟用的原生紀錄種類名稱。
        /// 當戰役未載入或模組總開關關閉時，回傳空清單，避免外部模組誤讓世界消息。
        /// </summary>
        public static IReadOnlyList<string> GetCoveredLogTypes(VividWorldConfig? config, bool isSessionActive)
        {
            if (!isSessionActive || config == null || !config.Enabled)
            {
                return System.Array.Empty<string>();
            }

            return GetCoveredLogTypes(config.Events?.Sources);
        }
    }
}
