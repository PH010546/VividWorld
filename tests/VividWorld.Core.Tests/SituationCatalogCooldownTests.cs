using System;
using System.Collections.Generic;
using System.IO;
using VividWorld.Core.Situations;
using Xunit;

namespace VividWorld.Core.Tests
{
    public class SituationCatalogCooldownTests
    {
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
        public void MaxCooldownDays_NoCooldownConditions_ReturnsZero()
        {
            var template = new SituationTemplate
            {
                Id = "sit_no_cd",
                Conditions = new List<SituationConditionDef>
                {
                    new() { Type = "relationship", Value = true }
                },
                Branches = new List<SituationBranchDef>
                {
                    new()
                    {
                        Id = "b1",
                        Preconditions = new List<SituationConditionDef>
                        {
                            new() { Type = "trait" }
                        }
                    }
                }
            };

            var catalog = new SituationCatalog(new[] { template }, Array.Empty<SituationIssue>(), 0);
            Assert.Equal(0.0, catalog.MaxCooldownDays());
        }

        [Fact]
        public void MaxCooldownDays_CalculatesMaxFromBothConditionsAndPreconditions()
        {
            var template = new SituationTemplate
            {
                Id = "sit_cd",
                Conditions = new List<SituationConditionDef>
                {
                    new() { Type = "cooldown", Days = 30.0 },
                    new() { Type = "cooldown", Days = 15.0 }
                },
                Branches = new List<SituationBranchDef>
                {
                    new()
                    {
                        Id = "b1",
                        Preconditions = new List<SituationConditionDef>
                        {
                            new() { Type = "cooldown", Days = 45.0 },
                            new() { Type = "cooldown", Days = 20.0 }
                        }
                    }
                }
            };

            var catalog = new SituationCatalog(new[] { template }, Array.Empty<SituationIssue>(), 0);
            Assert.Equal(45.0, catalog.MaxCooldownDays());
        }

        [Fact]
        public void MaxCooldownDays_ShippedSituations_Returns45()
        {
            string sitPath = FindRepoFile(Path.Combine("module", "ModuleData", "vividworld_situations.json"));
            string json = File.ReadAllText(sitPath);
            var catalog = SituationCatalogLoader.Load(json);

            Assert.Equal(45.0, catalog.MaxCooldownDays());
        }
    }
}
