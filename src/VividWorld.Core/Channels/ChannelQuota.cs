using System;
using System.Collections.Generic;
using VividWorld.Core.Config;
using VividWorld.Core.Rumors;

namespace VividWorld.Core.Channels
{
    public sealed class ChannelQuotaResult
    {
        public IReadOnlyList<ChannelLink> Selected { get; }
        public IReadOnlyList<ChannelLink> SqueezedOut { get; }

        public ChannelQuotaResult(IReadOnlyList<ChannelLink> selected, IReadOnlyList<ChannelLink> squeezedOut)
        {
            Selected = selected ?? Array.Empty<ChannelLink>();
            SqueezedOut = squeezedOut ?? Array.Empty<ChannelLink>();
        }
    }

    public static class ChannelQuota
    {
        public static ChannelQuotaResult Allocate(
            IEnumerable<ChannelLink> candidates,
            PropagationConfig propagationConfig,
            RelationConfig relationConfig,
            string? selfHeroId = null,
            string? playerHeroId = null)
        {
            if (propagationConfig == null) throw new ArgumentNullException(nameof(propagationConfig));
            if (relationConfig == null) throw new ArgumentNullException(nameof(relationConfig));

            return Allocate(
                candidates,
                propagationConfig.ChannelWeights,
                relationConfig,
                propagationConfig.MaxContactsPerQuery,
                propagationConfig.MaxInPersonContacts,
                propagationConfig.MaxRemoteContacts,
                selfHeroId,
                playerHeroId);
        }

        public static ChannelQuotaResult Allocate(
            IEnumerable<ChannelLink> candidates,
            ChannelWeights channelWeights,
            RelationConfig relationConfig,
            int maxTotal = 6,
            int maxInPerson = 4,
            int maxRemote = 2,
            string? selfHeroId = null,
            string? playerHeroId = null)
        {
            if (candidates == null)
            {
                return new ChannelQuotaResult(Array.Empty<ChannelLink>(), Array.Empty<ChannelLink>());
            }

            channelWeights ??= new ChannelWeights();
            relationConfig ??= new RelationConfig();

            // 5. 移除自己與玩家
            // 4. 去重在分組之前做，保留權重（score = channelWeight * relationFactor）最高者
            var bestByHero = new Dictionary<string, (ChannelLink Link, double Score)>(StringComparer.Ordinal);

            foreach (var link in candidates)
            {
                if (link == null || string.IsNullOrEmpty(link.HeroId)) continue;
                if (!string.IsNullOrEmpty(selfHeroId) && link.HeroId == selfHeroId) continue;
                if (!string.IsNullOrEmpty(playerHeroId) && link.HeroId == playerHeroId) continue;

                double cw = channelWeights.For(link.Kind);
                double rf = RelationFactor.For(link.Kind, link.Relation, relationConfig);
                double score = cw * rf;

                if (!bestByHero.TryGetValue(link.HeroId, out var existing) || score > existing.Score)
                {
                    bestByHero[link.HeroId] = (link, score);
                }
            }

            // 分成見面與遠端兩組
            var inPersonList = new List<(ChannelLink Link, double Score)>();
            var remoteList = new List<(ChannelLink Link, double Score)>();

            foreach (var item in bestByHero.Values)
            {
                if (ChannelClass.IsInPerson(item.Link.Kind))
                {
                    inPersonList.Add(item);
                }
                else
                {
                    remoteList.Add(item);
                }
            }

            // 排序：由高到低，同分以 HeroId 排序打破平手保證確定性
            inPersonList.Sort((a, b) =>
            {
                int cmp = b.Score.CompareTo(a.Score);
                return cmp != 0 ? cmp : string.CompareOrdinal(a.Link.HeroId, b.Link.HeroId);
            });

            remoteList.Sort((a, b) =>
            {
                int cmp = b.Score.CompareTo(a.Score);
                return cmp != 0 ? cmp : string.CompareOrdinal(a.Link.HeroId, b.Link.HeroId);
            });

            // 1. 見面候選取至多 maxInPerson 個
            // 2. 遠端候選取至多 maxRemote 個
            // 3. 某一側不足時，讓另一側補滿到總量 maxTotal
            int takeInPerson = Math.Min(maxInPerson, inPersonList.Count);
            int takeRemote = Math.Min(maxRemote, remoteList.Count);

            int remainingSlots = Math.Max(0, maxTotal - (takeInPerson + takeRemote));
            if (remainingSlots > 0)
            {
                int extraInPerson = Math.Min(remainingSlots, inPersonList.Count - takeInPerson);
                takeInPerson += extraInPerson;
                remainingSlots -= extraInPerson;
            }
            if (remainingSlots > 0)
            {
                int extraRemote = Math.Min(remainingSlots, remoteList.Count - takeRemote);
                takeRemote += extraRemote;
                remainingSlots -= extraRemote;
            }

            var selected = new List<ChannelLink>(takeInPerson + takeRemote);
            var squeezedOut = new List<ChannelLink>();

            for (int i = 0; i < inPersonList.Count; i++)
            {
                if (i < takeInPerson) selected.Add(inPersonList[i].Link);
                else squeezedOut.Add(inPersonList[i].Link);
            }

            for (int i = 0; i < remoteList.Count; i++)
            {
                if (i < takeRemote) selected.Add(remoteList[i].Link);
                else squeezedOut.Add(remoteList[i].Link);
            }

            return new ChannelQuotaResult(selected, squeezedOut);
        }
    }
}
