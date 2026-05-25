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
    /// !kick "Jacob Smith" Optional reason here
    /// !ban 76561198000000000 Griefing
    /// </summary>
    internal static class ServerCommandParser
    {
        /// <summary>
        /// Parses a raw command string into a command name and argument array.
        /// </summary>
        public static bool TryParse(string rawText, string prefix, out string commandName, out string[] args)
        {
            return TryParse(rawText, [prefix], out _, out commandName, out args);
        }

        /// <summary>
        /// Parses a raw command string using any accepted command prefix.
        /// </summary>
        public static bool TryParse(
            string rawText,
            IEnumerable<string> prefixes,
            out string usedPrefix,
            out string commandName,
            out string[] args)
        {
            usedPrefix = null;
            commandName = null;
            args = [];

            if (string.IsNullOrWhiteSpace(rawText))
                return false;

            string text = rawText.Trim();

            List<string> validPrefixes = NormalizePrefixes(prefixes);

            foreach (string prefix in validPrefixes)
            {
                if (!text.StartsWith(prefix, StringComparison.Ordinal))
                    continue;

                usedPrefix = prefix;
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

            return false;
        }

        /// <summary>
        /// Normalizes configured prefixes and checks longer prefixes first.
        /// This prevents "!" from stealing commands intended for "!!".
        /// </summary>
        private static List<string> NormalizePrefixes(IEnumerable<string> prefixes)
        {
            List<string> result = [];

            if (prefixes != null)
            {
                foreach (string rawPrefix in prefixes)
                {
                    string prefix = rawPrefix == null ? string.Empty : rawPrefix.Trim();

                    if (prefix.Length == 0)
                        continue;

                    if (!result.Contains(prefix))
                        result.Add(prefix);
                }
            }

            if (result.Count == 0)
                result.Add("!");

            return [.. result.OrderByDescending(prefix => prefix.Length)];
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
