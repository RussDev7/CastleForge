/*
SPDX-License-Identifier: GPL-3.0-or-later
Copyright (c) 2025 RussDev7
This file is part of https://github.com/RussDev7/CastleForge - see LICENSE for details.
*/

using System.Collections.Generic;
using System.Globalization;
using System.Windows.Forms;
using System.IO;
using System;

using static WorldEditPixelart.GamePatches;

namespace WorldEditPixelart
{
    /// <summary>
    /// Global single-source-of-truth for config values that the ENTIRE mod consumes.
    /// Keep this minimal and readonly-at-runtime (set once when the mod starts).
    /// </summary>
    internal static class ConfigGlobals
    {
        public static Keys? ToggleKey   = Keys.None;
        public static bool EmbedAsChild = true;
    }

    /// <summary>
    /// Simple POCO for mod configuration + loader that writes a default INI on first run.
    /// </summary>
    internal sealed class WEPConfig
    {
        // Last applied config snapshot (set by LoadApply) for fast runtime reads without disk I/O.
        internal static volatile WEPConfig Active;

        // --- Hotkeys ---
        public Keys? ToggleKey = null;
        public string ReloadConfigHotkey = "Ctrl+Shift+R";

        // --- Behavior ---
        public bool EmbedAsChild = true;

        // Example: <game>\!Mods\MyModNamespace\MyMod.Config.ini.
        public static string ConfigPath =>
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "!Mods", typeof(WEPConfig).Namespace, "WorldEditPixelart.ini");

        /// <summary>
        /// Creates the config directory and default INI if missing,
        /// then parses the current file into a WEPConfig instance.
        /// Safe to call multiple times (idempotent).
        /// </summary>
        public static WEPConfig LoadOrCreate()
        {
            // Ensure folder.
            var dir = Path.GetDirectoryName(ConfigPath);
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

            // Write default file once.
            if (!File.Exists(ConfigPath))
            {
                var lines = new[]
                {
                    $@"; WorldEditPixelart - Configuration",
                    $@"; Lines starting with ';' or '#' are comments.",
                    $@"",
                    $@"[Hotkeys]",
                    $@"; ToggleKey opens the editor and freezes input; pressing it again in the editor closes/unfreezes.",
                    $@"; Set to 'None' to disable the hotkey entirely.",
                    $@"; Examples: F10, F4, Insert, Delete, OemTilde, None",
                    $@"ToggleKey=None",
                    $@"; Reload this config while in-game:",
                    $@"ReloadConfig=Ctrl+Shift+R",
                    $@"",
                    $@"[Behavior]",
                    $@"; Overlay host mode:",
                    $@";   true  = Embed inside the game window (child mode).",
                    $@";           - Lives within the game's client area; can't be dragged outside.",
                    $@";           - No taskbar entry; z-order follows the game.",
                    $@";           - Best for windowed/borderless; avoids focus flicker.",
                    $@";   false = Show as a separate window (owned by the game).",
                    $@";           - Standard title bar + icon; can move to other monitors.",
                    $@";           - May briefly steal/return focus when opened/closed.",
                    $@";           - Use if you want a normal, freely movable window.",
                    $@"EmbedAsChild=true",
                };

                // Write a default file.
                File.WriteAllLines(ConfigPath, lines);
            }

            // Parse INI -> object.
            var ini = SimpleIni.Load(ConfigPath);
            var c = new WEPConfig();

            // [Hotkeys].

            // NOTE: Keep key names consistent with the default file ("ToggleKey").
            // Null => disabled.
            c.ToggleKey = ParseKey(ini.GetString("Hotkeys", "ToggleKey", c.ToggleKey.ToString()));
            c.ReloadConfigHotkey = ini.GetString("Hotkeys", "ReloadConfig", "Ctrl+Shift+R");

            // [Behavior].
            c.EmbedAsChild = ini.GetBool("Behavior", "EmbedAsChild", c.EmbedAsChild);

            return c;
        }

        public static void LoadApply()
        {
            try
            {
                bool oldEmbed = ConfigGlobals.EmbedAsChild;

                var cfg = LoadOrCreate();
                Active = cfg;

                ConfigGlobals.ToggleKey = cfg.ToggleKey;
                ConfigGlobals.EmbedAsChild = cfg.EmbedAsChild;

                WEPHotkeys.SetReloadBinding(cfg.ReloadConfigHotkey);

                if (oldEmbed != cfg.EmbedAsChild)
                    WinFormsOverlay.RebuildFromConfig();
                // ModLoader.LogSystem.Log($"[Config] Applied from {PathShortener.ShortenForLog(ConfigPath)}.");
            }
            catch (Exception ex)
            {
                ModLoader.LogSystem.Log($"[WEConfig] Failed to load/apply: {ex.Message}.");
            }
        }

        private static Keys? ParseKey(string s)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(s)) return null;

                var v = s.Trim();
                if (v.Equals("none", StringComparison.OrdinalIgnoreCase) ||
                    v.Equals("off", StringComparison.OrdinalIgnoreCase) ||
                    v.Equals("disabled", StringComparison.OrdinalIgnoreCase) ||
                    v.Equals("null", StringComparison.OrdinalIgnoreCase))
                    return null;

                return Enum.TryParse<Keys>(v, true, out var key) ? key : (Keys?)null;
            }
            catch { return null; }
        }
    }

    /// <summary>
    /// Tiny, case-insensitive INI reader.
    /// Supports [Section], key=value, ';' or '#' comments. No escaping, no multi-line.
    /// </summary>
    internal sealed class SimpleIni
    {
        private readonly Dictionary<string, Dictionary<string, string>> _data =
            new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Loads an INI file from disk into a simple nested dictionary:
        ///   section -> (key -> value).
        /// Unknown / malformed lines are ignored.
        /// </summary>
        public static SimpleIni Load(string path)
        {
            var ini = new SimpleIni();
            string section = "";

            foreach (var raw in File.ReadAllLines(path))
            {
                var line = raw.Trim();
                if (line.Length == 0) continue;
                if (line.StartsWith(";") || line.StartsWith("#")) continue;

                // Section header: [SectionName].
                if (line.StartsWith("[") && line.EndsWith("]"))
                {
                    section = line.Substring(1, line.Length - 2).Trim();
                    if (!ini._data.ContainsKey(section))
                        ini._data[section] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    continue;
                }

                // Key/value pair: key = value.
                int eq = line.IndexOf('=');
                if (eq <= 0) continue;

                string key = line.Substring(0, eq).Trim();
                string val = line.Substring(eq + 1).Trim();

                if (!ini._data.TryGetValue(section, out var dict))
                {
                    dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    ini._data[section] = dict;
                }
                dict[key] = val;
            }

            return ini;
        }

        /// <summary>
        /// Reads an int from the INI and clamps it to the inclusive range [min..max].
        /// Returns <paramref name="def"/> if missing/invalid before clamping.
        /// </summary>
        public int GetClamp(string sec, string key, int def, int min, int max)
        {
            var v = GetInt(sec, key, def);
            if (v < min) v = min;
            if (v > max) v = max;
            return v;
        }

        /// <summary>
        /// Reads a string value from [section] key=... and returns <paramref name="def"/> if missing.
        /// </summary>
        public string GetString(string section, string key, string def)
            => (_data.TryGetValue(section, out var d) && d.TryGetValue(key, out var v)) ? v : def;

        /// <summary>
        /// Reads an int value from [section] key=... using invariant culture; returns <paramref name="def"/> on failure.
        /// </summary>
        public int GetInt(string section, string key, int def)
            => int.TryParse(GetString(section, key, def.ToString(CultureInfo.InvariantCulture)), NumberStyles.Integer,
                            CultureInfo.InvariantCulture, out var v) ? v : def;

        /// <summary>
        /// Reads a double value from [section] key=... using invariant culture; returns <paramref name="def"/> on failure.
        /// </summary>
        public double GetDouble(string section, string key, double def)
            => double.TryParse(GetString(section, key, def.ToString(CultureInfo.InvariantCulture)),
                               NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : def;

        /// <summary>
        /// Reads a double value from [section] key=... using invariant culture; returns <paramref name="def"/> on failure.
        /// </summary>
        public bool GetBool(string section, string key, bool def)
        {
            var s = GetString(section, key, def ? "true" : "false");
            if (bool.TryParse(s, out var b)) return b;
            if (int.TryParse(s, out var i)) return i != 0;
            return def;
        }
    }
}