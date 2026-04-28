/*
SPDX-License-Identifier: GPL-3.0-or-later
Copyright (c) 2025 RussDev7, unknowghost0
This file is part of https://github.com/RussDev7/CMZDedicatedServers - see LICENSE for details.
*/

using System.Collections.Generic;
using System.Linq;
using System.Text;
using System;

namespace CMZDedicatedLidgrenServer.Commands
{
    /// <summary>
    /// Parses in-game command text while preserving quoted arguments.
    ///
    /// Examples:
    /// !kick Jacob
    /// !kick "Jacob Ladders" Optional reason here
    /// !ban 76561198000000000 Griefing
    /// </summary>
    internal static class ServerCommandParser
    {
        /// <summary>
        /// Parses a raw command string into a command name and argument array.
        /// </summary>
        public static bool TryParse(string rawText, string prefix, out string commandName, out string[] args)
        {
            commandName = null;
            args = [];

            if (string.IsNullOrWhiteSpace(rawText))
                return false;

            prefix = string.IsNullOrEmpty(prefix) ? "!" : prefix;

            string text = rawText.Trim();

            if (!text.StartsWith(prefix, StringComparison.Ordinal))
                return false;

            text = text.Substring(prefix.Length).Trim();

            if (text.Length == 0)
                return false;

            List<string> tokens = Tokenize(text);
            if (tokens.Count == 0)
                return false;

            commandName = NormalizeCommandName(tokens[0]);
            args = [.. tokens.Skip(1)];

            return !string.IsNullOrWhiteSpace(commandName);
        }

        /// <summary>
        /// Splits command text into tokens, keeping quoted names together.
        /// Backslash may escape a quote inside a quoted string.
        /// </summary>
        public static List<string> Tokenize(string text)
        {
            List<string> tokens = [];

            if (string.IsNullOrWhiteSpace(text))
                return tokens;

            StringBuilder current = new();
            bool inQuotes = false;
            bool escaping = false;

            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];

                if (escaping)
                {
                    current.Append(c);
                    escaping = false;
                    continue;
                }

                if (c == '\\' && inQuotes)
                {
                    escaping = true;
                    continue;
                }

                if (c == '"')
                {
                    inQuotes = !inQuotes;
                    continue;
                }

                if (char.IsWhiteSpace(c) && !inQuotes)
                {
                    if (current.Length > 0)
                    {
                        tokens.Add(current.ToString());
                        current.Length = 0;
                    }

                    continue;
                }

                current.Append(c);
            }

            if (current.Length > 0)
                tokens.Add(current.ToString());

            return tokens;
        }

        /// <summary>
        /// Joins arguments starting at <paramref name="startIndex"/> into one reason/message string.
        /// </summary>
        public static string JoinFrom(string[] args, int startIndex)
        {
            if (args == null || args.Length <= startIndex)
                return null;

            return string.Join(" ", args.Skip(startIndex)).Trim();
        }

        /// <summary>
        /// Normalizes a command name for dictionary lookup.
        /// </summary>
        public static string NormalizeCommandName(string commandName)
        {
            if (string.IsNullOrWhiteSpace(commandName))
                return string.Empty;

            commandName = commandName.Trim();

            while (commandName.StartsWith("!", StringComparison.Ordinal) ||
                   commandName.StartsWith("/", StringComparison.Ordinal))
            {
                commandName = commandName.Substring(1);
            }

            return commandName.Trim().ToLowerInvariant();
        }
    }
}
