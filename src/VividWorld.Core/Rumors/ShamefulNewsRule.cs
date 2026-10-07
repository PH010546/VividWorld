#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using VividWorld.Core.Catalog;
using VividWorld.Core.Config;
using VividWorld.Core.Dialogue;
using VividWorld.Core.Events;
using VividWorld.Core.Feelings;
using VividWorld.Core.Memory;

namespace VividWorld.Core.Rumors
{
    /// <summary>說話者對出醜者的關係分類（4x4 表的列）。</summary>
    public enum ShamefulSpeakerRow
    {
        Friend,
        Owe,
        Neutral,
        Enemy
    }

    /// <summary>聽者對出醜者的關係分類（4x4 表的欄）。</summary>
    public enum ShamefulListenerCol
    {
        Self,
        Friend,
        Unrelated,
        Enemy
    }

    /// <summary>單一出醜者在特定說者與聽者情境下的評估結果。</summary>
    public sealed class ShamefulPersonEvaluation
    {
        public string Role { get; set; } = string.Empty;
        public string HeroId { get; set; } = string.Empty;
        public ShamefulSpeakerRow Row { get; set; }
        public ShamefulListenerCol Col { get; set; }
        public bool CanTell { get; set; }
        public int Affection { get; set; }
        public double GrudgeNet { get; set; }
        public FeelingMood Mood { get; set; }
    }

    /// <summary>整則事件在醜事規則下的評估結果。</summary>
    public sealed class ShamefulEvaluationResult
    {
        public bool CanTell { get; set; } = true;
        public bool IsShameful { get; set; }
        public string? BlockingHeroId { get; set; }
        public ShamefulSpeakerRow? BlockingRow { get; set; }
        public ShamefulListenerCol? BlockingCol { get; set; }
        public string? HeldBackReason { get; set; }
        public string? HeldBackDetail { get; set; }
        public List<ShamefulPersonEvaluation> Evaluations { get; set; } = new();
    }

    /// <summary>
    /// 醜事講不講規則：看說話的人與聽的人各自跟出醜的人的關係。
    /// 依 4x4 關係矩陣判定消息是否能講，純規則不碰遊戲組件。
    /// </summary>
    public static class ShamefulNewsRule
    {
        public static bool TableCanTell(ShamefulSpeakerRow row, ShamefulListenerCol col)
        {
            switch (row)
            {
                case ShamefulSpeakerRow.Friend:
                    return col == ShamefulListenerCol.Self || col == ShamefulListenerCol.Friend;

                case ShamefulSpeakerRow.Owe:
                    return false;

                case ShamefulSpeakerRow.Neutral:
                    return col == ShamefulListenerCol.Unrelated || col == ShamefulListenerCol.Enemy;

                case ShamefulSpeakerRow.Enemy:
                    return col != ShamefulListenerCol.Self;

                default:
                    return true;
            }
        }

        public static bool HasShamedPersons(
            WorldEvent evt,
            Func<string, EventTemplate?>? getTemplate,
            string? excludeHeroId = null)
        {
            return GetShamedPersons(evt, getTemplate, excludeHeroId).Count > 0;
        }

        public static List<(string Role, string HeroId)> GetShamedPersons(
            WorldEvent evt,
            Func<string, EventTemplate?>? getTemplate,
            string? excludeHeroId = null)
        {
            var result = new List<(string Role, string HeroId)>();
            if (evt?.Participants == null || evt.Participants.Count == 0) return result;

            var template = getTemplate?.Invoke(evt.Type);
            if (template == null) return result;

            var seenHeroes = new HashSet<string>(StringComparer.Ordinal);
            foreach (var kvp in evt.Participants)
            {
                string role = kvp.Key;
                string heroId = kvp.Value;
                if (string.IsNullOrEmpty(heroId)) continue;
                if (!string.IsNullOrEmpty(excludeHeroId) && string.Equals(heroId, excludeHeroId, StringComparison.Ordinal))
                {
                    continue;
                }

                if (!seenHeroes.Add(heroId)) continue;

                string? category = FeelingResolver.CategoryFor(template, evt, role, out _);
                // 看的是感想類別，不是角色名稱：暗殺的 killer、下毒的 poisoner 都歸「下令處決或下手謀殺」。
                // 當眾處決雖然也歸這一類，但人人都看見了，不算醜事。
                bool isShamed = string.Equals(category, "shabby", StringComparison.OrdinalIgnoreCase)
                             || (string.Equals(category, "executor", StringComparison.OrdinalIgnoreCase)
                                 && !string.Equals(evt.Type, "hero_executed", StringComparison.OrdinalIgnoreCase));

                if (isShamed)
                {
                    result.Add((role, heroId));
                }
            }

            return result;
        }

        public static ShamefulSpeakerRow ClassifySpeakerRow(
            string speakerId,
            string shamedHeroId,
            string? eventId,
            IFeelingWorld world,
            IHeroTraitLookup? traits,
            VividWorldConfig config,
            double day,
            out int affection,
            out double grudgeNet,
            out FeelingMood mood)
        {
            var speakerFacts = world.InterestFacts(speakerId);
            var shamedFacts = world.InterestFacts(shamedHeroId);

            var fc = config.Presentation?.Feelings ?? new FeelingsConfig();
            var moodRes = FeelingResolver.ComputeMood(world, speakerId, shamedHeroId, eventId, traits,
                fc, config.Situations, day);
            affection = moodRes.Affection;
            grudgeNet = moodRes.GrudgeNet;
            mood = moodRes.Mood;

            // 1. 說話的人跟出醜的人同家族（ClanId 相同且非空），或 InterestCalculator.IsKin 為真 => 朋友或家人
            if (speakerFacts != null && shamedFacts != null &&
                !string.IsNullOrEmpty(speakerFacts.ClanId) &&
                string.Equals(speakerFacts.ClanId, shamedFacts.ClanId, StringComparison.Ordinal))
            {
                return ShamefulSpeakerRow.Friend;
            }

            if (InterestCalculator.IsKin(speakerFacts, speakerId, shamedFacts, shamedHeroId))
            {
                return ShamefulSpeakerRow.Friend;
            }

            // 2. 其餘照 FeelingGrid.MoodOf:
            // Close, Sore => Friend
            // Owe => Owe
            // Hostile => Enemy
            // Neutral => Neutral
            switch (mood)
            {
                case FeelingMood.Close:
                case FeelingMood.Sore:
                    return ShamefulSpeakerRow.Friend;
                case FeelingMood.Owe:
                    return ShamefulSpeakerRow.Owe;
                case FeelingMood.Hostile:
                    return ShamefulSpeakerRow.Enemy;
                case FeelingMood.Neutral:
                default:
                    return ShamefulSpeakerRow.Neutral;
            }
        }

        public static ShamefulListenerCol ClassifyListenerCol(
            string listenerId,
            string shamedHeroId,
            IFeelingWorld world,
            VividWorldConfig config,
            string? playerHeroId = null)
        {
            // 1. 聽的人就是出醜的人 => 本人
            if (string.Equals(listenerId, shamedHeroId, StringComparison.Ordinal))
            {
                return ShamefulListenerCol.Self;
            }

            var listenerFacts = world.InterestFacts(listenerId);
            var shamedFacts = world.InterestFacts(shamedHeroId);

            // 2. 同家族或 IsKin => 好友或家人（聽的人是玩家時，IsPlayerSpouse 也算）
            if (listenerFacts != null && shamedFacts != null &&
                !string.IsNullOrEmpty(listenerFacts.ClanId) &&
                string.Equals(listenerFacts.ClanId, shamedFacts.ClanId, StringComparison.Ordinal))
            {
                return ShamefulListenerCol.Friend;
            }

            if (InterestCalculator.IsKin(listenerFacts, listenerId, shamedFacts, shamedHeroId))
            {
                return ShamefulListenerCol.Friend;
            }

            bool isPlayer = !string.IsNullOrEmpty(playerHeroId) && string.Equals(listenerId, playerHeroId, StringComparison.Ordinal);
            if (isPlayer && (world as IDialogueWorld)?.IsPlayerSpouse(shamedHeroId) == true)
            {
                return ShamefulListenerCol.Friend;
            }

            // 3. Affection(聽的人, 出醜的人) >= affectionHigh => 好友或家人
            // 4. <= affectionLow => 仇人
            // 5. 其餘 => 不相干；好感讀不到當 0
            var fc = config.Presentation?.Feelings ?? new FeelingsConfig();
            int aff = world.Affection(listenerId, shamedHeroId) ?? 0;

            if (aff >= fc.AffectionHigh)
            {
                return ShamefulListenerCol.Friend;
            }
            if (aff <= fc.AffectionLow)
            {
                return ShamefulListenerCol.Enemy;
            }

            return ShamefulListenerCol.Unrelated;
        }

        public static ShamefulEvaluationResult Evaluate(
            WorldEvent evt,
            string speakerId,
            string listenerId,
            IFeelingWorld world,
            Func<string, EventTemplate?>? getTemplate,
            IHeroTraitLookup? traits,
            VividWorldConfig config,
            double day,
            string? playerHeroId = null)
        {
            var result = new ShamefulEvaluationResult();
            if (evt == null || string.IsNullOrEmpty(speakerId) || string.IsNullOrEmpty(listenerId) || world == null)
            {
                return result;
            }

            if (!config.Propagation.ShamefulNews.Enabled)
            {
                return result;
            }

            var shamed = GetShamedPersons(evt, getTemplate, speakerId);
            if (shamed.Count == 0)
            {
                return result;
            }

            result.IsShameful = true;

            foreach (var (role, shamedHeroId) in shamed)
            {
                var row = ClassifySpeakerRow(speakerId, shamedHeroId, evt.EventId, world, traits, config, day,
                    out int aff, out double grudgeNet, out FeelingMood mood);
                var col = ClassifyListenerCol(listenerId, shamedHeroId, world, config, playerHeroId);
                bool canTell = TableCanTell(row, col);

                result.Evaluations.Add(new ShamefulPersonEvaluation
                {
                    Role = role,
                    HeroId = shamedHeroId,
                    Row = row,
                    Col = col,
                    CanTell = canTell,
                    Affection = aff,
                    GrudgeNet = grudgeNet,
                    Mood = mood
                });

                if (!canTell && result.CanTell)
                {
                    result.CanTell = false;
                    result.BlockingHeroId = shamedHeroId;
                    result.BlockingRow = row;
                    result.BlockingCol = col;
                    result.HeldBackReason = string.Format(CultureInfo.InvariantCulture,
                        "shameful: {0} speaker {1} listener {2}", shamedHeroId, row, col);
                    result.HeldBackDetail = string.Format(CultureInfo.InvariantCulture,
                        "{0} speaker {1} listener {2}", shamedHeroId, row, col);
                }
            }

            return result;
        }
    }
}
