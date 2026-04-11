/*
SPDX-License-Identifier: GPL-3.0-or-later
Copyright (c) 2025 RussDev7
This file is part of https://github.com/RussDev7/CastleForge - see LICENSE for details.
*/

using System.Collections.Generic;
using DNA.CastleMinerZ;
using System.Linq;
using System.IO;
using System;
using DNA;

using static ModLoader.LogSystem;

namespace RegionProtect
{
    /// <summary>
    /// Persistent region database stored as INI-like text.
    ///
    /// Responsibilities:
    /// - Stores region definitions (AABB bounds + allowed player whitelist).
    /// - Stores optional spawn-protection settings (range + whitelist).
    /// - Persists data to disk under a per-world folder keyed by the current world GUID.
    /// - Exposes thread-safe query/mutation helpers for commands (create/edit/delete/list).
    /// - Pushes a lock-free snapshot into <see cref="RegionProtectCore"/> after load/save.
    ///
    /// Per-world file location:
    ///   !Mods/RegionProtect/Worlds/<WorldID>/RegionProtect.Regions.ini
    ///
    /// Sections:
    ///   [SpawnProtection]
    ///     Enabled = true|false
    ///     Range   = 16
    ///     AllowedPlayers = Alice,Bob
    ///
    ///   [Region:MyBase]
    ///     Min = x,y,z
    ///     Max = x,y,z
    ///     AllowedPlayers = Alice,Bob
    /// </summary>
    internal static class RegionProtectStore
    {
        #region Models

        /// <summary>
        /// Single protected region definition.
        /// - Name: user-facing identifier (e.g., "Base", "Town", "test").
        /// - Min/Max: normalized inclusive AABB bounds in block coordinates.
        /// - AllowedPlayers: whitelist of gamertags permitted to mine/build inside this region.
        /// </summary>
        internal sealed class RegionDef
        {
            public string Name;
            public IntVector3 Min;
            public IntVector3 Max;
            public HashSet<string> AllowedPlayers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            /// <summary>
            /// Returns true if <paramref name="p"/> is inside the region AABB (inclusive).
            /// </summary>
            public bool Contains(IntVector3 p)
            {
                return p.X >= Min.X && p.X <= Max.X &&
                       p.Y >= Min.Y && p.Y <= Max.Y &&
                       p.Z >= Min.Z && p.Z <= Max.Z;
            }

            /// <summary>
            /// Returns true if <paramref name="gamerTag"/> is whitelisted for this region.
            /// </summary>
            public bool IsPlayerAllowed(string gamerTag)
            {
                if (string.IsNullOrWhiteSpace(gamerTag)) return false;
                return AllowedPlayers.Contains(RegionProtectCore.NormalizePlayer(gamerTag));
            }
        }

        /// <summary>
        /// Spawn protection definition.
        /// - Enabled: whether the spawn area is protected.
        /// - Range: XZ radius (in blocks) centered at origin (0,0).
        /// - AllowedPlayers: whitelist allowed to mine/build within the spawn protected radius.
        /// </summary>
        internal sealed class SpawnProtectionDef
        {
            public bool Enabled = false;
            public int Range = 16; // blocks, XZ radius
            public HashSet<string> AllowedPlayers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }
        #endregion

        #region State

        /// <summary>
        /// Global lock protecting region/spawn structures against concurrent access.
        /// </summary>
        private static readonly object _lock = new object();

        /// <summary>
        /// In-memory region map for the currently loaded world.
        /// Keyed by region name (case-insensitive).
        /// </summary>
        private static readonly Dictionary<string, RegionDef> _regions =
            new Dictionary<string, RegionDef>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Spawn protection settings for the currently loaded world.
        /// </summary>
        internal static SpawnProtectionDef Spawn = new SpawnProtectionDef();

        /// <summary>
        /// Sentinel key used when the world is not available yet (e.g., menus/loading).
        /// </summary>
        private const string PendingWorld = "(pending-world)";

        /// <summary>
        /// World key currently loaded into this store (used to route file IO per-world).
        /// </summary>
        private static string _loadedWorldKey = PendingWorld;

        /// <summary>
        /// Base folder for RegionProtect mod data.
        /// </summary>
        private static string BaseDir =>
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "!Mods", "RegionProtect");

        /// <summary>
        /// Root folder containing one subfolder per world id.
        /// </summary>
        private static string WorldsDir => Path.Combine(BaseDir, "Worlds");

        #endregion

        #region Paths

        /// <summary>
        /// Path to the region database file for the currently loaded world key.
        /// </summary>
        public static string RegionsPath => RegionsPathFor(_loadedWorldKey);

        #endregion

        #region Load/Save

        /// <summary>
        /// Ensures the store is loaded for the current world.
        /// - If the world key hasn't changed and <paramref name="force"/> is false, does nothing.
        /// - If the world is pending, clears runtime state and returns false.
        /// - Otherwise loads/creates the per-world region database and returns true.
        /// </summary>
        public static bool EnsureLoadedForCurrentWorld(bool force = false)
        {
            string key = CurrentWorldKey();

            if (!force && string.Equals(key, _loadedWorldKey, StringComparison.OrdinalIgnoreCase))
                return key != PendingWorld;

            _loadedWorldKey = key;

            // If we don't have a world yet, keep runtime empty and don't persist.
            if (key == PendingWorld)
            {
                lock (_lock)
                {
                    _regions.Clear();
                    Spawn = new SpawnProtectionDef(); // defaults
                }

                RegionProtectCore.ApplyStoreSnapshot(new List<RegionDef>(), Spawn);
                return false;
            }

            LoadOrCreate(); // uses RegionsPath, which now points at the current world
            return true;
        }

        /// <summary>
        /// Loads the current world's region database (force reload).
        /// Intended to be called on mod startup and via admin reload commands.
        /// </summary>
        public static void LoadApply()
        {
            try
            {
                EnsureLoadedForCurrentWorld(force: true);
            }
            catch (Exception ex)
            {
                Log($"[RPStore] Failed to load: {ex.Message}.");
            }
        }

        /// <summary>
        /// Loads the per-world regions file (creating it if missing), populates in-memory state,
        /// then publishes a snapshot to <see cref="RegionProtectCore"/>.
        /// </summary>
        public static void LoadOrCreate()
        {
            var dir = Path.GetDirectoryName(RegionsPath);
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

            if (!File.Exists(RegionsPath))
            {
                File.WriteAllLines(RegionsPath, new[]
                {
                    "# RegionProtect - Regions",
                    "# Lines starting with ';' or '#' are comments.",
                    "",
                    "[SpawnProtection]",
                    "; If true, blocks are protected within Range of spawn (origin).",
                    "Enabled        = false",
                    "; Horizontal (XZ) radius in blocks.",
                    "Range          = 16",
                    "; Comma-separated list of players allowed to edit near spawn.",
                    "; Default is empty -> nobody can mine at spawn until added.",
                    "AllowedPlayers =",
                    "",
                    "# Example region:",
                    "# [Region:MyBase]",
                    "# Min = -10,0,-10",
                    "# Max =  10,50, 10",
                    "# AllowedPlayers = Alice,Bob",
                    "",
                });
            }

            var parsed = IniLite.Parse(File.ReadAllLines(RegionsPath));

            lock (_lock)
            {
                _regions.Clear();

                // SpawnProtection.
                {
                    var sec = parsed.GetSection("SpawnProtection");
                    Spawn.Enabled = sec.GetBool("Enabled", false);
                    Spawn.Range = Clamp(sec.GetInt("Range", 16), 0, 512);
                    Spawn.AllowedPlayers.Clear();
                    foreach (var p in RegionProtectCore.ParsePlayerCsv(sec.GetString("AllowedPlayers", "")))
                        Spawn.AllowedPlayers.Add(p);
                }

                // Regions.
                foreach (var s in parsed.SectionNames)
                {
                    if (!s.StartsWith("Region:", StringComparison.OrdinalIgnoreCase)) continue;

                    string name = s.Substring("Region:".Length).Trim();
                    if (name.Length == 0) continue;

                    var sec = parsed.GetSection(s);
                    if (!TryParseVec3(sec.GetString("Min", ""), out var min)) continue;
                    if (!TryParseVec3(sec.GetString("Max", ""), out var max)) continue;

                    var r = new RegionDef
                    {
                        Name = name,
                        Min = RegionProtectCore.Min(min, max),
                        Max = RegionProtectCore.Max(min, max),
                    };

                    foreach (var p in RegionProtectCore.ParsePlayerCsv(sec.GetString("AllowedPlayers", "")))
                        r.AllowedPlayers.Add(p);

                    _regions[name] = r;
                }
            }

            RegionProtectCore.ApplyStoreSnapshot(GetAllRegionsSnapshot(), Spawn);
        }

        /// <summary>
        /// Writes the current in-memory region/spawn state to the per-world regions file,
        /// then republishes an updated snapshot to <see cref="RegionProtectCore"/>.
        /// </summary>
        public static void Save()
        {
            lock (_lock)
            {
                if (_loadedWorldKey == PendingWorld)
                {
                    Log("World not ready yet; regions not saved.");
                    return;
                }

                var path = RegionsPath;
                var dir = Path.GetDirectoryName(path);
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

                var lines = new List<string>
                {
                    "# RegionProtect - Regions",
                    "# Lines starting with ';' or '#' are comments.",
                    "",
                    "[SpawnProtection]",
                    "Enabled        = " + (Spawn.Enabled ? "true" : "false"),
                    "Range          = " + Spawn.Range.ToString(),
                    "AllowedPlayers = " + string.Join(",", Spawn.AllowedPlayers.OrderBy(s => s, StringComparer.OrdinalIgnoreCase)),
                    ""
                };

                foreach (var kv in _regions.OrderBy(k => k.Key, StringComparer.OrdinalIgnoreCase))
                {
                    var r = kv.Value;
                    lines.Add("[Region:" + r.Name + "]");
                    lines.Add("Min            = " + Vec3ToString(r.Min));
                    lines.Add("Max            = " + Vec3ToString(r.Max));
                    lines.Add("AllowedPlayers = " + string.Join(",", r.AllowedPlayers.OrderBy(s => s, StringComparer.OrdinalIgnoreCase)));
                    lines.Add("");
                }

                File.WriteAllLines(path, lines.ToArray());
            }

            RegionProtectCore.ApplyStoreSnapshot(GetAllRegionsSnapshot(), Spawn);
        }
        #endregion

        #region Queries / Mutations

        /// <summary>
        /// Returns true if a region with <paramref name="name"/> exists in the currently loaded world.
        /// </summary>
        public static bool RegionExists(string name)
        {
            lock (_lock) return _regions.ContainsKey(name);
        }

        /// <summary>
        /// Creates a new region in the current world.
        /// Returns false if a region with the same name already exists.
        /// </summary>
        public static bool UpsertRegion(string name, IntVector3 min, IntVector3 max, HashSet<string> allowedPlayers)
        {
            lock (_lock)
            {
                if (_regions.ContainsKey(name)) return false;
                _regions[name] = new RegionDef
                {
                    Name = name,
                    Min = RegionProtectCore.Min(min, max),
                    Max = RegionProtectCore.Max(min, max),
                    AllowedPlayers = new HashSet<string>(allowedPlayers ?? new HashSet<string>(), StringComparer.OrdinalIgnoreCase),
                };
                return true;
            }
        }

        /// <summary>
        /// Deletes a region by name from the current world.
        /// Returns false if no such region exists.
        /// </summary>
        public static bool RemoveRegion(string name)
        {
            lock (_lock) return _regions.Remove(name);
        }

        /// <summary>
        /// Adds/removes a player from a region's whitelist.
        /// Returns false if the region doesn't exist.
        /// </summary>
        public static bool TryEditRegion(string name, bool add, string player)
        {
            lock (_lock)
            {
                if (!_regions.TryGetValue(name, out var r)) return false;
                var p = RegionProtectCore.NormalizePlayer(player);
                if (add) r.AllowedPlayers.Add(p);
                else r.AllowedPlayers.Remove(p);
                return true;
            }
        }

        /// <summary>
        /// Returns the first region (if any) containing <paramref name="p"/>.
        /// Used for "edit the region I'm standing in" command behavior.
        /// </summary>
        public static RegionDef FindFirstRegionAt(IntVector3 p)
        {
            lock (_lock)
            {
                foreach (var r in _regions.Values)
                    if (r.Contains(p)) return r;
            }
            return null;
        }

        /// <summary>
        /// Returns all regions containing <paramref name="p"/> (usually 0 or 1, but overlaps are supported).
        /// </summary>
        public static List<RegionDef> FindRegionsAt(IntVector3 p)
        {
            var list = new List<RegionDef>();
            lock (_lock)
            {
                foreach (var r in _regions.Values)
                    if (r.Contains(p)) list.Add(r);
            }
            return list;
        }

        /// <summary>
        /// Returns formatted summary lines for all regions in the currently loaded world.
        /// </summary>
        public static List<string> GetRegionSummaries()
        {
            var list = new List<string>();
            lock (_lock)
            {
                foreach (var r in _regions.Values.OrderBy(r => r.Name, StringComparer.OrdinalIgnoreCase))
                {
                    list.Add($"{r.Name}: {r.Min} .. {r.Max} | Allowed: {RegionProtectCore.FormatPlayers(r.AllowedPlayers)}");
                }
            }
            return list;
        }

        /// <summary>
        /// Builds a deep-ish copy of the region set for publishing to lock-free runtime reads.
        /// </summary>
        private static List<RegionDef> GetAllRegionsSnapshot()
        {
            lock (_lock)
            {
                // Deep-ish copy for runtime reads without locking.
                return _regions.Values.Select(r => new RegionDef
                {
                    Name = r.Name,
                    Min = r.Min,
                    Max = r.Max,
                    AllowedPlayers = new HashSet<string>(r.AllowedPlayers, StringComparer.OrdinalIgnoreCase),
                }).ToList();
            }
        }
        #endregion

        #region Helpers

        /// <summary>
        /// Formats an IntVector3 for INI serialization as "x,y,z".
        /// </summary>
        private static string Vec3ToString(IntVector3 v) => $"{v.X},{v.Y},{v.Z}";

        /// <summary>
        /// Parses an IntVector3 from a "x,y,z" string.
        /// Returns false if the input is missing or malformed.
        /// </summary>
        private static bool TryParseVec3(string s, out IntVector3 v)
        {
            v = IntVector3.Zero;
            if (string.IsNullOrWhiteSpace(s)) return false;

            var parts = s.Split(',');
            if (parts.Length != 3) return false;

            if (!int.TryParse(parts[0].Trim(), out int x)) return false;
            if (!int.TryParse(parts[1].Trim(), out int y)) return false;
            if (!int.TryParse(parts[2].Trim(), out int z)) return false;

            v = new IntVector3(x, y, z);
            return true;
        }

        /// <summary>
        /// Clamps an integer into [lo..hi].
        /// </summary>
        private static int Clamp(int v, int lo, int hi) => (v < lo) ? lo : (v > hi ? hi : v);

        #region Path Helpers

        /// <summary>
        /// Converts a world key into a safe folder name by replacing invalid filename characters.
        /// </summary>
        private static string SafeDirName(string key)
        {
            if (string.IsNullOrWhiteSpace(key)) return PendingWorld;
            foreach (char c in Path.GetInvalidFileNameChars())
                key = key.Replace(c, '_');
            return key;
        }

        /// <summary>
        /// Returns the per-world regions file path for the provided <paramref name="worldKey"/>.
        /// </summary>
        private static string RegionsPathFor(string worldKey)
        {
            return Path.Combine(WorldsDir, SafeDirName(worldKey), "RegionProtect.Regions.ini");
        }
        #endregion

        #region World Helpers

        /// <summary>
        /// Returns the current world GUID as string, or "(pending-world)" if unavailable.
        /// Used to scope region files per world (multiplayer safe).
        /// </summary>
        public static string CurrentWorldKey()
        {
            var g = CastleMinerZGame.Instance;
            var id = g?.CurrentWorld?.WorldID.ToString();
            return string.IsNullOrWhiteSpace(id) ? PendingWorld : id;
        }
        #endregion

        #endregion

        #region IniLite

        /// <summary>
        /// Minimal INI parser used for the regions database.
        /// - Supports [Section] headers
        /// - Supports "key = value" pairs
        /// - Skips ';' and '#' comments and blank lines
        /// - Intended for simple persistence (not a full-featured INI implementation)
        /// </summary>
        private sealed class IniLite
        {
            private readonly Dictionary<string, Dictionary<string, string>> _data =
                new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);

            /// <summary>
            /// Enumerates all section names found during parsing.
            /// </summary>
            public IEnumerable<string> SectionNames => _data.Keys;

            /// <summary>
            /// Parses raw INI lines into an IniLite structure.
            /// </summary>
            public static IniLite Parse(string[] lines)
            {
                var ini = new IniLite();
                string section = "";

                foreach (var raw in lines ?? Array.Empty<string>())
                {
                    var line = (raw ?? "").Trim();
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
            /// Returns a typed view over a section's key-value pairs.
            /// Missing sections return an empty view (with defaults applied at read time).
            /// </summary>
            public IniSection GetSection(string name)
            {
                if (!_data.TryGetValue(name, out var d))
                    d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                return new IniSection(d);
            }

            /// <summary>
            /// Typed accessor wrapper for a single INI section.
            /// Provides basic string/int/bool reads with defaults.
            /// </summary>
            public sealed class IniSection
            {
                private readonly Dictionary<string, string> _d;

                /// <summary>
                /// Wraps the provided dictionary as a section accessor.
                /// </summary>
                public IniSection(Dictionary<string, string> d) => _d = d;

                /// <summary>
                /// Reads a string value by key (or returns <paramref name="def"/> if missing).
                /// </summary>
                public string GetString(string key, string def)
                    => _d.TryGetValue(key, out var v) ? v : def;

                /// <summary>
                /// Reads an int value by key (or returns <paramref name="def"/> if missing/invalid).
                /// </summary>
                public int GetInt(string key, int def)
                {
                    var s = GetString(key, def.ToString());
                    return int.TryParse(s, out var v) ? v : def;
                }

                /// <summary>
                /// Reads a bool value by key (or returns <paramref name="def"/> if missing/invalid).
                /// Supports: true/false, 1/0, on/off.
                /// </summary>
                public bool GetBool(string key, bool def)
                {
                    var s = GetString(key, def ? "true" : "false");
                    if (bool.TryParse(s, out var b)) return b;
                    s = (s ?? "").Trim();
                    if (s == "1" || s.Equals("on", StringComparison.OrdinalIgnoreCase)) return true;
                    if (s == "0" || s.Equals("off", StringComparison.OrdinalIgnoreCase)) return false;
                    return def;
                }
            }
        }
        #endregion
    }
}