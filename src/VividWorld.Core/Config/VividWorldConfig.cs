using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using VividWorld.Core.Diagnostics;

namespace VividWorld.Core.Config
{
    public sealed class VividWorldConfig
    {
        public int ConfigVersion { get; set; } = 1;
        public bool Enabled { get; set; } = true;
        public string LogLevel { get; set; } = "Info";

        public RetentionConfig     Retention     { get; set; } = new();
        public PropagationConfig   Propagation   { get; set; } = new();
        public RelationConfig      Relation      { get; set; } = new();
        public LeakConfig          Leak          { get; set; } = new();
        public SchedulingConfig    Scheduling    { get; set; } = new();
        public DialogueConfig      Dialogue      { get; set; } = new();
        public PresentationConfig  Presentation  { get; set; } = new();
        public PersistenceConfig   Persistence   { get; set; } = new();
        public ConsequenceConfig   Consequences  { get; set; } = new();
        public EmbellishmentConfig Embellishment { get; set; } = new();
        public EventsConfig        Events        { get; set; } = new();
        public SituationsConfig    Situations    { get; set; } = new();
        public MemoryConfig        Memory        { get; set; } = new();
        public DebugConfig         Debug         { get; set; } = new();

        [JsonExtensionData]
        public IDictionary<string, JToken> Extra { get; set; } = new Dictionary<string, JToken>();

        /// <summary>純函數：夾住每個數值到合法範圍、以重複末元素補齊過短陣列、
        /// 未知的 policy 字串退回 "Threshold"。回傳自身以便串接。</summary>
        public VividWorldConfig Normalize(IList<ClampNotice>? notices = null)
        {
            Retention ??= new RetentionConfig();
            Propagation ??= new PropagationConfig();
            Relation ??= new RelationConfig();
            Leak ??= new LeakConfig();
            Scheduling ??= new SchedulingConfig();
            Dialogue ??= new DialogueConfig();
            Presentation ??= new PresentationConfig();
            Persistence ??= new PersistenceConfig();
            Consequences ??= new ConsequenceConfig();
            Embellishment ??= new EmbellishmentConfig();
            Events ??= new EventsConfig();
            Events.Sources ??= new EventSourcesConfig();
            Events.PrisonerDramaByProminence ??= new ProminenceDramaConfig();
            Events.ReleaseDramaByProminence ??= new ProminenceDramaConfig
            {
                Ruler = 4,
                ClanLeader = 3,
                NobleMember = 1,
                Minor = 1
            };
            Situations ??= new SituationsConfig();
            Situations.Contest ??= new SituationContestConfig();
            Memory ??= new MemoryConfig();
            Memory.InterestFloors ??= new InterestFloorsConfig();
            Debug ??= new DebugConfig();

            Events.CatalogFile = string.IsNullOrWhiteSpace(Events.CatalogFile) ? "vividworld_events.json" : Events.CatalogFile.Trim();
            Situations.CatalogFile = string.IsNullOrWhiteSpace(Situations.CatalogFile) ? "vividworld_situations.json" : Situations.CatalogFile.Trim();
            Situations.EventCatalogFile = string.IsNullOrWhiteSpace(Situations.EventCatalogFile) ? "vividworld_situation_events.json" : Situations.EventCatalogFile.Trim();

            // Persistence
            Persistence.ShardDays = Math.Max(1, Math.Min(10000, Persistence.ShardDays));
            int origMaxSnapshots = Persistence.MaxSnapshots;
            Persistence.MaxSnapshots = Math.Max(0, Math.Min(64, origMaxSnapshots));   // 0 = 不限制（SNAP1）
            if (Persistence.MaxSnapshots != origMaxSnapshots && notices != null)
            {
                notices.Add(new ClampNotice
                {
                    Key = "persistence.maxSnapshots",
                    Requested = origMaxSnapshots,
                    Applied = Persistence.MaxSnapshots,
                    AllowedRange = "0..64"
                });
            }
            Persistence.MaxFactsPerEvent = Math.Max(1, Persistence.MaxFactsPerEvent);
            Persistence.MaxPendingIngestChars = Math.Max(0, Math.Min(31000, Persistence.MaxPendingIngestChars));

            int origIdleFlushes = Persistence.ShardCacheIdleFlushes;
            Persistence.ShardCacheIdleFlushes = Math.Max(0, Math.Min(1000, origIdleFlushes));
            if (Persistence.ShardCacheIdleFlushes != origIdleFlushes && notices != null)
            {
                notices.Add(new ClampNotice
                {
                    Key = "persistence.shardCacheIdleFlushes",
                    Requested = origIdleFlushes,
                    Applied = Persistence.ShardCacheIdleFlushes,
                    AllowedRange = "0..1000"
                });
            }

            // Retention
            if (string.Equals(Retention.Policy, "Probabilistic", StringComparison.OrdinalIgnoreCase))
            {
                Retention.Policy = "Probabilistic";
            }
            else
            {
                Retention.Policy = "Threshold";
            }

            if (Retention.HopThresholds == null || Retention.HopThresholds.Length == 0)
            {
                Retention.HopThresholds = new[] { 5, 3, 2, 2, 1, 1 };   // 與 RetentionConfig 的預設同步（帳本 L-17）
            }
            for (int i = 0; i < Retention.HopThresholds.Length; i++)
            {
                Retention.HopThresholds[i] = Math.Max(1, Math.Min(5, Retention.HopThresholds[i]));
            }

            Retention.Softness = Math.Max(0.001, Math.Min(5.0, Retention.Softness));
            Retention.AlwaysKeepAtOrBelowFragility = Math.Max(0, Math.Min(5, Retention.AlwaysKeepAtOrBelowFragility));
            Retention.MinFactsRetained = Math.Max(0, Math.Min(Persistence.MaxFactsPerEvent, Retention.MinFactsRetained));

            Retention.DramaThresholdShift = PadOrTruncate(
                Retention.DramaThresholdShift,
                new[] { 0, 0, 0, 0, 0 },
                5,
                -10, 10);

            // Propagation
            Propagation.BaseTellChancePerContact = Math.Max(0.0, Math.Min(1.0, Propagation.BaseTellChancePerContact));
            Propagation.SelfIncriminationMultiplier = Math.Max(0.0, Math.Min(10.0, Propagation.SelfIncriminationMultiplier));
            Propagation.TellToSubjectMultiplier = Math.Max(0.0, Math.Min(10.0, Propagation.TellToSubjectMultiplier));

            Propagation.ChannelWeights ??= new ChannelWeights();
            Propagation.ChannelWeights.SameParty      = ClampTracked("propagation.channelWeights.sameParty", Propagation.ChannelWeights.SameParty, 0.0, 10.0, notices);
            Propagation.ChannelWeights.SameArmy       = ClampTracked("propagation.channelWeights.sameArmy", Propagation.ChannelWeights.SameArmy, 0.0, 10.0, notices);
            Propagation.ChannelWeights.SameSettlement = ClampTracked("propagation.channelWeights.sameSettlement", Propagation.ChannelWeights.SameSettlement, 0.0, 10.0, notices);
            Propagation.ChannelWeights.SameClan       = ClampTracked("propagation.channelWeights.sameClan", Propagation.ChannelWeights.SameClan, 0.0, 10.0, notices);
            Propagation.ChannelWeights.KinAbroad      = ClampTracked("propagation.channelWeights.kinAbroad", Propagation.ChannelWeights.KinAbroad, 0.0, 10.0, notices);
            Propagation.ChannelWeights.Kingdom        = ClampTracked("propagation.channelWeights.kingdom", Propagation.ChannelWeights.Kingdom, 0.0, 10.0, notices);

            Propagation.DramaTellMultiplier = PadOrTruncate(
                Propagation.DramaTellMultiplier,
                new[] { 0.35, 0.6, 1.0, 1.5, 2.2 },
                5,
                0.0, 10.0);

            Propagation.DramaMaxHop = PadOrTruncate(
                Propagation.DramaMaxHop,
                new[] { 2, 3, 4, 5, 6 },
                5,
                0, 20);

            Propagation.TopicDramaWeight = PadOrTruncate(
                Propagation.TopicDramaWeight,
                new[] { 0.35, 0.6, 1.0, 1.5, 2.2 },
                5,
                0.0, 10.0);

            Propagation.TopicFreshnessFloor = ClampTracked("propagation.topicFreshnessFloor", Propagation.TopicFreshnessFloor, 0.0, 1.0, notices);

            NormalizeMinMax(
                v => Propagation.TellerMultiplierMin = v,
                () => Propagation.TellerMultiplierMin,
                v => Propagation.TellerMultiplierMax = v,
                () => Propagation.TellerMultiplierMax,
                0.0, 100.0);

            int origPerQuery = Propagation.MaxContactsPerQuery;
            Propagation.MaxContactsPerQuery = Math.Max(1, Propagation.MaxContactsPerQuery);
            if (Propagation.MaxContactsPerQuery != origPerQuery && notices != null)
            {
                notices.Add(new ClampNotice { Key = "propagation.maxContactsPerQuery", Requested = origPerQuery, Applied = Propagation.MaxContactsPerQuery, AllowedRange = ">= 1" });
            }

            int origInPerson = Propagation.MaxInPersonContacts;
            Propagation.MaxInPersonContacts = Math.Max(1, Propagation.MaxInPersonContacts);
            if (Propagation.MaxInPersonContacts != origInPerson && notices != null)
            {
                notices.Add(new ClampNotice { Key = "propagation.maxInPersonContacts", Requested = origInPerson, Applied = Propagation.MaxInPersonContacts, AllowedRange = ">= 1" });
            }

            int origRemote = Propagation.MaxRemoteContacts;
            Propagation.MaxRemoteContacts = Math.Max(1, Propagation.MaxRemoteContacts);
            if (Propagation.MaxRemoteContacts != origRemote && notices != null)
            {
                notices.Add(new ClampNotice { Key = "propagation.maxRemoteContacts", Requested = origRemote, Applied = Propagation.MaxRemoteContacts, AllowedRange = ">= 1" });
            }

            if (Propagation.MaxInPersonContacts + Propagation.MaxRemoteContacts < Propagation.MaxContactsPerQuery)
            {
                int adjustedInPerson = Propagation.MaxContactsPerQuery - Propagation.MaxRemoteContacts;
                if (notices != null)
                {
                    notices.Add(new ClampNotice
                    {
                        Key = "propagation.maxInPersonContacts",
                        Requested = Propagation.MaxInPersonContacts,
                        Applied = adjustedInPerson,
                        AllowedRange = $">= {Propagation.MaxContactsPerQuery - Propagation.MaxRemoteContacts}"
                    });
                }
                Propagation.MaxInPersonContacts = adjustedInPerson;
            }

            Propagation.KingdomCorrespondentCacheSize = Math.Max(1, Propagation.KingdomCorrespondentCacheSize);
            Propagation.MaxNewKnowersPerEventPerTick = Math.Max(1, Propagation.MaxNewKnowersPerEventPerTick);
            Propagation.MaxInitialWitnesses = Math.Max(1, Propagation.MaxInitialWitnesses);

            Propagation.DefaultDrama = Math.Max(1, Math.Min(5, Propagation.DefaultDrama));
            if (Propagation.DefaultDramaByEventType != null)
            {
                foreach (var key in Propagation.DefaultDramaByEventType.Keys.ToList())
                {
                    Propagation.DefaultDramaByEventType[key] = Math.Max(1, Math.Min(5, Propagation.DefaultDramaByEventType[key]));
                }
            }

            // Relation
            Relation.ScaleOverride = Math.Max(0.0, Relation.ScaleOverride);
            Relation.MeetPositiveWeight = ClampTracked("relation.meetPositiveWeight", Relation.MeetPositiveWeight, 0.0, 10.0, notices);
            Relation.MeetNegativeWeight = ClampTracked("relation.meetNegativeWeight", Relation.MeetNegativeWeight, 0.0, 10.0, notices);
            NormalizeMinMax(
                "relation.meetFactorMin", "relation.meetFactorMax",
                v => Relation.MeetFactorMin = v,
                () => Relation.MeetFactorMin,
                v => Relation.MeetFactorMax = v,
                () => Relation.MeetFactorMax,
                0.0, 10.0,
                notices);
            Relation.RemoteBaseFactor = ClampTracked("relation.remoteBaseFactor", Relation.RemoteBaseFactor, 0.0, 10.0, notices);
            Relation.RemoteRelationWeight = ClampTracked("relation.remoteRelationWeight", Relation.RemoteRelationWeight, 0.0, 10.0, notices);
            Relation.RemoteFactorMax = ClampTracked("relation.remoteFactorMax", Relation.RemoteFactorMax, 0.0, 10.0, notices);

            // Leak
            Leak.BaseChancePerInsiderPerDay = Math.Max(0.0, Math.Min(1.0, Leak.BaseChancePerInsiderPerDay));
            Leak.GraceDays = Math.Max(0.0, Leak.GraceDays);
            Leak.ChanceDecayHalfLifeDays = Math.Max(0.1, Leak.ChanceDecayHalfLifeDays);

            NormalizeMinMax(
                v => Leak.MultiplierMin = v,
                () => Leak.MultiplierMin,
                v => Leak.MultiplierMax = v,
                () => Leak.MultiplierMax,
                0.0, 100.0);

            // Scheduling
            Scheduling.EventsPerHourlyTick = Math.Max(1, Scheduling.EventsPerHourlyTick);
            int origTellers = Scheduling.TellersPerHourlyTick;
            Scheduling.TellersPerHourlyTick = Math.Max(1, Scheduling.TellersPerHourlyTick);
            if (Scheduling.TellersPerHourlyTick != origTellers && notices != null)
            {
                notices.Add(new ClampNotice
                {
                    Key = "scheduling.tellersPerHourlyTick",
                    Requested = origTellers,
                    Applied = Scheduling.TellersPerHourlyTick,
                    AllowedRange = ">= 1"
                });
            }
            Scheduling.FlushIntervalHours = Math.Max(1, Scheduling.FlushIntervalHours);
            Scheduling.RumorLifetimeDays = Math.Max(0.1, Scheduling.RumorLifetimeDays);
            Scheduling.StaleDays = Math.Max(0.1, Scheduling.StaleDays);
            Scheduling.SecretWatchDays = Math.Max(0.0, Scheduling.SecretWatchDays);

            // Dialogue
            Dialogue.MaxVolunteersPerDay = Math.Max(1, Dialogue.MaxVolunteersPerDay);
            Dialogue.VolunteerCooldownDays = Math.Max(0.0, Dialogue.VolunteerCooldownDays);
            Dialogue.ScoreRetellMultiplier = Math.Max(0.0, Math.Min(10.0, Dialogue.ScoreRetellMultiplier));

            // §9.1 的四個評分權重都是「越大越優先」的正向項；負值會讓排序整個反轉，
            // 那不是一種合理的調校，是設定寫錯。
            Dialogue.ScoreDrama     = Clamp(Dialogue.ScoreDrama,     0.0, 100.0);
            Dialogue.ScoreFreshness = Clamp(Dialogue.ScoreFreshness, 0.0, 100.0);
            Dialogue.ScoreDetail    = Clamp(Dialogue.ScoreDetail,    0.0, 100.0);
            Dialogue.ScoreRelevance = Clamp(Dialogue.ScoreRelevance, 0.0, 100.0);

            // 傳聞模式（feature-LISTEN1/spec.md §12，LISTEN1d）
            Dialogue.GistExtraHops = Math.Max(0, Dialogue.GistExtraHops);
            string vMode = (Dialogue.VolunteerMode ?? string.Empty).Trim();
            if (string.Equals(vMode, "casual", StringComparison.OrdinalIgnoreCase))
            {
                Dialogue.VolunteerMode = "casual";
            }
            else if (string.Equals(vMode, "realistic", StringComparison.OrdinalIgnoreCase))
            {
                Dialogue.VolunteerMode = "realistic";
            }
            else
            {
                Dialogue.VolunteerMode = "auto";
            }

            // Presentation
            Presentation.ChronicleMaxEntries = Math.Max(1, Presentation.ChronicleMaxEntries);
            if (Presentation.FactOrder == null || Presentation.FactOrder.Length == 0)
            {
                Presentation.FactOrder = new[] { "WHO", "CONTEXT", "WHEN", "WHERE", "WHAT", "WHY", "OUTCOME" };
            }

            // Consequences
            Consequences.BystanderMultiplier = ClampTracked("consequences.bystanderMultiplier", Consequences.BystanderMultiplier, 0.0, 1.0, notices);
            Consequences.MaxAbsoluteDeltaPerHeroPerDay = ClampTracked("consequences.maxAbsoluteDeltaPerHeroPerDay", Consequences.MaxAbsoluteDeltaPerHeroPerDay, 0.0, 100.0, notices);

            int origMinDelta = Consequences.MinAbsoluteDelta;
            Consequences.MinAbsoluteDelta = Math.Max(0, Math.Min(50, origMinDelta));
            if (Consequences.MinAbsoluteDelta != origMinDelta && notices != null)
            {
                notices.Add(new ClampNotice
                {
                    Key = "consequences.minAbsoluteDelta",
                    Requested = origMinDelta,
                    Applied = Consequences.MinAbsoluteDelta,
                    AllowedRange = "0..50"
                });
            }

            Consequences.HopConfidence = PadOrTruncate(
                Consequences.HopConfidence,
                new[] { 1.0, 1.0, 0.75, 0.5, 0.3, 0.2 },
                6,
                0.0, 1.0);

            // Misconception
            if (Consequences.Misconception != null)
            {
                Consequences.Misconception.BaseClearUpChancePerMeeting =
                    Math.Max(0.0, Math.Min(1.0, Consequences.Misconception.BaseClearUpChancePerMeeting));

                NormalizeMinMax(
                    v => Consequences.Misconception.MultiplierMin = v,
                    () => Consequences.Misconception.MultiplierMin,
                    v => Consequences.Misconception.MultiplierMax = v,
                    () => Consequences.Misconception.MultiplierMax,
                    0.0, 100.0);
            }

            // Embellishment
            Embellishment.ChancePerNegativeHonor = Math.Max(0.0, Math.Min(10.0, Embellishment.ChancePerNegativeHonor));
            Embellishment.MinHop = Math.Max(0, Embellishment.MinHop);

            // Events: PrisonerDramaByProminence
            int origRuler = Events.PrisonerDramaByProminence.Ruler;
            Events.PrisonerDramaByProminence.Ruler = Math.Max(1, Math.Min(5, origRuler));
            if (Events.PrisonerDramaByProminence.Ruler != origRuler && notices != null)
            {
                notices.Add(new ClampNotice
                {
                    Key = "events.prisonerDramaByProminence.ruler",
                    Requested = origRuler,
                    Applied = Events.PrisonerDramaByProminence.Ruler,
                    AllowedRange = "1..5"
                });
            }

            int origClanLeader = Events.PrisonerDramaByProminence.ClanLeader;
            Events.PrisonerDramaByProminence.ClanLeader = Math.Max(1, Math.Min(5, origClanLeader));
            if (Events.PrisonerDramaByProminence.ClanLeader != origClanLeader && notices != null)
            {
                notices.Add(new ClampNotice
                {
                    Key = "events.prisonerDramaByProminence.clanLeader",
                    Requested = origClanLeader,
                    Applied = Events.PrisonerDramaByProminence.ClanLeader,
                    AllowedRange = "1..5"
                });
            }

            int origNobleMember = Events.PrisonerDramaByProminence.NobleMember;
            Events.PrisonerDramaByProminence.NobleMember = Math.Max(1, Math.Min(5, origNobleMember));
            if (Events.PrisonerDramaByProminence.NobleMember != origNobleMember && notices != null)
            {
                notices.Add(new ClampNotice
                {
                    Key = "events.prisonerDramaByProminence.nobleMember",
                    Requested = origNobleMember,
                    Applied = Events.PrisonerDramaByProminence.NobleMember,
                    AllowedRange = "1..5"
                });
            }

            int origMinor = Events.PrisonerDramaByProminence.Minor;
            Events.PrisonerDramaByProminence.Minor = Math.Max(1, Math.Min(5, origMinor));
            if (Events.PrisonerDramaByProminence.Minor != origMinor && notices != null)
            {
                notices.Add(new ClampNotice
                {
                    Key = "events.prisonerDramaByProminence.minor",
                    Requested = origMinor,
                    Applied = Events.PrisonerDramaByProminence.Minor,
                    AllowedRange = "1..5"
                });
            }

            // Events: ReleaseDramaByProminence
            int origRelRuler = Events.ReleaseDramaByProminence.Ruler;
            Events.ReleaseDramaByProminence.Ruler = Math.Max(1, Math.Min(5, origRelRuler));
            if (Events.ReleaseDramaByProminence.Ruler != origRelRuler && notices != null)
            {
                notices.Add(new ClampNotice
                {
                    Key = "events.releaseDramaByProminence.ruler",
                    Requested = origRelRuler,
                    Applied = Events.ReleaseDramaByProminence.Ruler,
                    AllowedRange = "1..5"
                });
            }

            int origRelClanLeader = Events.ReleaseDramaByProminence.ClanLeader;
            Events.ReleaseDramaByProminence.ClanLeader = Math.Max(1, Math.Min(5, origRelClanLeader));
            if (Events.ReleaseDramaByProminence.ClanLeader != origRelClanLeader && notices != null)
            {
                notices.Add(new ClampNotice
                {
                    Key = "events.releaseDramaByProminence.clanLeader",
                    Requested = origRelClanLeader,
                    Applied = Events.ReleaseDramaByProminence.ClanLeader,
                    AllowedRange = "1..5"
                });
            }

            int origRelNobleMember = Events.ReleaseDramaByProminence.NobleMember;
            Events.ReleaseDramaByProminence.NobleMember = Math.Max(1, Math.Min(5, origRelNobleMember));
            if (Events.ReleaseDramaByProminence.NobleMember != origRelNobleMember && notices != null)
            {
                notices.Add(new ClampNotice
                {
                    Key = "events.releaseDramaByProminence.nobleMember",
                    Requested = origRelNobleMember,
                    Applied = Events.ReleaseDramaByProminence.NobleMember,
                    AllowedRange = "1..5"
                });
            }

            int origRelMinor = Events.ReleaseDramaByProminence.Minor;
            Events.ReleaseDramaByProminence.Minor = Math.Max(1, Math.Min(5, origRelMinor));
            if (Events.ReleaseDramaByProminence.Minor != origRelMinor && notices != null)
            {
                notices.Add(new ClampNotice
                {
                    Key = "events.releaseDramaByProminence.minor",
                    Requested = origRelMinor,
                    Applied = Events.ReleaseDramaByProminence.Minor,
                    AllowedRange = "1..5"
                });
            }

            // Debug
            Debug.FakeProducerEventsPerDay = Math.Max(0.0, Debug.FakeProducerEventsPerDay);
            int origCrowdedOut = Debug.DevReportMaxCrowdedOut;
            Debug.DevReportMaxCrowdedOut = Math.Max(0, Math.Min(100, Debug.DevReportMaxCrowdedOut));
            if (Debug.DevReportMaxCrowdedOut != origCrowdedOut && notices != null)
            {
                notices.Add(new ClampNotice
                {
                    Key = "debug.devReportMaxCrowdedOut",
                    Requested = origCrowdedOut,
                    Applied = Debug.DevReportMaxCrowdedOut,
                    AllowedRange = "0..100"
                });
            }

            int origFlushLines = Debug.LogFlushEveryLines;
            Debug.LogFlushEveryLines = Math.Max(1, Debug.LogFlushEveryLines);
            if (Debug.LogFlushEveryLines != origFlushLines && notices != null)
            {
                notices.Add(new ClampNotice
                {
                    Key = "debug.logFlushEveryLines",
                    Requested = origFlushLines,
                    Applied = Debug.LogFlushEveryLines,
                    AllowedRange = ">= 1"
                });
            }

            double origFlushSeconds = Debug.LogFlushEverySeconds;
            Debug.LogFlushEverySeconds = Math.Max(0.0, Debug.LogFlushEverySeconds);
            if (Math.Abs(Debug.LogFlushEverySeconds - origFlushSeconds) > 1e-9 && notices != null)
            {
                notices.Add(new ClampNotice
                {
                    Key = "debug.logFlushEverySeconds",
                    Requested = origFlushSeconds,
                    Applied = Debug.LogFlushEverySeconds,
                    AllowedRange = ">= 0.0"
                });
            }

            int origRelationBoost = Debug.DevRelationBoost;
            Debug.DevRelationBoost = Math.Max(-100, Math.Min(100, Debug.DevRelationBoost));
            if (Debug.DevRelationBoost != origRelationBoost && notices != null)
            {
                notices.Add(new ClampNotice
                {
                    Key = "debug.devRelationBoost",
                    Requested = origRelationBoost,
                    Applied = Debug.DevRelationBoost,
                    AllowedRange = "-100..100"
                });
            }

            // Situations
            double origMinBranchWeight = Situations.MinBranchWeight;
            Situations.MinBranchWeight = Math.Max(0.0, Math.Min(10.0, Situations.MinBranchWeight));
            if (Math.Abs(Situations.MinBranchWeight - origMinBranchWeight) > 1e-9 && notices != null)
            {
                notices.Add(new ClampNotice
                {
                    Key = "situations.minBranchWeight",
                    Requested = origMinBranchWeight,
                    Applied = Situations.MinBranchWeight,
                    AllowedRange = "0.0..10.0"
                });
            }

            double origMaxPerDay = Situations.MaxPerDay;
            Situations.MaxPerDay = Math.Max(0.0, Situations.MaxPerDay);
            if (Math.Abs(Situations.MaxPerDay - origMaxPerDay) > 1e-9 && notices != null)
            {
                notices.Add(new ClampNotice
                {
                    Key = "situations.maxPerDay",
                    Requested = origMaxPerDay,
                    Applied = Situations.MaxPerDay,
                    AllowedRange = ">= 0.0"
                });
            }

            double origClanFactor = Situations.ClanEscalationFactor;
            Situations.ClanEscalationFactor = Math.Max(0.0, Math.Min(1.0, Situations.ClanEscalationFactor));
            if (Math.Abs(Situations.ClanEscalationFactor - origClanFactor) > 1e-9 && notices != null)
            {
                notices.Add(new ClampNotice
                {
                    Key = "situations.clanEscalationFactor",
                    Requested = origClanFactor,
                    Applied = Situations.ClanEscalationFactor,
                    AllowedRange = "0.0..1.0"
                });
            }

            int origClanThreshold = Situations.ClanEscalationThreshold;
            Situations.ClanEscalationThreshold = Math.Max(0, Situations.ClanEscalationThreshold);
            if (Situations.ClanEscalationThreshold != origClanThreshold && notices != null)
            {
                notices.Add(new ClampNotice
                {
                    Key = "situations.clanEscalationThreshold",
                    Requested = origClanThreshold,
                    Applied = Situations.ClanEscalationThreshold,
                    AllowedRange = ">= 0"
                });
            }

            int origMaxPending = Situations.MaxPending;
            Situations.MaxPending = Math.Max(0, Situations.MaxPending);
            if (Situations.MaxPending != origMaxPending && notices != null)
            {
                notices.Add(new ClampNotice
                {
                    Key = "situations.maxPending",
                    Requested = origMaxPending,
                    Applied = Situations.MaxPending,
                    AllowedRange = ">= 0"
                });
            }

            int origMaxPendingChars = Situations.MaxPendingChars;
            Situations.MaxPendingChars = Math.Max(0, Math.Min(30000, Situations.MaxPendingChars));
            if (Situations.MaxPendingChars != origMaxPendingChars && notices != null)
            {
                notices.Add(new ClampNotice
                {
                    Key = "situations.maxPendingChars",
                    Requested = origMaxPendingChars,
                    Applied = Situations.MaxPendingChars,
                    AllowedRange = "0..30000"
                });
            }

            if (Situations.Scan == null) Situations.Scan = new();

            int origMaxPairs = Situations.Scan.MaxPairsPerSettlement;
            Situations.Scan.MaxPairsPerSettlement = Math.Max(2, Math.Min(200, origMaxPairs));
            if (Situations.Scan.MaxPairsPerSettlement != origMaxPairs && notices != null)
            {
                notices.Add(new ClampNotice
                {
                    Key = "situations.scan.maxPairsPerSettlement",
                    Requested = origMaxPairs,
                    Applied = Situations.Scan.MaxPairsPerSettlement,
                    AllowedRange = "2..200"
                });
            }

            int origMaxCandidates = Situations.Scan.MaxCandidates;
            Situations.Scan.MaxCandidates = Math.Max(1, Math.Min(5000, origMaxCandidates));
            if (Situations.Scan.MaxCandidates != origMaxCandidates && notices != null)
            {
                notices.Add(new ClampNotice
                {
                    Key = "situations.scan.maxCandidates",
                    Requested = origMaxCandidates,
                    Applied = Situations.Scan.MaxCandidates,
                    AllowedRange = "1..5000"
                });
            }

            int origMaxWitnessRollers = Situations.Contest.MaxWitnessRollers;
            Situations.Contest.MaxWitnessRollers = Math.Max(0, Situations.Contest.MaxWitnessRollers);
            if (Situations.Contest.MaxWitnessRollers != origMaxWitnessRollers && notices != null)
            {
                notices.Add(new ClampNotice
                {
                    Key = "situations.contest.maxWitnessRollers",
                    Requested = origMaxWitnessRollers,
                    Applied = Situations.Contest.MaxWitnessRollers,
                    AllowedRange = ">= 0"
                });
            }

            // Situations: GrudgeDecay
            Situations.GrudgeDecay ??= new GrudgeDecayConfig();
            Situations.GrudgeDecay.Personal ??= new GrudgeBandConfig { Band = 10.0, DaysPerPoint = 3.0 };
            Situations.GrudgeDecay.Clan ??= new GrudgeBandConfig { Band = 20.0, DaysPerPoint = 6.0 };
            Situations.GrudgeDecay.TraitWeights ??= new GrudgeTraitWeightsConfig();

            double origPersonalBand = Situations.GrudgeDecay.Personal.Band;
            Situations.GrudgeDecay.Personal.Band = Math.Max(0.0, origPersonalBand);
            if (Situations.GrudgeDecay.Personal.Band != origPersonalBand && notices != null)
            {
                notices.Add(new ClampNotice
                {
                    Key = "situations.grudgeDecay.personal.band",
                    Requested = origPersonalBand,
                    Applied = Situations.GrudgeDecay.Personal.Band,
                    AllowedRange = ">= 0"
                });
            }

            double origPersonalDpp = Situations.GrudgeDecay.Personal.DaysPerPoint;
            if (origPersonalDpp <= 0.0)
            {
                Situations.GrudgeDecay.Personal.DaysPerPoint = 3.0;
                if (notices != null)
                {
                    notices.Add(new ClampNotice
                    {
                        Key = "situations.grudgeDecay.personal.daysPerPoint",
                        Requested = origPersonalDpp,
                        Applied = 3.0,
                        AllowedRange = "> 0"
                    });
                }
            }

            double origClanBand = Situations.GrudgeDecay.Clan.Band;
            Situations.GrudgeDecay.Clan.Band = Math.Max(0.0, origClanBand);
            if (Situations.GrudgeDecay.Clan.Band != origClanBand && notices != null)
            {
                notices.Add(new ClampNotice
                {
                    Key = "situations.grudgeDecay.clan.band",
                    Requested = origClanBand,
                    Applied = Situations.GrudgeDecay.Clan.Band,
                    AllowedRange = ">= 0"
                });
            }

            double origClanDpp = Situations.GrudgeDecay.Clan.DaysPerPoint;
            if (origClanDpp <= 0.0)
            {
                Situations.GrudgeDecay.Clan.DaysPerPoint = 6.0;
                if (notices != null)
                {
                    notices.Add(new ClampNotice
                    {
                        Key = "situations.grudgeDecay.clan.daysPerPoint",
                        Requested = origClanDpp,
                        Applied = 6.0,
                        AllowedRange = "> 0"
                    });
                }
            }

            double origMulMin = Situations.GrudgeDecay.MultiplierMin;
            double origMulMax = Situations.GrudgeDecay.MultiplierMax;
            double mulMin = origMulMin;
            double mulMax = origMulMax;
            if (mulMax < mulMin)
            {
                (mulMin, mulMax) = (mulMax, mulMin);
            }
            mulMin = Math.Max(0.01, Math.Min(1.0, mulMin));
            mulMax = Math.Max(1.0, Math.Min(10.0, mulMax));

            if (Math.Abs(mulMin - origMulMin) > 1e-9 && notices != null)
            {
                notices.Add(new ClampNotice
                {
                    Key = "situations.grudgeDecay.multiplierMin",
                    Requested = origMulMin,
                    Applied = mulMin,
                    AllowedRange = "0.01..1"
                });
            }
            if (Math.Abs(mulMax - origMulMax) > 1e-9 && notices != null)
            {
                notices.Add(new ClampNotice
                {
                    Key = "situations.grudgeDecay.multiplierMax",
                    Requested = origMulMax,
                    Applied = mulMax,
                    AllowedRange = "1..10"
                });
            }
            Situations.GrudgeDecay.MultiplierMin = mulMin;
            Situations.GrudgeDecay.MultiplierMax = mulMax;

            Situations.GrudgeDecay.TraitWeights.Mercy = ClampTracked("situations.grudgeDecay.traitWeights.mercy", Situations.GrudgeDecay.TraitWeights.Mercy, -2.0, 2.0, notices);
            Situations.GrudgeDecay.TraitWeights.Calculating = ClampTracked("situations.grudgeDecay.traitWeights.calculating", Situations.GrudgeDecay.TraitWeights.Calculating, -2.0, 2.0, notices);
            Situations.GrudgeDecay.TraitWeights.HonorForGratitude = ClampTracked("situations.grudgeDecay.traitWeights.honorForGratitude", Situations.GrudgeDecay.TraitWeights.HonorForGratitude, -2.0, 2.0, notices);

            // Memory
            if (Memory.BaseDays < 0.0)
            {
                if (notices != null)
                {
                    notices.Add(new ClampNotice
                    {
                        Key = "memory.baseDays",
                        Requested = Memory.BaseDays,
                        Applied = 0.0,
                        AllowedRange = ">= 0"
                    });
                }
                Memory.BaseDays = 0.0;
            }

            int origDramaRef = Memory.DramaReference;
            Memory.DramaReference = Math.Max(1, Math.Min(5, Memory.DramaReference));
            if (Memory.DramaReference != origDramaRef && notices != null)
            {
                notices.Add(new ClampNotice
                {
                    Key = "memory.dramaReference",
                    Requested = origDramaRef,
                    Applied = Memory.DramaReference,
                    AllowedRange = "1..5"
                });
            }

            if (Memory.MinDays < 0.0)
            {
                if (notices != null)
                {
                    notices.Add(new ClampNotice
                    {
                        Key = "memory.minDays",
                        Requested = Memory.MinDays,
                        Applied = 0.0,
                        AllowedRange = ">= 0"
                    });
                }
                Memory.MinDays = 0.0;
            }

            int origRelFull = Memory.RelationFullAt;
            Memory.RelationFullAt = Math.Max(1, Math.Min(100, Memory.RelationFullAt));
            if (Memory.RelationFullAt != origRelFull && notices != null)
            {
                notices.Add(new ClampNotice
                {
                    Key = "memory.relationFullAt",
                    Requested = origRelFull,
                    Applied = Memory.RelationFullAt,
                    AllowedRange = "1..100"
                });
            }

            Memory.InterestFloors.Participant = ClampTracked("memory.interestFloors.participant", Memory.InterestFloors.Participant, 0.0, 1.0, notices);
            Memory.InterestFloors.Kin = ClampTracked("memory.interestFloors.kin", Memory.InterestFloors.Kin, 0.0, 1.0, notices);
            Memory.InterestFloors.SameClan = ClampTracked("memory.interestFloors.sameClan", Memory.InterestFloors.SameClan, 0.0, 1.0, notices);
            Memory.InterestFloors.Other = ClampTracked("memory.interestFloors.other", Memory.InterestFloors.Other, 0.0, 1.0, notices);
            Memory.InterestFloors.OtherByDrama = PadOrTruncate(
                Memory.InterestFloors.OtherByDrama,
                new[] { 0.05, 0.05, 0.1, 0.2, 0.4 },
                5,
                0.0, 1.0);
            Memory.TellFactorMin = ClampTracked("memory.tellFactorMin", Memory.TellFactorMin, 0.0, 1.0, notices);
            Memory.UpgradeMinInterest = ClampTracked("memory.upgradeMinInterest", Memory.UpgradeMinInterest, 0.0, 1.0, notices);
            Memory.ReinforceGrowth = ClampTracked("memory.reinforceGrowth", Memory.ReinforceGrowth, 1.0, 10.0, notices);
            Memory.ReinforceMaxMultiplier = ClampTracked("memory.reinforceMaxMultiplier", Memory.ReinforceMaxMultiplier, 1.0, 10.0, notices);

            return this;
        }

        private static double Clamp(double value, double min, double max) =>
            Math.Max(min, Math.Min(max, value));

        private static int[] PadOrTruncate(int[]? source, int[] defaultVal, int targetLen, int minVal, int maxVal)
        {
            if (source == null || source.Length == 0)
            {
                source = (int[])defaultVal.Clone();
            }

            var list = new List<int>(source);
            while (list.Count < targetLen)
            {
                list.Add(list[list.Count - 1]);
            }
            if (list.Count > targetLen)
            {
                list = list.GetRange(0, targetLen);
            }

            for (int i = 0; i < list.Count; i++)
            {
                list[i] = Math.Max(minVal, Math.Min(maxVal, list[i]));
            }

            return list.ToArray();
        }

        private static double[] PadOrTruncate(double[]? source, double[] defaultVal, int targetLen, double minVal, double maxVal)
        {
            if (source == null || source.Length == 0)
            {
                source = (double[])defaultVal.Clone();
            }

            var list = new List<double>(source);
            while (list.Count < targetLen)
            {
                list.Add(list[list.Count - 1]);
            }
            if (list.Count > targetLen)
            {
                list = list.GetRange(0, targetLen);
            }

            for (int i = 0; i < list.Count; i++)
            {
                list[i] = Math.Max(minVal, Math.Min(maxVal, list[i]));
            }

            return list.ToArray();
        }


        private static double ClampTracked(string key, double value, double min, double max, IList<ClampNotice>? notices)
        {
            double clamped = Math.Max(min, Math.Min(max, value));
            if (Math.Abs(clamped - value) > 1e-9 && notices != null)
            {
                notices.Add(new ClampNotice
                {
                    Key = key,
                    Requested = value,
                    Applied = clamped,
                    AllowedRange = $"{min:0.##}..{max:0.##}"
                });
            }
            return clamped;
        }

        private static void NormalizeMinMax(
            Action<double> setMin, Func<double> getMin,
            Action<double> setMax, Func<double> getMax,
            double boundMin, double boundMax)
        {
            NormalizeMinMax(null, null, setMin, getMin, setMax, getMax, boundMin, boundMax, null);
        }

        private static void NormalizeMinMax(
            string? minKey, string? maxKey,
            Action<double> setMin, Func<double> getMin,
            Action<double> setMax, Func<double> getMax,
            double boundMin, double boundMax,
            IList<ClampNotice>? notices)
        {
            double origMin = getMin();
            double origMax = getMax();
            double min = Math.Max(boundMin, Math.Min(boundMax, origMin));
            double max = Math.Max(boundMin, Math.Min(boundMax, origMax));
            if (min > max)
            {
                (min, max) = (max, min);
            }
            if (minKey != null && Math.Abs(min - origMin) > 1e-9 && notices != null)
            {
                notices.Add(new ClampNotice
                {
                    Key = minKey,
                    Requested = origMin,
                    Applied = min,
                    AllowedRange = $"{boundMin:0.##}..{boundMax:0.##}"
                });
            }
            if (maxKey != null && Math.Abs(max - origMax) > 1e-9 && notices != null)
            {
                notices.Add(new ClampNotice
                {
                    Key = maxKey,
                    Requested = origMax,
                    Applied = max,
                    AllowedRange = $"{boundMin:0.##}..{boundMax:0.##}"
                });
            }
            setMin(min);
            setMax(max);
        }
    }
}
