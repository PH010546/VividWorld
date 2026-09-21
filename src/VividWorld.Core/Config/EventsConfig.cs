using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace VividWorld.Core.Config
{
    public sealed class EventSourcesConfig
    {
        public bool HeroKilled { get; set; } = true;
        public bool HeroPrisonerTaken { get; set; } = true;
        public bool HeroesMarried { get; set; } = true;
        public bool ChildBorn { get; set; } = true;
        public bool HeroPrisonerReleased { get; set; } = true;

        [JsonExtensionData]
        public IDictionary<string, JToken> Extra { get; set; } = new Dictionary<string, JToken>();
    }

    public sealed class ProminenceDramaConfig
    {
        public int Ruler { get; set; } = 5;
        public int ClanLeader { get; set; } = 4;
        public int NobleMember { get; set; } = 2;
        public int Minor { get; set; } = 1;

        [JsonExtensionData]
        public IDictionary<string, JToken> Extra { get; set; } = new Dictionary<string, JToken>();
    }

    public sealed class EventsConfig
    {
        public string CatalogFile { get; set; } = "vividworld_events.json";
        public bool StrictCatalog { get; set; } = false;
        public EventSourcesConfig Sources { get; set; } = new();
        public ProminenceDramaConfig PrisonerDramaByProminence { get; set; } = new();
        public ProminenceDramaConfig ReleaseDramaByProminence { get; set; } = new()
        {
            Ruler = 4,
            ClanLeader = 3,
            NobleMember = 1,
            Minor = 1
        };

        [JsonExtensionData]
        public IDictionary<string, JToken> Extra { get; set; } = new Dictionary<string, JToken>();
    }
}
