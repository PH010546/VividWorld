using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using MCM.Common;
using VividWorld.Core.Config;

namespace VividWorld.Mcm
{
    /// <summary>
    /// Soft-dependency bridge between MCM (Bannerlord.MBOptionScreen) and Vivid World config.
    /// When MCM is not installed, no MCM types are ever resolved by the CLR.
    /// When MCM is installed, synchronizes settings bi-directionally between config.json and the MCM menu.
    /// </summary>
    internal static class McmBridge
    {
        private static bool _bound;
        public static bool IsBound => _bound;
        private static int _failures;
        private static int _bindAttempts;
        private static DateTime _nextAttemptUtc = DateTime.MinValue;
        private static DateTime _firstSeenMcmUtc = DateTime.MinValue;
        private static bool _mcmNotInstalledLogged;
        private static bool _unboundWarned;
        private static string _lastMenuSig = string.Empty;
        private static string _lastCfgSig = string.Empty;
        private static DateTime _lastSyncUtc = DateTime.MinValue;

        /// <summary>
        /// 選單現在該寫進哪一份設定。主選單用的那一份與戰役用的那一份**不是同一個物件**
        /// （`SubModule.OnGameStart` 與 `OnGameEnd` 各換一次），所以絕對不能把它捉進
        /// `PropertyChanged` 的閉包——那會讓選單改的值寫進已經換掉的那一份，
        /// 同一次改動被寫回檔案兩次，而且舊的那一份會連帶把別的鍵的舊值一起蓋回去。
        /// </summary>
        private static VividWorldConfig? _live;

        /// <summary>
        /// Offers the bridge a heartbeat: binds if MCM is present, then keeps menu and config in step.
        /// Throttled to ~1 second. Safe to call every tick when MCM is absent.
        /// NOTE: This method must NOT contain any MCM types in its signature or body to avoid JIT loading.
        /// </summary>
        public static void TryBind(VividWorldConfig? live)
        {
            if (live == null) return;

            // 物件換手要**先於**節流處理：戰役一開始就換，慢一拍就會有一段時間寫錯物件。
            if (!ReferenceEquals(_live, live))
            {
                if (_live != null && _bound)
                {
                    ModLog.Info("MCM: settings object swapped (main menu <-> campaign); the menu now reads and writes the current one.");
                }
                _live = live;
            }

            var now = DateTime.UtcNow;
            if (now < _nextAttemptUtc) return;
            _nextAttemptUtc = now.AddSeconds(1.0);

            var mcmLoaded = AppDomain.CurrentDomain.GetAssemblies()
                .Any(a => string.Equals(a.GetName().Name, "MCMv5", StringComparison.OrdinalIgnoreCase));
            if (!mcmLoaded)
            {
                if (!_mcmNotInstalledLogged)
                {
                    _mcmNotInstalledLogged = true;
                    ModLog.Info("MCM: not installed; settings can only be changed in config.json.");
                }
                return;
            }

            if (_firstSeenMcmUtc == DateTime.MinValue)
            {
                _firstSeenMcmUtc = now;
            }

            if (_bound)
            {
                try { SyncTick(live); } catch { /* one bad tick must not kill bridge */ }
                return;
            }

            try
            {
                if (Bind(live))
                {
                    _bound = true;
                    _lastSyncUtc = DateTime.UtcNow;
                }
                else
                {
                    WarnIfNeverRegistered(now);
                }
            }
            catch (Exception ex)
            {
                _failures++;
                _nextAttemptUtc = now.AddSeconds(5.0);
                if (_failures <= 3)
                    ModLog.Error("MCM: bind attempt " + _failures + " failed: " + ex.Message, ex);
                else if (_failures % 20 == 0)
                    ModLog.Error("MCM: bind still failing (attempt " + _failures + "): " + ex.Message, ex);
            }
        }

        public static string GetStatusString()
        {
            var mcmLoaded = AppDomain.CurrentDomain.GetAssemblies()
                .Any(a => string.Equals(a.GetName().Name, "MCMv5", StringComparison.OrdinalIgnoreCase));
            if (!mcmLoaded) return "MCM: not installed";
            if (!_bound) return $"MCM: installed, not bound ({_bindAttempts} attempts)";
            var syncStr = _lastSyncUtc == DateTime.MinValue ? "never" : _lastSyncUtc.ToString("HH:mm:ss", CultureInfo.InvariantCulture);
            return $"MCM: bound (last sync: {syncStr} UTC)";
        }

        private static void WarnIfNeverRegistered(DateTime now)
        {
            if (_unboundWarned || (now - _firstSeenMcmUtc).TotalSeconds < 30) return;
            _unboundWarned = true;
            ModLog.Warn($"MCM: assembly loaded but our settings never registered after {_bindAttempts} attempts; the menu will not reach config.json.");
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static bool Bind(VividWorldConfig live)
        {
            var s = VividWorldMcmSettings.Instance;
            _bindAttempts++;
            if (s == null) return false;

            RepairMenuInstance(s);
            PushConfigToMenu(s, live, out int seededCount, out var skippedReasons);

            ModLog.Info($"MCM: bound; {seededCount} of {McmExposedKeys.All.Count} keys seeded from config.json.");
            foreach (var reason in skippedReasons)
            {
                ModLog.Warn("MCM: skipped seeding: " + reason);
            }

            RecordSnapshots(s, live);

            s.PropertyChanged += (_, __) =>
            {
                // 這裡刻意不用 live：綁定只發生一次，而設定物件會換。要寫的一律是現在那一份。
                try
                {
                    var current = _live;
                    if (current == null) return;
                    PullAndRecord(s, current);
                }
                catch (Exception ex) { ModLog.Error("MCM: error handling PropertyChanged event", ex); }
            };

            return true;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void SyncTick(VividWorldConfig live)
        {
            var s = VividWorldMcmSettings.Instance;
            if (s == null) return;

            var menuSig = MenuSignature(s);
            if (!string.Equals(menuSig, _lastMenuSig, StringComparison.Ordinal))
            {
                PullAndRecord(s, live);
                return;
            }

            var cfgSig = CfgSignature(live);
            if (!string.Equals(cfgSig, _lastCfgSig, StringComparison.Ordinal))
            {
                PushConfigToMenu(s, live, out _, out _);
                RecordSnapshots(s, live);
            }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void PullAndRecord(VividWorldMcmSettings s, VividWorldConfig live)
        {
            bool anyChanged = false;
            foreach (var key in McmExposedKeys.All)
            {
                var menuVal = ReadMenuValue(s, key.Path);
                var oldVal = McmExposedKeys.Read(live, key.Path);

                if (!ValuesEqual(key.Kind, menuVal, oldVal))
                {
                    ModLog.Info($"MCM: {key.Path} {FormatVal(oldVal)} -> {FormatVal(menuVal)}");
                    bool ok = McmExposedKeys.Write(live, key.Path, menuVal);
                    var actualVal = McmExposedKeys.Read(live, key.Path);
                    if (!ok)
                    {
                        if (key.Kind == McmKeyKind.Int || key.Kind == McmKeyKind.Double)
                        {
                            ModLog.Warn($"MCM: {key.Path} {FormatVal(menuVal)} clamped to {FormatVal(actualVal)} (allowed [{key.Min}, {key.Max}])");
                        }
                        else
                        {
                            ModLog.Warn($"MCM: {key.Path} {FormatVal(menuVal)} invalid");
                        }
                    }
                    anyChanged = true;
                }
            }

            if (anyChanged)
            {
                var notices = new List<ClampNotice>();
                live.Normalize(notices);
                foreach (var n in notices)
                {
                    ModLog.Warn($"config.{n.Key} = {n.Requested} clamped to {n.Applied} (allowed {n.AllowedRange})");
                }

                if (Enum.TryParse<LogLevel>(live.LogLevel, true, out var parsedLevel))
                {
                    ModLog.Level = parsedLevel;
                }

                bool saved = ConfigStore.SaveExposedKeys(live);
                if (!saved)
                {
                    ModLog.Error("MCM: failed to write config.json");
                }

                SubModule.RefreshHotkeys();
                PushConfigToMenu(s, live, out _, out _);
                _lastSyncUtc = DateTime.UtcNow;
            }

            RecordSnapshots(s, live);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void RecordSnapshots(VividWorldMcmSettings s, VividWorldConfig live)
        {
            _lastMenuSig = MenuSignature(s);
            _lastCfgSig = CfgSignature(live);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static object? ReadMenuValue(VividWorldMcmSettings s, string path)
        {
            switch (path)
            {
                case "enabled": return s.Enabled;
                case "logLevel": return SelectedOf(s.LogLevel);
                case "debug.logTellerTurns": return s.LogTellerTurns;
                case "presentation.chronicleHotkey": return s.ChronicleHotkey;
                case "presentation.snapshotManagerHotkey": return s.SnapshotManagerHotkey;
                case "presentation.chronicleMaxEntries": return s.ChronicleMaxEntries;
                case "presentation.encyclopediaLinksEnabled": return s.EncyclopediaLinksEnabled;
                case "scheduling.tellersPerHourlyTick": return s.TellersPerHourlyTick;
                case "dialogue.askRelationGate": return s.AskRelationGate;
                case "dialogue.askWillingnessThreshold": return (double)s.AskWillingnessThreshold;
                case "dialogue.npcVolunteerRelationGate": return s.NpcVolunteerRelationGate;
                case "dialogue.volunteerMode": return VolunteerModeOf(s.VolunteerMode);
                case "consequences.enabled": return s.ConsequencesEnabled;
                case "consequences.bystanderMultiplier": return (double)s.BystanderMultiplier;
                case "consequences.maxAbsoluteDeltaPerHeroPerDay": return (double)s.MaxAbsoluteDeltaPerHeroPerDay;
                case "consequences.ledgerOnly": return s.ConsequencesLedgerOnly;
                case "situations.dailyScanEnabled": return s.DailyScanEnabled;
                case "situations.maxPerDay": return (double)s.SituationsMaxPerDay;
                case "situations.grudgesEnabled": return s.GrudgesEnabled;
                case "situations.clanEscalationThreshold": return s.ClanEscalationThreshold;
                case "events.sources.heroKilled": return s.SourceHeroKilled;
                case "events.sources.heroPrisonerTaken": return s.SourceHeroPrisonerTaken;
                case "events.sources.heroPrisonerReleased": return s.SourceHeroPrisonerReleased;
                case "persistence.maxSnapshots": return s.MaxSnapshots;
                default: return null;
            }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void PushConfigToMenu(VividWorldMcmSettings s, VividWorldConfig live, out int seededCount, out List<string> skipped)
        {
            int count = 0;
            var list = new List<string>();

            void PushKey(string path, Action act)
            {
                try
                {
                    act();
                    count++;
                }
                catch (Exception ex)
                {
                    list.Add($"{path} ({ex.Message})");
                }
            }

            PushKey("enabled", () => s.Enabled = live.Enabled);
            PushKey("logLevel", () =>
            {
                if (s.LogLevel != null)
                {
                    s.LogLevel.SelectedIndex = McmChoiceLists.IndexOf(McmChoiceLists.LogLevels, live.LogLevel, 2);
                }
                else
                {
                    s.LogLevel = new Dropdown<string>(McmChoiceLists.LogLevels, McmChoiceLists.IndexOf(McmChoiceLists.LogLevels, live.LogLevel, 2));
                }
            });
            PushKey("debug.logTellerTurns", () => s.LogTellerTurns = live.Debug.LogTellerTurns);
            PushKey("presentation.chronicleHotkey", () => s.ChronicleHotkey = live.Presentation.ChronicleHotkey ?? "Ctrl+L");
            PushKey("presentation.snapshotManagerHotkey", () => s.SnapshotManagerHotkey = live.Presentation.SnapshotManagerHotkey ?? "F9");
            PushKey("presentation.chronicleMaxEntries", () => s.ChronicleMaxEntries = live.Presentation.ChronicleMaxEntries);
            PushKey("presentation.encyclopediaLinksEnabled", () => s.EncyclopediaLinksEnabled = live.Presentation.EncyclopediaLinksEnabled);
            PushKey("scheduling.tellersPerHourlyTick", () => s.TellersPerHourlyTick = live.Scheduling.TellersPerHourlyTick);
            PushKey("dialogue.askRelationGate", () => s.AskRelationGate = live.Dialogue.AskRelationGate);
            PushKey("dialogue.askWillingnessThreshold", () => s.AskWillingnessThreshold = (float)live.Dialogue.AskWillingnessThreshold);
            PushKey("dialogue.npcVolunteerRelationGate", () => s.NpcVolunteerRelationGate = live.Dialogue.NpcVolunteerRelationGate);
            PushKey("dialogue.volunteerMode", () =>
            {
                // 選單上顯示「自動／暢玩／寫實」（MCM 直接顯示選項字串本身，帳本 X-39），
                // 存進 config.json 的是 auto／casual／realistic——兩者靠「第幾項」對應，見 VolunteerModeOf。
                int index = McmChoiceLists.IndexOf(McmChoiceLists.VolunteerModes, live.Dialogue.VolunteerMode, 0);
                var labels = McmChoiceLists.VolunteerModeLabels();
                if (s.VolunteerMode != null && HasLabels(s.VolunteerMode, labels))
                {
                    s.VolunteerMode.SelectedIndex = index;
                }
                else
                {
                    s.VolunteerMode = new Dropdown<string>(labels, index);
                }
            });
            PushKey("consequences.enabled", () => s.ConsequencesEnabled = live.Consequences.Enabled);
            PushKey("consequences.bystanderMultiplier", () => s.BystanderMultiplier = (float)live.Consequences.BystanderMultiplier);
            PushKey("consequences.maxAbsoluteDeltaPerHeroPerDay", () => s.MaxAbsoluteDeltaPerHeroPerDay = (float)live.Consequences.MaxAbsoluteDeltaPerHeroPerDay);
            PushKey("consequences.ledgerOnly", () => s.ConsequencesLedgerOnly = live.Consequences.LedgerOnly);
            PushKey("situations.dailyScanEnabled", () => s.DailyScanEnabled = live.Situations.DailyScanEnabled);
            PushKey("situations.maxPerDay", () => s.SituationsMaxPerDay = (float)live.Situations.MaxPerDay);
            PushKey("situations.grudgesEnabled", () => s.GrudgesEnabled = live.Situations.GrudgesEnabled);
            PushKey("situations.clanEscalationThreshold", () => s.ClanEscalationThreshold = live.Situations.ClanEscalationThreshold);
            PushKey("events.sources.heroKilled", () => s.SourceHeroKilled = live.Events.Sources.HeroKilled);
            PushKey("events.sources.heroPrisonerTaken", () => s.SourceHeroPrisonerTaken = live.Events.Sources.HeroPrisonerTaken);
            PushKey("events.sources.heroPrisonerReleased", () => s.SourceHeroPrisonerReleased = live.Events.Sources.HeroPrisonerReleased);
            PushKey("persistence.maxSnapshots", () => s.MaxSnapshots = live.Persistence.MaxSnapshots);
            seededCount = count;
            skipped = list;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void RepairMenuInstance(VividWorldMcmSettings s)
        {
            bool repaired = false;
            if (s.LogLevel == null)
            {
                s.LogLevel = new Dropdown<string>(McmChoiceLists.LogLevels, 2);
                repaired = true;
            }
            if (s.VolunteerMode == null)
            {
                s.VolunteerMode = new Dropdown<string>(McmChoiceLists.VolunteerModeLabels(), 0);
                repaired = true;
            }
            if (s.ChronicleHotkey == null)
            {
                s.ChronicleHotkey = "Ctrl+L";
                repaired = true;
            }
            if (s.SnapshotManagerHotkey == null)
            {
                s.SnapshotManagerHotkey = "F9";
                repaired = true;
            }
            if (repaired)
            {
                ModLog.Warn("MCM served an uninitialized settings instance — dropdowns/strings repaired by hand.");
            }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static string? SelectedOf(Dropdown<string>? dropdown)
        {
            try { return dropdown?.SelectedValue; }
            catch { return null; }
        }

        /// <summary>傳聞模式的下拉顯示的是在地化標籤，值要照「第幾項」換回 auto／casual／realistic。</summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static string? VolunteerModeOf(Dropdown<string>? dropdown)
        {
            try
            {
                if (dropdown == null) return null;
                int i = dropdown.SelectedIndex;
                return i >= 0 && i < McmChoiceLists.VolunteerModes.Length ? McmChoiceLists.VolunteerModes[i] : null;
            }
            catch { return null; }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static bool HasLabels(Dropdown<string> dropdown, string[] labels)
        {
            try
            {
                if (dropdown.Count != labels.Length) return false;
                for (int i = 0; i < labels.Length; i++)
                {
                    if (!string.Equals(dropdown[i], labels[i], StringComparison.Ordinal)) return false;
                }
                return true;
            }
            catch { return false; }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static string MenuSignature(VividWorldMcmSettings s)
        {
            return string.Join("|",
                s.Enabled,
                SelectedOf(s.LogLevel),
                s.LogTellerTurns,
                s.ChronicleHotkey,
                s.SnapshotManagerHotkey,
                s.ChronicleMaxEntries,
                s.EncyclopediaLinksEnabled,
                s.TellersPerHourlyTick,
                s.AskRelationGate,
                s.AskWillingnessThreshold.ToString("R", CultureInfo.InvariantCulture),
                s.NpcVolunteerRelationGate,
                VolunteerModeOf(s.VolunteerMode),
                s.ConsequencesEnabled,
                s.BystanderMultiplier.ToString("R", CultureInfo.InvariantCulture),
                s.MaxAbsoluteDeltaPerHeroPerDay.ToString("R", CultureInfo.InvariantCulture),
                s.ConsequencesLedgerOnly,
                s.DailyScanEnabled,
                s.SituationsMaxPerDay.ToString("R", CultureInfo.InvariantCulture),
                s.GrudgesEnabled,
                s.ClanEscalationThreshold,
                s.SourceHeroKilled,
                s.SourceHeroPrisonerTaken,
                s.SourceHeroPrisonerReleased,
                s.MaxSnapshots);
        }

        private static string CfgSignature(VividWorldConfig live)
        {
            return string.Join("|",
                live.Enabled,
                live.LogLevel,
                live.Debug.LogTellerTurns,
                live.Presentation.ChronicleHotkey,
                live.Presentation.SnapshotManagerHotkey,
                live.Presentation.ChronicleMaxEntries,
                live.Presentation.EncyclopediaLinksEnabled,
                live.Scheduling.TellersPerHourlyTick,
                live.Dialogue.AskRelationGate,
                live.Dialogue.AskWillingnessThreshold.ToString("R", CultureInfo.InvariantCulture),
                live.Dialogue.NpcVolunteerRelationGate,
                live.Dialogue.VolunteerMode,
                live.Consequences.Enabled,
                live.Consequences.BystanderMultiplier.ToString("R", CultureInfo.InvariantCulture),
                live.Consequences.MaxAbsoluteDeltaPerHeroPerDay.ToString("R", CultureInfo.InvariantCulture),
                live.Consequences.LedgerOnly,
                live.Situations.DailyScanEnabled,
                live.Situations.MaxPerDay.ToString("R", CultureInfo.InvariantCulture),
                live.Situations.GrudgesEnabled,
                live.Situations.ClanEscalationThreshold,
                live.Events.Sources.HeroKilled,
                live.Events.Sources.HeroPrisonerTaken,
                live.Events.Sources.HeroPrisonerReleased,
                live.Persistence.MaxSnapshots);
        }

        private static bool ValuesEqual(McmKeyKind kind, object? a, object? b)
        {
            if (a == null && b == null) return true;
            if (a == null || b == null) return false;
            switch (kind)
            {
                case McmKeyKind.Bool:
                    return a is bool b1 && b is bool b2 && b1 == b2;
                case McmKeyKind.Int:
                    return Convert.ToInt32(a) == Convert.ToInt32(b);
                case McmKeyKind.Double:
                    return Math.Abs(Convert.ToDouble(a) - Convert.ToDouble(b)) < 0.0001;
                case McmKeyKind.Text:
                case McmKeyKind.Dropdown:
                    return string.Equals(a.ToString(), b.ToString(), StringComparison.Ordinal);
                default:
                    return Equals(a, b);
            }
        }

        private static string FormatVal(object? val)
        {
            if (val == null) return "null";
            if (val is double d) return d.ToString("0.###", CultureInfo.InvariantCulture);
            if (val is float f) return f.ToString("0.###", CultureInfo.InvariantCulture);
            return val.ToString() ?? "null";
        }
    }
}
