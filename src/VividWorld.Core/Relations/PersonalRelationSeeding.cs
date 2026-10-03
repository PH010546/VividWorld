using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace VividWorld.Core.Relations
{
    /// <summary>第一次打開「跟玩家的好感各人各算」時，搬數字這一步的結果。</summary>
    public enum SeedOutcome
    {
        /// <summary>搬了。</summary>
        Seeded,
        /// <summary>別的模組已經讓跟玩家有關的一對不換族長，不搬。</summary>
        SkippedOtherMod,
        /// <summary>找不到任何別家的非族長，沒有東西可搬。</summary>
        SkippedNoCandidates
    }

    /// <summary>決定要不要動手搬之前的兩道閘。</summary>
    public enum SeedGate
    {
        SwitchOff,
        AlreadyDone,
        Proceed
    }

    /// <summary>搬數字的規則只需要的一位人物資料；Module 負責從遊戲物件填。</summary>
    public sealed class SeedCandidate
    {
        public string Id { get; }
        public string Name { get; }
        public string? ClanId { get; }
        public string? ClanLeaderId { get; }
        public bool IsAlive { get; }
        public bool IsPlayer { get; }

        public SeedCandidate(string id, string name, string? clanId, string? clanLeaderId, bool isAlive, bool isPlayer)
        {
            Id = id;
            Name = name;
            ClanId = clanId;
            ClanLeaderId = clanLeaderId;
            IsAlive = isAlive;
            IsPlayer = isPlayer;
        }
    }

    /// <summary>某一位被搬過的人：原本的個人值與搬完的值，給日誌用。</summary>
    public sealed class SeedChange
    {
        public string Id { get; }
        public string Name { get; }
        public int OldValue { get; }
        public int NewValue { get; }
        public string LeaderName { get; }

        public SeedChange(string id, string name, int oldValue, int newValue, string leaderName)
        {
            Id = id;
            Name = name;
            OldValue = oldValue;
            NewValue = newValue;
            LeaderName = leaderName;
        }
    }

    /// <summary>
    /// 原版把「玩家與別家非族長」的好感讀寫成「玩家與他的族長」，所以畫面上那個人的數字其實是族長的。
    /// 開關第一次打開時，要把那個人本人的個人值設成畫面上本來看到的數字，之後各算各的。
    /// 這裡只放純規則：搬誰、偵測結果怎麼分、存檔標記怎麼組與解析、日誌怎麼寫。
    /// 讀寫遊戲好感的動作在 Module。
    /// </summary>
    public static class PersonalRelationSeeding
    {
        /// <summary>日誌最多逐行列出幾位被覆寫的人，其餘只印數量。</summary>
        public const int MaxChangeLines = 30;

        private const string LogPrefix = "Personal relations with the player: ";
        private const string SeededKind = "seeded";
        private const string OtherModKind = "skipped-other-mod";
        private const string NoCandidatesKind = "skipped-no-candidates";

        /// <summary>
        /// 要搬的人：活著、有家族、不在玩家的家族、家族有族長、本人不是族長、不是玩家本人。
        /// 族長本人不搬，因為他的個人值本來就是畫面上那一格；沒有家族的人原版不換族長，也不用搬。
        /// </summary>
        public static IReadOnlyList<SeedCandidate> SelectTargets(IEnumerable<SeedCandidate> heroes, string? playerClanId)
        {
            var result = new List<SeedCandidate>();
            if (heroes == null) return result;

            foreach (var h in heroes)
            {
                if (h == null) continue;
                if (!h.IsAlive || h.IsPlayer) continue;
                if (string.IsNullOrEmpty(h.ClanId)) continue;
                if (playerClanId != null && string.Equals(h.ClanId, playerClanId, StringComparison.Ordinal)) continue;
                if (string.IsNullOrEmpty(h.ClanLeaderId)) continue;
                if (string.Equals(h.ClanLeaderId, h.Id, StringComparison.Ordinal)) continue;
                result.Add(h);
            }

            return result;
        }

        /// <summary>開關關著、或標記已經設過，就不用再做。標記非空一律當作做過（包含認不得的格式）。</summary>
        public static SeedGate Gate(bool switchOn, string? marker)
        {
            if (!switchOn) return SeedGate.SwitchOff;
            if (!string.IsNullOrEmpty(marker)) return SeedGate.AlreadyDone;
            return SeedGate.Proceed;
        }

        /// <summary>
        /// 偵測：拿玩家與一位別家非族長問「要換成誰來算」，回來的第二個人就是他本人，
        /// 代表有別的模組已經讓這一對不換族長。
        /// </summary>
        public static bool OtherModAlreadySeparates(string probeId, string? effectiveSecondId)
        {
            return !string.IsNullOrEmpty(effectiveSecondId)
                && string.Equals(probeId, effectiveSecondId, StringComparison.Ordinal);
        }

        public static string MakeMarker(SeedOutcome outcome, int day)
        {
            return KindOf(outcome) + ":" + day.ToString(CultureInfo.InvariantCulture);
        }

        public static bool TryParseMarker(string? marker, out SeedOutcome outcome, out int day)
        {
            outcome = SeedOutcome.Seeded;
            day = 0;
            if (string.IsNullOrEmpty(marker)) return false;

            int colon = marker!.IndexOf(':');
            if (colon <= 0 || colon == marker.Length - 1) return false;

            string kind = marker.Substring(0, colon);
            if (!int.TryParse(marker.Substring(colon + 1), NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsedDay)) return false;

            switch (kind)
            {
                case SeededKind: outcome = SeedOutcome.Seeded; break;
                case OtherModKind: outcome = SeedOutcome.SkippedOtherMod; break;
                case NoCandidatesKind: outcome = SeedOutcome.SkippedNoCandidates; break;
                default: return false;
            }

            day = parsedDay;
            return true;
        }

        public static string FormatSwitchOffNoop() => LogPrefix + "switch is off";

        public static string FormatAlreadyDone(string marker) => LogPrefix + "already done (" + marker + ")";

        public static string FormatSeeded(int count) =>
            LogPrefix + "seeded " + count.ToString(CultureInfo.InvariantCulture) + " heroes";

        public static string FormatSkippedOtherMod() =>
            LogPrefix + "skipped - another mod already keeps player pairs per person";

        public static string FormatSkippedNoCandidates() =>
            LogPrefix + "skipped - no other-clan non-leaders";

        public static string FormatSwitched(bool on) =>
            LogPrefix + "switched " + (on ? "on" : "off") + " in settings";

        /// <summary>
        /// 被覆寫掉非 0 原值的人，一人一行，最多 <see cref="MaxChangeLines"/> 行，其餘印數量。
        /// 原本是 0 的人是常態，不列。
        /// </summary>
        public static IReadOnlyList<string> FormatOverwrittenLines(IEnumerable<SeedChange> changes)
        {
            var lines = new List<string>();
            if (changes == null) return lines;

            var overwritten = changes.Where(c => c.OldValue != 0).ToList();
            for (int i = 0; i < overwritten.Count && i < MaxChangeLines; i++)
            {
                var c = overwritten[i];
                lines.Add(string.Format(CultureInfo.InvariantCulture,
                    "  {0} ({1}): personal {2} -> {3} (clan leader {4} {3})",
                    c.Name, c.Id, c.OldValue, c.NewValue, c.LeaderName));
            }

            if (overwritten.Count > MaxChangeLines)
            {
                lines.Add("  ... and " + (overwritten.Count - MaxChangeLines).ToString(CultureInfo.InvariantCulture) + " more");
            }

            return lines;
        }

        private static string KindOf(SeedOutcome outcome)
        {
            switch (outcome)
            {
                case SeedOutcome.Seeded: return SeededKind;
                case SeedOutcome.SkippedOtherMod: return OtherModKind;
                default: return NoCandidatesKind;
            }
        }
    }
}
