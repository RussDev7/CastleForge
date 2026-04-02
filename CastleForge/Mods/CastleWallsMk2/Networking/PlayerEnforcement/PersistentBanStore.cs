/*
SPDX-License-Identifier: GPL-3.0-or-later
Copyright (c) 2025 RussDev7
This file is part of https://github.com/RussDev7/CastleForge - see LICENSE for details.
*/

using System.Collections.Generic;
using DNA.Net.GamerServices;
using System.Globalization;
using System.Linq;
using System.Text;
using System.IO;
using System;

namespace CastleWallsMk2
{
    /// <summary>
    /// Persistent storage for Player Enforcement bans.
    ///
    /// Summary:
    /// - Stores SteamID-backed bans and Gamertag-backed bans.
    /// - Supports loading/saving a mixed ban list from disk.
    /// - Provides lookup helpers for host and off-host enforcement paths.
    ///
    /// Notes:
    /// - SteamID entries are used when a real Steam identity is known.
    /// - Gamertag entries are used when only a name is available (for example, some off-host flows).
    /// - The store maintains two indexes:
    ///   - _entries           : keyed by SteamID
    ///   - _entriesByGamertag : keyed by normalized Gamertag
    /// - Snapshot/save logic merges both dictionaries and de-duplicates entries.
    /// </summary>
    internal static class PersistentBanStore
    {
        #region Entry Model

        /// <summary>
        /// Single persisted ban entry.
        ///
        /// Summary:
        /// - SteamId        : non-zero when a real Steam identity is known.
        /// - Name           : display name used when showing the entry.
        /// - MatchGamertag  : normalized/local name match key.
        /// - Mode           : "VanillaBan" or "HardBan".
        /// - Reason         : optional stored reason / message.
        /// - CreatedUtc     : when this entry was created.
        /// </summary>
        internal sealed class Entry
        {
            public ulong SteamId;
            public string Name;
            public string MatchGamertag;
            public string Mode;        // "VanillaBan" or "HardBan"
            public string Reason;
            public DateTime CreatedUtc;
        }
        #endregion

        #region Backing State

        /// <summary>
        /// Synchronizes access to in-memory collections and dirty/load flags.
        /// </summary>
        private static readonly object _sync = new object();

        /// <summary>
        /// Primary SteamID-backed index.
        /// </summary>
        private static readonly Dictionary<ulong, Entry> _entries =
            new Dictionary<ulong, Entry>();

        /// <summary>
        /// Secondary Gamertag-backed index.
        ///
        /// Notes:
        /// - Case-insensitive so user-facing Gamertag comparisons are more forgiving.
        /// </summary>
        private static readonly Dictionary<string, Entry> _entriesByGamertag =
            new Dictionary<string, Entry>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Tracks whether the store has already been loaded this process lifetime.
        /// </summary>
        private static bool _loadedOnce;

        /// <summary>
        /// Tracks whether the in-memory store has unsaved changes.
        /// </summary>
        private static bool _dirty;

        #endregion

        #region Paths

        /// <summary>
        /// Base directory used by this store.
        /// </summary>
        internal static string DirPath =>
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "!Mods", "CastleWallsMk2");

        /// <summary>
        /// Full path to the persistent bans file.
        /// </summary>
        internal static string FilePath =>
            Path.Combine(DirPath, "CastleWallsMk2.Bans.ini");

        #endregion

        #region Normalization Helpers

        /// <summary>
        /// Normalizes a Gamertag for consistent dictionary lookups.
        ///
        /// Summary:
        /// - Converts null to empty string.
        /// - Trims whitespace.
        /// </summary>
        private static string NormalizeGamertag(string gamertag)
        {
            return (gamertag ?? "").Trim();
        }
        #endregion

        #region Load / Reload

        /// <summary>
        /// Loads the ban store once.
        ///
        /// Summary:
        /// - Clears both in-memory indexes.
        /// - Reads the INI-style file if present.
        /// - Rebuilds SteamID and Gamertag indexes from file contents.
        ///
        /// Notes:
        /// - Safe to call repeatedly; only the first call performs work unless Reload() is used.
        /// - Failures are intentionally swallowed so runtime enforcement is not interrupted.
        /// - Both section boundaries and end-of-file finalize the current Entry.
        /// </summary>
        internal static void LoadOnce()
        {
            if (_loadedOnce)
                return;

            _loadedOnce = true;

            lock (_sync)
            {
                _entries.Clear();
                _entriesByGamertag.Clear();

                try
                {
                    if (!File.Exists(FilePath))
                        return;

                    string[] lines = File.ReadAllLines(FilePath);
                    Entry cur = null;

                    foreach (string raw in lines)
                    {
                        string line = (raw ?? "").Trim();
                        if (string.IsNullOrWhiteSpace(line) || line.StartsWith(";"))
                            continue;

                        if (line.StartsWith("[") && line.EndsWith("]"))
                        {
                            if (cur != null &&
                                (cur.SteamId != 0UL || !string.IsNullOrWhiteSpace(cur.MatchGamertag)))
                            {
                                if (cur.SteamId != 0UL)
                                    _entries[cur.SteamId] = cur;

                                if (!string.IsNullOrWhiteSpace(cur.MatchGamertag))
                                    _entriesByGamertag[NormalizeGamertag(cur.MatchGamertag)] = cur;
                            }

                            cur = new Entry();
                            continue;
                        }

                        int eq = line.IndexOf('=');
                        if (eq <= 0 || cur == null)
                            continue;

                        string key = line.Substring(0, eq).Trim();
                        string val = line.Substring(eq + 1).Trim();

                        switch (key)
                        {
                            case "SteamId":
                                ulong.TryParse(val, out cur.SteamId);
                                break;

                            case "Name":
                                cur.Name = val;
                                break;

                            case "MatchGamertag":
                                cur.MatchGamertag = val;
                                break;

                            case "Mode":
                                cur.Mode = val;
                                break;

                            case "Reason":
                                cur.Reason = val;
                                break;

                            case "CreatedUtc":
                                DateTime.TryParse(
                                    val,
                                    CultureInfo.InvariantCulture,
                                    DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal,
                                    out cur.CreatedUtc);
                                break;
                        }
                    }

                    if (cur != null &&
                        (cur.SteamId != 0UL || !string.IsNullOrWhiteSpace(cur.MatchGamertag)))
                    {
                        if (cur.SteamId != 0UL)
                            _entries[cur.SteamId] = cur;

                        if (!string.IsNullOrWhiteSpace(cur.MatchGamertag))
                            _entriesByGamertag[NormalizeGamertag(cur.MatchGamertag)] = cur;
                    }
                }
                catch
                {
                }
            }
        }

        /// <summary>
        /// Forces the store to reload from disk.
        ///
        /// Summary:
        /// - Clears the one-time load guard.
        /// - Immediately reloads current data from file.
        /// </summary>
        internal static void Reload()
        {
            _loadedOnce = false;
            LoadOnce();
        }
        #endregion

        #region Lookup Helpers

        /// <summary>
        /// Returns true when the given SteamID exists in the SteamID index.
        /// </summary>
        internal static bool IsBanned(ulong steamId)
        {
            if (steamId == 0UL)
                return false;

            LoadOnce();

            lock (_sync)
                return _entries.ContainsKey(steamId);
        }

        /// <summary>
        /// Tries to find an entry by SteamID.
        ///
        /// Notes:
        /// - Returns false immediately for SteamID 0.
        /// </summary>
        internal static bool TryGetEntry(ulong steamId, out Entry entry)
        {
            entry = null;

            if (steamId == 0UL)
                return false;

            LoadOnce();

            lock (_sync)
                return _entries.TryGetValue(steamId, out entry);
        }

        /// <summary>
        /// Tries to find an entry for off-host Gamertag enforcement.
        ///
        /// Summary:
        /// - First checks the explicit Gamertag index.
        /// - Then falls back to older SteamID-backed entries that may only have Name.
        ///
        /// Notes:
        /// - Performs a small self-heal when a fallback match is found by filling MatchGamertag
        ///   and caching the entry in the Gamertag index for faster future lookups.
        /// </summary>
        internal static bool TryGetEntryForOffHostGamertag(string gamertag, out Entry entry)
        {
            entry = null;

            string key = NormalizeGamertag(gamertag);
            if (string.IsNullOrWhiteSpace(key))
                return false;

            LoadOnce();

            lock (_sync)
            {
                // Fast path: Explicit gamertag-backed entry.
                if (_entriesByGamertag.TryGetValue(key, out entry))
                    return true;

                // Fallback: Older SteamID-backed entries may only have Name.
                foreach (Entry cur in _entries.Values)
                {
                    string match = NormalizeGamertag(
                        !string.IsNullOrWhiteSpace(cur.MatchGamertag)
                            ? cur.MatchGamertag
                            : cur.Name);

                    if (!string.IsNullOrWhiteSpace(match) &&
                        string.Equals(match, key, StringComparison.OrdinalIgnoreCase))
                    {
                        entry = cur;

                        // Self-heal so future lookups are fast and the file can be updated later.
                        cur.MatchGamertag = key;
                        _entriesByGamertag[key] = cur;
                        _dirty = true;

                        return true;
                    }
                }

                return false;
            }
        }

        /// <summary>
        /// Tries to find an entry for the given live gamer.
        ///
        /// Summary:
        /// - Uses SteamID when available.
        /// - Falls back to off-host Gamertag matching when needed.
        ///
        /// Notes:
        /// - Useful for enforcement paths that may operate in host or off-host contexts.
        /// </summary>
        internal static bool TryGetEntryForCurrentGamer(NetworkGamer gamer, out Entry entry)
        {
            entry = null;

            if (gamer == null)
                return false;

            ulong steamId = gamer.AlternateAddress;
            if (steamId != 0UL && TryGetEntry(steamId, out entry))
                return true;

            return TryGetEntryForOffHostGamertag(gamer.Gamertag, out entry);
        }

        /// <summary>
        /// Tries to find an entry by normalized Gamertag.
        /// </summary>
        internal static bool TryGetEntryByGamertag(string gamertag, out Entry entry)
        {
            entry = null;

            string key = NormalizeGamertag(gamertag);
            if (string.IsNullOrWhiteSpace(key))
                return false;

            LoadOnce();

            lock (_sync)
                return _entriesByGamertag.TryGetValue(key, out entry);
        }

        /// <summary>
        /// Returns a merged, de-duplicated snapshot of all entries.
        ///
        /// Summary:
        /// - Combines SteamID and Gamertag indexes.
        /// - De-duplicates by SteamID when available, otherwise by Gamertag.
        /// - Sorts newest-first, then by name.
        ///
        /// Notes:
        /// - Intended for UI display and save serialization.
        /// </summary>
        internal static List<Entry> GetSnapshot()
        {
            LoadOnce();

            lock (_sync)
            {
                return _entries.Values
                    .Concat(_entriesByGamertag.Values)
                    .GroupBy(x => x.SteamId != 0UL ? $"sid:{x.SteamId}" : $"gt:{x.MatchGamertag}")
                    .Select(g => g.First())
                    .OrderByDescending(x => x.CreatedUtc)
                    .ThenBy(x => x.Name ?? "")
                    .ToList();
            }
        }
        #endregion

        #region Add / Update

        /// <summary>
        /// Adds or updates a SteamID-backed entry.
        ///
        /// Summary:
        /// - Creates/updates the SteamID index.
        /// - Also mirrors the entry into the Gamertag index when a valid name is present.
        /// - Marks the store dirty.
        ///
        /// Notes:
        /// - SteamID 0 is ignored.
        /// </summary>
        internal static void AddOrUpdate(ulong steamId, string name, string mode, string reason = null)
        {
            if (steamId == 0UL)
                return;

            LoadOnce();

            lock (_sync)
            {
                string key = NormalizeGamertag(name);

                var entry = new Entry
                {
                    SteamId = steamId,
                    Name = name ?? "",
                    MatchGamertag = key,
                    Mode = mode ?? "",
                    Reason = reason ?? "",
                    CreatedUtc = DateTime.UtcNow
                };

                _entries[steamId] = entry;

                if (!string.IsNullOrWhiteSpace(key))
                    _entriesByGamertag[key] = entry;

                _dirty = true;
            }
        }

        /// <summary>
        /// Adds or updates a Gamertag-backed entry.
        ///
        /// Summary:
        /// - Creates/updates the Gamertag index.
        /// - Stores SteamId as 0 because the real Steam identity is unknown.
        /// - Marks the store dirty.
        /// </summary>
        internal static void AddOrUpdateGamertag(string gamertag, string displayName, string mode, string reason = null)
        {
            string key = NormalizeGamertag(gamertag);
            if (string.IsNullOrWhiteSpace(key))
                return;

            LoadOnce();

            lock (_sync)
            {
                var entry = new Entry
                {
                    SteamId = 0UL,
                    Name = displayName ?? gamertag ?? "",
                    MatchGamertag = key,
                    Mode = mode ?? "",
                    Reason = reason ?? "",
                    CreatedUtc = DateTime.UtcNow
                };

                _entriesByGamertag[key] = entry;
                _dirty = true;
            }
        }
        #endregion

        #region Remove

        /// <summary>
        /// Removes a SteamID-backed entry.
        ///
        /// Notes:
        /// - SteamID 0 is ignored.
        /// - Does not explicitly remove any mirrored Gamertag-backed entry object.
        /// </summary>
        internal static bool Remove(ulong steamId)
        {
            if (steamId == 0UL)
                return false;

            LoadOnce();

            lock (_sync)
            {
                bool removed = _entries.Remove(steamId);
                if (removed)
                    _dirty = true;

                return removed;
            }
        }

        /// <summary>
        /// Removes a Gamertag-backed entry.
        ///
        /// Notes:
        /// - Blank/null Gamertags are ignored.
        /// </summary>
        internal static bool RemoveByGamertag(string gamertag)
        {
            string key = NormalizeGamertag(gamertag);
            if (string.IsNullOrWhiteSpace(key))
                return false;

            LoadOnce();

            lock (_sync)
            {
                bool removed = _entriesByGamertag.Remove(key);
                if (removed)
                    _dirty = true;

                return removed;
            }
        }
        #endregion

        #region Save

        /// <summary>
        /// Saves the store to disk only when changes are pending.
        ///
        /// Summary:
        /// - Builds a merged/de-duplicated snapshot.
        /// - Writes the snapshot as INI-style sections.
        /// - Resets the dirty flag on success.
        ///
        /// Notes:
        /// - If save fails, the store is marked dirty again so a later retry can persist changes.
        /// - Save format intentionally includes both SteamId and MatchGamertag to support mixed enforcement paths.
        /// </summary>
        internal static void SaveIfDirty()
        {
            LoadOnce();

            List<Entry> snapshot;
            lock (_sync)
            {
                if (!_dirty)
                    return;

                snapshot = _entries.Values
                    .Concat(_entriesByGamertag.Values)
                    .GroupBy(x => x.SteamId != 0UL ? $"sid:{x.SteamId}" : $"gt:{x.MatchGamertag}")
                    .Select(g => g.First())
                    .OrderByDescending(x => x.CreatedUtc)
                    .ThenBy(x => x.Name ?? "")
                    .ToList();

                _dirty = false;
            }

            try
            {
                Directory.CreateDirectory(DirPath);

                using (var sw = new StreamWriter(FilePath, false, Encoding.UTF8))
                {
                    sw.WriteLine("; CastleWallsMk2 persistent bans");
                    sw.WriteLine("; Auto-generated");
                    sw.WriteLine();

                    for (int i = 0; i < snapshot.Count; i++)
                    {
                        var e = snapshot[i];
                        sw.WriteLine($"[Ban{i}]");
                        sw.WriteLine($"SteamId={e.SteamId}");
                        sw.WriteLine($"Name={e.Name ?? ""}");
                        sw.WriteLine($"MatchGamertag={e.MatchGamertag ?? ""}");
                        sw.WriteLine($"Mode={e.Mode ?? ""}");
                        sw.WriteLine($"Reason={e.Reason ?? ""}");
                        sw.WriteLine($"CreatedUtc={e.CreatedUtc.ToUniversalTime():o}");
                        sw.WriteLine();
                    }
                }
            }
            catch
            {
                lock (_sync)
                    _dirty = true;
            }
        }
        #endregion
    }
}