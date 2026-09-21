using System;
using TaleWorlds.InputSystem;
using VividWorld.Core.Presentation;

namespace VividWorld
{
    /// <summary>
    /// 一組「修飾鍵 ＋ 按鍵」的熱鍵。設定字串怎麼拆在 Core 的 <see cref="HotkeySpec"/>，
    /// 這裡只負責問引擎現在有沒有按著（帳本 S-18／D-71／D-72）。
    /// </summary>
    internal readonly struct HotkeyBinding
    {
        internal HotkeyBinding(InputKey key, bool ctrl = false, bool alt = false, bool shift = false)
        {
            Key = key;
            Ctrl = ctrl;
            Alt = alt;
            Shift = shift;
        }

        internal InputKey Key { get; }
        internal bool Ctrl { get; }
        internal bool Alt { get; }
        internal bool Shift { get; }

        /// <summary>
        /// 這一幀剛按下，而且修飾鍵**剛好**符合：要的按著、沒要的沒按著。
        /// 「沒要的沒按著」是故意的——不然設成單獨 L 的人按 Ctrl+L 也會開，
        /// 兩把鑰匙只差一個修飾鍵時就分不開了。
        /// </summary>
        internal bool IsPressed()
            => Input.IsKeyPressed(Key) && ModifiersMatch();

        private bool ModifiersMatch()
            => Ctrl == (Input.IsKeyDown(InputKey.LeftControl) || Input.IsKeyDown(InputKey.RightControl))
            && Alt == (Input.IsKeyDown(InputKey.LeftAlt) || Input.IsKeyDown(InputKey.RightAlt))
            && Shift == (Input.IsKeyDown(InputKey.LeftShift) || Input.IsKeyDown(InputKey.RightShift));

        /// <summary>設定字串 → 這個型別。按鍵名不是 <c>InputKey</c> 時回 null（呼叫端負責退回預設並印一行）。</summary>
        internal static HotkeyBinding? FromSpec(HotkeySpec spec)
        {
            if (!spec.IsValid) return null;
            if (!Enum.TryParse<InputKey>(spec.KeyName, ignoreCase: true, out var key)) return null;
            return new HotkeyBinding(key, spec.Ctrl, spec.Alt, spec.Shift);
        }

        /// <summary>印成設定檔裡那種寫法（例 <c>Ctrl+L</c>），版型與 Core 共用一份。</summary>
        public override string ToString()
            => HotkeySpec.Create(Key.ToString(), Ctrl, Alt, Shift).ToString();
    }
}
