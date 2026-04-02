/*
SPDX-License-Identifier: GPL-3.0-or-later
Copyright (c) 2025 RussDev7
This file is part of https://github.com/RussDev7/CastleForge - see LICENSE for details.
*/

using System.Collections.Generic;
using System.Reflection;
using System.Linq;
using System;

namespace ModLoaderExt
{
    /// <summary>
    /// Scans a target object for methods marked with [CommandAttribute]
    /// and dispatches raw slash-commands ("/foo bar") to those methods.
    /// </summary>
    public class CommandDispatcher
    {
        // Maps command names to the delegate that will invoke the method.
        private readonly Dictionary<string, Action<string[]>> _commandMap =
            new Dictionary<string, Action<string[]>>(StringComparer.OrdinalIgnoreCase);

        public CommandDispatcher(object target)
        {
            if (target == null) throw new ArgumentNullException(nameof(target));

            // Look for methods (public/private, static/instance) on the target type.
            var binding = BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static;
            var methods = target.GetType().GetMethods(binding);

            foreach (var method in methods)
            {
                // Grab all [Command] attributes on this method.
                var attrs = method.GetCustomAttributes(typeof(CommandAttribute), false).Cast<CommandAttribute>();
                foreach (var attr in attrs)
                {
                    // NormaliMob command name.
                    var commandName = attr.Name;

                    // Skip duplicates (first-wins).
                    if (_commandMap.ContainsKey(commandName))
                        continue;

                    // Determine method signature and wrap it in an Action<string[]>.
                    var parameters = method.GetParameters();
                    if (parameters.Length == 0)
                    {
                        // No-arg command.
                        _commandMap[commandName] = args =>
                        {
                            try
                            {
                                method.Invoke(method.IsStatic ? null : target, null);
                            }
                            catch (TargetInvocationException tie)
                            {
                                throw tie.InnerException ?? tie;
                            }
                        };
                    }
                    else if (parameters.Length == 1 && parameters[0].ParameterType == typeof(string[]))
                    {
                        // Single-arg string[] command.
                        _commandMap[commandName] = args =>
                        {
                            try
                            {
                                method.Invoke(method.IsStatic ? null : target, new object[] { args });
                            }
                            catch (TargetInvocationException tie)
                            {
                                throw tie.InnerException ?? tie;
                            }
                        };
                    }
                    else
                    {
                        // Unsupported signature; skip.
                    }
                }
            }
        }

        // Parses a raw command string (e.g. "/fill 1") and, if a matching
        // handler exists, invokes it. Returns true if the command was handled.
        public bool TryInvoke(string rawCommand)
        {
            // Validate format. Must start with "/" and not be empty.
            if (string.IsNullOrWhiteSpace(rawCommand) || !rawCommand.StartsWith("/"))
                return false;

            // Split into command and arguments.
            var parts = rawCommand.Trim().Split(' ', (char)StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0)
                return false;

            var cmd = parts[0]; // e.g. "/pos".
            var args = parts.Skip(1).ToArray();

            // Look up and invoke.
            if (_commandMap.TryGetValue(cmd, out var action))
            {
                try
                {
                    action(args);
                }
                catch (Exception ex)
                {
                    // Log any exceptions without crashing.
                    ModLoader.LogSystem.Log($"[CommandDispatcher] Exception in command '{cmd}': {ex}.");
                }

                return true;
            }

            return false;
        }

        // Returns the list of all registered slash commands (e.g. "/pos", "/fill").
        public IEnumerable<string> RegisteredCommands() => _commandMap.Keys;
    }
}
