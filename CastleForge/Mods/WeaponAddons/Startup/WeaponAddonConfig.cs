/*
SPDX-License-Identifier: GPL-3.0-or-later
Copyright (c) 2025 RussDev7
This file is part of https://github.com/RussDev7/CastleForge - see LICENSE for details.
*/

using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System;

using static WeaponAddons.GamePatches;

namespace WeaponAddons
{
    // =========================================================================================
    // WeaponAddons - INI Configuration
    // =========================================================================================
    //
    // Summary:
    // - Provides a small INI-backed config for the WeaponAddons system.
    // - Controls:
    //   • Global enable/disable toggle.
    //   • Slot mappings: PackFolderName -> InventoryItemIDs (or numeric ID).
    //   • A reload hotkey string (binding handled elsewhere).
    //
    // Notes:
    // - The INI format is intentionally simple:
    //   • Case-insensitive sections/keys.
    //   • key=value pairs, no escaping, no multi-line values.
    //   • ';' and '#' comment lines.
    // - [Slots] are parsed by scanning raw file lines on purpose (keeps SimpleIni minimal).
    // =========================================================================================

    #region WeaponAddonConfig (Public Config Shape)

    /// <summary>
    /// WeaponAddonConfig
    /// -----------------
    /// In-memory representation of WeaponAddons.Config.ini.
    ///
    /// Summary:
    /// - Enabled: master toggle for the WeaponAddons pipeline.
    /// - Slots: maps pack folder name -> target inventory slot (enum name or numeric).
    /// - ReloadConfigHotkey: textual binding for config reload (e.g. "Ctrl+Shift+R").
    ///
    /// Notes:
    /// - Slots are loaded from the [Slots] section by scanning the file directly so we can:
    ///   • preserve the "simple INI" style
    ///   • avoid adding more complex parsing behavior to SimpleIni
    /// </summary>
    internal sealed class WeaponAddonConfig
    {
        #region Settings

        // Last applied config snapshot (set by LoadApply) for fast runtime reads without disk I/O.
        internal static volatile WeaponAddonConfig Active;

        public bool Enabled = true;

        // FolderName -> slot ID string ("PrecisionLaser" or "123").
        public Dictionary<string, string> Slots = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        public static readonly Dictionary<string, int> NewItemIds = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        public string ReloadConfigHotkey = "Ctrl+Shift+R";

        #endregion

        #region Paths

        public static string ConfigPath =>
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "!Mods", "WeaponAddons", "WeaponAddons.Config.ini");

        #endregion

        #region Load / Create

        /// <summary>
        /// Loads WeaponAddons.Config.ini or creates a default file if it does not exist.
        ///
        /// Summary:
        /// - Ensures the config directory exists.
        /// - Writes a minimal starter config if missing.
        /// - Uses SimpleIni for core reads (General/Hotkeys).
        /// - Scans raw lines to build the [Slots] mapping.
        ///
        /// Notes:
        /// - Raw scan for [Slots] is intentional:
        ///   • It keeps SimpleIni tiny and predictable.
        ///   • It allows simple comments and formatting without more parser rules.
        /// </summary>
        public static WeaponAddonConfig LoadOrCreate()
        {
            var dir = Path.GetDirectoryName(ConfigPath);
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

            if (!File.Exists(ConfigPath))
            {
                File.WriteAllLines(ConfigPath, new[]
                {
                    "# WeaponAddons - Configuration",
                    "# Lines starting with ';' or '#' are comments.",
                    "",
                    "[General]",
                    "Enabled = true",
                    "",
                    "[Slots]",
                    "; Map pack folder name -> InventoryItemIDs (or numeric).",
                    "; Raygun = PrecisionLaser",
                    "",
                    "[Hotkeys]",
                    "ReloadConfig = Ctrl+Shift+R",
                });
            }

            var ini = SimpleIni.Load(ConfigPath);

            var cfg = new WeaponAddonConfig
            {
                Enabled = ini.GetBool("General", "Enabled", true),
                ReloadConfigHotkey = ini.GetString("Hotkeys", "ReloadConfig", "Ctrl+Shift+R"),
            };

            // Read [Slots] by scanning raw file (SimpleIni is minimal by design).
            // This preserves your "simple INI" feel and avoids adding complexity.
            string section = "";
            foreach (var raw in File.ReadAllLines(ConfigPath))
            {
                var line = raw.Trim();
                if (line.Length == 0) continue;
                if (line.StartsWith(";") || line.StartsWith("#")) continue;

                if (line.StartsWith("[") && line.EndsWith("]"))
                {
                    section = line.Substring(1, line.Length - 2).Trim();
                    continue;
                }

                if (!section.Equals("Slots", StringComparison.OrdinalIgnoreCase))
                    continue;

                int eq = line.IndexOf('=');
                if (eq <= 0) continue;

                var k = line.Substring(0, eq).Trim();
                var v = line.Substring(eq + 1).Trim();
                if (k.Length == 0) continue;

                cfg.Slots[k] = v;
            }

            NewItemIds.Clear();
            foreach (var kv in ini.GetSection("NewItemIds"))
            {
                if (int.TryParse(kv.Value, out var id) && id > 0)
                    NewItemIds[kv.Key] = id;
            }

            return cfg;
        }

        public static void LoadApply()
        {
            try
            {
                var cfg = LoadOrCreate();
                Active = cfg;
                WAHotkeys.SetReloadBinding(cfg.ReloadConfigHotkey);
                // ModLoader.LogSystem.Log($"[Config] Applied from {PathShortener.ShortenForLog(ConfigPath)}.");
            }
            catch (Exception ex)
            {
                ModLoader.LogSystem.Log($"[WAConfig] Failed to load/apply: {ex.Message}.");
            }
        }

        public static void Save()
        {
            try
            {
                var dir = Path.GetDirectoryName(ConfigPath);
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                if (!File.Exists(ConfigPath))
                    LoadOrCreate(); // creates default

                var src = new List<string>(File.ReadAllLines(ConfigPath));
                var dst = new List<string>(src.Count + 32);

                bool inNewIds = false;
                bool wroteNewIds = false;

                for (int i = 0; i < src.Count; i++)
                {
                    var line = src[i];

                    var t = line.Trim();
                    bool isHeader = t.StartsWith("[") && t.EndsWith("]");

                    if (isHeader)
                    {
                        var name = t.Substring(1, t.Length - 2).Trim();

                        // leaving [NewItemIds]
                        if (inNewIds)
                            inNewIds = false;

                        if (name.Equals("NewItemIds", StringComparison.OrdinalIgnoreCase))
                        {
                            // replace section body
                            dst.Add(line); // keep original header line formatting
                            dst.Add("; PackKey = ItemId (numeric). Autogenerated/updated by WeaponAddons.");
                            foreach (var kv in NewItemIds)
                                dst.Add($"{kv.Key} = {kv.Value}");

                            wroteNewIds = true;
                            inNewIds = true;
                            continue; // skip original header handling below
                        }
                    }

                    if (inNewIds)
                    {
                        // skip old body lines until next header
                        continue;
                    }

                    dst.Add(line);
                }

                if (!wroteNewIds)
                {
                    dst.Add("");
                    dst.Add("[NewItemIds]");
                    dst.Add("; PackKey = ItemId (numeric). Autogenerated/updated by WeaponAddons.");
                    foreach (var kv in NewItemIds)
                        dst.Add($"{kv.Key} = {kv.Value}");
                }

                File.WriteAllLines(ConfigPath, dst.ToArray());
            }
            catch { /* best-effort */ }
        }
        #endregion

        #region SimpleIni (Minimal Reader)

        /// <summary>
        /// SimpleIni
        /// ---------
        /// Tiny, case-insensitive INI reader.
        /// Supports [Section], key=value, ';' or '#' comments. No escaping, no multi-line.
        ///
        /// Summary:
        /// - Stores values in:
        ///     section -> (key -> value)
        /// - Ignores unknown/malformed lines.
        /// - Provides small helpers for common reads (string/bool).
        ///
        /// Notes:
        /// - This stays intentionally minimal; specialized parsing (like [Slots]) may be done
        ///   by direct file scans elsewhere to keep this class stable and predictable.
        /// </summary>
        internal sealed class SimpleIni
        {
            private readonly Dictionary<string, Dictionary<string, string>> _data =
                new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);

            /// <summary>
            /// Load
            /// ----
            /// Reads an INI file into the internal dictionary structure.
            ///
            /// Summary:
            /// - Skips blank lines and comment lines.
            /// - Tracks current section via [SectionName].
            /// - Parses key/value pairs using the first '=' character.
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

                    if (line.StartsWith("[") && line.EndsWith("]"))
                    {
                        section = line.Substring(1, line.Length - 2).Trim();
                        if (!ini._data.ContainsKey(section))
                            ini._data[section] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                        continue;
                    }

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
            /// Reads a string from [section] key=... or returns def if missing.
            /// </summary>
            public string GetString(string section, string key, string def)
                => (_data.TryGetValue(section, out var d) && d.TryGetValue(key, out var v)) ? v : def;

            /// <summary>
            /// Reads a bool from [section] key=...
            ///
            /// Summary:
            /// - Accepts "true/false" or "0/1" (any non-zero integer = true).
            /// - Returns def if missing/invalid.
            /// </summary>
            public bool GetBool(string section, string key, bool def)
            {
                var s = GetString(section, key, def ? "true" : "false");
                if (bool.TryParse(s, out var b)) return b;
                if (int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var i)) return i != 0;
                return def;
            }

            public IEnumerable<KeyValuePair<string, string>> GetSection(string section)
            {
                if (section == null) yield break;
                if (_data.TryGetValue(section, out var d))
                    foreach (var kv in d)
                        yield return kv;
            }
        }
        #endregion
    }
    #endregion
}