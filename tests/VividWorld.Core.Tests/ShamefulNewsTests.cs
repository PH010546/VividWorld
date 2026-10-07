#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using VividWorld.Core.Catalog;
using VividWorld.Core.Channels;
using VividWorld.Core.Config;
using VividWorld.Core.Diagnostics;
using VividWorld.Core.Dialogue;
using VividWorld.Core.Events;
using VividWorld.Core.Feelings;
using VividWorld.Core.Grudges;
using VividWorld.Core.Memory;
using VividWorld.Core.Persistence;
using VividWorld.Core.Rumors;
using VividWorld.Core.Tests.Fakes;
using VividWorld.Core.Util;
using Xunit;

namespace VividWorld.Core.Tests
{
    public class ShamefulNewsTests
    {
        private static EventTemplate CreateShabbyTemplate(string type = "shabby_type", string shamedRole = "victim")
        {
            return new EventTemplate
            {
                Type = type,
                Feelings = new Dictionary<string, string>
                {
                    [shamedRole] = "shabby"
                }
            };
        }

        private static EventTemplate CreateExecutorTemplate(string type = "defeat_type")
        {
            return new EventTemplate
            {
                Type = type,
                Feelings = new Dictionary<string, string>
                {
                    ["killer"] = "executor"
                }
            };
        }

        private static EventTemplate CreateBrashTemplate(string type = "brash_type")
        {
            return new EventTemplate
            {
                Type = type,
                Feelings = new Dictionary<string, string>
                {
                    ["brash_role"] = "brash"
                }
            };
        }

        private static EventTemplate CreateExecutedTemplate()
        {
            return new EventTemplate
            {
                Type = "hero_executed",
                Feelings = new Dictionary<string, string>
                {
                    ["killer"] = "executor"
                }
            };
        }

        private static WorldEvent CreateShamefulEvent(string eventId, string shamedHeroId, string shamedRole = "victim", string type = "shabby_type")
        {
            var evt = new WorldEvent
            {
                EventId = eventId,
                Type = type,
                Day = 10.0,
                Origin = EventOrigin.Public,
                Participants = new Dictionary<string, string>
                {
                    [shamedRole] = shamedHeroId
                },
                KnownBy = new List<KnownByEntry>()
            };
            return evt;
        }

        // ──────────────── 16 Cells of 4x4 Table ────────────────

        [Fact]
        public void TableCanTell_Friend_Self_CanTell() => Assert.True(ShamefulNewsRule.TableCanTell(ShamefulSpeakerRow.Friend, ShamefulListenerCol.Self));

        [Fact]
        public void TableCanTell_Friend_Friend_CanTell() => Assert.True(ShamefulNewsRule.TableCanTell(ShamefulSpeakerRow.Friend, ShamefulListenerCol.Friend));

        [Fact]
        public void TableCanTell_Friend_Unrelated_HeldBack() => Assert.False(ShamefulNewsRule.TableCanTell(ShamefulSpeakerRow.Friend, ShamefulListenerCol.Unrelated));

        [Fact]
        public void TableCanTell_Friend_Enemy_HeldBack() => Assert.False(ShamefulNewsRule.TableCanTell(ShamefulSpeakerRow.Friend, ShamefulListenerCol.Enemy));

        [Fact]
        public void TableCanTell_Owe_Self_HeldBack() => Assert.False(ShamefulNewsRule.TableCanTell(ShamefulSpeakerRow.Owe, ShamefulListenerCol.Self));

        [Fact]
        public void TableCanTell_Owe_Friend_HeldBack() => Assert.False(ShamefulNewsRule.TableCanTell(ShamefulSpeakerRow.Owe, ShamefulListenerCol.Friend));

        [Fact]
        public void TableCanTell_Owe_Unrelated_HeldBack() => Assert.False(ShamefulNewsRule.TableCanTell(ShamefulSpeakerRow.Owe, ShamefulListenerCol.Unrelated));

        [Fact]
        public void TableCanTell_Owe_Enemy_HeldBack() => Assert.False(ShamefulNewsRule.TableCanTell(ShamefulSpeakerRow.Owe, ShamefulListenerCol.Enemy));

        [Fact]
        public void TableCanTell_Neutral_Self_HeldBack() => Assert.False(ShamefulNewsRule.TableCanTell(ShamefulSpeakerRow.Neutral, ShamefulListenerCol.Self));

        [Fact]
        public void TableCanTell_Neutral_Friend_HeldBack() => Assert.False(ShamefulNewsRule.TableCanTell(ShamefulSpeakerRow.Neutral, ShamefulListenerCol.Friend));

        [Fact]
        public void TableCanTell_Neutral_Unrelated_CanTell() => Assert.True(ShamefulNewsRule.TableCanTell(ShamefulSpeakerRow.Neutral, ShamefulListenerCol.Unrelated));

        [Fact]
        public void TableCanTell_Neutral_Enemy_CanTell() => Assert.True(ShamefulNewsRule.TableCanTell(ShamefulSpeakerRow.Neutral, ShamefulListenerCol.Enemy));

        [Fact]
        public void TableCanTell_Enemy_Self_HeldBack() => Assert.False(ShamefulNewsRule.TableCanTell(ShamefulSpeakerRow.Enemy, ShamefulListenerCol.Self));

        [Fact]
        public void TableCanTell_Enemy_Friend_CanTell() => Assert.True(ShamefulNewsRule.TableCanTell(ShamefulSpeakerRow.Enemy, ShamefulListenerCol.Friend));

        [Fact]
        public void TableCanTell_Enemy_Unrelated_CanTell() => Assert.True(ShamefulNewsRule.TableCanTell(ShamefulSpeakerRow.Enemy, ShamefulListenerCol.Unrelated));

        [Fact]
        public void TableCanTell_Enemy_Enemy_CanTell() => Assert.True(ShamefulNewsRule.TableCanTell(ShamefulSpeakerRow.Enemy, ShamefulListenerCol.Enemy));

        [Theory]
        [InlineData(ShamefulSpeakerRow.Friend, ShamefulListenerCol.Self, true)]
        [InlineData(ShamefulSpeakerRow.Friend, ShamefulListenerCol.Friend, true)]
        [InlineData(ShamefulSpeakerRow.Friend, ShamefulListenerCol.Unrelated, false)]
        [InlineData(ShamefulSpeakerRow.Friend, ShamefulListenerCol.Enemy, false)]
        [InlineData(ShamefulSpeakerRow.Owe, ShamefulListenerCol.Self, false)]
        [InlineData(ShamefulSpeakerRow.Owe, ShamefulListenerCol.Friend, false)]
        [InlineData(ShamefulSpeakerRow.Owe, ShamefulListenerCol.Unrelated, false)]
        [InlineData(ShamefulSpeakerRow.Owe, ShamefulListenerCol.Enemy, false)]
        [InlineData(ShamefulSpeakerRow.Neutral, ShamefulListenerCol.Self, false)]
        [InlineData(ShamefulSpeakerRow.Neutral, ShamefulListenerCol.Friend, false)]
        [InlineData(ShamefulSpeakerRow.Neutral, ShamefulListenerCol.Unrelated, true)]
        [InlineData(ShamefulSpeakerRow.Neutral, ShamefulListenerCol.Enemy, true)]
        [InlineData(ShamefulSpeakerRow.Enemy, ShamefulListenerCol.Self, false)]
        [InlineData(ShamefulSpeakerRow.Enemy, ShamefulListenerCol.Friend, true)]
        [InlineData(ShamefulSpeakerRow.Enemy, ShamefulListenerCol.Unrelated, true)]
        [InlineData(ShamefulSpeakerRow.Enemy, ShamefulListenerCol.Enemy, true)]
        public void TableCanTell_Theory_MatchesMatrix(ShamefulSpeakerRow row, ShamefulListenerCol col, bool expected)
        {
            Assert.Equal(expected, ShamefulNewsRule.TableCanTell(row, col));
        }

        // ──────────────── 16 Cells End-to-End Evaluation Between Lords ────────────────

        [Theory]
        [InlineData(ShamefulSpeakerRow.Friend, ShamefulListenerCol.Self, true)]
        [InlineData(ShamefulSpeakerRow.Friend, ShamefulListenerCol.Friend, true)]
        [InlineData(ShamefulSpeakerRow.Friend, ShamefulListenerCol.Unrelated, false)]
        [InlineData(ShamefulSpeakerRow.Friend, ShamefulListenerCol.Enemy, false)]
        [InlineData(ShamefulSpeakerRow.Owe, ShamefulListenerCol.Self, false)]
        [InlineData(ShamefulSpeakerRow.Owe, ShamefulListenerCol.Friend, false)]
        [InlineData(ShamefulSpeakerRow.Owe, ShamefulListenerCol.Unrelated, false)]
        [InlineData(ShamefulSpeakerRow.Owe, ShamefulListenerCol.Enemy, false)]
        [InlineData(ShamefulSpeakerRow.Neutral, ShamefulListenerCol.Self, false)]
        [InlineData(ShamefulSpeakerRow.Neutral, ShamefulListenerCol.Friend, false)]
        [InlineData(ShamefulSpeakerRow.Neutral, ShamefulListenerCol.Unrelated, true)]
        [InlineData(ShamefulSpeakerRow.Neutral, ShamefulListenerCol.Enemy, true)]
        [InlineData(ShamefulSpeakerRow.Enemy, ShamefulListenerCol.Self, false)]
        [InlineData(ShamefulSpeakerRow.Enemy, ShamefulListenerCol.Friend, true)]
        [InlineData(ShamefulSpeakerRow.Enemy, ShamefulListenerCol.Unrelated, true)]
        [InlineData(ShamefulSpeakerRow.Enemy, ShamefulListenerCol.Enemy, true)]
        public void Evaluate_BetweenLords_MatchesRowAndCol(ShamefulSpeakerRow expectedRow, ShamefulListenerCol expectedCol, bool expectedCanTell)
        {
            var world = new FakeDialogueWorld();
            var config = new VividWorldConfig();
            string shamed = "shamed_lord";
            string speaker = "speaker_lord";
            string listener = expectedCol == ShamefulListenerCol.Self ? shamed : "listener_lord";

            // Configure speaker row
            switch (expectedRow)
            {
                case ShamefulSpeakerRow.Friend:
                    world.SetAffection(speaker, shamed, 40); // Close -> Friend
                    break;
                case ShamefulSpeakerRow.Owe:
                    world.SetAffection(speaker, shamed, -40); // Low affection + Favor => Owe
                    world.Grudges[(speaker, shamed)] = new List<GrudgeEntry>
                    {
                        new GrudgeEntry { FromHeroId = speaker, AboutHeroId = shamed, Scope = GrudgeScope.Personal, Requested = 25.0, Delta = 25, Day = 100.0 }
                    };
                    break;
                case ShamefulSpeakerRow.Neutral:
                    world.SetAffection(speaker, shamed, 0);
                    break;
                case ShamefulSpeakerRow.Enemy:
                    world.SetAffection(speaker, shamed, -40); // Hostile -> Enemy
                    break;
            }

            // Configure listener col
            if (expectedCol == ShamefulListenerCol.Self)
            {
                // listener is already shamed
            }
            else if (expectedCol == ShamefulListenerCol.Friend)
            {
                world.SetAffection(listener, shamed, 35); // >= 30 -> Friend
            }
            else if (expectedCol == ShamefulListenerCol.Enemy)
            {
                world.SetAffection(listener, shamed, -35); // <= -30 -> Enemy
            }
            else
            {
                world.SetAffection(listener, shamed, 5); // Unrelated
            }

            var evt = CreateShamefulEvent("evt1", shamed);
            var tpl = CreateShabbyTemplate();
            var eval = ShamefulNewsRule.Evaluate(
                evt,
                speaker,
                listener,
                world,
                _ => tpl,
                null,
                config,
                100.0);

            Assert.True(eval.IsShameful);
            Assert.Equal(expectedCanTell, eval.CanTell);
            Assert.Single(eval.Evaluations);
            Assert.Equal(expectedRow, eval.Evaluations[0].Row);
            Assert.Equal(expectedCol, eval.Evaluations[0].Col);
        }

        // ──────────────── Player Interactions (At Least 4 Tests) ────────────────

        [Fact]
        public void PlayerInteraction_FriendDoesNotTell_UnrelatedPlayer()
        {
            var world = new FakeDialogueWorld();
            var config = new VividWorldConfig();
            string shamed = "shamed_lord";
            string teller = "teller_friend";
            string player = "player";

            world.SetAffection(teller, shamed, 40); // teller is Friend of shamed
            world.SetAffection(player, shamed, 0);   // player is Unrelated

            var evt = CreateShamefulEvent("evt1", shamed);
            var tpl = CreateShabbyTemplate();

            var eval = ShamefulNewsRule.Evaluate(evt, teller, player, world, _ => tpl, null, config, 100.0, player);
            Assert.False(eval.CanTell);
            Assert.Equal(ShamefulSpeakerRow.Friend, eval.Evaluations[0].Row);
            Assert.Equal(ShamefulListenerCol.Unrelated, eval.Evaluations[0].Col);
            Assert.Equal("shameful: shamed_lord speaker Friend listener Unrelated", eval.HeldBackReason);
        }

        [Fact]
        public void PlayerInteraction_FriendTells_PlayerWhoIsShamedFriend()
        {
            var world = new FakeDialogueWorld();
            var config = new VividWorldConfig();
            string shamed = "shamed_lord";
            string teller = "teller_friend";
            string player = "player";

            world.SetAffection(teller, shamed, 40); // teller is Friend of shamed
            world.SetAffection(player, shamed, 35); // player is Friend of shamed (affection >= 30)

            var evt = CreateShamefulEvent("evt1", shamed);
            var tpl = CreateShabbyTemplate();

            var eval = ShamefulNewsRule.Evaluate(evt, teller, player, world, _ => tpl, null, config, 100.0, player);
            Assert.True(eval.CanTell);
            Assert.Equal(ShamefulSpeakerRow.Friend, eval.Evaluations[0].Row);
            Assert.Equal(ShamefulListenerCol.Friend, eval.Evaluations[0].Col);
        }

        [Fact]
        public void PlayerInteraction_EnemyTells_UnrelatedPlayer()
        {
            var world = new FakeDialogueWorld();
            var config = new VividWorldConfig();
            string shamed = "shamed_lord";
            string teller = "teller_enemy";
            string player = "player";

            world.SetAffection(teller, shamed, -40); // teller is Enemy of shamed
            world.SetAffection(player, shamed, 0);    // player is Unrelated

            var evt = CreateShamefulEvent("evt1", shamed);
            var tpl = CreateShabbyTemplate();

            var eval = ShamefulNewsRule.Evaluate(evt, teller, player, world, _ => tpl, null, config, 100.0, player);
            Assert.True(eval.CanTell);
            Assert.Equal(ShamefulSpeakerRow.Enemy, eval.Evaluations[0].Row);
            Assert.Equal(ShamefulListenerCol.Unrelated, eval.Evaluations[0].Col);
        }

        [Fact]
        public void PlayerInteraction_OweTellsNobody_EvenFriendPlayer()
        {
            var world = new FakeDialogueWorld();
            var config = new VividWorldConfig();
            string shamed = "shamed_lord";
            string teller = "teller_owe";
            string player = "player";

            // teller owes shamed: low affection, but has a favor (grudgeNet >= threshold)
            world.SetAffection(teller, shamed, -40);
            world.Grudges[(teller, shamed)] = new List<GrudgeEntry>
            {
                new GrudgeEntry { FromHeroId = teller, AboutHeroId = shamed, Scope = GrudgeScope.Personal, Requested = 25.0, Delta = 25, Day = 100.0 }
            };
            world.SetAffection(player, shamed, 50); // player is close friend of shamed

            var evt = CreateShamefulEvent("evt1", shamed);
            var tpl = CreateShabbyTemplate();

            var eval = ShamefulNewsRule.Evaluate(evt, teller, player, world, _ => tpl, null, config, 100.0, player);
            Assert.False(eval.CanTell);
            Assert.Equal(ShamefulSpeakerRow.Owe, eval.Evaluations[0].Row);
            Assert.Equal(ShamefulListenerCol.Friend, eval.Evaluations[0].Col);
            Assert.Equal("shameful: shamed_lord speaker Owe listener Friend", eval.HeldBackReason);
        }

        [Fact]
        public void PlayerInteraction_PlayerSpouse_CountsAsFriend()
        {
            var world = new FakeDialogueWorld();
            var config = new VividWorldConfig();
            string shamed = "shamed_spouse";
            string teller = "teller_friend";
            string player = "player";

            world.SetAffection(teller, shamed, 40);
            world.PlayerSpouses.Add(shamed); // listener is player, and shamed is player's spouse

            var evt = CreateShamefulEvent("evt1", shamed);
            var tpl = CreateShabbyTemplate();

            var eval = ShamefulNewsRule.Evaluate(evt, teller, player, world, _ => tpl, null, config, 100.0, player);
            Assert.True(eval.CanTell);
            Assert.Equal(ShamefulListenerCol.Friend, eval.Evaluations[0].Col);
        }

        // ──────────────── Event Type & Feelings Category Tests ────────────────

        [Fact]
        public void HeroExecuted_IsNotShameful()
        {
            var world = new FakeDialogueWorld();
            var config = new VividWorldConfig();
            var evt = CreateShamefulEvent("evt_exec", "lord_a", "killer", "hero_executed");
            var tpl = CreateExecutedTemplate();

            var eval = ShamefulNewsRule.Evaluate(evt, "teller", "listener", world, _ => tpl, null, config, 100.0);
            Assert.False(eval.IsShameful);
            Assert.True(eval.CanTell);
            Assert.Empty(eval.Evaluations);
        }

        [Fact]
        public void BrashCategory_IsNotShameful()
        {
            var world = new FakeDialogueWorld();
            var config = new VividWorldConfig();
            var evt = CreateShamefulEvent("evt_brash", "lord_a", "brash_role", "brash_type");
            var tpl = CreateBrashTemplate();

            var eval = ShamefulNewsRule.Evaluate(evt, "teller", "listener", world, _ => tpl, null, config, 100.0);
            Assert.False(eval.IsShameful);
            Assert.True(eval.CanTell);
            Assert.Empty(eval.Evaluations);
        }

        [Fact]
        public void ShabbyCategory_IsShameful()
        {
            var evt = CreateShamefulEvent("evt_shabby", "lord_a");
            var tpl = CreateShabbyTemplate();
            var shamed = ShamefulNewsRule.GetShamedPersons(evt, _ => tpl);
            Assert.Single(shamed);
            Assert.Equal("lord_a", shamed[0].HeroId);
        }

        [Fact]
        public void ExecutorCategoryOtherEventType_IsShameful()
        {
            var evt = CreateShamefulEvent("evt_poison", "lord_a", "killer", "conduct_poisoned");
            var tpl = CreateExecutorTemplate("conduct_poisoned");
            var shamed = ShamefulNewsRule.GetShamedPersons(evt, _ => tpl);
            Assert.Single(shamed);
            Assert.Equal("lord_a", shamed[0].HeroId);
            Assert.Equal("killer", shamed[0].Role);
        }

        // ──────────────── Multi-person & Edge Cases ────────────────

        [Fact]
        public void TwoShamedPersons_OneBlocks_BlocksAll()
        {
            var world = new FakeDialogueWorld();
            var config = new VividWorldConfig();
            string speaker = "speaker";
            string listener = "listener";
            string shamed1 = "shamed1";
            string shamed2 = "shamed2";

            // For shamed1: speaker is Friend (aff=40), listener is Unrelated -> Blocked!
            world.SetAffection(speaker, shamed1, 40);
            world.SetAffection(listener, shamed1, 0);

            // For shamed2: speaker is Neutral (aff=0), listener is Unrelated -> CanTell
            world.SetAffection(speaker, shamed2, 0);
            world.SetAffection(listener, shamed2, 0);

            var evt = new WorldEvent
            {
                EventId = "evt_dual",
                Type = "dual_shabby",
                Day = 10.0,
                Origin = EventOrigin.Public,
                Participants = new Dictionary<string, string>
                {
                    ["victim1"] = shamed1,
                    ["victim2"] = shamed2
                }
            };
            var tpl = new EventTemplate
            {
                Type = "dual_shabby",
                Feelings = new Dictionary<string, string>
                {
                    ["victim1"] = "shabby",
                    ["victim2"] = "shabby"
                }
            };

            var eval = ShamefulNewsRule.Evaluate(evt, speaker, listener, world, _ => tpl, null, config, 100.0);
            Assert.False(eval.CanTell);
            Assert.Equal(shamed1, eval.BlockingHeroId);
            Assert.Equal(ShamefulSpeakerRow.Friend, eval.BlockingRow);
            Assert.Equal(ShamefulListenerCol.Unrelated, eval.BlockingCol);
            Assert.Equal(2, eval.Evaluations.Count);
        }

        [Fact]
        public void SpeakerIsShamedPerson_ExcludesSelf()
        {
            var world = new FakeDialogueWorld();
            var config = new VividWorldConfig();
            string speaker = "speaker_shamed";
            string listener = "listener";

            var evt = CreateShamefulEvent("evt1", speaker);
            var tpl = CreateShabbyTemplate();

            // Speaker is the only shamed person -> excluded -> rule does not apply
            var eval = ShamefulNewsRule.Evaluate(evt, speaker, listener, world, _ => tpl, null, config, 100.0);
            Assert.False(eval.IsShameful);
            Assert.True(eval.CanTell);
            Assert.Empty(eval.Evaluations);
        }

        [Fact]
        public void ConfigDisabled_BehaviorUnchanged()
        {
            var world = new FakeDialogueWorld();
            var config = new VividWorldConfig();
            config.Propagation.ShamefulNews.Enabled = false;

            string speaker = "speaker_friend";
            string listener = "listener_unrelated";
            string shamed = "shamed_lord";

            world.SetAffection(speaker, shamed, 40);
            world.SetAffection(listener, shamed, 0);

            var evt = CreateShamefulEvent("evt1", shamed);
            var tpl = CreateShabbyTemplate();

            var eval = ShamefulNewsRule.Evaluate(evt, speaker, listener, world, _ => tpl, null, config, 100.0);
            Assert.True(eval.CanTell); // config disabled -> CanTell defaults to true
            Assert.False(eval.IsShameful);
        }

        [Fact]
        public void GrudgeCalculation_ExcludesOwnEvent()
        {
            var world = new FakeDialogueWorld();
            var config = new VividWorldConfig();
            string speaker = "speaker";
            string listener = "listener";
            string shamed = "shamed";

            world.SetAffection(speaker, shamed, 35); // base affection >= 30 -> Close/Friend

            // Suppose this event created a huge grudge against shamed:
            world.Grudges[(speaker, shamed)] = new List<GrudgeEntry>
            {
                new GrudgeEntry
                {
                    FromHeroId = speaker,
                    AboutHeroId = shamed,
                    EventId = "evt_scandal", // same event ID!
                    Scope = GrudgeScope.Personal,
                    Requested = 50.0,
                    Delta = 50,
                    Day = 100.0
                }
            };

            var evt = CreateShamefulEvent("evt_scandal", shamed);
            var tpl = CreateShabbyTemplate();

            // When evaluating evt_scandal, its own grudge must be excluded.
            // Therefore, grudgeNet is 0, affection is 35 -> speaker row is Friend!
            var eval = ShamefulNewsRule.Evaluate(evt, speaker, listener, world, _ => tpl, null, config, 100.0);
            Assert.Single(eval.Evaluations);
            Assert.Equal(0.0, eval.Evaluations[0].GrudgeNet);
            Assert.Equal(ShamefulSpeakerRow.Friend, eval.Evaluations[0].Row);
        }

        // ──────────────── RumorEngine & Logging Tests ────────────────

        private sealed class TestCapturingLogSink : ILogSink
        {
            public List<string> Infos { get; } = new();
            public List<string> Warns { get; } = new();
            public List<string> Errors { get; } = new();

            public void Info(string msg) => Infos.Add(msg);
            public void Warn(string msg) => Warns.Add(msg);
            public void Error(string msg, Exception? ex = null) => Errors.Add(msg);
        }

        [Fact]
        public void NullFeelingWorld_LogsWarnOnceAndDoesNotBlock()
        {
            var cfg = new VividWorldConfig();
            var rng = new SplitMix64Rng();
            var channel = new FakePropagationChannel();
            var traits = new FakeHeroTraitLookup();
            traits.Set(new TraitProfile { HeroId = "spk", IsAlive = true, IsLord = true });
            traits.Set(new TraitProfile { HeroId = "list", IsAlive = true, IsLord = true });
            var retention = FactRetentionPolicies.Create(cfg, rng, 42);
            var tpl = CreateShabbyTemplate();

            var sink = new TestCapturingLogSink();
            var engine = new RumorEngine(cfg, retention, NullEmbellishmentPolicy.Instance, channel, traits, rng, 42, "player", _ => tpl, log: sink);
            // Engine has FeelingWorld = null

            string speaker = "spk";
            string listener = "list";
            string shamed = "shamed";

            channel.AddLink(speaker, listener, ChannelKind.SameSettlement, 0, ownRelation: 0, isFamily: false);
            var evt = CreateShamefulEvent("evt1", shamed);
            evt.KnownBy.Add(new KnownByEntry { HeroId = speaker, Hop = 1 });

            // Propagate multiple times
            engine.PropagateFromTeller(evt, speaker, 100.0, 12);
            engine.PropagateFromTeller(evt, speaker, 100.0, 12);

            // Should warn exactly once
            Assert.Single(sink.Warns);
            Assert.Contains("FeelingWorld", sink.Warns[0]);
        }

        [Fact]
        public void PropagateFromTeller_BlocksHeldBackContact()
        {
            var cfg = new VividWorldConfig();
            cfg.Debug.LogTellerTurns = true;
            var rng = new SplitMix64Rng();
            var channel = new FakePropagationChannel();
            var traits = new FakeHeroTraitLookup();
            traits.Set(new TraitProfile { HeroId = "spk", IsAlive = true, IsLord = true });
            traits.Set(new TraitProfile { HeroId = "list", IsAlive = true, IsLord = true });
            var retention = FactRetentionPolicies.Create(cfg, rng, 42);
            var tpl = CreateShabbyTemplate();

            var world = new FakeDialogueWorld();
            string speaker = "spk";
            string listener = "list";
            string shamed = "shamed";

            // speaker is Friend of shamed, listener is Unrelated -> HeldBack!
            world.SetAffection(speaker, shamed, 40);
            world.SetAffection(listener, shamed, 0);

            channel.AddLink(speaker, listener, ChannelKind.SameSettlement, 10, ownRelation: 0, isFamily: false);

            var engine = new RumorEngine(cfg, retention, NullEmbellishmentPolicy.Instance, channel, traits, rng, 42, "player", _ => tpl);
            engine.FeelingWorld = world;

            var evt = CreateShamefulEvent("evt1", shamed);
            evt.KnownBy.Add(new KnownByEntry { HeroId = speaker, Hop = 1 });

            var outcome = engine.PropagateFromTeller(evt, speaker, 100.0, 12);

            // Contact was held back due to shameful news
            Assert.False(evt.IsKnownBy(listener));
            Assert.NotNull(outcome.Contacts);
            Assert.Single(outcome.Contacts);
            var contact = outcome.Contacts[0];
            Assert.Equal(ContactStatus.HeldBackShameful, contact.Status);
            Assert.Equal($"shameful: {shamed} speaker Friend listener Unrelated", contact.HeldBackReason);
        }

        [Fact]
        public void TellTierLogFormatter_FormatsShamefulHeldBack()
        {
            var obs = new ContactObservation(
                "contact1",
                ChannelKind.SameSettlement,
                10,
                0.0,
                ContactStatus.HeldBackShameful,
                ownRelation: 0,
                isFamily: false,
                willingness: 10.0,
                tier: TellTier.Familiar,
                heldBackReason: "shameful: hero_shamed speaker Friend listener Unrelated");

            string line = TellTierLogFormatter.FormatTurnLine("speaker", "evt1", 6, false, new[] { obs });
            Assert.Contains("held back (shameful: hero_shamed speaker Friend listener Unrelated)", line);

            var tally = new TellTierTally();
            tally.Record(obs, 6, 8);
            string daily = TellTierLogFormatter.FormatDailyLine(1, tally);
            Assert.Contains("shameful 1 (in-person 1, remote 0)", daily);
        }

        // ──────────────── RumorOfferSelector Dialogue Tests ────────────────

        [Fact]
        public void RumorOfferSelector_RejectsCandidateWithHeldBackShameful()
        {
            var cfg = new VividWorldConfig();
            var rng = new SplitMix64Rng();
            var channel = new FakePropagationChannel();
            var traits = new FakeHeroTraitLookup();
            var retention = FactRetentionPolicies.Create(cfg, rng, 42);
            var tpl = CreateShabbyTemplate();

            string speaker = "teller_lord";
            string player = "player_hero";
            string shamed = "shamed_lord";

            traits.Set(new TraitProfile { HeroId = speaker, IsAlive = true, IsLord = true });

            var world = new FakeDialogueWorld();
            world.SetAffection(speaker, shamed, 40); // speaker is Friend of shamed
            world.SetAffection(player, shamed, 0);   // player is Unrelated
            world.SetAffection(speaker, player, 30); // speaker knows player well

            var engine = new RumorEngine(cfg, retention, NullEmbellishmentPolicy.Instance, channel, traits, rng, 42, player, _ => tpl);
            engine.FeelingWorld = world;

            var evt = CreateShamefulEvent("scandal_1", shamed);
            evt.KnownBy.Add(new KnownByEntry { HeroId = speaker, Hop = 1 });

            var selector = new RumorOfferSelector(
                cfg,
                engine,
                player,
                RumorMode.Casual,
                _ => tpl,
                traits,
                dialogueWorld: world,
                getEvent: id => id == "scandal_1" ? evt : null);

            var tellerProfile = new HeroSocialProfile { HeroId = speaker, RelationWithPlayer = 30 };
            var candidates = new List<RumorCandidate>
            {
                new RumorCandidate { Event = evt, TellerHop = 1 }
            };

            var decision = selector.DecideOnAsk(tellerProfile, candidates, 100.0);

            // Should be filtered out
            Assert.Equal(AskRefusal.AllCandidatesFiltered, decision.Refusal);
            Assert.Contains(decision.FilterNotes, note => note.Contains("scandal_1: shameful, held back (shamed_lord speaker Friend listener Unrelated)"));
        }

        // ──────────────── Config Merge Tests ────────────────

        [Fact]
        public void ConfigMerge_AddsShamefulNewsToExistingJson()
        {
            var existing = JObject.Parse("{ \"configVersion\": 1, \"propagation\": { \"eventsPerHourlyTick\": 5 } }");
            var canonical = JObject.Parse(VividJson.Write(new VividWorldConfig().Normalize()));

            var result = ConfigMerge.AddMissingKeys(existing, canonical);
            Assert.Contains("propagation.shamefulNews.enabled", result.AddedPaths);
            Assert.True((bool)result.Merged.SelectToken("propagation.shamefulNews.enabled")!);
        }
    }
}
