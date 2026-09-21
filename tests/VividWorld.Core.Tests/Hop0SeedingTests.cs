using System.Collections.Generic;
using System.Linq;
using VividWorld.Core.Config;
using VividWorld.Core.Events;
using VividWorld.Core.Ingest;
using VividWorld.Core.Rumors;
using VividWorld.Core.Tests.Fakes;
using Xunit;

namespace VividWorld.Core.Tests
{
    public class Hop0SeedingTests
    {
        [Fact]
        public void Seed_KnowingRoles_SeedsOnlyTheNamedParticipants()
        {
            var channel = new FakePropagationChannel();
            var traits = new FakeHeroTraitLookup();
            var cfg = new PropagationConfig();

            var submission = new EventSubmission
            {
                Type = "covert_sabotage",
                Day = 100.0,
                Origin = EventOrigin.Secret,
                Participants = new Dictionary<string, string>
                {
                    ["mastermind"] = "hero_ivan",
                    ["target"] = "hero_boris",
                    ["agent"] = "hero_aldric"
                },
                KnowingRoles = new HashSet<string> { "mastermind", "agent" }
            };

            var evt = new WorldEvent
            {
                EventId = "evt_0100_test",
                Origin = EventOrigin.Secret,
                Participants = new Dictionary<string, string>(submission.Participants)
            };

            Hop0Seeding.Seed(evt, submission, channel, traits, cfg, "player", 100.0);

            var knowerIds = evt.KnownBy.Select(k => k.HeroId).ToList();
            Assert.Contains("hero_ivan", knowerIds);
            Assert.Contains("hero_aldric", knowerIds);
            Assert.DoesNotContain("hero_boris", knowerIds);
            Assert.All(evt.KnownBy, k => Assert.Equal(0, k.Hop));

            // 空集合時才是全部參與者
            submission.KnowingRoles.Clear();
            var evt2 = new WorldEvent
            {
                EventId = "evt_0100_test2",
                Origin = EventOrigin.Secret,
                Participants = new Dictionary<string, string>(submission.Participants)
            };
            Hop0Seeding.Seed(evt2, submission, channel, traits, cfg, "player", 100.0);
            var knowerIds2 = evt2.KnownBy.Select(k => k.HeroId).ToList();
            Assert.Contains("hero_ivan", knowerIds2);
            Assert.Contains("hero_aldric", knowerIds2);
            Assert.Contains("hero_boris", knowerIds2);
        }

        [Fact]
        public void Seed_SecretEvent_NeverAddsWitnesses()
        {
            var channel = new FakePropagationChannel();
            var traits = new FakeHeroTraitLookup();
            var cfg = new PropagationConfig();

            traits.Set(new TraitProfile { HeroId = "hero_witness", IsAlive = true, IsLord = true });
            channel.AddWitness("hero_ivan", "hero_witness");

            var submission = new EventSubmission
            {
                Type = "covert_sabotage",
                Day = 100.0,
                Origin = EventOrigin.Secret,
                AutoResolveWitnesses = true,
                Participants = new Dictionary<string, string>
                {
                    ["mastermind"] = "hero_ivan"
                }
            };

            var evt = new WorldEvent
            {
                EventId = "evt_0100_secret",
                Origin = EventOrigin.Secret,
                Participants = new Dictionary<string, string>(submission.Participants)
            };

            Hop0Seeding.Seed(evt, submission, channel, traits, cfg, "player", 100.0);

            Assert.Equal(0, channel.WitnessQueryCount);
            var knowerIds = evt.KnownBy.Select(k => k.HeroId).ToList();
            Assert.DoesNotContain("hero_witness", knowerIds);
            Assert.Single(evt.KnownBy);
        }

        [Fact]
        public void Seed_PublicWitnesses_MustPassTheEligibilityPredicate()
        {
            var channel = new FakePropagationChannel();
            var traits = new FakeHeroTraitLookup();
            var cfg = new PropagationConfig();

            // witness_lord 合格
            traits.Set(new TraitProfile { HeroId = "witness_lord", IsAlive = true, IsPrisoner = false, IsLord = true });
            // witness_peasant 不合格（非領主非流浪者）
            traits.Set(new TraitProfile { HeroId = "witness_peasant", IsAlive = true, IsPrisoner = false, IsLord = false, IsWanderer = false });
            // witness_prisoner 不合格（俘虜）
            traits.Set(new TraitProfile { HeroId = "witness_prisoner", IsAlive = true, IsPrisoner = true, IsLord = true });

            channel.AddWitness("hero_ivan", "witness_lord");
            channel.AddWitness("hero_ivan", "witness_peasant");
            channel.AddWitness("hero_ivan", "witness_prisoner");

            var submission = new EventSubmission
            {
                Type = "tavern_quarrel",
                Day = 100.0,
                Origin = EventOrigin.Public,
                AutoResolveWitnesses = true,
                Participants = new Dictionary<string, string>
                {
                    ["initiator"] = "hero_ivan"
                }
            };

            var evt = new WorldEvent
            {
                EventId = "evt_0100_public",
                Origin = EventOrigin.Public,
                Participants = new Dictionary<string, string>(submission.Participants)
            };

            Hop0Seeding.Seed(evt, submission, channel, traits, cfg, "player", 100.0);

            var knowerIds = evt.KnownBy.Select(k => k.HeroId).ToList();
            Assert.Contains("witness_lord", knowerIds);
            Assert.DoesNotContain("witness_peasant", knowerIds);
            Assert.DoesNotContain("witness_prisoner", knowerIds);
        }

        [Fact]
        public void Seed_Witnesses_NeverIncludeThePlayer()
        {
            var channel = new FakePropagationChannel();
            var traits = new FakeHeroTraitLookup();
            var cfg = new PropagationConfig();

            traits.Set(new TraitProfile { HeroId = "hero_player", IsAlive = true, IsLord = true });
            traits.Set(new TraitProfile { HeroId = "witness_npc", IsAlive = true, IsLord = true });

            channel.AddWitness("hero_ivan", "hero_player");
            channel.AddWitness("hero_ivan", "witness_npc");

            var submission = new EventSubmission
            {
                Type = "duel",
                Day = 100.0,
                Origin = EventOrigin.Public,
                AutoResolveWitnesses = true,
                Participants = new Dictionary<string, string>
                {
                    ["fighter"] = "hero_ivan"
                }
            };

            var evt = new WorldEvent
            {
                EventId = "evt_0100_duel",
                Origin = EventOrigin.Public,
                Participants = new Dictionary<string, string>(submission.Participants)
            };

            Hop0Seeding.Seed(evt, submission, channel, traits, cfg, "hero_player", 100.0);

            var knowerIds = evt.KnownBy.Select(k => k.HeroId).ToList();
            Assert.DoesNotContain("hero_player", knowerIds);
            Assert.Contains("witness_npc", knowerIds);
        }

        [Fact]
        public void Seed_NamedPlayerParticipant_IsSeededAtHopZero()
        {
            var channel = new FakePropagationChannel();
            var traits = new FakeHeroTraitLookup();
            var cfg = new PropagationConfig();

            var submission = new EventSubmission
            {
                Type = "duel",
                Day = 100.0,
                Origin = EventOrigin.Public,
                AutoResolveWitnesses = false,
                Participants = new Dictionary<string, string>
                {
                    ["fighter_a"] = "hero_player",
                    ["fighter_b"] = "hero_ivan"
                },
                KnowingRoles = new HashSet<string> { "fighter_a", "fighter_b" }
            };

            var evt = new WorldEvent
            {
                EventId = "evt_0100_duel",
                Origin = EventOrigin.Public,
                Participants = new Dictionary<string, string>(submission.Participants)
            };

            Hop0Seeding.Seed(evt, submission, channel, traits, cfg, "hero_player", 100.0);

            var entry = evt.KnownBy.FirstOrDefault(k => k.HeroId == "hero_player");
            Assert.NotNull(entry);
            Assert.Equal(0, entry!.Hop);

            // 測試作為 InitialKnowerHeroIds 種入
            var submission2 = new EventSubmission
            {
                Type = "secret_meeting",
                Day = 100.0,
                Origin = EventOrigin.Secret,
                Participants = new Dictionary<string, string> { ["host"] = "hero_ivan" },
                KnowingRoles = new HashSet<string> { "host" },
                InitialKnowerHeroIds = new List<string> { "hero_player" }
            };
            var evt2 = new WorldEvent
            {
                EventId = "evt_0100_sec",
                Origin = EventOrigin.Secret,
                Participants = new Dictionary<string, string>(submission2.Participants)
            };

            Hop0Seeding.Seed(evt2, submission2, channel, traits, cfg, "hero_player", 100.0);
            var entry2 = evt2.KnownBy.FirstOrDefault(k => k.HeroId == "hero_player");
            Assert.NotNull(entry2);
            Assert.Equal(0, entry2!.Hop);
        }

        [Fact]
        public void Seed_Participants_AreNotFilteredByEligibility()
        {
            var channel = new FakePropagationChannel();
            var traits = new FakeHeroTraitLookup();
            var cfg = new PropagationConfig();

            // 名人 / 平民參與者（非領主非流浪者）
            traits.Set(new TraitProfile { HeroId = "notable_boris", IsAlive = true, IsLord = false, IsWanderer = false });

            var submission = new EventSubmission
            {
                Type = "trade_deal",
                Day = 100.0,
                Origin = EventOrigin.Public,
                AutoResolveWitnesses = false,
                Participants = new Dictionary<string, string>
                {
                    ["merchant"] = "notable_boris"
                }
            };

            var evt = new WorldEvent
            {
                EventId = "evt_0100_trade",
                Origin = EventOrigin.Public,
                Participants = new Dictionary<string, string>(submission.Participants)
            };

            Hop0Seeding.Seed(evt, submission, channel, traits, cfg, "player", 100.0);

            var knowerIds = evt.KnownBy.Select(k => k.HeroId).ToList();
            Assert.Contains("notable_boris", knowerIds);
            Assert.Equal(0, evt.KnownBy.First(k => k.HeroId == "notable_boris").Hop);
        }

        [Fact]
        public void Seed_RespectsMaxInitialWitnesses_AndDeduplicates()
        {
            var channel = new FakePropagationChannel();
            var traits = new FakeHeroTraitLookup();
            var cfg = new PropagationConfig
            {
                MaxInitialWitnesses = 3
            };

            for (int i = 1; i <= 10; i++)
            {
                string wid = $"witness_{i}";
                traits.Set(new TraitProfile { HeroId = wid, IsAlive = true, IsLord = true });
                channel.AddWitness("hero_ivan", wid);
            }

            // 同時讓 witness_1 也是已有的參與者
            var submission = new EventSubmission
            {
                Type = "tavern_quarrel",
                Day = 100.0,
                Origin = EventOrigin.Public,
                AutoResolveWitnesses = true,
                Participants = new Dictionary<string, string>
                {
                    ["initiator"] = "hero_ivan",
                    ["participant"] = "witness_1"
                }
            };

            var evt = new WorldEvent
            {
                EventId = "evt_0100_pub",
                Origin = EventOrigin.Public,
                Participants = new Dictionary<string, string>(submission.Participants)
            };

            Hop0Seeding.Seed(evt, submission, channel, traits, cfg, "player", 100.0);

            // witness_1 在 Participants 已經種入，目擊者不能重複種入 witness_1
            Assert.Equal(1, evt.KnownBy.Count(k => k.HeroId == "witness_1"));

            // 總知情者數：2 名參與者 (hero_ivan, witness_1) + 最多 3 名目擊者 (如 witness_2, witness_3, witness_4)
            var witnessCount = evt.KnownBy.Count(k => k.HeroId.StartsWith("witness_") && k.HeroId != "witness_1");
            Assert.True(witnessCount <= cfg.MaxInitialWitnesses);
            Assert.Equal(evt.KnownBy.Count, evt.KnownBy.Select(k => k.HeroId).Distinct().Count());
        }

        [Fact]
        public void Seed_DevSecretShape_LeavesANonPlayerKnower()
        {
            var channel = new FakePropagationChannel();
            var traits = new FakeHeroTraitLookup();
            var cfg = new PropagationConfig();

            traits.Set(new TraitProfile { HeroId = "hero_npc", IsAlive = true, IsLord = true });
            traits.Set(new TraitProfile { HeroId = "hero_player", IsAlive = true, IsLord = true });

            var submission = new EventSubmission
            {
                Type = "dev_test",
                Day = 100.0,
                Origin = EventOrigin.Secret,
                AutoResolveWitnesses = false,
                Participants = new Dictionary<string, string>
                {
                    ["mastermind"] = "hero_npc",
                    ["target"] = "hero_player"
                },
                KnowingRoles = new HashSet<string> { "mastermind" }
            };

            var evt = new WorldEvent
            {
                EventId = "evt_dev_secret",
                Origin = EventOrigin.Secret,
                Day = 100.0,
                Participants = new Dictionary<string, string>(submission.Participants)
            };

            Hop0Seeding.Seed(evt, submission, channel, traits, cfg, "hero_player", 100.0);

            Assert.Single(evt.KnownBy);
            var knower = evt.KnownBy[0];
            Assert.Equal("hero_npc", knower.HeroId);
            Assert.NotEqual("hero_player", knower.HeroId);
            Assert.Equal(0, knower.Hop);
        }
    }
}
