#nullable enable
using System;
using System.Collections.Generic;
using VividWorld.Core.Config;

namespace VividWorld.Core.Dialogue
{
    public static class ListenPreviewAggregator
    {
        public static ListenPreviewResult Generate(
            RumorOfferSelector offerSelector,
            CommonerCompatState? compat,
            int playerClanTier,
            int volunteersAlreadyToday,
            double day,
            DialogueConfig dialogueConfig,
            IEnumerable<ListenPreviewPerson> persons,
            long elapsedMs = 0,
            bool includeHeroDetails = false)
        {
            if (offerSelector == null) throw new ArgumentNullException(nameof(offerSelector));
            if (dialogueConfig == null) throw new ArgumentNullException(nameof(dialogueConfig));

            int chatGate = offerSelector.ChatRelationGate;
            int fullGate = dialogueConfig.NpcVolunteerRelationGate;
            var modeResult = compat?.RumorModeResult ?? RumorModeResolver.Resolve(dialogueConfig.VolunteerMode, compat?.DetectedModules);

            var result = new ListenPreviewResult
            {
                VolunteersToday = volunteersAlreadyToday,
                MaxVolunteersPerDay = dialogueConfig.MaxVolunteersPerDay,
                VolunteerRelationGate = fullGate,
                ChatRelationGate = chatGate,
                ElapsedMilliseconds = elapsedMs,
                Day = (int)Math.Floor(day)
            };

            bool askBlocked = CommonerCompat.BlocksAsk(compat, playerClanTier);
            result.AskBlockedByClanTier = askBlocked;
            result.PlayerClanTier = playerClanTier;
            result.AskMinClanTier = compat?.AskMinClanTier ?? 0;
            string tierStr = playerClanTier < 0 ? "none" : playerClanTier.ToString();
            result.AskClanTierStatus = askBlocked
                ? $"tier {tierStr} < {result.AskMinClanTier} (BLOCKED)"
                : $"tier {tierStr} >= {result.AskMinClanTier} (allowed)";

            result.ModeLine = RumorModeResolver.FormatModeLine(modeResult, chatGate, fullGate, dialogueConfig.GistExtraHops, result.AskMinClanTier);

            bool volCommonerBlocked = CommonerCompat.BlocksVolunteer(compat, playerClanTier);

            if (persons == null) return result;

            foreach (var p in persons)
            {
                if (p == null) continue;

                result.TotalNetworkCount++;
                if (p.IsLord) result.LordCount++;
                else result.WandererCount++;

                result.UnstampedEntriesCount += p.UnstampedCount;

                int relation = p.Profile?.RelationWithPlayer ?? 0;
                string relationBin = ListenTallyClassifier.GetRelationBin(relation);
                AddOutcome(result.RelationHist, result.RelationHistNames, relationBin, p.HeroName);

                bool isCloseKin = dialogueConfig.NpcVolunteerAlwaysForCloseKin &&
                    (p.Profile?.IsPlayerSpouse == true || p.Profile?.IsPlayerCompanion == true || p.Profile?.IsPlayerClanMember == true);
                if (relation >= dialogueConfig.NpcVolunteerRelationGate || isCloseKin)
                {
                    result.HeroesMeetingVolunteerRelationGate++;
                }
                if (relation >= chatGate || isCloseKin)
                {
                    result.HeroesMeetingChatRelationGate++;
                }

                var profile = p.Profile ?? new HeroSocialProfile();
                var candidates = p.Candidates ?? Array.Empty<RumorCandidate>();

                // 1. Volunteer path
                string volKey;
                if (volCommonerBlocked)
                {
                    volKey = ListenTallyKeys.VolunteerBlockedCommonerTier;
                }
                else
                {
                    var volDecision = offerSelector.DecideOnVolunteer(profile, candidates, day, volunteersAlreadyToday);
                    volKey = ListenTallyClassifier.ClassifyVolunteer(
                        isEligible: true,
                        volunteerConditionRan: true,
                        rivalCount: 0,
                        commonerTierBlocked: false,
                        willLordAttack: false,
                        volDecision,
                        p.KnownCount,
                        p.ForgottenCount,
                        p.OutdatedCount,
                        delivered: true);

                    if (volKey == ListenTallyKeys.VolunteerTold)
                    {
                        if (volDecision.Tier == VolunteerTier.Gist)
                        {
                            result.VolunteerToldGistCount++;
                        }
                        else
                        {
                            result.VolunteerToldFullCount++;
                        }
                    }
                }
                AddOutcome(result.VolunteerCounts, result.VolunteerNames, volKey, p.HeroName);

                // 2. Topic-only path：跟兩個 Decide 同一套——候選是空的才細分「沒東西講」，細分只准呼叫 ClassifyNoTopic
                string topicKey;
                if (candidates.Count == 0)
                {
                    topicKey = TopicKeyOf(ListenTallyClassifier.ClassifyNoTopic(p.KnownCount, p.ForgottenCount, p.OutdatedCount));
                }
                else
                {
                    var classification = offerSelector.ClassifyCandidates(profile, candidates, day);
                    topicKey = classification.Eligible.Count > 0 ? "hasTopic" : "filtered";
                }
                AddOutcome(result.TopicOnlyCounts, result.TopicOnlyNames, topicKey, p.HeroName);

                // 3. Ask path (even if blocked by clan tier, evaluate DecideOnAsk)
                var askDecision = offerSelector.DecideOnAsk(profile, candidates, day);
                string askKey = ListenTallyClassifier.ClassifyAsk(
                    asked: true,
                    told: askDecision.Offer != null,
                    askDecision,
                    p.KnownCount,
                    p.ForgottenCount,
                    p.OutdatedCount);
                AddOutcome(result.AskCounts, result.AskNames, askKey, p.HeroName);

                // 3b. Ask refusal lines (as if the clan-tier gate passed)
                if (askDecision.Offer != null)
                {
                    AddOutcome(result.AskRefusalCounts, result.AskRefusalNames, "told", p.HeroName);
                }
                else
                {
                    var refusalKind = AskRefusalLine.Choose(askDecision, p.KnownCount, p.ForgottenCount, p.OutdatedCount);
                    AddOutcome(result.AskRefusalCounts, result.AskRefusalNames, refusalKind.ToString(), p.HeroName);
                }

                if (includeHeroDetails)
                {
                    result.HeroDetails.Add(new ListenPreviewHeroDetail
                    {
                        HeroId = p.HeroId,
                        HeroName = p.HeroName,
                        IsLord = p.IsLord,
                        Relation = relation,
                        VolunteerOutcome = volKey,
                        TopicOnlyOutcome = topicKey,
                        AskOutcome = askKey
                    });
                }
            }

            return result;
        }

        private static string TopicKeyOf(NoTopicReason reason)
        {
            switch (reason)
            {
                case NoTopicReason.NothingOnFile: return "nothingOnFile";
                case NoTopicReason.AllForgotten: return "allForgotten";
                case NoTopicReason.AllOutdated: return "allOutdated";
                default: return "forgottenOrOutdated";
            }
        }

        private static void AddOutcome(Dictionary<string, int> counts, Dictionary<string, List<string>> names, string key, string heroName)
        {
            counts[key] = (counts.TryGetValue(key, out int c) ? c : 0) + 1;
            if (!names.TryGetValue(key, out var list))
            {
                list = new List<string>();
                names[key] = list;
            }
            if (list.Count < 5 && !string.IsNullOrEmpty(heroName))
            {
                list.Add(heroName);
            }
        }
    }
}
