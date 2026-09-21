using System;
using System.Collections.Generic;
using System.Linq;
using VividWorld.Core.Channels;
using VividWorld.Core.Config;
using VividWorld.Core.Events;
using VividWorld.Core.Rumors;

namespace VividWorld.Core.Ingest
{
    public static class Hop0Seeding
    {
        /// <summary>
        /// 這個角色的參與者會不會被種成 hop 0。KnowingRoles 為空 → 全部參與者都會。
        /// 種入、錨點、以及 log 的 hop 0 摘要都只准呼叫這一個——判斷有兩份，log 遲早會跟實際種入的名單對不上。
        /// </summary>
        public static bool IsKnowingRole(IReadOnlyCollection<string>? knowingRoles, string role)
            => knowingRoles == null || knowingRoles.Count == 0 || knowingRoles.Contains(role);

        /// <summary>決定事件的錨點英雄（第一位被種入的參與者）。若無符合角色則回傳 null。</summary>
        public static string? AnchorOf(EventSubmission submission)
        {
            if (submission?.Participants == null) return null;
            foreach (var kvp in submission.Participants)
            {
                if (string.IsNullOrEmpty(kvp.Value)) continue;
                if (IsKnowingRole(submission.KnowingRoles, kvp.Key))
                {
                    return kvp.Value;
                }
            }
            return null;
        }

        /// <summary>依規格 §10.1 第 4 步種下 hop 0 知情者。純函數，就地修改 evt.KnownBy。</summary>
        public static void Seed(WorldEvent evt,
                                EventSubmission submission,
                                IPropagationChannel channel,
                                IHeroTraitLookup traits,
                                PropagationConfig cfg,
                                string playerHeroId,
                                double day)
        {
            if (evt == null || submission == null) return;
            evt.KnownBy ??= new List<KnownByEntry>();

            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var k in evt.KnownBy)
            {
                if (!string.IsNullOrEmpty(k.HeroId))
                {
                    seen.Add(k.HeroId);
                }
            }

            string? anchorHeroId = AnchorOf(submission);

            // 1. 參與者：只有 KnowingRoles 指名的角色。KnowingRoles 為空 → 全部參與者
            if (submission.Participants != null)
            {
                foreach (var kvp in submission.Participants)
                {
                    string heroId = kvp.Value;
                    if (string.IsNullOrEmpty(heroId)) continue;

                    if (IsKnowingRole(submission.KnowingRoles, kvp.Key))
                    {
                        if (seen.Add(heroId))
                        {
                            evt.KnownBy.Add(new KnownByEntry
                            {
                                HeroId = heroId,
                                Hop = 0,
                                LearnedDay = day,
                                SourceHeroId = null
                            });
                        }
                    }
                }
            }

            // 2. ＋ InitialKnowerHeroIds 全部
            if (submission.InitialKnowerHeroIds != null)
            {
                foreach (var heroId in submission.InitialKnowerHeroIds)
                {
                    if (!string.IsNullOrEmpty(heroId))
                    {
                        if (seen.Add(heroId))
                        {
                            evt.KnownBy.Add(new KnownByEntry
                            {
                                HeroId = heroId,
                                Hop = 0,
                                LearnedDay = day,
                                SourceHeroId = null
                            });
                        }
                    }
                }
            }

            // 3. ＋ 僅當 Origin == Public 且 AutoResolveWitnesses：channel.WitnessesAt(anchorHeroId, cfg.MaxInitialWitnesses)
            // 4. Origin == Secret 時無論旗標為何都絕不加入目擊者
            if (evt.Origin == EventOrigin.Public
                && submission.AutoResolveWitnesses
                && !string.IsNullOrEmpty(anchorHeroId)
                && channel != null)
            {
                int maxWitnesses = cfg != null ? cfg.MaxInitialWitnesses : 8;
                var witnesses = channel.WitnessesAt(anchorHeroId!, maxWitnesses);
                if (witnesses != null)
                {
                    int addedWitnesses = 0;
                    foreach (var witnessHeroId in witnesses)
                    {
                        if (addedWitnesses >= maxWitnesses) break;
                        if (string.IsNullOrEmpty(witnessHeroId)) continue;

                        // 目擊者一律濾掉玩家
                        if (!string.IsNullOrEmpty(playerHeroId) && witnessHeroId == playerHeroId)
                            continue;

                        // 每個目擊者都必須通過 Eligibility.IsEligible
                        if (!Eligibility.IsEligible(traits, witnessHeroId))
                            continue;

                        if (seen.Add(witnessHeroId))
                        {
                            evt.KnownBy.Add(new KnownByEntry
                            {
                                HeroId = witnessHeroId,
                                Hop = 0,
                                LearnedDay = day,
                                SourceHeroId = null
                            });
                            addedWitnesses++;
                        }
                    }
                }
            }
        }
    }
}
