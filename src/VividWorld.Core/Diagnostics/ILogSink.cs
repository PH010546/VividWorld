namespace VividWorld.Core.Diagnostics
{
    /// <summary>Core 對外的唯一日誌出口。Module 注入實作，測試注入假物件。</summary>
    public interface ILogSink
    {
        void Info(string message);
        void Warn(string message);
        void Error(string message, System.Exception? ex = null);
    }
}
