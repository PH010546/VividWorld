using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using VividWorld.Core.Catalog;
using Newtonsoft.Json.Linq;
using VividWorld.Core.Config;
using VividWorld.Core.Events;
using VividWorld.Core.Persistence;
using VividWorld.Core.Rumors;
using VividWorld.Core.Situations;
using VividWorld.Core.Tests.Fakes;
using VividWorld.Core.Util;
using Xunit;

namespace VividWorld.Core.Tests
{
    public class WandererSituationTests
    {
        /// <summary>一個合法的流浪者情境：三個做法（一個沒標的、一個裁決、一個不插手），流浪者 employer 綁玩家。</summary>
        private const string BaseWandererJson = @"{
  ""situations"": [ {
    ""id"": ""w_test"",
    ""trigger"": ""wandererBattle"",
    ""decider"": ""slighted"",
    ""roles"": {
      ""slighted"": { },
      ""favored"": { },
      ""employer"": { ""player"": true }
    },
    ""conditions"": [ ],
    ""player"": { ""judge"": true, ""judgeRole"": ""employer"" },
    ""branches"": [
      { ""id"": ""plain"", ""base"": 1.0, ""events"": [ { ""type"": ""wanderer_plain"", ""bind"": { ""A"": ""slighted"" } } ] },
      { ""id"": ""decide"", ""base"": 1.0, ""judge"": true, ""events"": [ { ""type"": ""wanderer_decide"", ""bind"": { ""A"": ""slighted"" } } ] },
      { ""id"": ""stand"", ""base"": 1.0, ""judge"": true, ""standAside"": true, ""events"": [ ],
        ""grudges"": [ { ""from"": ""slighted"", ""to"": ""employer"", ""amount"": -3 } ] }
    ]
  } ]
}";

        private static JObject Template(JObject root) => (JObject)root["situations"]![0]!;

        /// <summary>讀基底 JSON，讓測試改其中一處後載入。</summary>
        private static SituationCatalog LoadWith(Action<JObject> edit)
        {
            var root = JObject.Parse(BaseWandererJson);
            edit(Template(root));
            return SituationCatalogLoader.Load(root.ToString());
        }

        private static IEnumerable<SituationIssue> Errors(SituationCatalog catalog) => catalog.Issues.Where(i => i.IsError);

        // ---------------------------------------------------------------
        // 情境模板讀取：觸發、noSpread、player 角色、judge 與 standAside、新效果、新條件
        // ---------------------------------------------------------------

        [Fact]
        public void Wanderer_BaseTemplate_LoadsWithoutErrors()
        {
            var catalog = LoadWith(_ => { });
            Assert.Empty(Errors(catalog));
            Assert.Single(catalog.Situations);
            Assert.Equal("wandererBattle", catalog.Situations[0].Trigger);
        }

        [Fact]
        public void Wanderer_WandererDailyTrigger_IsAccepted()
        {
            var catalog = LoadWith(t => t["trigger"] = "wandererDaily");
            Assert.Empty(Errors(catalog));
            Assert.Equal("wandererDaily", catalog.Situations[0].Trigger);
        }

        [Fact]
        public void Wanderer_UnknownTrigger_IsRejectedWithUpdatedMessage()
        {
            var catalog = LoadWith(t => t["trigger"] = "wandererNight");
            var bad = Errors(catalog).Single(i => i.Code == SituationIssueCode.BadTrigger);
            Assert.Contains("wandererBattle", bad.Detail);
            Assert.Contains("wandererDaily", bad.Detail);
            Assert.Empty(catalog.Situations);
        }

        [Fact]
        public void Wanderer_NoSpread_DefaultsToFalse()
        {
            var catalog = LoadWith(_ => { });
            Assert.False(catalog.Situations[0].NoSpread);
        }

        [Fact]
        public void Wanderer_NoSpread_TrueIsRead()
        {
            var catalog = LoadWith(t => t["noSpread"] = true);
            Assert.Empty(Errors(catalog));
            Assert.True(catalog.Situations[0].NoSpread);
        }

        [Fact]
        public void Wanderer_NoSpread_NonBoolIsAnError()
        {
            var catalog = LoadWith(t => t["noSpread"] = "yes");
            Assert.Contains(Errors(catalog), i => i.Code == SituationIssueCode.InvalidSituationField && i.Field == "noSpread");
        }

        [Fact]
        public void Wanderer_PlayerRole_IsRead()
        {
            var catalog = LoadWith(_ => { });
            Assert.True(catalog.Situations[0].Roles["employer"].PlayerOnly);
            Assert.False(catalog.Situations[0].Roles["slighted"].PlayerOnly);
        }

        [Fact]
        public void Wanderer_TwoPlayerRoles_IsAnError()
        {
            var catalog = LoadWith(t => t["roles"]!["favored"] = JObject.Parse(@"{ ""player"": true }"));
            Assert.Contains(Errors(catalog), i => i.Code == SituationIssueCode.InvalidSituationField && i.Field == "roles");
        }

        [Fact]
        public void Wanderer_PlayerOnlyRoleAsDecider_IsAnError()
        {
            var catalog = LoadWith(t => t["decider"] = "employer");
            Assert.Contains(Errors(catalog), i => i.Code == SituationIssueCode.InvalidSituationField && i.Field == "decider");
        }

        [Fact]
        public void Wanderer_PlayerJudge_IsRead_WithJudgeRole()
        {
            var catalog = LoadWith(_ => { });
            var player = catalog.Situations[0].Player;
            Assert.NotNull(player);
            Assert.True(player!.Judge);
            Assert.Equal("employer", player.JudgeRole);
        }

        [Fact]
        public void Wanderer_PlayerJudge_WithoutJudgeRole_IsAnError()
        {
            var catalog = LoadWith(t => t["player"] = JObject.Parse(@"{ ""judge"": true }"));
            Assert.Contains(Errors(catalog), i => i.Code == SituationIssueCode.InvalidPlayerBlock && i.Field == "player.judgeRole");
        }

        [Fact]
        public void Wanderer_PlayerJudge_JudgeRoleMustBeThePlayerRole_IsAnError()
        {
            var catalog = LoadWith(t => t["player"] = JObject.Parse(@"{ ""judge"": true, ""judgeRole"": ""favored"" }"));
            Assert.Contains(Errors(catalog), i => i.Code == SituationIssueCode.InvalidPlayerBlock && i.Field == "player.judgeRole");
        }

        [Fact]
        public void Wanderer_JudgedBranchWithoutPlayerJudge_IsAnError()
        {
            var catalog = LoadWith(t => t["player"] = JObject.Parse(@"{ ""bystander"": true }"));
            Assert.Contains(Errors(catalog), i => i.Code == SituationIssueCode.InvalidWandererTemplate);
        }

        [Fact]
        public void Wanderer_PlayerJudgeWithoutJudgedBranch_IsAnError()
        {
            var catalog = LoadWith(t => t["branches"] = JArray.Parse(
                @"[ { ""id"": ""plain"", ""base"": 1.0, ""events"": [ { ""type"": ""wanderer_plain"", ""bind"": { ""A"": ""slighted"" } } ] } ]"));
            Assert.Contains(Errors(catalog), i => i.Code == SituationIssueCode.InvalidWandererTemplate && i.Field == "player.judge");
        }

        [Fact]
        public void Wanderer_TwoStandAsideBranches_IsAnError()
        {
            var catalog = LoadWith(t => ((JArray)t["branches"]!).Add(JObject.Parse(
                @"{ ""id"": ""stand2"", ""base"": 1.0, ""judge"": true, ""standAside"": true, ""grudges"": [ { ""from"": ""slighted"", ""to"": ""employer"", ""amount"": -3 } ] }")));
            Assert.Contains(Errors(catalog), i => i.Code == SituationIssueCode.InvalidWandererTemplate && i.Field == "branches");
        }

        [Fact]
        public void Wanderer_StandAsideWithoutJudge_IsAnError()
        {
            var catalog = LoadWith(t => ((JObject)((JArray)t["branches"]!)[2]!).Remove("judge"));
            Assert.Contains(Errors(catalog), i => i.Code == SituationIssueCode.InvalidWandererTemplate && i.Field == "branches[2].standAside");
        }

        [Fact]
        public void Wanderer_StandAsideWithEvents_IsAnError()
        {
            var catalog = LoadWith(t => ((JObject)((JArray)t["branches"]!)[2]!)["events"] = JArray.Parse(
                @"[ { ""type"": ""should_not_be_here"", ""bind"": { ""A"": ""slighted"" } } ]"));
            Assert.Contains(Errors(catalog), i => i.Code == SituationIssueCode.InvalidWandererTemplate && i.Field == "branches[2].events");
        }

        [Fact]
        public void Wanderer_StandAsideWithEmptyEvents_IsAccepted()
        {
            var catalog = LoadWith(t => ((JObject)((JArray)t["branches"]!)[2]!)["events"] = new JArray());
            Assert.Empty(Errors(catalog));
        }

        [Fact]
        public void Wanderer_WanderersWithoutAnyNonJudgeBranch_IsAnError()
        {
            var catalog = LoadWith(t => t["branches"] = JArray.Parse(
                @"[ { ""id"": ""decide"", ""base"": 1.0, ""judge"": true, ""events"": [ { ""type"": ""wanderer_decide"", ""bind"": { ""A"": ""slighted"" } } ] } ]"));
            Assert.Contains(Errors(catalog), i => i.Code == SituationIssueCode.InvalidWandererTemplate && i.Field == "branches");
        }

        [Fact]
        public void Wanderer_NonJudgeBranchPlayerEffectsMorale_IsAccepted()
        {
            var catalog = LoadWith(t => ((JObject)((JArray)t["branches"]!)[0]!)["playerEffects"] = JObject.Parse(@"{ ""morale"": 2 }"));
            Assert.Empty(Errors(catalog));
        }

        [Theory]
        [InlineData("renown")]
        [InlineData("influence")]
        [InlineData("traits")]
        public void Wanderer_NonJudgeBranchPlayerEffectsOtherThanMorale_IsAnError(string key)
        {
            var effects = new JObject();
            effects[key] = key == "traits" ? JObject.Parse(@"{ ""honor"": 100 }") : (JToken)1;
            var catalog = LoadWith(t => ((JObject)((JArray)t["branches"]!)[0]!)["playerEffects"] = effects);
            Assert.Contains(Errors(catalog), i => i.Code == SituationIssueCode.InvalidPlayerEffects && i.Field.EndsWith(key, StringComparison.Ordinal));
        }

        [Fact]
        public void Wanderer_SkillXp_IsRead()
        {
            var catalog = LoadWith(t => ((JObject)((JArray)t["branches"]!)[1]!)["skillXp"] = JArray.Parse(
                @"[ { ""role"": ""favored"", ""skill"": ""@taught"", ""amount"": 100 } ]"));
            Assert.Empty(Errors(catalog));
            var xp = catalog.Situations[0].Branches[1].SkillXp!;
            Assert.Single(xp);
            Assert.Equal("favored", xp[0].Role);
            Assert.Equal("@taught", xp[0].Skill);
            Assert.Equal(100, xp[0].Amount);
        }

        [Fact]
        public void Wanderer_SkillXp_UnknownBinding_IsAnError()
        {
            var catalog = LoadWith(t => ((JObject)((JArray)t["branches"]!)[1]!)["skillXp"] = JArray.Parse(
                @"[ { ""role"": ""favored"", ""skill"": ""@other"", ""amount"": 100 } ]"));
            Assert.Contains(Errors(catalog), i => i.Code == SituationIssueCode.InvalidNewEffect);
        }

        [Fact]
        public void Wanderer_PartyRoleChange_IsRead_AndUnknownRoleIsAnError()
        {
            var ok = LoadWith(t => ((JObject)((JArray)t["branches"]!)[1]!)["partyRoleChange"] = JObject.Parse(@"{ ""to"": ""favored"" }"));
            Assert.Empty(Errors(ok));
            Assert.Equal("favored", ok.Situations[0].Branches[1].PartyRoleChange!.To);

            var bad = LoadWith(t => ((JObject)((JArray)t["branches"]!)[1]!)["partyRoleChange"] = JObject.Parse(@"{ ""to"": ""ghost"" }"));
            Assert.Contains(Errors(bad), i => i.Code == SituationIssueCode.InvalidNewEffect);
        }

        [Fact]
        public void Wanderer_Contest_IsRead()
        {
            var catalog = LoadWith(t => ((JObject)((JArray)t["branches"]!)[1]!)["contest"] = JObject.Parse(
                @"{ ""a"": ""slighted"", ""b"": ""favored"", ""skill"": ""@disputedRoleSkill"", ""randomMax"": 60, ""winnerTakesRole"": true }"));
            Assert.Empty(Errors(catalog));
            var c = catalog.Situations[0].Branches[1].Contest!;
            Assert.Equal("slighted", c.A);
            Assert.Equal("favored", c.B);
            Assert.Equal("@disputedRoleSkill", c.Skill);
            Assert.Equal(60, c.RandomMax);
            Assert.True(c.WinnerTakesRole);
        }

        [Fact]
        public void Wanderer_Contest_SameRoleTwice_IsAnError()
        {
            var catalog = LoadWith(t => ((JObject)((JArray)t["branches"]!)[1]!)["contest"] = JObject.Parse(
                @"{ ""a"": ""slighted"", ""b"": ""slighted"", ""skill"": ""@disputedRoleSkill"", ""randomMax"": 60 }"));
            Assert.Contains(Errors(catalog), i => i.Code == SituationIssueCode.InvalidNewEffect);
        }

        [Fact]
        public void Wanderer_Contest_ZeroRandomMax_IsAnError()
        {
            var catalog = LoadWith(t => ((JObject)((JArray)t["branches"]!)[1]!)["contest"] = JObject.Parse(
                @"{ ""a"": ""slighted"", ""b"": ""favored"", ""skill"": ""@disputedRoleSkill"", ""randomMax"": 0 }"));
            Assert.Contains(Errors(catalog), i => i.Code == SituationIssueCode.InvalidNewEffect && i.Field.EndsWith("randomMax", StringComparison.Ordinal));
        }

        [Fact]
        public void Wanderer_GrudgeFromLoserToWinner_IsAcceptedOnlyWithContest()
        {
            var grudge = JArray.Parse(@"[ { ""from"": ""@loser"", ""to"": ""@winner"", ""amount"": -3 } ]");
            var contest = JObject.Parse(
                @"{ ""a"": ""slighted"", ""b"": ""favored"", ""skill"": ""@disputedRoleSkill"", ""randomMax"": 60 }");

            var withContest = LoadWith(t =>
            {
                var b = (JObject)((JArray)t["branches"]!)[1]!;
                b["grudges"] = grudge;
                b["contest"] = contest;
            });
            Assert.Empty(Errors(withContest));

            var withoutContest = LoadWith(t => ((JObject)((JArray)t["branches"]!)[1]!)["grudges"] = grudge);
            Assert.Contains(Errors(withoutContest), i => i.Code == SituationIssueCode.UnknownRoleInGrudge);
        }

        [Fact]
        public void Wanderer_NewConditionKinds_AreParsed_InBattleSituation()
        {
            var conditions = JArray.Parse(@"[
  { ""type"": ""partyRoleHolder"", ""role"": ""favored"" },
  { ""type"": ""teachableSkill"", ""teacher"": ""slighted"", ""learner"": ""favored"" },
  { ""type"": ""skillBelow"", ""a"": ""slighted"", ""b"": ""favored"", ""skill"": ""@disputedRoleSkill"" },
  { ""type"": ""foughtInBattle"", ""roles"": [ ""slighted"", ""favored"" ] },
  { ""type"": ""woundedInBattle"", ""role"": ""favored"" },
  { ""type"": ""battleWon"" }
]");
            var catalog = LoadWith(t => t["conditions"] = conditions);
            Assert.Empty(Errors(catalog));

            var parsed = catalog.Situations[0].Conditions;
            Assert.Equal("favored", parsed[0].Role);
            Assert.Equal("slighted", parsed[1].Teacher);
            Assert.Equal("favored", parsed[1].Learner);
            Assert.Equal("slighted", parsed[2].A);
            Assert.Equal("@disputedRoleSkill", parsed[2].Skill);
            Assert.Equal(new[] { "slighted", "favored" }, parsed[3].Roles);
            Assert.Equal("favored", parsed[4].Role);
        }

        [Theory]
        [InlineData(@"{ ""type"": ""foughtInBattle"", ""roles"": [ ""slighted"" ] }")]
        [InlineData(@"{ ""type"": ""woundedInBattle"", ""role"": ""favored"" }")]
        [InlineData(@"{ ""type"": ""battleWon"" }")]
        public void Wanderer_BattleOnlyCondition_InDailySituation_IsAnError(string conditionJson)
        {
            var catalog = LoadWith(t =>
            {
                t["trigger"] = "wandererDaily";
                t["conditions"] = new JArray(JObject.Parse(conditionJson));
            });
            Assert.Contains(Errors(catalog), i => i.Code == SituationIssueCode.InvalidWandererTemplate && i.Field == "conditions");
        }

        [Fact]
        public void Wanderer_SkillBelowWithUnknownBinding_IsAnError()
        {
            var catalog = LoadWith(t => t["conditions"] = new JArray(JObject.Parse(
                @"{ ""type"": ""skillBelow"", ""a"": ""slighted"", ""b"": ""favored"", ""skill"": ""@something"" }")));
            Assert.Contains(Errors(catalog), i => i.Code == SituationIssueCode.MissingConditionProperty && i.Field.EndsWith(".skill", StringComparison.Ordinal));
        }

        // ---------------------------------------------------------------
        // 不往外傳：講述者排除原因 NoSpread
        // ---------------------------------------------------------------

        private static WorldEvent WitnessedEvent(string? situationId, EventOrigin origin = EventOrigin.Public)
        {
            var evt = new WorldEvent
            {
                EventId = "evt_wanderer",
                Origin = origin,
                Day = 5.0,
                SituationId = situationId
            };
            evt.KnownBy.Add(new KnownByEntry { HeroId = "lord_1", Hop = 0 });
            return evt;
        }

        private static FakeHeroTraitLookup LordTraits()
        {
            var traits = new FakeHeroTraitLookup();
            traits.Set(new TraitProfile { HeroId = "lord_1", IsAlive = true, IsLord = true });
            return traits;
        }

        [Fact]
        public void NoSpread_SituationEvent_IsExcludedFromTelling()
        {
            var reason = TellerEligibility.Check(WitnessedEvent("s_nospread"), "lord_1", 10.0, 3, "player",
                new VividWorldConfig(), LordTraits(), null, id => id == "s_nospread", out _);
            Assert.Equal(TellReason.NoSpread, reason);
        }

        [Fact]
        public void NoSpread_OtherSituation_StillTells()
        {
            var reason = TellerEligibility.Check(WitnessedEvent("s_normal"), "lord_1", 10.0, 3, "player",
                new VividWorldConfig(), LordTraits(), null, id => id == "s_nospread", out _);
            Assert.Equal(TellReason.Ok, reason);
        }

        [Fact]
        public void NoSpread_WithoutCatalog_IsNotExcluded()
        {
            var reason = TellerEligibility.Check(WitnessedEvent("s_nospread"), "lord_1", 10.0, 3, "player",
                new VividWorldConfig(), LordTraits(), null, null, out _);
            Assert.Equal(TellReason.Ok, reason);
        }

        [Fact]
        public void NoSpread_InvisibleEvent_ReportsNotVisibleFirst()
        {
            var evt = WitnessedEvent("s_nospread", EventOrigin.Secret);
            var reason = TellerEligibility.Check(evt, "lord_1", 10.0, 3, "player",
                new VividWorldConfig(), LordTraits(), null, id => id == "s_nospread", out _);
            Assert.Equal(TellReason.NotVisible, reason);
        }

        [Fact]
        public void NoSpread_HasALogText()
        {
            Assert.Contains("no spread", TellerEligibility.TellReasonText(TellReason.NoSpread));
        }

        // ---------------------------------------------------------------
        // 城裡的每日掃描只收 direct
        // ---------------------------------------------------------------

        [Theory]
        [InlineData("direct", true)]
        [InlineData("afterEvent", false)]
        [InlineData("wandererBattle", false)]
        [InlineData("wandererDaily", false)]
        [InlineData("", false)]
        public void CityScan_OnlyTakesDirect(string trigger, bool expected)
        {
            Assert.Equal(expected, CityScanTriggerFilter.IsCityScanTrigger(trigger));
        }

        // ---------------------------------------------------------------
        // 設定鍵：八個鍵、MCM 只露 enabled、合併、夾取
        // ---------------------------------------------------------------

        [Fact]
        public void Wanderer_CatalogLog_OneLinePerTemplate_WithBranchCounts()
        {
            var catalog = LoadWith(t => t["noSpread"] = true);
            var lines = WandererCatalogLogFormatter.FormatTemplates(catalog);
            Assert.Single(lines);
            Assert.Equal("Wanderer situation 'w_test': trigger=wandererBattle noSpread=True judgeBranches=2 plainBranches=1 standAsideBranches=1", lines[0]);
        }

        [Fact]
        public void WandererEvents_Defaults_MatchTheSpec()
        {
            var w = new VividWorldConfig().Situations.WandererEvents;
            Assert.True(w.Enabled);
            Assert.Equal(0.3, w.BattleChance);
            Assert.Equal(0.1, w.DailyChance);
            Assert.Equal(10, w.BattleCooldownDays);
            Assert.Equal(10, w.DailyCooldownDays);
            Assert.Equal(3, w.MinGapDays);
            Assert.Equal(0.5, w.UnseenShare);
            Assert.Equal(30, w.RoleRevertDays);
        }

        [Fact]
        public void WandererEvents_ExistingConfigJson_GetsAllEightKeys_ExistingValuesKept()
        {
            var existing = JObject.Parse(@"{ ""situations"": { ""maxPerDay"": 3.0 } }");
            var canonical = JObject.Parse(VividJson.Write(new VividWorldConfig()));
            var merged = ConfigMerge.AddMissingKeys(existing, canonical);

            var block = (JObject)merged.Merged["situations"]!["wandererEvents"]!;
            Assert.Equal(8, block.Properties().Count());
            Assert.Equal(0.3, (double)block["battleChance"]!);
            Assert.Equal(30, (int)block["roleRevertDays"]!);
            Assert.Contains("situations.wandererEvents.enabled", merged.AddedPaths);
            Assert.Contains("situations.wandererEvents.unseenShare", merged.AddedPaths);
            Assert.Equal(3.0, (double)merged.Merged["situations"]!["maxPerDay"]!);
        }

        [Fact]
        public void WandererEvents_Normalize_ClampsChancesAndReportsIt()
        {
            var cfg = new VividWorldConfig();
            cfg.Situations.WandererEvents.BattleChance = 2.0;
            var notices = new List<ClampNotice>();
            cfg.Normalize(notices);
            Assert.Equal(1.0, cfg.Situations.WandererEvents.BattleChance);
            Assert.Contains(notices, n => n.Key == "situations.wandererEvents.battleChance");
        }

        [Fact]
        public void WandererEvents_OnlyEnabledIsExposedInMcm()
        {
            Assert.Contains(McmExposedKeys.All, k => k.Path == "situations.wandererEvents.enabled" && k.Kind == McmKeyKind.Bool);
            Assert.DoesNotContain(McmExposedKeys.All, k => k.Path == "situations.wandererEvents.battleChance");
            Assert.DoesNotContain(McmExposedKeys.All, k => k.Path == "situations.wandererEvents.roleRevertDays");

            var cfg = new VividWorldConfig();
            Assert.True(McmExposedKeys.Write(cfg, "situations.wandererEvents.enabled", false));
            Assert.Equal(false, McmExposedKeys.Read(cfg, "situations.wandererEvents.enabled"));
            Assert.False(cfg.Situations.WandererEvents.Enabled);
        }

        private static string FindRepoFile(string relativePath)
        {
            string? current = AppContext.BaseDirectory;
            while (!string.IsNullOrEmpty(current))
            {
                if (File.Exists(Path.Combine(current, "VividWorld.sln")))
                {
                    string path = Path.Combine(current, relativePath);
                    if (File.Exists(path)) return path;
                }
                var parent = Directory.GetParent(current);
                if (parent == null) break;
                current = parent.FullName;
            }
            throw new FileNotFoundException($"Could not find '{relativePath}' relative to {AppContext.BaseDirectory}");
        }

        [Fact]
        public void WandererSituations_ShippedFiles_LoadAndCrossCheck_ZeroErrors()
        {
            string sitPath = FindRepoFile(Path.Combine("module", "ModuleData", "vividworld_situations.json"));
            string sitEventsPath = FindRepoFile(Path.Combine("module", "ModuleData", "vividworld_situation_events.json"));
            string mainEventsPath = FindRepoFile(Path.Combine("module", "ModuleData", "vividworld_events.json"));

            string sitJson = File.ReadAllText(sitPath);
            string sitEventsJson = File.ReadAllText(sitEventsPath);
            string mainEventsJson = File.ReadAllText(mainEventsPath);

            var sitCatalog = SituationCatalogLoader.Load(sitJson);
            var sitErrors = sitCatalog.Issues.Where(i => i.IsError).ToList();
            Assert.Empty(sitErrors);

            var persistence = new PersistenceConfig { MaxFactsPerEvent = 24 };
            var sitEventCatalog = EventCatalogLoader.Load(sitEventsJson, persistence);
            var sitEventErrors = sitEventCatalog.Issues.Where(i => i.IsError).ToList();
            Assert.Empty(sitEventErrors);

            var mainCatalog = EventCatalogLoader.Load(mainEventsJson, persistence);

            var crossIssues = SituationCatalogCrossCheck.Check(sitCatalog, sitEventCatalog, mainCatalog);
            var crossErrors = crossIssues.Where(i => i.IsError).ToList();
            Assert.Empty(crossErrors);

            string[] expectedSituations =
            {
                "wanderer_battle_credit",
                "wanderer_battle_rescue",
                "wanderer_party_role",
                "wanderer_campfire_jab",
                "wanderer_teaching"
            };

            foreach (var sitId in expectedSituations)
            {
                var sit = sitCatalog.ById(sitId);
                Assert.NotNull(sit);
                if (sitId == "wanderer_battle_rescue")
                {
                    Assert.False(sit!.NoSpread, $"Situation '{sitId}' must have noSpread: false");
                }
                else
                {
                    Assert.True(sit!.NoSpread, $"Situation '{sitId}' must have noSpread: true");
                }
            }

            var expectedDramaWeights = new Dictionary<string, int>(StringComparer.Ordinal)
            {
                ["wanderer_battle_credit_yielded"] = 2,
                ["wanderer_battle_credit_disputed"] = 3,
                ["wanderer_battle_credit_to_claimant"] = 2,
                ["wanderer_battle_credit_to_rival"] = 2,
                ["wanderer_battle_credit_both"] = 1,
                ["wanderer_battle_credit_rebuked"] = 2,
                ["wanderer_battle_rescued"] = 4,
                ["wanderer_party_role_backed_down"] = 1,
                ["wanderer_party_role_bad_blood"] = 2,
                ["wanderer_party_role_keep_incumbent"] = 1,
                ["wanderer_party_role_swapped"] = 3,
                ["wanderer_party_role_contested"] = 2,
                ["wanderer_campfire_jab_laughed_off"] = 1,
                ["wanderer_campfire_jab_brawled"] = 3,
                ["wanderer_campfire_jab_cold_shoulder"] = 1,
                ["wanderer_campfire_jab_punished"] = 2,
                ["wanderer_campfire_jab_both_rebuked"] = 2,
                ["wanderer_teaching_completed"] = 1,
                ["wanderer_teaching_encouraged"] = 1,
                ["wanderer_teaching_joined"] = 1
            };

            foreach (var kvp in expectedDramaWeights)
            {
                var tmpl = sitEventCatalog.ByType(kvp.Key);
                Assert.NotNull(tmpl);
                Assert.Equal(kvp.Value, tmpl!.DramaWeight);
                Assert.Equal(EventOrigin.Public, tmpl.Origin);

                var facts = tmpl.Facts;
                Assert.NotNull(facts);
                Assert.Contains(facts, f => string.Equals(f.Id, "who", StringComparison.OrdinalIgnoreCase));
                Assert.Contains(facts, f => string.Equals(f.Id, "where", StringComparison.OrdinalIgnoreCase) || f.Category == FactCategory.Where);
            }

            var laughedOff = sitEventCatalog.ByType("wanderer_campfire_jab_laughed_off");
            Assert.NotNull(laughedOff);
            Assert.NotNull(laughedOff!.Feelings);
            Assert.Equal("brash", laughedOff.Feelings!["mocker"]);
            Assert.Equal("decent", laughedOff.Feelings!["mocked"]);

            string enPath = FindRepoFile(Path.Combine("module", "ModuleData", "Languages", "std_module_strings_xml.xml"));
            string cntPath = FindRepoFile(Path.Combine("module", "ModuleData", "Languages", "CNt", "std_module_strings_xml.xml"));

            var enDoc = System.Xml.Linq.XDocument.Load(enPath);
            var cntDoc = System.Xml.Linq.XDocument.Load(cntPath);

            var enStrings = enDoc.Descendants("string").ToDictionary(x => (string)x.Attribute("id")!, x => (string)x.Attribute("text")!);
            var cntStrings = cntDoc.Descendants("string").ToDictionary(x => (string)x.Attribute("id")!, x => (string)x.Attribute("text")!);

            Assert.True(enStrings.ContainsKey("VividWorld_WandererSkillXp_Companion"));
            Assert.True(enStrings.ContainsKey("VividWorld_WandererSkillXp_Player"));
            Assert.True(cntStrings.ContainsKey("VividWorld_WandererSkillXp_Companion"));
            Assert.True(cntStrings.ContainsKey("VividWorld_WandererSkillXp_Player"));

            Assert.Equal("{LEARNER} learned {SKILL_NAME} from {TEACHER}: +{XP} experience", enStrings["VividWorld_WandererSkillXp_Companion"]);
            Assert.Equal("You learned {SKILL_NAME} from {TEACHER}: +{XP} experience", enStrings["VividWorld_WandererSkillXp_Player"]);
            Assert.Equal("{LEARNER}跟著{TEACHER}學{SKILL_NAME}，經驗 +{XP}", cntStrings["VividWorld_WandererSkillXp_Companion"]);
            Assert.Equal("你跟著{TEACHER}學{SKILL_NAME}，經驗 +{XP}", cntStrings["VividWorld_WandererSkillXp_Player"]);
        }
    }
}
