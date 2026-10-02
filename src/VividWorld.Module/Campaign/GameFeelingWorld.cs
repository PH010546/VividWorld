using System;
using System.Collections.Generic;
using TaleWorlds.CampaignSystem;
using VividWorld.Core.Dialogue;
using VividWorld.Core.Events;
using VividWorld.Core.Feelings;
using VividWorld.Core.Grudges;
using VividWorld.Core.Memory;

namespace VividWorld.Campaign
{
    /// <summary>感想與對話判定從遊戲讀的資料：好感度、地位、親屬氏族、恩怨帳本。只讀，不改任何狀態。</summary>
    internal sealed class GameFeelingWorld : IDialogueWorld
    {
        private readonly HeroLookup _heroLookup;
        private readonly GrudgeIndex _grudges;

        internal GameFeelingWorld(HeroLookup heroLookup, GrudgeIndex grudges)
        {
            _heroLookup = heroLookup ?? throw new ArgumentNullException(nameof(heroLookup));
            _grudges = grudges ?? throw new ArgumentNullException(nameof(grudges));
        }

        public double Today => CampaignTime.Now.ToDays;

        // 焦點人物常常已經過世：查活人的快取找不到就直接向遊戲要
        private Hero? Find(string heroId)
        {
            if (string.IsNullOrEmpty(heroId)) return null;
            return _heroLookup.Get(heroId) ?? Hero.Find(heroId);
        }

        public int? Affection(string speakerId, string heroId)
        {
            var speaker = Find(speakerId);
            var other = Find(heroId);
            if (speaker == null || other == null || speaker == other) return null;
            return speaker.GetBaseHeroRelation(other);
        }

        public int? StandingRank(string heroId)
        {
            var hero = Find(heroId);
            if (hero == null) return null;
            if (hero.IsKingdomLeader) return 4;
            if (hero.IsClanLeader) return 3;
            if (hero.IsLord) return 2;
            if (hero.IsWanderer) return 1;
            return 0;
        }

        public InterestHeroFacts? InterestFacts(string heroId)
        {
            var hero = Find(heroId);
            return hero != null ? MemoryStamper.ToFacts(hero) : null;
        }

        public IReadOnlyList<GrudgeEntry> PersonalGrudges(string speakerId, string heroId)
            => _grudges.Between(speakerId, heroId, GrudgeScope.Personal);

        public string NameOf(string heroId)
        {
            var hero = Find(heroId);
            return hero?.Name?.ToString() ?? heroId;
        }

        public bool? IsFemale(string heroId)
        {
            var hero = Find(heroId);
            return hero?.IsFemale;
        }

        public string? PlayerKingdomLeaderId => Clan.PlayerClan?.Kingdom?.Leader?.StringId;

        public bool IsPlayerCompanion(string heroId)
        {
            var hero = Find(heroId);
            return hero != null && (hero.CompanionOf == Clan.PlayerClan || hero.IsPlayerCompanion);
        }

        public bool IsPlayerClanMember(string heroId)
        {
            var hero = Find(heroId);
            return hero != null && hero.Clan != null && hero.Clan == Clan.PlayerClan;
        }

        public bool IsPlayerSpouse(string heroId)
        {
            var hero = Find(heroId);
            return hero != null && hero.Spouse == Hero.MainHero;
        }
    }
}
