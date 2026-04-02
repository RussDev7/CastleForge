/*
SPDX-License-Identifier: GPL-3.0-or-later
Copyright (c) 2025 RussDev7
This file is part of https://github.com/RussDev7/CastleForge - see LICENSE for details.
*/

using System.Collections.Generic;
using Microsoft.Xna.Framework;    // Vector3.
using System.Globalization;
using DNA.CastleMinerZ;
using System.Linq;
using System.IO;
using System;

namespace SetHomes
{
    /// <summary>
    /// Persistence layer for per-world "homes" (named <see cref="Vector3"/> positions).
    ///
    /// DESIGN GOALS
    /// ------------
    /// • Simple, durable persistence (INI file on disk).
    /// • Per-world separation: each world has its own independent set of names.
    /// • Support a per-world default home when the player omits a name.
    /// • Case-insensitive home names.
    /// • Best-effort IO: parsing/IO failures are swallowed; the in-memory DB remains usable.
    ///
    /// STORAGE
    /// -------
    /// • Path: !Mods\{resolver-ns}\{resolver-ns}.Homes.ini
    /// • Format: one section per world - [<world-guid>]
    ///   Each line: HomeName = x,y,z (InvariantCulture)
    /// • The per-world default home is stored under a reserved internal key.
    ///
    /// THREADING
    /// ---------
    /// Not thread-safe; call from the game thread (chat command handler, UI, etc.).
    /// </summary>
    internal static class HomesStorage
    {
        #region Constants

        // Reserved internal key used to store the per-world default home.
        // Chosen to be very unlikely to collide with a user-chosen name.
        private const string DefaultInternalName = "__default__";

        // How we present the default home in chat listings.
        public const string DefaultDisplayName = "<default>";

        /// <summary>
        /// Stored home data for one saved home entry.
        /// Includes the player's world position, body rotation (quaternion),
        /// and torso pitch serialized as radians for save/load support.
        /// </summary>
        private struct HomeRecord
        {
            public Vector3    Position;
            public Quaternion Rotation;
            public float      PitchRadians;

            public HomeRecord(Vector3 position, Quaternion rotation, float pitchRadians)
            {
                Position     = position;
                Rotation     = rotation;
                PitchRadians = pitchRadians;
            }
        }
        #endregion

        #region Path / In-Memory DB

        /// <summary>
        /// Absolute path to the homes INI.
        /// Example: !Mods\SetHomes\SetHomes.Homes.ini
        /// </summary>
        public static string HomesPath =>
            Path.Combine(
                AppDomain.CurrentDomain.BaseDirectory,
                "!Mods",
                typeof(EmbeddedResolver).Namespace,
                $"{typeof(EmbeddedResolver).Namespace}.Homes.ini");

        // In-memory DB: worldId -> (homeName -> position).
        // Both layers are case-insensitive.
        private static readonly Dictionary<string, Dictionary<string, HomeRecord>> _db =
            new Dictionary<string, Dictionary<string, HomeRecord>>(StringComparer.OrdinalIgnoreCase);

        private static bool _loaded;

        #endregion

        #region World Key

        /// <summary>
        /// Returns the current world's GUID string, or "(pending-world)" if unavailable.
        /// </summary>
        public static string CurrentWorldKey()
        {
            var g = CastleMinerZGame.Instance;
            var id = g?.CurrentWorld?.WorldID.ToString();
            return string.IsNullOrWhiteSpace(id) ? "(pending-world)" : id;
        }
        #endregion

        #region Load / Save

        /// <summary>
        /// Lazy-load the INI once; creates an empty file with a header if missing.
        /// </summary>
        public static void EnsureLoaded()
        {
            if (_loaded) return;
            _loaded = true;

            try
            {
                var dir = Path.GetDirectoryName(HomesPath);
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

                if (!File.Exists(HomesPath))
                {
                    File.WriteAllLines(HomesPath, new[]
                    {
                        $"; {typeof(EmbeddedResolver).Namespace} - Homes",
                        $"; This file stores per-world homes. Do not hand-edit while the game is running.",
                        $"; [<world-guid>]",
                        $"; HomeName = x,y,z[,qx,qy,qz,qw,pitch]",
                        ""
                    });
                    return;
                }

                ParseIni(File.ReadAllLines(HomesPath));
            }
            catch
            {
                // Best-effort: if parsing fails, leave _db empty.
                // A future Save() will rewrite a clean file when possible.
            }
        }

        /// <summary>
        /// Persist the entire in-memory DB back to INI in a stable, readable order.
        /// </summary>
        public static void Save()
        {
            try
            {
                var dir = Path.GetDirectoryName(HomesPath);
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

                using (var sw = new StreamWriter(HomesPath, false))
                {
                    sw.WriteLine($"; {typeof(EmbeddedResolver).Namespace} - Homes");
                    sw.WriteLine($"; LastSaved = {DateTime.Now:G}");
                    sw.WriteLine();

                    foreach (var world in _db.Keys.OrderBy(k => k, StringComparer.OrdinalIgnoreCase))
                    {
                        sw.WriteLine($"[{world}]");

                        foreach (var kv in _db[world].OrderBy(k => k.Key, StringComparer.OrdinalIgnoreCase))
                        {
                            var h = kv.Value;
                            sw.WriteLine(
                                   $"{kv.Key} = "                                            +
                                   $"{h.Position.X.ToString(CultureInfo.InvariantCulture)}," +
                                   $"{h.Position.Y.ToString(CultureInfo.InvariantCulture)}," +
                                   $"{h.Position.Z.ToString(CultureInfo.InvariantCulture)}," +
                                   $"{h.Rotation.X.ToString(CultureInfo.InvariantCulture)}," +
                                   $"{h.Rotation.Y.ToString(CultureInfo.InvariantCulture)}," +
                                   $"{h.Rotation.Z.ToString(CultureInfo.InvariantCulture)}," +
                                   $"{h.Rotation.W.ToString(CultureInfo.InvariantCulture)}," +
                                   $"{h.PitchRadians.ToString(CultureInfo.InvariantCulture)}");
                        }

                        sw.WriteLine();
                    }
                }
            }
            catch
            {
                // Swallow IO errors (disk locked, permissions, etc.).
            }
        }
        #endregion

        #region Naming Helpers

        /// <summary>
        /// Converts a user-supplied name (possibly null/empty) into the internal name key.
        /// - null/empty -> DefaultInternalName
        /// - "<default>" or "default" (case-insensitive) -> DefaultInternalName
        /// - otherwise -> trimmed name
        /// </summary>
        private static string ToInternalName(string displayName)
        {
            var s = (displayName ?? "").Trim();
            if (s.Length == 0) return DefaultInternalName;

            // Treat a couple of common ways of saying "default" as the default home.
            if (s.Equals(DefaultDisplayName, StringComparison.OrdinalIgnoreCase) ||
                s.Equals("default", StringComparison.OrdinalIgnoreCase))
                return DefaultInternalName;

            return s;
        }

        /// <summary>
        /// Converts an internal home key into a display name for chat.
        /// </summary>
        private static string ToDisplayNameInternal(string internalName)
            => string.Equals(internalName, DefaultInternalName, StringComparison.OrdinalIgnoreCase)
                ? DefaultDisplayName
                : internalName;

        /// <summary>
        /// Public helper: returns a chat-friendly name for whatever the player typed.
        /// </summary>
        public static string ToDisplayName(string displayName)
            => ToDisplayNameInternal(ToInternalName(displayName));

        #endregion

        #region CRUD API

        /// <summary>
        /// Returns a sorted list of home display names for a world.
        /// If includeDefault is false, the default home (if set) is excluded.
        /// </summary>
        public static IReadOnlyList<string> GetHomeDisplayNames(string worldKey, bool includeDefault)
        {
            EnsureLoaded();
            if (string.IsNullOrWhiteSpace(worldKey)) return Array.Empty<string>();

            if (!_db.TryGetValue(worldKey, out var map) || map.Count == 0)
                return Array.Empty<string>();

            // Sort by display name. Keep default at the front if included.
            var keys = map.Keys.ToList();
            if (!includeDefault)
                keys.RemoveAll(k => k.Equals(DefaultInternalName, StringComparison.OrdinalIgnoreCase));

            // Convert to display names.
            var display = keys.Select(ToDisplayNameInternal)
                              .OrderBy(s => s, StringComparer.OrdinalIgnoreCase)
                              .ToList();

            if (includeDefault)
            {
                // If default is present, force it to the beginning.
                int idx = display.FindIndex(s => s.Equals(DefaultDisplayName, StringComparison.OrdinalIgnoreCase));
                if (idx > 0)
                {
                    var tmp = display[idx];
                    display.RemoveAt(idx);
                    display.Insert(0, tmp);
                }
            }

            return display;
        }

        /// <summary>
        /// Looks up a home position.
        /// If displayName is null/empty, returns the per-world default home.
        /// </summary>
        public static bool TryGetHome(string worldKey, string displayName, out Vector3 pos, out Quaternion rot, out DNA.Angle pit)
        {
            EnsureLoaded();

            pos = default;
            rot = Quaternion.Identity;
            pit = DNA.Angle.Zero;

            if (string.IsNullOrWhiteSpace(worldKey)) return false;

            string key = ToInternalName(displayName);

            if (_db.TryGetValue(worldKey, out var map) && map.TryGetValue(key, out var home))
            {
                pos = home.Position;
                rot = home.Rotation;
                pit = DNA.Angle.FromRadians(home.PitchRadians);
                return true;
            }

            return false;
        }

        /// <summary>
        /// Try to get a saved home position using the legacy position-only API.
        /// This overload preserves compatibility with older call sites and ignores
        /// any stored rotation/pitch data.
        /// </summary>
        public static bool TryGetHome(string worldKey, string displayName, out Vector3 pos)
        {
            // Backward-compatible API for any old call sites.
            return TryGetHome(worldKey, displayName, out pos, out _, out _);
        }

        /// <summary>
        /// Adds or updates a home for a world; persists immediately.
        /// If displayName is null/empty, updates the per-world default home.
        /// </summary>
        public static void SaveHome(string worldKey, string displayName, Vector3 pos, Quaternion rot, DNA.Angle pit, bool overwrite)
        {
            if (string.IsNullOrWhiteSpace(worldKey)) return;

            // Basic safety for corrupted/invalid values.
            float pitRadians = pit.Radians;
            if (float.IsNaN(pitRadians) || float.IsInfinity(pitRadians))
                pitRadians = 0f;

            float lenSq = (rot.X * rot.X) + (rot.Y * rot.Y) + (rot.Z * rot.Z) + (rot.W * rot.W);
            if (float.IsNaN(lenSq) || float.IsInfinity(lenSq) || lenSq <= 0.000001f)
                rot = Quaternion.Identity;
            else
                rot.Normalize();

            EnsureLoaded();
            if (!_db.TryGetValue(worldKey, out var map))
            {
                map = new Dictionary<string, HomeRecord>(StringComparer.OrdinalIgnoreCase);
                _db[worldKey] = map;
            }

            string key = ToInternalName(displayName);
            var record = new HomeRecord(pos, rot, pitRadians);

            if (map.ContainsKey(key))
            {
                if (!overwrite) return;
                map[key] = record;
            }
            else
            {
                map.Add(key, record);
            }

            Save();
        }

        /// <summary>
        /// Save a home using the legacy position-only API.
        /// This overload preserves compatibility with older call sites by saving
        /// identity rotation and zero torso pitch.
        /// </summary>
        public static void SaveHome(string worldKey, string displayName, Vector3 pos, bool overwrite)
        {
            // Backward-compatible API for any old call sites.
            SaveHome(worldKey, displayName, pos, Quaternion.Identity, DNA.Angle.Zero, overwrite);
        }

        /// <summary>
        /// Removes a home by name (if present) and persists.
        /// If displayName is null/empty, deletes the per-world default home.
        /// Returns true if something was removed.
        /// </summary>
        public static bool RemoveHome(string worldKey, string displayName)
        {
            EnsureLoaded();
            if (string.IsNullOrWhiteSpace(worldKey)) return false;

            string key = ToInternalName(displayName);

            if (_db.TryGetValue(worldKey, out var map) && map.Remove(key))
            {
                Save();
                return true;
            }

            return false;
        }
        #endregion

        #region INI Parser

        /// <summary>
        /// Parses INI lines into the in-memory DB (tolerant of comments/whitespace).
        /// </summary>
        private static void ParseIni(IEnumerable<string> lines)
        {
            string section = null;

            foreach (var raw in lines)
            {
                var line = (raw ?? "").Trim();
                if (line.Length == 0) continue;
                if (line.StartsWith(";") || line.StartsWith("#")) continue;

                if (line.StartsWith("[") && line.EndsWith("]"))
                {
                    section = line.Substring(1, line.Length - 2).Trim();
                    if (section.Length == 0) section = null;
                    continue;
                }

                int eq = line.IndexOf('=');
                if (eq <= 0 || section == null) continue;

                string key = line.Substring(0, eq).Trim();
                string val = line.Substring(eq + 1).Trim();

                if (TryParseHomeRecord(val, out var h))
                {
                    if (!_db.TryGetValue(section, out var map))
                    {
                        map = new Dictionary<string, HomeRecord>(StringComparer.OrdinalIgnoreCase);
                        _db[section] = map;
                    }

                    // Keep whatever key was stored (including DefaultInternalName if present).
                    map[key] = h;
                }
            }
        }

        /// <summary>
        /// Parse one saved home entry from storage text into a HomeRecord.
        /// Supports both the legacy position-only format (x,y,z) and the newer
        /// position+rotation+pitch format (x,y,z,qx,qy,qz,qw,pitch), with basic
        /// validation/sanitization for quaternion and pitch values.
        /// </summary>
        private static bool TryParseHomeRecord(string s, out HomeRecord h)
        {
            h = default;
            if (string.IsNullOrWhiteSpace(s)) return false;

            var parts = s.Split(',');
            // Old format: x,y,z
            // New format: x,y,z,qx,qy,qz,qw,pitch
            if (parts.Length != 3 && parts.Length != 8) return false;

            bool TryParsePart(int i, out float f) =>
                float.TryParse(parts[i].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out f);

            if (!TryParsePart(0, out float x) ||
                !TryParsePart(1, out float y) ||
                !TryParsePart(2, out float z))
                return false;

            var pos = new Vector3(x, y, z);

            // Backward compatibility for old saved homes.
            if (parts.Length == 3)
            {
                h = new HomeRecord(pos, Quaternion.Identity, 0f);
                return true;
            }

            if (!TryParsePart(3, out float qx) ||
                !TryParsePart(4, out float qy) ||
                !TryParsePart(5, out float qz) ||
                !TryParsePart(6, out float qw) ||
                !TryParsePart(7, out float pit))
                return false;

            var rot = new Quaternion(qx, qy, qz, qw);

            float lenSq = (rot.X * rot.X) + (rot.Y * rot.Y) + (rot.Z * rot.Z) + (rot.W * rot.W);
            if (float.IsNaN(lenSq) || float.IsInfinity(lenSq) || lenSq <= 0.000001f)
                rot = Quaternion.Identity;
            else
                rot.Normalize();

            if (float.IsNaN(pit) || float.IsInfinity(pit))
                pit = 0f;

            h = new HomeRecord(pos, rot, pit);
            return true;
        }

        /// <summary>
        /// Parses "x,y,z" into a Vector3 using InvariantCulture.
        /// </summary>
        private static bool TryParseVec3(string s, out Vector3 v)
        {
            v = default;
            if (string.IsNullOrWhiteSpace(s)) return false;

            var parts = s.Split(',');
            if (parts.Length != 3) return false;

            if (float.TryParse(parts[0].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out float x) &&
                float.TryParse(parts[1].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out float y) &&
                float.TryParse(parts[2].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out float z))
            {
                v = new Vector3(x, y, z);
                return true;
            }

            return false;
        }
        #endregion
    }
}