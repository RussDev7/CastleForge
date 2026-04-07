/*
SPDX-License-Identifier: GPL-3.0-or-later
Copyright (c) 2025 RussDev7
This file is part of https://github.com/RussDev7/CastleForge - see LICENSE for details.
*/

using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System;

using static PhysicsEngine.GamePatches;
using static ModLoader.LogSystem;

namespace PhysicsEngine
{
    /// <summary>
    /// Runtime knobs used by PhysicsEngine patches / simulation.
    /// </summary>
    internal static class PhysicsEngine_Settings
    {
        public static volatile bool Enabled = true;

        public static int StepIntervalMs            = 125;
        public static int MaxCellEvaluationsPerStep = 256;
        public static int MaxBlockWritesPerStep     = 12;
        public static int MaxOwnedCells             = 16384;
        public static int MaxHorizontalFlowDistance = 5;

        public static volatile bool KeepLavaAlive             = false;
        public static volatile bool EndSimulationWhenIdle     = true;
        public static int           RemovedCellRespawnDelayMs = 2500;

        public static volatile bool DoAnnouncement = false;
        public static volatile bool DoLogging      = false;
    }

    /// <summary>
    /// INI-backed config for PhysicsEngine.
    /// </summary>
    internal sealed class PEConfig
    {
        // Last applied config snapshot (set by LoadApply) for fast runtime reads without disk I/O.
        internal static volatile PEConfig Active;

        // [PhysicsEngine].
        public bool Enabled = true;

        // [Simulation].
        public int StepIntervalMs             = 125;
        public int MaxCellEvaluationsPerStep  = 256;
        public int MaxBlockWritesPerStep      = 12;
        public int MaxOwnedCells              = 16384;
        public int MaxHorizontalFlowDistance  = 5;
        public bool KeepLavaAlive             = false;
        public bool EndSimulationWhenIdle     = true;
        public int  RemovedCellRespawnDelayMs = 2500;

        // [Logging].
        public bool DoAnnouncement = false;
        public bool DoLogging      = false;

        // [Hotkeys].
        // Hotkey to reload this config at runtime.
        public string ReloadConfigHotkey = "Ctrl+Shift+R";

        // Paths.
        public static string ConfigPath =>
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "!Mods", "PhysicsEngine", "PhysicsEngine.Config.ini");

        public static PEConfig LoadOrCreate()
        {
            string dir = Path.GetDirectoryName(ConfigPath);
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

            if (!File.Exists(ConfigPath))
            {
                File.WriteAllLines(ConfigPath, new[]
                {
                    "# PhysicsEngine - Configuration",
                    "# Lines starting with ';' or '#' are comments.",
                    "",
                    "[PhysicsEngine]",
                    "; Master toggle for the entire mod.",
                    "Enabled = true",
                    "",
                    "[Simulation]",
                    "; Milliseconds between lava simulation steps.",
                    "StepIntervalMs            = 125",
                    "; Maximum queued cells evaluated per simulation step.",
                    "MaxCellEvaluationsPerStep = 256",
                    "; Maximum terrain writes performed per simulation step.",
                    "MaxBlockWritesPerStep     = 12",
                    "; Safety cap for all lava cells currently managed by the simulation.",
                    "MaxOwnedCells             = 16384",
                    "; Maximum flat sideways travel from a supported cell.",
                    "MaxHorizontalFlowDistance = 5",
                    "; If false, mined/removed lava cells are temporarily blocked from immediate refill.",
                    "KeepLavaAlive             = false",
                    "; How long a mined lava cell should stay empty before refill is allowed again.",
                    "RemovedCellRespawnDelayMs = 2500",
                    "; If true, a lava run is considered finished when no frontier/pending writes remain.",
                    "EndSimulationWhenIdle     = true",
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
            var cfg = new PEConfig
            {
                // [PhysicsEngine].
                Enabled = ini.GetBool("PhysicsEngine", "Enabled", true),

                // [Simulation].
                StepIntervalMs            = Clamp(ini.GetInt("Simulation", "StepIntervalMs", 125), 10, 5000),
                MaxCellEvaluationsPerStep = Clamp(ini.GetInt("Simulation", "MaxCellEvaluationsPerStep", 256), 1, 65536),
                MaxBlockWritesPerStep     = Clamp(ini.GetInt("Simulation", "MaxBlockWritesPerStep", 12), 1, 2048),
                MaxOwnedCells             = Clamp(ini.GetInt("Simulation", "MaxOwnedCells", 16384), 1, 1000000),
                MaxHorizontalFlowDistance = Clamp(ini.GetInt("Simulation", "MaxHorizontalFlowDistance", 5), 0, 256),
                KeepLavaAlive             = ini.GetBool("Simulation", "KeepLavaAlive", false),
                RemovedCellRespawnDelayMs = Clamp(ini.GetInt("Simulation", "RemovedCellRespawnDelayMs", 2500), 0, 600000),
                EndSimulationWhenIdle     = ini.GetBool("Simulation", "EndSimulationWhenIdle", true),

                // [Logging].
                DoAnnouncement = ini.GetBool("Logging", "DoAnnouncement", false),
                DoLogging = ini.GetBool("Logging", "DoLogging", false),

                // [Hotkeys].
                ReloadConfigHotkey = ini.GetString("Hotkeys", "ReloadConfig", "Ctrl+Shift+R"),
            };
            return cfg;
        }

        public static void LoadApply()
        {
            try
            {
                PEConfig cfg = LoadOrCreate();
                Active = cfg;
                cfg.ApplyToStatics();
                PEHotkeys.SetReloadBinding(cfg.ReloadConfigHotkey);
                // Log($"Applied from {ConfigPath}.");
            }
            catch (Exception ex)
            {
                Log($"Failed to load/apply: {ex.Message}.");
            }
        }

        public void ApplyToStatics()
        {
            PhysicsEngine_Settings.Enabled = Enabled;

            PhysicsEngine_Settings.StepIntervalMs            = StepIntervalMs;
            PhysicsEngine_Settings.MaxCellEvaluationsPerStep = MaxCellEvaluationsPerStep;
            PhysicsEngine_Settings.MaxBlockWritesPerStep     = MaxBlockWritesPerStep;
            PhysicsEngine_Settings.MaxOwnedCells             = MaxOwnedCells;
            PhysicsEngine_Settings.MaxHorizontalFlowDistance = MaxHorizontalFlowDistance;
            PhysicsEngine_Settings.KeepLavaAlive             = KeepLavaAlive;
            PhysicsEngine_Settings.RemovedCellRespawnDelayMs = RemovedCellRespawnDelayMs;
            PhysicsEngine_Settings.EndSimulationWhenIdle     = EndSimulationWhenIdle;

            PhysicsEngine_Settings.DoAnnouncement = DoAnnouncement;
            PhysicsEngine_Settings.DoLogging      = DoLogging;
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