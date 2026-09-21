using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using VividWorld.Core.Config;

namespace VividWorld.Core.Memory
{
    public sealed class InterestHeroFacts
    {
        public string HeroId = "";
        public string? ClanId, FatherId, MotherId, SpouseId;
        public IReadOnlyList<string> SiblingIds = Array.Empty<string>();

        public InterestHeroFacts() { }

        public InterestHeroFacts(string heroId, string? clanId = null, string? fatherId = null, string? motherId = null, string? spouseId = null, IReadOnlyList<string>? siblingIds = null)
        {
            HeroId = heroId ?? "";
            ClanId = clanId;
            FatherId = fatherId;
            MotherId = motherId;
            SpouseId = spouseId;
            SiblingIds = siblingIds ?? Array.Empty<string>();
        }
    }

    public sealed class InterestParticipant
    {
        public string Role = "", HeroId = "";
        public InterestHeroFacts? Facts;         // 查不到為 null
        public int? PersonalRelation;            // 聽者對這位當事人的 GetBaseHeroRelation；查不到為 null

        public InterestParticipant() { }

        public InterestParticipant(string role, string heroId, InterestHeroFacts? facts = null, int? personalRelation = null)
        {
            Role = role ?? "";
            HeroId = heroId ?? "";
            Facts = facts;
            PersonalRelation = personalRelation;
        }
    }

    public sealed class InterestParticipantDetail
    {
        public string Role { get; set; } = "";
        public string HeroId { get; set; } = "";
        public double Value { get; set; }
        public string Source { get; set; } = "";
        public int? PersonalRelation { get; set; }
    }

    public sealed class InterestResult
    {
        public double Interest { get; set; }
        public string BestRole { get; set; } = "";
        public string BestHeroId { get; set; } = "";
        public string BestSource { get; set; } = "";
        public int? BestRelation { get; set; }
        public string InterestSource { get; set; } = "";
        public IReadOnlyList<InterestParticipantDetail> ParticipantDetails { get; set; } = Array.Empty<InterestParticipantDetail>();
        public bool KnowerFound { get; set; }
    }

    public static class InterestCalculator
    {
        public static InterestResult Compute(
            InterestHeroFacts? knower,
            IReadOnlyList<InterestParticipant>? participants,
            MemoryConfig cfg)
        {
            return Compute(knower, knower?.HeroId ?? "", participants, cfg);
        }

        /// <summary>drama 未知的既有呼叫點：陌生人保底維持 <c>interestFloors.other</c>（行為與 MF1 相同）。</summary>
        public static InterestResult Compute(
            InterestHeroFacts? knower,
            string knowerId,
            IReadOnlyList<InterestParticipant>? participants,
            MemoryConfig cfg)
        {
            return ComputeCore(knower, knowerId, participants, cfg.InterestFloors.Other, "other floor", cfg);
        }

        public static InterestResult Compute(
            InterestHeroFacts? knower,
            IReadOnlyList<InterestParticipant>? participants,
            int drama,
            MemoryConfig cfg)
        {
            return Compute(knower, knower?.HeroId ?? "", participants, drama, cfg);
        }

        /// <summary>【v3.21，MF1b】陌生人保底依事件大小提高：max(other, otherByDrama[drama−1])，規格 §6.8.1。</summary>
        public static InterestResult Compute(
            InterestHeroFacts? knower,
            string knowerId,
            IReadOnlyList<InterestParticipant>? participants,
            int drama,
            MemoryConfig cfg)
        {
            int d = Math.Max(1, Math.Min(5, drama));
            var otherByDrama = cfg.InterestFloors.OtherByDrama;
            double dramaFloorVal = (otherByDrama != null && otherByDrama.Length >= d) ? otherByDrama[d - 1] : cfg.InterestFloors.Other;

            // 嚴格大於 other 時才標 drama floor（規格 §6.8.1）
            double strangerFloor = Math.Max(cfg.InterestFloors.Other, dramaFloorVal);
            string strangerFloorName = dramaFloorVal > cfg.InterestFloors.Other ? "drama floor" : "other floor";

            return ComputeCore(knower, knowerId, participants, strangerFloor, strangerFloorName, cfg);
        }

        private static InterestResult ComputeCore(
            InterestHeroFacts? knower,
            string knowerId,
            IReadOnlyList<InterestParticipant>? participants,
            double strangerFloor,
            string strangerFloorName,
            MemoryConfig cfg)
        {
            bool knowerFound = knower != null;
            if (participants == null || participants.Count == 0)
            {
                return new InterestResult
                {
                    Interest = strangerFloor,
                    BestRole = "",
                    BestHeroId = "",
                    BestSource = strangerFloorName,
                    BestRelation = null,
                    InterestSource = string.Format(CultureInfo.InvariantCulture, "{0} (no participants)", strangerFloorName),
                    ParticipantDetails = Array.Empty<InterestParticipantDetail>(),
                    KnowerFound = knowerFound
                };
            }

            // 依 Role 序數排序後逐一
            var sorted = participants.OrderBy(p => p.Role, StringComparer.Ordinal).ToList();
            var details = new List<InterestParticipantDetail>(sorted.Count);
            InterestParticipantDetail? best = null;

            int relFullAt = Math.Max(1, cfg.RelationFullAt);

            foreach (var p in sorted)
            {
                // 查不到聽者 ⇒ 一律陌生人保底（規格 §6.8.1）
                double relTerm = knowerFound && p.PersonalRelation.HasValue
                    ? Math.Min(1.0, (double)Math.Abs(p.PersonalRelation.Value) / relFullAt)
                    : 0.0;

                double floor;
                string floorName;

                if (knowerFound && string.Equals(knowerId, p.HeroId, StringComparison.Ordinal))
                {
                    floor = cfg.InterestFloors.Participant;
                    floorName = "participant floor";
                }
                else if (knower != null && p.Facts != null && IsKin(knower, knowerId, p.Facts, p.HeroId))
                {
                    floor = cfg.InterestFloors.Kin;
                    floorName = "kin floor";
                }
                else if (knower?.ClanId != null && p.Facts?.ClanId != null && string.Equals(knower.ClanId, p.Facts.ClanId, StringComparison.Ordinal))
                {
                    floor = cfg.InterestFloors.SameClan;
                    floorName = "sameClan floor";
                }
                else
                {
                    floor = strangerFloor;
                    floorName = strangerFloorName;
                }

                double val;
                string src;
                bool relWon = knowerFound && relTerm >= floor && p.PersonalRelation.HasValue;
                if (relWon)
                {
                    val = relTerm;
                    int r = p.PersonalRelation!.Value;
                    src = string.Format(CultureInfo.InvariantCulture, "relation {0:+0;-0;0}", r);
                }
                else
                {
                    val = floor;
                    src = floorName;
                }

                var detail = new InterestParticipantDetail
                {
                    Role = p.Role,
                    HeroId = p.HeroId,
                    Value = val,
                    Source = src,
                    PersonalRelation = p.PersonalRelation
                };
                details.Add(detail);

                // 最大值；同分取排序在前者
                if (best == null || val > best.Value)
                {
                    best = detail;
                }
            }

            string interestSourceStr;
            if (best!.Source.StartsWith("relation", StringComparison.Ordinal))
            {
                int r = best.PersonalRelation!.Value;
                interestSourceStr = string.Format(CultureInfo.InvariantCulture, "relation {0:+0;-0;0} with {1}", r, best.HeroId);
            }
            else
            {
                interestSourceStr = string.Format(CultureInfo.InvariantCulture, "{0} via {1}", best.Source, best.HeroId);
            }

            return new InterestResult
            {
                Interest = best.Value,
                BestRole = best.Role,
                BestHeroId = best.HeroId,
                BestSource = best.Source,
                BestRelation = best.PersonalRelation,
                InterestSource = interestSourceStr,
                ParticipantDetails = details,
                KnowerFound = knowerFound
            };
        }

        private static bool IsKin(InterestHeroFacts knower, string knowerId, InterestHeroFacts pFacts, string pHeroId)
        {
            if (!string.IsNullOrEmpty(pHeroId))
            {
                if (string.Equals(knower.FatherId, pHeroId, StringComparison.Ordinal)) return true;
                if (string.Equals(knower.MotherId, pHeroId, StringComparison.Ordinal)) return true;
                if (string.Equals(knower.SpouseId, pHeroId, StringComparison.Ordinal)) return true;
                if (knower.SiblingIds != null && knower.SiblingIds.Contains(pHeroId)) return true;
            }

            if (!string.IsNullOrEmpty(knowerId))
            {
                if (string.Equals(pFacts.FatherId, knowerId, StringComparison.Ordinal)) return true;
                if (string.Equals(pFacts.MotherId, knowerId, StringComparison.Ordinal)) return true;
                if (string.Equals(pFacts.SpouseId, knowerId, StringComparison.Ordinal)) return true;
                if (pFacts.SiblingIds != null && pFacts.SiblingIds.Contains(knowerId)) return true;
            }

            return false;
        }
    }
}
