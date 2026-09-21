using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace VividWorld.Core.Diagnostics
{
    public static class KnowledgeShareFormatter
    {
        private const string Prefix = "- Knowledge share by event type (hop entries): ";
        private const string NoEventsSuffix = "(no events)";

        public static string Format(IEnumerable<(string Type, int Count)>? items)
        {
            if (items == null)
            {
                return Prefix + NoEventsSuffix;
            }

            var list = items.Where(x => !string.IsNullOrEmpty(x.Type)).ToList();
            if (list.Count == 0)
            {
                return Prefix + NoEventsSuffix;
            }

            long total = list.Sum(x => (long)x.Count);
            var sorted = list
                .OrderByDescending(x => x.Count)
                .ThenBy(x => x.Type, StringComparer.Ordinal)
                .ToList();

            var parts = new List<string>(sorted.Count);
            foreach (var item in sorted)
            {
                double pct = total > 0 ? (item.Count * 100.0 / total) : 0.0;
                parts.Add(string.Format(CultureInfo.InvariantCulture, "{0} {1} ({2:0.0}%)", item.Type, item.Count, pct));
            }

            return Prefix + string.Join(", ", parts);
        }

        public static string Format(IEnumerable<KeyValuePair<string, int>>? items)
        {
            return Format(items?.Select(kvp => (kvp.Key, kvp.Value)));
        }

        private const string RememberedPrefix = "- Knowledge share by event type (remembered NPC entries): ";

        public static string FormatRemembered(IEnumerable<(string Type, int Count)>? items)
        {
            if (items == null)
            {
                return RememberedPrefix + NoEventsSuffix;
            }

            var list = items.Where(x => !string.IsNullOrEmpty(x.Type)).ToList();
            if (list.Count == 0)
            {
                return RememberedPrefix + NoEventsSuffix;
            }

            long total = list.Sum(x => (long)x.Count);
            var sorted = list
                .OrderByDescending(x => x.Count)
                .ThenBy(x => x.Type, StringComparer.Ordinal)
                .ToList();

            var parts = new List<string>(sorted.Count);
            foreach (var item in sorted)
            {
                double pct = total > 0 ? (item.Count * 100.0 / total) : 0.0;
                parts.Add(string.Format(CultureInfo.InvariantCulture, "{0} {1} ({2:0.0}%)", item.Type, item.Count, pct));
            }

            return RememberedPrefix + string.Join(", ", parts);
        }

        public static string FormatRemembered(IEnumerable<KeyValuePair<string, int>>? items)
        {
            return FormatRemembered(items?.Select(kvp => (kvp.Key, kvp.Value)));
        }
    }
}
