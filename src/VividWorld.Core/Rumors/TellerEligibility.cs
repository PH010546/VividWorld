using System;
using System.Linq;
using VividWorld.Core.Config;
using VividWorld.Core.Events;
using VividWorld.Core.Memory;
using VividWorld.Core.Persistence;

namespace VividWorld.Core.Rumors
{
    public enum TellReason
    {
        Ok,
        NotVisible,
        FutureEvent,
        Player,
        NoEntry,
        NotEligible,
        Forgotten,
        Outdated,
        AtMaxHop,
        LeakNotSpread
    }

    public static class TellerEligibility
    {
        public static TellReason Check(
            WorldEvent evt,
            string heroId,
            double day,
            int maxHop,
            string playerHeroId,
            VividWorldConfig cfg,
            IHeroTraitLookup traits)
        {
            if (evt == null || !evt.IsVisibleToRumorSystem)
            {
                return TellReason.NotVisible;
            }

            // 日期比今天晚 ⇒ 來自一條被抹掉的時間線，今天不該有人講得出來（規格 §2.2.1）。
            // 這裡擋掉之後，PropagateOnce 與 ChooseTopic 兩條路一起生效，
            // 而且排除理由會印進診斷行，不是靜靜消失。
            if (!EventVisibility.IsVisibleOn(evt, day))
            {
                return TellReason.FutureEvent;
            }

            if (string.Equals(heroId, playerHeroId, StringComparison.Ordinal) && !cfg.Propagation.PlayerCanTell)
            {
                return TellReason.Player;
            }

            var entry = evt.EntryFor(heroId);
            if (entry == null)
            {
                return TellReason.NoEntry;
            }

            if (!Eligibility.IsEligible(traits, heroId))
            {
                return TellReason.NotEligible;
            }

            if (Forgetting.IsForgotten(evt, entry, day, playerHeroId, cfg.Memory))
            {
                return TellReason.Forgotten;
            }

            if (Outdating.IsOutdated(entry))
            {
                return TellReason.Outdated;
            }

            if (entry.Hop >= maxHop)
            {
                return TellReason.AtMaxHop;
            }

            bool isSecretJustLeakedNoHop1 = evt.Origin == EventOrigin.Secret
                && evt.State != null
                && evt.State.Leaked
                && !string.IsNullOrEmpty(evt.State.LeakerHeroId)
                && (evt.KnownBy == null || !evt.KnownBy.Any(k => k.Hop >= 1));

            if (isSecretJustLeakedNoHop1 && !string.Equals(heroId, evt.State!.LeakerHeroId, StringComparison.Ordinal))
            {
                return TellReason.LeakNotSpread;
            }

            return TellReason.Ok;
        }

        public static string TellReasonText(TellReason reason) => reason switch
        {
            TellReason.Ok => "ok",
            TellReason.NotVisible => "not visible",
            TellReason.FutureEvent => "from a future timeline (event day is later than today)",
            TellReason.Player => "player",
            TellReason.NoEntry => "no entry",
            TellReason.NotEligible => "not eligible",
            TellReason.Forgotten => "forgotten",
            TellReason.Outdated => "outdated",
            TellReason.AtMaxHop => "at max hop",
            TellReason.LeakNotSpread => "leak not spread (only leaker can tell)",
            _ => reason.ToString()
        };
    }
}
