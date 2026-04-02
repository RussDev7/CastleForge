/*
SPDX-License-Identifier: GPL-3.0-or-later
Copyright (c) 2025 RussDev7
This file is part of https://github.com/RussDev7/CastleForge - see LICENSE for details.
*/

using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System;

namespace TexturePacks
{
    /// <summary>
    /// Minimal INI-backed config:
    /// </summary>
    internal sealed class TPConfig
    {
        public string ActivePack    = "";   // Subfolder under !Mods\TexturePacks.
        public int    TileSize      = 64;   // Atlas cell size (px) for terrain tiles. Adjust to game's atlas.

        // Picker UI layout.
        public int    UI_RowH       = 64;
        public int    UI_RowPad     = 8;
        public int    UI_PanelPad   = 24;
        public int    UI_TitleGap   = 12;
        public int    UI_ButtonsGap = 18;
        public int    UI_IconPad    = 6;
        public int    UI_IconGap    = 10;

        // Model export / round-trip tuning.
        // FBX_COMP: Root scale compensation for GLB->Blender->FBX->XNB workflows.
        // Common value is 0.01 (cm vs m). Set to 1.0 to disable.
        public float ModelFbxComp = 0.01f;

        /// <summary>
        /// Export ALL game assets via an hotkey combo.
        ///
        /// Examples:
        /// - Basic:               Ctrl+Shift+F12
        /// - Left/right specific: LCtrl+LShift+F12
        /// - Single key:          F10
        /// - Disabled:            None
        /// </summary>
        public string ExportHotkey = "Ctrl+Shift+F3";

        public static string ConfigPath =>
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "!Mods", typeof(TPConfig).Namespace, "TexturePacks.Config.ini");

        public static TPConfig LoadOrCreate()
        {
            var dir = Path.GetDirectoryName(ConfigPath);
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

            if (!File.Exists(ConfigPath))
            {
                File.WriteAllLines(ConfigPath, new[]
                {
                    "; TexturePacks - Configuration",
                    "; Lines starting with ';' or '#' are comments.",
                    "",
                    "[General]",
                    "ActivePack =",
                    "TileSize   = 64",
                    "",
                    "[Hotkeys]",
                    "Export     = Ctrl+Shift+F3",
                    "",
                    "[PickerUI]",
                    "; Row height in pixels (min 24).",
                    "RowH       = 64",
                    "; Vertical gap between rows.",
                    "RowPad     = 8",
                    "; Inner padding of the panel.",
                    "PanelPad   = 24",
                    "; Space under the title.",
                    "TitleGap   = 12",
                    "; Space above the bottom buttons.",
                    "ButtonsGap = 18",
                    "; Icon padding inside a row.",
                    "IconPad    = 6",
                    "; Gap between icon and text.",
                    "IconGap    = 10",
                    "",
                    "[Models]",
                    "; Root scale compensation for GLB exports (Blender/FBX round-trip).",
                    "; 0.01 is common (cm vs m). Set to 1.0 to disable.",
                    "FbxComp    = 0.01",
                });
            }

            var ini = SimpleIni.Load(ConfigPath);
            return new TPConfig
            {
                // General.
                ActivePack    = ini.GetString("General", "ActivePack", "Default"),
                TileSize      = ini.GetClamp( "General", "TileSize",   64,64,1024),
                ExportHotkey  = ini.GetString("Hotkeys", "Export",     "Ctrl+Shift+F3"),

                // UI keys (with safe ranges).
                UI_RowH       = ini.GetClamp("PickerUI","RowH",       64, 24, 160),
                UI_RowPad     = ini.GetClamp("PickerUI","RowPad",     8,  0,  32),
                UI_PanelPad   = ini.GetClamp("PickerUI","PanelPad",   24, 8,  64),
                UI_TitleGap   = ini.GetClamp("PickerUI","TitleGap",   12, 0,  64),
                UI_ButtonsGap = ini.GetClamp("PickerUI","ButtonsGap", 18, 8,  64),
                UI_IconPad    = ini.GetClamp("PickerUI","IconPad",    6,  0,  16),
                UI_IconGap    = ini.GetClamp("PickerUI","IconGap",    10, 0,  32),

                // Models.
                ModelFbxComp  = ini.GetFloat("Models", "FbxComp", 0.01f),
            };
        }

        public void Save()
        {
            var dir = Path.GetDirectoryName(ConfigPath);
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

            var tmp = ConfigPath + ".tmp";
            File.WriteAllLines(tmp, new[]
            {
                $"; TexturePacks - Configuration",
                $"; Lines starting with ';' or '#' are comments.",
                $"",
                $"[General]",
                $"ActivePack = {ActivePack}",
                $"TileSize   = {TileSize}",
                $"",
                $"[Hotkeys]",
                $"Export     = {ExportHotkey}",
                $"",
                $"[PickerUI]",
                $"; Row height in pixels (min 24).",
                $"RowH       = {UI_RowH}",
                $"; Vertical gap between rows.",
                $"RowPad     = {UI_RowPad}",
                $"; Inner padding of the panel.",
                $"PanelPad   = {UI_PanelPad}",
                $"; Space under the title.",
                $"TitleGap   = {UI_TitleGap}",
                $"; Space above the bottom buttons.",
                $"ButtonsGap = {UI_ButtonsGap}",
                $"; Icon padding inside a row.",
                $"IconPad    = {UI_IconPad}",
                $"; Gap between icon and text.",
                $"IconGap    = {UI_IconGap}"
            });

            try
            {
                if (File.Exists(ConfigPath))
                    File.Replace(tmp, ConfigPath, ConfigPath + ".bak");
                else
                    File.Move(tmp, ConfigPath);
            }
            catch
            {
                // Fallback if File.Replace isn't available/allowed.
                File.Copy(tmp, ConfigPath, true);
                File.Delete(tmp);
            }
        }

        public static void SetActivePackAndSave(string pack)
        {
            var cfg = LoadOrCreate();
            cfg.ActivePack = string.IsNullOrWhiteSpace(pack) ? "Default" : pack.Trim();
            cfg.Save();
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
            => int.TryParse(GetString(section, key, def.ToString()), out var v) ? v : def;

        /// <summary>
        /// Reads a bool value from [section] key=... accepting:
        ///   "true/false" (case-insensitive) or "1/0".
        /// Returns <paramref name="def"/> on failure.
        /// </summary>
        public bool GetBool(string section, string key, bool def)
        {
            var s = GetString(section, key, def ? "true" : "false");
            if (bool.TryParse(s, out var b)) return b;
            if (int.TryParse(s, out var i)) return i != 0;
            return def;
        }

        /// <summary>
        /// Reads a float value from [section] key=... using invariant culture; returns <paramref name="def"/> on failure.
        /// Accepts either '.' or ',' as decimal separator (normalizes to '.').
        /// </summary>
        public float GetFloat(string section, string key, float def)
        {
            var s = GetString(section, key, def.ToString(CultureInfo.InvariantCulture));
            if (string.IsNullOrWhiteSpace(s)) return def;

            // Normalize common decimal separators
            s = s.Trim().Replace(',', '.');

            return float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : def;
        }
    }
}