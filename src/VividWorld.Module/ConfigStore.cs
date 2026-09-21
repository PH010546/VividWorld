using System;
using System.Collections.Generic;
using System.Globalization;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using TaleWorlds.CampaignSystem;
using VividWorld.Core.Config;
using VividWorld.Core.Events;
using VividWorld.Core.Persistence;

namespace VividWorld
{
    internal static class ConfigStore
    {
        private const string DefaultReadme =
            "VividWorld Configuration and Data Directory\n" +
            "===========================================\n\n" +
            "This directory stores configuration, logs, and persistent campaign rumor data for Vivid World.\n\n" +
            "- config.json: Configuration settings for Vivid World. Automatically updated with missing keys on mod update; hand-written comments are not preserved (本檔案會在模組更新後自動補上新設定鍵，手寫註解不會被保留).\n" +
            "- log.txt: Diagnostic and error log.\n" +
            "- campaigns\\: Contains subdirectories per campaign (campaign_<id>), storing event shards and rumor indices.\n" +
            "- campaigns\\campaign_<id>\\_snapshots\\: One photograph of that campaign's rumor data per save\n  slot, used to rewind rumors when you load an older save. **You do not have to come here to manage them:\n  press F9 in game (presentation.snapshotManagerHotkey) to list them and delete the ones you no longer\n  want.** Nothing is deleted on its own - persistence.maxSnapshots defaults to 0, which means no limit.\n  Deleting snapshots by hand is also safe: older saves simply stop rewinding, nothing else breaks.\n\n" +
            "In-game keys (both can be rebound in config.json, under presentation):\n" +
            "- Ctrl+L  What you have heard - everything you have been told so far (presentation.chronicleHotkey).\n" +
            "- F9      Snapshot manager (presentation.snapshotManagerHotkey).\n" +
            "A modifier prefix is allowed: \"Ctrl+L\", \"Alt+K\", \"Shift+F9\", or a bare key name such as \"L\".\n\n" +
            "Uninstalling or removing this mod does not corrupt native save files.\n";

        private static VividWorldConfig? _unscaledConfig;

        /// <summary>
        /// Reads config.json -> Normalize(notices) -> reports clamped notices as Warn.
        /// Merges any missing keys from schema into existing config.json without touching existing values.
        /// Does NOT touch campaign runtime state.
        /// </summary>
        internal static VividWorldConfig LoadOrCreate()
        {
            try
            {
                var writer = new SystemFileWriter();
                var json = writer.ReadAllText(VividWorldPaths.ConfigFile);
                bool fileExists = (json != null);
                var config = (fileExists ? VividJson.Read<VividWorldConfig>(json!) : null) ?? new VividWorldConfig();

                var notices = new List<ClampNotice>();
                config.Normalize(notices);

                foreach (var n in notices)
                {
                    ModLog.Warn($"config.{n.Key} = {n.Requested} clamped to {n.Applied} (allowed {n.AllowedRange})");
                }

                if (Enum.TryParse<LogLevel>(config.LogLevel, true, out var parsedLevel))
                {
                    ModLog.Level = parsedLevel;
                }

                ModLog.FlushEveryLines = config.Debug.LogFlushEveryLines;
                ModLog.FlushEverySeconds = config.Debug.LogFlushEverySeconds;
                VividWorld.Debug.DevMetrics.Enabled = config.Debug.MetricsEnabled;

                if (!fileExists)
                {
                    var serialized = VividJson.Write(config);
                    AtomicFile.Write(writer, VividWorldPaths.ConfigFile, serialized);
                    if (!writer.Exists(VividWorldPaths.ReadmeFile))
                    {
                        writer.WriteAllText(VividWorldPaths.ReadmeFile, DefaultReadme);
                    }
                }
                else
                {
                    // 標記字串＝說明檔的世代。改內容時一起換掉，舊玩家手上那份才會被更新。
                    if (!writer.Exists(VividWorldPaths.ReadmeFile) || !(writer.ReadAllText(VividWorldPaths.ReadmeFile) ?? "").Contains("What you have heard"))
                    {
                        writer.WriteAllText(VividWorldPaths.ReadmeFile, DefaultReadme);
                    }

                    try
                    {
                        var existingJObj = JObject.Parse(json!);
                        var canonicalJson = VividJson.Write(config);
                        var canonicalJObj = JObject.Parse(canonicalJson);

                        var mergeResult = ConfigMerge.AddMissingKeys(existingJObj, canonicalJObj);
                        if (mergeResult.AddedPaths.Count > 0)
                        {
                            foreach (var path in mergeResult.AddedPaths)
                            {
                                var token = mergeResult.Merged.SelectToken(path);
                                string valStr;
                                if (token is JValue jv && jv.Type == JTokenType.String)
                                {
                                    valStr = jv.Value?.ToString() ?? "";
                                }
                                else
                                {
                                    valStr = token?.ToString(Formatting.None) ?? "null";
                                }
                                ModLog.Info($"config: added missing key {path} = {valStr}");
                            }

                            var mergedJson = mergeResult.Merged.ToString(Formatting.Indented);
                            AtomicFile.Write(writer, VividWorldPaths.ConfigFile, mergedJson);
                        }
                    }
                    catch (Exception ex)
                    {
                        ModLog.Error("ConfigStore failed to merge missing config keys.", ex);
                    }
                }

                _unscaledConfig = VividJson.Read<VividWorldConfig>(VividJson.Write(config));
                return config;
            }
            catch (Exception ex)
            {
                ModLog.Error("ConfigStore.LoadOrCreate encountered an unexpected exception, returning default config.", ex);
                var fallback = new VividWorldConfig();
                fallback.Normalize();
                _unscaledConfig = VividJson.Read<VividWorldConfig>(VividJson.Write(fallback));
                return fallback;
            }
        }

        /// <summary>
        /// Applies campaign runtime scales (calendar duration scaling and diplomacy relation scale).
        /// Must be idempotent: resetting from unscaled baseline before applying.
        /// </summary>
        internal static void ApplyRuntimeScales(VividWorldConfig config)
        {
            if (config == null) return;

            if (_unscaledConfig == null)
            {
                _unscaledConfig = VividJson.Read<VividWorldConfig>(VividJson.Write(config));
            }

            if (_unscaledConfig != null)
            {
                config.Leak.ChanceDecayHalfLifeDays = _unscaledConfig.Leak.ChanceDecayHalfLifeDays;
                config.Scheduling.RumorLifetimeDays = _unscaledConfig.Scheduling.RumorLifetimeDays;
                config.Scheduling.StaleDays = _unscaledConfig.Scheduling.StaleDays;
                config.Scheduling.SecretWatchDays = _unscaledConfig.Scheduling.SecretWatchDays;
                config.Dialogue.VolunteerCooldownDays = _unscaledConfig.Dialogue.VolunteerCooldownDays;
                config.Memory.BaseDays = _unscaledConfig.Memory.BaseDays;
            }

            double daysInYear = CampaignTime.DaysInYear;
            if (daysInYear <= 0)
            {
                ModLog.Warn("CampaignTime.DaysInYear <= 0 or unreadable; skipping calendar duration scaling.");
            }
            else
            {
                CalendarScaling.Apply(config, daysInYear);
            }

            int maxRel = TaleWorlds.CampaignSystem.Campaign.Current?.Models?.DiplomacyModel?.MaxRelationLimit ?? 0;
            if (maxRel > 0 && config.Relation.ScaleOverride <= 0)
            {
                config.Relation.EffectiveScale = maxRel;
            }
            else if (config.Relation.ScaleOverride <= 0)
            {
                config.Relation.EffectiveScale = 100.0;
                ModLog.Warn("DiplomacyModel.MaxRelationLimit is unreadable or <= 0; falling back to default relation scale 100.0.");
            }
            else
            {
                config.Relation.EffectiveScale = config.Relation.ScaleOverride;
            }

            double scale = daysInYear > 0 ? (daysInYear / CalendarScaling.NativeDaysInYear) : 1.0;
            ModLog.Info(string.Format(CultureInfo.InvariantCulture,
                "Calendar: DaysInYear={0:0} (native {1:0}), scale={2:0.000}\n             rumorLifetime={3:0.0} stale={4:0.0} secretWatch={5:0.0} leakHalfLife={6:0.0} volunteerCooldown={7:0.0} memoryBaseDays={8:0.0}",
                daysInYear,
                CalendarScaling.NativeDaysInYear,
                scale,
                config.Scheduling.RumorLifetimeDays,
                config.Scheduling.StaleDays,
                config.Scheduling.SecretWatchDays,
                config.Leak.ChanceDecayHalfLifeDays,
                config.Dialogue.VolunteerCooldownDays,
                config.Memory.BaseDays));
            ModLog.Info(string.Format(CultureInfo.InvariantCulture,
                "Relation: ScaleOverride={0}, EffectiveScale={1:0.0}",
                config.Relation.ScaleOverride,
                config.Relation.EffectiveScale));
        }

        internal static bool Reload(VividWorldConfig target)
        {
            if (target == null) return false;
            try
            {
                var fresh = LoadOrCreate();
                CopyInto(fresh, target);
                ApplyRuntimeScales(target);

                ModLog.Info("Config reloaded successfully from config.json.");
                VividWorld.Campaign.EventCatalogStore.Reload(target);
                return true;
            }
            catch (Exception ex)
            {
                ModLog.Error("Error during ConfigStore.Reload", ex);
                return false;
            }
        }

        private static void CopyInto(VividWorldConfig source, VividWorldConfig target)
        {
            target.ConfigVersion = source.ConfigVersion;
            target.Enabled = source.Enabled;
            target.LogLevel = source.LogLevel;
            target.Retention = source.Retention;
            target.Propagation = source.Propagation;
            target.Leak = source.Leak;
            target.Scheduling = source.Scheduling;
            target.Dialogue = source.Dialogue;
            target.Presentation = source.Presentation;
            target.Persistence = source.Persistence;
            target.Consequences = source.Consequences;
            target.Embellishment = source.Embellishment;
            target.Relation = source.Relation;
            target.Events = source.Events;
            target.Situations = source.Situations;
            target.Memory = source.Memory;
            target.Debug = source.Debug;
            target.Extra = source.Extra;
        }

        /// <summary>
        /// Reads existing config.json into JObject, updates ONLY the 25 exposed MCM keys via SelectToken,
        /// and writes atomically back to config.json. Preserves unknown keys and untouched settings.
        /// If a token path is not found in config.json, logs a warning and skips it without failing.
        /// </summary>
        internal static bool SaveExposedKeys(VividWorldConfig config)
        {
            if (config == null) return false;
            try
            {
                var writer = new SystemFileWriter();
                var json = writer.ReadAllText(VividWorldPaths.ConfigFile);
                if (string.IsNullOrEmpty(json))
                {
                    ModLog.Warn("ConfigStore.SaveExposedKeys: config.json not found or empty.");
                    return false;
                }

                var jobj = JObject.Parse(json!);
                foreach (var key in McmExposedKeys.All)
                {
                    var token = jobj.SelectToken(key.Path);
                    if (token == null)
                    {
                        ModLog.Warn($"ConfigStore.SaveExposedKeys: token '{key.Path}' not found in config.json; skipping.");
                        continue;
                    }

                    var val = McmExposedKeys.Read(config, key.Path);
                    JToken newTok;
                    if (val == null)
                    {
                        newTok = JValue.CreateNull();
                    }
                    else if (val is bool b)
                    {
                        newTok = new JValue(b);
                    }
                    else if (val is int i)
                    {
                        newTok = new JValue(i);
                    }
                    else if (val is double d)
                    {
                        newTok = new JValue(d);
                    }
                    else if (val is string s)
                    {
                        newTok = new JValue(s);
                    }
                    else
                    {
                        newTok = JToken.FromObject(val);
                    }

                    token.Replace(newTok);
                }

                var updatedJson = jobj.ToString(Formatting.Indented);
                bool written = AtomicFile.Write(writer, VividWorldPaths.ConfigFile, updatedJson);
                if (!written)
                {
                    ModLog.Error("ConfigStore.SaveExposedKeys: AtomicFile.Write returned false.");
                    return false;
                }

                return true;
            }
            catch (Exception ex)
            {
                ModLog.Error("ConfigStore.SaveExposedKeys encountered an exception while saving exposed keys.", ex);
                return false;
            }
        }
    }
}
