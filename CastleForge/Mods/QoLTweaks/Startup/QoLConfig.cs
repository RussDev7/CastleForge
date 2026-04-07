/*
SPDX-License-Identifier: GPL-3.0-or-later
Copyright (c) 2025 RussDev7
This file is part of https://github.com/RussDev7/CMZModSuite - see LICENSE for details.
*/

using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System;

using static QoLTweaks.GamePatches;

namespace QoLTweaks
{
    #region Runtime State

    /// <summary>
    /// Live runtime values consumed by QoL patches.
    /// </summary>
    internal static class QoL_Settings
    {
        // [QoLTweaks].
        public static volatile bool Enabled                      = true;

        // [ConstructionRange].
        public static bool          EnableConstructionRangePatch = true;
        public static float         ConstructionRange            = 420f;

        // [OfflineChat].
        public static bool          EnableOfflineChat            = true;

        // [TargetedBlockLabel].
        public static bool          EnableTargetedBlockNameAndId = true;

        // [WideChatInput].
        public static bool          EnableWideChatInput          = true;

        // [ConsoleOpacity].
        public static bool          EnableConsoleOpacityFloor    = true;
        public static float         ConsoleOpacityFloorBonus     = 0.25f;

        // [TextInput].
        public static bool          EnableAllowAnyCharPlusPaste  = true;

        // [WorldHeight].
        public static bool          EnableRemoveMaxWorldHeight   = true;

        // [QoLLogging].
        public static bool          DoLogging                    = false;
    }
    #endregion

    #region Load / Create / Apply

    /// <summary>
    /// Lightweight INI-backed config container for the standalone QoL mod.
    /// </summary>
    internal sealed class QoLConfig
    {
        internal static volatile QoLConfig Active;

        // [QoLTweaks].
        public bool  Enabled                      = true;

        // [ConstructionRange].
        public bool  EnableConstructionRangePatch = true;
        public float ConstructionRange            = 420f;

        // [OfflineChat].
        public bool  EnableOfflineChat            = true;

        // [TargetedBlockLabel].
        public bool  EnableTargetedBlockNameAndId = true;

        // [WideChatInput].
        public bool  EnableWideChatInput          = true;

        // [ConsoleOpacity].
        public bool  EnableConsoleOpacityFloor    = true;
        public float ConsoleOpacityFloorBonus     = 0.25f;

        // [TextInput].
        public bool  EnableAllowAnyCharPlusPaste  = true;

        // [WorldHeight].
        public bool  EnableRemoveMaxWorldHeight   = true;

        // [QoLLogging].
        public bool  DoLogging                    = false;

        // [Hotkeys].
        public string ReloadConfigHotkey          = "Ctrl+Shift+R";

        public static string ConfigPath =>
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "!Mods", "QoLTweaks", "QoLTweaks.Config.ini");

        /// <summary>
        /// Ensures the config exists, then loads and validates the current file.
        /// </summary>
        public static QoLConfig LoadOrCreate()
        {
            var dir = Path.GetDirectoryName(ConfigPath);
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

            if (!File.Exists(ConfigPath))
            {
                File.WriteAllLines(ConfigPath, new[]
                {
                    "# QoLTweaks - Configuration",
                    "# Standalone quality-of-life feature pack.",
                    "",
                    "[QoLTweaks]",
                    "; Master toggle for the entire mod.",
                    "Enabled                      = true",
                    "",
                    "[ConstructionRange]",
                    "; Replaces the vanilla construction range with the value below.",
                    "EnableConstructionRangePatch = true",
                    "ConstructionRange            = 420",
                    "",
                    "[OfflineChat]",
                    "; Allows the chat screen to open in offline worlds.",
                    "EnableOfflineChat            = true",
                    "",
                    "[TargetedBlockLabel]",
                    "; Replaces the default targeted block text with \"Name (ID)\".",
                    "EnableTargetedBlockNameAndId = true",
                    "",
                    "[WideChatInput]",
                    "; Makes the chat input width follow the game window width.",
                    "EnableWideChatInput          = true",
                    "",
                    "[ConsoleOpacity]",
                    "; Raises the console fade floor by this bonus amount.",
                    "; 0.25 means fading messages bottom out at roughly 25% more visibility.",
                    "EnableConsoleOpacityFloor    = true",
                    "ConsoleOpacityFloorBonus     = 0.25",
                    "",
                    "[TextInput]",
                    "; Broadens accepted text input and restores Ctrl+V paste support.",
                    "EnableAllowAnyCharPlusPaste  = true",
                    "",
                    "[WorldHeight]",
                    "; Removes the vanilla upper world-height clamp on player movement.",
                    "EnableRemoveMaxWorldHeight   = true",
                    "",
                    "[QoLLogging]",
                    "; Enables optional diagnostic logging for this mod.",
                    "DoLogging                    = false",
                    "",
                    "[Hotkeys]",
                    "; Reload this config while in-game:",
                    "ReloadConfig                 = Ctrl+Shift+R",
                });
            }

            var ini = SimpleIni.Load(ConfigPath);
            var cfg = new QoLConfig
            {
                Enabled                      = ini.GetBool("QoLTweaks", "Enabled", true),
                EnableConstructionRangePatch = ini.GetBool("ConstructionRange", "EnableConstructionRangePatch", true),
                ConstructionRange            = Clamp(ini.GetFloat("ConstructionRange", "ConstructionRange", 420f), 5f, 10000f),
                EnableOfflineChat            = ini.GetBool("OfflineChat", "EnableOfflineChat", true),
                EnableTargetedBlockNameAndId = ini.GetBool("TargetedBlockLabel", "EnableTargetedBlockNameAndId", true),
                EnableWideChatInput          = ini.GetBool("WideChatInput", "EnableWideChatInput", true),
                EnableConsoleOpacityFloor    = ini.GetBool("ConsoleOpacity", "EnableConsoleOpacityFloor", true),
                ConsoleOpacityFloorBonus     = Clamp(ini.GetFloat("ConsoleOpacity", "ConsoleOpacityFloorBonus", 0.25f), 0f, 1f),
                EnableAllowAnyCharPlusPaste  = ini.GetBool("TextInput", "EnableAllowAnyCharPlusPaste", true),
                EnableRemoveMaxWorldHeight   = ini.GetBool("WorldHeight", "EnableRemoveMaxWorldHeight", true),
                DoLogging                    = ini.GetBool("QoLLogging", "DoLogging", false),
                ReloadConfigHotkey           = ini.GetString("Hotkeys", "ReloadConfig", "Ctrl+Shift+R"),
            };
            return cfg;
        }

        /// <summary>
        /// Loads the config from disk and applies it to the live runtime statics.
        /// </summary>
        public static void LoadApply()
        {
            try
            {
                var cfg = LoadOrCreate();
                Active  = cfg;
                cfg.ApplyToStatics();
                QoLHotkeys.SetReloadBinding(cfg.ReloadConfigHotkey);
            }
            catch (Exception ex)
            {
                ModLoader.LogSystem.Log($"[QoLConfig] Failed to load/apply: {ex.Message}.");
            }
        }

        /// <summary>
        /// Copies parsed config values into the live runtime statics.
        /// </summary>
        public void ApplyToStatics()
        {
            QoL_Settings.Enabled                      = Enabled;
            QoL_Settings.EnableConstructionRangePatch = EnableConstructionRangePatch;
            QoL_Settings.ConstructionRange            = ConstructionRange;
            QoL_Settings.EnableOfflineChat            = EnableOfflineChat;
            QoL_Settings.EnableTargetedBlockNameAndId = EnableTargetedBlockNameAndId;
            QoL_Settings.EnableWideChatInput          = EnableWideChatInput;
            QoL_Settings.EnableConsoleOpacityFloor    = EnableConsoleOpacityFloor;
            QoL_Settings.ConsoleOpacityFloorBonus     = ConsoleOpacityFloorBonus;
            QoL_Settings.EnableAllowAnyCharPlusPaste  = EnableAllowAnyCharPlusPaste;
            QoL_Settings.EnableRemoveMaxWorldHeight   = EnableRemoveMaxWorldHeight;
            QoL_Settings.DoLogging                    = DoLogging;
        }

        #region Helpers

        /// <summary>
        /// Clamps an integer to an inclusive range.
        /// </summary>
        public static int Clamp(int v, int lo, int hi) => (v < lo) ? lo : (v > hi ? hi : v);

        /// <summary>
        /// Clamps a float to an inclusive range.
        /// </summary>
        public static float Clamp(float v, float lo, float hi) => (v < lo) ? lo : (v > hi ? hi : v);

        /// <summary>
        /// Clamps a double to an inclusive range.
        /// </summary>
        public static double Clamp(double v, double lo, double hi) => (v < lo) ? lo : (v > hi ? hi : v);

        #endregion
    }
    #endregion

    #region SimpleIni

    /// <summary>
    /// Tiny case-insensitive INI reader used by this example config.
    ///
    /// Supported:
    /// - [Section] headers
    /// - key=value pairs
    /// - ';' and '#' comment lines
    ///
    /// Not supported:
    /// - Escaping
    /// - Multi-line values
    /// - Advanced INI features
    ///
    /// Design goal:
    /// Keep the parser small, readable, and easy for modders to copy into their own projects.
    /// </summary>
    internal sealed class SimpleIni
    {
        // Storage layout:
        //   section -> (key -> value)
        //
        // StringComparer.OrdinalIgnoreCase makes section/key lookups case-insensitive.
        private readonly Dictionary<string, Dictionary<string, string>> _data =
            new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Loads an INI file from disk into a nested dictionary structure.
        ///
        /// Parsing rules:
        /// - Blank lines are ignored.
        /// - Lines beginning with ';' or '#' are treated as comments.
        /// - [Section] creates or switches the current section.
        /// - key=value stores the value under the current section.
        /// - Malformed lines are ignored.
        ///
        /// Notes:
        /// - Keys appearing before any section are stored under an empty section name.
        /// - Repeated keys overwrite earlier values in the same section.
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
        /// Reads an integer value and clamps it to the inclusive range [min..max].
        /// Returns <paramref name="def"/> if the key is missing or invalid before clamping.
        /// </summary>
        public int GetClamp(string sec, string key, int def, int min, int max)
        {
            var v = GetInt(sec, key, def);
            if (v < min) v = min;
            if (v > max) v = max;
            return v;
        }

        /// <summary>
        /// Reads a raw string value from [section] key=...
        /// Returns <paramref name="def"/> if the section or key is missing.
        /// </summary>
        public string GetString(string section, string key, string def)
            => (_data.TryGetValue(section, out var d) && d.TryGetValue(key, out var v)) ? v : def;

        /// <summary>
        /// Reads an integer value from [section] key=...
        /// Uses invariant culture and returns <paramref name="def"/> on parse failure.
        /// </summary>
        public int GetInt(string section, string key, int def)
            => int.TryParse(GetString(section, key, def.ToString(CultureInfo.InvariantCulture)), NumberStyles.Integer,
                            CultureInfo.InvariantCulture, out var v) ? v : def;

        /// <summary>
        /// Reads a float value from [section] key=...
        /// Uses invariant culture and returns <paramref name="def"/> on parse failure.
        /// </summary>
        public float GetFloat(string section, string key, float def)
            => float.TryParse(GetString(section, key, def.ToString(CultureInfo.InvariantCulture)), NumberStyles.Float,
                              CultureInfo.InvariantCulture, out var v) ? v : def;

        /// <summary>
        /// Reads a double value from [section] key=...
        /// Uses invariant culture and returns <paramref name="def"/> on parse failure.
        /// </summary>
        public double GetDouble(string section, string key, double def)
            => double.TryParse(GetString(section, key, def.ToString(CultureInfo.InvariantCulture)),
                               NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : def;

        /// <summary>
        /// Reads a boolean value from [section] key=...
        ///
        /// Accepted forms:
        /// - true / false
        /// - 1 / 0
        ///
        /// Returns <paramref name="def"/> if the value is missing or cannot be parsed.
        /// </summary>
        public bool GetBool(string section, string key, bool def)
        {
            var s = GetString(section, key, def ? "true" : "false");
            if (bool.TryParse(s, out var b)) return b;
            if (int.TryParse(s, out var i)) return i != 0;
            return def;
        }
    }
    #endregion
}