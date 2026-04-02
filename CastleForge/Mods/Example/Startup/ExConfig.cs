/*
SPDX-License-Identifier: GPL-3.0-or-later
Copyright (c) 2025 RussDev7
This file is part of https://github.com/RussDev7/CastleForge - see LICENSE for details.
*/

using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System;

namespace Example
{
    #region Runtime State

    /// <summary>
    /// Live runtime values consumed by the rest of the mod.
    ///
    /// Purpose:
    /// - Keeps hot-path reads simple and fast.
    /// - Lets patches / gameplay systems read current values without touching disk.
    /// - Acts as the shared state written by <see cref="ExConfig.ApplyToStatics"/>.
    ///
    /// Pattern:
    /// - <see cref="ExConfig"/> owns file creation, parsing, and validation.
    /// - This class owns the live values the mod actually reads during play.
    /// </summary>
    internal static class Example_Settings
    {
        // [ExampleConifg].
        // Master on/off switch for the example mod.
        // Marked volatile because it may be read frequently by runtime systems.
        public static volatile bool Enabled        = true;

        // [ExampleConifg].
        // Example boolean toggle showing how a normal true/false setting is exposed at runtime.
        public static bool          ExampleSetting = true;

        // [AnotherSetting].
        // Example numeric runtime value after parsing / clamping.
        public static int           ExampleAmount  = 420;

        // [ExampleLogging].
        // Enables extra logging for diagnostics / troubleshooting.
        public static bool          DoLogging      = false;
    }
    #endregion

    #region Load / Create / Apply

    /// <summary>
    /// Lightweight INI-backed config container.
    ///
    /// This class demonstrates the full config lifecycle:
    /// 1. Create a default INI file if one does not already exist.
    /// 2. Load and parse the current file from disk.
    /// 3. Validate / clamp values as needed.
    /// 4. Apply the parsed values into shared runtime statics.
    ///
    /// Recommended usage:
    /// - Call <see cref="LoadApply"/> during startup.
    /// - Call <see cref="LoadApply"/> again during hot-reload.
    /// - Let gameplay systems read <see cref="Example_Settings"/> instead of reading disk directly.
    /// </summary>
    internal sealed class ExConfig
    {
        // Last successfully loaded config snapshot.
        //
        // Use cases:
        // - UI can inspect the last applied config object if needed.
        // - Debug tools can see what was parsed from disk.
        // - Runtime systems should usually prefer Example_Settings for fast reads.
        internal static volatile ExConfig Active;

        // [ExampleConifg].
        // Master toggle for the entire example mod.
        public bool Enabled        = true;

        // [ExampleConifg].
        // Example feature toggle used to demonstrate a standard boolean setting.
        public bool ExampleSetting = true;

        // [AnotherSetting].
        // Example numeric setting read from disk and clamped into a safe range.
        public int  ExampleAmount  = 420;
        
        // [ExampleLogging].
        // Enables optional logging for debugging and tracing.
        public bool DoLogging      = false;

        // Full path to the INI file used by this example.
        //
        // Result:
        //   <GameFolder>\!Mods\Example\Example.Config.ini
        public static string ConfigPath =>
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "!Mods", "Example", "Example.Config.ini");

        /// <summary>
        /// Ensures the config directory and INI file exist, then loads the current file from disk.
        ///
        /// Behavior:
        /// - Creates the target directory if missing.
        /// - Writes a default INI template if the file does not exist yet.
        /// - Parses the INI into an <see cref="ExConfig"/> instance.
        /// - Applies basic validation / clamping while reading values.
        ///
        /// Safe to call repeatedly.
        /// This method is idempotent with respect to folder/file creation.
        /// </summary>
        public static ExConfig LoadOrCreate()
        {
            var dir = Path.GetDirectoryName(ConfigPath);
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

            if (!File.Exists(ConfigPath))
            {
                File.WriteAllLines(ConfigPath, new[]
                {
                    "# Example - Configuration",
                    "# This file is intentionally simple and heavily commented for modders.",
                    "# Lines starting with ';' or '#' are treated as comments.",
                    "",
                    "[ExampleConifg]",
                    "; Master toggle for the entire mod.",
                    "; When false, patches / features should treat the mod as disabled.",
                    "Enabled        = true",
                    "",
                    "; Example boolean setting.",
                    "; Demonstrates a normal true/false config value.",
                    "ExampleSetting = true",
                    "",
                    "[AnotherSetting]",
                    "; Example numeric setting.",
                    "; This value is clamped in code to the inclusive range 0..800.",
                    "ExampleAmount  = 420",
                    "",
                    "[ExampleLogging]",
                    "; Enables optional logging for diagnostics and troubleshooting.",
                    "DoLogging      = true",
                });
            }

            var ini = SimpleIni.Load(ConfigPath);
            var cfg = new ExConfig
            {
                // [ExampleConifg].
                // Reads the master mod toggle.
                Enabled        = ini.GetBool("ExampleConifg", "Enabled", true),

                // [ExampleConifg].
                // Reads an example boolean feature toggle.
                ExampleSetting = ini.GetBool("ExampleConifg", "ExampleSetting", true),

                // [AnotherSetting].
                // Reads an integer and clamps it into a safe runtime range.
                ExampleAmount  = Clamp(ini.GetInt("AnotherSetting", "ExampleAmount", 420), 0, 800), // Clamp to 0 min - 800 max.

                // [ExampleLogging].
                // Reads the optional logging toggle.
                DoLogging      = ini.GetBool("ExampleLogging", "DoLogging", false),
            };
            return cfg;
        }

        /// <summary>
        /// Loads the current config from disk and applies it to the live runtime statics.
        ///
        /// This is the main "refresh everything" entry point for the config system.
        /// Recommended call sites:
        /// - Mod startup / initialization
        /// - Manual hot-reload actions
        /// - Any explicit "reload config" command
        ///
        /// Failure behavior:
        /// - Exceptions are caught and logged.
        /// - This avoids crashing the mod because of a bad or missing config load.
        /// </summary>
        public static void LoadApply()
        {
            try
            {
                var cfg = LoadOrCreate();
                Active  = cfg;
                cfg.ApplyToStatics();
            }
            catch (Exception ex)
            {
                ModLoader.LogSystem.Log($"[ExConfig] Failed to load/apply: {ex.Message}.");
            }
        }

        /// <summary>
        /// Copies the parsed config values into the live shared runtime statics.
        ///
        /// Why this exists:
        /// - Keeps runtime reads centralized and cheap.
        /// - Separates disk/parsing concerns from gameplay/patched code.
        /// - Ensures non-UI systems immediately see the latest config after startup or hot-reload.
        /// </summary>
        public void ApplyToStatics()
        {
            // [ExampleConifg].
            Example_Settings.Enabled        = Enabled;
            Example_Settings.ExampleSetting = ExampleSetting;

            // [AnotherSetting].
            Example_Settings.ExampleAmount  = ExampleAmount;

            // [ExampleLogging].
            Example_Settings.DoLogging      = DoLogging;
        }

        #region Helpers

        /// <summary>
        /// Clamps an integer to an inclusive range.
        /// Ensures <paramref name="v"/> never falls below <paramref name="lo"/> or above <paramref name="hi"/>.
        /// </summary>
        public static int Clamp(int v, int lo, int hi) => (v < lo) ? lo : (v > hi ? hi : v);

        /// <summary>
        /// Clamps a float to an inclusive range.
        /// Ensures <paramref name="v"/> never falls below <paramref name="lo"/> or above <paramref name="hi"/>.
        /// </summary>
        public static float Clamp(float v, float lo, float hi) => (v < lo) ? lo : (v > hi ? hi : v);

        /// <summary>
        /// Clamps a double to an inclusive range.
        /// Ensures <paramref name="v"/> never falls below <paramref name="lo"/> or above <paramref name="hi"/>.
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