using System;
using VividWorld.Core.Diagnostics;

namespace VividWorld
{
    /// <summary>把 Core 的 ILogSink 接到 Module 的 ModLog 上。</summary>
    internal sealed class ModLogSink : ILogSink
    {
        internal static readonly ModLogSink Instance = new ModLogSink();
        private ModLogSink() { }

        public void Info(string message) => ModLog.Info(message);
        public void Warn(string message) => ModLog.Warn(message);
        public void Error(string message, Exception? ex = null) => ModLog.Error(message, ex);
    }
}
