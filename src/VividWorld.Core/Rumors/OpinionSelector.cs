#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using VividWorld.Core.Catalog;

namespace VividWorld.Core.Rumors
{
    /// <summary>
    /// 一則事件的哪幾條 opinion 要結算。模板的 opinion 可以寫 <c>when</c>（"believed" 或 "disbelieved"）：
    /// 編的話信了結算沒寫 when 與 believed 的、不信結算 disbelieved 的；
    /// 不是編的話的事件（包括舊的吹噓事件）只用沒寫 when 與 disbelieved 的，當成一般的 opinion，信了才結算。
    /// </summary>
    public static class OpinionSelector
    {
        public const string Believed = "believed";
        public const string Disbelieved = "disbelieved";

        /// <summary>這位聽者信或不信時要結算的 opinion（可能是空的）。</summary>
        public static List<OpinionDef> ToSettle(IReadOnlyList<OpinionDef>? opinions, bool fabricated, bool believes)
        {
            var list = new List<OpinionDef>();
            if (opinions == null) return list;

            foreach (var o in opinions)
            {
                if (o == null) continue;
                bool noWhen = string.IsNullOrEmpty(o.When);
                bool isBelieved = string.Equals(o.When, Believed, StringComparison.OrdinalIgnoreCase);
                bool isDisbelieved = string.Equals(o.When, Disbelieved, StringComparison.OrdinalIgnoreCase);

                if (fabricated)
                {
                    if (believes ? (noWhen || isBelieved) : isDisbelieved) list.Add(o);
                }
                else
                {
                    if (believes && (noWhen || isDisbelieved)) list.Add(o);
                }
            }
            return list;
        }

        /// <summary>「被說的人」那一條：這則事件信了會結算的第一條；一條都沒有就退回模板的第一條。</summary>
        public static OpinionDef? Subject(IReadOnlyList<OpinionDef>? opinions, bool fabricated)
        {
            if (opinions == null || opinions.Count == 0) return null;
            return ToSettle(opinions, fabricated, believes: true).FirstOrDefault() ?? opinions[0];
        }
    }
}
