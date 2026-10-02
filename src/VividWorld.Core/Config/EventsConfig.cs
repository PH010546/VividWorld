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

    /// <summary>個人的事（死亡、被抓、被放、逃脫）：當事人本人的身分加在基礎分上的加成。份量是 1..10。</summary>
    public sealed class ProminenceWeightBonusConfig
    {
        public int Ruler { get; set; } = 4;
        public int ClanLeader { get; set; } = 2;
        public int NobleMember { get; set; } = 0;
        public int Minor { get; set; } = -2;

        [JsonExtensionData]
        public IDictionary<string, JToken> Extra { get; set; } = new Dictionary<string, JToken>();
    }

    /// <summary>家族的事（成親、生子）：門第加在基礎分上的加成。份量是 1..10。</summary>
    public sealed class ClanStandingWeightBonusConfig
    {
        public int Royal { get; set; } = 4;
        public int High { get; set; } = 2;
        public int Ordinary { get; set; } = 0;
        public int Minor { get; set; } = -2;

        [JsonExtensionData]
        public IDictionary<string, JToken> Extra { get; set; } = new Dictionary<string, JToken>();
    }

    public sealed class EventsConfig
    {
        public string CatalogFile { get; set; } = "vividworld_events.json";
        public bool StrictCatalog { get; set; } = false;
        public EventSourcesConfig Sources { get; set; } = new();

        /// <summary>個人的事看本人的身分，加成加在模板寫的基礎分上。取代舊的四組 <c>*DramaByProminence</c>（1..5 的段，現在不再讀）。</summary>
        public ProminenceWeightBonusConfig WeightBonusByProminence { get; set; } = new();

        /// <summary>家族的事看門第，加成加在模板寫的基礎分上。</summary>
        public ClanStandingWeightBonusConfig WeightBonusByClanStanding { get; set; } = new();

        /// <summary>家族等級（<c>Clan.Tier</c>）達到這個數字以上算高等家族。</summary>
        public int HighClanMinTier { get; set; } = 5;

        [JsonExtensionData]
        public IDictionary<string, JToken> Extra { get; set; } = new Dictionary<string, JToken>();
    }
}
