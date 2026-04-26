/*
SPDX-License-Identifier: GPL-3.0-or-later
Copyright (c) 2025 RussDev7, unknowghost0
This file is part of https://github.com/RussDev7/CMZDedicatedServers - see LICENSE for details.
*/

using System.Reflection;
using System.Threading;
using System.IO;
using System;

namespace CMZDedicatedLidgrenServer
{
    internal static class Program
    {
        #region Entry Point

        /// <summary>
        /// Program entry point for the dedicated CMZ server host.
        ///
        /// Purpose:
        /// - Resolves and loads the game/runtime assemblies from the local "game" folder.
        /// - Applies optional runtime patches before the server starts.
        /// - Loads server configuration from disk.
        /// - Creates and starts the Lidgren-backed server host.
        /// - Runs the fixed-rate update loop until Ctrl+C is pressed.
        ///
        /// Flow:
        /// 1) Validate the expected local game/runtime files.
        /// 2) Register AssemblyResolve so dependent assemblies can be loaded from /game.
        /// 3) Load CastleMinerZ.exe and DNA.Common.dll.
        /// 4) Apply runtime patches.
        /// 5) Load config and print a startup summary.
        /// 6) Construct and start the server.
        /// 7) Enter update loop until shutdown.
        ///
        /// Notes:
        /// - Return codes are intentionally preserved exactly as before.
        /// - No behavior or logic has been changed; this is documentation / organization only.
        /// - The update loop uses TickRateHz from config to derive the sleep interval.
        /// </summary>
        static int Main()
        {
            Console.Title = "CMZ Dedicated Lidgren Server";

            try
            {
                #region Resolve Base Paths

                // Root folder for the current executable.
                string baseDir = AppDomain.CurrentDomain.BaseDirectory;
                ServerConfig config = ServerConfig.Load(baseDir);

                // Expected sub-folder containing CastleMinerZ.exe and companion assemblies.
                string gamePath = string.IsNullOrWhiteSpace(config.GamePath)
                    ? Path.Combine(baseDir, "game")
                    : (Path.IsPathRooted(config.GamePath)
                        ? config.GamePath
                        : Path.GetFullPath(Path.Combine(baseDir, config.GamePath)));

                /// <summary>
                /// Resolves the local support-library folder used for non-game assemblies,
                /// such as Harmony, that do not need to live beside the executable.
                /// </summary>
                string libsPath = Path.Combine(baseDir, "libs");

                #endregion

                #region Validate Required Game Folder / Executable

                if (!Directory.Exists(gamePath))
                {
                    Console.WriteLine("ERROR: Missing game folder / binaries path.");
                    Console.WriteLine("Expected CastleMinerZ.exe under: " + gamePath);
                    return 1;
                }

                string exePath = Path.Combine(gamePath, "CastleMinerZ.exe");
                string commonPath = Path.Combine(gamePath, "DNA.Common.dll");

                if (!File.Exists(exePath))
                {
                    Console.WriteLine("ERROR: Missing CastleMinerZ.exe");
                    return 2;
                }

                /// <summary>
                /// Optional validation for support libraries required by the server host.
                ///
                /// Purpose:
                /// - Fails early with a clear message if Harmony is missing from the libs folder.
                /// </summary>
                string harmonyPath = Path.Combine(libsPath, "0Harmony.dll");
                if (!File.Exists(harmonyPath))
                {
                    Console.WriteLine("ERROR: Missing 0Harmony.dll");
                    Console.WriteLine("Expected: " + harmonyPath);
                    return 3;
                }
                #endregion

                #region Assembly Resolution

                /// <summary>
                /// Resolves missing dependent assemblies from known local folders.
                ///
                /// Purpose:
                /// - Probes the support-library folder first (for things like Harmony).
                /// - Then probes the CastleMiner Z game folder for reflected runtime dependencies.
                ///
                /// Notes:
                /// - First checks for a matching DLL.
                /// - Then checks for a matching EXE assembly in the game folder.
                /// - Returns null when the requested assembly cannot be resolved here.
                /// </summary>
                AppDomain.CurrentDomain.AssemblyResolve += (sender, resolveArgs) =>
                {
                    var asmName = new AssemblyName(resolveArgs.Name);

                    // 1) Support libraries beside the server, but inside /libs.
                    string libsDllPath = Path.Combine(libsPath, asmName.Name + ".dll");
                    if (File.Exists(libsDllPath))
                        return Assembly.LoadFrom(libsDllPath);

                    // 2) Game folder DLLs.
                    string gameDllPath = Path.Combine(gamePath, asmName.Name + ".dll");
                    if (File.Exists(gameDllPath))
                        return Assembly.LoadFrom(gameDllPath);

                    // 3) Game folder EXE assemblies.
                    string gameExeAsmPath = Path.Combine(gamePath, asmName.Name + ".exe");
                    if (File.Exists(gameExeAsmPath))
                        return Assembly.LoadFrom(gameExeAsmPath);

                    return null;
                };
                #endregion

                #region Load Runtime Assemblies

                // Main CastleMinerZ game assembly.
                Assembly gameAsm = Assembly.LoadFrom(exePath);

                // Optional shared/common assembly used by parts of the host/runtime.
                Assembly commonAsm = File.Exists(commonPath) ? Assembly.LoadFrom(commonPath) : null;

                #endregion

                #region Apply Runtime Patches

                // Optional runtime server patches.
                //
                // Notes:
                // - Intentionally executed before server construction/startup.
                // - Preserved exactly as-is.
                ServerPatches.ApplyAllPatches();

                #endregion

                #region Print Startup Summary

                Console.WriteLine("CMZ Dedicated Lidgren Server");
                Console.WriteLine("----------------------------");
                Console.WriteLine($"GameName         : {config.GameName}");
                Console.WriteLine($"NetworkVersion   : {config.NetworkVersion}");
                Console.WriteLine($"Bind             : {config.BindAddress}:{config.Port}");
                Console.WriteLine($"ServerName       : {config.ServerName}");
                Console.WriteLine($"ServerName Tokens: {{day}}, {{day00}}, {{players}}, {{maxplayers}}");
                Console.WriteLine($"MaxPlayers       : {config.MaxPlayers}");
                Console.WriteLine($"SaveOwnerSteamId : {config.SaveOwnerSteamId}");
                Console.WriteLine($"WorldGuid        : {config.WorldGuid}");
                Console.WriteLine($"WorldFolder      : {config.WorldFolder}");
                Console.WriteLine($"WorldPath        : {config.WorldPath}");
                Console.WriteLine($"WorldInfo file   : {Path.Combine(config.WorldPath, "world.info")}");
                Console.WriteLine($"World loaded     : {File.Exists(Path.Combine(config.WorldPath, "world.info"))}");
                Console.WriteLine();

                #endregion

                #region Create Server Instance

                /// <summary>
                /// Construct the dedicated server host.
                ///
                /// Notes:
                /// - All constructor arguments are preserved exactly.
                /// - worldFolder remains nullable when config.WorldFolder is blank/whitespace.
                /// - saveRoot continues to point at the executable base directory.
                /// </summary>
                LidgrenServer server = new(
                    gamePath: gamePath,
                    port: config.Port,
                    maxPlayers: config.MaxPlayers,
                    log: Console.WriteLine,
                    gameAsm: gameAsm,
                    worldFolder: string.IsNullOrWhiteSpace(config.WorldFolder) ? null : config.WorldFolder,
                    saveRoot: baseDir,
                    saveOwnerSteamId: config.SaveOwnerSteamId,
                    bindAddress: config.BindAddress,
                    viewRadiusChunks: config.ViewDistanceChunks,
                    serverName: config.ServerName,
                    gameMode: config.GameMode,
                    pvpState: config.PvpState,
                    difficulty: config.Difficulty,
                    gameName: config.GameName,
                    networkVersion: config.NetworkVersion,
                    logNetworkPackets: config.LogNetworkPackets,
                    logHostMessages: config.LogHostMessages);

                #endregion

                #region Start Server

                server.Start();

                Console.WriteLine();
                Console.WriteLine("Server started.");
                Console.WriteLine($"Local test target: 127.0.0.1:{config.Port}");
                Console.WriteLine("Press Ctrl+C to stop.");

                #endregion

                #region Shutdown Signal Handling

                bool running = true;

                /// <summary>
                /// Ctrl+C handler.
                ///
                /// Notes:
                /// - Cancels default process termination.
                /// - Allows the main loop to exit cleanly and call server.Stop().
                /// </summary>
                Console.CancelKeyPress += (s, e) =>
                {
                    e.Cancel = true;
                    running = false;
                };
                #endregion

                #region Tick Timing

                // Derive loop sleep interval from configured tick rate.
                //
                // Notes:
                // - Preserves the original defensive Math.Max usage.
                // - Prevents division by zero and enforces a minimum 1 ms sleep.
                int sleepMs = Math.Max(1, 1000 / Math.Max(1, config.TickRateHz));

                #endregion

                #region Main Server Loop

                /// <summary>
                /// Main server update loop.
                ///
                /// Purpose:
                /// - Calls server.Update() repeatedly until shutdown is requested.
                /// - Catches per-tick exceptions so a single update failure does not
                ///   terminate the process immediately.
                ///
                /// Notes:
                /// - Thread.Sleep cadence is preserved exactly.
                /// - Any update exception is logged and the loop continues.
                /// </summary>
                while (running)
                {
                    try
                    {
                        server.Update();
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine("[Server] Update error: " + ex);
                    }

                    Thread.Sleep(sleepMs);
                }
                #endregion

                #region Graceful Stop

                server.Stop();

                return 0;

                #endregion
            }
            catch (Exception ex)
            {
                #region Fatal Startup / Runtime Failure

                // Top-level fatal exception handler.
                //
                // Notes:
                // - Preserves existing return code and message format.
                // - Intended to catch anything not handled by the per-tick loop.
                Console.WriteLine("FATAL: " + ex);
                return 99;

                #endregion
            }
        }
        #endregion
    }
}