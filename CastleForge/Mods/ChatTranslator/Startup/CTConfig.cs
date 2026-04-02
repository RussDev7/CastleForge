/*
SPDX-License-Identifier: GPL-3.0-or-later
Copyright (c) 2025 RussDev7
This file is part of https://github.com/RussDev7/CastleForge - see LICENSE for details.
*/

using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System;

using static ChatTranslator.GamePatches;
using static ModLoader.LogSystem;

namespace ChatTranslator
{
    /// <summary>
    /// INI-backed config for ChatTranslator.
    /// Stores the baseline language and an optional default remote language.
    /// File: !Mods\ChatTranslator\ChatTranslator.Config.ini
    /// </summary>
    internal sealed class CTConfig
    {
        // [Languages].
        public string BaseLanguage          = "en";
        public string DefaultRemoteLanguage = "";

        // Hotkey to reload this config at runtime.
        public string ReloadConfigHotkey    = "Ctrl+Shift+R";

        // Paths.
        public static string ConfigPath =>
            Path.Combine(
                AppDomain.CurrentDomain.BaseDirectory,
                "!Mods",
                "ChatTranslator",
                "ChatTranslator.Config.ini");

        public static CTConfig LoadOrCreate()
        {
            var dir = Path.GetDirectoryName(ConfigPath);
            if (!Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            if (!File.Exists(ConfigPath))
            {
                File.WriteAllLines(ConfigPath, new[]
                {
                    "# ChatTranslator - Configuration",
                    "# Use ISO language codes like en, es, de, ru, zh, fr, etc.",
                    "# Lines starting with ';' or '#' are comments.",
                    "",
                    "[Languages]",
                    "; Your own baseline language (what you type/read in).",
                    "BaseLanguage          = en",
                    "",
                    "; Optional default remote language when manual mode is used.",
                    "; Leave empty to start with translation off.",
                    "DefaultRemoteLanguage = ",
                    "",
                    "[Hotkeys]",
                    "; Reload this config while in-game:",
                    "ReloadConfig          = Ctrl+Shift+R",
                });
            }

            var ini = SimpleIni.Load(ConfigPath);
            var cfg = new CTConfig
            {
                // [Languages].
                BaseLanguage          = ini.GetString("Languages", "BaseLanguage",          "en"),
                DefaultRemoteLanguage = ini.GetString("Languages", "DefaultRemoteLanguage", ""),

                // [Hotkeys].
                ReloadConfigHotkey    = ini.GetString("Hotkeys",   "ReloadConfig",          "Ctrl+Shift+R"),
            };

            return cfg;
        }

        public static void LoadApply()
        {
            try
            {
                var cfg = LoadOrCreate();
                cfg.ApplyToStatics();

                // Push into runtime translation state.
                ChatTranslationState.SetBaseLanguage(CTRuntimeConfig.BaseLanguage);

                if (!string.IsNullOrEmpty(CTRuntimeConfig.DefaultRemoteLanguage))
                {
                    // This sets/updates the manual remote language.
                    // (If auto mode is on, it will still behave correctly.)
                    ChatTranslationState.ToggleRemoteLanguage(CTRuntimeConfig.DefaultRemoteLanguage);
                }

                CTHotkeys.SetReloadBinding(cfg.ReloadConfigHotkey);
                ChatTranslationState.LoadConfig();
                // ModLoader.LogSystem.Log($"[Config] Applied from {PathShortener.ShortenForLog(ConfigPath)}.");
            }
            catch (Exception ex)
            {
                Log($"[CTConfig] Failed to load/apply: {ex.Message}.");
            }
        }

        /// <summary>
        /// Applies this CTConfig instance to the global runtime holder (CTRuntimeConfig).
        /// Normalizes language codes and fills the reload hotkey.
        /// </summary>
        public void ApplyToStatics()
        {
            // Normalize languages via ChatTranslationState helper.
            var baseLang = ChatTranslationState.NormalizeLanguageCode(BaseLanguage ?? "en");

            var remote = string.IsNullOrWhiteSpace(DefaultRemoteLanguage)
                ? string.Empty
                : ChatTranslationState.NormalizeLanguageCode(DefaultRemoteLanguage);

            CTRuntimeConfig.BaseLanguage          = baseLang;
            CTRuntimeConfig.DefaultRemoteLanguage = remote;
            CTRuntimeConfig.ReloadConfigHotkey    =
                string.IsNullOrWhiteSpace(ReloadConfigHotkey) ? "Ctrl+Shift+R" : ReloadConfigHotkey;
        }

        /// <summary>
        /// Writes current values back to ChatTranslator.Config.ini.
        /// </summary>
        public static void Save()
        {
            try
            {
                var dir = Path.GetDirectoryName(ConfigPath);
                if (!Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                string[] lines =
                {
                    "# ChatTranslator - Configuration",
                    "# Use ISO language codes like en, es, de, ru, zh, fr, etc.",
                    "# Lines starting with ';' or '#' are comments.",
                    "",
                    "[Languages]",
                    "; Your own baseline language (what you type/read in).",
                    "BaseLanguage          = " + (CTRuntimeConfig.BaseLanguage          ?? "en"),
                    "",
                    "; Optional default remote language when manual mode is used.",
                    "; Leave empty to start with translation off.",
                    "DefaultRemoteLanguage = " + (CTRuntimeConfig.DefaultRemoteLanguage ?? string.Empty),
                    "",
                    "[Hotkeys]",
                    "; Reload this config while in-game:",
                    "ReloadConfig          = " + (CTRuntimeConfig.ReloadConfigHotkey    ?? "Ctrl+Shift+R"),
                };

                File.WriteAllLines(ConfigPath, lines);
            }
            catch (Exception ex)
            {
                Log($"Failed to save config: {ex.Message}.");
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