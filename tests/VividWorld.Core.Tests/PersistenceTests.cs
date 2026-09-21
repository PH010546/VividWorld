#nullable enable
using System.Collections.Generic;
using VividWorld.Core.Persistence;
using Xunit;

namespace VividWorld.Core.Tests
{
    public class PersistenceTests
    {
        [Fact]
        public void StoreTimeline_DetectsEventsAfterCurrentDay()
        {
            var index = new RumorIndex();
            index.Upsert(new RumorIndexEntry { EventId = "e1", Day = 100.0 });
            index.Upsert(new RumorIndexEntry { EventId = "e2", Day = 120.0 });
            index.Upsert(new RumorIndexEntry { EventId = "e3", Day = 130.0 });

            var (count, maxDay) = StoreTimeline.EventsAfter(index, 110.0);

            Assert.Equal(2, count);
            Assert.Equal(130.0, maxDay);
        }

        [Fact]
        public void StoreTimeline_WithinTolerance_ReportsNothing()
        {
            var index = new RumorIndex();
            index.Upsert(new RumorIndexEntry { EventId = "e1", Day = 100.0 });
            index.Upsert(new RumorIndexEntry { EventId = "e2", Day = 110.4 });

            var (count, maxDay) = StoreTimeline.EventsAfter(index, 110.0);

            Assert.Equal(0, count);
            Assert.Equal(0.0, maxDay);
        }
    }
}
