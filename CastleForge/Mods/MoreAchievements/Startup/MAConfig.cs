/*
SPDX-License-Identifier: GPL-3.0-or-later
Copyright (c) 2025 RussDev7
This file is part of https://github.com/RussDev7/CastleForge - see LICENSE for details.
*/

using System.Collections.Generic;
using DNA.CastleMinerZ.Terrain;
using System.Globalization;
using System.IO;
using System;

using static MoreAchievements.GamePatches;

namespace MoreAchievements
{
    /// <summary>
    /// INI-backed config for TacticalNuke. Mirrors NukeRegistry & NukeFuseConfig.
    /// </summary>
    internal sealed class MAConfig
    {
        // === [Rules] values ===
        public string CustomUnlockGameModes   = "Endurance";

        // === [UI] values ===
        public int    RowH       = 80;
        public int    RowPad     = 4;
        public int    PanelPad   = 20;
        public int    TitleGap   = 12;
        public int    ButtonsGap = 16;
        public int    IconPad    = 8;
        public int    IconGap    = 12;
        public string SortAchievementsBy      = "Name"; // Valid values: "Name" or "Id".

        // === [Sound] values ===
        public bool   PlaySounds              = true;
        public bool   UseCustomAwardSound     = true;

        // === [Announce] values ===
        public bool   AnnounceChat            = true;
        public bool   AnnounceAllAchievements = true;

        // === [Popup] values ===
        public string ShowAchievementPopup    = "All"; // None|Custom|Steam|All.

        // === [Hotkeys] values ===
        // Hotkey to reload this config at runtime.
        public string ReloadConfigHotkey      = "Ctrl+Shift+R";

        // Path: !Mods\MoreAchievements\MoreAchievements.Config.ini
        public static string ConfigPath =>
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory,
                         "!Mods", "MoreAchievements", "MoreAchievements.Config.ini");

        public static MAConfig LoadOrCreate()
        {
            var dir = Path.GetDirectoryName(ConfigPath);
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

            if (!File.Exists(ConfigPath))
            {
                File.WriteAllLines(ConfigPath, new[]
                {
                    "# MoreAchievements - UI Configuration",
                    "# Lines starting with ';' or '#' are comments.",
                    "",
                    "[Rules]",
                    "; Which game modes allow custom achievements to unlock.",
                    "; Valid values (case-insensitive, comma separated):",
                    ";   Endurance, Survival, DragonEndurance, Creative, Exploration, Scavenger, All",
                    "; \"All\" overrides the list and allows every mode.",
                    "CustomUnlockGameModes = Endurance",
                    "",
                    "[UI]",
                    "; Row height (per achievement entry).",
                    "RowH       = 80",
                    "; Extra vertical spacing between rows.",
                    "RowPad     = 4",
                    "; Padding inside the panel.",
                    "PanelPad   = 20",
                    "; Gap between title and top of list.",
                    "TitleGap   = 12",
                    "; Gap between list and buttons row.",
                    "ButtonsGap = 16",
                    "; Padding inside the icon box.",
                    "IconPad    = 8",
                    "; Horizontal gap between icon and text.",
                    "IconGap    = 12",
                    "; How to sort achievements within each difficulty group:",
                    ";   Name - Alphabetical by achievement name.",
                    ";   Id   - Numeric/ID-based ordering.",
                    "SortAchievementsBy = Name",
                    "",
                    "[Sound]",
                    "; If true, achievement UI plays click/close sounds.",
                    "PlaySounds          = true",
                    "; If true and a custom Award.(mp3|wav) exists in",
                    "; !Mods\\MoreAchievements\\CustomSounds, use it instead of",
                    "; the stock \"Award\" sound cue.",
                    "UseCustomAwardSound = true",
                    "",
                    "[Announce]",
                    "; If true, post a chat message when an achievement pops.",
                    "AnnounceChat            = true",
                    "; If true, announcements for all achievements (vanilla + MoreAchievements).",
                    "; If false, only announce custom achievements (MoreAchievements).",
                    "AnnounceAllAchievements = true",
                    "",
                    "[Popup]",
                    "; Which achievements should show HUD popups:",
                    ";   None   - No HUD popup.",
                    ";   Custom - Only MoreAchievements custom achievements.",
                    ";   Steam  - Only vanilla/Steam achievements.",
                    ";   All    - Both vanilla + custom.",
                    "ShowAchievementPopup = All",
                    "",
                    "[Hotkeys]",
                    "; Reload this config while in-game:",
                    "ReloadConfig = Ctrl+Shift+R",
                });
            }

            var ini = SimpleIni.Load(ConfigPath);
            var cfg = new MAConfig
            {
                // [Rules]
                CustomUnlockGameModes = ini.GetString  ("Rules",     "CustomUnlockGameModes",   "Endurance"),

                // [UI].
                RowH       = ini.GetClamp("UI", "RowH",       80, 16, 256),
                RowPad     = ini.GetClamp("UI", "RowPad",     4,  0, 64),
                PanelPad   = ini.GetClamp("UI", "PanelPad",   20, 0, 128),
                TitleGap   = ini.GetClamp("UI", "TitleGap",   12, 0, 128),
                ButtonsGap = ini.GetClamp("UI", "ButtonsGap", 16, 0, 128),
                IconPad    = ini.GetClamp("UI", "IconPad",    8,  0, 64),
                IconGap    = ini.GetClamp("UI", "IconGap",    12, 0, 128),
                SortAchievementsBy      = ini.GetString("UI",        "SortAchievementsBy",      "Name"),

                // [Sound].
                PlaySounds              = ini.GetBool  ("Sound",     "PlaySounds",              true),
                UseCustomAwardSound     = ini.GetBool  ("Sound",     "UseCustomAwardSound",     true),

                // [Announce].
                AnnounceChat            = ini.GetBool  ("Announce",  "AnnounceChat",            true),
                AnnounceAllAchievements = ini.GetBool  ("Announce",  "AnnounceAllAchievements", false),

                // [Popup].
                ShowAchievementPopup    = ini.GetString("Popup",     "ShowAchievementPopup",    "All"),

                // [Hotkeys].
                ReloadConfigHotkey      = ini.GetString("Hotkeys",   "ReloadConfig",            "Ctrl+Shift+R"),
            };

            return cfg;
        }

        public static void LoadApply()
        {
            try
            {
                var cfg = LoadOrCreate();
                cfg.ApplyToStatics();
                MAHotkeys.SetReloadBinding(cfg.ReloadConfigHotkey);
                // ModLoader.LogSystem.Log($"[Config] Applied from {PathShortener.ShortenForLog(ConfigPath)}.");
            }
            catch (Exception ex)
            {
                ModLoader.LogSystem.Log($"[Config] Failed to load/apply: {ex.Message}.");
            }
        }

        public void ApplyToStatics()
        {
            // [Rules].
            AchievementModeRules.ApplyFromConfig(CustomUnlockGameModes);

            // [UI].
            AchievementUIConfig.RowH       = RowH;
            AchievementUIConfig.RowPad     = RowPad;
            AchievementUIConfig.PanelPad   = PanelPad;
            AchievementUIConfig.TitleGap   = TitleGap;
            AchievementUIConfig.ButtonsGap = ButtonsGap;
            AchievementUIConfig.IconPad    = IconPad;
            AchievementUIConfig.IconGap    = IconGap;
            AchievementUIConfig.SortMode   = AchievementUIConfig.ParseSortMode(SortAchievementsBy);

            // [Sound].
            AchievementUIConfig.PlaySounds          = PlaySounds;
            AchievementUIConfig.UseCustomAwardSound = UseCustomAwardSound;

            // [Announce].
            AchievementUIConfig.AnnounceChat            = AnnounceChat;
            AchievementUIConfig.AnnounceAllAchievements = AnnounceAllAchievements;

            // [Popup].
            AchievementUIConfig.ShowAchievementPopupMode =
                AchievementUIConfig.ParsePopupMode(ShowAchievementPopup);
        }

        // Helpers.
        public static int Clamp(int v, int lo, int hi) => (v < lo) ? lo : (v > hi ? hi : v);
        public static float Clamp(float v, float lo, float hi) => (v < lo) ? lo : (v > hi ? hi : v);
        public static double Clamp(double v, double lo, double hi) => (v < lo) ? lo : (v > hi ? hi : v);

        private static BlockTypeEnum ParseEnum(BlockTypeEnum def, string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return def;

            // Allow numeric.
            if (int.TryParse(s.Trim(), out var i) && Enum.IsDefined(typeof(BlockTypeEnum), i))
                return (BlockTypeEnum)i;

            // Allow forgiving name match: Case-insensitive, ignores spaces/underscores.
            string norm = s.Trim().Replace("_", "").Replace(" ", "");
            foreach (BlockTypeEnum e in Enum.GetValues(typeof(BlockTypeEnum)))
            {
                var en = e.ToString().Replace("_", "").Replace(" ", "");
                if (string.Equals(en, norm, StringComparison.OrdinalIgnoreCase))
                    return e;
            }
            return def;
        }

        private static IEnumerable<BlockTypeEnum> ParseBlockList(string csv)
        {
            if (string.IsNullOrWhiteSpace(csv)) yield break;

            foreach (var raw in csv.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var token = raw.Trim();
                if (token.Length == 0) continue;
                yield return ParseEnum(BlockTypeEnum.Bedrock, token);
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
