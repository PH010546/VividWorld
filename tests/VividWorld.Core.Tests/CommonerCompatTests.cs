using System.Collections.Generic;
using VividWorld.Core.Config;
using VividWorld.Core.Dialogue;
using Xunit;

namespace VividWorld.Core.Tests
{
    public sealed class CommonerCompatTests
    {
        private static DialogueConfig Cfg() => new DialogueConfig();

        [Fact]
        public void Auto_WithoutTheModule_StaysVanilla()
        {
            var state = CommonerCompat.Resolve(Cfg(), new[] { "TaleWorlds.CampaignSystem", "VividWorld" });

            Assert.False(state.Active);
            Assert.Equal(105, state.NpcLinePriority);
            Assert.Equal(0, state.AskMinClanTier);
            Assert.Equal(0, state.VolunteerMinClanTier);
            Assert.Contains("none of [NaN, Lowborn] loaded", state.Reason);
            Assert.Empty(state.DetectedModules);
        }

        [Fact]
        public void Auto_WithTheModule_RaisesPriorityAndTurnsOnTheTierGate()
        {
            var state = CommonerCompat.Resolve(Cfg(), new[] { "TaleWorlds.CampaignSystem", "NaN", "NaN.MCMBridge" });

            Assert.True(state.Active);
            Assert.Equal(155, state.NpcLinePriority);
            Assert.Equal(1, state.AskMinClanTier);
            Assert.Equal(0, state.VolunteerMinClanTier);
            Assert.Contains("detected NaN", state.Reason);
            Assert.Equal(new List<string> { "NaN" }, state.DetectedModules);
        }

        [Fact]
        public void ModuleNameMatchIsCaseInsensitive()
        {
            var state = CommonerCompat.Resolve(Cfg(), new[] { "nan" });
            Assert.True(state.Active);
        }

        [Fact]
        public void LowbornAloneAlsoTurnsItOn()
        {
            var state = CommonerCompat.Resolve(Cfg(), new[] { "Lowborn", "Titles" });

            Assert.True(state.Active);
            Assert.Equal(155, state.NpcLinePriority);
            Assert.Contains("detected Lowborn", state.Reason);
            Assert.Equal(new List<string> { "Lowborn" }, state.DetectedModules);
        }

        [Fact]
        public void BothDetected_AreBothNamedInTheReason()
        {
            var state = CommonerCompat.Resolve(Cfg(), new[] { "NaN", "Lowborn" });

            Assert.True(state.Active);
            Assert.Contains("detected NaN, Lowborn", state.Reason);
        }

        [Fact]
        public void OffAndOn_OverrideDetection()
        {
            var off = Cfg();
            off.CommonerCompatMode = "off";
            var offState = CommonerCompat.Resolve(off, new[] { "NaN" });
            Assert.False(offState.Active);
            Assert.Equal(105, offState.NpcLinePriority);

            var on = Cfg();
            on.CommonerCompatMode = "on";
            on.VolunteerMode = "realistic";
            var onState = CommonerCompat.Resolve(on, new string[0]);
            Assert.True(onState.Active);
            Assert.Equal(155, onState.NpcLinePriority);
            Assert.Equal(1, onState.AskMinClanTier);

            // LISTEN1d：強制 on 只管優先權；「問」的氏族等級跟著傳聞模式走。
            // 自動模式只看真的載進來的模組——什麼都沒偵測到 ⇒ 暢玩 ⇒ 等級 0，而且偵測名單不能被塞進假名字（它會出現在彈窗裡）。
            var onAuto = Cfg();
            onAuto.CommonerCompatMode = "on";
            var onAutoState = CommonerCompat.Resolve(onAuto, new string[0]);
            Assert.True(onAutoState.Active);
            Assert.Equal(155, onAutoState.NpcLinePriority);
            Assert.Equal(0, onAutoState.AskMinClanTier);
            Assert.Empty(onAutoState.RumorModeResult!.DetectedModules);
        }

        [Fact]
        public void AskIsBlockedAtTierZero_ButVolunteeringIsNot()
        {
            var active = CommonerCompat.Resolve(Cfg(), new[] { "NaN" });

            // 詢問＝玩家跑去攀談 ⇒ Tier 0 擋掉。
            Assert.True(CommonerCompat.BlocksAsk(active, 0));
            Assert.False(CommonerCompat.BlocksAsk(active, 1));
            Assert.False(CommonerCompat.BlocksAsk(active, 4));

            // 主動講＝領主自己開口 ⇒ Tier 0 照講，它自己那道好感 ≥ 30 才是把關的。
            Assert.False(CommonerCompat.BlocksVolunteer(active, 0));
            Assert.False(CommonerCompat.BlocksVolunteer(active, 1));

            // 連氏族都讀不到（-1）就兩條都停，而且會留下診斷行。
            Assert.True(CommonerCompat.BlocksAsk(active, -1));
            Assert.True(CommonerCompat.BlocksVolunteer(active, -1));
        }

        [Fact]
        public void InactiveCompatBlocksNothing()
        {
            var inactive = CommonerCompat.Resolve(Cfg(), new string[0]);

            Assert.False(CommonerCompat.BlocksAsk(inactive, -1));
            Assert.False(CommonerCompat.BlocksAsk(inactive, 0));
            Assert.False(CommonerCompat.BlocksVolunteer(inactive, -1));
            Assert.False(CommonerCompat.BlocksVolunteer(inactive, 0));
            Assert.False(CommonerCompat.BlocksAsk(null, 0));
            Assert.False(CommonerCompat.BlocksVolunteer(null, 0));
        }

        [Fact]
        public void BothTierGatesAreConfigurableIndependently()
        {
            var cfg = Cfg();
            cfg.CommonerCompatAskMinClanTier = 3;
            cfg.CommonerCompatVolunteerMinClanTier = 2;
            var state = CommonerCompat.Resolve(cfg, new[] { "Lowborn" });

            Assert.True(CommonerCompat.BlocksAsk(state, 2));
            Assert.False(CommonerCompat.BlocksAsk(state, 3));
            Assert.True(CommonerCompat.BlocksVolunteer(state, 1));
            Assert.False(CommonerCompat.BlocksVolunteer(state, 2));
        }

        [Fact]
        public void FormatBlocked_NamesTheGateAndBothNumbers()
        {
            var state = CommonerCompat.Resolve(Cfg(), new[] { "NaN" });

            string line = CommonerCompat.FormatBlocked("Ask", "拉斯", "lord_5_131", 0, state.AskMinClanTier, state);
            Assert.Contains("Ask 拉斯 (lord_5_131)", line);
            Assert.Contains("commoner compat is on", line);
            Assert.Contains("detected NaN", line);
            Assert.Contains("player clan tier 0 < 1", line);

            string noClan = CommonerCompat.FormatBlocked("Volunteer", "拉斯", "lord_5_131", -1, state.VolunteerMinClanTier, state);
            Assert.Contains("player clan tier none < 0", noClan);
        }

        [Fact]
        public void ModeChangedAtRuntime_ReappliesModeAndAskGate_WithoutTouchingPriority()
        {
            var cfg = Cfg();
            var state = CommonerCompat.Resolve(cfg, new[] { "NaN", "Lowborn" });
            Assert.Equal(RumorMode.Realistic, state.RumorMode);
            Assert.Equal(1, state.AskMinClanTier);
            Assert.False(CommonerCompat.IsRumorModeStale(state, cfg));

            cfg.VolunteerMode = "casual";
            Assert.True(CommonerCompat.IsRumorModeStale(state, cfg));

            CommonerCompat.ApplyRumorMode(state, cfg);
            Assert.Equal(RumorMode.Casual, state.RumorMode);
            Assert.Equal("forced by config", state.RumorModeResult!.Reason);
            Assert.Equal(0, state.AskMinClanTier);
            Assert.Equal(155, state.NpcLinePriority);
            Assert.False(CommonerCompat.IsRumorModeStale(state, cfg));

            // 換回自動：偵測名單沿用讀檔時的那一份
            cfg.VolunteerMode = "auto";
            CommonerCompat.ApplyRumorMode(state, cfg);
            Assert.Equal(RumorMode.Realistic, state.RumorMode);
            Assert.Contains("detected NaN, Lowborn", state.RumorModeResult!.Reason);
            Assert.Equal(1, state.AskMinClanTier);
        }

        [Fact]
        public void ModeChangedAtRuntime_WithoutCommonerMod_NeverAddsAskGate()
        {
            var cfg = Cfg();
            var state = CommonerCompat.Resolve(cfg, new[] { "TaleWorlds.CampaignSystem" });
            Assert.Equal(RumorMode.Casual, state.RumorMode);

            cfg.VolunteerMode = "realistic";
            CommonerCompat.ApplyRumorMode(state, cfg);
            Assert.Equal(RumorMode.Realistic, state.RumorMode);
            Assert.Equal(0, state.AskMinClanTier);
        }

        [Fact]
        public void StaleCheck_IgnoresCaseAndSurroundingSpaces()
        {
            var cfg = Cfg();
            var state = CommonerCompat.Resolve(cfg, new string[0]);
            cfg.VolunteerMode = " AUTO ";
            Assert.False(CommonerCompat.IsRumorModeStale(state, cfg));
        }

        [Fact]
        public void FormatRegistration_SaysWhichSetIsInUseAndWhy()
        {
            string on = CommonerCompat.FormatRegistration(
                CommonerCompat.Resolve(Cfg(), new[] { "NaN" }), "lord_start", 105);
            Assert.Contains("Commoner compat: ON", on);
            Assert.Contains("105 -> 155", on);
            Assert.Contains("asking needs tier >= 1", on);
            Assert.Contains("volunteering needs tier >= 0", on);

            string off = CommonerCompat.FormatRegistration(
                CommonerCompat.Resolve(Cfg(), new string[0]), "lord_start", 105);
            Assert.Contains("Commoner compat: OFF", off);
            Assert.Contains("priority 105", off);
            Assert.Contains("no clan-tier gate", off);
        }
    }
}
