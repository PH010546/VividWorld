#nullable enable

using System;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using VividWorld.Core.Dialogue;
using VividWorld.Core.Presentation;
using Xunit;

namespace VividWorld.Core.Tests
{
    public class AskRefusalLineTests
    {
        [Fact]
        public void AskRefusalLine_Choose_NullDecision_ReturnsOther()
        {
            var result = AskRefusalLine.Choose(null, 1, 0, 0);
            Assert.Equal(AskRefusalLineKind.Other, result);
        }

        [Fact]
        public void AskRefusalLine_Choose_HasOffer_ReturnsOther()
        {
            var decision = new AskDecision
            {
                Refusal = AskRefusal.None,
                Offer = new RumorOffer { EventId = "evt_1", Composed = new ComposedRumor() }
            };
            var result = AskRefusalLine.Choose(decision, 1, 0, 0);
            Assert.Equal(AskRefusalLineKind.Other, result);
        }

        [Fact]
        public void AskRefusalLine_Choose_RefusalNone_WithoutOffer_ReturnsOther()
        {
            var decision = new AskDecision
            {
                Refusal = AskRefusal.None,
                Offer = null
            };
            var result = AskRefusalLine.Choose(decision, 1, 0, 0);
            Assert.Equal(AskRefusalLineKind.Other, result);
        }

        [Fact]
        public void AskRefusalLine_Choose_RelationGate_ReturnsUnwilling()
        {
            var decision = new AskDecision
            {
                Refusal = AskRefusal.RelationGate
            };
            var result = AskRefusalLine.Choose(decision, 5, 0, 0);
            Assert.Equal(AskRefusalLineKind.Unwilling, result);
        }

        [Fact]
        public void AskRefusalLine_Choose_WillingnessGate_ReturnsUnwilling()
        {
            var decision = new AskDecision
            {
                Refusal = AskRefusal.WillingnessGate
            };
            var result = AskRefusalLine.Choose(decision, 5, 0, 0);
            Assert.Equal(AskRefusalLineKind.Unwilling, result);
        }

        [Fact]
        public void AskRefusalLine_Choose_NoKnownEvents_NothingOnFile_ReturnsNothingHeard()
        {
            var decision = new AskDecision
            {
                Refusal = AskRefusal.NoKnownEvents
            };
            var result = AskRefusalLine.Choose(decision, knownCount: 0, forgottenCount: 0, outdatedCount: 0);
            Assert.Equal(AskRefusalLineKind.NothingHeard, result);
        }

        [Fact]
        public void AskRefusalLine_Choose_NoKnownEvents_AllForgotten_ReturnsForgotten()
        {
            var decision = new AskDecision
            {
                Refusal = AskRefusal.NoKnownEvents
            };
            var result = AskRefusalLine.Choose(decision, knownCount: 3, forgottenCount: 3, outdatedCount: 0);
            Assert.Equal(AskRefusalLineKind.Forgotten, result);
        }

        [Fact]
        public void AskRefusalLine_Choose_NoKnownEvents_ForgottenOrOutdated_ReturnsForgotten()
        {
            // ForgottenOrOutdated => Forgotten (spec §11.2)
            var decision = new AskDecision
            {
                Refusal = AskRefusal.NoKnownEvents
            };
            var result = AskRefusalLine.Choose(decision, knownCount: 4, forgottenCount: 2, outdatedCount: 2);
            Assert.Equal(AskRefusalLineKind.Forgotten, result);
        }

        [Fact]
        public void AskRefusalLine_Choose_NoKnownEvents_AllOutdated_ReturnsOutdated()
        {
            var decision = new AskDecision
            {
                Refusal = AskRefusal.NoKnownEvents
            };
            var result = AskRefusalLine.Choose(decision, knownCount: 2, forgottenCount: 0, outdatedCount: 2);
            Assert.Equal(AskRefusalLineKind.Outdated, result);
        }

        [Fact]
        public void AskRefusalLine_Choose_AllCandidatesFiltered_OnlySecret_ReturnsOther()
        {
            // 只有秘密 => Other（秘密不准有專屬句，免得洩漏）
            var decision = new AskDecision
            {
                Refusal = AskRefusal.AllCandidatesFiltered,
                CandidateCount = 1,
                FilteredNotVisible = 1,
                FilteredFutureTimeline = 0,
                FilteredPlayerKnows = 0,
                FilteredOther = 0
            };
            var result = AskRefusalLine.Choose(decision, 1, 0, 0);
            Assert.Equal(AskRefusalLineKind.Other, result);
        }

        [Fact]
        public void AskRefusalLine_Choose_AllCandidatesFiltered_OnlyFuture_ReturnsOther()
        {
            // 只有未來事件 => Other
            var decision = new AskDecision
            {
                Refusal = AskRefusal.AllCandidatesFiltered,
                CandidateCount = 1,
                FilteredNotVisible = 0,
                FilteredFutureTimeline = 1,
                FilteredPlayerKnows = 0,
                FilteredOther = 0
            };
            var result = AskRefusalLine.Choose(decision, 1, 0, 0);
            Assert.Equal(AskRefusalLineKind.Other, result);
        }

        [Fact]
        public void AskRefusalLine_Choose_AllCandidatesFiltered_SecretAndPlayerKnows_ReturnsPlayerKnowsAll()
        {
            // 秘密＋玩家已知 => PlayerKnowsAll (FilteredPlayerKnows > 0)
            var decision = new AskDecision
            {
                Refusal = AskRefusal.AllCandidatesFiltered,
                CandidateCount = 2,
                FilteredNotVisible = 1,
                FilteredFutureTimeline = 0,
                FilteredPlayerKnows = 1,
                FilteredOther = 0
            };
            var result = AskRefusalLine.Choose(decision, 2, 0, 0);
            Assert.Equal(AskRefusalLineKind.PlayerKnowsAll, result);
        }

        [Fact]
        public void AskRefusalLine_Choose_AllCandidatesFiltered_OnlyPlayerKnows_ReturnsPlayerKnowsAll()
        {
            var decision = new AskDecision
            {
                Refusal = AskRefusal.AllCandidatesFiltered,
                CandidateCount = 2,
                FilteredNotVisible = 0,
                FilteredFutureTimeline = 0,
                FilteredPlayerKnows = 2,
                FilteredOther = 0
            };
            var result = AskRefusalLine.Choose(decision, 2, 0, 0);
            Assert.Equal(AskRefusalLineKind.PlayerKnowsAll, result);
        }

        [Fact]
        public void AskRefusalLine_Choose_AllCandidatesFiltered_OnlyOther_ReturnsOther()
        {
            var decision = new AskDecision
            {
                Refusal = AskRefusal.AllCandidatesFiltered,
                CandidateCount = 1,
                FilteredNotVisible = 0,
                FilteredFutureTimeline = 0,
                FilteredPlayerKnows = 0,
                FilteredOther = 1
            };
            var result = AskRefusalLine.Choose(decision, 1, 0, 0);
            Assert.Equal(AskRefusalLineKind.Other, result);
        }

        [Fact]
        public void AskRefusalLine_GetStringKey_And_GetEnglishFallback_MatchesAllKinds()
        {
            Assert.Equal("VividWorld_AskRefuseUnwilling", AskRefusalLine.GetStringKey(AskRefusalLineKind.Unwilling));
            Assert.Equal("That's not something I'd care to discuss.", AskRefusalLine.GetEnglishFallback(AskRefusalLineKind.Unwilling));

            Assert.Equal("VividWorld_AskRefuseNothingHeard", AskRefusalLine.GetStringKey(AskRefusalLineKind.NothingHeard));
            Assert.Equal("I haven't heard any news lately.", AskRefusalLine.GetEnglishFallback(AskRefusalLineKind.NothingHeard));

            Assert.Equal("VividWorld_AskRefuseForgotten", AskRefusalLine.GetStringKey(AskRefusalLineKind.Forgotten));
            Assert.Equal("Someone mentioned something... I can't quite recall what.", AskRefusalLine.GetEnglishFallback(AskRefusalLineKind.Forgotten));

            Assert.Equal("VividWorld_AskRefuseOutdated", AskRefusalLine.GetStringKey(AskRefusalLineKind.Outdated));
            Assert.Equal("Whatever I heard is likely no longer the case.", AskRefusalLine.GetEnglishFallback(AskRefusalLineKind.Outdated));

            Assert.Equal("VividWorld_AskRefusePlayerKnows", AskRefusalLine.GetStringKey(AskRefusalLineKind.PlayerKnowsAll));
            Assert.Equal("Whatever I know, you've surely heard already.", AskRefusalLine.GetEnglishFallback(AskRefusalLineKind.PlayerKnowsAll));

            Assert.Null(AskRefusalLine.GetStringKey(AskRefusalLineKind.Other));
            Assert.Null(AskRefusalLine.GetEnglishFallback(AskRefusalLineKind.Other));
        }

        [Fact]
        public void AskRefusalLine_FormatRefusedLog_MatchesSpecFormat()
        {
            var decision = new AskDecision
            {
                Refusal = AskRefusal.RelationGate,
                FilteredNotVisible = 1,
                FilteredFutureTimeline = 2,
                FilteredPlayerKnows = 3,
                FilteredOther = 4
            };

            string line = AskRefusalLine.FormatRefusedLog(
                "Derthert", "lord_derthert", "VividWorld_AskRefuseUnwilling", decision,
                knownCount: 10, forgottenCount: 5, outdatedCount: 2);

            Assert.Equal(
                "Ask refused line: Derthert (lord_derthert) -> VividWorld_AskRefuseUnwilling (refusal RelationGate, known 10, forgotten 5, outdated 2, filtered notVisible 1 / future 2 / playerKnows 3 / other 4)",
                line);
        }

        [Fact]
        public void AskRefusalLine_FormatFallbackReason_OfferRenderedEmpty_MatchesCardFormat()
        {
            string reason = AskRefusalLine.FormatFallbackReason(null, offerRenderedEmpty: true);
            Assert.Equal("offer rendered empty", reason);

            string log = AskRefusalLine.FormatFallbackLog("Bob", "hero_bob", reason);
            Assert.Equal("Ask fallback line: Bob (hero_bob) - offer rendered empty", log);
        }

        [Fact]
        public void AskRefusalLine_FormatFallbackReason_NoDedicatedLine_MatchesCardFormat()
        {
            var decision = new AskDecision
            {
                Refusal = AskRefusal.AllCandidatesFiltered,
                FilteredNotVisible = 2,
                FilteredFutureTimeline = 1,
                FilteredPlayerKnows = 0,
                FilteredOther = 0
            };

            string reason = AskRefusalLine.FormatFallbackReason(decision, offerRenderedEmpty: false);
            Assert.Equal(
                "no dedicated line (refusal AllCandidatesFiltered, filtered notVisible 2 / future 1 / playerKnows 0 / other 0)",
                reason);

            string log = AskRefusalLine.FormatFallbackLog("Bob", "hero_bob", reason);
            Assert.Equal(
                "Ask fallback line: Bob (hero_bob) - no dedicated line (refusal AllCandidatesFiltered, filtered notVisible 2 / future 1 / playerKnows 0 / other 0)",
                log);
        }

        [Fact]
        public void AskRefusalLine_LocalizationKeys_ExistInEnglishAndCNtTables_WithExactText()
        {
            string repoRoot = FindRepoRoot();
            string enPath = Path.Combine(repoRoot, "module", "ModuleData", "Languages", "std_module_strings_xml.xml");
            string cntPath = Path.Combine(repoRoot, "module", "ModuleData", "Languages", "CNt", "std_module_strings_xml.xml");

            Assert.True(File.Exists(enPath), $"English XML not found at {enPath}");
            Assert.True(File.Exists(cntPath), $"CNt XML not found at {cntPath}");

            var enDoc = XDocument.Load(enPath);
            var cntDoc = XDocument.Load(cntPath);

            var enDict = enDoc.Descendants("string").ToDictionary(x => (string)x.Attribute("id")!, x => (string)x.Attribute("text")!);
            var cntDict = cntDoc.Descendants("string").ToDictionary(x => (string)x.Attribute("id")!, x => (string)x.Attribute("text")!);

            // 1. Unwilling
            Assert.Equal("That's not something I'd care to discuss.", enDict["VividWorld_AskRefuseUnwilling"]);
            Assert.Equal("這種事，我不方便多說。", cntDict["VividWorld_AskRefuseUnwilling"]);

            // 2. NothingHeard
            Assert.Equal("I haven't heard any news lately.", enDict["VividWorld_AskRefuseNothingHeard"]);
            Assert.Equal("最近沒聽到什麼消息。", cntDict["VividWorld_AskRefuseNothingHeard"]);

            // 3. Forgotten
            Assert.Equal("Someone mentioned something... I can't quite recall what.", enDict["VividWorld_AskRefuseForgotten"]);
            Assert.Equal("好像聽誰提過些什麼……一時想不起來了。", cntDict["VividWorld_AskRefuseForgotten"]);

            // 4. Outdated
            Assert.Equal("Whatever I heard is likely no longer the case.", enDict["VividWorld_AskRefuseOutdated"]);
            Assert.Equal("我聽說的那些，現在大概都不是那麼回事了。", cntDict["VividWorld_AskRefuseOutdated"]);

            // 5. PlayerKnowsAll
            Assert.Equal("Whatever I know, you've surely heard already.", enDict["VividWorld_AskRefusePlayerKnows"]);
            Assert.Equal("我知道的，你應該都聽說了。", cntDict["VividWorld_AskRefusePlayerKnows"]);
        }

        private static string FindRepoRoot()
        {
            string dir = AppContext.BaseDirectory;
            while (!string.IsNullOrEmpty(dir))
            {
                if (Directory.Exists(Path.Combine(dir, "module", "ModuleData", "Languages")))
                {
                    return dir;
                }
                dir = Path.GetDirectoryName(dir) ?? string.Empty;
            }
            throw new DirectoryNotFoundException("Could not locate repository root with module/ModuleData/Languages");
        }
    }
}
