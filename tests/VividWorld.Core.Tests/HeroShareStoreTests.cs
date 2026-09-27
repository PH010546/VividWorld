#nullable enable
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using VividWorld.Core.Dialogue;
using VividWorld.Core.Persistence;
using VividWorld.Core.Tests.Fakes;
using Xunit;

namespace VividWorld.Core.Tests
{
    public sealed class HeroShareStoreTests
    {
        [Fact]
        public void HeroShareStore_SaveAndLoad_RoundTrips()
        {
            var writer = new FailingFileWriter();
            string path = "campaign_1/shares.json";
            var store = new HeroShareStore(path, writer);

            var ledger = new HeroShareLedger();
            ledger.Record("hero_alice", 10.5);
            ledger.Record("hero_alice", 10.5);
            ledger.Record("hero_bob", 10.2);

            bool saved = store.Save(ledger);
            Assert.True(saved);
            Assert.True(writer.Files.ContainsKey(path));

            var loadedStore = new HeroShareStore(path, writer);
            var loadedLedger = loadedStore.Load();

            Assert.True(loadedStore.FileExisted);
            Assert.Null(loadedStore.LastLoadError);
            Assert.Equal(2, loadedLedger.SharedOn("hero_alice", 10.5));
            Assert.Equal(1, loadedLedger.SharedOn("hero_bob", 10.2));
            Assert.Equal(0, loadedLedger.SharedOn("hero_alice", 11.0));
        }

        [Fact]
        public void HeroShareStore_LoadNonExistent_ReturnsEmptyLedger()
        {
            var writer = new FailingFileWriter();
            string path = "missing/shares.json";
            var store = new HeroShareStore(path, writer);

            var ledger = store.Load();

            Assert.NotNull(ledger);
            Assert.False(store.FileExisted);
            Assert.Null(store.LastLoadError);
            Assert.Empty(ledger.ToDictionary());
        }

        [Fact]
        public void HeroShareStore_CorruptedJson_ReportsErrorAndReturnsEmptyLedger()
        {
            var writer = new FailingFileWriter();
            string path = "campaign_1/shares.json";
            writer.Files[path] = "{ invalid json content }";
            var store = new HeroShareStore(path, writer);

            var ledger = store.Load();

            Assert.NotNull(ledger);
            Assert.True(store.FileExisted);
            Assert.NotNull(store.LastLoadError);
            Assert.Empty(ledger.ToDictionary());
        }

        [Fact]
        public void HeroShareStore_SaveFailure_ReturnsFalse()
        {
            var writer = new FailingFileWriter();
            string path = "campaign_1/shares.json";
            writer.WriteAllText(path, "{}");
            var store = new HeroShareStore(path, writer);

            writer.FailOn("shares.json", FileOp.Replace);

            var ledger = new HeroShareLedger();
            ledger.Record("hero_1", 1.0);

            bool saved = store.Save(ledger);
            Assert.False(saved);
        }

        [Fact]
        public void HeroShareStore_PreservesExtraFields()
        {
            var writer = new FailingFileWriter();
            string path = "campaign_1/shares.json";
            string jsonWithFutureField = @"{
  ""hero_future"": {
    ""day"": 15,
    ""count"": 2,
    ""futureProperty"": ""preserved_value""
  }
}";
            writer.Files[path] = jsonWithFutureField;

            var store = new HeroShareStore(path, writer);
            var ledger = store.Load();

            Assert.Equal(2, ledger.SharedOn("hero_future", 15.0));
            var dict = ledger.ToDictionary();
            Assert.True(dict.ContainsKey("hero_future"));
            Assert.True(dict["hero_future"].Extra.ContainsKey("futureProperty"));
            Assert.Equal("preserved_value", dict["hero_future"].Extra["futureProperty"].ToString());

            // Save back and verify the futureProperty is preserved in the json
            bool saved = store.Save(ledger);
            Assert.True(saved);
            string savedJson = writer.Files[path];
            Assert.Contains("futureProperty", savedJson);
            Assert.Contains("preserved_value", savedJson);
        }
    }
}
