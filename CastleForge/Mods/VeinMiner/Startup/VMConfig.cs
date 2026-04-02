/*
SPDX-License-Identifier: GPL-3.0-or-later
Copyright (c) 2025 RussDev7
This file is part of https://github.com/RussDev7/CastleForge - see LICENSE for details.
*/

using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System;

using static VeinMiner.GamePatches;
using static ModLoader.LogSystem;

namespace VeinMiner
{
    /// <summary>
    /// Runtime knobs used by VeinMiner patches / simulation.
    /// </summary>
    internal static class VeinMiner_Settings
    {
        public static volatile bool Enabled = true;

        public static int MaxTraversalCells = 512;
        public static int MaxBlocksToMine   = 384;
        public static int MaxAxisRadius     = 24;

        public static bool MineGoldOre    = true;
        public static bool MineIronOre    = true;
        public static bool MineCopperOre  = true;
        public static bool MineCoalOre    = true;
        public static bool MineDiamondOre = true;
        public static bool MineSlime      = true;

        public static volatile bool DoAnnouncement = false;
        public static volatile bool DoLogging      = false;
    }

    /// <summary>
    /// INI-backed config for VeinMiner.
    /// </summary>
    internal sealed class VMConfig
    {
        // Last applied config snapshot (set by LoadApply) for fast runtime reads without disk I/O.
        internal static volatile VMConfig Active;

        // [VeinMiner].
        public bool Enabled = true;

        // [Safety].
        public int MaxTraversalCells = 512;
        public int MaxBlocksToMine   = 384;
        public int MaxAxisRadius     = 24;

        // [Ores].
        public bool MineGoldOre    = true;
        public bool MineIronOre    = true;
        public bool MineCopperOre  = true;
        public bool MineCoalOre    = true;
        public bool MineDiamondOre = true;
        public bool MineSlime      = true;

        // [Logging].
        public bool DoAnnouncement = false;
        public bool DoLogging      = false;

        // [Hotkeys].
        // Hotkey to reload this config at runtime.
        public string ReloadConfigHotkey = "Ctrl+Shift+R";

        // Paths.
        public static string ConfigPath =>
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "!Mods", "VeinMiner", "VeinMiner.Config.ini");

        public static VMConfig LoadOrCreate()
        {
            string dir = Path.GetDirectoryName(ConfigPath);
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

            if (!File.Exists(ConfigPath))
            {
                File.WriteAllLines(ConfigPath, new[]
                {
                    "# VeinMiner - Configuration",
                    "# Lines starting with ';' or '#' are comments.",
                    "",
                    "[VeinMiner]",
                    "; Master toggle for the entire mod.",
                    "Enabled = true",
                    "",
                    "[Safety]",
                    "; Maximum number of cells the bounded flood-fill may visit.",
                    "MaxTraversalCells = 512",
                    "; Hard cap on extra ore/resource blocks mined after the first block.",
                    "MaxBlocksToMine   = 384",
                    "; Maximum axis distance from the originally mined ore block.",
                    "MaxAxisRadius     = 24",
                    "",
                    "[Ores]",
                    "; Toggle specific ore/resource types on or off.",
                    "MineGoldOre    = true",
                    "MineIronOre    = true",
                    "MineCopperOre  = true",
                    "MineCoalOre    = true",
                    "MineDiamondOre = true",
                    "MineSlime      = true",
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
            var cfg = new VMConfig
            {
                // [VeinMiner].
                Enabled = ini.GetBool("VeinMiner", "Enabled", true),

                // [Safety].
                MaxTraversalCells = Clamp(ini.GetInt("Safety", "MaxTraversalCells", 512), 32, 8192),
                MaxBlocksToMine   = Clamp(ini.GetInt("Safety", "MaxBlocksToMine", 384), 1, 4096),
                MaxAxisRadius     = Clamp(ini.GetInt("Safety", "MaxAxisRadius", 24), 1, 128),

                // [Ores].
                MineGoldOre    = ini.GetBool("Ores", "MineGoldOre", true),
                MineIronOre    = ini.GetBool("Ores", "MineIronOre", true),
                MineCopperOre  = ini.GetBool("Ores", "MineCopperOre", true),
                MineCoalOre    = ini.GetBool("Ores", "MineCoalOre", true),
                MineDiamondOre = ini.GetBool("Ores", "MineDiamondOre", true),
                MineSlime      = ini.GetBool("Ores", "MineSlime", true),

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
                VMConfig cfg = LoadOrCreate();
                Active = cfg;
                cfg.ApplyToStatics();
                VMHotkeys.SetReloadBinding(cfg.ReloadConfigHotkey);
                // Log($"Applied from {ConfigPath}.");
            }
            catch (Exception ex)
            {
                Log($"Failed to load/apply: {ex.Message}.");
            }
        }

        public void ApplyToStatics()
        {
            VeinMiner_Settings.Enabled = Enabled;

            VeinMiner_Settings.MaxTraversalCells = MaxTraversalCells;
            VeinMiner_Settings.MaxBlocksToMine   = MaxBlocksToMine;
            VeinMiner_Settings.MaxAxisRadius     = MaxAxisRadius;

            VeinMiner_Settings.MineGoldOre    = MineGoldOre;
            VeinMiner_Settings.MineIronOre    = MineIronOre;
            VeinMiner_Settings.MineCopperOre  = MineCopperOre;
            VeinMiner_Settings.MineCoalOre    = MineCoalOre;
            VeinMiner_Settings.MineDiamondOre = MineDiamondOre;
            VeinMiner_Settings.MineSlime      = MineSlime;

            VeinMiner_Settings.DoAnnouncement = DoAnnouncement;
            VeinMiner_Settings.DoLogging      = DoLogging;
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