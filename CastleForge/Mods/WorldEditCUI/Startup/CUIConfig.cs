/*
SPDX-License-Identifier: GPL-3.0-or-later
Copyright (c) 2025 RussDev7
This file is part of https://github.com/RussDev7/CastleForge - see LICENSE for details.
*/

using Microsoft.Xna.Framework;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System;

using static WorldEditCUI.GamePatches;
using static ModLoader.LogSystem;

namespace WorldEditCUI
{
    /// <summary>
    /// INI-backed config for the WorldEditCUI overlay (colors + chunk overlay toggles).
    /// Stored at: !Mods/WorldEditCUI/WorldEditCUI.Config.ini
    /// </summary>
    internal sealed class CUIConfig
    {
        // Settings.
        public bool ChunksEnabled        = false;  // 16x16 chunk boundaries within selection.
        public bool ChunkGridEnabled     = false;  // 24x24-chunk mega-grid within selection.

        public Color CUIOutlineColor     = Color.LightCoral;
        public Color ChunkOutlineColor   = Color.Yellow;
        public Color ChunkGridColor      = Color.Lime;

        // Base outline mode + thickness.
        public bool UseGridBaseOutline   = true;   // true => OutlineSelectionWithGrid (default), false => OutlineSelection (edges only).

        public float OutlineThickness    = 0.06f;  // Main selection outline thickness.
        public float GridLineThickness   = 0.02f;  // Interior grid thickness (OutlineSelectionWithGrid).
        public float ChunkThickness      = 0.025f; // 16x16 chunk boundary thickness.
        public float ChunkGridThickness  = 0.03f;  // 24x24 mega-grid boundary thickness.

        // Hotkey to reload this config at runtime.
        public string ReloadConfigHotkey = "Ctrl+Shift+R";

        // Path.
        public static string ConfigPath =>
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "!Mods", "WorldEditCUI", "WorldEditCUI.Config.ini");

        public static CUIConfig LoadOrCreate()
        {
            var dir = Path.GetDirectoryName(ConfigPath);
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

            if (!File.Exists(ConfigPath))
            {
                File.WriteAllLines(ConfigPath, new[]
                {
                    "# WorldEditCUI - Configuration",
                    "# Lines starting with ';' or '#' are comments.",
                    "",
                    "[CUI]",
                    "; Selection outline color (R,G,B,A).",
                    "OutlineColor     = 240,128,128,255",
                    "",
                    "; Base outline mode: Grid (default) or Outline (edges only).",
                    "BaseMode         = Grid",
                    "; Selection outline thickness (world units).",
                    "OutlineThickness = 0.06",
                    "; Interior grid line thickness used by OutlineSelectionWithGrid (world units).",
                    "GridThickness    = 0.02",
                    "",
                    "[Chunks]",
                    "; Show 16x16 chunk boundaries inside the selection.",
                    "Enabled   = false",
                    "; Chunk boundary line color (R,G,B,A).",
                    "Color     = 255,255,0,255",
                    "; Chunk boundary line thickness (world units).",
                    "Thickness = 0.025",
                    "",
                    "[ChunkGrid]",
                    "; Show the 24x24-chunk mega-grid boundaries (every 384 blocks) inside the selection.",
                    "Enabled   = false",
                    "; Mega-grid boundary line color (R,G,B,A).",
                    "Color     = 0,255,0,255",
                    "; Mega-grid boundary line thickness (world units).",
                    "Thickness = 0.03",
                    "",
                    "[Hotkeys]",
                    "; Reload this config while in-game:",
                    "ReloadConfig = Ctrl+Shift+R",
                });
            }

            var ini = SimpleIni.Load(ConfigPath);

            var cfg = new CUIConfig
            {
                // Toggles.
                ChunksEnabled      = ini.GetBool("Chunks", "Enabled", false),
                ChunkGridEnabled   = ini.GetBool("ChunkGrid", "Enabled", false),

                // Base outline mode.
                UseGridBaseOutline = ParseBaseMode(ini.GetString("CUI", "BaseMode", "Grid")),

                // Thickness (world units).
                OutlineThickness   = ClampThickness(ini.GetFloat("CUI", "OutlineThickness", 0.06f), 0.06f),
                GridLineThickness  = ClampThickness(ini.GetFloat("CUI", "GridThickness", 0.02f), 0.02f),
                ChunkThickness     = ClampThickness(ini.GetFloat("Chunks", "Thickness", 0.025f), 0.025f),
                ChunkGridThickness = ClampThickness(ini.GetFloat("ChunkGrid", "Thickness", 0.03f), 0.03f),

                // Colors.
                CUIOutlineColor    = TryParseColorArgs(ini.GetString("CUI", "OutlineColor", "240,128,128,255"), Color.LightCoral),
                ChunkOutlineColor  = TryParseColorArgs(ini.GetString("Chunks", "Color", "255,255,0,255"), Color.Yellow),
                ChunkGridColor     = TryParseColorArgs(ini.GetString("ChunkGrid", "Color", "0,255,0,255"), Color.Lime),

                // Reload hotkey.
                ReloadConfigHotkey = ini.GetString("Hotkeys", "ReloadConfig", "Ctrl+Shift+R"),
            };

            return cfg;
        }

        /// <summary>
        /// Loads (or creates) the INI, applies to WorldEditCUI statics.
        /// </summary>
        public static void LoadApply()
        {
            try
            {
                var cfg = LoadOrCreate();
                cfg.ApplyToStatics();
                CUIHotkeys.SetReloadBinding(cfg.ReloadConfigHotkey);
                CUIOverlayRenderer._config = cfg;
            }
            catch (Exception ex)
            {
                Log($"[CUIConfig] Failed to load/apply config: {ex.Message}.");
            }
        }

        public void ApplyToStatics()
        {
            // Toggles.
            CUIOverlayRenderer.ShowChunkOutlines         = ChunksEnabled;
            CUIOverlayRenderer.ShowChunkGrid             = ChunkGridEnabled;
            CUIOverlayRenderer.UseGridBaseOutline        = UseGridBaseOutline;

            // Thickness.
            CUIOverlayRenderer.CUIOutlineThickness       = OutlineThickness;
            CUIOverlayRenderer.CUIGridLineThickness      = GridLineThickness;
            CUIOverlayRenderer.ChunkOutlineThickness     = ChunkThickness;
            CUIOverlayRenderer.ChunkGridOutlineThickness = ChunkGridThickness;

            // Colors.
            CUIOverlayRenderer.CUIOutlineColor           = CUIOutlineColor;
            CUIOverlayRenderer.ChunkOutlineColor         = ChunkOutlineColor;
            CUIOverlayRenderer.ChunkGridOutlineColor     = ChunkGridColor;

            // Reload hotkey.
            CUIOverlayRenderer.ReloadHotkey              = ReloadConfigHotkey;
        }

        /// <summary>
        /// Writes WorldEditCUI's current statics to the INI file.
        /// </summary>
        public static void SaveFromStatics()
        {
            try
            {
                var dir = Path.GetDirectoryName(ConfigPath);
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

                File.WriteAllLines(ConfigPath, new[]
                {
                    "# WorldEditCUI - Configuration",
                    "# Lines starting with ';' or '#' are comments.",
                    "",
                    "[CUI]",
                    "; Selection outline color (R,G,B,A).",
                    $"OutlineColor     = {ColorToIni(CUIOverlayRenderer.CUIOutlineColor)}",
                    "",
                    "; Base outline mode: Grid (default) or Outline (edges only).",
                    $"BaseMode         = {(CUIOverlayRenderer.UseGridBaseOutline ? "Grid" : "Outline")}",
                    "; Selection outline thickness (world units).",
                    $"OutlineThickness = {ToIniFloat(CUIOverlayRenderer.CUIOutlineThickness)}",
                    "; Interior grid line thickness used by OutlineSelectionWithGrid (world units).",
                    $"GridThickness    = {ToIniFloat(CUIOverlayRenderer.CUIGridLineThickness)}",
                    "",
                    "[Chunks]",
                    "; Show 16x16 chunk boundaries inside the selection.",
                    $"Enabled   = {(CUIOverlayRenderer.ShowChunkOutlines ? "true" : "false")}",
                    "; Chunk boundary line color (R,G,B,A).",
                    $"Color     = {ColorToIni(CUIOverlayRenderer.ChunkOutlineColor)}",
                    "; Chunk boundary line thickness (world units).",
                    $"Thickness = {ToIniFloat(CUIOverlayRenderer.ChunkOutlineThickness)}",
                    "",
                    "[ChunkGrid]",
                    "; Show the 24x24-chunk mega-grid boundaries (every 384 blocks) inside the selection.",
                    $"Enabled   = {(CUIOverlayRenderer.ShowChunkGrid ? "true" : "false")}",
                    "; Mega-grid boundary line color (R,G,B,A).",
                    $"Color     = {ColorToIni(CUIOverlayRenderer.ChunkGridOutlineColor)}",
                    "; Mega-grid boundary line thickness (world units).",
                    $"Thickness = {ToIniFloat(CUIOverlayRenderer.ChunkGridOutlineThickness)}",
                    "",
                    "[Hotkeys]",
                    "; Reload this config while in-game:",
                    $"ReloadConfig = {CUIOverlayRenderer.ReloadHotkey}",
                });
            }
            catch (Exception ex)
            {
                Log($"[CUIConfig] Failed to save config: {ex.Message}.");
            }
        }

        /// <summary>
        /// Serializes an XNA Color to the INI-friendly "R,G,B,A" format (0..255 each).
        /// </summary>
        public static string ColorToIni(Color c) => $"{c.R},{c.G},{c.B},{c.A}";

        /// <summary>
        /// Clamps an integer to the valid 8-bit color channel range (0..255).
        /// </summary>
        public static int ClampByte(int v) => (v < 0) ? 0 : (v > 255 ? 255 : v);

        /// <summary>
        /// INI-friendly float formatting (InvariantCulture).
        /// </summary>
        private static string ToIniFloat(float v)
            => v.ToString("0.###", CultureInfo.InvariantCulture);

        /// <summary>
        /// Clamps a thickness value to a safe range so rendering can't explode or disappear.
        /// If the parsed value is invalid (NaN/Infinity/<=0), falls back to <paramref name="fallback"/>.
        /// </summary>
        private static float ClampThickness(float v, float fallback)
        {
            if (float.IsNaN(v) || float.IsInfinity(v) || v <= 0f)
                return fallback;

            // Reasonable world-unit thickness bounds for BasicEffect quads.
            const float Min = 0.001f;
            const float Max = 1.000f;
            if (v < Min) return Min;
            if (v > Max) return Max;
            return v;
        }

        /// <summary>
        /// Parses "Grid" / "Outline" (case-insensitive). Unknown values default to Grid.
        /// </summary>
        private static bool ParseBaseMode(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return true;

            s = s.Trim().ToLowerInvariant();
            if (s == "grid" || s == "withgrid" || s == "outlinewithgrid" || s == "outline_grid")
                return true;

            if (s == "outline" || s == "edges" || s == "box")
                return false;

            // Accept partials.
            if (s.StartsWith("g")) return true;
            if (s.StartsWith("o") || s.StartsWith("e") || s.StartsWith("b")) return false;

            return true;
        }

        /// <summary>
        /// Parses an INI/chat color string like "255,255,0,255" or "255 255 0 255".
        /// Requires at least R,G,B; alpha is optional (defaults to 255). Returns <paramref name="fallback"/> on failure.
        /// </summary>
        public static Color TryParseColorArgs(string s, Color fallback)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(s)) return fallback;

                var parts = s.Replace(",", " ").Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length < 3) return fallback;

                if (!int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out int r)) return fallback;
                if (!int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int g)) return fallback;
                if (!int.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out int b)) return fallback;

                int a = 255;
                if (parts.Length >= 4 && !int.TryParse(parts[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out a))
                    a = 255;

                r = ClampByte(r); g = ClampByte(g); b = ClampByte(b); a = ClampByte(a);
                return new Color(r, g, b, a);
            }
            catch
            {
                return fallback;
            }
        }

        /// <summary>
        /// Parses a chat command color from args (comma or space separated).
        /// Requires at least R,G,B; alpha is optional (defaults to 255). Returns true on success.
        /// </summary>
        public static bool TryParseColorArgs(string[] args, out Color color)
        {
            color = default;

            if (args == null || args.Length == 0) return false;

            string raw = string.Join(" ", args).Trim();
            if (raw.Length == 0) return false;

            var parts = raw.Replace(",", " ").Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 3) return false;

            if (!int.TryParse(parts[0], out int r)) return false;
            if (!int.TryParse(parts[1], out int g)) return false;
            if (!int.TryParse(parts[2], out int b)) return false;
            int a = 255;
            if (parts.Length >= 4 && !int.TryParse(parts[3], out a)) return false;

            r = ClampByte(r); g = ClampByte(g); b = ClampByte(b); a = ClampByte(a);
            color = new Color(r, g, b, a);
            return true;
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
        /// Reads a float value from [section] key=... using invariant culture; returns <paramref name="def"/> on failure.
        /// </summary>
        public float GetFloat(string section, string key, float def)
            => float.TryParse(GetString(section, key, def.ToString(CultureInfo.InvariantCulture)), NumberStyles.Float,
                              CultureInfo.InvariantCulture, out var v) ? v : def;

        /// <summary>
        /// Reads a bool value from [section] key=... accepting:
        ///   "true/false" (case-insensitive) or "1/0".
        /// Returns <paramref name="def"/> on failure.
        /// </summary>
        public bool GetBool(string section, string key, bool def)
        {
            var s = GetString(section, key, def ? "true" : "false");
            if (bool.TryParse(s, out var b)) return b;
            if (int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var i)) return i != 0;
            return def;
        }
    }
}