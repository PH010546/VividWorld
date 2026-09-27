#nullable enable
using System.Collections.Generic;

namespace VividWorld.Core.Presentation
{
    public enum RumorPrefixKind
    {
        None,
        Eyewitness,
        HeardFromSource,
        HeardGeneral,
        CorrectionSource,
        CorrectionGeneral,
        Retell,
        RetellSelf,
        Self
    }

    public sealed class RumorPrefix
    {
        public RumorPrefixKind Kind { get; }
        public string? TextId { get; }
        public string? Fallback { get; }
        public IReadOnlyDictionary<string, string> Vars { get; }

        public RumorPrefix(RumorPrefixKind kind, string? textId, string? fallback, IReadOnlyDictionary<string, string>? vars = null)
        {
            Kind = kind;
            TextId = textId;
            Fallback = fallback;
            Vars = vars ?? new Dictionary<string, string>();
        }

        public static readonly RumorPrefix Empty = new(RumorPrefixKind.None, null, null);
    }

    /// <summary>
    /// 傳聞開頭語挑選（純函式）。
    /// 依據講述者的手數、有無消息來源、是否為重述、是否為更正、以及是否為當事人來挑選開頭語。
    /// 開頭語只提來源，不提及經過時間。
    /// </summary>
    public static class RumorPrefixSelector
    {
        public const string RetellSelfTextId = "VividWorld_RetellPrefix_Self";
        public const string RetellSelfFallback = "You may have heard about this already—";

        public const string EyewitnessTextId = "VividWorld_Prefix_Eyewitness";
        public const string EyewitnessFallback = "I saw it myself:";

        public const string HeardFromSourceTextId = "VividWorld_Prefix_HeardFromSource";
        public const string HeardFromSourceFallback = "{SOURCE} told me that";

        public const string HeardGeneralTextId = "VividWorld_Prefix_HeardGeneral";
        public const string HeardGeneralFallback = "I heard it said that";

        public const string CorrectionSourceTextId = "VividWorld_Prefix_CorrectionSource";
        public const string CorrectionSourceFallback = "Later, {SOURCE} told me that";

        public const string CorrectionGeneralTextId = "VividWorld_Prefix_CorrectionGeneral";
        public const string CorrectionGeneralFallback = "Later, I heard it said that";

        public static RumorPrefix SelectPrefix(int hop, string? sourceHeroId, bool isRetell, bool isCorrection, bool isParticipant = false)
        {
            bool hasSource = !string.IsNullOrEmpty(sourceHeroId);
            return SelectPrefix(hop, hasSource, isRetell, isCorrection, sourceHeroId, isParticipant);
        }

        public static RumorPrefix SelectPrefix(int hop, bool hasSource, bool isRetell, bool isCorrection, string? sourceHeroId = null, bool isParticipant = false)
        {
            // 重述前綴只在講述者親眼看到時成立，而且取代下面的開頭語、不疊加。
            // 講述者是聽來的，重述照樣要交代聽誰說的，否則又回到念公告。
            if (isRetell && hop <= 0)
            {
                if (isParticipant)
                {
                    return new RumorPrefix(
                        RumorPrefixKind.RetellSelf,
                        RetellSelfTextId,
                        RetellSelfFallback);
                }

                return new RumorPrefix(
                    RumorPrefixKind.Retell,
                    RumorTextComposer.RetellPrefixTextId,
                    RumorTextComposer.RetellPrefixFallback);
            }

            // 更正的開頭語是「後來又聽說」，只有講述者是聽來的才成立；
            // 第 0 手是自己在場的人，講的就是親身經歷，照下面第 0 手的開頭語。
            if (isCorrection && hop > 0)
            {
                if (hasSource)
                {
                    var vars = new Dictionary<string, string>();
                    if (!string.IsNullOrEmpty(sourceHeroId))
                    {
                        vars["SOURCE"] = "hero:" + sourceHeroId;
                    }
                    return new RumorPrefix(
                        RumorPrefixKind.CorrectionSource,
                        CorrectionSourceTextId,
                        CorrectionSourceFallback,
                        vars);
                }
                return new RumorPrefix(
                    RumorPrefixKind.CorrectionGeneral,
                    CorrectionGeneralTextId,
                    CorrectionGeneralFallback);
            }

            if (hop <= 0)
            {
                if (isParticipant)
                {
                    return new RumorPrefix(RumorPrefixKind.Self, null, null);
                }

                return new RumorPrefix(
                    RumorPrefixKind.Eyewitness,
                    EyewitnessTextId,
                    EyewitnessFallback);
            }

            if (hop <= 2 && hasSource)
            {
                var vars = new Dictionary<string, string>();
                if (!string.IsNullOrEmpty(sourceHeroId))
                {
                    vars["SOURCE"] = "hero:" + sourceHeroId;
                }
                return new RumorPrefix(
                    RumorPrefixKind.HeardFromSource,
                    HeardFromSourceTextId,
                    HeardFromSourceFallback,
                    vars);
            }

            return new RumorPrefix(
                RumorPrefixKind.HeardGeneral,
                HeardGeneralTextId,
                HeardGeneralFallback);
        }
    }
}
