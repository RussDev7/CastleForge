/*
SPDX-License-Identifier: GPL-3.0-or-later
Copyright (c) 2025 RussDev7, unknowghost0
This file is part of https://github.com/RussDev7/CMZDedicatedServers - see LICENSE for details.
*/

using System.Collections.Generic;
using System.Text;
using System.IO;
using System;

namespace CMZDedicatedLidgrenServer.Commands
{
    /// <summary>
    /// Loads and saves command settings, command permission requirements, and player ranks.
    ///
    /// Files:
    /// Commands\CommandPermissions.ini
    /// Commands\CommandRanks.ini
    /// </summary>
    internal sealed class ServerCommandPermissionStore
    {
        private readonly string _commandsDir;
        private readonly string _permissionsPath;
        private readonly string _ranksPath;
        private readonly Action<string> _log;

        private readonly Dictionary<string, ServerCommandRank> _commandRanks =
            new(StringComparer.OrdinalIgnoreCase);

        private readonly Dictionary<string, ServerCommandRank> _playerRanks =
            new(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// True when in-game command handling is enabled.
        /// </summary>
        public bool Enabled { get; private set; } = true;

        /// <summary>
        /// Chat prefix used for server commands.
        /// </summary>
        public string Prefix { get; private set; } = "!";

        /// <summary>
        /// Maximum number of chat lines used by one help page.
        /// </summary>
        public int HelpMaxLines { get; private set; } = 5;

        public ServerCommandPermissionStore(string baseDir, Action<string> log)
        {
            if (string.IsNullOrWhiteSpace(baseDir))
                baseDir = AppDomain.CurrentDomain.BaseDirectory;

            _commandsDir = Path.Combine(baseDir, "Commands");
            _permissionsPath = Path.Combine(_commandsDir, "CommandPermissions.ini");
            _ranksPath = Path.Combine(_commandsDir, "CommandRanks.ini");
            _log = log ?? (_ => { });
        }

        /// <summary>
        /// Reloads both command config files from disk, creating default files if needed.
        /// </summary>
        public void Reload()
        {
            Directory.CreateDirectory(_commandsDir);

            EnsurePermissionsFileExists();
            EnsureRanksFileExists();

            LoadPermissions();
            LoadRanks();
        }

        /// <summary>
        /// Ensures newly registered commands have a permission entry.
        /// Existing user-customized values are not overwritten.
        /// </summary>
        public void EnsureCommandDefaults(IEnumerable<ServerCommandDefinition> commands)
        {
            bool changed = false;

            if (commands != null)
            {
                foreach (ServerCommandDefinition command in commands)
                {
                    if (command == null || string.IsNullOrWhiteSpace(command.Name))
                        continue;

                    string normalized = ServerCommandParser.NormalizeCommandName(command.Name);

                    if (!_commandRanks.ContainsKey(normalized))
                    {
                        _commandRanks[normalized] = command.DefaultRequiredRank;
                        changed = true;
                    }
                }
            }

            if (changed)
                SavePermissions();
        }

        /// <summary>
        /// Gets the required rank for a command, falling back to its compiled default.
        /// </summary>
        public ServerCommandRank GetRequiredRank(string commandName, ServerCommandRank fallback)
        {
            commandName = ServerCommandParser.NormalizeCommandName(commandName);

            if (_commandRanks.TryGetValue(commandName, out ServerCommandRank rank))
                return rank;

            return fallback;
        }

        /// <summary>
        /// Resolves the sender's rank using the most stable available identity.
        /// </summary>
        public ServerCommandRank GetPlayerRank(ServerCommandContext context)
        {
            if (context == null)
                return ServerCommandRank.Default;

            if (context.RankOverride.HasValue)
                return context.RankOverride.Value;

            string primaryKey = NormalizeIdentityKey(context.RemoteKey);
            if (!string.IsNullOrWhiteSpace(primaryKey) &&
                _playerRanks.TryGetValue(primaryKey, out ServerCommandRank directRank))
            {
                return directRank;
            }

            if (context.AdditionalRemoteKeys != null)
            {
                foreach (string extraKeyRaw in context.AdditionalRemoteKeys)
                {
                    string extraKey = NormalizeIdentityKey(extraKeyRaw);

                    if (!string.IsNullOrWhiteSpace(extraKey) &&
                        _playerRanks.TryGetValue(extraKey, out ServerCommandRank extraRank))
                    {
                        return extraRank;
                    }
                }
            }

            if (context.RemoteId != 0UL)
            {
                string steamKey = "steam:" + context.RemoteId;
                if (_playerRanks.TryGetValue(steamKey, out ServerCommandRank steamRank))
                    return steamRank;
            }

            if (!string.IsNullOrWhiteSpace(context.PlayerName))
            {
                string nameKey = "name:" + context.PlayerName.Trim();
                if (_playerRanks.TryGetValue(nameKey, out ServerCommandRank nameRank))
                    return nameRank;
            }

            return ServerCommandRank.Default;
        }

        /// <summary>
        /// Stores or updates one player's command rank.
        /// </summary>
        public void SetPlayerRank(string identityKey, ServerCommandRank rank)
        {
            identityKey = NormalizeIdentityKey(identityKey);

            if (string.IsNullOrWhiteSpace(identityKey))
                return;

            _playerRanks[identityKey] = rank;
            SaveRanks();
        }

        /// <summary>
        /// Removes one explicit player rank entry.
        /// </summary>
        public bool RemovePlayerRank(string identityKey)
        {
            identityKey = NormalizeIdentityKey(identityKey);

            if (string.IsNullOrWhiteSpace(identityKey))
                return false;

            bool removed = _playerRanks.Remove(identityKey);

            if (removed)
                SaveRanks();

            return removed;
        }

        /// <summary>
        /// Converts raw player input into a config identity key when possible.
        /// </summary>
        public static string BuildIdentityKeyFromRawTarget(string target)
        {
            if (string.IsNullOrWhiteSpace(target))
                return null;

            target = target.Trim();

            if (target.IndexOf(':') > 0)
                return NormalizeIdentityKey(target);

            if (ulong.TryParse(target, out ulong steamId) && steamId > 10000000000000000UL)
                return "steam:" + steamId;

            return "name:" + target;
        }

        /// <summary>
        /// Normalizes supported rank keys such as steam:, name:, and ip:.
        /// </summary>
        public static string NormalizeIdentityKey(string key)
        {
            if (string.IsNullOrWhiteSpace(key))
                return null;

            key = key.Trim();

            int colon = key.IndexOf(':');
            if (colon <= 0)
                return "name:" + key;

            string prefix = key.Substring(0, colon).Trim().ToLowerInvariant();
            string value = key.Substring(colon + 1).Trim();

            if (value.Length == 0)
                return null;

            return prefix switch
            {
                "steam" or "steamid" => "steam:" + value,
                "ip" or "addr" or "address" => "ip:" + value,
                "name" or "player" or "gamertag" => "name:" + value,
                _ => prefix + ":" + value,
            };
        }

        private void EnsurePermissionsFileExists()
        {
            if (File.Exists(_permissionsPath))
                return;

            StringBuilder sb = new();

            sb.AppendLine("# Server command settings and required ranks.");
            sb.AppendLine("# Rank order: Default < Member < Moderator < Admin");
            sb.AppendLine("# Note: \"Defualt\" is accepted as a typo-compatible alias, but this file writes \"Default\".");
            sb.AppendLine();
            sb.AppendLine("[Settings]");
            sb.AppendLine("Enabled=true");
            sb.AppendLine("Prefix=!");
            sb.AppendLine("HelpMaxLines=5");
            sb.AppendLine();
            sb.AppendLine("[Commands]");
            sb.AppendLine("help=Default");
            sb.AppendLine("players=Moderator");
            sb.AppendLine("kick=Moderator");
            sb.AppendLine("ban=Admin");
            sb.AppendLine("unban=Admin");
            sb.AppendLine("reload=Admin");
            sb.AppendLine("op=Admin");
            sb.AppendLine("deop=Admin");
            sb.AppendLine("setrank=Admin");

            File.WriteAllText(_permissionsPath, sb.ToString());
        }

        private void EnsureRanksFileExists()
        {
            if (File.Exists(_ranksPath))
                return;

            StringBuilder sb = new();

            sb.AppendLine("# Persistent command ranks for players.");
            sb.AppendLine("# Steam server should prefer SteamID keys:");
            sb.AppendLine("# steam:76561198000000000=Admin");
            sb.AppendLine();
            sb.AppendLine("# Lidgren can use names or IPs:");
            sb.AppendLine("# name:Jacob Ladders=Admin");
            sb.AppendLine("# ip:127.0.0.1=Admin");
            sb.AppendLine();
            sb.AppendLine("[Players]");

            File.WriteAllText(_ranksPath, sb.ToString());
        }

        private void LoadPermissions()
        {
            _commandRanks.Clear();

            Enabled = true;
            Prefix = "!";
            HelpMaxLines = 5;

            string section = string.Empty;

            foreach (string rawLine in File.ReadAllLines(_permissionsPath))
            {
                string line = StripComment(rawLine).Trim();

                if (line.Length == 0)
                    continue;

                if (line.StartsWith("[", StringComparison.Ordinal) &&
                    line.EndsWith("]", StringComparison.Ordinal))
                {
                    section = line.Substring(1, line.Length - 2).Trim();
                    continue;
                }

                int equals = line.IndexOf('=');
                if (equals <= 0)
                    continue;

                string key = line.Substring(0, equals).Trim();
                string value = line.Substring(equals + 1).Trim();

                if (section.Equals("Settings", StringComparison.OrdinalIgnoreCase))
                {
                    if (key.Equals("Enabled", StringComparison.OrdinalIgnoreCase))
                    {
                        Enabled = ParseBool(value, true);
                    }
                    else if (key.Equals("Prefix", StringComparison.OrdinalIgnoreCase))
                    {
                        Prefix = string.IsNullOrEmpty(value) ? "!" : value;
                    }
                    else if (key.Equals("HelpMaxLines", StringComparison.OrdinalIgnoreCase))
                    {
                        if (int.TryParse(value, out int maxLines))
                            HelpMaxLines = Math.Max(1, Math.Min(20, maxLines));
                    }
                }
                else if (section.Equals("Commands", StringComparison.OrdinalIgnoreCase))
                {
                    if (ServerCommandRankHelper.TryParse(value, out ServerCommandRank rank))
                    {
                        string normalized = ServerCommandParser.NormalizeCommandName(key);
                        if (!string.IsNullOrWhiteSpace(normalized))
                            _commandRanks[normalized] = rank;
                    }
                }
            }
        }

        private void LoadRanks()
        {
            _playerRanks.Clear();

            string section = string.Empty;

            foreach (string rawLine in File.ReadAllLines(_ranksPath))
            {
                string line = StripComment(rawLine).Trim();

                if (line.Length == 0)
                    continue;

                if (line.StartsWith("[", StringComparison.Ordinal) &&
                    line.EndsWith("]", StringComparison.Ordinal))
                {
                    section = line.Substring(1, line.Length - 2).Trim();
                    continue;
                }

                if (!section.Equals("Players", StringComparison.OrdinalIgnoreCase))
                    continue;

                int equals = line.IndexOf('=');
                if (equals <= 0)
                    continue;

                string key = NormalizeIdentityKey(line.Substring(0, equals));
                string value = line.Substring(equals + 1).Trim();

                if (string.IsNullOrWhiteSpace(key))
                    continue;

                if (ServerCommandRankHelper.TryParse(value, out ServerCommandRank rank))
                    _playerRanks[key] = rank;
            }
        }

        private void SavePermissions()
        {
            StringBuilder sb = new();

            sb.AppendLine("# Server command settings and required ranks.");
            sb.AppendLine("# Rank order: Default < Member < Moderator < Admin");
            sb.AppendLine();
            sb.AppendLine("[Settings]");
            sb.AppendLine("Enabled=" + (Enabled ? "true" : "false"));
            sb.AppendLine("Prefix=" + Prefix);
            sb.AppendLine("HelpMaxLines=" + HelpMaxLines);
            sb.AppendLine();
            sb.AppendLine("[Commands]");

            foreach (KeyValuePair<string, ServerCommandRank> kvp in _commandRanks)
                sb.AppendLine(kvp.Key + "=" + ServerCommandRankHelper.ToConfigString(kvp.Value));

            File.WriteAllText(_permissionsPath, sb.ToString());
        }

        private void SaveRanks()
        {
            StringBuilder sb = new();

            sb.AppendLine("# Persistent command ranks for players.");
            sb.AppendLine("# Examples:");
            sb.AppendLine("# steam:76561198000000000=Admin");
            sb.AppendLine("# name:Jacob Ladders=Moderator");
            sb.AppendLine("# ip:127.0.0.1=Admin");
            sb.AppendLine();
            sb.AppendLine("[Players]");

            foreach (KeyValuePair<string, ServerCommandRank> kvp in _playerRanks)
                sb.AppendLine(kvp.Key + "=" + ServerCommandRankHelper.ToConfigString(kvp.Value));

            File.WriteAllText(_ranksPath, sb.ToString());
        }

        /// <summary>
        /// Returns the saved rank for a specific identity key, such as steam:..., name:..., or ip:...
        /// </summary>
        public ServerCommandRank GetRankForIdentityKey(string identityKey)
        {
            string normalizedKey = NormalizeIdentityKey(identityKey);

            if (string.IsNullOrWhiteSpace(normalizedKey))
                return ServerCommandRank.Default;

            if (_playerRanks.TryGetValue(normalizedKey, out ServerCommandRank rank))
                return rank;

            return ServerCommandRank.Default;
        }

        private static bool ParseBool(string value, bool fallback)
        {
            if (string.IsNullOrWhiteSpace(value))
                return fallback;

            value = value.Trim();

            if (value.Equals("1", StringComparison.OrdinalIgnoreCase) ||
                value.Equals("true", StringComparison.OrdinalIgnoreCase) ||
                value.Equals("yes", StringComparison.OrdinalIgnoreCase) ||
                value.Equals("on", StringComparison.OrdinalIgnoreCase))
                return true;

            if (value.Equals("0", StringComparison.OrdinalIgnoreCase) ||
                value.Equals("false", StringComparison.OrdinalIgnoreCase) ||
                value.Equals("no", StringComparison.OrdinalIgnoreCase) ||
                value.Equals("off", StringComparison.OrdinalIgnoreCase))
                return false;

            return fallback;
        }

        private static string StripComment(string line)
        {
            if (line == null)
                return string.Empty;

            bool inQuotes = false;

            for (int i = 0; i < line.Length; i++)
            {
                char c = line[i];

                if (c == '"')
                    inQuotes = !inQuotes;

                if (!inQuotes && (c == '#' || c == ';'))
                    return line.Substring(0, i);
            }

            return line;
        }
    }
}
