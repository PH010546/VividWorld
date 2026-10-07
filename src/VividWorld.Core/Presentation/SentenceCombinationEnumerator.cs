#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using VividWorld.Core.Catalog;
using VividWorld.Core.Config;
using VividWorld.Core.Events;
using VividWorld.Core.Rumors;

namespace VividWorld.Core.Presentation
{
    /// <summary>誰在講：旁人、從當事人那裡聽來的人、當事人自己。</summary>
    public enum SentenceAngleKind
    {
        Onlooker,
        From,
        Self,
        /// <summary>親眼看到的旁人，句型鍵接 _Witness（選用，沒有就用不帶尾巴的句子）。</summary>
        Witness
    }

    /// <summary>一個「這組碎片 × 這個講的人」需要有句子。</summary>
    public sealed class SentenceRequirement
    {
        public SentenceCombination Combination { get; }
        public SentenceAngleKind Kind { get; }
        /// <summary>角色名（大寫）；旁人為 null。</summary>
        public string? Role { get; }
        /// <summary>
        /// 選用：這個鍵沒有時，同一組碎片還有不帶尾巴的句子可退，所以不算缺少
        /// （來源是當事人的第 1 手，旁人句本來就要寫的時候）。
        /// </summary>
        public bool IsOptionalKey { get; }

        public SentenceRequirement(SentenceCombination combination, SentenceAngleKind kind, string? role, bool isOptionalKey = false)
        {
            Combination = combination;
            Kind = kind;
            Role = role;
            IsOptionalKey = isOptionalKey;
        }
    }

    /// <summary>
    /// 純函式：依事件模板與保留設定，列舉所有實際可能出現的碎片組合與講的人，
    /// 並提供句型字幹、段落與句型鍵的運算。
    /// </summary>
    public static class SentenceCombinationEnumerator
    {
        /// <summary>
        /// 種下第一批知情者時，另外把當事人的家族記成第 1 手、沒有指名來源的消息種類（模組端 <c>BanditCaptureFamilySelector</c>）。
        /// </summary>
        private static readonly HashSet<string> FamilyHearsayTypes = new(StringComparer.Ordinal) { "hero_captured_by_bandits" };

        /// <summary>消息裡已經不在人世、不會再聽到這則消息的角色（模板的 selfTell 秘密類不能宣告，所以另列）。</summary>
        public static readonly Dictionary<string, string[]> DeadRoles = new(StringComparer.Ordinal)
        {
            ["hero_murdered"] = new[] { "victim" },
            ["conduct_poisoned"] = new[] { "victim" },
            ["talk_denied_poisoned"] = new[] { "victim" }
        };

        public static bool IsDeadRole(string templateType, string role)
        {
            if (DeadRoles.TryGetValue(templateType ?? string.Empty, out var roles))
            {
                return roles.Contains(role, StringComparer.OrdinalIgnoreCase);
            }
            return false;
        }

        private static readonly HashSet<string> GenericSegments = new(StringComparer.Ordinal)
        {
            "Who", "Where", "When", "Why", "What", "Context", "Outcome"
        };

        public static string ExtractStem(string textId)
        {
            if (string.IsNullOrEmpty(textId)) return string.Empty;

            string s = textId;
            if (s.StartsWith("VividWorld_Fact_", StringComparison.Ordinal))
            {
                s = s.Substring("VividWorld_Fact_".Length);
            }
            else if (s.StartsWith("VividWorld_Sentence_", StringComparison.Ordinal))
            {
                s = s.Substring("VividWorld_Sentence_".Length);
            }

            int selfIdx = s.IndexOf("_Self_", StringComparison.Ordinal);
            if (selfIdx >= 0)
            {
                s = s.Substring(0, selfIdx);
            }

            int lastUnder = s.LastIndexOf('_');
            if (lastUnder > 0)
            {
                return s.Substring(0, lastUnder);
            }
            return s;
        }

        public static string ExtractSegment(string textId)
        {
            if (string.IsNullOrEmpty(textId)) return string.Empty;

            string s = textId;
            if (s.StartsWith("VividWorld_Fact_", StringComparison.Ordinal))
            {
                s = s.Substring("VividWorld_Fact_".Length);
            }
            else if (s.StartsWith("VividWorld_Sentence_", StringComparison.Ordinal))
            {
                s = s.Substring("VividWorld_Sentence_".Length);
            }

            int selfIdx = s.IndexOf("_Self_", StringComparison.Ordinal);
            if (selfIdx >= 0)
            {
                s = s.Substring(0, selfIdx);
            }

            int lastUnder = s.LastIndexOf('_');
            if (lastUnder >= 0 && lastUnder < s.Length - 1)
            {
                return s.Substring(lastUnder + 1);
            }
            return s;
        }

        /// <summary>整句句型鍵：<c>VividWorld_Sentence_字幹_段…</c>，當事人自己講再接 <c>_Self_角色</c>。</summary>
        public static string ComputeSentenceKey(string stem, IEnumerable<string> segments, string? roleUpper = null)
        {
            string segs = string.Join("_", segments);
            string key = string.IsNullOrEmpty(segs)
                ? $"VividWorld_Sentence_{stem}"
                : $"VividWorld_Sentence_{stem}_{segs}";

            if (!string.IsNullOrEmpty(roleUpper))
            {
                key += $"_Self_{roleUpper}";
            }
            return key;
        }

        /// <summary>說話的人是從當事人（<paramref name="roleUpper"/>）那裡聽來的：句型鍵接 <c>_From_角色</c>。</summary>
        public static string ComputeFromSentenceKey(string stem, IEnumerable<string> segments, string roleUpper)
        {
            string segs = string.Join("_", segments);
            string key = string.IsNullOrEmpty(segs)
                ? $"VividWorld_Sentence_{stem}"
                : $"VividWorld_Sentence_{stem}_{segs}";

            return $"{key}_From_{roleUpper}";
        }

        /// <summary>
        /// 說話的人是親眼看到的旁人：句型鍵接 <c>_Witness</c>。同一組碎片第 1 手以後也會用到時，
        /// 親眼看到的人只講看得到的那一幕、看不到的部分用「聽說」帶出，跟聽來的人講法不同，所以另寫一句；
        /// 沒有這個鍵就用不帶尾巴的那一句。
        /// </summary>
        public static string ComputeWitnessSentenceKey(string stem, IEnumerable<string> segments)
        {
            return ComputeSentenceKey(stem, segments, null) + "_Witness";
        }

        /// <summary>當事人的句尾鍵（一種消息的一個角色一句）。</summary>
        public static string ComputeSelfFeelingKey(string stem, string roleUpper)
        {
            return $"VividWorld_SelfFeeling_{stem}_{roleUpper}";
        }

        /// <summary>
        /// 句尾鍵的候選（由具體到一般）：碎片裡有專屬於某個變體的段（例：放人的原因 <c>Ransom</c>）時，
        /// 先查帶那一段的鍵，查不到再用一般的鍵。同一種消息各變體的句尾相同時，只寫一般的那一句。
        /// </summary>
        public static IReadOnlyList<string> ComputeSelfFeelingKeys(string stem, IEnumerable<string> segments, string roleUpper)
        {
            var keys = new List<string>();
            foreach (var seg in segments.Reverse())
            {
                if (!string.IsNullOrEmpty(seg) && !GenericSegments.Contains(seg))
                {
                    keys.Add($"VividWorld_SelfFeeling_{stem}_{seg}_{roleUpper}");
                }
            }
            keys.Add(ComputeSelfFeelingKey(stem, roleUpper));
            return keys;
        }

        /// <summary>
        /// 所有實際會出現的碎片組合（不分講的人）。
        /// 每種手數的保留結果直接問實際的保留規則，沒有另寫一份；再加上「可省略碎片不在」的版本。
        /// 兩個版本剩下同一組碎片時合併成一筆，出現的手數取聯集。
        /// </summary>
        public static IReadOnlyList<SentenceCombination> EnumerateCombinations(EventTemplate tmpl, RetentionConfig? retention = null)
        {
            var merged = new List<SentenceCombination>();
            var index = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var (_, combos) in EnumerateVersions(tmpl, retention))
            {
                foreach (var comb in combos)
                {
                    if (index.TryGetValue(comb.CombinationKey, out int at))
                    {
                        var old = merged[at];
                        var hops = old.Hops.Union(comb.Hops).OrderBy(h => h).ToList();
                        merged[at] = new SentenceCombination(old.CombinationKey, old.Facts, old.IsMissingOptional, hops[0], hops);
                    }
                    else
                    {
                        index[comb.CombinationKey] = merged.Count;
                        merged.Add(comb);
                    }
                }
            }
            return merged;
        }

        private static List<(bool HasPlace, List<SentenceCombination> Combinations)> EnumerateVersions(EventTemplate tmpl, RetentionConfig? retention)
        {
            var versions = new List<(bool, List<SentenceCombination>)>();
            if (tmpl == null || tmpl.Facts == null || tmpl.Facts.Count == 0)
            {
                return versions;
            }

            retention ??= new RetentionConfig();
            int drama = tmpl.DramaWeight ?? 3;
            int hopCount = retention.HopThresholds != null && retention.HopThresholds.Length > 0
                ? retention.HopThresholds.Length
                : 1;
            var policy = new ThresholdRetentionPolicy(retention);

            var factLists = new List<(List<TemplateFact> Facts, bool IsMissingOptional)> { (tmpl.Facts.ToList(), false) };
            if (tmpl.Facts.Any(f => f.Optional))
            {
                var withoutOptional = tmpl.Facts.Where(f => !f.Optional).ToList();
                if (withoutOptional.Count > 0 && withoutOptional.Count < tmpl.Facts.Count)
                {
                    factLists.Add((withoutOptional, true));
                }
            }

            foreach (var (initialFacts, isMissingOptional) in factLists)
            {
                var dummyEvt = new WorldEvent
                {
                    DramaWeight = drama,
                    DramaScale = tmpl.DramaScale,
                    Facts = initialFacts.Select(tf => new Fact
                    {
                        Id = tf.Id,
                        Category = tf.Category,
                        TextId = tf.TextId ?? string.Empty,
                        Text = tf.Text,
                        Vars = tf.Vars != null ? new Dictionary<string, string>(tf.Vars) : new Dictionary<string, string>(),
                        Fragility = tf.Fragility
                    }).ToList()
                };

                var order = new List<string>();
                var byKey = new Dictionary<string, (List<TemplateFact> Facts, List<int> Hops)>(StringComparer.Ordinal);
                for (int h = 0; h < hopCount; h++)
                {
                    var retained = policy.Retain(dummyEvt, h, string.Empty);
                    var keptIds = new HashSet<string>(retained.Select(rf => rf.Id), StringComparer.Ordinal);
                    var keptFacts = initialFacts.Where(f => keptIds.Contains(f.Id)).ToList();

                    string combKey = string.Join("_", keptFacts.Select(f => ExtractSegment(f.TextId ?? string.Empty)));
                    if (!byKey.TryGetValue(combKey, out var entry))
                    {
                        entry = (keptFacts, new List<int>());
                        byKey[combKey] = entry;
                        order.Add(combKey);
                    }
                    entry.Hops.Add(h);
                }

                var combos = order
                    .Select(k => new SentenceCombination(k, byKey[k].Facts, isMissingOptional, byKey[k].Hops[0], byKey[k].Hops))
                    .ToList();
                bool hasPlace = initialFacts.Any(f => f.Category == FactCategory.Where);
                versions.Add((hasPlace, combos));
            }

            return versions;
        }

        /// <summary>
        /// 這種消息實際會需要句子的（碎片組合 × 講的人）。
        /// 講的人分四種：
        /// 當事人自己（第 0 手；秘密消息裡不知情的當事人則是第 1 手起聽到之後）、
        /// 現場旁人（第 0 手，要有地點、公開消息、且不是「同地者算聽來的」那幾種）、
        /// 從當事人那裡聽來的人（第 1 手，每個會講的當事人各一種）、
        /// 其餘的旁人（同地者、家族聽到消息的人、第 2 手起）。
        /// 當事人不會講的角色（<c>selfTell: never</c>）不列；沒有任何人可能知道的版本（例：過世的人沒有地點）不列。
        /// </summary>
        public static IReadOnlyList<SentenceRequirement> EnumerateRequirements(EventTemplate tmpl, RetentionConfig? retention = null, int gistExtraHops = 2)
        {
            // 當事人（第 0 手）好感不夠時只講大概：用多降幾手的詳細程度挑碎片，
            // 落點＝min(1＋多降的手數, max(1, 這種消息最遠傳幾手))，所以要替那幾手的碎片組也寫當事人句
            var gistLandingHops = new HashSet<int>();
            foreach (int maxHop in new PropagationConfig().DramaMaxHop)
            {
                gistLandingHops.Add(Math.Min(1 + Math.Max(0, gistExtraHops), Math.Max(1, maxHop)));
            }

            var found = new Dictionary<string, SentenceRequirement>(StringComparer.Ordinal);
            var order = new List<string>();
            if (tmpl == null) return Array.Empty<SentenceRequirement>();

            bool secret = tmpl.Origin == EventOrigin.Secret;
            var roleNames = tmpl.Roles?.Keys.ToList() ?? new List<string>();
            var tellers = roleNames
                .Where(r => !IsNeverTell(tmpl, r) && (!secret || (tmpl.KnowingRoles != null && tmpl.KnowingRoles.Contains(r))))
                .Select(r => r.ToUpperInvariant())
                .ToList();
            var dead = DeadRoles.TryGetValue(tmpl.Type ?? string.Empty, out var deadRoles) ? deadRoles : Array.Empty<string>();
            var lateLearners = secret
                ? roleNames
                    .Where(r => !IsNeverTell(tmpl, r) && !dead.Contains(r) && (tmpl.KnowingRoles == null || !tmpl.KnowingRoles.Contains(r)))
                    .Select(r => r.ToUpperInvariant())
                    .ToList()
                : new List<string>();
            bool familyHearsay = FamilyHearsayTypes.Contains(tmpl.Type ?? string.Empty);

            void Add(SentenceCombination comb, SentenceAngleKind kind, string? role, bool optional)
            {
                string key = $"{comb.CombinationKey}|{kind}|{role}";
                if (found.TryGetValue(key, out var existing))
                {
                    if (existing.IsOptionalKey && !optional)
                    {
                        found[key] = new SentenceRequirement(existing.Combination, kind, role, false);
                    }
                    return;
                }
                found[key] = new SentenceRequirement(comb, kind, role, optional);
                order.Add(key);
            }

            foreach (var (hasPlace, combos) in EnumerateVersions(tmpl, retention))
            {
                bool witnessPossible;
                if (string.Equals(tmpl.WitnessSource, "none", StringComparison.OrdinalIgnoreCase))
                {
                    witnessPossible = false;
                }
                else if (string.Equals(tmpl.WitnessSource, "triggerCaptorArmy", StringComparison.OrdinalIgnoreCase))
                {
                    witnessPossible = !secret;
                }
                else
                {
                    witnessPossible = !secret && !tmpl.ColocatedWitnessAsHearsay && hasPlace;
                }
                bool hearsaySeed = (!secret && tmpl.ColocatedWitnessAsHearsay && hasPlace) || familyHearsay || tmpl.MadeUpHearsay;
                if (tellers.Count == 0 && !witnessPossible && !hearsaySeed)
                {
                    continue;
                }

                bool isResponse = !string.IsNullOrEmpty(tmpl.Response);
                foreach (var comb in combos)
                {
                    // 秘密的當事人交情不夠時不講自己的秘密，所以秘密沒有只講大概的當事人句；回應類別也沒有當事人大概句
                    if (comb.OccursAtHop0 || (!secret && !isResponse && comb.Hops.Any(gistLandingHops.Contains)))
                    {
                        foreach (var role in tellers) Add(comb, SentenceAngleKind.Self, role, false);
                    }

                    if (comb.OccursAtHop0)
                    {
                        if (witnessPossible)
                        {
                            Add(comb, SentenceAngleKind.Onlooker, null, false);
                            // 同一組碎片第 1 手以後也會用到時，親眼看到的人可以另有一句（選用）
                            if (comb.OccursAfterHop0) Add(comb, SentenceAngleKind.Witness, null, true);
                        }
                    }

                    if (comb.OccursAfterHop0)
                    {
                        bool plainNeeded = comb.OccursAtHop2OrLater
                            || (comb.OccursAtHop1 && (witnessPossible || hearsaySeed));
                        if (comb.OccursAtHop1)
                        {
                            foreach (var role in tellers) Add(comb, SentenceAngleKind.From, role, plainNeeded);
                        }
                        if (plainNeeded) Add(comb, SentenceAngleKind.Onlooker, null, false);
                        foreach (var role in lateLearners) Add(comb, SentenceAngleKind.Self, role, false);
                    }
                }
            }

            return order.Select(k => found[k]).ToList();
        }

        private static bool IsNeverTell(EventTemplate tmpl, string role)
        {
            return tmpl.SelfTell != null
                && tmpl.SelfTell.TryGetValue(role, out var rule)
                && rule != null
                && rule.IsNever;
        }

        /// <summary>舊介面：旁人（null）加上每個會自己講的角色。新程式用 <see cref="EnumerateRequirements"/>。</summary>
        public static IReadOnlyList<string?> EnumerateAngles(EventTemplate tmpl)
        {
            var angles = new List<string?> { null };

            if (tmpl?.Roles != null)
            {
                foreach (var role in tmpl.Roles.Keys)
                {
                    if (IsNeverTell(tmpl, role)) continue;
                    if (tmpl.Origin == EventOrigin.Secret && tmpl.KnowingRoles != null && !tmpl.KnowingRoles.Contains(role))
                    {
                        continue;
                    }
                    angles.Add(role.ToUpperInvariant());
                }
            }

            return angles;
        }

        public static EventTemplate CreateWhoNoCaptorVariant(EventTemplate baseTemplate)
            => TemplateVariants.EscapeWithoutCaptor(baseTemplate);
    }
}
