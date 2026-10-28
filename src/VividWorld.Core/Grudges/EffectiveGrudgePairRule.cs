using System;
using System.Collections.Generic;
using VividWorld.Core.Events;

namespace VividWorld.Core.Grudges
{
    /// <summary>
    /// 決定恩怨寫入的原生配對以及是否評估家族牽連的決策結果。
    /// </summary>
    public sealed class GrudgeEffectivePairDecision
    {
        /// <summary>是否寫入遊戲換過族長之後的有效配對。</summary>
        public bool UseEffectivePair { get; }

        /// <summary>是否依一般規則評估家族牽連升級。</summary>
        public bool ShouldEvaluateClanEscalation { get; }

        /// <summary>決定原因字串（供日誌與診斷使用）。</summary>
        public string? Reason { get; }

        public GrudgeEffectivePairDecision(bool useEffectivePair, bool shouldEvaluateClanEscalation, string? reason)
        {
            UseEffectivePair = useEffectivePair;
            ShouldEvaluateClanEscalation = shouldEvaluateClanEscalation;
            Reason = reason;
        }
    }

    /// <summary>
    /// 撤回好感度時施加目標配對。
    /// </summary>
    public readonly struct RetractionPair : IEquatable<RetractionPair>
    {
        public string Hero1 { get; }
        public string Hero2 { get; }

        public RetractionPair(string hero1, string hero2)
        {
            Hero1 = hero1 ?? string.Empty;
            Hero2 = hero2 ?? string.Empty;
        }

        public void Deconstruct(out string hero1, out string hero2)
        {
            hero1 = Hero1;
            hero2 = Hero2;
        }

        public bool Equals(RetractionPair other) =>
            string.Equals(Hero1, other.Hero1, StringComparison.Ordinal) &&
            string.Equals(Hero2, other.Hero2, StringComparison.Ordinal);

        public override bool Equals(object? obj) => obj is RetractionPair other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                return (StringComparer.Ordinal.GetHashCode(Hero1) * 397) ^
                       StringComparer.Ordinal.GetHashCode(Hero2);
            }
        }

        public static bool operator ==(RetractionPair left, RetractionPair right) => left.Equals(right);
        public static bool operator !=(RetractionPair left, RetractionPair right) => !left.Equals(right);

        public static implicit operator (string Hero1, string Hero2)(RetractionPair pair) =>
            (pair.Hero1, pair.Hero2);
    }

    /// <summary>
    /// 當「跟玩家的好感各人各算」開關關閉時，對玩家的恩怨與傳聞好感改落在遊戲畫面讀取的外交有效配對上，
    /// 並且情境不再升級牽連家族（避免同一筆恩怨在族長身上重複扣除）。
    /// </summary>
    public static class EffectiveGrudgePairRule
    {
        public const string DefaultEffectivePairReason =
            "per-person relations with the player are off, the grudge already landed on the pair the game shows";

        /// <summary>
        /// 評估是否使用有效配對與是否評估家族牽連。
        /// </summary>
        public static GrudgeEffectivePairDecision Evaluate(
            bool personalWithPlayerEnabled,
            GrudgeSource source,
            bool isAboutPlayer)
        {
            if (!personalWithPlayerEnabled && isAboutPlayer)
            {
                return new GrudgeEffectivePairDecision(
                    useEffectivePair: true,
                    shouldEvaluateClanEscalation: false,
                    reason: DefaultEffectivePairReason);
            }

            return new GrudgeEffectivePairDecision(
                useEffectivePair: false,
                shouldEvaluateClanEscalation: source == GrudgeSource.Situation,
                reason: null);
        }

        /// <summary>
        /// 評估是否使用有效配對與是否評估家族牽連。
        /// </summary>
        public static GrudgeEffectivePairDecision Evaluate(
            bool personalWithPlayerEnabled,
            GrudgeSource source,
            string? aboutHeroId,
            string? playerHeroId)
        {
            bool isAboutPlayer = !string.IsNullOrEmpty(aboutHeroId) &&
                                 !string.IsNullOrEmpty(playerHeroId) &&
                                 string.Equals(aboutHeroId, playerHeroId, StringComparison.Ordinal);

            return Evaluate(personalWithPlayerEnabled, source, isAboutPlayer);
        }

        /// <summary>
        /// 決定撤回時應還給哪一對英雄（優先照記錄時實際寫入的原生配對，否則退回知情者與對象本人）。
        /// </summary>
        public static RetractionPair ResolveRetractionPair(
            string knowerHeroId,
            string aboutHeroId,
            IReadOnlyList<string>? nativePair)
        {
            if (nativePair != null &&
                nativePair.Count >= 2 &&
                !string.IsNullOrEmpty(nativePair[0]) &&
                !string.IsNullOrEmpty(nativePair[1]))
            {
                return new RetractionPair(nativePair[0], nativePair[1]);
            }

            return new RetractionPair(knowerHeroId ?? string.Empty, aboutHeroId ?? string.Empty);
        }
    }
}
