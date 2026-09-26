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

        /// <summary>傳聞模式解析結果（LISTEN1d）。</summary>
        public RumorModeResult? RumorModeResult { get; set; }

        /// <summary>實際生效的傳聞模式（暢玩／寫實）。</summary>
        public RumorMode RumorMode => RumorModeResult?.Mode ?? RumorMode.Casual;

        /// <summary>算出 <see cref="RumorModeResult"/> 時依據的 `dialogue.volunteerMode` 字串（LISTEN1e）。
        /// 設定改了而這裡還是舊的 ⇒ 要重算，見 <see cref="CommonerCompat.IsRumorModeStale"/>。</summary>
        public string? RumorModeConfiguredAs { get; set; }

        /// <summary>NPC 主動行實際要用的優先權。</summary>
        public int NpcLinePriority { get; set; }

        /// <summary>主動講的氏族 Tier 閘。未啟用時為 0。</summary>
        public int VolunteerMinClanTier { get; set; }

        /// <summary>玩家詢問的氏族 Tier 閘。未啟用時為 0。</summary>
        public int AskMinClanTier { get; set; }
    }

    public static class CommonerCompat
    {
        /// <summary>程式內建的平民身分模組名單（組件名，不含 .dll）。</summary>
        public static readonly IReadOnlyList<string> BuiltInCommonerModules = new[] { "NaN", "Lowborn" };

        /// <summary>
        /// 取得內建名單與玩家自訂名單的聯集（不分大小寫去重）。
        /// 規格 §12.4、卡片 LISTEN1d §14(3)。
        /// </summary>
        public static List<string> GetEffectiveCommonerModules(IEnumerable<string>? configuredModules)
        {
            var list = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var mod in BuiltInCommonerModules)
            {
                if (!string.IsNullOrWhiteSpace(mod) && seen.Add(mod.Trim()))
                {
                    list.Add(mod.Trim());
                }
            }
            if (configuredModules != null)
            {
                foreach (var mod in configuredModules)
                {
                    if (!string.IsNullOrWhiteSpace(mod) && seen.Add(mod.Trim()))
                    {
                        list.Add(mod.Trim());
                    }
                }
            }
            return list;
        }

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
            var wanted = GetEffectiveCommonerModules(config.CommonerCompatModules);

            var loaded = new HashSet<string>(
                (loadedAssemblyNames ?? Enumerable.Empty<string>()).Where(n => !string.IsNullOrEmpty(n)),
                StringComparer.OrdinalIgnoreCase);

            var detected = wanted.Where(w => !string.IsNullOrEmpty(w) && loaded.Contains(w)).ToList();
            state.DetectedModules = detected;

            switch (mode)
            {
                case "off":
                    state.Reason = "off (forced by config)";
                    break;

                case "on":
                    Activate(state, config);
                    state.Reason = "on (forced by config)";
                    break;

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
                    break;
            }

            ApplyRumorMode(state, config);
            return state;
        }

        private static void Activate(CommonerCompatState state, DialogueConfig config)
        {
            state.Active = true;
            state.NpcLinePriority = config.CommonerCompatLinePriority;
            state.VolunteerMinClanTier = config.CommonerCompatVolunteerMinClanTier;
        }

        /// <summary>
        /// 依 `dialogue.volunteerMode` 算傳聞模式，以及它連帶決定的「問」的氏族等級閘。**唯一**的一份：
        /// 讀檔時 <see cref="Resolve"/> 呼叫它，執行中在選單改了模式也呼叫它（LISTEN1e，不必重新讀檔）。
        /// 偵測名單用 <see cref="CommonerCompatState.DetectedModules"/>——載入的組件執行中不會變。
        /// 傳聞模式的「自動」只看真的載進來的模組（規格 §12.4）；commonerCompatMode 強制 on／off 只管優先權，不影響模式判定。
        /// 優先權與主動講的氏族閘不在這裡：前者在對話行註冊時就定了，改了也沒有用。
        /// </summary>
        public static void ApplyRumorMode(CommonerCompatState state, DialogueConfig config)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));
            if (config == null) throw new ArgumentNullException(nameof(config));

            state.RumorModeResult = RumorModeResolver.Resolve(config.VolunteerMode, state.DetectedModules);
            state.RumorModeConfiguredAs = config.VolunteerMode;
            // 暢玩模式下不擋問（氏族 Tier 閘設為 0），寫實模式照設定（規格 §12.2、卡片 LISTEN1d §14(4)）
            state.AskMinClanTier = state.Active && state.RumorMode == RumorMode.Realistic
                ? config.CommonerCompatAskMinClanTier
                : 0;
        }

        /// <summary>設定裡的傳聞模式跟上次算的依據不一樣 ⇒ true（不分大小寫、去頭尾空白）。</summary>
        public static bool IsRumorModeStale(CommonerCompatState? state, DialogueConfig config)
        {
            if (state == null || config == null) return false;
            return !string.Equals(
                (state.RumorModeConfiguredAs ?? string.Empty).Trim(),
                (config.VolunteerMode ?? string.Empty).Trim(),
                StringComparison.OrdinalIgnoreCase);
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
                   $"(must outrank NaN's nan_peasant_greeting at 150). " +
                   $"Clan-tier gates: asking needs tier >= {state.AskMinClanTier}, volunteering needs tier >= {state.VolunteerMinClanTier} " +
                   "(a lord speaking up is not the player accosting him; that path has its own relation gates, see the Rumor mode line).";
        }
    }
}
