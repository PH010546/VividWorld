using System;
using System.Collections.Generic;
using System.Linq;
using VividWorld.Core.Util;

namespace VividWorld.Core.Situations
{
    public sealed class SituationCandidate
    {
        public string SituationId = string.Empty;
        public string SettlementId = string.Empty;
        public double Weight = 1.0;
        public Dictionary<string, string> RoleHeroIds = new(StringComparer.OrdinalIgnoreCase); // 只含非 derived 角色
    }

    public sealed class SituationScanPlan
    {
        public List<SituationPick> Picked = new();      // 依挑中順序
        public List<SituationCandidate> NotPicked = new();
    }

    public sealed class SituationPick
    {
        public SituationCandidate Candidate = null!;
        public int Slot;              // 第幾個名額，0 起算
        public double Roll;           // PickWeighted 的擲值（照 IDeterministicRng 的回傳決定怎麼記）
        public double Probability;    // 這一輪它的機率，log 要印
        public double TotalWeight;    // 本輪總權重
        public int DroppedCount;      // 本輪因共用英雄被排掉的候選數
    }

    public static class SituationScanPlanner
    {
        public static SituationScanPlan Plan(
            IReadOnlyList<SituationCandidate> candidates,
            int quota,
            IDeterministicRng rng,
            Func<int, long> seedForSlot)
        {
            var plan = new SituationScanPlan();

            if (candidates == null || candidates.Count == 0 || quota <= 0)
            {
                if (candidates != null && candidates.Count > 0)
                {
                    plan.NotPicked.AddRange(candidates);
                }
                return plan;
            }

            // 1. 候選先穩定排序（SituationId → SettlementId → 角色鍵排序後的 角色=英雄 串接，全部 Ordinal）
            var pool = candidates
                .OrderBy(c => c.SituationId, StringComparer.Ordinal)
                .ThenBy(c => c.SettlementId, StringComparer.Ordinal)
                .ThenBy(c => FormatRolesKey(c.RoleHeroIds), StringComparer.Ordinal)
                .ToList();

            // 2. 逐個名額加權挑選
            for (int slot = 0; slot < quota; slot++)
            {
                if (pool.Count == 0 || pool.All(c => c.Weight <= 0.0))
                {
                    break;
                }

                var weights = pool.Select(c => Math.Max(0.0, c.Weight)).ToList();
                double totalWeight = weights.Sum();
                if (totalWeight <= 0.0)
                {
                    break;
                }

                long seed = seedForSlot(slot);
                double roll = rng.NextDouble(seed);
                int pickedIdx = rng.PickWeighted(weights, seed);
                var pickedCandidate = pool[pickedIdx];
                double prob = pickedCandidate.Weight / totalWeight;

                pool.RemoveAt(pickedIdx);

                // 3. 挑中之後，把與它共用任何一位英雄的候選全部移出候選池
                var pickedHeroes = new HashSet<string>(
                    pickedCandidate.RoleHeroIds.Values.Where(h => !string.IsNullOrEmpty(h)),
                    StringComparer.Ordinal);

                var survivingPool = new List<SituationCandidate>();
                int droppedCount = 0;

                foreach (var cand in pool)
                {
                    bool sharesHero = false;
                    foreach (var h in cand.RoleHeroIds.Values)
                    {
                        if (!string.IsNullOrEmpty(h) && pickedHeroes.Contains(h))
                        {
                            sharesHero = true;
                            break;
                        }
                    }

                    if (sharesHero)
                    {
                        droppedCount++;
                        plan.NotPicked.Add(cand);
                    }
                    else
                    {
                        survivingPool.Add(cand);
                    }
                }

                pool = survivingPool;

                plan.Picked.Add(new SituationPick
                {
                    Candidate = pickedCandidate,
                    Slot = slot,
                    Roll = roll,
                    Probability = prob,
                    TotalWeight = totalWeight,
                    DroppedCount = droppedCount
                });
            }

            // 4. 候選池剩餘候選全數加入 NotPicked
            foreach (var remaining in pool)
            {
                plan.NotPicked.Add(remaining);
            }

            return plan;
        }

        private static string FormatRolesKey(IReadOnlyDictionary<string, string>? roleHeroes)
        {
            if (roleHeroes == null || roleHeroes.Count == 0) return string.Empty;
            return string.Join(";", roleHeroes.OrderBy(kvp => kvp.Key, StringComparer.Ordinal).Select(kvp => $"{kvp.Key}={kvp.Value}"));
        }
    }
}
