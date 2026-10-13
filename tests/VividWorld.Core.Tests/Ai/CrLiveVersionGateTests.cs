using VividWorld.Core.Ai;
using Xunit;

namespace VividWorld.Core.Tests.Ai
{
    public class CrLiveVersionGateTests
    {
        [Theory]
        [InlineData(2, 7, 7, true)]
        [InlineData(2, 7, 6, false)]
        [InlineData(2, 6, 99, false)]
        [InlineData(2, 8, 0, true)]
        [InlineData(3, 0, 0, true)]
        [InlineData(1, 9, 9, false)]
        [InlineData(2, 7, 10, true)]
        public void IsLiveVersion(int major, int minor, int revision, bool expected)
        {
            Assert.Equal(expected, CrLiveVersionGate.IsLiveVersion(major, minor, revision));
        }
    }
}
