/*
SPDX-License-Identifier: GPL-3.0-or-later
Copyright (c) 2025 RussDev7, unknowghost0
This file is part of https://github.com/RussDev7/CMZDedicatedServers - see LICENSE for details.
*/

using System.Collections.Generic;
using System.Net;
using System.IO;
using System;

namespace CMZDedicatedLidgrenServer
{
    #region Server Configuration

    /// <summary>
    /// Loads and stores server-side configuration values for the dedicated host.
    ///
    /// Purpose:
    /// - Provides strongly-typed access to values read from "server.properties".
    /// - Supplies sane defaults when the config file is missing or entries are invalid.
    /// - Derives world-related paths after the world GUID is known.
    ///
    /// Notes:
    /// - This class is intentionally read-only from the outside after load.
    /// - Missing properties do not throw; defaults remain in effect instead.
    /// - Parsing is case-insensitive for keys because the backing dictionary uses
    ///   <see cref="StringComparer.OrdinalIgnoreCase"/>.
    /// </summary>
    internal sealed class ServerConfig
    {
        #region Core Server Identity

        /// <summary>
        /// Display name reported by the server.
        /// </summary>
        public string ServerName { get; private set; } = "CMZ Server";

        /// <summary>
        /// Network game name expected by compatible clients.
        /// </summary>
        public string GameName { get; private set; } = "CastleMinerZSteam";

        /// <summary>
        /// Network protocol/version number expected by compatible clients.
        /// </summary>
        public int NetworkVersion { get; private set; } = 4;

        /// <summary>
        /// Optional path to the CastleMiner Z game folder.
        ///
        /// Purpose:
        /// - Lets the server load game assemblies from a custom location.
        /// - Avoids requiring the files to be copied into a local "game" folder.
        ///
        /// Notes:
        /// - Can be absolute or relative.
        /// - If left empty, the server will fall back to ".\game".
        /// </summary>
        public string GamePath { get; private set; } = "";

        #endregion

        #region Bind / Session Limits

        /// <summary>
        /// Local IP address to bind the server socket to.
        /// Defaults to <see cref="IPAddress.Any"/>.
        /// </summary>
        public IPAddress BindAddress { get; private set; } = IPAddress.Any;

        /// <summary>
        /// UDP/TCP port used by the host.
        /// </summary>
        public int Port { get; private set; } = 61903;

        /// <summary>
        /// Maximum number of players allowed by the server.
        /// </summary>
        public int MaxPlayers { get; private set; } = 8;

        #endregion

        #region Save / World Identity

        /// <summary>
        /// Steam user id used for save-device key derivation and storage access.
        /// </summary>
        public ulong SaveOwnerSteamId { get; private set; } = 0UL;

        /// <summary>
        /// World GUID string loaded from configuration.
        /// </summary>
        public string WorldGuid { get; private set; } = "";

        /// <summary>
        /// Relative world folder path, e.g. "Worlds\{guid}".
        /// </summary>
        public string WorldFolder { get; private set; } = "";

        /// <summary>
        /// Absolute world path rooted under the supplied server directory.
        /// </summary>
        public string WorldPath { get; private set; } = "";

        #endregion

        #region Runtime / Simulation Tuning

        /// <summary>
        /// View distance used by the host for chunk-related behavior.
        /// </summary>
        public int ViewDistanceChunks { get; private set; } = 8;

        /// <summary>
        /// Main server tick/update rate in Hz.
        /// </summary>
        public int TickRateHz { get; private set; } = 60;

        #endregion

        #region Session Gameplay Properties

        /// <summary>
        /// Game mode session property value.
        /// </summary>
        public int GameMode { get; private set; } = 1;

        /// <summary>
        /// PVP session property value.
        /// </summary>
        public int PvpState { get; private set; } = 0;

        /// <summary>
        /// Difficulty session property value.
        /// </summary>
        public int Difficulty { get; private set; } = 1;

        #endregion

        #region Gameplay sync settings

        /// <summary>
        /// Allows clients to submit TimeOfDayMessage values that update the server's authoritative day value.
        ///
        /// Notes:
        /// - Disabled by default to avoid griefing / accidental time spam.
        /// - When enabled, the server may accept incoming time sync packets from clients.
        /// </summary>
        public bool AllowClientTimeSync { get; private set; } = false;

        #endregion

        #region Load

        /// <summary>
        /// Loads configuration values from "server.properties" located in the specified server directory.
        ///
        /// Purpose:
        /// - Reads plain-text key/value configuration entries.
        /// - Applies validated values onto a default-initialized configuration object.
        /// - Builds derived world folder/path values when a world GUID is present.
        ///
        /// Notes:
        /// - Invalid or missing lines are ignored instead of throwing.
        /// - Comment lines begin with '#'.
        /// - The returned object always contains defaults, even when the file is missing.
        /// </summary>
        /// <param name="serverDir">Base server directory containing "server.properties".</param>
        /// <returns>A populated <see cref="ServerConfig"/> instance.</returns>
        public static ServerConfig Load(string serverDir)
        {
            #region Initialize Defaults / Locate File

            var cfg = new ServerConfig();
            string path = Path.Combine(serverDir, "server.properties");

            // If the file does not exist, return defaults exactly as initialized.
            if (!File.Exists(path))
                return cfg;

            #endregion

            #region Read Raw Key/Value Map

            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            foreach (var raw in File.ReadAllLines(path))
            {
                string line = (raw ?? string.Empty).Trim();

                // Skip blank lines and comments.
                if (line.Length == 0 || line.StartsWith("#"))
                    continue;

                int idx = line.IndexOf('=');

                // Ignore malformed lines that do not contain "key=value".
                if (idx <= 0)
                    continue;

                string key = line.Substring(0, idx).Trim();
                string val = line.Substring(idx + 1).Trim();
                map[key] = val;
            }
            #endregion

            #region Core Server Identity

            if (map.TryGetValue("server-name", out var serverName) && !string.IsNullOrWhiteSpace(serverName))
                cfg.ServerName = serverName;

            if (map.TryGetValue("game-name", out var gameName) && !string.IsNullOrWhiteSpace(gameName))
                cfg.GameName = gameName;

            if (map.TryGetValue("network-version", out var nv) && int.TryParse(nv, out var networkVersion) && networkVersion > 0)
                cfg.NetworkVersion = networkVersion;

            #endregion

            #region Bind / Session Limits

            if (map.TryGetValue("server-port", out var portStr) && int.TryParse(portStr, out var port) && port > 0 && port < 65536)
                cfg.Port = port;

            if (map.TryGetValue("max-players", out var mp) && int.TryParse(mp, out var maxPlayers) && maxPlayers > 0)
                cfg.MaxPlayers = maxPlayers;

            if (map.TryGetValue("server-ip", out var ipStr) && !string.IsNullOrWhiteSpace(ipStr))
            {
                // Support both explicit "0.0.0.0" and friendly "any" aliases.
                if (ipStr.Equals("0.0.0.0", StringComparison.OrdinalIgnoreCase) ||
                    ipStr.Equals("any", StringComparison.OrdinalIgnoreCase))
                {
                    cfg.BindAddress = IPAddress.Any;
                }
                else if (IPAddress.TryParse(ipStr, out var ip))
                {
                    cfg.BindAddress = ip;
                }
            }
            #endregion

            #region Game path settings

            if (map.TryGetValue("game-path", out var gamePath) && !string.IsNullOrWhiteSpace(gamePath))
                cfg.GamePath = gamePath;

            #endregion

            #region Save / World Identity

            if (map.TryGetValue("save-owner-steam-id", out var suid) && ulong.TryParse(suid, out var saveOwnerSteamId))
                cfg.SaveOwnerSteamId = saveOwnerSteamId;

            if (map.TryGetValue("world-guid", out var worldGuid) && !string.IsNullOrWhiteSpace(worldGuid))
                cfg.WorldGuid = worldGuid;

            #endregion

            #region Runtime / Simulation Tuning

            if (map.TryGetValue("view-distance-chunks", out var vd) && int.TryParse(vd, out var viewDist) && viewDist > 0)
                cfg.ViewDistanceChunks = viewDist;

            if (map.TryGetValue("tick-rate-hz", out var tr) && int.TryParse(tr, out var tickRate) && tickRate > 0)
                cfg.TickRateHz = tickRate;

            #endregion

            #region Session Gameplay Properties

            if (map.TryGetValue("game-mode", out var gm) && int.TryParse(gm, out var gameMode))
                cfg.GameMode = gameMode;

            if (map.TryGetValue("pvp-state", out var pv) && int.TryParse(pv, out var pvpState))
                cfg.PvpState = pvpState;

            if (map.TryGetValue("difficulty", out var df) && int.TryParse(df, out var difficulty))
                cfg.Difficulty = difficulty;

            #endregion

            #region Gameplay sync settings

            if (map.TryGetValue("allow-client-time-sync", out var acts) && bool.TryParse(acts, out var allowClientTimeSync))
                cfg.AllowClientTimeSync = allowClientTimeSync;

            #endregion

            #region Derived Paths

            // Build relative + absolute world paths only when a GUID has been provided.
            if (!string.IsNullOrWhiteSpace(cfg.WorldGuid))
            {
                cfg.WorldFolder = Path.Combine("Worlds", cfg.WorldGuid);
                cfg.WorldPath = Path.Combine(serverDir, cfg.WorldFolder);
            }
            #endregion

            return cfg;
        }
        #endregion
    }
    #endregion
}