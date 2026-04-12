/*
SPDX-License-Identifier: GPL-3.0-or-later
Copyright (c) 2025 RussDev7
This file is part of https://github.com/RussDev7/CastleForge - see LICENSE for details.
*/

using System.Collections.Generic;
using Microsoft.Xna.Framework;
using System.Globalization;
using DNA.CastleMinerZ;
using System.IO;
using System;

using static CMZMaterialColors.GamePatches;

namespace CMZMaterialColors
{
    /// <summary>
    /// Runtime-applied settings used by patches and live color lookups.
    /// Summary: Stores the active enabled state, logging flag, and the current material/laser color maps.
    /// </summary>
    internal static class CMZMaterialColors_Settings
    {
        public static volatile bool Enabled   = true;  // Master toggle for the mod at runtime.
        public static bool          DoLogging = false; // Enables extra logging output when true.

        // Active material color overrides keyed by tool material type.
        public static readonly Dictionary<ToolMaterialTypes, Color> MaterialColors =
            new Dictionary<ToolMaterialTypes, Color>();

        // Active laser color overrides keyed by tool material type.
        public static readonly Dictionary<ToolMaterialTypes, Color> LaserColors =
            new Dictionary<ToolMaterialTypes, Color>();
    }

    /// <summary>
    /// Config loader and applier for CMZMaterialColors.
    /// Summary: Creates the default INI, reads user settings, parses colors, and pushes values into runtime statics.
    /// </summary>
    internal sealed class ColorConfig
    {
        internal static volatile ColorConfig Active;

        public bool Enabled   = true;
        public bool DoLogging = false;

        public string ReloadConfigHotkey = "Ctrl+Shift+R";

        public readonly Dictionary<ToolMaterialTypes, Color> MaterialColors =
            new Dictionary<ToolMaterialTypes, Color>();

        public readonly Dictionary<ToolMaterialTypes, Color> LaserColors =
            new Dictionary<ToolMaterialTypes, Color>();

        public static string ConfigPath =>
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "!Mods", "CMZMaterialColors", "CMZMaterialColors.Config.ini");

        /// <summary>
        /// Loads the config from disk or creates a default one if missing.
        /// Summary: Reads general settings, material colors, and laser colors for every <see cref="ToolMaterialTypes"/> value.
        /// </summary>
        public static ColorConfig LoadOrCreate()
        {
            var dir = Path.GetDirectoryName(ConfigPath);
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

            if (!File.Exists(ConfigPath))
            {
                File.WriteAllLines(ConfigPath, new[]
                {
                    "# CMZMaterialColors - Configuration",
                    "# Supports named colors (Magenta, GhostWhite, DarkRed, etc.) or RGBA values like 255,0,255,255.",
                    "",
                    "[General]",
                    "Enabled   = true",
                    "DoLogging = false",
                    "",
                    "[MaterialColors]",
                    "Wood       = SaddleBrown",
                    "Stone      = DarkGray",
                    "Copper     = 184,115,51,255",
                    "Iron       = 128,128,128,255",
                    "Gold       = 255,189,0,255",
                    "Diamond    = 26,168,177,255",
                    "BloodStone = DarkRed",
                    "",
                    "[LaserColors]",
                    "Wood       = SaddleBrown",
                    "Stone      = DarkGray",
                    "Copper     = Lime",
                    "Iron       = Red",
                    "Gold       = Yellow",
                    "Diamond    = Blue",
                    "BloodStone = GhostWhite",
                    "",
                    "[Hotkeys]",
                    "; Reload this config while in-game:",
                    "ReloadConfig = Ctrl+Shift+R",
                });
            }

            var ini = SimpleIni.Load(ConfigPath);
            var cfg = new ColorConfig
            {
                Enabled            = ini.GetBool("General", "Enabled", true),
                DoLogging          = ini.GetBool("General", "DoLogging", false),
                ReloadConfigHotkey = ini.GetString("Hotkeys", "ReloadConfig", "Ctrl+Shift+R"),
            };

            foreach (ToolMaterialTypes mat in Enum.GetValues(typeof(ToolMaterialTypes)))
            {
                cfg.MaterialColors[mat] = ReadColor(ini, "MaterialColors", mat.ToString(), GetVanillaMaterialColor(mat));
                cfg.LaserColors[mat]    = ReadColor(ini, "LaserColors",    mat.ToString(), GetVanillaLaserColor(mat));
            }

            return cfg;
        }

        /// <summary>
        /// Loads the config and applies it to runtime state.
        /// Summary: Refreshes static settings, reapplies registered item colors, and updates the reload hotkey binding.
        /// </summary>

        public static void LoadApply()
        {
            try
            {
                var cfg = LoadOrCreate();
                Active = cfg;
                cfg.ApplyToStatics();
                RuntimeColorRefresh.ApplyRegisteredItemColors();
                CMCHotkeys.SetReloadBinding(cfg.ReloadConfigHotkey);
            }
            catch (Exception ex)
            {
                ModLoader.LogSystem.Log($"[ColorConfig] Failed to load/apply: {ex.Message}.");
            }
        }

        /// <summary>
        /// Copies this config instance into the shared runtime settings.
        /// Summary: Replaces the current static material and laser color dictionaries with the loaded values.
        /// </summary>
        public void ApplyToStatics()
        {
            CMZMaterialColors_Settings.Enabled = Enabled;
            CMZMaterialColors_Settings.DoLogging = DoLogging;

            CMZMaterialColors_Settings.MaterialColors.Clear();
            CMZMaterialColors_Settings.LaserColors.Clear();

            foreach (var kv in MaterialColors)
                CMZMaterialColors_Settings.MaterialColors[kv.Key] = kv.Value;

            foreach (var kv in LaserColors)
                CMZMaterialColors_Settings.LaserColors[kv.Key] = kv.Value;
        }

        /// <summary>
        /// Returns the default non-laser color for a given material type.
        /// Summary: Provides the fallback material color used when config does not override a value.
        /// </summary>
        public static Color GetVanillaMaterialColor(ToolMaterialTypes mat)
        {
            switch (mat)
            {
                case ToolMaterialTypes.Wood:       return Color.SaddleBrown;
                case ToolMaterialTypes.Stone:      return Color.DarkGray;
                case ToolMaterialTypes.Copper:     return new Color(184, 115, 51);
                case ToolMaterialTypes.Iron:       return new Color(128, 128, 128);
                case ToolMaterialTypes.Gold:       return new Color(255, 189, 0);
                case ToolMaterialTypes.Diamond:    return new Color(26, 168, 177);
                case ToolMaterialTypes.BloodStone: return Color.DarkRed;
                default:                           return Color.Gray;
            }
        }

        /// <summary>
        /// Returns the default laser color for a given material type.
        /// Summary: Provides the fallback beam color used when config does not override a value.
        /// </summary>
        public static Color GetVanillaLaserColor(ToolMaterialTypes mat)
        {
            switch (mat)
            {
                case ToolMaterialTypes.Wood:       return Color.SaddleBrown;
                case ToolMaterialTypes.Stone:      return Color.DarkGray;
                case ToolMaterialTypes.Copper:     return Color.Lime;
                case ToolMaterialTypes.Iron:       return Color.Red;
                case ToolMaterialTypes.Gold:       return Color.Yellow;
                case ToolMaterialTypes.Diamond:    return Color.Blue;
                case ToolMaterialTypes.BloodStone: return Color.GhostWhite;
                default:                           return Color.Gray;
            }
        }

        /// <summary>
        /// Reads a color string from the INI and parses it.
        /// Summary: Returns the parsed color if valid; otherwise returns the supplied default color.
        /// </summary>
        private static Color ReadColor(SimpleIni ini, string sec, string key, Color def)
        {
            string raw = ini.GetString(sec, key, null);
            if (string.IsNullOrWhiteSpace(raw)) return def;

            if (TryParseColor(raw, out var c))
                return c;

            return def;
        }

        /// <summary>
        /// Parses a color from either "R,G,B[,A]" or a supported named color.
        /// Summary: Accepts a small set of named XNA colors and invariant-culture integer channel values.
        /// </summary>
        public static bool TryParseColor(string raw, out Color color)
        {
            color = Color.White;
            if (string.IsNullOrWhiteSpace(raw)) return false;

            raw = raw.Trim();

            string[] parts = raw.Split(',');
            if (parts.Length == 3 || parts.Length == 4)
            {
                int a = 255;
                if (!int.TryParse(parts[0].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int r)) return false;
                if (!int.TryParse(parts[1].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int g)) return false;
                if (!int.TryParse(parts[2].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int b)) return false;
                if (parts.Length == 4 && !int.TryParse(parts[3].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out a)) return false;

                r = Clamp(r, 0, 255);
                g = Clamp(g, 0, 255);
                b = Clamp(b, 0, 255);
                a = Clamp(a, 0, 255);
                color = new Color(r, g, b, a);
                return true;
            }

            switch (raw.ToLowerInvariant())
            {
                case "black":       color = Color.Black;       return true;
                case "white":       color = Color.White;       return true;
                case "gray":        color = Color.Gray;        return true;
                case "darkgray":    color = Color.DarkGray;    return true;
                case "red":         color = Color.Red;         return true;
                case "green":       color = Color.Green;       return true;
                case "lime":        color = Color.Lime;        return true;
                case "blue":        color = Color.Blue;        return true;
                case "yellow":      color = Color.Yellow;      return true;
                case "orange":      color = Color.Orange;      return true;
                case "magenta":     color = Color.Magenta;     return true;
                case "purple":      color = Color.Purple;      return true;
                case "pink":        color = Color.Pink;        return true;
                case "cyan":        color = Color.Cyan;        return true;
                case "aqua":        color = Color.Aqua;        return true;
                case "ghostwhite":  color = Color.GhostWhite;  return true;
                case "saddlebrown": color = Color.SaddleBrown; return true;
                case "darkred":     color = Color.DarkRed;     return true;
                default:             return false;
            }
        }

        /// <summary>
        /// Clamps an integer to an inclusive range.
        /// Summary: Ensures <paramref name="v"/> stays within <paramref name="lo"/>.. <paramref name="hi"/>.
        /// </summary>
        public static int Clamp(int v, int lo, int hi) => (v < lo) ? lo : (v > hi ? hi : v);
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
        /// Reads a boolean value from the INI.
        /// Summary: Accepts 1/0, true/false, yes/no, and on/off; returns <paramref name="def"/> if missing or invalid.
        /// </summary>
        public bool GetBool(string sec, string key, bool def)
        {
            var s = GetString(sec, key, null);
            if (string.IsNullOrWhiteSpace(s)) return def;

            switch (s.Trim().ToLowerInvariant())
            {
                case "1":
                case "true":
                case "yes":
                case "on":
                    return true;
                case "0":
                case "false":
                case "no":
                case "off":
                    return false;
                default:
                    return def;
            }
        }
    }
}
