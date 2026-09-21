using System;
using System.Collections.Generic;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Settlements;
using VividWorld.Core.Rumors;

namespace VividWorld.Campaign
{
    internal static class EligibilityLabel
    {
        public static string? GetRejectionReason(Hero? h, IHeroTraitLookup? traits)
        {
            if (h == null) return "null";
            if (!h.IsAlive) return "dead";
            if (h.IsPrisoner) return "prisoner";
            if (h.IsChild) return "child";

            if (traits == null) return "no trait profile";
            if (traits.Of(h.StringId) == null) return "no trait profile";

            // 資格判定只有一個擁有者（規格 §6.5）。這裡只准「呼叫」它，不准重寫它的條件——
            // 本方法之所以存在，就是因為產生器曾經有自己的一套（M6a-fix2 的 C-1）。
            if (Eligibility.IsEligible(traits, h.StringId)) return null;

            // 到這裡一定不合格，剩下的工作只是貼一個誠實的標籤。
            // 名人是壓倒性多數的那一類（帳本 D-41），但不是唯一一類：Villager／Townsfolk／
            // Mercenary 等佔用值同樣既非領主也非流浪者。把它們一律報成 notable 會讓
            // §3.3 那行日誌說謊，而那行正是驗收 C-1 要看的。
            return h.IsNotable ? "notable" : "not a lord or wanderer";
        }

        public static bool IsUsableAsTestSubject(Hero? h)
            => h != null && h != Hero.MainHero;

        public static List<Hero> GetPresentHeroesAtSettlement(Settlement? settlement)
        {
            var result = new List<Hero>();
            if (settlement == null) return result;

            if (settlement.HeroesWithoutParty != null)
            {
                foreach (var h in settlement.HeroesWithoutParty)
                {
                    if (IsUsableAsTestSubject(h) && !result.Contains(h))
                    {
                        result.Add(h);
                    }
                }
            }
            if (settlement.Notables != null)
            {
                foreach (var h in settlement.Notables)
                {
                    if (IsUsableAsTestSubject(h) && !result.Contains(h))
                    {
                        result.Add(h);
                    }
                }
            }
            if (settlement.Parties != null)
            {
                foreach (var p in settlement.Parties)
                {
                    if (p?.LeaderHero != null && IsUsableAsTestSubject(p.LeaderHero) && !result.Contains(p.LeaderHero))
                    {
                        result.Add(p.LeaderHero);
                    }
                }
            }
            return result;
        }
    }
}
