using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using VividWorld.Core.Config;

namespace VividWorld.Core.Rumors
{
    /// <summary>量領主之間傳話的日誌與開發者報告。一律英文、不帶樣式，數字用固定文化。</summary>
    public static class TellTierLogFormatter
    {
        private static readonly string[] RelationBinLabels = { "<=-20", "-19..-1", "0", "1..9", "10..29", ">=30" };
        private static readonly string[] TierLabels = { "family", "familiar", "unfamiliar", "hostile" };
        private static readonly string[] WillingnessBinLabels = { "<0", "0..5", "5..10", "10..30", ">=30" };

        /// <summary>每一輪一行：講的人、消息、份量，以及每位聽的人的通道、通道好感、自己的好感、意願、層、機率、結果。</summary>
        public static string FormatTurnLine(
            string tellerId,
            string eventId,
            int weightTen,
            bool isBig,
            IReadOnlyList<ContactObservation> contacts)
        {
            var sb = new StringBuilder();
            sb.Append(string.Format(CultureInfo.InvariantCulture,
                "Tell tiers: {0} told {1} (weight {2}, {3}) -> ",
                tellerId, eventId, weightTen, isBig ? "big" : "small"));

            if (contacts == null || contacts.Count == 0)
            {
                sb.Append("(no contacts)");
                return sb.ToString();
            }

            for (int i = 0; i < contacts.Count; i++)
            {
                var c = contacts[i];
                if (i > 0) sb.Append("; ");
                sb.Append(string.Format(CultureInfo.InvariantCulture,
                    "{0} {1} rel {2} own {3} will {4:0.0} rf {5:0.00} {6} p {7:0.0}% {8}",
                    c.HeroId, c.Kind, FormatSigned(c.Relation), FormatSigned(c.OwnRelation), c.Willingness,
                    c.RelationFactor, TierText(c), c.Chance * 100.0, StatusText(c)));
            }
            return sb.ToString();
        }

        /// <summary>每天一行：這一天所有領主開口的合計。</summary>
        public static string FormatDailyLine(int day, TellTierTally t)
        {
            if (t == null || t.Total == 0)
            {
                return string.Format(CultureInfo.InvariantCulture, "Tell tiers day {0}: no lord spoke", day);
            }

            var sb = new StringBuilder();
            sb.Append(string.Format(CultureInfo.InvariantCulture,
                "Tell tiers day {0}: links {1} (in-person {2}, remote {3})",
                day, t.Total, t.InPerson, t.Remote));
            sb.Append(string.Format(CultureInfo.InvariantCulture,
                " | told {0} (big {1}, small {2}), re-heard {7}, missed {3}, already knew {4}, not eligible {5}, not reached {6}",
                t.ToldTotal, t.ToldBig, t.ToldSmall,
                t.StatusCount(ContactStatus.Missed),
                t.StatusCount(ContactStatus.AlreadyKnows),
                t.StatusCount(ContactStatus.NotEligible),
                t.StatusCount(ContactStatus.NotReached),
                t.StatusCount(ContactStatus.Reheard)));
            sb.Append(" | relation in-person: ").Append(FormatBins(RelationBinLabels, t.RelationInPerson));
            sb.Append(" | relation remote: ").Append(FormatBins(RelationBinLabels, t.RelationRemote));
            sb.Append(" | own relation in-person: ").Append(FormatBins(RelationBinLabels, t.OwnRelationInPerson));
            sb.Append(" | own relation remote: ").Append(FormatBins(RelationBinLabels, t.OwnRelationRemote));
            sb.Append(" | willingness: ").Append(FormatBins(WillingnessBinLabels, t.WillingnessBins));
            sb.Append(" | tiers in-person: ").Append(FormatBins(TierLabels, t.TierInPerson));
            sb.Append(" | tiers remote: ").Append(FormatBins(TierLabels, t.TierRemote));
            sb.Append(" | sign differs ").Append(t.SignDiffers.ToString(CultureInfo.InvariantCulture));
            sb.Append(" | ").Append(FormatHeldBack(t));
            return sb.ToString();
        }

        /// <summary>開發者工具的多行報告：今天到目前為止，與這次讀檔以來，各一段。</summary>
        public static string FormatDevReport(TellTierTally today, TellTierTally session, int day,
            TellTiersConfig cfg, int bigNewsLine)
        {
            var sb = new StringBuilder();
            sb.AppendLine("=== Tell tiers (How do lords pass news to each other?) ===");
            sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                "tiers {0}, hostile own <= {1}, familiar will >= {2}, big news weight >= {3}",
                cfg.Enabled ? "on" : "off",
                cfg.HostileAtOrBelow,
                cfg.FamiliarWillingness.ToString("0.##", CultureInfo.InvariantCulture),
                bigNewsLine));
            sb.AppendLine(string.Format(CultureInfo.InvariantCulture, "Day {0}", day));
            sb.AppendLine();
            AppendSection(sb, "Today so far", today);
            sb.AppendLine();
            AppendSection(sb, "Since this save was loaded (includes today)", session);
            return sb.ToString().TrimEnd();
        }

        private static void AppendSection(StringBuilder sb, string title, TellTierTally t)
        {
            sb.AppendLine("--- " + title + " ---");
            if (t == null || t.Total == 0)
            {
                sb.AppendLine("no lord spoke");
                return;
            }

            sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                "links {0} (in-person {1}, remote {2})", t.Total, t.InPerson, t.Remote));
            sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                "told {0} (big {1}, small {2})", t.ToldTotal, t.ToldBig, t.ToldSmall));
            sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                "re-heard {0}", t.StatusCount(ContactStatus.Reheard)));
            sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                "missed {0}", t.StatusCount(ContactStatus.Missed)));
            sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                "already knew {0}", t.StatusCount(ContactStatus.AlreadyKnows)));
            sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                "not eligible {0}", t.StatusCount(ContactStatus.NotEligible)));
            sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                "not reached {0}", t.StatusCount(ContactStatus.NotReached)));
            sb.AppendLine("relation in-person: " + FormatBins(RelationBinLabels, t.RelationInPerson));
            sb.AppendLine("relation remote: " + FormatBins(RelationBinLabels, t.RelationRemote));
            sb.AppendLine("own relation in-person: " + FormatBins(RelationBinLabels, t.OwnRelationInPerson));
            sb.AppendLine("own relation remote: " + FormatBins(RelationBinLabels, t.OwnRelationRemote));
            sb.AppendLine("willingness: " + FormatBins(WillingnessBinLabels, t.WillingnessBins));
            sb.AppendLine("tiers in-person: " + FormatBins(TierLabels, t.TierInPerson));
            sb.AppendLine("tiers remote: " + FormatBins(TierLabels, t.TierRemote));
            sb.AppendLine("sign differs " + t.SignDiffers.ToString(CultureInfo.InvariantCulture));
            sb.AppendLine(FormatHeldBack(t));
        }

        private static string FormatHeldBack(TellTierTally t)
        {
            int hostile = t.HeldBackHostileInPerson + t.HeldBackHostileRemote;
            int small = t.HeldBackSmallNewsInPerson + t.HeldBackSmallNewsRemote;
            int shameful = t.HeldBackShamefulInPerson + t.HeldBackShamefulRemote;
            return string.Format(CultureInfo.InvariantCulture,
                "held back: hostile {0} (in-person {1}, remote {2}), small news {3} (in-person {4}, remote {5}), shameful {6} (in-person {7}, remote {8})",
                hostile, t.HeldBackHostileInPerson, t.HeldBackHostileRemote,
                small, t.HeldBackSmallNewsInPerson, t.HeldBackSmallNewsRemote,
                shameful, t.HeldBackShamefulInPerson, t.HeldBackShamefulRemote);
        }

        private static string TierText(ContactObservation c)
        {
            if (c.IsFamily) return "family";
            switch (c.Tier)
            {
                case TellTier.Hostile: return "hostile";
                case TellTier.Unfamiliar: return "unfamiliar";
                default: return "familiar";
            }
        }

        private static string FormatBins(string[] labels, IReadOnlyList<int> counts)
        {
            var parts = new List<string>(labels.Length);
            for (int i = 0; i < labels.Length; i++)
            {
                parts.Add(string.Format(CultureInfo.InvariantCulture, "{0} {1}", labels[i], counts[i]));
            }
            return string.Join(", ", parts);
        }

        private static string FormatSigned(int value)
        {
            if (value > 0) return "+" + value.ToString(CultureInfo.InvariantCulture);
            return value.ToString(CultureInfo.InvariantCulture);
        }

        private static string StatusText(ContactObservation c)
        {
            switch (c.Status)
            {
                case ContactStatus.Told: return "told";
                case ContactStatus.Missed: return "missed";
                case ContactStatus.AlreadyKnows: return "already knows";
                case ContactStatus.NotEligible: return "not eligible";
                case ContactStatus.NotReached: return "not reached";
                case ContactStatus.Reheard: return "re-heard";
                case ContactStatus.HeldBackHostile: return "held back (hostile)";
                case ContactStatus.HeldBackSmallNews: return "held back (small news)";
                case ContactStatus.HeldBackShameful:
                    return !string.IsNullOrEmpty(c.HeldBackReason)
                        ? "held back (" + c.HeldBackReason + ")"
                        : "held back (shameful)";
                default: return c.Status.ToString();
            }
        }

        private static string StatusText(ContactStatus status)
        {
            switch (status)
            {
                case ContactStatus.Told: return "told";
                case ContactStatus.Missed: return "missed";
                case ContactStatus.AlreadyKnows: return "already knows";
                case ContactStatus.NotEligible: return "not eligible";
                case ContactStatus.NotReached: return "not reached";
                case ContactStatus.Reheard: return "re-heard";
                case ContactStatus.HeldBackHostile: return "held back (hostile)";
                case ContactStatus.HeldBackSmallNews: return "held back (small news)";
                case ContactStatus.HeldBackShameful: return "held back (shameful)";
                default: return status.ToString();
            }
        }
    }
}
