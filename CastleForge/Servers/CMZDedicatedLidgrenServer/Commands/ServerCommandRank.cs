/*
SPDX-License-Identifier: GPL-3.0-or-later
Copyright (c) 2025 RussDev7, unknowghost0
This file is part of https://github.com/RussDev7/CMZDedicatedServers - see LICENSE for details.
*/

using System;

namespace CMZDedicatedLidgrenServer.Commands
{
    /// <summary>
    /// Permission tiers used by in-game dedicated-server commands.
    /// Higher numeric values inherit access to lower-rank commands.
    /// </summary>
    internal enum ServerCommandRank
    {
        Default = 0,
        Member = 10,
        Moderator = 50,
        Admin = 100
    }

    /// <summary>
    /// Shared helpers for parsing, normalizing, and formatting command ranks.
    /// </summary>
    internal static class ServerCommandRankHelper
    {
        /// <summary>
        /// Attempts to parse a rank name from config or chat.
        /// Accepts "Defualt" as a typo-compatible alias for "Default".
        /// </summary>
        public static bool TryParse(string value, out ServerCommandRank rank)
        {
            rank = ServerCommandRank.Default;

            if (string.IsNullOrWhiteSpace(value))
                return false;

            string normalized = value.Trim();

            if (string.Equals(normalized, "Defualt", StringComparison.OrdinalIgnoreCase))
            {
                rank = ServerCommandRank.Default;
                return true;
            }

            if (string.Equals(normalized, "Operator", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(normalized, "Op", StringComparison.OrdinalIgnoreCase))
            {
                rank = ServerCommandRank.Admin;
                return true;
            }

            return Enum.TryParse(normalized, true, out rank);
        }

        /// <summary>
        /// Returns true when <paramref name="actual"/> is high enough for <paramref name="required"/>.
        /// </summary>
        public static bool HasAccess(ServerCommandRank actual, ServerCommandRank required)
        {
            return (int)actual >= (int)required;
        }

        /// <summary>
        /// Converts a rank to the exact config spelling this server writes.
        /// </summary>
        public static string ToConfigString(ServerCommandRank rank)
        {
            return rank switch
            {
                ServerCommandRank.Member => "Member",
                ServerCommandRank.Moderator => "Moderator",
                ServerCommandRank.Admin => "Admin",
                _ => "Default",
            };
        }
    }
}
