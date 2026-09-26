#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace VividWorld.Core.Config
{
    public enum McmKeyKind
    {
        Bool,
        Int,
        Double,
        Text,
        Dropdown
    }

    public sealed class McmExposedKey
    {
        public string Path { get; }
        public McmKeyKind Kind { get; }
        public double Min { get; }
        public double Max { get; }
        public IReadOnlyList<string>? Choices { get; }

        public McmExposedKey(string path, McmKeyKind kind, double min = 0.0, double max = 0.0, IReadOnlyList<string>? choices = null)
        {
            Path = path;
            Kind = kind;
            Min = min;
            Max = max;
            Choices = choices;
        }
    }

    public static class McmExposedKeys
    {
        private static readonly Dictionary<string, McmExposedKey> KeyMap;

        public static IReadOnlyList<McmExposedKey> All { get; }

        static McmExposedKeys()
        {
            var keys = new List<McmExposedKey>
            {
                // Group 0: General
                new McmExposedKey("enabled", McmKeyKind.Bool),
                new McmExposedKey("logLevel", McmKeyKind.Dropdown, choices: new[] { "Error", "Warn", "Info" }),
                new McmExposedKey("debug.logTellerTurns", McmKeyKind.Bool),

                // Group 1: Interface (Presentation)
                new McmExposedKey("presentation.chronicleHotkey", McmKeyKind.Text),
                new McmExposedKey("presentation.snapshotManagerHotkey", McmKeyKind.Text),
                new McmExposedKey("presentation.chronicleMaxEntries", McmKeyKind.Int, 1, 200),
                new McmExposedKey("presentation.encyclopediaLinksEnabled", McmKeyKind.Bool),

                // Group 2: Propagation (Scheduling)
                new McmExposedKey("scheduling.tellersPerHourlyTick", McmKeyKind.Int, 1, 32),

                // Group 3: Dialogue
                new McmExposedKey("dialogue.askRelationGate", McmKeyKind.Int, -100, 100),
                new McmExposedKey("dialogue.askWillingnessThreshold", McmKeyKind.Double, -20.0, 40.0),
                new McmExposedKey("dialogue.npcVolunteerRelationGate", McmKeyKind.Int, -100, 100),
                new McmExposedKey("dialogue.volunteerMode", McmKeyKind.Dropdown, choices: new[] { "auto", "casual", "realistic" }),

                // Group 4: Consequences
                new McmExposedKey("consequences.enabled", McmKeyKind.Bool),
                new McmExposedKey("consequences.bystanderMultiplier", McmKeyKind.Double, 0.0, 1.0),
                new McmExposedKey("consequences.maxAbsoluteDeltaPerHeroPerDay", McmKeyKind.Double, 0.0, 100.0),
                new McmExposedKey("consequences.ledgerOnly", McmKeyKind.Bool),

                // Group 5: Situations
                new McmExposedKey("situations.dailyScanEnabled", McmKeyKind.Bool),
                new McmExposedKey("situations.maxPerDay", McmKeyKind.Double, 0.0, 10.0),
                new McmExposedKey("situations.grudgesEnabled", McmKeyKind.Bool),
                new McmExposedKey("situations.clanEscalationThreshold", McmKeyKind.Int, 0, 200),

                // Group 6: Event Sources
                new McmExposedKey("events.sources.heroKilled", McmKeyKind.Bool),
                new McmExposedKey("events.sources.heroPrisonerTaken", McmKeyKind.Bool),
                new McmExposedKey("events.sources.heroPrisonerReleased", McmKeyKind.Bool),

                // Group 7: Persistence
                new McmExposedKey("persistence.maxSnapshots", McmKeyKind.Int, 0, 64)
            };

            All = keys.AsReadOnly();
            KeyMap = keys.ToDictionary(k => k.Path, StringComparer.Ordinal);
        }

        public static object? Read(VividWorldConfig cfg, string path)
        {
            if (cfg == null || string.IsNullOrEmpty(path)) return null;

            switch (path)
            {
                case "enabled":
                    return cfg.Enabled;
                case "logLevel":
                    return cfg.LogLevel;
                case "debug.logTellerTurns":
                    return cfg.Debug.LogTellerTurns;
                case "presentation.chronicleHotkey":
                    return cfg.Presentation.ChronicleHotkey;
                case "presentation.snapshotManagerHotkey":
                    return cfg.Presentation.SnapshotManagerHotkey;
                case "presentation.chronicleMaxEntries":
                    return cfg.Presentation.ChronicleMaxEntries;
                case "presentation.encyclopediaLinksEnabled":
                    return cfg.Presentation.EncyclopediaLinksEnabled;
                case "scheduling.tellersPerHourlyTick":
                    return cfg.Scheduling.TellersPerHourlyTick;
                case "dialogue.askRelationGate":
                    return cfg.Dialogue.AskRelationGate;
                case "dialogue.askWillingnessThreshold":
                    return cfg.Dialogue.AskWillingnessThreshold;
                case "dialogue.npcVolunteerRelationGate":
                    return cfg.Dialogue.NpcVolunteerRelationGate;
                case "dialogue.volunteerMode":
                    return cfg.Dialogue.VolunteerMode;
                case "consequences.enabled":
                    return cfg.Consequences.Enabled;
                case "consequences.bystanderMultiplier":
                    return cfg.Consequences.BystanderMultiplier;
                case "consequences.maxAbsoluteDeltaPerHeroPerDay":
                    return cfg.Consequences.MaxAbsoluteDeltaPerHeroPerDay;
                case "consequences.ledgerOnly":
                    return cfg.Consequences.LedgerOnly;
                case "situations.dailyScanEnabled":
                    return cfg.Situations.DailyScanEnabled;
                case "situations.maxPerDay":
                    return cfg.Situations.MaxPerDay;
                case "situations.grudgesEnabled":
                    return cfg.Situations.GrudgesEnabled;
                case "situations.clanEscalationThreshold":
                    return cfg.Situations.ClanEscalationThreshold;
                case "events.sources.heroKilled":
                    return cfg.Events.Sources.HeroKilled;
                case "events.sources.heroPrisonerTaken":
                    return cfg.Events.Sources.HeroPrisonerTaken;
                case "events.sources.heroPrisonerReleased":
                    return cfg.Events.Sources.HeroPrisonerReleased;
                case "persistence.maxSnapshots":
                    return cfg.Persistence.MaxSnapshots;
                default:
                    return null;
            }
        }

        public static bool Write(VividWorldConfig cfg, string path, object? value)
        {
            if (cfg == null || string.IsNullOrEmpty(path)) return false;
            if (!KeyMap.TryGetValue(path, out var key)) return false;

            switch (key.Kind)
            {
                case McmKeyKind.Bool:
                {
                    bool bVal;
                    try
                    {
                        bVal = Convert.ToBoolean(value, CultureInfo.InvariantCulture);
                    }
                    catch
                    {
                        return false;
                    }

                    switch (path)
                    {
                        case "enabled":
                            cfg.Enabled = bVal;
                            return true;
                        case "debug.logTellerTurns":
                            cfg.Debug.LogTellerTurns = bVal;
                            return true;
                        case "presentation.encyclopediaLinksEnabled":
                            cfg.Presentation.EncyclopediaLinksEnabled = bVal;
                            return true;
                        case "consequences.enabled":
                            cfg.Consequences.Enabled = bVal;
                            return true;
                        case "consequences.ledgerOnly":
                            cfg.Consequences.LedgerOnly = bVal;
                            return true;
                        case "situations.dailyScanEnabled":
                            cfg.Situations.DailyScanEnabled = bVal;
                            return true;
                        case "situations.grudgesEnabled":
                            cfg.Situations.GrudgesEnabled = bVal;
                            return true;
                        case "events.sources.heroKilled":
                            cfg.Events.Sources.HeroKilled = bVal;
                            return true;
                        case "events.sources.heroPrisonerTaken":
                            cfg.Events.Sources.HeroPrisonerTaken = bVal;
                            return true;
                        case "events.sources.heroPrisonerReleased":
                            cfg.Events.Sources.HeroPrisonerReleased = bVal;
                            return true;
                        default:
                            return false;
                    }
                }

                case McmKeyKind.Text:
                {
                    string sVal = value?.ToString() ?? string.Empty;
                    switch (path)
                    {
                        case "presentation.chronicleHotkey":
                            cfg.Presentation.ChronicleHotkey = sVal;
                            return true;
                        case "presentation.snapshotManagerHotkey":
                            cfg.Presentation.SnapshotManagerHotkey = sVal;
                            return true;
                        default:
                            return false;
                    }
                }

                case McmKeyKind.Dropdown:
                {
                    string sVal = value?.ToString() ?? string.Empty;
                    if (key.Choices == null || !key.Choices.Contains(sVal))
                    {
                        return false;
                    }

                    switch (path)
                    {
                        case "logLevel":
                            cfg.LogLevel = sVal;
                            return true;
                        case "dialogue.volunteerMode":
                            cfg.Dialogue.VolunteerMode = sVal;
                            return true;
                        default:
                            return false;
                    }
                }

                case McmKeyKind.Int:
                {
                    double raw;
                    try
                    {
                        raw = Convert.ToDouble(value, CultureInfo.InvariantCulture);
                    }
                    catch
                    {
                        return false;
                    }

                    bool clamped = false;
                    if (raw < key.Min)
                    {
                        raw = key.Min;
                        clamped = true;
                    }
                    else if (raw > key.Max)
                    {
                        raw = key.Max;
                        clamped = true;
                    }

                    int iVal = (int)Math.Round(raw);
                    switch (path)
                    {
                        case "presentation.chronicleMaxEntries":
                            cfg.Presentation.ChronicleMaxEntries = iVal;
                            break;
                        case "scheduling.tellersPerHourlyTick":
                            cfg.Scheduling.TellersPerHourlyTick = iVal;
                            break;
                        case "dialogue.askRelationGate":
                            cfg.Dialogue.AskRelationGate = iVal;
                            break;
                        case "dialogue.npcVolunteerRelationGate":
                            cfg.Dialogue.NpcVolunteerRelationGate = iVal;
                            break;
                        case "situations.clanEscalationThreshold":
                            cfg.Situations.ClanEscalationThreshold = iVal;
                            break;
                        case "persistence.maxSnapshots":
                            cfg.Persistence.MaxSnapshots = iVal;
                            break;
                        default:
                            return false;
                    }

                    return !clamped;
                }

                case McmKeyKind.Double:
                {
                    double raw;
                    try
                    {
                        raw = Convert.ToDouble(value, CultureInfo.InvariantCulture);
                    }
                    catch
                    {
                        return false;
                    }

                    bool clamped = false;
                    if (raw < key.Min)
                    {
                        raw = key.Min;
                        clamped = true;
                    }
                    else if (raw > key.Max)
                    {
                        raw = key.Max;
                        clamped = true;
                    }

                    switch (path)
                    {
                        case "dialogue.askWillingnessThreshold":
                            cfg.Dialogue.AskWillingnessThreshold = raw;
                            break;
                        case "consequences.bystanderMultiplier":
                            cfg.Consequences.BystanderMultiplier = raw;
                            break;
                        case "consequences.maxAbsoluteDeltaPerHeroPerDay":
                            cfg.Consequences.MaxAbsoluteDeltaPerHeroPerDay = raw;
                            break;
                        case "situations.maxPerDay":
                            cfg.Situations.MaxPerDay = raw;
                            break;
                        default:
                            return false;
                    }

                    return !clamped;
                }

                default:
                    return false;
            }
        }
    }
}
