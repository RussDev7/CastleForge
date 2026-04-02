/*
SPDX-License-Identifier: GPL-3.0-or-later
Copyright (c) 2025 RussDev7
This file is part of https://github.com/RussDev7/CastleForge - see LICENSE for details.
*/

using System.Collections.Generic;
using System.Linq;
using System;

namespace ModLoaderExt
{
    /// <summary>
    /// Central registry of chat commands per mod.
    /// Each mod calls Register(modName, commands) once (e.g., in Start()).
    /// </summary>
    public static class HelpRegistry
    {
        public struct CommandEntry
        {
            public string Command;
            public string Description;

            public CommandEntry(string command, string description)
            {
                Command = command ?? string.Empty;
                Description = description ?? string.Empty;
            }
        }

        private static readonly object _lock = new object();

        // Case-insensitive so example "WorldEdit" and "worldedit" map to one bucket.
        private static readonly Dictionary<string, List<CommandEntry>> _byMod =
            new Dictionary<string, List<CommandEntry>>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Register (or replace) the command list for a given mod.
        /// Safe to call multiple times; last call wins.
        /// </summary>
        public static void Register(string modName, IEnumerable<(string command, string description)> items)
        {
            if (string.IsNullOrWhiteSpace(modName) || items == null)
                return;

            lock (_lock)
            {
                _byMod[modName] = items
                    .Select(t => new CommandEntry(t.command, t.description))
                    .ToList();
            }
        }

        /// <summary>
        /// Snapshot a copy for read-only enumeration.
        /// </summary>
        public static IReadOnlyDictionary<string, IReadOnlyList<CommandEntry>> Snapshot()
        {
            lock (_lock)
            {
                // Deep copy lists so callers can't mutate our internal state.
                var dict = new Dictionary<string, IReadOnlyList<CommandEntry>>(
                    _byMod.Count, StringComparer.OrdinalIgnoreCase);

                foreach (var kv in _byMod)
                    dict[kv.Key] = kv.Value.ToList(); // Clone list.

                return dict;
            }
        }
    }
}
