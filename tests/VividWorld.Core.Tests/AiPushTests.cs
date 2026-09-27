using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using VividWorld.Core.Ai;
using VividWorld.Core.Config;
using VividWorld.Core.Events;
using VividWorld.Core.Memory;
using VividWorld.Core.Persistence;
using VividWorld.Core.Presentation;
using Xunit;

namespace VividWorld.Core.Tests
{
    #region Mock Types for Target Validator Tests
    public static class MockValidConversation
    {
        public static event Action<object>? Started;
        public static void FireStarted(object info) => Started?.Invoke(info);
    }

    public class MockValidMemoryEntry
    {
    }

    public static class MockValidNpcMemory
    {
        public static int ApiVersion = 1;
        public static int AddMemory(MockValidMemoryEntry entry) => 0;
    }

    public static class MockVersionMismatchNpcMemory
    {
        public static int ApiVersion = 99;
        public static int AddMemory(MockValidMemoryEntry entry) => 0;
    }

    public static class MockMissingMethodNpcMemory
    {
        public static int ApiVersion = 1;
    }
    #endregion

    public class AiPushTests
    {
        [Fact]
        public void TargetValidator_FailsGracefully_WhenAssemblyNotFound()
        {
            var desc = AiTargetDescription.CalradiaRemembers();
            var result = AiTargetValidator.Validate(desc, null);

            Assert.False(result.IsEnabled);
            Assert.Equal(0, result.DetectedVersion);
            Assert.Contains("not found", result.Reason);
        }

        [Fact]
        public void TargetValidator_FailsGracefully_WhenMemberNotFound()
        {
            var desc = new AiTargetDescription
            {
                TargetId = "MockTarget",
                AssemblyName = typeof(AiPushTests).Assembly.GetName().Name ?? "VividWorld.Core.Tests",
                ConversationTypeName = typeof(MockValidConversation).FullName ?? string.Empty,
                ConversationEventName = "Started",
                MemoryTypeName = typeof(MockMissingMethodNpcMemory).FullName ?? string.Empty,
                MemoryEntryTypeName = typeof(MockValidMemoryEntry).FullName ?? string.Empty,
                AddMemoryMethodName = "AddMemory", // Missing on MockMissingMethodNpcMemory
                ApiVersionMemberName = "ApiVersion",
                AcceptedApiVersion = 1,
                IsPersistent = true
            };

            var result = AiTargetValidator.Validate(desc, typeof(AiPushTests).Assembly);

            Assert.False(result.IsEnabled);
            Assert.Contains("Method 'AddMemory' not found", result.Reason);
        }

        [Fact]
        public void TargetValidator_FailsGracefully_WhenVersionMismatch()
        {
            var desc = new AiTargetDescription
            {
                TargetId = "MockTarget",
                AssemblyName = typeof(AiPushTests).Assembly.GetName().Name ?? "VividWorld.Core.Tests",
                ConversationTypeName = typeof(MockValidConversation).FullName ?? string.Empty,
                ConversationEventName = "Started",
                MemoryTypeName = typeof(MockVersionMismatchNpcMemory).FullName ?? string.Empty,
                MemoryEntryTypeName = typeof(MockValidMemoryEntry).FullName ?? string.Empty,
                AddMemoryMethodName = "AddMemory",
                ApiVersionMemberName = "ApiVersion",
                AcceptedApiVersion = 1, // Target has 99
                IsPersistent = true
            };

            var result = AiTargetValidator.Validate(desc, typeof(AiPushTests).Assembly);

            Assert.False(result.IsEnabled);
            Assert.Equal(99, result.DetectedVersion);
            Assert.Contains("Version mismatch", result.Reason);
        }

        [Fact]
        public void TargetValidator_Succeeds_WhenAllRequirementsMet()
        {
            var desc = new AiTargetDescription
            {
                TargetId = "MockTarget",
                AssemblyName = typeof(AiPushTests).Assembly.GetName().Name ?? "VividWorld.Core.Tests",
                ConversationTypeName = typeof(MockValidConversation).FullName ?? string.Empty,
                ConversationEventName = "Started",
                MemoryTypeName = typeof(MockValidNpcMemory).FullName ?? string.Empty,
                MemoryEntryTypeName = typeof(MockValidMemoryEntry).FullName ?? string.Empty,
                AddMemoryMethodName = "AddMemory",
                ApiVersionMemberName = "ApiVersion",
                AcceptedApiVersion = 1,
                IsPersistent = true
            };

            var result = AiTargetValidator.Validate(desc, typeof(AiPushTests).Assembly);

            Assert.True(result.IsEnabled);
            Assert.Equal(1, result.DetectedVersion);
            Assert.Equal("Binding successful", result.Reason);
        }

        [Fact]
        public void AiPushedStore_LoadsEmptyStore_WhenFileDoesNotExist()
        {
            string nonExistentPath = Path.Combine(Path.GetTempPath(), "vw_non_existent_" + Guid.NewGuid().ToString("N") + ".json");

            var store = AiPushedStore.LoadFromFile(nonExistentPath);

            Assert.NotNull(store);
            Assert.False(store.IsDirty);
            Assert.False(store.HasPushedVersion("CalradiaRemembers", "hero_1", "evt_1", "v1"));
            var pushed = store.GetPushedForHero("CalradiaRemembers", "hero_1");
            Assert.NotNull(pushed);
            Assert.Empty(pushed);
        }

        [Fact]
        public void AiPushedStore_TracksPushedEntries_AndSavesLoadsDeterministically()
        {
            var store = new AiPushedStore();
            Assert.False(store.IsDirty);

            store.Record("CalradiaRemembers", "hero_1", "evt_100", "f1,f2", 15.5);
            Assert.True(store.IsDirty);

            Assert.True(store.HasPushedVersion("CalradiaRemembers", "hero_1", "evt_100", "f1,f2"));
            Assert.False(store.HasPushedVersion("CalradiaRemembers", "hero_1", "evt_100", "f1,f2,f3"));
            Assert.False(store.HasPushedVersion("CalradiaRemembers", "hero_2", "evt_100", "f1,f2"));

            var heroPushed = store.GetPushedForHero("CalradiaRemembers", "hero_1");
            Assert.Single(heroPushed);
            Assert.True(heroPushed.ContainsKey("evt_100"));
            Assert.Equal("f1,f2", heroPushed["evt_100"].Version);
            Assert.Equal(15.5, heroPushed["evt_100"].PushedDay);

            string tempFile = Path.Combine(Path.GetTempPath(), "vw_test_pushed_" + Guid.NewGuid().ToString("N") + ".json");
            try
            {
                bool saved = store.SaveToFile(tempFile);
                Assert.True(saved);
                Assert.False(store.IsDirty);

                var loaded = AiPushedStore.LoadFromFile(tempFile);
                Assert.NotNull(loaded);
                Assert.True(loaded.HasPushedVersion("CalradiaRemembers", "hero_1", "evt_100", "f1,f2"));
                Assert.False(loaded.HasPushedVersion("CalradiaRemembers", "hero_1", "evt_100", "other"));
            }
            finally
            {
                if (File.Exists(tempFile)) File.Delete(tempFile);
            }
        }

        [Fact]
        public void AiPushVersion_ComputesDeterministically_WithSortedFactIdsAndCorrectionSuffix()
        {
            var unorderedFacts = new[] { "fact_gamma", "fact_alpha", "fact_beta" };

            string v1 = AiPushVersion.Compute(unorderedFacts, false);
            string v2 = AiPushVersion.Compute(new[] { "fact_alpha", "fact_beta", "fact_gamma" }, false);
            string vCorr = AiPushVersion.Compute(unorderedFacts, true);

            Assert.Equal("fact_alpha,fact_beta,fact_gamma", v1);
            Assert.Equal(v1, v2);
            Assert.Equal("fact_alpha,fact_beta,fact_gamma:corr", vCorr);

            Assert.Equal(string.Empty, AiPushVersion.Compute(null, false));
            Assert.Equal(":corr", AiPushVersion.Compute(null, true));
        }

        [Fact]
        public void AiPushPlanner_ExcludesAlreadyPushedSameVersion_AllowsNewVersion()
        {
            var store = new AiPushedStore();
            store.Record("CalradiaRemembers", "hero_1", "evt_1", "f1,f2", 10.0);

            var fact1 = new Fact { Id = "f1", Text = "Fact one" };
            var fact2 = new Fact { Id = "f2", Text = "Fact two" };
            var fact3 = new Fact { Id = "f3", Text = "Fact three" };

            var memorySame = new NpcRecalledMemory
            {
                EventId = "evt_1",
                Facts = new[] { fact1, fact2 },
                Hop = 0,
                SourceHeroId = null,
                LearnedDay = 10.0,
                Interest = 1.0,
                IsCorrection = false
            };
            var memoryNewVer = new NpcRecalledMemory
            {
                EventId = "evt_1",
                Facts = new[] { fact1, fact2, fact3 },
                Hop = 0,
                SourceHeroId = null,
                LearnedDay = 10.0,
                Interest = 1.0,
                IsCorrection = false
            };

            // Plan with same version -> excluded
            var planSame = AiPushPlanner.Plan("CalradiaRemembers", "hero_1", new[] { memorySame }, store, true, 2);
            Assert.Empty(planSame.ToPush);
            Assert.Single(planSame.Exclusions);
            Assert.Equal(AiPushExclusionReason.AlreadyPushedSameVersion, planSame.Exclusions[0].Reason);

            // Plan with new version -> included
            var planNew = AiPushPlanner.Plan("CalradiaRemembers", "hero_1", new[] { memoryNewVer }, store, true, 2);
            Assert.Single(planNew.ToPush);
            Assert.Empty(planNew.Exclusions);
            Assert.Equal("f1,f2,f3", planNew.ToPush[0].Version);
        }

        [Fact]
        public void AiPushPlanner_LimitsToConfiguredMax_AndExcludesSurplus()
        {
            var store = new AiPushedStore();
            var fact = new Fact { Id = "f1", Text = "Fact" };

            var memories = new List<NpcRecalledMemory>();
            for (int i = 0; i < 5; i++)
            {
                memories.Add(new NpcRecalledMemory
                {
                    EventId = $"evt_{i}",
                    Facts = new[] { fact },
                    Hop = 0,
                    SourceHeroId = null,
                    LearnedDay = 10.0,
                    Interest = 1.0,
                    IsCorrection = false
                });
            }

            var plan = AiPushPlanner.Plan("CalradiaRemembers", "hero_1", memories, store, true, maxPerChat: 2);

            Assert.Equal(2, plan.ToPush.Count);
            Assert.Equal(3, plan.Exclusions.Count);
            Assert.All(plan.Exclusions, ex => Assert.Equal(AiPushExclusionReason.ExceedsLimit, ex.Reason));
        }

        [Fact]
        public void TargetDescription_ImmersiveAI_MatchesSpecification()
        {
            var desc = AiTargetDescription.ImmersiveAI();

            Assert.Equal("ImmersiveAI", desc.TargetId);
            Assert.Equal("ImmersiveAI", desc.AssemblyName);
            Assert.Equal("ImmersiveAI.Interop.IaConversation", desc.ConversationTypeName);
            Assert.Equal("Started", desc.ConversationEventName);
            Assert.Equal("ImmersiveAI.Interop.IaNpcMemory", desc.MemoryTypeName);
            Assert.Equal("AddMemory", desc.AddMemoryMethodName);
            Assert.Equal("GetMemories", desc.GetMemoriesMethodName);
            Assert.Equal("ImmersiveAI.Interop.IaMemoryEntry", desc.MemoryEntryTypeName);
            Assert.Equal("ApiVersion", desc.ApiVersionMemberName);
            Assert.Equal(1, desc.AcceptedApiVersion);
            Assert.False(desc.IsPersistent);
        }

        [Fact]
        public void TargetValidator_ImmersiveAI_FailsGracefully_WhenAssemblyNotFound()
        {
            var desc = AiTargetDescription.ImmersiveAI();
            var result = AiTargetValidator.Validate(desc, null);

            Assert.False(result.IsEnabled);
            Assert.Equal(0, result.DetectedVersion);
            Assert.Contains("Assembly 'ImmersiveAI' not found", result.Reason);
        }

        [Fact]
        public void AiPushPlanner_WhenTargetNotPersistent_DoesNotExcludePreviouslyPushedSameVersion()
        {
            var store = new AiPushedStore();
            store.Record("ImmersiveAI", "hero_1", "evt_1", "f1,f2", 10.0);

            var fact1 = new Fact { Id = "f1", Text = "Fact one" };
            var fact2 = new Fact { Id = "f2", Text = "Fact two" };

            var memory = new NpcRecalledMemory
            {
                EventId = "evt_1",
                Facts = new[] { fact1, fact2 },
                Hop = 0,
                SourceHeroId = null,
                LearnedDay = 10.0,
                Interest = 1.0,
                IsCorrection = false
            };

            // Persistent target -> excluded
            var planPersistent = AiPushPlanner.Plan("ImmersiveAI", "hero_1", new[] { memory }, store, isPersistent: true, maxPerChat: 8);
            Assert.Empty(planPersistent.ToPush);
            Assert.Single(planPersistent.Exclusions);
            Assert.Equal(AiPushExclusionReason.AlreadyPushedSameVersion, planPersistent.Exclusions[0].Reason);

            // Chat-only (non-persistent) target -> NOT excluded, pushes again
            var planChatOnly = AiPushPlanner.Plan("ImmersiveAI", "hero_1", new[] { memory }, store, isPersistent: false, maxPerChat: 8);
            Assert.Single(planChatOnly.ToPush);
            Assert.Empty(planChatOnly.Exclusions);
            Assert.Equal("f1,f2", planChatOnly.ToPush[0].Version);
        }

        [Fact]
        public void AiPushPlanner_WhenTargetNotPersistent_LimitsToConfiguredChatOnlyMax()
        {
            var store = new AiPushedStore();
            var fact = new Fact { Id = "f1", Text = "Fact" };

            var memories = new List<NpcRecalledMemory>();
            for (int i = 0; i < 10; i++)
            {
                memories.Add(new NpcRecalledMemory
                {
                    EventId = $"evt_{i}",
                    Facts = new[] { fact },
                    Hop = 0,
                    SourceHeroId = null,
                    LearnedDay = 10.0,
                    Interest = 1.0,
                    IsCorrection = false
                });
            }

            var plan = AiPushPlanner.Plan("ImmersiveAI", "hero_1", memories, store, isPersistent: false, maxPerChat: 8);

            Assert.Equal(8, plan.ToPush.Count);
            Assert.Equal(2, plan.Exclusions.Count);
            Assert.All(plan.Exclusions, ex => Assert.Equal(AiPushExclusionReason.ExceedsLimit, ex.Reason));
        }

        [Fact]
        public void AiPushTruncator_Under300Chars_Unchanged()
        {
            var facts = new[]
            {
                new Fact { Id = "f1", Text = "Short fact 1" },
                new Fact { Id = "f2", Text = "Short fact 2" }
            };

            var result = AiPushTruncator.Truncate(facts, fList => string.Join("; ", fList.Select(f => f.Text)));

            Assert.False(result.IsTruncated);
            Assert.Equal(2, result.KeptFacts.Count);
            Assert.Equal("Short fact 1; Short fact 2", result.Text);
        }

        [Fact]
        public void AiPushTruncator_Over300Chars_DropsTrailingFactsSafely()
        {
            // 3 facts of 120 chars each -> 360 chars combined (> 300)
            var facts = new[]
            {
                new Fact { Id = "f1", Text = new string('A', 120) },
                new Fact { Id = "f2", Text = new string('B', 120) },
                new Fact { Id = "f3", Text = new string('C', 120) }
            };

            var result = AiPushTruncator.Truncate(facts, fList => string.Join(" ", fList.Select(f => f.Text)));

            Assert.True(result.IsTruncated);
            Assert.Equal(2, result.KeptFacts.Count);
            Assert.Equal(facts[0].Id, result.KeptFacts[0].Id);
            Assert.Equal(facts[1].Id, result.KeptFacts[1].Id);
            Assert.True(result.Text.Length <= 300);
        }

        [Fact]
        public void AiPushTruncator_SingleFactOver300Chars_KeptIntact()
        {
            var singleLongFact = new[]
            {
                new Fact { Id = "f1", Text = new string('X', 350) }
            };

            var result = AiPushTruncator.Truncate(singleLongFact, fList => fList[0].Text);

            Assert.True(result.IsTruncated);
            Assert.Single(result.KeptFacts);
            Assert.Equal(350, result.Text.Length);
        }

        [Fact]
        public void EnglishStringTable_ParsesXmlAndLookupsCorrectly()
        {
            string xml = @"<?xml version=""1.0"" encoding=""utf-8""?>
<base xmlns:xsi=""http://www.w3.org/2001/XMLSchema-instance"">
  <strings>
    <!-- Some comment -->
    <string id=""VividWorld_Fact_Sample"" text=""Sample fact text"" />
    <string id=""VividWorld_Fact_Another"" text=""Another fact text"" />
  </strings>
</base>";

            var table = EnglishStringTable.Parse(xml);

            Assert.Equal(2, table.Count);
            Assert.Equal("Sample fact text", table.Get("VividWorld_Fact_Sample"));
            Assert.Equal("Another fact text", table.Lookup("VividWorld_Fact_Another", "fallback"));
            Assert.Equal("fallback", table.Lookup("VividWorld_Fact_NonExistent", "fallback"));
            Assert.Equal(string.Empty, table.Lookup(null, null));
        }

        [Fact]
        public void RecallMemory_English_UsesStringTableWording_OverOldStoredLiteral()
        {
            // Simulate an event created with an older version where the stored fallback was different
            string textId = "VividWorld_Event_Duel_Outcome";
            string oldStoredText = "He was wounded in the duel.";
            string freshStringTableText = "He suffered serious injury in the duel.";

            var table = new EnglishStringTable(new Dictionary<string, string>
            {
                [textId] = freshStringTableText
            });

            var rumor = new ComposedRumor
            {
                Parts = new List<ComposedFactPart>
                {
                    new ComposedFactPart
                    {
                        TextId = textId,
                        Fallback = oldStoredText
                    }
                }
            };

            // Assembler with string table lookup (as configured in English recall mode)
            var result = RumorTextAssembler.Assemble(
                rumor,
                cfg: new PresentationConfig(),
                resolveVar: (v, links) => v,
                getTemplate: (id, fallback) => table.Lookup(id, fallback),
                getLocalized: null
            );

            Assert.Contains(freshStringTableText, result.PlainText);
            Assert.DoesNotContain(oldStoredText, result.PlainText);

            // Verify fallback when TextId is not found in string table
            var missingIdRumor = new ComposedRumor
            {
                Parts = new List<ComposedFactPart>
                {
                    new ComposedFactPart
                    {
                        TextId = string.Empty,
                        Fallback = "Fallback raw text"
                    }
                }
            };

            var fallbackResult = RumorTextAssembler.Assemble(
                missingIdRumor,
                cfg: new PresentationConfig(),
                resolveVar: (v, links) => v,
                getTemplate: (id, fallback) => table.Lookup(id, fallback),
                getLocalized: null
            );

            Assert.Contains("Fallback raw text", fallbackResult.PlainText);
        }
    }
}
