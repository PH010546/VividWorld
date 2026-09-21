using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using VividWorld.Core.Diagnostics;

namespace VividWorld
{
    internal enum LogLevel { Error = 0, Warn = 1, Info = 2 }

    /// <summary>
    /// 最後手段的除錯出口。鐵律：任何情況都不得拋例外——包括磁碟滿、路徑無權限、
    /// 檔案被其他程序鎖住。日誌自己壞掉的話就什麼都查不到了。
    /// </summary>
    internal static class ModLog
    {
        private static readonly object Gate = new object();
        private const long MaxBytes = 4L * 1024 * 1024;   // 超過就換檔，避免長戰役把日誌養到幾百 MB

        private static readonly List<string> Buffer = new List<string>();
        private static DateTime _lastFlushTime = DateTime.UtcNow;

        internal static LogLevel Level { get; set; } = LogLevel.Info;   // M5 由 ConfigStore 覆寫
        internal static int FlushEveryLines { get; set; } = 64;         // M5.8 由 ConfigStore 覆寫
        internal static double FlushEverySeconds { get; set; } = 5.0;   // M5.8 由 ConfigStore 覆寫

        internal static void Info(string message) => Write(LogLevel.Info, "INFO", message, null);
        internal static void Warn(string message) => Write(LogLevel.Warn, "WARN", message, null);
        internal static void Error(string message, Exception? ex = null) => Write(LogLevel.Error, "ERROR", message, ex);

        internal static void Flush()
        {
            try
            {
                lock (Gate)
                {
                    FlushLocked();
                }
            }
            catch
            {
                // 刻意吞掉。日誌失敗絕不能影響遊戲。
            }
        }

        private static void Write(LogLevel level, string tag, string message, Exception? ex)
        {
            if (level > Level) return;
            try
            {
                var sb = new StringBuilder();
                // InvariantCulture 是必要的：ToString(format) 預設走 CurrentCulture，
                // 在使用非西曆的地區（如 th-TH 的佛曆）年份會變成 2569。
                sb.Append('[')
                  .Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture))
                  .Append("] ");
                sb.Append(tag).Append(": ").Append(message);
                if (ex != null) sb.AppendLine().Append(ex);

                string line = sb.ToString();

                lock (Gate)
                {
                    Buffer.Add(line);
                    double secondsSince = (DateTime.UtcNow - _lastFlushTime).TotalSeconds;
                    bool isError = (level == LogLevel.Error);

                    if (LogFlushPolicy.ShouldFlush(Buffer.Count, secondsSince, isError, FlushEveryLines, FlushEverySeconds))
                    {
                        FlushLocked();
                    }
                }
            }
            catch
            {
                // 刻意吞掉。日誌失敗絕不能影響遊戲。
            }
        }

        private static void FlushLocked()
        {
            if (Buffer.Count == 0) return;
            try
            {
                var dir = VividWorldPaths.ConfigDirectory;
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

                var path = VividWorldPaths.LogFile;
                RollIfTooLarge(path);

                var sb = new StringBuilder();
                for (int i = 0; i < Buffer.Count; i++)
                {
                    sb.AppendLine(Buffer[i]);
                }
                Buffer.Clear();
                _lastFlushTime = DateTime.UtcNow;

                File.AppendAllText(path, sb.ToString(), Encoding.UTF8);
            }
            catch
            {
                // 刻意吞掉。日誌失敗絕不能影響遊戲。
            }
        }

        private static void RollIfTooLarge(string path)
        {
            try
            {
                var fi = new FileInfo(path);
                if (!fi.Exists || fi.Length < MaxBytes) return;
                var old = path + ".1";
                if (File.Exists(old)) File.Delete(old);
                File.Move(path, old);
            }
            catch { }
        }
    }
}
