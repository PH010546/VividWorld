using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace VividWorld.Core.Situations
{
    public enum WandererRoleSwapStatus
    {
        Active,
        Reverted,
        Expired,
        PartyLeft,
        ReplacedByThirdParty
    }

    public sealed class WandererRoleSwapRecord
    {
        public string Role { get; set; } = string.Empty;
        public string From { get; set; } = string.Empty;
        public string To { get; set; } = string.Empty;
        public double Day { get; set; }
        public string Source { get; set; } = string.Empty; // "swap" | "contest"

        [JsonExtensionData]
        public IDictionary<string, JToken> Extra { get; set; } = new Dictionary<string, JToken>();
    }

    public sealed class WandererPendingComplaint
    {
        public string HeroId { get; set; } = string.Empty;
        public string Role { get; set; } = string.Empty;
        public double Day { get; set; }
        public int RelationBefore { get; set; }
        public int RelationAfter { get; set; }

        [JsonExtensionData]
        public IDictionary<string, JToken> Extra { get; set; } = new Dictionary<string, JToken>();
    }

    public sealed class WandererRoleData
    {
        public int Version { get; set; } = 1;

        public Dictionary<string, string> Yesterday { get; set; } = new(StringComparer.OrdinalIgnoreCase);

        public List<WandererRoleSwapRecord> Swaps { get; set; } = new();

        public WandererPendingComplaint? PendingComplaint { get; set; }

        [JsonExtensionData]
        public IDictionary<string, JToken> Extra { get; set; } = new Dictionary<string, JToken>();
    }

    public sealed class WandererRevertAction
    {
        public string Role { get; set; } = string.Empty;
        public string ReplacedHeroId { get; set; } = string.Empty; // 被拔掉的人（換成的那位）
        public string OriginalHeroId { get; set; } = string.Empty; // 原本那位
        public double SwapDay { get; set; }
        public double RevertDay { get; set; }
        public int RelationChange { get; set; } = -8;
        public double MoraleChange { get; set; } = -1.0;
    }

    public sealed class WandererRoleEvaluation
    {
        public WandererRoleSwapRecord Swap { get; set; } = null!;
        public WandererRoleSwapStatus Status { get; set; }
        public string? Reason { get; set; }
    }

    public sealed class WandererRoleCheckDayResult
    {
        public List<WandererRoleEvaluation> Evaluations { get; set; } = new();
        public List<WandererRevertAction> Reverts { get; set; } = new();
    }

    public sealed class WandererRoleRecords
    {
        private readonly WandererRoleData _data;

        public WandererRoleRecords(WandererRoleData? data = null)
        {
            _data = data ?? new WandererRoleData();
            _data.Yesterday ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            _data.Swaps ??= new List<WandererRoleSwapRecord>();
        }

        public WandererRoleData Data => _data;

        public IReadOnlyList<WandererRoleSwapRecord> Swaps => _data.Swaps;

        public IReadOnlyDictionary<string, string> Yesterday => _data.Yesterday;

        public WandererPendingComplaint? PendingComplaint
        {
            get => _data.PendingComplaint;
            set => _data.PendingComplaint = value;
        }

        /// <summary>記錄換人；一個職位只留最新一筆。</summary>
        public void RecordSwap(string role, string from, string to, double day, string source)
        {
            if (string.IsNullOrEmpty(role)) throw new ArgumentNullException(nameof(role));
            _data.Swaps.RemoveAll(s => string.Equals(s.Role, role, StringComparison.OrdinalIgnoreCase));
            _data.Swaps.Add(new WandererRoleSwapRecord
            {
                Role = role,
                From = from ?? string.Empty,
                To = to ?? string.Empty,
                Day = day,
                Source = source ?? string.Empty
            });
        }

        /// <summary>
        /// 每日檢查玩家隊伍的四個職位。
        /// 對每筆紀錄判斷狀態：進行中／換回去了／過期／有人不在隊伍裡／換成第三人。
        /// 非「進行中」的結清；最後把今天四個職位存成 yesterday。
        /// </summary>
        public WandererRoleCheckDayResult CheckDay(
            IReadOnlyDictionary<string, string?> todayRoles,
            double today,
            double roleRevertDays,
            Func<string, bool> isInPlayerParty)
        {
            todayRoles ??= new Dictionary<string, string?>();
            var result = new WandererRoleCheckDayResult();
            var keptSwaps = new List<WandererRoleSwapRecord>();

            foreach (var s in _data.Swaps)
            {
                todayRoles.TryGetValue(s.Role, out string? currentHolder);

                // 1. 有人不在隊伍裡（死、離隊、被俘）
                bool fromInParty = isInPlayerParty != null && isInPlayerParty(s.From);
                bool toInParty = isInPlayerParty != null && isInPlayerParty(s.To);
                if (!fromInParty || !toInParty)
                {
                    result.Evaluations.Add(new WandererRoleEvaluation
                    {
                        Swap = s,
                        Status = WandererRoleSwapStatus.PartyLeft,
                        Reason = !fromInParty && !toInParty
                            ? $"both {s.From} and {s.To} left party"
                            : !fromInParty ? $"{s.From} left party" : $"{s.To} left party"
                    });
                    continue; // 結清
                }

                // 2. 過期（超過 roleRevertDays）
                double elapsed = today - s.Day;
                if (elapsed > roleRevertDays)
                {
                    result.Evaluations.Add(new WandererRoleEvaluation
                    {
                        Swap = s,
                        Status = WandererRoleSwapStatus.Expired,
                        Reason = $"expired ({elapsed:0.##}d > {roleRevertDays:0.##}d)"
                    });
                    continue; // 結清
                }

                // 3. 換回去了（某職位今天的人是紀錄裡原本那個人，且 <= roleRevertDays）
                if (string.Equals(currentHolder, s.From, StringComparison.OrdinalIgnoreCase))
                {
                    result.Evaluations.Add(new WandererRoleEvaluation
                    {
                        Swap = s,
                        Status = WandererRoleSwapStatus.Reverted,
                        Reason = $"reverted to original holder {s.From}"
                    });
                    result.Reverts.Add(new WandererRevertAction
                    {
                        Role = s.Role,
                        ReplacedHeroId = s.To,
                        OriginalHeroId = s.From,
                        SwapDay = s.Day,
                        RevertDay = today,
                        RelationChange = -8,
                        MoraleChange = -1.0
                    });
                    continue; // 結清
                }

                // 4. 換成第三人（不是原本那位，也不是換成的那位）
                if (!string.Equals(currentHolder, s.To, StringComparison.OrdinalIgnoreCase))
                {
                    result.Evaluations.Add(new WandererRoleEvaluation
                    {
                        Swap = s,
                        Status = WandererRoleSwapStatus.ReplacedByThirdParty,
                        Reason = string.IsNullOrEmpty(currentHolder)
                            ? "role became vacant"
                            : $"replaced by third party {currentHolder}"
                    });
                    continue; // 結清
                }

                // 5. 進行中
                result.Evaluations.Add(new WandererRoleEvaluation
                {
                    Swap = s,
                    Status = WandererRoleSwapStatus.Active,
                    Reason = "active"
                });
                keptSwaps.Add(s);
            }

            _data.Swaps = keptSwaps;

            // 把今天四個職位存成 yesterday
            _data.Yesterday.Clear();
            foreach (var kv in todayRoles)
            {
                if (!string.IsNullOrEmpty(kv.Value))
                {
                    _data.Yesterday[kv.Key] = kv.Value!;
                }
            }

            return result;
        }
    }
}
