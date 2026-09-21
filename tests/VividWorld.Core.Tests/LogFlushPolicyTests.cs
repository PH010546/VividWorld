using VividWorld.Core.Diagnostics;
using Xunit;

namespace VividWorld.Core.Tests
{
    public class LogFlushPolicyTests
    {
        [Fact]
        public void Flush_OnError_IsAlwaysImmediate()
        {
            // isError is true: unconditional flush, regardless of lines or elapsed time
            Assert.True(LogFlushPolicy.ShouldFlush(bufferedLines: 0, secondsSinceLastFlush: 0.0, isError: true, everyLines: 64, everySeconds: 5.0));
            Assert.True(LogFlushPolicy.ShouldFlush(bufferedLines: 1, secondsSinceLastFlush: 0.1, isError: true, everyLines: 64, everySeconds: 5.0));
            Assert.True(LogFlushPolicy.ShouldFlush(bufferedLines: 63, secondsSinceLastFlush: 0.0, isError: true, everyLines: 64, everySeconds: 5.0));

            // When isError is false with the same small buffers, it should not flush
            Assert.False(LogFlushPolicy.ShouldFlush(bufferedLines: 1, secondsSinceLastFlush: 0.1, isError: false, everyLines: 64, everySeconds: 5.0));
        }

        [Fact]
        public void Flush_WhenLineThresholdReached()
        {
            // Below threshold -> do not flush
            Assert.False(LogFlushPolicy.ShouldFlush(bufferedLines: 63, secondsSinceLastFlush: 1.0, isError: false, everyLines: 64, everySeconds: 5.0));

            // At or above threshold -> flush
            Assert.True(LogFlushPolicy.ShouldFlush(bufferedLines: 64, secondsSinceLastFlush: 1.0, isError: false, everyLines: 64, everySeconds: 5.0));
            Assert.True(LogFlushPolicy.ShouldFlush(bufferedLines: 100, secondsSinceLastFlush: 1.0, isError: false, everyLines: 64, everySeconds: 5.0));
        }

        [Fact]
        public void Flush_WhenTimeThresholdReached_EvenWithFewLines()
        {
            // Only 1 line, below time threshold -> do not flush
            Assert.False(LogFlushPolicy.ShouldFlush(bufferedLines: 1, secondsSinceLastFlush: 4.9, isError: false, everyLines: 64, everySeconds: 5.0));

            // Only 1 line, at or above time threshold -> flush
            Assert.True(LogFlushPolicy.ShouldFlush(bufferedLines: 1, secondsSinceLastFlush: 5.0, isError: false, everyLines: 64, everySeconds: 5.0));
            Assert.True(LogFlushPolicy.ShouldFlush(bufferedLines: 1, secondsSinceLastFlush: 10.0, isError: false, everyLines: 64, everySeconds: 5.0));

            // 0 lines -> even if time exceeded, nothing to flush
            Assert.False(LogFlushPolicy.ShouldFlush(bufferedLines: 0, secondsSinceLastFlush: 10.0, isError: false, everyLines: 64, everySeconds: 5.0));
        }
    }
}
