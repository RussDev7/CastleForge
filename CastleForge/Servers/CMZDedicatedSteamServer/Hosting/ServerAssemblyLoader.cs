/*
SPDX-License-Identifier: GPL-3.0-or-later
Copyright (c) 2025 RussDev7, unknowghost0
This file is part of https://github.com/RussDev7/CMZDedicatedServers - see LICENSE for details.
*/

using System.Reflection;
using System.IO;
using System;

namespace CMZDedicatedSteamServer.Hosting
{
    /// <summary>
    /// Loads the CastleMiner Z, DNA.Common, and DNA.Steam assemblies used by the dedicated Steam host.
    /// </summary>
    internal sealed class ServerAssemblyLoader(string gamePath)
    {
        public string GamePath { get; } = gamePath;
        public Assembly GameAssembly { get; private set; }
        public Assembly CommonAssembly { get; private set; }
        public Assembly SteamAssembly { get; private set; }

        public void Load()
        {
            if (!Directory.Exists(GamePath))
                throw new DirectoryNotFoundException("Game folder not found: " + GamePath);

            AppDomain.CurrentDomain.AssemblyResolve += CurrentDomain_AssemblyResolve;

            string gameExe = Path.Combine(GamePath, "CastleMinerZ.exe");
            string commonDll = Path.Combine(GamePath, "DNA.Common.dll");
            string steamDll = Path.Combine(GamePath, "DNA.Steam.dll");

            if (!File.Exists(gameExe))
                throw new FileNotFoundException("Missing CastleMinerZ.exe", gameExe);
            if (!File.Exists(commonDll))
                throw new FileNotFoundException("Missing DNA.Common.dll", commonDll);
            if (!File.Exists(steamDll))
                throw new FileNotFoundException("Missing DNA.Steam.dll", steamDll);

            GameAssembly = Assembly.LoadFrom(gameExe);
            CommonAssembly = Assembly.LoadFrom(commonDll);
            SteamAssembly = Assembly.LoadFrom(steamDll);
        }

        private Assembly CurrentDomain_AssemblyResolve(object sender, ResolveEventArgs args)
        {
            AssemblyName asmName = new(args.Name);

            string dllPath = Path.Combine(GamePath, asmName.Name + ".dll");
            if (File.Exists(dllPath))
                return Assembly.LoadFrom(dllPath);

            string exePath = Path.Combine(GamePath, asmName.Name + ".exe");
            return File.Exists(exePath) ? Assembly.LoadFrom(exePath) : null;
        }
    }
}
