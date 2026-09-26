using System.Collections.Generic;
using VividWorld.Core.Persistence;
using Xunit;

namespace VividWorld.Core.Tests
{
    public class EventPurgeLogFormatterTests
    {
        [Fact]
        public void FormatSummary_WithCooldown_MatchesVerbatimTemplate()
        {
            var plan = new EventPurgePlan
            {
                KeepActiveCount = 1,
                KeepRememberedCount = 2,
                KeepUnleakedSecretCount = 3,
                KeepGrudgesCount = 4,
                KeepSituationCooldownCount = 5,
                KeepTooRecentCount = 6,
                KeepFutureCount = 7,
                KeepPlayerNotLoggedCount = 8
            };
            plan.PurgeEventIds.Add("e_del_1");
            plan.PurgeEventIds.Add("e_del_2");

            string summary = EventPurgeLogFormatter.FormatSummary("slot_1", 100.5, plan, 45.0, 3, 12);

            string expected = "Purge at save 'slot_1' (day 100.5): deleted 2 event(s) (no NPC remembers); kept 36: active 1, remembered 2, unleaked secret 3, grudges 4, situation cooldown 5 (min age 45.0), too recent 6, future 7, player heard but not logged 8; 3 shard(s) rewritten in 12 ms";
            Assert.Equal(expected, summary);
        }

        [Fact]
        public void FormatSummary_CatalogNotLoaded_MatchesVerbatimTemplate()
        {
            var plan = new EventPurgePlan();
            string summary = EventPurgeLogFormatter.FormatSummary("slot_auto", 50.0, plan, null, 0, 0);

            string expected = "Purge at save 'slot_auto' (day 50.0): deleted 0 event(s) (no NPC remembers); kept 0: active 0, remembered 0, unleaked secret 0, grudges 0, situation cooldown 0 (min age catalog not loaded), too recent 0, future 0, player heard but not logged 0; 0 shard(s) rewritten in 0 ms";
            Assert.Equal(expected, summary);
        }

        [Fact]
        public void FormatDetail_WithNpcKnowers_MatchesVerbatimTemplate()
        {
            string detail = EventPurgeLogFormatter.FormatDetail("evt_duel", "duel", 24.3, "all 3 NPC knower(s) forgot it");
            string expected = "  purged evt_duel (duel, day 24.3): all 3 NPC knower(s) forgot it";
            Assert.Equal(expected, detail);
        }

        [Fact]
        public void FormatDetail_NoNpcKnower_MatchesVerbatimTemplate()
        {
            string detail = EventPurgeLogFormatter.FormatDetail("evt_alone", "secret_chat", 15.0, "no NPC knower");
            string expected = "  purged evt_alone (secret_chat, day 15.0): no NPC knower";
            Assert.Equal(expected, detail);
        }

        [Fact]
        public void FormatSkippedDisabled_MatchesVerbatimTemplate()
        {
            string skipped = EventPurgeLogFormatter.FormatSkippedDisabled("quick_save");
            string expected = "Purge skipped at save 'quick_save': persistence.purgeForgottenEvents is false";
            Assert.Equal(expected, skipped);
        }

        [Fact]
        public void FormatSkippedHeardLogDirty_MatchesVerbatimTemplate()
        {
            string skipped = EventPurgeLogFormatter.FormatSkippedHeardLogDirty("quick_save");
            string expected = "Purge skipped at save 'quick_save': the player heard-log could not be written, so nothing is deleted this time";
            Assert.Equal(expected, skipped);
        }

        [Fact]
        public void FormatPlayerNotLoggedWarn_MatchesVerbatimTemplate()
        {
            var ids = new List<string> { "e1", "e2", "e3" };
            string warn = EventPurgeLogFormatter.FormatPlayerNotLoggedWarn(3, ids);
            string expected = "Purge kept 3 event(s) the player knows but the heard-log does not have: e1, e2, e3";
            Assert.Equal(expected, warn);
        }

        [Fact]
        public void FormatWorldStatusPreview_MatchesVerbatimTemplate()
        {
            var plan = new EventPurgePlan
            {
                KeepActiveCount = 1,
                KeepRememberedCount = 2,
                KeepUnleakedSecretCount = 3,
                KeepGrudgesCount = 4,
                KeepSituationCooldownCount = 5,
                KeepTooRecentCount = 6,
                KeepFutureCount = 7,
                KeepPlayerNotLoggedCount = 8
            };
            plan.PurgeEventIds.Add("e_del_1");

            string preview = EventPurgeLogFormatter.FormatWorldStatusPreview(plan, 45.0);
            string expected = "- Purge preview (if saved now): would delete 1 (no NPC remembers); would keep 36: active 1, remembered 2, unleaked secret 3, grudges 4, situation cooldown 5 (min age 45.0), too recent 6, future 7, player heard but not logged 8";
            Assert.Equal(expected, preview);
        }
    }
}
