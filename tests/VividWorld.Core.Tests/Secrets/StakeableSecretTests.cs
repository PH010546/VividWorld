#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using VividWorld.Core.Catalog;
using VividWorld.Core.Config;
using VividWorld.Core.Events;
using VividWorld.Core.Secrets;
using VividWorld.Core.Situations;
using Xunit;

namespace VividWorld.Core.Tests.Secrets
{
    public class StakeableSecretTests
    {
        private static string ReadShipped(string fileName)
        {
            string? current = AppContext.BaseDirectory;
            while (!string.IsNullOrEmpty(current))
            {
                if (File.Exists(Path.Combine(current, "VividWorld.sln")))
                {
                    return File.ReadAllText(Path.Combine(current, "module", "ModuleData", fileName));
                }
                current = Directory.GetParent(current)?.FullName;
            }
            throw new FileNotFoundException(fileName);
        }

        private static WorldEvent CreateValidSecret(
            string eventId = "secret_1",
            string ownerId = "third_party_owner",
            string knowerId = "bettor_hero",
            int dramaWeight = 6,
            double day = 10.0)
        {
            return new WorldEvent
            {
                EventId = eventId,
                Type = "test_secret",
                Day = day,
                Origin = EventOrigin.Secret,
                DramaWeight = dramaWeight,
                DramaScale = DramaScales.Ten,
                SecretRetired = false,
                SecretOwnerHeroIds = new List<string> { ownerId },
                State = new RumorState { Leaked = false },
                KnownBy = new List<KnownByEntry>
                {
                    new KnownByEntry { HeroId = knowerId, Hop = 1, SourceHeroId = ownerId, LearnedDay = day }
                }
            };
        }

        [Fact]
        public void StakeableSecret_ExcludesOwnSecret()
        {
            // Bettor is in SecretOwnerHeroIds -> disqualified
            var evt = CreateValidSecret(ownerId: "bettor_hero", knowerId: "bettor_hero");

            var result = StakeableSecretEvaluation.Evaluate("bettor_hero", "opponent_hero", "player_hero", new[] { evt });

            Assert.False(result.IsEligible);
            Assert.Null(result.ChosenSecret);
            Assert.Single(result.Candidates);
            Assert.False(result.Candidates[0].IsEligible);
            Assert.Contains("bettor is owner", result.Candidates[0].DisqualifiedReason);
        }

        [Fact]
        public void StakeableSecret_ExcludesLeakedSecret()
        {
            // State.Leaked == true -> disqualified
            var evt = CreateValidSecret();
            evt.State.Leaked = true;

            var result = StakeableSecretEvaluation.Evaluate("bettor_hero", "opponent_hero", "player_hero", new[] { evt });

            Assert.False(result.IsEligible);
            Assert.Null(result.ChosenSecret);
            Assert.Single(result.Candidates);
            Assert.False(result.Candidates[0].IsEligible);
            Assert.Contains("already leaked", result.Candidates[0].DisqualifiedReason);
        }

        [Fact]
        public void StakeableSecret_ExcludesRetiredSecret()
        {
            // SecretRetired == true -> disqualified
            var evt = CreateValidSecret();
            evt.SecretRetired = true;

            var result = StakeableSecretEvaluation.Evaluate("bettor_hero", "opponent_hero", "player_hero", new[] { evt });

            Assert.False(result.IsEligible);
            Assert.Null(result.ChosenSecret);
            Assert.Single(result.Candidates);
            Assert.False(result.Candidates[0].IsEligible);
            Assert.Contains("secret retired", result.Candidates[0].DisqualifiedReason);
        }

        [Fact]
        public void StakeableSecret_ExcludesMissingOwners()
        {
            // SecretOwnerHeroIds is null or empty (old shape) -> disqualified
            var evt = CreateValidSecret();
            evt.SecretOwnerHeroIds = null;

            var result = StakeableSecretEvaluation.Evaluate("bettor_hero", "opponent_hero", "player_hero", new[] { evt });

            Assert.False(result.IsEligible);
            Assert.Null(result.ChosenSecret);
            Assert.Single(result.Candidates);
            Assert.False(result.Candidates[0].IsEligible);
            Assert.Contains("missing secret owners", result.Candidates[0].DisqualifiedReason);
        }

        [Fact]
        public void StakeableSecret_ExcludesOpponentAsOwner()
        {
            // Opponent is secret owner -> disqualified
            var evt = CreateValidSecret(ownerId: "opponent_hero");

            var result = StakeableSecretEvaluation.Evaluate("bettor_hero", "opponent_hero", "player_hero", new[] { evt });

            Assert.False(result.IsEligible);
            Assert.Null(result.ChosenSecret);
            Assert.Single(result.Candidates);
            Assert.False(result.Candidates[0].IsEligible);
            Assert.Contains("opponent is owner", result.Candidates[0].DisqualifiedReason);
        }

        [Fact]
        public void StakeableSecret_ExcludesWhenOpponentAlreadyKnows()
        {
            // Opponent is in KnownBy -> disqualified
            var evt = CreateValidSecret();
            evt.KnownBy.Add(new KnownByEntry { HeroId = "opponent_hero", Hop = 2, LearnedDay = 11.0 });

            var result = StakeableSecretEvaluation.Evaluate("bettor_hero", "opponent_hero", "player_hero", new[] { evt });

            Assert.False(result.IsEligible);
            Assert.Null(result.ChosenSecret);
            Assert.Single(result.Candidates);
            Assert.False(result.Candidates[0].IsEligible);
            Assert.Contains("opponent already knows", result.Candidates[0].DisqualifiedReason);
        }

        [Fact]
        public void StakeableSecret_ExcludesWhenBettorDoesNotKnow()
        {
            // Bettor is not in KnownBy -> disqualified
            var evt = CreateValidSecret();
            evt.KnownBy.Clear();

            var result = StakeableSecretEvaluation.Evaluate("bettor_hero", "opponent_hero", "player_hero", new[] { evt });

            Assert.False(result.IsEligible);
            Assert.Null(result.ChosenSecret);
            Assert.Single(result.Candidates);
            Assert.False(result.Candidates[0].IsEligible);
            Assert.Contains("bettor does not know", result.Candidates[0].DisqualifiedReason);
        }

        [Fact]
        public void StakeableSecret_ExcludesNonSecretOrigin()
        {
            // Origin is Public -> disqualified
            var evt = CreateValidSecret();
            evt.Origin = EventOrigin.Public;

            var result = StakeableSecretEvaluation.Evaluate("bettor_hero", "opponent_hero", "player_hero", new[] { evt });

            Assert.False(result.IsEligible);
            Assert.Null(result.ChosenSecret);
            Assert.Single(result.Candidates);
            Assert.False(result.Candidates[0].IsEligible);
            Assert.Contains("origin is not secret", result.Candidates[0].DisqualifiedReason);
        }

        [Fact]
        public void StakeableSecret_PlayerAsDecider_BranchFails()
        {
            // Bettor is player -> branch ineligible (player does not stake secrets)
            var evt = CreateValidSecret(knowerId: "player_hero");

            var result = StakeableSecretEvaluation.Evaluate("player_hero", "opponent_hero", "player_hero", new[] { evt });

            Assert.False(result.IsEligible);
            Assert.Null(result.ChosenSecret);
            Assert.Contains("bettor is player", result.Reason);
        }

        [Fact]
        public void StakeableSecret_PicksHighestDramaWeight()
        {
            var secretLight = CreateValidSecret("secret_light", dramaWeight: 4, day: 5.0);
            var secretHeavy = CreateValidSecret("secret_heavy", dramaWeight: 8, day: 10.0);
            var secretMedium = CreateValidSecret("secret_medium", dramaWeight: 6, day: 1.0);

            var result = StakeableSecretEvaluation.Evaluate(
                "bettor_hero", "opponent_hero", "player_hero",
                new[] { secretLight, secretHeavy, secretMedium });

            Assert.True(result.IsEligible);
            Assert.NotNull(result.ChosenSecret);
            Assert.Equal("secret_heavy", result.ChosenSecret!.EventId);
        }

        [Fact]
        public void StakeableSecret_TiedDramaWeight_PicksEarliestDay()
        {
            var secretLater = CreateValidSecret("secret_later", dramaWeight: 7, day: 25.0);
            var secretEarlier = CreateValidSecret("secret_earlier", dramaWeight: 7, day: 10.0);
            var secretMiddle = CreateValidSecret("secret_middle", dramaWeight: 7, day: 15.0);

            var result = StakeableSecretEvaluation.Evaluate(
                "bettor_hero", "opponent_hero", "player_hero",
                new[] { secretLater, secretEarlier, secretMiddle });

            Assert.True(result.IsEligible);
            Assert.NotNull(result.ChosenSecret);
            Assert.Equal("secret_earlier", result.ChosenSecret!.EventId);
        }

        [Fact]
        public void HuntWagerContest_HighestScoreWins()
        {
            // Formula: (bow + riding) * (0.75 + 0.25 * roll)
            // Bettor: (100 + 100) * (0.75 + 0.25 * 0.8) = 200 * 0.95 = 190
            // Opponent: (80 + 80) * (0.75 + 0.25 * 0.5) = 160 * 0.875 = 140
            var resBettorWins = StakeableSecretEvaluation.CalculateContest(
                "bettor", 100, 100, 0.8,
                "opponent", 80, 80, 0.5);

            Assert.True(resBettorWins.BettorWon);
            Assert.Equal("bettor", resBettorWins.WinnerHeroId);
            Assert.Equal("opponent", resBettorWins.LoserHeroId);
            Assert.Equal(190.0, resBettorWins.BettorScore, precision: 2);
            Assert.Equal(140.0, resBettorWins.OpponentScore, precision: 2);

            // Opponent wins when opponent score is higher
            var resOpponentWins = StakeableSecretEvaluation.CalculateContest(
                "bettor", 50, 50, 0.5,
                "opponent", 150, 150, 0.5);

            Assert.False(resOpponentWins.BettorWon);
            Assert.Equal("opponent", resOpponentWins.WinnerHeroId);
            Assert.Equal("bettor", resOpponentWins.LoserHeroId);
        }

        [Fact]
        public void HuntWagerContest_TieCountsAsBettorLosing()
        {
            // Equal skill and roll -> tie -> bettor loses ("同分算押的人輸")
            var res = StakeableSecretEvaluation.CalculateContest(
                "bettor", 100, 100, 0.5,
                "opponent", 100, 100, 0.5);

            Assert.Equal(res.BettorScore, res.OpponentScore);
            Assert.False(res.BettorWon);
            Assert.Equal("opponent", res.WinnerHeroId);
            Assert.Equal("bettor", res.LoserHeroId);
        }

        [Fact]
        public void HuntWager_BettorLoses_OpponentAddedToKnownByWithHopAndSource()
        {
            var secret = CreateValidSecret("sec_1", ownerId: "third_party", knowerId: "bettor", dramaWeight: 6, day: 5.0);
            var bettorEntry = secret.EntryFor("bettor");
            Assert.NotNull(bettorEntry);
            Assert.Equal(1, bettorEntry!.Hop);

            bool added = StakeableSecretEvaluation.ApplyContestConsequence(
                secret, "bettor", "opponent", bettorWon: false, day: 12.0, out var newEntry);

            Assert.True(added);
            Assert.NotNull(newEntry);
            Assert.Equal("opponent", newEntry!.HeroId);
            Assert.Equal(2, newEntry.Hop); // hop = bettor.Hop + 1
            Assert.Equal("bettor", newEntry.SourceHeroId);
            Assert.Equal(12.0, newEntry.LearnedDay);
            Assert.True(secret.IsKnownBy("opponent"));
        }

        [Fact]
        public void HuntWager_BettorWins_OpponentNotAddedToKnownBy()
        {
            var secret = CreateValidSecret("sec_1", ownerId: "third_party", knowerId: "bettor", dramaWeight: 6, day: 5.0);

            bool added = StakeableSecretEvaluation.ApplyContestConsequence(
                secret, "bettor", "opponent", bettorWon: true, day: 12.0, out var newEntry);

            Assert.False(added);
            Assert.Null(newEntry);
            Assert.False(secret.IsKnownBy("opponent"));
        }

        [Fact]
        public void SituationCondition_HasStakeableSecret_EvaluatesPrecondition()
        {
            var cond = new SituationConditionDef
            {
                Type = "hasStakeableSecret",
                Bettor = "challenged",
                Opponent = "challenger"
            };

            var factsByRole = new Dictionary<string, SituationRoleFacts>
            {
                ["challenged"] = new SituationRoleFacts { HeroId = "hero_challenged" },
                ["challenger"] = new SituationRoleFacts { HeroId = "hero_challenger" }
            };

            var eligibleSecret = CreateValidSecret("sec_1", ownerId: "owner_3", knowerId: "hero_challenged");

            var ctx = new SituationWorldContext
            {
                PlayerHeroId = "player_hero",
                GetKnownSecrets = (hId) => hId == "hero_challenged" ? new[] { eligibleSecret } : Array.Empty<WorldEvent>()
            };

            // 1. Eligible secret -> Ok
            var res = SituationConditionEvaluator.Evaluate(cond, factsByRole, context: ctx);
            Assert.True(res.Ok);
            Assert.Contains("sec_1", res.Detail);

            // 2. Player as bettor -> Ineligible
            var playerFacts = new Dictionary<string, SituationRoleFacts>
            {
                ["challenged"] = new SituationRoleFacts { HeroId = "player_hero" },
                ["challenger"] = new SituationRoleFacts { HeroId = "hero_challenger" }
            };
            var resPlayer = SituationConditionEvaluator.Evaluate(cond, playerFacts, context: ctx);
            Assert.False(resPlayer.Ok);
            Assert.Contains("player", resPlayer.Detail);

            // 3. No secrets -> Ineligible
            var emptyCtx = new SituationWorldContext
            {
                PlayerHeroId = "player_hero",
                GetKnownSecrets = (hId) => Array.Empty<WorldEvent>()
            };
            var resEmpty = SituationConditionEvaluator.Evaluate(cond, factsByRole, context: emptyCtx);
            Assert.False(resEmpty.Ok);
        }

        [Fact]
        public void ShippingTemplates_HuntWagerSettledAndStakedSecret_ExistAndAreValid()
        {
            var eventsJson = ReadShipped("vividworld_situation_events.json");
            var catalog = EventCatalogLoader.Load(eventsJson, new PersistenceConfig());

            // 1. hunt_wager_settled
            var settled = catalog.ByType("hunt_wager_settled");
            Assert.NotNull(settled);
            Assert.Equal(EventOrigin.Public, settled!.Origin);
            Assert.Equal(3, settled.DramaWeight);
            Assert.Contains("winner", settled.Roles.Keys);
            Assert.Contains("loser", settled.Roles.Keys);

            // 2. hunt_wager_staked_secret
            var staked = catalog.ByType("hunt_wager_staked_secret");
            Assert.NotNull(staked);
            Assert.Equal(EventOrigin.Secret, staked!.Origin);
            Assert.Equal(4, staked.DramaWeight);
            Assert.Equal("hunt_wager_settled", staked.LinkedTemplateType);
            Assert.Contains("bettor", staked.OwnerRoles!);
            Assert.Contains("bettor", staked.KnowingRoles);
            Assert.Contains("opponent", staked.KnowingRoles);
            Assert.Contains("staked_owner", staked.Roles.Keys);

            // 3. Situation hunt_wager secret_stake branch
            var situationsJson = ReadShipped("vividworld_situations.json");
            var sitCatalog = SituationCatalogLoader.Load(situationsJson);
            Assert.Empty(sitCatalog.Issues.Where(i => i.IsError));
            var huntWager = sitCatalog.ById("hunt_wager");
            Assert.NotNull(huntWager);
            var secretStakeBranch = huntWager!.Branches.FirstOrDefault(b => b.Id == "secret_stake");
            Assert.NotNull(secretStakeBranch);

            // Verify weights: calculating 0.8, honor -0.5
            Assert.NotNull(secretStakeBranch!.Traits);
            Assert.Equal(0.8, secretStakeBranch.Traits!["calculating"]);
            Assert.Equal(-0.5, secretStakeBranch.Traits!["honor"]);

            // Verify precondition
            Assert.Single(secretStakeBranch.Preconditions);
            Assert.Equal("hasStakeableSecret", secretStakeBranch.Preconditions[0].Type, ignoreCase: true);

            // Verify branch events
            Assert.Equal(2, secretStakeBranch.Events.Count);
            Assert.Equal("hunt_wager_settled", secretStakeBranch.Events[0].Type);
            Assert.Equal("hunt_wager_staked_secret", secretStakeBranch.Events[1].Type);
            Assert.Equal("hunt_wager_settled", secretStakeBranch.Events[1].LinkTo);
        }
    }
}
