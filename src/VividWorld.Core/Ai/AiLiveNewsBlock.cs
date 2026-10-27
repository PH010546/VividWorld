using System;
using System.Collections.Generic;
using System.Text;

namespace VividWorld.Core.Ai
{
    public sealed class AiLiveNewsItem
    {
        public string EventId { get; }
        public string Kind { get; }
        public int DaysAgo { get; }
        public string Text { get; }

        public AiLiveNewsItem(string eventId, string kind, int daysAgo, string text)
        {
            EventId = eventId ?? string.Empty;
            Kind = kind ?? AiMemoryKind.Other;
            DaysAgo = daysAgo < 0 ? 0 : daysAgo;
            Text = text ?? string.Empty;
        }
    }

    public sealed class AiLiveNewsExcludedItem
    {
        public string EventId { get; }
        public int ItemChars { get; }
        public int UsedCharsBefore { get; }

        public AiLiveNewsExcludedItem(string eventId, int itemChars, int usedCharsBefore)
        {
            EventId = eventId ?? string.Empty;
            ItemChars = itemChars;
            UsedCharsBefore = usedCharsBefore;
        }
    }

    public sealed class AiLiveNewsBlockResult
    {
        public string Text { get; }
        public IReadOnlyList<string> IncludedEventIds { get; }
        public IReadOnlyList<AiLiveNewsExcludedItem> ExcludedItems { get; }
        public int MaxChars { get; }

        public AiLiveNewsBlockResult(
            string text,
            IReadOnlyList<string> includedEventIds,
            IReadOnlyList<AiLiveNewsExcludedItem> excludedItems,
            int maxChars)
        {
            Text = text ?? string.Empty;
            IncludedEventIds = includedEventIds ?? Array.Empty<string>();
            ExcludedItems = excludedItems ?? Array.Empty<AiLiveNewsExcludedItem>();
            MaxChars = maxChars;
        }
    }

    public static class AiLiveNewsBlock
    {
        public const string VariableName = "vividworld_news";
        public const string HeardNowVariableName = "vividworld_heard_now";

        public const int MaxChars = 500;
        public const int LeanMaxChars = 300;

        public const int HeardNowMaxChars = 1000;
        public const int HeardNowLeanMaxChars = 300;

        public const string HeaderStandardTemplate = "What {0} knows right now about recent happenings in Calradia. This is current: where an older memory disagrees, go by this.";
        public const string HeaderLeanTemplate = "{0} knows right now (current, overrides older memories):";

        public const string TagHearsay = "heard";
        public const string TagOwnLife = "own life";
        public const string TagOther = "with the player";

        public static string FormatKindTag(string kind)
        {
            if (string.Equals(kind, AiMemoryKind.Hearsay, StringComparison.OrdinalIgnoreCase))
            {
                return TagHearsay;
            }
            if (string.Equals(kind, AiMemoryKind.OwnLife, StringComparison.OrdinalIgnoreCase))
            {
                return TagOwnLife;
            }
            return TagOther;
        }

        public static string FormatTimeAgo(int daysAgo)
        {
            if (daysAgo <= 0) return "today";
            if (daysAgo == 1) return "1 day ago";
            return $"{daysAgo} days ago";
        }

        public static string FormatLine(AiLiveNewsItem item)
        {
            if (item == null) return string.Empty;
            string tag = FormatKindTag(item.Kind);
            string time = FormatTimeAgo(item.DaysAgo);
            return $"- [{tag}, {time}] {item.Text}";
        }

        public static int GetMaxChars(string? variableName, bool lean)
        {
            if (string.Equals(variableName, HeardNowVariableName, StringComparison.OrdinalIgnoreCase))
            {
                return lean ? HeardNowLeanMaxChars : HeardNowMaxChars;
            }

            return lean ? LeanMaxChars : MaxChars;
        }

        public static AiLiveNewsBlockResult BuildForVariable(string? variableName, string npcName, bool lean, IReadOnlyList<AiLiveNewsItem> items)
        {
            return Build(npcName, lean, items, GetMaxChars(variableName, lean));
        }

        public static AiLiveNewsBlockResult BuildHeardNow(string npcName, bool lean, IReadOnlyList<AiLiveNewsItem> items)
        {
            return Build(npcName, lean, items, lean ? HeardNowLeanMaxChars : HeardNowMaxChars);
        }

        public static AiLiveNewsBlockResult Build(string npcName, bool lean, IReadOnlyList<AiLiveNewsItem> items)
        {
            return Build(npcName, lean, items, lean ? LeanMaxChars : MaxChars);
        }

        public static AiLiveNewsBlockResult Build(string npcName, bool lean, IReadOnlyList<AiLiveNewsItem> items, int maxChars)
        {
            int limit = maxChars;
            var includedEventIds = new List<string>();
            var excludedItems = new List<AiLiveNewsExcludedItem>();

            if (items == null || items.Count == 0)
            {
                return new AiLiveNewsBlockResult(string.Empty, includedEventIds, excludedItems, limit);
            }

            var includedLines = new List<string>();
            int currentLength = 0;

            for (int i = 0; i < items.Count; i++)
            {
                var item = items[i];
                string line = FormatLine(item);
                int additionalLength = (includedLines.Count == 0) ? line.Length : (1 + line.Length); // \n + line

                if (includedLines.Count == 0)
                {
                    // 第一則一定放，即使自己就超過上限
                    includedLines.Add(line);
                    includedEventIds.Add(item.EventId);
                    currentLength += line.Length;
                }
                else
                {
                    if (currentLength + additionalLength <= limit)
                    {
                        includedLines.Add(line);
                        includedEventIds.Add(item.EventId);
                        currentLength += additionalLength;
                    }
                    else
                    {
                        excludedItems.Add(new AiLiveNewsExcludedItem(item.EventId, line.Length, currentLength));
                    }
                }
            }

            if (includedLines.Count == 0)
            {
                return new AiLiveNewsBlockResult(string.Empty, includedEventIds, excludedItems, limit);
            }

            string name = string.IsNullOrEmpty(npcName) ? "This character" : npcName;
            string header = lean
                ? string.Format(HeaderLeanTemplate, name)
                : string.Format(HeaderStandardTemplate, name);

            var sb = new StringBuilder();
            sb.Append(header);
            for (int i = 0; i < includedLines.Count; i++)
            {
                sb.Append('\n');
                sb.Append(includedLines[i]);
            }

            return new AiLiveNewsBlockResult(sb.ToString(), includedEventIds, excludedItems, limit);
        }
    }
}
