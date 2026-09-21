using System;
using System.Collections.Generic;
using VividWorld.Core.Ingest;
using Xunit;

namespace VividWorld.Core.Tests
{
    public class SettlementFallbackTests
    {
        [Fact]
        public void Resolve_FirstNonEmptyHits_SubsequentProbesNotCalled()
        {
            int probe1Calls = 0;
            int probe2Calls = 0;

            var probes = new[]
            {
                new SettlementProbe("probe1", () =>
                {
                    probe1Calls++;
                    return "town_A1";
                }),
                new SettlementProbe("probe2", () =>
                {
                    probe2Calls++;
                    return "town_B2";
                })
            };

            var result = SettlementFallback.Resolve(probes);

            Assert.Equal("town_A1", result.SettlementId);
            Assert.Equal("probe1", result.Via);
            Assert.Equal(1, probe1Calls);
            Assert.Equal(0, probe2Calls);
            Assert.Empty(result.Misses);
        }

        [Fact]
        public void Resolve_AllNull_ReturnsMiss_WithAllMissesInOrder()
        {
            var probes = new[]
            {
                new SettlementProbe("victim.CurrentSettlement", () => null),
                new SettlementProbe("victim.GetClosestSettlement", () => null),
                new SettlementProbe("killer.CurrentSettlement", () => null),
                new SettlementProbe("killer.GetClosestSettlement", () => null)
            };

            var result = SettlementFallback.Resolve(probes);

            Assert.Null(result.SettlementId);
            Assert.Null(result.Via);
            Assert.Equal(4, result.Misses.Count);
            Assert.Equal("victim.CurrentSettlement=null", result.Misses[0]);
            Assert.Equal("victim.GetClosestSettlement=null", result.Misses[1]);
            Assert.Equal("killer.CurrentSettlement=null", result.Misses[2]);
            Assert.Equal("killer.GetClosestSettlement=null", result.Misses[3]);
        }

        [Fact]
        public void Resolve_WhitespaceString_TreatedAsNull()
        {
            var probes = new[]
            {
                new SettlementProbe("empty", () => ""),
                new SettlementProbe("whitespace", () => "   \t\n"),
                new SettlementProbe("valid", () => "town_A1")
            };

            var result = SettlementFallback.Resolve(probes);

            Assert.Equal("town_A1", result.SettlementId);
            Assert.Equal("valid", result.Via);
            Assert.Equal(2, result.Misses.Count);
            Assert.Equal("empty=null", result.Misses[0]);
            Assert.Equal("whitespace=null", result.Misses[1]);
        }

        [Fact]
        public void Resolve_ProbeThrows_RecordsThrewTypeName_AndContinues()
        {
            var probes = new[]
            {
                new SettlementProbe("prisoner.GetClosestSettlement", () => throw new NullReferenceException()),
                new SettlementProbe("captor.CurrentSettlement", () => throw new InvalidOperationException("failed")),
                new SettlementProbe("captor.GetClosestSettlement", () => null)
            };

            var result = SettlementFallback.Resolve(probes);

            Assert.Null(result.SettlementId);
            Assert.Equal(3, result.Misses.Count);
            Assert.Equal("prisoner.GetClosestSettlement threw NullReferenceException", result.Misses[0]);
            Assert.Equal("captor.CurrentSettlement threw InvalidOperationException", result.Misses[1]);
            Assert.Equal("captor.GetClosestSettlement=null", result.Misses[2]);
        }

        [Fact]
        public void Resolve_ProbeHitsAfterException_StillSucceeds()
        {
            var probes = new[]
            {
                new SettlementProbe("broken", () => throw new InvalidOperationException()),
                new SettlementProbe("working", () => "castle_B2")
            };

            var result = SettlementFallback.Resolve(probes);

            Assert.Equal("castle_B2", result.SettlementId);
            Assert.Equal("working", result.Via);
            Assert.Single(result.Misses);
            Assert.Equal("broken threw InvalidOperationException", result.Misses[0]);
        }

        [Fact]
        public void Resolve_EmptyProbesList_ReturnsMiss_WithEmptyMisses()
        {
            var result = SettlementFallback.Resolve(Array.Empty<SettlementProbe>());

            Assert.Null(result.SettlementId);
            Assert.Null(result.Via);
            Assert.Empty(result.Misses);
            Assert.Equal("SETTLEMENT unbound", result.Describe());
        }

        [Fact]
        public void Describe_MatchesDocumentedFormats_ForNoMissHit_MissedHit_AndAllMissed()
        {
            // 1. 命中、前面沒有落空：SETTLEMENT=town_A1 via victim.CurrentSettlement
            var res1 = new SettlementFallbackResult("town_A1", "victim.CurrentSettlement", Array.Empty<string>());
            Assert.Equal("SETTLEMENT=town_A1 via victim.CurrentSettlement", res1.Describe());

            // 2. 命中、前面有落空：SETTLEMENT=village_EN4_2 via killer.GetClosestSettlement (missed: victim.CurrentSettlement=null, victim.GetClosestSettlement=null)
            var res2 = new SettlementFallbackResult("village_EN4_2", "killer.GetClosestSettlement", new[]
            {
                "victim.CurrentSettlement=null",
                "victim.GetClosestSettlement=null"
            });
            Assert.Equal("SETTLEMENT=village_EN4_2 via killer.GetClosestSettlement (missed: victim.CurrentSettlement=null, victim.GetClosestSettlement=null)", res2.Describe());

            // 3. 全部落空：SETTLEMENT unbound (missed: victim.CurrentSettlement=null, victim.GetClosestSettlement=null, killer.CurrentSettlement=null, killer.GetClosestSettlement=null)
            var res3 = new SettlementFallbackResult(null, null, new[]
            {
                "victim.CurrentSettlement=null",
                "victim.GetClosestSettlement=null",
                "killer.CurrentSettlement=null",
                "killer.GetClosestSettlement=null"
            });
            Assert.Equal("SETTLEMENT unbound (missed: victim.CurrentSettlement=null, victim.GetClosestSettlement=null, killer.CurrentSettlement=null, killer.GetClosestSettlement=null)", res3.Describe());

            // 4. 探針丟例外：... (missed: prisoner.GetClosestSettlement threw NullReferenceException, ...)
            var res4 = new SettlementFallbackResult("town_A1", "captor.CurrentSettlement", new[]
            {
                "prisoner.GetClosestSettlement threw NullReferenceException"
            });
            Assert.Equal("SETTLEMENT=town_A1 via captor.CurrentSettlement (missed: prisoner.GetClosestSettlement threw NullReferenceException)", res4.Describe());
        }
    }
}
