using System;
using System.Collections.Generic;
using System.Globalization;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using VividWorld.Core.Relations;
using VividWorld.Patches;
using GameCampaign = TaleWorlds.CampaignSystem.Campaign;

namespace VividWorld.Campaign
{
    /// <summary>一次搬數字檢查的結果。<see cref="NewMarker"/> 為 null 表示什麼都沒動，存檔標記維持原樣。</summary>
    internal sealed class PersonalSeedResult
    {
        internal SeedOutcome? Outcome { get; }
        internal string? NewMarker { get; }
        internal int SeededCount { get; }

        /// <summary>這一次真的把數字搬過了（不是跳過、也不是早就做過）。</summary>
        internal bool JustSeeded => Outcome == SeedOutcome.Seeded;

        internal PersonalSeedResult(SeedOutcome? outcome, string? newMarker, int seededCount)
        {
            Outcome = outcome;
            NewMarker = newMarker;
            SeededCount = seededCount;
        }

        internal static readonly PersonalSeedResult NothingDone = new PersonalSeedResult(null, null, 0);
    }

    /// <summary>
    /// 第一次打開「跟玩家的好感各人各算」時，把每位別家非族長對玩家的個人值設成他族長的值，
    /// 讓畫面上的數字在切換前後一樣。規則在 Core 的 <see cref="PersonalRelationSeeding"/>，這裡只負責讀寫遊戲。
    /// </summary>
    internal static class PersonalRelationSeeder
    {
        /// <summary>開機狀態一行：兩個補丁掛上沒有、實際的外交模型型別、別的模組掛在同一個方法上的補丁。</summary>
        internal static void LogBootStatus()
        {
            try
            {
                string modelType = GameCampaign.Current?.Models?.DiplomacyModel?.GetType().FullName ?? "(unavailable)";
                ModLog.Info(
                    "Personal relations with the player: patch on GetHeroesForEffectiveRelation applied="
                    + PlayerPairEffectiveHeroesPatch.Applied
                    + ", patch on clan leader change applied=" + ClanLeaderChangeKeepsPlayerRelationPatch.Applied
                    + ", diplomacy model " + modelType
                    + ", other patches on GetHeroesForEffectiveRelation: " + OtherPatchOwners());
            }
            catch (Exception ex)
            {
                ModLog.Error("Personal relations with the player: could not report the patch status.", ex);
            }
        }

        private static string OtherPatchOwners()
        {
            var target = AccessTools.Method(
                typeof(TaleWorlds.CampaignSystem.GameComponents.DefaultDiplomacyModel),
                "GetHeroesForEffectiveRelation",
                new[] { typeof(Hero), typeof(Hero), typeof(Hero).MakeByRefType(), typeof(Hero).MakeByRefType() });
            if (target == null) return "none";

            var info = Harmony.GetPatchInfo(target);
            if (info == null) return "none";

            var owners = new SortedSet<string>(StringComparer.Ordinal);
            foreach (var p in info.Prefixes) owners.Add(p.owner + " (prefix)");
            foreach (var p in info.Postfixes) owners.Add(p.owner + " (postfix)");
            foreach (var p in info.Transpilers) owners.Add(p.owner + " (transpiler)");
            foreach (var p in info.Finalizers) owners.Add(p.owner + " (finalizer)");
            owners.Remove(SubModule.HarmonyId + " (prefix)");

            return owners.Count == 0 ? "none" : string.Join(", ", owners);
        }

        /// <summary>
        /// 檢查並視情況搬數字。<paramref name="marker"/> 是存檔裡現在的標記。
        /// 開關關著、標記已設、補丁沒掛上，都什麼也不改。
        /// </summary>
        internal static PersonalSeedResult Run(bool switchOn, string? marker)
        {
            var gate = PersonalRelationSeeding.Gate(switchOn, marker);
            if (gate == SeedGate.SwitchOff)
            {
                ModLog.Info(PersonalRelationSeeding.FormatSwitchOffNoop());
                return PersonalSeedResult.NothingDone;
            }
            if (gate == SeedGate.AlreadyDone)
            {
                ModLog.Info(PersonalRelationSeeding.FormatAlreadyDone(marker ?? string.Empty));
                return PersonalSeedResult.NothingDone;
            }

            var player = Hero.MainHero;
            if (player == null || GameCampaign.Current == null)
            {
                ModLog.Info("Personal relations with the player: not checked - the campaign is not ready.");
                return PersonalSeedResult.NothingDone;
            }

            if (!PlayerPairEffectiveHeroesPatch.Applied)
            {
                // 補丁沒掛上時畫面還是讀族長的值，搬了也看不出差別；標記不設，下次載入再檢查。
                ModLog.Warn("Personal relations with the player: not seeded - the patch on GetHeroesForEffectiveRelation is not applied.");
                return PersonalSeedResult.NothingDone;
            }

            int day = (int)CampaignTime.Now.ToDays;
            string playerClanId = player.Clan?.StringId ?? string.Empty;

            var candidates = new List<SeedCandidate>();
            var heroById = new Dictionary<string, Hero>(StringComparer.Ordinal);
            var leaderById = new Dictionary<string, Hero>(StringComparer.Ordinal);
            foreach (var hero in Hero.AllAliveHeroes)
            {
                if (hero == null) continue;
                var clan = hero.Clan;
                var leader = clan?.Leader;
                candidates.Add(new SeedCandidate(
                    hero.StringId,
                    hero.Name?.ToString() ?? hero.StringId,
                    clan?.StringId,
                    leader?.StringId,
                    hero.IsAlive,
                    hero == player));
                heroById[hero.StringId] = hero;
                if (leader != null) leaderById[hero.StringId] = leader;
            }

            var targets = PersonalRelationSeeding.SelectTargets(candidates, string.IsNullOrEmpty(playerClanId) ? null : playerClanId);
            if (targets.Count == 0)
            {
                string noneMarker = PersonalRelationSeeding.MakeMarker(SeedOutcome.SkippedNoCandidates, day);
                ModLog.Info(PersonalRelationSeeding.FormatSkippedNoCandidates());
                return new PersonalSeedResult(SeedOutcome.SkippedNoCandidates, noneMarker, 0);
            }

            // 偵測：讓自己的補丁暫時不作用，問「玩家與這個人」會被換成誰。
            var probe = targets[0];
            Hero? effective1 = null;
            Hero? effective2 = null;
            PlayerPairEffectiveHeroesPatch.Bypass = true;
            try
            {
                GameCampaign.Current.Models.DiplomacyModel.GetHeroesForEffectiveRelation(player, heroById[probe.Id], out effective1, out effective2);
            }
            finally
            {
                PlayerPairEffectiveHeroesPatch.Bypass = false;
            }

            if (PersonalRelationSeeding.OtherModAlreadySeparates(probe.Id, effective2?.StringId))
            {
                string otherMarker = PersonalRelationSeeding.MakeMarker(SeedOutcome.SkippedOtherMod, day);
                ModLog.Info(PersonalRelationSeeding.FormatSkippedOtherMod()
                    + string.Format(CultureInfo.InvariantCulture, " (probe {0}: effective pair {1} / {2})",
                        probe.Id, effective1?.StringId ?? "null", effective2?.StringId ?? "null"));
                return new PersonalSeedResult(SeedOutcome.SkippedOtherMod, otherMarker, 0);
            }

            var changes = new List<SeedChange>(targets.Count);
            foreach (var target in targets)
            {
                var hero = heroById[target.Id];
                var leader = leaderById[target.Id];
                int old = CharacterRelationManager.GetHeroRelation(player, hero);
                int fromLeader = CharacterRelationManager.GetHeroRelation(player, leader);
                if (old != fromLeader)
                {
                    CharacterRelationManager.SetHeroRelation(player, hero, fromLeader);
                }
                changes.Add(new SeedChange(target.Id, target.Name, old, fromLeader, leader.Name?.ToString() ?? leader.StringId));
            }

            string seededMarker = PersonalRelationSeeding.MakeMarker(SeedOutcome.Seeded, day);
            ModLog.Info(PersonalRelationSeeding.FormatSeeded(changes.Count));
            foreach (var line in PersonalRelationSeeding.FormatOverwrittenLines(changes))
            {
                ModLog.Info(line);
            }

            return new PersonalSeedResult(SeedOutcome.Seeded, seededMarker, changes.Count);
        }
    }
}
