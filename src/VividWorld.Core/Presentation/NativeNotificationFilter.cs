#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;

namespace VividWorld.Core.Presentation
{
    /// <summary>
    /// 原版通知參與者描述。
    /// </summary>
    public sealed class NativeNotificationParticipant
    {
        public string Name { get; }
        public string Id { get; }
        public bool IsPlayerOrPlayerClan { get; }

        public NativeNotificationParticipant(string name, string id, bool isPlayerOrPlayerClan)
        {
            Name = name ?? string.Empty;
            Id = id ?? string.Empty;
            IsPlayerOrPlayerClan = isPlayerOrPlayerClan;
        }
    }

    /// <summary>
    /// 原版通知過濾判定結果。
    /// </summary>
    public sealed class NativeNotificationFilterResult
    {
        public bool ShouldMute { get; }
        public string Reason { get; }
        public string? LogLine { get; }

        public NativeNotificationFilterResult(bool shouldMute, string reason, string? logLine)
        {
            ShouldMute = shouldMute;
            Reason = reason ?? string.Empty;
            LogLine = logLine;
        }
    }

    /// <summary>
    /// 原版通知攔截過濾器。
    /// 世界上發生的事若有傳聞網路覆蓋，原版左下角廣播預設靜音改由口耳相傳，但牽涉玩家家族的依然跳出。
    /// </summary>
    public static class NativeNotificationFilter
    {
        /// <summary>
        /// 攔截名單中的原版通知型別。往後擴充型別只在此集合維護。
        /// </summary>
        public static readonly IReadOnlyCollection<string> TargetLogTypes = new HashSet<string>(StringComparer.Ordinal)
        {
            "TakePrisonerLogEntry",
            "EndCaptivityLogEntry",
            "CharacterKilledLogEntry",
            "CharacterMarriedLogEntry",
            "CharacterBornLogEntry",
            "ChildbirthLogEntry",
            "TournamentWonLogEntry"
        };

        /// <summary>
        /// 評估指定紀錄是否應該攔截或放行。
        /// 順序：不在名單 ⇒ 放行（不印）；開關關著 ⇒ 放行；家族欄是玩家家族 ⇒ 放行；任一參與者是玩家或玩家家族 ⇒ 放行；其餘 ⇒ 攔。
        /// </summary>
        public static NativeNotificationFilterResult Evaluate(
            string logTypeName,
            bool enabled,
            IEnumerable<NativeNotificationParticipant?>? participants,
            bool isVictimClanPlayerClan = false)
        {
            if (string.IsNullOrEmpty(logTypeName) || !TargetLogTypes.Contains(logTypeName))
            {
                return new NativeNotificationFilterResult(false, "not covered", null);
            }

            if (!enabled)
            {
                const string reason = "switch off";
                return new NativeNotificationFilterResult(false, reason, $"native notification shown: {logTypeName}, {reason}");
            }

            if (isVictimClanPlayerClan)
            {
                const string reason = "victim clan is the player's clan";
                return new NativeNotificationFilterResult(false, reason, $"native notification shown: {logTypeName}, {reason}");
            }

            var validParticipants = new List<NativeNotificationParticipant>();
            if (participants != null)
            {
                foreach (var p in participants)
                {
                    if (p != null)
                    {
                        validParticipants.Add(p);
                    }
                }
            }

            foreach (var p in validParticipants)
            {
                if (p.IsPlayerOrPlayerClan)
                {
                    string reason = $"{p.Name} ({p.Id}) is the player or in the player's clan";
                    return new NativeNotificationFilterResult(false, reason, $"native notification shown: {logTypeName}, {reason}");
                }
            }

            string formattedParticipants = string.Join(", ", validParticipants.Select(p => $"{p.Name} ({p.Id})"));
            string muteLog = $"native notification muted: {logTypeName} ({formattedParticipants})";
            return new NativeNotificationFilterResult(true, "muted", muteLog);
        }
    }
}
