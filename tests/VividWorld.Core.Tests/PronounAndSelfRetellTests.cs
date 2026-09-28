#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using VividWorld.Core.Catalog;
using VividWorld.Core.Config;
using VividWorld.Core.Events;
using VividWorld.Core.Presentation;
using Xunit;

namespace VividWorld.Core.Tests
{
    public class PronounAndSelfRetellTests
    {
        private static string FindRepoRoot()
        {
            string? current = AppContext.BaseDirectory;
            while (!string.IsNullOrEmpty(current))
            {
                if (File.Exists(Path.Combine(current, "VividWorld.sln")))
                {
                    return current;
                }
                var parent = Directory.GetParent(current);
                if (parent == null) break;
                current = parent.FullName;
            }
            throw new DirectoryNotFoundException("Could not find repository root containing VividWorld.sln");
        }

        private static PresentationConfig CreateTestConfig() => new PresentationConfig
        {
            EncyclopediaLinksEnabled = false,
            FactSeparator = ", ",
            SentenceEnd = "."
        };

        [Theory]
        [InlineData("male", "he", "him", "his")]
        [InlineData("female", "she", "her", "her")]
        [InlineData("unknown", "they", "them", "their")]
        [InlineData("speaker", "I", "me", "my")]
        public void Pronouns_ResolveCorrectly_ForAllGendersAndSpeaker(
            string scenario, string expectedSubj, string expectedObj, string expectedPoss)
        {
            var cfg = CreateTestConfig();
            string targetHeroId = "hero_target";
            string otherHeroId = "hero_other";

            string speakerHeroId = scenario == "speaker" ? targetHeroId : otherHeroId;
            string speakerRole = scenario == "speaker" ? "target" : "other";

            Func<string, bool?> isFemale = id =>
            {
                if (id == targetHeroId)
                {
                    if (scenario == "female") return true;
                    if (scenario == "male") return false;
                    return null;
                }
                return null;
            };

            var evt = new WorldEvent
            {
                EventId = "evt_test",
                Type = "test_event",
                Origin = EventOrigin.Public,
                Participants = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["target"] = targetHeroId,
                    ["other"] = otherHeroId
                }
            };

            var facts = new[]
            {
                new Fact { Id = "subj", TextId = "Fact_Subj", Text = "{TARGET.he} went away" },
                new Fact { Id = "obj", TextId = "Fact_Obj", Text = "saw {TARGET.him} yesterday" },
                new Fact { Id = "poss", TextId = "Fact_Poss", Text = "took {TARGET.his} weapon" }
            };

            var composed = RumorTextComposer.Compose(evt, facts, cfg, prefix: null, speakerHeroId: speakerHeroId);

            var warnings = new List<string>();
            var result = RumorTextAssembler.Assemble(
                composed,
                cfg,
                resolveVar: (val, links) => val,
                getTemplate: (id, fb) => fb ?? string.Empty,
                getLocalized: (k, fb) => fb,
                onWarning: warnings.Add,
                isFemale: isFemale);

            Assert.Empty(warnings);

            string capitalizedSubj = char.ToUpperInvariant(expectedSubj[0]) + expectedSubj.Substring(1);
            string expected = $"{capitalizedSubj} went away, saw {expectedObj} yesterday, took {expectedPoss} weapon.";
            Assert.Equal(expected, result.PlainText);
        }

        [Fact]
        public void Pronouns_WhenRoleNotInEvent_EmitsWarning_AndFallsBackToTheyThemTheir()
        {
            var cfg = CreateTestConfig();
            var evt = new WorldEvent
            {
                EventId = "evt_test",
                Type = "test_event",
                Origin = EventOrigin.Public,
                Participants = new Dictionary<string, string>
                {
                    ["known_role"] = "hero_known"
                }
            };

            var fact = new Fact
            {
                Id = "outcome",
                TextId = "Fact_Outcome",
                Text = "and so {MISSING_ROLE.he} walked away with {MISSING_ROLE.his} horse and saw {MISSING_ROLE.him}",
                Vars = new Dictionary<string, string>()
            };

            var composed = RumorTextComposer.Compose(evt, new[] { fact }, cfg, prefix: null, speakerHeroId: "hero_speaker");

            var warnings = new List<string>();
            var result = RumorTextAssembler.Assemble(
                composed,
                cfg,
                resolveVar: (val, links) => val,
                getTemplate: (id, fb) => fb ?? string.Empty,
                getLocalized: (k, fb) => fb,
                onWarning: warnings.Add,
                isFemale: id => false);

            Assert.Single(warnings);
            string warn = warnings[0];
            Assert.Contains("MISSING_ROLE.he", warn);
            Assert.Contains("MISSING_ROLE.his", warn);
            Assert.Contains("MISSING_ROLE.him", warn);
            Assert.Contains("Fact_Outcome", warn);

            Assert.Equal("And so they walked away with their horse and saw them.", result.PlainText);
        }

        [Fact]
        public void Pronoun_Token_ResolvedAfterSelfTemplate_AndFirstPersonPronounReplacement()
        {
            // 例：HeroReleased_Outcome 由抓人的一方（captor）講、俘虜（prisoner）是女性 ⇒ she
            var cfg = CreateTestConfig();
            var evt = new WorldEvent
            {
                EventId = "evt_release",
                Type = "hero_released_from_captivity",
                Origin = EventOrigin.Public,
                Participants = new Dictionary<string, string>
                {
                    ["captor"] = "hero_captor",
                    ["prisoner"] = "hero_female_prisoner"
                }
            };

            var fact = new Fact
            {
                Id = "outcome",
                TextId = "VividWorld_Fact_HeroReleased_Outcome",
                Text = "and so {PRISONER.he} walked free",
                Vars = new Dictionary<string, string>()
            };

            // Captor speaks
            var composed = RumorTextComposer.Compose(evt, new[] { fact }, cfg, prefix: null, speakerHeroId: "hero_captor");

            var warnings = new List<string>();
            var result = RumorTextAssembler.Assemble(
                composed,
                cfg,
                resolveVar: (val, links) => val,
                getTemplate: (id, fb) => fb ?? string.Empty,
                getLocalized: (k, fb) => fb,
                onWarning: warnings.Add,
                isFemale: id => id == "hero_female_prisoner" ? true : false);

            Assert.Empty(warnings);
            Assert.Equal("And so she walked free.", result.PlainText);
        }

        [Fact]
        public void CNt_StringTable_ContainsNoPronounTokens()
        {
            string repoRoot = FindRepoRoot();
            string cntPath = Path.Combine(repoRoot, "module", "ModuleData", "Languages", "CNt", "std_module_strings_xml.xml");
            string content = File.ReadAllText(cntPath);

            var doc = XDocument.Parse(content);
            var badElements = doc.Descendants("string")
                .Where(el =>
                {
                    string text = el.Attribute("text")?.Value ?? string.Empty;
                    return Regex.IsMatch(text, @"\.(he|him|his)\}", RegexOptions.IgnoreCase);
                })
                .Select(el => el.Attribute("id")?.Value)
                .ToList();

            Assert.True(badElements.Count == 0,
                $"CNt string table must not contain pronoun tokens (.he}}/.him}}/.his}}). Found in: {string.Join(", ", badElements)}");
        }

        [Fact]
        public void AllPronounTokens_InEnglishTableAndTemplates_MatchTemplateRoles()
        {
            string repoRoot = FindRepoRoot();
            string eventsPath = Path.Combine(repoRoot, "module", "ModuleData", "vividworld_events.json");
            string sitEventsPath = Path.Combine(repoRoot, "module", "ModuleData", "vividworld_situation_events.json");
            string enXmlPath = Path.Combine(repoRoot, "module", "ModuleData", "Languages", "std_module_strings_xml.xml");

            var pCfg = new PersistenceConfig();
            var allTemplates = new List<EventTemplate>();
            allTemplates.AddRange(EventCatalogLoader.Load(File.ReadAllText(eventsPath), pCfg).Templates);
            allTemplates.AddRange(EventCatalogLoader.Load(File.ReadAllText(sitEventsPath), pCfg).Templates);

            var enDoc = XDocument.Parse(File.ReadAllText(enXmlPath));
            var stringMap = enDoc.Descendants("string")
                .ToDictionary(
                    el => el.Attribute("id")?.Value ?? string.Empty,
                    el => el.Attribute("text")?.Value ?? string.Empty,
                    StringComparer.OrdinalIgnoreCase);

            var tokenRegex = new Regex(@"\{([A-Za-z0-9_]+)\.(he|him|his)\}", RegexOptions.IgnoreCase);

            foreach (var template in allTemplates)
            {
                var roleKeys = new HashSet<string>(template.Roles?.Keys ?? Enumerable.Empty<string>(), StringComparer.OrdinalIgnoreCase);

                foreach (var tf in template.Facts)
                {
                    // Check fallback text
                    if (!string.IsNullOrEmpty(tf.Text))
                    {
                        var matches = tokenRegex.Matches(tf.Text);
                        foreach (Match m in matches)
                        {
                            string role = m.Groups[1].Value;
                            Assert.True(roleKeys.Contains(role),
                                $"Template '{template.Type}' fact '{tf.Id}' has pronoun token for role '{role}', but role is not in template roles [{string.Join(", ", roleKeys)}].");
                        }
                    }

                    // Check XML text
                    if (!string.IsNullOrEmpty(tf.TextId) && stringMap.TryGetValue(tf.TextId!, out var xmlText))
                    {
                        var matches = tokenRegex.Matches(xmlText);
                        foreach (Match m in matches)
                        {
                            string role = m.Groups[1].Value;
                            Assert.True(roleKeys.Contains(role),
                                $"Template '{template.Type}' fact '{tf.Id}' XML '{tf.TextId}' has pronoun token for role '{role}', but role is not in template roles [{string.Join(", ", roleKeys)}].");
                        }
                    }
                }
            }
        }

        [Fact]
        public void RetellPrefix_Participant_Hop0_ReturnsRetellSelf()
        {
            var prefix = RumorPrefixSelector.SelectPrefix(
                hop: 0,
                sourceHeroId: null,
                isRetell: true,
                isCorrection: false,
                isParticipant: true);

            Assert.Equal(RumorPrefixKind.RetellSelf, prefix.Kind);
            Assert.Equal("VividWorld_RetellPrefix_Self", prefix.TextId);
            Assert.Equal("You may have heard about this already—", prefix.Fallback);
        }

        [Fact]
        public void RetellPrefix_Onlooker_Hop0_ReturnsRetell()
        {
            var prefix = RumorPrefixSelector.SelectPrefix(
                hop: 0,
                sourceHeroId: null,
                isRetell: true,
                isCorrection: false,
                isParticipant: false);

            Assert.Equal(RumorPrefixKind.Retell, prefix.Kind);
            Assert.Equal("VividWorld_RetellPrefix", prefix.TextId);
            Assert.Equal("I was there, in fact—", prefix.Fallback);
        }

        [Fact]
        public void RetellPrefix_Hop1_UnaffectedByRetellSelf()
        {
            var prefix = RumorPrefixSelector.SelectPrefix(
                hop: 1,
                sourceHeroId: "lord_source",
                isRetell: true,
                isCorrection: false,
                isParticipant: true);

            Assert.Equal(RumorPrefixKind.HeardFromSource, prefix.Kind);
            Assert.Equal("VividWorld_Prefix_HeardFromSource", prefix.TextId);
            Assert.Equal("{SOURCE} told me that", prefix.Fallback);
        }
    }
}
