using System;
using System.Collections.Generic;
using VividWorld.Core.Config;

namespace VividWorld.Core.Feelings
{
    /// <summary>感想類別（焦點人物在這件事裡碰上的遭遇）。清單與每個模板角色歸哪一類見模板 JSON 的 <c>feelings</c> 欄位。</summary>
    public static class FeelingCategories
    {
        public static readonly IReadOnlyList<string> Ids = new[]
        {
            "killed", "died", "executor", "victor", "captured", "freed", "lost_prisoner",
            "released", "lost_face", "brash", "decent", "cared", "joy", "pending",
            "shabby", "wronged"
        };

        public static bool IsKnown(string? id)
        {
            if (string.IsNullOrEmpty(id)) return false;
            foreach (var c in Ids)
            {
                if (string.Equals(c, id, StringComparison.Ordinal)) return true;
            }
            return false;
        }
    }

    /// <summary>說話的人對焦點人物的好感分級。</summary>
    public enum AffectionLevel { High, Neutral, Low }

    /// <summary>說話的人對焦點人物的個人恩怨淨額分級：恩、怨、或都不到門檻。</summary>
    public enum GrudgeLevel { None, Favor, Grudge }

    /// <summary>九格併成的五種心情。</summary>
    public enum FeelingMood
    {
        /// <summary>好感高且沒有怨；或好感普通、有恩。</summary>
        Close,
        /// <summary>好感普通、沒有恩怨。</summary>
        Neutral,
        /// <summary>好感低且沒有恩；或好感普通、有怨。</summary>
        Hostile,
        /// <summary>好感高，卻有怨。</summary>
        Sore,
        /// <summary>好感低，卻有恩。</summary>
        Owe
    }

    /// <summary>焦點人物的地位相對於說話的人。</summary>
    public enum StandingComparison { Higher, Equal, Lower }

    public static class FeelingGrid
    {
        public static AffectionLevel Classify(int affection, FeelingsConfig cfg)
        {
            if (affection >= cfg.AffectionHigh) return AffectionLevel.High;
            if (affection <= cfg.AffectionLow) return AffectionLevel.Low;
            return AffectionLevel.Neutral;
        }

        public static GrudgeLevel Classify(double grudgeNet, FeelingsConfig cfg)
        {
            if (grudgeNet >= cfg.GrudgeThreshold) return GrudgeLevel.Favor;
            if (grudgeNet <= -cfg.GrudgeThreshold) return GrudgeLevel.Grudge;
            return GrudgeLevel.None;
        }

        /// <summary>九格 → 五種心情。</summary>
        public static FeelingMood MoodOf(AffectionLevel affection, GrudgeLevel grudge)
        {
            switch (affection)
            {
                case AffectionLevel.High:
                    return grudge == GrudgeLevel.Grudge ? FeelingMood.Sore : FeelingMood.Close;
                case AffectionLevel.Neutral:
                    return grudge == GrudgeLevel.Favor ? FeelingMood.Close
                         : grudge == GrudgeLevel.Grudge ? FeelingMood.Hostile
                         : FeelingMood.Neutral;
                default:
                    return grudge == GrudgeLevel.Favor ? FeelingMood.Owe : FeelingMood.Hostile;
            }
        }

        public static string MoodId(FeelingMood mood)
        {
            switch (mood)
            {
                case FeelingMood.Close: return "close";
                case FeelingMood.Neutral: return "neutral";
                case FeelingMood.Hostile: return "hostile";
                case FeelingMood.Sore: return "sore";
                default: return "owe";
            }
        }

        /// <summary>地位序：國王 4 &gt; 族長 3 &gt; 領主 2 &gt; 流浪者 1 &gt; 其他 0。</summary>
        public static StandingComparison Compare(int focusRank, int speakerRank)
        {
            if (focusRank > speakerRank) return StandingComparison.Higher;
            if (focusRank < speakerRank) return StandingComparison.Lower;
            return StandingComparison.Equal;
        }

        /// <summary>稱呼字串鍵：地位比較 × 好感（高／普通／低）共 9 格。</summary>
        public static string AddressKey(StandingComparison standing, AffectionLevel affection)
            => "VividWorld_Address_" + standing + "_" + affection;

        public static IEnumerable<string> AllAddressKeys()
        {
            foreach (StandingComparison s in Enum.GetValues(typeof(StandingComparison)))
            {
                foreach (AffectionLevel a in Enum.GetValues(typeof(AffectionLevel)))
                {
                    yield return AddressKey(s, a);
                }
            }
        }
    }
}
