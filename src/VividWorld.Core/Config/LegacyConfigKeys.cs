using System;
using System.Collections.Generic;
using System.Linq;

namespace VividWorld.Core.Config
{
    /// <summary>
    /// 份量改成 1..10 之後不再讀的舊設定鍵。玩家的 <c>config.json</c> 裡留著不動、不刪（只補缺少的鍵），
    /// 這裡只負責認出它們，讓讀檔時印一行說明。
    /// </summary>
    public static class LegacyConfigKeys
    {
        /// <summary><c>events</c> 底下不再讀的四組「依身分對應 1..5 份量」。</summary>
        public static readonly string[] UnusedEventKeys =
        {
            "prisonerDramaByProminence",
            "releaseDramaByProminence",
            "banditCaptureDramaByProminence",
            "banditReleaseDramaByProminence"
        };

        /// <summary>設定檔裡實際存在（被保存在未知鍵裡）的舊鍵，完整路徑，依 <see cref="UnusedEventKeys"/> 的順序。</summary>
        public static IReadOnlyList<string> FindUnusedEventKeys(EventsConfig? events)
        {
            var found = new List<string>();
            if (events?.Extra == null || events.Extra.Count == 0) return found;
            foreach (var key in UnusedEventKeys)
            {
                if (events.Extra.Keys.Any(k => string.Equals(k, key, StringComparison.OrdinalIgnoreCase)))
                {
                    found.Add("events." + key);
                }
            }
            return found;
        }

        /// <summary>讀檔時印的那一行；沒有舊鍵時回傳 null。</summary>
        public static string? DescribeUnusedEventKeys(EventsConfig? events)
        {
            var found = FindUnusedEventKeys(events);
            if (found.Count == 0) return null;
            return "config: " + string.Join(", ", found)
                   + " " + (found.Count == 1 ? "is" : "are")
                   + " no longer read (event weight is now the template base score plus events.weightBonusByProminence / events.weightBonusByClanStanding on a 1..10 scale); left in the file untouched.";
        }

        public static readonly string[] UnusedDialogueVolunteerKeys =
        {
            "npcVolunteerRelationGate",
            "casualChatRelationGate",
            "realisticChatRelationGate"
        };

        public static readonly string[] UnusedDialogueGistKeys =
        {
            "gistExtraHops"
        };

        public static readonly string[] UnusedDialogueScoreKeys =
        {
            "scoreDrama",
            "scoreFreshness",
            "scoreDetail",
            "scoreRelevance",
            "scoreRetellMultiplier",
            "scoreRelevanceRelationGate"
        };

        /// <summary>對話設定中不再讀取的舊鍵說明，依類別產生，每類至多一行。</summary>
        public static IReadOnlyList<string> DescribeUnusedDialogueKeys(DialogueConfig? dialogue)
        {
            var notes = new List<string>();
            if (dialogue?.Extra == null || dialogue.Extra.Count == 0) return notes;

            var volFound = FindKeys(dialogue.Extra, UnusedDialogueVolunteerKeys, "dialogue.");
            if (volFound.Count > 0)
            {
                notes.Add("config: " + string.Join(", ", volFound)
                    + " " + (volFound.Count == 1 ? "is" : "are")
                    + " no longer read (willingness is now calculated from relation and personality traits against casualVolunteerLine and realisticVolunteerLine); left in the file untouched.");
            }

            var gistFound = FindKeys(dialogue.Extra, UnusedDialogueGistKeys, "dialogue.");
            if (gistFound.Count > 0)
            {
                notes.Add("config: " + string.Join(", ", gistFound)
                    + " " + (gistFound.Count == 1 ? "is" : "are")
                    + " no longer read (the gist tier has been removed; rumors told to the player are always complete); left in the file untouched.");
            }

            var scoreFound = FindKeys(dialogue.Extra, UnusedDialogueScoreKeys, "dialogue.");
            if (scoreFound.Count > 0)
            {
                notes.Add("config: " + string.Join(", ", scoreFound)
                    + " " + (scoreFound.Count == 1 ? "is" : "are")
                    + " no longer read (rumor selection now prioritizes closely related news and big news instead of numerical scoring); left in the file untouched.");
            }

            return notes;
        }

        public static readonly string[] UnusedConsequenceKeys =
        {
            "misconception"
        };

        /// <summary>後果設定中不再讀取的舊鍵說明。</summary>
        public static string? DescribeUnusedConsequenceKeys(ConsequenceConfig? consequences)
        {
            if (consequences?.Extra == null || consequences.Extra.Count == 0) return null;
            var found = FindKeys(consequences.Extra, UnusedConsequenceKeys, "consequences.");
            if (found.Count == 0) return null;
            return "config: " + string.Join(", ", found)
                   + " " + (found.Count == 1 ? "is" : "are")
                   + " no longer read (clarification and retracting false rumors are now handled by truth-knower responses); left in the file untouched.";
        }

        private static List<string> FindKeys(IDictionary<string, Newtonsoft.Json.Linq.JToken> extra, string[] keys, string prefix)
        {
            var found = new List<string>();
            foreach (var key in keys)
            {
                if (extra.Keys.Any(k => string.Equals(k, key, StringComparison.OrdinalIgnoreCase)))
                {
                    found.Add(prefix + key);
                }
            }
            return found;
        }
    }
}
