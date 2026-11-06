using System.Collections.Generic;
using VividWorld.Core.Secrets;
using Xunit;

namespace VividWorld.Core.Tests.Secrets
{
    public class PrisonVisitMeasureTests
    {
        [Fact]
        public void Boundary_RelationExactly30_DoesNotCountAsB()
        {
            var lordAt30 = new PrisonVisitLordFact
            {
                HeroId = "lord_1",
                IsCaptorSide = true,
                IsKin = true,
                Relation = 30
            };

            var lordAt31 = new PrisonVisitLordFact
            {
                HeroId = "lord_2",
                IsCaptorSide = true,
                IsKin = true,
                Relation = 31
            };

            Assert.True(lordAt30.CountsA);
            Assert.False(lordAt30.CountsB);
            Assert.False(lordAt30.CountsC);

            Assert.True(lordAt31.CountsA);
            Assert.True(lordAt31.CountsB);
            Assert.False(lordAt31.CountsC);
        }

        [Fact]
        public void Boundary_RelationExactly50_DoesNotCountAsC()
        {
            var lordAt50 = new PrisonVisitLordFact
            {
                HeroId = "lord_1",
                IsCaptorSide = true,
                IsKin = false,
                Relation = 50
            };

            var lordAt51 = new PrisonVisitLordFact
            {
                HeroId = "lord_2",
                IsCaptorSide = true,
                IsKin = false,
                Relation = 51
            };

            Assert.False(lordAt50.CountsA);
            Assert.False(lordAt50.CountsB);
            Assert.False(lordAt50.CountsC);

            Assert.False(lordAt51.CountsA);
            Assert.False(lordAt51.CountsB);
            Assert.True(lordAt51.CountsC);
        }

        [Fact]
        public void CaptorSide_Vs_OtherFaction_OnlyCaptorSideCountsForABC()
        {
            var otherFactionKin = new PrisonVisitLordFact
            {
                HeroId = "other_kin",
                IsCaptorSide = false,
                IsKin = true,
                Relation = 80
            };

            var otherFactionFriend = new PrisonVisitLordFact
            {
                HeroId = "other_friend",
                IsCaptorSide = false,
                IsKin = false,
                Relation = 80
            };

            var captorSideKin = new PrisonVisitLordFact
            {
                HeroId = "captor_kin",
                IsCaptorSide = true,
                IsKin = true,
                Relation = 80
            };

            // Other faction lords do not count towards A, B, or C
            Assert.False(otherFactionKin.CountsA);
            Assert.False(otherFactionKin.CountsB);
            Assert.False(otherFactionKin.CountsC);
            Assert.True(otherFactionKin.CountsAnyKinOrFriend);

            Assert.False(otherFactionFriend.CountsA);
            Assert.False(otherFactionFriend.CountsB);
            Assert.False(otherFactionFriend.CountsC);
            Assert.True(otherFactionFriend.CountsAnyKinOrFriend);

            // Captor side kin counts towards A and B
            Assert.True(captorSideKin.CountsA);
            Assert.True(captorSideKin.CountsB);
            Assert.False(captorSideKin.CountsC);
            Assert.True(captorSideKin.CountsAnyKinOrFriend);
        }

        [Fact]
        public void KinAndFriend_CountsInAAndB_NotC()
        {
            // A lord who is both kin and has friend-level relation (> 50)
            var kinFriend = new PrisonVisitLordFact
            {
                HeroId = "kin_friend",
                IsCaptorSide = true,
                IsKin = true,
                Relation = 60
            };

            Assert.True(kinFriend.CountsA);
            Assert.True(kinFriend.CountsB);
            Assert.False(kinFriend.CountsC);
        }

        [Fact]
        public void EmptyTown_NoLords_ReturnsZeroForABC()
        {
            var prisoner = new TownPrisonerFact
            {
                PrisonerHeroId = "prisoner_1",
                PrisonerName = "Prisoner One",
                SettlementId = "town_1",
                SettlementName = "Town One",
                LordsInTown = new List<PrisonVisitLordFact>()
            };

            Assert.False(prisoner.HasA);
            Assert.False(prisoner.HasB);
            Assert.False(prisoner.HasC);
            Assert.False(prisoner.HasBOrC);
            Assert.False(prisoner.HasAnyFactionKinOrFriend);

            var tally = PrisonVisitMeasure.ComputeTally(1, 0, new[] { prisoner });
            Assert.Equal(1, tally.TotalLordPrisoners);
            Assert.Equal(1, tally.HeldInSettlement);
            Assert.Equal(0, tally.HeldInParty);
            Assert.Equal(0, tally.CountA);
            Assert.Equal(0, tally.CountB);
            Assert.Equal(0, tally.CountC);
            Assert.Equal(0, tally.CountBOrC);
            Assert.Equal(0, tally.CountAnyFactionKinOrFriend);
        }

        [Fact]
        public void Tally_AggregatesPrisonersCorrectly_WithControlGroup()
        {
            var p1 = new TownPrisonerFact
            {
                PrisonerHeroId = "p1",
                LordsInTown = new List<PrisonVisitLordFact>
                {
                    new PrisonVisitLordFact { HeroId = "l1", IsCaptorSide = true, IsKin = true, Relation = 40 }
                }
            }; // Has A, B (BOrC=true), Any=true

            var p2 = new TownPrisonerFact
            {
                PrisonerHeroId = "p2",
                LordsInTown = new List<PrisonVisitLordFact>
                {
                    new PrisonVisitLordFact { HeroId = "l2", IsCaptorSide = true, IsKin = false, Relation = 55 }
                }
            }; // Has C (BOrC=true), Any=true

            var p3 = new TownPrisonerFact
            {
                PrisonerHeroId = "p3",
                LordsInTown = new List<PrisonVisitLordFact>
                {
                    new PrisonVisitLordFact { HeroId = "l3", IsCaptorSide = false, IsKin = false, Relation = 60 }
                }
            }; // Neither A, B, C; but Any=true (control group)

            var p4 = new TownPrisonerFact
            {
                PrisonerHeroId = "p4",
                LordsInTown = new List<PrisonVisitLordFact>
                {
                    new PrisonVisitLordFact { HeroId = "l4", IsCaptorSide = true, IsKin = false, Relation = 20 }
                }
            }; // None of A, B, C; Any=false

            var tally = PrisonVisitMeasure.ComputeTally(6, 2, new[] { p1, p2, p3, p4 });

            Assert.Equal(6, tally.TotalLordPrisoners);
            Assert.Equal(4, tally.HeldInSettlement);
            Assert.Equal(2, tally.HeldInParty);
            Assert.Equal(1, tally.CountA);
            Assert.Equal(1, tally.CountB);
            Assert.Equal(1, tally.CountC);
            Assert.Equal(2, tally.CountBOrC);
            Assert.Equal(3, tally.CountAnyFactionKinOrFriend);
        }

        [Fact]
        public void DescribeLord_ExplainsReasonsAccurately()
        {
            var ab = new PrisonVisitLordFact { IsCaptorSide = true, IsKin = true, Relation = 35 };
            Assert.Equal("A, B", PrisonVisitMeasure.DescribeLord(ab));

            var aOnly = new PrisonVisitLordFact { IsCaptorSide = true, IsKin = true, Relation = 20 };
            Assert.Equal("A", PrisonVisitMeasure.DescribeLord(aOnly));

            var c = new PrisonVisitLordFact { IsCaptorSide = true, IsKin = false, Relation = 55 };
            Assert.Equal("C", PrisonVisitMeasure.DescribeLord(c));

            var notCaptorKin = new PrisonVisitLordFact { IsCaptorSide = false, IsKin = true, Relation = 20 };
            Assert.Contains("not captor side: is kin", PrisonVisitMeasure.DescribeLord(notCaptorKin));

            var notCaptorFriend = new PrisonVisitLordFact { IsCaptorSide = false, IsKin = false, Relation = 60 };
            Assert.Contains("not captor side: friend", PrisonVisitMeasure.DescribeLord(notCaptorFriend));

            var notCaptorNone = new PrisonVisitLordFact { IsCaptorSide = false, IsKin = false, Relation = 20 };
            Assert.Contains("not captor side, not kin, relation 20 <= 50", PrisonVisitMeasure.DescribeLord(notCaptorNone));

            var captorNone = new PrisonVisitLordFact { IsCaptorSide = true, IsKin = false, Relation = 20 };
            Assert.Equal("none (not kin, relation 20 <= 50)", PrisonVisitMeasure.DescribeLord(captorNone));
        }
    }
}
