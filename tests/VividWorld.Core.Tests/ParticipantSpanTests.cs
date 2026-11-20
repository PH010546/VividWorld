using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using VividWorld.Core.Config;
using VividWorld.Core.Memory;
using VividWorld.Core.Persistence;
using Xunit;

namespace VividWorld.Core.Tests
{
    public class ParticipantSpanTests
    {
        private static MemoryConfig NewMemoryConfig(double participantMultiplier = 4.0)
        {
            return new MemoryConfig
            {
                BaseDays = 60.0,
                DramaReference = 4,
                MinDays = 1.0,
                ReinforceGrowth = 1.5,
                ReinforceMaxMultiplier = 3.0,
                ParticipantSpanMultiplier = participantMultiplier
            };
        }

        [Fact]
        public void Default_ParticipantSpanMultiplier_IsFour()
        {
            Assert.Equal(4.0, new VividWorldConfig().Memory.ParticipantSpanMultiplier);
        }

        [Fact]
        public void Participant_DaysAreFourTimesNonParticipant_SameInterestDramaAndHeardCount()
        {
            var cfg = NewMemoryConfig();

            foreach (int drama in new[] { 1, 3, 5 })
            {
                foreach (int heard in new[] { 0, 1, 4 })
                {
                    var outsider = MemorySpan.Compute(10.0, 0.5, drama, heard, null, cfg, knowerIsParticipant: false);
                    var party = MemorySpan.Compute(10.0, 0.5, drama, heard, null, cfg, knowerIsParticipant: true);

                    Assert.Equal(4.0 * outsider.Days, party.Days, 9);
                    Assert.Equal(4.0 * outsider.Raw, party.Raw, 9);
                    Assert.Equal(4.0, party.ParticipantMultiplier);
                }
            }
        }

        [Fact]
        public void Participant_ForgetDay_IsStartPlusMultipliedDays()
        {
            var cfg = NewMemoryConfig();
            // 同興趣、同份量、沒聽過：非當事人 30 天，當事人 120 天
            var outsider = MemorySpan.Compute(10.0, 0.5, 4, 0, null, cfg);
            var party = MemorySpan.Compute(10.0, 0.5, 4, 0, null, cfg, knowerIsParticipant: true);

            Assert.Equal(30.0, outsider.Days, 9);
            Assert.Equal(40.0, outsider.ForgetDay, 9);
            Assert.Equal(120.0, party.Days, 9);
            Assert.Equal(130.0, party.ForgetDay, 9);
        }

        [Fact]
        public void NonParticipant_OverloadsWithoutFlag_StayUnchanged()
        {
            var cfg = NewMemoryConfig();
            var legacy = MemorySpan.Compute(10.0, 0.5, 4, 2, 999.0, cfg);
            var explicitOutsider = MemorySpan.Compute(10.0, 0.5, 4, 2, 999.0, cfg, knowerIsParticipant: false);
            var legacyOverload = MemorySpan.Compute(0.5, 4, 10.0, 2, cfg, 999.0);

            Assert.Equal(1.0, legacy.ParticipantMultiplier);
            Assert.Equal(legacy.Days, explicitOutsider.Days, 12);
            Assert.Equal(legacy.ForgetDay, explicitOutsider.ForgetDay, 12);
            Assert.Equal(legacy.Days, legacyOverload.Days, 12);
            Assert.Equal(legacy.ForgetDay, legacyOverload.ForgetDay, 12);
        }

        [Fact]
        public void MultiplierOfOne_ParticipantMatchesNonParticipantExactly()
        {
            var cfg = NewMemoryConfig(participantMultiplier: 1.0);
            var outsider = MemorySpan.Compute(10.0, 0.7, 3, 2, 50.0, cfg);
            var party = MemorySpan.Compute(10.0, 0.7, 3, 2, 50.0, cfg, knowerIsParticipant: true);

            Assert.Equal(1.0, party.ParticipantMultiplier);
            Assert.Equal(outsider.Raw, party.Raw);
            Assert.Equal(outsider.Days, party.Days);
            Assert.Equal(outsider.Candidate, party.Candidate);
            Assert.Equal(outsider.ForgetDay, party.ForgetDay);
            Assert.Equal(outsider.KeptOld, party.KeptOld);
        }

        [Fact]
        public void HalfAndNegativeMultiplier_ReadAsOne_WhenConfigIsLoaded()
        {
            foreach (double bad in new[] { 0.5, -3.0, double.NaN })
            {
                var config = new VividWorldConfig();
                config.Memory.ParticipantSpanMultiplier = bad;
                var notices = new List<ClampNotice>();

                config.Normalize(notices);

                Assert.Equal(1.0, config.Memory.ParticipantSpanMultiplier);
                // 每次載入最多一則警告
                Assert.Single(notices, n => n.Key == "memory.participantSpanMultiplier");
                Assert.Equal(1.0, notices.Find(n => n.Key == "memory.participantSpanMultiplier")!.Applied);
            }
        }

        [Fact]
        public void HalfMultiplier_AfterLoad_GivesSameDaysAsNonParticipant()
        {
            var config = new VividWorldConfig();
            config.Memory.ParticipantSpanMultiplier = 0.5;
            config.Normalize();

            var cfg = config.Memory;
            var outsider = MemorySpan.Compute(10.0, 0.5, 4, 0, null, cfg);
            var party = MemorySpan.Compute(10.0, 0.5, 4, 0, null, cfg, knowerIsParticipant: true);

            Assert.Equal(outsider.Days, party.Days, 12);
            Assert.Equal(outsider.ForgetDay, party.ForgetDay, 12);
        }

        [Fact]
        public void ValidMultiplier_IsKept_WithoutNotice()
        {
            var config = new VividWorldConfig();
            config.Memory.ParticipantSpanMultiplier = 2.5;
            var notices = new List<ClampNotice>();

            config.Normalize(notices);

            Assert.Equal(2.5, config.Memory.ParticipantSpanMultiplier);
            Assert.DoesNotContain(notices, n => n.Key == "memory.participantSpanMultiplier");
        }

        [Fact]
        public void Participant_StillRaisedToMinDays()
        {
            var cfg = new MemoryConfig
            {
                BaseDays = 1.0,
                DramaReference = 4,
                MinDays = 1.0,
                ReinforceGrowth = 1.5,
                ReinforceMaxMultiplier = 3.0,
                ParticipantSpanMultiplier = 4.0
            };

            // 非當事人 raw = 1 × 0.1 × 0.25 = 0.025，當事人 0.1，都低於最少 1 天
            var outsider = MemorySpan.Compute(0.0, 0.1, 1, 0, null, cfg);
            var party = MemorySpan.Compute(0.0, 0.1, 1, 0, null, cfg, knowerIsParticipant: true);

            Assert.True(party.RaisedToMin);
            Assert.Equal(1.0, party.Days, 9);
            Assert.Equal(outsider.Days, party.Days, 9);
            Assert.Equal(0.1, party.Raw, 9);
        }

        [Fact]
        public void Participant_MaxWithOldForgetDay_KeepsLaterOne()
        {
            var cfg = NewMemoryConfig();

            // 舊忘記日子 500 比新算的 130 晚：取 500，記為保留舊的
            var kept = MemorySpan.Compute(10.0, 0.5, 4, 0, 500.0, cfg, knowerIsParticipant: true);
            Assert.Equal(130.0, kept.Candidate, 9);
            Assert.Equal(500.0, kept.ForgetDay, 9);
            Assert.True(kept.KeptOld);

            // 舊忘記日子 50 比較早：取新算的 130
            var replaced = MemorySpan.Compute(10.0, 0.5, 4, 0, 50.0, cfg, knowerIsParticipant: true);
            Assert.Equal(130.0, replaced.ForgetDay, 9);
            Assert.False(replaced.KeptOld);
        }

        [Fact]
        public void IsParticipant_OnlyTrueForAHeroInParticipants()
        {
            var participants = new Dictionary<string, string>
            {
                ["prisoner"] = "lord_a",
                ["captor"] = "lord_b"
            };

            Assert.True(MemorySpan.IsParticipant(participants, "lord_a"));
            Assert.True(MemorySpan.IsParticipant(participants, "lord_b"));
            Assert.False(MemorySpan.IsParticipant(participants, "bystander"));
            Assert.False(MemorySpan.IsParticipant(participants, ""));
            Assert.False(MemorySpan.IsParticipant(null, "lord_a"));
            Assert.False(MemorySpan.IsParticipant(new Dictionary<string, string>(), "lord_a"));
        }

        [Fact]
        public void Log_NonParticipant_LineHasNoParticipantPart()
        {
            var span = new MemorySpanResult
            {
                Reinforce = 1.0,
                Raw = 30.0,
                Days = 30.0,
                Candidate = 30.0,
                ForgetDay = 30.0
            };
            var interest = new InterestResult { Interest = 0.5, KnowerFound = false };

            string line = MemoryLogFormatter.FormatCalculation("lord_a", "learned", "ev1", interest, 2, 4, 60.0, span, null!);

            Assert.Equal(
                "Memory: lord_a learned ev1 - interest 0.50 via (knower not found) x drama 2/4 x base 60.0d x reinforce 1.00 = 30.0d -> forgets on day 30.0",
                line);
        }

        [Fact]
        public void Log_Participant_ShowsMultiplierInDayFormula()
        {
            var span = new MemorySpanResult
            {
                Reinforce = 1.0,
                ParticipantMultiplier = 4.0,
                Raw = 120.0,
                Days = 120.0,
                Candidate = 130.0,
                ForgetDay = 130.0
            };
            var interest = new InterestResult { Interest = 0.5, KnowerFound = false };

            string line = MemoryLogFormatter.FormatCalculation("lord_a", "learned", "ev1", interest, 2, 4, 60.0, span, null!);

            Assert.Equal(
                "Memory: lord_a learned ev1 - interest 0.50 via (knower not found) x drama 2/4 x base 60.0d x reinforce 1.00 x participant 4.00 = 120.0d -> forgets on day 130.0",
                line);
        }

        [Fact]
        public void Config_MergeIntoOldFile_AddsMultiplierAndKeepsExistingValues()
        {
            var existing = JObject.Parse("{ \"configVersion\": 2, \"memory\": { \"baseDays\": 30 } }");
            var canonical = JObject.Parse(VividJson.Write(new VividWorldConfig().Normalize()));

            var result = ConfigMerge.AddMissingKeys(existing, canonical);

            Assert.Contains("memory.participantSpanMultiplier", result.AddedPaths);
            Assert.Equal(4.0, (double)result.Merged.SelectToken("memory.participantSpanMultiplier")!);
            Assert.Equal(30, (double)result.Merged.SelectToken("memory.baseDays")!);
            Assert.DoesNotContain("memory.baseDays", result.AddedPaths);
        }

        [Fact]
        public void Config_OldFileWithoutKey_ReadsAsFourAndKeepsOtherMemoryValues()
        {
            var cfg = VividJson.Read<VividWorldConfig>("{ \"configVersion\": 2, \"memory\": { \"baseDays\": 30, \"minDays\": 2 } }");

            Assert.NotNull(cfg);
            Assert.Equal(4.0, cfg!.Memory.ParticipantSpanMultiplier);
            Assert.Equal(30.0, cfg.Memory.BaseDays);
            Assert.Equal(2.0, cfg.Memory.MinDays);
        }
    }
}
