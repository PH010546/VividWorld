using System.Collections.Generic;
using System.Text;

namespace VividWorld.Core.Presentation
{
    /// <summary>
    /// 設定檔裡的熱鍵字串（例 <c>"Ctrl+L"</c>、<c>"F9"</c>）拆成「修飾鍵 ＋ 按鍵名」。
    /// 這裡刻意不認識 <c>InputKey</c>——那是引擎的型別，Core 不參照 TaleWorlds。
    /// 按鍵名交給 Module 用 <c>Enum.TryParse</c> 解（帳本 S-18／D-71）。
    /// </summary>
    public readonly struct HotkeySpec
    {
        private HotkeySpec(string keyName, bool ctrl, bool alt, bool shift, bool isValid)
        {
            KeyName = keyName;
            Ctrl = ctrl;
            Alt = alt;
            Shift = shift;
            IsValid = isValid;
        }

        /// <summary>按鍵名，未經驗證（是不是真的 InputKey 由 Module 判斷）。</summary>
        public string KeyName { get; }

        public bool Ctrl { get; }
        public bool Alt { get; }
        public bool Shift { get; }

        /// <summary>字串拆得開就是 true。空白、只有修飾鍵、修飾鍵名不認得都是 false。</summary>
        public bool IsValid { get; }

        public static HotkeySpec Invalid => new HotkeySpec(string.Empty, false, false, false, false);

        public static HotkeySpec Create(string keyName, bool ctrl = false, bool alt = false, bool shift = false)
            => new HotkeySpec(keyName ?? string.Empty, ctrl, alt, shift, !string.IsNullOrWhiteSpace(keyName));

        /// <summary>
        /// 拆 <c>"Ctrl+Shift+L"</c> 這種寫法：最後一段是按鍵名，前面每一段都必須是修飾鍵。
        /// 大小寫不計，段與段之間的空白會吃掉。認得 Ctrl／Control、Alt、Shift。
        /// </summary>
        public static HotkeySpec Parse(string? text)
        {
            if (string.IsNullOrWhiteSpace(text)) return Invalid;

            string[] parts = text!.Split('+');
            bool ctrl = false, alt = false, shift = false;

            for (int i = 0; i < parts.Length - 1; i++)
            {
                switch (parts[i].Trim().ToLowerInvariant())
                {
                    case "ctrl":
                    case "control": ctrl = true; break;
                    case "alt": alt = true; break;
                    case "shift": shift = true; break;
                    default: return Invalid;      // 認不得的修飾鍵：整串作廢，不要猜
                }
            }

            string keyName = parts[parts.Length - 1].Trim();
            if (keyName.Length == 0) return Invalid;   // "Ctrl+" 這種

            return new HotkeySpec(keyName, ctrl, alt, shift, true);
        }

        /// <summary>寫回人看得懂的樣子：修飾鍵固定照 Ctrl → Alt → Shift 的順序。</summary>
        public override string ToString()
        {
            if (!IsValid) return string.Empty;

            var sb = new StringBuilder();
            if (Ctrl) sb.Append("Ctrl+");
            if (Alt) sb.Append("Alt+");
            if (Shift) sb.Append("Shift+");
            sb.Append(KeyName);
            return sb.ToString();
        }

        /// <summary>這一組熱鍵需要按住哪些修飾鍵；沒有就是空的。診斷用。</summary>
        public IReadOnlyList<string> Modifiers
        {
            get
            {
                var list = new List<string>(3);
                if (Ctrl) list.Add("Ctrl");
                if (Alt) list.Add("Alt");
                if (Shift) list.Add("Shift");
                return list;
            }
        }
    }
}
