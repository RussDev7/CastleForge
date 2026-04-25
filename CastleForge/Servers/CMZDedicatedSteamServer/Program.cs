/*
SPDX-License-Identifier: GPL-3.0-or-later
Copyright (c) 2025 RussDev7, unknowghost0
This file is part of https://github.com/RussDev7/CMZDedicatedServers - see LICENSE for details.
*/

using CMZDedicatedSteamServer.Config;
using CMZDedicatedSteamServer.Hosting;
using System;
using System.IO;
using System.Reflection;
using System.Threading;

namespace CMZDedicatedSteamServer
{
    /// <summary>
    /// Console entry point for the Steam-native CastleMiner Z dedicated server.
    /// </summary>
    /// <remarks>
    /// This host starts the Steam dedicated server runtime, applies optional runtime patches,
    /// loads configuration, and runs the main server update loop until shutdown.
    /// </remarks>
    internal static class Program
    {
        #region Entry Point

        /// <summary>
        /// Program entry point for the Steam-native dedicated server host.
        /// </summary>
        /// <returns>
        /// 0 when the server exits normally; 1 when a fatal startup/runtime exception reaches Main.
        /// </returns>
        /// <remarks>
        /// Purpose:
        /// - Loads server configuration from disk.
        /// - Resolves the CastleMiner Z game/runtime assemblies.
        /// - Initializes the Steam API against the currently logged-in Steam client.
        /// - Creates a Steam lobby owned by that active account.
        /// - Enters the main update loop for packet processing and future world ticks.
        ///
        /// Notes:
        /// - This host intentionally does NOT launch the game window.
        /// - This host also does NOT support username/password Steam login from config.
        ///   Steam's normal client API path requires an already-running Steam client.
        /// </remarks>
        private static int Main()
        {
            Console.Title = "CMZ Dedicated Steam Server";

            try
            {
                #region Resolve Base Paths

                string baseDir = AppDomain.CurrentDomain.BaseDirectory;
                SteamServerConfig config = SteamServerConfig.Load(baseDir);

                string gamePath = string.IsNullOrWhiteSpace(config.GamePath)
                    ? Path.Combine(baseDir, "Game")
                    : (Path.IsPathRooted(config.GamePath)
                        ? config.GamePath
                        : Path.GetFullPath(Path.Combine(baseDir, config.GamePath)));

                string libsPath = Path.Combine(baseDir, "Libs");

                #endregion

                #region Assembly Resolution

                AppDomain.CurrentDomain.AssemblyResolve += (sender, resolveArgs) =>
                {
                    var asmName = new AssemblyName(resolveArgs.Name);

                    // 1) Support libraries beside the server, inside /Libs.
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

                #region Apply Runtime Patches

                ServerPatches.ApplyAllPatches();

                #endregion

                using var server = new SteamDedicatedServer(baseDir, config, Log);

                Console.CancelKeyPress += (_, e) =>
                {
                    e.Cancel = true;
                    Log("Ctrl+C received. Shutting down...");
                    server.Stop();
                };

                server.Start();

                int sleepMs = Math.Max(1, 1000 / Math.Max(1, config.TickRateHz));

                while (server.IsRunning)
                {
                    try
                    {
                        server.Update();
                    }
                    catch (Exception ex)
                    {
                        Log("[MainLoop] " + ex);
                    }

                    Thread.Sleep(sleepMs);
                }

                return 0;
            }
            catch (Exception ex)
            {
                Log("FATAL: " + ex);
                return 1;
            }
        }
        #endregion

        #region Logging

        /// <summary>
        /// Writes a timestamped server message to the console.
        /// </summary>
        /// <param name="message">Message to write.</param>
        private static void Log(string message)
        {
            Console.WriteLine($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}");
        }
        #endregion
    }
}