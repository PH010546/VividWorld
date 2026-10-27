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

        [Theory]
        [InlineData(2, 7, 9, true)]
        [InlineData(2, 7, 8, false)]
        [InlineData(2, 7, 18, true)]
        [InlineData(2, 8, 0, true)]
        [InlineData(3, 0, 0, true)]
        [InlineData(1, 9, 9, false)]
        [InlineData(2, 6, 99, false)]
        public void IsHeardNowVersion(int major, int minor, int revision, bool expected)
        {
            Assert.Equal(expected, CrLiveVersionGate.IsHeardNowVersion(major, minor, revision));
        }

        [Theory]
        [InlineData(2, 7, 6, false, false)]
        [InlineData(2, 7, 7, true, false)]
        [InlineData(2, 7, 8, true, false)]
        [InlineData(2, 7, 9, true, true)]
        [InlineData(2, 7, 18, true, true)]
        public void VersionRouting_GatesCheck(int major, int minor, int revision, bool expectedLive, bool expectedHeardNow)
        {
            Assert.Equal(expectedLive, CrLiveVersionGate.IsLiveVersion(major, minor, revision));
            Assert.Equal(expectedHeardNow, CrLiveVersionGate.IsHeardNowVersion(major, minor, revision));
        }

        [Theory]
        [InlineData(2, 7, 6, null)]
        [InlineData(2, 7, 7, AiLiveNewsBlock.VariableName)]
        [InlineData(2, 7, 8, AiLiveNewsBlock.VariableName)]
        [InlineData(2, 7, 9, AiLiveNewsBlock.HeardNowVariableName)]
        [InlineData(2, 7, 18, AiLiveNewsBlock.HeardNowVariableName)]
        public void ResolveActiveVariable_RoutesVersionsCorrectly(int major, int minor, int revision, string? expectedVariable)
        {
            Assert.Equal(expectedVariable, CrLiveVersionGate.ResolveActiveVariable(major, minor, revision));
        }
    }
}
