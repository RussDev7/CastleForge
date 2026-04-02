/*
SPDX-License-Identifier: GPL-3.0-or-later
Copyright (c) 2025 RussDev7
This file is part of https://github.com/RussDev7/CastleForge - see LICENSE for details.
*/

using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using System.Globalization;
using System.IO;
using System;

using static TooManyItems.TMILog;

namespace TooManyItems
{
    /// <summary>
    /// Global single-source-of-truth for config values that the ENTIRE mod consumes.
    /// Keep this minimal and readonly-at-runtime (set once when the mod starts).
    /// </summary>
    internal static class ConfigGlobals
    {
        // Master toggle (feature enable) + hotkey
        public static bool Enabled       = true;   // Toggle with O.
        public static Keys ToggleKey     = Keys.O;
        public static bool ShowEnums     = false;
        public static bool ShowBlockIds  = false;
        public static bool ShowBlockInfo = false;
        public static bool ShowItemInfo  = false;

        // Time behavior.
        public static bool MidnightIsNewday = false;

        // Torch behavior.
        public static bool TorchesDropBlock = false;

        // Layout (UI geometry). Treat these as authoritative dimensions
        // the rest of the overlay reads during Draw/Update.
        public static int? ITEM_COLUMNS  = 6;
        public static int  MARGIN        = 8;
        public static int  TOPBAR_H      = 48;
        public static int  SEARCH_H      = 28;
        public static int  SAVE_BTN_W    = 172;
        public static int  SAVE_BTN_H    = 48;
        public static int  ITEMS_COL_W   = 365;
        public static int  CELL          = 52;  // Item size.
        public static int  CELL_PAD      = 6;

        // Toolbar sprite sheet geometry: computed at runtime when texture is loaded.
        public static int _toolbarCols = 0;

        // Colors (overlay theme). Use pre-multiplied or straight alpha consistently.
        public static Color ColPanel   = new Color(0, 0, 0, 160);
        public static Color ColPanelHi = new Color(15, 15, 25, 210);
        public static Color ColBtn     = new Color(25, 25, 35, 220);
        public static Color ColBtnHot  = new Color(40, 40, 60, 230);
        public static Color ColXBtn    = new Color(55, 30, 30, 220);
        public static Color ColXBtnHot = new Color(80, 40, 40, 230);
        public static Color ColLine    = new Color(220, 220, 220, 40);
        public static Color ColText    = Color.White * 0.92f;

        // Tooltips behavior.
        public static bool   USE_TOOLTIPS          = true;
        public static double TOOLTIP_DELAY_ITEM    = 0.7;  // Seconds before item tooltip appears.
        public static double TOOLTIP_DELAY_TOOLBAR = 0.35; // Quicker than item tooltips.

        // Item list behavior.
        public static bool HideUnusable = false;
        public static bool SortItems    = false;

        // Internal state flags (set once after applying a config / loading textures).
        public static bool      _itemWidthApplied;
        public static bool      _configApplied;
        public static bool      _stateApplied;
        public static bool      _uiInitApplied;
        public static string    _toolbarTexturePath;
        public static string    _missingTexturePath;
        public static Texture2D _missingTex;

        // Sounds.
        public static bool USE_SOUNDS = true;

        // Save slots.
        public static int      SAVE_SLOTS = 7;
        public static string[] _slotNames = new string[SAVE_SLOTS]; // Null = Empty.

        // Debugging latches.
        public static bool _exportLatch;
    }

    /// <summary>
    /// Strongly-typed config model + loader for TMI.
    /// - Writes a default INI the first time.
    /// - Loads and parses values into this POCO.
    /// Notes:
    ///  • Keys: Microsoft.Xna.Framework.Input.Keys throughout.
    ///  • ItemsColW = 0 means "auto" based on ItemColumns (see ApplyConfig usage).
    ///  • Color hex supports #RGB, #RGBA, #RRGGBB, #RRGGBBAA (and ARGB: prefix).
    /// </summary>
    internal sealed class TMIConfig
    {
        // --- General ---
        public bool Enabled       = true;
        public Keys ToggleKey     = Keys.O;
        public Keys DebugKey      = Keys.F3;
        public Keys ReloadKey     = Keys.F9;
        public bool ShowEnums     = false;
        public bool ShowBlockIds  = false;
        public bool ShowBlockInfo = false;
        public bool ShowItemInfo  = false;

        // --- Behavior ---
        public int  ItemColumns      = 6;
        public int  SaveSlots        = 7;
        public bool HideUnusable     = false;
        public bool SortItems        = false;
        public bool MidnightIsNewday = false;
        public bool TorchesDropBlock = false;

        // --- Layout ---
        public int Margin    = 8;
        public int TopbarH   = 48;
        public int SearchH   = 28;
        public int SaveBtnW  = 172;
        public int SaveBtnH  = 48;
        public int ItemsColW = 0;   // 0 = Auto from ItemColumns.
        public int Cell      = 52;
        public int CellPad   = 6;

        // --- Appearance ---
        public Color PanelColor      = new Color(0,0,0,160);
        public Color PanelHiColor    = new Color(15,15,25,210);
        public Color ButtonColor     = new Color(25,25,35,220);
        public Color ButtonHotColor  = new Color(40,40,60,230);
        public Color XButtonColor    = new Color(55, 30, 30, 220);
        public Color XButtonHotColor = new Color(80, 40, 40, 230);
        public Color LineColor       = new Color(220,220,220,40);
        public Color TextColor       = Color.White * 0.92f;

        // --- Textures (relative paths supported) ---
        public string ToolbarTexture = @"!Mods\TooManyItems\Textures\TMI.png";
        public string MissingTexture = @"!Mods\TooManyItems\Textures\MissingTexture.png";

        // --- Tooltips ---
        public bool   UseTooltips  = true;
        public double ItemDelay    = 0.70;
        public double ToolbarDelay = 0.35;

        // --- Sounds ---
        public bool UseSounds = true;

        // --- Logging ---
        public LoggingType LoggingType = LoggingType.SendFeedback;

        // Example: <game>\!Mods\MyModNamespace\MyMod.Config.ini.
        public static string ConfigPath =>
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "!Mods", typeof(TMIConfig).Namespace, "TooManyItems.Config.ini");

        /// <summary>
        /// Creates the config directory and default INI if missing,
        /// then parses the current file into a WEPConfig instance.
        /// Safe to call multiple times (idempotent).
        /// </summary>
        public static TMIConfig LoadOrCreate()
        {
            // Ensure folder.
            var dir = Path.GetDirectoryName(ConfigPath);
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

            // Write default file once.
            if (!File.Exists(ConfigPath))
            {
                var lines = new[]
                {
                    $@"; TooManyItems - Configuration",
                    $@"; Lines starting with ';' or '#' are comments.",
                    $@"",
                    $@"[General]",
                    $@"Enabled=true",
                    $@"ToggleKey=O",
                    $@"DebugKey=F3",
                    $@"ReloadKey=F9",
                    $@"ShowEnums=false",
                    $@"ShowBlockIds=false",
                    $@"ShowBlockInfo=false",
                    $@"ShowItemInfo=false",
                    $@"",
                    $@"[Behavior]",
                    $@"ItemColumns=6",
                    $@"SaveSlots=7",
                    $@"HideUnusable=false",
                    $@"SortItems=false",
                    $@"MidnightIsNewday=false",
                    $@"TorchesDropBlock=false",
                    $@"",
                    $@"[Layout]",
                    $@"Margin=8",
                    $@"TopbarH=48",
                    $@"SearchH=28",
                    $@"SaveBtnW=172",
                    $@"SaveBtnH=48",
                    $@"ItemsColW=0",
                    $@"Cell=52",
                    $@"CellPad=6",
                    $@"",
                    $@"[Appearance]",
                    $@"PanelColor=#000000A0",
                    $@"PanelHiColor=#0F0F19D2",
                    $@"ButtonColor=#191923DC",
                    $@"ButtonHotColor=#28283CEC",
                    $@"XButtonColor=#371E1EDC",
                    $@"XButtonHotColor=#502828E6",
                    $@"LineColor=#DCDCDC28",
                    $@"TextColor=#FFFFFFEA",
                    $@"",
                    $@"[Textures]",
                    $@"ToolbarTexture=!Mods\TooManyItems\Textures\tmi.png",
                    $@"MissingTexture=!Mods\TooManyItems\Textures\MissingTexture.png",
                    $@"",
                    $@"[Tooltips]",
                    $@"UseTooltips=true",
                    $@"ItemDelay=0.70",
                    $@"ToolbarDelay=0.35",
                    $@"",
                    $@"[Sounds]",
                    $@"UseSounds=true",
                    $@"",
                    $@"[Logging]",
                    $@"; Options:",
                    $@";   - 'SendFeedback' (Emits to console plus logs.)",
                    $@";   - 'Log'          (Logs only.)",
                    $@";   - 'None'         (Do nothing.)",
                    $@"LoggingType=SendFeedback",
                };

                File.WriteAllLines(ConfigPath, lines);
            }

            // Parse INI -> TMIConfig (use current field values as fallbacks).
            var ini = SimpleIni.Load(ConfigPath);
            var c   = new TMIConfig();

            // [General].
            c.Enabled          = ini.GetBool("General", "Enabled", c.Enabled);
            c.ToggleKey        = ParseKey(ini.GetString("General", "ToggleKey", c.ToggleKey.ToString()), c.ToggleKey);
            c.DebugKey         = ParseKey(ini.GetString("General", "DebugKey",  c.DebugKey.ToString()),  c.DebugKey);
            c.ReloadKey        = ParseKey(ini.GetString("General", "ReloadKey", c.ReloadKey.ToString()), c.ReloadKey);
            c.ShowEnums        = ini.GetBool("General", "ShowEnums",     c.ShowEnums);
            c.ShowBlockIds     = ini.GetBool("General", "ShowBlockIds",  c.ShowBlockIds);
            c.ShowBlockInfo    = ini.GetBool("General", "ShowBlockInfo", c.ShowBlockInfo);
            c.ShowItemInfo     = ini.GetBool("General", "ShowItemInfo",  c.ShowItemInfo);

            // [Behavior].
            c.ItemColumns      = ini.GetInt("Behavior",  "ItemColumns",      c.ItemColumns);
            c.SaveSlots        = ini.GetInt("Behavior",  "SaveSlots",        c.SaveSlots);
            c.HideUnusable     = ini.GetBool("Behavior", "HideUnusable",     c.HideUnusable);
            c.SortItems        = ini.GetBool("Behavior", "SortItems",        c.SortItems);
            c.MidnightIsNewday = ini.GetBool("Behavior", "MidnightIsNewday", c.MidnightIsNewday);
            c.TorchesDropBlock = ini.GetBool("Behavior", "TorchesDropBlock", c.TorchesDropBlock);

            // [Layout].
            c.Margin           = ini.GetInt("Layout", "Margin",    c.Margin);
            c.TopbarH          = ini.GetInt("Layout", "TopbarH",   c.TopbarH);
            c.SearchH          = ini.GetInt("Layout", "SearchH",   c.SearchH);
            c.SaveBtnW         = ini.GetInt("Layout", "SaveBtnW",  c.SaveBtnW);
            c.SaveBtnH         = ini.GetInt("Layout", "SaveBtnH",  c.SaveBtnH);
            c.ItemsColW        = ini.GetInt("Layout", "ItemsColW", c.ItemsColW); // 0 = auto.
            c.Cell             = ini.GetInt("Layout", "Cell",      c.Cell);
            c.CellPad          = ini.GetInt("Layout", "CellPad",   c.CellPad);

            // [Appearance] (hex parser supports #RGB, #RGBA, #RRGGBB, #RRGGBBAA; or "ARGB:#AARRGGBB").
            c.PanelColor       = ParseHexColor(ini.GetString("Appearance", "PanelColor",      ToHex(c.PanelColor)),      c.PanelColor);
            c.PanelHiColor     = ParseHexColor(ini.GetString("Appearance", "PanelHiColor",    ToHex(c.PanelHiColor)),    c.PanelHiColor);
            c.ButtonColor      = ParseHexColor(ini.GetString("Appearance", "ButtonColor",     ToHex(c.ButtonColor)),     c.ButtonColor);
            c.ButtonHotColor   = ParseHexColor(ini.GetString("Appearance", "ButtonHotColor",  ToHex(c.ButtonHotColor)),  c.ButtonHotColor);
            c.XButtonColor     = ParseHexColor(ini.GetString("Appearance", "XButtonColor",    ToHex(c.XButtonColor)),    c.XButtonColor);
            c.XButtonHotColor  = ParseHexColor(ini.GetString("Appearance", "XButtonHotColor", ToHex(c.XButtonHotColor)), c.XButtonHotColor);
            c.LineColor        = ParseHexColor(ini.GetString("Appearance", "LineColor",       ToHex(c.LineColor)),       c.LineColor);
            c.TextColor        = ParseHexColor(ini.GetString("Appearance", "TextColor",       ToHex(c.TextColor)),       c.TextColor);

            // [Textures].
            c.ToolbarTexture   = ini.GetString("Textures", "ToolbarTexture", c.ToolbarTexture);
            c.MissingTexture   = ini.GetString("Textures", "MissingTexture", c.MissingTexture);

            // [Tooltips].
            c.UseTooltips      = ini.GetBool("Tooltips",   "UseTooltips",  c.UseTooltips);
            c.ItemDelay        = ini.GetDouble("Tooltips", "ItemDelay",    c.ItemDelay);
            c.ToolbarDelay     = ini.GetDouble("Tooltips", "ToolbarDelay", c.ToolbarDelay);

            // [Sounds].
            c.UseSounds        = ini.GetBool("Sounds", "UseSounds", c.UseSounds);

            // [Logging].
            c.LoggingType      = ini.GetLoggingType("Logging", "LoggingType", c.LoggingType);

            return c;
        }

        /// <summary>
        /// Applies the parsed config object into the live shared statics that the rest of the mod reads.
        /// Use this during startup and hot-reload so non-UI systems see the current config immediately.
        /// </summary>
        public static void ApplyToStatics(TMIConfig c)
        {
            // General.
            ConfigGlobals.Enabled       = c.Enabled;
            ConfigGlobals.ToggleKey     = c.ToggleKey;
            ConfigGlobals.ShowEnums     = c.ShowEnums;
            ConfigGlobals.ShowBlockIds  = c.ShowBlockIds;
            ConfigGlobals.ShowBlockInfo = c.ShowBlockInfo;
            ConfigGlobals.ShowItemInfo  = c.ShowItemInfo;

            // Behavior.
            ConfigGlobals.MidnightIsNewday = c.MidnightIsNewday;
            ConfigGlobals.TorchesDropBlock = c.TorchesDropBlock;

            // Layout.
            ConfigGlobals.ITEM_COLUMNS = c.ItemColumns;
            ConfigGlobals.MARGIN       = c.Margin;
            ConfigGlobals.TOPBAR_H     = c.TopbarH;
            ConfigGlobals.SEARCH_H     = c.SearchH;
            ConfigGlobals.SAVE_BTN_W   = c.SaveBtnW;
            ConfigGlobals.SAVE_BTN_H   = c.SaveBtnH;
            ConfigGlobals.ITEMS_COL_W  = c.ItemsColW;
            ConfigGlobals.CELL         = c.Cell;
            ConfigGlobals.CELL_PAD     = c.CellPad;

            // Appearance.
            ConfigGlobals.ColPanel   = c.PanelColor;
            ConfigGlobals.ColPanelHi = c.PanelHiColor;
            ConfigGlobals.ColBtn     = c.ButtonColor;
            ConfigGlobals.ColBtnHot  = c.ButtonHotColor;
            ConfigGlobals.ColXBtn    = c.XButtonColor;
            ConfigGlobals.ColXBtnHot = c.XButtonHotColor;
            ConfigGlobals.ColLine    = c.LineColor;
            ConfigGlobals.ColText    = c.TextColor;

            // Tooltips / sounds.
            ConfigGlobals.USE_TOOLTIPS          = c.UseTooltips;
            ConfigGlobals.TOOLTIP_DELAY_ITEM    = c.ItemDelay;
            ConfigGlobals.TOOLTIP_DELAY_TOOLBAR = c.ToolbarDelay;
            ConfigGlobals.USE_SOUNDS            = c.UseSounds;
        }

        private static Keys ParseKey(string s, Keys fallback)
        {
            try { return (Keys)Enum.Parse(typeof(Keys), s.Trim(), true); }
            catch { return fallback; }
        }

        private static string ToHex(Color c)
        {
            // #AARRGGBB.
            return $"#{c.A:X2}{c.R:X2}{c.G:X2}{c.B:X2}";
        }

        /// <summary>
        /// Parses color from:
        ///   #RGB, #RGBA, #RRGGBB, #RRGGBBAA
        /// or "ARGB:#AARRGGBB".
        /// Returns fallback on any parse error.
        /// </summary>
        static Color ParseHexColor(string raw, Color fallback)
        {
            if (string.IsNullOrWhiteSpace(raw)) return fallback;

            string rawString = raw.Trim();
            bool isARGB = false;
            if (rawString.StartsWith("ARGB:", StringComparison.OrdinalIgnoreCase)) { isARGB = true; rawString = rawString.Substring(5).Trim(); }

            if (rawString.StartsWith("#")) rawString = rawString.Substring(1);
            if (rawString.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) rawString = rawString.Substring(2);

            byte r, g, b, a;

            switch (rawString.Length)
            {
                case 3: // #RGB.
                    r = Rep(rawString[0]); g = Rep(rawString[1]); b = Rep(rawString[2]); a = 255; break;

                case 4: // #RGBA.
                    r = Rep(rawString[0]); g = Rep(rawString[1]); b = Rep(rawString[2]); a = Rep(rawString[3]); break;

                case 6: // #RRGGBB.
                    r = Hex(rawString, 0); g = Hex(rawString, 2); b = Hex(rawString, 4); a = 255; break;

                case 8:
                    if (isARGB)
                    {   // #AARRGGBB (explicit).
                        a = Hex(rawString, 0); r = Hex(rawString, 2); g = Hex(rawString, 4); b = Hex(rawString, 6);
                    }
                    else
                    {   // default to #RRGGBBAA.
                        r = Hex(rawString, 0); g = Hex(rawString, 2); b = Hex(rawString, 4); a = Hex(rawString, 6);
                    }
                    break;

                default:
                    return fallback;
            }
            return new Color(r, g, b, a);

            byte Hex(string str, int start) =>
                byte.Parse(str.Substring(start, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);

            byte Rep(char c)
            {
                // Expand #RGBA nibble -> byte.
                string s = new string(new[] { c, c });
                return byte.Parse(s, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
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
        /// Reads a <see cref="LoggingType"/> enum value from the INI (case-insensitive).
        /// Returns <paramref name="def"/> if missing or unrecognized.
        /// </summary>
        public LoggingType GetLoggingType(string section, string key, LoggingType def)
        {
            var s = GetString(section, key, def.ToString());
            return Enum.TryParse<LoggingType>(s, true, out var v) ? v : def;
        }
    }
}
