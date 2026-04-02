/*
SPDX-License-Identifier: GPL-3.0-or-later
Copyright (c) 2025 RussDev7
This file is part of https://github.com/RussDev7/CastleForge - see LICENSE for details.
*/

using System.Collections.Generic;
using System.Reflection;
using System.Linq;
using System;

using static ModLoader.LogSystem;

namespace ModLoaderExt
{
    /// <summary>
    /// Marks a method as a slash command handler.
    /// Supported signatures:
    ///   void Method()
    ///   void Method(string[] args)
    /// </summary>
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = true)]
    public class CommandAttribute : Attribute
    {
        // The slash command name (e.g. "/pos").
        // Leading slash is added automatically if omitted.
        public string Name { get; }

        // Creates a new CommandAttribute, normalizing the name to lowercase and ensuring a leading slash.
        public CommandAttribute(string name) => Name = name.StartsWith("/") ? name.ToLowerInvariant() : "/" + name.ToLowerInvariant();
    }

    // Dispatches raw slash commands (e.g. "/pos 1") to attributed methods on a target object.
    public class ChatSystem
    {
        // Maps command names to the delegate that will invoke the method.
        private readonly Dictionary<string, Action<string[]>> _commandMap =
            new Dictionary<string, Action<string[]>>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Builds the dispatcher by reflecting <paramref name="target"/> for methods decorated with <see cref="CommandAttribute"/>.
        /// </summary>
        /// <param name="target">Instance (or static holder) containing command methods.</param>
        public ChatSystem(object target)
        {
            if (target == null) throw new ArgumentNullException(nameof(target));

            // Look for methods (public/private, static/instance) on the target type.
            var flags   = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
            var methods = target.GetType().GetMethods(flags);

            foreach (var method in methods)
            {
                // Find all CommandAttribute instances on this method.
                var attrs = method.GetCustomAttributes<CommandAttribute>(false);
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
                                // Unwrap.
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
                    Log($"[CommandDispatcher] Exception in command '{cmd}': {ex}");
                }

                return true;
            }

            return false;
        }

        #region Helpers

        // Returns the list of all registered slash commands (e.g. "/pos", "/fill").
        public IEnumerable<string> RegisteredCommands() => _commandMap.Keys;

        #endregion
    }
}
