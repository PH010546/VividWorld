using System;
using System.Linq;
using VividWorld.Core.Config;
using VividWorld.Core.Events;
using VividWorld.Core.Util;

namespace VividWorld.Core.Rumors
{
    /// <summary>信不信的結果裡，佔最重的那一項因素。平手時照這個列舉的宣告順序取第一個（Witness 不參與比較）。</summary>
    public enum BeliefReason
    {
        SubjectRelation,
        TellerRelation,
        TraitFit,
        ListenerNature,
        Distance,
        None,
        Witness,
        KnowsTruth,
        Response
    }

    public sealed class BeliefInputs
    {
        public long CampaignSeed;
        public string EventId = string.Empty;
        public string HearerId = string.Empty;
        public string SubjectId = string.Empty;          // 被說的人（opinion 第一條的 about 綁到的人）
        public string TellerId = string.Empty;           // 告訴他的人；第 0 手為空字串
        public int Hop;
        public bool HearerIsParticipant;
        public bool KnowsTruth;
        public string? KnowsTruthReason;
        public double ResponsePenalty;

        /// <summary>聽者對被說的人的個人好感；查不到為 null。</summary>
        public int? SubjectAffection;

        /// <summary>聽者對告訴他的人的個人好感；查不到為 null。</summary>
        public int? TellerAffection;

        /// <summary>opinion 的 trait 欄位（已小寫）；沒寫為 null。只給日誌用。</summary>
        public string? Trait;

        /// <summary>被說的人在 trait 那一項的等級；trait 沒寫或查不到為 null。</summary>
        public int? SubjectTraitLevel;

        /// <summary>這條 opinion 的量（負＝壞事、正＝好事）。</summary>
        public double OpinionAmount;

        /// <summary>聽者的理性等級（-2～2）。</summary>
        public int ListenerCalculating;

        /// <summary>第幾次判；從 0 起算。</summary>
        public int Round;
    }

    public sealed class BeliefResult
    {
        public bool Believes;

        /// <summary>false = 一律信（當事人或第 0 手），沒有擲骰，下面的機率與各項都不適用。</summary>
        public bool Rolled;

        public double Base;
        public double SubjectRelation;
        public double TellerRelation;
        public double TraitFit;
        public double ListenerNature;
        public double Distance;
        public double Response;

        /// <summary>夾限前的合計。</summary>
        public double RawChance;

        /// <summary>夾限後的信的機率（百分點）。</summary>
        public double Chance;

        /// <summary>擲出的值（0～100，小於機率就是信）。</summary>
        public double Roll;

        public BeliefReason Heaviest = BeliefReason.None;
    }

    public static class BeliefJudge
    {
        public const string LegacyReason = "Legacy";

        /// <summary>
        /// 更新前就因為這則消息結算過好感的人：沒判過信不信、但已經有來自傳聞的結算記錄。
        /// </summary>
        public static bool HasLegacySettlement(KnownByEntry entry)
        {
            return entry != null && entry.Believes == null && entry.RelationImpacts != null &&
                   entry.RelationImpacts.Any(r => r != null && r.Source == GrudgeSource.Rumor);
        }

        /// <summary>
        /// 這一筆的好感是不是更新前就算過的：之後每次結算都照更新前的算法（個性與對承受者關係兩個倍數當 1），
        /// 不然重聽時新倍數會把先前記下的量退回一部分。
        /// 信不信的判定關掉時，更新後算的與更新前算的分不出來，所以只認已經標成 Legacy 的那一筆，不去猜。
        /// </summary>
        public static bool IsSettledBeforeBelief(KnownByEntry entry, bool beliefEnabled)
        {
            if (entry == null) return false;
            if (string.Equals(entry.BeliefReason, LegacyReason, StringComparison.Ordinal)) return true;
            return beliefEnabled && HasLegacySettlement(entry);
        }

        public static BeliefResult Judge(BeliefInputs input, BeliefConfig cfg, IDeterministicRng rng)
        {
            if (input == null) throw new ArgumentNullException(nameof(input));
            if (cfg == null) throw new ArgumentNullException(nameof(cfg));
            if (rng == null) throw new ArgumentNullException(nameof(rng));

            if (input.KnowsTruth)
            {
                return new BeliefResult { Believes = false, Rolled = false, Heaviest = BeliefReason.KnowsTruth };
            }

            if (input.HearerIsParticipant || input.Hop <= 0)
            {
                return new BeliefResult { Believes = true, Rolled = false, Heaviest = BeliefReason.Witness };
            }

            var r = new BeliefResult { Rolled = true, Base = cfg.BaseChance };
            r.SubjectRelation = SubjectTerm(input.SubjectAffection, cfg, input.OpinionAmount);
            r.TellerRelation = TellerTerm(input.TellerAffection, cfg);
            r.TraitFit = FitTerm(input.OpinionAmount, input.SubjectTraitLevel, cfg);
            r.ListenerNature = input.ListenerCalculating * cfg.ListenerCalculatingStep;
            r.Distance = DistanceTerm(input.Hop, cfg);
            r.Response = input.ResponsePenalty;

            r.RawChance = r.Base + r.SubjectRelation + r.TellerRelation + r.TraitFit + r.ListenerNature + r.Distance + r.Response;
            r.Chance = Math.Max(cfg.MinChance, Math.Min(cfg.MaxChance, r.RawChance));

            long seed = RumorSeed.Of(input.CampaignSeed, input.EventId, input.HearerId, "belief", input.Round);
            r.Roll = rng.NextDouble(seed) * 100.0;
            r.Believes = rng.Chance(r.Chance / 100.0, seed);
            r.Heaviest = HeaviestOf(r);
            return r;
        }

        public static double SubjectTerm(int? affection, BeliefConfig cfg) => SubjectTerm(affection, cfg, 0);

        public static double SubjectTerm(int? affection, BeliefConfig cfg, double opinionAmount)
        {
            if (affection == null) return 0;
            int a = affection.Value;
            double sign = opinionAmount > 0 ? -1.0 : 1.0;
            if (a >= cfg.SubjectRelationFriend) return sign * cfg.SubjectFriendDelta;
            if (a >= cfg.SubjectRelationWarm) return sign * cfg.SubjectWarmDelta;
            if (a <= cfg.SubjectRelationHostile) return sign * cfg.SubjectHostileDelta;
            return 0;
        }

        public static double TellerTerm(int? affection, BeliefConfig cfg)
        {
            if (affection == null) return 0;
            int a = affection.Value;
            if (a >= cfg.TellerRelationTrusted) return cfg.TellerTrustedDelta;
            if (a <= cfg.TellerRelationDistrusted) return cfg.TellerDistrustedDelta;
            return 0;
        }

        /// <summary>壞事（量為負）配被說的人那一項為負、或好事（量為正）配為正 = 對得上；異號 = 相反；等級 0 或查不到 = 0。</summary>
        public static double FitTerm(double amount, int? level, BeliefConfig cfg)
        {
            if (level == null || level.Value == 0 || amount == 0) return 0;
            bool sameSign = (amount < 0) == (level.Value < 0);
            return sameSign ? cfg.FitsTraitDelta : cfg.ContradictsTraitDelta;
        }

        public static double DistanceTerm(int hop, BeliefConfig cfg)
        {
            var deltas = cfg.HopDeltas;
            if (deltas == null || deltas.Length == 0 || hop < 0) return 0;
            return deltas[Math.Min(hop, deltas.Length - 1)];
        }

        /// <summary>絕對值最大的那一項；全部是 0 回傳 None；平手取列舉裡排前面的。</summary>
        public static BeliefReason HeaviestOf(BeliefResult r)
        {
            var best = BeliefReason.None;
            double bestAbs = 0;
            void Consider(BeliefReason reason, double value)
            {
                if (r.Believes && value <= 0) return;
                if (!r.Believes && value >= 0) return;

                double abs = Math.Abs(value);
                if (abs > bestAbs)
                {
                    bestAbs = abs;
                    best = reason;
                }
            }
            Consider(BeliefReason.SubjectRelation, r.SubjectRelation);
            Consider(BeliefReason.TellerRelation, r.TellerRelation);
            Consider(BeliefReason.TraitFit, r.TraitFit);
            Consider(BeliefReason.ListenerNature, r.ListenerNature);
            Consider(BeliefReason.Distance, r.Distance);
            Consider(BeliefReason.Response, r.Response);
            return best;
        }
    }
}
