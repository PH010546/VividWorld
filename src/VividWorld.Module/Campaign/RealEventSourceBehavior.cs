using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using VividWorld.Core.Catalog;
using VividWorld.Core.Config;
using VividWorld.Core.Events;
using VividWorld.Core.Ingest;
using VividWorld.Core.Presentation;
using VividWorld.Core.Rumors;

namespace VividWorld.Campaign
{
    internal sealed class RealEventSourceBehavior : CampaignBehaviorBase
    {
        private readonly VividWorldConfig _config;
        private WorldEventStore? _eventStore;
        private IHeroTraitLookup? _traitLookup;
        private int _eventsSubmittedThisSession;
        private int _whereKeptCount;
        private int _whereDroppedCount;
        private int _prominenceRulerCount;
        private int _prominenceClanLeaderCount;
        private int _prominenceNobleMemberCount;
        private int _prominenceMinorCount;
        private int _prominenceFailedCount;
        private int _releasesCount;
        private int _releaseEscapedCount;
        private int _releaseReleasedCount;
        private int _releaseLinkedCount;
        private int _releaseUnlinkedCount;
        private int _banditCapturesCount;
        private int _banditRescuedCount;
        private int _banditRescuerFoundCount;
        private int _banditPlayerRescuedCount;
        private int _banditEscapedCount;

        public int EventsSubmittedThisSession => _eventsSubmittedThisSession;
        public int SubscribedCount => 5;
        public int WhereKeptCount => _whereKeptCount;
        public int WhereDroppedCount => _whereDroppedCount;
        public int ProminenceRulerCount => _prominenceRulerCount;
        public int ProminenceClanLeaderCount => _prominenceClanLeaderCount;
        public int ProminenceNobleMemberCount => _prominenceNobleMemberCount;
        public int ProminenceMinorCount => _prominenceMinorCount;
        public int ProminenceFailedCount => _prominenceFailedCount;
        public int ReleasesCount => _releasesCount;
        public int ReleaseEscapedCount => _releaseEscapedCount;
        public int ReleaseReleasedCount => _releaseReleasedCount;
        public int ReleaseLinkedCount => _releaseLinkedCount;
        public int ReleaseUnlinkedCount => _releaseUnlinkedCount;
        public int BanditCapturesCount => _banditCapturesCount;
        public int BanditRescuedCount => _banditRescuedCount;
        public int BanditRescuerFoundCount => _banditRescuerFoundCount;
        public int BanditPlayerRescuedCount => _banditPlayerRescuedCount;
        public int BanditEscapedCount => _banditEscapedCount;
        public int EntriesMarkedOutdatedCount => _eventStore?.Stamper?.EntriesMarkedOutdatedCount ?? 0;
        public int EventsDormantByOutdatingCount => _eventStore?.Stamper?.EventsDormantByOutdatingCount ?? 0;

        public RealEventSourceBehavior(VividWorldConfig config)
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));
        }

        public void Initialize(WorldEventStore eventStore, IHeroTraitLookup? traitLookup = null)
        {
            _eventStore = eventStore;
            _traitLookup = traitLookup;
        }

        public override void RegisterEvents()
        {
            CampaignEvents.BeforeHeroKilledEvent.AddNonSerializedListener(this, OnBeforeHeroKilled);
            CampaignEvents.HeroKilledEvent.AddNonSerializedListener(this, OnHeroKilled);
            CampaignEvents.HeroPrisonerTaken.AddNonSerializedListener(this, OnHeroPrisonerTaken);
            CampaignEvents.BeforeHeroesMarried.AddNonSerializedListener(this, OnHeroesMarried);
            CampaignEvents.OnGivenBirthEvent.AddNonSerializedListener(this, OnGivenBirth);
            CampaignEvents.HeroPrisonerReleased.AddNonSerializedListener(this, OnHeroPrisonerReleased);
            ModLog.Info("RealEventSourceBehavior.RegisterEvents: subscribed to 5 campaign events (plus the before-kill hook that records the victim's standing).");
        }

        public override void SyncData(IDataStore dataStore)
        {
            // No custom persistent state needed for real event hook listeners
        }

        // 死者死前的身分。遊戲在發出「某人死了」的通知之前，已經先替他的家族換了族長（國王也跟著換），
        // 等通知到的時候再問「他是不是國王、是不是族長」只會得到否；所以在死前那一刻先記下來，通知到了再取用。
        private readonly Dictionary<string, ProminenceResult> _standingBeforeDeath =
            new Dictionary<string, ProminenceResult>(StringComparer.Ordinal);

        private void OnBeforeHeroKilled(Hero victim, Hero killer, KillCharacterAction.KillCharacterActionDetail detail, bool showNotification)
        {
            try
            {
                if (victim == null || !_config.Events.Sources.HeroKilled) return;
                // 正常情況下每一筆都會在緊接著的死亡通知裡取走；留下來的是被別的模組攔掉的死亡，不讓它無限累積
                if (_standingBeforeDeath.Count > 64) _standingBeforeDeath.Clear();
                _standingBeforeDeath[victim.StringId] = ClassifyPerson(victim);
            }
            catch (Exception ex)
            {
                ModLog.Error("RealEventSourceBehavior.OnBeforeHeroKilled encountered an exception", ex);
            }
        }

        private void OnHeroKilled(Hero victim, Hero killer, KillCharacterAction.KillCharacterActionDetail detail, bool showNotification)
        {
            try
            {
                if (_eventStore == null) return;
                if (!_config.Events.Sources.HeroKilled) return;
                if (victim == null) return;

                string? templateType = RealEventMapping.TemplateForKill((int)detail);
                if (templateType == null)
                {
                    string killerStr = killer != null ? killer.StringId : "none";
                    ModLog.Info($"RealEventSource: HeroKilled detail={detail} -> no template, skipped (victim={victim.StringId}, killer={killerStr})");
                    return;
                }

                bool isSecret = string.Equals(templateType, "hero_murdered", StringComparison.OrdinalIgnoreCase);
                ModLog.Info($"RealEventSource: HeroKilled detail={detail} -> template '{templateType}' ({(isSecret ? "secret" : "public")})");

                if (!RealEventMapping.IsNaturalDeathTemplate(templateType) && killer == null)
                {
                    ModLog.Info($"RealEventSource: HeroKilled template '{templateType}' requires killer but killer is null, skipped (victim={victim.StringId})");
                    return;
                }

                var bindings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["VICTIM"] = victim.StringId
                };
                if (killer != null)
                {
                    bindings["KILLER"] = killer.StringId;
                }

                var probes = new List<SettlementProbe>();
                probes.AddRange(ProbesForHero("victim", victim));
                if (killer != null)
                {
                    probes.AddRange(ProbesForHero("killer", killer));
                }

                var fallbackResult = SettlementFallback.Resolve(probes);
                if (!string.IsNullOrEmpty(fallbackResult.SettlementId))
                {
                    bindings["SETTLEMENT"] = fallbackResult.SettlementId!;
                }

                // 死亡的份量看死者本人的身分（國王、族長、一般家族成員、小勢力或沒家族）
                ProminenceResult? victimProminence = null;
                Exception? victimProminenceException = null;
                try
                {
                    if (_standingBeforeDeath.TryGetValue(victim.StringId, out var standingBeforeDeath))
                    {
                        _standingBeforeDeath.Remove(victim.StringId);
                        victimProminence = standingBeforeDeath;
                        ModLog.Info($"  standing of {victim.StringId}: recorded before the death (leaders are replaced before the kill notification is sent)");
                    }
                    else
                    {
                        victimProminence = ClassifyPerson(victim);
                        ModLog.Info($"  standing of {victim.StringId}: not recorded before the death, read now - a ruler or clan leader has already been replaced by this point and reads as an ordinary member");
                    }
                }
                catch (Exception ex)
                {
                    victimProminenceException = ex;
                }

                TrySubmit(
                    templateType,
                    bindings,
                    $"HeroKilled detail={detail}",
                    fallbackResult,
                    victimProminence,
                    victimProminenceException,
                    victim.StringId);
            }
            catch (Exception ex)
            {
                ModLog.Error("RealEventSourceBehavior.OnHeroKilled encountered an exception", ex);
            }
        }

        private void OnHeroPrisonerTaken(PartyBase capturer, Hero prisoner)
        {
            try
            {
                if (_eventStore == null) return;
                if (!_config.Events.Sources.HeroPrisonerTaken) return;
                if (prisoner == null) return;

                Hero? captorHero = capturer?.LeaderHero ?? capturer?.Owner;
                if (captorHero == null)
                {
                    OnHeroCapturedByBandits(capturer, prisoner);
                    return;
                }

                var bindings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["CAPTOR"] = captorHero.StringId,
                    ["PRISONER"] = prisoner.StringId
                };

                var probes = new List<SettlementProbe>();
                probes.AddRange(ProbesForHero("prisoner", prisoner));
                if (capturer != null && capturer.IsSettlement)
                {
                    probes.Add(new SettlementProbe("capturer.Settlement", () => capturer.Settlement?.StringId));
                }
                if (captorHero != null)
                {
                    probes.AddRange(ProbesForHero("captor", captorHero));
                }

                var fallbackResult = SettlementFallback.Resolve(probes);
                if (!string.IsNullOrEmpty(fallbackResult.SettlementId))
                {
                    bindings["SETTLEMENT"] = fallbackResult.SettlementId!;
                }

                ProminenceResult? prominenceResult = null;
                Exception? prominenceException = null;
                try
                {
                    prominenceResult = ClassifyPerson(prisoner);
                }
                catch (Exception ex)
                {
                    prominenceException = ex;
                }

                TrySubmit(
                    "hero_taken_prisoner",
                    bindings,
                    "HeroPrisonerTaken",
                    fallbackResult,
                    prominenceResult,
                    prominenceException,
                    prisoner.StringId);
            }
            catch (Exception ex)
            {
                ModLog.Error("RealEventSourceBehavior.OnHeroPrisonerTaken encountered an exception", ex);
            }
        }

        private static string ResolveBanditsVar(PartyBase? capturer)
        {
            if (capturer?.MapFaction is Clan banditClan && banditClan.IsBanditFaction)
            {
                return $"faction:{banditClan.StringId}";
            }
            return "key:VividWorld_UnknownBandits";
        }

        private void OnHeroCapturedByBandits(PartyBase? capturer, Hero prisoner)
        {
            string capturerId = capturer?.Id ?? "none";
            string factionStr = capturer?.MapFaction?.StringId ?? "none";
            ModLog.Info($"RealEventSource: HeroPrisonerTaken - leaderless capturer {capturerId} (faction {factionStr}) -> template 'hero_captured_by_bandits'");

            string banditsVar = ResolveBanditsVar(capturer);

            var bindings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["PRISONER"] = prisoner.StringId,
                ["BANDITS"] = banditsVar
            };

            var probes = new List<SettlementProbe>();
            probes.AddRange(ProbesForHero("prisoner", prisoner));
            if (capturer != null && capturer.IsSettlement)
            {
                probes.Add(new SettlementProbe("capturer.Settlement", () => capturer.Settlement?.StringId));
            }

            var fallbackResult = SettlementFallback.Resolve(probes);
            if (!string.IsNullOrEmpty(fallbackResult.SettlementId))
            {
                bindings["SETTLEMENT"] = fallbackResult.SettlementId!;
            }

            ProminenceResult? prominenceResult = null;
            Exception? prominenceException = null;
            try
            {
                prominenceResult = ClassifyPerson(prisoner);
            }
            catch (Exception ex)
            {
                prominenceException = ex;
            }

            var candidates = new List<KeyValuePair<string, TraitProfile?>>();
            if (prisoner.Clan != null)
            {
                var clanHeroes = new HashSet<Hero>();
                if (prisoner.Clan.Heroes != null)
                {
                    foreach (var h in prisoner.Clan.Heroes)
                    {
                        if (h != null) clanHeroes.Add(h);
                    }
                }
                if (prisoner.Clan.Companions != null)
                {
                    foreach (var c in prisoner.Clan.Companions)
                    {
                        if (c != null) clanHeroes.Add(c);
                    }
                }

                foreach (var h in clanHeroes)
                {
                    string hid = h.StringId;
                    TraitProfile? profile = _traitLookup?.Of(hid);
                    if (profile == null && _traitLookup == null)
                    {
                        profile = new TraitProfile
                        {
                            HeroId = hid,
                            IsAlive = h.IsAlive,
                            IsPrisoner = h.IsPrisoner,
                            IsLord = h.IsLord,
                            IsWanderer = h.IsWanderer
                        };
                    }
                    candidates.Add(new KeyValuePair<string, TraitProfile?>(hid, profile));
                }
            }

            string? playerHeroId = Hero.MainHero?.StringId;
            var familyResult = BanditCaptureFamilySelector.Select(
                prisoner.StringId,
                candidates,
                playerHeroId,
                _config.Propagation.MaxInitialWitnesses);

            string selectedStr = string.Join(", ", familyResult.SelectedHeroIds);
            string excludedStr = string.Join(", ", familyResult.Exclusions.Select(e => $"{e.HeroId}: {e.Reason}"));
            ModLog.Info($"  family: {familyResult.SelectedHeroIds.Count} selected ({selectedStr}); excluded {familyResult.Exclusions.Count} ({excludedStr})");

            TrySubmit(
                "hero_captured_by_bandits",
                bindings,
                "HeroPrisonerTaken",
                fallbackResult,
                prominenceResult,
                prominenceException,
                prisoner.StringId,
                hearsayKnowerHeroIds: familyResult.SelectedHeroIds);
        }

        private void OnHeroPrisonerReleased(Hero prisoner, PartyBase party, IFaction faction, TaleWorlds.CampaignSystem.Actions.EndCaptivityDetail detail, bool showNotification)
        {
            try
            {
                if (_eventStore == null) return;
                if (!_config.Events.Sources.HeroPrisonerReleased) return;
                if (prisoner == null) return;

                double day = CampaignTime.Now.ToDays;
                var captureEntry = CaptureLookup.FindLatestCapture(_eventStore.Index, prisoner.StringId, day);
                string? linkedEventId = captureEntry?.EventId;

                Hero? captorHero = party?.LeaderHero ?? party?.Owner;
                string? captorId = captorHero?.StringId;
                bool isMainParty = party != null && party == MobileParty.MainParty?.Party;
                bool hasLeaderOrOwner = captorHero != null;
                var coreDetail = (VividWorld.Core.Events.EndCaptivityDetail)(int)detail;

                var playerRescueResult = PlayerRescueEvaluator.Evaluate(
                    captureEntry?.Type,
                    isMainParty,
                    hasLeaderOrOwner,
                    coreDetail);
                bool isPlayerRescue = playerRescueResult.IsPlayerRescue;

                string? templateType;
                if (isPlayerRescue)
                {
                    templateType = "hero_rescued_from_bandits";
                }
                else if (captorHero == null)
                {
                    if (detail == TaleWorlds.CampaignSystem.Actions.EndCaptivityDetail.ReleasedAfterBattle)
                    {
                        templateType = "hero_rescued_from_bandits";
                    }
                    else if (detail == TaleWorlds.CampaignSystem.Actions.EndCaptivityDetail.ReleasedAfterEscape)
                    {
                        templateType = "hero_escaped_bandits";
                    }
                    else
                    {
                        templateType = RealEventMapping.TemplateForRelease((int)detail);
                    }
                }
                else
                {
                    templateType = RealEventMapping.TemplateForRelease((int)detail);
                }

                if (templateType == null)
                {
                    ModLog.Info($"RealEventSource: HeroPrisonerReleased detail={detail} -> no template, skipped (prisoner={prisoner.StringId})");
                    return;
                }

                string captorPartyId = party?.Id ?? "none";
                if (captorHero == null)
                {
                    ModLog.Info(OutdatingLogFormatter.FormatReleaseHeader(detail.ToString(), templateType, prisoner.StringId, captorId, captorPartyId));
                }
                else
                {
                    ModLog.Info(OutdatingLogFormatter.FormatReleaseHeader(detail.ToString(), templateType, prisoner.StringId, captorId));
                }

                if (string.Equals(captureEntry?.Type, "hero_captured_by_bandits", StringComparison.Ordinal)
                    || isMainParty
                    || (captorHero == null && detail == TaleWorlds.CampaignSystem.Actions.EndCaptivityDetail.ReleasedByChoice))
                {
                    ModLog.Info("  " + PlayerRescueEvaluator.FormatLog(playerRescueResult, captorPartyId, detail.ToString()));
                }

                string? releaseReason = null;
                if (string.Equals(templateType, "hero_released", StringComparison.Ordinal))
                {
                    // 放人事件傳出的 party 是俘虜放人當下所在的隊伍：
                    // 戰後放人時 party 是城鎮城堡＝城換了主人，否則＝關人的隊伍打輸；
                    // 主動放人時 party 是玩家的隊伍（或隊長、主人是玩家）＝玩家放的，否則＝押人的隊伍沒了。
                    bool partyIsSettlement = party != null && party.IsSettlement;
                    bool actorIsPlayer = isMainParty
                        || (party?.Owner != null && party.Owner == Hero.MainHero)
                        || (party?.LeaderHero != null && party.LeaderHero == Hero.MainHero);
                    releaseReason = ReleaseReasons.Classify(coreDetail, partyIsSettlement, actorIsPlayer);
                    ModLog.Info($"  release reason: {releaseReason ?? "none"} (detail={detail}, partyIsSettlement={partyIsSettlement}, actorIsPlayer={actorIsPlayer}, captorKnown={captorHero != null})");
                }

                Hero? rescuerHero = null;
                if (string.Equals(templateType, "hero_rescued_from_bandits", StringComparison.Ordinal))
                {
                    if (isPlayerRescue)
                    {
                        rescuerHero = Hero.MainHero;
                        ModLog.Info($"  rescuer: {rescuerHero?.StringId ?? "player"} (player rescue)");
                    }
                    else
                    {
                        if (party == null)
                        {
                            ModLog.Info("  rescuer: none (party is null)");
                        }
                        else if (party.MapEvent == null)
                        {
                            ModLog.Info("  rescuer: none (no MapEvent)");
                        }
                        else if (!party.MapEvent.HasWinner)
                        {
                            ModLog.Info("  rescuer: none (no winner)");
                        }
                        else
                        {
                            var winnerSide = party.MapEvent.WinningSide;
                            var leaderParty = party.MapEvent.GetLeaderParty(winnerSide);
                            rescuerHero = leaderParty?.LeaderHero;
                            if (rescuerHero == null)
                            {
                                ModLog.Info("  rescuer: none (winner leader party has no hero)");
                            }
                            else
                            {
                                ModLog.Info($"  rescuer: {rescuerHero.StringId} via MapEvent winner side {winnerSide}");
                            }
                        }
                    }
                }

                // 逃脫時押著他的是聚落（人關在城鎮或城堡的牢裡）⇒ 不記看守的人：
                // 城主多半人在外面，不是這件事的當事人，他要知道得跟別人一樣聽說（放人事件的 party：帳本 D-102）。
                EscapeShapeChoice? escapeChoice = null;
                string? heldSettlementId = null;
                if (string.Equals(templateType, "hero_escaped_captivity", StringComparison.Ordinal))
                {
                    bool heldBySettlement = party != null && party.IsSettlement;
                    if (heldBySettlement)
                    {
                        heldSettlementId = party!.Settlement?.StringId;
                    }
                    escapeChoice = TemplateVariants.ChooseEscapeShape(heldBySettlement, heldSettlementId, captorHero != null);
                }
                bool escapedFromDungeon = escapeChoice != null && escapeChoice.Shape == EscapeShape.FromDungeon;
                bool captorIsParticipant = escapeChoice == null || escapeChoice.Shape == EscapeShape.WithCaptor;

                Dictionary<string, string> bindings;
                if (string.Equals(templateType, "hero_rescued_from_bandits", StringComparison.Ordinal))
                {
                    string banditsVar;
                    if (isPlayerRescue)
                    {
                        WorldEvent? captureEvent = !string.IsNullOrEmpty(captureEntry?.EventId)
                            ? _eventStore.Load(captureEntry!.EventId)
                            : null;
                        banditsVar = PlayerRescueEvaluator.ResolveBanditsVar(captureEvent);
                    }
                    else
                    {
                        banditsVar = ResolveBanditsVar(party);
                    }

                    bindings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["PRISONER"] = prisoner.StringId,
                        ["BANDITS"] = banditsVar
                    };
                    if (rescuerHero != null)
                    {
                        bindings["RESCUER"] = rescuerHero.StringId;
                    }
                }
                else if (string.Equals(templateType, "hero_escaped_bandits", StringComparison.Ordinal))
                {
                    bindings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["PRISONER"] = prisoner.StringId,
                        ["BANDITS"] = ResolveBanditsVar(party)
                    };
                }
                else
                {
                    bindings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["PRISONER"] = prisoner.StringId
                    };
                    if (captorHero != null && captorIsParticipant)
                    {
                        bindings["CAPTOR"] = captorHero.StringId;
                    }
                }

                var probes = new List<SettlementProbe>();
                if (escapedFromDungeon)
                {
                    // 人就關在這座城的牢裡：地點一定是這座城，不拿俘虜身上讀到的最近聚落來猜
                    probes.Add(new SettlementProbe("party.Settlement (held in its dungeon)", () => heldSettlementId));
                }
                probes.AddRange(ProbesForHero("prisoner", prisoner));
                if (party != null && party.IsSettlement && !escapedFromDungeon)
                {
                    probes.Add(new SettlementProbe("party.Settlement", () => party.Settlement?.StringId));
                }
                if (captorHero != null && captorIsParticipant)
                {
                    probes.AddRange(ProbesForHero("captor", captorHero));
                }
                if (rescuerHero != null)
                {
                    probes.AddRange(ProbesForHero("rescuer", rescuerHero));
                }

                var fallbackResult = SettlementFallback.Resolve(probes);
                if (!string.IsNullOrEmpty(fallbackResult.SettlementId))
                {
                    bindings["SETTLEMENT"] = fallbackResult.SettlementId!;
                }

                ProminenceResult? prominenceResult = null;
                Exception? prominenceException = null;
                try
                {
                    prominenceResult = ClassifyPerson(prisoner);
                }
                catch (Exception ex)
                {
                    prominenceException = ex;
                }

                var catalog = EventCatalogStore.Catalog;
                var baseTemplate = catalog.ByType(templateType);

                if (captureEntry != null)
                {
                    ModLog.Info(OutdatingLogFormatter.FormatLinkedCapture(captureEntry.EventId, captureEntry.Type, captureEntry.Day, day));
                    _releaseLinkedCount++;
                }
                else
                {
                    int checkedCount = _eventStore.Index?.Entries?.Count ?? 0;
                    ModLog.Info(OutdatingLogFormatter.FormatLinkedNone(prisoner.StringId, checkedCount));
                    _releaseUnlinkedCount++;
                }

                EventTemplate? adaptedTemplate = null;
                if (baseTemplate != null)
                {
                    if (string.Equals(templateType, "hero_rescued_from_bandits", StringComparison.Ordinal) && rescuerHero == null)
                    {
                        adaptedTemplate = TemplateVariants.RescueWithoutRescuer(baseTemplate);
                    }
                    else if (string.Equals(templateType, "hero_released", StringComparison.Ordinal))
                    {
                        adaptedTemplate = TemplateVariants.Released(baseTemplate, releaseReason, captorKnown: captorHero != null);
                        if (!adaptedTemplate.Roles.ContainsKey("captor"))
                        {
                            // 這一種講法不提抓人的人（不知道是誰，或關人的城換了主人、分不出新舊主人）
                            bindings.Remove("CAPTOR");
                        }
                        ModLog.Info($"  release template: {string.Join("_", adaptedTemplate.Facts.Select(f => SentenceCombinationEnumerator.ExtractSegment(f.TextId ?? string.Empty)))}");
                    }
                    else if (escapeChoice != null)
                    {
                        adaptedTemplate = TemplateVariants.EscapeFor(baseTemplate, escapeChoice.Shape);
                        ModLog.Info("  " + escapeChoice.Log);
                    }
                }

                TrySubmit(
                    templateType,
                    bindings,
                    $"HeroPrisonerReleased detail={detail}",
                    fallbackResult,
                    prominenceResult,
                    prominenceException,
                    prisoner.StringId,
                    linkedEventId,
                    adaptedTemplate);

                _releasesCount++;
                if (detail == TaleWorlds.CampaignSystem.Actions.EndCaptivityDetail.ReleasedAfterEscape)
                {
                    _releaseEscapedCount++;
                }
                else
                {
                    _releaseReleasedCount++;
                }
            }
            catch (Exception ex)
            {
                ModLog.Error("RealEventSourceBehavior.OnHeroPrisonerReleased encountered an exception", ex);
            }
        }

        private void OnHeroesMarried(Hero hero1, Hero hero2, bool showNotification)
        {
            try
            {
                if (_eventStore == null) return;
                if (!_config.Events.Sources.HeroesMarried) return;
                if (hero1 == null || hero2 == null)
                {
                    string h1 = hero1?.StringId ?? "none";
                    string h2 = hero2?.StringId ?? "none";
                    ModLog.Info($"RealEventSource: BeforeHeroesMarried - one or both heroes are null, skipped (hero1={h1}, hero2={h2})");
                    return;
                }

                var bindings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["SPOUSE_A"] = hero1.StringId,
                    ["SPOUSE_B"] = hero2.StringId
                };

                var probes = new List<SettlementProbe>();
                probes.AddRange(ProbesForHero("spouse_a", hero1));
                probes.AddRange(ProbesForHero("spouse_b", hero2));

                var fallbackResult = SettlementFallback.Resolve(probes);
                if (!string.IsNullOrEmpty(fallbackResult.SettlementId))
                {
                    bindings["SETTLEMENT"] = fallbackResult.SettlementId!;
                }

                // 成親的份量看兩家的門第，取較高的那一家
                List<FamilyParty>? marriageParties = null;
                Exception? marriageException = null;
                try
                {
                    marriageParties = new List<FamilyParty>
                    {
                        BuildFamilyParty("spouse_a", hero1),
                        BuildFamilyParty("spouse_b", hero2)
                    };
                }
                catch (Exception ex)
                {
                    marriageException = ex;
                }

                TrySubmit(
                    "heroes_married",
                    bindings,
                    "BeforeHeroesMarried",
                    fallbackResult,
                    prominenceException: marriageException,
                    prisonerIdForProminence: hero1.StringId,
                    familyParties: marriageParties);
            }
            catch (Exception ex)
            {
                ModLog.Error("RealEventSourceBehavior.OnHeroesMarried encountered an exception", ex);
            }
        }

        private void OnGivenBirth(Hero mother, List<Hero> aliveChildren, int stillbornCount)
        {
            try
            {
                if (_eventStore == null) return;
                if (!_config.Events.Sources.ChildBorn) return;
                if (mother == null) return;

                if (aliveChildren == null || aliveChildren.Count == 0)
                {
                    ModLog.Info($"RealEventSource: OnGivenBirth - no alive children, skipped (mother={mother.StringId}, stillborn={stillbornCount})");
                    return;
                }

                Hero child = aliveChildren[0];
                if (child == null) return;

                // 多胞胎只講第一個。這是取捨不是遺漏，但要印出來——
                // 不印的話「為什麼雙胞胎只有一個被談論」永遠查不出來。
                if (aliveChildren.Count > 1)
                {
                    ModLog.Info($"RealEventSource: OnGivenBirth - {aliveChildren.Count} alive children, only the first ({child.StringId}) is used as CHILD");
                }
                if (stillbornCount > 0)
                {
                    ModLog.Info($"RealEventSource: OnGivenBirth - {stillbornCount} stillborn not reported as events");
                }

                var bindings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["MOTHER"] = mother.StringId,
                    ["CHILD"] = child.StringId
                };

                var probes = new List<SettlementProbe>();
                probes.AddRange(ProbesForHero("mother", mother));
                probes.AddRange(ProbesForHero("child", child));

                var fallbackResult = SettlementFallback.Resolve(probes);
                if (!string.IsNullOrEmpty(fallbackResult.SettlementId))
                {
                    bindings["SETTLEMENT"] = fallbackResult.SettlementId!;
                }

                // 生子的份量看父母的門第，取較高的那一家；父親讀不到時只看母親這一邊
                List<FamilyParty>? birthParties = null;
                Exception? birthException = null;
                try
                {
                    birthParties = new List<FamilyParty> { BuildFamilyParty("mother", mother) };
                    Hero? father = child.Father ?? mother.Spouse;
                    if (father != null)
                    {
                        birthParties.Add(BuildFamilyParty("father", father));
                    }
                    else
                    {
                        ModLog.Info($"  family: father of {child.StringId} not readable (child.Father and mother.Spouse are null), only the mother side counts");
                    }
                }
                catch (Exception ex)
                {
                    birthException = ex;
                }

                TrySubmit(
                    "child_born",
                    bindings,
                    "OnGivenBirth",
                    fallbackResult,
                    prominenceException: birthException,
                    prisonerIdForProminence: mother.StringId,
                    familyParties: birthParties);
            }
            catch (Exception ex)
            {
                ModLog.Error("RealEventSourceBehavior.OnGivenBirth encountered an exception", ex);
            }
        }

        /// <summary>判一個人的身分（國王、族長、一般家族成員、小勢力或沒家族），並帶上判定用到的原生值供日誌。</summary>
        private ProminenceResult ClassifyPerson(Hero hero)
        {
            var clan = hero.Clan;
            var facts = new ProminenceFacts
            {
                HeroId = hero.StringId,
                IsKingdomLeader = hero.IsKingdomLeader,
                KingdomId = hero.MapFaction?.StringId,
                IsClanLeader = hero.IsClanLeader,
                ClanId = clan?.StringId,
                ClanIsMinorFaction = clan?.IsMinorFaction ?? false,
                IsLord = hero.IsLord,
                ClanTier = clan?.Tier,
                ClanIsRuling = IsRulingClan(clan)
            };
            return PrisonerProminence.Classify(facts, _config.Events.WeightBonusByProminence);
        }

        /// <summary>這個家族是不是它所屬王國的王族。小勢力、沒加入王國的家族 <c>Kingdom</c> 可能是 null，一律當成不是。</summary>
        private static bool IsRulingClan(Clan? clan)
        {
            var kingdom = clan?.Kingdom;
            return clan != null && kingdom != null && kingdom.RulingClan == clan;
        }

        private ClanStandingResult ClassifyClanOf(Hero hero)
        {
            var clan = hero.Clan;
            var facts = new ClanFacts
            {
                ClanId = clan?.StringId,
                Tier = clan?.Tier,
                IsMinorFaction = clan?.IsMinorFaction ?? false,
                IsRuling = IsRulingClan(clan),
                KingdomId = clan?.Kingdom?.StringId
            };
            return DramaWeightCalculator.ClassifyClan(facts, _config.Events);
        }

        private FamilyParty BuildFamilyParty(string role, Hero hero)
        {
            return new FamilyParty(role, hero.StringId, ClassifyPerson(hero), ClassifyClanOf(hero));
        }

        private static IEnumerable<SettlementProbe> ProbesForHero(string role, Hero? hero)
        {
            if (hero == null) yield break;
            yield return new SettlementProbe($"{role}.CurrentSettlement", () => hero.CurrentSettlement?.StringId);
            yield return new SettlementProbe($"{role}.GetClosestSettlement", () => Helpers.HeroHelper.GetClosestSettlement(hero)?.StringId);
        }

        private void TrySubmit(
            string templateType,
            Dictionary<string, string> bindings,
            string sourceTag,
            SettlementFallbackResult? fallbackResult = null,
            ProminenceResult? prominenceResult = null,
            Exception? prominenceException = null,
            string? prisonerIdForProminence = null,
            string? linkedEventId = null,
            EventTemplate? templateOverride = null,
            IReadOnlyList<string>? hearsayKnowerHeroIds = null,
            IReadOnlyList<FamilyParty>? familyParties = null)
        {
            var catalog = EventCatalogStore.Catalog;
            var template = templateOverride ?? catalog.ByType(templateType);
            if (template == null)
            {
                ModLog.Warn($"RealEventSource: Template '{templateType}' not found in catalog ({sourceTag}).");
                return;
            }

            double day = CampaignTime.Now.ToDays;
            var submission = TemplateBinder.Bind(template, bindings, day, linkedEventId, out var bindIssues);
            if (submission == null)
            {
                string issueDetails = string.Join("; ", bindIssues.Select(iss => $"{iss.Field}: {iss.Detail}"));
                ModLog.Warn($"RealEventSource: Failed to bind template '{templateType}' ({sourceTag}): {issueDetails}");
                return;
            }

            // 份量＝模板寫的基礎分＋當事人的身分（或門第）加成，夾在 1..10；
            // 傳多遠、記多久等機制讀的是它換成的段（1..5），不是這個數字本身
            var weightLines = new List<string>();
            int? baseScore = template.DramaWeightTen;
            bool weightApplies = prominenceResult != null || prominenceException != null || familyParties != null;
            if (weightApplies && baseScore.HasValue)
            {
                WeightComputation computation;
                if (familyParties != null)
                {
                    var family = DramaWeightCalculator.ComputeForFamily(baseScore.Value, familyParties);
                    computation = family.Computation;
                    weightLines.AddRange(family.DescribeLines());
                }
                else if (prominenceResult != null)
                {
                    computation = DramaWeightCalculator.Compute(baseScore.Value, prominenceResult.Bonus);
                    weightLines.Add(prominenceResult.Describe());
                }
                else
                {
                    string pid = !string.IsNullOrEmpty(prisonerIdForProminence) ? prisonerIdForProminence! : "unknown";
                    computation = DramaWeightCalculator.Compute(baseScore.Value, 0);
                    weightLines.Add($"hero={pid} standing failed ({prominenceException!.GetType().Name}), bonus +0 (template base score kept)");
                }
                submission.DramaWeight = computation.Weight;
                submission.DramaScale = DramaScales.Ten;
                weightLines.Add(computation.Describe());
            }
            else if (weightApplies)
            {
                weightLines.Add("template has no weight, standing bonus not applied (the configured default weight is used)");
            }

            if (hearsayKnowerHeroIds != null && hearsayKnowerHeroIds.Count > 0)
            {
                submission.HearsayKnowerHeroIds.AddRange(hearsayKnowerHeroIds);
            }

            var result = _eventStore!.Submit(submission, out string? eventId, out string? rejectionReason);
            if (result != IngestResult.Accepted || string.IsNullOrEmpty(eventId))
            {
                ModLog.Warn($"RealEventSource: Failed to submit template '{templateType}' ({sourceTag}): {result} - {rejectionReason}");
                return;
            }

            _eventsSubmittedThisSession++;

            if (string.Equals(templateType, "hero_captured_by_bandits", StringComparison.Ordinal))
            {
                _banditCapturesCount++;
            }
            else if (string.Equals(templateType, "hero_rescued_from_bandits", StringComparison.Ordinal))
            {
                _banditRescuedCount++;
                if (bindings.ContainsKey("RESCUER"))
                {
                    _banditRescuerFoundCount++;
                }
                string playerHeroId = Hero.MainHero?.StringId ?? "player";
                if (bindings.TryGetValue("RESCUER", out var rescuerId)
                    && !string.IsNullOrEmpty(rescuerId)
                    && string.Equals(rescuerId, playerHeroId, StringComparison.Ordinal))
                {
                    _banditPlayerRescuedCount++;
                }
            }
            else if (string.Equals(templateType, "hero_escaped_bandits", StringComparison.Ordinal))
            {
                _banditEscapedCount++;
            }

            // Only track prisoner prominence for hero_taken_prisoner
            if (string.Equals(templateType, "hero_taken_prisoner", StringComparison.Ordinal))
            {
                if (prominenceResult != null)
                {
                    switch (prominenceResult.Tier)
                    {
                        case ProminenceTier.Ruler:
                            _prominenceRulerCount++;
                            break;
                        case ProminenceTier.ClanLeader:
                            _prominenceClanLeaderCount++;
                            break;
                        case ProminenceTier.NobleMember:
                            _prominenceNobleMemberCount++;
                            break;
                        case ProminenceTier.Minor:
                            _prominenceMinorCount++;
                            break;
                    }
                }
                else if (prominenceException != null)
                {
                    _prominenceFailedCount++;
                }
            }

            var boundParts = new List<string>();
            foreach (var kvp in bindings)
            {
                if (string.Equals(kvp.Key, "SETTLEMENT", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
                boundParts.Add($"{kvp.Key}={kvp.Value}");
            }
            if (fallbackResult != null)
            {
                boundParts.Add(fallbackResult.Describe());
            }
            string boundVarsStr = string.Join(", ", boundParts);
            ModLog.Info($"RealEventSource: bound {templateType} as {eventId} ({boundVarsStr})");

            // 模板沒寫份量時由 WorldEventStore 依設定解析，這裡不能自己猜一個數字
            foreach (var line in weightLines)
            {
                ModLog.Info("  " + line);
            }

            var submittedFactIds = new HashSet<string>(submission.Facts.Select(f => f.Id), StringComparer.Ordinal);
            foreach (var tf in template.Facts)
            {
                if (submittedFactIds.Contains(tf.Id))
                {
                    if (tf.Optional)
                    {
                        if (string.Equals(tf.Id, "where", StringComparison.OrdinalIgnoreCase))
                        {
                            _whereKeptCount++;
                        }
                        ModLog.Info($"  fact '{tf.Id}' kept");
                    }
                }
                else
                {
                    if (string.Equals(tf.Id, "where", StringComparison.OrdinalIgnoreCase))
                    {
                        _whereDroppedCount++;
                    }
                    var why = bindIssues.FirstOrDefault(iss =>
                        !string.IsNullOrEmpty(iss.Detail) && iss.Detail.Contains($"'{tf.Id}'"));
                    string reason = why != null ? $"{why.Field}: {why.Detail}" : "reason not reported by the binder";
                    ModLog.Info($"  fact '{tf.Id}' dropped - {reason}");
                }
            }

            // §4.1: hop 0 摘要
            try
            {
                LogHop0Summary(template, submission, eventId!);
            }
            catch (Exception ex)
            {
                ModLog.Error($"RealEventSource: Failed to format hop0 summary for {eventId}", ex);
            }
        }

        private void LogHop0Summary(EventTemplate template, EventSubmission submission, string eventId)
        {
            Hop0SummaryLogger.LogHop0Summary(template, submission, eventId, _eventStore, _traitLookup);
        }
    }
}
