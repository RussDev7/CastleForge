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
using ImGuiNET;
using System;

namespace CastleWallsMk2
{
    /// <summary>
    /// Persistence layer for per-world "homes" (named <see cref="Vector3"/> positions).
    /// Stores data in an INI file per WorldID and exposes a small CRUD API used by the UI.
    /// </summary>
    /// <remarks>
    /// Storage:
    /// • Path: !Mods\{typeof(EmbeddedResolver).Namespace}\CastleWallsMk2.Homes.ini (created on first use).
    /// • Format: one section per world - [<world-guid>]; lines are HomeName = x,y,z[,qx,qy,qz,qw,pitch] (InvariantCulture).
    /// • Comments starting with ; or # are ignored.
    ///
    /// Dictionary semantics:
    /// • In-memory DB is Dictionary<string, Dictionary<string, HomeRecord>> with
    ///   StringComparer.OrdinalIgnoreCase (names are case-insensitive).
    /// • TryRenameHome handles case-only renames correctly by removing then re-adding the key.
    ///
    /// Threading & failure behavior:
    /// • Not thread-safe; call from the game thread/UI.
    /// • IO/parsing failures are swallowed; the in-memory DB remains usable and a future Save()
    ///   will rewrite a clean file when possible.
    ///
    /// Public API:
    /// • EnsureLoaded()                                                  - Lazy-load/seed file and in-memory DB.
    /// • Save()                                                          - Persist current DB to disk (stable, sorted order).
    /// • CurrentWorldKey()                                               - Returns active world's GUID (or "(pending-world)").
    /// • GetHomeNames(worldKey)                                          - Sorted list of home names for a world.
    /// • TryGetHome(worldKey, name, out pos, out rot, out pit)           - Lookup position + rotation + torso pitch.
    /// • TryGetHome(worldKey, name, out pos)                             - Legacy position-only lookup overload.
    /// • SaveHome(worldKey, name, pos, rot, pit, overwrite)              - Upsert a home (position + rotation + pitch), then persist.
    /// • SaveHome(worldKey, name, pos, overwrite)                        - Legacy position-only save overload.
    /// • RemoveHome(worldKey, name)                                      - Delete a home and persist.
    /// • TryRenameHome(worldKey, oldName, newName, out error, overwrite) - Rename with case-insensitive collision handling (supports case-only rename).
    ///
    /// Typical usage:
    /// 1) var key = HomesExtensions.CurrentWorldKey();
    /// 2) HomesExtensions.SaveHome(key, "Home 1", player.LocalPosition, player.LocalRotation, player.TorsoPitch);
    /// 3) if (HomesExtensions.TryGetHome(key, "Home 1", out var pos, out var rot, out var pit))
    ///    {
    ///        player.LocalPosition = pos;
    ///        player.LocalRotation = rot;
    ///        player.TorsoPitch    = pit;
    ///    }
    /// </remarks>
    internal static class HomesExtensions
    {
        #region Path / Storage

        /// <summary>Absolute path to the homes INI (under !Mods\{resolver ns}\{resolver ns}.Homes.ini).</summary>
        public static string HomesPath =>
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "!Mods", typeof(EmbeddedResolver).Namespace, $"{typeof(EmbeddedResolver).Namespace}.Homes.ini");

        // In-memory DB: worldId -> (homeName -> position). Case-insensitive keys on both levels.
        private static readonly Dictionary<string, Dictionary<string, HomeRecord>> _db =
            new Dictionary<string, Dictionary<string, HomeRecord>>(StringComparer.OrdinalIgnoreCase);

        private static bool _loaded;

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

        #region Load/Save

        /// <summary>Lazy-load the INI once; creates an empty file with a header if missing.</summary>
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
                    // Seed an empty file with a friendly header.
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
                // If parsing fails, leave _db empty; UI will still work and Save() will rewrite a clean file.
            }
        }

        /// <summary>Persist the entire in-memory DB back to INI in a stable, readable order.</summary>
        public static void Save()
        {
            try
            {
                var dir = Path.GetDirectoryName(HomesPath);
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

                using (var sw = new StreamWriter(HomesPath, false))
                {
                    sw.WriteLine("; CastleWallsMk2 - Homes");
                    sw.WriteLine($"; LastSaved = {DateTime.Now:G}");
                    sw.WriteLine();

                    // Stable, readable order
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
                // Swallow IO errors (disk locked, permissions, etc). Consider logging in your mod logger.
            }
        }

        #endregion

        #region Public API

        /// <summary>Returns the current world GUID as string, or "(pending-world)" if unavailable.</summary>
        public static string CurrentWorldKey()
        {
            var g = CastleMinerZGame.Instance;
            var id = g?.CurrentWorld?.WorldID.ToString();
            return string.IsNullOrWhiteSpace(id) ? "(pending-world)" : id;
        }

        /// <summary>Returns sorted home names for a world (empty list if none).</summary>
        public static IReadOnlyList<string> GetHomeNames(string worldKey)
        {
            EnsureLoaded();
            if (worldKey == null) return Array.Empty<string>();
            return _db.TryGetValue(worldKey, out var map)
                 ? map.Keys.OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToArray()
                 : Array.Empty<string>();
        }

        /// <summary>Looks up a home and returns position/rotation/pitch if found.</summary>
        public static bool TryGetHome(string worldKey, string homeName, out Vector3 pos, out Quaternion rot, out DNA.Angle pit)
        {
            EnsureLoaded();

            pos = default;
            rot = Quaternion.Identity;
            pit = DNA.Angle.Zero;

            return worldKey != null &&
                   _db.TryGetValue(worldKey, out var map) &&
                   map.TryGetValue(homeName ?? "", out var h) &&
                   TryUnpackHomeRecord(h, out pos, out rot, out pit);
        }

        /// <summary>
        /// Try to get a saved home position using the legacy position-only API.
        /// This overload preserves compatibility with older call sites and ignores
        /// any stored rotation/pitch data.
        /// </summary>
        public static bool TryGetHome(string worldKey, string homeName, out Vector3 pos)
        {
            return TryGetHome(worldKey, homeName, out pos, out _, out _);
        }

        /// <summary>
        /// Unpacks a stored HomeRecord into position, rotation, and torso pitch.
        /// Also sanitizes invalid rotation/pitch values (falls back to identity rotation
        /// and zero pitch) before converting pitch radians into a DNA.Angle.
        /// </summary>
        private static bool TryUnpackHomeRecord(HomeRecord h, out Vector3 pos, out Quaternion rot, out DNA.Angle pit)
        {
            pos = h.Position;
            rot = h.Rotation;

            float lenSq = (rot.X * rot.X) + (rot.Y * rot.Y) + (rot.Z * rot.Z) + (rot.W * rot.W);
            if (float.IsNaN(lenSq) || float.IsInfinity(lenSq) || lenSq <= 0.000001f)
                rot = Quaternion.Identity;
            else
                rot.Normalize();

            float pr = h.PitchRadians;
            if (float.IsNaN(pr) || float.IsInfinity(pr))
                pr = 0f;

            pit = DNA.Angle.FromRadians(pr);
            return true;
        }

        /// <summary>Adds or updates a home for a world; persists immediately.</summary>
        /// <summary>Adds or updates a home for a world (position + rotation + pitch); persists immediately.</summary>
        public static void SaveHome(string worldKey, string homeName, Vector3 pos, Quaternion rot, DNA.Angle pit, bool overwrite = true)
        {
            if (string.IsNullOrWhiteSpace(worldKey)) return;
            if (string.IsNullOrWhiteSpace(homeName)) return;

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

            var record = new HomeRecord(pos, rot, pitRadians);

            if (map.ContainsKey(homeName))
            {
                if (!overwrite) return;
                map[homeName] = record;
            }
            else
            {
                map.Add(homeName, record);
            }

            Save();
        }

        /// <summary>
        /// Save a home using the legacy position-only API.
        /// This overload preserves compatibility with older call sites by saving
        /// identity rotation and zero torso pitch.
        /// </summary>
        public static void SaveHome(string worldKey, string homeName, Vector3 pos, bool overwrite = true)
        {
            SaveHome(worldKey, homeName, pos, Quaternion.Identity, DNA.Angle.Zero, overwrite);
        }

        /// <summary>Removes a home by name (if present) and persists.</summary>
        public static void RemoveHome(string worldKey, string homeName)
        {
            EnsureLoaded();
            if (string.IsNullOrWhiteSpace(worldKey) || string.IsNullOrWhiteSpace(homeName)) return;

            if (_db.TryGetValue(worldKey, out var map))
            {
                if (map.Remove(homeName))
                    Save();
            }
        }

        /// <summary>Renames a home; handles case-only rename safely with a case-insensitive dictionary.</summary>
        public static bool TryRenameHome(
            string worldKey, string oldName, string newName,
            out string error, bool overwrite = false)
        {
            error = null;
            EnsureLoaded();

            if (string.IsNullOrWhiteSpace(worldKey)) { error = "No world."; return false; }
            if (string.IsNullOrWhiteSpace(oldName))  { error = "No source name."; return false; }
            if (string.IsNullOrWhiteSpace(newName))  { error = "New name is empty."; return false; }

            if (!_db.TryGetValue(worldKey, out var map)) { error = "World not found."; return false; }
            if (!map.TryGetValue(oldName, out var home)) { error = $"Home '{oldName}' not found."; return false; }

            // Exact same string (including case) -> nothing to do.
            if (string.Equals(oldName, newName, StringComparison.Ordinal))
                return true;

            bool caseOnly = string.Equals(oldName, newName, StringComparison.OrdinalIgnoreCase);

            if (!caseOnly && map.ContainsKey(newName) && !overwrite)
            {
                error = $"A home named '{newName}' already exists.";
                return false;
            }

            // NOTE: With a case-insensitive comparer, case-only renames must remove first, then add.
            if (caseOnly)
            {
                map.Remove(oldName);
                map[newName] = home;
            }
            else
            {
                if (overwrite && map.ContainsKey(newName))
                    map.Remove(newName); // Clear destination row first.

                map.Remove(oldName);
                map[newName] = home;
            }

            Save();
            return true;
        }
        #endregion

        #region INI Parser

        /// <summary>Parses INI lines into the in-memory DB (tolerant of comments/whitespace).</summary>
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

            // Old file format -> default facing.
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

        /// <summary>Parses "x,y,z" into a Vector3 using InvariantCulture.</summary>
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

    /// <summary>
    /// ImGui "Homes" widget for the current world (per-WorldID).
    /// Features:
    /// • Combo listing saved homes for the active world (auto-refreshing).
    /// • Load:   Teleports to the selected home (LocalPlayer.LocalPosition).
    /// • Save:   Saves the current position under the typed name, or auto-names "Home N"; selects it and clears the input.
    /// • Rename: Renames the selected home to the typed name (handles case-only renames with a case-insensitive map).
    /// • Delete: Removes the selected home, clears the input box, and clamps selection.
    /// UX details:
    /// • Selection & text box reset when the world changes (by WorldID).
    /// • Backed by HomesExtensions (INI persistence under !Mods\{resolver-ns}\{resolver-ns}.Homes.ini).
    /// Usage: Call HomesUI.DrawHomesWidget() from your CastleWallsMk2 tab draw on the game thread.
    /// </summary>
    internal static class HomesUI
    {
        #region Fields / UI State

        // UI state (per session)
        private static int    _selectedIndex = -1;
        private static string _nameBuffer    = "";   // Typed name for Save/Rename.
        private static string _lastWorldKey  = null; // Detects world switches to reset UI.

        #endregion

        #region Public Entry

        /// <summary>Draws the homes combo + name field + buttons; handles refresh/selection & input clearing.</summary>
        public static void DrawHomesWidget()
        {
            ImGui.PushID("CWMK2_Homes");
            try
            {
                var game = CastleMinerZGame.Instance;
                if (game?.LocalPlayer == null || game.CurrentWorld == null)
                {
                    // Optionally show a hint instead of UI.
                    // IGMainUI.CenterText("Homes (no world/player)");
                    // return;
                }

                #region World Switch Detection & Data Fetch

                // World change -> reset selection & text box so we don't leak state across worlds.
                string worldKey = HomesExtensions.CurrentWorldKey();
                if (!string.Equals(worldKey, _lastWorldKey, StringComparison.Ordinal))
                {
                    _lastWorldKey  = worldKey;
                    _selectedIndex = -1;
                    _nameBuffer    = "";
                }

                // Pull homes for this world (fresh each draw so list is always current).
                var names           = HomesExtensions.GetHomeNames(worldKey);
                string currentLabel = (_selectedIndex >= 0 && _selectedIndex < names.Count)
                                    ? names[_selectedIndex]
                                    : "<none>";
                #endregion

                // --- Layout ---

                ImGui.AlignTextToFramePadding();
                IGMainUI.CenterText("Save Positions:");
                ImGui.BeginGroup();

                #region Top Row: Combo + Name Input
                {
                    ImGui.SetNextItemWidth(-1);
                    if (ImGui.BeginCombo("##homes_combo", currentLabel))
                    {
                        for (int i = 0; i < names.Count; i++)
                        {
                            bool sel = (i == _selectedIndex);
                            if (ImGui.Selectable(names[i], sel))
                            {
                                _selectedIndex = i;
                                _nameBuffer = names[i]; // seed rename from selection
                            }
                            if (sel) ImGui.SetItemDefaultFocus();
                        }
                        ImGui.EndCombo();
                    }

                    ImGui.SetNextItemWidth(-1);
                    ImGui.InputTextWithHint("##home_name", "New home name.", ref _nameBuffer, 64);
                }
                #endregion

                #region Middle Row: Load | Save (50/50)
                {
                    float avail = ImGui.GetContentRegionAvail().X;
                    float gap   = ImGui.GetStyle().ItemSpacing.X;
                    float w     = (avail - gap * 1f) / 2f;

                    bool haveSelection = _selectedIndex >= 0 && _selectedIndex < names.Count;

                    // LOAD.
                    if (!haveSelection) ImGui.BeginDisabled();
                    if (ImGui.Button("Load", new System.Numerics.Vector2(w, 0)))
                    {
                        if (haveSelection &&
                            HomesExtensions.TryGetHome(worldKey, names[_selectedIndex], out var pos, out var rot, out var pit))
                        {
                            game.LocalPlayer.LocalPosition = pos;
                            game.LocalPlayer.LocalRotation = rot;
                            game.LocalPlayer.TorsoPitch    = pit;
                        }
                    }
                    if (!haveSelection) ImGui.EndDisabled();

                    ImGui.SameLine();

                    // SAVE (auto-names when box is empty; clears the box after save to enable quick "Home 1/2/3").
                    if (ImGui.Button("Save", new System.Numerics.Vector2(w, 0)))
                    {
                        string name = (_nameBuffer ?? "").Trim();
                        if (string.IsNullOrEmpty(name))
                            name = NextAutoName(worldKey); // Fresh read -> handles "save again" correctly.

                        var pos = game.LocalPlayer.LocalPosition;
                        var rot = game.LocalPlayer.LocalRotation;
                        var pit = game.LocalPlayer.TorsoPitch;
                        HomesExtensions.SaveHome(worldKey, name, pos, rot, pit, overwrite: true);

                        // Refresh & select the saved name; then clear input for next quick save.
                        var refreshed = HomesExtensions.GetHomeNames(worldKey);
                        _selectedIndex = Array.IndexOf(refreshed.ToArray(), name);
                        _nameBuffer    = string.Empty;
                    }
                }
                #endregion

                #region Bottom Row: Rename | Delete (50/50)
                {
                    float avail = ImGui.GetContentRegionAvail().X;
                    float gap   = ImGui.GetStyle().ItemSpacing.X;
                    float w     = (avail - gap * 1f) / 2f;

                    bool haveSelection = _selectedIndex >= 0 && _selectedIndex < names.Count;
                    bool canRename     = haveSelection && !string.IsNullOrWhiteSpace(_nameBuffer);

                    // RENAME.
                    if (!canRename) ImGui.BeginDisabled();
                    if (ImGui.Button("Rename", new System.Numerics.Vector2(w, 0)))
                    {
                        var oldName = names[_selectedIndex];
                        var newName = _nameBuffer.Trim();

                        if (!string.Equals(oldName, newName, StringComparison.Ordinal))
                        {
                            if (!HomesExtensions.TryRenameHome(worldKey, oldName, newName, out var err, overwrite: false))
                            {
                                // If only a conflict exists, we allow overwrite.
                                if (err?.IndexOf("already exists", StringComparison.OrdinalIgnoreCase) >= 0)
                                    HomesExtensions.TryRenameHome(worldKey, oldName, newName, out _, overwrite: true);
                                else
                                    ModLoader.LogSystem.Log($"House extensions error: {err}.");
                            }

                            // Refresh & keep selection on the new name.
                            var refreshed = HomesExtensions.GetHomeNames(worldKey);
                            _selectedIndex = Array.IndexOf(refreshed.ToArray(), newName);
                        }
                    }
                    if (!canRename) ImGui.EndDisabled();

                    ImGui.SameLine();

                    // DELETE (clears input box afterwards so it shows the hint).
                    if (!haveSelection) ImGui.BeginDisabled();
                    if (ImGui.Button("Delete", new System.Numerics.Vector2(w, 0)))
                    {
                        var delName = names[_selectedIndex];
                        HomesExtensions.RemoveHome(worldKey, delName);

                        _nameBuffer = string.Empty; // Clear the "##home_name" box.

                        var updated = HomesExtensions.GetHomeNames(worldKey);
                        _selectedIndex = updated.Count == 0 ? -1 : Math.Min(_selectedIndex, updated.Count - 1);
                    }
                    if (!haveSelection) ImGui.EndDisabled();
                }
                #endregion

                ImGui.EndGroup();
            }
            finally
            {
                ImGui.PopID();
            }
        }
        #endregion

        #region Helpers

        /// <summary>Generates "Home 1/2/3..." skipping existing names in the current world.</summary>
        private static string NextAutoName(string worldKey)
        {
            var existing = HomesExtensions.GetHomeNames(worldKey); // Always fresh.
            for (int n = 1; ; n++)
            {
                string cand = $"Home {n}";
                if (!existing.Any(s => s.Equals(cand, StringComparison.OrdinalIgnoreCase)))
                    return cand;
            }
        }
        #endregion
    }
}