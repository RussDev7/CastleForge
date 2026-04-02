/*
SPDX-License-Identifier: GPL-3.0-or-later
Copyright (c) 2025 RussDev7
This file is part of https://github.com/RussDev7/CastleForge - see LICENSE for details.
*/

using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.IO;
using System;

using static ModLoader.LogSystem;

namespace CastleWallsMk2
{
    /// <summary>
    /// ======================================================================================
    /// ServerHistory
    /// --------------------------------------------------------------------------------------
    /// SUMMARY:
    ///   Tracks a local history of server hosts you have connected to.
    ///   Uses AlternateAddress/SteamID as the stable identity (PlayerID removed).
    ///
    ///   Each entry captures:
    ///     - Name (Gamertag).
    ///     - AltAddress (stable key).
    ///     - LastKnownPassword (optional; updated only after successful join when a password was used).
    ///     - Ip (DATA ONLY; optional, filled by sniffer heuristic; NEVER used for joining).
    ///     - FirstSeen/LastSeen + TimesConnected counters.
    ///
    /// FILE:
    ///   !Mods\CastleWallsMk2\CastleWallsMk2.ServerHistory.ini
    ///
    /// FORMAT (v3):
    ///   server=alt=<...>;name=<...>;pass=<...>;ip=<...>;first=<iso>;last=<iso>;count=<n>
    ///
    /// COMPAT:
    ///   Reads v1 lines too (uuid field is ignored; alt/name/ip are imported if present).
    ///
    /// NOTES:
    ///   - Thread-safety: All mutation is guarded by _gate; callers can be from multiple threads.
    ///   - Persistence:   SaveIfDirty() writes atomically via .tmp then move.
    ///   - Identity:      Prefers "alt:<SteamID>" key; falls back to "name:<Gamertag>" if alt is not known yet.
    ///   - Migration:     LateFillCurrentHostIdentity can rename keys from name-based to alt-based once known.
    /// ======================================================================================
    /// </summary>
    internal static class ServerHistory
    {
        #region Data Model

        /// <summary>
        /// A single server-host history entry.
        /// NOTE: Key is the identity string used in-memory and for selecting entries in UI.
        /// </summary>
        internal sealed class Entry
        {
            public string   Key;               // Stable key: "alt:<AltAddress>" (or name fallback).
            public string   Name;              // Host gamer tag / display name.
            public ulong    AltAddress;        // Primary identity (stable).
            public string   LastKnownPassword; // Optional; updated after successful password join.
            public string   Ip;                // Optional public IP (sniffer/heuristic). (DATA ONLY)
            public DateTime FirstSeenUtc;
            public DateTime LastSeenUtc;
            public int      TimesConnected;
        }
        #endregion

        #region Storage State (In-Memory)

        /// <summary>Global lock for all state below.</summary>
        private static readonly object      _gate    = new object();

        /// <summary>In-memory history list (sorted on reload; snapshots re-sort by LastSeen).</summary>
        private static readonly List<Entry> _entries = new List<Entry>();

        /// <summary>Load guard; ensures Reload() only occurs once unless explicitly called.</summary>
        private static bool                 _loaded;

        /// <summary>Dirty guard; when true, SaveIfDirty() will write to disk.</summary>
        private static bool                 _dirty;

        /// <summary>
        /// Current session host identity so sniffer can attach IP + join flow can attach password.
        /// - Set on NoteJoinSuccess (after successful join).
        /// - Can be established/normalized/migrated in LateFillCurrentHostIdentity.
        /// </summary>
        private static string _currentHostKey;

        #endregion

        #region Paths

        /// <summary>!Mods\CastleWallsMk2</summary>
        public static string DirectoryPath
            => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "!Mods", typeof(CastleWallsMk2).Namespace);

        /// <summary>!Mods\CastleWallsMk2\CastleWallsMk2.ServerHistory.ini</summary>
        public static string FilePath
            => Path.Combine(DirectoryPath, "CastleWallsMk2.ServerHistory.ini");

        /// <summary>
        /// Ensures Load() has been called at least once.
        /// NOTE: Load() is idempotent and calls Reload().
        /// </summary>
        private static void EnsureLoaded()
        {
            if (_loaded) return;
            Load();
        }
        #endregion

        #region Public API - Load / Reload / Save

        /// <summary>
        /// One-time initialization entry point.
        /// - Marks as loaded and performs a Reload() from disk.
        /// </summary>
        public static void Load()
        {
            lock (_gate)
            {
                if (_loaded) return;
                _loaded = true;
            }

            Reload();
        }

        /// <summary>
        /// Hard reload from disk.
        /// - Clears current entries and re-parses the INI file.
        /// - Sorts entries by LastSeenUtc (descending).
        /// - Resets _currentHostKey (session identity is re-established on next join).
        /// </summary>
        public static void Reload()
        {
            lock (_gate)
            {
                _loaded         = true;
                _dirty          = false;
                _currentHostKey = null;
                _entries.Clear();

                try
                {
                    Directory.CreateDirectory(DirectoryPath);

                    if (!File.Exists(FilePath))
                        return;

                    foreach (var raw in File.ReadAllLines(FilePath, Encoding.UTF8))
                    {
                        string line = (raw ?? "").Trim();
                        if (line.Length == 0) continue;
                        if (line.StartsWith("#")) continue;

                        if (!line.StartsWith("server=", StringComparison.OrdinalIgnoreCase))
                            continue;

                        var e = ParseLine(line.Substring("server=".Length));
                        if (e != null)
                            Upsert_Unsafe(e, bumpCount: false);
                    }

                    _entries.Sort((a, b) => b.LastSeenUtc.CompareTo(a.LastSeenUtc));
                }
                catch (Exception ex)
                {
                    Log($"[ServerHistory] Reload failed: {ex.Message}.");
                }
                finally
                {
                    _dirty = false;
                }
            }
        }

        /// <summary>
        /// Writes to disk if _dirty is true (atomic replace).
        /// - Writes header comments + all entries as "server=" lines.
        /// - Uses a temp file then replaces the old file.
        /// </summary>
        public static void SaveIfDirty()
        {
            EnsureLoaded();

            lock (_gate)
            {
                if (!_dirty) return;

                try
                {
                    Directory.CreateDirectory(DirectoryPath);

                    string tmp = FilePath + ".tmp";
                    using (var sw = new StreamWriter(tmp, false, Encoding.UTF8))
                    {
                        sw.WriteLine("# CastleWallsMk2 Server History v3");
                        sw.WriteLine("# server=alt=...;name=...;pass=...;ip=...;first=...;last=...;count=...");
                        sw.WriteLine();

                        foreach (var e in _entries.OrderByDescending(x => x.LastSeenUtc))
                            sw.WriteLine("server=" + ToLine(e));
                    }

                    if (File.Exists(FilePath))
                        File.Delete(FilePath);

                    File.Move(tmp, FilePath);

                    _dirty = false;
                }
                catch (Exception ex)
                {
                    Log($"[ServerHistory] Save failed: {ex.Message}.");
                }
            }
        }
        #endregion

        #region Public API - Query / CRUD

        /// <summary>
        /// Returns a deep-copy snapshot, sorted by LastSeenUtc desc.
        /// NOTE: callers can safely enumerate and modify returned list without locking.
        /// </summary>
        public static List<Entry> GetSnapshot()
        {
            EnsureLoaded();

            lock (_gate)
            {
                return _entries
                    .OrderByDescending(x => x.LastSeenUtc)
                    .Select(x => new Entry
                    {
                        Key               = x.Key,
                        Name              = x.Name,
                        AltAddress        = x.AltAddress,
                        LastKnownPassword = x.LastKnownPassword,
                        Ip                = x.Ip,
                        FirstSeenUtc      = x.FirstSeenUtc,
                        LastSeenUtc       = x.LastSeenUtc,
                        TimesConnected    = x.TimesConnected
                    })
                    .ToList();
            }
        }

        /// <summary>
        /// Deletes a single entry by key (case-insensitive).
        /// - Clears _currentHostKey if it pointed at this entry.
        /// </summary>
        public static bool Delete(string key)
        {
            if (string.IsNullOrEmpty(key)) return false;

            EnsureLoaded();

            lock (_gate)
            {
                int idx = _entries.FindIndex(e => string.Equals(e.Key, key, StringComparison.OrdinalIgnoreCase));
                if (idx < 0) return false;

                _entries.RemoveAt(idx);
                _dirty = true;

                if (string.Equals(_currentHostKey, key, StringComparison.OrdinalIgnoreCase))
                    _currentHostKey = null;

                return true;
            }
        }

        /// <summary>
        /// Clears the entire history list.
        /// NOTE: caller should call SaveIfDirty() afterward if immediate persistence is desired.
        /// </summary>
        public static void Clear()
        {
            EnsureLoaded();

            lock (_gate)
            {
                _entries.Clear();
                _currentHostKey = null;
                _dirty = true;
            }
        }
        #endregion

        #region Public API - Session Tracking (Join / IP / Identity)

        /// <summary>
        /// Called ONLY after a successful join.
        /// - Records host by (name, alt)
        /// - Bumps TimesConnected
        /// - Sets current-host marker for sniffer IP attachment
        /// - Updates password ONLY if non-empty (does not wipe existing)
        /// </summary>
        public static void NoteJoinSuccess(string name, ulong altAddress, string passwordUsed)
        {
            EnsureLoaded();

            string pw = string.IsNullOrWhiteSpace(passwordUsed) ? "" : passwordUsed.Trim();

            lock (_gate)
            {
                var now = DateTime.UtcNow;
                var key = MakeKey(altAddress, name);

                var e = new Entry
                {
                    Key               = key,
                    Name              = name ?? "",
                    AltAddress        = altAddress,
                    LastKnownPassword = pw,      // empty => won't overwrite in Upsert merge logic
                    Ip                = "",      // DATA ONLY; sniffer can attach later
                    FirstSeenUtc      = now,
                    LastSeenUtc       = now,
                    TimesConnected    = 1
                };

                Upsert_Unsafe(e, bumpCount: true);
                _currentHostKey = key;
            }

            SaveIfDirty();
        }

        /// <summary>
        /// Updates an entry's IP (DATA ONLY) if it differs.
        /// NOTE: Saves immediately if a change occurred.
        /// </summary>
        public static bool TryUpdateIp(string key, string ip)
        {
            if (string.IsNullOrEmpty(key)) return false;
            if (string.IsNullOrEmpty(ip)) return false;

            EnsureLoaded();

            lock (_gate)
            {
                var e = _entries.FirstOrDefault(x => string.Equals(x.Key, key, StringComparison.OrdinalIgnoreCase));
                if (e == null) return false;

                if (!string.Equals(e.Ip ?? "", ip, StringComparison.OrdinalIgnoreCase))
                {
                    e.Ip = ip;
                    _dirty = true;
                }
            }

            SaveIfDirty();
            return true;
        }

        /// <summary>
        /// Convenience: updates the current host's IP (if we have a current host key).
        /// </summary>
        public static bool TryUpdateCurrentHostIp(string ip)
        {
            if (string.IsNullOrEmpty(ip)) return false;

            EnsureLoaded();

            string key;
            lock (_gate) { key = _currentHostKey; }

            if (string.IsNullOrEmpty(key)) return false;
            return TryUpdateIp(key, ip);
        }

        /// <summary>
        /// Clears the current session host marker.
        /// </summary>
        public static void ClearCurrentHost()
        {
            lock (_gate) { _currentHostKey = null; }
        }

        /// <summary>
        /// Late identity fill:
        /// - Useful when the host is initially "[unknown]" or alt (SteamID) is learned after join.
        ///
        /// Behavior:
        ///  1) Establish current host key if missing.
        ///  2) Replace placeholder name if hostName is now known.
        ///  3) Normalize a lingering "name:[unknown]" key to "name:<realname>" if still alt=0.
        ///  4) If alt becomes known, migrate/merge to "alt:<id>" and point _currentHostKey at it.
        ///
        /// NOTES:
        ///  - Uses "changed" to avoid redundant disk writes.
        ///  - Merges entries without inflating TimesConnected.
        /// </summary>
        public static void LateFillCurrentHostIdentity(string hostName, ulong hostAlt)
        {
            EnsureLoaded();

            hostName = string.IsNullOrWhiteSpace(hostName) ? "" : hostName.Trim();

            bool changed = false;

            lock (_gate)
            {
                // If we don't have a current host key yet, establish it now.
                if (string.IsNullOrEmpty(_currentHostKey))
                {
                    string key = MakeKey(hostAlt, hostName);
                    if (key == "name:" && hostAlt == 0 && hostName.Length == 0)
                        return;

                    Upsert_Unsafe(new Entry
                    {
                        Key               = key,
                        Name              = hostName,
                        AltAddress        = hostAlt,
                        LastKnownPassword = "",
                        Ip                = "",
                        FirstSeenUtc      = DateTime.UtcNow,
                        LastSeenUtc       = DateTime.UtcNow,
                        TimesConnected    = 1
                    }, bumpCount: false);

                    _currentHostKey = key;
                    _dirty = true;
                    changed = true;
                }

                // Always re-resolve by current key (because we may change keys below).
                var cur = _entries.FirstOrDefault(e =>
                    string.Equals(e.Key, _currentHostKey, StringComparison.OrdinalIgnoreCase));

                if (cur == null)
                    return;

                // Helper: merge 'src' into 'dst' without inflating counts.
                void MergeInto(Entry dst, Entry src)
                {
                    if (string.IsNullOrWhiteSpace(dst.Name) || dst.Name == "[unknown]")
                        dst.Name = src.Name;

                    if (!string.IsNullOrWhiteSpace(src.LastKnownPassword))
                        dst.LastKnownPassword = src.LastKnownPassword;

                    if (!string.IsNullOrWhiteSpace(src.Ip))
                        dst.Ip = src.Ip;

                    if (dst.FirstSeenUtc == DateTime.MinValue)
                        dst.FirstSeenUtc = src.FirstSeenUtc;

                    if (src.LastSeenUtc > dst.LastSeenUtc)
                        dst.LastSeenUtc = src.LastSeenUtc;

                    dst.TimesConnected = Math.Max(dst.TimesConnected, src.TimesConnected);
                }

                // 1) Fix placeholder name.
                bool curNameBad =
                    string.IsNullOrWhiteSpace(cur.Name) ||
                    string.Equals(cur.Name, "[unknown]", StringComparison.OrdinalIgnoreCase);

                if (curNameBad && hostName.Length > 0 && !string.Equals(cur.Name, hostName, StringComparison.Ordinal))
                {
                    cur.Name = hostName;
                    _dirty = true;
                    changed = true;
                }

                // 2) If we're still name-keyed (alt unknown), normalize the "name:" key to match cur.Name
                //    (this fixes the "name:[unknown]" key lingering after Name becomes real)
                if (cur.AltAddress == 0 && !string.IsNullOrWhiteSpace(cur.Name))
                {
                    string desiredNameKey = MakeKey(0, cur.Name); // "name:Bob".
                    if (desiredNameKey != "name:" &&
                        !string.Equals(cur.Key, desiredNameKey, StringComparison.OrdinalIgnoreCase))
                    {
                        string oldKey = cur.Key;

                        var other = _entries.FirstOrDefault(e =>
                            string.Equals(e.Key, desiredNameKey, StringComparison.OrdinalIgnoreCase));

                        if (other == null)
                        {
                            cur.Key = desiredNameKey;
                        }
                        else
                        {
                            MergeInto(other, cur);
                            _entries.Remove(cur);
                            cur = other; // IMPORTANT: Continue using the survivor.
                        }

                        if (string.Equals(_currentHostKey, oldKey, StringComparison.OrdinalIgnoreCase))
                            _currentHostKey = desiredNameKey;

                        _dirty = true;
                        changed = true;
                    }
                }

                // 3) If we learned alt, migrate/merge to alt:<id>
                if (hostAlt != 0)
                {
                    string newAltKey = MakeKey(hostAlt, cur.Name); // "alt:<id>".

                    if (!string.Equals(cur.Key, newAltKey, StringComparison.OrdinalIgnoreCase))
                    {
                        string oldKey = cur.Key;

                        var target = _entries.FirstOrDefault(e =>
                            string.Equals(e.Key, newAltKey, StringComparison.OrdinalIgnoreCase));

                        if (target == null)
                        {
                            cur.AltAddress = hostAlt;
                            cur.Key = newAltKey;
                        }
                        else
                        {
                            MergeInto(target, cur);
                            _entries.Remove(cur);
                            cur = target;
                        }

                        _currentHostKey = newAltKey; // Point at the stable identity.
                        _dirty = true;
                        changed = true;
                    }
                    else if (cur.AltAddress != hostAlt)
                    {
                        cur.AltAddress = hostAlt;
                        _dirty = true;
                        changed = true;
                    }
                }
            }

            if (changed)
                SaveIfDirty();
        }
        #endregion

        #region Internals - Upsert / Keys

        /// <summary>
        /// Inserts or merges an incoming entry into _entries.
        /// NOTES:
        ///  - Must be called under _gate lock.
        ///  - bumpCount: when true, increments TimesConnected on merge.
        ///  - Password is only overwritten if incoming password is non-empty.
        /// </summary>
        private static void Upsert_Unsafe(Entry incoming, bool bumpCount)
        {
            var existing = _entries.FirstOrDefault(e => string.Equals(e.Key, incoming.Key, StringComparison.OrdinalIgnoreCase));
            if (existing == null)
            {
                _entries.Add(incoming);
                _dirty = true;
                return;
            }

            if (!string.IsNullOrEmpty(incoming.Name) && !string.Equals(existing.Name, incoming.Name, StringComparison.Ordinal))
                existing.Name = incoming.Name;

            if (incoming.AltAddress != 0 && existing.AltAddress != incoming.AltAddress)
                existing.AltAddress = incoming.AltAddress;

            // Only overwrite password if a non-empty password was provided.
            if (!string.IsNullOrEmpty(incoming.LastKnownPassword) &&
                !string.Equals(existing.LastKnownPassword ?? "", incoming.LastKnownPassword, StringComparison.Ordinal))
            {
                existing.LastKnownPassword = incoming.LastKnownPassword;
            }

            // DATA ONLY: Merge IP if present.
            if (!string.IsNullOrEmpty(incoming.Ip) &&
                !string.Equals(existing.Ip ?? "", incoming.Ip, StringComparison.OrdinalIgnoreCase))
            {
                existing.Ip = incoming.Ip;
            }

            if (incoming.FirstSeenUtc != DateTime.MinValue && existing.FirstSeenUtc == DateTime.MinValue)
                existing.FirstSeenUtc = incoming.FirstSeenUtc;

            if (incoming.LastSeenUtc > existing.LastSeenUtc)
                existing.LastSeenUtc = incoming.LastSeenUtc;
            else
                existing.LastSeenUtc = DateTime.UtcNow;

            if (bumpCount)
                existing.TimesConnected = Math.Max(existing.TimesConnected + 1, 1);

            _dirty = true;
        }

        /// <summary>
        /// Builds the stable key string for an entry.
        /// - Prefers alt-based identity if present: "alt:<id>"
        /// - Otherwise falls back to name-based: "name:<trimmed>"
        /// </summary>
        private static string MakeKey(ulong altAddress, string name)
        {
            if (altAddress != 0)
                return "alt:" + altAddress.ToString(CultureInfo.InvariantCulture);

            return "name:" + (name ?? "").Trim();
        }
        #endregion

        #region Internals - Parse / Serialize

        /// <summary>
        /// Parses a single "server=" payload into an Entry.
        /// Supports:
        ///  - v3: alt/name/pass/ip/first/last/count
        ///  - v1-ish: reads any compatible fields present; ignores unknown/uuid
        /// </summary>
        private static Entry ParseLine(string payload)
        {
            var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            foreach (var part in SplitEscaped(payload, ';'))
            {
                if (string.IsNullOrEmpty(part)) continue;

                int eq = IndexOfUnescaped(part, '=');
                if (eq <= 0) continue;

                string k = part.Substring(0, eq).Trim();
                string v = part.Substring(eq + 1).Trim();
                dict[k] = Unescape(v);
            }

            string name = Get(dict, "name");

            string pass = Get(dict, "pass");
            if (string.IsNullOrEmpty(pass))
                pass = Get(dict, "last-known-password");

            string ip = Get(dict, "ip");

            ulong.TryParse(Get(dict, "alt"), NumberStyles.Integer, CultureInfo.InvariantCulture, out ulong alt);

            ParseIso(Get(dict, "first"), out DateTime first);
            ParseIso(Get(dict, "last"),  out DateTime last);

            int.TryParse(Get(dict, "count"), NumberStyles.Integer, CultureInfo.InvariantCulture, out int count);

            var key = MakeKey(alt, name);

            return new Entry
            {
                Key               = key,
                Name              = name ?? "",
                AltAddress        = alt,
                LastKnownPassword = pass ?? "",
                Ip                = ip ?? "",
                FirstSeenUtc      = first == DateTime.MinValue ? DateTime.UtcNow : first,
                LastSeenUtc       = last  == DateTime.MinValue ? DateTime.UtcNow : last,
                TimesConnected    = Math.Max(count, 0)
            };
        }

        /// <summary>
        /// Serializes an Entry into the "alt=...;name=...;pass=...;ip=...;first=...;last=...;count=..." payload form.
        /// </summary>
        private static string ToLine(Entry e)
        {
            return "alt="   + e.AltAddress.ToString(CultureInfo.InvariantCulture) +
                   ";name="  + Escape(e.Name) +
                   ";pass="  + Escape(e.LastKnownPassword) +
                   ";ip="    + Escape(e.Ip) +
                   ";first=" + Escape(ToIso(e.FirstSeenUtc)) +
                   ";last="  + Escape(ToIso(e.LastSeenUtc)) +
                   ";count=" + e.TimesConnected.ToString(CultureInfo.InvariantCulture);
        }

        /// <summary>Dictionary helper: returns "" if missing.</summary>
        private static string Get(Dictionary<string, string> d, string k)
        {
            if (d.TryGetValue(k, out string v)) return v;
            return "";
        }

        /// <summary>Parses ISO-8601 "o" timestamps.</summary>
        private static bool ParseIso(string s, out DateTime dt)
        {
            dt = DateTime.MinValue;
            if (string.IsNullOrEmpty(s)) return false;

            return DateTime.TryParseExact(s, "o", CultureInfo.InvariantCulture,
                DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out dt);
        }

        /// <summary>Formats UTC timestamps as ISO-8601 "o".</summary>
        private static string ToIso(DateTime utc)
        {
            if (utc == DateTime.MinValue) utc = DateTime.UtcNow;
            if (utc.Kind != DateTimeKind.Utc) utc = utc.ToUniversalTime();
            return utc.ToString("o", CultureInfo.InvariantCulture);
        }
        #endregion

        #region Internals - Escaping / Splitting

        /// <summary>
        /// Escapes fields for the INI payload:
        /// - \  => \\
        /// - ;  => \;
        /// - =  => \=
        /// - \n => literal "\n"
        /// </summary>
        private static string Escape(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s.Replace("\\", "\\\\")
                    .Replace(";", "\\;")
                    .Replace("=", "\\=")
                    .Replace("\r", "")
                    .Replace("\n", "\\n");
        }

        /// <summary>
        /// Unescapes fields for the INI payload:
        /// - "\n" => newline
        /// - "\"  => next char literal
        /// </summary>
        private static string Unescape(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";

            var sb = new StringBuilder(s.Length);
            bool esc = false;
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (esc)
                {
                    if (c == 'n') sb.Append('\n');
                    else sb.Append(c);
                    esc = false;
                }
                else
                {
                    if (c == '\\') esc = true;
                    else sb.Append(c);
                }
            }
            if (esc) sb.Append('\\');
            return sb.ToString();
        }

        /// <summary>Finds the first unescaped instance of a character.</summary>
        private static int IndexOfUnescaped(string s, char ch)
        {
            bool esc = false;
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (esc) { esc = false; continue; }
                if (c == '\\') { esc = true; continue; }
                if (c == ch) return i;
            }
            return -1;
        }

        /// <summary>
        /// Splits a string by a separator char, ignoring separators that are escaped.
        /// NOTE: preserves escape markers so Unescape() can process them later.
        /// </summary>
        private static IEnumerable<string> SplitEscaped(string s, char sep)
        {
            if (string.IsNullOrEmpty(s))
                yield break;

            var sb = new StringBuilder();
            bool esc = false;

            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (esc)
                {
                    sb.Append('\\');
                    sb.Append(c);
                    esc = false;
                    continue;
                }

                if (c == '\\')
                {
                    esc = true;
                    continue;
                }

                if (c == sep)
                {
                    yield return sb.ToString();
                    sb.Length = 0;
                    continue;
                }

                sb.Append(c);
            }

            if (esc) sb.Append('\\');
            yield return sb.ToString();
        }
        #endregion
    }
}