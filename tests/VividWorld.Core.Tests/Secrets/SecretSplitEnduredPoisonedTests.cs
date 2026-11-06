using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json;
using VividWorld.Core.Catalog;
using VividWorld.Core.Config;
using VividWorld.Core.Events;
using VividWorld.Core.Ingest;
using VividWorld.Core.Secrets;
using VividWorld.Core.Situations;
using Xunit;

namespace VividWorld.Core.Tests.Secrets
{
    public class SecretSplitEnduredPoisonedTests
    {
        private static string FindRepoFile(params string[] parts)
        {
            string? current = AppContext.BaseDirectory;
            while (!string.IsNullOrEmpty(current))
            {
                if (File.Exists(Path.Combine(current, "VividWorld.sln")))
                {
                    string candidate = Path.Combine(new[] { current }.Concat(parts).ToArray());
                    if (File.Exists(candidate)) return candidate;
                }
                var parent = Directory.GetParent(current);
                if (parent == null) break;
                current = parent.FullName;
            }
            throw new FileNotFoundException($"Could not find '{string.Join("/", parts)}'");
        }

        private sealed class FakeSecretRelationshipLookup : ISecretRelationshipLookup
        {
            public Dictionary<string, List<string>> Kin = new(StringComparer.OrdinalIgnoreCase);
            public Dictionary<string, List<string>> Friends = new(StringComparer.OrdinalIgnoreCase);
            public Dictionary<(string, string), int> Affection = new();

            public IReadOnlyCollection<string> GetKin(string heroId)
            {
                if (Kin.TryGetValue(heroId, out var list)) return list;
                return Array.Empty<string>();
            }

            public IReadOnlyCollection<string> GetFriends(string heroId)
            {
                if (Friends.TryGetValue(heroId, out var list)) return list;
                return Array.Empty<string>();
            }

            public int? GetAffection(string heroA, string heroB)
            {
                if (Affection.TryGetValue((heroA, heroB), out var val)) return val;
                if (Affection.TryGetValue((heroB, heroA), out var rev)) return rev;
                return null;
            }

            public bool IsKin(string heroA, string heroB)
            {
                if (Kin.TryGetValue(heroA, out var list) && list.Contains(heroB)) return true;
                if (Kin.TryGetValue(heroB, out var listB) && listB.Contains(heroA)) return true;
                return false;
            }
        }

        [Fact]
        public void ShippedJson_And_Templates_AreConsistent()
        {
            string sitPath = FindRepoFile("module", "ModuleData", "vividworld_situations.json");
            string eventsPath = FindRepoFile("module", "ModuleData", "vividworld_situation_events.json");
            var cat = EventCatalogLoader.Load(File.ReadAllText(eventsPath), new PersistenceConfig());
            var endure = cat.ByType("seat_dispute_endured")!;
            Assert.Equal(EventOrigin.Public, endure.Origin);
            Assert.Empty(endure.KnowingRoles);

            var grudge = cat.ByType("seat_dispute_grudge_held")!;
            Assert.Equal(EventOrigin.Secret, grudge.Origin);
            Assert.Equal("seat_dispute_endured", grudge.LinkedTemplateType);
            Assert.NotNull(grudge.OwnerRoles);
            Assert.Contains("slighted", grudge.OwnerRoles!);
            Assert.NotNull(grudge.Harmed?.Roles);
            Assert.Contains("favored", grudge.Harmed!.Roles!);

            var poisoned = cat.ByType("conduct_poisoned")!;
            Assert.Equal(EventOrigin.Secret, poisoned.Origin);
            Assert.NotNull(poisoned.OwnerRoles);
            Assert.Contains("poisoner", poisoned.OwnerRoles!);
            Assert.NotNull(poisoned.Harmed?.KinOf);
            Assert.Contains("victim", poisoned.Harmed!.KinOf!);
            Assert.NotNull(poisoned.Harmed?.FriendsOf);
            Assert.Contains("victim", poisoned.Harmed!.FriendsOf!);

            Assert.Equal(EventOrigin.Secret, cat.ByType("brawl_hushed_up")!.Origin);
            Assert.Equal(EventOrigin.Secret, cat.ByType("wager_secret_stake")!.Origin);
            Assert.Equal(EventOrigin.Secret, cat.ByType("tavern_confidence")!.Origin);
            Assert.Equal(EventOrigin.Secret, cat.ByType("victory_credit_belittled")!.Origin);

            string mainEventsPath = FindRepoFile("module", "ModuleData", "vividworld_events.json");
            var mainCat = EventCatalogLoader.Load(File.ReadAllText(mainEventsPath), new PersistenceConfig());
            Assert.Equal(EventOrigin.Secret, mainCat.ByType("hero_murdered")!.Origin);

            // Shipped situations check
            var sitCat = SituationCatalogLoader.Load(File.ReadAllText(sitPath));
            Assert.NotNull(sitCat);
            var brawl = sitCat.ById("market_brawl")!;
            Assert.DoesNotContain(brawl.Branches, b => b.Id == "hush_it_up");

            var seat = sitCat.ById("seat_dispute")!;
            var endureBranch = seat.Branches.Single(b => b.Id == "endure");
            Assert.Equal(2, endureBranch.Events.Count);
            Assert.Equal("seat_dispute_endured", endureBranch.Events[0].Type);
            Assert.Equal("seat_dispute_grudge_held", endureBranch.Events[1].Type);
            Assert.Equal("seat_dispute_endured", endureBranch.Events[1].LinkTo);
        }

        [Fact]
        public void OldJson_WithoutNewSecretFields_DeserializesAndSerializesIdentically()
        {
            string oldJson = @"{
  ""eventId"": ""evt_test_001"",
  ""type"": ""some_event"",
  ""origin"": ""secret"",
  ""day"": 10.5,
  ""dramaWeight"": 3,
  ""dramaScale"": 10,
  ""participants"": { ""slighted"": ""hero_1"" },
  ""facts"": [],
  ""knownBy"": [
    {
      ""heroId"": ""hero_1"",
      ""hop"": 0,
      ""learnedDay"": 10.5,
      ""sourceHeroId"": null
    }
  ]
}";
            var evt = JsonConvert.DeserializeObject<WorldEvent>(oldJson)!;
            Assert.NotNull(evt);
            Assert.Null(evt.SecretOwnerHeroIds);
            Assert.False(evt.SecretRetired);
            Assert.Null(evt.SecretHarmedHeroIds);
            Assert.Null(evt.SecretPath);
            Assert.Null(evt.StakedSecretEventId);

            var kb = evt.KnownBy.Single();
            Assert.Null(kb.OwnerReaction);
            Assert.Null(kb.SuspectedHeroIds);
            Assert.Null(kb.GrudgeAgainstHeroId);

            string roundTripJson = JsonConvert.SerializeObject(evt, Formatting.Indented);
            Assert.DoesNotContain("\"secretRetired\":", roundTripJson);

            var evt2 = JsonConvert.DeserializeObject<WorldEvent>(roundTripJson)!;
            Assert.Equal(evt.EventId, evt2.EventId);
            Assert.Equal(evt.Origin, evt2.Origin);
            Assert.Null(evt2.SecretOwnerHeroIds);
            Assert.False(evt2.SecretRetired);
        }

        [Fact]
        public void OldSeatDisputeEndured_KeepsSecretOrigin_EvenIfTemplateChanged()
        {
            string oldEventJson = @"{
  ""eventId"": ""evt_old_endured_001"",
  ""type"": ""seat_dispute_endured"",
  ""origin"": ""secret"",
  ""day"": 5.0,
  ""knownBy"": [ { ""heroId"": ""hero_1"", ""hop"": 0, ""learnedDay"": 5.0 } ]
}";
            var evt = JsonConvert.DeserializeObject<WorldEvent>(oldEventJson)!;
            Assert.Equal(EventOrigin.Secret, evt.Origin);
            Assert.Equal("seat_dispute_endured", evt.Type);
        }

        [Fact]
        public void SecretHarmEvaluation_Evaluates_UnionOfAllHarmedSources()
        {
            var evt = new WorldEvent
            {
                EventId = "evt_secret_harm",
                Type = "test_harm",
                Origin = EventOrigin.Secret,
                SecretHarmedHeroIds = new List<string> { "hero_manual_1" },
                Participants = new Dictionary<string, string>
                {
                    ["favored"] = "hero_favored",
                    ["rival"] = "hero_manual_1",
                    ["victim"] = "hero_victim"
                }
            };

            var template = new EventTemplate
            {
                Type = "test_harm",
                Origin = EventOrigin.Secret,
                Harmed = new TemplateHarmedDef
                {
                    Roles = new List<string> { "favored", "rival" },
                    KinOf = new List<string> { "victim" },
                    FriendsOf = new List<string> { "victim" }
                }
            };

            var lookup = new FakeSecretRelationshipLookup();
            lookup.Kin["hero_victim"] = new List<string> { "hero_kin_1", "hero_kin_2" };
            lookup.Friends["hero_victim"] = new List<string> { "hero_friend_1" };

            var harmed = SecretHarmEvaluation.EvaluateHarmedHeroIds(evt, template, lookup);

            Assert.Equal(5, harmed.Count);
            Assert.Contains("hero_manual_1", harmed);
            Assert.Contains("hero_favored", harmed);
            Assert.Contains("hero_kin_1", harmed);
            Assert.Contains("hero_kin_2", harmed);
            Assert.Contains("hero_friend_1", harmed);
        }

        [Fact]
        public void SecretHarmEvaluation_NullLookup_FallsBackGracefully()
        {
            var evt = new WorldEvent
            {
                Type = "test",
                Participants = new Dictionary<string, string> { ["target"] = "h1" }
            };
            var template = new EventTemplate
            {
                Type = "test",
                Harmed = new TemplateHarmedDef
                {
                    Roles = new List<string> { "target" },
                    KinOf = new List<string> { "target" }
                }
            };

            var harmed = SecretHarmEvaluation.EvaluateHarmedHeroIds(evt, template, null);
            Assert.Single(harmed);
            Assert.Contains("h1", harmed);
        }

        [Fact]
        public void EventCatalogLoader_Parses_OwnerRoles_Harmed_OnHeard()
        {
            string json = @"[
  {
    ""type"": ""test_secret"",
    ""origin"": ""secret"",
    ""dramaScale"": 10,
    ""dramaWeight"": 5,
    ""roles"": {
      ""poisoner"": ""hero:poisoner"",
      ""accomplice"": ""hero:accomplice"",
      ""victim_role"": ""hero:victim""
    },
    ""ownerRoles"": [""poisoner"", ""accomplice""],
    ""harmed"": {
      ""roles"": [""victim_role""],
      ""kinOf"": [""victim_role""],
      ""friendsOf"": [""victim_role""]
    },
    ""onHeard"": [
      {
        ""listenerKinOf"": ""victim_role"",
        ""targetRole"": ""poisoner"",
        ""amount"": -15.0
      }
    ],
    ""facts"": [
      { ""id"": ""who"", ""category"": ""WHO"", ""text"": ""secret"", ""fragility"": 1 }
    ]
  }
]";
            var catalog = EventCatalogLoader.Load(json, new PersistenceConfig());
            var tmpl = catalog.ByType("test_secret");
            Assert.NotNull(tmpl);
            Assert.NotNull(tmpl!.OwnerRoles);
            Assert.Equal(new[] { "poisoner", "accomplice" }, tmpl.OwnerRoles);

            Assert.NotNull(tmpl.Harmed);
            Assert.Equal(new[] { "victim_role" }, tmpl.Harmed!.Roles);
            Assert.Equal(new[] { "victim_role" }, tmpl.Harmed.KinOf);
            Assert.Equal(new[] { "victim_role" }, tmpl.Harmed.FriendsOf);

            Assert.NotNull(tmpl.OnHeard);
            Assert.Single(tmpl.OnHeard!);
            Assert.Equal("victim_role", tmpl.OnHeard![0].ListenerKinOf);
            Assert.Equal("poisoner", tmpl.OnHeard[0].TargetRole);
            Assert.Equal(-15.0, tmpl.OnHeard[0].Amount);
        }

        [Fact]
        public void TemplateBinder_BindsSecretOwnerHeroIds_WhenOriginIsSecret()
        {
            var tmpl = new EventTemplate
            {
                Type = "secret_tmpl",
                Origin = EventOrigin.Secret,
                OwnerRoles = new List<string> { "poisoner" },
                Roles = new Dictionary<string, string> { ["poisoner"] = "{POISONER}" },
                Facts = new List<TemplateFact>
                {
                    new TemplateFact { Id = "who", Category = FactCategory.Who, Text = "test", Fragility = 1 }
                }
            };

            var bindings = new Dictionary<string, string>
            {
                ["POISONER"] = "lord_evil"
            };

            var sub = TemplateBinder.Bind(tmpl, bindings, 10.0, null, out var issues);
            Assert.NotNull(sub);
            Assert.NotNull(sub!.SecretOwnerHeroIds);
            Assert.Contains("lord_evil", sub.SecretOwnerHeroIds!);

            // Public template does not populate SecretOwnerHeroIds
            tmpl.Origin = EventOrigin.Public;
            var subPub = TemplateBinder.Bind(tmpl, bindings, 10.0, null, out _);
            Assert.Null(subPub!.SecretOwnerHeroIds);
        }
    }
}
