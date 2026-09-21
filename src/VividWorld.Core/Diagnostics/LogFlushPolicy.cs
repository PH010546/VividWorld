namespace VividWorld.Core.Diagnostics
{
    /// <summary>
    /// 純函數：依緩衝行數、距上次落盤時間、訊息嚴重度與設定門檻，
    /// 決定現在是否應該落盤。
    /// </summary>
    public static class LogFlushPolicy
    {
        public static bool ShouldFlush(int bufferedLines, double secondsSinceLastFlush,
                                       bool isError, int everyLines, double everySeconds)
        {
            if (isError) return true;
            if (bufferedLines <= 0) return false;
            if (bufferedLines >= everyLines) return true;
            if (secondsSinceLastFlush >= everySeconds) return true;
            return false;
        }
    }
}
