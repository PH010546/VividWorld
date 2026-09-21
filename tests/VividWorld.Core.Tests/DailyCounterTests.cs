using Xunit;
using VividWorld.Core.Dialogue;

namespace VividWorld.Core.Tests
{
    public sealed class DailyCounterTests
    {
        [Fact]
        public void Advance_AcrossDayBoundary_ResetsCount()
        {
            var counter = new DailyCounter();

            // 同一日內：累加
            counter.Advance(10.2);
            counter.Increment();
            Assert.Equal(1, counter.Count);

            counter.Advance(10.8);
            counter.Increment();
            Assert.Equal(2, counter.Count);

            // 跨日：歸零
            counter.Advance(11.1);
            Assert.Equal(0, counter.Count);

            counter.Increment();
            Assert.Equal(1, counter.Count);

            // 日子倒退（讀舊檔）：也歸零
            counter.Advance(9.5);
            Assert.Equal(0, counter.Count);

            counter.Increment();
            Assert.Equal(1, counter.Count);
        }
    }
}
