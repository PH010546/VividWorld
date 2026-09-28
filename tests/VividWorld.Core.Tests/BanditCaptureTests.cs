using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using VividWorld.Core.Catalog;
using VividWorld.Core.Channels;
using VividWorld.Core.Config;
using VividWorld.Core.Events;
using VividWorld.Core.Ingest;
using VividWorld.Core.Persistence;
using VividWorld.Core.Presentation;
using VividWorld.Core.Rumors;
using VividWorld.Core.Tests.Fakes;
using Xunit;

namespace VividWorld.Core.Tests
{
    public class BanditCaptureTests
    {
        private static string FindRepoRoot()
        {
            string current = AppContext.BaseDirectory;
            while (!string.IsNullOrEmpty(current))
            {
                if (Directory.Exists(Path.Combine(current, "module", "ModuleData")))
                {
                    return current;
                }
                var parent = Directory.GetParent(current);
                if (parent == null) break;
                current = parent.FullName;
            }
            throw new FileNotFoundException("Repository root could not be located from " + AppContext.BaseDirectory);
        }

        [Fact]
        public void BanditCaptureFamily_ExcludesSelfPlayerDeadPrisonerNotInNetworkAndOverCap()
        {
            const string prisonerId = "lord_prisoner";
            const string playerId = "player_hero";

            var candidates = new List<KeyValuePair<string, TraitProfile?>>
            {
                new(prisonerId, new TraitProfile { HeroId = prisonerId, IsAlive = true, IsPrisoner = true, IsLord = true }),
                new(playerId, new TraitProfile { HeroId = playerId, IsAlive = true, IsPrisoner = false, IsLord = true }),
                new("lord_dead", new TraitProfile { HeroId = "lord_dead", IsAlive = false, IsPrisoner = false, IsLord = true }),
                new("lord_captured", new TraitProfile { HeroId = "lord_captured", IsAlive = true, IsPrisoner = true, IsLord = true }),
                new("notable_merchant", new TraitProfile { HeroId = "notable_merchant", IsAlive = true, IsPrisoner = false, IsLord = false, IsWanderer = false }),
                new("unknown_hero", null),
                new("lord_c", new TraitProfile { HeroId = "lord_c", IsAlive = true, IsPrisoner = false, IsLord = true }),
                new("lord_a", new TraitProfile { HeroId = "lord_a", IsAlive = true, IsPrisoner = false, IsLord = true }),
                new("lord_b", new TraitProfile { HeroId = "lord_b", IsAlive = true, IsPrisoner = false, IsLord = true })
            };

            var result = BanditCaptureFamilySelector.Select(
                prisonerHeroId: prisonerId,
                candidates: candidates,
                playerHeroId: playerId,
                maxCount: 2);

            Assert.Equal(new[] { "lord_a", "lord_b" }, result.SelectedHeroIds);

            var exclusionMap = result.Exclusions.ToDictionary(e => e.HeroId, e => e.Reason);
            Assert.Equal("self", exclusionMap[prisonerId]);
            Assert.Equal("player", exclusionMap[playerId]);
            Assert.Equal("dead", exclusionMap["lord_dead"]);
            Assert.Equal("prisoner", exclusionMap["lord_captured"]);
            Assert.Equal("not in network", exclusionMap["notable_merchant"]);
            Assert.Equal("not in network", exclusionMap["unknown_hero"]);
            Assert.Equal("over cap", exclusionMap["lord_c"]);
        }

        [Fact]
        public void BanditCaptureFamily_SortsEligibleByStringId_AndPicksUpToMax()
        {
            var candidates = new List<KeyValuePair<string, TraitProfile?>>
            {
                new("hero_z", new TraitProfile { HeroId = "hero_z", IsAlive = true, IsPrisoner = false, IsLord = true }),
                new("hero_a", new TraitProfile { HeroId = "hero_a", IsAlive = true, IsPrisoner = false, IsLord = true }),
                new("hero_m", new TraitProfile { HeroId = "hero_m", IsAlive = true, IsPrisoner = false, IsWanderer = true })
            };

            var result = BanditCaptureFamilySelector.Select(
                prisonerHeroId: "prisoner_other",
                candidates: candidates,
                playerHeroId: "player_other",
                maxCount: 2);

            Assert.Equal(2, result.SelectedHeroIds.Count);
            Assert.Equal("hero_a", result.SelectedHeroIds[0]);
            Assert.Equal("hero_m", result.SelectedHeroIds[1]);

            var overCap = Assert.Single(result.Exclusions);
            Assert.Equal("hero_z", overCap.HeroId);
            Assert.Equal("over cap", overCap.Reason);
        }

        [Fact]
        public void Hop0Seeding_HearsayKnower_SeedsAsHop1WithNullSource()
        {
            var evt = new WorldEvent
            {
                EventId = "evt_bandit_test",
                Type = "hero_captured_by_bandits",
                Day = 10.0,
                KnownBy = new List<KnownByEntry>()
            };

            var sub = new EventSubmission
            {
                Type = "hero_captured_by_bandits",
                Day = 10.0,
                Participants = new Dictionary<string, string>
                {
                    ["prisoner"] = "lord_prisoner"
                },
                HearsayKnowerHeroIds = new List<string> { "family_lord_1", "family_lord_2" }
            };

            var traitLookup = new FakeHeroTraitLookup();
            traitLookup.Set(new TraitProfile { HeroId = "lord_prisoner", IsAlive = true, IsPrisoner = true, IsLord = true });
            traitLookup.Set(new TraitProfile { HeroId = "family_lord_1", IsAlive = true, IsPrisoner = false, IsLord = true });
            traitLookup.Set(new TraitProfile { HeroId = "family_lord_2", IsAlive = true, IsPrisoner = false, IsLord = true });

            var channel = new FakePropagationChannel();
            var propConfig = new PropagationConfig();

            Hop0Seeding.Seed(evt, sub, channel, traitLookup, propConfig, playerHeroId: "player_hero", day: 10.0);

            var familyEntry1 = evt.KnownBy.FirstOrDefault(k => k.HeroId == "family_lord_1");
            var familyEntry2 = evt.KnownBy.FirstOrDefault(k => k.HeroId == "family_lord_2");

            Assert.NotNull(familyEntry1);
            Assert.Equal(1, familyEntry1!.Hop);
            Assert.Null(familyEntry1.SourceHeroId);
            Assert.Equal(10.0, familyEntry1.LearnedDay);

            Assert.NotNull(familyEntry2);
            Assert.Equal(1, familyEntry2!.Hop);
            Assert.Null(familyEntry2.SourceHeroId);
            Assert.Equal(10.0, familyEntry2.LearnedDay);
        }

        [Fact]
        public void Hop0Seeding_HearsayKnower_DeduplicatesAgainstParticipants()
        {
            var evt = new WorldEvent
            {
                EventId = "evt_bandit_dedup",
                Type = "hero_captured_by_bandits",
                Day = 10.0,
                KnownBy = new List<KnownByEntry>()
            };

            var sub = new EventSubmission
            {
                Type = "hero_captured_by_bandits",
                Day = 10.0,
                Participants = new Dictionary<string, string>
                {
                    ["prisoner"] = "lord_prisoner"
                },
                HearsayKnowerHeroIds = new List<string> { "lord_prisoner", "family_lord_1" }
            };

            var traitLookup = new FakeHeroTraitLookup();
            traitLookup.Set(new TraitProfile { HeroId = "lord_prisoner", IsAlive = true, IsPrisoner = true, IsLord = true });
            traitLookup.Set(new TraitProfile { HeroId = "family_lord_1", IsAlive = true, IsPrisoner = false, IsLord = true });

            var channel = new FakePropagationChannel();
            var propConfig = new PropagationConfig();

            Hop0Seeding.Seed(evt, sub, channel, traitLookup, propConfig, playerHeroId: "player_hero", day: 10.0);

            var prisonerEntries = evt.KnownBy.Where(k => k.HeroId == "lord_prisoner").ToList();
            Assert.Single(prisonerEntries);
            Assert.Equal(0, prisonerEntries[0].Hop);
        }

        [Fact]
        public void HearsayKnower_SelectsHeardGeneralPrefix()
        {
            var prefix = RumorPrefixSelector.SelectPrefix(
                hop: 1,
                sourceHeroId: null,
                isRetell: false,
                isCorrection: false,
                isParticipant: false);

            Assert.Equal(RumorPrefixKind.HeardGeneral, prefix.Kind);
            Assert.Equal(RumorPrefixSelector.HeardGeneralTextId, prefix.TextId);
            Assert.Equal(RumorPrefixSelector.HeardGeneralFallback, prefix.Fallback);
        }

        [Fact]
        public void BanditCaptureDrama_ClassifiesAllFourProminenceTiers()
        {
            var dramaConfig = new ProminenceDramaConfig
            {
                Ruler = 5,
                ClanLeader = 5,
                NobleMember = 3,
                Minor = 2
            };

            var rulerFacts = new ProminenceFacts
            {
                HeroId = "ruler_hero",
                IsKingdomLeader = true,
                KingdomId = "kingdom_vlandia",
                IsClanLeader = true,
                ClanId = "clan_king",
                IsLord = true
            };
            var rulerResult = PrisonerProminence.Classify(rulerFacts, dramaConfig);
            Assert.Equal(ProminenceTier.Ruler, rulerResult.Tier);
            Assert.Equal(5, rulerResult.Drama);

            var clanLeaderFacts = new ProminenceFacts
            {
                HeroId = "clan_leader_hero",
                IsKingdomLeader = false,
                KingdomId = "kingdom_vlandia",
                IsClanLeader = true,
                ClanId = "clan_noble",
                IsLord = true
            };
            var clanLeaderResult = PrisonerProminence.Classify(clanLeaderFacts, dramaConfig);
            Assert.Equal(ProminenceTier.ClanLeader, clanLeaderResult.Tier);
            Assert.Equal(5, clanLeaderResult.Drama);

            var nobleMemberFacts = new ProminenceFacts
            {
                HeroId = "noble_hero",
                IsKingdomLeader = false,
                KingdomId = "kingdom_vlandia",
                IsClanLeader = false,
                ClanId = "clan_noble",
                IsLord = true
            };
            var nobleResult = PrisonerProminence.Classify(nobleMemberFacts, dramaConfig);
            Assert.Equal(ProminenceTier.NobleMember, nobleResult.Tier);
            Assert.Equal(3, nobleResult.Drama);

            var minorFacts = new ProminenceFacts
            {
                HeroId = "minor_hero",
                IsKingdomLeader = false,
                KingdomId = null,
                IsClanLeader = false,
                ClanId = "clan_mercenary",
                ClanIsMinorFaction = true,
                IsLord = true
            };
            var minorResult = PrisonerProminence.Classify(minorFacts, dramaConfig);
            Assert.Equal(ProminenceTier.Minor, minorResult.Tier);
            Assert.Equal(2, minorResult.Drama);
        }

        [Fact]
        public void ConfigMerge_AddsBanditCaptureDramaByProminence_PreservingExistingValues()
        {
            const string oldConfigJson = @"{
  ""version"": 1,
  ""events"": {
    ""flushIntervalHours"": 12,
    ""prisonerDramaByProminence"": {
      ""ruler"": 5,
      ""clanLeader"": 4,
      ""nobleMember"": 3,
      ""minor"": 2
    }
  },
  ""presentation"": {
    ""factOrder"": [""WHO"", ""WHAT"", ""OUTCOME"", ""WHERE""]
  }
}";

            var defConfig = new VividWorldConfig();
            string defConfigJson = VividJson.Write(defConfig);

            var existingJObj = JObject.Parse(oldConfigJson);
            var canonicalJObj = JObject.Parse(defConfigJson);

            var result = ConfigMerge.AddMissingKeys(existingJObj, canonicalJObj);

            Assert.NotEmpty(result.AddedPaths);
            Assert.Contains(result.AddedPaths, p => p.Contains("banditCaptureDramaByProminence"));

            var mergedObj = result.Merged;
            Assert.Equal(12, (int)mergedObj["events"]!["flushIntervalHours"]!);
            Assert.Equal(5, (int)mergedObj["events"]!["banditCaptureDramaByProminence"]!["ruler"]!);
            Assert.Equal(5, (int)mergedObj["events"]!["banditCaptureDramaByProminence"]!["clanLeader"]!);
            Assert.Equal(3, (int)mergedObj["events"]!["banditCaptureDramaByProminence"]!["nobleMember"]!);
            Assert.Equal(2, (int)mergedObj["events"]!["banditCaptureDramaByProminence"]!["minor"]!);
        }

        [Fact]
        public void Normalize_ClampsBanditCaptureDramaByProminence_ToValidRange()
        {
            var cfg = new VividWorldConfig();
            cfg.Events.BanditCaptureDramaByProminence.Ruler = 99;
            cfg.Events.BanditCaptureDramaByProminence.ClanLeader = 0;
            cfg.Events.BanditCaptureDramaByProminence.NobleMember = -5;
            cfg.Events.BanditCaptureDramaByProminence.Minor = 6;

            var notices = new List<ClampNotice>();
            cfg.Normalize(notices);

            Assert.Equal(5, cfg.Events.BanditCaptureDramaByProminence.Ruler);
            Assert.Equal(1, cfg.Events.BanditCaptureDramaByProminence.ClanLeader);
            Assert.Equal(1, cfg.Events.BanditCaptureDramaByProminence.NobleMember);
            Assert.Equal(5, cfg.Events.BanditCaptureDramaByProminence.Minor);

            Assert.Contains(notices, n => n.Key == "events.banditCaptureDramaByProminence.ruler");
            Assert.Contains(notices, n => n.Key == "events.banditCaptureDramaByProminence.clanLeader");
            Assert.Contains(notices, n => n.Key == "events.banditCaptureDramaByProminence.nobleMember");
            Assert.Contains(notices, n => n.Key == "events.banditCaptureDramaByProminence.minor");
        }

        [Fact]
        public void TemplateBinder_BindsHeroCapturedByBandits_WithFactionOrUnknownBandits()
        {
            string repoRoot = FindRepoRoot();
            string eventsPath = Path.Combine(repoRoot, "module", "ModuleData", "vividworld_events.json");
            var catalog = EventCatalogLoader.Load(File.ReadAllText(eventsPath), new PersistenceConfig());

            var template = catalog.ByType("hero_captured_by_bandits");
            Assert.NotNull(template);

            // 1. With bandit faction
            var bindingsFaction = new Dictionary<string, string>
            {
                ["PRISONER"] = "lord_vlandia_1",
                ["BANDITS"] = "faction:clan_desert_bandits",
                ["SETTLEMENT"] = "town_v1"
            };
            var subFaction = TemplateBinder.Bind(template!, bindingsFaction, 10.0, null, out var issuesFaction);
            Assert.NotNull(subFaction);
            Assert.Empty(issuesFaction.Where(i => i.IsError));
            Assert.Equal("hero_captured_by_bandits", subFaction!.Type);

            var whoFactFaction = subFaction.Facts.FirstOrDefault(f => f.Id == "who");
            Assert.NotNull(whoFactFaction);
            Assert.NotNull(whoFactFaction!.Vars);
            Assert.Equal("faction:clan_desert_bandits", whoFactFaction.Vars!["BANDITS"]);
            Assert.Equal("hero:lord_vlandia_1", whoFactFaction.Vars["PRISONER"]);

            // 2. With unknown bandits
            var bindingsUnknown = new Dictionary<string, string>
            {
                ["PRISONER"] = "lord_vlandia_2",
                ["BANDITS"] = "key:VividWorld_UnknownBandits"
            };
            var subUnknown = TemplateBinder.Bind(template!, bindingsUnknown, 10.0, null, out var issuesUnknown);
            Assert.NotNull(subUnknown);
            Assert.Empty(issuesUnknown.Where(i => i.IsError));

            var whoFactUnknown = subUnknown!.Facts.FirstOrDefault(f => f.Id == "who");
            Assert.NotNull(whoFactUnknown);
            Assert.NotNull(whoFactUnknown!.Vars);
            Assert.Equal("key:VividWorld_UnknownBandits", whoFactUnknown.Vars!["BANDITS"]);
        }

        [Fact]
        public void TemplateBinder_BindsHeroCapturedByBandits_DropsWhereWhenUnbound()
        {
            string repoRoot = FindRepoRoot();
            string eventsPath = Path.Combine(repoRoot, "module", "ModuleData", "vividworld_events.json");
            var catalog = EventCatalogLoader.Load(File.ReadAllText(eventsPath), new PersistenceConfig());
            var template = catalog.ByType("hero_captured_by_bandits")!;

            var bindingsWithoutWhere = new Dictionary<string, string>
            {
                ["PRISONER"] = "lord_vlandia_1",
                ["BANDITS"] = "key:VividWorld_UnknownBandits"
            };

            var sub = TemplateBinder.Bind(template, bindingsWithoutWhere, 10.0, null, out var issues);
            Assert.NotNull(sub);
            Assert.Empty(issues.Where(i => i.IsError));
            Assert.Null(sub!.Facts.FirstOrDefault(f => f.Id == "where"));
            Assert.Equal(3, sub.Facts.Count); // who, what, outcome
        }
    }
}
