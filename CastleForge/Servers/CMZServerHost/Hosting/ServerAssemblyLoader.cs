/*
SPDX-License-Identifier: GPL-3.0-or-later
Copyright (c) 2025 RussDev7, unknowghost0
This file is part of https://github.com/RussDev7/CMZDedicatedServer - see LICENSE for details.
*/

#pragma warning disable IDE0130
using System.Reflection;
using System.IO;
using System;

namespace CMZServerHost
{
    #region ServerAssemblyLoader

    /// <summary>
    /// Loads the core CastleMiner Z assemblies used by the dedicated host.
    ///
    /// Purpose:
    /// - Stores the resolved game folder path.
    /// - Loads the main game executable assembly.
    /// - Loads the shared/common DNA assembly when present.
    /// - Hooks AppDomain.AssemblyResolve so dependent assemblies can be resolved
    ///   from the same game folder at runtime.
    ///
    /// Notes:
    /// - "CastleMinerZ.exe" is treated as the primary game assembly.
    /// - "DNA.Common.dll" is optional here and is only loaded if present.
    /// - Any additional dependency resolution is handled through
    ///   CurrentDomain_AssemblyResolve(...).
    /// - This class only loads assemblies; it does not initialize game systems.
    /// </summary>
    /// <remarks>
    /// Creates a new loader for the specified game folder.
    ///
    /// Notes:
    /// - This does not immediately load anything.
    /// - Call Load() to validate paths and load assemblies.
    /// </remarks>
    /// <param name="gamePath">Folder containing the CastleMiner Z binaries.</param>
    internal sealed class ServerAssemblyLoader(string gamePath)
    {
        #region Properties

        /// <summary>
        /// Absolute or relative folder path containing CastleMinerZ.exe and related assemblies.
        /// </summary>
        public string GamePath { get; } = gamePath;

        /// <summary>
        /// Loaded CastleMinerZ.exe assembly.
        /// </summary>
        public Assembly GameAssembly { get; private set; }

        /// <summary>
        /// Loaded DNA.Common.dll assembly, when present.
        /// </summary>
        public Assembly CommonAssembly { get; private set; }

        #endregion

        #region Public Load

        /// <summary>
        /// Validates the game folder and loads the primary assemblies.
        ///
        /// Purpose:
        /// - Ensures the target game folder exists.
        /// - Registers the AssemblyResolve handler so missing dependent assemblies
        ///   can be resolved from the game folder.
        /// - Loads CastleMinerZ.exe.
        /// - Loads DNA.Common.dll if it exists.
        ///
        /// Throws:
        /// - DirectoryNotFoundException when the game folder is missing.
        /// - FileNotFoundException when CastleMinerZ.exe is missing.
        ///
        /// Notes:
        /// - The AssemblyResolve event is attached before loading the main assembly
        ///   so dependent assembly lookups can succeed during load.
        /// - DNA.Common.dll is not required by this method, so absence does not fail load.
        /// </summary>
        public void Load()
        {
            if (!Directory.Exists(GamePath))
                throw new DirectoryNotFoundException("Game folder not found: " + GamePath);

            AppDomain.CurrentDomain.AssemblyResolve += CurrentDomain_AssemblyResolve;

            string gameExe = Path.Combine(GamePath, "CastleMinerZ.exe");
            string commonDll = Path.Combine(GamePath, "DNA.Common.dll");

            if (!File.Exists(gameExe))
                throw new FileNotFoundException("Missing CastleMinerZ.exe", gameExe);

            GameAssembly = Assembly.LoadFrom(gameExe);

            if (File.Exists(commonDll))
                CommonAssembly = Assembly.LoadFrom(commonDll);
        }

        #endregion

        #region Assembly Resolve Handler

        /// <summary>
        /// Resolves missing assemblies by probing the configured game folder.
        ///
        /// Resolution order:
        /// 1. {AssemblyName}.dll
        /// 2. {AssemblyName}.exe
        ///
        /// Notes:
        /// - Uses the simple assembly name extracted from ResolveEventArgs.Name.
        /// - Returns null when no matching file is found, allowing normal resolution
        ///   failure behavior to continue upstream.
        /// - This is intended to support dependency loading for game-side assemblies
        ///   that live beside CastleMinerZ.exe.
        /// </summary>
        /// <param name="sender">Assembly resolution source.</param>
        /// <param name="args">Assembly resolution request details.</param>
        /// <returns>The loaded assembly if found; otherwise null.</returns>
        private Assembly CurrentDomain_AssemblyResolve(object sender, ResolveEventArgs args)
        {
            var asmName = new AssemblyName(args.Name);

            string dllPath = Path.Combine(GamePath, asmName.Name + ".dll");
            if (File.Exists(dllPath))
                return Assembly.LoadFrom(dllPath);

            string exePath = Path.Combine(GamePath, asmName.Name + ".exe");
            if (File.Exists(exePath))
                return Assembly.LoadFrom(exePath);

            return null;
        }

        #endregion
    }
    #endregion
}