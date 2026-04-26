/*
SPDX-License-Identifier: GPL-3.0-or-later
Copyright (c) 2025 RussDev7
This file is part of https://github.com/RussDev7/CastleForge - see LICENSE for details.
*/

using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System;

namespace CMZDedicatedSteamServer.Plugins.Announcements
{
    /// <summary>
    /// Dedicated-server announcement plugin.
    /// Sends a private join message and periodic global server messages.
    /// </summary>
    internal sealed class ServerAnnouncementsPlugin :
        IServerWorldPlugin,
        IServerPlayerEventPlugin,
        IServerTickPlugin
    {
        #region Fields

        private Action<string> _log = _ => { };

        private readonly AnnouncementConfig _config = new();

        private string _pluginDir;

        private string _configPath;

        private DateTime _nextGlobalMessageUtc = DateTime.MaxValue;

        #endregion

        #region Properties

        public string Name => "Announcements";

        #endregion

        #region Plugin Lifecycle

        /// <summary>
        /// Initializes the announcement plugin, creates the config file if needed, and schedules the first global message.
        /// </summary>
        public void Initialize(ServerPluginContext context)
        {
            _log = context?.Log ?? (_ => { });

            string serverDir = context?.BaseDir;
            if (string.IsNullOrWhiteSpace(serverDir))
                serverDir = AppDomain.CurrentDomain.BaseDirectory;

            serverDir = Path.GetFullPath(serverDir);

            _pluginDir = Path.Combine(serverDir, "Plugins", "Announcements");
            Directory.CreateDirectory(_pluginDir);

            _configPath = Path.Combine(_pluginDir, "Announcements.Config.ini");

            EnsureDefaultConfig();
            LoadConfig();

            if (_config.Enabled && _config.TimedGlobalMessageEnabled)
                _nextGlobalMessageUtc = DateTime.UtcNow.AddSeconds(_config.InitialGlobalDelaySeconds);
            else
                _nextGlobalMessageUtc = DateTime.MaxValue;

            _log($"[Announcements] Config: {_configPath}.");
            _log(
                $"[Announcements] Enabled={_config.Enabled}, " +
                $"PrivateJoin={_config.PrivateJoinMessageEnabled}, " +
                $"Global={_config.TimedGlobalMessageEnabled}, " +
                $"IntervalMinutes={_config.GlobalMessageIntervalMinutes}.");
        }

        /// <summary>
        /// Announcements does not consume world packets.
        /// </summary>
        public bool BeforeHostMessage(HostMessageContext context)
        {
            return false;
        }
        #endregion

        #region Player Events

        /// <summary>
        /// Sends the configured private join message to the joining player.
        /// </summary>
        public void OnPlayerJoined(ServerPlayerEventContext context)
        {
            if (context == null || !_config.Enabled || !_config.PrivateJoinMessageEnabled)
                return;

            if (context.SendPrivateMessage == null)
                return;

            string message = ApplyTokens(_config.PrivateJoinMessage, context.PlayerName, context.ConnectedPlayers, context.MaxPlayers);

            if (string.IsNullOrWhiteSpace(message))
                return;

            context.SendPrivateMessage(message);
        }

        /// <summary>
        /// Reserved for future leave announcements.
        /// </summary>
        public void OnPlayerLeft(ServerPlayerEventContext context)
        {
        }
        #endregion

        #region Tick Events

        /// <summary>
        /// Sends the timed global announcement when its interval elapses.
        /// </summary>
        public void Update(ServerPluginTickContext context)
        {
            if (context == null || !_config.Enabled || !_config.TimedGlobalMessageEnabled)
                return;

            if (context.BroadcastMessage == null)
                return;

            if (_config.MinimumPlayersForGlobalMessage > 0 &&
                context.ConnectedPlayers < _config.MinimumPlayersForGlobalMessage)
            {
                return;
            }

            DateTime now = context.UtcNow == default ? DateTime.UtcNow : context.UtcNow;

            if (now < _nextGlobalMessageUtc)
                return;

            string message = ApplyTokens(_config.GlobalMessage, null, context.ConnectedPlayers, context.MaxPlayers);

            if (!string.IsNullOrWhiteSpace(message))
                context.BroadcastMessage(message);

            _nextGlobalMessageUtc = now.AddMinutes(_config.GlobalMessageIntervalMinutes);
        }
        #endregion

        #region Config

        /// <summary>
        /// Writes the default announcement config if it does not already exist.
        /// </summary>
        private void EnsureDefaultConfig()
        {
            if (File.Exists(_configPath))
                return;

            string text =
@"[General]
Enabled = true

[Join]
PrivateJoinMessageEnabled = true
PrivateJoinMessage = Welcome {player}! This is a CastleForge dedicated server.

[Global]
TimedGlobalMessageEnabled = true
GlobalMessage = Need help or updates? Check this server's community links.
InitialGlobalDelaySeconds = 120
GlobalMessageIntervalMinutes = 15
MinimumPlayersForGlobalMessage = 1
";

            File.WriteAllText(_configPath, text);
        }

        /// <summary>
        /// Loads announcement settings from Announcements.Config.ini.
        /// </summary>
        private void LoadConfig()
        {
            Dictionary<string, string> values = ReadIni(_configPath);

            _config.Enabled = GetBool(values, "General.Enabled", true);

            _config.PrivateJoinMessageEnabled = GetBool(values, "Join.PrivateJoinMessageEnabled", true);
            _config.PrivateJoinMessage = GetString(
                values,
                "Join.PrivateJoinMessage",
                "Welcome {player}! Join us: dsc.gg/cforge");

            _config.TimedGlobalMessageEnabled = GetBool(values, "Global.TimedGlobalMessageEnabled", true);
            _config.GlobalMessage = GetString(
                values,
                "Global.GlobalMessage",
                "Join the CastleForge Discord: dsc.gg/cforge");

            _config.InitialGlobalDelaySeconds = Clamp(
                GetInt(values, "Global.InitialGlobalDelaySeconds", 120),
                0,
                86400);

            _config.GlobalMessageIntervalMinutes = Clamp(
                GetInt(values, "Global.GlobalMessageIntervalMinutes", 15),
                1,
                1440);

            _config.MinimumPlayersForGlobalMessage = Clamp(
                GetInt(values, "Global.MinimumPlayersForGlobalMessage", 1),
                0,
                255);
        }

        /// <summary>
        /// Reads a small INI file into section.key values.
        /// </summary>
        private static Dictionary<string, string> ReadIni(string path)
        {
            var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            if (!File.Exists(path))
                return values;

            string section = string.Empty;

            foreach (string rawLine in File.ReadAllLines(path))
            {
                string line = rawLine.Trim();

                if (line.Length == 0 || line.StartsWith(";") || line.StartsWith("#"))
                    continue;

                if (line.StartsWith("[") && line.EndsWith("]"))
                {
                    section = line.Substring(1, line.Length - 2).Trim();
                    continue;
                }

                int equalsIndex = line.IndexOf('=');
                if (equalsIndex <= 0)
                    continue;

                string key = line.Substring(0, equalsIndex).Trim();
                string value = line.Substring(equalsIndex + 1).Trim();

                values[section + "." + key] = value;
            }

            return values;
        }

        private static string GetString(Dictionary<string, string> values, string key, string fallback)
        {
            return values.TryGetValue(key, out string value) && value != null
                ? value
                : fallback;
        }

        private static bool GetBool(Dictionary<string, string> values, string key, bool fallback)
        {
            if (!values.TryGetValue(key, out string value))
                return fallback;

            if (bool.TryParse(value, out bool parsed))
                return parsed;

            if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int intValue))
                return intValue != 0;

            return fallback;
        }

        private static int GetInt(Dictionary<string, string> values, string key, int fallback)
        {
            if (!values.TryGetValue(key, out string value))
                return fallback;

            return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed)
                ? parsed
                : fallback;
        }

        private static int Clamp(int value, int min, int max)
        {
            if (value < min)
                return min;

            if (value > max)
                return max;

            return value;
        }
        #endregion

        #region Tokens

        /// <summary>
        /// Applies simple message tokens.
        /// </summary>
        private static string ApplyTokens(string message, string playerName, int connectedPlayers, int maxPlayers)
        {
            if (string.IsNullOrEmpty(message))
                return string.Empty;

            string now = DateTime.Now.ToString("h:mm tt", CultureInfo.InvariantCulture);
            string date = DateTime.Now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

            return message
                .Replace("{player}", string.IsNullOrWhiteSpace(playerName) ? "Player" : playerName)
                .Replace("{players}", connectedPlayers.ToString(CultureInfo.InvariantCulture))
                .Replace("{maxplayers}", maxPlayers.ToString(CultureInfo.InvariantCulture))
                .Replace("{time}", now)
                .Replace("{date}", date);
        }
        #endregion

        #region Nested Types

        private sealed class AnnouncementConfig
        {
            public bool Enabled = true;

            public bool PrivateJoinMessageEnabled = true;

            public string PrivateJoinMessage = "Welcome {player}! Join us: dsc.gg/cforge";

            public bool TimedGlobalMessageEnabled = true;

            public string GlobalMessage = "Join the CastleForge Discord: dsc.gg/cforge";

            public int InitialGlobalDelaySeconds = 120;

            public int GlobalMessageIntervalMinutes = 15;

            public int MinimumPlayersForGlobalMessage = 1;
        }
        #endregion
    }
}