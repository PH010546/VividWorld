#nullable enable
using System;
using System.Collections.Generic;

namespace VividWorld.Core.Presentation
{
    public sealed class RumorRenderResult
    {
        public string DisplayText { get; }
        public string PlainText { get; }
        public bool UsedWholeSentence { get; }
        public string? SentenceKeyUsed { get; }
        public string? SentenceKeyCandidate { get; }
        /// <summary>這次查過的所有句型鍵（依查找順序）。</summary>
        public IReadOnlyList<string> SentenceKeysChecked { get; }
        public string? SelfFeelingKeyUsed { get; }
        /// <summary>只講大概又少講時接上的收尾句鍵；沒接為 null。</summary>
        public string? ClosingKeyUsed { get; }
        /// <summary>接在事實句後面的感想那一句的字串鍵；沒接為 null。</summary>
        public string? FeelingKeyUsed { get; }

        public RumorRenderResult(
            string displayText,
            string plainText,
            bool usedWholeSentence = false,
            string? sentenceKeyUsed = null,
            string? sentenceKeyCandidate = null,
            string? selfFeelingKeyUsed = null,
            IReadOnlyList<string>? sentenceKeysChecked = null,
            string? closingKeyUsed = null,
            string? feelingKeyUsed = null)
        {
            DisplayText = displayText ?? string.Empty;
            PlainText = plainText ?? string.Empty;
            UsedWholeSentence = usedWholeSentence;
            SentenceKeyUsed = sentenceKeyUsed;
            SentenceKeyCandidate = sentenceKeyCandidate;
            SelfFeelingKeyUsed = selfFeelingKeyUsed;
            SentenceKeysChecked = sentenceKeysChecked ?? Array.Empty<string>();
            ClosingKeyUsed = closingKeyUsed;
            FeelingKeyUsed = feelingKeyUsed;
        }
    }
}
