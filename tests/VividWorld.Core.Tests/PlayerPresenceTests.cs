using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using VividWorld.Core.Catalog;
using VividWorld.Core.Config;
using VividWorld.Core.Grudges;
using VividWorld.Core.Persistence;
using VividWorld.Core.Rumors;
using VividWorld.Core.Situations;
using VividWorld.Core.Tests.Fakes;
using VividWorld.Core.Util;
using Xunit;

namespace VividWorld.Core.Tests
{
    public class PlayerPresenceTests
    {
        private const string Player = "main_hero";

        // ── 測試用的世界 ────────────────────────────────────────────────────────────

        private static string FindRepoFile(params string[] parts)
        {
            string? current = AppContext.BaseDirectory;
            while (!string.IsNullOrEmpty(current))
            {
                if (File.Exists(Path.Combine(current, "VividWorld.sln")))
                {
                    string path = Path.Combine(new[] { current }.Concat(parts).ToArray());
                    if (File.Exists(path)) return path;
                }
                var parent = Directory.GetParent(current);
                if (parent == null) break;
                current = parent.FullName;
            }
            throw new FileNotFoundException($"Could not find '{string.Join("/", parts)}' relative to {AppContext.BaseDirectory}");
        }

        private static SituationCatalog RepoCatalog()
        {
            string json = File.ReadAllText(FindRepoFile("module", "ModuleData", "vividworld_situations.json"));
            return SituationCatalogLoader.Load(json);
        }

        private static SituationRoleFacts Facts(string id, string clan, string? kingdom, int tier, bool leader = true,
            string settlement = "town_1", string kind = "town", bool isPlayer = false)
        {
            return new SituationRoleFacts
            {
                HeroId = id,
                ClanId = clan,
                KingdomId = kingdom,
                ClanTier = tier,
                IsClanLeader = leader,
                SettlementId = settlement,
                SettlementKind = kind,
                IsPlayer = isPlayer,
                IsLord = true
            };
        }

        /// <summary>照 NPC 情境原本的求值器評估條件；推導角色用固定的城主與君主，推導成玩家時不綁（原本的規則就是這樣）。</summary>
        private sealed class FakeEvaluator : IPlayerPresenceEvaluator
        {
            public readonly Dictionary<string, SituationRoleFacts> Facts = new(StringComparer.Ordinal);
            public string? Host;
            public string? Ruler;
            public bool DeriveToPlayerAnyway;
            public readonly FakeHeroTraitLookup Traits = new FakeHeroTraitLookup();
            public readonly List<(string SituationId, List<string> ConditionTypes)> Seen = new();

            public PlayerPresenceEvaluation Evaluate(SituationTemplate template, IReadOnlyList<SituationConditionDef> conditions,
                IReadOnlyDictionary<string, string> nonDerivedRoleHeroIds, bool forced)
            {
                Seen.Add((template.Id, conditions.Select(c => c.Type).ToList()));
                var facts = new Dictionary<string, SituationRoleFacts>(StringComparer.OrdinalIgnoreCase);
                var bound = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
                var unbound = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (var kv in nonDerivedRoleHeroIds)
                {
                    bound[kv.Key] = kv.Value;
                    facts[kv.Key] = Facts[kv.Value];
                }
                foreach (var r in template.Roles.Where(r => r.Value.IsDerived))
                {
                    string? who = r.Value.Derived!.StartsWith("rulerOf", StringComparison.Ordinal) ? Ruler : Host;
                    bool isPlayer = who == Player;
                    if (who != null && (!isPlayer || DeriveToPlayerAnyway) && !bound.Values.Contains(who))
                    {
                        bound[r.Key] = who;
                        facts[r.Key] = Facts[who];
                    }
                    else
                    {
                        bound[r.Key] = null;
                        unbound[r.Key] = who == null ? "nobody" : isPlayer ? "is the player" : "already bound";
                        facts[r.Key] = new SituationRoleFacts();
                    }
                }
                var context = new SituationWorldContext { Traits = Traits, Rng = new SplitMix64Rng(), PlayerHeroId = Player };
                var results = SituationConditionEvaluator.EvaluateAll(conditions, facts, bound, unbound, template.Id, 10.0,
                    null, nonDerivedRoleHeroIds.Values.ToList(), context, forced);
                return new PlayerPresenceEvaluation { ConditionResults = results, BoundHeroIds = bound, State = facts };
            }
        }

        /// <summary>一座城：玩家（家族等級 3，屬於 k1）＋兩位 k1 的領主＋一位 k2 的領主。</summary>
        private static FakeEvaluator Town(string? playerKingdom = "k1", int playerTier = 3)
        {
            var ev = new FakeEvaluator();
            ev.Facts[Player] = Facts(Player, "clan_player", playerKingdom, playerTier, isPlayer: true);
            ev.Facts["lord_a"] = Facts("lord_a", "clan_a", "k1", 5);
            ev.Facts["lord_b"] = Facts("lord_b", "clan_b", "k1", 1);
            ev.Facts["lord_c"] = Facts("lord_c", "clan_c", "k2", 2);
            foreach (var id in ev.Facts.Keys)
            {
                ev.Traits.Set(new TraitProfile { HeroId = id, IsAlive = true, IsLord = true, Honor = -1 });
            }
            return ev;
        }

        private static PlayerPresenceRollInput Input(double today = 10.0, double? last = null, string kind = "town",
            PlayerPresenceForce? force = null, PlayerPresenceMoment moment = PlayerPresenceMoment.Entry, params string[] lords)
        {
            return new PlayerPresenceRollInput
            {
                PlayerHeroId = Player,
                Place = new PlayerPresencePlace { SettlementId = "town_1", SettlementKind = kind },
                AvailableHeroIds = lords.Length > 0 ? lords : new[] { "lord_a", "lord_b", "lord_c" },
                Today = today,
                LastOccurrenceDay = last,
                CampaignSeed = 4242,
                Moment = moment,
                Force = force
            };
        }

        private static VividWorldConfig Config(double chance = 1.0, int cooldown = 7, bool enabled = true)
        {
            var cfg = new VividWorldConfig();
            cfg.Situations.PlayerPresence.Chance = chance;
            cfg.Situations.PlayerPresence.CooldownDays = cooldown;
            cfg.Situations.PlayerPresence.Enabled = enabled;
            return cfg;
        }

        private static PlayerPresenceRollResult Roll(PlayerPresenceRollInput input, VividWorldConfig cfg, FakeEvaluator ev, SituationCatalog? catalog = null)
        {
            return PlayerPresenceRoller.Roll(input, TownPresenceTrigger.Instance, cfg, (catalog ?? RepoCatalog()).Situations, ev, new SplitMix64Rng());
        }

        private static PlayerPresenceCandidateReport Candidate(PlayerPresenceRollResult r, string situationId, PlayerPresenceRole role)
        {
            return r.Candidates.Single(c => c.SituationId == situationId && c.Role == role);
        }

        // ── 不擲的原因 ──────────────────────────────────────────────────────────────

        [Fact]
        public void Roll_Disabled_DoesNotRollOrEvaluate()
        {
            var ev = Town();
            var r = Roll(Input(), Config(enabled: false), ev);
            Assert.Equal(PlayerPresenceOutcome.Disabled, r.Outcome);
            Assert.Null(r.Roll);
            Assert.Empty(ev.Seen);
            Assert.Contains("disabled", PlayerPresenceLogFormatter.FormatRoll(r));
        }

        [Theory]
        [InlineData("village")]
        [InlineData("other")]
        public void Roll_VillageOrField_NotAllowed(string kind)
        {
            var r = Roll(Input(kind: kind), Config(), Town());
            Assert.Equal(PlayerPresenceOutcome.PlaceNotAllowed, r.Outcome);
            Assert.Contains("need town/castle", r.Reason);
        }

        [Fact]
        public void Roll_NoSettlement_NotAllowed()
        {
            var input = Input();
            input.Place = new PlayerPresencePlace();
            var r = Roll(input, Config(), Town());
            Assert.Equal(PlayerPresenceOutcome.PlaceNotAllowed, r.Outcome);
            Assert.Equal("not in a settlement", r.Reason);
        }

        [Fact]
        public void Roll_Castle_IsAllowed()
        {
            var r = Roll(Input(kind: "castle"), Config(), Town());
            Assert.NotEqual(PlayerPresenceOutcome.PlaceNotAllowed, r.Outcome);
        }

        [Fact]
        public void Roll_FewerThanTwoLords_NotRolled_PlayerNeverCountsAsALord()
        {
            var r = Roll(Input(lords: new[] { "lord_a", Player }), Config(), Town());
            Assert.Equal(PlayerPresenceOutcome.TooFewHeroes, r.Outcome);
            Assert.Equal(1, r.AvailableHeroCount);
            Assert.Contains("1 available lord(s) or wanderer(s), need 2", PlayerPresenceLogFormatter.FormatRoll(r));
        }

        // ── 冷卻 ────────────────────────────────────────────────────────────────────

        [Fact]
        public void Cooldown_NoRecord_MeansNoCooldown()
        {
            var r = Roll(Input(last: null), Config(), Town());
            Assert.NotEqual(PlayerPresenceOutcome.Cooldown, r.Outcome);
            Assert.Null(r.CooldownRemainingDays);
            Assert.Contains("cooldown none", PlayerPresenceLogFormatter.FormatRoll(r));
        }

        [Fact]
        public void Cooldown_JustBeforeBoundary_Blocks_AtBoundary_Rolls()
        {
            var before = Roll(Input(today: 16.9, last: 10.0), Config(cooldown: 7), Town());
            Assert.Equal(PlayerPresenceOutcome.Cooldown, before.Outcome);
            Assert.Equal(0.1, before.CooldownRemainingDays!.Value, 6);
            Assert.Null(before.Roll);
            Assert.Contains("cooldown 0.1d left", PlayerPresenceLogFormatter.FormatRoll(before));

            var at = Roll(Input(today: 17.0, last: 10.0), Config(cooldown: 7), Town());
            Assert.NotEqual(PlayerPresenceOutcome.Cooldown, at.Outcome);
            Assert.Equal(0.0, at.CooldownRemainingDays!.Value, 6);
            Assert.Contains("cooldown over", PlayerPresenceLogFormatter.FormatRoll(at));
        }

        [Fact]
        public void Cooldown_Zero_NeverBlocks()
        {
            var r = Roll(Input(today: 10.0, last: 10.0), Config(cooldown: 0), Town());
            Assert.NotEqual(PlayerPresenceOutcome.Cooldown, r.Outcome);
        }

        // ── 機率 ────────────────────────────────────────────────────────────────────

        [Fact]
        public void Chance_Zero_AlwaysMisses_One_NeverMisses()
        {
            for (int day = 0; day < 30; day++)
            {
                var miss = Roll(Input(today: day), Config(chance: 0.0), Town());
                Assert.Equal(PlayerPresenceOutcome.ChanceMissed, miss.Outcome);
                Assert.NotNull(miss.Roll);
                Assert.Contains("-> missed", PlayerPresenceLogFormatter.FormatRoll(miss));

                var hit = Roll(Input(today: day), Config(chance: 1.0), Town());
                Assert.NotEqual(PlayerPresenceOutcome.ChanceMissed, hit.Outcome);
            }
        }

        [Fact]
        public void Chance_HitRateFollowsSetting_AndIsDeterministic()
        {
            var cfg = Config(chance: 0.25);
            int hits = 0;
            const int days = 2000;
            for (int day = 0; day < days; day++)
            {
                var r1 = Roll(Input(today: day), cfg, Town());
                var r2 = Roll(Input(today: day), cfg, Town());
                Assert.Equal(r1.Roll, r2.Roll);
                Assert.Equal(r1.Outcome, r2.Outcome);
                if (r1.Outcome != PlayerPresenceOutcome.ChanceMissed) hits++;
            }
            double rate = hits / (double)days;
            Assert.InRange(rate, 0.21, 0.29);
        }

        [Fact]
        public void Chance_EntryAndDailyOnTheSameDay_AreSeparateRolls()
        {
            int differ = 0;
            for (int day = 0; day < 50; day++)
            {
                var entry = Roll(Input(today: day, moment: PlayerPresenceMoment.Entry), Config(chance: 0.5), Town());
                var daily = Roll(Input(today: day, moment: PlayerPresenceMoment.Daily), Config(chance: 0.5), Town());
                if (entry.Roll != daily.Roll) differ++;
            }
            Assert.Equal(50, differ);
        }

        [Fact]
        public void Force_IgnoresCooldownAndChance_AndNarrowsTheCandidates()
        {
            var force = new PlayerPresenceForce { SituationId = "tavern_words", Role = PlayerPresenceRole.Decider };
            var r = Roll(Input(today: 11.0, last: 10.0, force: force), Config(chance: 0.0, cooldown: 7), Town());
            Assert.Equal(PlayerPresenceOutcome.Happened, r.Outcome);
            Assert.Null(r.Roll);
            Assert.Single(r.Candidates);
            Assert.Equal("tavern_words", r.Pick!.Template.Id);
            Assert.Equal(PlayerPresenceRole.Decider, r.Pick.Role);
            Assert.Contains("forced", PlayerPresenceLogFormatter.FormatRoll(r));
        }

        [Fact]
        public void Force_StillHonorsTheOffSwitch()
        {
            var r = Roll(Input(force: new PlayerPresenceForce()), Config(enabled: false), Town());
            Assert.Equal(PlayerPresenceOutcome.Disabled, r.Outcome);
        }

        // ── 三種身分的挑選 ──────────────────────────────────────────────────────────

        [Fact]
        public void Candidates_OnlySituationsWithAPlayerBlock_InFixedRoleOrder()
        {
            var r = Roll(Input(), Config(), Town());
            var keys = r.Candidates.Select(c => $"{c.SituationId}/{c.Role}").ToList();
            Assert.Equal(new[]
            {
                "seat_dispute/Bystander", "seat_dispute/Decider",
                "advice_sought/Bystander", "advice_sought/Decider",
                "tavern_words/Bystander", "tavern_words/Decider",
                "victory_credit/Bystander",
                "market_brawl/Bystander", "market_brawl/Decider",
                "hunt_wager/Bystander",
                "spoke_against_ruler/Listener"
            }, keys);
        }

        [Fact]
        public void Decider_BindsThePlayerToTheDeciderRole_AndALordToTheOtherRole()
        {
            var force = new PlayerPresenceForce { SituationId = "tavern_words", Role = PlayerPresenceRole.Decider };
            var r = Roll(Input(force: force), Config(), Town());
            var pick = r.Pick!;
            Assert.Equal("speaker", pick.PlayerRoleName);
            Assert.Equal(Player, pick.Assignment.NonDerivedRoleHeroIds["speaker"]);
            Assert.Contains(pick.Assignment.NonDerivedRoleHeroIds["listener"], new[] { "lord_a", "lord_b", "lord_c" });
            Assert.Equal(3, Candidate(r, "tavern_words", PlayerPresenceRole.Decider).AssignmentsTried);
        }

        [Fact]
        public void Listener_BindsThePlayerToTheListenerRole()
        {
            var ev = Town();
            ev.Ruler = "lord_c";
            ev.Facts["lord_c"] = Facts("lord_c", "clan_c", "k1", 6);
            var force = new PlayerPresenceForce { SituationId = "spoke_against_ruler", Role = PlayerPresenceRole.Listener };
            var r = Roll(Input(force: force), Config(), ev);
            Assert.Equal(PlayerPresenceOutcome.Happened, r.Outcome);
            Assert.Equal("listener", r.Pick!.PlayerRoleName);
            Assert.Equal(Player, r.Pick.Assignment.NonDerivedRoleHeroIds["listener"]);
            Assert.NotEqual(Player, r.Pick.Assignment.NonDerivedRoleHeroIds["speaker"]);
            Assert.Equal("lord_c", r.Pick.Assignment.Evaluation.BoundHeroIds["ruler"]);
        }

        [Fact]
        public void Bystander_BindsTwoLords_ThePlayerHoldsNoRole()
        {
            var force = new PlayerPresenceForce { SituationId = "hunt_wager", Role = PlayerPresenceRole.Bystander };
            var r = Roll(Input(force: force), Config(), Town());
            var pick = r.Pick!;
            Assert.Null(pick.PlayerRoleName);
            Assert.DoesNotContain(Player, pick.Assignment.NonDerivedRoleHeroIds.Values);
            Assert.Equal(6, Candidate(r, "hunt_wager", PlayerPresenceRole.Bystander).AssignmentsTried);
        }

        [Fact]
        public void Bystander_ManyLords_AssignmentsCappedLikeTheDailyScan()
        {
            var ev = Town();
            var lords = new List<string>();
            for (int i = 0; i < 8; i++)
            {
                string id = $"lord_x{i}";
                ev.Facts[id] = Facts(id, $"clan_x{i}", "k1", 2);
                lords.Add(id);
            }
            var cfg = Config();
            cfg.Situations.Scan.MaxPairsPerSettlement = 10;
            var force = new PlayerPresenceForce { SituationId = "hunt_wager", Role = PlayerPresenceRole.Bystander };
            var r = Roll(Input(force: force, lords: lords.ToArray()), cfg, ev);
            Assert.Equal(10, Candidate(r, "hunt_wager", PlayerPresenceRole.Bystander).AssignmentsTried);
        }

        [Fact]
        public void Pick_IsWeightedBySituationWeight_AndProbabilityIsLogged()
        {
            var r = Roll(Input(), Config(), Town());
            Assert.Equal(PlayerPresenceOutcome.Happened, r.Outcome);
            var passing = r.Candidates.Where(c => c.Passed).ToList();
            double total = passing.Sum(c => c.Weight);
            var picked = passing.First(c => c.SituationId == r.Pick!.Template.Id && c.Role == r.Pick.Role);
            Assert.Equal(picked.Weight / total, r.Pick!.Probability, 6);
            Assert.Contains(PlayerPresenceLogFormatter.FormatCandidates(r), l => l.StartsWith("  picked ", StringComparison.Ordinal));
        }

        // ── 條件照原本的寫法；只有手下鬥毆牽涉玩家時跳過同一個王國 ───────────────────────

        [Fact]
        public void Brawl_PlayerAsPatronWithoutKingdom_SkipsSameKingdom()
        {
            var ev = Town(playerKingdom: null);
            var force = new PlayerPresenceForce { SituationId = "market_brawl", Role = PlayerPresenceRole.Decider };
            var r = Roll(Input(force: force), Config(), ev);
            var c = Candidate(r, "market_brawl", PlayerPresenceRole.Decider);
            Assert.True(c.Passed);
            Assert.Equal(3, c.AssignmentsPassed);
            Assert.Equal(new[] { "sameKingdom" }, c.SkippedConditions);
            Assert.All(ev.Seen.Where(s => s.SituationId == "market_brawl"), s => Assert.DoesNotContain("sameKingdom", s.ConditionTypes));
            Assert.Contains("not checked with the player: sameKingdom", string.Join("\n", PlayerPresenceLogFormatter.FormatCandidates(r)));
        }

        [Fact]
        public void Brawl_BetweenTwoLords_StillNeedsTheSameKingdom()
        {
            var ev = Town();
            var force = new PlayerPresenceForce { SituationId = "market_brawl", Role = PlayerPresenceRole.Bystander };
            var r = Roll(Input(force: force), Config(), ev);
            var c = Candidate(r, "market_brawl", PlayerPresenceRole.Bystander);
            Assert.Empty(c.SkippedConditions);
            Assert.All(ev.Seen, s => Assert.Contains("sameKingdom", s.ConditionTypes));
            // a、b 同屬 k1，c 屬 k2：六組有序配對裡只有 a↔b 兩組過
            Assert.Equal(6, c.AssignmentsTried);
            Assert.Equal(2, c.AssignmentsPassed);
            Assert.All(c.PassingAssignments, a => Assert.DoesNotContain("lord_c", a.NonDerivedRoleHeroIds.Values));
        }

        [Fact]
        public void OtherSituations_KeepSameKingdomForThePlayer()
        {
            var ev = Town(playerKingdom: null);
            var force = new PlayerPresenceForce { SituationId = "seat_dispute", Role = PlayerPresenceRole.Decider };
            var r = Roll(Input(force: force), Config(), ev);
            var c = Candidate(r, "seat_dispute", PlayerPresenceRole.Decider);
            Assert.False(c.Passed);
            Assert.Empty(c.SkippedConditions);
            Assert.True(c.FailedByLabel.ContainsKey("sameKingdom"));
            Assert.Equal(PlayerPresenceOutcome.NoCandidate, r.Outcome);
        }

        [Fact]
        public void ExcludedCandidate_SaysWhichConditionAndHowManyAssignments()
        {
            // 玩家家族等級 3：只有 lord_b（等級 1）比他低，請教只成立一組；座次之爭要對方比玩家高，lord_a（5）
            var ev = Town(playerTier: 0);
            var force = new PlayerPresenceForce { SituationId = "advice_sought", Role = PlayerPresenceRole.Decider };
            var r = Roll(Input(force: force), Config(), ev);
            var c = Candidate(r, "advice_sought", PlayerPresenceRole.Decider);
            Assert.False(c.Passed);
            Assert.Equal(3, c.FailedByLabel["clanTierCompare"]);
            Assert.Equal(PlayerPresenceOutcome.NoCandidate, r.Outcome);
            Assert.Equal("no candidate passed", r.Reason);

            string lines = string.Join("\n", PlayerPresenceLogFormatter.FormatCandidates(r));
            Assert.Contains("excluded advice_sought as decider (veteran): 0 of 3 assignment(s) passed", lines);
            Assert.Contains("clanTierCompare x3 (clanTierCompare failed (veteran 0 > student", lines);
            Assert.Contains("no situation: no candidate passed", PlayerPresenceLogFormatter.FormatRoll(r));
        }

        [Fact]
        public void DerivedRole_NeverBecomesThePlayer()
        {
            // 玩家是這座城的城主：原本的推導規則不會把他推成 host，拂袖離席因此沒有前提。
            // lord_d 不是族長、家族等級比 lord_a 高，兩人才湊得成座次之爭
            var ev = Town();
            ev.Facts["lord_d"] = Facts("lord_d", "clan_d", "k1", 6, leader: false);
            ev.Host = Player;
            var force = new PlayerPresenceForce { SituationId = "seat_dispute", Role = PlayerPresenceRole.Bystander };
            var lords = new[] { "lord_a", "lord_d" };
            var r = Roll(Input(force: force, lords: lords), Config(), ev);
            Assert.Equal(PlayerPresenceOutcome.Happened, r.Outcome);
            Assert.Null(r.Pick!.Assignment.Evaluation.BoundHeroIds["host"]);

            // 求值端萬一推成玩家，那組指派不准成立
            ev = Town();
            ev.Facts["lord_d"] = Facts("lord_d", "clan_d", "k1", 6, leader: false);
            ev.Host = Player;
            ev.DeriveToPlayerAnyway = true;
            r = Roll(Input(force: force, lords: lords), Config(), ev);
            var c = Candidate(r, "seat_dispute", PlayerPresenceRole.Bystander);
            Assert.False(c.Passed);
            Assert.True(c.FailedByLabel.ContainsKey("playerBound(host)"));
            Assert.Contains("role 'host' would be the player", string.Join("\n", PlayerPresenceLogFormatter.FormatCandidates(r)));
        }

        [Fact]
        public void DisabledSituation_IsNotOffered()
        {
            var cfg = Config();
            cfg.FalseRumors.Enabled = false;
            var r = Roll(Input(), cfg, Town());
            Assert.DoesNotContain(r.Candidates, c => c.SituationId == "spoke_against_ruler");
        }

        [Fact]
        public void PreviewRoll_WithForcedWithoutTarget_PreservesCandidatesAndLeavesCooldownIntact()
        {
            // 試算：Force 不指定 SituationId 與 Role，無視冷卻與機率求值所有候選；
            // 呼叫端（Module）不更新 LastOccurrenceDay 或 Cooldown，且候選中只有 Passed && Weight > 0 的會被列出
            var ev = Town();
            var input = Input(today: 20.0, last: 5.0, force: new PlayerPresenceForce());
            var r = Roll(input, Config(cooldown: 7), ev);

            Assert.True(r.Forced);
            Assert.NotEmpty(r.Candidates);

            var passing = r.Candidates.Where(c => c.Passed && c.Weight > 0.0).ToList();
            Assert.NotEmpty(passing);

            // Roll 不改動 input 的 LastOccurrenceDay
            Assert.Equal(5.0, input.LastOccurrenceDay);
        }

        // ── 恩怨過濾 ────────────────────────────────────────────────────────────────

        private static SituationBranchDef Branch(string situationId, string branchId)
        {
            return RepoCatalog().ById(situationId)!.Branches.Single(b => b.Id == branchId);
        }

        [Fact]
        public void GrudgeFilter_PlayerDecider_DropsFromPlayer_KeepsToPlayer()
        {
            var bound = new Dictionary<string, string?> { ["slighted"] = Player, ["favored"] = "lord_a", ["host"] = "lord_h" };

            var demand = PlayerGrudgeFilter.Filter(Branch("seat_dispute", "demand_seat").Grudges, bound, Player);
            Assert.Equal(new[] { "favored->slighted" }, demand.Kept.Select(g => $"{g.From}->{g.To}"));
            Assert.Equal(new[] { "slighted->favored" }, demand.Dropped.Select(g => $"{g.From}->{g.To}"));

            // 從玩家出發、會升級成家族恩怨的也不記；城主對玩家的照記
            var walk = PlayerGrudgeFilter.Filter(Branch("seat_dispute", "walk_out").Grudges, bound, Player);
            Assert.Equal(new[] { "host->slighted" }, walk.Kept.Select(g => $"{g.From}->{g.To}"));
            Assert.Single(walk.Dropped);
            Assert.Equal("clan", walk.Dropped[0].Escalate);

            string line = PlayerPresenceLogFormatter.FormatGrudgeFilter("seat_dispute", "walk_out", walk);
            Assert.Contains("kept 1 (host->slighted -6)", line);
            Assert.Contains("from the player: 1 (slighted->favored -8 escalate:clan)", line);
        }

        [Fact]
        public void GrudgeFilter_ToPlayer_LedgerOnlyAndEscalateAreKept()
        {
            var advice = new Dictionary<string, string?> { ["student"] = "lord_b", ["veteran"] = Player };
            var brush = PlayerGrudgeFilter.Filter(Branch("advice_sought", "brush_off").Grudges, advice, Player);
            Assert.Single(brush.Kept);
            Assert.True(brush.Kept[0].LedgerOnly);
            Assert.Empty(brush.Dropped);

            var brawl = new Dictionary<string, string?> { ["aggrieved"] = "lord_a", ["patron"] = Player };
            var shield = PlayerGrudgeFilter.Filter(Branch("market_brawl", "shield_own").Grudges, brawl, Player);
            Assert.Single(shield.Kept);
            Assert.Equal("clan", shield.Kept[0].Escalate);

            var counter = PlayerGrudgeFilter.Filter(Branch("market_brawl", "counter_accuse").Grudges, brawl, Player);
            Assert.Equal(new[] { "aggrieved->patron" }, counter.Kept.Select(g => $"{g.From}->{g.To}"));
            Assert.Equal(new[] { "patron->aggrieved" }, counter.Dropped.Select(g => $"{g.From}->{g.To}"));
        }

        [Fact]
        public void GrudgeFilter_Bystander_KeepsEverything()
        {
            var bound = new Dictionary<string, string?> { ["slighted"] = "lord_a", ["favored"] = "lord_b", ["host"] = null };
            var all = Branch("seat_dispute", "walk_out").Grudges!;
            var r = PlayerGrudgeFilter.Filter(all, bound, Player);
            Assert.Equal(all.Count, r.Kept.Count);
            Assert.Empty(r.Dropped);
        }

        // ── 旁觀時不擲只產生祕密事件的做法 ──────────────────────────────────────────────

        private static EventCatalog RepoEventCatalog()
        {
            string json = File.ReadAllText(FindRepoFile("module", "ModuleData", "vividworld_situation_events.json"));
            return EventCatalogLoader.Load(json, new PersistenceConfig());
        }

        [Fact]
        public void Bystander_SecretOnlyBranches_AreExactlyTheThreeSecretOnes()
        {
            var events = RepoEventCatalog();
            var excluded = new List<string>();
            foreach (var t in RepoCatalog().Situations.Where(t => t.Player != null && t.Player.Bystander))
            {
                foreach (var b in t.Branches)
                {
                    if (PlayerBystanderBranches.SecretOnlyReason(b, events.ByType) != null) excluded.Add($"{t.Id}/{b.Id}");
                }
            }
            Assert.Equal(new[] { "seat_dispute/endure", "market_brawl/hush_it_up", "hunt_wager/secret_stake" }, excluded);
            Assert.Equal("bystander: only secret events (seat_dispute_endured)",
                PlayerBystanderBranches.SecretOnlyReason(Branch("seat_dispute", "endure"), events.ByType));
        }

        [Fact]
        public void Bystander_UnknownTemplateOrNoEvents_NotExcluded()
        {
            Assert.Null(PlayerBystanderBranches.SecretOnlyReason(Branch("seat_dispute", "endure"), _ => null));
            Assert.Null(PlayerBystanderBranches.SecretOnlyReason(new SituationBranchDef { Id = "x" }, RepoEventCatalog().ByType));
        }

        [Fact]
        public void BranchSelector_ExcludedBranchIsNeverPicked_WithoutTheHookNothingChanges()
        {
            var seat = RepoCatalog().ById("seat_dispute")!;
            var events = RepoEventCatalog();
            // 理性極高：不排除時「忍下」幾乎每次都中
            var traits = new TraitProfile { HeroId = "lord_a", Calculating = 2, Mercy = -2, IsAlive = true, IsLord = true };
            var bound = new Dictionary<string, string?> { ["slighted"] = "lord_a", ["favored"] = "lord_b", ["host"] = null };
            var unbound = new Dictionary<string, string> { ["host"] = "nobody" };
            var rng = new SplitMix64Rng();
            int endureWithout = 0;
            for (long seed = 1; seed <= 200; seed++)
            {
                var plain = SituationBranchSelector.Select(seat, new Dictionary<string, SituationRoleFacts>(), bound, unbound, traits, 0.1, rng, seed);
                var again = SituationBranchSelector.Select(seat, new Dictionary<string, SituationRoleFacts>(), bound, unbound, traits, 0.1, rng, seed, excludeBranch: null);
                Assert.Equal(plain.SelectedBranchId, again.SelectedBranchId);
                if (plain.SelectedBranchId == "endure") endureWithout++;

                var watched = SituationBranchSelector.Select(seat, new Dictionary<string, SituationRoleFacts>(), bound, unbound, traits, 0.1, rng, seed,
                    excludeBranch: b => PlayerBystanderBranches.SecretOnlyReason(b, events.ByType));
                Assert.NotEqual("endure", watched.SelectedBranchId);
                var endure = watched.Branches.Single(d => d.BranchId == "endure");
                Assert.True(endure.IsExcluded);
                Assert.StartsWith("bystander: only secret events", endure.ExcludedReason);
            }
            Assert.True(endureWithout > 0);
        }

        // ── 做法清單 ────────────────────────────────────────────────────────────────

        [Fact]
        public void Options_AllBranchesListed_PreconditionFailureNotOffered()
        {
            var seat = RepoCatalog().ById("seat_dispute")!;
            var facts = new Dictionary<string, SituationRoleFacts>();
            var noHost = PlayerChoiceOptions.List(seat, facts,
                new Dictionary<string, string?> { ["slighted"] = Player, ["favored"] = "lord_a", ["host"] = null },
                new Dictionary<string, string> { ["host"] = "owner clan leader main_hero is the player" });
            Assert.Equal(new[] { "demand_seat", "endure", "yield", "walk_out" }, noHost.Select(o => o.Branch.Id));
            Assert.Equal(new[] { true, true, true, false }, noHost.Select(o => o.Offered));
            Assert.Contains("is the player", noHost[3].ExcludedReason);

            var withHost = PlayerChoiceOptions.List(seat, facts,
                new Dictionary<string, string?> { ["slighted"] = Player, ["favored"] = "lord_a", ["host"] = "lord_h" }, null);
            Assert.All(withHost, o => Assert.True(o.Offered));

            string line = PlayerPresenceLogFormatter.FormatChoice("seat_dispute", noHost, "yield");
            Assert.Contains("offered demand_seat, endure, yield; not offered: walk_out (roleBound(host) failed", line);
            Assert.EndsWith("chose yield", line);
        }

        // ── 數值後果與代價方向 ──────────────────────────────────────────────────────

        private static List<string> Dirs(string situationId, string branchId, Dictionary<string, string?> bound, bool hasKingdom, bool sameKingdom)
        {
            var t = RepoCatalog().ById(situationId)!;
            var b = t.Branches.Single(x => x.Id == branchId);
            return PlayerChoiceDirections.Compose(t, b, bound, Player, hasKingdom, sameKingdom).Select(d => d.ToString()).ToList();
        }

        private static readonly Dictionary<string, string?> SeatBound = new() { ["slighted"] = Player, ["favored"] = "lord_a", ["host"] = "lord_h" };

        [Fact]
        public void Directions_SeatDispute_DemandSeat()
        {
            Assert.Equal(new[] { "favored(lord_a) displeased", "renown up", "influence up", "valor up" },
                Dirs("seat_dispute", "demand_seat", SeatBound, true, true));
        }

        [Fact]
        public void Directions_GrudgeFromThePlayer_IsNotShown_PlayerWithoutKingdom_NoInfluence()
        {
            Assert.Equal(new[] { "influence down", "calculating up" }, Dirs("seat_dispute", "endure", SeatBound, true, true));
            Assert.Equal(new[] { "calculating up" }, Dirs("seat_dispute", "endure", SeatBound, false, false));
            Assert.Equal(new[] { "host(lord_h) displeased", "renown up", "influence down", "calculating down" },
                Dirs("seat_dispute", "walk_out", SeatBound, true, true));
        }

        [Fact]
        public void Directions_LedgerOnlyGrudge_IsNotShown()
        {
            var bound = new Dictionary<string, string?> { ["student"] = "lord_b", ["veteran"] = Player };
            Assert.Equal(new[] { "generosity down" }, Dirs("advice_sought", "brush_off", bound, true, true));
            Assert.Equal(new[] { "student(lord_b) pleased", "renown up", "generosity up" }, Dirs("advice_sought", "teach_freely", bound, true, true));

            var tavern = new Dictionary<string, string?> { ["speaker"] = Player, ["listener"] = "lord_c" };
            Assert.Equal(new[] { "renown up" }, Dirs("tavern_words", "boast", tavern, true, true));
        }

        [Fact]
        public void Directions_Brawl_InfluenceOnlyWhenTheOtherSideIsInThePlayersKingdom()
        {
            var bound = new Dictionary<string, string?> { ["aggrieved"] = "lord_a", ["patron"] = Player };
            Assert.Equal(new[] { "aggrieved(lord_a) pleased", "influence up", "morale down", "honor up" },
                Dirs("market_brawl", "hand_over", bound, true, true));
            Assert.Equal(new[] { "aggrieved(lord_a) pleased", "morale down", "honor up" },
                Dirs("market_brawl", "hand_over", bound, true, false));
            Assert.Equal(new[] { "calculating up" }, Dirs("market_brawl", "hush_it_up", bound, true, true));

            var t = RepoCatalog().ById("market_brawl")!;
            var plan = PlayerEffectsResolver.Resolve(t, t.Branches.Single(b => b.Id == "counter_accuse"), true, false);
            Assert.Equal(0.0, plan.Influence);
            Assert.Equal("other party is not in the player's kingdom", plan.InfluenceSkippedReason);
            Assert.Equal(5.0, plan.Morale);
            Assert.Equal(new[] { new KeyValuePair<string, int>("honor", -200) }, plan.Traits);

            // 其他情境沒有這條限制：座次之爭不看對方是不是同一王國
            var seat = RepoCatalog().ById("seat_dispute")!;
            Assert.Equal(10.0, PlayerEffectsResolver.Resolve(seat, seat.Branches[0], true, false).Influence);
            Assert.Equal("player has no kingdom", PlayerEffectsResolver.Resolve(seat, seat.Branches[0], false, false).InfluenceSkippedReason);
        }

        [Fact]
        public void Directions_LogLine()
        {
            var t = RepoCatalog().ById("seat_dispute")!;
            var d = PlayerChoiceDirections.Compose(t, t.Branches[0], SeatBound, Player, true, true);
            Assert.Equal("  directions for demand_seat: favored(lord_a) displeased, renown up, influence up, valor up",
                PlayerPresenceLogFormatter.FormatDirections("demand_seat", d));
        }

        // ── 資料檔：數字逐格等於規格的四張表、身分等於開放表 ─────────────────────────────

        [Theory]
        //           情境              做法              聲望  影響力 士氣 個性
        [InlineData("seat_dispute",  "demand_seat",    3,  10,  0, "valor:100")]
        [InlineData("seat_dispute",  "endure",         0,  -5,  0, "calculating:100")]
        [InlineData("seat_dispute",  "yield",          3, -10,  0, "generosity:100")]
        [InlineData("seat_dispute",  "walk_out",       3, -15,  0, "calculating:-100")]
        [InlineData("advice_sought", "teach_freely",   3,   0,  0, "generosity:100")]
        [InlineData("advice_sought", "brush_off",      0,   0,  0, "generosity:-50")]
        [InlineData("advice_sought", "mock",           0,   0,  0, "mercy:-100")]
        [InlineData("tavern_words",  "good_word",      0,   0,  0, "generosity:50")]
        [InlineData("tavern_words",  "boast",          3,   0,  0, "")]
        [InlineData("tavern_words",  "sour_words",     0,   0,  0, "mercy:-50")]
        [InlineData("market_brawl",  "hand_over",      0,   5, -5, "honor:100")]
        [InlineData("market_brawl",  "shield_own",     0,  -5,  5, "generosity:100")]
        [InlineData("market_brawl",  "counter_accuse", 0, -10,  5, "honor:-200")]
        [InlineData("market_brawl",  "hush_it_up",     0,   0,  0, "calculating:100")]
        public void DataFile_PlayerEffects_MatchTheSpecTables(string situationId, string branchId, double renown, double influence, double morale, string traits)
        {
            var fx = Branch(situationId, branchId).PlayerEffects;
            Assert.NotNull(fx);
            Assert.Equal(renown, fx!.Renown);
            Assert.Equal(influence, fx.Influence);
            Assert.Equal(morale, fx.Morale);
            var expected = traits.Length == 0
                ? new Dictionary<string, int>()
                : traits.Split(',').Select(p => p.Split(':')).ToDictionary(p => p[0], p => int.Parse(p[1]));
            Assert.Equal(expected.OrderBy(k => k.Key), fx.Traits.OrderBy(k => k.Key));
        }

        [Theory]
        //           情境                    對玩家的好感（照規格表，「暗中」＝只記帳本）
        [InlineData("seat_dispute",  "slighted", "demand_seat",    "favored:-8")]
        [InlineData("seat_dispute",  "slighted", "endure",         "")]
        [InlineData("seat_dispute",  "slighted", "yield",          "favored:5")]
        [InlineData("seat_dispute",  "slighted", "walk_out",       "host:-6")]
        [InlineData("advice_sought", "veteran",  "teach_freely",   "student:7")]
        [InlineData("advice_sought", "veteran",  "brush_off",      "student:-3:ledger")]
        [InlineData("advice_sought", "veteran",  "mock",           "student:-8")]
        [InlineData("tavern_words",  "speaker",  "good_word",      "listener:5")]
        [InlineData("tavern_words",  "speaker",  "boast",          "listener:-2:ledger")]
        [InlineData("tavern_words",  "speaker",  "sour_words",     "listener:-6")]
        [InlineData("market_brawl",  "patron",   "hand_over",      "aggrieved:6")]
        [InlineData("market_brawl",  "patron",   "shield_own",     "aggrieved:-7:clan")]
        [InlineData("market_brawl",  "patron",   "counter_accuse", "aggrieved:-9:clan")]
        [InlineData("market_brawl",  "patron",   "hush_it_up",     "")]
        public void DataFile_GrudgesTowardThePlayer_MatchTheSpecTables(string situationId, string playerRole, string branchId, string expected)
        {
            var toPlayer = (Branch(situationId, branchId).Grudges ?? new List<SituationGrudgeDef>())
                .Where(g => g.To == playerRole)
                .Select(g => $"{g.From}:{g.Amount}" + (g.LedgerOnly ? ":ledger" : "") + (g.Escalate != null ? $":{g.Escalate}" : ""));
            Assert.Equal(expected, string.Join(",", toPlayer));
        }

        [Fact]
        public void DataFile_PlayerBlocks_MatchTheOpenRolesTable()
        {
            var catalog = RepoCatalog();
            Assert.DoesNotContain(catalog.Issues, i => i.Code == SituationIssueCode.InvalidPlayerBlock || i.Code == SituationIssueCode.InvalidPlayerEffects);
            Assert.DoesNotContain(catalog.Issues, i => i.Code == SituationIssueCode.UnknownProperty && i.Field.StartsWith("player", StringComparison.Ordinal));
            Assert.DoesNotContain(catalog.Issues, i => i.IsError);

            var expected = new Dictionary<string, (bool Bystander, string? Decider, string? Listener)>
            {
                ["seat_dispute"] = (true, "slighted", null),
                ["advice_sought"] = (true, "veteran", null),
                ["tavern_words"] = (true, "speaker", null),
                ["market_brawl"] = (true, "patron", null),
                ["victory_credit"] = (true, null, null),
                ["hunt_wager"] = (true, null, null),
                ["spoke_against_ruler"] = (false, null, "listener"),
                ["wanderer_battle_credit"] = (false, null, null),
                ["wanderer_battle_rescue"] = (true, null, null),
                ["wanderer_party_role"] = (false, null, null),
                ["wanderer_campfire_jab"] = (false, null, null),
                ["wanderer_teaching"] = (false, null, null)
            };

            foreach (var s in catalog.Situations)
            {
                if (!expected.TryGetValue(s.Id, out var e))
                {
                    Assert.True(s.Player == null, $"{s.Id} must not be open to the player");
                    Assert.All(s.Branches, b => Assert.Null(b.PlayerEffects));
                    continue;
                }
                Assert.NotNull(s.Player);
                Assert.Equal(e.Bystander, s.Player!.Bystander);
                Assert.Equal(e.Decider, s.Player.DeciderRole);
                Assert.Equal(e.Listener, s.Player.ListenerRole);
                bool isBrawl = s.Id == "market_brawl";
                Assert.Equal(isBrawl ? new[] { "sameKingdom" } : null, s.Player.SkipConditionsWhenPlayer?.ToArray());
                Assert.Equal(isBrawl, s.Player.InfluenceOnlySameKingdom);
                // 流浪者事件發生在玩家自己的隊伍裡，不論玩家有沒有出面，隊伍士氣都會動，所以不套用這條
                if (s.Trigger.StartsWith("wanderer", StringComparison.Ordinal)) continue;
                if (e.Decider == null) Assert.All(s.Branches, b => Assert.Null(b.PlayerEffects));
                else Assert.All(s.Branches, b => Assert.NotNull(b.PlayerEffects));
            }
        }

        // ── 讀檔的驗證：玩家用的欄位寫錯只發警告，NPC 之間的情境照常 ──────────────────

        private const string MinimalSituation = @"{ ""situations"": [ {
            ""id"": ""s1"", ""trigger"": ""direct"", ""decider"": ""a"",
            ""roles"": { ""a"": {}, ""b"": {}, ""h"": { ""derived"": ""settlementOwnerClanLeader"", ""optional"": true } },
            ""conditions"": [ { ""type"": ""sameSettlement"", ""roles"": [""a"", ""b""], ""kinds"": [""town""] } ],
            ""branches"": [ { ""id"": ""x"", ""base"": 1.0, ""events"": [ { ""type"": ""e1"", ""bind"": { ""A"": ""a"" } } ] PLAYER_EFFECTS } ]
            PLAYER } ] }";

        private static SituationCatalog LoadMinimal(string player, string effects = "")
        {
            string json = MinimalSituation
                .Replace("PLAYER_EFFECTS", effects.Length == 0 ? "" : ", \"playerEffects\": " + effects)
                .Replace("PLAYER", player.Length == 0 ? "" : ", \"player\": " + player);
            return SituationCatalogLoader.Load(json);
        }

        [Fact]
        public void Loader_NoPlayerBlock_NotOpen()
        {
            var c = LoadMinimal("");
            Assert.Single(c.Situations);
            Assert.Null(c.Situations[0].Player);
            Assert.Null(c.Situations[0].Branches[0].PlayerEffects);
        }

        [Fact]
        public void Loader_ValidBlocks_AreRead()
        {
            var c = LoadMinimal(@"{ ""bystander"": true, ""deciderRole"": ""a"", ""listenerRole"": ""b"", ""skipConditionsWhenPlayer"": [""sameKingdom""], ""influenceOnlySameKingdom"": true }",
                @"{ ""renown"": 2.5, ""influence"": -1, ""morale"": 3, ""traits"": { ""Honor"": 100 } }");
            Assert.Empty(c.Issues);
            var p = c.Situations[0].Player!;
            Assert.True(p.Bystander);
            Assert.Equal("a", p.DeciderRole);
            Assert.Equal("b", p.ListenerRole);
            Assert.True(p.InfluenceOnlySameKingdom);
            var fx = c.Situations[0].Branches[0].PlayerEffects!;
            Assert.Equal(2.5, fx.Renown);
            Assert.Equal(-1.0, fx.Influence);
            Assert.Equal(3.0, fx.Morale);
            Assert.Equal(100, fx.Traits["honor"]);
        }

        [Theory]
        [InlineData(@"[]")]
        [InlineData(@"{ ""bystander"": ""yes"" }")]
        [InlineData(@"{ ""deciderRole"": ""b"" }")]
        [InlineData(@"{ ""deciderRole"": ""nobody"" }")]
        [InlineData(@"{ ""listenerRole"": ""h"" }")]
        [InlineData(@"{ ""listenerRole"": ""a"" }")]
        [InlineData(@"{ ""bystander"": true, ""skipConditionsWhenPlayer"": [""noSuchCondition""] }")]
        [InlineData(@"{ ""bystander"": false }")]
        public void Loader_BadPlayerBlock_WarnsAndClosesItToThePlayer_SituationStillLoads(string player)
        {
            var c = LoadMinimal(player);
            Assert.Single(c.Situations);
            Assert.Null(c.Situations[0].Player);
            Assert.Contains(c.Issues, i => i.Code == SituationIssueCode.InvalidPlayerBlock && !i.IsError);
            Assert.DoesNotContain(c.Issues, i => i.IsError);
        }

        [Theory]
        [InlineData(@"[]")]
        [InlineData(@"{ ""renown"": ""lots"" }")]
        [InlineData(@"{ ""traits"": { ""cunning"": 100 } }")]
        [InlineData(@"{ ""traits"": { ""honor"": 1.5 } }")]
        public void Loader_BadPlayerEffects_WarnsAndClosesTheSituationToThePlayer(string effects)
        {
            var c = LoadMinimal(@"{ ""bystander"": true, ""deciderRole"": ""a"" }", effects);
            Assert.Single(c.Situations);
            Assert.Null(c.Situations[0].Player);
            Assert.Null(c.Situations[0].Branches[0].PlayerEffects);
            Assert.Contains(c.Issues, i => i.Code == SituationIssueCode.InvalidPlayerEffects && !i.IsError);
            Assert.DoesNotContain(c.Issues, i => i.IsError);
        }

        [Fact]
        public void Loader_UnknownKeysInPlayerBlocks_OnlyWarn()
        {
            var c = LoadMinimal(@"{ ""bystander"": true, ""typo"": 1 }", @"{ ""renown"": 1, ""gold"": 5 }");
            Assert.NotNull(c.Situations[0].Player);
            Assert.Equal(1.0, c.Situations[0].Branches[0].PlayerEffects!.Renown);
            Assert.Equal(2, c.Issues.Count(i => i.Code == SituationIssueCode.UnknownProperty && !i.IsError));
        }

        // ── 設定 ────────────────────────────────────────────────────────────────────

        [Fact]
        public void Config_Defaults()
        {
            var cfg = new VividWorldConfig();
            Assert.True(cfg.Situations.PlayerPresence.Enabled);
            Assert.Equal(0.25, cfg.Situations.PlayerPresence.Chance);
            Assert.Equal(7, cfg.Situations.PlayerPresence.CooldownDays);
            Assert.Same(cfg.Situations.PlayerPresence, TownPresenceTrigger.Instance.Settings(cfg));
        }

        [Fact]
        public void Config_Clamped()
        {
            var cfg = new VividWorldConfig();
            cfg.Situations.PlayerPresence.Chance = 1.7;
            cfg.Situations.PlayerPresence.CooldownDays = -3;
            var notices = new List<ClampNotice>();
            cfg.Normalize(notices);
            Assert.Equal(1.0, cfg.Situations.PlayerPresence.Chance);
            Assert.Equal(0, cfg.Situations.PlayerPresence.CooldownDays);
            Assert.Contains(notices, n => n.Key == "situations.playerPresence.chance");
            Assert.Contains(notices, n => n.Key == "situations.playerPresence.cooldownDays");

            cfg.Situations.PlayerPresence = null!;
            cfg.Normalize();
            Assert.NotNull(cfg.Situations.PlayerPresence);
        }

        [Fact]
        public void Config_ExistingConfigJson_GetsTheThreeKeys_ExistingValuesKept()
        {
            var existing = JObject.Parse(@"{ ""situations"": { ""maxPerDay"": 3.0 } }");
            var canonical = JObject.Parse(VividJson.Write(new VividWorldConfig()));
            var merged = ConfigMerge.AddMissingKeys(existing, canonical);
            Assert.Contains(merged.AddedPaths, p => p.StartsWith("situations.playerPresence", StringComparison.Ordinal));
            Assert.True((bool)merged.Merged["situations"]!["playerPresence"]!["enabled"]!);
            Assert.Equal(0.25, (double)merged.Merged["situations"]!["playerPresence"]!["chance"]!);
            Assert.Equal(7, (int)merged.Merged["situations"]!["playerPresence"]!["cooldownDays"]!);
            Assert.Equal(3.0, (double)merged.Merged["situations"]!["maxPerDay"]!);

            var partial = JObject.Parse(@"{ ""situations"": { ""playerPresence"": { ""chance"": 0.5 } } }");
            var merged2 = ConfigMerge.AddMissingKeys(partial, canonical);
            Assert.Equal(0.5, (double)merged2.Merged["situations"]!["playerPresence"]!["chance"]!);
            Assert.Contains("situations.playerPresence.enabled", merged2.AddedPaths);
            Assert.Contains("situations.playerPresence.cooldownDays", merged2.AddedPaths);

            var read = VividJson.Read<VividWorldConfig>(merged2.Merged.ToString());
            Assert.Equal(0.5, read!.Situations.PlayerPresence.Chance);
        }

        [Fact]
        public void Config_OnlyTheSwitchIsInTheSettingsMenu()
        {
            var key = McmExposedKeys.All.Single(k => k.Path == "situations.playerPresence.enabled");
            Assert.Equal(McmKeyKind.Bool, key.Kind);
            Assert.DoesNotContain(McmExposedKeys.All, k => k.Path == "situations.playerPresence.chance");
            Assert.DoesNotContain(McmExposedKeys.All, k => k.Path == "situations.playerPresence.cooldownDays");

            var cfg = new VividWorldConfig();
            Assert.True(McmExposedKeys.Write(cfg, "situations.playerPresence.enabled", false));
            Assert.False(cfg.Situations.PlayerPresence.Enabled);
            Assert.Equal(false, McmExposedKeys.Read(cfg, "situations.playerPresence.enabled"));
        }

        // ── 日誌 ────────────────────────────────────────────────────────────────────

        [Fact]
        public void Log_RollLine_SaysWhereHowManyCooldownChanceAndResult()
        {
            var r = Roll(Input(today: 20.0, last: 5.0), Config(chance: 1.0), Town());
            string line = PlayerPresenceLogFormatter.FormatRoll(r);
            Assert.StartsWith("PlayerPresence[town] entry at town_1 (town): 3 lord(s) or wanderer(s), cooldown over (last on day 5.0, 7d), chance 1, roll ", line);
            Assert.Contains("-> happened: ", line);

            var cd = Roll(Input(today: 6.0, last: 5.0), Config(), Town());
            Assert.EndsWith("chance 1, not rolled -> not rolled: in cooldown", PlayerPresenceLogFormatter.FormatRoll(cd));
        }

        [Fact]
        public void Log_ValueAndTraitAndChronicleLines()
        {
            Assert.Equal("  player renown +3: 120 -> 123", PlayerPresenceLogFormatter.FormatValueChange("renown", 3, 120, 123));
            Assert.Equal("  player influence -5: 40.5 -> 35.5", PlayerPresenceLogFormatter.FormatValueChange("influence", -5, 40.5, 35.5));
            Assert.Equal("  player trait valor +100 xp: xp 950 -> 1050, level 0 -> 1",
                PlayerPresenceLogFormatter.FormatTraitChange("valor", 100, 950, 1050, 0, 1));
            Assert.Equal("  player renown +3: before/after not read", PlayerPresenceLogFormatter.FormatValueChange("renown", 3, null, null));
            Assert.Equal("  player trait honor -200 xp: xp before/after not read, level 1 -> 0",
                PlayerPresenceLogFormatter.FormatTraitChange("honor", -200, null, null, 1, 0));
            Assert.Equal("  player influence not changed: player has no kingdom",
                PlayerPresenceLogFormatter.FormatValueSkipped("influence", "player has no kingdom"));
            Assert.Equal("  chronicle: recorded ev_1 as seen first-hand", PlayerPresenceLogFormatter.FormatChronicle("ev_1", true));
        }
    }
}
