/*
SPDX-License-Identifier: GPL-3.0-or-later
Copyright (c) 2025 RussDev7, unknowghost0
This file is part of https://github.com/RussDev7/CMZDedicatedServers - see LICENSE for details.
*/

using System;

namespace CMZDedicatedLidgrenServer.Commands
{
    /// <summary>
    /// Runtime context for one in-game server command execution.
    /// Transport-specific code fills this out before dispatching to <see cref="ServerCommandManager"/>.
    /// </summary>
    internal sealed class ServerCommandContext
    {
        /// <summary>
        /// CastleMiner Z gamer id for the command sender.
        /// </summary>
        public byte PlayerId { get; set; }

        /// <summary>
        /// Best known display/gamertag for the command sender.
        /// </summary>
        public string PlayerName { get; set; }

        /// <summary>
        /// Stable transport id when available.
        /// Steam server uses SteamID; Lidgren may leave this as 0.
        /// </summary>
        public ulong RemoteId { get; set; }

        /// <summary>
        /// Persistent permission key for the sender, such as steam:7656119..., name:Player, or ip:127.0.0.1.
        /// </summary>
        public string RemoteKey { get; set; }

        /// <summary>
        /// Optional extra permission keys for the sender, such as an IP key plus a name key.
        /// This lets Lidgren support both name: and ip: entries.
        /// </summary>
        public string[] AdditionalRemoteKeys { get; set; }

        /// <summary>
        /// Chat content after the "Player: " prefix is removed.
        /// Example: !kick "Jacob Ladders" griefing
        /// </summary>
        public string RawText { get; set; }

        /// <summary>
        /// Sends a private server message back to only the command sender.
        /// </summary>
        public Action<string> Reply { get; set; }

        /// <summary>
        /// Sends a server message to every connected player.
        /// </summary>
        public Action<string> Broadcast { get; set; }

        /// <summary>
        /// Writes a message to the dedicated server console/log.
        /// </summary>
        public Action<string> Log { get; set; }

        /// <summary>
        /// Optional rank override for trusted command sources such as the dedicated server console.
        /// When set, permission checks use this rank instead of player rank lookup.
        /// </summary>
        public ServerCommandRank? RankOverride { get; set; }

        /// <summary>
        /// Sends a private response to the command sender, if a reply callback was supplied.
        /// </summary>
        public void SendReply(string message)
        {
            try
            {
                Reply?.Invoke(message ?? string.Empty);
            }
            catch
            {
            }
        }

        /// <summary>
        /// Writes a command-related log line without throwing back into packet handling.
        /// </summary>
        public void WriteLog(string message)
        {
            try
            {
                Log?.Invoke(message ?? string.Empty);
            }
            catch
            {
            }
        }
    }

    /// <summary>
    /// Resolved command target information used by commands such as !op and !deop.
    /// </summary>
    internal sealed class ServerCommandTarget
    {
        /// <summary>
        /// True when the target was resolved from the currently connected player list.
        /// </summary>
        public bool IsOnline { get; set; }

        /// <summary>
        /// CastleMiner Z gamer id for the target, if online.
        /// </summary>
        public byte PlayerId { get; set; }

        /// <summary>
        /// Best known display/gamertag for the target.
        /// </summary>
        public string PlayerName { get; set; }

        /// <summary>
        /// Stable transport id when available.
        /// Steam server uses SteamID; Lidgren may leave this as 0.
        /// </summary>
        public ulong RemoteId { get; set; }

        /// <summary>
        /// Persistent permission key for the target, such as steam:7656119..., name:Player, or ip:127.0.0.1.
        /// </summary>
        public string RemoteKey { get; set; }

        /// <summary>
        /// Optional fallback identity keys for the same target, such as name: or ip: aliases.
        /// </summary>
        public string[] AdditionalRemoteKeys { get; set; }

        /// <summary>
        /// Human-readable label for feedback messages.
        /// </summary>
        public string DisplayName
        {
            get
            {
                if (!string.IsNullOrWhiteSpace(PlayerName))
                    return PlayerName;

                if (!string.IsNullOrWhiteSpace(RemoteKey))
                    return RemoteKey;

                if (RemoteId != 0UL)
                    return RemoteId.ToString();

                return "Unknown";
            }
        }
    }
}
