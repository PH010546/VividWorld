using MCM.Abstractions.Attributes;
using MCM.Abstractions.Attributes.v2;
using MCM.Abstractions.Base.Global;
using MCM.Common;

namespace VividWorld.Mcm
{
    /// <summary>
    /// In-game Mod Configuration Menu (MCM) settings for Vivid World.
    /// Auto-discovered by MCM when the Bannerlord.MBOptionScreen module is loaded.
    /// config.json is the single source of truth; McmBridge synchronizes between config.json and this class.
    /// </summary>
    public sealed class VividWorldMcmSettings : AttributeGlobalSettings<VividWorldMcmSettings>
    {
        public override string Id => "VividWorld_v1";
        public override string DisplayName =>
            new TaleWorlds.Localization.TextObject("{=VividWorld_MCM_DisplayName}Vivid World").ToString();
        public override string FolderName => "VividWorld";
        public override string FormatType => "json2";

        // ── Group 0: General ────────────────────────────────────────────────────────

        [SettingPropertyBool("{=VividWorld_MCM_Enabled}Enable mod", Order = 0, RequireRestart = true,
            HintText = "{=VividWorld_MCM_EnabledHint}Enables or disables Vivid World entirely. Requires game restart.")]
        [SettingPropertyGroup("{=VividWorld_MCM_Group_General}General", GroupOrder = 0)]
        public bool Enabled { get; set; } = true;

        [SettingPropertyDropdown("{=VividWorld_MCM_LogLevel}Log level", Order = 1, RequireRestart = false,
            HintText = "{=VividWorld_MCM_LogLevelHint}Verbosity of mod logs written to log.txt. Info is recommended for normal play; Warn or Error reduces log file size.")]
        [SettingPropertyGroup("{=VividWorld_MCM_Group_General}General", GroupOrder = 0)]
        public Dropdown<string> LogLevel { get; set; } = new Dropdown<string>(McmChoiceLists.LogLevels, 2);

        [SettingPropertyBool("{=VividWorld_MCM_LogTellerTurns}Log teller turns", Order = 2, RequireRestart = false,
            HintText = "{=VividWorld_MCM_LogTellerTurnsHint}Writes detailed diagnostic information about hourly rumor propagation choices to log.txt. Useful for troubleshooting.")]
        [SettingPropertyGroup("{=VividWorld_MCM_Group_General}General", GroupOrder = 0)]
        public bool LogTellerTurns { get; set; } = false;

        // ── Group 1: Interface ──────────────────────────────────────────────────────

        [SettingPropertyText("{=VividWorld_MCM_ChronicleHotkey}'What you have heard' hotkey", Order = 0, RequireRestart = false,
            HintText = "{=VividWorld_MCM_ChronicleHotkeyHint}Hotkey to open the 'What you have heard' window on the campaign map (e.g. Ctrl+L, L).")]
        [SettingPropertyGroup("{=VividWorld_MCM_Group_Interface}Interface", GroupOrder = 1)]
        public string ChronicleHotkey { get; set; } = "Ctrl+L";

        [SettingPropertyText("{=VividWorld_MCM_SnapshotManagerHotkey}Snapshot manager hotkey", Order = 1, RequireRestart = false,
            HintText = "{=VividWorld_MCM_SnapshotManagerHotkeyHint}Hotkey to open the snapshot manager window (e.g. F9).")]
        [SettingPropertyGroup("{=VividWorld_MCM_Group_Interface}Interface", GroupOrder = 1)]
        public string SnapshotManagerHotkey { get; set; } = "F9";

        [SettingPropertyInteger("{=VividWorld_MCM_ChronicleMaxEntries}Max entries shown", 1, 200, "0", Order = 2, RequireRestart = false,
            HintText = "{=VividWorld_MCM_ChronicleMaxEntriesHint}Maximum number of recent rumor entries shown in the 'What you have heard' window.")]
        [SettingPropertyGroup("{=VividWorld_MCM_Group_Interface}Interface", GroupOrder = 1)]
        public int ChronicleMaxEntries { get; set; } = 50;

        [SettingPropertyBool("{=VividWorld_MCM_EncyclopediaLinksEnabled}Encyclopedia links in rumors", Order = 3, RequireRestart = false,
            HintText = "{=VividWorld_MCM_EncyclopediaLinksEnabledHint}Enables clickable links to hero and settlement encyclopedia pages within rumor text.")]
        [SettingPropertyGroup("{=VividWorld_MCM_Group_Interface}Interface", GroupOrder = 1)]
        public bool EncyclopediaLinksEnabled { get; set; } = true;

        // ── Group 2: Propagation ────────────────────────────────────────────────────

        [SettingPropertyInteger("{=VividWorld_MCM_TellersPerHourlyTick}Tellers per hourly tick", 1, 32, "0", Order = 0, RequireRestart = false,
            HintText = "{=VividWorld_MCM_TellersPerHourlyTickHint}Maximum number of NPCs picked to share rumors every in-game hour. Higher values increase rumor spread speed.")]
        [SettingPropertyGroup("{=VividWorld_MCM_Group_Propagation}Propagation", GroupOrder = 2)]
        public int TellersPerHourlyTick { get; set; } = 8;

        // ── Group 3: Dialogue ───────────────────────────────────────────────────────

        [SettingPropertyInteger("{=VividWorld_MCM_AskRelationGate}Ask rumor relation gate", -100, 100, "0", Order = 0, RequireRestart = false,
            HintText = "{=VividWorld_MCM_AskRelationGateHint}Minimum relation required before an NPC is willing to answer your inquiries about news.")]
        [SettingPropertyGroup("{=VividWorld_MCM_Group_Dialogue}Dialogue", GroupOrder = 3)]
        public int AskRelationGate { get; set; } = 0;

        [SettingPropertyFloatingInteger("{=VividWorld_MCM_AskWillingnessThreshold}Ask willingness threshold", -20f, 40f, "0.0", Order = 1, RequireRestart = false,
            HintText = "{=VividWorld_MCM_AskWillingnessThresholdHint}The threshold a lord must clear before he will answer you. The score weighs his opinion of you together with his character (generosity and honour add, a calculating nature subtracts).")]
        [SettingPropertyGroup("{=VividWorld_MCM_Group_Dialogue}Dialogue", GroupOrder = 3)]
        public float AskWillingnessThreshold { get; set; } = 5.0f;


        [SettingPropertyInteger("{=VividWorld_MCM_NpcVolunteerRelationGate}Volunteer relation gate", -100, 100, "0", Order = 3, RequireRestart = false,
            HintText = "{=VividWorld_MCM_NpcVolunteerRelationGateHint}Minimum relation required for an NPC to voluntarily offer news during conversation.")]
        [SettingPropertyGroup("{=VividWorld_MCM_Group_Dialogue}Dialogue", GroupOrder = 3)]
        public int NpcVolunteerRelationGate { get; set; } = 30;

        [SettingPropertyDropdown("{=VividWorld_MCM_CommonerCompatMode}Commoner-deference mode", Order = 4, RequireRestart = false,
            HintText = "{=VividWorld_MCM_CommonerCompatModeHint}Works alongside mods such as NaN and Lowborn that stop commoners addressing nobles. auto = enable on detection; while enabled, the option to ask for news is hidden below clan tier 1. on = always, off = never.")]
        [SettingPropertyGroup("{=VividWorld_MCM_Group_Dialogue}Dialogue", GroupOrder = 3)]
        public Dropdown<string> CommonerCompatMode { get; set; } = new Dropdown<string>(McmChoiceLists.CommonerCompatModes, 0);

        // ── Group 4: Consequences ───────────────────────────────────────────────────

        [SettingPropertyBool("{=VividWorld_MCM_ConsequencesEnabled}Enable relation consequences", Order = 0, RequireRestart = false,
            HintText = "{=VividWorld_MCM_ConsequencesEnabledHint}Enables personal relation changes between NPCs when they learn about events involving each other.")]
        [SettingPropertyGroup("{=VividWorld_MCM_Group_Consequences}Consequences", GroupOrder = 4)]
        public bool ConsequencesEnabled { get; set; } = true;

        [SettingPropertyFloatingInteger("{=VividWorld_MCM_BystanderMultiplier}Bystander impact multiplier", 0f, 1f, "0.00", Order = 1, RequireRestart = false,
            HintText = "{=VividWorld_MCM_BystanderMultiplierHint}Multiplies relation changes for bystanders who are not direct participants in an event.")]
        [SettingPropertyGroup("{=VividWorld_MCM_Group_Consequences}Consequences", GroupOrder = 4)]
        public float BystanderMultiplier { get; set; } = 0.35f;

        [SettingPropertyFloatingInteger("{=VividWorld_MCM_MaxAbsoluteDeltaPerHeroPerDay}Max daily relation delta", 0f, 100f, "0.0", Order = 2, RequireRestart = false,
            HintText = "{=VividWorld_MCM_MaxAbsoluteDeltaPerHeroPerDayHint}Maximum total absolute relation change any single hero can accumulate from rumors in a single day.")]
        [SettingPropertyGroup("{=VividWorld_MCM_Group_Consequences}Consequences", GroupOrder = 4)]
        public float MaxAbsoluteDeltaPerHeroPerDay { get; set; } = 6.0f;

        [SettingPropertyBool("{=VividWorld_MCM_ConsequencesLedgerOnly}Ledger only (no native relation changes)", Order = 3, RequireRestart = false,
            HintText = "{=VividWorld_MCM_ConsequencesLedgerOnlyHint}Calculates relation consequences internally without applying them to native game hero relations.")]
        [SettingPropertyGroup("{=VividWorld_MCM_Group_Consequences}Consequences", GroupOrder = 4)]
        public bool ConsequencesLedgerOnly { get; set; } = false;

        // ── Group 5: Situations ─────────────────────────────────────────────────────

        [SettingPropertyBool("{=VividWorld_MCM_DailyScanEnabled}Enable daily situation scan", Order = 0, RequireRestart = false,
            HintText = "{=VividWorld_MCM_DailyScanEnabledHint}Scans the settlements each day for occasions where two lords can play out a scene. Disabling this stops authored scenes from occurring.")]
        [SettingPropertyGroup("{=VividWorld_MCM_Group_Situations}Situations", GroupOrder = 5)]
        public bool DailyScanEnabled { get; set; } = true;

        [SettingPropertyFloatingInteger("{=VividWorld_MCM_SituationsMaxPerDay}Max situations triggered per day", 0f, 10f, "0.0", Order = 1, RequireRestart = false,
            HintText = "{=VividWorld_MCM_SituationsMaxPerDayHint}How many authored scenes are expected to occur worldwide each day.")]
        [SettingPropertyGroup("{=VividWorld_MCM_Group_Situations}Situations", GroupOrder = 5)]
        public float SituationsMaxPerDay { get; set; } = 1.5f;

        [SettingPropertyBool("{=VividWorld_MCM_GrudgesEnabled}Enable personal grudges", Order = 2, RequireRestart = false,
            HintText = "{=VividWorld_MCM_GrudgesEnabledHint}Lets conflicts between lords leave a score behind in the mod's own grudge ledger. Scenes still play out when disabled, but leave nothing behind.")]
        [SettingPropertyGroup("{=VividWorld_MCM_Group_Situations}Situations", GroupOrder = 5)]
        public bool GrudgesEnabled { get; set; } = true;

        [SettingPropertyInteger("{=VividWorld_MCM_ClanEscalationThreshold}Clan escalation grudge threshold", 0, 200, "0", Order = 3, RequireRestart = false,
            HintText = "{=VividWorld_MCM_ClanEscalationThresholdHint}Accumulated grudge intensity required before personal hostility escalates into an inter-clan feud.")]
        [SettingPropertyGroup("{=VividWorld_MCM_Group_Situations}Situations", GroupOrder = 5)]
        public int ClanEscalationThreshold { get; set; } = 30;

        // ── Group 6: Event Sources ──────────────────────────────────────────────────

        [SettingPropertyBool("{=VividWorld_MCM_SourceHeroKilled}Hero deaths", Order = 0, RequireRestart = false,
            HintText = "{=VividWorld_MCM_SourceHeroKilledHint}Produces news when a lord dies, distinguished by cause into murder, execution, death in battle and natural death.")]
        [SettingPropertyGroup("{=VividWorld_MCM_Group_Sources}Event Sources", GroupOrder = 6)]
        public bool SourceHeroKilled { get; set; } = true;

        [SettingPropertyBool("{=VividWorld_MCM_SourceHeroPrisonerTaken}Hero captures", Order = 1, RequireRestart = false,
            HintText = "{=VividWorld_MCM_SourceHeroPrisonerTakenHint}Generates world events when lords are captured following battles or raids.")]
        [SettingPropertyGroup("{=VividWorld_MCM_Group_Sources}Event Sources", GroupOrder = 6)]
        public bool SourceHeroPrisonerTaken { get; set; } = true;

        [SettingPropertyBool("{=VividWorld_MCM_SourceHeroPrisonerReleased}Hero releases and escapes", Order = 2, RequireRestart = false,
            HintText = "{=VividWorld_MCM_SourceHeroPrisonerReleasedHint}Generates world events when captured lords are ransomed, released, or escape captivity.")]
        [SettingPropertyGroup("{=VividWorld_MCM_Group_Sources}Event Sources", GroupOrder = 6)]
        public bool SourceHeroPrisonerReleased { get; set; } = true;

        // ── Group 7: Persistence ────────────────────────────────────────────────────

        [SettingPropertyInteger("{=VividWorld_MCM_MaxSnapshots}Max saved snapshots", 0, 64, "0", Order = 0, RequireRestart = false,
            HintText = "{=VividWorld_MCM_MaxSnapshotsHint}Maximum number of rumor system snapshots retained per campaign. 0 keeps all snapshots without automatic pruning.")]
        [SettingPropertyGroup("{=VividWorld_MCM_Group_Persistence}Persistence", GroupOrder = 7)]
        public int MaxSnapshots { get; set; } = 0;
    }
}
