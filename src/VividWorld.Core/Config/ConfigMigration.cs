using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;

namespace VividWorld.Core.Config
{
    public sealed class ConfigMigrationChange
    {
        public string Key { get; }
        public object OldValue { get; }
        public object NewValue { get; }
        public bool Migrated { get; }
        public string? Reason { get; }

        public ConfigMigrationChange(string key, object oldValue, object newValue, bool migrated, string? reason = null)
        {
            Key = key ?? throw new ArgumentNullException(nameof(key));
            OldValue = oldValue;
            NewValue = newValue;
            Migrated = migrated;
            Reason = reason;
        }
    }

    public sealed class ConfigMigrationResult
    {
        public int InitialVersion { get; }
        public int ResultVersion { get; }
        public bool HasChanges { get; }
        public IReadOnlyList<ConfigMigrationChange> Changes { get; }
        public IReadOnlyList<string> LogLines { get; }

        public ConfigMigrationResult(
            int initialVersion,
            int resultVersion,
            bool hasChanges,
            IReadOnlyList<ConfigMigrationChange> changes,
            IReadOnlyList<string> logLines)
        {
            InitialVersion = initialVersion;
            ResultVersion = resultVersion;
            HasChanges = hasChanges;
            Changes = changes ?? Array.Empty<ConfigMigrationChange>();
            LogLines = logLines ?? Array.Empty<string>();
        }
    }

    /// <summary>
    /// 設定檔版本一次性遷移純函式。
    /// </summary>
    public static class ConfigMigration
    {
        public static ConfigMigrationResult Migrate(JObject existing, int targetVersion = VividWorldConfig.CurrentConfigVersion)
        {
            if (existing == null) throw new ArgumentNullException(nameof(existing));

            var versionProp = existing.Property("configVersion", StringComparison.OrdinalIgnoreCase);
            int currentVersion = 1;
            if (versionProp != null && versionProp.Value != null && versionProp.Value.Type != JTokenType.Null)
            {
                if (int.TryParse(versionProp.Value.ToString(), out int parsedVersion))
                {
                    currentVersion = parsedVersion;
                }
            }

            int initialVersion = currentVersion;
            if (currentVersion >= targetVersion)
            {
                var noMigrationLogs = new[] { $"config: no migration needed (configVersion {currentVersion})" };
                return new ConfigMigrationResult(initialVersion, currentVersion, false, Array.Empty<ConfigMigrationChange>(), noMigrationLogs);
            }

            var changes = new List<ConfigMigrationChange>();
            var logLines = new List<string>();

            // 依版本逐步往上遷移
            if (currentVersion < 2 && targetVersion >= 2)
            {
                var presProp = existing.Property("presentation", StringComparison.OrdinalIgnoreCase);
                if (presProp?.Value is JObject presObj)
                {
                    var feelingsProp = presObj.Property("feelings", StringComparison.OrdinalIgnoreCase);
                    if (feelingsProp?.Value is JObject feelingsObj)
                    {
                        // affectionHigh
                        var highProp = feelingsObj.Property("affectionHigh", StringComparison.OrdinalIgnoreCase);
                        if (highProp != null && highProp.Value is JValue jvHigh && jvHigh.Value != null)
                        {
                            if (int.TryParse(jvHigh.Value.ToString(), out int highVal))
                            {
                                if (highVal == 30)
                                {
                                    highProp.Value = 50;
                                    changes.Add(new ConfigMigrationChange("presentation.feelings.affectionHigh", 30, 50, true));
                                    logLines.Add($"config: migrated presentation.feelings.affectionHigh 30 -> 50 (configVersion {currentVersion} -> 2)");
                                }
                                else
                                {
                                    changes.Add(new ConfigMigrationChange("presentation.feelings.affectionHigh", highVal, highVal, false, "not the old default 30"));
                                    logLines.Add($"config: kept presentation.feelings.affectionHigh = {highVal} (not the old default 30)");
                                }
                            }
                        }

                        // affectionLow
                        var lowProp = feelingsObj.Property("affectionLow", StringComparison.OrdinalIgnoreCase);
                        if (lowProp != null && lowProp.Value is JValue jvLow && jvLow.Value != null)
                        {
                            if (int.TryParse(jvLow.Value.ToString(), out int lowVal))
                            {
                                if (lowVal == -30)
                                {
                                    lowProp.Value = -50;
                                    changes.Add(new ConfigMigrationChange("presentation.feelings.affectionLow", -30, -50, true));
                                    logLines.Add($"config: migrated presentation.feelings.affectionLow -30 -> -50 (configVersion {currentVersion} -> 2)");
                                }
                                else
                                {
                                    changes.Add(new ConfigMigrationChange("presentation.feelings.affectionLow", lowVal, lowVal, false, "not the old default -30"));
                                    logLines.Add($"config: kept presentation.feelings.affectionLow = {lowVal} (not the old default -30)");
                                }
                            }
                        }
                    }
                }

                currentVersion = 2;
                if (versionProp != null)
                {
                    versionProp.Value = 2;
                }
                else
                {
                    existing["configVersion"] = 2;
                }
            }

            return new ConfigMigrationResult(initialVersion, currentVersion, true, changes, logLines);
        }
    }
}
