/*
SPDX-License-Identifier: GPL-3.0-or-later
Copyright (c) 2025 RussDev7, unknowghost0
This file is part of https://github.com/RussDev7/CMZDedicatedServers - see LICENSE for details.
*/

using System.Collections.Generic;
using System.Linq;
using System;

namespace CMZDedicatedLidgrenServer.Commands
{
    /// <summary>
    /// Metadata and handler for one registered in-game server command.
    /// </summary>
    internal sealed class ServerCommandDefinition(
        string name,
        string usage,
        string description,
        ServerCommandRank defaultRequiredRank,
        Action<ServerCommandContext, string[]> handler)
    {
        public string Name { get; } = ServerCommandParser.NormalizeCommandName(name);
        public string Usage { get; } = usage ?? string.Empty;
        public string Description { get; } = description ?? string.Empty;
        public ServerCommandRank DefaultRequiredRank { get; } = defaultRequiredRank;
        public Action<ServerCommandContext, string[]> Handler { get; } = handler ?? throw new ArgumentNullException(nameof(handler));
    }

    /// <summary>
    /// Central in-game server command registry, permission checker, and dispatcher.
    ///
    /// Transport-specific server code only needs to:
    /// - Detect that a chat packet contains a command.
    /// - Create a ServerCommandContext.
    /// - Call TryExecute(...).
    /// </summary>
    internal sealed class ServerCommandManager
    {
        private readonly Dictionary<string, ServerCommandDefinition> _commands =
            new(StringComparer.OrdinalIgnoreCase);

        private readonly ServerCommandPermissionStore _permissions;
        private readonly Action<string> _log;
        private readonly Func<string> _getPlayerListText;
        private readonly Func<string, string, bool> _kickPlayer;
        private readonly Func<string, string, bool> _banPlayer;
        private readonly Func<string, bool> _unbanPlayer;
        private readonly Action _reloadAll;
        private readonly Action _reloadPlugins;
        private readonly Func<string, ServerCommandTarget> _resolveTarget;

        public ServerCommandManager(
            string baseDir,
            Action<string> log,
            Func<string> getPlayerListText,
            Func<string, string, bool> kickPlayer,
            Func<string, string, bool> banPlayer,
            Func<string, bool> unbanPlayer,
            Action reloadAll,
            Action reloadPlugins,
            Func<string, ServerCommandTarget> resolveTarget)
        {
            _log = log ?? (_ => { });
            _getPlayerListText = getPlayerListText;
            _kickPlayer = kickPlayer;
            _banPlayer = banPlayer;
            _unbanPlayer = unbanPlayer;
            _reloadAll = reloadAll;
            _reloadPlugins = reloadPlugins;
            _resolveTarget = resolveTarget;

            _permissions = new ServerCommandPermissionStore(baseDir, _log);

            RegisterBuiltInCommands();
            Reload();
        }

        /// <summary>
        /// True when <paramref name="rawText"/> starts with the current configured command prefix.
        /// </summary>
        public bool IsCommand(string rawText)
        {
            if (string.IsNullOrWhiteSpace(rawText))
                return false;

            if (!_permissions.Enabled)
                return false;

            string prefix = string.IsNullOrEmpty(_permissions.Prefix) ? "!" : _permissions.Prefix;
            return rawText.TrimStart().StartsWith(prefix, StringComparison.Ordinal);
        }

        /// <summary>
        /// Returns true when the target is protected from moderation actions such as kick and ban.
        /// Currently, Admin-ranked / operator players are protected.
        /// </summary>
        private bool IsProtectedModerationTarget(ServerCommandTarget target)
        {
            if (target == null)
                return false;

            if (_permissions.GetRankForIdentityKey(target.RemoteKey) >= ServerCommandRank.Admin)
                return true;

            if (target.AdditionalRemoteKeys != null)
            {
                foreach (string key in target.AdditionalRemoteKeys)
                {
                    if (_permissions.GetRankForIdentityKey(key) >= ServerCommandRank.Admin)
                        return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Reloads command permissions and player ranks from disk.
        /// </summary>
        public void Reload()
        {
            _permissions.Reload();
            _permissions.EnsureCommandDefaults(_commands.Values);
            _log("[Commands] Reloaded command permissions and player ranks.");
        }

        /// <summary>
        /// Registers a command. Existing command names are replaced so future plugins/systems can override safely.
        /// </summary>
        public void Register(
            string name,
            string usage,
            string description,
            ServerCommandRank defaultRequiredRank,
            Action<ServerCommandContext, string[]> handler)
        {
            ServerCommandDefinition command = new(
                name,
                usage,
                description,
                defaultRequiredRank,
                handler);

            _commands[command.Name] = command;
        }

        /// <summary>
        /// Executes a command from chat.
        /// Returns true if the chat message should be consumed and not relayed publicly.
        /// </summary>
        public bool TryExecute(ServerCommandContext context)
        {
            if (context == null || !_permissions.Enabled)
                return false;

            if (!ServerCommandParser.TryParse(context.RawText, _permissions.Prefix, out string commandName, out string[] args))
                return false;

            if (!_commands.TryGetValue(commandName, out ServerCommandDefinition command))
            {
                context.SendReply("[Server] Unknown command. Use " + _permissions.Prefix + "help.");
                return true;
            }

            ServerCommandRank senderRank = _permissions.GetPlayerRank(context);
            ServerCommandRank requiredRank = _permissions.GetRequiredRank(command.Name, command.DefaultRequiredRank);

            if (!ServerCommandRankHelper.HasAccess(senderRank, requiredRank))
            {
                context.SendReply(
                    "[Server] You need " +
                    ServerCommandRankHelper.ToConfigString(requiredRank) +
                    " to use " +
                    _permissions.Prefix +
                    command.Name +
                    ".");

                context.WriteLog(
                    "[Commands] Denied " +
                    _permissions.Prefix +
                    command.Name +
                    " from " +
                    SafeName(context.PlayerName, context.PlayerId) +
                    " (" +
                    (context.RemoteKey ?? "unknown") +
                    "), rank=" +
                    ServerCommandRankHelper.ToConfigString(senderRank) +
                    ", required=" +
                    ServerCommandRankHelper.ToConfigString(requiredRank) +
                    ".");

                return true;
            }

            try
            {
                command.Handler(context, args);
            }
            catch (Exception ex)
            {
                context.SendReply("[Server] Command failed: " + ex.Message);
                context.WriteLog("[Commands] Exception while executing " + _permissions.Prefix + command.Name + ": " + ex);
            }

            return true;
        }

        /// <summary>
        /// Executes a server command from the dedicated server console.
        /// Console commands do not require the in-game prefix and always run as Admin.
        /// </summary>
        public bool TryExecuteConsole(string rawText, Action<string> reply, Action<string> broadcast)
        {
            if (string.IsNullOrWhiteSpace(rawText))
                return false;

            string commandText = rawText.Trim();
            string prefix = string.IsNullOrEmpty(_permissions.Prefix) ? "!" : _permissions.Prefix;

            // Console can type either:
            // op Jacob
            // or:
            // !op Jacob
            if (!commandText.StartsWith(prefix, StringComparison.Ordinal))
                commandText = prefix + commandText;

            return TryExecute(new ServerCommandContext
            {
                PlayerId = 0,
                PlayerName = "Console",
                RemoteId = 0UL,
                RemoteKey = "console",
                RawText = commandText,
                RankOverride = ServerCommandRank.Admin,
                Reply = message =>
                {
                    reply?.Invoke(message);
                },
                Broadcast = broadcast,
                Log = _log
            });
        }

        private void RegisterBuiltInCommands()
        {
            Register(
                "help",
                "help [page]",
                "Shows available commands.",
                ServerCommandRank.Default,
                HandleHelp);

            Register(
                "players",
                "players",
                "Lists connected players.",
                ServerCommandRank.Moderator,
                HandlePlayers);

            Register(
                "kick",
                "kick <player|id> [reason]",
                "Drops a non-operator player from the server.",
                ServerCommandRank.Moderator,
                HandleKick);

            Register(
                "ban",
                "ban <player|id|steamid> [reason]",
                "Bans and drops a non-operator player from the server.",
                ServerCommandRank.Admin,
                HandleBan);

            Register(
                "unban",
                "unban <player|steamid|ip>",
                "Removes a persisted ban.",
                ServerCommandRank.Admin,
                HandleUnban);

            Register(
                "reload",
                "reload [all|plugins|commands]",
                "Reloads server config, plugins, or command files.",
                ServerCommandRank.Admin,
                HandleReload);

            Register(
                "op",
                "op <player|steamid|key>",
                "Gives a player Admin command access.",
                ServerCommandRank.Admin,
                HandleOp);

            Register(
                "deop",
                "deop <player|steamid|key>",
                "Removes a player's explicit command rank.",
                ServerCommandRank.Admin,
                HandleDeop);

            Register(
                "setrank",
                "setrank <player|steamid|key> <Default|Member|Moderator|Admin>",
                "Sets a player's command rank.",
                ServerCommandRank.Admin,
                HandleSetRank);
        }

        #region Built-in Command Handlers

        private void HandleHelp(ServerCommandContext context, string[] args)
        {
            int page = 1;

            if (args != null && args.Length > 0)
                int.TryParse(args[0], out page);

            if (page < 1)
                page = 1;

            ServerCommandRank senderRank = _permissions.GetPlayerRank(context);

            List<ServerCommandDefinition> visible = [.. _commands.Values
                .Where(c => ServerCommandRankHelper.HasAccess(
                    senderRank,
                    _permissions.GetRequiredRank(c.Name, c.DefaultRequiredRank)))
                .OrderBy(c => c.Name)];

            int maxLines = Math.Max(1, _permissions.HelpMaxLines);
            int commandsPerPage = Math.Max(1, maxLines - 1);
            int totalPages = Math.Max(1, (int)Math.Ceiling(visible.Count / (double)commandsPerPage));

            if (page > totalPages)
                page = totalPages;

            context.SendReply("[Commands] Page " + page + "/" + totalPages + " (" + _permissions.Prefix + "help <page>)");

            foreach (ServerCommandDefinition command in visible.Skip((page - 1) * commandsPerPage).Take(commandsPerPage))
            {
                string required = ServerCommandRankHelper.ToConfigString(
                    _permissions.GetRequiredRank(command.Name, command.DefaultRequiredRank));

                context.SendReply(
                    _permissions.Prefix +
                    command.Usage +
                    " - " +
                    command.Description +
                    " [" +
                    required +
                    "]");
            }
        }

        private void HandlePlayers(ServerCommandContext context, string[] args)
        {
            string text = _getPlayerListText != null
                ? _getPlayerListText()
                : "Player list is not available.";

            SendMultiline(context, text);
        }

        private void HandleKick(ServerCommandContext context, string[] args)
        {
            if (args == null || args.Length < 1)
            {
                context.SendReply("[Server] Usage: " + _permissions.Prefix + "kick <player|id> [reason]");
                return;
            }

            string target = args[0];
            string reason = ServerCommandParser.JoinFrom(args, 1);

            ServerCommandTarget resolvedTarget = ResolveModerationTarget(target);

            if (IsProtectedModerationTarget(resolvedTarget))
            {
                context.SendReply("[Server] You cannot kick an operator/admin player.");
                context.WriteLog(
                    "[Commands] Blocked kick against protected target " +
                    (resolvedTarget != null ? resolvedTarget.DisplayName : target) +
                    " by " +
                    SafeName(context.PlayerName, context.PlayerId) +
                    ".");

                return;
            }

            bool ok = _kickPlayer != null && _kickPlayer(target, reason);

            context.SendReply(ok
                ? "[Server] Kicked " + target + "."
                : "[Server] Could not find player to kick: " + target);
        }

        private void HandleBan(ServerCommandContext context, string[] args)
        {
            if (args == null || args.Length < 1)
            {
                context.SendReply("[Server] Usage: " + _permissions.Prefix + "ban <player|id|steamid> [reason]");
                return;
            }

            string target = args[0];
            string reason = ServerCommandParser.JoinFrom(args, 1);

            ServerCommandTarget resolvedTarget = ResolveModerationTarget(target);

            if (IsProtectedModerationTarget(resolvedTarget))
            {
                context.SendReply("[Server] You cannot ban an operator/admin player.");
                context.WriteLog(
                    "[Commands] Blocked ban against protected target " +
                    (resolvedTarget != null ? resolvedTarget.DisplayName : target) +
                    " by " +
                    SafeName(context.PlayerName, context.PlayerId) +
                    ".");

                return;
            }

            bool ok = _banPlayer != null && _banPlayer(target, reason);

            context.SendReply(ok
                ? "[Server] Banned " + target + "."
                : "[Server] Could not find player to ban: " + target);
        }

        private void HandleUnban(ServerCommandContext context, string[] args)
        {
            if (args == null || args.Length < 1)
            {
                context.SendReply("[Server] Usage: " + _permissions.Prefix + "unban <player|steamid|ip>");
                return;
            }

            string target = args[0];
            bool ok = _unbanPlayer != null && _unbanPlayer(target);

            context.SendReply(ok
                ? "[Server] Unbanned " + target + "."
                : "[Server] No ban matched: " + target);
        }

        private void HandleReload(ServerCommandContext context, string[] args)
        {
            string scope = args != null && args.Length > 0
                ? args[0].Trim().ToLowerInvariant()
                : "all";

            if (scope == "commands" || scope == "command")
            {
                Reload();
                context.SendReply("[Server] Reloaded command files.");
                return;
            }

            if (scope == "plugins" || scope == "plugin")
            {
                if (_reloadPlugins != null)
                {
                    _reloadPlugins();
                    context.SendReply("[Server] Reloaded plugins.");
                }
                else
                {
                    context.SendReply("[Server] Plugin reload is not available.");
                }

                return;
            }

            if (scope == "all" || scope == "server" || scope == "config")
            {
                if (_reloadAll != null)
                {
                    _reloadAll();
                    context.SendReply("[Server] Reloaded server config, plugins, and commands.");
                }
                else
                {
                    Reload();
                    context.SendReply("[Server] Reloaded command files.");
                }

                return;
            }

            context.SendReply("[Server] Usage: " + _permissions.Prefix + "reload [all|plugins|commands]");
        }

        private void HandleOp(ServerCommandContext context, string[] args)
        {
            if (args == null || args.Length < 1)
            {
                context.SendReply("[Server] Usage: " + _permissions.Prefix + "op <player|steamid|key>");
                return;
            }

            ServerCommandTarget target = ResolveRankTarget(args[0]);
            if (target == null || string.IsNullOrWhiteSpace(target.RemoteKey))
            {
                context.SendReply("[Server] Could not resolve target: " + args[0]);
                return;
            }

            _permissions.SetPlayerRank(target.RemoteKey, ServerCommandRank.Admin);
            context.SendReply("[Server] Opped " + target.DisplayName + " as Admin.");
            context.WriteLog("[Commands] " + SafeName(context.PlayerName, context.PlayerId) + " opped " + target.DisplayName + " (" + target.RemoteKey + ").");
        }

        private void HandleDeop(ServerCommandContext context, string[] args)
        {
            if (args == null || args.Length < 1)
            {
                context.SendReply("[Server] Usage: " + _permissions.Prefix + "deop <player|steamid|key>");
                return;
            }

            ServerCommandTarget target = ResolveRankTarget(args[0]);
            string key = target != null && !string.IsNullOrWhiteSpace(target.RemoteKey)
                ? target.RemoteKey
                : ServerCommandPermissionStore.BuildIdentityKeyFromRawTarget(args[0]);

            bool removed = _permissions.RemovePlayerRank(key);

            context.SendReply(removed
                ? "[Server] Removed explicit rank for " + (target != null ? target.DisplayName : key) + "."
                : "[Server] No explicit rank existed for " + (target != null ? target.DisplayName : key) + ".");
        }

        private void HandleSetRank(ServerCommandContext context, string[] args)
        {
            if (args == null || args.Length < 2)
            {
                context.SendReply("[Server] Usage: " + _permissions.Prefix + "setrank <player|steamid|key> <Default|Member|Moderator|Admin>");
                return;
            }

            if (!ServerCommandRankHelper.TryParse(args[1], out ServerCommandRank rank))
            {
                context.SendReply("[Server] Unknown rank: " + args[1]);
                return;
            }

            ServerCommandTarget target = ResolveRankTarget(args[0]);
            string key = target != null && !string.IsNullOrWhiteSpace(target.RemoteKey)
                ? target.RemoteKey
                : ServerCommandPermissionStore.BuildIdentityKeyFromRawTarget(args[0]);

            if (string.IsNullOrWhiteSpace(key))
            {
                context.SendReply("[Server] Could not resolve target: " + args[0]);
                return;
            }

            if (rank == ServerCommandRank.Default)
                _permissions.RemovePlayerRank(key);
            else
                _permissions.SetPlayerRank(key, rank);

            context.SendReply(
                "[Server] Set " +
                (target != null ? target.DisplayName : key) +
                " to " +
                ServerCommandRankHelper.ToConfigString(rank) +
                ".");
        }

        #endregion

        #region Helpers

        private ServerCommandTarget ResolveRankTarget(string rawTarget)
        {
            ServerCommandTarget target = null;

            try
            {
                if (_resolveTarget != null)
                    target = _resolveTarget(rawTarget);
            }
            catch
            {
                target = null;
            }

            if (target != null && !string.IsNullOrWhiteSpace(target.RemoteKey))
                return target;

            string key = ServerCommandPermissionStore.BuildIdentityKeyFromRawTarget(rawTarget);
            if (string.IsNullOrWhiteSpace(key))
                return target;

            return new ServerCommandTarget
            {
                IsOnline = false,
                PlayerName = rawTarget,
                RemoteKey = key
            };
        }

        private static void SendMultiline(ServerCommandContext context, string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                context.SendReply(string.Empty);
                return;
            }

            string[] lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
            foreach (string line in lines)
                context.SendReply(line);
        }

        private static string SafeName(string name, byte playerId)
        {
            return string.IsNullOrWhiteSpace(name) ? "Player" + playerId : name;
        }

        /// <summary>
        /// Resolves a kick/ban target so command-rank protection can be checked before moderation actions.
        /// Online players are resolved through the transport-specific target resolver.
        /// Raw identity keys such as steam:..., name:..., ip:..., or bare SteamIDs are also supported.
        /// </summary>
        private ServerCommandTarget ResolveModerationTarget(string rawTarget)
        {
            ServerCommandTarget target = null;

            try
            {
                if (_resolveTarget != null)
                    target = _resolveTarget(rawTarget);
            }
            catch
            {
                target = null;
            }

            if (target != null && !string.IsNullOrWhiteSpace(target.RemoteKey))
                return target;

            string key = ServerCommandPermissionStore.BuildIdentityKeyFromRawTarget(rawTarget);
            if (string.IsNullOrWhiteSpace(key))
                return target;

            return new ServerCommandTarget
            {
                IsOnline = false,
                PlayerName = rawTarget,
                RemoteKey = key
            };
        }
        #endregion
    }
}
