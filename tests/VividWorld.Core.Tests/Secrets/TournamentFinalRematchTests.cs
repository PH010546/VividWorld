using System.Collections.Generic;
using VividWorld.Core.Secrets;
using Xunit;

namespace VividWorld.Core.Tests.Secrets
{
    public class TournamentFinalRematchTests
    {
        [Fact]
        public void Rematch_HighestScoreWins()
        {
            var candidates = new List<TournamentCandidateInput>
            {
                new TournamentCandidateInput("hero_low", "Hero Low", 10f),
                new TournamentCandidateInput("hero_high", "Hero High", 500f)
            };

            var result = TournamentFinalRematch.DetermineFinalOpponent(
                candidates,
                championHeroId: "champ",
                campaignSeed: 12345,
                townId: "town_1",
                day: 10.5);

            Assert.NotNull(result.SelectedOpponent);
            Assert.Equal("hero_high", result.SelectedOpponent!.HeroId);
            Assert.False(result.WasDesignated);
            Assert.Equal(2, result.Candidates.Count);
        }

        [Fact]
        public void Rematch_ChampionDoesNotParticipate()
        {
            var candidates = new List<TournamentCandidateInput>
            {
                new TournamentCandidateInput("champ", "Champion", 1000f),
                new TournamentCandidateInput("hero_other", "Hero Other", 50f)
            };

            var result = TournamentFinalRematch.DetermineFinalOpponent(
                candidates,
                championHeroId: "champ",
                campaignSeed: 12345,
                townId: "town_1",
                day: 10.5);

            Assert.NotNull(result.SelectedOpponent);
            Assert.Equal("hero_other", result.SelectedOpponent!.HeroId);
            Assert.Single(result.Candidates);
            Assert.DoesNotContain(result.Candidates, c => c.HeroId == "champ");
        }

        [Fact]
        public void Rematch_OnlyChampionHero_NoOpponent()
        {
            var candidates = new List<TournamentCandidateInput>
            {
                new TournamentCandidateInput("champ", "Champion", 1000f)
            };

            var result = TournamentFinalRematch.DetermineFinalOpponent(
                candidates,
                championHeroId: "champ",
                campaignSeed: 12345,
                townId: "town_1",
                day: 10.5);

            Assert.Null(result.SelectedOpponent);
            Assert.Empty(result.Candidates);
            Assert.False(result.WasDesignated);
        }

        [Fact]
        public void Rematch_FixedSeed_DeterministicResult()
        {
            var candidates = new List<TournamentCandidateInput>
            {
                new TournamentCandidateInput("hero_a", "Hero A", 100f),
                new TournamentCandidateInput("hero_b", "Hero B", 100f),
                new TournamentCandidateInput("hero_c", "Hero C", 100f)
            };

            var result1 = TournamentFinalRematch.DetermineFinalOpponent(
                candidates,
                championHeroId: "champ",
                campaignSeed: 42,
                townId: "town_epicrotea",
                day: 15.2);

            var result2 = TournamentFinalRematch.DetermineFinalOpponent(
                candidates,
                championHeroId: "champ",
                campaignSeed: 42,
                townId: "town_epicrotea",
                day: 15.8); // Same DayBucket(15)

            Assert.NotNull(result1.SelectedOpponent);
            Assert.NotNull(result2.SelectedOpponent);
            Assert.Equal(result1.SelectedOpponent!.HeroId, result2.SelectedOpponent!.HeroId);
            Assert.Equal(result1.SelectedOpponent.FinalScore, result2.SelectedOpponent.FinalScore);
            Assert.Equal(result1.SelectedOpponent.RandomRoll, result2.SelectedOpponent.RandomRoll);
        }

        [Fact]
        public void Rematch_DesignatedOpponent_TakesPrecedence()
        {
            var candidates = new List<TournamentCandidateInput>
            {
                new TournamentCandidateInput("hero_weak", "Hero Weak", 10f),
                new TournamentCandidateInput("hero_strong", "Hero Strong", 500f)
            };

            var result = TournamentFinalRematch.DetermineFinalOpponent(
                candidates,
                championHeroId: "champ",
                campaignSeed: 12345,
                townId: "town_1",
                day: 10.5,
                designatedOpponentHeroId: "hero_weak");

            Assert.NotNull(result.SelectedOpponent);
            Assert.Equal("hero_weak", result.SelectedOpponent!.HeroId);
            Assert.True(result.WasDesignated);
        }
    }
}
