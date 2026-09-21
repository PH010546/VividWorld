using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using VividWorld.Core.Catalog;
using VividWorld.Core.Config;
using VividWorld.Core.Events;
using VividWorld.Core.Ingest;
using VividWorld.Core.Persistence;
using Xunit;

namespace VividWorld.Core.Tests
{
    public class EventCatalogTests
    {
        private readonly PersistenceConfig _defaultConfig = new() { MaxFactsPerEvent = 24 };

        private const string ValidSingleTemplateJson = @"[
  {
    ""type"": ""sample_event"",
    ""origin"": ""public"",
    ""dramaWeight"": 3,
    ""roles"": {
      ""instigator"": ""{MASTERMIND}"",
      ""target"": ""{TARGET}""
    },
    ""knowingRoles"": [""instigator""],
    ""facts"": [
      {
        ""id"": ""fact_who"",
        ""category"": ""WHO"",
        ""textId"": ""VividWorld_Fact_Who"",
        ""text"": ""{INSTIGATOR} confronted {TARGET}"",
        ""vars"": {
          ""INSTIGATOR"": ""hero:{MASTERMIND}"",
          ""TARGET"": ""hero:{TARGET}""
        },
        ""fragility"": 2
      }
    ]
  }
]";

        [Fact]
        public void Load_ValidCatalog_LoadsAllTemplates()
        {
            var catalog = EventCatalogLoader.Load(ValidSingleTemplateJson, _defaultConfig);

            Assert.Single(catalog.Templates);
            Assert.Empty(catalog.Issues.Where(i => i.IsError));
            Assert.Equal(0, catalog.SkippedCount);

            var template = catalog.ByType("sample_event");
            Assert.NotNull(template);
            Assert.Equal("sample_event", template!.Type);
            Assert.Equal(EventOrigin.Public, template.Origin);
            Assert.Equal(3, template.DramaWeight);
            Assert.Equal(2, template.Roles.Count);
            Assert.Contains("instigator", template.KnowingRoles);
            Assert.Single(template.Facts);
        }

        [Fact]
        public void Load_BadJson_DoesNotThrow_AndReturnsBadJsonIssue()
        {
            const string malformedJson = "{ invalid json here ...";

            var catalog = EventCatalogLoader.Load(malformedJson, _defaultConfig);

            Assert.Empty(catalog.Templates);
            Assert.Contains(catalog.Issues, i => i.Code == CatalogIssueCode.BadJson && i.IsError && i.TemplateIndex == -1);
        }

        [Fact]
        public void Load_EmptyOrWhitespaceJson_ReturnsBadJson()
        {
            var catalog = EventCatalogLoader.Load("   ", _defaultConfig);

            Assert.Empty(catalog.Templates);
            Assert.Contains(catalog.Issues, i => i.Code == CatalogIssueCode.BadJson && i.IsError && i.TemplateIndex == -1);
        }

        [Fact]
        public void Load_MissingType_SkipsTemplate_OthersLoaded()
        {
            const string json = @"[
  {
    ""type"": """",
    ""origin"": ""public"",
    ""roles"": { ""hero"": ""{MASTERMIND}"" },
    ""facts"": [{ ""id"": ""f1"", ""text"": ""hello"" }]
  },
  {
    ""type"": ""valid_type"",
    ""origin"": ""public"",
    ""roles"": { ""hero"": ""{MASTERMIND}"" },
    ""facts"": [{ ""id"": ""f1"", ""text"": ""hello"" }]
  }
]";

            var catalog = EventCatalogLoader.Load(json, _defaultConfig);

            Assert.Single(catalog.Templates);
            Assert.Equal("valid_type", catalog.Templates[0].Type);
            Assert.Equal(1, catalog.SkippedCount);
            Assert.Contains(catalog.Issues, i => i.Code == CatalogIssueCode.MissingType && i.TemplateIndex == 0 && i.Field == "type");
        }

        [Fact]
        public void Load_DuplicateType_KeepsFirst_SkipsSubsequent()
        {
            const string json = @"[
  {
    ""type"": ""duplicate_type"",
    ""origin"": ""public"",
    ""dramaWeight"": 2,
    ""roles"": { ""hero"": ""{MASTERMIND}"" },
    ""facts"": [{ ""id"": ""f1"", ""text"": ""first definition"" }]
  },
  {
    ""type"": ""duplicate_type"",
    ""origin"": ""secret"",
    ""dramaWeight"": 5,
    ""roles"": { ""hero"": ""{MASTERMIND}"" },
    ""facts"": [{ ""id"": ""f1"", ""text"": ""second definition"" }]
  }
]";

            var catalog = EventCatalogLoader.Load(json, _defaultConfig);

            Assert.Single(catalog.Templates);
            Assert.Equal(2, catalog.Templates[0].DramaWeight);
            Assert.Equal(1, catalog.SkippedCount);
            Assert.Contains(catalog.Issues, i => i.Code == CatalogIssueCode.DuplicateType && i.TemplateIndex == 1 && i.Field == "type");
        }

        [Fact]
        public void Load_BadOrigin_MissingOrInvalid_SkipsTemplate()
        {
            const string json = @"[
  {
    ""type"": ""missing_origin"",
    ""roles"": { ""hero"": ""{MASTERMIND}"" },
    ""facts"": [{ ""id"": ""f1"", ""text"": ""fact text"" }]
  },
  {
    ""type"": ""invalid_origin"",
    ""origin"": ""unknown_origin_type"",
    ""roles"": { ""hero"": ""{MASTERMIND}"" },
    ""facts"": [{ ""id"": ""f1"", ""text"": ""fact text"" }]
  }
]";

            var catalog = EventCatalogLoader.Load(json, _defaultConfig);

            Assert.Empty(catalog.Templates);
            Assert.Equal(2, catalog.SkippedCount);
            Assert.Contains(catalog.Issues, i => i.Code == CatalogIssueCode.BadOrigin && i.TemplateIndex == 0 && i.Field == "origin");
            Assert.Contains(catalog.Issues, i => i.Code == CatalogIssueCode.BadOrigin && i.TemplateIndex == 1 && i.Field == "origin");
        }

        [Fact]
        public void Load_DramaOutOfRange_SkipsTemplate()
        {
            const string json = @"[
  {
    ""type"": ""drama_low"",
    ""origin"": ""public"",
    ""dramaWeight"": 0,
    ""roles"": { ""hero"": ""{MASTERMIND}"" },
    ""facts"": [{ ""id"": ""f1"", ""text"": ""fact text"" }]
  },
  {
    ""type"": ""drama_high"",
    ""origin"": ""public"",
    ""dramaWeight"": 6,
    ""roles"": { ""hero"": ""{MASTERMIND}"" },
    ""facts"": [{ ""id"": ""f1"", ""text"": ""fact text"" }]
  }
]";

            var catalog = EventCatalogLoader.Load(json, _defaultConfig);

            Assert.Empty(catalog.Templates);
            Assert.Equal(2, catalog.SkippedCount);
            Assert.Contains(catalog.Issues, i => i.Code == CatalogIssueCode.DramaOutOfRange && i.TemplateIndex == 0 && i.Field == "dramaWeight");
            Assert.Contains(catalog.Issues, i => i.Code == CatalogIssueCode.DramaOutOfRange && i.TemplateIndex == 1 && i.Field == "dramaWeight");
        }

        [Fact]
        public void Load_NoRoles_SkipsTemplate()
        {
            const string json = @"[
  {
    ""type"": ""empty_roles"",
    ""origin"": ""public"",
    ""roles"": {},
    ""facts"": [{ ""id"": ""f1"", ""text"": ""fact text"" }]
  }
]";

            var catalog = EventCatalogLoader.Load(json, _defaultConfig);

            Assert.Empty(catalog.Templates);
            Assert.Equal(1, catalog.SkippedCount);
            Assert.Contains(catalog.Issues, i => i.Code == CatalogIssueCode.NoRoles && i.TemplateIndex == 0 && i.Field == "roles");
        }

        [Fact]
        public void Load_NoFacts_SkipsTemplate()
        {
            const string json = @"[
  {
    ""type"": ""no_facts"",
    ""origin"": ""public"",
    ""roles"": { ""hero"": ""{MASTERMIND}"" },
    ""facts"": []
  }
]";

            var catalog = EventCatalogLoader.Load(json, _defaultConfig);

            Assert.Empty(catalog.Templates);
            Assert.Equal(1, catalog.SkippedCount);
            Assert.Contains(catalog.Issues, i => i.Code == CatalogIssueCode.NoFacts && i.TemplateIndex == 0 && i.Field == "facts");
        }

        [Fact]
        public void Load_TooManyFacts_ExceedingMaxFactsPerEvent_SkipsTemplate()
        {
            var cfg = new PersistenceConfig { MaxFactsPerEvent = 2 };
            const string json = @"[
  {
    ""type"": ""too_many_facts"",
    ""origin"": ""public"",
    ""roles"": { ""hero"": ""{MASTERMIND}"" },
    ""facts"": [
      { ""id"": ""f1"", ""text"": ""t1"" },
      { ""id"": ""f2"", ""text"": ""t2"" },
      { ""id"": ""f3"", ""text"": ""t3"" }
    ]
  }
]";

            var catalog = EventCatalogLoader.Load(json, cfg);

            Assert.Empty(catalog.Templates);
            Assert.Equal(1, catalog.SkippedCount);
            Assert.Contains(catalog.Issues, i => i.Code == CatalogIssueCode.TooManyFacts && i.TemplateIndex == 0 && i.Field == "facts");
        }

        [Fact]
        public void Load_EmptyFactId_SkipsTemplate()
        {
            const string json = @"[
  {
    ""type"": ""empty_fact_id"",
    ""origin"": ""public"",
    ""roles"": { ""hero"": ""{MASTERMIND}"" },
    ""facts"": [
      { ""id"": ""  "", ""text"": ""fact text"" }
    ]
  }
]";

            var catalog = EventCatalogLoader.Load(json, _defaultConfig);

            Assert.Empty(catalog.Templates);
            Assert.Equal(1, catalog.SkippedCount);
            Assert.Contains(catalog.Issues, i => i.Code == CatalogIssueCode.EmptyFactId && i.Field == "facts[0].id");
        }

        [Fact]
        public void Load_DuplicateFactId_SkipsTemplate()
        {
            const string json = @"[
  {
    ""type"": ""duplicate_fact_id"",
    ""origin"": ""public"",
    ""roles"": { ""hero"": ""{MASTERMIND}"" },
    ""facts"": [
      { ""id"": ""same_id"", ""text"": ""first fact"" },
      { ""id"": ""same_id"", ""text"": ""second fact"" }
    ]
  }
]";

            var catalog = EventCatalogLoader.Load(json, _defaultConfig);

            Assert.Empty(catalog.Templates);
            Assert.Equal(1, catalog.SkippedCount);
            Assert.Contains(catalog.Issues, i => i.Code == CatalogIssueCode.DuplicateFactId && i.Field == "facts[1].id");
        }

        [Fact]
        public void Load_EmptyFactText_SkipsTemplate()
        {
            const string json = @"[
  {
    ""type"": ""empty_fact_text"",
    ""origin"": ""public"",
    ""roles"": { ""hero"": ""{MASTERMIND}"" },
    ""facts"": [
      { ""id"": ""f1"", ""text"": """" }
    ]
  }
]";

            var catalog = EventCatalogLoader.Load(json, _defaultConfig);

            Assert.Empty(catalog.Templates);
            Assert.Equal(1, catalog.SkippedCount);
            Assert.Contains(catalog.Issues, i => i.Code == CatalogIssueCode.EmptyFactText && i.Field == "facts[0].text");
        }

        [Fact]
        public void Load_FragilityOutOfRange_SkipsTemplate()
        {
            const string json = @"[
  {
    ""type"": ""fragility_bad"",
    ""origin"": ""public"",
    ""roles"": { ""hero"": ""{MASTERMIND}"" },
    ""facts"": [
      { ""id"": ""f1"", ""text"": ""fact text"", ""fragility"": 6 }
    ]
  }
]";

            var catalog = EventCatalogLoader.Load(json, _defaultConfig);

            Assert.Empty(catalog.Templates);
            Assert.Equal(1, catalog.SkippedCount);
            Assert.Contains(catalog.Issues, i => i.Code == CatalogIssueCode.FragilityOutOfRange && i.Field == "facts[0].fragility");
        }

        [Fact]
        public void Load_BadVarPrefix_SkipsTemplate()
        {
            const string json = @"[
  {
    ""type"": ""bad_var_prefix"",
    ""origin"": ""public"",
    ""roles"": { ""hero"": ""{MASTERMIND}"" },
    ""facts"": [
      {
        ""id"": ""f1"",
        ""text"": ""{SUBJECT}"",
        ""vars"": { ""SUBJECT"": ""invalid_prefix:value"" }
      }
    ]
  }
]";

            var catalog = EventCatalogLoader.Load(json, _defaultConfig);

            Assert.Empty(catalog.Templates);
            Assert.Equal(1, catalog.SkippedCount);
            Assert.Contains(catalog.Issues, i => i.Code == CatalogIssueCode.BadVarPrefix && i.Field == "facts[0].vars.SUBJECT");
        }

        [Fact]
        public void Load_KnowingRoleNotARole_SkipsTemplate()
        {
            const string json = @"[
  {
    ""type"": ""bad_knowing_role"",
    ""origin"": ""public"",
    ""roles"": { ""mastermind"": ""{MASTERMIND}"" },
    ""knowingRoles"": [""target""],
    ""facts"": [
      { ""id"": ""f1"", ""text"": ""fact text"" }
    ]
  }
]";

            var catalog = EventCatalogLoader.Load(json, _defaultConfig);

            Assert.Empty(catalog.Templates);
            Assert.Equal(1, catalog.SkippedCount);
            Assert.Contains(catalog.Issues, i => i.Code == CatalogIssueCode.KnowingRoleNotARole && i.Field == "knowingRoles[0]");
        }

        [Fact]
        public void Load_UnknownPlaceholderInText_SkipsTemplate()
        {
            const string json = @"[
  {
    ""type"": ""unknown_placeholder"",
    ""origin"": ""public"",
    ""roles"": { ""mastermind"": ""{MASTERMIND}"" },
    ""facts"": [
      {
        ""id"": ""f1"",
        ""text"": ""{UNKNOWN_TAG} was seen"",
        ""vars"": {}
      }
    ]
  }
]";

            var catalog = EventCatalogLoader.Load(json, _defaultConfig);

            Assert.Empty(catalog.Templates);
            Assert.Equal(1, catalog.SkippedCount);
            Assert.Contains(catalog.Issues, i => i.Code == CatalogIssueCode.UnknownPlaceholder && i.Field == "facts[0].text");
        }

        [Fact]
        public void Load_MissingTextId_IsWarning_TemplateKept()
        {
            const string json = @"[
  {
    ""type"": ""missing_text_id"",
    ""origin"": ""public"",
    ""roles"": { ""hero"": ""{MASTERMIND}"" },
    ""facts"": [
      { ""id"": ""f1"", ""text"": ""plain text fallback without textId"" }
    ]
  }
]";

            var catalog = EventCatalogLoader.Load(json, _defaultConfig);

            Assert.Single(catalog.Templates);
            Assert.Equal(0, catalog.SkippedCount);
            var issue = Assert.Single(catalog.Issues);
            Assert.Equal(CatalogIssueCode.MissingTextId, issue.Code);
            Assert.False(issue.IsError);
            Assert.Equal("facts[0].textId", issue.Field);
        }

        [Fact]
        public void Load_DanglingTemplateRef_IsWarning_TemplateKept()
        {
            const string json = @"[
  {
    ""type"": ""standalone_event"",
    ""origin"": ""public"",
    ""linkedTemplateType"": ""nonexistent_template_type"",
    ""roles"": { ""hero"": ""{MASTERMIND}"" },
    ""facts"": [
      {
        ""id"": ""f1"",
        ""text"": ""plain text"",
        ""refersToTemplateType"": ""another_missing_template""
      }
    ]
  }
]";

            var catalog = EventCatalogLoader.Load(json, _defaultConfig);

            Assert.Single(catalog.Templates);
            Assert.Equal(0, catalog.SkippedCount);

            var issues = catalog.Issues.Where(i => i.Code == CatalogIssueCode.DanglingTemplateRef).ToList();
            Assert.Equal(2, issues.Count);
            Assert.All(issues, i => Assert.False(i.IsError));
            Assert.Contains(issues, i => i.Field == "linkedTemplateType");
            Assert.Contains(issues, i => i.Field == "facts[0].refersToTemplateType");
        }

        [Fact]
        public void Load_StrictCatalog_WhenErrorExists_ReturnsEmptyTemplates()
        {
            const string json = @"[
  {
    ""type"": ""valid_one"",
    ""origin"": ""public"",
    ""roles"": { ""hero"": ""{MASTERMIND}"" },
    ""facts"": [{ ""id"": ""f1"", ""text"": ""valid fact"" }]
  },
  {
    ""type"": ""broken_one"",
    ""origin"": ""public"",
    ""roles"": {},
    ""facts"": [{ ""id"": ""f1"", ""text"": ""fact"" }]
  }
]";

            // strictCatalog = false: valid_one is kept, broken_one skipped
            var lenient = EventCatalogLoader.Load(json, _defaultConfig, strictCatalog: false);
            Assert.Single(lenient.Templates);
            Assert.Equal("valid_one", lenient.Templates[0].Type);
            Assert.Equal(1, lenient.SkippedCount);

            // strictCatalog = true: error causes whole catalog to reject
            var strict = EventCatalogLoader.Load(json, _defaultConfig, strictCatalog: true);
            Assert.Empty(strict.Templates);
            Assert.Contains(strict.Issues, i => i.IsError);
        }

        [Fact]
        public void Load_IssuesFieldPath_MatchesExactJsonPath()
        {
            const string json = @"[
  {
    ""type"": ""path_test"",
    ""origin"": ""public"",
    ""roles"": { ""hero"": ""{MASTERMIND}"" },
    ""facts"": [
      { ""id"": ""f0"", ""text"": ""ok"" },
      {
        ""id"": ""f1"",
        ""text"": ""at {SETTLEMENT}"",
        ""vars"": {
          ""SETTLEMENT"": ""town_B1""
        }
      }
    ]
  }
]";

            var catalog = EventCatalogLoader.Load(json, _defaultConfig);

            Assert.Empty(catalog.Templates);
            var issue = Assert.Single(catalog.Issues.Where(i => i.Code == CatalogIssueCode.BadVarPrefix));
            Assert.Equal("facts[1].vars.SETTLEMENT", issue.Field);
            Assert.Equal(0, issue.TemplateIndex);
            Assert.Equal("path_test", issue.TemplateType);
        }

        [Fact]
        public void Bind_FullBinding_ProducesExactSubmissionFields()
        {
            var catalog = EventCatalogLoader.Load(ValidSingleTemplateJson, _defaultConfig);
            var template = Assert.Single(catalog.Templates);

            var bindings = new Dictionary<string, string>
            {
                ["MASTERMIND"] = "lord_1_2",
                ["TARGET"] = "lord_3_4"
            };

            var submission = TemplateBinder.Bind(template, bindings, 42.5, "evt_prev_123", out var issues);

            Assert.NotNull(submission);
            Assert.Empty(issues.Where(i => i.IsError));
            Assert.Equal("sample_event", submission!.Type);
            Assert.Equal(42.5, submission.Day);
            Assert.Equal(EventOrigin.Public, submission.Origin);
            Assert.Equal(3, submission.DramaWeight);
            Assert.True(submission.AutoResolveWitnesses);

            Assert.Equal("lord_1_2", submission.Participants["instigator"]);
            Assert.Equal("lord_3_4", submission.Participants["target"]);

            Assert.Contains("instigator", submission.KnowingRoles);

            var fact = Assert.Single(submission.Facts);
            Assert.Equal("fact_who", fact.Id);
            Assert.NotNull(fact.Vars);
            Assert.Equal("hero:lord_1_2", fact.Vars!["INSTIGATOR"]);
            Assert.Equal("hero:lord_3_4", fact.Vars["TARGET"]);
        }

        [Fact]
        public void Bind_KnowingRoles_ContainsOnlySpecifiedRoles()
        {
            const string json = @"[
  {
    ""type"": ""secret_sabotage"",
    ""origin"": ""secret"",
    ""roles"": {
      ""mastermind"": ""{MASTERMIND}"",
      ""target"": ""{TARGET}"",
      ""agent"": ""{AGENT}""
    },
    ""knowingRoles"": [""mastermind"", ""agent""],
    ""facts"": [
      { ""id"": ""f1"", ""text"": ""sabotage"" }
    ]
  }
]";
            var catalog = EventCatalogLoader.Load(json, _defaultConfig);
            var template = Assert.Single(catalog.Templates);

            var bindings = new Dictionary<string, string>
            {
                ["MASTERMIND"] = "lord_mm",
                ["TARGET"] = "lord_target",
                ["AGENT"] = "lord_agent"
            };

            var submission = TemplateBinder.Bind(template, bindings, 10.0, null, out var issues);

            Assert.NotNull(submission);
            Assert.False(submission!.AutoResolveWitnesses);
            Assert.Equal(2, submission.KnowingRoles.Count);
            Assert.Contains("mastermind", submission.KnowingRoles);
            Assert.Contains("agent", submission.KnowingRoles);
            Assert.DoesNotContain("target", submission.KnowingRoles);
        }

        [Fact]
        public void Bind_MissingPlaceholder_ReturnsNullWithUnboundPlaceholderIssue()
        {
            var catalog = EventCatalogLoader.Load(ValidSingleTemplateJson, _defaultConfig);
            var template = Assert.Single(catalog.Templates);

            // TARGET is omitted
            var bindings = new Dictionary<string, string>
            {
                ["MASTERMIND"] = "lord_1_2"
            };

            var submission = TemplateBinder.Bind(template, bindings, 10.0, null, out var issues);

            Assert.Null(submission);
            Assert.Contains(issues, i => i.Code == CatalogIssueCode.UnboundPlaceholder && i.IsError && i.Field == "roles.target");
        }

        [Fact]
        public void Bind_Vars_PreservePrefixAfterBinding()
        {
            const string json = @"[
  {
    ""type"": ""prefix_check"",
    ""origin"": ""public"",
    ""roles"": { ""hero"": ""{MASTERMIND}"", ""town"": ""{SETTLEMENT}"" },
    ""facts"": [
      {
        ""id"": ""f1"",
        ""text"": ""at place"",
        ""vars"": {
          ""HERO"": ""hero:{MASTERMIND}"",
          ""TOWN"": ""settlement:{SETTLEMENT}""
        }
      }
    ]
  }
]";
            var catalog = EventCatalogLoader.Load(json, _defaultConfig);
            var template = Assert.Single(catalog.Templates);

            var bindings = new Dictionary<string, string>
            {
                ["MASTERMIND"] = "lord_hero",
                ["SETTLEMENT"] = "town_A1"
            };

            var submission = TemplateBinder.Bind(template, bindings, 5.0, null, out var issues);

            Assert.NotNull(submission);
            Assert.Empty(issues.Where(i => i.IsError));
            Assert.Equal("hero:lord_hero", submission!.Facts[0].Vars!["HERO"]);
            Assert.Equal("settlement:town_A1", submission.Facts[0].Vars!["TOWN"]);
        }

        private const string LinkedTemplatesJson = @"[
  {
    ""type"": ""first_event"",
    ""origin"": ""public"",
    ""roles"": { ""hero"": ""{MASTERMIND}"" },
    ""facts"": [ { ""id"": ""f1"", ""text"": ""something happened"" } ]
  },
  {
    ""type"": ""other_event"",
    ""origin"": ""public"",
    ""roles"": { ""hero"": ""{MASTERMIND}"" },
    ""facts"": [ { ""id"": ""f1"", ""text"": ""something else happened"" } ]
  },
  {
    ""type"": ""second_event"",
    ""origin"": ""public"",
    ""linkedEventId"": ""{LINKED_FIRST_EVENT}"",
    ""roles"": { ""hero"": ""{MASTERMIND}"" },
    ""facts"": [
      { ""id"": ""f1"", ""text"": ""a follow-up"", ""refersTo"": ""{LINKED_FIRST_EVENT}"" }
    ]
  }
]";

        [Fact]
        public void Bind_FactRefersToTheLinkedTemplate_GetsTheLinkedEventId()
        {
            var catalog = EventCatalogLoader.Load(LinkedTemplatesJson, _defaultConfig);
            var template = catalog.ByType("second_event");
            Assert.NotNull(template);

            var bindings = new Dictionary<string, string> { ["MASTERMIND"] = "lord_1_2" };
            var submission = TemplateBinder.Bind(template!, bindings, 7.0, "evt_26000_aaaa", out var issues);

            Assert.NotNull(submission);
            Assert.Empty(issues.Where(i => i.IsError));
            Assert.Equal("evt_26000_aaaa", submission!.LinkedEventId);
            Assert.Equal("evt_26000_aaaa", submission.Facts[0].RefersTo);
        }

        [Fact]
        public void Bind_FactRefersToADifferentTemplateThanTheLink_IsAnErrorNotASilentWrongId()
        {
            // 碎片指向 other_event，但這則提交解析出來的 linkedEventId 是 first_event 那一則。
            // 沿用它就是靜默指錯事件 —— 必須整則不生。
            var catalog = EventCatalogLoader.Load(LinkedTemplatesJson, _defaultConfig);
            var template = catalog.ByType("second_event");
            Assert.NotNull(template);
            template!.Facts[0].RefersToTemplateType = "other_event";

            var bindings = new Dictionary<string, string> { ["MASTERMIND"] = "lord_1_2" };
            var submission = TemplateBinder.Bind(template, bindings, 7.0, "evt_26000_aaaa", out var issues);

            Assert.Null(submission);
            var issue = Assert.Single(issues.Where(i => i.Code == CatalogIssueCode.RefersToLinkMismatch));
            Assert.True(issue.IsError);
            Assert.Equal("facts[0].refersToTemplateType", issue.Field);
            Assert.Contains("other_event", issue.Detail);
            Assert.Contains("first_event", issue.Detail);
        }

        [Fact]
        public void Bind_SupportsBracedAndUnbracedPlaceholderKeys()
        {
            var catalog = EventCatalogLoader.Load(ValidSingleTemplateJson, _defaultConfig);
            var template = Assert.Single(catalog.Templates);

            // Pass keys wrapped in curly braces
            var bindings = new Dictionary<string, string>
            {
                ["{MASTERMIND}"] = "lord_1_2",
                ["{TARGET}"] = "lord_3_4"
            };

            var submission = TemplateBinder.Bind(template, bindings, 10.0, null, out var issues);

            Assert.NotNull(submission);
            Assert.Empty(issues.Where(i => i.IsError));
            Assert.Equal("lord_1_2", submission!.Participants["instigator"]);
            Assert.Equal("lord_3_4", submission.Participants["target"]);
        }

        [Fact]
        public void Regression_SampleEventsFile_LoadsExpectedFiveTemplates()
        {
            string json = FindSampleEventsJson();
            var catalog = EventCatalogLoader.Load(json, _defaultConfig);

            Assert.Equal(5, catalog.Templates.Count);
            Assert.Equal(0, catalog.SkippedCount);
            Assert.Empty(catalog.Issues.Where(i => i.IsError));

            // Template 1: duel_arranged
            var t1 = catalog.Templates[0];
            Assert.Equal("duel_arranged", t1.Type);
            Assert.Equal(EventOrigin.Public, t1.Origin);
            Assert.Equal(3, t1.DramaWeight);
            Assert.Equal(4, t1.Facts.Count);

            // Template 2: tavern_quarrel
            var t2 = catalog.Templates[1];
            Assert.Equal("tavern_quarrel", t2.Type);
            Assert.Equal(EventOrigin.Public, t2.Origin);
            Assert.Equal(4, t2.DramaWeight);
            Assert.Equal(5, t2.Facts.Count);

            // Template 3: shared_meal
            var t3 = catalog.Templates[2];
            Assert.Equal("shared_meal", t3.Type);
            Assert.Equal(EventOrigin.Public, t3.Origin);
            Assert.Equal(1, t3.DramaWeight);
            Assert.Equal(4, t3.Facts.Count);

            // Template 4: duel
            var t4 = catalog.Templates[3];
            Assert.Equal("duel", t4.Type);
            Assert.Equal(EventOrigin.Public, t4.Origin);
            Assert.Equal(5, t4.DramaWeight);
            Assert.Equal(5, t4.Facts.Count);
            Assert.Equal("tavern_quarrel", t4.LinkedTemplateType);

            // Template 5: covert_sabotage
            var t5 = catalog.Templates[4];
            Assert.Equal("covert_sabotage", t5.Type);
            Assert.Equal(EventOrigin.Secret, t5.Origin);
            Assert.Equal(4, t5.DramaWeight);
            Assert.Equal(8, t5.Facts.Count);
            Assert.Equal("duel_arranged", t5.LinkedTemplateType);
            Assert.Equal(2, t5.KnowingRoles.Count);
            Assert.Contains("mastermind", t5.KnowingRoles);
            Assert.Contains("agent", t5.KnowingRoles);
        }

        [Fact]
        public void ConfigMerge_AddsMissingEventsKeys_WithoutTouchingExisting()
        {
            var existingJson = @"{
                ""configVersion"": 1,
                ""events"": {
                    ""catalogFile"": ""custom_events.json""
                }
            }";

            var canonicalConfig = new VividWorldConfig();
            var canonicalJson = VividJson.Write(canonicalConfig);

            var existing = JObject.Parse(existingJson);
            var canonical = JObject.Parse(canonicalJson);

            var result = ConfigMerge.AddMissingKeys(existing, canonical);

            // catalogFile was already in existing, should not be overwritten
            Assert.Equal("custom_events.json", (string)result.Merged["events"]!["catalogFile"]!);
            // strictCatalog was missing, should be added with canonical default
            Assert.False((bool)result.Merged["events"]!["strictCatalog"]!);
            Assert.Contains("events.strictCatalog", result.AddedPaths);
            Assert.DoesNotContain("events.catalogFile", result.AddedPaths);
        }

        private static string FindSampleEventsJson()
        {
            string? current = AppContext.BaseDirectory;
            while (!string.IsNullOrEmpty(current))
            {
                if (File.Exists(Path.Combine(current, "VividWorld.sln")))
                {
                    string path = Path.Combine(current, "module", "ModuleData", "vividworld_sample_events.json");
                    if (File.Exists(path)) return File.ReadAllText(path);
                }

                var parent = Directory.GetParent(current);
                if (parent == null) break;
                current = parent.FullName;
            }
            throw new FileNotFoundException("vividworld_sample_events.json could not be found relative to " + AppContext.BaseDirectory);
        }
    }
}
