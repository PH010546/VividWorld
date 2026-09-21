#nullable enable
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using VividWorld.Core.Persistence;
using Xunit;

namespace VividWorld.Core.Tests
{
    public class IndexTests
    {
        [Fact]
        public void KnownByIndex_EventsKnownBy_And_EventsAbout_AreCorrect()
        {
            var index = new RumorIndex();

            index.Upsert(new RumorIndexEntry
            {
                EventId = "e1",
                Type = "duel",
                Day = 10,
                KnownByHeroIds = new List<string> { "h1", "h2" },
                ParticipantHeroIds = new List<string> { "h3", "h4" }
            });

            index.Upsert(new RumorIndexEntry
            {
                EventId = "e2",
                Type = "murder",
                Day = 20,
                KnownByHeroIds = new List<string> { "h2" },
                ParticipantHeroIds = new List<string> { "h1" }
            });

            var knownByIndex = new KnownByIndex();
            knownByIndex.Rebuild(index);

            // EventsKnownBy
            var h1Known = knownByIndex.EventsKnownBy("h1", 100.0);
            Assert.Contains("e1", h1Known);
            Assert.DoesNotContain("e2", h1Known);

            var h2Known = knownByIndex.EventsKnownBy("h2", 100.0);
            Assert.Contains("e1", h2Known);
            Assert.Contains("e2", h2Known);

            var unknownKnown = knownByIndex.EventsKnownBy("unknown_hero", 100.0);
            Assert.Empty(unknownKnown);

            // EventsAbout
            var h3About = knownByIndex.EventsAbout("h3", 100.0);
            Assert.Contains("e1", h3About);

            var h1About = knownByIndex.EventsAbout("h1", 100.0);
            Assert.Contains("e2", h1About);

            var unknownAbout = knownByIndex.EventsAbout("unknown_hero", 100.0);
            Assert.Empty(unknownAbout);
        }

        [Fact]
        public void KnownByIndex_NoteKnower_UpdatesIncrementally()
        {
            var index = new RumorIndex();
            index.Upsert(new RumorIndexEntry
            {
                EventId = "e1",
                Type = "duel",
                Day = 10,
                KnownByHeroIds = new List<string> { "h1" },
                ParticipantHeroIds = new List<string> { "h2" }
            });

            var knownByIndex = new KnownByIndex();
            knownByIndex.Rebuild(index);

            Assert.Single(knownByIndex.EventsKnownBy("h1", 100.0));

            // Add new event to existing hero
            knownByIndex.NoteKnower("h1", "e2", 10.0);
            var h1Known = knownByIndex.EventsKnownBy("h1", 100.0);
            Assert.Contains("e1", h1Known);
            Assert.Contains("e2", h1Known);

            // Deduplication: adding same event again must not duplicate
            knownByIndex.NoteKnower("h1", "e2", 10.0);
            Assert.Equal(1, knownByIndex.EventsKnownBy("h1", 100.0).Count(x => x == "e2"));

            // Add new hero
            knownByIndex.NoteKnower("h_new", "e3", 10.0);
            Assert.Contains("e3", knownByIndex.EventsKnownBy("h_new", 100.0));

            // NoteKnower only touches EventsKnownBy, EventsAbout remains unaffected
            Assert.Empty(knownByIndex.EventsAbout("h_new", 100.0));
        }

        [Fact]
        public void RumorIndex_PreservesUnknownFutureFields()
        {
            var json = @"
{
  ""entries"": [
    {
      ""eventId"": ""e1"",
      ""type"": ""duel"",
      ""day"": 10.0,
      ""shardKey"": ""d0000-0099"",
      ""futureEntryField"": ""futureVal""
    }
  ],
  ""futureIndexMeta"": 12345
}";

            var index = VividJson.Read<RumorIndex>(json);
            Assert.NotNull(index);
            Assert.Single(index!.Entries);

            // Check extra fields are preserved in dictionary
            Assert.True(index.Extra.ContainsKey("futureIndexMeta"));
            Assert.Equal(12345, (int)(long)index.Extra["futureIndexMeta"]);

            Assert.True(index.Entries[0].Extra.ContainsKey("futureEntryField"));
            Assert.Equal("futureVal", (string?)index.Entries[0].Extra["futureEntryField"]);

            // Roundtrip serialization
            var reserialized = VividJson.Write(index);

            Assert.Contains("\"futureIndexMeta\": 12345", reserialized);
            Assert.Contains("\"futureEntryField\": \"futureVal\"", reserialized);

            // Must not serialize the dictionary property itself as "extra" or "Extra"
            Assert.DoesNotContain("\"extra\":", reserialized);
            Assert.DoesNotContain("\"Extra\":", reserialized);
        }
    }
}
