#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.LogEntries;
using VividWorld.Core.Presentation;

namespace VividWorld.Patches
{
    /// <summary>
    /// 原版紀錄通知跳出前置攔截補丁。
    /// 在原版通知呼叫 NewLogEntryAdded 前，對已有傳聞覆蓋的紀錄型別靜音，避免左下角提早廣播。
    /// </summary>
    internal static class NativeNotificationMutePatch
    {
        internal static bool Applied { get; private set; }

        private static readonly object Lock = new object();
        private static readonly Dictionary<string, int> MutedCounts = new Dictionary<string, int>(StringComparer.Ordinal);
        private static readonly Dictionary<string, int> ShownCounts = new Dictionary<string, int>(StringComparer.Ordinal);

        static NativeNotificationMutePatch()
        {
            foreach (var typeName in NativeNotificationFilter.TargetLogTypes)
            {
                MutedCounts[typeName] = 0;
                ShownCounts[typeName] = 0;
            }
        }

        internal static bool TryApply(Harmony harmony)
        {
            try
            {
                var target = AccessTools.Method(
                    typeof(CampaignInformationManager),
                    "NewLogEntryAdded",
                    new[] { typeof(LogEntry) });

                if (target == null)
                {
                    ModLog.Warn("Patch on native notifications not applied: method not found.");
                    return false;
                }

                harmony.Patch(
                    target,
                    prefix: new HarmonyMethod(typeof(NativeNotificationMutePatch), nameof(Prefix)));

                Applied = true;
                ModLog.Info("Native notification muting patch applied to CampaignInformationManager.NewLogEntryAdded.");
                return true;
            }
            catch (Exception ex)
            {
                Applied = false;
                ModLog.Error("Patch on native notifications failed to apply; notifications will pop up natively.", ex);
                return false;
            }
        }

        private static bool Prefix(LogEntry log)
        {
            try
            {
                if (log == null) return true;

                string logTypeName = log.GetType().Name;
                bool isTargetType = NativeNotificationFilter.TargetLogTypes.Contains(logTypeName);

                // 不在名單的種類直接放行且不印，不浪費效能
                if (!isTargetType) return true;

                bool enabled = SubModule.MuteNativeNotificationsEnabled;
                var rawParticipants = new List<Hero?>();
                bool isVictimClanPlayerClan = false;

                if (log is TakePrisonerLogEntry prisonerEntry)
                {
                    rawParticipants.Add(prisonerEntry.Prisoner);
                    rawParticipants.Add(prisonerEntry.CapturerHero);
                    rawParticipants.Add(prisonerEntry.CapturerMobilePartyLeader);
                }
                else if (log is EndCaptivityLogEntry endCaptivityEntry)
                {
                    rawParticipants.Add(endCaptivityEntry.Prisoner);
                }
                else if (log is CharacterKilledLogEntry killedEntry)
                {
                    rawParticipants.Add(killedEntry.Victim);
                    rawParticipants.Add(killedEntry.Killer);
                    isVictimClanPlayerClan = killedEntry.VictimClan != null &&
                                             Clan.PlayerClan != null &&
                                             killedEntry.VictimClan == Clan.PlayerClan;
                }
                else if (log is CharacterMarriedLogEntry marriedEntry)
                {
                    rawParticipants.Add(marriedEntry.MarriedHero);
                    rawParticipants.Add(marriedEntry.MarriedTo);
                }
                else if (log is CharacterBornLogEntry bornEntry)
                {
                    rawParticipants.Add(bornEntry.BornCharacter);
                }
                else if (log is ChildbirthLogEntry childbirthEntry)
                {
                    rawParticipants.Add(childbirthEntry.Mother);
                    rawParticipants.Add(childbirthEntry.NewbornHero);
                    rawParticipants.Add(childbirthEntry.NewLogTwin);
                }
                else if (log is TournamentWonLogEntry tournamentWonEntry)
                {
                    rawParticipants.Add(tournamentWonEntry.Winner);
                }

                var participants = new List<NativeNotificationParticipant>();
                foreach (var hero in rawParticipants)
                {
                    if (hero != null)
                    {
                        bool isPlayerOrClan = (Hero.MainHero != null && hero == Hero.MainHero) ||
                                              (hero.Clan != null && Clan.PlayerClan != null && hero.Clan == Clan.PlayerClan);
                        string name = hero.Name?.ToString() ?? hero.StringId ?? string.Empty;
                        string id = hero.StringId ?? string.Empty;
                        participants.Add(new NativeNotificationParticipant(name, id, isPlayerOrClan));
                    }
                }

                var result = NativeNotificationFilter.Evaluate(
                    logTypeName,
                    enabled,
                    participants,
                    isVictimClanPlayerClan);

                lock (Lock)
                {
                    if (result.ShouldMute)
                    {
                        if (MutedCounts.ContainsKey(logTypeName))
                        {
                            MutedCounts[logTypeName]++;
                        }
                    }
                    else
                    {
                        if (ShownCounts.ContainsKey(logTypeName))
                        {
                            ShownCounts[logTypeName]++;
                        }
                    }
                }

                if (!string.IsNullOrEmpty(result.LogLine))
                {
                    ModLog.Info(result.LogLine!);
                }

                return !result.ShouldMute;
            }
            catch (Exception ex)
            {
                ModLog.Error("Native notification filter failed; falling back to showing the notification.", ex);
                return true;
            }
        }

        internal static void GetStats(out int totalMuted, out int totalShown, out Dictionary<string, (int Muted, int Shown)> perType)
        {
            totalMuted = 0;
            totalShown = 0;
            perType = new Dictionary<string, (int Muted, int Shown)>(StringComparer.Ordinal);

            lock (Lock)
            {
                foreach (var typeName in NativeNotificationFilter.TargetLogTypes)
                {
                    int muted = MutedCounts.TryGetValue(typeName, out var m) ? m : 0;
                    int shown = ShownCounts.TryGetValue(typeName, out var s) ? s : 0;
                    perType[typeName] = (muted, shown);
                    totalMuted += muted;
                    totalShown += shown;
                }
            }
        }

        internal static string FormatDetailedStats()
        {
            GetStats(out int totalMuted, out int totalShown, out var perType);
            var sb = new StringBuilder();
            sb.AppendLine($"Total: {totalMuted} muted, {totalShown} shown.");
            foreach (var kvp in perType)
            {
                sb.AppendLine($"  {kvp.Key}: {kvp.Value.Muted} muted, {kvp.Value.Shown} shown");
            }
            return sb.ToString().TrimEnd();
        }
    }
}
