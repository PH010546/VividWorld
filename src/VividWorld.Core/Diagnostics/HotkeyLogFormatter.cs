using System.Globalization;

namespace VividWorld.Core.Diagnostics
{
    /// <summary>
    /// 熱鍵的診斷版型。兩行都在 Core，才逐字測得到。
    /// 原本這兩件事寫在 <c>SnapshotManagerFormatter</c>，前綴寫死成 "Snapshot manager:"——
    /// 紀事視窗也是同一個方法，設定寫錯時會印成快照的訊息，所以搬到這裡並帶上是哪一把鑰匙。
    /// </summary>
    public static class HotkeyLogFormatter
    {
        /// <summary>解析完印一行，兩把鑰匙各是什麼（含修飾鍵）。</summary>
        public static string FormatBindings(string chronicle, string snapshotManager)
            => string.Format(CultureInfo.InvariantCulture,
                "Hotkeys: chronicle={0}, snapshot manager={1}.",
                chronicle, snapshotManager);

        /// <summary>設定值解析不出來：說出哪一把鑰匙、原本寫的是什麼、退回哪一個。</summary>
        public static string FormatUnknown(string which, string? requested, string fallback)
            => string.Format(CultureInfo.InvariantCulture,
                "Hotkey: unknown value \"{0}\" for {1}, falling back to {2}.",
                requested ?? string.Empty, which, fallback);
    }
}
