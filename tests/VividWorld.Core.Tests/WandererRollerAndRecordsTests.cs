using System;
using System.Collections.Generic;
using System.Linq;
using VividWorld.Core.Config;
using VividWorld.Core.Diagnostics;
using VividWorld.Core.Persistence;
using VividWorld.Core.Rumors;
using VividWorld.Core.Situations;
using VividWorld.Core.Tests.Fakes;
using VividWorld.Core.Util;
using Xunit;

namespace VividWorld.Core.Tests
{
    public class WandererRollerAndRecordsTests
    {
        private const string Player = "player_hero";

        private sealed class FakeWandererEvaluator : IPlayerPresenceEvaluator
        {
            public readonly HashSet<string> FailingSituations = new(StringComparer.OrdinalIgnoreCase);
            public readonly List<(string SituationId, Dictionary<string, string> Assignment)> Seen = new();

            public PlayerPresenceEvaluation Evaluate(
                SituationTemplate template,
                IReadOnlyList<SituationConditionDef> conditions,
                IReadOnlyDictionary<string, string> nonDerivedRoleHeroIds,
                bool forced)
            {
                var copy = new Dictionary<string, string>(nonDerivedRoleHeroIds, StringComparer.OrdinalIgnoreCase);
                Seen.Add((template.Id, copy));

                if (FailingSituations.Contains(template.Id))
                {
                    return new PlayerPresenceEvaluation
                    {
                        ConditionResults = new[]
                        {
                            new ConditionEvaluationResult
                            {
                                Ok = false,
                                Type = "customFail",
                                Label = "customFail",
                                Detail = $"Situation {template.Id} failed artificially"
                            }
                        },
                        BoundHeroIds = nonDerivedRoleHeroIds.ToDictionary(k => k.Key, k => (string?)k.Value)
                    };
                }

                return new PlayerPresenceEvaluation
                {
                    ConditionResults = Array.Empty<ConditionEvaluationResult>(),
                    BoundHeroIds = nonDerivedRoleHeroIds.ToDictionary(k => k.Key, k => (string?)k.Value)
                };
            }
        }

        private static SituationTemplate MakeBattleTemplate(string id, bool hasJudge, double weight = 1.0)
        {
            var t = new SituationTemplate
            {
                Id = id,
                Trigger = "wandererBattle",
                Weight = weight,
                Decider = "rival"
            };
            t.Roles["claimant"] = new SituationRoleDef();
            t.Roles["rival"] = new SituationRoleDef();
            t.Roles["employer"] = new SituationRoleDef { PlayerOnly = true };
            t.Player = new SituationPlayerDef { Judge = hasJudge, JudgeRole = "employer" };

            t.Branches.Add(new SituationBranchDef { Id = "plain_concession", Base = 1.0 });
            t.Branches.Add(new SituationBranchDef { Id = "plain_part_ways", Base = 1.0 });
            if (hasJudge)
            {
                t.Branches.Add(new SituationBranchDef { Id = "decide_stand_aside", Base = 1.0, Judge = true, StandAside = true });
                t.Branches.Add(new SituationBranchDef { Id = "decide_credit_a", Base = 1.0, Judge = true });
            }
            return t;
        }

        private static SituationTemplate MakeDailyTemplate(string id, bool hasJudge, double weight = 1.0)
        {
            var t = new SituationTemplate
            {
                Id = id,
                Trigger = "wandererDaily",
                Weight = weight,
                Decider = "challenger"
            };
            t.Roles["incumbent"] = new SituationRoleDef();
            t.Roles["challenger"] = new SituationRoleDef();
            t.Roles["employer"] = new SituationRoleDef { PlayerOnly = true };
            t.Player = new SituationPlayerDef { Judge = hasJudge, JudgeRole = "employer" };

            t.Branches.Add(new SituationBranchDef { Id = "plain_keep", Base = 1.0 });
            t.Branches.Add(new SituationBranchDef { Id = "plain_disagree", Base = 1.0 });
            if (hasJudge)
            {
                t.Branches.Add(new SituationBranchDef { Id = "decide_stand_aside", Base = 1.0, Judge = true, StandAside = true });
                t.Branches.Add(new SituationBranchDef { Id = "decide_swap", Base = 1.0, Judge = true });
            }
            return t;
        }

        private static WandererRollInput DefaultInput(WandererTrigger trigger = WandererTrigger.Daily)
        {
            return new WandererRollInput
            {
                Trigger = trigger,
                Today = 20.0,
                LastBattleDay = null,
                LastDailyDay = null,
                LastAnyDay = null,
                AvailableWandererHeroIds = new[] { "w_1", "w_2", "w_3" },
                PlayerHeroId = Player,
                TownWindowShownToday = false,
                PlayerCaptured = false,
                InBattle = false,
                InSiege = false,
                CampaignSeed = 12345
            };
        }

        private static VividWorldConfig DefaultConfig()
        {
            var cfg = new VividWorldConfig();
            cfg.Situations.WandererEvents.Enabled = true;
            cfg.Situations.WandererEvents.BattleChance = 1.0;
            cfg.Situations.WandererEvents.DailyChance = 1.0;
            cfg.Situations.WandererEvents.BattleCooldownDays = 10;
            cfg.Situations.WandererEvents.DailyCooldownDays = 10;
            cfg.Situations.WandererEvents.MinGapDays = 3;
            cfg.Situations.WandererEvents.UnseenShare = 0.5;
            cfg.Situations.WandererEvents.RoleRevertDays = 30;
            return cfg;
        }

        // ====================================================================
        // 1. 每一條「不擲」的檢查順序與阻斷
        // ====================================================================

        [Fact]
        public void Roll_Disabled_DoesNotRoll()
        {
            var cfg = DefaultConfig();
            cfg.Situations.WandererEvents.Enabled = false;

            var r = WandererEventRoller.Roll(DefaultInput(), cfg, new[] { MakeDailyTemplate("t1", true) }, new FakeWandererEvaluator(), new SplitMix64Rng());
            Assert.Equal(WandererRollOutcome.Disabled, r.Outcome);
            Assert.Equal("disabled in settings", r.Reason);
            Assert.Contains("disabled in settings", WandererEventLogFormatter.FormatRoll(r));
        }

        [Fact]
        public void Roll_PlayerCaptured_NotRolled()
        {
            var input = DefaultInput();
            input.PlayerCaptured = true;

            var r = WandererEventRoller.Roll(input, DefaultConfig(), new[] { MakeDailyTemplate("t1", true) }, new FakeWandererEvaluator(), new SplitMix64Rng());
            Assert.Equal(WandererRollOutcome.PlayerCaptured, r.Outcome);
            Assert.Equal("player is a prisoner", r.Reason);
            Assert.Contains("player is a prisoner", WandererEventLogFormatter.FormatRoll(r));
        }

        [Fact]
        public void Roll_Daily_InBattle_NotRolled()
        {
            var input = DefaultInput(WandererTrigger.Daily);
            input.InBattle = true;

            var r = WandererEventRoller.Roll(input, DefaultConfig(), new[] { MakeDailyTemplate("t1", true) }, new FakeWandererEvaluator(), new SplitMix64Rng());
            Assert.Equal(WandererRollOutcome.InBattle, r.Outcome);
            Assert.Equal("player party is in battle", r.Reason);
        }

        [Fact]
        public void Roll_Daily_InSiege_NotRolled()
        {
            var input = DefaultInput(WandererTrigger.Daily);
            input.InSiege = true;

            var r = WandererEventRoller.Roll(input, DefaultConfig(), new[] { MakeDailyTemplate("t1", true) }, new FakeWandererEvaluator(), new SplitMix64Rng());
            Assert.Equal(WandererRollOutcome.InSiege, r.Outcome);
            Assert.Equal("player party is in a siege", r.Reason);
        }

        [Fact]
        public void Roll_Battle_InBattle_IsAllowed()
        {
            // 戰後那類不受 InBattle 阻斷（戰鬥結束當下可能仍處於戰鬥結算中）
            var input = DefaultInput(WandererTrigger.Battle);
            input.InBattle = true;

            var r = WandererEventRoller.Roll(input, DefaultConfig(), new[] { MakeBattleTemplate("t1", true) }, new FakeWandererEvaluator(), new SplitMix64Rng());
            Assert.Equal(WandererRollOutcome.Happened, r.Outcome);
        }

        [Fact]
        public void Roll_TownWindowAlreadyShown_NotRolled()
        {
            var input = DefaultInput();
            input.TownWindowShownToday = true;

            var r = WandererEventRoller.Roll(input, DefaultConfig(), new[] { MakeDailyTemplate("t1", true) }, new FakeWandererEvaluator(), new SplitMix64Rng());
            Assert.Equal(WandererRollOutcome.TownWindowAlreadyShown, r.Outcome);
            Assert.Equal("town window already shown today", r.Reason);
        }

        [Fact]
        public void Roll_FewerThanTwoWanderers_NotRolled()
        {
            var input = DefaultInput();
            // 只有一個流浪者，且玩家在清單中會被剔除
            input.AvailableWandererHeroIds = new[] { "w_1", Player };

            var r = WandererEventRoller.Roll(input, DefaultConfig(), new[] { MakeDailyTemplate("t1", true) }, new FakeWandererEvaluator(), new SplitMix64Rng());
            Assert.Equal(WandererRollOutcome.TooFewWanderers, r.Outcome);
            Assert.Equal(1, r.AvailableWandererCount);
            Assert.Contains("1 available wanderer(s), need 2", r.Reason);
        }

        [Fact]
        public void Roll_InCooldown_NotRolled()
        {
            var input = DefaultInput(WandererTrigger.Daily);
            input.Today = 25.0;
            input.LastDailyDay = 20.0; // elapsed = 5 < 10

            var r = WandererEventRoller.Roll(input, DefaultConfig(), new[] { MakeDailyTemplate("t1", true) }, new FakeWandererEvaluator(), new SplitMix64Rng());
            Assert.Equal(WandererRollOutcome.Cooldown, r.Outcome);
            Assert.Equal("in cooldown", r.Reason);
            Assert.Equal(5.0, r.CooldownRemainingDays!.Value, 3);
        }

        [Fact]
        public void Roll_InGapCooldown_NotRolled()
        {
            var input = DefaultInput(WandererTrigger.Daily);
            input.Today = 22.0;
            input.LastDailyDay = 10.0; // cooldown over (12 > 10)
            input.LastAnyDay = 21.0;   // gap elapsed = 1 < 3

            var r = WandererEventRoller.Roll(input, DefaultConfig(), new[] { MakeDailyTemplate("t1", true) }, new FakeWandererEvaluator(), new SplitMix64Rng());
            Assert.Equal(WandererRollOutcome.GapCooldown, r.Outcome);
            Assert.Equal("gap cooldown", r.Reason);
            Assert.Equal(2.0, r.GapRemainingDays!.Value, 3);
        }

        [Fact]
        public void Roll_ChanceMissed_OutcomeIsChanceMissed()
        {
            var input = DefaultInput();
            var cfg = DefaultConfig();
            cfg.Situations.WandererEvents.DailyChance = 0.0;

            var r = WandererEventRoller.Roll(input, cfg, new[] { MakeDailyTemplate("t1", true) }, new FakeWandererEvaluator(), new SplitMix64Rng());
            Assert.Equal(WandererRollOutcome.ChanceMissed, r.Outcome);
            Assert.Equal("chance missed", r.Reason);
            Assert.NotNull(r.Roll);
            Assert.Contains("missed", WandererEventLogFormatter.FormatRoll(r));
        }

        // ====================================================================
        // 2. 冷卻與間隔的邊界（剛好到期）
        // ====================================================================

        [Fact]
        public void Cooldown_Boundary_BlocksJustBefore_RollsAtBoundary()
        {
            var cfg = DefaultConfig();
            cfg.Situations.WandererEvents.DailyCooldownDays = 10;

            // 剛好差 0.1 天：阻擋
            var before = DefaultInput(WandererTrigger.Daily);
            before.Today = 29.9;
            before.LastDailyDay = 20.0;
            var rBefore = WandererEventRoller.Roll(before, cfg, new[] { MakeDailyTemplate("t1", true) }, new FakeWandererEvaluator(), new SplitMix64Rng());
            Assert.Equal(WandererRollOutcome.Cooldown, rBefore.Outcome);
            Assert.Equal(0.1, rBefore.CooldownRemainingDays!.Value, 3);

            // 剛好滿 10 天：允許擲骰
            var at = DefaultInput(WandererTrigger.Daily);
            at.Today = 30.0;
            at.LastDailyDay = 20.0;
            var rAt = WandererEventRoller.Roll(at, cfg, new[] { MakeDailyTemplate("t1", true) }, new FakeWandererEvaluator(), new SplitMix64Rng());
            Assert.Equal(WandererRollOutcome.Happened, rAt.Outcome);
            Assert.Equal(0.0, rAt.CooldownRemainingDays!.Value, 3);
        }

        [Fact]
        public void MinGap_Boundary_BlocksJustBefore_RollsAtBoundary()
        {
            var cfg = DefaultConfig();
            cfg.Situations.WandererEvents.MinGapDays = 3;

            // 差 0.1 天：阻擋
            var before = DefaultInput(WandererTrigger.Daily);
            before.Today = 22.9;
            before.LastAnyDay = 20.0;
            var rBefore = WandererEventRoller.Roll(before, cfg, new[] { MakeDailyTemplate("t1", true) }, new FakeWandererEvaluator(), new SplitMix64Rng());
            Assert.Equal(WandererRollOutcome.GapCooldown, rBefore.Outcome);
            Assert.Equal(0.1, rBefore.GapRemainingDays!.Value, 3);

            // 剛好滿 3 天：允許
            var at = DefaultInput(WandererTrigger.Daily);
            at.Today = 23.0;
            at.LastAnyDay = 20.0;
            var rAt = WandererEventRoller.Roll(at, cfg, new[] { MakeDailyTemplate("t1", true) }, new FakeWandererEvaluator(), new SplitMix64Rng());
            Assert.Equal(WandererRollOutcome.Happened, rAt.Outcome);
            Assert.Equal(0.0, rAt.GapRemainingDays!.Value, 3);
        }

        // ====================================================================
        // 3. 同天城裡視窗
        // ====================================================================

        [Fact]
        public void TownWindow_SameDayBucket_Blocks_EarlierDayBucket_DoesNotBlock()
        {
            var inputSame = DefaultInput();
            inputSame.Today = 15.4;
            inputSame.LastTownPresenceDay = 15.1; // same day bucket

            var rSame = WandererEventRoller.Roll(inputSame, DefaultConfig(), new[] { MakeDailyTemplate("t1", true) }, new FakeWandererEvaluator(), new SplitMix64Rng());
            Assert.Equal(WandererRollOutcome.TownWindowAlreadyShown, rSame.Outcome);

            var inputEarlier = DefaultInput();
            inputEarlier.Today = 15.4;
            inputEarlier.LastTownPresenceDay = 14.8; // earlier day bucket

            var rEarlier = WandererEventRoller.Roll(inputEarlier, DefaultConfig(), new[] { MakeDailyTemplate("t1", true) }, new FakeWandererEvaluator(), new SplitMix64Rng());
            Assert.Equal(WandererRollOutcome.Happened, rEarlier.Outcome);
        }

        // ====================================================================
        // 4. 私下與不插手只擲非裁決做法
        // ====================================================================

        [Fact]
        public void RollNonJudgeBranch_NeverSelectsJudgeBranch_EvenIfHeavilyWeighted()
        {
            var t = MakeDailyTemplate("dispute", hasJudge: true);
            // 替裁決分支與不插手分支加上極高 Base
            t.Branches.Single(b => b.Id == "decide_swap").Base = 1000.0;
            t.Branches.Single(b => b.Id == "decide_stand_aside").Base = 1000.0;

            var deciderTraits = new TraitProfile { HeroId = "w_2", Valor = 2, Calculating = 2, IsAlive = true, IsLord = false };
            var rng = new SplitMix64Rng();

            for (long seed = 1; seed <= 50; seed++)
            {
                var decision = WandererEventRoller.RollNonJudgeBranch(
                    t,
                    factsByRole: null,
                    boundHeroes: new Dictionary<string, string?> { ["incumbent"] = "w_1", ["challenger"] = "w_2", ["employer"] = Player },
                    unboundReasons: null,
                    deciderTraits: deciderTraits,
                    defaultMinBranchWeight: 0.1,
                    rng: rng,
                    seed: seed);

                Assert.NotNull(decision.SelectedBranchId);
                Assert.True(decision.SelectedBranchId == "plain_keep" || decision.SelectedBranchId == "plain_disagree",
                    $"Selected branch {decision.SelectedBranchId} was expected to be non-judge!");
                Assert.False(decision.SelectedBranch!.Judge);
            }
        }

        // ====================================================================
        // 5. 看到時有裁決做法的一律是做決定
        // ====================================================================

        [Fact]
        public void Seen_WithJudgeBranches_IsDecide()
        {
            var input = DefaultInput();
            input.Force = new WandererRollForce { Unseen = false };

            var tJudge = MakeDailyTemplate("with_judge", hasJudge: true);
            var r = WandererEventRoller.Roll(input, DefaultConfig(), new[] { tJudge }, new FakeWandererEvaluator(), new SplitMix64Rng());

            Assert.Equal(WandererRollOutcome.Happened, r.Outcome);
            Assert.False(r.IsUnseen);
            Assert.Equal(WandererDecisionKind.Decide, r.DecisionKind);
            Assert.Equal(WandererDecisionKind.Decide, r.Pick!.DecisionKind);
        }

        [Fact]
        public void Seen_WithoutJudgeBranches_IsWatch()
        {
            var input = DefaultInput();
            input.Force = new WandererRollForce { Unseen = false };

            var tNoJudge = MakeDailyTemplate("no_judge", hasJudge: false);
            var r = WandererEventRoller.Roll(input, DefaultConfig(), new[] { tNoJudge }, new FakeWandererEvaluator(), new SplitMix64Rng());

            Assert.Equal(WandererRollOutcome.Happened, r.Outcome);
            Assert.False(r.IsUnseen);
            Assert.Equal(WandererDecisionKind.Watch, r.DecisionKind);
            Assert.Equal(WandererDecisionKind.Watch, r.Pick!.DecisionKind);
        }

        [Fact]
        public void Daily_Unseen_IsMarkedUnseen()
        {
            var input = DefaultInput();
            input.Force = new WandererRollForce { Unseen = true };

            var tJudge = MakeDailyTemplate("with_judge", hasJudge: true);
            var r = WandererEventRoller.Roll(input, DefaultConfig(), new[] { tJudge }, new FakeWandererEvaluator(), new SplitMix64Rng());

            Assert.Equal(WandererRollOutcome.Happened, r.Outcome);
            Assert.True(r.IsUnseen);
            Assert.Equal(WandererDecisionKind.Unseen, r.DecisionKind);
            Assert.Equal(WandererDecisionKind.Unseen, r.Pick!.DecisionKind);
        }

        // ====================================================================
        // 6. 強制（Force）
        // ====================================================================

        [Fact]
        public void Force_IgnoresCooldownGapAndChance_AndPicksTarget()
        {
            var input = DefaultInput(WandererTrigger.Daily);
            input.Today = 21.0;
            input.LastDailyDay = 20.0; // cooldown
            input.LastAnyDay = 20.5;   // gap cooldown
            input.Force = new WandererRollForce { SituationId = "target_daily", Unseen = false };

            var cfg = DefaultConfig();
            cfg.Situations.WandererEvents.DailyChance = 0.0; // chance 0

            var templates = new[]
            {
                MakeDailyTemplate("other_daily", hasJudge: true),
                MakeDailyTemplate("target_daily", hasJudge: true)
            };

            var r = WandererEventRoller.Roll(input, cfg, templates, new FakeWandererEvaluator(), new SplitMix64Rng());

            Assert.Equal(WandererRollOutcome.Happened, r.Outcome);
            Assert.True(r.Forced);
            Assert.Null(r.Roll);
            Assert.Equal("target_daily", r.Pick!.Template.Id);
            Assert.False(r.IsUnseen);
            Assert.Equal(WandererDecisionKind.Decide, r.DecisionKind);
        }

        [Fact]
        public void Force_StillHonorsDisabledSwitch()
        {
            var input = DefaultInput();
            input.Force = new WandererRollForce();
            var cfg = DefaultConfig();
            cfg.Situations.WandererEvents.Enabled = false;

            var r = WandererEventRoller.Roll(input, cfg, new[] { MakeDailyTemplate("t1", true) }, new FakeWandererEvaluator(), new SplitMix64Rng());
            Assert.Equal(WandererRollOutcome.Disabled, r.Outcome);
        }

        // ====================================================================
        // 7. 換人紀錄五種狀態
        // ====================================================================

        [Fact]
        public void CheckDay_Active_KeptInSwaps()
        {
            var records = new WandererRoleRecords();
            records.RecordSwap("scout", "w_old", "w_new", 10.0, "swap");

            var todayRoles = new Dictionary<string, string?> { ["scout"] = "w_new" };
            var result = records.CheckDay(todayRoles, today: 20.0, roleRevertDays: 30.0, id => true);

            Assert.Single(result.Evaluations);
            Assert.Equal(WandererRoleSwapStatus.Active, result.Evaluations[0].Status);
            Assert.Empty(result.Reverts);
            Assert.Single(records.Swaps);
            Assert.Equal("w_new", records.Yesterday["scout"]);
        }

        [Fact]
        public void CheckDay_Reverted_GeneratesRevertActionAndSettles()
        {
            var records = new WandererRoleRecords();
            records.RecordSwap("scout", "w_old", "w_new", 10.0, "swap");

            // 今天換回去了（原 holder w_old）
            var todayRoles = new Dictionary<string, string?> { ["scout"] = "w_old" };
            var result = records.CheckDay(todayRoles, today: 25.0, roleRevertDays: 30.0, id => true);

            Assert.Single(result.Evaluations);
            Assert.Equal(WandererRoleSwapStatus.Reverted, result.Evaluations[0].Status);

            Assert.Single(result.Reverts);
            var rev = result.Reverts[0];
            Assert.Equal("scout", rev.Role);
            Assert.Equal("w_new", rev.ReplacedHeroId);
            Assert.Equal("w_old", rev.OriginalHeroId);
            Assert.Equal(10.0, rev.SwapDay);
            Assert.Equal(25.0, rev.RevertDay);
            Assert.Equal(-8, rev.RelationChange);
            Assert.Equal(-1.0, rev.MoraleChange);

            // 結清：不再保留在 Swaps 裡
            Assert.Empty(records.Swaps);
            Assert.Equal("w_old", records.Yesterday["scout"]);
        }

        [Fact]
        public void CheckDay_Expired_SettlesWithoutAction()
        {
            var records = new WandererRoleRecords();
            records.RecordSwap("scout", "w_old", "w_new", 10.0, "swap");

            // 超過 30 天
            var todayRoles = new Dictionary<string, string?> { ["scout"] = "w_new" };
            var result = records.CheckDay(todayRoles, today: 40.1, roleRevertDays: 30.0, id => true);

            Assert.Single(result.Evaluations);
            Assert.Equal(WandererRoleSwapStatus.Expired, result.Evaluations[0].Status);
            Assert.Empty(result.Reverts);
            Assert.Empty(records.Swaps);
        }

        [Fact]
        public void CheckDay_PartyLeft_SettlesWithoutAction()
        {
            var records = new WandererRoleRecords();
            records.RecordSwap("scout", "w_old", "w_new", 10.0, "swap");

            var todayRoles = new Dictionary<string, string?> { ["scout"] = "w_new" };
            // w_new 離隊
            var result = records.CheckDay(todayRoles, today: 15.0, roleRevertDays: 30.0, id => id != "w_new");

            Assert.Single(result.Evaluations);
            Assert.Equal(WandererRoleSwapStatus.PartyLeft, result.Evaluations[0].Status);
            Assert.Empty(result.Reverts);
            Assert.Empty(records.Swaps);
        }

        [Fact]
        public void CheckDay_ReplacedByThirdParty_SettlesWithoutAction()
        {
            var records = new WandererRoleRecords();
            records.RecordSwap("scout", "w_old", "w_new", 10.0, "swap");

            // 換成第三人 w_third
            var todayRoles = new Dictionary<string, string?> { ["scout"] = "w_third" };
            var result = records.CheckDay(todayRoles, today: 15.0, roleRevertDays: 30.0, id => true);

            Assert.Single(result.Evaluations);
            Assert.Equal(WandererRoleSwapStatus.ReplacedByThirdParty, result.Evaluations[0].Status);
            Assert.Empty(result.Reverts);
            Assert.Empty(records.Swaps);
        }

        // ====================================================================
        // 8. yesterday 第一次為空
        // ====================================================================

        [Fact]
        public void CheckDay_FirstDay_YesterdayIsEmpty_SavesTodayRolesWithoutReverts()
        {
            var records = new WandererRoleRecords();
            Assert.Empty(records.Yesterday);

            var todayRoles = new Dictionary<string, string?>
            {
                ["scout"] = "w_1",
                ["quartermaster"] = "w_2",
                ["engineer"] = "w_3",
                ["surgeon"] = "w_4"
            };

            var result = records.CheckDay(todayRoles, today: 10.0, roleRevertDays: 30.0, id => true);

            Assert.Empty(result.Evaluations);
            Assert.Empty(result.Reverts);
            Assert.Equal(4, records.Yesterday.Count);
            Assert.Equal("w_1", records.Yesterday["scout"]);
            Assert.Equal("w_4", records.Yesterday["surgeon"]);
        }

        // ====================================================================
        // 9. 存檔相容：沒有檔、空物件、版本不認得、壞掉四種都讀成空紀錄
        // ====================================================================

        private sealed class CapturingSink : ILogSink
        {
            public readonly List<string> Warns = new();
            public readonly List<string> Infos = new();
            public readonly List<string> Errors = new();

            public void Warn(string message) => Warns.Add(message);
            public void Info(string message) => Infos.Add(message);
            public void Error(string message, Exception? ex = null) => Errors.Add(ex == null ? message : $"{message}: {ex.Message}");
            public void Flush() { }
        }

        [Fact]
        public void Store_FileNotFound_ReadsAsEmptyRecord()
        {
            var writer = new FailingFileWriter();
            var store = new WandererRoleRecordStore("wanderer_roles.json", writer);

            var res = store.Load();
            Assert.Equal(WandererRoleLoadStatus.FileNotFound, res.Status);
            Assert.Empty(store.Data.Swaps);
            Assert.Empty(store.Data.Yesterday);
            Assert.Null(store.Data.PendingComplaint);
        }

        [Fact]
        public void Store_EmptyObject_ReadsAsEmptyRecord()
        {
            var writer = new FailingFileWriter();
            writer.WriteAllText("wanderer_roles.json", "{}");
            var store = new WandererRoleRecordStore("wanderer_roles.json", writer);

            var res = store.Load();
            Assert.Equal(WandererRoleLoadStatus.Corrupt, res.Status);
            Assert.Empty(store.Data.Swaps);
            Assert.Empty(store.Data.Yesterday);
            Assert.Null(store.Data.PendingComplaint);
        }

        [Fact]
        public void Store_UnknownVersion_ReadsAsEmptyRecord()
        {
            var writer = new FailingFileWriter();
            writer.WriteAllText("wanderer_roles.json", @"{ ""version"": 999, ""yesterday"": { ""scout"": ""w_1"" } }");
            var sink = new CapturingSink();
            var store = new WandererRoleRecordStore("wanderer_roles.json", writer, sink);

            var res = store.Load();
            Assert.Equal(WandererRoleLoadStatus.Corrupt, res.Status);
            Assert.Empty(store.Data.Swaps);
            Assert.Empty(store.Data.Yesterday);
            Assert.NotEmpty(sink.Warns);
            Assert.NotNull(res.SetAsidePath);
        }

        [Fact]
        public void Store_CorruptJson_ReadsAsEmptyRecord()
        {
            var writer = new FailingFileWriter();
            writer.WriteAllText("wanderer_roles.json", "{ malformed json: not closed");
            var sink = new CapturingSink();
            var store = new WandererRoleRecordStore("wanderer_roles.json", writer, sink);

            var res = store.Load();
            Assert.Equal(WandererRoleLoadStatus.Corrupt, res.Status);
            Assert.Empty(store.Data.Swaps);
            Assert.Empty(store.Data.Yesterday);
            Assert.NotEmpty(sink.Warns);
            Assert.NotNull(res.SetAsidePath);
        }

        [Fact]
        public void Store_SaveAndLoad_RoundtripsSuccessfully()
        {
            var writer = new FailingFileWriter();
            var store = new WandererRoleRecordStore("wanderer_roles.json", writer);
            store.Records.RecordSwap("scout", "w_old", "w_new", 12.5, "swap");
            store.Records.PendingComplaint = new WandererPendingComplaint
            {
                HeroId = "w_new",
                Role = "scout",
                Day = 15.0,
                RelationBefore = 10,
                RelationAfter = 2
            };

            bool saved = store.Save();
            Assert.True(saved);

            var store2 = new WandererRoleRecordStore("wanderer_roles.json", writer);
            var res = store2.Load();
            Assert.Equal(WandererRoleLoadStatus.Success, res.Status);
            Assert.Single(store2.Data.Swaps);
            Assert.Equal("scout", store2.Data.Swaps[0].Role);
            Assert.Equal("w_old", store2.Data.Swaps[0].From);
            Assert.Equal("w_new", store2.Data.Swaps[0].To);
            Assert.NotNull(store2.Data.PendingComplaint);
            Assert.Equal("w_new", store2.Data.PendingComplaint!.HeroId);
        }

        // ====================================================================
        // 10. 戰鬥序號進種子與格式化輸出驗證
        // ====================================================================

        [Fact]
        public void BattleSequence_DifferentBattlesOnSameDay_ProduceDifferentRolls()
        {
            var input0 = DefaultInput(WandererTrigger.Battle);
            input0.BattleSequence = 0;
            var input1 = DefaultInput(WandererTrigger.Battle);
            input1.BattleSequence = 1;

            var cfg = DefaultConfig();
            cfg.Situations.WandererEvents.BattleChance = 0.5;

            var r0 = WandererEventRoller.Roll(input0, cfg, new[] { MakeBattleTemplate("t1", true) }, new FakeWandererEvaluator(), new SplitMix64Rng());
            var r1 = WandererEventRoller.Roll(input1, cfg, new[] { MakeBattleTemplate("t1", true) }, new FakeWandererEvaluator(), new SplitMix64Rng());

            Assert.NotEqual(r0.Roll, r1.Roll);
        }

        [Fact]
        public void Formatters_FormatRoll_Candidates_Decision_AndStatus()
        {
            var input = DefaultInput(WandererTrigger.Daily);
            input.Force = new WandererRollForce { Unseen = false };

            var t = MakeDailyTemplate("t_test", hasJudge: true, weight: 2.5);
            var r = WandererEventRoller.Roll(input, DefaultConfig(), new[] { t }, new FakeWandererEvaluator(), new SplitMix64Rng());

            string rollStr = WandererEventLogFormatter.FormatRoll(r);
            Assert.Contains("WandererEvent[daily]", rollStr);
            Assert.Contains("happened: t_test as decide", rollStr);

            var candLines = WandererEventLogFormatter.FormatCandidates(r);
            Assert.Contains(candLines, l => l.Contains("candidate t_test:"));
            Assert.Contains(candLines, l => l.Contains("picked t_test as decide"));

            var decision = WandererEventRoller.RollNonJudgeBranch(t, null, null, null,
                new TraitProfile { HeroId = "w_2", IsAlive = true, IsLord = false }, 0.1, new SplitMix64Rng(), 42);
            string decStr = WandererEventLogFormatter.FormatDecision("t_test", decision);
            Assert.Contains("decision for t_test:", decStr);
            Assert.Contains("plain_keep", decStr);

            string excludedStr = WandererEventLogFormatter.FormatExcludedWanderers(new[] { ("w_9", "wounded") });
            Assert.Contains("w_9 (wounded)", excludedStr);

            var checkResult = new WandererRoleCheckDayResult();
            checkResult.Evaluations.Add(new WandererRoleEvaluation
            {
                Swap = new WandererRoleSwapRecord { Role = "scout", From = "w_1", To = "w_2", Day = 10.0, Source = "swap" },
                Status = WandererRoleSwapStatus.Active,
                Reason = "active"
            });
            var roleCheckLines = WandererEventLogFormatter.FormatRoleCheck(checkResult,
                new Dictionary<string, string?> { ["scout"] = "w_2" },
                new Dictionary<string, string?> { ["scout"] = "w_1" });
            Assert.Contains(roleCheckLines, l => l.Contains("roles today: scout=w_2"));
            Assert.Contains(roleCheckLines, l => l.Contains("swap record scout"));

            string statusStr = WandererEventLogFormatter.FormatStatus(true, 20.0, 15.0, 10.0, 18.0, DefaultConfig().Situations.WandererEvents, new WandererRoleData());
            Assert.Contains("Wanderer Events Status: enabled=True", statusStr);
            Assert.Contains("Battle: lastDay=15.0", statusStr);
            Assert.Contains("Daily: lastDay=10.0", statusStr);
            Assert.Contains("MinGap: lastAnyDay=18.0", statusStr);
        }
    }
}
