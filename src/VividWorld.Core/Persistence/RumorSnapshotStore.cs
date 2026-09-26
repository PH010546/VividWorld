#nullable enable

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using VividWorld.Core.Diagnostics;

namespace VividWorld.Core.Persistence
{
    public enum SnapshotOutcome
    {
        Taken,             // 照片拍成功
        Restored,          // 還原成功
        NoToken,           // 沒有 token（M7 之前存的檔，或這一局是新戰役）
        NoCampaignFolder,  // campaign_<id>\ 還不存在（第一次存檔之前）
        SaveFailed,        // OnSaveOver 的 isSuccessful = false
        SnapshotMissing,   // token 指不到資料夾（被剪掉了，或被手動刪了）
        SnapshotEmpty,     // 資料夾在但是空的 ⇒ 現場一個位元組都不動
        SnapshotIncomplete,// 資料夾在、有東西，但那次複製沒跑完（manifest 沒列到它，或檔數對不上）⇒ 現場一個位元組都不動
        Failed             // 例外。Reason 帶例外訊息
    }

    public sealed class SnapshotResult
    {
        public SnapshotOutcome Outcome { get; set; }
        public string Token { get; set; } = string.Empty;      // 完整 32 碼；log 只印前 8 碼
        public string Reason { get; set; } = string.Empty;     // Taken／Restored 時是空字串
        public int Files { get; set; }
        public int LinkedFiles { get; set; }
        public int CopiedFiles { get; set; }
        public long Bytes { get; set; }
        public double ElapsedMs { get; set; }
        public int Kept { get; set; }                          // manifest 裡剩幾個 slot（只有 Take 會填）
        public IReadOnlyList<PrunedSnapshot> Pruned { get; set; } = Array.Empty<PrunedSnapshot>();
        public string? HardlinkNote { get; set; }
    }

    public sealed class PrunedSnapshot
    {
        public string Token { get; set; } = string.Empty;
        public string SaveName { get; set; } = string.Empty;
        public PruneReason Reason { get; set; }
    }

    public enum PruneReason
    {
        SlotOverwritten,
        OverCap,
        OrphanFolder
    }

    public sealed class SnapshotInventory
    {
        public bool FolderExists { get; set; }
        public IReadOnlyList<SnapshotSlot> Slots { get; set; } = Array.Empty<SnapshotSlot>();  // 依 Utc 新到舊
        public IReadOnlyList<string> OrphanFolders { get; set; } = Array.Empty<string>();
        public long TotalBytes { get; set; }        // _snapshots\ 整棵樹的位元組
        public int LiveFiles { get; set; }          // 現場（排除 _snapshots\）的檔數
        public long LiveBytes { get; set; }
    }

    public sealed class SnapshotSlot
    {
        public string SaveName { get; set; } = string.Empty;
        public string Token { get; set; } = string.Empty;
        public string Utc { get; set; } = string.Empty;
        public int Files { get; set; }
        public long Bytes { get; set; }
        public bool ExistsOnDisk { get; set; }      // manifest 有列但資料夾被手動刪掉 ⇒ false
    }

    /// <summary>SNAP1：玩家在遊戲裡刪快照的結果。刪不掉是回報，不是例外。</summary>
    public sealed class SnapshotDeleteResult
    {
        public IReadOnlyList<DeletedSnapshot> Deleted { get; set; } = Array.Empty<DeletedSnapshot>();
        public IReadOnlyList<FailedDelete> Failed { get; set; } = Array.Empty<FailedDelete>();
        public long BytesFreed { get; set; }
        public int RemainingSlots { get; set; }
    }

    public sealed class DeletedSnapshot
    {
        public string Token { get; set; } = string.Empty;
        public string SaveName { get; set; } = string.Empty;
        public long Bytes { get; set; }
    }

    public sealed class FailedDelete
    {
        public string Token { get; set; } = string.Empty;
        public string SaveName { get; set; } = string.Empty;
        public string Reason { get; set; } = string.Empty;
    }

    public static class RumorSnapshotStore
    {
        public const string SnapshotsFolderName = "_snapshots";
        public const string ManifestFileName    = "_manifest.json";

        public static SnapshotResult Take(string campaignRoot, string token, string saveName, int maxSnapshots)
            => Take(campaignRoot, token, saveName, maxSnapshots, hardlink: true);

        public static SnapshotResult Take(string campaignRoot, string token, string saveName, int maxSnapshots, bool hardlink)
        {
            var sw = Stopwatch.StartNew();

            if (string.IsNullOrEmpty(token))
            {
                sw.Stop();
                return new SnapshotResult
                {
                    Outcome = SnapshotOutcome.NoToken,
                    Token = string.Empty,
                    Reason = "no token was minted for this save",
                    ElapsedMs = sw.Elapsed.TotalMilliseconds
                };
            }

            // 路徑空字串與資料夾不存在是同一個結論，但理由必須是「沒有資料夾」而不是「沒有 token」，
            // 否則 log 會把呼叫端的路徑問題說成存檔的問題
            if (string.IsNullOrEmpty(campaignRoot) || !Directory.Exists(campaignRoot))
            {
                sw.Stop();
                return new SnapshotResult
                {
                    Outcome = SnapshotOutcome.NoCampaignFolder,
                    Token = token,
                    Reason = "the campaign folder does not exist yet",
                    ElapsedMs = sw.Elapsed.TotalMilliseconds
                };
            }

            try
            {
                // SNAP1：maxSnapshots <= 0 ⇒ 不限份數，跳過超量剪除。
                // 同名覆蓋（步驟 1）與孤兒清理（步驟 4）不受影響——那兩個刪的不是玩家還要的東西。
                bool unlimited = maxSnapshots <= 0;
                int effectiveMax = unlimited ? int.MaxValue : maxSnapshots;
                string snapshotsRoot = Path.Combine(campaignRoot, SnapshotsFolderName);
                string dest = Path.Combine(snapshotsRoot, token);

                Directory.CreateDirectory(snapshotsRoot);

                if (Directory.Exists(dest))
                {
                    Directory.Delete(dest, recursive: true);
                }
                Directory.CreateDirectory(dest);

                int fileCount = 0;
                int linkedCount = 0;
                int copiedCount = 0;
                long totalBytes = 0;
                bool hardlinkFailed = false;
                int firstWin32Error = 0;
                string? firstWin32ErrorDesc = null;

                PopulateSnapshotFolder(
                    campaignRoot,
                    dest,
                    excludeTopLevelName: SnapshotsFolderName,
                    hardlink: hardlink,
                    ref fileCount,
                    ref linkedCount,
                    ref copiedCount,
                    ref totalBytes,
                    ref hardlinkFailed,
                    ref firstWin32Error,
                    ref firstWin32ErrorDesc);

                string manifestPath = Path.Combine(snapshotsRoot, ManifestFileName);
                JObject manifest;
                try
                {
                    manifest = File.Exists(manifestPath) ? JObject.Parse(File.ReadAllText(manifestPath)) : new JObject();
                }
                catch
                {
                    manifest = new JObject();
                }

                if (manifest["slots"] is not JObject slots)
                {
                    slots = new JObject();
                    manifest["slots"] = slots;
                }

                string key = string.IsNullOrWhiteSpace(saveName) ? token : saveName.Trim();
                var prunedList = new List<PrunedSnapshot>();

                // 1. Same save slot overwritten: delete old token folder
                if (slots[key] is JObject prior && (string?)prior["token"] is string oldToken
                    && !string.IsNullOrEmpty(oldToken) && oldToken != token)
                {
                    string oldDir = Path.Combine(snapshotsRoot, oldToken);
                    try
                    {
                        if (Directory.Exists(oldDir)) Directory.Delete(oldDir, recursive: true);
                    }
                    catch { /* best-effort */ }

                    prunedList.Add(new PrunedSnapshot
                    {
                        Token = oldToken,
                        SaveName = key,
                        Reason = PruneReason.SlotOverwritten
                    });
                }

                // 2. Put current snapshot in manifest
                slots[key] = new JObject
                {
                    ["token"] = token,
                    ["utc"] = DateTime.UtcNow.ToString("o"),
                    ["files"] = fileCount,
                    ["bytes"] = totalBytes
                };

                // 3. Prune to cap
                var slotEntries = slots.Properties()
                    .Select(p => new
                    {
                        p.Name,
                        Utc = (string?)p.Value?["utc"] ?? string.Empty,
                        Token = (string?)p.Value?["token"] ?? string.Empty
                    })
                    .OrderBy(e => e.Utc, StringComparer.Ordinal)
                    .ToList();

                for (int i = 0; !unlimited && i < slotEntries.Count - effectiveMax; i++)
                {
                    slots.Remove(slotEntries[i].Name);
                    if (!string.IsNullOrEmpty(slotEntries[i].Token))
                    {
                        string dropDir = Path.Combine(snapshotsRoot, slotEntries[i].Token);
                        try
                        {
                            if (Directory.Exists(dropDir)) Directory.Delete(dropDir, recursive: true);
                        }
                        catch { /* best-effort */ }

                        prunedList.Add(new PrunedSnapshot
                        {
                            Token = slotEntries[i].Token,
                            SaveName = slotEntries[i].Name,
                            Reason = PruneReason.OverCap
                        });
                    }
                }

                // 4. Prune orphan folders
                try
                {
                    var liveTokens = new HashSet<string>(
                        slots.Properties()
                            .Select(p => (string?)p.Value?["token"] ?? string.Empty)
                            .Where(t => !string.IsNullOrEmpty(t)),
                        StringComparer.OrdinalIgnoreCase);

                    foreach (var dir in Directory.GetDirectories(snapshotsRoot))
                    {
                        string folderName = Path.GetFileName(dir);
                        if (!liveTokens.Contains(folderName))
                        {
                            try
                            {
                                Directory.Delete(dir, recursive: true);
                            }
                            catch { /* best-effort */ }

                            prunedList.Add(new PrunedSnapshot
                            {
                                Token = folderName,
                                SaveName = string.Empty,
                                Reason = PruneReason.OrphanFolder
                            });
                        }
                    }
                }
                catch { /* best-effort */ }

                // 5. Write manifest atomically。**這是整個 Take 的最後一步，而且它就是「這張照片拍完了」的憑證**：
                // 複製中途失敗（磁碟滿、OneDrive 鎖檔）會直接跳到 catch，manifest 就不會有這個 token
                // ⇒ Restore 找不到它，什麼都不做。半張照片絕不會被蓋回現場（見 Restore 的第 3 步）。
                // 寫不進去要當成失敗回報，否則一張拍好卻永遠還原不了的照片會安靜地存在
                string? hardlinkNote = null;
                if (!hardlink)
                {
                    hardlinkNote = SnapshotLogFormatter.FormatHardlinksDisabled(token, copiedCount);
                }
                else if (hardlinkFailed)
                {
                    hardlinkNote = SnapshotLogFormatter.FormatHardlinksUnavailable(
                        token,
                        firstWin32Error,
                        firstWin32ErrorDesc ?? "unknown error",
                        copiedCount);
                }

                if (!AtomicFile.Write(new SystemFileWriter(), manifestPath, manifest.ToString(Formatting.Indented)))
                {
                    sw.Stop();
                    return new SnapshotResult
                    {
                        Outcome = SnapshotOutcome.Failed,
                        Token = token,
                        Reason = "the campaign folder was copied but the manifest could not be written - this snapshot will not be restored",
                        Files = fileCount,
                        LinkedFiles = linkedCount,
                        CopiedFiles = copiedCount,
                        Bytes = totalBytes,
                        ElapsedMs = sw.Elapsed.TotalMilliseconds,
                        Pruned = prunedList,
                        HardlinkNote = hardlinkNote
                    };
                }

                sw.Stop();
                return new SnapshotResult
                {
                    Outcome = SnapshotOutcome.Taken,
                    Token = token,
                    Files = fileCount,
                    LinkedFiles = linkedCount,
                    CopiedFiles = copiedCount,
                    Bytes = totalBytes,
                    ElapsedMs = sw.Elapsed.TotalMilliseconds,
                    Kept = slots.Count,
                    Pruned = prunedList,
                    HardlinkNote = hardlinkNote
                };
            }
            catch (Exception ex)
            {
                sw.Stop();
                return new SnapshotResult
                {
                    Outcome = SnapshotOutcome.Failed,
                    Token = token,
                    Reason = ex.Message,
                    ElapsedMs = sw.Elapsed.TotalMilliseconds
                };
            }
        }

        public static SnapshotResult Restore(string campaignRoot, string token)
        {
            var sw = Stopwatch.StartNew();

            if (string.IsNullOrEmpty(campaignRoot) || string.IsNullOrEmpty(token))
            {
                sw.Stop();
                return new SnapshotResult
                {
                    Outcome = SnapshotOutcome.NoToken,
                    Token = token ?? string.Empty,
                    Reason = "this save carries no snapshot token (it was saved before snapshots existed)",
                    ElapsedMs = sw.Elapsed.TotalMilliseconds
                };
            }

            string token8 = token.Length >= 8 ? token.Substring(0, 8) : token;

            try
            {
                // Path.Combine 也會拋（路徑含非法字元）⇒ 整段都要在 try 裡面。
                // 本方法絕不往外拋：呼叫端是 OnGameLoaded，拋出去就等於讀檔路徑上多一個例外
                string snapshotsRoot = Path.Combine(campaignRoot, SnapshotsFolderName);
                string src = Path.Combine(snapshotsRoot, token);

                if (!Directory.Exists(src))
                {
                    sw.Stop();
                    return new SnapshotResult
                    {
                        Outcome = SnapshotOutcome.SnapshotMissing,
                        Token = token,
                        Reason = $"no snapshot folder for token {token8} - it was pruned, or the folder was deleted by hand",
                        ElapsedMs = sw.Elapsed.TotalMilliseconds
                    };
                }

                bool hasContent = Directory.EnumerateFileSystemEntries(src).Any();
                if (!hasContent)
                {
                    sw.Stop();
                    return new SnapshotResult
                    {
                        Outcome = SnapshotOutcome.SnapshotEmpty,
                        Token = token,
                        Reason = $"the snapshot for token {token8} is empty - live data left untouched",
                        ElapsedMs = sw.Elapsed.TotalMilliseconds
                    };
                }

                // 3b. **半張照片不准蓋回現場。** manifest 是 Take 的最後一步，所以「token 在 manifest 裡」
                // 就等於「那次複製跑完了」；連檔數都對得上才算完整。
                // 少了這一關，一次中途失敗的複製（磁碟滿、OneDrive 鎖檔）會在下次讀那個存檔時
                // 把不完整的內容蓋掉完好的現場資料——這是整個系統唯一一個會刪東西的地方
                int expectedFiles = ReadManifestFileCount(snapshotsRoot, token);
                int actualFiles = CountFiles(src);
                if (expectedFiles < 0)
                {
                    sw.Stop();
                    return new SnapshotResult
                    {
                        Outcome = SnapshotOutcome.SnapshotIncomplete,
                        Token = token,
                        Reason = $"the snapshot for token {token8} is not listed in the manifest, so the copy never finished - live data left untouched",
                        Files = actualFiles,
                        ElapsedMs = sw.Elapsed.TotalMilliseconds
                    };
                }
                if (expectedFiles != actualFiles)
                {
                    sw.Stop();
                    return new SnapshotResult
                    {
                        Outcome = SnapshotOutcome.SnapshotIncomplete,
                        Token = token,
                        Reason = $"the snapshot for token {token8} holds {actualFiles} file(s) but the manifest recorded {expectedFiles} - live data left untouched",
                        Files = actualFiles,
                        ElapsedMs = sw.Elapsed.TotalMilliseconds
                    };
                }

                // Clear campaignRoot contents, excluding top-level _snapshots
                ClearFolderContents(campaignRoot, excludeTopLevelName: SnapshotsFolderName);

                int fileCount = 0;
                long totalBytes = 0;
                CopyFolderContents(src, campaignRoot, excludeTopLevelName: null, ref fileCount, ref totalBytes);

                sw.Stop();
                return new SnapshotResult
                {
                    Outcome = SnapshotOutcome.Restored,
                    Token = token,
                    Files = fileCount,
                    Bytes = totalBytes,
                    ElapsedMs = sw.Elapsed.TotalMilliseconds
                };
            }
            catch (Exception ex)
            {
                sw.Stop();
                return new SnapshotResult
                {
                    Outcome = SnapshotOutcome.Failed,
                    Token = token,
                    Reason = ex.Message,
                    ElapsedMs = sw.Elapsed.TotalMilliseconds
                };
            }
        }

        public static SnapshotInventory Inventory(string campaignRoot)
        {
            int liveFiles = 0;
            long liveBytes = 0;

            if (Directory.Exists(campaignRoot))
            {
                ScanFolderStats(campaignRoot, excludeTopLevelName: SnapshotsFolderName, ref liveFiles, ref liveBytes);
            }

            string snapshotsRoot = Path.Combine(campaignRoot, SnapshotsFolderName);
            if (!Directory.Exists(snapshotsRoot))
            {
                return new SnapshotInventory
                {
                    FolderExists = false,
                    LiveFiles = liveFiles,
                    LiveBytes = liveBytes
                };
            }

            long totalBytes = 0;
            int totalFiles = 0;
            ScanFolderStats(snapshotsRoot, excludeTopLevelName: null, ref totalFiles, ref totalBytes);

            var slotsList = new List<SnapshotSlot>();
            string manifestPath = Path.Combine(snapshotsRoot, ManifestFileName);

            if (File.Exists(manifestPath))
            {
                try
                {
                    var manifest = JObject.Parse(File.ReadAllText(manifestPath));
                    if (manifest["slots"] is JObject slots)
                    {
                        foreach (var prop in slots.Properties())
                        {
                            if (prop.Value is JObject sObj)
                            {
                                string tok = (string?)sObj["token"] ?? string.Empty;
                                string utc = (string?)sObj["utc"] ?? string.Empty;
                                int f = (int?)sObj["files"] ?? 0;
                                long b = (long?)sObj["bytes"] ?? 0;
                                bool exists = !string.IsNullOrEmpty(tok) && Directory.Exists(Path.Combine(snapshotsRoot, tok));

                                slotsList.Add(new SnapshotSlot
                                {
                                    SaveName = prop.Name,
                                    Token = tok,
                                    Utc = utc,
                                    Files = f,
                                    Bytes = b,
                                    ExistsOnDisk = exists
                                });
                            }
                        }
                    }
                }
                catch { /* best-effort */ }
            }

            var orderedSlots = slotsList.OrderByDescending(s => s.Utc, StringComparer.Ordinal).ToList();

            var liveTokens = new HashSet<string>(
                slotsList.Select(s => s.Token).Where(t => !string.IsNullOrEmpty(t)),
                StringComparer.OrdinalIgnoreCase);

            var orphans = new List<string>();
            try
            {
                foreach (var dir in Directory.GetDirectories(snapshotsRoot))
                {
                    string folderName = Path.GetFileName(dir);
                    if (!liveTokens.Contains(folderName))
                    {
                        orphans.Add(folderName);
                    }
                }
            }
            catch { /* best-effort */ }

            orphans.Sort(StringComparer.Ordinal);

            return new SnapshotInventory
            {
                FolderExists = true,
                Slots = orderedSlots,
                OrphanFolders = orphans,
                TotalBytes = totalBytes,
                LiveFiles = liveFiles,
                LiveBytes = liveBytes
            };
        }

        /// <summary>
        /// SNAP1：刪掉指定的快照（資料夾 ＋ manifest 那一列）。**只碰 <c>_snapshots\</c> 底下的東西**，
        /// 現場資料夾一個位元組都不動。整段吞例外：刪不掉就記進 <see cref="SnapshotDeleteResult.Failed"/>，
        /// 絕不往上拋——這支是從遊戲的 UI 回呼進來的，拋出去會讓玩家的遊戲當掉。
        /// </summary>
        public static SnapshotDeleteResult Delete(string campaignRoot, IEnumerable<string> tokens)
        {
            var deleted = new List<DeletedSnapshot>();
            var failed = new List<FailedDelete>();
            long bytesFreed = 0;

            var wanted = (tokens ?? Array.Empty<string>())
                .Where(t => !string.IsNullOrWhiteSpace(t))
                .Select(t => t.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            string snapshotsRoot = Path.Combine(campaignRoot ?? string.Empty, SnapshotsFolderName);
            if (wanted.Count == 0 || !Directory.Exists(snapshotsRoot))
            {
                foreach (var t in wanted)
                {
                    failed.Add(new FailedDelete { Token = t, Reason = "there is no snapshot folder for this campaign" });
                }
                return new SnapshotDeleteResult { Failed = failed };
            }

            string manifestPath = Path.Combine(snapshotsRoot, ManifestFileName);
            JObject manifest;
            try
            {
                manifest = File.Exists(manifestPath) ? JObject.Parse(File.ReadAllText(manifestPath)) : new JObject();
            }
            catch
            {
                manifest = new JObject();
            }
            if (manifest["slots"] is not JObject slots)
            {
                slots = new JObject();
                manifest["slots"] = slots;
            }

            foreach (string token in wanted)
            {
                // manifest 裡對應的那一列（可能沒有——玩家刪的是孤兒資料夾）
                string saveName = string.Empty;
                foreach (var prop in slots.Properties())
                {
                    if (prop.Value is JObject slot
                        && string.Equals((string?)slot["token"], token, StringComparison.OrdinalIgnoreCase))
                    {
                        saveName = prop.Name;
                        break;
                    }
                }

                string dir = Path.Combine(snapshotsRoot, token);
                try
                {
                    long bytes = 0;
                    int files = 0;
                    if (Directory.Exists(dir))
                    {
                        ScanFolderStats(dir, excludeTopLevelName: null, ref files, ref bytes);
                        Directory.Delete(dir, recursive: true);
                    }
                    else if (string.IsNullOrEmpty(saveName))
                    {
                        failed.Add(new FailedDelete
                        {
                            Token = token,
                            SaveName = saveName,
                            Reason = "no folder and no manifest entry for this token"
                        });
                        continue;
                    }

                    if (!string.IsNullOrEmpty(saveName)) slots.Remove(saveName);

                    bytesFreed += bytes;
                    deleted.Add(new DeletedSnapshot { Token = token, SaveName = saveName, Bytes = bytes });
                }
                catch (Exception ex)
                {
                    failed.Add(new FailedDelete { Token = token, SaveName = saveName, Reason = ex.Message });
                }
            }

            if (deleted.Count > 0)
            {
                try
                {
                    File.WriteAllText(manifestPath, manifest.ToString(Formatting.Indented));
                }
                catch (Exception ex)
                {
                    // 資料夾已經刪了，manifest 沒寫成 ⇒ 那幾列變成「指不到資料夾」，
                    // 下次 Take 的孤兒清理會處理掉，還原那一側本來就擋得住（SnapshotMissing）。
                    failed.Add(new FailedDelete
                    {
                        Token = "(manifest)",
                        Reason = "folders were deleted but the manifest could not be rewritten: " + ex.Message
                    });
                }
            }

            return new SnapshotDeleteResult
            {
                Deleted = deleted,
                Failed = failed,
                BytesFreed = bytesFreed,
                RemainingSlots = slots.Properties().Count()
            };
        }

        /// <summary>
        /// manifest 裡這個 token 記了幾個檔。**沒列到就回 -1**（＝那次複製沒跑完，見 Restore 第 3b 步）。
        /// manifest 讀不到或壞掉也回 -1：擋下一次還原是安全的方向，蓋掉現場資料不是。
        /// </summary>
        private static int ReadManifestFileCount(string snapshotsRoot, string token)
        {
            try
            {
                string manifestPath = Path.Combine(snapshotsRoot, ManifestFileName);
                if (!File.Exists(manifestPath)) return -1;

                var manifest = JObject.Parse(File.ReadAllText(manifestPath));
                if (manifest["slots"] is not JObject slots) return -1;

                foreach (var prop in slots.Properties())
                {
                    if (prop.Value is JObject slot
                        && string.Equals((string?)slot["token"], token, StringComparison.OrdinalIgnoreCase))
                    {
                        return (int?)slot["files"] ?? -1;
                    }
                }
                return -1;
            }
            catch
            {
                return -1;
            }
        }

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode, EntryPoint = "CreateHardLinkW")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool CreateHardLinkW(
            string lpFileName,
            string lpExistingFileName,
            IntPtr lpSecurityAttributes);

        internal static string GetWin32ErrorMessage(int errorCode)
        {
            switch (errorCode)
            {
                case 17:
                    return "the system cannot move the file to a different disk drive";
                case 1:
                    return "incorrect function";
                case 5:
                    return "access is denied";
                case 50:
                    return "the request is not supported";
                case 1142:
                    return "an attempt was made to create more links on a file than the file system supports";
                case 1392:
                    return "the file or directory is corrupted and unreadable";
            }

            try
            {
                string msg = new System.ComponentModel.Win32Exception(errorCode).Message.Trim().TrimEnd('.');
                if (!string.IsNullOrEmpty(msg))
                {
                    return char.ToLowerInvariant(msg[0]) + msg.Substring(1);
                }
            }
            catch
            {
                // best-effort
            }

            return "unknown error";
        }

        private static void PopulateSnapshotFolder(
            string src,
            string dest,
            string? excludeTopLevelName,
            bool hardlink,
            ref int fileCount,
            ref int linkedCount,
            ref int copiedCount,
            ref long totalBytes,
            ref bool hardlinkFailed,
            ref int firstWin32Error,
            ref string? firstWin32ErrorDesc)
        {
            Directory.CreateDirectory(dest);

            foreach (var dir in Directory.GetDirectories(src))
            {
                string name = Path.GetFileName(dir);
                if (excludeTopLevelName != null && string.Equals(name, excludeTopLevelName, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
                PopulateDirRecursive(
                    dir,
                    Path.Combine(dest, name),
                    hardlink,
                    ref fileCount,
                    ref linkedCount,
                    ref copiedCount,
                    ref totalBytes,
                    ref hardlinkFailed,
                    ref firstWin32Error,
                    ref firstWin32ErrorDesc);
            }

            foreach (var file in Directory.GetFiles(src))
            {
                string destFile = Path.Combine(dest, Path.GetFileName(file));
                LinkOrCopyFile(
                    file,
                    destFile,
                    hardlink,
                    ref fileCount,
                    ref linkedCount,
                    ref copiedCount,
                    ref totalBytes,
                    ref hardlinkFailed,
                    ref firstWin32Error,
                    ref firstWin32ErrorDesc);
            }
        }

        private static void PopulateDirRecursive(
            string src,
            string dest,
            bool hardlink,
            ref int fileCount,
            ref int linkedCount,
            ref int copiedCount,
            ref long totalBytes,
            ref bool hardlinkFailed,
            ref int firstWin32Error,
            ref string? firstWin32ErrorDesc)
        {
            Directory.CreateDirectory(dest);

            foreach (var dir in Directory.GetDirectories(src))
            {
                PopulateDirRecursive(
                    dir,
                    Path.Combine(dest, Path.GetFileName(dir)),
                    hardlink,
                    ref fileCount,
                    ref linkedCount,
                    ref copiedCount,
                    ref totalBytes,
                    ref hardlinkFailed,
                    ref firstWin32Error,
                    ref firstWin32ErrorDesc);
            }

            foreach (var file in Directory.GetFiles(src))
            {
                string destFile = Path.Combine(dest, Path.GetFileName(file));
                LinkOrCopyFile(
                    file,
                    destFile,
                    hardlink,
                    ref fileCount,
                    ref linkedCount,
                    ref copiedCount,
                    ref totalBytes,
                    ref hardlinkFailed,
                    ref firstWin32Error,
                    ref firstWin32ErrorDesc);
            }
        }

        private static void LinkOrCopyFile(
            string srcFile,
            string destFile,
            bool hardlink,
            ref int fileCount,
            ref int linkedCount,
            ref int copiedCount,
            ref long totalBytes,
            ref bool hardlinkFailed,
            ref int firstWin32Error,
            ref string? firstWin32ErrorDesc)
        {
            bool linked = false;

            if (hardlink)
            {
                try
                {
                    if (CreateHardLinkW(destFile, srcFile, IntPtr.Zero))
                    {
                        linked = true;
                        linkedCount++;
                    }
                    else
                    {
                        int err = Marshal.GetLastWin32Error();
                        if (!hardlinkFailed)
                        {
                            hardlinkFailed = true;
                            firstWin32Error = err;
                            firstWin32ErrorDesc = GetWin32ErrorMessage(err);
                        }
                    }
                }
                catch (Exception ex)
                {
                    if (!hardlinkFailed)
                    {
                        hardlinkFailed = true;
                        firstWin32Error = -1;
                        firstWin32ErrorDesc = ex.Message;
                    }
                }
            }

            if (!linked)
            {
                File.Copy(srcFile, destFile, overwrite: true);
                copiedCount++;
            }

            fileCount++;
            try
            {
                totalBytes += new FileInfo(srcFile).Length;
            }
            catch { /* best-effort */ }
        }

        private static int CountFiles(string folder)
        {
            int count = 0;
            long bytes = 0;
            ScanFolderStats(folder, excludeTopLevelName: null, ref count, ref bytes);
            return count;
        }

        private static void CopyFolderContents(
            string src,
            string dest,
            string? excludeTopLevelName,
            ref int fileCount,
            ref long totalBytes)
        {
            Directory.CreateDirectory(dest);

            foreach (var dir in Directory.GetDirectories(src))
            {
                string name = Path.GetFileName(dir);
                if (excludeTopLevelName != null && string.Equals(name, excludeTopLevelName, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
                CopyDirRecursive(dir, Path.Combine(dest, name), ref fileCount, ref totalBytes);
            }

            foreach (var file in Directory.GetFiles(src))
            {
                string destFile = Path.Combine(dest, Path.GetFileName(file));
                File.Copy(file, destFile, overwrite: true);
                fileCount++;
                try
                {
                    totalBytes += new FileInfo(file).Length;
                }
                catch { /* best-effort */ }
            }
        }

        private static void CopyDirRecursive(string src, string dest, ref int fileCount, ref long totalBytes)
        {
            Directory.CreateDirectory(dest);
            foreach (var dir in Directory.GetDirectories(src))
            {
                CopyDirRecursive(dir, Path.Combine(dest, Path.GetFileName(dir)), ref fileCount, ref totalBytes);
            }
            foreach (var file in Directory.GetFiles(src))
            {
                string destFile = Path.Combine(dest, Path.GetFileName(file));
                File.Copy(file, destFile, overwrite: true);
                fileCount++;
                try
                {
                    totalBytes += new FileInfo(file).Length;
                }
                catch { /* best-effort */ }
            }
        }

        private static void ClearFolderContents(string folder, string? excludeTopLevelName)
        {
            foreach (var dir in Directory.GetDirectories(folder))
            {
                string name = Path.GetFileName(dir);
                if (excludeTopLevelName != null && string.Equals(name, excludeTopLevelName, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
                try { Directory.Delete(dir, recursive: true); } catch { /* best-effort */ }
            }

            foreach (var file in Directory.GetFiles(folder))
            {
                try { File.Delete(file); } catch { /* best-effort */ }
            }
        }

        private static void ScanFolderStats(string folder, string? excludeTopLevelName, ref int fileCount, ref long totalBytes)
        {
            foreach (var dir in Directory.GetDirectories(folder))
            {
                string name = Path.GetFileName(dir);
                if (excludeTopLevelName != null && string.Equals(name, excludeTopLevelName, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
                ScanDirStatsRecursive(dir, ref fileCount, ref totalBytes);
            }

            foreach (var file in Directory.GetFiles(folder))
            {
                fileCount++;
                try { totalBytes += new FileInfo(file).Length; } catch { /* best-effort */ }
            }
        }

        private static void ScanDirStatsRecursive(string dir, ref int fileCount, ref long totalBytes)
        {
            foreach (var sub in Directory.GetDirectories(dir))
            {
                ScanDirStatsRecursive(sub, ref fileCount, ref totalBytes);
            }
            foreach (var file in Directory.GetFiles(dir))
            {
                fileCount++;
                try { totalBytes += new FileInfo(file).Length; } catch { /* best-effort */ }
            }
        }
    }
}
