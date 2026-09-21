using System;
using VividWorld.Core.Catalog;
using VividWorld.Core.Config;
using Xunit;

namespace VividWorld.Core.Tests
{
    public class PrisonerProminenceTests
    {
        private readonly ProminenceDramaConfig _defaultCfg = new();

        [Fact]
        public void Classify_Ruler_WhenIsKingdomLeader()
        {
            var facts = new ProminenceFacts
            {
                HeroId = "lord_1_1",
                IsKingdomLeader = true,
                KingdomId = "empire",
                IsClanLeader = true,
                ClanId = "clan_empire_1",
                IsLord = true
            };

            var result = PrisonerProminence.Classify(facts, _defaultCfg);

            Assert.Equal(ProminenceTier.Ruler, result.Tier);
            Assert.Equal(5, result.Drama);
            Assert.Equal("leader of kingdom empire", result.Reason);
            Assert.Equal("prisoner=lord_1_1 -> ruler (leader of kingdom empire) => drama 5 (template 4)", result.Describe(4));
        }

        [Fact]
        public void Classify_ClanLeader_WhenIsClanLeader()
        {
            var facts = new ProminenceFacts
            {
                HeroId = "lord_4_15",
                IsKingdomLeader = false,
                IsClanLeader = true,
                ClanId = "clan_empire_north_3",
                IsLord = true
            };

            var result = PrisonerProminence.Classify(facts, _defaultCfg);

            Assert.Equal(ProminenceTier.ClanLeader, result.Tier);
            Assert.Equal(4, result.Drama);
            Assert.Equal("leader of clan clan_empire_north_3", result.Reason);
            Assert.Equal("prisoner=lord_4_15 -> clanLeader (leader of clan clan_empire_north_3) => drama 4 (template 4)", result.Describe(4));
        }

        [Fact]
        public void Classify_NobleMember_WhenRegularLord()
        {
            var facts = new ProminenceFacts
            {
                HeroId = "lord_5_21",
                IsKingdomLeader = false,
                IsClanLeader = false,
                ClanId = "clan_vlandia_7",
                ClanIsMinorFaction = false,
                IsLord = true
            };

            var result = PrisonerProminence.Classify(facts, _defaultCfg);

            Assert.Equal(ProminenceTier.NobleMember, result.Tier);
            Assert.Equal(2, result.Drama);
            Assert.Equal("lord of clan clan_vlandia_7", result.Reason);
            Assert.Equal("prisoner=lord_5_21 -> nobleMember (lord of clan clan_vlandia_7) => drama 2 (template 4)", result.Describe(4));
        }

        [Fact]
        public void Classify_Minor_WhenNoClan()
        {
            var facts = new ProminenceFacts
            {
                HeroId = "CharacterObject_2829",
                IsKingdomLeader = false,
                IsClanLeader = false,
                ClanId = null,
                ClanIsMinorFaction = false,
                IsLord = false
            };

            var result = PrisonerProminence.Classify(facts, _defaultCfg);

            Assert.Equal(ProminenceTier.Minor, result.Tier);
            Assert.Equal(1, result.Drama);
            Assert.Equal("no clan", result.Reason);
            Assert.Equal("prisoner=CharacterObject_2829 -> minor (no clan) => drama 1 (template 4)", result.Describe(4));
        }

        [Fact]
        public void Classify_Minor_WhenClanIsMinorFaction()
        {
            var facts = new ProminenceFacts
            {
                HeroId = "CharacterObject_4096",
                IsKingdomLeader = false,
                IsClanLeader = false,
                ClanId = "clan_minor_2",
                ClanIsMinorFaction = true,
                IsLord = true
            };

            var result = PrisonerProminence.Classify(facts, _defaultCfg);

            Assert.Equal(ProminenceTier.Minor, result.Tier);
            Assert.Equal(1, result.Drama);
            Assert.Equal("clan clan_minor_2 is a minor faction", result.Reason);
            Assert.Equal("prisoner=CharacterObject_4096 -> minor (clan clan_minor_2 is a minor faction) => drama 1 (template 4)", result.Describe(4));
        }

        [Fact]
        public void Classify_Minor_WhenNotALord()
        {
            var facts = new ProminenceFacts
            {
                HeroId = "CharacterObject_4111",
                IsKingdomLeader = false,
                IsClanLeader = false,
                ClanId = "player_faction",
                ClanIsMinorFaction = false,
                IsLord = false
            };

            var result = PrisonerProminence.Classify(facts, _defaultCfg);

            Assert.Equal(ProminenceTier.Minor, result.Tier);
            Assert.Equal(1, result.Drama);
            Assert.Equal("not a lord, clan player_faction", result.Reason);
            Assert.Equal("prisoner=CharacterObject_4111 -> minor (not a lord, clan player_faction) => drama 1 (template 4)", result.Describe(4));
        }

        [Fact]
        public void Classify_Ruler_WhenBothKingdomAndClanLeader()
        {
            var facts = new ProminenceFacts
            {
                HeroId = "lord_1_1",
                IsKingdomLeader = true,
                KingdomId = "empire",
                IsClanLeader = true,
                ClanId = "clan_empire_1",
                IsLord = true
            };

            var result = PrisonerProminence.Classify(facts, _defaultCfg);

            Assert.Equal(ProminenceTier.Ruler, result.Tier);
            Assert.Equal(5, result.Drama);
        }

        [Fact]
        public void Classify_RespectsCustomConfigDrama()
        {
            var customCfg = new ProminenceDramaConfig
            {
                NobleMember = 3
            };

            var facts = new ProminenceFacts
            {
                HeroId = "lord_5_21",
                ClanId = "clan_vlandia_7",
                IsLord = true
            };

            var result = PrisonerProminence.Classify(facts, customCfg);

            Assert.Equal(ProminenceTier.NobleMember, result.Tier);
            Assert.Equal(3, result.Drama);
        }

        [Fact]
        public void Classify_NullKingdomOrClanId_RendersQuestionMark()
        {
            var factsRuler = new ProminenceFacts
            {
                HeroId = "lord_ruler_unknown",
                IsKingdomLeader = true,
                KingdomId = null
            };
            var resultRuler = PrisonerProminence.Classify(factsRuler, _defaultCfg);
            Assert.Equal("leader of kingdom ?", resultRuler.Reason);

            var factsClanLeader = new ProminenceFacts
            {
                HeroId = "lord_clan_unknown",
                IsClanLeader = true,
                ClanId = null
            };
            var resultClanLeader = PrisonerProminence.Classify(factsClanLeader, _defaultCfg);
            Assert.Equal("leader of clan ?", resultClanLeader.Reason);
        }

        [Fact]
        public void Classify_NullConfig_FallsBackToDefaultValues()
        {
            var facts = new ProminenceFacts
            {
                HeroId = "lord_1_1",
                IsKingdomLeader = true,
                KingdomId = "empire"
            };

            var result = PrisonerProminence.Classify(facts, null);

            Assert.Equal(ProminenceTier.Ruler, result.Tier);
            Assert.Equal(5, result.Drama);
        }

        [Fact]
        public void Describe_TemplateWithoutDrama_PrintsUnset()
        {
            var facts = new ProminenceFacts
            {
                HeroId = "lord_5_21",
                ClanId = "clan_vlandia_7",
                IsLord = true
            };

            var result = PrisonerProminence.Classify(facts, _defaultCfg);

            Assert.Equal("prisoner=lord_5_21 -> nobleMember (lord of clan clan_vlandia_7) => drama 2 (template unset)", result.Describe(null));
        }
    }
}
