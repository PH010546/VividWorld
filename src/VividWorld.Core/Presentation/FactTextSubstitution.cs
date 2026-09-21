using System;
using System.Collections.Generic;
using System.Text;

namespace VividWorld.Core.Presentation
{
    /// <summary>
    /// 碎片文字裡的 <c>{VAR}</c> 代換。抽到 Core 是為了測得到：Module 那邊的 renderer 只管
    /// 「<c>hero:</c>／<c>settlement:</c> 要怎麼查成名字」，比對佔位符的規則在這裡。規格 §9.5.2。
    ///
    /// **比對大小寫不敏感**（帳本 L-23）：磁碟上已經有一批分片的 <see cref="Events.Fact.Vars"/> 鍵
    /// 被序列化器改成小寫，那些鍵配不上文字裡的 <c>{HOST}</c>。根因已經修在 VividJson，
    /// 但既有存檔改不回來，所以比對這一端也要能吃下去。
    ///
    /// 代換不掉的佔位符**原樣留著**，讓 <see cref="UnresolvedPlaceholders"/> 抓得到——
    /// 悄悄拿掉會變成把半句話送給玩家（實機看到的「shared a simple meal with.」就是這樣來的）。
    /// </summary>
    public static class FactTextSubstitution
    {
        public static string Apply(string? text, IReadOnlyDictionary<string, string>? vars, Func<string, string>? resolve)
        {
            if (string.IsNullOrEmpty(text)) return string.Empty;
            if (vars == null || vars.Count == 0) return text!;

            var lookup = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var kvp in vars)
            {
                if (!string.IsNullOrEmpty(kvp.Key)) lookup[kvp.Key] = kvp.Value;
            }

            var sb = new StringBuilder(text!.Length);
            int i = 0;
            while (i < text.Length)
            {
                if (!TryReadPlaceholder(text, i, out int open, out int close, out string name))
                {
                    sb.Append(text, i, text.Length - i);
                    break;
                }

                sb.Append(text, i, open - i);
                if (lookup.TryGetValue(name, out string? val))
                {
                    sb.Append(resolve != null ? resolve(val) : val);
                }
                else
                {
                    sb.Append(text, open, close - open + 1);
                }
                i = close + 1;
            }

            return sb.ToString();
        }

        /// <summary>代換之後還留在文字裡的 <c>{VAR}</c>，依出現順序、不去重。</summary>
        public static List<string> UnresolvedPlaceholders(string? text)
        {
            var found = new List<string>();
            if (string.IsNullOrEmpty(text)) return found;

            int i = 0;
            while (i < text!.Length)
            {
                if (!TryReadPlaceholder(text, i, out _, out int close, out string name)) break;
                found.Add(name);
                i = close + 1;
            }

            return found;
        }

        /// <summary>從 <paramref name="from"/> 起找下一個形狀合法的 <c>{VAR}</c>。</summary>
        private static bool TryReadPlaceholder(string text, int from, out int open, out int close, out string name)
        {
            int cursor = from;
            while (true)
            {
                open = text.IndexOf('{', cursor);
                if (open < 0) break;

                close = text.IndexOf('}', open + 1);
                if (close < 0) break;

                name = text.Substring(open + 1, close - open - 1);
                if (IsPlaceholderName(name)) return true;

                // {=!} 這種遊戲自己的標記不是佔位符，跳過去繼續找。
                cursor = open + 1;
            }

            open = -1;
            close = -1;
            name = string.Empty;
            return false;
        }

        private static bool IsPlaceholderName(string name)
        {
            if (string.IsNullOrEmpty(name)) return false;
            foreach (char c in name)
            {
                if (!char.IsLetterOrDigit(c) && c != '_') return false;
            }
            return true;
        }
    }
}
