/*
SPDX-License-Identifier: GPL-3.0-or-later
Copyright (c) 2025 RussDev7, unknowghost0
This file is part of https://github.com/RussDev7/CMZDedicatedServers - see LICENSE for details.
*/

using System.Collections.Generic;
using System.Net;
using System.IO;
using System;

namespace CMZDedicatedSteamServer.Config
{
    /// <summary>
    /// Steam-dedicated server configuration for the headless Steam host account path.
    ///
    /// Purpose:
    /// - Preserves the useful settings from the current dedicated host.
    /// - Adds Steam-specific knobs used by the new transport.
    /// - Supplies safe defaults when server.properties is missing or incomplete.
    ///
    /// Notes:
    /// - This configuration intentionally does NOT expose Steam username/password login.
    ///   The normal Steam client API path requires an already-running Steam client session.
    /// </summary>
    internal sealed class SteamServerConfig
    {
        #region Logging

        public bool LogNetworkPackets { get; set; } = false;
        public bool LogHostMessages { get; set; } = false;

        #endregion

        #region Core Identity

        public string ServerName { get; private set; } = "CMZ Steam Server";
        public string ServerMessage { get; private set; } = "Welcome to this CastleForge dedicated server.";
        public string GameName { get; private set; } = "CastleMinerZSteam";
        public int NetworkVersion { get; private set; } = 4;
        public string GamePath { get; private set; } = string.Empty;
        public string Password { get; private set; } = string.Empty;

        #endregion

        #region Session / World

        public IPAddress BindAddress { get; private set; } = IPAddress.Any;
        public int Port { get; private set; } = 61903;
        public int MaxPlayers { get; private set; } = 8;
        public ulong SaveOwnerSteamId { get; private set; } = 0UL;
        public string WorldGuid { get; private set; } = string.Empty;
        public string WorldFolder { get; private set; } = string.Empty;
        public string WorldPath { get; private set; } = string.Empty;
        public int ViewDistanceChunks { get; private set; } = 8;
        public int TickRateHz { get; private set; } = 60;
        public int GameMode { get; private set; } = 1;
        public int PvpState { get; private set; } = 0;
        public int Difficulty { get; private set; } = 1;
        public bool InfiniteResourceMode { get; private set; } = false;
        public bool AllowClientTimeSync { get; private set; } = false;

        #endregion

        #region Steam Settings

        public uint SteamAppId { get; private set; } = 253430;
        public bool SteamLobbyVisible { get; private set; } = true;
        public bool SteamAllowMinimalUpdates { get; private set; } = false;
        public bool SteamAccountRequired { get; private set; } = true;
        public bool SteamFriendsOnly { get; private set; } = false;
        public bool WriteSteamAppIdFile { get; private set; } = true;
        public bool RequireRunningSteamClient { get; private set; } = true;

        #endregion

        #region Load

        public static SteamServerConfig Load(string serverDir)
        {
            var cfg = new SteamServerConfig();
            string path = Path.Combine(serverDir, "server.properties");
            if (!File.Exists(path))
                return cfg;

            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (string raw in File.ReadAllLines(path))
            {
                string line = (raw ?? string.Empty).Trim();
                if (line.Length == 0 || line.StartsWith("#"))
                    continue;

                int eq = line.IndexOf('=');
                if (eq <= 0)
                    continue;

                map[line.Substring(0, eq).Trim()] = line.Substring(eq + 1).Trim();
            }

            if (map.TryGetValue("log-network-packets", out string logNetworkPackets) && bool.TryParse(logNetworkPackets, out bool logNetwork))
                cfg.LogNetworkPackets = logNetwork;

            if (map.TryGetValue("log-host-messages", out string logHostMessages) && bool.TryParse(logHostMessages, out bool logHost))
                cfg.LogHostMessages = logHost;

            if (map.TryGetValue("server-name", out string serverName) && !string.IsNullOrWhiteSpace(serverName))
                cfg.ServerName = serverName;

            if (map.TryGetValue("server-message", out var serverMessage) && !string.IsNullOrWhiteSpace(serverMessage))
                cfg.ServerMessage = serverMessage;

            if (map.TryGetValue("game-name", out string gameName) && !string.IsNullOrWhiteSpace(gameName))
                cfg.GameName = gameName;

            if (map.TryGetValue("network-version", out string networkVersionRaw) && int.TryParse(networkVersionRaw, out int networkVersion) && networkVersion > 0)
                cfg.NetworkVersion = networkVersion;

            if (map.TryGetValue("game-path", out string gamePath) && !string.IsNullOrWhiteSpace(gamePath))
                cfg.GamePath = gamePath;

            if (map.TryGetValue("password", out string password))
                cfg.Password = password ?? string.Empty;

            if (map.TryGetValue("server-port", out string portRaw) && int.TryParse(portRaw, out int port) && port > 0 && port < 65536)
                cfg.Port = port;

            if (map.TryGetValue("max-players", out string maxPlayersRaw) && int.TryParse(maxPlayersRaw, out int maxPlayers) && maxPlayers > 0)
                cfg.MaxPlayers = maxPlayers;

            if (map.TryGetValue("server-ip", out string ipRaw) && !string.IsNullOrWhiteSpace(ipRaw))
            {
                if (ipRaw.Equals("0.0.0.0", StringComparison.OrdinalIgnoreCase) || ipRaw.Equals("any", StringComparison.OrdinalIgnoreCase))
                {
                    cfg.BindAddress = IPAddress.Any;
                }
                else if (IPAddress.TryParse(ipRaw, out IPAddress ip))
                {
                    cfg.BindAddress = ip;
                }
            }

            if (map.TryGetValue("save-owner-steam-id", out string ownerRaw) && ulong.TryParse(ownerRaw, out ulong owner))
                cfg.SaveOwnerSteamId = owner;

            if (map.TryGetValue("world-guid", out string worldGuid) && !string.IsNullOrWhiteSpace(worldGuid))
                cfg.WorldGuid = worldGuid;

            if (map.TryGetValue("view-distance-chunks", out string viewDistanceRaw) && int.TryParse(viewDistanceRaw, out int viewDistance) && viewDistance > 0)
                cfg.ViewDistanceChunks = viewDistance;

            if (map.TryGetValue("tick-rate-hz", out string tickRateRaw) && int.TryParse(tickRateRaw, out int tickRate) && tickRate > 0)
                cfg.TickRateHz = tickRate;

            if (map.TryGetValue("game-mode", out string gameModeRaw) && int.TryParse(gameModeRaw, out int gameMode))
                cfg.GameMode = gameMode;

            if (map.TryGetValue("pvp-state", out string pvpRaw) && int.TryParse(pvpRaw, out int pvp))
                cfg.PvpState = pvp;

            if (map.TryGetValue("difficulty", out string difficultyRaw) && int.TryParse(difficultyRaw, out int difficulty))
                cfg.Difficulty = difficulty;

            if (map.TryGetValue("infinite-resource-mode", out string infRaw) && bool.TryParse(infRaw, out bool inf))
                cfg.InfiniteResourceMode = inf;

            if (map.TryGetValue("allow-client-time-sync", out string allowTimeRaw) && bool.TryParse(allowTimeRaw, out bool allowTime))
                cfg.AllowClientTimeSync = allowTime;

            if (map.TryGetValue("steam-app-id", out string steamAppIdRaw) && uint.TryParse(steamAppIdRaw, out uint appId))
                cfg.SteamAppId = appId;

            if (map.TryGetValue("steam-lobby-visible", out string lobbyVisibleRaw) && bool.TryParse(lobbyVisibleRaw, out bool lobbyVisible))
                cfg.SteamLobbyVisible = lobbyVisible;

            if (map.TryGetValue("steam-allow-minimal-updates", out string minimalUpdatesRaw) && bool.TryParse(minimalUpdatesRaw, out bool minimalUpdates))
                cfg.SteamAllowMinimalUpdates = minimalUpdates;

            if (map.TryGetValue("steam-account-required", out string accountRequiredRaw) && bool.TryParse(accountRequiredRaw, out bool accountRequired))
                cfg.SteamAccountRequired = accountRequired;

            if (map.TryGetValue("steam-friends-only", out string friendsOnlyRaw) && bool.TryParse(friendsOnlyRaw, out bool friendsOnly))
                cfg.SteamFriendsOnly = friendsOnly;

            if (map.TryGetValue("write-steam-appid-file", out string writeAppIdRaw) && bool.TryParse(writeAppIdRaw, out bool writeAppId))
                cfg.WriteSteamAppIdFile = writeAppId;

            if (map.TryGetValue("require-running-steam-client", out string requireSteamRaw) && bool.TryParse(requireSteamRaw, out bool requireSteam))
                cfg.RequireRunningSteamClient = requireSteam;

            if (!string.IsNullOrWhiteSpace(cfg.WorldGuid))
            {
                cfg.WorldFolder = Path.Combine("Worlds", cfg.WorldGuid);
                cfg.WorldPath = Path.Combine(serverDir, cfg.WorldFolder);
            }

            return cfg;
        }

        #endregion
    }
}
