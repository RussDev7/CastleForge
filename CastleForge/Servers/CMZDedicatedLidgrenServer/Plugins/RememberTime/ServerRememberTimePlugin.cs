/*
SPDX-License-Identifier: GPL-3.0-or-later
Copyright (c) 2025 RussDev7, unknowghost0
This file is part of https://github.com/RussDev7/CMZDedicatedServers - see LICENSE for details.
*/

using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System;

namespace CMZDedicatedLidgrenServer.Plugins.RememberTime
{
    /// <summary>
    /// Saves the dedicated server's authoritative day/time value to disk and restores it on startup.
    ///
    /// Summary:
    /// - Restores the last saved server time when the server starts.
    /// - Periodically saves the current server time on an interval.
    /// - Saves one final time during clean server shutdown.
    /// - Stores state per world so different worlds can keep different day/time progress.
    ///
    /// Notes:
    /// - The saved value is the full server day float, not only the 0..1 visual time fraction.
    /// - Example: 12.41 means the server is on display day 13 at roughly the same visual time as 0.41.
    /// - Saves are interval-based to avoid constant disk writes.
    /// - Shutdown saving reduces time loss on clean exits, while interval saving reduces time loss on crashes.
    /// </summary>
    internal sealed class ServerRememberTimePlugin :
        IServerWorldPlugin,
        IServerTickPlugin,
        IServerShutdownPlugin
    {
        #region Fields

        #region Services

        /// <summary>
        /// Server-provided logging callback.
        /// Defaults to a no-op logger so plugin code can safely call _log before initialization completes.
        /// </summary>
        private Action<string> _log = _ => { };

        /// <summary>
        /// Server-provided callback for reading the authoritative server day/time value.
        /// </summary>
        private Func<float> _getTimeOfDay;

        /// <summary>
        /// Server-provided callback for applying a restored authoritative server day/time value.
        /// </summary>
        private Action<float> _setTimeOfDay;

        #endregion

        #region Config / State

        /// <summary>
        /// Runtime RememberTime settings loaded from RememberTime.Config.ini.
        /// </summary>
        private readonly RememberTimeConfig _config = new();

        /// <summary>
        /// Root plugin folder:
        /// Plugins\RememberTime
        /// </summary>
        private string _pluginDir;

        /// <summary>
        /// Per-world state folder:
        /// Plugins\RememberTime\Worlds\{WorldGuid}
        /// </summary>
        private string _worldDir;

        /// <summary>
        /// Plugin config path:
        /// Plugins\RememberTime\RememberTime.Config.ini
        /// </summary>
        private string _configPath;

        /// <summary>
        /// Per-world saved time path:
        /// Plugins\RememberTime\Worlds\{WorldGuid}\Time.State.ini
        /// </summary>
        private string _statePath;

        /// <summary>
        /// UTC timestamp for the next allowed interval save.
        /// </summary>
        private DateTime _nextSaveUtc = DateTime.MinValue;

        /// <summary>
        /// Prevents plugin reload from resetting live server time back to the last saved file value.
        /// Restore should happen once per server process, not every console reload.
        /// </summary>
        private bool _restoredThisProcess;

        #endregion

        #endregion

        #region Properties

        /// <summary>
        /// Display name used by the server plugin manager and logs.
        /// </summary>
        public string Name => "RememberTime";

        #endregion

        #region Plugin Lifecycle

        /// <summary>
        /// Initializes RememberTime, creates config/state folders, loads settings, and restores saved time once.
        ///
        /// Notes:
        /// - Config is global to the plugin.
        /// - Time state is stored per world.
        /// - Restore only runs once per process so plugin reloads do not roll time backward.
        /// </summary>
        public void Initialize(ServerPluginContext context)
        {
            _log = context?.Log ?? (_ => { });
            _getTimeOfDay = context?.GetTimeOfDay;
            _setTimeOfDay = context?.SetTimeOfDay;

            string serverDir = context?.BaseDir;
            if (string.IsNullOrWhiteSpace(serverDir))
                serverDir = AppDomain.CurrentDomain.BaseDirectory;

            serverDir = Path.GetFullPath(serverDir);

            string worldKey = SanitizeWorldKey(context?.WorldGuid);

            _pluginDir = Path.Combine(serverDir, "Plugins", "RememberTime");
            _worldDir = Path.Combine(_pluginDir, "Worlds", worldKey);
            _configPath = Path.Combine(_pluginDir, "RememberTime.Config.ini");
            _statePath = Path.Combine(_worldDir, "Time.State.ini");

            Directory.CreateDirectory(_pluginDir);
            Directory.CreateDirectory(_worldDir);

            EnsureDefaultConfig();
            LoadConfig();

            if (_config.Enabled &&
                _config.RestoreOnStartup &&
                !_restoredThisProcess &&
                _setTimeOfDay != null)
            {
                if (TryLoadSavedTime(out float savedTimeOfDay))
                {
                    _setTimeOfDay(savedTimeOfDay);
                    _log($"[RememberTime] Restored server time to {savedTimeOfDay:0.000000} ({FormatDisplayDay(savedTimeOfDay)}).");
                }
                else
                {
                    _log("[RememberTime] No saved time found yet; using server default time.");
                }

                _restoredThisProcess = true;
            }

            _nextSaveUtc = DateTime.UtcNow.AddSeconds(_config.SaveIntervalSeconds);

            _log($"[RememberTime] Config: {_configPath}.");
            _log($"[RememberTime] State : {_statePath}.");
            _log($"[RememberTime] Enabled={_config.Enabled}, IntervalSeconds={_config.SaveIntervalSeconds}, RestoreOnStartup={_config.RestoreOnStartup}.");
        }

        /// <summary>
        /// RememberTime does not consume world packets.
        ///
        /// Summary:
        /// - Returns false so the server continues normal packet handling.
        /// - This plugin only uses lifecycle, tick, and shutdown events.
        /// </summary>
        public bool BeforeHostMessage(HostMessageContext context)
        {
            return false;
        }
        #endregion

        #region Tick / Shutdown Events

        /// <summary>
        /// Periodically saves the current authoritative server time to disk.
        ///
        /// Notes:
        /// - Uses UtcNow from the tick context when available.
        /// - Falls back to DateTime.UtcNow if the context timestamp is not populated.
        /// - SaveIntervalSeconds controls how often the plugin writes to disk.
        /// </summary>
        public void Update(ServerPluginTickContext context)
        {
            if (context == null || !_config.Enabled)
                return;

            DateTime now = context.UtcNow == default ? DateTime.UtcNow : context.UtcNow;

            if (now < _nextSaveUtc)
                return;

            _nextSaveUtc = now.AddSeconds(_config.SaveIntervalSeconds);

            SaveTime(context.TimeOfDay, now, "interval");
        }

        /// <summary>
        /// Saves one final time when the server is stopping.
        ///
        /// Notes:
        /// - This helps preserve the most accurate time during clean shutdowns.
        /// - Crash protection still depends on the interval save.
        /// </summary>
        public void OnServerStopping(ServerPluginShutdownContext context)
        {
            if (context == null || !_config.Enabled || !_config.SaveOnShutdown)
                return;

            DateTime now = context.UtcNow == default ? DateTime.UtcNow : context.UtcNow;

            SaveTime(context.TimeOfDay, now, "shutdown");
        }
        #endregion

        #region Config

        /// <summary>
        /// Writes the default RememberTime config if it does not already exist.
        ///
        /// Default behavior:
        /// - Enabled.
        /// - Saves once per minute.
        /// - Restores on startup.
        /// - Saves on clean shutdown.
        /// </summary>
        private void EnsureDefaultConfig()
        {
            if (File.Exists(_configPath))
                return;

            string text =
@"[General]
Enabled = true

# Saves the server's current day/time every X seconds.
# Higher values reduce disk writes.
# Lower values reduce lost time if the server crashes.
SaveIntervalSeconds = 60

# Restores the saved time when the server process starts.
RestoreOnStartup = true

# Writes one final time when the server stops cleanly.
SaveOnShutdown = true
";

            File.WriteAllText(_configPath, text);
        }

        /// <summary>
        /// Loads RememberTime settings from RememberTime.Config.ini.
        ///
        /// Notes:
        /// - Missing values fall back to safe defaults.
        /// - SaveIntervalSeconds is clamped so bad config values do not cause excessive writes.
        /// </summary>
        private void LoadConfig()
        {
            Dictionary<string, string> values = ReadIni(_configPath);

            _config.Enabled = GetBool(values, "General.Enabled", true);
            _config.RestoreOnStartup = GetBool(values, "General.RestoreOnStartup", true);
            _config.SaveOnShutdown = GetBool(values, "General.SaveOnShutdown", true);

            _config.SaveIntervalSeconds = Clamp(
                GetInt(values, "General.SaveIntervalSeconds", 60),
                5,
                86400);
        }
        #endregion

        #region Save / Load State

        /// <summary>
        /// Saves the current authoritative server time using an atomic temp-file write.
        ///
        /// Notes:
        /// - TimeOfDay is the full server day/time float.
        /// - DisplayDay is saved for readability only.
        /// - Reason records whether the save came from an interval or shutdown event.
        /// </summary>
        private void SaveTime(float timeOfDay, DateTime savedUtc, string reason)
        {
            if (!IsValidTimeOfDay(timeOfDay))
            {
                _log($"[RememberTime] Skipped save because time value was invalid: {timeOfDay}.");
                return;
            }

            try
            {
                Directory.CreateDirectory(_worldDir);

                int displayDay = Math.Max(1, (int)Math.Floor(timeOfDay) + 1);

                string text =
$@"[State]
TimeOfDay = {timeOfDay.ToString("R", CultureInfo.InvariantCulture)}
DisplayDay = {displayDay.ToString(CultureInfo.InvariantCulture)}
SavedUtc = {savedUtc.ToString("O", CultureInfo.InvariantCulture)}
Reason = {reason}
";

                WriteAllTextAtomic(_statePath, text);

                _log($"[RememberTime] Saved time {timeOfDay:0.000000} ({FormatDisplayDay(timeOfDay)}) via {reason}.");
            }
            catch (Exception ex)
            {
                _log($"[RememberTime] Failed to save time: {ex.Message}");
            }
        }

        /// <summary>
        /// Loads the last saved authoritative server time from disk.
        ///
        /// Returns:
        /// - true when a valid saved time was loaded.
        /// - false when no state file exists, the value is missing, or the value is invalid.
        /// </summary>
        private bool TryLoadSavedTime(out float timeOfDay)
        {
            timeOfDay = 0f;

            if (string.IsNullOrWhiteSpace(_statePath) || !File.Exists(_statePath))
                return false;

            try
            {
                Dictionary<string, string> values = ReadIni(_statePath);

                if (!values.TryGetValue("State.TimeOfDay", out string raw))
                    return false;

                if (!float.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out timeOfDay))
                    return false;

                return IsValidTimeOfDay(timeOfDay);
            }
            catch (Exception ex)
            {
                _log($"[RememberTime] Failed to load saved time: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Writes text through a temporary file so partial writes are less likely to corrupt the state file.
        ///
        /// Notes:
        /// - Existing files are replaced using File.Replace.
        /// - New files are created by moving the completed temp file into place.
        /// </summary>
        private static void WriteAllTextAtomic(string path, string text)
        {
            string dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(dir))
                Directory.CreateDirectory(dir);

            string tempPath = path + ".tmp";

            File.WriteAllText(tempPath, text);

            if (File.Exists(path))
                File.Replace(tempPath, path, null);
            else
                File.Move(tempPath, path);
        }
        #endregion

        #region INI Helpers

        /// <summary>
        /// Reads a small INI file into section.key values.
        ///
        /// Example:
        /// [General]
        /// Enabled = true
        ///
        /// Becomes:
        /// General.Enabled -> true
        ///
        /// Notes:
        /// - Blank lines are ignored.
        /// - Lines starting with ';' or '#' are ignored.
        /// - This parser is intentionally simple for small plugin config/state files.
        /// </summary>
        private static Dictionary<string, string> ReadIni(string path)
        {
            var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            if (!File.Exists(path))
                return values;

            string section = string.Empty;

            foreach (string rawLine in File.ReadAllLines(path))
            {
                string line = rawLine.Trim();

                if (line.Length == 0 || line.StartsWith(";") || line.StartsWith("#"))
                    continue;

                if (line.StartsWith("[") && line.EndsWith("]"))
                {
                    section = line.Substring(1, line.Length - 2).Trim();
                    continue;
                }

                int equalsIndex = line.IndexOf('=');
                if (equalsIndex <= 0)
                    continue;

                string key = line.Substring(0, equalsIndex).Trim();
                string value = line.Substring(equalsIndex + 1).Trim();

                values[section + "." + key] = value;
            }

            return values;
        }

        /// <summary>
        /// Reads a boolean INI value, returning the fallback when the key is missing or invalid.
        /// </summary>
        private static bool GetBool(Dictionary<string, string> values, string key, bool fallback)
        {
            return values.TryGetValue(key, out string value) && bool.TryParse(value, out bool parsed)
                ? parsed
                : fallback;
        }

        /// <summary>
        /// Reads an integer INI value, returning the fallback when the key is missing or invalid.
        /// </summary>
        private static int GetInt(Dictionary<string, string> values, string key, int fallback)
        {
            return values.TryGetValue(key, out string value) && int.TryParse(value, out int parsed)
                ? parsed
                : fallback;
        }

        /// <summary>
        /// Clamps a numeric value between a minimum and maximum value.
        /// </summary>
        private static int Clamp(int value, int min, int max)
        {
            if (value < min)
                return min;

            if (value > max)
                return max;

            return value;
        }
        #endregion

        #region Helpers

        /// <summary>
        /// Returns true when the saved/restored time value is safe to apply.
        ///
        /// Notes:
        /// - Rejects NaN and Infinity.
        /// - Rejects negative time.
        /// - Uses a high upper limit to avoid obviously corrupt state values.
        /// </summary>
        private static bool IsValidTimeOfDay(float value)
        {
            if (float.IsNaN(value) || float.IsInfinity(value))
                return false;

            return value >= 0f && value < 10000000f;
        }

        /// <summary>
        /// Returns a filesystem-safe world key for per-world time state.
        ///
        /// Notes:
        /// - Uses DefaultWorld when no world key is available.
        /// - Replaces invalid filename characters so the key can safely be used as a folder name.
        /// </summary>
        private static string SanitizeWorldKey(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return "DefaultWorld";

            string key = value.Trim();

            foreach (char invalid in Path.GetInvalidFileNameChars())
                key = key.Replace(invalid, '_');

            key = key.Replace('\\', '_').Replace('/', '_');

            return string.IsNullOrWhiteSpace(key) ? "DefaultWorld" : key;
        }

        /// <summary>
        /// Returns the player-facing day label for logs.
        ///
        /// Notes:
        /// - Internal time starts at 0.
        /// - Player-facing days start at Day 1.
        /// </summary>
        private static string FormatDisplayDay(float timeOfDay)
        {
            int day = Math.Max(1, (int)Math.Floor(timeOfDay) + 1);
            return "Day " + day.ToString(CultureInfo.InvariantCulture);
        }
        #endregion

        #region Nested Types

        /// <summary>
        /// Runtime configuration for RememberTime.
        /// </summary>
        private sealed class RememberTimeConfig
        {
            /// <summary>
            /// Enables or disables the RememberTime plugin.
            /// </summary>
            public bool Enabled = true;

            /// <summary>
            /// Number of seconds between interval saves.
            /// </summary>
            public int SaveIntervalSeconds = 60;

            /// <summary>
            /// When true, the plugin restores the saved time during server startup.
            /// </summary>
            public bool RestoreOnStartup = true;

            /// <summary>
            /// When true, the plugin writes one final time during clean server shutdown.
            /// </summary>
            public bool SaveOnShutdown = true;
        }
        #endregion
    }
}