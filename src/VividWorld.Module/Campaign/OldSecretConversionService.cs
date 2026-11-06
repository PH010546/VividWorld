#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using VividWorld.Core.Catalog;
using VividWorld.Core.Events;
using VividWorld.Core.Persistence;
using VividWorld.Core.Secrets;

namespace VividWorld.Campaign
{
    internal static class OldSecretConversionService
    {
        internal static bool IsConversionDone(string campaignId, IFileWriter? writer = null)
        {
            string path = VividWorldPaths.SecretConversionFile(campaignId);
            return writer != null ? writer.Exists(path) : File.Exists(path);
        }

        internal static SecretConversionRecord? ReadConversionRecord(string campaignId)
        {
            string path = VividWorldPaths.SecretConversionFile(campaignId);
            if (!File.Exists(path)) return null;
            try
            {
                return VividJson.Read<SecretConversionRecord>(File.ReadAllText(path));
            }
            catch
            {
                return null;
            }
        }

        internal static bool TryBackupCampaign(string campaignDir, out string backupPath, out string error)
        {
            backupPath = string.Empty;
            error = string.Empty;

            if (string.IsNullOrEmpty(campaignDir) || !Directory.Exists(campaignDir))
            {
                error = "campaign directory does not exist";
                return false;
            }

            try
            {
                string parentDir = Path.GetDirectoryName(campaignDir) ?? campaignDir;
                string dirName = Path.GetFileName(campaignDir);
                string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture);
                backupPath = Path.Combine(parentDir, $"{dirName}-before-secret-conversion-{timestamp}");

                CopyDirectory(campaignDir, backupPath);
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        private static void CopyDirectory(string sourceDir, string targetDir)
        {
            Directory.CreateDirectory(targetDir);

            foreach (var file in Directory.GetFiles(sourceDir))
            {
                string destFile = Path.Combine(targetDir, Path.GetFileName(file));
                File.Copy(file, destFile, overwrite: true);
            }

            foreach (var subDir in Directory.GetDirectories(sourceDir))
            {
                string dirName = Path.GetFileName(subDir);
                string destSubDir = Path.Combine(targetDir, dirName);
                CopyDirectory(subDir, destSubDir);
            }
        }

        internal static void RunIfNeeded(
            string campaignId,
            EventShardStore store,
            RumorIndex index,
            string playerHeroId,
            double day,
            bool isNewCampaign,
            IFileWriter writer,
            Func<string, string?>? settlementOwnerClanLeaderLookup,
            Func<string, EventTemplate?>? getTemplate)
        {
            if (string.IsNullOrEmpty(campaignId) || store == null || index == null) return;

            string conversionFilePath = VividWorldPaths.SecretConversionFile(campaignId);
            if (writer.Exists(conversionFilePath))
            {
                return;
            }

            if (isNewCampaign || index.Count == 0)
            {
                var newRec = new SecretConversionRecord
                {
                    Day = day,
                    TotalConverted = 0,
                    TotalProcessed = 0,
                    CountsByType = new Dictionary<string, int>(StringComparer.Ordinal)
                };
                AtomicFile.Write(writer, conversionFilePath, VividJson.Write(newRec));
                ModLog.Info($"secret conversion: new campaign, wrote secret_conversion.json (0 converted)");
                return;
            }

            string campaignDir = VividWorldPaths.CampaignDirectory(campaignId);
            if (!TryBackupCampaign(campaignDir, out string backupPath, out string backupError))
            {
                ModLog.Error($"secret conversion: backup failed ({backupError}). Aborting conversion without modifying events.");
                return;
            }

            ModLog.Info($"secret conversion: backup created at '{backupPath}'");

            var secretEntries = index.Entries.Where(e => e != null && e.Secret).ToList();
            var eventsToConvert = new List<WorldEvent>();
            foreach (var se in secretEntries)
            {
                var evt = store.Load(se.EventId, index);
                if (evt != null)
                {
                    eventsToConvert.Add(evt);
                }
            }

            var plan = OldSecretConverter.Plan(
                eventsToConvert,
                playerHeroId,
                settlementOwnerClanLeaderLookup,
                getTemplate,
                id => index.Find(id) != null,
                applyInPlace: true,
                conversionDay: day);

            foreach (var item in plan.Items)
            {
                ModLog.Info(item.FormatLogLine());

                if (item.Action == SecretConversionAction.EnduredNpcToPublic ||
                    item.Action == SecretConversionAction.EnduredPlayerToPublic ||
                    item.Action == SecretConversionAction.PoisonedAddedOwner ||
                    item.Action == SecretConversionAction.Retired)
                {
                    if (item.ConvertedEvent != null)
                    {
                        store.Upsert(item.ConvertedEvent);
                    }
                    if (item.GeneratedGrudgeEvent != null)
                    {
                        store.Upsert(item.GeneratedGrudgeEvent);
                    }
                }
            }

            store.Flush();

            int convertedTotal = plan.ConvertedCount + plan.RetiredCount;
            var record = new SecretConversionRecord
            {
                Day = day,
                BackupPath = backupPath,
                TotalConverted = convertedTotal,
                TotalProcessed = plan.TotalProcessed,
                CountsByType = plan.CountsByType
            };

            AtomicFile.Write(writer, conversionFilePath, VividJson.Write(record));

            ModLog.Info(string.Format(CultureInfo.InvariantCulture,
                "secret conversion: completed for campaign_{0}: total {1} secret(s) processed ({2} converted, {3} retired, {4} unchanged). Backup at '{5}'",
                campaignId, plan.TotalProcessed, plan.ConvertedCount, plan.RetiredCount, plan.UnchangedCount, backupPath));
        }
    }
}
