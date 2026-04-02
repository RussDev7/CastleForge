/*
SPDX-License-Identifier: GPL-3.0-or-later
Copyright (c) 2025 RussDev7
This file is part of https://github.com/RussDev7/CastleForge - see LICENSE for details.
*/

using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System;

using static TreeFeller.GamePatches;
using static ModLoader.LogSystem;

namespace TreeFeller
{
    /// <summary>
    /// Runtime knobs used by TreeFeller patches / simulation.
    /// </summary>
    internal static class TreeFeller_Settings
    {
        public static volatile bool Enabled            = true;
        public static bool          RequireNaturalTree = true;

        public static int MaxTraversalCells     = 512;
        public static int MaxBlocksToRemove     = 384;
        public static int MaxHorizontalRadius   = 6;
        public static int MaxVerticalSearchUp   = 20;
        public static int MaxVerticalSearchDown = 3;
        public static int MinRemainingLogs      = 2;
        public static int MinLeafCount          = 6;
        public static int MinLeavesNearCanopy   = 4;

        public static volatile bool DoAnnouncement = false;
        public static volatile bool DoLogging      = false;
    }

    /// <summary>
    /// INI-backed config for TreeFeller.
    /// </summary>
    internal sealed class TFConfig
    {
        // Last applied config snapshot (set by LoadApply) for fast runtime reads without disk I/O.
        internal static volatile TFConfig Active;

        // [TreeFeller].
        public bool Enabled            = true;
        public bool RequireNaturalTree = true;

        // [Safety].
        public int MaxTraversalCells     = 512;
        public int MaxBlocksToRemove     = 384;
        public int MaxHorizontalRadius   = 6;
        public int MaxVerticalSearchUp   = 20;
        public int MaxVerticalSearchDown = 3;

        // [Heuristics].
        public int MinRemainingLogs    = 2;
        public int MinLeafCount        = 6;
        public int MinLeavesNearCanopy = 4;

        // [Logging].
        public bool DoAnnouncement = false;
        public bool DoLogging      = false;

        // [Hotkeys].
        // Hotkey to reload this config at runtime.
        public string ReloadConfigHotkey = "Ctrl+Shift+R";

        // Paths.
        public static string ConfigPath =>
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "!Mods", "TreeFeller", "TreeFeller.Config.ini");

        public static TFConfig LoadOrCreate()
        {
            string dir = Path.GetDirectoryName(ConfigPath);
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

            if (!File.Exists(ConfigPath))
            {
                File.WriteAllLines(ConfigPath, new[]
                {
                    "# TreeFeller - Configuration",
                    "# Lines starting with ';' or '#' are comments.",
                    "",
                    "[TreeFeller]",
                    "; Master toggle for the entire mod.",
                    "Enabled            = true",
                    "; If true, only fell structures that look like natural trees (logs + canopy leaves).",
                    "RequireNaturalTree = true",
                    "",
                    "[Safety]",
                    "; Maximum number of cells the bounded flood-fill may visit.",
                    "MaxTraversalCells     = 512",
                    "; Hard cap on extra blocks removed after the first chopped log.",
                    "MaxBlocksToRemove     = 384",
                    "; Horizontal scan radius from the chopped log.",
                    "MaxHorizontalRadius   = 6",
                    "; Maximum upward scan distance from the chopped log.",
                    "MaxVerticalSearchUp   = 20",
                    "; Maximum downward scan distance from the chopped log.",
                    "MaxVerticalSearchDown = 3",
                    "",
                    "[Heuristics]",
                    "; Minimum connected remaining log blocks required to count as a tree.",
                    "MinRemainingLogs    = 2",
                    "; Minimum connected leaves required to count as a tree canopy.",
                    "MinLeafCount        = 6",
                    "; Minimum canopy leaves near the top of the trunk.",
                    "MinLeavesNearCanopy = 4",
                    "",
                    "[Logging]",
                    "; Show an in-game announcement to the player.",
                    "DoAnnouncement = false",
                    "; Write the action to the log.",
                    "DoLogging      = false",
                    "",
                    "[Hotkeys]",
                    "; Reload this config while in-game:",
                    "ReloadConfig = Ctrl+Shift+R",
                });
            }

            var ini = SimpleIni.Load(ConfigPath);
            var cfg = new TFConfig
            {
                // [TreeFeller].
                Enabled            = ini.GetBool("TreeFeller", "Enabled", true),
                RequireNaturalTree = ini.GetBool("TreeFeller", "RequireNaturalTree", true),

                // [Safety].
                MaxTraversalCells     = Clamp(ini.GetInt("Safety", "MaxTraversalCells", 512), 32, 8192),
                MaxBlocksToRemove     = Clamp(ini.GetInt("Safety", "MaxBlocksToRemove", 384), 1, 4096),
                MaxHorizontalRadius   = Clamp(ini.GetInt("Safety", "MaxHorizontalRadius", 6), 1, 64),
                MaxVerticalSearchUp   = Clamp(ini.GetInt("Safety", "MaxVerticalSearchUp", 20), 1, 128),
                MaxVerticalSearchDown = Clamp(ini.GetInt("Safety", "MaxVerticalSearchDown", 3), 0, 64),

                // [Heuristics].
                MinRemainingLogs    = Clamp(ini.GetInt("Heuristics", "MinRemainingLogs", 2), 0, 128),
                MinLeafCount        = Clamp(ini.GetInt("Heuristics", "MinLeafCount", 6), 0, 512),
                MinLeavesNearCanopy = Clamp(ini.GetInt("Heuristics", "MinLeavesNearCanopy", 4), 0, 512),

                // [Logging].
                DoAnnouncement = ini.GetBool("Logging", "DoAnnouncement", false),
                DoLogging      = ini.GetBool("Logging", "DoLogging", false),

                // [Hotkeys].
                ReloadConfigHotkey = ini.GetString("Hotkeys", "ReloadConfig", "Ctrl+Shift+R"),
            };
            return cfg;
        }

        public static void LoadApply()
        {
            try
            {
                TFConfig cfg = LoadOrCreate();
                Active = cfg;
                cfg.ApplyToStatics();
                TFHotkeys.SetReloadBinding(cfg.ReloadConfigHotkey);
                // Log($"Applied from {ConfigPath}.");
            }
            catch (Exception ex)
            {
                Log($"Failed to load/apply: {ex.Message}.");
            }
        }

        public void ApplyToStatics()
        {
            TreeFeller_Settings.Enabled            = Enabled;
            TreeFeller_Settings.RequireNaturalTree = RequireNaturalTree;

            TreeFeller_Settings.MaxTraversalCells     = MaxTraversalCells;
            TreeFeller_Settings.MaxBlocksToRemove     = MaxBlocksToRemove;
            TreeFeller_Settings.MaxHorizontalRadius   = MaxHorizontalRadius;
            TreeFeller_Settings.MaxVerticalSearchUp   = MaxVerticalSearchUp;
            TreeFeller_Settings.MaxVerticalSearchDown = MaxVerticalSearchDown;

            TreeFeller_Settings.MinRemainingLogs    = MinRemainingLogs;
            TreeFeller_Settings.MinLeafCount        = MinLeafCount;
            TreeFeller_Settings.MinLeavesNearCanopy = MinLeavesNearCanopy;

            TreeFeller_Settings.DoAnnouncement = DoAnnouncement;
            TreeFeller_Settings.DoLogging      = DoLogging;
        }

        private static int Clamp(int v, int lo, int hi) => (v < lo) ? lo : (v > hi ? hi : v);
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