/*
SPDX-License-Identifier: GPL-3.0-or-later
Copyright (c) 2025 RussDev7
This file is part of https://github.com/RussDev7/CastleForge - see LICENSE for details.
*/

using static ModLoader.LogSystem;

namespace TooManyItems
{
    /// <summary>
    /// Centralized logger for the TooManyItems mod.
    /// </summary>
    internal static class TMILog
    {
        #region Types

        /// <summary>
        /// Where <see cref="TMILog"/> sends messages:
        ///
        /// <see cref="LoggingType.SendFeedback"/> writes to the in-game chat (visible to the user).
        /// <see cref="LoggingType.Log"/> writes to the mod/system log (quieter, for diagnostics).
        /// <see cref="LoggingType.None"/> discards all messages.
        /// </summary>
        public enum LoggingType { SendFeedback, Log, None }

        #endregion

        #region State

        // Current routing mode. Set once at startup (or via a settings command).
        private static LoggingType _mode = LoggingType.SendFeedback;

        #endregion

        #region Configuration API

        /// <summary>
        /// Sets the logging destination for subsequent messages.
        /// </summary>
        public static void SetLoggingMode(LoggingType mode) => _mode = mode;

        /// <summary>
        /// Returns the current logging destination.
        /// </summary>
        public static LoggingType GetLoggingMode() => _mode;

        #endregion

        #region Write API

        /// <summary>
        /// Sends a message using the current routing mode.
        /// <para>
        /// Use this from anywhere in the mod; it's safe to call even with null/empty strings
        /// (those are ignored).
        /// </para>
        /// </summary>
        public static void SendLog(string message)
        {
            if (string.IsNullOrEmpty(message))
                return; // Ignore empty messages to avoid chat/log spam.

            switch (_mode)
            {
                case LoggingType.SendFeedback:
                    SendFeedback($"TMI: {message}");
                    break;
                case LoggingType.Log:
                    Log($"{message}");
                    break;
                case LoggingType.None:
                default:
                    break;
            }
        }
        #endregion
    }
}
