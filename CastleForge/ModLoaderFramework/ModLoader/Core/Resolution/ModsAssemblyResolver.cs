/*
SPDX-License-Identifier: GPL-3.0-or-later
Copyright (c) 2025 RussDev7
This file is part of https://github.com/RussDev7/CastleForge - see LICENSE for details.
*/

using System.Reflection;
using System.Linq;
using System.IO;
using System;

namespace ModLoader
{
    /// <summary>
    /// Lightweight, loader-side AssemblyResolve hook that helps the CLR locate dependency
    /// assemblies that live directly inside the game's "!Mods" directory. This exists to
    /// prevent "0 mods loaded" situations when the exe.config <probing> entry is not present.
    /// </summary>
    internal static class ModsAssemblyResolver
    {
        // Whether we've already installed the resolver hook (one-time install).
        private static bool   _installed;

        // Cached path to the "!Mods" directory used for direct-path probing.
        private static string _modsDir;

        /// <summary>
        /// Installs the AppDomain.AssemblyResolve handler once.
        ///
        /// Call this BEFORE ModManager.LoadMods(...) so it is active during the initial
        /// reflection/discovery phase (where missing dependencies most commonly surface).
        /// </summary>
        public static void Install(string modsDir)
        {
            // One-time registration guard.
            if (_installed) return;
            _installed = true;

            // Cache the mods directory so the resolver can construct candidate paths.
            _modsDir = modsDir;

            // AssemblyResolve fires when the CLR fails to find a referenced assembly through
            // normal resolution mechanisms (app base, GAC, load context, etc.).
            AppDomain.CurrentDomain.AssemblyResolve += (s, e) =>
            {
                try
                {
                    // Parse the requested assembly identity and extract its simple name.
                    var req        = new AssemblyName(e.Name);
                    var simpleName = req.Name;
                    if (string.IsNullOrWhiteSpace(simpleName)) return null;

                    // Reuse already-loaded assemblies (prevents duplicate loads of the same name).
                    var already = AppDomain.CurrentDomain.GetAssemblies()
                        .FirstOrDefault(a => string.Equals(a.GetName().Name, simpleName, StringComparison.OrdinalIgnoreCase));
                    if (already != null) return already;

                    // Only check TOP-LEVEL !Mods, no subfolders, no scanning.
                    // This keeps behavior deterministic and avoids "hidden" dependency pickup.
                    var candidate = Path.Combine(_modsDir, simpleName + ".dll");
                    if (File.Exists(candidate))
                        return Assembly.LoadFrom(candidate);
                }
                catch
                {
                    // Never throw from resolver.
                    // If anything goes wrong, return null so CLR continues its normal resolution flow.
                }

                return null;
            };
        }
    }
}