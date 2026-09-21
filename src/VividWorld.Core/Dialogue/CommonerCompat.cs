#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using VividWorld.Core.Config;

namespace VividWorld.Core.Dialogue
{
    /// <summary>
    /// 「平民不該跟貴族攀談」相容模式的解析結果（規格 §9.2.1，帳本 D-43／X-17／X-18／L-22）。
    /// </summary>
    public sealed class CommonerCompatState
    {
        public bool Active { get; set; }

        /// <summary>解析出這個結果的理由，直接進日誌。</summary>
        public string Reason { get; set; } = "";

        /// <summary>觸發相容模式的組件名（auto 模式下才會有東西）。</summary>
        public List<string> DetectedModules { get; set; } = new List<string>();

        /// <summary>NPC 主動行實際要用的優先權。</summary>
        public int NpcLinePriority { get; set; }

        /// <summary>主動講的氏族 Tier 閘。未啟用時為 0。</summary>
        public int VolunteerMinClanTier { get; set; }

        /// <summary>玩家詢問的氏族 Tier 閘。未啟用時為 0。</summary>
        public int AskMinClanTier { get; set; }
    }

    public static class CommonerCompat
    {
        /// <summary>
        /// 決定要不要開相容模式。`loadedAssemblyNames` 由 Module 側餵進來
        /// （執行期真正載進來的組件名，不是 Modules\ 資料夾裡有什麼——資料夾在、沒勾選是常見狀況）。
        /// </summary>
        public static CommonerCompatState Resolve(DialogueConfig config, IEnumerable<string>? loadedAssemblyNames)
        {
            if (config == null) throw new ArgumentNullException(nameof(config));

            var state = new CommonerCompatState
            {
                NpcLinePriority = config.NpcLinePriority,
                VolunteerMinClanTier = 0,
                AskMinClanTier = 0
            };

            string mode = (config.CommonerCompatMode ?? "auto").Trim().ToLowerInvariant();
            var wanted = config.CommonerCompatModules ?? new List<string>();

            var loaded = new HashSet<string>(
                (loadedAssemblyNames ?? Enumerable.Empty<string>()).Where(n => !string.IsNullOrEmpty(n)),
                StringComparer.OrdinalIgnoreCase);

            var detected = wanted.Where(w => !string.IsNullOrEmpty(w) && loaded.Contains(w)).ToList();
            state.DetectedModules = detected;

            switch (mode)
            {
                case "off":
                    state.Reason = "off (forced by config)";
                    return state;

                case "on":
                    Activate(state, config);
                    state.Reason = "on (forced by config)";
                    return state;

                default:
                    if (detected.Count > 0)
                    {
                        Activate(state, config);
                        state.Reason = $"auto - detected {string.Join(", ", detected)}";
                    }
                    else
                    {
                        string wantedStr = wanted.Count > 0 ? string.Join(", ", wanted) : "(none configured)";
                        state.Reason = $"auto - none of [{wantedStr}] loaded, vanilla behaviour";
                    }
                    return state;
            }
        }

        private static void Activate(CommonerCompatState state, DialogueConfig config)
        {
            state.Active = true;
            state.NpcLinePriority = config.CommonerCompatLinePriority;
            state.VolunteerMinClanTier = config.CommonerCompatVolunteerMinClanTier;
            state.AskMinClanTier = config.CommonerCompatAskMinClanTier;
        }

        /// <summary>
        /// 玩家開口問領主「路上有什麼消息嗎？」＝ 平民跑去跟貴族攀談，正是相容模組在意的事。
        /// 預設 Tier 0 就擋掉（與 NaN 拒絕談外交、拒絕發任務同一層）。
        /// </summary>
        public static bool BlocksAsk(CommonerCompatState? state, int playerClanTier)
        {
            if (state == null || !state.Active) return false;
            return playerClanTier < state.AskMinClanTier;
        }

        /// <summary>
        /// 領主自己開口講八卦不是攀談——而且這條路徑本來就要求好感 ≥ 30（比相容模組任何一條都嚴，
        /// 它們根本不看好感）。預設不擋，所以低出身的玩法會變成「問不到消息，只能等看得起你的人主動說」。
        /// </summary>
        public static bool BlocksVolunteer(CommonerCompatState? state, int playerClanTier)
        {
            if (state == null || !state.Active) return false;
            return playerClanTier < state.VolunteerMinClanTier;
        }

        /// <summary>診斷行（英文、不在地化，符合規格 §9.5.6）。</summary>
        public static string FormatBlocked(string path, string heroName, string heroId, int playerClanTier, int minTier, CommonerCompatState state)
        {
            string tierStr = playerClanTier < 0 ? "none" : playerClanTier.ToString();
            return $"{path} {heroName} ({heroId}): blocked - commoner compat is on ({state.Reason}), " +
                   $"player clan tier {tierStr} < {minTier}";
        }

        /// <summary>註冊時印一次，說清楚這一場用的是哪一套。</summary>
        public static string FormatRegistration(CommonerCompatState state, string inputToken, int vanillaPriority)
        {
            if (!state.Active)
            {
                return $"Commoner compat: OFF [{state.Reason}] - volunteer line stays on '{inputToken}' at priority {vanillaPriority}, no clan-tier gate.";
            }

            return $"Commoner compat: ON [{state.Reason}] - volunteer line on '{inputToken}' raised {vanillaPriority} -> {state.NpcLinePriority} " +
                   $"(must outrank NaN's nan_peasant_greeting at 150, ledger X-17). " +
                   $"Clan-tier gates: asking needs tier >= {state.AskMinClanTier}, volunteering needs tier >= {state.VolunteerMinClanTier} " +
                   "(a lord speaking up is not the player accosting him, and that path already needs relation >= 30).";
        }
    }
}
