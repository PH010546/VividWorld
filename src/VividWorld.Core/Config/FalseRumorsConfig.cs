using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace VividWorld.Core.Config
{
    /// <summary>聽到消息的人信不信的各項加減。機率單位是百分點（0～100），好感門檻是遊戲裡的個人好感度。</summary>
    public sealed class BeliefConfig
    {
        public double BaseChance { get; set; } = 70;

        // 對被說的人的好感：交情好的人比較不信他會幹這種事（壞話）；有怨的人比較容易信
        public int    SubjectRelationFriend  { get; set; } = 30;
        public double SubjectFriendDelta     { get; set; } = -40;
        public int    SubjectRelationWarm    { get; set; } = 10;
        public double SubjectWarmDelta       { get; set; } = -20;
        public int    SubjectRelationHostile { get; set; } = -20;
        public double SubjectHostileDelta    { get; set; } = 20;

        // 對告訴他的人的好感
        public int    TellerRelationTrusted     { get; set; } = 30;
        public double TellerTrustedDelta        { get; set; } = 15;
        public int    TellerRelationDistrusted  { get; set; } = -20;
        public double TellerDistrustedDelta     { get; set; } = -25;

        // 這件事像不像被說的人會做的
        public double FitsTraitDelta        { get; set; } = 20;
        public double ContradictsTraitDelta { get; set; } = -30;

        /// <summary>聽者的理性每高一級加的點數（理性 +2 ＝ 兩倍；−1 ＝ 反號）。</summary>
        public double ListenerCalculatingStep { get; set; } = -10;

        /// <summary>索引＝聽者的手數，超過最後一格用最後一格。</summary>
        public double[] HopDeltas { get; set; } = { 0, 0, 0, -5, -10, -15 };

        public double MinChance { get; set; } = 5;
        public double MaxChance { get; set; } = 95;

        /// <summary>不信的人把這則消息往下傳時，傳話機率乘上這個倍數。</summary>
        public double DisbelieverTellMultiplier { get; set; } = 0.3;
        public double DeniedDelta { get; set; } = -25;
        public double ClarifiedDelta { get; set; } = -50;

        [JsonExtensionData]
        public IDictionary<string, JToken> Extra { get; set; } = new Dictionary<string, JToken>();
    }

    /// <summary>聽到消息的人好感改多少，在「量 × 手數折扣 × 旁人倍數」之後再乘的兩個倍數。倍數一律 0～10。</summary>
    public sealed class ReactionConfig
    {
        /// <summary>關掉＝兩個倍數都當 1，結果跟沒有這兩個倍數時一模一樣。</summary>
        public bool Enabled { get; set; } = true;

        /// <summary>索引＝聽者在那一項個性的等級 + 2，即 −2／−1／0／+1／+2。</summary>
        public double[] TraitMultipliers { get; set; } = { 0.25, 0.5, 1.0, 1.5, 2.0 };

        /// <summary>聽者跟承受的人同家族。</summary>
        public double ReceiverSameClan { get; set; } = 2.0;

        /// <summary>聽者對承受的人好感達到這個值（含）算朋友。</summary>
        public int    ReceiverFriendRelation { get; set; } = 30;
        public double ReceiverFriend         { get; set; } = 1.5;

        /// <summary>聽者對承受的人好感低到這個值（含）算敵對。</summary>
        public int    ReceiverHostileRelation { get; set; } = -20;
        public double ReceiverHostile         { get; set; } = 0.5;

        [JsonExtensionData]
        public IDictionary<string, JToken> Extra { get; set; } = new Dictionary<string, JToken>();
    }

    public sealed class DenialConfig
    {
        public double BaseChance { get; set; } = 50;
        public double ValorBonus { get; set; } = 25;
        public double HonorBonus { get; set; } = 25;
        public double CautiousPenalty { get; set; } = -30;
        public double AccusedGrudgeMultiplier { get; set; } = 1.5;

        [JsonExtensionData]
        public IDictionary<string, JToken> Extra { get; set; } = new Dictionary<string, JToken>();
    }

    public sealed class ClarifyConfig
    {
        public double BaseChance { get; set; } = 40;
        public double HonorBonus { get; set; } = 30;
        public int FriendRelation { get; set; } = 30;
        public double FriendBonus { get; set; } = 20;
        public int HostileRelation { get; set; } = -20;
        public double HostilePenalty { get; set; } = -40;

        [JsonExtensionData]
        public IDictionary<string, JToken> Extra { get; set; } = new Dictionary<string, JToken>();
    }

    public sealed class SlipConfig
    {
        public double BaseChance { get; set; } = 10;
        public double RashBonus { get; set; } = 20;
        public double CalculatingBonus { get; set; } = -10;

        [JsonExtensionData]
        public IDictionary<string, JToken> Extra { get; set; } = new Dictionary<string, JToken>();
    }

    public sealed class FalseRumorsConfig
    {
        /// <summary>關掉＝不產生編的話、不產生五個「做了不體面的事」的情境；已經存在的照常傳。</summary>
        public bool Enabled { get; set; } = true;

        /// <summary>關掉＝聽到就信、照量結算。</summary>
        public bool BeliefEnabled { get; set; } = true;
        public BeliefConfig Belief { get; set; } = new();
        public ReactionConfig Reaction { get; set; } = new();
        public DenialConfig Denial { get; set; } = new();
        public ClarifyConfig Clarify { get; set; } = new();
        public SlipConfig Slip { get; set; } = new();

        public double StepForwardMinChance { get; set; } = 5;
        public double StepForwardMaxChance { get; set; } = 95;
        public int StepForwardWaitDays { get; set; } = 30;

        /// <summary>背後說君主、同袍求援不肯伸手，各自每天的上限。</summary>
        public double MisconductPerDay { get; set; } = 0.15;

        /// <summary>三種起因合計，全世界每天的上限（小數是機率）。</summary>
        public double MaxPerDay { get; set; } = 0.8;

        /// <summary>惡意中傷的比重。</summary>
        public double SlanderMaxPerDay { get; set; } = 0.5;

        /// <summary>爭權中傷的比重。</summary>
        public double RivalryPerDay { get; set; } = 0.15;

        /// <summary>替自家人說好話的比重。</summary>
        public double PraisePerDay { get; set; } = 0.15;

        /// <summary>每次被俘時莽撞被俘、苛待俘虜各擲的機率。</summary>
        public double CaptureMisconductChance { get; set; } = 0.3;

        /// <summary>每次有人年老過世時有人下毒的機率。</summary>
        public double PoisonChance { get; set; } = 0.2;

        /// <summary>個人恩怨合計低於此值（含）算有怨。</summary>
        public int GrudgeLine { get; set; } = -5;

        /// <summary>遊戲好感低於此值（含）算有怨。</summary>
        public int NativeGrudgeLine { get; set; } = -20;

        /// <summary>個人恩怨合計低到這個值（含）才算下毒等級的怨。</summary>
        public int PoisonGrudgeLine { get; set; } = -10;

        /// <summary>遊戲好感低到這個值（含）才算下毒等級的怨。</summary>
        public int PoisonNativeGrudgeLine { get; set; } = -30;

        [JsonExtensionData]
        public IDictionary<string, JToken> Extra { get; set; } = new Dictionary<string, JToken>();
    }
}
