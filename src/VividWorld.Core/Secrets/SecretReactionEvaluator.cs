#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using VividWorld.Core.Catalog;
using VividWorld.Core.Config;
using VividWorld.Core.Events;
using VividWorld.Core.Rumors;
using VividWorld.Core.Util;

namespace VividWorld.Core.Secrets
{
    public sealed class SecretReactionChange
    {
        public string ObserverHeroId { get; set; } = string.Empty;
        public string TargetHeroId { get; set; } = string.Empty;
        public double Amount { get; set; }
        public bool EscalateClan { get; set; }
        public bool LedgerOnly { get; set; }
        public string RuleDescription { get; set; } = string.Empty;
        public bool IsOwnerReaction { get; set; }
        public bool IsEnduredGrudgeConversion { get; set; }
    }

    public sealed class SecretReactionExclusion
    {
        public string HeroId { get; set; } = string.Empty;
        public string Reason { get; set; } = string.Empty; // "confided", "player", "liege", "old-shape", "already-reacted"
    }

    public sealed class SecretReactionOutcome
    {
        public bool Handled { get; set; }
        public bool IsOwner { get; set; }
        public string? OwnerReaction { get; set; } // "openly" or "hidden"
        public string? GrudgeAgainstHeroId { get; set; }
        public List<string>? SuspectedHeroIds { get; set; }
        public List<SecretReactionChange> Changes { get; set; } = new();
        public List<SecretReactionExclusion> Exclusions { get; set; } = new();
        public string LogDetail { get; set; } = string.Empty;
    }

    public static class SecretReactionEvaluator
    {
        public static SecretReactionOutcome Evaluate(
            WorldEvent evt,
            KnownByEntry hearer,
            EventTemplate? template,
            ISecretRelationshipLookup? relationshipLookup,
            IHeroTraitLookup? traitLookup,
            VividWorldConfig? config,
            IDeterministicRng? rng,
            string? playerHeroId,
            double day,
            Func<string, WorldEvent?>? loadEvent = null,
            int? heardHopOverride = null,
            long campaignSeed = 0)
        {
            var outcome = new SecretReactionOutcome();
            if (evt == null || hearer == null || string.IsNullOrEmpty(hearer.HeroId))
            {
                return outcome;
            }

            if (evt.Origin != EventOrigin.Secret)
            {
                return outcome;
            }

            // Old-shape secrets without SecretOwnerHeroIds are not handled
            if (evt.SecretOwnerHeroIds == null)
            {
                outcome.Exclusions.Add(new SecretReactionExclusion
                {
                    HeroId = hearer.HeroId,
                    Reason = "old-shape"
                });
                outcome.LogDetail = "old-shape secret without owners";
                return outcome;
            }

            // Player hearing leaked secret does not change their own relation
            if (!string.IsNullOrEmpty(playerHeroId) && string.Equals(hearer.HeroId, playerHeroId, StringComparison.Ordinal))
            {
                outcome.Exclusions.Add(new SecretReactionExclusion
                {
                    HeroId = hearer.HeroId,
                    Reason = "player"
                });
                outcome.LogDetail = "listener is player";
                return outcome;
            }

            bool isOwner = evt.SecretOwnerHeroIds.Contains(hearer.HeroId, StringComparer.Ordinal);
            outcome.IsOwner = isOwner;

            if (isOwner)
            {
                EvaluateOwnerReaction(
                    evt, hearer, traitLookup, config, rng, outcome, loadEvent, heardHopOverride, campaignSeed);
                return outcome;
            }

            EvaluateHearerReaction(
                evt, hearer, template, relationshipLookup, traitLookup, config, outcome);
            return outcome;
        }

        private static void EvaluateOwnerReaction(
            WorldEvent evt,
            KnownByEntry hearer,
            IHeroTraitLookup? traitLookup,
            VividWorldConfig? config,
            IDeterministicRng? rng,
            SecretReactionOutcome outcome,
            Func<string, WorldEvent?>? loadEvent,
            int? heardHopOverride,
            long campaignSeed)
        {
            // React once per secret event
            if (!string.IsNullOrEmpty(hearer.OwnerReaction))
            {
                outcome.Exclusions.Add(new SecretReactionExclusion
                {
                    HeroId = hearer.HeroId,
                    Reason = "already-reacted"
                });
                outcome.LogDetail = $"owner {hearer.HeroId} already reacted ({hearer.OwnerReaction})";
                return;
            }

            var cfgSecrets = config?.Secrets ?? new SecretsConfig();
            string? leakerId = evt.State?.LeakerHeroId;

            bool knowsOriginator;
            if (hearer.OriginatorKnownOverride.HasValue)
            {
                knowsOriginator = hearer.OriginatorKnownOverride.Value;
            }
            else if (!string.IsNullOrEmpty(leakerId) && !string.Equals(leakerId, hearer.HeroId, StringComparison.Ordinal))
            {
                int leakerHop = evt.EntryFor(leakerId!)?.Hop ?? 0;
                int? heardHop = heardHopOverride ?? (hearer.Hop > 0 ? hearer.Hop : (int?)null);
                if (heardHop.HasValue)
                {
                    int hands = heardHop.Value - leakerHop;
                    knowsOriginator = (hands == 1 || hands == 2);
                }
                else
                {
                    knowsOriginator = false;
                }
            }
            else
            {
                knowsOriginator = false;
            }

            var suspectReasons = new Dictionary<string, string>(StringComparer.Ordinal);
            if (knowsOriginator && !string.IsNullOrEmpty(leakerId))
            {
                outcome.GrudgeAgainstHeroId = leakerId;
                hearer.GrudgeAgainstHeroId = leakerId;
                hearer.SuspectedHeroIds = null;
            }
            else
            {
                outcome.GrudgeAgainstHeroId = null;
                hearer.GrudgeAgainstHeroId = null;

                var suspects = new HashSet<string>(StringComparer.Ordinal);
                if (evt.KnownBy != null)
                {
                    foreach (var k in evt.KnownBy)
                    {
                        if (k == null || string.IsNullOrEmpty(k.HeroId)) continue;
                        if (string.Equals(k.HeroId, hearer.HeroId, StringComparison.Ordinal)) continue;

                        bool confided = string.Equals(k.SourceHeroId, hearer.HeroId, StringComparison.Ordinal);
                        bool hop0 = k.Hop == 0;

                        if (confided || hop0)
                        {
                            suspects.Add(k.HeroId);
                            string r = (confided && hop0)
                                ? "personally confided, hop 0 at creation"
                                : (confided ? "personally confided" : "hop 0 at creation");
                            suspectReasons[k.HeroId] = r;
                        }
                    }
                }

                var suspectList = suspects.OrderBy(id => id, StringComparer.Ordinal).ToList();
                outcome.SuspectedHeroIds = suspectList;
                hearer.SuspectedHeroIds = suspectList;
            }

            // Determine openly vs hidden
            bool isOpenly;
            double wOpenly;
            double wHidden;

            // Poisoner in conduct_poisoned always acts hidden
            if (string.Equals(evt.Type, "conduct_poisoned", StringComparison.OrdinalIgnoreCase))
            {
                isOpenly = false;
                wOpenly = 0.0;
                wHidden = 1.0;
            }
            else
            {
                int valor = traitLookup?.Of(hearer.HeroId)?.Valor ?? 0;
                double coef = cfgSecrets.Owner?.OpenlyValorWeight ?? 0.5;
                wOpenly = Math.Max(0.1, 1.0 + coef * valor);
                wHidden = Math.Max(0.1, 1.0 - coef * valor);

                long seed = RumorSeed.Of(campaignSeed, evt.EventId, hearer.HeroId, "ownerReaction");
                var r = rng ?? new SplitMix64Rng();
                int pick = r.PickWeighted(new[] { wOpenly, wHidden }, seed);
                isOpenly = (pick == 0);
            }

            hearer.OwnerReaction = isOpenly ? "openly" : "hidden";
            outcome.OwnerReaction = hearer.OwnerReaction;
            bool ledgerOnly = !isOpenly;

            if (knowsOriginator && !string.IsNullOrEmpty(leakerId))
            {
                double amt = cfgSecrets.Owner?.GrudgeOnOriginator ?? -6.0;
                outcome.Changes.Add(new SecretReactionChange
                {
                    ObserverHeroId = hearer.HeroId,
                    TargetHeroId = leakerId!,
                    Amount = amt,
                    LedgerOnly = ledgerOnly,
                    IsOwnerReaction = true,
                    RuleDescription = "grudge on originator"
                });
            }
            else if (outcome.SuspectedHeroIds != null && outcome.SuspectedHeroIds.Count > 0)
            {
                double amt = cfgSecrets.Owner?.Suspicion ?? -3.0;
                foreach (var suspect in outcome.SuspectedHeroIds)
                {
                    outcome.Changes.Add(new SecretReactionChange
                    {
                        ObserverHeroId = hearer.HeroId,
                        TargetHeroId = suspect,
                        Amount = amt,
                        LedgerOnly = ledgerOnly,
                        IsOwnerReaction = true,
                        RuleDescription = "suspicion"
                    });
                }
            }

            // If openly and endured secret, convert earlier ledger-only -6 grudge to native
            if (isOpenly && string.Equals(evt.Type, "seat_dispute_grudge_held", StringComparison.OrdinalIgnoreCase))
            {
                ConvertEnduredGrudge(evt, hearer, loadEvent, outcome);
            }

            outcome.Handled = true;
            string reactionWhat = knowsOriginator
                ? $"grudge on originator {leakerId}"
                : $"suspecting {outcome.SuspectedHeroIds?.Count ?? 0} hero(es) [{(outcome.SuspectedHeroIds == null ? string.Empty : string.Join(", ", outcome.SuspectedHeroIds.Select(id => $"{id} ({(suspectReasons.TryGetValue(id, out var r) ? r : "unknown")})")))}]";
            outcome.LogDetail = $"owner {hearer.HeroId} heard secret: {hearer.OwnerReaction} (wOpenly={wOpenly:F2}, wHidden={wHidden:F2}), {reactionWhat}";
        }

        private static void ConvertEnduredGrudge(
            WorldEvent evt,
            KnownByEntry hearer,
            Func<string, WorldEvent?>? loadEvent,
            SecretReactionOutcome outcome)
        {
            WorldEvent? linkedEvt = null;
            if (loadEvent != null && !string.IsNullOrEmpty(evt.LinkedEventId))
            {
                linkedEvt = loadEvent(evt.LinkedEventId!);
            }

            string favoredId = string.Empty;
            if (evt.Participants != null && evt.Participants.TryGetValue("favored", out var fId))
            {
                favoredId = fId ?? string.Empty;
            }
            if (string.IsNullOrEmpty(favoredId) && linkedEvt?.Participants != null)
            {
                linkedEvt.Participants.TryGetValue("favored", out favoredId);
            }

            RelationImpact? impactToConvert = null;
            if (linkedEvt != null)
            {
                var entry = linkedEvt.EntryFor(hearer.HeroId);
                impactToConvert = entry?.RelationImpacts?.FirstOrDefault(ri =>
                    ri != null && ri.LedgerOnly &&
                    (string.IsNullOrEmpty(favoredId) || string.Equals(ri.AboutHeroId, favoredId, StringComparison.Ordinal)));
            }

            if (impactToConvert == null && hearer.RelationImpacts != null)
            {
                impactToConvert = hearer.RelationImpacts.FirstOrDefault(ri =>
                    ri != null && ri.LedgerOnly &&
                    (string.IsNullOrEmpty(favoredId) || string.Equals(ri.AboutHeroId, favoredId, StringComparison.Ordinal)));
            }

            if (impactToConvert != null)
            {
                impactToConvert.LedgerOnly = false;
                outcome.Changes.Add(new SecretReactionChange
                {
                    ObserverHeroId = hearer.HeroId,
                    TargetHeroId = impactToConvert.AboutHeroId,
                    Amount = impactToConvert.Requested,
                    LedgerOnly = false,
                    IsOwnerReaction = true,
                    IsEnduredGrudgeConversion = true,
                    RuleDescription = "endured grudge converted to native"
                });
            }
            else if (!string.IsNullOrEmpty(favoredId))
            {
                outcome.Changes.Add(new SecretReactionChange
                {
                    ObserverHeroId = hearer.HeroId,
                    TargetHeroId = favoredId,
                    Amount = -6.0,
                    LedgerOnly = false,
                    IsOwnerReaction = true,
                    IsEnduredGrudgeConversion = true,
                    RuleDescription = "endured grudge converted to native (default amount)"
                });
            }
        }

        private static void EvaluateHearerReaction(
            WorldEvent evt,
            KnownByEntry hearer,
            EventTemplate? template,
            ISecretRelationshipLookup? relationshipLookup,
            IHeroTraitLookup? traitLookup,
            VividWorldConfig? config,
            SecretReactionOutcome outcome)
        {
            // Confided secrets that have not leaked or entries learned before leak do not trigger
            bool leaked = evt.State?.Leaked ?? false;
            double leakedDay = evt.State?.LeakedDay ?? double.MaxValue;
            if (!leaked || hearer.LearnedDay < leakedDay || hearer.Hop == 0)
            {
                outcome.Exclusions.Add(new SecretReactionExclusion
                {
                    HeroId = hearer.HeroId,
                    Reason = "confided"
                });
                outcome.LogDetail = $"listener {hearer.HeroId} learned via confide (learned {hearer.LearnedDay:F1}, leaked {leakedDay:F1}, leaked={leaked})";
                return;
            }

            if (hearer.Believes.HasValue && !hearer.Believes.Value)
            {
                outcome.LogDetail = $"listener {hearer.HeroId} disbelieved secret {evt.EventId}";
                return;
            }

            if (template?.OnHeard == null || template.OnHeard.Count == 0)
            {
                outcome.LogDetail = $"no onHeard rules for {evt.Type}";
                return;
            }

            var appliedTargets = new HashSet<string>(StringComparer.Ordinal);
            var logParts = new List<string>();

            foreach (var rule in template.OnHeard)
            {
                if (rule == null) continue;

                // Match listener
                bool listenerMatches = false;

                if (!string.IsNullOrEmpty(rule.ListenerRole))
                {
                    if (evt.Participants != null &&
                        evt.Participants.TryGetValue(rule.ListenerRole!, out var rHero) &&
                        string.Equals(rHero, hearer.HeroId, StringComparison.Ordinal))
                    {
                        listenerMatches = true;
                    }
                }
                else if (!string.IsNullOrEmpty(rule.ListenerKinOf))
                {
                    if (evt.Participants != null &&
                        evt.Participants.TryGetValue(rule.ListenerKinOf!, out var rHero) &&
                        !string.IsNullOrEmpty(rHero) &&
                        relationshipLookup != null &&
                        relationshipLookup.IsKin(hearer.HeroId, rHero!))
                    {
                        listenerMatches = true;
                    }
                }
                else if (!string.IsNullOrEmpty(rule.ListenerFriendsOf))
                {
                    if (evt.Participants != null &&
                        evt.Participants.TryGetValue(rule.ListenerFriendsOf!, out var rHero) &&
                        !string.IsNullOrEmpty(rHero) &&
                        relationshipLookup != null)
                    {
                        bool isKin = relationshipLookup.IsKin(hearer.HeroId, rHero!);
                        int aff = relationshipLookup.GetAffection(hearer.HeroId, rHero!) ?? 0;
                        if (!isKin && aff > 50)
                        {
                            listenerMatches = true;
                        }
                    }
                }
                else if (!string.IsNullOrEmpty(rule.ListenerCategory))
                {
                    string cat = rule.ListenerCategory!.Trim();
                    if (string.Equals(cat, "secretHarmed", StringComparison.OrdinalIgnoreCase))
                    {
                        listenerMatches = evt.SecretHarmedHeroIds != null && evt.SecretHarmedHeroIds.Contains(hearer.HeroId);
                    }
                    else if (string.Equals(cat, "secretHarmed[0]", StringComparison.OrdinalIgnoreCase) ||
                             string.Equals(cat, "secretHarmedKing", StringComparison.OrdinalIgnoreCase))
                    {
                        if (evt.Extra != null && evt.Extra.TryGetValue("secretHarmedKing", out var kingToken) && kingToken != null)
                        {
                            string kingId = kingToken.ToString();
                            listenerMatches = !string.IsNullOrEmpty(kingId) && string.Equals(kingId, hearer.HeroId, StringComparison.Ordinal);
                        }
                        else
                        {
                            listenerMatches = evt.SecretHarmedHeroIds != null && evt.SecretHarmedHeroIds.Count > 0 &&
                                              string.Equals(evt.SecretHarmedHeroIds[0], hearer.HeroId, StringComparison.Ordinal);
                        }
                    }
                    else if (string.Equals(cat, "secretHarmed[1]", StringComparison.OrdinalIgnoreCase) ||
                             string.Equals(cat, "secretHarmedOwner", StringComparison.OrdinalIgnoreCase))
                    {
                        if (evt.Extra != null && evt.Extra.TryGetValue("secretHarmedOwner", out var ownerToken) && ownerToken != null)
                        {
                            string ownerId = ownerToken.ToString();
                            listenerMatches = !string.IsNullOrEmpty(ownerId) && string.Equals(ownerId, hearer.HeroId, StringComparison.Ordinal);
                        }
                        else
                        {
                            listenerMatches = evt.SecretHarmedHeroIds != null && evt.SecretHarmedHeroIds.Count > 1 &&
                                              string.Equals(evt.SecretHarmedHeroIds[1], hearer.HeroId, StringComparison.Ordinal);
                        }
                    }
                }
                else if (rule.ListenerHarmedIndex.HasValue)
                {
                    int idx = rule.ListenerHarmedIndex.Value;
                    listenerMatches = evt.SecretHarmedHeroIds != null && idx >= 0 && idx < evt.SecretHarmedHeroIds.Count &&
                                      string.Equals(evt.SecretHarmedHeroIds[idx], hearer.HeroId, StringComparison.Ordinal);
                }

                if (!listenerMatches) continue;

                // Match trait sign if specified
                if (!string.IsNullOrEmpty(rule.ListenerTrait) && !string.IsNullOrEmpty(rule.ListenerTraitSign))
                {
                    int tLevel = OpinionTraits.LevelOf(rule.ListenerTrait, traitLookup?.Of(hearer.HeroId)) ?? 0;
                    string sign = rule.ListenerTraitSign!.Trim().ToLowerInvariant();
                    bool signMatch = sign switch
                    {
                        "positive" or "+" => tLevel > 0,
                        "zero" or "0" => tLevel == 0,
                        "negative" or "-" => tLevel < 0,
                        _ => false
                    };
                    if (!signMatch) continue;
                }

                // Check condition (e.g. friendship vs liege)
                string? cond = rule.Condition ?? rule.TargetCondition;
                if (!string.IsNullOrEmpty(cond))
                {
                    if (string.Equals(cond, "friendship", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(cond, "not_liege", StringComparison.OrdinalIgnoreCase))
                    {
                        if (string.Equals(evt.SecretPath, "liege", StringComparison.OrdinalIgnoreCase))
                        {
                            outcome.Exclusions.Add(new SecretReactionExclusion
                            {
                                HeroId = hearer.HeroId,
                                Reason = "liege"
                            });
                            logParts.Add($"rule target {rule.TargetRole} skipped: liege path");
                            continue;
                        }
                    }
                }
                else if (string.Equals(rule.TargetRole, "thrower", StringComparison.OrdinalIgnoreCase) &&
                         string.Equals(evt.SecretPath, "liege", StringComparison.OrdinalIgnoreCase))
                {
                    outcome.Exclusions.Add(new SecretReactionExclusion
                    {
                        HeroId = hearer.HeroId,
                        Reason = "liege"
                    });
                    logParts.Add($"rule target {rule.TargetRole} skipped: liege path");
                    continue;
                }

                // Target
                string targetId = string.Empty;
                if (!string.IsNullOrEmpty(rule.TargetRole))
                {
                    if (evt.Participants != null)
                    {
                        evt.Participants.TryGetValue(rule.TargetRole!, out targetId);
                    }
                }

                if (string.IsNullOrEmpty(targetId) || string.Equals(targetId, hearer.HeroId, StringComparison.Ordinal))
                {
                    continue;
                }

                // Only one rule applied per target
                if (appliedTargets.Contains(targetId))
                {
                    continue;
                }

                // Amount
                double amount = 0.0;
                int honor = traitLookup?.Of(hearer.HeroId)?.Honor ?? 0;
                double[]? tierAmounts = null;
                if (!string.IsNullOrEmpty(rule.AmountRef))
                {
                    tierAmounts = SecretConfigResolver.ResolveDoubleArray(rule.AmountRef, config);
                }
                if (tierAmounts == null && rule.Amounts != null && rule.Amounts.Count > 0)
                {
                    tierAmounts = rule.Amounts.ToArray();
                }

                if (tierAmounts != null && tierAmounts.Length > 0)
                {
                    if (tierAmounts.Length >= 3)
                    {
                        if (honor > 0) amount = tierAmounts[0];
                        else if (honor == 0) amount = tierAmounts[1];
                        else amount = tierAmounts[2];
                    }
                    else
                    {
                        amount = tierAmounts[0];
                    }
                }
                else if (!string.IsNullOrEmpty(rule.AmountRef))
                {
                    var resolved = SecretConfigResolver.ResolveDouble(rule.AmountRef, config);
                    amount = resolved ?? (rule.Amount ?? 0.0);
                }
                else if (rule.Amount.HasValue)
                {
                    amount = rule.Amount.Value;
                }

                amount = Math.Max(-100.0, Math.Min(100.0, amount));
                bool escalateClan = string.Equals(rule.Escalate, "clan", StringComparison.OrdinalIgnoreCase);

                outcome.Changes.Add(new SecretReactionChange
                {
                    ObserverHeroId = hearer.HeroId,
                    TargetHeroId = targetId,
                    Amount = amount,
                    EscalateClan = escalateClan,
                    LedgerOnly = false,
                    RuleDescription = $"onHeard: {rule.TargetRole} (amount {amount:F1}, escalate={escalateClan})"
                });

                appliedTargets.Add(targetId);
                logParts.Add($"{hearer.HeroId} matched {rule.TargetRole} -> {targetId} amount {amount:F1} (escalate={escalateClan})");
            }

            outcome.Handled = true;
            outcome.LogDetail = logParts.Count > 0 ? string.Join("; ", logParts) : "no matching onHeard rules";
        }
    }
}
