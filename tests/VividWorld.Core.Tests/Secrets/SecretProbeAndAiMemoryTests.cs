#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using VividWorld.Core.Catalog;
using VividWorld.Core.Config;
using VividWorld.Core.Dialogue;
using VividWorld.Core.Events;
using VividWorld.Core.Memory;
using VividWorld.Core.Persistence;
using VividWorld.Core.Presentation;
using VividWorld.Core.Rumors;
using VividWorld.Core.Secrets;
using VividWorld.Core.Tests.Fakes;
using Xunit;

namespace VividWorld.Core.Tests.Secrets
{
    public class SecretProbeAndAiMemoryTests
    {
        private const string Teller = "lord_teller";
        private const string Player = "player_hero";
        private const string OwnerA = "lord_owner_a";
        private const string OwnerB = "lord_owner_b";
        private const double Today = 100.0;

        private static WorldEvent CreatePublicFace(string id = "public_1")
        {
            return new WorldEvent
            {
                EventId = id,
                Type = "tournament_won",
                Day = 90.0,
                Origin = EventOrigin.Public,
                State = new RumorState { Leaked = true },
                KnownBy = new List<KnownByEntry>
                {
                    new KnownByEntry { HeroId = Teller, Hop = 1, LearnedDay = 90.0 },
                    new KnownByEntry { HeroId = Player, Hop = 1, LearnedDay = 90.0 }
                },
                Facts = new List<Fact> { new Fact { Id = "f_pub", Category = FactCategory.What, Text = "won tournament" } }
            };
        }

        private static WorldEvent CreateHiddenPiece(
            string id = "secret_1",
            string linkedId = "public_1",
            IReadOnlyList<string>? owners = null,
            bool retired = false,
            bool leaked = false,
            int tellerHop = 0,
            string? ownerReaction = null)
        {
            var ownerList = owners != null ? owners.ToList() : new List<string> { OwnerA };
            return new WorldEvent
            {
                EventId = id,
                Type = "tournament_final_thrown",
                Day = 90.0,
                Origin = EventOrigin.Secret,
                LinkedEventId = linkedId,
                SecretRetired = retired,
                SecretOwnerHeroIds = ownerList,
                State = new RumorState { Leaked = leaked },
                KnownBy = new List<KnownByEntry>
                {
                    new KnownByEntry
                    {
                        HeroId = Teller,
                        Hop = tellerHop,
                        LearnedDay = 90.0,
                        OwnerReaction = ownerReaction
                    }
                },
                Facts = new List<Fact> { new Fact { Id = "f_sec", Category = FactCategory.What, Text = "threw match" } }
            };
        }

        private static ProbePlan ClassifyProbe(
            WorldEvent publicEvt,
            WorldEvent secretEvt,
            FakeSecretRelationshipLookup lookup,
            string tellerId = Teller)
        {
            var chain = new List<WorldEvent> { publicEvt, secretEvt };
            var heard = new List<PlayerHeardEntry>
            {
                new PlayerHeardEntry { EventId = publicEvt.EventId, RootEventId = publicEvt.EventId, PlayerHop = 1, UpdatedDay = 90.0 }
            };
            var cfg = new VividWorldConfig();
            return ProbeClassifier.Classify(
                tellerId,
                Player,
                new ChronicleEntry { EventId = publicEvt.EventId },
                heard,
                chain,
                Today,
                12345L,
                cfg,
                type => null,
                id => CandidateRejection.None,
                id => new RumorOffer { EventId = id },
                feelingWorld: null,
                traitLookup: null,
                secretLookup: lookup);
        }

        [Fact]
        public void Probe_SecretTell_WhenTrustsPlayerAndDoesNotProtectOwner()
        {
            var pub = CreatePublicFace();
            var sec = CreateHiddenPiece();

            var lookup = new FakeSecretRelationshipLookup();
            lookup.SetAffection(Teller, Player, 60); // > 50 (other)
            lookup.SetAffection(Teller, OwnerA, 10); // <= 50, not kin

            var plan = ClassifyProbe(pub, sec, lookup);

            Assert.Equal(ProbeResultKind.SecretTell, plan.ResultKind);
            Assert.Equal("secret-tell", plan.ResultKindLabel);
            Assert.Equal(sec.EventId, plan.EventId);
            Assert.NotNull(plan.Offer);
            Assert.Equal(sec.EventId, plan.Offer!.EventId);
        }

        [Fact]
        public void Probe_SecretDeflect_WhenTrustsPlayerButProtectsOwner()
        {
            var pub = CreatePublicFace();
            var sec = CreateHiddenPiece();

            var lookup = new FakeSecretRelationshipLookup();
            lookup.SetAffection(Teller, Player, 60); // trusts player (> 50)
            lookup.SetAffection(Teller, OwnerA, 60); // friend with owner (> 50)

            var plan = ClassifyProbe(pub, sec, lookup);

            Assert.Equal(ProbeResultKind.SecretDeflect, plan.ResultKind);
            Assert.Equal("secret-deflect", plan.ResultKindLabel);
            Assert.Equal(sec.EventId, plan.EventId);
            Assert.Equal("VividWorld_SecretProbe_Deflect", plan.SentenceKey);
            Assert.Null(plan.Offer);
        }

        [Fact]
        public void Probe_Untrusted_WhenAffectionTooLow_FallsBackToPublicFace()
        {
            var pub = CreatePublicFace();
            var sec = CreateHiddenPiece();

            var lookup = new FakeSecretRelationshipLookup();
            lookup.SetAffection(Teller, Player, 20); // <= 50 (untrusted)
            lookup.SetAffection(Teller, OwnerA, 10);

            var plan = ClassifyProbe(pub, sec, lookup);

            // Falls back to row 8 (tell public face)
            Assert.Equal(ProbeResultKind.Tell, plan.ResultKind);
            Assert.Equal(pub.EventId, plan.EventId);
        }

        [Fact]
        public void Probe_ExactLineEquality_DoesNotPassThreshold()
        {
            var pub = CreatePublicFace();
            var sec = CreateHiddenPiece();

            // 1. Non-kin with player: exactly 50 -> untrusted (must be > 50)
            var lookup1 = new FakeSecretRelationshipLookup();
            lookup1.SetAffection(Teller, Player, 50);
            lookup1.SetAffection(Teller, OwnerA, 0);
            var plan1 = ClassifyProbe(pub, sec, lookup1);
            Assert.Equal(ProbeResultKind.Tell, plan1.ResultKind); // fallback to public face

            // 2. Kin with player: exactly 30 -> untrusted (must be > 30)
            var lookup2 = new FakeSecretRelationshipLookup();
            lookup2.SetKin(Teller, Player);
            lookup2.SetAffection(Teller, Player, 30);
            lookup2.SetAffection(Teller, OwnerA, 0);
            var plan2 = ClassifyProbe(pub, sec, lookup2);
            Assert.Equal(ProbeResultKind.Tell, plan2.ResultKind); // fallback to public face

            // 3. With owner: exactly 50 is NOT > 50, so does not protect owner as friend
            var lookup3 = new FakeSecretRelationshipLookup();
            lookup3.SetAffection(Teller, Player, 51); // trusts player (> 50)
            lookup3.SetAffection(Teller, OwnerA, 50); // exactly 50 with owner -> does NOT protect
            var plan3 = ClassifyProbe(pub, sec, lookup3);
            Assert.Equal(ProbeResultKind.SecretTell, plan3.ResultKind); // tells!
        }

        [Fact]
        public void Probe_OwnerAlwaysTells_WhenTrustsPlayer()
        {
            var pub = CreatePublicFace();
            var sec = CreateHiddenPiece(owners: new[] { Teller });

            var lookup = new FakeSecretRelationshipLookup();
            lookup.SetAffection(Teller, Player, 55); // trusts player

            var plan = ClassifyProbe(pub, sec, lookup, tellerId: Teller);

            // Owner never protects against himself
            Assert.Equal(ProbeResultKind.SecretTell, plan.ResultKind);
            Assert.Equal(sec.EventId, plan.EventId);
        }

        [Fact]
        public void Probe_TwoOwners_FriendWithOne_Deflects()
        {
            var pub = CreatePublicFace();
            var sec = CreateHiddenPiece(owners: new[] { OwnerA, OwnerB });

            var lookup = new FakeSecretRelationshipLookup();
            lookup.SetAffection(Teller, Player, 70); // trusts player (> 50)
            lookup.SetAffection(Teller, OwnerA, 60); // friend with OwnerA (> 50)
            lookup.SetAffection(Teller, OwnerB, 0);  // stranger with OwnerB

            var plan = ClassifyProbe(pub, sec, lookup);

            // Friend with at least one owner -> protects owner -> deflect
            Assert.Equal(ProbeResultKind.SecretDeflect, plan.ResultKind);
            Assert.Equal("VividWorld_SecretProbe_Deflect", plan.SentenceKey);
        }

        [Fact]
        public void Probe_RetiredSecret_IgnoredAsNonExistent()
        {
            var pub = CreatePublicFace();
            var sec = CreateHiddenPiece(retired: true);

            var lookup = new FakeSecretRelationshipLookup();
            lookup.SetAffection(Teller, Player, 70); // trusts player
            lookup.SetAffection(Teller, OwnerA, 0);

            var plan = ClassifyProbe(pub, sec, lookup);

            // Retired secret ignored -> falls through to public face
            Assert.Equal(ProbeResultKind.Tell, plan.ResultKind);
            Assert.Equal(pub.EventId, plan.EventId);
        }

        [Fact]
        public void AiRecall_Handover_Own_WhenOwnerAndNotHeardLeaked()
        {
            var sec = CreateHiddenPiece(owners: new[] { Teller }, tellerHop: 0);

            var lookup = new FakeSecretRelationshipLookup();
            lookup.SetAffection(Teller, Player, 60); // trusts player

            var res = NpcRecallQuery.Query(
                Teller,
                Today,
                maxCount: 10,
                candidateEvents: new[] { sec },
                playerHeroId: Player,
                secretLookup: lookup);

            Assert.Single(res.Items);
            var item = res.Items[0];
            Assert.Equal(sec.EventId, item.EventId);
            Assert.Equal(SecretMemoryState.Own, item.SecretState);
            Assert.Equal("VividWorld_SecretMemory_Own", item.SecretSentenceKey);
        }

        [Fact]
        public void AiRecall_Handover_OwnLeaked_WhenOwnerAndHeardLeaked()
        {
            // Owner heard leaked news: hop > 0
            var sec1 = CreateHiddenPiece(owners: new[] { Teller }, tellerHop: 2);

            var lookup = new FakeSecretRelationshipLookup();
            lookup.SetAffection(Teller, Player, 60);

            var res1 = NpcRecallQuery.Query(
                Teller,
                Today,
                maxCount: 10,
                candidateEvents: new[] { sec1 },
                playerHeroId: Player,
                secretLookup: lookup);

            Assert.Single(res1.Items);
            Assert.Equal(SecretMemoryState.OwnLeaked, res1.Items[0].SecretState);
            Assert.Equal("VividWorld_SecretMemory_OwnLeaked", res1.Items[0].SecretSentenceKey);

            // Owner has OwnerReaction set
            var sec2 = CreateHiddenPiece(owners: new[] { Teller }, tellerHop: 0, ownerReaction: "furious");
            var res2 = NpcRecallQuery.Query(
                Teller,
                Today,
                maxCount: 10,
                candidateEvents: new[] { sec2 },
                playerHeroId: Player,
                secretLookup: lookup);

            Assert.Single(res2.Items);
            Assert.Equal(SecretMemoryState.OwnLeaked, res2.Items[0].SecretState);
            Assert.Equal("VividWorld_SecretMemory_OwnLeaked", res2.Items[0].SecretSentenceKey);
        }

        [Fact]
        public void AiRecall_Handover_Confided_WhenNonOwner()
        {
            var sec = CreateHiddenPiece(owners: new[] { OwnerA });

            var lookup = new FakeSecretRelationshipLookup();
            lookup.SetAffection(Teller, Player, 60); // trusts player
            lookup.SetAffection(Teller, OwnerA, 10); // does not protect owner

            var res = NpcRecallQuery.Query(
                Teller,
                Today,
                maxCount: 10,
                candidateEvents: new[] { sec },
                playerHeroId: Player,
                secretLookup: lookup);

            Assert.Single(res.Items);
            var item = res.Items[0];
            Assert.Equal(sec.EventId, item.EventId);
            Assert.Equal(SecretMemoryState.Confided, item.SecretState);
            Assert.Equal("VividWorld_SecretMemory_Confided", item.SecretSentenceKey);
        }

        [Fact]
        public void AiRecall_NotHandedOver_WhenUntrustedOrProtectsOwnerOrRetired()
        {
            var lookup = new FakeSecretRelationshipLookup();
            lookup.SetAffection(Teller, Player, 20); // untrusted
            lookup.SetAffection(Teller, OwnerA, 60); // protects owner

            var secUntrusted = CreateHiddenPiece(id: "s_untrusted", owners: new[] { OwnerB }); // OwnerB aff is 0, but player aff is 20 -> untrusted
            var secProtects = CreateHiddenPiece(id: "s_protects", owners: new[] { OwnerA });
            var secRetired = CreateHiddenPiece(id: "s_retired", retired: true);

            var res = NpcRecallQuery.Query(
                Teller,
                Today,
                maxCount: 10,
                candidateEvents: new[] { secUntrusted, secProtects, secRetired },
                playerHeroId: Player,
                secretLookup: lookup);

            Assert.Empty(res.Items);
            Assert.Equal(3, res.Exclusions.Count);

            var exclUntrusted = res.Exclusions.First(e => e.EventId == "s_untrusted");
            Assert.Equal(RecallExclusionReason.NotVisible, exclUntrusted.Reason);

            var exclProtects = res.Exclusions.First(e => e.EventId == "s_protects");
            Assert.Equal(RecallExclusionReason.NotVisible, exclProtects.Reason);

            var exclRetired = res.Exclusions.First(e => e.EventId == "s_retired");
            Assert.Equal(RecallExclusionReason.RetiredType, exclRetired.Reason);
        }
    }
}
