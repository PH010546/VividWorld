using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using VividWorld.Core.Catalog;
using VividWorld.Core.Config;
using VividWorld.Core.Events;
using VividWorld.Core.Persistence;
using VividWorld.Core.Rumors;
using VividWorld.Core.Situations;
using VividWorld.Core.Util;
using Xunit;

namespace VividWorld.Core.Tests
{
    public class SituationTests
    {
        private readonly PersistenceConfig _defaultPersistence = new() { MaxFactsPerEvent = 24 };

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

        private const string ValidSituationJson = @"{
  ""situations"": [
    {
      ""id"": ""test_dispute"",
      ""trigger"": ""direct"",
      ""decider"": ""slighted"",
      ""minBranchWeight"": 0.1,
      ""roles"": {
        ""slighted"": { },
        ""favored"":  { },
        ""host"":     { ""derived"": ""settlementOwnerClanLeader"", ""optional"": true }
      },
      ""conditions"": [
        { ""type"": ""sameSettlement"", ""roles"": [""slighted"", ""favored""], ""kinds"": [""town"", ""castle""] },
        { ""type"": ""differentClan"", ""a"": ""slighted"", ""b"": ""favored"" },
        { ""type"": ""sameKingdom"", ""a"": ""slighted"", ""b"": ""favored"" },
        { ""type"": ""isClanLeader"", ""role"": ""slighted"", ""value"": true },
        { ""type"": ""isClanLeader"", ""role"": ""favored"", ""value"": false },
        { ""type"": ""clanTierCompare"", ""a"": ""favored"", ""op"": "">"", ""b"": ""slighted"" }
      ],
      ""branches"": [
        {
          ""id"": ""demand_seat"",
          ""base"": 1.0,
          ""traits"": { ""valor"": 0.6, ""honor"": 0.8 },
          ""preconditions"": [],
          ""events"": [
            { ""type"": ""seat_dispute_demanded"", ""bind"": { ""SLIGHTED"": ""slighted"", ""FAVORED"": ""favored"", ""SETTLEMENT"": ""@settlement"" } }
          ]
        },
        {
          ""id"": ""walk_out"",
          ""base"": 0.5,
          ""traits"": { ""honor"": 0.6 },
          ""preconditions"": [ { ""type"": ""roleBound"", ""role"": ""host"" } ],
          ""events"": [
            { ""type"": ""seat_dispute_walked_out"", ""bind"": { ""SLIGHTED"": ""slighted"", ""FAVORED"": ""favored"", ""HOST"": ""host"", ""SETTLEMENT"": ""@settlement"" } }
          ]
        }
      ]
    }
  ]
}";

        // ============================================================
        // 1. Loading & Validation (>= 10 tests)
        // ============================================================

        [Fact]
        public void Load_ValidTemplate_ReturnsNoIssues()
        {
            var catalog = SituationCatalogLoader.Load(ValidSituationJson);
            Assert.Single(catalog.Situations);
            Assert.Equal(0, catalog.SkippedCount);
            Assert.Empty(catalog.Issues.Where(i => i.IsError));
        }

        [Fact]
        public void Validate_MissingId_ReturnsError()
        {
            string json = @"{ ""situations"": [ { ""trigger"": ""direct"", ""decider"": ""a"", ""roles"": { ""a"": {}, ""b"": {} } } ] }";
            var catalog = SituationCatalogLoader.Load(json);
            Assert.Empty(catalog.Situations);
            Assert.Equal(1, catalog.SkippedCount);
            Assert.Contains(catalog.Issues, i => i.Code == SituationIssueCode.MissingId && i.IsError);
        }

        [Fact]
        public void Validate_DuplicateId_ReturnsError()
        {
            string json = @"{
  ""situations"": [
    { ""id"": ""dup"", ""trigger"": ""direct"", ""decider"": ""a"", ""roles"": { ""a"": {}, ""b"": {} }, ""branches"": [ { ""id"": ""b1"", ""base"": 1.0, ""events"": [{ ""type"": ""e1"", ""bind"": { ""A"": ""a"" } }] } ] },
    { ""id"": ""dup"", ""trigger"": ""direct"", ""decider"": ""a"", ""roles"": { ""a"": {}, ""b"": {} }, ""branches"": [ { ""id"": ""b1"", ""base"": 1.0, ""events"": [{ ""type"": ""e1"", ""bind"": { ""A"": ""a"" } }] } ] }
  ]
}";
            var catalog = SituationCatalogLoader.Load(json);
            Assert.Single(catalog.Situations);
            Assert.Equal(1, catalog.SkippedCount);
            Assert.Contains(catalog.Issues, i => i.Code == SituationIssueCode.DuplicateId && i.IsError);
        }

        [Fact]
        public void Validate_NonDirectTrigger_ReturnsError()
        {
            string json = @"{
  ""situations"": [
    { ""id"": ""s1"", ""trigger"": ""indirect"", ""decider"": ""a"", ""roles"": { ""a"": {}, ""b"": {} }, ""branches"": [ { ""id"": ""b1"", ""base"": 1.0, ""events"": [{ ""type"": ""e1"", ""bind"": { ""A"": ""a"" } }] } ] }
  ]
}";
            var catalog = SituationCatalogLoader.Load(json);
            Assert.Empty(catalog.Situations);
            Assert.Contains(catalog.Issues, i => i.Code == SituationIssueCode.BadTrigger && i.IsError);
        }

        [Fact]
        public void Validate_DeciderNotInRoles_ReturnsError()
        {
            string json = @"{
  ""situations"": [
    { ""id"": ""s1"", ""trigger"": ""direct"", ""decider"": ""c"", ""roles"": { ""a"": {}, ""b"": {} }, ""branches"": [ { ""id"": ""b1"", ""base"": 1.0, ""events"": [{ ""type"": ""e1"", ""bind"": { ""A"": ""a"" } }] } ] }
  ]
}";
            var catalog = SituationCatalogLoader.Load(json);
            Assert.Empty(catalog.Situations);
            Assert.Contains(catalog.Issues, i => i.Code == SituationIssueCode.MissingDecider && i.IsError);
        }

        [Fact]
        public void Validate_DeciderIsDerived_ReturnsError()
        {
            string json = @"{
  ""situations"": [
    { ""id"": ""s1"", ""trigger"": ""direct"", ""decider"": ""host"", ""roles"": { ""a"": {}, ""b"": {}, ""host"": { ""derived"": ""settlementOwnerClanLeader"" } }, ""branches"": [ { ""id"": ""b1"", ""base"": 1.0, ""events"": [{ ""type"": ""e1"", ""bind"": { ""A"": ""a"" } }] } ] }
  ]
}";
            var catalog = SituationCatalogLoader.Load(json);
            Assert.Empty(catalog.Situations);
            Assert.Contains(catalog.Issues, i => i.Code == SituationIssueCode.DeciderIsDerived && i.IsError);
        }

        [Fact]
        public void Validate_FewerThanTwoNonDerivedRoles_ReturnsError()
        {
            string json = @"{
  ""situations"": [
    { ""id"": ""s1"", ""trigger"": ""direct"", ""decider"": ""a"", ""roles"": { ""a"": {}, ""host"": { ""derived"": ""settlementOwnerClanLeader"" } }, ""branches"": [ { ""id"": ""b1"", ""base"": 1.0, ""events"": [{ ""type"": ""e1"", ""bind"": { ""A"": ""a"" } }] } ] }
  ]
}";
            var catalog = SituationCatalogLoader.Load(json);
            Assert.Empty(catalog.Situations);
            Assert.Contains(catalog.Issues, i => i.Code == SituationIssueCode.InsufficientRoles && i.IsError);
        }

        [Fact]
        public void Validate_UnknownConditionType_ReturnsErrorAndNotLoaded()
        {
            string json = @"{
  ""situations"": [
    {
      ""id"": ""s1"", ""trigger"": ""direct"", ""decider"": ""a"",
      ""roles"": { ""a"": {}, ""b"": {} },
      ""conditions"": [ { ""type"": ""magicalForce"" } ],
      ""branches"": [ { ""id"": ""b1"", ""base"": 1.0, ""events"": [{ ""type"": ""e1"", ""bind"": { ""A"": ""a"" } }] } ]
    }
  ]
}";
            var catalog = SituationCatalogLoader.Load(json);
            Assert.Empty(catalog.Situations);
            Assert.Contains(catalog.Issues, i => i.Code == SituationIssueCode.UnknownConditionType && i.IsError);
        }

        [Fact]
        public void Validate_ConditionReferencesUnknownRole_ReturnsError()
        {
            string json = @"{
  ""situations"": [
    {
      ""id"": ""s1"", ""trigger"": ""direct"", ""decider"": ""a"",
      ""roles"": { ""a"": {}, ""b"": {} },
      ""conditions"": [ { ""type"": ""isClanLeader"", ""role"": ""ghost"", ""value"": true } ],
      ""branches"": [ { ""id"": ""b1"", ""base"": 1.0, ""events"": [{ ""type"": ""e1"", ""bind"": { ""A"": ""a"" } }] } ]
    }
  ]
}";
            var catalog = SituationCatalogLoader.Load(json);
            Assert.Empty(catalog.Situations);
            Assert.Contains(catalog.Issues, i => i.Code == SituationIssueCode.UnknownRoleInCondition && i.IsError);
        }

        [Fact]
        public void Validate_UnknownTraitName_ReturnsError()
        {
            string json = @"{
  ""situations"": [
    {
      ""id"": ""s1"", ""trigger"": ""direct"", ""decider"": ""a"",
      ""roles"": { ""a"": {}, ""b"": {} },
      ""branches"": [
        { ""id"": ""b1"", ""base"": 1.0, ""traits"": { ""magic"": 1.0 }, ""events"": [{ ""type"": ""e1"", ""bind"": { ""A"": ""a"" } }] }
      ]
    }
  ]
}";
            var catalog = SituationCatalogLoader.Load(json);
            Assert.Empty(catalog.Situations);
            Assert.Contains(catalog.Issues, i => i.Code == SituationIssueCode.UnknownTraitName && i.IsError);
        }

        [Fact]
        public void Validate_InvalidClanTierOp_ReturnsError()
        {
            string json = @"{
  ""situations"": [
    {
      ""id"": ""s1"", ""trigger"": ""direct"", ""decider"": ""a"",
      ""roles"": { ""a"": {}, ""b"": {} },
      ""conditions"": [ { ""type"": ""clanTierCompare"", ""a"": ""a"", ""op"": ""=~="", ""b"": ""b"" } ],
      ""branches"": [ { ""id"": ""b1"", ""base"": 1.0, ""events"": [{ ""type"": ""e1"", ""bind"": { ""A"": ""a"" } }] } ]
    }
  ]
}";
            var catalog = SituationCatalogLoader.Load(json);
            Assert.Empty(catalog.Situations);
            Assert.Contains(catalog.Issues, i => i.Code == SituationIssueCode.InvalidConditionOp && i.IsError);
        }

        [Fact]
        public void Validate_BranchWithoutEvents_ReturnsError()
        {
            string json = @"{
  ""situations"": [
    {
      ""id"": ""s1"", ""trigger"": ""direct"", ""decider"": ""a"",
      ""roles"": { ""a"": {}, ""b"": {} },
      ""branches"": [ { ""id"": ""b1"", ""base"": 1.0, ""events"": [] } ]
    }
  ]
}";
            var catalog = SituationCatalogLoader.Load(json);
            Assert.Empty(catalog.Situations);
            Assert.Contains(catalog.Issues, i => i.Code == SituationIssueCode.NoBranchEvents && i.IsError);
        }

        [Fact]
        public void Validate_UnknownFields_ReturnsWarningAndLoads()
        {
            string json = @"{
  ""extraGlobal"": 123,
  ""situations"": [
    {
      ""id"": ""s1"", ""trigger"": ""direct"", ""decider"": ""a"",
      ""extraSituationField"": ""hello"",
      ""roles"": { ""a"": {}, ""b"": {} },
      ""branches"": [ { ""id"": ""b1"", ""base"": 1.0, ""events"": [{ ""type"": ""e1"", ""bind"": { ""A"": ""a"" } }] } ]
    }
  ]
}";
            var catalog = SituationCatalogLoader.Load(json);
            Assert.Single(catalog.Situations);
            Assert.Contains(catalog.Issues, i => i.Code == SituationIssueCode.UnknownProperty && !i.IsError);
        }

        // ============================================================
        // 2. Cross-check (>= 4 tests)
        // ============================================================

        [Fact]
        public void CrossCheck_EventTypeNotFound_ReturnsError()
        {
            string sitEventsPath = FindRepoFile(Path.Combine("module", "ModuleData", "vividworld_situation_events.json"));
            var sitCatalog = SituationCatalogLoader.Load(ValidSituationJson);
            var sitEventCatalog = EventCatalogLoader.Load(@"{ ""templates"": [] }", _defaultPersistence);
            var mainCatalog = EventCatalogLoader.Load(@"{ ""templates"": [] }", _defaultPersistence);

            var issues = SituationCatalogCrossCheck.Check(sitCatalog, sitEventCatalog, mainCatalog);
            Assert.Contains(issues, i => i.Code == SituationIssueCode.EventTemplateNotFound && i.IsError);
        }

        [Fact]
        public void CrossCheck_EventDuplicateWithMainCatalog_ReturnsError()
        {
            string sitEventsPath = FindRepoFile(Path.Combine("module", "ModuleData", "vividworld_situation_events.json"));
            string sitEventsJson = File.ReadAllText(sitEventsPath);
            var sitCatalog = SituationCatalogLoader.Load(ValidSituationJson);
            var sitEventCatalog = EventCatalogLoader.Load(sitEventsJson, _defaultPersistence);

            // Create main catalog with duplicate type 'seat_dispute_demanded'
            string duplicateMainJson = @"[
  {
    ""type"": ""seat_dispute_demanded"",
    ""origin"": ""public"",
    ""dramaWeight"": 3,
    ""roles"": { ""a"": ""{A}"" },
    ""facts"": [ { ""id"": ""f"", ""category"": ""WHO"", ""text"": ""t"", ""fragility"": 1 } ]
  }
]";
            var mainCatalog = EventCatalogLoader.Load(duplicateMainJson, _defaultPersistence);

            var issues = SituationCatalogCrossCheck.Check(sitCatalog, sitEventCatalog, mainCatalog);
            Assert.Contains(issues, i => i.Code == SituationIssueCode.DuplicateWithMainCatalog && i.IsError);
        }

        [Fact]
        public void CrossCheck_MissingRequiredPlaceholder_ReturnsError()
        {
            string sitEventsPath = FindRepoFile(Path.Combine("module", "ModuleData", "vividworld_situation_events.json"));
            string sitEventsJson = File.ReadAllText(sitEventsPath);
            var sitEventCatalog = EventCatalogLoader.Load(sitEventsJson, _defaultPersistence);
            var mainCatalog = EventCatalogLoader.Load(@"{ ""templates"": [] }", _defaultPersistence);

            // Template misses required placeholder {FAVORED} for seat_dispute_demanded
            var sitTemplate = new SituationTemplate
            {
                Id = "test_dispute",
                Roles = new Dictionary<string, SituationRoleDef> { ["slighted"] = new(), ["favored"] = new() },
                Branches = new List<SituationBranchDef>
                {
                    new()
                    {
                        Id = "b1",
                        Events = new List<SituationBranchEventDef>
                        {
                            new()
                            {
                                Type = "seat_dispute_demanded",
                                Bind = new Dictionary<string, string> { ["SLIGHTED"] = "slighted" } // misses FAVORED
                            }
                        }
                    }
                }
            };
            var sitCatalog = new SituationCatalog(new[] { sitTemplate }, Array.Empty<SituationIssue>(), 0);

            var issues = SituationCatalogCrossCheck.Check(sitCatalog, sitEventCatalog, mainCatalog);
            Assert.Contains(issues, i => i.Code == SituationIssueCode.UnboundNonOptionalPlaceholder && i.IsError);
        }

        [Fact]
        public void CrossCheck_InvalidBindValue_ReturnsError()
        {
            string sitEventsPath = FindRepoFile(Path.Combine("module", "ModuleData", "vividworld_situation_events.json"));
            string sitEventsJson = File.ReadAllText(sitEventsPath);
            var sitEventCatalog = EventCatalogLoader.Load(sitEventsJson, _defaultPersistence);
            var mainCatalog = EventCatalogLoader.Load(@"{ ""templates"": [] }", _defaultPersistence);

            // Bind value 'ghost_role' is not in roles and not @settlement
            var sitTemplate = new SituationTemplate
            {
                Id = "test_dispute",
                Roles = new Dictionary<string, SituationRoleDef> { ["slighted"] = new(), ["favored"] = new() },
                Branches = new List<SituationBranchDef>
                {
                    new()
                    {
                        Id = "b1",
                        Events = new List<SituationBranchEventDef>
                        {
                            new()
                            {
                                Type = "seat_dispute_demanded",
                                Bind = new Dictionary<string, string>
                                {
                                    ["SLIGHTED"] = "slighted",
                                    ["FAVORED"] = "ghost_role"
                                }
                            }
                        }
                    }
                }
            };
            var sitCatalog = new SituationCatalog(new[] { sitTemplate }, Array.Empty<SituationIssue>(), 0);

            var issues = SituationCatalogCrossCheck.Check(sitCatalog, sitEventCatalog, mainCatalog);
            Assert.Contains(issues, i => i.Code == SituationIssueCode.InvalidBindTarget && i.IsError);
        }

        // ============================================================
        // 3. Conditions (>= 8 tests, exact verbatim detail strings)
        // ============================================================

        [Fact]
        public void Condition_SameSettlement_Success()
        {
            var cond = new SituationConditionDef { Type = "sameSettlement", Roles = new List<string> { "a", "b" }, Kinds = new List<string> { "town", "castle" } };
            var facts = new Dictionary<string, SituationRoleFacts>
            {
                ["a"] = new SituationRoleFacts { SettlementId = "town_V5", SettlementKind = "town" },
                ["b"] = new SituationRoleFacts { SettlementId = "town_V5", SettlementKind = "town" }
            };
            var res = SituationConditionEvaluator.Evaluate(cond, facts);
            Assert.True(res.Ok);
            Assert.Equal("sameSettlement ok (town_V5, town)", res.Detail);
        }

        [Fact]
        public void Condition_SameSettlement_Failure_RoleMissingSettlement_ExactDetail()
        {
            var cond = new SituationConditionDef { Type = "sameSettlement", Roles = new List<string> { "slighted", "favored" }, Kinds = new List<string> { "town", "castle" } };
            var facts = new Dictionary<string, SituationRoleFacts>
            {
                ["slighted"] = new SituationRoleFacts { SettlementId = "town_V5", SettlementKind = "town" },
                ["favored"] = new SituationRoleFacts { SettlementId = null, SettlementKind = null }
            };
            var res = SituationConditionEvaluator.Evaluate(cond, facts);
            Assert.False(res.Ok);
            Assert.Equal("sameSettlement failed (slighted=town_V5, favored=null)", res.Detail);
        }

        [Fact]
        public void Condition_SameSettlement_Failure_WrongKind_ExactDetail()
        {
            var cond = new SituationConditionDef { Type = "sameSettlement", Roles = new List<string> { "slighted", "favored" }, Kinds = new List<string> { "town", "castle" } };
            var facts = new Dictionary<string, SituationRoleFacts>
            {
                ["slighted"] = new SituationRoleFacts { SettlementId = "castle_B2", SettlementKind = "other" },
                ["favored"] = new SituationRoleFacts { SettlementId = "castle_B2", SettlementKind = "other" }
            };
            var res = SituationConditionEvaluator.Evaluate(cond, facts);
            Assert.False(res.Ok);
            Assert.Equal("sameSettlement failed (castle_B2 is other, need town/castle)", res.Detail);
        }

        [Fact]
        public void Condition_DifferentClan_Success()
        {
            var cond = new SituationConditionDef { Type = "differentClan", A = "slighted", B = "favored" };
            var facts = new Dictionary<string, SituationRoleFacts>
            {
                ["slighted"] = new SituationRoleFacts { ClanId = "clan_a" },
                ["favored"] = new SituationRoleFacts { ClanId = "clan_b" }
            };
            var res = SituationConditionEvaluator.Evaluate(cond, facts);
            Assert.True(res.Ok);
            Assert.Equal("differentClan ok (clan_a vs clan_b)", res.Detail);
        }

        [Fact]
        public void Condition_DifferentClan_Failure_BothSameClan_ExactDetail()
        {
            var cond = new SituationConditionDef { Type = "differentClan", A = "slighted", B = "favored" };
            var facts = new Dictionary<string, SituationRoleFacts>
            {
                ["slighted"] = new SituationRoleFacts { ClanId = "clan_a" },
                ["favored"] = new SituationRoleFacts { ClanId = "clan_a" }
            };
            var res = SituationConditionEvaluator.Evaluate(cond, facts);
            Assert.False(res.Ok);
            Assert.Equal("differentClan failed (both clan_a)", res.Detail);
        }

        [Fact]
        public void Condition_DifferentClan_Failure_MissingClan_ExactDetail()
        {
            var cond = new SituationConditionDef { Type = "differentClan", A = "slighted", B = "favored" };
            var facts = new Dictionary<string, SituationRoleFacts>
            {
                ["slighted"] = new SituationRoleFacts { ClanId = "clan_a" },
                ["favored"] = new SituationRoleFacts { ClanId = null }
            };
            var res = SituationConditionEvaluator.Evaluate(cond, facts);
            Assert.False(res.Ok);
            Assert.Equal("differentClan failed (favored has no clan)", res.Detail);
        }

        [Fact]
        public void Condition_SameKingdom_Success()
        {
            var cond = new SituationConditionDef { Type = "sameKingdom", A = "slighted", B = "favored" };
            var facts = new Dictionary<string, SituationRoleFacts>
            {
                ["slighted"] = new SituationRoleFacts { KingdomId = "empire" },
                ["favored"] = new SituationRoleFacts { KingdomId = "empire" }
            };
            var res = SituationConditionEvaluator.Evaluate(cond, facts);
            Assert.True(res.Ok);
            Assert.Equal("sameKingdom ok (empire)", res.Detail);
        }

        [Fact]
        public void Condition_SameKingdom_Failure_DifferentKingdom_ExactDetail()
        {
            var cond = new SituationConditionDef { Type = "sameKingdom", A = "slighted", B = "favored" };
            var facts = new Dictionary<string, SituationRoleFacts>
            {
                ["slighted"] = new SituationRoleFacts { KingdomId = "empire" },
                ["favored"] = new SituationRoleFacts { KingdomId = "vlandia" }
            };
            var res = SituationConditionEvaluator.Evaluate(cond, facts);
            Assert.False(res.Ok);
            Assert.Equal("sameKingdom failed (empire vs vlandia)", res.Detail);
        }

        [Fact]
        public void Condition_SameKingdom_Failure_MissingKingdom_ExactDetail()
        {
            var cond = new SituationConditionDef { Type = "sameKingdom", A = "slighted", B = "favored" };
            var facts = new Dictionary<string, SituationRoleFacts>
            {
                ["slighted"] = new SituationRoleFacts { KingdomId = null },
                ["favored"] = new SituationRoleFacts { KingdomId = "vlandia" }
            };
            var res = SituationConditionEvaluator.Evaluate(cond, facts);
            Assert.False(res.Ok);
            Assert.Equal("sameKingdom failed (slighted has no kingdom)", res.Detail);
        }

        [Fact]
        public void Condition_IsClanLeader_Success()
        {
            var cond = new SituationConditionDef { Type = "isClanLeader", Role = "slighted", Value = true };
            var facts = new Dictionary<string, SituationRoleFacts>
            {
                ["slighted"] = new SituationRoleFacts { IsClanLeader = true }
            };
            var res = SituationConditionEvaluator.Evaluate(cond, facts);
            Assert.True(res.Ok);
            Assert.Equal("isClanLeader(slighted) ok (true)", res.Detail);
        }

        [Fact]
        public void Condition_IsClanLeader_Failure_ExactDetail()
        {
            var cond = new SituationConditionDef { Type = "isClanLeader", Role = "favored", Value = false };
            var facts = new Dictionary<string, SituationRoleFacts>
            {
                ["favored"] = new SituationRoleFacts { IsClanLeader = true }
            };
            var res = SituationConditionEvaluator.Evaluate(cond, facts);
            Assert.False(res.Ok);
            Assert.Equal("isClanLeader(favored) failed (is true, need false)", res.Detail);
        }

        /// <summary>座次之爭同時宣告 isClanLeader(slighted) 與 isClanLeader(favored)。
        /// 聚合鍵要分得開，否則掃描的排除理由那一行會把兩條併成一個數字。</summary>
        [Fact]
        public void Condition_IsClanLeader_LabelCarriesRole()
        {
            var facts = new Dictionary<string, SituationRoleFacts>
            {
                ["slighted"] = new SituationRoleFacts { IsClanLeader = false },
                ["favored"] = new SituationRoleFacts { IsClanLeader = true }
            };

            var slighted = SituationConditionEvaluator.Evaluate(
                new SituationConditionDef { Type = "isClanLeader", Role = "slighted", Value = true }, facts);
            var favored = SituationConditionEvaluator.Evaluate(
                new SituationConditionDef { Type = "isClanLeader", Role = "favored", Value = false }, facts);

            Assert.Equal("isClanLeader", slighted.Type);
            Assert.Equal("isClanLeader", favored.Type);
            Assert.Equal("isClanLeader(slighted)", slighted.Label);
            Assert.Equal("isClanLeader(favored)", favored.Label);
            Assert.NotEqual(slighted.Label, favored.Label);
        }

        /// <summary>其餘條件的聚合鍵就是型別本身，不要平白多括號。</summary>
        [Fact]
        public void Condition_OtherTypes_LabelIsTypeItself()
        {
            var facts = new Dictionary<string, SituationRoleFacts>
            {
                ["favored"] = new SituationRoleFacts { ClanTier = 3 },
                ["slighted"] = new SituationRoleFacts { ClanTier = 5 }
            };

            var res = SituationConditionEvaluator.Evaluate(
                new SituationConditionDef { Type = "clanTierCompare", A = "favored", Op = ">", B = "slighted" }, facts);

            Assert.False(res.Ok);
            Assert.Equal("clanTierCompare", res.Label);
        }

        [Fact]
        public void Condition_ClanTierCompare_Success()
        {
            var cond = new SituationConditionDef { Type = "clanTierCompare", A = "favored", Op = ">", B = "slighted" };
            var facts = new Dictionary<string, SituationRoleFacts>
            {
                ["favored"] = new SituationRoleFacts { ClanTier = 4 },
                ["slighted"] = new SituationRoleFacts { ClanTier = 2 }
            };
            var res = SituationConditionEvaluator.Evaluate(cond, facts);
            Assert.True(res.Ok);
            Assert.Equal("clanTierCompare ok (favored 4 > slighted 2)", res.Detail);
        }

        [Fact]
        public void Condition_ClanTierCompare_Failure_ExactDetail()
        {
            var cond = new SituationConditionDef { Type = "clanTierCompare", A = "favored", Op = ">", B = "slighted" };
            var facts = new Dictionary<string, SituationRoleFacts>
            {
                ["favored"] = new SituationRoleFacts { ClanTier = 2 },
                ["slighted"] = new SituationRoleFacts { ClanTier = 3 }
            };
            var res = SituationConditionEvaluator.Evaluate(cond, facts);
            Assert.False(res.Ok);
            Assert.Equal("clanTierCompare failed (favored 2 > slighted 3)", res.Detail);
        }

        [Fact]
        public void Condition_RoleBound_Success()
        {
            var cond = new SituationConditionDef { Type = "roleBound", Role = "host" };
            var bound = new Dictionary<string, string?> { ["host"] = "lord_1_1" };
            var res = SituationConditionEvaluator.Evaluate(cond, null!, bound, null);
            Assert.True(res.Ok);
            Assert.Equal("roleBound(host) ok (lord_1_1)", res.Detail);
        }

        [Fact]
        public void Condition_RoleBound_Failure_ExactDetail()
        {
            var cond = new SituationConditionDef { Type = "roleBound", Role = "host" };
            var bound = new Dictionary<string, string?> { ["host"] = null };
            var unbound = new Dictionary<string, string> { ["host"] = "owner clan leader lord_4_15 is already slighted" };
            var res = SituationConditionEvaluator.Evaluate(cond, null!, bound, unbound);
            Assert.False(res.Ok);
            Assert.Equal("roleBound(host) failed (owner clan leader lord_4_15 is already slighted)", res.Detail);
        }

        [Fact]
        public void Condition_AllConditionsEvaluated_NoShortCircuit()
        {
            var conditions = new List<SituationConditionDef>
            {
                new() { Type = "isClanLeader", Role = "a", Value = true },
                new() { Type = "isClanLeader", Role = "b", Value = true },
                new() { Type = "isClanLeader", Role = "c", Value = true }
            };
            var facts = new Dictionary<string, SituationRoleFacts>
            {
                ["a"] = new SituationRoleFacts { IsClanLeader = false }, // fails
                ["b"] = new SituationRoleFacts { IsClanLeader = true },  // ok
                ["c"] = new SituationRoleFacts { IsClanLeader = false }  // fails
            };
            var results = SituationConditionEvaluator.EvaluateAll(conditions, facts);
            Assert.Equal(3, results.Count);
            Assert.False(results[0].Ok);
            Assert.True(results[1].Ok);
            Assert.False(results[2].Ok);
        }

        // ============================================================
        // 4. Branch Selection (>= 5 tests)
        // ============================================================

        [Fact]
        public void BranchSelection_WeightBreakdown_ExactFormat()
        {
            var template = new SituationTemplate
            {
                Id = "seat_dispute",
                Branches = new List<SituationBranchDef>
                {
                    new()
                    {
                        Id = "demand_seat",
                        Base = 1.0,
                        Traits = new Dictionary<string, double> { ["valor"] = 0.6, ["honor"] = 0.8 }
                    },
                    new()
                    {
                        Id = "endure",
                        Base = 1.0,
                        Traits = new Dictionary<string, double> { ["calculating"] = 0.8, ["mercy"] = -0.5 }
                    },
                    new()
                    {
                        Id = "yield",
                        Base = 1.0,
                        Traits = new Dictionary<string, double> { ["generosity"] = 0.7, ["mercy"] = 0.5 }
                    },
                    new()
                    {
                        Id = "walk_out",
                        Base = 0.5,
                        Traits = new Dictionary<string, double> { ["honor"] = 0.6, ["calculating"] = -0.6 },
                        Preconditions = new List<SituationConditionDef>
                        {
                            new() { Type = "roleBound", Role = "host" }
                        }
                    }
                }
            };

            var deciderTraits = new TraitProfile { Honor = 2, Valor = 1, Calculating = 0, Mercy = -1, Generosity = -2 };
            var bound = new Dictionary<string, string?> { ["host"] = null };
            var unbound = new Dictionary<string, string> { ["host"] = "owner clan leader lord_4_15 is already slighted" };
            var rng = new SplitMix64Rng();

            var decision = SituationBranchSelector.Select(
                template,
                factsByRole: null,
                boundHeroes: bound,
                unboundReasons: unbound,
                deciderTraits: deciderTraits,
                defaultMinBranchWeight: 0.1,
                rng: rng,
                seed: 42);

            Assert.NotNull(decision);
            Assert.Equal(4, decision.Branches.Count);

            // Verify branch 0: 1.0 + 1 * 0.60 + 2 * 0.80 = 3.20
            var b0 = decision.Branches[0];
            Assert.Equal("demand_seat", b0.BranchId);
            Assert.Equal(3.20, Math.Round(b0.Raw, 2));
            Assert.False(b0.IsClamped);

            // Verify branch 1: 1.0 + 0 * 0.80 + (-1) * (-0.50) = 1.50
            var b1 = decision.Branches[1];
            Assert.Equal("endure", b1.BranchId);
            Assert.Equal(1.50, Math.Round(b1.Raw, 2));

            // Verify branch 2: 1.0 + (-2) * 0.70 + (-1) * 0.50 = -0.90 -> floor 0.10
            var b2 = decision.Branches[2];
            Assert.Equal("yield", b2.BranchId);
            Assert.Equal(-0.90, Math.Round(b2.Raw, 2));
            Assert.True(b2.IsClamped);
            Assert.Equal(0.10, Math.Round(b2.Weight, 2));

            // Verify branch 3: excluded
            var b3 = decision.Branches[3];
            Assert.Equal("walk_out", b3.BranchId);
            Assert.True(b3.IsExcluded);
            Assert.Equal("roleBound(host) failed (owner clan leader lord_4_15 is already slighted)", b3.ExcludedReason);

            // Format check
            var logLines = SituationLogFormatter.FormatExecution(
                "seat_dispute",
                1234567890L,
                new Dictionary<string, string?> { ["slighted"] = "lord_4_15", ["favored"] = "lord_1_22", ["host"] = "lord_1_1" },
                "town_V5",
                new List<ConditionEvaluationResult> { new() { Ok = true, Detail = "sameSettlement ok (town_V5, town)" } },
                decision);

            Assert.Contains(logLines, l => l.Trim() == "branch demand_seat: base 1.00 + valor 1 x 0.60 + honor 2 x 0.80 = 3.20");
            Assert.Contains(logLines, l => l.Trim() == "branch endure: base 1.00 + calculating 0 x 0.80 + mercy -1 x -0.50 = 1.50");
            Assert.Contains(logLines, l => l.Trim() == "branch yield: base 1.00 + generosity -2 x 0.70 + mercy -1 x 0.50 = -0.90 -> floor 0.10");
            Assert.Contains(logLines, l => l.Trim() == "branch walk_out: excluded - roleBound(host) failed (owner clan leader lord_4_15 is already slighted)");
        }

        [Fact]
        public void BranchSelection_ClampedToMinBranchWeight()
        {
            var template = new SituationTemplate
            {
                Id = "s1",
                MinBranchWeight = 0.25,
                Branches = new List<SituationBranchDef>
                {
                    new() { Id = "b1", Base = -5.0 }
                }
            };
            var rng = new SplitMix64Rng();
            var decision = SituationBranchSelector.Select(
                template, null, null, null,
                new TraitProfile(), 0.1, rng, 123);

            Assert.Single(decision.Branches);
            Assert.True(decision.Branches[0].IsClamped);
            Assert.Equal(0.25, decision.Branches[0].Weight);
        }

        [Fact]
        public void BranchSelection_PreconditionFailed_ExcludedWithExactReason()
        {
            var template = new SituationTemplate
            {
                Id = "s1",
                Branches = new List<SituationBranchDef>
                {
                    new()
                    {
                        Id = "b1",
                        Base = 1.0,
                        Preconditions = new List<SituationConditionDef>
                        {
                            new() { Type = "roleBound", Role = "host" }
                        }
                    }
                }
            };
            var unbound = new Dictionary<string, string> { ["host"] = "settlement has no owner clan" };
            var decision = SituationBranchSelector.Select(
                template, null, null, unbound,
                new TraitProfile(), 0.1, new SplitMix64Rng(), 123);

            Assert.Single(decision.Branches);
            Assert.True(decision.Branches[0].IsExcluded);
            Assert.Equal("roleBound(host) failed (settlement has no owner clan)", decision.Branches[0].ExcludedReason);
            Assert.Null(decision.SelectedBranchId);
        }

        [Fact]
        public void BranchSelection_AllExcluded_ReturnsNoBranch()
        {
            var template = new SituationTemplate
            {
                Id = "s1",
                Branches = new List<SituationBranchDef>
                {
                    new()
                    {
                        Id = "b1",
                        Preconditions = new List<SituationConditionDef> { new() { Type = "roleBound", Role = "host" } }
                    }
                }
            };
            var decision = SituationBranchSelector.Select(
                template, null, null, null,
                new TraitProfile(), 0.1, new SplitMix64Rng(), 123);

            Assert.Null(decision.SelectedBranchId);
            Assert.Null(decision.SelectedBranch);
            var lines = SituationLogFormatter.FormatExecution("s1", 1, null!, "town_1", null!, decision);
            Assert.Contains(lines, l => l.Trim() == "pick: no branch available");
        }

        [Fact]
        public void BranchSelection_Determinism_SameSeedSamePick()
        {
            var template = new SituationTemplate
            {
                Id = "s1",
                Branches = new List<SituationBranchDef>
                {
                    new() { Id = "b1", Base = 1.0 },
                    new() { Id = "b2", Base = 1.0 }
                }
            };
            var rng1 = new SplitMix64Rng();
            var rng2 = new SplitMix64Rng();
            long seed = 987654321L;

            var d1 = SituationBranchSelector.Select(template, null, null, null, new TraitProfile(), 0.1, rng1, seed);
            var d2 = SituationBranchSelector.Select(template, null, null, null, new TraitProfile(), 0.1, rng2, seed);

            Assert.Equal(d1.SelectedBranchId, d2.SelectedBranchId);
            Assert.Equal(d1.Roll, d2.Roll);
        }

        [Fact]
        public void BranchSelection_DeciderSensitivity_DifferentTraitsDifferentPicks()
        {
            var template = new SituationTemplate
            {
                Id = "s1",
                Branches = new List<SituationBranchDef>
                {
                    new() { Id = "peaceful", Base = 1.0, Traits = new Dictionary<string, double> { ["mercy"] = 5.0 } },
                    new() { Id = "violent", Base = 1.0, Traits = new Dictionary<string, double> { ["mercy"] = -5.0 } }
                }
            };
            var deciderMerciful = new TraitProfile { Mercy = 2 };
            var deciderCruel = new TraitProfile { Mercy = -2 };
            var rng = new SplitMix64Rng();
            long seed = 42L;

            var d1 = SituationBranchSelector.Select(template, null, null, null, deciderMerciful, 0.1, rng, seed);
            var d2 = SituationBranchSelector.Select(template, null, null, null, deciderCruel, 0.1, rng, seed);

            Assert.Equal("peaceful", d1.SelectedBranchId);
            Assert.Equal("violent", d2.SelectedBranchId);
        }

        // ============================================================
        // 5. Seeds (>= 2 tests)
        // ============================================================

        [Fact]
        public void SituationSeed_InstanceId_OrderIndependent()
        {
            long campaignSeed = 12345L;
            string templateId = "seat_dispute";
            double day = 10.5;

            var roles1 = new Dictionary<string, string?>
            {
                ["slighted"] = "lord_1",
                ["favored"] = "lord_2",
                ["host"] = "lord_3"
            };

            var roles2 = new Dictionary<string, string?>
            {
                ["host"] = "lord_3",
                ["favored"] = "lord_2",
                ["slighted"] = "lord_1"
            };

            long id1 = SituationSeed.InstanceId(campaignSeed, templateId, day, roles1);
            long id2 = SituationSeed.InstanceId(campaignSeed, templateId, day, roles2);

            Assert.Equal(id1, id2);
        }

        [Fact]
        public void SituationSeed_BranchSeed_DayBucketChangesDayToDay()
        {
            long campaignSeed = 12345L;
            string templateId = "seat_dispute";
            var roles = new Dictionary<string, string?> { ["slighted"] = "lord_1", ["favored"] = "lord_2" };

            long instDay1 = SituationSeed.InstanceId(campaignSeed, templateId, 1.2, roles);
            long instDay2 = SituationSeed.InstanceId(campaignSeed, templateId, 2.2, roles);

            Assert.NotEqual(instDay1, instDay2);

            long branchSeed1 = SituationSeed.BranchSeed(campaignSeed, instDay1, "lord_1");
            long branchSeed2 = SituationSeed.BranchSeed(campaignSeed, instDay2, "lord_1");

            Assert.NotEqual(branchSeed1, branchSeed2);
        }

        // ============================================================
        // 6. Real Files (>= 2 tests)
        // ============================================================

        [Fact]
        public void RealSituationFiles_LoadAndCrossCheck_ZeroErrors()
        {
            string sitPath = FindRepoFile(Path.Combine("module", "ModuleData", "vividworld_situations.json"));
            string sitEventsPath = FindRepoFile(Path.Combine("module", "ModuleData", "vividworld_situation_events.json"));
            string mainEventsPath = FindRepoFile(Path.Combine("module", "ModuleData", "vividworld_events.json"));

            string sitJson = File.ReadAllText(sitPath);
            string sitEventsJson = File.ReadAllText(sitEventsPath);
            string mainEventsJson = File.ReadAllText(mainEventsPath);

            var sitCatalog = SituationCatalogLoader.Load(sitJson);
            Assert.NotEmpty(sitCatalog.Situations);
            Assert.Empty(sitCatalog.Issues.Where(i => i.IsError));

            var sitEventCatalog = EventCatalogLoader.Load(sitEventsJson, _defaultPersistence);
            Assert.NotEmpty(sitEventCatalog.Templates);
            Assert.Empty(sitEventCatalog.Issues.Where(i => i.IsError));

            var mainCatalog = EventCatalogLoader.Load(mainEventsJson, _defaultPersistence);

            var crossIssues = SituationCatalogCrossCheck.Check(sitCatalog, sitEventCatalog, mainCatalog);
            Assert.Empty(crossIssues.Where(i => i.IsError));
        }

        [Fact]
        public void RealSituationFiles_AllBranchesBindSuccessfully()
        {
            string sitPath = FindRepoFile(Path.Combine("module", "ModuleData", "vividworld_situations.json"));
            string sitEventsPath = FindRepoFile(Path.Combine("module", "ModuleData", "vividworld_situation_events.json"));

            var sitCatalog = SituationCatalogLoader.Load(File.ReadAllText(sitPath));
            var sitEventCatalog = EventCatalogLoader.Load(File.ReadAllText(sitEventsPath), _defaultPersistence);

            var dispute = sitCatalog.ById("seat_dispute");
            Assert.NotNull(dispute);
            Assert.Equal(4, dispute!.Branches.Count);

            var dummyBindings = new Dictionary<string, string>
            {
                ["SLIGHTED"] = "lord_1",
                ["FAVORED"] = "lord_2",
                ["HOST"] = "lord_3",
                ["SETTLEMENT"] = "town_V5"
            };

            foreach (var branch in dispute.Branches)
            {
                Assert.NotEmpty(branch.Events);
                foreach (var evtRef in branch.Events)
                {
                    var tmpl = sitEventCatalog.ByType(evtRef.Type);
                    Assert.NotNull(tmpl);

                    var submission = TemplateBinder.Bind(tmpl!, dummyBindings, 10.0, null, out var issues);
                    Assert.NotNull(submission);
                    Assert.Empty(issues.Where(i => i.IsError));
                }
            }
        }

        // ============================================================
        // 7. Config Merge & Normalize (>= 2 tests)
        // ============================================================

        [Fact]
        public void ConfigMerge_AddsMissingSituationsKeys()
        {
            string existingJson = @"{ ""enabled"": true, ""core"": { ""eventsPerHourlyTick"": 4 } }";
            var canonicalConfig = new VividWorldConfig();
            var canonicalJson = VividJson.Write(canonicalConfig);
            var userObj = JObject.Parse(existingJson);
            var defaultObj = JObject.Parse(canonicalJson);

            var result = ConfigMerge.AddMissingKeys(userObj, defaultObj);
            Assert.NotEmpty(result.AddedPaths);
            Assert.NotNull(result.Merged["situations"]);
            Assert.NotNull(result.Merged["situations"]?["minBranchWeight"]);
            Assert.NotNull(result.Merged["situations"]?["maxPerDay"]);
            Assert.NotNull(result.Merged["situations"]?["contest"]);
        }

        [Fact]
        public void ConfigNormalize_ClampsSituationsValues()
        {
            var config = new VividWorldConfig
            {
                Situations = new SituationsConfig
                {
                    MinBranchWeight = -0.5,
                    MaxPerDay = -5.0,
                    ClanEscalationFactor = 5.0,
                    ClanEscalationThreshold = -50,
                    MaxPending = -10,
                    MaxPendingChars = 50000,
                    Contest = new SituationContestConfig
                    {
                        MaxWitnessRollers = -1
                    }
                }
            };

            var notices = new List<ClampNotice>();
            config.Normalize(notices);
            Assert.NotEmpty(notices);
            Assert.Equal(0.0, config.Situations.MinBranchWeight);
            Assert.Equal(0.0, config.Situations.MaxPerDay);
            Assert.Equal(1.0, config.Situations.ClanEscalationFactor);
            Assert.Equal(0, config.Situations.ClanEscalationThreshold);
            Assert.Equal(0, config.Situations.MaxPending);
            Assert.Equal(30000, config.Situations.MaxPendingChars);
            Assert.Equal(0, config.Situations.Contest.MaxWitnessRollers);
        }

        // ============================================================
        // 8. 出貨情境的內容不變式（新增情境時會先在這裡紅掉）
        // ============================================================

        /// <summary>每日掃描只處理**剛好兩個**非 derived 角色的情境（SituationScanBehavior：
        /// `nonDerivedRoles.Count != 2` 就 continue）。目錄載入器只要求「至少兩個」，
        /// 所以三個角色的情境會**載入成功卻永遠不被掃到**——沉默失敗，只能靠這條測試擋。</summary>
        [Fact]
        public void ShippedSituations_EveryNonDevSituation_HasExactlyTwoNonDerivedRoles()
        {
            string sitPath = FindRepoFile(Path.Combine("module", "ModuleData", "vividworld_situations.json"));
            var catalog = SituationCatalogLoader.Load(File.ReadAllText(sitPath));
            Assert.Empty(catalog.Issues.Where(i => i.IsError));

            foreach (var situation in catalog.Situations.Where(s => !s.DevOnly))
            {
                int nonDerived = situation.Roles.Count(r => !r.Value.IsDerived);
                Assert.True(nonDerived == 2,
                    $"Situation '{situation.Id}' has {nonDerived} non-derived role(s); the daily scan only pairs situations with exactly 2.");
            }
        }

        /// <summary>每一個出貨情境的每一條分支都要綁得起來，不只 seat_dispute。</summary>
        [Fact]
        public void ShippedSituations_EveryBranchOfEverySituation_BindsWithoutErrors()
        {
            string sitPath = FindRepoFile(Path.Combine("module", "ModuleData", "vividworld_situations.json"));
            string sitEventsPath = FindRepoFile(Path.Combine("module", "ModuleData", "vividworld_situation_events.json"));

            var catalog = SituationCatalogLoader.Load(File.ReadAllText(sitPath));
            var eventCatalog = EventCatalogLoader.Load(File.ReadAllText(sitEventsPath), _defaultPersistence);

            int branchesChecked = 0;

            foreach (var situation in catalog.Situations)
            {
                foreach (var branch in situation.Branches)
                {
                    Assert.NotEmpty(branch.Events);
                    branchesChecked++;

                    foreach (var evtRef in branch.Events)
                    {
                        var template = eventCatalog.ByType(evtRef.Type);
                        Assert.True(template != null,
                            $"Situation '{situation.Id}' branch '{branch.Id}' refers to unknown event template '{evtRef.Type}'.");

                        var bindings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                        foreach (var key in evtRef.Bind.Keys)
                        {
                            string placeholder = key.Trim().Trim('{', '}');
                            bindings[placeholder] = string.Equals(placeholder, "SETTLEMENT", StringComparison.OrdinalIgnoreCase)
                                ? "town_dummy"
                                : "hero_" + placeholder.ToLowerInvariant();
                        }

                        var submission = TemplateBinder.Bind(template!, bindings, 10.0, null, out var issues);
                        Assert.True(submission != null,
                            $"Situation '{situation.Id}' branch '{branch.Id}' failed to bind '{evtRef.Type}'.");
                        Assert.Empty(issues.Where(i => i.IsError));
                    }
                }
            }

            Assert.True(branchesChecked >= 19, $"Expected at least 19 shipped branches, saw {branchesChecked}.");
        }
    }
}
