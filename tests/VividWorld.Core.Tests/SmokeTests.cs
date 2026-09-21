using VividWorld.Core.Diagnostics;
using Xunit;

namespace VividWorld.Core.Tests
{
    public class SmokeTests
    {
        [Fact]
        public void NullLogSink_SwallowsEverything_AndNeverThrows()
        {
            ILogSink sink = NullLogSink.Instance;
            sink.Info("info");
            sink.Warn("warn");
            sink.Error("error", new System.InvalidOperationException("boom"));
        }
    }
}
