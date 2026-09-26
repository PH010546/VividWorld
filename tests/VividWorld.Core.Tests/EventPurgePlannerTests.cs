using System;
using System.Collections.Generic;
using VividWorld.Core.Config;
using VividWorld.Core.Persistence;
using Xunit;

namespace VividWorld.Core.Tests
{
    public class EventPurgePlannerTests
    {
        private readonly MemoryConfig _memory = new() { Enabled = true };
        private const string PlayerHeroId = "player";

        [Fact]
        public void Evaluate_AllNineRules_EachTestedIndividually()
        {
            double today = 100.0;
            double situationMinAge = 45.0;

            // 1. KeepFuture: Day is in the future
            var futureEntry = new RumorIndexEntry
            {
                EventId = "e_future",
                Day = today + 2.0,
                Dormant = true
            };
            Assert.Equal(EventPurgeVerdict.KeepFuture,
                EventPurgePlanner.Evaluate(futureEntry, today, PlayerHeroId, _memory, situationMinAge, _ => true));

            // 2. KeepActive: Not dormant
            var activeEntry = new RumorIndexEntry
            {
                EventId = "e_active",
                Day = 10.0,
                Dormant = false
            };
            Assert.Equal(EventPurgeVerdict.KeepActive,
                EventPurgePlanner.Evaluate(activeEntry, today, PlayerHeroId, _memory, situationMinAge, _ => true));

            // 3. KeepGrudges: Has grudges
            var grudgesEntry = new RumorIndexEntry
            {
                EventId = "e_grudges",
                Day = 10.0,
                Dormant = true,
                HasGrudges = true
            };
            Assert.Equal(EventPurgeVerdict.KeepGrudges,
                EventPurgePlanner.Evaluate(grudgesEntry, today, PlayerHeroId, _memory, situationMinAge, _ => true));

            // 4. KeepTooRecent: today - Day < 1.0
            var tooRecentEntry = new RumorIndexEntry
            {
                EventId = "e_recent",
                Day = today - 0.5,
                Dormant = true
            };
            Assert.Equal(EventPurgeVerdict.KeepTooRecent,
                EventPurgePlanner.Evaluate(tooRecentEntry, today, PlayerHeroId, _memory, situationMinAge, _ => true));

            // 5. KeepSituationCooldown: within cooldown days
            var situationEntry = new RumorIndexEntry
            {
                EventId = "e_situation",
                Day = today - 30.0,
                Dormant = true,
                SituationId = "sit_1"
            };
            Assert.Equal(EventPurgeVerdict.KeepSituationCooldown,
                EventPurgePlanner.Evaluate(situationEntry, today, PlayerHeroId, _memory, situationMinAge, _ => true));

            // 6. KeepUnleakedSecret: Secret and not leaked
            var secretEntry = new RumorIndexEntry
            {
                EventId = "e_secret",
                Day = 10.0,
                Dormant = true,
                Secret = true,
                Leaked = false
            };
            Assert.Equal(EventPurgeVerdict.KeepUnleakedSecret,
                EventPurgePlanner.Evaluate(secretEntry, today, PlayerHeroId, _memory, situationMinAge, _ => true));

            // 7. KeepPlayerNotLogged: player knows but playerLogHas is false
            var playerNotLoggedEntry = new RumorIndexEntry
            {
                EventId = "e_player_not_logged",
                Day = 10.0,
                Dormant = true,
                KnownByHeroIds = new List<string> { PlayerHeroId }
            };
            Assert.Equal(EventPurgeVerdict.KeepPlayerNotLogged,
                EventPurgePlanner.Evaluate(playerNotLoggedEntry, today, PlayerHeroId, _memory, situationMinAge, _ => false));

            // 8. KeepRemembered: non-player knower still remembers
            var rememberedEntry = new RumorIndexEntry
            {
                EventId = "e_remembered",
                Day = 10.0,
                Dormant = true,
                KnownByHeroIds = new List<string> { "lord_npc" },
                ForgetDays = new Dictionary<string, double> { ["lord_npc"] = today + 10.0 }
            };
            Assert.Equal(EventPurgeVerdict.KeepRemembered,
                EventPurgePlanner.Evaluate(rememberedEntry, today, PlayerHeroId, _memory, situationMinAge, _ => true));

            // 9. DeleteForgotten: NPC knower forgot
            var forgottenEntry = new RumorIndexEntry
            {
                EventId = "e_forgotten",
                Day = 10.0,
                Dormant = true,
                KnownByHeroIds = new List<string> { "lord_npc" },
                ForgetDays = new Dictionary<string, double> { ["lord_npc"] = today - 5.0 }
            };
            Assert.Equal(EventPurgeVerdict.DeleteForgotten,
                EventPurgePlanner.Evaluate(forgottenEntry, today, PlayerHeroId, _memory, situationMinAge, _ => true));
        }

        [Fact]
        public void Evaluate_RuleOrder_PrecedingRuleTakesPrecedence()
        {
            double today = 100.0;

            // Future + HasGrudges => KeepFuture (Rule 1 > Rule 3)
            var futureGrudges = new RumorIndexEntry
            {
                EventId = "e_1",
                Day = today + 5.0,
                Dormant = true,
                HasGrudges = true
            };
            Assert.Equal(EventPurgeVerdict.KeepFuture,
                EventPurgePlanner.Evaluate(futureGrudges, today, PlayerHeroId, _memory, 45.0, _ => true));

            // Dormant unleaked secret, player knows but log doesn't have it => KeepUnleakedSecret, no WARN (Rule 6 > Rule 7)
            var secretPlayerNotLogged = new RumorIndexEntry
            {
                EventId = "e_2",
                Day = 10.0,
                Dormant = true,
                Secret = true,
                Leaked = false,
                KnownByHeroIds = new List<string> { PlayerHeroId }
            };
            var index = new RumorIndex { Entries = new List<RumorIndexEntry> { secretPlayerNotLogged } };
            var plan = EventPurgePlanner.Plan(index, today, PlayerHeroId, _memory, 45.0, _ => false);
            Assert.Equal(1, plan.KeepUnleakedSecretCount);
            Assert.Equal(0, plan.KeepPlayerNotLoggedCount);
            Assert.Empty(plan.PlayerNotLoggedEventIds);

            // Not dormant + Too recent => KeepActive (Rule 2 > Rule 4)
            var activeRecent = new RumorIndexEntry
            {
                EventId = "e_3",
                Day = today - 0.2,
                Dormant = false
            };
            Assert.Equal(EventPurgeVerdict.KeepActive,
                EventPurgePlanner.Evaluate(activeRecent, today, PlayerHeroId, _memory, 45.0, _ => true));

            // HasGrudges + Situation cooldown => KeepGrudges (Rule 3 > Rule 5)
            var grudgeSituation = new RumorIndexEntry
            {
                EventId = "e_4",
                Day = today - 10.0,
                Dormant = true,
                HasGrudges = true,
                SituationId = "sit_1"
            };
            Assert.Equal(EventPurgeVerdict.KeepGrudges,
                EventPurgePlanner.Evaluate(grudgeSituation, today, PlayerHeroId, _memory, 45.0, _ => true));
        }

        [Fact]
        public void Evaluate_SituationCatalogNotLoaded_KeepsAllSituationEvents()
        {
            double today = 100.0;
            // situationMinAgeDays is null => Catalog not loaded => all situation events kept
            var oldSituationEntry = new RumorIndexEntry
            {
                EventId = "e_old_sit",
                Day = 1.0, // 99 days ago, older than any cooldown
                Dormant = true,
                SituationId = "sit_1"
            };

            Assert.Equal(EventPurgeVerdict.KeepSituationCooldown,
                EventPurgePlanner.Evaluate(oldSituationEntry, today, PlayerHeroId, _memory, null, _ => true));
        }

        [Fact]
        public void Evaluate_NoNpcKnower_DeletesForgotten()
        {
            double today = 100.0;
            var noKnowersEntry = new RumorIndexEntry
            {
                EventId = "e_no_knowers",
                Type = "duel",
                Day = 10.0,
                Dormant = true,
                KnownByHeroIds = new List<string>()
            };

            var index = new RumorIndex { Entries = new List<RumorIndexEntry> { noKnowersEntry } };
            var plan = EventPurgePlanner.Plan(index, today, PlayerHeroId, _memory, 45.0, _ => true);

            Assert.Equal(1, plan.DeletedCount);
            Assert.Single(plan.PurgedEvents);
            Assert.Equal("no NPC knower", plan.PurgedEvents[0].Why);
            Assert.Equal(0, plan.PurgedEvents[0].NpcKnowerCount);
        }

        [Fact]
        public void Evaluate_PlayerIsOnlyKnowerAndLogged_DeletesForgotten()
        {
            double today = 100.0;
            var playerOnlyEntry = new RumorIndexEntry
            {
                EventId = "e_player_only",
                Type = "chatter",
                Day = 10.0,
                Dormant = true,
                KnownByHeroIds = new List<string> { PlayerHeroId }
            };

            var index = new RumorIndex { Entries = new List<RumorIndexEntry> { playerOnlyEntry } };
            var plan = EventPurgePlanner.Plan(index, today, PlayerHeroId, _memory, 45.0, id => id == "e_player_only");

            Assert.Equal(1, plan.DeletedCount);
            Assert.Single(plan.PurgedEvents);
            Assert.Equal("no NPC knower", plan.PurgedEvents[0].Why);
            Assert.Equal(0, plan.PurgedEvents[0].NpcKnowerCount);
        }
    }
}
