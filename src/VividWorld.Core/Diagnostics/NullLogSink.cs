namespace VividWorld.Core.Diagnostics
{
    /// <summary>什麼都不做的預設實作。Core 的任何建構參數都不得接受 null sink。</summary>
    public sealed class NullLogSink : ILogSink
    {
        public static readonly NullLogSink Instance = new NullLogSink();
        private NullLogSink() { }
        public void Info(string message) { }
        public void Warn(string message) { }
        public void Error(string message, System.Exception? ex = null) { }
    }
}
