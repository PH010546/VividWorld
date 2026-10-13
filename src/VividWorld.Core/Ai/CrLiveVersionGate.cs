namespace VividWorld.Core.Ai
{
    /// <summary>
    /// Calradia Remembers 哪些版本走即時交消息。即時變數的接口更早就有，但讀不讀模組附的提示檔只在 2.7.7 驗過，
    /// 更舊的版本照舊加記憶，免得變數沒被引用、記憶也沒加，NPC 什麼消息都拿不到。版本取自 CR 的 SubModule.xml，
    /// 不是組件版本（帳本 D-121、X-48）。
    /// </summary>
    public static class CrLiveVersionGate
    {
        public const int MinimumMajor = 2;
        public const int MinimumMinor = 7;
        public const int MinimumRevision = 7;

        /// <summary>依序比主、次、修訂版號；2.7.7 以上回 true。</summary>
        public static bool IsLiveVersion(int major, int minor, int revision)
        {
            if (major != MinimumMajor)
            {
                return major > MinimumMajor;
            }

            if (minor != MinimumMinor)
            {
                return minor > MinimumMinor;
            }

            return revision >= MinimumRevision;
        }
    }
}
