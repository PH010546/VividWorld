using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using TaleWorlds.CampaignSystem;
using VividWorld.Campaign;
using VividWorld.Core.Channels;
using VividWorld.Core.Config;
using VividWorld.Core.Diagnostics;
using VividWorld.Core.Events;
using VividWorld.Core.Grudges;
using VividWorld.Core.Memory;
using VividWorld.Core.Persistence;
using VividWorld.Core.Rumors;
using VividWorld.Core.Situations;

namespace VividWorld.Debug
{
    internal static class DevReport
    {
        public static string FormatKnownEvents(
            Hero hero,
            KnownByIndex knownBy,
            WorldEventStore store,
            RumorPropagationScheduler? scheduler = null)
        {
            if (hero == null) return "Invalid hero.";

            var eventIds = knownBy.EventsKnownBy(hero.StringId, CampaignTime.Now.ToDays);
            if (eventIds.Count == 0)
            {
                return $"{hero.Name} ({hero.StringId}) knows no rumor events.";
            }

            var sb = new StringBuilder();

            // 表頭要等逐則跑完才組得出來：`EventsKnownBy` 數的是「名字登記在內」，遺忘不在它的
            // 判斷裡，只有下面這個迴圈算得出誰真的還記得。先印「knows N」再印 N-1 個 (forgotten)，
            // 讀的人只會以為資料掉了。
            var body = new StringBuilder();
            int forgottenCount = 0;
            int secretKeptCount = 0;

            double currentDay = TaleWorlds.CampaignSystem.Campaign.Current != null ? CampaignTime.Now.ToDays : 0.0;
            string playerId = Hero.MainHero?.StringId ?? "player";
            bool isPlayer = string.Equals(hero.StringId, playerId, StringComparison.Ordinal);

            foreach (var eventId in eventIds)
            {
                var evt = store.Load(eventId);
                var entry = evt?.EntryFor(hero.StringId);
                string source = !string.IsNullOrEmpty(entry?.SourceHeroId) ? entry!.SourceHeroId! : "witness";
                string baseLine = string.Format(CultureInfo.InvariantCulture,
                    "  {0,-16} | hop {1} | source: {2,-12} | day: {3:0.0} | drama: {4}",
                    eventId,
                    entry?.Hop ?? -1,
                    source,
                    entry?.LearnedDay ?? 0.0,
                    evt?.DramaWeight ?? 0);

                string suffix;
                bool isSecretUnleaked = evt != null && evt.Origin == EventOrigin.Secret && (evt.State == null || !evt.State.Leaked);
                if (isPlayer)
                {
                    suffix = " | interest: - | forgets: - (player never forgets)";
                }
                else if (isSecretUnleaked)
                {
                    secretKeptCount++;
                    suffix = " | interest: - | forgets: - (secret not leaked - kept)";
                }
                else if (entry == null || entry.Interest == null)
                {
                    suffix = " | interest: - | forgets: - (unstamped)";
                }
                else
                {
                    bool forgotten = evt != null && Forgetting.IsForgotten(evt, entry, currentDay, playerId, store.Config.Memory);
                    double tellFactor = Forgetting.TellFactor(entry, store.Config.Memory);
                    int heardCount = entry.HeardCount ?? 0;
                    string interestSource = entry.InterestSource ?? "";
                    double forgetDay = entry.ForgetDay ?? 0.0;

                    if (forgotten)
                    {
                        forgottenCount++;
                        suffix = string.Format(CultureInfo.InvariantCulture,
                            " | interest: {0:0.00} ({1}) tell {2:0.00} heard {3} | forgets: {4:0.0} (forgotten)",
                            entry.Interest.Value,
                            interestSource,
                            tellFactor,
                            heardCount,
                            forgetDay);
                    }
                    else
                    {
                        suffix = string.Format(CultureInfo.InvariantCulture,
                            " | interest: {0:0.00} ({1}) tell {2:0.00} heard {3} | forgets: {4:0.0} (remembered)",
                            entry.Interest.Value,
                            interestSource,
                            tellFactor,
                            heardCount,
                            forgetDay);
                    }
                }

                if (entry != null && Outdating.IsOutdated(entry))
                {
                    string viaEvtId = "unknown";
                    // store 是非可空參數：這裡用 store?. 會讓流程分析認定它可能為 null，
                    // 連帶把同一個方法裡兩處 store.Load(...) 標成 CS8602
                    if (store.Index?.Entries != null)
                    {
                        var releaseEntry = store.Index.Entries
                            .Where(e => string.Equals(e.LinkedEventId, eventId, StringComparison.Ordinal)
                                        && e.KnownByHeroIds != null
                                        && e.KnownByHeroIds.Contains(hero.StringId))
                            .OrderByDescending(e => e.Day)
                            .ThenByDescending(e => e.EventId, StringComparer.Ordinal)
                            .FirstOrDefault();
                        if (releaseEntry != null && !string.IsNullOrEmpty(releaseEntry.EventId))
                        {
                            viaEvtId = releaseEntry.EventId;
                        }
                    }

                    suffix += string.Format(CultureInfo.InvariantCulture,
                        " (outdated day {0:0.0} via {1})",
                        entry.OutdatedDay ?? 0.0,
                        viaEvtId);
                }

                body.AppendLine(baseLine + suffix);
            }

            if (isPlayer)
            {
                sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                    "{0} ({1}) knows {2} event(s) - the player never forgets:",
                    hero.Name, hero.StringId, eventIds.Count));
            }
            else
            {
                string secretPart = secretKeptCount > 0
                    ? string.Format(CultureInfo.InvariantCulture, ", {0} unleaked secret(s) kept", secretKeptCount)
                    : string.Empty;
                sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                    "{0} ({1}) remembers {2} of {3} event(s) on file ({4} forgotten{5}):",
                    hero.Name, hero.StringId,
                    eventIds.Count - forgottenCount, eventIds.Count,
                    forgottenCount, secretPart));
            }
            sb.AppendLine("---------------------------------------------");
            sb.Append(body);

            if (scheduler != null && !isPlayer)
            {
                bool inRing = scheduler.IsInTellerRing(hero.StringId);
                int inactiveCount = 0;
                var activeEvents = new List<WorldEvent>();

                foreach (var eventId in eventIds)
                {
                    if (!scheduler.IsActiveEvent(eventId))
                    {
                        inactiveCount++;
                    }
                    else
                    {
                        var evt = store.Load(eventId);
                        if (evt == null)
                        {
                            inactiveCount++;
                        }
                        else
                        {
                            activeEvents.Add(evt);
                        }
                    }
                }

                double day = TaleWorlds.CampaignSystem.Campaign.Current != null ? CampaignTime.Now.ToDays : 0.0;
                int hour = TaleWorlds.CampaignSystem.Campaign.Current != null ? CampaignTime.Now.GetHourOfDay : 0;
                var choice = scheduler.Engine.ChooseTopic(hero.StringId, activeEvents, day, hour);
                string topicsLine = TellerLogFormatter.FormatTopicsIfSpokeNow(choice, inRing, inactiveCount);
                sb.AppendLine(topicsLine);
            }

            return sb.ToString().TrimEnd();
        }

        public static string FormatContactMultipliers(Hero hero, GameWorldChannel channel, VividWorldConfig config)
        {
            if (hero == null) return "Invalid hero.";

            var quotaResult = channel.QueryDetailedContacts(hero.StringId, out _, out var emptyReasons);
            var selected = quotaResult.Selected;
            var squeezed = quotaResult.SqueezedOut;

            int inPersonCount = selected.Count(c => ChannelClass.IsInPerson(c.Kind));
            int remoteCount = selected.Count(c => !ChannelClass.IsInPerson(c.Kind));

            string? factionName = hero.Clan?.Kingdom?.Name?.ToString() ?? hero.Clan?.Name?.ToString();
            string header = string.IsNullOrEmpty(factionName)
                ? string.Format(CultureInfo.InvariantCulture,
                    "{0} ({1}) can reach {2} [in-person {3} / remote {4}]",
                    hero.Name, hero.StringId, selected.Count, inPersonCount, remoteCount)
                : string.Format(CultureInfo.InvariantCulture,
                    "{0} of {1} ({2}) can reach {3} [in-person {4} / remote {5}]",
                    hero.Name, factionName, hero.StringId, selected.Count, inPersonCount, remoteCount);

            var sb = new StringBuilder();
            sb.AppendLine(header);
            sb.AppendLine("---------------------------------------------");

            if (selected.Count == 0)
            {
                sb.AppendLine("  (no active contacts)");
            }
            else
            {
                foreach (var link in selected)
                {
                    var contactHero = Hero.Find(link.HeroId);
                    string contactName = contactHero?.Name?.ToString() ?? link.HeroId;
                    double chWeight = config.Propagation.ChannelWeights.For(link.Kind);
                    double rf = RelationFactor.For(link.Kind, link.Relation, config.Relation);
                    double product = chWeight * rf;

                    string relStr = link.Relation >= 0 ? $"+{link.Relation}" : $"{link.Relation}";

                    sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                        "  {0,-15} {1,-10} rel {2,4}   ch {3:0.00}  rf {4:0.00}   ->  {5:0.00}",
                        link.Kind, contactName, relStr, chWeight, rf, product));
                }
            }

            sb.AppendLine("---------------------------------------------");

            // Teller trait multiplier calculation
            double twGenerosity = config.Propagation.TellerTraitWeights.Generosity;
            double twHonor = config.Propagation.TellerTraitWeights.Honor;
            double twCalc = config.Propagation.TellerTraitWeights.Calculating;
            int g = hero.GetTraitLevel(TaleWorlds.CampaignSystem.CharacterDevelopment.DefaultTraits.Generosity);
            int h = hero.GetTraitLevel(TaleWorlds.CampaignSystem.CharacterDevelopment.DefaultTraits.Honor);
            int cLevel = hero.GetTraitLevel(TaleWorlds.CampaignSystem.CharacterDevelopment.DefaultTraits.Calculating);
            double tellerMultiplier = 1.0 + (twGenerosity * g) + (twHonor * h) + (twCalc * cLevel);
            if (tellerMultiplier < config.Propagation.TellerMultiplierMin) tellerMultiplier = config.Propagation.TellerMultiplierMin;
            if (tellerMultiplier > config.Propagation.TellerMultiplierMax) tellerMultiplier = config.Propagation.TellerMultiplierMax;

            double relScale = config.Relation.EffectiveScale;
            double baseChance = config.Propagation.BaseTellChancePerContact;

            sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                "  relationScale={0:0}  teller x={1:0.00}  base={2:0.00}",
                relScale, tellerMultiplier, baseChance));

            if (squeezed.Count > 0)
            {
                int maxCrowdedOut = config.Debug.DevReportMaxCrowdedOut;
                var grouped = squeezed.GroupBy(x => x.Kind);
                foreach (var gGroup in grouped)
                {
                    sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                        "  crowded out: {0} {1}", gGroup.Key, gGroup.Count()));

                    var sorted = gGroup
                        .OrderByDescending(link => config.Propagation.ChannelWeights.For(link.Kind) * RelationFactor.For(link.Kind, link.Relation, config.Relation))
                        .ThenBy(link => link.HeroId, StringComparer.Ordinal)
                        .ToList();

                    int showCount = Math.Min(maxCrowdedOut, sorted.Count);
                    for (int i = 0; i < showCount; i++)
                    {
                        var link = sorted[i];
                        var contactHero = Hero.Find(link.HeroId);
                        string contactName = contactHero?.Name?.ToString() ?? link.HeroId;
                        string nameAndId = $"{contactName} ({link.HeroId})";

                        double chWeight = config.Propagation.ChannelWeights.For(link.Kind);
                        double rf = RelationFactor.For(link.Kind, link.Relation, config.Relation);
                        double product = chWeight * rf;

                        string relStr = link.Relation >= 0 ? $"+{link.Relation}" : $"{link.Relation}";

                        sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                            "      {0,-20} rel {1,3}   ch {2:0.00}  rf {3:0.00}  ->  {4:0.00}",
                            nameAndId, relStr, chWeight, rf, product));
                    }

                    if (sorted.Count > showCount)
                    {
                        sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                            "      ... {0} more", sorted.Count - showCount));
                    }
                }
            }

            if (emptyReasons.Count > 0)
            {
                var reasonList = emptyReasons.Select(kv => $"{kv.Key} ({kv.Value})");
                sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                    "  no candidates: {0}", string.Join(", ", reasonList)));
            }

            return sb.ToString().TrimEnd();
        }

        public static string FormatWorldStatus(
            RumorPropagationScheduler scheduler,
            WorldEventStore store,
            VividWorldConfig config,
            int rollbackCount = 0,
            double rollbackMaxDay = 0.0,
            double launchDay = 0.0,
            int volunteeredToday = 0,
            int maxVolunteers = 1,
            string? lastVolunteerInfo = null,
            string? commonerCompatInfo = null,
            IReadOnlyList<string>? lastConversationRoute = null,
            string? volunteerRecoveryInfo = null,
            RealEventSourceBehavior? realEvents = null,
            MemoryStamper? stamper = null,
            SnapshotSessionState? sessionState = null,
            string? campaignId = null,
            PlayerHeardLogStore? playerHeardLog = null)
        {
            double daysInYear = CampaignTime.DaysInYear;
            double scale = daysInYear > 0 ? (daysInYear / CalendarScaling.NativeDaysInYear) : 1.0;

            var sb = new StringBuilder();
            sb.AppendLine("World Status:");
            sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                "- Active propagation ring size: {0}", scheduler.ActiveRingSize));
            sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                "- Secret watch list size: {0}", scheduler.SecretWatchSize));
            int inertCount = 0;
            int propagatableCount = 0;
            if (scheduler != null && store != null && store.Traits != null)
            {
                foreach (var eventId in scheduler.ActiveRing)
                {
                    var evt = store.Load(eventId);
                    if (evt == null) continue;
                    int maxHop = scheduler.Engine.MaxHopFor(evt);
                    bool hasEligibleKnower = evt.KnownBy != null && evt.KnownBy.Any(k =>
                        k.Hop < maxHop &&
                        !string.IsNullOrEmpty(k.HeroId) &&
                        Eligibility.IsEligible(store.Traits, k.HeroId));
                    if (hasEligibleKnower)
                    {
                        propagatableCount++;
                    }
                    else
                    {
                        inertCount++;
                    }
                }
            }

            if (rollbackCount > 0)
            {
                sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                    "- (!) rollback detected at launch: {0} events dated after day {1:F1} (max {2:F1}) - two timelines mixed, the counts below are not clean",
                    rollbackCount, launchDay, rollbackMaxDay));
            }

            int totalIndexed = store?.Index?.Entries?.Count ?? 0;
            sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                "- Total indexed events: {0} (propagatable {1}, inert {2})", totalIndexed, propagatableCount, inertCount));
            // 游標本身對不回人（讀檔時輪是重建的，§7.3.1），所以連「下一個輪到誰」一起印
            string nextTellerId = scheduler?.TickCursorHeroId ?? string.Empty;
            string nextTeller = string.IsNullOrEmpty(nextTellerId)
                ? "(ring is empty)"
                : (Hero.Find(nextTellerId)?.Name?.ToString() ?? nextTellerId) + " (" + nextTellerId + ")";
            sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                "- Scheduler tick cursor: {0} of {1} (teller ring) - next teller {2}",
                scheduler?.TickCursor ?? 0, scheduler?.TellerRingCount ?? 0, nextTeller));
            sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                "- Calendar DaysInYear: {0:0.0} (native 84, scale {1:0.000})", daysInYear, scale));
            sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                "- Relation EffectiveScale: {0:0.0} (ScaleOverride: {1})",
                config.Relation.EffectiveScale, config.Relation.ScaleOverride));
            sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                "- Volunteered today: {0} / {1}", volunteeredToday, maxVolunteers));
            sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                "- Last volunteer: {0}", lastVolunteerInfo ?? "(none this session)"));
            sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                "- Volunteer recovery: {0}", volunteerRecoveryInfo ?? "(unknown - dialogue behavior not wired)"));
            sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                "- {0}", commonerCompatInfo ?? "Commoner compat: (unknown - dialogue behavior not wired)"));
            // 行為沒接上時不要照樣印「4 subscribed」——那一列會變成在說它不知道的事。
            sb.AppendLine(realEvents == null
                ? "- Real event sources: (unknown - behavior not wired)"
                : string.Format(CultureInfo.InvariantCulture,
                    "- Real event sources: {0} subscribed, {1} events submitted this session, where kept {2} / dropped {3}",
                    realEvents.SubscribedCount, realEvents.EventsSubmittedThisSession, realEvents.WhereKeptCount, realEvents.WhereDroppedCount));
            sb.AppendLine(realEvents == null
                ? "- Prisoner prominence this session: (unknown - behavior not wired)"
                : string.Format(CultureInfo.InvariantCulture,
                    "- Prisoner prominence this session: ruler {0}, clanLeader {1}, nobleMember {2}, minor {3} (drama {4}/{5}/{6}/{7}), failed {8}",
                    realEvents.ProminenceRulerCount,
                    realEvents.ProminenceClanLeaderCount,
                    realEvents.ProminenceNobleMemberCount,
                    realEvents.ProminenceMinorCount,
                    config.Events.PrisonerDramaByProminence.Ruler,
                    config.Events.PrisonerDramaByProminence.ClanLeader,
                    config.Events.PrisonerDramaByProminence.NobleMember,
                    config.Events.PrisonerDramaByProminence.Minor,
                    realEvents.ProminenceFailedCount));

            var activeStamper = stamper ?? store?.Stamper;
            int entriesMarkedOutdated = activeStamper?.EntriesMarkedOutdatedCount ?? 0;
            int eventsDormantByOutdating = activeStamper?.EventsDormantByOutdatingCount ?? 0;

            sb.AppendLine(realEvents == null
                ? "- Releases this session: (unknown - behavior not wired)"
                : string.Format(CultureInfo.InvariantCulture,
                    "- Releases this session: {0} (escaped {1}, released {2}), linked {3} / unlinked {4}, entries marked outdated {5}, events dormant by outdating {6}",
                    realEvents.ReleasesCount,
                    realEvents.ReleaseEscapedCount,
                    realEvents.ReleaseReleasedCount,
                    realEvents.ReleaseLinkedCount,
                    realEvents.ReleaseUnlinkedCount,
                    entriesMarkedOutdated,
                    eventsDormantByOutdating));

            var sitCatalog = SituationCatalogStore.Catalog;
            int loadedCount = sitCatalog?.Situations?.Count ?? 0;
            string sitIds = loadedCount > 0
                ? $" ({string.Join(", ", sitCatalog!.Situations.Select(s => s.Id))})"
                : string.Empty;
            sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                "- Situations: {0} loaded{1}, forced this session {2}, events submitted {3}, no-branch {4}",
                loadedCount,
                sitIds,
                SituationRunner.ForcedThisSession,
                SituationRunner.EventsSubmittedThisSession,
                SituationRunner.NoBranchThisSession));

            if (config.Situations != null && !config.Situations.GrudgesEnabled)
            {
                sb.AppendLine("- Grudges: disabled (situations.grudgesEnabled = false)");
            }
            else
            {
                int totalPairs = store?.Grudges?.Pairs.Count ?? 0;
                int personalPairs = store?.Grudges?.PairCount(GrudgeScope.Personal) ?? 0;
                int clanPairs = store?.Grudges?.PairCount(GrudgeScope.Clan) ?? 0;
                sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                    "- Grudges: {0} pair(s) (personal {1}, clan {2}); this session applied {3}, ledger-only {4}, escalated {5}, skipped {6}",
                    totalPairs,
                    personalPairs,
                    clanPairs,
                    GrudgeApplier.AppliedThisSession,
                    GrudgeApplier.LedgerOnlyThisSession,
                    GrudgeApplier.EscalatedThisSession,
                    GrudgeApplier.SkippedThisSession));
            }

            if (config.Situations != null && !config.Situations.DailyScanEnabled)
            {
                sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                    "- Situation scan: disabled (situations.dailyScanEnabled = false); this session triggered by scan {0}",
                    SituationRunner.ScanTriggeredThisSession));
            }
            else
            {
                var scanConfig = config.Situations?.Scan ?? new SituationScanConfig();
                string lastScanPart = SituationScanBehavior.HasScanned
                    ? string.Format(CultureInfo.InvariantCulture,
                        "last scan day {0}: {1} pair(s), {2} candidate(s), triggered {3}",
                        SituationScanLogFormatter.FormatDay(SituationScanBehavior.LastScanDay!.Value),
                        SituationScanBehavior.LastScanPairsEvaluated,
                        SituationScanBehavior.LastScanCandidatesPassed,
                        SituationScanBehavior.LastScanTriggered)
                    : "no scan yet";

                sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                    "- Situation scan: enabled, maxPerDay {0:0.00}, cap {1} pair(s)/settlement, {2} candidate(s); {3}; this session triggered by scan {4}",
                    config.Situations?.MaxPerDay ?? 1.5,
                    scanConfig.MaxPairsPerSettlement,
                    scanConfig.MaxCandidates,
                    lastScanPart,
                    SituationRunner.ScanTriggeredThisSession));
            }

            var countsByType = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            if (store?.Index?.Entries != null)
            {
                foreach (var entry in store.Index.Entries)
                {
                    if (entry == null || string.IsNullOrEmpty(entry.Type)) continue;
                    int knowerCount = entry.KnownByHeroIds?.Count ?? 0;
                    if (countsByType.TryGetValue(entry.Type, out int curCount))
                    {
                        countsByType[entry.Type] = curCount + knowerCount;
                    }
                    else
                    {
                        countsByType[entry.Type] = knowerCount;
                    }
                }
            }
            sb.AppendLine(KnowledgeShareFormatter.Format(countsByType));

            int remembered = 0;
            int forgotten = 0;
            int unstamped = 0;
            int eventsAllForgotten = 0;
            var rememberedByType = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            int[] dramaEventCount = new int[5];
            int[] dramaHopGte1Total = new int[5];
            int[] dramaHopGte1Remembered = new int[5];

            double currentDay = TaleWorlds.CampaignSystem.Campaign.Current != null ? CampaignTime.Now.ToDays : 0.0;
            string mainHeroId = Hero.MainHero?.StringId ?? "player";

            if (store?.Index?.Entries != null)
            {
                foreach (var indexEntry in store.Index.Entries)
                {
                    if (indexEntry == null || string.IsNullOrEmpty(indexEntry.EventId)) continue;
                    var evt = store.Load(indexEntry.EventId);
                    if (evt == null || evt.KnownBy == null) continue;

                    bool isSecretUnleaked = evt.Origin == EventOrigin.Secret && (evt.State == null || !evt.State.Leaked);
                    if (!isSecretUnleaked)
                    {
                        int d = Math.Max(1, Math.Min(5, evt.DramaWeight));
                        dramaEventCount[d - 1]++;
                        foreach (var k in evt.KnownBy)
                        {
                            if (string.IsNullOrEmpty(k.HeroId) || string.Equals(k.HeroId, mainHeroId, StringComparison.Ordinal))
                                continue;
                            if (k.Hop >= 1)
                            {
                                dramaHopGte1Total[d - 1]++;
                                if (!Forgetting.IsForgotten(evt, k, currentDay, mainHeroId, config.Memory))
                                {
                                    dramaHopGte1Remembered[d - 1]++;
                                }
                            }
                        }
                    }

                    var npcEntries = evt.KnownBy
                        .Where(k => !string.IsNullOrEmpty(k.HeroId) && !string.Equals(k.HeroId, mainHeroId, StringComparison.Ordinal))
                        .ToList();

                    if (npcEntries.Count == 0) continue;

                    bool allForgotten = true;
                    int eventRememberedCount = 0;

                    foreach (var k in npcEntries)
                    {
                        if (Forgetting.IsForgotten(evt, k, currentDay, mainHeroId, config.Memory))
                        {
                            forgotten++;
                        }
                        else if (k.Interest == null)
                        {
                            unstamped++;
                            allForgotten = false;
                            eventRememberedCount++;
                        }
                        else
                        {
                            remembered++;
                            allForgotten = false;
                            eventRememberedCount++;
                        }
                    }

                    if (allForgotten)
                    {
                        eventsAllForgotten++;
                    }

                    if (eventRememberedCount > 0 && !string.IsNullOrEmpty(evt.Type))
                    {
                        if (rememberedByType.TryGetValue(evt.Type, out int cur))
                        {
                            rememberedByType[evt.Type] = cur + eventRememberedCount;
                        }
                        else
                        {
                            rememberedByType[evt.Type] = eventRememberedCount;
                        }
                    }
                }
            }

            sb.AppendLine(MemoryLogFormatter.FormatWorldStatus(config.Memory, remembered, forgotten, unstamped, eventsAllForgotten));
            sb.AppendLine(KnowledgeShareFormatter.FormatRemembered(rememberedByType));

            var dramaStats = new (int drama, int eventCount, int hopGte1Total, int hopGte1Remembered)[5];
            for (int d = 1; d <= 5; d++)
            {
                dramaStats[d - 1] = (d, dramaEventCount[d - 1], dramaHopGte1Total[d - 1], dramaHopGte1Remembered[d - 1]);
            }
            sb.AppendLine(TellerLogFormatter.FormatWorldStatusTellerRing(
                scheduler?.TellerRingCount ?? 0,
                config.Scheduling.TellersPerHourlyTick,
                scheduler?.SessionTurns ?? 0,
                scheduler?.SessionToldByDrama ?? new int[5],
                scheduler?.SessionSkippedNotEligible ?? 0,
                scheduler?.SessionLeftRing ?? 0));
            sb.AppendLine(TellerLogFormatter.FormatWorldStatusReheard(
                scheduler?.SessionReheardCounted ?? 0,
                scheduler?.SessionReheardSameTellerIgnored ?? 0,
                scheduler?.SessionReheardSameTellerRelearned ?? 0));
            sb.AppendLine(TellerLogFormatter.FormatWorldStatusSpreadByDrama(dramaStats));

            string snapCampaignId = string.IsNullOrEmpty(campaignId) ? "legacy" : campaignId!;
            string snapRoot = VividWorldPaths.CampaignDirectory(snapCampaignId);
            var snapInv = RumorSnapshotStore.Inventory(snapRoot);
            sb.AppendLine(SnapshotLogFormatter.FormatWorldStatusSnapshots(
                snapInv,
                config.Persistence.MaxSnapshots,
                sessionState?.LastTake,
                sessionState?.LastRestore));

            if (playerHeardLog != null)
            {
                int n = playerHeardLog.Count;
                int v = playerHeardLog.VisibleEntries(currentDay).Count();
                int h = playerHeardLog.HiddenFutureCount(currentDay);
                if (h > 0)
                {
                    sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                        "- Player heard-log: {0} on file, {1} visible today, {2} hidden from a future timeline",
                        n, v, h));
                }
                else
                {
                    sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                        "- Player heard-log: {0} on file, {1} visible today",
                        n, v));
                }
            }

            if (store?.Index != null)
            {
                double today = CampaignTime.Now.ToDays;
                string playerHeroId = store.PlayerHeroId;
                double? situationMinAgeDays = null;
                var catalog = SituationCatalogStore.Catalog;
                if (!string.IsNullOrEmpty(SituationCatalogStore.ActiveFilePath) && catalog != null && catalog.Situations.Count > 0)
                {
                    situationMinAgeDays = catalog.MaxCooldownDays();
                }

                var purgePlan = EventPurgePlanner.Plan(
                    store.Index,
                    today,
                    playerHeroId,
                    config.Memory,
                    situationMinAgeDays,
                    id => playerHeardLog?.Contains(id) ?? false);

                sb.AppendLine(EventPurgeLogFormatter.FormatWorldStatusPreview(purgePlan, situationMinAgeDays));
            }

            int shardCacheM = store?.ShardStore?.CachedShardCount ?? 0;
            int shardCacheE = store?.ShardStore?.CachedEventCount ?? 0;
            int shardCacheR = store?.ShardStore?.ReleasedTotal ?? 0;
            int shardCacheK = config?.Persistence?.ShardCacheIdleFlushes ?? 0;
            sb.AppendLine(ShardCacheLogFormatter.FormatWorldStatus(shardCacheM, shardCacheE, shardCacheR, shardCacheK));

            // 上一場對話真正走過的路線（帳本 D-50／L-25／X-19）。主動講那條掛在 `lord_start`，
            // 而 `lord_start` 不是每一場都會走到——路線裡沒有它，就是被別人從 `start` 帶走了。
            if (lastConversationRoute != null && lastConversationRoute.Count > 0)
            {
                sb.AppendLine("- Last conversation route (every line the engine actually picked):");
                foreach (string line in lastConversationRoute)
                {
                    sb.AppendLine("    " + line);
                }
            }
            else
            {
                sb.AppendLine("- Last conversation route: (none recorded yet - talk to someone first, "
                              + "or debugDialogueEnabled is off)");
            }

            var mainHero = Hero.MainHero;
            string playerName = mainHero?.Name?.ToString() ?? "player";
            string playerId = mainHero?.StringId ?? "player";
            var playerKnown = store?.KnownBy?.EventsKnownBy(playerId, CampaignTime.Now.ToDays) ?? (IReadOnlyList<string>)Array.Empty<string>();
            int knownCount = playerKnown.Count;
            int aboutCount = store?.KnownBy?.EventsAbout(playerId, CampaignTime.Now.ToDays).Count ?? 0;

            int participantCount = 0;
            int toldByNpcCount = 0;
            foreach (var eid in playerKnown)
            {
                var evt = store?.Load(eid);
                var entry = evt?.EntryFor(playerId);
                if (entry != null && !string.IsNullOrEmpty(entry.SourceHeroId))
                {
                    toldByNpcCount++;
                }
                else
                {
                    participantCount++;
                }
            }

            sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                "player: {0} ({1})", playerName, playerId));
            sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                "  known events: {0}  (as participant {1}, told by an NPC {2})", knownCount, participantCount, toldByNpcCount));
            sb.AppendLine("      ^ only \"told by an NPC\" is bound by the §6.6 invariant; participation is not (§6.6.0)");
            sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                "  events about you: {0}", aboutCount));
            sb.AppendLine();
            sb.AppendLine(DevMetrics.DetailedReport());
            sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                "opinion shifts: {0} applied / {1} ledger-only / {2} skipped this session",
                GrudgeApplier.OpinionAppliedThisSession,
                GrudgeApplier.OpinionLedgerOnlyThisSession,
                GrudgeApplier.OpinionSkippedThisSession));

            return sb.ToString().TrimEnd();
        }

        public static string FormatOpinionShifts(
            string heroId,
            WorldEventStore store,
            DailyRelationBudget? budget,
            VividWorldConfig config,
            double day)
        {
            var sb = new StringBuilder();

            var impacts = new List<(WorldEvent Event, KnownByEntry Knower, RelationImpact Impact)>();
            if (store?.Index?.Entries != null)
            {
                foreach (var indexEntry in store.Index.Entries)
                {
                    if (indexEntry == null || string.IsNullOrEmpty(indexEntry.EventId)) continue;
                    // SE2 的 HasGrudges 標記就是為了不必把每一片都讀回來（規格 §10.4.6）。
                    // 沒有這道過濾，按一次這條 dev 行就會把整個戰役的分片全部讀進快取。
                    if (!indexEntry.HasGrudges) continue;
                    var evt = store.Load(indexEntry.EventId);
                    if (evt == null) continue;
                    var knower = evt.EntryFor(heroId);
                    if (knower?.RelationImpacts == null) continue;
                    foreach (var impact in knower.RelationImpacts)
                    {
                        if (impact != null && impact.Source == GrudgeSource.Rumor)
                        {
                            impacts.Add((evt, knower, impact));
                        }
                    }
                }
            }

            impacts.Sort((a, b) => b.Impact.AppliedDay.CompareTo(a.Impact.AppliedDay));

            double used = budget?.ConsumedToday(heroId, day) ?? 0.0;
            // 不要在這裡寫死預設值：它會跟 ConsequenceConfig 的預設值各走各的。
            double cap = (config?.Consequences ?? new ConsequenceConfig()).MaxAbsoluteDeltaPerHeroPerDay;

            if (impacts.Count == 0)
            {
                sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                    "Opinion shifts for {0} (day {1:0.0}): no opinion shifts from rumors recorded",
                    heroId, day));
                sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                    "today's budget: {0:0.0} of {1:0.0}", used, cap));
            }
            else
            {
                sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                    "Opinion shifts for {0} (day {1:0.0}): {2} shift(s) from rumors, budget {3:0.0}/{4:0.0} used today",
                    heroId, day, impacts.Count, used, cap));

                var heroA = Hero.Find(heroId);

                foreach (var item in impacts)
                {
                    var impact = item.Impact;
                    var evt = item.Event;
                    var knowerEntry = item.Knower;

                    if (impact.LedgerOnly)
                    {
                        sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                            "  vs {0} {1} hop {2}: requested {3:+0.0;-0.0} (ledger only), day {4:0.0}",
                            impact.AboutHeroId, evt.EventId, knowerEntry.Hop, impact.Requested, impact.AppliedDay));
                    }
                    else
                    {
                        // 只印「當初量到的變動」與「現在的值」。
                        // 不要用 current - Delta 回推當初的 before：同一對人只要再被改過一次
                        // （另一則傳聞、情境恩怨、原生自己的來源），那個數字就是錯的，
                        // 而它印出來會長得像量測值。
                        var heroB = Hero.Find(impact.AboutHeroId);
                        string nowText = (heroA != null && heroB != null)
                            ? heroA.GetBaseHeroRelation(heroB).ToString(CultureInfo.InvariantCulture)
                            : "unknown (hero not found)";
                        sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                            "  vs {0} {1} hop {2}: requested {3:+0.0;-0.0}, applied {4:+0;-0} on day {5:0.0}, base relation now {6}",
                            impact.AboutHeroId, evt.EventId, knowerEntry.Hop, impact.Requested, impact.Delta, impact.AppliedDay, nowText));
                    }
                }
            }

            return sb.ToString().TrimEnd();
        }

        public static string FormatEventRoster(WorldEvent evt, RumorEngine engine, HeroLookup? heroLookup)
        {
            if (evt == null || engine == null) return "(dev) no event data";

            int maxHop = engine.MaxHopFor(evt);
            var sb = new StringBuilder();
            sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                "{0}  {1}  day {2:0.0}  drama {3}  maxHop {4}",
                evt.EventId, evt.Type, evt.Day, evt.DramaWeight, maxHop));

            var orderedKnowers = (evt.KnownBy ?? (IReadOnlyList<KnownByEntry>)Array.Empty<KnownByEntry>())
                .OrderBy(k => k.Hop)
                .ThenBy(k => k.HeroId, StringComparer.Ordinal)
                .ToList();

            foreach (var k in orderedKnowers)
            {
                string knwId = k.HeroId ?? string.Empty;
                var h = !string.IsNullOrEmpty(knwId) ? (heroLookup?.Get(knwId) ?? Hero.Find(knwId)) : null;
                string heroName = h?.Name?.ToString() ?? k.HeroId ?? "?";
                string location = h?.CurrentSettlement?.Name?.ToString() ?? "travelling";
                string source = string.IsNullOrEmpty(k.SourceHeroId) ? "-" : k.SourceHeroId!;

                sb.AppendLine(MemoryLogFormatter.FormatRosterLine(k.Hop, heroName, k.HeroId, location, source, k.OutdatedDay));
            }

            sb.AppendLine("  ---");

            var distinctHops = orderedKnowers.Select(k => k.Hop).Distinct().OrderBy(h => h).ToList();
            foreach (int hop in distinctHops)
            {
                var facts = engine.FactsAtHop(evt, hop);
                string categories = facts != null && facts.Count > 0
                    ? string.Join(", ", facts.Select(f => f.Category.ToString().ToLowerInvariant()))
                    : "none";
                sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                    "  facts at hop {0}: {1}", hop, categories));
            }

            return sb.ToString().TrimEnd();
        }

        public static RumorIndexEntry? FindNewestUnleakedSecret(WorldEventStore store)
        {
            if (store?.Index?.Entries == null) return null;
            return store.Index.Entries
                .Where(e => e.Secret && !e.Leaked && !e.Dormant)
                .OrderByDescending(e => e.Day)
                .ThenByDescending(e => e.EventId, StringComparer.Ordinal)
                .FirstOrDefault();
        }

        public static string FormatKnownEventsSummary(Hero hero, KnownByIndex knownBy, WorldEventStore store)
        {
            if (hero == null) return "(dev) no result - see log.txt";

            var eventIds = knownBy.EventsKnownBy(hero.StringId, CampaignTime.Now.ToDays);
            if (eventIds.Count == 0)
            {
                return string.Format(CultureInfo.InvariantCulture,
                    "(dev) {0} knows 0 events - details in log", hero.Name);
            }

            int minHop = int.MaxValue;
            int maxHop = int.MinValue;
            int forgottenCount = 0;
            double currentDay = TaleWorlds.CampaignSystem.Campaign.Current != null ? CampaignTime.Now.ToDays : 0.0;
            string playerId = Hero.MainHero?.StringId ?? "player";
            foreach (var eventId in eventIds)
            {
                var evt = store.Load(eventId);
                var entry = evt?.EntryFor(hero.StringId);
                if (entry != null)
                {
                    if (entry.Hop < minHop) minHop = entry.Hop;
                    if (entry.Hop > maxHop) maxHop = entry.Hop;

                    // 跟表頭那一版同一個判斷（`Forgetting.IsForgotten`），不自己寫第二份。
                    if (evt != null && Forgetting.IsForgotten(evt, entry, currentDay, playerId, store.Config.Memory))
                    {
                        forgottenCount++;
                    }
                }
            }

            string hopsPart = (minHop <= maxHop)
                ? (minHop == maxHop
                    ? string.Format(CultureInfo.InvariantCulture, " (hop {0})", minHop)
                    : string.Format(CultureInfo.InvariantCulture, " (hops {0}-{1})", minHop, maxHop))
                : "";

            string eventWord = eventIds.Count == 1 ? "event" : "events";
            if (forgottenCount > 0)
            {
                return string.Format(CultureInfo.InvariantCulture,
                    "(dev) {0} remembers {1} of {2} {3} on file{4} - details in log",
                    hero.Name, eventIds.Count - forgottenCount, eventIds.Count, eventWord, hopsPart);
            }
            return string.Format(CultureInfo.InvariantCulture,
                "(dev) {0} has {1} {2} on file{3} - details in log",
                hero.Name, eventIds.Count, eventWord, hopsPart);
        }

        public static string FormatContactSummary(Hero hero, GameWorldChannel channel)
        {
            if (hero == null) return "(dev) no result - see log.txt";

            var quotaResult = channel.QueryDetailedContacts(hero.StringId, out _, out _);
            var selected = quotaResult.Selected;
            var squeezed = quotaResult.SqueezedOut;

            int inPersonCount = selected.Count(c => ChannelClass.IsInPerson(c.Kind));
            int remoteCount = selected.Count(c => !ChannelClass.IsInPerson(c.Kind));

            return string.Format(CultureInfo.InvariantCulture,
                "(dev) {0} contacts [in-person {1} / remote {2}], {3} crowded out - details in log",
                selected.Count, inPersonCount, remoteCount, squeezed.Count);
        }

        public static string FormatWorldStatusSummary(RumorPropagationScheduler scheduler, WorldEventStore store)
        {
            int ring = scheduler?.ActiveRingSize ?? 0;
            int secrets = scheduler?.SecretWatchSize ?? 0;
            int events = store?.Index?.Entries?.Count ?? 0;
            double today = CampaignTime.Now.ToDays;
            int hiddenFuture = store?.Index?.HiddenFutureCount(today) ?? 0;
            string playerId = Hero.MainHero?.StringId ?? "player";
            int playerKnows = store?.KnownBy?.EventsKnownBy(playerId, today)?.Count ?? 0;

            // 「事件 N 則」與「玩家知道 M 則」對不起來時，差額常常就是被藏起來的那幾則（規格 §2.2.1）。
            // 藏了 0 則就不印，免得每個玩家都看到一個他不需要知道的名詞。
            string hiddenPart = hiddenFuture > 0
                ? string.Format(CultureInfo.InvariantCulture, " ({0} hidden from a future timeline)", hiddenFuture)
                : string.Empty;

            return string.Format(CultureInfo.InvariantCulture,
                "(dev) ring {0} / secrets {1} / events {2}{4} / player knows {3}",
                ring, secrets, events, playerKnows, hiddenPart);
        }
    }
}
