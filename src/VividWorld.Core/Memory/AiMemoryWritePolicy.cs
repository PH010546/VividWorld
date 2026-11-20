#nullable enable
using System;
using System.Collections.Generic;
using VividWorld.Core.Catalog;
using VividWorld.Core.Events;
using VividWorld.Core.Persistence;
using VividWorld.Core.Rumors;

namespace VividWorld.Core.Memory
{
    /// <summary>不寫進 AI 模組記憶的原因；順序就是判斷的順序，第一條成立的勝出。</summary>
    public enum AiMemorySkipReason
    {
        None,
        Player,
        NotParticipant,
        NativeEvent,
        MadeUpTalk,
        Secret,
        RetiredType,
        DoesNotKnow,
        AlreadyWritten
    }

    public readonly struct AiMemoryWriteDecision
    {
        public bool Write { get; }
        public AiMemorySkipReason Reason { get; }

        public AiMemoryWriteDecision(bool write, AiMemorySkipReason reason)
        {
            Write = write;
            Reason = reason;
        }

        /// <summary>日誌用的原因文字；寫的時候是空字串。</summary>
        public string ReasonText => AiMemoryWritePolicy.DescribeReason(Reason);
    }

    /// <summary>
    /// 一則事件要不要把某位英雄自己講的那一版寫進 AI 對話模組的記憶（純函式）。
    /// 只寫 VW 自己記的、而且該英雄是事件角色的事；原版遊戲本來就有的事件、編的話、秘密都不寫。
    /// 放在 Memory 資料夾：這裡必須讀「編的話」旗標才能擋掉，而玩家看得到的 Ai 資料夾的程式不准讀那個旗標。
    /// </summary>
    public static class AiMemoryWritePolicy
    {
        /// <summary>寫過的紀錄用的目標名稱（沿用推送紀錄的格式）。</summary>
        public const string TargetId = "CalradiaRemembers";

        /// <summary>寫過的紀錄用的版本欄位；跟推送紀錄的版本區隔開。</summary>
        public const string RecordVersion = "memory";

        public const string AlreadyInMemoryDetail = "already in the AI mod's memory";

        public static AiMemoryWriteDecision Decide(
            WorldEvent evt,
            string heroId,
            string? playerHeroId,
            Func<string, EventTemplate?>? getTemplate,
            Func<string, string, bool> isAlreadyWritten)
        {
            if (evt == null) throw new ArgumentNullException(nameof(evt));
            if (isAlreadyWritten == null) throw new ArgumentNullException(nameof(isAlreadyWritten));

            if (!string.IsNullOrEmpty(playerHeroId) && string.Equals(heroId, playerHeroId, StringComparison.Ordinal))
                return Skip(AiMemorySkipReason.Player);

            if (string.IsNullOrEmpty(heroId) || evt.RoleOf(heroId) == null)
                return Skip(AiMemorySkipReason.NotParticipant);

            var template = getTemplate?.Invoke(evt.Type);
            if (template != null && template.FromNativeEvent)
                return Skip(AiMemorySkipReason.NativeEvent);

            if (evt.Fabricated)
                return Skip(AiMemorySkipReason.MadeUpTalk);

            if (evt.Origin == EventOrigin.Secret)
                return Skip(AiMemorySkipReason.Secret);

            if (RetiredTypeEvaluator.IsRetired(evt, getTemplate))
                return Skip(AiMemorySkipReason.RetiredType);

            if (evt.EntryFor(heroId) == null)
                return Skip(AiMemorySkipReason.DoesNotKnow);

            if (isAlreadyWritten(heroId, evt.EventId))
                return Skip(AiMemorySkipReason.AlreadyWritten);

            return new AiMemoryWriteDecision(true, AiMemorySkipReason.None);
        }

        private static AiMemoryWriteDecision Skip(AiMemorySkipReason reason) => new AiMemoryWriteDecision(false, reason);

        public static string DescribeReason(AiMemorySkipReason reason)
        {
            switch (reason)
            {
                case AiMemorySkipReason.Player: return "player";
                case AiMemorySkipReason.NotParticipant: return "not a participant";
                case AiMemorySkipReason.NativeEvent: return "native event, recorded by the AI mod itself";
                case AiMemorySkipReason.MadeUpTalk: return "made-up talk";
                case AiMemorySkipReason.Secret: return "secret";
                case AiMemorySkipReason.RetiredType: return "retired type";
                case AiMemorySkipReason.DoesNotKnow: return "does not know it";
                case AiMemorySkipReason.AlreadyWritten: return "already written";
                default: return string.Empty;
            }
        }

        /// <summary>他自己在 VW 裡不講這一則 ⇒ 寫進去也標成私下才講。</summary>
        public static bool IsPrivate(
            WorldEvent evt,
            string heroId,
            Func<string, EventTemplate?>? getTemplate,
            IHeroTraitLookup? traits)
        {
            return !SelfTellEvaluator.Evaluate(evt, heroId, getTemplate, traits).CanTell;
        }

        /// <summary>今天減他知道這件事的那天，取整、不小於 0。</summary>
        public static int DaysAgo(double currentDay, double learnedDay)
        {
            return (int)Math.Max(0, Math.Floor(currentDay - learnedDay));
        }

        /// <summary>把已經寫進 AI 模組記憶的那幾則從回想結果拿掉，排除原因照實記下來。</summary>
        public static NpcRecallResult ExcludeWritten(NpcRecallResult result, AiPushedStore? writtenStore)
        {
            if (result == null) throw new ArgumentNullException(nameof(result));
            if (writtenStore == null || result.Items.Count == 0) return result;

            var written = writtenStore.GetPushedForHero(TargetId, result.HeroId);
            if (written.Count == 0) return result;

            var kept = new List<NpcRecalledMemory>(result.Items.Count);
            var exclusions = new List<RecallExclusion>(result.Exclusions);
            foreach (var item in result.Items)
            {
                if (written.ContainsKey(item.EventId))
                {
                    exclusions.Add(new RecallExclusion(item.EventId, RecallExclusionReason.AlreadyInAiMemory, AlreadyInMemoryDetail));
                }
                else
                {
                    kept.Add(item);
                }
            }

            if (kept.Count == result.Items.Count) return result;
            return new NpcRecallResult(result.HeroId, result.CurrentDay, kept, exclusions, result.CandidateCount);
        }
    }
}
