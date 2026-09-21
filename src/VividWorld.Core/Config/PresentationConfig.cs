using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace VividWorld.Core.Config
{
    public sealed class PresentationConfig
    {
        public int ChronicleMaxEntries { get; set; } = 50;

        /// <summary>
        /// M8：叫出世界紀事視窗的熱鍵。可以帶修飾鍵（<c>"Ctrl+L"</c>、<c>"Alt+Shift+K"</c>）。
        /// 預設 <c>Ctrl+L</c>。單獨的 L 在大地圖是原生的「氏族」視窗，
        /// 而原生那段面板熱鍵按著 Ctrl 時整段不處理（帳本 D-73）⇒ Ctrl+L 不會同時開氏族視窗。
        /// 解析不出來就退回 <c>Ctrl+L</c> 並記一行 log。
        /// </summary>
        public string ChronicleHotkey { get; set; } = "Ctrl+L";

        /// <summary>SNAP1：叫出快照管理清單的熱鍵（InputKey 的名字）。解析不出來就退回 F9 並記一行 log。</summary>
        public string SnapshotManagerHotkey { get; set; } = "F9";
        public string[] FactOrder { get; set; } =
            { "WHO", "CONTEXT", "WHEN", "WHERE", "WHAT", "WHY", "OUTCOME" };

        // §9.5.4：空字串 = 用字串表的 VividWorld_FactSeparator／VividWorld_SentenceEnd。
        // 嚴禁在這裡填全形標點——那是 v2 的在地化 bug。
        public string FactSeparator { get; set; } = string.Empty;
        public string SentenceEnd { get; set; } = string.Empty;

        /// <summary>
        /// 規格 §9.3.1（M6c）：傳聞文字裡的人名與地名是否做成可點擊的百科超連結。預設 true。
        /// 關閉時退回純名字。診斷日誌一律使用純文字，不依賴此開關。
        /// </summary>
        public bool EncyclopediaLinksEnabled { get; set; } = true;

        [JsonExtensionData]
        public IDictionary<string, JToken> Extra { get; set; } = new Dictionary<string, JToken>();
    }
}
