using System.Collections.Generic;
using System.Linq;
using VividWorld.Core.Channels;
using VividWorld.Core.Config;
using VividWorld.Core.Events;
using VividWorld.Core.Rumors;
using VividWorld.Core.Tests.Fakes;
using VividWorld.Core.Util;
using Xunit;

namespace VividWorld.Core.Tests
{
    public class EngineTests
    {
        private (RumorEngine engine, FakePropagationChannel channel, FakeHeroTraitLookup traits, VividWorldConfig cfg)
            CreateEngine(string playerHeroId = "player", long seed = 42L, VividWorldConfig? customConfig = null, IDeterministicRng? customRng = null)
        {
            var cfg = customConfig ?? new VividWorldConfig();
            var rng = customRng ?? new SplitMix64Rng();
            var channel = new FakePropagationChannel();
            var traits = new FakeHeroTraitLookup();
            var retention = FactRetentionPolicies.Create(cfg, rng, seed);
            var embellishment = NullEmbellishmentPolicy.Instance;
            var engine = new RumorEngine(cfg, retention, embellishment, channel, traits, rng, seed, playerHeroId);
            return (engine, channel, traits, cfg);
        }

        /// <summary>
        /// 規格 §6.4／§6.6.0：洩漏迴圈絕不把玩家當 insider 擲骰。
        /// 玩家可以是秘密的 hop 0 當事人——甚至是唯一的當事人——但他的嘴只能由他自己開。
        /// §6.6.3 明訂「玩家說出口就是一次 Leak」是唯一一條玩家能觸發的洩漏路徑（M10），
        /// 所以在 M10 之前，玩家獨知的秘密是絕對安全的。這是刻意的。
        /// </summary>
        [Fact]
        public void Leak_NeverPicksThePlayerAsLeaker()
        {
            // BaseChance 拉到 1.0：任何被納入 insider 的人都必定洩漏。
            var cfg = new VividWorldConfig();
            cfg.Leak.BaseChancePerInsiderPerDay = 1.0;
            cfg.Leak.GraceDays = 0.0;

            var (engine, _, traits, _) = CreateEngine(playerHeroId: "player", customConfig: cfg);
            traits.Set(new TraitProfile { HeroId = "player", IsAlive = true, IsLord = true });
            traits.Set(new TraitProfile { HeroId = "lord_1", IsAlive = true, IsLord = true });

            // 情境一：玩家是唯一的知情當事人 → 永遠不洩漏
            var soloEvt = new WorldEvent
            {
                EventId = "evt_player_only",
                Origin = EventOrigin.Secret,
                Day = 100.0,
                Participants = new Dictionary<string, string> { ["mastermind"] = "player" },
                KnownBy = { new KnownByEntry { HeroId = "player", Hop = 0, LearnedDay = 100.0 } }
            };

            for (double day = 101.0; day <= 400.0; day += 1.0)
            {
                var outcome = engine.TryLeak(soloEvt, day);
                Assert.False(outcome.Leaked);
            }
            Assert.False(soloEvt.State.Leaked);
            Assert.Null(soloEvt.State.LeakerHeroId);

            // 情境二：玩家與一名 NPC 同為知情者 → 洩漏者只可能是那名 NPC
            var sharedEvt = new WorldEvent
            {
                EventId = "evt_shared",
                Origin = EventOrigin.Secret,
                Day = 100.0,
                Participants = new Dictionary<string, string>
                {
                    ["mastermind"] = "player",
                    ["agent"] = "lord_1"
                },
                KnownBy =
                {
                    new KnownByEntry { HeroId = "player", Hop = 0, LearnedDay = 100.0 },
                    new KnownByEntry { HeroId = "lord_1", Hop = 0, LearnedDay = 100.0 }
                }
            };

            var shared = engine.TryLeak(sharedEvt, 101.0);
            Assert.True(shared.Leaked);
            Assert.Equal("lord_1", shared.LeakerHeroId);
        }

        [Fact]
        public void UnleakedSecret_IsInvisible_NoPropagation_NoFacts_NoOffers()
        {
            var (engine, channel, traits, _) = CreateEngine();
            traits.Set(new TraitProfile { HeroId = "lord_1", IsAlive = true, IsLord = true });
            channel.AddLink("lord_1", "lord_2", ChannelKind.SameSettlement, 1.0);

            var evt = new WorldEvent
            {
                EventId = "evt_secret_unleaked",
                Origin = EventOrigin.Secret,
                Day = 1.0,
                Facts = new List<Fact>
                {
                    new Fact { Id = "f_1", Category = FactCategory.Who, Fragility = 1, TextId = "txt_who" }
                },
                KnownBy = new List<KnownByEntry>
                {
                    new KnownByEntry { HeroId = "lord_1", Hop = 0, LearnedDay = 1.0 }
                },
                State = new RumorState { Leaked = false }
            };

            // FactsAtHop 兩個多載皆回傳空
            Assert.Empty(engine.FactsAtHop(evt, 1));
            Assert.Empty(engine.FactsAtHop(evt, 1, "lord_1"));

            // PropagateOnce 回傳 Nothing
            var outcome = engine.PropagateOnce(evt, 2.0, 12);
            Assert.False(outcome.DidAnything);
            Assert.Null(outcome.TellerHeroId);
            Assert.Empty(outcome.NewKnowers);

            // ShouldGoDormant 回傳 false
            Assert.False(engine.ShouldGoDormant(evt, 2.0));
        }

        [Fact]
        public void LeakedSecret_FirstRetelling_ComesFromTheLeaker()
        {
            var (engine, channel, traits, cfg) = CreateEngine();
            cfg.Propagation.BaseTellChancePerContact = 1.0;

            traits.Set(new TraitProfile { HeroId = "leaker_lord", IsAlive = true, IsLord = true });
            traits.Set(new TraitProfile { HeroId = "insider_lord", IsAlive = true, IsLord = true });
            traits.Set(new TraitProfile { HeroId = "contact_lord", IsAlive = true, IsLord = true });

            channel.AddLink("leaker_lord", "contact_lord", ChannelKind.SameSettlement, 1.0);
            channel.AddLink("insider_lord", "contact_lord", ChannelKind.SameSettlement, 1.0);

            var evt = new WorldEvent
            {
                EventId = "evt_secret_leaked",
                Origin = EventOrigin.Secret,
                Day = 1.0,
                KnownBy = new List<KnownByEntry>
                {
                    new KnownByEntry { HeroId = "leaker_lord", Hop = 0, LearnedDay = 1.0 },
                    new KnownByEntry { HeroId = "insider_lord", Hop = 0, LearnedDay = 1.0 }
                },
                State = new RumorState
                {
                    Leaked = true,
                    LeakedDay = 2.0,
                    LeakerHeroId = "leaker_lord"
                }
            };

            // 剛洩漏且無 hop 1 知情者時，候選只有洩漏者
            var outcome = engine.PropagateOnce(evt, 2.0, 10);
            Assert.True(outcome.DidAnything);
            Assert.Equal("leaker_lord", outcome.TellerHeroId);
        }

        [Fact]
        public void Propagation_NewKnowerEntersAtTellerHopPlusOne()
        {
            var (engine, channel, traits, cfg) = CreateEngine();
            cfg.Propagation.BaseTellChancePerContact = 1.0;

            traits.Set(new TraitProfile { HeroId = "teller_hop1", IsAlive = true, IsLord = true });
            traits.Set(new TraitProfile { HeroId = "receiver_lord", IsAlive = true, IsLord = true });

            channel.AddLink("teller_hop1", "receiver_lord", ChannelKind.SameSettlement, 1.0);

            var evt = new WorldEvent
            {
                EventId = "evt_pub_hop1",
                Origin = EventOrigin.Public,
                Day = 1.0,
                DramaWeight = 3,
                KnownBy = new List<KnownByEntry>
                {
                    new KnownByEntry { HeroId = "teller_hop1", Hop = 1, LearnedDay = 1.0 }
                }
            };

            var outcome = engine.PropagateOnce(evt, 3.0, 10);

            Assert.True(outcome.DidAnything);
            Assert.Single(outcome.NewKnowers);
            var newKnower = outcome.NewKnowers[0];
            Assert.Equal("receiver_lord", newKnower.HeroId);
            Assert.Equal(2, newKnower.Hop); // 1 + 1
            Assert.Equal("teller_hop1", newKnower.SourceHeroId);
            Assert.Equal(3.0, newKnower.LearnedDay);

            Assert.True(evt.IsKnownBy("receiver_lord"));
            Assert.Equal(2, evt.EntryFor("receiver_lord")?.Hop);
        }

        [Fact]
        public void Propagation_NeverReAddsAnExistingKnower()
        {
            var (engine, channel, traits, cfg) = CreateEngine();
            cfg.Propagation.BaseTellChancePerContact = 1.0;

            traits.Set(new TraitProfile { HeroId = "teller", IsAlive = true, IsLord = true });
            traits.Set(new TraitProfile { HeroId = "already_knower", IsAlive = true, IsLord = true });

            channel.AddLink("teller", "already_knower", ChannelKind.SameSettlement, 1.0);

            var evt = new WorldEvent
            {
                EventId = "evt_pub_readd",
                Origin = EventOrigin.Public,
                Day = 1.0,
                DramaWeight = 3,
                KnownBy = new List<KnownByEntry>
                {
                    new KnownByEntry { HeroId = "teller", Hop = 0, LearnedDay = 1.0 },
                    new KnownByEntry { HeroId = "already_knower", Hop = 1, LearnedDay = 1.0 }
                }
            };

            var outcome = engine.PropagateOnce(evt, 2.0, 10);

            Assert.Empty(outcome.NewKnowers);
            Assert.Equal(2, evt.KnownBy.Count);
        }

        [Fact]
        public void Propagation_NeverAddsThePlayer()
        {
            var (engine, channel, traits, cfg) = CreateEngine("main_hero");
            cfg.Propagation.BaseTellChancePerContact = 1.0;

            traits.Set(new TraitProfile { HeroId = "teller", IsAlive = true, IsLord = true });
            traits.Set(new TraitProfile { HeroId = "main_hero", IsAlive = true, IsLord = true });

            channel.AddLink("teller", "main_hero", ChannelKind.SameSettlement, 1.0);

            var evt = new WorldEvent
            {
                EventId = "evt_pub_player_target",
                Origin = EventOrigin.Public,
                Day = 1.0,
                DramaWeight = 3,
                KnownBy = new List<KnownByEntry>
                {
                    new KnownByEntry { HeroId = "teller", Hop = 0, LearnedDay = 1.0 }
                }
            };

            var outcome = engine.PropagateOnce(evt, 2.0, 10);

            Assert.Empty(outcome.NewKnowers);
            Assert.False(evt.IsKnownBy("main_hero"));
        }

        [Fact]
        public void Propagation_NeverPicksThePlayerAsTeller()
        {
            var (engine, channel, traits, cfg) = CreateEngine("main_hero");
            cfg.Propagation.PlayerCanTell = false; // default false
            cfg.Propagation.BaseTellChancePerContact = 1.0;

            traits.Set(new TraitProfile { HeroId = "main_hero", IsAlive = true, IsLord = true });
            traits.Set(new TraitProfile { HeroId = "npc_knower", IsAlive = true, IsLord = true });
            traits.Set(new TraitProfile { HeroId = "npc_receiver", IsAlive = true, IsLord = true });

            channel.AddLink("main_hero", "npc_receiver", ChannelKind.SameSettlement, 1.0);
            channel.AddLink("npc_knower", "npc_receiver", ChannelKind.SameSettlement, 1.0);

            var evt = new WorldEvent
            {
                EventId = "evt_pub_player_teller",
                Origin = EventOrigin.Public,
                Day = 1.0,
                DramaWeight = 3,
                KnownBy = new List<KnownByEntry>
                {
                    new KnownByEntry { HeroId = "main_hero", Hop = 0, LearnedDay = 1.0 },
                    new KnownByEntry { HeroId = "npc_knower", Hop = 0, LearnedDay = 1.0 }
                }
            };

            for (int h = 0; h < 24; h++)
            {
                var outcome = engine.PropagateOnce(evt, 2.0, h);
                Assert.NotEqual("main_hero", outcome.TellerHeroId);
            }
        }

        [Fact]
        public void Propagation_NeverAddsAnIneligibleContact()
        {
            var (engine, channel, traits, cfg) = CreateEngine();
            cfg.Propagation.BaseTellChancePerContact = 1.0;

            traits.Set(new TraitProfile { HeroId = "teller", IsAlive = true, IsLord = true });
            // Notable: neither lord nor wanderer
            traits.Set(new TraitProfile { HeroId = "merchant_notable", IsAlive = true, IsLord = false, IsWanderer = false });

            channel.AddLink("teller", "merchant_notable", ChannelKind.SameSettlement, 1.0);

            var evt = new WorldEvent
            {
                EventId = "evt_pub_notable",
                Origin = EventOrigin.Public,
                Day = 1.0,
                DramaWeight = 3,
                KnownBy = new List<KnownByEntry>
                {
                    new KnownByEntry { HeroId = "teller", Hop = 0, LearnedDay = 1.0 }
                }
            };

            var outcome = engine.PropagateOnce(evt, 2.0, 10);

            Assert.Empty(outcome.NewKnowers);
            Assert.False(evt.IsKnownBy("merchant_notable"));
        }

        [Fact]
        public void Propagation_SkipsKnowerWithNullTraitProfile_ButKeepsTheEntry()
        {
            var (engine, channel, traits, cfg) = CreateEngine();
            cfg.Propagation.BaseTellChancePerContact = 1.0;

            // traits.Of("hero_unregistered") returns null
            traits.Set(new TraitProfile { HeroId = "contact", IsAlive = true, IsLord = true });
            channel.AddLink("hero_unregistered", "contact", ChannelKind.SameSettlement, 1.0);

            var evt = new WorldEvent
            {
                EventId = "evt_pub_null_trait",
                Origin = EventOrigin.Public,
                Day = 1.0,
                DramaWeight = 3,
                KnownBy = new List<KnownByEntry>
                {
                    new KnownByEntry { HeroId = "hero_unregistered", Hop = 0, LearnedDay = 1.0 }
                }
            };

            var outcome = engine.PropagateOnce(evt, 2.0, 10);

            Assert.False(outcome.DidAnything);
            Assert.Null(outcome.TellerHeroId);
            // 絕不從 KnownBy 移除
            Assert.Single(evt.KnownBy);
            Assert.Equal("hero_unregistered", evt.KnownBy[0].HeroId);
        }

        [Fact]
        public void Propagation_StopsAtDramaMaxHop()
        {
            var (engine, channel, traits, cfg) = CreateEngine();
            cfg.Propagation.BaseTellChancePerContact = 1.0;

            // Drama 1 -> DramaMaxHop is 2
            traits.Set(new TraitProfile { HeroId = "teller_hop2", IsAlive = true, IsLord = true });
            traits.Set(new TraitProfile { HeroId = "contact_lord", IsAlive = true, IsLord = true });
            channel.AddLink("teller_hop2", "contact_lord", ChannelKind.SameSettlement, 1.0);

            var evt = new WorldEvent
            {
                EventId = "evt_pub_maxhop",
                Origin = EventOrigin.Public,
                Day = 1.0,
                DramaWeight = 1,
                KnownBy = new List<KnownByEntry>
                {
                    new KnownByEntry { HeroId = "teller_hop2", Hop = 2, LearnedDay = 2.0 }
                }
            };

            var outcome = engine.PropagateOnce(evt, 3.0, 10);

            Assert.False(outcome.DidAnything);
            Assert.Empty(outcome.NewKnowers);
        }

        [Fact]
        public void Propagation_MundaneEvent_NeverPassesHopTwo()
        {
            var (engine, channel, traits, cfg) = CreateEngine();
            cfg.Propagation.BaseTellChancePerContact = 1.0;

            // Chain: H0 -> H1 -> H2 -> H3 -> H4
            for (int i = 0; i <= 4; i++)
            {
                traits.Set(new TraitProfile { HeroId = $"h_{i}", IsAlive = true, IsLord = true });
                if (i < 4)
                {
                    channel.AddLink($"h_{i}", $"h_{i + 1}", ChannelKind.SameSettlement, 1.0);
                }
            }

            var evt = new WorldEvent
            {
                EventId = "evt_mundane",
                Origin = EventOrigin.Public,
                Day = 1.0,
                DramaWeight = 1, // MaxHop = 2
                KnownBy = new List<KnownByEntry>
                {
                    new KnownByEntry { HeroId = "h_0", Hop = 0, LearnedDay = 1.0 }
                }
            };

            // Run hourly propagation ticks across days
            for (int d = 2; d <= 25; d++)
            {
                for (int h = 0; h < 24; h++)
                {
                    engine.PropagateOnce(evt, d, h);
                }
            }

            // H0, H1, H2 can be added, but H3 and H4 can NEVER be added
            Assert.True(evt.IsKnownBy("h_0"));
            Assert.True(evt.IsKnownBy("h_1"));
            Assert.True(evt.IsKnownBy("h_2"));
            Assert.False(evt.IsKnownBy("h_3"));
            Assert.False(evt.IsKnownBy("h_4"));
            Assert.Equal(2, evt.MaxHop());
        }

        [Fact]
        public void Propagation_DramaticEvent_ReachesHopFive()
        {
            var (engine, channel, traits, cfg) = CreateEngine();
            cfg.Propagation.BaseTellChancePerContact = 1.0;

            // Chain: H0 -> H1 -> H2 -> H3 -> H4 -> H5
            for (int i = 0; i <= 5; i++)
            {
                traits.Set(new TraitProfile { HeroId = $"h_{i}", IsAlive = true, IsLord = true });
                if (i < 5)
                {
                    channel.AddLink($"h_{i}", $"h_{i + 1}", ChannelKind.SameSettlement, 1.0);
                }
            }

            var evt = new WorldEvent
            {
                EventId = "evt_dramatic",
                Origin = EventOrigin.Public,
                Day = 1.0,
                DramaWeight = 5, // MaxHop = 6
                KnownBy = new List<KnownByEntry>
                {
                    new KnownByEntry { HeroId = "h_0", Hop = 0, LearnedDay = 1.0 }
                }
            };

            for (int d = 2; d <= 20; d++)
            {
                for (int h = 0; h < 24; h++)
                {
                    engine.PropagateOnce(evt, d, h);
                    if (evt.IsKnownBy("h_5")) break;
                }
                if (evt.IsKnownBy("h_5")) break;
            }

            Assert.True(evt.IsKnownBy("h_5"));
            Assert.Equal(5, evt.EntryFor("h_5")?.Hop);
        }

        [Fact]
        public void Propagation_QueriesAtMostOneContactSetPerCall()
        {
            var (engine, channel, traits, cfg) = CreateEngine();
            cfg.Propagation.BaseTellChancePerContact = 1.0;

            traits.Set(new TraitProfile { HeroId = "lord_1", IsAlive = true, IsLord = true });
            traits.Set(new TraitProfile { HeroId = "lord_2", IsAlive = true, IsLord = true });
            channel.AddLink("lord_1", "lord_2", ChannelKind.SameSettlement, 1.0);

            var evt = new WorldEvent
            {
                EventId = "evt_pub_query_count",
                Origin = EventOrigin.Public,
                Day = 1.0,
                DramaWeight = 3,
                KnownBy = new List<KnownByEntry>
                {
                    new KnownByEntry { HeroId = "lord_1", Hop = 0, LearnedDay = 1.0 }
                }
            };

            Assert.Equal(0, channel.QueryCount);

            // First call -> exactly 1 ContactsOf call
            engine.PropagateOnce(evt, 2.0, 10);
            Assert.Equal(1, channel.QueryCount);

            // Second call -> exactly 2 ContactsOf calls total
            engine.PropagateOnce(evt, 2.0, 11);
            Assert.Equal(2, channel.QueryCount);
        }

        [Fact]
        public void Propagation_UpdatesLastPropagatedDay_AndLastNewKnowerDayOnlyOnAddition()
        {
            var (engine, channel, traits, cfg) = CreateEngine();
            cfg.Propagation.BaseTellChancePerContact = 1.0;

            traits.Set(new TraitProfile { HeroId = "teller", IsAlive = true, IsLord = true });
            traits.Set(new TraitProfile { HeroId = "receiver", IsAlive = true, IsLord = true });
            channel.AddLink("teller", "receiver", ChannelKind.SameSettlement, 1.0);

            var evt = new WorldEvent
            {
                EventId = "evt_pub_state_days",
                Origin = EventOrigin.Public,
                Day = 1.0,
                DramaWeight = 3,
                KnownBy = new List<KnownByEntry>
                {
                    new KnownByEntry { HeroId = "teller", Hop = 0, LearnedDay = 1.0 }
                }
            };

            Assert.Equal(-1, evt.State.LastPropagatedDay);
            Assert.Equal(-1, evt.State.LastNewKnowerDay);

            // 第一次傳播：成功加入 receiver
            engine.PropagateOnce(evt, 2.0, 10);
            Assert.Equal(2.0, evt.State.LastPropagatedDay);
            Assert.Equal(2.0, evt.State.LastNewKnowerDay);

            // 第二次傳播：teller 再次轉述，但 receiver 已知情，沒有新知情者加入
            engine.PropagateOnce(evt, 5.0, 10);
            Assert.Equal(5.0, evt.State.LastPropagatedDay);
            Assert.Equal(2.0, evt.State.LastNewKnowerDay); // 維持 2.0，未更新為 5.0
        }

        [Theory]
        [InlineData("all_at_max_hop")]
        [InlineData("lifetime_expired")]
        [InlineData("staleness")]
        [InlineData("not_stale_when_minus_one")]
        public void Dormancy_TriggersOnLifetime_OnStaleness_AndOnAllKnowersAtMaxHop(string condition)
        {
            var (engine, _, traits, cfg) = CreateEngine();
            cfg.Scheduling.RumorLifetimeDays = 120.0;
            cfg.Scheduling.StaleDays = 30.0;

            traits.Set(new TraitProfile { HeroId = "hero_1", IsAlive = true, IsLord = true });
            traits.Set(new TraitProfile { HeroId = "hero_2", IsAlive = true, IsLord = true });

            switch (condition)
            {
                case "all_at_max_hop":
                {
                    // Drama 1 -> MaxHop = 2. All knowers at hop 2
                    var evt = new WorldEvent
                    {
                        EventId = "evt_dormant_maxhop",
                        Origin = EventOrigin.Public,
                        Day = 10.0,
                        DramaWeight = 1,
                        KnownBy = new List<KnownByEntry>
                        {
                            new KnownByEntry { HeroId = "hero_1", Hop = 2, LearnedDay = 12.0 },
                            new KnownByEntry { HeroId = "hero_2", Hop = 2, LearnedDay = 13.0 }
                        },
                        State = new RumorState { LastNewKnowerDay = 13.0 }
                    };
                    Assert.True(engine.ShouldGoDormant(evt, 15.0));
                    break;
                }
                case "lifetime_expired":
                {
                    // day - evt.Day = 125 > 120
                    var evt = new WorldEvent
                    {
                        EventId = "evt_dormant_lifetime",
                        Origin = EventOrigin.Public,
                        Day = 0.0,
                        DramaWeight = 3,
                        KnownBy = new List<KnownByEntry>
                        {
                            new KnownByEntry { HeroId = "hero_1", Hop = 0, LearnedDay = 0.0 }
                        },
                        State = new RumorState { LastNewKnowerDay = 0.0 }
                    };
                    Assert.True(engine.ShouldGoDormant(evt, 125.0));
                    break;
                }
                case "staleness":
                {
                    // LastNewKnowerDay = 10.0, day = 45.0 -> 45 - 10 = 35 > 30
                    var evt = new WorldEvent
                    {
                        EventId = "evt_dormant_stale",
                        Origin = EventOrigin.Public,
                        Day = 0.0,
                        DramaWeight = 3,
                        KnownBy = new List<KnownByEntry>
                        {
                            new KnownByEntry { HeroId = "hero_1", Hop = 0, LearnedDay = 0.0 }
                        },
                        State = new RumorState { LastNewKnowerDay = 10.0 }
                    };
                    Assert.True(engine.ShouldGoDormant(evt, 45.0));
                    break;
                }
                case "not_stale_when_minus_one":
                {
                    // LastNewKnowerDay = -1.0 且在 StaleDays 內 -> 從未有人新知情但事件仍新鮮，不得觸發停滯
                    var evt = new WorldEvent
                    {
                        EventId = "evt_dormant_new",
                        Origin = EventOrigin.Public,
                        Day = 0.0,
                        DramaWeight = 3,
                        KnownBy = new List<KnownByEntry>
                        {
                            new KnownByEntry { HeroId = "hero_1", Hop = 0, LearnedDay = 0.0 }
                        },
                        State = new RumorState { LastNewKnowerDay = -1.0 }
                    };
                    Assert.False(engine.ShouldGoDormant(evt, 15.0));
                    break;
                }
            }
        }

        [Fact]
        public void Propagation_ChannelWeightIsNotSquared()
        {
            var cfg = new VividWorldConfig();
            cfg.Propagation.BaseTellChancePerContact = 0.5;
            var recordingRng = new RecordingRng();
            var (engine, channel, traits, _) = CreateEngine(customConfig: cfg, customRng: recordingRng);

            traits.Set(new TraitProfile { HeroId = "teller", IsAlive = true, IsLord = true });
            traits.Set(new TraitProfile { HeroId = "contact", IsAlive = true, IsLord = true });

            channel.AddLink("teller", "contact", ChannelKind.Kingdom, weight: 1.0, relation: 50);

            var evt = new WorldEvent
            {
                EventId = "evt_not_squared",
                Origin = EventOrigin.Public,
                Day = 1.0,
                DramaWeight = 3,
                KnownBy = { new KnownByEntry { HeroId = "teller", Hop = 0, LearnedDay = 1.0 } }
            };

            engine.PropagateOnce(evt, 2.0, 10);

            Assert.Single(recordingRng.RecordedProbabilities);
            Assert.Equal(0.275, recordingRng.RecordedProbabilities[0], precision: 4);
        }

        [Fact]
        public void Propagation_UsesInPersonCurveForInPersonKinds()
        {
            var cfg = new VividWorldConfig();
            cfg.Propagation.BaseTellChancePerContact = 1.0;
            var recordingRng = new RecordingRng();
            var (engine, channel, traits, _) = CreateEngine(customConfig: cfg, customRng: recordingRng);

            traits.Set(new TraitProfile { HeroId = "teller", IsAlive = true, IsLord = true });
            traits.Set(new TraitProfile { HeroId = "contact", IsAlive = true, IsLord = true });

            channel.AddLink("teller", "contact", ChannelKind.SameSettlement, weight: 1.0, relation: 40);

            var evt = new WorldEvent
            {
                EventId = "evt_in_person_curve",
                Origin = EventOrigin.Public,
                Day = 1.0,
                DramaWeight = 3,
                KnownBy = { new KnownByEntry { HeroId = "teller", Hop = 0, LearnedDay = 1.0 } }
            };

            engine.PropagateOnce(evt, 2.0, 10);

            Assert.Single(recordingRng.RecordedProbabilities);
            Assert.Equal(0.912, recordingRng.RecordedProbabilities[0], precision: 4);
        }

        [Fact]
        public void Propagation_UsesRemoteCurveForRemoteKinds()
        {
            var cfg = new VividWorldConfig();
            cfg.Propagation.BaseTellChancePerContact = 1.0;
            var recordingRng = new RecordingRng();
            var (engine, channel, traits, _) = CreateEngine(customConfig: cfg, customRng: recordingRng);

            traits.Set(new TraitProfile { HeroId = "teller", IsAlive = true, IsLord = true });
            traits.Set(new TraitProfile { HeroId = "contact", IsAlive = true, IsLord = true });

            channel.AddLink("teller", "contact", ChannelKind.SameClan, weight: 1.0, relation: 40);

            var evt = new WorldEvent
            {
                EventId = "evt_remote_curve",
                Origin = EventOrigin.Public,
                Day = 1.0,
                DramaWeight = 3,
                KnownBy = { new KnownByEntry { HeroId = "teller", Hop = 0, LearnedDay = 1.0 } }
            };

            engine.PropagateOnce(evt, 2.0, 10);

            Assert.Single(recordingRng.RecordedProbabilities);
            Assert.Equal(0.90, recordingRng.RecordedProbabilities[0], precision: 4);
        }

        [Fact]
        public void Propagation_HostileContact_IsSkippedFarMoreOftenThanNeutral()
        {
            var cfg = new VividWorldConfig();
            cfg.Propagation.BaseTellChancePerContact = 0.5;

            var recordingRng = new RecordingRng();
            var (engine, channel, traits, _) = CreateEngine(customConfig: cfg, customRng: recordingRng);

            traits.Set(new TraitProfile { HeroId = "teller", IsAlive = true, IsLord = true });
            traits.Set(new TraitProfile { HeroId = "neutral_contact", IsAlive = true, IsLord = true });
            traits.Set(new TraitProfile { HeroId = "hostile_contact", IsAlive = true, IsLord = true });

            channel.AddLink("teller", "neutral_contact", ChannelKind.SameParty, weight: 1.0, relation: 0);
            channel.AddLink("teller", "hostile_contact", ChannelKind.SameParty, weight: 1.0, relation: -60);

            var evt = new WorldEvent
            {
                EventId = "evt_hostile_vs_neutral",
                Origin = EventOrigin.Public,
                Day = 1.0,
                DramaWeight = 3,
                KnownBy = { new KnownByEntry { HeroId = "teller", Hop = 0, LearnedDay = 1.0 } }
            };

            engine.PropagateOnce(evt, 2.0, 10);

            Assert.Equal(2, recordingRng.RecordedProbabilities.Count);
            double pNeutral = recordingRng.RecordedProbabilities[0];
            double pHostile = recordingRng.RecordedProbabilities[1];

            Assert.Equal(0.625, pNeutral, precision: 4);
            Assert.Equal(0.1375, pHostile, precision: 4);
            Assert.True(pHostile < pNeutral * 0.3);
        }

        [Fact]
        public void Propagation_RelationDefaultsToNeutral_WhenChannelOmitsIt()
        {
            var cfg = new VividWorldConfig();
            cfg.Propagation.BaseTellChancePerContact = 1.0;
            var recordingRng = new RecordingRng();
            var (engine, channel, traits, _) = CreateEngine(customConfig: cfg, customRng: recordingRng);

            traits.Set(new TraitProfile { HeroId = "teller", IsAlive = true, IsLord = true });
            traits.Set(new TraitProfile { HeroId = "contact", IsAlive = true, IsLord = true });

            var link = new ChannelLink("contact", ChannelKind.SameSettlement, 1.0);
            Assert.Equal(0, link.Relation);

            channel.AddLink("teller", link.HeroId, link.Kind, link.Weight);

            var evt = new WorldEvent
            {
                EventId = "evt_default_neutral",
                Origin = EventOrigin.Public,
                Day = 1.0,
                DramaWeight = 3,
                KnownBy = { new KnownByEntry { HeroId = "teller", Hop = 0, LearnedDay = 1.0 } }
            };

            engine.PropagateOnce(evt, 2.0, 10);

            Assert.Single(recordingRng.RecordedProbabilities);
            Assert.Equal(0.80, recordingRng.RecordedProbabilities[0], precision: 4);
        }

        [Fact]
        public void Dormancy_NeverGainedKnower_GoesStaleFromEventDay()
        {
            var (engine, _, traits, cfg) = CreateEngine();
            cfg.Scheduling.RumorLifetimeDays = 120.0;
            cfg.Scheduling.StaleDays = 30.0;

            traits.Set(new TraitProfile { HeroId = "hero_1", IsAlive = true, IsLord = true });

            var evt = new WorldEvent
            {
                EventId = "evt_never_gained_knower",
                Origin = EventOrigin.Public,
                Day = 10.0,
                DramaWeight = 3,
                KnownBy = new List<KnownByEntry>
                {
                    new KnownByEntry { HeroId = "hero_1", Hop = 0, LearnedDay = 10.0 }
                },
                State = new RumorState { LastNewKnowerDay = -1.0 }
            };

            // day - evt.Day = 45.0 - 10.0 = 35.0 > StaleDays (30.0) -> ShouldGoDormant is true
            Assert.True(engine.ShouldGoDormant(evt, 45.0));
        }

        [Fact]
        public void Dormancy_NeverGainedKnower_StaysActiveWithinStaleDays()
        {
            var (engine, _, traits, cfg) = CreateEngine();
            cfg.Scheduling.RumorLifetimeDays = 120.0;
            cfg.Scheduling.StaleDays = 30.0;

            traits.Set(new TraitProfile { HeroId = "hero_1", IsAlive = true, IsLord = true });

            var evt = new WorldEvent
            {
                EventId = "evt_never_gained_knower_fresh",
                Origin = EventOrigin.Public,
                Day = 10.0,
                DramaWeight = 3,
                KnownBy = new List<KnownByEntry>
                {
                    new KnownByEntry { HeroId = "hero_1", Hop = 0, LearnedDay = 10.0 }
                },
                State = new RumorState { LastNewKnowerDay = -1.0 }
            };

            // day - evt.Day = 25.0 - 10.0 = 15.0 <= StaleDays (30.0) -> ShouldGoDormant is false
            Assert.False(engine.ShouldGoDormant(evt, 25.0));
        }

        [Fact]
        public void PropagateOnce_SetsLastPropagatedDay_EvenWhenNoEligibleTeller()
        {
            var (engine, channel, traits, _) = CreateEngine();

            // hero_peasant 不合格（非領主非流浪者）
            traits.Set(new TraitProfile { HeroId = "hero_peasant", IsAlive = true, IsLord = false, IsWanderer = false });

            var evt = new WorldEvent
            {
                EventId = "evt_no_eligible_teller",
                Origin = EventOrigin.Public,
                Day = 1.0,
                DramaWeight = 3,
                KnownBy = new List<KnownByEntry>
                {
                    new KnownByEntry { HeroId = "hero_peasant", Hop = 0, LearnedDay = 1.0 }
                },
                State = new RumorState { LastPropagatedDay = -1.0 }
            };

            double testDay = 10.0;
            var outcome = engine.PropagateOnce(evt, testDay, 14);

            Assert.False(outcome.DidAnything);
            Assert.Empty(outcome.NewKnowers);
            Assert.Equal(testDay, evt.State.LastPropagatedDay);
            Assert.Equal(0, channel.QueryCount);
        }

        private sealed class RecordingRng : IDeterministicRng
        {
            public List<double> RecordedProbabilities { get; } = new();

            public double NextDouble(long seed) => 0.5;
            public bool Chance(double p, long seed)
            {
                RecordedProbabilities.Add(p);
                return false;
            }
            public int Pick(int count, long seed) => 0;
            public int PickWeighted(IReadOnlyList<double> weights, long seed) => 0;
        }
    }
}
