/*
SPDX-License-Identifier: GPL-3.0-or-later
Copyright (c) 2025 RussDev7
This file is part of https://github.com/RussDev7/CastleForge - see LICENSE for details.
*/

using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.IO;
using System;

namespace CastleWallsMk2
{
    /// <summary>
    /// Separate persisted store for remembered gameplay checkbox states.
    ///
    /// Why separate?
    /// - Keeps the main mod config small/readable.
    /// - Prevents the out-of-game reset path from overwriting the last in-game snapshot.
    /// - Lets the UI restore a deferred snapshot only when returning to a live game.
    /// </summary>
    internal static class RememberedToggleStore
    {
        private static readonly Dictionary<string, bool> _toggles =
            new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);

        private static readonly Dictionary<string, int> _intSliders =
            new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        private static readonly Dictionary<string, float> _floatSliders =
            new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);

        private static bool _loaded;
        private static bool _rememberEnabled;

        /// <summary>
        /// Full path to the remembered-toggle INI:
        /// !Mods/<Namespace>/<Namespace>.RememberedToggles.ini
        /// </summary>
        public static string ConfigPath =>
            Path.Combine(ModConfig.FolderPath, $"{typeof(EmbeddedResolver).Namespace}.RememberedToggles.ini");

        /// <summary>
        /// Master on/off flag for restoring checkbox states across session reloads.
        /// Stored inside this same dedicated INI.
        /// </summary>
        public static bool RememberEnabled
        {
            get
            {
                EnsureLoaded();
                return _rememberEnabled;
            }
            set
            {
                EnsureLoaded();
                if (_rememberEnabled == value) return;

                _rememberEnabled = value;
                Save();
            }
        }

        /// <summary>
        /// Reads one remembered checkbox value by UI field name.
        /// </summary>
        public static bool TryGetToggle(string key, out bool value)
        {
            EnsureLoaded();
            return _toggles.TryGetValue(key, out value);
        }

        /// <summary>
        /// Updates one remembered checkbox value in memory.
        /// Call <see cref="Save"/> to flush it to disk.
        /// </summary>
        public static void SetToggle(string key, bool value)
        {
            EnsureLoaded();
            _toggles[key] = value;
        }

        /// <summary>
        /// Reads one remembered int slider value by UI field name.
        /// </summary>
        public static bool TryGetInt(string key, out int value)
        {
            EnsureLoaded();
            return _intSliders.TryGetValue(key, out value);
        }

        /// <summary>
        /// Updates one remembered int slider value in memory.
        /// Call <see cref="Save"/> to flush it to disk.
        /// </summary>
        public static void SetInt(string key, int value)
        {
            EnsureLoaded();
            _intSliders[key] = value;
        }

        /// <summary>
        /// Reads one remembered float slider value by UI field name.
        /// </summary>
        public static bool TryGetFloat(string key, out float value)
        {
            EnsureLoaded();
            return _floatSliders.TryGetValue(key, out value);
        }

        /// <summary>
        /// Updates one remembered float slider value in memory.
        /// Call <see cref="Save"/> to flush it to disk.
        /// </summary>
        public static void SetFloat(string key, float value)
        {
            EnsureLoaded();
            _floatSliders[key] = value;
        }

        /// <summary>
        /// Writes the dedicated remembered-state INI to disk.
        /// Non-fatal if I/O fails.
        ///
        /// Sections:
        /// - [Session]      : Master on/off flag.
        /// - [Toggles]      : Remembered checkbox states.
        /// - [IntSliders]   : Remembered integer slider values.
        /// - [FloatSliders] : Remembered float slider values.
        /// </summary>
        public static void Save()
        {
            EnsureLoaded();

            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(ConfigPath));

                var lines = new List<string>
                {
                    "# ================================================================================",
                    $"# {typeof(RememberedToggleStore).Namespace} - Remembered Session Snapshot",
                    "# Lines beginning with ';' or '#' are comments.",
                    "#",
                    "# [Session]",
                    "#   RememberToggleStatesOnReload = true|false",
                    "#     - true  -> Saves remembered gameplay values and restores them on the next",
                    "#                game/session load.",
                    "#     - false -> Disables automatic restore, but keeps the last snapshot.",
                    "#",
                    "# [Toggles]",
                    "#   Keys are private/public IGMainUI bool field names on purpose so restores stay",
                    "#   stable even if the visible label text changes later.",
                    "#",
                    "# [IntSliders] / [FloatSliders]",
                    "#   Keys are private/public IGMainUI slider backing field names.",
                    "#   Values are persisted using invariant culture formatting.",
                    "#",
                    $"# Saved: {DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss 'UTC'", CultureInfo.InvariantCulture)}",
                    "# ================================================================================",
                    "",
                    "[Session]",
                    $"RememberToggleStatesOnReload={(_rememberEnabled ? "true" : "false")}",
                    "",
                    "[Toggles]",
                };

                foreach (var kv in _toggles.OrderBy(k => k.Key, StringComparer.OrdinalIgnoreCase))
                    lines.Add($"{kv.Key}={(kv.Value ? "true" : "false")}");

                lines.Add("");
                lines.Add("[IntSliders]");

                foreach (var kv in _intSliders.OrderBy(k => k.Key, StringComparer.OrdinalIgnoreCase))
                    lines.Add($"{kv.Key}={kv.Value.ToString(CultureInfo.InvariantCulture)}");

                lines.Add("");
                lines.Add("[FloatSliders]");

                foreach (var kv in _floatSliders.OrderBy(k => k.Key, StringComparer.OrdinalIgnoreCase))
                    lines.Add($"{kv.Key}={kv.Value.ToString(CultureInfo.InvariantCulture)}");

                File.WriteAllLines(ConfigPath, lines);
            }
            catch
            {
                // Non-fatal.
            }
        }

        /// <summary>
        /// Loads the remembered-state INI exactly once per process.
        /// Missing file is treated as defaults and immediately written out.
        /// </summary>
        private static void EnsureLoaded()
        {
            if (_loaded) return;
            _loaded = true;

            _toggles.Clear();
            _intSliders.Clear();
            _floatSliders.Clear();
            _rememberEnabled = false;

            if (!File.Exists(ConfigPath))
            {
                Save();
                return;
            }

            try
            {
                string section = "";

                foreach (var raw in File.ReadAllLines(ConfigPath))
                {
                    var line = raw.Trim();
                    if (line.Length == 0) continue;
                    if (line.StartsWith(";") || line.StartsWith("#")) continue;

                    if (line.StartsWith("[") && line.EndsWith("]"))
                    {
                        section = line.Substring(1, line.Length - 2).Trim();
                        continue;
                    }

                    int eq = line.IndexOf('=');
                    if (eq <= 0) continue;

                    string key = line.Substring(0, eq).Trim();
                    string val = line.Substring(eq + 1).Trim();

                    if (section.Equals("Session", StringComparison.OrdinalIgnoreCase))
                    {
                        if (key.Equals("RememberToggleStatesOnReload", StringComparison.OrdinalIgnoreCase))
                            _rememberEnabled = string.Equals(val, "true", StringComparison.OrdinalIgnoreCase);

                        continue;
                    }

                    if (section.Equals("Toggles", StringComparison.OrdinalIgnoreCase))
                    {
                        _toggles[key] = string.Equals(val, "true", StringComparison.OrdinalIgnoreCase);
                        continue;
                    }

                    if (section.Equals("IntSliders", StringComparison.OrdinalIgnoreCase))
                    {
                        if (int.TryParse(val, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsedInt))
                            _intSliders[key] = parsedInt;

                        continue;
                    }

                    if (section.Equals("FloatSliders", StringComparison.OrdinalIgnoreCase))
                    {
                        if (float.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out float parsedFloat))
                            _floatSliders[key] = parsedFloat;

                        continue;
                    }
                }
            }
            catch
            {
                // Fall back to defaults. Keep the file non-fatal.
                _toggles.Clear();
                _intSliders.Clear();
                _floatSliders.Clear();
                _rememberEnabled = false;
            }
        }
    }
}