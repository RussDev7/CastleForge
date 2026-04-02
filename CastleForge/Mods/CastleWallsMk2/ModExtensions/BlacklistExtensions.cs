/*
SPDX-License-Identifier: GPL-3.0-or-later
Copyright (c) 2025 RussDev7
This file is part of https://github.com/RussDev7/CastleForge - see LICENSE for details.
*/

using System.Collections.Generic;
using Microsoft.Xna.Framework;
using DNA.Net.GamerServices;
using DNA.CastleMinerZ;
using System.Linq;
using System.IO;
using ImGuiNET;
using System;

namespace CastleWallsMk2
{
    /// <summary>
    /// Persistence + CRUD for a global (non-world) player blacklist.
    ///
    /// Storage:
    ///   Path: !Mods\{resolver-ns}\{resolver-ns}.Blacklist.ini
    ///   Format:
    ///     [Settings]
    ///     UseCrash = true/false
    ///
    ///     [Players]
    ///     SomeName =
    ///     AnotherName =
    ///
    /// Notes:
    ///   - Names are stored case-insensitively (OrdinalIgnoreCase).
    ///   - IO/parsing failures are swallowed; file can be rewritten cleanly on Save().
    /// </summary>
    internal static class BlacklistExtensions
    {
        #region Path / Storage

        /// <summary>
        /// Full path to the blacklist INI file (global storage under !Mods).
        /// </summary>
        public static string BlacklistPath =>
            Path.Combine(
                AppDomain.CurrentDomain.BaseDirectory,
                "!Mods",
                typeof(EmbeddedResolver).Namespace,
                $"{typeof(EmbeddedResolver).Namespace}.Blacklist.ini"
            );

        /// <summary>
        /// In-memory set of blacklisted names (case-insensitive).
        /// </summary>
        private static readonly HashSet<string> _players = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// One-time load guard (prevents re-reading INI repeatedly).
        /// </summary>
        private static bool _loaded;

        /// <summary>
        /// Behavior toggle stored in [Settings] (UseCrash).
        /// </summary>
        private static bool _useCrash;

        #endregion

        #region Load/Save

        /// <summary>
        /// Loads the blacklist INI into memory (once).
        /// - Creates a default template file if missing.
        /// - Swallows IO/parse errors and falls back to empty list.
        /// </summary>
        public static void EnsureLoaded()
        {
            if (_loaded) return;
            _loaded = true;

            try
            {
                var dir = Path.GetDirectoryName(BlacklistPath);
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

                if (!File.Exists(BlacklistPath))
                {
                    File.WriteAllLines(BlacklistPath, new[]
                    {
                        "; CastleWallsMk2 - Player Blacklist",
                        "; Client-side enforcement list (Kick/Crash) for gamertags.",
                        "; Do not hand-edit while the game is running.",
                        "",
                        "[Settings]",
                        "UseCrash = false",
                        "",
                        "[Players]",
                        "; Example:",
                        "; SomePlayer =",
                        ""
                    });
                    return;
                }

                ParseIni(File.ReadAllLines(BlacklistPath));
            }
            catch
            {
                // Safe fallback: Empty list.
            }
        }

        /// <summary>
        /// Writes current in-memory state back to INI (best-effort).
        /// - Rewrites the file cleanly (canonical output).
        /// - Swallows IO errors.
        /// </summary>
        public static void Save()
        {
            try
            {
                var dir = Path.GetDirectoryName(BlacklistPath);
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

                using (var sw = new StreamWriter(BlacklistPath, false))
                {
                    sw.WriteLine("; CastleWallsMk2 - Player Blacklist");
                    sw.WriteLine($"; LastSaved = {DateTime.Now:G}");
                    sw.WriteLine();

                    sw.WriteLine("[Settings]");
                    sw.WriteLine($"UseCrash = {(_useCrash ? "true" : "false")}");
                    sw.WriteLine();

                    sw.WriteLine("[Players]");
                    foreach (var name in _players.OrderBy(s => s, StringComparer.OrdinalIgnoreCase))
                        sw.WriteLine($"{name} =");
                    sw.WriteLine();
                }
            }
            catch
            {
                // Swallow.
            }
        }
        #endregion

        #region INI Parsing Helpers

        /// <summary>
        /// INI reader (minimal parser):
        /// - Tracks [section]
        /// - Supports key=value (value optional)
        /// - Treats non-empty keys under [Players] (or no section) as entries
        /// </summary>
        private static void ParseIni(string[] lines)
        {
            _players.Clear();
            _useCrash = false;

            string section = null;

            foreach (var raw in lines ?? Array.Empty<string>())
            {
                var line = (raw ?? string.Empty).Trim();
                if (line.Length == 0) continue;
                if (line.StartsWith(";") || line.StartsWith("#")) continue;

                if (line.StartsWith("[") && line.EndsWith("]"))
                {
                    section = line.Substring(1, line.Length - 2).Trim();
                    continue;
                }

                string key = line;
                string val = "";

                int eq = line.IndexOf('=');
                if (eq >= 0)
                {
                    key = line.Substring(0, eq).Trim();
                    val = line.Substring(eq + 1).Trim();
                }

                if (string.IsNullOrWhiteSpace(key))
                    continue;

                if (section != null && section.Equals("Settings", StringComparison.OrdinalIgnoreCase))
                {
                    if (key.Equals("UseCrash", StringComparison.OrdinalIgnoreCase))
                    {
                        if (bool.TryParse(val, out bool parsed))
                            _useCrash = parsed;
                        else
                            _useCrash = (val == "1" || val.Equals("yes", StringComparison.OrdinalIgnoreCase));
                    }
                    continue;
                }

                // Default: Treat lines under [Players] as names.
                if (section == null || section.Equals("Players", StringComparison.OrdinalIgnoreCase))
                {
                    var name = NormalizeName(key);
                    if (!string.IsNullOrEmpty(name))
                        _players.Add(name);
                }
            }
        }

        /// <summary>
        /// Normalizes a gamertag string for storage/lookup.
        /// - Trims whitespace
        /// - Converts empty strings to null
        /// </summary>
        private static string NormalizeName(string s)
        {
            s = (s ?? string.Empty).Trim();
            return s.Length == 0 ? null : s;
        }
        #endregion

        #region Public API

        /// <summary>
        /// Gets/sets the behavior toggle stored in [Settings].
        /// - Always EnsureLoaded() before access.
        /// - Persists on change.
        /// </summary>
        public static bool UseCrash
        {
            get { EnsureLoaded(); return _useCrash; }
            set
            {
                EnsureLoaded();
                if (_useCrash == value) return;
                _useCrash = value;
                Save();
            }
        }

        /// <summary>
        /// Returns all saved names sorted (case-insensitive).
        /// </summary>
        public static IReadOnlyList<string> GetNames()
        {
            EnsureLoaded();
            return _players.OrderBy(s => s, StringComparer.OrdinalIgnoreCase).ToArray();
        }

        /// <summary>
        /// True if the given name is present in the blacklist.
        /// </summary>
        public static bool Contains(string name)
        {
            EnsureLoaded();
            name = NormalizeName(name);
            if (string.IsNullOrEmpty(name)) return false;
            return _players.Contains(name);
        }

        /// <summary>
        /// Adds a name to the blacklist and saves if it was new.
        /// </summary>
        public static void Add(string name)
        {
            EnsureLoaded();
            name = NormalizeName(name);
            if (string.IsNullOrEmpty(name)) return;

            if (_players.Add(name))
                Save();
        }

        /// <summary>
        /// Removes a name from the blacklist and saves if it existed.
        /// </summary>
        public static void Remove(string name)
        {
            EnsureLoaded();
            name = NormalizeName(name);
            if (string.IsNullOrEmpty(name)) return;

            if (_players.Remove(name))
                Save();
        }

        /// <summary>
        /// Renames an entry in the blacklist.
        /// - Supports overwrite for collisions.
        /// - Handles case-only renames correctly with a case-insensitive set.
        /// </summary>
        public static bool TryRename(string oldName, string newName, out string error, bool overwrite = false)
        {
            error = null;
            EnsureLoaded();

            oldName = NormalizeName(oldName);
            newName = NormalizeName(newName);

            if (string.IsNullOrEmpty(oldName)) { error = "No source name.";         return false; }
            if (string.IsNullOrEmpty(newName)) { error = "New name is empty.";      return false; }
            if (!_players.Contains(oldName))   { error = $"'{oldName}' not found."; return false; }

            if (string.Equals(oldName, newName, StringComparison.Ordinal))
                return true;

            bool caseOnly = string.Equals(oldName, newName, StringComparison.OrdinalIgnoreCase);

            if (!caseOnly && _players.Contains(newName) && !overwrite)
            {
                error = $"'{newName}' already exists.";
                return false;
            }

            // Case-insensitive set: Case-only rename must remove first.
            _players.Remove(oldName);
            _players.Add(newName);

            Save();
            return true;
        }
        #endregion
    }

    /// <summary>
    /// Auto-enforces the blacklist (client-side) on a small interval.
    /// - If a non-local player's gamertag is in the blacklist -> Kick/Crash them.
    /// - Rate-limited and "handled id" cached so we don't spam.
    /// </summary>
    internal static class BlacklistEnforcer
    {
        #region State

        private static NetworkSession         _lastSession;
        private static readonly HashSet<byte> _handledIds = new HashSet<byte>();
        private static double                 _nextScanAt;

        #endregion

        #region Public Loop Hook

        /// <summary>
        /// Periodic enforcement tick.
        /// - Resets state on session changes.
        /// - Scans on a fixed cadence (0.5s).
        /// - Applies the configured enforcement action (Kick/Crash).
        /// </summary>
        public static void Tick(GameTime gameTime)
        {
            var g = CastleMinerZGame.Instance;
            var s = g?.CurrentNetworkSession;

            if (s == null)
            {
                _lastSession = null;
                _handledIds.Clear();
                _nextScanAt  = 0;
                return;
            }

            if (!ReferenceEquals(s, _lastSession))
            {
                _lastSession = s;
                _handledIds.Clear();
                _nextScanAt  = 0;
            }

            // Host-only moderation.
            // if (!g.IsGameHost) return;

            // Rate-limit the scan.
            double now = gameTime.TotalGameTime.TotalSeconds;
            if (now < _nextScanAt) return;
            _nextScanAt = now + 0.50; // Twice per second.

            // Nothing to do.
            var list = BlacklistExtensions.GetNames();
            if (list.Count == 0) return;

            var from = g.MyNetworkGamer;
            if (from == null) return;

            foreach (NetworkGamer ng in s.AllGamers)
            {
                if (ng == null) continue;
                if (ng.IsLocal) continue;

                if (_handledIds.Contains(ng.Id))
                    continue;

                string tag = (ng.Gamertag ?? "").Trim();
                if (!BlacklistExtensions.Contains(tag))
                    continue;

                _handledIds.Add(ng.Id);

                try
                {
                    if (BlacklistExtensions.UseCrash)
                        CastleWallsMk2.CrashSelectedPlayer(ng);
                    else
                        CastleWallsMk2.KickPlayerPrivate(from, ng);
                }
                catch { /* Swallow. */ }
            }
        }

        #endregion
    }

    /// <summary>
    /// ImGui widget matching your Homes "Save Positions" flow:
    /// - Combo:    "Online Players" + "Blacklisted Entries" (auto-populated from INI).
    /// - Text box: Add/rename target.
    /// - Buttons:  Save | Rename, Delete | UseCrash (checkbox).
    /// </summary>
    internal static class BlacklistUI
    {
        #region Fields / UI State

        private static int            _selectedIndex = -1; // Selected entry in saved blacklist list.
        private static string         _nameBuffer    = "";
        private static NetworkSession _lastSession;

        #endregion

        #region UI Entry Point

        /// <summary>
        /// Draws the full "Player Blacklist" widget.
        /// - Uses a PushID scope so repeated labels (Save/Rename/Delete) don't collide with other panels.
        /// - Resets selection/input on session transitions.
        ///
        /// NOTE (ImGui IDs):
        /// - Online list items are already given unique IDs via "##online_{ng.Id}".
        /// - If a gamertag appears both online AND in the saved list, the saved list Selectable()
        ///   needs its own unique ID as well to avoid ID conflicts inside the same combo popup.
        /// </summary>
        public static void DrawBlacklistWidget()
        {
            ImGui.PushID("CWMK2_PlayerBlacklist");
            try
            {
                BlacklistExtensions.EnsureLoaded();

                var game = CastleMinerZGame.Instance;
                var sess = game?.CurrentNetworkSession;

                // Session switch -> reset selection/input so we don't leak state.
                if (!ReferenceEquals(sess, _lastSession))
                {
                    _lastSession   = sess;
                    _selectedIndex = -1;
                    _nameBuffer    = "";
                }

                var saved = BlacklistExtensions.GetNames();
                string currentLabel = (_selectedIndex >= 0 && _selectedIndex < saved.Count)
                    ? saved[_selectedIndex]
                    : "<none>";

                ImGui.AlignTextToFramePadding();
                IGMainUI.CenterText("Player Blacklist:");
                ImGui.BeginGroup();

                #region Top Row: Combo + Name Input

                ImGui.SetNextItemWidth(-1);
                if (ImGui.BeginCombo("##bl_combo", currentLabel))
                {
                    // --- Online players (quick-pick) ---
                    ImGui.TextDisabled("Online Players");
                    ImGui.Separator();

                    if (sess != null)
                    {
                        foreach (NetworkGamer ng in sess.AllGamers)
                        {
                            if (ng == null) continue;
                            if (ng == game.MyNetworkGamer) continue;
                            string label = ng.IsLocal ? $"{ng.Gamertag} (You)" : ng.Gamertag;

                            // Selecting an online player just seeds the textbox.
                            // Unique ID: online + gamer id.
                            if (ImGui.Selectable($"{label}##online_{ng.Id}", false))
                                _nameBuffer = (ng.Gamertag ?? "").Trim();
                        }
                    }
                    else
                    {
                        ImGui.TextDisabled("(no session)");
                    }

                    ImGui.Spacing();
                    ImGui.TextDisabled("Blacklisted Entries");
                    ImGui.Separator();

                    for (int i = 0; i < saved.Count; i++)
                    {
                        bool sel = (i == _selectedIndex);
                        if (ImGui.Selectable(saved[i], sel))
                        {
                            _selectedIndex = i;
                            _nameBuffer = saved[i];
                        }
                        if (sel) ImGui.SetItemDefaultFocus();
                    }

                    ImGui.EndCombo();
                }

                ImGui.SetNextItemWidth(-1);
                ImGui.InputTextWithHint("##bl_name", "Gamertag to blacklist.", ref _nameBuffer, 64);

                #endregion

                #region Row 1: Save | Rename (50/50)

                {
                    float avail = ImGui.GetContentRegionAvail().X;
                    float gap   = ImGui.GetStyle().ItemSpacing.X;
                    float w     = (avail - gap * 1f) / 2f;

                    bool haveSelection = (_selectedIndex >= 0 && _selectedIndex < saved.Count);
                    bool haveName      = !string.IsNullOrWhiteSpace(_nameBuffer);

                    // SAVE (adds typed name; clears input like Homes).
                    if (ImGui.Button("Save", new System.Numerics.Vector2(w, 0)))
                    {
                        string name = (_nameBuffer ?? "").Trim();
                        if (!string.IsNullOrEmpty(name))
                        {
                            BlacklistExtensions.Add(name);

                            // Refresh + select the saved name.
                            var refreshed  = BlacklistExtensions.GetNames();
                            _selectedIndex = Array.IndexOf(refreshed.ToArray(), name);
                            _nameBuffer    = string.Empty;
                        }
                    }

                    ImGui.SameLine();

                    // RENAME (requires selecting an existing entry).
                    bool canRename = haveSelection && haveName;
                    if (!canRename) ImGui.BeginDisabled();
                    if (ImGui.Button("Rename", new System.Numerics.Vector2(w, 0)))
                    {
                        var oldName = saved[_selectedIndex];
                        var newName = _nameBuffer.Trim();

                        if (!string.Equals(oldName, newName, StringComparison.Ordinal))
                        {
                            if (!BlacklistExtensions.TryRename(oldName, newName, out var err, overwrite: false))
                            {
                                // Allow overwrite on collision.
                                if (err != null && err.IndexOf("already exists", StringComparison.OrdinalIgnoreCase) >= 0)
                                    BlacklistExtensions.TryRename(oldName, newName, out _, overwrite: true);
                            }

                            var refreshed  = BlacklistExtensions.GetNames();
                            _selectedIndex = Array.IndexOf(refreshed.ToArray(), newName);
                        }
                    }
                    if (!canRename) ImGui.EndDisabled();
                }
                #endregion

                #region Row 2: Delete | UseCrash (50/50)

                {
                    float avail = ImGui.GetContentRegionAvail().X;
                    float gap   = ImGui.GetStyle().ItemSpacing.X;
                    float w     = (avail - gap * 1f) / 2f;

                    bool haveSelection = (_selectedIndex >= 0 && _selectedIndex < saved.Count);

                    // DELETE.
                    if (!haveSelection) ImGui.BeginDisabled();
                    if (ImGui.Button("Delete", new System.Numerics.Vector2(w, 0)))
                    {
                        var delName    = saved[_selectedIndex];
                        BlacklistExtensions.Remove(delName);

                        _nameBuffer    = string.Empty;

                        var updated    = BlacklistExtensions.GetNames();
                        _selectedIndex = updated.Count == 0 ? -1 : Math.Min(_selectedIndex, updated.Count - 1);
                    }
                    if (!haveSelection) ImGui.EndDisabled();

                    ImGui.SameLine();

                    // "4th slot" checkbox (safe alternative to "UseCrash"): Crash instead of Kick.
                    bool useCrash = BlacklistExtensions.UseCrash;
                    ImGui.BeginGroup();
                    // ImGui.SetCursorPosY(ImGui.GetCursorPosY() + 3f); // Tiny vertical align tweak.
                    ImGui.AlignTextToFramePadding();
                    if (ImGui.Checkbox("UseCrash", ref useCrash))
                        BlacklistExtensions.UseCrash = useCrash;
                    if (ImGui.IsItemHovered())
                        ImGui.SetTooltip("When enabled, blacklist enforcement crashes instead of kicks (client-side).");
                    ImGui.EndGroup();
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
    }
}