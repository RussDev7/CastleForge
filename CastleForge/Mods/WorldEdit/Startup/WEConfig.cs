/*
Copyright (c) 2025 RussDev7
This file is part of https://github.com/RussDev7/CastleForge.

This source is subject to the GNU General Public License v3.0 (GPLv3).
See https://www.gnu.org/licenses/gpl-3.0.html.

THIS PROGRAM IS FREE SOFTWARE: YOU CAN REDISTRIBUTE IT AND/OR MODIFY
IT UNDER THE TERMS OF THE GNU GENERAL PUBLIC LICENSE AS PUBLISHED BY
THE FREE SOFTWARE FOUNDATION, EITHER VERSION 3 OF THE LICENSE, OR
(AT YOUR OPTION) ANY LATER VERSION.

THIS PROGRAM IS DISTRIBUTED IN THE HOPE THAT IT WILL BE USEFUL,
BUT WITHOUT ANY WARRANTY; WITHOUT EVEN THE IMPLIED WARRANTY OF
MERCHANTABILITY OR FITNESS FOR A PARTICULAR PURPOSE. SEE THE
GNU GENERAL PUBLIC LICENSE FOR MORE DETAILS.
*/

using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System;

using static WorldEdit.GamePatches;

namespace WorldEdit
{
    /// <summary>
    /// Runtime knobs used by patches / AsyncBlockPlacer.
    /// </summary>
    internal static class AsyncBlockPlacer_Settings
    {
        public static volatile bool Enabled           = true;  // Toggle pipeline on/off.
        public static bool          ShowTelemetry     = false; // Optional debug log.
        public static int           MaxBlocksPerFrame = 2000;  // Per-frame budget.
    }

    /// <summary>
    /// INI-backed config for WEAsyncBlockPlacer.
    /// </summary>
    internal sealed class WEConfig
    {
        // Last applied config snapshot (set by LoadApply) for fast runtime reads without disk I/O.
        internal static volatile WEConfig Active;

        // [Wands].
        // NOTE: These accept either a numeric ID (e.g. "39") or an InventoryItemIDs enum name (e.g. "CopperAxe").
        public bool   GiveWandItemOnEnable = true; // If true, /wand enabling will grant 1x WandItem to the player.
        public string WandItem             = "CopperAxe";
        public string NavWandItem          = "Compass";
        public string NavWandItemPrevious  = "";

        // [Undo].
        // Global history capture toggle. When false, SaveUndo/SaveRedo calls become no-ops.
        public bool UndoRecordingEnabled = true;

        // [AsyncBlockPlacer].
        public bool Enabled           = true;
        public bool ShowTelemetry     = false;
        public int  MaxBlocksPerFrame = 2000;

        // [Hotkeys].
        // Hotkey to reload this config at runtime.
        public string ReloadConfigHotkey = "Ctrl+Shift+R";

        // Paths.
        public static string ConfigPath =>
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "!Mods", "WorldEdit", "WorldEdit.Config.ini");

        public static WEConfig LoadOrCreate()
        {
            var dir = Path.GetDirectoryName(ConfigPath);
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

            if (!File.Exists(ConfigPath))
            {
                File.WriteAllLines(ConfigPath, new[]
                {
                    "# WorldEdit - Configuration",
                    "# Lines starting with ';' or '#' are comments.",
                    "",
                    "[Wands]",
                    "; If true, enabling /wand will add 1x WandItem to your inventory.",
                    "GiveWandItemOnEnable = true",
                    "; The selection wand item (used when /wand is enabled): Left click sets pos1, right click sets pos2.",
                    "WandItem             = CopperAxe",
                    "; Navigation wand (always-on): Left click behaves like /jumpto, right click behaves like /thru.",
                    "NavWandItem          = Compass",
                    "; Stores the last non-none nav-wand item for /navwand toggling (empty -> use default).",
                    "NavWandItemPrevious  = ",
                    "",
                    "[Undo]",
                    "; If true, record undo/redo snapshots (normal editing behavior).",
                    "; If false, skips recording (useful for huge terrain ops; edits still happen).",
                    "UndoRecordingEnabled = true",
                    "",
                    "[AsyncBlockPlacer]",
                    "; Toggle the async world-edit pipeline (AlterBlockMessage per frame).",
                    "Enabled           = true",
                    "; Write a light telemetry line occasionally (frame drain, pending, budget).",
                    "ShowTelemetry     = false",
                    "; Per-frame cap on blocks sent. Higher = faster, but may hitch.",
                    "MaxBlocksPerFrame = 2000",
                    "",
                    "[Hotkeys]",
                    "; Reload this config while in-game:",
                    "ReloadConfig      = Ctrl+Shift+R",
                });
            }

            var ini = SimpleIni.Load(ConfigPath);
            var cfg = new WEConfig
            {
                // [Wands].
                GiveWandItemOnEnable = ini.GetBool("Wands", "GiveWandItemOnEnable", true),
                WandItem             = ini.GetString("Wands", "WandItem", "CopperAxe"),
                NavWandItem          = ini.GetString("Wands", "NavWandItem", "Compass"),
                NavWandItemPrevious  = ini.GetString("Wands", "NavWandItemPrevious", ""),

                // [Undo].
                UndoRecordingEnabled = ini.GetBool("Undo", "UndoRecordingEnabled", true),

                // [AsyncBlockPlacer].
                Enabled            = ini.GetBool("AsyncBlockPlacer", "Enabled", true),
                ShowTelemetry      = ini.GetBool("AsyncBlockPlacer", "ShowTelemetry", false),
                MaxBlocksPerFrame  = Clamp(ini.GetInt("AsyncBlockPlacer", "MaxBlocksPerFrame", 2000), 1, 20000),

                // [Hotkeys].
                ReloadConfigHotkey = ini.GetString("Hotkeys", "ReloadConfig", "Ctrl+Shift+R"),
            };
            return cfg;
        }

        public static void LoadApply()
        {
            try
            {
                var cfg = LoadOrCreate();
                Active = cfg;
                cfg.ApplyToStatics();
                WEHotkeys.SetReloadBinding(cfg.ReloadConfigHotkey);
                // ModLoader.LogSystem.Log($"[Config] Applied from {PathShortener.ShortenForLog(ConfigPath)}.");
            }
            catch (Exception ex)
            {
                ModLoader.LogSystem.Log($"[WEConfig] Failed to load/apply: {ex.Message}.");
            }
        }

        /// <summary>
        /// Updates the nav-wand keys on disk.
        /// Summary: Used by /navwand to toggle NavWandItem and remember the last non-none item.
        /// </summary>
        public static void UpdateNavWandConfig(string navWandItem, string navWandItemPrevious)
        {
            // Ensure the file exists (and has at least the baseline sections).
            LoadOrCreate();

            var lines = new List<string>(File.ReadAllLines(ConfigPath));

            UpsertIniKey(lines, "Wands", "NavWandItem", navWandItem ?? "", 18);
            UpsertIniKey(lines, "Wands", "NavWandItemPrevious", navWandItemPrevious ?? "", 18);

            File.WriteAllLines(ConfigPath, lines.ToArray());
        }

        /// <summary>
        /// Updates the undo-recording toggle on disk.
        /// Summary: Used by /undorecord to persist <see cref="WorldEditCore._undoRecordingEnabled"/>.
        /// </summary>
        public static void UpdateUndoRecordingConfig(bool enabled)
        {
            // Ensure the file exists (and has at least the baseline sections).
            LoadOrCreate();

            var lines = new List<string>(File.ReadAllLines(ConfigPath));

            UpsertIniKey(lines, "Undo", "UndoRecordingEnabled", enabled ? "true" : "false", 20);

            File.WriteAllLines(ConfigPath, lines.ToArray());
        }

        public void ApplyToStatics()
        {
            AsyncBlockPlacer_Settings.Enabled           = Enabled;
            AsyncBlockPlacer_Settings.ShowTelemetry     = ShowTelemetry;
            AsyncBlockPlacer_Settings.MaxBlocksPerFrame = MaxBlocksPerFrame;

            // Apply undo history capture toggle.
            // Summary: When disabled, world edits still apply but no undo/redo snapshots are recorded.
            WorldEditCore._undoRecordingEnabled = UndoRecordingEnabled;

            // Apply wand items.
            // Summary: Keeps both the selection wand and the navigation wand configurable without code changes.
            WorldEditCore.WandItemID    = ResolveInventoryItemId(WandItem, DNA.CastleMinerZ.Inventory.InventoryItemIDs.CopperAxe);
            WorldEditCore.NavWandItemID = ResolveInventoryItemId(NavWandItem, DNA.CastleMinerZ.Inventory.InventoryItemIDs.Compass);
        }

        // Helpers.

        /// <summary>
        /// Resolves an <see cref="InventoryItemIDs"/> from a config value.
        /// Summary: Supports either a numeric ID (e.g. "39") or an enum name (e.g. "CopperAxe").
        /// </summary>
        private static int ResolveInventoryItemId(string value, DNA.CastleMinerZ.Inventory.InventoryItemIDs fallback)
        {
            // Allow a friendly "none" to disable a wand item without removing the key.
            if (string.IsNullOrWhiteSpace(value) ||
                value.Equals("none",     StringComparison.OrdinalIgnoreCase) ||
                value.Equals("off",      StringComparison.OrdinalIgnoreCase) ||
                value.Equals("disabled", StringComparison.OrdinalIgnoreCase))
            {
                return -1;
            }

            // Numeric ID support.
            if (int.TryParse(value, out int numeric))
                return numeric;

            // Enum name support (case-insensitive).
            string cleaned = value.Trim();
            cleaned = StripPrefixIgnoreCase(cleaned, "DNA.CastleMinerZ.Inventory.InventoryItemIDs.");
            cleaned = StripPrefixIgnoreCase(cleaned, "InventoryItemIDs.");

            if (Enum.TryParse(cleaned, ignoreCase: true, out DNA.CastleMinerZ.Inventory.InventoryItemIDs parsed))
                return (int)parsed;

            ModLoader.LogSystem.Log($"[WEConfig] Invalid item '{value}'. Falling back to '{fallback}'.");
            return (int)fallback;
        }

        /// <summary>
        /// Removes the given prefix from <paramref name="value"/> if present (case-insensitive).
        /// Summary: Used to normalize config strings that may include fully-qualified enum prefixes.
        /// </summary>
        private static string StripPrefixIgnoreCase(string value, string prefix)
        {
            if (string.IsNullOrEmpty(value) || string.IsNullOrEmpty(prefix))
                return value;

            return value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                ? value.Substring(prefix.Length)
                : value;
        }

        /// <summary>
        /// Inserts or updates a key/value pair inside an INI section (case-insensitive).
        /// Summary: This edits only the requested key and preserves unrelated lines and comments.
        /// </summary>
        private static void UpsertIniKey(List<string> lines, string sectionName, string key, string value, int padWidth)
        {
            if (lines == null) throw new ArgumentNullException(nameof(lines));
            if (string.IsNullOrWhiteSpace(sectionName)) throw new ArgumentException("Section is required.", nameof(sectionName));
            if (string.IsNullOrWhiteSpace(key)) throw new ArgumentException("Key is required.", nameof(key));

            if (!TryFindSectionRange(lines, sectionName, out int startIndex, out int endIndex))
            {
                // Create the section at the end if it does not exist.
                if (lines.Count > 0 && !string.IsNullOrWhiteSpace(lines[lines.Count - 1]))
                    lines.Add("");

                lines.Add("[" + sectionName + "]");
                startIndex = lines.Count - 1;
                endIndex = lines.Count;
            }

            // Try to replace an existing key line inside the section.
            for (int i = startIndex + 1; i < endIndex; i++)
            {
                string raw = lines[i] ?? "";
                string trimmed = raw.TrimStart();

                if (trimmed.Length == 0) continue;
                if (trimmed[0] == ';' || trimmed[0] == '#') continue;
                if (trimmed[0] == '[') break; // Next section (should not happen inside our range, but safe).

                int eq = trimmed.IndexOf('=');
                if (eq <= 0) continue;

                string k = trimmed.Substring(0, eq).Trim();
                if (!k.Equals(key, StringComparison.OrdinalIgnoreCase)) continue;

                string leading = raw.Substring(0, raw.Length - trimmed.Length);
                lines[i] = leading + key.PadRight(padWidth) + "= " + (value ?? "");
                return;
            }

            // Not found -> insert before the next section header (endIndex).
            lines.Insert(endIndex, key.PadRight(padWidth) + "= " + (value ?? ""));
        }

        /// <summary>
        /// Finds the bounds of a section in an INI file.
        /// Summary: Returns the index of the section header and the index of the next header (or EOF).
        /// </summary>
        private static bool TryFindSectionRange(List<string> lines, string sectionName, out int startIndex, out int endIndex)
        {
            startIndex = -1;
            endIndex = -1;

            for (int i = 0; i < lines.Count; i++)
            {
                string line = (lines[i] ?? "").Trim();
                if (line.Length < 3) continue;

                if (line.StartsWith("[") && line.EndsWith("]"))
                {
                    string name = line.Substring(1, line.Length - 2).Trim();
                    if (name.Equals(sectionName, StringComparison.OrdinalIgnoreCase))
                    {
                        startIndex = i;
                        endIndex = lines.Count;

                        // Find next section header.
                        for (int j = i + 1; j < lines.Count; j++)
                        {
                            string n = (lines[j] ?? "").Trim();
                            if (n.StartsWith("[") && n.EndsWith("]"))
                            {
                                endIndex = j;
                                break;
                            }
                        }

                        return true;
                    }
                }
            }

            return false;
        }

        /// <summary>
        /// Clamps an integer to an inclusive range.
        /// Summary: Ensures <paramref name="v"/> stays within <paramref name="lo"/>.. <paramref name="hi"/>.
        /// </summary>
        private static int Clamp(int v, int lo, int hi) => (v < lo) ? lo : (v > hi ? hi : v);
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
}