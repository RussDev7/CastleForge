/*
SPDX-License-Identifier: GPL-3.0-or-later
Copyright (c) 2025 RussDev7
This file is part of https://github.com/RussDev7/CastleForge - see LICENSE for details.
*/

using DNA.CastleMinerZ.Inventory;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.IO;
using System;

namespace TooManyItems
{
    /// <summary>
    /// Persists / restores the TMI user state to:
    ///   <Game>\!Mods\<Namespace>\TooManyItems.UserData.ini
    ///
    /// Data stored:
    ///   • UI overlay toggle           ([UI]/Enabled = true|false)
    ///   • Favorites list              (InventoryItemIDs[])
    ///   • Named save-slots            (string[])
    ///   • Slot snapshots (trays+bag)  (per-slot InvSnapshot)
    ///
    /// Notes:
    ///   - Idempotent + silent: LoadAllOnce() is safe to call multiple times and
    ///     fails quietly (useful when user logging is disabled).
    ///   - Schema/version guard: If [State]/Version < STATE_VERSION, the existing
    ///     file is rotated to a timestamped "*.old" backup and a fresh, current-schema
    ///     file is written (minimal, empty sections) before loading continues.
    ///   - Culture-stable I/O: All numeric serialization uses InvariantCulture.
    ///   - Dependencies: BCL-only plus the existing SimpleIni reader.
    ///   - Save convenience: SaveFavorites/SaveSlots/SaveNames currently delegate to SaveAll().
    ///   - Robust parsing: Missing/extra sections are tolerated; corrupt entries are skipped,
    ///     allowing the game to continue with partial state.
    ///   - UI toggle persistence: The overlay's Enabled flag is cached in-process and emitted
    ///     under [UI]/Enabled. On first run (no file), a default is assumed; thereafter the
    ///     persisted value initializes the overlay at startup. Toggling the UI will call
    ///     SetOverlayEnabled() immediately to persist the new state.
    /// </summary>
    internal static class TMIState
    {
        #region Constants & Paths

        private const int    STATE_VERSION = 1;        // Reserved for future migrations.
        private static bool  _loadedOnce;              // Gate to prevent multiple loads.
        private static bool? _overlayEnabledCached;    // Overlay Enabled persistence.
        private static bool? _hardBlocksEnabledCached; // Allow mining very-hard blocks.
        private static int?  _hardBlockTierCached;     // 0-12 tool tier requirement.

        private static string Dir      => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "!Mods", typeof(TMIState).Namespace);
        private static string FilePath => Path.Combine(Dir, "TooManyItems.UserData.ini");

        #endregion

        #region Public API

        /// <summary>
        /// Load user state once per process. Subsequent calls are no-ops.
        /// Safe to call at mod startup and/or first overlay open.
        /// </summary>
        public static void LoadAllOnce()
        {
            if (_loadedOnce) return;
            _loadedOnce = true;

            try
            {
                if (!File.Exists(FilePath))
                {
                    // First run: Default ON; file will be created on first save.
                    _overlayEnabledCached = true;
                    return;
                }

                // Ensure file is at or above our schema version (future-proofing).
                if (!EnsureCurrentSchema())
                {
                    // If upgrade/reset failed, bail quietly (matches the silent policy).
                    return;
                }

                // From here on, the file is guaranteed to be current.
                var ini = SimpleIni.Load(FilePath);

                LoadFavorites(ini);
                var names = LoadSlotNames(ini);
                LoadSlots(ini, names);
            }
            catch (Exception)
            {
                // Intentionally silent (user may have logging set to None).
                // Corrupt/missing sections are simply ignored so the game still runs.
            }
        }

        /// <summary>
        /// Write out ALL state (favorites, slot names, slot data) in one go.
        /// </summary>
        public static void SaveAll()
        {
            try
            {
                if (!Directory.Exists(Dir))
                    Directory.CreateDirectory(Dir);

                var sb = new StringBuilder(8192);

                // Header / version (for future migrations).
                sb.AppendLine("[State]");
                sb.AppendLine($"Version={STATE_VERSION}");
                sb.AppendLine();

                WriteUI(sb);
                WriteFavorites(sb);
                WriteSlotNames(sb);
                WriteSlots(sb);

                File.WriteAllText(FilePath, sb.ToString());
            }
            catch (Exception)
            {
                // Intentionally silent (respect quiet mode).
            }
        }

        // Convenience "partial" saves (currently same as SaveAll for simplicity).
        public static void SaveFavorites() => SaveAll();
        public static void SaveSlots()     => SaveAll();
        public static void SaveNames()     => SaveAll();

        #endregion

        #region Schema Guard (Upgrade/Rotate)

        /// <summary>
        /// Ensures the on-disk INI matches the expected STATE_VERSION.
        /// If older, rotate to *.old-YYYYMMDD_HHMMSS and write a fresh file.
        /// Returns true if the file is usable after this call.
        /// </summary>
        private static bool EnsureCurrentSchema()
        {
            try
            {
                var ini = SimpleIni.Load(FilePath);
                int version = SafeGetVersion(ini);  // Missing/invalid -> 0.

                if (version >= STATE_VERSION)
                    return true; // Up-to-date.

                // Older schema: Back up and write a fresh file.
                RotateToBackup(FilePath);
                WriteFreshFile();
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static int SafeGetVersion(SimpleIni ini)
        {
            try { return ini.GetInt("State", "Version", 0); }
            catch { return 0; }
        }

        /// <summary> Renames path to a unique *.old-YYYYMMDD_HHMMSS file in the same directory. </summary>
        private static void RotateToBackup(string path)
        {
            try
            {
                var dir = Path.GetDirectoryName(path) ?? ".";
                var name = Path.GetFileName(path);
                var ts = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                var bak = Path.Combine(dir, $"{name}.{ts}.old");

                // If somehow exists, add a counter.
                int i = 1;
                while (File.Exists(bak))
                    bak = Path.Combine(dir, $"{name}.{ts}.{i++}.old");

                File.Move(path, bak);
            }
            catch
            {
                // Ignore - If rotation fails, caller will likely fail writing fresh too, and we bail quietly.
            }
        }

        /// <summary>
        /// Writes a minimal, valid file with the current schema.
        /// Keeps content empty (favorites/slots) so the app can start cleanly.
        /// </summary>
        private static void WriteFreshFile()
        {
            try
            {
                if (!Directory.Exists(Dir))
                    Directory.CreateDirectory(Dir);

                var sb = new StringBuilder(512);
                sb.AppendLine("; TooManyItems per-user state (auto-generated).");
                sb.AppendLine("; Delete this file to reset the saved UI state.");
                sb.AppendLine();

                sb.AppendLine("[State]");
                sb.AppendLine($"Version={STATE_VERSION}");
                sb.AppendLine();

                sb.AppendLine("[UI]");
                sb.AppendLine("Enabled=true");
                sb.AppendLine("HardBlocksEnabled=false");
                sb.AppendLine("HardBlockTier=12");
                sb.AppendLine();

                sb.AppendLine("[Favorites]");
                sb.AppendLine("Items=");
                sb.AppendLine();

                sb.AppendLine("[SlotNames]");
                sb.AppendLine("Count=0");
                sb.AppendLine();

                // No [SlotX] sections by default.
                File.WriteAllText(FilePath, sb.ToString());
            }
            catch
            {
                // Silent.
            }
        }
        #endregion

        #region UI Enabled (Overlay) - Read/Write Helpers

        /// <summary>
        /// Returns the persisted overlay Enabled value. If no file/entry exists,
        /// returns <paramref name="defaultIfMissing"/> and caches it (so the next SaveAll will write it).
        /// </summary>
        public static bool GetOverlayEnabled(bool defaultIfMissing = true)
        {
            // If we've already read (or set) it this session, return cached.
            if (_overlayEnabledCached.HasValue)
                return _overlayEnabledCached.Value;

            try
            {
                if (!File.Exists(FilePath))
                {
                    _overlayEnabledCached = defaultIfMissing;
                    return defaultIfMissing;
                }

                var ini = SimpleIni.Load(FilePath);

                // Fallback to defaultIfMissing if [UI]/Enabled is missing/bad.
                var raw      = ini.GetString("UI", "Enabled", defaultIfMissing ? "true" : "false");
                bool parsed  = false;
                bool enabled = defaultIfMissing;
                if      (bool.TryParse(raw, out var b)) { parsed = true; enabled = b;        }
                else if (int.TryParse(raw, out var i))  { parsed = true; enabled = (i != 0); }

                _overlayEnabledCached = parsed ? enabled : defaultIfMissing;
                return _overlayEnabledCached.Value;
            }
            catch
            {
                _overlayEnabledCached = defaultIfMissing;
                return defaultIfMissing;
            }
        }

        /// <summary>
        /// Set and persist the overlay Enabled state. Writes the full UserData file
        /// (same as other Save* helpers) so everything stays in one place.
        /// </summary>
        public static void SetOverlayEnabled(bool enabled)
        {
            _overlayEnabledCached = enabled;
            SaveAll(); // Simple & robust: Re-emit the full file with updated [UI].
        }
        #endregion

        #region Hard Blocks Settings (Read/Write Helpers)

        /// <summary>
        /// Reads whether mining very-hard blocks is enabled from the ini (UI.HardBlocksEnabled),
        /// falling back to the given default when missing or on error.
        /// </summary>
        public static bool GetHardBlocksEnabled(bool defaultIfMissing = false)
        {
            if (_hardBlocksEnabledCached.HasValue)
                return _hardBlocksEnabledCached.Value;

            try
            {
                if (!File.Exists(FilePath))
                {
                    _hardBlocksEnabledCached = defaultIfMissing;
                    return defaultIfMissing;
                }

                var ini = SimpleIni.Load(FilePath);
                var raw = ini.GetString("UI", "HardBlocksEnabled", defaultIfMissing ? "true" : "false");

                bool parsed  = false;
                bool enabled = defaultIfMissing;
                if      (bool.TryParse(raw, out var b)) { parsed = true; enabled = b;        }
                else if (int.TryParse(raw, out var i))  { parsed = true; enabled = (i != 0); }

                _hardBlocksEnabledCached = parsed ? enabled : defaultIfMissing;
                return _hardBlocksEnabledCached.Value;
            }
            catch
            {
                _hardBlocksEnabledCached = defaultIfMissing;
                return defaultIfMissing;
            }
        }

        /// <summary>
        /// Updates the in-memory flag for mining very-hard blocks and writes it back to the ini.
        /// </summary>
        public static void SetHardBlocksEnabled(bool enabled)
        {
            _hardBlocksEnabledCached = enabled;
            SaveAll();
        }

        /// <summary>
        /// Reads the required tool tier (0-12) for mining very-hard blocks from the ini,
        /// clamping to the valid range and falling back to the given default on error.
        /// </summary>
        public static int GetHardBlockTier(int defaultIfMissing = 12)
        {
            if (_hardBlockTierCached.HasValue)
                return _hardBlockTierCached.Value;

            try
            {
                if (!File.Exists(FilePath))
                {
                    _hardBlockTierCached = defaultIfMissing;
                    return defaultIfMissing;
                }

                var ini = SimpleIni.Load(FilePath);
                int tier = ini.GetInt("UI", "HardBlockTier", defaultIfMissing);
                tier = Math.Max(0, Math.Min(12, tier));

                _hardBlockTierCached = tier;
                return tier;
            }
            catch
            {
                _hardBlockTierCached = defaultIfMissing;
                return defaultIfMissing;
            }
        }

        /// <summary>
        /// Sets the required tool tier (0-12) for mining very-hard blocks and writes it to the ini.
        /// </summary>
        public static void SetHardBlockTier(int tier)
        {
            tier = Math.Max(0, Math.Min(12, tier));
            _hardBlockTierCached = tier;
            SaveAll();
        }
        #endregion

        #region Load Helpers

        private static void LoadFavorites(SimpleIni ini)
        {
            var csv = ini.GetString("Favorites", "Items", "");
            var list = new List<InventoryItemIDs>();

            if (!string.IsNullOrWhiteSpace(csv))
            {
                foreach (var tok in csv.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    if (int.TryParse(tok.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var num))
                        list.Add((InventoryItemIDs)num);
                }
            }

            // Replace existing favorites with those read from disk.
            TMIOverlay.ImportFavorites(list, clearFirst: true);
        }

        private static string[] LoadSlotNames(SimpleIni ini)
        {
            string[] names = null;

            int count = ini.GetInt("SlotNames", "Count", 0);
            if (count > 0)
            {
                names = new string[count];
                for (int i = 0; i < count; i++)
                    names[i] = ini.GetString("SlotNames", $"Name{i}", null);

                TMIOverlay.ImportSlotNames(names);
            }

            return names;
        }

        private static void LoadSlots(SimpleIni ini, string[] names)
        {
            // If we know the names length, prefer that; else try default 0..31.
            int upTo = (names?.Length ?? 32);

            var map = new Dictionary<int, TMIOverlayActions.InvSnapshot>();

            for (int i = 0; i < upTo; i++)
            {
                string sec = $"Slot{i}";
                var n = ini.GetString(sec, "Name", null);
                if (n == null) continue; // Section doesn't exist -> Skip.

                var snap = new TMIOverlayActions.InvSnapshot();

                // Trays (2 x 8).
                for (int t = 0; t < 2; t++)
                {
                    for (int s = 0; s < 8; s++)
                    {
                        var raw = ini.GetString(sec, $"Tray{t}{s}", "");
                        snap.Trays[t, s] = ParseItem(raw);
                    }
                }

                // Bag (variable length).
                int bagLen = Math.Max(0, ini.GetInt(sec, "BagLen", 0));
                snap.Bag = new TMIOverlayActions.ItemData[bagLen];
                for (int b = 0; b < bagLen; b++)
                {
                    var raw = ini.GetString(sec, $"Bag{b}", "");
                    snap.Bag[b] = ParseItem(raw);
                }

                map[i] = snap;
            }

            // Install the complete slot map.
            TMIOverlayActions.ImportAllSlots(map);
        }
        #endregion

        #region Save Helpers

        private static void WriteUI(StringBuilder sb)
        {
            var on       = _overlayEnabledCached    ?? true;  // Default when missing.
            var hardOn   = _hardBlocksEnabledCached ?? false;
            var hardTier = _hardBlockTierCached     ?? 12;

            // Clamp to sane ranges before writing.
            hardTier     = Math.Max(0, Math.Min(12, hardTier));

            sb.AppendLine("[UI]");
            sb.AppendLine($"Enabled={(on ? "true" : "false")}");
            sb.AppendLine($"HardBlocksEnabled={(hardOn ? "true" : "false")}");
            sb.AppendLine($"HardBlockTier={hardTier}");
            sb.AppendLine();
        }

        private static void WriteFavorites(StringBuilder sb)
        {
            var favs = TMIOverlay.ExportFavorites();
            var csv  = string.Join(",", favs.Select(id => ((int)id).ToString(CultureInfo.InvariantCulture)));

            sb.AppendLine("[Favorites]");
            sb.AppendLine($"Items={csv}");
            sb.AppendLine();
        }

        private static void WriteSlotNames(StringBuilder sb)
        {
            var names = TMIOverlay.ExportSlotNames() ?? Array.Empty<string>();

            sb.AppendLine("[SlotNames]");
            sb.AppendLine($"Count={names.Length}");
            for (int i = 0; i < names.Length; i++)
            {
                // Make INI-friendly (no newlines).
                var safe = (names[i] ?? string.Empty).Replace("\r", "").Replace("\n", " ");
                sb.AppendLine($"Name{i}={safe}");
            }
            sb.AppendLine();
        }

        private static void WriteSlots(StringBuilder sb)
        {
            var all = TMIOverlayActions.ExportAllSlots(); // IReadOnlyDictionary<int, InvSnapshot>.
            var names = TMIOverlay.ExportSlotNames() ?? Array.Empty<string>();

            foreach (var kv in all.OrderBy(k => k.Key))
            {
                int i = kv.Key;
                var snap = kv.Value;

                sb.AppendLine($"[Slot{i}]");

                // Name is optional: Mirror the name array for convenience.
                var name = (i >= 0 && i < names.Length) ? names[i] : null;
                sb.AppendLine($"Name={(name ?? "snapshot")}");

                // trays 2 x 8.
                for (int t = 0; t < 2; t++)
                    for (int s = 0; s < 8; s++)
                        sb.AppendLine($"Tray{t}{s}={EncodeItem(snap.Trays[t, s])}");

                // Bag.
                int bl = snap.Bag?.Length ?? 0;
                sb.AppendLine($"BagLen={bl}");
                for (int b = 0; b < bl; b++)
                    sb.AppendLine($"Bag{b}={EncodeItem(snap.Bag[b])}");

                sb.AppendLine();
            }
        }
        #endregion

        #region Encoding Helpers (Item <-> CSV)

        /// <summary> Serialize an item as "id,stack,health[,clip]" or empty for null. </summary>
        private static string EncodeItem(TMIOverlayActions.ItemData d)
        {
            if (d == null) return string.Empty;

            var h = d.Health.ToString("0.###", CultureInfo.InvariantCulture);
            return d.Clip.HasValue
                ? $"{(int)d.Id},{d.Stack},{h},{d.Clip.Value}"
                : $"{(int)d.Id},{d.Stack},{h}";
        }

        /// <summary> Parse "id,stack,health[,clip]" -> ItemData (null on malformed). </summary>
        private static TMIOverlayActions.ItemData ParseItem(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return null;

            var p = raw.Split(',');
            if (p.Length < 3) return null;

            if (!int.TryParse(p[0].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var id)) return null;
            if (!int.TryParse(p[1].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var st)) st = 1;
            if (!float.TryParse(p[2].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var hp)) hp = 1f;

            int? clip = null;
            if (p.Length >= 4 && int.TryParse(p[3].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var c))
                clip = c;

            return new TMIOverlayActions.ItemData
            {
                Id     = (InventoryItemIDs)id,
                Stack  = Math.Max(0, st),
                Health = Math.Max(0f, Math.Min(1f, hp)),
                Clip   = clip
            };
        }
        #endregion
    }
}