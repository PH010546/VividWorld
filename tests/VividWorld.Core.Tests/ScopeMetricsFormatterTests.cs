using VividWorld.Core.Diagnostics;
using Xunit;

namespace VividWorld.Core.Tests
{
    public class ScopeMetricsFormatterTests
    {
        [Fact]
        public void Detail_SubMillisecondAverage_KeepsTheDecimalPoint()
        {
            string result = ScopeMetricsFormatter.Detail("hourly", 100, 32.6, 1.1);
            Assert.Contains("avg 0.33ms", result);
            Assert.Contains("peak 1.10ms", result);
            Assert.DoesNotContain("avg 01ms", result);
        }

        [Fact]
        public void Detail_ZeroCalls_RendersZerosNotBlanks()
        {
            string result = ScopeMetricsFormatter.Detail("daily", 0, 0.0, 0.0);
            Assert.Contains("0 calls, total 0.00ms, avg 0.00ms, peak 0.00ms", result);
        }

        [Fact]
        public void Compact_OmitsPeakWhenItMatchesTheAverage()
        {
            string result = ScopeMetricsFormatter.Compact("contacts", 300, 15.0, 0.052);
            Assert.Equal("contacts 0.1ms x 300", result);
            Assert.DoesNotContain("peak", result);
        }
    }
}
