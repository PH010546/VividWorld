using System;
using System.Collections.Generic;
using System.Linq;
using VividWorld.Core.Config;
using VividWorld.Core.Events;

namespace VividWorld.Core.Ingest
{
    public sealed class ValidationOutcome
    {
        public bool IsValid { get; }
        public string? RejectionReason { get; }              // 第一個致命錯誤；合法時為 null
        public IReadOnlyList<string> Warnings { get; }       // 不阻擋收錄

        public ValidationOutcome(bool isValid, string? rejectionReason, IReadOnlyList<string>? warnings = null)
        {
            IsValid = isValid;
            RejectionReason = rejectionReason;
            Warnings = warnings ?? Array.Empty<string>();
        }

        public static ValidationOutcome Success(IReadOnlyList<string>? warnings = null) =>
            new(true, null, warnings);

        public static ValidationOutcome Reject(string reason) =>
            new(false, reason, null);
    }

    public static class EventSubmissionValidator
    {
        private static readonly string[] ValidVarPrefixes = FactValidationRules.ValidVarPrefixes;

        /// <param name="eventExists">判斷某個 eventId 是否已存在。Core 沒有儲存層，
        /// 由呼叫端注入；測試傳入假物件。與 EventId.Mint 的 isTaken 同一個模式。</param>
        public static ValidationOutcome Validate(
            EventSubmission s,
            PersistenceConfig cfg,
            Func<string, bool> eventExists)
        {
            if (s == null)
            {
                return ValidationOutcome.Reject("Submission cannot be null.");
            }

            cfg ??= new PersistenceConfig();
            eventExists ??= (_ => false);

            // 1. Type 為空或全空白
            if (string.IsNullOrWhiteSpace(s.Type))
            {
                return ValidationOutcome.Reject("Event type cannot be null or whitespace.");
            }

            // 2. Day < 0
            if (s.Day < 0)
            {
                return ValidationOutcome.Reject("Event day cannot be negative.");
            }

            // 3. Facts 為空
            if (s.Facts == null || s.Facts.Count == 0)
            {
                return ValidationOutcome.Reject("Event facts list cannot be empty.");
            }

            // 4. Facts.Count > cfg.MaxFactsPerEvent
            if (s.Facts.Count > cfg.MaxFactsPerEvent)
            {
                return ValidationOutcome.Reject($"Event facts count ({s.Facts.Count}) exceeds maximum allowed ({cfg.MaxFactsPerEvent}).");
            }

            // 5. 任一 Fact.Id 為空
            if (s.Facts.Any(f => string.IsNullOrWhiteSpace(f.Id)))
            {
                return ValidationOutcome.Reject("Fact ID cannot be empty or whitespace.");
            }

            // 6. Fact.Id 在事件內重複
            var factIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (var fact in s.Facts)
            {
                if (!factIds.Add(fact.Id))
                {
                    return ValidationOutcome.Reject($"Duplicate fact ID '{fact.Id}' found within event.");
                }
            }

            // 7. 任一 Fact.Fragility 不在 1..5
            if (s.Facts.Any(f => !FactValidationRules.IsValidFragility(f.Fragility)))
            {
                return ValidationOutcome.Reject("Fact fragility must be between 1 and 5.");
            }

            // 8. 任一 Fact.Text 為空（TextId 可空，見警告 2）
            if (s.Facts.Any(f => string.IsNullOrEmpty(f.Text)))
            {
                return ValidationOutcome.Reject("Fact text cannot be empty.");
            }

            // 9. Participants 為空
            if (s.Participants == null || s.Participants.Count == 0)
            {
                return ValidationOutcome.Reject("Event participants cannot be empty.");
            }

            // 10. KnowingRoles 有成員不是 Participants 的鍵
            if (s.KnowingRoles != null && s.KnowingRoles.Any(role => !s.Participants.ContainsKey(role)))
            {
                return ValidationOutcome.Reject("KnowingRoles contains a role not found in event participants.");
            }

            // 11. DramaWeight 非 null 且不在 1..5
            if (s.DramaWeight.HasValue && !FactValidationRules.IsValidDramaWeight(s.DramaWeight.Value))
            {
                return ValidationOutcome.Reject("DramaWeight must be between 1 and 5 when specified.");
            }

            // 12. Origin 為 null
            if (!s.Origin.HasValue)
            {
                return ValidationOutcome.Reject("Event origin must be explicitly specified (cannot be null).");
            }

            // 13. 任一 Fact.Vars 的值不帶合法前綴（hero:／settlement:／faction:／key:／num:／text:）
            foreach (var fact in s.Facts)
            {
                if (fact.Vars != null)
                {
                    foreach (var kvp in fact.Vars)
                    {
                        string val = kvp.Value;
                        if (!FactValidationRules.HasValidVarPrefix(val))
                        {
                            return ValidationOutcome.Reject(
                                $"Fact '{fact.Id}' variable '{kvp.Key}' has invalid value '{val}'. Must start with a valid prefix: hero:, settlement:, faction:, key:, num:, text:.");
                        }
                    }
                }
            }

            // Warnings
            var warnings = new List<string>();

            // 1. LinkedEventId 或任一 RefersTo 非空但 eventExists 為 false
            if (!string.IsNullOrEmpty(s.LinkedEventId) && !eventExists(s.LinkedEventId!))
            {
                warnings.Add($"Linked event '{s.LinkedEventId}' does not exist in store.");
            }

            foreach (var fact in s.Facts)
            {
                if (!string.IsNullOrEmpty(fact.RefersTo) && !eventExists(fact.RefersTo!))
                {
                    warnings.Add($"Fact '{fact.Id}' refers to non-existent event '{fact.RefersTo}'.");
                }

                // 2. 任一 Fact.TextId 為空
                if (string.IsNullOrEmpty(fact.TextId))
                {
                    warnings.Add($"Fact '{fact.Id}' has empty TextId.");
                }
            }

            return ValidationOutcome.Success(warnings);
        }
    }
}
