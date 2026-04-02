/*
SPDX-License-Identifier: GPL-3.0-or-later
Copyright (c) 2025 RussDev7
This file is part of https://github.com/RussDev7/CastleForge - see LICENSE for details.
*/

using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System;

using static RegionProtect.GamePatches;
using static ModLoader.LogSystem;

namespace RegionProtect
{
    /// <summary>
    /// INI-backed config for RegionProtect.
    /// Summary: Small runtime knobs. Region definitions live in RegionProtect.Regions.ini.
    /// </summary>
    internal sealed class RPConfig
    {
        internal static volatile RPConfig Active;

        /// [General]

        public bool Enabled              = true;

        // If true, deny digging (block->Empty) inside protected areas unless whitelisted.
        public bool ProtectMining        = true;

        // If true, also deny block placement / replacement inside protected areas unless whitelisted.
        public bool ProtectPlacing       = true;

        // If true, deny taking/placing items in crates inside protected areas.
        public bool ProtectCrateItems    = true;

        // If true, deny destroying crates inside protected areas.
        public bool ProtectCrateMining   = true;

        // If true, only enforce on host (recommended). If false, clients will also deny locally (but still no sender authority).
        public bool EnforceHostOnly      = false;

        // Deny-message spam throttle (ms).
        public int DenyNotifyCooldownMs  = 1200;

        // If true, send private messages to the offender on deny.
        public bool NotifyDeniedPlayer   = true;

        // If true, log denials to console.
        public bool LogDenied            = false;

        /// [Hotkeys]

        // Hotkey to reload this config at runtime.
        public string ReloadConfigHotkey = "Ctrl+Shift+R";

        public static string ConfigPath =>
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "!Mods", "RegionProtect", "RegionProtect.Config.ini");

        public static RPConfig LoadOrCreate()
        {
            var dir = Path.GetDirectoryName(ConfigPath);
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

            if (!File.Exists(ConfigPath))
            {
                File.WriteAllLines(ConfigPath, new[]
                {
                    "# RegionProtect - Configuration",
                    "# Lines starting with ';' or '#' are comments.",
                    "",
                    "[General]",
                    "Enabled              = true",
                    "; If true, deny digging (block -> Empty) in protected areas unless whitelisted.",
                    "ProtectMining        = true",
                    "; If true, also deny placing/replacing blocks in protected areas unless whitelisted.",
                    "ProtectPlacing       = true",
                    "; If true, deny taking/placing items in crates inside protected areas.",
                    "ProtectCrateItems    = true",
                    "; If true, deny destroying crates (mined/exploded) inside protected areas.",
                    "ProtectCrateMining   = true",
                    "; If true, only enforce on host (recommended).",
                    "EnforceHostOnly      = false",
                    "; Private message throttle (ms) per player to avoid chat spam.",
                    "DenyNotifyCooldownMs = 1200",
                    "; If true, send a private deny message to the offender.",
                    "NotifyDeniedPlayer   = true",
                    "; If true, log deny events to the console.",
                    "LogDenied            = false",
                    "",
                    "[Hotkeys]",
                    "; Reload this config while in-game:",
                    "ReloadConfig         = Ctrl+Shift+R",
                });
            }

            var ini = SimpleIni.Load(ConfigPath);
            var cfg = new RPConfig
            {
                /// [General]
                Enabled              = ini.GetBool  ("General",  "Enabled",            true),
                ProtectMining        = ini.GetBool  ("General",  "ProtectMining",      true),
                ProtectPlacing       = ini.GetBool  ("General",  "ProtectPlacing",     false),
                ProtectCrateItems    = ini.GetBool  ("General",  "ProtectCrateItems",  true),
                ProtectCrateMining   = ini.GetBool  ("General",  "ProtectCrateMining", true),
                EnforceHostOnly      = ini.GetBool  ("General",  "EnforceHostOnly",    true),
                DenyNotifyCooldownMs = ini.GetClamp ("General",  "DenyNotifyCooldownMs", 1200, 0, 60000),
                NotifyDeniedPlayer   = ini.GetBool  ("General",  "NotifyDeniedPlayer", true),
                LogDenied            = ini.GetBool  ("General",  "LogDenied",          false),

                /// [Hotkeys]
                ReloadConfigHotkey   = ini.GetString("Hotkeys",  "ReloadConfig",       "Ctrl+Shift+R"),
            };

            return cfg;
        }

        public static void LoadApply()
        {
            try
            {
                var cfg = LoadOrCreate();
                Active = cfg;
                RegionProtectCore.ApplyConfig(cfg);
                RPHotkeys.SetReloadBinding(cfg.ReloadConfigHotkey);
            }
            catch (Exception ex)
            {
                Log($"[RPtConfig] Failed to load/apply: {ex.Message}.");
            }
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