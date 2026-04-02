/*
SPDX-License-Identifier: GPL-3.0-or-later
Copyright (c) 2025 RussDev7
This file is part of https://github.com/RussDev7/CastleForge - see LICENSE for details.
*/

using Microsoft.Xna.Framework.Input;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System;

using static CastleWallsMk2.GamePatches;

namespace CastleWallsMk2
{
    /// <summary>
    /// Tiny INI-style config for the CastleWallsMk2 mod.
    ///
    /// Location:
    ///   !Mods/<Namespace>/<Namespace>.ini.
    ///
    /// Format:
    ///   key=value per line; blank lines and lines starting with '#' are ignored.
    ///   Parsing is tolerant: Unknown keys are ignored; malformed lines are skipped.
    /// </summary>
    internal sealed class ModConfig
    {
        internal static ModConfig Current { get; private set; }

        #region Config States

        public enum   Style { Classic, Light, Dark }

        public Keys   ToggleKey                  { get; set; } = Keys.OemTilde;  // Default: '~'.
        public Keys   FreeFlyToggleKey           { get; set; } = Keys.F6;        // Default: F6.
        public string ReloadConfigHotkey         { get; set; } = "Ctrl+Shift+R"; // Default: Ctrl+Shift+R.
        public bool   ShowMenuOnLaunch           { get; set; } = false;          // Default: Off.
        public bool   RandomizeUsernameOnLaunch  { get; set; } = false;          // Default: Off.
        public bool   StreamLogToFile            { get; set; } = true;           // Default: On.
        public bool   ShowInGameUIFeedback       { get; set; } = true;           // Default: On.
        public Style  Theme                      { get; set; } = Style.Dark;     // Default: Dark.
        public float  Scale                      { get; set; } = 1.0f;           // Default: 1.0.
        public string NetCapturePreferredAdapter { get; set; } = "";             // Note: Partial name match, e.g. "intel", "realtek".
        public int    NetCapturePreferredIndex   { get; set; } = -1;             // Note: 0..N-1 from the device list, -1 = ignore.
        public bool   NetCaptureHideOwnIp        { get; set; } = true;           // Default: On.
        public int    GeoConnectTimeoutMs        { get; set; } = 1500;           // Default: 1.5s.
        public int    GeoReadTimeoutMs           { get; set; } = 1500;           // Default: 1.5s.
        public bool   RemoveMaxWorldHeight       { get; set; } = true;           // Default: On.

        #endregion

        #region Paths

        /// <summary>Folder where the INI lives: !Mods/<Namespace>.</summary>
        public static string FolderPath =>
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "!Mods", typeof(ModConfig).Namespace);

        /// <summary>Full path to the INI: !Mods/<Namespace>/<Namespace>.ini.</summary>
        public static string ConfigPath => Path.Combine(FolderPath, $"{typeof(EmbeddedResolver).Namespace}.Config.ini");

        #endregion

        #region Load / Save

        /// <summary>
        /// Load config if present; otherwise create a default file and return defaults.
        /// Safe: Any I/O errors are swallowed and defaults are returned.
        /// </summary>
        public static ModConfig LoadOrCreateDefaults()
        {
            ModConfig cfg;
            if (!File.Exists(ConfigPath))
            {
                cfg = new ModConfig();
                try { Directory.CreateDirectory(FolderPath); } catch { }
                try { cfg.Save();                            } catch { }
            }
            else
            {
                cfg = TryLoad(out var loaded) ? loaded : new ModConfig();
            }

            // Always apply binding after we have a final cfg.
            try { CWHotkeys.SetReloadBinding(cfg.ReloadConfigHotkey); } catch { }

            // Cache snapshot.
            Current = cfg;

            return cfg;
        }

        /// <summary>
        /// Tolerant parser:
        ///   - Ignores blanks, comments (;..., #...), sections, and malformed lines.
        ///   - Keys are matched case-insensitively.
        ///   - Booleans accept only "true" (case-insensitive) as true; anything else -> false.
        /// Returns true on success; false if a fatal read error occurred.
        /// </summary>
        public static bool TryLoad(out ModConfig cfg)
        {
            cfg = new ModConfig();
            try
            {
                var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (var raw in File.ReadAllLines(ConfigPath))
                {
                    var line  = raw.Trim();
                    if (line.Length == 0 || line.StartsWith(";") || line.StartsWith("#")) continue; // Skip comment/blank.
                    if (line.StartsWith("[") && line.EndsWith("]")) continue;                       // Skip sections [S].

                    int eq    = line.IndexOf('=');
                    if (eq    <= 0) continue; // Malformed; skip.

                    var key   = line.Substring(0, eq).Trim();
                    var val   = line.Substring(eq + 1).Trim();
                    dict[key] = val; // Last write wins.
                }

                // --- Hotkey ---
                if (dict.TryGetValue("ToggleKey", out var toggleStr))
                    cfg.ToggleKey = ParseKey(toggleStr, cfg.ToggleKey);
                if (dict.TryGetValue("FreeFlyToggleKey", out var ffStr))
                    cfg.FreeFlyToggleKey = ParseKey(ffStr, cfg.FreeFlyToggleKey);
                if (dict.TryGetValue("ReloadConfigHotkey", out var reloadStr))
                    cfg.ReloadConfigHotkey = reloadStr;

                // --- Booleans ---
                if (dict.TryGetValue("ShowMenuOnLaunch", out var smol))
                    cfg.ShowMenuOnLaunch = string.Equals(smol, "true", StringComparison.OrdinalIgnoreCase);

                if (dict.TryGetValue("RandomizeUsernameOnLaunch", out var rnd))
                    cfg.RandomizeUsernameOnLaunch = string.Equals(rnd, "true", StringComparison.OrdinalIgnoreCase);

                if (dict.TryGetValue("StreamLogToFile", out var sltf))
                    cfg.StreamLogToFile = string.Equals(sltf, "true", StringComparison.OrdinalIgnoreCase);

                if (dict.TryGetValue("ShowInGameUIFeedback", out var sigf))
                    cfg.ShowInGameUIFeedback = string.Equals(sigf, "true", StringComparison.OrdinalIgnoreCase);

                if (dict.TryGetValue("RemoveMaxWorldHeight", out var ffMaxY))
                    cfg.RemoveMaxWorldHeight = string.Equals(ffMaxY, "true", StringComparison.OrdinalIgnoreCase);

                // --- Theme ---
                if (dict.TryGetValue("Theme", out var themeStr) &&
                    Enum.TryParse(themeStr, true, out Style parsedTheme))
                {
                    cfg.Theme = parsedTheme;
                }

                // --- Scale ---
                if (dict.TryGetValue("Scale", out var scaleStr) &&
                    float.TryParse(scaleStr, NumberStyles.Float, CultureInfo.InvariantCulture, out float parsedScale))
                {
                    cfg.Scale = Clamp(parsedScale, 0.5f, 2.5f);
                }

                // --- Net Capture ---
                if (dict.TryGetValue("NetCapturePreferredAdapter", out var nca))
                    cfg.NetCapturePreferredAdapter = nca;

                if (dict.TryGetValue("NetCapturePreferredIndex", out var nci) &&
                    int.TryParse(nci, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedIdx))
                    cfg.NetCapturePreferredIndex = parsedIdx;

                if (dict.TryGetValue("NetCaptureHideOwnIp", out var nch))
                    cfg.NetCaptureHideOwnIp = string.Equals(nch, "true", StringComparison.OrdinalIgnoreCase);

                if (dict.TryGetValue("GeoConnectTimeoutMs", out var gct) && int.TryParse(gct, out var v1))
                    cfg.GeoConnectTimeoutMs = Clamp(v1, 250, 10000); // 0.25s..10s

                if (dict.TryGetValue("GeoReadTimeoutMs", out var grt) && int.TryParse(grt, out var v2))
                    cfg.GeoReadTimeoutMs = Clamp(v2, 250, 10000);

                return true;
            }
            catch { return false; }
        }

        /// <summary>
        /// Writes a commented INI with the current values. Non-fatal if I/O fails.
        /// </summary>
        public void Save()
        {
            try
            {
                var lines = new[]
                {
                    $"# ================================================================================",
                    $"# {typeof(ModConfig).Namespace} - Config",
                    $"# Lines beginning with ';' or '#' are comments.",
                    $"#",
                    $"# Keys:",
                    $"#   ToggleKey = Microsoft.Xna.Framework.Input.Keys | single char",
                    $"#     - Examples: OemTilde | F8 | A | ~ | '",
                    $"#   FreeFlyToggleKey = Microsoft.Xna.Framework.Input.Keys | single char",
                    $"#     - Hotkey to toggle FreeFly / Spectator camera mode.",
                    $"#     - Examples: F6 | F7 | OemTilde | G | '",
                    $"#   ReloadConfigHotkey = string (modifier combo)",
                    $"#     - Format:   Ctrl+Shift+R (modifiers optional)",
                    $"#     - Examples: Ctrl+R | Alt+Shift+F5 | F9",
                    $"#   ShowMenuOnLaunch = true|false",
                    $"#     - true  -> Shows the menu as soon as the game launches.",
                    $"#     - false -> Hides the menu at launch until the hotkey is pressed.",
                    $"#   RandomizeUsernameOnLaunch = true|false",
                    $"#     - true  -> Call UsernameRandomizer.TryApplyAtStartup() on launch.",
                    $"#     - false -> Leave username unchanged.",
                    $"#   StreamLogToFile = true|false",
                    $"#     - true  -> Stream every log entry to !Mods/<namespace>/!Logs/<timestamp>.log",
                    $"#     - false -> Do not write a rolling file (you can still Save via the UI).",
                    $"#   ShowInGameUIFeedback = true|false",
                    $"#     - true  -> Shows all feedback from the UI.",
                    $"#     - false -> Hides all (non-callback) feedback from the UI.",
                    $"#   Theme = Classic|Light|Dark",
                    $"#     - Sets the UI color scheme (applied via ImGui style presets).",
                    $"#   Scale = 0.50 .. 2.50",
                    $"#     - UI scale multiplier (float). 1.0 = 100%. Uses '.' for decimals;",
                    $"#       values outside range are clamped.",
                    $"#   NetCapturePreferredAdapter = string (partial adapter name, e.g. 'intel', 'realtek', 'wi-fi')",
                    $"#   NetCapturePreferredIndex   = int (-1 = auto, otherwise 0..N-1 from device list)",
                    $"#   NetCaptureHideOwnIp        = true|false",
                    $"#   GeoConnectTimeoutMs = 250..10000 (milliseconds)",
                    $"#     - HTTP connect timeout for Geo-IP lookups (ip-api.com). Default: 1500.",
                    $"#   GeoReadTimeoutMs = 250..10000 (milliseconds)",
                    $"#     - HTTP read/write timeout for Geo-IP lookups. Default: 1500.",
                    $"#   RemoveMaxWorldHeight = true|false",
                    $"#     - true  -> Removes the hard max-Y clamp (74/64) for the local player.",
                    $"#     - false -> Keeps vanilla max world height behavior.",
                    $"#",
                    $"# Saved: {DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss 'UTC'", CultureInfo.InvariantCulture)}",
                    $"# ================================================================================",
                    $"",
                    $"[Hotkey]",
                    $"; The main key to show / hide the GUI. Examples: OemTilde | F8 | A | ~ | '",
                    $"ToggleKey={ToggleKey}",
                    $"; Toggle FreeFly / Spectator camera.",
                    $"FreeFlyToggleKey={FreeFlyToggleKey}",
                    $"; Reload this config while in-game.",
                    $"ReloadConfigHotkey={ReloadConfigHotkey}",
                    $"",
                    $"[Startup]",
                    $"; Show the menu immediately on launch.",
                    $"ShowMenuOnLaunch={(ShowMenuOnLaunch ? "true" : "false")}",
                    $"",
                    $"[Identity]",
                    $"; Randomize the username once at startup",
                    $"RandomizeUsernameOnLaunch={(RandomizeUsernameOnLaunch ? "true" : "false")}",
                    $"",
                    $"[Logging]",
                    $"; Stream logs to file continuously (UI can still trigger manual saves).",
                    $"StreamLogToFile={(StreamLogToFile ? "true" : "false")}",
                    $"",
                    $"[Feedback]",
                    $"; Echos in-game the set-states (e.g. tool enable / disable) from the UI.",
                    $"ShowInGameUIFeedback={(ShowInGameUIFeedback ? "true" : "false")}",
                    $"",
                    $"[UI]",
                    $"; Set the UI color theme. Options: Classic | Light | Dark. Default: Dark.",
                    $"Theme={Theme}",
                    $"; Set the UI scale. Float multiplier (e.g., 0.90, 1.00, 1.25).",
                    $"; Uses '.' decimal; applied to ImGui global scale.",
                    $"Scale={Scale}",
                    $"",
                    $"[NetworkCapture]",
                    $"; Pick which adapter SharpPcap should open.",
                    $"; You can put a partial name here, e.g. 'intel', 'realtek', 'wi-fi', 'ethernet'.",
                    $"NetCapturePreferredAdapter={NetCapturePreferredAdapter}",
                    $"; Or pick by index from the startup list (0..N-1). -1 = auto.",
                    $"NetCapturePreferredIndex={NetCapturePreferredIndex}",
                    $"; Hide your own local IP from the sniffer table/log.",
                    $"NetCaptureHideOwnIp={(NetCaptureHideOwnIp ? "true" : "false")}",
                    $"; Geo IP lookup HTTP timeouts (milliseconds).",
                    $"; Lower = snappier UI but more timeouts; Higher = more tolerant but slower.",
                    $"; Valid range is 250..10000 (clamped). Defaults are 1500/1500.",
                    $"GeoConnectTimeoutMs={GeoConnectTimeoutMs}",
                    $"GeoReadTimeoutMs={GeoReadTimeoutMs}",
                    $"",
                    $"[World]",
                    $"; Remove the hard max-Y world clamp (74/64).",
                    $"RemoveMaxWorldHeight={(RemoveMaxWorldHeight ? "true" : "false")}",
                };
                Directory.CreateDirectory(FolderPath);
                File.WriteAllLines(ConfigPath, lines);
            }
            catch { /* Non-fatal. */ }
        }
        #endregion

        #region Parsing Helpers

        /// <summary>
        /// Tolerant <see cref="Keys"/> parser. Accepts enum names (e.g., "OemTilde", "F8", "A")
        /// and single characters (e.g., "~", "'", "T", "0").
        /// </summary>
        private static Keys ParseKey(string s, Keys @default)
        {
            if (string.IsNullOrWhiteSpace(s)) return @default;
            s = s.Trim();

            // Single character shortcuts.
            if (s.Length == 1)
            {
                char c = s[0];
                if (c >= 'A' && c <= 'Z') return (Keys)Enum.Parse(typeof(Keys), c.ToString(), true);
                if (c >= 'a' && c <= 'z') return (Keys)Enum.Parse(typeof(Keys), char.ToUpperInvariant(c).ToString(), true);
                if (c >= '0' && c <= '9') return (Keys)Enum.Parse(typeof(Keys), "D" + c, true);

                switch (c)
                {
                    case '`':
                    case '~': return Keys.OemTilde;
                    case '-': return Keys.OemMinus;
                    case '=': return Keys.OemPlus;
                    case '[': return Keys.OemOpenBrackets;
                    case ']': return Keys.OemCloseBrackets;
                    case '\\': return Keys.OemPipe;
                    case ';': return Keys.OemSemicolon;
                    case '\'': return Keys.OemQuotes;
                    case ',': return Keys.OemComma;
                    case '.': return Keys.OemPeriod;
                    case '/': return Keys.OemQuestion;
                    case ' ': return Keys.Space;
                }
            }

            // F1..F24.
            if ((s[0] == 'F' || s[0] == 'f') &&
                int.TryParse(s.Substring(1), out int f) && f >= 1 && f <= 24)
            {
                return (Keys)((int)Keys.F1 + (f - 1));
            }

            // Friendly synonyms.
            switch (s.ToLowerInvariant())
            {
                case "tilde":
                case "grave":
                case "backquote":
                case "oemtilde":
                case "console": return Keys.OemTilde;

                case "minus": return Keys.OemMinus;
                case "plus":
                case "equals": return Keys.OemPlus;

                case "openbracket":
                case "lbracket": return Keys.OemOpenBrackets;

                case "closebracket":
                case "rbracket": return Keys.OemCloseBrackets;

                case "backslash": return Keys.OemPipe;
                case "semicolon": return Keys.OemSemicolon;

                case "quote":
                case "apostrophe": return Keys.OemQuotes;

                case "comma": return Keys.OemComma;

                case "period":
                case "dot": return Keys.OemPeriod;

                case "slash":
                case "question": return Keys.OemQuestion;

                case "space": return Keys.Space;
            }

            // Fall back to enum parse.
            return Enum.TryParse<Keys>(s, ignoreCase: true, out var parsed) ? parsed : @default;
        }

        /// <summary> Tiny clamp helpers to avoid Math.Clamp dependency. </summary>
        private static float Clamp(float v, float min, float max)
            => (v < min) ? min : (v > max) ? max : v;
        private static int Clamp(int v, int min, int max)
            => (v < min) ? min : (v > max) ? max : v;

        #endregion

        #region Optional: Hotkey Utility

        /// <summary>
        /// Rising-edge detector for a single key. Call once per frame.
        /// Returns true exactly on the frame the key transitions from Up->Down.
        /// </summary>
        public static class HotkeyUtil
        {
            private static KeyboardState _prev;

            public static bool ConsumeTogglePress(Keys key)
            {
                var cur = Keyboard.GetState();
                bool pressed = cur.IsKeyDown(key) && !_prev.IsKeyDown(key);
                _prev = cur;
                return pressed;
            }
        }
        #endregion
    }

    #region SimpleIni

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
    #endregion
}