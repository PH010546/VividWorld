using System;
using System.Collections.Generic;
using System.Linq;
using VividWorld.Core.Config;
using VividWorld.Core.Persistence;
using Xunit;

namespace VividWorld.Core.Tests
{
    public class ConfigTests
    {
        [Fact]
        public void Defaults_MatchTheDocumentedValues()
        {
            var cfg = new VividWorldConfig();

            // Top level
            Assert.Equal(1, cfg.ConfigVersion);
            Assert.True(cfg.Enabled);
            Assert.Equal("Info", cfg.LogLevel);

            // Retention
            Assert.Equal("Threshold", cfg.Retention.Policy);
            Assert.Equal(new[] { 5, 3, 2, 2, 1, 1 }, cfg.Retention.HopThresholds);   // 帳本 L-17、規格 §17
            Assert.Equal(0.35, cfg.Retention.Softness);
            Assert.Equal(1, cfg.Retention.AlwaysKeepAtOrBelowFragility);
            Assert.Equal(1, cfg.Retention.MinFactsRetained);
            Assert.Equal(new[] { 0, 0, 0, 0, 0 }, cfg.Retention.DramaThresholdShift);

            // Propagation
            Assert.Equal(0.18, cfg.Propagation.BaseTellChancePerContact);
            Assert.Equal(1.25, cfg.Propagation.ChannelWeights.SameParty);
            Assert.Equal(1.00, cfg.Propagation.ChannelWeights.SameArmy);
            Assert.Equal(0.80, cfg.Propagation.ChannelWeights.SameSettlement);
            Assert.Equal(1.00, cfg.Propagation.ChannelWeights.SameClan);
            Assert.Equal(0.80, cfg.Propagation.ChannelWeights.KinAbroad);
            Assert.Equal(0.50, cfg.Propagation.ChannelWeights.Kingdom);

            Assert.Equal(new[] { 0.35, 0.6, 1.0, 1.5, 2.2 }, cfg.Propagation.DramaTellMultiplier);
            Assert.Equal(new[] { 2, 3, 4, 5, 6 }, cfg.Propagation.DramaMaxHop);
            Assert.Equal(new[] { 0.35, 0.6, 1.0, 1.5, 2.2 }, cfg.Propagation.TopicDramaWeight);
            Assert.Equal(0.1, cfg.Propagation.TopicFreshnessFloor);

            Assert.Equal(0.10, cfg.Propagation.TellerTraitWeights.Generosity);
            Assert.Equal(-0.05, cfg.Propagation.TellerTraitWeights.Honor);
            Assert.Equal(-0.12, cfg.Propagation.TellerTraitWeights.Calculating);

            Assert.Equal(0.1, cfg.Propagation.TellerMultiplierMin);
            Assert.Equal(3.0, cfg.Propagation.TellerMultiplierMax);

            Assert.Equal(6, cfg.Propagation.MaxContactsPerQuery);
            Assert.Equal(4, cfg.Propagation.MaxInPersonContacts);
            Assert.Equal(2, cfg.Propagation.MaxRemoteContacts);
            Assert.Equal(8, cfg.Propagation.KingdomCorrespondentCacheSize);
            Assert.Equal(2, cfg.Propagation.MaxNewKnowersPerEventPerTick);
            Assert.Equal(8, cfg.Propagation.MaxInitialWitnesses);

            Assert.False(cfg.Propagation.PlayerCanTell);

            Assert.Equal(0.15, cfg.Propagation.SelfIncriminationMultiplier);
            Assert.Equal(0.03, cfg.Propagation.TellToSubjectMultiplier);
            Assert.True(cfg.Propagation.AllowParticipantRetell);

            Assert.Equal(3, cfg.Propagation.DefaultDrama);
            Assert.Equal(4, cfg.Propagation.DefaultDramaByEventType["covert_sabotage"]);
            Assert.Equal(5, cfg.Propagation.DefaultDramaByEventType["duel"]);
            Assert.Equal(3, cfg.Propagation.DefaultDramaByEventType["duel_arranged"]);
            Assert.Equal(4, cfg.Propagation.DefaultDramaByEventType["tavern_quarrel"]);
            Assert.Equal(1, cfg.Propagation.DefaultDramaByEventType["shared_meal"]);

            // Leak
            Assert.Equal(0.012, cfg.Leak.BaseChancePerInsiderPerDay);
            Assert.Equal(1.0, cfg.Leak.GraceDays);
            Assert.Equal(60.0, cfg.Leak.ChanceDecayHalfLifeDays);
            Assert.Equal(-0.15, cfg.Leak.TraitWeights.Honor);
            Assert.Equal(-0.20, cfg.Leak.TraitWeights.Calculating);
            Assert.Equal(0.15, cfg.Leak.TraitWeights.Generosity);
            Assert.Equal(0.05, cfg.Leak.MultiplierMin);
            Assert.Equal(5.0, cfg.Leak.MultiplierMax);

            // Scheduling
            Assert.Equal(4, cfg.Scheduling.EventsPerHourlyTick);
            Assert.Equal(8, cfg.Scheduling.TellersPerHourlyTick);
            Assert.Equal(120.0, cfg.Scheduling.RumorLifetimeDays);
            Assert.Equal(30.0, cfg.Scheduling.StaleDays);
            Assert.Equal(252.0, cfg.Scheduling.SecretWatchDays);        // = 3 遊戲年（84×3），§11.2
            Assert.True(cfg.Scheduling.ScaleDurationsToGameCalendar);
            Assert.Equal(6, cfg.Scheduling.FlushIntervalHours);

            // Dialogue
            Assert.Equal(30, cfg.Dialogue.NpcVolunteerRelationGate);
            Assert.True(cfg.Dialogue.NpcVolunteerAlwaysForCloseKin);
            Assert.Equal(1, cfg.Dialogue.MaxVolunteersPerDay);
            Assert.Equal(3.0, cfg.Dialogue.VolunteerCooldownDays);
            Assert.Equal(0, cfg.Dialogue.AskRelationGate);
            Assert.Equal(5.0, cfg.Dialogue.AskWillingnessThreshold);
            Assert.Equal(6.0, cfg.Dialogue.AskTraitWeights.Generosity);
            Assert.Equal(4.0, cfg.Dialogue.AskTraitWeights.Honor);
            Assert.Equal(-8.0, cfg.Dialogue.AskTraitWeights.Calculating);
            Assert.True(cfg.Dialogue.AllowRicherRetell);
            Assert.Equal(0.5, cfg.Dialogue.ScoreRetellMultiplier);
            Assert.Equal(2.0, cfg.Dialogue.ScoreDrama);
            Assert.Equal(3.0, cfg.Dialogue.ScoreFreshness);
            Assert.Equal(1.0, cfg.Dialogue.ScoreDetail);
            Assert.Equal(4.0, cfg.Dialogue.ScoreRelevance);
            Assert.Equal(20, cfg.Dialogue.ScoreRelevanceRelationGate);
            Assert.Equal("lord_start", cfg.Dialogue.NpcLineInputToken);
            Assert.Equal(105, cfg.Dialogue.NpcLinePriority);
            Assert.Equal("hero_main_options", cfg.Dialogue.PlayerLineInputToken);
            Assert.Equal(105, cfg.Dialogue.PlayerLinePriority);

            // Presentation
            Assert.Equal(50, cfg.Presentation.ChronicleMaxEntries);
            Assert.Equal("Ctrl+L", cfg.Presentation.ChronicleHotkey);
            Assert.Equal(new[] { "WHO", "CONTEXT", "WHEN", "WHERE", "WHAT", "WHY", "OUTCOME" }, cfg.Presentation.FactOrder);
            Assert.Equal(string.Empty, cfg.Presentation.FactSeparator);
            Assert.Equal(string.Empty, cfg.Presentation.SentenceEnd);
            Assert.True(cfg.Presentation.EncyclopediaLinksEnabled);

            // Persistence
            Assert.Equal(100, cfg.Persistence.ShardDays);
            Assert.Equal(0, cfg.Persistence.MaxSnapshots);   // SNAP1：0 = 不限份數，由玩家用遊戲內的管理工具刪
            Assert.Equal(24000, cfg.Persistence.MaxPendingIngestChars);
            Assert.Equal(24, cfg.Persistence.MaxFactsPerEvent);

            // Consequences
            Assert.True(cfg.Consequences.Enabled);
            Assert.Equal(new[] { 1.0, 1.0, 0.75, 0.5, 0.3, 0.2 }, cfg.Consequences.HopConfidence);
            Assert.Equal(0.35, cfg.Consequences.BystanderMultiplier);
            Assert.Equal(1, cfg.Consequences.MinAbsoluteDelta);
            Assert.Equal(6.0, cfg.Consequences.MaxAbsoluteDeltaPerHeroPerDay);
            Assert.False(cfg.Consequences.LedgerOnly);
            Assert.Equal(0.15, cfg.Consequences.Misconception.BaseClearUpChancePerMeeting);
            Assert.Equal(0.15, cfg.Consequences.Misconception.TraitWeights.HonorMisled);
            Assert.Equal(0.15, cfg.Consequences.Misconception.TraitWeights.HonorBlamed);
            Assert.Equal(0.10, cfg.Consequences.Misconception.TraitWeights.GenerosityBlamed);
            Assert.Equal(-0.20, cfg.Consequences.Misconception.TraitWeights.CalculatingMisled);
            Assert.Equal(0.05, cfg.Consequences.Misconception.MultiplierMin);
            Assert.Equal(3.0, cfg.Consequences.Misconception.MultiplierMax);

            // Embellishment
            Assert.False(cfg.Embellishment.Enabled);
            Assert.Equal(2, cfg.Embellishment.MinHop);
            Assert.Equal(0.15, cfg.Embellishment.ChancePerNegativeHonor);

            // Relation
            Assert.Equal(0, cfg.Relation.ScaleOverride);
            Assert.Equal(0.35, cfg.Relation.MeetPositiveWeight);
            Assert.Equal(1.30, cfg.Relation.MeetNegativeWeight);
            Assert.Equal(0.10, cfg.Relation.MeetFactorMin);
            Assert.Equal(1.40, cfg.Relation.MeetFactorMax);
            Assert.Equal(0.10, cfg.Relation.RemoteBaseFactor);
            Assert.Equal(2.00, cfg.Relation.RemoteRelationWeight);
            Assert.Equal(2.00, cfg.Relation.RemoteFactorMax);

            // Events
            Assert.Equal("vividworld_events.json", cfg.Events.CatalogFile);
            Assert.False(cfg.Events.StrictCatalog);
            Assert.NotNull(cfg.Events.Sources);
            Assert.True(cfg.Events.Sources.HeroKilled);
            Assert.True(cfg.Events.Sources.HeroPrisonerTaken);
            Assert.True(cfg.Events.Sources.HeroPrisonerReleased);
            Assert.True(cfg.Events.Sources.HeroesMarried);
            Assert.True(cfg.Events.Sources.ChildBorn);
            Assert.NotNull(cfg.Events.PrisonerDramaByProminence);
            Assert.Equal(5, cfg.Events.PrisonerDramaByProminence.Ruler);
            Assert.Equal(4, cfg.Events.PrisonerDramaByProminence.ClanLeader);
            Assert.Equal(2, cfg.Events.PrisonerDramaByProminence.NobleMember);
            Assert.Equal(1, cfg.Events.PrisonerDramaByProminence.Minor);
            Assert.NotNull(cfg.Events.ReleaseDramaByProminence);
            Assert.Equal(4, cfg.Events.ReleaseDramaByProminence.Ruler);
            Assert.Equal(3, cfg.Events.ReleaseDramaByProminence.ClanLeader);
            Assert.Equal(1, cfg.Events.ReleaseDramaByProminence.NobleMember);
            Assert.Equal(1, cfg.Events.ReleaseDramaByProminence.Minor);

            // Situations
            Assert.Equal("vividworld_situations.json", cfg.Situations.CatalogFile);
            Assert.Equal("vividworld_situation_events.json", cfg.Situations.EventCatalogFile);
            Assert.True(cfg.Situations.DailyScanEnabled);
            Assert.Equal(1.5, cfg.Situations.MaxPerDay);
            Assert.Equal(0.1, cfg.Situations.MinBranchWeight);
            Assert.Equal(30, cfg.Situations.ClanEscalationThreshold);
            Assert.Equal(0.5, cfg.Situations.ClanEscalationFactor);
            Assert.Equal(200, cfg.Situations.MaxPending);
            Assert.Equal(20000, cfg.Situations.MaxPendingChars);
            Assert.NotNull(cfg.Situations.Contest);
            Assert.Equal(3, cfg.Situations.Contest.MaxWitnessRollers);

            // Debug
            Assert.False(cfg.Debug.FakeProducerEnabled);
            Assert.Equal(1.0, cfg.Debug.FakeProducerEventsPerDay);
            Assert.False(cfg.Debug.DebugDialogueEnabled);
            Assert.False(cfg.Debug.VerboseTickLog);
            Assert.False(cfg.Debug.AllowFalseAccusationInjection);
            Assert.False(cfg.Debug.AllowTestEventInjection);
            Assert.False(cfg.Debug.AnnounceRumorEvents);
            Assert.Equal(8, cfg.Debug.DevReportMaxCrowdedOut);
            Assert.Equal(64, cfg.Debug.LogFlushEveryLines);
            Assert.Equal(5.0, cfg.Debug.LogFlushEverySeconds);
            Assert.False(cfg.Debug.MetricsEnabled);
            Assert.Equal(20, cfg.Debug.DevRelationBoost);
            Assert.False(cfg.Debug.LogTellerTurns);   // 發行前改成 false

            // Memory
            Assert.Equal(new[] { 0.05, 0.05, 0.1, 0.2, 0.4 }, cfg.Memory.InterestFloors.OtherByDrama);
        }

        [Fact]
        public void Normalize_PadsShortDramaArraysToFive()
        {
            var cfg = new VividWorldConfig();
            cfg.Propagation.DramaTellMultiplier = new double[] { 1.0, 2.5 };
            cfg.Propagation.DramaMaxHop = new int[] { 1, 2, 3 };
            cfg.Retention.DramaThresholdShift = new int[] { -1 };

            cfg.Normalize();

            Assert.Equal(5, cfg.Propagation.DramaTellMultiplier.Length);
            Assert.Equal(1.0, cfg.Propagation.DramaTellMultiplier[0]);
            Assert.Equal(2.5, cfg.Propagation.DramaTellMultiplier[1]);
            Assert.Equal(2.5, cfg.Propagation.DramaTellMultiplier[2]);
            Assert.Equal(2.5, cfg.Propagation.DramaTellMultiplier[3]);
            Assert.Equal(2.5, cfg.Propagation.DramaTellMultiplier[4]);

            Assert.Equal(5, cfg.Propagation.DramaMaxHop.Length);
            Assert.Equal(1, cfg.Propagation.DramaMaxHop[0]);
            Assert.Equal(2, cfg.Propagation.DramaMaxHop[1]);
            Assert.Equal(3, cfg.Propagation.DramaMaxHop[2]);
            Assert.Equal(3, cfg.Propagation.DramaMaxHop[3]);
            Assert.Equal(3, cfg.Propagation.DramaMaxHop[4]);

            Assert.Equal(5, cfg.Retention.DramaThresholdShift.Length);
            Assert.Equal(-1, cfg.Retention.DramaThresholdShift[0]);
            Assert.Equal(-1, cfg.Retention.DramaThresholdShift[1]);
            Assert.Equal(-1, cfg.Retention.DramaThresholdShift[2]);
            Assert.Equal(-1, cfg.Retention.DramaThresholdShift[3]);
            Assert.Equal(-1, cfg.Retention.DramaThresholdShift[4]);
        }

        [Fact]
        public void Normalize_UnknownPolicyString_FallsBackToThreshold()
        {
            var cfg = new VividWorldConfig();
            cfg.Retention.Policy = "SomeUnknownPolicy";

            cfg.Normalize();

            Assert.Equal("Threshold", cfg.Retention.Policy);

            cfg.Retention.Policy = "probabilistic";
            cfg.Normalize();
            Assert.Equal("Probabilistic", cfg.Retention.Policy);
        }

        [Fact]
        public void Normalize_ClampsUnprotectedWeightsToLegalRange()
        {
            var cfg = new VividWorldConfig();

            // ChannelWeights 直接乘進 §6.5 的機率式，沒有其他地方會擋住負值。
            cfg.Propagation.ChannelWeights.SameParty = 999.0;
            cfg.Propagation.ChannelWeights.SameArmy = -1.0;
            cfg.Propagation.ChannelWeights.SameSettlement = -1.0;
            cfg.Propagation.ChannelWeights.SameClan = -0.5;
            cfg.Propagation.ChannelWeights.KinAbroad = 50.0;
            cfg.Propagation.ChannelWeights.Kingdom = -2.0;

            // §9.1 的四個評分權重都是正向項，負值會讓排序反轉。
            cfg.Dialogue.ScoreDrama = -5.0;
            cfg.Dialogue.ScoreFreshness = 500.0;
            cfg.Dialogue.ScoreDetail = -0.1;
            cfg.Dialogue.ScoreRelevance = -100.0;

            cfg.Normalize();

            Assert.Equal(10.0, cfg.Propagation.ChannelWeights.SameParty);
            Assert.Equal(0.0, cfg.Propagation.ChannelWeights.SameArmy);
            Assert.Equal(0.0, cfg.Propagation.ChannelWeights.SameSettlement);
            Assert.Equal(0.0, cfg.Propagation.ChannelWeights.SameClan);
            Assert.Equal(10.0, cfg.Propagation.ChannelWeights.KinAbroad);
            Assert.Equal(0.0, cfg.Propagation.ChannelWeights.Kingdom);

            Assert.Equal(0.0, cfg.Dialogue.ScoreDrama);
            Assert.Equal(100.0, cfg.Dialogue.ScoreFreshness);
            Assert.Equal(0.0, cfg.Dialogue.ScoreDetail);
            Assert.Equal(0.0, cfg.Dialogue.ScoreRelevance);

            // 合法值不得被動到。
            var untouched = new VividWorldConfig();
            untouched.Normalize();
            Assert.Equal(1.25, untouched.Propagation.ChannelWeights.SameParty);
            Assert.Equal(1.00, untouched.Propagation.ChannelWeights.SameArmy);
            Assert.Equal(0.80, untouched.Propagation.ChannelWeights.SameSettlement);
            Assert.Equal(1.00, untouched.Propagation.ChannelWeights.SameClan);
            Assert.Equal(0.80, untouched.Propagation.ChannelWeights.KinAbroad);
            Assert.Equal(0.50, untouched.Propagation.ChannelWeights.Kingdom);
            Assert.Equal(2.0, untouched.Dialogue.ScoreDrama);
            Assert.Equal(4.0, untouched.Dialogue.ScoreRelevance);
        }

        [Fact]
        public void Config_PreservesUnknownFutureKeys()
        {
            string json = @"{
                ""configVersion"": 1,
                ""futureTopLevelSetting"": ""val_abc"",
                ""retention"": {
                    ""policy"": ""Threshold"",
                    ""futureRetentionSetting"": 12345
                }
            }";

            var cfg = VividJson.Read<VividWorldConfig>(json);
            Assert.NotNull(cfg);
            Assert.True(cfg!.Extra.ContainsKey("futureTopLevelSetting"));
            Assert.Equal("val_abc", cfg.Extra["futureTopLevelSetting"].ToString());
            Assert.True(cfg.Retention.Extra.ContainsKey("futureRetentionSetting"));
            Assert.Equal(12345, (int)cfg.Retention.Extra["futureRetentionSetting"]);

            cfg.Normalize();

            string serialized = VividJson.Write(cfg);
            Assert.Contains("\"futureTopLevelSetting\": \"val_abc\"", serialized);
            Assert.Contains("\"futureRetentionSetting\": 12345", serialized);
        }

        [Fact]
        public void Normalize_ReportsEveryClampedValue()
        {
            var cfg = new VividWorldConfig();
            cfg.Propagation.ChannelWeights.SameSettlement = 22.0;
            cfg.Relation.MeetFactorMax = 99.0;

            var notices = new System.Collections.Generic.List<ClampNotice>();
            cfg.Normalize(notices);

            Assert.Equal(10.0, cfg.Propagation.ChannelWeights.SameSettlement);
            Assert.Equal(10.0, cfg.Relation.MeetFactorMax);

            Assert.Equal(2, notices.Count);

            var noticeSettlement = notices.Find(n => n.Key.EndsWith("sameSettlement"));
            Assert.NotNull(noticeSettlement);
            Assert.Equal(22.0, noticeSettlement!.Requested);
            Assert.Equal(10.0, noticeSettlement.Applied);

            var noticeMeetMax = notices.Find(n => n.Key.EndsWith("meetFactorMax"));
            Assert.NotNull(noticeMeetMax);
            Assert.Equal(99.0, noticeMeetMax!.Requested);
            Assert.Equal(10.0, noticeMeetMax.Applied);

            // 同時斷言不傳 notices 時行為不變
            var cfgNoNotices = new VividWorldConfig();
            cfgNoNotices.Propagation.ChannelWeights.SameSettlement = 22.0;
            cfgNoNotices.Relation.MeetFactorMax = 99.0;
            cfgNoNotices.Normalize();

            Assert.Equal(10.0, cfgNoNotices.Propagation.ChannelWeights.SameSettlement);
            Assert.Equal(10.0, cfgNoNotices.Relation.MeetFactorMax);
        }

        /// <summary>
        /// 檔案裡的清單**取代** C# 預設清單，不是接在後面。
        /// Json.NET 的預設 ObjectCreationHandling.Auto 會沿用既有實例然後 Add，
        /// 註冊行因此印出「detected NaN, Lowborn, NaN, Lowborn」。
        /// </summary>
        [Fact]
        public void Deserialize_ListInFile_ReplacesTheDefaultList_DoesNotAppend()
        {
            const string json = @"{ ""dialogue"": { ""commonerCompatModules"": [ ""NaN"", ""Lowborn"" ] } }";

            var cfg = VividJson.Read<VividWorldConfig>(json);

            Assert.NotNull(cfg);
            Assert.Equal(new[] { "NaN", "Lowborn" }, cfg!.Dialogue.CommonerCompatModules);
        }

        /// <summary>檔案裡換掉一整份清單時，預設的兩筆不得殘留。</summary>
        [Fact]
        public void Deserialize_DifferentListInFile_LeavesNoDefaultLeftovers()
        {
            const string json = @"{ ""dialogue"": { ""commonerCompatModules"": [ ""SomeOtherMod"" ] } }";

            var cfg = VividJson.Read<VividWorldConfig>(json);

            Assert.NotNull(cfg);
            Assert.Equal(new[] { "SomeOtherMod" }, cfg!.Dialogue.CommonerCompatModules);
        }

        /// <summary>檔案裡沒提到就維持 C# 預設，一筆不多一筆不少。</summary>
        [Fact]
        public void Deserialize_ListAbsentFromFile_KeepsTheDefaultExactlyOnce()
        {
            const string json = @"{ ""dialogue"": { ""npcLinePriority"": 105 } }";

            var cfg = VividJson.Read<VividWorldConfig>(json);

            Assert.NotNull(cfg);
            Assert.Equal(new[] { "NaN", "Lowborn" }, cfg!.Dialogue.CommonerCompatModules);
        }

        [Fact]
        public void Normalize_ProminenceDrama_ClampsValuesAndProducesClampNotices()
        {
            var cfg = new VividWorldConfig();
            cfg.Events.PrisonerDramaByProminence.Ruler = 0;
            cfg.Events.PrisonerDramaByProminence.Minor = 9;

            var notices = new System.Collections.Generic.List<ClampNotice>();
            cfg.Normalize(notices);

            Assert.Equal(1, cfg.Events.PrisonerDramaByProminence.Ruler);
            Assert.Equal(5, cfg.Events.PrisonerDramaByProminence.Minor);

            var noticeRuler = notices.Find(n => n.Key == "events.prisonerDramaByProminence.ruler");
            Assert.NotNull(noticeRuler);
            Assert.Equal(0, noticeRuler!.Requested);
            Assert.Equal(1, noticeRuler.Applied);
            Assert.Equal("1..5", noticeRuler.AllowedRange);

            var noticeMinor = notices.Find(n => n.Key == "events.prisonerDramaByProminence.minor");
            Assert.NotNull(noticeMinor);
            Assert.Equal(9, noticeMinor!.Requested);
            Assert.Equal(5, noticeMinor.Applied);
            Assert.Equal("1..5", noticeMinor.AllowedRange);
        }

        [Fact]
        public void ConfigMerge_WithoutProminence_AddsAllFourKeysAndPreservesSources()
        {
            // 舊設定：有自訂 sources，但沒有 prisonerDramaByProminence
            const string oldJson = @"{
  ""events"": {
    ""catalogFile"": ""custom_events.json"",
    ""strictCatalog"": true,
    ""sources"": {
      ""heroKilled"": false,
      ""heroPrisonerTaken"": true,
      ""heroesMarried"": false,
      ""childBorn"": true
    }
  }
}";
            var existingJObj = Newtonsoft.Json.Linq.JObject.Parse(oldJson);
            var canonical = new VividWorldConfig().Normalize();
            var canonicalJObj = Newtonsoft.Json.Linq.JObject.Parse(VividJson.Write(canonical));

            var result = ConfigMerge.AddMissingKeys(existingJObj, canonicalJObj);

            Assert.Contains("events.prisonerDramaByProminence.ruler", result.AddedPaths);
            Assert.Contains("events.prisonerDramaByProminence.clanLeader", result.AddedPaths);
            Assert.Contains("events.prisonerDramaByProminence.nobleMember", result.AddedPaths);
            Assert.Contains("events.prisonerDramaByProminence.minor", result.AddedPaths);

            // 既有值維持不變
            Assert.Equal("custom_events.json", (string?)result.Merged.SelectToken("events.catalogFile"));
            Assert.True((bool?)result.Merged.SelectToken("events.strictCatalog"));
            Assert.False((bool?)result.Merged.SelectToken("events.sources.heroKilled"));
            Assert.True((bool?)result.Merged.SelectToken("events.sources.heroPrisonerTaken"));
            Assert.False((bool?)result.Merged.SelectToken("events.sources.heroesMarried"));
            Assert.True((bool?)result.Merged.SelectToken("events.sources.childBorn"));

            // 新補上的鍵有預設值
            Assert.Equal(5, (int?)result.Merged.SelectToken("events.prisonerDramaByProminence.ruler"));
            Assert.Equal(4, (int?)result.Merged.SelectToken("events.prisonerDramaByProminence.clanLeader"));
            Assert.Equal(2, (int?)result.Merged.SelectToken("events.prisonerDramaByProminence.nobleMember"));
            Assert.Equal(1, (int?)result.Merged.SelectToken("events.prisonerDramaByProminence.minor"));
        }

        [Fact]
        public void Normalize_ClampsReleaseDramaByProminence()
        {
            var cfg = new VividWorldConfig();
            cfg.Events.ReleaseDramaByProminence.Ruler = 10;
            cfg.Events.ReleaseDramaByProminence.ClanLeader = -2;
            cfg.Events.ReleaseDramaByProminence.NobleMember = 0;
            cfg.Events.ReleaseDramaByProminence.Minor = 6;

            var notices = new List<ClampNotice>();
            cfg.Normalize(notices);

            Assert.Equal(5, cfg.Events.ReleaseDramaByProminence.Ruler);
            Assert.Equal(1, cfg.Events.ReleaseDramaByProminence.ClanLeader);
            Assert.Equal(1, cfg.Events.ReleaseDramaByProminence.NobleMember);
            Assert.Equal(5, cfg.Events.ReleaseDramaByProminence.Minor);

            Assert.Equal(4, notices.Count(n => n.Key.StartsWith("events.releaseDramaByProminence")));
        }

        [Fact]
        public void ConfigMerge_AddsReleaseSettings_WhenMissing()
        {
            string oldJson = @"{
  ""configVersion"": 1,
  ""events"": {
    ""sources"": {
      ""heroKilled"": true,
      ""heroPrisonerTaken"": true
    }
  }
}";
            var existingJObj = Newtonsoft.Json.Linq.JObject.Parse(oldJson);
            var canonical = new VividWorldConfig().Normalize();
            var canonicalJObj = Newtonsoft.Json.Linq.JObject.Parse(VividJson.Write(canonical));

            var result = ConfigMerge.AddMissingKeys(existingJObj, canonicalJObj);

            Assert.Contains("events.sources.heroPrisonerReleased", result.AddedPaths);
            Assert.Contains("events.releaseDramaByProminence.ruler", result.AddedPaths);
            Assert.Contains("events.releaseDramaByProminence.clanLeader", result.AddedPaths);
            Assert.Contains("events.releaseDramaByProminence.nobleMember", result.AddedPaths);
            Assert.Contains("events.releaseDramaByProminence.minor", result.AddedPaths);

            Assert.True((bool?)result.Merged.SelectToken("events.sources.heroPrisonerReleased"));
            Assert.Equal(4, (int?)result.Merged.SelectToken("events.releaseDramaByProminence.ruler"));
            Assert.Equal(3, (int?)result.Merged.SelectToken("events.releaseDramaByProminence.clanLeader"));
            Assert.Equal(1, (int?)result.Merged.SelectToken("events.releaseDramaByProminence.nobleMember"));
            Assert.Equal(1, (int?)result.Merged.SelectToken("events.releaseDramaByProminence.minor"));
        }

        [Fact]
        public void Normalize_ClampsConsequenceSettings()
        {
            var cfg = new VividWorldConfig();
            cfg.Consequences.BystanderMultiplier = 2.5;
            cfg.Consequences.MaxAbsoluteDeltaPerHeroPerDay = 250.0;
            cfg.Consequences.MinAbsoluteDelta = 100;

            var notices = new List<ClampNotice>();
            cfg.Normalize(notices);

            Assert.Equal(1.0, cfg.Consequences.BystanderMultiplier);
            Assert.Equal(100.0, cfg.Consequences.MaxAbsoluteDeltaPerHeroPerDay);
            Assert.Equal(50, cfg.Consequences.MinAbsoluteDelta);

            Assert.Equal(3, notices.Count(n => n.Key.StartsWith("consequences.")));

            // Test lower bounds clamping
            cfg = new VividWorldConfig();
            cfg.Consequences.BystanderMultiplier = -0.5;
            cfg.Consequences.MaxAbsoluteDeltaPerHeroPerDay = -10.0;
            cfg.Consequences.MinAbsoluteDelta = -5;

            notices.Clear();
            cfg.Normalize(notices);

            Assert.Equal(0.0, cfg.Consequences.BystanderMultiplier);
            Assert.Equal(0.0, cfg.Consequences.MaxAbsoluteDeltaPerHeroPerDay);
            Assert.Equal(0, cfg.Consequences.MinAbsoluteDelta);
            Assert.Equal(3, notices.Count(n => n.Key.StartsWith("consequences.")));
        }

        [Fact]
        public void ConfigMerge_AddsConsequenceSettings_WhenMissing()
        {
            string oldJson = @"{
  ""configVersion"": 1,
  ""consequences"": {
    ""enabled"": false,
    ""relationDeltaByRole"": { ""mastermind"": -15 },
    ""unknownVictimMultiplier"": 0.5,
    ""minAbsoluteDelta"": 2
  }
}";
            var existingJObj = Newtonsoft.Json.Linq.JObject.Parse(oldJson);
            var canonical = new VividWorldConfig().Normalize();
            var canonicalJObj = Newtonsoft.Json.Linq.JObject.Parse(VividJson.Write(canonical));

            var result = ConfigMerge.AddMissingKeys(existingJObj, canonicalJObj);

            Assert.Contains("consequences.bystanderMultiplier", result.AddedPaths);
            Assert.Contains("consequences.maxAbsoluteDeltaPerHeroPerDay", result.AddedPaths);
            Assert.Contains("consequences.ledgerOnly", result.AddedPaths);

            // Existing values preserved
            Assert.False((bool?)result.Merged.SelectToken("consequences.enabled"));
            Assert.Equal(2, (int?)result.Merged.SelectToken("consequences.minAbsoluteDelta"));
            // Unknown/deprecated keys in user file are preserved
            Assert.Equal(-15, (int?)result.Merged.SelectToken("consequences.relationDeltaByRole.mastermind"));
            Assert.Equal(0.5, (double?)result.Merged.SelectToken("consequences.unknownVictimMultiplier"));

            // New keys added with canonical defaults
            Assert.Equal(0.35, (double?)result.Merged.SelectToken("consequences.bystanderMultiplier"));
            Assert.Equal(6.0, (double?)result.Merged.SelectToken("consequences.maxAbsoluteDeltaPerHeroPerDay"));
            Assert.False((bool?)result.Merged.SelectToken("consequences.ledgerOnly"));
        }
    }
}
