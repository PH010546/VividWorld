using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using VividWorld.Core.Catalog;
using VividWorld.Core.Config;
using VividWorld.Core.Events;
using VividWorld.Core.Ingest;
using VividWorld.Core.Memory;
using VividWorld.Core.Persistence;
using VividWorld.Core.Presentation;
using VividWorld.Core.Rumors;
using VividWorld.Core.Tests.Fakes;
using VividWorld.Core.Util;
using Xunit;

namespace VividWorld.Core.Tests
{
    public class BanditReleaseTests
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

        private static EventCatalog LoadCatalog()
        {
            string repoRoot = FindRepoRoot();
            string eventsPath = Path.Combine(repoRoot, "module", "ModuleData", "vividworld_events.json");
            return EventCatalogLoader.Load(File.ReadAllText(eventsPath), new PersistenceConfig());
        }

        #region 1. CaptureLookup (兩種被俘查找、取最近、修復連錯)

        [Fact]
        public void CaptureLookup_BothTypesPresent_PicksMostRecent()
        {
            var index = new RumorIndex();
            const string prisonerId = "lord_prisoner";

            index.Entries.Add(new RumorIndexEntry
            {
                EventId = "evt_lord_cap",
                Type = "hero_taken_prisoner",
                Day = 10.0,
                ParticipantHeroIds = new List<string> { prisonerId, "lord_captor" }
            });

            index.Entries.Add(new RumorIndexEntry
            {
                EventId = "evt_bandit_cap",
                Type = "hero_captured_by_bandits",
                Day = 20.0,
                ParticipantHeroIds = new List<string> { prisonerId }
            });

            var latest = CaptureLookup.FindLatestCapture(index, prisonerId, 25.0);

            Assert.NotNull(latest);
            Assert.Equal("evt_bandit_cap", latest!.EventId);
            Assert.Equal("hero_captured_by_bandits", latest.Type);
            Assert.Equal(20.0, latest.Day);
        }

        [Fact]
        public void CaptureLookup_BanditCaptureEarlierThanLordCapture_PicksLordCapture()
        {
            var index = new RumorIndex();
            const string prisonerId = "lord_prisoner";

            index.Entries.Add(new RumorIndexEntry
            {
                EventId = "evt_bandit_cap",
                Type = "hero_captured_by_bandits",
                Day = 10.0,
                ParticipantHeroIds = new List<string> { prisonerId }
            });

            index.Entries.Add(new RumorIndexEntry
            {
                EventId = "evt_lord_cap",
                Type = "hero_taken_prisoner",
                Day = 20.0,
                ParticipantHeroIds = new List<string> { prisonerId, "lord_captor" }
            });

            var latest = CaptureLookup.FindLatestCapture(index, prisonerId, 25.0);

            Assert.NotNull(latest);
            Assert.Equal("evt_lord_cap", latest!.EventId);
            Assert.Equal("hero_taken_prisoner", latest.Type);
            Assert.Equal(20.0, latest.Day);
        }

        [Fact]
        public void CaptureLookup_OldLordCaptureAndNewBanditCapture_PicksBanditCapture()
        {
            // 驗證獲救連回被俘時，取最近的一則盜匪俘虜而非21天前的舊領主俘虜
            var index = new RumorIndex();
            const string prisonerId = "lord_2_16"; // 約里格

            index.Entries.Add(new RumorIndexEntry
            {
                EventId = "evt_old_lord_cap",
                Type = "hero_taken_prisoner",
                Day = 91090.0,
                ParticipantHeroIds = new List<string> { prisonerId, "lord_other" }
            });

            index.Entries.Add(new RumorIndexEntry
            {
                EventId = "evt_recent_bandit_cap",
                Type = "hero_captured_by_bandits",
                Day = 91110.0,
                ParticipantHeroIds = new List<string> { prisonerId }
            });

            var latest = CaptureLookup.FindLatestCapture(index, prisonerId, 91111.0);

            Assert.NotNull(latest);
            Assert.Equal("evt_recent_bandit_cap", latest!.EventId);
            Assert.Equal("hero_captured_by_bandits", latest.Type);
            Assert.Equal(91110.0, latest.Day);
        }

        [Fact]
        public void CaptureLookup_SameDayTieBreak_UsesEventIdDescending()
        {
            var index = new RumorIndex();
            const string prisonerId = "lord_prisoner";

            index.Entries.Add(new RumorIndexEntry
            {
                EventId = "evt_cap_aaa",
                Type = "hero_taken_prisoner",
                Day = 15.0,
                ParticipantHeroIds = new List<string> { prisonerId }
            });

            index.Entries.Add(new RumorIndexEntry
            {
                EventId = "evt_cap_zzz",
                Type = "hero_captured_by_bandits",
                Day = 15.0,
                ParticipantHeroIds = new List<string> { prisonerId }
            });

            var latest = CaptureLookup.FindLatestCapture(index, prisonerId, 16.0);

            Assert.NotNull(latest);
            Assert.Equal("evt_cap_zzz", latest!.EventId);
        }

        #endregion

        #region 2. 救人者未查到時的模板改寫（拿掉 what、使用 WhoNoRescuer、角色只剩 prisoner）

        [Fact]
        public void NoRescuer_AdaptedTemplate_RemovesWhatFactAndHasWhoNoRescuer()
        {
            var catalog = LoadCatalog();
            var baseTemplate = catalog.ByType("hero_rescued_from_bandits");
            Assert.NotNull(baseTemplate);

            // 模擬無救人者時的改寫邏輯
            var adaptedTemplate = new EventTemplate
            {
                Type = baseTemplate!.Type,
                Origin = baseTemplate.Origin,
                DramaWeight = baseTemplate.DramaWeight,
                LinkedTemplateType = baseTemplate.LinkedTemplateType,
                Roles = new Dictionary<string, string> { ["prisoner"] = "{PRISONER}" },
                KnowingRoles = new HashSet<string>(baseTemplate.KnowingRoles),
                Facts = baseTemplate.Facts
                    .Where(f => !string.Equals(f.Id, "what", StringComparison.OrdinalIgnoreCase))
                    .Select(f =>
                    {
                        if (string.Equals(f.Id, "who", StringComparison.OrdinalIgnoreCase))
                        {
                            return new TemplateFact
                            {
                                Id = f.Id,
                                Category = f.Category,
                                TextId = "VividWorld_Fact_HeroRescuedFromBandits_WhoNoRescuer",
                                Text = "a band of {BANDITS} was routed, and {PRISONER} got away in the confusion",
                                Vars = new Dictionary<string, string>
                                {
                                    ["BANDITS"] = "{BANDITS}",
                                    ["PRISONER"] = "hero:{PRISONER}"
                                },
                                Fragility = f.Fragility,
                                Optional = f.Optional
                            };
                        }
                        return new TemplateFact
                        {
                            Id = f.Id,
                            Category = f.Category,
                            TextId = f.TextId,
                            Text = f.Text,
                            Vars = f.Vars != null ? new Dictionary<string, string>(f.Vars) : new Dictionary<string, string>(),
                            Fragility = f.Fragility,
                            Optional = f.Optional
                        };
                    }).ToList()
            };

            // 驗證角色只剩 prisoner
            Assert.Single(adaptedTemplate.Roles);
            Assert.True(adaptedTemplate.Roles.ContainsKey("prisoner"));
            Assert.False(adaptedTemplate.Roles.ContainsKey("rescuer"));

            // 驗證 what 碎片已被完全移除
            Assert.Equal(3, adaptedTemplate.Facts.Count);
            Assert.DoesNotContain(adaptedTemplate.Facts, f => string.Equals(f.Id, "what", StringComparison.OrdinalIgnoreCase));

            // 驗證 who 碎片使用 WhoNoRescuer
            var whoFact = adaptedTemplate.Facts.FirstOrDefault(f => string.Equals(f.Id, "who", StringComparison.OrdinalIgnoreCase));
            Assert.NotNull(whoFact);
            Assert.Equal("VividWorld_Fact_HeroRescuedFromBandits_WhoNoRescuer", whoFact!.TextId);

            // 綁定測試：無 RESCUER 綁定，僅提供 PRISONER 與 BANDITS
            var bindings = new Dictionary<string, string>
            {
                ["PRISONER"] = "lord_prisoner",
                ["BANDITS"] = "key:VividWorld_UnknownBandits",
                ["SETTLEMENT"] = "settlement_1"
            };

            var submission = TemplateBinder.Bind(adaptedTemplate, bindings, 50.0, "evt_linked_cap", out var issues);
            Assert.NotNull(submission);
            Assert.Empty(issues.Where(i => i.IsError));
            Assert.Equal("hero_rescued_from_bandits", submission!.Type);
            Assert.Equal("evt_linked_cap", submission.LinkedEventId);
            Assert.Equal(3, submission.Facts.Count);
            Assert.DoesNotContain(submission.Facts, f => string.Equals(f.Id, "what", StringComparison.OrdinalIgnoreCase));
        }

        #endregion

        #region 3. 第一人稱自述句（救人者 _Self_RESCUER 與 被俘者 _Self_PRISONER）

        [Fact]
        public void Rescuer_FirstPerson_ProducesSelfRescuerLine()
        {
            var catalog = LoadCatalog();
            var template = catalog.ByType("hero_rescued_from_bandits");
            Assert.NotNull(template);

            var bindings = new Dictionary<string, string>
            {
                ["PRISONER"] = "lord_prisoner",
                ["RESCUER"] = "lord_rescuer",
                ["BANDITS"] = "key:VividWorld_UnknownBandits",
                ["SETTLEMENT"] = "settlement_dungeon"
            };

            var submission = TemplateBinder.Bind(template!, bindings, 100.0, "evt_cap", out var issues);
            Assert.NotNull(submission);
            Assert.Empty(issues.Where(i => i.IsError));

            var worldEvent = new WorldEvent
            {
                EventId = "evt_rescue_1",
                Type = submission!.Type,
                Day = submission.Day,
                Origin = submission.Origin ?? EventOrigin.Public,
                DramaWeight = submission.DramaWeight ?? 4,
                LinkedEventId = submission.LinkedEventId,
                Participants = submission.Participants,
                Facts = submission.Facts
            };

            var cfg = new PresentationConfig();

            // 救人者自己講話：第一人稱
            var composedRescuer = RumorTextComposer.Compose(
                worldEvent,
                worldEvent.Facts,
                cfg,
                prefix: null,
                speakerHeroId: "lord_rescuer");

            Assert.Equal("rescuer", composedRescuer.SpeakerRole);
            var stringTable = EnglishStringTable.LoadFromFile(Path.Combine(FindRepoRoot(), "module", "ModuleData", "Languages", "std_module_strings_xml.xml"));
            string ResolveVar(string val, bool useLinks)
            {
                if (val.StartsWith("key:")) return stringTable.GetWithFallback(val.Substring(4), val.Substring(4));
                if (val.StartsWith("hero:")) return val.Substring(5);
                return val;
            }
            string GetTemplate(string? textId, string? fallback) =>
                fallback == null ? (stringTable.Get(textId) ?? string.Empty) : stringTable.Lookup(textId, fallback);
            string GetLocalized(string? key, string fallback) => stringTable.GetWithFallback(key, fallback);

            var renderedRescuer = RumorTextAssembler.Assemble(
                composedRescuer,
                cfg,
                ResolveVar,
                GetTemplate,
                GetLocalized);

            Assert.Contains("I routed those outlaws and freed lord_prisoner", renderedRescuer.PlainText);
            Assert.Contains("was mostly unharmed, just half-starved", renderedRescuer.PlainText);
            Assert.Contains("and I had my men escort", renderedRescuer.PlainText);

            // 被救者自己講話：第一人稱
            var composedPrisoner = RumorTextComposer.Compose(
                worldEvent,
                worldEvent.Facts,
                cfg,
                prefix: null,
                speakerHeroId: "lord_prisoner");

            Assert.Equal("prisoner", composedPrisoner.SpeakerRole);
            var renderedPrisoner = RumorTextAssembler.Assemble(
                composedPrisoner,
                cfg,
                ResolveVar,
                GetTemplate,
                GetLocalized);

            Assert.Contains("lord_rescuer routed those outlaws and freed me", renderedPrisoner.PlainText);
            Assert.Contains("when they cut me loose, my legs gave way", renderedPrisoner.PlainText);
            Assert.Contains("I count myself lucky to be alive", renderedPrisoner.PlainText);
        }

        [Fact]
        public void EscapedBandits_FirstPerson_ProducesSelfPrisonerLine()
        {
            var catalog = LoadCatalog();
            var template = catalog.ByType("hero_escaped_bandits");
            Assert.NotNull(template);

            var bindings = new Dictionary<string, string>
            {
                ["PRISONER"] = "lord_prisoner",
                ["BANDITS"] = "key:VividWorld_UnknownBandits",
                ["SETTLEMENT"] = "settlement_wilds"
            };

            var submission = TemplateBinder.Bind(template!, bindings, 100.0, "evt_cap", out var issues);
            Assert.NotNull(submission);
            Assert.Empty(issues.Where(i => i.IsError));

            var worldEvent = new WorldEvent
            {
                EventId = "evt_escape_1",
                Type = submission!.Type,
                Day = submission.Day,
                Origin = submission.Origin ?? EventOrigin.Public,
                DramaWeight = submission.DramaWeight ?? 4,
                LinkedEventId = submission.LinkedEventId,
                Participants = submission.Participants,
                Facts = submission.Facts
            };

            var cfg = new PresentationConfig();

            var composed = RumorTextComposer.Compose(
                worldEvent,
                worldEvent.Facts,
                cfg,
                prefix: null,
                speakerHeroId: "lord_prisoner");

            Assert.Equal("prisoner", composed.SpeakerRole);
            var stringTable = EnglishStringTable.LoadFromFile(Path.Combine(FindRepoRoot(), "module", "ModuleData", "Languages", "std_module_strings_xml.xml"));
            string ResolveVar(string val, bool useLinks)
            {
                if (val.StartsWith("key:")) return stringTable.GetWithFallback(val.Substring(4), val.Substring(4));
                if (val.StartsWith("hero:")) return val.Substring(5);
                return val;
            }
            string GetTemplate(string? textId, string? fallback) =>
                fallback == null ? (stringTable.Get(textId) ?? string.Empty) : stringTable.Lookup(textId, fallback);
            string GetLocalized(string? key, string fallback) => stringTable.GetWithFallback(key, fallback);

            var rendered = RumorTextAssembler.Assemble(
                composed,
                cfg,
                ResolveVar,
                GetTemplate,
                GetLocalized);

            Assert.Contains("I escaped from those outlaws", rendered.PlainText);
            Assert.Contains("while the guards slept, I wore through my bonds", rendered.PlainText);
            Assert.Contains("I walked all night in the dark before I saw a light", rendered.PlainText);
        }

        #endregion

        #region 4. 過時標記（獲救連到盜匪俘虜時，知情者標記過時且不再講述）

        [Fact]
        public void Outdating_RescueFromBandits_MarksBanditCaptureOutdated()
        {
            // 建立盜匪俘虜事件
            var banditCapture = new WorldEvent
            {
                EventId = "evt_bandit_cap_1",
                Type = "hero_captured_by_bandits",
                Day = 10.0,
                Origin = EventOrigin.Public,
                DramaWeight = 4,
                Participants = new Dictionary<string, string> { ["prisoner"] = "lord_p" },
                KnownBy = new List<KnownByEntry>
                {
                    new KnownByEntry { HeroId = "lord_knower_1", Hop = 1, LearnedDay = 10.5 },
                    new KnownByEntry { HeroId = "lord_knower_2", Hop = 1, LearnedDay = 11.0 }
                }
            };

            // 獲救事件知情者 lord_knower_1 在第 15 天知曉釋放
            var marked = new List<string>();
            int count = Outdating.MarkOutdated(banditCapture, new[] { "lord_knower_1" }, 15.0, marked);

            Assert.Equal(1, count);
            Assert.Equal("lord_knower_1", marked[0]);

            var entry1 = banditCapture.EntryFor("lord_knower_1");
            Assert.NotNull(entry1);
            Assert.Equal(15.0, entry1!.OutdatedDay);
            Assert.True(Outdating.IsOutdated(entry1));

            // lord_knower_2 尚未知曉獲救，未過時
            var entry2 = banditCapture.EntryFor("lord_knower_2");
            Assert.NotNull(entry2);
            Assert.Null(entry2!.OutdatedDay);
            Assert.False(Outdating.IsOutdated(entry2));

            // 驗證講述者輪排除過時候選
            var cfg = new VividWorldConfig();
            var rng = new SplitMix64Rng();
            var channel = new FakePropagationChannel();
            var traits = new FakeHeroTraitLookup();
            traits.Set(new TraitProfile { HeroId = "lord_knower_1", IsAlive = true, IsLord = true });
            var retention = FactRetentionPolicies.Create(cfg, rng, 42L);
            var engine = new RumorEngine(cfg, retention, NullEmbellishmentPolicy.Instance, channel, traits, rng, 42L, "player");

            var choice = engine.ChooseTopic("lord_knower_1", new[] { banditCapture }, 20.0, 12);
            Assert.NotNull(choice);
            Assert.Null(choice.PickedEventId);
            var exclusion = choice.Exclusions.FirstOrDefault(e => e.EventId == "evt_bandit_cap_1");
            Assert.NotNull(exclusion);
            Assert.Equal(TellReason.Outdated, exclusion!.Reason);
        }

        #endregion

        #region 5. Drama 四種身分與 Normalize 夾取

        [Fact]
        public void BanditReleaseDrama_FourProminenceTiers()
        {
            var cfg = new VividWorldConfig();
            var dramaCfg = cfg.Events.BanditReleaseDramaByProminence;

            // 驗證預設值：Ruler 5, ClanLeader 4, NobleMember 2, Minor 1
            Assert.Equal(5, dramaCfg.Ruler);
            Assert.Equal(4, dramaCfg.ClanLeader);
            Assert.Equal(2, dramaCfg.NobleMember);
            Assert.Equal(1, dramaCfg.Minor);

            // Ruler
            var rulerFacts = new ProminenceFacts { HeroId = "h_ruler", IsKingdomLeader = true, IsLord = true };
            var resRuler = PrisonerProminence.Classify(rulerFacts, dramaCfg);
            Assert.Equal(ProminenceTier.Ruler, resRuler.Tier);
            Assert.Equal(5, resRuler.Drama);

            // ClanLeader
            var clanLeaderFacts = new ProminenceFacts { HeroId = "h_leader", IsClanLeader = true, IsLord = true };
            var resClanLeader = PrisonerProminence.Classify(clanLeaderFacts, dramaCfg);
            Assert.Equal(ProminenceTier.ClanLeader, resClanLeader.Tier);
            Assert.Equal(4, resClanLeader.Drama);

            // NobleMember
            var nobleFacts = new ProminenceFacts { HeroId = "h_noble", ClanId = "clan_noble", IsLord = true };
            var resNoble = PrisonerProminence.Classify(nobleFacts, dramaCfg);
            Assert.Equal(ProminenceTier.NobleMember, resNoble.Tier);
            Assert.Equal(2, resNoble.Drama);

            // Minor
            var minorFacts = new ProminenceFacts { HeroId = "h_wanderer", IsLord = false };
            var resMinor = PrisonerProminence.Classify(minorFacts, dramaCfg);
            Assert.Equal(ProminenceTier.Minor, resMinor.Tier);
            Assert.Equal(1, resMinor.Drama);
        }

        [Fact]
        public void Normalize_ClampsBanditReleaseDramaByProminence_ToValidRange()
        {
            var cfg = new VividWorldConfig();
            cfg.Events.BanditReleaseDramaByProminence.Ruler = 99;
            cfg.Events.BanditReleaseDramaByProminence.ClanLeader = 0;
            cfg.Events.BanditReleaseDramaByProminence.NobleMember = -5;
            cfg.Events.BanditReleaseDramaByProminence.Minor = 10;

            var notices = new List<ClampNotice>();
            cfg.Normalize(notices);

            Assert.Equal(5, cfg.Events.BanditReleaseDramaByProminence.Ruler);
            Assert.Equal(1, cfg.Events.BanditReleaseDramaByProminence.ClanLeader);
            Assert.Equal(1, cfg.Events.BanditReleaseDramaByProminence.NobleMember);
            Assert.Equal(5, cfg.Events.BanditReleaseDramaByProminence.Minor);

            Assert.Contains(notices, n => n.Key == "events.banditReleaseDramaByProminence.ruler");
            Assert.Contains(notices, n => n.Key == "events.banditReleaseDramaByProminence.clanLeader");
            Assert.Contains(notices, n => n.Key == "events.banditReleaseDramaByProminence.nobleMember");
            Assert.Contains(notices, n => n.Key == "events.banditReleaseDramaByProminence.minor");
        }

        #endregion

        #region 6. ConfigMerge 補鍵

        [Fact]
        public void ConfigMerge_FillsBanditReleaseDramaByProminence_LeavingOtherSettingsIntact()
        {
            string oldConfigJson = @"{
  ""configVersion"": 1,
  ""events"": {
    ""flushIntervalHours"": 12,
    ""banditCaptureDramaByProminence"": {
      ""ruler"": 5,
      ""clanLeader"": 5,
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
            Assert.Contains(result.AddedPaths, p => p.Contains("banditReleaseDramaByProminence"));

            var mergedObj = result.Merged;
            Assert.Equal(12, (int)mergedObj["events"]!["flushIntervalHours"]!);
            Assert.Equal(5, (int)mergedObj["events"]!["banditReleaseDramaByProminence"]!["ruler"]!);
            Assert.Equal(4, (int)mergedObj["events"]!["banditReleaseDramaByProminence"]!["clanLeader"]!);
            Assert.Equal(2, (int)mergedObj["events"]!["banditReleaseDramaByProminence"]!["nobleMember"]!);
            Assert.Equal(1, (int)mergedObj["events"]!["banditReleaseDramaByProminence"]!["minor"]!);
        }

        #endregion

        #region 7. TemplateBinder LinkedEventId 保留

        [Fact]
        public void Bind_BanditReleaseAndEscape_KeepsLinkedEventId()
        {
            var catalog = LoadCatalog();

            var rescueTemplate = catalog.ByType("hero_rescued_from_bandits");
            Assert.NotNull(rescueTemplate);
            var rescueBindings = new Dictionary<string, string>
            {
                ["PRISONER"] = "lord_p",
                ["RESCUER"] = "lord_r",
                ["BANDITS"] = "key:VividWorld_UnknownBandits",
                ["SETTLEMENT"] = "settlement_1"
            };
            var rescueSub = TemplateBinder.Bind(rescueTemplate!, rescueBindings, 50.0, "evt_linked_cap_1", out var rescueIssues);
            Assert.NotNull(rescueSub);
            Assert.Empty(rescueIssues.Where(i => i.IsError));
            Assert.Equal("evt_linked_cap_1", rescueSub!.LinkedEventId);

            var escapeTemplate = catalog.ByType("hero_escaped_bandits");
            Assert.NotNull(escapeTemplate);
            var escapeBindings = new Dictionary<string, string>
            {
                ["PRISONER"] = "lord_p",
                ["BANDITS"] = "key:VividWorld_UnknownBandits",
                ["SETTLEMENT"] = "settlement_1"
            };
            var escapeSub = TemplateBinder.Bind(escapeTemplate!, escapeBindings, 50.0, "evt_linked_cap_2", out var escapeIssues);
            Assert.NotNull(escapeSub);
            Assert.Empty(escapeIssues.Where(i => i.IsError));
            Assert.Equal("evt_linked_cap_2", escapeSub!.LinkedEventId);
        }

        #endregion

        #region 8. 玩家從盜匪手中救出英雄判定 (PlayerRescueEvaluator)

        [Fact]
        public void PlayerRescue_WhenMainPartyHoldsPrisoner_EvaluatesToYesMainPartyHoldsPrisoner()
        {
            var result1 = PlayerRescueEvaluator.Evaluate(
                "hero_captured_by_bandits",
                isMainParty: true,
                hasLeaderOrOwner: true,
                EndCaptivityDetail.ReleasedByChoice);

            Assert.True(result1.IsPlayerRescue);
            Assert.Equal("main party holds prisoner", result1.Via);
            Assert.Null(result1.Reason);

            var result2 = PlayerRescueEvaluator.Evaluate(
                "hero_captured_by_bandits",
                isMainParty: true,
                hasLeaderOrOwner: false,
                EndCaptivityDetail.ReleasedAfterBattle);

            Assert.True(result2.IsPlayerRescue);
            Assert.Equal("main party holds prisoner", result2.Via);
            Assert.Null(result2.Reason);
        }

        [Fact]
        public void PlayerRescue_WhenReleasedByChoiceFromLeaderlessParty_EvaluatesToYesReleasedByChoiceFromLeaderlessParty()
        {
            var result = PlayerRescueEvaluator.Evaluate(
                "hero_captured_by_bandits",
                isMainParty: false,
                hasLeaderOrOwner: false,
                EndCaptivityDetail.ReleasedByChoice);

            Assert.True(result.IsPlayerRescue);
            Assert.Equal("released by choice from leaderless party", result.Via);
            Assert.Null(result.Reason);

            var strResult = PlayerRescueEvaluator.Evaluate(
                "hero_captured_by_bandits",
                isMainParty: false,
                hasLeaderOrOwner: false,
                EndCaptivityDetail.ReleasedByChoice);

            Assert.True(strResult.IsPlayerRescue);
            Assert.Equal("released by choice from leaderless party", strResult.Via);
            Assert.Null(strResult.Reason);
        }

        [Fact]
        public void PlayerRescue_WhenLatestCaptureNotBandits_EvaluatesToNoLatestCaptureIsNotByBandits()
        {
            var result1 = PlayerRescueEvaluator.Evaluate(
                "hero_taken_prisoner",
                isMainParty: true,
                hasLeaderOrOwner: true,
                EndCaptivityDetail.ReleasedByChoice);

            Assert.False(result1.IsPlayerRescue);
            Assert.Equal("latest capture is not by bandits", result1.Reason);
            Assert.Null(result1.Via);

            var result2 = PlayerRescueEvaluator.Evaluate(
                null,
                isMainParty: false,
                hasLeaderOrOwner: false,
                EndCaptivityDetail.ReleasedByChoice);

            Assert.False(result2.IsPlayerRescue);
            Assert.Equal("latest capture is not by bandits", result2.Reason);
            Assert.Null(result2.Via);

            var result3 = PlayerRescueEvaluator.Evaluate(
                "other_event_type",
                isMainParty: true,
                hasLeaderOrOwner: false,
                EndCaptivityDetail.ReleasedAfterBattle);

            Assert.False(result3.IsPlayerRescue);
            Assert.Equal("latest capture is not by bandits", result3.Reason);
            Assert.Null(result3.Via);
        }

        [Fact]
        public void PlayerRescue_WhenPartyBelongsToAnotherHero_EvaluatesToNoPartyIsAnotherHeros()
        {
            var result = PlayerRescueEvaluator.Evaluate(
                "hero_captured_by_bandits",
                isMainParty: false,
                hasLeaderOrOwner: true,
                EndCaptivityDetail.ReleasedByChoice);

            Assert.False(result.IsPlayerRescue);
            Assert.Equal("party is another hero's", result.Reason);
            Assert.Null(result.Via);

            var strResult = PlayerRescueEvaluator.Evaluate(
                "hero_captured_by_bandits",
                isMainParty: false,
                hasLeaderOrOwner: true,
                EndCaptivityDetail.ReleasedByChoice);

            Assert.False(strResult.IsPlayerRescue);
            Assert.Equal("party is another hero's", strResult.Reason);
            Assert.Null(strResult.Via);
        }

        [Fact]
        public void PlayerRescue_WhenLeaderlessPartyDetailNotReleasedByChoice_EvaluatesToNoDetailFromLeaderlessParty()
        {
            var resultBattle = PlayerRescueEvaluator.Evaluate(
                "hero_captured_by_bandits",
                isMainParty: false,
                hasLeaderOrOwner: false,
                EndCaptivityDetail.ReleasedAfterBattle);

            Assert.False(resultBattle.IsPlayerRescue);
            Assert.Equal("detail ReleasedAfterBattle from leaderless party", resultBattle.Reason);
            Assert.Null(resultBattle.Via);

            var resultEscape = PlayerRescueEvaluator.Evaluate(
                "hero_captured_by_bandits",
                isMainParty: false,
                hasLeaderOrOwner: false,
                EndCaptivityDetail.ReleasedAfterEscape);

            Assert.False(resultEscape.IsPlayerRescue);
            Assert.Equal("detail ReleasedAfterEscape from leaderless party", resultEscape.Reason);
            Assert.Null(resultEscape.Via);

            var resultRansom = PlayerRescueEvaluator.Evaluate(
                "hero_captured_by_bandits",
                isMainParty: false,
                hasLeaderOrOwner: false,
                EndCaptivityDetail.Ransom);

            Assert.False(resultRansom.IsPlayerRescue);
            Assert.Equal("detail Ransom from leaderless party", resultRansom.Reason);
            Assert.Null(resultRansom.Via);
        }

        [Fact]
        public void PlayerRescue_ResolveBanditsVar_ReadsFromWhoFactInLinkedEvent()
        {
            var captureEvent = new WorldEvent
            {
                EventId = "evt_cap_1",
                Type = "hero_captured_by_bandits",
                Facts = new List<Fact>
                {
                    new Fact
                    {
                        Id = "who",
                        Vars = new Dictionary<string, string>
                        {
                            ["PRISONER"] = "hero:lord_1",
                            ["BANDITS"] = "faction:mountain_bandits"
                        }
                    }
                }
            };

            var bandits = PlayerRescueEvaluator.ResolveBanditsVar(captureEvent);
            Assert.Equal("faction:mountain_bandits", bandits);
        }

        [Fact]
        public void PlayerRescue_ResolveBanditsVar_WhenMissingOrEmpty_ReturnsUnknownBandits()
        {
            var evtWithoutBandits = new WorldEvent
            {
                EventId = "evt_cap_2",
                Type = "hero_captured_by_bandits",
                Facts = new List<Fact>
                {
                    new Fact
                    {
                        Id = "who",
                        Vars = new Dictionary<string, string>
                        {
                            ["PRISONER"] = "hero:lord_1"
                        }
                    }
                }
            };
            Assert.Equal("key:VividWorld_UnknownBandits", PlayerRescueEvaluator.ResolveBanditsVar(evtWithoutBandits));

            var evtWithEmptyBandits = new WorldEvent
            {
                EventId = "evt_cap_3",
                Type = "hero_captured_by_bandits",
                Facts = new List<Fact>
                {
                    new Fact
                    {
                        Id = "who",
                        Vars = new Dictionary<string, string>
                        {
                            ["BANDITS"] = ""
                        }
                    }
                }
            };
            Assert.Equal("key:VividWorld_UnknownBandits", PlayerRescueEvaluator.ResolveBanditsVar(evtWithEmptyBandits));

            var evtWithNullVars = new WorldEvent
            {
                EventId = "evt_cap_4",
                Facts = new List<Fact>
                {
                    new Fact { Id = "who", Vars = null }
                }
            };
            Assert.Equal("key:VividWorld_UnknownBandits", PlayerRescueEvaluator.ResolveBanditsVar(evtWithNullVars));
        }

        [Fact]
        public void PlayerRescue_ResolveBanditsVar_WhenEventOrWhoFactNull_ReturnsUnknownBandits()
        {
            Assert.Equal("key:VividWorld_UnknownBandits", PlayerRescueEvaluator.ResolveBanditsVar(null));

            var evtNoWho = new WorldEvent
            {
                EventId = "evt_cap_5",
                Facts = new List<Fact>
                {
                    new Fact { Id = "where" }
                }
            };
            Assert.Equal("key:VividWorld_UnknownBandits", PlayerRescueEvaluator.ResolveBanditsVar(evtNoWho));
        }

        [Fact]
        public void PlayerRescue_FormatLog_FormatsYesAndNoCorrectly()
        {
            var yesResult = new PlayerRescueResult(true, "main party holds prisoner", null);
            var logYes = PlayerRescueEvaluator.FormatLog(yesResult, "party_player", "ReleasedByChoice");
            Assert.Equal("player rescue: yes via main party holds prisoner (party=party_player, detail=ReleasedByChoice)", logYes);

            var logYesNone = PlayerRescueEvaluator.FormatLog(yesResult, null, "ReleasedByChoice");
            Assert.Equal("player rescue: yes via main party holds prisoner (party=none, detail=ReleasedByChoice)", logYesNone);

            var noResultBandits = new PlayerRescueResult(false, null, "latest capture is not by bandits");
            var logNoBandits = PlayerRescueEvaluator.FormatLog(noResultBandits, "party_1", "ReleasedByChoice");
            Assert.Equal("player rescue: no (latest capture is not by bandits) (party=party_1, detail=ReleasedByChoice)", logNoBandits);

            var noResultParty = new PlayerRescueResult(false, null, "party is another hero's");
            var logNoParty = PlayerRescueEvaluator.FormatLog(noResultParty, "party_lord", "ReleasedByChoice");
            Assert.Equal("player rescue: no (party is another hero's) (party=party_lord, detail=ReleasedByChoice)", logNoParty);

            var noResultDetail = new PlayerRescueResult(false, null, "detail ReleasedAfterBattle from leaderless party");
            var logNoDetail = PlayerRescueEvaluator.FormatLog(noResultDetail, null, "ReleasedAfterBattle");
            Assert.Equal("player rescue: no (detail ReleasedAfterBattle from leaderless party) (party=none, detail=ReleasedAfterBattle)", logNoDetail);
        }

        [Fact]
        public void PlayerRescue_WhenConditionNotMet_ReleaseMappingAndBindingsMatchPriorBehavior()
        {
            // 當條件不符時，走既有分流邏輯：
            // 1. 若無領隊且為 ReleasedAfterBattle，分流至 hero_rescued_from_bandits（NPC 救人）
            var evalNpcRescue = PlayerRescueEvaluator.Evaluate(
                "hero_captured_by_bandits",
                isMainParty: false,
                hasLeaderOrOwner: false,
                EndCaptivityDetail.ReleasedAfterBattle);
            Assert.False(evalNpcRescue.IsPlayerRescue);

            // 2. 若無領隊且為 ReleasedAfterEscape，分流至 hero_escaped_bandits
            var evalEscape = PlayerRescueEvaluator.Evaluate(
                "hero_captured_by_bandits",
                isMainParty: false,
                hasLeaderOrOwner: false,
                EndCaptivityDetail.ReleasedAfterEscape);
            Assert.False(evalEscape.IsPlayerRescue);

            // 3. 原生 RealEventMapping 映射行為保持一致
            Assert.Equal("hero_escaped_captivity", RealEventMapping.TemplateForRelease((int)EndCaptivityDetail.ReleasedAfterEscape));
            Assert.Equal("hero_released", RealEventMapping.TemplateForRelease((int)EndCaptivityDetail.ReleasedByChoice));
            Assert.Equal("hero_released", RealEventMapping.TemplateForRelease((int)EndCaptivityDetail.ReleasedAfterBattle));
            Assert.Equal("hero_released", RealEventMapping.TemplateForRelease((int)EndCaptivityDetail.Ransom));
        }

        #endregion
    }
}
