using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using VividWorld.Core.Catalog;
using VividWorld.Core.Config;
using VividWorld.Core.Events;
using VividWorld.Core.Grudges;
using VividWorld.Core.Memory;
using VividWorld.Core.Rumors;
using VividWorld.Core.Util;

namespace VividWorld.Core.Feelings
{
    /// <summary>算感想需要從遊戲讀的資料。遊戲那一側實作，Core 不碰遊戲組件。</summary>
    public interface IFeelingWorld
    {
        /// <summary>今天（遊戲曆的天數）。恩怨照這一天重播淡化。</summary>
        double Today { get; }

        /// <summary>說話的人對這位英雄的個人好感度；任一方查不到英雄時為 null。</summary>
        int? Affection(string speakerId, string heroId);

        /// <summary>地位序：國王 4、族長 3、領主 2、流浪者 1、其他 0；查不到英雄為 null。</summary>
        int? StandingRank(string heroId);

        /// <summary>興趣計算用的親屬與氏族資料；查不到英雄為 null。</summary>
        InterestHeroFacts? InterestFacts(string heroId);

        /// <summary>說話的人對這位英雄、個人層級的恩怨紀錄（尚未淡化）。</summary>
        IReadOnlyList<GrudgeEntry> PersonalGrudges(string speakerId, string heroId);

        /// <summary>日誌用的名字；查不到時回傳 id。</summary>
        string NameOf(string heroId);

        /// <summary>性別：女 true、男 false、查不到為 null。</summary>
        bool? IsFemale(string heroId);
    }

    /// <summary>一次感想判定的結果與理由。<see cref="LineKey"/> 為 null 表示沒有感想，理由在 <see cref="Reason"/>。</summary>
    public sealed class FeelingDecision
    {
        public string SpeakerId = string.Empty;
        public string EventId = string.Empty;

        /// <summary>產生了感想的那一句字串鍵；沒產生為 null。</summary>
        public string? LineKey;

        /// <summary>稱呼字串鍵（句子裡有 <c>{ADDRESS}</c> 時才會用到）。</summary>
        public string? AddressKey;

        public string? FocusHeroId;
        public string? FocusRole;
        public bool? FocusIsFemale;
        public string? Category;
        public FeelingMood? Mood;

        /// <summary>沒有感想的原因；有感想時為空。</summary>
        public string Reason = string.Empty;

        /// <summary>一行日誌：焦點人物與為什麼是他、好感、恩怨、地位、類別、心情、候選與挑中的那一句，或沒產生的原因。</summary>
        public string LogLine = string.Empty;

        public bool Applied => LineKey != null;
    }

    /// <summary>一次好感與恩怨心情計算的結果。</summary>
    public sealed class MoodResolution
    {
        public int? AffectionRaw { get; set; }
        public int Affection { get; set; }
        public AffectionLevel AffectionLevel { get; set; }
        public string AffectionText { get; set; } = string.Empty;
        public double GrudgeNet { get; set; }
        public int KeptGrudgeCount { get; set; }
        public int ExcludedGrudgeCount { get; set; }
        public double ExcludedGrudgePoints { get; set; }
        public GrudgeLevel GrudgeLevel { get; set; }
        public string GrudgeText { get; set; } = string.Empty;
        public FeelingMood Mood { get; set; }
        public TraitProfile? SpeakerTraits { get; set; }
    }

    public sealed class FeelingResolver
    {
        private readonly VividWorldConfig _config;
        private readonly FeelingCatalog _catalog;
        private readonly IFeelingWorld _world;
        private readonly IHeroTraitLookup? _traits;
        private readonly Func<string, EventTemplate?>? _getTemplate;
        private readonly long _campaignSeed;

        public FeelingResolver(VividWorldConfig config, FeelingCatalog catalog, IFeelingWorld world,
                               IHeroTraitLookup? traits, Func<string, EventTemplate?>? getTemplate, long campaignSeed)
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));
            _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
            _world = world ?? throw new ArgumentNullException(nameof(world));
            _traits = traits;
            _getTemplate = getTemplate;
            _campaignSeed = campaignSeed;
        }

        /// <summary>
        /// 好感與恩怨心情的共用計算：好感讀 <see cref="IFeelingWorld.Affection"/>，
        /// 恩怨排除指定事件自己造成的幾筆，其餘重播淡化到今天。
        /// </summary>
        public static MoodResolution ComputeMood(
            IFeelingWorld world,
            string speakerId,
            string targetHeroId,
            string? eventId,
            IHeroTraitLookup? traits,
            FeelingsConfig fc,
            SituationsConfig situations,
            double today)
        {
            if (world == null) throw new ArgumentNullException(nameof(world));
            fc ??= new FeelingsConfig();
            situations ??= new SituationsConfig();

            int? affectionRaw = world.Affection(speakerId, targetHeroId);
            int affection = affectionRaw ?? 0;
            var affectionLevel = FeelingGrid.Classify(affection, fc);
            string affectionText = string.Format(CultureInfo.InvariantCulture,
                "affection {0}{1:+0;-0;0} -> {2} (high >= {3}, low <= {4})",
                affectionRaw.HasValue ? string.Empty : "unreadable, counted as ", affection, affectionLevel, fc.AffectionHigh, fc.AffectionLow);

            var all = world.PersonalGrudges(speakerId, targetHeroId) ?? Array.Empty<GrudgeEntry>();
            var excluded = all.Where(e => !string.IsNullOrEmpty(eventId) && string.Equals(e.EventId, eventId, StringComparison.Ordinal)).ToList();
            var kept = all.Where(e => string.IsNullOrEmpty(eventId) || !string.Equals(e.EventId, eventId, StringComparison.Ordinal)).ToList();
            var speakerTraits = traits?.Of(speakerId);
            var replay = GrudgeDecay.Replay(kept, GrudgeScope.Personal, situations, speakerTraits, today);
            var grudgeLevel = FeelingGrid.Classify(replay.Value, fc);
            string grudgeText = string.Format(CultureInfo.InvariantCulture,
                "grudge net {0:+0.##;-0.##;0} over {1} entr{2} (excluded {3} from this event, {4:+0.##;-0.##;0} points) -> {5} (threshold {6:0.##})",
                replay.Value, kept.Count, kept.Count == 1 ? "y" : "ies", excluded.Count, excluded.Sum(e => e.Requested), grudgeLevel, fc.GrudgeThreshold);

            var mood = FeelingGrid.MoodOf(affectionLevel, grudgeLevel);

            return new MoodResolution
            {
                AffectionRaw = affectionRaw,
                Affection = affection,
                AffectionLevel = affectionLevel,
                AffectionText = affectionText,
                GrudgeNet = replay.Value,
                KeptGrudgeCount = kept.Count,
                ExcludedGrudgeCount = excluded.Count,
                ExcludedGrudgePoints = excluded.Sum(e => e.Requested),
                GrudgeLevel = grudgeLevel,
                GrudgeText = grudgeText,
                Mood = mood,
                SpeakerTraits = speakerTraits
            };
        }

        /// <summary>
        /// 稱呼的共用計算：兩人的地位比較 × 說話者對目標的好感等級 → FeelingGrid.AddressKey。
        /// 給感想句與打探回答（ProbeClassifier）共用。
        /// </summary>
        public static string ResolveAddressKey(IFeelingWorld? world, string speakerId, string targetHeroId, AffectionLevel affectionLevel)
        {
            if (world == null) return FeelingGrid.AddressKey(StandingComparison.Equal, affectionLevel);
            int speakerRank = world.StandingRank(speakerId) ?? 0;
            int focusRank = world.StandingRank(targetHeroId) ?? 0;
            var standing = FeelingGrid.Compare(focusRank, speakerRank);
            return FeelingGrid.AddressKey(standing, affectionLevel);
        }

        public static string ResolveAddressKey(IFeelingWorld? world, string speakerId, string targetHeroId, FeelingsConfig? fc = null)
        {
            fc ??= new FeelingsConfig();
            int affection = (world != null ? world.Affection(speakerId, targetHeroId) : null) ?? 0;
            var affectionLevel = FeelingGrid.Classify(affection, fc);
            return ResolveAddressKey(world, speakerId, targetHeroId, affectionLevel);
        }

        /// <summary>模板裡這個角色的感想類別：事件帶著覆寫指定的碎片就用覆寫，否則用 <c>feelings</c> 欄位；都沒有回 null。</summary>
        public static string? CategoryFor(EventTemplate? template, WorldEvent evt, string role, out string how)
        {
            how = "no template";
            if (template == null) return null;

            if (template.FeelingOverrides != null && evt?.Facts != null)
            {
                foreach (var ov in template.FeelingOverrides)
                {
                    if (!string.Equals(ov.Role, role, StringComparison.OrdinalIgnoreCase)) continue;
                    if (evt.Facts.Any(f => string.Equals(f.TextId, ov.WhenFact, StringComparison.Ordinal)))
                    {
                        how = $"override: the event carries '{ov.WhenFact}'";
                        return ov.Category;
                    }
                }
            }

            if (template.Feelings != null && template.Feelings.TryGetValue(role, out var cat) && !string.IsNullOrEmpty(cat))
            {
                how = "template feelings";
                return cat;
            }

            how = $"role '{role}' has no feeling category in the template";
            return null;
        }

        /// <param name="isGist">交情不夠、只講大概：不附感想。</param>
        public FeelingDecision Resolve(WorldEvent evt, string speakerId, bool isGist)
        {
            string speaker = speakerId ?? string.Empty;
            var d = new FeelingDecision { SpeakerId = speaker, EventId = evt?.EventId ?? string.Empty };
            if (evt == null || string.IsNullOrEmpty(speaker))
            {
                return None(d, evt, "no event or no speaker");
            }

            var fc = _config.Presentation?.Feelings ?? new FeelingsConfig();
            if (!fc.Enabled) return None(d, evt, "feelings are switched off (presentation.feelings.enabled is false)");
            if (isGist) return None(d, evt, "only told the gist, so the speaker adds no feeling");
            if (evt.RoleOf(speaker!) != null) return None(d, evt, "the speaker is one of the people this happened to");

            var entry = evt.EntryFor(speaker!);
            if (entry != null && entry.Believes == false)
            {
                var disbeliefKeys = new[]
                {
                    "VividWorld_FeelingDisbelief_1",
                    "VividWorld_FeelingDisbelief_2",
                    "VividWorld_FeelingDisbelief_3"
                };
                long h = RumorSeed.Of(_campaignSeed, evt.EventId, speaker, "disbelief");
                int roll = (int)((ulong)h % (ulong)disbeliefKeys.Length);
                d.LineKey = disbeliefKeys[roll];
                d.AddressKey = null;
                d.Category = "disbelief";
                d.Mood = null;
                d.LogLine = string.Format(CultureInfo.InvariantCulture,
                    "Feeling: {0} on {1} ({2}): speaker does not believe this rumor; category disbelief, mood none; candidates {3}; chose {4} - seeded roll picked {5} of {6}",
                    Who(speaker), evt.EventId, evt.Type, string.Join(", ", disbeliefKeys), d.LineKey, roll + 1, disbeliefKeys.Length);
                return d;
            }

            var template = _getTemplate?.Invoke(evt.Type);
            if (template == null) return None(d, evt, $"no template for event type '{evt.Type}'");

            // 焦點人物：跟興趣計算挑「最在意的當事人」同一套規則
            var participants = new List<InterestParticipant>();
            foreach (var kvp in evt.Participants ?? new Dictionary<string, string>())
            {
                int? rel = string.Equals(kvp.Value, speaker, StringComparison.Ordinal) ? null : _world.Affection(speaker, kvp.Value);
                participants.Add(new InterestParticipant
                {
                    Role = kvp.Key,
                    HeroId = kvp.Value,
                    Facts = _world.InterestFacts(kvp.Value),
                    PersonalRelation = rel
                });
            }
            var interest = InterestCalculator.Compute(_world.InterestFacts(speaker), speaker, participants, evt.DramaBand, _config.Memory);
            if (string.IsNullOrEmpty(interest.BestHeroId))
            {
                return None(d, evt, "no focus hero: the event has no participants");
            }

            d.FocusHeroId = interest.BestHeroId;
            d.FocusRole = interest.BestRole;
            bool? focusFemale = _world.IsFemale(interest.BestHeroId) ?? _traits?.Of(interest.BestHeroId)?.IsFemale;
            d.FocusIsFemale = focusFemale;
            string genderStr = focusFemale == true ? "female" : (focusFemale == false ? "male" : "unknown");
            string focusWhy = $"focus hero {Who(interest.BestHeroId)} as {interest.BestRole} ({interest.InterestSource}), gender {genderStr}";

            string? category = CategoryFor(template, evt, interest.BestRole, out string categoryHow);
            if (category == null)
            {
                return None(d, evt, $"{focusWhy}; no feeling: {categoryHow}");
            }
            d.Category = category;

            // 好感與恩怨心情
            var moodRes = ComputeMood(_world, speaker, interest.BestHeroId, evt.EventId, _traits, fc, _config.Situations, _world.Today);
            int affection = moodRes.Affection;
            var affectionLevel = moodRes.AffectionLevel;
            string affectionText = moodRes.AffectionText;
            string grudgeText = moodRes.GrudgeText;
            var mood = moodRes.Mood;
            d.Mood = mood;
            var speakerTraits = moodRes.SpeakerTraits;

            // 稱呼
            d.AddressKey = ResolveAddressKey(_world, speaker, interest.BestHeroId, affectionLevel);
            int speakerRank = _world.StandingRank(speaker) ?? 0;
            int focusRank = _world.StandingRank(interest.BestHeroId) ?? 0;
            var standing = FeelingGrid.Compare(focusRank, speakerRank);
            string standingText = $"standing: focus rank {focusRank} vs speaker rank {speakerRank} -> {standing}, address {d.AddressKey}";

            // 挑句
            var lines = _catalog.Lines(category, mood);
            if (lines.Count == 0)
            {
                d.AddressKey = null;
                return None(d, evt, $"{focusWhy}; {affectionText}; {grudgeText}; no lines in the feeling catalog for {category}/{FeelingGrid.MoodId(mood)}");
            }

            var scores = lines.Select(l => l.Score(speakerTraits)).ToList();
            int best = scores.Max();
            var tied = new List<int>();
            for (int i = 0; i < lines.Count; i++) if (scores[i] == best) tied.Add(i);

            int chosen;
            string chosenWhy;
            if (tied.Count == 1)
            {
                chosen = tied[0];
                chosenWhy = best > 0
                    ? $"trait match {lines[chosen].TagText} (speaker {lines[chosen].Trait} {FeelingLine.ValueOf(speakerTraits!, lines[chosen].Trait!):+0;-0;0})"
                    : "the other candidate's trait tag points the opposite way";
            }
            else
            {
                long h = RumorSeed.Of(_campaignSeed, "feeling", speaker, evt.EventId);
                int roll = (int)((ulong)h % (ulong)tied.Count);
                chosen = tied[roll];
                chosenWhy = best > 0
                    ? $"{tied.Count} lines match the speaker's traits equally, seeded roll picked {roll + 1} of {tied.Count}"
                    : $"no trait preference between {tied.Count} lines, seeded roll picked {roll + 1} of {tied.Count}";
            }

            d.LineKey = lines[chosen].Key;
            string candidates = string.Join(", ", lines.Select((l, i) => l.Key + (l.Trait != null ? " [" + l.TagText + "]" : string.Empty)));
            d.LogLine = string.Format(CultureInfo.InvariantCulture,
                "Feeling: {0} on {1} ({2}): {3}; {4}; {5}; {6}; category {7}, mood {8}; candidates {9}; chose {10} - {11}",
                Who(speaker), evt.EventId, evt.Type, focusWhy, affectionText, grudgeText, standingText,
                category, FeelingGrid.MoodId(mood), candidates, d.LineKey, chosenWhy);
            return d;
        }

        private FeelingDecision None(FeelingDecision d, WorldEvent? evt, string reason)
        {
            d.Reason = reason;
            d.LogLine = string.Format(CultureInfo.InvariantCulture,
                "Feeling: none for {0} on {1} ({2}): {3}",
                string.IsNullOrEmpty(d.SpeakerId) ? "(no speaker)" : Who(d.SpeakerId), d.EventId, evt?.Type ?? "?", reason);
            return d;
        }

        private string Who(string heroId)
        {
            string name = _world.NameOf(heroId);
            return string.Equals(name, heroId, StringComparison.Ordinal) ? heroId : $"{name} ({heroId})";
        }
    }
}
