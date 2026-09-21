#nullable enable

using System;
using System.Globalization;
using System.Text;
using VividWorld.Core.Persistence;

namespace VividWorld.Core.Diagnostics
{
    public static class SnapshotLogFormatter
    {
        public static string Token8(string? token)
        {
            if (string.IsNullOrEmpty(token)) return string.Empty;
            return token!.Length >= 8 ? token.Substring(0, 8) : token;
        }

        public static string FormatTake(SnapshotResult result, string saveName, int maxSnapshots)
        {
            string t8 = Token8(result.Token);
            long kb = result.Bytes / 1024;
            string ms = result.ElapsedMs.ToString("F1", CultureInfo.InvariantCulture);
            // SNAP1：maxSnapshots <= 0 是「不限份數」，印成 "of 0" 會看起來像壞掉。
            string kept = maxSnapshots > 0
                ? $"{result.Kept} of {maxSnapshots} kept"
                : $"{result.Kept} kept (no limit)";
            return $"Snapshot {t8}: {result.Files} file(s) ({result.LinkedFiles} linked, {result.CopiedFiles} copied) / {kb} KB copied in {ms} ms - slot \"{saveName}\", {kept}.";
        }

        public static string FormatHardlinksUnavailable(string token, int errorCode, string errorDescription, int copiedFiles)
        {
            string t8 = Token8(token);
            return $"Snapshot {t8}: hard links unavailable (error {errorCode}: {errorDescription}) - copied {copiedFiles} file(s) instead.";
        }

        public static string FormatHardlinksDisabled(string token, int copiedFiles)
        {
            string t8 = Token8(token);
            return $"Snapshot {t8}: hard links disabled by config - copied {copiedFiles} file(s).";
        }

        public static string GetTakeSkippedReason(SnapshotOutcome outcome)
        {
            switch (outcome)
            {
                case SnapshotOutcome.SaveFailed:
                    return "the save did not complete successfully";
                case SnapshotOutcome.NoToken:
                    return "no token was minted for this save";
                case SnapshotOutcome.NoCampaignFolder:
                    return "the campaign folder does not exist yet";
                default:
                    return outcome.ToString();
            }
        }

        public static string FormatSkippedTake(SnapshotOutcome outcome)
        {
            return $"Snapshot skipped: {GetTakeSkippedReason(outcome)}.";
        }

        public static string FormatSkippedTake(string reason)
        {
            return $"Snapshot skipped: {reason}.";
        }

        public static string GetPruneReason(PruneReason reason, int maxSnapshots = 0)
        {
            switch (reason)
            {
                case PruneReason.SlotOverwritten:
                    return "slot was overwritten by a newer save";
                case PruneReason.OverCap:
                    return $"over the cap of {maxSnapshots}";
                case PruneReason.OrphanFolder:
                    return "orphan folder with no manifest entry";
                default:
                    return reason.ToString();
            }
        }

        public static string FormatPrune(PrunedSnapshot pruned, int maxSnapshots)
        {
            string t8 = Token8(pruned.Token);
            string reason = GetPruneReason(pruned.Reason, maxSnapshots);
            return $"Snapshot prune: dropped {t8} (slot \"{pruned.SaveName}\") - {reason}.";
        }

        public static string FormatRestore(SnapshotResult result)
        {
            string t8 = Token8(result.Token);
            long kb = result.Bytes / 1024;
            string ms = result.ElapsedMs.ToString("F1", CultureInfo.InvariantCulture);
            return $"Snapshot restore {t8}: {result.Files} file(s) / {kb} KB restored in {ms} ms - the event store is back to the state this save was written in.";
        }

        public static string GetRestoreSkippedReason(SnapshotOutcome outcome, string token = "")
        {
            string t8 = Token8(token);
            switch (outcome)
            {
                case SnapshotOutcome.NoToken:
                    return "this save carries no snapshot token (it was saved before M7)";
                case SnapshotOutcome.SnapshotMissing:
                    return $"no snapshot folder for token {t8} - it was pruned, or the folder was deleted by hand";
                case SnapshotOutcome.SnapshotEmpty:
                    return $"the snapshot for token {t8} is empty - live data left untouched";
                case SnapshotOutcome.SnapshotIncomplete:
                    // 這一條的實際理由帶著檔數，由 Restore 填進 SnapshotResult.Reason；
                    // 只有拿不到那份 Reason 時才會落到這句通用說法
                    return $"the snapshot for token {t8} never finished copying - live data left untouched";
                default:
                    return outcome.ToString();
            }
        }

        public static string FormatSkippedRestore(SnapshotOutcome outcome, string token = "")
        {
            return $"Snapshot restore skipped: {GetRestoreSkippedReason(outcome, token)}.";
        }

        public static string FormatSkippedRestore(string reason)
        {
            return $"Snapshot restore skipped: {reason}.";
        }

        public static string FormatFailed(string operation, string token, string message)
        {
            string t8 = Token8(token);
            return $"Snapshot {operation} failed for token {t8}: {message}";
        }

        public static string FormatRollbackVerdict(SnapshotResult? lastRestore)
        {
            if (lastRestore == null)
            {
                return "did not run: this session started a new campaign, not a loaded save";
            }

            if (lastRestore.Outcome == SnapshotOutcome.Restored)
            {
                string t8 = Token8(lastRestore.Token);
                return $"ran for token {t8} and restored {lastRestore.Files} file(s), so these leftovers came from somewhere else - a folder copied by hand?";
            }

            if (lastRestore.Outcome == SnapshotOutcome.Failed)
            {
                return $"failed: {lastRestore.Reason}";
            }

            string reason = !string.IsNullOrEmpty(lastRestore.Reason)
                ? lastRestore.Reason
                : GetRestoreSkippedReason(lastRestore.Outcome, lastRestore.Token);

            return $"did not run: {reason}";
        }

        public static string FormatWorldStatusSnapshots(
            SnapshotInventory inv,
            int maxSnapshots,
            SnapshotResult? lastTake,
            SnapshotResult? lastRestore)
        {
            long kb = inv.TotalBytes / 1024;
            string ms = (lastTake != null && lastTake.Outcome == SnapshotOutcome.Taken)
                ? lastTake.ElapsedMs.ToString("F1", CultureInfo.InvariantCulture)
                : "0.0";

            string verdict;
            if (lastRestore == null)
            {
                verdict = "no revert (new campaign)";
            }
            else if (lastRestore.Outcome == SnapshotOutcome.Restored)
            {
                verdict = $"restored {lastRestore.Files} file(s)";
            }
            else if (lastRestore.Outcome == SnapshotOutcome.Failed)
            {
                verdict = $"failed: {lastRestore.Reason}";
            }
            else
            {
                string reason = !string.IsNullOrEmpty(lastRestore.Reason)
                    ? lastRestore.Reason
                    : GetRestoreSkippedReason(lastRestore.Outcome, lastRestore.Token);
                verdict = $"no revert ({reason})";
            }

            string capText = maxSnapshots > 0 ? $"{inv.Slots.Count}/{maxSnapshots}" : $"{inv.Slots.Count} (no limit)";
            return $"- Snapshots: {capText} kept, {kb} KB on disk, last save {ms} ms, this session {verdict}";
        }

        public static string FormatInventory(
            SnapshotInventory inv,
            string campaignId,
            string? sessionToken,
            SnapshotResult? lastTake,
            SnapshotResult? lastRestore,
            int maxSnapshots)
        {
            var sb = new StringBuilder();
            sb.AppendLine("Snapshots:");
            sb.AppendLine("---------------------------------------------");
            sb.AppendLine($"  campaign folder: {campaignId}  ({inv.LiveFiles} file(s) / {inv.LiveBytes / 1024} KB live)");

            // 只有「這一局是新戰役」才是 null（`OnGameLoaded` 根本沒觸發）。
            // `NoToken` 是**讀了一個 M7 之前存的檔**，兩件事不一樣，不能印成同一句話。
            if (lastRestore == null)
            {
                sb.AppendLine("  this session:    new campaign - nothing to revert");
            }
            else if (lastRestore.Outcome == SnapshotOutcome.Restored)
            {
                string t8 = Token8(lastRestore.Token);
                long kb = lastRestore.Bytes / 1024;
                string ms = lastRestore.ElapsedMs.ToString("F1", CultureInfo.InvariantCulture);
                sb.AppendLine($"  this session:    restored from {t8} - {lastRestore.Files} file(s) / {kb} KB in {ms} ms");
            }
            else if (lastRestore.Outcome == SnapshotOutcome.Failed)
            {
                sb.AppendLine($"  this session:    failed: {lastRestore.Reason}");
            }
            else
            {
                string reason = !string.IsNullOrEmpty(lastRestore.Reason)
                    ? lastRestore.Reason
                    : GetRestoreSkippedReason(lastRestore.Outcome, lastRestore.Token);
                sb.AppendLine($"  this session:    no revert ({reason})");
            }

            string capLine = maxSnapshots > 0
                ? $"{maxSnapshots} slot(s)"
                : "no limit (players prune with the snapshot manager)";
            sb.AppendLine($"  cap:             {capLine}, {inv.Slots.Count} in use, {inv.TotalBytes / 1024} KB on disk");
            sb.AppendLine("  note:            sizes are logical; linked files share their bytes with the live folder");
            if (inv.OrphanFolders != null && inv.OrphanFolders.Count > 0)
            {
                sb.AppendLine($"  orphan folder(s) with no manifest entry: {inv.OrphanFolders.Count}");
            }

            sb.AppendLine("  ---");

            if (inv.Slots == null || inv.Slots.Count == 0)
            {
                sb.AppendLine("  (no snapshots yet - save once)");
            }
            else
            {
                foreach (var slot in inv.Slots)
                {
                    bool isCurrent = !string.IsNullOrEmpty(sessionToken) && (
                        string.Equals(slot.Token, sessionToken, StringComparison.OrdinalIgnoreCase) ||
                        (sessionToken!.Length >= 8 && slot.Token != null && slot.Token.StartsWith(sessionToken!, StringComparison.OrdinalIgnoreCase)));

                    string prefix = isCurrent ? "* " : "  ";
                    string t8 = Token8(slot.Token);
                    string quotedSaveName = $"\"{slot.SaveName}\"";

                    string utcStr;
                    if (DateTime.TryParse(slot.Utc, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal, out var parsedUtc))
                    {
                        utcStr = parsedUtc.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);
                    }
                    else
                    {
                        utcStr = slot.Utc;
                    }

                    string line = $"{prefix}{t8}  {quotedSaveName,-22}  {utcStr}  {slot.Files} file(s)  {slot.Bytes / 1024} KB";
                    if (!slot.ExistsOnDisk)
                    {
                        line += "  <missing on disk>";
                    }
                    sb.AppendLine(line);
                }
            }

            sb.AppendLine("  ---");

            if (lastTake != null && lastTake.Outcome == SnapshotOutcome.Taken)
            {
                string ms = lastTake.ElapsedMs.ToString("F1", CultureInfo.InvariantCulture);
                sb.Append($"  last save: {lastTake.Files} file(s) copied in {ms} ms");
            }
            else
            {
                sb.Append("  last save: (none)");
            }

            return sb.ToString();
        }
    }
}
