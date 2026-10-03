using System;
using System.Collections.Generic;
using System.Globalization;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;

namespace VividWorld.Patches
{
    /// <summary>
    /// 原版換族長時，新族長對每一位活著的人的個人好感＝她原本的值＋舊族長對那個人好感的 70%。
    /// 原版把家族共用的那個值轉給新族長，是因為它假設一個家族對玩家只有一個好感；
    /// 各人各算之後，新族長對玩家已經有她自己的值，再加舊族長的就是憑空多出來的好感。
    /// 這個補丁只管「玩家這一對」：換族長前記下這個家族每位成員對玩家的個人值，換完把新族長那一格寫回。
    /// 其他人照原版（D-115）。
    /// </summary>
    internal static class ClanLeaderChangeKeepsPlayerRelationPatch
    {
        internal static bool Applied { get; private set; }

        /// <summary>前置補丁留給後置補丁的東西：換之前的舊族長，以及每位成員對玩家的個人值。</summary>
        internal sealed class ChangeState
        {
            internal Hero? OldLeader;
            internal readonly Dictionary<Hero, int> BeforeValues = new Dictionary<Hero, int>();
        }

        internal static bool TryApply(Harmony harmony)
        {
            try
            {
                var target = AccessTools.Method(
                    typeof(ChangeClanLeaderAction),
                    "ApplyInternal",
                    new[] { typeof(Clan), typeof(Hero) });
                if (target == null)
                {
                    ModLog.Warn("Patch on clan leader change not applied: method not found.");
                    return false;
                }

                harmony.Patch(
                    target,
                    prefix: new HarmonyMethod(typeof(ClanLeaderChangeKeepsPlayerRelationPatch), nameof(Prefix)),
                    postfix: new HarmonyMethod(typeof(ClanLeaderChangeKeepsPlayerRelationPatch), nameof(Postfix)));
                Applied = true;
                return true;
            }
            catch (Exception ex)
            {
                Applied = false;
                ModLog.Error("Patch on clan leader change failed to apply; new clan leaders keep the game's own inheritance of relations.", ex);
                return false;
            }
        }

        private static void Prefix(Clan clan, ref ChangeState? __state)
        {
            __state = null;
            try
            {
                if (!SubModule.PersonalWithPlayerEnabled) return;

                var player = Hero.MainHero;
                if (player == null || clan == null) return;
                if (clan == player.Clan) return;

                var state = new ChangeState { OldLeader = clan.Leader };
                var members = clan.Heroes;
                if (members != null)
                {
                    for (int i = 0; i < members.Count; i++)
                    {
                        var member = members[i];
                        if (member == null || !member.IsAlive || member == player) continue;
                        state.BeforeValues[member] = CharacterRelationManager.GetHeroRelation(player, member);
                    }
                }

                __state = state;
            }
            catch (Exception ex)
            {
                __state = null;
                ModLog.Error("Clan leader change: could not record relations with the player before the change.", ex);
            }
        }

        private static void Postfix(Clan clan, ChangeState? __state)
        {
            try
            {
                if (__state == null) return;

                var player = Hero.MainHero;
                var newLeader = clan?.Leader;
                if (player == null || clan == null) return;

                string clanName = clan.Name?.ToString() ?? clan.StringId;
                string oldName = __state.OldLeader?.Name?.ToString() ?? "(none)";

                if (newLeader == null)
                {
                    ModLog.Info($"Clan leader change in {clanName}: {oldName} -> (none); nothing to keep.");
                    return;
                }

                string newName = newLeader.Name?.ToString() ?? newLeader.StringId;

                if (!__state.BeforeValues.TryGetValue(newLeader, out int kept))
                {
                    ModLog.Info($"Clan leader change in {clanName}: {oldName} -> {newName}; {newName} was not among the clan members recorded before the change, so the relation with the player was left as the game set it.");
                    return;
                }

                int gameMade = CharacterRelationManager.GetHeroRelation(player, newLeader);
                if (gameMade != kept)
                {
                    CharacterRelationManager.SetHeroRelation(player, newLeader, kept);
                }

                ModLog.Info(string.Format(CultureInfo.InvariantCulture,
                    "Clan leader change in {0}: {1} -> {2}; new leader's relation with the player kept at {3} (the game would have made it {4})",
                    clanName, oldName, newName, kept, gameMade));
            }
            catch (Exception ex)
            {
                ModLog.Error("Clan leader change: could not restore the new leader's relation with the player.", ex);
            }
        }
    }
}
