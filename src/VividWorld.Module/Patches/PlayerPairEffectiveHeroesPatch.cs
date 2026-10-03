using System;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.GameComponents;

namespace VividWorld.Patches
{
    /// <summary>
    /// 原版讀、寫兩個人的好感之前，會先把各自換成家族的族長，所以別家非族長對玩家的好感其實是他族長的。
    /// 這個補丁讓「牽涉玩家的一對」不換：開關開著時，玩家與任何人的好感都落在那個人本人。
    /// 兩個人都不是玩家的一對一律照原版，NPC 之間的好感不受影響。
    /// 開關每次呼叫都現讀，設定選單切換立即生效（D-57、D-114、D-115）。
    /// </summary>
    internal static class PlayerPairEffectiveHeroesPatch
    {
        internal static bool Applied { get; private set; }

        /// <summary>
        /// 暫時讓補丁不作用，只給搬數字前的偵測用：要問的是「沒有我們的補丁時，這一對會被換成誰」。
        /// 用 try/finally 設定，離開時一定關回來。
        /// </summary>
        [ThreadStatic]
        internal static bool Bypass;

        internal static bool TryApply(Harmony harmony)
        {
            try
            {
                var target = AccessTools.Method(
                    typeof(DefaultDiplomacyModel),
                    "GetHeroesForEffectiveRelation",
                    new[] { typeof(Hero), typeof(Hero), typeof(Hero).MakeByRefType(), typeof(Hero).MakeByRefType() });
                if (target == null)
                {
                    ModLog.Warn("Patch on GetHeroesForEffectiveRelation not applied: method not found.");
                    return false;
                }

                harmony.Patch(target, prefix: new HarmonyMethod(typeof(PlayerPairEffectiveHeroesPatch), nameof(Prefix)));
                Applied = true;
                return true;
            }
            catch (Exception ex)
            {
                Applied = false;
                ModLog.Error("Patch on GetHeroesForEffectiveRelation failed to apply; relations with the player stay as the game computes them.", ex);
                return false;
            }
        }

        // 回 false 就跳過原版。出參數用 ref 接，這樣回 true 時不必替它們指定值。
        private static bool Prefix(Hero hero1, Hero hero2, ref Hero effectiveHero1, ref Hero effectiveHero2)
        {
            try
            {
                if (Bypass) return true;
                if (!SubModule.PersonalWithPlayerEnabled) return true;

                var player = Hero.MainHero;
                if (player == null) return true;
                if (hero1 != player && hero2 != player) return true;

                effectiveHero1 = hero1;
                effectiveHero2 = hero2;
                return false;
            }
            catch
            {
                // 補丁出錯不准拖垮原版的好感計算：這個方法一幀會被叫很多次，這裡不印日誌。
                return true;
            }
        }
    }
}
