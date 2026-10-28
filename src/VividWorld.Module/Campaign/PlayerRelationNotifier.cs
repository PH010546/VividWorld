using System;
using System.Collections.Generic;
using Helpers;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Core;
using TaleWorlds.Localization;
using VividWorld.Core.Grudges;

namespace VividWorld.Campaign
{
    /// <summary>
    /// 在情境中對玩家的好感變動寫入原生數值後，跳出原版樣式的快訊通知。
    /// </summary>
    internal static class PlayerRelationNotifier
    {
        public static void NotifyAll(IEnumerable<PlayerRelationNotice>? notices, HeroLookup? heroLookup)
        {
            if (notices == null) return;
            foreach (var notice in notices)
            {
                Notify(notice, heroLookup);
            }
        }

        public static void Notify(PlayerRelationNotice? notice, HeroLookup? heroLookup)
        {
            if (notice == null || !notice.ShouldNotify || string.IsNullOrEmpty(notice.OtherHeroId))
            {
                return;
            }

            try
            {
                var otherHero = heroLookup?.Get(notice.OtherHeroId)
                    ?? Hero.FindFirst(h => string.Equals(h.StringId, notice.OtherHeroId, StringComparison.Ordinal));

                if (otherHero == null)
                {
                    ModLog.Warn($"relation notice skipped for {notice.OtherHeroId}: hero not found");
                    return;
                }

                var effectiveKind = notice.Kind;
                if (effectiveKind == PlayerRelationNoticeKind.Clan && otherHero.Clan == null)
                {
                    ModLog.Info(GrudgeLogFormatter.FormatPlayerRelationNoticeClanFallback(otherHero.StringId, "hero has no clan"));
                    effectiveKind = PlayerRelationNoticeKind.Person;
                }

                TextObject text;
                string kindStr;

                if (effectiveKind == PlayerRelationNoticeKind.Clan)
                {
                    kindStr = "clan";
                    string textId = notice.Direction == PlayerRelationDirection.Increased
                        ? "str_your_relation_increased_with_clan"
                        : "str_your_relation_decreased_with_clan";
                    text = GameTexts.FindText(textId, null);
                    text.SetTextVariable("CLAN_LEADER", otherHero.Clan!.Name);
                }
                else
                {
                    kindStr = "person";
                    string textId = notice.Direction == PlayerRelationDirection.Increased
                        ? "str_your_relation_increased_with_notable"
                        : "str_your_relation_decreased_with_notable";
                    text = GameTexts.FindText(textId, null);
                    StringHelpers.SetCharacterProperties("HERO", otherHero.CharacterObject, text, false);
                }

                int value = Hero.MainHero != null ? otherHero.GetRelation(Hero.MainHero) : 0;
                text.SetTextVariable("VALUE", value);
                text.SetTextVariable("MAGNITUDE", notice.Magnitude);

                MBInformationManager.AddQuickInformation(
                    text,
                    0,
                    otherHero.IsNotable ? otherHero.CharacterObject : null,
                    null,
                    "event:/ui/notification/relation");

                ModLog.Info(GrudgeLogFormatter.FormatPlayerRelationNoticeShown(
                    kindStr,
                    otherHero.StringId,
                    notice.OriginalFromHeroId,
                    notice.Delta,
                    value));
            }
            catch (Exception ex)
            {
                ModLog.Error($"Failed to show player relation notice for {notice.OtherHeroId}", ex);
            }
        }
    }
}
