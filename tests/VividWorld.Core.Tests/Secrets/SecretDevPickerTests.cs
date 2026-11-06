#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using VividWorld.Core.Catalog;
using VividWorld.Core.Events;
using VividWorld.Core.Secrets;
using Xunit;

namespace VividWorld.Core.Tests.Secrets
{
    public class SecretDevPickerTests
    {
        private sealed class FakeSecretRelationshipLookup : ISecretRelationshipLookup
        {
            public HashSet<(string, string)> KinPairs { get; } = new();
            public Dictionary<(string, string), int> AffectionMap { get; } = new();

            public IReadOnlyCollection<string> GetKin(string heroId)
            {
                var list = new List<string>();
                foreach (var pair in KinPairs)
                {
                    if (pair.Item1 == heroId) list.Add(pair.Item2);
                    else if (pair.Item2 == heroId) list.Add(pair.Item1);
                }
                return list;
            }

            public IReadOnlyCollection<string> GetFriends(string heroId)
            {
                var list = new List<string>();
                foreach (var kvp in AffectionMap)
                {
                    if (kvp.Value > 50)
                    {
                        if (kvp.Key.Item1 == heroId) list.Add(kvp.Key.Item2);
                        else if (kvp.Key.Item2 == heroId) list.Add(kvp.Key.Item1);
                    }
                }
                return list;
            }

            public bool IsKin(string heroA, string heroB)
            {
                return KinPairs.Contains((heroA, heroB)) || KinPairs.Contains((heroB, heroA));
            }

            public int? GetAffection(string heroA, string heroB)
            {
                if (AffectionMap.TryGetValue((heroA, heroB), out int aff)) return aff;
                if (AffectionMap.TryGetValue((heroB, heroA), out aff)) return aff;
                return 0;
            }
        }

        // ==========================================
        // Tool 1: SelectPublicSideSecret
        // ==========================================

        [Fact]
        public void Tool1_ExcludesNonSecret()
        {
            var publicEvt = new WorldEvent
            {
                EventId = "evt_pub",
                Origin = EventOrigin.Public,
                LinkedEventId = "linked_pub"
            };

            var res = SecretDevPicker.SelectPublicSideSecret(new[] { publicEvt }, _ => false, id => new WorldEvent { EventId = id });
            Assert.False(res.IsEligible);
            Assert.Null(res.ChosenSecret);
            Assert.Contains("not a secret", res.Candidates[0].DisqualifiedReason);
        }

        [Fact]
        public void Tool1_ExcludesRetiredSecret()
        {
            var secret = new WorldEvent
            {
                EventId = "sec_retired",
                Origin = EventOrigin.Secret,
                SecretRetired = true,
                LinkedEventId = "linked_pub"
            };

            var res = SecretDevPicker.SelectPublicSideSecret(new[] { secret }, _ => false, id => new WorldEvent { EventId = id });
            Assert.False(res.IsEligible);
            Assert.Contains("secret retired", res.Candidates[0].DisqualifiedReason);
        }

        [Fact]
        public void Tool1_ExcludesLeakedSecret()
        {
            var secret = new WorldEvent
            {
                EventId = "sec_leaked",
                Origin = EventOrigin.Secret,
                State = new RumorState { Leaked = true },
                LinkedEventId = "linked_pub"
            };

            var res = SecretDevPicker.SelectPublicSideSecret(new[] { secret }, _ => false, id => new WorldEvent { EventId = id });
            Assert.False(res.IsEligible);
            Assert.Contains("already leaked", res.Candidates[0].DisqualifiedReason);
        }

        [Fact]
        public void Tool1_ExcludesMissingLinkedEvent()
        {
            var secret = new WorldEvent
            {
                EventId = "sec_nolink",
                Origin = EventOrigin.Secret,
                LinkedEventId = null
            };

            var res = SecretDevPicker.SelectPublicSideSecret(new[] { secret }, _ => false, id => new WorldEvent { EventId = id });
            Assert.False(res.IsEligible);
            Assert.Contains("missing linked public event", res.Candidates[0].DisqualifiedReason);
        }

        [Fact]
        public void Tool1_ExcludesLinkedEventNotFound()
        {
            var secret = new WorldEvent
            {
                EventId = "sec_missing_link",
                Origin = EventOrigin.Secret,
                LinkedEventId = "pub_not_found"
            };

            var res = SecretDevPicker.SelectPublicSideSecret(new[] { secret }, _ => false, _ => null);
            Assert.False(res.IsEligible);
            Assert.Contains("not found", res.Candidates[0].DisqualifiedReason);
        }

        [Fact]
        public void Tool1_ExcludesWhenPlayerAlreadyHeardPublicEvent()
        {
            var secret = new WorldEvent
            {
                EventId = "sec_heard",
                Origin = EventOrigin.Secret,
                LinkedEventId = "pub_heard"
            };

            var res = SecretDevPicker.SelectPublicSideSecret(new[] { secret }, id => id == "pub_heard", id => new WorldEvent { EventId = id });
            Assert.False(res.IsEligible);
            Assert.Contains("player already heard", res.Candidates[0].DisqualifiedReason);
        }

        [Fact]
        public void Tool1_EligiblePick_PrioritizesNewestSecret()
        {
            var pubOlder = new WorldEvent { EventId = "pub_older" };
            var pubNewer = new WorldEvent { EventId = "pub_newer" };

            var secretOlder = new WorldEvent
            {
                EventId = "sec_older",
                Origin = EventOrigin.Secret,
                Day = 10.0,
                LinkedEventId = "pub_older"
            };
            var secretNewer = new WorldEvent
            {
                EventId = "sec_newer",
                Origin = EventOrigin.Secret,
                Day = 25.0,
                LinkedEventId = "pub_newer"
            };

            var store = new Dictionary<string, WorldEvent>
            {
                ["pub_older"] = pubOlder,
                ["pub_newer"] = pubNewer
            };

            var res = SecretDevPicker.SelectPublicSideSecret(
                new[] { secretOlder, secretNewer },
                _ => false,
                id => store.TryGetValue(id, out var e) ? e : null);

            Assert.True(res.IsEligible);
            Assert.Equal("sec_newer", res.ChosenSecret?.EventId);
            Assert.Equal("pub_newer", res.LinkedPublicEvent?.EventId);
        }

        // ==========================================
        // Tool 2: SelectFriendSecret
        // ==========================================

        [Fact]
        public void Tool2_ExcludesRetiredOrLeakedOrNoLinked()
        {
            var secRetired = new WorldEvent { EventId = "s1", Origin = EventOrigin.Secret, SecretRetired = true, LinkedEventId = "p1" };
            var secLeaked = new WorldEvent { EventId = "s2", Origin = EventOrigin.Secret, State = new RumorState { Leaked = true }, LinkedEventId = "p2" };
            var secNoLink = new WorldEvent { EventId = "s3", Origin = EventOrigin.Secret, LinkedEventId = null };

            var res = SecretDevPicker.SelectFriendSecret(new[] { secRetired, secLeaked, secNoLink }, "hero1", new FakeSecretRelationshipLookup());
            Assert.False(res.IsEligible);
            Assert.Contains("secret retired", res.Candidates[0].DisqualifiedReason);
            Assert.Contains("already leaked", res.Candidates[1].DisqualifiedReason);
            Assert.Contains("missing linked public event", res.Candidates[2].DisqualifiedReason);
        }

        [Fact]
        public void Tool2_ExcludesWhenHeroAlreadyKnowsOrIsOwnerOrHarmed()
        {
            var secKnown = new WorldEvent
            {
                EventId = "s1",
                Origin = EventOrigin.Secret,
                LinkedEventId = "p1",
                SecretOwnerHeroIds = new List<string> { "ownerA" },
                KnownBy = new List<KnownByEntry> { new() { HeroId = "hero1" } }
            };

            var secOwner = new WorldEvent
            {
                EventId = "s2",
                Origin = EventOrigin.Secret,
                LinkedEventId = "p2",
                SecretOwnerHeroIds = new List<string> { "hero1" }
            };

            var secHarmed = new WorldEvent
            {
                EventId = "s3",
                Origin = EventOrigin.Secret,
                LinkedEventId = "p3",
                SecretOwnerHeroIds = new List<string> { "ownerA" },
                SecretHarmedHeroIds = new List<string> { "hero1" }
            };

            var lookup = new FakeSecretRelationshipLookup();
            lookup.KinPairs.Add(("hero1", "ownerA"));

            var res = SecretDevPicker.SelectFriendSecret(new[] { secKnown, secOwner, secHarmed }, "hero1", lookup);
            Assert.False(res.IsEligible);
            Assert.Contains("hero already knows", res.Candidates[0].DisqualifiedReason);
            Assert.Contains("hero is owner", res.Candidates[1].DisqualifiedReason);
            Assert.Contains("hero would be harmed", res.Candidates[2].DisqualifiedReason);
        }

        [Fact]
        public void Tool2_WhenHeroDoesNotProtectAnyOwner_FallsBackToRelationBoost()
        {
            var sec = new WorldEvent
            {
                EventId = "s1",
                Origin = EventOrigin.Secret,
                LinkedEventId = "p1",
                SecretOwnerHeroIds = new List<string> { "ownerA" }
            };

            var lookup = new FakeSecretRelationshipLookup();
            lookup.AffectionMap[("hero1", "ownerA")] = 35; // not kin, and 35 <= 50

            var res = SecretDevPicker.SelectFriendSecret(new[] { sec }, "hero1", lookup);
            Assert.True(res.IsEligible);
            Assert.True(res.RequiresRelationBoost);
            Assert.Equal("ownerA", res.TargetOwnerHeroId);
            Assert.Equal("s1", res.ChosenSecret?.EventId);
            Assert.Contains("does not protect any owner", res.Candidates[0].DisqualifiedReason);
        }

        [Fact]
        public void Tool2_Fallback_PicksNewestSecret()
        {
            var secOlder = new WorldEvent
            {
                EventId = "s_old",
                Origin = EventOrigin.Secret,
                LinkedEventId = "p1",
                Day = 10.0,
                SecretOwnerHeroIds = new List<string> { "ownerA" }
            };
            var secNewer = new WorldEvent
            {
                EventId = "s_new",
                Origin = EventOrigin.Secret,
                LinkedEventId = "p2",
                Day = 30.0,
                SecretOwnerHeroIds = new List<string> { "ownerB" }
            };

            var lookup = new FakeSecretRelationshipLookup();

            var res = SecretDevPicker.SelectFriendSecret(new[] { secOlder, secNewer }, "hero1", lookup);
            Assert.True(res.IsEligible);
            Assert.True(res.RequiresRelationBoost);
            Assert.Equal("s_new", res.ChosenSecret?.EventId);
            Assert.Equal("ownerB", res.TargetOwnerHeroId);
        }

        [Fact]
        public void Tool2_EligibleWhenHeroIsKinOrFriendOfOwner()
        {
            var secKin = new WorldEvent
            {
                EventId = "s_kin",
                Origin = EventOrigin.Secret,
                LinkedEventId = "p1",
                Day = 10.0,
                SecretOwnerHeroIds = new List<string> { "ownerKin" }
            };
            var secFriend = new WorldEvent
            {
                EventId = "s_friend",
                Origin = EventOrigin.Secret,
                LinkedEventId = "p2",
                Day = 20.0,
                SecretOwnerHeroIds = new List<string> { "ownerFriend" }
            };

            var lookup = new FakeSecretRelationshipLookup();
            lookup.KinPairs.Add(("hero1", "ownerKin"));
            lookup.AffectionMap[("hero1", "ownerFriend")] = 60; // > 50

            var res = SecretDevPicker.SelectFriendSecret(new[] { secKin, secFriend }, "hero1", lookup);
            Assert.True(res.IsEligible);
            // sec_friend is Day 20.0 (newer than Day 10.0), so it is chosen first
            Assert.Equal("s_friend", res.ChosenSecret?.EventId);
            Assert.Equal("ownerFriend", res.SourceOwnerHeroId);
        }

        // ==========================================
        // Tool 3: SelectLeakedSecretForHearer
        // ==========================================

        [Fact]
        public void Tool3_ExcludesUnleakedRetiredOrOldShape()
        {
            var notLeaked = new WorldEvent { EventId = "s1", Origin = EventOrigin.Secret, State = new RumorState { Leaked = false }, SecretOwnerHeroIds = new List<string> { "o1" } };
            var retired = new WorldEvent { EventId = "s2", Origin = EventOrigin.Secret, State = new RumorState { Leaked = true }, SecretRetired = true, SecretOwnerHeroIds = new List<string> { "o1" } };
            var oldShape = new WorldEvent { EventId = "s3", Origin = EventOrigin.Secret, State = new RumorState { Leaked = true }, SecretOwnerHeroIds = null };

            var res = SecretDevPicker.SelectLeakedSecretForHearer(new[] { notLeaked, retired, oldShape }, "hearer1", new FakeSecretRelationshipLookup(), null);
            Assert.False(res.IsEligible);
            Assert.Contains("not leaked", res.Candidates[0].DisqualifiedReason);
            Assert.Contains("secret retired", res.Candidates[1].DisqualifiedReason);
            Assert.Contains("old shape", res.Candidates[2].DisqualifiedReason);
        }

        [Fact]
        public void Tool3_PrioritizesSecretWithReactionOverSecretWithoutReaction()
        {
            // Event 1: newer (Day 30), but hearer has no reaction
            var secNoReaction = new WorldEvent
            {
                EventId = "sec_no_rx",
                Type = "test_secret_type",
                Origin = EventOrigin.Secret,
                Day = 30.0,
                State = new RumorState { Leaked = true },
                SecretOwnerHeroIds = new List<string> { "owner1" }
            };

            // Event 2: older (Day 15), but hearer is harmed and in onHeard
            var secWithReaction = new WorldEvent
            {
                EventId = "sec_with_rx",
                Type = "test_secret_type",
                Origin = EventOrigin.Secret,
                Day = 15.0,
                State = new RumorState { Leaked = true },
                SecretOwnerHeroIds = new List<string> { "owner2" },
                SecretHarmedHeroIds = new List<string> { "hearer1" }
            };

            var tmpl = new EventTemplate
            {
                Type = "test_secret_type",
                OnHeard = new List<TemplateOnHeardRule>
                {
                    new() { ListenerCategory = "secretHarmed" }
                }
            };

            var res = SecretDevPicker.SelectLeakedSecretForHearer(
                new[] { secNoReaction, secWithReaction },
                "hearer1",
                new FakeSecretRelationshipLookup(),
                null,
                _ => tmpl);

            Assert.True(res.IsEligible);
            // sec_with_rx should be chosen despite being older because it has a reaction!
            Assert.Equal("sec_with_rx", res.ChosenSecret?.EventId);
            Assert.True(res.HasReaction);
        }

        [Fact]
        public void Tool3_OwnerReactionPriority_UnreactedOwnerPrioritizedOverReactedOwner()
        {
            var secReacted = new WorldEvent
            {
                EventId = "sec_reacted",
                Origin = EventOrigin.Secret,
                Day = 40.0,
                State = new RumorState { Leaked = true },
                SecretOwnerHeroIds = new List<string> { "ownerHero" },
                KnownBy = new List<KnownByEntry> { new() { HeroId = "ownerHero", OwnerReaction = "openly" } }
            };

            var secUnreacted = new WorldEvent
            {
                EventId = "sec_unreacted",
                Origin = EventOrigin.Secret,
                Day = 20.0,
                State = new RumorState { Leaked = true },
                SecretOwnerHeroIds = new List<string> { "ownerHero" },
                KnownBy = new List<KnownByEntry> { new() { HeroId = "ownerHero", OwnerReaction = null } }
            };

            var res = SecretDevPicker.SelectLeakedSecretForHearer(
                new[] { secReacted, secUnreacted },
                "ownerHero",
                new FakeSecretRelationshipLookup(),
                null);

            Assert.True(res.IsEligible);
            Assert.Equal("sec_unreacted", res.ChosenSecret?.EventId);
            Assert.True(res.HasReaction);
        }

        [Fact]
        public void Tool3_WhenNoLeakedSecretsWithReaction_PicksUnleakedSecretWithReactionForForcedLeak()
        {
            // Event 1: leaked, but hearer has no reaction
            var secLeakedNoRx = new WorldEvent
            {
                EventId = "sec_leaked_no_rx",
                Type = "test_type",
                Origin = EventOrigin.Secret,
                Day = 40.0,
                State = new RumorState { Leaked = true },
                SecretOwnerHeroIds = new List<string> { "owner1" }
            };

            // Event 2: unleaked, but hearer is harmed and matches onHeard
            var secUnleakedWithRx = new WorldEvent
            {
                EventId = "sec_unleaked_rx",
                Type = "test_type",
                Origin = EventOrigin.Secret,
                Day = 25.0,
                State = new RumorState { Leaked = false },
                SecretOwnerHeroIds = new List<string> { "owner2" },
                SecretHarmedHeroIds = new List<string> { "hearer1" },
                KnownBy = new List<KnownByEntry>
                {
                    new() { HeroId = "owner2" },
                    new() { HeroId = "confidedFriend" }
                }
            };

            var tmpl = new EventTemplate
            {
                Type = "test_type",
                OnHeard = new List<TemplateOnHeardRule>
                {
                    new() { ListenerCategory = "secretHarmed" }
                }
            };

            var res = SecretDevPicker.SelectLeakedSecretForHearer(
                new[] { secLeakedNoRx, secUnleakedWithRx },
                "hearer1",
                new FakeSecretRelationshipLookup(),
                null,
                _ => tmpl);

            Assert.True(res.IsEligible);
            Assert.Equal("sec_unleaked_rx", res.ChosenSecret?.EventId);
            Assert.True(res.HasReaction);
            Assert.True(res.WasForcedLeak);
            Assert.Equal("confidedFriend", res.LeakerHeroId); // first non-owner knower
        }

        [Fact]
        public void Tool3_DetermineLeakerHeroId_PriorityOrder()
        {
            // 1. First non-owner knower
            var evt1 = new WorldEvent
            {
                SecretOwnerHeroIds = new List<string> { "ownerA", "ownerB" },
                KnownBy = new List<KnownByEntry>
                {
                    new() { HeroId = "ownerA" },
                    new() { HeroId = "nonOwner1" },
                    new() { HeroId = "nonOwner2" }
                }
            };
            Assert.Equal("nonOwner1", SecretDevPicker.DetermineLeakerHeroId(evt1, "hearerX"));

            // 2. No non-owner knower: picks other owner (not the hearer)
            var evt2 = new WorldEvent
            {
                SecretOwnerHeroIds = new List<string> { "ownerA", "ownerB" },
                KnownBy = new List<KnownByEntry>
                {
                    new() { HeroId = "ownerA" }
                }
            };
            // When hearer is ownerA, other owner is ownerB
            Assert.Equal("ownerB", SecretDevPicker.DetermineLeakerHeroId(evt2, "ownerA"));
            // When hearer is not an owner, other owner is ownerA
            Assert.Equal("ownerA", SecretDevPicker.DetermineLeakerHeroId(evt2, "thirdParty"));

            // 3. Neither other non-owner nor other owner: falls back to hearer
            var evt3 = new WorldEvent
            {
                SecretOwnerHeroIds = new List<string> { "ownerOnly" },
                KnownBy = new List<KnownByEntry>
                {
                    new() { HeroId = "ownerOnly" }
                }
            };
            Assert.Equal("ownerOnly", SecretDevPicker.DetermineLeakerHeroId(evt3, "ownerOnly"));
        }

        // ==========================================
        // Tool 4: FindFirstStakeablePair
        // ==========================================

        [Fact]
        public void Tool4_TriesOpponentsInGivenOrder_StopsAtFirstEligible()
        {
            string bettorId = "bettor1";
            var staked1 = new WorldEvent
            {
                EventId = "sec1",
                Origin = EventOrigin.Secret,
                DramaWeight = 7,
                DramaScale = DramaScales.Ten,
                SecretOwnerHeroIds = new List<string> { "third_party" },
                KnownBy = new List<KnownByEntry>
                {
                    new() { HeroId = bettorId },
                    new() { HeroId = "opp1" } // opp1 already knows sec1
                }
            };
            var staked2 = new WorldEvent
            {
                EventId = "sec2",
                Origin = EventOrigin.Secret,
                DramaWeight = 5,
                DramaScale = DramaScales.Ten,
                SecretOwnerHeroIds = new List<string> { "third_party_2" },
                KnownBy = new List<KnownByEntry>
                {
                    new() { HeroId = bettorId }
                    // opp2 does not know sec2
                }
            };

            var secrets = new List<WorldEvent> { staked1, staked2 };
            var candidateChallengers = new List<string> { "opp1", "opp2", "opp3" };

            // When testing opp1, sec1 is known by opp1, but sec2 is not known by opp1!
            // So opp1 will qualify with sec2!
            var res1 = SecretDevPicker.FindFirstStakeablePair(bettorId, candidateChallengers, "player", secrets);
            Assert.True(res1.IsEligible);
            Assert.Equal("opp1", res1.ChosenOpponentHeroId);
            Assert.Equal("sec2", res1.ChosenSecret?.EventId);

            // Now test where opp1 knows ALL secrets, but opp2 does not
            staked2.KnownBy.Add(new KnownByEntry { HeroId = "opp1" });
            var res2 = SecretDevPicker.FindFirstStakeablePair(bettorId, candidateChallengers, "player", secrets);
            Assert.True(res2.IsEligible);
            Assert.Equal("opp2", res2.ChosenOpponentHeroId);
            Assert.Equal(2, res2.TestedOpponents.Count); // stopped at opp2!
        }

        [Fact]
        public void Tool4_WhenNoneEligible_ReturnsAllTestedOpponentsAndReasons()
        {
            string bettorId = "bettor1";
            var secrets = new List<WorldEvent>(); // bettor knows no secrets
            var candidateChallengers = new List<string> { "opp1", "opp2" };

            var res = SecretDevPicker.FindFirstStakeablePair(bettorId, candidateChallengers, "player", secrets);
            Assert.False(res.IsEligible);
            Assert.Null(res.ChosenOpponentHeroId);
            Assert.Equal(2, res.TestedOpponents.Count);
            Assert.Contains("none of 2 candidate challenger(s) qualified", res.SummaryReason);
        }

        // ==========================================
        // Tool 5: SelectPrisonerToFree
        // ==========================================

        [Fact]
        public void Tool5_ExcludesPrisonerWhenNeitherKinNorFriend()
        {
            var p1 = new FreePrisonerCandidate
            {
                PrisonerHeroId = "p1",
                IsKin = false,
                Relation = 20
            };
            var res = SecretDevPicker.SelectPrisonerToFree("freer1", new[] { p1 });
            // When neither kin nor friend, it falls back to picking the highest relation prisoner and sets IsForcedNonFriend
            Assert.True(res.IsEligible);
            Assert.True(res.IsForcedNonFriend);
            Assert.Equal("p1", res.ChosenPrisoner?.PrisonerHeroId);
            Assert.Contains("friend relation 20 <= 50", res.Candidates[0].DisqualifiedReason);
        }

        [Fact]
        public void Tool5_ExcludesKinWithRelationBelowOrEqualToThirty()
        {
            var p1 = new FreePrisonerCandidate
            {
                PrisonerHeroId = "p1",
                IsKin = true,
                Relation = 30
            };
            var res = SecretDevPicker.SelectPrisonerToFree("freer1", new[] { p1 });
            // When kin has relation <= 30, it falls back to picking them with IsForcedNonFriend
            Assert.True(res.IsEligible);
            Assert.True(res.IsForcedNonFriend);
            Assert.Equal("p1", res.ChosenPrisoner?.PrisonerHeroId);
            Assert.Contains("kin relation 30 <= 30", res.Candidates[0].DisqualifiedReason);
        }

        [Fact]
        public void Tool5_FailsWhenNoImprisonedLordsExist()
        {
            var res = SecretDevPicker.SelectPrisonerToFree("freer1", Array.Empty<FreePrisonerCandidate>());
            Assert.False(res.IsEligible);
            Assert.Null(res.ChosenPrisoner);
            Assert.Contains("no imprisoned lords", res.SummaryReason);
        }

        [Fact]
        public void Tool5_EligibleWhenKinAboveThirtyOrFriendAboveFifty_PicksHighestRelation()
        {
            var pKin = new FreePrisonerCandidate
            {
                PrisonerHeroId = "p_kin",
                IsKin = true,
                Relation = 40
            };
            var pFriend = new FreePrisonerCandidate
            {
                PrisonerHeroId = "p_friend",
                IsKin = false,
                Relation = 70
            };

            var res = SecretDevPicker.SelectPrisonerToFree("freer1", new[] { pKin, pFriend });
            Assert.True(res.IsEligible);
            // p_friend has relation 70 > 40, so highest relation is chosen!
            Assert.Equal("p_friend", res.ChosenPrisoner?.PrisonerHeroId);
        }

        // ==========================================
        // Tool 6: SelectUnrelatedSecret
        // ==========================================

        [Fact]
        public void Tool6_ExcludesKinOfAnyOwner()
        {
            var sec = new WorldEvent
            {
                EventId = "sec_kin",
                Origin = EventOrigin.Secret,
                LinkedEventId = "pub1",
                SecretOwnerHeroIds = new List<string> { "ownerA", "ownerB" }
            };

            var lookup = new FakeSecretRelationshipLookup();
            lookup.KinPairs.Add(("hero1", "ownerA"));
            lookup.AffectionMap[("hero1", "ownerB")] = 10;

            var res = SecretDevPicker.SelectUnrelatedSecret(new[] { sec }, "hero1", lookup);
            Assert.False(res.IsEligible);
            Assert.Null(res.ChosenSecret);
            Assert.Contains("protects owner", res.Candidates[0].DisqualifiedReason);
            Assert.Contains("protects owner: 1", res.SummaryReason);
        }

        [Fact]
        public void Tool6_ExcludesAffectionAboveFifty()
        {
            var sec = new WorldEvent
            {
                EventId = "sec_aff51",
                Origin = EventOrigin.Secret,
                LinkedEventId = "pub1",
                SecretOwnerHeroIds = new List<string> { "ownerA" }
            };

            var lookup = new FakeSecretRelationshipLookup();
            lookup.AffectionMap[("hero1", "ownerA")] = 51;

            var res = SecretDevPicker.SelectUnrelatedSecret(new[] { sec }, "hero1", lookup);
            Assert.False(res.IsEligible);
            Assert.Null(res.ChosenSecret);
            Assert.Contains("protects owner", res.Candidates[0].DisqualifiedReason);
            Assert.Contains("protects owner: 1", res.SummaryReason);
        }

        [Fact]
        public void Tool6_AffectionFifty_IsEligible()
        {
            var sec = new WorldEvent
            {
                EventId = "sec_aff50",
                Origin = EventOrigin.Secret,
                LinkedEventId = "pub1",
                SecretOwnerHeroIds = new List<string> { "ownerA" }
            };

            var lookup = new FakeSecretRelationshipLookup();
            lookup.AffectionMap[("hero1", "ownerA")] = 50;

            var res = SecretDevPicker.SelectUnrelatedSecret(new[] { sec }, "hero1", lookup);
            Assert.True(res.IsEligible);
            Assert.NotNull(res.ChosenSecret);
            Assert.Equal("sec_aff50", res.ChosenSecret?.EventId);
            Assert.Equal("ownerA", res.FirstOwnerHeroId);
            Assert.Empty(res.Candidates[0].DisqualifiedReason);
        }

        [Fact]
        public void Tool6_ExcludesOwner()
        {
            var sec = new WorldEvent
            {
                EventId = "sec_owner",
                Origin = EventOrigin.Secret,
                LinkedEventId = "pub1",
                SecretOwnerHeroIds = new List<string> { "hero1", "ownerA" }
            };

            var lookup = new FakeSecretRelationshipLookup();

            var res = SecretDevPicker.SelectUnrelatedSecret(new[] { sec }, "hero1", lookup);
            Assert.False(res.IsEligible);
            Assert.Null(res.ChosenSecret);
            Assert.Contains("hero is owner", res.Candidates[0].DisqualifiedReason);
        }

        [Fact]
        public void Tool6_ExcludesHarmed()
        {
            var sec = new WorldEvent
            {
                EventId = "sec_harmed",
                Origin = EventOrigin.Secret,
                LinkedEventId = "pub1",
                SecretOwnerHeroIds = new List<string> { "ownerA" },
                SecretHarmedHeroIds = new List<string> { "hero1" }
            };

            var lookup = new FakeSecretRelationshipLookup();
            lookup.AffectionMap[("hero1", "ownerA")] = 0;

            var res = SecretDevPicker.SelectUnrelatedSecret(new[] { sec }, "hero1", lookup);
            Assert.False(res.IsEligible);
            Assert.Null(res.ChosenSecret);
            Assert.Contains("hero would be harmed", res.Candidates[0].DisqualifiedReason);
        }

        [Fact]
        public void Tool6_EligiblePick_PrioritizesNewestSecret()
        {
            var secOlder = new WorldEvent
            {
                EventId = "sec_old",
                Origin = EventOrigin.Secret,
                LinkedEventId = "pub1",
                Day = 10.0,
                SecretOwnerHeroIds = new List<string> { "ownerA" }
            };
            var secNewerA = new WorldEvent
            {
                EventId = "sec_new_a",
                Origin = EventOrigin.Secret,
                LinkedEventId = "pub2",
                Day = 30.0,
                SecretOwnerHeroIds = new List<string> { "ownerB" }
            };
            var secNewerB = new WorldEvent
            {
                EventId = "sec_new_b",
                Origin = EventOrigin.Secret,
                LinkedEventId = "pub3",
                Day = 30.0,
                SecretOwnerHeroIds = new List<string> { "ownerC" }
            };

            var lookup = new FakeSecretRelationshipLookup();
            lookup.AffectionMap[("hero1", "ownerA")] = 10;
            lookup.AffectionMap[("hero1", "ownerB")] = 20;
            lookup.AffectionMap[("hero1", "ownerC")] = 30;

            var res = SecretDevPicker.SelectUnrelatedSecret(new[] { secOlder, secNewerB, secNewerA }, "hero1", lookup);
            Assert.True(res.IsEligible);
            // Day 30 is newer than Day 10; between sec_new_a and sec_new_b, "sec_new_a" comes first alphabetically
            Assert.Equal("sec_new_a", res.ChosenSecret?.EventId);
            Assert.Equal("ownerB", res.FirstOwnerHeroId);
        }

        [Fact]
        public void Tool6_ExcludesRetiredOrLeakedOrNoLinkedOrAlreadyKnown()
        {
            var secRetired = new WorldEvent { EventId = "s1", Origin = EventOrigin.Secret, SecretRetired = true, LinkedEventId = "p1", SecretOwnerHeroIds = new List<string> { "ownerA" } };
            var secLeaked = new WorldEvent { EventId = "s2", Origin = EventOrigin.Secret, State = new RumorState { Leaked = true }, LinkedEventId = "p2", SecretOwnerHeroIds = new List<string> { "ownerA" } };
            var secNoLink = new WorldEvent { EventId = "s3", Origin = EventOrigin.Secret, LinkedEventId = null, SecretOwnerHeroIds = new List<string> { "ownerA" } };
            var secKnown = new WorldEvent { EventId = "s4", Origin = EventOrigin.Secret, LinkedEventId = "p4", SecretOwnerHeroIds = new List<string> { "ownerA" }, KnownBy = new List<KnownByEntry> { new() { HeroId = "hero1" } } };

            var lookup = new FakeSecretRelationshipLookup();

            var res = SecretDevPicker.SelectUnrelatedSecret(new[] { secRetired, secLeaked, secNoLink, secKnown }, "hero1", lookup);
            Assert.False(res.IsEligible);
            Assert.Contains("secret retired", res.Candidates[0].DisqualifiedReason);
            Assert.Contains("already leaked", res.Candidates[1].DisqualifiedReason);
            Assert.Contains("missing linked public event", res.Candidates[2].DisqualifiedReason);
            Assert.Contains("hero already knows", res.Candidates[3].DisqualifiedReason);
        }
    }
}
